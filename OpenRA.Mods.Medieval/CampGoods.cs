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
		public readonly int[] Capacity = [100, 12, 0, 0];
		public override object Create(ActorInitializer init) => new MedievalCampStore(this);
	}
	public sealed class MedievalCampStore : ISync, IRenderAnnotations, IRender
	{
		public static readonly string[] Names = ["Laine", "Tentes"];
		public static readonly string[] Sequences = ["wool", "tent"];
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
			for (var r = 0; r < 2; r++)
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
			for (var r = 0; r < 2; r++)
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
			var goods = Enumerable.Range(0, 2).Where(r => yard?.HasArea != true || MedievalStockyard.IndoorAmount(self, r + 8) > 0).ToArray();
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
		Actor artisan;
		[VerifySync] public int ArtisanId => (int)(artisan?.ActorID ?? 0);
		public MedievalTentWorkshop(MedievalTentWorkshopInfo info) { this.info = info; }
		public bool Assign(Actor self, Actor worker)
		{
			if (artisan != null && MedievalStockpile.Active(artisan) && artisan.Owner == self.Owner && artisan != worker) return false;
			artisan = worker; return true;
		}
		public bool Needed(Actor self) => MedievalCampStore.Amount(self.Owner, 1)
			+ self.World.ActorsHavingTrait<MedievalWorker>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a))
				.Sum(a => a.Trait<MedievalWorker>() is { CampCargo: true, Resource: 1 } worker ? worker.Cargo : 0) < Target;
		public bool Ready(Actor self) => Inputs.Available(0) >= 4 && self.Trait<MedievalCampStore>().Stock.Available(0) >= 8;
		// Presentation follows the assigned citizen's real work, including stops and sleep.
		public bool Working(Actor self) => MedievalStockpile.Active(artisan) && artisan.Owner == self.Owner && artisan.IsIdle
			&& artisan.TraitOrDefault<MedievalRest>()?.Resting != true
			&& artisan.Trait<MedievalWorker>() is { Automatic: true, IsWorking: true } worker && worker.WorkTarget == self
			&& Ready(self) && self.Trait<MedievalCampStore>().Stock.Free(1) > 0 && Needed(self);
		public bool Work(Actor self)
		{
			var stock = self.Trait<MedievalCampStore>().Stock;
			if (!Ready(self) || !Needed(self) || stock.Free(1) == 0) return false;
			if (++Progress < info.WorkSteps) return true;
			Inputs.Take(0, 4); stock.Take(0, 8); stock.Add(1, 1); Progress = 0; return false;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalTentTarget") Target = Math.Clamp((int)order.ExtraData, 0, 20);
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
				if (ReserveTrip(home, goods, depot, depot.Trait<MedievalCampStore>().Stock, 1, 1, camp: true)) return;
			if (!workshop.Needed(home)) { Status = "Stock cible de tentes atteint"; return; }
			var woolNeeded = 8 - goods.Amount(0) - goods.Incoming(0);
			if (woolNeeded > 0)
				foreach (var depot in Nearby(self, a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a != home,
					self.World.ActorsHavingTrait<MedievalCampStore>()))
					if (ReserveTrip(depot, depot.Trait<MedievalCampStore>().Stock, home, goods, 0, Math.Min(Info.Capacity, woolNeeded), camp: true)) return;
			var woodNeeded = 4 - workshop.Inputs.Amount(0) - workshop.Inputs.Incoming(0);
			if (woodNeeded > 0)
				foreach (var depot in MedievalConstructionSupply.Sources(self.Owner, home.CenterPosition))
					if (ReserveTrip(depot, depot.Trait<MedievalStockpile>().Stock, home, workshop.Inputs, 0, woodNeeded)) return;
			if (!workshop.Ready(home)) { Status = "Tente : attend 8 laine et 4 bois"; return; }
			destination = home; phase = 7; Status = "Tisse une tente militaire";
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
					for (var r = 0; r < 2; r++)
						if ((r == 1 || source.TraitOrDefault<MedievalTentWorkshop>() == null)
							&& ReserveTrip(source, source.Trait<MedievalCampStore>().Stock, depot, depot.Trait<MedievalCampStore>().Stock, r, Math.Min(Info.Capacity, r == 1 ? 1 : 10), camp: true)) return true;
			return false;
		}
	}
}
