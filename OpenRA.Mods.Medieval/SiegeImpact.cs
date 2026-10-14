using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Effects;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	// Cosmetic randomness is private to each effect; no particles affect combat or stocks.
	public sealed class SiegeImpactEffect : IEffect
	{
		public const int Lifetime = 110;
		public WPos Position { get; }
		public int Age { get; private set; }
		public readonly bool Masonry;
		public readonly int EmissionHeight;
		public readonly bool Ram;
		public readonly bool Collapse;
		public int Duration => Collapse ? 180 : Ram ? 70 : Lifetime;
		internal readonly ImpactMote[] Motes;
		public SiegeImpactEffect(WPos position, int seed, bool masonry = false, int emissionHeight = 0, bool ram = false, float direction = 0, bool collapse = false)
		{
			Position = position; Masonry = masonry; EmissionHeight = emissionHeight; Ram = ram; Collapse = collapse;
			var random = new Random(seed);
			Motes = new ImpactMote[collapse ? 78 : ram ? 35 : masonry ? 22 : 62];
			for (var i = 0; i < Motes.Length; i++)
			{
				var angle = ram ? direction + (float)(random.NextDouble() - .5) * 2.8f : (float)(random.NextDouble() * Math.PI * 2);
				Motes[i] = new ImpactMote(angle, ram ? 10 + random.Next(36) : 22 + random.Next(70), ram ? 6 + random.Next(18) : (masonry ? 18 : 28) + random.Next(50),
					ram ? 2 + random.Next(6) : 4 + random.Next(13), random.Next(collapse ? 24 : ram ? 4 : 8), i < (collapse ? 38 : ram ? 14 : masonry ? 13 : 23), random.Next(3));
			}
		}
		public void Tick(World world) { if (++Age >= Duration) world.AddFrameEndTask(w => w.Remove(this)); }
		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			var at = wr.ScreenPxPosition(Position);
			if (at.X + 170 < wr.Viewport.TopLeft.X || at.X - 150 > wr.Viewport.BottomRight.X
				|| at.Y + 90 < wr.Viewport.TopLeft.Y || at.Y - 170 > wr.Viewport.BottomRight.Y) yield break;
			if (!wr.World.FogObscures(Position)) yield return new SiegeImpactRenderable(Position, 2048, this);
		}
	}
	public readonly record struct ImpactMote(float Angle, float Reach, float Height, float Size, int Delay, bool Debris, int Kind);
	sealed class SiegeImpactRenderable : IRenderable, IFinalizedRenderable
	{
		readonly SiegeImpactEffect effect;
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => true;
		public SiegeImpactRenderable(WPos pos, int z, SiegeImpactEffect effect) { Pos = pos; ZOffset = z; this.effect = effect; }
		public IRenderable WithZOffset(int z) => new SiegeImpactRenderable(Pos, z, effect);
		public IRenderable OffsetBy(in WVec offset) => new SiegeImpactRenderable(Pos + offset, ZOffset, effect);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var at = wr.ScreenPxPosition(Pos); return new Rectangle(at.X - 150, at.Y - 170, 320, 260); }
		static Color Dust(int alpha, int shade, bool masonry) => Color.FromArgb(Math.Clamp(alpha, 0, 255),
			Math.Clamp((masonry ? 171 : 170) + shade, 0, 255), Math.Clamp((masonry ? 166 : 149) + shade, 0, 255), Math.Clamp((masonry ? 143 : 109) + shade, 0, 255));
		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer; var at = wr.Screen3DPxPosition(Pos); var age = effect.Age;
			if (!effect.Masonry)
			{
				// A flattened, broken dust ring travels along the isometric ground plane.
				var ring = Math.Clamp(age / 24f, 0, 1); var opacity = Math.Clamp(1 - age / 32f, 0, 1);
				for (var i = 0; i < 28; i++)
				{
					var a = i * MathF.PI * 2 / 28; var reach = (18 + 75 * ring) * (1 + .09f * MathF.Sin(i * 3.1f));
					var p = at + new Vector3(MathF.Cos(a) * reach, MathF.Sin(a) * reach * .48f, 0);
					var size = new Vector3(5 + ring * 8, 2 + ring * 3, 0);
					cr.FillEllipse(p - size, p + size, Dust((int)(opacity * 38), 27, false));
				}
				// Brief warm stone flash, then a shallow ground scar that fades with the debris.
				if (age < 5)
					cr.FillEllipse(at - new Vector3(17, 8, 0), at + new Vector3(17, 8, 0), Color.FromArgb(125 - age * 24, 229, 216, 173));
				var scar = Math.Clamp((SiegeImpactEffect.Lifetime - age) / 40f, 0, 1);
				cr.FillEllipse(at - new Vector3(17, 6, 0), at + new Vector3(17, 6, 0), Color.FromArgb((int)(70 * scar), 65, 54, 36));
				for (var i = 0; i < 7; i++)
				{
					var a = i * 2.4f; var end = at + new Vector3(MathF.Cos(a) * (23 + i), MathF.Sin(a) * (9 + i * .4f), 0);
					cr.DrawLine(at, end, 1, Color.FromArgb((int)(75 * scar), 76, 61, 40));
				}
			}
			// Stones and splinters follow a short ballistic hop and bounce once, then settle.
			foreach (var p in effect.Motes)
			{
				if (!p.Debris || age < p.Delay) continue;
				var time = (age - p.Delay) / (effect.Ram ? 22f : 36f);
				var distance = time < 1 ? time : 1 + .22f * Math.Clamp((time - 1) * 2, 0, 1);
				var height = time < 1 ? 4 * time * (1 - time) * p.Height : time < 1.5f ? MathF.Sin((time - 1) * MathF.PI * 2) * p.Height * .16f : 0;
				height += effect.EmissionHeight * Math.Max(0, 1 - time);
				var opacity = Math.Clamp((effect.Duration - age) / 35f, 0, 1);
				var point = at + new Vector3(MathF.Cos(p.Angle) * p.Reach * distance, MathF.Sin(p.Angle) * p.Reach * distance * .5f - height, 0);
				var angle = p.Angle + Math.Min(time, 1.5f) * (p.Kind == 0 ? 9 : -7);
				var tip = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * (p.Size * .28f + 1);
				var cross = new Vector3(-tip.Y, tip.X, 0) * (effect.Masonry && p.Kind == 0 ? .28f : .66f);
				cr.FillRect(point - tip, point + cross, point + tip, point - cross, Color.FromArgb((int)(235 * opacity), p.Kind == 0 ? 107 : 119, p.Kind == 0 ? 79 : 114, p.Kind == 0 ? 48 : 93));
				cr.DrawLine(point - tip, point + cross, 1, Dust((int)(200 * opacity), 35, effect.Masonry));
			}
			// Overlapping, softly edged lobes: fast outward blast, rising billows, slow wind drift.
			foreach (var p in effect.Motes)
			{
				if (p.Debris || age < p.Delay) continue;
				var t = (age - p.Delay) / (effect.Duration - 10f);
				var spread = 1 - MathF.Exp(-t * 4.4f);
				var opacity = (1 - MathF.Exp(-t * 18)) * MathF.Pow(Math.Max(0, 1 - t), 1.4f);
				var radius = (p.Size + (effect.Ram ? 4 : 10)) * (.4f + spread * 1.5f);
				var point = at + new Vector3(MathF.Cos(p.Angle) * p.Reach * spread + t * 17,
					MathF.Sin(p.Angle) * p.Reach * spread * .45f - p.Height * spread * 1.4f - t * 18 - effect.EmissionHeight * (1 - t * .25f), 0);
				for (var layer = 4; layer >= 0; layer--)
				{
					var size = new Vector3(radius * (1 + layer * .12f), radius * (.67f + layer * .08f), 0);
					var lit = point + new Vector3(-radius * .12f, -radius * .18f, 0) * (4 - layer) / 4;
					cr.FillEllipse(lit - size, lit + size, Dust((int)(opacity * (layer == 0 ? 55 : 20)), 22 - layer * 11 + p.Kind * 5, effect.Masonry));
				}
			}
		}
	}
}
