using System;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalSeasonsTest
	{
		[Test]
		public void ThirtyMinuteSeasonsRepeatInOrderAndSnowMelts()
		{
			var clock = new SeasonClock(45000);
			Assert.That(clock.Snow, Is.Zero);
			for (var season = 0; season < 4; season++)
			{
				Assert.That(clock.Season, Is.EqualTo((VillageSeason)season));
				Assert.That(clock.Remaining, Is.EqualTo(30 * 60 * 25));
				for (var i = 0; i < 45000; i++) clock.Advance();
			}
			Assert.That(clock.Year, Is.EqualTo(2));
			Assert.That(clock.Season, Is.EqualTo(VillageSeason.Spring));
			Assert.That(clock.Snow, Is.EqualTo(1000));
			for (var i = 0; i < 750; i++) clock.Advance();
			Assert.That(clock.Snow, Is.Zero);
		}
		[Test]
		public void WinterGrowthIsSlowerButDoesNotDuplicateHarvest()
		{
			var info = new MedievalSourceInfo();
			FieldLoader.LoadFieldOrProperty(info, "Renewable", "True");
			FieldLoader.LoadFieldOrProperty(info, "GrowthTicks", "100");
			FieldLoader.LoadFieldOrProperty(info, "Amount", "20");
			var spring = new MedievalSource(info);
			var winter = new MedievalSource(info);
			spring.Cultivate(); winter.Cultivate();
			for (var i = 0; i < 100; i++) { spring.Advance(SeasonClock.Crops(VillageSeason.Spring)); winter.Advance(SeasonClock.Crops(VillageSeason.Winter)); }
			Assert.That(spring.Remaining, Is.EqualTo(20));
			Assert.That(winter.Remaining, Is.Zero);
			Assert.That(winter.Growth, Is.EqualTo(80));
			for (var i = 0; i < 400; i++) winter.Advance(20);
			Assert.That(winter.Harvest(50), Is.EqualTo(20));
			Assert.That(winter.Harvest(50), Is.Zero);
		}
		[Test]
		public void WinterExposureRaisesFatigueAndShelterStillRecovers()
		{
			var exposed = new SoldierFatigue(); var mild = new SoldierFatigue();
			for (var i = 0; i < 60; i++) { exposed.Advance(0, false, 0, false, true); mild.Advance(0, false, 0, false); }
			Assert.That(exposed.Value, Is.GreaterThan(mild.Value));
			var before = exposed.Value;
			exposed.Advance(0, true, 1000, true, true);
			Assert.That(exposed.Value, Is.LessThan(before));
		}
		[Test]
		public void SnowAndRainPenalizeMovementAndBowsWithinSafeBounds()
		{
			Assert.That(SeasonClock.Speed(VillageSeason.Winter, 1000), Is.LessThan(SeasonClock.Speed(VillageSeason.Spring, 0)));
			Assert.That(SeasonClock.Combat(VillageSeason.Autumn, 1000, true), Is.LessThan(SeasonClock.Combat(VillageSeason.Autumn, 1000, false)));
			foreach (var season in Enum.GetValues<VillageSeason>())
			{
				Assert.That(SeasonClock.Speed(season, int.MaxValue), Is.InRange(70, 100));
				Assert.That(SeasonClock.Combat(season, int.MaxValue, true), Is.InRange(75, 100));
				Assert.That(SeasonClock.Work(season, "hunter"), Is.GreaterThan(0));
			}
		}
	}
}
