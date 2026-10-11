#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Pathfinder
{
	// Short probes must not clear a continent-sized pooled layer for a few dozen
	// visited cells. Keep the identical movement graph with sparse search state.
	sealed class SparseMapPathGraph : DensePathGraph
	{
		readonly Dictionary<CPos, CellInfo> infos;

		public SparseMapPathGraph(Locomotor locomotor, Actor actor, World world, BlockedByActor check,
			Func<CPos, int> customCost, Actor ignoreActor, bool laneBias, int estimatedSearchSize)
			: base(locomotor, actor, world, check, customCost, ignoreActor, laneBias, false)
		{
			infos = new Dictionary<CPos, CellInfo>(estimatedSearchSize);
		}

		public override CellInfo this[CPos pos]
		{
			get => infos.TryGetValue(pos, out var info) ? info : default;
			set => infos[pos] = value;
		}
	}

	/// <summary>
	/// A dense pathfinding graph that supports a search over all cells within a map.
	/// It implements the ability to cost and get connections for cells, and supports <see cref="ICustomMovementLayer"/>.
	/// </summary>
	sealed class MapPathGraph : DensePathGraph
	{
		readonly CellInfoLayerPool.PooledCellInfoLayer pooledLayer;
		readonly PathSearchLayer[] cellInfoForLayer;

		public MapPathGraph(CellInfoLayerPool layerPool, Locomotor locomotor, Actor actor, World world, BlockedByActor check,
			Func<CPos, int> customCost, Actor ignoreActor, bool laneBias, bool inReverse)
			: base(locomotor, actor, world, check, customCost, ignoreActor, laneBias, inReverse)
		{
			// As we support a search over the whole map area,
			// use the pool to grab the CellInfos we need to track the graph state.
			// This allows us to avoid the cost of allocating large arrays constantly.
			// PERF: Avoid LINQ
			pooledLayer = layerPool.Get();
			cellInfoForLayer = new PathSearchLayer[CustomMovementLayers.Length];
			cellInfoForLayer[0] = pooledLayer.GetLayer();
			foreach (var cml in CustomMovementLayers)
				if (cml != null && cml.EnabledForLocomotor(locomotor.Info))
					cellInfoForLayer[cml.Index] = pooledLayer.GetLayer();
		}

		public override CellInfo this[CPos pos]
		{
			get => cellInfoForLayer[pos.Layer][pos];
			set => cellInfoForLayer[pos.Layer][pos] = value;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				pooledLayer.Dispose();

			base.Dispose(disposing);
		}
	}
}
