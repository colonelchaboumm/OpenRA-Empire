using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalHouseTest
	{
		[TestCase(0u)]
		[TestCase(41u)]
		[TestCase(4294967295u)]
		public void EachGroupOfSixUsesEveryStyleAndNeverRepeatsAtBoundaries(uint seed)
		{
			var styles = Enumerable.Range(0, 120).Select(i => HouseStyleDeck.At(i, seed)).ToArray();
			for (var i = 0; i < styles.Length; i += HouseStyleDeck.Count)
				Assert.That(styles.Skip(i).Take(HouseStyleDeck.Count).Distinct().Count(), Is.EqualTo(HouseStyleDeck.Count));
			for (var i = 1; i < styles.Length; i++) Assert.That(styles[i], Is.Not.EqualTo(styles[i - 1]));
		}
		[Test]
		public void RepeatedPreviewsCannotConsumeOrChangeTheNextStyle()
		{
			var before = Enumerable.Range(0, 18).Select(i => HouseStyleDeck.At(i, 2718)).ToArray();
			for (var i = 0; i < 1000; i++) HouseStyleDeck.At(7, 2718);
			Assert.That(Enumerable.Range(0, 18).Select(i => HouseStyleDeck.At(i, 2718)), Is.EqualTo(before));
			Assert.That(Enumerable.Range(0, 18).Select(i => HouseStyleDeck.At(i, 3141)), Is.Not.EqualTo(before));
		}
	}
}
