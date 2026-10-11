using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalSiegeCrewTest
	{
		static Inventory Wagon() => new([0, 0, 0, 60], [0, 0, 0, 20]);
		static Inventory Carrier() => new([0, 0, 0, 5]);
		[Test]
		public void ProjectileTravelsThroughThreePhysicalInventories()
		{
			var wagon = Wagon(); var carrier = Carrier(); var sling = Carrier();
			var trip = SiegeLoadingTrip.Book(wagon, carrier, sling);
			Assert.That(wagon.Amount(3), Is.EqualTo(20)); Assert.That(sling.Amount(3), Is.Zero);
			trip.PickUp(); trip.PickUp();
			Assert.That(wagon.Amount(3), Is.EqualTo(15)); Assert.That(carrier.Amount(3), Is.EqualTo(5)); Assert.That(sling.Amount(3), Is.Zero);
			Assert.That(trip.Deliver(), Is.EqualTo(5)); Assert.That(trip.Deliver(), Is.Zero);
			Assert.That(carrier.Amount(3), Is.Zero); Assert.That(sling.Amount(3), Is.EqualTo(5));
			Assert.That(wagon.Reserved(3) + sling.Incoming(3), Is.Zero);
		}
		[Test]
		public void CompetingSoldiersCannotLoadTwoProjectilesIntoOneSling()
		{
			var wagon = Wagon(); var sling = Carrier(); var first = SiegeLoadingTrip.Book(wagon, Carrier(), sling);
			Assert.That(SiegeLoadingTrip.Book(wagon, Carrier(), sling), Is.Null);
			Assert.That(wagon.Reserved(3), Is.EqualTo(5)); first.Cancel();
			Assert.That(wagon.Available(3), Is.EqualTo(20)); Assert.That(sling.Free(3), Is.EqualTo(5));
		}
		[Test]
		public void InterruptedCarrierKeepsItsStoneAndCanServeAnotherEngine()
		{
			var wagon = Wagon(); var carrier = Carrier(); var sling = Carrier();
			var trip = SiegeLoadingTrip.Book(wagon, carrier, sling); trip.PickUp(); trip.Cancel(); trip.Cancel();
			Assert.That(carrier.Amount(3), Is.EqualTo(5)); Assert.That(sling.Incoming(3), Is.Zero);
			var nextSling = Carrier(); var next = SiegeLoadingTrip.Book(wagon, carrier, nextSling);
			Assert.That(next.AwaitingPickup, Is.False); next.Deliver();
			Assert.That(wagon.Amount(3), Is.EqualTo(15)); Assert.That(nextSling.Amount(3), Is.EqualTo(5));
		}
		[Test]
		public void LostCarrierReleasesTheSlingWithoutReturningItsCargoByMagic()
		{
			var wagon = Wagon(); var carrier = Carrier(); var sling = Carrier();
			var trip = SiegeLoadingTrip.Book(wagon, carrier, sling); trip.PickUp(); trip.Cancel();
			Assert.That(sling.Free(3), Is.EqualTo(5)); Assert.That(wagon.Amount(3), Is.EqualTo(15));
			Assert.That(SiegeLoadingTrip.Book(wagon, Carrier(), sling), Is.Not.Null);
		}
		[Test]
		public void InsufficientStonesLeaveNoPhantomReservation()
		{
			var wagon = new Inventory([0, 0, 0, 60], [0, 0, 0, 4]); var sling = Carrier();
			Assert.That(SiegeLoadingTrip.Book(wagon, Carrier(), sling), Is.Null);
			Assert.That(wagon.Available(3), Is.EqualTo(4)); Assert.That(sling.Incoming(3), Is.Zero);
		}
		[Test]
		public void AdditionalCrewAcceleratesWorkAndTransportWhileHungerStillMatters()
		{
			Assert.That(SiegeRules.CrewWorkPercent(0, 0), Is.Zero); Assert.That(SiegeRules.CrewSpeedPercent(0), Is.Zero);
			for (var n = 1; n <= 4; n++)
			{
				Assert.That(SiegeRules.CrewWorkPercent(n, n * 100), Is.GreaterThan(SiegeRules.CrewWorkPercent(n - 1, (n - 1) * 100)));
				Assert.That(SiegeRules.CrewSpeedPercent(n), Is.GreaterThan(SiegeRules.CrewSpeedPercent(n - 1)));
				Assert.That(SiegeRules.CrewWorkPercent(n, n * 50), Is.LessThan(SiegeRules.CrewWorkPercent(n, n * 100)));
			}
			Assert.That(SiegeRules.CrewWorkPercent(4, 400), Is.EqualTo(220));
		}
	}
}
