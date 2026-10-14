using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalNationalPrideInterface
	{
		public static void Bind(Widget widget, World world)
		{
			var pride = MedievalNationalPride.For(world.LocalPlayer);
			var button = widget.Get<ButtonWidget>("MD_PRIDE_OPEN");
			var panel = widget.Get("MD_PRIDE_PANEL");
			var open = false;
			button.IsVisible = () => pride != null;
			panel.IsVisible = () => open && pride != null;
			if (pride == null) return;
			var state = pride.State;
			button.GetText = () => $"Fierte : {state.Value}/100";
			button.GetTooltipText = () => "Fierte nationale";
			button.GetTooltipDesc = () => "Or, nourriture, armee, absence de famine et respect des morts.\nCliquez pour consulter les causes et l'effet sur le moral.";
			button.IsHighlighted = () => open;
			button.OnClick = () => open = !open;
			var close = widget.Get<ButtonWidget>("MD_PRIDE_CLOSE");
			close.GetText = () => "Fermer"; close.OnClick = () => open = false;
			widget.Get<LabelWidget>("MD_PRIDE_TITLE").GetText = () => "NATION : " + MedievalHeraldry.For(world.LocalPlayer).CountryName;
			var score = widget.Get<LabelWidget>("MD_PRIDE_SCORE");
			score.GetText = () => $"Fierte {state.Value}/100 - {state.Status} | {(state.Target > state.Value ? "Hausse" : state.Target < state.Value ? "Baisse" : "Stable")} vers {state.Target}";
			score.GetColor = () => state.Value < 45 ? Color.Orange : state.Value >= 65 ? Color.LightGreen : Color.White;
			widget.Get<ProgressBarWidget>("MD_PRIDE_BAR").GetPercentage = () => state.Value;
			widget.Get<LabelWidget>("MD_PRIDE_GOLD").GetText = () => $"Tresor : +{state.GoldPoints}/15\n{pride.Facts.Gold} or disponible dans vos reserves.";
			widget.Get<LabelWidget>("MD_PRIDE_FOOD").GetText = () => $"Nourriture : +{state.FoodPoints}/20\n{pride.Facts.Food} repas en reserve, besoins {pride.Facts.FoodPerMinute}/min.";
			widget.Get<LabelWidget>("MD_PRIDE_ARMY").GetText = () => $"Armee : +{state.ArmyPoints}/15\nSoldats armes : sante et ravitaillement comptent.";
			widget.Get<LabelWidget>("MD_PRIDE_WELFARE").GetText = () => $"Bien-etre : {state.WelfarePoints:+0;-0;0} (maximum +20)\n{pride.Facts.Hungry} affames, dont {pride.Facts.Starving} en famine.";
			widget.Get<LabelWidget>("MD_PRIDE_BURIALS").GetText = () => $"Respect des morts : +{state.BurialPoints}/10\n{state.Burials} enterres, {state.Unburied} sans sepulture dans votre nation.";
			widget.Get<LabelWidget>("MD_PRIDE_EFFECT").GetText = () => $"Effet sur le moral de vos soldats : {state.MoraleBonus:+0;-0;0}\nSocle +20. Evolution progressive, bonus limite a +/-10.";
			widget.Get<LabelWidget>("MD_PRIDE_HINT").GetText = () => state.FamineMemory > 0
				? $"Deces par famine recent : cible limitee a 50 pendant encore {state.FamineMemory} s."
				: pride.Facts.Starving > 0 ? "Famine en cours : la cible ne peut pas depasser 60. Ravitaillez vos habitants."
				: "Les besoins augmentent avec la population. Enterrer tous ses morts restaure le respect, sans bonus par deces.";
		}
	}
}
