using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalFireSupportValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFireSupportValidation();
	}
	// Opt-in scenario. Real menus, builder deliveries, siege crews and projectiles.
	public sealed class MedievalFireSupportValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, stage, until;
		bool done, cargo, animated, litArrow, normalArrow, moved, captured, pauseScheduled, paused;
		Actor workshop, fire, site, depot, cauldron, militia, saboteur, archer, target, ordinaryTarget;
		Player foe;
		WPos start;
		readonly List<string> errors = [], checks = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string name) { if (ok) checks.Add(name); else if (!errors.Contains(name)) errors.Add(name); }
		Actor Spawn(World w, string name, int x, int y, Player owner = null)
		{
			var a = w.CreateActor("md." + name, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); return a;
		}
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "fire-support", "native"); Directory.CreateDirectory(output); }
		void Click(World w, Actor actor, string id) => Sync.RunUnsynced(w, () =>
		{
			if (actor != null) w.Selection.Combine(w, [actor], false, true);
			var b = Ui.Root.Get<ButtonWidget>(id); Check(b.IsVisible() && !b.IsDisabled(), "Available: " + id); b.OnClick();
		});
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			foe = w.Players.First(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = foe.PlayerMask; foe.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			workshop = Spawn(w, "siegeworkshop", 25, 21);
			depot = Spawn(w, "warehouse", 22, 25); depot.Trait<MedievalStockpile>().Stock.Add(0, 200); depot.Trait<MedievalStockpile>().Stock.Add(3, 150);
			var market = Spawn(w, "market", 20, 29); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			for (var i = 0; i < 3; i++) { Spawn(w, "house", 18, 21 + i * 3); Spawn(w, "builder", 24 + i, 28); }
			fire = Spawn(w, "campfire", 30, 30);
			site = w.CreateActor("md.campfire", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(28, 26)), new MedievalConstructionInit()]);
			militia = Spawn(w, "militia", 32, 30); saboteur = Spawn(w, "saboteur", 39, 30, foe);
			saboteur.Trait<Mobile>().SetPosition(saboteur, w.Map.CellContaining(militia.CenterPosition + new WVec(0, 5100, 0)));
			Spawn(w, "swordsman", 29, 24); Spawn(w, "swordsman", 29, 25);
			Spawn(w, "sheeppasture.4x3", 32, 26); Spawn(w, "palisade", 29, 32); Spawn(w, "tree", 28, 32);
			archer = Spawn(w, "archer", 32, 35); target = Spawn(w, "firetarget", 36, 35, foe); ordinaryTarget = Spawn(w, "firetarget", 36, 38, foe);
		}
		void Move(World w, Actor a, CPos cell) => w.IssueOrder(new Order("Move", a, Target.FromCell(w, cell), false));
		void Next(int value, int wait) { stage = value; until = tick + wait; }
		void Shot(World w, string name)
		{
			Sync.RunUnsynced(w, () => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png")));
		}
		void WinterNight(World w)
		{
			var season = w.WorldActor.Trait<MedievalSeasons>().Clock;
			while (season.Season != VillageSeason.Winter || season.Elapsed < 1000) season.Advance();
			var day = w.WorldActor.Trait<MedievalDayNight>().Clock; while (day.Phase != 12500) day.Advance();
		}
		void Finish(World w)
		{
			Check(stage == 8, "Full native scenario completed");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, errors, checks,
				cargo, animated, litArrow, normalArrow, moved, lights = w.WorldActor.Trait<MedievalFireEnvironment>().VisibleLights,
				heatSamples = w.WorldActor.Trait<MedievalFireEnvironment>().Heat.Count }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL FIRE SUPPORT: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(700, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(28, 30))));
			cargo |= w.ActorsWithTrait<MedievalWorker>().Any(p => p.Trait.Cargo > 0);
			animated |= workshop.Trait<MedievalSiegeWorkshopAnimation>().Operating;
			litArrow |= w.Effects.OfType<MedievalFireArrow>().Any(a => a.Lit);
			if (stage == 6) normalArrow |= w.Effects.OfType<MedievalFireArrow>().Any(a => !a.Lit);
			if (stage == 5 && tick == until - 150) Check(FireSupportRules.Supported(archer, true), "Archer receives flames near friendly cauldron");
			if (stage == 4 && tick == until - 190) Shot(w, "02-cauldron-crew");
			if (stage == 6 && !captured && cauldron.Owner == foe)
			{
				captured = true; until = tick + 150;
				w.IssueOrder(new Order("ForceAttack", archer, Target.FromActor(ordinaryTarget), false));
			}
			if (stage == 7 && !pauseScheduled && tick >= until - 300)
			{
				pauseScheduled = true; w.SetPauseState(true);
				Game.RunAfterDelay(120, () =>
				{
					var smoke = fire.Trait<MedievalFireSource>().Smoke.Clouds[0]; var age = smoke.Age;
					var heat = w.WorldActor.Trait<MedievalFireEnvironment>().Heat.SnowRemaining(fire.CenterPosition);
					var time = w.WorldTick;
					Game.RunAfterDelay(150, () =>
					{
						paused = w.WorldTick == time && smoke.Age == age && heat == w.WorldActor.Trait<MedievalFireEnvironment>().Heat.SnowRemaining(fire.CenterPosition);
						w.SetPauseState(false);
					});
				});
			}
			if (tick == 8) Click(w, workshop, "MD_SIEGE_PRODUCT_3");
			// Construction tabs belong to a selected builder in the contextual sidebar.
			if (tick == 10) Sync.RunUnsynced(w, () => w.Selection.Combine(w, [w.Actors.First(a => a.Info.Name == "md.builder")], false, true));
			if (tick == 12) Click(w, null, "MD_MENU_TAB_6");
			if (tick == 15) Sync.RunUnsynced(w, () => w.Selection.Combine(w, [workshop], false, true));
			if (tick == 35)
			{
				Check(militia.Trait<MedievalFireWatch>().Supported, "Militia supported by campfire");
				Check(militia.Trait<DetectCloaked>().Range.Length == 6144, "Detection range increased to six cells");
				Check(saboteur.Trait<Cloak>().Cloaked && saboteur.Trait<Cloak>().IsVisible(saboteur, w.LocalPlayer), "Cloaked enemy exposed beyond normal four-cell radius");
				Shot(w, "01-defense-workshop");
			}
			if (stage == 0)
			{
				cauldron = w.Actors.FirstOrDefault(a => a.Info.Name == "md.cauldron");
				if (cauldron != null && site.Trait<MedievalBuilding>().Complete)
				{
					Check(cargo && animated, "Builders deliver actual materials and animate fabrication");
					Check(depot.Trait<MedievalStockpile>().Stock.Amount(0) == 115 && depot.Trait<MedievalStockpile>().Stock.Amount(3) == 100, "Exact wood and stone cost for cauldron and campfire");
					Check(cauldron.TraitOrDefault<MedievalNeedsFood>() == null && cauldron.Trait<MedievalSiegeEngine>().CrewLimit == 2, "Chassis uses two real soldiers without a phantom citizen");
					start = cauldron.CenterPosition; Move(w, cauldron, cauldron.Location + new CVec(3, 0)); Next(1, 35);
				}
			}
			else if (stage == 1 && tick >= until)
			{
				Check(cauldron.CenterPosition == start, "Unmanned cauldron cannot move"); cauldron.Trait<MedievalSiegeEngine>().CallCrew(true); Next(2, 150);
			}
			else if (stage == 2 && tick >= until)
			{
				Check(cauldron.Trait<MedievalSiegeEngine>().ReadyCrew == 1 && !cauldron.Trait<MedievalSiegeEngine>().CanMove && cauldron.CenterPosition == start, "One soldier cannot push the cauldron");
				// Cancel the earlier blocked move before checking arrival at a stationary chassis.
				w.IssueOrder(new Order("Stop", cauldron, false));
				cauldron.Trait<MedievalSiegeEngine>().CallCrew(true); Next(3, 250);
			}
			else if (stage == 3 && tick >= until)
			{
				Check(cauldron.Trait<MedievalSiegeEngine>().ReadyCrew == 2, "Both soldiers physically reach their cauldron");
				Sync.RunUnsynced(w, () => w.Selection.Combine(w, [cauldron], false, true));
				Move(w, cauldron, Cell(30, 25)); Next(4, 250);
			}
			else if (stage == 4 && tick >= until)
			{
				moved = (cauldron.CenterPosition - start).HorizontalLength > 1024; Check(moved, "Two soldiers push the moving cauldron");
				foreach (var a in cauldron.Trait<MedievalSiegeEngine>().Crew.ToArray()) a.Trait<MedievalSiegeCrew>().Release(a);
				cauldron.CancelActivity(); cauldron.Trait<Mobile>().SetPosition(cauldron, Cell(31, 35));
				w.IssueOrder(new Order("ForceAttack", archer, Target.FromActor(target), false)); Next(5, 160);
			}
			else if (stage == 5 && tick >= until)
			{
				Check(litArrow && target.Trait<MedievalBurnable>().Burning, "Real flaming arrow ignites the enemy structure"); Shot(w, "02-flaming-arrows");
				archer.CancelActivity();
				w.AddFrameEndTask(_ =>
				{
					var captor = Spawn(w, "swordsman", 30, 35, foe);
					w.IssueOrder(new Order("CaptureActor", captor, Target.FromActor(cauldron), false));
				});
				Next(6, 600);
			}
			else if (stage == 6 && tick >= until)
			{
				Check(captured, "Enemy soldier captures the cauldron through the real capture order");
				Check(!FireSupportRules.Supported(archer, true) && normalArrow && !ordinaryTarget.Trait<MedievalBurnable>().Burning, "Captured cauldron cannot supply enemy archers; ordinary arrows stay ordinary");
				Check(!cauldron.Trait<MedievalSiegeEngine>().CanMove, "Captured cauldron waits for its new crew");
				archer.CancelActivity(); WinterNight(w); Next(7, 400);
				w.AddFrameEndTask(_ => cauldron.ChangeOwner(w.LocalPlayer));
			}
			else if (stage == 7 && tick >= until)
			{
				Check(paused, "Pause freezes flames, smoke and progressive snowmelt");
				var env = w.WorldActor.Trait<MedievalFireEnvironment>();
				Check(env.Heat.SnowRemaining(fire.CenterPosition) < .08f && env.Heat.SnowRemaining(fire.CenterPosition + new WVec(-6500, 0, 0)) > .9f, "Progressive local snowmelt leaves distant snow intact");
				Check(env.VisibleLights >= 3 && fire.Trait<MedievalFireSource>().Smoke.Clouds.Count >= 20, "Visible light sources and dense bounded smoke");
				Sync.RunUnsynced(w, () => Check(env.ShadowAt(militia, renderer).Opacity > .05f, "Nearby militia casts a local fire shadow"));
				Shot(w, "03-winter-firelight");
				w.AddFrameEndTask(_ => fire.Dispose()); Next(8, 30);
			}
			else if (stage == 8 && tick >= until)
			{
				Check(!militia.Trait<MedievalFireWatch>().Supported && militia.Trait<DetectCloaked>().Range.Length == 4096, "Destroyed campfire removes detection bonus"); Finish(w);
			}
			if (tick > 7500) { errors.Add("Native scenario timed out at stage " + stage); Finish(w); }
		}
	}
}
