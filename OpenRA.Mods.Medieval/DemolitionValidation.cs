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
	public class MedievalDemolitionValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDemolitionValidation();
	}
	public sealed class MedievalDemolitionValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor depot, house, enemy, tower, archer, store, cancelled, doomed, gilded, unfinished;
		readonly List<Actor> targets = [], builders = [];
		readonly List<string> errors = [], checks = [];
		readonly int[] expected = new int[StorageGoods.Count], costs = new int[Inventory.ResourceCount];
		int tick, stage, until;
		bool done, worked, carried, unloaded, interrupted;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool condition, string text) { checks.Add(text); if (!condition) errors.Add(text); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "demolition", "native"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, int x, int y, Player owner = null, bool construction = false)
		{
			var init = new OpenRA.Primitives.TypeDictionary { new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y)), new MedievalStockInit([0, 0, 0, 0, 0]) };
			if (construction) init.Add(new MedievalConstructionInit());
			var a = w.CreateActor("md." + type, init); a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); return a;
		}
		void Order(World w, Actor subject, string name, Actor target, uint data = 0)
		{
			using var reader = new BinaryReader(new MemoryStream(new Order(name, subject, Target.FromActor(target), false) { ExtraData = data }.Serialize()));
			var received = OpenRA.Order.Deserialize(w, reader);
			foreach (var trait in subject.TraitsImplementing<IResolveOrder>()) trait.ResolveOrder(subject, received);
		}
		void Mark(World w, Actor a, bool cancel = false) => Order(w, w.LocalPlayer.PlayerActor, "MedievalDemolish", a, cancel ? 1u : 0u);
		void Track(Actor a)
		{
			targets.Add(a); var b = a.Trait<MedievalBuilding>();
			for (var r = 0; r < costs.Length; r++) if (b.Complete) costs[r] += b.Info.Cost.ElementAtOrDefault(r); else expected[r] += b.Materials.Amount(r);
			for (var g = 0; g < expected.Length; g++) expected[g] += StorageGoods.Amount(a, g);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.")).ToArray()) a.Dispose();
			w.LocalPlayer.Shroud.Disabled = true;
			depot = Spawn(w, "warehouse", 21, 29); depot.Trait<MedievalStockpile>().Stock.SetCapacity([0, 0, 0, 0, 0]);
			house = Spawn(w, "house", 28, 29); Track(house);
			enemy = Spawn(w, "house", 37, 21, w.Players.Single(p => p.InternalName == "Rival"));
			cancelled = Spawn(w, "house", 28, 24); doomed = Spawn(w, "house", 42, 42);
			tower = Spawn(w, "woodtower", 37, 32); archer = Spawn(w, "archer", 37, 35);
			Order(w, archer, "EnterTransport", tower);
			Sync.RunUnsynced(w, () => { w.Selection.Clear(); renderer.Viewport.AdjustZoom((float)Math.Log(1.2f / renderer.Viewport.Zoom)); });
		}
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		void Finish(World w)
		{
			Check(stage == 5, "All demolition scenarios complete");
			Check(worked && carried && unloaded, "Builders visibly dismantle, carry real goods and use the stock handling animation");
			Check(enemy.IsInWorld && cancelled.IsInWorld && !cancelled.Trait<MedievalDemolition>().Requested, "Enemy and cancelled buildings remain intact");
			Check(archer.IsInWorld && !archer.IsDead && archer.Trait<Passenger>().Transport == null, "The tower's archer survives normal evacuation");
			var actual = Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Amount(depot, g)).ToArray();
			Check(actual.SequenceEqual(expected), "Only 20 percent of construction costs plus all stored and unused goods reach the depot");
			Check(Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Stock(depot, g).Reserved(StorageGoods.Index(g)) == 0 && StorageGoods.Stock(depot, g).Incoming(StorageGoods.Index(g)) == 0), "All stock reservations are released");
			Check(!w.ActorsHavingTrait<MedievalSalvage>().Any(), "Empty salvage piles are cleared");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, checks, tick, stage, worked, carried, unloaded, actual, expected }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL DEMOLITION: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 29))));
			worked |= builders.Any(a => a.Trait<MedievalWorker>() is { IsWorking: true, WorkTarget: { IsInWorld: true, IsDead: false } target } && MedievalDemolition.Pending(target));
			carried |= builders.Any(a => a.Trait<MedievalWorker>().Cargo > 0);
			unloaded |= builders.Any(a => a.Trait<MedievalWorker>() is { HandlingStock: true, HandlingPickup: false, HandlingCommitted: true });
			if (tick == 10)
			{
				Sync.RunUnsynced(w, () =>
				{
					for (var c = 0; c < 6; c++)
					{
						Ui.Root.Get<ButtonWidget>("MD_MENU_TAB_" + c).OnClick(); var button = Ui.Root.Get<ButtonWidget>("MD_MENU_REMOVE");
						Check(button.IsVisible(), "Demolition tool visible in category " + c); button.OnClick(); Check(w.OrderGenerator is MedievalDemolitionOrderGenerator, "Demolition tool opens in category " + c);
					}
					Ui.Root.Get<ButtonWidget>("MD_MENU_TAB_0").OnClick(); Ui.Root.Get<ButtonWidget>("MD_MENU_REMOVE").OnClick();
				});
				Mark(w, house); Mark(w, house); Mark(w, enemy); Mark(w, cancelled); Mark(w, cancelled, true); Mark(w, doomed); doomed.Kill(doomed);
			}
			if (tick == 70)
			{
				Check(house.IsInWorld && house.Trait<MedievalDemolition>().Progress == 0 && !w.ActorsHavingTrait<MedievalSalvage>().Any(), "No builders means no demolition or instant refund");
				Check(!enemy.Trait<MedievalDemolition>().Requested && !cancelled.Trait<MedievalDemolition>().Requested, "Enemy orders are rejected and cancellation works");
				Shot("01-demolition-tool-marked-building");
				w.AddFrameEndTask(_ => { for (var i = 0; i < 3; i++) builders.Add(Spawn(w, "builder", 24 + i, 32)); }); stage = 1;
			}
			if (stage == 1 && house.IsInWorld && house.Trait<MedievalDemolition>().Progress >= 3 && !interrupted)
			{
				Mark(w, house, true); Check(MedievalStockpile.Active(house) && house.Trait<MedievalDemolition>().Progress == 0, "Cancellation during work restores the building without resource creation");
				Mark(w, house); interrupted = true; Shot("02-builders-dismantling");
			}
			if (stage == 1 && !house.IsInWorld) { stage = 2; until = tick + 80; }
			if (stage == 2 && tick >= until)
			{
				var pile = w.ActorsHavingTrait<MedievalSalvage>().Single();
				Check(MedievalConstructionSupply.Sources(w.LocalPlayer, house.CenterPosition).Contains(pile), "Salvage can also supply nearby construction directly when no depot has room");
				Check(StorageGoods.Amount(pile, 0) == 12 && StorageGoods.Amount(pile, 3) == 4 && builders.All(a => a.Trait<MedievalWorker>().Cargo == 0), "Full depots leave exactly the recoverable materials on the ground");
				Shot("03-salvage-waits-for-storage"); depot.Trait<MedievalStockpile>().Stock.SetCapacity([1000, 1000, 1000, 1000, 1000]);
				stage = 3;
			}
			if (stage == 3 && !w.ActorsHavingTrait<MedievalSalvage>().Any() && builders.All(a => a.Trait<MedievalWorker>().Cargo == 0))
			{
				Check(StorageGoods.Amount(depot, 0) == 12 && StorageGoods.Amount(depot, 3) == 4, "Three builders return house salvage exactly once");
				Check(tower.Trait<Cargo>().PassengerCount == 1, "Tower contains a real garrison before demolition");
				w.AddFrameEndTask(_ =>
				{
					store = Spawn(w, "warehouse", 32, 27); var contents = new[] { 13, 17, 9, 7, 11, 2, 3, 1, 4, 1 };
					for (var g = 0; g < contents.Length; g++) StorageGoods.Stock(store, g).Add(StorageGoods.Index(g), contents[g]); Track(store);
					gilded = Spawn(w, "gildedknight", 33, 34); Track(gilded); Track(tower);
					for (var i = 0; i < 5; i++) { var road = Spawn(w, "stonepath", 27 + i, 35); Track(road); }
					unfinished = Spawn(w, "house", 26, 20, construction: true); unfinished.Trait<MedievalBuilding>().Materials.Add(0, 7); unfinished.Trait<MedievalBuilding>().Materials.Add(3, 3); Track(unfinished);
					foreach (var a in targets.Where(a => a.IsInWorld)) { Mark(w, a); Mark(w, a); }
					for (var r = 0; r < costs.Length; r++) expected[r] += costs[r] / 5;
				}); stage = 4;
			}
			if (stage == 4 && targets.Count > 1 && targets.All(a => !a.IsInWorld) && !w.ActorsHavingTrait<MedievalSalvage>().Any() && builders.All(a => a.Trait<MedievalWorker>().Cargo == 0))
			{ stage = 5; until = tick + 40; Shot("04-demolition-complete-materials-returned"); }
			if (stage == 5 && tick >= until) Finish(w);
			if (tick % 500 == 0) Console.WriteLine($"DEMOLITION stage {stage}, tick {tick}, piles {w.ActorsHavingTrait<MedievalSalvage>().Count()}, jobs {string.Join("; ", builders.Select(a => a.Trait<MedievalWorker>().Description))}");
			if (tick > 6000) Finish(w);
		}
	}
}
