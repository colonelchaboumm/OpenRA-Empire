using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalGoldDepositsInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalGoldDeposits();
	}
	// Derive distributed deposits from the map's existing finite stone clusters.
	// Actor order and candidate cells are deterministic on every network client.
	public sealed class MedievalGoldDeposits : IWorldLoaded
	{
		void IWorldLoaded.WorldLoaded(World world, WorldRenderer renderer)
		{
			if (world.Type != WorldType.Regular) return;
			world.AddFrameEndTask(w =>
			{
				if (w.ActorsHavingTrait<MedievalSource>().Any(a => a.Trait<MedievalSource>().Info.Resource == Material.Gold)) return;
				var neutral = w.Players.First(p => p.InternalName == "Neutral");
				var rocks = w.ActorsHavingTrait<MedievalSource>().Where(a => a.IsInWorld && a.Trait<MedievalSource>().Info.Resource == Material.Stone).OrderBy(a => a.ActorID).ToArray();
				var actor = w.Map.Rules.Actors["md.gold"]; var building = actor.TraitInfo<BuildingInfo>();
				for (var i = 0; i < rocks.Length; i += 3)
				{
					var cell = w.Map.FindTilesInAnnulus(rocks[i].Location, 2, 5).OrderBy(c => (c - rocks[i].Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X)
						.Where(c => w.Map.Contains(c) && w.CanPlaceBuilding(c, actor, building, null)).Select(c => (CPos?)c).FirstOrDefault();
					if (cell != null) w.CreateActor("md.gold", [new OwnerInit(neutral), new LocationInit(cell.Value)]);
				}
			});
		}
	}
	public class MedievalGoldYardInfo : TraitInfo, Requires<MedievalStockyardInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalGoldYard();
	}
	// Legacy rule name retained; shared storage-zone rendering now owns all gold piles.
	public sealed class MedievalGoldYard { }
}
