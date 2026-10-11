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
	public sealed class MedievalDecorationOrderGenerator : OrderGenerator
	{
		readonly WorldRenderer renderer;
		CPos? start;
		public string Kind { get; private set; }
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalDecorationOrderGenerator(World world, WorldRenderer renderer, string type) : base(world) { Kind = type; this.renderer = renderer; }
		CPos Cell => renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
		DecorationPlot[] Plan(CPos cell) => MedievalDecorations.Plan(Kind, start ?? cell, cell);
		public void Rotate() { Kind = MedievalDecorations.Rotate(Kind); }
		public string PreviewText(World world)
		{
			if (Kind == "remove") return "Cliquez une decoration : les batisseurs la demontent et recuperent 20% des materiaux. Clic droit : terminer.";
			var plan = Plan(Cell);
			if (plan.Length == 0) return "Trace trop long : 64 cases maximum. Rapprochez le second point.";
			var missing = MedievalDecorations.Missing(world, world.LocalPlayer, plan);
			var costs = missing.Select(p => world.Map.Rules.Actors[p.Type].TraitInfo<MedievalBuildingInfo>().Cost).ToArray();
			var gold = costs.Sum(c => c.ElementAtOrDefault(4));
			return $"{missing.Length} case(s) : {costs.Sum(c => c[0])} bois, {costs.Sum(c => c[3])} pierre" + (gold > 0 ? $", {gold} or. " : ". ")
				+ (MedievalDecorations.Linear(Kind) ? start == null ? "Cliquez le debut puis la fin. " : "Cliquez la fin du trace. " : "Cliquez pour placer. ") + "Clic droit : terminer.";
		}
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (world.Paused) yield break;
			if (Kind == "remove")
			{
				if (MedievalDecorations.At(world, world.LocalPlayer, cell) is Actor a)
					yield return new Order("MedievalRemoveDecoration", world.LocalPlayer.PlayerActor, Target.FromActor(a), false);
				yield break;
			}
			if (MedievalDecorations.Linear(Kind) && start == null) { start = cell; yield break; }
			if (!MedievalDecorations.Valid(world, world.LocalPlayer, Plan(cell))) yield break;
			yield return new Order("MedievalDecorate", world.LocalPlayer.PlayerActor, Target.FromCell(world, start ?? cell), false) { TargetString = Kind, ExtraLocation = cell };
			start = null;
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
		{
			if (Kind == "remove") yield break;
			var plan = Plan(Cell);
			var valid = MedievalDecorations.Valid(world, world.LocalPlayer, plan);
			foreach (var plot in plan)
				yield return MedievalPlaceOrderGenerator.Preview(world, plot.Type, plot.Cell, valid, renderer: wr);
		}
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (Kind == "remove")
			{
				var decoration = MedievalDecorations.At(world, world.LocalPlayer, Cell);
				var cells = decoration != null ? decoration.Info.TraitInfo<BuildingInfo>().Tiles(decoration.Location) : new[] { Cell };
				foreach (var cell in cells)
					foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, cell, Color.OrangeRed)) yield return line;
				yield break;
			}
			var plan = Plan(Cell);
			var valid = MedievalDecorations.Valid(world, world.LocalPlayer, plan);
			foreach (var plot in plan)
				foreach (var cell in world.Map.Rules.Actors[plot.Type].TraitInfo<BuildingInfo>().Tiles(plot.Cell))
					foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, cell, valid ? Color.LimeGreen : Color.Red)) yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) =>
			(Kind == "remove" ? MedievalDecorations.At(world, world.LocalPlayer, cell) != null : MedievalDecorations.Valid(world, world.LocalPlayer, Plan(cell))) ? "ability" : "move-blocked";
	}

	public static class MedievalDecorationInterface
	{
		public static void Bind(Widget widget, World world, WorldRenderer renderer)
		{
			var open = false;
			var entry = widget.Get<ButtonWidget>("MD_DECOR_OPEN");
			entry.GetText = () => "";
			widget.Get<LabelWidget>("MD_DECOR_OPEN_NAME").GetText = () => "Decorations";
			widget.Get<LabelWidget>("MD_DECOR_OPEN_COST").GetText = () => "Embellir";
			entry.OnClick = () => { open = !open || world.Selection.Actors.Any(); world.Selection.Clear(); world.CancelInputMode(); };
			widget.Get("MD_DECOR_PANEL").IsVisible = () => open && !world.Selection.Actors.Any();
			widget.Get<LabelWidget>("MD_DECOR_TITLE").GetText = () => "EMBELLIR LE BOURG";
			var close = widget.Get<ButtonWidget>("MD_DECOR_CLOSE");
			close.GetText = () => "Fermer";
			close.OnClick = () => { open = false; world.CancelInputMode(); };
			var torch = widget.Get<ButtonWidget>("MD_DECOR_TORCH");
			torch.GetText = () => "Torche : 5 bois / 1 pierre - eclairage nocturne";
			torch.IsDisabled = () => world.Paused;
			torch.OnClick = () => { open = false; world.OrderGenerator = new MedievalPlaceOrderGenerator(world, renderer, "md.torch"); };
			for (var i = 0; i < MedievalDecorations.MenuTypes.Length; i++)
			{
				var type = MedievalDecorations.MenuTypes[i]; var name = MedievalDecorations.Names[i];
				var cost = world.Map.Rules.Actors[type].TraitInfo<MedievalBuildingInfo>().Cost;
				var button = widget.Get<ButtonWidget>("MD_DECOR_" + i);
				button.GetText = () => "";
				widget.Get<LabelWidget>($"MD_DECOR_{i}_NAME").GetText = () => name;
				widget.Get<LabelWidget>($"MD_DECOR_{i}_COST").GetText = () => $"{cost[0]} bois / {cost[3]} pierre";
				button.GetTooltipText = () => name;
				button.GetTooltipDesc = () => type == "md.well" ? "Eau pour les secours incendie, jour et nuit. Placez un puits a moins de 16 cases des batiments."
					: type == "md.gardenfence" ? "Deux clics pour une ligne, orientee selon le trace.\nBloque le passage : laissez des ouvertures pour les portillons."
					: type == "md.stonepath" ? "Deux clics pour un chemin, avec un virage si necessaire.\nLes cases deja pavees ne sont pas facturees deux fois."
					: "Decoration traversable, construite par les batisseurs.";
				button.IsDisabled = () => world.Paused;
				button.IsHighlighted = () => world.OrderGenerator is MedievalDecorationOrderGenerator g && (g.Kind == type || MedievalDecorations.Rotatable(type) && g.Kind == type + "y");
				button.OnClick = () => world.OrderGenerator = new MedievalDecorationOrderGenerator(world, renderer, type);
			}
			var rotate = widget.Get<ButtonWidget>("MD_DECOR_ROTATE");
			rotate.GetText = () => "Tourner le portillon / cloture";
			rotate.IsDisabled = () => world.OrderGenerator is not MedievalDecorationOrderGenerator g || !MedievalDecorations.Rotatable(g.Kind);
			rotate.OnClick = () => { if (world.OrderGenerator is MedievalDecorationOrderGenerator g) g.Rotate(); };
			var remove = widget.Get<ButtonWidget>("MD_DECOR_REMOVE");
			remove.GetText = () => "Retirer un decor";
			remove.IsDisabled = () => world.Paused;
			remove.OnClick = () => world.OrderGenerator = new MedievalDemolitionOrderGenerator(world, renderer);
			widget.Get<LabelWidget>("MD_DECOR_HINT").GetText = () => "Clotures : 2 clics. Portillons : passage libre.";
			widget.Get<LabelWidget>("MD_DECOR_HINT2").GetText = () => "Clic droit : terminer la pose. Cout par case.";
		}
	}
}
