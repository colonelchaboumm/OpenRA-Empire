using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSiegeHeraldryInfo : TraitInfo, Requires<MedievalSiegeEngineInfo>, Requires<WithMedievalSiegeBodyInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeHeraldry();
	}
	public sealed class MedievalSiegeHeraldry : IRender, INotifyCreated
	{
		Mobile mobile;
		MedievalSiegeEngine engine;
		WithMedievalSiegeBody body;
		void INotifyCreated.Created(Actor self)
		{
			mobile = self.Trait<Mobile>(); engine = self.Trait<MedievalSiegeEngine>(); body = self.Trait<WithMedievalSiegeBody>();
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (self.IsDead || self.World.FogObscures(self)) yield break;
			// Resolve the current owner on every draw: capture and recapture change both
			// the cloth and the emblem, without caching the builder's national identity.
			var identity = MedievalHeraldry.For(self.Owner);
			var pose = new SiegeLiveryPose(engine.Info.Trebuchet, OpenRA.Mods.Common.Util.IndexFacing(mobile.Facing, 8), body.Sequence, body.Frame);
			yield return new SiegeLiveryRenderable(self.CenterPosition, pose, identity.Color);
			var sprite = self.World.Map.Sequences.GetSequence("md.heraldic-banner-" + identity.Emblem, "idle").GetSprite(MedievalCartHeraldry.FlagFrame(self));
			yield return new SpriteRenderable(sprite, self.CenterPosition, WVec.Zero, 1026,
				wr.Palette("md-livery" + self.Owner.InternalName), .6f, 1, Vector3.One, TintModifiers.None, true)
				.WithScreenTransform(Matrix4x4.CreateTranslation(pose.FlagAnchor));
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			var at = wr.ScreenPxPosition(self.CenterPosition); yield return new(at.X - 110, at.Y - 130, 220, 200);
		}
	}
	readonly struct SiegeLiveryPose
	{
		public readonly bool Trebuchet;
		public readonly float Sin, Cos, Build, CartOffset;
		public SiegeLiveryPose(bool trebuchet, int facing, string sequence, int frame)
		{
			Trebuchet = trebuchet; var angle = -facing * MathF.PI / 4;
			Sin = MathF.Sin(angle); Cos = MathF.Cos(angle);
			// Follow the quantized sprite pose, including reverse assembly while packing.
			Build = sequence == "assemble" ? frame / 47f : sequence is "deployed" or "fire" ? 1 : 0;
			CartOffset = -32 * Ease(Build, .02f, .18f);
		}
		public static float Ease(float value, float start, float end)
		{
			var t = Math.Clamp((value - start) / (end - start), 0, 1); return t * t * (3 - 2 * t);
		}
		public Vector3 Point(float forward, float side, float height) => new(Sin * forward + Cos * side, (-Cos * forward + Sin * side) * .5f - height, 0);
		public Vector3 Cart(float f, float s, float z) => Point(f + CartOffset, s + CartOffset, z);
		// The trebuchet's standard stays on its transport wagon when the wagon parks
		// beside the assembled engine. The ram's pole is fixed to its roof, not its log.
		public Vector3 FlagAnchor => Trebuchet ? Cart(-23, Sin < 0 ? -16 : 16, 18) : Point(-25, Sin < 0 ? -16 : 16, 45);
	}
	sealed class SiegeLiveryRenderable : IRenderable, IFinalizedRenderable
	{
		readonly SiegeLiveryPose pose;
		public Color Color { get; }
		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => true;
		public SiegeLiveryRenderable(WPos pos, SiegeLiveryPose pose, Color color, int z = 512) { Pos = pos; this.pose = pose; Color = color; ZOffset = z; }
		public IRenderable WithZOffset(int z) => new SiegeLiveryRenderable(Pos, pose, Color, z);
		public IRenderable OffsetBy(in WVec offset) => new SiegeLiveryRenderable(Pos + offset, pose, Color, ZOffset);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 110, p.Y - 130, 220, 200); }
		public void Render(WorldRenderer wr)
		{
			var at = wr.Screen3DPxPosition(Pos); var cr = Game.Renderer.WorldRgbaColorRenderer;
			var lighting = wr.TerrainLighting?.TintAt(Pos) ?? Vector3.One;
			Color Lit(int r, int g, int b) => Primitives.Color.FromArgb(Math.Clamp((int)(r * lighting.X), 0, 255), Math.Clamp((int)(g * lighting.Y), 0, 255), Math.Clamp((int)(b * lighting.Z), 0, 255));
			Color Shade(float shade) => Lit((int)(Color.R * shade), (int)(Color.G * shade), (int)(Color.B * shade));
			var gold = Lit(205, 174, 107);
			Vector3 P(float f, float s, float z) => at + pose.Point(f, s, z);
			Vector3 C(float f, float s, float z) => at + pose.Cart(f, s, z);
			void Line(Vector3 a, Vector3 b, float width, Color color) { if (Vector3.DistanceSquared(a, b) > .1f) cr.DrawLine(a, b, width, color); }
			void Cloth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float shade)
			{
				cr.FillRect(a, b, c, d, Shade(shade)); Line(a, b, .7f, gold); Line(c, d, .7f, gold);
				Line(Vector3.Lerp(a, b, .5f), Vector3.Lerp(d, c, .5f), .7f, Shade(shade * .72f));
			}
			var near = pose.Sin < 0 ? -1 : 1;
			if (!pose.Trebuchet)
			{
				// Sewn caparison bands follow both slopes of the original hide roof.
				for (var face = 0; face < 2; face++)
				{
					var side = (face == 0 ? -near : near) * 19;
					for (var band = 0; band < 2; band++)
					{
						var f = band == 0 ? -21 : 12;
						Cloth(P(f, 0, 56.5f), P(f + 9, 0, 56.5f), P(f + 9, side, 43), P(f, side, 43), side < 0 ? .97f : .78f);
					}
				}
				Line(P(-32, near * 19, 42), P(32, near * 19, 42), 2, Shade(.75f));
				return;
			}
			Cloth(C(-9, near * 15, 18), C(9, near * 15, 18), C(9, near * 15, 9), C(-9, near * 15, 9), .84f);
			var front = pose.Cos > 0 ? -27 : 27;
			Cloth(C(front, -8, 18), C(front, 8, 18), C(front, 8, 10), C(front, -8, 10), .96f);
			// A short hanging banner rises with the nearest A-frame. It folds away
			// before that support is lowered, following the same curve as the artwork.
			var index = near < 0 ? 0 : 1;
			var raised = SiegeLiveryPose.Ease(pose.Build, .43f + index * .065f, .64f + index * .065f);
			var cloth = SiegeLiveryPose.Ease(raised, .5f, 1);
			if (cloth <= 0) return;
			var angle = raised * MathF.PI / 2;
			Vector3 Support(float f, float h) => P(f, near * (18 + (h - 8) * MathF.Cos(angle)), 8 + (h - 8) * MathF.Sin(angle));
			Cloth(Support(-6, 59), Support(6, 59), Support(6, 59 - 17 * cloth), Support(-6, 59 - 17 * cloth), .9f);
		}
	}
}
