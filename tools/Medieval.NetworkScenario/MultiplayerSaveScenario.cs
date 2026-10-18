using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.LoadScreens;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Mods.Medieval;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Platforms.NetworkTest
{
	// This load screen and trait are used only by the private network audit mod.
	public sealed class MultiplayerSaveLoadScreen : BlankLoadScreen
	{
		public static OrderManager Orders;
		public static string Resume;
		public static string SaveName;
		public static bool PauseBeforeSave;
		public override void StartGame(Arguments args)
		{
			Resume = args.GetValue("Audit.Resume", "");
			SaveName = args.GetValue("Audit.SaveName", "first.orasav");
			PauseBeforeSave = args.GetValue("Audit.Pause", "False") == "True";
			var host = args.GetValue("Audit.Host", "False") == "True";
			var slot = args.GetValue("Audit.Slot", "");
			var useMenu = host && Resume.Length > 0 && args.GetValue("Audit.UseMenu", "False") == "True";
			OrderManager om = null;
			var loadSent = useMenu;
			void Connected(OrderManager connected, string password, NetworkConnection connection)
			{
				if (connection.ConnectionState == ConnectionState.Connected)
					Orders = om = connected;
			}
			void Ready()
			{
				if (om == null || om.GameStarted || om.LocalClient == null) return;
				if (Resume.Length > 0 && !om.LobbyInfo.GlobalSettings.RestoringGameSave)
				{
					if (host && !loadSent)
					{
						loadSent = true;
						om.IssueOrder(Order.FromTargetString("LoadGameSave", Resume, true));
					}
					return;
				}
				if (om.LocalClient.Slot != slot)
				{
					if (!om.LocalClient.IsReady)
						om.IssueOrder(Order.Command("slot " + slot));
					return;
				}
				if (!om.LocalClient.IsReady)
					om.IssueOrder(Order.Command("state Ready"));
				File.WriteAllText(Path.Combine(Platform.SupportDir, "joined.txt"), om.LocalClient.Index.ToStringInvariant());
			}
			Game.LobbyInfoChanged += Ready;
			if (useMenu)
			{
				Game.LoadShellMap();
				Game.ConnectionStateChanged += Connected;
				Game.RunAfterDelay(100, () =>
				{
					var multiplayer = Ui.OpenWindow("MULTIPLAYER_PANEL", new WidgetArgs {
						{ "onStart", new Action(() => { }) }, { "onExit", new Action(() => { }) }, { "directConnectEndPoint", null }
					});
					var resume = multiplayer.Get<ButtonWidget>("RESUME_BUTTON");
					if (resume.IsDisabled()) throw new InvalidOperationException("Multiplayer resume button disabled");
					resume.OnClick();
					var window = Ui.Root.Get<Widget>("LOAD_GAME_BROWSER_PANEL");
					window.Get<ScrollPanelWidget>("GAME_LIST").Children.OfType<ScrollItemWidget>()
						.First(i => i.ItemKey != null && Path.GetFileName(i.ItemKey) == Resume).OnClick();
					var load = window.Get<ButtonWidget>("LOAD_BUTTON");
					if (load.IsDisabled()) throw new InvalidOperationException("Resume button disabled");
					load.OnClick();
					var create = Ui.Root.Get<ButtonWidget>("CREATE_BUTTON");
					create.OnClick();
				});
			}
			else
			{
				var endpoint = host ? Game.CreateServer(Game.Settings.Server.Clone()) : new LaunchArguments(args).GetConnectEndPoint();
				om = Orders = Game.JoinServer(endpoint, "", false);
			}
			Game.BeforeGameStart += () => { Game.LobbyInfoChanged -= Ready; Game.ConnectionStateChanged -= Connected; };
		}
	}

	public sealed class MultiplayerSaveAuditInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MultiplayerSaveAudit();
	}

	public sealed class MultiplayerSaveAudit : IWorldLoaded, ITick, INotifyGameSaved, INotifyGameLoaded
	{
		readonly List<object> checkpoints = [];
		readonly List<string> errors = [];
		WorldRenderer renderer;
		bool acknowledged, loaded, inspected, finished;
		int loadedTick;
		void Check(bool condition, string message) { if (!condition && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr) { renderer = wr; }
		void INotifyGameSaved.GameSaved(World w, bool auto)
		{
			acknowledged = true;
			if (MultiplayerSaveLoadScreen.PauseBeforeSave && MultiplayerSaveLoadScreen.Orders.LocalClient.IsAdmin)
				Game.RunAfterDelay(500, () => w.SetPauseState(false));
		}
		void INotifyGameLoaded.GameLoaded(World w)
		{
			loaded = true;
			loadedTick = w.WorldTick;
			if (MultiplayerSaveLoadScreen.PauseBeforeSave)
			{
				Check(w.Paused, "Saved pause state was lost");
				if (MultiplayerSaveLoadScreen.Orders.LocalClient.IsAdmin)
					Game.RunAfterDelay(500, () => w.SetPauseState(false));
			}
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (finished || w.LocalPlayer == null) return;
			var om = MultiplayerSaveLoadScreen.Orders;
			var tick = w.WorldTick;
			var local = w.LocalPlayer;
			Check(!om.IsOutOfSync, "Network synchronization diverged");
			if (!w.IsLoadingGameSave && !inspected)
			{
				inspected = true;
				Sync.RunUnsynced(w, () =>
				{
					var menu = Ui.OpenWindow("INGAME_MENU", new WidgetArgs {
						{ "world", w }, { "worldRenderer", renderer }, { "onExit", new Action(() => { }) },
						{ "initialPanel", IngameInfoPanel.AutoSelect }
					});
					Check((menu.GetOrNull<Widget>("SAVE_GAME") != null) == om.LocalClient.IsAdmin, "Save button must belong to host");
					Ui.CloseWindow();
				});
			}
			if (!w.IsLoadingGameSave)
			{
				if (tick == 150 && !om.LocalClient.IsAdmin)
				{
					// These bypass the UI to verify that authority is enforced by the server.
					w.IssueOrder(Order.FromTargetString("GameSaveTraitData", "invalid guest data", true));
					w.IssueOrder(Order.FromTargetString("CreateGameSave", "unauthorized.orasav", true));
				}
				if (tick == 10)
					w.IssueOrder(new Order("MedievalFoundMarket", w.Actors.Single(a => a.Owner == local && a.Info.Name == "md.caravan"), false));
				if (tick == 100 || tick == 500)
				{
					var market = w.Actors.FirstOrDefault(a => a.Owner == local && a.Info.Name == "md.foundingmarket");
					Check(market != null, "Market missing after restoration");
					if (market != null)
						w.IssueOrder(new Order("MedievalSetEmpireIdentity", market, false) { TargetString = $"Pays {local.InternalName} {tick}", ExtraData = 0x4488AA });
				}
				if (tick == (MultiplayerSaveLoadScreen.Resume.Length == 0 ? 300 : loadedTick + 60) && om.LocalClient.IsAdmin)
				{
					if (MultiplayerSaveLoadScreen.PauseBeforeSave)
					{
						w.SetPauseState(true);
						void SaveWhenPaused()
						{
							if (w.Paused) w.RequestGameSave(MultiplayerSaveLoadScreen.SaveName, false);
							else Game.RunAfterDelay(100, SaveWhenPaused);
						}
						Game.RunAfterDelay(100, SaveWhenPaused);
					}
					else w.RequestGameSave(MultiplayerSaveLoadScreen.SaveName, false);
				}
			}
			if (tick % 30 == 0) checkpoints.Add(new { tick, hash = w.SyncHash() });
			if (tick != 600) return;
			finished = true;
			Check(acknowledged, "Server did not acknowledge saving");
			Check(MultiplayerSaveLoadScreen.Resume.Length == 0 || loaded, "Game never finished restoring");
			Check(w.LobbyInfo.NonBotPlayers.Count() >= 2 && w.LobbyInfo.GlobalSettings.EnableGameSaves, "Multiplayer saves unavailable");
			var report = new { passed = errors.Count == 0, errors, checkpoints, loaded, loadedTick, acknowledged,
				client = om.LocalClient.Index, slot = om.LocalClient.Slot, markets = w.Actors.Count(a => a.Info.Name == "md.foundingmarket") };
			File.WriteAllText(Path.Combine(Platform.SupportDir, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine(JsonSerializer.Serialize(report));
			void WaitForPeers()
			{
				if (File.Exists(Path.Combine(Platform.SupportDir, "finish.txt"))) Game.Exit();
				else Game.RunAfterDelay(200, WaitForPeers);
			}
			Game.RunAfterDelay(200, WaitForPeers);
		}
	}
}
