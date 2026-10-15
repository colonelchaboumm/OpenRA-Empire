using System;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class CargoRecovery
	{
		// Called only at the pile, in simulation order. Distant carts cannot claim the loot.
		public static int Transfer(Inventory[] source, Inventory[] destination)
		{
			var total = 0;
			for (var g = 0; g < StorageGoods.Count; g++)
			{
				var r = StorageGoods.Index(g); var n = Math.Min(source[g]?.Available(r) ?? 0, destination[g]?.Free(r) ?? 0);
				if (n > 0) { destination[g].Add(r, source[g].Take(r, n)); total += n; }
			}
			return total;
		}
	}

	public class MedievalDroppedCargoInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDroppedCargo();
	}
	public sealed class MedievalDroppedCargo
	{
		public static bool Visible(Actor a, Player player) => MedievalStockpile.Active(a) && !a.WillDispose
			&& a.TraitOrDefault<MedievalDroppedCargo>() != null && a.CanBeViewedByPlayer(player);
	}

	public class MedievalCartCargoInfo : TraitInfo, Requires<MedievalStockpileInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalCartCargo();
	}
	public sealed class MedievalCartCargo : INotifyKilled, ISync
	{
		[VerifySync] bool dropped;
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (dropped) return; dropped = true;
			// Physical goods only: an incoming reservation still belongs to its source.
			// The dead actor is removed at frame end; its consumers cancel their reservations.
			var amounts = Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Amount(self, g)).ToArray();
			if (!amounts.Any(n => n > 0)) return;
			var cell = self.Location;
			self.World.AddFrameEndTask(w =>
			{
				var pile = w.CreateActor("md.droppedcargo", [new OwnerInit(w.WorldActor.Owner), new LocationInit(cell)]);
				foreach (var first in new[] { 0, 5, 8 })
				{
					var contents = new int[Inventory.ResourceCount]; var count = first == 0 ? 5 : 3;
					Array.Copy(amounts, first, contents, 0, count); var inventory = StorageGoods.Stock(pile, first);
					inventory.SetCapacity(contents);
					for (var r = 0; r < count; r++) inventory.Add(r, contents[r]);
				}
			});
		}
	}
}
