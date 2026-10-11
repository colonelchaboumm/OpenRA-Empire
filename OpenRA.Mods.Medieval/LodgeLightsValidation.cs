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
	public class MedievalLodgeLightsValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalLodgeLightsValidation();
	}
	public sealed class MedievalLodgeLightsValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		readonly List<Actor> houses = [];
		readonly List<string> errors = [];
		Actor lodge, site;
		int tick, litWindows;
		bool done, daylight, night, dawn, winter;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "lodge-lights"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			for (var i = 0; i < 6; i++) houses.Add(w.CreateActor("md.house", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(23 + i % 3 * 5, 25 + i / 3 * 7)), new MedievalHouseStyleInit(i)]));
			lodge = w.CreateActor("md.huntinglodge", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(33, 39))]);
			site = w.CreateActor("md.house", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(22, 39)), new MedievalConstructionInit()]);
		}
		void Capture(World w, string filename) => Game.Renderer.SaveScreenshot(Path.Combine(output, filename));
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 31))));
			if (tick == 100)
			{
				Sync.RunUnsynced(w, () => daylight = houses.All(a => MedievalHouseLights.Brightness(a) == 0));
				Capture(w, "village-jour.png");
			}
			if (tick == 1100)
			{
				Sync.RunUnsynced(w, () =>
				{
					night = houses.All(a => MedievalHouseLights.Brightness(a) >= .99f);
					litWindows = houses.Sum(a => a.Trait<MedievalHouseLights>().Windows.Count);
					Check(MedievalHouseLights.Brightness(site) == 0, "An unfinished house must stay dark");
				});
				Check(lodge.Trait<MedievalChimney>().Smoke.Puffs.Count > 0, "Hunter cabin chimney must emit smoke");
				Check(lodge.Info.TraitInfo<BuildingInfo>().OccupiedTiles(lodge.Location).Count() == 4, "Cabin must keep its 2x2 footprint");
				Capture(w, "village-nuit.png");
			}
			if (tick == 1900)
			{
				Sync.RunUnsynced(w, () => dawn = houses.All(a => MedievalHouseLights.Brightness(a) == 0));
				Capture(w, "village-matin.png");
			}
			if (tick == 1950)
			{
				var seasons = w.WorldActor.Trait<MedievalSeasons>();
				while (seasons.Clock.Season != VillageSeason.Winter || seasons.Clock.Snow < 950) seasons.Clock.Advance();
			}
			if (tick == 2050)
			{
				Sync.RunUnsynced(w, () => winter = lodge.Trait<MedievalSnowCover>().SurfaceCount > 0);
				Capture(w, "cabane-hiver.png");
			}
			if (tick < 2100) return;
			Check(daylight && night && dawn, "Six house styles must illuminate at night and turn off at dawn");
			Check(winter, "The new hunting cabin needs aligned winter roof coverage");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick,
				daylight, night, dawn, winter, litWindows, houseStyles = houses.Select(a => a.Trait<MedievalHouseAppearance>().Style).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL LODGE LIGHTS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(700, Game.Exit);
		}
	}
}
