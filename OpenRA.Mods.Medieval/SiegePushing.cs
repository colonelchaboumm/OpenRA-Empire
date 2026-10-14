using System;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class SiegePushMotion
	{
		// Feet sit one arm's reach behind the model's crossbars and wagon handles.
		public static (int Forward, int Side, bool Diagonal) Post(bool trebuchet, bool cauldron, int slot)
		{
			var side = slot % 2 == 0 ? -1 : 1;
			return cauldron ? (-50, side * 13, false)
				: trebuchet ? slot < 2 ? (-61, side * 15, false) : (-33, side * 23, true)
				: (slot < 2 ? -28 : 16, side * 23, true);
		}
		// The cauldron sheet was drawn in diagonal tile axes, clockwise from southeast.
		public static WAngle SpriteFacing(bool cauldron, WAngle facing) => cauldron ? new WAngle(640 - facing.Angle) : facing;
	}

	public sealed partial class MedievalSiegeCrew
	{
		[VerifySync] public bool Pushing { get; internal set; }
		internal bool CanPush => SiegeRules.Live(engine, engine?.Owner) && (!Trebuchet || engine.Trait<MedievalSiegeEngine>() is { Packed: true, Busy: false });
		public WPos PushPosition(Actor self)
		{
			var siege = engine.Trait<MedievalSiegeEngine>();
			var slot = Array.IndexOf(siege.Crew.ToArray(), self);
			var post = SiegePushMotion.Post(siege.Info.Trebuchet, siege.Info.Cauldron, Math.Max(0, slot));
			return ModelPoint(engine, post.Forward, post.Side);
		}
		internal WAngle PushFacing(Actor self)
		{
			var siege = engine.Trait<MedievalSiegeEngine>();
			var slot = Array.IndexOf(siege.Crew.ToArray(), self);
			var post = SiegePushMotion.Post(siege.Info.Trebuchet, siege.Info.Cauldron, Math.Max(0, slot));
			return (ModelPoint(engine, post.Forward + 8, post.Side - (post.Diagonal ? Math.Sign(post.Side) * 8 : 0)) - PushPosition(self)).Yaw;
		}
	}

	// Normal pathfinding reaches the machine; only short, collision-checked steps
	// align the real actor with its moving handhold. No cosmetic duplicate or teleport.
	sealed class SiegePush : Activity
	{
		readonly MedievalSiegeCrew crew;
		readonly Actor engine;
		readonly Mobile mobile;
		WPos pathTarget;
		bool returning;
		int retry;
		public SiegePush(Actor self, MedievalSiegeCrew crew, Actor engine)
		{ this.crew = crew; this.engine = engine; mobile = self.Trait<Mobile>(); ChildHasPriority = false; }
		public override bool Tick(Actor self)
		{
			if (IsCanceling || !crew.Serves(engine) || !SiegeRules.Live(engine, self.Owner) || !crew.CanPush || self.TraitOrDefault<MedievalRest>()?.Resting == true)
			{
				if (!IsCanceling) Cancel(self, true);
				crew.Pushing = false;
				if (!TickChild(self)) return false;
				if (!returning)
				{
					returning = true;
					QueueChild(mobile.ReturnToCell(self));
					return false;
				}
				return true;
			}
			var point = crew.PushPosition(self);
			if (ChildActivity != null && (point - pathTarget).HorizontalLength > 724) ChildActivity.Cancel(self);
			if (!TickChild(self)) { crew.Pushing = false; return false; }
			if (mobile.IsTraitDisabled || mobile.IsTraitPaused) { crew.Pushing = false; return false; }
			var delta = point - self.CenterPosition;
			var distance = delta.HorizontalLength;
			var speed = mobile.MovementSpeedForCell(self.Location);
			if (distance <= 1150 && speed > 0)
			{
				var next = distance <= speed ? point : self.CenterPosition + delta * speed / distance;
				var cell = self.World.Map.CellContaining(next);
				var sub = self.World.Map.Contains(cell) && mobile.CanExistInCell(cell)
					? self.World.ActorMap.FreeSubCell(cell, mobile.ToSubCell, a => a != self && a != engine) : SubCell.Invalid;
				if (sub != SubCell.Invalid && mobile.CanStayInCell(cell)
					&& Math.Abs(self.World.Map.Height[cell] - self.World.Map.Height[self.Location]) <= 1)
				{
					mobile.SetLocation(cell, sub, cell, sub);
					next -= new WVec(0, 0, self.World.Map.DistanceAboveTerrain(next).Length);
					mobile.SetCenterPosition(self, next);
					crew.Pushing = (point - next).HorizontalLength <= 160;
					mobile.Facing = Util.TickFacing(mobile.Facing, crew.Pushing ? crew.PushFacing(self) : delta.Yaw, mobile.TurnSpeed);
					return false;
				}
			}
			crew.Pushing = false;
			if (--retry > 0) return false;
			retry = 15;
			var destination = self.World.Map.FindTilesInCircle(self.World.Map.CellContaining(point), 2)
				.Where(c => c != engine.Location && mobile.CanEnterCell(c, check: BlockedByActor.Immovable) && mobile.CanStayInCell(c))
				.OrderBy(c => (self.World.Map.CenterOfCell(c) - point).HorizontalLengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X)
				.Cast<CPos?>().FirstOrDefault();
			if (destination != null)
			{
				pathTarget = point;
				QueueChild(mobile.ReturnToCell(self));
				QueueChild(mobile.MoveTo(destination.Value));
			}
			return false;
		}
		protected override void OnLastRun(Actor self) { crew.Pushing = false; }
		protected override void OnActorDispose(Actor self) { crew.Pushing = false; }
	}
}
