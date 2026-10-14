using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalEquipment
	{
		public static readonly string[] Names = ["Arc", "Epee", "Armure"];
		public static readonly string[] Sequences = ["bow", "sword", "armor"];
		public static readonly int[][] Recipes = [[6, 0, 0, 2], [4, 0, 0, 6], [6, 0, 0, 10]];
		public static readonly string[] Soldiers = ["md.militia", "md.archer", "md.swordsman", "md.manatarms", "md.elitearcher", "md.royalguard", "md.drummer"];
		public static readonly string[] SoldierNames = ["Milicien", "Archer", "Epeiste", "Homme d'armes", "Archer d'elite", "Garde royal", "Tambour militaire"];
		public static readonly string[] BarracksRecruits = Soldiers.Concat(new[] { "md.messenger", "md.standardbearer", "md.commander", "md.saboteur" }).ToArray();
		public static readonly string[] BarracksNames = SoldierNames.Concat(new[] { "Messager", "Porte-drapeau", "Commandant", "Saboteur" }).ToArray();
		public static readonly string[] SoldierRoles = ["Infanterie legere a la masse.", "Tir a l'arc a 5 cases. Fragile au contact.", "Infanterie a l'epee, sans armure lourde.",
			"Infanterie en armure lourde. Plus lente et resistante.", "Arc long a 6 cases et armure legere. Tir plus puissant.", "Epee, grand bouclier et armure lourde. Garde d'elite au contact.",
			"Musicien sans attaque. Moral allies +20, ennemis -10, rayon 5 cases.\nEffets non cumulables. Affaibli par la faim, silencieux en famine."];
		public static readonly int[][] Kits = [[0, 0, 0, 0], [1, 0, 0, 0], [0, 1, 0, 0], [0, 1, 1, 0], [1, 0, 1, 0], [0, 1, 1, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 1, 1, 0], [0, 1, 0, 0]];
		public static IEnumerable<Actor> Stores(Player p) => p.World.ActorsWithTrait<MedievalEquipmentStore>()
			.Where(a => a.Trait.Info.Public && a.Actor.Owner == p && MedievalStockpile.Active(a.Actor)).Select(a => a.Actor).OrderBy(a => a.ActorID);
		public static int Available(Player p, int r) => Stores(p).Sum(a => a.Trait<MedievalEquipmentStore>().Stock.Available(r));
		public static int Amount(Player p, int r) => Stores(p).Sum(a => a.Trait<MedievalEquipmentStore>().Stock.Amount(r));
		public static IEnumerable<Actor> Workshops(Player p) => p.World.ActorsWithTrait<MedievalMilitaryWorkshop>().Select(t => t.Actor)
			.Where(a => a.Owner == p && MedievalStockpile.Active(a)).OrderBy(a => a.ActorID);
		// Count each physical item once, including a smith's current order and deliveries.
		// Equipment booked by barracks is no longer available and is replaced by the forges.
		public static int Committed(Player p, int r) => Available(p, r)
			+ Workshops(p).Where(a => a.Trait<MedievalMilitaryWorkshop>().Info.Forge).Sum(a => a.Trait<MedievalEquipmentStore>().Stock.Amount(r)
				+ (a.Trait<MedievalMilitaryWorkshop>().Product == r ? 1 : 0))
			+ p.World.ActorsWithTrait<MedievalWorker>().Where(t => t.Actor.Owner == p && t.Actor.IsInWorld && !t.Actor.IsDead
				&& t.Trait.Info.Profession == "blacksmith" && t.Trait.EquipmentCargo && t.Trait.Resource == r).Sum(t => t.Trait.Cargo);
		public static string Summary(Inventory stock) => string.Join(" / ", Enumerable.Range(0, 3).Select(r => $"{Names[r]} {stock.Amount(r)} ({stock.Reserved(r)} res.)"));
		public static string StockText(Actor actor) => actor?.TraitOrDefault<MedievalEquipmentStore>() is MedievalEquipmentStore store
			? $"Arcs {store.Stock.Amount(0)} | Epees {store.Stock.Amount(1)} | Armures {store.Stock.Amount(2)}" : "";
	}
	public class MedievalEquipmentStoreInfo : TraitInfo
	{
		public readonly int[] Capacity = [100, 100, 100, 0];
		public readonly int[] Initial = [0, 0, 0, 0];
		public readonly bool Public = false;
		public override object Create(ActorInitializer init) => new MedievalEquipmentStore(this);
	}
	public sealed class MedievalEquipmentStore : ISync, IRenderAnnotations, IRender
	{
		public readonly MedievalEquipmentStoreInfo Info;
		public readonly Inventory Stock;
		[VerifySync] public int StockHash => Stock.Hash;
		public MedievalEquipmentStore(MedievalEquipmentStoreInfo info) { Info = info; Stock = new Inventory(info.Capacity, info.Initial); }
		// Display the physical equipment once, next to its storage building. Incoming goods
		// only appear after unloading, and outgoing reservations remain until pickup.
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (!Info.Public || !MedievalStockpile.Active(self) || self.World.FogObscures(self)) yield break;
			var dimensions = self.Info.TraitInfo<BuildingInfo>().Dimensions;
			for (var r = 0; r < 3; r++)
			{
				var amount = MedievalStockyard.IndoorAmount(self, r + 5);
				if (amount == 0) continue;
				var pos = self.CenterPosition + new WVec((r - 1) * 1200, (dimensions.X + dimensions.Y) * 362 + 900, 0);
				var floor = self.World.Map.Sequences.GetSequence("md.stockyard-floor", "idle").GetSprite(0);
				yield return new SpriteRenderable(floor, pos, WVec.Zero, -4096, null, .8f, 1, Vector3.One, TintModifiers.None, true);
				var sprite = self.World.Map.Sequences.GetSequence("md.equipment", MedievalEquipment.Sequences[r]).GetSprite(0);
				for (var i = 0; i < amount; i++)
				{
					var item = pos + new WVec((i % 4 - 1) * 160, (i % 16 / 4 - 1) * 160, i / 16 * 96 + 160);
					yield return new SpriteRenderable(sprite, item, WVec.Zero, 1024, null, .8f, 1, Vector3.One, TintModifiers.None, true);
				}
			}
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			if (!Info.Public) yield break;
			var dimensions = self.Info.TraitInfo<BuildingInfo>().Dimensions;
			var point = wr.ScreenPxPosition(self.CenterPosition + new WVec(0, (dimensions.X + dimensions.Y) * 362 + 900, 0));
			yield return new Rectangle(point.X - 90, point.Y - 70, 180, 120);
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (MedievalInfluence.StrategicView(wr) || !MedievalStockpile.Active(self) || self.World.FogObscures(self)) yield break;
			var yard = self.TraitOrDefault<MedievalStockyard>();
			var resources = Enumerable.Range(0, 3).Where(r => yard?.HasArea != true || MedievalStockyard.IndoorAmount(self, r + 5) > 0).ToArray();
			if (resources.Length == 0) yield break;
			var height = self.Info.TraitInfo<MedievalStockDisplayInfo>().LabelHeight + (self.TraitOrDefault<MedievalStockpile>() != null ? 22 : 0);
			yield return new StockBadgeRenderable(self.World, self.CenterPosition, new int2(0, -height), resources,
				resources.Select(r => MedievalStockyard.IndoorAmount(self, r + 5)).ToArray(), resources.Select(r => yard?.HasArea == true ? StorageGoods.BaseCapacity(self, r + 5) : Stock.Capacity(r)).ToArray(), true);
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
	public class MedievalEquipmentEconomyInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalEquipmentEconomy();
	}
	public sealed class MedievalEquipmentEconomy : ISync, IResolveOrder
	{
		readonly int[] targets = [0, 0, 0];
		public int Target(int r) => targets[r];
		[VerifySync] public int TargetHash => targets[0] + 101 * targets[1] + 10201 * targets[2];
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "MedievalEquipmentTarget" || order.ExtraData >= 3) return;
			if (order.TargetString == "+") targets[order.ExtraData] = Math.Min(100, targets[order.ExtraData] + 1);
			if (order.TargetString == "-") targets[order.ExtraData] = Math.Max(0, targets[order.ExtraData] - 1);
		}
	}
}
