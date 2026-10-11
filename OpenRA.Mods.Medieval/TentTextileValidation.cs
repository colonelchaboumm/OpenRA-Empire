using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalTentTextileValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalTentTextileValidation();
	}
	// Real worker deliveries from a market, alongside haulers competing for output.
	public sealed class MedievalTentTextileValidation : IWorldLoaded, ITick
	{
		Actor depot, market, loom, artisan;
		string output;
		int tick, completedAt, releasedAt;
		bool done, clothCarried, shortageBlocked, returned;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		Actor Spawn(World w, string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "tent-textile", "native"); Directory.CreateDirectory(output);
		}
		void RecipeChecks(World w)
		{
			var actor = Spawn(w, "tentweaver", 40, 40);
			var workshop = actor.Trait<MedievalTentWorkshop>(); var stock = actor.Trait<MedievalCampStore>().Stock;
			workshop.Target = 1; workshop.ClothTarget = 0; workshop.Product = 1;
			workshop.Inputs.Add(0, 4); stock.Add(0, 8);
			Check(!workshop.Work(actor) && workshop.Progress == 0, "Raw wool cannot substitute for cloth in a tent.");
			stock.Add(2, 3);
			Check(!workshop.Work(actor) && workshop.Progress == 0, "Three cloth cannot start a tent.");
			stock.Add(2, 1); stock.ReserveOut(2, 1);
			Check(!workshop.Work(actor), "A tent cannot consume cloth reserved by another carrier.");
			stock.ReleaseOut(2, 1);
			for (var i = 0; i < 40; i++) workshop.Work(actor);
			Check(stock.Amount(1) == 1 && stock.Amount(2) == 0 && stock.Amount(0) == 8 && workshop.Inputs.Amount(0) == 0,
				"One tent consumes exactly four cloth and four wood, leaving raw wool untouched.");
			actor.Dispose();
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			RecipeChecks(w);
			market = Spawn(w, "market", 22, 28); market.Trait<MedievalStockpile>().Stock.Add(1, 250); market.Trait<MedievalCampStore>().Stock.Add(2, 3);
			depot = Spawn(w, "warehouse", 22, 33); depot.Trait<MedievalStockpile>().Stock.Add(0, 4);
			Spawn(w, "house", 26, 23);
			loom = Spawn(w, "tentweaver", 28, 28); loom.Trait<MedievalTentWorkshop>().Target = 1; loom.Trait<MedievalTentWorkshop>().ClothTarget = 0;
			artisan = Spawn(w, "weaver", 27, 29); Spawn(w, "hauler", 26, 33); Spawn(w, "hauler", 26, 34);
		}
		int Cloth(World w) => MedievalCampStore.Amount(w.LocalPlayer, 2)
			+ w.ActorsHavingTrait<MedievalWorker>().Sum(a => a.Trait<MedievalWorker>() is { CampCargo: true, Resource: 2 } worker ? worker.Cargo : 0);
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			if (loom == null) return;
			var workshop = loom.Trait<MedievalTentWorkshop>(); var stock = loom.Trait<MedievalCampStore>().Stock;
			clothCarried |= artisan.Trait<MedievalWorker>() is { CampCargo: true, Resource: 2, Cargo: > 0 };
			if (tick == 1200)
			{
				shortageBlocked = MedievalCampStore.Amount(w.LocalPlayer, 1) == 0 && workshop.Progress == 0 && Cloth(w) == 3;
				Check(shortageBlocked, "Missing cloth must block production without consuming or duplicating stock.");
				market.Trait<MedievalCampStore>().Stock.Add(2, 1);
			}
			if (completedAt == 0 && MedievalCampStore.Amount(w.LocalPlayer, 1) == 1)
			{
				completedAt = tick;
				Check(tick > 1200 && Cloth(w) == 0 && workshop.Inputs.Amount(0) + depot.Trait<MedievalStockpile>().Stock.Amount(0) == 0,
					"Market-supplied cloth must yield one tent at the exact cost without any sheep or wool.");
				workshop.Target = 2; stock.Add(2, 4);
			}
			if (completedAt > 0 && tick == completedAt + 500)
			{
				Check(stock.Amount(2) == 4 && stock.Reserved(2) == 0 && MedievalCampStore.Amount(w.LocalPlayer, 1) == 1,
					"Haulers must retain the next tent's fabric while its wood is missing.");
				w.IssueOrder(new Order("MedievalTentTarget", loom, false)); releasedAt = tick;
			}
			if (releasedAt > 0 && stock.Amount(2) == 0 && MedievalCampStore.Stores(w.LocalPlayer).Sum(a => a.Trait<MedievalCampStore>().Stock.Amount(2)) == 4)
				returned = true;
			if (!returned && tick < 6000) return;
			Check(completedAt > 1200 && clothCarried && returned, "Physical cloth pickup, tent assembly and export after target cancellation must complete.");
			Check(Cloth(w) == 4 && MedievalCampStore.Amount(w.LocalPlayer, 0) == 0, "Four released cloth must be conserved with no wool introduced.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, completedAt, releasedAt,
				clothCarried, shortageBlocked, returned, cloth = Cloth(w), artisan = artisan.Trait<MedievalWorker>().Description }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL TENT TEXTILE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
		}
	}
}
