using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum BuildingMotion { Saw, Crane, Vendor, Sheep, Loom }

	public class MedievalBuildingAnimationInfo : TraitInfo, Requires<WithSpriteBodyInfo>, Requires<MedievalBuildingInfo>
	{
		public readonly BuildingMotion Motion = BuildingMotion.Saw;
		public readonly int FrameTicks = 3;
		[SequenceReference] public readonly string WorkingSequence = "working";
		public override object Create(ActorInitializer init) => new MedievalBuildingAnimation(this, init.Self.ActorID);
	}

	// Presentation only. A real delivery triggers processing; this never produces or consumes resources.
	public sealed class MedievalBuildingAnimation : ITick
	{
		readonly MedievalBuildingAnimationInfo info;
		readonly uint phase;
		WithSpriteBody body;
		string sequence;
		int clock;
		int cycles;
		public bool Operating { get; private set; }
		public int Frame { get; private set; }
		public int Deliveries { get; private set; }
		public int CompletedCycles { get; private set; }
		public MedievalBuildingAnimation(MedievalBuildingAnimationInfo info, uint phase)
		{
			if (info.FrameTicks < 1) throw new ArgumentException("Building animation interval must be positive.");
			this.info = info; this.phase = phase;
		}
		public void Receive(int resource, int amount)
		{
			if (amount <= 0 || !(info.Motion == BuildingMotion.Saw && resource == 0 || info.Motion == BuildingMotion.Crane && resource == 3)) return;
			Deliveries++;
			cycles = Math.Max(cycles, 2);
		}
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self)) return;
			body ??= self.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body");
			Operating = info.Motion == BuildingMotion.Vendor || cycles > 0
				|| info.Motion == BuildingMotion.Sheep && self.World.WorldActor.Trait<MedievalDayNight>().Clock.Night == false
				|| info.Motion == BuildingMotion.Loom && self.Trait<MedievalTentWorkshop>().Working(self);
			var next = Operating ? info.WorkingSequence : "idle";
			if (sequence != next || body.DefaultAnimation.CurrentSequence.Name != next)
			{
				sequence = next;
				clock = 0; Frame = 0;
				body.DefaultAnimation.PlayFetchIndex(next, () => Frame);
				if (Operating && info.Motion is BuildingMotion.Vendor or BuildingMotion.Sheep) clock = (int)(phase % (uint)body.DefaultAnimation.CurrentSequence.Length) * info.FrameTicks;
			}
			if (!Operating) return;
			var length = body.DefaultAnimation.CurrentSequence.Length;
			if (++clock >= length * info.FrameTicks)
			{
				clock = 0; CompletedCycles++;
				if (cycles > 0) cycles--;
			}
			Frame = clock / info.FrameTicks;
		}
	}

	public sealed class ChimneyPuff
	{
		public Vector2 Drift;
		public Vector2 Velocity;
		public int Age;
		public float Phase;
		public float Height => Age * .43f;
		public float Radius => 1.8f + Age * .065f;
		public float Opacity => Math.Clamp(Math.Min(Age / 12f, (ChimneySmoke.Lifetime - Age) / 42f), 0, 1);
	}

	public sealed class ChimneySmoke
	{
		public const int Lifetime = 112;
		public const int MaxPuffs = 8;
		readonly List<ChimneyPuff> puffs = [];
		readonly uint seed;
		int clock;
		public IReadOnlyList<ChimneyPuff> Puffs => puffs;
		public ChimneySmoke(uint seed) { this.seed = seed; clock = (int)(seed % 16); }
		public void Tick(Vector2 wind, bool emitting)
		{
			clock++;
			foreach (var p in puffs)
			{
				p.Age++;
				p.Velocity = Vector2.Lerp(p.Velocity, wind * .33f, .035f);
				p.Drift += p.Velocity + new Vector2(MathF.Sin(p.Age * .06f + p.Phase) * .05f, 0);
			}
			puffs.RemoveAll(p => p.Age >= Lifetime);
			if (emitting && clock % 16 == 0 && puffs.Count < MaxPuffs)
				puffs.Add(new ChimneyPuff { Velocity = wind * .12f, Phase = (clock + seed % 97) * .37f });
		}
	}

	public class MedievalChimneyInfo : TraitInfo, Requires<MedievalBuildingInfo>
	{
		public readonly int2 ChimneyOffset = new(-1, -58);
		public override object Create(ActorInitializer init) => new MedievalChimney(this, init.Self.ActorID);
	}
	public sealed class MedievalChimney : ITick, IRenderModifier
	{
		readonly MedievalChimneyInfo info;
		public ChimneySmoke Smoke { get; }
		public MedievalChimney(MedievalChimneyInfo info, uint seed) { this.info = info; Smoke = new ChimneySmoke(seed); }
		void ITick.Tick(Actor self) => Smoke.Tick(self.World.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene.Wind ?? new Vector2(.45f, .1f), MedievalStockpile.Active(self));
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var item in renderables) yield return item;
			if (MedievalStockpile.Active(self) && !self.World.FogObscures(self) && Smoke.Puffs.Count > 0)
				yield return new ChimneySmokeRenderable(self.CenterPosition, 1,
					MedievalArchitecture.For(self).Project(self.TraitOrDefault<MedievalHouseAppearance>()?.ChimneyOffset ?? info.ChimneyOffset), Smoke);
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			foreach (var b in bounds) yield return Rectangle.FromLTRB(b.Left - 60, b.Top - 70, b.Right + 60, b.Bottom);
		}
	}

	sealed class ChimneySmokeRenderable : IRenderable, IFinalizedRenderable
	{
		readonly int2 mouth;
		readonly ChimneySmoke smoke;
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;
		public ChimneySmokeRenderable(WPos pos, int z, int2 mouth, ChimneySmoke smoke) { Pos = pos; ZOffset = z; this.mouth = mouth; this.smoke = smoke; }
		public IRenderable WithZOffset(int z) => new ChimneySmokeRenderable(Pos, z, mouth, smoke);
		public IRenderable OffsetBy(in WVec offset) => new ChimneySmokeRenderable(Pos + offset, ZOffset, mouth, smoke);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr)
		{
			var at = wr.ScreenPxPosition(Pos) + mouth;
			return new Rectangle(at.X - 70, at.Y - 70, 140, 82);
		}
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Screen3DPxPosition(Pos) + new Vector3(mouth.X, mouth.Y, 0);
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			foreach (var puff in smoke.Puffs)
			{
				var at = origin + new Vector3(puff.Drift.X, puff.Drift.Y - puff.Height, 0);
				// Translucent nested lobes soften the silhouette without opaque circles.
				for (var lobe = 0; lobe < 3; lobe++)
				{
					var shift = new Vector3(MathF.Sin(puff.Phase + puff.Age * .035f + lobe * 2) * puff.Radius * .4f, (lobe - 1) * puff.Radius * .32f, 0);
					for (var layer = 4; layer > 0; layer--)
					{
						var r = puff.Radius * (.35f + layer * .16f);
						var color = Color.FromArgb((int)((layer == 4 ? 6 : 10) * puff.Opacity), 182 + lobe * 10 + layer * 4, 183 + lobe * 9 + layer * 4, 176 + lobe * 9 + layer * 4);
						// Smoke is a rising volume; only its drift follows the ground plane.
						cr.FillEllipse(at + shift - new Vector3(r, r * .96f, 0), at + shift + new Vector3(r, r * .96f, 0), color);
					}
				}
			}
		}
	}
}
