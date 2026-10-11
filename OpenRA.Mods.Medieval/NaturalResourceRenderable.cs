using System;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	// The standing artwork is transformed in the renderer, around its ground anchor.
	// No random simulation state, harvest or inventory update happens while drawing.
	sealed class NaturalResourceRenderable : IRenderable, IFinalizedRenderable, IMedievalShadowCaster
	{
		readonly Sprite sprite;
		readonly Sprite remnant;
		readonly float alpha;
		readonly Vector3 tint;
		readonly NaturalResourceKind kind;
		readonly float fraction;
		readonly float fall;
		readonly int impact;
		readonly uint seed;
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;
		public NaturalResourceRenderable(Sprite sprite, Sprite remnant, WPos pos, int z, float alpha, Vector3 tint,
			NaturalResourceKind kind, float fraction, float fall, int impact, uint seed)
		{
			this.sprite = sprite; this.remnant = remnant; Pos = pos; ZOffset = z; this.alpha = alpha;
			this.tint = tint; this.kind = kind; this.fraction = fraction; this.fall = fall; this.impact = impact; this.seed = seed;
		}
		public IRenderable WithZOffset(int z) => new NaturalResourceRenderable(sprite, remnant, Pos, z, alpha, tint, kind, fraction, fall, impact, seed);
		public IRenderable OffsetBy(in WVec offset) => new NaturalResourceRenderable(sprite, remnant, Pos + offset, ZOffset, alpha, tint, kind, fraction, fall, impact, seed);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public IRenderable Shadow(SolarShadow light, WorldRenderer wr)
		{
			var image = sprite;
			var transform = Matrix4x4.Identity;
			var opacity = alpha;
			if (kind == NaturalResourceKind.Stone && fraction > 0)
			{
				var scale = MathF.Pow(fraction, .55f);
				transform = Matrix4x4.CreateScale(scale, scale, 1) * Matrix4x4.CreateTranslation(0, 10 * (1 - scale), 0);
			}
			else if (kind == NaturalResourceKind.Tree && fall >= 0)
			{
				var progress = Math.Clamp(fall / .8f, 0, 1); var turn = progress * progress;
				var angle = (seed % 2 == 0 ? 1 : -1) * turn * MathF.Atan2(2, -1);
				transform = Matrix4x4.CreateTranslation(0, -6, 0) * Matrix4x4.CreateScale(1 - .35f * turn, 1 - .16f * turn, 1)
					* Matrix4x4.CreateRotationZ(angle);
				opacity *= Math.Clamp((1 - fall) * 5, 0, 1);
			}
			else
			{
				image = remnant;
				if (kind == NaturalResourceKind.Tree) transform = Matrix4x4.CreateTranslation(0, -6, 0);
			}
			return light.Project(new SpriteRenderable(image, Pos, WVec.Zero, 0, null, 1, opacity, Vector3.One, TintModifiers.None, false)
				.WithScreenTransform(transform), wr);
		}
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			var at = wr.ScreenPxPosition(Pos);
			return new Rectangle(at.X - 150, at.Y - 120, 300, 220);
		}
		void DrawSprite(WorldRenderer wr, Sprite image, Vector3 ground, Func<Vector3, Vector3> transform, float opacity = 1)
		{
			var tl = image.Offset - image.Size * .5f - new Vector3(0, kind == NaturalResourceKind.Tree ? 6 : 0, 0);
			var tr = tl + new Vector3(image.Size.X, 0, 0);
			var bl = tl + new Vector3(0, image.Size.Y, 0);
			var br = tl + new Vector3(image.Size.X, image.Size.Y, 0);
			var color = tint * alpha * opacity;
			if (wr.TerrainLighting != null) color *= wr.TerrainLighting.TintAt(Pos);
			Game.Renderer.WorldRgbaSpriteRenderer.DrawSprite(image, ground + transform(tl), ground + transform(tr),
				ground + transform(br), ground + transform(bl), color, alpha * opacity);
		}
		Color Color(int r, int g, int b, float opacity = 1) => Primitives.Color.FromArgb((int)(Math.Clamp(alpha * opacity, 0, 1) * 255), r, g, b);
		public void Render(WorldRenderer wr)
		{
			var at = wr.Screen3DPxPosition(Pos);
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			if (kind == NaturalResourceKind.Stone)
			{
				DrawSprite(wr, remnant, at, p => p);
				if (fraction > 0)
				{
					var scale = MathF.Pow(fraction, .55f);
					DrawSprite(wr, sprite, at, p => new Vector3(p.X * scale, 10 + (p.Y - 10) * scale, p.Z));
				}
				if (impact > 0) DrawDebris(at, (12 - impact) / 12f, false);
				return;
			}

			var direction = seed % 2 == 0 ? 1 : -1;
			if (fall >= 0)
			{
				// Slow initial lean, accelerating fall, then a short impact and settling.
				var progress = Math.Clamp(fall / .8f, 0, 1);
				var angle = progress * progress * MathF.PI / 2;
				var lean = MathF.Sin(angle);
				var shadow = at + new Vector3(direction * lean * 30, lean * 12 + 6, 0);
				cr.FillEllipse(shadow - new Vector3(24 + lean * 20, 6 + lean * 5, 0), shadow + new Vector3(24 + lean * 20, 6 + lean * 5, 0), Color(23, 28, 20, .18f));
				if (fall > .8f)
				{
					DrawSprite(wr, remnant, at, p => p);
					DrawTrunk(at, direction, (fall - .8f) * 5);
				}
				DrawSprite(wr, sprite, at, p => NatureGeometry.Fall(p, progress, direction), Math.Clamp((1 - fall) * 5, 0, 1));
				if (fall > .72f) DrawDebris(at + new Vector3(direction * 44, 22, 0), (fall - .72f) / .28f, true);
			}
			else
			{
				DrawSprite(wr, remnant, at, p => p);
				if (fraction > 0) DrawTrunk(at, direction, 1);
				if (impact > 0) DrawDebris(at + new Vector3(direction * 13, 8, 0), (12 - impact) / 12f, true);
			}
		}
		void DrawTrunk(Vector3 at, int direction, float opacity)
		{
			if (fraction <= 0) return;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var start = at + new Vector3(direction * 8, 6, 0);
			var end = start + new Vector3(direction * (9 + fraction * 54), (9 + fraction * 54) * .5f, 0);
			cr.DrawLine(start + new Vector3(1, 5, 0), end + new Vector3(1, 5, 0), 9, Color(29, 31, 20, opacity * .25f));
			cr.DrawLine(start, end, 11, Color(50, 39, 24, opacity));
			cr.DrawLine(start + new Vector3(0, -1, 0), end + new Vector3(0, -1, 0), 8, Color(113, 78, 42, opacity));
			cr.DrawLine(start + new Vector3(0, -3, 0), end + new Vector3(0, -3, 0), 2, Color(164, 124, 66, opacity));
			cr.DrawLine(start + new Vector3(0, 2, 0), end + new Vector3(0, 2, 0), 1, Color(75, 53, 31, opacity));
			cr.FillEllipse(end - new Vector3(3, 5, 0), end + new Vector3(3, 4, 0), Color(191, 157, 92, opacity));
			cr.FillEllipse(end - new Vector3(1.5f, 3, 0), end + new Vector3(1.5f, 2, 0), Color(142, 105, 54, opacity));
			for (var i = 1; i <= (int)(fraction * 4); i++)
			{
				var joint = Vector3.Lerp(start, end, i / 5f);
				var tip = joint + new Vector3(direction * 5, i % 2 == 0 ? -9 : 8, 0);
				cr.DrawLine(joint, tip, 3, Color(84, 58, 32, opacity));
				cr.DrawLine(joint - new Vector3(0, 1, 0), tip, 1, Color(157, 119, 64, opacity));
			}
		}
		void DrawDebris(Vector3 at, float age, bool wood)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			for (var i = 0; i < 12; i++)
			{
				var angle = (i * 2.4f + seed % 17);
				var reach = age * (12 + i * 2);
				var p = at + new Vector3(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach * .5f - MathF.Sin(age * MathF.PI) * (7 + i), 0);
				var color = wood ? Color(170, 135, 67, 1 - age) : Color(177, 173, 145, 1 - age);
				cr.FillRect(p - new Vector3(1.3f, .8f, 0), p + new Vector3(1.3f, .8f, 0), color);
			}
			var dust = at + new Vector3(0, -age * 9, 0);
			cr.FillEllipse(dust - new Vector3(12 + age * 16, 4 + age * 4, 0), dust + new Vector3(12 + age * 16, 4 + age * 4, 0), Color(175, 157, 110, (1 - age) * .13f));
		}
	}
}
