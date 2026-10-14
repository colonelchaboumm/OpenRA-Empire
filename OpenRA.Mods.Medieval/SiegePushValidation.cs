using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSiegePushValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegePushValidation();
	}
	public sealed class MedievalSiegePushValidation : IWorldLoaded, ITick
	{
		readonly List<Actor> engines = [], soldiers = [];
		readonly List<string> errors = [];
		readonly Dictionary<Actor, WPos> previous = [], starts = [];
		readonly Dictionary<Actor, HashSet<int>> frames = [];
		readonly CVec[] directions = [new(3, -3), new(3, 0), new(3, 3), new(0, 3), new(-3, 3), new(-3, 0), new(-3, -3), new(0, -3)];
		WorldRenderer renderer;
		string output;
		int tick, stage, since, movingSamples, contactSamples, maximumStep;
		bool done;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/siege-push"); Directory.CreateDirectory(output); }
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			Actor Spawn(string type, CPos cell) => w.CreateActor("md." + type, [new OwnerInit(w.LocalPlayer), new LocationInit(cell)]);
			var types = new[] { "ram", "trebuchet", "cauldron" };
			for (var i = 0; i < types.Length; i++)
			{
				var cell = Cell(20 + i * 12, 24);
				var engine = Spawn(types[i], cell); engines.Add(engine); starts[engine] = engine.CenterPosition;
				engine.TraitOrDefault<AutoTarget>()?.SetStance(engine, UnitStance.HoldFire);
				for (var j = 0; j < engine.Trait<MedievalSiegeEngine>().CrewLimit; j++)
				{
					var soldier = Spawn(j % 2 == 0 ? "swordsman" : "militia", cell + new CVec(j - 1, 3));
					soldiers.Add(soldier); previous[soldier] = soldier.CenterPosition; frames[soldier] = [];
					soldier.Trait<MedievalSiegeCrew>().Assign(soldier, engine);
				}
			}
			Sync.RunUnsynced(w, () => { w.Selection.Clear(); renderer.Viewport.AdjustZoom((float)Math.Log(2.5f / renderer.Viewport.Zoom)); });
		}
		bool Contact => soldiers.All(a => a.Trait<MedievalSiegeCrew>().Pushing);
		void Move(World w)
		{
			foreach (var engine in engines)
			{
				starts[engine] = engine.CenterPosition;
				w.IssueOrder(new Order("Move", engine, Target.FromCell(w, engine.Location + directions[stage - 1]), false));
			}
			since = tick;
		}
		void Finish(World w)
		{
			Check(movingSamples > 50 && contactSamples > movingSamples / 2, "Crew maintains handhold contact through most movement samples");
			Check(maximumStep < 200, "Real soldiers never teleport to the machine or after leaving it");
			Check(frames.Values.All(f => f.Count >= 6), "Every servant plays a distance-driven pushing gait");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
			{
				passed = errors.Count == 0, errors, tick, stage, movingSamples, contactSamples, maximumStep,
				crew = soldiers.Select(a => new { type = a.Info.Name, location = a.Location.ToString(), a.Trait<MedievalSiegeCrew>().Pushing,
					frames = frames[a].Count, activity = a.CurrentActivity?.GetType().Name, distance = a.Trait<MedievalSiegeCrew>().Assigned ? (a.CenterPosition - a.Trait<MedievalSiegeCrew>().PushPosition(a)).HorizontalLength : 0 })
			}, new JsonSerializerOptions { WriteIndented = true }));
			done = true; Game.RunAfterDelay(500, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			foreach (var a in soldiers)
			{
				var step = (a.CenterPosition - previous[a]).HorizontalLength; previous[a] = a.CenterPosition;
				maximumStep = Math.Max(maximumStep, step);
				if (a.Trait<WithMedievalInfantryBody>().Sequence == "push" && step > 0) frames[a].Add(a.Trait<WithMedievalInfantryBody>().Frame);
				if (stage is > 0 and <= 8 && tick - since > 45 && step > 0)
				{ movingSamples++; if (a.Trait<MedievalSiegeCrew>().Pushing) contactSamples++; }
			}
			if (stage == 0)
			{
				if (tick == 400 || tick == 440 || tick == 480)
					Sync.RunUnsynced(w, () => renderer.Viewport.Center(engines[(tick - 400) / 40].CenterPosition));
				if (tick == 410 || tick == 450 || tick == 490)
					Game.Renderer.SaveScreenshot(Path.Combine(output, $"{engines[(tick - 410) / 40].Info.Name}-contact.png"));
				if (tick < 500) return;
				Check(Contact, "All ten real servants reach distinct contact points on the three machines");
				if (!Contact) { Finish(w); return; }
				stage = 1; Move(w); return;
			}
			if (stage <= 8)
			{
				var elapsed = tick - since;
				if (elapsed == 65 || elapsed == 85 || elapsed == 105)
					Sync.RunUnsynced(w, () => renderer.Viewport.Center(engines[(elapsed - 65) / 20].CenterPosition));
				if (elapsed == 72 || elapsed == 92 || elapsed == 112)
					Game.Renderer.SaveScreenshot(Path.Combine(output, $"{engines[(elapsed - 72) / 20].Info.Name}-direction-{stage}.png"));
				if (elapsed < 300) return;
				foreach (var engine in engines) Check((engine.CenterPosition - starts[engine]).HorizontalLength > 1500, engine.Info.Name + " advances in direction " + stage);
				Console.WriteLine($"SIEGE PUSH {stage}: contact={soldiers.Count(a => a.Trait<MedievalSiegeCrew>().Pushing)}/10");
				stage++;
				if (stage <= 8) Move(w);
				else
				{
					since = tick;
					var soldier = soldiers[0];
					w.IssueOrder(new Order("Move", soldier, Target.FromCell(w, soldier.Location + new CVec(-4, 0)), false));
				}
				return;
			}
			if (tick - since < 200) return;
			Check(!soldiers[0].Trait<MedievalSiegeCrew>().Assigned && !soldiers[0].Trait<MedievalSiegeCrew>().Pushing, "Manual movement releases the handhold and resumes normal movement");
			Check(!engines[0].Trait<MedievalSiegeEngine>().CanMove, "Ram still requires four real servants");
			Finish(w);
		}
	}
}
