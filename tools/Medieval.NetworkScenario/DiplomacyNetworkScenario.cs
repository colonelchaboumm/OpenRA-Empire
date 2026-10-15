using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Medieval;
using OpenRA.Traits;

namespace OpenRA.Platforms.NetworkTest
{
	public class DiplomacyNetworkAuditInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new DiplomacyNetworkAudit();
	}
	public sealed class DiplomacyNetworkAudit : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<object> checkpoints = [];
		Player[] players;
		Actor courier, target, fighter, victim, depot, cart, foodCart, citizen;
		MedievalDiplomacy diplomacy;
		DiplomaticRequest request;
		int tick, stage;
		bool done;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) { errors.Add(message); Console.WriteLine("DIPLOMACY TCP FAIL: " + message); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			players = w.Players.Where(p => p.PlayerReference.Playable).OrderBy(p => p.InternalName).ToArray();
			Check(players.Length == 2 && w.LobbyInfo.NonBotPlayers.Count() == 2, "Two independent clients connected by TCP.");
		}
		Actor Spawn(World w, string type, int p, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(players[p]), new LocationInit(Cell(x, y))]);
		void Send(World w, Order order) { if (order.Subject.Owner == w.LocalPlayer) w.IssueOrder(order); }
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			diplomacy = w.WorldActor.Trait<MedievalDiplomacy>();
			Spawn(w, "barracks", 0, 18, 20); target = Spawn(w, "barracks", 1, 30, 20);
			courier = Spawn(w, "messenger", 0, 18, 23); Spawn(w, "messenger", 0, 27, 22);
			fighter = Spawn(w, "archer", 0, 19, 30); victim = Spawn(w, "archer", 1, 23, 30);
			depot = Spawn(w, "warehouse", 1, 34, 27); cart = Spawn(w, "tradecart", 0, 20, 26);
			cart.Trait<MedievalStockpile>().Stock.Add(0, 20);
			foodCart = Spawn(w, "supplycart", 0, 46, 45); foodCart.Trait<MedievalStockpile>().Stock.Add(1, 60);
			citizen = Spawn(w, "builder", 1, 46, 46);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			Check(!NetworkAuditLoadScreen.Orders.IsOutOfSync, "No synchronization mismatch.");
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 100)
			{
				Check(players[0].RelationshipWith(players[1]) == PlayerRelationship.Neutral
					&& fighter.Trait<MedievalCombatProbe>().Shots == 0 && victim.Trait<MedievalCombatProbe>().Shots == 0, "Default neutrality prevents automatic attacks on both clients.");
				Check(!MedievalFoodEconomy.Providers(players[1]).Contains(foodCart), "Neutral nations cannot consume each other's supplies.");
				Send(w, new Order("Attack", fighter, Target.FromActor(victim), false)); stage = 1;
			}
			if (stage == 1 && fighter.Trait<MedievalCombatProbe>().Shots > 0)
			{
				Check(players[0].RelationshipWith(players[1]) == PlayerRelationship.Enemy && players[1].RelationshipWith(players[0]) == PlayerRelationship.Enemy, "An attack synchronizes mutual war.");
				foreach (var a in new[] { fighter, victim }) { Send(w, new Order("Stop", a, false)); Send(w, new Order("SetUnitStance", a, false) { ExtraData = (uint)UnitStance.HoldFire }); }
				Send(w, MedievalDiplomacy.Request(players[0], courier, target, false)); stage = 2;
			}
			else if (stage == 2 && diplomacy.Requests.Count > 0) { request = diplomacy.Requests.Last(); stage = 3; }
			else if (stage == 3 && diplomacy.CanRespond(request, players[1])) { Send(w, MedievalDiplomacy.Respond(players[1], request, true)); stage = 4; }
			else if (stage == 4 && request.State == DiplomaticRequestState.Accepted && courier.Trait<MedievalMessenger>().CanCompose(courier, players[0]))
			{
				Check(players[0].RelationshipWith(players[1]) == PlayerRelationship.Neutral, "The recipient accepts peace over TCP after physical delivery.");
				Send(w, MedievalDiplomacy.Request(players[0], courier, target, true)); stage = 5;
			}
			else if (stage == 5 && diplomacy.Requests.Last() != request) { request = diplomacy.Requests.Last(); stage = 6; }
			else if (stage == 6 && diplomacy.CanRespond(request, players[1])) { Send(w, MedievalDiplomacy.Respond(players[1], request, true)); stage = 7; }
			else if (stage == 7 && request.State == DiplomaticRequestState.Accepted)
			{
				Check(players[0].IsAlliedWith(players[1]) && players[1].IsAlliedWith(players[0]), "Accepted alliance is mutual on both clients.");
				Send(w, new Order("MedievalUnloadResource", cart, Target.FromActor(depot), false) { ExtraData = 0, TargetString = "10" }); stage = 8;
			}
			else if (stage == 8 && StorageGoods.Amount(depot, 0) == 10 && courier.Trait<MedievalMessenger>().CanCompose(courier, players[0]))
			{
				Check(StorageGoods.Amount(cart, 0) == 10 && !cart.Trait<MedievalManualTransfer>().Busy, "Allied delivery is physical, exact and synchronized.");
				Check(citizen.Trait<MedievalNeedsFood>().Need.InSupply && foodCart.Trait<MedievalStockpile>().Stock.Amount(1) < 60, "Allied citizen consumes real food from the other player's supply cart.");
				Send(w, new Order("ForceAttack", fighter, Target.FromActor(target), false)); stage = 9;
			}
			else if (stage == 9 && players[0].RelationshipWith(players[1]) == PlayerRelationship.Enemy)
			{
				Send(w, new Order("Stop", fighter, false));
				Send(w, MedievalDiplomacy.Request(players[0], courier, target, false)); stage = 10;
			}
			else if (stage == 10 && diplomacy.Requests.Last() != request) { request = diplomacy.Requests.Last(); stage = 11; }
			else if (stage == 11 && diplomacy.CanRespond(request, players[1])) { Send(w, MedievalDiplomacy.Respond(players[1], request, false)); stage = 12; }
			else if (stage == 12 && request.State == DiplomaticRequestState.Refused)
			{
				Check(players[0].RelationshipWith(players[1]) == PlayerRelationship.Enemy, "Refusal preserves war on both clients.");
				Check(!MedievalFoodEconomy.Providers(players[1]).Contains(foodCart), "Alliance rupture removes supply access on both clients."); stage = 13;
			}
			if (tick % 100 == 0) checkpoints.Add(new { tick, hash = w.SyncHash(), stage, relations = diplomacy.RelationsHash, requests = diplomacy.RequestsHash,
				cart = StorageGoods.Amount(cart, 0), depot = StorageGoods.Amount(depot, 0), food = foodCart.Trait<MedievalStockpile>().Stock.Amount(1) });
			if (tick % 500 == 0) Console.WriteLine($"DIPLOMACY TCP {w.LocalPlayer.InternalName} tick={tick} stage={stage}");
			if (tick < 4000) return;
			Check(stage == 13, "All diplomatic and delivery phases completed.");
			File.WriteAllText(Path.Combine(Platform.SupportDir, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, stage, local = w.LocalPlayer.InternalName, checkpoints }, new JsonSerializerOptions { WriteIndented = true }));
			done = true; Game.RunAfterDelay(2000, Game.Exit);
		}
	}
}
