using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalFoodIndexTest
	{
		// Independent exhaustive allocator: no spatial buckets or early exits.
		static int Reference(IEnumerable<(FoodNeed Need, WPos Position)> people, IEnumerable<FoodStation> stations)
		{
			var sources = stations.DistinctBy(s => s.Stock).ToArray();
			var consumed = 0;
			foreach (var person in people.DistinctBy(p => p.Need).OrderByDescending(p => p.Need.TicksWithoutSupply))
			{
				var covering = sources.Where(s => s.Covers(person.Position) && s.Stock.Available(1) > 0)
					.OrderBy(s => s.Priority).ThenBy(s => (s.Position - person.Position).HorizontalLengthSquared).ToArray();
				if (covering.Length == 0) continue;
				if (!person.Need.MealDue) { person.Need.Sustain(); continue; }
				covering[0].Stock.Take(1, 1); person.Need.Feed(); consumed++;
			}
			return consumed;
		}

		[TestCase(4, false)]
		[TestCase(32, false)]
		[TestCase(32, true)]
		public void IndexedMealsMatchExhaustiveAllocationWithMovingCartsAndScarceReservedFood(int providers, bool largeRadius)
		{
			var random = new Random(20261010);
			var actual = Enumerable.Range(0, 128).Select(i => new FoodNeed(3 + i % 18, 40, 5)).ToArray();
			var expected = Enumerable.Range(0, 128).Select(i => new FoodNeed(3 + i % 18, 40, 5)).ToArray();
			var actualStocks = Enumerable.Range(0, providers).Select(_ => new Inventory([0, 200, 0, 0], [0, 4, 0, 0])).ToArray();
			var expectedStocks = Enumerable.Range(0, providers).Select(_ => new Inventory([0, 200, 0, 0], [0, 4, 0, 0])).ToArray();
			for (var tick = 0; tick < 80; tick++)
			{
				foreach (var n in actual.Concat(expected)) n.Advance();
				for (var i = 0; i < providers; i++)
				{
					if (tick % 9 == 0) { actualStocks[i].Add(1, 3); expectedStocks[i].Add(1, 3); }
					if (tick % 13 == 0) { actualStocks[i].ReserveOut(1, 1); expectedStocks[i].ReserveOut(1, 1); }
				}
				// Negative coordinates, sector boundaries, mobile providers and changed
				// priorities. Duplicate inventory references must retain their first entry.
				var positions = Enumerable.Range(0, providers).Select(_ => new WPos(random.Next(-5, 6) * 16384, random.Next(-5, 6) * 16384, 0)).ToArray();
				var radii = Enumerable.Range(0, providers).Select(i => new WDist(largeRadius && i == 0 ? 100000 : random.Next(0, 32769))).ToArray();
				var sources = actualStocks.Select((s, i) => new FoodStation(s, positions[i], radii[i], i % 3)).ToArray();
				var reference = expectedStocks.Select((s, i) => new FoodStation(s, positions[i], radii[i], i % 3)).ToArray();
				var personPositions = Enumerable.Range(0, actual.Length).Select(i => positions[i % providers] + new WVec(random.Next(-16384, 16385), random.Next(-16384, 16385), i % 2 * 1024)).ToArray();
				var people = actual.Select((n, i) => (n, personPositions[i])).Append((actual[0], WPos.Zero));
				var referencePeople = expected.Select((n, i) => (n, personPositions[i])).Append((expected[0], WPos.Zero));
				var got = FoodSupply.Feed(people, sources.Append(sources[0] with { Position = WPos.Zero, Priority = -1 }));
				var wanted = Reference(referencePeople, reference.Append(reference[0] with { Position = WPos.Zero, Priority = -1 }));
				Assert.That(got, Is.EqualTo(wanted), $"Consumed at tick {tick}");
				Assert.That(actualStocks.Select(s => s.Hash), Is.EqualTo(expectedStocks.Select(s => s.Hash)), $"Inventory at tick {tick}");
				Assert.That(actual.Select(n => (n.TicksSinceMeal, n.TicksWithoutSupply, n.InSupply)),
					Is.EqualTo(expected.Select(n => (n.TicksSinceMeal, n.TicksWithoutSupply, n.InSupply))), $"People at tick {tick}");
			}
		}

		[Test]
		public void SectorBoundaryMealsKeepStableTiesAndRecheckExhaustion()
		{
			var stocks = Enumerable.Range(0, 8).Select(_ => new Inventory([0, 10, 0, 0], [0, 1, 0, 0])).ToArray();
			var sources = stocks.Select((s, i) => new FoodStation(s, new WPos(i == 0 ? -16384 : i == 1 ? 16384 : i * 65536, 0, 0), new WDist(16384))).ToArray();
			var people = Enumerable.Range(0, 3).Select(_ => new FoodNeed(1, 3, 1)).ToArray();
			foreach (var p in people) p.Advance();
			Assert.That(FoodSupply.Feed(people.Select(p => (p, WPos.Zero)), sources), Is.EqualTo(2));
			Assert.That(stocks.Take(2).Select(s => s.Amount(1)), Is.EqualTo(new[] { 0, 0 }));
			Assert.That(people.Select(p => p.InSupply), Is.EqualTo(new[] { true, true, false }));
		}
	}
}
