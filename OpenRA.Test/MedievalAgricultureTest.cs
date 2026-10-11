using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalAgricultureTest
	{
		[Test]
		public void BothDrawingDirectionsProduceTheSameRectangleAndFootprint()
		{
			var zone = AgriculturalZone.Between(new CPos(12, 16), new CPos(9, 14));
			Assert.That(zone, Is.EqualTo(AgriculturalZone.Between(new CPos(9, 14), new CPos(12, 16))));
			Assert.That(zone.Start, Is.EqualTo(new CPos(9, 14)));
			Assert.That(zone.Size, Is.EqualTo(new CVec(4, 3)));
			Assert.That(zone.Cells().Distinct().Count(), Is.EqualTo(12));
			Assert.That(zone.Cells().Contains(new CPos(12, 16)));
			Assert.That(zone.ActorType("md.pasture"), Is.EqualTo("md.pasture.4x3"));
		}
		[TestCase(1, 4, false)]
		[TestCase(4, 1, false)]
		[TestCase(2, 2, true)]
		[TestCase(10, 10, true)]
		[TestCase(11, 4, false)]
		[TestCase(4, 11, false)]
		public void ZoneDimensionsAreBoundedBeforeActorLookup(int width, int height, bool valid)
			=> Assert.That(new AgriculturalZone(new CPos(5, 5), new CVec(width, height)).ValidSize, Is.EqualTo(valid));
		[Test]
		public void CapacityGrowsWithAreaAndDistinguishesFarmingFromBreeding()
		{
			Assert.That(AgriculturalZone.Capacity(4, 4), Is.EqualTo(1));
			Assert.That(AgriculturalZone.Capacity(16, 4), Is.EqualTo(4));
			Assert.That(AgriculturalZone.Capacity(16, 8), Is.EqualTo(2));
			Assert.That(AgriculturalZone.Capacity(18, 8), Is.EqualTo(3));
		}
		[Test]
		public void FarmAccessUsesTheNearestZoneEdgeIncludingTheEightCellBoundary()
		{
			Assert.That(MedievalAgriculturalArea.DistanceToRectangle(new CPos(20, 20), new CVec(3, 3), new CPos(30, 20), new CVec(10, 4)), Is.EqualTo(64));
			Assert.That(MedievalAgriculturalArea.DistanceToRectangle(new CPos(20, 20), new CVec(3, 3), new CPos(31, 20), new CVec(10, 4)), Is.GreaterThan(64));
			Assert.That(MedievalAgriculturalArea.DistanceToRectangle(new CPos(20, 20), new CVec(3, 3), new CPos(10, 20), new CVec(10, 4)), Is.EqualTo(1));
		}
	}
}
