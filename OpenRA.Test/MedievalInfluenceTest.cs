using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalInfluenceTest
	{
		[Test]
		public void ExpansionAndRetreatAreGradualAndBounded()
		{
			var radius = new InfluenceRadius();
			radius.Advance(100, 100, false);
			Assert.That(radius.Value, Is.EqualTo(InfluenceRadius.Minimum + 128));
			Assert.That(radius.Target, Is.EqualTo(InfluenceRadius.Maximum));
			for (var i = 0; i < 300; i++) radius.Advance(100, 100, false);
			Assert.That(radius.Value, Is.EqualTo(InfluenceRadius.Maximum));
			radius.Advance(0, 100, false);
			Assert.That(radius.Value, Is.EqualTo(InfluenceRadius.Maximum - 128));
			for (var i = 0; i < 300; i++) radius.Advance(0, 100, false);
			Assert.That(radius.Value, Is.EqualTo(InfluenceRadius.Minimum));
		}
		[Test]
		public void HealthAndVillageSizeLimitReach()
		{
			var healthy = new InfluenceRadius(); healthy.Advance(80, 100, false);
			var wounded = new InfluenceRadius(); wounded.Advance(80, 40, false);
			var village = new InfluenceRadius(); village.Advance(80, 100, true);
			Assert.That(wounded.Target, Is.LessThan(healthy.Target));
			Assert.That(village.Target, Is.EqualTo(healthy.Target * 3 / 4));
		}
		[TestCase(50, 50, 0)]
		[TestCase(80, 50, 0)]
		[TestCase(50, 64, 0)]
		[TestCase(50, 65, 1)]
		[TestCase(30, 90, 5)]
		[TestCase(0, 100, 6)]
		public void OnlyClearlyHappierNationsExertLimitedPressure(int own, int foreign, int expected)
			=> Assert.That(InfluenceRadius.Pressure(own, foreign), Is.EqualTo(expected));
		[Test]
		public void CompetingFrontiersUseDistanceAndRadiusWithStableTies()
		{
			var at = new WPos(5000, 0, 0);
			var near = InfluenceField.Power(WPos.Zero, at, 6000);
			var far = InfluenceField.Power(new WPos(12000, 0, 0), at, 6000);
			Assert.That(near, Is.GreaterThan(0)); Assert.That(far, Is.LessThan(0));
			Assert.That(InfluenceField.Stronger(near, 2, near, 1), Is.False);
			Assert.That(InfluenceField.Stronger(near, 1, near, 2), Is.True);
			Assert.That(InfluenceField.Stronger(0, 1, 0, 2), Is.False);
			Assert.That(InfluenceField.Stronger(InfluenceField.Power(new WPos(12000, 0, 0), at, 10000), 2, near, 1), Is.True);
		}
		[Test]
		public void SmallWorkPenaltiesKeepTheirActualRateOverTime()
		{
			foreach (var percent in new[] { 94, 97, 99, 100 })
			{
				var remainder = 0; var ticks = 0;
				for (var i = 0; i < 1000; i++) ticks += InfluenceWork.Delay(8, percent, ref remainder);
				Assert.That(ticks, Is.EqualTo(800000 / percent).Within(1));
			}
		}
		[Test]
		public void OverlayNeverRevealsUnknownGroundOrHiddenEnemyBorders()
		{
			Assert.That(MedievalInfluence.CanShow(false, false, true), Is.False);
			Assert.That(MedievalInfluence.CanShow(true, false, false), Is.False);
			Assert.That(MedievalInfluence.CanShow(true, false, true), Is.True);
			Assert.That(MedievalInfluence.CanShow(true, true, false), Is.True);
		}
	}
}
