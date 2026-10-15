#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	// A bounded response: never chase an attacker across the map or discard the parent's mission.
	public sealed class Retaliation : Activity, IActivityNotifyStanceChanged
	{
		readonly Actor attacker, protectedUnit;
		readonly AutoTarget autoTarget;
		readonly IMove move;
		readonly WDist radius;
		readonly bool returnToOrigin;
		Player owner;
		WPos origin;
		CPos cell;
		bool started, ending, returning;

		public Retaliation(Actor self, Actor attacker, WDist radius, Actor protectedUnit = null, bool returnToOrigin = true)
		{
			this.attacker = attacker;
			this.radius = radius;
			this.protectedUnit = protectedUnit;
			this.returnToOrigin = returnToOrigin;
			autoTarget = self.Trait<AutoTarget>();
			move = self.TraitOrDefault<IMove>();
			ChildHasPriority = false;
		}

		protected override void OnFirstRun(Actor self)
		{
			owner = self.Owner;
			origin = self.CenterPosition;
			cell = self.Location;
		}

		bool Valid(Actor self)
		{
			if (autoTarget.IsTraitDisabled || autoTarget.Stance == UnitStance.HoldFire || self.Owner != owner
				|| attacker == null || !attacker.IsInWorld || attacker.IsDead || attacker.WillDispose || attacker.AppearsFriendlyTo(self)
				|| !autoTarget.Info.RetaliateAgainstNeutral && self.Owner.RelationshipWith(attacker.Owner) == PlayerRelationship.Neutral
				|| !attacker.CanBeViewedByPlayer(self.Owner))
				return false;

			if (protectedUnit != null && (!protectedUnit.IsInWorld || protectedUnit.IsDead || !self.Owner.IsAlliedWith(protectedUnit.Owner)))
				return false;

			var anchor = protectedUnit?.CenterPosition ?? origin;
			return (attacker.CenterPosition - anchor).HorizontalLengthSquared <= (long)radius.Length * radius.Length
				&& (self.CenterPosition - anchor).HorizontalLengthSquared <= (long)radius.Length * radius.Length
				&& autoTarget.ActiveAttackBases.Any(a => a.HasAnyValidWeapons(Target.FromActor(attacker)));
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || self.Owner != owner)
			{
				ChildActivity?.Cancel(self);
				return TickChild(self);
			}

			if (returning)
				return TickChild(self);

			if (!ending && !Valid(self))
			{
				ending = true;
				ChildActivity?.Cancel(self);
			}

			if (!ending && !started)
			{
				started = true;
				foreach (var attack in autoTarget.ActiveAttackBases)
					QueueChild(attack.GetAttackActivity(self, AttackSource.AutoTarget, Target.FromActor(attacker), move != null && autoTarget.Info.AllowMovement, false));
			}

			if (!TickChild(self))
				return false;

			if (returnToOrigin && move != null && self.Location != cell)
			{
				returning = true;
				QueueChild(move.MoveTo(cell, 1, targetLineColor: Color.Green));
				return false;
			}

			return true;
		}

		void IActivityNotifyStanceChanged.StanceChanged(Actor self, AutoTarget trait, UnitStance oldStance, UnitStance newStance)
		{
			if (newStance == UnitStance.HoldFire)
				Cancel(self, true);
		}

		public override IEnumerable<Target> GetTargets(Actor self) => returning ? [Target.FromCell(self.World, cell)] : [Target.FromActor(attacker)];
	}
}
