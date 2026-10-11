using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalMilitaryInterface
	{
		public static void Bind(Widget widget, World world, WorldRenderer renderer)
		{
			var p = world.LocalPlayer;
			var economy = p.PlayerActor.Trait<MedievalEquipmentEconomy>();
			Actor Selected(bool forge) => world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).FirstOrDefault(a => a.Owner == p && a.TraitOrDefault<MedievalMilitaryWorkshop>()?.Info.Forge == forge);
			for (var i = 0; i < 2; i++)
			{
				var forge = i == 0; var type = MedievalMilitaryWorkshop.Types[i]; var prefix = forge ? "MD_FORGE" : "MD_BARRACKS";
				var build = widget.Get<ButtonWidget>("MD_RECRUIT_" + (i + 6));
				build.GetText = () => forge ? "Forge" : "Caserne";
				build.GetTooltipText = () => forge ? "Forge : 80 bois, 20 pierre" : "Caserne : 100 bois, 40 pierre";
				build.GetTooltipDesc = () => forge ? "Fabrique des arcs, epees et armures selon vos objectifs de stock.\nRecrutez ensuite un forgeron dans le batiment."
					: "Forme les soldats avec les provisions du depot.\nRecrutez d'abord un recruteur dans le batiment.";
				build.IsDisabled = () => world.Paused;
				build.OnClick = () => world.OrderGenerator = new MedievalPlaceOrderGenerator(world, renderer, type);
				widget.Get(prefix + "_PANEL").IsVisible = () => Selected(forge) != null && !world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).Any(a => a.TraitOrDefault<MedievalMessenger>() != null);
				widget.Get<LabelWidget>(prefix + "_TITLE").GetText = () => forge ? "FORGE - OBJECTIFS DE STOCK DU VILLAGE" : "CASERNE - RECRUTEMENT MILITAIRE";
				var staff = widget.Get<ButtonWidget>(prefix + "_STAFF");
				staff.GetText = () => Selected(forge) is not Actor a ? ""
					: a.Trait<MedievalMilitaryWorkshop>().StaffTraining > 0 ? "Recrutement en cours..."
					: forge ? $"Forgerons {a.Trait<MedievalMilitaryWorkshop>().StaffCount(a)}/3" + (a.Trait<MedievalMilitaryWorkshop>().StaffCount(a) < 3 ? " | Recruter : 10 nourriture" : " | Complet")
					: a.Trait<MedievalMilitaryWorkshop>().Staff(a) != null ? "Recruteur present" : "Recruter un recruteur : 10 nourriture";
				staff.IsDisabled = () => world.Paused || Selected(forge) is not Actor a || !a.Trait<MedievalMilitaryWorkshop>().CanHire(a);
				staff.OnClick = () => { if (Selected(forge) is Actor a) world.IssueOrder(new Order("MedievalHireArtisan", a, false)); };
				widget.Get<LabelWidget>(prefix + "_STATUS").GetText = () => Selected(forge) is Actor a
					? !MedievalStockpile.Active(a) ? "Attendez la fin du chantier." : !forge && a.Trait<MedievalMilitaryWorkshop>() is { Recruit: >= 0, Progress: 0 } workshop && !workshop.Ready(a)
						? workshop.MissingSupplies(a) : a.Trait<MedievalMilitaryWorkshop>().Status : "";
				widget.Get<LabelWidget>(prefix + "_WORKER").GetText = () => Selected(forge) is Actor a
					? forge ? $"{a.Trait<MedievalMilitaryWorkshop>().StaffCount(a)} forgeron(s), dont {a.Trait<MedievalMilitaryWorkshop>().Team(a).Count(s => s.Trait<MedievalWorker>().IsWorking)} au travail | 1 logement chacun"
					: a.Trait<MedievalMilitaryWorkshop>().Staff(a)?.Trait<MedievalWorker>().Description ?? "Artisan : 1 logement et 2 nourriture/min." : "";
			}
			for (var r = 0; r < 3; r++)
			{
				var gear = r;
				widget.Get<LabelWidget>($"MD_FORGE_TARGET_{r}").GetText = () => $"{MedievalEquipment.Names[gear]} : objectif {economy.Target(gear)} | Stock {MedievalEquipment.Available(p, gear)} libres";
				var recipe = MedievalEquipment.Recipes[r];
				widget.Get<LabelWidget>($"MD_FORGE_RECIPE_{r}").GetText = () => $"1 {MedievalEquipment.Names[gear].ToLowerInvariant()} = {recipe[0]} bois + {recipe[3]} pierre";
				foreach (var sign in new[] { "MINUS", "PLUS" })
				{
					var add = sign == "PLUS";
					var button = widget.Get<ButtonWidget>($"MD_FORGE_{sign}_{r}");
					button.GetText = () => add ? "+" : "-";
					button.IsDisabled = () => world.Paused || (add ? economy.Target(gear) >= 100 : economy.Target(gear) == 0);
					button.OnClick = () => world.IssueOrder(new Order("MedievalEquipmentTarget", p.PlayerActor, false) { ExtraData = (uint)gear, TargetString = add ? "+" : "-" });
				}
			}
			widget.Get<LabelWidget>("MD_FORGE_HINT").GetText = () => "3 artisans max. Livraison au stockage le plus proche.";
			widget.Get<LabelWidget>("MD_FORGE_INPUTS").GetText = () => Selected(true) is Actor a
				? $"Materiaux livres : {a.Trait<MedievalMilitaryWorkshop>().Inputs.Amount(0)} bois / {a.Trait<MedievalMilitaryWorkshop>().Inputs.Amount(3)} pierre" : "";
			for (var i = 0; i < MedievalEquipment.Soldiers.Length; i++)
			{
				var index = i; var type = MedievalEquipment.Soldiers[i]; var cost = MedievalSettlement.Cost(p, type);
				var button = widget.Get<ButtonWidget>("MD_BARRACKS_RECRUIT_" + i);
				button.GetText = () => "";
				var caption = widget.Get<LabelWidget>($"MD_BARRACKS_RECRUIT_{i}_NAME");
				caption.GetText = () => MedievalEquipment.SoldierNames[index] + (cost.Elite ? " | ELITE" : "");
				caption.GetColor = () => button.IsDisabled() ? Color.Gray : cost.Elite ? Color.FromArgb(236, 199, 111) : Color.FromArgb(238, 233, 212);
				button.IsDisabled = () => world.Paused || Selected(false) is not Actor a || !a.Trait<MedievalMilitaryWorkshop>().CanRecruit(a);
				button.OnClick = () => { if (Selected(false) is Actor a) world.IssueOrder(new Order("MedievalTrainSoldier", a, false) { TargetString = type }); };
				var kit = string.Join(" + ", Enumerable.Range(0, 3).Where(r => MedievalEquipment.Kits[index][r] > 0).Select(r => MedievalEquipment.Names[r]));
				widget.Get<LabelWidget>($"MD_BARRACKS_RECRUIT_{i}_COST").GetText = () => cost + (kit.Length > 0 ? " | " + kit : " | sans equipement");
				button.GetTooltipText = () => MedievalEquipment.SoldierNames[index] + " : " + cost;
				button.GetTooltipDesc = () => MedievalEquipment.SoldierRoles[index]
					+ (cost.Elite ? "\nViande obligatoire, livree par le recruteur avant formation." : "")
					+ "\n1 logement et 2 nourriture/min. Ravitaillement par marche ou charrette.";
			}
			var messenger = widget.Get<ButtonWidget>("MD_BARRACKS_MESSENGER");
			messenger.GetText = () => "Messager | 20 nourriture";
			messenger.IsDisabled = () => world.Paused || Selected(false) is not Actor a || !a.Trait<MedievalMilitaryWorkshop>().CanRecruit(a);
			messenger.OnClick = () => { if (!messenger.IsDisabled() && Selected(false) is Actor a) world.IssueOrder(new Order("MedievalTrainSoldier", a, false) { TargetString = "md.messenger" }); };
			var standard = widget.Get<ButtonWidget>("MD_BARRACKS_STANDARD");
			standard.GetText = () => "Porte-drapeau | 25 N + 2 tissu";
			standard.GetTooltipText = () => "Porte-drapeau : 25 nourriture + 2 tissu";
			standard.GetTooltipDesc = () => "Le recruteur apporte 2 tissus du depot ou du marche. Le tisseur les fabrique avec la laine des moutons.\nSoutien sans attaque. Moral allies +15, rayon 5 cases.\nPlusieurs drapeaux ne se cumulent pas. Avec un tambour : maximum +30.\nFaim : +7 ; famine : aucun bonus. 1 logement et 2 nourriture/min.\nEmbleme et couleur choisis dans votre marche.";
			standard.IsDisabled = () => world.Paused || Selected(false) is not Actor a || !a.Trait<MedievalMilitaryWorkshop>().CanRecruit(a);
			standard.OnClick = () => { if (!standard.IsDisabled() && Selected(false) is Actor a) world.IssueOrder(new Order("MedievalTrainSoldier", a, false) { TargetString = "md.standardbearer" }); };
			var cancel = widget.Get<ButtonWidget>("MD_BARRACKS_CANCEL");
			var commander = widget.Get<ButtonWidget>("MD_BARRACKS_COMMANDER");
			commander.GetText = () => "Commandant | 40 nourr. + 15 viande";
			commander.GetTooltipText = () => "Commandant : officier du campement";
			commander.GetTooltipDesc = () => "40 nourriture, 15 viande, une epee et une armure.\n1 logement et 2 nourriture/min. Se recrute avec le recruteur.\nVa chercher les tentes dans les charrettes et monte les pavillons.\nOrdonne le repos et le reveil dans leurs zones de 6 cases.";
			commander.IsDisabled = () => world.Paused || Selected(false) is not Actor a || !a.Trait<MedievalMilitaryWorkshop>().CanRecruit(a);
			commander.OnClick = () => { if (!commander.IsDisabled() && Selected(false) is Actor a) world.IssueOrder(new Order("MedievalTrainSoldier", a, false) { TargetString = "md.commander" }); };
			cancel.GetText = () => "Annuler formation";
			cancel.IsDisabled = () => world.Paused || Selected(false)?.Trait<MedievalMilitaryWorkshop>().Recruit is not >= 0;
			cancel.OnClick = () => { if (Selected(false) is Actor a) world.IssueOrder(new Order("MedievalCancelTraining", a, false)); };
			widget.Get<LabelWidget>("MD_BARRACKS_HINT").GetText = () => "Chaque soldat : 1 logement et 2 nourriture/min.";
			widget.Get<LabelWidget>("MD_BARRACKS_INPUTS").GetText = () => Selected(false) is Actor a
				? $"Livres : {a.Trait<MedievalMilitaryWorkshop>().Inputs.Amount(1)} nourriture / {a.Trait<MedievalMilitaryWorkshop>().Inputs.Amount(2)} viande / {a.Trait<MedievalCampStore>().Stock.Amount(MedievalCampStore.Cloth)} tissu" : "";
		}
	}
}
