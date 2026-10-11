using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalLocalityTest
	{
		[TestCase("  Val\t des\nPrés  ", "Val des Prés")]
		[TestCase("Saint-Martin d'Anjou", "Saint-Martin d'Anjou")]
		[TestCase("  \u0001  ", "")]
		[TestCase(null, "")]
		public void NamesAreSingleLineAndPreserveFrenchSpelling(string input, string expected)
		{ Assert.That(LocalityNames.Normalize(input), Is.EqualTo(expected)); }
		[Test]
		public void LimitDoesNotSplitUnicodeOrLeaveTrailingWhitespace()
		{
			Assert.That(LocalityNames.Normalize(new string('a', 31) + "🕊"), Is.EqualTo(new string('a', 31)));
			Assert.That(LocalityNames.Normalize(new string('a', 30) + "🕊"), Has.Length.EqualTo(32));
			Assert.That(LocalityNames.Normalize(new string('a', 31) + " village"), Is.EqualTo(new string('a', 31)));
		}
		[Test]
		public void SettlementsCannotShareTheSameNameWithinOnePlayer()
		{
			Assert.That(LocalityNames.Available("VAL DES PRÉS", ["Val des Prés", "Montbois"]), Is.False);
			Assert.That(LocalityNames.Available("", ["Val des Prés"]), Is.False);
			Assert.That(LocalityNames.Available("Montbois", ["Val des Prés"]), Is.True);
		}
		[TestCase(16 * 1024, true)]
		[TestCase(16 * 1024 + 1, false)]
		public void LocalityRangeHasAnInclusiveBoundary(int distance, bool inside)
		{
			Assert.That(LocalityNames.InRange(WPos.Zero, new WPos(distance, 0, 0), new WDist(16 * 1024)), Is.EqualTo(inside));
		}
	}
}
