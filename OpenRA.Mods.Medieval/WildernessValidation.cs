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
	public class MedievalWildernessValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalWildernessValidation();
	}
	// This fixture is enabled only by the medieval-wilderness test mod.
	public sealed class MedievalWildernessValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		WPos center;
		int tick, maxAnimals, maxPlants, maxDrops;
		bool finished, sawRabbit, sawDeer, sawRain, dried;
		readonly List<string> errors = [];
		readonly HashSet<int> deerFrames = [];
		void Check(bool value, string error) { if (!value && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "wilderness", w.Map.MapSize.Width > 100 ? "immense" : "village");
			Directory.CreateDirectory(output);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			var wildlife = w.WorldActor.Trait<MedievalWilderness>();
			var weather = w.WorldActor.Trait<MedievalWeather>().Scene;
			if (tick == 1)
			{
				w.LocalPlayer.Shroud.Disabled = true;
				center = w.Map.MapSize.Width > 100
					? w.Actors.First(a => a.Owner == w.LocalPlayer && a.TraitOrDefault<Mobile>() != null).CenterPosition + new WVec(4200, 0, 0)
					: w.Map.CenterOfCell(new CPos(50, 3));
			}
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(center + (tick >= 900 && tick < 1080 ? new WVec(6500, 4000, 0) : WVec.Zero)));
			var game = w.ActorsWithTrait<MedievalGameAnimal>().ToArray();
			maxAnimals = Math.Max(maxAnimals, game.Length); maxPlants = Math.Max(maxPlants, wildlife.PlantCount); maxDrops = Math.Max(maxDrops, weather.Drops.Count);
			Check(game.Length <= 600 && wildlife.PlantCount <= MedievalWilderness.MaxPlants && weather.Drops.Count <= VillageWeather.MaxDrops, "Scenery and rain must remain bounded on every map.");
			foreach (var pair in game)
			{
				sawRabbit |= pair.Actor.Info.Name == "md.game-rabbit"; sawDeer |= pair.Actor.Info.Name == "md.game-deer";
				var frame = pair.Trait.VisualFrame(pair.Actor);
				if (pair.Actor.Info.Name == "md.game-deer" && frame >= 96) deerFrames.Add(frame);
				Check(w.Map.GetTerrainInfo(pair.Actor.Location).Type != "Water", "Game must stay off water.");
			}
			sawRain |= weather.Intensity > .5f;
			dried |= sawRain && weather.Intensity == 0 && weather.Drops.Count == 0;
			if (tick == 220)
			{
				var animal = game.Select(p => p.Actor).FirstOrDefault(a => a.Info.Name == "md.game-deer");
				Check(animal != null, "A deer must have appeared in the countryside.");
				if (animal != null)
				{
					var cell = w.Map.FindTilesInAnnulus(animal.Location, 1, 2).First(c => w.Map.Contains(c) && !w.ActorMap.GetActorsAt(c).Any() && w.Map.GetTerrainInfo(c).Type == "Clear");
					w.AddFrameEndTask(_ => w.CreateActor("md.builder", [new OwnerInit(w.LocalPlayer), new LocationInit(cell)]));
				}
			}
			if (tick is 150 or 400 or 550 or 1000 or 1250) Game.Renderer.SaveScreenshot(Path.Combine(output, $"view-{tick}.png"));
			if (tick < 1600) return;
			Check(sawRabbit && sawDeer && deerFrames.Count >= 8, "Both species and the deer walking/running animation must be visible.");
			var fled = game.Sum(p => p.Trait.Fled);
			Check(fled > 0, "A nearby citizen must make the deer flee.");
			Check(maxPlants > 20, "Wild vegetation must enrich the visible landscape.");
			Check(sawRain && dried && weather.Showers >= 2 && weather.Splashes > 100, "Showers must arrive, splash, clear and return.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, sawRabbit, sawDeer,
				deerFrames = deerFrames.Count, fled, maxAnimals, maxPlants, maxDrops, weather.Showers, weather.Splashes, dried,
				averageTickMs = PerfHistory.Items["tick_time"].ActiveGameTotalAverage(), peakMemoryMiB = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576 }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL WILDERNESS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
