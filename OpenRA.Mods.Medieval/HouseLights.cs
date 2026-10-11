using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct HouseWindow(Vector3 A, Vector3 B, Vector3 C, Vector3 D)
	{
		public Vector3 Center => (A + B + C + D) / 4;
	}
	// Coordinates measured inside the existing 184px house frames, anchor (92,140).
	// Only the glass is illuminated; timber frames and the architecture stay intact.
	public static class HouseWindowLayout
	{
		static HouseWindow Pane(float x, float y, float width, float height, float slope = .5f)
		{
			var a = new Vector3(x - 92, y - 140, 0); var b = a + new Vector3(width, width * slope, 0);
			return new(a, b, b + new Vector3(0, height, 0), a + new Vector3(0, height, 0));
		}
		static readonly HouseWindow[][] Styles = [
			[Pane(63, 126, 3, 7), Pane(87, 137, 3, 7), Pane(106, 133, 3, 8, -.5f), Pane(115, 128, 3, 8, -.5f)],
			[Pane(64, 124, 3, 6), Pane(86, 134, 3, 6), Pane(112, 132, 3, 6, -.5f)],
			[Pane(69, 78, 3, 8), Pane(82, 84, 3, 8), Pane(72, 102, 3, 8), Pane(84, 110, 3, 8),
				Pane(73, 128, 3, 8), Pane(108, 85, 3, 8, -.5f), Pane(108, 109, 3, 8, -.5f), Pane(108, 134, 3, 8, -.5f)],
			[Pane(47, 110, 3, 7), Pane(58, 116, 3, 7), Pane(71, 121, 3, 7), Pane(47, 131, 3, 7), Pane(71, 140, 3, 8),
				Pane(97, 139, 3, 7), Pane(106, 142, 3, 8), Pane(124, 136, 3, 8, -.5f), Pane(136, 131, 3, 7, -.5f)],
			[Pane(72, 112, 3, 7), Pane(96, 125, 3, 7), Pane(124, 120, 4, 8, -.5f)],
			[Pane(61, 94, 4, 8), Pane(84, 104, 4, 8), Pane(111, 106, 5, 9, -.5f), Pane(119, 102, 4, 9, -.5f),
				Pane(61, 121, 4, 8), Pane(84, 134, 4, 7), Pane(113, 133, 5, 9, -.5f), Pane(121, 129, 4, 9, -.5f)]
		];
		public static IReadOnlyList<HouseWindow> For(int style) => Styles[Math.Clamp(style, 0, Styles.Length - 1)];
		public static float Intensity(float darkness, uint id) => Math.Clamp((darkness - .12f - id % 7 * .025f) * 1.5f, 0, 1);
	}
	public class MedievalHouseLightsInfo : TraitInfo, Requires<MedievalHouseAppearanceInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalHouseLights(init.Self.Trait<MedievalHouseAppearance>().Style);
	}
	// Pure presentation, with no shared RNG, economy changes or per-tick work.
	public sealed class MedievalHouseLights : IRenderModifier
	{
		public readonly IReadOnlyList<HouseWindow> Windows;
		public readonly Vector3 Center;
		public MedievalHouseLights(int style)
		{
			var projection = MedievalArchitecture.For("md.house", style);
			Windows = HouseWindowLayout.For(style).Select(w => new HouseWindow(projection.Project(w.A), projection.Project(w.B), projection.Project(w.C), projection.Project(w.D))).ToArray();
			Center = Windows.Aggregate(Vector3.Zero, (sum, window) => sum + window.Center) / Windows.Count;
		}
		public static bool Visible(Actor self) => MedievalStockpile.Active(self) && self.TraitOrDefault<MedievalBurnable>()?.Burning != true
			&& !self.World.FogObscures(self) && (self.World.RenderPlayer == null || self.CanBeViewedByPlayer(self.World.RenderPlayer));
		public static float Brightness(Actor self) => Visible(self)
			? HouseWindowLayout.Intensity(self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.Darkness ?? 0, self.ActorID) : 0;
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var r in renderables) yield return r;
			var intensity = Brightness(self);
			if (intensity > 0) yield return new HouseLightsRenderable(self.CenterPosition, 2, Windows, intensity, self.ActorID);
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => bounds;
	}
	sealed class HouseLightsRenderable : IRenderable, IFinalizedRenderable
	{
		readonly IReadOnlyList<HouseWindow> windows;
		readonly float intensity;
		readonly uint seed;
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;
		public HouseLightsRenderable(WPos pos, int z, IReadOnlyList<HouseWindow> windows, float intensity, uint seed)
		{ Pos = pos; ZOffset = z; this.windows = windows; this.intensity = intensity; this.seed = seed; }
		public IRenderable WithZOffset(int z) => new HouseLightsRenderable(Pos, z, windows, intensity, seed);
		public IRenderable OffsetBy(in WVec offset) => new HouseLightsRenderable(Pos + offset, ZOffset, windows, intensity, seed);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 80, p.Y - 130, 160, 170); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos); var cr = Game.Renderer.WorldRgbaColorRenderer;
			var flicker = .96f + .04f * MathF.Sin(wr.World.WorldTick * .055f + seed % 31);
			foreach (var window in windows)
			{
				var center = origin + window.Center;
				for (var radius = 9; radius >= 5; radius -= 2)
				{
					var spread = new Vector3(radius, radius * .8f, 0);
					cr.FillEllipse(center - spread, center + spread, Color.FromArgb((int)(7 * intensity), 255, 172, 66));
				}
				var a = origin + window.A; var b = origin + window.B; var c = origin + window.C; var d = origin + window.D;
				var top = Color.FromArgb((int)(245 * intensity * flicker), 255, 177, 64);
				var bottom = Color.FromArgb((int)(250 * intensity * flicker), 255, 222, 126);
				cr.FillRect(a, b, c, d, top, top, bottom, bottom);
				var bars = Color.FromArgb((int)(215 * intensity), 104, 65, 29);
				cr.DrawLine((a + b) / 2, (c + d) / 2, .65f, bars);
				cr.DrawLine(Vector3.Lerp(a, d, .46f), Vector3.Lerp(b, c, .46f), .65f, bars);
			}
		}
	}
}
