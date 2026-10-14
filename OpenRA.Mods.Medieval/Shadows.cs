using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// Presentation only: use the synchronized clocks, never advance them or consume RNG.
	public readonly record struct SolarShadow(Vector2 Direction, float Length, float Opacity, Vector3 Color, float Softness = 0)
	{
		public const int MaximumReach = 180;
		public static SolarShadow At(DayClock day, SeasonClock season, int rain)
		{
			var clouds = 1 - .82f * Math.Clamp(rain / 1000f, 0, 1);
			if (day.Night)
			{
				var phase = (day.Phase - day.DayTicks) / (float)day.NightTicks;
				var moon = MathF.Sin(MathF.PI * phase);
				return new(Vector2.Normalize(new Vector2(.45f - phase * .9f, .65f)), .65f,
					.065f * moon * moon * clouds, new Vector3(.10f, .15f, .23f), 1.2f);
			}

			var t = day.Phase / (float)day.DayTicks;
			var height = .68f + .23f * (season?.Weight(VillageSeason.Summer) ?? 0)
				- .10f * (season?.Weight(VillageSeason.Autumn) ?? 0) - .30f * (season?.Weight(VillageSeason.Winter) ?? 0);
			var arc = MathF.Sin(MathF.PI * t);
			var direction = Vector2.Normalize(new Vector2(.95f * MathF.Cos(MathF.PI * t), .46f + arc * .20f));
			var length = Math.Clamp(1.1f / (.28f + height * arc) - .30f, .45f, 3.1f);
			var fade = SolarLighting.Smooth(t / .065f) * SolarLighting.Smooth((1 - t) / .065f);
			var highSun = SolarLighting.Smooth(arc / .65f);
			var color = Vector3.Lerp(new Vector3(.10f, .13f, .21f), new Vector3(.07f, .10f, .13f), highSun);
			color = Vector3.Lerp(color, new Vector3(.13f, .21f, .30f), (season?.Snow ?? 0) / 1000f);
			return new(direction, length, .32f * (.58f + .42f * highSun) * fade * clouds * (1 - SolarLighting.Smooth(day.Darkness)), color,
				.55f + 1.35f * (1 - highSun) + .65f * (1 - clouds));
		}

		public Matrix4x4 Projection(float height)
		{
			var distance = Math.Min(Length, MaximumReach / Math.Max(1, height));
			// The ground origin never moves. Upright pixels are projected onto the
			// isometric ground; a positive Y component prevents a singular noon pose.
			return new Matrix4x4(1, 0, 0, 0, -Direction.X * distance, -Direction.Y * distance * .55f, 0, 0,
				0, 0, 1, 0, 0, 0, 0, 1);
		}

		public IRenderable Project(SpriteRenderable sprite, WorldRenderer wr)
		{
			var anchor = wr.ScreenPxPosition(sprite.Pos);
			var bounds = sprite.ScreenBounds(wr);
			var height = Math.Max(1, Math.Max(Math.Abs(bounds.Top - anchor.Y), Math.Abs(bounds.Bottom - anchor.Y)));
			var projection = Projection(height);
			var silhouette = sprite.WithScreenTransform((sprite.ScreenTransform ?? Matrix4x4.Identity) * projection)
				.WithTint(Color, TintModifiers.ReplaceColor | TintModifiers.IgnoreWorldTint).WithAlpha(sprite.Alpha * Opacity);
			if (Softness > 0 && wr.Viewport.Zoom >= .75f)
				return new MedievalGroundLayer(new SoftSolarShadow(sprite, this, height), 2);
			return new MedievalGroundLayer(silhouette.AsDecoration(), 2);
		}
	}

	// Three bounded penumbra samples, only at close zoom. The roots stay pinned;
	// the softness grows towards the tip. Distant views keep a single silhouette.
	sealed class SoftSolarShadow : IRenderable, IFinalizedRenderable
	{
		readonly SpriteRenderable sprite;
		readonly SolarShadow light;
		readonly float height;
		readonly IFinalizedRenderable[] samples;
		public WPos Pos => sprite.Pos;
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public SoftSolarShadow(SpriteRenderable sprite, SolarShadow light, float height)
		{
			this.sprite = sprite; this.light = light; this.height = height;
			samples = new IFinalizedRenderable[3];
			for (var i = 0; i < samples.Length; i++)
			{
				var projection = light.Projection(height);
				var spread = (i - 1) * light.Softness / height;
				projection.M21 += spread * light.Direction.Y;
				projection.M22 -= spread * light.Direction.X * .55f;
				// Composite to the requested opacity instead of darkening three times.
				var alpha = 1 - MathF.Pow(1 - Math.Clamp(sprite.Alpha * light.Opacity, 0, 1), i == 1 ? .5f : .25f);
				samples[i] = (IFinalizedRenderable)sprite.WithScreenTransform((sprite.ScreenTransform ?? Matrix4x4.Identity) * projection)
					.WithTint(light.Color, TintModifiers.ReplaceColor | TintModifiers.IgnoreWorldTint).WithAlpha(alpha);
			}
		}
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new SoftSolarShadow((SpriteRenderable)sprite.OffsetBy(offset), light, height);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void Render(WorldRenderer wr) { foreach (var sample in samples) sample.Render(wr); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			var a = samples[0].ScreenBounds(wr); var b = samples[1].ScreenBounds(wr); var c = samples[2].ScreenBounds(wr);
			return Rectangle.FromLTRB(Math.Min(a.Left, Math.Min(b.Left, c.Left)), Math.Min(a.Top, Math.Min(b.Top, c.Top)),
				Math.Max(a.Right, Math.Max(b.Right, c.Right)), Math.Max(a.Bottom, Math.Max(b.Bottom, c.Bottom)));
		}
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalSunlightInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSunlight(init.World);
	}
	public sealed class MedievalSunlight : ITickRender
	{
		readonly World world;
		MedievalDayNight day;
		MedievalSeasons season;
		MedievalWeather weather;
		public SolarShadow Shadow { get; private set; }
		public int VisibleShadows { get; internal set; }
		public MedievalSunlight(World world) { this.world = world; }
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			day ??= world.WorldActor.TraitOrDefault<MedievalDayNight>();
			season ??= world.WorldActor.TraitOrDefault<MedievalSeasons>();
			weather ??= world.WorldActor.TraitOrDefault<MedievalWeather>();
			Shadow = day == null ? default : SolarShadow.At(day.Clock, season?.Clock, weather?.RainPermille ?? 0);
			VisibleShadows = 0;
		}
	}

	interface IMedievalShadowCaster
	{
		IRenderable Shadow(SolarShadow light, WorldRenderer wr);
	}

	public class MedievalCastShadowInfo : TraitInfo
	{
		public readonly bool GroundOnly = false;
		public override object Create(ActorInitializer init) => new MedievalCastShadow(init.Self, this);
	}
	public sealed class MedievalCastShadow : IFinalRenderModifier
	{
		readonly MedievalCastShadowInfo info;
		readonly World world;
		MedievalSunlight sunlight;
		SnowSurface[] enclosureRails;
		public MedievalCastShadow(Actor self, MedievalCastShadowInfo info) { this.info = info; world = self.World; }
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			sunlight ??= world.WorldActor.TraitOrDefault<MedievalSunlight>();
			var ground = info.GroundOnly || self.TraitOrDefault<MedievalAgriculturalArea>() != null;
			var groundDrawn = false;
			var visible = sunlight != null && !self.IsDead && !world.FogObscures(self)
				&& (world.RenderPlayer == null || self.CanBeViewedByPlayer(world.RenderPlayer));
			var fire = visible ? world.WorldActor.TraitOrDefault<MedievalFireEnvironment>()?.ShadowAt(self, wr) ?? default : default;
			foreach (var r in renderables)
			{
				if (ground)
				{
					groundDrawn |= r is SpriteRenderable;
					yield return r is SpriteRenderable || r is FieldReliefRenderable ? new MedievalGroundLayer(r, 0) : r;
					continue;
				}
				yield return r;
				// Lights, smoke, fire, UI and garrison passengers are not ground casters.
				if (!visible || r.IsDecoration || r.Pos != self.CenterPosition) continue;
				for (var source = 0; source < 2; source++)
				{
					var light = source == 0 ? sunlight.Shadow : fire;
					if (light.Opacity <= .002f) continue;
					var shadow = r is SpriteRenderable sprite ? light.Project(sprite, wr)
						: r is IMedievalShadowCaster caster ? caster.Shadow(light, wr) : null;
					if (shadow == null) continue;
					sunlight.VisibleShadows++;
					yield return shadow;
				}
			}
			if (groundDrawn && visible && MedievalStockpile.Active(self)
				&& self.TraitOrDefault<MedievalAgriculturalArea>() is MedievalAgriculturalArea area && area.Info.Profession != "farmer")
			{
				enclosureRails ??= SnowGeometry.For(self.Info.Name, 0, area.Size).Where(s => s.Rail).ToArray();
				if (sunlight.Shadow.Opacity > .002f) yield return new MedievalGroundLayer(new EnclosureShadow(self.CenterPosition, enclosureRails, sunlight.Shadow), 2);
				if (fire.Opacity > .002f) yield return new MedievalGroundLayer(new EnclosureShadow(self.CenterPosition, enclosureRails, fire), 2);
			}
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			// Spatial culling includes cast shadows from actors just outside the view.
			// Fixed bounds avoid rebuilding the screen index every time the sun moves.
			var margin = info.GroundOnly ? 0 : SolarShadow.MaximumReach + 3;
			foreach (var b in bounds) yield return Rectangle.FromLTRB(b.Left - margin, b.Top - margin, b.Right + margin, b.Bottom + margin);
		}
	}

	sealed class EnclosureShadow : IRenderable, IFinalizedRenderable
	{
		readonly SnowSurface[] rails;
		readonly SolarShadow light;
		public WPos Pos { get; }
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public EnclosureShadow(WPos pos, SnowSurface[] rails, SolarShadow light) { Pos = pos; this.rails = rails; this.light = light; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new EnclosureShadow(Pos + offset, rails, light);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 450, p.Y - 220, 900, 440); }
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos);
			var color = Color.FromArgb((int)(light.Opacity * 255), (int)(light.Color.X * 255), (int)(light.Color.Y * 255), (int)(light.Color.Z * 255));
			foreach (var rail in rails)
			{
				var post = (rail.B - rail.A).LengthSquared() < 9;
				var height = post ? 14 : 11;
				var root = new Vector3(0, height, 0);
				var offset = new Vector3(light.Direction.X, light.Direction.Y * .55f, 0) * height * light.Length;
				var a = origin + rail.A + root;
				Game.Renderer.WorldRgbaColorRenderer.DrawLine(post ? a : a + offset, origin + rail.B + root + offset, post ? 2 : 1.4f, color);
			}
		}
	}

	sealed class MedievalGroundLayer : IGroundLayerRenderable, IFinalizedRenderable
	{
		readonly IRenderable source;
		IFinalizedRenderable prepared;
		public int GroundLayer { get; }
		public WPos Pos => source.Pos;
		public int ZOffset => source.ZOffset;
		public bool IsDecoration => true;
		public MedievalGroundLayer(IRenderable source, int layer) { this.source = source; GroundLayer = layer; }
		public IRenderable WithZOffset(int z) => new MedievalGroundLayer(source.WithZOffset(z), GroundLayer);
		public IRenderable OffsetBy(in WVec offset) => new MedievalGroundLayer(source.OffsetBy(offset), GroundLayer);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { prepared = source.PrepareRender(wr); return this; }
		public void Render(WorldRenderer wr) => prepared.Render(wr);
		public void RenderDebugGeometry(WorldRenderer wr) => prepared.RenderDebugGeometry(wr);
		public Rectangle ScreenBounds(WorldRenderer wr) => (prepared ?? source.PrepareRender(wr)).ScreenBounds(wr);
	}
}
