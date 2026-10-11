using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum MessengerJourney { Idle, Outbound, Waiting, Returning, Returned, Stranded }

	// The game synchronizes letters like other simulation data; UI access is restricted
	// to the author and the recipient after physical delivery and explicit opening.
	public sealed class MessengerLetter
	{
		public const int MaxLength = 320;
		string body = "";
		public string Author { get; private set; } = "";
		public string Recipient { get; private set; } = "";
		public bool Opened { get; private set; }
		public bool Present => body.Length > 0;
		public static string Normalize(string text)
		{
			var result = new StringBuilder();
			foreach (var rune in (text ?? "").EnumerateRunes())
			{
				if (result.Length >= MaxLength) break;
				if (Rune.IsControl(rune)) { if (rune.Value is 10 or 13 or 9) result.Append(' '); continue; }
				if (result.Length + rune.Utf16SequenceLength > MaxLength) break;
				result.Append(rune);
			}
			return result.ToString().Trim();
		}
		public bool Write(string text, string author, string recipient)
		{
			var clean = Normalize(text); if (clean.Length == 0 || author == recipient) return false;
			body = clean; Author = author; Recipient = recipient; Opened = false; return true;
		}
		public bool Read(string player, bool delivered)
		{
			if (!Present || player != Recipient || !delivered) return false;
			Opened = true; return true;
		}
		public string TextFor(string player, bool delivered) => player == Author || player == Recipient && delivered && Opened ? body : "";
		public void Clear() { body = Author = Recipient = ""; Opened = false; }
		public int Hash => (Author + "\0" + Recipient + "\0" + body).Aggregate(Opened ? 1 : 0, (hash, c) => unchecked(hash * 31 + c));
	}

	[IncludeStaticFluentReferences(typeof(MedievalMessenger))]
	public class MedievalMessengerInfo : TraitInfo, Requires<MobileInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalMessenger();
	}
	public sealed class MedievalMessenger : ITick, IResolveOrder, INotifyOwnerChanged, INotifyRemovedFromWorld, IRenderAnnotations, ISync
	{
		[FluentReference] const string ArrivedNotification = "medieval-mail-arrived";
		[FluentReference] const string ReturnedNotification = "medieval-mail-returned";
		public readonly MessengerLetter Letter = new();
		public Actor Origin { get; private set; }
		public Actor Destination { get; private set; }
		[VerifySync] Player destinationOwner;
		[VerifySync] public int LetterHash => Letter.Hash;
		[VerifySync] public int RouteHash => unchecked((int)(Origin?.ActorID ?? 0) * 31 + (int)(Destination?.ActorID ?? 0));
		public MessengerJourney Journey { get; private set; }
		[VerifySync] public int JourneyHash => (int)Journey;
		public uint Revision { get; private set; }
		[VerifySync] public int RevisionHash => unchecked((int)Revision);
		[VerifySync] public bool Paused { get; private set; }
		[VerifySync] public bool Refused { get; private set; }
		[VerifySync] int cadence;
		[VerifySync] int attempts;
		public string Status { get; private set; } = "Rejoignez une de vos casernes pour ecrire.";
		public static bool Barracks(Actor actor) => MedievalStockpile.Active(actor) && actor.TraitOrDefault<MedievalMilitaryWorkshop>() is { Info.Forge: false };
		public static Actor NearbyBarracks(Actor self) => self.World.Actors.Where(a => a.Owner == self.Owner && Barracks(a) && MedievalAccess.Adjacent(self, a)).OrderBy(a => a.ActorID).FirstOrDefault();
		public bool CanCompose(Actor self, Player player) => MedievalStockpile.Active(self) && player == self.Owner
			&& Journey is (MessengerJourney.Idle or MessengerJourney.Returned or MessengerJourney.Stranded) && NearbyBarracks(self) != null;
		public bool CanReceive(Actor self, Player player) => MedievalStockpile.Active(self) && Journey == MessengerJourney.Waiting
			&& Barracks(Destination) && Destination.Owner == destinationOwner && player == destinationOwner && MedievalAccess.Adjacent(self, Destination);
		public bool DeliveredTo(Actor self, Player player) => CanReceive(self, player) || Journey == MessengerJourney.Returned
			&& self.Owner == player && Barracks(Origin) && Origin.Owner == player && MedievalAccess.Adjacent(self, Origin);
		public string VisibleText(Actor self, Player player) => Letter.TextFor(player.InternalName, DeliveredTo(self, player));
		public bool CanTarget(Actor self, Player player, Actor target) => CanCompose(self, player) && Barracks(target) && target.Owner != player
			&& !target.Owner.NonCombatant && target.CanBeViewedByPlayer(player);
		// Uses the issuing player's actor as subject, so a recipient never gains ownership of the messenger.
		public static Order Command(Player player, Actor messenger, string command, string text = "", Actor target = null) =>
			new(command, player.PlayerActor, Target.FromActor(messenger), false)
			{ TargetString = text, ExtraData = messenger.Trait<MedievalMessenger>().Revision, ExtraActors = target == null ? [] : [target] };
		public void Resolve(Actor self, Player player, Order order)
		{
			if (!MedievalStockpile.Active(self) || order.ExtraData != Revision) return;
			if (order.OrderString == "MedievalSendLetter")
			{
				var target = order.ExtraActors is { Length: 1 } ? order.ExtraActors[0] : null;
				if (!CanCompose(self, player) || !Barracks(target) || target.Owner == player || target.Owner.NonCombatant
					|| !target.CanBeViewedByPlayer(player) || !Letter.Write(order.TargetString, player.InternalName, target.Owner.InternalName)) return;
				Origin = NearbyBarracks(self); Destination = target; destinationOwner = target.Owner;
				Journey = MessengerJourney.Outbound; Refused = false; Start(self); Status = "Porte un courrier a la caserne destinataire.";
			}
			else if (order.OrderString == "MedievalReadLetter")
			{
				if (Letter.Read(player.InternalName, DeliveredTo(self, player))) Status = CanReceive(self, player)
					? "Courrier ouvert. Reponse ou renvoi possible." : "Reponse lue. Pret pour un nouveau courrier.";
			}
			else if (order.OrderString == "MedievalReplyLetter")
			{
				if (!CanReceive(self, player) || !Letter.Opened || !Letter.Write(order.TargetString, player.InternalName, self.Owner.InternalName)) return;
				Refused = false; Return(self); Status = "Rapporte la reponse a sa caserne d'origine.";
			}
			else if (order.OrderString == "MedievalReturnMessenger")
			{
				if (!CanReceive(self, player)) return;
				Refused = !Letter.Opened; Return(self); Status = Refused ? "Courrier refuse sans lecture : retour." : "Retour sans reponse.";
			}
			else if (player == self.Owner && order.OrderString == "MedievalRecallMessenger" && Journey is MessengerJourney.Outbound or MessengerJourney.Waiting)
			{ Return(self); Status = "Rappel vers sa caserne d'origine."; }
			else if (player == self.Owner && order.OrderString == "MedievalResumeMessenger" && Journey is MessengerJourney.Outbound or MessengerJourney.Returning)
			{ Paused = false; self.CancelActivity(); attempts = cadence = 0; }
			else if (player == self.Owner && order.OrderString == "MedievalClearLetter" && Journey is MessengerJourney.Returned or MessengerJourney.Stranded)
			{ Letter.Clear(); Journey = MessengerJourney.Idle; Revision++; Status = "Pret pour un nouveau courrier."; }
		}
		void Start(Actor self) { self.CancelActivity(); Paused = false; attempts = cadence = 0; Revision++; }
		void Return(Actor self) { Journey = MessengerJourney.Returning; Start(self); }
		void ITick.Tick(Actor self)
		{
			if (--cadence > 0) return; cadence = 10;
			if (Journey is (MessengerJourney.Outbound or MessengerJourney.Waiting) && (!Barracks(Destination) || Destination.Owner != destinationOwner))
			{ Return(self); Status = "Caserne destinataire perdue : retour a l'origine."; }
			if (Journey == MessengerJourney.Returning && (!Barracks(Origin) || Origin.Owner != self.Owner))
			{
				self.CancelActivity(); Journey = MessengerJourney.Stranded; Paused = false; Revision++;
				Status = "Caserne d'origine perdue. Rejoignez une autre caserne."; return;
			}
			if (Paused || Journey is not (MessengerJourney.Outbound or MessengerJourney.Returning) || !self.IsIdle) return;
			var target = Journey == MessengerJourney.Outbound ? Destination : Origin;
			if (!MedievalLogistics.Approach(self, target, ref attempts)) { Status = "En route. Si l'acces est bloque, degagez le passage."; return; }
			if (Journey == MessengerJourney.Outbound)
			{
				Journey = MessengerJourney.Waiting; Status = "Devant la caserne : attend lecture, reponse ou renvoi.";
				TextNotificationsManager.AddTransientLine(destinationOwner, ArrivedNotification);
			}
			else
			{
				Journey = MessengerJourney.Returned;
				Status = Refused ? "Le destinataire a refuse de lire le courrier." : Letter.Recipient == self.Owner.InternalName ? "Une reponse vous attend." : "Messager revenu sans reponse.";
				TextNotificationsManager.AddTransientLine(self.Owner, ReturnedNotification);
			}
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString is ("Stop" or "Move") && Journey is (MessengerJourney.Outbound or MessengerJourney.Returning)) Paused = true;
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{ self.CancelActivity(); Letter.Clear(); Origin = Destination = null; destinationOwner = null; Journey = MessengerJourney.Idle; Paused = false; Revision++; }
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{ Letter.Clear(); Journey = MessengerJourney.Idle; Revision++; }
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (Letter.Present && !self.World.FogObscures(self)) yield return new MessengerEnvelopeRenderable(self.CenterPosition);
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
	public class MedievalCorrespondenceInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCorrespondence();
	}
	public sealed class MedievalCorrespondence : IResolveOrder
	{
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.Subject != self || order.Target.Type != TargetType.Actor) return;
			var messenger = order.Target.Actor;
			messenger.TraitOrDefault<MedievalMessenger>()?.Resolve(messenger, self.Owner, order);
		}
	}
}
