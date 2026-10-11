using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalTradeTest
	{
		static Inventory Store(int[] initial = null) => new([120, 200, 120, 120], initial);
		static Inventory Cart() => new([60, 60, 60, 60]);
		[Test]
		public void TradeMovesAllFourRealStocksOnlyAfterArrival()
		{
			var source = Store([80, 140, 35, 90]); var target = Store(); var cargo = Cart();
			var trip = new TradeShipment(cargo, target); trip.Load(source, 15, 40);
			Assert.That(Enumerable.Range(0, 4).Select(source.Amount), Is.EqualTo(new[] { 20, 80, 0, 30 }));
			Assert.That(Enumerable.Range(0, 4).Select(target.Amount), Is.EqualTo(new int[4]));
			Assert.That(Enumerable.Range(0, 4).Select(target.Incoming), Is.EqualTo(new[] { 60, 60, 35, 60 }));
			Assert.That(trip.Unload(), Is.EqualTo(215));
			Assert.That(Enumerable.Range(0, 4).Select(target.Amount), Is.EqualTo(new[] { 60, 60, 35, 60 }));
			Assert.That(Enumerable.Range(0, 4).Sum(cargo.Amount), Is.Zero);
			Assert.That(trip.Unload(), Is.Zero);
		}
		[Test]
		public void ExportsRespectSelectionAndKeepFortyFoodForResidents()
		{
			var source = Store([80, 55, 35, 90]); var target = Store(); var cargo = Cart();
			new TradeShipment(cargo, target).Load(source, 2, 40);
			Assert.That(source.Amount(1), Is.EqualTo(40));
			Assert.That(cargo.Amount(1), Is.EqualTo(15));
			Assert.That(cargo.Amount(0) + cargo.Amount(2) + cargo.Amount(3), Is.Zero);
		}
		[Test]
		public void CompetingCartsCannotOverbookAMarket()
		{
			var source = Store([120, 0, 0, 0]); var target = Store([30, 0, 0, 0]);
			var a = Cart(); var b = Cart();
			var first = new TradeShipment(a, target); var second = new TradeShipment(b, target);
			first.Load(source, 1, 40); second.Load(source, 1, 40);
			Assert.That(a.Amount(0), Is.EqualTo(60)); Assert.That(b.Amount(0), Is.EqualTo(30));
			Assert.That(target.Free(0), Is.Zero);
			second.Unload(); first.Unload();
			Assert.That(target.Amount(0), Is.EqualTo(120)); Assert.That(target.Incoming(0), Is.Zero);
		}
		[Test]
		public void InterruptionReleasesDestinationButKeepsCargoAndCanResume()
		{
			var source = Store([60, 0, 0, 0]); var target = Store(); var cargo = Cart();
			var trip = new TradeShipment(cargo, target); trip.Load(source, 1, 40);
			trip.Cancel(); trip.Cancel();
			Assert.That(target.Incoming(0), Is.Zero); Assert.That(cargo.Amount(0), Is.EqualTo(60)); Assert.That(source.Amount(0), Is.Zero);
			var resumed = new TradeShipment(cargo, target); resumed.BookCarriedCargo(); resumed.BookCarriedCargo();
			Assert.That(target.Incoming(0), Is.EqualTo(60)); resumed.Unload();
			Assert.That(target.Amount(0), Is.EqualTo(60));
		}
		[Test]
		public void RecoveryOfAPartialLoadWaitsForSpaceWithoutLoss()
		{
			var cargo = Cart(); cargo.Add(3, 60); var target = Store([0, 0, 0, 100]);
			var trip = new TradeShipment(cargo, target); trip.BookCarriedCargo(); trip.Unload();
			Assert.That(cargo.Amount(3), Is.EqualTo(40)); Assert.That(target.Amount(3), Is.EqualTo(120));
			target.Take(3, 50); trip.BookCarriedCargo(); trip.Unload();
			Assert.That(cargo.Amount(3), Is.Zero); Assert.That(target.Amount(3), Is.EqualTo(110));
		}
		[Test]
		public void ConstructionAndHaulerReservationsCannotBeExported()
		{
			var source = Store([80, 60, 0, 0]); source.ReserveOut(0, 70); source.ReserveOut(1, 10);
			var target = Store(); target.ReserveIn(0, 115); var cargo = Cart();
			var trip = new TradeShipment(cargo, target); trip.Load(source, 3, 40);
			Assert.That(cargo.Amount(0), Is.EqualTo(5)); Assert.That(cargo.Amount(1), Is.EqualTo(10));
			Assert.That(source.Reserved(0), Is.EqualTo(70)); Assert.That(source.Reserved(1), Is.EqualTo(10));
			trip.Cancel(); Assert.That(target.Incoming(0), Is.EqualTo(115));
		}
	}
}
