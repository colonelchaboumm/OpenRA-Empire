using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum BarterState { Invited, Negotiating, Offered, Delivering, Completed, Refused, Interrupted }
	public sealed class BarterDeal
	{
		public uint Id, Revision, MailRevision;
		public Actor Messenger;
		public Player First, Second, Proposer;
		public readonly Actor[] Depots = new Actor[2];
		public readonly int[][] Goods = [new int[StorageGoods.Count], new int[StorageGoods.Count]];
		public readonly BarterLeg[] Legs = new BarterLeg[2];
		public readonly int[] CartCounts = [1, 1];
		public BarterState State;
		public int ChangedAt;
		public string Status = "Le messager porte votre invitation.";
		public bool Negotiable => State is BarterState.Invited or BarterState.Negotiating or BarterState.Offered;
		public bool Active => Negotiable || State == BarterState.Delivering;
		public int Side(Player p) => p == First ? 0 : p == Second ? 1 : -1;
		public Player Player(int side) => side == 0 ? First : Second;
		public bool Returning => Legs.Any(l => l != null && !l.Done);
		public int Hash
		{
			get
			{
				var values = new[] { (int)Id, (int)Revision, (int)MailRevision, (int)Messenger.ActorID, (int)First.PlayerActor.ActorID,
					(int)Second.PlayerActor.ActorID, (int)(Proposer?.PlayerActor.ActorID ?? 0), (int)State, ChangedAt };
				return values.Concat(Depots.Select(a => (int)(a?.ActorID ?? 0))).Concat(Goods.SelectMany(g => g))
					.Concat(CartCounts).Concat(Legs.Select(l => l?.Hash ?? 0)).Aggregate(17, (h, n) => unchecked(h * 31 + n));
			}
		}
	}

	public class MedievalBarterInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalBarter();
	}
	public sealed class MedievalBarter : ITick, ISync
	{
		public const int MaximumCarts = 100;
		public static IEnumerable<Actor> AvailableCarts(Player p) => p == null ? [] : p.World.ActorsHavingTrait<MedievalTradeCart>()
			.Where(a => MedievalStockpile.Active(a) && !a.WillDispose && a.Owner == p && a.TraitOrDefault<MedievalBarterCart>() is { Busy: false }
				&& a.Trait<MedievalTradeCart>().CanLend && a.TraitOrDefault<MedievalRest>()?.Resting != true
				&& Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Stock(a, g) is { } s && s.Amount(StorageGoods.Index(g)) == 0
					&& s.Incoming(StorageGoods.Index(g)) == 0 && s.Reserved(StorageGoods.Index(g)) == 0)).OrderBy(a => a.ActorID);
		readonly List<BarterDeal> deals = [];
		uint nextId = 1;
		[VerifySync] public int NextIdHash => unchecked((int)nextId);
		public IReadOnlyList<BarterDeal> Deals => deals;
		[VerifySync] public int DealsHash => deals.Aggregate(17, (h, d) => unchecked(h * 31 + d.Hash));
		public static bool Depot(Actor a, Player owner) => MedievalStockpile.Active(a) && !a.WillDispose && a.Owner == owner
			&& a.TraitOrDefault<MedievalStockpile>()?.Info.Warehouse == true && a.TraitOrDefault<Building>() != null;
		public static IEnumerable<Actor> Depots(Player p) => p.World.ActorsHavingTrait<MedievalStockpile>().Where(a => Depot(a, p)).OrderBy(a => a.ActorID);
		public static Inventory[] Stocks(Actor a) => Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Stock(a, g)).ToArray();
		public static string DepotName(Actor a) => a == null ? "Aucun dépôt" : $"Dépôt ({a.Location.X}, {a.Location.Y})";
		public BarterDeal ForMessenger(Actor a) => deals.LastOrDefault(d => d.Messenger == a && d.Active);
		public bool CanInvite(Actor a, Player p) => p != null && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalMessenger>() is { } mail && mail.CanCompose(a, p)
			&& ForMessenger(a) == null && Depots(p).Any() && deals.Count(d => d.Active && d.Side(p) >= 0) < 8;
		public bool Visible(BarterDeal d, Player p) => d.Side(p) >= 0 && (p == d.First || d.State != BarterState.Invited
			|| d.Messenger.IsInWorld && d.Messenger.Trait<MedievalMessenger>().CanReceive(d.Messenger, p));
		public static Order Command(Player p, BarterDeal d, string command, string payload = "", Actor depot = null) =>
			new(command, p.PlayerActor, false) { ExtraData = d.Id, TargetString = d.Revision.ToString(CultureInfo.InvariantCulture) + ";" + payload,
				ExtraActors = depot == null ? [] : [depot] };
		static void Changed(BarterDeal d, World w) { d.Revision++; d.ChangedAt = w.WorldTick; }
		static bool MessengerPresent(BarterDeal d)
		{
			if (!MedievalStockpile.Active(d.Messenger) || d.Messenger.WillDispose || d.Messenger.Owner != d.First) return false;
			var mail = d.Messenger.Trait<MedievalMessenger>();
			return mail.Revision == d.MailRevision && mail.Journey is (MessengerJourney.Outbound or MessengerJourney.Waiting)
				&& (d.State == BarterState.Invited || mail.CanReceive(d.Messenger, d.Second));
		}
		void ReturnMessenger(BarterDeal d)
		{
			if (!MedievalStockpile.Active(d.Messenger) || d.Messenger.Owner != d.First) return;
			var mail = d.Messenger.Trait<MedievalMessenger>();
			if (mail.Revision == d.MailRevision)
				mail.Resolve(d.Messenger, d.First, MedievalMessenger.Command(d.First, d.Messenger, "MedievalRecallMessenger"));
		}
		public void Abort(BarterDeal d, string reason)
		{
			if (!d.Active) return;
			var shipping = d.State == BarterState.Delivering;
			d.State = shipping ? BarterState.Interrupted : BarterState.Refused;
			d.Status = reason + (shipping ? " Cargaisons survivantes rapatriées ; les livraisons déjà reçues restent au dépôt." : " Aucune marchandise envoyée.");
			Changed(d, d.First.World);
			foreach (var leg in d.Legs) leg?.Cancel();
			ReturnMessenger(d);
		}
		public void Resolve(Player p, Order order)
		{
			var w = p.World;
			if (p.NonCombatant || p.WinState == WinState.Lost) return;
			if (order.OrderString == "MedievalInviteBarter")
			{
				if (order.Target.Type != TargetType.Actor || order.ExtraActors is not { Length: 1 }) return;
				var courier = order.Target.Actor; var target = order.ExtraActors[0];
				if (!CanInvite(courier, p) || !MedievalMessenger.Barracks(target) || !Depots(target.Owner).Any()
					|| deals.Count(d => d.Active && d.Side(target.Owner) >= 0) >= 8) return;
				var mail = courier.Trait<MedievalMessenger>();
				if (!mail.CanTarget(courier, p, target) || mail.Revision != order.ExtraData) return;
				mail.Resolve(courier, p, MedievalMessenger.Command(p, courier, "MedievalSendLetter", "Invitation au marchandage : souhaitez-vous négocier un échange de marchandises ?", target));
				if (mail.Journey != MessengerJourney.Outbound) return;
				var d = new BarterDeal { Id = nextId++, Messenger = courier, First = p, Second = target.Owner,
					MailRevision = mail.Revision, ChangedAt = w.WorldTick };
				d.Depots[0] = Depots(p).OrderBy(a => (a.CenterPosition - mail.Origin.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).First();
				d.Depots[1] = Depots(target.Owner).OrderBy(a => (a.CenterPosition - target.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).First();
				deals.Add(d); return;
			}
			var deal = deals.FirstOrDefault(d => d.Id == order.ExtraData);
			var fields = (order.TargetString ?? "").Split(';', 2);
			if (deal == null || deal.Side(p) < 0 || !Visible(deal, p) || fields.Length != 2
				|| !uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision != deal.Revision) return;
			if (deal.Negotiable && !MessengerPresent(deal)) { Abort(deal, "Le messager n'est plus disponible pour négocier."); return; }
			if (order.OrderString == "MedievalBarterRefuse" && deal.Negotiable) { Abort(deal, "Marchandage refusé ou annulé."); return; }
			if (order.OrderString == "MedievalBarterOpen" && deal.State == BarterState.Invited && p == deal.Second
				&& deal.Messenger.Trait<MedievalMessenger>().CanReceive(deal.Messenger, p))
			{
				deal.Messenger.Trait<MedievalMessenger>().Resolve(deal.Messenger, p, MedievalMessenger.Command(p, deal.Messenger, "MedievalReadLetter"));
				deal.State = BarterState.Negotiating; deal.Status = "Invitation acceptée. Proposez les quantités à échanger."; Changed(deal, w); return;
			}
			if (deal.State is not (BarterState.Negotiating or BarterState.Offered)) return;
			if (order.OrderString == "MedievalBarterDepot" && order.ExtraActors is { Length: 1 } && Depot(order.ExtraActors[0], p))
			{
				deal.Depots[deal.Side(p)] = order.ExtraActors[0]; deal.Proposer = null; deal.State = BarterState.Negotiating;
				deal.Status = "Dépôt modifié. Une nouvelle proposition est nécessaire."; Changed(deal, w);
			}
			else if (order.OrderString == "MedievalBarterCarts" && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
				&& count is >= 1 and <= MaximumCarts)
			{
				deal.CartCounts[deal.Side(p)] = count; deal.Proposer = null; deal.State = BarterState.Negotiating;
				deal.Status = "Nombre de charrettes modifié. Confirmez les conditions avec une nouvelle proposition."; Changed(deal, w);
			}
			else if (order.OrderString == "MedievalBarterPropose" && BarterOffer.TryParse(fields[1], out var give, out var receive))
			{
				deal.Goods[deal.Side(p)] = give; deal.Goods[1 - deal.Side(p)] = receive;
				deal.Proposer = p; deal.State = BarterState.Offered; deal.Status = p.ResolvedPlayerName + " propose cet échange. Acceptez, refusez ou faites une contre-offre."; Changed(deal, w);
			}
			else if (order.OrderString == "MedievalBarterAccept" && deal.State == BarterState.Offered && deal.Proposer != p)
			{
				if (!Depot(deal.Depots[0], deal.First) || !Depot(deal.Depots[1], deal.Second))
				{ deal.Status = "Un dépôt n'est plus disponible. Choisissez un dépôt terminé."; return; }
				var carts = Enumerable.Range(0, 2).Select(i => AvailableCarts(deal.Player(i))
					.OrderBy(a => (a.CenterPosition - deal.Depots[i].CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).Take(deal.CartCounts[i]).ToArray()).ToArray();
				if (Enumerable.Range(0, 2).Any(i => carts[i].Length < deal.CartCounts[i]))
				{ deal.Status = "Pas assez de charrettes commerciales libres. Attendez leur retour, videz-les ou réduisez l'allocation."; return; }
				var first = new BarterCargo(Stocks(deal.Depots[0]), Stocks(deal.Depots[1]), deal.Goods[0]);
				var second = new BarterCargo(Stocks(deal.Depots[1]), Stocks(deal.Depots[0]), deal.Goods[1]);
				if (!BarterCargo.ReserveExchange(first, second))
				{ deal.Status = "Accord impossible : stocks disponibles ou place à l'arrivée insuffisants. Modifiez l'offre ou libérez les dépôts."; return; }
				deal.Legs[0] = new BarterLeg(deal, 0, first, carts[0]); deal.Legs[1] = new BarterLeg(deal, 1, second, carts[1]);
				deal.State = BarterState.Delivering; deal.Status = "Accord conclu. Les deux convois préparent leurs chargements."; Changed(deal, w); ReturnMessenger(deal);
			}
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (w.WorldTick % 10 != 0) return;
			foreach (var d in deals)
			{
				if (d.Negotiable)
				{
					if (!MessengerPresent(d)) Abort(d, "Le messager a quitté la négociation ou a été perdu.");
					else if (w.WorldTick - d.ChangedAt > 15000) Abort(d, "L'invitation ou la proposition a expiré.");
					else if (d.State == BarterState.Invited && d.Messenger.Trait<MedievalMessenger>().CanReceive(d.Messenger, d.Second))
						d.Status = "Le messager est arrivé. Le destinataire peut accepter ou refuser le marchandage.";
				}
				if (d.State == BarterState.Delivering && d.Legs.Any(l => !Depot(l.Source, l.Owner) || !Depot(l.Destination, d.Player(1 - l.Side))
					|| l.Journeys.Any(j => !j.Done && (!MedievalStockpile.Active(j.Cart) || j.Cart.Owner != l.Owner))))
					Abort(d, "Livraison interrompue : dépôt ou charrette perdu.");
				foreach (var leg in d.Legs) leg?.Tick(w);
				if (d.State == BarterState.Delivering)
					d.Status = string.Join("\n", d.Legs.Select((l, i) => $"{d.Player(i).ResolvedPlayerName} : {l.Status} ({l.Cargo.Delivered}/{d.Goods[i].Sum()} livrés)"));
				if (d.State == BarterState.Delivering && d.Legs.All(l => l.Cargo.Complete))
				{ d.State = BarterState.Completed; d.Status = "Échange livré dans les deux dépôts. Les charrettes rentrent."; Changed(d, w); }
			}
			// Retain recent receipts without allowing an unlimited history to burden the simulation.
			foreach (var old in deals.Where(d => !d.Active && !d.Returning).Reverse().Skip(32).ToArray()) deals.Remove(old);
		}
	}

	public class MedievalBarterOrdersInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalBarterOrders();
	}
	public sealed class MedievalBarterOrders : IResolveOrder, INotifyPlayerDisconnected
	{
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.Subject == self && (order.OrderString == "MedievalInviteBarter" || order.OrderString.StartsWith("MedievalBarter", StringComparison.Ordinal)))
				self.World.WorldActor.Trait<MedievalBarter>().Resolve(self.Owner, order);
		}
		void INotifyPlayerDisconnected.PlayerDisconnected(Actor self, Player player)
		{
			var barter = self.World.WorldActor.Trait<MedievalBarter>();
			foreach (var d in barter.Deals.Where(d => d.Active && d.Side(player) >= 0)) barter.Abort(d, "Un partenaire s'est déconnecté.");
		}
	}

	public enum BarterTravel { Loading, Outbound, Returning, Recovering, Done }
	public sealed class BarterLeg
	{
		public readonly int Side;
		public readonly Player Owner;
		public readonly Actor Source, Destination;
		public readonly BarterCargo Cargo;
		public readonly BarterJourney[] Journeys;
		public Actor Cart => Journeys[0].Cart;
		public BarterTravel Travel => Journeys[0].Travel;
		public bool Done => Journeys.All(j => j.Done);
		public string Status => $"{Journeys.Count(j => !j.Done)}/{Journeys.Length} charrette(s) en mission, {Journeys.Count(j => j.Travel == BarterTravel.Outbound)} en livraison";
		public int Hash => Journeys.Select(j => j.Hash).Aggregate(Cargo.Hash, (h, n) => unchecked(h * 31 + n));
		public BarterLeg(BarterDeal deal, int side, BarterCargo cargo, Actor[] carts)
		{
			Side = side; Owner = deal.Player(side); Source = deal.Depots[side]; Destination = deal.Depots[1 - side]; Cargo = cargo;
			Journeys = carts.Select(a => new BarterJourney(deal, this, a)).ToArray();
		}
		public void Cancel() { Cargo.Cancel(); foreach (var j in Journeys.Where(j => !j.Done)) j.Cancel(); }
		public void Tick(World w) { foreach (var j in Journeys) j.Tick(w); }
	}
	public sealed class BarterJourney
	{
		readonly BarterDeal deal;
		public readonly BarterLeg Leg;
		public readonly Actor Cart;
		readonly bool resumeRoute;
		public BarterCargo Cargo => Leg.Cargo;
		public Actor Source => Leg.Source;
		public Actor Destination => Leg.Destination;
		public Player Owner => Leg.Owner;
		Actor recoveryDepot;
		int attempts;
		public int Loads { get; private set; }
		public BarterTravel Travel { get; private set; }
		public bool Done => Travel == BarterTravel.Done;
		public BarterJourney(BarterDeal deal, BarterLeg leg, Actor cart)
		{
			this.deal = deal; Leg = leg; Cart = cart;
			resumeRoute = cart.Trait<MedievalTradeCart>().Lend(cart);
			cart.Trait<MedievalBarterCart>().Journey = this;
		}
		public int Hash => unchecked((((((int)Travel * 31 + (int)Cart.ActorID) * 31 + attempts) * 31 + (int)(recoveryDepot?.ActorID ?? 0)) * 2 + (resumeRoute ? 1 : 0)) * 31 + Loads);
		public void Abort() => Cart.World.WorldActor.Trait<MedievalBarter>().Abort(deal, "Mission interrompue par un nouvel ordre à une charrette.");
		public string Status => Travel switch
		{
			BarterTravel.Loading => "Rejoint le dépôt pour charger", BarterTravel.Outbound => "Livraison chez le partenaire",
			BarterTravel.Returning => Cargo.Waiting > 0 ? "Retour pour le prochain chargement" : "Retour au dépôt d'origine",
			BarterTravel.Recovering => "Échange interrompu : rapatriement de la cargaison", _ => "Trajet terminé"
		};
		public void Cancel()
		{
			Cargo.Cancel(); Travel = BarterTravel.Recovering; attempts = 0;
			if (MedievalStockpile.Active(Cart)) Cart.CancelActivity();
		}
		void Finish(World w)
		{
			Travel = BarterTravel.Done;
			if (MedievalStockpile.Active(Cart))
			{
				Cart.Trait<MedievalBarterCart>().Journey = null;
				Cart.Trait<MedievalTradeCart>().ReturnFromLoan(Cart, resumeRoute && Cart.Owner == Owner);
			}
		}
		public void Tick(World w)
		{
			if (Done) return;
			if (!MedievalStockpile.Active(Cart)) { Travel = BarterTravel.Done; return; }
			if (deal.State == BarterState.Completed && Travel != BarterTravel.Recovering && (!MedievalBarter.Depot(Source, Cart.Owner) || Cart.Owner != Owner)) Cancel();
			if (!Cart.IsIdle) return;
			var stocks = MedievalBarter.Stocks(Cart);
			if (Travel == BarterTravel.Recovering)
			{
				var next = MedievalBarter.Depots(Cart.Owner).OrderBy(a => (a.CenterPosition - Cart.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID)
					.FirstOrDefault(a => Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Amount(Cart, g) == 0)
						|| Enumerable.Range(0, StorageGoods.Count).Any(g => StorageGoods.Amount(Cart, g) > 0 && (StorageGoods.Stock(a, g)?.Free(StorageGoods.Index(g)) ?? 0) > 0));
				if (next != recoveryDepot) { recoveryDepot = next; attempts = 0; }
				if (next == null || !MedievalLogistics.Approach(Cart, next, ref attempts)) return;
				for (var g = 0; g < StorageGoods.Count; g++)
				{
					var r = StorageGoods.Index(g); var target = StorageGoods.Stock(next, g);
					var n = Math.Min(stocks[g].Available(r), target?.Free(r) ?? 0);
					if (n > 0) target.Add(r, stocks[g].Take(r, n));
				}
				if (Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Amount(Cart, g) == 0)) Finish(w);
				return;
			}
			var destination = Travel == BarterTravel.Outbound ? Destination : Source;
			if (!MedievalLogistics.Approach(Cart, destination, ref attempts)) return;
			if (Travel == BarterTravel.Outbound)
			{
				Cargo.Unload(stocks);
				if (Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Amount(Cart, g) == 0)) { Travel = BarterTravel.Returning; attempts = 0; }
			}
			else if (Cargo.Waiting == 0) Finish(w);
			else if (Cargo.Load(stocks) > 0) { Loads++; Travel = BarterTravel.Outbound; attempts = 0; }
		}
	}
	public class MedievalBarterCartInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalBarterCart();
	}
	public sealed class MedievalBarterCart : IResolveOrder
	{
		public BarterJourney Journey;
		public bool Busy => Journey != null && !Journey.Done;
		public string Status => Journey?.Status ?? "Disponible pour le marchandage si vide";
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (Busy && order.OrderString is "Stop" or "Move" or "MedievalAssignTrader" or "MedievalResumeTrade" or "MedievalPauseTrade" or "MedievalRecoverTrade") Journey.Abort();
		}
	}
}
