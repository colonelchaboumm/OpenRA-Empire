using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalHerdSpawnValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalHerdSpawnValidation();
	}
	public sealed class MedievalHerdSpawnValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, count;
		bool valid = true;
		uint[] ids;
		Actor anchor;
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "herds"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "spawn-result.json"), "{\"passed\":false,\"status\":\"running\"}");
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; tick++;
			if (tick == 15)
			{
				var all = w.ActorsHavingTrait<MedievalAnimal>().ToArray(); count = all.Length; ids = all.Select(a => a.ActorID).Order().ToArray();
				anchor = w.ActorsHavingTrait<MedievalFoundingCaravan>().First(a => a.Owner == w.LocalPlayer);
				valid &= count >= 8 && count <= 400 && all.All(a => a.Owner.NonCombatant && a.Trait<MedievalAnimal>().Pen == null && w.Map.Contains(a.Location));
				var mobile = anchor.Trait<Mobile>(); var path = w.WorldActor.Trait<PathFinder>();
				foreach (var kind in new[] { "md.cow", "md.sheep" }) valid &= all.Count(a => a.Info.Name == kind && (a.CenterPosition - anchor.CenterPosition).HorizontalLengthSquared < 32768L * 32768
					&& path.PathExistsForLocomotor(mobile.Locomotor, anchor.Location, a.Location)) >= 2;
			}
			if (tick is 100 or 200 or 300)
			{
				var cell = new MPos(w.Map.Bounds.Left + w.Map.Bounds.Width * tick / 400, w.Map.Bounds.Top + w.Map.Bounds.Height * tick / 400).ToCPos(w.Map);
				Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(cell)));
			}
			if (tick == 400) Sync.RunUnsynced(w, () => renderer.Viewport.Center(anchor.CenterPosition));
			if (tick != 450) return;
			var stableIds = ids.SequenceEqual(w.ActorsHavingTrait<MedievalAnimal>().Select(a => a.ActorID).Order());
			valid &= stableIds;
			Game.Renderer.SaveScreenshot(Path.Combine(output, "wild-herds-start.png"));
			File.WriteAllText(Path.Combine(output, "spawn-result.json"), JsonSerializer.Serialize(new { passed = valid, map = w.Map.Title, count, tick, stableIds }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL WILD HERDS: " + (valid ? "PASS" : "FAIL") + $" ({count} animals)"); Game.RunAfterDelay(1000, Game.Exit);
		}
	}
}
