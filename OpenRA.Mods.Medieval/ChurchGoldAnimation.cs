using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// The clapper strikes once at the far end of each swing. Ticks, never rendering,
	// advance the motion, so frame rate and pause cannot duplicate a bell sound.
	public sealed class ChurchBellMotion
	{
		public const int Period = 50;
		int remaining;
		public int Phase { get; private set; }
		public bool Ringing => remaining > 0;
		public int Strikes { get; private set; }
		public static float AngleAt(int phase) => MathF.Sin(phase * MathF.Tau / Period) * .62f;
		public float Swing => Ringing ? AngleAt(Phase) : 0;
		public void Ring(int strokes)
		{
			if (strokes <= 0) return;
			if (!Ringing) Phase = 0;
			remaining = Math.Max(remaining, Math.Min(3, strokes));
		}
		public void Stop() { remaining = Phase = 0; }
		public bool Tick()
		{
			if (!Ringing) return false;
			Phase++;
			if (Phase >= Period) { Phase = 0; remaining--; }
			if (Phase != 12) return false;
			Strikes++; return true;
		}
	}
	public class MedievalChurchAnimationInfo : TraitInfo, Requires<MedievalChurchInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalChurchAnimation();
	}
	public sealed class MedievalChurchAnimation : ITick, IRenderModifier
	{
		public readonly ChurchBellMotion Bell = new();
		bool completed, night;
		int care;
		public void ShowCare() { care = 30; }
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self) || self.TraitOrDefault<MedievalBurnable>()?.Burning == true)
			{ Bell.Stop(); care = 0; return; }
			if (care > 0) care--;
			var nowNight = self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.Night == true;
			if (!completed || night != nowNight) Bell.Ring(3);
			completed = true; night = nowNight;
			if (Bell.Tick()) MedievalAudio.At(self.World, "church-bell", self.CenterPosition);
		}
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var r in renderables) yield return r;
			if (!MedievalHouseLights.Visible(self)) yield break;
			var darkness = self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.Darkness ?? 0;
			var light = Math.Max(darkness * .72f, care > 0 ? .24f : 0);
			yield return new ChurchGoldRenderable(self.CenterPosition, 2, ChurchGoldVisual.Church, Bell.Ringing ? Bell.Phase : -1, light);
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => bounds;
	}
	public class MedievalGoldWorkshopAnimationInfo : TraitInfo, Requires<MedievalBuildingInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalGoldWorkshopAnimation();
	}
	public sealed class MedievalGoldWorkshopAnimation : ITick, IRenderModifier
	{
		int remaining;
		public int Phase { get; private set; }
		public bool Operating => remaining > 0;
		public void Receive(int material, int amount)
		{
			if (material != (int)Material.Gold || amount <= 0) return;
			if (!Operating) Phase = 0;
			remaining = 150;
		}
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self) || self.TraitOrDefault<MedievalBurnable>()?.Burning == true) { remaining = 0; return; }
			if (!Operating) return;
			if (Phase % 50 == 0) MedievalAudio.At(self.World, "pulley", self.CenterPosition);
			if (Phase % 50 == 25) MedievalAudio.At(self.World, "gold-sort", self.CenterPosition);
			Phase = (Phase + 1) % 100; remaining--;
		}
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var r in renderables) yield return r;
			if (Operating && MedievalHouseLights.Visible(self)) yield return new ChurchGoldRenderable(self.CenterPosition, 2, ChurchGoldVisual.Gold, Phase, 0);
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => bounds;
	}
	public enum ChurchGoldVisual { Church, Gold, Healing }
	// Small native overlays retain the painted isometric silhouettes and participate
	// in normal depth sorting. They do not allocate particles, actors or resources.
	sealed class ChurchGoldRenderable : IRenderable, IFinalizedRenderable
	{
		readonly ChurchGoldVisual visual;
		readonly int phase;
		readonly float light;
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;
		public ChurchGoldRenderable(WPos pos, int z, ChurchGoldVisual visual, int phase, float light)
		{ Pos = pos; ZOffset = z; this.visual = visual; this.phase = phase; this.light = light; }
		public IRenderable WithZOffset(int z) => new ChurchGoldRenderable(Pos, z, visual, phase, light);
		public IRenderable OffsetBy(in WVec offset) => new ChurchGoldRenderable(Pos + offset, ZOffset, visual, phase, light);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			var p = wr.ScreenPxPosition(Pos);
			var projection = visual == ChurchGoldVisual.Church ? MedievalArchitecture.For("md.church")
				: visual == ChurchGoldVisual.Gold ? MedievalArchitecture.For("md.goldmine") : ArchitectureProjection.Identity;
			var local = projection.Bounds(visual == ChurchGoldVisual.Church ? new(-108, -220, 216, 264) : new(-40, -45, 95, 80));
			return new(p.X + local.X, p.Y + local.Y, local.Width, local.Height);
		}
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos); var cr = Game.Renderer.WorldRgbaColorRenderer;
			var projection = visual == ChurchGoldVisual.Church ? MedievalArchitecture.For("md.church")
				: visual == ChurchGoldVisual.Gold ? MedievalArchitecture.For("md.goldmine") : ArchitectureProjection.Identity;
			var anchor = visual == ChurchGoldVisual.Church ? new Vector3(108, 220, 0)
				: visual == ChurchGoldVisual.Gold ? new Vector3(92, 140, 0) : Vector3.Zero;
			Vector3 P(float x, float y) => origin + projection.Project(new Vector3(x, y, 0) - anchor);
			void Line(float x, float y, float xx, float yy, float width, Color color) => cr.DrawLine(P(x, y), P(xx, yy), width, color);
			void Oval(float x, float y, float rx, float ry, Color color) => cr.FillEllipse(P(x - rx, y - ry), P(x + rx, y + ry), color);
			if (visual == ChurchGoldVisual.Healing)
			{
				var t = 1 - phase / 24f; var alpha = (int)(190 * MathF.Sin(MathF.PI * t));
				Oval(0, 1, 5 + t * 5, 2 + t * 2, Color.FromArgb(alpha / 6, 211, 229, 155));
				var color = Color.FromArgb(alpha, 191, 231, 170);
				var y = -26 - t * 9;
				Line(-2.5f, y, 2.5f, y, 1.5f, color); Line(0, y - 3, 0, y + 3, 1.5f, color);
				for (var i = 0; i < 3; i++) Oval((i - 1) * 5, -19 - t * 11 - i * 2, .65f, .65f, Color.FromArgb(alpha / 2, 245, 226, 167));
				return;
			}
			if (visual == ChurchGoldVisual.Church)
			{
				foreach (var strip in ChurchBellArt.Frame(phase)) cr.FillRect(P(strip.X, strip.Y), P(strip.X + strip.Width, strip.Y),
					P(strip.X + strip.Width, strip.Y + 1), P(strip.X, strip.Y + 1), strip.Color);
				// Gentle gold reflections, with an occasional small specular glint.
				// Deterministic presentation clock: no flash continues while paused.
				var time = wr.World.WorldTick + Pos.X / 512 + Pos.Y / 512;
				var warmth = .68f + .18f * MathF.Sin(time * .025f);
				var sparkle = MathF.Pow(Math.Max(0, MathF.Sin(time * .018f)), 24);
				Oval(146, 72, 7, 10, Color.FromArgb((int)(8 * warmth), 255, 205, 84));
				Oval(146, 72, 4, 7, Color.FromArgb((int)(14 * warmth), 255, 222, 119));
				Line(146, 64, 146, 81, .65f, Color.FromArgb((int)(85 * warmth), 255, 243, 167));
				Line(140, 73, 152, 69, .65f, Color.FromArgb((int)(100 * warmth), 255, 235, 141));
				if (sparkle > .03f)
				{
					var shine = Color.FromArgb((int)(205 * sparkle), 255, 247, 196);
					Line(146 - 4 * sparkle, 70, 146 + 4 * sparkle, 70, .7f, shine);
					Line(146, 70 - 5 * sparkle, 146, 70 + 5 * sparkle, .7f, shine);
					Oval(146, 70, .8f, .8f, shine);
				}
				if (light > 0)
				{
					var flicker = .94f + .06f * MathF.Sin(wr.World.WorldTick * .08f);
					foreach (var window in new[] { (53f, 188f), (74f, 205f), (95f, 218f), (132f, 185f) })
					{
						Oval(window.Item1, window.Item2, 6, 11, Color.FromArgb((int)(13 * light), 255, 178, 71));
						Line(window.Item1, window.Item2 - 5, window.Item1, window.Item2 + 5, 1.7f, Color.FromArgb((int)(160 * light * flicker), 255, 203, 102));
					}
				}
				return;
			}
			var turn = phase * MathF.Tau / 50; var stroke = MathF.Sin(turn);
			// Winch hub within the open workshop, seen as a narrow isometric ellipse.
			Oval(116, 130, 3.2f, 4.2f, Color.FromArgb(69, 53, 33));
			for (var i = 0; i < 4; i++)
			{
				var a = turn + i * MathF.PI / 2;
				Line(116, 130, 116 + MathF.Cos(a) * 2.6f, 130 + MathF.Sin(a) * 3.7f, 1, Color.FromArgb(177, 139, 67));
			}
			Oval(116, 130, .8f, 1, Color.FromArgb(231, 190, 91));
			// A sorting rake and loose fragments move across the existing counter.
			var rakeX = 103 + stroke * 3; var rakeY = 140 - stroke;
			// Upper body stays behind the counter; the worker holds the moving rake.
			Line(101, 134, 101, 138, 5, Color.FromArgb(112, 88, 51));
			Oval(101, 130 + stroke * .3f, 2.2f, 2.8f, Color.FromArgb(193, 154, 102));
			Line(99, 128, 103, 128, 1.6f, Color.FromArgb(85, 73, 44));
			Line(103, 135, rakeX + 3, rakeY - 3, 1.7f, Color.FromArgb(193, 154, 102));
			Line(99, 136, rakeX, rakeY, 1.5f, Color.FromArgb(174, 134, 83));
			Line(rakeX + 3, rakeY - 5, rakeX, rakeY + 2, 1.2f, Color.FromArgb(168, 128, 75));
			Line(rakeX - 3, rakeY + 3, rakeX + 3, rakeY + 1, 1, Color.FromArgb(165, 164, 131));
			for (var i = 0; i < 5; i++)
			{
				var progress = (phase + i * 11) % 50 / 50f;
				Oval(94 + i * 4 + stroke, 145 - i * 1.2f - MathF.Sin(progress * MathF.PI) * 2, .8f, .6f, Color.FromArgb(207, 177, 68));
				if (progress < .45f) Oval(102 + i * 2 + progress * 4, 140 - progress * 12, 1 + progress * 2, .7f + progress, Color.FromArgb((int)(45 * (1 - progress / .45f)), 188, 168, 122));
			}
		}
	}
}
