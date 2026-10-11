using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalHerdValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalHerdValidation();
	}
	public sealed class MedievalHerdValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor cowPen, sheepPen, receiving, enemyPen, singlePen, breeder, shepherd, thief, ranch, fold, transferred, cancelled, interceptor;
		Player rival, neutral;
		int tick, phase, since, herdCountBefore, foodBefore;
		bool done, calves, lambs, ledCow, ledSheep, captured, transferredByWorker, stolen, cancelledEscort, producedMeat, producedWool, intercepted, guideDeath;
		readonly List<string> errors = [];
		readonly HashSet<int> breederCaptureFrames = [], shepherdCaptureFrames = [];
		bool breederLeadPose, shepherdLeadPose, youngCowArt, youngSheepArt;
		int youngSeenAt;
		Actor gateFollower;
		bool neighboringGateArrival;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string text) { if (!ok && !errors.Contains(text)) { errors.Add(text); Console.WriteLine($"HERD CHECK {tick}: {text}"); } }
		Actor Spawn(World w, string type, int x, int y, Player owner = null) => w.CreateActor("md." + type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, false));
		void Stop(World w, Actor a) => w.IssueOrder(new Order("Stop", a, false));
		void Next() { phase++; since = tick; Console.WriteLine($"HERD PHASE {phase} at {tick}"); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "herds"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"status\":\"running\"}");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			rival = w.Players.Single(p => p.InternalName == "Rival"); neutral = w.Players.Single(p => p.InternalName == "Neutral");
			w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask; rival.EnemyPlayersMask = w.LocalPlayer.PlayerMask; rival.Shroud.Disabled = true;
			Spawn(w, "market", 31, 23).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "market", 49, 24, rival).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "house", 24, 23); Spawn(w, "house", 26, 23);
			ranch = Spawn(w, "ranch", 29, 26); cowPen = Spawn(w, "pasture.4x4", 28, 31); receiving = Spawn(w, "pasture.4x4", 36, 31);
			Spawn(w, "ranch", 46, 26, rival); enemyPen = Spawn(w, "pasture.4x4", 46, 32, rival);
			fold = Spawn(w, "sheepfold", 34, 41); sheepPen = Spawn(w, "sheeppasture.4x4", 34, 44);
			singlePen = Spawn(w, "sheeppasture.2x4", 40, 44);
			var single = Spawn(w, "sheep", 41, 47); Check(singlePen.Trait<MedievalLivestock>().Enter(single), "Single sheep fixture must enter its pen.");
			breeder = Spawn(w, "breeder", 27, 36); shepherd = Spawn(w, "shepherd", 34, 49);
			thief = Spawn(w, "militia", 43, 36, rival); thief.Trait<AutoTarget>().SetStance(thief, UnitStance.HoldFire);
			Spawn(w, "cow", 26, 38, neutral); Spawn(w, "cow", 28, 39, neutral);
			Spawn(w, "sheep", 33, 50, neutral); Spawn(w, "sheep", 35, 51, neutral);
			Check(cowPen.Trait<MedievalLivestock>().Count == 0 && sheepPen.Trait<MedievalLivestock>().Count == 0, "New pens must start empty.");
			Check(!cowPen.Trait<MedievalSource>().NeedsWork && !sheepPen.Trait<MedievalSource>().NeedsWork, "Empty pens cannot manufacture meat or wool.");
		}
		void Finish(World w)
		{
			Check(breederCaptureFrames.Count >= 6 && shepherdCaptureFrames.Count >= 6, "Each profession must perform the capture gesture through its successive stages.");
			Check(breederLeadPose && shepherdLeadPose, "Both guides must use the dedicated leading walk.");
			Check(youngCowArt && youngSheepArt, "Calves and lambs must use their own full-scale sprite sheets.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, phase, calves, lambs,
				ledCow, ledSheep, captured, transferredByWorker, stolen, cancelledEscort, producedMeat, producedWool, intercepted, guideDeath,
				breederCaptureFrames, shepherdCaptureFrames, breederLeadPose, shepherdLeadPose, youngCowArt, youngSheepArt,
				neighboringGateArrival,
				meat = ranch.Trait<MedievalStockpile>().Stock.Amount(2), wool = fold.Trait<MedievalCampStore>().Stock.Amount(0) }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL HERDS: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(35, 36))));
			var cows = cowPen.Trait<MedievalLivestock>(); var sheep = sheepPen.Trait<MedievalLivestock>();
			foreach (var pen in new[] { cowPen, sheepPen, receiving, enemyPen, singlePen })
			{
				var herd = pen.Trait<MedievalLivestock>();
				Check(herd.Count + herd.Incoming <= herd.Capacity, "Occupants plus reserved arrivals must never exceed pen capacity.");
				Check(herd.Animals.All(a => a.Trait<MedievalAnimal>().Pen == pen && a.Owner == pen.Owner && a.IsInWorld && !a.IsDead), "Every displayed animal must be real, alive and owned by its pen.");
			}
			calves |= cows.Animals.Any(a => !a.Trait<MedievalAnimal>().Adult); lambs |= sheep.Animals.Any(a => !a.Trait<MedievalAnimal>().Adult);
			youngCowArt |= cows.Animals.Any(a => a.Trait<MedievalAnimal>().VisualImage == "md.livestock-cow-young");
			youngSheepArt |= sheep.Animals.Any(a => a.Trait<MedievalAnimal>().VisualImage == "md.livestock-sheep-young");
			if (youngCowArt && youngSheepArt && youngSeenAt == 0) youngSeenAt = tick;
			if (youngSeenAt > 0 && tick == youngSeenAt + 20) Game.Renderer.SaveScreenshot(Path.Combine(output, "adults-and-young.png"));
			foreach (var guide in new[] { breeder, shepherd })
			{
				var body = guide.Trait<WithMedievalInfantryBody>();
				if (body.Sequence == "catch")
				{
					var frames = guide == breeder ? breederCaptureFrames : shepherdCaptureFrames;
					if (frames.Add(body.Frame % 8) && body.Frame % 8 is 2 or 5 or 7)
						Game.Renderer.SaveScreenshot(Path.Combine(output, $"{guide.Info.Name}-capture-{body.Frame % 8}.png"));
				}
				if (body.Sequence == "lead")
				{
					if (guide == breeder) breederLeadPose = true; else shepherdLeadPose = true;
				}
			}
			if (phase == 3 && tick == since + 30) Game.Renderer.SaveScreenshot(Path.Combine(output, "animal-transfer-panel.png"));
			ledCow |= breeder.Trait<MedievalHerding>().Animal?.Trait<MedievalAnimal>().Escort == breeder;
			ledSheep |= shepherd.Trait<MedievalHerding>().Animal?.Trait<MedievalAnimal>().Escort == shepherd;
			if (tick % 500 == 0) Console.WriteLine($"HERDS {tick} phase {phase}: cows {cows.Count}+{cows.Incoming}, sheep {sheep.Count}+{sheep.Incoming}; {breeder.Trait<MedievalHerding>().Status}; {shepherd.Trait<MedievalHerding>().Status}");
			if (phase == 0)
			{
				if (cows.Count >= 2) Stop(w, breeder);
				if (sheep.Count >= 2) Stop(w, shepherd);
				if (cows.Count >= 2 && sheep.Count >= 2)
				{
					captured = ledCow && ledSheep; Check(captured, "Both species must be physically escorted one by one by the correct worker."); Next();
				}
			}
			else if (phase == 1 && cows.Count == cows.Capacity && sheep.Count == sheep.Capacity)
			{
				Check(calves && lambs, "Both species must produce visible young before they mature.");
				Check(singlePen.Trait<MedievalLivestock>().Count == 1, "One animal cannot reproduce alone.");
				Select(w, cowPen); Game.Renderer.SaveScreenshot(Path.Combine(output, "populated-pens.png")); Next();
			}
			else if (phase == 2 && tick - since >= 350)
			{
				Check(cows.Count == 4 && sheep.Count == 4, "Breeding must stop at the real capacity.");
				transferred = cows.Animals.First();
				Check(!transferred.Trait<MedievalAnimal>().Request(transferred, sheepPen, w.LocalPlayer), "Cow to sheep pen transfer must be rejected.");
				Select(w, transferred);
				Sync.RunUnsynced(w, () =>
				{
					var button = Ui.Root.Get<ButtonWidget>("MD_HERD_TRANSFER"); Check(button.IsVisible() && !button.IsDisabled(), "Animal transfer button must be usable."); button.OnClick();
					var gen = (OrderGenerator)w.OrderGenerator;
					foreach (var o in gen.Order(w, receiving.Location + new CVec(1, 1), int2.Zero, new MouseInput(MouseInputEvent.Down, gen.ActionButton, int2.Zero, int2.Zero, Modifiers.None, 1))) w.IssueOrder(o);
				});
				Next();
			}
			else if (phase == 3 && transferred.Trait<MedievalAnimal>().Pen == receiving)
			{
				transferredByWorker = true; Check(!cows.Animals.Contains(transferred), "Transferred actor must leave the first herd, without duplication.");
				Stop(w, breeder);
				Check(MedievalHerding.CanHandle(thief, transferred) && !MedievalHerding.CanHandle(shepherd, transferred), "Enemy soldiers may steal, but a shepherd cannot handle cattle.");
				Check(!transferred.Trait<MedievalAnimal>().Request(transferred, enemyPen, rival), "The rival cannot transfer an animal remotely through the owner UI.");
				w.IssueOrder(new Order("MedievalGatherAnimal", thief, Target.FromActor(transferred), false));
				Next();
			}
			else if (phase == 4)
			{
				if (transferred.Owner == rival && transferred.Trait<MedievalAnimal>().Pen == null && transferred.Trait<MedievalAnimal>().Escort == thief && !stolen)
				{
					stolen = true; Select(w, transferred); Game.Renderer.SaveScreenshot(Path.Combine(output, "stolen-cow-escort.png"));
				}
				if (transferred.Trait<MedievalAnimal>().Pen == enemyPen)
				{
					Check(stolen && transferred.Owner == rival && !receiving.Trait<MedievalLivestock>().Animals.Contains(transferred), "Stolen animal must physically arrive in the rival pen.");
					cancelled = Spawn(w, "cow", 35, 37, neutral);
					w.IssueOrder(new Order("MedievalGatherAnimal", breeder, Target.FromActor(cancelled), false)); Next();
				}
			}
			else if (phase == 5 && cancelled.Trait<MedievalAnimal>().Escort == breeder)
			{
				Stop(w, breeder); Next();
			}
			else if (phase == 6 && tick - since >= 35)
			{
				cancelledEscort = cancelled.Trait<MedievalAnimal>().Escort == null && !breeder.Trait<MedievalHerding>().Busy;
				Check(cancelledEscort && receiving.Trait<MedievalLivestock>().Incoming == 0, "Stop must release the living animal and its reserved place.");
				Check(cows.Adults > 2, "Meat fixture must have surplus adults.");
				herdCountBefore = cows.Count; cowPen.Trait<MedievalSource>().Cultivate();
				producedMeat = cowPen.Trait<MedievalSource>().Remaining == 30 && cows.Count == herdCountBefore - 1 && cows.Adults >= 2;
				Check(producedMeat, "Thirty meat must consume exactly one surplus adult and preserve two breeders.");
				var count = sheep.Count; sheepPen.Trait<MedievalSource>().Cultivate();
				producedWool = sheepPen.Trait<MedievalSource>().Remaining == 8 && sheep.Count == count;
				Check(producedWool, "Shearing must create eight wool while preserving the sheep.");
				foodBefore = ranch.Trait<MedievalStockpile>().Stock.Amount(2);
				w.IssueOrder(new Order("MedievalWork", breeder, false)); w.IssueOrder(new Order("MedievalWork", shepherd, false)); Next();
			}
			else if (phase == 7 && ranch.Trait<MedievalStockpile>().Stock.Amount(2) > foodBefore && fold.Trait<MedievalCampStore>().Stock.Amount(0) > 0)
			{
				Check(singlePen.Trait<MedievalLivestock>().Count == 1, "An isolated sheep must still not reproduce after the whole scenario.");
				Check(captured && transferredByWorker && stolen && cancelledEscort && producedMeat && producedWool, "All husbandry stages must complete.");
				Stop(w, breeder); Stop(w, shepherd);
				interceptor = Spawn(w, "militia", 43, 36); interceptor.Trait<AutoTarget>().SetStance(interceptor, UnitStance.HoldFire);
				w.IssueOrder(new Order("MedievalGatherAnimal", interceptor, Target.FromActor(transferred), false)); Next();
			}
			else if (phase == 8 && transferred.Owner == w.LocalPlayer && transferred.Trait<MedievalAnimal>().Escort == interceptor)
			{
				w.IssueOrder(new Order("MedievalGatherAnimal", thief, Target.FromActor(transferred), false)); Next();
			}
			else if (phase == 9 && transferred.Owner == rival && transferred.Trait<MedievalAnimal>().Escort == thief)
			{
				intercepted = true; Select(w, transferred); Next();
			}
			else if (phase == 10 && tick - since >= 10)
			{
				Check(!interceptor.Trait<MedievalHerding>().Busy, "Interception must release the former guide and its reserved place.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "road-interception.png"));
				thief.Kill(interceptor); Next();
			}
			else if (phase == 11 && tick - since >= 20)
			{
				guideDeath = MedievalStockpile.Active(transferred) && transferred.Trait<MedievalAnimal>().Escort == null;
				Check(guideDeath && enemyPen.Trait<MedievalLivestock>().Incoming == 0, "Killing the escort must leave one living loose animal and release the incoming place.");
				breeder.Trait<MedievalHerding>().Cancel(breeder);
				gateFollower = Spawn(w, "cow", 37, 37, neutral);
				Check(breeder.Trait<MedievalHerding>().Start(breeder, gateFollower, receiving), "Gate regression must reserve its destination.");
				Next();
			}
			else if (phase == 12 && gateFollower.Trait<MedievalAnimal>().Escort == breeder)
			{
				// Reproduce the blocked approach: the full-cell animal is 2x1 cells
				// away and cannot share the guide's occupied cell outside the gate.
				var outside = receiving.Trait<MedievalLivestock>().Outside;
				breeder.CancelActivity(); gateFollower.CancelActivity();
				breeder.Trait<Mobile>().SetPosition(breeder, outside);
				breeder.Trait<Mobile>().SetCenterPosition(breeder, w.Map.CenterOfCell(outside));
				gateFollower.Trait<Mobile>().SetPosition(gateFollower, outside + new CVec(-2, 1));
				Next();
			}
			else if (phase == 13 && gateFollower.Trait<MedievalAnimal>().Pen == receiving)
			{
				neighboringGateArrival = true;
				Finish(w); return;
			}
			if (tick > 11000 || tick - since > 2300)
			{
				Check(false, $"Husbandry phase {phase} timed out: {breeder.Trait<MedievalHerding>().Status}, guide {breeder.Location}, animal {breeder.Trait<MedievalHerding>().Animal?.Location}, destination {breeder.Trait<MedievalHerding>().Destination?.Location}."); Finish(w);
			}
		}
	}
}
