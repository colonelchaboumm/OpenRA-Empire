using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalBattlefieldTest
	{
		[Test]
		public void FogNeverExposesUnknownTerrainAndClearInteriorsStayClear()
		{
			float[] hidden = [0, 0, 0, 0, 1, 0, 0, 0, 0];
			for (var y = 0; y <= 20; y++)
				for (var x = 0; x <= 20; x++)
				{
					Assert.That(VillageFog.Opacity(x / 20f - .5f, y / 20f - .5f, hidden), Is.EqualTo(1));
					Assert.That(VillageFog.Opacity(x / 20f - .5f, y / 20f - .5f, new float[9]), Is.Zero);
				}
		}
		[Test]
		public void FogBordersMatchAcrossVisibleRememberedAndUnknownTiles()
		{
			float[] a = [0, 0, 1, 0, 0, VillageFog.Remembered, 0, 0, VillageFog.Remembered];
			float[] b = [0, 1, 1, 0, VillageFog.Remembered, VillageFog.Remembered, 0, VillageFog.Remembered, VillageFog.Remembered];
			for (var i = 0; i <= 40; i++)
				Assert.That(VillageFog.Opacity(.5f, i / 40f - .5f, a), Is.EqualTo(VillageFog.Opacity(-.5f, i / 40f - .5f, b)).Within(.0001f));
			float[] edge = [0, 0, 1, 0, 0, 1, 0, 0, 1];
			var previous = 0f;
			for (var i = 0; i <= 40; i++)
			{
				var value = VillageFog.Opacity(i / 40f - .5f, 0, edge);
				Assert.That(value, Is.GreaterThanOrEqualTo(previous).And.LessThanOrEqualTo(1)); previous = value;
			}
		}
		[Test]
		public void FallenBodiesStayOnTheGroundWithoutRepeatedTumbling()
		{
			Assert.That(BattlefieldMotion.Fall(0, 0), Is.EqualTo(Matrix4x4.Identity));
			var head = new Vector3(0, -24, 0);
			var left = Vector3.Transform(head, BattlefieldMotion.Fall(24, 0));
			var right = Vector3.Transform(head, BattlefieldMotion.Fall(24, 1));
			Assert.That(left.X, Is.GreaterThan(20)); Assert.That(right.X, Is.LessThan(-20));
			Assert.That(left.Y, Is.InRange(1.99f, 2.01f));
			Assert.That(BattlefieldMotion.Fall(24, 0), Is.EqualTo(BattlefieldMotion.Fall(100000, 0)));
			for (var strip = 0; strip < 3; strip++)
			{
				Assert.That(BattlefieldMotion.Collapse(0, strip), Is.EqualTo(Matrix4x4.Identity));
				Assert.That(Vector3.Transform(new Vector3(0, -100, 0), BattlefieldMotion.Collapse(48, strip)).Y, Is.InRange(-7f, -5f));
			}
		}
		[Test]
		public void RemainsKeepRecentBattlefieldsAndBoundMemoryInLongGames()
		{
			var scene = new MedievalBattlefield();
			for (var i = 0; i < MedievalBattlefield.MaximumRemains + 10; i++) scene.Add(new BattlefieldRemains { Seed = i });
			Assert.That(scene.Remains.Count, Is.EqualTo(MedievalBattlefield.MaximumRemains));
			Assert.That(scene.Remains[0].Seed, Is.EqualTo(10));
			Assert.That(scene.Remains.Last().Seed, Is.EqualTo(MedievalBattlefield.MaximumRemains + 9));
		}
		[Test]
		public void SnowHasStableDepthSoftWindAndMeltsWithoutTurningIntoRain()
		{
			var scene = new VillageWeather(14);
			var view = new Rectangle(0, 0, 1280, 800);
			for (var i = 0; i < 80; i++) scene.Tick(view, Vector2.UnitX, .85f, true);
			Assert.That(scene.Drops.All(d => d.Snow), Is.True);
			Assert.That(scene.Drops.Max(d => d.Size) - scene.Drops.Min(d => d.Size), Is.GreaterThan(1.5f));
			var flake = scene.Drops.First(d => d.Height > 100); var depth = flake.Depth; var drift = flake.Drift;
			scene.Tick(view, -Vector2.UnitX, .85f, true);
			Assert.That(flake.Depth, Is.EqualTo(depth)); Assert.That(Math.Abs(flake.Drift - drift), Is.LessThan(.25f));
			for (var i = 0; i < 1600; i++)
			{
				scene.Tick(view, Vector2.Zero, 0, false);
				Assert.That(scene.Drops.Count, Is.LessThanOrEqualTo(VillageWeather.MaxDrops));
				Assert.That(scene.Drops.All(d => d.Snow && d.Height >= 0 && float.IsFinite(d.Ground.X)), Is.True);
			}
			Assert.That(scene.Drops, Is.Empty);
		}
	}
}
