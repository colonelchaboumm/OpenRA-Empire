using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// Opt-in native audit oracle, frozen from the pre-optimization target chooser.
	// Compare decisions against the old implementation on actual live actors.
	static class TargetingReference
	{
		static readonly MethodInfo Choose = typeof(AutoTarget).GetMethod("ChooseTarget", BindingFlags.Instance | BindingFlags.NonPublic);
		public static int MatchedTargets { get; private set; }
		public static int Verify(Actor self)
		{
			if (self.Disposed || !self.IsInWorld || self.IsDead) return 0;
			var auto = self.TraitOrDefault<AutoTarget>();
			if (auto == null) return 0;
			var checks = 0;
			foreach (var attack in auto.ActiveAttackBases)
				foreach (var move in new[] { false, true })
					foreach (var turn in new[] { false, true })
						foreach (var range in new[] { new WDist(4096), new WDist(8192) })
						{
							var stances = attack.UnforcedAttackTargetStances();
							var expected = Reference(self, attack, stances, range, move, turn);
							var actual = (Target)Choose.Invoke(auto, new object[] { self, attack, stances, range, move, turn });
							if (actual.Type != expected.Type || actual.Type == TargetType.Actor && actual.Actor != expected.Actor
								|| actual.Type == TargetType.FrozenActor && actual.FrozenActor != expected.FrozenActor)
								throw new InvalidOperationException($"Target selection changed for {self}, move={move}, turn={turn}, range={range}.");
							checks++;
							if (actual.Type != TargetType.Invalid) MatchedTargets++;
						}
			return checks;
		}
		static Target Reference(Actor self, AttackBase ab, PlayerRelationship attackStances, WDist scanRange, bool allowMove, bool allowTurn)
		{
			var chosenTarget = Target.Invalid;
			var chosenTargetPriority = int.MinValue;
			var chosenTargetRange = 0;

			var activePriorities = self.TraitsImplementing<AutoTargetPriority>().Where(t => !t.IsTraitDisabled).OrderByDescending(t => t.Info.Priority).Select(t => t.Info).ToList();
			if (activePriorities.Count == 0)
				return chosenTarget;

			var targetsInRange = self.World.FindActorsInCircle(self.CenterPosition, scanRange)
				.Select(Target.FromActor);

			if (allowMove || ab.Info.TargetFrozenActors)
				targetsInRange = targetsInRange
					.Concat(self.Owner.FrozenActorLayer.FrozenActorsInCircle(self.World, self.CenterPosition, scanRange)
					.Select(Target.FromFrozenActor));

			// PERF: Avoid allocating a new list for each target.
			List<AutoTargetPriorityInfo> validPriorities = [];

			foreach (var target in targetsInRange)
			{
				BitSet<TargetableType> targetTypes;
				Player owner;
				if (target.Type == TargetType.Actor)
				{
					// PERF: Most units can only attack enemy units. If this is the case but the target is not an enemy, we
					// can bail early and avoid the more expensive targeting checks and armament selection. For groups of
					// allied units, this helps significantly reduce the cost of auto target scans. This is important as
					// these groups will continuously rescan their allies until an enemy finally comes into range.
					if (attackStances == PlayerRelationship.Enemy && !target.Actor.AppearsHostileTo(self))
						continue;

					// Check whether we can auto-target this actor
					targetTypes = target.Actor.GetEnabledTargetTypes();

					if (PreventsAutoTarget(self, target.Actor) || !target.Actor.CanBeViewedByPlayer(self.Owner))
						continue;

					owner = target.Actor.Owner;
				}
				else if (target.Type == TargetType.FrozenActor)
				{
					if (attackStances == PlayerRelationship.Enemy && self.Owner.RelationshipWith(target.FrozenActor.Owner) == PlayerRelationship.Ally)
						continue;

					// Bot-controlled units aren't yet capable of understanding visibility changes
					// Prevent that bot-controlled units endlessly fire at frozen actors.
					// TODO: Teach the AI to support long range artillery units with units that provide line of sight
					if (self.Owner.IsBot && target.FrozenActor.Actor == null)
						continue;

					targetTypes = target.FrozenActor.TargetTypes;
					owner = target.FrozenActor.Owner;
				}
				else
					continue;

				validPriorities.Clear();
				foreach (var ati in activePriorities)
				{
					// Already have a higher priority target
					if (ati.Priority < chosenTargetPriority)
						continue;

					// Incompatible relationship
					if (!ati.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(owner)))
						continue;

					// Incompatible target types
					if (!ati.ValidTargets.Overlaps(targetTypes) || ati.InvalidTargets.Overlaps(targetTypes))
						continue;

					validPriorities.Add(ati);
				}

				if (validPriorities.Count == 0)
					continue;

				// Priorities are ordered highest first. A farther candidate with no higher
				// priority cannot replace the current choice, regardless of its weapons.
				// Preserve the existing rounded distance and stable tie behavior.
				var targetRange = (target.CenterPosition - self.CenterPosition).Length;
				if (chosenTarget.Type != TargetType.Invalid && validPriorities[0].Priority == chosenTargetPriority
					&& targetRange >= chosenTargetRange)
					continue;

				// Make sure that we can actually fire on the actor
				var armaments = ab.ChooseArmamentsForTarget(target, false);
				if (!allowMove)
				{
					// PERF: This lambda captures, contain it within a local function to prevent
					// the compiler allocating the helper class at the top of the loop.
					static Func<Armament, bool> IsInRange(Actor self, Target target) =>
						arm =>
							target.IsInRange(self.CenterPosition, arm.MaxRange()) &&
							!target.IsInRange(self.CenterPosition, arm.Weapon.MinRange);

					armaments = armaments.Where(IsInRange(self, target));
				}

				if (!armaments.Any())
					continue;

				if (!allowTurn && !ab.TargetInFiringArc(self, target, ab.Info.FacingTolerance))
					continue;

				// Evaluate whether we want to target this actor
				foreach (var ati in validPriorities)
				{
					if (chosenTarget.Type == TargetType.Invalid || chosenTargetPriority < ati.Priority
						|| (chosenTargetPriority == ati.Priority && targetRange < chosenTargetRange))
					{
						chosenTarget = target;
						chosenTargetPriority = ati.Priority;
						chosenTargetRange = targetRange;
					}
				}
			}

			return chosenTarget;
		}

		static bool PreventsAutoTarget(Actor attacker, Actor target)
		{
			foreach (var deat in target.TraitsImplementing<IDisableEnemyAutoTarget>())
				if (deat.DisableEnemyAutoTarget(target, attacker))
					return true;

			return false;
		}
	}
}
