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
	public static class BattlefieldMotion
	{
		public const int FallTicks = 24;
		public const int CollapseTicks = 48;
		public static Matrix4x4 Fall(int age, int seed)
		{
			var t = Math.Clamp(age / (float)FallTicks, 0, 1);
			var ease = t * t * (3 - 2 * t);
			var side = (seed & 1) == 0 ? 1 : -1;
			return Matrix4x4.CreateRotationZ(side * MathF.PI * .5f * ease)
				* Matrix4x4.CreateScale(1, 1 - .46f * ease, 1)
				* Matrix4x4.CreateTranslation(side * 2 * ease, 2 * ease, 0);
		}
		public static Matrix4x4 Collapse(int age, int strip)
		{
			var t = Math.Clamp((age - strip * 4) / 32f, 0, 1);
			var fall = t * t;
			return new Matrix4x4(1 + fall * .1f, (strip - 1) * fall * .06f, 0, 0,
				(strip - 1) * fall * .12f, 1 - .94f * fall, 0, 0, 0, 0, 1, 0,
				MathF.Sin(age * 2.3f + strip) * (1 - t) * Math.Min(age / 4f, 1) * 1.8f, 0, 0, 1);
		}
	}

	public sealed class BattlefieldRemains
	{
		public WPos Position;
		public Sprite Sprite;
		public Sprite[] Strips;
		public float Scale;
		public string Palette;
		public Matrix4x4 Projection;
		public int Age, Seed;
		public CVec Size;
		public bool Building;
		public Player Owner;
		// Gameplay identity and reservations are independent of the captured sprite.
		public uint ClaimedBy, Carrier;
		public bool Settled => Age >= (Building ? BattlefieldMotion.CollapseTicks : BattlefieldMotion.FallTicks);
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalBattlefieldInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalBattlefield();
	}
	// Kills are replayed when a saved game is loaded. Keeping these lightweight
	// records in the world recreates the same remains, without dead actors, AI,
	// collision, food consumption or references to disposed units.
	public sealed class MedievalBattlefield : ITick, IRender, ISync
	{
		public const int MaximumRemains = 4096;
		readonly List<BattlefieldRemains> remains = [];
		public IReadOnlyList<BattlefieldRemains> Remains => remains;
		public int VisibleRemains { get; private set; }
		[VerifySync] public int RemainsHash
		{
			get { unchecked { return remains.Aggregate(17, (h, r) => ((((((h * 31 + r.Seed) * 31 + r.Position.X) * 31 + r.Position.Y)
				* 31 + r.Position.Z) * 31 + r.Age) * 31 + (int)r.ClaimedBy) * 31 + (int)r.Carrier + (int)(r.Owner?.PlayerActor.ActorID ?? 0)); } }
		}
		public bool Remove(BattlefieldRemains item) => remains.Remove(item);
		public bool Bury(BattlefieldRemains item)
		{
			if (item.Building || !remains.Remove(item)) return false;
			MedievalNationalPride.For(item.Owner)?.State.RecordBurial();
			return true;
		}
		public void Add(BattlefieldRemains item)
		{
			if (!item.Building) MedievalNationalPride.For(item.Owner)?.State.RecordDeath();
			// Never evict a body that is being collected or carried.
			if (remains.Count >= MaximumRemains)
			{
				var oldest = remains.FindIndex(r => r.ClaimedBy == 0);
				if (oldest >= 0) remains.RemoveAt(oldest);
			}
			remains.Add(item);
		}
		void ITick.Tick(Actor self)
		{
			foreach (var item in remains) if (item.Age < 300) item.Age++;
		}
		public static bool Visible(World world, WPos position) => world.Map.Contains(world.Map.CellContaining(position)) && !world.FogObscures(position);
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			VisibleRemains = 0;
			foreach (var item in remains)
			{
				if (item.Carrier != 0 || item.Sprite == null) continue;
				var at = wr.ScreenPxPosition(item.Position);
				if (at.X + 220 < wr.Viewport.TopLeft.X || at.X - 220 > wr.Viewport.BottomRight.X
					|| at.Y + 100 < wr.Viewport.TopLeft.Y || at.Y - 240 > wr.Viewport.BottomRight.Y || !Visible(self.World, item.Position)) continue;
				VisibleRemains++;
				var palette = wr.Palette(item.Palette);
				if (item.Building)
				{
					yield return new MedievalGroundLayer(new BattlefieldDebrisRenderable(item), 3);
					if (item.Age >= BattlefieldMotion.CollapseTicks) continue;
					for (var strip = 0; strip < item.Strips.Length; strip++)
					{
						var alpha = Math.Clamp((44 - item.Age + strip * 2) / 14f, 0, 1);
						yield return new SpriteRenderable(item.Strips[strip], item.Position, WVec.Zero, 0, palette, item.Scale,
							alpha, new Vector3(1 - item.Age * .006f), TintModifiers.None, true)
							.WithScreenTransform(item.Projection * BattlefieldMotion.Collapse(item.Age, strip));
					}
				}
				else
				{
					var tint = Vector3.Lerp(Vector3.One, new Vector3(.73f, .72f, .68f), Math.Clamp(item.Age / 45f, 0, 1));
					var body = new SpriteRenderable(item.Sprite, item.Position, WVec.Zero, 0, palette, item.Scale, 1, tint, TintModifiers.None, true)
						.WithScreenTransform(BattlefieldMotion.Fall(item.Age, item.Seed));
					yield return item.Settled ? new MedievalGroundLayer(body, 4) : body;
				}
			}
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr) { yield break; }
	}

	public class MedievalDeathVisualsInfo : TraitInfo, Requires<RenderSpritesInfo>
	{
		public readonly bool Building = false;
		public override object Create(ActorInitializer init) => new MedievalDeathVisuals(this);
	}
	public sealed class MedievalDeathVisuals(MedievalDeathVisualsInfo info) : INotifyKilled
	{
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			var battlefield = self.World.WorldActor.TraitOrDefault<MedievalBattlefield>();
			if (battlefield == null || !self.IsInWorld) return;
			var animation = info.Building
				? self.TraitsImplementing<WithSpriteBody>().FirstOrDefault(b => !b.IsTraitDisabled)?.DefaultAnimation
				: self.TraitOrDefault<WithMedievalInfantryBody>()?.DeathAnimation;
			var render = self.Info.TraitInfo<RenderSpritesInfo>();
			var record = new BattlefieldRemains
			{
				Position = self.CenterPosition, Sprite = animation?.CurrentSequence != null ? animation.Image : null, Scale = animation?.CurrentSequence?.Scale ?? 1,
				Palette = render.Palette ?? render.PlayerPalette + self.Owner.InternalName,
				Building = info.Building, Seed = (int)self.ActorID, Owner = self.Owner,
				Size = self.Info.TraitInfoOrDefault<BuildingInfo>()?.Dimensions ?? new CVec(1, 1),
				Projection = info.Building && self.TraitOrDefault<MedievalBuilding>()?.Complete == true ? MedievalArchitecture.For(self).Matrix : Matrix4x4.Identity
			};
			if (info.Building && record.Sprite != null)
			{
				var sprite = record.Sprite; record.Strips = new Sprite[3];
				for (var i = 0; i < 3; i++)
				{
					var left = sprite.Bounds.Width * i / 3; var right = sprite.Bounds.Width * (i + 1) / 3;
					record.Strips[i] = new Sprite(sprite.Sheet, new Rectangle(sprite.Bounds.X + left, sprite.Bounds.Y, right - left, sprite.Bounds.Height),
						sprite.ZRamp, sprite.Offset + new Vector3((left + right - sprite.Bounds.Width) / 2f, 0, 0), sprite.Channel, sprite.BlendMode);
				}
			}
			battlefield.Add(record);
			if (info.Building)
				self.World.AddFrameEndTask(w => w.Add(new SiegeImpactEffect(record.Position, record.Seed, masonry: true,
					emissionHeight: Math.Clamp((record.Size.X + record.Size.Y) * 6, 18, 65), collapse: true)));
		}
	}

	sealed class BattlefieldDebrisRenderable(BattlefieldRemains item) : IRenderable, IFinalizedRenderable
	{
		public WPos Pos => item.Position;
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => this;
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new Rectangle(p.X - 160, p.Y - 90, 320, 180); }
		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer; var origin = wr.Screen3DPxPosition(Pos);
			var fade = Math.Clamp(item.Age / 22f, 0, 1); var count = Math.Clamp(item.Size.X * item.Size.Y * 7, 12, 90);
			var u = new Vector3(item.Size.X * 11, item.Size.X * 5.5f, 0);
			var v = new Vector3(-item.Size.Y * 11, item.Size.Y * 5.5f, 0);
			cr.FillRect(origin - u - v, origin + u - v, origin + u + v, origin - u + v, Color.FromArgb((int)(45 * fade), 60, 49, 34));
			cr.FillRect(origin - u * .8f - v * .8f, origin + u * .8f - v * .8f, origin + u * .8f + v * .8f,
				origin - u * .8f + v * .8f, Color.FromArgb((int)(55 * fade), 74, 63, 47));
			var random = new Random(item.Seed);
			for (var i = 0; i < count; i++)
			{
				var x = ((float)random.NextDouble() - .5f) * item.Size.X * 22;
				var y = ((float)random.NextDouble() - .5f) * item.Size.Y * 22;
				var p = origin + new Vector3(x - y, (x + y) * .5f, 0);
				var length = 2 + (float)random.NextDouble() * 7; var wood = i % 3 == 0;
				var axis = Vector3.Normalize(new Vector3(1, i % 2 == 0 ? .5f : -.5f, 0)) * length;
				var side = new Vector3(-axis.Y, axis.X, 0) * (wood ? .15f : .55f);
				var lift = new Vector3(0, wood ? 1.5f : 2 + length * .3f, 0);
				cr.FillRect(p - side, p + axis, p + axis + lift, p - side + lift, Color.FromArgb((int)(235 * fade), 64, 59, 49));
				cr.FillRect(p + axis, p + side, p + side + lift, p + axis + lift, Color.FromArgb((int)(235 * fade), 84, 75, 61));
				cr.FillRect(p - axis, p - side, p + axis, p + side, Color.FromArgb((int)(235 * fade), wood ? 88 : 121, wood ? 66 : 113, wood ? 40 : 98));
				cr.DrawLine(p - axis, p - side, 1, Color.FromArgb((int)(190 * fade), wood ? 137 : 174, wood ? 109 : 164, wood ? 68 : 139));
			}
		}
	}
}
