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
	public class MedievalSelectionValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSelectionValidation();
	}
	public sealed class MedievalSelectionValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<Actor> units = [];
		Actor builder, house;
		Widget panel;
		WorldRenderer renderer;
		Actor[] narrowed;
		string output;
		int tick, beforeRemove, beforeDeath;
		bool done, pauseChecked;
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		Actor Spawn(World w, string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Select(World w, IEnumerable<Actor> actors) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, actors, false, false));
		ButtonWidget Icon(int index) => panel.Get<ButtonWidget>("MD_UNIT_" + index);
		ButtonWidget[] Visible() => Enumerable.Range(0, 6).Select(Icon).Where(b => b.IsVisible()).ToArray();
		void Click(World w, ButtonWidget button, Modifiers modifiers = Modifiers.None) => Sync.RunUnsynced(w, () =>
		{
			var at = button.RenderOrigin + new int2(button.Bounds.Width / 2, button.Bounds.Height / 2);
			foreach (var action in new[] { MouseInputEvent.Down, MouseInputEvent.Up })
				Check(button.HandleMouseInput(new MouseInput(action, MouseButton.Left, at, int2.Zero, modifiers, 1)), "The portrait must consume its mouse click.");
		});
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "selection", Game.Renderer.Resolution.Width < 1200 ? "small" : "native");
			Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			house = Spawn(w, "house", 24, 25);
			var types = new[] { "builder", "hauler", "lumberjack", "stonecutter", "farmer", "breeder", "shepherd", "weaver", "hunter", "goldminer",
				"militia", "archer", "swordsman", "manatarms", "elitearcher", "royalguard", "drummer", "standardbearer", "commander", "priest", "saboteur", "messenger", "supplycart", "ram", "trebuchet", "sheep" };
			for (var i = 0; i < types.Length; i++)
			{
				var unit = Spawn(w, types[i], 28 + i % 8, 25 + i / 8 * 2); units.Add(unit);
				if (unit.TraitOrDefault<MedievalWorker>() is { } worker) worker.Automatic = false;
			}
			for (var i = 0; i < 200; i++) units.Add(Spawn(w, "archer", 22 + i % 20, 36 + i / 20));
			builder = units[0]; panel = Ui.Root.Get("MD_UNIT_SELECTION");
			Select(w, []); Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 30))));
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			if (tick == 80) { Check(!panel.IsVisible(), "No portraits without a selection."); Select(w, [builder]); }
			if (tick == 160)
			{
				Check(panel.IsVisible() && Visible().Length == 1 && Icon(0).Get<LabelWidget>("COUNT").GetText() == "1", "A single citizen needs one portrait and a count of one.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "single.png")); Select(w, units);
			}
			if (tick == 240)
			{
				Check(panel.Get<LabelWidget>("MD_UNIT_TOTAL").GetText().Contains("226"), "The panel must count every unit in a large mixed selection.");
				Check(Icon(0).Get<LabelWidget>("COUNT").GetText() == "201", "Archers must share one portrait with their exact count.");
				Check(Ui.Root.Get("MD_STATUS_PANEL").RenderBounds.Left >= panel.RenderBounds.Right, "Portraits cannot overlap the status text.");
				Check(panel.RenderBounds.Bottom < Ui.Root.Get("COMMAND_BAR_BACKGROUND").RenderBounds.Top, "Portraits cannot overlap the command bar.");
				foreach (var button in Visible()) Check(button.Get<SpriteWidget>("ICON").GetSprite() != null, "Each visible group needs a sprite.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "mixed.png"));
			}
			if (tick == 250) Sync.RunUnsynced(w, () => Icon(0).MouseEntered());
			if (tick == 278)
			{
				Game.Renderer.SaveScreenshot(Path.Combine(output, "tooltip.png"));
				Sync.RunUnsynced(w, () => Icon(0).MouseExited());
			}
			// Visit every page, including civilians, soldiers, carts, siege engines and animals.
			if (tick is >= 280 and <= 800 && tick % 40 == 0)
			{
				foreach (var button in Visible()) Check(button.Get<SpriteWidget>("ICON").GetSprite() != null && button.GetTooltipText().Length > 0, "Every page needs valid named portraits.");
				var next = panel.Get<ButtonWidget>("MD_UNIT_NEXT"); if (!next.IsDisabled()) Click(w, next);
			}
			if (tick == 840)
			{
				Check(panel.Get<ButtonWidget>("MD_UNIT_NEXT").IsDisabled(), "Pagination must reach the final group.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "last-page.png"));
				var groups = MedievalSelectionLogic.Group(w.Selection.Actors); narrowed = groups[^Visible().Length]; Click(w, Icon(0));
			}
			if (tick == 920)
			{
				Check(w.Selection.Actors.Count == narrowed.Length && narrowed.All(w.Selection.Contains), "Click must select only members of that type from the original selection."); Select(w, units);
			}
			if (tick == 1000) { beforeRemove = w.Selection.Actors.Count - MedievalSelectionLogic.Group(w.Selection.Actors)[0].Length; Click(w, Icon(0), Modifiers.Shift); }
			if (tick == 1080)
			{
				Check(w.Selection.Actors.Count == beforeRemove && w.Selection.Actors.All(a => a.Info.Name != "md.archer"), "Shift-click must remove just the clicked type.");
				beforeDeath = w.Selection.Actors.Count; var removed = w.Selection.Actors.First(a => a != builder); w.AddFrameEndTask(_ => removed.Dispose());
			}
			if (tick == 1160)
			{
				Check(w.Selection.Actors.Count == beforeDeath - 1 && panel.Get<LabelWidget>("MD_UNIT_TOTAL").GetText().Contains((beforeDeath - 1).ToString()), "Removed units must disappear from the panel."); Select(w, [house]);
			}
			if (tick == 1190) Sync.RunUnsynced(w, () => Ui.Root.Get<LabelWithTooltipWidget>("MD_SELECTION").MouseEntered());
			if (tick == 1230) Sync.RunUnsynced(w, () => Ui.Root.Get<LabelWithTooltipWidget>("MD_SELECTION").MouseExited());
			if (tick == 1240)
			{
				Check(!panel.IsVisible() && Ui.Root.Get("MD_STATUS_PANEL").Bounds.X == 8, "Building selection restores the normal status panel.");
				w.SetPauseState(true);
				Game.RunAfterDelay(100, () => Select(w, [builder]));
				Game.RunAfterDelay(350, () => { pauseChecked = panel.IsVisible() && Visible().Length == 1; Check(pauseChecked, "Selection UI must update while paused."); w.SetPauseState(false); });
			}
			if (tick < 1320) return;
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, units = units.Count,
				pauseChecked, resolution = Game.Renderer.Resolution.ToString(), slots = Visible().Length }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SELECTION: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
		}
	}
}
