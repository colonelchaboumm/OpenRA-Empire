using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalTransferWidget : Widget
	{
		readonly World world;
		readonly Widget chooser;
		readonly TextFieldWidget quantity;
		readonly Dictionary<string, Sprite> portraits = [];
		readonly Sprite[] icons = new Sprite[StorageGoods.Count];
		Actor[] candidates = [];
		int candidatesTick = -1;
		Actor selected, store, cart, frozenStore, frozenCart;
		int selectionHash = -1, dragged = -1, proposed = -1;
		bool loading;
		bool showNotice;
		int2 pointer;
		string notice = "Glissez une ressource d’un stock vers l’autre.";
		static bool Live(Actor a) => a != null && a.IsInWorld && !a.IsDead && !a.WillDispose;
		public Actor Store => MedievalManualTransfer.Store(store, world.LocalPlayer) ? store : null;
		public Actor Cart => Live(cart) && cart.Owner == world.LocalPlayer ? cart : null;
		public bool Choosing => proposed >= 0;
		public bool Available => Selected() != null;
		Actor Selected() => world.Selection.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead && !a.WillDispose
			&& (MedievalManualTransfer.Store(a, world.LocalPlayer) || a.TraitOrDefault<MedievalManualTransfer>() != null));
		int Limit => Choosing && frozenCart?.Owner == world.LocalPlayer ? MedievalManualTransfer.Maximum(frozenCart, frozenStore, proposed, loading) : 0;
		int Number => int.TryParse(quantity.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
		public MedievalTransferWidget(World world, WorldRenderer renderer)
		{
			this.world = world; Id = "MD_TRANSFER_PANEL"; Bounds = new WidgetBounds(0, 0, 410, 398);
			chooser = new BackgroundWidget { Id = "MD_TRANSFER_CHOICE", Background = "md-panel", Bounds = new WidgetBounds(0, 0, 410, 398), IsVisible = () => Choosing };
			AddChild(chooser);
			MedievalMenuIcons.Add(chooser, renderer, "MD_TRANSFER_RESOURCE_ICON", 12, 14, 24, () => Choosing ? Icon(proposed) : null, () => "chrome");
			Label(chooser, "MD_TRANSFER_TITLE", 44, 14, 354, 25, () => "QUANTITÉ À TRANSFÉRER");
			Label(chooser, "MD_TRANSFER_DIRECTION", 12, 48, 386, 48, () => Choosing
				? StorageGoods.Names[proposed] + " — " + (loading ? "bâtiment → charrette" : "charrette → bâtiment") + "\n" + Name(frozenStore) + " / charrette #" + frozenCart.ActorID : "");
			Label(chooser, "MD_TRANSFER_LIMIT", 12, 100, 386, 36, () => Choosing && Limit == 0
				? WidgetUtils.WrapText(Blocked(frozenCart, frozenStore, proposed, loading), 386, Game.Renderer.Fonts["TinyBold"])
				: $"Disponible pour ce transfert : {Limit}\nLe chargement aura lieu à l’arrivée de la charrette.");
			quantity = new TextFieldWidget { Id = "MD_TRANSFER_QUANTITY", Bounds = new WidgetBounds(117, 154, 120, 30), Type = TextFieldType.Integer, MaxLength = 8 };
			quantity.IsValid = () => Number > 0 && Number <= Limit;
			quantity.OnEnterKey = _ => { Confirm(); return true; };
			quantity.OnEscKey = _ => { CloseChoice(); return true; };
			chooser.AddChild(quantity);
			Button(chooser, "MD_TRANSFER_MINUS", "−", 68, 154, 42, () => quantity.Text = Math.Max(1, Number - 1).ToString(CultureInfo.InvariantCulture));
			Button(chooser, "MD_TRANSFER_PLUS", "+", 244, 154, 42, () => quantity.Text = Math.Min(Limit, Number + 1).ToString(CultureInfo.InvariantCulture));
			Button(chooser, "MD_TRANSFER_MAX", "Max", 294, 154, 65, () => quantity.Text = Limit.ToString(CultureInfo.InvariantCulture));
			var confirm = Button(chooser, "MD_TRANSFER_CONFIRM", "Confirmer le transfert", 12, 210, 238, Confirm);
			confirm.IsDisabled = () => world.Paused || !quantity.IsValid();
			Button(chooser, "MD_TRANSFER_CANCEL", "Annuler", 258, 210, 140, CloseChoice);
			Label(chooser, "MD_TRANSFER_NOTE", 12, 258, 386, 70,
				() => "Les ressources et la place seront réservées\naprès confirmation. Un ordre de déplacement\nou d’arrêt annule le transfert en cours.");
			var change = Button(this, "MD_TRANSFER_PARTNER", "Suivante", 310, 174, 90, NextPartner);
			change.IsVisible = () => !Choosing && dragged < 0 && Cart?.TraitOrDefault<MedievalManualTransfer>()?.Busy != true && Candidates().Length > 1;
			change.GetText = () => Live(selected) && selected.TraitOrDefault<MedievalManualTransfer>() != null ? "Autre dépôt" : "Autre charr.";
			var stop = Button(this, "MD_TRANSFER_STOP", "Annuler le transfert", 9, 369, 184, () =>
			{ if (Cart is { } ownCart) world.IssueOrder(new Order("MedievalCancelTransfer", ownCart, false)); });
			stop.Bounds.Height = 24;
			stop.IsVisible = () => !Choosing && Cart?.TraitOrDefault<MedievalManualTransfer>()?.Busy == true;
			stop.IsDisabled = () => world.Paused;
			var portrait = new SpriteWidget(renderer) { Id = "MD_TRANSFER_CART_VIEW", Bounds = new WidgetBounds(241, 162, 60, 44), IsVisible = () => !Choosing && Cart != null };
			portrait.GetSprite = CartPortrait;
			portrait.GetPalette = () => cart == null ? null : "md-livery" + cart.Owner.InternalName;
			portrait.GetScale = () => CartPortrait() is { } s ? Math.Min(58f / s.Bounds.Width, 42f / s.Bounds.Height) : 1;
			AddChild(portrait);
		}
		public Sprite Icon(int good) => icons[good] ??= MedievalMenuIcons.Good(world, good);
		public string StockDetail(bool upper, int good)
		{
			var actor = upper ? Store : Cart;
			var stock = actor == null ? null : StorageGoods.Stock(actor, good); var r = StorageGoods.Index(good);
			if ((stock?.Capacity(r) ?? 0) == 0) return StorageGoods.Names[good] + " : ce stock ne peut pas contenir cette ressource.";
			return $"{StorageGoods.Names[good]} : {stock.Amount(r)} en stock · {stock.Available(r)} disponibles · {stock.Reserved(r)} réservés\n"
				+ $"{stock.Incoming(r)} attendus · {stock.Free(r)} places libres · capacité {stock.Capacity(r)}";
		}
		static string Blocked(Actor carrier, Actor building, int good, bool load)
		{
			if (MedievalManualTransfer.UnavailableReason(carrier) is { } reason) return reason;
			if (!MedievalManualTransfer.Store(building, carrier.Owner)) return "Aucun bâtiment de stockage disponible.";
			var from = StorageGoods.Stock(load ? building : carrier, good); var to = StorageGoods.Stock(load ? carrier : building, good); var r = StorageGoods.Index(good);
			if ((to?.Capacity(r) ?? 0) == 0) return "Destination incompatible avec " + StorageGoods.Names[good] + ". Choisissez un autre stock.";
			if ((from?.Available(r) ?? 0) == 0) return (from?.Reserved(r) ?? 0) > 0 ? "Tout ce stock est déjà réservé pour un transport." : "Aucune quantité disponible de " + StorageGoods.Names[good] + ".";
			return "Destination pleine ou place déjà réservée pour une livraison.";
		}
		Sprite CartPortrait()
		{
			if (Cart == null) return null;
			var image = cart.Info.Name == "md.stonecart" ? "md.stonecart" : "md.supplycart";
			if (portraits.TryGetValue(image, out var portrait)) return portrait;
			var sprite = world.Map.Sequences.GetSequence(image, image == "md.stonecart" ? "idle" : "stand").GetSprite(0);
			var bounds = sprite.Bounds;
			if (sprite.Channel == TextureChannel.RGBA)
			{
				var buffered = sprite.Sheet.Buffered; var pixels = sprite.Sheet.GetData();
				var left = bounds.Right; var top = bounds.Bottom; var right = bounds.Left; var bottom = bounds.Top;
				for (var y = bounds.Top; y < bounds.Bottom; y++) for (var x = bounds.Left; x < bounds.Right; x++)
					if (pixels[(y * sprite.Sheet.Size.Width + x) * 4 + 3] >= 16)
					{ left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
				if (right > left && bottom > top) bounds = Rectangle.FromLTRB(left, top, right, bottom);
				if (!buffered) sprite.Sheet.ReleaseBuffer();
			}
			portrait = new Sprite(sprite.Sheet, bounds, 0, Vector3.Zero, sprite.Channel, sprite.BlendMode); portraits.Add(image, portrait); return portrait;
		}
		public static LabelWidget Label(Widget parent, string id, int x, int y, int width, int height, Func<string> text)
		{
			var label = new LabelWidget(Game.ModData) { Id = id, Bounds = new WidgetBounds(x, y, width, height), Font = "TinyBold", GetText = text };
			parent.AddChild(label); return label;
		}
		static ButtonWidget Button(Widget parent, string id, string text, int x, int y, int width, Action action)
		{
			var button = new ButtonWidget(Game.ModData) { Id = id, Bounds = new WidgetBounds(x, y, width, 30), Background = "md-button", Font = "TinyBold", GetText = () => text, OnClick = action };
			parent.AddChild(button); return button;
		}
		static string Name(Actor a) => a == null ? "Aucun dépôt disponible" : (a.Info.Name switch
		{
			"md.warehouse" => "Dépôt", "md.market" => "Grand marché", "md.marketstall" => "Petit marché", "md.farm" => "Ferme",
			"md.ranch" => "Élevage", "md.sheepfold" => "Bergerie", "md.forge" => "Forge", "md.sawmill" => "Scierie",
			"md.quarry" => "Carrière", "md.goldmine" => "Mine d’or", "md.tentweaver" => "Tisseur", "md.huntinglodge" => "Cabane de chasse",
			_ => "Stock du bâtiment"
		}) + " #" + a.ActorID;
		Actor[] Candidates()
		{
			if (!Live(selected)) return [];
			if (candidatesTick == world.WorldTick) return candidates;
			candidatesTick = world.WorldTick;
			return candidates = (selected.TraitOrDefault<MedievalManualTransfer>() != null
				? world.Actors.Where(a => MedievalManualTransfer.Store(a, world.LocalPlayer) && a.Info.Name is "md.warehouse" or "md.market" or "md.marketstall")
				: world.ActorsHavingTrait<MedievalManualTransfer>().Where(a => a.Owner == world.LocalPlayer && MedievalManualTransfer.Available(a)))
				.OrderBy(a => (a.CenterPosition - selected.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).ToArray();
		}
		void NextPartner()
		{
			var choices = Candidates(); if (choices.Length == 0) return;
			var isCart = selected.TraitOrDefault<MedievalManualTransfer>() != null;
			var next = choices[(Array.IndexOf(choices, isCart ? store : cart) + 1) % choices.Length];
			if (isCart) store = next; else cart = next;
		}
		public override void Tick()
		{
			var next = Selected();
			if (selectionHash != world.Selection.Hash || next != selected)
			{
				selectionHash = world.Selection.Hash; selected = next; CloseChoice(); dragged = -1; YieldMouseFocus(default);
				store = cart = null; showNotice = false; candidatesTick = -1;
			}
			if (selected == null) return;
			if (selected.TraitOrDefault<MedievalManualTransfer>() is { } manual)
			{
				cart = selected;
				if (manual.Busy) store = manual.Building;
				else if (!MedievalManualTransfer.Store(store, world.LocalPlayer)) store = Candidates().FirstOrDefault();
			}
			else
			{
				store = selected;
				if (!MedievalManualTransfer.Available(cart) && !(cart?.IsInWorld == true && !cart.IsDead && cart.Owner == world.LocalPlayer
					&& cart.TraitOrDefault<MedievalManualTransfer>() is { Busy: true } busy && busy.Building == store) && !Choosing && dragged < 0) cart = Candidates().FirstOrDefault();
			}
			if ((Choosing || dragged >= 0) && (!MedievalManualTransfer.Store(frozenStore, world.LocalPlayer) || !Live(frozenCart) || frozenCart.Owner != world.LocalPlayer))
			{ CloseChoice(); dragged = -1; YieldMouseFocus(default); }
		}
		void CloseChoice() { proposed = -1; quantity?.YieldKeyboardFocus(); chooser?.Hidden(); }
		void Confirm()
		{
			if (!Choosing || world.Paused || Number <= 0 || Number > Limit) return;
			world.IssueOrder(new Order(loading ? "MedievalLoadResource" : "MedievalUnloadResource", frozenCart, Target.FromActor(frozenStore), false)
				{ ExtraData = (uint)proposed, TargetString = Number.ToString(CultureInfo.InvariantCulture) });
			notice = "Ordre envoyé à la charrette."; showNotice = false; CloseChoice();
		}
		public Rectangle Cell(bool upper, int good) => new(RenderOrigin.X + 9 + good % 3 * 132, RenderOrigin.Y + (upper ? 42 : 210) + good / 3 * 32, 128, 30);
		Rectangle Area(bool upper) => new(RenderOrigin.X + 5, RenderOrigin.Y + (upper ? 6 : 174), 400, 164);
		public override bool HandleMouseInput(MouseInput mi)
		{
			pointer = mi.Location;
			if (mi.Button == MouseButton.Right && dragged >= 0) { dragged = -1; YieldMouseFocus(mi); return true; }
			if (world.Paused && dragged >= 0) { dragged = -1; YieldMouseFocus(mi); }
			if (Choosing || world.Paused) return mi.Event != MouseInputEvent.Scroll;
			if (mi.Event == MouseInputEvent.Down && mi.Button == MouseButton.Left)
				for (var upper = 0; upper < 2; upper++)
					for (var g = 0; g < StorageGoods.Count; g++)
						if (Cell(upper == 0, g).Contains(mi.Location))
						{
							if (MedievalManualTransfer.Maximum(Cart, Store, g, upper == 0) <= 0) { notice = Blocked(Cart, Store, g, upper == 0); showNotice = true; return true; }
							if (!TakeMouseFocus(mi)) return false;
							showNotice = false; dragged = g; loading = upper == 0; frozenStore = store; frozenCart = cart; return true;
						}
			if (mi.Event == MouseInputEvent.Up && mi.Button == MouseButton.Left && dragged >= 0)
			{
				if (Area(!loading).Contains(mi.Location) && frozenStore == store && frozenCart == cart)
				{
					proposed = dragged;
					if (Limit > 0) { quantity.Text = Limit.ToString(CultureInfo.InvariantCulture); quantity.TakeKeyboardFocus(); }
					else CloseChoice();
				}
				dragged = -1; YieldMouseFocus(mi); return true;
			}
			return dragged >= 0 || mi.Event != MouseInputEvent.Scroll;
		}
		public override void Hidden() { dragged = -1; CloseChoice(); base.Hidden(); }
		public override void Draw()
		{
			WidgetUtils.DrawPanel("md-panel", RenderBounds);
			if (Choosing) return;
			var font = Game.Renderer.Fonts["TinyBold"];
			void Text(string text, int x, int y, Color color) => font.DrawText(text, (RenderOrigin + new int2(x, y)).ToVector2(), color);
			Text(Name(Store), 9, 6, Color.FromArgb(232, 203, 143));
			Text("Quantité / capacité · survol : détails · glisser : transfert", 9, 23, Color.LightGray);
			Text(cart == null ? "Aucune charrette disponible" : "Charrette #" + cart.ActorID + (selected == cart ? " sélectionnée" : " à proximité"), 9, 177, Color.FromArgb(232, 203, 143));
			Text("Glisser vers le stock du haut ↑", 9, 194, Color.LightGray);
			string detail = null;
			for (var side = 0; side < 2; side++)
				for (var g = 0; g < StorageGoods.Count; g++)
				{
					var a = side == 0 ? Store : Cart;
					var inventory = a == null ? null : StorageGoods.Stock(a, g); var index = StorageGoods.Index(g);
					var cell = Cell(side == 0, g); var enabled = (inventory?.Capacity(index) ?? 0) > 0;
					WidgetUtils.DrawPanel("md-button", cell);
					var icon = Icon(g); var scale = Math.Min(20f / icon.Bounds.Width, 20f / icon.Bounds.Height);
					Game.Renderer.SpriteRenderer.DrawSprite(icon, null, (cell.TopLeft + new int2(3, 4)).ToVector3(), scale);
					font.DrawText(StorageGoods.Names[g], (cell.TopLeft + new int2(26, 2)).ToVector2(), enabled ? Color.FromArgb(232, 203, 143) : Color.Gray);
					font.DrawText(enabled ? $"{inventory.Amount(index)} / {inventory.Capacity(index)}" : "—", (cell.TopLeft + new int2(26, 15)).ToVector2(), enabled ? Color.White : Color.Gray);
					if (enabled)
					{
						var cr = Game.Renderer.RgbaColorRenderer; var at = (cell.TopLeft + new int2(3, 28)).ToVector3();
						var available = 122f * inventory.Available(index) / inventory.Capacity(index);
						var physical = 122f * inventory.Amount(index) / inventory.Capacity(index);
						var incoming = 122f * inventory.Incoming(index) / inventory.Capacity(index);
						cr.FillRect(at, at + new Vector3(available, 1, 0), Color.LightGreen);
						cr.FillRect(at + new Vector3(available, 0, 0), at + new Vector3(physical, 1, 0), Color.Orange);
						cr.FillRect(at + new Vector3(physical, 0, 0), at + new Vector3(physical + incoming, 1, 0), Color.SkyBlue);
						if (inventory.Reserved(index) > 0 || inventory.Incoming(index) > 0)
							font.DrawText(inventory.Reserved(index) > 0 ? "↑" : "↓", (cell.TopLeft + new int2(114, 2)).ToVector2(), inventory.Reserved(index) > 0 ? Color.Orange : Color.SkyBlue);
					}
					if (Ui.MouseOverWidget == this && cell.Contains(Viewport.LastMousePos)) detail = StockDetail(side == 0, g);
				}
			var status = Cart?.TraitOrDefault<MedievalManualTransfer>();
			var message = detail ?? (showNotice ? notice : status?.Busy == true ? status.Status : MedievalManualTransfer.UnavailableReason(Cart) ?? status?.Status ?? notice);
			Text(WidgetUtils.WrapText(message, 390, font), 9, 343, Color.LightGray);
			if (dragged >= 0)
			{
				var target = Area(!loading);
				Game.Renderer.RgbaColorRenderer.DrawRect(target.TopLeft.ToVector3(), target.BottomRight.ToVector3(), 2, Color.Gold);
				var at = new int2(Math.Clamp(pointer.X, RenderOrigin.X + 6, RenderOrigin.X + 230), Math.Clamp(pointer.Y - 22, RenderOrigin.Y, RenderOrigin.Y + 350));
				WidgetUtils.DrawPanel("md-panel", new Rectangle(at.X, at.Y, 170, 25));
				font.DrawText(StorageGoods.Names[dragged] + " → choisir quantité", (at + new int2(5, 5)).ToVector2(), Color.Gold);
			}
		}
	}
}
