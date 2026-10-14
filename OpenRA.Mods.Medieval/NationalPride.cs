using System;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct NationalPrideFacts(int Population, int Gold, int Food, int FoodPerMinute, int ArmyStrength, int Hungry, int Starving);

	// Updated once per simulation second. No local visibility, rendering or random state participates.
	public sealed class NationalPrideState
	{
		public int Value { get; private set; } = 50;
		public int Target { get; private set; } = 50;
		public int Deaths { get; private set; }
		public int Burials { get; private set; }
		public int Unburied => Deaths - Burials;
		public int FamineMemory { get; private set; }
		public int LastStarvationDeaths { get; private set; }
		public int GoldPoints { get; private set; }
		public int FoodPoints { get; private set; }
		public int ArmyPoints { get; private set; }
		public int WelfarePoints { get; private set; }
		public int BurialPoints { get; private set; } = 10;
		public int MoraleBonus => (Value - 50) / 5;
		public string Status => Value < 25 ? "Decouragee" : Value < 45 ? "Fragile" : Value < 65 ? "Confiante" : Value < 85 ? "Fiere" : "Rayonnante";
		public void RecordDeath() { if (Deaths < int.MaxValue) Deaths++; }
		public bool RecordBurial()
		{
			if (Burials >= Deaths) return false;
			Burials++; return true;
		}
		static int Share(long amount, long full, int maximum) => (int)(Math.Clamp(amount, 0, Math.Max(1, full)) * maximum / Math.Max(1, full));
		public static int Strength(int hp, bool hungry, bool starving) => starving ? 0 : Math.Max(0, hp) / (hungry ? 200 : 100);
		public void Advance(NationalPrideFacts facts, int starvationDeaths)
		{
			if (starvationDeaths > LastStarvationDeaths) FamineMemory = 120;
			else if (FamineMemory > 0) FamineMemory--;
			LastStarvationDeaths = starvationDeaths;
			var population = Math.Max(1, facts.Population);
			GoldPoints = Share(facts.Gold, Math.Max(100L, population * 10L), 15);
			FoodPoints = Share(facts.Food, Math.Max(20L, facts.FoodPerMinute * 4L), 20);
			ArmyPoints = Share(facts.ArmyStrength, Math.Max(300L, population * 20L), 15);
			WelfarePoints = 20 - Share(facts.Hungry, population, 20) - Share(facts.Starving, population, 20) - (FamineMemory > 0 ? 10 : 0);
			// Caring for everyone restores the same maximum as a nation that has lost nobody.
			BurialPoints = Deaths == 0 ? 10 : Share(Burials, Deaths, 10);
			Target = Math.Clamp(20 + GoldPoints + FoodPoints + ArmyPoints + WelfarePoints + BurialPoints, 0, 100);
			if (facts.Starving > 0) Target = Math.Min(Target, 60);
			if (FamineMemory > 0) Target = Math.Min(Target, 50);
			Value += Math.Clamp(Target - Value, -2, 1);
		}

		public int Hash
		{
			get { unchecked { return (((((Value * 31 + Target) * 31 + Deaths) * 31 + Burials) * 31 + FamineMemory) * 31 + LastStarvationDeaths); } }
		}
	}

	[TraitLocation(SystemActors.Player)]
	public class MedievalNationalPrideInfo : TraitInfo, Requires<MedievalFoodEconomyInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalNationalPride();
	}
	public sealed class MedievalNationalPride : ITick, ISync
	{
		public readonly NationalPrideState State = new();
		public NationalPrideFacts Facts { get; private set; }
		[VerifySync] public int StateHash => State.Hash;
		[VerifySync] int clock;
		public static MedievalNationalPride For(Player player) => player?.PlayerActor.TraitOrDefault<MedievalNationalPride>();
		void ITick.Tick(Actor self)
		{
			if (++clock < 25) return;
			clock = 0;
			var citizens = MedievalFoodEconomy.Citizens(self.Owner).ToArray();
			var stocks = self.World.ActorsWithTrait<MedievalStockpile>().Where(p => p.Actor.Owner == self.Owner && MedievalStockpile.Active(p.Actor)).ToArray();
			var army = citizens.Where(a => a.TraitOrDefault<MedievalMorale>() != null && a.TraitsImplementing<Armament>().Any())
				.Sum(a => NationalPrideState.Strength(a.Trait<Health>().HP, a.Trait<MedievalNeedsFood>().Need.Hungry, a.Trait<MedievalNeedsFood>().Need.Starving));
			Facts = new(citizens.Sum(MedievalSettlement.CitizenCount), stocks.Sum(p => p.Trait.Stock.Available((int)Material.Gold)),
				stocks.Sum(p => p.Trait.Stock.Available((int)Material.Food)), (int)Math.Ceiling(citizens.Sum(a => a.Trait<MedievalNeedsFood>().Need.DemandPerMinute)), army,
				citizens.Where(a => a.Trait<MedievalNeedsFood>().Need.Hungry).Sum(MedievalSettlement.CitizenCount),
				citizens.Where(a => a.Trait<MedievalNeedsFood>().Need.Starving).Sum(MedievalSettlement.CitizenCount));
			State.Advance(Facts, self.Trait<MedievalFoodEconomy>().StarvationDeaths);
		}
	}
}
