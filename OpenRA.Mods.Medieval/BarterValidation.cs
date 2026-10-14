using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalBarterValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalBarterValidation();
	}
	// Real actors, paths, GUI buttons and serialized orders from three player identities.
	public sealed class MedievalBarterValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		MedievalBarter barter;
		Actor home, target, courier, firstDepot, secondDepot, backupDepot;
		Player first, second, third;
		BarterDeal deal;
		Order duplicate;
		int tick, stage, until, completeTrips;
		int[] beforeA, beforeB, interruptionA, interruptionB, lost, recoveredToBackup;
		Inventory[] lostDepotStocks;
		bool finished, transit, partial, earlyBlocked, thirdBlocked, staleBlocked, insufficientBlocked, refusal, interception, depotChanged, depotLost;
		readonly List<string> errors = [];
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		static int[] Goods(int n, int tents) { var a = Enumerable.Repeat(n, StorageGoods.Count).ToArray(); a[9] = tents; return a; }
		static int[] Amounts(Actor a) => Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Amount(a, g)).ToArray();
		static bool NoReservations(Actor a) => Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Stock(a, g).Reserved(StorageGoods.Index(g)) == 0
			&& StorageGoods.Stock(a, g).Incoming(StorageGoods.Index(g)) == 0);
		void Check(bool condition, string error) { if (!condition && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/barter/native"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, Player owner, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(owner), new LocationInit(Cell(x, y))]);
		void Remote(World w, Order order)
		{
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = Order.Deserialize(w, reader);
			Check(received.ExtraData == order.ExtraData && received.TargetString == order.TargetString, "Network round-trip preserves deal, revision and all quantities.");
			((IResolveOrder)received.Subject.Trait<MedievalBarterOrders>()).ResolveOrder(received.Subject, received);
		}
		void Command(World w, Player p, string name, string text = "") => Remote(w, MedievalBarter.Command(p, deal, name, text));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{ var b = Ui.Root.Get<ButtonWidget>(id); Check(!b.IsDisabled(), "Button must be enabled: " + id); if (!b.IsDisabled()) b.OnClick(); });
		void Invite(World w, bool ui = false)
		{
			if (ui)
				Sync.RunUnsynced(w, () =>
				{
					w.Selection.Combine(w, [courier], false, true); Button(w, "MD_MAIL_BARTER");
					var generator = w.OrderGenerator as MedievalMessengerTargetGenerator;
					Check(generator != null, "Trade invitation must use the messenger's targeting tool."); if (generator == null) return;
					var pixel = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(target.CenterPosition));
					var orders = ((IOrderGenerator)generator).Order(w, target.Location, pixel, new MouseInput(MouseInputEvent.Down,
						((OrderGenerator)generator).ActionButton, pixel, int2.Zero, Modifiers.None, 1)).ToArray();
					Check(orders.Length == 1 && orders[0].OrderString == "MedievalInviteBarter", "Targeting must issue an authenticated invitation.");
					foreach (var order in orders) w.IssueOrder(order);
				});
			else Remote(w, MedievalMessenger.Command(first, courier, "MedievalInviteBarter", target: target));
		}
		void Fill(World w, int[] a, int[] b) => Sync.RunUnsynced(w, () =>
		{
			for (var g = 0; g < StorageGoods.Count; g++)
			{ Ui.Root.Get<TextFieldWidget>("MD_BARTER_GIVE_" + g).Text = a[g].ToString(); Ui.Root.Get<TextFieldWidget>("MD_BARTER_RECEIVE_" + g).Text = b[g].ToString(); }
		});
		void Setup(World w)
		{
			first = w.LocalPlayer; second = w.Players.Single(p => p.InternalName == "Rival"); third = w.Players.Single(p => p.InternalName == "Third");
			barter = w.WorldActor.Trait<MedievalBarter>(); first.Shroud.Disabled = true;
			firstDepot = Spawn(w, "warehouse", first, 19, 20); secondDepot = Spawn(w, "warehouse", second, 43, 20);
			home = Spawn(w, "barracks", first, 22, 29); target = Spawn(w, "barracks", second, 43, 29);
			courier = Spawn(w, "messenger", first, 23, 32);
			Spawn(w, "tradecart", first, 18, 24); Spawn(w, "tradecart", first, 17, 24);
			Spawn(w, "tradecart", second, 42, 24);
			foreach (var a in new[] { firstDepot, secondDepot })
				for (var g = 0; g < StorageGoods.Count; g++) StorageGoods.Stock(a, g).Add(StorageGoods.Index(g), g == 9 ? 4 : 30);
			beforeA = Amounts(firstDepot); beforeB = Amounts(secondDepot); stage = 1; until = tick + 20;
		}
		void Finish(World w)
		{
			Check(stage == 19, "All barter scenarios must complete.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, transit, partial,
				earlyBlocked, thirdBlocked, staleBlocked, insufficientBlocked, refusal, interception, depotChanged, depotLost, completeTrips, errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL BARTER: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(500, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick % 500 == 0) Console.WriteLine($"BARTER stage={stage} tick={tick} state={deal?.State} status={deal?.Status} "
				+ string.Join(" | ", deal?.Legs.Select(l => l == null ? "none" : $"{l.Travel}, waiting={l.Cargo.Waiting}, travelling={l.Cargo.Travelling}, cart={l.Cart?.Location}") ?? []));
			if (stage == 1 && tick >= until) { Invite(w, true); stage = 2; }
			else if (stage == 2 && barter.Deals.Count > 0)
			{
				deal = barter.Deals.Last(); Command(w, second, "MedievalBarterOpen");
				earlyBlocked = deal.State == BarterState.Invited; Check(earlyBlocked, "No negotiation before physical messenger arrival."); stage = 3;
			}
			else if (stage == 3 && courier.Trait<MedievalMessenger>().CanReceive(courier, second))
			{
				Command(w, third, "MedievalBarterOpen"); thirdBlocked = deal.State == BarterState.Invited;
				Check(thirdBlocked && !barter.Visible(deal, third), "Third player cannot access or accept another nation's invitation.");
				Command(w, second, "MedievalBarterOpen"); Check(deal.State == BarterState.Negotiating, "Recipient acceptance must open negotiation.");
				stage = 4; until = tick + 20;
			}
			else if (stage == 4 && tick >= until)
			{
				Check(Ui.Root.Get("MD_BARTER_PANEL").IsVisible(), "Acceptance must open sender's exchange window.");
				Fill(w, Goods(8, 2), Goods(4, 1)); Button(w, "MD_BARTER_PROPOSE"); stage = 5;
			}
			else if (stage == 5 && deal.State == BarterState.Offered)
			{
				var revision = deal.Revision; Command(w, third, "MedievalBarterPropose", BarterOffer.Encode(Goods(1, 1), Goods(1, 1)));
				Check(deal.Revision == revision, "Third party cannot change payment terms.");
				Command(w, first, "MedievalBarterAccept"); Check(deal.State == BarterState.Offered, "Proposer cannot accept their own offer.");
				Command(w, second, "MedievalBarterPropose", BarterOffer.Encode(Goods(5, 1), Goods(8, 2)));
				var old = MedievalBarter.Command(first, deal, "MedievalBarterAccept");
				Command(w, second, "MedievalBarterPropose", BarterOffer.Encode(Goods(4, 1), Goods(8, 2))); Remote(w, old);
				staleBlocked = deal.State == BarterState.Offered; Check(staleBlocked, "Stale acceptance cannot approve revised quantities.");
				var gold = secondDepot.Trait<MedievalStockpile>().Stock.Take(4, 999);
				Command(w, first, "MedievalBarterAccept"); insufficientBlocked = deal.State == BarterState.Offered && NoReservations(firstDepot) && NoReservations(secondDepot);
				Check(insufficientBlocked, "Insufficient payment must reserve neither side."); secondDepot.Trait<MedievalStockpile>().Stock.Add(4, gold);
				stage = 6; until = tick + 20;
			}
			else if (stage == 6 && tick >= until)
			{
				Check(Ui.Root.Get<TextFieldWidget>("MD_BARTER_GIVE_7").Text == "8" && Ui.Root.Get<TextFieldWidget>("MD_BARTER_RECEIVE_7").Text == "4", "Counter-offer must update both quantity columns.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "01-negotiation.png"));
				stage = 20; until = tick + 150;
			}
			else if (stage == 20 && tick >= until)
			{
				duplicate = MedievalBarter.Command(first, deal, "MedievalBarterAccept"); Button(w, "MD_BARTER_ACCEPT"); stage = 7;
			}
			else if (stage == 7 && deal.State == BarterState.Delivering)
			{
				Remote(w, duplicate); Check(deal.Legs.All(l => l.Cargo.Delivered == 0), "Acceptance cannot immediately teleport goods.");
				Check(firstDepot.Trait<MedievalEquipmentStore>().Stock.Reserved(2) == 8, "Duplicate acceptance cannot reserve payment twice.");
				Button(w, "MD_BARTER_CLOSE"); stage = 8;
			}
			else if (stage == 8)
			{
				if (!transit && deal.Legs[0].Cargo.Travelling > 0 && deal.Legs[0].Cargo.Delivered == 0 && deal.Legs[0].Cart != null
					&& !MedievalAccess.Adjacent(deal.Legs[0].Cart, firstDepot))
				{
					transit = true; Check(secondDepot.Trait<MedievalStockpile>().Stock.Amount(0) == beforeB[0] - 4, "Destination cannot spend incoming wood while it travels.");
					renderer.Viewport.Center(deal.Legs[0].Cart.CenterPosition); Game.Renderer.SaveScreenshot(Path.Combine(output, "02-caravans.png"));
				}
				if (deal.Legs[0].Cargo.Delivered > 0 && deal.Legs[0].Cargo.Waiting > 0) partial = true;
				if (deal.State == BarterState.Completed && !deal.Returning)
				{
					var a = Goods(8, 2); var b = Goods(4, 1);
					Check(Amounts(firstDepot).SequenceEqual(beforeA.Select((n, g) => n - a[g] + b[g])), "First depot must receive exactly the agreed payment in all eleven commodities.");
					Check(Amounts(secondDepot).SequenceEqual(beforeB.Select((n, g) => n - b[g] + a[g])), "Second depot must receive exactly the agreed goods.");
					Check(NoReservations(firstDepot) && NoReservations(secondDepot), "Completed deliveries release every reservation.");
					Check(transit && partial, "Large offers require actual travel and several loads."); completeTrips = deal.Legs.Sum(l => l.Cargo.Delivered);
					stage = 9;
				}
			}
			else if (stage == 9 && courier.Trait<MedievalMessenger>().CanCompose(courier, first)) { Invite(w); deal = barter.Deals.Last(); stage = 10; }
			else if (stage == 10 && courier.Trait<MedievalMessenger>().CanReceive(courier, second))
			{
				var a = Amounts(firstDepot); var b = Amounts(secondDepot); Command(w, second, "MedievalBarterRefuse");
				refusal = deal.State == BarterState.Refused && Amounts(firstDepot).SequenceEqual(a) && Amounts(secondDepot).SequenceEqual(b);
				Check(refusal && deal.Legs.All(l => l == null), "Refusal must not create carts or move any stock."); stage = 11;
			}
			else if (stage == 11 && courier.Trait<MedievalMessenger>().CanCompose(courier, first)) { Invite(w); deal = barter.Deals.Last(); stage = 12; }
			else if (stage == 12 && courier.Trait<MedievalMessenger>().CanReceive(courier, second))
			{
				interruptionA = Amounts(firstDepot); interruptionB = Amounts(secondDepot);
				Command(w, second, "MedievalBarterOpen"); Command(w, first, "MedievalBarterPropose", BarterOffer.Encode(Goods(4, 1), Goods(4, 1)));
				Command(w, second, "MedievalBarterAccept"); Check(deal.State == BarterState.Delivering, "Second agreement should dispatch another pair of carts."); stage = 13;
			}
			else if (stage == 13 && deal.Legs.All(l => l?.Cargo.Travelling > 0 && l.Cart != null && l.Cargo.Delivered == 0))
			{
				lost = Amounts(deal.Legs[0].Cart); deal.Legs[0].Cart.Kill(deal.Legs[0].Cart); stage = 14;
			}
			else if (stage == 14 && deal.State == BarterState.Interrupted && !deal.Returning)
			{
				interception = true;
				Check(Amounts(firstDepot).SequenceEqual(interruptionA.Select((n, g) => n - lost[g])), "Destroyed cargo must not be duplicated or magically refunded.");
				Check(Amounts(secondDepot).SequenceEqual(interruptionB), "Surviving counter-payment returns physically to its owner's depot.");
				Check(NoReservations(firstDepot) && NoReservations(secondDepot), "Interception releases all pending stock and capacity reservations.");
				stage = 15;
				w.AddFrameEndTask(_ => backupDepot = Spawn(w, "warehouse", second, 43, 39));
			}
			else if (stage == 15 && courier.Trait<MedievalMessenger>().CanCompose(courier, first)) { Invite(w); deal = barter.Deals.Last(); stage = 16; }
			else if (stage == 16 && courier.Trait<MedievalMessenger>().CanReceive(courier, second))
			{
				Command(w, second, "MedievalBarterOpen"); Command(w, first, "MedievalBarterPropose", BarterOffer.Encode(Goods(2, 1), Goods(2, 1)));
				var old = MedievalBarter.Command(second, deal, "MedievalBarterAccept"); var revision = deal.Revision;
				Remote(w, MedievalBarter.Command(first, deal, "MedievalBarterDepot", depot: backupDepot));
				Check(deal.Revision == revision && deal.Depots[0] == firstDepot, "A player cannot choose a foreign depot as their own.");
				Remote(w, MedievalBarter.Command(second, deal, "MedievalBarterDepot", depot: backupDepot)); Remote(w, old);
				depotChanged = deal.State == BarterState.Negotiating && deal.Proposer == null && deal.Depots[1] == backupDepot;
				Check(depotChanged, "Changing an owned depot invalidates prior consent and old acceptances.");
				Remote(w, MedievalBarter.Command(second, deal, "MedievalBarterDepot", depot: secondDepot));
				interruptionA = Amounts(firstDepot); lostDepotStocks = MedievalBarter.Stocks(secondDepot);
				Command(w, first, "MedievalBarterPropose", BarterOffer.Encode(Goods(2, 1), Goods(2, 1))); Command(w, second, "MedievalBarterAccept");
				stage = 17;
			}
			else if (stage == 17 && deal.Legs.All(l => l?.Cargo.Travelling > 0 && l.Cart != null && l.Cargo.Delivered == 0))
			{
				recoveredToBackup = Amounts(deal.Legs[1].Cart); secondDepot.Dispose(); stage = 18;
			}
			else if (stage == 18 && deal.State == BarterState.Interrupted && !deal.Returning)
			{
				depotLost = true;
				Check(Amounts(firstDepot).SequenceEqual(interruptionA), "Lost destination must return the first player's cargo without loss.");
				Check(Amounts(backupDepot).SequenceEqual(recoveredToBackup), "Surviving cargo must find another depot belonging to its owner.");
				Check(NoReservations(firstDepot) && Enumerable.Range(0, StorageGoods.Count).All(g => lostDepotStocks[g].Incoming(StorageGoods.Index(g)) == 0
					&& lostDepotStocks[g].Reserved(StorageGoods.Index(g)) == 0), "Removed depot must leave no dangling reservations.");
				stage = 19; Finish(w);
			}
			if (tick > 16000) { errors.Add("Timeout: stage " + stage + ", " + deal?.Status); Finish(w); }
		}
	}
}
