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
	public class MedievalCartHeraldryValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCartHeraldryValidation();
	}
	public sealed class MedievalCartHeraldryValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Player foe;
		Actor market, captured, fresh;
		MedievalCartHeraldry destroyedLivery;
		int tick;
		bool done, moved;
		readonly List<Actor> carts = [];
		readonly List<string> errors = [];
		readonly HashSet<int> frames = [];
		readonly Dictionary<uint, WPos> starts = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "cart-heraldry", "native"); Directory.CreateDirectory(output);
		}
		Actor Spawn(World w, string type, Player owner, CPos cell, int facing = 0) => w.CreateActor("md." + type,
			[new OwnerInit(owner), new LocationInit(cell), new FacingInit(new WAngle(facing * 128)), new MedievalStockInit(type == "stonecart" ? [0, 0, 0, 20, 0] : [20, 0, 0, 0, 0])]);
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			w.LocalPlayer.Shroud.Disabled = true; foe = w.Players.Single(p => p.InternalName == "Creeps");
			MedievalHeraldry.For(w.LocalPlayer).SetEmpire("Lion rubis", 0xB63838, 0); MedievalHeraldry.For(foe).SetEmpire("Aigle azur", 0x356FAD, 1);
			market = Spawn(w, "market", w.LocalPlayer, Cell(22, 23));
			var types = new[] { "supplycart", "tradecart", "stonecart", "caravan" };
			for (var row = 0; row < types.Length; row++)
				for (var col = 0; col < 8; col++)
				{
					var a = Spawn(w, types[row], col < 4 ? w.LocalPlayer : foe, Cell(20 + col * 2 + row * 3, 36 - col * 2 + row * 3), col);
					carts.Add(a); starts[a.ActorID] = a.CenterPosition;
				}
			Sync.RunUnsynced(w, () => { w.Selection.Clear(); renderer.Viewport.AdjustZoom((float)Math.Log(1.1f / renderer.Viewport.Zoom)); });
		}
		void CheckRender(World w, Actor actor) => Sync.RunUnsynced(w, () =>
		{
			var drawn = ((IRender)actor.Trait<MedievalCartHeraldry>()).Render(actor, renderer).ToArray();
			Check(drawn.OfType<CartLiveryRenderable>().Single().Color == MedievalHeraldry.For(actor.Owner).Color, "Cloth must follow its owner's current national color: " + actor.Info.Name);
			Check(drawn.OfType<IPalettedRenderable>().Single().Palette?.Name == "md-livery" + actor.Owner.InternalName, "The animated emblem must use the current owner's palette: " + actor.Info.Name);
			Check(drawn.All(r => !r.PrepareRender(renderer).ScreenBounds(renderer).IsEmpty), "Cloth and flag need bounds for viewport culling.");
		});
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(33, 32))));
			frames.Add(MedievalCartHeraldry.FlagFrame(carts[0]));
			if (tick == 30) foreach (var a in carts) CheckRender(w, a);
			if (tick == 70) Shot("01-eight-directions-red-blue");
			if (tick == 90) foreach (var a in carts.Where((a, i) => i % 8 == 1)) w.IssueOrder(new Order("Move", a, Target.FromCell(w, a.Location + new CVec(2, 1)), false));
			if (tick == 145) Shot("02-moving-flags");
			if (tick == 160) w.IssueOrder(new Order("MedievalSetEmpireIdentity", market, false) { TargetString = "Terres du Cerf", ExtraData = 0x0439814B });
			if (tick == 200)
			{
				Check(MedievalHeraldry.For(w.LocalPlayer).Emblem == 4 && MedievalHeraldry.For(w.LocalPlayer).Hex == "39814B", "A real market order must update existing cart colors and emblems.");
				Check(MedievalHeraldry.For(foe).Hex == "356FAD", "An enemy country's colors must remain independent.");
				foreach (var a in carts) CheckRender(w, a);
				moved = carts.Where((a, i) => i % 8 == 1).All(a => a.CenterPosition != starts[a.ActorID]); Check(moved, "All four vehicle types must retain normal movement.");
				Shot("03-existing-carts-green-deer"); captured = carts[0]; w.AddFrameEndTask(_ => captured.ChangeOwner(foe));
			}
			if (tick == 230) { CheckRender(w, captured); w.AddFrameEndTask(_ => fresh = Spawn(w, "supplycart", w.LocalPlayer, Cell(29, 34), 5)); }
			if (tick == 260) { CheckRender(w, fresh); Shot("04-new-cart-and-captured-cart"); }
			if (tick == 285) { destroyedLivery = captured.Trait<MedievalCartHeraldry>(); captured.Trait<Health>().InflictDamage(captured, captured, new Damage(100000), true); }
			if (tick == 300)
			{
				Sync.RunUnsynced(w, () => Check(!((IRender)destroyedLivery).Render(captured, renderer).Any(), "Destroyed carts must not leave a floating flag."));
				Check(frames.Count == 12, "All twelve flutter frames must play.");
				Check(carts.Where(a => a != captured).All(a => a.Trait<MedievalStockpile>().Stock.Amount(a.Info.Name == "md.stonecart" ? 3 : 0) == 20), "Livery must never change the physical cargo.");
				File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, variants = 4, facings = 8, frames = frames.Count, moved, recolored = true, ownershipChanged = true }, new JsonSerializerOptions { WriteIndented = true }));
				Console.WriteLine("MEDIEVAL CART HERALDRY: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
			}
		}
	}
}
