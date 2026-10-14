using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed class InfluenceRadius
	{
		public const int Minimum = 4 * 1024;
		public const int Maximum = 30 * 1024;
		public int Value { get; private set; } = Minimum;
		public int Target { get; private set; } = Minimum;
		public void Advance(int pride, int health, bool village)
		{
			Target = (Minimum + (Maximum - Minimum) * Math.Clamp(pride, 0, 100) / 100)
				* (75 + Math.Clamp(health, 0, 100) / 4) / 100;
			if (village) Target = Target * 3 / 4;
			// One eighth of a cell per simulation second, both growing and receding.
			Value += Math.Clamp(Target - Value, -128, 128);
		}
		public static int Pressure(int ownPride, int foreignPride) => Math.Clamp((foreignPride - ownPride - 5) / 10, 0, 6);
	}

	public readonly record struct InfluenceClaim(Player Owner, long Power);
	public static class InfluenceWork
	{
		// Carry fractional ticks: rounding every 8-tick job to 9 would turn even
		// a one-percent mood penalty into an eleven-percent production penalty.
		public static int Delay(int normalTicks, int percent, ref int remainder)
		{
			var scaled = normalTicks * 1000000 / Math.Clamp(percent, 1, 100) + remainder;
			remainder = scaled % 10000;
			return scaled / 10000;
		}
	}
	public sealed class InfluenceSource(Actor actor)
	{
		public Actor Actor = actor;
		public readonly Player Owner = actor.Owner;
		public readonly InfluenceRadius Radius = new();
		public WPos Position => Actor.CenterPosition;
	}

	// The union takes the strongest source, never the sum of nearby markets.
	public sealed class InfluenceField
	{
		readonly Dictionary<CPos, InfluenceClaim> claims = [];
		public IReadOnlyDictionary<CPos, InfluenceClaim> Claims => claims;
		public void Clear() => claims.Clear();
		public InfluenceClaim At(CPos cell) => claims.GetValueOrDefault(cell);
		public static long Power(WPos center, WPos position, int radius) => (long)radius * radius - (center - position).HorizontalLengthSquared;
		public static bool Stronger(long power, uint nation, long previousPower, uint previousNation) => power > 0
			&& (power > previousPower || power == previousPower && nation < previousNation);
		public void Offer(CPos cell, Player owner, long power)
		{
			var previous = At(cell);
			if (Stronger(power, owner.PlayerActor.ActorID, previous.Power, previous.Owner?.PlayerActor.ActorID ?? uint.MaxValue))
				claims[cell] = new(owner, power);
		}
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalInfluenceInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalInfluence();
	}
	public sealed partial class MedievalInfluence : ITick, ISync
	{
		readonly SortedDictionary<ulong, InfluenceSource> sources = [];
		readonly Dictionary<Player, int> health = [];
		public readonly InfluenceField Field = new();
		public IEnumerable<InfluenceSource> Sources => sources.Values;
		[VerifySync] int clock;
		[VerifySync] public int SourcesHash => sources.Aggregate(17, (h, s) => unchecked(((h * 31 + (int)s.Key) * 31
			+ (int)s.Value.Owner.PlayerActor.ActorID) * 31 + s.Value.Radius.Value * 31 + s.Value.Radius.Target));
		public int Health(Player player) => health.GetValueOrDefault(player, 100);
		public int Radius(Player player) => sources.Values.Where(s => s.Owner == player).Select(s => s.Radius.Value).DefaultIfEmpty().Max();
		public int TargetRadius(Player player) => sources.Values.Where(s => s.Owner == player).Select(s => s.Radius.Target).DefaultIfEmpty().Max();
		public int Area(Player player) => Field.Claims.Count(c => c.Value.Owner == player);
		static ulong CityKey(Actor actor) => ((ulong)actor.Owner.PlayerActor.ActorID << 32)
			| (MedievalLocalities.Id(actor) > 0 ? (uint)MedievalLocalities.Id(actor) : actor.ActorID | 0x80000000u);
		void ITick.Tick(Actor self)
		{
			if (++clock < 25) return;
			clock = 0;
			var world = self.World;
			var centers = world.ActorsHavingTrait<MedievalLocality>().Where(a => MedievalStockpile.Active(a) && !a.WillDispose
				&& MedievalNationalPride.For(a.Owner) != null)
				.GroupBy(CityKey)
				.Select(g => g.OrderBy(a => a.TraitOrDefault<MedievalMarket>() == null ? 1 : 0).ThenBy(a => a.ActorID).First()).OrderBy(a => a.ActorID).ToArray();
			var present = centers.Select(CityKey).ToHashSet();
			foreach (var id in sources.Keys.Where(id => !present.Contains(id)).ToArray()) sources.Remove(id);
			health.Clear();
			foreach (var owner in centers.Select(a => a.Owner).Distinct())
			{
				var citizens = MedievalFoodEconomy.Citizens(owner).ToArray();
				health[owner] = citizens.Length == 0 ? 100 : (int)(citizens.Sum(a => 100L * a.Trait<Health>().HP / a.Trait<Health>().MaxHP) / citizens.Length);
			}
			Field.Clear();
			foreach (var center in centers)
			{
				var key = CityKey(center);
				if (!sources.TryGetValue(key, out var source)) sources[key] = source = new InfluenceSource(center);
				// Another market can carry on the same city's influence after its anchor is lost.
				source.Actor = center;
				source.Radius.Advance(MedievalNationalPride.For(center.Owner).State.Value, Health(center.Owner), center.TraitOrDefault<MedievalMarket>() == null);
				foreach (var cell in world.Map.FindTilesInCircle(world.Map.CellContaining(source.Position), (source.Radius.Value + 1023) / 1024 + 2))
					if (world.Map.Contains(cell)) Field.Offer(cell, source.Owner, InfluenceField.Power(source.Position, world.Map.CenterOfCell(cell), source.Radius.Value));
			}
		}
		public (int Penalty, Player Nation) Pressure(Actor actor)
		{
			var own = MedievalNationalPride.For(actor.Owner);
			if (own == null) return (0, null);
			var cell = actor.World.Map.CellContaining(actor.CenterPosition);
			var best = 0; Player nation = null;
			// Also reach two cells beyond a shared frontier. This is cultural pressure, not a weapon aura.
			for (var y = -2; y <= 2; y++)
				for (var x = -2; x <= 2; x++)
				{
					if (x * x + y * y > 4) continue;
					var owner = Field.At(cell + new CVec(x, y)).Owner;
					if (owner == null || actor.Owner.RelationshipWith(owner) != PlayerRelationship.Enemy) continue;
					var penalty = InfluenceRadius.Pressure(own.State.Value, MedievalNationalPride.For(owner)?.State.Value ?? 0);
					if (penalty > best || penalty == best && penalty > 0 && owner.PlayerActor.ActorID < nation.PlayerActor.ActorID)
					{ best = penalty; nation = owner; }
				}
			return (best, nation);
		}
	}

	public class MedievalInfluenceMoodInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalInfluenceMood(init.Self);
	}
	public sealed class MedievalInfluenceMood(Actor actor) : ITick, INotifyOwnerChanged, ISync
	{
		[VerifySync] int clock;
		[VerifySync] public int Penalty { get; private set; }
		[VerifySync] public int Target { get; private set; }
		[VerifySync] public int NationId => (int)(Nation?.PlayerActor.ActorID ?? 0);
		public Player Nation { get; private set; }
		public bool Civilian => actor.TraitOrDefault<MedievalMorale>() == null;
		public int WorkPercent => Civilian ? 100 - Penalty : 100;
		public string Description => Penalty == 0 ? "" : $"Influence de {MedievalHeraldry.For(Nation).CountryName} : moral -{Penalty}"
			+ (Civilian ? $", travail {WorkPercent}%" : "");
		void ITick.Tick(Actor self)
		{
			if (++clock < 25) return;
			clock = 0;
			var pressure = MedievalTowerArcher.Present(self) && self.TraitOrDefault<MedievalNeedsFood>() != null
				? self.World.WorldActor.TraitOrDefault<MedievalInfluence>()?.Pressure(self) ?? (0, null) : (0, null);
			Target = pressure.Item1;
			if (pressure.Item2 != null) Nation = pressure.Item2;
			Penalty += Math.Clamp(Target - Penalty, -1, 1);
			if (Penalty == 0) Nation = null;
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Penalty = Target = 0; Nation = null; }
	}
}
