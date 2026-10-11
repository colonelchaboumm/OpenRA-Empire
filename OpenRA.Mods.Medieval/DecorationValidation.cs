using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalDecorationValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDecorationValidation();
	}
	// Exercises the actual menu, two-click tools, worker hauling, pathfinding and salvage.
	public sealed class MedievalDecorationValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, completedAt, finishAt, expected;
		bool finished, carried, cancellation;
		Actor walker, cancelled;
		readonly List<string> errors = [];
		readonly HashSet<int> flagFrames = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "decoration-preview");
			Directory.CreateDirectory(output);
		}
		void Check(bool valid, string message) { if (!valid) errors.Add(message); }
		Actor[] Decorations(World w) => w.Actors.Where(a => a.Owner == w.LocalPlayer && a.TraitOrDefault<MedievalDecoration>() != null).ToArray();
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var button = Ui.Root.Get<ButtonWidget>(id);
			Check(!button.IsDisabled(), "Unavailable button: " + id);
			button.OnClick();
		});
		int Click(World w, CPos cell)
		{
			var count = 0;
			Sync.RunUnsynced(w, () =>
			{
				var generator = (OrderGenerator)w.OrderGenerator;
				var orders = generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down,
					generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
				orders = orders.Concat(generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Up,
					generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1))).ToArray();
				count = orders.Length;
				foreach (var order in orders) w.IssueOrder(order);
			});
			return count;
		}
		void Place(World w, int index, int x, int y, int? ex = null, int? ey = null)
		{
			Button(w, "MD_DECOR_" + index);
			if (MedievalDecorations.Linear(MedievalDecorations.MenuTypes[index]))
			{
				Check(Click(w, Cell(x, y)) == 0, "First line click must only anchor the preview.");
				Check(Click(w, Cell(ex ?? x, ey ?? y)) == 1, "Second line click must issue a construction order.");
			}
			else Check(Click(w, Cell(x, y)) == 1, "Single decoration click must issue one construction order.");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
			var warehouse = Spawn("warehouse", 22, 27);
			warehouse.Trait<MedievalCampStore>().Stock.Add(MedievalCampStore.Cloth, 20);
			var depot = warehouse.Trait<MedievalStockpile>().Stock;
			depot.Add(0, 400); depot.Add(3, 300);
			Spawn("market", 22, 33).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn("house", 30, 25); Spawn("house", 34, 25);
			for (var i = 0; i < 6; i++) Spawn("builder", 25 + i, 39);
			walker = Spawn("militia", 32, 43);
		}
		void Finish(World w)
		{
			var totals = new int[4];
			foreach (var a in w.Actors.Where(a => a.Owner == w.LocalPlayer))
			{
				if (a.TraitOrDefault<MedievalStockpile>() is MedievalStockpile stock)
					for (var r = 0; r < 4; r++) totals[r] += stock.Stock.Amount(r);
				if (a.TraitOrDefault<MedievalBuilding>() is MedievalBuilding b)
					for (var r = 0; r < 4; r++) totals[r] += b.Materials.Amount(r);
				if (a.TraitOrDefault<MedievalWorker>() is MedievalWorker worker && worker.Cargo > 0 && !worker.CampCargo && !worker.EquipmentCargo) totals[worker.Resource] += worker.Cargo;
			}
			Check(totals[0] == 400 && totals[3] == 297, "Only the three stones incorporated in demolished paving are lost; unused deliveries and reservations are conserved.");
			Check(carried && cancellation, "Must observe real hauling and cancel a reserved construction.");
			Check(flagFrames.Count == 12, "All twelve flag poses must animate in the game.");
			Check(walker.Location == Cell(32, 35), "Soldier must actually cross the garden entrance and stone path.");
			Game.Renderer.SaveScreenshot(Path.Combine(output, "garden.png"));
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, totals, carried,
				cancellation, completedAt, expected, decorations = Decorations(w).Length, flagFrames = flagFrames.Count,
				unfinished = Decorations(w).Where(a => !a.Trait<MedievalBuilding>().Complete).Select(a => new { type = a.Info.Name, location = a.Location.ToString() }).ToArray()
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL DECORATIONS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (finished || w.LocalPlayer == null) return;
			tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(29, 32)));
			if (tick == 20)
			{
				Button(w, "MD_DECOR_OPEN");
				Check(Ui.Root.Get("MD_DECOR_PANEL").IsVisible(), "Decoration menu must open.");
				Sync.RunUnsynced(w, () => w.Selection.Combine(w, [walker], false, true));
				Check(!Ui.Root.Get("MD_DECOR_PANEL").IsVisible(), "Selecting a citizen must hide the decoration menu.");
				Button(w, "MD_DECOR_OPEN");
				Check(Ui.Root.Get("MD_DECOR_PANEL").IsVisible(), "One click must reopen the catalogue after changing selection.");
				Check(MedievalDecorations.Plan("md.stonepath", Cell(1, 1), Cell(65, 1)).Length == 0, "Oversized traces must be rejected before placement.");
				Check(MedievalDecorations.Plan("md.stonepath", Cell(1, 1), Cell(3, 4)).Select(p => p.Cell).Distinct().Count() == 6, "A bent path must contain each paving cell exactly once.");
				Button(w, "MD_DECOR_2"); Button(w, "MD_DECOR_ROTATE");
				Check(((MedievalDecorationOrderGenerator)w.OrderGenerator).Kind == "md.gardengatey", "Portillon rotation must select the other isometric axis.");
				Place(w, 1, 29, 30, 36, 30); Place(w, 1, 29, 31, 29, 36); Place(w, 1, 36, 31, 36, 36);
				Place(w, 1, 29, 37, 31, 37); Place(w, 1, 33, 37, 36, 37);
				Place(w, 2, 32, 37); Place(w, 0, 32, 33);
				Place(w, 5, 32, 34, 32, 36); Place(w, 5, 32, 38, 32, 41);
				foreach (var x in new[] { 30, 34 }) { Place(w, 3, x, 32); Place(w, 3, x, 34); Place(w, 4, x, 36); }
				// Invalid batch must be rejected as a whole, even via a direct synchronized order.
				w.IssueOrder(new Order("MedievalDecorate", w.LocalPlayer.PlayerActor, Target.FromCell(w, Cell(28, 25)), false) { TargetString = "md.stonepath", ExtraLocation = Cell(36, 25) });
				Button(w, "MD_DECOR_CLOSE");
			}
			if (tick == 40)
			{
				expected = Decorations(w).Length;
				Check(expected == 42, "All requested decorations must be placed, and the invalid batch must leave none.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.house", Cell(32, 34)), "A passable path must still prevent overlapping buildings.");
				Place(w, 5, 32, 38, 32, 41);
				Place(w, 0, 38, 33);
				Sync.RunUnsynced(w, w.CancelInputMode);
			}
			if (tick >= 60 && !cancellation)
			{
				cancelled ??= MedievalDecorations.At(w, w.LocalPlayer, Cell(38, 33));
				if (cancelled != null && Enumerable.Range(0, 4).Any(r => cancelled.Trait<MedievalBuilding>().Materials.Incoming(r) > 0))
				{
					Check(Decorations(w).Length == expected + 1, "Redrawing existing paving must not duplicate its construction or cost.");
					w.IssueOrder(new Order("MedievalRemoveDecoration", w.LocalPlayer.PlayerActor, Target.FromActor(cancelled), false));
					cancellation = true;
				}
			}
			carried |= w.Actors.Any(a => a.TraitOrDefault<MedievalWorker>()?.Cargo > 0);
			foreach (var flag in Decorations(w).Where(a => a.Info.Name == "md.banner" && MedievalStockpile.Active(a)))
				flagFrames.Add(flag.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.CurrentFrame);
			if (tick > 100 && completedAt == 0 && cancellation && Decorations(w).All(MedievalStockpile.Active))
			{
				completedAt = tick;
				Check(Decorations(w).Length == expected, "Cancelled decoration must disappear.");
				var mobile = walker.Trait<Mobile>();
				Check(mobile.CanEnterCell(Cell(32, 37), check: BlockedByActor.Immovable), "Gate must be passable.");
				Check(mobile.CanEnterCell(Cell(32, 35), check: BlockedByActor.Immovable), "Stone path must be passable.");
				Check(!mobile.CanEnterCell(Cell(31, 37), check: BlockedByActor.Immovable), "Fence must block movement.");
				Check(!mobile.CanEnterCell(Cell(32, 33), check: BlockedByActor.Immovable), "Well must block movement.");
				Check(MedievalDecorations.At(w, w.LocalPlayer, Cell(29, 30)).Trait<MedievalGardenFence>().Connections == 5, "Fence corner must join both cell axes.");
				w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(32, 35)), false));
				Button(w, "MD_DECOR_REMOVE");
				Check(Click(w, Cell(32, 41)) == 1, "Erase tool must remove a completed decoration.");
				Sync.RunUnsynced(w, w.CancelInputMode);
				finishAt = tick + 500;
			}
			if (tick == finishAt - 20)
			{
				Button(w, "MD_DECOR_OPEN");
				renderer.Viewport.AdjustZoom((float)Math.Log(1.5f / renderer.Viewport.Zoom));
			}
			if (tick == finishAt || tick == 9000)
			{
				Check(completedAt > 0, "Builders must finish every decoration before the timeout.");
				Finish(w);
			}
		}
	}
}
