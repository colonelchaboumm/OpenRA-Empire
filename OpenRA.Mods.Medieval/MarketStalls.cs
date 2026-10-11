using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class StallDemand
	{
		public static int Food(double mealsPerMinute, int capacity) => mealsPerMinute <= 0 ? 0 : Math.Min(capacity, Math.Max(8, (int)Math.Ceiling(mealsPerMinute * 2)));
		public static int Request(int target, Inventory stock, int resource) => Math.Max(0, Math.Min(stock.Free(resource), target - stock.Amount(resource) - stock.Incoming(resource)));
		public static int Workshop(int required, Inventory inputs, int resource) => Math.Max(0, required - inputs.Amount(resource) - inputs.Incoming(resource));
	}
	public class MedievalMarketStallInfo : TraitInfo, Requires<MedievalStockpileInfo>, Requires<MedievalFoodProviderInfo>, Requires<MedievalConstructionSupplyInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalMarketStall();
	}
	public sealed class MedievalMarketStall : ITick, IResolveOrder, INotifyOwnerChanged, IRenderAnnotations, ISync
	{
		public Actor Market { get; private set; }
		[VerifySync] public int MarketId => unchecked((int)(Market?.ActorID ?? 0));
		[VerifySync] int cadence;
		public bool Linked(Actor self) => MedievalStockpile.Active(self) && MedievalStockpile.Active(Market) && Market.Owner == self.Owner && Market.TraitOrDefault<MedievalMarket>() != null;
		public static IEnumerable<Actor> Stalls(Player p) => p.World.ActorsWithTrait<MedievalMarketStall>().Select(a => a.Actor)
			.Where(a => a.Owner == p && MedievalStockpile.Active(a)).OrderBy(a => a.ActorID);
		public int Desired(Actor self, int resource)
		{
			if (!MedievalStockpile.Active(self)) return 0;
			var stock = self.Trait<MedievalStockpile>().Stock;
			if (resource == (int)Material.Food)
			{
				var station = MedievalFoodEconomy.Station(self);
				var demand = MedievalFoodEconomy.Citizens(self.Owner).Where(a => station.Covers(a.CenterPosition)).Sum(a => a.Trait<MedievalNeedsFood>().Need.DemandPerMinute);
				return StallDemand.Food(demand, stock.Capacity(resource));
			}
			if (resource != (int)Material.Wood && resource != (int)Material.Stone && resource != (int)Material.Gold) return 0;
			var construction = MedievalConstructionSupply.Demand(self, resource);
			var forging = resource == (int)Material.Gold ? 0 : MedievalEquipment.Workshops(self.Owner).Sum(a =>
			{
				var workshop = a.Trait<MedievalMilitaryWorkshop>();
				return workshop.Info.Forge && workshop.Staff(a) != null && workshop.SupplyStall(a) == self
					? StallDemand.Workshop(workshop.Required(a)[resource], workshop.Inputs, resource) : 0;
			});
			return Math.Min(stock.Capacity(resource), construction + forging);
		}
		public int Request(Actor self, int resource) => Linked(self) ? StallDemand.Request(Desired(self, resource), self.Trait<MedievalStockpile>().Stock, resource) : 0;
		public static int MarketTarget(Actor market, int resource, IEnumerable<Actor> constructionSites = null) => Math.Min(market.Trait<MedievalStockpile>().Stock.Capacity(resource),
			Stalls(market.Owner).Where(a => a.Trait<MedievalMarketStall>().Market == market).Sum(a => a.Trait<MedievalMarketStall>().Request(a, resource))
			+ (resource is 0 or 3 or 4 ? MedievalConstructionSupply.Demand(market, resource, constructionSites) : 0));
		public string Description(Actor self) => !Linked(self) ? "Etal sans grand marche termine : liaison automatique en attente."
			: $"{MedievalLogistics.Name(self)} <- {MedievalLogistics.Name(Market)} | Besoins : {Desired(self, 1)} nourriture, {Desired(self, 0)} bois, {Desired(self, 3)} pierre";
		void ITick.Tick(Actor self)
		{
			if (--cadence > 0) return;
			cadence = 25;
			if (!MedievalStockpile.Active(self)) return;
			if (!Linked(self)) Market = MedievalMarket.Markets(self.Owner).OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalLinkStall" && order.Target.Type == TargetType.Actor && MedievalStockpile.Active(self))
			{
				var market = order.Target.Actor;
				if (MedievalStockpile.Active(market) && market.Owner == self.Owner && market.TraitOrDefault<MedievalMarket>() != null) Market = market;
			}
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Market = null; cadence = 0; }
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (self.Owner != self.World.LocalPlayer || !self.World.Selection.Contains(self) || self.World.FogObscures(self) || !Linked(self)) yield break;
			foreach (var line in MedievalTradeMarket.Connection(self, Market)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
