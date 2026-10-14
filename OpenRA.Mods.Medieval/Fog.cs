using System;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class VillageFog
	{
		public const float Remembered = .68f;
		// Feather into the known cell only. Unexplored cells remain fully opaque.
		// Taking the maximum also keeps both sides of a shared edge identical.
		public static float Opacity(float x, float y, ReadOnlySpan<float> neighbors)
		{
			var opacity = neighbors[4];
			for (var row = -1; row <= 1; row++)
				for (var col = -1; col <= 1; col++)
				{
					var level = neighbors[(row + 1) * 3 + col + 1];
					if (level <= opacity) continue;
					var dx = Math.Max(0, Math.Abs(x - col) - .5f);
					var dy = Math.Max(0, Math.Abs(y - row) - .5f);
					var t = Math.Clamp(1 - MathF.Sqrt(dx * dx + dy * dy) / .8f, 0, 1);
					var smooth = t * t * (3 - 2 * t);
					opacity = Math.Max(opacity, level * smooth);
				}
			return opacity;
		}
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalFogInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFog();
	}

	public sealed class MedievalFog : IRenderShroud
	{
		public int PaintedCells { get; private set; }
		public int FeatheredCells { get; private set; }
		static Color Shade(float opacity) => Color.FromArgb((int)MathF.Round(opacity * 255), 10, 17, 24);
		void IRenderShroud.RenderShroud(WorldRenderer wr)
		{
			var world = wr.World; var map = world.Map;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			PaintedCells = FeatheredCells = 0;
			Span<float> neighbors = stackalloc float[9];
			Span<Color> colors = stackalloc Color[25];
			Span<Vector3> points = stackalloc Vector3[25];
			foreach (var uv in wr.Viewport.AllVisibleCells.CandidateMapCoords)
			{
				var cell = uv.ToCPos(map);
				var min = 1f; var max = 0f;
				for (var y = -1; y <= 1; y++)
					for (var x = -1; x <= 1; x++)
					{
						var c = cell + new CVec(x, y);
						var level = !map.Contains(c) || world.ShroudObscures(c) ? 1 : world.FogObscures(c) ? VillageFog.Remembered : 0;
						neighbors[(y + 1) * 3 + x + 1] = level;
						min = Math.Min(min, level); max = Math.Max(max, level);
					}
				if (max == 0) continue;
				PaintedCells++;
				var pos = map.CenterOfCell(cell);
				pos -= new WVec(0, 0, pos.Z);
				var corners = map.Grid.Ramps[0].Corners;
				var a = wr.Screen3DPosition(pos + corners[0]);
				var b = wr.Screen3DPosition(pos + corners[1]);
				var c0 = wr.Screen3DPosition(pos + corners[2]);
				var d = wr.Screen3DPosition(pos + corners[3]);
				if (min == max || neighbors[4] == max)
				{
					cr.FillRect(a, b, c0, d, Shade(neighbors[4]));
					continue;
				}
				FeatheredCells++;
				for (var y = 0; y <= 4; y++)
					for (var x = 0; x <= 4; x++)
					{
						points[y * 5 + x] = a + (b - a) * (x / 4f) + (d - a) * (y / 4f);
						colors[y * 5 + x] = Shade(VillageFog.Opacity(x / 4f - .5f, y / 4f - .5f, neighbors));
					}
				for (var y = 0; y < 4; y++)
					for (var x = 0; x < 4; x++)
					{
						var n = y * 5 + x;
						cr.FillRect(points[n], points[n + 1], points[n + 6], points[n + 5], colors[n], colors[n + 1], colors[n + 6], colors[n + 5]);
					}
			}
		}
	}
}
