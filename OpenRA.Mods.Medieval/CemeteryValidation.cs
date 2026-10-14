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
	public class MedievalCemeteryValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCemeteryValidation();
	}
	public sealed class MedievalCemeteryValidation : IWorldLoaded, ITick, INotifyGameSaved, INotifyGameLoaded
	{
		const string SaveName = "cemetery-validation.orasav";
		static string expected;
		readonly List<string> errors = [];
		readonly List<Actor> victims = [];
		WorldRenderer renderer;
		Actor church, depot, hidden, stopped, killedCarrier, blocked;
		BattlefieldRemains interruptedBody, droppedBody;
		string output;
		int tick, stoppedAt, killedAt, food, saveAt;
		bool done, restoring, saved, sawPickup, sawCarrying, sawBurial;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; restoring = w.IsLoadingGameSave; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "cemetery"); Directory.CreateDirectory(output); }
		void INotifyGameSaved.GameSaved(World w, bool autoSave) { saved = true; }
		void INotifyGameLoaded.GameLoaded(World w) => Game.RunAfterDelay(1, () => { Ui.CloseWindow(); w.SetPauseState(false); });
		Actor Spawn(World w, string type, int x, int y, bool enemy = false)
		{
			var a = w.CreateActor("md." + type, [new OwnerInit(enemy ? w.Players.First(p => p.InternalName == "Rival") : w.LocalPlayer), new LocationInit(Cell(x, y))]);
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); return a;
		}
		void Kill(Actor a) => a.Trait<Health>().InflictDamage(a, a, new Damage(1000000), true);
		void Click(World w, string id)
		{
			if (restoring) return;
			Sync.RunUnsynced(w, () =>
			{
				w.Selection.Combine(w, [church], false, true);
				var b = Ui.Root.Get<ButtonWidget>(id); Check(b.IsVisible() && !b.IsDisabled(), "Button available: " + id); b.OnClick();
			});
		}
		void Zone(World w, int x, int y)
		{
			if (restoring) return;
			Click(w, "MD_CHURCH_CEMETERY");
			Sync.RunUnsynced(w, () =>
			{
				var generator = w.OrderGenerator as MedievalCemeteryOrderGenerator;
				Check(generator != null, "Church opens the cemetery tool.");
				if (generator == null) return;
				foreach (var cell in new[] { Cell(x, y), Cell(x + 1, y + 1) })
					foreach (var order in generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, MouseButton.Left, int2.Zero, int2.Zero, Modifiers.None, 1))) w.IssueOrder(order);
			});
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			church = Spawn(w, "church", 28, 27); depot = Spawn(w, "warehouse", 23, 30);
			depot.Trait<MedievalStockpile>().Stock.Add(1, 200);
			for (var x = 0; x < 4; x++) Spawn(w, "house", 22 + x * 3, 23);
			for (var i = 0; i < 6; i++) victims.Add(Spawn(w, i % 2 == 0 ? "farmer" : "militia", 34 + i, 36));
			hidden = Spawn(w, "farmer", 8, 8, true);
		}
		static string Snapshot(World w) => JsonSerializer.Serialize(new
		{
			cemetery = w.WorldActor.Trait<MedievalCemeteries>().PlotsHash,
			bodies = w.WorldActor.Trait<MedievalBattlefield>().RemainsHash,
			workers = w.ActorsWithTrait<MedievalUndertaker>().Select(p => new { p.Actor.ActorID, p.Trait.BodyId, p.Trait.Burials, p.Trait.Carrying, p.Trait.Progress, cell = p.Actor.Location.ToString() })
		});
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			var cemetery = w.WorldActor.Trait<MedievalCemeteries>(); var scene = w.WorldActor.Trait<MedievalBattlefield>();
			var workers = w.ActorsWithTrait<MedievalUndertaker>().Where(p => MedievalStockpile.Active(p.Actor)).ToArray();
			if (tick % 250 == 0) Console.WriteLine($"CEMETERY {tick}: graves={cemetery.Plots.Count(p => p.Buried)} " + string.Join("; ", workers.Select(p => $"{p.Actor.ActorID} at {p.Actor.Location} body={p.Trait.BodyId} carry={p.Trait.Carrying} work={p.Trait.Progress} plot={p.Trait.GraveCell}")));
			Sync.RunUnsynced(w, () =>
			{
				renderer.Viewport.Center(w.Map.CenterOfCell(Cell(32, 30)));
				renderer.Viewport.AdjustZoom((float)Math.Log(1.6f / renderer.Viewport.Zoom));
			});
			if (tick == 10) { food = MedievalSettlement.Available(w.LocalPlayer, 1); Click(w, "MD_CHURCH_UNDERTAKER"); }
			if (tick == 25)
			{
				Check(MedievalSettlement.Available(w.LocalPlayer, 1) == food - 30, "Recruitment costs exactly 30 food.");
				Check(MedievalSettlement.PendingPopulation(w.LocalPlayer) == 1 && !church.Trait<MedievalChurch>().CanRecruit(church), "Training reserves housing and shares the priest queue.");
			}
			if (tick == 200) Check(workers.Length == 1 && MedievalSettlement.PendingPopulation(w.LocalPlayer) == 0, "The church recruits one actual undertaker.");
			if (tick == 210) { foreach (var v in victims) Kill(v); Kill(hidden); }
			if (tick == 245) w.LocalPlayer.Shroud.Disabled = false;
			if (tick == 280)
			{
				Check(scene.Remains.Count(r => !r.Building) == 7 && workers.All(p => !p.Trait.Busy), "Without a cemetery, bodies must remain on the battlefield.");
				Check(!w.LocalPlayer.Shroud.IsVisible(hidden.CenterPosition), "The distant body is hidden by real fog.");
				scene.Remove(scene.Remains.Single(r => r.Seed == (int)hidden.ActorID));
				w.LocalPlayer.Shroud.Disabled = true;
				Check(cemetery.PlacementError(church, AgriculturalZone.Between(Cell(28, 27), Cell(29, 28))) != null, "No zoning on a building.");
				Check(cemetery.PlacementError(church, AgriculturalZone.Between(Cell(60, 50), Cell(61, 51))) != null, "Cemetery must be near a church.");
				Check(cemetery.PlacementError(church, AgriculturalZone.Between(Cell(34, 29), Cell(34, 29))) != null, "Reject undersized zones.");
			}
			if (tick == 300) Zone(w, 34, 29);
			if (tick == 320)
			{
				Check(cemetery.Plots.Count() == 4, "Two clicks create four burial places.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.house", Cell(34, 29)), "Buildings cannot overwrite a cemetery.");
				Check(cemetery.PlacementError(church, AgriculturalZone.Between(Cell(34, 29), Cell(35, 30))) != null, "Zones cannot overlap.");
			}
			if (tick == 340) Click(w, "MD_CHURCH_UNDERTAKER");
			foreach (var p in workers)
			{
				sawPickup |= p.Trait.Working && !p.Trait.Carrying; sawCarrying |= p.Trait.Carrying; sawBurial |= p.Trait.Working && p.Trait.Carrying;
				if (p.Trait.Busy)
				{
					Check(p.Trait.Body.ClaimedBy == p.Actor.ActorID && p.Trait.Plot.ReservedBy == p.Actor.ActorID, "Every job exclusively reserves its corpse and grave.");
					Check(scene.Remains.Count(r => r.Carrier == p.Actor.ActorID) == (p.Trait.Carrying ? 1 : 0), "A worker carries at most one real body.");
				}
			}
			if (tick == 1400)
			{
				Check(workers.Length == 2 && cemetery.Plots.Count(p => p.Buried) == 4, "Two undertakers fill the four places.");
				Check(scene.Remains.Count(r => !r.Building) == 2 && workers.All(p => !p.Trait.Busy), "A full cemetery leaves surplus corpses untouched.");
			}
			if (tick == 1450) Zone(w, 34, 32);
			if (tick > 1450 && tick < 1800 && stoppedAt == 0 && workers.Any(p => p.Trait.Carrying))
			{
				var p = workers.First(p => p.Trait.Carrying); stopped = p.Actor; interruptedBody = p.Trait.Body; stoppedAt = tick;
				if (!restoring) w.IssueOrder(new Order("Stop", stopped, false));
			}
			if (stoppedAt > 0 && tick == stoppedAt + 15)
				Check(!stopped.Trait<MedievalUndertaker>().Busy && interruptedBody.Carrier != stopped.ActorID && scene.Remains.Contains(interruptedBody), "Stop returns the carried corpse to the ground.");
			if (stoppedAt > 0 && tick == stoppedAt + 50 && !restoring) w.IssueOrder(new Order("MedievalUndertakerWork", stopped, Target.FromActor(church), false));
			if (tick == 1800) w.AddFrameEndTask(_ => Kill(Spawn(w, "farmer", 39, 36)));
			if (tick > 1800 && tick < 2200 && killedAt == 0 && workers.Any(p => p.Trait.Carrying))
			{
				var p = workers.First(p => p.Trait.Carrying); killedCarrier = p.Actor; droppedBody = p.Trait.Body; killedAt = tick; Kill(p.Actor);
			}
			if (killedAt > 0 && tick == killedAt + 2)
				Check(droppedBody.Carrier == 0 && droppedBody.ClaimedBy == 0 && scene.Remains.Contains(droppedBody)
					&& scene.Remains.Any(r => r.Seed == (int)killedCarrier.ActorID), "Death drops the passenger and leaves the undertaker's own body.");
			if (tick == 2300) Zone(w, 36, 29);
			if (tick == 2400) Kill(church);
			if (tick == 2420) w.AddFrameEndTask(_ => Kill(Spawn(w, "farmer", 38, 34)));
			if (tick == 2500) Check(workers.All(p => !p.Trait.Busy), "Destruction of the last church releases all jobs.");
			if (tick == 2600) w.AddFrameEndTask(_ => church = Spawn(w, "church", 28, 27));
			if (tick == 2700) w.AddFrameEndTask(_ =>
			{
				Spawn(w, "warehouse", 44, 37); blocked = Spawn(w, "farmer", 45, 38); Kill(blocked);
				hidden = Spawn(w, "farmer", 8, 8, true); Kill(hidden); w.LocalPlayer.Shroud.Disabled = false;
			});
			if (tick == 2950)
			{
				var unseen = scene.Remains.SingleOrDefault(r => r.Seed == (int)hidden.ActorID);
				Check(unseen != null && unseen.ClaimedBy == 0 && !w.LocalPlayer.Shroud.IsVisible(unseen.Position), "Workers with available graves must not discover hidden enemy bodies.");
				if (unseen != null) scene.Remove(unseen); w.LocalPlayer.Shroud.Disabled = true;
			}
			if (tick == 3000) w.AddFrameEndTask(_ => Kill(Spawn(w, "farmer", 40, 35)));
			if (tick > 3000 && saveAt == 0 && workers.Any(p => p.Trait.Carrying))
			{
				saveAt = tick;
				if (!restoring) w.RequestGameSave(SaveName, false);
			}
			if (!restoring && tick is 220 or 540 or 1100 or 2250 or 3550) Game.Renderer.SaveScreenshot(Path.Combine(output, $"cemetery-{tick}.png"));
			if (tick != 3800) return;
			Check(stoppedAt > 0 && killedAt > 0 && sawPickup && sawCarrying && sawBurial, "All stages and both interruptions must be exercised.");
			Check(cemetery.Plots.Count(p => p.Buried) == 10, "Every accessible body produces exactly one gravestone.");
			Check(cemetery.Plots.Where(p => p.Buried).Select(p => p.BodyId).Distinct().Count() == 10, "No corpse can be buried twice.");
			Check(scene.Remains.Count(r => !r.Building) == 1 && scene.Remains.Single(r => !r.Building).Seed == (int)blocked.ActorID, "An inaccessible body stays on the ground without preventing other jobs.");
			Check(cemetery.Plots.All(p => p.ReservedBy == 0), "Finished and cancelled jobs release every reservation.");
			if (!restoring)
			{
				Check(saved && saveAt > 3000, "Save the actual game while carrying a body.");
				if (errors.Count == 0)
				{
					expected = Snapshot(w); var map = w.Map.Uid; done = true;
					Game.RunAfterDelay(1, () => { Game.Disconnect(); Ui.ResetAll(); Game.CreateAndStartLocalServer(map,
						[Order.FromTargetString("LoadGameSave", SaveName, true), Order.Command($"state {Session.ClientState.Ready}")]); }); return;
				}
			}
			else Check(!w.IsLoadingGameSave && expected == Snapshot(w), "Save replay restores zones, graves, bodies and the interrupted transport exactly.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, restored = restoring,
				graves = cemetery.Plots.Count(p => p.Buried), bodies = scene.Remains.Count(r => !r.Building), stoppedAt, killedAt, saveAt }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL CEMETERY: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
