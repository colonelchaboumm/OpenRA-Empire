using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalPatrolLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalPatrolLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			var player = world.LocalPlayer;
			Actor[] Fighters() => world.Selection.Actors.Where(a => a.Owner == player && MedievalStockpile.Active(a)
				&& a.TraitOrDefault<MedievalMorale>() != null && a.TraitOrDefault<AutoTarget>() != null).ToArray();
			Actor[] Militia() => Fighters().Where(a => a.TraitOrDefault<MedievalPatrol>() != null).ToArray();
			MedievalPatrolOrderGenerator Draft() => world.OrderGenerator as MedievalPatrolOrderGenerator;
			widget.Get("MD_PATROL_PANEL").IsVisible = () => player != null && Fighters().Length > 0
				&& !(world.Selection.Actors.Count() == 1 && world.Selection.Actors.First().TraitOrDefault<MedievalSaboteur>() != null);
			widget.Get<LabelWidget>("MD_PATROL_TITLE").GetText = () => $"TROUPES - {Fighters().Length} UNITE(S)";
			Actor[] Escorts() => Fighters().Where(a => a.TraitOrDefault<MedievalEscort>() != null).ToArray();
			var escort = widget.Get<ButtonWidget>("MD_ESCORT"); escort.GetText = () => "Escorter : choisir une unité";
			escort.IsDisabled = () => world.Paused || Escorts().Length == 0;
			escort.OnClick = () => { if (!escort.IsDisabled()) world.OrderGenerator = new MedievalEscortOrderGenerator(world, worldRenderer, Escorts()); };
			escort.GetTooltipText = () => "Suivre et protéger une unité";
			escort.GetTooltipDesc = () => "Désignez une charrette, un messager ou une autre unité de votre pays ou alliée.\nLes soldats suivent, combattent selon leur attitude puis rejoignent l'escorte.\nUn ordre manuel remplace l'escorte. Clic droit : annuler le choix.";
			var endEscort = widget.Get<ButtonWidget>("MD_ESCORT_STOP"); endEscort.GetText = () => "Arrêter l'escorte";
			endEscort.IsDisabled = () => world.Paused || !Escorts().Any(a => a.Trait<MedievalEscort>().Protected != null);
			endEscort.OnClick = () => { if (!endEscort.IsDisabled()) foreach (var a in Escorts().Where(a => a.Trait<MedievalEscort>().Protected != null)) world.IssueOrder(new Order("MedievalStopEscort", a, false)); };
			var names = new[] { "Offensive", "Defensive", "Neutre" };
			var stances = new[] { UnitStance.AttackAnything, UnitStance.Defend, UnitStance.HoldFire };
			var tips = new[] { "Engage les ennemis visibles et les poursuit.", "Combat a portee, sans poursuivre l'ennemi.", "Aucune attaque automatique, meme en cas d'agression.\nLes ordres d'attaque manuels restent possibles." };
			for (var i = 0; i < 3; i++)
			{
				var index = i; var button = widget.Get<ButtonWidget>("MD_PATROL_STANCE_" + i);
				button.GetText = () => names[index]; button.GetTooltipText = () => names[index]; button.GetTooltipDesc = () => tips[index];
				button.IsDisabled = () => world.Paused || Fighters().Length == 0;
				button.IsHighlighted = () => Fighters() is { Length: > 0 } actors && actors.All(a => a.Trait<AutoTarget>().PredictedStance == stances[index]);
				button.OnClick = () =>
				{
					if (button.IsDisabled()) return;
					foreach (var a in Fighters()) { a.Trait<AutoTarget>().PredictedStance = stances[index]; world.IssueOrder(new Order("SetUnitStance", a, false) { ExtraData = (uint)stances[index] }); }
				};
			}
			var draw = widget.Get<ButtonWidget>("MD_PATROL_DRAW"); draw.GetText = () => "Tracer une ronde";
			draw.IsDisabled = () => world.Paused || Militia().Length == 0;
			draw.OnClick = () => { if (!draw.IsDisabled()) world.OrderGenerator = new MedievalPatrolOrderGenerator(world, worldRenderer, Militia()); };
			var start = widget.Get<ButtonWidget>("MD_PATROL_START"); start.GetText = () => "Enregistrer ronde";
			start.IsDisabled = () => world.Paused || Draft()?.CanStart != true;
			start.OnClick = () => { if (!start.IsDisabled()) Draft().Start(world); };
			var night = widget.Get<ButtonWidget>("MD_PATROL_NIGHT");
			night.GetText = () =>
			{
				var units = Militia();
				return units.Length == 0 || units.All(a => a.Trait<MedievalPatrol>().NightOnly) ? "Mode : nuit auto"
					: units.All(a => !a.Trait<MedievalPatrol>().NightOnly) ? "Mode : en continu" : "Mode : mixte";
			};
			night.IsHighlighted = () => Militia().Length > 0 && Militia().All(a => a.Trait<MedievalPatrol>().NightOnly);
			night.IsDisabled = () => world.Paused || Militia().Length == 0;
			night.GetTooltipText = () => "Rondes automatiques au crepuscule";
			night.GetTooltipDesc = () => "Nuit auto : depart au crepuscule, pause a l'aube.\nLe parcours reste memorise pour la nuit suivante.\nCliquez pour alterner avec les rondes en continu.";
			night.OnClick = () =>
			{
				if (night.IsDisabled()) return;
				var units = Militia(); var enable = !units.All(a => a.Trait<MedievalPatrol>().NightOnly);
				foreach (var a in units) world.IssueOrder(new Order("MedievalPatrolNightOnly", a, false) { ExtraData = enable ? 1u : 0u });
			};
			var stop = widget.Get<ButtonWidget>("MD_PATROL_STOP"); stop.GetText = () => "Supprimer ronde";
			stop.IsDisabled = () => world.Paused || !Militia().Any(a => a.Trait<MedievalPatrol>().HasRoute);
			stop.OnClick = () =>
			{
				if (stop.IsDisabled()) return;
				foreach (var a in Militia()) world.IssueOrder(new Order("MedievalStopPatrol", a, false));
				if (Draft() != null) world.CancelInputMode();
			};
			widget.Get<LabelWidget>("MD_PATROL_HINT").GetText = () => "Tracez 2 a 16 points, puis Enregistrer ronde.\nNuit auto : depart au crepuscule, pause a l'aube.";
			widget.Get<LabelWidget>("MD_PATROL_STATUS").GetText = () =>
			{
				if (world.OrderGenerator is MedievalEscortOrderGenerator) return "Cliquez sur l'unité à protéger. Clic droit pour annuler.";
				var protecting = Escorts().Count(a => a.Trait<MedievalEscort>().Protected != null);
				if (protecting > 0) return $"{protecting} soldat(s) en escorte. Suit et protège la cible.\nUn déplacement, une attaque manuelle ou Arrêter remplace l'escorte.";
				if (Draft() is { } draft) return $"Trace : {draft.Count}/16 points. " + draft.Message;
				var units = Militia();
				if (units.Length == 0) return "Choisissez une attitude ou une unité à escorter.";
				var resting = units.Count(a => a.Trait<MedievalRest>().Resting);
				var status = units.Length == 1 ? units[0].Trait<MedievalPatrol>().Description
					: $"{units.Count(a => a.Trait<MedievalPatrol>().HasRoute)} parcours memorise(s), {units.Count(a => a.Trait<MedievalPatrol>().Active)} en service";
				var orderedRest = units.Any(a => a.Trait<MedievalRest>() is { Resting: true, OrderedMilitaryRest: true });
				return status + (resting > 0 ? orderedRest ? $"\n{resting} au repos ; ordre du commandant prioritaire."
					: $"\n{resting} au repos ; reveil automatique pour la ronde." : "\nUn ordre manuel supprime la ronde.");
			};
			// Only militia can patrol. Other soldiers keep their stance and escort actions.
			foreach (var id in new[] { "HINT", "DRAW", "START", "NIGHT", "STOP" })
				widget.Get("MD_PATROL_" + id).IsVisible = () => Militia().Length > 0;
			void Layout()
			{
				var patrol = Militia().Length > 0;
				widget.Get("MD_PATROL_PANEL").Bounds.Height = patrol ? 274 : 160;
				widget.Get("MD_ESCORT").Bounds.Y = widget.Get("MD_ESCORT_STOP").Bounds.Y = patrol ? 186 : 72;
				widget.Get("MD_PATROL_STATUS").Bounds.Y = patrol ? 221 : 107;
			}
			layout = Layout;
		}
		readonly Action layout;
		public override void Tick() => layout();
	}
	public sealed class MedievalPatrolOrderGenerator : OrderGenerator
	{
		readonly Actor[] militia;
		readonly WorldRenderer renderer;
		readonly List<CPos> points = [];
		public int Count => points.Count;
		public bool CanStart => points.Distinct().Count() >= 2 && militia.Any(a => SiegeRules.Live(a, a.World.LocalPlayer));
		public string Message { get; private set; } = "Cliquez sur les points de passage.";
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalPatrolOrderGenerator(World world, WorldRenderer renderer, Actor[] militia) : base(world) { this.militia = militia; this.renderer = renderer; }
		bool Valid(World world, CPos cell) => world.Map.Contains(cell) && world.LocalPlayer.Shroud.IsExplored(cell)
			&& militia.Where(a => SiegeRules.Live(a, world.LocalPlayer)).All(a => a.Trait<Mobile>().CanEnterCell(cell, check: BlockedByActor.Immovable));
		public void Start(World world)
		{
			if (world.Paused || !CanStart) return;
			foreach (var a in militia.Where(a => SiegeRules.Live(a, world.LocalPlayer))) world.IssueOrder(new Order("MedievalStartPatrol", a, false) { TargetString = PatrolRoute.Encode(points) });
			world.CancelInputMode();
		}
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (world.Paused) yield break;
			if (!Valid(world, cell)) { Message = "Point bloque ou inexplore."; yield break; }
			if (points.Count >= PatrolRoute.MaximumPoints) { Message = "Limite atteinte : enregistrez la ronde."; yield break; }
			if (points.Count == 0 || points[^1] != cell) points.Add(cell);
			Message = CanStart ? "Enregistrez. Clic droit pour annuler." : "Ajoutez au moins un autre point.";
		}
		protected override void Tick(World world) { if (!militia.Any(a => SiegeRules.Live(a, world.LocalPlayer))) world.CancelInputMode(); }
		protected override void SelectionChanged(World world, IEnumerable<Actor> selected) { if (!selected.Any(militia.Contains)) world.CancelInputMode(); }
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			foreach (var line in RouteLines(world, points, Color.Gold)) yield return line;
			foreach (var p in points)
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, p, Color.Gold)) yield return line;
			var mouse = renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
			if (world.Map.Contains(mouse)) foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, mouse, Valid(world, mouse) ? Color.LimeGreen : Color.Red)) yield return line;
		}
		internal static IEnumerable<IRenderable> RouteLines(World world, IReadOnlyList<CPos> route, Color color)
		{
			if (route.Count < 2) yield break;
			for (var i = 0; i < route.Count; i++)
			{
				var from = world.Map.CenterOfCell(route[i]); var to = world.Map.CenterOfCell(route[(i + 1) % route.Count]);
				for (var dash = 0; dash < 20; dash += 2)
					yield return new LineAnnotationRenderable(WPos.Lerp(from, to, dash, 20), WPos.Lerp(from, to, dash + 1, 20), 1, color);
			}
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) => Valid(world, cell) ? "move" : "move-blocked";
	}
}
