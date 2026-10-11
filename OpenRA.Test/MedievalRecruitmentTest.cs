using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalRecruitmentTest
	{
		[Test]
		public void EliteCostCannotBeConfiguredWithoutMeat()
		{
			Assert.Throws<System.ArgumentException>(() => new RecruitmentCost(35, 0, elite: true));
		}
		[TestCase(30, 10)]
		[TestCase(35, 15)]
		[TestCase(40, 20)]
		public void EliteRecruitmentWaitsForEveryUnitOfMeat(int food, int meat)
		{
			var stock = new Inventory([0, 100, 100, 0], [0, food, meat - 1, 0]);
			var cost = new RecruitmentCost(food, meat, elite: true);
			var before = stock.Hash;
			Assert.That(cost.TryPay([stock]), Is.False);
			Assert.That(stock.Hash, Is.EqualTo(before));
			stock.Add(2, 1);
			Assert.That(cost.TryPay([stock]));
			Assert.That(stock.Amount(1) + stock.Amount(2), Is.Zero);
		}
		[TestCase(10)]
		[TestCase(20)]
		public void VillagersAndBasicTroopsNeedNoMeatOrBuildingMaterials(int food)
		{
			var stock = new Inventory([0, food, 0, 0], [0, food, 0, 0]);
			Assert.That(new RecruitmentCost(food).TryPay([stock]));
			Assert.That(stock.Amount(1), Is.Zero);
		}
		[Test]
		public void AdvancedTroopsPayFoodAndMeatWithoutTouchingBuildingMaterials()
		{
			var stock = new Inventory([100, 100, 50, 100], [60, 70, 30, 40]);
			Assert.That(new RecruitmentCost(30, 10).TryPay([stock]));
			Assert.That(new[] { stock.Amount(0), stock.Amount(1), stock.Amount(2), stock.Amount(3) }, Is.EqualTo(new[] { 60, 40, 20, 40 }));
		}
		[TestCase(100, 9)]
		[TestCase(29, 100)]
		public void AFailedRecruitmentDoesNotDeductEitherResource(int food, int meat)
		{
			var stock = new Inventory([0, 100, 100, 0], [0, food, meat, 0]);
			var before = stock.Hash;
			Assert.That(new RecruitmentCost(30, 10).TryPay([stock]), Is.False);
			Assert.That(stock.Hash, Is.EqualTo(before));
		}
		[Test]
		public void PaymentCanCombineSeveralWarehousesButNotCountOneTwice()
		{
			var first = new Inventory([0, 20, 20, 0], [0, 20, 0, 0]);
			var second = new Inventory([0, 20, 20, 0], [0, 10, 10, 0]);
			var price = new RecruitmentCost(30, 10);
			Assert.That(price.TryPay([second, second]), Is.False);
			Assert.That(price.TryPay([first, second]));
			Assert.That(first.Amount(1) + second.Amount(1) + second.Amount(2), Is.Zero);
		}
		[Test]
		public void ReservedAndIncomingGoodsCannotPayForRecruitment()
		{
			var stock = new Inventory([0, 100, 100, 0], [0, 30, 10, 0]);
			stock.ReserveOut(2, 1);
			stock.ReserveIn(2, 20);
			var before = stock.Hash;
			Assert.That(new RecruitmentCost(30, 10).TryPay([stock]), Is.False);
			Assert.That(stock.Hash, Is.EqualTo(before));
			stock.ReleaseOut(2, 1);
			Assert.That(new RecruitmentCost(30, 10).TryPay([stock]));
			Assert.That(stock.Incoming(2), Is.EqualTo(20));
		}
	}
}
