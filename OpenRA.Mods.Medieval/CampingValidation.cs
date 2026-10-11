using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalCampingValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCampingValidation();
	}
	// Opt-in native integration test: physical wool, production, carts, sleep, rest, UI and conservation.
	public sealed class MedievalCampingValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, maxFatigue, restBefore, tentsBeforePack, tentsMade, maxCloth;
		Actor market, depot, weaver, sheep, fold, shepherd, cart, house, soldier, tent, artisan, carrier, commander;
		readonly HashSet<int> sheepFrames = [], loomFrames = [];
		int stoppedArtisanAt;
		bool animationsPaused, loomStopped;
		readonly List<string> errors = [];
		bool done, woolCarried, tentCarried, slept, resumed, rested, deployed, packed, rain;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		Actor Spawn(World w, string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Order(Actor a, string name, uint data = 0) => a.World.IssueOrder(new Order(name, a, false) { ExtraData = data });
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () => { var b = Ui.Root.Get<ButtonWidget>("MD_CAMP_" + id); Check(!b.IsDisabled(), "Disabled camp button: " + id); b.OnClick(); });
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "camping"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			market = Spawn(w, "market", 22, 28); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			depot = Spawn(w, "warehouse", 22, 33); depot.Trait<MedievalStockpile>().Stock.Add(0, 100); depot.Trait<MedievalStockpile>().Stock.Add(1, 200);
			house = Spawn(w, "house", 26, 23);
			fold = Spawn(w, "sheepfold", 28, 35); sheep = Spawn(w, "sheeppasture.4x4", 33, 35);
			// Pens now start empty: this production scenario needs an actual adult flock.
			for (var n = 0; n < 2; n++) Check(sheep.Trait<MedievalLivestock>().Enter(Spawn(w, "sheep", 34 + n, 36)), "Fixture sheep must enter the wool-producing pen.");
			weaver = Spawn(w, "tentweaver", 28, 28); weaver.Trait<MedievalTentWorkshop>().Target = 1;
			weaver.Trait<MedievalTentWorkshop>().ClothTarget = 0;
			shepherd = Spawn(w, "shepherd", 31, 35); artisan = Spawn(w, "weaver", 27, 29);
			Spawn(w, "hauler", 26, 33); Spawn(w, "hauler", 26, 34);
			cart = Spawn(w, "supplycart", 30, 33);
			commander = Spawn(w, "commander", 29, 33);
			soldier = Spawn(w, "campguard", 32, 29); Spawn(w, "drummer", 30, 26); Spawn(w, "standardbearer", 32, 27);
			Spawn(w, "torch", 30, 31); Spawn(w, "torch", 26, 26);
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(29, 31))));
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			if (cart == null) return;
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(29, 31))));
			var flock = fold.Trait<MedievalBuildingAnimation>(); var loom = weaver.Trait<MedievalBuildingAnimation>();
			if (flock.Operating) sheepFrames.Add(fold.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.CurrentFrame);
			if (loom.Operating)
			{
				loomFrames.Add(weaver.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.CurrentFrame);
				if (stoppedArtisanAt == 0 && loomFrames.Count == 32)
				{
					Game.Renderer.SaveScreenshot(Path.Combine(output, "workshops-operating.png"));
					stoppedArtisanAt = tick; Order(artisan, "Stop");
				}
			}
			if (stoppedArtisanAt > 0 && tick == stoppedArtisanAt + 20)
			{
				loomStopped = !loom.Operating; Check(loomStopped, "The loom must stop when the assigned artisan is stopped."); Order(artisan, "MedievalWork");
			}
			foreach (var a in w.ActorsHavingTrait<MedievalWorker>())
			{
				var worker = a.Trait<MedievalWorker>();
				woolCarried |= worker.CampCargo && worker.Resource == 0 && worker.Cargo > 0;
				tentCarried |= worker.CampCargo && worker.Resource == 1 && worker.Cargo > 0;
			}
			maxFatigue = Math.Max(maxFatigue, soldier.Trait<MedievalRest>().Fatigue);
			maxCloth = Math.Max(maxCloth, MedievalCampStore.Amount(w.LocalPlayer, MedievalCampStore.Cloth));
			rain |= w.WorldActor.Trait<MedievalWeather>().RainPermille > 500;
			slept |= shepherd.Trait<MedievalRest>().Sleeping;
			resumed |= slept && tick > 6950 && !shepherd.Trait<MedievalRest>().Resting && shepherd.Trait<MedievalWorker>().Automatic;
			if (tick == 20) { Select(w, cart); Button(w, "OPEN"); Button(w, "LOAD"); }
			if (tick == 30) Game.Renderer.SaveScreenshot(Path.Combine(output, "menu.png"));
			if (tick == 40) { Check(cart.Trait<MedievalCampCart>().LoadTents, "Cart tent loading toggle did not reach simulation."); Button(w, "CLOSE"); }
			if (tick == 50) { Select(w, weaver); Button(w, "OPEN"); Button(w, "MORE"); }
			if (tick == 80) { Check(weaver.Trait<MedievalTentWorkshop>().Target == 2, "Tent target button failed."); Button(w, "LESS"); Button(w, "CLOSE"); }
			if (tick == 100) w.IssueOrder(new Order("Move", soldier, Target.FromCell(w, Cell(39, 25)), false));
			if (tick == 150)
			{
				Check(!loom.Operating, "The loom must wait for physical wool and wood deliveries.");
				w.SetPauseState(true);
				Game.RunAfterDelay(120, () =>
				{
					var frame = flock.Frame;
					Game.RunAfterDelay(120, () =>
					{
						animationsPaused = frame == flock.Frame; Check(animationsPaused, "Sheep animation must freeze during pause."); w.SetPauseState(false);
					});
				});
			}
			if (tick == 700) w.IssueOrder(new Order("Move", soldier, Target.FromCell(w, Cell(32, 29)), false));
			if (tick == 5600 && cart.Trait<MedievalCampStore>().Stock.Available(1) > 0)
			{
				tentsMade = MedievalCampStore.Amount(w.LocalPlayer, 1);
				Check(maxCloth == 4 && MedievalCampStore.Amount(w.LocalPlayer, MedievalCampStore.Cloth) == 0,
					"A tent must first weave and then consume four cloth even with a zero cloth target.");
				Check(depot.Trait<MedievalStockpile>().Stock.Amount(0) + weaver.Trait<MedievalTentWorkshop>().Inputs.Amount(0) == 96, "One tent must consume exactly four wood.");
				w.IssueOrder(new Order("MedievalCommanderDeploy", commander, Target.FromCell(w, Cell(32, 31)), false));
			}
			if (tick == 5800)
			{
				tent = w.ActorsHavingTrait<MedievalPackTent>().FirstOrDefault(); deployed = tent != null;
				Check(deployed, "The sheep-to-cart production chain must yield a deployable tent before night.");
				if (tent != null) { Check(cart.Trait<MedievalCampStore>().Stock.Amount(1) == 0, "Deployment must consume the carried tent."); Order(weaver, "MedievalTentTarget"); }
			}
			if (tick == 5850) w.IssueOrder(new Order("Move", commander, Target.FromCell(w, Cell(26, 25)), false));
			if (tick == 6150) w.AddFrameEndTask(_ =>
			{
				carrier = Spawn(w, "builder", 27, 25);
				carrier.Trait<MedievalWorker>().Automatic = false; carrier.Trait<MedievalWorker>().Cargo = 7;
				for (var i = 0; i < 5; i++) Spawn(w, "campguard", 34 + i % 3, 28 + i / 3);
			});
			if (tick == 6170)
			{
				restBefore = soldier.Trait<MedievalRest>().Fatigue; Order(commander, "MedievalCommanderRest");
			}
			if (tick > 6170 && soldier.Trait<MedievalRest>().Sleeping && soldier.Trait<MedievalRest>().Fatigue < restBefore) rested = true;
			if (tick == 6620)
			{
				File.WriteAllText(Path.Combine(output, "night-state.json"), JsonSerializer.Serialize(new { phase = w.WorldActor.Trait<MedievalDayNight>().Phase,
					actors = w.ActorsHavingTrait<MedievalRest>().Select(a => new { type = a.Info.Name, location = a.Location.ToString(), rest = a.Trait<MedievalRest>().Description,
						home = a.Trait<MedievalRest>().Shelter?.Info.Name, worker = a.TraitOrDefault<MedievalWorker>()?.Description,
						herding = a.TraitOrDefault<MedievalHerding>()?.Status }).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
				Check(!flock.Operating && !loom.Operating, "The sheepfold and sleeping artisan must be quiet at night.");
				Check(shepherd.Trait<MedievalRest>().Sleeping && artisan.Trait<MedievalRest>().Sleeping, "Citizens must enter their homes at night.");
				Check(house.Trait<MedievalShelter>().Occupants(house) <= 8, "Housing beds cannot be oversubscribed.");
				Check(MedievalSettlement.Population(w.LocalPlayer) == 15, "Sleeping must retain housing and food population.");
				Check(soldier.Trait<MedievalRest>().Sleeping, "Idle troops must shelter in a nearby tent at night.");
				Check(carrier.Trait<MedievalRest>().Sleeping && carrier.Trait<MedievalWorker>().Cargo == 7, "A sleeping worker must retain his physical cargo.");
				if (tent != null) Check(tent.Trait<MedievalShelter>().Occupants(tent) == 6, "An overcrowded camp must reserve exactly six beds.");
			}
			if (tick == 6650 && tent != null)
			{
				w.IssueOrder(new Order("Move", soldier, Target.FromCell(w, Cell(36, 30)), false));
				w.IssueOrder(new Order("Move", cart, Target.FromCell(w, Cell(31, 31)), false));
			}
			if (tick == 6700) Check(!soldier.Trait<MedievalRest>().Sleeping, "A movement order must wake a sleeping soldier at night.");
			if (tick == 6740) w.AddFrameEndTask(_ => house.Dispose());
			if (tick == 6750) Check(carrier.Trait<MedievalRest>().Shelter != house, "A destroyed home must release its sleeping inhabitants.");
			if (tick == 6800 && tent != null)
			{
				Check(tent.Trait<MedievalShelter>().Occupants(tent) > 0, "The packing scenario must exercise an occupied tent.");
				tentsBeforePack = MedievalCampStore.Amount(w.LocalPlayer, 1); Order(tent, "MedievalPackTent");
			}
			if (tick == 6850 && tent != null)
			{
				packed = !tent.IsInWorld; Check(packed, "An adjacent cart must pack a tent after evacuating it.");
				Check(MedievalCampStore.Amount(w.LocalPlayer, 1) == tentsBeforePack + 1, "Packing must return exactly one tent.");
			}
			if (tick == 6950) Order(carrier, "MedievalWork");
			if (tick == 7450) Check(carrier.Trait<MedievalWorker>().Cargo == 0, "A worker must deliver his retained cargo after waking.");
			if (tick is 180 or 5900 or 6520 or 7070) { Select(w, tick == 6520 ? house : weaver); Game.Renderer.SaveScreenshot(Path.Combine(output, $"view-{tick}.png")); }
			if (tick < 7500) return;
			Check(sheepFrames.Count == 32 && loomFrames.Count == 32, "Both new buildings must play all 32 articulated poses in the engine.");
			Check(loomStopped && animationsPaused && flock.Operating, "Workshop stop, pause and morning animation restart must all succeed.");
			Check(woolCarried && tentCarried, "Wool and tents must travel on real carriers."); Check(slept && resumed, "Citizens must resume work after dawn.");
			Check(rain && maxFatigue > 0 && rested, "Rain and marching must cause fatigue, and tent rest must recover it.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, woolCarried, tentCarried, slept, resumed, rested, deployed, packed, rain, maxFatigue, tentsMade,
				cartTents = cart.Trait<MedievalCampStore>().Stock.Amount(1), maxCloth, woolRemaining = sheep.Trait<MedievalSource>().Remaining,
				sheepFrames = sheepFrames.Count, loomFrames = loomFrames.Count, loomStopped, animationsPaused,
				artisan = artisan.Trait<MedievalWorker>().Description, shepherd = shepherd.Trait<MedievalWorker>().Description }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL CAMPING: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
