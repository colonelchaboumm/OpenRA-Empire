using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalDisplayValidationInfo : TraitInfo
	{
		public readonly int Step = 0;
		public override object Create(ActorInitializer init) => new MedievalDisplayValidation(this);
	}
	// Opt-in native test. Each process uses the settings saved by the previous one.
	public sealed class MedievalDisplayValidation : IWorldLoaded, ITick
	{
		readonly MedievalDisplayValidationInfo info;
		readonly List<string> errors = [], checks = [];
		WorldRenderer renderer;
		string output;
		int tick;
		bool done;
		float minimumZoom, startingZoom;
		WindowMode mode;
		Size resolution;
		public MedievalDisplayValidation(MedievalDisplayValidationInfo info) { this.info = info; }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "display-zoom", "native", "step-" + info.Step); Directory.CreateDirectory(output);
		}
		void Check(bool ok, string text) { checks.Add(text); if (!ok) errors.Add(text); }
		static IEnumerable<Widget> Widgets(Widget widget)
		{
			yield return widget;
			foreach (var child in widget.Children) foreach (var inner in Widgets(child)) yield return inner;
		}
		void Select(Widget panel, string dropdownId, string label)
		{
			var dropdown = panel.Get<DropDownButtonWidget>(dropdownId);
			dropdown.OnMouseDown(new MouseInput(MouseInputEvent.Down, MouseButton.Left, int2.Zero, int2.Zero, Modifiers.None, 1));
			var item = Widgets(Ui.Root).OfType<ScrollItemWidget>().Single(i => i.IsVisible() && i.GetOrNull<LabelWidget>("LABEL")?.GetText() == label);
			item.OnClick();
		}
		void Zoom(World w)
		{
			var viewport = renderer.Viewport;
			startingZoom = viewport.Zoom;
			Check(Math.Abs(startingZoom - viewport.MinZoom) < .001f, "Normal camera distance is preserved on startup");
			var controller = Widgets(Ui.Root).OfType<ViewportControllerWidget>().Single();
			var pointer = new int2(500, 400); Viewport.LastMousePos = pointer;
			if (info.Step == 4) viewport.Center(w.Map.ProjectedTopLeft + (w.Map.ProjectedBottomRight - w.Map.ProjectedTopLeft) / 2);
			controller.HandleMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, pointer, new int2(0, -100), Game.Settings.Game.ZoomModifier, 0));
			minimumZoom = viewport.Zoom;
			Check(Math.Abs(minimumZoom - viewport.MinZoom * .25f) < .001f, "Mouse wheel reaches four times the original terrain width");
			var center = w.Map.CellContaining(viewport.CenterPosition);
			for (var x = -6; x <= 6; x += 3)
				for (var y = -6; y <= 6; y += 3)
				{
					var cell = center + new CVec(x, y);
					if (!w.Map.Contains(cell)) continue;
					var pixel = viewport.WorldToViewPx(renderer.ScreenPxPosition(w.Map.CenterOfCell(cell)));
					Check(viewport.ViewToWorld(pixel) == cell, "Isometric picking remains aligned at maximum zoom out");
				}
			viewport.ToggleZoom();
			Check(Math.Abs(viewport.Zoom - viewport.MinZoom) < .001f, "Reset zoom restores the normal distance");
			controller.HandleMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, pointer, new int2(0, 100), Game.Settings.Game.ZoomModifier, 0));
			Check(Math.Abs(viewport.Zoom - viewport.MaxZoom) < .001f, "Close zoom remains available");
			viewport.ToggleZoom();
			controller.HandleMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, pointer, new int2(0, -100), Game.Settings.Game.ZoomModifier, 0));
		}
		void Open(World w)
		{
			mode = Game.Settings.Graphics.Mode; resolution = Game.Renderer.NativeResolution;
			var expected = info.Step == 2 ? WindowMode.PseudoFullscreen : info.Step == 3 ? WindowMode.Fullscreen : WindowMode.Windowed;
			Check(mode == expected, "Saved display mode is used by the next process");
			if (expected == WindowMode.Windowed)
			{
				var size = info.Step == 0 ? new Size(1280, 800) : info.Step == 1 ? new Size(1600, 900) : new Size(1366, 768);
				Check(resolution == size, "Saved window resolution is applied: " + size);
			}
			else Check(resolution.Width >= 1280 && resolution.Height >= 800, "Fullscreen uses the desktop display size");
			Zoom(w);
		}
		void OpenSettings(World w)
		{
			Game.Renderer.SaveScreenshot(Path.Combine(output, "zoom-out.png"));
			renderer.Viewport.ToggleZoom();
			Ui.OpenWindow("SETTINGS_PANEL", new WidgetArgs { { "world", w }, { "worldRenderer", renderer }, { "onExit", () => { } } });
			var panel = Ui.Root.Get("SETTINGS_PANEL");
			panel.Get("SETTINGS_TAB_CONTAINER").Get<ButtonWidget>("DISPLAY_PANEL").OnClick();
			var display = panel.Get("PANEL_CONTAINER").Get("DISPLAY_PANEL");
			if (info.Step is 0 or 3)
			{
				Select(display, "MODE_DROPDOWN", FluentProvider.GetMessage("options-display-mode.windowed"));
				Select(display, "RESOLUTION_PRESET_DROPDOWN", info.Step == 0 ? "1600x900" : "1366x768");
				Check(display.Get("WINDOW_RESOLUTION_CONTAINER").IsVisible(), "Window resolution fields are available");
			}
			else if (info.Step is 1 or 2)
			{
				Select(display, "MODE_DROPDOWN", FluentProvider.GetMessage(info.Step == 1 ? "options-display-mode.fullscreen" : "options-display-mode.legacy-fullscreen"));
				Check(!display.Get("WINDOW_RESOLUTION_CONTAINER").IsVisible() && display.Get("DISPLAY_SELECTION_CONTAINER").IsVisible(), "Fullscreen offers monitor selection");
			}
			if (info.Step < 4) Check(display.Get("VIDEO_RESTART_REQUIRED_DESC").IsVisible(), "Display changes explain the required restart");
		}
		void Save()
		{
			Game.Renderer.SaveScreenshot(Path.Combine(output, "display-settings.png"));
			Ui.Root.Get("SETTINGS_PANEL").Get<ButtonWidget>("BACK_BUTTON").OnClick();
			var saved = new Settings(Path.Combine(Platform.SupportDir, "settings.yaml"), new Arguments([])).Graphics;
			Check(saved.Mode == Game.Settings.Graphics.Mode && saved.WindowedSize == Game.Settings.Graphics.WindowedSize, "The Back button persists mode and resolution to settings.yaml");
			if (info.Step is 0 or 3)
				Check(saved.WindowedSize == (info.Step == 0 ? new int2(1600, 900) : new int2(1366, 768)), "Selected resolution preset is saved exactly");
		}
		void Finish()
		{
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, checks, mode = mode.ToString(), resolution = resolution.ToString(), startingZoom, minimumZoom }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL DISPLAY: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(500, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			if (done || self.World.LocalPlayer == null) return;
			if (++tick is not (30 or 40 or 50 or 60)) return;
			var step = tick;
			// Reveal only this opt-in test world so the maximum zoom actually renders
			// a wide region of forest, water and resources instead of black shroud.
			if (step == 30 && info.Step == 4) self.World.LocalPlayer.Shroud.Disabled = true;
			Game.RunAfterTick(() => Sync.RunUnsynced(self.World, () =>
			{
				try { if (step == 30) Open(self.World); else if (step == 40) OpenSettings(self.World); else if (step == 50) Save(); else Finish(); }
				catch (Exception e) { errors.Add(e.ToString()); Finish(); }
			}));
		}
	}
}
