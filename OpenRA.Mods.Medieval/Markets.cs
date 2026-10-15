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
	public static class MedievalAccess
	{
		public static IEnumerable<CPos> Perimeter(Actor target)
		{
			var size = target.Info.TraitInfoOrDefault<BuildingInfo>()?.Dimensions ?? new CVec(1, 1);
			var origin = target.Location;
			if (target.TraitOrDefault<MedievalSiegeEngine>() is { Info.Trebuchet: true, State: > 0 }) { size = new CVec(3, 3); origin -= new CVec(1, 1); }
			for (var y = -1; y <= size.Y; y++)
				for (var x = -1; x <= size.X; x++)
					if (x == -1 || y == -1 || x == size.X || y == size.Y) yield return origin + new CVec(x, y);
		}
		public static bool Adjacent(Actor unit, Actor target) => Perimeter(target).Contains(unit.Location);
	}

	public class MedievalFoodProviderInfo : TraitInfo, Requires<MedievalStockpileInfo>
	{
		public readonly WDist Radius = new(16384);
		public override object Create(ActorInitializer init) => new MedievalFoodProvider(this);
	}
	public class MedievalFoodProvider : IRenderAnnotations
	{
		public readonly MedievalFoodProviderInfo Info;
		public MedievalFoodProvider(MedievalFoodProviderInfo info) { Info = info; }
		public static IEnumerable<IRenderable> Ring(WPos center, WDist radius, Color color)
		{
			const int segments = 64;
			for (var i = 0; i < segments; i++)
			{
				WPos Point(int n) => center + new WVec((int)(Math.Cos(n * Math.PI * 2 / segments) * radius.Length),
					(int)(Math.Sin(n * Math.PI * 2 / segments) * radius.Length), 0);
				yield return new LineAnnotationRenderable(Point(i), Point(i + 1), 2, color);
			}
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || self.World.FogObscures(self)) yield break;
			var stocked = MedievalStockpile.Active(self) && self.Trait<MedievalStockpile>().Stock.Available((int)Material.Food) > 0;
			foreach (var segment in Ring(self.CenterPosition, Info.Radius, stocked ? Color.LightGreen : Color.OrangeRed)) yield return segment;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}

	public class MedievalMarketInfo : TraitInfo, Requires<MedievalBuildingInfo>, Requires<MedievalFoodProviderInfo>
	{
		public readonly int CartTrainingTicks = 100;
		public override object Create(ActorInitializer init) => new MedievalMarket(this);
	}
	public class MedievalMarket : ITick, IResolveOrder, INotifyOwnerChanged, ISync
	{
		readonly MedievalMarketInfo info;
		[VerifySync] int training;
		[VerifySync] bool recruitingTrader;
		[VerifySync] public bool Recruiting { get; private set; }
		public string Status { get; private set; } = "Selectionnez ce marche pour creer une charrette.";
		public MedievalMarket(MedievalMarketInfo info) { this.info = info; }
		public static IEnumerable<Actor> Markets(Player p) => p.World.ActorsHavingTrait<MedievalMarket>().Where(a => a.Owner == p && MedievalStockpile.Active(a)
			&& a.TraitOrDefault<MedievalMarket>() != null).OrderBy(a => a.ActorID);
		public bool CanRecruit(Actor self, bool trader = false) => MedievalStockpile.Active(self) && !Recruiting
			&& MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) < MedievalSettlement.Housing(self.Owner)
			&& MedievalSettlement.Cost(self.Owner, trader ? "md.tradecart" : "md.supplycart").CanPay(MedievalSettlement.RecruitmentStores(self.Owner).Select(a => a.Trait<MedievalStockpile>().Stock));
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "MedievalCreateCart" && order.OrderString != "MedievalCreateTrader") return;
			var trader = order.OrderString == "MedievalCreateTrader";
			if (!CanRecruit(self, trader)) { Status = "Marché terminé, nourriture et logement requis."; return; }
			if (!MedievalSettlement.Cost(self.Owner, trader ? "md.tradecart" : "md.supplycart").TryPay(MedievalSettlement.RecruitmentStores(self.Owner).Select(a => a.Trait<MedievalStockpile>().Stock))) return;
			recruitingTrader = trader;
			Recruiting = true; training = info.CartTrainingTicks; Status = "Preparation d'une charrette et de son porteur.";
		}
		void ITick.Tick(Actor self)
		{
			if (!Recruiting || !MedievalStockpile.Active(self) || --training > 0) return;
			if (MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) > MedievalSettlement.Housing(self.Owner))
			{ training = 25; Status = "Charrette en attente de logements."; return; }
			var cell = MedievalAccess.Perimeter(self).Where(c => self.World.Map.Contains(c) && !self.World.ActorMap.GetActorsAt(c).Any()
				&& new[] { "Clear", "Road", "Rough" }.Contains(self.World.Map.GetTerrainInfo(c).Type)).OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => (CPos?)c).FirstOrDefault();
			if (cell == null) { training = 25; Status = "Sortie du marche bloquee."; return; }
			training = 25;
			var owner = self.Owner;
			self.World.AddFrameEndTask(w =>
			{
				if (!Recruiting || !MedievalStockpile.Active(self) || self.WillDispose || self.Owner != owner || w.ActorMap.GetActorsAt(cell.Value).Any()
					|| MedievalSettlement.Population(owner) + MedievalSettlement.PendingPopulation(owner) > MedievalSettlement.Housing(owner)) return;
				var cart = w.CreateActor(recruitingTrader ? "md.tradecart" : "md.supplycart", [new OwnerInit(owner), new LocationInit(cell.Value)]);
				if (recruitingTrader) cart.Trait<MedievalTradeCart>().Assign(cart, self);
				Recruiting = false;
				Status = recruitingTrader ? cart.Trait<MedievalTradeCart>().Active ? "Charrette marchande créée et affectée à cette route." : "Charrette marchande disponible pour un échange ou une route." : "Charrette creee : chargez-la ou affectez-la a un chantier.";
			});
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Recruiting = false; }
	}

	public class MedievalSupplyCartInfo : TraitInfo, Requires<MobileInfo>, Requires<MedievalStockpileInfo>, Requires<MedievalFoodProviderInfo>
	{
		public readonly int LoadAmount = 20;
		public readonly int LoadInterval = 25;
		public override object Create(ActorInitializer init) => new MedievalSupplyCart(this);
	}
	public class MedievalSupplyCart : ITick, IResolveOrder, INotifyOwnerChanged, ISync
	{
		readonly MedievalSupplyCartInfo info;
		Actor reloadTarget;
		[VerifySync] int cadence;
		[VerifySync] int failedMoves;
		[VerifySync] int loadMask = 1 << (int)Material.Food;
		[VerifySync] public int ReloadTarget => unchecked((int)(reloadTarget?.ActorID ?? 0));
		public string Status { get; private set; } = "Choisissez le chargement dans la zone d'un marche.";
		public MedievalSupplyCart(MedievalSupplyCartInfo info) { this.info = info; }
		public bool Wants(int resource) => resource >= 0 && resource < Inventory.ResourceCount && (loadMask & (1 << resource)) != 0;
		public static bool InMarketZone(Actor self) => MedievalMarket.Markets(self.Owner).Any(a => MedievalFoodEconomy.Station(a).Covers(self.CenterPosition));
		public bool AcceptsDelivery(Actor self, int resource) => MedievalStockpile.Active(self) && self.IsIdle && reloadTarget == null
			&& !MedievalManualTransfer.Manual(self) && Wants(resource) && InMarketZone(self)
			&& (self.TraitOrDefault<MedievalConstructionConvoy>() is not { Active: true } convoy || convoy.CanLoad(self));
		public int DeliveryFree(Actor self, int resource)
		{
			var stock = self.Trait<MedievalStockpile>().Stock;
			return self.TraitOrDefault<MedievalConstructionConvoy>() is { Active: true } convoy
				? Math.Max(0, Math.Min(stock.Free(resource), convoy.Desired(self, resource) - stock.Amount(resource) - stock.Incoming(resource))) : stock.Free(resource);
		}
		public void PrepareConvoy() { reloadTarget = null; failedMoves = 0; loadMask = (1 << 0) | (1 << 1) | (1 << 3) | (1 << 4); }
		public string LoadingDescription => "Charger : " + string.Join(", ", new[] { 1, 0, 3, 4 }.Where(Wants).Select(r => MedievalStockDisplay.Labels[r]));
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalCartCargo")
			{
				if (self.TraitOrDefault<MedievalConstructionConvoy>()?.Active == true) return;
				if (order.ExtraData != 0 && order.ExtraData != 1 && order.ExtraData != 3 && order.ExtraData != 4) return;
				if (!InMarketZone(self)) { Status = "Revenez dans la zone d'un marche pour choisir le chargement."; return; }
				loadMask ^= 1 << (int)order.ExtraData;
				Status = "Chargement modifie. Les ressources deja chargees sont conservees.";
			}
			else if (order.OrderString == "MedievalReloadCart")
			{
				reloadTarget = MedievalMarket.Markets(self.Owner).OrderBy(a => a.Trait<MedievalStockpile>().Stock.Available(1) > 0 ? 0 : 1)
					.ThenBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
				self.CancelActivity(); failedMoves = 0;
				Status = reloadTarget == null ? "Aucun marche termine." : "Retour au marche pour recharger.";
			}
			else if (order.OrderString == "Move" || order.OrderString == "Stop") { reloadTarget = null; failedMoves = 0; }
		}
		void ITick.Tick(Actor self)
		{
			if (self.IsDead || MedievalManualTransfer.Manual(self) || --cadence > 0) return;
			cadence = info.LoadInterval;
			if (!self.IsIdle) return;
			if (self.TraitOrDefault<MedievalConstructionConvoy>() is { Active: true } convoy && !convoy.CanLoad(self)) return;
			if (reloadTarget != null)
			{
				if (!MedievalStockpile.Active(reloadTarget) || reloadTarget.Owner != self.Owner) reloadTarget = null;
				else if (MedievalAccess.Adjacent(self, reloadTarget)) { reloadTarget = null; failedMoves = 0; }
				else
				{
					var mobile = self.Trait<Mobile>();
					var cells = MedievalAccess.Perimeter(reloadTarget).Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.Immovable))
						.OrderBy(c => (c - self.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).ToArray();
					if (failedMoves >= cells.Length) { reloadTarget = null; Status = "Acces au marche bloque."; return; }
					self.QueueActivity(mobile.MoveTo(cells[failedMoves++])); return;
				}
			}
			var stock = self.Trait<MedievalStockpile>().Stock;
			foreach (var market in MedievalMarket.Markets(self.Owner).Where(a => MedievalAccess.Adjacent(self, a)))
			{
				if (!Wants((int)Material.Food)) break;
				var source = market.Trait<MedievalStockpile>().Stock;
				var amount = Math.Min(info.LoadAmount, Math.Min(source.Available(1), DeliveryFree(self, 1)));
				if (amount <= 0) continue;
				stock.Add(1, source.Take(1, amount));
				Status = "Nourriture chargee au marche. Materiaux apportes par transporteurs.";
				break;
			}
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { reloadTarget = null; failedMoves = 0; }
	}
}
