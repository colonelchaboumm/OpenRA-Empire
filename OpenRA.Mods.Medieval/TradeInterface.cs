using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalTradeInterface
	{
		public static void Bind(Widget widget, World world, WorldRenderer renderer)
		{
			var owner = world.LocalPlayer;
			Actor Selected<T>() => world.Selection.Actors.FirstOrDefault(a => a.Owner == owner && a.IsInWorld && a.TraitOrDefault<T>() != null);
			Actor Market() => Selected<MedievalTradeMarket>();
			Actor Cart() => Selected<MedievalTradeCart>();
			Actor Supply() => Selected<MedievalSupplyCart>();
			void Target(Actor actor, string command) { if (actor != null) world.OrderGenerator = new MedievalLogisticsTargetGenerator(world, renderer, actor, command); }
			widget.Get("MD_TRADE_PANEL").IsVisible = () => Market() != null && Selected<MedievalStockyard>() == null && Supply() == null;
			widget.Get<LabelWidget>("MD_TRADE_TITLE").GetText = () => "MARCHE - ROUTE COMMERCIALE";
			var link = widget.Get<ButtonWidget>("MD_TRADE_LINK");
			link.GetText = () => "Relier un marche";
			link.GetTooltipText = () => "Creer une route entre deux marches";
			link.GetTooltipDesc = () => "Cliquez un autre marche termine. Une liaison par marche.\nLes charrettes marchandes effectuent les allers-retours.\nChaque ressource circule dans un seul sens, choisi ici.";
			link.IsDisabled = () => world.Paused || Market() is not Actor market || !MedievalStockpile.Active(market);
			link.OnClick = () => Target(Market(), "MedievalLinkMarkets");
			var unlink = widget.Get<ButtonWidget>("MD_TRADE_UNLINK");
			unlink.GetText = () => "Supprimer la liaison";
			unlink.GetTooltipText = () => "Arreter les echanges entre ces marches";
			unlink.GetTooltipDesc = () => "Les charrettes s'arretent et conservent leur cargaison.\nSelectionnez-les pour rapatrier les marchandises.";
			unlink.IsDisabled = () => world.Paused || Market()?.Trait<MedievalTradeMarket>().Partner == null;
			unlink.OnClick = () => { if (Market() is Actor market) world.IssueOrder(new Order("MedievalUnlinkMarkets", market, false)); };
			widget.Get<LabelWidget>("MD_TRADE_PARTNER").GetText = () => Market() is Actor market && market.Trait<MedievalTradeMarket>().Linked(market)
				? MedievalLogistics.Name(market) + " <-> " + MedievalLogistics.Name(market.Trait<MedievalTradeMarket>().Partner) : "Aucune liaison : reliez un second marche termine.";
			widget.Get<LabelWidget>("MD_TRADE_EXPORT_LABEL").GetText = () => "Exporter depuis ce marche :";
			foreach (var r in new[] { 1, 0, 3, 2, 4 })
			{
				var resource = r;
				var button = widget.Get<ButtonWidget>("MD_TRADE_EXPORT_" + r);
				button.GetText = () => (Market()?.Trait<MedievalTradeMarket>().Exports(resource) == true ? "[X] " : "[ ] ") + (resource == 1 ? "Nourr." : MedievalStockDisplay.Labels[resource]);
				button.IsDisabled = () => world.Paused || Market() is not Actor market || !market.Trait<MedievalTradeMarket>().Linked(market);
				button.GetTooltipText = () => "Exporter : " + MedievalStockDisplay.Labels[resource];
				button.GetTooltipDesc = () => "Les transporteurs apportent cette ressource des depots locaux.\nLa cocher ici desactive son export dans l'autre sens.\n40 nourriture restent au marche pour les habitants.";
				button.OnClick = () => { if (Market() is Actor market) world.IssueOrder(new Order("MedievalTradeExport", market, false) { ExtraData = (uint)resource }); };
			}
			widget.Get<LabelWidget>("MD_TRADE_IMPORTS").GetText = () => Market() is Actor market && market.Trait<MedievalTradeMarket>().Linked(market)
				? "Retour : " + market.Trait<MedievalTradeMarket>().Partner.Trait<MedievalTradeMarket>().ExportDescription : "Le retour se configure depuis l'autre marche.";
			var create = widget.Get<ButtonWidget>("MD_TRADE_CREATE");
			create.GetText = () => Market()?.Trait<MedievalMarket>().Recruiting == true ? "Preparation..." : "Charrette marchande 20N";
			create.IsDisabled = () => world.Paused || Market() is not Actor market || !market.Trait<MedievalMarket>().CanRecruit(market, true);
			create.GetTooltipText = () => "Recruter une charrette pour cette route";
			create.GetTooltipDesc = () => "20 nourriture et une place d'habitation.\n60 unites de chacune des cinq ressources, or compris.\nCharge, voyage et decharge automatiquement.\nLe conducteur doit manger dans les zones de ravitaillement.";
			create.OnClick = () => { if (Market() is Actor market) world.IssueOrder(new Order("MedievalCreateTrader", market, false)); };
			var peer = widget.Get<ButtonWidget>("MD_TRADE_OTHER");
			peer.GetText = () => "Voir l'autre marche";
			peer.IsDisabled = () => Market() is not Actor market || !market.Trait<MedievalTradeMarket>().Linked(market);
			peer.OnClick = () =>
			{
				if (Market() is not Actor market || !market.Trait<MedievalTradeMarket>().Linked(market)) return;
				var other = market.Trait<MedievalTradeMarket>().Partner;
				world.Selection.Combine(world, [other], false, true); renderer.Viewport.Center(other.CenterPosition);
			};
			widget.Get<LabelWidget>("MD_TRADE_STATUS").GetText = () => Market() is Actor market
				? $"Charrettes actives : {world.ActorsWithTrait<MedievalTradeCart>().Count(p => p.Actor.Owner == owner && p.Trait.Active && p.Trait.Serves(market))} | Reserve locale : 40 nourriture" : "";
			widget.Get<LabelWidget>("MD_TRADE_HINT").GetText = () => "Transporteurs : depots a 16 cases du marche maximum.";
			widget.Get<LabelWidget>("MD_TRADE_EQUIPMENT").GetText = () => MedievalEquipment.StockText(Market());

			widget.Get("MD_TRADER_PANEL").IsVisible = () => Cart() != null && Market() == null && Selected<MedievalStockyard>() == null && Supply() == null;
			widget.Get<LabelWidget>("MD_TRADER_TITLE").GetText = () => "CHARRETTE MARCHANDE";
			var assign = widget.Get<ButtonWidget>("MD_TRADER_ASSIGN");
			assign.GetText = () => "Choisir une route";
			assign.IsDisabled = () => world.Paused || Cart() == null;
			assign.OnClick = () => Target(Cart(), "MedievalAssignTrader");
			var pause = widget.Get<ButtonWidget>("MD_TRADER_PAUSE");
			pause.GetText = () => Cart()?.Trait<MedievalTradeCart>().Active == true ? "Mettre en pause" : "Reprendre la route";
			pause.IsDisabled = () => world.Paused || Cart() is not Actor cart || !cart.Trait<MedievalTradeCart>().Active && !cart.Trait<MedievalTradeCart>().ValidRoute(cart);
			pause.OnClick = () => { if (Cart() is Actor cart) world.IssueOrder(new Order(cart.Trait<MedievalTradeCart>().Active ? "MedievalPauseTrade" : "MedievalResumeTrade", cart, false)); };
			var recover = widget.Get<ButtonWidget>("MD_TRADER_RECOVER");
			recover.GetText = () => "Rapatrier la cargaison";
			recover.IsDisabled = () => world.Paused || Cart() == null || !MedievalMarket.Markets(owner).Any();
			recover.GetTooltipText = () => "Decharger au marche le plus proche";
			recover.GetTooltipDesc = () => "La charrette se deplace jusqu'au marche puis y depose sa charge.\nElle attend si le marche est plein et reste sur place une fois vide.";
			recover.OnClick = () => { if (Cart() is Actor cart) world.IssueOrder(new Order("MedievalRecoverTrade", cart, false)); };
			widget.Get<LabelWidget>("MD_TRADER_STOCK").GetText = () => Cart() is Actor cart ? string.Join(" | ", new[] { 1, 0, 3, 2, 4 }.Select(r =>
				$"{(r == 1 ? "Nourr." : MedievalStockDisplay.Labels[r])} {cart.Trait<MedievalStockpile>().Stock.Amount(r)}")) : "";
			widget.Get<LabelWidget>("MD_TRADER_STATUS").GetText = () => Cart()?.Trait<MedievalTradeCart>().Status ?? "";
			widget.Get<LabelWidget>("MD_TRADER_ROUTE").GetText = () => Cart() is Actor cart ? cart.Trait<MedievalTradeCart>().RouteName(cart) : "";

			var convoy = widget.Get<ButtonWidget>("MD_CONVOY_ASSIGN");
			convoy.GetText = () => "Approvisionner chantier";
			convoy.IsDisabled = () => world.Paused || Supply() == null || !MedievalMarket.Markets(owner).Any();
			convoy.GetTooltipText = () => "Construire loin de la ville";
			convoy.GetTooltipDesc = () => "Posez un entrepot ou un autre chantier, puis cliquez-le.\nLa charrette fait autant d'allers-retours que necessaire.\nLes transporteurs chargent au marche ; les batisseurs prennent les materiaux au chantier.\nElle emporte aussi de la nourriture pour l'expedition.";
			convoy.OnClick = () => Target(Supply(), "MedievalSupplySite");
			var stop = widget.Get<ButtonWidget>("MD_CONVOY_STOP");
			stop.GetText = () => "Arreter le convoi";
			stop.IsDisabled = () => world.Paused || Supply()?.Trait<MedievalConstructionConvoy>().Active != true;
			stop.OnClick = () => { if (Supply() is Actor cart) world.IssueOrder(new Order("MedievalStopConvoy", cart, false)); };
			widget.Get<LabelWidget>("MD_CONVOY_STATUS").GetText = () => Supply()?.Trait<MedievalConstructionConvoy>().Status ?? "";
		}
	}
}
