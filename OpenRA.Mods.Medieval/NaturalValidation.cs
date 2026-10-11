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
	public class MedievalNaturalValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalNaturalValidation();
	}
	// Dedicated finite-source scenario, never loaded in a normal game.
	public sealed class MedievalNaturalValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick;
		bool finished;
		Actor[] trees;
		Actor[] rocks;
		Actor[] workTrees;
		Actor[] workRocks;
		MedievalSource[] workerSources;
		Actor lumberjack;
		Actor stonecutter;
		Actor sawmill;
		Actor quarry;
		readonly HashSet<int> fallAges = [];
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "natural-preview");
			Directory.CreateDirectory(output);
		}
		void Check(bool ok, string message) { if (!ok) errors.Add(message); }
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		void Setup(World w)
		{
			foreach (var actor in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null
				|| a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) actor.Dispose();
			Actor Spawn(string name, int x, int y, bool neutral = true) => w.CreateActor("md." + name,
				[new OwnerInit(neutral ? w.Players.First(p => p.InternalName == "Neutral") : w.LocalPlayer), new LocationInit(Cell(x, y))]);
			trees = new[] { Spawn("tree", 24, 26), Spawn("pine", 28, 26), Spawn("birch", 32, 26) };
			rocks = new[] { Spawn("stone", 24, 30), Spawn("stone", 28, 30), Spawn("stone", 32, 30) };
			workTrees = new[] { Spawn("tree", 18, 28), Spawn("birch", 18, 25) };
			workRocks = new[] { Spawn("stone", 39, 28), Spawn("stone", 42, 28) };
			// Small fixture reserves exercise final partial loads with real workers quickly.
			foreach (var actor in workTrees.Concat(workRocks)) actor.Trait<MedievalSource>().Remaining = 25;
			workerSources = workTrees.Concat(workRocks).Select(a => a.Trait<MedievalSource>()).ToArray();
			sawmill = Spawn("sawmill", 18, 31, false);
			quarry = Spawn("quarry", 36, 31, false);
			lumberjack = Spawn("lumberjack", 17, 28, false);
			stonecutter = Spawn("stonecutter", 38, 28, false);
			var market = Spawn("market", 28, 33, false);
			market.Trait<MedievalStockpile>().Stock.Add(1, 200);
		}
		void ITick.Tick(Actor self)
		{
			if (finished || self.World.LocalPlayer == null) return;
			var w = self.World;
			tick++;
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(28, 29)));
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			foreach (var actor in trees.Where(a => a.IsInWorld))
				if (actor.Trait<MedievalSource>().Falling) fallAges.Add(actor.Trait<MedievalSource>().FallAge);
			if (tick == 20)
			{
				Check(trees.All(a => a.Trait<MedievalSource>().Remaining == 200), "Each tree starts with 200 wood.");
				Check(rocks.All(a => a.Trait<MedievalSource>().Remaining == 350), "Each rock starts with 350 stone.");
				Shot("01-intact");
			}
			if (tick == 40)
			{
				foreach (var actor in trees) Check(actor.Trait<MedievalSource>().Harvest(10) == 10, "First cut must deduct real wood.");
				rocks[1].Trait<MedievalSource>().Harvest(175);
				rocks[2].Trait<MedievalSource>().Harvest(315);
			}
			if (tick >= 40 && tick <= 84 && tick % 2 == 0) Shot($"fall-{tick - 40:000}");
			if (tick == 90)
			{
				Check(fallAges.Count >= 30, "Tree falling must advance through the real simulation.");
				Check(trees.All(a => !a.Trait<MedievalSource>().StandingTree && !a.Trait<MedievalSource>().Falling), "All three species must finish falling.");
				Check(trees.All(a => a.Trait<MedievalSource>().Remaining == 190), "Falling must not consume or create extra wood.");
				Shot("02-felled-and-quarried");
			}
			if (tick == 110)
			{
				trees[1].Trait<MedievalSource>().Harvest(90);
				trees[2].Trait<MedievalSource>().Harvest(170);
			}
			if (tick == 135) Shot("03-trunks-decreasing");
			if (tick == 160)
			{
				foreach (var actor in trees.Concat(rocks))
				{
					var source = actor.Trait<MedievalSource>();
					var remaining = source.Remaining;
					Check(source.Harvest(10000) == remaining, "Final load must be capped by the remaining natural stock.");
					Check(source.Harvest(10) == 0, "An exhausted source must never yield another load.");
				}
			}
			if (tick == 220)
			{
				Check(trees.Concat(rocks).All(a => !a.IsInWorld), "Exhausted resource actors must release their footprints.");
				Check(MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.house", Cell(24, 26)), "A cleared tree cell must accept construction.");
				Check(MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.house", Cell(24, 30)), "An exhausted rock cell must accept construction.");
				Check(w.Actors.Any(a => a.Info.Name == "md.stump" && a.Location == Cell(24, 26)), "A tree leaves a passable stump.");
				Check(w.Actors.Any(a => a.Info.Name == "md.rubble" && a.Location == Cell(24, 30)), "Stone leaves decorative rubble.");
				Shot("04-exhausted");
			}
			if (tick != 2200) return;
			Check(workerSources.All(s => s.Remaining == 0) && workTrees.Concat(workRocks).All(a => !a.IsInWorld), "Workers must empty both sources and switch automatically.");
			var wood = sawmill.Trait<MedievalStockpile>().Stock.Amount(0);
			var stone = quarry.Trait<MedievalStockpile>().Stock.Amount(3);
			Check(wood == 50 && stone == 50, $"Workers must physically deliver exactly 50 wood and 50 stone, got {wood}, {stone}.");
			Check(lumberjack.Trait<MedievalWorker>().Cargo == 0 && stonecutter.Trait<MedievalWorker>().Cargo == 0, "Final partial loads must be delivered.");
			Check(!w.ActorsWithTrait<MedievalSource>().Any(), "None of the exhausted natural nodes may regenerate.");
			Shot("05-workers-finished");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, woodDelivered = wood, stoneDelivered = stone, fallingPoses = fallAges.Count }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL NATURAL: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true;
			Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
