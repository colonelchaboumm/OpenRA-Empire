using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Orders;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalCargoValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCargoValidation();
	}
	// Actual combat, actor removal, shared loot, UI orders and market route changes.
	public sealed class MedievalCargoValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		WorldRenderer renderer;
		Player first, enemy;
		Actor victim, empty, archer, depot, pile, collector, competitor, last, destination, a, b, third, trader;
		int tick, stage, until;
		int[] lost;
		bool finished;
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		static int[] Amounts(Actor actor) => Enumerable.Range(0, StorageGoods.Count).Select(g => StorageGoods.Amount(actor, g)).ToArray();
		static bool Unreserved(Actor actor) => Enumerable.Range(0, StorageGoods.Count).All(g =>
			(StorageGoods.Stock(actor, g)?.Reserved(StorageGoods.Index(g)) ?? 0) == 0 && (StorageGoods.Stock(actor, g)?.Incoming(StorageGoods.Index(g)) ?? 0) == 0);
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/cargo/native"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, Player owner, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(owner), new LocationInit(Cell(x, y))]);
		void Remote(World w, Order order)
		{
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = Order.Deserialize(w, reader); received.Subject.ResolveOrder(received);
		}
		void Command(World w, Actor actor, string command, Actor target = null) => Remote(w, target == null ? new Order(command, actor, false) : new Order(command, actor, Target.FromActor(target), false));
		void Setup(World w)
		{
			foreach (var actor in w.Actors.Where(actor => actor.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) actor.Dispose();
			first = w.LocalPlayer; enemy = w.Players.Single(p => p.InternalName == "Rival"); first.Shroud.Disabled = enemy.Shroud.Disabled = true;
			victim = Spawn(w, "tradecart", first, 19, 25); empty = Spawn(w, "tradecart", first, 18, 25);
			depot = Spawn(w, "warehouse", first, 43, 20);
			collector = Spawn(w, "tradecart", enemy, 28, 26); competitor = Spawn(w, "tradecart", first, 28, 30); last = Spawn(w, "tradecart", first, 29, 30);
			destination = Spawn(w, "market", enemy, 43, 39);
			a = Spawn(w, "market", first, 19, 39); b = Spawn(w, "market", first, 30, 39); third = Spawn(w, "market", first, 30, 20);
			trader = Spawn(w, "tradecart", first, 30, 24);
			foreach (var actor in new[] { depot, destination, a, b, third }) for (var g = 0; g < StorageGoods.Count; g++) StorageGoods.Stock(actor, g)?.Take(StorageGoods.Index(g), int.MaxValue);
			victim.Trait<MedievalStockpile>().Stock.SetCapacity([120, 60, 60, 60, 60]);
			for (var g = 0; g < StorageGoods.Count; g++) StorageGoods.Stock(victim, g).Add(StorageGoods.Index(g), g == 0 ? 95 : g == 9 ? 2 : 4);
			lost = Amounts(victim); collector.Trait<MedievalStockpile>().Stock.Add(0, 45); depot.Trait<MedievalStockpile>().Stock.Add(0, 20);
			var hp = victim.Trait<Health>(); hp.InflictDamage(victim, victim, new Damage(hp.HP - 1), true);
			stage = 1;
		}
		void Finish(World w)
		{
			Check(stage == 11, "Every cargo scenario must finish.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL CARGO: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(500, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick % 500 == 0) Console.WriteLine($"CARGO tick={tick} stage={stage} collector={collector?.Trait<MedievalManualTransfer>().Status}");
			if (stage == 1 && tick == 20)
			{
				Remote(w, new Order("MedievalUnloadResource", victim, Target.FromActor(depot), false) { ExtraData = 0, TargetString = "7" });
				Remote(w, new Order("MedievalLoadResource", empty, Target.FromActor(depot), false) { ExtraData = 0, TargetString = "17" });
				Check(depot.Trait<MedievalStockpile>().Stock.Incoming(0) == 7 && depot.Trait<MedievalStockpile>().Stock.Reserved(0) == 17, "Both manual reservations exist before death.");
				empty.Kill(victim);
				w.AddFrameEndTask(_ => { archer = Spawn(w, "archer", enemy, 21, 25); Command(w, archer, "Attack", victim); }); stage = 2;
			}
			else if (stage == 2 && victim.IsDead && w.ActorsHavingTrait<MedievalDroppedCargo>().Any())
			{
				archer.Dispose(); pile = w.ActorsHavingTrait<MedievalDroppedCargo>().Single();
				Check(Amounts(pile).SequenceEqual(lost), "Combat death drops every physical good exactly once, including reserved departures.");
				Check(Unreserved(depot) && StorageGoods.Amount(depot, 0) == 20, "Death releases bookings without refund or incoming ghost cargo.");
				Check(pile.Owner != first && pile.Owner != enemy, "Dropped goods belong to neither nation.");
				foreach (var carrier in new[] { collector, competitor })
				{
					var targeter = ((IIssueOrder)carrier.Trait<MedievalManualTransfer>()).Orders.Single();
					var modifiers = TargetModifiers.None; string cursor = null;
					Check(targeter.CanTarget(carrier, Target.FromActor(pile), ref modifiers, ref cursor), "Right-click recovery accepts either nation.");
					modifiers = TargetModifiers.ForceMove;
					Check(!targeter.CanTarget(carrier, Target.FromActor(pile), ref modifiers, ref cursor), "Force-move must not accidentally collect cargo.");
				}
				first.Shroud.Disabled = false; stage = 20; until = tick + 10;
			}
			else if (stage == 20 && tick >= until)
			{
				Check(first.Shroud.FogEnabled && !MedievalDroppedCargo.Visible(pile, first), "A cargo pile outside the nation's vision is hidden by fog.");
				Command(w, competitor, "MedievalCollectCargo", pile);
				Check(!competitor.Trait<MedievalManualTransfer>().Busy, "Forged recovery of hidden cargo must be rejected.");
				first.Shroud.Disabled = true;
				Command(w, collector, "MedievalCollectCargo", pile);
				Check(collector.Trait<MedievalManualTransfer>().Collecting && StorageGoods.Amount(collector, 0) == 45 && Unreserved(pile), "Enemy collection requires physical travel and cannot reserve loot remotely.");
				stage = 3;
			}
			else if (stage == 3 && !collector.Trait<MedievalManualTransfer>().Busy)
			{
				Check(StorageGoods.Amount(collector, 0) == 60 && StorageGoods.Amount(pile, 0) == 80, "A partially full enemy cart takes only its remaining capacity.");
				Check(Enumerable.Range(1, 10).All(g => StorageGoods.Amount(collector, g) == lost[g]), "All equipment, food, gold and camp goods can be recovered.");
				Sync.RunUnsynced(w, () => { w.Selection.Combine(w, [competitor], false, true); renderer.Viewport.Center(pile.CenterPosition); });
				stage = 4; until = tick + 20;
			}
			else if (stage == 4 && tick >= until)
			{
				Sync.RunUnsynced(w, () =>
				{
					var button = Ui.Root.Get<ButtonWidget>("MD_TRANSFER_COLLECT");
					Check(button.IsVisible() && !button.IsDisabled(), "Cart menu offers ground recovery."); button.OnClick();
					var generator = w.OrderGenerator as MedievalLogisticsTargetGenerator; Check(generator != null, "Ground button opens targeting.");
					if (generator == null) return;
					var pixel = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(pile.CenterPosition));
					var orders = ((IOrderGenerator)generator).Order(w, pile.Location, pixel, new MouseInput(MouseInputEvent.Down, ((OrderGenerator)generator).ActionButton, pixel, int2.Zero, Modifiers.None, 1)).ToArray();
					Check(orders.Length == 1 && orders[0].OrderString == "MedievalCollectCargo", "Ground targeting emits a valid collection order.");
					foreach (var order in orders) w.IssueOrder(order);
				}); stage = 5;
			}
			else if (stage == 5 && competitor.Trait<MedievalManualTransfer>().Collecting)
			{
				stage = 6; until = tick + 10;
			}
			else if (stage == 6 && tick >= until)
			{
				Check(Ui.Root.Get<MedievalTransferWidget>("MD_TRANSFER_PANEL").Store == pile, "Menu shows the actual ground cargo during recovery.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "ground-cargo.png"));
				Command(w, competitor, "Stop"); Check(!competitor.Trait<MedievalManualTransfer>().Busy && StorageGoods.Amount(pile, 0) == 80, "Stopping collection leaves every good on the ground.");
				Command(w, competitor, "MedievalCollectCargo", pile); Command(w, last, "MedievalCollectCargo", pile); stage = 7;
			}
			else if (stage == 7 && !competitor.Trait<MedievalManualTransfer>().Busy && !last.Trait<MedievalManualTransfer>().Busy)
			{
				Check(StorageGoods.Amount(competitor, 0) + StorageGoods.Amount(last, 0) == 80 && (!pile.IsInWorld || StorageGoods.Amount(pile, 0) == 0), "Competing carts share the remainder without duplication.");
				Check(Unreserved(competitor) && Unreserved(last), "No stranded reservation after contested recovery.");
				Command(w, collector, "MedievalUnloadResource", depot); // Malformed foreign unload must not change stock.
				Check(StorageGoods.Amount(collector, 0) == 60, "Loot cannot be deposited through an invalid foreign order.");
				Command(w, collector, "MedievalRecoverTrade");
				Command(w, a, "MedievalLinkMarkets", b); Command(w, trader, "MedievalAssignTrader", a); Command(w, trader, "MedievalPauseTrade");
				Command(w, trader, "MedievalRecoverTrade"); Check(trader.Trait<MedievalTradeCart>().TargetId == (int)third.ActorID, "Recovery initially targets the nearest third market.");
				Command(w, trader, "MedievalPauseTrade"); Command(w, trader, "MedievalResumeTrade");
				Check(trader.Trait<MedievalTradeCart>().TargetId == (int)a.ActorID, "Resume returns to the assigned route, never the unrelated recovery market.");
				Command(w, trader, "Scatter"); Check(!trader.Trait<MedievalTradeCart>().Active, "Scatter frees a cart from its automatic route."); stage = 8;
			}
			else if (stage == 8 && !collector.Trait<MedievalTradeCart>().Active)
			{
				Check(!pile.IsInWorld, "An emptied ground pile disappears.");
				Check(Amounts(collector).All(n => n == 0) && Unreserved(destination), "Stolen goods unload physically with no remaining booking.");
				Check(Amounts(destination).SequenceEqual(lost.Select((n, g) => g == 0 ? 60 : n)), "Enemy depot receives the exact recovered goods.");
				a.Trait<MedievalStockpile>().Stock.Add(0, 90); Command(w, trader, "MedievalAssignTrader", a); stage = 9;
			}
			else if (stage == 9 && StorageGoods.Amount(trader, 0) > 0 && b.Trait<MedievalStockpile>().Stock.Incoming(0) > 0)
			{
				Check(StorageGoods.Amount(trader, 0) == 60 && b.Trait<MedievalStockpile>().Stock.Incoming(0) == 60, "Market route books only its physical sixty-unit shipment.");
				trader.Kill(trader); stage = 10; until = tick + 60;
			}
			else if (stage == 10 && tick >= until)
			{
				var dropped = w.ActorsHavingTrait<MedievalDroppedCargo>().SingleOrDefault();
				Check(dropped != null && StorageGoods.Amount(dropped, 0) == 60 && StorageGoods.Amount(a, 0) == 30 && StorageGoods.Amount(b, 0) == 0,
					"Destroyed market shipment stays at the wreck without loss, duplication or remote delivery.");
				Check(Unreserved(a) && Unreserved(b), "Destroyed market cart releases destination capacity.");
				stage = 11; Finish(w);
			}
			if (tick > 6000) { errors.Add("Timeout at stage " + stage); Finish(w); }
		}
	}
}
