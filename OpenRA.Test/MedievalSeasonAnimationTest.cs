using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalSeasonAnimationTest
	{
		static readonly Rectangle View = new(0, 0, 1280, 800);
		[Test]
		public void SnowAccumulatesSmoothlyAcrossTileBoundariesAndMeltsCompletely()
		{
			for (var x = -10000; x < 10000; x += 347)
			{
				Assert.That(SeasonClock.SnowCoverage(x, 1257, 0), Is.Zero);
				Assert.That(SeasonClock.SnowCoverage(x, 1257, 1000), Is.EqualTo(1).Within(.0001f));
				var previous = 0f;
				for (var snow = 0; snow <= 1000; snow += 20)
				{
					var coverage = SeasonClock.SnowCoverage(x, 1257, snow);
					Assert.That(coverage, Is.GreaterThanOrEqualTo(previous)); previous = coverage;
					Assert.That(Math.Abs(coverage - SeasonClock.SnowCoverage(x + 1, 1257, snow)), Is.LessThan(.001f));
				}
			}
		}
		[Test]
		public void SeasonTransitionsHaveNoColorJumpIncludingNewYear()
		{
			var clock = new SeasonClock(1000);
			var previous = clock.FoliageTint(7, false);
			for (var t = 0; t < 9000; t++)
			{
				clock.Advance(); var tint = clock.FoliageTint(7, false);
				Assert.That(Vector3.Distance(previous, tint), Is.LessThan(.006f)); previous = tint;
				Assert.That(Enum.GetValues<VillageSeason>().Sum(clock.Weight), Is.EqualTo(1).Within(.0001f));
			}
		}
		[Test]
		public void SnowflakesVarySettleAndRemainBoundedThroughLongWinters()
		{
			var weather = new VillageWeather(19);
			var settled = false;
			for (var t = 0; t < 10000; t++)
			{
				weather.Tick(View, new Vector2(.8f, .1f), .85f, true);
				settled |= weather.Drops.Any(d => d.Splash > 0);
				Assert.That(weather.Drops.Count, Is.LessThanOrEqualTo(VillageWeather.MaxDrops));
				Assert.That(weather.Drops.All(d => d.Height >= 0 && d.Opacity >= 0 && d.Opacity <= 1 && float.IsFinite(d.Ground.X)), Is.True);
			}
			Assert.That(settled, Is.True);
			Assert.That(weather.Drops.Max(d => d.Size) - weather.Drops.Min(d => d.Size), Is.GreaterThan(.8f));
			Assert.That(weather.Drops.Max(d => d.Depth) - weather.Drops.Min(d => d.Depth), Is.GreaterThan(.5f));
		}
		[Test]
		public void WinterThawDoesNotTurnExistingFlakesIntoRaindropsAndDryWeatherClearsThem()
		{
			var weather = new VillageWeather(42);
			for (var t = 0; t < 40; t++) weather.Tick(View, Vector2.Zero, 1, true);
			var before = weather.Drops.ToArray();
			weather.Tick(View, Vector2.Zero, 1, false);
			Assert.That(before.All(d => d.Snow), Is.True);
			for (var t = 0; t < 1200; t++) weather.Tick(View, Vector2.Zero, 0, false);
			Assert.That(weather.Drops, Is.Empty);
		}
		[Test]
		public void LeavesSettleThenFadeAndWinterStopsEmittingThem()
		{
			var scene = new VillageAmbience(77);
			AmbientEmitter[] trees = [new(new Vector2(300, 300), 60, AmbientKind.Leaf)];
			var settled = false;
			for (var t = 0; t < 1000; t++)
			{
				scene.Tick(View, trees, VillageSeason.Autumn);
				settled |= scene.Particles.Any(p => p.Settled);
			}
			Assert.That(settled, Is.True);
			for (var t = 0; t < 40; t++) scene.Tick(View, trees, VillageSeason.Winter);
			Assert.That(scene.Particles.All(p => p.Kind == AmbientKind.Bird), Is.True);
		}
		[Test]
		public void CameraJumpImmediatelyRecyclesThePrecipitationPool()
		{
			var weather = new VillageWeather(5);
			for (var t = 0; t < 60; t++) weather.Tick(View, Vector2.Zero, 1, true);
			var before = weather.Drops.ToArray();
			weather.Tick(new Rectangle(100000, 100000, 1280, 800), Vector2.Zero, 1, true);
			Assert.That(weather.Drops.Intersect(before), Is.Empty);
			Assert.That(weather.Drops, Is.Not.Empty);
		}
	}
}
