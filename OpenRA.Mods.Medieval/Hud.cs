using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
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
		readonly ScrollPanelWidget sidebar;
		readonly Widget occupancy;
		MedievalOccupancy occupancyReport;
		Actor occupancyActor;
		int occupancyTick = -1;
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
			var right = Game.Renderer.Resolution.Width - 446;
			sidebar = new ScrollPanelWidget(Game.ModData) { Id = "MD_CONTEXT_SCROLL", Bounds = new WidgetBounds(right, 354, 434, Game.Renderer.Resolution.Height - 366), Background = "md-panel" };
			widget.AddChild(sidebar);
			var transfer = new MedievalTransferWidget(world, worldRenderer); widget.AddChild(transfer); transfer.IsVisible = () => transfer.Available;
			var overview = new BackgroundWidget { Id = "MD_OVERVIEW", Bounds = new WidgetBounds(right, 274, 434, 46), Background = "md-panel", IsVisible = () => world.LocalPlayer != null && !OverlayOpen() };
			widget.AddChild(overview);
			foreach (var id in Enumerable.Range(0, 5).Select(i => "MD_RESOURCE_" + i).Concat(Enumerable.Range(0, 4).Select(i => "MD_RESOURCE_ICON_" + i)).Append("MD_POP"))
			{
				var child = widget.Get(id); child.Parent.RemoveChild(child); overview.AddChild(child);
				var icon = id.Contains("ICON", StringComparison.Ordinal); var index = id == "MD_POP" ? 5 : id[^1] - '0';
				child.Bounds.X = 9 + index % 3 * 140 + (icon || index >= 4 ? 0 : 20);
				child.Bounds.Y = 2 + index / 3 * 21 + (icon ? 3 : 0);
				if (!icon) child.Bounds.Width = index >= 4 ? 134 : 114;
			}
			for (var index = 4; index <= 5; index++)
			{
				var i = index;
				MedievalMenuIcons.Add(overview, renderer, "MD_OVERVIEW_ICON_" + i, 9 + i % 3 * 140, 26, 17,
					() => i == 4 ? MedievalMenuIcons.Good(world, 4) : MedievalMenuIcons.Entry(world, "md.builder"), () => "md-livery" + world.LocalPlayer?.InternalName);
				var label = overview.Get<LabelWidget>(i == 4 ? "MD_RESOURCE_4" : "MD_POP"); label.Bounds.X += 20; label.Bounds.Width = 114;
			}
			overview.Get<LabelWidget>("MD_POP").GetText = () => world.LocalPlayer is { } player ? $"Pop. : {MedievalSettlement.Population(player)}/{MedievalSettlement.Housing(player)}" : "";
			var info = new BackgroundWidget { Id = "MD_BUILDING_INFO", Bounds = new WidgetBounds(0, 0, 410, 94), Background = "md-panel" };
			Actor Building() => world.Selection.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalBuilding>() != null);
			occupancy = new BackgroundWidget { Id = "MD_OCCUPANCY", Bounds = new WidgetBounds(right, 324, 434, 54), Background = "md-panel" };
			occupancy.IsVisible = () => occupancyReport != null && occupancyActor?.Owner == world.LocalPlayer && occupancyActor.IsInWorld && !occupancyActor.IsDead
				&& world.Selection.Contains(occupancyActor) && !collapsed && !OverlayOpen();
			MedievalMenuIcons.Add(occupancy, renderer, "MD_OCCUPANCY_ICON", 9, 10, 30,
				() => MedievalMenuIcons.Entry(world, "md.house"), () => "md-livery" + world.LocalPlayer?.InternalName);
			MedievalTransferWidget.Label(occupancy, "MD_OCCUPANCY_RATE", 46, 3, 378, 24, () => occupancyReport?.Title ?? "");
			MedievalTransferWidget.Label(occupancy, "MD_OCCUPANCY_DETAILS", 46, 27, 378, 21, () => occupancyReport?.Details ?? "");
			widget.AddChild(occupancy);
			info.IsVisible = () => Building() != null;
			MedievalTransferWidget.Label(info, "MD_BUILDING_NAME", 9, 8, 392, 24, () => Building() is { } a ? (MedievalConstructionMenuLogic.Categories.SelectMany(c => c.Items).FirstOrDefault(e => e.Type == a.Info.Name).Name ?? "Bâtiment") + " #" + a.ActorID : "");
			MedievalTransferWidget.Label(info, "MD_BUILDING_STATE", 9, 36, 392, 48, () => Building()?.Trait<MedievalBuilding>().Complete == true ? "Bâtiment en service.\nSes commandes sont accessibles dans les onglets." : "Chantier en cours. Les bâtisseurs apportent les matériaux.");
			widget.AddChild(info);
			// Keep the global camp report, and expose its production/rest controls at the selected building too.
			Widget CopyControl(string id, Widget parent, int x, int y, int width)
			{
				var source = widget.Get(id); var copy = source.Clone(); copy.Id = id + "_CONTEXT";
				copy.Bounds.X = x; copy.Bounds.Y = y; copy.Bounds.Width = width;
				if (source is ButtonWidget button && copy is ButtonWidget duplicate) { duplicate.OnClick = button.OnClick; duplicate.IsDisabled = button.IsDisabled; }
				parent.AddChild(copy); return copy;
			}
			var textile = new BackgroundWidget { Id = "MD_TEXTILE_PANEL", Bounds = new WidgetBounds(0, 0, 410, 242), Background = "md-panel", IsVisible = () => Building()?.TraitOrDefault<MedievalTentWorkshop>() != null };
			MedievalTransferWidget.Label(textile, "MD_TEXTILE_TITLE", 9, 6, 392, 24, () => "TISSEUR — PRODUCTION");
			CopyControl("MD_CAMP_RECIPE", textile, 9, 38, 392);
			CopyControl("MD_CAMP_CLOTH_TARGET", textile, 9, 96, 392);
			CopyControl("MD_CAMP_CLOTH_LESS", textile, 9, 122, 192); CopyControl("MD_CAMP_CLOTH_MORE", textile, 207, 122, 194);
			CopyControl("MD_CAMP_TARGET", textile, 9, 170, 392);
			CopyControl("MD_CAMP_LESS", textile, 9, 196, 192); CopyControl("MD_CAMP_MORE", textile, 207, 196, 194);
			widget.AddChild(textile);
			var shelter = new BackgroundWidget { Id = "MD_SHELTER_PANEL", Bounds = new WidgetBounds(0, 0, 410, 182), Background = "md-panel", IsVisible = () => Building()?.TraitOrDefault<MedievalShelter>() != null };
			MedievalTransferWidget.Label(shelter, "MD_SHELTER_TITLE", 9, 6, 392, 24, () => "HÉBERGEMENT ET REPOS");
			CopyControl("MD_CAMP_STATUS", shelter, 9, 38, 392);
			CopyControl("MD_CAMP_REST", shelter, 9, 104, 392);
			CopyControl("MD_CAMP_PACK", shelter, 9, 140, 392).IsVisible = () => Building()?.TraitOrDefault<MedievalPackTent>() != null;
			widget.AddChild(shelter);
			Actor[] Workers() => world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalWorker>() != null).ToArray();
			bool Site() => world.Selection.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalBuilding>()?.Complete == false);
			widget.Get("MD_WORK_PANEL").IsVisible = () => Workers().Length > 0 || Site();
			widget.Get<LabelWidget>("MD_WORK_TITLE").GetText = () => "TRAVAIL ET CHANTIERS";
			widget.Get<LabelWidget>("MD_WORK_HINT").GetText = () => "Clic droit : affecter à un chantier ou lieu de travail.\nTravail automatique : libérer l’affectation.";
			widget.Get("MD_WORK_PANEL").Bounds.Height = 138;
			MedievalTransferWidget.Label(widget.Get("MD_WORK_PANEL"), "MD_WORK_ASSIGNMENT", 9, 106, 392, 24, () =>
			{
				var workers = Workers().Select(a => a.Trait<MedievalWorker>()).ToArray();
				return workers.Length == 1 ? workers[0].AssignmentDescription : $"{workers.Count(w => w.AssignedWorkplace != null)}/{workers.Length} ouvriers affectés";
			});
			foreach (var (id, parent, x, y, width) in new[] {
				("MD_CART_CREATE", "MD_TRADE_PANEL", 9, 326, 392), ("MD_CART_RELOAD", "MD_CART_PANEL", 9, 178, 392),
				("MD_RESUME", "MD_WORK_PANEL", 9, 32, 192), ("MD_CANCEL", "MD_WORK_PANEL", 207, 32, 194) })
			{
				var button = widget.Get(id); button.Parent.RemoveChild(button); widget.Get(parent).AddChild(button);
				button.Bounds.X = x; button.Bounds.Y = y; button.Bounds.Width = width;
			}
			widget.Get("MD_TRADE_PANEL").Bounds.Height = 360; widget.Get("MD_CART_PANEL").Bounds.Height = 212;
			widget.Get<ButtonWidget>("MD_RESUME").IsDisabled = () => world.Paused || Workers().Length == 0;
			widget.Get<ButtonWidget>("MD_RESUME").GetText = () => "Travail automatique";
			widget.Get<ButtonWidget>("MD_CANCEL").IsDisabled = () => world.Paused || !Site();
			// Priority selects the most useful action page, with secondary actions in tabs.
			foreach (var (id, name) in new[] {
				("MD_TEXTILE_PANEL", "Production"), ("MD_PANEL", "Actions"),
				("MD_FOUNDING_PANEL", "Fondation"), ("MD_MAIL_PANEL", "Messager"),
				("MD_SABOTEUR_PANEL", "Saboteur"), ("MD_COMMAND_PANEL", "Commandant"),
				("MD_PATROL_PANEL", "Troupes"), ("MD_SIEGE_UNIT", "Engin"), ("MD_CREW_PANEL", "Équipage"),
				("MD_TOWER_PANEL", "Garnison"), ("MD_CHURCH_PANEL", "Église"),
				("MD_BARRACKS_PANEL", "Recrutement"), ("MD_FORGE_PANEL", "Forge"),
				("MD_SIEGE_WORKSHOP", "Atelier"), ("MD_TRANSFER_PANEL", "Stocks"), ("MD_YARD_PANEL", "Entrepôt"),
				("MD_STALL_PANEL", "Relais"), ("MD_TRADE_PANEL", "Commerce"),
				("MD_TRADER_PANEL", "Charrette"), ("MD_CART_PANEL", "Ravitaillement"),
				("MD_STONE_PANEL", "Pierres"), ("MD_HERD_PANEL", "Troupeau"), ("MD_SHELTER_PANEL", "Repos"), ("MD_LOCALITY_PANEL", "Localité"), ("MD_WORK_PANEL", "Travail"), ("MD_BUILDING_INFO", "Bâtiment") })
			{
				var panel = widget.Get(id);
				var page = new Page(panel, name, panel.IsVisible); pages.Add(page);
				panel.Parent.RemoveChild(panel); sidebar.AddChild(panel); panel.Bounds.X = 0;
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
			overlays = new[] { "MD_FOOD_DETAILS", "MD_PRIDE_PANEL", "MD_PLACES_PANEL", "MD_CAMP_PANEL", "MD_HERALDRY_PANEL", "MD_BARTER_PANEL", "MD_DIPLOMACY_PANEL", "MD_DECOR_PANEL", "MD_FORT_PANEL" }.Select(widget.Get).ToArray();
			var influenceLegend = widget.Get("MD_INFLUENCE_LEGEND");
			var influenceVisible = influenceLegend.IsVisible;
			influenceLegend.IsVisible = () => influenceVisible() && !OverlayOpen();
			foreach (var prefix in new[] { "MD_FOOD", "MD_PRIDE", "MD_PLACES", "MD_CAMP", "MD_HERALDRY", "MD_DIPLOMACY", "MD_DECOR", "MD_FORT" })
			{
				var toggle = widget.Get<ButtonWidget>(prefix == "MD_FOOD" ? "MD_FOOD_TOGGLE" : prefix + "_OPEN");
				var action = toggle.OnClick;
				toggle.OnClick = () =>
				{
					foreach (var other in new[] { "MD_FOOD", "MD_PRIDE", "MD_PLACES", "MD_CAMP", "MD_HERALDRY", "MD_DIPLOMACY", "MD_DECOR", "MD_FORT", "MD_BARTER" })
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
			dock.Bounds.X = right; dock.Bounds.Y = 324; dock.Bounds.Width = 434;
			sidebar.CollapseHiddenChildren = true;
			sidebar.IsVisible = () => world.LocalPlayer != null && !collapsed && !OverlayOpen() && Current() != null;
			dock.IsVisible = () => world.LocalPlayer != null && !OverlayOpen() && Available().Length > 0;
			for (var i = 0; i < 3; i++)
			{
				var slot = i; var tab = dock.Get<ButtonWidget>("MD_CONTEXT_TAB_" + i);
				tab.Bounds.X = i * 122; tab.Bounds.Width = 118; tab.Align = TextAlign.Left; tab.LeftMargin = 25;
				Page At() => Available().Skip(pageOffset + slot).FirstOrDefault();
				string IconType() => At()?.Panel.Id switch
				{
					"MD_TRANSFER_PANEL" or "MD_YARD_PANEL" => "md.warehouse",
					"MD_TRADE_PANEL" or "MD_STALL_PANEL" or "MD_TRADER_PANEL" or "MD_CART_PANEL" => "md.market",
					"MD_TEXTILE_PANEL" => "md.tentweaver", "MD_SHELTER_PANEL" => "md.house",
					"MD_BARRACKS_PANEL" or "MD_PATROL_PANEL" or "MD_COMMAND_PANEL" => "md.barracks",
					"MD_FORGE_PANEL" => "md.forge", "MD_CHURCH_PANEL" => "md.church",
					"MD_SIEGE_WORKSHOP" or "MD_SIEGE_UNIT" or "MD_CREW_PANEL" => "md.siegeworkshop",
					"MD_STONE_PANEL" => "md.quarry", "MD_HERD_PANEL" => "md.ranch", "MD_TOWER_PANEL" => "md.woodtower",
					"MD_PANEL" when world.Selection.Actors.FirstOrDefault()?.Info.Name is "md.farm" or "md.ranch" or "md.sheepfold" => "md.farm",
					_ => "md.builder"
				};
				MedievalMenuIcons.Add(tab, renderer, "ICON", 3, 3, 20, () => MedievalMenuIcons.Entry(world, IconType()),
					() => (IconType() == "md.market" ? "md-market-livery" : "md-livery") + world.LocalPlayer?.InternalName);
				tab.IsVisible = () => At() != null;
				tab.GetText = () => At()?.Panel.Id == "MD_PANEL" ? (world.Selection.Actors.FirstOrDefault()?.Info.Name switch
				{
					"md.market" or "md.marketstall" => "Citoyens", "md.farm" or "md.ranch" or "md.sheepfold" => "Aménager",
					"md.builder" or null => "Construire", _ => "Recrutement"
				}) : At()?.Name ?? "";
				tab.IsHighlighted = () => !collapsed && At() == Current();
				tab.GetTooltipText = tab.GetText;
				tab.OnClick = () => { chosen = At()?.Panel.Id; collapsed = false; ReleaseHiddenFocus(); };
			}
			var next = dock.Get<ButtonWidget>("MD_CONTEXT_NEXT"); next.GetText = () => "›";
			next.Bounds.X = 366;
			next.IsVisible = () => Available().Length > 3;
			next.OnClick = () => pageOffset = pageOffset + 3 >= Available().Length ? 0 : pageOffset + 3;
			var collapse = dock.Get<ButtonWidget>("MD_CONTEXT_COLLAPSE"); collapse.GetText = () => collapsed ? "+" : "−";
			collapse.Bounds.X = 398;
			collapse.OnClick = () => { collapsed = !collapsed; ReleaseHiddenFocus(); };
			collapse.GetTooltipText = () => collapsed ? "Afficher les commandes" : "Replier les commandes";
			var stocks = widget.Get<ButtonWidget>("MD_STOCKS_TOGGLE");
			stocks.GetText = () => state.AllStocks ? "Stocks : tous" : "Stocks : sélection";
			stocks.IsHighlighted = () => state.AllStocks;
			stocks.OnClick = () => state.AllStocks = !state.AllStocks;
			stocks.GetTooltipText = () => "Étiquettes de ressources sur la carte";
			stocks.GetTooltipDesc = () => "Par défaut : sélection et survol. Cliquez pour afficher tous les stocks.\nLes alertes de faim restent visibles dans les deux modes.";
			// Reflow the catalogue to the same readable width as the other context pages.
			widget.Get("MD_PANEL").Bounds.Width = 410;
			for (var i = 0; i < 7; i++)
			{
				var tab = widget.Get("MD_MENU_TAB_" + i); tab.Bounds.X = 9 + i % 4 * 99; tab.Bounds.Y = 8 + i / 4 * 27; tab.Bounds.Width = 95;
			}
			var title = widget.Get("MD_MENU_TITLE"); title.Bounds.X = 9; title.Bounds.Width = 392; title.Bounds.Y = 65;
			for (var i = 0; i < 8; i++)
			{
				var item = widget.Get("MD_MENU_ITEM_" + i); item.Bounds.X = 9 + i % 2 * 198; item.Bounds.Y = 91 + i / 2 * 46; item.Bounds.Width = 194;
				foreach (var label in item.Children.OfType<LabelWidget>()) label.Bounds.Width = 152;
			}
			foreach (var id in new[] { "MD_MENU_HINT", "MD_MENU_STOCK" }) widget.Get(id).Bounds.Width = 392;
			widget.Get("MD_MENU_NEXT").Bounds.X = 309; widget.Get("MD_MENU_PAGE").Bounds.X = 180;
			widget.Get("MD_MENU_REMOVE").Bounds.X = 207; widget.Get("MD_MENU_REMOVE").Bounds.Width = 194;
			widget.Get("MD_MENU_ROTATE").Bounds.X = 9; widget.Get("MD_MENU_ROTATE").Bounds.Width = 194;
			var footer = widget.Get("MD_STATUS_PANEL"); footer.Bounds.Width = right - 16;
			foreach (var label in footer.Children) label.Bounds.Width = footer.Bounds.Width - 14;
		}

		void ReleaseHiddenFocus()
		{
			var next = !collapsed && !OverlayOpen() ? Current()?.Panel : null;
			if (previousPanel != next) { previousPanel?.Hidden(); sidebar.ScrollToTop(); }
			previousPanel = next;
		}

		public override void Tick()
		{
			cachedTick = -1;
			var selected = world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).ToArray();
			if (!selection.SequenceEqual(selected)) { selection = selected; chosen = null; pageOffset = 0; collapsed = false; }
			var building = selected.FirstOrDefault(a => a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalBuilding>() != null);
			if (occupancyActor != building || occupancyTick != world.WorldTick)
			{
				occupancyActor = building; occupancyTick = world.WorldTick; occupancyReport = MedievalOccupancy.Read(building);
			}
			var occupancyHeight = occupancy.IsVisible() ? occupancy.Bounds.Height + 4 : 0;
			root.Get("MD_CONTEXT_TABS").Bounds.Y = 324 + occupancyHeight;
			sidebar.Bounds.Y = 354 + occupancyHeight;
			root.Get<MedievalTransferWidget>("MD_TRANSFER_PANEL").FitHeight(Game.Renderer.Resolution.Height - sidebar.Bounds.Y - 16);
			if (pageOffset >= Available().Length) pageOffset = 0;
			ReleaseHiddenFocus();
			// Size the catalogue to the current category instead of keeping a tall empty background.
			var tabs = Enumerable.Range(0, 7).Any(i => root.Get("MD_MENU_TAB_" + i).IsVisible());
			root.Get("MD_MENU_TITLE").Bounds.Y = tabs ? 65 : 8;
			for (var i = 0; i < 8; i++) root.Get("MD_MENU_ITEM_" + i).Bounds.Y = (tabs ? 91 : 34) + i / 2 * 46;
			var bottom = Enumerable.Range(0, 8).Select(i => root.Get("MD_MENU_ITEM_" + i)).Where(b => b.IsVisible()).Select(b => b.Bounds.Bottom).DefaultIfEmpty(34).Max() + 6;
			foreach (var id in new[] { "MD_MENU_PREV", "MD_MENU_PAGE", "MD_MENU_NEXT" }) root.Get(id).Bounds.Y = bottom;
			if (root.Get("MD_MENU_NEXT").IsVisible()) bottom += 26;
			root.Get("MD_MENU_HINT").Bounds.Y = bottom; bottom += 34;
			root.Get("MD_MENU_ROTATE").Bounds.Y = root.Get("MD_MENU_REMOVE").Bounds.Y = bottom; bottom += 30;
			root.Get("MD_MENU_STOCK").Bounds.Y = bottom;
			root.Get("MD_PANEL").Bounds.Height = bottom + 36;
			sidebar.Layout.AdjustChildren();
			sidebar.Bounds.Height = Math.Min(Game.Renderer.Resolution.Height - sidebar.Bounds.Y - 12, sidebar.ContentHeight);
			state.Hovered.Clear();
			if (Ui.MouseOverWidget is WorldInteractionControllerWidget or ViewportControllerWidget)
				foreach (var hit in world.ScreenMap.ActorsAtMouse(renderer.Viewport.ViewToWorldPx(Viewport.LastMousePos)))
					if (hit.Actor.IsInWorld && !hit.Actor.IsDead && !world.FogObscures(hit.Actor)) state.Hovered.Add(hit.Actor);
		}
	}
}
