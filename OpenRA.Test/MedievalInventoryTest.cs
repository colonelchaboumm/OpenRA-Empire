using System;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalInventoryTest
	{
		[Test]
		public void ReservationIsNotPhysicalMovement()
		{
			var source = new Inventory([100, 0, 0, 0], [40, 0, 0, 0]);
			var target = new Inventory([100, 0, 0, 0]);
			Assert.That(source.ReserveOut(0, 20));
			Assert.That(target.ReserveIn(0, 20));
			Assert.That(source.Amount(0), Is.EqualTo(40));
			Assert.That(source.Available(0), Is.EqualTo(20));
			Assert.That(target.Amount(0), Is.Zero);
			source.Load(0, 20);
			Assert.That(source.Amount(0), Is.EqualTo(20));
			Assert.That(target.Amount(0), Is.Zero);
			target.Unload(0, 20);
			Assert.That(target.Amount(0), Is.EqualTo(20));
			Assert.That(target.Incoming(0), Is.Zero);
		}
		[Test]
		public void CompetingCarriersCannotReserveTheSameGoodsOrSpace()
		{
			var s = new Inventory([30, 0, 0, 0], [30, 0, 0, 0]);
			Assert.That(s.ReserveOut(0, 20));
			Assert.That(s.ReserveOut(0, 20), Is.False);
			var d = new Inventory([30, 0, 0, 0]);
			Assert.That(d.ReserveIn(0, 20));
			Assert.That(d.ReserveIn(0, 20), Is.False);
			Assert.That(d.Add(0, 20), Is.EqualTo(10));
			d.Unload(0, 20);
			Assert.That(d.Amount(0), Is.EqualTo(30));
		}
		[Test]
		public void CancellationReleasesBothReservationsWithoutCreatingResources()
		{
			var s = new Inventory([50, 0, 0, 0], [40, 0, 0, 0]);
			var d = new Inventory([50, 0, 0, 0]);
			s.ReserveOut(0, 20); d.ReserveIn(0, 20);
			s.ReleaseOut(0, 20); d.ReleaseIn(0, 20);
			Assert.That(s.Available(0), Is.EqualTo(40));
			Assert.That(d.Free(0), Is.EqualTo(50));
			Assert.That(d.Amount(0), Is.Zero);
		}
		[Test]
		public void FoodAndMeatHaveIndependentCapacityAndBalance()
		{
			var stock = new Inventory([0, 20, 7, 0]);
			Assert.That(stock.Add(1, 50), Is.EqualTo(20));
			Assert.That(stock.Add(2, 50), Is.EqualTo(7));
			stock.Take(2, 4);
			Assert.That(stock.Amount(1), Is.EqualTo(20));
			Assert.That(stock.Amount(2), Is.EqualTo(3));
		}
		[TestCase(1)]
		[TestCase(7)]
		[TestCase(20)]
		[TestCase(200)]
		public void RepeatedPartialTransfersConserveEveryUnit(int capacity)
		{
			var source = new Inventory([1000, 0, 0, 0], [1000, 0, 0, 0]);
			var target = new Inventory([capacity, 0, 0, 0]);
			var used = 0;
			for (var i = 0; i < 1200; i++)
			{
				var n = Math.Min(13, Math.Min(source.Available(0), target.Free(0)));
				if (n > 0)
				{
					source.ReserveOut(0, n); target.ReserveIn(0, n);
					source.Load(0, n); target.Unload(0, n);
				}
				used += target.Take(0, 3);
				Assert.That(source.Amount(0) + target.Amount(0) + used, Is.EqualTo(1000));
			}
			Assert.That(used, Is.EqualTo(1000));
		}
		[Test]
		public void ReservationsCannotBeReleasedTwice()
		{
			var stock = new Inventory([10, 0, 0, 0]);
			stock.ReserveIn(0, 10); stock.Unload(0, 10);
			Assert.Throws<InvalidOperationException>(() => stock.Unload(0, 10));
			Assert.That(stock.Amount(0), Is.EqualTo(10));
		}
	}
}
