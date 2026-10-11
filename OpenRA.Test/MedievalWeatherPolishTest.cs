using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalWeatherPolishTest
	{
		[Test]
		public void ThunderFollowsRareLightningAndWinterPreventsNewStorms()
		{
			var storm = new StormClock();
			for (var i = 0; i < 1000; i++) storm.Advance(600, false);
			Assert.That(storm.Strikes, Is.Zero);
			for (var i = 0; i < 150; i++) storm.Advance(900, false);
			Assert.That(storm.Strikes, Is.EqualTo(1));
			Assert.That(storm.Flash, Is.GreaterThan(0));
			Assert.That(storm.ThunderDelay, Is.InRange(12, 55));
			for (var i = 0; i < 12; i++) storm.Advance(900, false);
			Assert.That(storm.Flash, Is.Zero);
			for (var i = 0; i < 2000; i++) storm.Advance(1000, true);
			Assert.That(storm.Strikes, Is.EqualTo(1));
			Assert.That(storm.Active, Is.False);
		}
		[Test]
		public void StormScheduleIsDeterministicAndBoundedAcrossRainChanges()
		{
			var a = new StormClock(); var b = new StormClock(); var previous = -1000;
			for (var i = 0; i < 15000; i++)
			{
				var rain = i % 1700 < 1200 ? 850 : 200;
				a.Advance(rain, false); b.Advance(rain, false);
				Assert.That(a.Strikes, Is.EqualTo(b.Strikes));
				Assert.That(a.Flash, Is.InRange(0f, 1f));
				if (a.Age != 0) continue;
				Assert.That(i - previous, Is.GreaterThanOrEqualTo(150)); previous = i;
			}
			Assert.That(a.Strikes, Is.GreaterThan(10));
		}
		[Test]
		public void SnowFollowsHouseVariantsGateMovementAndEnclosureSize()
		{
			for (var i = 0; i < 6; i++) Assert.That(SnowGeometry.For("md.house", i, new(2, 2)), Is.Not.Empty);
			var closed = SnowGeometry.For("md.woodgate", 0, new(3, 1));
			var open = SnowGeometry.For("md.woodgate", 8, new(3, 1));
			Assert.That(open[0].A, Is.EqualTo(closed[0].A), "Hinge must remain fixed");
			Assert.That(open[0].B, Is.Not.EqualTo(closed[0].B), "Snow must follow the swinging leaf");
			Assert.That(SnowGeometry.For("md.sheeppasture.10x10", 0, new(10, 10)).Length,
				Is.GreaterThan(SnowGeometry.For("md.sheeppasture", 0, new(2, 2)).Length));
			Assert.That(SnowGeometry.For("md.quarry", 0, new(3, 2)), Is.Empty, "No invented roof on the crane");
		}
		[Test]
		public void RefugeStartsAtThunderAndEndsAfterEightSecondsOfCalm()
		{
			var storm = new StormClock();
			for (var i = 0; i < 150; i++) storm.Advance(900, false);
			Assert.That(storm.ShelterTicks, Is.Zero, "Lightning alone must not trigger the flight");
			while (!storm.ThunderNow) storm.Advance(900, false);
			Assert.That(storm.ShelterTicks, Is.EqualTo(StormClock.CalmTicks));
			for (var i = 0; i < 1000; i++) storm.Advance(900, false);
			Assert.That(storm.ShelterTicks, Is.EqualTo(200), "Stay inside between thunderclaps");
			for (var i = 0; i < 199; i++) storm.Advance(0, false);
			Assert.That(storm.ShelterTicks, Is.EqualTo(1));
			storm.Advance(0, false); Assert.That(storm.ShelterTicks, Is.Zero);
		}
		[Test]
		public void AllCowAndSheepPenSizesHaveGroundSnowAndFenceCaps()
		{
			foreach (var kind in new[] { "md.pasture", "md.sheeppasture" })
				for (var x = 2; x <= 10; x++)
					for (var y = 2; y <= 10; y++)
					{
						var surfaces = SnowGeometry.For($"{kind}.{x}x{y}", 0, new(x, y));
						Assert.That(surfaces[0].Ground, Is.True);
						Assert.That(surfaces[0].C.Y - surfaces[0].A.Y, Is.GreaterThan((x + y) * 12 - 2));
						Assert.That(surfaces[1].Rail, Is.True);
					}
		}
	}
}
