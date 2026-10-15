using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	// A separate real-engine scenario; never enabled by the playable manifest.
	public class MedievalStockyardValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalStockyardValidation();
	}
	public sealed class MedievalStockyardValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor warehouse;
		Actor second;
		Actor stall;
		int stallHash;
		int tick;
		int engineTicks;
		int savedHash;
		string output;
		bool finished;
		readonly List<string> errors = [];
		readonly List<object> snapshots = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "stockyard-preview");
			Directory.CreateDirectory(output);
		}
		void Check(bool ok, string message) { if (!ok) errors.Add(message); }
		static int ContentsHash(Inventory stock)
		{
			var hash = 17;
			for (var r = 0; r < Inventory.ResourceCount; r++) unchecked { hash = ((hash * 31 + stock.Amount(r)) * 31 + stock.Incoming(r)) * 31 + stock.Reserved(r); }
			return hash;
		}
		void Snapshot(string label)
		{
			var stock = warehouse.Trait<MedievalStockpile>().Stock;
			var yard = warehouse.Trait<MedievalStockyard>();
			var actual = Enumerable.Range(0, 4).Select(stock.Amount).ToArray();
			var shown = Enumerable.Range(0, 4).Select(r => yard.Inside(warehouse, r) + yard.Zones.Sum(z => z.Stacks(g => yard.Amount(warehouse, z, g)).Where(s => s.Good == r).Sum(s => s.Amount))).ToArray();
			Check(actual.SequenceEqual(shown), label + ": displayed objects do not match the warehouse inventory.");
			snapshots.Add(new { label, actual, shown });
		}
		void DrawArea(World w, CPos first, CPos last, Actor storage = null)
		{
			storage ??= warehouse;
			var small = storage.TraitOrDefault<MedievalMarketStall>() != null;
			Check(MedievalAuditUi.OpenContext(w, storage, small ? "Relais" : "Entrepôt"), "Storage management tab is accessible.");
			Sync.RunUnsynced(w, () =>
			{
				Check(Ui.Root.Get(small ? "MD_STALL_PANEL" : "MD_YARD_PANEL").IsVisible(), "Storage tab must expose its exterior storage panel.");
				if (small) Check(!Ui.Root.Get("MD_YARD_PANEL").IsVisible(), "Stall and warehouse panels must not overlap.");
				Ui.Root.Get<ButtonWidget>(small ? "MD_STALL_YARD_PLACE" : "MD_YARD_PLACE").OnClick();
				var generator = w.OrderGenerator as MedievalStockyardOrderGenerator;
				Check(generator != null, "The actual warehouse button must start exterior area placement.");
				if (generator == null) return;
				IEnumerable<Order> Click(CPos c) => generator.Order(w, c, int2.Zero,
					new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1));
				Check(!Click(first).Any(), "First corner must not create an exterior area.");
				var orders = Click(last).ToArray();
				Check(orders.Length == 1, "Second corner must issue one placement order.");
				foreach (var order in orders) w.IssueOrder(order);
			});
		}
		void ITick.Tick(Actor self)
		{
			if (finished || self.World.LocalPlayer == null) return;
			var w = self.World;
			// Leave enough simulation time for the engine's queued/network orders.
			if (++engineTicks % 3 != 0) return;
			tick++;
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(26, 30)));
			if (tick == 1)
			{
				warehouse = MedievalSettlement.Warehouses(w.LocalPlayer).First();
				foreach (var actor in w.ActorsWithTrait<MedievalWorker>()) w.IssueOrder(new Order("Stop", actor.Actor, false));
			}
			var yard = warehouse.Trait<MedievalStockyard>();
			var stock = warehouse.Trait<MedievalStockpile>().Stock;
			if (tick == 10) DrawArea(w, Cell(25, 31), Cell(22, 28));
			if (tick == 16)
			{
				Check(yard.HasArea && yard.Area == new StockyardArea(Cell(22, 28), new CVec(4, 4)), "Reverse corner drawing must create the expected 4x4 area.");
				Snapshot("Initial stocks");
				Check(MedievalStockyard.RenderArea(warehouse, yard.Area, true).Count() == yard.Area.Cells().Count(), "Preview must contain ground only, never a second copy of the stock.");
				Check(!((IRender)warehouse.Trait<MedievalStockDisplay>()).Render(warehouse, renderer).Any()
					&& !((IRenderAnnotations)warehouse.Trait<MedievalStockDisplay>()).RenderAnnotations(warehouse, renderer).Any(), "An exterior yard must replace the building's piles and duplicate resource counters.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.house", Cell(22, 28)), "A new building must not overlap an exterior storage area.");
				Check(yard.PlacementError(warehouse, new StockyardArea(Cell(28, 28), new CVec(4, 4))) != null, "Existing buildings must reject area placement.");
				Check(yard.PlacementError(warehouse, new StockyardArea(Cell(5, 45), new CVec(4, 4))) != null, "Distant areas must be refused.");
				Check(yard.PlacementError(warehouse, new StockyardArea(Cell(61, 60), new CVec(4, 4))) != null, "Out-of-map areas must be refused.");
			}
			if (tick == 18) Game.Renderer.SaveScreenshot(Path.Combine(output, "01-initial.png"));
			if (tick == 20)
			{
				Check(stock.ReserveOut(0, 30) && stock.ReserveIn(1, 25), "Stock reservations must succeed.");
				Check(stock.Amount(0) == 180 && stock.Amount(1) == 100, "Reservations must not move physical goods.");
				Snapshot("Reserved goods still present; incoming delivery not yet present");
			}
			if (tick == 25) { stock.Load(0, 30); stock.Unload(1, 25); Snapshot("After physical load and delivery"); }
			if (tick == 28) Game.Renderer.SaveScreenshot(Path.Combine(output, "02-transfer.png"));
			if (tick == 30) w.IssueOrder(new Order("MedievalPlaceStockyard", warehouse, Target.FromCell(w, Cell(28, 28)), false) { ExtraLocation = Cell(31, 31) });
			if (tick == 36) Check(yard.Area.Start == Cell(22, 28), "An invalid replacement must preserve the previous area.");
			if (tick == 40) w.AddFrameEndTask(_ =>
			{
				second = w.CreateActor("md.warehouse", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(36, 28)), new MedievalStockInit([1, 2, 3, 4])]);
				w.IssueOrder(new Order("MedievalPlaceStockyard", second, Target.FromCell(w, Cell(34, 21)), false) { ExtraLocation = Cell(37, 24) });
			});
			if (tick == 48)
			{
				Check(second.Trait<MedievalStockyard>().HasArea, "A second warehouse must be able to place its own area.");
				Check(yard.PlacementError(warehouse, second.Trait<MedievalStockyard>().Area) != null, "Areas belonging to different warehouses must not overlap.");
				Check(second.Trait<MedievalStockyard>().Area.Stacks(second.Trait<MedievalStockpile>().Stock).Sum(s => s.Amount) == 10, "An area must show only its own warehouse stock.");
				Snapshot("Two separate warehouse inventories");
			}
			if (tick == 50) { for (var r = 0; r < 4; r++) stock.Take(r, stock.Amount(r)); Snapshot("Completely empty"); }
			if (tick == 53) Game.Renderer.SaveScreenshot(Path.Combine(output, "03-empty.png"));
			if (tick == 60) { for (var r = 0; r < 4; r++) stock.Add(r, stock.Capacity(r)); Snapshot("Completely full"); }
			if (tick == 63) Game.Renderer.SaveScreenshot(Path.Combine(output, "04-full.png"));
			if (tick == 70)
			{
				stock.ReserveOut(0, 10); savedHash = ContentsHash(stock);
				DrawArea(w, Cell(20, 30), Cell(24, 33));
			}
			if (tick == 76)
			{
				Check(yard.Area.Size == new CVec(5, 4) && ContentsHash(stock) == savedHash, "Redrawing the area must conserve goods and reservations.");
				Check(!MedievalStockyard.Covers(w, Cell(22, 28)), "Redrawing must release the old ground.");
				Snapshot("Redrawn without any inventory change");
			}
			if (tick == 78) Game.Renderer.SaveScreenshot(Path.Combine(output, "05-redrawn.png"));
			if (tick == 79)
			{
				Check(yard.RemoveError(warehouse, yard.Zones[0].Id) != null, "A full expanded reserve must refuse removal.");
				for (var r = 0; r < 4; r++) stock.Take(r, Math.Max(0, stock.Amount(r) - StorageGoods.BaseCapacity(warehouse, r)));
				savedHash = ContentsHash(stock);
			}
			if (tick == 80) Sync.RunUnsynced(w, () =>
			{
				w.Selection.Combine(w, [warehouse], false, true);
				Ui.Root.Get<ButtonWidget>("MD_YARD_REMOVE").OnClick();
			});
			if (tick == 86) Check(!yard.HasArea && ContentsHash(stock) == savedHash, "Removing the area must conserve every resource and reservation.");
			if (tick == 90) DrawArea(w, Cell(22, 28), Cell(25, 31));
			if (tick == 98) Game.Renderer.SaveScreenshot(Path.Combine(output, "06-finished.png"));
			if (tick == 110) w.AddFrameEndTask(_ =>
			{
				var unfinished = w.CreateActor("md.warehouse", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(44, 36)), new MedievalConstructionInit()]);
				Check(unfinished.Trait<MedievalStockyard>().PlacementError(unfinished, new StockyardArea(Cell(44, 40), new CVec(4, 4))) != null, "An unfinished warehouse must not create an area.");
			});
			if (tick == 120) w.AddFrameEndTask(_ => second.Dispose());
			if (tick == 126) Check(!MedievalStockyard.Covers(w, Cell(34, 21)), "Removing a warehouse must release its exterior area.");
			if (tick == 145) w.AddFrameEndTask(_ =>
			{
				stall = w.CreateActor("md.marketstall", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(44, 28)), new MedievalStockInit([17, 23, 0, 9])]);
				var inventory = stall.Trait<MedievalStockpile>().Stock;
				inventory.ReserveOut(0, 4); inventory.ReserveIn(3, 7); stallHash = ContentsHash(inventory);
			});
			if (tick >= 150) renderer.Viewport.Center(w.Map.CenterOfCell(Cell(44, 30)));
			if (tick == 150) DrawArea(w, Cell(45, 32), Cell(44, 31), stall);
			if (tick == 156)
			{
				var smallYard = stall.Trait<MedievalStockyard>();
				var inventory = stall.Trait<MedievalStockpile>().Stock;
				Check(smallYard.HasArea && smallYard.Area.Size == new CVec(2, 2), "A stall must support a 2x2 exterior storage area through its own UI.");
				Check(ContentsHash(inventory) == stallHash && smallYard.Area.Stacks(inventory).Sum(s => s.Amount) == 49, "Stall yard must show only delivered goods and conserve reservations.");
				Check(((IRenderAnnotations)smallYard).RenderAnnotations(stall, renderer).OfType<StockBadgeRenderable>().Count() == 4, "Mixed zones must expose four commodity capacities, including the newly added meat space.");
				Check(!((IRenderAnnotations)stall.Trait<MedievalStockDisplay>()).RenderAnnotations(stall, renderer).Any(), "Stall resource counters must not be drawn twice.");
			}
			if (tick == 160) DrawArea(w, Cell(44, 32), Cell(46, 34), stall);
			if (tick == 166) Check(stall.Trait<MedievalStockyard>().Area.Size == new CVec(3, 3)
				&& ContentsHash(stall.Trait<MedievalStockpile>().Stock) == stallHash && !MedievalStockyard.Covers(w, Cell(44, 31)), "Moving a stall yard must preserve the physical inventory and release the former area.");
			if (tick == 170) Sync.RunUnsynced(w, () => Ui.Root.Get<ButtonWidget>("MD_STALL_YARD_REMOVE").OnClick());
			if (tick == 176) Check(!stall.Trait<MedievalStockyard>().HasArea && ContentsHash(stall.Trait<MedievalStockpile>().Stock) == stallHash, "Removing a stall yard must conserve its inventory and reservations.");
			if (tick == 180) DrawArea(w, Cell(44, 31), Cell(45, 32), stall);
			if (tick == 205) Game.Renderer.SaveScreenshot(Path.Combine(output, "07-stall-yard.png"));
			if (tick != 210) return;
			Snapshot("Final conservation check");
			Check(ContentsHash(stock) == savedHash, "Area operations must not change the inventory.");
			Check(ContentsHash(stall.Trait<MedievalStockpile>().Stock) == stallHash, "All stall yard operations must preserve the same stock and reservations.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, engineTicks, errors, snapshots }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL STOCKYARD: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true;
			Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
