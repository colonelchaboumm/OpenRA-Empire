using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalStoneCartInfo : TraitInfo, Requires<MedievalStockpileInfo>, Requires<MobileInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalStoneCart();
	}
	public sealed class MedievalStoneCart : ITick, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, IRenderAnnotations, ISync
	{
		Actor target, source;
		TradeShipment shipment;
		[VerifySync] int cadence, attempts;
		[VerifySync] bool recovering;
		[VerifySync] public bool Active { get; private set; }
		[VerifySync] public int Delivered { get; private set; }
		[VerifySync] public int TargetId => unchecked((int)(target?.ActorID ?? 0));
		[VerifySync] public int SourceId => unchecked((int)(source?.ActorID ?? 0));
		[VerifySync] public int ShipmentHash => shipment?.Hash ?? 0;
		public string Status { get; private set; } = "Choisissez un trebuchet a ravitailler.";
		public bool HasTarget(Actor self) => SiegeRules.Live(target, self.Owner) && target.TraitOrDefault<MedievalSiegeEngine>()?.Info.Trebuchet == true;
		public static IEnumerable<Actor> Stores(Player p) => MedievalSettlement.Warehouses(p).Concat(MedievalMarket.Markets(p)).Concat(MedievalMarketStall.Stalls(p)).OrderBy(a => a.ActorID);
		public string Destination(Actor self) => HasTarget(self) ? $"Trebuchet #{target.ActorID}" : "Aucune affectation";
		void Pause(Actor self, string status, bool cancelActivity = true)
		{
			shipment?.Cancel(); shipment = null; source = null; attempts = 0; cadence = 0;
			Active = false; if (cancelActivity) self.CancelActivity(); Status = status;
		}
		public bool Assign(Actor self, Actor engine)
		{
			if (!SiegeRules.Live(engine, self.Owner) || engine.TraitOrDefault<MedievalSiegeEngine>()?.Info.Trebuchet != true) return false;
			Pause(self, "Rejoint le stock de pierres le plus proche."); target = engine; recovering = false; Active = true; return true;
		}
		public void LostEngine(Actor self, Actor engine)
		{
			if (target != engine || self.Owner == engine.Owner) return;
			Pause(self, "Trebuchet capture : pierres conservees, nouvelle affectation requise."); target = null;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalSiegeSupply" && order.Target.Type == TargetType.Actor) Assign(self, order.Target.Actor);
			else if (order.OrderString == "MedievalStonePause")
			{
				if (Active) Pause(self, "Ravitaillement suspendu. Pierres conservees.");
				else if (HasTarget(self)) Assign(self, target);
			}
			else if (order.OrderString is "Move" or "Stop" or "Scatter") Pause(self, "Ordre manuel. Pierres conservees : reprendre ou rapatrier.", false);
			else if (order.OrderString == "MedievalStoneRecover")
			{
				Pause(self, "Rapatrie les pierres au stockage.");
				target = Stores(self.Owner).Where(a => a.Trait<MedievalStockpile>().Stock.Free(3) > 0)
					.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
				recovering = true; Active = target != null;
				if (!Active) Status = "Aucun stockage disponible. Pierres conservees.";
			}
		}
		void ITick.Tick(Actor self)
		{
			if (!Active || --cadence > 0) return; cadence = 12;
			if (!SiegeRules.Live(target, self.Owner) || !recovering && !HasTarget(self)) { Pause(self, "Destination perdue : pierres conservees, choisissez un autre trebuchet."); return; }
			if (source != null && !SiegeRules.Live(source, self.Owner)) { source = null; self.CancelActivity(); }
			if (!self.IsIdle) return;
			var cargo = self.Trait<MedievalStockpile>().Stock;
			if (cargo.Amount(3) > 0)
			{
				if (shipment == null) { shipment = new TradeShipment(cargo, target.Trait<MedievalStockpile>().Stock); shipment.BookCarriedCargo(); }
				if (shipment.Total == 0) { shipment.Cancel(); shipment = null; Status = "Destination pleine : attend avec ses pierres."; return; }
				if (!MedievalLogistics.Approach(self, target, ref attempts)) { Status = "Transporte les pierres vers " + (recovering ? "le stockage." : Destination(self) + "."); return; }
				Delivered += shipment.Unload(); shipment = null; source = null; attempts = 0;
				if (cargo.Amount(3) > 0) return;
			}
			if (recovering) { Pause(self, "Pierres rapatriees au stockage."); return; }
			if (target.Trait<MedievalStockpile>().Stock.Free(3) == 0) { Status = "Trebuchet ravitaille : attend ses prochains tirs."; return; }
			if (source == null || source.Trait<MedievalStockpile>().Stock.Available(3) == 0)
			{
				source = Stores(self.Owner).Where(a => a.Trait<MedievalStockpile>().Stock.Available(3) > 0)
					.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault(); attempts = 0;
			}
			if (source == null) { Status = "Aucune pierre disponible dans les depots et marches."; return; }
			if (!MedievalLogistics.Approach(self, source, ref attempts)) { Status = "Va chercher des pierres au stockage."; return; }
			shipment = new TradeShipment(cargo, target.Trait<MedievalStockpile>().Stock);
			shipment.Load(source.Trait<MedievalStockpile>().Stock, 8, 0);
			if (shipment.Total == 0) { shipment.Cancel(); shipment = null; }
			source = null; attempts = 0; Status = "Pierres chargees : depart vers le trebuchet.";
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { shipment?.Cancel(); shipment = null; }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Pause(self, "Nouvelle affectation requise."); target = null; }
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (self.Owner == self.World.LocalPlayer && self.World.Selection.Contains(self) && HasTarget(self))
				foreach (var line in MedievalTradeMarket.Connection(self, target)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
