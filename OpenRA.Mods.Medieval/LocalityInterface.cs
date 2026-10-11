using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalLocalityLogic : ChromeLogic
	{
		readonly World world;
		readonly MedievalLocalities registry;
		readonly TextFieldWidget input;
		readonly List<(Actor Target, string Title, string Detail)> entries = [];
		Actor edited;
		string previousName = "";
		bool open, routes;
		int page, refresh;
		const int PageSize = 6;
		Actor Selected() => world.Selection.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalLocality>() != null);
		void Entries()
		{
			entries.Clear(); var player = world.LocalPlayer;
			if (player == null) return;
			if (!routes)
			{
				foreach (var id in registry.Ids(player))
				{
					var anchor = registry.Anchor(player, id);
					entries.Add((anchor, registry.Kind(player, id) + " : " + registry.Name(id), registry.Summary(player, id)));
				}
			}
			else
			{
				foreach (var a in MedievalMarket.Markets(player))
				{
					var trade = a.Trait<MedievalTradeMarket>();
					if (!trade.Linked(a) || a.ActorID > trade.Partner.ActorID) continue;
					var carts = world.ActorsWithTrait<MedievalTradeCart>().Count(t => t.Actor.Owner == player && t.Actor.IsInWorld && !t.Actor.IsDead && t.Trait.Active && t.Trait.Serves(a));
					entries.Add((a, MedievalLogistics.Name(a) + " <-> " + MedievalLogistics.Name(trade.Partner), $"Commerce | {carts} charrette(s) active(s) | Voir pour regler"));
				}
				foreach (var a in MedievalMarketStall.Stalls(player))
				{
					var stall = a.Trait<MedievalMarketStall>();
					entries.Add((a, (stall.Linked(a) ? MedievalLogistics.Name(stall.Market) : "Grand marche manquant") + " -> " + MedievalLogistics.Name(a),
						stall.Linked(a) ? $"Relais | Besoins : {stall.Desired(a, 1)} nourriture / {stall.Desired(a, 0)} bois / {stall.Desired(a, 3)} pierre" : "Relais en attente d'un grand marche termine"));
				}
			}
			page = Math.Min(page, Math.Max(0, (entries.Count - 1) / PageSize));
		}
		public override void Tick()
		{
			if (world.LocalPlayer == null) return;
			var actor = Selected(); var name = actor == null || MedievalLocalities.Id(actor) == 0 ? "" : registry.Name(MedievalLocalities.Id(actor));
			if (actor != edited || name != previousName)
			{
				input.YieldKeyboardFocus(); input.Text = name; edited = actor; previousName = name;
			}
			if (world.Selection.Actors.Any()) open = false;
			if (--refresh <= 0) { refresh = 15; if (open) Entries(); }
		}
		[ObjectCreator.UseCtor]
		public MedievalLocalityLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.world = world;
			input = widget.Get<TextFieldWidget>("MD_LOCALITY_INPUT"); input.MaxLength = LocalityNames.MaxLength;
			if (world.LocalPlayer == null)
			{
				widget.Get("MD_LOCALITY_PANEL").IsVisible = widget.Get("MD_PLACES_PANEL").IsVisible = widget.Get("MD_PLACES_OPEN").IsVisible = () => false;
				return;
			}
			registry = MedievalLocalities.For(world.LocalPlayer);
			widget.Get("MD_LOCALITY_PANEL").IsVisible = () => Selected() != null;
			widget.Get<LabelWidget>("MD_LOCALITY_TITLE").GetText = () => Selected() is Actor a && MedievalLocalities.Id(a) != 0
				? registry.Kind(a.Owner, MedievalLocalities.Id(a)).ToUpperInvariant() + " : " + registry.Name(MedievalLocalities.Id(a)) : "FONDATION A LA FIN DU CHANTIER";
			input.IsDisabled = () => world.Paused || Selected() is not Actor a || MedievalLocalities.Id(a) == 0;
			var rename = widget.Get<ButtonWidget>("MD_LOCALITY_RENAME"); rename.GetText = () => "Renommer";
			rename.IsDisabled = () => input.IsDisabled() || Selected() is not Actor a || registry.RenameError(a, input.Text).Length != 0;
			rename.OnClick = () =>
			{
				if (!rename.IsDisabled() && Selected() is Actor a) { world.IssueOrder(new Order("MedievalRenameLocality", a, false) { TargetString = input.Text }); input.YieldKeyboardFocus(); }
			};
			input.OnEnterKey = _ => { rename.OnClick(); return true; };
			widget.Get<LabelWidget>("MD_LOCALITY_STATS").GetText = () => Selected() is Actor a && MedievalLocalities.Id(a) > 0 ? registry.Summary(a.Owner, MedievalLocalities.Id(a)) : "Petit marche isole : un nouveau village prendra forme.";
			widget.Get<LabelWidget>("MD_LOCALITY_HINT").GetText = () => Selected() is not Actor a ? ""
				: registry.RenameError(a, input.Text) is string error && error.Length > 0 ? error : "Nom partage par les marches de cette localite. 32 caracteres.";
			var toggle = widget.Get<ButtonWidget>("MD_PLACES_OPEN"); toggle.GetText = () => "Villes & routes";
			toggle.OnClick = () => { open = !open || world.Selection.Actors.Any(); world.Selection.Clear(); world.CancelInputMode(); Entries(); };
			widget.Get("MD_PLACES_PANEL").IsVisible = () => open && !world.Selection.Actors.Any();
			widget.Get<LabelWidget>("MD_PLACES_TITLE").GetText = () => routes ? "ROUTES ENTRE VILLES ET VILLAGES" : "VILLES ET VILLAGES";
			var close = widget.Get<ButtonWidget>("MD_PLACES_CLOSE"); close.GetText = () => "Fermer"; close.OnClick = () => open = false;
			var towns = widget.Get<ButtonWidget>("MD_PLACES_TOWNS"); towns.GetText = () => (routes ? "" : "[X] ") + "Localites";
			towns.OnClick = () => { routes = false; page = 0; Entries(); };
			var links = widget.Get<ButtonWidget>("MD_PLACES_ROUTES"); links.GetText = () => (routes ? "[X] " : "") + "Routes & relais";
			links.OnClick = () => { routes = true; page = 0; Entries(); };
			for (var i = 0; i < PageSize; i++)
			{
				var row = i;
				widget.Get<LabelWidget>($"MD_PLACES_NAME_{i}").GetText = () => page * PageSize + row < entries.Count ? entries[page * PageSize + row].Title : "";
				widget.Get<LabelWidget>($"MD_PLACES_DETAIL_{i}").GetText = () => page * PageSize + row < entries.Count ? entries[page * PageSize + row].Detail : "";
				var view = widget.Get<ButtonWidget>($"MD_PLACES_VIEW_{i}"); view.GetText = () => "Voir";
				view.IsVisible = () => page * PageSize + row < entries.Count;
				view.OnClick = () =>
				{
					if (page * PageSize + row >= entries.Count) return;
					var actor = entries[page * PageSize + row].Target;
					if (!MedievalStockpile.Active(actor) || actor.Owner != world.LocalPlayer) { Entries(); return; }
					world.Selection.Combine(world, [actor], false, true); worldRenderer.Viewport.Center(actor.CenterPosition); open = false;
				};
			}
			var previous = widget.Get<ButtonWidget>("MD_PLACES_PREVIOUS"); previous.GetText = () => "Precedent";
			previous.IsDisabled = () => page == 0; previous.OnClick = () => { page = Math.Max(0, page - 1); Entries(); };
			var next = widget.Get<ButtonWidget>("MD_PLACES_NEXT"); next.GetText = () => "Suivant";
			next.IsDisabled = () => (page + 1) * PageSize >= entries.Count; next.OnClick = () => { if (!next.IsDisabled()) page++; Entries(); };
			widget.Get<LabelWidget>("MD_PLACES_COUNT").GetText = () => $"{entries.Count} {(routes ? "liaison(s)" : "localite(s)")} | Page {page + 1}/{Math.Max(1, (entries.Count + PageSize - 1) / PageSize)}";
			widget.Get<LabelWidget>("MD_PLACES_HINT").GetText = () => routes ? "Voir un relais pour changer son grand marche fournisseur."
				: "Voir une localite pour la renommer depuis son marche.";
		}
	}
}
