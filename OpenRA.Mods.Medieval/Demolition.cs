using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalDemolitionOrdersInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDemolitionOrders();
	}
	public sealed class MedievalDemolitionOrders : IResolveOrder, ISync
	{
		readonly int[] remainder = new int[StorageGoods.Count];
		[VerifySync] public int RemainderHash => remainder.Aggregate(17, (h, n) => unchecked(h * 31 + n));
		// Carry fractions between jobs: five 3-stone paving tiles recover exactly
		// three stones, rather than rounding every tiny tile to zero or up to one.
		public int Recover(int resource, int spent)
		{
			var total = spent + remainder[resource]; remainder[resource] = total % 5; return total / 5;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "MedievalDemolish" || order.Target.Type != TargetType.Actor || self.World.Paused) return;
			Request(self, order.Target.Actor, order.ExtraData == 1);
		}
		public static void Request(Actor player, Actor target, bool cancel = false)
		{
			if (player.World.Paused || !MedievalDemolition.Owned(target, player.Owner)) return;
			var job = target.Trait<MedievalDemolition>();
			player.Trait<MedievalSettlement>().Notify(cancel ? job.Cancel() : job.Request(target));
		}
	}
	public class MedievalDemolitionInfo : TraitInfo, Requires<MedievalBuildingInfo>, Requires<BuildingInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalDemolition();
	}
	public sealed class MedievalDemolition : INotifyCreated, INotifyOwnerChanged, ISync, IRenderAnnotations
	{
		MedievalBuilding building;
		[VerifySync] public int Progress { get; private set; }
		[VerifySync] bool finishing;
		public bool Requested => building.Demolishing;
		public int WorkRequired => Math.Max(4, building.Info.Work / 2);
		public string Description => $"Demolition : {Progress * 100 / WorkRequired}% | Transporteurs et batisseurs | 20% recuperes. Recliquez avec Demolir pour annuler.";
		void INotifyCreated.Created(Actor self) { building = self.Trait<MedievalBuilding>(); }
		public static bool Pending(Actor a) => a is { IsInWorld: true, IsDead: false } && a.TraitOrDefault<MedievalBuilding>()?.Demolishing == true;
		public static bool Owned(Actor a, Player p) => a is { IsInWorld: true, IsDead: false, WillDispose: false } && a.Owner == p && a.TraitOrDefault<MedievalDemolition>() != null;
		public string Request(Actor self)
		{
			if (Requested) return "Ce demontage est deja programme.";
			if (self.TraitOrDefault<MedievalBurnable>()?.Burning == true) return "Eteignez l'incendie avant de demonter ce batiment.";
			building.Demolishing = true; Progress = 0;
			return "Demolition programmee : transporteurs et batisseurs viennent demonter puis rapporter les materiaux. Recuperation : 20%.";
		}
		public string Cancel()
		{
			if (finishing) return "Le demontage est termine.";
			building.Demolishing = false; Progress = 0;
			return "Demolition annulee. Aucun materiau n'a ete cree.";
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{ finishing = false; Cancel(); }
		public void Work(Actor self, Actor builder)
		{
			if (!Requested || finishing || self.IsDead || !self.IsInWorld || builder.Owner != self.Owner || !StorageHandling.AtBuilding(builder, self)) return;
			// Unload a garrison through its normal activity, including blocked exits.
			// Sleeping citizens wake through the existing inactive-shelter checks.
			if (self.TraitOrDefault<Cargo>() is { } cargo && !cargo.IsEmpty())
			{
				if (self.IsIdle) cargo.ResolveOrder(self, new Order("Unload", self, false));
				return;
			}
			if (self.TraitOrDefault<MedievalShelter>() != null && self.World.ActorsWithTrait<MedievalRest>().Any(p => p.Trait.Shelter == self && p.Trait.Sleeping)) return;
			if (++Progress < WorkRequired) return;
			finishing = true; var owner = self.Owner;
			self.World.AddFrameEndTask(w =>
			{
				if (!Owned(self, owner) || !Requested || !finishing) return;
				var amounts = new int[StorageGoods.Count];
				for (var good = 0; good < amounts.Length; good++) amounts[good] = StorageGoods.Amount(self, good);
				var recovery = owner.PlayerActor.Trait<MedievalDemolitionOrders>();
				for (var r = 0; r < Inventory.ResourceCount; r++)
				{
					// Unused deliveries in an unfinished site are returned in full.
					amounts[r] += building.Complete ? recovery.Recover(r, building.Info.Cost.ElementAtOrDefault(r)) : building.Materials.Amount(r);
					amounts[r] += self.TraitOrDefault<MedievalMilitaryWorkshop>()?.Inputs.Amount(r) ?? 0;
					amounts[r] += self.TraitOrDefault<MedievalSiegeWorkshop>()?.Inputs.Amount(r) ?? 0;
					amounts[r] += self.TraitOrDefault<MedievalTentWorkshop>()?.Inputs.Amount(r) ?? 0;
				}
				amounts[10] += building.Complete ? recovery.Recover(10, building.Info.Cloth) : building.CampMaterials.Amount(MedievalCampStore.Cloth);
				if (amounts.Any(n => n > 0))
				{
					var pile = w.CreateActor("md.salvage", [new OwnerInit(owner), new LocationInit(self.Location)]);
					foreach (var first in new[] { 0, 5, 8 })
					{
						var count = first == 0 ? 5 : first == 5 ? 3 : MedievalCampStore.GoodsCount; var contents = new int[Inventory.ResourceCount];
						Array.Copy(amounts, first, contents, 0, count); var inventory = StorageGoods.Stock(pile, first);
						inventory.SetCapacity(contents);
						for (var r = 0; r < count; r++) inventory.Add(r, contents[r]);
					}
				}
				w.Add(new SiegeImpactEffect(self.CenterPosition, (int)self.ActorID, masonry: true, ram: true));
				self.Dispose();
			});
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Requested || self.IsDead || self.World.FogObscures(self) || self.Owner != self.World.LocalPlayer) yield break;
			foreach (var c in self.Info.TraitInfo<BuildingInfo>().Tiles(self.Location))
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(self.World, c, Color.OrangeRed)) yield return line;
			yield return new LocalityLabelRenderable(self.CenterPosition, 30, Progress == 0 ? "A DEMONTER" : $"DEMOLITION {Progress * 100 / WorkRequired}%");
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
	public class MedievalSalvageInfo : TraitInfo, Requires<MedievalStockpileInfo>, Requires<MedievalEquipmentStoreInfo>, Requires<MedievalCampStoreInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalSalvage();
	}
	public sealed class MedievalSalvage : ITick
	{
		void ITick.Tick(Actor self)
		{
			if (self.World.WorldTick % 25 != self.ActorID % 25 || self.WillDispose) return;
			if (Enumerable.Range(0, StorageGoods.Count).Any(g => StorageGoods.Amount(self, g) > 0 || StorageGoods.Stock(self, g).Incoming(StorageGoods.Index(g)) > 0)) return;
			self.World.AddFrameEndTask(_ => { if (self.IsInWorld && Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Amount(self, g) == 0)) self.Dispose(); });
		}
	}
	public partial class MedievalWorker
	{
		bool FindDemolitionJob(Actor self)
		{
			foreach (var pile in Nearby(self, a => a.Owner == self.Owner, self.World.ActorsHavingTrait<MedievalSalvage>()))
				for (var good = 0; good < StorageGoods.Count; good++)
				{
					var r = StorageGoods.Index(good); var stock = StorageGoods.Stock(pile, good);
					if (stock.Available(r) <= 0) continue;
					var stores = good >= 8 ? MedievalCampStore.Stores(self.Owner) : good >= 5 ? MedievalEquipment.Stores(self.Owner)
						: Stores(self, true).Concat(MedievalMarket.Markets(self.Owner)).Concat(MedievalMarketStall.Stalls(self.Owner))
							.Concat(MedievalConstructionSupply.Sources(self.Owner, pile.CenterPosition).Where(a => a.TraitOrDefault<MedievalSupplyCart>() != null));
					foreach (var depot in stores.OrderBy(a => (a.CenterPosition - pile.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID))
					{
						if (Info.Profession == "hauler" && depot.TraitOrDefault<MedievalSupplyCart>() is { } cart
							&& !(good >= 8 ? depot.Trait<MedievalCampCart>().AcceptsTents(depot) : cart.AcceptsDelivery(depot, r))) continue;
						if (ReserveTrip(pile, stock, depot, StorageGoods.Stock(depot, good), r, Info.Capacity, equipment: good is >= 5 and < 8, camp: good >= 8))
						{ Status = "Va recuperer les materiaux du demontage"; return true; }
					}
				}
			var target = Nearby(self, a => a.Owner == self.Owner && MedievalDemolition.Pending(a), self.World.ActorsHavingTrait<MedievalDemolition>()).FirstOrDefault();
			if (target == null) return false;
			destination = target; phase = 8; Status = "Rejoint le chantier de demolition"; return true;
		}
	}
}
