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
	public class MedievalWorkerAssignmentValidationInfo : TraitInfo
	{
		public readonly bool Network = false;
		public override object Create(ActorInitializer init) => new MedievalWorkerAssignmentValidation(Network);
	}
	// Same physical scenario in a native window and in two independent TCP clients.
	public sealed class MedievalWorkerAssignmentValidation : IWorldLoaded, ITick
	{
		readonly bool network;
		readonly List<string> errors = [], checks = [];
		readonly List<object> checkpoints = [];
		readonly List<Actor> workers = [];
		Actor builder, builder2, warehouse, site, otherSite, farm, nearFarm, field, farmer, farmer2, quarry, nearQuarry, stone,
			mine, nearMine, miner, ranch, fold, cowPen, sheepPen, breeder, shepherd, foreignFarm, placed;
		Player primary, second;
		WorldRenderer renderer;
		string output;
		int tick;
		bool done, cattleLed, sheepLed, harvested, builderCarried;
		public MedievalWorkerAssignmentValidation(bool network) { this.network = network; }
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string text)
		{ if (!checks.Contains(text)) checks.Add(text); if (!ok && !errors.Contains(text)) { errors.Add(text); Console.WriteLine("ASSIGNMENT FAIL: " + text); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = network ? Platform.SupportDir : Path.Combine(Platform.EngineDir, "analysis/medieval/worker-assignment");
			Directory.CreateDirectory(output);
			var players = w.Players.Where(p => p.PlayerReference.Playable).OrderBy(p => p.InternalName).ToArray();
			primary = network ? players[0] : w.LocalPlayer; second = network ? players[1] : primary;
		}
		void Send(World w, string id, Actor actor, Actor target = null)
		{ if (actor.Owner == w.LocalPlayer) w.IssueOrder(new Order(id, actor, target == null ? Target.Invalid : Target.FromActor(target), false)); }
		void Assign(World w, Actor actor, Actor target) => Send(w, "MedievalAssignWorkplace", actor, target);
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			primary.Shroud.Disabled = second.Shroud.Disabled = true;
			Actor Spawn(string name, int x, int y, Player owner = null, bool construction = false)
			{
				var a = construction ? w.CreateActor("md." + name, [new OwnerInit(owner ?? primary), new LocationInit(Cell(x, y)), new MedievalConstructionInit()])
					: w.CreateActor("md." + name, [new OwnerInit(owner ?? primary), new LocationInit(Cell(x, y))]);
				if (a.TraitOrDefault<MedievalWorker>() is { } worker) { worker.Automatic = false; workers.Add(a); }
				return a;
			}
			warehouse = Spawn("warehouse", 20, 22); warehouse.Trait<MedievalStockpile>().Stock.Add(0, 300); warehouse.Trait<MedievalStockpile>().Stock.Add(3, 200);
			builder = Spawn("builder", 24, 24); builder2 = Spawn("builder", 25, 24);
			site = Spawn("house", 28, 22, construction: true); otherSite = Spawn("house", 22, 26, construction: true);
			nearFarm = Spawn("farm", 20, 34); Spawn("field", 20, 37); farm = Spawn("farm", 30, 34); field = Spawn("field", 30, 37);
			farmer = Spawn("farmer", 22, 35); farmer2 = Spawn("farmer", 23, 35);
			nearQuarry = Spawn("quarry", 34, 22, second); quarry = Spawn("quarry", 40, 22, second); stone = Spawn("stonecutter", 35, 24, second);
			Spawn("stone", 43, 26, w.Players.First(p => p.InternalName == "Neutral"));
			nearMine = Spawn("goldmine", 38, 32, second); mine = Spawn("goldmine", 44, 32, second); miner = Spawn("goldminer", 39, 34, second);
			Spawn("gold", 47, 36, w.Players.First(p => p.InternalName == "Neutral"));
			ranch = Spawn("ranch", 20, 45); cowPen = Spawn("pasture.4x4", 20, 49); breeder = Spawn("breeder", 18, 46); Spawn("cow", 23, 45);
			fold = Spawn("sheepfold", 30, 45, second); sheepPen = Spawn("sheeppasture.4x4", 30, 49, second); shepherd = Spawn("shepherd", 29, 46, second); Spawn("sheep", 33, 45, second);
			foreignFarm = Spawn("farm", 45, 42, network ? second : w.Players.First(p => p.InternalName == "Rival"));
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 20)
			{
				Assign(w, builder, site); Assign(w, builder2, site); Assign(w, farmer, farm); Assign(w, farmer2, farm);
				Assign(w, stone, quarry); Assign(w, miner, mine); Assign(w, breeder, ranch); Assign(w, shepherd, fold);
			}
			if (tick > 40 && tick < 2300)
			{
				cattleLed |= breeder.Trait<MedievalHerding>().Leading; sheepLed |= shepherd.Trait<MedievalHerding>().Leading;
				harvested |= farmer.Trait<MedievalWorker>().IsHarvesting || farmer2.Trait<MedievalWorker>().IsHarvesting;
				builderCarried |= builder.Trait<MedievalWorker>().Cargo > 0 || builder2.Trait<MedievalWorker>().Cargo > 0;
				Check(field.Trait<MedievalAgriculturalArea>().Workers(field) <= field.Trait<MedievalAgriculturalArea>().Capacity, "Group assignment respects field capacity");
				Check(workers.Where(a => a == farmer || a == farmer2).All(a => a.Trait<MedievalWorker>().WorkArea == null || a.Trait<MedievalWorker>().WorkArea == field), "Farmers use the selected farm's fields despite a closer farm");
			}
			if (tick == 70)
			{
				foreach (var pair in new[] { (builder, site), (builder2, site), (farmer, farm), (farmer2, farm), (stone, quarry), (miner, mine), (breeder, ranch), (shepherd, fold) })
					Check(pair.Item1.Trait<MedievalWorker>().AssignedWorkplace == pair.Item2, "Assignment accepted: " + pair.Item1.Info.Name);
				Check(otherSite.Trait<MedievalBuilding>().Materials.Incoming(0) == 0 && otherSite.Trait<MedievalBuilding>().Progress == 0, "Builders ignore the closer unselected site");
				Sync.RunUnsynced(w, () =>
				{
					var targeter = ((IIssueOrder)farmer.Trait<MedievalWorker>()).Orders.Single();
					var modifiers = TargetModifiers.None; var cursor = "";
					Check(targeter.CanTarget(farmer, Target.FromActor(farm), ref modifiers, ref cursor), "Right-click exposes the worker assignment order");
					modifiers = TargetModifiers.ForceMove;
					Check(!targeter.CanTarget(farmer, Target.FromActor(farm), ref modifiers, ref cursor), "Forced movement remains available");
				});
				Assign(w, farmer, foreignFarm); Assign(w, farmer, quarry); Send(w, "MedievalAssignWorkplace", farmer);
			}
			if (tick == 120) Check(farmer.Trait<MedievalWorker>().AssignedWorkplace == farm, "Invalid, missing and foreign targets preserve the current assignment");
			if (tick == 900 && !network)
			{
				Sync.RunUnsynced(w, () => { MedievalAuditUi.OpenContext(w, farmer, "Travail"); renderer.Viewport.Center(farmer.CenterPosition); });
			}
			if (tick == 910 && !network) Game.Renderer.SaveScreenshot(Path.Combine(output, "assignment.png"));
			if (tick == 2300)
			{
				Check(builderCarried && site.Trait<MedievalBuilding>().Complete, "Assigned builders physically supply and finish construction");
				Check(builder.Trait<MedievalWorker>().AssignedWorkplace == null && builder2.Trait<MedievalWorker>().AssignedWorkplace == null, "Completed construction releases both builders");
				Check(harvested && farm.Trait<MedievalStockpile>().Stock.Amount(1) > 0 && nearFarm.Trait<MedievalStockpile>().Stock.Amount(1) == 0, "Harvest delivered to the chosen farm");
				Check(quarry.Trait<MedievalStockpile>().Stock.Amount(3) > 0 && nearQuarry.Trait<MedievalStockpile>().Stock.Amount(3) == 0, "Stone delivered to the chosen quarry");
				Check(mine.Trait<MedievalStockpile>().Stock.Amount(4) > 0 && nearMine.Trait<MedievalStockpile>().Stock.Amount(4) == 0, "Gold delivered to the chosen mine");
				Check(cattleLed && cowPen.Trait<MedievalLivestock>().Count > 0, "Breeder leads an animal into the assigned ranch's pen");
				Check(sheepLed && sheepPen.Trait<MedievalLivestock>().Count > 0, "Shepherd leads an animal into the assigned sheepfold's pen");
				Send(w, "Stop", farmer); Send(w, "Stop", farmer2); Send(w, "Stop", builder); Send(w, "Stop", builder2);
			}
			if (tick == 2350)
			{
				Check(farmer.Trait<MedievalWorker>().AssignedWorkplace == null && farmer.Trait<MedievalWorker>().WorkArea == null && !farmer.Trait<MedievalWorker>().Automatic, "Stop clears assignment and frees the field place");
				// Retarget a physical load; cancellation must never discard it.
				var worker = builder.Trait<MedievalWorker>(); worker.Cargo = 7; worker.Resource = 0;
				Assign(w, farmer, field);
				if (primary == w.LocalPlayer)
				{
					Order placement = null;
					Sync.RunUnsynced(w, () =>
					{
						w.Selection.Combine(w, [builder, builder2, stone], false, false);
						var generator = new MedievalPlaceOrderGenerator(w, renderer, "md.house");
						placement = generator.Order(w, Cell(26, 29), int2.Zero,
							new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).SingleOrDefault();
						Check(placement != null && placement.ExtraActors.SequenceEqual(new[] { builder, builder2 }), "Construction tool serializes only the selected owned builders");
					});
					if (placement != null) { placement.ExtraActors = [builder, builder2, stone, builder]; w.IssueOrder(placement); }
				}
			}
			if (tick == 2400)
			{
				placed = w.Actors.FirstOrDefault(a => a.Info.Name == "md.house" && a.Location == Cell(26, 29));
				Check(placed != null && builder.Trait<MedievalWorker>().AssignedWorkplace == placed && builder2.Trait<MedievalWorker>().AssignedWorkplace == placed, "Placing from builder selection assigns both builders through serialized ExtraActors");
				Check(stone.Trait<MedievalWorker>().AssignedWorkplace == quarry, "Placement ignores foreign or incompatible extra actors");
				Check(farmer.Trait<MedievalWorker>().AssignedWorkplace == field, "A field can be selected directly");
				Check(builder.Trait<MedievalWorker>().Cargo == 7, "Retargeting retains the carried load before delivery");
			}
			if (tick == 3300)
			{
				Send(w, "MedievalWork", farmer); Send(w, "Stop", builder); Send(w, "Stop", builder2);
				farm.Dispose(); quarry.Dispose(); fold.Dispose();
			}
			if (tick == 3350)
			{
				Check(farmer.Trait<MedievalWorker>().AssignedWorkplace == null && farmer.Trait<MedievalWorker>().Automatic, "Resume restores automatic work");
				Check(stone.Trait<MedievalWorker>().AssignedWorkplace == null && shepherd.Trait<MedievalWorker>().AssignedWorkplace == null, "Destroyed workplaces release their workers");
				Check(warehouse.Trait<MedievalStockpile>().Stock.Reserved(0) == 0 && (placed?.Trait<MedievalBuilding>().Materials.Incoming(0) ?? 0) == 0, "Interrupted building deliveries release reservations");
				Check(w.ActorsWithTrait<MedievalStockpile>().Where(p => p.Actor.Owner == primary).Sum(p => p.Trait.Stock.Amount(0))
					+ w.ActorsWithTrait<MedievalBuilding>().Where(p => p.Actor.Owner == primary).Sum(p => p.Trait.Materials.Amount(0))
					+ workers.Where(a => a.Owner == primary).Sum(a => a.Trait<MedievalWorker>() is { Resource: 0, CampCargo: false } worker ? worker.Cargo : 0) == 307, "Construction and retargeted cargo conserve all 307 wood");
			}
			if (tick % 100 == 0) checkpoints.Add(new { tick, hash = w.SyncHash(), assignments = workers.Select(a => a.Trait<MedievalWorker>().AssignedWorkplaceId).ToArray() });
			if (tick != 3600) return;
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, checks, checkpoints, tick }, new JsonSerializerOptions { WriteIndented = true }));
			done = true; Console.WriteLine("WORKER ASSIGNMENT: " + (errors.Count == 0 ? "PASS" : "FAIL")); Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
