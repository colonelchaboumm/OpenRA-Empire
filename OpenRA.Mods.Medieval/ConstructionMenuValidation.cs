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
	public class MedievalConstructionMenuValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalConstructionMenuValidation();
	}
	// Opt-in native test of menu bindings, real placement orders and recruitment.
	public sealed class MedievalConstructionMenuValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor market;
		int tick, checkedItems;
		bool done;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "construction-menu"); Directory.CreateDirectory(output);
		}
		Actor Spawn(World w, string name, int x, int y, Player owner = null) => w.CreateActor("md." + name, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>("MD_MENU_" + id);
			Check(b.IsVisible() && !b.IsDisabled(), "Unavailable menu button " + id); b.OnClick();
		});
		void Click(World w, int x, int y) => Sync.RunUnsynced(w, () =>
		{
			var generator = (OrderGenerator)w.OrderGenerator;
			foreach (var order in generator.Order(w, Cell(x, y), int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray()) w.IssueOrder(order);
		});
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			market = Spawn(w, "market", 20, 23); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			var depot = Spawn(w, "warehouse", 20, 28); depot.Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "house", 24, 23); Spawn(w, "sheepfold", 27, 28); Spawn(w, "tentweaver", 27, 23);
			Spawn(w, "militarytent", 32, 26);
			var foe = w.Players.First(p => p.InternalName == "Creeps");
			MedievalHeraldry.For(foe).SetEmpire("Azur", 0x347ACA, 0);
			Spawn(w, "militarytent", 32, 30, foe);
			MedievalHeraldry.For(w.LocalPlayer).SetEmpire("Rubis", 0xCA3838, 0);
		}
		void InspectMenu(World w) => Sync.RunUnsynced(w, () =>
		{
			for (var c = 0; c < MedievalConstructionMenuLogic.Categories.Length; c++)
			{
				Button(w, "TAB_" + c);
				var tab = Ui.Root.Get<ButtonWidget>("MD_MENU_TAB_" + c);
				Check(tab.IsHighlighted(), "Category must highlight " + c);
				Check(Game.Renderer.Fonts[tab.Font].Measure(tab.GetText()).X <= tab.Bounds.Width - 6, "Tab text clipped " + c);
				var items = MedievalConstructionMenuLogic.Categories[c].Items;
				for (var j = 0; j < (items.Length + 7) / 8 * 8; j++)
				{
					if (j > 0 && j % 8 == 0) Button(w, "NEXT");
					var i = j % 8;
					var b = Ui.Root.Get<ButtonWidget>("MD_MENU_ITEM_" + i);
					Check(b.IsVisible() == (j < items.Length), "Item visibility " + c + "/" + j);
					if (j >= items.Length) continue;
					var label = Ui.Root.Get<LabelWidget>($"MD_MENU_ITEM_{i}_NAME");
					Check(label.GetText() == items[j].Name, "Wrong item label " + c + "/" + j);
					Check(Game.Renderer.Fonts[label.Font].Measure(label.GetText()).X <= label.Bounds.Width, "Item text clipped: " + label.GetText());
					var cost = Ui.Root.Get<LabelWidget>($"MD_MENU_ITEM_{i}_COST");
					Check(Game.Renderer.Fonts[cost.Font].Measure(cost.GetText()).X <= cost.Bounds.Width, "Cost text clipped: " + cost.GetText());
					Check(Ui.Root.Get<SpriteWidget>($"MD_MENU_ITEM_{i}_ICON").GetSprite() != null, "Missing icon " + items[j].Type);
					checkedItems++;
					if (c == 5) continue;
					Button(w, "ITEM_" + i);
					var type = items[j].Type;
					Check(AgriculturalZone.IsKind(type) ? w.OrderGenerator is MedievalZoneOrderGenerator
						: MedievalFortifications.Types.Contains(type) ? w.OrderGenerator is MedievalFortificationOrderGenerator
						: MedievalDecorations.Types.Contains(type) ? w.OrderGenerator is MedievalDecorationOrderGenerator
						: w.OrderGenerator is MedievalPlaceOrderGenerator, "Wrong placement tool " + type);
				}
			}
			Button(w, "TAB_3"); Button(w, "ITEM_5"); Button(w, "ROTATE");
			Check(w.OrderGenerator is MedievalFortificationOrderGenerator f && f.Kind == "md.woodgatey", "Gate rotation unavailable");
			Button(w, "TAB_4"); Button(w, "ITEM_2"); Button(w, "ROTATE");
			Check(w.OrderGenerator is MedievalDecorationOrderGenerator d && d.Kind == "md.gardengatey", "Garden gate rotation unavailable");
			Button(w, "REMOVE"); Check(w.OrderGenerator is MedievalDemolitionOrderGenerator, "Demolition unavailable");
			Check(!Ui.Root.Get("MD_BUILD_0").IsVisible() && !Ui.Root.Get("MD_RECRUIT_0").IsVisible(), "Old sidebar overlaps new categories");
			Button(w, "TAB_1");
		});
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(28, 28))));
			if (tick == 20) InspectMenu(w);
			if (tick == 50) Game.Renderer.SaveScreenshot(Path.Combine(output, "nourriture.png"));
			if (tick == 60) Button(w, "TAB_3");
			if (tick == 80) Game.Renderer.SaveScreenshot(Path.Combine(output, "militaire.png"));
			if (tick == 90) w.IssueOrder(new Order("MedievalSetEmpireIdentity", market, false) { ExtraData = 0x3A9950, TargetString = "Jade" });
			if (tick == 110) Game.Renderer.SaveScreenshot(Path.Combine(output, "pavillons-couleur.png"));
			if (tick == 120) { Button(w, "TAB_1"); Button(w, "ITEM_4"); Click(w, 30, 35); }
			if (tick == 150) Check(w.Actors.Any(a => a.Info.Name == "md.sheepfold" && a.Location == Cell(30, 35) && !a.Trait<MedievalBuilding>().Complete), "Sheepfold menu placement failed: "
				+ w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().Message + " Occupants: " + string.Join(", ", w.Map.Rules.Actors["md.sheepfold"].TraitInfo<BuildingInfo>().Tiles(Cell(30,35)).SelectMany(w.ActorMap.GetActorsAt).Select(a=>a.Info.Name)));
			if (tick == 160) { Button(w, "TAB_3"); Button(w, "ITEM_3"); Click(w, 35, 35); }
			if (tick == 190) Check(w.Actors.Any(a => a.Info.Name == "md.tentweaver" && a.Location == Cell(35, 35) && !a.Trait<MedievalBuilding>().Complete), "Tent workshop menu placement failed");
			if (tick == 200) { Button(w, "TAB_1"); Button(w, "ITEM_5"); Click(w, 34, 28); Click(w, 37, 31); }
			if (tick == 225) Check(w.Actors.Any(a => a.Info.Name == "md.sheeppasture.4x4" && a.Location == Cell(34, 28)), "Variable sheep enclosure menu placement failed: "
				+ w.LocalPlayer.PlayerActor.Trait<MedievalSettlement>().Message + " Occupants: " + string.Join(", ", w.Map.Rules.Actors["md.sheeppasture.4x4"].TraitInfo<BuildingInfo>().Tiles(Cell(34, 28)).SelectMany(w.ActorMap.GetActorsAt).Select(a => a.Info.Name)));
			if (tick == 230) { Button(w, "TAB_5"); Button(w, "ITEM_6"); }
			if (tick == 250) Game.Renderer.SaveScreenshot(Path.Combine(output, "citoyens.png"));
			if (tick == 350) { Check(w.Actors.Any(a => a.Info.Name == "md.shepherd"), "Shepherd recruitment failed"); Button(w, "ITEM_7"); }
			if (tick == 460) Check(w.Actors.Any(a => a.Info.Name == "md.weaver"), "Weaver recruitment failed");
			if (tick == 470) Sync.RunUnsynced(w, () => Ui.Root.Get<ButtonWidget>("MD_CAMP_OPEN").OnClick());
			if (tick == 490) Game.Renderer.SaveScreenshot(Path.Combine(output, "campements.png"));
			if (tick < 510) return;
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, checkedItems }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL CONSTRUCTION MENU: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
