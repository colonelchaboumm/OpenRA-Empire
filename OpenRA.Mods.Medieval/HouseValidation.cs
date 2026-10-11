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
	public class MedievalHouseValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalHouseValidation();
	}
	public sealed class MedievalHouseValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, completedAt;
		bool finished, carried;
		Actor depot;
		readonly List<string> errors = [];
		readonly Dictionary<CPos, int> expected = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "house-preview");
			Directory.CreateDirectory(output);
		}
		void Check(bool valid, string message) { if (!valid && !errors.Contains(message)) errors.Add(message); }
		Actor[] Houses(World w) => w.Actors.Where(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.house" && expected.ContainsKey(a.Location)).ToArray();
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
			depot = Spawn("warehouse", 22, 29);
			depot.Trait<MedievalStockpile>().Stock.Add(0, 400); depot.Trait<MedievalStockpile>().Stock.Add(3, 300);
			Spawn("market", 24, 35).Trait<MedievalStockpile>().Stock.Add(1, 200);
		}
		void Place(World w, CPos cell)
		{
			var style = HouseStyleDeck.Next(w.LocalPlayer);
			expected.Add(cell, style);
			Sync.RunUnsynced(w, () =>
			{
				Ui.Root.Get<ButtonWidget>("MD_BUILD_0").OnClick();
				var tool = (MedievalPlaceOrderGenerator)w.OrderGenerator;
				Check(tool.HousePreviewText(w).Contains(HouseStyleDeck.Names[style], StringComparison.Ordinal), "Placement text must name the chosen architecture.");
				var orders = tool.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, tool.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
				Check(orders.Length == 1, "House button and terrain click must place a construction.");
				foreach (var order in orders) w.IssueOrder(order);
				Check(Ui.Root.Get<ButtonWidget>("MD_BUILD_0").IsDisabled(), "A second house must wait for acknowledgement, preventing a stale preview on rapid clicks.");
			});
		}
		void Finish(World w)
		{
			var houses = Houses(w);
			Check(houses.Length == 6 && houses.All(MedievalStockpile.Active), "Six real houses must finish construction.");
			Check(houses.Select(a => a.Trait<MedievalHouseAppearance>().Style).Distinct().Count() == 6, "The built neighborhood must contain all six random styles.");
			Check(carried, "Builders must carry the real construction materials.");
			Check(depot.Trait<MedievalStockpile>().Stock.Amount(0) == 40 && depot.Trait<MedievalStockpile>().Stock.Amount(3) == 180, "Six houses must cost exactly 360 wood and 120 stone.");
			Check(MedievalSettlement.Housing(w.LocalPlayer) == 60, "Every completed style must provide eight places plus the depot's twelve.");
			var heights = new Dictionary<int, int>();
			foreach (var house in houses)
			{
				var appearance = house.Trait<MedievalHouseAppearance>();
				Check(house.Trait<MedievalChimney>().Smoke.Puffs.Count > 0, "Every completed style must emit chimney smoke.");
				heights[appearance.Style] = house.Trait<IsometricSelectable>().Bounds(house, renderer).BoundingRect.Height;
				Check(house.Info.TraitInfo<BuildingInfo>().OccupiedTiles(house.Location).Count() == 4, "All visual styles must reserve the same four plot cells.");
			}
			Check(heights.GetValueOrDefault(2) > heights.GetValueOrDefault(1), "The tower's real mouse selection must be taller than the cottage's.");
			Game.Renderer.SaveScreenshot(Path.Combine(output, "neighborhood.png"));
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, completedAt, errors, carried,
				styles = houses.Select(a => new { style = a.Trait<MedievalHouseAppearance>().Name, id = a.Trait<MedievalHouseAppearance>().Style,
					chimney = a.Trait<MedievalHouseAppearance>().ChimneyOffset.ToString(), frame = a.Trait<WithMedievalHouseBody>().DefaultAnimation.CurrentFrame }).ToArray(), heights
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL HOUSES: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (finished || w.LocalPlayer == null) return;
			tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			// Keep the whole neighborhood left of the command sidebar.
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(33, 30)));
			if (tick >= 20 && tick <= 120 && tick % 20 == 0)
			{
				var i = (tick - 20) / 20;
				Place(w, Cell(28 + i % 3 * 5, 28 + i / 3 * 6));
			}
			if (tick == 150)
			{
				var before = HouseStyleDeck.Next(w.LocalPlayer);
				Sync.RunUnsynced(w, () =>
				{
					Ui.Root.Get<ButtonWidget>("MD_BUILD_0").OnClick();
					var tool = (OrderGenerator)w.OrderGenerator;
					var orders = tool.Order(w, depot.Location, int2.Zero, new MouseInput(MouseInputEvent.Down, tool.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
					Check(orders.Length == 0, "An occupied plot must reject the placement click.");
					w.CancelInputMode();
				});
				Check(w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().HousesPlaced == 6 && HouseStyleDeck.Next(w.LocalPlayer) == before, "Invalid or cancelled input must not consume a house style.");
			}
			if (tick == 165)
			{
				w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor, Target.FromCell(w, depot.Location), false) { TargetString = "md.house" });
			}
			if (tick == 180)
			{
				Check(w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().HousesPlaced == 6, "A rejected synchronized order must not advance the style deck.");
				w.AddFrameEndTask(world =>
				{
					for (var i = 0; i < 6; i++) world.CreateActor("md.builder", [new OwnerInit(world.LocalPlayer), new LocationInit(Cell(27 + i, 41))]);
				});
			}
			carried |= w.Actors.Any(a => a.TraitOrDefault<MedievalWorker>()?.Cargo > 0);
			foreach (var house in Houses(w))
			{
				Check(house.Trait<MedievalHouseAppearance>().Style == expected[house.Location], "House architecture must stay identical to the chosen preview throughout construction.");
				if (!MedievalStockpile.Active(house)) Check(house.Trait<MedievalChimney>().Smoke.Puffs.Count == 0, "Unfinished houses must not smoke.");
				else Check(house.Trait<WithMedievalHouseBody>().DefaultAnimation.CurrentFrame == expected[house.Location], "Finished houses must display a fixed style, never cycle through the sprite sheet.");
			}
			if (completedAt == 0 && tick > 100 && Houses(w).Length == 6 && Houses(w).All(MedievalStockpile.Active)) completedAt = tick;
			if (completedAt > 0 && tick == completedAt + 80)
			{
				renderer.Viewport.AdjustZoom((float)Math.Log(1.5f / renderer.Viewport.Zoom));
				Sync.RunUnsynced(w, () => w.Selection.Clear());
			}
			if (completedAt > 0 && tick == completedAt + 120 || tick == 9000) Finish(w);
		}
	}
}
