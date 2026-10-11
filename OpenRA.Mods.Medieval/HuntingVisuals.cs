using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Effects;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	// These effects never damage an actor or create food. Harvesting remains the
	// worker's synchronized job, including interruptions and finite prey stocks.
	public sealed class HuntingArrowEffect : IEffect
	{
		readonly Actor hunter, prey;
		readonly WPos from, to;
		readonly int duration;
		public int Age { get; private set; }
		public HuntingArrowEffect(Actor hunter, Actor prey, int duration)
		{
			this.hunter = hunter; this.prey = prey; this.duration = Math.Max(1, duration);
			from = hunter.CenterPosition + new WVec(0, 0, 540);
			to = prey.CenterPosition + new WVec(0, 0, prey.Info.Name == "md.game-deer" ? 380 : 110);
		}
		public void Tick(World world)
		{
			if (++Age >= duration || hunter.IsDead || !hunter.IsInWorld || prey.IsDead || !prey.IsInWorld
				|| hunter.Trait<MedievalWorker>().WorkTarget != prey)
				world.AddFrameEndTask(w => w.Remove(this));
		}
		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			var position = WPos.Lerp(from, to, Age, duration);
			if (wr.World.FogObscures(position)) yield break;
			yield return new HuntingArrowRenderable(position, to - from, MathF.Sin(Age * MathF.PI / duration) * 4);
		}
	}
	sealed class HuntingArrowRenderable(WPos pos, WVec direction, float arc) : IRenderable, IFinalizedRenderable
	{
		public WPos Pos => pos;
		public int ZOffset => 256;
		public bool IsDecoration => true;
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new HuntingArrowRenderable(pos + offset, direction, arc);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new Rectangle(p.X - 12, p.Y - 16, 24, 24); }
		public void Render(WorldRenderer wr)
		{
			var at = wr.Screen3DPosition(Pos) - new Vector3(0, arc, 0);
			var projected = wr.Screen3DPosition(Pos + direction) - wr.Screen3DPosition(Pos);
			var forward = projected.LengthSquared() > .01f ? Vector3.Normalize(projected) : Vector3.UnitX;
			var side = new Vector3(-forward.Y, forward.X, 0);
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			cr.DrawLine(at - forward * 9, at, 1.3f, Color.FromArgb(232, 187, 152, 98));
			cr.DrawLine(at - forward * 2, at + forward, 1.5f, Color.FromArgb(246, 221, 225, 215));
			cr.DrawLine(at - forward * 8 + side * 2, at - forward * 6, 1, Color.FromArgb(220, 225, 215, 173));
			cr.DrawLine(at - forward * 8 - side * 2, at - forward * 6, 1, Color.FromArgb(220, 225, 215, 173));
		}
	}
	public sealed class HuntingRemainsEffect(WPos position, string image, int facing, int fallAge) : IEffect
	{
		public int Age { get; private set; }
		public void Tick(World world) { if (++Age >= 65) world.AddFrameEndTask(w => w.Remove(this)); }
		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			if (wr.World.FogObscures(position)) yield break;
			var sprite = wr.World.Map.Sequences.GetSequence(image + "-fall", "idle").GetSprite(facing * 12 + Math.Clamp((fallAge + Age) / 3, 0, 11));
			yield return new SpriteRenderable(sprite, position, WVec.Zero, 0, null, 1, Math.Clamp((65 - Age) / 25f, 0, 1),
				new Vector3(.85f, .82f, .78f), TintModifiers.None, false);
		}
	}
}
