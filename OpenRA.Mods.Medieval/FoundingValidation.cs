using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalFoundingValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFoundingValidation();
	}
	public sealed class MedievalFoundingValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor caravan, blocker, market, warehouse;
		MedievalFoundingCaravan founding;
		CPos start, moveTo, origin;
		int tick, stage, at, recruited, expectedFood;
		bool rejected, cancelled, transferred, moved, finished;
		readonly List<string> errors = [];
		string output;
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "founding"); Directory.CreateDirectory(output);
		}
		void Deploy(World w)
		{
			Sync.RunUnsynced(w, () =>
			{
				w.Selection.Combine(w, [caravan], false, true);
				var button = Ui.Root.Get<ButtonWidget>("MD_FOUNDING_DEPLOY");
				Check(!button.IsDisabled(), "The founding button must be available."); button.OnClick();
			});
		}
		void Place(World w, string type, CPos pos) => w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor, Target.FromCell(w, pos), false) { TargetString = "md." + type });
		void Next(int next) { stage = next; at = tick; }
		void Finish(World w)
		{
			var economy = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>();
			var buildings = w.ActorsHavingTrait<MedievalBuilding>().Where(a => a.Owner == w.LocalPlayer).Select(a => new { a.Info.Name, cell = a.Location.ToString(), a.Trait<MedievalBuilding>().Complete,
				a.Trait<MedievalBuilding>().Progress, materials = Enumerable.Range(0, Inventory.ResourceCount).Select(a.Trait<MedievalBuilding>().Materials.Amount).ToArray() }).ToArray();
			var workers = w.ActorsHavingTrait<MedievalWorker>().Where(a => a.Owner == w.LocalPlayer).Select(a => new { a.Info.Name, cell = a.Location.ToString(), a.Trait<MedievalWorker>().Status, a.Trait<MedievalWorker>().Cargo }).ToArray();
			var foundation = origin + new CVec(4, -1);
			var foundationCells = w.Map.Rules.Actors["md.warehouse"].TraitInfo<BuildingInfo>().Tiles(foundation).Select(c => new { cell = c.ToString(), explored = w.LocalPlayer.Shroud.IsExplored(c),
				actors = w.ActorMap.GetActorsAt(c).Select(a => a.Info.Name).ToArray() }).ToArray();
			if (errors.Count > 0) Game.Renderer.SaveScreenshot(Path.Combine(output, "failure.png"));
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, errors,
				buildings, workers, foundationCells, placementAvailable = MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.warehouse", foundation), message = w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().Message,
				rejected, cancelled, moved, transferred, recruited, harvest = economy.Harvests.Total, delivered = economy.Deliveries.Total,
				population = MedievalSettlement.Population(w.LocalPlayer), housing = MedievalSettlement.Housing(w.LocalPlayer), consumed = economy.TotalConsumed }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL FOUNDING: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick > 7000) { Check(false, "Starting economy stalled at stage " + stage); Finish(w); return; }
			if (caravan != null) Sync.RunUnsynced(w, () => renderer.Viewport.Center(market?.CenterPosition ?? caravan.CenterPosition));
			if (stage == 0)
			{
				caravan = w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.caravan");
				if (caravan == null) { Check(false, "Playable map must start with a caravan."); Finish(w); return; }
				founding = caravan.Trait<MedievalFoundingCaravan>(); start = caravan.Location; moveTo = start + new CVec(6, 0);
				Check(w.Actors.Count(a => a.Owner == w.LocalPlayer && a.Info.HasTraitInfo<MedievalStockpileInfo>()) == 1, "No prebuilt village may accompany the caravan.");
				Check(MedievalSettlement.Population(w.LocalPlayer) == 3 && MedievalSettlement.Housing(w.LocalPlayer) == 3, "The packed settlers need three places.");
				Check(MedievalFoodEconomy.Demand(w.LocalPlayer) == 6, "The three settlers must eat while travelling.");
				w.AddFrameEndTask(_ => blocker = w.CreateActor("md.stone", [new OwnerInit(w.Players.Single(p => p.InternalName == "Neutral")), new LocationInit(founding.Foundation(caravan))]));
				Next(1);
			}
			else if (stage == 1 && tick-at == 3) { Check(!founding.CanFound(caravan), "A rock must block deployment."); Deploy(w); }
			else if (stage == 1 && tick-at > 12)
			{
				rejected = caravan.IsInWorld && !w.ActorsHavingTrait<MedievalMarket>().Any(); Check(rejected, "An invalid deployment created a market.");
				w.AddFrameEndTask(_ => blocker.Dispose()); Next(2);
			}
			else if (stage == 2 && tick-at == 4) Deploy(w);
			else if (stage == 2 && tick-at == 29) { Game.Renderer.SaveScreenshot(Path.Combine(output, "caravan.png")); w.IssueOrder(new Order("Stop", caravan, false)); }
			else if (stage == 2 && tick-at > 40)
			{
				cancelled = caravan.IsInWorld && founding.Progress == 0 && !w.ActorsHavingTrait<MedievalMarket>().Any(); Check(cancelled, "Stop must cancel founding without losing the caravan.");
				w.IssueOrder(new Order("Move", caravan, Target.FromCell(w, moveTo), false)); Next(3);
			}
			else if (stage == 3 && caravan.Location == moveTo && caravan.IsIdle)
			{
				moved = true; origin = founding.Foundation(caravan);
				caravan.Trait<MedievalStockpile>().Stock.Take(0, 1); caravan.Trait<MedievalStockpile>().Stock.Take(1, 9);
				expectedFood = caravan.Trait<MedievalStockpile>().Stock.Amount(1);
				caravan.Trait<Health>().InflictDamage(caravan, caravan, new Damage(1200), true);
				Deploy(w); Deploy(w); Next(4);
			}
			else if (stage == 4 && founding.FoundedMarket != null)
			{
				market = founding.FoundedMarket; var stock = market.Trait<MedievalStockpile>().Stock;
				transferred = !caravan.IsInWorld && stock.Amount(0) == 279 && stock.Amount(3) == 100 && stock.Amount(1) <= expectedFood && stock.Amount(1) >= expectedFood-1;
				Check(transferred, "Only the remaining physical cargo may transfer to the market.");
				Check(w.ActorsHavingTrait<MedievalMarket>().Count() == 1, "Repeated deploy orders must not duplicate markets.");
				Check(w.ActorsHavingTrait<MedievalWorker>().Count() == 3, "Exactly two builders and one hauler must unpack.");
				Check(market.Trait<Health>().HP < market.Trait<Health>().MaxHP, "Deploying must not heal damage.");
				Check(MedievalSettlement.Population(w.LocalPlayer) == 3 && MedievalSettlement.Housing(w.LocalPlayer) == 3, "Unpacking must preserve population and camp housing.");
				Place(w, "warehouse", origin + new CVec(4, -1)); Next(5);
			}
			else if (stage == 5)
			{
				warehouse = MedievalSettlement.Warehouses(w.LocalPlayer).FirstOrDefault(); if (warehouse == null) return;
				Place(w, "sawmill", origin + new CVec(-7, -2)); Place(w, "quarry", origin + new CVec(7, 4));
				Place(w, "farm", origin + new CVec(-4, 7)); Next(6);
			}
			else if (stage == 6)
			{
				var settlement = w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>();
				if (recruited < 3 && !settlement.Recruiting && tick-at > 8)
				{
					w.IssueOrder(new Order("MedievalRecruit", w.LocalPlayer.PlayerActor, false) { TargetString = new[] { "md.lumberjack", "md.stonecutter", "md.farmer" }[recruited++] }); at = tick;
				}
				var farm = w.Actors.FirstOrDefault(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.farm" && MedievalStockpile.Active(a));
				if (recruited == 3 && !settlement.Recruiting && farm != null) { Place(w, "field", farm.Location + new CVec(-1, 4)); Next(7); }
			}
			else if (stage == 7 && w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().Harvests.Total > 0 && w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().Deliveries.Total > 0
				&& w.ActorsHavingTrait<MedievalBuilding>().Where(a => a.Owner == w.LocalPlayer).All(a => a.Trait<MedievalBuilding>().Complete))
			{
				Check(w.ActorsHavingTrait<MedievalBuilding>().Where(a => a.Owner == w.LocalPlayer).All(a => a.Trait<MedievalBuilding>().Complete), "The starter reserves and production must finish all founding buildings.");
				Check(MedievalSettlement.Population(w.LocalPlayer) == 6, "All founders and recruited workers must survive.");
				foreach (var kind in new[] { NaturalResourceKind.Tree, NaturalResourceKind.Stone })
					Check(w.ActorsWithTrait<MedievalSource>().Any(p => p.Trait.Info.Natural == kind && p.Trait.Remaining < p.Trait.Info.Amount), "Founding workers must harvest " + kind);
				Game.Renderer.SaveScreenshot(Path.Combine(output, "first-village.png")); Finish(w);
			}
		}
	}
}
