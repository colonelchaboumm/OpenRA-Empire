using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalUndertakerInfo : TraitInfo, Requires<MobileInfo>, Requires<MedievalRestInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalUndertaker();
	}
	public sealed class MedievalUndertaker : ITick, ISync, IIssueOrder, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, IRenderModifier, IRenderAnnotations
	{
		public const int PickupTicks = 30, BurialTicks = 75;
		public Actor Church { get; private set; }
		public BattlefieldRemains Body { get; private set; }
		public CemeteryPlot Plot { get; private set; }
		[VerifySync] public bool Automatic { get; private set; } = true;
		[VerifySync] public bool Carrying { get; private set; }
		[VerifySync] public int Progress { get; private set; }
		[VerifySync] public int Burials { get; private set; }
		[VerifySync] public int ChurchId => (int)(Church?.ActorID ?? 0);
		[VerifySync] public int BodyId => Body?.Seed ?? -1;
		[VerifySync] public CPos GraveCell => Plot?.Cell ?? CPos.Zero;
		[VerifySync] int cadence, stalled, avoidBody = -1, avoidUntil;
		[VerifySync] WPos previous;
		[VerifySync] CPos destination;
		public bool Busy => Body != null;
		public bool Working => Progress > 0;
		public string Description(Actor self) => !Automatic ? "Arrete : clic droit sur une eglise pour reprendre."
			: self.Trait<MedievalRest>().Resting ? self.Trait<MedievalRest>().Description
			: Working ? Carrying ? "Enterre le defunt" : "Recueille un defunt"
			: Carrying ? "Porte un corps au cimetiere" : Busy ? "Va chercher un corps"
			: !MedievalChurch.Churches(self.Owner).Any() ? "Attend une eglise terminee"
			: !self.World.WorldActor.Trait<MedievalCemeteries>().Plots.Any(p => p.Owner == self.Owner && p.Free) ? "Attend des places libres au cimetiere"
			: "Attend un corps visible et accessible";
		public void Assign(Actor self, Actor church)
		{
			if (!MedievalStockpile.Active(church) || church.Owner != self.Owner || church.TraitOrDefault<MedievalChurch>() == null) return;
			Church = church; Automatic = true; cadence = 0;
		}
		public void Cancel(Actor self, bool cancelActivity = true)
		{
			if (Busy && cancelActivity) self.CancelActivity();
			if (Body != null && Body.ClaimedBy == self.ActorID)
			{
				if (Carrying) { Body.Position = self.CenterPosition; Body.Age = BattlefieldMotion.FallTicks; }
				Body.ClaimedBy = Body.Carrier = 0;
			}
			Plot?.Release(self.ActorID);
			Body = null; Plot = null; Carrying = false; Progress = stalled = 0;
		}
		static CPos? AccessPoint(Actor self, WPos position)
		{
			var mobile = self.Trait<Mobile>(); var map = self.World.Map;
			return map.FindTilesInCircle(map.CellContaining(position), 1)
				.Where(c => mobile.CanEnterCell(c, check: BlockedByActor.Immovable)
					&& (map.CenterOfCell(c) - position).HorizontalLengthSquared <= 1250L * 1250
					&& mobile.PathFinder.PathExistsForLocomotor(mobile.Locomotor, self.Location, c))
				// Infantry can stand off-centre in a shared cell. Prefer the target cell
				// so an adjacent subcell cannot keep returning an already-finished move.
				.OrderBy(c => (map.CenterOfCell(c) - position).HorizontalLengthSquared)
				.ThenBy(c => (c - self.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).Cast<CPos?>().FirstOrDefault();
		}
		void FindJob(Actor self)
		{
			var cemetery = self.World.WorldActor.Trait<MedievalCemeteries>();
			var plot = cemetery.Plots.Where(p => p.Owner == self.Owner && p.Free)
				.OrderBy(p => (p.Cell - self.Location).LengthSquared).ThenBy(p => p.Cell.Y).ThenBy(p => p.Cell.X)
				.FirstOrDefault(p => AccessPoint(self, self.World.Map.CenterOfCell(p.Cell)) != null);
			if (plot == null) return;
			foreach (var body in self.World.WorldActor.Trait<MedievalBattlefield>().Remains
				.Where(b => !b.Building && b.Settled && b.ClaimedBy == 0 && (b.Seed != avoidBody || self.World.WorldTick >= avoidUntil)
					&& self.Owner.Shroud.IsVisible(b.Position))
				.OrderBy(b => (b.Position - self.CenterPosition).HorizontalLengthSquared).ThenBy(b => b.Seed))
			{
				var access = AccessPoint(self, body.Position);
				if (access == null || !plot.Reserve(self.ActorID)) continue;
				Body = body; Plot = plot; body.ClaimedBy = self.ActorID;
				destination = access.Value; previous = self.CenterPosition; Progress = stalled = 0; return;
			}
		}
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self)) return;
			if (!Automatic || self.Trait<MedievalRest>().Resting || self.TraitOrDefault<MedievalPatient>()?.Church != null)
			{ Cancel(self, false); return; }
			if (--cadence > 0) return; cadence = Busy ? 5 : 25;
			if (!MedievalStockpile.Active(Church) || Church.Owner != self.Owner)
				Church = MedievalChurch.Churches(self.Owner).OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
			if (Church == null) { Cancel(self); return; }
			if (!Busy) { if (self.IsIdle) FindJob(self); return; }
			if (Body.ClaimedBy != self.ActorID || Plot.ReservedBy != self.ActorID || Plot.Owner != self.Owner || Plot.Buried)
			{ Cancel(self); return; }
			if (!Carrying && !self.Owner.Shroud.IsVisible(Body.Position)) { Cancel(self); return; }
			if (self.CenterPosition == previous) stalled += 5; else { stalled = 0; previous = self.CenterPosition; }
			if (stalled > 375) { avoidBody = Body.Seed; avoidUntil = self.World.WorldTick + 750; Cancel(self); return; }
			var target = Carrying ? self.World.Map.CenterOfCell(Plot.Cell) : Body.Position;
			if ((target - self.CenterPosition).HorizontalLengthSquared > 1250L * 1250)
			{
				Progress = 0;
				if (self.IsIdle)
				{
					var access = AccessPoint(self, target);
					if (access == null) { avoidBody = Body.Seed; avoidUntil = self.World.WorldTick + 750; Cancel(self); return; }
					destination = access.Value; self.QueueActivity(self.Trait<Mobile>().MoveTo(destination, 0));
				}
				return;
			}
			self.CancelActivity();
			var facing = target - self.CenterPosition;
			if (facing.HorizontalLengthSquared > 0) self.Trait<Mobile>().Facing = facing.Yaw;
			Progress += 5 * (self.TraitOrDefault<MedievalNeedsFood>()?.WorkPercent ?? 100);
			if (Progress < 100 * (Carrying ? BurialTicks : PickupTicks)) return;
			Progress = stalled = 0;
			if (!Carrying) { Carrying = true; Body.Carrier = self.ActorID; return; }
			if (Plot.Bury(self.ActorID, Body.Seed))
			{
				self.World.WorldActor.Trait<MedievalBattlefield>().Bury(Body); Burials++;
				Body = null; Plot = null; Carrying = false;
			}
		}
		sealed class WorkTargeter : UnitOrderTargeter
		{
			public WorkTargeter() : base("MedievalUndertakerWork", 6, "enter", false, true) { }
			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor) => target.Owner == self.Owner
				&& MedievalStockpile.Active(target) && target.TraitOrDefault<MedievalChurch>() != null;
			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor) => false;
		}
		IEnumerable<IOrderTargeter> IIssueOrder.Orders { get { yield return new WorkTargeter(); } }
		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) => new("MedievalUndertakerWork", self, target, false);
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalUndertakerWork" && order.Target.Type == TargetType.Actor
				&& order.Target.Actor.Owner == self.Owner && MedievalStockpile.Active(order.Target.Actor) && order.Target.Actor.TraitOrDefault<MedievalChurch>() != null)
			{
				if (!self.Trait<MedievalRest>().Wake(self, true, true)) return;
				self.TraitOrDefault<MedievalPatient>()?.Cancel(self);
				Cancel(self); self.CancelActivity(); Assign(self, order.Target.Actor);
			}
			else if (order.OrderString is "Move" or "Stop" or "AttackMove" or "AssaultMove" or "Scatter" or "EnterTransport")
			{ Cancel(self, false); Automatic = false; }
			else if (order.OrderString is "MedievalRest" or "MedievalChurchCare") Cancel(self, false);
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Cancel(self); }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Cancel(self); Church = null; }
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var r in renderables) yield return r is SpriteRenderable s ? s.WithTint(s.Tint * new Vector3(.52f, .53f, .58f), s.TintModifiers) : r;
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => bounds;
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Carrying || Body?.Sprite == null || self.World.FogObscures(self) || self.Trait<MedievalRest>().Sleeping) yield break;
			yield return new SpriteRenderable(Body.Sprite, self.CenterPosition + new WVec(0, 0, 600), WVec.Zero, 1, wr.Palette(Body.Palette), Body.Scale * .85f,
				1, new Vector3(.7f, .69f, .66f), TintModifiers.None, true).WithScreenTransform(BattlefieldMotion.Fall(BattlefieldMotion.FallTicks, Body.Seed));
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
}
