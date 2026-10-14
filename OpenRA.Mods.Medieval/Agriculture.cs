using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct AgriculturalZone(CPos Start, CVec Size)
	{
		public const int MinimumSize = 2;
		public const int MaximumSize = 10;
		public int Area => Size.X * Size.Y;
		public bool ValidSize => Size.X >= MinimumSize && Size.Y >= MinimumSize && Size.X <= MaximumSize && Size.Y <= MaximumSize;
		public static AgriculturalZone Between(CPos first, CPos last) => new(new CPos(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y)),
			new CVec(Math.Abs(first.X - last.X) + 1, Math.Abs(first.Y - last.Y) + 1));
		public string ActorType(string kind) => Size == new CVec(2, 2) ? kind : $"{kind}.{Size.X}x{Size.Y}";
		public static bool IsKind(string type) => type == "md.field" || type == "md.pasture" || type == "md.sheeppasture";
		public static int Capacity(int area, int cellsPerWorker) => Math.Max(1, (area + cellsPerWorker - 1) / cellsPerWorker);
		public IEnumerable<CPos> Cells()
		{
			for (var y = 0; y < Size.Y; y++)
				for (var x = 0; x < Size.X; x++) yield return Start + new CVec(x, y);
		}
	}

	public class MedievalAgriculturalAreaInfo : TraitInfo, Requires<BuildingInfo>, Requires<MedievalSourceInfo>, Requires<MedievalBuildingInfo>
	{
		public readonly string Profession = "farmer";
		public readonly string Farm = "md.farm";
		public readonly int CellsPerWorker = 4;
		public readonly int CellsPerCow = 0;
		public override object Create(ActorInitializer init) => new MedievalAgriculturalArea(init, this);
	}
	public class MedievalAgriculturalArea
	{
		public readonly MedievalAgriculturalAreaInfo Info;
		public readonly CVec Size;
		public int Capacity => AgriculturalZone.Capacity(Size.X * Size.Y, Info.CellsPerWorker);
		public int CattleCapacity => Info.CellsPerCow == 0 ? 0 : Math.Max(1, Size.X * Size.Y / Info.CellsPerCow);
		public MedievalAgriculturalArea(ActorInitializer init, MedievalAgriculturalAreaInfo info)
		{ Info = info; Size = init.Self.Info.TraitInfo<BuildingInfo>().Dimensions; }
		public static long DistanceToRectangle(CPos farm, CVec farmSize, CPos start, CVec size)
		{
			var dx = Math.Max(0, Math.Max(start.X - (farm.X + farmSize.X - 1), farm.X - (start.X + size.X - 1)));
			var dy = Math.Max(0, Math.Max(start.Y - (farm.Y + farmSize.Y - 1), farm.Y - (start.Y + size.Y - 1)));
			return (long)dx * dx + (long)dy * dy;
		}
		public static Actor FindFarm(World world, Player owner, string type, CPos start, CVec size) => world.ActorsHavingTrait<MedievalBuilding>()
			.Where(a => a.Owner == owner && a.Info.Name == type && MedievalStockpile.Active(a)
				&& DistanceToRectangle(a.Location, a.Info.TraitInfo<BuildingInfo>().Dimensions, start, size) <= 64)
			.OrderBy(a => DistanceToRectangle(a.Location, a.Info.TraitInfo<BuildingInfo>().Dimensions, start, size)).ThenBy(a => a.ActorID).FirstOrDefault();
		public Actor Farm(Actor self) => FindFarm(self.World, self.Owner, Info.Farm, self.Location, Size);
		public int Workers(Actor self) => self.World.ActorsWithTrait<MedievalWorker>().Count(p => p.Actor.Owner == self.Owner && p.Actor.IsInWorld && !p.Actor.IsDead
			&& p.Trait.Automatic && p.Trait.WorkArea == self);
		public string Description(Actor self) => $"{Size.X}x{Size.Y} : {Workers(self)}/{Capacity} {(Info.Profession == "farmer" ? "fermiers" : "eleveurs")}" 
			+ (CattleCapacity > 0 ? " | " + self.Trait<MedievalLivestock>().Status : "");
	}

	public class MedievalZoneOrderGenerator : OrderGenerator
	{
		readonly string kind;
		readonly WorldRenderer renderer;
		CPos? first;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalZoneOrderGenerator(World world, WorldRenderer renderer, string kind) : base(world) { this.kind = kind; this.renderer = renderer; }
		CPos Cell => renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
		AgriculturalZone Zone(CPos cell) => AgriculturalZone.Between(first ?? cell, first.HasValue ? cell : cell + new CVec(1, 1));
		public string PreviewText(World world)
		{
			var zone = Zone(Cell);
			if (!zone.ValidSize) return "Zone : choisissez de 2 a 10 cases de largeur et de hauteur. Clic droit : annuler.";
			var actor = world.Map.Rules.Actors[zone.ActorType(kind)];
			var info = actor.TraitInfo<MedievalAgriculturalAreaInfo>();
			var cost = actor.TraitInfo<MedievalBuildingInfo>().Cost;
			return $"{(first.HasValue ? "Second coin" : "Premier coin")} : {zone.Size.X}x{zone.Size.Y} ({zone.Area} cases), {cost[0]} bois {cost[3]} pierre | "
				+ $"{AgriculturalZone.Capacity(zone.Area, info.CellsPerWorker)} {(info.Profession == "farmer" ? "fermiers" : "eleveurs")}" 
				+ (info.CellsPerCow > 0 ? $", {Math.Max(1, zone.Area / info.CellsPerCow)} {(info.Profession == "shepherd" ? "moutons" : "vaches")}" : "") + " | Clic droit : annuler";
		}
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (world.Paused) yield break;
			if (!first.HasValue) { if (world.Map.Contains(cell) && world.LocalPlayer.Shroud.IsExplored(cell)) first = cell; yield break; }
			var zone = Zone(cell);
			if (!zone.ValidSize || !MedievalSettlement.CanPlace(world, world.LocalPlayer, zone.ActorType(kind), zone.Start)) yield break;
			yield return new Order("MedievalPlaceZone", world.LocalPlayer.PlayerActor, Target.FromCell(world, first.Value), false) { TargetString = kind, ExtraLocation = cell };
			world.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
		{
			var zone = Zone(Cell);
			if (zone.ValidSize)
				yield return MedievalPlaceOrderGenerator.Preview(world, zone.ActorType(kind), zone.Start,
					MedievalSettlement.CanPlace(world, world.LocalPlayer, zone.ActorType(kind), zone.Start));
		}
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			var zone = Zone(Cell);
			// Bound preview work as well as the accepted order size.
			if (zone.Size.X > 64 || zone.Size.Y > 64) yield break;
			var valid = zone.ValidSize && MedievalSettlement.CanPlace(world, world.LocalPlayer, zone.ActorType(kind), zone.Start);
			foreach (var c in zone.Cells())
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, c, valid ? Color.LimeGreen : Color.Red))
					yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var zone = Zone(cell);
			return zone.ValidSize && MedievalSettlement.CanPlace(world, world.LocalPlayer, zone.ActorType(kind), zone.Start) ? "ability" : "move-blocked";
		}
	}
}
