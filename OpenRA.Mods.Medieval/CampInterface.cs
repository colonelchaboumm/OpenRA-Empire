using System;
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
	public sealed class MedievalCampLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalCampLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			var p = world.LocalPlayer; if (p == null) return;
			var open = false;
			Actor Selected<T>() => world.Selection.Actors.FirstOrDefault(a => a.Owner == p && MedievalStockpile.Active(a) && a.TraitOrDefault<T>() != null);
			Actor CampCommander() => Selected<MedievalCampArea>() is Actor tent ? world.ActorsHavingTrait<MedievalCommander>()
				.Where(a => a.Owner == p && MedievalStockpile.Active(a) && a.Trait<MedievalCommander>().Tents(a).Contains(tent))
				.OrderBy(a => (a.CenterPosition - tent.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault() : null;
			void Button(string id, Func<string> text, Action click, Func<bool> disabled = null)
			{
				var b = widget.Get<ButtonWidget>("MD_CAMP_" + id); b.GetText = text; b.OnClick = click; b.IsDisabled = () => world.Paused || (disabled?.Invoke() ?? false);
			}
			widget.Get<LabelWidget>("MD_CAMP_CLOCK").GetText = () =>
			{
				var clock = world.WorldActor.Trait<MedievalDayNight>().Clock; var seconds = (clock.Remaining + 24) / 25;
				var season = world.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock;
				var weather = world.WorldActor.Trait<MedievalWeather>();
				var sky = weather.RainPermille > 100 ? weather.Snowing ? "Neige" : weather.Storm.Active ? "Orage" : "Pluie" : "Sec";
				return $"{(clock.Night ? "NUIT" : "JOUR")} {seconds / 60}:{seconds % 60:00} | {sky}"
					+ (season == null ? "" : $"\n{SeasonClock.Name(season.Season)} · An {season.Year} · {(season.Remaining + 1499) / 1500} min restantes");
			};
			Button("OPEN", () => "Campements & repos", () => open = !open);
			Button("CLOSE", () => "Fermer", () => open = false);
			widget.Get("MD_CAMP_PANEL").IsVisible = () => open;
			widget.Get<LabelWidget>("MD_CAMP_TITLE").GetText = () => "PAVILLONS MILITAIRES ET REPOS";
			widget.Get<LabelWidget>("MD_CAMP_HINT").GetText = () => "Jour : 8 min / nuit : 2 min. Chaque saison dure 30 min.\nLes pavillons protègent les soldats de la pluie, de la neige et du froid.";
			widget.Get<LabelWidget>("MD_CAMP_STOCK").GetText = () => $"Laine stockee : {MedievalCampStore.Amount(p, 0)} | Tentes stockees : {MedievalCampStore.Amount(p, 1)}";
			widget.Get<LabelWidget>("MD_CAMP_RECIPE").GetText = () => "1 pavillon = 8 laine + 4 bois. Abrite 6 soldats.\nBergerie : Nourriture. Tisseur de tentes : Militaire.";
			widget.Get<LabelWidget>("MD_CAMP_TARGET").GetText = () => Selected<MedievalTentWorkshop>() is Actor a
				? $"Tisseur selectionne : cible {a.Trait<MedievalTentWorkshop>().Target} tentes | travail {a.Trait<MedievalTentWorkshop>().Progress}/40"
				: "Selectionnez le tisseur pour regler le stock cible (0 a 20).";
			foreach (var delta in new[] { -1, 1 }) Button(delta < 0 ? "LESS" : "MORE", () => delta < 0 ? "-" : "+", () =>
			{
				if (Selected<MedievalTentWorkshop>() is Actor a) world.IssueOrder(new Order("MedievalTentTarget", a, false)
				{ ExtraData = (uint)Math.Clamp(a.Trait<MedievalTentWorkshop>().Target + delta, 0, 20) });
			}, () => Selected<MedievalTentWorkshop>() == null);
			Button("LOAD", () => (Selected<MedievalCampCart>()?.Trait<MedievalCampCart>().LoadTents == true ? "[X] " : "[ ] ") + "Charger des tentes", () =>
			{
				if (Selected<MedievalCampCart>() is Actor a) world.IssueOrder(new Order("MedievalLoadTents", a, false));
			}, () => Selected<MedievalCampCart>() is not Actor a || !MedievalSupplyCart.InMarketZone(a) || a.Trait<MedievalConstructionConvoy>().Active);
			Button("DEPLOY", () => "Installer un pavillon", () =>
			{
				if (Selected<MedievalCommander>() is Actor a) { open = false; world.OrderGenerator = new MedievalTentOrderGenerator(world, worldRenderer, a); }
			}, () => Selected<MedievalCommander>() is not Actor a || a.Trait<MedievalCommander>().Busy || !a.Trait<MedievalCommander>().HasTent(a));
			Button("PACK", () => "Replier le pavillon selectionne", () =>
			{
				if (Selected<MedievalPackTent>() is Actor a) world.IssueOrder(new Order("MedievalPackTent", a, false));
			}, () => Selected<MedievalPackTent>() is not Actor a || !world.ActorsHavingTrait<MedievalCampCart>().Any(c => c.Owner == p && MedievalStockpile.Active(c)
				&& c.IsIdle && MedievalAccess.Adjacent(c, a) && c.Trait<MedievalCampStore>().Stock.Free(1) > 0));
			Button("REST", () => Selected<MedievalCommander>() != null ? "Repos du camp" : Selected<MedievalCampArea>() != null ? "Selectionner le commandant" : Selected<MedievalShelter>() != null ? "Reveiller les occupants" : "Repos des citoyens", () =>
			{
				if (Selected<MedievalCommander>() is Actor commander) { world.IssueOrder(new Order("MedievalCommanderRest", commander, false)); return; }
				if (CampCommander() is Actor leader) { world.Selection.Combine(world, [leader], false, true); open = false; return; }
				if (Selected<MedievalShelter>() is Actor home && !home.Trait<MedievalShelter>().Info.Military) { world.IssueOrder(new Order("MedievalWakeShelter", home, false)); return; }
				foreach (var a in world.Selection.Actors.Where(a => a.Owner == p && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalWorker>() != null))
					world.IssueOrder(new Order("MedievalRest", a, false));
			}, () => Selected<MedievalCommander>() is Actor commander ? !commander.Trait<MedievalCommander>().Tents(commander).Any()
				: Selected<MedievalCampArea>() != null ? CampCommander() == null
				: Selected<MedievalWorker>() == null && Selected<MedievalShelter>()?.Trait<MedievalShelter>().Info.Military != false);
			widget.Get<LabelWidget>("MD_CAMP_STATUS").GetText = () => Selected<MedievalCampCart>() is Actor cart
				? $"Charrette : {cart.Trait<MedievalCampStore>().Stock.Amount(1)}/4 tentes.\nChargement dans la zone du marche par les transporteurs."
				: Selected<MedievalShelter>() is Actor shelter ? $"Places reservees / occupees : {shelter.Trait<MedievalShelter>().Occupants(shelter)}/{shelter.Trait<MedievalShelter>().Info.Beds}.\n"
					+ (shelter.TraitOrDefault<MedievalCampArea>() is { } area ? (area.RestEnabled ? "Cercle vert : repos autorise." : "Cercle dore : attend le commandant.") : "Les citoyens rentrent a la nuit.")
				: Selected<MedievalRest>() is Actor unit ? unit.Trait<MedievalRest>().Description + "\nUn ordre de deplacement interrompt le repos."
				: "Chargez les charrettes, puis selectionnez votre commandant.\nIl monte les pavillons et ordonne le repos dans leurs cercles.";
			widget.Get("MD_COMMAND_PANEL").IsVisible = () => !open && Selected<MedievalCommander>() != null;
			widget.Get<LabelWidget>("MD_COMMAND_TITLE").GetText = () => "COMMANDANT - CAMPEMENT";
			widget.Get<LabelWidget>("MD_COMMAND_HINT").GetText = () => "Charrette chargee a moins de 8 cases. Montage sur place.\nRepos : pavillons a moins de 12 cases du commandant.\nChaque cercle vert : 6 cases de rayon, 6 lits.";
			widget.Get<LabelWidget>("MD_COMMAND_STATUS").GetText = () => Selected<MedievalCommander>() is Actor a
				? a.Trait<MedievalCommander>().Status + "\nTente portee : " + a.Trait<MedievalCampStore>().Stock.Amount(1)
					+ (a.Trait<MedievalCommander>().Building ? $" | Montage {a.Trait<MedievalCommander>().Progress}/{a.Trait<MedievalCommander>().Info.BuildTicks}" : "") : "";
			var deploy = widget.Get<ButtonWidget>("MD_COMMAND_DEPLOY");
			deploy.GetText = () => "Installer un pavillon depuis la charrette";
			deploy.IsDisabled = widget.Get<ButtonWidget>("MD_CAMP_DEPLOY").IsDisabled;
			deploy.OnClick = widget.Get<ButtonWidget>("MD_CAMP_DEPLOY").OnClick;
			foreach (var pair in new[] { ("REST", "Repos du camp", "MedievalCommanderRest"), ("WAKE", "Fin du repos", "MedievalCommanderWake") })
			{
				var b = widget.Get<ButtonWidget>("MD_COMMAND_" + pair.Item1); b.GetText = () => pair.Item2;
				b.IsDisabled = () => world.Paused || Selected<MedievalCommander>() is not Actor a || !a.Trait<MedievalCommander>().Tents(a).Any();
				b.OnClick = () => { if (!b.IsDisabled() && Selected<MedievalCommander>() is Actor a) world.IssueOrder(new Order(pair.Item3, a, false)); };
			}
		}
	}
	public sealed class MedievalTentOrderGenerator : OrderGenerator
	{
		readonly Actor commander;
		readonly WorldRenderer renderer;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalTentOrderGenerator(World world, WorldRenderer renderer, Actor commander) : base(world) { this.commander = commander; this.renderer = renderer; }
		bool Valid => MedievalStockpile.Active(commander) && commander.Owner == commander.World.LocalPlayer && !commander.Trait<MedievalCommander>().Busy;
		bool CanDeploy(CPos cell) => Valid && commander.Trait<MedievalCommander>().CanDeploy(commander, cell);
		protected override void Tick(World world) { if (!Valid) world.CancelInputMode(); }
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (!CanDeploy(cell)) yield break;
			yield return new Order("MedievalCommanderDeploy", commander, Target.FromCell(world, cell), false); world.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
		{
			if (Valid) { var cell = renderer.Viewport.ViewToWorld(Viewport.LastMousePos); yield return MedievalPlaceOrderGenerator.Preview(world, "md.militarytent", cell, CanDeploy(cell), renderer: wr); }
		}
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (!Valid) yield break;
			var cell = renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
			foreach (var tile in world.Map.Rules.Actors["md.militarytent"].TraitInfo<BuildingInfo>().Tiles(cell))
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, tile, CanDeploy(cell) ? Color.LimeGreen : Color.Red)) yield return line;
			var center = world.Map.CenterOfCell(cell) + world.Map.Rules.Actors["md.militarytent"].TraitInfo<BuildingInfo>().CenterOffset(world);
			foreach (var line in MedievalFoodProvider.Ring(center, world.Map.Rules.Actors["md.militarytent"].TraitInfo<MedievalCampAreaInfo>().Radius, Color.Goldenrod)) yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) => CanDeploy(cell) ? "ability" : "move-blocked";
	}
}
