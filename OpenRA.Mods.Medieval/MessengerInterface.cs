using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MessengerEnvelopeRenderable : IRenderable, IFinalizedRenderable
	{
		public WPos Pos { get; }
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public MessengerEnvelopeRenderable(WPos pos) { Pos = pos; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new MessengerEnvelopeRenderable(Pos + offset);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) => Rectangle.Empty;
		public void Render(WorldRenderer wr)
		{
			var point = wr.Viewport.WorldToViewPx(wr.ScreenPxPosition(Pos)) + new int2(-9, -51);
			var p = new Vector3(point.X, point.Y, 0); var cr = Game.Renderer.RgbaColorRenderer;
			cr.FillRect(p, p + new Vector3(18, 13, 0), Color.FromArgb(243, 224, 166));
			cr.DrawRect(p, p + new Vector3(18, 13, 0), 1, Color.FromArgb(58, 44, 30));
			cr.DrawLine(p + new Vector3(1, 1, 0), p + new Vector3(9, 8, 0), 1, Color.FromArgb(111, 78, 42));
			cr.DrawLine(p + new Vector3(17, 1, 0), p + new Vector3(9, 8, 0), 1, Color.FromArgb(111, 78, 42));
			cr.FillRect(p + new Vector3(8, 7, 0), p + new Vector3(11, 10, 0), Color.DarkRed);
		}
	}
	public sealed class MedievalMessengerTargetGenerator : OrderGenerator
	{
		readonly Actor messenger;
		readonly string body;
		readonly string command;
		readonly uint revision;
		readonly WorldRenderer renderer;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalMessengerTargetGenerator(World w, WorldRenderer wr, Actor messenger, string body, string command = "MedievalSendLetter") : base(w)
		{ renderer = wr; this.messenger = messenger; this.body = body; this.command = command; revision = messenger.Trait<MedievalMessenger>().Revision; }
		bool Valid(World w) => messenger.IsInWorld && !messenger.IsDead && messenger.Trait<MedievalMessenger>().Revision == revision
			&& messenger.Trait<MedievalMessenger>().CanCompose(messenger, w.LocalPlayer);
		Actor At(World w, CPos cell) => w.ActorMap.GetActorsAt(cell).FirstOrDefault(a => messenger.Trait<MedievalMessenger>().CanTarget(messenger, w.LocalPlayer, a)
			&& (command != "MedievalInviteBarter" || MedievalBarter.Depots(a.Owner).Any()));
		public string PreviewText => "Cliquez une caserne visible d'un autre joueur. Clic droit : annuler.";
		protected override void Tick(World w) { if (!Valid(w)) w.CancelInputMode(); }
		protected override IEnumerable<Order> OrderInner(World w, CPos cell, int2 pixel, MouseInput mi)
		{
			if (w.Paused || !Valid(w) || At(w, cell) is not Actor target) yield break;
			yield return MedievalMessenger.Command(w.LocalPlayer, messenger, command, body, target); w.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World w)
		{
			if (!Valid(w) || At(w, renderer.Viewport.ViewToWorld(Viewport.LastMousePos)) is not Actor target) yield break;
			foreach (var tile in target.Info.TraitInfo<BuildingInfo>().Tiles(target.Location))
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(w, tile, Color.LimeGreen)) yield return line;
		}
		protected override string GetCursor(World w, CPos cell, int2 pixel, MouseInput mi) => Valid(w) && At(w, cell) != null ? "ability" : "move-blocked";
	}
	public sealed class MedievalMessengerLogic : ChromeLogic
	{
		readonly World world;
		readonly TextFieldWidget input;
		Actor edited;
		uint revision;
		Actor Selected() => world.Selection.Actors.Count() == 1
			? world.Selection.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.TraitOrDefault<MedievalMessenger>() != null && !world.FogObscures(a)) : null;
		Actor Incoming() => world.ActorsWithTrait<MedievalMessenger>().Where(a => a.Actor.IsInWorld && !a.Actor.IsDead
			&& a.Trait.Letter.Present && !a.Trait.Letter.Opened && a.Trait.Letter.Recipient == world.LocalPlayer.InternalName
			&& a.Trait.DeliveredTo(a.Actor, world.LocalPlayer)).OrderBy(a => a.Actor.ActorID).Select(a => a.Actor).FirstOrDefault();
		public override void Tick()
		{
			if (world.LocalPlayer == null) return;
			var actor = Selected(); var currentRevision = actor?.Trait<MedievalMessenger>().Revision ?? 0;
			if (actor == edited && currentRevision == revision) return;
			input.YieldKeyboardFocus(); input.Text = ""; edited = actor; revision = currentRevision;
		}
		[ObjectCreator.UseCtor]
		public MedievalMessengerLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.world = world; var p = world.LocalPlayer;
			input = widget.Get<TextFieldWidget>("MD_MAIL_INPUT"); input.MaxLength = MessengerLetter.MaxLength;
			if (p == null)
			{
				widget.Get("MD_MAIL_PANEL").IsVisible = () => false;
				widget.Get("MD_MAIL_ALERT").IsVisible = () => false;
				return;
			}
			widget.Get("MD_MAIL_PANEL").IsVisible = () => Selected() != null;
			widget.Get<LabelWidget>("MD_MAIL_TITLE").GetText = () => "MESSAGER - COURRIER ENTRE CASERNES";
			widget.Get<LabelWidget>("MD_MAIL_STATUS").GetText = () => Selected()?.Trait<MedievalMessenger>().Status ?? "";
			widget.Get<LabelWidget>("MD_MAIL_ROUTE").GetText = () => Selected() is Actor a ? "Messager de " + a.Owner.ResolvedPlayerName : "";
			widget.Get<LabelWidget>("MD_MAIL_BODY").GetText = () =>
			{
				if (Selected() is not Actor a) return "";
				var mail = a.Trait<MedievalMessenger>(); var text = mail.VisibleText(a, p);
				return text.Length > 0 ? text : mail.CanReceive(a, p) || mail.DeliveredTo(a, p) ? "Lettre scellee. Lire le courrier ou renvoyer sans l'ouvrir."
					: mail.CanCompose(a, p) ? "Brouillon : " + input.Text : "Le courrier sera accessible a son destinataire devant sa caserne.";
			};
			widget.Get<LabelWidget>("MD_MAIL_INPUT_LABEL").GetText = () => $"Votre message / reponse : {input.Text.Length}/{MessengerLetter.MaxLength}";
			input.IsDisabled = () => world.Paused || Selected() is not Actor a || !(a.Trait<MedievalMessenger>().CanCompose(a, p)
				|| a.Trait<MedievalMessenger>().CanReceive(a, p) && a.Trait<MedievalMessenger>().Letter.Opened);
			void Bind(string id, string title, string command, System.Func<Actor, bool> allowed, bool text = false)
			{
				var b = widget.Get<ButtonWidget>(id); b.GetText = () => title;
				b.IsDisabled = () => world.Paused || Selected() is not Actor a || !allowed(a) || text && MessengerLetter.Normalize(input.Text).Length == 0;
				b.OnClick = () => { if (!b.IsDisabled() && Selected() is Actor a) world.IssueOrder(MedievalMessenger.Command(p, a, command, text ? input.Text : "")); };
			}
			var send = widget.Get<ButtonWidget>("MD_MAIL_SEND"); send.GetText = () => "Envoyer : choisir la caserne";
			send.IsDisabled = () => world.Paused || Selected() is not Actor a || !a.Trait<MedievalMessenger>().CanCompose(a, p) || MessengerLetter.Normalize(input.Text).Length == 0;
			send.OnClick = () => { if (!send.IsDisabled() && Selected() is Actor a) { input.YieldKeyboardFocus(); world.OrderGenerator = new MedievalMessengerTargetGenerator(world, worldRenderer, a, input.Text); } };
			Bind("MD_MAIL_READ", "Lire le courrier", "MedievalReadLetter", a => a.Trait<MedievalMessenger>().DeliveredTo(a, p)
				&& a.Trait<MedievalMessenger>().Letter.Recipient == p.InternalName && !a.Trait<MedievalMessenger>().Letter.Opened);
			Bind("MD_MAIL_REPLY", "Repondre et renvoyer", "MedievalReplyLetter", a => a.Trait<MedievalMessenger>().CanReceive(a, p) && a.Trait<MedievalMessenger>().Letter.Opened, true);
			Bind("MD_MAIL_RETURN", "Renvoyer sans reponse", "MedievalReturnMessenger", a => a.Trait<MedievalMessenger>().CanReceive(a, p));
			widget.Get<ButtonWidget>("MD_MAIL_RETURN").GetText = () => Selected()?.Trait<MedievalMessenger>().Letter.Opened == true ? "Renvoyer sans reponse" : "Refuser et renvoyer";
			Bind("MD_MAIL_RECALL", "Rappeler le messager", "MedievalRecallMessenger", a => a.Owner == p && a.Trait<MedievalMessenger>().Journey is MessengerJourney.Outbound or MessengerJourney.Waiting);
			Bind("MD_MAIL_RESUME", "Reprendre le trajet", "MedievalResumeMessenger", a => a.Owner == p && a.Trait<MedievalMessenger>().Paused);
			Bind("MD_MAIL_CLEAR", "Classer / nouveau courrier", "MedievalClearLetter", a => a.Owner == p && a.Trait<MedievalMessenger>().Journey is MessengerJourney.Returned or MessengerJourney.Stranded);
			widget.Get<LabelWidget>("MD_MAIL_HINT").GetText = () => "Vulnerable : les soldats peuvent l'attaquer. Le courrier est perdu si le messager meurt.";
			var alert = widget.Get<ButtonWidget>("MD_MAIL_ALERT"); alert.IsVisible = () => Incoming() != null;
			alert.GetText = () => "Un messager vous attend : voir le courrier";
			alert.OnClick = () => { if (Incoming() is Actor a) { world.Selection.Combine(world, [a], false, true); worldRenderer.Viewport.Center(a.CenterPosition); } };
		}
	}
}
