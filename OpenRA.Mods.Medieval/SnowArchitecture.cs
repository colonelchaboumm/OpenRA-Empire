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
	// Registered to the existing sprite anchors. Geometry is cached per pose, never
	// generated for every building on the map each frame. No simulation state changes.
	public class MedievalSnowCoverInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSnowCover();
	}
	public sealed class MedievalSnowCover : IRenderModifier
	{
		WithSpriteBody body;
		SnowSurface[] surfaces;
		SnowSurface[] groundSurfaces;
		int pose = -1;
		public int SurfaceCount => surfaces?.Length ?? 0;
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var r in renderables) yield return r;
			var snow = self.World.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Snow ?? 0;
			if (snow == 0 || !MedievalStockpile.Active(self) || self.World.FogObscures(self)) yield break;
			body ??= self.TraitsImplementing<WithSpriteBody>().FirstOrDefault(b => b.Info.Name == "body");
			var frame = body?.DefaultAnimation.CurrentFrame ?? 0;
			if (self.Info.Name != "md.house" && !self.Info.Name.StartsWith("md.palisade", StringComparison.Ordinal)
				&& !self.Info.Name.StartsWith("md.gardenfence", StringComparison.Ordinal) && !self.Info.Name.StartsWith("md.gildedfence", StringComparison.Ordinal)
				&& !self.Info.Name.StartsWith("md.woodgate", StringComparison.Ordinal)) frame = 0;
			if (surfaces == null || pose != frame)
			{
				pose = frame;
				var projection = MedievalArchitecture.For(self);
				var geometry = SnowGeometry.For(self.Info.Name, frame, self.Info.TraitInfo<BuildingInfo>().Dimensions)
					.Select(s => s with { A = projection.Project(s.A), B = projection.Project(s.B), C = projection.Project(s.C), D = projection.Project(s.D) }).ToArray();
				surfaces = geometry.Where(s => !s.Ground).ToArray();
				groundSurfaces = geometry.Where(s => s.Ground).ToArray();
			}
			if (groundSurfaces.Length > 0)
				yield return new MedievalGroundLayer(new SnowCoverRenderable(self.CenterPosition, 0, groundSurfaces, snow), 1);
			if (surfaces.Length > 0)
				yield return new SnowCoverRenderable(self.CenterPosition, (body?.DefaultAnimation.CurrentSequence.ZOffset ?? 0) + 1, surfaces, snow);
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => bounds;
	}
	public readonly record struct SnowSurface(Vector3 A, Vector3 B, Vector3 C, Vector3 D, bool Rail = false, bool Ground = false);
	public static class SnowGeometry
	{
		static SnowSurface Roof(int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy, int anchorX = 92, int anchorY = 140)
			=> new(new(ax - anchorX, ay - anchorY, 0), new(bx - anchorX, by - anchorY, 0), new(cx - anchorX, cy - anchorY, 0), new(dx - anchorX, dy - anchorY, 0));
		static SnowSurface Rail(Vector3 a, Vector3 b) => new(a, b, b, a, true);
		static Vector3 P(float x, float y, float z = 0) => new(24 * (x - y), 12 * (x + y) - z, 0);
		public static SnowSurface[] For(string name, int frame, CVec size)
		{
			if (name.StartsWith("md.pasture", StringComparison.Ordinal) || name.StartsWith("md.sheeppasture", StringComparison.Ordinal))
			{
				var rails = new List<SnowSurface>();
				Vector3 Point(float x, float y, float z) => P(x - size.X / 2f, y - size.Y / 2f, z);
				// Full ground coverage, including the strip directly under the fence.
				rails.Add(new(Point(.02f, .02f, 0), Point(size.X - .02f, .02f, 0), Point(size.X - .02f, size.Y - .02f, 0), Point(.02f, size.Y - .02f, 0), Ground: true));
				void Fence(float ax, float ay, float bx, float by)
				{
					rails.Add(Rail(Point(ax, ay, 11), Point(bx, by, 11)));
					var count = (int)Math.Ceiling((Math.Abs(bx - ax) + Math.Abs(by - ay)) * 2);
					for (var i = 0; i <= count; i++)
					{
						var p = Point(ax + (bx - ax) * i / Math.Max(1, count), ay + (by - ay) * i / Math.Max(1, count), 14);
						rails.Add(Rail(p - Vector3.UnitX, p + Vector3.UnitX));
					}
				}
				Fence(.08f, .08f, size.X - .08f, .08f); Fence(.08f, .08f, .08f, size.Y - .08f);
				Fence(size.X - .08f, .08f, size.X - .08f, size.Y - .08f);
				var gate = size.X / 2;
				Fence(.08f, size.Y - .08f, gate, size.Y - .08f);
				if (gate + 1 < size.X - .08f) Fence(gate + 1, size.Y - .08f, size.X - .08f, size.Y - .08f);
				Fence(gate, size.Y - .08f, gate + .55f, size.Y - .7f);
				return rails.ToArray();
			}
			var iron = name.StartsWith("md.gildedfence", StringComparison.Ordinal) || name.StartsWith("md.gildedgate", StringComparison.Ordinal);
			if (iron || name.StartsWith("md.garden", StringComparison.Ordinal) || name.StartsWith("md.palisade", StringComparison.Ordinal))
			{
				var rails = new List<SnowSurface>();
				var wall = name.StartsWith("md.palisade", StringComparison.Ordinal);
				var gate = name.StartsWith("md.gardengate", StringComparison.Ordinal) || name.StartsWith("md.gildedgate", StringComparison.Ordinal);
				var railHeight = iron ? 15 : 13;
				var capHeight = iron ? 25 : 18;
				var alongY = name.EndsWith('y');
				if (gate)
				{
					Vector3 G(float t, float h) => P(alongY ? 0 : t, alongY ? t : 0, h);
					rails.Add(Rail(G(-.5f, railHeight), G(-.27f, railHeight)));
					foreach (var t in new[] { -.48f, .48f }) rails.Add(Rail(G(t, capHeight) - Vector3.UnitX, G(t, capHeight) + Vector3.UnitX));
					return rails.ToArray();
				}
				var mask = frame == 0 ? wall ? alongY ? 5 : 10 : alongY ? 12 : 3 : frame;
				CVec[] arms = wall ? [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)] : [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
				for (var i = 0; i < 4; i++)
				{
					if ((mask & (1 << i)) == 0) continue;
					if (wall)
					{
						for (var j = 0; j <= 4; j++)
						{
							var x = arms[i].X * j / 8f; var y = arms[i].Y * j / 8f;
							var h = 34 + ((int)MathF.Round((x - y) * 16) % 3 + 3) % 3;
							var p = P(x, y, h);
							rails.Add(Rail(p + new Vector3(-2, 2, 0), p)); rails.Add(Rail(p, p + new Vector3(2, 2, 0)));
						}
					}
					else
					{
						var end = P(arms[i].X * .5f, arms[i].Y * .5f, railHeight);
						rails.Add(Rail(P(0, 0, railHeight), end));
						var cap = end - new Vector3(0, capHeight - railHeight, 0); rails.Add(Rail(cap - Vector3.UnitX, cap + Vector3.UnitX));
					}
				}
				if (!wall) rails.Add(Rail(new(-1, -capHeight, 0), new(1, -capHeight, 0)));
				return rails.ToArray();
			}
			if (name is "md.woodgate" or "md.woodgatey")
			{
				var rails = new List<SnowSurface>(); var angle = Math.Clamp(frame, 0, 8) * MathF.PI / 16;
				Vector3 G(float t, float depth, float h) => name.EndsWith('y') ? P(depth, t, h) : P(t, depth, h);
				foreach (var side in new[] { -1, 1 })
				{
					rails.Add(Rail(G(side * 1.4f, 0, 41), G(side * (1.4f - 1.37f * MathF.Cos(angle)), 1.37f * MathF.Sin(angle), 41)));
					var cap = G(side * 1.45f, 0, 49); rails.Add(Rail(cap + new Vector3(-2, 1, 0), cap + new Vector3(2, 1, 0)));
				}
				return rails.ToArray();
			}
			// Roof planes use the untouched 184px plates. Leave chimneys, dormers,
			// animated mechanisms and heraldic canvas edges visible.
			return name switch
			{
				"md.house" => frame switch
				{
					0 => [Roof(94, 96, 105, 90, 129, 112, 99, 129), Roof(57, 115, 81, 101, 85, 103, 62, 117)],
					1 => [Roof(80, 99, 109, 109, 104, 129, 70, 115)],
					2 => [Roof(86, 40, 89, 32, 119, 48, 108, 59), Roof(88, 43, 99, 54, 103, 77, 96, 80), Roof(111, 61, 120, 56, 126, 69, 116, 75)],
					3 => [Roof(62, 81, 98, 94, 81, 118, 48, 100), Roof(106, 100, 128, 109, 114, 133, 90, 121)],
					4 => [Roof(73, 87, 100, 95, 117, 107, 111, 123), Roof(74, 90, 109, 105, 108, 120, 60, 103)],
					_ => [Roof(67, 61, 104, 78, 99, 106, 77, 77), Roof(46, 84, 65, 67, 68, 70, 50, 87)]
				},
				"md.warehouse" => [Roof(59, 79, 122, 104, 98, 139, 31, 112)],
				"md.sawmill" => [Roof(105, 76, 139, 108, 99, 128, 72, 95), Roof(39, 101, 92, 79, 94, 82, 42, 103)],
				"md.market" or "md.foundingmarket" => [Roof(52, 87, 87, 67, 99, 108, 58, 96), Roof(90, 68, 125, 89, 101, 108, 101, 108)],
				"md.marketstall" => [Roof(82, 81, 126, 103, 102, 121, 58, 100)],
				"md.farm" => [Roof(91, 105, 126, 117, 117, 137, 85, 125)],
				"md.ranch" => [Roof(46, 67, 121, 94, 106, 132, 23, 101)],
				"md.forge" => [Roof(65, 72, 111, 90, 80, 120, 32, 101)],
				"md.barracks" => [Roof(62, 51, 114, 68, 109, 78, 78, 64), Roof(84, 71, 130, 89, 112, 106, 105, 100), Roof(37, 83, 73, 66, 77, 69, 41, 85)],
				"md.sheepfold" => [Roof(51, 62, 121, 92, 112, 125, 63, 80), Roof(33, 94, 44, 73, 48, 75, 38, 96), Roof(133, 110, 159, 126, 139, 136, 117, 120)],
				"md.tentweaver" => [Roof(43, 61, 80, 75, 72, 89, 55, 87), Roof(27, 88, 43, 64, 58, 74, 42, 98), Roof(83, 77, 126, 96, 113, 128, 78, 108)],
				"md.huntinglodge" => [Roof(80, 65, 105, 80, 75, 117, 44, 99), Roof(106, 83, 135, 108, 138, 106, 110, 81),
					Roof(80, 118, 126, 103, 133, 108, 89, 123)],
				"md.siegeworkshop" => [Roof(81, 71, 177, 119, 162, 141, 65, 93, 112, 164)],
				"md.militarytent" => [Roof(89, 76, 88, 116, 59, 106, 59, 106), Roof(92, 77, 118, 108, 101, 117, 93, 116)],
				"md.well" => [Roof(40, 33, 56, 41, 51, 49, 36, 41, 48, 84)],
				_ => []
			};
		}
	}
	sealed class SnowCoverRenderable : IRenderable, IFinalizedRenderable
	{
		readonly SnowSurface[] surfaces;
		readonly int snow;
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;
		public SnowCoverRenderable(WPos pos, int z, SnowSurface[] surfaces, int snow) { Pos = pos; ZOffset = z; this.surfaces = surfaces; this.snow = snow; }
		public IRenderable WithZOffset(int z) => new SnowCoverRenderable(Pos, z, surfaces, snow);
		public IRenderable OffsetBy(in WVec offset) => new SnowCoverRenderable(Pos + offset, ZOffset, surfaces, snow);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 256, p.Y - 256, 512, 512); }
		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer; var origin = wr.Screen3DPxPosition(Pos);
			var alpha = snow / 1000f;
			foreach (var s in surfaces)
			{
				if (s.Ground)
				{
					var heat = wr.World.WorldActor.TraitOrDefault<MedievalFireEnvironment>()?.Heat;
					float Remaining(Vector3 point) => heat?.SnowRemaining(wr.ProjectedPosition(int2.FromVector(origin + point))) ?? 1;
					Color Ground(Vector3 point) => Color.FromArgb((int)(244 * alpha * Remaining(point)), 233, 240, 244);
					// An enclosure is one large plate. Subdivide it so a fire near its
					// middle thaws locally instead of only changing its four corners.
					var columns = heat?.Count > 0 ? Math.Clamp((int)Math.Ceiling((s.B - s.A).Length() / 24), 1, 12) : 1;
					var rows = heat?.Count > 0 ? Math.Clamp((int)Math.Ceiling((s.D - s.A).Length() / 24), 1, 12) : 1;
					Vector3 Point(float x, float y) => Vector3.Lerp(Vector3.Lerp(s.A, s.B, x), Vector3.Lerp(s.D, s.C, x), y);
					for (var row = 0; row < rows; row++)
						for (var column = 0; column < columns; column++)
						{
							var a = Point(column / (float)columns, row / (float)rows);
							var b = Point((column + 1f) / columns, row / (float)rows);
							var c = Point((column + 1f) / columns, (row + 1f) / rows);
							var d = Point(column / (float)columns, (row + 1f) / rows);
							cr.FillRect(origin + a, origin + b, origin + c, origin + d, Ground(a), Ground(b), Ground(c), Ground(d));
						}
					var center = (s.A + s.B + s.C + s.D) / 4;
					Vector3[] rim = [s.A, s.B, s.C, s.D];
					for (var i = 0; i < 4; i++)
					{
						var a = rim[i]; var b = rim[(i + 1) % 4];
						cr.FillRect(origin + a, origin + b, origin + Vector3.Lerp(b, center, .08f), origin + Vector3.Lerp(a, center, .08f), Color.FromArgb((int)(120 * alpha * Math.Min(Remaining(a), Remaining(b))), 247, 251, 253));
					}
					continue;
				}
				if (s.Rail)
				{
					cr.DrawLine(origin + s.A + new Vector3(0, 1, 0), origin + s.B + new Vector3(0, 1, 0), 2.8f, Color.FromArgb((int)(160 * alpha), 146, 171, 191));
					cr.DrawLine(origin + s.A, origin + s.B, 1.7f + alpha, Color.FromArgb((int)(244 * alpha), 238, 246, 251));
					continue;
				}
				// Translucent snow preserves roof texture; deterministic short strips
				// form irregular drifts instead of turning the plate into a white tile.
				cr.FillRect(origin + s.A, origin + s.B, origin + s.C, origin + s.D, Color.FromArgb((int)(182 * alpha), 222, 234, 244));
				for (var i = 0; i < 8; i++)
				{
					var t = (i + .3f) / 8;
					var left = Vector3.Lerp(s.A, s.D, t); var right = Vector3.Lerp(s.B, s.C, t);
					var a = Vector3.Lerp(left, right, .06f + (i * 7 % 5) * .03f);
					var b = Vector3.Lerp(left, right, .83f + (i * 3 % 4) * .03f);
					if ((b - a).LengthSquared() < 1) continue;
					cr.DrawLine(origin + a, origin + b, 1.1f + alpha, Color.FromArgb((int)(115 * alpha), 248, 251, 253));
				}
				if ((s.B - s.A).LengthSquared() > 1) cr.DrawLine(origin + s.A, origin + s.B, 2.2f, Color.FromArgb((int)(228 * alpha), 241, 248, 252));
				if ((s.C - s.D).LengthSquared() > 1) cr.DrawLine(origin + s.D, origin + s.C, 1.5f, Color.FromArgb((int)(220 * alpha), 226, 237, 247));
				if (snow < 700 || (s.C - s.D).LengthSquared() < 160) continue;
				for (var i = 1; i < 4; i++)
				{
					var icicle = origin + Vector3.Lerp(s.D, s.C, i * .23f);
					cr.DrawLine(icicle, icicle + new Vector3(0, 1.5f + i % 2, 0), .8f, Color.FromArgb((int)(150 * alpha), 215, 237, 250));
				}
			}
		}
	}
}
