using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalCargoRecoveryTest
	{
		static Inventory[] Stocks(int capacity, int amount)
		{
			var basic = new Inventory(Enumerable.Repeat(capacity, 5).ToArray(), Enumerable.Repeat(amount, 5).ToArray());
			var equipment = new Inventory([capacity, capacity, capacity, 0, 0], [amount, amount, amount, 0, 0]);
			var camp = new Inventory([capacity, capacity, capacity, 0, 0], [amount, amount, amount, 0, 0]);
			return Enumerable.Range(0, 11).Select(g => g < 5 ? basic : g < 8 ? equipment : camp).ToArray();
		}
		[Test]
		public void CompetingCartsSharePhysicalGoodsWithoutDuplication()
		{
			var pile = Stocks(95, 95); var first = Stocks(60, 45); var second = Stocks(60, 0);
			Assert.That(CargoRecovery.Transfer(pile, first), Is.EqualTo(165));
			Assert.That(CargoRecovery.Transfer(pile, second), Is.EqualTo(660));
			Assert.That(CargoRecovery.Transfer(pile, second), Is.Zero);
			for (var g = 0; g < 11; g++)
			{
				var r = StorageGoods.Index(g);
				Assert.That(first[g].Amount(r), Is.EqualTo(60));
				Assert.That(second[g].Amount(r), Is.EqualTo(60));
				Assert.That(pile[g].Amount(r), Is.EqualTo(20));
			}
		}
		[Test]
		public void RecoveryHonorsExistingReservationsAndUnsupportedGoods()
		{
			var pile = Stocks(30, 30); var cart = Stocks(60, 0);
			pile[0].ReserveOut(0, 25); cart[1].ReserveIn(1, 55); cart[5] = cart[6] = cart[7] = null;
			Assert.That(CargoRecovery.Transfer(pile, cart), Is.EqualTo(190));
			Assert.That(pile[0].Amount(0), Is.EqualTo(25)); Assert.That(pile[0].Reserved(0), Is.EqualTo(25));
			Assert.That(cart[1].Amount(1), Is.EqualTo(5)); Assert.That(cart[1].Incoming(1), Is.EqualTo(55));
			Assert.That(pile[7].Amount(2), Is.EqualTo(30));
		}
	}
}
