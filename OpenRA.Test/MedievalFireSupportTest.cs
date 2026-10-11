using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalFireSupportTest
	{
		[Test]
		public void HeatThawsLocallyThenRefreezesWithoutAnActiveFire()
		{
			var heat = new FireHeatField(); var origin = new WPos(8192, 8192, 0);
			Assert.That(heat.SnowRemaining(origin), Is.EqualTo(1));
			heat.Warm(origin, 2400);
			Assert.That(heat.SnowRemaining(origin), Is.InRange(.85, .95));
			for (var i = 0; i < 35; i++) { heat.Cool(); heat.Warm(origin, 2400); }
			Assert.That(heat.SnowRemaining(origin), Is.EqualTo(0));
			Assert.That(heat.SnowRemaining(origin + new WVec(6000, 0, 0)), Is.EqualTo(1));
			for (var i = 0; i < 160; i++) heat.Cool();
			Assert.That(heat.SnowRemaining(origin), Is.EqualTo(1)); Assert.That(heat.Count, Is.Zero);
		}
		[Test]
		public void HeatIsContinuousAcrossCellSeamsAndBoundedOnHugeMaps()
		{
			var heat = new FireHeatField();
			for (var i = 0; i < 25; i++) heat.Warm(new WPos(-450, 1500, 0), 2800);
			Assert.That(heat.SnowRemaining(new WPos(-1, 1024, 0)), Is.EqualTo(heat.SnowRemaining(new WPos(1, 1024, 0))).Within(.002));
			for (var i = 0; i < 2000; i++) heat.Warm(new WPos(i * 8000, 20000, 0), 2800);
			Assert.That(heat.Count, Is.LessThanOrEqualTo(FireHeatField.MaximumSamples));
		}
	}
}
