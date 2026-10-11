using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSiegeHeraldryValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeHeraldryValidation();
	}
	// Opt-in native scene: real assembly/packing in all eight facings, then a
	// market identity order. Actual enemy capture is covered by SiegeCaptureValidation.
	public sealed class MedievalSiegeHeraldryValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor market;
		int tick, stage, until;
		bool done, assemblyShot, packingShot;
		readonly List<Actor> engines = [];
		readonly List<string> errors = [];
		readonly HashSet<int> frames = [], assembly = [], packing = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "siege-heraldry", "native"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, Player owner, CPos cell, int facing = 0)
		{
			var a = w.CreateActor("md." + type, [new OwnerInit(owner), new LocationInit(cell), new FacingInit(new WAngle(facing * 128))]);
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); return a;
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			w.LocalPlayer.Shroud.Disabled = true; var foe = w.Players.Single(p => p.InternalName == "Rival");
			MedievalHeraldry.For(w.LocalPlayer).SetEmpire("Lion rubis", 0xB63838, 0); MedievalHeraldry.For(foe).SetEmpire("Aigle azur", 0x356FAD, 1);
			market = Spawn(w, "market", w.LocalPlayer, Cell(14, 18));
			for (var row = 0; row < 2; row++)
				for (var col = 0; col < 8; col++)
				{
					var cell = Cell(15 + col * 3 + row * 5, 40 - col * 3 + row * 5);
					var a = Spawn(w, row == 0 ? "ram" : "trebuchet", col < 4 ? w.LocalPlayer : foe, cell, col); engines.Add(a);
					if (row == 1) { var soldier = Spawn(w, "swordsman", a.Owner, cell + new CVec(0, 3)); soldier.Trait<MedievalSiegeCrew>().Assign(soldier, a); }
				}
			Sync.RunUnsynced(w, () => { w.Selection.Clear(); renderer.Viewport.UnlockMinimumZoom(.75f); renderer.Viewport.AdjustZoom((float)Math.Log(.85f / renderer.Viewport.Zoom)); });
		}
		internal static void CheckRender(Actor a, WorldRenderer wr, Action<bool, string> check)
		{
			var drawn = ((IRender)a.Trait<MedievalSiegeHeraldry>()).Render(a, wr).ToArray();
			check(drawn.OfType<SiegeLiveryRenderable>().Single().Color == MedievalHeraldry.For(a.Owner).Color, "Siege cloth must follow its current owner's national color.");
			check(drawn.OfType<IPalettedRenderable>().Single().Palette?.Name == "md-livery" + a.Owner.InternalName, "Siege flag must use its current owner's palette.");
			check(drawn.All(r => !r.PrepareRender(wr).ScreenBounds(wr).IsEmpty), "Cloth and flag need viewport bounds.");
		}
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		void Finish()
		{
			Check(stage == 3, "All engines must finish assembly, recoloring and packing.");
			Check(frames.Count == 12 && assembly.Count == 48 && packing.Count == 48, "All flutter, assembly and reversed packing frames must be observed.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, stage, engines = engines.Count, facings = 8, flutterFrames = frames.Count, assemblyFrames = assembly.Count, packingFrames = packing.Count }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SIEGE HERALDRY: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(31, 29))));
			frames.Add(MedievalCartHeraldry.FlagFrame(engines[0]));
			var trebuchets = engines.Skip(8).ToArray(); var first = trebuchets[0].Trait<MedievalSiegeEngine>(); var body = trebuchets[0].Trait<WithMedievalSiegeBody>();
			if (body.Sequence == "assemble") (first.State == 3 ? packing : assembly).Add(body.Frame);
			if (tick % 25 == 0) Sync.RunUnsynced(w, () => { foreach (var a in engines) CheckRender(a, renderer, Check); });
			if (tick == 70) Shot("01-eight-directions-packed-red-blue");
			if (stage == 0 && tick >= 90)
			{
				foreach (var a in trebuchets.Where(a => a.Trait<MedievalSiegeEngine>().Packed && !a.Trait<MedievalSiegeEngine>().Busy)) w.IssueOrder(new Order("MedievalSiegeDeploy", a, false));
				if (!assemblyShot && body.Sequence == "assemble" && body.Frame >= 29) { Shot("02-assembling"); assemblyShot = true; }
				if (trebuchets.All(a => a.Trait<MedievalSiegeEngine>().Deployed)) { stage = 1; until = tick + 35; }
			}
			if (stage == 1 && tick == until)
			{
				Shot("03-eight-directions-deployed-red-blue"); w.IssueOrder(new Order("MedievalSetEmpireIdentity", market, false) { TargetString = "Terres du Cerf", ExtraData = 0x0439814B }); stage = 2; until = tick + 40;
			}
			if (stage == 2 && tick == until)
			{
				Check(MedievalHeraldry.For(w.LocalPlayer).Hex == "39814B" && MedievalHeraldry.For(w.LocalPlayer).Emblem == 4, "Market identity changes must reach existing siege engines.");
				Check(MedievalHeraldry.For(trebuchets[7].Owner).Hex == "356FAD", "Enemy identity must remain independent.");
				Shot("04-updated-green-deer"); foreach (var a in trebuchets) w.IssueOrder(new Order("MedievalSiegePack", a, false));
			}
			if (stage == 2 && tick > until)
			{
				if (!packingShot && first.State == 3 && body.Frame <= 23) { Shot("05-packing"); packingShot = true; }
				if (trebuchets.All(a => a.Trait<MedievalSiegeEngine>().Packed)) { stage = 3; until = tick + 35; }
			}
			if (stage == 3 && tick == until) { Shot("06-packed-green-blue"); Finish(); }
			if (tick > 2000) Finish();
		}
	}
}
