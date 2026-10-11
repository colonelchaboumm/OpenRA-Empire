using NUnit.Framework;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class PathSearchLayerTest
	{
		[TestCase(MapGridType.Rectangular)]
		[TestCase(MapGridType.RectangularIsometric)]
		public void ReusedLayerMatchesFreshStateAfterSparseAndDenseSearches(MapGridType grid)
		{
			var layer = new PathSearchLayer(grid, new Size(32, 64));
			foreach (var count in new[] { 0, 1, 5, 800, 3, 2048, 0, 17 })
			{
				for (var i = 0; i < count; i++)
				{
					var cell = new MPos(i % 32, i / 32).ToCPos(grid);
					layer[cell] = new CellInfo(CellStatus.Open, i, i + 5, cell);
					layer[cell] = new CellInfo(CellStatus.Closed, i + 1, i + 6, cell);
				}
				for (var i = 0; i < count; i++)
					Assert.That(layer[new MPos(i % 32, i / 32).ToCPos(grid)].CostSoFar, Is.EqualTo(i + 1));
				layer.Clear();
				for (var i = 0; i < 2048; i++)
					Assert.That(layer[new MPos(i % 32, i / 32).ToCPos(grid)].Status, Is.EqualTo(CellStatus.Unvisited));
			}
		}

		[Test]
		public void OverlappingSearchLayersRetainIndependentFrontiers()
		{
			var forward = new PathSearchLayer(MapGridType.Rectangular, new Size(128, 128));
			var reverse = new PathSearchLayer(MapGridType.Rectangular, new Size(128, 128));
			var cell = new CPos(50, 50);
			forward[cell] = new CellInfo(CellStatus.Open, 5, 10, cell);
			reverse[cell] = new CellInfo(CellStatus.Closed, 7, 12, cell);
			forward.Clear();
			Assert.That(forward[cell].Status, Is.EqualTo(CellStatus.Unvisited));
			Assert.That(reverse[cell].CostSoFar, Is.EqualTo(7));
		}
	}
}
