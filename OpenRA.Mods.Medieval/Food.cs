using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// All durations use simulation ticks: 25 ticks per second at normal speed.
	// Each person has their own meal clock, preserved when ownership changes.
	public sealed class FoodNeed
	{
		public const int TicksPerMinute = 1500;
		public readonly int MealInterval;
		public readonly int StarvationDelay;
		public readonly int DamageInterval;
		public int TicksSinceMeal { get; private set; }
		public int TicksWithoutSupply { get; private set; }
		public bool InSupply { get; private set; }
		public bool MealDue => TicksSinceMeal >= MealInterval;
		public bool Hungry => TicksWithoutSupply >= MealInterval;
		public bool Starving => TicksWithoutSupply >= StarvationDelay;
		public bool DamageDue => Starving && (TicksWithoutSupply - StarvationDelay) % DamageInterval == 0;
		public double DemandPerMinute => (double)TicksPerMinute / MealInterval;
		public FoodNeed(int mealInterval = 750, int starvationDelay = 3000, int damageInterval = 125)
		{
			if (mealInterval <= 0 || starvationDelay <= mealInterval || damageInterval <= 0)
				throw new ArgumentException("Food intervals must be positive and starvation must follow hunger.");
			MealInterval = mealInterval; StarvationDelay = starvationDelay; DamageInterval = damageInterval;
		}
		public void Advance()
		{
			if (TicksSinceMeal < int.MaxValue) TicksSinceMeal++;
			if (TicksWithoutSupply < int.MaxValue) TicksWithoutSupply++;
			InSupply = false;
		}
		public void Sustain() { TicksWithoutSupply = 0; InSupply = true; }
		public void Feed() { TicksSinceMeal = 0; Sustain(); }
		// Packed settlers keep their food debt and hunger when leaving the caravan.
		public void Inherit(FoodNeed previous)
		{
			TicksSinceMeal = (int)Math.Min(int.MaxValue, (long)previous.TicksSinceMeal * MealInterval / previous.MealInterval);
			TicksWithoutSupply = previous.TicksWithoutSupply; InSupply = previous.InSupply;
		}
	}

	public readonly record struct FoodStation(Inventory Stock, WPos Position, WDist Radius, int Priority = 0)
	{
		public bool Covers(WPos position) => (position - Position).HorizontalLengthSquared <= (long)Radius.Length * Radius.Length;
	}

	public static class FoodSupply
	{
		// Rebuilt for this allocation pass: carts move and stores may change owner
		// or be destroyed between ticks. Buckets preserve the input provider order.
		sealed class CoverageIndex
		{
			readonly FoodStation[] sources;
			readonly Dictionary<int2, List<FoodStation>> buckets;
			public CoverageIndex(FoodStation[] sources)
			{
				this.sources = sources;
				// Small villages need no index. Unusually large modded radii retain
				// the linear path instead of creating an unbounded number of buckets.
				if (sources.Length < 8 || sources.Any(s => Math.Abs((long)s.Radius.Length) > 65536)) return;
				buckets = [];
				foreach (var source in sources)
				{
					var radius = Math.Abs((long)source.Radius.Length);
					for (var x = (int)((source.Position.X - radius) >> 14); x <= (source.Position.X + radius) >> 14; x++)
						for (var y = (int)((source.Position.Y - radius) >> 14); y <= (source.Position.Y + radius) >> 14; y++)
						{
							var key = new int2(x, y);
							if (!buckets.TryGetValue(key, out var local)) buckets.Add(key, local = []);
							local.Add(source);
						}
				}
			}
			public IEnumerable<FoodStation> Near(WPos position) => buckets == null ? sources
				: buckets.TryGetValue(new int2(position.X >> 14, position.Y >> 14), out var local) ? local : [];
		}

		// Oldest missed meal first; stable input order breaks ties. One meal recovers
		// a starving person, without accumulating an unpayable backlog of food debt.
		public static int Feed(IEnumerable<(FoodNeed Need, WPos Position)> people, IEnumerable<FoodStation> stations)
		{
			var sources = stations.DistinctBy(s => s.Stock).Where(s => s.Stock.Available((int)Material.Food) > 0).ToArray();
			if (sources.Length == 0) return 0;
			var coverage = new CoverageIndex(sources);
			var consumed = 0;
			foreach (var person in people.DistinctBy(p => p.Need).OrderByDescending(p => p.Need.TicksWithoutSupply))
			{
				FoodStation? best = null;
				var distance = long.MaxValue;
				foreach (var station in coverage.Near(person.Position))
				{
					if (!station.Covers(person.Position) || station.Stock.Available((int)Material.Food) <= 0) continue;
					// Between meals, any stocked provider sustains coverage without taking food.
					if (!person.Need.MealDue) { best = station; break; }
					var candidateDistance = (station.Position - person.Position).HorizontalLengthSquared;
					if (best == null || station.Priority < best.Value.Priority || station.Priority == best.Value.Priority && candidateDistance < distance)
					{ best = station; distance = candidateDistance; }
				}
				if (best == null) continue;
				if (!person.Need.MealDue) { person.Need.Sustain(); continue; }
				best.Value.Stock.Take((int)Material.Food, 1); person.Need.Feed(); consumed++;
			}
			return consumed;
		}
	}

	// Observation only: actual harvests/deliveries, never a theoretical field yield.
	public sealed class FoodFlowMeter
	{
		readonly int[] buckets = new int[FoodNeed.TicksPerMinute];
		int cursor;
		int elapsed;
		public int Total { get; private set; }
		public double PerMinute => (double)Total * FoodNeed.TicksPerMinute / Math.Max(1, elapsed);
		public void Advance()
		{
			cursor = (cursor + 1) % buckets.Length;
			Total -= buckets[cursor];
			buckets[cursor] = 0;
			elapsed = Math.Min(elapsed + 1, buckets.Length);
		}
		public void Record(int amount)
		{
			if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
			buckets[cursor] += amount;
			Total += amount;
		}
	}

	public class MedievalNeedsFoodInfo : TraitInfo, Requires<HealthInfo>
	{
		public readonly int MealInterval = 750;
		public readonly int StarvationDelay = 3000;
		public readonly int DamageInterval = 125;
		public readonly int StarvationDamagePercent = 5;
		public readonly int HungrySpeedPercent = 75;
		public readonly int HungryFirepowerPercent = 75;
		public readonly int HungryWorkPercent = 50;
		public override object Create(ActorInitializer init) => new MedievalNeedsFood(this, init.Self);
	}
	public class MedievalNeedsFood : ISync, ISpeedModifier, IFirepowerModifier
	{
		public readonly MedievalNeedsFoodInfo Info;
		public readonly FoodNeed Need;
		readonly Actor actor;
		[VerifySync] public int HungerClock => unchecked((Need.TicksSinceMeal * 31 + Need.TicksWithoutSupply) * 2 + (Need.InSupply ? 1 : 0));
		public int BaseWorkPercent => Need.Hungry ? Info.HungryWorkPercent : 100;
		public int WorkPercent => (BaseWorkPercent * (actor?.TraitOrDefault<MedievalInfluenceMood>()?.WorkPercent ?? 100) + 99) / 100;
		public MedievalNeedsFood(MedievalNeedsFoodInfo info, Actor self = null)
		{
			if (info.StarvationDamagePercent <= 0 || info.StarvationDamagePercent > 100
				|| info.HungryWorkPercent <= 0 || info.HungryWorkPercent > 100
				|| info.HungrySpeedPercent <= 0 || info.HungrySpeedPercent > 100
				|| info.HungryFirepowerPercent <= 0 || info.HungryFirepowerPercent > 100)
				throw new ArgumentException("Food penalties must be percentages between 1 and 100.");
			Info = info; actor = self;
			Need = new FoodNeed(info.MealInterval, info.StarvationDelay, info.DamageInterval);
		}
		int ISpeedModifier.GetSpeedModifier() => Need.Hungry ? Info.HungrySpeedPercent : 100;
		int IFirepowerModifier.GetFirepowerModifier() => Need.Hungry ? Info.HungryFirepowerPercent : 100;
		public string Description => FoodDescription + (actor?.TraitOrDefault<MedievalInfluenceMood>() is { Civilian: true, Penalty: > 0 } mood ? " | " + mood.Description : "");
		string FoodDescription => Need.Starving ? "Famine : pertes de sante, ravitaillement urgent !"
			: Need.Hungry ? $"Faim : activite reduite, famine dans {(Need.StarvationDelay - Need.TicksWithoutSupply + 24) / 25}s"
			: Need.InSupply ? $"Ravitaille : prochain repas dans {Math.Max(0, (Need.MealInterval - Need.TicksSinceMeal + 24) / 25)}s"
			: $"Hors ravitaillement : faim dans {Math.Max(0, (Need.MealInterval - Need.TicksWithoutSupply + 24) / 25)}s";
		public void ApplyStarvationDamage(Actor self)
		{
			if (self.IsDead) return;
			if (!Need.DamageDue) return;
			var health = self.Trait<Health>();
			// Armor cannot protect against famine. Other damage already suffered is retained.
			health.InflictDamage(self, self, new Damage(Math.Max(1, health.MaxHP * Info.StarvationDamagePercent / 100)), true);
			if (self.IsDead && self.Owner.PlayerActor.TraitOrDefault<MedievalFoodEconomy>() is MedievalFoodEconomy economy)
				economy.RecordDeath();
		}
	}

	public class MedievalFoodEconomyInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFoodEconomy();
	}
	public class MedievalFoodEconomy : ITick, ISync
	{
		public readonly FoodFlowMeter Harvests = new();
		public readonly FoodFlowMeter Deliveries = new();
		public readonly FoodFlowMeter MarketDeliveries = new();
		[VerifySync] public int TotalConsumed { get; private set; }
		[VerifySync] public int StarvationDeaths { get; private set; }
		public static IEnumerable<Actor> Citizens(Player player) => player.World.ActorsHavingTrait<MedievalNeedsFood>().Where(a => a.Owner == player
			&& MedievalTowerArcher.Present(a)).OrderBy(a => a.ActorID);
		public static IEnumerable<FoodNeed> Needs(Player player) => Citizens(player).Select(a => a.Trait<MedievalNeedsFood>().Need);
		public static double Demand(Player player) => Needs(player).Sum(n => n.DemandPerMinute);
		public static int Hungry(Player player) => Citizens(player).Where(a => a.Trait<MedievalNeedsFood>().Need.Hungry).Sum(MedievalSettlement.CitizenCount);
		public static int Starving(Player player) => Citizens(player).Where(a => a.Trait<MedievalNeedsFood>().Need.Starving).Sum(MedievalSettlement.CitizenCount);
		public static IEnumerable<Actor> Providers(Player p) => p.World.ActorsHavingTrait<MedievalFoodProvider>().Where(a => a.Owner == p && MedievalStockpile.Active(a)
			&& a.TraitOrDefault<MedievalFoodProvider>() != null).OrderBy(a => a.ActorID);
		public static int ReadyFood(Player p) => Providers(p).Sum(a => a.Trait<MedievalStockpile>().Stock.Available((int)Material.Food));
		public static FoodStation Station(Actor a) => new(a.Trait<MedievalStockpile>().Stock, a.CenterPosition,
			a.Trait<MedievalFoodProvider>().Info.Radius, a.TraitOrDefault<MedievalMarket>() != null ? 0 : 1);
		public static int Covered(Player p)
		{
			var stations = Providers(p).Select(Station).Where(s => s.Stock.Available((int)Material.Food) > 0).ToArray();
			return Citizens(p).Where(a => stations.Any(s => s.Covers(a.CenterPosition))).Sum(MedievalSettlement.CitizenCount);
		}
		public void RecordDeath() { StarvationDeaths++; }
		void ITick.Tick(Actor self)
		{
			Harvests.Advance();
			Deliveries.Advance();
			MarketDeliveries.Advance();
			var citizens = Citizens(self.Owner).ToArray();
			var needs = citizens.Select(a => a.Trait<MedievalNeedsFood>().Need).ToArray();
			foreach (var need in needs) need.Advance();
			// Meals are allocated before penalties/damage in the same tick, avoiding
			// a spurious hunger frame when a meal is due and food is already available.
			TotalConsumed += FoodSupply.Feed(citizens.Select(a => (a.Trait<MedievalNeedsFood>().Need, a.CenterPosition)), Providers(self.Owner).Select(Station));
			foreach (var citizen in citizens) citizen.Trait<MedievalNeedsFood>().ApplyStarvationDamage(citizen);
		}
	}
}
