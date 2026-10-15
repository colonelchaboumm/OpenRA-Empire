using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalManualTransferTest
	{
		[Test]
		public void GoodsStayAtSourceUntilArrivalAndBookingsProtectBothInventories()
		{
			var source = new Inventory([100, 0, 0, 0], [80, 0, 0, 0]); var cart = new Inventory([60, 0, 0, 0]);
			var transfer = ManualCargo.Reserve(source, cart, 0, 45);
			Assert.That(source.Amount(0), Is.EqualTo(80)); Assert.That(cart.Amount(0), Is.Zero);
			Assert.That(source.Take(0, 100), Is.EqualTo(35)); Assert.That(cart.Add(0, 100), Is.EqualTo(15));
			transfer.Complete(); transfer.Complete(); transfer.Cancel();
			Assert.That(source.Amount(0), Is.Zero); Assert.That(cart.Amount(0), Is.EqualTo(60));
			Assert.That(source.Reserved(0) + cart.Incoming(0), Is.Zero);
		}
		[Test]
		public void CancellationReleasesBookingsWithoutMovingCargo()
		{
			var cart = new Inventory([60, 0, 0, 0], [40, 0, 0, 0]); var depot = new Inventory([100, 0, 0, 0]);
			var transfer = ManualCargo.Reserve(cart, depot, 0, 23); transfer.Cancel(); transfer.Cancel();
			Assert.That(cart.Amount(0), Is.EqualTo(40)); Assert.That(depot.Amount(0), Is.Zero);
			Assert.That(cart.Available(0), Is.EqualTo(40)); Assert.That(depot.Free(0), Is.EqualTo(100));
		}
		[TestCase(-1, 10)] [TestCase(5, 10)] [TestCase(0, 0)] [TestCase(0, -2)] [TestCase(0, int.MaxValue)]
		public void InvalidRequestsCannotBookOrChangeStocks(int good, int amount)
		{
			var source = new Inventory([100, 0, 0, 0], [100, 0, 0, 0]); var target = new Inventory([60, 0, 0, 0]);
			var before = (source.Hash, target.Hash);
			Assert.That(ManualCargo.Reserve(source, target, good, amount), Is.Null);
			Assert.That((source.Hash, target.Hash), Is.EqualTo(before));
		}
		[Test]
		public void ConcurrentTransfersCannotOverbookAndRejectedRequestDoesNotLeakReservation()
		{
			var source = new Inventory([100, 0, 0, 0], [100, 0, 0, 0]); var target = new Inventory([60, 0, 0, 0]);
			var first = ManualCargo.Reserve(source, target, 0, 40);
			Assert.That(ManualCargo.Reserve(source, target, 0, 30), Is.Null);
			Assert.That(source.Reserved(0), Is.EqualTo(40));
			var second = ManualCargo.Reserve(source, target, 0, 20); first.Cancel(); second.Complete();
			Assert.That(source.Amount(0), Is.EqualTo(80)); Assert.That(target.Amount(0), Is.EqualTo(20));
			Assert.That(source.Reserved(0) + target.Incoming(0), Is.Zero);
		}
	}
}
