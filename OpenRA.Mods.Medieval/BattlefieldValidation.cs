using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalBattlefieldValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalBattlefieldValidation();
	}
	public sealed class MedievalBattlefieldValidation : IWorldLoaded, ITick, INotifyGameSaved, INotifyGameLoaded
	{
		const string SaveName = "battlefield-validation.orasav";
		static string savedRemains;
		static bool savedPause;
		static int savedFeathered;
		readonly List<string> errors = [];
		readonly List<Actor> victims = [];
		WorldRenderer renderer;
		Actor house, warehouse, walker, hidden;
		string output;
		int tick, visible, feathered;
		bool done, pauseChecked, restoring, saved;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; restoring = w.IsLoadingGameSave; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "battlefield"); Directory.CreateDirectory(output); }
		void INotifyGameSaved.GameSaved(World w, bool isAutoSave) { saved = true; }
		void INotifyGameLoaded.GameLoaded(World w) => Game.RunAfterDelay(1, () => { Ui.CloseWindow(); w.SetPauseState(false); });
		static string Snapshot(MedievalBattlefield scene) => JsonSerializer.Serialize(scene.Remains.Select(r => new { position = r.Position.ToString(), r.Seed, r.Age, r.Building, r.Scale, r.Palette }));
		void Setup(World w)
		{
			var foe = w.Players.First(p => p.InternalName == "Rival");
			Actor Spawn(string name, int x, int y, bool enemy = false) => w.CreateActor("md." + name,
				[new OwnerInit(enemy ? foe : w.LocalPlayer), new LocationInit(Cell(x, y))]);
			house = Spawn("house", 27, 24); warehouse = Spawn("warehouse", 32, 25);
			Spawn("house", 24, 29); Spawn("church", 36, 29);
			var names = new[] { "builder", "hauler", "farmer", "militia", "manatarms", "archer", "royalguard", "standardbearer" };
			for (var i = 0; i < names.Length; i++)
			{
				var victim = Spawn(names[i], 25 + i * 2, 35);
				victim.TraitOrDefault<AutoTarget>()?.SetStance(victim, UnitStance.HoldFire);
				victims.Add(victim);
			}
			walker = Spawn("militia", 25, 37); walker.Trait<AutoTarget>().SetStance(walker, UnitStance.HoldFire);
			hidden = Spawn("royalguard", 8, 8, true); hidden.Trait<AutoTarget>().SetStance(hidden, UnitStance.HoldFire);
			Spawn("house", 9, 8, true);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			var scene = w.WorldActor.Trait<MedievalBattlefield>();
			var weather = w.WorldActor.Trait<MedievalWeather>();
			Sync.RunUnsynced(w, () =>
			{
				renderer.Viewport.Center(w.Map.CenterOfCell(tick >= 300 && tick < 400 ? Cell(26, 31) : Cell(31, 30)));
				var zoom = tick >= 400 ? 1.7f : 1.5f;
				renderer.Viewport.AdjustZoom((float)Math.Log(zoom / renderer.Viewport.Zoom));
			});
			if (tick == 40) foreach (var a in victims) a.Trait<Health>().InflictDamage(a, walker, new Damage(100000), true);
			if (tick == 90) house.Trait<Health>().InflictDamage(house, walker, new Damage(1000000), true);
			if (tick == 120) warehouse.Trait<Health>().InflictDamage(warehouse, walker, new Damage(1000000), true);
			if (!restoring && tick is 45 or 57 or 100 or 127 or 170 or 270 or 340 or 620) Game.Renderer.SaveScreenshot(Path.Combine(output, $"battlefield-{tick}.png"));
			if (tick == 175)
			{
				Check(scene.Remains.Count == 10 && scene.Remains.Count(r => r.Building) == 2, "Every killed person and building must leave one record.");
				Check(victims.All(a => a.Disposed) && house.Disposed && warehouse.Disposed, "Dead gameplay actors must be disposed normally.");
				Check(scene.Remains.All(r => r.Settled), "Fall and collapse animations must settle.");
				if (!restoring) w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(38, 35)), false));
			}
			if (!restoring && tick == 210)
			{
				w.SetPauseState(true);
				Game.RunAfterDelay(100, () =>
				{
					var ages = scene.Remains.Select(r => r.Age).ToArray(); var drops = weather.Scene.Drops.Select(d => d.Age).ToArray();
					Game.RunAfterDelay(160, () =>
					{
						pauseChecked = ages.SequenceEqual(scene.Remains.Select(r => r.Age)) && drops.SequenceEqual(weather.Scene.Drops.Select(d => d.Age));
						Check(pauseChecked, "Pause must freeze bodies, debris and snowfall."); w.SetPauseState(false);
					});
				});
			}
			if (tick == 290)
			{
				w.LocalPlayer.Shroud.Disabled = false;
				Check(w.LocalPlayer.Shroud.FogEnabled, "This scenario must exercise real fog of war.");
			}
			if (tick == 320)
			{
				Check(!hidden.CanBeViewedByPlayer(w.LocalPlayer), "An enemy outside vision must not be visible.");
				Check(!MedievalBattlefield.Visible(w, hidden.CenterPosition), "An unseen death must not reveal its location.");
				hidden.Trait<Health>().InflictDamage(hidden, walker, new Damage(100000), true);
			}
			if (!restoring && tick > 300 && tick < 390) feathered = Math.Max(feathered, w.WorldActor.Trait<MedievalFog>().FeatheredCells);
			if (tick == 400)
			{
				w.LocalPlayer.Shroud.Disabled = true;
				var season = w.WorldActor.Trait<MedievalSeasons>().Clock;
				while (season.Season != VillageSeason.Winter || season.Snow < 950) season.Advance();
			}
			if (tick > 450) visible = Math.Max(visible, scene.VisibleRemains);
			if (!restoring && tick == 650) w.RequestGameSave(SaveName, false);
			if (tick != 700) return;
			Check(scene.Remains.Count == 11 && scene.Remains.Take(10).All(r => r.Age == 300), "Corpses and ruins must remain after the transient effects finish.");
			Check(walker.Location == Cell(38, 35), "Bodies must not block a living soldier's route.");
			if (!restoring)
			{
				Check(weather.Scene.Drops.Any(d => d.Snow && d.Age > 25), "The winter scene must contain falling snow.");
				Check(feathered > 0 && visible >= 8, "Soft fog borders and battlefield remains must actually render.");
				Check(saved, "The save must be written before testing restoration.");
				if (errors.Count == 0)
				{
					savedRemains = Snapshot(scene); savedPause = pauseChecked; savedFeathered = feathered;
					var map = w.Map.Uid; done = true;
					File.WriteAllText(Path.Combine(output, "before-load.json"), JsonSerializer.Serialize(new { pauseChecked, feathered, visible }));
					Game.RunAfterDelay(1, () =>
					{
						Game.Disconnect(); Ui.ResetAll();
						Game.CreateAndStartLocalServer(map, [Order.FromTargetString("LoadGameSave", SaveName, true), Order.Command($"state {Session.ClientState.Ready}")]);
					});
					return;
				}
			}
			else Check(savedRemains == Snapshot(scene) && !w.IsLoadingGameSave, "Loading a saved game must restore the same corpses and ruins.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick,
				bodies = scene.Remains.Count(r => !r.Building), ruins = scene.Remains.Count(r => r.Building), restored = restoring,
				pauseChecked = restoring ? savedPause : pauseChecked, feathered = restoring ? savedFeathered : feathered, visible }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL BATTLEFIELD: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
