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
	public class MedievalActionParticlesValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalActionParticlesValidation();
	}
	public sealed class MedievalActionParticlesValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<Actor> soldiers = [];
		readonly HashSet<VillageActionKind> captured = [];
		readonly HashSet<VillageActionKind> closeups = [];
		WorldRenderer renderer;
		Actor wood, stone, gold, farm, forge;
		string output;
		int tick, peak, populationPeak, stoppedTotal, zoomTotal;
		bool done, pauseChecked;
		WPos closePosition;
		VillageActionKind closeKind;
		int closeStart, closeUntil;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "action-particles", "native"); Directory.CreateDirectory(output); }
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y, bool neutral = false) => w.CreateActor("md." + name,
				[new OwnerInit(neutral ? w.Players.First(p => p.InternalName == "Neutral") : w.LocalPlayer), new LocationInit(Cell(x, y))]);
			Spawn("market", 29, 27).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn("house", 25, 26); Spawn("house", 26, 24);
			wood = Spawn("sawmill", 23, 31); stone = Spawn("quarry", 32, 32); gold = Spawn("goldmine", 37, 30);
			Spawn("tree", 21, 34, true); Spawn("stone", 33, 36, true); Spawn("gold", 39, 34, true);
			Spawn("lumberjack", 22, 34); Spawn("stonecutter", 33, 35); Spawn("goldminer", 38, 34);
			farm = Spawn("farm", 23, 39); Spawn("field.4x4", 26, 40); Spawn("farmer", 26, 39); Spawn("farmer", 27, 39);
			var depot = Spawn("warehouse", 33, 40).Trait<MedievalStockpile>().Stock; depot.Add(0, 250); depot.Add(3, 200);
			forge = Spawn("forge", 37, 39);
			forge.Trait<MedievalMilitaryWorkshop>().AssignStaff(forge, Spawn("blacksmith", 37, 42));
			for (var i = 0; i < 3; i++) w.IssueOrder(new Order("MedievalEquipmentTarget", w.LocalPlayer.PlayerActor, false) { ExtraData = 0, TargetString = "+" });
			for (var i = 0; i < 20; i++)
			{
				var soldier = Spawn("militia", 23 + i % 10, 47 + i / 10); soldiers.Add(soldier);
				soldier.Trait<AutoTarget>().SetStance(soldier, UnitStance.HoldFire);
			}
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			var particles = w.WorldActor.Trait<MedievalActionParticles>(); var scene = particles.Scene;
			Sync.RunUnsynced(w, () =>
			{
				renderer.Viewport.Center(tick < closeUntil ? closePosition : w.Map.CenterOfCell(Cell(30, 36)));
				var zoom = tick >= 2000 && tick < 2080 ? .5f : tick < closeUntil ? 2.4f : 1.25f;
				renderer.Viewport.AdjustZoom((float)Math.Log(zoom / renderer.Viewport.Zoom));
			});
			peak = Math.Max(peak, scene.Bursts.Count); populationPeak = Math.Max(populationPeak, particles.Observed);
			Check(scene.Bursts.Count <= VillageActionScene.MaximumBursts && particles.Observed <= MedievalActionParticles.MaximumObserved, "Effects and tracked people must remain bounded.");
			if (tick < 2100 && tick % 160 == 20)
				for (var i = 0; i < soldiers.Count; i++)
					w.IssueOrder(new Order("Move", soldiers[i], Target.FromCell(w, Cell(tick / 160 % 2 == 0 ? 39 : 22, 45 + i % 3)), false));
			foreach (var b in scene.Bursts)
				if (b.Age is >= 4 and <= 12 && captured.Add(b.Kind)) Game.Renderer.SaveScreenshot(Path.Combine(output, b.Kind + ".png"));
			if (tick < 1500 && tick >= closeUntil)
				foreach (var b in scene.Bursts)
					if (closeups.Add(b.Kind)) { closePosition = b.Position; closeKind = b.Kind; closeStart = tick; closeUntil = tick + 40; break; }
			if (tick > closeStart + 4 && tick < closeUntil && (tick - closeStart) % 3 == 0)
				Game.Renderer.SaveScreenshot(Path.Combine(output, $"close-{closeKind}-{tick - closeStart:00}.png"));
			if (tick is 1540 or 1900) Game.Renderer.SaveScreenshot(Path.Combine(output, $"village-{tick}.png"));
			if (tick == 400)
			{
				w.SetPauseState(true);
				Game.RunAfterDelay(100, () =>
				{
					var ages = scene.Bursts.Select(b => b.Age).ToArray(); var totals = scene.Totals.ToArray(); var sync = Sync.HashActor(self);
					Game.RunAfterDelay(160, () =>
					{
						pauseChecked = ages.SequenceEqual(scene.Bursts.Select(b => b.Age)) && totals.SequenceEqual(scene.Totals);
						Check(pauseChecked, "Pause must freeze the particles and stop emission.");
						Check(sync == Sync.HashActor(self), "Rendering during pause cannot modify synchronized world state.");
						w.SetPauseState(false);
					});
				});
			}
			if (tick == 1600)
				w.AddFrameEndTask(world =>
				{
					for (var i = 0; i < 600; i++)
					{
						var a = world.CreateActor("md.archer", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(20 + i % 30, 44 + i / 30))]);
						soldiers.Add(a);
					}
				});
			if (tick == 2020) zoomTotal = scene.Totals.Sum();
			if (tick == 2060) Check(scene.Bursts.Count == 0 && scene.Totals.Sum() == zoomTotal && particles.Observed == 0, "Very distant zoom must remove detailed particles and actor observations.");
			if (tick == 2100)
				foreach (var a in w.ActorsHavingTrait<Mobile>()) w.IssueOrder(new Order("Stop", a, false));
			if (tick == 2200) stoppedTotal = scene.Totals.Sum();
			if (tick != 2300) return;
			Check(stoppedTotal == scene.Totals.Sum() && scene.Bursts.Count == 0, "Stopped workers and soldiers must not keep emitting effects.");
			foreach (var kind in Enum.GetValues<VillageActionKind>()) Check(scene.Totals[(int)kind] > 0, "Missing actual action effect: " + kind);
			Check(wood.Trait<MedievalStockpile>().Stock.Amount(0) > 0 && stone.Trait<MedievalStockpile>().Stock.Amount(3) > 0 && gold.Trait<MedievalStockpile>().Stock.Amount(4) > 0,
				"Visual effects must accompany actual delivered wood, stone and gold.");
			Check(pauseChecked && soldiers.Count == 620, "Pause and dense-army stages must run.");
			Check(MedievalEquipment.Amount(w.LocalPlayer, 0) > 0, "Forge sparks must accompany completed equipment production.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, peak, populationPeak, pauseChecked,
				army = soldiers.Count, totals = Enum.GetValues<VillageActionKind>().ToDictionary(k => k.ToString(), k => scene.Totals[(int)k]), captured }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL ACTION PARTICLES: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
