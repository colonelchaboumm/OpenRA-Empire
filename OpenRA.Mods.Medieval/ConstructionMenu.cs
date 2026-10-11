using System;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct MedievalMenuEntry(string Type, string Name, string Description);
	public readonly record struct MedievalMenuCategory(string Name, string Hint, MedievalMenuEntry[] Items);

	public sealed class MedievalConstructionMenuLogic : ChromeLogic
	{
		public const int IconSlotsPerCategory = 32;
		public static readonly MedievalMenuCategory[] Categories =
		[
			new("Ville", "Habitations, reserves et ravitaillement.\nB bois / P pierre / O or / N nourriture.", [
				new("md.house", "Maison", "8 places. Architecture choisie au hasard a chaque construction."),
				new("md.warehouse", "Entrepot", "Stockage des ressources. Selectionnez le depot pour tracer son aire exterieure."),
				new("md.market", "Marche", "Ravitaille les habitants. Permet les routes commerciales et les charrettes."),
				new("md.marketstall", "Petit marche", "Relais local de nourriture et de materiaux. Peut fonder un village eloigne."),
				new("md.church", "Eglise", "120 bois, 100 pierre et 60 or. Recrute les pretres.\nEnvoyez les blesses par clic droit pour recevoir des soins.\nUn pretre doit etre present, eveille et nourri.")]),
			new("Nourriture", "Tracez deux coins pour dimensionner\nles cultures et les enclos (2 a 10 cases).", [
				new("md.farm", "Ferme", "Accueille les fermiers. Ajoutez des cultures a proximite."),
				new("md.field", "Cultures", "Zone de cereales. Cout et nombre de fermiers selon la surface."),
				new("md.ranch", "Elevage bovin", "Les eleveurs capturent les bovins libres et livrent la viande des adultes en surplus.\nDeux reproducteurs sont conserves."),
				new("md.pasture", "Enclos bovin", "Commence vide : 1 bovin pour 4 cases. Deux adultes pour reproduire.\nAu moins 8 cases pour un couple, 12 pour produire de la viande."),
				new("md.sheepfold", "Bergerie", "Abrite la laine recoltee par les eleveurs de moutons. Ajoutez un enclos."),
				new("md.sheeppasture", "Moutons", "Commence vide : le berger capture les moutons libres. Deux adultes pour reproduire.\n1 mouton pour 4 cases ; tonte sans tuer l'animal."),
				new("md.huntinglodge", "Chasse", "Cabane du chasseur : reçoit la nourriture du gibier. Construisez près des bois ; un transporteur emporte le stock.")]),
			new("Ressources", "Recrutez les ouvriers dans Citoyens.\nLes transporteurs livrent les reserves.", [
				new("md.sawmill", "Scierie", "Les bucherons y deposent le bois coupe dans les forets."),
				new("md.quarry", "Carriere", "Les tailleurs de pierre y deposent la pierre extraite."),
				new("md.goldmine", "Mine d'or", "Les mineurs extraient les gisements limites puis deposent l'or ici.\nUn transporteur livre ensuite l'or aux reserves.")]),
			new("Militaire", "Caserne : troupes et commandant.\nPavillons : montage et repos.", [
				new("md.barracks", "Caserne", "Recrute les soldats, musiciens, porte-drapeaux et messagers."),
				new("md.forge", "Forgeron", "Fabrique arcs, epees et armures. Jusqu'a 3 forgerons et cibles de stock reglables."),
				new("md.siegeworkshop", "Atelier siege", "Fabrique les beliers et les trebuchets, servis par les soldats."),
				new("md.tentweaver", "Tisseur", "2 laine = 1 tissu. 4 tissus + 4 bois = 1 pavillon pour 6 soldats.\nRecrutez un tisseur dans Citoyens. Stocks cibles dans Campements & repos."),
				new("md.palisade", "Palissade", "Deux clics pour tracer une ligne de palissades."),
				new("md.woodgate", "Porte en bois", "Grande porte a ouverture laterale. Tourner change son orientation."),
				new("md.woodtower", "Tour en bois", "Defense automatique et 2 places pour vos archers.\nArchers selectionnes : clic droit sur la tour.\nGarnison : portee +50%, vision 12 cases.")]),
			new("Decorations", "Clotures et chemins : deux clics.\nClic droit pour terminer la pose.", [
				new("md.well", "Puits", "Fournit l'eau aux citoyens et miliciens pour eteindre les incendies proches, meme la nuit."),
				new("md.gardenfence", "Cloture", "Tracez une cloture de jardin. Prevoyez des ouvertures."),
				new("md.gardengate", "Portillon", "Passage dans une cloture. Tourner change son orientation."),
				new("md.flowerbed", "Fleurs", "Parterre de fleurs pour embellir le village."),
				new("md.banner", "Drapeau", "Necessite 2 tissus livres par un batisseur. Moutons > laine > tisseur > tissu.\nEmbleme et couleur du pays, choisis depuis le marche."),
				new("md.stonepath", "Chemin pierre", "Deux clics pour tracer un chemin pave."),
				new("md.torch", "Torche", "Eclaire les alentours pendant la nuit."),
				new("md.plaza", "Placette", "Petite place pavee de 2 x 2 cases. Traversable et raccordee aux chemins."),
				new("md.plazalarge", "Place pavee", "Place de 3 x 3 cases, avec bordure et rosace en pierre. Traversable."),
				new("md.statueknight", "Chevalier", "Statue de chevalier sur un socle de pierre. Peut se placer sur une place pavee."),
				new("md.statuelion", "Lion sculpte", "Lion de pierre sur un piedestal. Peut se placer sur une place pavee."),
				new("md.plantedoak", "Chene pave", "Chene plante dans un entourage en pierre, assorti aux chemins."),
				new("md.plantedcypress", "Cypres pave", "Cypres plante dans une bordure de pierre. Arbre decoratif, sans abattage."),
				new("md.gildedknight", "Chevalier or", "Statue de pierre : heaume, bouclier et socle ornes d'or.\n8 bois, 40 pierre, 20 or. Peut se placer sur une place terminee."),
				new("md.gildedlion", "Lion dore", "Lion de pierre avec couronne, collier et ornements dores.\n6 bois, 32 pierre, 14 or. Peut se placer sur une place terminee."),
				new("md.gildedplaza", "Placette or", "Place de 2 x 2 cases avec une bordure et un motif incrustes d'or.\n24 pierre, 12 or. Traversable ; accueille les statues."),
				new("md.gildedplazalarge", "Place royale", "Place de 3 x 3 cases : rosace et motifs dores dans les paves.\n45 pierre, 25 or. Traversable ; accueille les statues."),
				new("md.gildedfence", "Grille fer", "Grille en fer sombre, pointes et attaches dorees.\nPar case : 2 bois, 4 pierre, 2 or. Deux clics pour tracer ; bloque le passage."),
				new("md.gildedgate", "Portillon or", "Portillon ouvert assorti aux grilles.\n4 bois, 6 pierre, 3 or. Passage libre ; Tourner change son orientation.")]),
			new("Citoyens", "Habitants : logement et nourriture.\nForgerons : depuis la forge.", [
				new("md.builder", "Batisseur", "Transporte les materiaux jusqu'aux chantiers puis construit."),
				new("md.hauler", "Transporteur", "Transporte physiquement les ressources et les equipements."),
				new("md.lumberjack", "Bucheron", "Coupe les arbres puis rapporte le bois a la scierie."),
				new("md.stonecutter", "Tailleur pierre", "Extrait les roches et rapporte la pierre a la carriere."),
				new("md.farmer", "Fermier", "Cultive et recolte les cereales dans les parcelles."),
				new("md.breeder", "Eleveur bovin", "Capture et transfere les bovins un par un ; livre la viande du surplus."),
				new("md.shepherd", "Berger", "Capture et transfere les moutons un par un ; tond et livre la laine."),
				new("md.weaver", "Tisseur", "Transforme la laine des moutons en tissu et en pavillons militaires dans l'atelier de tissage."),
				new("md.hunter", "Chasseur", "Chasse le gibier près d'une cabane puis y rapporte la nourriture. Le gibier est limité et la chasse ralentit en hiver."),
				new("md.goldminer", "Mineur d'or", "Extrait l'or avec sa pioche, puis le rapporte a la mine d'or.")]),
			new("Defense", "Feux : vision et vigilance des milices.\nFortifications : controle des acces.", [
				new("md.campfire", "Feu de camp", "Eclaire la nuit. Milices alliees a 4 cases : vision +25%, detection des saboteurs de 4 a 6 cases.\nLa chaleur fait fondre la neige autour du foyer."),
				new("md.palisade", "Palissade", "Deux clics pour tracer une ligne de palissades."),
				new("md.woodgate", "Porte en bois", "Porte large. Tourner change son orientation."),
				new("md.woodtower", "Tour en bois", "Defense et garnison pour deux archers.")])
		];

		[ObjectCreator.UseCtor]
		public MedievalConstructionMenuLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			var p = world.LocalPlayer;
			if (p == null) return;
			var settlement = p.PlayerActor.Trait<MedievalSettlement>();
			var category = 0; var page = 0;
			foreach (var id in Enumerable.Range(0, 9).Select(i => "MD_BUILD_" + i)
				.Concat(Enumerable.Range(0, 8).Select(i => "MD_RECRUIT_" + i))
				.Concat(new[] { "MD_FORT_OPEN", "MD_DECOR_OPEN", "MD_RECRUIT_COST", "MD_MILITIA_COST", "MD_ARMS_COST" }))
				widget.Get(id).IsVisible = () => false;
			for (var c = 0; c < Categories.Length; c++)
			{
				var index = c;
				var tab = widget.Get<ButtonWidget>("MD_MENU_TAB_" + c);
				tab.GetText = () => Categories[index].Name;
				tab.IsHighlighted = () => category == index;
				tab.OnClick = () => { category = index; page = 0; world.CancelInputMode(); };
			}
			widget.Get<LabelWidget>("MD_MENU_TITLE").GetText = () => category == 5 ? "CITOYENS" : Categories[category].Name.ToUpperInvariant();
			widget.Get<LabelWidget>("MD_MENU_HINT").GetText = () => Categories[category].Hint;
			foreach (var delta in new[] { -1, 1 })
			{
				var b = widget.Get<ButtonWidget>(delta < 0 ? "MD_MENU_PREV" : "MD_MENU_NEXT");
				b.GetText = () => delta < 0 ? "Precedent" : "Suivant";
				b.IsVisible = () => Categories[category].Items.Length > 8;
				b.IsDisabled = () => page + delta < 0 || (page + delta) * 8 >= Categories[category].Items.Length;
				b.OnClick = () => { if (!b.IsDisabled()) { page += delta; world.CancelInputMode(); } };
			}
			widget.Get<LabelWidget>("MD_MENU_PAGE").GetText = () => Categories[category].Items.Length > 8 ? $"{page + 1}/{(Categories[category].Items.Length + 7) / 8}" : "";
			widget.Get<LabelWidget>("MD_MENU_STOCK").GetText = () => category == 3
				? $"Arcs {MedievalEquipment.Amount(p, 0)} | Epees {MedievalEquipment.Amount(p, 1)} | Armures {MedievalEquipment.Amount(p, 2)}\nLaine {MedievalCampStore.Amount(p, 0)} | Pavillons {MedievalCampStore.Amount(p, 1)} | Tissu {MedievalCampStore.Amount(p, 2)}"
				: category == 5 && settlement.Recruiting ? "Recrutement en cours..." : "B bois / P pierre / O or / N nourriture / T tissu";
			for (var i = 0; i < 8; i++)
			{
				var index = i;
				bool Visible() => page * 8 + index < Categories[category].Items.Length;
				MedievalMenuEntry Entry() => Categories[category].Items[page * 8 + index];
				var button = widget.Get<ButtonWidget>("MD_MENU_ITEM_" + i);
				button.IsVisible = Visible;
				button.GetText = () => "";
				button.IsDisabled = () => world.Paused || !Visible() || (category == 5
					? settlement.Recruiting || !MedievalSettlement.Warehouses(p).Any()
						|| MedievalSettlement.Population(p) + MedievalSettlement.PendingPopulation(p) >= MedievalSettlement.Housing(p)
						|| !MedievalSettlement.Cost(p, Entry().Type).CanPay(MedievalSettlement.RecruitmentStores(p).Select(a => a.Trait<MedievalStockpile>().Stock))
					: Entry().Type == "md.house" && settlement.HousePlacementPending);
				var label = widget.Get<LabelWidget>($"MD_MENU_ITEM_{i}_NAME");
				label.GetText = () => Visible() ? Entry().Name : "";
				label.GetColor = () => button.IsDisabled() ? Color.Gray : Color.FromArgb(238, 233, 212);
				widget.Get<LabelWidget>($"MD_MENU_ITEM_{i}_COST").GetText = () => !Visible() ? "" : category == 5
					? $"{MedievalSettlement.Cost(p, Entry().Type).Food} N" : AgriculturalZone.IsKind(Entry().Type) ? "Tracer zone"
					: CostText(Entry().Type);
				var icon = widget.Get<SpriteWidget>($"MD_MENU_ITEM_{i}_ICON");
				icon.GetSprite = () => !Visible() ? null : category == 6 && index == 0 ? world.Map.Sequences.GetSequence("md.fire-icons", "idle").GetSprite(0)
					: world.Map.Sequences.GetSequence("md.construction-icons", "idle").GetSprite(category == 6 ? 3 * IconSlotsPerCategory + 3 + index : category * IconSlotsPerCategory + page * 8 + index);
				icon.GetPalette = () => (Visible() && Entry().Type is "md.market" or "md.marketstall" ? "md-market-livery" : "md-livery") + p.InternalName;
				button.GetTooltipText = () => Visible() ? Entry().Name : "";
				button.GetTooltipDesc = () => Visible() ? Entry().Description + (category == 5 ? "\nUne place d'habitation. Necessite de la nourriture en continu." : "\nLes batisseurs apportent les materiaux sur place.") : "";
				button.OnClick = () =>
				{
					if (button.IsDisabled()) return;
					var type = Entry().Type;
					if (category == 5) world.IssueOrder(new Order("MedievalRecruit", p.PlayerActor, false) { TargetString = type });
					else if (AgriculturalZone.IsKind(type)) world.OrderGenerator = new MedievalZoneOrderGenerator(world, worldRenderer, type);
					else if (MedievalFortifications.Types.Contains(type)) world.OrderGenerator = new MedievalFortificationOrderGenerator(world, worldRenderer, type);
					else if (MedievalDecorations.Types.Contains(type)) world.OrderGenerator = new MedievalDecorationOrderGenerator(world, worldRenderer, type);
					else world.OrderGenerator = new MedievalPlaceOrderGenerator(world, worldRenderer, type);
				};
			}
			var rotate = widget.Get<ButtonWidget>("MD_MENU_ROTATE");
			rotate.GetText = () => "Tourner";
			rotate.IsVisible = () => category is 3 or 4 or 6;
			rotate.IsDisabled = () => world.Paused || !(world.OrderGenerator is MedievalDecorationOrderGenerator d && MedievalDecorations.Rotatable(d.Kind)
				|| world.OrderGenerator is MedievalFortificationOrderGenerator f && MedievalFortifications.Rotatable(f.Kind));
			rotate.OnClick = () => { if (world.OrderGenerator is MedievalDecorationOrderGenerator d) d.Rotate(); else if (world.OrderGenerator is MedievalFortificationOrderGenerator f) f.Rotate(); };
			var remove = widget.Get<ButtonWidget>("MD_MENU_REMOVE");
			remove.GetText = () => "Demolir";
			remove.IsVisible = () => true;
			remove.IsDisabled = () => world.Paused;
			remove.IsHighlighted = () => world.OrderGenerator is MedievalDemolitionOrderGenerator;
			remove.GetTooltipText = () => "Outil bulldozer : demolition";
			remove.GetTooltipDesc = () => MedievalDemolitionOrderGenerator.Help;
			remove.OnClick = () => { if (!remove.IsDisabled()) world.OrderGenerator = new MedievalDemolitionOrderGenerator(world, worldRenderer); };
			string CostText(string type)
			{
				var building = world.Map.Rules.Actors[type].TraitInfo<MedievalBuildingInfo>();
				var cost = building.Cost;
				return (cost.Length > 4 && cost[4] > 0 ? $"{cost[0]}B{cost[3]}P{cost[4]}O" : $"{cost[0]}B {cost[3]}P") + (building.Cloth > 0 ? $" {building.Cloth}T" : "");
			}
		}
	}
}
