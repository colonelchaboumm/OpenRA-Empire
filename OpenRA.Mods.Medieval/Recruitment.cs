using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// Recruitment consumes food; only advanced troops also consume meat.
	// Wood and stone remain construction materials and cannot enter this price.
	public sealed class RecruitmentCost
	{
		public readonly int Food;
		public readonly int Meat;
		public readonly bool Elite;
		public RecruitmentCost(int food, int meat = 0, bool elite = false)
		{
			if (food <= 0 || meat < 0) throw new ArgumentException("Recruitment needs positive food and non-negative meat.");
			if (elite && meat == 0) throw new ArgumentException("Elite troops require meat for recruitment.");
			Food = food;
			Meat = meat;
			Elite = elite;
		}
		public bool CanPay(IEnumerable<Inventory> inventories)
		{
			var stores = inventories.Distinct().ToArray();
			return stores.Sum(s => s.Available((int)Material.Food)) >= Food
				&& stores.Sum(s => s.Available((int)Material.Meat)) >= Meat;
		}
		public bool TryPay(IEnumerable<Inventory> inventories)
		{
			var stores = inventories.Distinct().ToArray();
			// Check the whole price before removing anything. Incoming or reserved goods cannot pay.
			if (!CanPay(stores)) return false;
			var food = Food;
			var meat = Meat;
			foreach (var stock in stores)
			{
				food -= stock.Take((int)Material.Food, food);
				meat -= stock.Take((int)Material.Meat, meat);
			}
			return true;
		}
		public override string ToString() => Food + " nourriture" + (Meat > 0 ? " + " + Meat + " viande" : "");
	}

	public class MedievalRecruitableInfo : TraitInfo
	{
		public readonly int Food = 10;
		public readonly int Meat = 0;
		public readonly bool Elite = false;
		public RecruitmentCost Cost => new(Food, Meat, Elite);
		public override object Create(ActorInitializer init) => new MedievalRecruitable();
	}
	public class MedievalRecruitable { }
}
