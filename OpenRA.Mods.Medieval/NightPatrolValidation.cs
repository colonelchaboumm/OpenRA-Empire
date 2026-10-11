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
	public class MedievalNightPatrolValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalNightPatrolValidation();
	}
	public sealed class MedievalNightPatrolValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor guard, cancelled, resting, continuous, unassigned, commander;
		int tick, dawnVisits, secondNightVisits;
		bool done, autoWoke, secondAutoWoke, orderedRestRespected;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string text) { if (!ok && !errors.Contains(text)) { errors.Add(text); Console.WriteLine($"NIGHT WATCH {tick}: {text}"); } }
		Actor Spawn(World w, string type, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Select(World w, params Actor[] actors) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, actors, false, false));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>("MD_PATROL_" + id); Check(b.IsVisible() && !b.IsDisabled(), "Unavailable button " + id); if (!b.IsDisabled()) b.OnClick();
		});
		void Draw(World w, Actor[] actors, params CPos[] points)
		{
			Select(w, actors); Button(w, "DRAW");
			Sync.RunUnsynced(w, () =>
			{
				var gen = (OrderGenerator)w.OrderGenerator;
				foreach (var p in points) foreach (var order in gen.Order(w, p, int2.Zero, new MouseInput(MouseInputEvent.Down, gen.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1))) w.IssueOrder(order);
			});
			Button(w, "START");
		}
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "night-patrol"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"status\":\"running\"}");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Spawn(w, "market", 32, 25).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "house", 27, 25); Spawn(w, "house", 24, 25);
			Spawn(w, "militarytent", 31, 32).Trait<MedievalCampArea>().RestEnabled = true;
			// Keep the two camps outside command range even after the commander leaves his tent.
			Spawn(w, "militarytent", 46, 31);
			guard = Spawn(w, "militia", 30, 35); cancelled = Spawn(w, "militia", 24, 32);
			resting = Spawn(w, "militia", 46, 34); continuous = Spawn(w, "militia", 24, 40); unassigned = Spawn(w, "militia", 24, 38);
			commander = Spawn(w, "commander", 52, 32);
			foreach (var a in new[] { guard, cancelled, resting, continuous, unassigned, commander }) a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(33, 33))));
			var patrol = guard.Trait<MedievalPatrol>(); var rest = guard.Trait<MedievalRest>();
			if (tick == 20)
			{
				Draw(w, [guard, cancelled], Cell(30, 35), Cell(34, 35), Cell(34, 37), Cell(30, 37));
				Draw(w, [resting], Cell(46, 34), Cell(49, 34));
				Select(w, continuous); Button(w, "NIGHT"); Draw(w, [continuous], Cell(24, 40), Cell(28, 40)); Select(w, guard);
			}
			if (tick == 60) w.IssueOrder(new Order("MedievalCommanderRest", commander, false));
			if (tick == 200)
			{
				Check(patrol.HasRoute && patrol.NightOnly && !patrol.Active && patrol.Visits == 0, "Registered night watch must wait for dusk in daylight.");
				Check(rest.Sleeping && !rest.OrderedMilitaryRest, "An automatically resting militiaman must remain in bed before dusk.");
				Check(continuous.Trait<MedievalPatrol>().Active && continuous.Trait<MedievalPatrol>().Visits > 0, "Continuous patrol option must still work by day.");
				Check(!Ui.Root.Get<ButtonWidget>("MD_PATROL_STOP").IsDisabled(), "A scheduled daytime circuit must be removable.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "waiting-for-dusk.png"));
			}
			if (tick == 360)
			{
				autoWoke = patrol.Active && !rest.Resting && patrol.Visits > 0;
				Check(autoWoke && MedievalTorch.Lit(guard), "Dusk must automatically wake the guard and begin the stored route with a torch.");
				orderedRestRespected = resting.Trait<MedievalRest>().Sleeping && resting.Trait<MedievalRest>().OrderedMilitaryRest && resting.Trait<MedievalPatrol>().Visits == 0;
				Check(orderedRestRespected, "The commander's explicit rest order must remain higher priority than the automatic watch.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "automatic-departure.png"));
			}
			if (tick == 400) w.IssueOrder(new Order("Move", cancelled, Target.FromCell(w, Cell(24, 30)), false));
			if (tick == 650) w.IssueOrder(new Order("MedievalCommanderWake", commander, false));
			if (tick == 1000)
			{
				Check(resting.Trait<MedievalPatrol>().Visits > 0 && !resting.Trait<MedievalRest>().Resting, "Explicit wake must resume the watch during the same night.");
				Check(!cancelled.Trait<MedievalPatrol>().HasRoute, "Manual movement must remove the scheduled circuit.");
			}
			if (tick == 1550)
			{
				Check(!patrol.Active && patrol.HasRoute && patrol.Points.Count == 4, "Dawn must stop the circuit without erasing its four waypoints."); dawnVisits = patrol.Visits;
				Check(!MedievalTorch.Lit(guard), "The watch torch must be extinguished by day.");
			}
			if (tick == 1740)
			{
				Check(patrol.Visits == dawnVisits, "The guard must not advance its circuit between dawn and dusk.");
				Check(rest.Sleeping && !rest.OrderedMilitaryRest, $"A guard within the tent radius must rest between nights: {rest.Description}, {guard.Location}.");
				Check(continuous.Trait<MedievalPatrol>().Active, "Dawn must not stop a continuous patrol.");
			}
			if (tick == 2000)
			{
				secondAutoWoke = patrol.Active && !rest.Resting && patrol.Visits > dawnVisits;
				Check(secondAutoWoke, "The next dusk must restart the same circuit without any new player order.");
				Check(!cancelled.Trait<MedievalPatrol>().HasRoute && !cancelled.Trait<MedievalPatrol>().Active, "A cancelled circuit must not restart the next night.");
				Check(!unassigned.Trait<MedievalPatrol>().Active && !unassigned.Trait<MedievalPatrol>().HasRoute, "Militia with no assigned circuit must not invent a patrol.");
				secondNightVisits = patrol.Visits; Game.Renderer.SaveScreenshot(Path.Combine(output, "second-night.png"));
			}
			if (tick == 3050)
			{
				Check(!patrol.Active && patrol.HasRoute, "The second dawn must preserve the circuit again.");
				Select(w, guard); Button(w, "STOP");
			}
			if (tick < 3100) return;
			Check(!patrol.HasRoute, "Removing a waiting circuit through the UI must cancel future automatic starts.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, autoWoke, secondAutoWoke, orderedRestRespected, dawnVisits, secondNightVisits }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL NIGHT PATROL: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
