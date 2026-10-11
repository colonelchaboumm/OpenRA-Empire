using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalStorageZonesValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalStorageZonesValidation();
	}
	// Opt-in native integration: real UI orders, reservations, sabotage and rendering.
	public sealed class MedievalStorageZonesValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor warehouse, saboteur;
		string output;
		int tick, extinguished, lost;
		bool done, burned, delivered;
		readonly List<string> errors = [];
		readonly List<object> snapshots = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) { errors.Add(error); Console.WriteLine("STORAGE CHECK: " + error); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "nature-storage", "native"); Directory.CreateDirectory(output);
		}
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			var enemy = w.Players.Single(p => p.InternalName == "Rival");
			w.LocalPlayer.EnemyPlayersMask = enemy.PlayerMask; enemy.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			w.LocalPlayer.Shroud.Disabled = true; enemy.Shroud.Disabled = true;
			Actor Spawn(string name, int x, int y, Player owner = null) => w.CreateActor("md." + name, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
			warehouse = Spawn("warehouse", 26, 29);
			Spawn("house", 33, 33); Spawn("farm", 31, 37);
			var crop = Spawn("field.4x4", 25, 36).Trait<MedievalSource>(); crop.Remaining = crop.Info.Amount;
			Spawn("field.3x5", 35, 36);
			var neutral = w.Players.First(p => p.InternalName == "Neutral");
			Spawn("tree", 18, 27, neutral); Spawn("pine", 18, 30, neutral); Spawn("birch", 18, 33, neutral);
			Spawn("market", 17, 23, enemy).Trait<MedievalStockpile>().Stock.Add(1, 200);
			saboteur = Spawn("saboteur", 21, 29, enemy);
			Sync.RunUnsynced(w, () => { w.Selection.Combine(w, [warehouse], false, true); renderer.Viewport.AdjustZoom((float)Math.Log(1.25f / renderer.Viewport.Zoom)); });
		}
		void Draw(World w, StorageKind kind, int zoneIndex, CPos first, CPos last)
		{
			Sync.RunUnsynced(w, () =>
			{
				var yard = warehouse.Trait<MedievalStockyard>();
				w.Selection.Combine(w, [warehouse], false, true);
				var zone = Ui.Root.Get<ButtonWidget>("MD_YARD_ZONE");
				var expected = zoneIndex >= yard.Zones.Count ? "Nouvelle zone >" : $"Zone {zoneIndex + 1}/{yard.Zones.Count} >";
				for (var i = 0; i <= MedievalStockyard.MaxZones && zone.GetText() != expected; i++) zone.OnClick();
				Check(zone.GetText() == expected, "Zone selection must reach the requested reserve.");
				var type = Ui.Root.Get<ButtonWidget>("MD_YARD_KIND");
				for (var i = 0; i < 8 && type.GetText() != "Type : " + StorageGoods.KindNames[(int)kind] + " >"; i++) type.OnClick();
				Ui.Root.Get<ButtonWidget>("MD_YARD_PLACE").OnClick();
				var generator = w.OrderGenerator as MedievalStockyardOrderGenerator;
				Check(generator != null, "Placement button must open the storage rectangle tool."); if (generator == null) return;
				IEnumerable<Order> Click(CPos c) => generator.Order(w, c, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1));
				Check(!Click(first).Any(), "First corner must not place stock.");
				var orders = Click(last).ToArray(); Check(orders.Length == 1, "Valid second corner must issue exactly one order: " + kind);
				foreach (var order in orders) w.IssueOrder(order);
			});
		}
		void Snapshot(string name)
		{
			var yard = warehouse.Trait<MedievalStockyard>();
			var actual = Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Amount(warehouse, g)).ToArray();
			var shown = Enumerable.Range(0, StorageGoods.Count).Select(g => yard.Inside(warehouse, g) + yard.Zones.Sum(z => z.Stacks(r => yard.Amount(warehouse, z, r)).Where(s => s.Good == g).Sum(s => s.Amount))).ToArray();
			Check(actual.SequenceEqual(shown), name + ": indoor plus exterior display must equal all ten actual inventories.");
			Check(yard.Zones.All(z => z.Stacks(g => yard.Amount(warehouse, z, g)).All(s => s.Amount <= StorageGoods.Limit(s.Good))), "No drawn stack may exceed its cell capacity.");
			snapshots.Add(new { name, actual, shown, capacities = Enumerable.Range(0, 10).Select(g => StorageGoods.Stock(warehouse, g).Capacity(StorageGoods.Index(g))).ToArray() });
		}
		void Finish()
		{
			Check(burned && delivered && lost > 0, "Sabotage, smoke extinction, repair and physical delivery must all complete.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, burned, delivered, lost, snapshots }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL NATURE STORAGE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(27, 30))));
			var yard = warehouse.Trait<MedievalStockyard>(); var stock = warehouse.Trait<MedievalStockpile>().Stock; var fire = warehouse.Trait<MedievalBurnable>();
			if (tick == 20) Draw(w, StorageKind.Wood, 0, Cell(22, 29), Cell(23, 30));
			if (tick == 50) { Check(stock.Capacity(0) == 800, "A 2x2 wood zone adds 400 wood capacity."); Draw(w, StorageKind.Wood, 1, Cell(24, 25), Cell(25, 26)); }
			if (tick == 80) Draw(w, StorageKind.Grain, 2, Cell(22, 32), Cell(23, 34));
			if (tick == 110) Draw(w, StorageKind.Equipment, 3, Cell(29, 33), Cell(30, 34));
			if (tick == 140) Draw(w, StorageKind.Gold, 4, Cell(31, 29), Cell(32, 30));
			if (tick == 170) Draw(w, StorageKind.Camp, 5, Cell(26, 24), Cell(27, 25));
			if (tick == 200) Draw(w, StorageKind.Wood, 0, Cell(22, 29), Cell(23, 31));
			if (tick == 230)
			{
				Check(yard.Zones.Count == 6 && stock.Capacity(0) == 1400, "Six typed zones and a real resize must be applied.");
				if (yard.Zones.Count != 6) { Finish(); return; }
				stock.Add(0, 1150); stock.Add(1, 173); stock.Add(3, 87); stock.Add(4, 193);
				warehouse.Trait<MedievalEquipmentStore>().Stock.Add(0, 21); warehouse.Trait<MedievalEquipmentStore>().Stock.Add(1, 15); warehouse.Trait<MedievalEquipmentStore>().Stock.Add(2, 9);
				warehouse.Trait<MedievalCampStore>().Stock.Add(0, 38); warehouse.Trait<MedievalCampStore>().Stock.Add(1, 3);
				Check(stock.ReserveOut(0, 30) && stock.ReserveIn(0, 200), "Expanded capacity must allow booked deliveries.");
				Check(yard.PlacementError(warehouse, new StockyardArea(Cell(22, 29), new CVec(2, 2)), StorageKind.Wood, yard.Zones[0].Id) != null, "Reduction must refuse to drop physical goods or booked deliveries.");
				Check(yard.RemoveError(warehouse, yard.Zones[0].Id) != null, "A full reserve must not be removable.");
				Snapshot("Six reserves, 200 incoming wood not yet displayed");
			}
			if (tick == 250) Shot("01-reserves-and-nature");
			if (tick == 270) Check(saboteur.Trait<MedievalSaboteur>().Start(saboteur, warehouse, false, Cell(22, 29), true), "An enemy saboteur must be able to target the exterior reserve.");
			if (extinguished == 0 && fire.LostGoods >= 32)
			{
				burned = true; lost = fire.LostGoods;
				Check(stock.Amount(0) == 1150 - lost && yard.Amount(warehouse, yard.Zones[1], 0) == 400 && yard.Inside(warehouse, 0) == 150, "The fire must destroy only the attacked reserve, without drawing from other zones or indoors.");
				Check(stock.Amount(3) == 87 && stock.Amount(4) == 193 && stock.Incoming(0) == 200 && stock.Reserved(0) == 30, "Stone, gold and booked shipments must survive this wood reserve fire.");
				Check(fire.Smoke.Clouds.Count > 10 && yard.RemoveError(warehouse, yard.Zones[0].Id) != null, "A burning reserve produces dense smoke and cannot be removed.");
				Snapshot("Burning reserve with real losses"); Shot("02-dense-fire"); extinguished = tick;
			}
			if (extinguished > 0 && tick == extinguished + 8)
			{
				while (fire.Burning) fire.Douse(warehouse); lost = fire.LostGoods;
				Check(fire.Smoke.Clouds.Count > 0, "Smoke must persist immediately after extinction.");
			}
			if (extinguished > 0 && tick == extinguished + 32) Shot("03-cooling-smoke");
			if (extinguished > 0 && tick == extinguished + 165)
			{
				Check(!fire.Burning && fire.Smoke.Clouds.Count == 0, "Extinguished smoke must fully dissipate.");
				Draw(w, StorageKind.Wood, 0, Cell(22, 29), Cell(23, 31));
			}
			if (extinguished > 0 && tick == extinguished + 195)
			{
				Check(stock.Capacity(0) == 1400 && stock.Amount(0) == 1150 - lost, "Repair restores capacity, never destroyed resources.");
				stock.Unload(0, 200); stock.Load(0, 30); delivered = true;
				Check(stock.Amount(0) == 1320 - lost && stock.Incoming(0) == 0 && stock.Reserved(0) == 0, "Goods change only when the booked delivery and pickup really occur.");
				Snapshot("Repaired, delivered and picked up"); Shot("04-repaired");
			}
			if (extinguished > 0 && tick == extinguished + 215 || tick >= 1600) Finish();
		}
	}
}
