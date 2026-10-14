using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenRA.Mods.Medieval
{
	public static class BarterOffer
	{
		public const int Limit = 1000;
		public static bool Valid(int[] goods) => goods is { Length: StorageGoods.Count }
			&& goods.All(n => n >= 0 && n <= Limit) && goods.Sum() is > 0 and <= 2000;
		public static string Encode(int[] first, int[] second) => string.Join(",", first.Concat(second));
		public static bool TryParse(string text, out int[] first, out int[] second)
		{
			first = second = null;
			if (text == null || text.Length > 120) return false;
			var parts = text.Split(','); var values = new int[StorageGoods.Count * 2];
			if (parts.Length != values.Length) return false;
			for (var i = 0; i < values.Length; i++)
				if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]) || values[i] > Limit) return false;
			first = values.Take(StorageGoods.Count).ToArray(); second = values.Skip(StorageGoods.Count).ToArray();
			return Valid(first) && Valid(second);
		}
	}

	// Reservations leave goods physically at their source until a cart collects them.
	// Every destination slot remains booked until arrival, cancellation or loss.
	public sealed class BarterCargo
	{
		readonly Inventory[] sources, destinations;
		readonly int[] waiting, travelling, delivered = new int[StorageGoods.Count];
		readonly Dictionary<Inventory, (Inventory[] Stocks, int[] Quantities)> loads = [];
		bool reserved;
		public int Waiting => waiting.Sum();
		public int Travelling => travelling.Sum();
		public int Delivered => delivered.Sum();
		public bool Complete => reserved && Waiting == 0 && Travelling == 0;
		public BarterCargo(Inventory[] sources, Inventory[] destinations, int[] quantities)
		{
			if (sources.Length != StorageGoods.Count || destinations.Length != StorageGoods.Count || !BarterOffer.Valid(quantities))
				throw new ArgumentException("Invalid barter cargo.");
			this.sources = sources; this.destinations = destinations; waiting = (int[])quantities.Clone(); travelling = new int[StorageGoods.Count];
		}
		public bool CanReserve => !reserved && Waiting > 0 && Enumerable.Range(0, StorageGoods.Count).All(g => waiting[g] == 0
			|| sources[g] != null && destinations[g] != null && sources[g] != destinations[g]
			&& sources[g].Available(StorageGoods.Index(g)) >= waiting[g] && destinations[g].Free(StorageGoods.Index(g)) >= waiting[g]);
		bool Reserve()
		{
			if (!CanReserve) return false;
			for (var g = 0; g < waiting.Length; g++)
			{
				if (waiting[g] == 0) continue;
				var r = StorageGoods.Index(g);
				sources[g].ReserveOut(r, waiting[g]); destinations[g].ReserveIn(r, waiting[g]);
			}
			reserved = true; return true;
		}
		public static bool ReserveExchange(BarterCargo first, BarterCargo second)
		{
			if (!first.CanReserve || !second.CanReserve || !first.Reserve()) return false;
			if (second.Reserve()) return true;
			first.Cancel(); return false;
		}
		public int Load(Inventory[] cart, int capacity = 60)
		{
			if (!reserved || loads.ContainsKey(cart[0])) return 0;
			var shipment = new int[StorageGoods.Count];
			var loaded = 0;
			for (var g = 0; g < waiting.Length && loaded < capacity; g++)
			{
				var r = StorageGoods.Index(g); var n = Math.Min(waiting[g], Math.Min(capacity - loaded, cart[g]?.Free(r) ?? 0));
				if (n <= 0) continue;
				sources[g].Load(r, n); cart[g].Add(r, n); cart[g].ReserveOut(r, n);
				waiting[g] -= n; travelling[g] += n; shipment[g] = n; loaded += n;
			}
			if (loaded > 0) loads.Add(cart[0], (cart, shipment));
			return loaded;
		}
		public int Unload(Inventory[] cart)
		{
			if (!reserved || !loads.TryGetValue(cart[0], out var load)) return 0;
			var shipment = load.Quantities;
			var total = 0;
			for (var g = 0; g < travelling.Length; g++)
			{
				if (shipment[g] == 0) continue;
				var r = StorageGoods.Index(g); var n = shipment[g]; cart[g].Load(r, n);
				destinations[g].Unload(r, n); shipment[g] -= n; travelling[g] -= n; delivered[g] += n; total += n;
			}
			if (shipment.All(n => n == 0)) loads.Remove(cart[0]);
			return total;
		}
		public void Cancel()
		{
			if (!reserved) return;
			for (var g = 0; g < waiting.Length; g++)
			{
				var r = StorageGoods.Index(g);
				if (waiting[g] > 0) sources[g].ReleaseOut(r, waiting[g]);
				if (waiting[g] + travelling[g] > 0) destinations[g].ReleaseIn(r, waiting[g] + travelling[g]);
				waiting[g] = travelling[g] = 0;
			}
			foreach (var load in loads.Values)
				for (var g = 0; g < StorageGoods.Count; g++)
					if (load.Quantities[g] > 0) load.Stocks[g].ReleaseOut(StorageGoods.Index(g), load.Quantities[g]);
			loads.Clear(); reserved = false;
		}
		public int Hash => waiting.Concat(travelling).Concat(delivered).Concat(loads.Values.SelectMany(a => a.Quantities)).Aggregate(reserved ? 1 : 0, (h, n) => unchecked(h * 31 + n));
	}
}
