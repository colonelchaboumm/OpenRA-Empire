using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum DiplomaticRequestState { Travelling, Delivered, Accepted, Refused, Cancelled }
	public sealed class DiplomaticRequest
	{
		public uint Id, Revision, MailRevision;
		public Player Sender, Recipient;
		public Actor Messenger;
		public bool Alliance, Arrived;
		public DiplomaticRequestState State;
		public string Status = "Le messager est en route.";
		public bool Pending => State is DiplomaticRequestState.Travelling or DiplomaticRequestState.Delivered;
		public bool Between(Player a, Player b) => Sender == a && Recipient == b || Sender == b && Recipient == a;
		public int Hash => new[] { (int)Id, (int)Revision, (int)MailRevision, (int)Sender.PlayerActor.ActorID,
			(int)Recipient.PlayerActor.ActorID, (int)Messenger.ActorID, Alliance ? 1 : 0, Arrived ? 1 : 0, (int)State }
			.Aggregate(17, (h, n) => unchecked(h * 31 + n));
	}

	public class MedievalDiplomacyInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDiplomacy(init.Self.World);
	}
	public sealed class MedievalDiplomacy : IWorldLoaded, ITick, ISync, INotifyPlayerDisconnected
	{
		readonly World world;
		readonly List<DiplomaticRequest> requests = [];
		// Network disconnect notifications run on synchronized frames. Lobby client
		// states arrive asynchronously and must never decide simulation orders.
		readonly HashSet<Player> disconnected = [];
		uint nextId = 1;
		[VerifySync] int cadence;
		public IReadOnlyList<DiplomaticRequest> Requests => requests;
		[VerifySync] public int RequestsHash => requests.Aggregate(unchecked((int)nextId), (h, r) => unchecked(h * 31 + r.Hash));
		[VerifySync] public int RelationsHash => Nations.SelectMany(p => Nations.Select(q => (int)p.RelationshipWith(q))).Aggregate(17, (h, n) => unchecked(h * 31 + n));
		[VerifySync] public int DeparturesHash => disconnected.OrderBy(p => p.PlayerActor.ActorID).Aggregate(17, (h, p) => unchecked(h * 31 + (int)p.PlayerActor.ActorID));
		public static bool Nation(Player p) => p != null && !p.NonCombatant && !p.Spectating;
		public bool HasLeft(Player p) => p != null && disconnected.Contains(p);
		public bool Available(Player p) => Nation(p) && p.WinState != WinState.Lost && !HasLeft(p);
		public IEnumerable<Player> Nations => world.Players.Where(Nation).OrderBy(p => p.PlayerActor.ActorID);
		public MedievalDiplomacy(World world) { this.world = world; }
		public static string RelationName(PlayerRelationship relation) => relation == PlayerRelationship.Ally ? "Allié" : relation == PlayerRelationship.Enemy ? "Ennemi" : "Neutre";
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			if (w.Type != WorldType.Regular) return;
			var nations = Nations.ToArray();
			foreach (var p in nations) foreach (var q in nations.Where(q => q != p)) SetMask(p, q, PlayerRelationship.Neutral);
			RefreshVision(nations);
		}
		static void SetMask(Player a, Player b, PlayerRelationship relation)
		{
			a.AlliedPlayersMask = a.AlliedPlayersMask.Except(b.PlayerMask);
			a.EnemyPlayersMask = a.EnemyPlayersMask.Except(b.PlayerMask);
			if (relation == PlayerRelationship.Ally) a.AlliedPlayersMask = a.AlliedPlayersMask.Union(b.PlayerMask);
			if (relation == PlayerRelationship.Enemy) a.EnemyPlayersMask = a.EnemyPlayersMask.Union(b.PlayerMask);
		}
		void RefreshVision(IEnumerable<Player> owners)
		{
			var set = owners.ToHashSet();
			foreach (var a in world.Actors.Where(a => a.IsInWorld && set.Contains(a.Owner)).ToArray())
			{
				foreach (var shroud in a.TraitsImplementing<AffectsShroud>()) shroud.RefreshRelationships(a);
				if (a.OccupiesSpace != null) world.ActorMap.UpdateOccupiedCells(a.OccupiesSpace);
			}
		}
		void Change(Player a, Player b, PlayerRelationship relation)
		{
			SetMask(a, b, relation); SetMask(b, a, relation);
			foreach (var r in requests.Where(r => r.Pending && r.Between(a, b)).ToArray()) End(r, DiplomaticRequestState.Cancelled, "Relations modifiées : demande annulée.");
			if (relation == PlayerRelationship.Ally) { a.Shroud.Explore(b.Shroud); b.Shroud.Explore(a.Shroud); }
			RefreshVision([a, b]);
			if (relation == PlayerRelationship.Enemy)
			{
				var barter = world.WorldActor.TraitOrDefault<MedievalBarter>();
				if (barter != null) foreach (var deal in barter.Deals.Where(d => d.Active && d.Side(a) >= 0 && d.Side(b) >= 0).ToArray())
					barter.Abort(deal, "La guerre interrompt cet échange.");
			}
			else
			{
				// Cancel both running and queued attacks, including forced attacks, without
				// discarding the escort/patrol parent or unrelated movement orders.
				foreach (var actor in world.Actors.Where(x => x.Owner == a || x.Owner == b).ToArray())
				{
					var other = actor.Owner == a ? b : a;
					foreach (var attack in actor.CurrentActivity?.ActivitiesImplementing<Attack>().Where(x => x.TargetsPlayer(other)).ToArray() ?? []) attack.Cancel(actor, true);
					if (actor.TraitOrDefault<MedievalSaboteur>() is { } saboteur && saboteur.Victim?.Owner == other) saboteur.Cancel(actor);
				}
			}
		}
		public void DeclareWar(Player a, Player b)
		{
			if (!Nation(a) || !Nation(b) || a == b || a.RelationshipWith(b) == PlayerRelationship.Enemy && b.RelationshipWith(a) == PlayerRelationship.Enemy) return;
			Change(a, b, PlayerRelationship.Enemy);
		}
		public DiplomaticRequest Pending(Player a, Player b) => requests.LastOrDefault(r => r.Pending && r.Between(a, b));
		public bool Visible(DiplomaticRequest r, Player p) => r.Sender == p || r.Recipient == p && r.Arrived;
		bool MessengerPresent(DiplomaticRequest r) => MedievalStockpile.Active(r.Messenger) && !r.Messenger.WillDispose && r.Messenger.Owner == r.Sender
			&& r.Messenger.Trait<MedievalMessenger>().Revision == r.MailRevision
			&& r.Messenger.Trait<MedievalMessenger>().Journey is MessengerJourney.Outbound or MessengerJourney.Waiting;
		bool RecipientReady(DiplomaticRequest r, Player p) => r != null && r.Pending && r.Recipient == p && Available(r.Sender) && Available(r.Recipient) && MessengerPresent(r)
			&& r.Messenger.Trait<MedievalMessenger>().CanReceive(r.Messenger, p);
		public bool CanRespond(DiplomaticRequest r, Player p) => r?.State == DiplomaticRequestState.Delivered && RecipientReady(r, p);
		public Actor Destination(Player sender, Player recipient) => world.Actors.Where(a => a.Owner == recipient && MedievalMessenger.Barracks(a)
			&& !a.WillDispose && a.CanBeViewedByPlayer(sender)).OrderBy(a => a.ActorID).FirstOrDefault();
		public Actor Courier(Player p) => world.ActorsHavingTrait<MedievalMessenger>().Where(a => a.Owner == p && !a.WillDispose
			&& a.Trait<MedievalMessenger>().CanCompose(a, p)).OrderBy(a => a.ActorID).FirstOrDefault();
		public bool CanRequest(Player p, Player other, bool alliance) => Available(p) && Available(other) && p != other && Pending(p, other) == null
			&& (alliance ? p.RelationshipWith(other) != PlayerRelationship.Ally : p.RelationshipWith(other) == PlayerRelationship.Enemy);
		public static Order Request(Player p, Actor courier, Actor destination, bool alliance) =>
			MedievalMessenger.Command(p, courier, alliance ? "MedievalRequestAlliance" : "MedievalRequestPeace", target: destination);
		public static Order Respond(Player p, DiplomaticRequest r, bool accept) => new(accept ? "MedievalAcceptDiplomacy" : "MedievalRefuseDiplomacy", p.PlayerActor, false)
			{ ExtraData = r.Id, TargetString = r.Revision.ToString(CultureInfo.InvariantCulture) };
		void End(DiplomaticRequest r, DiplomaticRequestState state, string message)
		{
			r.State = state; r.Revision++; r.Status = message;
			if (MessengerPresent(r)) r.Messenger.Trait<MedievalMessenger>().Resolve(r.Messenger, r.Sender,
				MedievalMessenger.Command(r.Sender, r.Messenger, "MedievalRecallMessenger"));
		}
		public void Resolve(Player p, Order order)
		{
			if (!Available(p)) return;
			if (order.OrderString is "MedievalRequestAlliance" or "MedievalRequestPeace")
			{
				if (order.Target.Type != TargetType.Actor || order.ExtraActors is not { Length: 1 }) return;
				var courier = order.Target.Actor; var target = order.ExtraActors[0]; var mail = courier.TraitOrDefault<MedievalMessenger>();
				var alliance = order.OrderString == "MedievalRequestAlliance";
				if (mail == null || target == null || !CanRequest(p, target.Owner, alliance) || order.ExtraData != mail.Revision
					|| !mail.CanTarget(courier, p, target) || requests.Count(r => r.Pending && (r.Sender == p || r.Recipient == p)) >= 8) return;
				mail.Resolve(courier, p, MedievalMessenger.Command(p, courier, "MedievalSendLetter", alliance
					? "Demande d'alliance : partager notre vision et permettre le ravitaillement de nos nations." : "Demande de paix : cesser les hostilités et redevenir neutres.", target));
				if (mail.Journey != MessengerJourney.Outbound) return;
				requests.Add(new DiplomaticRequest { Id = nextId++, Sender = p, Recipient = target.Owner, Messenger = courier, Alliance = alliance, MailRevision = mail.Revision });
				return;
			}
			if (order.OrderString is not ("MedievalAcceptDiplomacy" or "MedievalRefuseDiplomacy")) return;
			var request = requests.FirstOrDefault(r => r.Id == order.ExtraData);
			if (!CanRespond(request, p) || !uint.TryParse(order.TargetString, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision != request.Revision) return;
			request.Arrived = true;
			var accept = order.OrderString == "MedievalAcceptDiplomacy";
			var messenger = request.Messenger.Trait<MedievalMessenger>();
			messenger.Resolve(request.Messenger, p, MedievalMessenger.Command(p, request.Messenger, "MedievalReadLetter"));
			messenger.Resolve(request.Messenger, p, MedievalMessenger.Command(p, request.Messenger, "MedievalReplyLetter", accept
				? request.Alliance ? "Alliance acceptée." : "Paix acceptée : nos nations sont neutres." : "Votre proposition diplomatique est refusée."));
			request.State = accept ? DiplomaticRequestState.Accepted : DiplomaticRequestState.Refused; request.Revision++;
			request.Status = accept ? request.Alliance ? "Alliance acceptée : vision et ravitaillement partagés." : "Paix acceptée : relations neutres." : "Demande refusée. Relations inchangées.";
			if (accept) Change(request.Sender, request.Recipient, request.Alliance ? PlayerRelationship.Ally : PlayerRelationship.Neutral);
		}
		void ITick.Tick(Actor self)
		{
			if (--cadence > 0) return; cadence = 10;
			foreach (var r in requests.Where(r => r.Pending).ToArray())
			{
				if (!MessengerPresent(r) || !Available(r.Sender) || !Available(r.Recipient))
				{ End(r, DiplomaticRequestState.Cancelled, "Messager ou destinataire indisponible : demande annulée."); continue; }
				if (r.State == DiplomaticRequestState.Travelling && RecipientReady(r, r.Recipient))
				{ r.State = DiplomaticRequestState.Delivered; r.Arrived = true; r.Revision++; r.Status = "Le messager est arrivé : accepter ou refuser la demande."; }
			}
			if (requests.Count > 128) requests.RemoveAll(r => !r.Pending && r.Id < nextId - 64);
		}
		void INotifyPlayerDisconnected.PlayerDisconnected(Actor self, Player player)
		{
			if (!disconnected.Add(player)) return;
			foreach (var r in requests.Where(r => r.Pending && (r.Sender == player || r.Recipient == player)).ToArray())
				End(r, DiplomaticRequestState.Cancelled, "Un joueur s’est déconnecté : demande annulée.");
		}
	}
	public class MedievalDiplomacyOrdersInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDiplomacyOrders();
	}
	public sealed class MedievalDiplomacyOrders : IResolveOrder
	{
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{ if (order.Subject == self) self.World.WorldActor.Trait<MedievalDiplomacy>().Resolve(self.Owner, order); }
	}
	public class MedievalDiplomacyCombatInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDiplomacyCombat();
	}
	public sealed class MedievalDiplomacyCombat : IResolveOrder, INotifyAttack
	{
		void War(Actor self, in Target target)
		{
			var owner = target.Type == TargetType.Actor ? target.Actor.Owner : target.Type == TargetType.FrozenActor ? target.FrozenActor.Owner : null;
			self.World.WorldActor.Trait<MedievalDiplomacy>().DeclareWar(self.Owner, owner);
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.Subject == self && order.OrderString is "Attack" or "ForceAttack" && order.Target.IsValidFor(self)
				&& self.TraitsImplementing<AttackBase>().Any(a => !a.IsTraitDisabled && a.HasAnyValidWeapons(order.Target))) War(self, order.Target);
		}
		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament armament, Barrel barrel) => War(self, target);
		void INotifyAttack.Attacking(Actor self, in Target target, Armament armament, Barrel barrel) { }
	}
}
