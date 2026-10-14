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
	public class MedievalNationalPrideValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalNationalPrideValidation();
	}
	public sealed class MedievalNationalPrideValidation : IWorldLoaded, ITick, INotifyGameSaved, INotifyGameLoaded
	{
		const string SaveName = "pride-validation.orasav";
		static string expected;
		readonly List<string> errors = [];
		readonly List<Actor> soldiers = [];
		WorldRenderer renderer;
		Actor depot, market, victim;
		Player rival;
		string output;
		int tick, healthyArmy;
		bool done, restoring, saved;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; restoring = w.IsLoadingGameSave; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "pride"); Directory.CreateDirectory(output); }
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
			depot = Spawn(w, "warehouse", w.LocalPlayer, 23, 30); depot.Trait<MedievalStockpile>().Stock.Add(4, 150);
			market = Spawn(w, "market", w.LocalPlayer, 28, 28); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			for (var i = 0; i < 5; i++) soldiers.Add(Spawn(w, "swordsman", w.LocalPlayer, 28 + i, 32));
			victim = Spawn(w, "farmer", w.LocalPlayer, 33, 31);
			victim.Trait<Health>().InflictDamage(victim, victim, new Damage(victim.Trait<Health>().MaxHP * 99 / 100), true);
			Spawn(w, "market", rival, 48, 48).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "swordsman", rival, 48, 52);
			MedievalHeraldry.For(w.LocalPlayer).SetEmpire("Royaume des Hautes Terres", 0x356FAD, 0);
		}
		static string Snapshot(World w) => JsonSerializer.Serialize(new
		{
			pride = w.Players.Select(p => new { p.InternalName, hash = MedievalNationalPride.For(p)?.StateHash }),
			bodies = w.WorldActor.Trait<MedievalBattlefield>().RemainsHash,
			morale = w.ActorsWithTrait<MedievalMorale>().Select(p => new { p.Actor.ActorID, p.Actor.Owner.InternalName, p.Trait.Value, p.Trait.NationalBonus })
		});
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			var pride = MedievalNationalPride.For(w.LocalPlayer); var state = pride.State;
			if (!restoring) Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(31, 30))));
			if (tick == 50 && !restoring) Sync.RunUnsynced(w, () =>
			{
				renderer.Viewport.Center(w.Map.CenterOfCell(Cell(31, 30)));
				Ui.Root.Get<ButtonWidget>("MD_PRIDE_OPEN").OnClick();
				Check(Ui.Root.Get("MD_PRIDE_PANEL").IsVisible(), "HUD button opens pride details.");
				Check(Ui.Root.Get<LabelWidget>("MD_PRIDE_TITLE").GetText().Contains("Royaume des Hautes Terres"), "Details use the player's own country name.");
			});
			if (tick == 700)
			{
				Check(state.Target == 100 && state.Value >= 75, "Actual national reserves, fed army and welfare raise pride.");
				Check(soldiers[0].Trait<MedievalMorale>().NationalBonus > 0 && soldiers[0].Trait<MedievalMorale>().Value > 70, "Pride changes actual soldier morale.");
				Check(MedievalNationalPride.For(rival).Facts.Gold == 0 && pride.Facts.Gold == 150, "Reserves stay isolated by owner.");
				healthyArmy = pride.Facts.ArmyStrength;
			}
			if (tick == 750) depot.Trait<MedievalStockpile>().Stock.Take(4, int.MaxValue);
			if (tick == 825) Check(state.GoldPoints == 0, "Spending real gold updates national wealth.");
			if (tick == 850) { depot.Trait<MedievalStockpile>().Stock.Add(4, 150); market.Trait<MedievalStockpile>().Stock.Take(1, int.MaxValue); }
			if (tick == 1200) Check(state.FoodPoints == 0 && pride.Facts.Hungry > 0 && pride.Facts.ArmyStrength < healthyArmy, "Real supply shortages weaken welfare and army strength.");
			if (tick == 1675) Check(victim.IsDead && state.FamineMemory > 0 && state.Target <= 50 && state.Deaths == 1, "A real starvation death leaves grief and an owned corpse.");
			if (tick == 1700) market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			if (tick == 1900) Check(pride.Facts.Hungry == 0 && state.Target <= 50, "Feeding survivors ends hunger but does not erase recent starvation deaths.");
			if (tick == 2000 && !restoring) w.RequestGameSave(SaveName, false);
			if (tick == 2300) w.AddFrameEndTask(_ =>
			{
				soldiers[0].ChangeOwner(rival);
				soldiers[0].Trait<AutoTarget>().SetStance(soldiers[0], UnitStance.HoldFire);
			});
			if (tick == 2400) Check(soldiers[0].Trait<MedievalMorale>().NationalBonus == MedievalNationalPride.For(rival).State.MoraleBonus,
				"A captured soldier uses the new nation's pride, not its former owner.");
			// Keep the remote captured soldier supplied so the test isolates the remembered death.
			if (tick >= 2350 && tick % 100 == 0) soldiers[0].Trait<MedievalNeedsFood>().Need.Feed();
			if (tick == 3000) market.Trait<MedievalStockpile>().Stock.Add(1, 100);
			if (!restoring && tick is 700 or 1675 or 1900 or 4800) Game.Renderer.SaveScreenshot(Path.Combine(output, $"pride-{tick}.png"));
			if (tick != 4850) return;
			Check(state.FamineMemory == 0 && state.Value > 50 && state.Burials == 0, "Pride gradually recovers after grief, preserving outstanding burials.");
			Check(state.Deaths == 1 && state.FoodPoints == 20 && soldiers.All(a => !a.IsDead), "The surviving army remains fed and only the intended famine victim dies.");
			if (!restoring)
			{
				Check(saved, "Save during famine recovery.");
				if (errors.Count == 0)
				{
					expected = Snapshot(w); var map = w.Map.Uid; done = true;
					Game.RunAfterDelay(1, () => { Game.Disconnect(); Ui.ResetAll(); Game.CreateAndStartLocalServer(map,
						[Order.FromTargetString("LoadGameSave", SaveName, true), Order.Command($"state {Session.ClientState.Ready}")]); }); return;
				}
			}
			else Check(!w.IsLoadingGameSave && expected == Snapshot(w), "Save replay exactly restores pride, grief, deaths, ownership and morale.");
			if (!restoring) Sync.RunUnsynced(w, () => { Ui.Root.Get<ButtonWidget>("MD_PRIDE_CLOSE").OnClick(); Check(!Ui.Root.Get("MD_PRIDE_PANEL").IsVisible(), "Close returns to the game."); });
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, restored = restoring,
				state.Value, state.Target, state.Deaths, state.Burials }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL PRIDE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
