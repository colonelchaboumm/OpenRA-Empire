using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalFirefightingValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFirefightingValidation();
	}
	public sealed class MedievalFirefightingValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor farmer, carrier, guard, warehouse, well, dryFire, saboteur, enemySaboteur, tent, doomed;
		Actor[] people;
		int tick, phase, since, ignited, yardHP, yardBuckets, visits;
		bool done, slept, woke, filled, poured, quenched, resumedSleep, resumedWork, yardQuenched, resumedPatrol, noWell, tinyTorch, hiddenTorch, cancelled, destroyed;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string text) { if (!ok && !errors.Contains(text)) { errors.Add(text); Console.WriteLine($"FIRE CHECK {tick}: {text}"); } }
		Actor Spawn(World w, string type, int x, int y, Player owner = null) => w.CreateActor("md." + type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Next() { phase++; since = tick; Console.WriteLine($"FIRE PHASE {phase} at {tick}"); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "firefighting"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"status\":\"running\"}");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			var enemy = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = enemy.PlayerMask; enemy.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			w.LocalPlayer.Shroud.Disabled = true; enemy.Shroud.Disabled = true;
			Spawn(w, "house", 29, 27); Spawn(w, "market", 26, 31).Trait<MedievalStockpile>().Stock.Add(1, 180);
			tent = Spawn(w, "militarytent", 35, 29); tent.Trait<MedievalCampArea>().RestEnabled = true;
			warehouse = Spawn(w, "warehouse", 34, 36); dryFire = Spawn(w, "warehouse", 47, 40);
			foreach (var r in new[] { 0, 1, 2, 3 }) warehouse.Trait<MedievalStockpile>().Stock.Add(r, 80);
			farmer = Spawn(w, "farmer", 30, 31); carrier = Spawn(w, "hauler", 31, 31); guard = Spawn(w, "militia", 36, 32);
			carrier.Trait<MedievalWorker>().Cargo = 7; carrier.Trait<MedievalWorker>().Resource = 3; carrier.Trait<MedievalWorker>().Automatic = false;
			guard.Trait<AutoTarget>().SetStance(guard, UnitStance.HoldFire);
			people = [farmer, carrier, guard];
			saboteur = Spawn(w, "saboteur", 42, 30); enemySaboteur = Spawn(w, "saboteur", 45, 30, enemy);
		}
		void Finish()
		{
			Check(slept && woke && filled && poured && quenched && resumedSleep && resumedWork && yardQuenched && resumedPatrol && noWell && tinyTorch && hiddenTorch && cancelled && destroyed, "Every firefighting stage must complete.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, phase, slept, woke, filled, poured, quenched, resumedSleep, resumedWork, yardQuenched, resumedPatrol, noWell, tinyTorch, hiddenTorch, cancelled, destroyed }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL FIREFIGHTING: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(35, 33))));
			if (tick % 250 == 0) Console.WriteLine($"FIRE {tick}, phase {phase}: " + string.Join("; ", people.Select(a => a.Trait<MedievalFirefighter>().Description + " / " + a.Trait<MedievalRest>().Description)));
			if (tick == 80) guard.Trait<MedievalRest>().CampRest(guard);
			if (tick == 100)
			{
				Check(!MedievalTorch.Lit(saboteur), "The saboteur torch must be off in daylight.");
				dryFire.Trait<MedievalBurnable>().Ignite(dryFire, saboteur, dryFire.Location, false);
			}
			if (tick == 200)
			{
				noWell = people.All(a => !a.Trait<MedievalFirefighter>().Busy) && dryFire.Trait<MedievalBurnable>().WaterBuckets == 0;
				Check(noWell, "Without a well, no water may be created remotely.");
			}
			if (phase == 0 && tick == 1300)
			{
				slept = people.All(a => a.Trait<MedievalRest>().Sleeping);
				Check(slept && guard.Trait<MedievalRest>().OrderedMilitaryRest, "Citizens and explicitly resting militia must sleep before the alarm.");
				tinyTorch = MedievalTorch.Lit(saboteur) && saboteur.Trait<MedievalTorch>().Info.ScalePercent == 35;
				hiddenTorch = !enemySaboteur.CanBeViewedByPlayer(w.LocalPlayer) && !MedievalTorch.Lit(enemySaboteur);
				Check(tinyTorch && hiddenTorch, "Small nocturnal torch must not leak an undetected enemy saboteur.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "night-before-alarm.png"));
				w.AddFrameEndTask(world => well = Spawn(world, "well", 31, 34));
				warehouse.Trait<MedievalBurnable>().Ignite(warehouse, saboteur, warehouse.Location, false); ignited = tick; Next();
			}
			if (phase == 1)
			{
				woke |= people.All(a => !a.Trait<MedievalRest>().Sleeping && a.Trait<MedievalFirefighter>().Busy);
				filled |= people.Any(a => a.Trait<MedievalFirefighter>().Water && MedievalAccess.Adjacent(a, well));
				Check(carrier.Trait<MedievalWorker>().Cargo == 7, "Emergency must preserve carried materials.");
				if (!poured && people.Any(a => a.Trait<MedievalFirefighter>().Phase == 3))
				{
					poured = true; Sync.RunUnsynced(w, () => w.Selection.Combine(w, [farmer], false, false));
					Game.Renderer.SaveScreenshot(Path.Combine(output, "night-water-buckets.png"));
				}
				if (!warehouse.Trait<MedievalBurnable>().Burning)
				{
					quenched = tick - ignited < SabotageRules.BurnTicks && warehouse.Trait<MedievalBurnable>().WaterBuckets >= 2 && guard.Trait<MedievalFirefighter>().Buckets > 0;
					Check(quenched, "Citizens and militia must physically deliver water and extinguish the fire early.");
					Check(warehouse.Trait<Health>().HP > warehouse.Trait<Health>().MaxHP / 2, "Water must stop damage before most of the building is lost."); Next();
				}
			}
			else if (phase == 2 && tick - since > 250)
			{
				resumedSleep = people.All(a => a.Trait<MedievalRest>().Sleeping) && guard.Trait<MedievalRest>().OrderedMilitaryRest;
				Check(resumedSleep, "The inhabitants must return to their beds after the alarm, including ordered military rest.");
				var clock = w.WorldActor.Trait<MedievalDayNight>().Clock; while (clock.Phase != 100) clock.Advance();
				carrier.Trait<MedievalWorker>().Automatic = true;
				tent.Trait<MedievalCampArea>().RestEnabled = false;
				w.IssueOrder(new Order("MedievalPatrolNightOnly", guard, false) { ExtraData = 0 });
				w.IssueOrder(new Order("MedievalStartPatrol", guard, false) { TargetString = PatrolRoute.Encode([Cell(33, 34), Cell(36, 34)]) });
				w.IssueOrder(new Order("MedievalPlaceStockyard", warehouse, Target.FromCell(w, Cell(35, 41)), false) { ExtraLocation = Cell(38, 44) }); Next();
			}
			else if (phase == 3 && carrier.Trait<MedievalWorker>().Cargo == 0 && guard.Trait<MedievalPatrol>().Visits > 0)
			{
				resumedWork = farmer.Trait<MedievalWorker>().Automatic && farmer.Trait<MedievalWorker>().Status != "Secours incendie"
					&& MedievalSettlement.RecruitmentStores(w.LocalPlayer).Sum(a => a.Trait<MedievalStockpile>().Stock.Amount(3)) == 87;
				Check(resumedWork, "Workers must resume and deliver the seven conserved stones exactly once.");
				Check(warehouse.Trait<MedievalStockyard>().HasArea, "External stock area fixture must exist.");
				yardHP = warehouse.Trait<Health>().HP; yardBuckets = warehouse.Trait<MedievalBurnable>().WaterBuckets;
				warehouse.Trait<MedievalBurnable>().Ignite(warehouse, saboteur, Cell(35, 41), true); ignited = tick; Next();
			}
			else if (phase == 4 && !warehouse.Trait<MedievalBurnable>().Burning)
			{
				yardQuenched = warehouse.Trait<MedievalBurnable>().WaterBuckets > yardBuckets && warehouse.Trait<Health>().HP == yardHP && tick - ignited < SabotageRules.BurnTicks;
				Check(yardQuenched, "Responders must extinguish the external area at its actual location without damaging the building.");
				visits = guard.Trait<MedievalPatrol>().Visits; Next();
			}
			else if (phase == 5 && tick - since > 150)
			{
				resumedPatrol = guard.Trait<MedievalPatrol>().HasRoute && guard.Trait<MedievalPatrol>().Visits > visits;
				Check(resumedPatrol, "Militia must retain and resume the same patrol after the fire.");
				w.AddFrameEndTask(world =>
				{
					doomed = Spawn(world, "house", 30, 39); doomed.Trait<MedievalBurnable>().Ignite(doomed, saboteur, doomed.Location, false);
				}); Next();
			}
			else if (phase == 6 && farmer.Trait<MedievalFirefighter>().Busy)
			{
				w.IssueOrder(new Order("Move", farmer, Target.FromCell(w, Cell(28, 35)), false));
				well.Dispose(); Next();
			}
			else if (phase == 7 && tick - since > 150)
			{
				cancelled = !farmer.Trait<MedievalFirefighter>().Busy && farmer.Location == Cell(28, 35);
				Check(cancelled, "A manual retreat must override the automatic alarm and retain its move order.");
				Check(people.All(a => !a.Trait<MedievalFirefighter>().Busy), "Destroying the only well must safely release responders without water.");
				w.AddFrameEndTask(world => well = Spawn(world, "well", 31, 34)); Next();
			}
			else if (phase == 8 && people.Any(a => a.Trait<MedievalFirefighter>().Busy))
			{
				doomed.Dispose(); Next();
			}
			else if (phase == 9 && tick - since > 75)
			{
				destroyed = people.All(a => !a.Trait<MedievalFirefighter>().Busy); Check(destroyed, "Destroyed fire target must release all responders safely.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "after-response.png")); Finish();
			}
			if (tick > 6500) { Check(false, $"Timeout at phase {phase}"); Finish(); }
		}
	}
}
