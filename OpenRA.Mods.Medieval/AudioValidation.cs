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
	public class MedievalAudioValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalAudioValidation();
	}
	public sealed class MedievalAudioValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		MedievalAudio audio;
		Actor site, sawmill, quarry, patrol, church, goldmine;
		string output;
		int tick, decoded;
		bool paused, finished, muted;
		float soloDrumGain;
		int daytimeBirds, daytimeBells;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; audio = w.WorldActor.Trait<MedievalAudio>();
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "audio"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"status\":\"running\"}");
			foreach (var entry in MedievalAudio.Bank)
				for (var v = 1; v <= entry.Value.Variants; v++)
				{
					var name = "medieval|audio/" + entry.Key + (entry.Key == "drum" ? "" : "-" + v) + ".wav";
					using var stream = w.Map.Open(name); var valid = false;
					foreach (var loader in Game.ModData.SoundLoaders)
					{
						stream.Position = 0;
						if (!loader.TryParseSound(stream, out var sound)) continue;
						using (sound) valid = sound.SampleRate == 22050 && sound.SampleBits == 16 && sound.Channels == 1 && sound.LengthInSeconds > .05f;
						break;
					}
					Check(valid, "Engine could not decode " + name); if (valid) decoded++;
				}
			Check(!w.Map.Rules.InstalledMusic.Any(), "Only ambience and effects were requested; the music playlist must be empty.");
		}
		void Setup(World w)
		{
			Actor Spawn(string type, int x, int y, bool neutral = false) => w.CreateActor("md." + type,
				[new OwnerInit(neutral ? w.Players.Single(p => p.InternalName == "Neutral") : w.LocalPlayer), new LocationInit(Cell(x, y))]);
			Spawn("house", 25, 27); Spawn("house", 23, 27);
			var depot = Spawn("warehouse", 30, 24); depot.Trait<MedievalStockpile>().Stock.Add(1, 240);
			depot.Trait<MedievalStockpile>().Stock.Add(0, 200); depot.Trait<MedievalStockpile>().Stock.Add(3, 200);
			var market = Spawn("market", 30, 29); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			sawmill = Spawn("sawmill", 24, 35); quarry = Spawn("quarry", 31, 35);
			church = Spawn("church", 20, 33);
			goldmine = Spawn("goldmine", 34, 25);
			Spawn("goldminer", 36, 24); Spawn("gold", 37, 23, true);
			Spawn("tree", 21, 38, true); Spawn("tree", 22, 39, true); Spawn("stone", 35, 39, true);
			Spawn("tree", 20, 26, true).Trait<MedievalSource>().Remaining = 10000;
			Spawn("lumberjack", 24, 38); Spawn("stonecutter", 34, 38);
			Spawn("farm", 37, 30); var field = Spawn("field", 40, 30); field.Trait<MedievalSource>().Remaining = 40;
			Spawn("farmer", 39, 32);
			Spawn("ranch", 36, 36); var pasture = Spawn("pasture", 39, 38); pasture.Trait<MedievalSource>().Remaining = 30;
			Spawn("breeder", 38, 39); Spawn("forge", 24, 30); Spawn("blacksmith", 26, 30);
			Spawn("cow", 36, 39); Spawn("sheep", 26, 34);
			foreach (var name in new[] { "cow", "sheep" })
				w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(28, 34)), new MedievalYoungAnimalInit()]);
			Spawn("drummer", 29, 32); Spawn("standardbearer", 28, 32);
			patrol = Spawn("militia", 22, 31);
			patrol.Trait<AutoTarget>().SetStance(patrol, UnitStance.HoldFire);
			w.IssueOrder(new Order("MedievalEquipmentTarget", w.LocalPlayer.PlayerActor, false) { ExtraData = 0, TargetString = "+" });
			w.IssueOrder(new Order("MedievalRecruit", w.LocalPlayer.PlayerActor, false) { TargetString = "md.farmer" });
			Spawn("woodgate", 35, 23);
			var cart = Spawn("supplycart", 34, 21); w.IssueOrder(new Order("Move", cart, Target.FromCell(w, Cell(40, 23)), false));
			var rival = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask;
			for (var i = 0; i < 2; i++)
			{
				var fighter = Spawn(i == 0 ? "archer" : "swordsman", 29 + i * 3, 42);
				var target = w.CreateActor("md.royalguard", [new OwnerInit(rival), new LocationInit(Cell(29 + i * 3, 44))]);
				fighter.Trait<AutoTarget>().SetStance(fighter, UnitStance.HoldFire); target.Trait<AutoTarget>().SetStance(target, UnitStance.HoldFire);
				w.IssueOrder(new Order("ForceAttack", fighter, Target.FromActor(target), false));
			}
			site = w.CreateActor("md.house", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(38, 26)), new MedievalConstructionInit()]);
			for (var r = 0; r < 4; r++) site.Trait<MedievalBuilding>().Materials.Add(r, site.Trait<MedievalBuilding>().Info.Cost[r]);
			Spawn("builder", 37, 27);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 32))));
			if (tick == 25)
			{
				var route = w.Map.FindTilesInAnnulus(patrol.Location, 2, 5)
					.Where(c => w.Map.Contains(c) && patrol.Trait<Mobile>().CanEnterCell(c, check: BlockedByActor.Immovable)).Take(3).ToArray();
				Check(route.Length == 3, "The patrol fixture needs three accessible waypoints.");
				w.IssueOrder(new Order("MedievalStartPatrol", patrol, false) { TargetString = PatrolRoute.Encode(route) });
				var far = w.Map.CenterOfCell(Cell(2, 2));
				Check(!audio.Play("ram-hit", far), "An offscreen impact must be inaudible.");
				Game.Sound.DisableWorldSounds = true;
				muted = !audio.Play("ram-hit", site.CenterPosition);
				Game.Sound.DisableWorldSounds = false;
				Check(muted, "The existing world sound mute must be honored.");
			}
			if (tick == 300)
			{
				w.SetPauseState(true);
				Game.RunAfterDelay(250, () =>
				{
					paused = audio.ActiveVoices == 0 && !audio.Play("ram-hit", site.CenterPosition);
					Check(paused, "Pause must stop the ambience and prevent new effects.");
					w.SetPauseState(false);
				});
			}
			if (tick == 350)
			{
				soloDrumGain = audio.LastDrumGain;
				Check(audio.Played.ContainsKey("drum") && soloDrumGain == 1, "A single fed drummer must play at the normal volume.");
				w.AddFrameEndTask(world =>
				{
					for (var i = 0; i < 2; i++) world.CreateActor("md.drummer", [new OwnerInit(world.LocalPlayer), new LocationInit(Cell(29 + i, 33))]);
				});
			}
			if (tick == 950)
			{
				Check(!audio.Played.ContainsKey("night") && !audio.Played.ContainsKey("owl"), "Night insects and owls must not play in daylight.");
				daytimeBirds = audio.Played.GetValueOrDefault("bird");
				daytimeBells = audio.Played.GetValueOrDefault("church-bell");
				var day = w.WorldActor.Trait<MedievalDayNight>().Clock;
				while (!day.Night) day.Advance();
			}
			if (tick == 1000) Game.Renderer.SaveScreenshot(Path.Combine(output,"eglise-de-nuit.png"));
			if (tick < 1450) return;
			foreach (var cue in new[] { "step", "axe", "pick", "hammer", "harvest", "load", "saw", "pulley", "wind", "bird", "cattle", "market", "fire", "tree-fall", "complete", "ready", "anvil", "sword", "bow", "hit", "cart", "gate",
				"sheep", "calf", "lamb", "hoof", "forge", "combat-cry", "rally", "patrol", "night", "owl", "drum-group" })
				Check(audio.Played.ContainsKey(cue), "Real village activity did not trigger " + cue);
			Check(daytimeBells > 0 && audio.Played.GetValueOrDefault("church-bell") > daytimeBells, "Church bells must sound at completion and again at nightfall.");
			Check(audio.Played.ContainsKey("gold-sort") && goldmine.Trait<MedievalStockpile>().Stock.Amount(4) > 0, "Actual gold deliveries must produce sorting foley.");
			Check(site.Trait<MedievalBuilding>().Complete, "Builders must still finish the house.");
			Check(sawmill.Trait<MedievalStockpile>().Stock.Amount(0) > 0 && quarry.Trait<MedievalStockpile>().Stock.Amount(3) > 0, "Workers must still deliver real wood and stone.");
			Check(audio.PeakVoices <= 18 && paused && muted, "Mixing limits, pause and mute must all be enforced.");
			Check(audio.LargestDrumGroup == 3 && audio.LastDrumGain > soloDrumGain && audio.LastDrumGain <= 2, "Three nearby drummers must produce a louder, bounded group mix.");
			Check(audio.Played.GetValueOrDefault("bird") == daytimeBirds, "Night must replace day birds.");
			Check(patrol.Trait<MedievalPatrol>().Visits > 0, "Night must start a real audible patrol: " + patrol.Trait<MedievalPatrol>().Description);
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, decoded,
				paused, muted, soloDrumGain, audio.LargestDrumGroup, audio.LastDrumGain, patrolVisits = patrol.Trait<MedievalPatrol>().Visits,
				audio.PeakVoices, audio.Filtered, played = audio.Played }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL AUDIO: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
