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
	public class MedievalStormRefugeValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalStormRefugeValidation();
	}
	// Opt-in native simulation: no injected refuge calls, actual thunder and movement.
	public sealed class MedievalStormRefugeValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		readonly List<Actor> citizens = [], houses = [], pens = [];
		readonly List<string> errors = [];
		Actor soldier;
		int tick, alarmTick, maxRunning, maxInside, manualTick;
		bool done, cargoSafe, delivered, manualObeyed, resumed;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		Actor Spawn(World w, string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Check(bool ok, string text) { if (!ok && !errors.Contains(text)) { errors.Add(text); Console.WriteLine($"REFUGE {tick}: {text}"); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "storm-refuge"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			houses.Add(Spawn(w, "house", 20, 25)); houses.Add(Spawn(w, "house", 28, 25));
			Spawn(w, "warehouse", 20, 30); Spawn(w, "market", 20, 21).Trait<MedievalStockpile>().Stock.Add(1, 150);
			var professions = new[] { "hauler", "farmer", "builder", "lumberjack", "stonecutter", "breeder", "shepherd", "blacksmith", "hauler", "builder" };
			for (var i = 0; i < professions.Length; i++)
			{
				var a = Spawn(w, professions[i], 23 + i % 4, 34 + i / 4);
				a.Trait<MedievalWorker>().Automatic = false; citizens.Add(a);
			}
			citizens[0].Trait<MedievalWorker>().Cargo = 7;
			citizens[0].Trait<MedievalWorker>().Resource = (int)Material.Wood;
			soldier = Spawn(w, "militia", 28, 33);
			Spawn(w, "ranch", 32, 28); Spawn(w, "sheepfold", 40, 27);
			pens.Add(Spawn(w, "pasture.4x4", 32, 33)); pens.Add(Spawn(w, "sheeppasture.5x3", 40, 32));
			foreach (var pen in pens)
				for (var i = 0; i < 2; i++)
				{
					var animal = w.CreateActor(pen.Trait<MedievalLivestock>().AnimalType, [new OwnerInit(w.LocalPlayer), new LocationInit(pen.Location + new CVec(i, 1))]);
					Check(pen.Trait<MedievalLivestock>().Enter(animal), "Fixture animal must enter its pen");
				}
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 30))));
			var weather = w.WorldActor.Trait<MedievalWeather>();
			var rests = citizens.Select(a => a.Trait<MedievalRest>()).ToArray();
			if (alarmTick == 0 && weather.Storm.ShelterTicks == 0) Check(rests.All(r => !r.Resting), "Citizens hid before the thunder");
			if (alarmTick == 0 && weather.Storm.ShelterTicks > 0) alarmTick = tick;
			maxRunning = Math.Max(maxRunning, rests.Count(r => r.RunningForShelter));
			maxInside = Math.Max(maxInside, rests.Count(r => r.Sleeping));
			foreach (var r in rests.Where(r => r.RunningForShelter)) Check(((ISpeedModifier)r).GetSpeedModifier() == 165, "Refuge movement must use the running speed");
			foreach (var house in houses) Check(house.Trait<MedievalShelter>().Occupants(house) <= house.Trait<MedievalShelter>().Info.Beds, "House over capacity");
			Check(!soldier.Trait<MedievalRest>().Resting, "Militia must remain on duty during daytime thunder");
			if (rests.All(r => r.Sleeping) && manualTick == 0)
			{
				cargoSafe = citizens[0].Trait<MedievalWorker>().Cargo == 7;
				citizens[0].Trait<MedievalWorker>().Automatic = true;
				manualTick = tick;
				w.IssueOrder(new Order("Move", citizens[1], Target.FromCell(w, Cell(23, 33)), false));
				Game.Renderer.SaveScreenshot(Path.Combine(output, "citoyens-abrites.png"));
			}
			if (manualTick > 0 && tick > manualTick + 20 && weather.Storm.ShelterTicks > 0)
			{
				manualObeyed |= !rests[1].Resting;
				Check(rests.Where((_, i) => i != 1).All(r => r.Resting), "Citizens left shelter before the calm");
			}
			if (alarmTick > 0 && weather.Storm.ShelterTicks == 0 && tick < 1500)
			{
				resumed |= rests.All(r => !r.Resting);
				delivered |= citizens[0].Trait<MedievalWorker>().Cargo == 0 && w.ActorsWithTrait<MedievalStockpile>().Sum(p => p.Trait.Stock.Amount((int)Material.Wood)) == 7;
			}
			if (tick == 370) Game.Renderer.SaveScreenshot(Path.Combine(output, "course-vers-les-maisons.png"));
			if (tick == 1500)
			{
				var seasons = w.WorldActor.Trait<MedievalSeasons>();
				while (seasons.Clock.Season != VillageSeason.Winter || seasons.Clock.Snow < 950) seasons.Clock.Advance();
			}
			if (tick == 1600) Game.Renderer.SaveScreenshot(Path.Combine(output, "neige-enclos.png"));
			if (tick < 1650) return;
			Check(alarmTick > 0 && maxRunning >= 8 && maxInside == citizens.Count, "All citizens must run and reach an available home");
			Check(cargoSafe && delivered, "The physical load must survive sheltering and be delivered after the calm");
			Check(manualObeyed && resumed, "Manual movement or automatic return to work failed");
			var snowSurfaces = 0;
			Sync.RunUnsynced(w, () => snowSurfaces = pens.Sum(p => p.Trait<MedievalSnowCover>().SurfaceCount));
			Check(snowSurfaces > 20 && pens.All(p => p.Trait<MedievalLivestock>().Count >= 2), "Both occupied pens need rendered snow surfaces");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, alarmTick, maxRunning, maxInside,
				cargoSafe, delivered, manualObeyed, resumed, snowSurfaces, muted = Game.Settings.Sound.Mute }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL STORM REFUGE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(700, Game.Exit);
		}
	}
}
