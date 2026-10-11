using System;
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
	public class MedievalSaveValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSaveValidation();
	}
	// Opt-in integration scenario: the real save server and real replay-based restoration.
	public sealed class MedievalSaveValidation : IWorldLoaded, ITick, INotifyGameSaved, INotifyGameLoaded
	{
		const string Filename = "medieval-audit.orasav";
		static string expected;
		static int expectedHash;
		string output;
		bool saved, restoring, done;
		int tick;
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "audit", "save");
			Directory.CreateDirectory(output);
			restoring = w.IsLoadingGameSave;
			Console.WriteLine("AUDIT SAVE: " + (restoring ? "restoring" : "initial game"));
		}
		void INotifyGameSaved.GameSaved(World w, bool isAutoSave) { saved = true; Console.WriteLine("AUDIT SAVE: disk write acknowledged"); }
		void INotifyGameLoaded.GameLoaded(World w)
		{
			// Loading a save opens the engine's paused options menu. Resume as a player would.
			Game.RunAfterDelay(1, () => { Ui.CloseWindow(); w.SetPauseState(false); });
		}
		static string Snapshot(World w) => JsonSerializer.Serialize(new {
			phase = w.WorldActor.Trait<MedievalDayNight>().Phase,
			stormShelterTicks = w.WorldActor.Trait<MedievalWeather>().StormShelterTicks,
			identity = MedievalHeraldry.For(w.LocalPlayer).CountryName,
			population = MedievalSettlement.Population(w.LocalPlayer),
			actors = w.Actors.OrderBy(a => a.ActorID).Where(a => a.Info.Name.StartsWith("md.")).Select(a => new {
				a.ActorID, a.Info.Name, owner = a.Owner.InternalName, position = a.CenterPosition.ToString(),
				health = a.TraitOrDefault<Health>()?.HP,
				stock = a.TraitOrDefault<MedievalStockpile>()?.Stock.Hash,
				materials = a.TraitOrDefault<MedievalBuilding>()?.Materials.Hash,
				source = a.TraitOrDefault<MedievalSource>()?.Remaining,
				equipment = a.TraitOrDefault<MedievalEquipmentStore>()?.Stock.Hash,
				camp = a.TraitOrDefault<MedievalCampStore>()?.Stock.Hash,
				cargo = a.TraitOrDefault<MedievalWorker>()?.Cargo,
				job = a.TraitOrDefault<MedievalWorker>()?.JobHash,
				fatigue = a.TraitOrDefault<MedievalRest>()?.Fatigue,
				sleeping = a.TraitOrDefault<MedievalRest>()?.Sleeping,
				stormSheltering = a.TraitOrDefault<MedievalRest>()?.StormSheltering,
				home = a.TraitOrDefault<MedievalRest>()?.HomeId
			}).ToArray()
		}, new JsonSerializerOptions { WriteIndented = true });
		void Finish(bool passed, string error, int hash)
		{
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, error = passed ? null : error, tick, expectedHash, actualHash = hash, restored = restoring }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SAVE: " + (passed ? "PASS" : error)); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			// Saved orders replay automatically. Issuing them again would duplicate the player's actions.
			if (!restoring && tick == 20)
			{
				var market = MedievalMarket.Markets(w.LocalPlayer).First();
				w.IssueOrder(new Order("MedievalSetEmpireIdentity", market, false) { TargetString = "Royaume de sauvegarde", ExtraData = 0x02458ABB });
				w.IssueOrder(new Order("MedievalRecruit", w.LocalPlayer.PlayerActor, false) { TargetString = "md.builder" });
			}
			if (!restoring && tick == 500) w.RequestGameSave(Filename, false);
			if (tick != 700) return;
			var snapshot = Snapshot(w); var hash = w.SyncHash();
			if (restoring)
			{
				File.WriteAllText(Path.Combine(output, "restored.json"), snapshot);
				Finish(!w.IsLoadingGameSave && expected == snapshot && expectedHash == hash, "Restored state or synchronized simulation differs from the uninterrupted game.", hash);
				return;
			}
			if (!saved) { Finish(false, "The server did not acknowledge the save.", hash); return; }
			expected = snapshot; expectedHash = hash;
			File.WriteAllText(Path.Combine(output, "uninterrupted.json"), snapshot);
			var map = w.Map.Uid; done = true;
			Game.RunAfterDelay(1, () => {
				Game.Disconnect(); Ui.ResetAll();
				Game.CreateAndStartLocalServer(map, [Order.FromTargetString("LoadGameSave", Filename, true), Order.Command($"state {Session.ClientState.Ready}")]);
			});
		}
	}
}
