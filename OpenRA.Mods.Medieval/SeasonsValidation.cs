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
	public class MedievalSeasonsValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSeasonsValidation();
	}
	// Opt-in native scenario. Production seasons still last 45,000 simulation ticks.
	public sealed class MedievalSeasonsValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor lodge, depot, market, walker, hunter;
		readonly List<string> errors = [];
		readonly HashSet<VillageSeason> seasons = [];
		readonly HashSet<string> huntingPoses = [];
		bool arrow, falling, snowfall, settledLeaves;
		int tick, maxTracks, harvested;
		bool carried, stored, hauled, recruited, built, done;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		Actor Spawn(World w, string name, int x, int y, Player owner = null) => w.CreateActor("md." + name, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "seasons-hunting"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			depot = Spawn(w, "warehouse", 24, 25); depot.Trait<MedievalStockpile>().Stock.Add(0, 100); depot.Trait<MedievalStockpile>().Stock.Add(3, 40);
			market = Spawn(w, "market", 27, 24); market.Trait<MedievalStockpile>().Stock.Add(1, 160);
			Spawn(w, "house", 24, 29); Spawn(w, "builder", 24, 28);
			walker = Spawn(w, "militia", 20, 30);
			var neutral = w.Players.First(p => p.InternalName == "Neutral");
			Spawn(w, "game-deer", 35, 28, neutral); Spawn(w, "game-rabbit", 36, 31, neutral);
			Spawn(w, "tree", 37, 27, neutral); Spawn(w, "birch", 35, 33, neutral); Spawn(w, "pine", 38, 29, neutral);
			w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor, Target.FromCell(w, Cell(30, 29)), false) { TargetString = "md.huntinglodge" });
			w.IssueOrder(new Order("MedievalRecruit", w.LocalPlayer.PlayerActor, false) { TargetString = "md.hunter" });
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			var season = w.WorldActor.Trait<MedievalSeasons>(); seasons.Add(season.Clock.Season);
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 28))));
			lodge ??= w.Actors.FirstOrDefault(a => a.Info.Name == "md.huntinglodge");
			hunter ??= w.Actors.FirstOrDefault(a => a.Info.Name == "md.hunter");
			built |= lodge?.Trait<MedievalBuilding>().Complete == true;
			recruited |= hunter != null;
			Sync.RunUnsynced(w, () =>
			{
				var pose = hunter?.Trait<WithMedievalInfantryBody>().Sequence;
				if (pose?.StartsWith("hunt-", StringComparison.Ordinal) == true && huntingPoses.Add(pose))
					Game.Renderer.SaveScreenshot(Path.Combine(output, pose + ".png"));
				if (!arrow && w.Effects.OfType<HuntingArrowEffect>().Any(e => e.Age > 4))
				{ arrow = true; Game.Renderer.SaveScreenshot(Path.Combine(output, "fleche.png")); }
				if (!falling && w.ActorsWithTrait<MedievalGameAnimal>().Any(p => p.Trait.Down && p.Trait.FallAge is > 10 and < 30))
				{ falling = true; Game.Renderer.SaveScreenshot(Path.Combine(output, "chute.png")); }
				snowfall |= w.WorldActor.Trait<MedievalWeather>().Scene.Drops.Any(d => d.Snow && d.Age > 15);
				settledLeaves |= w.WorldActor.Trait<MedievalAmbience>().Scene.Particles.Any(p => p.Kind == AmbientKind.Leaf && p.Settled);
			});
			carried |= hunter?.Trait<MedievalWorker>().Cargo > 0;
			if (!stored && lodge?.Trait<MedievalStockpile>().Stock.Amount(1) > 0)
			{
				stored = true;
				w.AddFrameEndTask(world => Spawn(world, "hauler", 27, 29));
			}
			hauled |= stored && depot.Trait<MedievalStockpile>().Stock.Amount(1) > 0;
			harvested = 30 - w.ActorsWithTrait<MedievalGameAnimal>().Sum(p => p.Actor.Trait<MedievalSource>().Remaining);
			if (tick % 130 == 10 && walker != null)
				w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(tick % 260 < 130 ? 33 : 21, 33)), false));
			Sync.RunUnsynced(w, () => maxTracks = Math.Max(maxTracks, season.TrackCount));
			if (tick is 350 or 950 or 1550 or 2270)
				Game.Renderer.SaveScreenshot(Path.Combine(output, SeasonClock.Name(season.Clock.Season) + ".png"));
			// Thunder now sends the hunter and builder home. Let the real job finish
			// after sheltering, while retaining a bounded deadline and exact totals.
			if (tick < 3300 || tick < 9000 && (harvested < 30 || !hauled)) return;
			if (!built || !recruited || !carried || !stored || !hauled) errors.Add($"Hunting chain incomplete: built={built}, recruited={recruited}, carried={carried}, cabin={stored}, hauled={hauled}, hunter={hunter?.Trait<MedievalWorker>().Description}");
			if (harvested != 30) errors.Add($"Finite prey harvest expected 30, got {harvested}; hunter={hunter?.Trait<MedievalWorker>().Description}, rest={hunter?.Trait<MedievalRest>().Description}");
			// Recruitment removed 10 food; meals are accounted by the ordinary economy.
			var total = w.ActorsWithTrait<MedievalStockpile>().Where(p => p.Actor.Owner == w.LocalPlayer).Sum(p => p.Trait.Stock.Amount(1))
				+ w.ActorsWithTrait<MedievalWorker>().Where(p => p.Actor.Owner == w.LocalPlayer && p.Trait.Resource == 1 && !p.Trait.EquipmentCargo && !p.Trait.CampCargo).Sum(p => p.Trait.Cargo);
			var consumed = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
			if (total + consumed + (30 - harvested) != 180) errors.Add($"Food conservation failed: stored/cargo={total}, meals={consumed}, harvested={harvested}");
			if (seasons.Count != 4 || season.Clock.Year < 2) errors.Add("Incomplete season cycle");
			if (maxTracks == 0 || maxTracks > MedievalSeasons.MaximumTracks) errors.Add("Snow footprints missing or unbounded");
			if (!arrow || !falling || !snowfall || !settledLeaves || huntingPoses.Count != 4) errors.Add($"Missing presentation: arrow={arrow}, falling={falling}, snow={snowfall}, leaves={settledLeaves}, poses={string.Join(',', huntingPoses)}");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, built, recruited, carried, stored, hauled, harvested, foodRemaining = total, maxTracks, seasons = seasons.Count, year = season.Clock.Year, arrow, falling, snowfall, settledLeaves, huntingPoses }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SEASONS/HUNTING: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(800, Game.Exit);
		}
	}
}
