using System;
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalBarterLogic : ChromeLogic
	{
		readonly World world;
		readonly MedievalBarter barter;
		readonly Widget panel;
		readonly TextFieldWidget[] give = new TextFieldWidget[StorageGoods.Count], receive = new TextFieldWidget[StorageGoods.Count];
		readonly System.Collections.Generic.Dictionary<uint, uint> seen = [];
		BarterDeal current;
		uint shownRevision;
		bool opened;
		// Keep food together, followed by supplies, military equipment and materials.
		static readonly int[] Rows = [1, 2, 8, 10, 9, 7, 5, 6, 4, 3, 0];
		static readonly string[] Names = ["Bois", "Nourriture (céréales)", "Viande", "Pierre", "Or", "Armes : arcs", "Armes : épées", "Armures", "Fournitures : laine", "Fournitures : tentes", "Fournitures : tissu"];
		Player Local => world.LocalPlayer;
		bool Editing => current != null && current.State is BarterState.Negotiating or BarterState.Offered;
		bool Draft(out int[] offered, out int[] requested)
		{
			offered = new int[StorageGoods.Count]; requested = new int[StorageGoods.Count];
			for (var g = 0; g < StorageGoods.Count; g++)
				if (!int.TryParse(give[g].Text, NumberStyles.None, CultureInfo.InvariantCulture, out offered[g])
					|| !int.TryParse(receive[g].Text, NumberStyles.None, CultureInfo.InvariantCulture, out requested[g])) return false;
			return BarterOffer.Valid(offered) && BarterOffer.Valid(requested);
		}
		bool MatchesOffer() => current != null && Draft(out var a, out var b)
			&& a.SequenceEqual(current.Goods[current.Side(Local)]) && b.SequenceEqual(current.Goods[1 - current.Side(Local)]);
		void Show(BarterDeal deal)
		{
			current = deal; shownRevision = deal.Revision; opened = true;
			var side = deal.Side(Local);
			for (var g = 0; g < StorageGoods.Count; g++)
			{
				give[g].YieldKeyboardFocus(); receive[g].YieldKeyboardFocus();
				give[g].Text = deal.Goods[side][g].ToString(CultureInfo.InvariantCulture);
				receive[g].Text = deal.Goods[1 - side][g].ToString(CultureInfo.InvariantCulture);
			}
		}
		void Send(string command, string payload = "", Actor depot = null)
		{ if (current != null && !world.Paused) world.IssueOrder(MedievalBarter.Command(Local, current, command, payload, depot)); }
		public override void Tick()
		{
			if (Local == null) return;
			foreach (var deal in barter.Deals.Where(d => barter.Visible(d, Local)))
			{
				if (seen.TryGetValue(deal.Id, out var revision) && revision == deal.Revision) continue;
				seen[deal.Id] = deal.Revision;
				if (deal.State is BarterState.Negotiating or BarterState.Offered || current == deal && opened) Show(deal);
			}
			if (current != null && (!barter.Deals.Contains(current) || !barter.Visible(current, Local))) { opened = false; current = null; }
			if (opened && current != null && shownRevision != current.Revision) Show(current);
		}
		[ObjectCreator.UseCtor]
		public MedievalBarterLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.world = world; barter = world.WorldActor.Trait<MedievalBarter>(); panel = widget.Get("MD_BARTER_PANEL");
			panel.IsVisible = () => Local != null && opened && current != null;
			var template = panel.Get("MD_BARTER_ROW"); panel.RemoveChild(template);
			for (var row = 0; row < Rows.Length; row++)
			{
				var good = Rows[row]; var item = template.Clone(); item.Id = "MD_BARTER_ROW_" + good; item.Bounds.Y += row * 26; panel.AddChild(item);
				item.Get<LabelWidget>("GOOD").GetText = () => Names[good];
				item.Get<LabelWidget>("AVAILABLE").GetText = () => current?.Depots[current.Side(Local)] is { } depot && MedievalBarter.Depot(depot, Local)
					? (StorageGoods.Stock(depot, good)?.Available(StorageGoods.Index(good)) ?? 0).ToString(CultureInfo.InvariantCulture) : "—";
				give[good] = item.Get<TextFieldWidget>("GIVE"); receive[good] = item.Get<TextFieldWidget>("RECEIVE");
				give[good].Id = "MD_BARTER_GIVE_" + good; receive[good].Id = "MD_BARTER_RECEIVE_" + good;
				foreach (var field in new[] { give[good], receive[good] })
				{ field.Type = TextFieldType.Integer; field.MaxLength = 4; field.Text = "0"; field.IsDisabled = () => world.Paused || !Editing; }
			}
			var invite = widget.Get<ButtonWidget>("MD_MAIL_BARTER");
			Actor Messenger() => world.Selection.Actors.Count() == 1 ? world.Selection.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.TraitOrDefault<MedievalMessenger>() != null) : null;
			BarterDeal SelectedDeal() => barter.ForMessenger(Messenger()) is { } d && barter.Visible(d, Local) ? d : null;
			invite.GetText = () => SelectedDeal() != null ? "Voir le marchandage" : "Inviter au marchandage";
			invite.IsDisabled = () => Local == null || world.Paused || SelectedDeal() == null && !barter.CanInvite(Messenger(), Local);
			invite.OnClick = () =>
			{
				if (invite.IsDisabled()) return;
				if (SelectedDeal() is { } d) Show(d);
				else world.OrderGenerator = new MedievalMessengerTargetGenerator(world, worldRenderer, Messenger(), "", "MedievalInviteBarter");
			};
			invite.GetTooltipText = () => "Ouvrir un commerce avec un autre joueur";
			invite.GetTooltipDesc = () => "Depuis votre caserne, envoyez le messager à une caserne visible.\nChaque joueur doit posséder un dépôt terminé.\nLe destinataire accepte l'invitation avant de négocier les quantités.";
			BarterDeal[] Visible() => Local == null ? [] : barter.Deals.Where(d => barter.Visible(d, Local)).OrderByDescending(d => d.Id).ToArray();
			var alert = widget.Get<ButtonWidget>("MD_BARTER_ALERT"); alert.IsVisible = () => Visible().Length > 0;
			alert.GetText = () => Visible().Any(d => d.State == BarterState.Invited && d.Second == Local) ? "Invitation commerciale reçue : voir"
				: "Marchandage : " + Visible().Count(d => d.Active) + " échange(s) en cours";
			alert.OnClick = () => { var d = Visible().FirstOrDefault(d => d.Active) ?? Visible().FirstOrDefault(); if (d != null) Show(d); };
			panel.Get<LabelWidget>("MD_BARTER_TITLE").GetText = () => current == null ? "" : "MARCHANDAGE AVEC " + current.Player(1 - current.Side(Local)).ResolvedPlayerName;
			panel.Get<LabelWidget>("MD_BARTER_STATUS").GetText = () => current?.Status ?? "";
			panel.Get<LabelWidget>("MD_BARTER_GOOD_HEADER").GetText = () => "Marchandise";
			panel.Get<LabelWidget>("MD_BARTER_STOCK_HEADER").GetText = () => "Mon stock libre";
			panel.Get<LabelWidget>("MD_BARTER_GIVE_HEADER").GetText = () => "Je donne";
			panel.Get<LabelWidget>("MD_BARTER_RECEIVE_HEADER").GetText = () => "Je reçois";
			panel.Get<LabelWidget>("MD_BARTER_HINT").GetText = () => current?.State == BarterState.Invited
				? Local == current.Second ? "Votre partenaire propose de négocier. Ouvrez le marchandage ou refusez." : "Le destinataire doit recevoir le messager et accepter l'invitation."
				: current?.State is BarterState.Delivering or BarterState.Completed or BarterState.Interrupted
				? string.Join(" | ", current.Legs.Select((l, i) => $"{(i == current.Side(Local) ? "Envoyé" : "Reçu")} : {l?.Cargo.Delivered ?? 0}/{current.Goods[i].Sum()}"))
				: "Une contre-offre remplace la précédente. Les deux stocks sont réservés à l'accord ; livraison par charrettes de 60 objets.";
			var depotButton = panel.Get<ButtonWidget>("MD_BARTER_DEPOT");
			depotButton.GetText = () => current == null ? "" : "Mon " + MedievalBarter.DepotName(current.Depots[current.Side(Local)]) + "  ›";
			depotButton.IsDisabled = () => world.Paused || !Editing || MedievalBarter.Depots(Local).Count() < 2;
			depotButton.OnClick = () =>
			{
				if (depotButton.IsDisabled()) return; var depots = MedievalBarter.Depots(Local).ToArray();
				Send("MedievalBarterDepot", depot: depots[(Array.IndexOf(depots, current.Depots[current.Side(Local)]) + 1) % depots.Length]);
			};
			panel.Get<LabelWidget>("MD_BARTER_OTHER_DEPOT").GetText = () => current == null ? "" : "Partenaire : " + MedievalBarter.DepotName(current.Depots[1 - current.Side(Local)]);
			var propose = panel.Get<ButtonWidget>("MD_BARTER_PROPOSE"); propose.GetText = () => current?.State == BarterState.Offered ? "Proposer une contre-offre" : "Proposer cet échange";
			propose.IsDisabled = () => world.Paused || !Editing || !Draft(out _, out _);
			propose.OnClick = () => { if (!propose.IsDisabled() && Draft(out var a, out var b)) Send("MedievalBarterPropose", BarterOffer.Encode(a, b)); };
			var accept = panel.Get<ButtonWidget>("MD_BARTER_ACCEPT");
			accept.GetText = () => current?.State == BarterState.Invited ? "Ouvrir le marchandage" : "Accepter et livrer";
			accept.IsDisabled = () => world.Paused || current == null || !(current.State == BarterState.Invited && Local == current.Second
				|| current.State == BarterState.Offered && current.Proposer != Local && MatchesOffer());
			accept.OnClick = () => { if (!accept.IsDisabled()) Send(current.State == BarterState.Invited ? "MedievalBarterOpen" : "MedievalBarterAccept"); };
			var refuse = panel.Get<ButtonWidget>("MD_BARTER_REFUSE"); refuse.GetText = () => "Refuser / annuler";
			refuse.IsDisabled = () => world.Paused || current?.Negotiable != true;
			refuse.OnClick = () => { if (!refuse.IsDisabled()) Send("MedievalBarterRefuse"); };
			var close = panel.Get<ButtonWidget>("MD_BARTER_CLOSE"); close.GetText = () => "Fermer";
			close.OnClick = () => { foreach (var f in give.Concat(receive)) f.YieldKeyboardFocus(); opened = false; };
			var next = panel.Get<ButtonWidget>("MD_BARTER_NEXT"); next.GetText = () => "Autre échange ›";
			next.IsDisabled = () => Visible().Length < 2;
			next.OnClick = () => { var all = Visible(); if (all.Length > 0) Show(all[(Array.IndexOf(all, current) + 1) % all.Length]); };
		}
	}
}
