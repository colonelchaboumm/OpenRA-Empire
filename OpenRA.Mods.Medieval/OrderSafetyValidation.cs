using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalOrderSafetyValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalOrderSafetyValidation();
	}
	// Exercise real targeting tools before, during and after a synchronized pause.
	public sealed class MedievalOrderSafetyValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<string> checks = [];
		readonly List<(string Name, Func<OrderGenerator> Create, CPos[] Cells)> cases = [];
		readonly Dictionary<string, OrderGenerator> pausedTools = [];
		WorldRenderer renderer;
		Actor escort, escorted, fire, well, goldMarket, goldCart, campCommander;
		string output;
		int tick;
		bool tested;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool passed, string text)
		{ checks.Add(text); if (!passed) errors.Add(text); Console.WriteLine($"ORDER SAFETY {(passed ? "PASS" : "FAIL")}: {text}"); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/order-safety"); Directory.CreateDirectory(output); }
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			Actor Spawn(string type, int x, int y, Player owner = null) => w.CreateActor("md." + type,
				[new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
			var rival = w.Players.Single(p => p.InternalName == "Rival");
			w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask; rival.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			Spawn("farm", 20, 20); Spawn("ranch", 20, 31);
			var pen = Spawn("pasture.4x4", 24, 33); var cow = Spawn("cow", 22, 36);
			var market = Spawn("market", 30, 20); var secondMarket = Spawn("market", 35, 20);
			goldMarket = market; goldCart = Spawn("tradecart", 29, 21);
			Spawn("barracks", 20, 42); var messenger = Spawn("messenger", 21, 45);
			var target = Spawn("barracks", 42, 42, rival);
			var commander = Spawn("commander", 39, 30); commander.Trait<MedievalCampStore>().Stock.Add(1, 1);
			campCommander = commander;
			var saboteur = Spawn("saboteur", 38, 40);
			escort = Spawn("militia", 30, 27); escorted = Spawn("tradecart", 35, 28);
			fire = Spawn("house", 32, 26); well = Spawn("well", 28, 26);
			escort.Trait<AutoTarget>().SetStance(escort, UnitStance.HoldFire);
			cases.Add(("Agricultural field", () => new MedievalZoneOrderGenerator(w, renderer, "md.field"), [Cell(24, 20), Cell(25, 21)]));
			cases.Add(("Animal transfer", () => new MedievalAnimalTransferGenerator(w, renderer, cow), [pen.Location]));
			cases.Add(("Market connection", () => new MedievalLogisticsTargetGenerator(w, renderer, market, "MedievalLinkMarkets"), [secondMarket.Location]));
			cases.Add(("Messenger", () => new MedievalMessengerTargetGenerator(w, renderer, messenger, "Test"), [target.Location]));
			cases.Add(("Commander's tent", () => new MedievalTentOrderGenerator(w, renderer, commander), [Cell(40, 30)]));
			cases.Add(("Sabotage", () => new MedievalSabotageGenerator(saboteur, renderer, 0), [target.Location]));
		}
		static Order[] Click(World w, OrderGenerator generator, CPos cell) => generator.Order(w, cell, int2.Zero,
			new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
		void Test(World w, bool paused)
		{
			Check(w.Paused == paused, "Simulation pause state: " + paused);
			foreach (var test in cases)
			{
				if (test.Name == "Commander's tent")
				{
					// This checks pause handling on a valid target, not whether a particular
					// tile happens to be free in the current randomized village.
					var site = w.Map.FindTilesInAnnulus(campCommander.Location, 2, 6)
						.Where(c => campCommander.Trait<MedievalCommander>().CanDeploy(campCommander, c))
						.Select(c => (CPos?)c).FirstOrDefault();
					Check(site.HasValue, "Commander has a valid tent site for the pause scenario");
					if (site.HasValue) test.Cells[0] = site.Value;
				}
				var generator = !paused && pausedTools.TryGetValue(test.Name, out var retained) ? retained : test.Create();
				w.OrderGenerator = generator;
				var orders = new List<Order>();
				for (var i = 0; i < test.Cells.Length; i++)
				{
					orders.AddRange(Click(w, generator, test.Cells[i]));
					if (test.Cells.Length > 1 && i == 0) Check(orders.Count == 0, "Field first corner does not complete a zone");
				}
				Check(orders.Count == (paused ? 0 : 1), test.Name + (paused ? " rejects clicks during pause" : " accepts a valid target"));
				if (paused) { Check(w.OrderGenerator == generator, test.Name + " keeps its targeting tool during pause"); pausedTools[test.Name] = generator; }
				w.CancelInputMode();
			}
		}
		void AwaitPause(World w, int attempts)
		{
			if (!w.Paused && attempts > 0) { Game.RunAfterDelay(50, () => AwaitPause(w, attempts - 1)); return; }
			Sync.RunUnsynced(w, () => { Test(w, true); w.SetPauseState(false); }); tested = true;
		}
		void MalformedConstruction(World w)
		{
			try
			{
				var order = new Order("MedievalPlace", w.LocalPlayer.PlayerActor, false) { TargetString = "md.house" };
				using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
				var received = Order.Deserialize(w, reader);
				received.Subject.ResolveOrder(received);
				Check(true, "Construction order without a target is safely ignored");
			}
			catch (InvalidOperationException e) { Check(false, "Construction order without a target throws: " + e.Message); }
			try { Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, null, Cell(40, 20)), "Missing building type is safely rejected"); }
			catch (ArgumentNullException e) { Check(false, "Missing building type throws: " + e.ParamName); }
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); }
			if (tick == 10) MalformedConstruction(w);
			if (tick == 20) Sync.RunUnsynced(w, () => { Test(w, false); w.SetPauseState(true); Game.RunAfterDelay(50, () => AwaitPause(w, 40)); });
			if (tick == 40 && tested) Sync.RunUnsynced(w, () => Test(w, false));
			if (tick == 50)
			{
				fire.Trait<MedievalBurnable>().Ignite(fire, escort, fire.Location, false);
				Check(escort.Trait<MedievalFirefighter>().Start(escort, fire, well), "Militia starts fighting a fire before escort order");
				w.IssueOrder(new Order("MedievalEscort", escort, Target.FromActor(escorted), false));
			}
			var cargo = goldCart?.Trait<MedievalStockpile>().Stock;
			var stock = goldMarket?.Trait<MedievalStockpile>().Stock;
			if (tick == 100)
			{
				Check(!escort.Trait<MedievalFirefighter>().Busy, "Explicit escort order releases the firefighting job");
				Check(!escort.Trait<AutoTarget>().IsTraitDisabled, "Militia can defend again after leaving the fire");
				Check(escort.Trait<MedievalEscort>().Protected == escorted, "Escort mission survives the interrupted emergency");
				cargo.Add((int)Material.Gold, 25);
				w.IssueOrder(new Order("MedievalRecoverTrade", goldCart, false));
			}
			if (tick == 400)
			{
				Check(cargo.Amount((int)Material.Gold) == 0 && stock.Amount((int)Material.Gold) == 25
					&& !goldCart.Trait<MedievalTradeCart>().Active, "Cart carrying only gold unloads and finishes recovery");
				stock.Take((int)Material.Gold, stock.Amount((int)Material.Gold));
				cargo.Take((int)Material.Gold, cargo.Amount((int)Material.Gold));
				stock.Add((int)Material.Gold, stock.Capacity((int)Material.Gold) - 7);
				cargo.Add((int)Material.Gold, 20); cargo.Add((int)Material.Wood, 3);
				w.IssueOrder(new Order("MedievalRecoverTrade", goldCart, false));
			}
			if (tick == 700)
			{
				Check(cargo.Amount((int)Material.Gold) == 13 && cargo.Amount((int)Material.Wood) == 0
					&& stock.Free((int)Material.Gold) == 0, "Partial unloading retains the gold when the market fills");
				stock.Take((int)Material.Gold, 20);
			}
			if (tick != 1000) return;
			Check(cargo.Amount((int)Material.Gold) == 0 && !goldCart.Trait<MedievalTradeCart>().Active
				&& stock.Amount((int)Material.Gold) == stock.Capacity((int)Material.Gold) - 7,
				"Recovery resumes when space becomes available and conserves the remaining gold");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, checks, tick }, new JsonSerializerOptions { WriteIndented = true }));
			Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
