using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSiegeCaptureInfo : CapturesInfo, Requires<MedievalSiegeCrewInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeCapture(init.Self, this);
	}

	// Use the engine's capture types, progress bars and synchronized orders. Only the
	// approach differs: a deployed trebuchet must be reached outside its 3 x 3 supports.
	public sealed class MedievalSiegeCapture : Captures, IIssueOrder, IResolveOrder, ICaptureProgressWatcher
	{
		public int CapturePercent { get; private set; }
		public bool Capturing { get; private set; }
		public MedievalSiegeCapture(Actor self, MedievalSiegeCaptureInfo info) : base(self, info) { }
		void ICaptureProgressWatcher.Update(Actor self, Actor captor, Actor target, int progress, int total)
		{ Capturing = total > 0; CapturePercent = total > 0 ? progress * 100 / total : 0; }
		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get { if (!IsTraitDisabled) yield return new SiegeCaptureTargeter(this); }
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "CaptureActor" || IsTraitDisabled || order.Target.Type != TargetType.Actor
				|| order.Target.Actor.TraitOrDefault<MedievalSiegeEngine>() == null
				|| order.Target.Actor.TraitOrDefault<CaptureManager>() is not CaptureManager manager || !CaptureManager.CanTarget(manager)) return;
			self.QueueActivity(order.Queued, new CaptureSiege(self, order.Target.Actor, this));
			self.ShowTargetLines();
		}
		sealed class SiegeCaptureTargeter : UnitOrderTargeter
		{
			readonly MedievalSiegeCapture capture;
			public SiegeCaptureTargeter(MedievalSiegeCapture capture) : base("CaptureActor", 7, capture.Info.EnterCursor, true, false)
			{ this.capture = capture; ForceAttack = false; }
			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor) =>
				!modifiers.HasModifier(TargetModifiers.ForceMove) && !target.IsDead
				&& target.TraitOrDefault<MedievalSiegeEngine>() != null
				&& target.TraitOrDefault<CaptureManager>() is CaptureManager manager && capture.CaptureManager.CanTarget(manager);
			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor) => false;
		}
	}

	sealed class CaptureSiege : Activity
	{
		readonly Actor target;
		readonly Player originalOwner, captorOwner;
		readonly MedievalSiegeCapture capture;
		readonly CaptureManager manager, targetManager;
		readonly Mobile mobile;
		int attempts, retry;
		public CaptureSiege(Actor self, Actor target, MedievalSiegeCapture capture)
		{
			this.target = target; this.capture = capture;
			originalOwner = target.Owner; captorOwner = self.Owner;
			manager = capture.CaptureManager; targetManager = target.Trait<CaptureManager>();
			mobile = self.Trait<Mobile>(); ChildHasPriority = false;
		}
		public override bool Tick(Actor self)
		{
			var current = Target.FromActor(target).Recalculate(self.Owner, out var hidden);
			if (self.Owner != captorOwner || target.IsDead || !target.IsInWorld || hidden || !current.IsValidFor(self) || capture.IsTraitDisabled)
				Cancel(self);
			if (!IsCanceling && target.Owner == self.Owner)
			{
				// Other soldiers in the same capture order can join the newly acquired crew.
				Cancel(self, true);
				self.World.AddFrameEndTask(_ => { if (SiegeRules.Live(self, captorOwner) && SiegeRules.Live(target, captorOwner)) self.Trait<MedievalSiegeCrew>().Assign(self, target); });
			}
			else if (target.Owner != originalOwner || !manager.CanTarget(targetManager)) Cancel(self);
			if (!TickChild(self)) return false;
			if (IsCanceling) return true;
			// Queued captures leave care only when their valid activity actually starts.
			self.TraitOrDefault<MedievalPatient>()?.Cancel(self);
			if (!MedievalAccess.Adjacent(self, target))
			{
				manager.CancelCapture(target, targetManager);
				if (--retry > 0) return false;
				retry = 15;
				var cells = MedievalAccess.Perimeter(target).Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.Immovable))
					.OrderBy(c => (c - self.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).ToArray();
				if (cells.Length > 0) QueueChild(mobile.MoveTo(cells[attempts++ % cells.Length], 0));
				return false;
			}
			mobile.Facing = (target.CenterPosition - self.CenterPosition).Yaw;
			if (!manager.StartCapture(targetManager, out _)) return false;
			self.World.AddFrameEndTask(_ =>
			{
				// Revalidate both parties at transfer time, including simultaneous captures.
				if (!SiegeRules.Live(self, captorOwner) || !SiegeRules.Live(target, originalOwner)
					|| !MedievalAccess.Adjacent(self, target) || !manager.CanTarget(targetManager)) return;
				target.ChangeOwnerSync(captorOwner);
				foreach (var t in target.TraitsImplementing<INotifyCapture>())
					t.OnCapture(target, self, originalOwner, captorOwner, capture.Info.CaptureTypes);
				self.Trait<MedievalSiegeCrew>().Assign(self, target);
			});
			return true;
		}
		protected override void OnLastRun(Actor self) { manager.CancelCapture(target, targetManager); }
		protected override void OnActorDispose(Actor self) { manager.CancelCapture(target, targetManager); }
		public override void Cancel(Actor self, bool keepQueue = false)
		{ manager.CancelCapture(target, targetManager); base.Cancel(self, keepQueue); }
		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{ yield return new TargetLineNode(Target.FromActor(target), capture.Info.TargetLineColor); }
	}
}
