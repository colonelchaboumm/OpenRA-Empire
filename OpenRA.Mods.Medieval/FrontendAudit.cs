using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;
using OpenRA.Traits;
using OpenRA.Graphics;

namespace OpenRA.Mods.Medieval
{
	// Opt-in native UI traversal, activated only by Medieval.AuditMenu=True.
	sealed class MedievalFrontendAudit
	{
		internal static MedievalFrontendAudit Current;
		readonly Widget root;
		World currentWorld;
		readonly List<string> errors = [];
		readonly List<string> captures = [];
		int tick, lobbyAt;
		bool finished;
		public MedievalFrontendAudit(Widget root, World world) { this.root = root; currentWorld = world; Current = this; }
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) { errors.Add(message); Console.WriteLine("FRONTEND CHECK: " + message); } }
		static IEnumerable<Widget> Visible(Widget widget)
		{
			if (!widget.IsVisible()) yield break;
			yield return widget;
			foreach (var child in widget.Children) foreach (var descendant in Visible(child)) yield return descendant;
		}
		void Capture(string name)
		{
			Game.Renderer.SaveScreenshot(Path.Combine(MedievalLoadScreen.AuditOutput, name + ".png")); captures.Add(name);
		}
		void Fits(Widget widget)
		{
			foreach (var button in Visible(widget).OfType<ButtonWidget>())
			{
				Check(Game.Renderer.Fonts[button.Font].Measure(button.GetText()).X <= button.Bounds.Width - 12, "Button text clipped: " + button.Id);
				var b = button.RenderBounds; var size = Game.Renderer.Resolution;
				var scrolling = false;
				for (var parent = button.Parent; parent != null; parent = parent.Parent) if (parent is ScrollPanelWidget) scrolling = true;
				if (!scrolling) Check(b.Left >= 0 && b.Top >= 0 && b.Right <= size.Width && b.Bottom <= size.Height, "Button outside screen: " + button.Id);
			}
		}
		void Click(Widget widget, string id)
		{
			var button = widget.Get<ButtonWidget>(id);
			Check(button.IsVisible() && !button.IsDisabled(), "Button unavailable: " + id);
			if (button.IsVisible() && !button.IsDisabled()) Game.RunAfterTick(() => Sync.RunUnsynced(currentWorld, button.OnClick));
		}
		void ClickText(Widget widget, string text)
		{
			var button = Visible(widget).OfType<ButtonWidget>().Single(b => b.GetText() == text);
			Check(!button.IsDisabled(), "Button disabled: " + text);
			if (!button.IsDisabled()) Game.RunAfterTick(() => Sync.RunUnsynced(currentWorld, button.OnClick));
		}
		public void Tick()
		{
			if (finished) return;
			tick++;
			try
			{
				if (tick == 35) { Fits(root); Capture("main-menu"); Click(root.Get("MAIN_MENU"), "SINGLEPLAYER_BUTTON"); }
				if (tick == 55) { Fits(root); Capture("solo"); Click(root.Get("SINGLEPLAYER_MENU"), "BACK_BUTTON"); }
				if (tick == 75) Click(root.Get("MAIN_MENU"), "CONTENT_BUTTON");
				if (tick is >= 95 and <= 175 && (tick - 95) % 20 == 0)
				{
					var guide = Ui.Root.Get("MD_GUIDE"); var index = (tick - 95) / 20;
					Click(guide, "GUIDE_TAB_" + index); Fits(guide);
				}
				if (tick is >= 96 and <= 176 && (tick - 96) % 20 == 0)
				{
					var guide = Ui.Root.Get("MD_GUIDE"); var index = (tick - 96) / 20;
					var body = guide.Get<LabelWidget>("GUIDE_TEXT");
					var text = WidgetUtils.WrapText(body.GetText(), body.Bounds.Width, Game.Renderer.Fonts[body.Font]);
					Check(Game.Renderer.Fonts[body.Font].Measure(text).Y <= body.Bounds.Height, "Guide page overflow: " + index);
				}
				if (tick == 190) Capture("guide");
				if (tick == 200) Click(Ui.Root.Get("MD_GUIDE"), "GUIDE_CLOSE");
				if (tick == 220) Click(root.Get("MAIN_MENU"), "SETTINGS_BUTTON");
				if (tick == 245) { Capture("settings"); Fits(Ui.Root.Get("SETTINGS_PANEL")); }
				if (tick == 250) ClickText(Ui.Root.Get("SETTINGS_PANEL"), "Audio");
				if (tick == 255) { Capture("audio-settings"); Fits(Ui.Root.Get("SETTINGS_PANEL")); }
				if (tick == 260) Click(Ui.Root.Get("SETTINGS_PANEL"), "BACK_BUTTON");
				if (tick == 280) Click(root.Get("MAIN_MENU"), "EXTRAS_BUTTON");
				if (tick == 300) { Fits(root); Capture("archives"); Check(!root.Get("EXTRAS_MENU").Get("MUSIC_BUTTON").IsVisible(), "An empty music player must not be offered."); }
				if (tick == 320) Click(root.Get("EXTRAS_MENU"), "BACK_BUTTON");
				if (tick == 340) Click(root.Get("MAIN_MENU"), "SINGLEPLAYER_BUTTON");
				if (tick == 360) Click(root.Get("SINGLEPLAYER_MENU"), "SKIRMISH_BUTTON");
				if (tick > 370 && lobbyAt == 0 && Ui.Root.GetOrNull("SERVER_LOBBY") != null) lobbyAt = tick;
				if (lobbyAt > 0 && tick == lobbyAt + 30)
				{
					Capture("lobby");
					var mapUid = Ui.Root.Get("SERVER_LOBBY").LogicObjects.OfType<MedievalLobbyPresentationLogic>().Single().MapUid;
					Check(Game.ModData.MapCache[mapUid].PlayerCount == 1, "The solo lobby must start on a solo region.");
					Click(Ui.Root.Get("SERVER_LOBBY"), "CHANGEMAP_BUTTON");
				}
				if (lobbyAt > 0 && tick == lobbyAt + 70) { Capture("map-chooser"); Fits(Ui.Root.Get("MAPCHOOSER_PANEL")); }
				if (lobbyAt > 0 && tick == lobbyAt + 80) Click(Ui.Root.Get("MAPCHOOSER_PANEL"), "BUTTON_CANCEL");
				if (lobbyAt > 0 && tick == lobbyAt + 95) Click(Ui.Root.Get("SERVER_LOBBY"), "DISCONNECT_BUTTON");
				if (lobbyAt > 0 && tick == lobbyAt + 120) Click(root.Get("SINGLEPLAYER_MENU"), "MISSIONS_BUTTON");
				if (tick >= 950) { Check(false, "Menu traversal timed out before reaching the map chooser."); Finish(); }
			}
			catch (Exception e) { Check(false, e.ToString()); Finish(); }
		}
		internal void Gameplay(World world)
		{
			currentWorld = world;
			void Later(int ms, Action action) => Game.RunAfterDelay(ms, () =>
			{
				if (finished) return;
				try { action(); } catch (Exception e) { Check(false, e.ToString()); Finish(); }
			});
			Later(1200, () =>
			{
				Check(world.Actors.Any(a => a.Info.Name == "md.caravan" && a.Owner == world.LocalPlayer), "Quick start must create the player's founding caravan.");
				Check(world.Selection.Actors.Any(a => a.Info.Name == "md.caravan"), "The starting caravan must be selected for easy founding.");
				Capture("gameplay"); Click(Ui.Root, "OPTIONS_BUTTON");
			});
			Later(2000, () =>
			{
				var menu = Ui.Root.Get("INGAME_MENU"); Capture("pause"); Fits(menu);
				Check(world.Paused, "Singleplayer menu must pause the simulation.");
				Check(!Visible(menu).OfType<ButtonWidget>().Any(b => b.GetText() == FluentProvider.GetMessage("menu-ingame.music")), "Pause must not offer an empty music player.");
				ClickText(menu, FluentProvider.GetMessage("menu-ingame.save-game"));
			});
			Later(2800, () => { var saves = Ui.Root.Get("GAMESAVE_BROWSER_PANEL"); Capture("save-menu"); Fits(saves); Click(saves, "CANCEL_BUTTON"); });
			Later(3600, () => ClickText(Ui.Root.Get("INGAME_MENU"), FluentProvider.GetMessage("menu-ingame.resume")));
			Later(4500, () => { Check(Ui.Root.GetOrNull("INGAME_MENU") == null && !world.Paused, "Resume must close the menu and resume play."); Finish(); });
		}
		void Finish()
		{
			finished = true;
			File.WriteAllText(Path.Combine(MedievalLoadScreen.AuditOutput, "result.json"), JsonSerializer.Serialize(new {
				passed = errors.Count == 0, tick, errors, captures, resolution = Game.Renderer.Resolution.ToString()
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL FRONTEND: " + (errors.Count == 0 ? "PASS" : "FAIL"));
			Game.RunAfterDelay(1000, Game.Exit);
		}
	}
	public class MedievalFrontendGameValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFrontendGameValidation();
	}
	public sealed class MedievalFrontendGameValidation : IWorldLoaded
	{
		void IWorldLoaded.WorldLoaded(World world, WorldRenderer renderer)
		{
			if (world.Type == WorldType.Regular && MedievalLoadScreen.AuditRequested)
				MedievalFrontendAudit.Current?.Gameplay(world);
		}
	}
}
