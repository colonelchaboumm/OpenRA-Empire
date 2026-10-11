using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class NatureGeometry
	{
		public static Vector3 Ground(float x, float y, float height = 0) => new((x - y) * 24, (x + y) * 12 - height, 0);
		// Root centres, relative to the existing packed image's ground origin.
		public static int2 Root(string type) => type switch
		{
			"md.tree" or "md.pine" or "md.birch" or "md.stump" => new(0, 6),
			"md.shrub" or "md.grasses" => new(0, 4), "md.flowers" => new(0, 3), _ => int2.Zero
		};
		public static Vector3 Bend(Vector3 p, float height, float sway)
		{
			var canopy = MathF.Pow(Math.Clamp((-p.Y - 10) / Math.Max(10, height - 10), 0, 1), 1.5f);
			return p + new Vector3(sway * canopy, sway * canopy * .12f, 0);
		}
		public static Vector3 Fall(Vector3 p, float progress, int direction)
		{
			progress = Math.Clamp(progress, 0, 1);
			var turn = progress * progress;
			// A projected silhouette retains crown volume while rotating toward the
			// 2:1 ground axis. A linear shear crosses a singular, paper-thin pose.
			var angle = direction * turn * MathF.Atan2(2, -1);
			var width = 1 - .35f * turn; var height = 1 - .16f * turn;
			return new Vector3(p.X * width * MathF.Cos(angle) - p.Y * height * MathF.Sin(angle),
				p.X * width * MathF.Sin(angle) + p.Y * height * MathF.Cos(angle), p.Z);
		}
	}
	// Reuse the original texture. Three cached strips bend foliage while roots
	// and the lower trunk stay still; no bitmap copy is performed during rendering.
	sealed class PlantBands
	{
		static readonly ConditionalWeakTable<Sprite, PlantBands> Cache = new();
		public readonly (Sprite Sprite, float Top, float Height)[] Parts;
		PlantBands(Sprite sprite)
		{
			var height = sprite.Bounds.Height; var cuts = new[] { 0, height / 3, height * 2 / 3, height };
			Parts = new (Sprite, float, float)[3];
			var scale = sprite.Size.Y / height;
			for (var i = 0; i < 3; i++)
			{
				var h = cuts[i + 1] - cuts[i];
				Parts[i] = (new Sprite(sprite.Sheet, new Rectangle(sprite.Bounds.X, sprite.Bounds.Y + cuts[i], sprite.Bounds.Width, h), sprite.Channel), cuts[i] * scale, h * scale);
			}
		}
		public static PlantBands For(Sprite sprite) => Cache.GetValue(sprite, s => new PlantBands(s));
	}
	public class MedievalFieldReliefInfo : TraitInfo, Requires<MedievalAgriculturalAreaInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalFieldRelief();
	}
	public sealed class MedievalFieldRelief : IRenderModifier
	{
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			var active = MedievalStockpile.Active(self); var area = self.Trait<MedievalAgriculturalArea>();
			var field = area.Info.Profession == "farmer";
			foreach (var r in renderables)
				// The crop texture is ground. Its animated ears follow immediately,
				// and workers must remain in front of both, even at the rear of a large plot.
				yield return active && field && r is SpriteRenderable && r.Pos == self.CenterPosition ? r.WithZOffset(-8192) : r;
			if (!active || self.World.FogObscures(self)) yield break;
			var crop = self.Trait<MedievalSource>();
			var snow = self.World.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Snow ?? 0;
			yield return new FieldReliefRenderable(self.CenterPosition, area.Size, field, crop.Remaining > 0 && crop.Growth == 0,
				self.World.WorldTick, self.ActorID, snow);
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{ foreach (var b in bounds) yield return new Rectangle(b.X - 2, b.Y - 2, b.Width + 4, b.Height + 6); }
	}
	sealed class FieldReliefRenderable : IRenderable, IFinalizedRenderable
	{
		readonly CVec size;
		readonly bool crop, ripe;
		readonly int tick, snow;
		readonly uint seed;
		public WPos Pos { get; }
		public int ZOffset => -8191;
		public bool IsDecoration => false;
		public FieldReliefRenderable(WPos pos, CVec size, bool crop, bool ripe, int tick, uint seed, int snow)
		{ Pos = pos; this.size = size; this.crop = crop; this.ripe = ripe; this.tick = tick; this.seed = seed; this.snow = snow; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new FieldReliefRenderable(Pos + offset, size, crop, ripe, tick, seed, snow);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); var w = (size.X + size.Y) * 24; return new(p.X - w / 2 - 3, p.Y - w / 4 - 15, w + 6, w / 2 + 23); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos); var cr = Game.Renderer.WorldRgbaColorRenderer;
			Vector3 P(float x, float y, float z = 0) => origin + NatureGeometry.Ground(x - size.X / 2f, y - size.Y / 2f, z);
			var frost = snow > 400;
			var dark = frost ? Color.FromArgb(170, 158, 174, 180) : crop ? Color.FromArgb(220, 57, 45, 27) : Color.FromArgb(170, 44, 62, 34);
			var light = frost ? Color.FromArgb(190, 227, 233, 224) : crop ? Color.FromArgb(150, 135, 103, 60) : Color.FromArgb(130, 120, 135, 72);
			cr.FillRect(P(0, size.Y), P(size.X, size.Y), P(size.X, size.Y, -2.5f), P(0, size.Y, -2.5f), dark);
			cr.FillRect(P(size.X, size.Y), P(size.X, 0), P(size.X, 0, -2.5f), P(size.X, size.Y, -2.5f), dark);
			cr.DrawLine(P(.03f, size.Y - .02f), P(size.X - .02f, size.Y - .02f), .8f, light);
			cr.DrawLine(P(size.X - .02f, size.Y - .02f), P(size.X - .02f, .03f), .8f, light);
			if (!crop || frost) return;
			// Furrows end at the plot margin; their cross sections expose soil depth.
			for (var row = 0; row < size.Y * 4; row++)
			{
				var y = row * .25f + .15f;
				cr.DrawLine(P(size.X - .11f, y, .8f), P(size.X, y, .8f), 1.2f, light);
				cr.DrawLine(P(size.X - .1f, y + .03f), P(size.X, y + .03f, -2), 1, dark);
			}
			if (!ripe || wr.Viewport.Zoom < .8f) return;
			// Sparse articulated ears add depth without redrawing every stem in a large field.
			for (var i = 0; i < Math.Min(36, size.X * size.Y * 2); i++)
			{
				var x = .25f + ((i * 37 + seed % 97) % 101) / 101f * (size.X - .5f);
				var y = .25f + ((i * 61 + seed % 79) % 103) / 103f * (size.Y - .5f);
				var stem = P(x, y); var ear = P(x, y, 7 + i % 3) + new Vector3(MathF.Sin(tick * .07f + x * .7f + y * .4f) * 1.1f, 0, 0);
				cr.DrawLine(stem, ear, .7f, Color.FromArgb(145, 171, 151, 75));
				cr.DrawLine(ear, ear + new Vector3(.5f, 2.7f, 0), 1.3f, Color.FromArgb(170, 228, 210, 143));
			}
		}
	}
}
