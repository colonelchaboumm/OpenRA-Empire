using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class PatrolRoute
	{
		public const int MaximumPoints = 16;
		public static string Encode(IEnumerable<CPos> points) => string.Join(";", points.Select(p => FormattableString.Invariant($"{p.X},{p.Y}")));
		public static bool TryDecode(string text, Func<CPos, bool> valid, out CPos[] points)
		{
			points = [];
			if (string.IsNullOrEmpty(text) || text.Length > 512) return false;
			var entries = text.Split(';');
			if (entries.Length < 2 || entries.Length > MaximumPoints) return false;
			var result = new List<CPos>();
			foreach (var entry in entries)
			{
				var xy = entry.Split(',');
				if (xy.Length != 2 || !int.TryParse(xy[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
					|| !int.TryParse(xy[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)) return false;
				// CPos uses packed coordinates. Reject values that would wrap on construction.
				var cell = new CPos(x, y);
				if (cell.X != x || cell.Y != y || !valid(cell)) return false;
				if (result.Count == 0 || result[^1] != cell) result.Add(cell);
			}
			if (result.Distinct().Count() < 2) return false;
			points = result.ToArray(); return true;
		}
	}
	public class MedievalPatrolInfo : TraitInfo, Requires<MobileInfo>, Requires<AutoTargetInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalPatrol();
	}
	public sealed class MedievalPatrol : ITick, IResolveOrder, INotifyOwnerChanged, INotifyRemovedFromWorld, ISync, IRenderAnnotations
	{
		CPos[] points = [];
		public IReadOnlyList<CPos> Points => points;
		public bool HasRoute => points.Length >= 2;
		[VerifySync] public bool NightOnly { get; private set; } = true;
		[VerifySync] int wakeRetry;
		[VerifySync] public bool Active { get; private set; }
		[VerifySync] public int Index { get; private set; }
		[VerifySync] public int Visits { get; private set; }
		[VerifySync] public int Skipped { get; private set; }
		[VerifySync] public int RouteHash => points.Aggregate(17, (h, p) => unchecked((h * 31 + p.X) * 31 + p.Y));
		public string Description => !HasRoute ? "Aucune ronde" : !Active ? "Ronde memorisee : attend le crepuscule"
			: $"Ronde {(NightOnly ? "nocturne" : "continue")} : point {Index + 1}/{points.Length}" + (Skipped > 0 ? " | passage bloque contourne" : "");
		bool Due(Actor self) => HasRoute && (!NightOnly || self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.NightWatch == true);
		public void Stop(Actor self, bool cancel = true)
		{
			Active = false; points = []; Index = wakeRetry = 0;
			if (cancel) self.CancelActivity();
		}
		public void Advance(Actor self)
		{
			if ((self.Location - points[Index]).LengthSquared <= 2) Visits++; else Skipped++;
			Index = (Index + 1) % points.Length;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalStartPatrol")
			{
				if (!PatrolRoute.TryDecode(order.TargetString, c => self.World.Map.Contains(c) && self.Owner.Shroud.IsExplored(c)
					&& self.Trait<Mobile>().CanEnterCell(c, check: BlockedByActor.Immovable), out var route)) return;
				var rest = self.TraitOrDefault<MedievalRest>();
				if (!NightOnly && rest != null && !rest.Wake(self, true)) return;
				self.TraitOrDefault<MedievalSiegeCrew>()?.Release(self);
				if (rest?.Resting != true) self.CancelActivity();
				points = route; Index = Visits = Skipped = wakeRetry = 0; Active = Due(self);
			}
			else if (order.OrderString == "MedievalPatrolNightOnly" && order.ExtraData <= 1) NightOnly = order.ExtraData == 1;
			else if (order.OrderString == "MedievalStopPatrol") Stop(self);
			else if (order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "CaptureActor" or "MedievalAssignSiege") Stop(self, false);
		}
		void ITick.Tick(Actor self)
		{
			if (self.TraitOrDefault<MedievalFirefighter>()?.Busy == true) return;
			var due = Due(self);
			if (Active != due)
			{
				Active = due; wakeRetry = 0;
				// Keep both the circuit and any explicit rest or other non-patrol activity.
				if (!Active && self.CurrentActivity is MilitiaRound) self.CancelActivity();
			}
			if (Active && self.TraitOrDefault<MedievalRest>() is { Resting: true, OrderedMilitaryRest: false } rest && --wakeRetry <= 0)
			{
				wakeRetry = 25;
				if (rest.Wake(self, true)) self.CancelActivity();
			}
			if (Active && self.IsIdle && self.TraitOrDefault<MedievalRest>()?.Resting != true && self.TraitOrDefault<MedievalSiegeCrew>()?.Assigned != true)
				self.QueueActivity(new MilitiaRound(self, this));
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Stop(self); }
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Stop(self); }
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!HasRoute || self.Owner != self.World.LocalPlayer || !self.World.Selection.Contains(self)) yield break;
			var color = Active ? Color.LightGreen : Color.Gray;
			foreach (var line in MedievalPatrolOrderGenerator.RouteLines(self.World, points, color)) yield return line;
			foreach (var p in points)
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(self.World, p, Active && p == points[Index] ? Color.Gold : color)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
	// A persistent activity lets combat interrupt a leg and then resume the same circuit.
	sealed class MilitiaRound : Activity, IActivityNotifyStanceChanged
	{
		readonly MedievalPatrol patrol;
		readonly AutoTarget autoTarget;
		readonly Mobile mobile;
		bool moving;
		public MilitiaRound(Actor self, MedievalPatrol patrol)
		{
			this.patrol = patrol; autoTarget = self.Trait<AutoTarget>(); mobile = self.Trait<Mobile>(); ChildHasPriority = false;
		}
		public override bool Tick(Actor self)
		{
			if (IsCanceling || !patrol.Active) return TickChild(self);
			if ((ChildActivity == null || moving) && !autoTarget.IsTraitDisabled && autoTarget.Stance >= UnitStance.ReturnFire)
			{
				var attacker = self.TraitOrDefault<MedievalThreat>()?.RecentAttacker(self);
				if (attacker != null && ((attacker.CenterPosition - self.CenterPosition).HorizontalLengthSquared > 8192L * 8192
					|| !autoTarget.ActiveAttackBases.Any(a => a.HasAnyValidWeapons(Target.FromActor(attacker))))) attacker = null;
				var target = attacker != null ? Target.FromActor(attacker) : autoTarget.Stance >= UnitStance.Defend ? autoTarget.ScanForTarget(self, autoTarget.AllowMove, true) : Target.Invalid;
				if (target.Type != TargetType.Invalid)
				{
					ChildActivity?.Cancel(self); moving = false;
					if (attacker != null) QueueChild(new Retaliation(self, attacker, new WDist(8192), returnToOrigin: false));
					else foreach (var attack in autoTarget.ActiveAttackBases)
						QueueChild(attack.GetAttackActivity(self, AttackSource.AutoTarget, target, autoTarget.AllowMove, false));
				}
			}
			if (ChildActivity == null)
			{
				moving = true;
				QueueChild(mobile.MoveTo(patrol.Points[patrol.Index], 1, targetLineColor: Color.LightGreen));
			}
			if (TickChild(self) && moving)
			{
				patrol.Advance(self); moving = false; QueueChild(new Wait(20));
			}
			return false;
		}
		void IActivityNotifyStanceChanged.StanceChanged(Actor self, AutoTarget trait, UnitStance oldStance, UnitStance newStance)
		{
			// Stop a pursuit immediately when switching to a more restrained attitude.
			if (newStance < oldStance) { ChildActivity?.Cancel(self); moving = false; }
		}
	}
}
