using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalWorkerInfo : TraitInfo, Requires<MobileInfo>
	{
		public readonly string Profession = "builder";
		public readonly Material Resource = Material.Wood;
		public readonly int Capacity = 10;
		public readonly int WorkTicks = 12;
		public override object Create(ActorInitializer init) => new MedievalWorker(this);
	}
	public partial class MedievalWorker : ITick, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		public readonly MedievalWorkerInfo Info;
		[VerifySync] public int Cargo;
		[VerifySync] public int Resource;
		[VerifySync] public bool EquipmentCargo;
		[VerifySync] public bool Automatic = true;
		[VerifySync] int phase;
		[VerifySync] int countdown;
		[VerifySync] int cadence;
		[VerifySync] int influenceWorkRemainder;
		[VerifySync] int promised;
		[VerifySync] int failedMoves;
		[VerifySync] bool foodInNetwork;
		Actor source;
		Actor destination;
		public Actor WorkArea { get; private set; }
		// Presentation follows the real job. It never completes work or transfers stock.
		public Actor WorkTarget { get; private set; }
		public bool IsWorking { get; private set; }
		public bool IsDemolishing => IsWorking && phase == 8;
		public bool IsHarvesting => IsWorking && phase == 3 && MedievalStockpile.Active(source) && source.TraitOrDefault<MedievalSource>()?.Remaining > 0;
		[VerifySync] public int HandlingTicks { get; private set; }
		[VerifySync] public int WorkAreaId => unchecked((int)(WorkArea?.ActorID ?? 0));
		Inventory sourceStock;
		Inventory destinationStock;
		bool destinationReserved;
		public string Status { get; private set; } = "Recherche de travail";
		[VerifySync] public int JobHash => unchecked((int)(source?.ActorID ?? 0) * 31 + (int)(destination?.ActorID ?? 0) * 7 + (destinationReserved ? 1 : 0));
		public string CargoName => CampCargo ? MedievalCampStore.Names[Resource] : EquipmentCargo ? MedievalEquipment.Names[Resource] : MedievalStockDisplay.Labels[Resource];
		public string Description => Cargo > 0 ? $"{CargoName} {Cargo} - {Status}" : Status;
		public MedievalWorker(MedievalWorkerInfo info) { Info = info; Resource = (int)info.Resource; }

		IEnumerable<Actor> Nearby(Actor self, Func<Actor, bool> filter, IEnumerable<Actor> candidates = null) => (candidates ?? self.World.Actors)
			.Where(a => a.IsInWorld && !a.IsDead && filter(a))
			.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID);
		IEnumerable<Actor> Stores(Actor self, bool warehouse) => Nearby(self, a => a.Owner == self.Owner && MedievalStockpile.Active(a)
			&& a.TraitOrDefault<MedievalStockpile>() is MedievalStockpile s && s.Info.Warehouse == warehouse, self.World.ActorsHavingTrait<MedievalStockpile>());
		bool Valid(Actor self, Actor a) => a != null && a.IsInWorld && !a.IsDead && a.Owner == self.Owner;
		bool ValidSource(Actor self, Actor a) => a != null && a.IsInWorld && !a.IsDead
			&& (a.Owner == self.Owner || (a.Owner.NonCombatant && a.TraitOrDefault<MedievalSource>() != null));

		void Release()
		{
			if (source is { Disposed: false }) source.TraitOrDefault<MedievalGameAnimal>()?.ReleaseClaim();
			hasFieldCell = false;
			IsWorking = false;
			WorkTarget = null;
			ClearHandling();
			if (promised > 0 && sourceStock != null) sourceStock.ReleaseOut(Resource, promised);
			if (destinationReserved) destinationStock.ReleaseIn(Resource, Cargo + promised);
			promised = 0;
			destinationReserved = false;
			sourceStock = destinationStock = null;
			source = destination = null;
			phase = 0;
			countdown = 0;
			failedMoves = 0;
			if (Cargo == 0) foodInNetwork = false;
		}
		public void PauseForHerding() { Release(); }
		public void PauseForEmergency() { Release(); Status = "Secours incendie"; }
		public void PauseForStorm() { Release(); Status = "Court se mettre à l’abri de l’orage"; }
		bool ReserveTrip(Actor from, Inventory fromStock, Actor to, Inventory toStock, int resource, int max, bool equipment = false, bool camp = false)
		{
			var amount = Math.Min(max, Math.Min(fromStock.Available(resource), toStock.Free(resource)));
			if (amount <= 0) return false;
			if (!fromStock.ReserveOut(resource, amount)) return false;
			if (!toStock.ReserveIn(resource, amount)) { fromStock.ReleaseOut(resource, amount); return false; }
			Resource = resource;
			EquipmentCargo = equipment;
			CampCargo = camp;
			foodInNetwork = resource == (int)Material.Food && (from.TraitOrDefault<MedievalStockpile>()?.Info.Warehouse == true
				|| from.TraitOrDefault<MedievalFoodProvider>() != null);
			source = from; sourceStock = fromStock;
			destination = to; destinationStock = toStock;
			promised = amount; destinationReserved = true; phase = 1;
			Status = "Va charger";
			return true;
		}
		bool ReturnCargo(Actor self)
		{
			if (CampCargo) return ReturnCampCargo(self);
			if (EquipmentCargo) return ReturnEquipment(self);
			var localCarts = Info.Profession == "builder"
				? MedievalConstructionSupply.Sources(self.Owner, self.CenterPosition).Where(a => a.TraitOrDefault<MedievalSupplyCart>() != null)
				: Enumerable.Empty<Actor>();
			// An interrupted delivery must also be recoverable in a remote village that
			// only has a market or stall. Retain the physical load until space is reserved.
			var destinations = localCarts.Concat(Stores(self, true)).Concat(MedievalMarket.Markets(self.Owner))
				.Concat(MedievalMarketStall.Stalls(self.Owner))
				.Concat(Stores(self, false).Where(a => a.Trait<MedievalStockpile>().Info.Profession == Info.Profession)).Distinct()
				.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID);
			foreach (var a in destinations)
			{
				var stock = a.Trait<MedievalStockpile>().Stock;
				if (!stock.ReserveIn(Resource, Cargo)) continue;
				destination = a; destinationStock = stock; destinationReserved = true; phase = 2;
				Status = "Rapporte sa charge";
				return true;
			}
			Status = "Stockage plein - charge conservee";
			return false;
		}
		bool CanAssignArea(Actor self, Actor area)
		{
			if (!Valid(self, area) || !MedievalStockpile.Active(area)) return false;
			var zone = area.TraitOrDefault<MedievalAgriculturalArea>();
			return zone != null && zone.Info.Profession == Info.Profession && zone.Farm(area) != null
				&& (area == WorkArea || zone.Workers(area) < zone.Capacity);
		}
		void FindAgriculturalJob(Actor self)
		{
			if (!CanAssignArea(self, WorkArea)) WorkArea = null;
			if (Info.Profession is "breeder" or "shepherd")
			{
				var empty = Nearby(self, a => CanAssignArea(self, a) && a.Trait<MedievalLivestock>().Count + a.Trait<MedievalLivestock>().Incoming < Math.Min(2, a.Trait<MedievalLivestock>().Capacity),
					self.World.ActorsHavingTrait<MedievalLivestock>()).FirstOrDefault();
				if (empty != null && self.TraitOrDefault<MedievalHerding>()?.TryAutomatic(self, empty) == true) { WorkArea = empty; return; }
			}
			// Keep a working place while carrying its harvest. Between tasks a worker
			// may help another available zone instead of waiting through its growth.
			if (WorkArea == null || !WorkArea.Trait<MedievalSource>().NeedsWork)
				WorkArea = Nearby(self, a => a.Trait<MedievalSource>().NeedsWork && CanAssignArea(self, a), self.World.ActorsHavingTrait<MedievalAgriculturalArea>()).FirstOrDefault()
					?? WorkArea ?? Nearby(self, a => CanAssignArea(self, a), self.World.ActorsHavingTrait<MedievalAgriculturalArea>()).FirstOrDefault();
			if (WorkArea == null) { Status = "Aucune place : agrandissez les zones pres d'une ferme/elevage termine"; return; }
			if (Info.Profession is "breeder" or "shepherd" && self.TraitOrDefault<MedievalHerding>()?.TryAutomatic(self, WorkArea) == true) return;
			var area = WorkArea.Trait<MedievalAgriculturalArea>();
			var farm = area.Farm(WorkArea);
			CampCargo = Info.Profession == "shepherd"; EquipmentCargo = false;
			var stock = CampCargo ? farm.Trait<MedievalCampStore>().Stock : farm.Trait<MedievalStockpile>().Stock;
			var crop = WorkArea.Trait<MedievalSource>();
			if (!crop.NeedsWork && WorkArea.TraitOrDefault<MedievalLivestock>() is { } herd) { Status = herd.Status; return; }
			if (crop.Growth > 0) { Status = $"Croissance : {(crop.Growth + 24) / 25}s restantes"; return; }
			if (stock.Free(Resource) <= 0) { Status = "Depot agricole plein : attend les transporteurs"; return; }
			source = WorkArea; destination = farm; destinationStock = stock;
			phase = 3; countdown = Info.WorkTicks; Status = "Rejoint sa zone agricole";
			if (Info.Profession == "farmer") ChooseFieldCell(self);
		}
		void FindJob(Actor self)
		{
			if (Cargo > 0) { ReturnCargo(self); return; }
			if (Info.Profession is "breeder" or "shepherd" && self.TraitOrDefault<MedievalHerding>()?.TryAutomatic(self) == true) return;
			CampCargo = false;
			if (Info.Profession == "weaver") { FindTentWorkshopJob(self); return; }
			if (Info.Profession is "blacksmith" or "recruiter") { FindWorkshopJob(self); return; }
			if (Info.Profession is "farmer" or "breeder" or "shepherd")
			{
				Resource = (int)Info.Resource;
				FindAgriculturalJob(self);
			}
			else if (Info.Profession == "builder")
			{
				if (FindDemolitionJob(self)) return;
				foreach (var site in Nearby(self, a => a.Owner == self.Owner && a.TraitOrDefault<MedievalBuilding>() is MedievalBuilding b && !b.Complete, self.World.ActorsHavingTrait<MedievalBuilding>()))
				{
					var building = site.Trait<MedievalBuilding>();
					if (building.Demolishing) continue;
					if (building.Supplied) { destination = site; phase = 4; Status = "Construit"; return; }
					if (building.Info.Cloth > 0)
						foreach (var depot in MedievalConstructionSupply.Sources(self.Owner, site.CenterPosition).Where(a => a.TraitOrDefault<MedievalCampStore>() != null))
							if (ReserveTrip(depot, depot.Trait<MedievalCampStore>().Stock, site, building.CampMaterials, MedievalCampStore.Cloth, Info.Capacity, camp: true)) return;
					for (var r = 0; r < Inventory.ResourceCount; r++)
						foreach (var depot in MedievalConstructionSupply.Sources(self.Owner, site.CenterPosition))
							if (ReserveTrip(depot, depot.Trait<MedievalStockpile>().Stock, site, building.Materials, r, Info.Capacity)) return;
				}
				if (FindSiegeJob(self)) return;
				Status = "Attend des materiaux : entrepot ou charrette pres du chantier";
			}
			else if (Info.Profession == "hauler")
			{
				if (FindDemolitionJob(self)) return;
				// Alternate priorities so wool cannot starve food deliveries.
				if (preferCamp && FindCampHaulingJob(self)) { preferCamp = false; return; }
				preferCamp = true;
				var jobs = new List<(Actor Source, Actor Target, int Resource, int Fill, long Distance)>();
				// These lists cannot change during this synchronous search. Re-enumerating
				// and sorting every warehouse for every product/source is cubic in a large town.
				var warehouses = Stores(self, true).ToArray();
				var localStores = Stores(self, false).ToArray();
				var markets = MedievalMarket.Markets(self.Owner).ToArray();
				var constructionSites = self.World.ActorsWithTrait<MedievalBuilding>().Where(p => p.Actor.Owner == self.Owner && !p.Trait.Complete && !p.Trait.Demolishing).Select(p => p.Actor).ToArray();
				var depots = localStores.Where(a => a.TraitOrDefault<MedievalFoodProvider>() == null && a.TraitOrDefault<MedievalTradeCart>() == null).ToArray();
				var acceptingWarehouses = Enumerable.Range(0, Inventory.ResourceCount).Select(r => warehouses.Where(a => a.Trait<MedievalStockpile>().Stock.Free(r) > 0).ToArray()).ToArray();
				foreach (var depot in depots)
				{
					var sourceInventory = depot.Trait<MedievalStockpile>().Stock;
					for (var r = 0; r < Inventory.ResourceCount; r++)
					{
						if (sourceInventory.Available(r) <= 0) continue;
						foreach (var warehouse in acceptingWarehouses[r])
						{
							if (!MedievalLogistics.Local(depot, warehouse)) continue;
							var target = warehouse.Trait<MedievalStockpile>().Stock;
							jobs.Add((depot, warehouse, r, (target.Amount(r) + target.Incoming(r)) * 1000 / target.Capacity(r),
								(depot.CenterPosition - self.CenterPosition).HorizontalLengthSquared));
						}
					}
				}
				// Markets collect food and selected exports locally. Only imported materials
				// go back to local warehouses; one direction per resource avoids hauling loops.
				foreach (var market in markets)
				{
					var stock = market.Trait<MedievalStockpile>().Stock;
					var trade = market.Trait<MedievalTradeMarket>();
					for (var r = 0; r < Inventory.ResourceCount; r++)
					{
						// A full market with no possible return destination cannot schedule
						// a trip, regardless of construction/export demand.
						if (stock.Free(r) <= 0 && !acceptingWarehouses[r].Any(a => MedievalLogistics.Local(a, market, 16))) continue;
						var stallTarget = MedievalMarketStall.MarketTarget(market, r, constructionSites);
						if (r == 1 || trade.Linked(market) && trade.Exports(r) || stallTarget > 0)
						{
							if (stock.Free(r) <= 0 || r != 1 && !(trade.Linked(market) && trade.Exports(r)) && StallDemand.Request(stallTarget, stock, r) == 0) continue;
							foreach (var depot in warehouses.Concat(depots))
							{
								if (!MedievalLogistics.Local(depot, market, 16) || depot.Trait<MedievalStockpile>().Stock.Available(r) <= 0) continue;
								var fill = (stock.Amount(r) + stock.Incoming(r)) * 1000 / stock.Capacity(r);
								if (r == 1 && stock.Amount(r) + stock.Incoming(r) < 20) fill -= 1000;
								jobs.Add((depot, market, r, fill, (depot.CenterPosition - self.CenterPosition).HorizontalLengthSquared));
							}
						}
						else if (stock.Available(r) > 0)
							foreach (var warehouse in acceptingWarehouses[r].Where(a => MedievalLogistics.Local(a, market, 16)))
							{
								var destination = warehouse.Trait<MedievalStockpile>().Stock;
								if (destination.Free(r) > 0) jobs.Add((market, warehouse, r, -250, (market.CenterPosition - self.CenterPosition).HorizontalLengthSquared));
							}
					}
				}
				// Neighborhood stalls request local meals and outstanding construction
				// materials from their parent market. Existing haulers carry every load.
				foreach (var stall in MedievalMarketStall.Stalls(self.Owner))
				{
					var link = stall.Trait<MedievalMarketStall>();
					foreach (var r in new[] { 1, 0, 3, 4 })
					{
						var needed = link.Request(stall, r);
						if (needed <= 0 || link.Market.Trait<MedievalStockpile>().Stock.Available(r) <= 0) continue;
						var stock = stall.Trait<MedievalStockpile>().Stock;
						var fill = (stock.Amount(r) + stock.Incoming(r)) * 1000 / Math.Max(1, link.Desired(stall, r));
						jobs.Add((link.Market, stall, r, fill - (r == 1 ? 1200 : 500), (link.Market.CenterPosition - self.CenterPosition).HorizontalLengthSquared));
					}
				}
				// A parked cart requests only the cargo selected by its owner, and only
				// inside a completed market's loading zone. Every delivery is carried.
				foreach (var cart in localStores.Where(a => a.TraitOrDefault<MedievalSupplyCart>() != null))
				{
					var cargo = cart.Trait<MedievalSupplyCart>();
					var target = cart.Trait<MedievalStockpile>().Stock;
					foreach (var r in new[] { (int)Material.Food, (int)Material.Wood, (int)Material.Stone, (int)Material.Gold })
					{
						if (!cargo.AcceptsDelivery(cart, r) || target.Free(r) <= 0) continue;
						var sources = r == (int)Material.Food ? markets
							: warehouses.Concat(depots);
						foreach (var depot in sources)
						{
							if (!MedievalLogistics.Local(depot, cart, 16) || depot.Trait<MedievalStockpile>().Stock.Available(r) <= 0) continue;
							jobs.Add((depot, cart, r, (target.Amount(r) + target.Incoming(r)) * 1000 / target.Capacity(r) - 500,
								(depot.CenterPosition - self.CenterPosition).HorizontalLengthSquared));
						}
					}
				}
				// Balance warehouse fill before minimizing travel; a busy nearby sawmill must not starve the granary.
				foreach (var job in jobs.OrderBy(j => j.Fill).ThenBy(j => j.Distance).ThenBy(j => j.Resource)
					.ThenBy(j => j.Source.ActorID).ThenBy(j => j.Target.ActorID))
					if (ReserveTrip(job.Source, job.Source.Trait<MedievalStockpile>().Stock, job.Target,
						job.Target.Trait<MedievalStockpile>().Stock, job.Resource, Math.Min(Info.Capacity,
							HaulingLimit(job.Source, job.Target, job.Resource)))) return;
				if (FindCampHaulingJob(self)) return;
				Status = "Attend des marchandises ou de la place";
			}
			else
			{
				Resource = (int)Info.Resource;
				foreach (var depot in Stores(self, false).Where(a => a.Trait<MedievalStockpile>().Info.Profession == Info.Profession))
				{
					if (depot.Trait<MedievalStockpile>().Stock.Free(Resource) < Info.Capacity) continue;
					var range = new WVec(18000, 18000, 0);
					var nearbyNodes = self.World.ActorMap.ActorsInBox(depot.CenterPosition - range, depot.CenterPosition + range);
					var node = Nearby(self, a => ValidSource(self, a) && MedievalStockpile.Active(a)
						&& a.TraitOrDefault<MedievalSource>() is MedievalSource n && (int)n.Info.Resource == Resource && n.NeedsWork
						&& (Info.Profession != "hunter" || a.TraitOrDefault<MedievalGameAnimal>()?.Available(self) == true)
						&& (a.CenterPosition - depot.CenterPosition).HorizontalLengthSquared < 18000L * 18000, nearbyNodes).FirstOrDefault();
					if (node == null) continue;
					source = node; destination = depot; destinationStock = depot.Trait<MedievalStockpile>().Stock;
					if (Info.Profession == "hunter") node.Trait<MedievalGameAnimal>().Claim(self);
					phase = 3; countdown = Info.WorkTicks; Status = "Rejoint son lieu de travail"; return;
				}
				Status = "Attend un depot, une source ou la croissance";
			}
		}

		int HaulingLimit(Actor from, Actor to, int resource)
		{
			if (to.TraitOrDefault<MedievalMarketStall>() is MedievalMarketStall stall) return stall.Request(to, resource);
			if (to.TraitOrDefault<MedievalSupplyCart>() is MedievalSupplyCart cart) return cart.DeliveryFree(to, resource);
			if (resource != 1 && to.TraitOrDefault<MedievalTradeMarket>() is MedievalTradeMarket trade && !(trade.Linked(to) && trade.Exports(resource)))
				return StallDemand.Request(MedievalMarketStall.MarketTarget(to, resource), to.Trait<MedievalStockpile>().Stock, resource);
			if (from.TraitOrDefault<MedievalMarket>() != null && to.Trait<MedievalStockpile>().Info.Warehouse)
				return Math.Max(0, from.Trait<MedievalStockpile>().Stock.Available(resource) - MedievalMarketStall.MarketTarget(from, resource));
			return Info.Capacity;
		}

		// Work occurs only next to the footprint. Move activities perform actual pathfinding.
		bool Approach(Actor self, Actor target)
		{
			var dimensions = target.Info.TraitInfoOrDefault<BuildingInfo>()?.Dimensions ?? new CVec(1, 1);
			var loc = target.Location;
			var x = self.Location.X; var y = self.Location.Y;
			// A diagonal corner was considered close enough, leaving a visible gap
			// between the tool and its target. Work from a face or inside a passable area.
			var alongX = x >= loc.X && x < loc.X + dimensions.X;
			var alongY = y >= loc.Y && y < loc.Y + dimensions.Y;
			if ((alongX && y >= loc.Y - 1 && y <= loc.Y + dimensions.Y)
				|| (alongY && x >= loc.X - 1 && x <= loc.X + dimensions.X))
			{ failedMoves = 0; return true; }
			var mobile = self.Trait<Mobile>();
			var cells = new List<CPos>();
			for (var dy = -1; dy <= dimensions.Y; dy++)
				for (var dx = -1; dx <= dimensions.X; dx++)
					if ((dx == -1 || dy == -1 || dx == dimensions.X || dy == dimensions.Y)
						&& ((dx >= 0 && dx < dimensions.X) || (dy >= 0 && dy < dimensions.Y)))
						cells.Add(loc + new CVec(dx, dy));
			var options = cells.Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.Immovable))
				.OrderBy(c => (c - self.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).ToArray();
			if (options.Length == 0 || failedMoves >= options.Length)
			{ Release(); cadence = 125; Status = "Acces bloque"; return false; }
			self.QueueActivity(mobile.MoveTo(options[failedMoves++]));
			IsWorking = false;
			WorkTarget = null;
			return false;
		}
		void FaceWork(Actor self, Actor target)
		{
			IsWorking = true;
			WorkTarget = target;
			var direction = target.CenterPosition - self.CenterPosition;
			if (direction.HorizontalLengthSquared > 0) self.Trait<Mobile>().Facing = direction.Yaw;
		}
		void ITick.Tick(Actor self)
		{
			if (self.TraitOrDefault<MedievalPatient>()?.Church != null) return;
			if (self.TraitOrDefault<MedievalFirefighter>()?.Busy == true) return;
			if (self.TraitOrDefault<MedievalHerding>()?.Busy == true) return;
			if (self.TraitOrDefault<MedievalRest>()?.Resting == true) return;
			if (!HandlingStock && HandlingTicks > 0) HandlingTicks--;
			if (--cadence > 0 && !HandlingStock) return;
			var workPercent = self.TraitOrDefault<MedievalNeedsFood>()?.BaseWorkPercent ?? 100;
			if (phase == 3 && self.World.WorldActor.TraitOrDefault<MedievalSeasons>() is { } seasons)
				workPercent = Math.Max(5, workPercent * SeasonClock.Work(seasons.Clock.Season, Info.Profession) / 100);
			var influenceWork = self.TraitOrDefault<MedievalInfluenceMood>()?.WorkPercent ?? 100;
			cadence = InfluenceWork.Delay((800 + workPercent - 1) / workPercent, influenceWork, ref influenceWorkRemainder);
			workPercent = (workPercent * influenceWork + 99) / 100;
			if (WorkArea != null && !CanAssignArea(self, WorkArea)) WorkArea = null;
			if (!Automatic) return;
			if ((source != null && !ValidSource(self, source)) || (destination != null && !Valid(self, destination)))
			{ self.CancelActivity(); Release(); }
			if (MedievalDemolition.Pending(source) || phase != 8 && MedievalDemolition.Pending(destination)
				|| phase == 8 && !MedievalDemolition.Pending(destination))
			{ self.CancelActivity(); Release(); }
			// A cart can move while a worker is walking to it. Revalidate the local
			// construction source before pickup, releasing both reservations if it leaves.
			if (Info.Profession == "builder" && phase == 1 && source.TraitOrDefault<MedievalSalvage>() == null
				&& !MedievalConstructionSupply.CanSupply(source, self.Owner, destination.CenterPosition))
			{ self.CancelActivity(); Release(); }
			if (Info.Profession == "hauler" && destination?.TraitOrDefault<MedievalSupplyCart>() is MedievalSupplyCart cart
				&& !(CampCargo ? destination.Trait<MedievalCampCart>().AcceptsTents(destination) : cart.AcceptsDelivery(destination, Resource)))
			{ self.CancelActivity(); Release(); }
			// A booked market-to-stall route depends on its link. Returning an already
			// carried load has no source and must remain possible even for an isolated stall.
			if (source != null && source.TraitOrDefault<MedievalSalvage>() == null && !CampCargo && Info.Profession == "hauler" && destination?.TraitOrDefault<MedievalMarketStall>() is MedievalMarketStall stall
				&& (!stall.Linked(destination) || source != stall.Market))
			{ self.CancelActivity(); Release(); }
			if (HandlingStock) { AdvanceHandling(self); return; }
			if (!self.IsIdle) return;
			if (phase == 0)
			{
				var start = PerfTickLogger.GetTimestamp(); FindJob(self);
				PerfTickLogger.LogLongTick(start, "Medieval job", Info.Profession); return;
			}
			if (phase == 1 && ApproachStock(self, source, sourceStock, true, out var pickup))
				BeginHandling(self, source, pickup, true, workPercent);
			else if (phase == 2 && ApproachStock(self, destination, destinationStock, false, out var drop))
				BeginHandling(self, destination, drop, false, workPercent);
			else if (phase == 3 && (Info.Profession == "farmer" ? ApproachField(self) : Info.Profession == "hunter" ? ApproachGame(self) : Approach(self, source)))
			{
				var node = source.Trait<MedievalSource>();
				if (node.Falling) { IsWorking = false; WorkTarget = null; Status = "Attend la chute de l'arbre"; return; }
				if (!node.NeedsWork) { Release(); return; }
				Status = "Travaille";
				FaceWork(self, source);
				if (Info.Profession == "hunter") Status = source.Trait<MedievalGameAnimal>().Down ? "Prépare le gibier" : "Ajuste sa visée";
				if (Info.Profession == "farmer") FaceFieldRow(self);
				countdown--;
				if (Info.Profession == "hunter" && countdown == 2 && !source.Trait<MedievalGameAnimal>().Down)
				{
					var shot = new HuntingArrowEffect(self, source, cadence * 2);
					self.World.AddFrameEndTask(w => w.Add(shot));
					MedievalAudio.At(self.World, "bow", self.CenterPosition);
				}
				if (countdown > 0) return;
				if (node.Remaining == 0) { node.Cultivate(); Release(); return; }
				var amount = Math.Min(Info.Capacity, Math.Min(node.Remaining, destinationStock.Free(Resource)));
				if (amount <= 0 || !destinationStock.ReserveIn(Resource, amount)) { Release(); return; }
				Cargo = node.Harvest(amount); destinationReserved = true;
				if (Info.Profession == "hunter") { source.Trait<MedievalGameAnimal>().Harvested(source); source = null; }
				IsWorking = false; WorkTarget = null; HandlingTicks = 8;
				if (!CampCargo && Resource == (int)Material.Food) self.Owner.PlayerActor.Trait<MedievalFoodEconomy>().Harvests.Record(Cargo);
				phase = 2; failedMoves = 0; Status = "Rapporte sa production";
			}
			else if (phase == 4 && Approach(self, destination))
			{
				var building = destination.Trait<MedievalBuilding>();
				FaceWork(self, destination);
				building.Work(destination);
				if (building.Complete) Release();
			}
			else if (phase == 5 && Approach(self, destination))
			{
				FaceWork(self, destination);
				if (!destination.Trait<MedievalMilitaryWorkshop>().Work(destination)) Release();
			}
			else if (phase == 6 && Approach(self, destination))
			{
				FaceWork(self, destination);
				if (!destination.Trait<MedievalSiegeWorkshop>().Work(destination)) Release();
			}
			else if (phase == 7 && Approach(self, destination))
			{
				FaceWork(self, destination);
				if (!destination.Trait<MedievalTentWorkshop>().Work(destination)) Release();
			}
			else if (phase == 8 && Approach(self, destination))
			{
				FaceWork(self, destination); Status = "Demonte le batiment";
				destination.Trait<MedievalDemolition>().Work(destination, self);
			}
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalWork") { self.CancelActivity(); Release(); Automatic = true; }
			else if (order.OrderString == "Move" || order.OrderString == "Stop") { Release(); WorkArea = null; Automatic = false; Status = "Ordre manuel"; }
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Release(); WorkArea = null; }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Release(); WorkArea = null; Automatic = false; }
	}
}
