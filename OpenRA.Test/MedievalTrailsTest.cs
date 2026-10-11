using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalTrailsTest
	{
		[Test]
		public void RenderInvalidationFollowsWearLevelsAndRemovalWithoutChangingSimulation()
		{
			var observed = new FootfallTrails(); var reference = new FootfallTrails(); var invalidations = 0;
			observed.VisualChanged += _ => invalidations++;
			var key = new int2(3, 4);
			for (var i = 0; i < 32; i++) { observed.Pass(key, i, 1); reference.Pass(key, i, 1); }
			Assert.That(invalidations, Is.EqualTo(6));
			Assert.That(observed.StateHash, Is.EqualTo(reference.StateHash));
			for (var t = 100; t < 800; t += 20) { observed.Recover(t, 50, 20); reference.Recover(t, 50, 20); }
			Assert.That(observed.Marks, Is.Empty); Assert.That(observed.StateHash, Is.EqualTo(reference.StateHash));
			Assert.That(invalidations, Is.EqualTo(13), "Six growing levels, six fading levels, then removal of the final invisible mark.");
		}

		[Test]
		public void SpawningAndStandingStillDoNotWearTheGround()
		{
			var paths = new FootfallTrails();
			for (var tick = 0; tick < 2000; tick++) paths.Track(1, new WPos(500, 500, 0), tick, 1, _ => true);
			Assert.That(paths.Marks, Is.Empty);
		}
		[Test]
		public void RepeatedPassagesProgressFromGrassToDirtAndSaturate()
		{
			var paths = new FootfallTrails();
			var key = new int2(2, 3);
			paths.Pass(key, 1, 1);
			Assert.That(paths.Marks[key].Level, Is.Zero);
			paths.Pass(key, 2, 1);
			Assert.That(paths.Marks[key].Level, Is.EqualTo(1));
			for (var i = 0; i < 60; i++) paths.Pass(key, i + 3, 1);
			Assert.That(paths.Marks[key].Wear, Is.EqualTo(FootfallTrails.MaximumWear));
			Assert.That(paths.Marks[key].Level, Is.EqualTo(6));
		}
		[Test]
		public void CartsWearFasterAndCrossedSamplesRemainConnected()
		{
			var pedestrian = new FootfallTrails();
			var cart = new FootfallTrails();
			foreach (var paths in new[] { pedestrian, cart })
			{
				paths.Track(1, new WPos(128, 128, 0), 0, 1, _ => true);
				paths.Track(1, new WPos(1152, 1152, 0), 1, paths == cart ? 2 : 1, _ => true);
				Assert.That(paths.Marks.Count, Is.EqualTo(4));
				Assert.That(paths.Marks.Keys.OrderBy(k => k.X), Is.EqualTo(Enumerable.Range(1, 4).Select(i => new int2(i, i))));
			}
			Assert.That(cart.Marks.Values.Sum(v => v.Wear), Is.EqualTo(pedestrian.Marks.Values.Sum(v => v.Wear) * 2));
		}
		[Test]
		public void TeleportsAndProtectedGroundDoNotCreatePaths()
		{
			var paths = new FootfallTrails();
			paths.Track(1, new WPos(128, 128, 0), 0, 1, _ => true);
			paths.Track(1, new WPos(10000, 10000, 0), 1, 1, _ => true);
			paths.Track(1, new WPos(10500, 10000, 0), 2, 1, _ => false);
			Assert.That(paths.Marks, Is.Empty);
		}
		[Test]
		public void NeglectedPathsRegrowGraduallyAndDisappearCompletely()
		{
			var paths = new FootfallTrails();
			var key = new int2(1, 2);
			paths.Pass(key, 0, 10);
			paths.Recover(100, 100, 20);
			Assert.That(paths.Marks[key].Wear, Is.EqualTo(10));
			paths.Recover(120, 100, 20);
			Assert.That(paths.Marks[key].Wear, Is.EqualTo(9));
			paths.Recover(120, 100, 20);
			Assert.That(paths.Marks[key].Wear, Is.EqualTo(9), "Recovery cannot depend on frame rate.");
			paths.Recover(300, 100, 20);
			Assert.That(paths.Marks, Is.Empty);
			Assert.That(paths.StateHash, Is.Zero);
		}
		[Test]
		public void FreshTrafficRestartsTheRegrowthDelay()
		{
			var paths = new FootfallTrails();
			var key = new int2(1, 2);
			paths.Pass(key, 0, 5);
			paths.Pass(key, 99, 1);
			paths.Recover(120, 100, 20);
			Assert.That(paths.Marks[key].Wear, Is.EqualTo(6));
			paths.Recover(219, 100, 20);
			Assert.That(paths.Marks[key].Wear, Is.EqualTo(5));
		}
		[Test]
		public void SameWalkProducesIdenticalWearAtDifferentSamplingRates()
		{
			var paths = new[] { new FootfallTrails(), new FootfallTrails() };
			foreach (var p in paths) p.Track(1, new WPos(128, 128, 0), 0, 1, _ => true);
			paths[0].Track(1, new WPos(1152, 128, 0), 1, 1, _ => true);
			for (var x = 192; x <= 1152; x += 64) paths[1].Track(1, new WPos(x, 128, 0), 1, 1, _ => true);
			Assert.That(paths[0].Marks, Is.EquivalentTo(paths[1].Marks));
			Assert.That(paths[0].StateHash, Is.EqualTo(paths[1].StateHash));
		}
	}
}
