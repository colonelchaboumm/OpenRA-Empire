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
	public class MedievalSaboteurValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSaboteurValidation();
	}
	public sealed class MedievalSaboteurValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Player enemy;
		Actor market, barracks, warehouse, house, saboteur, cow, stolenCow, ownPen, enemyPen, militia;
		MedievalSaboteur sabotage;
		MedievalHerding herding;
		int tick, phase, since, savedHP, yardHP;
		bool done, recruited, hidden, revealed, cancelled, targetDestroyed, retreated, burned, killed, led, stolen, yardBurned, detected, intercepted;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) { errors.Add(error); Console.WriteLine("SABOTEUR CHECK: " + error); } }
		Actor Spawn(World w, string type, int x, int y, Player p = null) => w.CreateActor("md." + type, [new OwnerInit(p ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, false));
		void Next() { phase++; since = tick; Console.WriteLine($"SABOTEUR PHASE {phase} at {tick}"); }
		void Click(World w, int action, CPos cell)
		{
			Select(w, saboteur);
			Sync.RunUnsynced(w, () =>
			{
				var button = Ui.Root.Get<ButtonWidget>("MD_SABOTEUR_ACTION_" + action);
				Check(button.IsVisible() && !button.IsDisabled(), "Saboteur action button must be usable."); button.OnClick();
				var generator = (OrderGenerator)w.OrderGenerator;
				foreach (var o in generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1))) w.IssueOrder(o);
			});
		}
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "saboteur"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"status\":\"running\"}");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			enemy = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = enemy.PlayerMask; enemy.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			w.LocalPlayer.Shroud.Disabled = true; enemy.Shroud.Disabled = true;
			market = Spawn(w, "market", 24, 24); market.Trait<MedievalStockpile>().Stock.Add(1, 180); market.Trait<MedievalEquipmentStore>().Stock.Add(1, 1);
			Spawn(w, "house", 22, 24); barracks = Spawn(w, "barracks", 24, 29);
			var recruiter = Spawn(w, "recruiter", 25, 33); barracks.Trait<MedievalMilitaryWorkshop>().AssignStaff(barracks, recruiter);
			Spawn(w, "ranch", 28, 38); ownPen = Spawn(w, "pasture.4x4", 28, 42);
			warehouse = Spawn(w, "warehouse", 39, 29, enemy); house = Spawn(w, "house", 44, 26, enemy);
			foreach (var r in new[] { 0, 1, 2, 3 }) warehouse.Trait<MedievalStockpile>().Stock.Add(r, 80);
			Spawn(w, "ranch", 45, 38, enemy); enemyPen = Spawn(w, "pasture.4x4", 43, 42, enemy);
			cow = Spawn(w, "cow", 44, 44, enemy); stolenCow = Spawn(w, "cow", 45, 44, enemy);
			enemyPen.Trait<MedievalLivestock>().Enter(cow); enemyPen.Trait<MedievalLivestock>().Enter(stolenCow);
			Select(w, barracks);
			Sync.RunUnsynced(w, () =>
			{
				var button = Ui.Root.Get<ButtonWidget>("MD_BARRACKS_SABOTEUR");
				Check(button.IsVisible() && !button.IsDisabled(), "Caserne must expose the new recruit button."); button.OnClick();
			});
		}
		void Finish(World w)
		{
			Check(recruited && hidden && revealed && cancelled && targetDestroyed && retreated && burned && killed && led && stolen && yardBurned && detected && intercepted, "All infiltration, fire, livestock and militia stages must complete.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, phase,
				recruited, hidden, revealed, cancelled, targetDestroyed, retreated, burned, killed, led, stolen, yardBurned, detected, intercepted }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SABOTEUR: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(35, 35))));
			if (tick % 500 == 0) Console.WriteLine($"SABOTEUR {tick}, phase {phase}: {sabotage?.Status}; {herding?.Status}");
			if (tick == 35) Game.Renderer.SaveScreenshot(Path.Combine(output, "barracks.png"));
			if (phase == 0 && tick == 200)
			{
				var workshop = barracks.Trait<MedievalMilitaryWorkshop>();
				Check(workshop.Recruit == 10 && workshop.Progress == 0 && !w.ActorsHavingTrait<MedievalSaboteur>().Any(), "Recruitment must wait for the mandatory meat delivery.");
				market.Trait<MedievalStockpile>().Stock.Add(2, 10); Next();
			}
			else if (phase == 1 && w.ActorsHavingTrait<MedievalSaboteur>().FirstOrDefault() is { } trained)
			{
				saboteur = trained; sabotage = saboteur.Trait<MedievalSaboteur>(); herding = saboteur.Trait<MedievalHerding>(); recruited = true;
				Check(MedievalSettlement.Population(w.LocalPlayer) == 2 && saboteur.TraitOrDefault<MedievalNeedsFood>() != null && saboteur.TraitOrDefault<MedievalRest>() != null, "Saboteur must use housing, food and military fatigue.");
				Check(market.Trait<MedievalEquipmentStore>().Stock.Amount(1) == 0 && barracks.Trait<MedievalMilitaryWorkshop>().Inputs.Amount(2) == 0, "Recruiter must really deliver and consume the sword and meat.");
				Next();
			}
			else if (phase == 2 && tick - since > 50)
			{
				hidden = saboteur.Trait<Cloak>().Cloaked && !saboteur.CanBeViewedByPlayer(enemy); Check(hidden, "Distant undetected saboteur must be hidden from the enemy.");
				Check(!saboteur.Trait<MedievalSaboteur>().Start(saboteur, market, false, market.Location), "Friendly targets must be refused.");
				saboteur.Trait<Mobile>().SetPosition(saboteur, warehouse.Location + new CVec(-1, 1));
				Click(w, 0, warehouse.Location); Next();
			}
			else if (phase == 3 && saboteur.Trait<MedievalSaboteur>().Progress >= 10000)
			{
				revealed = saboteur.CanBeViewedByPlayer(enemy); Check(revealed && !warehouse.Trait<MedievalBurnable>().Burning, "Preparation must reveal the saboteur without igniting early.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "preparing-fire.png"));
				w.IssueOrder(new Order("Stop", saboteur, false)); Next();
			}
			else if (phase == 4 && tick == since + 31)
			{
				cancelled = !saboteur.Trait<MedievalSaboteur>().Busy && !warehouse.Trait<MedievalBurnable>().Burning;
				Check(cancelled, "Stop must discard unfinished sabotage.");
				Check(sabotage.Start(saboteur, house, false, house.Location), "Destruction fixture must start targeting the hostile house.");
				house.Dispose();
			}
			else if (phase == 4 && tick == since + 61)
			{
				targetDestroyed = house.Disposed && !sabotage.Busy;
				Check(targetDestroyed, "Destroying a target during approach must cancel safely.");
				Click(w, 0, warehouse.Location);
			}
			else if (phase == 4 && tick == since + 91)
			{
				Check(sabotage.Working, "Retreat fixture must be preparing an actual sabotage.");
				w.IssueOrder(new Order("Move", saboteur, Target.FromCell(w, Cell(36, 32)), false));
			}
			else if (phase == 4 && tick > since + 211)
			{
				retreated = !sabotage.Busy && saboteur.Location == Cell(36, 32) && !warehouse.Trait<MedievalBurnable>().Burning;
				Check(retreated, "A single move order must cancel sabotage and actually move to the retreat cell.");
				savedHP = warehouse.Trait<Health>().HP;
				Click(w, 0, warehouse.Location); Next();
			}
			else if (phase == 5 && warehouse.Trait<MedievalBurnable>().LostGoods > 6)
			{
				burned = warehouse.Trait<Health>().HP < savedHP && warehouse.Trait<MedievalStockpile>().Stock.Amount(1) < 80;
				Check(burned && warehouse.Trait<MedievalStockpile>().Stock.Amount(3) == 80, "Fire must damage the building and destroy real provisions, preserving stone.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "warehouse-fire.png"));
				Click(w, 1, cow.Location); Next();
			}
			else if (phase == 6 && cow.IsDead)
			{
				killed = true; Check(!enemyPen.Trait<MedievalLivestock>().Animals.Contains(cow), "Slaughter must remove the real animal from its herd.");
				Click(w, 2, stolenCow.Location); Next();
			}
			else if (phase == 7)
			{
				if (stolenCow.Trait<MedievalAnimal>().Escort == saboteur)
				{
					led = true; Check(saboteur.CanBeViewedByPlayer(enemy), "An escorting saboteur cannot hide the led animal.");
					if (tick % 100 == 0) Game.Renderer.SaveScreenshot(Path.Combine(output, "stolen-animal.png"));
				}
				if (stolenCow.Trait<MedievalAnimal>().Pen == ownPen)
				{
					stolen = stolenCow.Owner == w.LocalPlayer && ownPen.Trait<MedievalLivestock>().Incoming == 0;
					w.IssueOrder(new Order("MedievalPlaceStockyard", warehouse, Target.FromCell(w, Cell(39, 34)), false) { ExtraLocation = Cell(42, 37) }); Next();
				}
			}
			else if (phase == 8 && tick - since > 40 && !warehouse.Trait<MedievalBurnable>().Burning)
			{
				Check(warehouse.Trait<MedievalStockyard>().HasArea, "The external reserve fixture must be valid.");
				warehouse.Trait<MedievalStockpile>().Stock.Add(1, 40); yardHP = warehouse.Trait<Health>().HP;
				saboteur.Trait<Mobile>().SetPosition(saboteur, Cell(38, 35)); Click(w, 0, Cell(39, 35)); Next();
			}
			else if (phase == 9 && warehouse.Trait<MedievalBurnable>().Burning && warehouse.Trait<MedievalBurnable>().Remaining < SabotageRules.BurnTicks - 50)
			{
				yardBurned = warehouse.Trait<Health>().HP == yardHP; Check(yardBurned, "Burning an outside stock area must not remotely damage the warehouse building.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "outside-stock-fire.png"));
				Next();
			}
			else if (phase == 10 && tick - since > 150)
			{
				Check(!saboteur.CanBeViewedByPlayer(enemy), "After an action, concealment must recover away from militia.");
				militia = Spawn(w, "militia", 36, 35, enemy); militia.Trait<AutoTarget>().SetStance(militia, UnitStance.AttackAnything);
				w.IssueOrder(new Order("MedievalPatrolNightOnly", militia, false) { ExtraData = 0 });
				w.IssueOrder(new Order("MedievalStartPatrol", militia, false) { TargetString = PatrolRoute.Encode([Cell(36, 35), Cell(39, 35)]) }); Next();
				Click(w, 0, warehouse.Location);
			}
			else if (phase == 11)
			{
				if (!saboteur.Disposed) detected |= saboteur.CanBeViewedByPlayer(enemy);
				if (tick == since + 20) Game.Renderer.SaveScreenshot(Path.Combine(output, "militia-interception.png"));
				if (saboteur.IsDead)
				{
					intercepted = true; Check(!sabotage.Busy && !herding.Busy, "Death must release every action and animal reservation."); Next();
				}
			}
			else if (phase == 12 && tick > since + 100) Finish(w);
			if (tick > 8500) { Check(false, $"Scenario timed out at phase {phase}."); Finish(w); }
		}
	}
}
