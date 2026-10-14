using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalPlaceOrderGenerator : OrderGenerator
	{
		readonly string type;
		readonly WorldRenderer renderer;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalPlaceOrderGenerator(World world, WorldRenderer renderer, string type) : base(world)
		{ this.type = type; this.renderer = renderer; }
		CPos Cell => renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
		public string HousePreviewText(World world) => type == "md.house" ? $"{HouseStyleDeck.Names[HouseStyleDeck.Next(world.LocalPlayer)]} : 60 bois, 20 pierre | 8 habitants | Parcelle 2x2. Cliquez pour construire." : null;
		public string LocalityPreviewText(World world)
		{
			if (type is not ("md.market" or "md.marketstall")) return null;
			var registry = MedievalLocalities.For(world.LocalPlayer);
			var position = world.Map.CenterOfCell(Cell) + world.Map.Rules.Actors[type].TraitInfo<BuildingInfo>().CenterOffset(world);
			var neighbor = registry.At(world.LocalPlayer, position);
			return neighbor != null ? "Rejoindra " + registry.Name(MedievalLocalities.Id(neighbor)) + ". Nom modifiable depuis le marche."
				: (type == "md.marketstall" ? "Fondera un nouveau village" : "Fondera une nouvelle ville") + " a la fin du chantier. Vous pourrez choisir son nom.";
		}
		internal static IRenderable Preview(World world, string type, CPos cell, bool valid, int frame = 0, WorldRenderer renderer = null)
		{
			var building = world.Map.Rules.Actors[type].TraitInfo<BuildingInfo>();
			var center = world.Map.CenterOfCell(cell) + building.CenterOffset(world);
			var image = type == "md.banner" ? "md.heraldic-banner-" + MedievalHeraldry.For(world.LocalPlayer).Emblem
				: world.Map.Rules.Actors[type].TraitInfo<RenderSpritesInfo>().Image ?? type;
			var sprite = world.Map.Sequences.GetSequence(image, "idle").GetSprite(type == "md.house" ? HouseStyleDeck.Next(world.LocalPlayer) : frame);
			var palette = renderer == null ? null : type is "md.banner" or "md.militarytent" ? renderer.Palette("md-livery" + world.LocalPlayer.InternalName)
				: type is "md.market" or "md.marketstall" or "md.foundingmarket" ? renderer.Palette("md-market-livery" + world.LocalPlayer.InternalName) : null;
			return MedievalArchitecture.Apply(new SpriteRenderable(sprite, center, WVec.Zero, 1024, palette, 1f, .55f,
				valid ? new Vector3(.85f, 1f, .85f) : new Vector3(1f, .45f, .4f), TintModifiers.IgnoreWorldTint, true),
				type, type == "md.house" ? HouseStyleDeck.Next(world.LocalPlayer) : frame);
		}
		internal static IEnumerable<IRenderable> CellOutline(World world, CPos cell, Color color)
		{
			var center = world.Map.CenterOfCell(cell);
			var corners = world.Map.Grid.Ramps[0].Corners;
			for (var i = 0; i < 4; i++)
				yield return new LineAnnotationRenderable(center + corners[i], center + corners[(i + 1) % 4], 1, color);
		}
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var settlement = world.LocalPlayer.PlayerActor.Trait<MedievalSettlement>();
			if (world.Paused || type == "md.house" && settlement.HousePlacementPending) yield break;
			if (!MedievalSettlement.CanPlace(world, world.LocalPlayer, type, cell)) yield break;
			if (type == "md.house") settlement.AwaitHousePlacement();
			yield return new Order("MedievalPlace", world.LocalPlayer.PlayerActor, Target.FromCell(world, cell), false) { TargetString = type };
			world.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
		{
			yield return Preview(world, type, Cell, MedievalSettlement.CanPlace(world, world.LocalPlayer, type, Cell), renderer: wr);
		}
		// Line annotations already use screen coordinates. Draw them after the
		// world buffer is scaled, otherwise zoom is applied to their position twice.
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			var valid = MedievalSettlement.CanPlace(world, world.LocalPlayer, type, Cell);
			var color = valid ? Color.LimeGreen : Color.Red;
			foreach (var cell in world.Map.Rules.Actors[type].TraitInfo<BuildingInfo>().Tiles(Cell))
				foreach (var line in CellOutline(world, cell, color)) yield return line;
			var provider = world.Map.Rules.Actors[type].TraitInfoOrDefault<MedievalFoodProviderInfo>();
			if (provider != null)
			{
				var building = world.Map.Rules.Actors[type].TraitInfo<BuildingInfo>();
				var center = world.Map.CenterOfCell(Cell) + building.CenterOffset(world);
				foreach (var segment in MedievalFoodProvider.Ring(center, provider.Radius, color)) yield return segment;
			}
			foreach (var a in world.ActorsHavingTrait<MedievalConstructionSupply>().Where(a => a.Owner == world.LocalPlayer && MedievalStockpile.Active(a)
				&& a.TraitOrDefault<MedievalConstructionSupply>() != null))
				foreach (var segment in MedievalFoodProvider.Ring(a.CenterPosition, a.Trait<MedievalConstructionSupply>().Info.Radius, Color.CornflowerBlue))
					yield return segment;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) =>
			MedievalSettlement.CanPlace(world, world.LocalPlayer, type, cell) ? "ability" : "move-blocked";
	}

	public class MedievalEconomyLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalEconomyLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			var p = world.LocalPlayer;
			MedievalNationalPrideInterface.Bind(widget, world, worldRenderer);
			if (p == null) return;
			var settlement = p.PlayerActor.Trait<MedievalSettlement>();
			var food = p.PlayerActor.Trait<MedievalFoodEconomy>();
			MedievalCaravanInterface.Bind(widget, world);
			MedievalChurch.Bind(widget, world, worldRenderer);
			foreach (var id in new[] { "SIDEBAR_PRODUCTION", "SIDEBAR_MONEYBIN", "SUPPORT_POWERS", "SELL_BUTTON", "POWER_BUTTON", "REPAIR_BUTTON" })
			{
				var old = widget.GetOrNull(id);
				if (old != null) old.IsVisible = () => false;
			}
			for (var r = 0; r < Inventory.ResourceCount; r++)
			{
				var resource = r;
				widget.Get<LabelWidget>("MD_RESOURCE_" + r).GetText = () =>
					$"{(resource == 1 ? "Nourr." : MedievalStockDisplay.Labels[resource])} : {MedievalSettlement.Available(p, resource)}";
			}
			widget.Get<LabelWidget>("MD_POP").GetText = () => $"Habitants : {MedievalSettlement.Population(p)}/{MedievalSettlement.Housing(p)}";
			widget.Get<LabelWidget>("MD_FOOD_TITLE").GetText = () => "RAVITAILLEMENT";
			widget.Get<LabelWidget>("MD_FOOD_FLOW").GetText = () => $"Recoltes : {food.Harvests.PerMinute:0.#}/min | Livre : {food.Deliveries.PerMinute:0.#}/min";
			var balance = widget.Get<LabelWidget>("MD_FOOD_BALANCE");
			balance.GetText = () => $"Besoins : {MedievalFoodEconomy.Demand(p):0.#}/min | Solde : {food.Deliveries.PerMinute - MedievalFoodEconomy.Demand(p):+0.#;-0.#;0}/min";
			balance.GetColor = () => food.Deliveries.PerMinute < MedievalFoodEconomy.Demand(p) ? Color.Orange : Color.LightGreen;
			widget.Get<LabelWidget>("MD_FOOD_RESERVE").GetText = () =>
			{
				return $"Marches, etals, charrettes : {MedievalFoodEconomy.ReadyFood(p)} | Couverts : {MedievalFoodEconomy.Covered(p)}/{MedievalSettlement.Population(p)}";
			};
			var status = widget.Get<LabelWidget>("MD_FOOD_STATUS");
			status.GetText = () => MedievalFoodEconomy.Starving(p) > 0 ? $"FAMINE : {MedievalFoodEconomy.Starving(p)} en danger de mort !"
				: MedievalFoodEconomy.Hungry(p) > 0 ? $"FAIM : {MedievalFoodEconomy.Hungry(p)} habitants affaiblis. Ravitaillez !"
				: MedievalFoodEconomy.Covered(p) < MedievalSettlement.Population(p) ? $"Ravitaillement : {MedievalFoodEconomy.Covered(p)}/{MedievalSettlement.Population(p)} couverts. Rapprochez les réserves."
				: MedievalFoodEconomy.ReadyFood(p) < MedievalFoodEconomy.Demand(p) ? "Reserves de ravitaillement faibles : approvisionnez."
				: $"Ravitaillement assuré : {MedievalFoodEconomy.ReadyFood(p)} repas prêts.";
			status.GetColor = () => MedievalFoodEconomy.Starving(p) > 0 ? Color.OrangeRed
				: MedievalFoodEconomy.Covered(p) < MedievalSettlement.Population(p) || MedievalFoodEconomy.ReadyFood(p) < MedievalFoodEconomy.Demand(p) ? Color.Orange : Color.White;
			widget.Get<LabelWidget>("MD_FOOD_DEATHS").GetText = () => $"Deces par famine : {food.StarvationDeaths} | Recruter augmente les besoins.";
			for (var i = 0; i < MedievalSettlement.Buildings.Length; i++)
			{
				var index = i;
				var type = MedievalSettlement.Buildings[index];
				var cost = world.Map.Rules.Actors[type].TraitInfo<MedievalBuildingInfo>().Cost;
				var button = widget.Get<ButtonWidget>("MD_BUILD_" + index);
				button.IsDisabled = () => world.Paused || type == "md.house" && settlement.HousePlacementPending;
				button.GetText = () => "";
				widget.Get<LabelWidget>($"MD_BUILD_{index}_NAME").GetText = () => AgriculturalZone.IsKind(type)
					? (type == "md.field" ? "Cultures" : "Enclos") : MedievalSettlement.BuildingNames[index];
				widget.Get<LabelWidget>($"MD_BUILD_{index}_COST").GetText = () => AgriculturalZone.IsKind(type) ? "Tracer zone" : $"{cost[0]}B {cost[3]}P";
				button.GetTooltipText = () => MedievalSettlement.BuildingNames[index];
				button.GetTooltipDesc = () => type == "md.house" ? "Six architectures tirees au sort : chaumiere, etages, jardin, balcon...\nL'apercu indique le modele de votre prochain chantier.\n60 bois, 20 pierre, 8 habitants. Parcelle de 2x2 cases."
					: type == "md.marketstall" ? "Petit relais de 2x1 cases : 25 bois et 5 pierre.\nRelie automatiquement au grand marche le plus proche.\nLes transporteurs apportent nourriture, bois et pierre selon les besoins.\nNourrit les unites et fournit les chantiers dans un rayon de 6 cases."
					: AgriculturalZone.IsKind(type)
					? "Cliquez deux coins pour choisir la surface.\nLe cout et les capacites suivent la taille."
					: $"Construction : {cost[0]} bois, {cost[3]} pierre.\nLes batisseurs apportent les materiaux sur place.";
				button.OnClick = () =>
				{
					if (!button.IsDisabled()) world.OrderGenerator = AgriculturalZone.IsKind(type)
						? new MedievalZoneOrderGenerator(world, worldRenderer, type) : new MedievalPlaceOrderGenerator(world, worldRenderer, type);
				};
			}
			for (var i = 0; i < 6; i++)
			{
				var index = i;
				var button = widget.Get<ButtonWidget>("MD_RECRUIT_" + i);
				var cost = MedievalSettlement.Cost(p, MedievalSettlement.Recruits[index]);
				button.GetText = () => index < 6 ? "" : MedievalSettlement.RecruitNames[index];
				if (index < 6)
				{
					var caption = widget.Get<LabelWidget>($"MD_RECRUIT_{index}_NAME");
					caption.GetText = () => index == 3 ? "T. de pierre" : MedievalSettlement.RecruitNames[index];
					caption.GetColor = () => button.IsDisabled() ? Color.Gray : Color.FromArgb(238, 233, 212);
					widget.Get<LabelWidget>($"MD_RECRUIT_{index}_COST").GetText = () => $"{cost.Food} nourriture";
				}
				var upkeep = (double)FoodNeed.TicksPerMinute / world.Map.Rules.Actors[MedievalSettlement.Recruits[index]].TraitInfo<MedievalNeedsFoodInfo>().MealInterval;
				button.GetTooltipText = () => MedievalSettlement.RecruitNames[index] + " : " + cost;
				button.GetTooltipDesc = () => $"Entretien : +{upkeep:0.#} nourriture/min.\nSolde apres recrutement : {food.Deliveries.PerMinute - MedievalFoodEconomy.Demand(p) - upkeep:+0.#;-0.#;0}/min.\nPrevoir un surplus pour les prochains recrutements.";
				button.IsDisabled = () => settlement.Recruiting || world.Paused || MedievalSettlement.Population(p) + MedievalSettlement.PendingPopulation(p) >= MedievalSettlement.Housing(p)
					|| !MedievalSettlement.Warehouses(p).Any()
					|| !cost.CanPay(MedievalSettlement.RecruitmentStores(p).Select(a => a.Trait<MedievalStockpile>().Stock));
				button.OnClick = () => world.IssueOrder(new Order("MedievalRecruit", p.PlayerActor, false) { TargetString = MedievalSettlement.Recruits[index] });
			}
			widget.Get<LabelWidget>("MD_RECRUIT_COST").GetText = () => "Villageois : " + MedievalSettlement.Cost(p, "md.builder");
			widget.Get<LabelWidget>("MD_MILITIA_COST").GetText = () => $"Arcs {MedievalEquipment.Amount(p, 0)} | Epees {MedievalEquipment.Amount(p, 1)} | Armures {MedievalEquipment.Amount(p, 2)}";
			widget.Get<LabelWidget>("MD_ARMS_COST").GetText = () => "Forge 80B 20P | Caserne 100B 40P";
			Actor SelectedMarket() => world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).FirstOrDefault(a => a.Owner == p && a.TraitOrDefault<MedievalMarket>() != null);
			Actor SelectedWarehouse() => world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).FirstOrDefault(a => a.Owner == p && a.TraitOrDefault<MedievalStockyard>() != null && a.TraitOrDefault<MedievalMarketStall>() == null);
			widget.Get("MD_YARD_PANEL").IsVisible = () => SelectedWarehouse() != null;
			widget.Get<LabelWidget>("MD_YARD_TITLE").GetText = () => "ENTREPOT - STOCKAGE EXTERIEUR";
			MedievalStorageInterface.Bind(widget, world, worldRenderer, "MD_YARD", SelectedWarehouse);
			widget.Get<LabelWidget>("MD_YARD_STOCK").GetText = () => MedievalStorageInterface.Totals(SelectedWarehouse(), 1, 0);
			widget.Get<LabelWidget>("MD_YARD_MORE").GetText = () => MedievalStorageInterface.Totals(SelectedWarehouse(), 3, 2, 4);
			widget.Get<LabelWidget>("MD_YARD_EQUIPMENT").GetText = () => MedievalStorageInterface.Totals(SelectedWarehouse(), 5, 6, 7);
			var cartButton = widget.Get<ButtonWidget>("MD_CART_CREATE");
			cartButton.GetText = () => SelectedMarket()?.Trait<MedievalMarket>().Recruiting == true ? "Preparation..." : "Charrette 20N";
			cartButton.GetTooltipText = () => "Creer une charrette au marche selectionne";
			cartButton.GetTooltipDesc = () => "Cout : " + MedievalSettlement.Cost(p, "md.supplycart") + ". Une place d'habitation.\nTransport : 60 nourriture, 60 bois et 60 pierre.\nRepas : rayon 4 cases. Chantiers : rayon 8 cases.\nChoisissez les ressources dans la zone d'un marche.\nSon porteur consomme aussi 2 nourriture/min.";
			cartButton.IsDisabled = () => world.Paused || SelectedMarket() is not Actor market || !market.Trait<MedievalMarket>().CanRecruit(market);
			cartButton.OnClick = () => { if (SelectedMarket() is Actor market) world.IssueOrder(new Order("MedievalCreateCart", market, false)); };
			var reloadButton = widget.Get<ButtonWidget>("MD_CART_RELOAD");
			reloadButton.GetText = () => "Recharger";
			reloadButton.GetTooltipText = () => "Renvoyer les charrettes selectionnees au marche";
			reloadButton.GetTooltipDesc = () => "Retour physique au marche, puis chargement des ressources cochees.\nLes transporteurs apportent le bois et la pierre des depots.\nLa charrette reste au marche jusqu'a votre prochain ordre.";
			reloadButton.IsDisabled = () => world.Paused || !MedievalMarket.Markets(p).Any()
				|| !world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).Any(a => a.Owner == p && a.TraitOrDefault<MedievalSupplyCart>() != null);
			reloadButton.OnClick = () =>
			{
				foreach (var cart in world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).Where(a => a.Owner == p && a.TraitOrDefault<MedievalSupplyCart>() != null))
					world.IssueOrder(new Order("MedievalReloadCart", cart, false));
			};
			Actor SelectedCart() => world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).FirstOrDefault(a => a.Owner == p && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalSupplyCart>() != null);
			widget.Get("MD_CART_PANEL").IsVisible = () => SelectedCart() != null && SelectedWarehouse() == null;
			widget.Get<LabelWidget>("MD_CART_TITLE").GetText = () => "CHARRETTE - RESSOURCES A CHARGER";
			foreach (var resource in new[] { 1, 0, 3, 4 })
			{
				var r = resource;
				var button = widget.Get<ButtonWidget>("MD_CART_CARGO_" + r);
				button.GetText = () => (SelectedCart()?.Trait<MedievalSupplyCart>().Wants(r) == true ? "[X] " : "[ ] ") + MedievalStockDisplay.Labels[r];
				button.IsDisabled = () => world.Paused || SelectedCart() is not Actor cart || !MedievalSupplyCart.InMarketZone(cart) || cart.Trait<MedievalConstructionConvoy>().Active;
				button.GetTooltipText = () => "Activer ou arreter le chargement : " + MedievalStockDisplay.Labels[r];
				button.GetTooltipDesc = () => SelectedCart() is Actor cart
					? $"Capacite : {cart.Trait<MedievalStockpile>().Stock.Capacity(r)}. Reserve aux batisseurs : {cart.Trait<MedievalStockpile>().Stock.Reserved(r)}. En livraison : {cart.Trait<MedievalStockpile>().Stock.Incoming(r)}.\nLa charrette doit rester arretee dans la zone d'un marche.\nDecocher conserve le contenu deja charge."
					: "Selectionnez une charrette.";
				button.OnClick = () => { if (SelectedCart() is Actor cart) world.IssueOrder(new Order("MedievalCartCargo", cart, false) { ExtraData = (uint)r }); };
			}
			widget.Get<LabelWidget>("MD_CART_STOCK").GetText = () => SelectedCart() is Actor cart
				? string.Join(" | ", new[] { 1, 0, 3, 4 }.Select(r => $"{MedievalStockDisplay.Labels[r]} {cart.Trait<MedievalStockpile>().Stock.Amount(r)}/{cart.Trait<MedievalStockpile>().Stock.Capacity(r)}")) : "";
			widget.Get<LabelWidget>("MD_CART_HINT").GetText = () => SelectedCart() is not Actor cart ? ""
				: !MedievalSupplyCart.InMarketZone(cart) ? "Hors marche : retour avec Recharger. Bleu = chantiers."
				: !cart.IsIdle ? "Arretez la charrette pour recevoir les transporteurs."
				: "Transporteurs requis. Bleu : chantiers. Vert : repas.";
			widget.Get<ButtonWidget>("MD_RESUME").OnClick = () =>
			{
				foreach (var a in world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).Where(a => a.Owner == p && a.TraitOrDefault<MedievalWorker>() != null))
					world.IssueOrder(new Order("MedievalWork", a, false));
			};
			widget.Get<ButtonWidget>("MD_CANCEL").OnClick = () =>
			{
				foreach (var a in world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).Where(a => a.Owner == p && a.TraitOrDefault<MedievalBuilding>()?.Complete == false))
					world.IssueOrder(new Order("MedievalCancel", p.PlayerActor, Target.FromActor(a), false));
			};
			widget.Get<LabelWidget>("MD_MESSAGE").GetText = () => world.OrderGenerator is MedievalMessengerTargetGenerator mail ? mail.PreviewText
				: world.OrderGenerator is MedievalDemolitionOrderGenerator demolitionTool ? demolitionTool.PreviewText(world)
				: world.OrderGenerator is MedievalSiegeSupplyOrderGenerator ? "Cliquez votre trebuchet a ravitailler en pierres. Clic droit : annuler."
				: world.OrderGenerator is MedievalDecorationOrderGenerator decor ? decor.PreviewText(world)
				: world.OrderGenerator is MedievalFortificationOrderGenerator fortification ? fortification.PreviewText(world)
				: world.OrderGenerator is MedievalPlaceOrderGenerator placement && placement.HousePreviewText(world) is string housePreview ? housePreview
				: world.OrderGenerator is MedievalPlaceOrderGenerator marketPlacement && marketPlacement.LocalityPreviewText(world) is string localityPreview ? localityPreview
				: world.OrderGenerator is MedievalZoneOrderGenerator zone ? zone.PreviewText(world)
				: world.OrderGenerator is MedievalCemeteryOrderGenerator cemetery ? cemetery.PreviewText(world)
				: world.OrderGenerator is MedievalStockyardOrderGenerator yard ? yard.PreviewText()
				: world.OrderGenerator is MedievalLogisticsTargetGenerator logistics ? logistics.PreviewText : settlement.Message;
			Actor Stall() => world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).FirstOrDefault(a => a.Owner == p && a.TraitOrDefault<MedievalMarketStall>() != null);
			widget.Get("MD_STALL_PANEL").IsVisible = () => Stall() != null;
			widget.Get<LabelWidget>("MD_STALL_TITLE").GetText = () => "PETIT MARCHE - APPROVISIONNEMENT";
			widget.Get<LabelWidget>("MD_STALL_LINK").GetText = () => Stall() is Actor a && a.Trait<MedievalMarketStall>().Linked(a)
				? "Depuis " + MedievalLogistics.Name(a.Trait<MedievalMarketStall>().Market) : "Attend un grand marche termine.";
			widget.Get<LabelWidget>("MD_STALL_STOCK").GetText = () => Stall() is Actor a ? "Stock : " + string.Join(" | ", new[] { 1, 0, 3, 4 }.Select(r => $"{MedievalStockDisplay.Labels[r]} {a.Trait<MedievalStockpile>().Stock.Amount(r)}")) : "";
			widget.Get<LabelWidget>("MD_STALL_DEMAND").GetText = () => Stall() is Actor a ? "A livrer : " + string.Join(" | ", new[] { 1, 0, 3, 4 }.Select(r => $"{MedievalStockDisplay.Labels[r]} {a.Trait<MedievalMarketStall>().Request(a, r)}")) : "";
			widget.Get<LabelWidget>("MD_STALL_HINT").GetText = () => "Rayon 6 cases : repas, chantiers et forges.";
			widget.Get<LabelWidget>("MD_STALL_EQUIPMENT").GetText = () => MedievalEquipment.StockText(Stall());
			var stallLink = widget.Get<ButtonWidget>("MD_STALL_RELINK");
			stallLink.GetText = () => "Choisir un autre grand marche";
			stallLink.IsDisabled = () => world.Paused || Stall() is not Actor a || !MedievalStockpile.Active(a);
			stallLink.OnClick = () => { if (Stall() is Actor a) world.OrderGenerator = new MedievalLogisticsTargetGenerator(world, worldRenderer, a, "MedievalLinkStall"); };
			MedievalStorageInterface.Bind(widget, world, worldRenderer, "MD_STALL_YARD", Stall);
			widget.Get<LabelWidget>("MD_SELECTION").GetText = () =>
			{
				var a = world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).FirstOrDefault(a => a.Owner == p || a.Owner.NonCombatant
					&& a.TraitOrDefault<MedievalSource>() is MedievalSource source && source.Info.Natural != NaturalResourceKind.None);
				if (a == null) return "Stocks : entrepots et marches. Selectionnez un marche pour creer une charrette. B = bois, P = pierre.";
				if (a.TraitOrDefault<MedievalDemolition>() is { Requested: true } demolition) return demolition.Description;
				if (a.TraitOrDefault<MedievalFoundingCaravan>() is MedievalFoundingCaravan caravan) return caravan.Status;
				if (a.TraitOrDefault<MedievalChurch>() is MedievalChurch church && MedievalStockpile.Active(a)) return church.Status(a) + " | Recrutez un pretre puis envoyez les blesses par clic droit.";
				if (a.TraitOrDefault<MedievalPatient>()?.Church != null) return "Rejoint l'eglise / attend les soins | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalUndertaker>() is { } undertaker) return "Croque-mort : " + undertaker.Description(a) + " | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalPriest>() != null) return "Pretre : +10 moral a 5 cases | Soigne a son eglise | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalFirefighter>() is { Busy: true } firefighter) return firefighter.Description + " | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalBurnable>() is { Burning: true } burning) return $"Incendie : {(burning.Remaining + 24) / 25}s | Seaux verses : {burning.WaterBuckets} | Pertes : {burning.LostGoods} ressources. Les secours utilisent les puits proches.";
				if (a.TraitOrDefault<MedievalSource>() is MedievalSource natural && natural.Info.Natural != NaturalResourceKind.None) return natural.Description;
				if (a.TraitOrDefault<MedievalMessenger>() is MedievalMessenger messenger)
					return messenger.Status + " | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalCommander>() is MedievalCommander commander)
					return commander.Status + " | " + a.Trait<MedievalRest>().Description + " | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalSupplyCart>() is MedievalSupplyCart selectedCart)
					return selectedCart.LoadingDescription + " | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalTradeCart>() is MedievalTradeCart trader)
					return trader.Status + " | " + a.Trait<MedievalNeedsFood>().Description;
				if (a.TraitOrDefault<MedievalBarterCart>() is { } barterCart)
					return barterCart.Status + " | " + string.Join(", ", Enumerable.Range(0, StorageGoods.Count).Where(g => StorageGoods.Amount(a, g) > 0)
						.Select(g => $"{StorageGoods.Names[g]} {StorageGoods.Amount(a, g)}"));
				if (a.TraitOrDefault<MedievalAgriculturalArea>() is MedievalAgriculturalArea area)
				{
					var crop = a.Trait<MedievalSource>();
					return area.Description(a) + " | " + (!MedievalStockpile.Active(a) ? "Amenagement par les batisseurs"
						: area.Farm(a) == null ? "Ferme/elevage termine requis a proximite"
						: crop.Growth > 0 ? $"Croissance : {(crop.Growth + 24) / 25}s restantes"
						: crop.Remaining > 0 ? $"Production disponible : {crop.Remaining}" : $"Preparation : {crop.Prepared}/{crop.Info.CultivationWork}");
				}
				if (a.TraitOrDefault<MedievalBuilding>() is MedievalBuilding site && !site.Complete)
					return (a.TraitOrDefault<MedievalHouseAppearance>()?.Name ?? "Chantier") + " : " + (MedievalConstructionSupply.Sources(p, a.CenterPosition).Any()
						? "entrepot ou charrette a portee. Les batisseurs apportent les materiaux." : "hors portee des depots. Amenez une charrette chargee a moins de 8 cases.");
				if (a.TraitOrDefault<MedievalHouseAppearance>() is MedievalHouseAppearance house)
					return house.Name + $" | 8 places d'habitation | {a.Trait<MedievalShelter>().Occupants(a)}/8 lits reserves | Parcelle 2x2";
				if (a.TraitOrDefault<MedievalPackTent>() != null)
					return $"Pavillon : {a.Trait<MedievalShelter>().Occupants(a)}/6 places | Cercle de repos : 6 cases | "
						+ (a.Trait<MedievalCampArea>().RestEnabled ? "Repos ordonne" : "Attend le commandant") + " | Menu Campements & repos";
				if (a.TraitOrDefault<MedievalTentWorkshop>() is MedievalTentWorkshop tents)
					return $"Tisseur : 2 laine par tissu, 4 tissus + 4 bois par tente | {tents.ProductName} {tents.Progress}/{tents.WorkRequired} | Cibles : {tents.Target} tentes, {tents.ClothTarget} tissu | Campements & repos";
				if (a.Info.Name == "md.sheepfold") return $"Bergerie : {a.Trait<MedievalCampStore>().Stock.Amount(0)}/80 laine | Tracez un enclos et recrutez des eleveurs de moutons.";
				if (a.TraitOrDefault<MedievalMarketStall>() is MedievalMarketStall stall) return stall.Description(a);
				if (a.TraitOrDefault<MedievalMilitaryWorkshop>() is MedievalMilitaryWorkshop workshop)
					return workshop.Status + " | Artisans : " + workshop.StaffCount(a) + "/" + workshop.Info.MaxStaff + " | 1 logement et 2 nourriture/min chacun.";
				if (a.TraitOrDefault<MedievalWell>() != null) return "Puits : eau pour les incendies a moins de 16 cases. Les citoyens et la milice se reveillent pour intervenir.";
				if (a.TraitOrDefault<MedievalDecoration>() != null)
					return "Decoration terminee. Outil Demolir : transporteurs et batisseurs recuperent 20% des materiaux.";
				if (a.TraitOrDefault<MedievalFortification>() != null)
					return a.TraitOrDefault<MedievalTowerGarrison>() is MedievalTowerGarrison garrison ? garrison.Description
						: a.TraitOrDefault<Gate>() is Gate gate ? "Porte large : 3 cases, deux battants | " + (gate.Position == gate.OpenPosition ? "Ouverte" : "Fermee / en mouvement") + " | Passage des allies"
						: "Palissade en bois | Bloque le passage | Raccords automatiques";
				var stock = a.TraitOrDefault<MedievalStockpile>()?.Stock;
				if (stock == null) return string.Join(" | ", new[] { a.TraitOrDefault<MedievalAnimal>()?.Description, a.TraitOrDefault<MedievalHerding>() is { Busy: true } guide ? guide.Status : null, a.TraitOrDefault<MedievalRest>()?.Description, a.TraitOrDefault<MedievalMorale>()?.Description, a.TraitOrDefault<MedievalDrummer>()?.Description(a), a.TraitOrDefault<MedievalStandardBearer>()?.Description(a), a.TraitOrDefault<MedievalWorker>()?.Description, a.TraitOrDefault<MedievalNeedsFood>()?.Description }.Where(s => !string.IsNullOrEmpty(s)));
				var text = string.Join(" | ", Enumerable.Range(0, Inventory.ResourceCount).Where(r => stock.Capacity(r) > 0)
					.Select(r => $"{MedievalStockDisplay.Labels[r]} {stock.Amount(r)}/{stock.Capacity(r)} (res. {stock.Reserved(r)}, entrant {stock.Incoming(r)})"));
				if (a.TraitOrDefault<MedievalFoodProvider>() is MedievalFoodProvider provider) text += $" | Rayon {provider.Info.Radius.Length / 1024} cases";
				if (a.TraitOrDefault<MedievalMarket>() is MedievalMarket selectedMarket) text += " | " + selectedMarket.Status;
				if (a.TraitOrDefault<MedievalEquipmentStore>() is MedievalEquipmentStore equipmentStore) text += " | " + MedievalEquipment.Summary(equipmentStore.Stock);
				else if (a.TraitOrDefault<MedievalConstructionSupply>() is MedievalConstructionSupply constructionSupply) text += $" | Chantiers : rayon {constructionSupply.Info.Radius.Length / 1024} cases (bleu)";
				return text;
			};
			MedievalTradeInterface.Bind(widget, world, worldRenderer);
			MedievalDecorationInterface.Bind(widget, world, worldRenderer);
			MedievalFortificationInterface.Bind(widget, world, worldRenderer);
			MedievalMilitaryInterface.Bind(widget, world, worldRenderer);
		}
	}
}
