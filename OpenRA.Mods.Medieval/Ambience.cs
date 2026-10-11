using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum AmbientKind { Bird, Petal, Seed, Leaf }
	public readonly record struct AmbientEmitter(Vector2 Ground, float Height, AmbientKind Kind);
	public sealed class AmbientParticle
	{
		public AmbientKind Kind;
		public Vector2 Ground;
		public Vector2 Velocity;
		public float Height;
		public float Phase;
		public float Rotation;
		public float Size;
		public int Age;
		public int Life;
		public int Variation;
		public VillageSeason Season;
		public bool Settled => Kind != AmbientKind.Bird && Height <= .1f;
		public float Opacity => Math.Clamp(Math.Min(Age / 18f, (Life - Age) / 35f), 0, 1);
	}

	// Cosmetic state only: a private RNG never consumes the simulation's RNG.
	// World-pixel positions stay anchored when the camera moves or zooms.
	public sealed class VillageAmbience
	{
		public const int MaxBirds = 8;
		public const int MaxDebris = 72;
		readonly Random random;
		readonly List<AmbientParticle> particles = [];
		int clock;
		int nextWind;
		int nextFlock = 1;
		int nextDebris = 3;
		Vector2 targetWind = new(.55f, .13f);
		public Vector2 Wind { get; private set; } = new(.45f, .1f);
		public float Seconds => clock / 25f;
		public int SpawnedBirds { get; private set; }
		public int SpawnedPetals { get; private set; }
		public int SpawnedSeeds { get; private set; }
		public IReadOnlyList<AmbientParticle> Particles => particles;
		public VillageAmbience(int seed) { random = new Random(seed); }
		float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
		public void Tick(Rectangle view, IReadOnlyList<AmbientEmitter> emitters, VillageSeason season = VillageSeason.Spring)
		{
			clock++;
			if (clock >= nextWind)
			{
				var angle = Range(-.9f, .9f) + (random.Next(5) == 0 ? MathF.PI : 0);
				var strength = Range(.25f, 1.65f);
				targetWind = new Vector2(MathF.Cos(angle) * strength, MathF.Sin(angle) * strength * .5f);
				nextWind = clock + random.Next(175, 451);
			}
			Wind = Vector2.Lerp(Wind, targetWind, .012f);
			foreach (var p in particles)
			{
				p.Age++;
				if (season == VillageSeason.Winter && p.Kind != AmbientKind.Bird) p.Life = Math.Min(p.Life, p.Age + 35);
				if (p.Kind == AmbientKind.Bird)
				{
					p.Ground += p.Velocity + new Vector2(0, MathF.Sin(p.Age * .018f + p.Phase) * .2f);
					p.Height += MathF.Sin(p.Age * .04f + p.Phase) * .10f;
				}
				else
				{
					if (p.Settled) { p.Velocity *= .65f; p.Ground += p.Velocity; continue; }
					p.Rotation = p.Age * (p.Kind == AmbientKind.Leaf ? .09f : .06f) + p.Phase;
					p.Velocity = Vector2.Lerp(p.Velocity, Wind * (p.Kind == AmbientKind.Seed ? 1.25f : .9f), .04f);
					p.Ground += p.Velocity + new Vector2(MathF.Sin(p.Age * .11f + p.Phase) * .13f, MathF.Cos(p.Age * .08f + p.Phase) * .12f);
					var fall = p.Kind == AmbientKind.Leaf ? .35f + .09f * MathF.Sin(p.Age * .08f + p.Phase) : p.Kind == AmbientKind.Petal ? .11f : .035f;
					p.Height = Math.Max(0, p.Height + (p.Age < 20 ? .12f * Wind.Length() : -fall));
				}
			}
			particles.RemoveAll(p => p.Age >= p.Life || p.Ground.X < view.Left - 220 || p.Ground.X > view.Right + 220 || p.Ground.Y < view.Top - 100 || p.Ground.Y > view.Bottom + 220);
			if (clock >= nextFlock)
			{
				var count = Math.Min(random.Next(1, 4), MaxBirds - particles.Count(p => p.Kind == AmbientKind.Bird));
				var direction = random.Next(2) == 0 ? 1 : -1;
				var start = new Vector2(view.Left + view.Width * (direction == 1 ? .15f : .85f), view.Top + view.Height * Range(.5f, .8f));
				for (var i = 0; i < count; i++)
				{
					particles.Add(new AmbientParticle { Kind = AmbientKind.Bird, Ground = start + new Vector2(-i * 24 * direction, i * 13),
						Velocity = new Vector2(direction * Range(1.3f, 2.0f), Range(-.25f, .25f)), Height = Range(100, 145),
						Size = Range(1.6f, 2.5f), Phase = Range(0, 6.28f), Life = random.Next(480, 851), Variation = random.Next(3) });
					SpawnedBirds++;
				}
				nextFlock = clock + random.Next(300, 701);
			}
			if (clock < nextDebris || emitters.Count == 0 || season == VillageSeason.Winter) return;
			var free = MaxDebris - particles.Count(p => p.Kind != AmbientKind.Bird);
			var burst = Math.Min(free, Wind.Length() > .85f ? random.Next(3, 7) : random.Next(1, 4));
			for (var i = 0; i < burst; i++)
			{
				var source = emitters[random.Next(emitters.Count)];
				particles.Add(new AmbientParticle { Kind = source.Kind, Ground = source.Ground + new Vector2(Range(-9, 9), Range(-4, 4)),
					Height = source.Height, Velocity = Wind * Range(.6f, 1.3f), Phase = Range(0, 6.28f), Size = Range(.8f, 1.4f),
					Life = random.Next(source.Kind == AmbientKind.Leaf ? 300 : 170, 401), Variation = random.Next(4), Season = season });
				if (source.Kind == AmbientKind.Petal) SpawnedPetals++;
				if (source.Kind == AmbientKind.Seed) SpawnedSeeds++;
			}
			nextDebris = clock + (season == VillageSeason.Autumn ? random.Next(6, 15) : random.Next(14, 30));
		}
		public float Sway(uint id, float amplitude) => amplitude * Math.Clamp(Wind.Length(), .15f, 1.5f)
			* (.45f * MathF.Sin(Seconds * 1.7f + id * .73f) + .25f * MathF.Sin(Seconds * 3.1f + id * .31f) + Wind.X * .25f);
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalAmbienceInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalAmbience(init.World);
	}
	public sealed class MedievalAmbience : IWorldLoaded, ITick, IRenderAboveWorld, IRenderOverlay
	{
		readonly World world;
		readonly List<AmbientEmitter> emitters = [];
		WorldRenderer renderer;
		int tick;
		public VillageAmbience Scene { get; }
		public MedievalAmbience(World world) { this.world = world; Scene = new VillageAmbience(world.LocalRandom.Next()); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr) { renderer = wr; }
		void ITick.Tick(Actor self)
		{
			if (renderer == null || world.Type == WorldType.Editor) return;
			tick++;
			var season = world.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Season ?? VillageSeason.Spring;
			var view = Rectangle.FromLTRB(renderer.Viewport.TopLeft.X, renderer.Viewport.TopLeft.Y, renderer.Viewport.BottomRight.X, renderer.Viewport.BottomRight.Y);
			if (tick % 25 == 1)
			{
				emitters.Clear();
				foreach (var a in world.ScreenMap.RenderableActorsInBox(renderer.Viewport.TopLeft, renderer.Viewport.BottomRight))
				{
					var vegetation = a.TraitOrDefault<MedievalWindVegetation>();
					if (vegetation == null) continue;
					if (season == VillageSeason.Winter) continue;
					var point = renderer.ScreenPxPosition(a.CenterPosition);
					if (!view.Contains(point)) continue;
					if (!a.IsInWorld || a.IsDead || world.FogObscures(a) || vegetation.Hidden(a) || !vegetation.Info.EmitParticles) continue;
					if (a.TraitOrDefault<MedievalSource>() is MedievalSource source && (source.Remaining == 0 || !source.StandingTree)) continue;
					var kind = vegetation.Info.Particles;
					if (kind == AmbientKind.Leaf && a.Info.Name == "md.pine") continue;
					if (season == VillageSeason.Spring && kind == AmbientKind.Leaf && a.ActorID % 3 == 0) kind = AmbientKind.Petal;
					if (season == VillageSeason.Summer && kind == AmbientKind.Petal) kind = AmbientKind.Seed;
					if (season == VillageSeason.Autumn && kind != AmbientKind.Leaf) continue;
					emitters.Add(new AmbientEmitter(point.ToVector2(), vegetation.Info.EmissionHeight, kind));
				}
			}
			Scene.Tick(view, emitters, season);
		}
		bool Visible(AmbientParticle p, WorldRenderer wr)
		{
			var cell = world.Map.CellContaining(wr.ProjectedPosition(new int2((int)p.Ground.X, (int)p.Ground.Y)));
			return world.Map.Contains(cell) && !world.FogObscures(cell)
				&& p.Ground.X > wr.Viewport.TopLeft.X - 20 && p.Ground.X < wr.Viewport.BottomRight.X + 20
				&& p.Ground.Y - p.Height > wr.Viewport.TopLeft.Y - 20 && p.Ground.Y - p.Height < wr.Viewport.BottomRight.Y + 20;
		}
		void IRenderOverlay.Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			foreach (var p in Scene.Particles)
				if (p.Kind == AmbientKind.Bird && Visible(p, wr))
				{
					var c = new Vector3(p.Ground, 0);
					cr.FillEllipse(c - new Vector3(5, 2, 0) * p.Size, c + new Vector3(5, 2, 0) * p.Size, Color.FromArgb((int)(20 * p.Opacity), 30, 35, 27));
				}
		}
		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			foreach (var p in Scene.Particles)
			{
				if (!Visible(p, wr) || p.Opacity <= 0) continue;
				var at = new Vector3(p.Ground.X, p.Ground.Y - p.Height, 0);
				var alpha = (int)(220 * p.Opacity);
				if (p.Kind == AmbientKind.Bird)
				{
					var f = new Vector3(Vector2.Normalize(p.Velocity), 0);
					var side = new Vector3(-f.Y, f.X, 0);
					// Larger birds alternate slow wing beats with sustained gliding.
					var glide = p.Age % 220 > 80;
					var flap = glide ? .15f + MathF.Sin(p.Age * .06f) * .12f : MathF.Sin(p.Age * .23f + p.Phase);
					var span = (5.2f + flap * 2.8f) * p.Size;
					var dark = p.Variation == 1 ? Color.FromArgb(alpha, 89, 98, 99) : Color.FromArgb(alpha, 53, 46, 35);
					var wing = p.Variation == 1 ? Color.FromArgb(alpha, 206, 211, 197) : Color.FromArgb(alpha, 114, 98, 66);
					foreach (var sign in new[] { -1, 1 })
					{
						var elbow = at + side * sign * span * .55f + f * p.Size;
						var tip = at + side * sign * span - f * p.Size * 1.5f + new Vector3(0, -flap * 3, 0);
						cr.FillRect(at + f * p.Size, elbow, tip, at - f * p.Size * 2, wing);
						cr.DrawLine(elbow, tip, p.Size, dark);
						for (var feather = 0; feather < 3; feather++)
							cr.DrawLine(Vector3.Lerp(elbow, tip, .6f + feather * .15f), tip - f * (feather + 1) * p.Size, 1, dark);
					}
					cr.DrawLine(at - f * 3.5f * p.Size, at + f * 2.8f * p.Size, 2 * p.Size, dark);
					cr.FillRect(at - f * 3 * p.Size, at - f * 5 * p.Size + side * 1.7f * p.Size,
						at - f * 5 * p.Size - side * 1.7f * p.Size, at - f * 3 * p.Size, wing);
					cr.DrawLine(at + f * 2.5f * p.Size, at + f * 3.4f * p.Size, p.Size, Color.FromArgb(alpha, 180, 158, 103));
				}
				else
				{
					var angle = p.Rotation;
					var tip = new Vector3(MathF.Cos(angle), MathF.Sin(angle) * (p.Settled ? .45f : 1), 0) * p.Size * (p.Kind == AmbientKind.Leaf ? 1.5f : 1);
					var across = new Vector3(-tip.Y, tip.X, 0) * (p.Settled ? .45f : .15f + MathF.Abs(MathF.Cos(p.Age * .12f + p.Phase)) * .5f);
					var color = p.Kind == AmbientKind.Leaf ? p.Season == VillageSeason.Autumn ? Color.FromArgb(alpha, 174 + p.Variation * 16, 83 + p.Variation * 22, 33 + p.Variation * 6) : Color.FromArgb(alpha, 155, 159, 76)
						: p.Kind == AmbientKind.Seed ? Color.FromArgb(alpha, 231, 225, 186)
						: p.Variation == 0 ? Color.FromArgb(alpha, 226, 160, 149) : Color.FromArgb(alpha, 237, 219, 167);
					if (p.Kind == AmbientKind.Seed)
					{
						cr.DrawLine(at - tip * 2, at + tip, .8f, color);
						cr.DrawLine(at + tip - across, at + tip + across, 1, color);
					}
					else cr.FillRect(at - tip, at + across, at + tip, at - across, color);
				}
			}
		}
	}

	public class MedievalWindVegetationInfo : TraitInfo, Requires<WithSpriteBodyInfo>
	{
		public readonly float Amplitude = 2.3f;
		public readonly int EmissionHeight = 12;
		public readonly AmbientKind Particles = AmbientKind.Leaf;
		public readonly bool HideUnderBuildings = false;
		public readonly bool EmitParticles = true;
		public override object Create(ActorInitializer init) => new MedievalWindVegetation(this);
	}
	public sealed class MedievalWindVegetation : IRenderModifier
	{
		public readonly MedievalWindVegetationInfo Info;
		public MedievalWindVegetation(MedievalWindVegetationInfo info) { Info = info; }
		public bool Hidden(Actor self) => Info.HideUnderBuildings && (MedievalStockyard.Covers(self.World, self.Location)
			|| self.World.WorldActor.TraitOrDefault<MedievalTrails>()?.ProtectsCell(self.Location) == true
			|| self.World.ActorMap.GetActorsAt(self.Location).Any(a => a != self && a.TraitOrDefault<Building>() != null));
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			if (Hidden(self)) yield break;
			if (self.TraitOrDefault<MedievalSource>() is MedievalSource source && !source.StandingTree)
			{
				foreach (var renderable in renderables) yield return renderable;
				yield break;
			}
			var scene = self.World.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene;
			var body = self.Trait<WithSpriteBody>().DefaultAnimation;
			var sway = scene == null || self.TraitOrDefault<MedievalSource>()?.Remaining == 0 ? 0 : scene.Sway(self.ActorID, Info.Amplitude);
			foreach (var renderable in renderables)
				if (renderable is SpriteRenderable sprite && body.Image.Channel == TextureChannel.RGBA)
					yield return new WindVegetationRenderable(body.Image, sprite.Pos, sprite.ZOffset, sprite.Alpha, sprite.Tint, sway, NatureGeometry.Root(self.Info.Name));
				else yield return renderable;
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			foreach (var b in bounds) yield return Rectangle.FromLTRB(b.Left - 6, b.Top - 12, b.Right + 6, b.Bottom + 3);
		}
	}
	sealed class WindVegetationRenderable : IRenderable, IFinalizedRenderable, IMedievalShadowCaster
	{
		readonly Sprite sprite;
		readonly float alpha;
		readonly Vector3 tint;
		readonly float sway;
		readonly int2 root;
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;
		public WindVegetationRenderable(Sprite sprite, WPos pos, int z, float alpha, Vector3 tint, float sway, int2 root = default)
		{ this.sprite = sprite; Pos = pos; ZOffset = z; this.alpha = alpha; this.tint = tint; this.sway = sway; this.root = root; }
		public IRenderable WithZOffset(int z) => new WindVegetationRenderable(sprite, Pos, z, alpha, tint, sway, root);
		public IRenderable WithAppearance(Vector3 factor, float opacity) => new WindVegetationRenderable(sprite, Pos, ZOffset, alpha * opacity, tint * factor, sway, root);
		public IRenderable OffsetBy(in WVec offset) => new WindVegetationRenderable(sprite, Pos + offset, ZOffset, alpha, tint, sway, root);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public IRenderable Shadow(SolarShadow light, WorldRenderer wr)
		{
			var height = Math.Max(12, sprite.Size.Y * .5f - sprite.Offset.Y + root.Y);
			var transform = Matrix4x4.CreateTranslation(-root.X, -root.Y, 0);
			var bend = Matrix4x4.Identity;
			bend.M21 = -sway / height;
			var silhouette = new SpriteRenderable(sprite, Pos, WVec.Zero, 0, null, 1, alpha, Vector3.One, TintModifiers.None, false)
				.WithScreenTransform(transform * bend);
			return light.Project(silhouette, wr);
		}
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			var at = wr.ScreenPxPosition(Pos);
			return new Rectangle(at.X + (int)sprite.Offset.X - (int)sprite.Size.X / 2 - 6 - root.X, at.Y + (int)sprite.Offset.Y - (int)sprite.Size.Y / 2 - 3 - root.Y, (int)sprite.Size.X + 12, (int)sprite.Size.Y + 6);
		}
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos);
			var tl = sprite.Offset - sprite.Size * .5f - new Vector3(root.X, root.Y, 0);
			var t = alpha * tint;
			if (wr.TerrainLighting != null) t *= wr.TerrainLighting.TintAt(Pos);
			Vector3 P(float x, float y) => origin + NatureGeometry.Bend(tl + new Vector3(x, y, 0), Math.Max(12, -tl.Y), sway);
			if (MathF.Abs(sway) < .03f || wr.Viewport.Zoom < .65f)
				Game.Renderer.WorldRgbaSpriteRenderer.DrawSprite(sprite, P(0, 0), P(sprite.Size.X, 0), P(sprite.Size.X, sprite.Size.Y), P(0, sprite.Size.Y), t, alpha);
			else foreach (var part in PlantBands.For(sprite).Parts)
				Game.Renderer.WorldRgbaSpriteRenderer.DrawSprite(part.Sprite, P(0, part.Top), P(sprite.Size.X, part.Top),
					P(sprite.Size.X, part.Top + part.Height), P(0, part.Top + part.Height), t, alpha);
		}
	}
}
