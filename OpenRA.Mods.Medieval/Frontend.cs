using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.FileFormats;
using OpenRA.FileSystem;
using OpenRA.Mods.Common.LoadScreens;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalFrontendArt
	{
		public static void Cover(Renderer r, Sprite sprite, Rectangle bounds)
		{
			var scale = Math.Max(bounds.Width / sprite.Size.X, bounds.Height / sprite.Size.Y);
			var size = sprite.Size * scale;
			r.RgbaSpriteRenderer.DrawSprite(sprite, new Vector3(bounds.X + (bounds.Width - size.X) / 2,
				bounds.Y + (bounds.Height - size.Y) / 2, 0), scale);
		}
		public static void Shade(Renderer r, int width, int height)
		{
			var edge = Math.Min(width, 700);
			r.RgbaColorRenderer.FillRect(Vector3.Zero, new Vector3(edge, 0, 0), new Vector3(edge, height, 0), new Vector3(0, height, 0),
				Color.FromArgb(150, 9, 16, 14), Color.Transparent, Color.Transparent, Color.FromArgb(175, 9, 16, 14));
		}
		public static void Title(Renderer r, int x, int y)
		{
			if (r.Fonts == null || !r.Fonts.ContainsKey("MedievalTitle")) return;
			var gold = Color.FromArgb(205, 170, 106);
			r.Fonts["TinyBold"].DrawText("STRATÉGIE MÉDIÉVALE", new Vector2(x, y), gold);
			r.Fonts["MedievalTitle"].DrawText("LES TERRES", new Vector2(x - 2, y + 27), Color.FromArgb(246, 235, 210));
			r.Fonts["MedievalTitle"].DrawText("DU BOURG", new Vector2(x - 2, y + 70), Color.FromArgb(246, 235, 210));
			r.RgbaColorRenderer.FillRect(new Vector3(x, y + 124, 0), new Vector3(x + 62, y + 126, 0), gold);
			r.Fonts["Regular"].DrawText("Bâtir. Ravitailler. Conquérir.", new Vector2(x, y + 141), Color.FromArgb(219, 207, 182));
		}
	}
	public sealed class MedievalLoadScreen : BlankLoadScreen
	{
		SheetBuilder sheet;
		string imagePath;
		readonly Stopwatch refresh = new();
		public Sprite Image { get; private set; }
		public static bool AuditRequested { get; private set; }
		public static string AuditOutput { get; private set; }
		bool captured;
		public override void Init(Manifest manifest, IReadOnlyFileSystem fileSystem)
		{
			base.Init(manifest, fileSystem);
			imagePath = manifest.LoadScreen.ToDictionary(n => n.Value)["Image"];
		}
		public override void StartGame(Arguments args)
		{
			AuditRequested = args.GetValue("Medieval.AuditMenu", "False") == "True";
			AuditOutput = args.GetValue("Medieval.AuditOutput", Path.Combine(Platform.EngineDir, "analysis", "medieval", "finishing", "frontend"));
			if (AuditRequested) Directory.CreateDirectory(AuditOutput);
			base.StartGame(args);
		}
		void DisplayInner(Renderer r)
		{
			var width = r.Resolution.Width; var height = r.Resolution.Height;
			MedievalFrontendArt.Cover(r, Image, new Rectangle(0, 0, width, height));
			MedievalFrontendArt.Shade(r, width, height);
			var x = Math.Max(32, width / 16); var y = Math.Max(35, (height - 560) / 2);
			MedievalFrontendArt.Title(r, x, y);
			if (r.Fonts == null) return;
			r.Fonts["Bold"].DrawText("Préparation des terres…", new Vector2(x, height - 103), Color.FromArgb(246, 235, 210));
			r.Fonts["Regular"].DrawText("Les grandes régions demandent un peu plus de temps.", new Vector2(x, height - 77), Color.FromArgb(219, 207, 182));
			// Indeterminate activity, never a fabricated percentage of world loading.
			r.RgbaColorRenderer.FillRect(new Vector3(x, height - 39, 0), new Vector3(width - x, height - 37, 0), Color.FromArgb(105, 91, 68));
			var travel = Math.Max(1, width - x * 2 - 85);
			var at = x + (int)(Game.RunTime / 9 % travel);
			r.RgbaColorRenderer.FillRect(new Vector3(at, height - 40, 0), new Vector3(at + 85, height - 36, 0), Color.FromArgb(209, 173, 102));
		}
		public override void Display()
		{
			if (Game.Renderer == null || (refresh.IsRunning && refresh.ElapsedMilliseconds < 200)) return;
			if (Image == null)
			{
				using var stream = fileSystem.Open(imagePath);
				var png = new Png(stream);
				var size = 1;
				while (size < Math.Max(png.Width, png.Height) + 2) size *= 2;
				sheet = new SheetBuilder(SheetType.BGRA, size);
				Image = sheet.Add(png);
				Image.Sheet.GetTexture().ScaleFilter = TextureScaleFilter.Linear;
				Image.Sheet.ReleaseBuffer();
			}
			Game.Renderer.BeginUI();
			DisplayInner(Game.Renderer);
			Game.Renderer.EndFrame(new NullInputHandler());
			refresh.Restart();
			if (AuditRequested && !captured && Game.Renderer?.Fonts != null)
			{
				captured = true; Game.Renderer.SaveScreenshot(Path.Combine(AuditOutput, "loading.png"));
			}
		}
		protected override void Dispose(bool disposing)
		{
			if (disposing) sheet?.Dispose();
			base.Dispose(disposing);
		}
	}
	public sealed class MedievalBackdropWidget : Widget
	{
		public override void Draw()
		{
			var renderer = Game.Renderer;
			if (Game.ModData.LoadScreen is MedievalLoadScreen load && load.Image != null)
				MedievalFrontendArt.Cover(renderer, load.Image, RenderBounds);
			MedievalFrontendArt.Shade(renderer, RenderBounds.Width, RenderBounds.Height);
		}
		public override bool HandleMouseInput(MouseInput mi) => false;
	}
	public sealed class MedievalTitleWidget : Widget
	{
		public override void Draw() => MedievalFrontendArt.Title(Game.Renderer, RenderOrigin.X, RenderOrigin.Y);
		public override bool HandleMouseInput(MouseInput mi) => false;
	}
	public sealed class MedievalMainMenuLogic : MainMenuLogic
	{
		readonly Widget root;
		readonly MedievalFrontendAudit audit;
		[ObjectCreator.UseCtor]
		public MedievalMainMenuLogic(Widget widget, World world, ModData modData) : base(widget, world, modData)
		{
			root = widget;
			widget.Get("MD_TITLE").IsVisible = () => menuType != MenuType.None;
			var main = widget.Get("MAIN_MENU");
			var guide = main.Get<ButtonWidget>("CONTENT_BUTTON");
			guide.Disabled = false;
			guide.OnClick = () =>
			{
				menuType = MenuType.None;
				Ui.OpenWindow("MD_GUIDE", new WidgetArgs { { "onExit", () => menuType = MenuType.Main } });
			};
			var quick = widget.Get("SINGLEPLAYER_MENU").Get<ButtonWidget>("MISSIONS_BUTTON");
			quick.Disabled = false;
			quick.OnClick = () => Game.RunAfterTick(() => Game.LoadMap("fondation"));
			var choose = widget.Get("SINGLEPLAYER_MENU").Get<ButtonWidget>("SKIRMISH_BUTTON");
			choose.OnClick = () =>
			{
				// The base chooser prefers the newest map, which can require three humans.
				var solo = modData.MapCache.FirstOrDefault(m => m.Status == MapStatus.Available && Path.GetFileName(m.Path) == "fondation");
				if (solo == null) return;
				menuType = MenuType.None;
				ConnectionLogic.Connect(Game.CreateLocalServer(solo.Uid, isSkirmish: true), "", () =>
					Game.OpenWindow("SERVER_LOBBY", new WidgetArgs {
						{ "onExit", () => { Game.Disconnect(); menuType = MenuType.Singleplayer; } },
						{ "onStart", () => { root.Parent?.RemoveChild(root); lastGameState = MenuPanel.Skirmish; } },
						{ "skirmishMode", true } }),
					() => { Game.CloseServer(); menuType = MenuType.Singleplayer; });
			};
			widget.Get("EXTRAS_MENU").Get("MUSIC_BUTTON").IsVisible = () => false;
			if (MedievalLoadScreen.AuditRequested) audit = new MedievalFrontendAudit(widget, world);
		}
		public override void Tick() { base.Tick(); audit?.Tick(); }
	}
	public sealed class MedievalGuideLogic : ChromeLogic
	{
		[FluentReference]
		static readonly string[] Headings = ["md-guide-heading-0", "md-guide-heading-1", "md-guide-heading-2", "md-guide-heading-3", "md-guide-heading-4"];
		[FluentReference]
		static readonly string[] Bodies = ["md-guide-body-0", "md-guide-body-1", "md-guide-body-2", "md-guide-body-3", "md-guide-body-4"];
		[ObjectCreator.UseCtor]
		public MedievalGuideLogic(Widget widget, Action onExit)
		{
			var body = widget.Get<LabelWidget>("GUIDE_TEXT");
			var heading = widget.Get<LabelWidget>("GUIDE_HEADING");
			var selected = 0;
			for (var i = 0; i < 5; i++)
			{
				var page = i;
				var button = widget.Get<ButtonWidget>("GUIDE_TAB_" + i);
				button.IsHighlighted = () => selected == page;
				button.OnClick = () => selected = page;
			}
			heading.GetText = () => FluentProvider.GetMessage(Headings[selected]);
			body.GetText = () => FluentProvider.GetMessage(Bodies[selected]);
			widget.Get<ButtonWidget>("GUIDE_CLOSE").OnClick = () => { Ui.CloseWindow(); onExit(); };
		}
	}
	public sealed class MedievalLobbyPresentationLogic : ChromeLogic
	{
		readonly OrderManager orderManager;
		readonly Widget players;
		readonly LabelWidget serverName;
		public string MapUid => orderManager.LobbyInfo.GlobalSettings.Map;
		[ObjectCreator.UseCtor]
		public MedievalLobbyPresentationLogic(Widget widget, OrderManager orderManager)
		{
			this.orderManager = orderManager;
			players = widget.Get("LOBBY_PLAYERS");
			serverName = widget.Get<LabelWidget>("SERVER_NAME");
			foreach (var name in new[] { "SKIRMISH_TABS", "MULTIPLAYER_TABS" })
			{
				var tabs = widget.Get(name);
				tabs.Get("MUSIC_TAB").IsVisible = () => false;
				var servers = tabs.GetOrNull("SERVERS_TAB");
				if (servers != null) servers.Bounds.X = tabs.Get("MUSIC_TAB").Bounds.X;
			}
		}
		public override void Tick()
		{
			if (orderManager.LobbyInfo.GlobalSettings.ServerName == "Skirmish Game") serverName.GetText = () => "Votre domaine";
			foreach (var row in players.Children)
			{
				var name = row.GetOrNull<LabelWidget>("FACTIONNAME");
				if (name == null) continue;
				// Countries and heraldry are chosen at the market, not from Red Alert factions.
				name.GetText = () => "À personnaliser";
				name.Bounds.X = 8; name.Bounds.Width = 123;
				row.Get("FACTIONFLAG").IsVisible = () => false;
				if (row.GetOrNull("FACTION") is DropDownButtonWidget dropdown)
				{
					dropdown.IsDisabled = () => true;
					dropdown.GetTooltipText = () => "Choisissez le nom et l’emblème de votre pays depuis votre marché.";
				}
			}
		}
	}
	public sealed class MedievalStartCameraLogic : ChromeLogic
	{
		public const float MinimumZoomScale = .25f;
		[ObjectCreator.UseCtor]
		public MedievalStartCameraLogic(World world, WorldRenderer worldRenderer)
		{
			// Keep the normal starting distance, but let the wheel reveal four times
			// the width of terrain. Also applies when resuming a saved game.
			if (world.Type == WorldType.Regular && world.LocalPlayer != null)
				worldRenderer.Viewport.UnlockMinimumZoom(MinimumZoomScale);
			if (world.Type != WorldType.Regular || world.IsLoadingGameSave || world.IsReplay || world.LocalPlayer == null) return;
			Game.RunAfterTick(() =>
			{
				if (!Game.IsCurrentWorld(world)) return;
				var caravan = world.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && a.Info.HasTraitInfo<MedievalFoundingCaravanInfo>());
				if (caravan != null) Sync.RunUnsynced(world, () =>
				{
					worldRenderer.Viewport.Center(caravan.CenterPosition);
					world.Selection.Combine(world, [caravan], false, true);
				});
			});
		}
	}
	public sealed class MedievalPauseOverviewLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalPauseOverviewLogic(Widget widget, World world)
		{
			var overview = widget.Get("MD_PAUSE_OVERVIEW");
			overview.IsVisible = () => world.Type == WorldType.Regular && widget.Get("MENU_BUTTONS").IsVisible();
			widget.Get<LabelWidget>("MD_PAUSE_REGION").GetText = () => world.Map.Title;
			var nextRefresh = 0;
			var status = "";
			widget.Get<LabelWidget>("MD_PAUSE_STATUS").GetText = () =>
			{
				if (Game.LocalTick < nextRefresh) return status;
				nextRefresh = Game.LocalTick + 25;
				var player = world.LocalPlayer;
				if (player == null) return status = "Vous observez cette région.";
				return status = MedievalHeraldry.For(player).CountryName + "\n\n"
					+ $"Habitants : {MedievalSettlement.Population(player)} / {MedievalSettlement.Housing(player)} logements\n"
					+ $"Besoins alimentaires : {MedievalFoodEconomy.Demand(player)} par minute\n\n"
					+ "Développez vos récoltes, reliez les marchés et protégez les convois.\n"
					+ "Les citoyens et les soldats ont besoin de ravitaillement et de repos.\n\n"
					+ "Le domaine se développe librement. La conquête ne déclenche pas encore de victoire automatique.";
			};
		}
	}
	public sealed class MedievalAudioPresentationLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalAudioPresentationLogic(Widget widget)
		{
			foreach (var id in new[] { "CASH_TICKS_CONTAINER", "VIDEO_VOLUME_CONTAINER", "MUTE_BACKGROUND_MUSIC_CONTAINER", "MUSIC_VOLUME_CONTAINER" })
				widget.Get(id).IsVisible = () => false;
			widget.Get("MUSIC_VOLUME_CONTAINER").Parent.IsVisible = () => false;
		}
	}
}
