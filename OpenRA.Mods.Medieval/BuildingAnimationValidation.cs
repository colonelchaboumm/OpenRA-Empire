using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalBuildingAnimationValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalBuildingAnimationValidation();
	}
	// Real deliveries, rendered frames and pause behavior; not enabled in the playable mod.
	public sealed class MedievalBuildingAnimationValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick;
		bool finished;
		bool pausePassed;
		Actor sawmill, quarry, market, house, secondHouse, site, lumberjack, stonecutter;
		int[] stoppedStocks;
		readonly List<string> errors = [];
		readonly Dictionary<string, HashSet<int>> frames = new() { ["sawmill"] = [], ["quarry"] = [], ["market"] = [] };
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "building-animation-preview");
			Directory.CreateDirectory(output);
		}
		void Check(bool ok, string message) { if (!ok) errors.Add(message); }
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null
				|| a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y, bool neutral = false) => w.CreateActor("md." + name,
				[new OwnerInit(neutral ? w.Players.First(p => p.InternalName == "Neutral") : w.LocalPlayer), new LocationInit(Cell(x, y))]);
			house = Spawn("house", 24, 27);
			secondHouse = Spawn("house", 21, 26);
			market = Spawn("market", 30, 29);
			market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			sawmill = Spawn("sawmill", 24, 35);
			quarry = Spawn("quarry", 32, 35);
			Spawn("tree", 21, 38, true);
			Spawn("stone", 35, 39, true);
			lumberjack = Spawn("lumberjack", 24, 38);
			stonecutter = Spawn("stonecutter", 34, 38);
			site = w.CreateActor("md.house", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(38, 28)), new MedievalConstructionInit()]);
		}
		void ITick.Tick(Actor self)
		{
			if (finished || self.World.LocalPlayer == null) return;
			var w = self.World;
			tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 2) renderer.Viewport.Center(w.Map.CenterOfCell(Cell(28, 32)));
			foreach (var a in new[] { sawmill, quarry, market })
			{
				var motion = a.Trait<MedievalBuildingAnimation>();
				if (motion.Operating) frames[a.Info.Name[3..]].Add(a.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.CurrentFrame);
			}
			if (tick == 25)
			{
				Check(!sawmill.Trait<MedievalBuildingAnimation>().Operating && !quarry.Trait<MedievalBuildingAnimation>().Operating, "Empty production buildings must wait for a real delivery.");
				Shot("01-idle-village");
			}
			if (tick == 300)
			{
				Check(site.Trait<MedievalChimney>().Smoke.Puffs.Count == 0, "A construction site must not smoke.");
				Check(house.Trait<MedievalChimney>().Smoke.Puffs.Count > 0, "A completed house must emit smoke.");
				Check(house.Trait<MedievalChimney>().Smoke.Puffs[0].Age != secondHouse.Trait<MedievalChimney>().Smoke.Puffs[0].Age, "House smoke must not be synchronized.");
				w.SetPauseState(true);
				Game.RunAfterDelay(120, () =>
				{
					var age = house.Trait<MedievalChimney>().Smoke.Puffs[0].Age;
					var frame = market.Trait<MedievalBuildingAnimation>().Frame;
					Game.RunAfterDelay(120, () =>
					{
						pausePassed = age == house.Trait<MedievalChimney>().Smoke.Puffs[0].Age && frame == market.Trait<MedievalBuildingAnimation>().Frame;
						Check(pausePassed, "Smoke and vendor must stop during a paused game.");
						w.SetPauseState(false);
					});
				});
			}
			var subjects = new[] { sawmill, quarry, market, house };
			if (tick >= 400 && tick < 1200)
			{
				var subject = subjects[(tick - 400) / 200];
				if ((tick - 400) % 200 == 0)
				{
					renderer.Viewport.AdjustZoom((float)Math.Log(2f / renderer.Viewport.Zoom));
				}
				// Keep the camera anchored even if the desktop pointer is on a scroll edge.
				renderer.Viewport.Center(subject.CenterPosition);
				if ((tick - 400) % 8 == 4) Shot($"{subject.Info.Name[3..]}-{tick:0000}");
			}
			if (tick == 1200)
			{
				w.IssueOrder(new Order("Stop", lumberjack, false));
				w.IssueOrder(new Order("Stop", stonecutter, false));
				renderer.Viewport.AdjustZoom((float)Math.Log(1f / renderer.Viewport.Zoom));
				renderer.Viewport.Center(w.Map.CenterOfCell(Cell(28, 32)));
			}
			if (tick == 1250) stoppedStocks = [sawmill.Trait<MedievalStockpile>().Stock.Hash, quarry.Trait<MedievalStockpile>().Stock.Hash];
			if (tick != 1800) return;
			foreach (var a in new[] { sawmill, quarry })
			{
				var motion = a.Trait<MedievalBuildingAnimation>();
				Check(motion.Deliveries > 0 && motion.CompletedCycles > 0, a.Info.Name + ": real workers must trigger full mechanism cycles.");
				Check(!motion.Operating, a.Info.Name + ": mechanism must rest after processing ends.");
			}
			Check(frames.All(p => p.Value.Count == 24), "All 24 articulated poses must be played by each building in the engine.");
			Check(stoppedStocks.SequenceEqual(new[] { sawmill.Trait<MedievalStockpile>().Stock.Hash, quarry.Trait<MedievalStockpile>().Stock.Hash }), "Decorative animations must not change stock.");
			Check(house.Trait<MedievalChimney>().Smoke.Puffs.Count <= ChimneySmoke.MaxPuffs, "Smoke particles must remain bounded.");
			Check(site.Trait<MedievalChimney>().Smoke.Puffs.Count == 0, "The unfinished house must remain without smoke.");
			Shot("02-settled-village");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors,
				frames = frames.ToDictionary(p => p.Key, p => p.Value.Count), pausePassed,
				sawmillDeliveries = sawmill.Trait<MedievalBuildingAnimation>().Deliveries,
				quarryDeliveries = quarry.Trait<MedievalBuildingAnimation>().Deliveries,
				wood = sawmill.Trait<MedievalStockpile>().Stock.Amount(0), stone = quarry.Trait<MedievalStockpile>().Stock.Amount(3),
				housePuffs = house.Trait<MedievalChimney>().Smoke.Puffs.Count }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL BUILDING ANIMATIONS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true;
			Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
