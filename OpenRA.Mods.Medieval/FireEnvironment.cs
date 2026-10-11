using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// A bounded, world-anchored cosmetic heat field: moving fires leave a thawed
	// trail that slowly freezes again. No map-sized arrays or gameplay RNG.
	public sealed class FireHeatField
	{
		public const int MaximumSamples = 8192;
		readonly Dictionary<(int X, int Y), float> heat = [];
		public int Count => heat.Count;
		public void Clear() => heat.Clear();
		public void Cool()
		{
			foreach (var key in heat.Keys.ToArray())
			{
				var next = heat[key] - .012f;
				if (next <= 0) heat.Remove(key); else heat[key] = next;
			}
		}
		public void Warm(WPos pos, int radius)
		{
			for (var y = (int)Math.Floor((pos.Y - radius) / 1024f); y <= (pos.Y + radius) / 1024; y++)
				for (var x = (int)Math.Floor((pos.X - radius) / 1024f); x <= (pos.X + radius) / 1024; x++)
				{
					var dx = x * 1024L - pos.X; var dy = y * 1024L - pos.Y;
					var strength = Math.Clamp(1 - (dx * dx + dy * dy) / (float)((long)radius * radius), 0, 1);
					if (strength <= 0 || heat.Count >= MaximumSamples && !heat.ContainsKey((x, y))) continue;
					var old = heat.GetValueOrDefault((x, y));
					heat[(x, y)] = Math.Max(old, Math.Min(strength * 1.6f, old + strength * .12f));
				}
		}
		public float SnowRemaining(WPos pos)
		{
			var x = (int)Math.Floor(pos.X / 1024f); var y = (int)Math.Floor(pos.Y / 1024f);
			var u = pos.X / 1024f - x; var v = pos.Y / 1024f - y;
			var a = heat.GetValueOrDefault((x, y)) * (1 - u) + heat.GetValueOrDefault((x + 1, y)) * u;
			var b = heat.GetValueOrDefault((x, y + 1)) * (1 - u) + heat.GetValueOrDefault((x + 1, y + 1)) * u;
			return 1 - Math.Clamp(a * (1 - v) + b * v, 0, 1);
		}
	}

	public readonly record struct FireLight(uint ActorId, Vector2 Point, float Radius, float Strength, float Aspect = .55f)
	{
		public float At(Vector2 point)
		{
			var d = (point - Point) / new Vector2(Radius, Radius * Aspect);
			return Strength * Math.Clamp(1 - d.LengthSquared(), 0, 1);
		}
	}
	[TraitLocation(SystemActors.World)]
	public class MedievalFireEnvironmentInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFireEnvironment();
	}
	public sealed class MedievalFireEnvironment : ITick, ITickRender
	{
		readonly Dictionary<(int X, int Y), List<FireLight>> lights = [];
		public readonly FireHeatField Heat = new();
		public int VisibleLights { get; private set; }
		static (int X, int Y) Bin(Vector2 p) => ((int)MathF.Floor(p.X / 256), (int)MathF.Floor(p.Y / 256));
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (w.WorldTick % 10 != 0) return;
			if (w.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Snow is not > 0) { Heat.Clear(); return; }
			Heat.Cool();
			foreach (var p in w.ActorsWithTrait<MedievalFireSource>())
				if (MedievalFireSource.Visible(p.Actor)) Heat.Warm(p.Actor.CenterPosition, p.Trait.Info.Cauldron ? 2800 : 2400);
			foreach (var p in w.ActorsWithTrait<MedievalBurnable>())
				if (p.Trait.Burning && MedievalFireSource.Visible(p.Actor) && !w.FogObscures(p.Trait.Origin)) Heat.Warm(p.Trait.Origin, 3200);
		}
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			lights.Clear(); VisibleLights = 0;
			var w = self.World; var left = wr.Viewport.TopLeft; var right = wr.Viewport.BottomRight;
			void Add(Actor a, WPos position, float radius, float strength, float aspect = .55f)
			{
				var p = wr.ScreenPxPosition(position); var point = new Vector2(p.X, p.Y);
				if (p.X < left.X - radius || p.X > right.X + radius || p.Y < left.Y - radius || p.Y > right.Y + radius) return;
				var light = new FireLight(a.ActorID, point, radius, strength * (.94f + .06f * MathF.Sin(w.WorldTick * .35f + a.ActorID)), aspect);
				var from = Bin(point - new Vector2(radius)); var to = Bin(point + new Vector2(radius));
				for (var y = from.Y; y <= to.Y; y++)
					for (var x = from.X; x <= to.X; x++)
					{
						if (!lights.TryGetValue((x, y), out var list)) lights[(x, y)] = list = [];
						list.Add(light);
					}
				VisibleLights++;
			}
			// Hidden enemy fires do not leak through lighting or their shadows.
			foreach (var p in w.ActorsWithTrait<MedievalFireSource>())
				if (MedievalFireSource.Visible(p.Actor)) Add(p.Actor, p.Actor.CenterPosition, p.Trait.Info.Cauldron ? 160 : 135, 1);
			foreach (var p in w.ActorsWithTrait<MedievalBurnable>())
				if (p.Trait.Burning && MedievalFireSource.Visible(p.Actor) && !w.FogObscures(p.Trait.Origin)) Add(p.Actor, p.Trait.Origin, 185, 1);
			foreach (var a in w.ActorsHavingTrait<MedievalTorch>())
				if (MedievalTorch.Lit(a)) Add(a, a.CenterPosition, 105 * Math.Clamp(a.Trait<MedievalTorch>().Info.ScalePercent, 20, 100) / 100f, 1, 72f / 105);
		}
		public float StrengthAt(Vector2 point)
		{
			var value = 0f;
			if (lights.TryGetValue(Bin(point), out var nearby)) foreach (var l in nearby) value = Math.Max(value, l.At(point));
			return value;
		}
		public SolarShadow ShadowAt(Actor a, WorldRenderer wr)
		{
			var p = wr.ScreenPxPosition(a.CenterPosition); var point = new Vector2(p.X, p.Y);
			var strongest = 0f; FireLight light = default;
			if (lights.TryGetValue(Bin(point), out var nearby)) foreach (var candidate in nearby)
			{
				if (candidate.ActorId == a.ActorID) continue;
				var strength = candidate.At(point);
				if (strength > strongest) { strongest = strength; light = candidate; }
			}
			var d = (point - light.Point) / new Vector2(1, .55f);
			if (strongest < .02f || d.LengthSquared() < 16) return default;
			var night = a.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.Darkness ?? 0;
			return new SolarShadow(Vector2.Normalize(d), .65f + d.Length() / 120,
				strongest * (.08f + .34f * night), new Vector3(.07f, .045f, .03f));
		}
	}

	sealed class SmokeShadowRenderable : IRenderable, IFinalizedRenderable
	{
		readonly FireSmoke smoke;
		readonly SolarShadow sun;
		public WPos Pos { get; }
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public SmokeShadowRenderable(WPos pos, FireSmoke smoke, SolarShadow sun) { Pos = pos; this.smoke = smoke; this.sun = sun; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new SmokeShadowRenderable(Pos + offset, smoke, sun);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 260, p.Y - 200, 520, 400); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos); var cr = Game.Renderer.WorldRgbaColorRenderer;
			foreach (var cloud in smoke.Clouds.Where((_, i) => i % 2 == 0))
			{
				var reach = Math.Min(SolarShadow.MaximumReach, cloud.Height * sun.Length);
				var at = origin + new Vector3(cloud.Drift.X + sun.Direction.X * reach, cloud.Drift.Y + sun.Direction.Y * reach * .55f, 0);
				var r = cloud.Radius * 1.3f;
				cr.FillEllipse(at - new Vector3(r, r * .55f, 0), at + new Vector3(r, r * .55f, 0), Color.FromArgb((int)(cloud.Opacity * sun.Opacity * 32), 27, 29, 30));
			}
		}
	}
}
