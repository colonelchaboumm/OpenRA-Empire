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
	public class MedievalFortificationValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFortificationValidation();
	}
	public sealed class MedievalFortificationValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, builtAt, completedAt, finishAt, gateOpenedAt;
		bool finished, carried, prematureShot, enemyWasHit, gateReclosed, screenshot;
		Actor walker, cart, enemy, enemyGateProbe, tower, gateActor, depot;
		Health enemyHealth;
		readonly List<string> errors = [];
		readonly HashSet<int> gateFrames = [];
		readonly HashSet<CPos> crossed = [];
		bool openScreenshot;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "fortification-preview");
			Directory.CreateDirectory(output);
		}
		void Check(bool valid, string message) { if (!valid) errors.Add(message); }
		Actor[] Fortifications(World w) => w.ActorsWithTrait<MedievalFortification>().Select(pair => pair.Actor).Where(a => a.Owner == w.LocalPlayer && a.IsInWorld && !a.IsDead).ToArray();
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
				count = orders.Length;
				foreach (var order in orders) w.IssueOrder(order);
			});
			return count;
		}
		void Wall(World w, int x, int y, int ex, int ey)
		{
			Button(w, "MD_FORT_0");
			Check(Click(w, Cell(x, y)) == 0, "First wall click must only anchor preview.");
			Check(Click(w, Cell(ex, ey)) == 1, "Second wall click must place a line.");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
			depot = Spawn("warehouse", 22, 30);
			depot.Trait<MedievalStockpile>().Stock.Add(0, 400);
			depot.Trait<MedievalStockpile>().Stock.Add(3, 100);
			Spawn("market", 23, 35).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn("house", 32, 32);
			walker = Spawn("militia", 31, 41);
			// This actor tests a path, not combat: it must not leave its destination to pursue the enemy probe.
			walker.Trait<AutoTarget>().SetStance(walker, UnitStance.HoldFire);
			cart = Spawn("supplycart", 32, 42);
			var enemyPlayer = w.Players.First(p => p.InternalName == "Creeps");
			w.LocalPlayer.EnemyPlayersMask = enemyPlayer.PlayerMask;
			enemyPlayer.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			enemy = w.CreateActor("md.forttarget", [new OwnerInit(enemyPlayer), new LocationInit(Cell(30, 25))]);
			enemyHealth = enemy.Trait<Health>();
			enemyGateProbe = w.CreateActor("md.forttarget", [new OwnerInit(enemyPlayer), new LocationInit(Cell(44, 40))]);
			// Creeps use InitialStanceAI: keep these geometry/target dummies passive for both player types.
			enemy.Trait<AutoTarget>().SetStance(enemy, UnitStance.HoldFire);
			enemyGateProbe.Trait<AutoTarget>().SetStance(enemyGateProbe, UnitStance.HoldFire);
		}
		void Finish(World w)
		{
			Check(completedAt > 0, "Builders must finish all fortifications.");
			Check(carried, "Builders must physically carry wood.");
			Check(!prematureShot, "Unfinished towers must never fire.");
			Check(enemyWasHit && enemy.IsDead, "A completed tower must automatically kill the enemy with visible arrows.");
			Check(walker.Location == Cell(31, 34), "Allied soldier must cross the automatic gate.");
			Check(cart.Location == Cell(32, 34), "A supply cart must cross the widened gate.");
			Check(crossed.Count >= 2, "Traffic must use at least two different gate lanes.");
			Check(gateOpenedAt > 0 && gateReclosed, "Gate must open for the soldier then close again.");
			Check(gateFrames.Count == 9, "All gate animation poses must play.");
			// Builders may return spare materials to the supply cart rather than the original depot.
			var inventories = w.ActorsHavingTrait<MedievalStockpile>().Where(a => a.Owner == w.LocalPlayer && MedievalStockpile.Active(a))
				.Select(a => new { actor = a.Info.Name, wood = a.Trait<MedievalStockpile>().Stock.Amount(0), stone = a.Trait<MedievalStockpile>().Stock.Amount(3) }).ToArray();
			var carriers = w.ActorsWithTrait<MedievalWorker>().Where(p => p.Actor.Owner == w.LocalPlayer && MedievalStockpile.Active(p.Actor)
				&& !p.Trait.CampCargo && !p.Trait.EquipmentCargo).Select(p => p.Trait).ToArray();
			var looseWood = inventories.Sum(s => s.wood) + carriers.Where(c => c.Resource == 0).Sum(c => c.Cargo);
			var looseStone = inventories.Sum(s => s.stone) + carriers.Where(c => c.Resource == 3).Sum(c => c.Cargo);
			Check(looseWood == 70 && looseStone == 100 && Fortifications(w).Sum(a => a.Trait<MedievalBuilding>().Materials.Amount(0)) == 330,
				"25 palisades + one wide gate + one tower must consume exactly 330 wood and no stone.");
			if (!screenshot) Game.Renderer.SaveScreenshot(Path.Combine(output, "fortifications.png"));
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, completedAt,
				carried, prematureShot, enemyWasHit, enemyDead = enemy.IsDead, gateOpenedAt, gateReclosed, gateFrames = gateFrames.Count,
				gateWidth = gateActor.Trait<Building>().Info.Dimensions.X, crossedLanes = crossed.Count,
				wood = depot.Trait<MedievalStockpile>().Stock.Amount(0), looseWood, looseStone, inventories, fortifications = Fortifications(w).Length,
				unfinished = Fortifications(w).Where(a => !MedievalStockpile.Active(a)).Select(a => new { a.Info.Name, location = a.Location.ToString() }).ToArray()
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL FORTIFICATIONS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (finished || w.LocalPlayer == null) return;
			tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 31)));
			if (tick == 20)
			{
				Button(w, "MD_DECOR_OPEN"); Button(w, "MD_FORT_OPEN");
				Check(Ui.Root.Get("MD_FORT_PANEL").IsVisible() && !Ui.Root.Get("MD_DECOR_PANEL").IsVisible(), "Palettes must not overlap.");
				Button(w, "MD_FORT_1"); Button(w, "MD_FORT_ROTATE");
				Check(((MedievalFortificationOrderGenerator)w.OrderGenerator).Kind == "md.woodgatey", "Gate rotation must change the wall axis.");
				Check(w.Map.Rules.Actors["md.woodgate"].TraitInfo<BuildingInfo>().Dimensions == new CVec(3, 1)
					&& w.Map.Rules.Actors["md.woodgatey"].TraitInfo<BuildingInfo>().Dimensions == new CVec(1, 3), "Rotation must rotate the three-cell footprint.");
				Wall(w, 28, 29, 35, 29); Wall(w, 28, 30, 28, 36); Wall(w, 35, 30, 35, 36);
				Wall(w, 29, 36, 29, 36); Wall(w, 33, 36, 34, 36);
				Button(w, "MD_FORT_1"); Check(Click(w, Cell(30, 36)) == 1, "Wide gate must place.");
				Button(w, "MD_FORT_2"); Check(Click(w, Cell(29, 30)) == 1, "Tower must place.");
				// Entire invalid line must be rejected at synchronization, without partial placement.
				w.IssueOrder(new Order("MedievalFortify", w.LocalPlayer.PlayerActor, Target.FromCell(w, Cell(20, 30)), false) { TargetString = "md.palisade", ExtraLocation = Cell(26, 30) });
				Button(w, "MD_FORT_CLOSE");
			}
			if (tick == 50)
			{
				Check(Fortifications(w).Length == 27, "All 27 constructions must exist; invalid batch must place none.");
				tower = MedievalFortifications.At(w, w.LocalPlayer, Cell(29, 30));
				gateActor = MedievalFortifications.At(w, w.LocalPlayer, Cell(30, 36));
				Check(!walker.Trait<Mobile>().CanEnterCell(Cell(29, 36), check: BlockedByActor.Immovable), "Palisades must block movement even while building.");
				Check(Enumerable.Range(30, 3).All(x => !MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.stonepath", Cell(x, 36))), "Every gate tile must prevent overlapping construction.");
				Wall(w, 28, 29, 35, 29);
				Sync.RunUnsynced(w, w.CancelInputMode);
			}
			if (tick == 80)
			{
				Check(Fortifications(w).Length == 27, "Redrawing a wall must not duplicate actors or costs.");
				w.AddFrameEndTask(world =>
				{
					for (var i = 0; i < 8; i++) world.CreateActor("md.builder", [new OwnerInit(world.LocalPlayer), new LocationInit(Cell(24 + i, 39))]);
				});
			}
			carried |= w.Actors.Any(a => a.TraitOrDefault<MedievalWorker>()?.Cargo > 0);
			if (tower != null && !MedievalStockpile.Active(tower)) prematureShot |= enemyHealth.HP < enemyHealth.MaxHP;
			enemyWasHit |= enemyHealth.HP < enemyHealth.MaxHP;
			if (gateActor != null && MedievalStockpile.Active(gateActor))
			{
				var gate = gateActor.Trait<Gate>();
				gateFrames.Add(gateActor.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.CurrentFrame);
				if (completedAt > 0 && gate.Position == gate.OpenPosition && gateOpenedAt == 0) gateOpenedAt = tick;
				if (gateOpenedAt > 0 && gate.Position == 0) gateReclosed = true;
				if (completedAt > 0)
				{
					foreach (var a in new[] { walker, cart })
						if (gate.Footprint.Contains(a.Location)) crossed.Add(a.Location);
					if (!openScreenshot && gate.Position == gate.OpenPosition && gateOpenedAt < tick - 2)
					{
						Game.Renderer.SaveScreenshot(Path.Combine(output, "gate-open.png")); openScreenshot = true;
					}
				}
			}
			if (tick > 100 && builtAt == 0 && Fortifications(w).All(MedievalStockpile.Active)) builtAt = tick;
			// Wall connectors and their render frames update after the completion tick.
			if (builtAt > 0 && tick >= builtAt + 10 && completedAt == 0 && gateActor.Trait<Gate>().Position == 0)
			{
				completedAt = tick;
				Check(Fortifications(w).Length == 27, "Only the requested defenses may be present.");
				Check(Enumerable.Range(30, 3).All(x => !enemyGateProbe.Trait<Mobile>().CanEnterCell(Cell(x, 36), check: BlockedByActor.Immovable)), "A closed gate must block enemies across all three cells.");
				Check(MedievalFortifications.At(w, w.LocalPlayer, Cell(29, 36)).TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.CurrentFrame == 10
					&& MedievalFortifications.At(w, w.LocalPlayer, Cell(33, 36)).TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.CurrentFrame == 10,
					"Both palisade ends must connect to the widened gate.");
				w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(31, 34)), false));
				w.IssueOrder(new Order("Move", cart, Target.FromCell(w, Cell(32, 34)), false));
				finishAt = tick + 1000;
				renderer.Viewport.AdjustZoom((float)Math.Log(1.5f / renderer.Viewport.Zoom));
			}
			if (tick == finishAt - 40)
			{
				Button(w, "MD_FORT_OPEN");
				Check(Ui.Root.Get("MD_FORT_PANEL").IsVisible(), "Defense catalogue must remain accessible after construction.");
			}
			if (tick == finishAt - 20)
			{
				Game.Renderer.SaveScreenshot(Path.Combine(output, "fortifications.png")); screenshot = true;
			}
			if (tick == finishAt || tick == 10000) Finish(w);
		}
	}
}
