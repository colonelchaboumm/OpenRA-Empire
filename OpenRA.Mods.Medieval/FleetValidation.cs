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
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalFleetValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFleetValidation();
	}
	public sealed class MedievalFleetValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Player first, second, third;
		Actor depotA, depotB, barracksB, courier, enemy, cargoCart, borrowed, marketA, marketB;
		Actor[] carts, guards;
		MedievalBarter barter;
		BarterDeal deal;
		readonly List<string> errors = [];
		string output;
		int tick, stage, until;
		bool finished, followed, attacked, resumed;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		static int[] Basket(int good, int count) { var result = new int[StorageGoods.Count]; result[good] = count; return result; }
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/barter/fleet"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, Player p, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(p), new LocationInit(Cell(x, y))]);
		void Resolve(Actor actor, Order order)
		{
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = Order.Deserialize(actor.World, reader);
			foreach (var t in actor.TraitsImplementing<IResolveOrder>()) t.ResolveOrder(actor, received);
		}
		void Command(Player p, string name, string value = "") => Resolve(p.PlayerActor, MedievalBarter.Command(p, deal, name, value));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{ var b = Ui.Root.Get<ButtonWidget>(id); Check(!b.IsDisabled(), "Enabled button: " + id); if (!b.IsDisabled()) b.OnClick(); });
		void EscortUi(World w, Actor target)
		{
			Sync.RunUnsynced(w, () =>
			{
				w.Selection.Combine(w, guards, false, false); Button(w, "MD_ESCORT");
				Check(w.OrderGenerator is MedievalEscortOrderGenerator, "Escort button opens the targeting tool.");
				if (w.OrderGenerator is not OrderGenerator generator) return;
				var pixel = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(target.CenterPosition));
				foreach (var order in ((IOrderGenerator)generator).Order(w, target.Location, pixel,
					new MouseInput(MouseInputEvent.Down, generator.ActionButton, pixel, int2.Zero, Modifiers.None, 1)).ToArray()) w.IssueOrder(order);
			});
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			first = w.LocalPlayer; second = w.Players.Single(p => p.InternalName == "Rival"); third = w.Players.Single(p => p.InternalName == "Third");
			first.EnemyPlayersMask = second.PlayerMask; second.EnemyPlayersMask = first.PlayerMask;
			first.Shroud.Disabled = second.Shroud.Disabled = true; barter = w.WorldActor.Trait<MedievalBarter>();
			depotA = Spawn(w, "warehouse", first, 19, 20); depotB = Spawn(w, "warehouse", second, 43, 20);
			Spawn(w, "house", first, 25, 15); Spawn(w, "house", first, 28, 15);
			foreach (var a in new[] { depotA, depotB }) a.Trait<MedievalStockpile>().Stock.SetCapacity([1000, 1000, 1000, 1000, 1000]);
			depotA.Trait<MedievalStockpile>().Stock.Add(0, 700); depotB.Trait<MedievalStockpile>().Stock.Add(3, 300);
			Spawn(w, "barracks", first, 22, 29); barracksB = Spawn(w, "barracks", second, 43, 29); courier = Spawn(w, "messenger", first, 23, 32);
			carts = Enumerable.Range(0, 10).Select(i => Spawn(w, "tradecart", first, 10 + 2 * (i % 5), 24 + 2 * (i / 5))).ToArray();
			Spawn(w, "tradecart", second, 42, 24); cargoCart = Spawn(w, "tradecart", first, 10, 28); cargoCart.Trait<MedievalStockpile>().Stock.Add(0, 1);
			guards = [Spawn(w, "royalguard", first, 10, 22), Spawn(w, "royalguard", first, 12, 22)];
			foreach (var a in guards) a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire);
			marketA = Spawn(w, "market", first, 11, 34); marketB = Spawn(w, "market", first, 16, 34);
			Check(marketA.Trait<MedievalMarket>().CanRecruit(marketA, true) == false, "Recruitment still requires food.");
			marketA.Trait<MedievalStockpile>().Stock.Add(1, 20);
			Check(marketA.Trait<MedievalMarket>().CanRecruit(marketA, true), "Recruiting a barter cart does not require linking two markets.");
			marketA.Trait<MedievalStockpile>().Stock.Take(1, 20);
			Resolve(marketA, new Order("MedievalLinkMarkets", marketA, Target.FromActor(marketB), false));
			borrowed = carts[9]; borrowed.Trait<MedievalTradeCart>().Assign(borrowed, marketA);
			stage = 1; until = 20;
		}
		void Finish()
		{
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, followed, attacked, resumed,
				firstLoads = deal?.Legs[0]?.Journeys.Select(j => j.Loads).ToArray(), secondLoads = deal?.Legs[1]?.Journeys.Select(j => j.Loads).ToArray(), errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL FLEET: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(500, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick % 500 == 0) Console.WriteLine($"FLEET tick={tick} stage={stage} deal={deal?.State} status={deal?.Status} cart={carts[0].ActorID}@{carts[0].Location} guards="
				+ string.Join(" | ", guards.Select(a => $"{a.Location}, target={a.Trait<MedievalEscort>().ProtectedId}, distance={(a.CenterPosition - carts[0].CenterPosition).HorizontalLength}, activity={a.CurrentActivity?.GetType().Name}")));
			if (stage == 1 && tick >= until)
			{
				Check(MedievalBarter.AvailableCarts(first).Count() == 10, "Only empty owned carts are available, including an empty route cart.");
				EscortUi(w, carts[0]);
				Resolve(first.PlayerActor, MedievalMessenger.Command(first, courier, "MedievalInviteBarter", target: barracksB)); deal = barter.Deals.Last(); stage = 2;
			}
			else if (stage == 2 && courier.Trait<MedievalMessenger>().CanReceive(courier, second))
			{ Command(second, "MedievalBarterOpen"); stage = 3; until = tick + 20; }
			else if (stage == 3 && tick >= until)
			{
				Check(guards.All(a => a.Trait<MedievalEscort>().Protected == carts[0]), "UI target selection assigns the cart to both guards.");
				Button(w, "MD_BARTER_CARTS_ALL"); stage = 4; until = tick + 20;
			}
			else if (stage == 4 && tick >= until)
			{
				Check(deal.CartCounts.SequenceEqual(new[] { 10, 1 }), "All button allocates ten existing carts only on my side.");
				var rev = deal.Revision; Command(third, "MedievalBarterCarts", "2"); Command(first, "MedievalBarterCarts", "-1"); Command(first, "MedievalBarterCarts", "101");
				Check(deal.Revision == rev, "Invalid counts and foreign allocation orders are rejected.");
				Command(first, "MedievalBarterCarts", "11"); Command(first, "MedievalBarterPropose", BarterOffer.Encode(Basket(0, 600), Basket(3, 180))); Command(second, "MedievalBarterAccept");
				Check(deal.State == BarterState.Offered && depotA.Trait<MedievalStockpile>().Stock.Reserved(0) == 0, "Missing cart leaves both payments and carts unreserved.");
				var stale = MedievalBarter.Command(second, deal, "MedievalBarterAccept"); Command(first, "MedievalBarterCarts", "10"); Resolve(second.PlayerActor, stale);
				Check(deal.State == BarterState.Negotiating, "Fleet changes invalidate stale consent.");
				Command(first, "MedievalBarterPropose", BarterOffer.Encode(Basket(0, 600), Basket(3, 180))); Command(second, "MedievalBarterAccept");
				Check(deal.State == BarterState.Delivering && MedievalBarter.AvailableCarts(first).Count() == 0, "Agreed fleet cannot be lent to another contract.");
				stage = 5; Button(w, "MD_BARTER_CLOSE");
			}
			else if (stage == 5)
			{
				followed |= carts[0].Location.X > Cell(27, 24).X && guards.All(a => a.Trait<MedievalEscort>().Protected == carts[0]
					&& (a.CenterPosition - carts[0].CenterPosition).HorizontalLengthSquared < 4096L * 4096);
				if (deal.State == BarterState.Completed && !deal.Returning)
				{
					Check(deal.Legs[0].Journeys.All(j => j.Loads == 1) && deal.Legs[1].Journeys.Single().Loads == 3, "Ten carts make one delivery each; the single cart makes three trips.");
					Check(carts.All(a => MedievalStockpile.Active(a) && !a.Trait<MedievalBarterCart>().Busy), "Borrowed carts survive and become available after returning.");
					Check(borrowed.Trait<MedievalTradeCart>().Active, "Previously active route resumes after the loan.");
					Check(depotA.Trait<MedievalStockpile>().Stock.Amount(0) == 100 && depotB.Trait<MedievalStockpile>().Stock.Amount(0) == 600
						&& depotA.Trait<MedievalStockpile>().Stock.Amount(3) == 180 && depotB.Trait<MedievalStockpile>().Stock.Amount(3) == 120, "Exact real bilateral delivery.");
					Check(followed, "Selected combat units follow a real moving delivery cart.");
					foreach (var a in guards) Resolve(a, new Order("MedievalEscort", a, Target.FromActor(courier), false));
					Resolve(courier, new Order("Move", courier, Target.FromCell(w, Cell(30, 36)), false)); stage = 6;
				}
			}
			else if (stage == 6 && courier.IsIdle && guards.All(a => (a.CenterPosition - courier.CenterPosition).HorizontalLengthSquared < 3072L * 3072))
			{
				Check(guards.All(a => a.Trait<MedievalEscort>().Protected == courier), "Escort can protect a messenger instead of a cart.");
				Check(!MedievalEscort.CanEscort(guards[0], guards[0]) && !MedievalEscort.CanEscort(guards[0], deal.Legs[1].Cart), "Self and enemy units cannot be escorted.");
				foreach (var a in guards) a.Trait<AutoTarget>().SetStance(a, UnitStance.AttackAnything);
				w.AddFrameEndTask(_ =>
				{
					var cell = guards[0].Location + new CVec(1, 0);
					enemy = w.CreateActor("md.archer", [new OwnerInit(second), new LocationInit(cell)]); enemy.Trait<AutoTarget>().SetStance(enemy, UnitStance.HoldFire);
				}); stage = 7;
			}
			else if (stage == 7 && enemy != null)
			{
				attacked |= !MedievalStockpile.Active(enemy) || enemy.Trait<Health>().HP < enemy.Trait<Health>().MaxHP;
				if (!MedievalStockpile.Active(enemy))
				{ Resolve(courier, new Order("Move", courier, Target.FromCell(w, Cell(38, 36)), false)); stage = 8; until = tick + 500; }
			}
			else if (stage == 8 && tick >= until)
			{
				resumed = guards.All(a => a.Trait<MedievalEscort>().Protected == courier && (a.CenterPosition - courier.CenterPosition).HorizontalLengthSquared < 4096L * 4096);
				Check(attacked && resumed, "Guards fight and resume following the protected unit.");
				Sync.RunUnsynced(w, () => { w.Selection.Combine(w, guards, false, false); renderer.Viewport.Center(courier.CenterPosition); });
				Game.Renderer.SaveScreenshot(Path.Combine(output, "escort.png"));
				Resolve(guards[0], new Order("Move", guards[0], Target.FromCell(w, Cell(35, 38)), false)); courier.Kill(courier); stage = 9; until = tick + 150;
			}
			else if (stage == 9 && tick >= until)
			{
				Check(guards.All(a => a.Trait<MedievalEscort>().Protected == null), "A manual move and target death both end their escort.");
				Resolve(guards[0], new Order("MedievalEscort", guards[0], Target.FromActor(guards[1]), false));
				Resolve(guards[1], new Order("MedievalEscort", guards[1], Target.FromActor(guards[0]), false));
				Check(guards[1].Trait<MedievalEscort>().Protected == null, "Circular escorts are rejected.");
				Button(w, "MD_ESCORT_STOP"); stage = 10; until = tick + 20;
			}
			else if (stage == 10 && tick >= until) { Check(guards.All(a => a.Trait<MedievalEscort>().Protected == null), "Stop escort releases the selected guard."); Finish(); }
			if (tick > 18000) { errors.Add("Timeout at stage " + stage); Finish(); }
		}
	}
}
