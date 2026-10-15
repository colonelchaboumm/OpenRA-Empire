using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalMilitaryWorkshopInfo : TraitInfo, Requires<MedievalEquipmentStoreInfo>, Requires<MedievalBuildingInfo>
	{
		public readonly bool Forge = false;
		public readonly int MaxStaff = 1;
		public readonly int WorkSteps = 40;
		public override object Create(ActorInitializer init) => new MedievalMilitaryWorkshop(this);
	}
	public sealed class MedievalMilitaryWorkshop : ITick, IResolveOrder, INotifyOwnerChanged, ISync
	{
		public static readonly string[] Types = ["md.forge", "md.barracks"];
		public readonly MedievalMilitaryWorkshopInfo Info;
		public readonly Inventory Inputs = new([20, 40, 20, 20]);
		[VerifySync] public int InputsHash => Inputs.Hash;
		readonly List<uint> staffIds = [];
		[VerifySync] public int StaffHash => staffIds.Aggregate(17, (hash, id) => unchecked(hash * 31 + (int)id));
		[VerifySync] public int Product = -1;
		[VerifySync] public int Recruit = -1;
		[VerifySync] public int Progress;
		[VerifySync] public int StaffTraining;
		[VerifySync] bool spawning;
		public int PendingPopulation => (StaffTraining > 0 ? 1 : 0) + (Recruit >= 0 ? 1 : 0);
		public string Status { get; private set; } = "Recrutez l'artisan pour activer ce batiment.";
		public MedievalMilitaryWorkshop(MedievalMilitaryWorkshopInfo info) { Info = info; }
		public IEnumerable<Actor> Team(Actor self) => self.World.Actors.Where(a => staffIds.Contains(a.ActorID) && a.Owner == self.Owner && a.IsInWorld && !a.IsDead).OrderBy(a => a.ActorID);
		public Actor Staff(Actor self) => Team(self).FirstOrDefault();
		public int StaffCount(Actor self) => Team(self).Count();
		public bool HasStaff(Actor staff) => staffIds.Contains(staff.ActorID);
		public bool HasVacancy(Actor self) => StaffCount(self) + (StaffTraining > 0 ? 1 : 0) < Info.MaxStaff;
		public bool AssignStaff(Actor self, Actor staff)
		{
			if (staff.Owner != self.Owner || !staff.IsInWorld || staff.IsDead
				|| staff.TraitOrDefault<MedievalWorker>()?.Info.Profession != (Info.Forge ? "blacksmith" : "recruiter")) return false;
			if (HasStaff(staff)) return true;
			if (!HasVacancy(self)) return false;
			var living = Team(self).Select(a => a.ActorID).ToHashSet();
			staffIds.RemoveAll(id => !living.Contains(id));
			staffIds.Add(staff.ActorID); staffIds.Sort(); return true;
		}
		// One nearby relay owns each forge's demand, even when several influence zones overlap.
		public Actor SupplyStall(Actor self) => !Info.Forge ? null : MedievalMarketStall.Stalls(self.Owner)
			.Where(a => a.Trait<MedievalMarketStall>().Linked(a) && MedievalConstructionSupply.CanSupply(a, self.Owner, self.CenterPosition))
			.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
		public bool CanHire(Actor self) => MedievalStockpile.Active(self) && HasVacancy(self) && StaffTraining == 0
			&& MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) < MedievalSettlement.Housing(self.Owner)
			&& new RecruitmentCost(10).CanPay(MedievalSettlement.RecruitmentStores(self.Owner).Select(a => a.Trait<MedievalStockpile>().Stock));
		public bool CanRecruit(Actor self) => !Info.Forge && MedievalStockpile.Active(self) && Staff(self) != null && Recruit < 0
			&& MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) < MedievalSettlement.Housing(self.Owner);
		public bool Request(Actor self, string type)
		{
			var index = Array.IndexOf(MedievalEquipment.BarracksRecruits, type);
			if (index < 0 || !CanRecruit(self)) { Status = "Caserne, recruteur et place libre requis."; return false; }
			Recruit = index; Progress = 0; Status = "Commande : " + MedievalEquipment.BarracksNames[index]; return true;
		}
		public int[] Required(Actor self)
		{
			if (Info.Forge) return Product >= 0 ? MedievalEquipment.Recipes[Product] : [0, 0, 0, 0];
			if (Recruit < 0) return [0, 0, 0, 0];
			var cost = MedievalSettlement.Cost(self.Owner, MedievalEquipment.BarracksRecruits[Recruit]);
			return [0, cost.Food, cost.Meat, 0];
		}
		public bool Ready(Actor self)
		{
			if ((Info.Forge ? Product : Recruit) < 0) return false;
			if (!Enumerable.Range(0, 4).All(r => Inputs.Amount(r) >= Required(self)[r])) return false;
			return Info.Forge || Enumerable.Range(0, 3).All(r => self.Trait<MedievalEquipmentStore>().Stock.Available(r) >= MedievalEquipment.Kits[Recruit][r])
				&& (self.TraitOrDefault<MedievalCampStore>()?.Stock.Available(MedievalCampStore.Cloth) ?? 0) >= RequiredCloth(self);
		}
		public int RequiredCloth(Actor self) => Info.Forge || Recruit < 0 ? 0
			: self.World.Map.Rules.Actors[MedievalEquipment.BarracksRecruits[Recruit]].TraitInfo<MedievalRecruitableInfo>().Cloth;
		public string MissingSupplies(Actor self)
		{
			var required = Required(self);
			var missing = Enumerable.Range(0, 4).Where(r => Inputs.Amount(r) < required[r])
				.Select(r => $"{required[r] - Inputs.Amount(r)} {MedievalStockDisplay.Labels[r]}");
			if (!Info.Forge && Recruit >= 0)
				missing = missing.Concat(Enumerable.Range(0, 3).Where(r => self.Trait<MedievalEquipmentStore>().Stock.Available(r) < MedievalEquipment.Kits[Recruit][r])
					.Select(r => $"{MedievalEquipment.Kits[Recruit][r] - self.Trait<MedievalEquipmentStore>().Stock.Available(r)} {MedievalEquipment.Names[r]}"));
			var cloth = RequiredCloth(self) - (self.TraitOrDefault<MedievalCampStore>()?.Stock.Available(MedievalCampStore.Cloth) ?? 0);
			if (cloth > 0) missing = missing.Append($"{cloth} tissu");
			return "Attend : " + string.Join(" / ", missing);
		}
		public void Plan(Actor self)
		{
			if (!Info.Forge || Product >= 0) return;
			if (Enumerable.Range(0, 3).Any(r => self.Trait<MedievalEquipmentStore>().Stock.Available(r) > 0)) { Status = "Equipement termine : livraison au stockage."; return; }
			var economy = self.Owner.PlayerActor.Trait<MedievalEquipmentEconomy>();
			var next = Enumerable.Range(0, 3).Where(r => MedievalEquipment.Committed(self.Owner, r) < economy.Target(r)
				&& MedievalEquipment.Stores(self.Owner).Any(a => a.Trait<MedievalEquipmentStore>().Stock.Free(r) > 0))
				.OrderByDescending(r => (economy.Target(r) - MedievalEquipment.Committed(self.Owner, r)) * 100 / Math.Max(1, economy.Target(r)))
				.ThenBy(r => r).Select(r => (int?)r).FirstOrDefault();
			if (next == null) { Status = "Objectifs atteints, nuls ou stocks pleins."; return; }
			Product = next.Value; Progress = 0; Status = "Prepare : " + MedievalEquipment.Names[Product];
		}
		public bool Work(Actor self)
		{
			if (!Ready(self) || spawning) return false;
			if (!Info.Forge && MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) > MedievalSettlement.Housing(self.Owner))
			{ Status = "Formation suspendue : logements insuffisants."; return false; }
			Status = (Info.Forge ? "Fabrication " : "Formation ") + $"{Progress}/{Info.WorkSteps}";
			Progress = Math.Min(Info.WorkSteps, Progress + 1);
			if (Progress < Info.WorkSteps) return true;
			if (Info.Forge)
			{
				if (self.Trait<MedievalEquipmentStore>().Stock.Free(Product) == 0) return false;
				for (var r = 0; r < 4; r++) Inputs.Take(r, Required(self)[r]);
				self.Trait<MedievalEquipmentStore>().Stock.Add(Product, 1);
				Product = -1; Progress = 0; Status = "Equipement termine : livraison au stockage.";
			}
			else
			{
				var cell = SpawnCell(self);
				if (cell == null) { Progress = Info.WorkSteps; Status = "Sortie de la caserne bloquee."; return false; }
				var owner = self.Owner;
				var expectedRecruit = Recruit;
				spawning = true;
				self.World.AddFrameEndTask(w =>
				{
					spawning = false;
					if (!MedievalStockpile.Active(self) || self.WillDispose || self.Owner != owner || Recruit != expectedRecruit
						|| !Ready(self) || Progress < Info.WorkSteps || w.ActorMap.GetActorsAt(cell.Value).Any()
						|| MedievalSettlement.Population(owner) + MedievalSettlement.PendingPopulation(owner) > MedievalSettlement.Housing(owner)) return;
					for (var r = 0; r < 4; r++) Inputs.Take(r, Required(self)[r]);
					for (var r = 0; r < 3; r++) self.Trait<MedievalEquipmentStore>().Stock.Take(r, MedievalEquipment.Kits[Recruit][r]);
					self.TraitOrDefault<MedievalCampStore>()?.Stock.Take(MedievalCampStore.Cloth, RequiredCloth(self));
					w.CreateActor(MedievalEquipment.BarracksRecruits[Recruit], [new OwnerInit(self.Owner), new LocationInit(cell.Value)]);
					MedievalAudio.Notify(self, "ready");
					Recruit = -1; Progress = 0; Status = "Unite formee. Nouvelle commande disponible.";
				});
			}
			return false;
		}
		static CPos? SpawnCell(Actor self) => MedievalAccess.Perimeter(self).Concat(self.World.Map.FindTilesInAnnulus(self.Location, 2, 6))
			.Where(c => self.World.Map.Contains(c) && !self.World.ActorMap.GetActorsAt(c).Any()
				&& new[] { "Clear", "Road", "Rough" }.Contains(self.World.Map.GetTerrainInfo(c).Type)).Select(c => (CPos?)c).FirstOrDefault();
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalHireArtisan" && CanHire(self)
				&& new RecruitmentCost(10).TryPay(MedievalSettlement.RecruitmentStores(self.Owner).Select(a => a.Trait<MedievalStockpile>().Stock)))
			{ StaffTraining = 100; Status = "Recrutement de l'artisan..."; }
			else if (order.OrderString == "MedievalTrainSoldier") Request(self, order.TargetString);
			else if (order.OrderString == "MedievalCancelTraining" && !Info.Forge)
			{
				Recruit = -1; Progress = 0; Status = "Commande annulee. Provisions conservees.";
				if (Staff(self) is Actor staff) ((IResolveOrder)staff.Trait<MedievalWorker>()).ResolveOrder(staff, new Order("MedievalWork", staff, false));
			}
		}
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self) || StaffTraining <= 0 || spawning) return;
			if (StaffTraining > 1) { StaffTraining--; return; }
			if (MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) > MedievalSettlement.Housing(self.Owner)) return;
			var cell = SpawnCell(self); if (cell == null) return;
			var owner = self.Owner;
			spawning = true;
			self.World.AddFrameEndTask(w =>
			{
				spawning = false;
				if (!MedievalStockpile.Active(self) || self.WillDispose || self.Owner != owner || StaffTraining == 0
					|| w.ActorMap.GetActorsAt(cell.Value).Any()
					|| MedievalSettlement.Population(owner) + MedievalSettlement.PendingPopulation(owner) > MedievalSettlement.Housing(owner)) return;
				StaffTraining = 0;
				AssignStaff(self, w.CreateActor(Info.Forge ? "md.blacksmith" : "md.recruiter", [new OwnerInit(self.Owner), new LocationInit(cell.Value)]));
				Status = $"Artisans : {StaffCount(self)}/{Info.MaxStaff}.";
			});
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Recruit = -1; StaffTraining = 0; staffIds.Clear(); Progress = 0; }
	}

	public partial class MedievalWorker
	{
		bool ReturnEquipment(Actor self)
		{
			foreach (var depot in MedievalEquipment.Stores(self.Owner).OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID))
			{
				var stock = depot.Trait<MedievalEquipmentStore>().Stock;
				if (!stock.ReserveIn(Resource, Cargo)) continue;
				destination = depot; destinationStock = stock; destinationReserved = true; phase = 2; Status = "Rapporte " + CargoName; return true;
			}
			Status = "Equipement conserve : depot plein ou absent."; return false;
		}
		void FindWorkshopJob(Actor self)
		{
			var forge = Info.Profession == "blacksmith";
			var workshops = MedievalEquipment.Workshops(self.Owner).Where(a => a.Trait<MedievalMilitaryWorkshop>().Info.Forge == forge).ToArray();
			var home = workshops.FirstOrDefault(a => a.Trait<MedievalMilitaryWorkshop>().HasStaff(self))
				?? workshops.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID)
					.FirstOrDefault(a => a.Trait<MedievalMilitaryWorkshop>().HasVacancy(a));
			if (home == null) { Status = "Aucun atelier termine disponible."; return; }
			var workshop = home.Trait<MedievalMilitaryWorkshop>();
			if (!workshop.AssignStaff(home, self)) return;
			var equipment = home.Trait<MedievalEquipmentStore>().Stock;
			var stores = MedievalEquipment.Stores(self.Owner).OrderBy(a => (a.CenterPosition - home.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).ToArray();
			if (forge)
			{
				for (var r = 0; r < 3; r++)
					foreach (var depot in stores)
						if (ReserveTrip(home, equipment, depot, depot.Trait<MedievalEquipmentStore>().Stock, r, 1, true)) return;
				workshop.Plan(home);
			}
			else
			{
				// Return cancelled or surplus kits before requesting another unit's equipment.
				var cloth = home.Trait<MedievalCampStore>().Stock;
				var textileStores = MedievalCampStore.Stores(self.Owner).OrderBy(a => (a.CenterPosition - home.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).ToArray();
				var clothNeeded = workshop.RequiredCloth(home);
				if (cloth.Available(MedievalCampStore.Cloth) > clothNeeded)
					foreach (var depot in textileStores)
						if (ReserveTrip(home, cloth, depot, depot.Trait<MedievalCampStore>().Stock, MedievalCampStore.Cloth,
							cloth.Available(MedievalCampStore.Cloth) - clothNeeded, camp: true)) return;
				var missingCloth = clothNeeded - cloth.Amount(MedievalCampStore.Cloth) - cloth.Incoming(MedievalCampStore.Cloth);
				if (missingCloth > 0)
					foreach (var depot in textileStores)
						if (ReserveTrip(depot, depot.Trait<MedievalCampStore>().Stock, home, cloth, MedievalCampStore.Cloth, Math.Min(Info.Capacity, missingCloth), camp: true)) return;
				for (var r = 0; r < 3; r++)
				{
					var needed = workshop.Recruit < 0 ? 0 : MedievalEquipment.Kits[workshop.Recruit][r];
					if (equipment.Available(r) <= needed) continue;
					foreach (var depot in stores)
						if (ReserveTrip(home, equipment, depot, depot.Trait<MedievalEquipmentStore>().Stock, r, equipment.Available(r) - needed, true)) return;
				}
				if (workshop.Recruit >= 0)
					for (var r = 0; r < 3; r++)
					{
						var missing = MedievalEquipment.Kits[workshop.Recruit][r] - equipment.Amount(r) - equipment.Incoming(r);
						if (missing <= 0) continue;
						foreach (var depot in stores)
							if (ReserveTrip(depot, depot.Trait<MedievalEquipmentStore>().Stock, home, equipment, r, missing, true)) return;
					}
			}
			var required = workshop.Required(home);
			var relay = workshop.SupplyStall(home);
			// Wait for the local relay's haulers instead of immediately booking the same goods at a distant depot.
			var rawStores = relay == null ? stores : new[] { relay };
			for (var r = 0; r < 4; r++)
			{
				var missing = required[r] - workshop.Inputs.Amount(r) - workshop.Inputs.Incoming(r);
				if (missing <= 0) continue;
				foreach (var depot in rawStores)
					if (ReserveTrip(depot, depot.Trait<MedievalStockpile>().Stock, home, workshop.Inputs, r, Math.Min(Info.Capacity, missing))) return;
			}
			if (workshop.Ready(home)) { destination = home; phase = 5; Status = forge ? "Forge l'equipement" : "Entraine le soldat"; return; }
			Status = (forge ? workshop.Product >= 0 : workshop.Recruit >= 0) ? workshop.MissingSupplies(home) : workshop.Status;
			if (relay != null && workshop.Product >= 0) Status += " (petit marche)";
		}
	}
}
