using System;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalCampingTest
	{
		[Test]
		public void EightMinuteDayTwoMinuteNightRepeatAtExactSimulationBoundaries()
		{
			var clock = new DayClock(12000, 3000);
			for (var cycle = 0; cycle < 3; cycle++)
			{
				for (var tick = 0; tick < 12000; tick++) { Assert.That(clock.Night, Is.False); clock.Advance(); }
				Assert.That(clock.Remaining, Is.EqualTo(3000));
				for (var tick = 0; tick < 3000; tick++) { Assert.That(clock.Night, Is.True); clock.Advance(); }
				Assert.That(clock.Phase, Is.Zero);
			}
			Assert.Throws<ArgumentOutOfRangeException>(() => new DayClock(0, 1));
		}
		[Test]
		public void ShelterProtectsAgainstRainAndFatigueRecoversWithoutNegativeValues()
		{
			var marcher = new SoldierFatigue(); var dry = new SoldierFatigue(); var rainy = new SoldierFatigue();
			for (var i = 0; i < 100; i++) { marcher.Advance(6144, false, 0, false); dry.Advance(0, true, 0, false); rainy.Advance(0, true, 1000, false); }
			Assert.That(marcher.Value, Is.EqualTo(10000)); Assert.That(marcher.Speed, Is.EqualTo(65)); Assert.That(marcher.Firepower, Is.EqualTo(75));
			Assert.That(rainy.Value, Is.GreaterThan(dry.Value));
			for (var i = 0; i < 100; i++) { dry.Advance(0, true, 1000, true); rainy.Advance(0, true, 1000, true); }
			Assert.That(rainy.Value, Is.Zero); Assert.That(dry.Value, Is.Zero); Assert.That(rainy.Speed, Is.EqualTo(100));
		}
		[Test]
		public void CampDeliveriesReserveSingleStockAndCancellationDoesNotDuplicateTents()
		{
			var depot = new Inventory([100, 12, 0, 0]); var cart = new Inventory([0, 4, 0, 0]); depot.Add(1, 1);
			Assert.That(depot.ReserveOut(1, 1), Is.True); Assert.That(depot.ReserveOut(1, 1), Is.False);
			Assert.That(cart.ReserveIn(1, 1), Is.True); depot.Load(1, 1);
			Assert.That(depot.Amount(1) + cart.Amount(1), Is.Zero);
			cart.ReleaseIn(1, 1); Assert.That(depot.ReserveIn(1, 1), Is.True); depot.Unload(1, 1);
			Assert.That(depot.Amount(1) + cart.Amount(1), Is.EqualTo(1));
		}
		[Test]
		public void RainPresentationCannotChangeAuthoritativeRainIntensity()
		{
			var left = new VillageWeather(1, 1, 100, 100); var right = new VillageWeather(500, 5000, 800, 200);
			for (var i = 0; i < 500; i++)
			{
				var intensity = i % 200 / 200f;
				left.Tick(new Rectangle(0, 0, 1280, 800), Vector2.Zero, intensity);
				right.Tick(new Rectangle(5000, 5000, 400, 300), Vector2.One, intensity);
				Assert.That(left.Intensity, Is.EqualTo(right.Intensity));
			}
		}
	}
}
