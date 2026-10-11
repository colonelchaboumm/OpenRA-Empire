using System;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Medieval;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalArchitectureTest
	{
		[TestCase(1f, .35f, -.55f, 3f, 5f)]
		[TestCase(.87f, .42f, -.5f, 0f, -4f)]
		[TestCase(.93f, .432f, -.315f, -3f, 5f)]
		public void CalibrationAlignsBothAxesAndGroundWithoutLeaningUprights(float scale, float down, float up, float x, float y)
		{
			var p = ArchitectureProjection.Calibrate(scale, down, up, new(x, y));
			var ground = p.Project(new Vector3(x, y, 0));
			Assert.That(ground.Length(), Is.LessThan(.0001));
			var right = p.Project(new Vector3(x + 24, y + 24 * down, 0)) - ground;
			var left = p.Project(new Vector3(x - 24, y - 24 * up, 0)) - ground;
			Assert.That(right.Y / right.X, Is.EqualTo(.5).Within(.0001));
			Assert.That(left.Y / left.X, Is.EqualTo(-.5).Within(.0001));
			Assert.That(p.Project(new Vector3(x, y - 100, 0)).X, Is.EqualTo(ground.X).Within(.0001));
			Assert.That(p.Project(new Vector3(x, y, 200)).Z, Is.EqualTo(200), "Depth sorting must remain unchanged.");
		}
		[Test]
		public void HousesHaveDistinctCalibrationsAndGroundDrawingsRemainUntouched()
		{
			Assert.That(MedievalArchitecture.For("md.house", 1), Is.Not.EqualTo(MedievalArchitecture.For("md.house", 2)));
			Assert.That(MedievalArchitecture.For("md.foundingmarket"), Is.EqualTo(MedievalArchitecture.For("md.market")));
			foreach (var name in new[] { "md.field.8x6", "md.sheeppasture", "md.gildedplaza", "md.gardenfence", "md.militarytent", "md.woodtower", "md.builder" })
				Assert.That(MedievalArchitecture.For(name), Is.EqualTo(ArchitectureProjection.Identity), name);
		}
		[Test]
		public void ProjectedBoundsContainAllCornersAndLightsFollowTheSameMatrix()
		{
			for (var style = 0; style < 6; style++)
			{
				var p = MedievalArchitecture.For("md.house", style); var bounds = p.Bounds(new Rectangle(-92, -140, 184, 184));
				foreach (var x in new[] { -92f, 92f }) foreach (var y in new[] { -140f, 44f })
				{
					var v = p.Project(new Vector3(x, y, 0));
					Assert.That(v.X, Is.InRange((float)bounds.Left, bounds.Right)); Assert.That(v.Y, Is.InRange((float)bounds.Top, bounds.Bottom));
				}
				var lights = new MedievalHouseLights(style); var original = HouseWindowLayout.For(style);
				Assert.That(lights.Windows.Count, Is.EqualTo(original.Count));
				for (var i = 0; i < original.Count; i++)
				{
					Assert.That(lights.Windows[i].A, Is.EqualTo(p.Project(original[i].A)));
					Assert.That(lights.Windows[i].D, Is.EqualTo(p.Project(original[i].D)));
				}
			}
		}
		[Test]
		public void SpriteClonesPreserveProjectionWithTintAlphaDepthAndPositionChanges()
		{
			using var sheet = new Sheet(SheetType.BGRA, new Size(32, 32));
			var sprite = new Sprite(sheet, new Rectangle(0, 0, 20, 20), TextureChannel.RGBA);
			var matrix = MedievalArchitecture.For("md.church").Matrix;
			var renderable = new SpriteRenderable(sprite, WPos.Zero, WVec.Zero, 5, null, 1, 1, Vector3.One, TintModifiers.None, false).WithScreenTransform(matrix);
			IRenderable[] clones = [renderable.WithPalette(null), renderable.WithZOffset(8), renderable.OffsetBy(new WVec(100, 200, 0)),
				renderable.AsDecoration(), renderable.WithAlpha(.5f), renderable.WithTint(Vector3.Zero, TintModifiers.ReplaceColor)];
			foreach (var clone in clones) Assert.That(((SpriteRenderable)clone).ScreenTransform, Is.EqualTo(matrix));
			Assert.That(((SpriteRenderable)clones[1]).ZOffset, Is.EqualTo(8));
			Assert.That(clones[2].Pos, Is.EqualTo(new WPos(100, 200, 0)));
			Assert.That(((SpriteRenderable)clones[4]).Alpha, Is.EqualTo(.5f));
		}
	}
}
