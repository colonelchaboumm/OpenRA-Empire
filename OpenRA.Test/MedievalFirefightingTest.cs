using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalFirefightingTest
	{
		[Test]
		public void RepeatedBucketsShortenAndExtinguishFireWithoutNegativeDuration()
		{
			var remaining = SabotageRules.BurnTicks;
			for (var i = 0; i < 4; i++) remaining = FirefightingRules.Douse(remaining);
			Assert.That(remaining, Is.EqualTo(150));
			Assert.That(FirefightingRules.Douse(remaining), Is.Zero);
			Assert.That(FirefightingRules.Douse(12), Is.Zero);
			Assert.That(FirefightingRules.Douse(0), Is.Zero);
		}
	}
}
