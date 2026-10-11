using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalStressProbeInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalStressProbe();
	}
	public sealed class MedievalStressProbe : INotifyDamage, INotifyKilled
	{
		public long EnemyDamage;
		public bool CombatDeath;
		public readonly HashSet<string> Attackers = [];
		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (e.Attacker == null || self.Owner.RelationshipWith(e.Attacker.Owner) != PlayerRelationship.Enemy || e.Damage.Value <= 0) return;
			EnemyDamage += e.Damage.Value; Attackers.Add(e.Attacker.Owner.InternalName);
		}
		void INotifyKilled.Killed(Actor self, AttackInfo e) { CombatDeath = e.Attacker != null && self.Owner.RelationshipWith(e.Attacker.Owner) == PlayerRelationship.Enemy; }
	}
	public class MedievalStressValidationInfo : TraitInfo
	{
		public readonly int SoldiersPerArmy = 500, WorkersPerArmy = 250, Duration = 4000;
		public readonly string Output = "massive";
		public override object Create(ActorInitializer init) => new MedievalStressValidation(this);
	}
	// Opt-in scenario. No artificial income, damage, healing or hunger exemptions during simulation.
	public sealed class MedievalStressValidation(MedievalStressValidationInfo info) : IWorldLoaded, ITick, ITickRender
	{
		sealed class Unit
		{
			public Actor Actor;
			public MedievalStressProbe Probe;
			public bool Soldier, Moved, Carried, Worked;
			public CPos Start;
		}
		readonly List<Unit> units = [];
		readonly List<Actor> sites = [];
		readonly List<Actor> structures = [];
		readonly List<object> samples = [];
		readonly List<double> timings = [];
		readonly List<string> errors = [];
		readonly Stopwatch timer = new();
		Player[] players;
		WorldRenderer renderer;
		string output;
		int tick, lastSample, maxSleeping, maxHungry, lastVisualTick, visualChecks;
		double lastTime, loadSeconds;
		bool done, ready;
		int pathingChecks, targetingChecks, spatialChecks;
		static readonly CPos[] Towns = [new(250, -60), new(480, -60), new(250, 175), new(480, 175)];
		static readonly CPos[] Camps = [new(368, 65), new(431, 65), new(368, 131), new(431, 131)];
		static readonly string[] Troops = ["militia", "swordsman", "archer", "manatarms", "elitearcher", "royalguard", "militia", "archer", "swordsman", "royalguard"];
		static readonly string[] Jobs = ["farmer", "farmer", "hauler", "hauler", "lumberjack", "stonecutter", "builder"];
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "load-test", info.Output); Directory.CreateDirectory(output);
			loadSeconds = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
			Console.WriteLine($"STRESS LOADED {w.Map.Title}: {w.Actors.Count()} actors, {loadSeconds:F1}s");
		}
		Actor Spawn(World w, Player p, string type, CPos cell, bool construction = false)
		{
			var init = new OpenRA.Primitives.TypeDictionary { new OwnerInit(p), new LocationInit(cell) };
			if (construction) init.Add(new MedievalConstructionInit());
			var a = w.CreateActor("md." + type, init);
			if (a.Info.HasTraitInfo<BuildingInfo>()) structures.Add(a);
			return a;
		}
		void Fill(Actor a)
		{
			var stock = a.TraitOrDefault<MedievalStockpile>()?.Stock;
			if (stock != null) for (var r = 0; r < 4; r++) stock.Add(r, stock.Capacity(r));
		}
		void AddUnit(Actor a, bool soldier)
		{
			units.Add(new Unit { Actor = a, Probe = a.Trait<MedievalStressProbe>(), Soldier = soldier, Start = a.Location });
		}
		void ValidatePaths(World w)
		{
			// Native collision/path tests on the cleared arena, before the armies spawn.
			var temporary = new List<Actor>();
			Actor Create(string type, CPos cell)
			{
				var a = w.CreateActor("md." + type, [new OwnerInit(players[0]), new LocationInit(cell)]);
				temporary.Add(a); return a;
			}
			void ClearBlockers()
			{
				foreach (var a in temporary.Skip(1).ToArray()) { w.Remove(a); a.Dispose(); temporary.Remove(a); }
			}
			void AssertPath(bool ok, string name) { pathingChecks++; Check(ok, "Pathing: " + name); }
			var source = new CPos(400, 100);
			var target = new CPos(404, 100);
			var actor = Create("militia", source);
			var finder = w.WorldActor.Trait<PathFinder>();
			var path = finder.FindPathToTargetCells(actor, source, [target, target + new CVec(0, 1)], BlockedByActor.All);
			AssertPath(path.Count > 0 && path[^1] == source && (path[0] == target || path[0] == target + new CVec(0, 1)), "nearby open targets");
			foreach (var offset in CVec.Directions) Create("palisade", source + offset);
			var blocked = finder.LocalTargetBlocked;
			AssertPath(finder.FindPathToTargetCells(actor, source, [target], BlockedByActor.All).Count == 0
				&& finder.LocalTargetBlocked > blocked, "surrounded attacker stops without a continent search");
			ClearBlockers();
			for (var y = 60; y <= 140; y++) Create("palisade", new CPos(402, y));
			var fallback = finder.LocalTargetFallbacks;
			path = finder.FindPathToTargetCells(actor, source, [target], BlockedByActor.All);
			AssertPath(path.Count > 80 && path.Any(c => c.Y < 60 || c.Y > 140) && finder.LocalTargetFallbacks > fallback,
				"long detour remains available after local budget exhaustion");
			ClearBlockers();
			Create("palisade", source);
			path = finder.FindPathToTargetCells(actor, source, [target], BlockedByActor.All);
			AssertPath(path.Count > 0 && path[^1] == source, "can leave an obstructed source");
			ClearBlockers();
			Create("palisade", target);
			var alternative = target + new CVec(0, 2);
			path = finder.FindPathToTargetCells(actor, source, [target, alternative], BlockedByActor.All);
			AssertPath(path.Count > 0 && path[0] == alternative, "rejects blocked destination but retains accessible alternative");
			ClearBlockers(); w.Remove(actor); actor.Dispose();
			Console.WriteLine($"STRESS PATHING: {pathingChecks} checks, {errors.Count} errors");
		}
		void Setup(World w)
		{
			players = new[] { w.LocalPlayer }.Concat(Enumerable.Range(1, 3).Select(n => w.Players.Single(p => p.InternalName == "Army" + n))).ToArray();
			foreach (var p in players)
			{
				foreach (var other in players.Where(o => o != p)) p.EnemyPlayersMask = p.EnemyPlayersMask.Union(other.PlayerMask);
				p.Shroud.Disabled = true;
			}
			ValidatePaths(w);
			for (var team = 0; team < players.Length; team++)
			{
				var p = players[team]; var origin = Towns[team]; var workers = 0;
				var blocks = Math.Max((info.WorkersPerArmy + 6) / 7, (info.SoldiersPerArmy + info.WorkersPerArmy + 35) / 36);
				for (var n = 0; n < blocks; n++)
				{
					var at = origin + new CVec(n % 6 * 16, n / 6 * 16);
					for (var h = 0; h < 3; h++) Spawn(w, p, "house", at + new CVec(h * 4, 0));
					Fill(Spawn(w, p, "warehouse", at + new CVec(0, 4))); Fill(Spawn(w, p, "market", at + new CVec(5, 4)));
					Spawn(w, p, "farm", at + new CVec(10, 4)); Spawn(w, p, "field.4x4", at + new CVec(9, 9));
					Spawn(w, p, "sawmill", at + new CVec(0, 9)); Spawn(w, p, "quarry", at + new CVec(4, 9));
					Spawn(w, w.Players.First(p => p.InternalName == "Neutral"), "tree", at + new CVec(0, 13));
					Spawn(w, w.Players.First(p => p.InternalName == "Neutral"), "stone", at + new CVec(4, 13));
					sites.Add(Spawn(w, p, "house", at + new CVec(12, 0), true));
					for (var j = 0; j < 7 && workers < info.WorkersPerArmy; j++, workers++) AddUnit(Spawn(w, p, Jobs[j], at + new CVec(7, 8+j)), false);
				}
				Check(MedievalSettlement.Housing(p) >= info.SoldiersPerArmy + info.WorkersPerArmy, "Insufficient initial housing: " + p.InternalName);
				for (var i = 0; i < info.SoldiersPerArmy; i++)
				{
					var type = i % 50 == 0 ? "drummer" : i % 50 == 1 ? "standardbearer" : Troops[i % Troops.Length];
					var a = Spawn(w, p, type, Camps[team] + new CVec(i % 25, i / 25));
					a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); AddUnit(a, true);
				}
				for (var i = 0; i < Math.Max(2, info.SoldiersPerArmy / 25); i++)
				{
					var a = Spawn(w, p, "supplycart", Camps[team] + new CVec(i % 10 * 3, -3 - i / 10 * 3)); Fill(a);
				}
			}
			foreach (var unit in units.Where(u => u.Soldier).Where((u, i) => i % 8 == 0))
				targetingChecks += TargetingReference.Verify(unit.Actor);
			var clock = w.WorldActor.Trait<MedievalDayNight>().Clock;
			var nightAt = info.Duration * 3 / 5;
			for (var i = 0; i < clock.DayTicks - nightAt; i++) clock.Advance();
			ready = true; timer.Start();
			Console.WriteLine($"STRESS READY: {units.Count} citizens/soldiers, {structures.Count} structures, four armies");
			Write(w, false);
		}
		void Orders(World w, bool first)
		{
			var i = 0;
			foreach (var unit in units.Where(u => u.Soldier && MedievalStockpile.Active(u.Actor)))
			{
				var a = unit.Actor; if (!first && !a.IsIdle) continue;
				a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.AttackAnything);
				var target = new CPos(412 + i % 17 - 8, 115 + i / 17 % 17 - 8); i++;
				w.IssueOrder(new Order(a.Info.HasTraitInfo<ArmamentInfo>() ? "AttackMove" : "Move", a, Target.FromCell(w, target), false));
			}
		}
		object Army(Player p) => new { player = p.InternalName,
			soldiers = units.Count(u => u.Soldier && u.Actor.Owner == p && MedievalStockpile.Active(u.Actor)),
			workers = units.Count(u => !u.Soldier && u.Actor.Owner == p && MedievalStockpile.Active(u.Actor)),
			combatDeaths = units.Count(u => u.Actor.Owner == p && u.Probe.CombatDeath),
			damageReceived = units.Where(u => u.Actor.Owner == p).Sum(u => u.Probe.EnemyDamage),
			opponents = units.Where(u => u.Actor.Owner == p).SelectMany(u => u.Probe.Attackers).Distinct().Order().ToArray(),
			foodConsumed = p.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed,
			starvationDeaths = p.PlayerActor.Trait<MedievalFoodEconomy>().StarvationDeaths,
			moved = units.Count(u => u.Actor.Owner == p && u.Moved), carried = units.Count(u => u.Actor.Owner == p && u.Carried),
			worked = units.Count(u => u.Actor.Owner == p && u.Worked) };
		void Write(World w, bool final)
		{
			var ordered = timings.Order().ToArray();
			double Percentile(double p) => ordered.Length == 0 ? 0 : ordered[(int)((ordered.Length - 1) * p)];
			var report = new { passed = final && errors.Count == 0, errors, tick, map = w.Map.Title, mapSize = w.Map.MapSize.ToString(),
				requestedSoldiers = info.SoldiersPerArmy * 4, requestedWorkers = info.WorkersPerArmy * 4, initialStructures = structures.Count,
				aliveActors = w.Actors.Count(), loadSeconds, seconds = timer.Elapsed.TotalSeconds,
				syncChecks = Game.Settings.Debug.SyncCheckUnsyncedCode, averageTickMs = timings.Count == 0 ? 0 : timings.Average(),
				p50TickMs = Percentile(.5), p95TickMs = Percentile(.95), p99TickMs = Percentile(.99), maxTickMs = ordered.LastOrDefault(),
				peakMemoryMiB = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576, maxSleeping, maxHungry, visualChecks, pathingChecks, targetingChecks, spatialChecks,
				localTargetPaths = w.WorldActor.Trait<PathFinder>().LocalTargetPaths,
				localTargetBlocked = w.WorldActor.Trait<PathFinder>().LocalTargetBlocked,
				localTargetFallbacks = w.WorldActor.Trait<PathFinder>().LocalTargetFallbacks,
				completedSites = sites.Count(a => MedievalStockpile.Active(a) && a.Trait<MedievalBuilding>().Complete),
				armies = players.Select(Army).ToArray(), samples };
			File.WriteAllText(Path.Combine(output, final ? "result.json" : "progress.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
		}
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			if (!ready || tick % 500 != 0 || lastVisualTick == tick) return;
			lastVisualTick = tick;
			var trails = self.World.WorldActor.Trait<MedievalTrails>();
			foreach (var pair in trails.Trails.Marks)
			{
				var cell = self.World.Map.CellContaining(FootfallTrails.Position(pair.Key));
				if (self.World.FogObscures(cell)) continue;
				Check(trails.ObservedLevel(pair.Key) == (trails.ProtectsCell(cell) ? 0 : pair.Value.Level), "Trail cache differs from visible terrain wear.");
			}
			foreach (var key in trails.ObservedKeys)
				if (!self.World.FogObscures(self.World.Map.CellContaining(FootfallTrails.Position(key))))
					Check(trails.Trails.Marks.ContainsKey(key), "Recovered trail remains visible after deletion.");
			visualChecks++;
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (!ready) return;
			if (tick > 20) timings.Add(PerfHistory.Items["tick_time"].LastValue);
			if (tick == 200 || tick > 200 && tick % 500 == 0) Orders(w, tick == 200);
			foreach (var u in units.Where(u => MedievalStockpile.Active(u.Actor)))
			{
				u.Moved |= u.Actor.Location != u.Start;
				if (u.Actor.TraitOrDefault<MedievalWorker>() is { } worker) { u.Carried |= worker.Cargo > 0; u.Worked |= worker.IsWorking; }
			}
			var center = tick / 500 % 2 == 0 ? new CPos(412, 115) : Towns[0] + new CVec(20, 20);
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(center)));
			if (tick % 250 == 0)
			{
				if (tick % 500 == 0)
				{
					var expected = w.Actors.Where(a => a.IsInWorld && !a.IsDead && a.OccupiesSpace != null).Select(a => a.ActorID).Order();
					var indexed = w.ActorMap.ActorsInBox(new WPos(-10000000, -10000000, 0), new WPos(10000000, 10000000, 0))
						.Where(a => !a.IsDead).Select(a => a.ActorID).Order();
					spatialChecks++; Check(indexed.SequenceEqual(expected), "Spatial index must contain every live positioned actor exactly once.");
				}
				maxSleeping = Math.Max(maxSleeping, units.Count(u => MedievalStockpile.Active(u.Actor) && u.Actor.TraitOrDefault<MedievalRest>()?.Sleeping == true));
				maxHungry = Math.Max(maxHungry, players.Sum(MedievalFoodEconomy.Hungry));
				var time = timer.Elapsed.TotalSeconds;
				samples.Add(new { tick, seconds = time, ticksPerSecond = (tick - lastSample) / Math.Max(.001, time-lastTime),
					tickMs = PerfHistory.Items["tick_time"].Average(100), memoryMiB = Process.GetCurrentProcess().WorkingSet64 / 1048576,
					soldiers = units.Count(u => u.Soldier && MedievalStockpile.Active(u.Actor)), combatDeaths = units.Count(u => u.Probe.CombatDeath),
					sleeping = maxSleeping, hungry = maxHungry }); lastTime = time; lastSample = tick; Write(w, false);
				Console.WriteLine($"STRESS {tick}/{info.Duration}: {time:F1}s, tick {PerfHistory.Items["tick_time"].Average(100):F1}ms, combat deaths {units.Count(u => u.Probe.CombatDeath)}, sleeping {maxSleeping}");
			}
			if (tick is 350 or 1200 || tick == info.Duration - 100) Game.Renderer.SaveScreenshot(Path.Combine(output, $"view-{tick}.png"));
			if (tick < info.Duration) return;
			foreach (var p in players)
			{
				var army = units.Where(u => u.Actor.Owner == p).ToArray();
				Check(army.Any(u => u.Probe.EnemyDamage > 0) && army.Any(u => u.Probe.CombatDeath), "Army did not take damage and casualties: " + p.InternalName);
				Check(army.Count(u => u.Soldier && u.Moved) > info.SoldiersPerArmy / 2, "Most soldiers failed to move: " + p.InternalName);
				Check(army.Any(u => !u.Soldier && u.Carried) && army.Any(u => !u.Soldier && u.Worked), "Civilian production/transport missing: " + p.InternalName);
				Check(p.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed > 0, "No real food consumption: " + p.InternalName);
			}
			Check(maxSleeping > 0, "Citizens did not reach beds at night.");
			Check(sites.Any(a => MedievalStockpile.Active(a) && a.Trait<MedievalBuilding>().Complete), "No construction completed.");
			Write(w, true); done = true; Console.WriteLine("MEDIEVAL STRESS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
