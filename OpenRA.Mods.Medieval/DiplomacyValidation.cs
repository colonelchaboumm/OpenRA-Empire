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
	public class MedievalDiplomacyValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDiplomacyValidation();
	}
	public sealed class MedievalDiplomacyValidation : IWorldLoaded, ITick
	{
		sealed class ContextOrders(World w) : UnitOrderGenerator(w)
		{
			public Order Against(Actor self, Actor target)
			{
				var selected = OrderForUnit(self, Target.FromActor(target), target.Location,
					new MouseInput(MouseInputEvent.Down, ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1));
				return selected?.Trait.IssueOrder(self, selected.Order, selected.Target, false);
			}
		}
		readonly List<string> errors = [], checks = [];
		WorldRenderer renderer;
		Player first, second, third;
		Actor barracks, otherBarracks, messenger, fighter, victim, secret, citizen, depot, cart, foodCart;
		Actor commerceCourier, idleCourier, departingCourier, ownDepot, departingDepot, ownCart, departingCart;
		MedievalBarter barter;
		BarterDeal trade;
		Order staleAcceptance;
		MedievalDiplomacy diplomacy;
		DiplomaticRequest request;
		int tick, stage, until, good, beforeShots;
		bool done, defeatChecked;
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string text) { if (!checks.Contains(text)) checks.Add(text); if (!ok && !errors.Contains(text)) { errors.Add(text); Console.WriteLine("DIPLOMACY FAIL: " + text); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/diplomacy/" + (Game.Renderer.Resolution.Width <= 1024 ? "small" : "native")); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, Player p, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(p), new LocationInit(Cell(x, y))]);
		void Resolve(World w, Order order)
		{
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = Order.Deserialize(w, reader); received.Subject.ResolveOrder(received);
		}
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{ var b = Ui.Root.Get<ButtonWidget>(id); Check(!b.IsDisabled(), "Enabled UI: " + id); if (!b.IsDisabled()) b.OnClick(); });
		static void Disconnect(World w, Player p)
		{
			// Use the same notifications as World.OnClientDisconnected, without closing the test client.
			foreach (var trait in w.WorldActor.TraitsImplementing<INotifyPlayerDisconnected>()) trait.PlayerDisconnected(w.WorldActor, p);
			foreach (var player in w.Players)
				foreach (var trait in player.PlayerActor.TraitsImplementing<INotifyPlayerDisconnected>()) trait.PlayerDisconnected(player.PlayerActor, p);
		}
		static bool Unreserved(Actor a) => Enumerable.Range(0, StorageGoods.Count).All(g =>
			StorageGoods.Stock(a, g).Reserved(StorageGoods.Index(g)) == 0 && StorageGoods.Stock(a, g).Incoming(StorageGoods.Index(g)) == 0);
		void SetStage(int value, int delay = 0) { stage = value; until = tick + delay; Console.WriteLine("DIPLOMACY STAGE " + stage); }
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			first = w.LocalPlayer; second = w.Players.Single(p => p.InternalName == "Rival"); third = w.Players.Single(p => p.InternalName == "Third");
			diplomacy = w.WorldActor.Trait<MedievalDiplomacy>();
			Check(first.RelationshipWith(second) == PlayerRelationship.Neutral && second.RelationshipWith(first) == PlayerRelationship.Neutral, "Nations start mutually neutral.");
			Check(!first.Shroud.FogEnabled || !first.Shroud.Disabled, "Visibility is tested without disabling the shroud.");
			barracks = Spawn(w, "barracks", first, 18, 20); otherBarracks = Spawn(w, "barracks", second, 30, 20);
			messenger = Spawn(w, "messenger", first, 18, 23); Spawn(w, "messenger", first, 27, 22);
			fighter = Spawn(w, "archer", first, 19, 30); victim = Spawn(w, "archer", second, 23, 30);
			secret = Spawn(w, "house", second, 47, 44); citizen = Spawn(w, "builder", second, 46, 45);
			Spawn(w, "militia", second, 48, 46);
			depot = Spawn(w, "warehouse", second, 34, 27); cart = Spawn(w, "tradecart", first, 20, 26);
			for (var g = 0; g < StorageGoods.Count; g++) StorageGoods.Stock(cart, g).Add(StorageGoods.Index(g), 5);
			second.Shroud.ExploreProjectedCells([w.Map.ProjectedCellCovering(w.Map.CenterOfCell(Cell(55, 48)))]);
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(fighter.CenterPosition));
			SetStage(1, 100);
		}
		void Finish()
		{
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, checks, errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL DIPLOMACY: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(500, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick % 500 == 0) Console.WriteLine($"DIPLOMACY tick={tick} stage={stage} request={request?.State} messenger={messenger?.Trait<MedievalMessenger>().Journey}");
			if (tick > 9000) { Check(false, "Scenario completed before timeout at stage " + stage); Finish(); return; }
			if (stage == 1 && tick >= until)
			{
				Check(fighter.Trait<MedievalCombatProbe>().Shots == 0 && victim.Trait<MedievalCombatProbe>().Shots == 0, "Neutral soldiers never attack automatically.");
				Check(!secret.CanBeViewedByPlayer(first), "Neutral nation does not reveal its distant positions.");
				Button(w, "MD_DIPLOMACY_OPEN"); Button(w, "MD_DIPLOMACY_PLAYER_Rival");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "neutral.png"));
				var attack = new ContextOrders(w).Against(fighter, victim);
				Check(attack?.OrderString == "Attack", "Normal contextual attack can target a neutral soldier.");
				if (attack != null) Resolve(w, attack);
				Check(first.RelationshipWith(second) == PlayerRelationship.Enemy && second.RelationshipWith(first) == PlayerRelationship.Enemy, "Explicit attack declares war for both nations.");
				SetStage(2);
			}
			else if (stage == 2 && fighter.Trait<MedievalCombatProbe>().Shots > 0)
			{
				Resolve(w, new Order("Stop", fighter, false)); Resolve(w, new Order("Stop", victim, false));
				fighter.Trait<AutoTarget>().SetStance(fighter, UnitStance.HoldFire); victim.Trait<AutoTarget>().SetStance(victim, UnitStance.HoldFire);
				Button(w, "MD_DIPLOMACY_PEACE"); SetStage(3);
			}
			else if (stage == 3 && diplomacy.Requests.Count > 0)
			{
				request = diplomacy.Requests.Last(); messenger = request.Messenger;
				Resolve(w, MedievalDiplomacy.Respond(second, request, true));
				Check(request.Pending && first.RelationshipWith(second) == PlayerRelationship.Enemy, "Peace cannot be accepted before physical delivery.");
				SetStage(4);
			}
			else if (stage == 4 && diplomacy.CanRespond(request, second))
			{
				Resolve(w, MedievalDiplomacy.Respond(first, request, true)); Resolve(w, MedievalDiplomacy.Respond(third, request, true));
				Check(request.Pending, "Only the intended recipient can accept.");
				Resolve(w, new Order("ForceAttack", fighter, Target.FromActor(victim), false));
				Resolve(w, MedievalDiplomacy.Respond(second, request, true));
				fighter.Trait<AutoTarget>().SetStance(fighter, UnitStance.Defend);
				Check(first.RelationshipWith(second) == PlayerRelationship.Neutral && second.RelationshipWith(first) == PlayerRelationship.Neutral, "Accepted peace restores mutual neutrality.");
				beforeShots = fighter.Trait<MedievalCombatProbe>().Shots;
				fighter.Trait<Health>().InflictDamage(fighter, victim, new Damage(50), true);
				SetStage(5, 100);
			}
			else if (stage == 5 && tick >= until && messenger.Trait<MedievalMessenger>().CanCompose(messenger, first))
			{
				Check(fighter.Trait<MedievalCombatProbe>().Shots == beforeShots && first.RelationshipWith(second) == PlayerRelationship.Neutral, "Peace cancels forced attacks; old projectiles do not restart war.");
				Button(w, "MD_DIPLOMACY_ALLIANCE"); SetStage(6);
			}
			else if (stage == 6 && diplomacy.Requests.Last() != request) { request = diplomacy.Requests.Last(); messenger = request.Messenger; SetStage(7); }
			else if (stage == 7 && diplomacy.CanRespond(request, second))
			{ Resolve(w, MedievalDiplomacy.Respond(second, request, true)); SetStage(8, 3); }
			else if (stage == 8 && tick >= until)
			{
				Check(first.IsAlliedWith(second) && second.IsAlliedWith(first) && first.RelationshipWith(third) == PlayerRelationship.Neutral, "Alliance affects only the consenting pair.");
				Check(secret.CanBeViewedByPlayer(first) && first.Shroud.IsVisible(secret.Location), "Alliance shares current vision, including stationary buildings.");
				Check(first.Shroud.IsExplored(Cell(55, 48)), "Alliance shares previously explored terrain.");
				Check(MedievalManualTransfer.Maximum(cart, depot, 0, true) == 0, "Allied stock cannot be withdrawn unilaterally.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "alliance.png"));
				w.AddFrameEndTask(_ => { foodCart = Spawn(w, "supplycart", first, 47, 46); foodCart.Trait<MedievalStockpile>().Stock.Add(1, 60); });
				good = 0; SetStage(9);
			}
			else if (stage == 9)
			{
				Resolve(w, new Order("MedievalUnloadResource", cart, Target.FromActor(depot), false) { ExtraData = (uint)good, TargetString = "3" });
				Check(cart.Trait<MedievalManualTransfer>().Busy, "Reserve allied delivery for good " + good); SetStage(10);
			}
			else if (stage == 10 && !cart.Trait<MedievalManualTransfer>().Busy)
			{
				Check(StorageGoods.Amount(cart, good) == 2 && StorageGoods.Amount(depot, good) == 3, "Physical allied delivery conserves good " + good);
				good++; SetStage(good < StorageGoods.Count ? 9 : 11, 80);
			}
			else if (stage == 11 && tick >= until)
			{
				Check(citizen.Trait<MedievalNeedsFood>().Need.InSupply && foodCart.Trait<MedievalStockpile>().Stock.Amount(1) < 60, "Allied citizens consume real food from the supply cart.");
				Check(w.ActorsHavingTrait<MedievalPatrol>().Any(a => a.Owner == second && a.Trait<MedievalNeedsFood>().Need.InSupply), "Allied soldiers are also supplied.");
				Sync.RunUnsynced(w, () => { Ui.Root.Get<ButtonWidget>("MD_DIPLOMACY_CLOSE").OnClick(); w.Selection.Combine(w, [depot], false, false); });
				SetStage(12, 4);
			}
			else if (stage == 12 && tick >= until)
			{
				Check(Ui.Root.Get<MedievalTransferWidget>("MD_TRANSFER_PANEL").Store == depot, "Selecting an allied depot opens its resource transfer panel.");
				Resolve(w, new Order("Move", cart, Target.FromCell(w, Cell(20, 26)), false)); SetStage(13, 350);
			}
			else if (stage == 13 && tick >= until)
			{
				Resolve(w, new Order("MedievalUnloadResource", cart, Target.FromActor(depot), false) { ExtraData = 0, TargetString = "2" });
				Check(cart.Trait<MedievalManualTransfer>().Busy, "A delivery is pending before breaking the alliance.");
				Resolve(w, new Order("ForceAttack", fighter, Target.FromActor(otherBarracks), false)); Resolve(w, new Order("Stop", fighter, false));
				fighter.Trait<AutoTarget>().SetStance(fighter, UnitStance.HoldFire); SetStage(14, 15);
			}
			else if (stage == 14 && tick >= until)
			{
				Check(first.RelationshipWith(second) == PlayerRelationship.Enemy && !first.IsAlliedWith(second), "Attacking an allied building breaks the alliance and declares war.");
				Check(!cart.Trait<MedievalManualTransfer>().Busy && StorageGoods.Amount(cart, 0) == 2 && StorageGoods.Amount(depot, 0) == 3
					&& StorageGoods.Stock(depot, 0).Incoming(0) == 0, "Alliance rupture cancels reserved delivery without loss or duplication.");
				Check(!MedievalFoodEconomy.Providers(second).Contains(foodCart), "Former allies immediately lose supply access.");
				foodCart.Dispose(); SetStage(15, 3);
			}
			else if (stage == 15 && tick >= until && messenger.Trait<MedievalMessenger>().CanCompose(messenger, first))
			{
				Check(!first.Shroud.IsVisible(secret.Location) && !secret.CanBeViewedByPlayer(first), "Live shared vision ends after alliance rupture.");
				Resolve(w, MedievalDiplomacy.Request(first, messenger, otherBarracks, false)); request = diplomacy.Requests.Last();
				messenger.Kill(victim); SetStage(16, 20);
			}
			else if (stage == 16 && tick >= until)
			{
				Resolve(w, MedievalDiplomacy.Respond(second, request, true));
				Check(request.State == DiplomaticRequestState.Cancelled && first.RelationshipWith(second) == PlayerRelationship.Enemy, "Dead messengers cancel requests; replayed acceptance cannot change relations.");
				w.AddFrameEndTask(_ => { messenger = Spawn(w, "messenger", first, 18, 23); Resolve(w, MedievalDiplomacy.Request(first, messenger, otherBarracks, false)); request = diplomacy.Requests.Last(); });
				SetStage(17);
			}
			else if (stage == 17 && diplomacy.CanRespond(request, second))
			{
				var stale = MedievalDiplomacy.Respond(second, request, true);
				Resolve(w, MedievalDiplomacy.Respond(second, request, false)); Resolve(w, stale);
				Check(request.State == DiplomaticRequestState.Refused && first.RelationshipWith(second) == PlayerRelationship.Enemy, "Refusing peace preserves the relationship and invalidates stale acceptance.");
				SetStage(18);
			}
			else if (stage == 18 && messenger.Trait<MedievalMessenger>().CanCompose(messenger, first))
			{
				Resolve(w, MedievalDiplomacy.Request(first, messenger, otherBarracks, false)); request = diplomacy.Requests.Last();
				w.AddFrameEndTask(_ => otherBarracks.ChangeOwner(third)); SetStage(19, 30);
			}
			else if (stage == 19 && tick >= until)
			{
				Resolve(w, MedievalDiplomacy.Respond(second, request, true)); Resolve(w, MedievalDiplomacy.Respond(third, request, true));
				Check(request.State == DiplomaticRequestState.Cancelled && first.RelationshipWith(second) == PlayerRelationship.Enemy, "Capture of the destination barracks cancels the letter without a treaty.");
				w.AddFrameEndTask(_ =>
				{
					var saboteur = Spawn(w, "saboteur", first, 27, 23);
					Check(first.RelationshipWith(third) == PlayerRelationship.Neutral, "Sabotage target begins neutral.");
					Check(saboteur.Trait<MedievalSaboteur>().Start(saboteur, otherBarracks, false, otherBarracks.Location), "Explicit sabotage can target a neutral nation's building.");
					Check(first.RelationshipWith(third) == PlayerRelationship.Enemy && third.RelationshipWith(first) == PlayerRelationship.Enemy, "Ordering sabotage declares war for both nations.");
					saboteur.Trait<MedievalSaboteur>().Cancel(saboteur);
					barter = w.WorldActor.Trait<MedievalBarter>();
					ownDepot = Spawn(w, "warehouse", first, 14, 35); departingDepot = Spawn(w, "warehouse", third, 42, 35);
					ownDepot.Trait<MedievalStockpile>().Stock.Add(0, 90); departingDepot.Trait<MedievalStockpile>().Stock.Add(4, 90);
					ownCart = Spawn(w, "tradecart", first, 14, 38); departingCart = Spawn(w, "tradecart", third, 42, 38);
					messenger = Spawn(w, "messenger", first, 18, 23); commerceCourier = Spawn(w, "messenger", first, 19, 23);
					idleCourier = Spawn(w, "messenger", first, 20, 23); departingCourier = Spawn(w, "messenger", third, 32, 23);
					Resolve(w, MedievalDiplomacy.Request(first, messenger, otherBarracks, false)); request = diplomacy.Requests.Last();
					Resolve(w, MedievalMessenger.Command(first, commerceCourier, "MedievalInviteBarter", target: otherBarracks)); trade = barter.Deals.Last();
					SetStage(21);
				}); SetStage(20);
			}
			else if (stage == 21 && diplomacy.CanRespond(request, third) && commerceCourier.Trait<MedievalMessenger>().CanReceive(commerceCourier, third))
			{
				Resolve(w, MedievalBarter.Command(third, trade, "MedievalBarterOpen"));
				var offer = new int[StorageGoods.Count]; var payment = new int[StorageGoods.Count]; offer[0] = payment[4] = 80;
				Resolve(w, MedievalBarter.Command(first, trade, "MedievalBarterPropose", BarterOffer.Encode(offer, payment)));
				if (!defeatChecked)
				{
					// Exercise responses in the same frame as defeat, before periodic cleanup.
					first.WinState = WinState.Lost;
					Resolve(w, MedievalDiplomacy.Respond(third, request, true));
					Resolve(w, MedievalBarter.Command(third, trade, "MedievalBarterAccept"));
					Check(request.State == DiplomaticRequestState.Delivered && first.RelationshipWith(third) == PlayerRelationship.Enemy, "Peace cannot be accepted for a defeated sender before periodic cleanup.");
					Check(trade.State == BarterState.Refused && trade.Legs.All(l => l == null) && Unreserved(ownDepot) && Unreserved(departingDepot), "An offer from an eliminated player cannot dispatch carts or reserve goods.");
					first.WinState = WinState.Undefined; defeatChecked = true; SetStage(24); return;
				}
				Resolve(w, MedievalBarter.Command(third, trade, "MedievalBarterAccept"));
				Check(trade.State == BarterState.Delivering, "Agreed wartime trade can coexist with a pending peace request.");
				staleAcceptance = MedievalDiplomacy.Respond(third, request, true); SetStage(22);
			}
			else if (stage == 22 && trade.Legs.All(l => l.Cargo.Travelling > 0 && l.Cargo.Delivered == 0))
			{
				Disconnect(w, third);
				Check(request.State == DiplomaticRequestState.Cancelled && !diplomacy.CanRespond(request, third), "Disconnect cancels the pending treaty immediately.");
				Resolve(w, staleAcceptance);
				Check(first.RelationshipWith(third) == PlayerRelationship.Enemy, "A disconnected player's queued acceptance cannot establish peace.");
				Check(!diplomacy.CanRequest(second, third, true) && !diplomacy.CanRequest(third, second, true), "Disconnected nations cannot send or receive new diplomatic requests.");
				Check(!barter.CanInvite(departingCourier, third), "A disconnected player cannot initiate new trade.");
				var count = barter.Deals.Count;
				Resolve(w, MedievalMessenger.Command(first, idleCourier, "MedievalInviteBarter", target: otherBarracks));
				Resolve(w, MedievalMessenger.Command(third, departingCourier, "MedievalInviteBarter", target: barracks));
				Check(barter.Deals.Count == count, "New barter invitations to or from disconnected players are rejected.");
				Check(trade.State == BarterState.Interrupted && new[] { ownDepot, departingDepot, ownCart, departingCart }.All(Unreserved), "Disconnect interrupts shipping and frees all stock and capacity reservations.");
				SetStage(23);
			}
			else if (stage == 23 && !trade.Returning)
			{
				Check(StorageGoods.Amount(ownDepot, 0) == 90 && StorageGoods.Amount(departingDepot, 4) == 90
					&& StorageGoods.Amount(ownDepot, 4) == 0 && StorageGoods.Amount(departingDepot, 0) == 0
					&& new[] { ownCart, departingCart }.All(a => Enumerable.Range(0, StorageGoods.Count).All(g => StorageGoods.Amount(a, g) == 0)),
					"Interrupted wartime trade returns both surviving payments physically without loss or duplication.");
				if (!Ui.Root.Get("MD_DIPLOMACY_PANEL").IsVisible()) Button(w, "MD_DIPLOMACY_OPEN");
				Button(w, "MD_DIPLOMACY_PLAYER_Third"); SetStage(25, 5);
			}
			else if (stage == 24 && commerceCourier.Trait<MedievalMessenger>().CanCompose(commerceCourier, first))
			{
				Resolve(w, MedievalMessenger.Command(first, commerceCourier, "MedievalInviteBarter", target: otherBarracks)); trade = barter.Deals.Last(); SetStage(21);
			}
			else if (stage == 25 && tick >= until)
			{
				Check(Ui.Root.Get("MD_DIPLOMACY_PANEL").IsVisible() && Ui.Root.Get<ButtonWidget>("MD_DIPLOMACY_PLAYER_Third").GetText().Contains("déconnecté", StringComparison.Ordinal), "The visible diplomacy panel identifies the departed player.");
				Check(Ui.Root.Get<ButtonWidget>("MD_DIPLOMACY_PEACE").IsDisabled() && Ui.Root.Get<ButtonWidget>("MD_DIPLOMACY_ALLIANCE").IsDisabled(), "The UI disables requests to disconnected nations.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "disconnected.png")); Finish();
			}
		}
	}
}
