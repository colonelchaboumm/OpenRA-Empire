using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSettlementInfo : TraitInfo
	{
		public readonly int RecruitmentTicks = 100;
		public override object Create(ActorInitializer init) => new MedievalSettlement(this);
	}
	public class MedievalSettlement : IResolveOrder, ITick, ISync
	{
		public static readonly string[] Buildings = ["md.house", "md.warehouse", "md.sawmill", "md.quarry", "md.farm", "md.field", "md.ranch", "md.pasture", "md.market", "md.marketstall"];
		public static readonly string[] Recruits = ["md.builder", "md.hauler", "md.lumberjack", "md.stonecutter", "md.farmer", "md.breeder", "md.militia", "md.manatarms", "md.shepherd", "md.weaver", "md.hunter", "md.goldminer"];
		public static readonly string[] BuildingNames = ["Maison", "Entrepot", "Scierie", "Carriere", "Ferme", "Parcelle", "Elevage", "Enclos", "Marche", "Etal de marche"];
		public static readonly string[] RecruitNames = ["Batisseur", "Transporteur", "Bucheron", "Tailleur de pierre", "Fermier", "Eleveur", "Milicien", "Homme d'armes", "Eleveur de moutons", "Tisseur", "Chasseur", "Mineur d'or"];
		readonly MedievalSettlementInfo info;
		[VerifySync] int training;
		[VerifySync] int recruit = -1;
		[VerifySync] public int HousesPlaced { get; private set; }
		[VerifySync] int houseRequestsResolved;
		// Local UI acknowledgement only; this field never participates in simulation decisions.
		int? pendingHouseRequest;
		public bool HousePlacementPending => pendingHouseRequest.HasValue && pendingHouseRequest.Value == houseRequestsResolved;
		public void AwaitHousePlacement() { pendingHouseRequest = houseRequestsResolved; }
		public string Message { get; private set; } = "Choisissez un batiment puis son emplacement.";
		public bool Recruiting => recruit >= 0;
		public void Notify(string message) { Message = message; }
		public MedievalSettlement(MedievalSettlementInfo info) { this.info = info; }
		public static IEnumerable<Actor> Warehouses(Player p) => p.World.ActorsHavingTrait<MedievalStockpile>().Where(a => a.Owner == p && MedievalStockpile.Active(a)
			&& a.TraitOrDefault<MedievalStockpile>()?.Info.Warehouse == true).OrderBy(a => a.ActorID);
		public static IEnumerable<Actor> RecruitmentStores(Player p) => Warehouses(p).Concat(MedievalMarket.Markets(p)).OrderBy(a => a.ActorID);
		public static int Available(Player p, int r) => RecruitmentStores(p).Concat(p.World.ActorsHavingTrait<MedievalFoundingCaravan>().Where(a => a.Owner == p && MedievalStockpile.Active(a)))
			.Sum(a => a.Trait<MedievalStockpile>().Stock.Available(r));
		public static int CitizenCount(Actor a) => a.TraitOrDefault<MedievalFoundingCaravan>()?.Info.Crew.Length ?? 1;
		public static int Population(Player p) => p.World.ActorsHavingTrait<MedievalRecruitable>().Where(a => a.Owner == p && MedievalTowerArcher.Present(a)).Sum(CitizenCount);
		public static int PendingPopulation(Player p) => (p.PlayerActor.Trait<MedievalSettlement>().Recruiting ? 1 : 0)
			+ MedievalMarket.Markets(p).Count(a => a.Trait<MedievalMarket>().Recruiting)
			+ MedievalChurch.Churches(p).Count(a => a.Trait<MedievalChurch>().Recruiting)
			+ MedievalEquipment.Workshops(p).Sum(a => a.Trait<MedievalMilitaryWorkshop>().PendingPopulation)
			+ MedievalSiegeWorkshop.Workshops(p).Sum(a => a.Trait<MedievalSiegeWorkshop>().PendingPopulation);
		public static RecruitmentCost Cost(Player p, string type) => p.World.Map.Rules.Actors[type].TraitInfo<MedievalRecruitableInfo>().Cost;
		public static int Housing(Player p) => p.World.ActorsWithTrait<MedievalBuilding>().Where(a => a.Actor.Owner == p && MedievalStockpile.Active(a.Actor))
			.Sum(a => a.Trait.Info.Population) + p.World.ActorsHavingTrait<MedievalFoundingCaravan>().Where(a => a.Owner == p && MedievalStockpile.Active(a)).Sum(CitizenCount);
		public static bool CanPlace(World world, Player player, string type, CPos cell)
		{
			if (!world.Map.Rules.Actors.TryGetValue(type, out var actor)) return false;
			var area = actor.TraitInfoOrDefault<MedievalAgriculturalAreaInfo>();
			var decoration = actor.HasTraitInfo<MedievalDecorationInfo>() || actor.HasTraitInfo<MedievalFortificationInfo>();
			if (!Buildings.Contains(type) && area == null && !MedievalDecorations.Types.Contains(type) && !MedievalFortifications.Types.Contains(type) && !MedievalMilitaryWorkshop.Types.Contains(type) && type != "md.siegeworkshop" && type != "md.sheepfold" && type != "md.tentweaver" && type != "md.torch" && type != "md.huntinglodge" && type != "md.goldmine" && type != "md.church") return false;
			var bi = actor.TraitInfo<BuildingInfo>();
			var cells = bi.Tiles(cell).ToHashSet();
			var onPaving = actor.TraitInfoOrDefault<MedievalDecorationInfo>() is { Ground: false } && bi.Dimensions == new CVec(1, 1);
			if (cells.Any(c => !world.Map.Contains(c) || !player.Shroud.IsExplored(c))) return false;
			var paving = onPaving ? MedievalDecorations.At(world, player, cell) : null;
			if (paving?.Trait<MedievalDecoration>().Info.Ground == true && MedievalStockpile.Active(paving))
			{
				// Paving is a walkable building, so the engine's generic building-influence
				// check rejects it. Ignore only this floor; retain terrain and occupant checks.
				if (world.Map.Ramp[cell] != 0 || !bi.TerrainTypes.Contains(world.Map.GetTerrainInfo(cell).Type)
					|| world.ActorMap.GetActorsAt(cell).Any(a => a != paving)) return false;
			}
			else if (!world.CanPlaceBuilding(cell, actor, bi, null)) return false;
			if (cells.Any(c => MedievalStockyard.Covers(world, c))) return false;
			if (world.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Info.HasTraitInfo<BuildingInfo>()
				&& (area != null || decoration || a.TraitOrDefault<MedievalDecoration>() != null || a.TraitOrDefault<MedievalFortification>() != null || a.TraitOrDefault<MedievalAgriculturalArea>() != null)
				&& !(onPaving && a.Owner == player && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalDecoration>()?.Info.Ground == true)
				&& a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location).Any(cells.Contains))) return false;
			if (area != null) return MedievalAgriculturalArea.FindFarm(world, player, area.Farm, cell, bi.Dimensions) != null;
			var extension = actor.TraitInfo<MedievalBuildingInfo>().ExpansionOf;
			if (extension.Length == 0) return true;
			// Expansions belong to a nearby working farm/ranch and retain a one-cell access lane.
			return world.Actors.Any(a => a.Owner == player && a.Info.Name == extension && MedievalStockpile.Active(a)
				&& (a.Location - cell).LengthSquared <= 64);
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalFortify" && order.Target.Type == TargetType.Terrain)
				MedievalFortifications.Place(self, order.TargetString, self.World.Map.CellContaining(order.Target.CenterPosition), order.ExtraLocation, text => Message = text);
			else if (order.OrderString == "MedievalDecorate" && order.Target.Type == TargetType.Terrain)
				MedievalDecorations.Place(self, order.TargetString, self.World.Map.CellContaining(order.Target.CenterPosition), order.ExtraLocation, text => Message = text);
			else if (order.OrderString == "MedievalRemoveDecoration" && order.Target.Type == TargetType.Actor)
				MedievalDecorations.Remove(self, order.Target.Actor, text => Message = text);
			else if (order.OrderString == "MedievalPlaceZone")
			{
				if (!AgriculturalZone.IsKind(order.TargetString) || order.Target.Type != TargetType.Terrain) return;
				var zone = AgriculturalZone.Between(self.World.Map.CellContaining(order.Target.CenterPosition), order.ExtraLocation);
				if (!zone.ValidSize) { Message = "La zone doit mesurer entre 2 et 10 cases par cote."; return; }
				Place(self, zone.ActorType(order.TargetString), zone.Start);
			}
			else if (order.OrderString == "MedievalPlace")
			{
				var type = order.TargetString;
				var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
				Place(self, type, cell);
			}
			else if (order.OrderString == "MedievalRecruit")
			{
				if (MedievalEquipment.Soldiers.Contains(order.TargetString))
				{
					var barracks = MedievalEquipment.Workshops(self.Owner).FirstOrDefault(a => a.Trait<MedievalMilitaryWorkshop>().CanRecruit(a));
					if (barracks == null) { Message = "Selectionnez une caserne avec recruteur et logement libre."; return; }
					barracks.Trait<MedievalMilitaryWorkshop>().Request(barracks, order.TargetString); return;
				}
				var index = Array.IndexOf(Recruits, order.TargetString);
				if (index < 0 || Recruiting) return;
				if (!Warehouses(self.Owner).Any()) { Message = "Un entrepot termine est necessaire."; return; }
				if (Population(self.Owner) + PendingPopulation(self.Owner) >= Housing(self.Owner)) { Message = "Construisez une maison pour loger plus d'habitants."; return; }
				var cost = Cost(self.Owner, Recruits[index]);
				if (!cost.TryPay(RecruitmentStores(self.Owner).Select(a => a.Trait<MedievalStockpile>().Stock)))
				{ Message = "Recrutement : " + cost + " necessaires dans les entrepots ou marches."; return; }
				recruit = index; training = info.RecruitmentTicks; Message = "Recrutement : " + RecruitNames[index];
			}
			else if (order.OrderString == "MedievalCancel" && order.Target.Type == TargetType.Actor)
			{
				var a = order.Target.Actor;
				var b = a.TraitOrDefault<MedievalBuilding>();
				if (a.Owner != self.Owner || !a.IsInWorld || b == null || b.Complete) return;
				self.World.AddFrameEndTask(w =>
				{
					if (!a.IsInWorld || a.IsDead || a.WillDispose || a.Owner != self.Owner || b.Complete) return;
					var salvage = w.CreateActor("md.salvage", [new OwnerInit(self.Owner), new LocationInit(a.Location)]);
					for (var r = 0; r < Inventory.ResourceCount; r++) salvage.Trait<MedievalStockpile>().Stock.Add(r, b.Materials.Amount(r));
					a.Dispose();
					Message = "Chantier annule : les materiaux restent recuperables sur place.";
				});
			}
		}
		void Place(Actor self, string type, CPos cell)
		{
			self.World.AddFrameEndTask(w =>
			{
				if (type == "md.house") houseRequestsResolved++;
				if (!CanPlace(w, self.Owner, type, cell)) { Message = "Zone occupee, terrain invalide ou ferme/elevage termine trop loin (8 cases)."; return; }
				var site = type == "md.house"
					? w.CreateActor(type, [new OwnerInit(self.Owner), new LocationInit(cell), new MedievalConstructionInit(), new MedievalHouseStyleInit(HouseStyleDeck.Next(self.Owner))])
					: w.CreateActor(type, [new OwnerInit(self.Owner), new LocationInit(cell), new MedievalConstructionInit()]);
				if (type == "md.house") HousesPlaced++;
				Message = MedievalConstructionSupply.Sources(self.Owner, site.CenterPosition).Any()
					? "Chantier pose. Les batisseurs vont chercher les materiaux."
					: "Chantier hors portee des depots : amenez une charrette de bois et pierre.";
			});
		}
		void ITick.Tick(Actor self)
		{
			if (!Recruiting || --training > 0) return;
			if (Population(self.Owner) + PendingPopulation(self.Owner) > Housing(self.Owner))
			{ training = 25; Message = "Recrue en attente de logements."; return; }
			var warehouse = Warehouses(self.Owner).FirstOrDefault();
			if (warehouse == null) { training = 25; Message = "Recrue en attente d'un entrepot."; return; }
			var cell = self.World.Map.FindTilesInAnnulus(warehouse.Location, 3, 6)
				.Where(c => self.World.Map.Contains(c) && !self.World.ActorMap.GetActorsAt(c).Any()
					&& new[] { "Clear", "Road", "Rough" }.Contains(self.World.Map.GetTerrainInfo(c).Type))
				.OrderBy(c => (c - warehouse.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X)
				.Select(c => (CPos?)c).FirstOrDefault();
			if (cell == null) { training = 25; Message = "Sortie de l'entrepot bloquee."; return; }
			var type = Recruits[recruit];
			training = 25;
			self.World.AddFrameEndTask(w =>
			{
				if (!Recruiting || !MedievalStockpile.Active(warehouse) || warehouse.WillDispose || warehouse.Owner != self.Owner
					|| w.ActorMap.GetActorsAt(cell.Value).Any() || Population(self.Owner) + PendingPopulation(self.Owner) > Housing(self.Owner)) return;
				var recruited = w.CreateActor(type, [new OwnerInit(self.Owner), new LocationInit(cell.Value)]);
				recruit = -1;
				MedievalAudio.Notify(recruited, "ready");
				Message = w.Map.Rules.Actors[type].HasTraitInfo<MedievalWorkerInfo>()
					? "Villageois recrute et affecte automatiquement." : "Soldat recrute. Selectionnez-le pour lui donner des ordres.";
			});
		}
	}
}
