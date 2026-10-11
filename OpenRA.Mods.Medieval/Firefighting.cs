using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class FirefightingRules
	{
		public const int FillTicks = 38, PourTicks = 25, BucketTicks = 150, Responders = 4;
		public static int Douse(int remaining) => Math.Max(0, remaining - BucketTicks);
	}
	public class MedievalWellInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalWell();
	}
	public sealed class MedievalWell
	{
		public static bool Available(Actor well, Player owner) => MedievalStockpile.Active(well) && well.Owner == owner
			&& well.TraitOrDefault<MedievalWell>() != null && well.TraitOrDefault<MedievalBurnable>()?.Burning != true;
	}
	[TraitLocation(SystemActors.World)]
	public class MedievalFireResponseInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFireResponse();
	}
	// One bounded dispatch per second, independent of the number of empty map cells.
	public sealed class MedievalFireResponse : ITick, ISync
	{
		[VerifySync] int cadence;
		void ITick.Tick(Actor self)
		{
			if (self.World.Type != WorldType.Regular || ++cadence % 25 != 0) return;
			var fires = self.World.ActorsHavingTrait<MedievalBurnable>().Where(a => MedievalStockpile.Active(a) && a.Trait<MedievalBurnable>().Burning)
				.OrderBy(a => a.ActorID).ToArray();
			if (fires.Length == 0) return;
			var wells = self.World.ActorsHavingTrait<MedievalWell>().Where(MedievalStockpile.Active).ToArray();
			if (wells.Length == 0) return;
			var people = self.World.ActorsHavingTrait<MedievalFirefighter>().Where(MedievalStockpile.Active).ToArray();
			foreach (var fire in fires)
			{
				var origin = fire.Trait<MedievalBurnable>().Origin;
				var responding = people.Count(a => a.Trait<MedievalFirefighter>().Fire == fire);
				var sources = wells.Where(a => MedievalWell.Available(a, fire.Owner) && (a.CenterPosition - origin).HorizontalLengthSquared <= 16384L * 16384).ToArray();
				if (sources.Length == 0 || responding >= FirefightingRules.Responders) continue;
				foreach (var person in people.Where(a => a.Owner == fire.Owner && a.Trait<MedievalFirefighter>().Available(a)
					&& (a.CenterPosition - origin).HorizontalLengthSquared <= 24576L * 24576)
					.OrderBy(a => (a.CenterPosition - origin).HorizontalLengthSquared).ThenBy(a => a.ActorID))
				{
					var water = sources.OrderBy(a => (a.CenterPosition - person.CenterPosition).HorizontalLengthSquared
						+ (a.CenterPosition - origin).HorizontalLengthSquared).ThenBy(a => a.ActorID).First();
					if (person.Trait<MedievalFirefighter>().Start(person, fire, water) && ++responding >= FirefightingRules.Responders) break;
				}
			}
		}
	}
	public class MedievalFirefighterInfo : TraitInfo, Requires<MobileInfo>, Requires<MedievalRestInfo>
	{
		[GrantedConditionReference] public readonly string BusyCondition = "";
		public override object Create(ActorInitializer init) => new MedievalFirefighter(this);
	}
	public sealed class MedievalFirefighter : ITick, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, IRenderAnnotations, ISync
	{
		readonly MedievalFirefighterInfo info;
		public Actor Fire { get; private set; }
		public Actor Well { get; private set; }
		Actor shelter;
		[VerifySync] public int FireId => (int)(Fire?.ActorID ?? 0);
		[VerifySync] public int WellId => (int)(Well?.ActorID ?? 0);
		[VerifySync] public int ShelterId => (int)(shelter?.ActorID ?? 0);
		[VerifySync] public int Phase { get; private set; }
		[VerifySync] public int Progress { get; private set; }
		[VerifySync] public bool Water { get; private set; }
		[VerifySync] public int Buckets { get; private set; }
		[VerifySync] int cooldown, cadence, attempts, stalled, condition = Actor.InvalidConditionToken;
		[VerifySync] bool orderedRest;
		[VerifySync] WPos previous;
		public bool Busy => Fire != null;
		public string Description => !Busy ? "" : Phase == 1 ? "Remplit son seau au puits" : Phase == 3 ? "Jette l'eau sur le feu"
			: Water ? "Apporte un seau d'eau a l'incendie" : "Va chercher de l'eau au puits";
		public MedievalFirefighter(MedievalFirefighterInfo info) { this.info = info; }
		public bool Available(Actor self) => !Busy && cooldown <= 0 && self.TraitOrDefault<MedievalPatient>()?.Church == null && self.TraitOrDefault<MedievalHerding>()?.Busy != true
			&& self.TraitOrDefault<MedievalSiegeCrew>()?.Assigned != true
			&& (self.TraitOrDefault<MedievalMorale>() == null || self.IsIdle || self.CurrentActivity is MilitiaRound || self.Trait<MedievalRest>().Resting)
			&& !self.TraitsImplementing<AttackBase>().Any(a => a.IsAiming);
		public bool Start(Actor self, Actor fire, Actor well)
		{
			if (!Available(self) || !MedievalStockpile.Active(fire) || fire.Owner != self.Owner || fire.Trait<MedievalBurnable>().Burning != true
				|| !MedievalWell.Available(well, self.Owner)) return false;
			var rest = self.Trait<MedievalRest>(); var home = rest.Shelter; var ordered = rest.OrderedMilitaryRest;
			if (!rest.Wake(self)) return false;
			shelter = home; orderedRest = ordered;
			self.CancelActivity(); self.TraitOrDefault<MedievalWorker>()?.PauseForEmergency();
			Fire = fire; Well = well; Phase = Progress = cadence = attempts = stalled = 0; Water = false; previous = self.CenterPosition;
			if (!string.IsNullOrEmpty(info.BusyCondition)) condition = self.GrantCondition(info.BusyCondition);
			return true;
		}
		public void Cancel(Actor self, bool resume = true, bool cancelActivity = true)
		{
			var home = shelter; var ordered = orderedRest; var busy = Busy;
			if (busy && cancelActivity) self.CancelActivity();
			Fire = Well = shelter = null; Phase = Progress = attempts = stalled = 0; Water = false;
			if (condition != Actor.InvalidConditionToken) condition = self.RevokeCondition(condition);
			if (busy && resume) self.Trait<MedievalRest>().ResumeAfterEmergency(self, home, ordered);
		}
		bool Approach(Actor self, Actor target)
		{
			if (MedievalAccess.Adjacent(self, target)) return true;
			if (self.IsIdle) MedievalLogistics.Approach(self, target, ref attempts);
			return false;
		}
		bool ApproachFire(Actor self, MedievalBurnable fire)
		{
			if (!fire.YardOnly) return Approach(self, Fire);
			var cell = self.World.Map.CellContaining(fire.Origin);
			if ((self.Location - cell).LengthSquared <= 2) return true;
			if (self.IsIdle) self.QueueActivity(self.Trait<Mobile>().MoveTo(cell, 1));
			return false;
		}
		void ITick.Tick(Actor self)
		{
			if (cooldown > 0) cooldown--;
			if (!Busy) return;
			if (!MedievalStockpile.Active(Fire) || Fire.Owner != self.Owner || !Fire.Trait<MedievalBurnable>().Burning)
			{ Cancel(self); return; }
			if (!Water && !MedievalWell.Available(Well, self.Owner)) { Cancel(self); cooldown = 25; return; }
			if (self.CenterPosition == previous) stalled++; else { stalled = attempts = 0; previous = self.CenterPosition; }
			if (stalled > 375) { Cancel(self); cooldown = 250; return; }
			if (--cadence > 0) return; cadence = 5;
			var fire = Fire.Trait<MedievalBurnable>();
			var near = Water ? ApproachFire(self, fire) : Approach(self, Well);
			if (!near) { Phase = Water ? 2 : 0; Progress = 0; return; }
			self.CancelActivity(); Phase = Water ? 3 : 1;
			var direction = (Water ? fire.Origin : Well.CenterPosition) - self.CenterPosition;
			if (direction.HorizontalLengthSquared > 0) self.Trait<Mobile>().Facing = direction.Yaw;
			Progress += 5 * (self.TraitOrDefault<MedievalNeedsFood>()?.WorkPercent ?? 100);
			if (Progress < 100 * (Water ? FirefightingRules.PourTicks : FirefightingRules.FillTicks)) return;
			Progress = stalled = 0;
			if (!Water) { Water = true; Phase = 2; return; }
			fire.Douse(Fire); Buckets++; Water = false; Phase = 0;
			if (!fire.Burning) Cancel(self);
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "MedievalWork"
				or "MedievalGatherAnimal" or "MedievalAssignSiege" or "MedievalRest" or "MedievalStartPatrol" or "MedievalCommanderRest")
			{
				var busy = Busy; Cancel(self, false, false);
				if (busy || order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter") cooldown = 750;
			}
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Cancel(self, false); }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Cancel(self, false); cooldown = 25; }
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Busy || Phase != 3 || self.World.FogObscures(self) || !MedievalStockpile.Active(Fire)) yield break;
			var from = self.CenterPosition + new WVec(0, 0, 570); var to = Fire.Trait<MedievalBurnable>().Origin + new WVec(0, 0, 160);
			WPos Point(int n) => WPos.Lerp(from, to, n, 12) + new WVec(0, 0, 9 * n * (12 - n));
			for (var n = 0; n < 12; n++)
				if ((n + self.World.WorldTick / 2) % 3 != 0) yield return new LineAnnotationRenderable(Point(n), Point(n + 1), 1, Color.FromArgb(220, 152, 218, 242));
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
}
