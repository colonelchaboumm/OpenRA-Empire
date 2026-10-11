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
	public class MedievalVillageLifeValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalVillageLifeValidation();
	}
	public sealed class MedievalVillageLifeValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, carried, maximumWorking;
		bool done, paused, removeSent;
		Actor field, cows, sheep, depot, farm, walker, plaza;
		readonly List<Actor> farmers = [];
		readonly Dictionary<uint, HashSet<CPos>> worked = [];
		readonly HashSet<int> cowFrames = [], sheepFrames = [];
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		Actor Spawn(World w, string type, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "village-life"); Directory.CreateDirectory(output);
		}
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>("MD_MENU_" + id); Check(b.IsVisible() && !b.IsDisabled(), "Unavailable menu " + id); b.OnClick();
		});
		void Click(World w, CPos cell) => Sync.RunUnsynced(w, () =>
		{
			var g = (OrderGenerator)w.OrderGenerator;
			foreach (var o in g.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, g.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).ToArray()) w.IssueOrder(o);
		});
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			depot = Spawn(w, "warehouse", 24, 25); depot.Trait<MedievalStockpile>().Stock.Add(0, 300); depot.Trait<MedievalStockpile>().Stock.Add(3, 250); depot.Trait<MedievalStockpile>().Stock.Add(1, 100);
			var remote = Spawn(w, "warehouse", 40, 35).Trait<MedievalStockpile>().Stock; remote.Add(0, 100); remote.Add(3, 150);
			Spawn(w, "market", 26, 29).Trait<MedievalStockpile>().Stock.Add(1, 40);
			for (var i = 0; i < 3; i++) Spawn(w, "house", 20 + i * 3, 22);
			farm = Spawn(w, "farm", 20, 29); field = Spawn(w, "field.8x6", 20, 34);
			Spawn(w, "ranch", 32, 24); cows = Spawn(w, "pasture.6x5", 32, 28);
			Spawn(w, "sheepfold", 40, 24); sheep = Spawn(w, "sheeppasture.6x5", 40, 28);
			cows.Trait<MedievalSource>().Remaining = 210; sheep.Trait<MedievalSource>().Remaining = 56;
			for (var i = 0; i < 6; i++) { var f = Spawn(w, "farmer", 19 + i, 33); farmers.Add(f); worked[f.ActorID] = []; }
			for (var i = 0; i < 3; i++) Spawn(w, "hauler", 24 + i, 28);
			for (var i = 0; i < 5; i++) Spawn(w, "builder", 28 + i, 35);
			walker = Spawn(w, "builder", 39, 37); walker.Trait<MedievalWorker>().Automatic = false;
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			if (field == null) return;
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(32, 33))));
			var working = farmers.Where(a => a.Trait<MedievalWorker>().IsWorking).ToArray(); maximumWorking = Math.Max(maximumWorking, working.Length);
			foreach (var f in farmers)
			{
				var worker = f.Trait<MedievalWorker>();
				if (worker.Cargo > 0) carried++;
				if (!worker.IsWorking) continue;
				Check(new AgriculturalZone(field.Location, new CVec(8, 6)).Cells().Contains(f.Location), "Farmer must work inside the crop, never from its perimeter.");
				worked[f.ActorID].Add(f.Location);
			}
			Check(working.Select(a => a.Location).Distinct().Count() == working.Length, "Working farmers must occupy different cells.");
			for (var i = 0; i < 7; i++) { cowFrames.Add(cows.Trait<MedievalLivestock>().Pose(cows, i).Frame); sheepFrames.Add(sheep.Trait<MedievalLivestock>().Pose(sheep, i).Frame); }
			if (tick == 20)
			{
				Button(w, "TAB_4"); Button(w, "ITEM_7"); Click(w, Cell(31, 36)); Button(w, "NEXT");
				Check(Ui.Root.Get<LabelWidget>("MD_MENU_PAGE").GetText() == $"2/{(MedievalConstructionMenuLogic.Categories[4].Items.Length + 7) / 8}", "Decoration pagination must show page two.");
				for (var i = 0; i < 5; i++)
				{
					var b = Ui.Root.Get<ButtonWidget>("MD_MENU_ITEM_" + i); var label = Ui.Root.Get<LabelWidget>($"MD_MENU_ITEM_{i}_NAME");
					Check(b.IsVisible() && Game.Renderer.Fonts[label.Font].Measure(label.GetText()).X <= label.Bounds.Width, "Decoration label must fit " + label.GetText());
					Check(Ui.Root.Get<SpriteWidget>($"MD_MENU_ITEM_{i}_ICON").GetSprite() != null, "Decoration icon must exist.");
					Button(w, "ITEM_" + i); Click(w, Cell(35 + (i == 0 ? 0 : (i - 1) * 2), i == 0 ? 36 : 41));
				}
			}
			if (tick == 70) { Check(w.ActorsHavingTrait<MedievalDecoration>().Count() == 6, "All six new decorations must place construction sites through the menu."); Game.Renderer.SaveScreenshot(Path.Combine(output, "construction.png")); }
			if (tick == 450)
			{
				w.SetPauseState(true);
				Game.RunAfterDelay(100, () =>
				{
					var before = cows.Trait<MedievalLivestock>().Pose(cows, 0);
					Game.RunAfterDelay(150, () => { paused = before == cows.Trait<MedievalLivestock>().Pose(cows, 0); Check(paused, "Livestock must freeze during pause."); w.SetPauseState(false); });
				});
			}
			if (tick == 1300)
			{
				plaza = w.Actors.FirstOrDefault(a => a.Info.Name == "md.plazalarge");
				Check(plaza != null && MedievalStockpile.Active(plaza), "Large paved square must be built with physical materials.");
				if (plaza != null)
				{
					Check(MedievalDecorations.At(w, w.LocalPlayer, plaza.Location + new CVec(2, 2)) == plaza, "Every tile must identify the same square for removal.");
					Check(walker.Trait<Mobile>().CanEnterCell(plaza.Location + new CVec(1, 1)), "Paved squares must be walkable.");
					Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.plaza", plaza.Location), "A second floor must not overlap the existing square.");
					Check(MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.statuelion", plaza.Location + new CVec(1, 1)), "Statues must be placeable on completed paving.");
					w.IssueOrder(new Order("Move", walker, Target.FromCell(w, plaza.Location + new CVec(1, 1)), false));
				}
			}
			if (tick is 700 or 1550 or 1600) Game.Renderer.SaveScreenshot(Path.Combine(output, $"village-{tick}.png"));
			if (tick == 1650 && plaza != null)
			{
				Check(walker.Location == plaza.Location + new CVec(1, 1), "A citizen must physically walk onto the square.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.statuelion", walker.Location), "A statue must not be built on a citizen occupying the square.");
				w.IssueOrder(new Order("Move", walker, Target.FromCell(w, Cell(39, 37)), false));
			}
			if (tick == 1750 && plaza != null)
			{
				Button(w, "TAB_4"); Button(w, "NEXT"); Button(w, "ITEM_2"); Click(w, plaza.Location + new CVec(1, 1));
			}
			if (tick == 2250 && plaza != null)
			{
				var statue = MedievalDecorations.At(w, w.LocalPlayer, plaza.Location + new CVec(1, 1));
				Check(statue != null && statue.Info.Name == "md.statuelion" && MedievalStockpile.Active(statue), "A real statue must be constructed on top of paving.");
				Check(!MedievalSettlement.CanPlace(w, w.LocalPlayer, "md.statueknight", plaza.Location + new CVec(1, 1)), "A second statue must not overlap the first.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "statue-on-plaza.png"));
				if (statue?.Info.Name == "md.statuelion") { Button(w, "REMOVE"); Click(w, statue.Location); removeSent = true; }
			}
			if (tick < 2400) return;
			Check(worked.Values.All(c => c.Count >= 2) && worked.Values.SelectMany(c => c).Distinct().Count() >= 12 && maximumWorking >= 3, "Farmers must distribute across the crop and work multiple distinct rows.");
			Check(carried > 0 && farm.Trait<MedievalStockpile>().Stock.Amount(1) >= 0 && w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().Harvests.Total > 0, "Harvest must still be carried and delivered.");
			Check(cowFrames.Count > 24 && sheepFrames.Count > 24 && paused, "Cattle and sheep must walk, graze, turn and obey pause.");
			var wild = w.WorldActor.Trait<MedievalWilderness>();
			Check(wild.Scene.Animals.All(a => wild.Walkable(a.Position)), "Wild animals must remain outside agricultural footprints and paved squares.");
			var trails = w.WorldActor.Trait<MedievalTrails>();
			Check(w.ActorsWithTrait<MedievalWindVegetation>().Where(a => a.Trait.Info.HideUnderBuildings && trails.ProtectsCell(a.Actor.Location))
				.All(a => a.Trait.Hidden(a.Actor)), "Low vegetation must stay hidden inside crops, pens and squares.");
			Check(cows.Trait<MedievalSource>().Remaining == 210 && sheep.Trait<MedievalSource>().Remaining == 56, "Animal scenery cannot create or consume production stock.");
			Check(w.ActorsHavingTrait<MedievalDecoration>().Count(a => MedievalStockpile.Active(a)) == 6, "All six decorations must finish, and removing an overlaid statue must preserve paving.");
			Check(removeSent && MedievalDecorations.At(w, w.LocalPlayer, plaza.Location + new CVec(1, 1)) == plaza, "Removing a statue must leave its paved square intact.");
			var economy = w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>();
			var food = w.ActorsHavingTrait<MedievalStockpile>().Sum(a => a.Trait<MedievalStockpile>().Stock.Amount(1))
				+ w.ActorsHavingTrait<MedievalWorker>().Select(a => a.Trait<MedievalWorker>()).Where(v => !v.CampCargo && !v.EquipmentCargo && v.Resource == 1).Sum(v => v.Cargo);
			Check(food + economy.TotalConsumed == 140 + economy.Harvests.Total, "Field work must conserve all food through harvests, hauling and meals.");
			Game.Renderer.SaveScreenshot(Path.Combine(output, "village-final.png"));
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, maximumWorking, worked = worked.Values.Select(c => c.Count).ToArray(),
				carried, cowFrames = cowFrames.Count, sheepFrames = sheepFrames.Count, paused, harvests = economy.Harvests.Total, food, eaten = economy.TotalConsumed }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL VILLAGE LIFE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
