using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalLogisticsValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalLogisticsValidation();
	}
	// Isolated native-engine regression: nearest supplies, staffing, actual work and equipment logistics.
	public sealed class MedievalLogisticsValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor depot, market, stall, site, forge, barracks;
		int tick, stage, until, hires, trialStart, singleTicks, tripleTicks, paidFood;
		bool finished, recruiterSword, recruiterArmor;
		readonly HashSet<uint> workingSmiths = [];
		readonly int[] recipes = new int[4];
		readonly List<string> errors = [];
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World world, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "logistics-preview"); Directory.CreateDirectory(output);
		}
		Actor Spawn(World w, string type, int x, int y, bool construction = false)
		{
			var init = new TypeDictionary { new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y)) };
			if (construction) init.Add(new MedievalConstructionInit());
			return w.CreateActor("md." + type, init);
		}
		void Select(World w, Actor actor) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [actor], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var button = Ui.Root.Get<ButtonWidget>(id); Check(!button.IsDisabled(), "Button should be enabled: " + id); button.OnClick();
		});
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			depot = Spawn(w, "warehouse", 20, 28);
			market = Spawn(w, "market", 28, 28);
			stall = Spawn(w, "marketstall", 36, 28);
			var stocks = new[] { new[] { 400, 250, 200, 300 }, new[] { 60, 200, 0, 40 }, new[] { 60, 80, 0, 60 } };
			var buildings = new[] { depot, market, stall };
			for (var i = 0; i < 3; i++) for (var r = 0; r < 4; r++) buildings[i].Trait<MedievalStockpile>().Stock.Add(r, stocks[i][r]);
			for (var i = 0; i < 3; i++) Spawn(w, "builder", 36 + i, 34);
			site = Spawn(w, "house", 39, 30, true); stage = 1;
		}
		void StopSmiths(World w)
		{
			foreach (var smith in forge.Trait<MedievalMilitaryWorkshop>().Team(forge)) w.IssueOrder(new Order("Stop", smith, false));
		}
		void Trial(World w, int workers)
		{
			var workshop = forge.Trait<MedievalMilitaryWorkshop>();
			for (var r = 0; r < 4; r++) workshop.Inputs.Add(r, depot.Trait<MedievalStockpile>().Stock.Take(r, MedievalEquipment.Recipes[0][r]));
			workshop.Product = 0; workshop.Progress = 0;
			foreach (var smith in workshop.Team(forge).Take(workers)) w.IssueOrder(new Order("MedievalWork", smith, false));
			trialStart = tick;
		}
		void SetTargets(World w, int[] values)
		{
			var economy = w.LocalPlayer.PlayerActor.Trait<MedievalEquipmentEconomy>();
			for (var r = 0; r < 3; r++)
				for (var i = 0; i < Math.Abs(economy.Target(r) - values[r]); i++)
					w.IssueOrder(new Order("MedievalEquipmentTarget", w.LocalPlayer.PlayerActor, false) { ExtraData = (uint)r, TargetString = values[r] > economy.Target(r) ? "+" : "-" });
		}
		int Physical(World w, int r) => w.Actors.Where(a => a.Owner == w.LocalPlayer && a.IsInWorld && !a.IsDead).Sum(a =>
			(a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(r) ?? 0) + (a.TraitOrDefault<MedievalBuilding>()?.Materials.Amount(r) ?? 0)
			+ (a.TraitOrDefault<MedievalMilitaryWorkshop>()?.Inputs.Amount(r) ?? 0)
			+ (a.TraitOrDefault<MedievalWorker>() is { EquipmentCargo: false } worker && worker.Resource == r ? worker.Cargo : 0));
		void Conservation(World w, int soldierFood = 0, int soldierMeat = 0)
		{
			for (var r = 0; r < 4; r++)
			{
				var total = Physical(w, r) + recipes[r];
				if (r == 1) total += paidFood + soldierFood + w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
				if (r == 2) total += soldierMeat;
				Check(total == new[] { 520, 530, 200, 400 }[r], $"Physical resources must be conserved: resource {r}, total {total}.");
			}
		}
		void Finish(World w)
		{
			Check(stage == 12, "Every logistics stage must finish.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, singleTicks, tripleTicks,
				workingSmiths = workingSmiths.Count, recruiterSword, recruiterArmor, errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL LOGISTICS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(34, 30)));
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 10)
			{
				Check(MedievalConstructionSupply.Sources(w.LocalPlayer, site.CenterPosition).First() == stall, "Nearest stall must precede the warehouse and main market.");
				Check(MedievalConstructionSupply.CanSupply(depot, w.LocalPlayer, site.CenterPosition), "Warehouse must be in range for the priority comparison.");
			}
			if (stage == 1 && MedievalStockpile.Active(site))
			{
				Check(stall.Trait<MedievalStockpile>().Stock.Amount(0) == 0 && stall.Trait<MedievalStockpile>().Stock.Amount(3) == 40
					&& depot.Trait<MedievalStockpile>().Stock.Amount(0) == 400 && market.Trait<MedievalStockpile>().Stock.Amount(0) == 60, "First house must use exactly the nearest stall's wood and stone.");
				w.AddFrameEndTask(_ => site = Spawn(w, "house", 31, 24, true)); stage = 2;
			}
			else if (stage == 2 && MedievalStockpile.Active(site))
			{
				Check(market.Trait<MedievalStockpile>().Stock.Amount(0) == 0 && market.Trait<MedievalStockpile>().Stock.Amount(3) == 20
					&& depot.Trait<MedievalStockpile>().Stock.Amount(0) == 400, "Second house must use the nearer main market, not the depot.");
				foreach (var builder in w.ActorsWithTrait<MedievalWorker>()) w.IssueOrder(new Order("Stop", builder.Actor, false));
				w.AddFrameEndTask(_ => forge = Spawn(w, "forge", 35, 32)); stage = 3; until = tick + 10;
			}
			if (stage == 3 && tick >= until && forge != null)
			{
				Select(w, forge);
				if (hires < 3) { Button(w, "MD_FORGE_STAFF"); hires++; paidFood += 10; until = tick + 140; }
				else
				{
					var workshop = forge.Trait<MedievalMilitaryWorkshop>();
					Check(workshop.StaffCount(forge) == 3 && !workshop.CanHire(forge), "Forge must hold exactly three artisans and reject a fourth.");
					Check(Ui.Root.Get<ButtonWidget>("MD_FORGE_STAFF").IsDisabled(), "Hire button must be disabled at three smiths.");
					Check(MedievalSettlement.Population(w.LocalPlayer) == 6 && MedievalFoodEconomy.Demand(w.LocalPlayer) == 12, "All three smiths must occupy housing and eat.");
					w.IssueOrder(new Order("MedievalHireArtisan", forge, false)); StopSmiths(w); stage = 4; until = tick + 20;
				}
			}
			if (stage == 4 && tick >= until)
			{
				Conservation(w); Check(forge.Trait<MedievalMilitaryWorkshop>().StaffTraining == 0, "A fourth hire order must not charge food or begin training.");
				Trial(w, 1); stage = 5;
			}
			if (stage is 5 or 7)
			{
				var workshop = forge.Trait<MedievalMilitaryWorkshop>();
				if (stage == 7) foreach (var smith in workshop.Team(forge).Where(a => a.Trait<MedievalWorker>().IsWorking)) workingSmiths.Add(smith.ActorID);
				if (workshop.Product < 0)
				{
					for (var r = 0; r < 4; r++) recipes[r] += MedievalEquipment.Recipes[0][r];
					if (stage == 5) { singleTicks = tick - trialStart; stage = 6; }
					else { tripleTicks = tick - trialStart; stage = 8; }
				}
			}
			if (stage == 6 && stall.Trait<MedievalEquipmentStore>().Stock.Amount(0) == 1) { Trial(w, 3); stage = 7; }
			if (stage == 8 && stall.Trait<MedievalEquipmentStore>().Stock.Amount(0) == 2)
			{
				Check(tripleTicks < singleTicks * 3 / 4 && workingSmiths.Count == 3, "Three real workers must forge faster than one worker.");
				Conservation(w);
				SetTargets(w, [3, 1, 1]);
				w.AddFrameEndTask(_ => { for (var i = 0; i < 3; i++) Spawn(w, "hauler", 30 + i, 34); });
				stage = 9;
			}
			if (stage == 9 && Enumerable.Range(0, 3).All(r => MedievalEquipment.Amount(w.LocalPlayer, r) == new[] { 3, 1, 1 }[r]))
			{
				var stock = stall.Trait<MedievalEquipmentStore>().Stock;
				Check(Enumerable.Range(0, 3).All(r => stock.Amount(r) == new[] { 3, 1, 1 }[r]), "Smiths must deliver each equipment type to the closest small market.");
				Check(Enumerable.Range(0, 3).All(r => MedievalEquipment.Committed(w.LocalPlayer, r) == new[] { 3, 1, 1 }[r]), "Three smiths must not exceed the common equipment targets.");
				for (var r = 0; r < 4; r++) recipes[r] += MedievalEquipment.Recipes.Sum(recipe => recipe[r]);
				Conservation(w); Select(w, stall);
				Check(Ui.Root.Get<LabelWidget>("MD_STALL_EQUIPMENT").GetText() == "Arcs 3 | Epees 1 | Armures 1", "Stall panel must show its actual equipment stock.");
				Check(((IRender)stall.Trait<MedievalEquipmentStore>()).Render(stall, renderer).Count() == 8, "Equipment display must contain three pallets and exactly five delivered items.");
				SetTargets(w, [0, 0, 0]); stage = 10; until = tick + 150;
			}
			if (stage == 10 && tick >= until)
			{
				Game.Renderer.SaveScreenshot(Path.Combine(output, "market-equipment.png"));
				w.AddFrameEndTask(_ => barracks = Spawn(w, "barracks", 41, 34)); stage = 11; until = tick + 20;
			}
			if (stage == 11 && tick == until) { Select(w, barracks); Button(w, "MD_BARRACKS_STAFF"); paidFood += 10; }
			if (stage == 11 && tick == until + 160) Button(w, "MD_BARRACKS_RECRUIT_3");
			if (stage == 11 && barracks != null)
			{
				var recruiter = barracks.Trait<MedievalMilitaryWorkshop>().Staff(barracks);
				if (recruiter?.Trait<MedievalWorker>() is { Cargo: > 0, EquipmentCargo: true } worker && MedievalAccess.Adjacent(recruiter, stall))
				{ recruiterSword |= worker.Resource == 1; recruiterArmor |= worker.Resource == 2; }
				if (w.Actors.Any(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.manatarms"))
				{
					Check(recruiterSword && recruiterArmor, "Recruiter must physically collect sword and armor from the small market.");
					var cost = MedievalSettlement.Cost(w.LocalPlayer, "md.manatarms"); Conservation(w, cost.Food, cost.Meat);
					Check(stall.Trait<MedievalEquipmentStore>().Stock.Amount(1) == 0 && stall.Trait<MedievalEquipmentStore>().Stock.Amount(2) == 0, "Recruitment must consume the delivered equipment exactly once.");
					Select(w, forge); stage = 12; until = tick + 150;
				}
			}
			if (stage == 12 && tick >= until) { Game.Renderer.SaveScreenshot(Path.Combine(output, "three-smiths.png")); Finish(w); }
			else if (tick == 18000) Finish(w);
			if (tick % 1000 == 0) File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { tick, stage, hires, singleTicks, tripleTicks, errors,
				workshop = forge?.Trait<MedievalMilitaryWorkshop>().Status, team = forge?.Trait<MedievalMilitaryWorkshop>().Team(forge).Select(a => a.Trait<MedievalWorker>().Description).ToArray() }));
		}
	}
}
