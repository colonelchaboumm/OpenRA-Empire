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
	public sealed class TransferNetworkAuditInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new TransferNetworkAudit();
	}
	public sealed class TransferNetworkAudit : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<object> checkpoints = [];
		readonly List<Actor> stores = [], carts = [];
		Player[] players;
		int tick;
		bool finished;
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			players = w.Players.Where(p => p.PlayerReference.Playable).OrderBy(p => p.InternalName).ToArray();
			Check(w.LobbyInfo.NonBotPlayers.Count() == 2 && players.Length == 2, "Two independent human clients required.");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			for (var p = 0; p < players.Length; p++)
			{
				players[p].Shroud.Disabled = true;
				Actor Spawn(string type, CPos cell) => w.CreateActor("md." + type, [new OwnerInit(players[p]), new LocationInit(cell)]);
				var origin = new CPos(47, -14 + p * 24);
				var depot = Spawn("warehouse", origin); stores.Add(depot); carts.Add(Spawn("tradecart", origin + new CVec(8, 4)));
				for (var g = 0; g < StorageGoods.Count; g++)
				{
					var stock = StorageGoods.Stock(depot, g); var r = StorageGoods.Index(g); stock.Take(r, int.MaxValue); stock.Add(r, 10);
				}
			}
		}
		void Order(World w, int p, int good, int count, bool load)
		{
			if (players[p] == w.LocalPlayer) w.IssueOrder(new Order(load ? "MedievalLoadResource" : "MedievalUnloadResource", carts[p], Target.FromActor(stores[p]), false)
				{ ExtraData = (uint)good, TargetString = count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			Check(!NetworkAuditLoadScreen.Orders.IsOutOfSync, "Native TCP synchronization mismatch.");
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 30) for (var p = 0; p < players.Length; p++) Order(w, p, 0, 7, true);
			if (tick == 650)
			{
				Check(carts.All(a => StorageGoods.Amount(a, 0) == 7), "Both clients loaded exact quantities after real movement.");
				for (var p = 0; p < players.Length; p++) Order(w, p, 0, 3, false);
			}
			if (tick >= 800 && tick <= 1700 && (tick - 800) % 100 == 0)
				for (var p = 0; p < players.Length; p++) Order(w, p, 1 + (tick - 800) / 100, 2, true);
			if (tick == 1850)
			{
				for (var p = 0; p < players.Length; p++)
				{
					Check(StorageGoods.Amount(carts[p], 0) == 4 && StorageGoods.Amount(stores[p], 0) == 6, "Reverse transfer synchronized.");
					for (var g = 1; g < StorageGoods.Count; g++) Check(StorageGoods.Amount(carts[p], g) == 2, "Commodity synchronized: " + p + "/" + g);
					for (var g = 0; g < StorageGoods.Count; g++) Check(StorageGoods.Amount(carts[p], g) + StorageGoods.Amount(stores[p], g) == 10, "Conservation across inventories: " + p + "/" + g);
				}
			}
			if (tick % 100 == 0) checkpoints.Add(new { tick, hash = w.SyncHash(), stocks = carts.SelectMany(a => Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Amount(a, g))).ToArray() });
			if (tick < 1900) return;
			File.WriteAllText(Path.Combine(Platform.SupportDir, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, local = w.LocalPlayer.InternalName, checkpoints }, new JsonSerializerOptions { WriteIndented = true }));
			finished = true; Game.RunAfterDelay(2000, Game.Exit);
		}
	}
}
