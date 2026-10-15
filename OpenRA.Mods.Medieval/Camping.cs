using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalCampCartInfo : TraitInfo, Requires<MedievalSupplyCartInfo>, Requires<MedievalCampStoreInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalCampCart();
	}
	public sealed class MedievalCampCart : IResolveOrder, ISync
	{
		[VerifySync] public bool LoadTents;
		public string Status { get; private set; } = "Placez la charrette pres du campement.";
		public bool AcceptsTents(Actor self) => LoadTents && !MedievalManualTransfer.Manual(self) && MedievalStockpile.Active(self) && self.IsIdle
			&& MedievalSupplyCart.InMarketZone(self) && self.TraitOrDefault<MedievalConstructionConvoy>()?.Active != true;
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalLoadTents" && MedievalSupplyCart.InMarketZone(self))
			{
				self.TraitOrDefault<MedievalManualTransfer>()?.ResumeAutomatic(self);
				LoadTents = !LoadTents;
			}
		}
	}
	public class MedievalPackTentInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalPackTent();
	}
	public sealed class MedievalPackTent : IResolveOrder
	{
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "MedievalPackTent") return;
			self.World.AddFrameEndTask(w =>
			{
				if (!MedievalStockpile.Active(self) || self.WillDispose) return;
				var cart = w.ActorsHavingTrait<MedievalCampCart>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a.IsIdle
					&& MedievalAccess.Adjacent(a, self) && a.Trait<MedievalCampStore>().Stock.Free(1) > 0).OrderBy(a => a.ActorID).FirstOrDefault();
				if (cart == null) return;
				var occupants = w.ActorsHavingTrait<MedievalRest>().Where(a => a.Trait<MedievalRest>().Shelter == self).ToArray();
				foreach (var a in occupants) a.Trait<MedievalRest>().Wake(a);
				if (occupants.Any(a => a.Trait<MedievalRest>().Shelter == self)) return;
				cart.Trait<MedievalCampStore>().Stock.Add(1, 1); self.Dispose();
			});
		}
	}
}
