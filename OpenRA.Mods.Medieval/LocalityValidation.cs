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
	public class MedievalLocalityValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalLocalityValidation();
	}
	public sealed class MedievalLocalityValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor city, secondCityMarket, localStall, village, villageStall, villageMarket, trader;
		int tick, stage, until, cityId, villageId, paidFood, consumedBefore;
		bool finished, realBuilder, realDelivery, inTransit;
		readonly List<string> errors = [];
		readonly int[] destroyedStock = new int[4];
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "locality-preview"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, int x, int y, int[] stock = null) => w.CreateActor("md." + type,
			stock == null ? [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))] : [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y)), new MedievalStockInit(stock)]);
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var button = Ui.Root.Get<ButtonWidget>(id); Check(!button.IsDisabled(), "Button must be enabled: " + id); button.OnClick();
		});
		void Target(World w, Actor origin, string button, CPos cell)
		{
			Select(w, origin); Button(w, button);
			Sync.RunUnsynced(w, () =>
			{
				if (w.OrderGenerator is not OrderGenerator generator) { Check(false, "Missing targeting tool: " + button); return; }
				var orders = generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray();
				Check(orders.Length == 1, "One placement/link order required: " + button); foreach (var order in orders) w.IssueOrder(order);
			});
		}
		void Rename(World w, Actor a, string name)
		{
			Select(w, a);
			Sync.RunUnsynced(w, () => { Ui.Root.Get<TextFieldWidget>("MD_LOCALITY_INPUT").Text = name; Button(w, "MD_LOCALITY_RENAME"); });
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Spawn(w, "warehouse", 10, 28, [200, 250, 80, 200]); Spawn(w, "warehouse", 41, 26, [80, 120, 30, 80]);
			city = Spawn(w, "market", 14, 28, [0, 100, 0, 0]); secondCityMarket = Spawn(w, "market", 18, 37, [0, 60, 0, 0]);
			localStall = Spawn(w, "marketstall", 22, 28);
			Spawn(w, "builder", 43, 30); Spawn(w, "hauler", 16, 32); Spawn(w, "hauler", 18, 32); Spawn(w, "farmer", 46, 33);
			consumedBefore = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
		}
		void Conservation(World w)
		{
			var total = new int[4];
			foreach (var a in w.Actors.Where(a => a.IsInWorld && !a.IsDead))
			{
				for (var r = 0; r < 4; r++) total[r] += (a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(r) ?? 0) + (a.TraitOrDefault<MedievalBuilding>()?.Materials.Amount(r) ?? 0);
				if (a.TraitOrDefault<MedievalWorker>() is { EquipmentCargo: false, Cargo: > 0 } worker) total[worker.Resource] += worker.Cargo;
			}
			total[1] += paidFood + w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed - consumedBefore;
			for (var r = 0; r < 4; r++) Check(total[r] + destroyedStock[r] == new[] { 280, 530, 110, 280 }[r], $"Physical resource conservation failed for {r}: {total[r]} + {destroyedStock[r]} destroyed.");
		}
		void Finish(World w)
		{
			Check(stage == 15, "All locality stages must finish."); Conservation(w);
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, realBuilder, realDelivery, inTransit, errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL LOCALITIES: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (stage < 9) renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 31)));
			var registry = MedievalLocalities.For(w.LocalPlayer);
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 40)
			{
				cityId = MedievalLocalities.Id(city);
				Check(cityId > 0 && MedievalLocalities.Id(localStall) == cityId && MedievalLocalities.Id(secondCityMarket) == cityId, "Nearby markets must form one city.");
				Rename(w, city, "  Montbois  "); Target(w, city, "MD_BUILD_9", Cell(44, 31)); stage = 1;
			}
			if (stage == 1)
			{
				village = w.Actors.FirstOrDefault(a => a.Info.Name == "md.marketstall" && a.Location == Cell(44, 31));
				if (village != null)
				{
					Check(MedievalLocalities.Id(village) == 0 && !MedievalStockpile.Active(village), "An unfinished stall must not found a village or accept a name.");
					w.IssueOrder(new Order("MedievalRenameLocality", village, false) { TargetString = "Unfinished" }); stage = 2;
				}
			}
			if (stage == 2)
			{
				realBuilder |= w.ActorsWithTrait<MedievalWorker>().Any(t => t.Trait.Info.Profession == "builder" && t.Trait.Cargo > 0);
				if (MedievalStockpile.Active(village) && MedievalLocalities.Id(village) > 0)
				{
					villageId = MedievalLocalities.Id(village);
					Check(realBuilder && villageId != cityId && !registry.City(w.LocalPlayer, villageId), "A real completed remote stall must establish a separate village.");
					Check(registry.Name(cityId) == "Montbois" && registry.Name(villageId) != "Unfinished", "Rename must normalize names and reject unfinished sites.");
					Rename(w, village, "Val-des-Prés"); w.AddFrameEndTask(_ => villageStall = Spawn(w, "marketstall", 48, 33)); stage = 3; until = tick + 40;
				}
			}
			if (stage == 3 && tick >= until)
			{
				Check(MedievalLocalities.Id(villageStall) == villageId && registry.Name(villageId) == "Val-des-Prés", "A neighborhood stall must share the village name.");
				Select(w, city);
				Sync.RunUnsynced(w, () =>
				{
					var input = Ui.Root.Get<TextFieldWidget>("MD_LOCALITY_INPUT"); input.Text = "val-des-prés";
					Check(Ui.Root.Get<ButtonWidget>("MD_LOCALITY_RENAME").IsDisabled(), "The UI must reject duplicate locality names.");
					input.Text = " "; Check(Ui.Root.Get<ButtonWidget>("MD_LOCALITY_RENAME").IsDisabled(), "The UI must reject empty names.");
				});
				w.IssueOrder(new Order("MedievalRenameLocality", city, false) { TargetString = "val-des-prés" });
				var other = w.Players.First(p => p.InternalName == "Creeps");
				Check(!MedievalLocalities.For(other).Rename(city, "Stolen"), "Another player cannot rename a settlement.");
				Target(w, village, "MD_STALL_RELINK", city.Location); stage = 4; until = tick + 35;
			}
			if (stage == 4 && tick >= until)
			{
				Check(registry.Name(cityId) == "Montbois", "The simulation must also reject a duplicate name.");
				Check(village.Trait<MedievalMarketStall>().Market == city && Ui.Root.Get<LabelWidget>("MD_STALL_LINK").GetText().Contains("Montbois"), "Relay assignment must show the named supplier.");
				Conservation(w); Button(w, "MD_PLACES_OPEN"); Button(w, "MD_PLACES_ROUTES"); stage = 5; until = tick + 20;
			}
			if (stage == 5 && tick >= until)
			{
				Check(Ui.Root.Get("MD_PLACES_PANEL").IsVisible(), "Locality manager must open without a selection.");
				Check(Enumerable.Range(0, 6).Any(i => Ui.Root.Get<LabelWidget>($"MD_PLACES_NAME_{i}").GetText() is string text && text.Contains("Montbois") && text.Contains("Val-des-Prés")), "Route overview must show both settlement names.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "named-relays.png"));
				w.AddFrameEndTask(_ => villageMarket = Spawn(w, "market", 44, 36)); stage = 6; until = tick + 40;
			}
			if (stage == 6 && tick >= until)
			{
				Check(MedievalLocalities.Id(villageMarket) == villageId && registry.Name(villageId) == "Val-des-Prés" && registry.City(w.LocalPlayer, villageId), "Adding a grand market must preserve the established name and upgrade the village to a city.");
				Target(w, city, "MD_TRADE_LINK", villageMarket.Location); stage = 7; until = tick + 30;
			}
			if (stage == 7 && tick >= until)
			{
				Check(Ui.Root.Get<LabelWidget>("MD_TRADE_PARTNER").GetText().Contains("Montbois") && Ui.Root.Get<LabelWidget>("MD_TRADE_PARTNER").GetText().Contains("Val-des-Prés"), "Commercial route panel must show the city names.");
				Button(w, "MD_TRADE_CREATE"); paidFood = 20; stage = 8;
			}
			if (stage == 8)
			{
				trader ??= w.Actors.FirstOrDefault(a => a.Info.Name == "md.tradecart");
				if (trader != null && trader.Trait<MedievalStockpile>().Stock.Amount(1) > 0 && !MedievalAccess.Adjacent(trader, city))
				{
					inTransit = true; Rename(w, village, "Port-des-Prés"); stage = 9; until = tick + 30;
				}
			}
			if (stage == 9 && tick >= until)
			{
				Check(trader.Trait<MedievalTradeCart>().Active && trader.Trait<MedievalTradeCart>().RouteName(trader).Contains("Port-des-Prés"), "Renaming in transit must preserve the active route and update its names.");
				Select(w, village); renderer.Viewport.Center(village.CenterPosition); stage = 10; until = tick + 15;
			}
			if (stage == 10 && tick >= until)
			{
				if (tick == until) Game.Renderer.SaveScreenshot(Path.Combine(output, "village-name.png"));
				if (trader.Trait<MedievalTradeCart>().Delivered > 0)
				{
					realDelivery = true; Conservation(w);
					for (var r = 0; r < 4; r++) destroyedStock[r] += village.Trait<MedievalStockpile>().Stock.Amount(r) + village.Trait<MedievalBuilding>().Materials.Amount(r);
					village.Dispose(); stage = 11; until = tick + 40;
				}
			}
			if (stage == 11 && tick >= until)
			{
				Check(MedievalLocalities.Id(villageStall) == villageId && registry.Name(villageId) == "Port-des-Prés", "Surviving markets must retain the name after the founding stall is lost.");
				villageStall.ChangeOwner(w.Players.First(p => p.InternalName == "Creeps")); stage = 12; until = tick + 40;
			}
			if (stage == 12 && tick >= until)
			{
				Check(MedievalLocalities.For(villageStall.Owner).Name(MedievalLocalities.Id(villageStall)) != "Port-des-Prés", "A captured stall must join its new owner's settlement registry.");
				Check(registry.Name(villageId) == "Port-des-Prés", "Capture must not rename the former owner's remaining market.");
				w.AddFrameEndTask(_ => { foreach (var (x, y) in new[] { (10, 8), (22, 8), (34, 8), (46, 8), (10, 55), (24, 55), (38, 55), (52, 55) }) Spawn(w, "marketstall", x, y); });
				stage = 13; until = tick + 45;
			}
			if (stage == 13 && tick >= until)
			{
				Button(w, "MD_PLACES_OPEN"); Button(w, "MD_PLACES_TOWNS"); Button(w, "MD_PLACES_NEXT"); stage = 14; until = tick + 20;
			}
			if (stage == 14 && tick >= until)
			{
				Check(Ui.Root.Get<LabelWidget>("MD_PLACES_COUNT").GetText().Contains("Page 2/"), "Many settlements must be accessible through pagination.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "locality-directory.png")); Button(w, "MD_PLACES_VIEW_0");
				Check(w.Selection.Actors.SingleOrDefault()?.TraitOrDefault<MedievalLocality>() != null, "View must select and locate the chosen settlement's market."); stage = 15; Finish(w);
			}
			if (tick > 8000) { errors.Add("Timeout at stage " + stage); Finish(w); }
		}
	}
}
