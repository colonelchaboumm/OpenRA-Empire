using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalGoldTest
	{
		[Test]
		public void LegacyInventoryHasNoGoldCapacityAndCannotCreateIt()
		{
			var legacy = new Inventory([100, 100, 100, 100], [20, 30, 40, 50]);
			Assert.That(legacy.Capacity(4), Is.Zero);
			Assert.That(legacy.Add(4, 10), Is.Zero);
			Assert.That(legacy.Amount(3), Is.EqualTo(50));
		}
		[Test]
		public void GoldReservationsPreventDuplicateLoadsAndParticipateInHash()
		{
			var stock = new Inventory([0, 0, 0, 0, 50], [0, 0, 0, 0, 40]);
			var before = stock.Hash;
			Assert.That(stock.ReserveOut(4, 30));
			Assert.That(stock.ReserveOut(4, 30), Is.False);
			Assert.That(stock.Hash, Is.Not.EqualTo(before));
			Assert.That(stock.Amount(4), Is.EqualTo(40));
			stock.ReleaseOut(4, 30);
			Assert.That(stock.Hash, Is.EqualTo(before));
		}
		[Test]
		public void GoldTradePreservesTheEntireCargoWhenInterruptedAndRerouted()
		{
			var source = new Inventory([0, 0, 0, 0, 100], [0, 0, 0, 0, 75]);
			var cargo = new Inventory([0, 0, 0, 0, 40]);
			var target = new Inventory([0, 0, 0, 0, 60]);
			var trip = new TradeShipment(cargo, target); trip.Load(source, 1 << 4, 40);
			Assert.That(trip.Total, Is.EqualTo(40)); Assert.That(target.Incoming(4), Is.EqualTo(40));
			trip.Cancel();
			Assert.That(target.Incoming(4), Is.Zero); Assert.That(cargo.Amount(4), Is.EqualTo(40));
			var returnTrip = new TradeShipment(cargo, source); returnTrip.BookCarriedCargo();
			Assert.That(returnTrip.Unload(), Is.EqualTo(40)); Assert.That(source.Amount(4), Is.EqualTo(75));
			Assert.That(cargo.Amount(4), Is.Zero);
		}
	}
}
