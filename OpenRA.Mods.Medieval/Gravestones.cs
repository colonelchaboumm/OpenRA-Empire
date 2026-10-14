using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	sealed class GravestoneRenderable(WPos position, int seed) : IRenderable, IFinalizedRenderable
	{
		// Fixed outlines and a hash of the buried actor's identity: no random state,
		// frame-dependent variation or extra save data is needed for these decorations.
		static readonly Vector2[][] Outlines =
		[
			[new(-.5f, 0), new(-.5f, 1), new(.5f, 1), new(.5f, 0)],
			[new(-.5f, 0), new(-.5f, .72f), new(-.42f, .88f), new(-.23f, .98f), new(0, 1.02f), new(.23f, .98f), new(.42f, .88f), new(.5f, .72f), new(.5f, 0)],
			[new(-.5f, 0), new(-.5f, .76f), new(0, 1.16f), new(.5f, .76f), new(.5f, 0)],
			[new(-.48f, 0), new(-.6f, .36f), new(-.36f, .9f), new(.1f, 1), new(.42f, .77f), new(.52f, .14f), new(.38f, 0)],
			// Crosses use three convex sections, avoiding overlapping front faces.
			[new(-.16f, 0), new(-.16f, .58f), new(.16f, .58f), new(.16f, 0)],
			[new(-.5f, .58f), new(-.5f, .77f), new(.5f, .77f), new(.5f, .58f)],
			[new(-.16f, .77f), new(-.16f, 1), new(.16f, 1), new(.16f, .77f)]
		];
		static readonly Color[] StoneColors =
		[
			Color.FromArgb(255, 177, 173, 152), // Limestone.
			Color.FromArgb(255, 139, 148, 150), // Blue-grey stone.
			Color.FromArgb(255, 164, 133, 103), // Sandstone.
			Color.FromArgb(255, 119, 122, 111)  // Dark, weathered stone.
		];
		public WPos Pos => position;
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new GravestoneRenderable(position + offset, seed);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new Rectangle(p.X - 18, p.Y - 28, 36, 42); }
		public void Render(WorldRenderer wr)
		{
			var hash = unchecked((uint)seed * 0x9E3779B9u); hash ^= hash >> 16;
			var shape = (int)(hash % 6); var wood = shape == 5;
			var width = 6.5f + (hash >> 16) % 4;
			var height = 10 + (hash >> 20) % 4 * 1.2f + (shape >= 4 ? 4 : 0);
			var lean = ((int)((hash >> 28) % 3) - 1) * .55f;
			var front = wood ? Color.FromArgb(255, 117 + (int)(hash % 3) * 9, 85, 49) : StoneColors[(hash >> 8) % 4];
			var side = Color.FromArgb(255, front.R * 58 / 100, front.G * 60 / 100, front.B * 62 / 100);
			var light = Color.FromArgb(255, front.R + 35, front.G + 33, front.B + 28);
			var ink = Color.FromArgb(210, front.R * 52 / 100, front.G * 55 / 100, front.B * 54 / 100);
			var cr = Game.Renderer.WorldRgbaColorRenderer; var origin = wr.Screen3DPxPosition(Pos);
			Vector3 V(float x, float y) => origin + new Vector3(x, y, 0);
			Vector3 Face(Vector2 v) => V(-2 + v.X * width + v.Y * lean, -3 - v.Y * height - v.X * width * .28f);
			var depth = new Vector3(wood ? 1.1f : 2.1f, wood ? .6f : 1.2f, 0);
			cr.FillRect(V(-10, 1), V(1, -4), V(12, 2), V(1, 8), Color.FromArgb(130, 58, 46, 29));
			cr.FillRect(V(-8, 1), V(1, -3), V(10, 2), V(1, 6), Color.FromArgb(230, 102, 82, 52));
			cr.FillRect(V(-5, -4), V(3, -3), V(12, 2), V(4, 3), Color.FromArgb(70, 20, 22, 18));
			if (shape is 0 or 4)
			{
				cr.FillRect(V(-7, -3), V(1, -5), V(5, -2), V(-3, 0), side);
				cr.DrawLine(V(-7, -3), V(1, -5), 1, light);
			}
			void Plate(Vector2[] outline)
			{
				for (var i = 0; i < outline.Length; i++)
				{
					var a = Face(outline[i]); var b = Face(outline[(i + 1) % outline.Length]);
					cr.FillRect(a, a + depth, b + depth, b, side);
				}
				// A degenerate quad emits one triangle for each fan segment.
				for (var i = 1; i < outline.Length - 1; i++)
					cr.FillRect(Face(outline[0]), Face(outline[i]), Face(outline[i + 1]), Face(outline[i + 1]), front);
				for (var i = 0; i < outline.Length - 1; i++)
					if (outline[i].Y >= .7f || outline[i + 1].Y >= .7f)
						cr.DrawLine(Face(outline[i]), Face(outline[i + 1]), .65f, light);
			}
			if (shape < 4) Plate(Outlines[shape]);
			else { Plate(Outlines[4]); Plate(Outlines[5]); Plate(Outlines[6]); }
			void Mark(float ax, float ay, float bx, float by) => cr.DrawLine(Face(new Vector2(ax, ay)), Face(new Vector2(bx, by)), .8f, ink);
			if (wood)
			{
				Mark(-.05f, .1f, -.05f, .53f); Mark(.07f, .81f, .07f, .95f);
				Mark(-.39f, .65f, .4f, .65f); Mark(-.04f, .64f, .04f, .7f);
			}
			else if (shape >= 4) { Mark(0, .17f, 0, .47f); Mark(-.08f, .37f, .08f, .37f); }
			else if (shape == 3)
			{
				Mark(.12f, .81f, -.05f, .56f); Mark(-.05f, .56f, .06f, .36f);
				Mark(-.05f, .56f, -.25f, .5f);
			}
			else if ((hash >> 24) % 2 == 0)
			{
				Mark(0, .35f, 0, .75f); Mark(-.2f, .6f, .2f, .6f);
				Mark(-.23f, .19f, .21f, .19f);
			}
			else
			{
				Mark(-.25f, .66f, .25f, .66f); Mark(-.3f, .46f, .28f, .46f); Mark(-.17f, .26f, .17f, .26f);
			}
			if (!wood && (hash >> 12) % 3 == 0)
				cr.DrawLine(Face(new Vector2(-.35f, .07f)), Face(new Vector2(.23f, .06f)), 1.4f, Color.FromArgb(165, 81, 93, 49));
		}
	}
}
