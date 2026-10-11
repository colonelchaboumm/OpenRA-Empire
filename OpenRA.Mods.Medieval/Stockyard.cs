using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct StockyardArea(CPos Start, CVec Size)
	{
		public const int MaxStack = 100;
		public bool ValidSize => ValidSizeFor(4);
		public bool ValidSizeFor(int minimum) => Size.X >= Math.Max(2, minimum) && Size.Y >= Math.Max(2, minimum) && Size.X <= 10 && Size.Y <= 10;
		public static StockyardArea Between(CPos a, CPos b)
		{
			var rectangle = AgriculturalZone.Between(a, b);
			return new StockyardArea(rectangle.Start, rectangle.Size);
		}
		public bool Contains(CPos cell) => cell.X >= Start.X && cell.Y >= Start.Y && cell.X < Start.X + Size.X && cell.Y < Start.Y + Size.Y;
		public IEnumerable<CPos> Cells() => new AgriculturalZone(Start, Size).Cells();
		public int ResourceAt(CPos cell)
		{
			var right = cell.X - Start.X >= Size.X / 2;
			var bottom = cell.Y - Start.Y >= Size.Y / 2;
			return bottom ? right ? 2 : 3 : right ? 0 : 1;
		}
		public IEnumerable<CPos> ResourceCells(int resource)
		{
			foreach (var cell in Cells()) if (ResourceAt(cell) == resource) yield return cell;
		}
		public static int StackAmount(int amount, int cells, int index) => amount / cells + (index < amount % cells ? 1 : 0);
		public IEnumerable<(CPos Cell, int Resource, int Amount)> Stacks(Inventory stock)
		{
			for (var r = 0; r < 4; r++)
			{
				var cells = ResourceCells(r).ToArray();
				for (var i = 0; i < cells.Length; i++) yield return (cells[i], r, StackAmount(stock.Amount(r), cells.Length, i));
			}
		}
	}

}
