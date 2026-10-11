using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Pathfinder;

namespace OpenRA.Test
{
	[TestFixture]
	public class BoundedPathSearchTest
	{
		static PathSearch Corridor(int length, int target) => PathSearch.ToTargetCellOverGraph(
			cell => cell.X < length ? [new GraphConnection(new CPos(cell.X + 1, 0), 1)] : [],
			new CPos(0, 0), new CPos(target, 0), (_, _) => 0);

		[Test]
		public void BudgetExhaustionDoesNotReportUnreachableAndSearchCanResume()
		{
			using var search = Corridor(100, 90);
			Assert.That(search.FindPath(64, out var complete), Is.Empty);
			Assert.That(complete, Is.False);
			var path = search.FindPath(64, out complete);
			Assert.That(complete, Is.True);
			Assert.That(path.Count, Is.EqualTo(91));
			Assert.That(path[0], Is.EqualTo(new CPos(90, 0)));
			Assert.That(path[^1], Is.EqualTo(new CPos(0, 0)));
		}

		[Test]
		public void ExhaustedEnclosureIsConclusiveEvenOnLastBudgetedNode()
		{
			using var search = Corridor(3, 90);
			Assert.That(search.FindPath(4, out var complete), Is.Empty);
			Assert.That(complete, Is.True);
		}

		[Test]
		public void TargetOnLastBudgetedNodeIsReturned()
		{
			using var search = Corridor(100, 63);
			Assert.That(search.FindPath(64, out var complete).Count, Is.EqualTo(64));
			Assert.That(complete, Is.True);
		}

		[Test]
		public void ZeroBudgetDoesNotConsumeSearchAndNegativeBudgetIsRejected()
		{
			using var search = Corridor(10, 3);
			Assert.That(search.FindPath(0, out var complete), Is.Empty);
			Assert.That(complete, Is.False);
			Assert.Throws<ArgumentOutOfRangeException>(() => search.FindPath(-1, out _));
			Assert.That(search.FindPath().Count, Is.EqualTo(4));
		}

		[Test]
		public void PredicateSearchChoosesCheapestReachableDestination()
		{
			var start = new CPos(0, 0);
			var expensive = new CPos(1, 0);
			var middle = new CPos(0, 1);
			var cheap = new CPos(1, 1);
			List<GraphConnection> Edges(CPos cell) => cell == start
				? [new(expensive, 20), new(middle, 2)] : cell == middle ? [new(cheap, 2)] : [];
			using var search = PathSearch.ToTargetCellOverGraph(Edges, start, expensive, (_, _) => 0);
			search.TargetPredicate = cell => cell == expensive || cell == cheap;
			Assert.That(search.FindPath(64, out var complete), Is.EqualTo(new[] { cheap, middle, start }));
			Assert.That(complete, Is.True);
		}
	}
}
