using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// Native rendering regression for placement at different zooms and camera positions.
	public class MedievalPlacementValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalPlacementValidation();
	}
	public sealed class MedievalPlacementValidation : IWorldLoaded, ITick
	{
		static readonly float[] Zooms = [.75f, 1f, 1.75f];
		static readonly string[] Cases = ["warehouse", "market", "field", "pasture", "stockyard", "path", "palisade", "tower", "trade"];
		readonly List<string> errors = [];
		readonly List<object> screenshots = [];
		WorldRenderer renderer;
		Actor warehouse, market, secondMarket;
		OrderGenerator generator;
		CPos first, last;
		CPos[] cells;
		string output, label;
		int tick, stage;
		bool finished;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string error) { if (!ok) errors.Add(label + ": " + error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "placement-preview", "ui-" + Game.Settings.Graphics.UIScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
			Directory.CreateDirectory(output);
			wr.Viewport.UnlockMinimumZoom(.5f);
		}
		void PointAt(World w, CPos cell)
		{
			Viewport.LastMousePos = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(w.Map.CenterOfCell(cell)));
			// Simulate pointer motion so a world tooltip cannot cover the sampled edges.
			Viewport.LastMoveRunTime = Game.RunTime;
			Check(renderer.Viewport.ViewToWorld(Viewport.LastMousePos) == cell, "Cursor must resolve to the intended terrain cell.");
		}
		Order[] Click(World w, CPos cell)
		{
			PointAt(w, cell);
			var pixel = Viewport.LastMousePos;
			return generator.Order(w, renderer.Viewport.ViewToWorld(pixel), renderer.Viewport.ViewToWorldPx(pixel),
				new MouseInput(MouseInputEvent.Down, generator.ActionButton, pixel, int2.Zero, Modifiers.None, 1)).ToArray();
		}
		void Begin(World w)
		{
			var kind = Cases[stage % Cases.Length];
			var zoomIndex = stage / Cases.Length;
			label = kind + "-zoom-" + Zooms[zoomIndex].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
			first = last = Cell(22, 28);
			var drag = false;
			switch (kind)
			{
				case "field": case "pasture":
					first = Cell(kind == "field" ? 27 : 32, 43); last = first + new CVec(2, 3); drag = true;
					generator = new MedievalZoneOrderGenerator(w, renderer, "md." + kind);
					cells = AgriculturalZone.Between(first, last).Cells().ToArray(); break;
				case "stockyard":
					last = first + new CVec(3, 3); drag = true;
					generator = new MedievalStockyardOrderGenerator(w, renderer, warehouse);
					cells = StockyardArea.Between(first, last).Cells().ToArray(); break;
				case "path": case "palisade":
					first = Cell(23, 31); last = first + new CVec(4, 0); drag = true;
					generator = kind == "path" ? new MedievalDecorationOrderGenerator(w, renderer, "md.stonepath")
						: new MedievalFortificationOrderGenerator(w, renderer, "md.palisade");
					cells = AgriculturalZone.Between(first, last).Cells().ToArray(); break;
				case "trade":
					first = last = market.Location;
					generator = new MedievalLogisticsTargetGenerator(w, renderer, secondMarket, "MedievalLinkMarkets");
					cells = market.Info.TraitInfo<BuildingInfo>().Tiles(first).ToArray(); break;
				default:
					var type = kind == "tower" ? "md.woodtower" : "md." + kind;
					generator = kind == "tower" ? new MedievalFortificationOrderGenerator(w, renderer, type)
						: new MedievalPlaceOrderGenerator(w, renderer, type);
					cells = w.Map.Rules.Actors[type].TraitInfo<BuildingInfo>().Tiles(first).ToArray(); break;
			}
			if (drag && zoomIndex == 1) (first, last) = (last, first);
			renderer.Viewport.AdjustZoom((float)Math.Log(Zooms[zoomIndex] / renderer.Viewport.Zoom));
			// Pan as well as zoom: the outlines must remain on their ground footprint.
			renderer.Viewport.Center(w.Map.CenterOfCell(cells[cells.Length / 2]) + new WVec(zoomIndex * 512, -512, 0));
			w.OrderGenerator = generator;
			if (drag) Check(Click(w, first).Length == 0, "The first corner must not place anything.");
			PointAt(w, last);
		}
		void Capture(World w)
		{
			var tool = (IOrderGenerator)generator;
			var sprites = tool.RenderAboveShroud(renderer, w).ToArray();
			var annotations = tool.RenderAnnotations(renderer, w).ToArray();
			Check(sprites.All(r => r is SpriteRenderable), "Screen-space lines must not enter the scaled world buffer.");
			Check(annotations.Length >= cells.Length * 4 && annotations.All(r => r is LineAnnotationRenderable), "Footprint lines must render in the annotation pass.");
			var points = new List<int[]>();
			foreach (var cell in cells)
			{
				var center = w.Map.CenterOfCell(cell);
				var corners = w.Map.Grid.Ramps[0].Corners;
				for (var i = 0; i < 4; i++)
				{
					Check(annotations.Any(r => r.Pos == center + corners[i]), "Missing corner of terrain cell " + cell);
					var midpoint = center + (corners[i] + corners[(i + 1) % 4]) / 2;
					var point = renderer.Viewport.WorldToViewPx(renderer.Screen3DPosition(midpoint));
					points.Add([(int)(point.X * Game.Renderer.WindowScale), (int)(point.Y * Game.Renderer.WindowScale)]);
				}
			}
			var filename = label + ".png";
			Game.Renderer.SaveScreenshot(Path.Combine(output, filename));
			screenshots.Add(new { filename, zoom = renderer.Viewport.Zoom, points });
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (finished || w.LocalPlayer == null) return;
			if (++tick == 1)
			{
				w.AddFrameEndTask(world =>
				{
					foreach (var a in world.Actors.Where(a => a.TraitOrDefault<MedievalAgriculturalArea>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
					warehouse = MedievalSettlement.Warehouses(world.LocalPlayer).First();
					market = world.Actors.First(a => a.Owner == world.LocalPlayer && a.Info.Name == "md.market");
					secondMarket = world.CreateActor("md.market", [new OwnerInit(world.LocalPlayer), new LocationInit(Cell(20, 35))]);
				});
				return;
			}
			if (tick < 10) return;
			var phase = (tick - 10) % 30;
			Sync.RunUnsynced(w, () =>
			{
				if (phase == 0) Begin(w);
				PointAt(w, last);
				if (phase == 15) Capture(w);
				if (phase != 29) return;
				var orders = Click(w, last);
				Check(orders.Length == 1, "The click on the preview must produce one valid order.");
				if (orders.Length == 1)
				{
					Check(orders[0].Target.Type == TargetType.Actor ? orders[0].Target.Actor == market
						: w.Map.CellContaining(orders[0].Target.CenterPosition) == first, "Order target must match the displayed first cell.");
					if (first != last) Check(orders[0].ExtraLocation == last, "The order must preserve the displayed opposite corner.");
				}
				w.CancelInputMode();
				if (++stage < Cases.Length * Zooms.Length) return;
				File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, cases = stage, errors, screenshots }, new JsonSerializerOptions { WriteIndented = true }));
				Console.WriteLine("MEDIEVAL PLACEMENT: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
				finished = true; Game.RunAfterDelay(1000, Game.Exit);
			});
		}
	}
}
