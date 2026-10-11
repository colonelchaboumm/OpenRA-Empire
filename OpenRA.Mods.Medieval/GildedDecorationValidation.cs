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
	public class MedievalGildedDecorationValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalGildedDecorationValidation();
	}
	// Actual catalogue orders, gold delivery, collision, overlapping paving and salvage.
	public sealed class MedievalGildedDecorationValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor depot, walker;
		int tick, phase, finishAt;
		bool done, goldCarried, shortageChecked, stocked;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool valid, string error) { if (!valid && !errors.Contains(error)) errors.Add(error); }
		Actor[] Decorations(World w) => w.Actors.Where(a => a.IsInWorld && a.Owner == w.LocalPlayer && a.TraitOrDefault<MedievalDecoration>() != null).ToArray();
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "gilded-decorations", "native");
			Directory.CreateDirectory(output);
		}
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var button = Ui.Root.Get<ButtonWidget>("MD_MENU_" + id);
			Check(button.IsVisible() && !button.IsDisabled(), "Unavailable catalogue button " + id);
			button.OnClick();
		});
		void Click(World w, CPos cell) => Sync.RunUnsynced(w, () =>
		{
			var g = (OrderGenerator)w.OrderGenerator;
			foreach (var order in g.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, g.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray()) w.IssueOrder(order);
		});
		void Place(World w, int index, int x, int y, int? endX = null, int? endY = null, bool rotate = false)
		{
			Button(w, "TAB_4");
			for (var p = 0; p < index / 8; p++) Button(w, "NEXT");
			Button(w, "ITEM_" + index % 8);
			if (rotate) Button(w, "ROTATE");
			Click(w, Cell(x, y));
			if (endX.HasValue) Click(w, Cell(endX.Value, endY.Value));
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
			depot = Spawn("warehouse", 22, 27); var stock = depot.Trait<MedievalStockpile>().Stock;
			stock.Add(0, 400); stock.Add(3, 300);
			Spawn("market", 22, 33).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn("house", 30, 25); Spawn("house", 34, 25);
			for (var i = 0; i < 6; i++) Spawn("builder", 25 + i, 29);
			walker = Spawn("militia", 32, 40);
		}
		int[] Totals(World w)
		{
			var totals = new int[Inventory.ResourceCount];
			foreach (var a in w.Actors.Where(a => a.IsInWorld && a.Owner == w.LocalPlayer))
			{
				for (var r = 0; r < totals.Length; r++)
				{
					totals[r] += a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(r) ?? 0;
					totals[r] += a.TraitOrDefault<MedievalBuilding>()?.Materials.Amount(r) ?? 0;
				}
				if (a.TraitOrDefault<MedievalWorker>() is MedievalWorker worker && !worker.CampCargo && !worker.EquipmentCargo && worker.Cargo > 0)
					totals[worker.Resource] += worker.Cargo;
			}
			return totals;
		}
		void Finish(World w)
		{
			var totals = Totals(w);
			Check(phase == 3, "All decorations must finish and the overlaid lion must be removed before timeout.");
			Check(goldCarried && shortageChecked, "Builders must physically carry gold, and lack of gold must block completion.");
			Check(totals[0] == 400 && totals[3] == 300 && totals[4] == 200, "Construction, duplicate traces and removal must conserve wood, stone and gold.");
			Check(Decorations(w).Length == 15, "Removing only the lion must preserve all other decorations.");
			Check(MedievalDecorations.At(w, w.LocalPlayer, Cell(34, 31))?.Info.Name == "md.gildedplaza", "Removing a statue must preserve its golden paving.");
			Game.Renderer.SaveScreenshot(Path.Combine(output, "salvage.png"));
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, phase, goldCarried, shortageChecked, totals,
				decorations = Decorations(w).Select(a => new { type = a.Info.Name, complete = a.Trait<MedievalBuilding>().Complete, at = a.Location.ToString(),
					materials = Enumerable.Range(0, Inventory.ResourceCount).Select(a.Trait<MedievalBuilding>().Materials.Amount).ToArray() }).ToArray()
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL GILDED DECORATIONS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			if (depot == null) return;
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 33))));
			goldCarried |= w.ActorsHavingTrait<MedievalWorker>().Any(a => a.Trait<MedievalWorker>().Resource == 4 && a.Trait<MedievalWorker>().Cargo > 0);
			if (tick == 20)
			{
				Place(w, 16, 30, 30);
				Check(Ui.Root.Get<LabelWidget>("MD_MENU_PAGE").GetText() == "3/3", "Golden fences and royal square must be accessible on catalogue page three.");
			}
			var waiting = Decorations(w).FirstOrDefault();
			if (!stocked && tick >= 320 && (waiting?.Trait<MedievalBuilding>().Materials.Amount(3) > 0 || tick == 1400))
			{
				var site = Decorations(w).SingleOrDefault();
				shortageChecked = site != null && !site.Trait<MedievalBuilding>().Complete && site.Trait<MedievalBuilding>().Materials.Amount(4) == 0
					&& site.Trait<MedievalBuilding>().Materials.Amount(3) > 0;
				Check(shortageChecked, "Paving must wait for gold despite delivered stone.");
				Check(depot.Trait<MedievalStockpile>().Stock.Add(4, 200) == 200, "Gold must enter a real storage inventory.");
				stocked = true;
				Place(w, 15, 34, 31);
				Place(w, 17, 29, 36, 31, 36); Place(w, 17, 33, 36, 36, 36); Place(w, 17, 29, 37, 29, 39);
				Place(w, 18, 32, 36); Place(w, 18, 29, 40, rotate: true);
				Check(((MedievalDecorationOrderGenerator)w.OrderGenerator).Kind == "md.gildedgatey", "Rotated iron gates must retain their material and gold cost.");
				Sync.RunUnsynced(w, w.CancelInputMode);
			}
			var decorations = Decorations(w);
			if (tick > 350 && phase == 0 && decorations.Length == 14 && decorations.All(MedievalStockpile.Active))
			{
				Check(decorations.Count(a => a.Info.Name == "md.gildedfencey") == 3, "Vertical iron traces must not turn into wooden fencing.");
				Place(w, 13, 31, 31); Place(w, 14, 34, 31); phase = 1;
				Sync.RunUnsynced(w, w.CancelInputMode);
			}
			if (phase == 1 && decorations.Length == 16 && decorations.All(MedievalStockpile.Active))
			{
				foreach (var a in decorations)
				{
					var b = a.Trait<MedievalBuilding>();
					Check(b.Info.Cost[4] > 0 && b.Supplied, "Every gilded decoration must incorporate its exact gold cost.");
				}
				var mobile = walker.Trait<Mobile>();
				Check(mobile.CanEnterCell(Cell(32, 36), check: BlockedByActor.Immovable), "Iron entrance must remain passable.");
				Check(mobile.CanEnterCell(Cell(30, 30), check: BlockedByActor.Immovable), "Golden paving must remain walkable.");
				Check(!mobile.CanEnterCell(Cell(31, 36), check: BlockedByActor.Immovable), "Iron rails must block movement.");
				Check(!mobile.CanEnterCell(Cell(31, 31), check: BlockedByActor.Immovable), "Statues on paving must block movement.");
				Check(MedievalDecorations.At(w, w.LocalPlayer, Cell(29, 36)).Trait<MedievalGardenFence>().Connections == 5, "Iron corner must join both axes.");
				Check(MedievalDecorations.At(w, w.LocalPlayer, Cell(31, 36)).Trait<MedievalGardenFence>().Connections == 3, "Iron fence must connect to its gate.");
				Place(w, 17, 29, 36, 31, 36);
				Sync.RunUnsynced(w, w.CancelInputMode);
				w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(32, 34)), false));
				renderer.Viewport.AdjustZoom((float)Math.Log(1.5f / renderer.Viewport.Zoom));
				phase = 2; finishAt = tick + 250;
			}
			if (phase == 2 && tick == finishAt)
			{
				Check(decorations.Length == 16, "Redrawing an iron fence must not duplicate sites or charge gold again.");
				Check(walker.Location == Cell(32, 34), "Soldier must physically cross the open iron gate.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "gilded-garden.png"));
				Button(w, "REMOVE"); Click(w, Cell(34, 31));
				Sync.RunUnsynced(w, w.CancelInputMode);
				phase = 3; finishAt = tick + 60;
			}
			if ((phase == 3 && tick == finishAt) || tick == 10000) Finish(w);
		}
	}
}
