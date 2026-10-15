using System;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// Both reservations live until the cart reaches the building. Cancellation never moves goods.
	public sealed class ManualCargo
	{
		readonly Inventory source, destination;
		readonly int resource;
		public int Quantity { get; private set; }
		ManualCargo(Inventory source, Inventory destination, int resource, int quantity)
		{ this.source = source; this.destination = destination; this.resource = resource; Quantity = quantity; }
		public static ManualCargo Reserve(Inventory source, Inventory destination, int resource, int quantity)
		{
			if (source == null || destination == null || source == destination || resource < 0 || resource >= Inventory.ResourceCount
				|| quantity <= 0 || source.Available(resource) < quantity || destination.Free(resource) < quantity) return null;
			if (!source.ReserveOut(resource, quantity)) return null;
			if (!destination.ReserveIn(resource, quantity)) { source.ReleaseOut(resource, quantity); return null; }
			return new ManualCargo(source, destination, resource, quantity);
		}
		public void Complete() { source.Load(resource, Quantity); destination.Unload(resource, Quantity); Quantity = 0; }
		public void Cancel() { source.ReleaseOut(resource, Quantity); destination.ReleaseIn(resource, Quantity); Quantity = 0; }
	}

	public class MedievalManualTransferInfo : TraitInfo, Requires<MobileInfo>, Requires<MedievalStockpileInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalManualTransfer();
	}
	public sealed class MedievalManualTransfer : ITick, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		Actor building;
		ManualCargo cargo;
		[VerifySync] int good, cadence, attempts, elapsed, handling;
		[VerifySync] bool loading;
		[VerifySync] WPos lastPosition;
		[VerifySync] public bool ManualControl { get; private set; }
		[VerifySync] public int BuildingId => unchecked((int)(building?.ActorID ?? 0));
		[VerifySync] public int Quantity => cargo?.Quantity ?? 0;
		public bool Busy => cargo != null;
		public Actor Building => building;
		public string Status { get; private set; } = "Glissez une ressource pour préparer un transfert.";
		public static bool Manual(Actor a) => a.TraitOrDefault<MedievalManualTransfer>()?.ManualControl == true;
		public static bool Store(Actor a, Player owner) => a != null && MedievalStockpile.Active(a) && !a.WillDispose && a.Owner == owner
			&& a.TraitOrDefault<MedievalBuilding>() is { Complete: true, Demolishing: false }
			&& Enumerable.Range(0, StorageGoods.Count).Any(g => StorageGoods.Stock(a, g)?.Capacity(StorageGoods.Index(g)) > 0);
		public static bool Available(Actor a) => a != null && MedievalStockpile.Active(a) && !a.WillDispose
			&& a.TraitOrDefault<MedievalManualTransfer>() is { Busy: false }
			&& a.TraitOrDefault<MedievalBarterCart>()?.Busy != true
			&& a.TraitOrDefault<MedievalConstructionConvoy>()?.Active != true
			&& a.TraitOrDefault<MedievalTradeCart>()?.Active != true
			&& a.TraitOrDefault<MedievalStoneCart>()?.Active != true
			&& a.TraitOrDefault<MedievalRest>()?.Resting != true;
		public static int Maximum(Actor cart, Actor store, int commodity, bool load)
		{
			if (commodity < 0 || commodity >= StorageGoods.Count || !Available(cart) || !Store(store, cart.Owner)) return 0;
			var from = StorageGoods.Stock(load ? store : cart, commodity);
			var to = StorageGoods.Stock(load ? cart : store, commodity);
			var r = StorageGoods.Index(commodity);
			return Math.Max(0, Math.Min(from?.Available(r) ?? 0, to?.Free(r) ?? 0));
		}
		void Cancel(Actor self, string status, bool stop)
		{
			cargo?.Cancel(); cargo = null; building = null; attempts = cadence = elapsed = handling = 0;
			if (stop) self.CancelActivity();
			Status = status;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString is "MedievalLoadResource" or "MedievalUnloadResource")
			{
				if (order.Target.Type != TargetType.Actor || order.ExtraData >= StorageGoods.Count
					|| !int.TryParse(order.TargetString, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0) return;
				var target = order.Target.Actor; var commodity = (int)order.ExtraData; var load = order.OrderString == "MedievalLoadResource";
				if (Maximum(self, target, commodity, load) < quantity) { Status = "Transfert impossible : stock, capacité ou charrette indisponible."; return; }
				var reserved = ManualCargo.Reserve(StorageGoods.Stock(load ? target : self, commodity),
					StorageGoods.Stock(load ? self : target, commodity), StorageGoods.Index(commodity), quantity);
				if (reserved == null) return;
				self.CancelActivity(); cargo = reserved; building = target; good = commodity; loading = load;
				ManualControl = true; attempts = cadence = elapsed = handling = 0;
				lastPosition = self.CenterPosition;
				Status = "Rejoint le bâtiment pour " + (loading ? "charger " : "décharger ") + quantity + " " + StorageGoods.Names[good] + ".";
			}
			else if (order.OrderString is "Move" or "Stop" or "Scatter" or "MedievalCancelTransfer")
				Cancel(self, "Transfert annulé. Ressources conservées.", order.OrderString == "MedievalCancelTransfer");
			else if (order.OrderString is "MedievalCartCargo" or "MedievalReloadCart" or "MedievalLoadTents" or "MedievalSupplySite"
				or "MedievalAssignTrader" or "MedievalResumeTrade" or "MedievalRecoverTrade" or "MedievalSiegeSupply" or "MedievalStonePause" or "MedievalStoneRecover")
			{ if (ManualControl) { Cancel(self, "Commandes automatiques réactivées.", true); ManualControl = false; } }
		}
		void ITick.Tick(Actor self)
		{
			if (!Busy) return;
			if (!MedievalStockpile.Active(self) || self.WillDispose || !Store(building, self.Owner))
			{ Cancel(self, "Transfert annulé : bâtiment ou charrette indisponible.", true); return; }
			if (self.CenterPosition != lastPosition) { lastPosition = self.CenterPosition; elapsed = 0; }
			if (++elapsed > 1500) { Cancel(self, "Accès bloqué trop longtemps. Transfert annulé.", true); return; }
			if (--cadence > 0) return; cadence = 10;
			if (!self.IsIdle) return;
			if (!MedievalLogistics.Approach(self, building, ref attempts)) { handling = 0; return; }
			Status = (loading ? "Chargement" : "Déchargement") + " : " + Quantity + " " + StorageGoods.Names[good] + "…";
			if (++handling < 3) return;
			var count = Quantity; cargo.Complete(); cargo = null; building = null;
			Status = count + " " + StorageGoods.Names[good] + (loading ? " chargés. Charrette disponible." : " déposés. Charrette disponible.");
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) => Cancel(self, "Charrette retirée.", false);
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{ Cancel(self, "Transfert annulé après changement de propriétaire.", true); ManualControl = false; }
	}
}
