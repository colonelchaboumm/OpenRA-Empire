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
	public class MedievalCommanderValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCommanderValidation();
	}
	// Opt-in engine scenario. Exercise serialized orders, actual carrying, UI and shared beds.
	public sealed class MedievalCommanderValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, stage, until;
		bool finished, meatCarried, kitCarried, builtAnimation, reservedCancelled, carriedCancelled, contested;
		Actor depot, barracks, recruiter, cart, commander, second, third, outsider, enemy, citizen;
		readonly List<Actor> troops = [];
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		Actor Spawn(World w, string type, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Order(Actor a, string order) => a.World.IssueOrder(new Order(order, a, false));
		void Move(Actor a, int x, int y) => a.World.IssueOrder(new Order("Move", a, Target.FromCell(a.World, Cell(x, y)), false));
		void Deploy(Actor a, int x, int y) => a.World.IssueOrder(new Order("MedievalCommanderDeploy", a, Target.FromCell(a.World, Cell(x, y)), false));
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>(id); Check(!b.IsDisabled(), "Disabled UI button: " + id); if (!b.IsDisabled()) b.OnClick();
		});
		Actor[] Tents(World w) => w.ActorsHavingTrait<MedievalCampArea>().Where(MedievalStockpile.Active).ToArray();
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "commander"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			depot = Spawn(w, "warehouse", 22, 23); depot.Trait<MedievalStockpile>().Stock.Add(1, 200);
			depot.Trait<MedievalEquipmentStore>().Stock.Add(1, 1); depot.Trait<MedievalEquipmentStore>().Stock.Add(2, 1);
			Spawn(w, "market", 22, 28).Trait<MedievalStockpile>().Stock.Add(1, 200);
			for (var i = 0; i < 3; i++) Spawn(w, "house", 24 + 3 * i, 20);
			barracks = Spawn(w, "barracks", 28, 23); recruiter = Spawn(w, "recruiter", 27, 25);
			barracks.Trait<MedievalMilitaryWorkshop>().AssignStaff(barracks, recruiter);
			cart = Spawn(w, "supplycart", 33, 30); cart.Trait<MedievalCampStore>().Stock.Add(1, 2);
			cart.Trait<MedievalStockpile>().Stock.Add(1, 80);
		}
		void Finish(World w)
		{
			Check(stage == 14, "Commander scenario must complete every stage.");
			Check(meatCarried && kitCarried && builtAnimation && reservedCancelled && carriedCancelled && contested, "Recruitment, work and all cargo interruption checks must run.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, stage, tick,
				meatCarried, kitCarried, builtAnimation, reservedCancelled, carriedCancelled, contested,
				tents = Tents(w).Length, cartTents = cart.Trait<MedievalCampStore>().Stock.Amount(1),
				commander = commander?.Trait<MedievalCommander>().Status,
				workshop = barracks.Trait<MedievalMilitaryWorkshop>().Status,
				worker = recruiter.Trait<MedievalWorker>().Description
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL COMMANDER: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			if (cart == null) return;
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(stage < 2 ? 28 : 35, stage < 2 ? 27 : 33))));
			var workshop = barracks.Trait<MedievalMilitaryWorkshop>(); var worker = recruiter.Trait<MedievalWorker>(); var stock = cart.Trait<MedievalCampStore>().Stock;
			meatCarried |= worker.Cargo > 0 && !worker.EquipmentCargo && worker.Resource == 2;
			kitCarried |= worker.Cargo > 0 && worker.EquipmentCargo;
			builtAnimation |= commander?.Trait<MedievalCommander>().Building == true;
			if (tick == 20)
			{
				Select(w, barracks); Button(w, "MD_BARRACKS_COMMANDER");
				w.IssueOrder(new Order("MedievalDeployTent", cart, Target.FromCell(w, Cell(32, 33)), false));
			}
			if (stage == 0 && workshop.Inputs.Amount(1) == 40 && barracks.Trait<MedievalEquipmentStore>().Stock.Amount(2) == 1)
			{
				stage = 1; until = tick + 100;
			}
			if (stage == 1 && tick >= until)
			{
				Check(workshop.Progress == 0 && !w.ActorsHavingTrait<MedievalCommander>().Any(), "An elite commander cannot train without meat.");
				Check(Tents(w).Length == 0 && stock.Amount(1) == 2, "A cart cannot deploy tents without a commander.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "barracks.png"));
				depot.Trait<MedievalStockpile>().Stock.Add(2, 15); stage = 2;
			}
			if (stage == 2 && (commander = w.ActorsHavingTrait<MedievalCommander>().FirstOrDefault()) != null)
			{
				Check(depot.Trait<MedievalStockpile>().Stock.Amount(2) + workshop.Inputs.Amount(2) == 0, "Recruitment must consume exactly 15 meat.");
				Check(MedievalEquipment.Amount(w.LocalPlayer, 1) == 0 && MedievalEquipment.Amount(w.LocalPlayer, 2) == 0, "Commander must consume one sword and one armour.");
				Check(MedievalSettlement.Population(w.LocalPlayer) == 3 && MedievalFoodEconomy.Demand(w.LocalPlayer) == 6, "Commander occupies one housing place and eats two food per minute.");
				Move(commander, 28, 30); stage = 3;
			}
			if (stage == 3 && commander.IsIdle && commander.Location == Cell(28, 30))
			{
				Select(w, commander); Button(w, "MD_COMMAND_DEPLOY");
				Sync.RunUnsynced(w, () =>
				{
					var generator = (OrderGenerator)w.OrderGenerator;
					var orders = generator.Order(w, Cell(32, 33), int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
					Check(orders.Length == 1, "Commander placement preview must produce one deployment order."); foreach (var order in orders) w.IssueOrder(order);
				});
				stage = 4; until = tick + 3;
			}
			if (stage == 4 && commander.Trait<MedievalCommander>().Busy)
			{
				Check(stock.Reserved(1) == 1, "Pickup must reserve exactly one tent."); Order(commander, "Stop"); stage = 5; until = tick + 3;
			}
			if (stage == 5 && tick >= until && !commander.Trait<MedievalCommander>().Busy)
			{
				reservedCancelled = stock.Amount(1) == 2 && stock.Reserved(1) == 0; Check(reservedCancelled, "Stop before pickup must release the reservation.");
				Deploy(commander, 32, 33); stage = 6;
			}
			if (stage == 6 && commander.Trait<MedievalCampStore>().Stock.Amount(1) == 1)
			{
				Order(commander, "Stop"); stage = 7; until = tick + 3;
				w.AddFrameEndTask(_ => { second = Spawn(w, "commander", 29, 30); third = Spawn(w, "commander", 29, 31); });
			}
			if (stage == 7 && tick >= until && third != null && !commander.Trait<MedievalCommander>().Busy)
			{
				carriedCancelled = !commander.Trait<MedievalCommander>().Busy && commander.Trait<MedievalCampStore>().Stock.Amount(1) == 1 && stock.Amount(1) == 1;
				Check(carriedCancelled, "Stop after pickup must keep the actual tent with the commander.");
				Deploy(commander, 32, 33); Deploy(second, 37, 33); Deploy(third, 37, 33); stage = 8; until = tick + 3;
			}
			if (stage == 8 && tick >= until && (second.Trait<MedievalCommander>().Busy || third.Trait<MedievalCommander>().Busy))
			{
				contested = stock.Reserved(1) == 1 && second.Trait<MedievalCommander>().Busy && !third.Trait<MedievalCommander>().Busy;
				Check(contested, "Two commanders cannot claim the last tent simultaneously."); stage = 9;
			}
			if (stage == 9 && Tents(w).Length == 2)
			{
				Check(MedievalCampStore.Amount(w.LocalPlayer, 1) == 0 && stock.Reserved(1) == 0, "Each pavilion consumes one real tent with no leaked reservation.");
				Move(commander, 40, 26); Move(second, 40, 25); Move(third, 41, 25);
				w.AddFrameEndTask(_ =>
				{
					for (var i = 0; i < 15; i++) troops.Add(Spawn(w, "commandguard", 32 + i % 5, 36 + i / 5));
					outsider = Spawn(w, "commandguard", 45, 40); citizen = Spawn(w, "builder", 35, 32); citizen.Trait<MedievalWorker>().Automatic = false;
					enemy = w.CreateActor("md.commandguard", [new OwnerInit(w.Players.First(p => p.InternalName == "Creeps")), new LocationInit(Cell(34, 34))]);
				}); stage = 10; until = tick + 250;
			}
			if (stage == 10 && tick >= until)
			{
				Check(troops.All(a => !a.Trait<MedievalRest>().Resting), "Troops must await the commander's rest order.");
				Check(Tents(w).All(t => !t.Trait<MedievalCampArea>().RestEnabled), "New pavilion circles must initially be inactive.");
				Select(w, commander); Game.Renderer.SaveScreenshot(Path.Combine(output, "camp-waiting.png")); Button(w, "MD_COMMAND_REST"); stage = 11; until = tick + 500;
			}
			if (stage == 11 && tick >= until)
			{
				var tents = Tents(w);
				Check(tents.All(t => t.Trait<MedievalCampArea>().RestEnabled && t.Trait<MedievalShelter>().Occupants(t) == 6), "Each overlapping circle must reserve exactly six beds, without double counting.");
				Check(troops.Count(a => a.Trait<MedievalRest>().Sleeping) == 12, "Twelve troops must physically reach and sleep inside two tents.");
				Check(!outsider.Trait<MedievalRest>().Resting && !enemy.Trait<MedievalRest>().Resting && !tents.Contains(citizen.Trait<MedievalRest>().Shelter), "Circles cannot admit distant soldiers, enemies or citizens.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "camp-resting.png"));
				Select(w, tents[0]); Button(w, "MD_CAMP_OPEN"); Button(w, "MD_CAMP_REST");
				Check(w.Selection.Actors.Any(a => a.TraitOrDefault<MedievalCommander>() != null), "Selecting a pavilion must allow finding its commander.");
				Select(w, commander); Button(w, "MD_COMMAND_WAKE"); stage = 12; until = tick + 200;
			}
			if (stage == 12 && tick >= until && w.WorldActor.Trait<MedievalDayNight>().Clock.Night)
			{
				Check(Tents(w).All(t => !t.Trait<MedievalCampArea>().RestEnabled && t.Trait<MedievalShelter>().Occupants(t) == 0), "Fin du repos must disable circles and empty every commanded tent, even at night.");
				Check(troops.All(a => !a.Trait<MedievalRest>().Resting), "Troops cannot automatically return to disabled camps at night.");
				// Disposal must release a reservation before collection, just like death.
				stock.Add(1, 1); third.Trait<Mobile>().SetPosition(third, Cell(28, 30)); Deploy(third, 35, 28); stage = 13; until = tick + 6;
			}
			if (stage == 13 && third.Trait<MedievalCommander>().Busy)
			{
				Check(stock.Reserved(1) == 1, "Disposal scenario must begin with a reserved tent.");
				w.AddFrameEndTask(_ => third.Dispose()); stage = 14; until = tick + 6;
			}
			if (stage == 14 && tick >= until)
			{
				Check(stock.Amount(1) == 1 && stock.Reserved(1) == 0, "A removed commander must release uncollected supplies."); Finish(w);
			}
			if (!finished && tick >= 8500) Finish(w);
		}
	}
}
