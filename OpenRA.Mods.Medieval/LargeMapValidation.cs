using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalLargeMapValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalLargeMapValidation();
	}

	// Opt-in native scenario only. Normal maps never enable this trait.
	public sealed class MedievalLargeMapValidation : IWorldLoaded, ITick
	{
		sealed class Crossing
		{
			public Actor Actor;
			public CPos Destination;
			public bool TouchedDeck, Arrived;
		}
		WorldRenderer renderer;
		JsonDocument geography;
		string output;
		readonly List<string> errors = [];
		readonly List<Crossing> crossings = [];
		readonly List<int> paths = [];
		readonly Stopwatch elapsed = new();
		int tick, sources, waterCells;
		long initialWood, initialStone;
		double loadSeconds;
		bool finished, harvestingFixture;
		static CPos Cell(JsonElement a) => new(a[0].GetInt32(), a[1].GetInt32());
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			using var stream = w.Map.Open("geography.json");
			geography = JsonDocument.Parse(stream);
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "large-maps", geography.RootElement.GetProperty("folder").GetString());
			Directory.CreateDirectory(output);
			loadSeconds = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
			foreach (var pair in w.ActorsWithTrait<MedievalSource>())
			{
				if (pair.Trait.Info.Natural == NaturalResourceKind.None) continue;
				sources++;
				Check(!pair.Trait.Info.Renewable, "Natural resources must remain finite.");
				Check(w.Map.GetTerrainInfo(pair.Actor.Location).Type != "Water", "A natural deposit is underwater.");
				if (pair.Trait.Info.Resource == Material.Wood) initialWood += pair.Trait.Remaining;
				else initialStone += pair.Trait.Remaining;
			}
			Check(initialWood == geography.RootElement.GetProperty("finiteWood").GetInt64(), "Generated and loaded wood stocks disagree.");
			Check(initialStone == geography.RootElement.GetProperty("finiteStone").GetInt64(), "Generated and loaded stone stocks disagree.");
			Console.WriteLine($"LARGE MAP LOADED: {w.Map.Title}, {w.Actors.Count()} actors, {loadSeconds:F1}s");
		}
		void Setup(World w)
		{
			w.LocalPlayer.Shroud.ExploreAll(); w.LocalPlayer.Shroud.Disabled = true;
			var finder = w.WorldActor.Trait<IPathFinder>();
			var builder = w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.builder");
			harvestingFixture = builder != null;
			var caravan = w.ActorsHavingTrait<MedievalFoundingCaravan>().FirstOrDefault(a => a.Owner == w.LocalPlayer);
			var origin = (builder ?? caravan).Location;
			if (caravan != null) w.IssueOrder(new Order("MedievalFoundMarket", caravan, false));
			foreach (var bridge in geography.RootElement.GetProperty("bridges").EnumerateArray())
			{
				var start = Cell(bridge.GetProperty("start")); var end = Cell(bridge.GetProperty("end"));
				foreach (var reverse in new[] { false, true })
				{
					var from = reverse ? end : start; var to = reverse ? start : end;
					var actor = w.CreateActor(reverse ? "md.supplycart" : "md.militia", [new OwnerInit(w.LocalPlayer), new LocationInit(from)]);
					var path = finder.FindPathToTargetCell(actor, [from], to, BlockedByActor.Immovable);
					Check(path.Count > 0 && path.Any(c => w.Map.GetTerrainInfo(c).Type == "Bridge"), "Native pathfinder did not cross the bridge.");
					Check(path.All(c => w.Map.GetTerrainInfo(c).Type != "Water"), "Land units must not walk on river water.");
					paths.Add(path.Count);
					if (!reverse)
					{
						var longPath = finder.FindPathToTargetCell(actor, [origin], to, BlockedByActor.Immovable);
						Check(longPath.Count > 0, "A bridge cannot be reached from the village.");
						paths.Add(longPath.Count);
					}
					crossings.Add(new Crossing { Actor = actor, Destination = to });
					w.IssueOrder(new Order("Move", actor, Target.FromCell(w, to), false));
				}
			}
			var mobile = crossings[0].Actor.Trait<Mobile>();
			foreach (var cell in w.Map.AllCells.Where(c => w.Map.GetTerrainInfo(c).Type == "Water").Take(100))
			{
				Check(!mobile.CanEnterCell(cell), "River terrain must block ground units."); waterCells++;
			}
			elapsed.Start();
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			foreach (var crossing in crossings)
			{
				if (w.Map.GetTerrainInfo(crossing.Actor.Location).Type == "Bridge") crossing.TouchedDeck = true;
				if (crossing.Actor.Location == crossing.Destination) crossing.Arrived = true;
			}
			var bridges = geography.RootElement.GetProperty("bridges");
			var otherAxis = bridges.EnumerateArray().FirstOrDefault(b => Cell(b.GetProperty("start")).X == Cell(b.GetProperty("end")).X);
			if (otherAxis.ValueKind == JsonValueKind.Undefined) otherAxis = bridges[bridges.GetArrayLength()-1];
			var lookAt = tick < 180 ? Cell(bridges[0].GetProperty("center")) : tick < 350 ? Cell(otherAxis.GetProperty("center")) : Cell(geography.RootElement.GetProperty("start"));
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(lookAt)));
			if (tick is 80 or 250 or 450) Game.Renderer.SaveScreenshot(Path.Combine(output, $"view-{tick}.png"));
			if (tick < 1000) return;
			Check(waterCells == 100, "Water blocking samples were not checked.");
			Check(crossings.All(c => c.TouchedDeck && c.Arrived), "A unit or cart failed to physically traverse a bridge.");
			var currentWood = w.ActorsWithTrait<MedievalSource>().Where(p => p.Trait.Info.Natural == NaturalResourceKind.Tree).Sum(p => (long)p.Trait.Remaining);
			var currentStone = w.ActorsWithTrait<MedievalSource>().Where(p => p.Trait.Info.Natural == NaturalResourceKind.Stone).Sum(p => (long)p.Trait.Remaining);
			if (harvestingFixture) Check(currentWood < initialWood && currentStone < initialStone, "Village workers must still harvest wood and stone on a large map.");
			else Check(w.Actors.Any(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.foundingmarket")
				&& w.ActorsWithTrait<MedievalWorker>().Count(p => p.Actor.Owner == w.LocalPlayer) == 3, "The founding caravan must deploy on the immense map.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors,
				loadSeconds, simulationSeconds = elapsed.Elapsed.TotalSeconds, syncChecks = Game.Settings.Debug.SyncCheckUnsyncedCode,
				averageTickMs = PerfHistory.Items["tick_time"].ActiveGameTotalAverage(),
				peakMemoryMiB = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576,
				sources, waterCells, initialWood, currentWood, initialStone, currentStone, pathLengths = paths,
				crossings = crossings.Select(c => new { type = c.Actor.Info.Name, c.TouchedDeck, c.Arrived }) }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL LARGE MAP: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; geography.Dispose(); Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
