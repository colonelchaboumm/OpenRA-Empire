using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Medieval;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Platforms.NetworkTest
{
	public sealed class BarterNetworkAuditInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new BarterNetworkAudit();
	}
	public sealed class BarterNetworkAudit : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<object> checkpoints = [];
		Player[] players;
		Actor firstDepot, secondDepot, home, target, courier;
		Actor cart, guard;
		MedievalBarter barter;
		int tick;
		bool opened, configured, proposed, countered, accepted, invitedAgain, refused, finished;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		static int[] Goods(int n, int tents) { var goods = Enumerable.Repeat(n, StorageGoods.Count).ToArray(); goods[9] = tents; return goods; }
		void Check(bool value, string error) { if (!value && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			players = w.Players.Where(p => p.PlayerReference.Playable).OrderBy(p => p.InternalName).ToArray();
			Check(w.LobbyInfo.NonBotPlayers.Count() == 2 && players.Length == 2, "Two independent human network clients required.");
			barter = w.WorldActor.Trait<MedievalBarter>();
		}
		Actor Spawn(World w, string type, Player p, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(p), new LocationInit(Cell(x, y))]);
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			foreach (var p in players) p.Shroud.Disabled = true;
			firstDepot = Spawn(w, "warehouse", players[0], 19, 20); secondDepot = Spawn(w, "warehouse", players[1], 43, 20);
			home = Spawn(w, "barracks", players[0], 22, 29); target = Spawn(w, "barracks", players[1], 43, 29);
			courier = Spawn(w, "messenger", players[0], 23, 32);
			cart = Spawn(w, "tradecart", players[0], 18, 24); Spawn(w, "tradecart", players[0], 17, 24);
			Spawn(w, "tradecart", players[1], 42, 24);
			guard = Spawn(w, "royalguard", players[0], 18, 26); guard.Trait<AutoTarget>().SetStance(guard, UnitStance.HoldFire);
			foreach (var a in new[] { firstDepot, secondDepot })
				for (var g = 0; g < StorageGoods.Count; g++) StorageGoods.Stock(a, g).Add(StorageGoods.Index(g), g == 9 ? 4 : 30);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			Check(!NetworkAuditLoadScreen.Orders.IsOutOfSync, "Native TCP sync mismatch.");
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			var local = w.LocalPlayer;
			if (tick == 20 && local == players[0])
			{
				w.IssueOrder(MedievalMessenger.Command(local, courier, "MedievalInviteBarter", target: target));
				w.IssueOrder(new Order("MedievalEscort", guard, Target.FromActor(cart), false));
			}
			var deal = barter.Deals.FirstOrDefault();
			if (deal != null)
			{
				if (tick == 100 && local == players[1])
					w.IssueOrder(MedievalBarter.Command(players[0], deal, "MedievalBarterRefuse")); // Forged subject, rejected by the server/client order validator.
				if (local == players[1] && deal.State == BarterState.Invited && courier.Trait<MedievalMessenger>().CanReceive(courier, local) && !opened)
				{ opened = true; w.IssueOrder(MedievalBarter.Command(local, deal, "MedievalBarterOpen")); }
				if (local == players[0] && deal.State == BarterState.Negotiating && !configured)
				{ configured = true; w.IssueOrder(MedievalBarter.Command(local, deal, "MedievalBarterCarts", "2")); }
				if (local == players[0] && deal.State == BarterState.Negotiating && deal.CartCounts[0] == 2 && !proposed)
				{ proposed = true; w.IssueOrder(MedievalBarter.Command(local, deal, "MedievalBarterPropose", BarterOffer.Encode(Goods(8, 2), Goods(4, 1)))); }
				if (local == players[1] && deal.State == BarterState.Offered && deal.Proposer == players[0] && !countered)
				{ countered = true; w.IssueOrder(MedievalBarter.Command(local, deal, "MedievalBarterPropose", BarterOffer.Encode(Goods(5, 1), Goods(8, 2)))); }
				if (local == players[0] && deal.State == BarterState.Offered && deal.Proposer == players[1] && !accepted)
				{ accepted = true; w.IssueOrder(MedievalBarter.Command(local, deal, "MedievalBarterAccept")); }
				if (local == players[0] && deal.State == BarterState.Completed && !deal.Returning && courier.Trait<MedievalMessenger>().CanCompose(courier, local) && !invitedAgain)
				{ invitedAgain = true; w.IssueOrder(MedievalMessenger.Command(local, courier, "MedievalInviteBarter", target: target)); }
			}
			if (local == players[1] && barter.Deals.Count == 2 && courier.Trait<MedievalMessenger>().CanReceive(courier, local) && !refused)
			{ refused = true; w.IssueOrder(MedievalBarter.Command(local, barter.Deals[1], "MedievalBarterRefuse")); }
			if (tick % 100 == 0) checkpoints.Add(new { tick, hash = w.SyncHash(), barter = barter.DealsHash, first = Enumerable.Range(0, 11).Select(g => StorageGoods.Amount(firstDepot, g)).ToArray(),
				second = Enumerable.Range(0, 11).Select(g => StorageGoods.Amount(secondDepot, g)).ToArray() });
			if (tick % 500 == 0) Console.WriteLine($"BARTER TCP {local.InternalName} tick={tick}, deal={deal?.State}, count={barter.Deals.Count}");
			if (tick < 5000) return;
			Check(deal?.State == BarterState.Completed && !deal.Returning, "Both delivery legs must finish over TCP.");
			Check(deal?.Legs[0]?.Journeys.Length == 2 && deal.Legs[0].Journeys.All(j => j.Loads == 1), "Two allocated carts deliver in parallel over TCP.");
			Check(guard.Trait<MedievalEscort>().Protected == cart && (guard.CenterPosition - cart.CenterPosition).HorizontalLengthSquared < 4096L * 4096,
				"Network escort follows the same cart until it returns.");
			Check(barter.Deals.Count == 2 && barter.Deals[1].State == BarterState.Refused && barter.Deals[1].Legs.All(l => l == null), "Second invitation refused over TCP without dispatch.");
			var a = Goods(8, 2); var b = Goods(5, 1);
			for (var g = 0; g < StorageGoods.Count; g++)
			{
				var initial = g == 9 ? 4 : 30;
				Check(StorageGoods.Amount(firstDepot, g) == initial - a[g] + b[g], "First payment mismatch: " + g);
				Check(StorageGoods.Amount(secondDepot, g) == initial - b[g] + a[g], "Second payment mismatch: " + g);
				foreach (var depot in new[] { firstDepot, secondDepot }) Check(StorageGoods.Stock(depot, g).Reserved(StorageGoods.Index(g)) == 0
					&& StorageGoods.Stock(depot, g).Incoming(StorageGoods.Index(g)) == 0, "Dangling TCP reservation: " + g);
			}
			File.WriteAllText(Path.Combine(Platform.SupportDir, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick,
				local = local.InternalName, checkpoints, delivered = deal?.Legs.Select(l => l?.Cargo.Delivered ?? 0).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
			finished = true; Game.RunAfterDelay(2000, Game.Exit);
		}
	}
}
