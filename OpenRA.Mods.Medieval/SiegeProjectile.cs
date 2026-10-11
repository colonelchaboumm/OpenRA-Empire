using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class SiegeShotMotion
	{
		public const int FireTicks = 40, FireFrames = 32, ReleaseTick = 8;
		public static int Frame(int tick) => Math.Clamp(tick * FireFrames / FireTicks, 0, FireFrames - 1);
		public static int Smooth(int t, int a, int b)
		{
			var u = Math.Clamp((t - a) * 1000 / (b - a), 0, 1000);
			return (int)((long)u * u * (3000 - 2 * u) / 1000000);
		}
		// The same integer pose curve is used by tools/trebuchet_art.py.
		public static WAngle ArmAngle(int frame)
		{
			var t = frame * 1000 / (FireFrames - 1);
			return new WAngle(t < 190 ? -49 + 219 * Smooth(t, 0, 190) / 1000
				: t < 320 ? 170 - 20 * Smooth(t, 190, 320) / 1000 : 150 - 199 * Smooth(t, 320, 1000) / 1000);
		}
		public static WPos SlingPosition(WPos origin, WAngle facing, int frame, int tileScale, int tileWidth, int tileHeight)
		{
			var arm = ArmAngle(frame); var t = frame * 1000 / (FireFrames - 1);
			var forward = 63 * arm.Cos() / 1024 + 14;
			var lift = -12 + 18 * (t < 320 ? Smooth(t, 0, 190) : 1000 - Smooth(t, 320, 800)) / 1000;
			var height = 65 + 63 * arm.Sin() / 1024 + lift;
			var angle = new WAngle(OpenRA.Mods.Common.Util.IndexFacing(facing, 8) * 128);
			return origin + new WVec(-angle.Sin() * forward * tileScale / (1024 * tileWidth),
				-angle.Cos() * forward * tileScale / (1024 * tileWidth), height * tileScale / tileHeight);
		}
		public static WPos SlingPosition(Actor actor, int frame = 0) => SlingPosition(actor.CenterPosition, actor.Trait<Mobile>().Facing, frame,
			actor.World.Map.Grid.TileScale, actor.World.Map.Rules.TerrainInfo.TileSize.Width, actor.World.Map.Rules.TerrainInfo.TileSize.Height);
	}

	public class MedievalSiegeStoneInfo : BulletInfo
	{
		public override IProjectile Create(ProjectileArgs args) => new MedievalSiegeStone(this, args);
	}
	public sealed class MedievalSiegeStone : Bullet
	{
		readonly WPos origin;
		readonly WAngle facing;
		readonly int tileScale, tileWidth, tileHeight;
		readonly Queue<WPos> trail = new();
		[VerifySync] int age;
		public int Age => age;
		public WPos Position => pos;
		public bool Released => age > SiegeShotMotion.ReleaseTick;
		static ProjectileArgs Launch(ProjectileArgs args)
		{
			args.Source = SiegeShotMotion.SlingPosition(args.SourceActor, SiegeShotMotion.Frame(SiegeShotMotion.ReleaseTick));
			return args;
		}
		public MedievalSiegeStone(MedievalSiegeStoneInfo info, ProjectileArgs args) : base(info, Launch(args))
		{
			origin = args.SourceActor.CenterPosition; facing = args.SourceActor.Trait<Mobile>().Facing;
			var map = args.SourceActor.World.Map;
			tileScale = map.Grid.TileScale; tileWidth = map.Rules.TerrainInfo.TileSize.Width; tileHeight = map.Rules.TerrainInfo.TileSize.Height;
			pos = SiegeShotMotion.SlingPosition(origin, facing, 0, tileScale, tileWidth, tileHeight);
		}
		public override void Tick(World world)
		{
			if (age++ < SiegeShotMotion.ReleaseTick)
			{
				pos = SiegeShotMotion.SlingPosition(origin, facing, SiegeShotMotion.Frame(age), tileScale, tileWidth, tileHeight);
				return;
			}
			if (age == SiegeShotMotion.ReleaseTick + 1) MedievalAudio.At(world, "trebuchet-release", pos);
			if (age == SiegeShotMotion.ReleaseTick + 14) MedievalAudio.At(world, "stone-air", pos);
			trail.Enqueue(pos); if (trail.Count > 5) trail.Dequeue();
			base.Tick(world);
		}
		protected override void Explode(World world)
		{
			base.Explode(world);
			var impact = pos - new WVec(0, 0, world.Map.DistanceAboveTerrain(pos).Length);
			MedievalAudio.At(world, "stone-impact", impact);
			var effect = new SiegeImpactEffect(impact, world.LocalRandom.Next());
			world.AddFrameEndTask(w => w.Add(effect));
		}
		public override IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			if (wr.World.FogObscures(pos) || FlightLengthReached) yield break;
			var sprite = Animation.Image;
			var rotation = new WAngle(Released ? (age - SiegeShotMotion.ReleaseTick) * 37 : 0);
			if (Released)
			{
				var ground = pos - new WVec(0, 0, wr.World.Map.DistanceAboveTerrain(pos).Length);
				yield return new SiegeStoneShadow(ground, Math.Max(0, pos.Z - ground.Z));
				var i = 0;
				foreach (var point in trail)
				{
					i++;
					if (!wr.World.FogObscures(point)) yield return new SpriteRenderable(sprite, point, WVec.Zero, 1024, null, .35f + i * .07f,
						.015f * i, new Vector3(.72f, .65f, .50f), TintModifiers.None, true, rotation);
				}
			}
			yield return new SpriteRenderable(sprite, pos, WVec.Zero, 1024, null, .75f, 1, Vector3.One, TintModifiers.None, true, rotation);
		}
	}
	sealed class SiegeStoneShadow : IRenderable, IFinalizedRenderable
	{
		readonly int height;
		public WPos Pos { get; }
		public int ZOffset => -128;
		public bool IsDecoration => true;
		public SiegeStoneShadow(WPos pos, int height) { Pos = pos; this.height = height; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new SiegeStoneShadow(Pos + offset, height);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var at = wr.ScreenPxPosition(Pos); return new Rectangle(at.X - 15, at.Y - 8, 30, 16); }
		public void Render(WorldRenderer wr)
		{
			var at = wr.Screen3DPxPosition(Pos); var far = Math.Clamp(height / 10000f, 0, 1);
			for (var i = 3; i >= 0; i--)
			{
				var size = new Vector3(5 + far * 4 + i, 2.3f + far * 2 + i * .4f, 0);
				Game.Renderer.WorldRgbaColorRenderer.FillEllipse(at - size, at + size, Color.FromArgb((int)(15 + (1 - far) * 13), 27, 26, 21));
			}
		}
	}
}
