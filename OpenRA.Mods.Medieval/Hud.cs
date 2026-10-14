using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	// Presentation state belongs to a world, never to the synchronized simulation.
	public sealed class MedievalHudState
	{
		static readonly ConditionalWeakTable<World, MedievalHudState> States = new();
		public bool AllStocks;
		public readonly HashSet<Actor> Hovered = [];
		public static MedievalHudState For(World world) => States.GetOrCreateValue(world);
		public static bool StockVisible(Actor actor) => actor.World.Selection.Contains(actor)
			|| For(actor.World).AllStocks || For(actor.World).Hovered.Contains(actor);
	}

	public sealed class MedievalHudLogic : ChromeLogic
	{
		sealed record Page(Widget Panel, string Name, Func<bool> Available);
		readonly World world;
		readonly WorldRenderer renderer;
		readonly Widget root;
		readonly List<Page> pages = [];
		readonly Widget[] overlays;
		readonly MedievalHudState state;
		Actor[] selection = [];
		string chosen;
		int pageOffset;
		int cachedSelection = -1, cachedTick = -1;
		Page[] cachedPages = [];
		bool collapsed, foodOpen;
		Widget previousPanel;
		Page[] Available()
		{
			if (cachedSelection != world.Selection.Hash || cachedTick != world.WorldTick)
			{
				cachedSelection = world.Selection.Hash; cachedTick = world.WorldTick;
				cachedPages = pages.Where(p => p.Available()).ToArray();
			}
			return cachedPages;
		}
		Page Current() => Available().FirstOrDefault(p => p.Panel.Id == chosen) ?? Available().FirstOrDefault();
		bool OverlayOpen() => overlays.Any(p => p.IsVisible());

		[ObjectCreator.UseCtor]
		public MedievalHudLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			root = widget; this.world = world; renderer = worldRenderer; state = MedievalHudState.For(world);
			Actor[] Workers() => world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalWorker>() != null).ToArray();
			bool Site() => world.Selection.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalBuilding>()?.Complete == false);
			widget.Get("MD_WORK_PANEL").IsVisible = () => Workers().Length > 0 || Site();
			widget.Get<LabelWidget>("MD_WORK_TITLE").GetText = () => "TRAVAIL ET CHANTIERS";
			widget.Get<LabelWidget>("MD_WORK_HINT").GetText = () => "Reprendre les tâches automatiques des ouvriers sélectionnés.\nAnnuler uniquement les chantiers sélectionnés.";
			foreach (var (id, parent, x, y, width) in new[] {
				("MD_CART_CREATE", "MD_TRADE_PANEL", 9, 326, 392), ("MD_CART_RELOAD", "MD_CART_PANEL", 9, 178, 392),
				("MD_RESUME", "MD_WORK_PANEL", 9, 32, 192), ("MD_CANCEL", "MD_WORK_PANEL", 207, 32, 194) })
			{
				var button = widget.Get(id); button.Parent.RemoveChild(button); widget.Get(parent).AddChild(button);
				button.Bounds.X = x; button.Bounds.Y = y; button.Bounds.Width = width;
			}
			widget.Get("MD_TRADE_PANEL").Bounds.Height = 360; widget.Get("MD_CART_PANEL").Bounds.Height = 212;
			widget.Get<ButtonWidget>("MD_RESUME").IsDisabled = () => world.Paused || Workers().Length == 0;
			widget.Get<ButtonWidget>("MD_CANCEL").IsDisabled = () => world.Paused || !Site();
			// Priority selects the most useful action page, with secondary actions in tabs.
			foreach (var (id, name) in new[] {
				("MD_FOUNDING_PANEL", "Fondation"), ("MD_MAIL_PANEL", "Messager"),
				("MD_SABOTEUR_PANEL", "Saboteur"), ("MD_COMMAND_PANEL", "Commandant"),
				("MD_PATROL_PANEL", "Troupes"), ("MD_SIEGE_UNIT", "Engin"), ("MD_CREW_PANEL", "Équipage"),
				("MD_TOWER_PANEL", "Garnison"), ("MD_CHURCH_PANEL", "Église"),
				("MD_BARRACKS_PANEL", "Recrutement"), ("MD_FORGE_PANEL", "Forge"),
				("MD_SIEGE_WORKSHOP", "Atelier"), ("MD_YARD_PANEL", "Entrepôt"),
				("MD_STALL_PANEL", "Relais"), ("MD_TRADE_PANEL", "Commerce"),
				("MD_TRADER_PANEL", "Charrette"), ("MD_CART_PANEL", "Ravitaillement"),
				("MD_STONE_PANEL", "Pierres"), ("MD_HERD_PANEL", "Troupeau"), ("MD_LOCALITY_PANEL", "Localité"), ("MD_WORK_PANEL", "Travail") })
			{
				var panel = widget.Get(id);
				var page = new Page(panel, name, panel.IsVisible); pages.Add(page);
				panel.Bounds.Y = 108;
				panel.IsVisible = () => !collapsed && !OverlayOpen() && Current() == page;
			}
			var details = widget.Get("MD_FOOD_DETAILS");
			widget.Get<LabelWidget>("MD_FOOD_DETAILS_TITLE").GetText = () => "BILAN DU RAVITAILLEMENT";
			details.IsVisible = () => foodOpen && world.LocalPlayer != null;
			foreach (var (id, y) in new[] { ("MD_FOOD_FLOW", 32), ("MD_FOOD_RESERVE", 54), ("MD_FOOD_DEATHS", 76) })
			{
				var label = widget.Get(id); label.Parent.RemoveChild(label); details.AddChild(label); label.Bounds.Y = y;
			}
			widget.Get("MD_FOOD_TITLE").IsVisible = () => false;
			var food = widget.Get("MD_FOOD_PANEL"); food.Bounds.Height = 62;
			widget.Get("MD_FOOD_BALANCE").Bounds.Y = 23;
			widget.Get("MD_FOOD_STATUS").Bounds.Y = 41;
			var foodToggle = widget.Get<ButtonWidget>("MD_FOOD_TOGGLE");
			foodToggle.GetText = () => foodOpen ? "Ravitaillement −" : "Ravitaillement +";
			foodToggle.OnClick = () => foodOpen = !foodOpen;
			foodToggle.IsHighlighted = () => foodOpen;
			var foodClose = widget.Get<ButtonWidget>("MD_FOOD_CLOSE");
			foodClose.GetText = () => "Fermer"; foodClose.OnClick = () => foodOpen = false;
			overlays = new[] { "MD_FOOD_DETAILS", "MD_PRIDE_PANEL", "MD_PLACES_PANEL", "MD_CAMP_PANEL", "MD_HERALDRY_PANEL", "MD_BARTER_PANEL", "MD_DECOR_PANEL", "MD_FORT_PANEL" }.Select(widget.Get).ToArray();
			var influenceLegend = widget.Get("MD_INFLUENCE_LEGEND");
			var influenceVisible = influenceLegend.IsVisible;
			influenceLegend.IsVisible = () => influenceVisible() && !OverlayOpen();
			foreach (var prefix in new[] { "MD_FOOD", "MD_PRIDE", "MD_PLACES", "MD_CAMP", "MD_HERALDRY", "MD_DECOR", "MD_FORT" })
			{
				var toggle = widget.Get<ButtonWidget>(prefix == "MD_FOOD" ? "MD_FOOD_TOGGLE" : prefix + "_OPEN");
				var action = toggle.OnClick;
				toggle.OnClick = () =>
				{
					foreach (var other in new[] { "MD_FOOD", "MD_PRIDE", "MD_PLACES", "MD_CAMP", "MD_HERALDRY", "MD_DECOR", "MD_FORT", "MD_BARTER" })
						if (other != prefix && widget.Get(other == "MD_FOOD" ? "MD_FOOD_DETAILS" : other + "_PANEL").IsVisible())
							widget.Get<ButtonWidget>(other + "_CLOSE").OnClick();
					action(); ReleaseHiddenFocus();
				};
			}
			foreach (var id in new[] { "MD_PRIDE_PANEL", "MD_PLACES_PANEL", "MD_DECOR_PANEL", "MD_FORT_PANEL" }) widget.Get(id).Bounds.Y = 108;
			foreach (var id in new[] { "MD_CAMP_PANEL", "MD_HERALDRY_PANEL" })
			{
				var panel = widget.Get(id); panel.Bounds.X = (Game.Renderer.Resolution.Width - panel.Bounds.Width) / 2; panel.Bounds.Y = 108;
			}
			var dock = widget.Get("MD_CONTEXT_TABS");
			dock.IsVisible = () => world.LocalPlayer != null && !OverlayOpen() && Available().Length > 0;
			for (var i = 0; i < 3; i++)
			{
				var slot = i; var tab = dock.Get<ButtonWidget>("MD_CONTEXT_TAB_" + i);
				Page At() => Available().Skip(pageOffset + slot).FirstOrDefault();
				tab.IsVisible = () => At() != null; tab.GetText = () => At()?.Name ?? "";
				tab.IsHighlighted = () => !collapsed && At() == Current();
				tab.OnClick = () => { chosen = At()?.Panel.Id; collapsed = false; ReleaseHiddenFocus(); };
			}
			var next = dock.Get<ButtonWidget>("MD_CONTEXT_NEXT"); next.GetText = () => "›";
			next.IsVisible = () => Available().Length > 3;
			next.OnClick = () => pageOffset = pageOffset + 3 >= Available().Length ? 0 : pageOffset + 3;
			var collapse = dock.Get<ButtonWidget>("MD_CONTEXT_COLLAPSE"); collapse.GetText = () => collapsed ? "+" : "−";
			collapse.OnClick = () => { collapsed = !collapsed; ReleaseHiddenFocus(); };
			collapse.GetTooltipText = () => collapsed ? "Afficher les commandes" : "Replier les commandes";
			var stocks = widget.Get<ButtonWidget>("MD_STOCKS_TOGGLE");
			stocks.GetText = () => state.AllStocks ? "Stocks : tous" : "Stocks : sélection";
			stocks.IsHighlighted = () => state.AllStocks;
			stocks.OnClick = () => state.AllStocks = !state.AllStocks;
			stocks.GetTooltipText = () => "Étiquettes de ressources sur la carte";
			stocks.GetTooltipDesc = () => "Par défaut : sélection et survol. Cliquez pour afficher tous les stocks.\nLes alertes de faim restent visibles dans les deux modes.";
		}

		void ReleaseHiddenFocus()
		{
			var next = !collapsed && !OverlayOpen() ? Current()?.Panel : null;
			if (previousPanel != next) previousPanel?.Hidden();
			previousPanel = next;
		}

		public override void Tick()
		{
			cachedTick = -1;
			var selected = world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).ToArray();
			if (!selection.SequenceEqual(selected)) { selection = selected; chosen = null; pageOffset = 0; collapsed = false; }
			if (pageOffset >= Available().Length) pageOffset = 0;
			ReleaseHiddenFocus();
			// Size the catalogue to the current category instead of keeping a tall empty background.
			var bottom = Enumerable.Range(0, 8).Select(i => root.Get("MD_MENU_ITEM_" + i)).Where(b => b.IsVisible()).Max(b => b.Bounds.Bottom) + 6;
			foreach (var id in new[] { "MD_MENU_PREV", "MD_MENU_PAGE", "MD_MENU_NEXT" }) root.Get(id).Bounds.Y = bottom;
			if (root.Get("MD_MENU_NEXT").IsVisible()) bottom += 26;
			root.Get("MD_MENU_HINT").Bounds.Y = bottom; bottom += 34;
			root.Get("MD_MENU_ROTATE").Bounds.Y = root.Get("MD_MENU_REMOVE").Bounds.Y = bottom; bottom += 30;
			root.Get("MD_MENU_STOCK").Bounds.Y = bottom;
			root.Get("MD_PANEL").Bounds.Height = bottom + 36;
			state.Hovered.Clear();
			if (Ui.MouseOverWidget is WorldInteractionControllerWidget or ViewportControllerWidget)
				foreach (var hit in world.ScreenMap.ActorsAtMouse(renderer.Viewport.ViewToWorldPx(Viewport.LastMousePos)))
					if (hit.Actor.IsInWorld && !hit.Actor.IsDead && !world.FogObscures(hit.Actor)) state.Hovered.Add(hit.Actor);
		}
	}
}
