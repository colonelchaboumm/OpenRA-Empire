using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalStockyardTest
	{
		[Test]
		public void EveryRectanglePartitionsItsCellsAmongFourResources()
		{
			for (var w = 4; w <= 10; w++)
				for (var h = 4; h <= 10; h++)
				{
					var area = StockyardArea.Between(new CPos(11 + w, 21 + h), new CPos(12, 22));
					Assert.That(area.ValidSize, Is.True);
					Assert.That(area.Size, Is.EqualTo(new CVec(w, h)));
					var cells = Enumerable.Range(0, 4).SelectMany(area.ResourceCells).ToArray();
					Assert.That(cells.Distinct().Count(), Is.EqualTo(w * h));
					Assert.That(cells.All(area.Contains), Is.True);
					Assert.That(Enumerable.Range(0, 4).All(r => area.ResourceCells(r).Count() >= 4), Is.True);
				}
		}
		[Test]
		public void AllStocksIncludingRemaindersAreRepresentedExactly()
		{
			var capacity = new[] { 400, 250, 200, 300 };
			for (var w = 4; w <= 10; w++)
				for (var h = 4; h <= 10; h++)
					foreach (var contents in new[] { new int[4], new[] { 1, 2, 3, 4 }, new[] { 183, 97, 79, 121 }, capacity })
					{
						var stock = new Inventory(capacity, contents);
						var area = new StockyardArea(new CPos(12, 22), new CVec(w, h));
						var stacks = area.Stacks(stock).ToArray();
						for (var r = 0; r < 4; r++) Assert.That(stacks.Where(s => s.Resource == r).Sum(s => s.Amount), Is.EqualTo(contents[r]));
						Assert.That(stacks.All(s => s.Amount >= 0 && s.Amount <= StockyardArea.MaxStack), Is.True);
					}
		}
		[Test]
		public void SmallStallYardsRepresentOnlyTheirOwnPhysicalInventory()
		{
			var stock = new Inventory([60, 80, 0, 60], [17, 23, 0, 9]);
			stock.ReserveIn(0, 12); stock.ReserveOut(1, 7);
			var hash = stock.Hash;
			for (var width = 2; width <= 10; width++)
				for (var height = 2; height <= 10; height++)
				{
					var area = new StockyardArea(new CPos(5, 8), new CVec(width, height));
					Assert.That(area.ValidSizeFor(2), Is.True);
					var stacks = area.Stacks(stock).ToArray();
					for (var r = 0; r < 4; r++)
						Assert.That(stacks.Where(s => s.Resource == r).Sum(s => s.Amount), Is.EqualTo(stock.Amount(r)));
				}
			Assert.That(stock.Hash, Is.EqualTo(hash));
			Assert.That(new StockyardArea(new CPos(0, 0), new CVec(1, 2)).ValidSizeFor(2), Is.False);
		}
		[Test]
		public void ReservationsStayVisibleUntilPhysicalLoadingAndDeliveriesMustArrive()
		{
			var stock = new Inventory([400, 250, 200, 300], [180, 100, 80, 100]);
			var area = new StockyardArea(new CPos(0, 0), new CVec(4, 4));
			int Shown(int r) => area.Stacks(stock).Where(s => s.Resource == r).Sum(s => s.Amount);
			stock.ReserveOut(0, 30); stock.ReserveIn(1, 25);
			Assert.That(Shown(0), Is.EqualTo(180));
			Assert.That(Shown(1), Is.EqualTo(100));
			stock.Load(0, 30); stock.Unload(1, 25);
			Assert.That(Shown(0), Is.EqualTo(150));
			Assert.That(Shown(1), Is.EqualTo(125));
			stock.Take(2, 80);
			Assert.That(Shown(2), Is.Zero);
		}
		[Test]
		public void RedrawingOnlyRedistributesTheSameInventory()
		{
			var stock = new Inventory([400, 250, 200, 300], [180, 100, 80, 100]);
			stock.ReserveOut(0, 10);
			var hash = stock.Hash;
			var small = new StockyardArea(new CPos(1, 2), new CVec(4, 4));
			var large = new StockyardArea(new CPos(6, 9), new CVec(10, 7));
			Assert.That(small.Stacks(stock).Sum(s => s.Amount), Is.EqualTo(large.Stacks(stock).Sum(s => s.Amount)));
			Assert.That(stock.Hash, Is.EqualTo(hash));
			Assert.That(new StockyardArea(new CPos(0, 0), new CVec(3, 4)).ValidSize, Is.False);
			Assert.That(new StockyardArea(new CPos(0, 0), new CVec(4, 11)).ValidSize, Is.False);
		}
	}
}
