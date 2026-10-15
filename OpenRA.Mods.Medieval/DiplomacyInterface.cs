using System;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalDiplomacyLogic : ChromeLogic
	{
		readonly World world;
		readonly MedievalDiplomacy diplomacy;
		readonly Widget root, panel;
		bool opened;
		Player selected;
		Player Local => world.LocalPlayer;
		DiplomaticRequest Current => selected == null || Local == null ? null : diplomacy.Requests.LastOrDefault(r => r.Between(Local, selected) && diplomacy.Visible(r, Local));
		Actor Courier => world.Selection.Actors.FirstOrDefault(a => a.Owner == Local && a.IsInWorld && !a.IsDead
			&& a.TraitOrDefault<MedievalMessenger>()?.CanCompose(a, Local) == true) ?? diplomacy.Courier(Local);
		bool CanSend(bool alliance) => Local != null && selected != null && !world.Paused && diplomacy.CanRequest(Local, selected, alliance)
			&& Courier != null && diplomacy.Destination(Local, selected) != null;
		void Show()
		{
			foreach (var prefix in new[] { "MD_FOOD", "MD_PRIDE", "MD_PLACES", "MD_CAMP", "MD_HERALDRY", "MD_BARTER", "MD_DECOR", "MD_FORT" })
				if (root.Get(prefix == "MD_FOOD" ? "MD_FOOD_DETAILS" : prefix + "_PANEL").IsVisible()) root.Get<ButtonWidget>(prefix + "_CLOSE").OnClick();
			selected = diplomacy.Requests.FirstOrDefault(r => diplomacy.Visible(r, Local) && diplomacy.CanRespond(r, Local))?.Sender
				?? selected ?? diplomacy.Nations.FirstOrDefault(p => p != Local);
			opened = true;
		}
		string Hint()
		{
			if (selected == null) return "Aucune autre nation dans cette partie.";
			if (!diplomacy.Available(selected)) return "Ce joueur a quitté la partie ou ne peut plus négocier. Les nouvelles demandes diplomatiques sont désactivées.";
			if (diplomacy.Pending(Local, selected) is { } pending && diplomacy.Visible(pending, Local))
				return pending.Sender == Local ? "Votre messager porte la demande. L’accord de l’autre joueur est nécessaire." : "Une demande vous est adressée. Acceptez-la ou refusez-la ci-dessous.";
			if (Local.RelationshipWith(selected) == PlayerRelationship.Ally) return "Vision partagée. Livrez les dépôts alliés depuis vos charrettes ; vos charrettes de ravitaillement nourrissent les unités alliées proches.";
			if (Courier == null) return "Recrutez un messager à la caserne et placez-le près d’une de vos casernes. Un messager en mission doit d’abord revenir.";
			if (diplomacy.Destination(Local, selected) == null) return "Découvrez une caserne de cette nation pour y envoyer votre messager. Sa position doit être visible.";
			return "Le messager disponible rejoint une caserne visible du destinataire. Le destinataire choisit d’accepter ou de refuser à son arrivée.";
		}
		[ObjectCreator.UseCtor]
		public MedievalDiplomacyLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.world = world; root = widget; diplomacy = world.WorldActor.Trait<MedievalDiplomacy>();
			ButtonWidget Button(Widget parent, string id, string text, int x, int y, int width, Action action)
			{
				var b = new ButtonWidget(Game.ModData) { Id = id, Bounds = new WidgetBounds(x, y, width, 28), Background = "md-button", Font = "TinyBold", GetText = () => text, OnClick = action };
				parent.AddChild(b); return b;
			}
			var toggle = Button(widget, "MD_DIPLOMACY_OPEN", "Diplomatie", 590, 81, 152, () => { if (opened) opened = false; else Show(); });
			toggle.IsVisible = () => Local != null;
			toggle.IsHighlighted = () => opened || diplomacy.Requests.Any(r => diplomacy.Visible(r, Local) && diplomacy.CanRespond(r, Local));
			toggle.GetText = () => "Diplomatie" + (diplomacy.Requests.Count(r => diplomacy.Visible(r, Local) && diplomacy.CanRespond(r, Local)) is var n && n > 0 ? " (" + n + ")" : "");
			panel = new BackgroundWidget { Id = "MD_DIPLOMACY_PANEL", Bounds = new WidgetBounds((Game.Renderer.Resolution.Width - 600) / 2, 114, 600, 510), Background = "md-panel", IsVisible = () => Local != null && opened };
			widget.AddChild(panel);
			MedievalTransferWidget.Label(panel, "MD_DIPLOMACY_TITLE", 14, 10, 490, 24, () => "DIPLOMATIE — RELATIONS ENTRE NATIONS");
			Button(panel, "MD_DIPLOMACY_CLOSE", "Fermer", 510, 8, 76, () => { opened = false; panel.Hidden(); });
			MedievalTransferWidget.Label(panel, "MD_DIPLOMACY_LEGEND", 14, 42, 572, 36,
				() => "Les nations commencent neutres. Attaquer une nation déclare la guerre.\nLa paix et l’alliance demandent un messager et l’accord des deux joueurs.");
			var list = new ScrollPanelWidget(Game.ModData) { Id = "MD_DIPLOMACY_LIST", Bounds = new WidgetBounds(14, 86, 572, 140), Background = "md-panel" };
			panel.AddChild(list);
			var index = 0;
			foreach (var nation in diplomacy.Nations.Where(p => p != Local))
			{
				var row = Button(list, "MD_DIPLOMACY_PLAYER_" + nation.InternalName, "", 6, 6 + index++ * 32, 536, () => selected = nation);
				row.GetText = () => nation.ResolvedPlayerName + "  —  " + MedievalDiplomacy.RelationName(Local.RelationshipWith(nation))
					+ (diplomacy.HasLeft(nation) ? "  ·  déconnecté" : "")
					+ (diplomacy.Pending(Local, nation) is { } request && diplomacy.Visible(request, Local) ? "  ·  courrier" : "");
				row.IsHighlighted = () => selected == nation;
				row.Align = TextAlign.Left; row.LeftMargin = 14;
			}
			MedievalTransferWidget.Label(panel, "MD_DIPLOMACY_SELECTED", 14, 238, 572, 24,
				() => selected == null ? "Sélectionnez une nation" : selected.ResolvedPlayerName + " : " + MedievalDiplomacy.RelationName(Local.RelationshipWith(selected)));
			MedievalTransferWidget.Label(panel, "MD_DIPLOMACY_HINT", 14, 268, 572, 54, () => WidgetUtils.WrapText(Hint(), 572, Game.Renderer.Fonts["TinyBold"]));
			foreach (var alliance in new[] { false, true })
			{
				var b = Button(panel, alliance ? "MD_DIPLOMACY_ALLIANCE" : "MD_DIPLOMACY_PEACE", alliance ? "Envoyer une demande d’alliance" : "Envoyer une demande de paix",
					alliance ? 304 : 14, 326, 282, () => { if (CanSend(alliance)) world.IssueOrder(MedievalDiplomacy.Request(Local, Courier, diplomacy.Destination(Local, selected), alliance)); });
				b.IsDisabled = () => !CanSend(alliance);
			}
			MedievalTransferWidget.Label(panel, "MD_DIPLOMACY_MAIL", 14, 366, 572, 52, () => Current is { } r
				? WidgetUtils.WrapText((r.Alliance ? "ALLIANCE" : "PAIX") + " — " + r.Status, 572, Game.Renderer.Fonts["TinyBold"]) : "Aucun courrier diplomatique avec cette nation.");
			foreach (var accept in new[] { true, false })
			{
				var b = Button(panel, accept ? "MD_DIPLOMACY_ACCEPT" : "MD_DIPLOMACY_REFUSE", accept ? "Accepter la demande" : "Refuser la demande", accept ? 14 : 304, 426, 282,
					() => { if (!world.Paused && diplomacy.CanRespond(Current, Local)) world.IssueOrder(MedievalDiplomacy.Respond(Local, Current, accept)); });
				b.IsDisabled = () => world.Paused || !diplomacy.CanRespond(Current, Local);
			}
			MedievalTransferWidget.Label(panel, "MD_DIPLOMACY_FOOTER", 14, 467, 572, 32,
				() => "Allié : vision partagée et dons aux dépôts. Aucun prélèvement dans ses stocks.\nLes charrettes de ravitaillement nourrissent aussi ses citoyens et ses soldats.");
		}
	}
}
