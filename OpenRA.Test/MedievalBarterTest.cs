using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalBarterTest
	{
		static Inventory[] Stores(int amount = 0, int capacity = 200)
		{
			var material = new Inventory(Enumerable.Repeat(capacity, 5).ToArray(), Enumerable.Repeat(amount, 5).ToArray());
			var equipment = new Inventory(Enumerable.Repeat(capacity, 5).ToArray(), Enumerable.Repeat(amount, 5).ToArray());
			var camp = new Inventory(Enumerable.Repeat(capacity, 5).ToArray(), Enumerable.Repeat(amount, 5).ToArray());
			return Enumerable.Range(0, 11).Select(g => g < 5 ? material : g < 8 ? equipment : camp).ToArray();
		}
		static int[] Basket(int amount) => Enumerable.Repeat(amount, 11).ToArray();
		static int Amount(Inventory[] stocks) => Enumerable.Range(0, 11).Sum(g => stocks[g].Amount(StorageGoods.Index(g)));
		[Test]
		public void BothPaymentsAreReservedAndAllGoodsTravelInSeveralLoads()
		{
			var a = Stores(100); var b = Stores(100); var cartA = Stores(); var cartB = Stores();
			var first = new BarterCargo(a, b, Basket(15)); var second = new BarterCargo(b, a, Basket(7));
			Assert.That(BarterCargo.ReserveExchange(first, second), Is.True);
			Assert.That(Amount(a), Is.EqualTo(1100)); Assert.That(Amount(b), Is.EqualTo(1100));
			Assert.That(a[7].Available(2), Is.EqualTo(85)); Assert.That(b[10].Incoming(2), Is.EqualTo(15));
			Assert.That(first.Load(cartA), Is.EqualTo(60)); Assert.That(Amount(b), Is.EqualTo(1100));
			Assert.That(first.Load(cartA), Is.Zero);
			Assert.That(first.Unload(cartA), Is.EqualTo(60)); Assert.That(first.Unload(cartA), Is.Zero);
			while (!first.Complete) { first.Load(cartA); first.Unload(cartA); }
			while (!second.Complete) { second.Load(cartB); second.Unload(cartB); }
			Assert.That(Amount(a), Is.EqualTo(1012)); Assert.That(Amount(b), Is.EqualTo(1188));
			Assert.That(Amount(cartA) + Amount(cartB), Is.Zero);
			Assert.That(Enumerable.Range(0, 11).All(g => a[g].Incoming(StorageGoods.Index(g)) + a[g].Reserved(StorageGoods.Index(g))
				+ b[g].Incoming(StorageGoods.Index(g)) + b[g].Reserved(StorageGoods.Index(g)) == 0), Is.True);
		}
		[Test]
		public void TenCartsDeliverSixHundredItemsInOneLoadEachAndCannotUnloadAnotherCartsGoods()
		{
			var a = Stores(800, 2000); var b = Stores(800, 2000);
			var offer = new int[11]; offer[0] = 600;
			var payment = new int[11]; payment[3] = 180;
			var first = new BarterCargo(a, b, offer); var second = new BarterCargo(b, a, payment);
			Assert.That(BarterCargo.ReserveExchange(first, second), Is.True);
			var carts = Enumerable.Range(0, 10).Select(_ => Stores()).ToArray();
			foreach (var cart in carts) Assert.That(first.Load(cart), Is.EqualTo(60));
			foreach (var cart in carts) Assert.That(cart[0].Take(0, 60), Is.Zero, "Cargo belongs to the contract while in transit.");
			Assert.That(first.Waiting, Is.Zero); Assert.That(first.Travelling, Is.EqualTo(600));
			Assert.That(first.Unload(Stores()), Is.Zero);
			foreach (var cart in carts.Reverse())
			{
				// The actor builds fresh wrapper arrays each tick, while its inventories remain the same.
				Assert.That(first.Unload(cart.ToArray()), Is.EqualTo(60)); Assert.That(first.Unload(cart), Is.Zero);
			}
			Assert.That(first.Complete, Is.True); Assert.That(b[0].Amount(0), Is.EqualTo(1400));
			var onlyCart = Stores(); var trips = 0;
			while (!second.Complete) { Assert.That(second.Load(onlyCart), Is.EqualTo(60)); second.Unload(onlyCart); trips++; }
			Assert.That(trips, Is.EqualTo(3)); Assert.That(a[3].Amount(3), Is.EqualTo(980));
		}
		[Test]
		public void CancellingSeveralLoadedCartsReleasesAllBookingsAndRetainsEachPhysicalLoad()
		{
			var a = Stores(100); var b = Stores(100);
			var cargo = new BarterCargo(a, b, Basket(15)); var payment = new BarterCargo(b, a, Basket(2));
			Assert.That(BarterCargo.ReserveExchange(cargo, payment), Is.True);
			var carts = new[] { Stores(), Stores(), Stores() };
			foreach (var cart in carts) cargo.Load(cart);
			cargo.Unload(carts[1]); cargo.Cancel(); payment.Cancel();
			Assert.That(carts.Sum(Amount), Is.EqualTo(105));
			Assert.That(carts.Sum(c => c.Sum(s => s.Reserved(0))), Is.Zero, "Recovery releases the cart's cargo reservations.");
			Assert.That(Enumerable.Range(0, 11).All(g => a[g].Reserved(StorageGoods.Index(g)) == 0 && b[g].Incoming(StorageGoods.Index(g)) == 0), Is.True);
			Assert.That(cargo.Load(Stores()), Is.Zero); Assert.That(cargo.Unload(carts[0]), Is.Zero);
		}
		[Test]
		public void InsufficientPaymentOrSpaceLeavesBothSidesUntouched()
		{
			var a = Stores(100); var b = Stores(3);
			Assert.That(BarterCargo.ReserveExchange(new(a, b, Basket(20)), new(b, a, Basket(4))), Is.False);
			Assert.That(a[0].Reserved(0), Is.Zero); Assert.That(b[0].Incoming(0), Is.Zero);
			b = Stores(199);
			Assert.That(BarterCargo.ReserveExchange(new(a, b, Basket(20)), new(b, a, Basket(4))), Is.False);
			Assert.That(Amount(a), Is.EqualTo(1100)); Assert.That(a[7].Reserved(2), Is.Zero);
		}
		[Test]
		public void AnotherContractCannotSpendReservedPayment()
		{
			var a = Stores(100); var b = Stores(100); var c = Stores(100);
			Assert.That(BarterCargo.ReserveExchange(new(a, b, Basket(80)), new(b, a, Basket(10))), Is.True);
			Assert.That(BarterCargo.ReserveExchange(new(a, c, Basket(21)), new(c, a, Basket(10))), Is.False);
			Assert.That(a[4].Reserved(4), Is.EqualTo(80)); Assert.That(c[4].Reserved(4), Is.Zero);
		}
		[Test]
		public void CancellationReleasesEveryReservationAndPreservesPhysicalCargo()
		{
			var a = Stores(100); var b = Stores(100); var cart = Stores();
			var first = new BarterCargo(a, b, Basket(12)); var second = new BarterCargo(b, a, Basket(8));
			Assert.That(BarterCargo.ReserveExchange(first, second), Is.True);
			first.Load(cart); first.Cancel(); second.Cancel(); first.Cancel();
			Assert.That(Amount(cart), Is.EqualTo(60)); Assert.That(Amount(a) + Amount(cart), Is.EqualTo(1100));
			Assert.That(Amount(b), Is.EqualTo(1100)); Assert.That(first.Unload(cart), Is.Zero);
			Assert.That(Enumerable.Range(0, 11).All(g => a[g].Incoming(StorageGoods.Index(g)) + a[g].Reserved(StorageGoods.Index(g))
				+ b[g].Incoming(StorageGoods.Index(g)) + b[g].Reserved(StorageGoods.Index(g)) == 0), Is.True);
		}
		[TestCase("")]
		[TestCase("-1,0")]
		[TestCase("2147483648,0")]
		[TestCase("1e3,0")]
		public void MalformedNetworkOffersAreRejected(string text) => Assert.That(BarterOffer.TryParse(text, out _, out _), Is.False);
		[Test]
		public void EmptyExcessiveAndNegativeBasketsAreRejected()
		{
			Assert.That(BarterOffer.TryParse(BarterOffer.Encode(Basket(0), Basket(1)), out _, out _), Is.False);
			Assert.That(BarterOffer.TryParse(BarterOffer.Encode(Basket(1000), Basket(1)), out _, out _), Is.False);
			var a = Basket(1); a[7] = -1;
			Assert.That(BarterOffer.TryParse(BarterOffer.Encode(a, Basket(1)), out _, out _), Is.False);
			Assert.That(BarterOffer.TryParse(BarterOffer.Encode(Basket(4), Basket(8)), out var first, out var second), Is.True);
			Assert.That(first, Is.EqualTo(Basket(4))); Assert.That(second, Is.EqualTo(Basket(8)));
		}
	}
}
