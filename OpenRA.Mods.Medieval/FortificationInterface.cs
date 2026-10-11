using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalFortificationOrderGenerator : OrderGenerator
	{
		readonly WorldRenderer renderer;
		CPos? start;
		public string Kind { get; private set; }
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalFortificationOrderGenerator(World world, WorldRenderer renderer, string type) : base(world) { Kind = type; this.renderer = renderer; }
		CPos Cell => renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
		FortificationPlot[] Plan(CPos cell) => MedievalFortifications.Plan(Kind, start ?? cell, cell);
		public void Rotate() { Kind = MedievalFortifications.Rotate(Kind); }
		public string PreviewText(World world)
		{
			var plan = Plan(Cell);
			if (plan.Length == 0) return "Trace trop long : 64 cases maximum.";
			var missing = MedievalFortifications.Missing(world, world.LocalPlayer, plan);
			var wood = missing.Sum(p => world.Map.Rules.Actors[p.Type].TraitInfo<MedievalBuildingInfo>().Cost[0]);
			return $"{missing.Length} chantier(s) : {wood} bois. "
				+ (MedievalFortifications.Linear(Kind) ? start == null ? "Cliquez le debut puis la fin. " : "Cliquez la fin du trace. " : "Cliquez pour placer. ")
				+ "Clic droit : terminer.";
		}
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (world.Paused) yield break;
			if (MedievalFortifications.Linear(Kind) && start == null) { start = cell; yield break; }
			if (!MedievalFortifications.Valid(world, world.LocalPlayer, Plan(cell))) yield break;
			yield return new Order("MedievalFortify", world.LocalPlayer.PlayerActor, Target.FromCell(world, start ?? cell), false) { TargetString = Kind, ExtraLocation = cell };
			start = null;
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
		{
			var plan = Plan(Cell);
			var valid = MedievalFortifications.Valid(world, world.LocalPlayer, plan);
			foreach (var plot in plan)
				yield return MedievalPlaceOrderGenerator.Preview(world, plot.Type, plot.Cell, valid,
					MedievalFortifications.Linear(plot.Type) ? plot.Type.EndsWith('y') ? 5 : 10 : 0);
		}
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			var plan = Plan(Cell);
			var valid = MedievalFortifications.Valid(world, world.LocalPlayer, plan);
			var color = valid ? Color.LimeGreen : Color.Red;
			foreach (var plot in plan)
			{
				var building = world.Map.Rules.Actors[plot.Type].TraitInfo<BuildingInfo>();
				foreach (var c in building.Tiles(plot.Cell))
					foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, c, color)) yield return line;
				if (plot.Type == "md.woodtower")
					foreach (var line in MedievalFoodProvider.Ring(world.Map.CenterOfCell(plot.Cell) + building.CenterOffset(world), new WDist(7 * 1024), color)) yield return line;
			}
			foreach (var a in world.Actors.Where(a => a.Owner == world.LocalPlayer && MedievalStockpile.Active(a)
				&& a.TraitOrDefault<MedievalConstructionSupply>() != null))
				foreach (var line in MedievalFoodProvider.Ring(a.CenterPosition, a.Trait<MedievalConstructionSupply>().Info.Radius, Color.CornflowerBlue)) yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) =>
			MedievalFortifications.Valid(world, world.LocalPlayer, Plan(cell)) ? "ability" : "move-blocked";
	}

	public static class MedievalFortificationInterface
	{
		public static void Bind(Widget widget, World world, WorldRenderer renderer)
		{
			Actor Tower() => world.Selection.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead && a.TraitOrDefault<MedievalTowerGarrison>() != null);
			widget.Get("MD_TOWER_PANEL").IsVisible = () => Tower() != null;
			widget.Get<LabelWidget>("MD_TOWER_TITLE").GetText = () => Tower()?.Trait<MedievalTowerGarrison>().Description ?? "";
			widget.Get<LabelWidget>("MD_TOWER_HINT").GetText = () => "Selectionnez vos archers, puis clic droit sur la tour.\n2 places : archers ordinaires ou d'elite.\nPortee : 7,5 / 9 cases. Vision occupee : 12 cases.\nLa garnison a toujours besoin de nourriture.";
			var unload = widget.Get<ButtonWidget>("MD_TOWER_UNLOAD");
			unload.GetText = () => "Faire descendre les archers";
			unload.IsDisabled = () => world.Paused || Tower() is not Actor a || !a.Trait<Cargo>().CanUnload();
			unload.OnClick = () => { if (Tower() is Actor a) world.IssueOrder(new Order("Unload", a, false)); };
			var open = false;
			var entry = widget.Get<ButtonWidget>("MD_FORT_OPEN");
			entry.GetText = () => "Defenses";
			entry.OnClick = () =>
			{
				open = !open || world.Selection.Actors.Any();
				widget.Get<ButtonWidget>("MD_DECOR_CLOSE").OnClick();
				world.Selection.Clear(); world.CancelInputMode();
			};
			var decorEntry = widget.Get<ButtonWidget>("MD_DECOR_OPEN");
			var openDecor = decorEntry.OnClick;
			decorEntry.OnClick = () => { open = false; openDecor(); };
			widget.Get("MD_FORT_PANEL").IsVisible = () => open && !world.Selection.Actors.Any();
			widget.Get<LabelWidget>("MD_FORT_TITLE").GetText = () => "FORTIFICATIONS ET SIEGE";
			var close = widget.Get<ButtonWidget>("MD_FORT_CLOSE");
			close.GetText = () => "Fermer";
			close.OnClick = () => { open = false; world.CancelInputMode(); };
			for (var i = 0; i < MedievalFortifications.MenuTypes.Length; i++)
			{
				var type = MedievalFortifications.MenuTypes[i]; var name = MedievalFortifications.Names[i];
				var cost = world.Map.Rules.Actors[type].TraitInfo<MedievalBuildingInfo>().Cost[0];
				var button = widget.Get<ButtonWidget>("MD_FORT_" + i);
				button.GetText = () => "";
				widget.Get<LabelWidget>($"MD_FORT_{i}_NAME").GetText = () => name;
				widget.Get<LabelWidget>($"MD_FORT_{i}_COST").GetText = () => $"{cost} bois" + (MedievalFortifications.Linear(type) ? " par case" : "");
				button.GetTooltipText = () => name;
				button.GetTooltipDesc = () => type == "md.palisade" ? "Deux clics pour une ligne. Angles et raccords automatiques.\nBloque le passage. Laissez trois cases libres pour la porte."
					: type == "md.woodgate" ? "Porte large de 3 cases, a deux battants lateraux.\nS'ouvre pour les allies, puis se referme. Tourner change son axe."
					: "Tour 2x2 : defense automatique a 7 cases et 2 places d'archers.\nClic droit avec des archers : monter. Portee +50%, vision 12 cases.\nLa defense et la garnison exigent une construction terminee.";
				button.IsDisabled = () => world.Paused;
				button.IsHighlighted = () => world.OrderGenerator is MedievalFortificationOrderGenerator g && (g.Kind == type || g.Kind == type + "y");
				button.OnClick = () => world.OrderGenerator = new MedievalFortificationOrderGenerator(world, renderer, type);
			}
			var rotate = widget.Get<ButtonWidget>("MD_FORT_ROTATE");
			rotate.GetText = () => "Tourner la porte / palissade";
			rotate.IsDisabled = () => world.OrderGenerator is not MedievalFortificationOrderGenerator g || !MedievalFortifications.Rotatable(g.Kind);
			rotate.OnClick = () => { if (world.OrderGenerator is MedievalFortificationOrderGenerator g) g.Rotate(); };
			widget.Get<LabelWidget>("MD_FORT_HINT").GetText = () => "Bois livre par les batisseurs. Clic droit : terminer.";
			widget.Get<LabelWidget>("MD_FORT_HINT2").GetText = () => "Porte : laissez trois cases libres dans la palissade.";
		}
	}
}
