using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	public sealed class FireCloud
	{
		public int Age;
		public Vector2 Drift, Velocity;
		public float Phase, StartHeight;
		public float Height => StartHeight + Age * .85f;
		public float Radius => 5 + Age * .14f;
		public float Opacity => Math.Clamp(Math.Min(Age / 8f, (FireSmoke.Lifetime - Age) / 50f), 0, 1);
	}
	// Cosmetic, bounded particles. They follow simulation ticks and stop on pause;
	// neither wind nor smoke ever changes damage, inventory or shared random state.
	public sealed class FireSmoke
	{
		public const int Lifetime = 150, MaxClouds = 26;
		readonly List<FireCloud> clouds = [];
		int clock;
		public IReadOnlyList<FireCloud> Clouds => clouds;
		public void Tick(Vector2 wind, bool emitting, float height, uint seed)
		{
			if (!emitting && clouds.Count == 0) return;
			clock++;
			foreach (var cloud in clouds)
			{
				cloud.Age++; cloud.Velocity = Vector2.Lerp(cloud.Velocity, wind * .6f, .025f);
				cloud.Drift += cloud.Velocity + new Vector2(MathF.Sin(cloud.Age * .08f + cloud.Phase) * .16f, 0);
			}
			clouds.RemoveAll(c => c.Age >= Lifetime);
			if (emitting && clock % 6 == 0 && clouds.Count < MaxClouds)
				clouds.Add(new FireCloud { Phase = (clock + seed % 83) * .73f, StartHeight = height,
					Drift = new Vector2(MathF.Sin(clock * .37f + seed % 19) * 14, MathF.Cos(clock * .21f) * 4), Velocity = wind * .14f });
		}
	}
	sealed class FirePlumeRenderable : IRenderable, IFinalizedRenderable
	{
		readonly FireSmoke smoke;
		readonly bool burning, yard;
		readonly float strength, scale, lift;
		readonly uint seed;
		readonly int tick;
		public WPos Pos { get; }
		public int ZOffset => 512;
		public bool IsDecoration => false;
		public FirePlumeRenderable(WPos position, FireSmoke smoke, bool burning, bool yard, int remaining, int tick, uint seed, float scale = 1, float lift = 0)
		{ this.scale = scale; this.lift = lift; Pos = position; this.smoke = smoke; this.burning = burning; this.yard = yard; this.tick = tick; this.seed = seed; strength = .6f + .4f * remaining / SabotageRules.BurnTicks; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new FirePlumeRenderable(Pos + offset, smoke, burning, yard, (int)((strength - .6f) * SabotageRules.BurnTicks / .4f), tick, seed, scale, lift);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 200, p.Y - 280, 400, 320); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos); var cr = Game.Renderer.WorldRgbaColorRenderer;
			// Old clouds behind newer ones, warm soot near the flames and cooler grey above.
			var step = wr.Viewport.Zoom < .65f ? 2 : 1;
			for (var i = 0; i < smoke.Clouds.Count; i += step)
			{
				var c = smoke.Clouds[i]; var r = c.Radius; var age = c.Age / (float)FireSmoke.Lifetime;
				var center = origin + new Vector3(c.Drift.X, c.Drift.Y - c.Height, 0);
				for (var lobe = 0; lobe < 3; lobe++)
				{
					var p = center + new Vector3(MathF.Sin(c.Phase + age * 3 + lobe * 2.1f) * r * .4f, (lobe - 1) * r * .3f, 0);
					var shade = 39 + (int)(age * 35) + lobe * 7;
					cr.FillEllipse(p - new Vector3(r * 1.1f, r * .92f, 0), p + new Vector3(r * 1.1f, r * .92f, 0), Color.FromArgb((int)(30 * c.Opacity), shade, shade - 3, shade - 6));
					cr.FillEllipse(p - new Vector3(r * .74f, r * .68f, 0), p + new Vector3(r * .74f, r * .68f, 0), Color.FromArgb((int)(43 * c.Opacity), shade + 12, shade + 8, shade + 3));
				}
			}
			if (!burning) return;
			origin -= new Vector3(0, lift, 0);
			var time = tick * .14f + seed % 53;
			cr.FillEllipse(origin - new Vector3(42, 18, 0), origin + new Vector3(42, 18, 0), Color.FromArgb(45, 234, 97, 18));
			for (var jet = 0; jet < (yard ? 5 : 7); jet++)
			{
				var x = (jet % 3 - 1) * 13f * scale; var y = (jet / 3 - 1) * 7f * scale;
				var foot = origin + new Vector3(x - y, (x + y) * .5f - 3, 0);
				var h = (yard ? 40 : 55) + 12 * MathF.Sin(time * .7f + jet * 1.7f);
				h *= strength * scale;
				for (var layer = 0; layer < 3; layer++)
				{
					var height = h * (1 - layer * .27f); var width = (7 - layer * 2) * strength * scale;
					var color = layer == 0 ? Color.FromArgb(180, 211, 55, 12) : layer == 1 ? Color.FromArgb(225, 253, 133, 22) : Color.FromArgb(240, 255, 231, 137);
					Vector3 P(float t, float side)
					{
						var bend = MathF.Sin(time * 1.3f + jet + t * 6) * (2 + t * 5) * t;
						return foot + new Vector3(bend + side * width * MathF.Pow(1 - t, .65f), -t * height, 0);
					}
					for (var s = 0; s < 6; s++) { var t = s / 6f; var next = (s + 1) / 6f; cr.FillRect(P(t, -1), P(t, 1), P(next, 1), P(next, -1), color); }
				}
			}
			for (var ember = 0; ember < 9; ember++)
			{
				var t = ((tick + ember * 17 + seed % 19) % 100) / 100f;
				var p = origin + new Vector3(MathF.Sin(ember * 2.7f) * 22 * scale + t * 18, -t * 100 * scale, 0);
				cr.DrawLine(p, p + new Vector3(-1, 3, 0), 1, Color.FromArgb((int)(200 * (1 - t)), 255, 174, 62));
			}
		}
	}
}
