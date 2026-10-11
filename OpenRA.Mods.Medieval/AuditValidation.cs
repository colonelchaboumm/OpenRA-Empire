using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalAuditValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalAuditValidation();
	}
	// Regression cases for simultaneous orders and changes between accepting and completing a job.
	public sealed class MedievalAuditValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<string> checks = [];
		string output;
		int tick, phase, age;
		bool done, removedField;
		Actor site, decoration, tent, cart, warehouse, house, market, barracks, blocker;
		Actor returningBuilder, returningHauler;
		Actor animal, pen, breeder, field, farmer;
		int[] siteCost, decorationCost;
		WorldRenderer renderer;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool valid, string message)
		{ checks.Add(message); if (!valid) errors.Add(message); Console.WriteLine($"AUDIT {(valid ? "PASS" : "FAIL")}: {message}"); }
		Actor Spawn(World w, string name, int x, int y, bool construction = false)
		{
			var init = new TypeDictionary { new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y)) };
			if (construction) init.Add(new MedievalConstructionInit());
			var a = w.CreateActor("md." + name, init);
			if (a.TraitOrDefault<MedievalWorker>() is { } worker) worker.Automatic = false;
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire);
			return a;
		}
		void Send(World w, Actor a, string command, Actor target = null, string type = null)
			=> w.IssueOrder(new Order(command, a, target == null ? Target.Invalid : Target.FromActor(target), false) { TargetString = type });
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "audit", "edge"); Directory.CreateDirectory(output); }
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.")).ToArray()) a.Dispose();
			age = 0;
			if (phase == 0)
			{
				site = Spawn(w, "house", 20, 20, true); decoration = Spawn(w, "gardenfence", 33, 20, true);
				siteCost = [7, 0, 0, 3]; decorationCost = [Math.Min(3, decoration.Trait<MedievalBuilding>().Info.Cost[0]), 0, 0, 0];
				for (var r = 0; r < 4; r++) { site.Trait<MedievalBuilding>().Materials.Add(r, siteCost[r]); decoration.Trait<MedievalBuilding>().Materials.Add(r, decorationCost[r]); }
				tent = Spawn(w, "militarytent", 45, 20); cart = Spawn(w, "supplycart", 44, 20);
			}
			else if (phase is 1 or 2)
			{
				warehouse = Spawn(w, "warehouse", 30, 30); warehouse.Trait<MedievalStockpile>().Stock.Add(1, 200);
				house = Spawn(w, "house", 34, 30);
				market = Spawn(w, "market", 38, 30);
				for (var n = 0; n < 12; n++) Spawn(w, "builder", 20 + n % 6, 40 + n / 6);
			}
			else if (phase == 3)
			{
				house = Spawn(w, "house", 35, 30); barracks = Spawn(w, "barracks", 25, 25);
				var recruiter = Spawn(w, "recruiter", 28, 28); var workshop = barracks.Trait<MedievalMilitaryWorkshop>();
				workshop.AssignStaff(barracks, recruiter); Check(workshop.Request(barracks, "md.militia"), "Recruitment collision setup accepted");
				var required = workshop.Required(barracks); for (var r = 0; r < 4; r++) workshop.Inputs.Add(r, required[r]);
			}
			else if (phase == 4)
			{
				market = Spawn(w, "marketstall", 25, 30);
				returningBuilder = Spawn(w, "builder", 23, 30); returningHauler = Spawn(w, "hauler", 23, 32);
				foreach (var a in new[] { returningBuilder, returningHauler })
				{
					var worker = a.Trait<MedievalWorker>(); worker.Resource = 0; worker.Cargo = 6; worker.Automatic = true;
				}
			}
			else if (phase is 5 or 6)
			{
				Spawn(w, "ranch", 24, 26); pen = Spawn(w, "pasture.4x4", 26, 30);
				animal = Spawn(w, "cow", 24, 36); breeder = Spawn(w, "breeder", 23, 35);
			}
			else if (phase == 7)
			{
				Spawn(w, "farm", 24, 26); field = Spawn(w, "field.4x4", 26, 30);
				field.Trait<MedievalSource>().Remaining = field.Trait<MedievalSource>().Info.Amount;
				farmer = Spawn(w, "farmer", 25, 31); farmer.Trait<MedievalWorker>().Automatic = true;
			}
		}
		void Next(World w) { phase++; w.AddFrameEndTask(Setup); }
		void Finish()
		{
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, checks }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL AUDIT EDGE: " + (errors.Count == 0 ? "PASS" : "FAIL")); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++; age++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(31, 28))));
			if (phase == 0)
			{
				if (age == 20) for (var n = 0; n < 3; n++)
				{
					Send(w, w.LocalPlayer.PlayerActor, "MedievalCancel", site);
					Send(w, w.LocalPlayer.PlayerActor, "MedievalRemoveDecoration", decoration);
					Send(w, tent, "MedievalPackTent");
				}
				if (age == 45)
				{
					int[] Salvage(CPos cell) => Enumerable.Range(0, 4).Select(r => w.ActorsHavingTrait<MedievalStockpile>().Where(a => a.Info.Name == "md.salvage" && a.Location == cell).Sum(a => a.Trait<MedievalStockpile>().Stock.Amount(r))).ToArray();
					Check(Salvage(Cell(20, 20)).SequenceEqual(siteCost), "Repeated construction cancellation returns delivered materials once");
					Check(decoration.IsInWorld && decoration.Trait<MedievalDemolition>().Requested && decoration.Trait<MedievalDemolition>().Progress == 0
						&& Enumerable.Range(0, 4).All(r => decoration.Trait<MedievalBuilding>().Materials.Amount(r) == decorationCost[r])
						&& Salvage(Cell(33, 20)).All(n => n == 0), "Repeated demolition orders wait for a builder without duplicating delivered materials");
					Check(cart.Trait<MedievalCampStore>().Stock.Amount(1) == 1, "Repeated packing returns exactly one pavilion");
					Next(w);
				}
			}
			else if (phase is 1 or 2)
			{
				if (age == 20) Send(w, phase == 1 ? w.LocalPlayer.PlayerActor : market, phase == 1 ? "MedievalRecruit" : "MedievalCreateCart", type: phase == 1 ? "md.builder" : null);
				if (age == 30) { Check(phase == 1 ? w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().Recruiting : market.Trait<MedievalMarket>().Recruiting, "Recruitment was accepted before housing loss " + phase); house.Kill(warehouse); }
				if (age == 155)
				{
					Check(MedievalSettlement.Population(w.LocalPlayer) == 12 && MedievalSettlement.PendingPopulation(w.LocalPlayer) == 1, "Recruitment waits when housing is destroyed " + phase);
					w.AddFrameEndTask(world => house = Spawn(world, "house", 34, 30));
				}
				if (age == 300)
				{
					Check(MedievalSettlement.Population(w.LocalPlayer) == 13 && MedievalSettlement.PendingPopulation(w.LocalPlayer) == 0, "Recruitment resumes after housing restoration " + phase);
					Check(warehouse.Trait<MedievalStockpile>().Stock.Amount(1) == 200 - MedievalSettlement.Cost(w.LocalPlayer, phase == 1 ? "md.builder" : "md.supplycart").Food, "Resumed recruitment charges food only once " + phase);
					Next(w);
				}
			}
			else if (phase == 3)
			{
				var workshop = barracks.Trait<MedievalMilitaryWorkshop>();
				if (age == 20)
				{
					for (var i = 0; i < workshop.Info.WorkSteps; i++) workshop.Work(barracks);
					blocker = Spawn(w, "gardenfence", 24, 24);
				}
				if (age == 30)
				{
					Check(!w.Actors.Any(a => a.Info.Name == "md.militia") && workshop.Recruit >= 0, "Recruit cannot spawn inside a newly occupied exit");
					Check(workshop.Inputs.Amount(1) == MedievalSettlement.Cost(w.LocalPlayer, "md.militia").Food, "Blocked exit retains paid recruitment inputs");
					blocker.Dispose();
				}
				if (age == 40) workshop.Work(barracks);
				if (age == 50)
				{
					Check(w.Actors.Count(a => a.Info.Name == "md.militia") == 1, "Cleared exit produces exactly one soldier");
					Next(w);
				}
			}
			else if (phase == 4 && age == 180)
			{
				Check(returningBuilder.Trait<MedievalWorker>().Cargo == 0 && returningHauler.Trait<MedievalWorker>().Cargo == 0,
					"Interrupted builder and hauler can return materials to a remote market stall");
				Check(market.Trait<MedievalStockpile>().Stock.Amount(0) == 12, "Returning cargo is conserved exactly in the market stall");
				Next(w);
			}
			else if (phase is 5 or 6)
			{
				if (age == 20) Check(phase == 5 ? breeder.Trait<MedievalHerding>().Start(breeder, animal, pen)
					: animal.Trait<MedievalAnimal>().Request(animal, pen, w.LocalPlayer), "Animal transfer accepted before pen removal " + phase);
				if (age == 21) pen.Dispose();
				if (age == 45)
				{
					Check(!breeder.Trait<MedievalHerding>().Busy && animal.Trait<MedievalAnimal>().RequestedPen == null
						&& animal.Trait<MedievalAnimal>().Escort == null && !animal.Trait<MedievalAnimal>().Claimed(w.LocalPlayer),
						"Removed pen releases the guide, transfer and animal without crashing " + phase);
					Next(w);
				}
			}
			else if (phase == 7)
			{
				var worker = farmer.Trait<MedievalWorker>();
				if (!removedField && worker.IsHarvesting)
				{
					removedField = true; Check(true, "Farmer is harvesting before field removal"); field.Dispose();
					w.AddFrameEndTask(world =>
					{
						Check(field.Disposed && !worker.IsHarvesting, "Harvest animation safely stops when the field has been disposed");
					});
				}
				if (age == 450)
				{
					Check(removedField && worker.WorkArea == null && !worker.IsWorking, "Farmer clears a destroyed working field and remains available");
					Finish();
				}
			}
		}
	}
}
