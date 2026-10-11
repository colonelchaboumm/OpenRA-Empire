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
	public class MedievalWeatherPolishValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalWeatherPolishValidation();
	}
	public sealed class MedievalWeatherPolishValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor walker, cart, gate;
		int tick, decoded, snowyBuildings, gateFrames;
		bool done, lightningShot, paused;
		readonly HashSet<int> poses = [];
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		Actor Spawn(World w, string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "weather-polish"); Directory.CreateDirectory(output);
			foreach (var cue in new[] { "snow-step", "snow-cart", "thunder", "storm-surprise" })
				for (var variant = 1; variant <= MedievalAudio.Bank[cue].Variants; variant++)
				{
					using var stream = w.Map.Open($"medieval|audio/{cue}-{variant}.wav"); var valid = false;
					foreach (var loader in Game.ModData.SoundLoaders)
					{
						stream.Position = 0; if (!loader.TryParseSound(stream, out var sound)) continue;
						using (sound) valid = sound.SampleRate == 22050 && sound.SampleBits == 16 && sound.Channels == 1;
						break;
					}
					if (valid) decoded++; else errors.Add("Cannot decode " + cue + variant);
				}
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			for (var i = 0; i < 6; i++) w.CreateActor("md.house", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(20 + i * 4, 22)), new MedievalHouseStyleInit(i)]);
			var names = new[] { "warehouse", "market", "ranch", "sheepfold", "tentweaver", "huntinglodge", "sawmill", "forge", "barracks", "marketstall", "militarytent", "siegeworkshop" };
			for (var i = 0; i < names.Length; i++)
			{
				var a = Spawn(w, names[i], 20 + i % 6 * 4, 28 + i / 6 * 6);
				if (names[i] == "market") a.Trait<MedievalStockpile>().Stock.Add(1, 150);
			}
			for (var i = 0; i < 7; i++) { Spawn(w, "gardenfence", 20 + i, 39); Spawn(w, "palisade", 30 + i, 39); }
			Spawn(w, "sheeppasture.4x3", 37, 38);
			gate = Spawn(w, "woodgate", 28, 42);
			walker = Spawn(w, "builder", 23, 26); cart = Spawn(w, "supplycart", 27, 26);
			Spawn(w, "hauler", 26, 25); Spawn(w, "farmer", 26, 26);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 31))));
			var weather = w.WorldActor.Trait<MedievalWeather>(); var audio = w.WorldActor.Trait<MedievalAudio>();
			var seasons = w.WorldActor.Trait<MedievalSeasons>();
			if (!lightningShot && weather.Storm.Age == 1)
			{
				lightningShot = true; Game.Renderer.SaveScreenshot(Path.Combine(output, "orage.png"));
			}
			if (tick % 150 == 20)
			{
				w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(tick % 300 < 150 ? 35 : 21, 26)), false));
				w.IssueOrder(new Order("Move", cart, Target.FromCell(w, Cell(tick % 300 < 150 ? 35 : 21, 25)), false));
			}
			if (tick == 520)
			{
				w.SetPauseState(true);
				Game.RunAfterDelay(300, () => { paused = audio.ActiveVoices == 0; w.SetPauseState(false); });
			}
			if (tick == 600)
			{
				while (seasons.Clock.Season != VillageSeason.Winter || seasons.Clock.Snow < 950) seasons.Clock.Advance();
				if (audio.Played.GetValueOrDefault("thunder") == 0 || audio.Played.GetValueOrDefault("storm-surprise") == 0) errors.Add("Missing actual thunder or citizen reaction");
			}
			if (tick >= 650)
			{
				Sync.RunUnsynced(w, () => snowyBuildings = Math.Max(snowyBuildings, w.ActorsWithTrait<MedievalSnowCover>().Count(p => p.Trait.SurfaceCount > 0)));
				if (tick % 100 == 0) ((INotifyBlockingMove)gate.Trait<Gate>()).OnNotifyBlockingMove(gate, walker);
				poses.Add(gate.Trait<Gate>().Position); gateFrames = poses.Count;
			}
			if (tick == 800) Game.Renderer.SaveScreenshot(Path.Combine(output, "neige-village.png"));
			if (tick < 1100) return;
			if (!lightningShot || weather.StormScene.ThunderEvents == 0) errors.Add("No lightning/thunder sequence");
			if (!paused || audio.PeakVoices > 18) errors.Add("Pause or voice limit regression");
			if (audio.Played.GetValueOrDefault("snow-step") == 0 || audio.Played.GetValueOrDefault("snow-cart") == 0) errors.Add("Missing movement-driven snow foley");
			if (snowyBuildings < 16 || gateFrames < 2) errors.Add("Missing snow surfaces or animated gate: " + snowyBuildings + "/" + gateFrames);
			if (weather.Storm.Active || weather.StormScene.PendingThunder) errors.Add("Thunder must stop when winter replaces rain with snow");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, decoded, snowyBuildings, gateFrames,
				lightningShot, paused, weather.Storm.Strikes, weather.StormScene.ThunderEvents, weather.StormScene.Reactions, audio.PeakVoices, played = audio.Played }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL WEATHER POLISH: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(700, Game.Exit);
		}
	}
}
