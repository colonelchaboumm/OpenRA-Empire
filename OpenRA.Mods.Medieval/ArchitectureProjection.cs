using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// A ground-plane calibration. The vertical axis remains vertical; both ground
	// directions become exactly +/- 1/2. One GPU quad is used for every animation pose.
	public readonly record struct ArchitectureProjection(Matrix4x4 Matrix)
	{
		public static readonly ArchitectureProjection Identity = new(Matrix4x4.Identity);
		public Vector3 Project(Vector3 point) => Vector3.Transform(point, Matrix);
		public int2 Project(int2 point)
		{
			var p = Project(new Vector3(point.X, point.Y, 0));
			return new((int)MathF.Round(p.X), (int)MathF.Round(p.Y));
		}
		public int Height(int height) => height == 0 ? 0 : Math.Max(1, (int)MathF.Ceiling(height * Matrix.M22 - Matrix.M42));
		public static ArchitectureProjection Calibrate(float scale, float positiveSlope, float negativeSlope, Vector2 groundOffset)
		{
			if (scale <= 0 || positiveSlope <= negativeSlope) throw new ArgumentException("Invalid architectural ground plane.");
			var vertical = scale / (positiveSlope - negativeSlope);
			var shear = scale * .5f - vertical * positiveSlope;
			return new(new Matrix4x4(scale, shear, 0, 0, 0, vertical, 0, 0, 0, 0, 1, 0,
				-groundOffset.X * scale, -groundOffset.X * shear - groundOffset.Y * vertical, 0, 1));
		}
		public Rectangle Bounds(Rectangle source)
		{
			Span<Vector3> q = [Project(new Vector3(source.Left, source.Top, 0)), Project(new Vector3(source.Right, source.Top, 0)),
				Project(new Vector3(source.Right, source.Bottom, 0)), Project(new Vector3(source.Left, source.Bottom, 0))];
			var min = q[0]; var max = q[0];
			foreach (var p in q) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
			return Rectangle.FromLTRB((int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), (int)MathF.Ceiling(max.X), (int)MathF.Ceiling(max.Y));
		}
	}
	public static class MedievalArchitecture
	{
		// Measured foundation centres and facade slopes in the existing native plates.
		// Do not use the alpha bounding box: balconies, props and shadows are asymmetric.
		static ArchitectureProjection P(float scale, float down, float up, float x, float y, float ax = 92, float ay = 140) =>
			ArchitectureProjection.Calibrate(scale, down, up, new(x - ax, y - ay));
		static readonly ArchitectureProjection[] Houses =
		[
			// Residential facades share the village's building scale. Compact houses
			// need more enlargement than the broad longhouse; preserve ground anchors
			// so placement, snow, windows, chimneys and shadows use the same geometry.
			P(1.35f, .386f, -.516f, 92.5f, 141.5f), P(1.35f, .421f, -.518f, 93.5f, 135),
			P(1.25f, .48f, -.50f, 93.5f, 147), P(1.05f, .42f, -.50f, 92, 143),
			P(1.18f, .47f, -.52f, 92, 140), P(1.18f, .47f, -.48f, 94, 143)
		];
		static readonly Dictionary<string, ArchitectureProjection> Profiles = new(StringComparer.Ordinal)
		{
			["md.warehouse"] = P(1, .42f, -.45f, 92, 148),
			["md.sawmill"] = P(1, .50f, -.53f, 92, 145),
			["md.quarry"] = P(1, .47f, -.50f, 92, 140),
			["md.farm"] = P(1, .35f, -.55f, 93, 135),
			["md.ranch"] = P(.96f, .35f, -.426f, 92, 140),
			["md.market"] = P(.92f, .347f, -.50f, 92, 139),
			["md.forge"] = P(.94f, .406f, -.403f, 92, 141),
			["md.barracks"] = P(.94f, .50f, -.50f, 92, 137),
			["md.siegeworkshop"] = P(1, .561f, -.526f, 116, 168, 112, 164),
			["md.sheepfold"] = P(.90f, .46f, -.47f, 92, 143),
			["md.tentweaver"] = P(.90f, .422f, -.475f, 92, 150),
			["md.huntinglodge"] = P(1, .58f, -.49f, 91, 143),
			["md.goldmine"] = P(.93f, .432f, -.315f, 89, 145),
			["md.church"] = P(.97f, .55f, -.50f, 107, 225, 108, 220),
			["md.well"] = P(1, .50f, -.50f, 48, 81, 48, 84),
			// These are already built with the exact ground projection.
			["md.woodtower"] = ArchitectureProjection.Identity,
			["md.marketstall"] = ArchitectureProjection.Identity,
			["md.militarytent"] = ArchitectureProjection.Identity
		};
		public static ArchitectureProjection For(string type, int style = 0) => type == "md.house" ? Houses[Math.Clamp(style, 0, Houses.Length - 1)]
			: Profiles.TryGetValue(type == "md.foundingmarket" ? "md.market" : type, out var p) ? p : ArchitectureProjection.Identity;
		public static ArchitectureProjection For(Actor self) => For(self.Info.Name, self.TraitOrDefault<MedievalHouseAppearance>()?.Style ?? 0);
		public static IRenderable Apply(IRenderable renderable, string type, int style = 0)
		{
			var projection = For(type, style);
			return projection == ArchitectureProjection.Identity || renderable is not SpriteRenderable sprite
				? renderable : sprite.WithScreenTransform(projection.Matrix);
		}
	}
	public class MedievalArchitectureInfo : TraitInfo, Requires<MedievalBuildingInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalArchitectureRenderer();
	}
	public sealed class MedievalArchitectureRenderer : IRenderModifier, IIsometricSelectionHeightModifier
	{
		int IIsometricSelectionHeightModifier.ModifySelectionHeight(Actor self, int height) =>
			MedievalStockpile.Active(self) ? MedievalArchitecture.For(self).Height(height) : height;
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			if (!MedievalStockpile.Active(self)) { foreach (var r in renderables) yield return r; yield break; }
			var projection = MedievalArchitecture.For(self);
			if (projection == ArchitectureProjection.Identity) { foreach (var r in renderables) yield return r; yield break; }
			foreach (var r in renderables)
				// Cargo archers, annotations and environmental overlays have their own origins.
				yield return r is SpriteRenderable sprite && r.Pos == self.CenterPosition ? sprite.WithScreenTransform(projection.Matrix) : r;
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			if (!MedievalStockpile.Active(self)) { foreach (var b in bounds) yield return b; yield break; }
			var projection = MedievalArchitecture.For(self);
			if (projection == ArchitectureProjection.Identity) { foreach (var b in bounds) yield return b; yield break; }
			var at = wr.ScreenPxPosition(self.CenterPosition);
			foreach (var b in bounds)
			{
				// Stockyards and other overlays use their own world origins.
				yield return b;
				var projected = projection.Bounds(new Rectangle(b.X - at.X, b.Y - at.Y, b.Width, b.Height));
				yield return new(projected.X + at.X, projected.Y + at.Y, projected.Width, projected.Height);
			}
		}
	}
}
