using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalCampStoreInfo : TraitInfo
	{
		public readonly bool Public = false;
		public readonly int[] Capacity = [100, 12, 60, 0];
		public override object Create(ActorInitializer init) => new MedievalCampStore(this);
	}
	public sealed class MedievalCampStore : ISync, IRenderAnnotations, IRender
	{
		public const int Cloth = 2;
		public const int GoodsCount = 3;
		public static readonly string[] Names = ["Laine", "Tentes", "Tissu"];
		public static readonly string[] Sequences = ["wool", "tent", "cloth"];
		public readonly MedievalCampStoreInfo Info;
		public readonly Inventory Stock;
		[VerifySync] public int StockHash => Stock.Hash;
		public MedievalCampStore(MedievalCampStoreInfo info) { Info = info; Stock = new(info.Capacity); }
		public static IEnumerable<Actor> Stores(Player player) => player.World.ActorsHavingTrait<MedievalCampStore>()
			.Where(a => a.Owner == player && MedievalStockpile.Active(a) && a.Trait<MedievalCampStore>().Info.Public).OrderBy(a => a.ActorID);
		public static int Amount(Player player, int r) => player.World.ActorsHavingTrait<MedievalCampStore>()
			.Where(a => a.Owner == player && MedievalStockpile.Active(a)).Sum(a => a.Trait<MedievalCampStore>().Stock.Amount(r));
		WVec StockOffset(Actor self, int r)
		{
			var size = self.Info.TraitInfoOrDefault<BuildingInfo>()?.Dimensions ?? CVec.Zero;
			return size == CVec.Zero ? new WVec(0, -200, 180) : new WVec((r * 2 - 1) * 700, (size.X + size.Y) * 362 + (Info.Public ? 1700 : 150), 0);
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (!MedievalStockpile.Active(self) || self.World.FogObscures(self)) yield break;
			for (var r = 0; r < GoodsCount; r++)
			{
				var sprite = self.World.Map.Sequences.GetSequence("md.campgoods", Sequences[r]).GetSprite(0);
				var position = self.CenterPosition + StockOffset(self, r);
				for (var i = 0; i < MedievalStockyard.IndoorAmount(self, r + 8); i++)
					yield return new SpriteRenderable(sprite, position, new WVec(i % 4 * 160, i % 16 / 4 * 160, i / 16 * 90), 1024,
						null, self.TraitOrDefault<Mobile>() != null ? .55f : .75f, 1, Vector3.One, TintModifiers.None, true);
			}
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			for (var r = 0; r < GoodsCount; r++)
			{
				var point = wr.ScreenPxPosition(self.CenterPosition + StockOffset(self, r));
				yield return new Rectangle(point.X - 35, point.Y - 60, 90, 90);
			}
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!MedievalStockpile.Active(self) || self.World.FogObscures(self) || !self.World.Selection.Contains(self)) yield break;
			if (self.TraitOrDefault<MedievalCommander>() != null && Stock.Amount(1) == 0) yield break;
			var yard = self.TraitOrDefault<MedievalStockyard>();
			var goods = Enumerable.Range(0, GoodsCount).Where(r => Stock.Capacity(r) > 0 && (yard?.HasArea != true || MedievalStockyard.IndoorAmount(self, r + 8) > 0)).ToArray();
			if (goods.Length == 0) yield break;
			yield return new StockBadgeRenderable(self.World, self.CenterPosition, new int2(0, -154), goods,
				goods.Select(r => MedievalStockyard.IndoorAmount(self, r + 8)).ToArray(),
				goods.Select(r => yard?.HasArea == true ? StorageGoods.BaseCapacity(self, r + 8) : Stock.Capacity(r)).ToArray(), camp: true);
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
	public class MedievalTentWorkshopInfo : TraitInfo, Requires<MedievalCampStoreInfo>
	{
		public readonly int WorkSteps = 40;
		public override object Create(ActorInitializer init) => new MedievalTentWorkshop(this);
	}
	public sealed class MedievalTentWorkshop : IResolveOrder, ISync
	{
		readonly MedievalTentWorkshopInfo info;
		public readonly Inventory Inputs = new([4, 0, 0, 0]);
		[VerifySync] public int InputHash => Inputs.Hash;
		[VerifySync] public int Progress;
		[VerifySync] public int Target = 4;
		[VerifySync] public int ClothTarget = 12;
		[VerifySync] public int Product = MedievalCampStore.Cloth;
		public const int TentCloth = 4;
		public int WoolRequired => Product == MedievalCampStore.Cloth ? 2 : 0;
		public int ClothRequired => Product == MedievalCampStore.Cloth ? 0 : TentCloth;
		public int WoodRequired => Product == MedievalCampStore.Cloth ? 0 : 4;
		public int WorkRequired => Product == MedievalCampStore.Cloth ? 12 : info.WorkSteps;
		public string ProductName => Product == MedievalCampStore.Cloth ? "tissu" : "pavillon";
		Actor artisan;
		[VerifySync] public int ArtisanId => (int)(artisan?.ActorID ?? 0);
		public MedievalTentWorkshop(MedievalTentWorkshopInfo info) { this.info = info; }
		public bool Assign(Actor self, Actor worker)
		{
			if (artisan != null && MedievalStockpile.Active(artisan) && artisan.Owner == self.Owner && artisan != worker) return false;
			artisan = worker; return true;
		}
		bool Needed(Actor self, int product) => MedievalCampStore.Amount(self.Owner, product)
			+ self.World.ActorsHavingTrait<MedievalWorker>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a))
				.Sum(a => a.Trait<MedievalWorker>() is { CampCargo: true } worker && worker.Resource == product ? worker.Cargo : 0) < (product == MedievalCampStore.Cloth ? ClothTarget : Target);
		public bool Needed(Actor self) => Needed(self, Product) || Product == MedievalCampStore.Cloth && Needed(self, 1)
			&& self.Trait<MedievalCampStore>().Stock.Available(MedievalCampStore.Cloth)
				+ self.Trait<MedievalCampStore>().Stock.Incoming(MedievalCampStore.Cloth) < TentCloth;
		// Keep the next tent's fabric in the workshop. Both the artisan and haulers
		// use the unreserved surplus so concurrent trips cannot export its inputs.
		public int Exportable(Actor self, int product) => Math.Max(0, self.Trait<MedievalCampStore>().Stock.Available(product)
			- (product == MedievalCampStore.Cloth && Needed(self, 1) ? TentCloth : 0));
		public void Plan(Actor self)
		{
			var stock = self.Trait<MedievalCampStore>().Stock;
			var assemble = Needed(self, 1) && (stock.Available(MedievalCampStore.Cloth) >= TentCloth
				|| stock.Incoming(MedievalCampStore.Cloth) > 0
				|| MedievalCampStore.Stores(self.Owner).Any(a => a.Trait<MedievalCampStore>().Stock.Available(MedievalCampStore.Cloth) > 0));
			// Finish a piece already being woven before switching to supplied fabric.
			if (Product == MedievalCampStore.Cloth && Progress > 0 && Needed(self)) return;
			var next = assemble ? 1 : MedievalCampStore.Cloth;
			if (Product != next) { Product = next; Progress = 0; }
		}
		public bool Ready(Actor self) => Inputs.Available(0) >= WoodRequired && self.Trait<MedievalCampStore>().Stock.Available(0) >= WoolRequired
			&& self.Trait<MedievalCampStore>().Stock.Available(MedievalCampStore.Cloth) >= ClothRequired;
		// Presentation follows the assigned citizen's real work, including stops and sleep.
		public bool Working(Actor self) => MedievalStockpile.Active(artisan) && artisan.Owner == self.Owner && artisan.IsIdle
			&& artisan.TraitOrDefault<MedievalRest>()?.Resting != true
			&& artisan.Trait<MedievalWorker>() is { Automatic: true, IsWorking: true } worker && worker.WorkTarget == self
			&& Ready(self) && self.Trait<MedievalCampStore>().Stock.Free(Product) > 0 && Needed(self);
		public bool Work(Actor self)
		{
			var stock = self.Trait<MedievalCampStore>().Stock;
			if (!Ready(self) || !Needed(self) || stock.Free(Product) == 0) return false;
			if (++Progress < WorkRequired) return true;
			Inputs.Take(0, WoodRequired); stock.Take(0, WoolRequired); stock.Take(MedievalCampStore.Cloth, ClothRequired);
			stock.Add(Product, 1); Progress = 0;
			Product = Product == 1 ? MedievalCampStore.Cloth : 1;
			return false;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalTentTarget") Target = Math.Clamp((int)order.ExtraData, 0, 20);
			if (order.OrderString == "MedievalClothTarget") ClothTarget = Math.Clamp((int)order.ExtraData, 0, 60);
		}
	}
	public partial class MedievalWorker
	{
		[VerifySync] public bool CampCargo;
		[VerifySync] bool preferCamp = true;
		public void PauseForSleep() { Release(); Status = "Rentre dormir"; }
		bool ReturnCampCargo(Actor self)
		{
			foreach (var depot in MedievalCampStore.Stores(self.Owner).OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID))
			{
				var stock = depot.Trait<MedievalCampStore>().Stock;
				if (!stock.ReserveIn(Resource, Cargo)) continue;
				destination = depot; destinationStock = stock; destinationReserved = true; phase = 2; Status = "Rapporte " + CargoName; return true;
			}
			Status = "Charge conservee : depot plein ou absent"; return false;
		}
		void FindTentWorkshopJob(Actor self)
		{
			var home = Nearby(self, a => a.Owner == self.Owner && MedievalStockpile.Active(a), self.World.ActorsHavingTrait<MedievalTentWorkshop>())
				.FirstOrDefault(a => a.Trait<MedievalTentWorkshop>().Assign(a, self));
			if (home == null) { Status = "Aucun atelier de tissage disponible"; return; }
			var workshop = home.Trait<MedievalTentWorkshop>(); var goods = home.Trait<MedievalCampStore>().Stock;
			foreach (var depot in MedievalCampStore.Stores(self.Owner).OrderBy(a => (a.CenterPosition - home.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID))
				foreach (var product in new[] { MedievalCampStore.Cloth, 1 })
					if (ReserveTrip(home, goods, depot, depot.Trait<MedievalCampStore>().Stock, product, Math.Min(Info.Capacity, workshop.Exportable(home, product)), camp: true)) return;
			workshop.Plan(home);
			if (!workshop.Needed(home)) { Status = "Stocks cibles de tissu et de tentes atteints"; return; }
			var clothNeeded = workshop.ClothRequired - goods.Available(MedievalCampStore.Cloth) - goods.Incoming(MedievalCampStore.Cloth);
			if (clothNeeded > 0)
				foreach (var depot in Nearby(self, _ => true, MedievalCampStore.Stores(self.Owner)))
					if (ReserveTrip(depot, depot.Trait<MedievalCampStore>().Stock, home, goods, MedievalCampStore.Cloth, Math.Min(Info.Capacity, clothNeeded), camp: true)) return;
			var woolNeeded = workshop.WoolRequired - goods.Amount(0) - goods.Incoming(0);
			if (woolNeeded > 0)
				foreach (var depot in Nearby(self, a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a != home,
					self.World.ActorsHavingTrait<MedievalCampStore>()))
					if (ReserveTrip(depot, depot.Trait<MedievalCampStore>().Stock, home, goods, 0, Math.Min(Info.Capacity, woolNeeded), camp: true)) return;
			var woodNeeded = workshop.WoodRequired - workshop.Inputs.Amount(0) - workshop.Inputs.Incoming(0);
			if (woodNeeded > 0)
				foreach (var depot in MedievalConstructionSupply.Sources(self.Owner, home.CenterPosition))
					if (ReserveTrip(depot, depot.Trait<MedievalStockpile>().Stock, home, workshop.Inputs, 0, woodNeeded)) return;
			if (!workshop.Ready(home))
			{
				Status = workshop.Product == MedievalCampStore.Cloth ? "Tissu : attend 2 laine" : "Pavillon : attend 4 tissus et 4 bois"; return;
			}
			destination = home; phase = 7; Status = "Tisse : " + workshop.ProductName;
		}
		bool FindCampHaulingJob(Actor self)
		{
			foreach (var cart in Nearby(self, a => a.Owner == self.Owner && a.Trait<MedievalCampCart>().AcceptsTents(a), self.World.ActorsHavingTrait<MedievalCampCart>()))
				foreach (var depot in MedievalCampStore.Stores(self.Owner).Where(a => MedievalLogistics.Local(a, cart, 16))
					.OrderBy(a => (a.CenterPosition - cart.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID))
					if (ReserveTrip(depot, depot.Trait<MedievalCampStore>().Stock, cart, cart.Trait<MedievalCampStore>().Stock, 1, 1, camp: true)) return true;
			foreach (var source in Nearby(self, a => a.Owner == self.Owner && MedievalStockpile.Active(a) && !a.Trait<MedievalCampStore>().Info.Public
				&& a.TraitOrDefault<MedievalSupplyCart>() == null && a.TraitOrDefault<MedievalCommander>() == null, self.World.ActorsHavingTrait<MedievalCampStore>()))
				foreach (var depot in MedievalCampStore.Stores(self.Owner).Where(a => MedievalLogistics.Local(source, a, 16)))
						for (var r = 0; r < MedievalCampStore.GoodsCount; r++)
							if ((r != 0 || source.TraitOrDefault<MedievalTentWorkshop>() == null)
								&& (r != MedievalCampStore.Cloth || source.TraitOrDefault<MedievalMilitaryWorkshop>() == null)
							&& ReserveTrip(source, source.Trait<MedievalCampStore>().Stock, depot, depot.Trait<MedievalCampStore>().Stock, r,
								Math.Min(Info.Capacity, source.TraitOrDefault<MedievalTentWorkshop>()?.Exportable(source, r) ?? (r == 1 ? 1 : 10)), camp: true)) return true;
			return false;
		}
	}
}
