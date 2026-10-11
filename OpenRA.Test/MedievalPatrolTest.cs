using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalPatrolTest
	{
		[Test]
		public void RouteRoundTripPreservesSignedIsometricCoordinates()
		{
			CPos[] cells = [new(64, -32), new(71, -18), new(61, -3)];
			Assert.That(PatrolRoute.TryDecode(PatrolRoute.Encode(cells), _ => true, out var result), Is.True);
			Assert.That(result, Is.EqualTo(cells));
		}
		[TestCase("")]
		[TestCase("1,2")]
		[TestCase("1,2;1,2")]
		[TestCase("1,2;bad")]
		[TestCase("1,2;2147483647,0")]
		[TestCase("1,2;3,4,5")]
		public void MalformedOrDegenerateRoutesAreRejected(string text) => Assert.That(PatrolRoute.TryDecode(text, _ => true, out _), Is.False);
		[Test]
		public void RouteLimitsAndForbiddenCellsAreEnforced()
		{
			var points = Enumerable.Range(0, 17).Select(i => new CPos(i, -1)).ToArray();
			Assert.That(PatrolRoute.TryDecode(PatrolRoute.Encode(points.Take(16)), _ => true, out _), Is.True);
			Assert.That(PatrolRoute.TryDecode(PatrolRoute.Encode(points), _ => true, out _), Is.False);
			Assert.That(PatrolRoute.TryDecode("1,2;3,4", p => p.X != 3, out _), Is.False);
		}
		[TestCase(12000, 3000)]
		[TestCase(1000, 500)]
		[TestCase(100, 50)]
		public void NightWatchRepeatsAtDuskAndEndsAtDawn(int day, int night)
		{
			var clock = new DayClock(day, night);
			var starts = 0; var ends = 0; var wasOnWatch = false;
			for (var i = 0; i <= 2 * (day + night); i++)
			{
				Assert.That(clock.NightWatch, Is.EqualTo(clock.Darkness > 0));
				if (clock.NightWatch && !wasOnWatch) starts++;
				if (!clock.NightWatch && wasOnWatch) ends++;
				wasOnWatch = clock.NightWatch; clock.Advance();
			}
			Assert.That(starts, Is.EqualTo(2)); Assert.That(ends, Is.EqualTo(2));
		}
	}
}
