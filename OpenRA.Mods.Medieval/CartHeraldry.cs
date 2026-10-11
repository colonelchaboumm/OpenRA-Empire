using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum CartLiveryStyle { Supply, Stone, Caravan }
	public class MedievalCartHeraldryInfo : TraitInfo, Requires<MobileInfo>
	{
		public readonly CartLiveryStyle Style = CartLiveryStyle.Supply;
		public override object Create(ActorInitializer init) => new MedievalCartHeraldry(this);
	}
	// Uses the existing national palette and animated banner plates. All geometry is
	// attached to the same eight-facing model as the native cart; no inventory changes.
	public sealed class MedievalCartHeraldry : IRender, INotifyCreated
	{
		readonly MedievalCartHeraldryInfo info;
		Mobile mobile;
		WithMedievalInfantryBody infantry;
		WithMedievalSiegeBody siege;
		public MedievalCartHeraldry(MedievalCartHeraldryInfo info) { this.info = info; }
		public int LabelHeight => info.Style switch { CartLiveryStyle.Stone => 78, CartLiveryStyle.Caravan => 74, _ => 54 };
		public static int FlagFrame(Actor self) => (self.World.WorldTick / 3 + (int)(self.ActorID % 12)) % 12;
		void INotifyCreated.Created(Actor self)
		{
			mobile = self.Trait<Mobile>(); infantry = self.TraitOrDefault<WithMedievalInfantryBody>(); siege = self.TraitOrDefault<WithMedievalSiegeBody>();
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (self.IsDead || self.World.FogObscures(self)) yield break;
			var identity = MedievalHeraldry.For(self.Owner);
			var moving = (infantry?.Sequence ?? siege?.Sequence) is "run" or "carry-run";
			var pose = new CartLiveryPose(info.Style, OpenRA.Mods.Common.Util.IndexFacing(mobile.Facing, 8), moving ? (infantry?.Frame ?? siege?.Frame ?? 0) % 8 : 0);
			yield return new CartLiveryRenderable(self.CenterPosition, pose, identity.Color);
			var sprite = self.World.Map.Sequences.GetSequence("md.heraldic-banner-" + identity.Emblem, "idle").GetSprite(FlagFrame(self));
			// Keep the cart's depth origin: shifting its world position upwards would
			// sort the flag behind its own canvas. Only the screen attachment is moved.
			yield return new SpriteRenderable(sprite, self.CenterPosition, WVec.Zero, 1026,
				wr.Palette("md-livery" + self.Owner.InternalName), pose.FlagScale, 1, Vector3.One, TintModifiers.None, true)
				.WithScreenTransform(Matrix4x4.CreateTranslation(pose.FlagAnchor));
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			var at = wr.ScreenPxPosition(self.CenterPosition); yield return new(at.X - 90, at.Y - 95, 180, 140);
		}
	}
	readonly struct CartLiveryPose
	{
		public readonly CartLiveryStyle Style;
		public readonly float Sin, Cos, Bounce;
		public CartLiveryPose(CartLiveryStyle style, int facing, int frame)
		{
			Style = style; var angle = -facing * MathF.PI / 4;
			Sin = MathF.Sin(angle); Cos = MathF.Cos(angle);
			Bounce = style == CartLiveryStyle.Stone ? 0 : MathF.Round(MathF.Sin(frame * MathF.PI / 2) * (style == CartLiveryStyle.Caravan ? .7f : .6f));
		}
		public Vector3 Point(float forward, float side, float height) => new(MathF.Round(Sin * forward + Cos * side),
			MathF.Round((Style == CartLiveryStyle.Supply ? -6 : 0) + (-Cos * forward + Sin * side) * (Style == CartLiveryStyle.Supply ? .7f : .5f) - height - Bounce), 0);
		public Vector3 FlagAnchor => Style switch
		{
			CartLiveryStyle.Stone => Point(17, 14, 29), CartLiveryStyle.Caravan => Point(8, 9, 20), _ => Point(5, 7, 10)
		};
		public float FlagScale => Style switch { CartLiveryStyle.Stone => .5f, CartLiveryStyle.Caravan => .66f, _ => .42f };
	}
	sealed class CartLiveryRenderable : IRenderable, IFinalizedRenderable
	{
		readonly CartLiveryPose pose;
		public Color Color { get; }
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => true;
		public CartLiveryRenderable(WPos pos, CartLiveryPose pose, Color color, int z = 512) { Pos = pos; this.pose = pose; Color = color; ZOffset = z; }
		public IRenderable WithZOffset(int z) => new CartLiveryRenderable(Pos, pose, Color, z);
		public IRenderable OffsetBy(in WVec offset) => new CartLiveryRenderable(Pos + offset, pose, Color, ZOffset);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 80, p.Y - 80, 160, 120); }
		public void Render(WorldRenderer wr)
		{
			var at = wr.Screen3DPxPosition(Pos); var cr = Game.Renderer.WorldRgbaColorRenderer;
			var lighting = wr.TerrainLighting?.TintAt(Pos) ?? Vector3.One;
			Color Shade(float shade) => Primitives.Color.FromArgb(Math.Clamp((int)(Color.R * shade * lighting.X), 0, 255),
				Math.Clamp((int)(Color.G * shade * lighting.Y), 0, 255), Math.Clamp((int)(Color.B * shade * lighting.Z), 0, 255));
			var gold = Primitives.Color.FromArgb(Math.Clamp((int)(205 * lighting.X), 0, 255), Math.Clamp((int)(174 * lighting.Y), 0, 255), Math.Clamp((int)(107 * lighting.Z), 0, 255));
			Vector3 P(float f, float s, float z) => at + pose.Point(f, s, z);
			void Hem(Vector3 a, Vector3 b) { if (Vector3.DistanceSquared(a, b) > .1f) cr.DrawLine(a, b, .7f, gold); }
			void Cloth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float shade)
			{
				cr.FillRect(a, b, c, d, Shade(shade)); Hem(a, b); Hem(c, d);
				cr.DrawLine(Vector3.Lerp(a, b, .45f), Vector3.Lerp(d, c, .45f), .7f, Shade(shade * .75f));
			}
			if (pose.Style == CartLiveryStyle.Caravan)
			{
				// Two sewn bands follow the existing arched canvas in all eight facings.
				foreach (var f in new[] { -8f, 5f })
					for (var i = 0; i < 8; i++)
					{
						var s0 = -10 + i * 2.5f; var s1 = s0 + 2.5f;
						var z0 = 17 + 11 * MathF.Sqrt(Math.Max(0, 1 - s0 * s0 / 100));
						var z1 = 17 + 11 * MathF.Sqrt(Math.Max(0, 1 - s1 * s1 / 100));
						cr.FillRect(P(f, s0, z0), P(f + 4, s0, z0), P(f + 4, s1, z1), P(f, s1, z1), Shade(.72f + .25f * MathF.Sin((i + .5f) * MathF.PI / 8)));
						Hem(P(f, s0, z0), P(f, s1, z1));
					}
				// Cover the original blue pennant with matching cloth underneath the
				// national standard. Its narrow wooden mast remains part of the wagon.
				var old = P(8, 9, 15) + new Vector3(0, -33, 0);
				cr.FillRect(old + new Vector3(0, -1, 0), old + new Vector3(17, -1, 0), old + new Vector3(17, 11, 0), old + new Vector3(0, 9, 0), Shade(.8f));
				return;
			}
			var large = pose.Style == CartLiveryStyle.Stone;
			var side = (pose.Sin < 0 ? -1 : 1) * (large ? 14 : 7);
			var top = large ? 29 : 10; var bottom = large ? 17 : 3;
			Cloth(P(large ? -13 : -4, side, top), P(large ? 14 : 5, side, top),
				P(large ? 14 : 5, side, bottom), P(large ? -13 : -4, side, bottom), .84f);
			var front = pose.Cos > 0 ? large ? -25 : -7 : large ? 26 : 8;
			var width = large ? 8 : 4;
			Cloth(P(front, -width, top), P(front, width, top), P(front, width, bottom), P(front, -width, bottom), .96f);
		}
	}
}
