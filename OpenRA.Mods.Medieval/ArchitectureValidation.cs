using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalArchitectureValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalArchitectureValidation();
	}
	public sealed class MedievalArchitectureValidation : IWorldLoaded, ITick, IRenderAboveWorld
	{
		static readonly string[] Types = ["house", "house", "house", "house", "house", "house", "warehouse", "sawmill", "quarry", "farm", "ranch", "market",
			"marketstall", "forge", "barracks", "siegeworkshop", "sheepfold", "tentweaver", "huntinglodge", "goldmine", "church", "woodtower", "militarytent", "well", "foundingmarket"];
		readonly List<Actor> actors = [];
		readonly List<string> errors = [];
		readonly List<object> checkedActors = [];
		WorldRenderer renderer;
		string output;
		int tick, group;
		bool done, outlines = true;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "perspective", "native"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			actors.Clear();
			for (var i = group * 6; i < Math.Min(Types.Length, (group + 1) * 6); i++)
			{
				var n = i % 6; var col = n % 3; var row = n / 3;
				var initializers = new TypeDictionary { new OwnerInit(w.LocalPlayer), new LocationInit(Cell(24 + col * 4 + row * 7, 36 - col * 4 + row * 7)) };
				if (Types[i] == "house") initializers.Add(new MedievalHouseStyleInit(i));
				actors.Add(w.CreateActor("md." + Types[i], initializers));
			}
		}
		void Inspect(World w)
		{
			foreach (var a in actors)
			{
				var projection = MedievalArchitecture.For(a); var body = a.Render(renderer).OfType<SpriteRenderable>().Where(r => r.Pos == a.CenterPosition).ToArray();
				Check(body.Length > 0, "Missing architecture sprite " + a.Info.Name);
				foreach (var sprite in body)
				{
					Check(projection == ArchitectureProjection.Identity ? !sprite.ScreenTransform.HasValue : sprite.ScreenTransform == projection.Matrix, "Live projection mismatch " + a.Info.Name);
					var bounds = sprite.ScreenBounds(renderer); Check(bounds.Width > 0 && bounds.Height > 0, "Invalid transformed bounds " + a.Info.Name);
					Check(a.ScreenBounds(renderer).Any(b => b.Left <= bounds.Left && b.Top <= bounds.Top && b.Right >= bounds.Right && b.Bottom >= bounds.Bottom), "Culling clips architecture " + a.Info.Name);
				}
				var preview = (SpriteRenderable)MedievalPlaceOrderGenerator.Preview(w, a.Info.Name, a.Location, true, renderer: renderer);
				var style = a.Info.Name == "md.house" ? HouseStyleDeck.Next(w.LocalPlayer) : 0;
				var expected = MedievalArchitecture.For(a.Info.Name, style);
				Check(expected == ArchitectureProjection.Identity ? !preview.ScreenTransform.HasValue : preview.ScreenTransform == expected.Matrix, "Placement projection mismatch " + a.Info.Name);
				var originalTiles = a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location).ToArray();
				Check(originalTiles.Length > 0, "Visual correction cannot erase the footprint.");
				checkedActors.Add(new { type = a.Info.Name, style = a.TraitOrDefault<MedievalHouseAppearance>()?.Style, cells = originalTiles.Length,
					sx = projection.Matrix.M11, sy = projection.Matrix.M22, shear = projection.Matrix.M12, dx = projection.Matrix.M41, dy = projection.Matrix.M42 });
			}
		}
		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			if (done || !outlines) return;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			foreach (var a in actors.Where(a => a.IsInWorld))
				foreach (var cell in a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location))
				{
					var center = self.World.Map.CenterOfCell(cell); var corners = self.World.Map.Grid.Ramps[0].Corners;
					for (var i = 0; i < 4; i++) cr.DrawLine(wr.Screen3DPxPosition(center + corners[i]), wr.Screen3DPxPosition(center + corners[(i + 1) % 4]), .7f, Color.FromArgb(120, 225, 232, 134));
				}
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1)
			{
				w.LocalPlayer.Shroud.Disabled = true;
				renderer.Viewport.AdjustZoom((float)Math.Log(1.25f / renderer.Viewport.Zoom));
				w.AddFrameEndTask(Setup); return;
			}
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(31, 34))));
			var age = (tick - 2) % 140;
			if (age == 20)
			{
				foreach (var a in actors)
				{
					a.TraitOrDefault<MedievalBuildingAnimation>()?.Receive(a.Info.Name == "md.quarry" ? 3 : 0, 1);
					a.TraitOrDefault<MedievalGoldWorkshopAnimation>()?.Receive(4, 1);
				}
			}
			if (age == 40) { Sync.RunUnsynced(w, () => Inspect(w)); Game.Renderer.SaveScreenshot(Path.Combine(output, $"group-{group}-grid.png")); outlines = false; }
			if (age == 60) Game.Renderer.SaveScreenshot(Path.Combine(output, $"group-{group}-day.png"));
			if (age == 70)
			{
				var seasons = w.WorldActor.Trait<MedievalSeasons>().Clock; var day = w.WorldActor.Trait<MedievalDayNight>().Clock;
				while (seasons.Season != VillageSeason.Winter || seasons.Snow < 950) seasons.Advance();
				while (!day.Night) day.Advance();
			}
			if (age == 90) Game.Renderer.SaveScreenshot(Path.Combine(output, $"group-{group}-winter-night.png"));
			if (age != 139) return;
			if (++group * 6 < Types.Length)
			{
				var seasons = w.WorldActor.Trait<MedievalSeasons>().Clock; var day = w.WorldActor.Trait<MedievalDayNight>().Clock;
				while (seasons.Season != VillageSeason.Summer || seasons.Elapsed < 800) seasons.Advance();
				while (day.Night) day.Advance();
				outlines = true; w.AddFrameEndTask(Setup); return;
			}
			Check(checkedActors.Count == Types.Length, "Every architecture and all six house styles must be inspected.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, checkedActors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL ARCHITECTURE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			done = true; Game.RunAfterDelay(700, Game.Exit);
		}
	}
}
