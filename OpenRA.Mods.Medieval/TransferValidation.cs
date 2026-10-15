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
	public class MedievalTransferValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalTransferValidation();
	}
	public sealed class MedievalTransferValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		WorldRenderer renderer;
		Actor depot, cart, farther, enemy, market, builder, farm, barracks, siege, forge, church, doomed, weaver, house;
		string output;
		int tick, recoveryStopped;
		bool done, pauseChecked;
		MedievalTransferWidget Panel => Ui.Root.Get<MedievalTransferWidget>("MD_TRANSFER_PANEL");
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World world, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/transfers", Game.Renderer.Resolution.Width < 1200 ? "small" : "native"); Directory.CreateDirectory(output);
		}
		void Select(World w, Actor actor) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, actor == null ? [] : new[] { actor }, false, false));
		void Click(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>(id);
			for (Widget p = b; p != null; p = p.Parent) Check(p.IsVisible(), "Visible click: " + id + " / " + p.Id);
			Check(!b.IsDisabled(), "Enabled click: " + id);
			var at = b.RenderOrigin + new int2(b.Bounds.Width / 2, b.Bounds.Height / 2);
			foreach (var ev in new[] { MouseInputEvent.Down, MouseInputEvent.Up }) Ui.HandleInput(new MouseInput(ev, MouseButton.Left, at, int2.Zero, Modifiers.None, 1));
		});
		void Drag(World w, bool load, int good) => Sync.RunUnsynced(w, () =>
		{
			var from = Panel.Cell(load, good).TopLeft + new int2(20, 12); var to = Panel.Cell(!load, good).TopLeft + new int2(20, 12);
			Ui.HandleInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, from, int2.Zero, Modifiers.None, 1));
			Ui.HandleInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, to, int2.Zero, Modifiers.None, 0));
			Ui.HandleInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, to, int2.Zero, Modifiers.None, 1));
			Check(Panel.Choosing, "Drop opens quantity chooser, load=" + load);
		});
		void Quantity(World w, int n) => Sync.RunUnsynced(w, () => Ui.Root.Get<TextFieldWidget>("MD_TRANSFER_QUANTITY").Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture));
		void Transfer(World w, Actor carrier, Actor target, int good, int n, bool load = true) => w.IssueOrder(new Order(load ? "MedievalLoadResource" : "MedievalUnloadResource", carrier, Target.FromActor(target), false) { ExtraData = (uint)good, TargetString = n.ToString(System.Globalization.CultureInfo.InvariantCulture) });
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			Actor Spawn(string type, int x, int y, Player owner = null) => w.CreateActor("md." + type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(new CPos(x + 32, y - 32))]);
			depot = Spawn("warehouse", 20, 20); cart = Spawn("tradecart", 28, 24); farther = Spawn("supplycart", 34, 25);
			enemy = Spawn("warehouse", 12, 20, w.Players.First(p => p.InternalName == "Creeps"));
			market = Spawn("market", 20, 30); builder = Spawn("builder", 28, 30); farm = Spawn("farm", 32, 30);
			barracks = Spawn("barracks", 36, 30); siege = Spawn("siegeworkshop", 40, 30); forge = Spawn("forge", 36, 36); church = Spawn("church", 40, 36);
			doomed = Spawn("warehouse", 12, 32); weaver = Spawn("tentweaver", 44, 30); house = Spawn("house", 44, 36);
			foreach (var a in new[] { depot, doomed }) for (var g = 0; g < StorageGoods.Count; g++)
			{
				var inventory = StorageGoods.Stock(a, g); var index = StorageGoods.Index(g);
				inventory?.Take(index, int.MaxValue); inventory?.Add(index, 100);
			}
			Select(w, depot);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(depot.CenterPosition));
			if (tick == 60)
			{
				Sync.RunUnsynced(w, () =>
				{
					for (var g = 0; g < StorageGoods.Count; g++) Check(Panel.Icon(g)?.Bounds.Width > 0, "Resource has a valid icon: " + g);
					foreach (var tab in Enumerable.Range(0, 3).Select(i => Ui.Root.Get<ButtonWidget>("MD_CONTEXT_TAB_" + i)).Where(t => t.IsVisible()))
						Check(tab.Get<SpriteWidget>("ICON").GetSprite() != null, "Visible context tab has an icon: " + tab.GetText());
				});
				Check(Panel.IsVisible() && Panel.Cart == cart && Panel.Store == depot, "Depot exposes its stock and nearest available cart.");
				Check(Panel.RenderBounds.Right <= Game.Renderer.Resolution.Width && Panel.RenderBounds.Bottom < Game.Renderer.Resolution.Height, "Transfer stocks fit screen.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "stocks.png")); Drag(w, true, 0); Quantity(w, 17);
			}
			if (tick == 80)
			{
				Game.Renderer.SaveScreenshot(Path.Combine(output, "quantity.png"));
				Check(cart.Trait<MedievalStockpile>().Stock.Amount(0) == 0 && depot.Trait<MedievalStockpile>().Stock.Reserved(0) == 0, "Dragging does not move or reserve anything.");
				Click(w, "MD_TRANSFER_CANCEL"); Check(!Panel.Choosing && Ui.KeyboardFocusWidget == null, "Cancel dismisses quantity and keyboard focus.");
				Drag(w, true, 0); Quantity(w, 17); Click(w, "MD_TRANSFER_CONFIRM");
			}
			if (tick == 110)
			{
				Check(cart.Trait<MedievalManualTransfer>().Busy, "Confirmed order is travelling.");
				Check(cart.Trait<MedievalStockpile>().Stock.Amount(0) == 0 && depot.Trait<MedievalStockpile>().Stock.Reserved(0) == 17, "Real stock stays at depot during journey.");
				Check(!MedievalBarter.AvailableCarts(w.LocalPlayer).Contains(cart), "A manually reserved cart cannot be loaned to barter.");
				Check(Panel.StockDetail(true, 0).Contains("83 disponibles · 17 réservés", StringComparison.Ordinal)
					&& Panel.StockDetail(false, 0).Contains("17 attendus", StringComparison.Ordinal), "Stock details distinguish physical, available, reserved and incoming goods.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "reserved.png"));
				w.IssueOrder(new Order("MedievalAssignTrader", cart, Target.FromActor(enemy), false));
				w.IssueOrder(new Order("MedievalResumeTrade", cart, false));
				w.IssueOrder(new Order("MedievalCartCargo", cart, false) { ExtraData = uint.MaxValue });
			}
			if (tick == 140) Check(cart.Trait<MedievalManualTransfer>().Busy && depot.Trait<MedievalStockpile>().Stock.Reserved(0) == 17,
				"Rejected and unsupported automatic commands must not cancel a manual transfer.");
			if (tick == 115) Sync.RunUnsynced(w, () => Ui.HandleInput(new MouseInput(MouseInputEvent.Move, MouseButton.None,
				Panel.Cell(true, 0).TopLeft + new int2(40, 10), int2.Zero, Modifiers.None, 0)));
			if (tick == 125) Game.Renderer.SaveScreenshot(Path.Combine(output, "stock-details.png"));
			if (tick == 650)
			{
				Check(!cart.Trait<MedievalManualTransfer>().Busy && cart.Trait<MedievalStockpile>().Stock.Amount(0) == 17, "Physical loading completes exact requested quantity.");
				Check(depot.Trait<MedievalStockpile>().Stock.Amount(0) == 83, "Loading conserves depot stock.");
				Select(w, cart);
			}
			if (tick == 690) { Check(Panel.Store == depot, "Selected cart shows nearest depot."); Drag(w, false, 0); Quantity(w, 5); Click(w, "MD_TRANSFER_CONFIRM"); }
			if (tick == 800)
			{
				Check(cart.Trait<MedievalStockpile>().Stock.Amount(0) == 12 && depot.Trait<MedievalStockpile>().Stock.Amount(0) == 88, "Reverse drag unloads exact quantity.");
				Transfer(w, cart, enemy, 0, 10); Transfer(w, cart, depot, 0, int.MaxValue); Transfer(w, cart, depot, -1, 10);
			}
			if (tick == 840) { Check(!cart.Trait<MedievalManualTransfer>().Busy, "Foreign and malformed orders rejected."); Transfer(w, cart, doomed, 0, 10); }
			if (tick == 870) { Check(doomed.Trait<MedievalStockpile>().Stock.Reserved(0) == 10, "Distant store booked."); doomed.Dispose(); }
			if (tick == 910) { Check(!cart.Trait<MedievalManualTransfer>().Busy && cart.Trait<MedievalStockpile>().Stock.Incoming(0) == 0, "Destroyed destination frees cart reservation."); Select(w, market); }
			if (tick == 940) { Check(Ui.Root.Get("MD_PANEL").IsVisible() && Ui.Root.Get<LabelWidget>("MD_MENU_ITEM_0_NAME").GetText() == "Batisseur", "Market opens citizen recruitment."); Game.Renderer.SaveScreenshot(Path.Combine(output, "market.png")); Select(w, builder); }
			if (tick == 970) { Check(Ui.Root.Get("MD_MENU_TAB_0").IsVisible() && Ui.Root.Get<LabelWidget>("MD_MENU_ITEM_0_NAME").GetText() == "Maison", "Builder opens construction catalogue."); Select(w, farm); }
			if (tick == 1000) { Check(Ui.Root.Get<LabelWidget>("MD_MENU_ITEM_0_NAME").GetText() == "Cultures" && Ui.Root.Get<LabelWidget>("MD_MENU_ITEM_1_NAME").GetText() == "Enclos bovin", "Farm offers fields and enclosures."); Click(w, "MD_MENU_ITEM_1"); Check(w.OrderGenerator is MedievalZoneOrderGenerator, "Farm enclosure opens zoning tool."); Sync.RunUnsynced(w, w.CancelInputMode); Select(w, barracks); }
			if (tick == 1030)
			{
				Check(Ui.Root.Get("MD_BARRACKS_PANEL").IsVisible(), "Barracks opens military recruitment.");
				var scroll = Ui.Root.Get<ScrollPanelWidget>("MD_CONTEXT_SCROLL"); Sync.RunUnsynced(w, () => scroll.ScrollToBottom());
				Check(scroll.ContentHeight > scroll.Bounds.Height, "Long recruitment panel scrolls.");
			}
			if (tick == 1050) { Game.Renderer.SaveScreenshot(Path.Combine(output, "barracks-scroll.png")); Select(w, siege); }
			if (tick == 1080) { Check(Ui.Root.Get("MD_SIEGE_WORKSHOP").IsVisible(), "Siege workshop opens siege recruitment."); Select(w, forge); }
			if (tick == 1110) { Check(Ui.Root.Get("MD_FORGE_PANEL").IsVisible(), "Forge opens production commands."); Select(w, church); }
			if (tick == 1140) { Check(Ui.Root.Get("MD_CHURCH_PANEL").IsVisible(), "Church retains priest and cemetery actions."); Select(w, depot); }
			if (tick == 1170) { Drag(w, true, 0); Quantity(w, 99999); Check(Ui.Root.Get<ButtonWidget>("MD_TRANSFER_CONFIRM").IsDisabled(), "Over-capacity amount cannot confirm."); Select(w, enemy); }
			if (tick == 1200) { Check(!Panel.Choosing && !Panel.IsVisible() && Ui.KeyboardFocusWidget == null, "Selection change closes quantity; enemy stock is not exposed."); Select(w, cart); }
			// Equipment and camp goods use separate inventories but the same synchronized order.
			if (tick == 1230) Transfer(w, cart, depot, 5, 2);
			if (tick >= 1410 && tick <= 1650 && (tick - 1410) % 60 == 0) Transfer(w, cart, depot, 6 + (tick - 1410) / 60, 2);
			if (tick == 1250) Select(w, weaver);
			if (tick == 1280) { Check(Ui.Root.Get("MD_TEXTILE_PANEL").IsVisible(), "Weaver opens cloth and tent production."); Click(w, "MD_CAMP_CLOTH_MORE_CONTEXT"); }
			if (tick == 1310) { Check(weaver.Trait<MedievalTentWorkshop>().ClothTarget > 0, "Contextual textile button issues its production order."); Select(w, house); }
			if (tick == 1340) { Check(Ui.Root.Get("MD_SHELTER_PANEL").IsVisible(), "House opens rest and occupancy controls."); Select(w, cart); }
			if (tick == 1695)
			{
				Drag(w, true, 0); Quantity(w, 3);
				Sync.RunUnsynced(w, () => w.SetPauseState(true));
				Game.RunAfterDelay(300, () => Sync.RunUnsynced(w, () =>
				{
					pauseChecked = w.Paused && Ui.Root.Get<ButtonWidget>("MD_TRANSFER_CONFIRM").IsDisabled();
					Check(pauseChecked, "Paused game disables quantity confirmation."); Click(w, "MD_TRANSFER_CANCEL"); w.SetPauseState(false);
				}));
			}
			if (tick == 1710)
			{
				for (var g = 5; g < StorageGoods.Count; g++) Check(StorageGoods.Amount(cart, g) == 2, "Equipment/camp commodity loaded: " + g);
				Transfer(w, farther, depot, 0, 10);
			}
			if (tick == 1740) w.IssueOrder(new Order("Stop", farther, false));
			if (tick == 1720)
			{
				w.IssueOrder(new Order("MedievalSupplySite", farther, Target.FromActor(depot), false));
				w.IssueOrder(new Order("MedievalCartCargo", farther, false) { ExtraData = uint.MaxValue });
			}
			if (tick == 1735) Check(farther.Trait<MedievalManualTransfer>().Busy, "Invalid supply-cart automatic orders preserve the manual booking.");
			if (tick == 1780)
			{
				Check(!farther.Trait<MedievalManualTransfer>().Busy && farther.Trait<MedievalStockpile>().Stock.Incoming(0) == 0 && depot.Trait<MedievalStockpile>().Stock.Reserved(0) == 0, "Stop cancels travel and releases both bookings.");
				w.IssueOrder(new Order("MedievalReloadCart", farther, false));
				Transfer(w, farther, depot, 0, 10); Select(w, farther);
			}
			if (tick == 1800)
			{
				Check(farther.Trait<MedievalSupplyCart>().ReloadTarget == 0 && farther.Trait<MedievalManualTransfer>().Busy,
					"Accepted manual transfer clears an old automatic reload destination.");
				w.AddFrameEndTask(_ => farther.ChangeOwner(enemy.Owner));
			}
			if (tick == 1840)
			{
				Check(!farther.Trait<MedievalManualTransfer>().Busy && depot.Trait<MedievalStockpile>().Stock.Reserved(0) == 0, "Captured cart releases previous owner's resource booking.");
				Check(!Panel.IsVisible() && Panel.Cart == null, "Captured selected cart no longer exposes its stock controls.");
				Check(pauseChecked, "Pause scenario ran.");
				Select(w, cart);
			}
			if (tick == 1870)
			{
				Drag(w, true, 0); Quantity(w, 3);
				w.IssueOrder(new Order("MedievalRecoverTrade", cart, false));
			}
			if (tick == 1890)
			{
				Check(Panel.Choosing && Panel.Cart == cart && Ui.Root.Get<ButtonWidget>("MD_TRANSFER_CONFIRM").IsDisabled(),
					"Cart becoming busy during quantity choice disables confirmation without substituting a different cart.");
				Click(w, "MD_TRANSFER_CANCEL"); w.IssueOrder(new Order("Stop", cart, false));
			}
			if (tick == 1920) Transfer(w, cart, market, 0, 5, false);
			if (tick == 1940)
			{
				Check(cart.Trait<MedievalManualTransfer>().Busy, "Manual unloading is travelling before replacement.");
				w.IssueOrder(new Order("MedievalRecoverTrade", cart, false));
			}
			if (tick == 1960)
			{
				Check(!cart.Trait<MedievalManualTransfer>().Busy && !cart.Trait<MedievalManualTransfer>().ManualControl && cart.Trait<MedievalTradeCart>().Active,
					"Valid automatic recovery replaces manual transfer and releases manual control.");
			}
			// A cancelled move may finish its current cell before the next logistics tick books cargo.
			if (tick >= 1960 && recoveryStopped == 0 && Enumerable.Range(5, 6).All(g => StorageGoods.Stock(market, g).Incoming(StorageGoods.Index(g)) == 2))
			{
				recoveryStopped = tick;
				w.IssueOrder(new Order("Stop", cart, false));
			}
			if (recoveryStopped > 0 && tick == recoveryStopped + 20)
			{
				for (var g = 5; g < StorageGoods.Count; g++) Check(StorageGoods.Amount(cart, g) == 2 && StorageGoods.Stock(market, g).Incoming(StorageGoods.Index(g)) == 0,
					"Stopping automatic recovery keeps equipment/camp cargo and releases booked space: " + g);
				w.IssueOrder(new Order("MedievalRecoverTrade", cart, false));
			}
			if (tick == 2600)
			{
				Check(recoveryStopped > 0, "Equipment/camp recovery reservations were observed and cancellation was tested.");
				for (var g = 5; g < StorageGoods.Count; g++)
					Check(StorageGoods.Amount(cart, g) == 0 && StorageGoods.Stock(market, g).Incoming(StorageGoods.Index(g)) == 0,
						"Automatic recovery unloads equipment/camp goods and releases destination reservations: " + g);
				Check(!cart.Trait<MedievalTradeCart>().Active && StorageGoods.Amount(cart, 0) == 0 && cart.Trait<MedievalStockpile>().Stock.Reserved(0) == 0,
					"Automatic recovery after manual transfer reaches its destination and leaves no departure reservation.");
				Transfer(w, cart, depot, 0, 3);
			}
			if (tick == 2610)
			{
				market.Dispose(); w.IssueOrder(new Order("MedievalRecoverTrade", cart, false));
			}
			if (tick == 2630)
			{
				Check(cart.Trait<MedievalManualTransfer>().Busy && depot.Trait<MedievalStockpile>().Stock.Reserved(0) == 3,
					"A vanished recovery market must not cancel the current manual transfer.");
				File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, pauseChecked }, new JsonSerializerOptions { WriteIndented = true }));
				done = true; Console.WriteLine("MEDIEVAL TRANSFERS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); Game.RunAfterDelay(300, Game.Exit);
			}
		}
	}
}
