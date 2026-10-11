using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalMilitaryValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalMilitaryValidation();
	}
	public sealed class MedievalMilitaryValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, stage, waitUntil, soldierIndex, heldMeat, shortageUntil;
		bool finished, builderCarried, smithRaw, smithGear, recruiterGear, recruiterFood, trainedWithKit, cancelIssued;
		Actor depot, forge, forge2, barracks, enemy;
		Health enemyHealth;
		readonly List<string> errors = [];
		readonly HashSet<uint> trained = [];
		readonly HashSet<string> eliteMeatChecks = [];
		readonly HashSet<string> eliteMeatCarried = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "military-preview"); Directory.CreateDirectory(output);
		}
		void Check(bool valid, string message) { if (!valid && !errors.Contains(message)) errors.Add(message); }
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>(id); Check(!b.IsDisabled(), "Disabled button " + id); b.OnClick();
		});
		void Place(World w, int button, CPos cell)
		{
			Button(w, "MD_RECRUIT_" + button);
			Sync.RunUnsynced(w, () =>
			{
				var generator = (OrderGenerator)w.OrderGenerator;
				var orders = generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
				orders = orders.Concat(generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Up, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1))).ToArray();
				Check(orders.Length == 1, "Building button must place a military construction."); foreach (var o in orders) w.IssueOrder(o);
			});
		}
		Actor Spawn(World w, string type, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Setup(World w)
		{
			w.LocalPlayer.Shroud.Disabled = true;
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			depot = Spawn(w, "warehouse", 28, 34);
			var stock = depot.Trait<MedievalStockpile>().Stock;
			stock.Add(0, 400); stock.Add(1, 250); stock.Add(2, 100); stock.Add(3, 300);
			Spawn(w, "market", 33, 36).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "market", 38, 35).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "house", 23, 32);
			for (var i = 0; i < 6; i++) Spawn(w, "builder", 25 + i, 39);
		}
		Actor[] Soldiers(World w) => w.Actors.Where(a => a.Owner == w.LocalPlayer && MedievalEquipment.Soldiers.Contains(a.Info.Name)).ToArray();
		bool StockReady(World w) => Enumerable.Range(0, 3).All(r => MedievalEquipment.Amount(w.LocalPlayer, r) == 1 && MedievalEquipment.Committed(w.LocalPlayer, r) == 1);
		void Finish(World w)
		{
			Check(stage == 9, "Military supply chain must finish every stage.");
			Check(builderCarried && smithRaw && smithGear && recruiterGear && recruiterFood && trainedWithKit, "All materials and equipment must physically travel before training.");
			Check(trained.Count == MedievalEquipment.Soldiers.Length, "Every military unit must be recruited.");
			Check(eliteMeatChecks.Count == 3 && eliteMeatCarried.Count == 3, "All three elite units must wait for meat and resume after its physical delivery.");
			Check(enemyHealth != null && enemyHealth.HP < enemyHealth.MaxHP, "Archer must deal ranged damage.");
			Check(MedievalSettlement.Population(w.LocalPlayer) == 16, "Six builders, three artisans, six fighters and one drummer must occupy 16 houses slots.");
			Check(MedievalSettlement.PendingPopulation(w.LocalPlayer) == 0, "Cancelled training must release its housing reservation.");
			Check(MedievalFoodEconomy.Demand(w.LocalPlayer) == 32, "Every artisan, soldier and drummer must consume food.");
			var food = w.Actors.Where(a => a.Owner == w.LocalPlayer && a.IsInWorld && !a.IsDead).Sum(a =>
				(a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(1) ?? 0)
				+ (a.TraitOrDefault<MedievalMilitaryWorkshop>()?.Inputs.Amount(1) ?? 0)
				+ (a.TraitOrDefault<MedievalWorker>() is { EquipmentCargo: false, Resource: 1 } worker ? worker.Cargo : 0));
			var eaten = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
			Check(eaten > 0 && food + eaten == 425, "Food must be conserved after 30 artisan and 195 military recruitment provisions and real meals.");
			Check(depot.Trait<MedievalStockpile>().Stock.Amount(2) == 55, "Elite soldiers must consume exactly 10, 15 and 20 meat; basic soldiers none.");
			Check(depot.Trait<MedievalStockpile>().Stock.Amount(0) == 162 && depot.Trait<MedievalStockpile>().Stock.Amount(3) == 170, "Construction and eleven recipes must spend exactly 238 wood and 130 stone.");
			Check(StockReady(w), "Two forges must refill each target exactly, and cancellation must return carried equipment.");
			foreach (var store in MedievalEquipment.Stores(w.LocalPlayer))
				Check(Enumerable.Range(0, 3).All(r => store.Trait<MedievalEquipmentStore>().Stock.Reserved(r) == 0 && store.Trait<MedievalEquipmentStore>().Stock.Incoming(r) == 0), "No leaked equipment reservation after cancellation.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, errors, trained = trained.Count,
				builderCarried, smithRaw, smithGear, recruiterGear, recruiterFood, trainedWithKit, cancelIssued, eliteMeatChecks, eliteMeatCarried,
				meat = depot.Trait<MedievalStockpile>().Stock.Amount(2),
				wood = depot.Trait<MedievalStockpile>().Stock.Amount(0), stone = depot.Trait<MedievalStockpile>().Stock.Amount(3),
				gear = Enumerable.Range(0, 3).Select(r => MedievalEquipment.Amount(w.LocalPlayer, r)).ToArray(), population = MedievalSettlement.Population(w.LocalPlayer), food, eaten,
				citizens = MedievalFoodEconomy.Citizens(w.LocalPlayer).GroupBy(a => a.Info.Name).Select(g => new { type = g.Key, count = g.Count() }).ToArray()
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL MILITARY: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(31, 31)));
			if (tick == 20)
			{
				// Direct legacy recruitment must not bypass the barracks.
				w.IssueOrder(new Order("MedievalRecruit", w.LocalPlayer.PlayerActor, false) { TargetString = "md.manatarms" });
				Place(w, 6, Cell(29, 29)); Place(w, 7, Cell(35, 29));
				foreach (var type in MedievalEquipment.Soldiers)
				{
					var cost = MedievalSettlement.Cost(w.LocalPlayer, type);
					Check(cost.Elite ? cost.Meat > 0 : cost.Meat == 0, "Meat must be mandatory for elites only: " + type);
				}
			}
			if (tick == 50)
			{
				forge = w.Actors.First(a => a.Info.Name == "md.forge"); barracks = w.Actors.First(a => a.Info.Name == "md.barracks");
				Check(Soldiers(w).Length == 0 && !w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().Recruiting, "No military recruitment outside a staffed barracks.");
			}
			foreach (var pair in w.ActorsWithTrait<MedievalWorker>().Where(t => t.Actor.Owner == w.LocalPlayer))
			{
				var worker = pair.Trait; if (worker.Cargo == 0) continue;
				builderCarried |= worker.Info.Profession == "builder";
				smithRaw |= worker.Info.Profession == "blacksmith" && !worker.EquipmentCargo;
				smithGear |= worker.Info.Profession == "blacksmith" && worker.EquipmentCargo;
				recruiterGear |= worker.Info.Profession == "recruiter" && worker.EquipmentCargo;
				recruiterFood |= worker.Info.Profession == "recruiter" && !worker.EquipmentCargo && worker.Resource == 1;
				if (worker.Info.Profession == "recruiter" && !worker.EquipmentCargo && worker.Resource == 2 && barracks?.Trait<MedievalMilitaryWorkshop>().Recruit is >= 0)
					eliteMeatCarried.Add(MedievalEquipment.Soldiers[barracks.Trait<MedievalMilitaryWorkshop>().Recruit]);
			}
			if (barracks != null && barracks.Trait<MedievalMilitaryWorkshop>().Progress > 0)
			{
				trainedWithKit = true;
				Check(barracks.Trait<MedievalMilitaryWorkshop>().Ready(barracks), "Training cannot advance before all equipment and food arrive.");
			}
			if (stage == 0 && forge != null && MedievalStockpile.Active(forge) && MedievalStockpile.Active(barracks))
			{
				Select(w, forge); Button(w, "MD_FORGE_STAFF");
				for (var r = 0; r < 3; r++) Button(w, "MD_FORGE_PLUS_" + r);
				Select(w, barracks); Button(w, "MD_BARRACKS_STAFF");
				stage = 1;
			}
			if (stage == 1 && StockReady(w))
			{
				Check(depot.Trait<MedievalStockpile>().Stock.Amount(0) == 204 && depot.Trait<MedievalStockpile>().Stock.Amount(3) == 222, "First three items must consume their wood and stone recipes.");
				w.AddFrameEndTask(world => forge2 = Spawn(world, "forge", 23, 27)); stage = 2;
			}
			if (stage == 2 && forge2 != null)
			{
				Select(w, forge2); Button(w, "MD_FORGE_STAFF"); stage = 3; waitUntil = tick + 180;
			}
			if (stage == 3 && tick >= waitUntil)
			{
				if (soldierIndex == 0) Check(StockReady(w), "A second forge must respect the existing stock targets.");
				if (MedievalSettlement.Cost(w.LocalPlayer, MedievalEquipment.Soldiers[soldierIndex]).Elite)
				{
					var meat = depot.Trait<MedievalStockpile>().Stock;
					heldMeat = meat.Take(2, meat.Available(2)); shortageUntil = 0;
				}
				Select(w, barracks); Button(w, "MD_BARRACKS_RECRUIT_" + soldierIndex); stage = 4;
			}
			if (stage == 4)
			{
				if (heldMeat > 0)
				{
					var workshop = barracks.Trait<MedievalMilitaryWorkshop>();
					var equipment = barracks.Trait<MedievalEquipmentStore>().Stock;
					var cost = MedievalSettlement.Cost(w.LocalPlayer, MedievalEquipment.Soldiers[soldierIndex]);
					Check(workshop.Progress == 0 && Soldiers(w).Length == soldierIndex, "Elite training must not advance without meat.");
					if (workshop.Inputs.Amount(1) == cost.Food && Enumerable.Range(0, 3).All(r => equipment.Amount(r) == MedievalEquipment.Kits[soldierIndex][r]))
					{
						if (shortageUntil == 0) shortageUntil = tick + 175;
						if (tick == shortageUntil - 50) Game.Renderer.SaveScreenshot(Path.Combine(output, "elite-waiting-for-meat.png"));
						if (tick >= shortageUntil)
						{
							Check(!workshop.Ready(barracks) && workshop.Inputs.Amount(2) == 0, "Food and equipment alone must not start elite training.");
							Check(MedievalSettlement.PendingPopulation(w.LocalPlayer) == 1, "Waiting elite must reserve its housing slot.");
							eliteMeatChecks.Add(MedievalEquipment.Soldiers[soldierIndex]);
							depot.Trait<MedievalStockpile>().Stock.Add(2, heldMeat); heldMeat = 0;
						}
					}
				}
				foreach (var soldier in Soldiers(w).Where(a => !trained.Contains(a.ActorID)).ToArray())
				{
					trained.Add(soldier.ActorID);
					Check(soldier.TraitOrDefault<MedievalNeedsFood>() != null, "Combat unit must have a food clock.");
					if (soldier.Info.Name == "md.archer") w.AddFrameEndTask(world =>
					{
						var foe = world.Players.First(p => p.InternalName == "Creeps");
						world.LocalPlayer.EnemyPlayersMask = foe.PlayerMask; foe.EnemyPlayersMask = world.LocalPlayer.PlayerMask;
						enemy = world.CreateActor("md.militarytarget", [new OwnerInit(foe), new LocationInit(Cell(41, 30))]); enemyHealth = enemy.Trait<Health>();
						world.IssueOrder(new Order("Attack", soldier, Target.FromActor(enemy), false));
					});
					soldierIndex++; stage = soldierIndex < MedievalEquipment.Soldiers.Length ? 3 : 5; waitUntil = tick + 25;
				}
			}
			if (stage == 5 && StockReady(w) && Enumerable.Range(0, 3).All(r => MedievalEquipment.Workshops(w.LocalPlayer).All(a => a.Trait<MedievalMilitaryWorkshop>().Product < 0)))
			{
				Select(w, forge);
				for (var r = 0; r < 3; r++) Button(w, "MD_FORGE_MINUS_" + r);
				stage = 6; waitUntil = tick + 80;
			}
			if (stage == 6 && tick == waitUntil - 20) Game.Renderer.SaveScreenshot(Path.Combine(output, "forge.png"));
			if (stage == 6 && tick >= waitUntil)
			{
				Select(w, barracks); Button(w, "MD_BARRACKS_RECRUIT_3"); stage = 7;
			}
			if (stage == 7 && barracks.Trait<MedievalMilitaryWorkshop>().Staff(barracks)?.Trait<MedievalWorker>() is { Cargo: > 0, EquipmentCargo: true })
			{
				Check(MedievalSettlement.PendingPopulation(w.LocalPlayer) == 1, "A pending soldier must reserve one housing slot.");
				Button(w, "MD_BARRACKS_CANCEL"); cancelIssued = true; stage = 8;
			}
			if (stage == 8 && StockReady(w) && barracks.Trait<MedievalMilitaryWorkshop>().Staff(barracks)?.Trait<MedievalWorker>().Cargo == 0)
			{
				stage = 9; waitUntil = tick + 120; renderer.Viewport.AdjustZoom((float)Math.Log(1.5f / renderer.Viewport.Zoom));
			}
			if (stage == 9 && tick == waitUntil - 40) Game.Renderer.SaveScreenshot(Path.Combine(output, "barracks.png"));
			if (tick % 1000 == 0)
				File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { tick, stage, soldierIndex, errors,
					workshops = MedievalEquipment.Workshops(w.LocalPlayer).Select(a => new { a.Info.Name, a.Trait<MedievalMilitaryWorkshop>().Status, a.Trait<MedievalMilitaryWorkshop>().Product, a.Trait<MedievalMilitaryWorkshop>().Recruit,
						worker = a.Trait<MedievalMilitaryWorkshop>().Staff(a)?.Trait<MedievalWorker>().Description,
						position = a.Trait<MedievalMilitaryWorkshop>().Staff(a)?.Location.ToString(),
						rest = a.Trait<MedievalMilitaryWorkshop>().Staff(a)?.TraitOrDefault<MedievalRest>()?.Description,
						idle = a.Trait<MedievalMilitaryWorkshop>().Staff(a)?.IsIdle,
						inputs = Enumerable.Range(0, 4).Select(a.Trait<MedievalMilitaryWorkshop>().Inputs.Amount).ToArray() }).ToArray()
				}, new JsonSerializerOptions { WriteIndented = true }));
			if (stage == 9 && tick >= waitUntil || tick == 30000) Finish(w);
		}
	}
}
