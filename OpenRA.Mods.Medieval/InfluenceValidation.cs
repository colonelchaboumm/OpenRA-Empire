using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalInfluenceValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalInfluenceValidation();
	}
	public sealed class MedievalInfluenceValidation : IWorldLoaded, ITick, INotifyGameSaved, INotifyGameLoaded
	{
		const string SaveName = "influence-validation.orasav";
		static string expected;
		readonly List<string> errors = [];
		readonly List<Actor> soldiers = [];
		WorldRenderer renderer;
		Actor capital, secondMarket, colony, depot, enemyCitizen, enemySoldier, remote, ownCitizen, ally, neutral;
		Player rival;
		string output;
		int tick, initialArea, largeRadius;
		bool done, restoring, saved;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; restoring = w.IsLoadingGameSave; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "influence"); Directory.CreateDirectory(output); }
		void INotifyGameSaved.GameSaved(World w, bool autoSave) { saved = true; }
		void INotifyGameLoaded.GameLoaded(World w) => Game.RunAfterDelay(1, () => { Ui.CloseWindow(); w.SetPauseState(false); });
		Actor Spawn(World w, string type, Player owner, int x, int y)
		{
			var a = w.CreateActor("md." + type, [new OwnerInit(owner), new LocationInit(Cell(x, y))]);
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); return a;
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			rival = w.Players.First(p => p.InternalName == "Rival");
			var friend = w.Players.First(p => p.InternalName == "Third");
			w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask; rival.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			friend.EnemyPlayersMask = default; friend.AlliedPlayersMask = w.LocalPlayer.PlayerMask;
			w.LocalPlayer.AlliedPlayersMask = friend.PlayerMask;
			capital = Spawn(w, "market", w.LocalPlayer, 18, 28); capital.Trait<MedievalStockpile>().Stock.Add(1, 200);
			secondMarket = Spawn(w, "market", w.LocalPlayer, 22, 27);
			colony = Spawn(w, "market", w.LocalPlayer, 8, 10);
			depot = Spawn(w, "warehouse", w.LocalPlayer, 13, 30); depot.Trait<MedievalStockpile>().Stock.Add(4, 150);
			Spawn(w, "market", rival, 42, 28);
			for (var i = 0; i < 5; i++) soldiers.Add(Spawn(w, "swordsman", w.LocalPlayer, 18 + i, 32));
			enemyCitizen = Spawn(w, "farmer", rival, 28, 30); enemySoldier = Spawn(w, "militia", rival, 29, 29);
			remote = Spawn(w, "farmer", rival, 58, 52); ownCitizen = Spawn(w, "farmer", w.LocalPlayer, 27, 30);
			ally = Spawn(w, "farmer", friend, 28, 29); neutral = Spawn(w, "farmer", w.Players.First(p => p.InternalName == "Neutral"), 28, 31);
			MedievalHeraldry.For(w.LocalPlayer).SetEmpire("Royaume d'Azur", 0x399FE7, 0);
			MedievalHeraldry.For(rival).SetEmpire("Marche d'Ambre", 0xE39B48, 1);
		}
		static string Snapshot(World w) => JsonSerializer.Serialize(new
		{
			influence = w.WorldActor.Trait<MedievalInfluence>().SourcesHash,
			claims = w.WorldActor.Trait<MedievalInfluence>().Field.Claims.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X)
				.Select(p => new { cell = p.Key.ToString(), p.Value.Owner.InternalName, p.Value.Power }),
			moods = w.ActorsWithTrait<MedievalInfluenceMood>().Select(p => new { p.Actor.ActorID, p.Trait.Penalty, p.Trait.Target, p.Trait.NationId }),
			pride = w.Players.Select(p => MedievalNationalPride.For(p)?.StateHash)
		});
		static int Penalty(Actor actor) => actor.Trait<MedievalInfluenceMood>().Penalty;
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			var influence = w.WorldActor.Trait<MedievalInfluence>();
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 28))));
			if (tick % 500 == 0) Console.WriteLine($"INFLUENCE {tick}: radius={influence.Radius(w.LocalPlayer)} area={influence.Area(w.LocalPlayer)} citizen={Penalty(enemyCitizen)} soldier={Penalty(enemySoldier)} sources={influence.Sources.Count()}");
			if (tick == 100)
			{
				initialArea = influence.Area(w.LocalPlayer);
				Check(influence.Sources.Count(s => s.Owner == w.LocalPlayer) == 2, "Nearby markets share one city source; a distant colony creates another.");
				Check(!influence.Visible(renderer), "Frontiers stay hidden at normal zoom.");
			}
			if (tick == 150) Sync.RunUnsynced(w, () => renderer.Viewport.AdjustZoom(-10));
			if (tick == 225 && !restoring)
			{
				Check(influence.Visible(renderer) && influence.PaintedCells > 0 && influence.BorderSegments > 0, "Maximum camera zoom-out draws colored territory and borders.");
				Check(Ui.Root.Get<LabelWidget>("MD_INFLUENCE_LEGEND").IsVisible(), "Strategic view labels the influence overlay.");
			}
			if (tick == 1800)
			{
				Check(influence.Area(w.LocalPlayer) > initialArea * 3, "Prosperity gradually expands the actual territory.");
				Check(Penalty(enemyCitizen) is > 0 and <= 6 && Penalty(enemySoldier) is > 0 and <= 6, "A happier neighboring nation demoralizes enemy civilians and troops.");
				Check(enemyCitizen.Trait<MedievalNeedsFood>().WorkPercent < 100 && enemySoldier.Trait<MedievalInfluenceMood>().WorkPercent == 100, "Civilian work slows slightly; soldiers receive only their morale penalty.");
				Check(enemySoldier.Trait<MedievalMorale>().InfluencePenalty > 0 && enemySoldier.Trait<MedievalMorale>().Value < 70, "Foreign influence affects actual combat morale.");
				Check(Penalty(remote) == 0 && Penalty(ownCitizen) == 0 && Penalty(ally) == 0 && Penalty(neutral) == 0, "Remote, own, allied and neutral citizens are unaffected.");
			}
			if (tick == 1900) enemyCitizen.Trait<Mobile>().SetPosition(enemyCitizen, Cell(57, 52));
			if (tick == 2150)
			{
				Check(Penalty(enemyCitizen) == 0 && enemyCitizen.Trait<MedievalNeedsFood>().WorkPercent == 100, "Leaving the frontier restores civilian work gradually.");
				enemyCitizen.Trait<Mobile>().SetPosition(enemyCitizen, Cell(28, 30));
			}
			if (tick == 2600 && !restoring) w.RequestGameSave(SaveName, false);
			if (tick == 4000)
			{
				Check(influence.Field.At(Cell(35, 29)).Owner == w.LocalPlayer, "The rich nation's frontier reaches the rival's approaches.");
				if (!restoring) Sync.RunUnsynced(w, () => Ui.Root.Get<ButtonWidget>("MD_PRIDE_OPEN").OnClick());
			}
			if (tick == 4100) w.AddFrameEndTask(_ => colony.ChangeOwner(rival));
			if (tick == 4175) Check(influence.Sources.Any(s => s.Actor == colony && s.Owner == rival && s.Radius.Value < 6 * 1024), "Capturing a city starts the new owner's influence small.");
			if (tick == 4250)
			{
				largeRadius = influence.Radius(w.LocalPlayer);
				capital.Trait<MedievalStockpile>().Stock.Take(1, int.MaxValue); depot.Trait<MedievalStockpile>().Stock.Take(4, int.MaxValue);
				foreach (var soldier in soldiers) soldier.Trait<Health>().InflictDamage(soldier, soldier, new Damage(5000), true);
			}
			if (tick == 4800) Check(influence.Radius(w.LocalPlayer) < largeRadius && influence.Health(w.LocalPlayer) < 100, "Loss of wealth and health makes influence recede gradually.");
			if (tick == 4850) w.AddFrameEndTask(_ => capital.Dispose());
			if (tick == 4925) Check(influence.Sources.Any(s => s.Actor == secondMarket && s.Owner == w.LocalPlayer && s.Radius.Value > 15 * 1024), "A surviving market preserves its city's accumulated influence.");
			if (tick == 5000) w.AddFrameEndTask(_ => secondMarket.Dispose());
			if (tick == 5250) Check(!influence.Sources.Any(s => s.Owner == w.LocalPlayer) && Penalty(enemyCitizen) == 0, "Losing the last cities removes their territorial pressure.");
			if (!restoring && tick is 250 or 1800 or 3950 or 4050 or 4800) Game.Renderer.SaveScreenshot(Path.Combine(output, $"influence-{tick}.png"));
			if (tick != 5300) return;
			if (!restoring)
			{
				Check(saved, "Save while national borders are expanding and enemies are demoralized.");
				if (errors.Count == 0)
				{
					expected = Snapshot(w); var map = w.Map.Uid; done = true;
					Game.RunAfterDelay(1, () => { Game.Disconnect(); Ui.ResetAll(); Game.CreateAndStartLocalServer(map,
						[Order.FromTargetString("LoadGameSave", SaveName, true), Order.Command($"state {Session.ClientState.Ready}")]); }); return;
				}
			}
			else Check(!w.IsLoadingGameSave && expected == Snapshot(w), "Replay restores territories, city capture, health, pride and mood exactly.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, restored = restoring,
				initialArea, largeRadius, sources = influence.Sources.Count() }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL INFLUENCE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
