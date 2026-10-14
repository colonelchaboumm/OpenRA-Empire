using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalEscortInfo : TraitInfo, Requires<MobileInfo>, Requires<AutoTargetInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalEscort();
	}
	public sealed class MedievalEscort : IResolveOrder, INotifyOwnerChanged, INotifyRemovedFromWorld, ISync
	{
		public Actor Protected { get; private set; }
		[VerifySync] public int Mission { get; private set; }
		[VerifySync] public int ProtectedId => (int)(Protected?.ActorID ?? 0);
		public static bool FriendlyUnit(Actor self, Actor target) => MedievalStockpile.Active(target) && !target.WillDispose
			&& target != self && self.Owner.IsAlliedWith(target.Owner) && target.TraitOrDefault<Mobile>() != null;
		public static bool CanEscort(Actor self, Actor target)
		{
			if (!FriendlyUnit(self, target) || !target.CanBeViewedByPlayer(self.Owner)) return false;
			// Refuse circular follow chains, including indirect loops between several guards.
			var seen = new HashSet<Actor> { self };
			for (var a = target; MedievalStockpile.Active(a); a = a.TraitOrDefault<MedievalEscort>()?.Protected)
				if (!seen.Add(a)) return false;
			return true;
		}
		public void Clear(int mission) { if (Mission == mission) Protected = null; }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Mission++; Protected = null; self.CancelActivity(); }
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Mission++; Protected = null; }
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (!order.Queued && order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter"
				or "CaptureActor" or "MedievalStartPatrol" or "MedievalAssignSiege" or "MedievalGatherAnimal" or "MedievalCommanderDeploy"
				or "MedievalCommanderRest" or "MedievalCommanderWake" or "MedievalSabotageFire" or "MedievalSabotageKill" or "EnterTransport")
			{ Mission++; Protected = null; }
			if (order.OrderString == "MedievalStopEscort") { Mission++; Protected = null; self.CancelActivity(); return; }
			if (order.OrderString != "MedievalEscort" || order.Target.Type != TargetType.Actor || !CanEscort(self, order.Target.Actor)) return;
			if (self.TraitOrDefault<MedievalRest>() is { } rest && !rest.Wake(self, true, true)) return;
			self.TraitOrDefault<MedievalFirefighter>()?.Cancel(self, false);
			self.TraitOrDefault<MedievalPatrol>()?.Stop(self);
			self.TraitOrDefault<MedievalSiegeCrew>()?.Release(self);
			self.TraitOrDefault<MedievalHerding>()?.Cancel(self);
			self.TraitOrDefault<MedievalSaboteur>()?.Cancel(self);
			self.TraitOrDefault<MedievalCommander>()?.Cancel(self);
			Mission++; Protected = order.Target.Actor;
			self.QueueActivity(false, new EscortActivity(self, this, Protected)); self.ShowTargetLines();
		}
	}
	sealed class EscortActivity : Activity, IActivityNotifyStanceChanged
	{
		readonly MedievalEscort escort;
		readonly Actor target;
		readonly Player owner;
		readonly int mission;
		readonly AutoTarget autoTarget;
		readonly Mobile mobile;
		bool moving;
		public EscortActivity(Actor self, MedievalEscort escort, Actor target)
		{ this.escort = escort; this.target = target; owner = self.Owner; mission = escort.Mission; autoTarget = self.Trait<AutoTarget>(); mobile = self.Trait<Mobile>(); ChildHasPriority = false; }
		public override bool Tick(Actor self)
		{
			if (IsCanceling || escort.Mission != mission || escort.Protected != target || self.Owner != owner || !MedievalEscort.FriendlyUnit(self, target))
			{ ChildActivity?.Cancel(self); return TickChild(self); }
			var far = (self.CenterPosition - target.CenterPosition).HorizontalLengthSquared > 6144L * 6144;
			if (far && !moving) { ChildActivity?.Cancel(self); moving = true; }
			if (!far && (ChildActivity == null || moving) && !autoTarget.IsTraitDisabled && autoTarget.Stance >= UnitStance.Defend)
			{
				var enemy = autoTarget.ScanForTarget(self, autoTarget.AllowMove, true);
				if (enemy.Type != TargetType.Invalid)
				{
					ChildActivity?.Cancel(self); moving = false;
					foreach (var attack in autoTarget.ActiveAttackBases)
						QueueChild(attack.GetAttackActivity(self, AttackSource.AutoTarget, enemy, autoTarget.AllowMove, false));
				}
			}
			if (ChildActivity == null)
			{ moving = true; QueueChild(mobile.MoveFollow(self, Target.FromActor(target), WDist.Zero, new WDist(2048), targetLineColor: Color.Gold)); }
			TickChild(self); return false;
		}
		protected override void OnLastRun(Actor self) => escort.Clear(mission);
		void IActivityNotifyStanceChanged.StanceChanged(Actor self, AutoTarget trait, UnitStance oldStance, UnitStance newStance)
		{ if (newStance < oldStance) { ChildActivity?.Cancel(self); moving = true; } }
		public override IEnumerable<Target> GetTargets(Actor self) => [Target.FromActor(target)];
		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{ yield return new TargetLineNode(Target.FromActor(target), Color.Gold); }
	}
	public sealed class MedievalEscortOrderGenerator : OrderGenerator
	{
		readonly Actor[] units;
		readonly WorldRenderer renderer;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalEscortOrderGenerator(World w, WorldRenderer wr, Actor[] units) : base(w) { this.units = units; renderer = wr; }
		IEnumerable<Actor> Live(World w) => units.Where(a => MedievalStockpile.Active(a) && a.Owner == w.LocalPlayer);
		Actor At(World w, CPos cell) => w.ActorMap.GetActorsAt(cell).FirstOrDefault(a => Live(w).Any(u => MedievalEscort.CanEscort(u, a)));
		protected override IEnumerable<Order> OrderInner(World w, CPos cell, int2 pixel, MouseInput mi)
		{
			if (w.Paused || At(w, cell) is not Actor target) yield break;
			foreach (var a in Live(w).Where(a => MedievalEscort.CanEscort(a, target))) yield return new Order("MedievalEscort", a, Target.FromActor(target), false);
			w.CancelInputMode();
		}
		protected override void Tick(World w) { if (!Live(w).Any()) w.CancelInputMode(); }
		protected override void SelectionChanged(World w, IEnumerable<Actor> selected) { if (!selected.Any(units.Contains)) w.CancelInputMode(); }
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World w)
		{
			if (At(w, renderer.Viewport.ViewToWorld(Viewport.LastMousePos)) is not Actor target) yield break;
			foreach (var line in MedievalPlaceOrderGenerator.CellOutline(w, target.Location, Color.Gold)) yield return line;
		}
		protected override string GetCursor(World w, CPos cell, int2 pixel, MouseInput mi) => At(w, cell) != null ? "guard" : "move-blocked";
	}
}
