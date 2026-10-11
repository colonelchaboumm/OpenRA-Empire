using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalFoodTest
	{
		static int Feed(IEnumerable<FoodNeed> people, IEnumerable<Inventory> stores) => FoodSupply.Feed(
			people.Select(n => (n, new WPos(0, 0, 0))), stores.Select(s => new FoodStation(s, new WPos(0, 0, 0), new WDist(1024))));

		static void Advance(FoodNeed person, int ticks)
			{ for (var i = 0; i < ticks; i++) person.Advance(); }

		[Test]
		public void UnpackingSettlersPreservesStarvationAndMealDebt()
		{
			var caravan = new FoodNeed(250);
			Advance(caravan, 3200);
			var crew = Enumerable.Range(0, 3).Select(_ => new FoodNeed()).ToArray();
			foreach (var person in crew) person.Inherit(caravan);
			Assert.That(crew.All(p => p.Starving && p.MealDue && p.TicksWithoutSupply == 3200));
			var stock = new Inventory([0, 10, 0, 0], [0, 3, 0, 0]);
			Assert.That(Feed(crew, [stock]), Is.EqualTo(3));
			Assert.That(stock.Amount(1), Is.Zero);
			Assert.That(crew.All(p => !p.Hungry && p.InSupply));
		}

		[Test]
		public void TenPeopleConsumeTwentyFoodPerMinuteInAdditionToRecruitment()
		{
			var people = Enumerable.Range(0, 10).Select(_ => new FoodNeed()).ToArray();
			var stock = new Inventory([0, 100, 80, 0], [0, 100, 80, 0]);
			Assert.That(new RecruitmentCost(10).TryPay([stock]));
			var consumed = 0;
			for (var tick = 1; tick <= FoodNeed.TicksPerMinute; tick++)
			{
				foreach (var person in people) person.Advance();
				consumed += Feed(people, [stock]);
			}
			Assert.That(consumed, Is.EqualTo(20));
			Assert.That(stock.Amount(1), Is.EqualTo(70));
			Assert.That(stock.Amount(2), Is.EqualTo(80));
			Assert.That(people.All(p => !p.Hungry));
		}

		[Test]
		public void RecruitmentAndPopulationLossChangeDemandWithoutResettingExistingMealClocks()
		{
			var original = new FoodNeed();
			Advance(original, 700);
			var recruit = new FoodNeed();
			Assert.That(new[] { original, recruit }.Sum(n => n.DemandPerMinute), Is.EqualTo(4));
			Assert.That(original.TicksSinceMeal, Is.EqualTo(700));
			var stock = new Inventory([0, 10, 0, 0], [0, 10, 0, 0]);
			Advance(original, 50); Advance(recruit, 50);
			Assert.That(Feed([original, recruit], [stock]), Is.EqualTo(1));
			Advance(recruit, 750);
			Assert.That(Feed([recruit], [stock]), Is.EqualTo(1));
			Assert.That(stock.Amount(1), Is.EqualTo(8));
		}

		[Test]
		public void AShortageNeverEatsMeatReservedFoodOrIncomingFood()
		{
			var person = new FoodNeed(); Advance(person, 750);
			var stock = new Inventory([10, 20, 50, 10], [10, 1, 50, 10]);
			stock.ReserveOut(1, 1); stock.ReserveIn(1, 10);
			var before = stock.Hash;
			Assert.That(Feed([person], [stock]), Is.Zero);
			Assert.That(person.Hungry);
			Assert.That(stock.Hash, Is.EqualTo(before));
			stock.ReleaseOut(1, 1);
			Assert.That(Feed([person], [stock]), Is.EqualTo(1));
			Assert.That(person.Hungry, Is.False);
		}

		[Test]
		public void ScarceMealsGoToTheLongestWaitingPersonWithoutDebtOrDuplicateMeals()
		{
			var recent = new FoodNeed(); Advance(recent, 750);
			var oldest = new FoodNeed(); Advance(oldest, 4000);
			var stock = new Inventory([0, 10, 0, 0], [0, 1, 0, 0]);
			Assert.That(Feed([recent, oldest, oldest], [stock]), Is.EqualTo(1));
			Assert.That(oldest.TicksSinceMeal, Is.Zero);
			Assert.That(recent.Hungry);
			stock.Add(1, 1);
			Assert.That(Feed([recent, oldest], [stock]), Is.EqualTo(1));
			Assert.That(recent.Hungry, Is.False);
		}

		[Test]
		public void HungerPrecedesDamageAndOneMealRemovesAllPenalties()
		{
			var citizen = new MedievalNeedsFood(new MedievalNeedsFoodInfo());
			var need = citizen.Need;
			Advance(need, 749);
			Assert.That(need.Hungry, Is.False);
			need.Advance();
			Assert.That(citizen.WorkPercent, Is.EqualTo(50));
			Assert.That(((ISpeedModifier)citizen).GetSpeedModifier(), Is.EqualTo(75));
			Assert.That(((IFirepowerModifier)citizen).GetFirepowerModifier(), Is.EqualTo(75));
			Advance(need, 2249);
			Assert.That(need.DamageDue, Is.False);
			need.Advance();
			Assert.That(need.DamageDue);
			need.Advance(); Assert.That(need.DamageDue, Is.False);
			Advance(need, 124); Assert.That(need.DamageDue);
			need.Feed();
			Assert.That(need.Starving, Is.False);
			Assert.That(citizen.WorkPercent, Is.EqualTo(100));
			Assert.That(((ISpeedModifier)citizen).GetSpeedModifier(), Is.EqualTo(100));
			Assert.That(((IFirepowerModifier)citizen).GetFirepowerModifier(), Is.EqualTo(100));
		}

		[Test]
		public void AShortageCannotCreateNegativeFoodOrChargeForUnfedPeople()
		{
			var people = Enumerable.Range(0, 8).Select(_ => new FoodNeed()).ToArray();
			foreach (var person in people) Advance(person, 750);
			var first = new Inventory([0, 10, 0, 0], [0, 2, 0, 0]);
			var second = new Inventory([0, 10, 0, 0], [0, 1, 0, 0]);
			Assert.That(Feed(people, [first, second]), Is.EqualTo(3));
			Assert.That(people.Count(p => p.Hungry), Is.EqualTo(5));
			Assert.That(first.Amount(1) + second.Amount(1), Is.Zero);
		}

		[Test]
		public void ObservedDeliveryRateExpiresAfterOneMinuteWithoutTransport()
		{
			var deliveries = new FoodFlowMeter();
			for (var t = 0; t < FoodNeed.TicksPerMinute; t++) deliveries.Advance();
			deliveries.Record(20);
			Assert.That(deliveries.PerMinute, Is.EqualTo(20));
			for (var t = 0; t < FoodNeed.TicksPerMinute - 1; t++) deliveries.Advance();
			Assert.That(deliveries.Total, Is.EqualTo(20));
			deliveries.Advance();
			Assert.That(deliveries.PerMinute, Is.Zero);
		}

		[Test]
		public void OnlyCitizensInsideTheActualRadiusCanEatIncludingItsBoundary()
		{
			var distant = new FoodNeed(); Advance(distant, 1000);
			var nearby = new FoodNeed(); Advance(nearby, 750);
			var food = new Inventory([0, 10, 0, 0], [0, 10, 0, 0]);
			var station = new FoodStation(food, new WPos(0, 0, 0), new WDist(4096));
			Assert.That(FoodSupply.Feed([(distant, new WPos(4097, 0, 0)), (nearby, new WPos(4096, 0, 0))], [station]), Is.EqualTo(1));
			Assert.That(distant.Hungry);
			Assert.That(nearby.Hungry, Is.False);
			Assert.That(food.Amount(1), Is.EqualTo(9));
		}

		[Test]
		public void LeavingCoverageGrantsThirtySecondsWithoutResettingTheMealBill()
		{
			var person = new FoodNeed();
			var stock = new Inventory([0, 10, 0, 0], [0, 10, 0, 0]);
			for (var tick = 0; tick < 749; tick++) { person.Advance(); Feed([person], [stock]); }
			Assert.That(stock.Amount(1), Is.EqualTo(10));
			Advance(person, 749);
			Assert.That(person.Hungry, Is.False);
			person.Advance(); Assert.That(person.Hungry);
			Assert.That(Feed([person], [stock]), Is.EqualTo(1));
			Assert.That(person.Hungry, Is.False);
			Assert.That(stock.Amount(1), Is.EqualTo(9));
		}

		[Test]
		public void EmptyOrReservedStationsCannotRefreshTheGracePeriod()
		{
			var person = new FoodNeed(); Advance(person, 749);
			var stock = new Inventory([0, 10, 0, 0], [0, 1, 0, 0]); stock.ReserveOut(1, 1);
			person.Advance();
			Assert.That(Feed([person], [stock]), Is.Zero);
			Assert.That(person.Hungry);
			Assert.That(person.InSupply, Is.False);
		}

		[Test]
		public void MovingACartChangesItsCoverageAndMealsUseItsCarriedStock()
		{
			var person = new FoodNeed(); Advance(person, 750);
			var stock = new Inventory([0, 60, 0, 0], [0, 2, 0, 0]);
			var target = new WPos(20000, 0, 0);
			Assert.That(FoodSupply.Feed([(person, target)], [new FoodStation(stock, new WPos(0, 0, 0), new WDist(4096), 1)]), Is.Zero);
			Assert.That(FoodSupply.Feed([(person, target)], [new FoodStation(stock, target, new WDist(4096), 1)]), Is.EqualTo(1));
			Assert.That(stock.Amount(1), Is.EqualTo(1));
			Assert.That(person.InSupply);
		}

		[Test]
		public void OverlappingZonesDoNotChargeTwiceAndMarketsPreserveCartProvisions()
		{
			var person = new FoodNeed(); Advance(person, 750);
			var cart = new Inventory([0, 60, 0, 0], [0, 20, 0, 0]);
			var market = new Inventory([0, 200, 0, 0], [0, 20, 0, 0]);
			Assert.That(FoodSupply.Feed([(person, new WPos(0, 0, 0))], [new FoodStation(cart, new WPos(0, 0, 0), new WDist(4096), 1),
				new FoodStation(market, new WPos(1024, 0, 0), new WDist(16384))]), Is.EqualTo(1));
			Assert.That(cart.Amount(1), Is.EqualTo(20));
			Assert.That(market.Amount(1), Is.EqualTo(19));
		}

		[Test]
		public void MealsUseNearestEqualPriorityThenStableOrderAndSkipExhaustedStores()
		{
			var people = Enumerable.Range(0, 3).Select(_ => new FoodNeed()).ToArray();
			foreach (var need in people) Advance(need, 750);
			var far = new Inventory([0, 5, 0, 0], [0, 5, 0, 0]);
			var first = new Inventory([0, 2, 0, 0], [0, 2, 0, 0]);
			var second = new Inventory([0, 5, 0, 0], [0, 5, 0, 0]);
			var sources = new[] { new FoodStation(far, new WPos(2000, 0, 0), new WDist(4000)),
				new FoodStation(first, new WPos(1000, 0, 0), new WDist(4000)), new FoodStation(second, new WPos(-1000, 0, 0), new WDist(4000)) };
			Assert.That(FoodSupply.Feed(people.Select(p => (p, new WPos(0, 0, 0))), sources), Is.EqualTo(3));
			Assert.That(first.Amount(1), Is.Zero); Assert.That(second.Amount(1), Is.EqualTo(4)); Assert.That(far.Amount(1), Is.EqualTo(5));
			Assert.That(people.All(p => p.InSupply && !p.Hungry));
			Assert.That(FoodSupply.Feed(people.Select(p => (p, new WPos(0, 0, 0))), sources), Is.Zero);
			Assert.That(second.Amount(1), Is.EqualTo(4)); Assert.That(far.Amount(1), Is.EqualTo(5));
		}
	}
}
