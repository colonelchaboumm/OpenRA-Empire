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
	public class MedievalSiegeSelectableInfo : SelectableInfo, Requires<MedievalSiegeEngineInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeSelectable(init.Self, this);
	}
	public sealed class MedievalSiegeSelectable : Selectable, IMouseBounds
	{
		public MedievalSiegeSelectable(Actor self, MedievalSiegeSelectableInfo info) : base(self, info) { }
		Polygon IMouseBounds.MouseoverBounds(Actor self, WorldRenderer wr)
		{
			var point = wr.ScreenPxPosition(self.CenterPosition);
			var tall = !self.Trait<MedievalSiegeEngine>().Packed;
			return new Polygon(new Rectangle(point.X - (tall ? 114 : 76), point.Y - (tall ? 156 : 84), tall ? 228 : 152, tall ? 212 : 116));
		}
	}

	public sealed class MedievalSiegeSupplyOrderGenerator : OrderGenerator
	{
		readonly Actor cart;
		readonly Actor[] soldiers;
		readonly WorldRenderer renderer;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalSiegeSupplyOrderGenerator(World world, WorldRenderer renderer, Actor cart, Actor[] soldiers = null) : base(world) { this.cart = cart; this.renderer = renderer; this.soldiers = soldiers; }
		bool Valid => SiegeRules.Live(cart, cart.World.LocalPlayer);
		bool ValidTarget(Actor a) => SiegeRules.Live(a, cart.Owner) && a.TraitOrDefault<MedievalSiegeEngine>() is MedievalSiegeEngine engine
			&& (soldiers == null ? engine.Info.Trebuchet : engine.Crew.Count() < engine.CrewLimit);
		Actor At(World world, CPos cell) => world.ActorsWithTrait<MedievalSiegeEngine>().Select(p => p.Actor)
			.Where(a => ValidTarget(a)
				&& (a.Location == cell || a.Trait<MedievalSiegeEngine>().State > 0 && SiegeRules.Braces(a.Location).Contains(cell)))
			.OrderBy(a => a.ActorID).FirstOrDefault()
			?? world.ScreenMap.ActorsAtMouse(renderer.Viewport.ViewToWorldPx(Viewport.LastMousePos)).Select(p => p.Actor).FirstOrDefault(ValidTarget);
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (!Valid || world.Paused || At(world, cell) is not Actor target) yield break;
			if (soldiers == null) yield return new Order("MedievalSiegeSupply", cart, Target.FromActor(target), false);
			else foreach (var soldier in soldiers.Where(a => SiegeRules.Live(a, cart.Owner))) yield return new Order("MedievalAssignSiege", soldier, Target.FromActor(target), false);
			world.CancelInputMode();
		}
		protected override void Tick(World world) { if (!Valid) world.CancelInputMode(); }
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (!Valid) yield break;
			var target = At(world, renderer.Viewport.ViewToWorld(Viewport.LastMousePos));
			if (target != null) foreach (var line in MedievalTradeMarket.Connection(cart, target)) yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) => Valid && At(world, cell) != null ? "ability" : "move-blocked";
	}

	public sealed class MedievalSiegeLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalSiegeLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			var player = world.LocalPlayer;
			if (player == null)
			{
				foreach (var id in new[] { "MD_SIEGE_WORKSHOP", "MD_SIEGE_UNIT", "MD_STONE_PANEL", "MD_CREW_PANEL" }) widget.Get(id).IsVisible = () => false;
				return;
			}
			Actor Selected<T>() where T : class => world.Selection.Actors.FirstOrDefault(a => SiegeRules.Live(a, player) && a.TraitOrDefault<T>() != null);
			var build = widget.Get<ButtonWidget>("MD_SIEGE_BUILD");
			build.GetText = () => "";
			widget.Get<LabelWidget>("MD_SIEGE_BUILD_NAME").GetText = () => "Atelier de siege";
			widget.Get<LabelWidget>("MD_SIEGE_BUILD_COST").GetText = () => "160 bois / 80 pierre";
			build.GetTooltipText = () => "Atelier de siege - 4 x 3 cases";
			build.GetTooltipDesc = () => "Les batisseurs fabriquent beliers, trebuchets, charrettes de pierres et chaudrons.\nLes materiaux sont apportes physiquement depuis les stocks proches.";
			build.IsDisabled = () => world.Paused;
			build.OnClick = () => { if (!world.Paused) world.OrderGenerator = new MedievalPlaceOrderGenerator(world, worldRenderer, "md.siegeworkshop"); };
			widget.Get("MD_SIEGE_WORKSHOP").IsVisible = () => Selected<MedievalSiegeWorkshop>() != null;
			widget.Get<LabelWidget>("MD_SIEGE_TITLE").GetText = () => "ATELIER DE SIEGE";
			for (var i = 0; i < SiegeRules.Products.Length; i++)
			{
				var product = i;
				if (i == 3)
				{
					var icon = widget.Get<SpriteWidget>("MD_SIEGE_PRODUCT_3_ICON");
					icon.GetSprite = () => world.Map.Sequences.GetSequence("md.fire-icons", "idle").GetSprite(1);
					icon.GetPalette = () => "md-livery" + player.InternalName;
				}
				var button = widget.Get<ButtonWidget>("MD_SIEGE_PRODUCT_" + i);
				button.GetText = () => "";
				widget.Get<LabelWidget>($"MD_SIEGE_PRODUCT_{i}_NAME").GetText = () => SiegeRules.Names[product];
				widget.Get<LabelWidget>($"MD_SIEGE_PRODUCT_{i}_COST").GetText = () =>
				{
					var cost = SiegeRules.Costs[product];
					return $"{cost[0]} bois / {cost[3]} pierre" + (cost[1] > 0 ? $" / {cost[1]} nourriture" : product == 3 ? " | 2 soldats requis" : " | 4 soldats requis");
				};
				button.GetTooltipText = () => SiegeRules.Names[product];
				button.GetTooltipDesc = () => product == 0 ? "Assignez quatre soldats proches pour le pousser et frapper les structures."
					: product == 1 ? "Affectez 1 a 4 soldats pour le deplacer, le monter et le charger.\nChaque projectile est porte depuis sa charrette et coute 5 pierres."
					: product == 3 ? "Deux soldats pour pousser le chaudron. Les archers allies a 3 cases enflamment leurs fleches.\nLes fleches peuvent incendier les structures ennemies."
					: "Un conducteur, un logement et des repas. Transporte jusqu'a 60 pierres.\nSelectionnez ensuite la charrette et choisissez son trebuchet.";
				button.IsDisabled = () => world.Paused || Selected<MedievalSiegeWorkshop>() is not Actor a || !a.Trait<MedievalSiegeWorkshop>().CanRequest(a, product);
				button.OnClick = () => { if (!button.IsDisabled() && Selected<MedievalSiegeWorkshop>() is Actor a) world.IssueOrder(new Order("MedievalSiegeProduce", a, false) { TargetString = SiegeRules.Products[product] }); };
			}
			var cancel = widget.Get<ButtonWidget>("MD_SIEGE_CANCEL"); cancel.GetText = () => "Annuler la fabrication";
			cancel.IsDisabled = () => world.Paused || Selected<MedievalSiegeWorkshop>()?.Trait<MedievalSiegeWorkshop>().Working != true;
			cancel.OnClick = () => { if (!cancel.IsDisabled() && Selected<MedievalSiegeWorkshop>() is Actor a) world.IssueOrder(new Order("MedievalSiegeCancel", a, false)); };
			widget.Get<LabelWidget>("MD_SIEGE_STATUS").GetText = () => Selected<MedievalSiegeWorkshop>() is not Actor a ? ""
				: !MedievalStockpile.Active(a) ? "Attendez la fin du chantier." : a.Trait<MedievalSiegeWorkshop>().Status;
			widget.Get<LabelWidget>("MD_SIEGE_INPUTS").GetText = () => Selected<MedievalSiegeWorkshop>() is Actor a ? a.Trait<MedievalSiegeWorkshop>().Working
				? "Materiaux livres : " + a.Trait<MedievalSiegeWorkshop>().Supplies
				: $"Reserve : {a.Trait<MedievalSiegeWorkshop>().Inputs.Amount(0)} bois / {a.Trait<MedievalSiegeWorkshop>().Inputs.Amount(3)} pierre / {a.Trait<MedievalSiegeWorkshop>().Inputs.Amount(1)} nourriture" : "";
			widget.Get<LabelWidget>("MD_SIEGE_HINT").GetText = () => "Gardez des batisseurs au travail et des stocks dans la zone de l'atelier. Les soldats du belier se recrutent a la caserne.";
			widget.Get("MD_SIEGE_UNIT").IsVisible = () => Selected<MedievalSiegeEngine>() != null;
			widget.Get<LabelWidget>("MD_ENGINE_TITLE").GetText = () => Selected<MedievalSiegeEngine>()?.Trait<MedievalSiegeEngine>().Info.Trebuchet == true ? "TREBUCHET - TRANSPORT ET TIR" : Selected<MedievalSiegeEngine>()?.Trait<MedievalSiegeEngine>().Info.Cauldron == true ? "CHAUDRON - FLECHES ENFLAMMEES" : "BELIER - EQUIPE DE POUSSEE";
			widget.Get<LabelWidget>("MD_ENGINE_STATUS").GetText = () => Selected<MedievalSiegeEngine>()?.Trait<MedievalSiegeEngine>().Status ?? "";
			var action = widget.Get<ButtonWidget>("MD_ENGINE_ACTION");
			action.GetText = () => Selected<MedievalSiegeEngine>() is not Actor a ? "" : !a.Trait<MedievalSiegeEngine>().Info.Trebuchet ? $"Appeler {a.Trait<MedievalSiegeEngine>().CrewLimit} soldats proches (10 cases)"
				: a.Trait<MedievalSiegeEngine>().Busy ? "Operation en cours..." : a.Trait<MedievalSiegeEngine>().Packed ? "Monter le trebuchet" : "Demonter sur la charrette";
			action.IsDisabled = () =>
			{
				if (world.Paused || Selected<MedievalSiegeEngine>() is not Actor a) return true;
				var engine = a.Trait<MedievalSiegeEngine>();
				return engine.Info.Trebuchet ? engine.Busy || engine.ReadyCrew == 0 || engine.Packed && (!a.IsIdle || !engine.ClearSite()) : engine.Crew.Count() >= engine.CrewLimit;
			};
			action.OnClick = () =>
			{
				if (action.IsDisabled() || Selected<MedievalSiegeEngine>() is not Actor a) return;
				var engine = a.Trait<MedievalSiegeEngine>();
				world.IssueOrder(new Order(!engine.Info.Trebuchet ? "MedievalSiegeCrew" : engine.Packed ? "MedievalSiegeDeploy" : "MedievalSiegePack", a, false));
			};
			var release = widget.Get<ButtonWidget>("MD_ENGINE_RELEASE");
			release.GetText = () => Selected<MedievalSiegeEngine>()?.Trait<MedievalSiegeEngine>().Info.Trebuchet == true ? "Cesser le tir" : "Liberer les soldats";
			release.IsDisabled = () => world.Paused || Selected<MedievalSiegeEngine>() is not Actor a || a.Trait<MedievalSiegeEngine>().Busy;
			release.OnClick = () => { if (!release.IsDisabled() && Selected<MedievalSiegeEngine>() is Actor a) world.IssueOrder(new Order(a.Trait<MedievalSiegeEngine>().Info.Trebuchet ? "Stop" : "MedievalSiegeRelease", a, false)); };
			var addCrew = widget.Get<ButtonWidget>("MD_ENGINE_ADD_CREW"); addCrew.GetText = () => "Ajouter un soldat proche (10 cases)";
			addCrew.IsDisabled = () => world.Paused || Selected<MedievalSiegeEngine>() is not Actor a || a.Trait<MedievalSiegeEngine>().Crew.Count() >= a.Trait<MedievalSiegeEngine>().CrewLimit;
			addCrew.OnClick = () => { if (!addCrew.IsDisabled() && Selected<MedievalSiegeEngine>() is Actor a) world.IssueOrder(new Order("MedievalSiegeAddCrew", a, false)); };
			var freeCrew = widget.Get<ButtonWidget>("MD_ENGINE_FREE_CREW"); freeCrew.GetText = () => "Liberer tout l'equipage";
			freeCrew.IsDisabled = () => world.Paused || Selected<MedievalSiegeEngine>() is not Actor a || !a.Trait<MedievalSiegeEngine>().Crew.Any();
			freeCrew.OnClick = () => { if (!freeCrew.IsDisabled() && Selected<MedievalSiegeEngine>() is Actor a) world.IssueOrder(new Order("MedievalSiegeRelease", a, false)); };
			widget.Get<LabelWidget>("MD_ENGINE_CREW").GetText = () => Selected<MedievalSiegeEngine>() is not Actor a ? "" : $"Equipage : {a.Trait<MedievalSiegeEngine>().ReadyCrew} present(s) / {a.Trait<MedievalSiegeEngine>().Crew.Count()} affecte(s), max. {a.Trait<MedievalSiegeEngine>().CrewLimit} | Travail : {a.Trait<MedievalSiegeEngine>().WorkPercent}%";
			widget.Get<LabelWidget>("MD_ENGINE_AMMO").GetText = () => Selected<MedievalSiegeEngine>() is not Actor a ? ""
				: a.Trait<MedievalSiegeEngine>().Info.Trebuchet ? $"Charrette : {a.Trait<MedievalSiegeEngine>().Stone}/60 pierres | Fronde : {a.Trait<MedievalSiegeEngine>().Sling.Amount(3)}/5\n5 pierres par projectile | Portee : 4 a 16 cases"
				: $"Soldats affectes : {a.Trait<MedievalSiegeEngine>().Crew.Count()}/{a.Trait<MedievalSiegeEngine>().CrewLimit}. Leur faim et leur moral restent actifs.";
			widget.Get<LabelWidget>("MD_ENGINE_HINT").GetText = () => Selected<MedievalSiegeEngine>()?.Trait<MedievalSiegeEngine>().Info.Trebuchet == true
				? "1 a 4 soldats : plus ils sont presents, plus les manoeuvres sont rapides. Le porteur prend la pierre sur la charrette et charge la fronde. Montage sur 3 x 3 cases libres."
				: Selected<MedievalSiegeEngine>()?.Trait<MedievalSiegeEngine>().Info.Cauldron == true ? "Archers allies a 3 cases : fleches enflammees. Deux soldats affectes doivent rester au contact pour le pousser. Chaleur, lumiere et fumee restent actives a l’arret."
				: "Les soldats rejoignent le belier a pied. Il attend si l'un manque. Ordres normaux de deplacement et d'attaque des batiments. Un ordre manuel a un soldat le libere.";
			widget.Get("MD_STONE_PANEL").IsVisible = () => Selected<MedievalStoneCart>() != null;
			widget.Get<LabelWidget>("MD_STONE_TITLE").GetText = () => "CHARRETTE DE PIERRES";
			widget.Get<LabelWidget>("MD_STONE_STATUS").GetText = () => Selected<MedievalStoneCart>()?.Trait<MedievalStoneCart>().Status ?? "";
			widget.Get<LabelWidget>("MD_STONE_STOCK").GetText = () => Selected<MedievalStoneCart>() is Actor a
				? $"Pierres transportees : {a.Trait<MedievalStockpile>().Stock.Amount(3)}/60 | {a.Trait<MedievalStoneCart>().Destination(a)}" : "";
			widget.Get<LabelWidget>("MD_STONE_HINT").GetText = () => "Prend les pierres au depot ou au marche le plus proche, puis les livre reellement au trebuchet. Son conducteur mange et occupe un logement.";
			var assign = widget.Get<ButtonWidget>("MD_STONE_ASSIGN"); assign.GetText = () => "Choisir le trebuchet a ravitailler";
			assign.IsDisabled = () => world.Paused || Selected<MedievalStoneCart>() == null;
			assign.OnClick = () => { if (!assign.IsDisabled() && Selected<MedievalStoneCart>() is Actor a) world.OrderGenerator = new MedievalSiegeSupplyOrderGenerator(world, worldRenderer, a); };
			var pause = widget.Get<ButtonWidget>("MD_STONE_PAUSE");
			pause.GetText = () => Selected<MedievalStoneCart>()?.Trait<MedievalStoneCart>().Active == true ? "Suspendre les livraisons" : "Reprendre les livraisons";
			pause.IsDisabled = () => world.Paused || Selected<MedievalStoneCart>() is not Actor a || !a.Trait<MedievalStoneCart>().Active && !a.Trait<MedievalStoneCart>().HasTarget(a);
			pause.OnClick = () => { if (!pause.IsDisabled() && Selected<MedievalStoneCart>() is Actor a) world.IssueOrder(new Order("MedievalStonePause", a, false)); };
			var recover = widget.Get<ButtonWidget>("MD_STONE_RECOVER"); recover.GetText = () => "Rapporter les pierres au stockage";
			recover.IsDisabled = () => world.Paused || Selected<MedievalStoneCart>() is not Actor a || a.Trait<MedievalStockpile>().Stock.Amount(3) == 0;
			recover.OnClick = () => { if (!recover.IsDisabled() && Selected<MedievalStoneCart>() is Actor a) world.IssueOrder(new Order("MedievalStoneRecover", a, false)); };
			widget.Get("MD_CREW_PANEL").IsVisible = () => Selected<MedievalSiegeEngine>() == null && Selected<MedievalSiegeCrew>() != null;
			widget.Get<LabelWidget>("MD_CREW_TITLE").GetText = () => "EQUIPAGE D'ENGIN DE SIEGE";
			widget.Get<LabelWidget>("MD_CREW_STATUS").GetText = () => Selected<MedievalSiegeCrew>() is not Actor a ? ""
				: a.TraitOrDefault<MedievalSiegeCapture>() is { Capturing: true } capture ? $"Capture de l'engin : {capture.CapturePercent}% - restez au contact."
				: a.Trait<MedievalSiegeCrew>().Description;
			widget.Get<LabelWidget>("MD_CREW_CAPTURE_HINT").GetText = () => "Voler un engin de siege ennemi : clic droit sur l'engin, puis 6 s au contact. Ctrl + clic droit pour l'attaquer.";
			var assignCrew = widget.Get<ButtonWidget>("MD_CREW_ASSIGN"); assignCrew.GetText = () => "Affecter les soldats selectionnes a un engin";
			assignCrew.IsDisabled = () => world.Paused;
			assignCrew.OnClick = () =>
			{
				var soldiers = world.Selection.Actors.Where(a => SiegeRules.Live(a, player) && a.TraitOrDefault<MedievalSiegeCrew>() != null).ToArray();
				if (!world.Paused && soldiers.Length > 0) world.OrderGenerator = new MedievalSiegeSupplyOrderGenerator(world, worldRenderer, soldiers[0], soldiers);
			};
			var leave = widget.Get<ButtonWidget>("MD_CREW_LEAVE"); leave.GetText = () => "Quitter l'equipage";
			leave.IsDisabled = () => world.Paused;
			leave.OnClick = () => { if (!world.Paused) foreach (var a in world.Selection.Actors.Where(a => SiegeRules.Live(a, player) && a.TraitOrDefault<MedievalSiegeCrew>()?.Assigned == true)) world.IssueOrder(new Order("MedievalLeaveSiege", a, false)); };
		}
	}
}
