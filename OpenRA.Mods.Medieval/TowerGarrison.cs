using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalTowerArcherInfo : TraitInfo, Requires<PassengerInfo>
	{
		[GrantedConditionReference] public readonly string ClimbingCondition = "tower-climbing";
		public readonly int ClimbTicks = 30;
		public override object Create(ActorInitializer init) => new MedievalTowerArcher(init.Self, this);
	}

	// Passenger actors keep ticking, but are removed from the ground actor map.
	// Their real weapons, hunger, morale and fatigue remain attached to the same actor.
	public sealed class MedievalTowerArcher : ITick, INotifyEnteredCargo, INotifyExitedCargo, ISync
	{
		readonly MedievalTowerArcherInfo info;
		readonly Passenger passenger;
		[VerifySync] int climbing, token = Actor.InvalidConditionToken;
		[VerifySync] public int TowerId => (int)(passenger.Transport?.ActorID ?? 0);
		public bool Climbing => climbing > 0;
		public MedievalTowerArcher(Actor self, MedievalTowerArcherInfo info)
		{ this.info = info; passenger = self.Trait<Passenger>(); }
		public static bool Present(Actor a) => !a.IsDead && !a.Disposed && (a.IsInWorld
			|| a.TraitOrDefault<Passenger>()?.Transport is { IsInWorld: true, IsDead: false, Disposed: false } tower
			&& tower.TraitOrDefault<MedievalTowerGarrison>() != null);

		void INotifyEnteredCargo.OnEnteredCargo(Actor self, Actor tower)
		{
			climbing = info.ClimbTicks;
			token = self.GrantCondition(info.ClimbingCondition);
			Position(self, tower);
		}
		void INotifyExitedCargo.OnExitedCargo(Actor self, Actor tower)
		{
			climbing = 0;
			if (token != Actor.InvalidConditionToken) token = self.RevokeCondition(token);
		}
		void Position(Actor self, Actor tower)
		{
			var slot = tower.Trait<Cargo>().Passengers.TakeWhile(a => a != self).Count();
			var height = 4400 * (info.ClimbTicks - climbing) / System.Math.Max(1, info.ClimbTicks);
			self.Trait<IPositionable>().SetCenterPosition(self, tower.CenterPosition + new WVec(slot == 0 ? -340 : 340, 230, height));
		}
		void ITick.Tick(Actor self)
		{
			if (passenger.Transport is not { IsInWorld: true, IsDead: false, Disposed: false } tower || self.IsDead) return;
			if (climbing > 0 && --climbing == 0) token = self.RevokeCondition(token);
			Position(self, tower);
		}
	}

	public class MedievalTowerGarrisonInfo : TraitInfo, Requires<CargoInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalTowerGarrison(init.Self);
	}
	public sealed class MedievalTowerGarrison : IRender, ISync
	{
		readonly Cargo cargo;
		public MedievalTowerGarrison(Actor self) { cargo = self.Trait<Cargo>(); }
		[VerifySync] public int OccupantsHash => cargo.Passengers.Aggregate(0, (h, a) => unchecked(h * 31 + (int)a.ActorID));
		public string Description => $"Tour : {cargo.PassengerCount}/{cargo.Info.MaxWeight} archers | Portee +50% | Vision {(cargo.IsEmpty() ? 9 : 12)} cases";
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			foreach (var archer in cargo.Passengers)
				foreach (var renderable in archer.Render(wr)) yield return renderable;
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr) { yield break; }
	}

	public class MedievalTowerAttackInfo : AttackGarrisonedInfo
	{
		public override object Create(ActorInitializer init) => new MedievalTowerAttack(init.Self, this);
	}
	public sealed class MedievalTowerAttack : AttackGarrisoned
	{
		public MedievalTowerAttack(Actor self, MedievalTowerAttackInfo info) : base(self, info) { }
		public override void DoAttack(Actor self, in Target target)
		{
			if (!CanAttack(self, target)) return;
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled || armament.Actor.Trait<MedievalTowerArcher>().Climbing) continue;
				var archer = armament.Actor;
				var direction = archer.Trait<IFacing>();
				direction.Facing = (target.CenterPosition - archer.CenterPosition).Yaw;
				armament.CheckFire(archer, direction, target);
			}
		}
	}
}
