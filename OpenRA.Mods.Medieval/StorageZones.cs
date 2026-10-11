using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Medieval
{
	public enum StorageKind { Mixed, Wood, Grain, Meat, Stone, Gold, Equipment, Camp }
	// Commodity indices: five materials, three equipment types, wool and tents.
	public static class StorageGoods
	{
		public const int Count = 10;
		public static readonly string[] KindNames = ["Reserve mixte", "Bois", "Cereales", "Viande", "Pierre", "Or", "Armes et armures", "Laine et tentes"];
		public static readonly string[] Names = ["Bois", "Cereales", "Viande", "Pierre", "Or", "Arcs", "Epees", "Armures", "Laine", "Tentes"];
		public static int Limit(int good) => good < 5 ? 100 : good == 8 ? 40 : good == 9 ? 8 : 16;
		public static bool Flammable(int good) => good is 0 or 1 or 2 or 5 or 8 or 9;
		public static int[] Types(StorageKind kind) => kind switch
		{
			StorageKind.Mixed => [1, 0, 3, 2], StorageKind.Equipment => [5, 6, 7], StorageKind.Camp => [8, 9],
			_ => [(int)kind - 1]
		};
		public static Inventory Stock(Actor self, int good) => good < 5 ? self.TraitOrDefault<MedievalStockpile>()?.Stock
			: good < 8 ? self.TraitOrDefault<MedievalEquipmentStore>()?.Stock : self.TraitOrDefault<MedievalCampStore>()?.Stock;
		public static int Index(int good) => good < 5 ? good : good < 8 ? good - 5 : good - 8;
		public static int Amount(Actor self, int good) => Stock(self, good)?.Amount(Index(good)) ?? 0;
		public static int BaseCapacity(Actor self, int good)
		{
			var values = good < 5 ? self.TraitOrDefault<MedievalStockpile>()?.Info.Capacity
				: good < 8 ? self.TraitOrDefault<MedievalEquipmentStore>()?.Info.Capacity : self.TraitOrDefault<MedievalCampStore>()?.Info.Capacity;
			var index = Index(good); return values != null && index < values.Length ? values[index] : 0;
		}
	}
	public sealed class StorageZone
	{
		public readonly int Id;
		public readonly StockyardArea Area;
		public readonly StorageKind Kind;
		readonly int[] damage = new int[StorageGoods.Count];
		readonly int[] capacities = new int[StorageGoods.Count];
		readonly (CPos Cell, int Good)[] slots;
		public IReadOnlyList<(CPos Cell, int Good)> Slots => slots;
		public bool Damaged => damage.Any(n => n > 0);
		public StorageZone(int id, StockyardArea area, StorageKind kind)
		{
			Id = id; Area = area; Kind = kind;
			var goods = StorageGoods.Types(kind);
			slots = area.Cells().Select((cell, i) => (cell, kind == StorageKind.Mixed ? area.ResourceAt(cell)
				: goods[Math.Min(goods.Length - 1, i * goods.Length / (area.Size.X * area.Size.Y))])).ToArray();
			foreach (var (_, good) in slots) capacities[good] += StorageGoods.Limit(good);
		}
		public int Capacity(int good) => capacities[good] - damage[good];
		public void Scorch(int good, int amount) => damage[good] += Math.Clamp(amount, 0, Capacity(good));
		public int Hash
		{
			get { unchecked { var h = (((Id * 31 + (int)Kind) * 31 + Area.Start.X) * 31 + Area.Start.Y) * 31 + Area.Size.X;
				h = h * 31 + Area.Size.Y; foreach (var n in damage) h = h * 31 + n; return h; } }
		}
		public IEnumerable<(CPos Cell, int Good, int Amount)> Stacks(Func<int, int> amount)
		{
			var used = new int[StorageGoods.Count];
			foreach (var (cell, good) in slots)
			{
				var count = Math.Clamp(amount(good) - used[good], 0, Math.Min(StorageGoods.Limit(good), Math.Max(0, Capacity(good) - used[good])));
				yield return (cell, good, count);
				used[good] += StorageGoods.Limit(good);
			}
		}
	}
	public static class StorageAllocation
	{
		public static int Outside(IReadOnlyList<StorageZone> zones, int id, int good, int total)
		{
			foreach (var zone in zones)
			{
				var amount = Math.Min(total, zone.Capacity(good));
				if (zone.Id == id) return amount;
				total -= amount;
			}
			return 0;
		}
		public static int Inside(IReadOnlyList<StorageZone> zones, int good, int total) => Math.Max(0, total - zones.Sum(z => z.Capacity(good)));
	}
}
