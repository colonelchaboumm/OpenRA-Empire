using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalDemolitionAreaValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDemolitionAreaValidation();
	}
	// Opt-in native scenario: real mouse gestures, serialized orders and haulers only.
	public sealed class MedievalDemolitionAreaValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		MedievalDemolitionOrderGenerator generator;
		string output;
		Actor stall, outside, enemy, tree, citizen;
		readonly List<Actor> targets = [], haulers = [];
		readonly List<string> errors = [], checks = [];
		readonly int[] expected = new int[StorageGoods.Count], costs = new int[Inventory.ResourceCount];
		int tick;
		bool done, worked, carried, unloaded;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string text) { checks.Add(text); if (!ok) errors.Add(text); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "demolition-area", "native"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, int x, int y, Player owner = null) => w.CreateActor("md." + type,
			[new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y)), new MedievalStockInit([0, 0, 0, 0, 0])]);
		void Track(Actor a)
		{
			targets.Add(a);
			for (var r = 0; r < costs.Length; r++) costs[r] += a.Trait<MedievalBuilding>().Info.Cost.ElementAtOrDefault(r);
			for (var g = 0; g < expected.Length; g++) expected[g] += StorageGoods.Amount(a, g);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.")).ToArray()) a.Dispose();
			w.LocalPlayer.Shroud.Disabled = true;
			stall = Spawn(w, "marketstall", 21, 29);
			stall.Trait<MedievalStockpile>().Stock.SetCapacity([500, 0, 0, 500, 500]);
			stall.Trait<MedievalEquipmentStore>().Stock.SetCapacity([500, 500, 500, 0, 0]);
			stall.Trait<MedievalCampStore>().Stock.SetCapacity([500, 500, 0, 0, 0]);
			outside = Spawn(w, "house", 24, 24);
			enemy = Spawn(w, "house", 35, 28, w.Players.Single(p => p.InternalName == "Rival"));
			tree = Spawn(w, "tree", 30, 30, w.Players.Single(p => p.InternalName == "Neutral"));
			citizen = Spawn(w, "farmer", 30, 32);
			Track(Spawn(w, "house", 27, 29)); // Only the eastern half lies in the drag.
			for (var y = 34; y <= 35; y++) for (var x = 29; x <= 34; x++) Track(Spawn(w, "stonepath", x, y));
			Track(Spawn(w, "gardenfence", 31, 28)); Track(Spawn(w, "gildedknight", 33, 29));
			var warehouse = Spawn(w, "warehouse", 34, 31); var contents = new[] { 3, 0, 0, 7, 11, 2, 3, 1, 4, 1 };
			for (var g = 0; g < contents.Length; g++) StorageGoods.Stock(warehouse, g).Add(StorageGoods.Index(g), contents[g]);
			Track(warehouse);
			for (var r = 0; r < costs.Length; r++) expected[r] += costs[r] / 5;
			Sync.RunUnsynced(w, () => { w.Selection.Clear(); renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 30))); });
		}
		void Open(World w) => Sync.RunUnsynced(w, () =>
		{
			Ui.Root.Get<ButtonWidget>("MD_MENU_REMOVE").OnClick(); generator = (MedievalDemolitionOrderGenerator)w.OrderGenerator;
		});
		Order[] Mouse(World w, CPos cell, MouseInputEvent type, bool cancel = false)
		{
			Order[] orders = [];
			Sync.RunUnsynced(w, () =>
			{
				var pixel = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(w.Map.CenterOfCell(cell)));
				Viewport.LastMousePos = pixel; Viewport.LastMoveRunTime = Game.RunTime;
				Check(renderer.Viewport.ViewToWorld(pixel) == cell, "Pointer resolves to the intended isometric cell");
				orders = generator.Order(w, renderer.Viewport.ViewToWorld(pixel), renderer.Viewport.ViewToWorldPx(pixel),
					new MouseInput(type, cancel ? generator.CancelButton : generator.ActionButton, pixel, int2.Zero, Modifiers.None, 1)).ToArray();
			});
			return orders;
		}
		void Resolve(World w, IEnumerable<Order> orders)
		{
			foreach (var order in orders)
			{
				using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
				var received = Order.Deserialize(w, reader);
				foreach (var trait in received.Subject.TraitsImplementing<IResolveOrder>()) trait.ResolveOrder(received.Subject, received);
			}
		}
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		void Finish(World w)
		{
			Check(targets.All(a => !a.IsInWorld), "All selected buildings, paths and fences are dismantled");
			Check(outside.IsInWorld && !MedievalDemolition.Pending(outside) && enemy.IsInWorld && !MedievalDemolition.Pending(enemy)
				&& tree.IsInWorld && citizen.IsInWorld, "Outside buildings, enemies, nature and citizens remain intact");
			Check(worked && carried && unloaded && !w.ActorsWithTrait<MedievalWorker>().Any(p => p.Trait.Info.Profession == "builder"), "Haulers dismantle and physically recover materials without any builder");
			var actual = Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Amount(stall, g)).ToArray();
			Check(actual.SequenceEqual(expected), "Exactly 20 percent plus every stored item reaches the unlinked neighborhood stall");
			Check(!w.ActorsHavingTrait<MedievalSalvage>().Any() && haulers.All(a => a.Trait<MedievalWorker>().Cargo == 0), "No residual cargo or salvage remains");
			Check(Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Stock(stall, g).Reserved(StorageGoods.Index(g)) == 0 && StorageGoods.Stock(stall, g).Incoming(StorageGoods.Index(g)) == 0), "All transport reservations are released");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, checks, tick, worked, carried, unloaded, actual, expected }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL DEMOLITION AREA: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Shot("03-area-cleared-materials-stored"); Game.RunAfterDelay(700, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 10)
			{
				Open(w);
				foreach (var zoom in new[] { .8f, 1.25f })
				{
					Sync.RunUnsynced(w, () => renderer.Viewport.AdjustZoom((float)Math.Log(zoom / renderer.Viewport.Zoom)));
					Check(Mouse(w, Cell(28, 27), MouseInputEvent.Down).Length == 0, "Pressing the mouse does not demolish anything");
					Check(Mouse(w, Cell(37, 36), MouseInputEvent.Move).Length == 0, "Moving the mouse only previews the zone");
					var orders = Mouse(w, Cell(37, 36), MouseInputEvent.Up);
					Check(orders.Select(o => o.Target.Actor).SequenceEqual(targets.OrderBy(a => a.ActorID)), "Drag selects each intersecting owned footprint once, at zoom " + zoom);
					// Deliberately do not resolve this preview/zoom check.
				}
				Mouse(w, Cell(28, 27), MouseInputEvent.Down); Mouse(w, Cell(37, 36), MouseInputEvent.Up, cancel: true);
				Check(Mouse(w, Cell(37, 36), MouseInputEvent.Up).Length == 0 && w.OrderGenerator != generator, "Right click cancels a drag without orders");
				Open(w); Mouse(w, outside.Location, MouseInputEvent.Down); var click = Mouse(w, outside.Location, MouseInputEvent.Up); Resolve(w, click);
				Check(click.Length == 1 && MedievalDemolition.Pending(outside), "A simple click still queues one building");
				Mouse(w, outside.Location, MouseInputEvent.Down); Resolve(w, Mouse(w, outside.Location, MouseInputEvent.Up));
				Check(!MedievalDemolition.Pending(outside), "A second simple click cancels one building");
				Mouse(w, Cell(28, 27), MouseInputEvent.Down); w.SetLocalPauseState(true);
				Check(Mouse(w, Cell(37, 36), MouseInputEvent.Up).Length == 0, "Pausing during a drag prevents demolition orders"); w.SetLocalPauseState(false);
				Mouse(w, Cell(37, 36), MouseInputEvent.Down); Mouse(w, Cell(28, 27), MouseInputEvent.Move);
			}
			if (tick == 30) Shot("01-demolition-area-preview");
			if (tick == 35)
			{
				var orders = Mouse(w, Cell(28, 27), MouseInputEvent.Up);
				Check(orders.Length == targets.Count && orders.All(o => o.ExtraData == 0), "Reverse drag queues the same targets"); Resolve(w, orders);
				Mouse(w, Cell(28, 27), MouseInputEvent.Down);
				Check(Mouse(w, Cell(37, 36), MouseInputEvent.Up).Length == 0 && targets.All(MedievalDemolition.Pending), "Overlapping zones never cancel an existing demolition");
				Mouse(w, Cell(42, 38), MouseInputEvent.Down);
				Check(Mouse(w, Cell(43, 39), MouseInputEvent.Up).Length == 0, "Empty zones issue no orders");
			}
			if (tick == 60)
			{
				Check(targets.All(a => a.Trait<MedievalDemolition>().Progress == 0), "Area selection waits for real workers");
				// The empty-zone gesture ended near the screen edge. Move the test
				// pointer away so normal edge scrolling does not hide the workers.
				Sync.RunUnsynced(w, () =>
				{
					renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 30)));
					Viewport.LastMousePos = new int2(500, 400);
				});
				w.AddFrameEndTask(_ => { for (var i = 0; i < 4; i++) haulers.Add(Spawn(w, "hauler", 24 + i, 33)); });
			}
			worked |= haulers.Any(a => a.Trait<MedievalWorker>().IsDemolishing);
			carried |= haulers.Any(a => a.Trait<MedievalWorker>().Cargo > 0);
			unloaded |= haulers.Any(a => a.Trait<MedievalWorker>() is { HandlingStock: true, HandlingPickup: false, HandlingCommitted: true });
			if (tick == 400) Shot("02-haulers-dismantling");
			if (tick > 60 && targets.All(a => !a.IsInWorld) && !w.ActorsHavingTrait<MedievalSalvage>().Any() && haulers.All(a => a.Trait<MedievalWorker>().Cargo == 0)) Finish(w);
			else if (tick > 6000) Finish(w);
			if (tick % 500 == 0) Console.WriteLine($"DEMOLITION AREA tick {tick}, remaining {targets.Count(a => a.IsInWorld)}, piles {w.ActorsHavingTrait<MedievalSalvage>().Count()}, jobs {string.Join("; ", haulers.Select(a => a.Trait<MedievalWorker>().Description))}");
		}
	}
}
