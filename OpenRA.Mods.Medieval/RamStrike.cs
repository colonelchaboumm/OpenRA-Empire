using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class RamMotion
	{
		public const int FireTicks = 32, FireFrames = 24, ContactTick = 14;
		public static int Frame(int tick) => Math.Clamp(tick * FireFrames / FireTicks, 0, FireFrames - 1);
		// The first occupied footprint edge on the approach, including wide gates.
		public static WPos Contact(Actor source, Actor target)
		{
			var origin = source.CenterPosition; var end = target.CenterPosition;
			var building = target.Info.TraitInfoOrDefault<BuildingInfo>();
			if (building == null) return end;
			var tiles = building.Tiles(target.Location).ToHashSet();
			for (var step = 1; step <= 128; step++)
			{
				var point = WPos.Lerp(origin, end, step, 128);
				if (tiles.Contains(source.World.Map.CellContaining(point))) return point;
			}
			return end;
		}
	}

	public class MedievalRamStrikeInfo : IProjectileInfo
	{
		public IProjectile Create(ProjectileArgs args) => new MedievalRamStrike(args);
	}
	// The vehicle occupies one navigation cell, but its suspended beam extends
	// farther forward. Approach the wall surface instead of driving under its roof.
	public sealed class RamAttack : Attack
	{
		readonly IMove movement;
		readonly bool allowMove;
		bool approaching;
		public RamAttack(Actor self, in Target target, bool allowMove, bool forceAttack, Color? targetLineColor)
			: base(self, target, allowMove, forceAttack, targetLineColor)
		{ movement = self.Trait<IMove>(); this.allowMove = allowMove; }
		public override bool Tick(Actor self)
		{
			approaching = false;
			var complete = base.Tick(self);
			return complete && !approaching;
		}
		protected override AttackStatus TickAttack(Actor self, AttackFrontal attack)
		{
			if (target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<BuildingInfo>())
			{
				var contact = RamMotion.Contact(self, target.Actor);
				var distance = (contact - self.CenterPosition).HorizontalLength;
				if (distance < 1250 || distance > 1950)
				{
					if (!allowMove) return AttackStatus.UnableToAttack;
					QueueChild(movement.MoveWithinRange(Target.FromPos(contact), new WDist(1300), new WDist(1850)));
					approaching = true;
					return AttackStatus.NeedsToMove;
				}
			}
			return base.TickAttack(self, attack);
		}
	}
	// Damage is delayed until the head reaches its forward pose. An interrupted
	// manoeuvre must not leave an invisible projectile that can hit later.
	public sealed class MedievalRamStrike : IProjectile, ISync
	{
		readonly ProjectileArgs args;
		readonly Target target;
		readonly Player owner;
		readonly WPos origin;
		readonly int shot;
		[VerifySync] int age;
		public int Age => age;
		public MedievalRamStrike(ProjectileArgs args)
		{
			this.args = args; target = args.GuidedTarget; owner = args.SourceActor.Owner;
			origin = args.SourceActor.CenterPosition; shot = args.SourceActor.Trait<MedievalSiegeEngine>().Shots + 1;
		}
		public void Tick(World world)
		{
			var source = args.SourceActor; var engine = source.Trait<MedievalSiegeEngine>();
			if (!SiegeRules.Live(source, owner) || target.Type != TargetType.Actor || engine.AttackTicks == 0
				|| engine.Shots != shot || engine.ReadyCrew < SiegeRules.RamCrew
				|| (source.CenterPosition - origin).HorizontalLength > 128
				|| !target.IsInRange(source.CenterPosition, source.Trait<MedievalSiegeArmament>().MaxRange())
				|| !args.Weapon.IsValidAgainst(target, world, source))
			{ world.AddFrameEndTask(w => w.Remove(this)); return; }
			if (age++ < RamMotion.ContactTick) return;
			var contact = RamMotion.Contact(source, target.Actor);
			if ((contact - source.CenterPosition).HorizontalLength is < 1250 or > 1950)
			{ world.AddFrameEndTask(w => w.Remove(this)); return; }
			var health = target.Actor.TraitOrDefault<IHealth>(); var before = health?.HP ?? 0;
			args.Weapon.Impact(target, new WarheadArgs(args)
			{
				ImpactPosition = contact,
				ImpactOrientation = new WRot(WAngle.Zero, WAngle.Zero, args.Facing)
			});
			if (health != null && health.HP < before)
			{
				MedievalAudio.At(world, "ram-hit", contact);
				var outward = source.CenterPosition - contact;
				var direction = (float)Math.Atan2(outward.Y, outward.X);
				var effect = new SiegeImpactEffect(contact, world.LocalRandom.Next(), true, 29, true, direction);
				world.AddFrameEndTask(w => w.Add(effect));
			}
			world.AddFrameEndTask(w => w.Remove(this));
		}
		public IEnumerable<IRenderable> Render(WorldRenderer wr) => [];
	}
}
