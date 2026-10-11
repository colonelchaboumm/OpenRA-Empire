using System.Collections.Generic;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Pathfinder
{
	// Reusable dense storage with a sparse reset. Short routes on large maps
	// should clear only their visited cells, not millions of unrelated entries.
	public sealed class PathSearchLayer
	{
		readonly CellLayer<CellInfo> cells;
		readonly List<CPos> visited = [];
		readonly int fullClearThreshold;

		public PathSearchLayer(MapGridType gridType, Size size)
		{
			cells = new CellLayer<CellInfo>(gridType, size);
			fullClearThreshold = size.Width * size.Height / 8;
		}

		public CellInfo this[CPos cell]
		{
			get => cells[cell];
			set
			{
				if (cells[cell].Status == CellStatus.Unvisited) visited.Add(cell);
				cells[cell] = value;
			}
		}

		public void Clear()
		{
			// Bulk clearing is still faster after a continent-wide search.
			if (visited.Count > fullClearThreshold) cells.Clear();
			else foreach (var cell in visited) cells[cell] = default;
			visited.Clear();
		}
	}
}
