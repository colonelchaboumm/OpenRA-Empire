using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalYardHandlingValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalYardHandlingValidation();
	}
	// Opt-in real pathfinding, production, orders, interruptions and rendered gestures.
	public sealed class MedievalYardHandlingValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor warehouse, mill, hauler, builder, site;
		string output;
		int tick, stage, until, lastWood, lastStone, stoppedWood, deliveries, pickups;
		bool done, moved, splitDrop, splitPickup;
		int extraWood;
		readonly List<Actor> extraHaulers = [];
		readonly HashSet<uint> concurrentDeliverers = [];
		readonly List<string> errors = [];
		readonly List<object> transfers = [];
		readonly HashSet<string> shots = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) { errors.Add(error); Console.WriteLine("HANDLING CHECK: " + error); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "yard-handling", "native"); Directory.CreateDirectory(output);
		}
		Actor Spawn(World w, string name, int x, int y, bool construction = false)
		{
			var init = new TypeDictionary { new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y)) };
			if (construction) init.Add(new MedievalConstructionInit());
			return w.CreateActor("md." + name, init);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			w.LocalPlayer.Shroud.Disabled = true;
			warehouse = Spawn(w, "warehouse", 26, 29); mill = Spawn(w, "sawmill", 18, 29);
			Spawn(w, "house", 32, 38);
			Sync.RunUnsynced(w, () => { w.Selection.Combine(w, [warehouse], false, true); renderer.Viewport.AdjustZoom((float)Math.Log(2f / renderer.Viewport.Zoom)); });
		}
		void Zone(World w, StorageKind kind, int x, int y, int id = 0) => w.IssueOrder(new Order("MedievalPlaceStockyard", warehouse, Target.FromCell(w, Cell(x, y)), false)
			{ TargetString = kind.ToString(), ExtraData = (uint)id, ExtraLocation = Cell(x + 1, y + 1) });
		void Shot(string name)
		{
			if (shots.Add(name)) Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		}
		void Observe(World w)
		{
			var stock = warehouse.Trait<MedievalStockpile>().Stock;
			var yard = warehouse.Trait<MedievalStockyard>();
			var workers = new[] { hauler, builder }.Concat(extraHaulers).Where(a => a != null).ToArray();
			foreach (var a in workers)
			{
				var worker = a.Trait<MedievalWorker>();
				if (!worker.HandlingStock || worker.HandlingZone == 0) continue;
				// Relocation can invalidate the old contact for one tick, but never commit it.
				if (worker.HandlingCommitted) Check(StorageHandling.Faces(worker.HandlingCell).Contains(a.Location), "A committed exterior transfer requires a worker alongside its exact pile.");
				if (worker.HandlingGood == 0 && worker.HandlingPose is 2 or 4 or 6)
					Shot((worker.HandlingPickup ? "pickup-" : "deposit-") + worker.HandlingPose);
			}
			foreach (var r in new[] { 0, 3 })
			{
				var previous = r == 0 ? lastWood : lastStone; var change = stock.Amount(r) - previous;
				if (change != 0)
				{
					var actors = workers.Where(a => a.Trait<MedievalWorker>() is { HandlingStock: true, HandlingCommitted: true } t && t.Resource == r && t.HandlingPickup == (change < 0)
						&& t.HandlingElapsed == t.HandlingDuration / 2).ToArray();
					Check(actors.Length > 0, "Every inventory change must occur during a committed gesture, never while walking.");
					Check(actors.Sum(a => a.Trait<MedievalWorker>().HandlingAmount) == Math.Abs(change), "Concurrent gestures change stock exactly once per actual load.");
					foreach (var actor in actors)
					{
						var worker = actor.Trait<MedievalWorker>();
						Check(worker.HandlingZone > 0 && yard.At(worker.HandlingCell)?.Id == worker.HandlingZone, "Materials must be touched in the currently placed exterior reserve.");
						if (change > 0) { deliveries++; splitDrop |= change == 3; } else pickups++;
						if (stage == 7 && change > 0) concurrentDeliverers.Add(actor.ActorID);
						transfers.Add(new { tick, unit = actor.Info.Name, resource = r, change = worker.HandlingAmount * Math.Sign(change), cell = worker.HandlingCell.ToString(), cargo = worker.Cargo });
					}
				}
				var total = stock.Amount(r) + mill.Trait<MedievalStockpile>().Stock.Amount(r)
					+ workers.Sum(a => a.Trait<MedievalWorker>().Resource == r ? a.Trait<MedievalWorker>().Cargo : 0)
					+ (site?.Trait<MedievalBuilding>().Materials.Amount(r) ?? 0);
				Check(total == (r == 0 ? 132 + extraWood : 20), "Wood and stone must be conserved through travel, partial transfers and cancelled orders.");
				var displayed = yard.Inside(warehouse, r) + yard.Zones.Sum(z => z.Stacks(g => yard.Amount(warehouse, z, g)).Where(s => s.Good == r).Sum(s => s.Amount));
				Check(displayed == stock.Amount(r), "Displayed piles must equal the real inventory at every tick.");
			}
			lastWood = stock.Amount(0); lastStone = stock.Amount(3);
		}
		void Finish()
		{
			Check(stage == 8 && site.Trait<MedievalBuilding>().Complete, "Builder must finish the real house after both interruptions.");
			Check(moved && splitDrop && splitPickup && deliveries >= 7 && pickups == 10 && concurrentDeliverers.Count == 3, "Relocation, split transfers and all three simultaneous transporters must complete.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, stage, moved, splitDrop, splitPickup, deliveries, pickups, concurrentTransporters = concurrentDeliverers.Count, transfers }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL YARD HANDLING: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(24, 31))));
			var stock = warehouse.Trait<MedievalStockpile>().Stock; var yard = warehouse.Trait<MedievalStockyard>();
			if (tick % 500 == 0) Console.WriteLine($"HANDLING stage {stage}, tick {tick}, stock {stock.Amount(0)}/{stock.Amount(3)}, builder {builder?.Trait<MedievalWorker>().Description}, hauler {hauler?.Trait<MedievalWorker>().Description}");
			if (tick == 10) Zone(w, StorageKind.Wood, 22, 29);
			if (tick == 20) Zone(w, StorageKind.Stone, 24, 25);
			if (tick == 30)
			{
				Check(yard.Zones.Count == 2, "Both exterior zones must be created by real orders.");
				stock.Add(0, 97); stock.Add(3, 20); mill.Trait<MedievalStockpile>().Stock.Add(0, 35); lastWood = 97; lastStone = 20;
				w.AddFrameEndTask(_ => hauler = Spawn(w, "hauler", 18, 32)); stage = 1;
			}
			if (tick <= 30) return;
			Observe(w);
			if (stage == 1 && !moved && hauler.Trait<MedievalWorker>() is { HandlingStock: true, HandlingPickup: false, HandlingCommitted: false, HandlingZone: > 0 } moving && moving.HandlingElapsed >= 2)
			{ Zone(w, StorageKind.Wood, 22, 32, yard.Zones[0].Id); moved = true; }
			if (stage == 1 && stock.Amount(0) == 132 && !hauler.Trait<MedievalWorker>().HandlingStock)
			{
				w.IssueOrder(new Order("Stop", hauler, false));
				w.AddFrameEndTask(_ => { site = Spawn(w, "house", 30, 29, true); builder = Spawn(w, "builder", 25, 34); }); stage = 2;
			}
			if (stage == 2 && builder != null && builder.Trait<MedievalWorker>() is { HandlingStock: true, HandlingPickup: true, HandlingCommitted: false } early && early.HandlingElapsed >= 2)
			{ stoppedWood = stock.Amount(0); w.IssueOrder(new Order("Stop", builder, false)); stage = 3; until = tick + 25; }
			if (stage == 3 && tick >= until)
			{
				Check(stock.Amount(0) == stoppedWood && builder.Trait<MedievalWorker>().Cargo == 0 && stock.Reserved(0) == 0 && site.Trait<MedievalBuilding>().Materials.Incoming(0) == 0,
					"Stopping before hand contact cancels both reservations without moving goods.");
				w.IssueOrder(new Order("MedievalWork", builder, false)); stage = 4;
			}
			if (stage == 4 && builder.Trait<MedievalWorker>() is { HandlingStock: true, HandlingPickup: true, HandlingCommitted: true, HandlingAmount: 2, Cargo: 2 })
			{ splitPickup = true; w.IssueOrder(new Order("Stop", builder, false)); stage = 5; until = tick + 25; }
			if (stage == 5 && tick >= until)
			{
				Check(stock.Amount(0) == 100 && builder.Trait<MedievalWorker>().Cargo == 2 && stock.Reserved(0) == 0 && site.Trait<MedievalBuilding>().Materials.Incoming(0) == 0,
					"Stopping after a partial pickup preserves the carried two logs and releases both remaining reservations.");
				w.IssueOrder(new Order("MedievalWork", builder, false)); stage = 6;
			}
			if (stage == 6 && site.Trait<MedievalBuilding>().Complete)
			{
				Check(stock.Amount(0) == 72 && stock.Amount(3) == 0, "Finished house consumes exactly 60 wood and 20 stone.");
				Check(deliveries == 4 && pickups == 10, "Three hauler deposits, one returned load and ten builder pickups must finish the house.");
				Shot("completed-house"); extraWood = 60; mill.Trait<MedievalStockpile>().Stock.Add(0, extraWood);
				w.IssueOrder(new Order("MedievalWork", hauler, false));
				w.AddFrameEndTask(_ => { extraHaulers.Add(Spawn(w, "hauler", 18, 32)); extraHaulers.Add(Spawn(w, "hauler", 19, 32)); }); stage = 7;
			}
			if (stage == 7 && stock.Amount(0) == 132 && extraHaulers.Append(hauler).All(a => !a.Trait<MedievalWorker>().HandlingStock)) { stage = 8; Shot("concurrent-transports"); Finish(); }
			else if (tick >= 6500) Finish();
		}
	}
}
