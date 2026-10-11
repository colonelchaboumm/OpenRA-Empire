using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct ChurchBellStrip(int X, int Y, int Width, Color Color);
	// Cached native pixel geometry. The arch masks hide the painted stationary
	// bells and clip the larger swing behind the existing stone jambs.
	public static class ChurchBellArt
	{
		static readonly Vector2[] Left = [new(135, 125), new(132, 129), new(132, 142), new(141, 148), new(141, 134), new(138, 128)];
		static readonly Vector2[] Right = [new(158, 125), new(153, 130), new(152, 134), new(152, 147), new(161, 144), new(161, 128)];
		static readonly Color[] Palette = [Color.FromArgb(44, 37, 25), Color.FromArgb(67, 51, 29), Color.FromArgb(127, 93, 41),
			Color.FromArgb(160, 113, 42), Color.FromArgb(211, 159, 61), Color.FromArgb(249, 210, 114), Color.FromArgb(234, 195, 89)];
		static readonly ChurchBellStrip[][] Frames = CreateFrames();
		public static IReadOnlyList<ChurchBellStrip> Frame(int phase) => Frames[phase < 0 ? ChurchBellMotion.Period : phase % ChurchBellMotion.Period];
		static bool Inside(Vector2 p, Vector2[] polygon)
		{
			var inside = false;
			for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
			{
				var a = polygon[i]; var b = polygon[j];
				if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
			}
			return inside;
		}
		static ChurchBellStrip[][] CreateFrames()
		{
			var frames = new ChurchBellStrip[ChurchBellMotion.Period + 1][];
			for (var phase = 0; phase < frames.Length; phase++)
			{
				var angle = phase == ChurchBellMotion.Period ? 0 : ChurchBellMotion.AngleAt(phase);
				var co = MathF.Cos(angle); var si = MathF.Sin(angle);
				var clapper = phase == ChurchBellMotion.Period ? 0 : MathF.Sin((phase - 5) * MathF.Tau / ChurchBellMotion.Period) * -1.8f;
				int Pixel(int x, int y)
				{
					var point = new Vector2(x + .5f, y + .5f);
					var left = x < 147;
					if (!Inside(point, left ? Left : Right)) return -1;
					var dx = point.X - (left ? 136.5f : 156.5f); var dy = point.Y - 130;
					var u = dx * co - dy * si; var v = dx * si + dy * co;
					var color = x % 4 == 0 ? 1 : 0;
					if (v >= 0 && v < 4 && Math.Abs(u) < .6f) color = 2;
					if (v >= 3 && v <= 10.5f && Math.Abs(u) < 1.2f + (v - 3) * .37f)
						color = u < -.6f ? 3 : u < 1.2f ? 5 : 4;
					if (v >= 9.6f && v <= 11 && Math.Abs(u) < 4) color = v > 10.5f ? 2 : 6;
					if (v >= 10.5f && v <= 13 && Math.Abs(u - clapper) < (v > 12 ? 1 : .5f)) color = 6;
					return color;
				}
				var strips = new List<ChurchBellStrip>();
				for (var y = 125; y < 149; y++)
					for (var x = 131; x < 163;)
					{
						var color = Pixel(x, y); var start = x++;
						while (x < 163 && Pixel(x, y) == color) x++;
						if (color >= 0) strips.Add(new(start, y, x - start, Palette[color]));
					}
				frames[phase] = strips.ToArray();
			}
			return frames;
		}
	}
}
