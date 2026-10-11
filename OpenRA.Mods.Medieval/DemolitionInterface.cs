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
	public sealed class MedievalDemolitionOrderGenerator : OrderGenerator
	{
		readonly WorldRenderer renderer;
		CPos? start;
		public const string Help = "Demolir : cliquez ou maintenez et glissez pour tracer une zone. Transporteurs et batisseurs : recuperation 20%. Recliquez pour annuler. Clic droit : quitter.";
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalDemolitionOrderGenerator(World world, WorldRenderer renderer) : base(world) { this.renderer = renderer; }
		public static Actor At(World world, Player player, CPos cell) => world.ActorsHavingTrait<MedievalDemolition>()
			.Where(a => MedievalDemolition.Owned(a, player) && !world.FogObscures(a) && a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location).Contains(cell))
			.OrderBy(a => a.TraitOrDefault<MedievalDecoration>()?.Info.Ground == true).ThenByDescending(a => a.ActorID).FirstOrDefault();
		public static Actor[] InArea(World world, Player player, CPos first, CPos last)
		{
			var area = StockyardArea.Between(first, last);
			return world.ActorsHavingTrait<MedievalDemolition>()
				.Where(a => MedievalDemolition.Owned(a, player) && !world.FogObscures(a)
					&& a.TraitOrDefault<MedievalBurnable>()?.Burning != true
					&& a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location).Any(area.Contains))
				.OrderBy(a => a.ActorID).ToArray();
		}
		public string PreviewText(World world)
		{
			var cell = renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
			if (start is not { } first || first == cell) return Help;
			var targets = InArea(world, world.LocalPlayer, first, cell);
			return $"Zone : {targets.Count(a => !a.Trait<MedievalDemolition>().Requested)} element(s) a demonter. Relachez pour confirmer. Transporteurs / batisseurs : 20%. Clic droit : annuler.";
		}
		public override IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button == CancelButton)
			{
				start = null;
				if (mi.Event == MouseInputEvent.Up) world.CancelInputMode();
				return [];
			}
			if (world.Paused || world.LocalPlayer == null) { start = null; return []; }
			if (mi.Button != ActionButton) return [];
			if (mi.Event == MouseInputEvent.Down) { start = world.Map.Contains(cell) ? cell : null; return []; }
			if (mi.Event != MouseInputEvent.Up || start is not { } first) return [];
			start = null;
			if (first == cell) return OrderInner(world, cell, worldPixel, mi);
			// Freeze the targets in the issued actor orders. Every peer validates ownership
			// when resolving them; local fog and mouse state never affect simulation.
			// Overlapping rectangles only add work, never cancel an existing job.
			return InArea(world, world.LocalPlayer, first, cell).Where(a => !a.Trait<MedievalDemolition>().Requested)
				.Select(a => new Order("MedievalDemolish", world.LocalPlayer.PlayerActor, Target.FromActor(a), false)).ToArray();
		}
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (world.Paused || At(world, world.LocalPlayer, cell) is not { } a) yield break;
			yield return new Order("MedievalDemolish", world.LocalPlayer.PlayerActor, Target.FromActor(a), false) { ExtraData = a.Trait<MedievalDemolition>().Requested ? 1u : 0u };
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			var cell = renderer.Viewport.ViewToWorld(Viewport.LastMousePos); var a = At(world, world.LocalPlayer, cell);
			if (start is { } first && first != cell)
			{
				var area = StockyardArea.Between(first, cell);
				var center = world.Map.CenterOfCell(area.Start);
				var dx = world.Map.CenterOfCell(area.Start + new CVec(1, 0)) - center;
				var dy = world.Map.CenterOfCell(area.Start + new CVec(0, 1)) - center;
				var origin = center - (dx + dy) / 2;
				var corners = new[] { origin, origin + dx * area.Size.X, origin + dx * area.Size.X + dy * area.Size.Y, origin + dy * area.Size.Y };
				// Four edges even for a map-sized drag; do not render every empty tile.
				for (var i = 0; i < 4; i++) yield return new LineAnnotationRenderable(corners[i], corners[(i + 1) % 4], 2, Color.OrangeRed);
				foreach (var target in InArea(world, world.LocalPlayer, first, cell))
					foreach (var c in target.Info.TraitInfo<BuildingInfo>().Tiles(target.Location))
						foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, c, Color.OrangeRed)) yield return line;
				yield break;
			}
			foreach (var c in a != null ? a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location) : new[] { cell })
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, c, a == null ? Color.Gray : Color.OrangeRed)) yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) => world.Map.Contains(cell) ? "ability" : "move-blocked";
	}
}
