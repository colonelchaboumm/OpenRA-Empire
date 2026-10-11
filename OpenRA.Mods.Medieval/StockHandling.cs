using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct StorageContact(int ZoneId, CPos Cell, int Amount)
	{
		public bool Outside => ZoneId != 0;
	}
	public static class StorageHandling
	{
		public const int Duration = 24;
		// Same packing order as the displayed stacks. A transfer changes only its
		// contacted pile; loads spanning piles are handled in several physical stops.
		public static StorageContact Plan(IReadOnlyList<StorageZone> zones, int good, int total, int maximum, bool pickup, CPos indoor)
		{
			if (maximum <= 0 || pickup && total <= 0) return new(0, indoor, 0);
			var cursor = 0;
			foreach (var zone in zones)
			{
				var capacity = zone.Capacity(good); var used = 0;
				foreach (var slot in zone.Slots)
				{
					if (slot.Good != good) continue;
					var count = Math.Min(StorageGoods.Limit(good), capacity - used);
					if (count <= 0) break;
					if (pickup ? total > cursor && total <= cursor + count : total >= cursor && total < cursor + count)
						return new(zone.Id, slot.Cell, Math.Min(maximum, pickup ? total - cursor : cursor + count - total));
					cursor += count; used += count;
				}
			}
			return new(0, indoor, pickup ? Math.Min(maximum, total - cursor) : maximum);
		}
		public static IEnumerable<CPos> Faces(CPos cell)
		{
			yield return cell + new CVec(0, -1); yield return cell + new CVec(-1, 0);
			yield return cell + new CVec(1, 0); yield return cell + new CVec(0, 1);
		}
		public static int Pose(int elapsed, int duration) => Math.Clamp(elapsed * 8 / Math.Max(1, duration), 0, 7);
		public static bool AtBuilding(Actor unit, Actor target)
		{
			var size = target.Info.TraitInfoOrDefault<BuildingInfo>()?.Dimensions ?? new CVec(1, 1);
			var delta = unit.Location - target.Location;
			return delta.X >= 0 && delta.X < size.X && delta.Y >= -1 && delta.Y <= size.Y
				|| delta.Y >= 0 && delta.Y < size.Y && delta.X >= -1 && delta.X <= size.X;
		}
	}
	public partial class MedievalWorker
	{
		[VerifySync] public bool HandlingStock { get; private set; }
		[VerifySync] public bool HandlingPickup { get; private set; }
		[VerifySync] public bool HandlingCommitted { get; private set; }
		[VerifySync] public int HandlingDuration { get; private set; }
		[VerifySync] public int HandlingAmount { get; private set; }
		[VerifySync] public int HandlingZone { get; private set; }
		[VerifySync] public CPos HandlingCell { get; private set; }
		[VerifySync] public WPos HandlingPosition { get; private set; }
		public int HandlingGood => CampCargo ? Resource + 8 : EquipmentCargo ? Resource + 5 : Resource;
		public int HandlingElapsed => HandlingDuration - HandlingTicks;
		public int HandlingPose => StorageHandling.Pose(HandlingElapsed, HandlingDuration);
		public float HandlingProgress => Math.Clamp(HandlingElapsed / (float)Math.Max(1, HandlingDuration), 0, 1);
		StorageContact Contact(Actor target, Inventory inventory, bool pickup)
		{
			var maximum = pickup ? promised : Cargo;
			if (target.TraitOrDefault<MedievalStockyard>() is { HasArea: true } yard && ReferenceEquals(inventory, StorageGoods.Stock(target, HandlingGood)))
				return StorageHandling.Plan(yard.Zones, HandlingGood, inventory.Amount(Resource), maximum, pickup, target.Location);
			return new(0, target.Location, maximum);
		}
		bool SafeContact(Actor target, StorageContact contact)
		{
			var fire = target.TraitOrDefault<MedievalBurnable>();
			return !contact.Outside || fire?.Burning != true || fire.YardOnly && target.Trait<MedievalStockyard>().At(target.World.Map.CellContaining(fire.Origin))?.Id != contact.ZoneId;
		}
		bool ApproachStock(Actor self, Actor target, Inventory inventory, bool pickup, out StorageContact contact)
		{
			contact = Contact(target, inventory, pickup);
			if (contact.Amount <= 0 || !SafeContact(target, contact)) { Release(); cadence = 50; Status = "Reserve indisponible ou en feu"; return false; }
			if (!contact.Outside) return Approach(self, target);
			var cells = StorageHandling.Faces(contact.Cell).ToArray();
			if (cells.Contains(self.Location)) { failedMoves = 0; return true; }
			var mobile = self.Trait<Mobile>();
			var pile = contact.Cell;
			var options = cells.Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.Immovable))
				// Use an accessible front face first so tall stacks do not hide the gesture.
				.OrderBy(c => c.X + c.Y < pile.X + pile.Y ? 1 : 0)
				.ThenBy(c => (c - self.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).ToArray();
			if (options.Length == 0 || failedMoves >= options.Length) { Release(); cadence = 125; Status = "Acces au tas bloque : charge conservee"; return false; }
			self.QueueActivity(mobile.MoveTo(options[failedMoves++])); IsWorking = false; WorkTarget = null;
			Status = pickup ? "Rejoint le tas pour charger" : "Apporte sa charge au tas";
			return false;
		}
		void BeginHandling(Actor self, Actor target, StorageContact contact, bool pickup, int workPercent)
		{
			HandlingStock = true; HandlingPickup = pickup; HandlingCommitted = false;
			HandlingDuration = (StorageHandling.Duration * 100 + workPercent - 1) / workPercent; HandlingTicks = HandlingDuration;
			HandlingAmount = contact.Amount; HandlingZone = contact.ZoneId; HandlingCell = contact.Cell;
			HandlingPosition = contact.Outside ? self.World.Map.CenterOfCell(contact.Cell) : target.CenterPosition;
			var direction = HandlingPosition - self.CenterPosition;
			if (direction.HorizontalLengthSquared > 0) self.Trait<Mobile>().Facing = direction.Yaw;
			IsWorking = false; WorkTarget = target;
			Status = pickup ? "Se penche et prend sa charge" : "Pose et range sa charge";
		}
		void ClearHandling()
		{
			HandlingStock = HandlingCommitted = false; HandlingTicks = 0;
			HandlingDuration = HandlingAmount = HandlingZone = 0;
		}
		void AdvanceHandling(Actor self)
		{
			if (!self.IsIdle) { ClearHandling(); return; }
			var target = HandlingPickup ? source : destination; var inventory = HandlingPickup ? sourceStock : destinationStock;
			if (!HandlingCommitted)
			{
				var contact = Contact(target, inventory, HandlingPickup);
				var adjacent = contact.Outside ? StorageHandling.Faces(contact.Cell).Contains(self.Location) : StorageHandling.AtBuilding(self, target);
				if (contact.Amount <= 0 || contact.ZoneId != HandlingZone || contact.Cell != HandlingCell || !adjacent || !SafeContact(target, contact))
				{ ClearHandling(); WorkTarget = null; cadence = 1; return; }
			}
			HandlingTicks--;
			if (!HandlingCommitted && HandlingElapsed >= HandlingDuration / 2)
			{
				HandlingAmount = Math.Min(HandlingAmount, Contact(target, inventory, HandlingPickup).Amount);
				if (HandlingPickup)
				{ sourceStock.Load(Resource, HandlingAmount); Cargo += HandlingAmount; promised -= HandlingAmount; }
				else
				{
					if (!CampCargo && !EquipmentCargo && Resource == (int)Material.Food)
					{
						var market = destination.TraitOrDefault<MedievalMarket>() != null; var economy = self.Owner.PlayerActor.Trait<MedievalFoodEconomy>();
						if (!foodInNetwork && (market || destination.TraitOrDefault<MedievalStockpile>()?.Info.Warehouse == true)) economy.Deliveries.Record(HandlingAmount);
						if (market) economy.MarketDeliveries.Record(HandlingAmount);
					}
					destinationStock.Unload(Resource, HandlingAmount); Cargo -= HandlingAmount;
					if (!CampCargo && !EquipmentCargo && MedievalStockpile.Active(destination))
					{ destination.TraitOrDefault<MedievalBuildingAnimation>()?.Receive(Resource, HandlingAmount); destination.TraitOrDefault<MedievalGoldWorkshopAnimation>()?.Receive(Resource, HandlingAmount); }
					if (Cargo == 0) destinationReserved = false;
				}
				HandlingCommitted = true;
			}
			if (HandlingTicks > 0) return;
			var pickup = HandlingPickup; ClearHandling(); WorkTarget = null; failedMoves = 0; cadence = 1;
			if (pickup) { if (promised == 0) phase = 2; Status = promised == 0 ? "Livre sa charge" : "Complete sa charge au tas suivant"; }
			else if (Cargo == 0) { Release(); Status = "Livraison terminee"; }
			else Status = "Complete le depot au tas suivant";
		}
	}
}
