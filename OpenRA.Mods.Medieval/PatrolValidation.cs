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
	public class MedievalPatrolValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalPatrolValidation();
	}
	public sealed class MedievalPatrolValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor first, second, offensive, defensive, neutral, offensiveTarget, defensiveTarget, neutralTarget, patrolTarget, commander, tent, ram;
		Player rival;
		int tick, visitsBeforeRest, visitsBeforeCombat, neutralHP, pursuitHP;
		CPos pursuitPosition;
		bool finished, pauseChecked;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		static CPos[] Route => [Cell(24, 32), Cell(28, 32), Cell(28, 36), Cell(24, 36)];
		void Check(bool ok, string text) { if (!ok && !errors.Contains(text)) { errors.Add(text); Console.WriteLine($"PATROL CHECK {tick}: {text}"); } }
		Actor Spawn(World w, string type, int x, int y, Player owner = null) => w.CreateActor("md." + type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Select(World w, params Actor[] actors) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, actors, false, false));
		void Button(World w, string name) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>("MD_PATROL_" + name); Check(b.IsVisible() && !b.IsDisabled(), "Unavailable patrol button: " + name); if (!b.IsDisabled()) b.OnClick();
		});
		void Click(World w, CPos cell) => Sync.RunUnsynced(w, () =>
		{
			var generator = (OrderGenerator)w.OrderGenerator;
			foreach (var order in generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray()) w.IssueOrder(order);
		});
		void Start(Actor a) => a.World.IssueOrder(new Order("MedievalStartPatrol", a, false) { TargetString = PatrolRoute.Encode(Route) });
		void Move(Actor a, int x, int y) => a.World.IssueOrder(new Order("Move", a, Target.FromCell(a.World, Cell(x, y)), false));
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "patrol"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"status\":\"running\"}");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			rival = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask; rival.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			Spawn(w, "market", 24, 25).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "house", 20, 26); Spawn(w, "house", 20, 29);
			first = Spawn(w, "militia", 24, 32); second = Spawn(w, "militia", 24, 34);
			offensive = Spawn(w, "militia", 40, 25); offensiveTarget = Spawn(w, "royalguard", 45, 25, rival);
			defensive = Spawn(w, "militia", 40, 40); defensiveTarget = Spawn(w, "royalguard", 45, 40, rival);
			neutral = Spawn(w, "militia", 42, 50); neutralTarget = Spawn(w, "archer", 44, 50, rival);
			foreach (var a in new[] { offensiveTarget, defensiveTarget, neutralTarget }) a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire);
			commander = Spawn(w, "commander", 34, 32); commander.Trait<AutoTarget>().SetStance(commander, UnitStance.HoldFire);
			tent = Spawn(w, "militarytent", 31, 32); ram = Spawn(w, "ram", 31, 27);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(28, 32))));
			if (tick == 20)
			{
				Check(first.TraitOrDefault<MedievalTorch>() != null && !MedievalTorch.Lit(first), "Militia torch must be equipped but unlit in daylight.");
				Select(w, offensive); Button(w, "STANCE_0"); Select(w, defensive); Button(w, "STANCE_1"); Select(w, neutral); Button(w, "STANCE_2");
				Select(w, first, second); Button(w, "STANCE_2"); Button(w, "NIGHT"); Button(w, "DRAW");
				foreach (var cell in Route) Click(w, cell);
				Button(w, "START");
			}
			if (tick == 50)
			{
				Check(new[] { first, second }.All(a => a.Trait<MedievalPatrol>().Active && a.Trait<MedievalPatrol>().Points.Count == 4), "Multi-selection must assign the complete four-point circuit.");
				Check(first.Trait<AutoTarget>().Stance == UnitStance.HoldFire && offensive.Trait<AutoTarget>().Stance == UnitStance.AttackAnything && defensive.Trait<AutoTarget>().Stance == UnitStance.Defend, "All three attitudes must resolve through real orders.");
				foreach (var id in new[] { "STANCE_0", "STANCE_1", "STANCE_2", "DRAW", "START", "NIGHT", "STOP" })
				{
					var b = Ui.Root.Get<ButtonWidget>("MD_PATROL_" + id); Check(Game.Renderer.Fonts[b.Font].Measure(b.GetText()).X <= b.Bounds.Width - 8, "Patrol button text must fit: " + id);
				}
			}
			if (tick == 240)
			{
				Check(offensiveTarget.Trait<Health>().HP < offensiveTarget.Trait<Health>().MaxHP && offensive.Location != Cell(40, 25), "Offensive stance must approach and damage an enemy within detection range.");
				Check(defensive.Location == Cell(40, 40) && defensiveTarget.Trait<Health>().HP == defensiveTarget.Trait<Health>().MaxHP, "Defensive stance must not chase distant enemies.");
				Move(offensiveTarget, 53, 25);
				w.IssueOrder(new Order("Attack", neutralTarget, Target.FromActor(neutral), false));
				w.IssueOrder(new Order("Attack", defensiveTarget, Target.FromActor(defensive), false));
			}
			if (tick == 390)
			{
				Check(neutral.Trait<Health>().HP < neutral.Trait<Health>().MaxHP && neutralTarget.Trait<Health>().HP == neutralTarget.Trait<Health>().MaxHP, "Neutral stance must not retaliate when attacked.");
				Check(defensive.Location == Cell(40, 40) && defensiveTarget.Trait<Health>().HP < defensiveTarget.Trait<Health>().MaxHP, "Defensive stance must fight at reach without abandoning its position.");
				w.IssueOrder(new Order("Stop", neutralTarget, false)); w.IssueOrder(new Order("Stop", defensiveTarget, false));
				Select(w, offensive); Button(w, "STANCE_2");
			}
			if (tick == 450) { pursuitHP = offensiveTarget.Trait<Health>().HP; pursuitPosition = offensive.Location; }
			if (tick == 550) Check(offensive.Location == pursuitPosition && offensiveTarget.Trait<Health>().HP == pursuitHP, "Switching to neutral must cancel automatic pursuit and attacks.");
			if (tick == 600)
			{
				Check(first.Trait<MedievalPatrol>().Visits >= 4 && second.Trait<MedievalPatrol>().Visits >= 4, "Both militiamen must physically visit and loop through all waypoints.");
				w.AddFrameEndTask(world =>
				{
					patrolTarget = world.CreateActor("md.builder", [new OwnerInit(rival), new LocationInit(second.Location + new CVec(1, 0))]); patrolTarget.Trait<MedievalWorker>().Automatic = false;
					neutralHP = patrolTarget.Trait<Health>().HP;
				});
			}
			if (tick == 680)
			{
				Check(patrolTarget.Trait<Health>().HP == neutralHP, "Neutral patrol must pass enemies without attacking them.");
				Select(w, second); Button(w, "STANCE_0"); visitsBeforeCombat = second.Trait<MedievalPatrol>().Visits;
			}
			if (tick == 800) { Select(w, first); Button(w, "STOP"); }
			if (tick == 850) { Check(!first.Trait<MedievalPatrol>().Active && first.IsIdle, "Stop patrol must end the activity."); Start(first); }
			if (tick == 900) Move(first, 30, 32);
			if (tick == 1050) Check(!first.Trait<MedievalPatrol>().Active && first.Location == Cell(30, 32), "Manual movement must cancel a patrol and keep its destination.");
			if (tick == 1100)
			{
				Check(MedievalTorch.Lit(first) && MedievalTorch.Lit(second), "Militia must light torches at night."); Select(w, first, second);
				Game.Renderer.SaveScreenshot(Path.Combine(output, "night-patrol.png"));
				w.SetPauseState(true);
				Game.RunAfterDelay(100, () =>
				{
					var position = second.CenterPosition; var phase = w.WorldActor.Trait<MedievalDayNight>().Phase;
					Game.RunAfterDelay(150, () => { pauseChecked = w.Paused && second.CenterPosition == position && w.WorldActor.Trait<MedievalDayNight>().Phase == phase; w.SetPauseState(false); });
				});
			}
			if (tick == 1200) { Start(first); w.IssueOrder(new Order("MedievalCommanderRest", commander, false)); visitsBeforeRest = first.Trait<MedievalPatrol>().Visits; }
			if (tick == 1400)
			{
				Check(first.Trait<MedievalPatrol>().Active && first.Trait<MedievalRest>().Sleeping, "Commander rest must suspend an assigned patrol at the tent.");
				Check(!MedievalTorch.Lit(first), "A sleeping militiaman must not display a floating torch.");
				visitsBeforeRest = first.Trait<MedievalPatrol>().Visits;
				w.IssueOrder(new Order("MedievalCommanderWake", commander, false));
			}
			if (tick == 1700)
			{
				Check(first.Trait<MedievalPatrol>().Active && !first.Trait<MedievalRest>().Resting && first.Trait<MedievalPatrol>().Visits > visitsBeforeRest, "Waking the camp must resume the stored circuit.");
				Check(second.Trait<MedievalPatrol>().Active && second.Trait<MedievalPatrol>().Visits > visitsBeforeCombat && (patrolTarget.IsDead || !patrolTarget.IsInWorld || patrolTarget.Trait<Health>().HP < neutralHP), "Offensive patrol must fight and resume its circuit.");
				w.IssueOrder(new Order("MedievalAssignSiege", first, Target.FromActor(ram), false));
			}
			if (tick == 1750) { Check(!first.Trait<MedievalPatrol>().Active && first.Trait<MedievalSiegeCrew>().Assigned, "Siege assignment must replace a patrol."); Start(first); }
			if (tick == 1800) { Check(first.Trait<MedievalPatrol>().Active && !first.Trait<MedievalSiegeCrew>().Assigned, "Starting a patrol must release siege assignment."); w.IssueOrder(new Order("Stop", first, false)); }
			if (tick == 1850) { Check(!first.Trait<MedievalPatrol>().Active, "Normal Stop must also cancel patrol."); w.IssueOrder(new Order("MedievalStartPatrol", first, false) { TargetString = "bad route" }); }
			if (tick == 1900) Check(!first.Trait<MedievalPatrol>().Active, "Malformed circuit must be rejected without starting an activity.");
			if (tick == 2450) { Check(!MedievalTorch.Lit(first) && !MedievalTorch.Lit(second), "Torches must turn off again in daylight."); Game.Renderer.SaveScreenshot(Path.Combine(output, "day-patrol.png")); }
			if (tick < 2500) return;
			Check(pauseChecked, "Pause must freeze patrol motion and night cycle.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, pauseChecked,
				firstVisits = first.Trait<MedievalPatrol>().Visits, secondVisits = second.Trait<MedievalPatrol>().Visits, skipped = second.Trait<MedievalPatrol>().Skipped }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL PATROL: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
