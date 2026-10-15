using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Orders;
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
	public sealed class MedievalManualTransfer : ITick, IIssueOrder, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		Actor building;
		ManualCargo cargo;
		[VerifySync] int good, cadence, attempts, elapsed, handling;
		[VerifySync] bool loading;
		[VerifySync] WPos lastPosition;
		[VerifySync] public bool ManualControl { get; private set; }
		[VerifySync] public bool Collecting { get; private set; }
		[VerifySync] public int BuildingId => unchecked((int)(building?.ActorID ?? 0));
		[VerifySync] public int Quantity => cargo?.Quantity ?? 0;
		public bool Busy => cargo != null || Collecting;
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
		public static string UnavailableReason(Actor a)
		{
			if (a == null) return "Aucune charrette disponible : recrutez-en une ou libérez un transport.";
			if (!MedievalStockpile.Active(a) || a.WillDispose) return "Cette charrette n’est plus disponible.";
			if (a.TraitOrDefault<MedievalManualTransfer>() is not { Busy: false }) return "Un transfert est déjà en cours. Attendez ou annulez-le.";
			if (a.TraitOrDefault<MedievalBarterCart>()?.Busy == true) return "Charrette affectée à un marchandage.";
			if (a.TraitOrDefault<MedievalConstructionConvoy>()?.Active == true) return "Charrette affectée à un chantier. Arrêtez le convoi d’abord.";
			if (a.TraitOrDefault<MedievalTradeCart>()?.Active == true) return "Route commerciale active. Mettez-la en pause d’abord.";
			if (a.TraitOrDefault<MedievalStoneCart>()?.Active == true) return "Ravitaillement du trébuchet actif. Mettez-le en pause d’abord.";
			return a.TraitOrDefault<MedievalRest>()?.Resting == true ? "Le conducteur se repose." : null;
		}
		public static int Maximum(Actor cart, Actor store, int commodity, bool load)
		{
			if (commodity < 0 || commodity >= StorageGoods.Count || !Available(cart) || !Store(store, cart.Owner)) return 0;
			var from = StorageGoods.Stock(load ? store : cart, commodity);
			var to = StorageGoods.Stock(load ? cart : store, commodity);
			var r = StorageGoods.Index(commodity);
			return Math.Max(0, Math.Min(from?.Available(r) ?? 0, to?.Free(r) ?? 0));
		}
		public static bool CanCollect(Actor cart, Actor pile) => Available(cart) && MedievalDroppedCargo.Visible(pile, cart.Owner)
			&& Enumerable.Range(0, StorageGoods.Count).Any(g => (StorageGoods.Stock(pile, g)?.Available(StorageGoods.Index(g)) ?? 0) > 0
				&& (StorageGoods.Stock(cart, g)?.Free(StorageGoods.Index(g)) ?? 0) > 0);
		sealed class CargoTargeter : UnitOrderTargeter
		{
			public CargoTargeter() : base("MedievalCollectCargo", 7, "enter", true, true) { }
			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor) =>
				!modifiers.HasModifier(TargetModifiers.ForceMove) && CanCollect(self, target);
			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor) => false;
		}
		IEnumerable<IOrderTargeter> IIssueOrder.Orders { get { yield return new CargoTargeter(); } }
		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) => new("MedievalCollectCargo", self, target, false);
		void Cancel(Actor self, string status, bool stop)
		{
			cargo?.Cancel(); cargo = null; Collecting = false; building = null; attempts = cadence = elapsed = handling = 0;
			if (stop) self.CancelActivity();
			Status = status;
		}
		// Only the trait accepting a valid automatic command may replace a manual booking.
		public void ResumeAutomatic(Actor self)
		{
			if (!ManualControl) return;
			Cancel(self, "Commandes automatiques réactivées.", true); ManualControl = false;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalCollectCargo")
			{
				if (order.Target.Type != TargetType.Actor || !CanCollect(self, order.Target.Actor)) return;
				self.CancelActivity(); self.TraitOrDefault<MedievalSupplyCart>()?.CancelReload();
				building = order.Target.Actor; Collecting = ManualControl = true; attempts = cadence = elapsed = handling = 0; lastPosition = self.CenterPosition;
				Status = "Rejoint la cargaison abandonnée. Chargement sur place, selon la capacité libre."; return;
			}
			if (order.OrderString is "MedievalLoadResource" or "MedievalUnloadResource")
			{
				if (order.Target.Type != TargetType.Actor || order.ExtraData >= StorageGoods.Count
					|| !int.TryParse(order.TargetString, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0) return;
				var target = order.Target.Actor; var commodity = (int)order.ExtraData; var load = order.OrderString == "MedievalLoadResource";
				if (Maximum(self, target, commodity, load) < quantity) { Status = "Transfert impossible : stock, capacité ou charrette indisponible."; return; }
				var reserved = ManualCargo.Reserve(StorageGoods.Stock(load ? target : self, commodity),
					StorageGoods.Stock(load ? self : target, commodity), StorageGoods.Index(commodity), quantity);
				if (reserved == null) return;
				self.CancelActivity(); self.TraitOrDefault<MedievalSupplyCart>()?.CancelReload();
				cargo = reserved; building = target; good = commodity; loading = load;
				ManualControl = true; attempts = cadence = elapsed = handling = 0;
				lastPosition = self.CenterPosition;
				Status = "Rejoint le bâtiment pour " + (loading ? "charger " : "décharger ") + quantity + " " + StorageGoods.Names[good] + ".";
			}
			else if (order.OrderString is "Move" or "Stop" or "Scatter" or "MedievalCancelTransfer")
				Cancel(self, "Transfert annulé. Ressources conservées.", order.OrderString == "MedievalCancelTransfer");
		}
		void ITick.Tick(Actor self)
		{
			if (!Busy) return;
			if (!MedievalStockpile.Active(self) || self.WillDispose || !(Collecting ? MedievalDroppedCargo.Visible(building, self.Owner) : Store(building, self.Owner)))
			{ Cancel(self, "Transfert annulé : bâtiment ou charrette indisponible.", true); return; }
			if (self.CenterPosition != lastPosition) { lastPosition = self.CenterPosition; elapsed = 0; }
			if (++elapsed > 1500) { Cancel(self, "Accès bloqué trop longtemps. Transfert annulé.", true); return; }
			if (--cadence > 0) return; cadence = 10;
			if (!self.IsIdle) return;
			if (!MedievalLogistics.Approach(self, building, ref attempts)) { handling = 0; return; }
			Status = Collecting ? "Récupération de la cargaison abandonnée…" : (loading ? "Chargement" : "Déchargement") + " : " + Quantity + " " + StorageGoods.Names[good] + "…";
			if (++handling < 3) return;
			if (Collecting)
			{
				var recovered = CargoRecovery.Transfer(MedievalBarter.Stocks(building), MedievalBarter.Stocks(self));
				Collecting = false; building = null;
				Status = recovered > 0 ? recovered + " marchandises récupérées. Déposez-les dans votre dépôt." : "Plus de marchandise compatible ou charrette pleine.";
				return;
			}
			var count = Quantity; cargo.Complete(); cargo = null; building = null;
			Status = count + " " + StorageGoods.Names[good] + (loading ? " chargés. Charrette disponible." : " déposés. Charrette disponible.");
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) => Cancel(self, "Charrette retirée.", false);
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{ Cancel(self, "Transfert annulé après changement de propriétaire.", true); ManualControl = false; }
	}
}
