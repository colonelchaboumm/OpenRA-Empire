using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;
using OpenRA.Widgets;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Mods.Medieval
{
	// Enabled only by the dedicated medieval-test manifest, never by the playable mod.
	public class MedievalValidationInfo : TraitInfo
	{
		public readonly bool VisualOnly = false;
		public readonly bool AmbienceOnly = false;
		public readonly bool AnimationOnly = false;
		public override object Create(ActorInitializer init) => new MedievalValidation(this);
	}
	public class MedievalValidation : ITick, IWorldLoaded
	{
		readonly bool visualOnly;
		readonly bool ambienceOnly;
		readonly bool animationOnly;
		int animationStage;
		int animationCapture;
		Actor animationActor;
		public MedievalValidation(MedievalValidationInfo info) { visualOnly = info.VisualOnly; ambienceOnly = info.AmbienceOnly; animationOnly = info.AnimationOnly; }
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		int tick;
		readonly bool[] hauled = new bool[4];
		readonly bool[] produced = new bool[4];
		readonly bool[] growing = new bool[4];
		bool builderCarried;
		bool cancelled;
		Actor cancelledSite, cancelledPile;
		MedievalBuilding cancelledBuilding;
		bool cancellationRefundChecked;
		int[] cancelledMaterials;
		readonly int[] recoveredMaterials = new int[Inventory.ResourceCount];
		readonly HashSet<(uint ActorId, int StartTick)> cancellationLoads = [];
		bool prematureCompletion;
		bool finished;
		int heldMeat;
		readonly List<Actor> combatTargets = [];
		readonly List<Actor> combatAttackers = [];
		readonly List<string> recruitmentChecks = [];
		readonly List<string> foodChecks = [];
		readonly List<string> cartChecks = [];
		readonly List<string> agricultureChecks = [];
		readonly HashSet<string> animationChecks = [];
		readonly HashSet<int> walkingFrames = [];
		readonly HashSet<int> workingFrames = [];
		readonly HashSet<int> cropFrames = [];
		readonly HashSet<int> cattleFrames = [];
		readonly List<Actor> agricultureFixtures = [];
		Actor fixtureField;
		Actor fixturePasture;
		Actor fixtureFarm;
		Actor fixtureRanch;
		Actor stoppedFarmer;
		Actor waitingFarmer;
		MedievalZoneOrderGenerator zonePlacement;
		Actor remoteSite;
		bool remoteBuilderCarried;
		int remoteSuppliedAt, remoteStartedAt, remoteCompletedAt, remoteStormTicks;
		int[] remoteMaterialsBefore, remoteMaterialsAfter;
		Actor famineWorker;
		Actor famineSoldier;
		Actor rescuedCitizen;
		Actor rescueWarehouse;
		Actor rescueMarket;
		Actor supplyCart;
		Actor expeditionSoldier;
		bool marketReceivedFood;
		Actor unfinishedFoodWarehouse;
		int rescuedHealth;
		readonly HashSet<uint> newSites = [];
		readonly List<string> errors = [];
		string output;
		WorldRenderer renderer;
		void IWorldLoaded.WorldLoaded(World world, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", animationOnly ? "animation-preview" : ambienceOnly ? "ambience-preview" : visualOnly ? "isometric-preview" : "integration");
			Directory.CreateDirectory(output);
			wr.Viewport.Center(world.Map.CenterOfCell(Cell(30, 32)));
		}
		void Place(World w, string type, CPos cell) => w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor,
			Target.FromCell(w, cell), false) { TargetString = type });
		void Check(bool ok, string error) { if (!ok) errors.Add(error); }
		IEnumerable<Order> ZoneClick(World w, MedievalZoneOrderGenerator generator, CPos cell) => generator.Order(w, cell, int2.Zero,
			new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1));
		void CheckAgriculture(World w)
		{
			if (tick == 50) w.AddFrameEndTask(world =>
			{
				var owner = world.Players.First(p => p.InternalName == "Neutral");
				Actor Spawn(string type, CPos at)
				{
					var actor = world.CreateActor(type, [new OwnerInit(owner), new LocationInit(at)]);
					agricultureFixtures.Add(actor); return actor;
				}
				fixtureFarm = Spawn("md.farm", Cell(5, 5));
				fixtureRanch = Spawn("md.ranch", Cell(14, 5));
				Spawn("md.market", Cell(9, 5)).Trait<MedievalStockpile>().Stock.Add(1, 200);
				fixtureField = Spawn("md.field.4x4", Cell(4, 10));
				fixturePasture = Spawn("md.pasture.4x4", Cell(13, 10));
				for (var i = 0; i < 4; i++) fixturePasture.Trait<MedievalLivestock>().Enter(Spawn("md.cow", Cell(14 + i % 2, 11 + i / 2)));
				for (var i = 0; i < 6; i++) Spawn("md.farmer", Cell(5 + i, 8));
				for (var i = 0; i < 4; i++) Spawn("md.breeder", Cell(12 + i, 8));
			});
			if (tick > 100 && tick < 2300)
			{
				Check(fixtureField.Trait<MedievalAgriculturalArea>().Workers(fixtureField) <= 4, "Field exceeds its four farmer places.");
				Check(fixturePasture.Trait<MedievalAgriculturalArea>().Workers(fixturePasture) <= 2, "Pasture exceeds its two breeder places.");
			}
			if (tick == 400)
			{
				Check(fixtureField.Trait<MedievalAgriculturalArea>().Workers(fixtureField) == 4 && fixturePasture.Trait<MedievalAgriculturalArea>().Workers(fixturePasture) == 2,
					"Available zone places should be filled and excess workers should wait.");
				Check(fixturePasture.Trait<MedievalAgriculturalArea>().CattleCapacity == 4, "A 4x4 enclosure should house four cows.");
				var footprint = fixturePasture.Info.TraitInfo<BuildingInfo>().Footprint;
				Check(footprint[new CVec(0, 0)] == FootprintCellType.Occupied && footprint[new CVec(2, 3)] == FootprintCellType.OccupiedPassable,
					"Enclosure must have blocking fence edges and a passable southern gate.");
				agricultureChecks.Add("4x4 zones enforce four farmers, two breeders and four cows with a passable gate");
			}
			if (tick == 1800) Sync.RunUnsynced(w, () =>
			{
				Ui.Root.Get<ButtonWidget>("MD_BUILD_5").OnClick();
				zonePlacement = w.OrderGenerator as MedievalZoneOrderGenerator;
				Check(zonePlacement != null, "Culture button must start rectangle placement.");
				if (zonePlacement != null)
				{
					Check(!ZoneClick(w, zonePlacement, Cell(21, 38)).Any(), "First corner must not create a zone.");
					var orders = ZoneClick(w, zonePlacement, Cell(18, 35)).ToArray();
					Check(orders.Length == 1, "Opposite corner should create exactly one rectangle order.");
					foreach (var order in orders) w.IssueOrder(order);
				}
				Ui.Root.Get<ButtonWidget>("MD_BUILD_7").OnClick();
				zonePlacement = w.OrderGenerator as MedievalZoneOrderGenerator;
				if (zonePlacement != null)
				{
					ZoneClick(w, zonePlacement, Cell(40, 35)).ToArray();
					Viewport.LastMousePos = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(w.Map.CenterOfCell(Cell(43, 38))));
				}
			});
			if (tick == 1820) Sync.RunUnsynced(w, () =>
			{
				Game.Renderer.SaveScreenshot(Path.Combine(output, "agriculture-zone-preview.png"));
				if (zonePlacement != null)
					foreach (var order in ZoneClick(w, zonePlacement, Cell(43, 38))) w.IssueOrder(order);
			});
			if (tick == 1900)
			{
				Check(w.Actors.Any(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.field.4x4" && a.Location == Cell(18, 35)), "Reverse-drawn culture must have correct type and top-left corner.");
				Check(w.Actors.Any(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.pasture.4x4" && a.Location == Cell(40, 35)), "Enclosure rectangle must create a construction site.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.field.4x4", Cell(18, 35)), "Overlapping farm zones must be refused.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.field.4x4", Cell(3, 55)), "A culture too far from a working farm must be refused.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.field.10x10", Cell(59, 59)), "Out-of-map rectangles must be refused.");
				agricultureChecks.Add("Two-corner placement works in both directions and rejects overlap, distant farms and map boundaries");
			}
			if (tick == 2000)
			{
				Check(fixtureFarm.Trait<MedievalStockpile>().Stock.Amount(1) > 0 && fixtureRanch.Trait<MedievalStockpile>().Stock.Amount(2) > 0,
					"Allocated farmers and breeders must physically deliver their zone production.");
				stoppedFarmer = agricultureFixtures.First(a => a.Info.Name == "md.farmer" && a.Trait<MedievalWorker>().WorkArea == fixtureField);
				waitingFarmer = agricultureFixtures.First(a => a.Info.Name == "md.farmer" && a.Trait<MedievalWorker>().WorkArea == null);
				w.IssueOrder(new Order("Stop", stoppedFarmer, false));
			}
			if (tick == 2150)
			{
				Check(stoppedFarmer.Trait<MedievalWorker>().WorkArea == null && waitingFarmer.Trait<MedievalWorker>().WorkArea == fixtureField,
					"Stopping a farmer must free a place for a waiting worker.");
				agricultureChecks.Add("Workers deliver food/meat; freed zone places are reused automatically");
				Check(!stoppedFarmer.Trait<MedievalWorker>().IsWorking
					&& stoppedFarmer.Trait<WithMedievalInfantryBody>().Sequence is not ("work" or "harvest"),
					"A manually stopped farmer must stop its working animation.");
			}
			if (tick == 2300) w.AddFrameEndTask(_ => fixtureField.Dispose());
			if (tick == 2400)
				Check(agricultureFixtures.Where(a => a.Info.Name == "md.farmer").All(a => a.Trait<MedievalWorker>().WorkArea == null), "Removed zone must release every assigned farmer.");
			if (tick == 3500) w.AddFrameEndTask(_ => { foreach (var a in agricultureFixtures.Where(a => a.IsInWorld).ToArray()) a.Dispose(); });
			if (tick == 7000)
			{
				var field = w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.field.4x4");
				var pasture = w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.pasture.4x4");
				Check(field != null && pasture != null && MedievalStockpile.Active(field) && MedievalStockpile.Active(pasture), "Drawn farm zones must be completed by builders after delivery and work.");
				if (pasture != null) w.Selection.Combine(w, [pasture], false, true);
				agricultureChecks.Add("Builders complete both drawn zones with scaled wood/stone costs");
			}
			if (tick == 7020) Game.Renderer.SaveScreenshot(Path.Combine(output, "agriculture-zones-tested.png"));
			if (tick == 7050) w.Selection.Clear();
		}
		void CheckRecruitment(World w, int food, int meat, int population, string name)
		{
			var eaten = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
			var physicalFood = w.Actors.Where(a => a.Owner == w.LocalPlayer && a.IsInWorld && !a.IsDead)
				.Sum(a => (a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(1) ?? 0)
					+ (a.TraitOrDefault<MedievalWorker>() is MedievalWorker worker && !worker.EquipmentCargo && worker.Resource == 1 ? worker.Cargo : 0));
			Check(physicalFood + eaten == food && MedievalSettlement.Available(w.LocalPlayer, 2) == meat
				&& MedievalSettlement.Population(w.LocalPlayer) == population, name + ": wrong payment or population.");
			recruitmentChecks.Add(name);
		}
		void CheckCancellation(World w)
		{
			if (cancelledSite == null || cancelledSite.IsInWorld) return;
			if (!cancellationRefundChecked)
			{
				cancellationRefundChecked = true;
				// Read the retained construction ledger after the order resolves: a builder
				// may have delivered another load during the order's normal latency.
				cancelledMaterials = Enumerable.Range(0, Inventory.ResourceCount).Select(cancelledBuilding.Materials.Amount).ToArray();
				var piles = w.ActorsHavingTrait<MedievalSalvage>().Where(a => a.Owner == w.LocalPlayer && a.Location == Cell(22, 31)).ToArray();
				Check(piles.Length == 1 && cancelledMaterials.Any(n => n > 0), "Cancellation must create exactly one nonempty salvage pile.");
				cancelledPile = piles.FirstOrDefault();
				if (cancelledPile != null)
					Check(Enumerable.Range(0, Inventory.ResourceCount).Select(cancelledPile.Trait<MedievalStockpile>().Stock.Amount).SequenceEqual(cancelledMaterials),
						"Cancellation must return exactly the materials actually delivered to the site.");
			}
			if (cancelledPile == null) return;
			foreach (var pair in w.ActorsWithTrait<MedievalWorker>())
			{
				var worker = pair.Trait;
				if (worker.WorkTarget != cancelledPile || !worker.HandlingStock || !worker.HandlingPickup || !worker.HandlingCommitted) continue;
				if (cancellationLoads.Add((pair.Actor.ActorID, tick - worker.HandlingElapsed)))
					recoveredMaterials[worker.Resource] += worker.HandlingAmount;
			}
		}
		void TrackRemoteConstruction(World w)
		{
			if (remoteSite == null) return;
			var building = remoteSite.Trait<MedievalBuilding>();
			if (building.Supplied && remoteSuppliedAt == 0) remoteSuppliedAt = tick;
			if (building.Progress > 0 && remoteStartedAt == 0) remoteStartedAt = tick;
			if (building.Complete && remoteCompletedAt == 0) remoteCompletedAt = tick;
			if (!building.Complete && w.ActorsWithTrait<MedievalWorker>().Any(p => p.Actor.Owner == w.LocalPlayer && p.Trait.Info.Profession == "builder"
				&& p.Actor.TraitOrDefault<MedievalRest>()?.StormSheltering == true)) remoteStormTicks++;
		}
		void CheckRemoteConstruction(World w)
		{
			var building = remoteSite?.Trait<MedievalBuilding>();
			Check(remoteCompletedAt > 0 && building?.Complete == true,
				$"Builders must finish the remote house using the parked cart (work {building?.Progress}/{building?.Info.Work}, storm pause {remoteStormTicks} ticks).");
			var stock = supplyCart?.Trait<MedievalStockpile>().Stock;
			var delivered = remoteBuilderCarried && stock != null && building?.Materials.Amount(0) == 60 && building.Materials.Amount(3) == 20
				&& stock.Reserved(0) == 0 && stock.Reserved(3) == 0 && stock.Incoming(0) == 0 && stock.Incoming(3) == 0;
			Check(delivered, "Remote house must consume exactly 60 wood and 20 stone from the cart through builder trips.");
			remoteMaterialsAfter = ConstructionMaterials(w);
			Check(remoteMaterialsBefore != null && remoteMaterialsAfter.SequenceEqual(remoteMaterialsBefore),
				"Remote construction must conserve wood and stone across depots, the cart, carried loads and incorporated materials, including interrupted trips.");
			if (delivered && building?.Complete == true) cartChecks.Add("Builders finish the remote house after actual delivery of 60 wood and 20 stone from the cart");
			Game.Renderer.SaveScreenshot(Path.Combine(output, "remote-construction-tested.png"));
		}
		static int[] ConstructionMaterials(World w) => new[] { 0, 3 }.Select(r => w.Actors.Where(a => a.Owner == w.LocalPlayer && a.IsInWorld && !a.IsDead)
			.Sum(a => (a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(r) ?? 0) + (a.TraitOrDefault<MedievalBuilding>()?.Materials.Amount(r) ?? 0)
				+ (a.TraitOrDefault<MedievalWorker>() is { EquipmentCargo: false, CampCargo: false } worker && worker.Resource == r ? worker.Cargo : 0))).ToArray();
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (w.LocalPlayer == null || finished) return;
			if (animationOnly) { AnimationTick(w); return; }
			if (ambienceOnly) { AmbienceTick(w); return; }
			if (visualOnly) { VisualTick(w); return; }
			// Keep automated screenshots stable if the desktop pointer sits on a viewport edge.
			renderer.Viewport.Center(w.Map.CenterOfCell(tick >= 9000 ? Cell(48, 21) : tick >= 5300 && tick <= 5500 ? Cell(49, 15)
				: tick >= 4000 && tick < 4150 ? Cell(50, 50) : Cell(30, 32)));
			tick++;
			CheckAgriculture(w);
			CheckAnimations(w);
			CheckCancellation(w);
			TrackRemoteConstruction(w);
			if (tick == 1) Game.Renderer.SaveScreenshot(Path.Combine(output, "paths-start.png"));
			if (tick == 1500) Game.Renderer.SaveScreenshot(Path.Combine(output, "paths-forming.png"));
			if (tick == 7300) Game.Renderer.SaveScreenshot(Path.Combine(output, "paths-developed.png"));
			if (tick == 10)
			{
				Check(MedievalFoodEconomy.Demand(w.LocalPlayer) == 16, "Eight initial villagers must need 16 food per minute.");
				foreach (var type in MedievalSettlement.Recruits.Take(6))
				{
					var cost = MedievalSettlement.Cost(w.LocalPlayer, type);
					Check(cost.Food == 10 && cost.Meat == 0, "Villager costs must only use food: " + type);
				}
				foreach (var type in MedievalSettlement.Buildings)
				{
					var cost = w.Map.Rules.Actors[type].TraitInfo<MedievalBuildingInfo>().Cost;
					Check(cost[1] == 0 && cost[2] == 0, "Construction must only use wood/stone: " + type);
				}
				var stock = MedievalSettlement.Warehouses(w.LocalPlayer).First().Trait<MedievalStockpile>().Stock;
				heldMeat = stock.Take(2, stock.Available(2));
				w.IssueOrder(new Order("MedievalRecruit", w.LocalPlayer.PlayerActor, false) { TargetString = "md.manatarms" });
			}
			if (tick == 150)
			{
				CheckRecruitment(w, 140, 0, 8, "Military recruitment refused without barracks, no food lost");
				Check(!w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().Recruiting, "Rejected recruitment must not queue a soldier.");
				MedievalSettlement.Warehouses(w.LocalPlayer).First().Trait<MedievalStockpile>().Stock.Add(2, heldMeat);
			}
			if (tick == 20)
			{
				Place(w, "md.house", Cell(34, 32));
				Place(w, "md.field", Cell(20, 42));
				Place(w, "md.pasture", Cell(41, 43));
			}
			if (tick == 100) Place(w, "md.house", Cell(22, 31));
			if (tick == 200)
				w.IssueOrder(new Order("MedievalRecruit", w.LocalPlayer.PlayerActor, false) { TargetString = "md.hauler" });
			// Barracks logistics and actual military recruitment are exercised by MedievalMilitaryValidation.
			// Paid soldier fixtures keep this existing combat, hunger and expedition scenario independent.
			if (tick == 500 || tick == 700)
			{
				var type = tick == 500 ? "md.militia" : "md.manatarms";
				Check(MedievalSettlement.Cost(w.LocalPlayer, type).TryPay(MedievalSettlement.RecruitmentStores(w.LocalPlayer).Select(a => a.Trait<MedievalStockpile>().Stock)), "Combat fixture must pay its food cost.");
				w.AddFrameEndTask(world =>
				{
					var cell = world.Map.FindTilesInAnnulus(Cell(30, 32), 0, 4).First(c => !world.ActorMap.GetActorsAt(c).Any());
					world.CreateActor(type, [new OwnerInit(world.LocalPlayer), new LocationInit(cell)]);
				});
			}
			if (tick == 390) CheckRecruitment(w, 130, 80, 9, "Villager: 10 food, no meat");
			if (tick == 590) CheckRecruitment(w, 110, 80, 10, "Militia fixture: 20 food, no meat");
			if (tick == 790) CheckRecruitment(w, 80, 70, 11, "Man at arms fixture: 30 food and 10 meat");
			// The original 2x2 decorative pens cannot support a breeding pair. Exercise
			// production with a real 4x4 herd, after the exact recruitment-cost checks.
			if (tick == 800) w.AddFrameEndTask(world =>
			{
				foreach (var pen in world.ActorsHavingTrait<MedievalLivestock>().Where(a => a.Owner == world.LocalPlayer && a.Location is var c && (c == Cell(35, 43) || c == Cell(39, 43))).ToArray()) pen.Dispose();
				var penActor = world.CreateActor("md.pasture.4x4", [new OwnerInit(world.LocalPlayer), new LocationInit(Cell(35, 43))]);
				for (var i = 0; i < 4; i++)
					penActor.Trait<MedievalLivestock>().Enter(world.CreateActor("md.cow", [new OwnerInit(world.LocalPlayer), new LocationInit(Cell(36 + i % 2, 44 + i / 2))]));
			});
			if (tick >= 730 && tick <= 800)
				Check(MedievalFoodEconomy.Hungry(w.LocalPlayer) == 0, "Stocked village must not show transient hunger when meals become due.");
			if (tick == 900)
			{
				w.IssueOrder(new Order("MedievalCreateCart", MedievalMarket.Markets(w.LocalPlayer).First(), false));
				w.AddFrameEndTask(world =>
				{
					var soldiers = world.Actors.Where(a => a.Owner == world.LocalPlayer && (a.Info.Name == "md.militia" || a.Info.Name == "md.manatarms"))
						.OrderBy(a => a.ActorID).ToArray();
					for (var i = 0; i < soldiers.Length; i++)
					{
						// Empty salvage is intentionally removed by the economy. A dedicated
						// target must survive until a real weapon destroys it.
						var target = world.CreateActor("md.integrationtarget", [new OwnerInit(world.Players.First(p => p.InternalName == "Neutral")), new LocationInit(Cell(22 + 13 * i, 35))]);
						combatTargets.Add(target);
						combatAttackers.Add(soldiers[i]);
						world.IssueOrder(new Order("ForceAttack", soldiers[i], Target.FromActor(target), false));
					}
					var creeps = world.Players.First(p => p.InternalName == "Creeps");
					expeditionSoldier = soldiers.FirstOrDefault(a => a.Info.Name == "md.manatarms");
					var neutral = world.Players.First(p => p.InternalName == "Neutral");
					famineWorker = world.CreateActor("md.hauler", [new OwnerInit(creeps), new LocationInit(Cell(52, 50))]);
					famineSoldier = world.CreateActor("md.manatarms", [new OwnerInit(creeps), new LocationInit(Cell(54, 50))]);
					famineSoldier.Trait<AutoTarget>().SetStance(famineSoldier, UnitStance.HoldFire);
					// Food in an unfinished store and other owners' warehouses must not feed them.
					unfinishedFoodWarehouse = world.CreateActor("md.market", [new OwnerInit(creeps), new LocationInit(Cell(55, 53)),
						new MedievalConstructionInit(), new MedievalStockInit([0, 100, 0, 0])]);
					rescuedCitizen = world.CreateActor("md.builder", [new OwnerInit(neutral), new LocationInit(Cell(48, 50))]);
					rescueWarehouse = world.CreateActor("md.warehouse", [new OwnerInit(neutral), new LocationInit(Cell(47, 53)),
						new MedievalStockInit([0, 40, 80, 0])]);
					rescueMarket = world.CreateActor("md.market", [new OwnerInit(neutral), new LocationInit(Cell(47, 57))]);
				});
			}
			if (tick == 1200)
			{
				supplyCart = w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.supplycart");
				Check(supplyCart != null, "Selected market must create a paid supply cart with a citizen.");
				Check(MedievalSettlement.Population(w.LocalPlayer) == 12, "Cart crew must occupy one population slot.");
			}
			if (tick == 3000 && expeditionSoldier != null)
				w.IssueOrder(new Order("Move", expeditionSoldier, Target.FromCell(w, Cell(52, 12)), false));
			if (tick == 4200 && expeditionSoldier != null)
			{
				Check(expeditionSoldier.Trait<MedievalNeedsFood>().Need.Hungry, "Soldier outside market coverage must become hungry despite food in town.");
				foodChecks.Add("Soldier becomes hungry after leaving market influence");
			}
			if (tick == 4300 && supplyCart != null)
			{
				Check(supplyCart.Trait<MedievalStockpile>().Stock.Amount(1) > 0, "Cart must load food from its market before departure.");
				w.IssueOrder(new Order("Move", supplyCart, Target.FromCell(w, Cell(51, 12)), false));
			}
			if (tick == 5400 && supplyCart != null && expeditionSoldier != null)
			{
				Check(MedievalFoodEconomy.Station(supplyCart).Covers(expeditionSoldier.CenterPosition)
					&& expeditionSoldier.Trait<MedievalNeedsFood>().Need.InSupply && !expeditionSoldier.Trait<MedievalNeedsFood>().Need.Hungry,
					"Moving cart must feed the remote soldier within its own radius.");
				Check(supplyCart.Trait<MedievalStockpile>().Stock.Amount(1) < 60, "Meals must deplete the cart's own stock.");
				foodChecks.Add("Moving supply cart restores a remote soldier using its carried food");
			}
			if (tick == 5350 && supplyCart != null) w.Selection.Combine(w, [supplyCart], false, true);
			if (tick == 5450) Game.Renderer.SaveScreenshot(Path.Combine(output, "mobile-supply-tested.png"));
			if (tick == 5500) w.Selection.Clear();
			if (tick == 5600 && supplyCart != null) w.IssueOrder(new Order("MedievalReloadCart", supplyCart, false));
			if (tick == 6700 && expeditionSoldier != null)
			{
				Check(expeditionSoldier.Trait<MedievalNeedsFood>().Need.Hungry, "Cart leaving must remove its coverage from the remote soldier.");
				w.IssueOrder(new Order("Move", expeditionSoldier, Target.FromCell(w, Cell(32, 36)), false));
				foodChecks.Add("Coverage follows the cart instead of remaining behind");
			}
			if (tick == 7400 && supplyCart != null)
			{
				Check(MedievalMarket.Markets(w.LocalPlayer).Any(a => MedievalAccess.Adjacent(supplyCart, a))
					&& supplyCart.Trait<MedievalStockpile>().Stock.Amount(1) >= 50, "Reload order must physically return and replenish the cart.");
				foodChecks.Add("Supply cart returns physically to a market and reloads");
				w.Selection.Combine(w, [supplyCart], false, true);
				foreach (var resource in new[] { 0, 3 })
				{
					var button = Ui.Root.Get<ButtonWidget>("MD_CART_CARGO_" + resource);
					Check(!button.IsDisabled(), "Cargo choice must be enabled inside market influence.");
					button.OnClick();
				}
			}
			if (tick == 7450 && supplyCart != null)
			{
				var cargo = supplyCart.Trait<MedievalSupplyCart>();
				Check(cargo.Wants(0) && cargo.Wants(1) && cargo.Wants(3) && !cargo.Wants(2), "Cargo buttons must enable wood and stone alongside food, never meat.");
				cartChecks.Add("Actual cargo buttons select food, wood and stone independently");
			}
			if (tick == 7800)
			{
				// Production has already been exercised. Stop new wood/stone extraction so
				// conservation can include loads returned to a different depot during a storm.
				foreach (var worker in w.ActorsWithTrait<MedievalWorker>().Where(p => p.Actor.Owner == w.LocalPlayer && p.Trait.Info.Profession is "lumberjack" or "stonecutter"))
					w.IssueOrder(new Order("Stop", worker.Actor, false));
				Place(w, "md.house", Cell(52, 18));
				Game.Renderer.SaveScreenshot(Path.Combine(output, "cart-loading-tested.png"));
			}
			if (tick == 8500 && supplyCart != null)
			{
				Check(w.ActorsWithTrait<MedievalWorker>().Where(p => p.Actor.Owner == w.LocalPlayer && p.Trait.Info.Profession is "lumberjack" or "stonecutter")
					.All(p => !p.Trait.Automatic), "Wood and stone production must be paused for the construction conservation check.");
				remoteMaterialsBefore = ConstructionMaterials(w);
				remoteSite = w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.house" && a.Location == Cell(52, 18));
				Check(remoteSite != null, "Remote house must be placeable without a local depot.");
				if (remoteSite != null)
				{
					Check(!MedievalConstructionSupply.Sources(w.LocalPlayer, remoteSite.CenterPosition).Any(), "Distant town warehouse must not cover the remote house.");
					Check(remoteSite.Trait<MedievalBuilding>().Materials.Amount(0) == 0 && remoteSite.Trait<MedievalBuilding>().Materials.Amount(3) == 0,
						"Remote site must wait without collecting materials from a distant warehouse.");
				}
				var stock = supplyCart.Trait<MedievalStockpile>().Stock;
				Check(stock.Amount(0) == 60 && stock.Amount(3) == 60 && stock.Amount(1) > 0,
					"Haulers must fill both selected building materials while preserving food cargo.");
				cartChecks.Add("Transporters physically load 60 wood and 60 stone; remote site waits for cart");
				w.IssueOrder(new Order("Move", supplyCart, Target.FromCell(w, Cell(50, 20)), false));
			}
			if (tick == 9400 && supplyCart != null)
			{
				Check(!MedievalSupplyCart.InMarketZone(supplyCart), "Construction cart must be outside the market loading zone.");
				Check(Ui.Root.Get<ButtonWidget>("MD_CART_CARGO_0").IsDisabled(), "Cargo choice must be disabled outside market influence.");
				w.IssueOrder(new Order("MedievalCartCargo", supplyCart, false) { ExtraData = 0 });
			}
			if (tick == 9450 && supplyCart != null)
			{
				Check(supplyCart.Trait<MedievalSupplyCart>().Wants(0), "Remote cargo-change order must be rejected by simulation, not only UI.");
				cartChecks.Add("Loading choices cannot change outside a market zone");
			}
			if (tick == 1700)
			{
				Check(famineWorker.Trait<MedievalNeedsFood>().Need.Hungry && famineSoldier.Trait<MedievalNeedsFood>().Need.Hungry,
					"Both worker and soldier must become hungry without an available meal.");
				Check(famineWorker.Trait<MedievalNeedsFood>().WorkPercent == 50
					&& ((ISpeedModifier)famineSoldier.Trait<MedievalNeedsFood>()).GetSpeedModifier() == 75
					&& famineSoldier.Trait<MedievalMorale>().State.Firepower < 100
					&& ((IFirepowerModifier)famineSoldier.Trait<MedievalNeedsFood>()).GetFirepowerModifier() == 100, "Hunger must weaken military morale without a second damage penalty.");
				Check(famineWorker.Trait<Health>().HP == famineWorker.Trait<Health>().MaxHP, "Hunger must have a grace period before damage.");
				foodChecks.Add("Worker and soldier slowed/weakened after a missed meal, no early damage");
			}
			if (tick == 4100)
			{
				Check(famineWorker.Trait<Health>().HP < famineWorker.Trait<Health>().MaxHP
					&& famineSoldier.Trait<Health>().HP < famineSoldier.Trait<Health>().MaxHP, "Prolonged famine must damage civilians and armored soldiers.");
				foodChecks.Add("Prolonged famine damages both civilians and armored troops");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "famine-tested.png"));
			}
			if (tick == 4200)
			{
				rescuedHealth = rescuedCitizen.Trait<Health>().HP;
				Check(rescuedHealth < rescuedCitizen.Trait<Health>().MaxHP, "Rescue scenario must start with a damaged, starving citizen.");
				rescueMarket.Trait<MedievalStockpile>().Stock.Add(1, 5);
			}
			if (tick == 4250)
			{
				var needs = rescuedCitizen.Trait<MedievalNeedsFood>();
				Check(!needs.Need.Hungry && needs.WorkPercent == 100 && ((ISpeedModifier)needs).GetSpeedModifier() == 100,
					"Delivery must immediately restore normal food modifiers.");
				Check(rescuedCitizen.Trait<Health>().HP == rescuedHealth, "Feeding must stop famine damage, without healing existing wounds.");
				Check(rescueMarket.Trait<MedievalStockpile>().Stock.Amount(1) == 4 && rescueWarehouse.Trait<MedievalStockpile>().Stock.Amount(1) == 40
					&& rescueWarehouse.Trait<MedievalStockpile>().Stock.Amount(2) == 80, "Rescue must spend one food, no meat or accumulated debt.");
				foodChecks.Add("One meal rescues a starving citizen, without consuming meat or healing damage");
			}
			if (tick == 6700)
			{
				Check(famineWorker.IsDead && famineSoldier.IsDead, "Unfed civilian and soldier must die after prolonged famine.");
				var owner = unfinishedFoodWarehouse.Owner;
				Check(owner.PlayerActor.Trait<MedievalFoodEconomy>().StarvationDeaths == 2 && MedievalFoodEconomy.Demand(owner) == 0,
					"Famine deaths must be counted and stop contributing to food demand.");
				Check(unfinishedFoodWarehouse.Trait<MedievalStockpile>().Stock.Amount(1) == 100, "Unfinished stores must not feed citizens.");
				foodChecks.Add("Two deaths after prolonged famine; removed people no longer consume food");
			}
			foreach (var a in w.Actors.Where(a => a.Owner == w.LocalPlayer))
			{
				var worker = a.TraitOrDefault<MedievalWorker>();
				if (worker?.Cargo > 0)
				{
					if (worker.Info.Profession == "hauler") hauled[worker.Resource] = true;
					else if (worker.Info.Profession == "builder")
					{
						builderCarried = true;
						if (tick > 8500 && (a.Location - Cell(52, 18)).LengthSquared < 100) remoteBuilderCarried = true;
					}
					else produced[worker.Resource] = true;
				}
				var source = a.TraitOrDefault<MedievalSource>();
				if (source?.Growth > 0) growing[(int)source.Info.Resource] = true;
				if (a.TraitOrDefault<MedievalLivestock>() is { Sheep: false, BreedingProgress: > 0 }) growing[2] = true;
				var b = a.TraitOrDefault<MedievalBuilding>();
				if (b == null) continue;
				if (!b.Complete) newSites.Add(a.ActorID);
				if (newSites.Contains(a.ActorID) && b.Complete && (!b.Supplied || b.Progress < b.Info.Work)) prematureCompletion = true;
				if (!cancelled && a.Location == Cell(22, 31) && Enumerable.Range(0, 4).Any(r => b.Materials.Amount(r) > 0))
				{
					w.IssueOrder(new Order("MedievalCancel", w.LocalPlayer.PlayerActor, Target.FromActor(a), false));
					cancelledSite = a; cancelledBuilding = b;
					cancelled = true;
				}
			}
			if (tick == 780) w.Selection.Combine(w, [MedievalMarket.Markets(w.LocalPlayer).First()], false, true);
			if (tick == 800) Game.Renderer.SaveScreenshot(Path.Combine(output, "village-early.png"));
			if (tick == 820) w.Selection.Clear();
			if (w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().MarketDeliveries.Total > 0) marketReceivedFood = true;
			if (tick % 500 == 0)
			{
				if (remoteSite != null)
				{
					var b = remoteSite.Trait<MedievalBuilding>();
					Console.WriteLine($"REMOTE tick={tick} supplied={b.Supplied} progress={b.Progress}/{b.Info.Work} complete={b.Complete}");
					foreach (var worker in w.ActorsWithTrait<MedievalWorker>().Where(p => p.Actor.Owner == w.LocalPlayer && p.Trait.Info.Profession == "builder"))
						Console.WriteLine($"BUILDER {worker.Actor.ActorID}: {worker.Actor.Location} {worker.Trait.Description} idle={worker.Actor.IsIdle} working={worker.Trait.IsWorking} food={worker.Actor.Trait<MedievalNeedsFood>().Description} rest={worker.Actor.TraitOrDefault<MedievalRest>()?.Resting}");
				}
				var trace = w.Actors.Where(a => a.Owner == w.LocalPlayer && a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).Select(a => new
				{
					id = a.ActorID, type = a.Info.Name, cell = a.Location.ToString(),
					worker = a.TraitOrDefault<MedievalWorker>()?.Description,
					stock = a.TraitOrDefault<MedievalStockpile>() is MedievalStockpile s ? Enumerable.Range(0, 4).Select(r => s.Stock.Amount(r)).ToArray() : null,
					complete = a.TraitOrDefault<MedievalBuilding>()?.Complete
				});
				File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { tick, hauled, produced, growing, foodChecks, cartChecks, agricultureChecks, errors, trace }, new JsonSerializerOptions { WriteIndented = true }));
			}
			// Real storms and night rest may interrupt the trip and the work. Keep a
			// hard deadline, but judge completion after the builders have actually resumed.
			if (tick < 11000 || tick < 16000 && remoteCompletedAt == 0) return;
			CheckRemoteConstruction(w);
			foreach (var expected in new[] { "md.builder:work", "md.lumberjack:harvest", "md.stonecutter:harvest", "md.farmer:work", "md.farmer:harvest", "md.breeder:work", "md.hauler:carry-run", "md.supplycart:run", "md.militia:attack", "md.manatarms:attack" })
				Check(animationChecks.Contains(expected), "Missing real activity animation: " + expected);
			Check(walkingFrames.Count == 8 && workingFrames.Count == 8 && cropFrames.Count == 8 && cattleFrames.Count == 8,
				"Walking, working, crops and cattle must play every animation pose in the real engine.");
			var trails = w.WorldActor.Trait<MedievalTrails>().Trails;
			var visibleTrailSamples = trails.Marks.Values.Count(m => m.Level > 0);
			var establishedTrailSamples = trails.Marks.Values.Count(m => m.Level >= 3);
			Check(visibleTrailSamples > 40 && establishedTrailSamples > 10, "Actual villager and cart movement must gradually create visible trails.");
			Check(hauled.All(x => x), "All four materials must have been carried by haulers.");
			Check(produced.All(x => x), "All four professions must have carried their production.");
			Check(builderCarried, "Builder must carry materials physically.");
			Check(growing[1] && growing[2], "Both farming and cattle breeding must renew after work and growth.");
			Check(cancelled && cancellationRefundChecked && cancelledMaterials != null && recoveredMaterials.SequenceEqual(cancelledMaterials),
				"Workers must physically recover all cancelled materials exactly once.");
			Check(cancelledPile != null && !cancelledPile.IsInWorld, "An emptied cancellation pile must be removed after recovery.");
			Check(!prematureCompletion, "Construction completed before receiving materials and work.");
			foreach (var cell in new[] { Cell(34, 32), Cell(20, 42), Cell(41, 43) })
				Check(w.Actors.Any(a => a.TraitOrDefault<MedievalBuilding>()?.Complete == true && a.Location == cell), "Unfinished building at " + cell);
			Check(MedievalSettlement.Population(w.LocalPlayer) == 12, "Recruitment must create one paid worker, two paid soldiers and a cart crew.");
			var economy = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>();
			Check(economy.TotalConsumed > 0 && economy.StarvationDeaths == 0 && MedievalFoodEconomy.Demand(w.LocalPlayer) == 24,
				"The growing village must sustain its 12 citizens/soldiers including the cart crew.");
			Check(marketReceivedFood, "Haulers must physically deliver food from depots to a market.");
			Check(economy.Harvests.PerMinute > 0 && economy.Deliveries.PerMinute > 0, "Budget must observe actual harvests and warehouse deliveries.");
			var combatPassed = combatTargets.Count == 2 && combatTargets.All(a => a.IsDead) && combatAttackers.All(a => a.Trait<MedievalCombatProbe>().Shots > 0);
			Check(combatPassed, "Both soldiers must fire their weapons and kill their melee targets, not merely observe their removal.");
			Game.Renderer.SaveScreenshot(Path.Combine(output, "village-tested.png"));
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, hauled, produced, growing, builderCarried, cancelled, recruitmentChecks,
				combatPassed, combatShots = combatAttackers.Select(a => new { type = a.Info.Name, shots = a.Trait<MedievalCombatProbe>().Shots }),
				cancelledMaterials, recoveredMaterials, cancellationRefundChecked, remoteSuppliedAt, remoteStartedAt, remoteCompletedAt, remoteStormTicks,
				remoteMaterialsBefore, remoteMaterialsAfter,
				visibleTrailSamples, establishedTrailSamples, foodChecks, foodConsumed = economy.TotalConsumed,
				foodDemand = MedievalFoodEconomy.Demand(w.LocalPlayer), foodHarvestedPerMinute = economy.Harvests.PerMinute,
				foodDeliveredPerMinute = economy.Deliveries.PerMinute, marketReceivedFood, cartChecks, remoteBuilderCarried, agricultureChecks,
				animationChecks = animationChecks.OrderBy(s => s).ToArray(), walkingFrames = walkingFrames.Count, workingFrames = workingFrames.Count,
				cropFrames = cropFrames.Count, cattleFrames = cattleFrames.Count }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL INTEGRATION: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true;
			Game.RunAfterDelay(1000, Game.Exit);
		}

		void AnimationTick(World w)
		{
			tick++;
			if (tick == 1) Place(w, "md.house", Cell(34, 32));
			var scenes = new[] { ("md.farmer", "work"), ("md.lumberjack", "harvest"), ("md.stonecutter", "harvest"),
				("md.builder", "work"), ("md.farmer", "harvest"), ("md.hauler", "carry-run") };
			if (animationStage < scenes.Length)
			{
				var (role, sequence) = scenes[animationStage];
				animationActor ??= w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == role
					&& a.Trait<WithMedievalInfantryBody>().Sequence == sequence);
				if (animationActor != null)
				{
					renderer.Viewport.Center(animationActor.CenterPosition);
					// Two settling ticks ensure the first image is from the new camera.
					if (++animationCapture > 2 && animationCapture % 2 == 0)
						Game.Renderer.SaveScreenshot(Path.Combine(output, $"{animationStage}-{role}-{sequence}-{animationCapture:00}.png"));
					if (animationCapture == 26) { animationStage++; animationCapture = 0; animationActor = null; }
				}
			}
			CheckAnimations(w);
			if (animationStage < scenes.Length && tick < 4500) return;
			Check(animationStage == scenes.Length, "All six real activity clips must be captured.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, clips = animationStage,
				animationChecks = animationChecks.OrderBy(s => s).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
			finished = true;
			Game.RunAfterDelay(1000, Game.Exit);
		}

		void CheckAnimations(World w)
		{
			foreach (var pair in w.ActorsWithTrait<WithMedievalInfantryBody>())
			{
				if (!pair.Actor.IsInWorld || pair.Actor.IsDead) continue;
				var body = pair.Trait;
				animationChecks.Add(pair.Actor.Info.Name + ":" + body.Sequence);
				if (body.Sequence is "run" or "carry-run") walkingFrames.Add(body.Frame);
				if (body.Sequence is "work" or "harvest") workingFrames.Add(body.Frame);
			}
			foreach (var pair in w.ActorsWithTrait<MedievalAgriculturalArea>())
			{
				if (!MedievalStockpile.Active(pair.Actor)) continue;
				var body = pair.Actor.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body");
				if (body.DefaultAnimation.CurrentSequence.Name is not ("idle" or "growing")) continue;
				if (pair.Trait.Info.Profession == "farmer") cropFrames.Add(body.DefaultAnimation.CurrentFrame);
			}
			foreach (var a in w.ActorsHavingTrait<MedievalAnimal>().Where(a => a.Info.Name == "md.cow" && a.Trait<MedievalAnimal>().Pen != null))
				cattleFrames.Add(a.Trait<MedievalAnimal>().VisualFrame(a) % 8);
		}

		void AmbienceTick(World w)
		{
			tick++;
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(26, 34)));
			if (tick >= 90 && tick <= 150 && tick % 5 == 0)
				Game.Renderer.SaveScreenshot(Path.Combine(output, $"frame-{tick:000}.png"));
			if (tick == 300) Game.Renderer.SaveScreenshot(Path.Combine(output, "village.png"));
			if (tick != 500) return;
			var scene = w.WorldActor.Trait<MedievalAmbience>().Scene;
			Check(scene.SpawnedBirds > 0 && scene.SpawnedPetals > 0 && scene.SpawnedSeeds > 0, "Ambient birds, petals and seeds must spawn in the real engine.");
			Check(scene.Particles.Count <= VillageAmbience.MaxBirds + VillageAmbience.MaxDebris, "Ambient effects must stay within their budget.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors,
				scene.SpawnedBirds, scene.SpawnedPetals, scene.SpawnedSeeds, active = scene.Particles.Count,
				vegetation = w.ActorsWithTrait<MedievalWindVegetation>().Count() }, new JsonSerializerOptions { WriteIndented = true }));
			finished = true;
			Game.RunAfterDelay(1000, Game.Exit);
		}

		void VisualTick(World w)
		{
			tick++;
			renderer.Viewport.Center(w.Map.CenterOfCell(tick < 60 ? Cell(30, 32) : tick < 120 ? Cell(31, 41) : tick < 180 ? Cell(17, 21) : Cell(30, 42)));
			if (tick == 1) w.AddFrameEndTask(world =>
			{
				foreach (var a in world.Actors.Where(a => a.TraitOrDefault<MedievalAgriculturalArea>() != null).ToArray()) a.Dispose();
				foreach (var item in new[] { ("md.field.4x5", Cell(22, 43)), ("md.pasture.5x4", Cell(35, 44)) })
				{
					var actor = world.CreateActor(item.Item1, [new OwnerInit(world.LocalPlayer), new LocationInit(item.Item2)]);
					var source = actor.Trait<MedievalSource>();
					source.Remaining = source.Info.Amount;
				}
			});
			if (tick == 30) Game.Renderer.SaveScreenshot(Path.Combine(output, "village.png"));
			if (tick == 90) Game.Renderer.SaveScreenshot(Path.Combine(output, "agriculture.png"));
			if (tick == 150) Game.Renderer.SaveScreenshot(Path.Combine(output, "woodland.png"));
			if (tick == 185) Sync.RunUnsynced(w, () =>
			{
				zonePlacement = new MedievalZoneOrderGenerator(w, renderer, "md.field");
				w.OrderGenerator = zonePlacement;
				ZoneClick(w, zonePlacement, Cell(27, 43)).ToArray();
				Viewport.LastMousePos = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(w.Map.CenterOfCell(Cell(29, 47))));
			});
			if (tick == 195)
				for (var x = 22; x <= 38; x++)
					for (var y = 34; y <= 48; y++)
					{
						var cell = Cell(x, y);
						var pixel = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(w.Map.CenterOfCell(cell)));
						var actual = renderer.Viewport.ViewToWorld(pixel);
						Check(actual == cell, $"Mouse projection round-trip failed at {cell}: got {actual}, pixel {pixel}, world pixel {renderer.Viewport.ViewToWorldPx(pixel)}");
					}
			if (tick >= 185) Viewport.LastMousePos = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(w.Map.CenterOfCell(Cell(29, 47))));
			if (tick == 200) Game.Renderer.SaveScreenshot(Path.Combine(output, "placement.png"));
			if (tick != 220) return;
			finished = true;
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { rendered = true, ticks = tick, errors }));
			Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
