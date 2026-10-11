using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalHerdingInfo : TraitInfo, Requires<MobileInfo>
	{
		public readonly int CaptureTicks = 75;
		[GrantedConditionReference] public readonly string BusyCondition = "herding";
		public override object Create(ActorInitializer init) => new MedievalHerding(this);
	}
	public sealed class MedievalHerding : ITick, IIssueOrder, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		readonly MedievalHerdingInfo info;
		public Actor Animal { get; private set; }
		public Actor Destination { get; private set; }
		[VerifySync] int stage, capture, cadence, condition = Actor.InvalidConditionToken, stalled, failedMoves;
		[VerifySync] WPos lastGuide, lastAnimal;
		[VerifySync] Player originalOwner;
		[VerifySync] public int AnimalId => (int)(Animal?.ActorID ?? 0);
		[VerifySync] public int DestinationId => (int)(Destination?.ActorID ?? 0);
		public bool Busy => Animal != null;
		public bool Entering => stage == 3;
		public bool Working => Busy && capture > 0;
		public bool Leading => Busy && stage >= 2;
		public int CaptureFrame => Math.Clamp(capture * 8 / Math.Max(1, info.CaptureTicks * 100), 0, 7);
		public string Status { get; private set; } = "Pret a conduire un animal.";
		public MedievalHerding(MedievalHerdingInfo info) { this.info = info; }
		public static bool Claimed(Actor animal, Player player) => animal.Trait<MedievalAnimal>().Claimed(player);
		public static bool Profession(Actor worker, Actor animal) => worker.TraitOrDefault<MedievalWorker>()?.Info.Profession == (animal.Info.Name == "md.sheep" ? "shepherd" : "breeder");
		static bool Soldier(Actor self) => self.TraitOrDefault<MedievalMorale>() != null && self.Info.HasTraitInfo<ArmamentInfo>();
		public static bool CanHandle(Actor self, Actor animal) => MedievalStockpile.Active(self) && MedievalStockpile.Active(animal)
			&& animal.TraitOrDefault<MedievalAnimal>() != null && animal.CanBeViewedByPlayer(self.Owner)
			&& (Profession(self, animal) && (animal.Owner == self.Owner || animal.Owner.NonCombatant)
				|| Soldier(self) && animal.Owner != self.Owner && (animal.Owner.NonCombatant || self.Owner.RelationshipWith(animal.Owner) == PlayerRelationship.Enemy));
		public static Actor NearestPen(Actor animal, Player owner, WPos from) => animal.World.ActorsHavingTrait<MedievalLivestock>()
			.Where(a => a.Trait<MedievalLivestock>().CanReceive(animal, owner) && a.Trait<MedievalAgriculturalArea>().Farm(a) != null)
			.OrderBy(a => (a.CenterPosition - from).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
		static Actor DestinationFor(Actor self, Actor animal) => NearestPen(animal, self.Owner, self.CenterPosition)
			?? (Soldier(self) ? MedievalSettlement.RecruitmentStores(self.Owner).OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault() : null);
		public bool Start(Actor self, Actor animal, Actor destination)
		{
			if (!CanHandle(self, animal) || !MedievalStockpile.Active(destination) || destination.Owner != self.Owner
				|| self.TraitOrDefault<MedievalWorker>()?.Cargo > 0 || animal.Trait<MedievalAnimal>().ClaimedByGuide(self.Owner)
				|| destination.TraitOrDefault<MedievalLivestock>() is { } pen && !pen.CanReceive(animal, self.Owner)) return false;
			if (self.TraitOrDefault<MedievalRest>() is { } rest && !rest.Wake(self, true)) return false;
			Cancel(self);
			var beast = animal.Trait<MedievalAnimal>();
			if (beast.RequestedPen?.Owner == self.Owner) beast.ClearRequest(animal);
			if (destination.TraitOrDefault<MedievalLivestock>() is { } herd && !herd.Reserve(animal, self.Owner)) return false;
			self.TraitOrDefault<MedievalSiegeCrew>()?.Release(self);
			self.TraitOrDefault<MedievalPatrol>()?.Stop(self);
			self.TraitOrDefault<MedievalWorker>()?.PauseForHerding();
			self.CancelActivity();
			Animal = animal; Destination = destination; originalOwner = animal.Owner; beast.Claim(self);
			stage = 1; capture = cadence = stalled = failedMoves = 0; lastGuide = self.CenterPosition; lastAnimal = animal.CenterPosition;
			if (!string.IsNullOrEmpty(info.BusyCondition)) condition = self.GrantCondition(info.BusyCondition);
			Status = "Va chercher un animal"; return true;
		}
		public void Cancel(Actor self, bool cancelActivity = true)
		{
			if (Animal != null)
			{
				if (Destination is { Disposed: false }) Destination.TraitOrDefault<MedievalLivestock>()?.Unreserve(Animal);
				if (!Animal.Disposed) Animal.Trait<MedievalAnimal>().Release(Animal, self);
				if (cancelActivity) self.CancelActivity();
			}
			Animal = Destination = null; stage = capture = stalled = failedMoves = 0;
			if (condition != Actor.InvalidConditionToken) condition = self.RevokeCondition(condition);
		}
		public bool TryAutomatic(Actor self, Actor area = null)
		{
			if (Busy) return true;
			if (self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.Night == true) return false;
			foreach (var a in self.World.ActorsHavingTrait<MedievalAnimal>().Where(a => MedievalStockpile.Active(a) && a.Trait<MedievalAnimal>().RequestedPen?.Owner == self.Owner)
				.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID))
				if (Profession(self, a) && Start(self, a, a.Trait<MedievalAnimal>().RequestedPen)) return true;
			if (area?.TraitOrDefault<MedievalLivestock>() is not { } herd || herd.Count + herd.Incoming >= Math.Min(2, herd.Capacity)) return false;
			var candidates = self.World.ActorsHavingTrait<MedievalAnimal>().Where(a => MedievalStockpile.Active(a) && a.Trait<MedievalAnimal>().Pen == null
				&& a.Trait<MedievalAnimal>().Escort == null && !Claimed(a, self.Owner) && herd.Compatible(a) && (a.Owner == self.Owner || a.Owner.NonCombatant)
				&& a.CanBeViewedByPlayer(self.Owner) && (a.CenterPosition - area.CenterPosition).HorizontalLengthSquared <= 32768L * 32768)
				.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID);
			foreach (var a in candidates) if (Start(self, a, area)) return true;
			return false;
		}
		bool Move(Actor self, CPos cell, int near = 0)
		{
			if ((self.Location - cell).LengthSquared <= near * near) return true;
			if (self.IsIdle)
			{
				if (++failedMoves > 12) { Cancel(self); Status = "Acces bloque : liberez le passage"; return false; }
				self.QueueActivity(self.Trait<Mobile>().MoveTo(cell, near));
			}
			return false;
		}
		void ITick.Tick(Actor self)
		{
			if (self.TraitOrDefault<MedievalFirefighter>()?.Busy == true) return;
			if (!Busy)
			{
				if (--cadence <= 0)
				{
					cadence = 25;
					if (self.TraitOrDefault<MedievalWorker>() is { Cargo: 0 } && self.IsIdle && self.TraitOrDefault<MedievalRest>()?.Resting != true) TryAutomatic(self);
				}
				return;
			}
			if (!MedievalStockpile.Active(Animal) || !MedievalStockpile.Active(Destination) || Destination.Owner != self.Owner
				|| self.TraitOrDefault<MedievalRest>()?.Resting == true)
			{ Cancel(self); Status = "Conduite annulee : animal, destination ou guide indisponible"; return; }
			var beast = Animal.Trait<MedievalAnimal>();
			if (stage == 1 && (Animal.Owner != originalOwner || !Animal.CanBeViewedByPlayer(self.Owner)) || stage > 1 && beast.Escort != self)
			{ Cancel(self); Status = "Animal perdu ou repris par un autre joueur"; return; }
			if (lastGuide == self.CenterPosition && lastAnimal == Animal.CenterPosition) stalled++; else stalled = 0;
			if (lastGuide != self.CenterPosition) failedMoves = 0;
			lastGuide = self.CenterPosition; lastAnimal = Animal.CenterPosition;
			if (stalled > 750) { Cancel(self); Status = "Passage bloque : liberez l'acces et recommencez"; return; }
			if (--cadence > 0) return; cadence = 5;
			var distance = (Animal.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
			if (stage == 1)
			{
				if (beast.Pen is { } source)
				{
					var sourceHerd = source.Trait<MedievalLivestock>();
					if (!Move(self, sourceHerd.Outside)) { capture = 0; return; }
					beast.Calling = self;
				}
				else if (distance > 1536L * 1536)
				{
					capture = 0;
					if (self.IsIdle)
					{
						if (++failedMoves > 12) { Cancel(self); Status = "Animal inaccessible : liberez le passage"; return; }
						self.QueueActivity(self.Trait<Mobile>().MoveWithinRange(Target.FromActor(Animal), new WDist(1200)));
					}
					return;
				}
				if (distance > 1536L * 1536) return;
				if (beast.Escort == null) { beast.Calling = self; Animal.CancelActivity(); }
				self.CancelActivity();
				var delta = Animal.CenterPosition - self.CenterPosition;
				if (delta.HorizontalLengthSquared > 0) self.Trait<Mobile>().Facing = delta.Yaw;
				capture += 5 * (self.TraitOrDefault<MedievalNeedsFood>()?.WorkPercent ?? 100);
				Status = $"Capture de l'animal : {Math.Min(100, capture / info.CaptureTicks)}%";
				if (capture < info.CaptureTicks * 100) return;
				// Ownership changes only after physical contact and the capture delay.
				var caught = Animal;
				beast.Lead(caught, self);
				Destination.TraitOrDefault<MedievalLivestock>()?.Reserve(caught, self.Owner);
				stage = 2; capture = stalled = 0; Status = "Raccompagne l'animal, un par un";
			}
			if (stage == 2)
			{
				// Never leave the animal behind, including when it is slowed or blocked.
				if (distance > 2560L * 2560) { self.CancelActivity(); return; }
				var pen = Destination.TraitOrDefault<MedievalLivestock>();
				var cell = pen?.Outside ?? MedievalAccess.Perimeter(Destination).Where(c => self.World.Map.Contains(c) && self.Trait<Mobile>().CanEnterCell(c, check: BlockedByActor.Immovable))
					.OrderBy(c => (c - self.Location).LengthSquared).FirstOrDefault();
				if (!Move(self, cell) || distance > 1536L * 1536) return;
				if (pen == null) { beast.Release(Animal, self); Cancel(self); Status = "Animal ramene pres du depot : un eleveur peut le transferer"; return; }
				Animal.CancelActivity(); Animal.QueueActivity(Animal.Trait<Mobile>().MoveTo(pen.Gate));
				stage = 3;
			}
			if (stage == 3)
			{
				var pen = Destination.Trait<MedievalLivestock>();
				if (Animal.Location != pen.Gate || !Animal.IsIdle) return;
				var delivered = Animal;
				if (!pen.Enter(delivered)) { Cancel(self); Status = "Enclos indisponible : animal conserve a proximite"; return; }
				Cancel(self); Status = "Animal livre dans son enclos";
			}
		}
		IEnumerable<IOrderTargeter> IIssueOrder.Orders { get { yield return new AnimalTargeter(); } }
		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) => order.OrderID == "MedievalGatherAnimal" ? new Order(order.OrderID, self, target, false) : null;
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalGatherAnimal" && order.Target.Type == TargetType.Actor && CanHandle(self, order.Target.Actor))
			{
				var destination = DestinationFor(self, order.Target.Actor);
				if (destination == null || !Start(self, order.Target.Actor, destination)) Status = "Il faut un enclos compatible libre et un guide sans charge";
			}
			// The order's owning trait replaces the activity. Do not cancel its new attack or move.
			else if (order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "CaptureActor" or "MedievalAssignSiege" or "MedievalStartPatrol" or "MedievalWork" or "MedievalCommanderDeploy" or "MedievalCommanderRest" or "MedievalCommanderWake" or "EnterTransport") Cancel(self, false);
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Cancel(self); }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Cancel(self); }
		sealed class AnimalTargeter : UnitOrderTargeter
		{
			public AnimalTargeter() : base("MedievalGatherAnimal", 8, "enter", true, false) { ForceAttack = false; }
			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor) => !modifiers.HasModifier(TargetModifiers.ForceMove) && CanHandle(self, target);
			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor) => false;
		}
	}
}
