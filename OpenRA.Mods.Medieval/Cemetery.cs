using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed class CemeteryPlot(CPos cell, Player owner)
	{
		public readonly CPos Cell = cell;
		public readonly Player Owner = owner;
		public uint ReservedBy { get; private set; }
		public int BodyId { get; private set; } = -1;
		public bool Buried => BodyId >= 0;
		public bool Free => !Buried && ReservedBy == 0;
		public bool Reserve(uint actor)
		{
			if (!Free || actor == 0) return false;
			ReservedBy = actor; return true;
		}
		public void Release(uint actor) { if (ReservedBy == actor) ReservedBy = 0; }
		public bool Bury(uint actor, int body)
		{
			if (Buried || actor == 0 || ReservedBy != actor || body < 0) return false;
			BodyId = body; ReservedBy = 0; return true;
		}
	}
	[TraitLocation(SystemActors.World)]
	public class MedievalCemeteriesInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCemeteries();
	}
	// Like storage zones, plots are walkable ground, reconstructed by synchronized
	// orders during save replay. One reservation and one permanent grave per cell.
	public sealed class MedievalCemeteries : IRender, ISync
	{
		public const int MaximumPlots = 4096;
		readonly Dictionary<CPos, CemeteryPlot> plots = [];
		public IEnumerable<CemeteryPlot> Plots => plots.Values;
		public CemeteryPlot At(CPos cell) => plots.GetValueOrDefault(cell);
		[VerifySync] public int PlotsHash
		{
			get { unchecked { return plots.Values.Aggregate(17, (h, p) => ((((h * 31 + p.Cell.X) * 31 + p.Cell.Y)
				* 31 + (int)p.Owner.PlayerActor.ActorID) * 31 + p.BodyId) * 31 + (int)p.ReservedBy); } }
		}
		public static bool Covers(World world, CPos cell) => world.WorldActor.TraitOrDefault<MedievalCemeteries>()?.At(cell) != null;
		public string PlacementError(Actor church, AgriculturalZone zone)
		{
			if (!MedievalStockpile.Active(church) || church.TraitOrDefault<MedievalChurch>() == null) return "Selectionnez une eglise terminee.";
			if (!zone.ValidSize) return "Cimetiere : de 2 a 10 cases par cote.";
			if (plots.Count + zone.Area > MaximumPlots) return "La limite de places du cimetiere est atteinte.";
			if (MedievalAgriculturalArea.DistanceToRectangle(church.Location, church.Info.TraitInfo<BuildingInfo>().Dimensions, zone.Start, zone.Size) > 144)
				return "Tracez le cimetiere a moins de 12 cases de l'eglise.";
			var world = church.World; var cells = zone.Cells().ToHashSet();
			if (cells.Any(c => !world.Map.Contains(c) || !church.Owner.Shroud.IsExplored(c))) return "Choisissez un terrain explore.";
			if (cells.Any(c => world.Map.Ramp[c] != 0 || world.Map.GetTerrainInfo(c).Type is not ("Clear" or "Road" or "Rough"))) return "Choisissez un terrain plat et praticable.";
			if (cells.Any(c => plots.ContainsKey(c) || MedievalStockyard.Covers(world, c))) return "Une zone occupe deja cet emplacement.";
			if (world.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Info.TraitInfoOrDefault<BuildingInfo>() is BuildingInfo b && b.Tiles(a.Location).Any(cells.Contains)))
				return "Le cimetiere recouvre un batiment, une culture ou une ressource.";
			return null;
		}
		public bool Place(Actor church, AgriculturalZone zone)
		{
			var error = PlacementError(church, zone);
			church.Owner.PlayerActor.Trait<MedievalSettlement>().Notify(error ?? $"Cimetiere trace : {zone.Area} places. Recrutez un croque-mort a l'eglise.");
			if (error != null) return false;
			foreach (var cell in zone.Cells()) plots.Add(cell, new CemeteryPlot(cell, church.Owner));
			return true;
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			foreach (var plot in plots.Values)
			{
				var pos = self.World.Map.CenterOfCell(plot.Cell); var p = wr.ScreenPxPosition(pos);
				if (p.X + 40 < wr.Viewport.TopLeft.X || p.X - 40 > wr.Viewport.BottomRight.X || p.Y + 40 < wr.Viewport.TopLeft.Y
					|| p.Y - 60 > wr.Viewport.BottomRight.Y || self.World.FogObscures(plot.Cell)) continue;
				yield return new MedievalGroundLayer(new CemeteryPlotRenderable(self.World, plot, this), 1);
				if (plot.Buried) yield return new GravestoneRenderable(pos, plot.BodyId);
			}
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr) { yield break; }
	}
	public sealed class MedievalCemeteryOrderGenerator : OrderGenerator
	{
		readonly Actor church;
		readonly WorldRenderer renderer;
		CPos? first;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalCemeteryOrderGenerator(World world, WorldRenderer renderer, Actor church) : base(world) { this.renderer = renderer; this.church = church; }
		AgriculturalZone Zone(CPos cell) => AgriculturalZone.Between(first ?? cell, first.HasValue ? cell : cell + new CVec(1, 1));
		string Error(World world, AgriculturalZone zone) => church.Owner != world.LocalPlayer ? "Cette eglise ne vous appartient plus."
			: world.WorldActor.Trait<MedievalCemeteries>().PlacementError(church, zone);
		public string PreviewText(World world)
		{
			var zone = Zone(renderer.Viewport.ViewToWorld(Viewport.LastMousePos));
			return (Error(world, zone) ?? $"{(first.HasValue ? "Second coin" : "Premier coin")} : cimetiere {zone.Size.X}x{zone.Size.Y}, {zone.Area} tombes, gratuit.") + " Clic droit : annuler.";
		}
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 pixel, MouseInput mi)
		{
			if (!first.HasValue) { if (world.Map.Contains(cell) && church.Owner.Shroud.IsExplored(cell)) first = cell; yield break; }
			if (Error(world, Zone(cell)) != null) yield break;
			yield return new Order("MedievalPlaceCemetery", church, Target.FromCell(world, first.Value), false) { ExtraLocation = cell };
			world.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			var zone = Zone(renderer.Viewport.ViewToWorld(Viewport.LastMousePos));
			if (zone.Size.X > 32 || zone.Size.Y > 32) yield break;
			var valid = Error(world, zone) == null;
			foreach (var c in zone.Cells().Where(world.Map.Contains))
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, c, valid ? Color.LimeGreen : Color.Red)) yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 pixel, MouseInput mi) => Error(world, Zone(cell)) == null ? "ability" : "move-blocked";
	}
	sealed class CemeteryPlotRenderable(World world, CemeteryPlot plot, MedievalCemeteries cemetery) : IRenderable, IFinalizedRenderable
	{
		public WPos Pos => world.Map.CenterOfCell(plot.Cell);
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => this;
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new Rectangle(p.X - 30, p.Y - 20, 60, 40); }
		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var corners = world.Map.Grid.Ramps[0].Corners;
			var p = corners.Select(c => wr.Screen3DPxPosition(Pos + c)).ToArray();
			cr.FillRect(p[0], p[1], p[2], p[3], Color.FromArgb(72, 91, 80, 53));
			// A low stone edging surrounds the complete zone, without blocking paths.
			CVec[] neighbors = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];
			for (var i = 0; i < 4; i++)
				if (cemetery.At(plot.Cell + neighbors[i])?.Owner != plot.Owner)
				{
					cr.DrawLine(p[i], p[(i + 1) % 4], 3, Color.FromArgb(215, 74, 72, 60));
					cr.DrawLine(p[i] - new Vector3(0, 1, 0), p[(i + 1) % 4] - new Vector3(0, 1, 0), 1, Color.FromArgb(220, 171, 166, 143));
				}
		}
	}
	sealed class GravestoneRenderable(WPos position, int seed) : IRenderable, IFinalizedRenderable
	{
		public WPos Pos => position;
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new GravestoneRenderable(position + offset, seed);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new Rectangle(p.X - 18, p.Y - 26, 36, 40); }
		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer; var p = wr.Screen3DPxPosition(Pos);
			Vector3 V(float x, float y) => p + new Vector3(x, y, 0);
			cr.FillRect(V(-10, 1), V(1, -4), V(12, 2), V(1, 8), Color.FromArgb(130, 58, 46, 29));
			cr.FillRect(V(-8, 1), V(1, -3), V(10, 2), V(1, 6), Color.FromArgb(230, 102, 82, 52));
			cr.FillRect(V(-5, -4), V(5, -1), V(12, 2), V(2, 0), Color.FromArgb(70, 20, 22, 18));
			var height = 11 + seed % 3;
			cr.FillRect(V(-6, -height), V(1, -height - 2), V(1, -2), V(-6, -4), Color.FromArgb(255, 175, 172, 152));
			cr.FillRect(V(1, -height - 2), V(4, -height), V(4, -4), V(1, -2), Color.FromArgb(255, 98, 102, 93));
			cr.DrawLine(V(-6, -height), V(1, -height - 2), 1, Color.FromArgb(240, 224, 217, 192));
			cr.DrawLine(V(-3, -height + 3), V(-3, -5), 1, Color.FromArgb(220, 89, 91, 80));
			cr.DrawLine(V(-5, -height + 5), V(-1, -height + 4), 1, Color.FromArgb(220, 89, 91, 80));
		}
	}
}
