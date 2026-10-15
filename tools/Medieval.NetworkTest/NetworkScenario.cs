using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.LoadScreens;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Medieval;
using OpenRA.Network;
using OpenRA.Traits;

namespace OpenRA.Platforms.NetworkTest
{
	// Loaded only by the private audit manifest. Every client has its own process,
	// RNG, local player, camera and network connection. No shared simulation state.
	public sealed class NetworkAuditLoadScreen : BlankLoadScreen
	{
		public static OrderManager Orders;
		public override void StartGame(Arguments args)
		{
			var om = Game.JoinServer(new LaunchArguments(args).GetConnectEndPoint(), "", false);
			Orders = om;
			void Ready()
			{
				if (om.GameStarted) return;
				if (om.LocalClient != null && om.LocalClient.State != Session.ClientState.Ready)
					om.IssueOrder(Order.Command("state Ready"));
			}
			Game.LobbyInfoChanged += Ready;
			Game.BeforeGameStart += () => Game.LobbyInfoChanged -= Ready;
		}
	}
	public sealed class NetworkAuditInfo : TraitInfo
	{
		public readonly bool StormRefuge = false;
		public override object Create(ActorInitializer init) => new NetworkAudit(StormRefuge);
	}
	public sealed class NetworkAudit : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<object> checkpoints = [];
		readonly List<Actor> fighters = [];
		readonly List<MedievalCombatProbe> combat = [];
		Player[] players;
		bool[] harvested, delivered;
		CPos[] origins;
		WorldRenderer renderer;
		int tick, stage, recruited;
		bool finished, identitySent;
		readonly bool stormRefuge;
		int maxSheltered;
		public NetworkAudit(bool stormRefuge) { this.stormRefuge = stormRefuge; }
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) { errors.Add(message); Console.WriteLine(message); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			players = w.Players.Where(p => p.PlayerReference.Playable).OrderBy(p => p.InternalName).ToArray();
			harvested = new bool[players.Length]; delivered = new bool[players.Length];
			origins = players.Select(p => w.Actors.Single(a => a.Owner == p && a.Info.Name == "md.caravan").Trait<MedievalFoundingCaravan>()
				.Foundation(w.Actors.Single(a => a.Owner == p && a.Info.Name == "md.caravan"))).ToArray();
			Check(w.LobbyInfo.NonBotPlayers.Count() == players.Length && players.Length >= 2, "Missing real human network clients");
			Console.WriteLine($"NETWORK WORLD: {players.Length} players, local {w.LocalPlayer.InternalName}");
		}
		void Place(World w, string type, CPos pos)
		{
			var places = Enumerable.Range(-4,9).SelectMany(x => Enumerable.Range(-4,9).Select(y => pos + new CVec(x,y)))
				.OrderBy(c => (c-pos).LengthSquared).Where(c => MedievalSettlement.CanPlace(w,w.LocalPlayer,"md."+type,c));
			if (!places.Any()) { Check(false, "No legal placement for " + type); return; }
			w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor, Target.FromCell(w, places.First()), false) { TargetString = "md." + type });
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (finished || w.LocalPlayer == null) return;
			tick++;
			// Food flow totals cover only the latest minute. A winter or storm pause at
			// the final checkpoint must not erase production observed earlier in the game.
			for (var p = 0; p < players.Length; p++)
			{
				var food = players[p].PlayerActor.Trait<MedievalFoodEconomy>();
				harvested[p] |= food.Harvests.Total > 0;
				delivered[p] |= food.Deliveries.Total > 0;
			}
			if (tick % 1000 == 0) Console.WriteLine($"NETWORK TICK {tick}");
			var local = w.LocalPlayer;
			var index = Array.IndexOf(players, local);
			var origin = origins[index];
			if (tick % 25 == 1) Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(origin + new CVec(tick / 100 % 10, 0))));
			Check(!NetworkAuditLoadScreen.Orders.IsOutOfSync, "Native network sync mismatch");
			if (tick == 10)
				w.IssueOrder(new Order("MedievalFoundMarket", w.Actors.Single(a => a.Owner == local && a.Info.Name == "md.caravan"), false));
			var market = w.Actors.FirstOrDefault(a => a.Owner == local && a.Info.Name == "md.foundingmarket");
			if (market != null && !identitySent)
			{
				identitySent = true;
				w.IssueOrder(new Order("MedievalSetEmpireIdentity", market, false) { TargetString = "Pays " + local.InternalName, ExtraData = (uint)(0x4477AA + index * 0x110000) });
				Place(w, "warehouse", origin + new CVec(4, -1));
			}
			if (stage == 0 && MedievalSettlement.Warehouses(local).Any())
			{
				Place(w, "sawmill", origin + new CVec(-7, -2));
				Place(w, "quarry", origin + new CVec(7, 4));
				Place(w, "farm", origin + new CVec(-4, 7));
				stage = 1;
			}
			if (stage == 1 && tick % 50 == 0 && !local.PlayerActor.Trait<MedievalSettlement>().Recruiting && recruited < 3)
				w.IssueOrder(new Order("MedievalRecruit", local.PlayerActor, false) { TargetString = new[] { "md.lumberjack", "md.stonecutter", "md.farmer" }[recruited++] });
			var farm = w.Actors.FirstOrDefault(a => a.Owner == local && a.Info.Name == "md.farm" && MedievalStockpile.Active(a));
			if (stage == 1 && farm != null && recruited == 3) { Place(w, "field", farm.Location + new CVec(-1, 4)); stage = 2; }
			// Deliberately invalid foreign-owner order must be rejected by the normal
			// network order validator, never by a shortcut in this test.
			if (tick == 1200)
			{
				var foreign = w.Actors.FirstOrDefault(a => a.Owner == players[(index + 1) % players.Length] && a.Info.Name == "md.foundingmarket");
				Check(foreign != null, "Foreign founding market missing");
				if (foreign != null) w.IssueOrder(new Order("MedievalSetEmpireIdentity", foreign, false) { TargetString = "INVALID", ExtraData = 0xFFFFFF });
			}
			// Identical houses on both peers exercise bed reservations with independent
			// cameras and muted audio. The population still comes from real recruitment.
			if (stormRefuge && tick == 1500) w.AddFrameEndTask(_ =>
			{
				for (var p = 0; p < players.Length; p++)
				{
					var pos = origins[p] + new CVec(1, 7);
					var cell = w.Map.FindTilesInAnnulus(pos, 0, 12).Where(c => MedievalSettlement.CanPlace(w, players[p], "md.house", c)).Select(c => (CPos?)c).FirstOrDefault();
					Check(cell != null, "No house placement for refuge fixture");
					if (cell != null) w.CreateActor("md.house", [new OwnerInit(players[p]), new LocationInit(cell.Value)]);
				}
			});
			if (stormRefuge) maxSheltered = Math.Max(maxSheltered, w.ActorsWithTrait<MedievalRest>().Count(p => p.Actor.Owner == local && p.Trait.StormSheltering && p.Trait.Sleeping));
			// Identical fixture creation on all peers, then only the owning peer
			// submits combat orders through TCP. Economy remains the real start.
			if (tick == 6000) w.AddFrameEndTask(_ =>
			{
				var arena = origins[0] + new CVec(0, -12);
				for (var p = 0; p < players.Length; p++)
					for (var i = 0; i < 6; i++)
					{
						var a = w.CreateActor("md.militia", [new OwnerInit(players[p]), new LocationInit(arena + new CVec(p * 5, i))]);
						a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire); fighters.Add(a); combat.Add(a.Trait<MedievalCombatProbe>());
					}
			});
			if (tick >= 6050 && tick % 100 == 50)
				foreach (var fighter in fighters.Where(a => a.Owner == local && a.IsInWorld && !a.IsDead))
				{
					var target = fighters.FirstOrDefault(a => a.Owner != local && a.IsInWorld && !a.IsDead);
					if (target != null) w.IssueOrder(new Order("Attack", fighter, Target.FromActor(target), false));
				}
			if (tick % 250 == 0)
				checkpoints.Add(new { tick, hash = w.SyncHash(), actors = w.Actors.Count(), players = players.Select(p => new {
					name = p.InternalName, country = MedievalHeraldry.For(p).CountryName,
					population = MedievalSettlement.Population(p), consumed = p.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed,
					harvest = p.PlayerActor.Trait<MedievalFoodEconomy>().Harvests.Total,
					delivered = p.PlayerActor.Trait<MedievalFoodEconomy>().Deliveries.Total }).ToArray() });
			// Leave time for the haulers sheltering with cargo to resume their deliveries.
			if (tick < (stormRefuge ? 12000 : 9000)) return;
			foreach (var p in players)
			{
				Check(MedievalHeraldry.For(p).CountryName == "Pays " + p.InternalName, "Identity order missing or foreign order accepted");
				Check(MedievalSettlement.Warehouses(p).Any(), "Warehouse not constructed: " + p.InternalName);
				var food = p.PlayerActor.Trait<MedievalFoodEconomy>();
				var slot = Array.IndexOf(players, p);
				Check(food.TotalConsumed > 0 && harvested[slot] && delivered[slot], "Missing harvest, delivery or consumption during the game: " + p.InternalName);
			}
			var deaths = fighters.Count(a => a.IsDead || !a.IsInWorld);
			Check(deaths > 0, "Network combat caused no casualties");
			var shots = combat.Sum(c => c.Shots);
			Check(shots > 0, "No actual weapon attacks over the network");
			if (stormRefuge) Check(maxSheltered > 0, "No citizens reached shelter over TCP");
			File.WriteAllText(Path.Combine(Platform.SupportDir, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick,
				local = local.InternalName, players = players.Length, deaths, shots, stage, stormRefuge, maxSheltered, harvested, delivered, checkpoints,
				workers = w.ActorsWithTrait<MedievalWorker>().Select(p => new { type=p.Actor.Info.Name, owner=p.Actor.Owner.InternalName, p.Trait.Status, p.Trait.Cargo }).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("NETWORK AUDIT: " + (errors.Count == 0 ? "PASS" : "FAIL"));
			finished = true;
			// Keep receiving sync packets briefly before closing each client.
			Game.RunAfterDelay(2000, Game.Exit);
		}
	}
}
