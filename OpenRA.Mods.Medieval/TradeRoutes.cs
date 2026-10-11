using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalLogistics
	{
		public static bool Local(Actor source, Actor destination, int radius = 20) =>
			(source.CenterPosition - destination.CenterPosition).HorizontalLengthSquared <= (long)radius * radius * 1024 * 1024;
		public static string Name(Actor market) => market == null ? "Aucun" : MedievalLocalities.MarketName(market);
		public static bool Market(Actor a, Player owner) => MedievalStockpile.Active(a) && a.Owner == owner && a.TraitOrDefault<MedievalTradeMarket>() != null;
		public static bool Approach(Actor self, Actor target, ref int attempts)
		{
			if (MedievalAccess.Adjacent(self, target)) { attempts = 0; return true; }
			var mobile = self.Trait<Mobile>();
			var cells = MedievalAccess.Perimeter(target).Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.Immovable))
				.OrderBy(c => (c - self.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).ToArray();
			if (attempts >= cells.Length) { attempts = 0; return false; }
			self.QueueActivity(mobile.MoveTo(cells[attempts++]));
			return false;
		}
	}

	// Destination space is booked before departure; cargo becomes usable only on arrival.
	public sealed class TradeShipment
	{
		readonly Inventory cargo;
		readonly Inventory destination;
		readonly int[] booked = new int[Inventory.ResourceCount];
		public int Total => booked.Sum();
		public int Hash => booked.Aggregate(17, (hash, n) => unchecked(hash * 31 + n));
		public TradeShipment(Inventory cargo, Inventory destination) { this.cargo = cargo; this.destination = destination; }
		public void Load(Inventory source, int mask, int foodReserve)
		{
			for (var r = 0; r < Inventory.ResourceCount; r++)
			{
				if ((mask & (1 << r)) == 0) continue;
				var n = Math.Min(cargo.Free(r), Math.Min(destination.Free(r), Math.Max(0, source.Available(r) - (r == 1 ? foodReserve : 0))));
				if (n <= 0 || !destination.ReserveIn(r, n)) continue;
				cargo.Add(r, source.Take(r, n)); booked[r] += n;
			}
		}
		public void BookCarriedCargo()
		{
			for (var r = 0; r < Inventory.ResourceCount; r++)
			{
				var n = Math.Min(cargo.Available(r) - booked[r], destination.Free(r));
				if (n > 0 && destination.ReserveIn(r, n)) booked[r] += n;
			}
		}
		public int Unload()
		{
			var delivered = 0;
			for (var r = 0; r < Inventory.ResourceCount; r++)
			{
				if (booked[r] == 0) continue;
				var n = cargo.Take(r, booked[r]);
				destination.Unload(r, n);
				delivered += n;
				if (n < booked[r]) destination.ReleaseIn(r, booked[r] - n);
				booked[r] = 0;
			}
			return delivered;
		}
		public void Cancel()
		{
			for (var r = 0; r < Inventory.ResourceCount; r++)
			{
				if (booked[r] > 0) destination.ReleaseIn(r, booked[r]);
				booked[r] = 0;
			}
		}
	}

	public class MedievalTradeMarketInfo : TraitInfo, Requires<MedievalMarketInfo>
	{
		public readonly int FoodReserve = 40;
		public override object Create(ActorInitializer init) => new MedievalTradeMarket(this);
	}
	public sealed class MedievalTradeMarket : IResolveOrder, INotifyOwnerChanged, INotifyRemovedFromWorld, IRenderAnnotations, ISync
	{
		public readonly MedievalTradeMarketInfo Info;
		public Actor Partner { get; private set; }
		[VerifySync] public int PartnerId => unchecked((int)(Partner?.ActorID ?? 0));
		[VerifySync] public int ExportMask { get; private set; }
		public MedievalTradeMarket(MedievalTradeMarketInfo info) { Info = info; }
		public bool Linked(Actor self) => MedievalLogistics.Market(self, self.Owner) && MedievalLogistics.Market(Partner, self.Owner)
			&& Partner.Trait<MedievalTradeMarket>().Partner == self;
		public bool Exports(int r) => r >= 0 && r < Inventory.ResourceCount && (ExportMask & (1 << r)) != 0;
		public string ExportDescription => ExportMask == 0 ? "Rien" : string.Join(", ", Enumerable.Range(0, Inventory.ResourceCount).Where(Exports).Select(r => MedievalStockDisplay.Labels[r]));
		void Detach(Actor self)
		{
			if (Partner?.IsInWorld == true && Partner.TraitOrDefault<MedievalTradeMarket>() is MedievalTradeMarket peer && peer.Partner == self)
				peer.Partner = null;
			Partner = null;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalUnlinkMarkets") { Detach(self); return; }
			if (!MedievalLogistics.Market(self, self.Owner)) return;
			if (order.OrderString == "MedievalLinkMarkets" && order.Target.Type == TargetType.Actor)
			{
				var other = order.Target.Actor;
				if (other == self || !MedievalLogistics.Market(other, self.Owner)) return;
				var peer = other.Trait<MedievalTradeMarket>();
				Detach(self); peer.Detach(other);
				Partner = other; peer.Partner = self;
				ExportMask = (1 << 1) | (1 << 0); peer.ExportMask = (1 << 2) | (1 << 3);
			}
			else if (order.OrderString == "MedievalTradeExport" && order.ExtraData < Inventory.ResourceCount && Linked(self))
			{
				var bit = 1 << (int)order.ExtraData;
				ExportMask ^= bit;
				// One direction per resource prevents an immediate return of the same goods.
				if ((ExportMask & bit) != 0) Partner.Trait<MedievalTradeMarket>().ExportMask &= ~bit;
			}
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) => Detach(self);
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) => Detach(self);
		internal static IEnumerable<IRenderable> Connection(Actor first, Actor last)
		{
			for (var i = 0; i < 40; i += 2)
				yield return new LineAnnotationRenderable(WPos.Lerp(first.CenterPosition, last.CenterPosition, i, 40),
					WPos.Lerp(first.CenterPosition, last.CenterPosition, i + 1, 40), 2, Color.Goldenrod);
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (self.Owner != self.World.LocalPlayer || !Linked(self) || !self.World.Selection.Contains(self)) yield break;
			foreach (var line in Connection(self, Partner)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}

	public class MedievalTradeCartInfo : TraitInfo, Requires<MedievalStockpileInfo>, Requires<MobileInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalTradeCart();
	}
	public sealed class MedievalTradeCart : ITick, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, IRenderAnnotations, ISync
	{
		Actor first;
		Actor second;
		Actor target;
		TradeShipment shipment;
		[VerifySync] int cadence;
		[VerifySync] int attempts;
		[VerifySync] bool recovering;
		[VerifySync] public bool Active { get; private set; }
		[VerifySync] public int Delivered { get; private set; }
		[VerifySync] public int FirstId => unchecked((int)(first?.ActorID ?? 0));
		[VerifySync] public int SecondId => unchecked((int)(second?.ActorID ?? 0));
		[VerifySync] public int TargetId => unchecked((int)(target?.ActorID ?? 0));
		[VerifySync] public int ShipmentHash => shipment?.Hash ?? 0;
		public string Status { get; private set; } = "Affectez cette charrette a un marche relie.";
		public bool ValidRoute(Actor self) => MedievalLogistics.Market(first, self.Owner) && MedievalLogistics.Market(second, self.Owner)
			&& first.Trait<MedievalTradeMarket>().Partner == second && second.Trait<MedievalTradeMarket>().Partner == first;
		public bool Serves(Actor market) => market == first || market == second;
		public string RouteName(Actor self) => ValidRoute(self) ? MedievalLogistics.Name(first) + " <-> " + MedievalLogistics.Name(second) : "Route interrompue ou non affectee.";
		public void Assign(Actor self, Actor origin)
		{
			if (!MedievalLogistics.Market(origin, self.Owner) || !origin.Trait<MedievalTradeMarket>().Linked(origin)) return;
			Pause(self, "");
			first = origin; second = origin.Trait<MedievalTradeMarket>().Partner;
			target = first; Active = true; recovering = false; cadence = 0;
			Status = "Rejoint " + MedievalLogistics.Name(target);
		}
		void Pause(Actor self, string status, bool cancelMovement = true)
		{
			shipment?.Cancel(); shipment = null;
			Active = false; attempts = 0; recovering = false;
			if (cancelMovement) self.CancelActivity();
			Status = status;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalAssignTrader" && order.Target.Type == TargetType.Actor) Assign(self, order.Target.Actor);
			else if (order.OrderString is "Move" or "Stop" or "MedievalPauseTrade") Pause(self, "Route en pause. Cargaison conservee.", order.OrderString == "MedievalPauseTrade");
			else if (order.OrderString == "MedievalResumeTrade" && ValidRoute(self))
			{
				Active = true; recovering = false; cadence = 0;
				if (!MedievalLogistics.Market(target, self.Owner)) target = first;
			}
			else if (order.OrderString == "MedievalRecoverTrade")
			{
				Pause(self, "");
				target = MedievalMarket.Markets(self.Owner).OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
				Active = target != null; recovering = true; cadence = 0;
				Status = target == null ? "Aucun marche pour decharger. Cargaison conservee." : "Rapatrie sa cargaison au marche.";
			}
		}
		void ITick.Tick(Actor self)
		{
			if (!Active || --cadence > 0) return;
			cadence = 25;
			if (!MedievalLogistics.Market(target, self.Owner) || !recovering && !ValidRoute(self))
			{ Pause(self, "Liaison interrompue. Cargaison conservee : rapatriez-la."); return; }
			if (!self.IsIdle) return;
			var cargo = self.Trait<MedievalStockpile>().Stock;
			if (cargo.Available(0) + cargo.Available(1) + cargo.Available(2) + cargo.Available(3) > 0 && shipment == null)
			{
				shipment = new TradeShipment(cargo, target.Trait<MedievalStockpile>().Stock);
				shipment.BookCarriedCargo();
			}
			if (!MedievalLogistics.Approach(self, target, ref attempts)) { Status = "En route vers " + MedievalLogistics.Name(target) + " (attend si passage bloque)"; return; }
			if (shipment != null) { Delivered += shipment.Unload(); shipment = null; }
			if (Enumerable.Range(0, Inventory.ResourceCount).Any(r => cargo.Amount(r) > 0)) { Status = "Marche plein : attend de la place pour decharger."; return; }
			if (recovering) { Pause(self, "Cargaison rapatriee. Choisissez une route pour repartir."); return; }
			var next = target == first ? second : first;
			var origin = target.Trait<MedievalTradeMarket>();
			var load = new TradeShipment(cargo, next.Trait<MedievalStockpile>().Stock);
			load.Load(target.Trait<MedievalStockpile>().Stock, origin.ExportMask, origin.Info.FoodReserve);
			if (load.Total > 0)
			{
				shipment = load; target = next; attempts = 0;
				Status = "Marchandises chargees : " + MedievalLogistics.Name(next);
			}
			else
			{
				var peer = next.Trait<MedievalTradeMarket>();
				var stock = next.Trait<MedievalStockpile>().Stock;
				if (Enumerable.Range(0, Inventory.ResourceCount).Any(r => peer.Exports(r) && stock.Available(r) > (r == 1 ? peer.Info.FoodReserve : 0)
					&& target.Trait<MedievalStockpile>().Stock.Free(r) > 0)) { target = next; attempts = 0; }
				Status = "Attend des marchandises ou de la place a destination.";
			}
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { shipment?.Cancel(); shipment = null; }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) => Pause(self, "Nouvel ordre commercial requis.");
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (self.Owner != self.World.LocalPlayer || !self.World.Selection.Contains(self) || !ValidRoute(self)) yield break;
			foreach (var line in MedievalTradeMarket.Connection(first, second)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
