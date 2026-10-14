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
	public class MedievalHudValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalHudValidation();
	}
	public sealed class MedievalHudValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		WorldRenderer renderer;
		Actor guard, militia, market, barracks, cart, church;
		WPos cameraCenter;
		string output;
		int tick;
		bool done, pauseChecked;
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			output = Path.Combine(Platform.EngineDir, "analysis/medieval/hud", Game.Renderer.Resolution.Width < 1200 ? "small" : "native");
			Directory.CreateDirectory(output);
		}
		void Select(World w, params Actor[] actors) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, actors, false, false));
		void Click(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>(id);
			for (Widget p = b; p != null; p = p.Parent) Check(p.IsVisible(), "Visible click path: " + id + " / " + p.Id);
			Check(!b.IsDisabled(), "Enabled click: " + id);
			var at = b.RenderOrigin + new int2(b.Bounds.Width / 2, b.Bounds.Height / 2);
			foreach (var action in new[] { MouseInputEvent.Down, MouseInputEvent.Up })
				Check(Ui.Root.HandleMouseInputOuter(new MouseInput(action, MouseButton.Left, at, int2.Zero, Modifiers.None, 1)), "Root handles click: " + id);
		});
		void Tab(World w, string name)
		{
			var b = Enumerable.Range(0, 3).Select(i => Ui.Root.Get<ButtonWidget>("MD_CONTEXT_TAB_" + i)).FirstOrDefault(b => b.IsVisible() && b.GetText() == name);
			Check(b != null, "Context tab: " + name); if (b != null) Click(w, b.Id);
		}
		void Fits(string id)
		{
			var b = Ui.Root.Get(id).RenderBounds;
			Check(b.Left >= 0 && b.Right <= Game.Renderer.Resolution.Width && b.Top >= 0 && b.Bottom < Ui.Root.Get("MD_STATUS_PANEL").RenderBounds.Top,
				"Panel fits above the selection footer: " + id);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			Actor Spawn(string type, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(w.LocalPlayer), new LocationInit(new CPos(x + 32, y - 32))]);
			guard = Spawn("royalguard", 27, 28); militia = Spawn("militia", 28, 28);
			market = Spawn("market", 24, 20); barracks = Spawn("barracks", 30, 20); cart = Spawn("tradecart", 29, 27);
			Spawn("warehouse", 20, 24); Spawn("house", 23, 25);
			church = Spawn("church", 24, 34);
			cameraCenter = guard.CenterPosition;
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(cameraCenter));
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			// Native desktop pointer events must not edge-scroll the test scene off screen.
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(cameraCenter));
			if (tick == 40) Select(w, guard);
			if (tick == 80)
			{
				Check(Ui.Root.Get("MD_FOOD_PANEL").Bounds.Height == 62, "Compact food summary.");
				Check(Ui.Root.Get("MD_PATROL_PANEL").IsVisible() && !Ui.Root.Get("MD_CREW_PANEL").IsVisible(), "Troops and crew no longer overlap.");
				Check(!Ui.Root.Get("MD_PATROL_DRAW").IsVisible() && Ui.Root.Get("MD_PATROL_PANEL").Bounds.Height == 160, "Patrol controls hidden for non-militia.");
				Fits("MD_PATROL_PANEL"); Game.Renderer.SaveScreenshot(Path.Combine(output, "troops.png"));
			}
			if (tick == 100) { Click(w, "MD_CONTEXT_COLLAPSE"); Check(!Ui.Root.Get("MD_PATROL_PANEL").IsVisible(), "Collapse keeps the battlefield clear."); }
			if (tick == 120) { Tab(w, "Équipage"); Check(Ui.Root.Get("MD_CREW_PANEL").IsVisible() && !Ui.Root.Get("MD_PATROL_PANEL").IsVisible(), "Crew tab reopens the collapsed dock."); }
			if (tick == 140) Select(w, militia);
			if (tick == 180)
			{
				Check(Ui.Root.Get("MD_PATROL_PANEL").IsVisible() && Ui.Root.Get("MD_PATROL_DRAW").IsVisible(), "Militia retains patrol commands after a selection change.");
				Select(w, market);
			}
			if (tick == 220) { Check(Ui.Root.Get("MD_TRADE_PANEL").IsVisible() && !Ui.Root.Get("MD_LOCALITY_PANEL").IsVisible(), "Market defaults to commerce."); Tab(w, "Localité"); }
			if (tick == 240)
			{
				Check(Ui.Root.Get("MD_LOCALITY_PANEL").IsVisible(), "Rename and locality details remain reachable.");
				var input = Ui.Root.Get<TextFieldWidget>("MD_LOCALITY_INPUT");
				Sync.RunUnsynced(w, () => Check(input.TakeKeyboardFocus(), "Locality name can receive keyboard focus."));
				Click(w, "MD_CONTEXT_COLLAPSE");
				Check(!input.HasKeyboardFocus, "Collapsing a name field releases keyboard control.");
				Select(w, barracks);
			}
			if (tick == 280) { Fits("MD_BARRACKS_PANEL"); Game.Renderer.SaveScreenshot(Path.Combine(output, "barracks.png")); Click(w, "MD_FOOD_TOGGLE"); }
			if (tick == 300)
			{
				Check(Ui.Root.Get("MD_FOOD_DETAILS").IsVisible() && !Ui.Root.Get("MD_BARRACKS_PANEL").IsVisible(), "Food details replace the context panel.");
				Click(w, "MD_PRIDE_OPEN"); Check(!Ui.Root.Get("MD_FOOD_DETAILS").IsVisible() && Ui.Root.Get("MD_PRIDE_PANEL").IsVisible(), "Opening pride closes the other report.");
				Fits("MD_PRIDE_PANEL");
			}
			if (tick == 320) { Click(w, "MD_CAMP_OPEN"); Check(!Ui.Root.Get("MD_PRIDE_PANEL").IsVisible() && Ui.Root.Get("MD_CAMP_PANEL").IsVisible(), "Global windows do not overlap."); Fits("MD_CAMP_PANEL"); }
			if (tick == 340) { Click(w, "MD_CAMP_CLOSE"); Select(w, guard); }
			if (tick >= 350 && tick <= 370) Sync.RunUnsynced(w, () =>
			{
				// Aim inside the sprite, not at its ground anchor (outside the clickable bounds).
				var bounds = cart.MouseBounds(renderer).BoundingRect;
				var point = bounds.TopLeft + new int2(bounds.Width / 2, bounds.Height / 2);
				Check(w.ScreenMap.ActorsAtMouse(point).Any(hit => hit.Actor == cart), "Cart hover position intersects its clickable bounds.");
				Ui.HandleInput(new MouseInput(MouseInputEvent.Move, MouseButton.None,
					renderer.Viewport.WorldToViewPx(point), int2.Zero, Modifiers.None, 0));
			});
			if (tick == 370) Sync.RunUnsynced(w, () =>
			{
				Check(MedievalHudState.StockVisible(cart), "Hovering an unselected cart exposes its stock.");
				if (!MedievalHudState.StockVisible(cart)) Console.WriteLine($"HUD HOVER: widget={Ui.MouseOverWidget?.GetType().Name}, mouse={Viewport.LastMousePos}, cart={renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(cart.CenterPosition))}");
				Ui.HandleInput(new MouseInput(MouseInputEvent.Move, MouseButton.None, int2.Zero, int2.Zero, Modifiers.None, 0));
			});
			if (tick == 380)
			{
				var display = (IRenderAnnotations)cart.Trait<MedievalStockDisplay>();
				Check(!display.RenderAnnotations(cart, renderer).OfType<StockBadgeRenderable>().Any(), "Unselected cart has no stock labels.");
				Click(w, "MD_STOCKS_TOGGLE"); Check(display.RenderAnnotations(cart, renderer).OfType<StockBadgeRenderable>().Any(), "All-stocks toggle restores resource labels.");
				Click(w, "MD_STOCKS_TOGGLE"); Select(w, cart);
			}
			if (tick == 400)
			{
				Check(((IRenderAnnotations)cart.Trait<MedievalStockDisplay>()).RenderAnnotations(cart, renderer).OfType<StockBadgeRenderable>().Any(), "Selected stock remains visible.");
				Sync.RunUnsynced(w, () => w.SetPauseState(true));
				Game.RunAfterDelay(200, () => Select(w, guard));
				Game.RunAfterDelay(500, () => { pauseChecked = Ui.Root.Get("MD_PATROL_PANEL").IsVisible(); Check(pauseChecked, "Context updates while paused."); Click(w, "MD_CONTEXT_COLLAPSE"); w.SetPauseState(false); });
			}
			if (tick == 440) guard.Dispose();
			if (tick == 480) Check(!Ui.Root.Get("MD_CONTEXT_TABS").IsVisible(), "Disposed selection clears its contextual actions.");
			if (tick == 500) Select(w, church);
			if (tick == 540)
			{
				Click(w, "MD_CHURCH_CEMETERY");
				Sync.RunUnsynced(w, () => w.SetPauseState(true));
				Game.RunAfterDelay(300, () => Sync.RunUnsynced(w, () =>
				{
					var generator = (MedievalCemeteryOrderGenerator)w.OrderGenerator;
					Check(w.Paused, "Pause order has been applied before zoning checks.");
					foreach (var cell in new[] { new CPos(62, 2), new CPos(63, 3) })
						Check(!generator.Order(w, cell, int2.Zero, new MouseInput(MouseInputEvent.Down, generator.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1)).Any(), "Cemetery zoning cannot issue orders while paused.");
					w.SetPauseState(false);
				}));
			}
			if (tick == 560) church.Dispose();
			if (tick == 580) Check(w.OrderGenerator is not MedievalCemeteryOrderGenerator, "Destroyed church cancels its placement tool.");
			if (tick == 600) Select(w, militia, cart, market, barracks);
			if (tick == 640)
			{
				Check(Ui.Root.Get("MD_CONTEXT_NEXT").IsVisible(), "Mixed selection exposes additional context pages.");
				Click(w, "MD_CONTEXT_NEXT"); Click(w, "MD_CONTEXT_TAB_0");
				Check(Ui.Root.Get("MD_TRADE_PANEL").IsVisible(), "Additional mixed-selection pages can be opened.");
				Click(w, "MD_MENU_TAB_4");
			}
			if (tick == 680)
			{
				Check(Ui.Root.Get("MD_PANEL").RenderBounds.Bottom <= Game.Renderer.Resolution.Height - 8, "Longest catalogue fits the screen.");
				Click(w, "MD_MENU_NEXT"); Check(Ui.Root.Get<LabelWidget>("MD_MENU_PAGE").GetText() == "2/3", "Catalogue paging remains reachable.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "catalogue.png"));
			}
			if (tick < 700) return;
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, pauseChecked,
				resolution = Game.Renderer.Resolution.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
			done = true; Console.WriteLine("MEDIEVAL HUD: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
