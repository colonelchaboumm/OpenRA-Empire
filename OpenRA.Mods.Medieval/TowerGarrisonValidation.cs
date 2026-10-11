using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalTowerGarrisonValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalTowerGarrisonValidation();
	}
	public sealed class MedievalTowerGarrisonValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, population, consumedBefore, consumedInTower;
		bool done, climbed, bonus, rangeHit, unloaded, populationKept, fed, sight, exitsRemovedSight, overflowRejected;
		Actor tower, site, hostile, archer, elite, extra, target;
		Health targetHealth;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool valid, string error) { if (!valid) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "tower-garrison", "native");
			Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
			tower = Spawn("woodtower", 29, 30);
			Spawn("market", 24, 32).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn("house", 24, 28);
			archer = Spawn("archer", 29, 36); elite = Spawn("elitearcher", 30, 36); extra = Spawn("archer", 31, 36);
			foreach (var a in new[] { archer, elite, extra }) a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire);
			site = w.CreateActor("md.woodtower", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(20, 25)), new MedievalConstructionInit()]);
			var enemy = w.Players.First(p => p.InternalName == "Creeps");
			w.LocalPlayer.EnemyPlayersMask = enemy.PlayerMask; enemy.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			hostile = w.CreateActor("md.woodtower", [new OwnerInit(enemy), new LocationInit(Cell(44, 40))]);
		}
		bool CanEnterTarget(Actor actor, Actor destination)
		{
			var cursor = "";
			return ((EnterAlliedActorTargeter<CargoInfo>)((IIssueOrder)actor.Trait<Passenger>()).Orders.Single()).CanTargetActor(actor, destination, TargetModifiers.None, ref cursor);
		}
		void Enter(World w, Actor actor) => w.IssueOrder(new Order("EnterTransport", actor, Target.FromActor(tower), false));
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick < 10) return;
			if (tick == 20)
			{
				Check(CanEnterTarget(archer, tower), "Right-click must offer entry into a completed friendly tower.");
				Check(!CanEnterTarget(archer, site), "An unfinished tower must reject entry.");
				Check(!CanEnterTarget(archer, hostile), "An enemy tower must reject entry.");
				population = MedievalSettlement.Population(w.LocalPlayer);
				consumedBefore = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
				Enter(w, archer); Enter(w, elite);
				Sync.RunUnsynced(w, () => { renderer.Viewport.Center(tower.CenterPosition); renderer.Viewport.AdjustZoom((float)Math.Log(1.5f / renderer.Viewport.Zoom)); });
			}
			if (!archer.IsDead && !archer.Disposed) climbed |= archer.Trait<MedievalTowerArcher>().Climbing && archer.CenterPosition.Z > tower.CenterPosition.Z;
			if (tick == 450)
			{
				Check(tower.Trait<Cargo>().PassengerCount == 2 && !archer.IsInWorld && !elite.IsInWorld, "Both archers must reach the tower and leave the ground actor map.");
				populationKept = MedievalSettlement.Population(w.LocalPlayer) == population && MedievalFoodEconomy.Citizens(w.LocalPlayer).Count() == population;
				bonus = archer.Trait<Armament>().MaxRange().Length == 7680 && elite.Trait<Armament>().MaxRange().Length == 9216;
				sight = tower.TraitsImplementing<RevealsShroud>().Any(r => !r.IsTraitDisabled && r.Info.Range.Length == 12288);
				Check(!archer.Trait<MedievalRest>().CampRest(archer), "Camp orders must not pull a posted archer out of a tower.");
				Enter(w, extra);
				w.AddFrameEndTask(world =>
				{
					target = world.CreateActor("md.towertarget", [new OwnerInit(hostile.Owner), new LocationInit(Cell(36, 25))]);
					target.Trait<Mobile>().SetCenterPosition(target, tower.CenterPosition + new WVec(8400, 0, 0));
					target.Trait<AutoTarget>().SetStance(target, UnitStance.HoldFire);
					targetHealth = target.Trait<Health>();
				});
				Sync.RunUnsynced(w, () => w.Selection.Combine(w, [tower], false, true));
			}
			if (tick == 900)
			{
				rangeHit = targetHealth.HP < targetHealth.MaxHP;
				overflowRejected = extra.IsInWorld && extra.Trait<Passenger>().Transport == null && tower.Trait<Cargo>().PassengerCount == 2;
				fed = !archer.Trait<MedievalNeedsFood>().Need.Hungry && !elite.Trait<MedievalNeedsFood>().Need.Hungry;
				consumedInTower = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed - consumedBefore;
				Game.Renderer.SaveScreenshot(Path.Combine(output, "garnison.png"));
				Check(Ui.Root.Get("MD_TOWER_PANEL").IsVisible(), "Selecting a tower must display its garrison panel.");
				target.Dispose();
				Sync.RunUnsynced(w, () => Ui.Root.Get<ButtonWidget>("MD_TOWER_UNLOAD").OnClick());
			}
			if (tick == 1200)
			{
				unloaded = archer.IsInWorld && elite.IsInWorld && tower.Trait<Cargo>().IsEmpty()
					&& archer.Trait<Armament>().MaxRange().Length == 5120 && elite.Trait<Armament>().MaxRange().Length == 6144
					&& archer.CenterPosition.Z == tower.CenterPosition.Z && elite.CenterPosition.Z == tower.CenterPosition.Z;
				exitsRemovedSight = tower.TraitsImplementing<RevealsShroud>().Where(r => !r.IsTraitDisabled).Max(r => r.Info.Range.Length) == 9216;
				Check(MedievalSettlement.Population(w.LocalPlayer) == population, "Unloading must not duplicate the population.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "descente.png"));
				Enter(w, archer); Enter(w, elite);
			}
			if (tick == 1550)
			{
				Check(tower.Trait<Cargo>().PassengerCount == 2, "Archers must be able to enter again after unloading.");
				elite.Kill(hostile);
			}
			if (tick == 1570)
			{
				Check(tower.Trait<Cargo>().PassengerCount == 1 && MedievalSettlement.Population(w.LocalPlayer) == population - 1, "A dead passenger must free its slot and housing.");
				tower.Kill(hostile);
			}
			if (tick < 1600) return;
			Check(archer.IsDead && MedievalSettlement.Population(w.LocalPlayer) == population - 2, "Tower destruction must remove its garrison without immortal off-map units.");
			Check(climbed, "Entry must show a climb."); Check(bonus, "Normal/elite range must increase from 5/6 to 7.5/9 cells.");
			Check(sight && exitsRemovedSight, "Extra vision must exist only while occupied.");
			Check(rangeHit, "An elite archer must automatically hit beyond the tower's and ground archer's normal range.");
			Check(populationKept && fed && consumedInTower >= population, "Posted archers must keep housing and eat in market range.");
			Check(unloaded, "The UI unload order must restore ground units and normal ranges."); Check(overflowRejected, "A full tower must reject a third archer.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick,
				climbed, bonus, sight, exitsRemovedSight, rangeHit, populationKept, fed, consumedInTower, unloaded, overflowRejected }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL TOWER GARRISON: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(800, Game.Exit);
		}
	}
}
