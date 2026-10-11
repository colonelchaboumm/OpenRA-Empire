using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class FireSupportRules
	{
		public const int WatchRadius = 4096, ArrowRadius = 3072;
		// Gameplay queries use the simulation's spatial index, never the camera or fog.
		public static bool Supported(Actor self, bool arrows) => self.World.FindActorsInCircle(self.CenterPosition,
			new WDist(arrows ? ArrowRadius : WatchRadius)).Any(a => a.TraitOrDefault<MedievalFireSource>() is { } fire
				&& fire.Info.Cauldron == arrows && MedievalStockpile.Active(a)
				&& self.Owner.RelationshipWith(a.Owner) == PlayerRelationship.Ally);
	}

	public class MedievalFireWatchInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFireWatch();
	}
	public sealed class MedievalFireWatch : ITick, IDetectCloakedModifier, IRevealsShroudModifier, ISync
	{
		[VerifySync] public bool Supported { get; private set; }
		void ITick.Tick(Actor self)
		{
			if ((self.World.WorldTick + self.ActorID) % 10 == 0)
				Supported = MedievalStockpile.Active(self) && self.TraitOrDefault<MedievalRest>()?.Sleeping != true && FireSupportRules.Supported(self, false);
		}
		int IDetectCloakedModifier.GetDetectCloakedModifier() => Supported ? 150 : 100;
		int IRevealsShroudModifier.GetRevealsShroudModifier() => Supported ? 125 : 100;
	}

	public class MedievalFireSourceInfo : TraitInfo
	{
		public readonly bool Cauldron = false;
		public override object Create(ActorInitializer init) => new MedievalFireSource(init.Self, this);
	}
	public sealed class MedievalFireSource : ITick, IRender, IRenderAnnotations
	{
		public readonly MedievalFireSourceInfo Info;
		public readonly FireSmoke Smoke = new();
		WPos previous;
		public MedievalFireSource(Actor self, MedievalFireSourceInfo info) { Info = info; previous = self.CenterPosition; }
		public static bool Visible(Actor a) => MedievalStockpile.Active(a) && !a.World.FogObscures(a)
			&& (a.World.RenderPlayer == null || a.CanBeViewedByPlayer(a.World.RenderPlayer));
		void ITick.Tick(Actor self)
		{
			// Smoke remains behind a moving cauldron instead of travelling glued to its wheels.
			var movement = self.CenterPosition - previous; previous = self.CenterPosition;
			if (movement != WVec.Zero)
			{
				var map = self.World.Map;
				var tiles = map.Rules.TerrainInfo.TileSize;
				var delta = new Vector2(movement.X * (float)tiles.Width / map.Grid.TileScale,
					(movement.Y - movement.Z) * (float)tiles.Height / map.Grid.TileScale);
				foreach (var cloud in Smoke.Clouds) cloud.Drift -= delta;
			}
			Smoke.Tick(self.World.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene.Wind ?? Vector2.Zero,
				MedievalStockpile.Active(self), Info.Cauldron ? 40 : 22, self.ActorID);
			if ((self.World.WorldTick + self.ActorID) % 100 == 0 && MedievalStockpile.Active(self))
				MedievalAudio.At(self.World, "fire", self.CenterPosition);
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (!Visible(self)) yield break;
			yield return new FirePlumeRenderable(self.CenterPosition, Smoke, true, true, SabotageRules.BurnTicks,
				self.World.WorldTick, self.ActorID, Info.Cauldron ? .8f : .55f, Info.Cauldron ? 25 : 3);
			yield return new MedievalGroundLayer(new SmokeShadowRenderable(self.CenterPosition, Smoke,
				self.World.WorldActor.TraitOrDefault<MedievalSunlight>()?.Shadow ?? default), 2);
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			var p = wr.ScreenPxPosition(self.CenterPosition); yield return new Rectangle(p.X - 200, p.Y - 280, 400, 320);
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || !Visible(self)) yield break;
			foreach (var line in MedievalFoodProvider.Ring(self.CenterPosition,
				new WDist(Info.Cauldron ? FireSupportRules.ArrowRadius : FireSupportRules.WatchRadius), Color.Goldenrod)) yield return line;
		}
	}

	public class MedievalFireArrowInfo : BulletInfo
	{
		public override IProjectile Create(ProjectileArgs args) => new MedievalFireArrow(this, args);
	}
	public sealed class MedievalFireArrow : Bullet
	{
		[VerifySync] public readonly bool Lit;
		readonly Player firingOwner;
		public MedievalFireArrow(MedievalFireArrowInfo info, ProjectileArgs args) : base(info, args)
		{
			firingOwner = args.SourceActor.Owner;
			Lit = args.SourceActor.Info.Name is "md.archer" or "md.elitearcher" && FireSupportRules.Supported(args.SourceActor, true);
		}
		protected override void Explode(World world)
		{
			base.Explode(world);
			if (!Lit) return;
			// Impact must actually intersect a hostile burnable structure. Arrows that
			// miss, or hit troops, do not ignite the originally selected building remotely.
			foreach (var a in world.FindActorsInCircle(pos, new WDist(3072)).OrderBy(a => a.ActorID))
			{
				if (!MedievalStockpile.Active(a) || firingOwner.RelationshipWith(a.Owner) != PlayerRelationship.Enemy
					|| a.TraitOrDefault<MedievalBurnable>() is not { } fire) continue;
				if (!a.EnabledTargetablePositions.OfType<HitShape>().Any(h => h.DistanceFromEdge(a, pos).Length <= 0)) continue;
				fire.Ignite(a, Args.SourceActor, a.Location, false);
				break;
			}
		}
		public override IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			foreach (var r in base.Render(wr)) yield return r;
			if (Lit && !FlightLengthReached && !wr.World.FogObscures(pos))
				yield return new FireArrowRenderable(pos, lastPos, wr.World.WorldTick);
		}
	}

	sealed class FireArrowRenderable : IRenderable, IFinalizedRenderable
	{
		readonly WPos previous;
		readonly int tick;
		public WPos Pos { get; }
		public int ZOffset => 1;
		public bool IsDecoration => true;
		public FireArrowRenderable(WPos pos, WPos previous, int tick) { Pos = pos; this.previous = previous; this.tick = tick; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new FireArrowRenderable(Pos + offset, previous + offset, tick);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 24, p.Y - 24, 48, 48); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var p = wr.Screen3DPosition(Pos); var d = p - wr.Screen3DPosition(previous);
			if (d.LengthSquared() < .01f) d = Vector3.UnitX; else d = Vector3.Normalize(d);
			var tail = p - d * (11 + 2 * MathF.Sin(tick * 1.3f));
			cr.DrawLine(p - d * 2, tail, 4, Color.FromArgb(205, 236, 75, 13));
			cr.DrawLine(p, p - d * 7, 2, Color.FromArgb(255, 255, 210, 87));
			cr.FillEllipse(p - new Vector3(2, 2, 0), p + new Vector3(2, 2, 0), Color.LightYellow);
			cr.DrawLine(tail, tail - d * 7 + new Vector3(0, -3, 0), 3, Color.FromArgb(95, 55, 50, 44));
		}
	}
}
