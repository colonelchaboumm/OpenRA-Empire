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
	public class MedievalTradeValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalTradeValidation();
	}
	public sealed class MedievalTradeValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick;
		int phase;
		int phaseTick;
		int consumedAtStart;
		int foundationTrips;
		int marketTrips;
		int screenshotTick;
		string screenshot;
		Actor home;
		Actor cityMarket;
		Actor remote;
		Actor remoteMarket;
		Actor supply;
		Actor trader;
		bool builderCarried;
		bool bookedTransit;
		bool testedInvalidLink;
		bool finished;
		int[] pausedCargo;
		readonly List<string> errors = [];
		readonly List<string> checks = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "trade-preview");
			Directory.CreateDirectory(output);
		}
		void Check(bool ok, string message) { if (!ok) errors.Add(message); }
		void Advance(int next) { phase = next; phaseTick = tick; }
		void Select(World w, Actor actor) => w.Selection.Combine(w, [actor], false, true);
		void Click(World w, Actor actor, string button) => Sync.RunUnsynced(w, () =>
		{
			Select(w, actor);
			var widget = Ui.Root.Get<ButtonWidget>(button);
			Check(!widget.IsDisabled(), "Button must be available: " + button);
			widget.OnClick();
		});
		void TargetClick(World w, Actor actor, string button, CPos cell)
		{
			Click(w, actor, button);
			Sync.RunUnsynced(w, () =>
			{
				if (w.OrderGenerator is not OrderGenerator generator) { Check(false, "Target generator missing: " + button); return; }
				var orders = generator.Order(w, cell, int2.Zero,
					new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
				Check(orders.Length == 1, "Target click must issue exactly one order: " + button);
				foreach (var order in orders) w.IssueOrder(order);
			});
		}
		void Shot(World w, Actor selected, string name)
		{
			Sync.RunUnsynced(w, () => { Select(w, selected); renderer.Viewport.Center(selected.CenterPosition); });
			// Let the selected panel and camera render before reading the framebuffer.
			screenshot = Path.Combine(output, name + ".png");
			screenshotTick = tick + 5;
		}
		void Setup(World w)
		{
			foreach (var actor in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null
				|| a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) actor.Dispose();
			home = w.CreateActor("md.warehouse", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(12, 32)), new MedievalStockInit([400, 250, 0, 300])]);
			cityMarket = w.CreateActor("md.market", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(17, 32)), new MedievalStockInit([0, 160, 0, 0])]);
			foreach (var (type, x, y) in new[] { ("md.builder", 16, 36), ("md.builder", 18, 36), ("md.hauler", 15, 35), ("md.hauler", 16, 35) })
				w.CreateActor(type, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
			consumedAtStart = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
		}
		void Finish(World w)
		{
			var totals = new int[4];
			foreach (var a in w.Actors.Where(a => a.Owner == w.LocalPlayer))
			{
				if (a.TraitOrDefault<MedievalStockpile>() is MedievalStockpile stock)
					for (var r = 0; r < 4; r++) totals[r] += stock.Stock.Amount(r);
				if (a.TraitOrDefault<MedievalBuilding>() is MedievalBuilding building)
					for (var r = 0; r < 4; r++) totals[r] += building.Materials.Amount(r);
				if (a.TraitOrDefault<MedievalWorker>() is MedievalWorker worker && worker.Cargo > 0) totals[worker.Resource] += worker.Cargo;
			}
			var consumed = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed - consumedAtStart;
			if (phase >= 4)
			{
				Check(totals[0] == 400 && totals[2] == 60 && totals[3] == 390, $"Physical material conservation failed: {string.Join(",", totals)}");
				Check(totals[1] + consumed == 370, $"Food must equal 410 initial minus 40 recruitment and meals: stocks {totals[1]}, meals {consumed}");
			}
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, phase, errors, checks,
				foundationTrips, marketTrips, builderCarried, bookedTransit, totals, consumed }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL TRADE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true;
			Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			if (self.World.LocalPlayer == null) return;
			var w = self.World;
			tick++;
			if (screenshot != null && tick >= screenshotTick)
			{
				Game.Renderer.SaveScreenshot(screenshot);
				screenshot = null;
			}
			if (finished) return;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 20)
			{
				TargetClick(w, home, "MD_BUILD_1", Cell(45, 32));
				TargetClick(w, home, "MD_BUILD_8", Cell(49, 34));
				Click(w, cityMarket, "MD_CART_CREATE");
			}
			if (tick == 160)
			{
				remote = w.Actors.FirstOrDefault(a => a.Info.Name == "md.warehouse" && a.Location == Cell(45, 32));
				remoteMarket = w.Actors.FirstOrDefault(a => a.Info.Name == "md.market" && a.Location == Cell(49, 34));
				supply = w.Actors.FirstOrDefault(a => a.Info.Name == "md.supplycart");
				Check(remote != null && remoteMarket != null && supply != null, "Both remote sites and the recruited cart must exist.");
				if (remote == null || remoteMarket == null || supply == null) { Finish(w); return; }
				Check(!MedievalConstructionSupply.Sources(w.LocalPlayer, remote.CenterPosition).Any(), "The remote depot must be outside town's construction influence.");
				Check(remote.Trait<MedievalBuilding>().Materials.Amount(0) == 0, "Remote construction cannot teleport town resources.");
				w.IssueOrder(new Order("MedievalLinkMarkets", cityMarket, Target.FromActor(remoteMarket), false));
				TargetClick(w, supply, "MD_CONVOY_ASSIGN", remote.Location);
				Advance(1);
			}
			if (tick == 180) { testedInvalidLink = !cityMarket.Trait<MedievalTradeMarket>().Linked(cityMarket); Check(testedInvalidLink, "An unfinished market must not be linkable."); }
			if (phase >= 1 && phase <= 2)
				builderCarried |= w.ActorsWithTrait<MedievalWorker>().Any(p => p.Trait.Info.Profession == "builder" && p.Trait.Cargo > 0 && p.Actor.Location.X > Cell(38, 30).X);
			if (phase == 1 && remote.Trait<MedievalBuilding>().Complete)
			{
				foundationTrips = supply.Trait<MedievalConstructionConvoy>().Trips;
				Check(foundationTrips >= 2 && builderCarried, "A real multi-trip convoy and builders must found the remote warehouse.");
				Check(remote.Trait<MedievalBuilding>().Materials.Amount(0) == 100 && remote.Trait<MedievalBuilding>().Materials.Amount(3) == 40, "Warehouse construction cost must be exact.");
				checks.Add("Remote warehouse built with multiple physical cart loads");
				Shot(w, supply, "01-remote-warehouse-built");
				TargetClick(w, supply, "MD_CONVOY_ASSIGN", remoteMarket.Location);
				Advance(2);
			}
			if (phase == 2 && remoteMarket.Trait<MedievalBuilding>().Complete)
			{
				marketTrips = supply.Trait<MedievalConstructionConvoy>().Trips;
				Check(marketTrips >= 2, "The remote market must also receive multiple real loads.");
				Check(remoteMarket.Trait<MedievalBuilding>().Materials.Amount(0) == 80 && remoteMarket.Trait<MedievalBuilding>().Materials.Amount(3) == 30, "Market construction cost must be exact.");
				Check(!MedievalLogistics.Local(cityMarket, remote, 16), "Haulers must not bypass the commercial route across towns.");
				checks.Add("Second market built with locally delivered resources");
				TargetClick(w, cityMarket, "MD_TRADE_LINK", remoteMarket.Location);
				remote.Trait<MedievalStockpile>().Stock.Add(3, 90); remote.Trait<MedievalStockpile>().Stock.Add(2, 60);
				w.AddFrameEndTask(_ =>
				{
					w.CreateActor("md.hauler", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(48, 37))]);
					w.CreateActor("md.hauler", [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(49, 37))]);
				});
				Advance(3);
			}
			if (phase == 3 && tick > phaseTick + 15)
			{
				Check(cityMarket.Trait<MedievalTradeMarket>().Linked(cityMarket), "The link button and target click must connect both markets.");
				Click(w, cityMarket, "MD_TRADE_EXPORT_3");
				Advance(31);
			}
			if (phase == 31 && tick > phaseTick + 15)
			{
				Check(cityMarket.Trait<MedievalTradeMarket>().Exports(3) && !remoteMarket.Trait<MedievalTradeMarket>().Exports(3), "Export choice must reverse one resource without a loop.");
				Click(w, remoteMarket, "MD_TRADE_EXPORT_3");
				Click(w, cityMarket, "MD_TRADE_CREATE");
				Advance(32);
			}
			if (phase == 32 && tick > phaseTick + 140)
			{
				trader = w.Actors.FirstOrDefault(a => a.Info.Name == "md.tradecart");
				Check(trader != null && trader.Trait<MedievalTradeCart>().Active, "A paid trader must spawn and start the route.");
				if (trader == null) { Finish(w); return; }
				Shot(w, cityMarket, "02-commercial-route-configured");
				Advance(4);
			}
			if (phase == 4 && tick > phaseTick + 15)
			{
				var cart = trader.Trait<MedievalTradeCart>();
				if (!bookedTransit && trader.Trait<MedievalStockpile>().Stock.Amount(0) > 0 && !MedievalAccess.Adjacent(trader, remoteMarket) && remoteMarket.Trait<MedievalStockpile>().Stock.Incoming(0) > 0)
				{ bookedTransit = true; Shot(w, trader, "03-goods-in-transit"); }
				if (remote.Trait<MedievalStockpile>().Stock.Amount(0) > 0 && home.Trait<MedievalStockpile>().Stock.Amount(2) > 0
					&& home.Trait<MedievalStockpile>().Stock.Amount(3) > 230 && remoteMarket.Trait<MedievalStockpile>().Stock.Amount(1) > 0
					&& Enumerable.Range(0, 4).Sum(trader.Trait<MedievalStockpile>().Stock.Amount) > 0)
				{
					Check(bookedTransit && cart.Delivered > 0, "Commercial cargo must be physically carried and delivered.");
					checks.Add("Wood and food reach the outpost; stone and meat return to town through local haulers");
					pausedCargo = Enumerable.Range(0, 4).Select(trader.Trait<MedievalStockpile>().Stock.Amount).ToArray();
					Click(w, trader, "MD_TRADER_PAUSE"); Advance(5);
				}
			}
			if (phase == 5 && tick > phaseTick + 15)
			{
				Check(!trader.Trait<MedievalTradeCart>().Active && pausedCargo.SequenceEqual(Enumerable.Range(0, 4).Select(trader.Trait<MedievalStockpile>().Stock.Amount)), "Pause must preserve the entire cargo.");
				Check(trader.Trait<MedievalTradeCart>().ShipmentHash == 0, "Pause must release reserved destination space.");
				Click(w, trader, "MD_TRADER_PAUSE"); Advance(6);
			}
			if (phase == 6 && tick > phaseTick + 15)
			{
				Check(trader.Trait<MedievalTradeCart>().Active, "Resume must restart the route.");
				Click(w, cityMarket, "MD_TRADE_UNLINK"); Advance(7);
			}
			if (phase == 7 && tick > phaseTick + 40)
			{
				Check(!cityMarket.Trait<MedievalTradeMarket>().Linked(cityMarket) && !trader.Trait<MedievalTradeCart>().Active, "Unlinking must safely stop the trader.");
				Check(pausedCargo.SequenceEqual(Enumerable.Range(0, 4).Select(trader.Trait<MedievalStockpile>().Stock.Amount)), "Unlinking must not erase a cargo in transit.");
				Click(w, trader, "MD_TRADER_RECOVER"); Advance(8);
			}
			if (phase == 8 && !trader.Trait<MedievalTradeCart>().Active && tick > phaseTick + 30)
			{
				Check(Enumerable.Range(0, 4).Sum(trader.Trait<MedievalStockpile>().Stock.Amount) == 0, "Recovery must physically empty the cart at a market.");
				checks.Add("Pause, resume, unlink and physical cargo recovery preserve resources");
				Check(w.WorldActor.Trait<MedievalTrails>().Trails.Marks.Values.Count(m => m.Level > 0) > 30, "Convoys and traders must wear a natural road through actual traffic.");
				Shot(w, remote, "04-outpost-and-trade-complete");
				Finish(w); return;
			}
			if (tick % 500 == 0)
				File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { tick, phase,
					convoy = supply?.TraitOrDefault<MedievalConstructionConvoy>()?.Status,
					cart = supply == null ? null : Enumerable.Range(0, 4).Select(supply.Trait<MedievalStockpile>().Stock.Amount).ToArray(),
					trade = trader?.TraitOrDefault<MedievalTradeCart>()?.Status,
					workers = w.ActorsWithTrait<MedievalWorker>().Select(p => new { p.Actor.Location, p.Trait.Description }), errors }, new JsonSerializerOptions { IncludeFields = true }));
			if (tick < 22000) return;
			Check(false, "Scenario timed out in phase " + phase); Finish(w);
		}
	}
}
