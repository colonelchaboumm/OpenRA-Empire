using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalShadowsValidationInfo : TraitInfo
	{
		public readonly bool Large = false;
		public override object Create(ActorInitializer init) => new MedievalShadowsValidation(this);
	}
	// Opt-in native scenario; never loaded by the normal mod.
	public sealed class MedievalShadowsValidation : IWorldLoaded, ITick
	{
		readonly MedievalShadowsValidationInfo info;
		readonly List<string> errors = [];
		readonly List<object> samples = [];
		WorldRenderer renderer;
		string output;
		int tick;
		bool done;
		Actor walker, tree, stone, sleeper;
		WPos initialWalker;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		public MedievalShadowsValidation(MedievalShadowsValidationInfo info) { this.info = info; }
		Actor Spawn(World w, string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "shadows", info.Large ? "large" : "native"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			if (info.Large) return;
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			var names = new[] { "house", "market", "church", "woodtower", "sawmill", "forge", "sheepfold", "huntinglodge" };
			for (var i = 0; i < names.Length; i++) Spawn(w, names[i], 24 + i % 4 * 5, 24 + i / 4 * 6);
			Spawn(w, "field.4x3", 24, 36); Spawn(w, "sheeppasture.4x3", 31, 36);
			Spawn(w, "cow", 33, 38); Spawn(w, "sheep", 32, 37);
			Spawn(w, "supplycart", 29, 34); Spawn(w, "trebuchet", 38, 36); Spawn(w, "ram", 38, 40);
			for (var i = 0; i < 6; i++) Spawn(w, "palisade", 24 + i, 42);
			Spawn(w, "banner", 33, 42); Spawn(w, "torch", 31, 42);
			tree = Spawn(w, "tree", 20, 31); Spawn(w, "pine", 20, 35); Spawn(w, "birch", 20, 39);
			stone = Spawn(w, "stone", 24, 40);
			walker = Spawn(w, "militia", 30, 31); initialWalker = walker.CenterPosition;
			sleeper = Spawn(w, "builder", 26, 26);
			for (var i = 0; i < 5; i++) Spawn(w, "archer", 30 + i, 34);
		}
		void SetTime(World w, int phase, VillageSeason season)
		{
			var day = w.WorldActor.Trait<MedievalDayNight>().Clock;
			while (day.Phase != phase) day.Advance();
			var clock = w.WorldActor.Trait<MedievalSeasons>().Clock;
			while (clock.Season != season || clock.Elapsed < 1000) clock.Advance();
		}
		void Inspect(World w, string name)
		{
			Sync.RunUnsynced(w, () =>
			{
				var sun = w.WorldActor.Trait<MedievalSunlight>();
				var day = w.WorldActor.Trait<MedievalDayNight>();
				if (day.ShadingQuads > 64 * 48) errors.Add("Night lighting exceeds its bounded viewport mesh.");
				var actors = w.ScreenMap.RenderableActorsInBox(renderer.Viewport.TopLeft, renderer.Viewport.BottomRight).ToArray();
				var counts = new Dictionary<string, int>(); var casts = 0; var ground = 0;
				foreach (var a in actors)
				{
					foreach (var r in a.Render(renderer))
					{
						if (r is not IGroundLayerRenderable layer) continue;
						if (layer.GroundLayer != 2) { ground++; continue; }
						casts++; counts[a.Info.Name] = counts.GetValueOrDefault(a.Info.Name) + 1;
						var bounds = r.PrepareRender(renderer).ScreenBounds(renderer);
						if (bounds.Width > 1500 || bounds.Height > 1500) errors.Add("Unbounded shadow: " + a.Info.Name);
						if (w.FogObscures(a) || a.TraitOrDefault<MedievalRest>()?.Sleeping == true) errors.Add("Hidden actor casts shadow: " + a.Info.Name);
					}
				}
				if (casts < (info.Large ? 80 : 20)) errors.Add("Too few visible shadows in " + name + ": " + casts);
				if (!info.Large && ground < 3) errors.Add("Agricultural ground layers missing");
				samples.Add(new { name, actors = actors.Length, casts, ground, counts, opacity = sun.Shadow.Opacity, length = sun.Shadow.Length,
					direction = new[] { sun.Shadow.Direction.X, sun.Shadow.Direction.Y }, zoom = renderer.Viewport.Zoom, shadingQuads = day.ShadingQuads,
					renderWorldMs = PerfHistory.Items["render_world"].Average(25), rain = w.WorldActor.Trait<MedievalWeather>().RainPermille });
				Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
			});
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			// Keep the tested objects in view even if the desktop pointer touches an edge.
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(info.Large ? w.Map.ProjectedTopLeft + (w.Map.ProjectedBottomRight - w.Map.ProjectedTopLeft) / 2 : w.Map.CenterOfCell(Cell(29, 32))));
			if (tick == 2)
			{
				Sync.RunUnsynced(w, () =>
				{
					renderer.Viewport.Center(info.Large ? w.Map.ProjectedTopLeft + (w.Map.ProjectedBottomRight - w.Map.ProjectedTopLeft) / 2 : w.Map.CenterOfCell(Cell(29, 32)));
					if (info.Large) renderer.Viewport.AdjustZoom((float)Math.Log(renderer.Viewport.MinZoom * .25f / renderer.Viewport.Zoom));
				});
				SetTime(w, 1600, VillageSeason.Spring);
			}
			if (tick == 40) Inspect(w, "printemps-matin");
			if (tick == 60) SetTime(w, 6000, VillageSeason.Summer);
			if (tick == 100) Inspect(w, "ete-midi");
			if (tick == 120) SetTime(w, 10200, VillageSeason.Autumn);
			if (tick == 160) Inspect(w, "automne-soir");
			if (tick == 180) SetTime(w, 6000, VillageSeason.Winter);
			if (tick == 220) Inspect(w, "hiver-midi");
			if (tick == 240)
			{
				SetTime(w, 13500, VillageSeason.Winter);
				if (!info.Large) w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(35, 33)), false));
			}
			if (tick == 280) Inspect(w, "hiver-nuit");
			if (tick == 300 && !info.Large)
			{
				if (!sleeper.Trait<MedievalRest>().Sleeping) errors.Add("Sleeping citizen was not exercised");
				Sync.RunUnsynced(w, () => { if (sleeper.Render(renderer).Any()) errors.Add("Sleeping citizen still draws body or shadow"); });
				SetTime(w, 2000, VillageSeason.Winter);
				tree.Trait<MedievalSource>().Harvest(200); stone.Trait<MedievalSource>().Harvest(300);
			}
			if (tick == 320 && !info.Large) Inspect(w, "abattage-pierre");
			if (tick == 340 && !info.Large) SetTime(w, 6000, VillageSeason.Spring);
			if (tick == 570 && !info.Large)
			{
				Inspect(w, "pluie-midi");
				var weather = w.WorldActor.Trait<MedievalWeather>();
				Sync.RunUnsynced(w, () => { if (weather.RainPermille < 900 || w.WorldActor.Trait<MedievalSunlight>().Shadow.Opacity > .10f) errors.Add("Rain does not attenuate shadows"); });
			}
			if (tick < (info.Large ? 370 : 610)) return;
			if (!info.Large && walker.CenterPosition == initialWalker) errors.Add("Moving silhouette not exercised");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, samples }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SHADOWS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(700, Game.Exit);
		}
	}
}
