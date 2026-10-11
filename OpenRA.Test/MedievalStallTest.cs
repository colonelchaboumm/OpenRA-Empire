using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalStallTest
	{
		[TestCase(0, 0)]
		[TestCase(2, 8)]
		[TestCase(12, 24)]
		[TestCase(100, 80)]
		public void FoodBufferFollowsNearbyPopulation(double demand, int expected)
		{
			Assert.That(StallDemand.Food(demand, 80), Is.EqualTo(expected));
		}
		[Test]
		public void SeveralHaulersCannotBookTheSameDemand()
		{
			var stock = new Inventory([60, 80, 0, 60]);
			stock.Add(0, 12);
			Assert.That(StallDemand.Request(30, stock, 0), Is.EqualTo(18));
			stock.ReserveIn(0, 10);
			Assert.That(StallDemand.Request(30, stock, 0), Is.EqualTo(8));
			stock.ReserveIn(0, 8);
			Assert.That(StallDemand.Request(30, stock, 0), Is.Zero);
			stock.ReleaseIn(0, 10);
			Assert.That(StallDemand.Request(30, stock, 0), Is.EqualTo(10));
		}
		[Test]
		public void ForgeRequestsOnlyMissingMaterialsIncludingDeliveriesAlreadyBooked()
		{
			var inputs = new Inventory([20, 40, 20, 20]);
			inputs.Add(0, 2); inputs.ReserveIn(0, 3);
			Assert.That(StallDemand.Workshop(6, inputs, 0), Is.EqualTo(1));
			inputs.Unload(0, 3);
			Assert.That(StallDemand.Workshop(6, inputs, 0), Is.EqualTo(1));
			inputs.ReserveIn(0, 1);
			Assert.That(StallDemand.Workshop(6, inputs, 0), Is.Zero);
			Assert.That(StallDemand.Workshop(0, inputs, 0), Is.Zero);
			Assert.That(StallDemand.Workshop(2, inputs, 3), Is.EqualTo(2));
		}
		[Test]
		public void FinishedOrCancelledSiteStopsNewDeliveriesWithoutDeletingStock()
		{
			var stock = new Inventory([60, 80, 0, 60]);
			stock.Add(3, 10); stock.ReserveIn(3, 5);
			Assert.That(StallDemand.Request(0, stock, 3), Is.Zero);
			stock.Unload(3, 5);
			Assert.That(stock.Amount(3), Is.EqualTo(15));
			Assert.That(StallDemand.Request(20, stock, 3), Is.EqualTo(5));
			Assert.That(StallDemand.Request(500, stock, 3), Is.EqualTo(45));
			Assert.That(StallDemand.Request(500, stock, 2), Is.Zero);
		}
	}
}
