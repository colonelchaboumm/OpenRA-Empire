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
	public class MedievalStallValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalStallValidation();
	}
	public sealed class MedievalStallValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor depot, market, stall, emptyStall, house, citizen, soldier, fallback;
		Actor forge, smith;
		bool smithFed;
		readonly bool[] smithPickup = new bool[4];
		readonly int[] destroyedStock = new int[4];
		int tick, stage, until;
		bool finished, builderPickup, citizenFed, soldierFed;
		readonly bool[] hauled = new bool[4];
		readonly List<string> errors = [];
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "stall-preview"); Directory.CreateDirectory(output);
		}
		Actor Spawn(World w, string type, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var button = Ui.Root.Get<ButtonWidget>(id); Check(!button.IsDisabled(), "Disabled button: " + id); button.OnClick();
		});
		void Place(World w, int index, CPos cell)
		{
			if (index == 9)
			{
				Check(MedievalAuditUi.OpenContext(w, market, "Commerce"), "Market trade tab is accessible.");
				Button(w, "MD_BUILD_9");
			}
			else
			{
				Check(MedievalAuditUi.OpenContext(w, w.Actors.First(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.builder"), "Construire"), "Builder construction tab is accessible.");
				Button(w, "MD_MENU_TAB_0"); Button(w, "MD_MENU_ITEM_" + index);
			}
			Sync.RunUnsynced(w, () =>
			{
				var generator = (OrderGenerator)w.OrderGenerator;
				var orders = generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
				Check(orders.Length == 1, "Placement must issue a valid construction order.");
				foreach (var order in orders) w.IssueOrder(order);
			});
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			depot = Spawn(w, "warehouse", 20, 28);
			var stock = depot.Trait<MedievalStockpile>().Stock;
			stock.Add(0, 180); stock.Add(1, 250); stock.Add(3, 100);
			market = Spawn(w, "market", 24, 28); market.Trait<MedievalStockpile>().Stock.Add(1, 100);
			for (var i = 0; i < 3; i++) Spawn(w, "builder", 23+i, 33);
			for (var i = 0; i < 4; i++) Spawn(w, "hauler", 26+i, 33);
			emptyStall = Spawn(w, "marketstall", 15, 48);
		}
		void Finish(World w)
		{
			Check(stage == 7, "All relay stages must finish before the timeout.");
			Check(hauled[0] && hauled[1] && hauled[3] && builderPickup, "Haulers must carry all requested resources and builders must collect at the stall.");
			Check(citizenFed && soldierFed, "A citizen and a soldier outside the main market must eat from the stall.");
			Check(smithFed && smithPickup[0] && smithPickup[3], "The smith must eat at a small market and physically collect both wood and stone there.");
			Check(MedievalEquipment.Amount(w.LocalPlayer, 0) == 1, "The smith must return the completed bow to the warehouse.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, errors, hauled, builderPickup, citizenFed, soldierFed, smithFed, smithPickup, destroyedStock }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL STALLS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (finished || w.LocalPlayer == null) return;
			tick++;
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(34, 31)));
			if (tick == 1) w.AddFrameEndTask(Setup);
			if (tick == 10) Select(w, market);
			if (tick < 20) return;
			if (tick == 20)
			{
				Check(MedievalAuditUi.OpenContext(w, market, "Commerce") && Ui.Root.Get("MD_TRADE_PANEL").IsVisible(), "Market trade tab must reveal the stall build button.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "market-menu.png"));
				Place(w, 9, Cell(38, 28)); stage = 1;
			}
			stall ??= w.Actors.FirstOrDefault(a => a.Info.Name == "md.marketstall" && a.Location == Cell(38, 28));
			foreach (var worker in w.ActorsWithTrait<MedievalWorker>())
			{
				if (worker.Trait.Info.Profession == "hauler" && worker.Trait.Cargo > 0) hauled[worker.Trait.Resource] = true;
				if (stall != null && worker.Trait.Info.Profession == "builder" && worker.Trait.Cargo > 0 && MedievalAccess.Adjacent(worker.Actor, stall)) builderPickup = true;
			}
			if (stage == 1 && MedievalStockpile.Active(stall) && stall.Trait<MedievalMarketStall>().Linked(stall))
			{
				Check(stall.Trait<MedievalMarketStall>().Market == market, "Stall must auto-link to the nearest completed main market.");
				Check(Enumerable.Range(0, 4).All(r => emptyStall.Trait<MedievalMarketStall>().Request(emptyStall, r) == 0 && emptyStall.Trait<MedievalStockpile>().Stock.Amount(r) == 0), "An unused stall must not draw supplies.");
				w.AddFrameEndTask(world => { citizen = Spawn(world, "farmer", 42, 30); soldier = Spawn(world, "militia", 43, 30); });
				Place(w, 0, Cell(43, 28)); stage = 2;
			}
			house ??= w.Actors.FirstOrDefault(a => a.Info.Name == "md.house");
			if (citizen != null)
			{
				citizenFed |= citizen.Trait<MedievalNeedsFood>().Need.InSupply;
				soldierFed |= soldier.Trait<MedievalNeedsFood>().Need.InSupply;
				Check(!MedievalFoodEconomy.Station(market).Covers(citizen.CenterPosition) && !MedievalFoodEconomy.Station(market).Covers(soldier.CenterPosition), "Remote people must lie outside the main market food radius.");
			}
			if (stage == 2 && house != null)
				Check(!MedievalConstructionSupply.CanSupply(depot, w.LocalPlayer, house.CenterPosition), "Remote construction must lie outside the warehouse radius.");
			if (stage == 2 && MedievalStockpile.Active(house) && citizenFed && soldierFed)
			{
				Check(stall.Trait<MedievalMarketStall>().Desired(stall, 0) == 0 && stall.Trait<MedievalMarketStall>().Desired(stall, 3) == 0, "Finished construction must stop requesting materials.");
				Check(MedievalAuditUi.OpenContext(w, stall, "Relais") && Ui.Root.Get("MD_STALL_PANEL").IsVisible(), "Stall relay tab must show its live stock and parent market.");
				stage = 3; until = tick + 800;
			}
			if (stage == 3 && tick == until - 650) Game.Renderer.SaveScreenshot(Path.Combine(output, "supplied-stall.png"));
			if (stage == 3 && tick >= until)
			{
				Check(citizen.Trait<MedievalNeedsFood>().Need.InSupply && citizen.Trait<MedievalNeedsFood>().Need.TicksSinceMeal < 750
					&& soldier.Trait<MedievalNeedsFood>().Need.InSupply && soldier.Trait<MedievalNeedsFood>().Need.TicksSinceMeal < 750,
					"Both remote people must actually consume meals and stay supplied beyond one meal interval.");
				for (var r = 0; r < 4; r++)
				{
					var total = w.Actors.Where(a => a.Owner == w.LocalPlayer && a.IsInWorld && !a.IsDead).Sum(a => (a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(r) ?? 0)
						+ (a.TraitOrDefault<MedievalBuilding>()?.Materials.Amount(r) ?? 0) + (a.TraitOrDefault<MedievalWorker>() is { EquipmentCargo: false } worker && worker.Resource == r ? worker.Cargo : 0));
					if (r == 1) total += w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
					Check(total == new[] { 180, 350, 0, 100 }[r], "Resources must be conserved through all relays, meals and construction: " + r);
				}
				w.AddFrameEndTask(world => { fallback = Spawn(world, "market", 23, 22); });
				stage = 4; until = tick + 30;
			}
			if (stage == 4 && tick >= until)
			{
				Select(w, stall); Button(w, "MD_STALL_RELINK");
				Sync.RunUnsynced(w, () =>
				{
					var generator = (OrderGenerator)w.OrderGenerator;
					foreach (var o in generator.Order(w, fallback.Location, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1))) w.IssueOrder(o);
				});
				stage = 5; until = tick + 100;
			}
			if (stage == 5 && tick == until)
			{
				Check(stall.Trait<MedievalMarketStall>().Market == fallback, "Manual reassignment must select the chosen parent market.");
				w.AddFrameEndTask(world =>
				{
					for (var r = 0; r < 4; r++) destroyedStock[r] = fallback.Trait<MedievalStockpile>().Stock.Amount(r);
					fallback.Dispose();
				});
			}
			if (stage == 5 && tick > until + 100)
			{
				Check(stall.Trait<MedievalMarketStall>().Market == market, "Destroyed parent must be replaced automatically by a surviving friendly market.");
				w.AddFrameEndTask(world =>
				{
					forge = Spawn(world, "forge", 39, 32);
					smith = Spawn(world, "blacksmith", 41, 31);
					var need = smith.Trait<MedievalNeedsFood>().Need;
					for (var i = 0; i < need.MealInterval; i++) need.Advance();
					world.IssueOrder(new Order("MedievalEquipmentTarget", world.LocalPlayer.PlayerActor, false) { ExtraData = 0, TargetString = "+" });
				});
				Select(w, stall); stage = 6;
			}
			if (stage == 6 && smith != null)
			{
				var worker = smith.Trait<MedievalWorker>();
				if (worker.Cargo > 0 && !worker.EquipmentCargo && MedievalAccess.Adjacent(smith, stall)) smithPickup[worker.Resource] = true;
				smithFed |= smith.Trait<MedievalNeedsFood>().Need.TicksSinceMeal == 0
					&& MedievalFoodEconomy.Station(stall).Covers(smith.CenterPosition) && !MedievalFoodEconomy.Station(market).Covers(smith.CenterPosition);
				Check(forge.Trait<MedievalMilitaryWorkshop>().SupplyStall(forge) == stall, "Forge must select the nearby linked stall as its material supply.");
				if (MedievalEquipment.Amount(w.LocalPlayer, 0) == 1)
				{
					Check(stall.Trait<MedievalMarketStall>().Desired(stall, 0) == 0 && stall.Trait<MedievalMarketStall>().Desired(stall, 3) == 0, "Reached equipment targets must stop the forge's material requests.");
					for (var r = 0; r < 4; r++)
					{
						var total = w.Actors.Where(a => a.Owner == w.LocalPlayer && a.IsInWorld && !a.IsDead).Sum(a => (a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(r) ?? 0)
							+ (a.TraitOrDefault<MedievalBuilding>()?.Materials.Amount(r) ?? 0) + (a.TraitOrDefault<MedievalMilitaryWorkshop>()?.Inputs.Amount(r) ?? 0)
							+ (a.TraitOrDefault<MedievalWorker>() is { EquipmentCargo: false } carrier && carrier.Resource == r ? carrier.Cargo : 0));
						if (r == 1) total += w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
						total += MedievalEquipment.Recipes[0][r] + destroyedStock[r];
						Check(total == new[] { 180, 350, 0, 100 }[r], $"Forge relay must conserve resources, including recipe costs and destroyed stock: resource {r}, accounted {total}.");
					}
					stage = 7; until = tick + 150;
					Select(w, forge);
				}
			}
			if (stage == 7 && tick >= until) { Game.Renderer.SaveScreenshot(Path.Combine(output, "supplied-forge.png")); Finish(w); }
			else if (tick == 11000) Finish(w);
		}
	}
}
