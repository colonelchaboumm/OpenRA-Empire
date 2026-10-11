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
	public class MedievalSiegeValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeValidation();
	}
	public sealed class MedievalSiegeValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output, screenshot;
		int tick, stage, until, shotTick, hpBefore, stoneBefore, finalShots, carriedBefore;
		Actor workshop, ram, trebuchet, target, ramTarget, manualSoldier;
		WPos ramStart, deploymentPosition, soldierStart, cartStart;
		uint trebuchetId;
		bool finished, realBuild, transit, realSupply, movedRam;
		bool animationPaused, pauseScheduled, cancelAnimationChecked;
		int workshopShots;
		readonly Dictionary<string, HashSet<int>> workshopFrames = [];
		readonly HashSet<int> assemblyFrames = [], packingFrames = [];
		readonly HashSet<string> deploymentShots = [];
		int previousDeploymentState, previousDeploymentFrame;
		bool deploymentPauseScheduled, deploymentPaused;
		Actor[] trebuchetCrew;
		WPos unmannedPosition, carrierStart;
		int mountStart, mountDuration, crewDeliveries;
		bool physicalCarrier, loadingAnimation, crewWorking;
		Actor interruptedCarrier;
		int interruptTick, stoppedProgress;
		bool manualCarrierPreserved, crewLossPaused;
		int TotalAmmo() => trebuchet.Trait<MedievalStockpile>().Stock.Amount(3) + trebuchet.Trait<MedievalSiegeEngine>().Sling.Amount(3)
			+ trebuchet.Trait<MedievalSiegeEngine>().Crew.Sum(a => a.Trait<MedievalSiegeCrew>().Cargo);
		readonly List<string> errors = [];
		readonly List<string> checks = [];
		readonly int[] spent = new int[4];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World world, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "siege-preview"); Directory.CreateDirectory(output);
		}
		void Check(bool condition, string message) { if (!condition && !errors.Contains(message)) errors.Add(message); }
		void Next(int value, int wait = 0) { stage = value; until = tick + wait; }
		Actor Create(World w, string type, int x, int y, Player owner = null) => w.CreateActor(type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		Actor[] Carts(World w) => w.ActorsWithTrait<MedievalStoneCart>().Select(a => a.Actor).Where(a => SiegeRules.Live(a, w.LocalPlayer)).OrderBy(a => a.ActorID).ToArray();
		void Click(World w, Actor actor, string id)
		{
			Sync.RunUnsynced(w, () =>
			{
				if (actor != null) w.Selection.Combine(w, [actor], false, true);
				var button = Ui.Root.Get<ButtonWidget>(id); Check(!button.IsDisabled(), "Button unavailable: " + id);
				button.OnClick();
			});
		}
		void TargetClick(World w, CPos cell, int expectedOrders = 1) => Sync.RunUnsynced(w, () =>
		{
			if (w.OrderGenerator is not OrderGenerator generator) { Check(false, "Missing target generator."); return; }
			var orders = generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
			Check(orders.Length == expectedOrders, "Targeting must issue an order for every selected unit.");
			foreach (var order in orders) w.IssueOrder(order);
		});
		void Move(World w, Actor actor, CPos cell) => w.IssueOrder(new Order("Move", actor, Target.FromCell(w, cell), false));
		void Attack(World w, Actor actor, Actor victim) => w.IssueOrder(new Order("ForceAttack", actor, Target.FromActor(victim), false));
		void Shot(World w, Actor actor, string name)
		{
			Sync.RunUnsynced(w, () => { w.Selection.Combine(w, [actor], false, true); renderer.Viewport.Center(actor.CenterPosition); });
			screenshot = Path.Combine(output, name + ".png"); shotTick = tick + 6;
		}
		void Produce(World w, int product)
		{
			Click(w, workshop, "MD_SIEGE_PRODUCT_" + product);
			for (var r = 0; r < 4; r++) spent[r] += SiegeRules.Costs[product][r];
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			w.CreateActor("md.warehouse", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(18, 25)), new MedievalStockInit([400, 220, 0, 300])]);
			w.CreateActor("md.warehouse", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(12, 25)), new MedievalStockInit([200, 180, 0, 100])]);
			w.CreateActor("md.market", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(23, 34)), new MedievalStockInit([0, 180, 0, 0])]);
			for (var i = 0; i < 4; i++) { Create(w, "md.builder", 20+i, 30); Create(w, "md.swordsman", 26+i, 31); }
			var enemy = w.Players.First(p => p.InternalName == "Creeps");
			ramTarget = Create(w, "md.siegetarget", 38, 27, enemy);
			target = Create(w, "md.siegetarget", 45, 41, enemy);
		}
		void Finish(World w)
		{
			Check(physicalCarrier && loadingAnimation && crewWorking && crewDeliveries >= 2, "Soldiers must walk with real stones, visibly load them, and operate the trebuchet.");
			Check(mountDuration > 0 && mountDuration < SiegeRules.AssemblyTicks, "Four soldiers must assemble faster than one.");
			Check(manualCarrierPreserved && crewLossPaused, "Crew loss must pause work and a manually detached carrier must keep its stone.");
			Check(assemblyFrames.Count == 48 && packingFrames.Count == 48, "Assembly and packing must display all 48 poses in the engine.");
			Check(deploymentPaused, "A paused game must freeze assembly progress and the articulated pose together.");
			Check(animationPaused && cancelAnimationChecked, "Workshop animation must pause with the game and stop when the order is cancelled.");
			Check(workshopFrames.TryGetValue("ordered", out var waiting) && waiting.Count > 3, "A requested order must animate before fabrication begins.");
			for (var product = 0; product < 3; product++)
				for (var part = 0; part < 3; part++)
					Check(workshopFrames.TryGetValue($"work-{product}-{part}", out var poses) && poses.Count > 3, "Every product must show three animated assembly stages.");
			var totals = new int[4];
			foreach (var a in w.Actors.Where(a => SiegeRules.Live(a, w.LocalPlayer)))
			{
				if (a.TraitOrDefault<MedievalStockpile>() is MedievalStockpile stock) for (var r = 0; r < 4; r++) totals[r] += stock.Stock.Amount(r);
				if (a.TraitOrDefault<MedievalBuilding>() is MedievalBuilding building) for (var r = 0; r < 4; r++) totals[r] += building.Materials.Amount(r);
				if (a.TraitOrDefault<MedievalSiegeWorkshop>() is MedievalSiegeWorkshop forge) for (var r = 0; r < 4; r++) totals[r] += forge.Inputs.Amount(r);
				if (a.TraitOrDefault<MedievalWorker>() is MedievalWorker worker && worker.Cargo > 0) totals[worker.Resource] += worker.Cargo;
				if (a.TraitOrDefault<MedievalSiegeCrew>() is MedievalSiegeCrew crew) totals[3] += crew.Cargo;
				if (a.TraitOrDefault<MedievalSiegeEngine>() is MedievalSiegeEngine engine) totals[3] += engine.Sling.Amount(3);
			}
			var shots = SiegeRules.Live(trebuchet, w.LocalPlayer) ? trebuchet.Trait<MedievalSiegeEngine>().Shots : finalShots;
			var meals = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
			if (stage >= 15)
			{
				Check(totals[0] + spent[0] == 600, "All wood must be in stocks, construction, cargo or produced equipment.");
				Check(totals[3] + spent[3] + shots * SiegeRules.StonePerShot == 400, "Every stone must remain accounted for, including projectiles.");
				Check(totals[1] + spent[1] + meals == 580, "Food must pay for real drivers and meals without duplication.");
			}
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, errors, checks, realBuild, transit, realSupply, movedRam, shots, totals, spent, meals,
				animationPaused, cancelAnimationChecked, workshopFrames = workshopFrames.ToDictionary(p => p.Key, p => p.Value.Count),
				deploymentPaused, assemblyFrames = assemblyFrames.Count, packingFrames = packingFrames.Count,
				physicalCarrier, loadingAnimation, crewWorking, crewDeliveries, mountDuration, manualCarrierPreserved, crewLossPaused }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SIEGE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (w.LocalPlayer == null || finished) return; tick++;
			if (trebuchet != null && stage < 21 && !SiegeRules.Live(trebuchet, w.LocalPlayer))
			{ Check(false, "The trebuchet was lost before the scenario reached destruction testing."); Finish(w); return; }
			if (screenshot != null && tick >= shotTick) { Game.Renderer.SaveScreenshot(screenshot); screenshot = null; }
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 20)
			{
				Click(w, null, "MD_FORT_OPEN"); Click(w, null, "MD_SIEGE_BUILD"); TargetClick(w, Cell(25, 24)); Next(1);
			}
			realBuild |= w.ActorsWithTrait<MedievalWorker>().Any(p => p.Trait.Cargo > 0);
			if (SiegeRules.Live(trebuchet, w.LocalPlayer))
			{
				if (trebuchetCrew != null)
				{
					crewDeliveries = trebuchetCrew.Sum(a => a.Trait<MedievalSiegeCrew>().Deliveries);
					foreach (var soldier in trebuchetCrew.Where(a => SiegeRules.Live(a, w.LocalPlayer)))
					{
						var crew = soldier.Trait<MedievalSiegeCrew>(); var soldierBody = soldier.Trait<WithMedievalInfantryBody>();
						if (crew.Cargo > 0 && carrierStart == WPos.Zero) carrierStart = soldier.CenterPosition;
						physicalCarrier |= crew.Cargo > 0 && carrierStart != WPos.Zero && (soldier.CenterPosition - carrierStart).HorizontalLength > 600;
						loadingAnimation |= crew.Cargo > 0 && soldierBody.Sequence == "carry-run";
						crewWorking |= crew.Working && soldierBody.Sequence is "push" or "load";
						if (crew.Cargo > 0 && deploymentShots.Add("carrier")) Shot(w, soldier, "06-soldier-carrying-stone");
					}
				}
				var engine = trebuchet.Trait<MedievalSiegeEngine>();
				var body = trebuchet.Trait<WithMedievalSiegeBody>();
				if (engine.State is 1 or 3 && body.Sequence == "assemble")
				{
					(engine.State == 1 ? assemblyFrames : packingFrames).Add(body.Frame);
					if (previousDeploymentState == engine.State)
						Check(engine.State == 1 ? body.Frame >= previousDeploymentFrame : body.Frame <= previousDeploymentFrame, "Deployment poses must follow progress without backwards jumps.");
					var key = (engine.State == 1 ? "mount" : "pack") + "-" + body.Frame / 12;
					if (deploymentShots.Add(key)) Shot(w, trebuchet, "trebuchet-" + key);
					if (engine.State == 1 && engine.Progress > 100 && !deploymentPauseScheduled)
					{
						deploymentPauseScheduled = true; w.SetPauseState(true);
						Game.RunAfterDelay(120, () =>
						{
							var pose = body.Frame; var progress = engine.Progress;
							Game.RunAfterDelay(150, () => { deploymentPaused = pose == body.Frame && progress == engine.Progress; w.SetPauseState(false); });
						});
					}
				}
				previousDeploymentState = body.Sequence == "assemble" && engine.State is 1 or 3 ? engine.State : 0; previousDeploymentFrame = body.Frame;
			}
			if (workshop != null && MedievalStockpile.Active(workshop))
			{
				var motion = workshop.Trait<MedievalSiegeWorkshopAnimation>();
				var job = workshop.Trait<MedievalSiegeWorkshop>();
				if (motion.Sequence != null)
				{
					if (!workshopFrames.ContainsKey(motion.Sequence)) workshopFrames[motion.Sequence] = [];
					workshopFrames[motion.Sequence].Add(motion.Frame);
				}
				if (motion.Operating && !pauseScheduled)
				{
					pauseScheduled = true; w.SetPauseState(true);
					Game.RunAfterDelay(120, () =>
					{
						var frame = motion.Frame; var progress = job.Progress; var stock = job.Inputs.Hash;
						Game.RunAfterDelay(150, () =>
						{
							animationPaused = frame == motion.Frame && progress == job.Progress && stock == job.Inputs.Hash;
							w.SetPauseState(false);
						});
					});
				}
				if (motion.Operating && job.Product == 0 && workshopShots < 24 && tick % 5 == 0)
				{
					Sync.RunUnsynced(w, () => { w.Selection.Combine(w, [workshop], false, true); renderer.Viewport.Center(workshop.CenterPosition); });
					Game.Renderer.SaveScreenshot(Path.Combine(output, $"workshop-motion-{workshopShots++:00}.png"));
				}
			}
			if (stage == 1)
			{
				workshop = w.Actors.FirstOrDefault(a => a.Info.Name == "md.siegeworkshop");
				if (workshop != null && MedievalStockpile.Active(workshop)) { Check(realBuild, "Workshop must be supplied by real builders."); Produce(w, 0); Next(26, 80); }
			}
			if (stage == 26 && tick >= until) { Click(w, workshop, "MD_SIEGE_CANCEL"); Next(27, 24); }
			if (stage == 27 && tick >= until)
			{
				cancelAnimationChecked = !workshop.Trait<MedievalSiegeWorkshop>().Working && workshop.Trait<MedievalSiegeWorkshopAnimation>().Sequence == "idle";
				Click(w, workshop, "MD_SIEGE_PRODUCT_0"); Next(2);
			}
			if (stage == 2 && w.Actors.FirstOrDefault(a => a.Info.Name == "md.ram") is Actor builtRam)
			{
				ram = builtRam; ramStart = ram.CenterPosition;
				Check(ram.TraitOrDefault<MedievalRecruitable>() == null, "Ram must not create invisible soldiers or housing demand.");
				Move(w, ram, Cell(34, 27)); Produce(w, 1); Shot(w, workshop, "01-workshop"); Next(3, 120);
			}
			if (stage == 3 && tick >= until)
			{
				Check((ram.CenterPosition - ramStart).HorizontalLength < 100, "Unmanned ram must remain immobile.");
				Click(w, ram, "MD_ENGINE_ACTION"); Next(4);
			}
			if (stage == 4 && ram.Trait<MedievalSiegeEngine>().ReadyCrew == SiegeRules.RamCrew && w.Actors.FirstOrDefault(a => a.Info.Name == "md.trebuchet") is Actor builtTrebuchet)
			{
				trebuchet = builtTrebuchet; trebuchetId = trebuchet.ActorID;
				Check(trebuchet.Trait<MedievalSiegeEngine>().Packed && !trebuchet.Trait<MedievalSiegeEngine>().CanFire, "Trebuchet must leave the workshop packed and unable to fire.");
				Move(w, ram, Cell(34, 27)); Next(5);
			}
			if (stage == 5 && (ram.CenterPosition - ramStart).HorizontalLength > 3 * 1024)
			{
				movedRam = true; Attack(w, ram, ramTarget); Shot(w, ram, "02-ram-and-soldiers"); Next(6);
			}
			if (stage == 6 && ram.Trait<MedievalSiegeEngine>().Shots > 0 && ramTarget.Trait<Health>().HP < ramTarget.Trait<Health>().MaxHP)
			{
				Check(ramTarget.Trait<Health>().HP < ramTarget.Trait<Health>().MaxHP, "Ram must damage structures through its actual attack.");
				var soldier = ram.Trait<MedievalSiegeEngine>().Crew.First();
				soldier.Trait<Health>().InflictDamage(soldier, ramTarget, new Damage(100000), true);
				Check(!ram.Trait<MedievalSiegeEngine>().CanFire && !ram.Trait<MedievalSiegeEngine>().CanMove, "Loss of a pusher must immediately stop movement and attacks.");
				w.IssueOrder(new Order("Stop", ram, false));
				Click(w, ram, "MD_ENGINE_RELEASE");
				Produce(w, 2); unmannedPosition = trebuchet.CenterPosition; Move(w, trebuchet, Cell(33, 39)); checks.Add("Real soldiers push the ram; crew losses stop it."); Next(28, 100);
			}
			if (stage == 28 && tick >= until)
			{
				Check((trebuchet.CenterPosition - unmannedPosition).HorizontalLength < 100, "A trebuchet without soldiers cannot move.");
				w.AddFrameEndTask(world =>
				{
					trebuchetCrew = Enumerable.Range(0, 4).Select(i => Create(world, "md.swordsman", 26 + i, 30)).ToArray();
					Sync.RunUnsynced(world, () => world.Selection.Combine(world, trebuchetCrew, false, false));
					Click(world, null, "MD_CREW_ASSIGN"); TargetClick(world, trebuchet.Location, 4);
				});
				Next(7);
			}
			if (stage == 7 && trebuchet.Location == Cell(33, 39) && trebuchet.IsIdle && Carts(w).Length == 1)
			{
				Check(trebuchet.Trait<MedievalSiegeEngine>().Crew.Count() == 4, "Group assignment must attach all four selected soldiers.");
				Check(trebuchet.Trait<MedievalSiegeEngine>().ClearSite(), "Deployment site must be clear.");
				deploymentPosition = trebuchet.CenterPosition;
				Click(w, trebuchet, "MD_ENGINE_ACTION"); mountStart = tick; Produce(w, 2); Next(8, 30);
			}
			if (stage == 8 && tick >= until)
			{
				Check(trebuchet.Trait<MedievalSiegeEngine>().State == 1, "Assembly must take time.");
				Check(w.Actors.Count(a => a.Info.Name == "md.siegebrace") == 8, "Deployment must occupy a full three by three footprint.");
				Click(w, trebuchet, "MD_ENGINE_FREE_CREW"); Next(30, 10);
			}
			if (stage == 30 && tick >= until) { stoppedProgress = trebuchet.Trait<MedievalSiegeEngine>().Progress; Next(31, 35); }
			if (stage == 31 && tick >= until)
			{
				var engine = trebuchet.Trait<MedievalSiegeEngine>();
				crewLossPaused = engine.Progress == stoppedProgress && !engine.CanMove && !engine.CanFire;
				foreach (var soldier in trebuchetCrew) w.IssueOrder(new Order("MedievalAssignSiege", soldier, Target.FromActor(trebuchet), false));
				Move(w, trebuchet, Cell(34, 40)); Next(9);
			}
			if (stage == 9 && trebuchet.Trait<MedievalSiegeEngine>().Deployed)
			{
				mountDuration = tick - mountStart;
				Check(trebuchet.CenterPosition == deploymentPosition, "An assembling or deployed trebuchet cannot move.");
				Check(!trebuchet.Trait<Mobile>().CanEnterCell(trebuchet.Location + new CVec(1, 0), check: BlockedByActor.Immovable), "Braces must really block paths.");
				Attack(w, trebuchet, target); Next(10, 220);
			}
			if (stage == 10 && tick >= until && Carts(w).Length == 2)
			{
				Check(trebuchet.Trait<MedievalSiegeEngine>().Shots == 0, "No stone means no free shots.");
				foreach (var cart in Carts(w)) { Click(w, cart, "MD_STONE_ASSIGN"); TargetClick(w, trebuchet.Location); }
				Next(11);
			}
			if (stage == 11)
			{
				if (interruptedCarrier == null && trebuchetCrew.FirstOrDefault(a => a.Trait<MedievalSiegeCrew>().Cargo > 0) is Actor carrier)
				{
					interruptedCarrier = carrier; interruptTick = tick;
					Move(w, carrier, Cell(30, 42));
				}
				if (interruptedCarrier != null && !manualCarrierPreserved && tick >= interruptTick + 10)
				{
					Check(!interruptedCarrier.Trait<MedievalSiegeCrew>().Assigned && interruptedCarrier.Trait<MedievalSiegeCrew>().Cargo == 5, "A manual carrier order must release assignment without deleting or teleporting its stone.");
					manualCarrierPreserved = true;
					w.IssueOrder(new Order("MedievalAssignSiege", interruptedCarrier, Target.FromActor(trebuchet), false));
				}
				var stock = trebuchet.Trait<MedievalStockpile>().Stock;
				Check(stock.Amount(3) + stock.Incoming(3) <= 60, "Multiple supply carts must not overbook ammunition space.");
				transit |= Carts(w).Any(a => a.Trait<MedievalStockpile>().Stock.Amount(3) > 0 && !MedievalAccess.Adjacent(a, trebuchet));
				realSupply |= Carts(w).Any(a => a.Trait<MedievalStoneCart>().Delivered > 0);
				if (trebuchet.Trait<MedievalSiegeEngine>().Shots >= 2 && target.Trait<Health>().HP < target.Trait<Health>().MaxHP)
				{
					w.IssueOrder(new Order("Stop", trebuchet, false));
					foreach (var cart in Carts(w)) if (cart.Trait<MedievalStoneCart>().Active) Click(w, cart, "MD_STONE_PAUSE");
					Shot(w, trebuchet, "03-deployed-and-supplied"); Next(12, 30);
				}
			}
			if (stage == 12 && tick >= until)
			{
				Check(transit && realSupply, "Stone must travel on actual carts before the trebuchet can consume it.");
				trebuchet.Trait<Health>().InflictDamage(trebuchet, target, new Damage(1234), true);
				hpBefore = trebuchet.Trait<Health>().HP; stoneBefore = TotalAmmo();
				Click(w, trebuchet, "MD_ENGINE_ACTION"); Next(13);
			}
			if (stage == 13 && trebuchet.Trait<MedievalSiegeEngine>().Packed)
			{
				Check(trebuchet.ActorID == trebuchetId && trebuchet.Trait<Health>().HP == hpBefore && TotalAmmo() == stoneBefore, "Packing must preserve identity, health and ammunition.");
				Move(w, trebuchet, Cell(37, 38)); Next(14, 20);
			}
			if (stage == 14 && tick >= until && trebuchet.Location == Cell(37, 38) && trebuchet.IsIdle)
			{
				Check(!w.Actors.Any(a => a.Info.Name == "md.siegebrace"), "Packing must release all occupied ground.");
				Shot(w, trebuchet, "04-packed-transport"); Click(w, trebuchet, "MD_ENGINE_ACTION"); Next(15);
			}
			if (stage == 15 && trebuchet.Trait<MedievalSiegeEngine>().Deployed)
			{
				Check(trebuchet.Trait<Health>().HP == hpBefore, "Remounting must never repair the siege engine for free.");
				stoneBefore = trebuchet.Trait<MedievalSiegeEngine>().Shots;
				w.AddFrameEndTask(world => Attack(world, trebuchet, Create(world, "md.siegetarget", 39, 38, target.Owner)));
				Next(25, 200);
			}
			if (stage == 25 && tick >= until)
			{
				Check(trebuchet.Trait<MedievalSiegeEngine>().Shots == stoneBefore, "A target inside minimum range must not consume ammunition.");
				Attack(w, trebuchet, target); Next(16);
			}
			if (stage == 16 && TotalAmmo() < SiegeRules.StonePerShot)
			{
				stoneBefore = trebuchet.Trait<MedievalSiegeEngine>().Shots; Next(17, 350);
			}
			if (stage == 17 && tick >= until)
			{
				Check(trebuchet.Trait<MedievalSiegeEngine>().Shots == stoneBefore, "Firing must stop exactly when delivered ammunition runs out.");
				foreach (var cart in Carts(w)) if (cart.Trait<MedievalStockpile>().Stock.Amount(3) > 0) Click(w, cart, "MD_STONE_RECOVER");
				checks.Add("Assembly, packed transport and remounting preserve health and stones.");
				checks.Add("Multiple physical supply carts feed real stone-consuming projectiles.");
				Next(18);
			}
			if (stage == 18 && Carts(w).All(a => a.Trait<MedievalStockpile>().Stock.Amount(3) == 0 && !a.Trait<MedievalStoneCart>().Active))
			{
				Check(trebuchet.Trait<MedievalStockpile>().Stock.Incoming(3) == 0, "Suspending and recovery must release every destination reservation.");
				Shot(w, trebuchet, "05-ammunition-exhausted"); Next(19, 15);
			}
			if (stage == 19 && tick >= until)
			{
				var cart = Carts(w)[0];
				Click(w, cart, "MD_STONE_ASSIGN"); TargetClick(w, trebuchet.Location); Next(20);
			}
			if (stage == 20 && Carts(w)[0].Trait<MedievalStockpile>().Stock.Amount(3) > 0 && !MedievalAccess.Adjacent(Carts(w)[0], trebuchet))
			{
				var cart = Carts(w)[0];
				carriedBefore = cart.Trait<MedievalStockpile>().Stock.Amount(3);
				finalShots = trebuchet.Trait<MedievalSiegeEngine>().Shots;
				Check(trebuchet.Trait<MedievalStockpile>().Stock.Amount(3) == 0, "Destroyed target fixture must contain no unaccounted ammo.");
				trebuchet.Trait<Health>().InflictDamage(trebuchet, target, new Damage(1000000), true); Next(21, 40);
			}
			if (stage == 21 && tick >= until)
			{
				var cart = Carts(w)[0];
				Check(!cart.Trait<MedievalStoneCart>().Active && cart.Trait<MedievalStockpile>().Stock.Amount(3) == carriedBefore, "Losing the destination must preserve stones already in transit.");
				Check(!w.Actors.Any(a => a.Info.Name == "md.siegebrace"), "Destroying a deployed trebuchet must remove every brace.");
				Click(w, cart, "MD_STONE_RECOVER"); Next(22);
			}
			if (stage == 22 && Carts(w).All(a => a.Trait<MedievalStockpile>().Stock.Amount(3) == 0 && !a.Trait<MedievalStoneCart>().Active))
			{
				checks.Add("Minimum range, destroyed destination and recovery preserve ammunition and release the footprint.");
				ram.Trait<MedievalSiegeEngine>().CallCrew();
				manualSoldier = ram.Trait<MedievalSiegeEngine>().Crew.First(); soldierStart = manualSoldier.CenterPosition;
				cartStart = Carts(w)[0].CenterPosition;
				Move(w, manualSoldier, Cell(29, 33)); Move(w, Carts(w)[0], Cell(30, 42)); Next(23, 500);
			}
			if (stage == 23 && tick >= until)
			{
				Check(!manualSoldier.Trait<MedievalSiegeCrew>().Assigned && (manualSoldier.CenterPosition - soldierStart).HorizontalLength > 2048,
					"A manual order must detach the soldier and execute its movement.");
				Check(!Carts(w)[0].Trait<MedievalStoneCart>().Active && (Carts(w)[0].CenterPosition - cartStart).HorizontalLength > 2048,
					"A manual cart order must pause supply without cancelling its movement.");
				checks.Add("Manual movement releases soldiers and pauses carts while preserving the player's order.");
				Finish(w); return;
			}
			if (tick % 500 == 0) File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { tick, stage, errors,
				workshop = workshop?.Trait<MedievalSiegeWorkshop>().Status, supplies = workshop?.Trait<MedievalSiegeWorkshop>().Supplies,
				ram = ram?.Trait<MedievalSiegeEngine>().Status, trebuchet = SiegeRules.Live(trebuchet, w.LocalPlayer) ? trebuchet.Trait<MedievalSiegeEngine>().Status : "Absent",
				carts = Carts(w).Select(a => a.Trait<MedievalStoneCart>().Status),
				ramDebug = ram == null ? null : new { cell = ram.Location.ToString(), position = ram.CenterPosition.ToString(),
					target = ramTarget.CenterPosition.ToString(), activity = ram.CurrentActivity?.GetType().Name,
					ready = ram.Trait<MedievalSiegeEngine>().CanFire,
					paused = ram.Trait<Mobile>().IsTraitPaused, shots = ram.Trait<MedievalSiegeEngine>().Shots,
					armaments = ram.Trait<MedievalSiegeAttack>().Armaments.Count(),
					valid = ram.Trait<MedievalSiegeArmament>().Weapon.IsValidAgainst(Target.FromActor(ramTarget), w, ram),
					range = Target.FromActor(ramTarget).IsInRange(ram.CenterPosition, ram.Trait<MedievalSiegeArmament>().MaxRange()) } }));
			if (tick >= 18000) { Check(false, "Timed out at stage " + stage); Finish(w); }
		}
	}
}
