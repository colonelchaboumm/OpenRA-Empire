using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSiegeCaptureValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeCaptureValidation();
	}
	// Exercises the real movement, capture activity and owner notifications. Enemy
	// commands use the serialized order format, without changing ownership in tests.
	public sealed class MedievalSiegeCaptureValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Player rival;
		Actor ram, thief, oldRamCrew, trebuchet, oldCrew, cart, target, ramTarget, workshop, rivalWorkshop, localScout;
		Actor[] thieves;
		MedievalSiegeEngine engine;
		MedievalSiegeHeraldry destroyedLivery;
		Inventory stock;
		int tick, stage, until, hp, load, partialProgress, initialStones, accountedStones;
		bool finished;
		WPos ramStart, transportStart;
		readonly List<string> errors = [], checks = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "siege-capture"); Directory.CreateDirectory(output); }
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		void Next(int value, int wait = 0) { stage = value; until = tick + wait; Console.WriteLine($"SIEGE CAPTURE stage {stage} tick {tick}"); }
		Actor Spawn(World w, string type, int x, int y, Player owner = null)
		{
			var a = w.CreateActor("md." + type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); return a;
		}
		void Command(Actor actor, string command, Target destination = default)
		{
			var order = new Order(command, actor, destination, false);
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = Order.Deserialize(actor.World, reader);
			foreach (var t in actor.TraitsImplementing<IResolveOrder>()) t.ResolveOrder(actor, received);
		}
		void Capture(Actor actor, Actor victim) => Command(actor, "CaptureActor", Target.FromActor(victim));
		void CheckLivery(Actor actor, string phase) => Sync.RunUnsynced(actor.World, () =>
		{
			MedievalSiegeHeraldryValidation.CheckRender(actor, renderer, Check);
			checks.Add(phase + ": " + actor.Owner.InternalName + " / " + MedievalHeraldry.For(actor.Owner).Hex);
		});
		void Shot(string name) => Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png"));
		string MouseOrder(Actor actor, Actor victim, TargetModifiers modifiers = TargetModifiers.None)
		{
			var target = Target.FromActor(victim); string cursor = null;
			foreach (var t in actor.TraitsImplementing<IIssueOrder>().SelectMany(t => t.Orders).OrderByDescending(t => t.OrderPriority))
				if (t.CanTarget(actor, target, ref modifiers, ref cursor)) return t.OrderID;
			return null;
		}
		MedievalSiegeEngine Engine => engine;
		int Braces(World w) => w.Actors.Count(a => a.Info.Name == "md.siegebrace" && SiegeRules.Braces(trebuchet.Location).Contains(a.Location) && a.Owner == trebuchet.Owner);
		int Stones(World w) => w.ActorsWithTrait<MedievalStockpile>().Sum(p => p.Trait.Stock.Amount(3))
			+ w.ActorsWithTrait<MedievalSiegeCrew>().Sum(p => p.Trait.Cargo)
			+ w.ActorsWithTrait<MedievalSiegeEngine>().Sum(p => p.Trait.Sling.Amount(3)) + Engine.Shots * SiegeRules.StonePerShot;
		void Setup(World w)
		{
			rival = w.Players.Single(p => p.InternalName == "Rival");
			// Nations start neutral: capture requires hostility from the captor's side too.
			w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask; rival.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			MedievalHeraldry.For(w.LocalPlayer).SetEmpire("Lion rubis", 0xB63838, 0);
			MedievalHeraldry.For(rival).SetEmpire("Aigle azur", 0x356FAD, 1);
			Sync.RunUnsynced(w, () => { w.Selection.Clear(); renderer.Viewport.AdjustZoom((float)Math.Log(1.75f / renderer.Viewport.Zoom)); });
			ram = Spawn(w, "ram", 25, 27); ramStart = ram.CenterPosition;
			oldRamCrew = Spawn(w, "swordsman", 25, 29); oldRamCrew.Trait<MedievalSiegeCrew>().Assign(oldRamCrew, ram);
			thief = Spawn(w, "swordsman", 22, 27, rival);
			trebuchet = Spawn(w, "trebuchet", 34, 38); engine = trebuchet.Trait<MedievalSiegeEngine>(); stock = trebuchet.Trait<MedievalStockpile>().Stock;
			stock.Add(3, 30); Engine.Sling.Add(3, 5);
			trebuchet.InflictDamage(trebuchet, new Damage(1234)); hp = trebuchet.Trait<IHealth>().HP;
			oldCrew = Spawn(w, "swordsman", 34, 41); oldCrew.Trait<MedievalSiegeCrew>().Assign(oldCrew, trebuchet);
			oldCrew.Trait<MedievalSiegeCrew>().StoneCargo.Add(3, 5);
			workshop = Spawn(w, "siegeworkshop", 17, 20); rivalWorkshop = Spawn(w, "siegeworkshop", 40, 20, rival);
			target = Spawn(w, "siegetarget", 46, 38); ramTarget = Spawn(w, "siegetarget", 31, 27);
			localScout = Spawn(w, "swordsman", 40, 25);
			initialStones = Stones(w);
			Check(MouseOrder(localScout, rivalWorkshop) != "CaptureActor" && MouseOrder(thief, workshop) != "CaptureActor", "Workshops must never be capturable.");
			Check(MouseOrder(thief, ram) == "CaptureActor", "Right click on an enemy engine must prefer capture over attack.");
			Check(MouseOrder(thief, ram, TargetModifiers.ForceAttack) == "ForceAttack", "Ctrl must retain force attack.");
			Check(MouseOrder(thief, ram, TargetModifiers.ForceMove) != "CaptureActor", "Alt must retain movement.");
			Check(MouseOrder(localScout, ram) != "CaptureActor", "Friendly engines must not be capture targets.");
			Capture(thief, workshop); Check(thief.CurrentActivity is not CaptureSiege, "Forged workshop capture orders must also be refused.");
			Capture(thief, ram); Next(1);
		}
		void Finish(World w)
		{
			Check(stage == 19, "All capture scenarios must complete.");
			Check(workshop.Owner == w.LocalPlayer && rivalWorkshop.Owner == rival, "Both workshops must retain their owners.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
				passed = errors.Count == 0, tick, stage, errors, checks, ramOwner = ram.Owner.InternalName, trebuchetOwner = trebuchet.Owner.InternalName,
				state = Engine.State, stones = stock.Amount(3), sling = Engine.Sling.Amount(3), shots = Engine.Shots,
				cartStones = cart?.Trait<MedievalStockpile>().Stock.Amount(3), oldCrewCargo = oldCrew.Trait<MedievalSiegeCrew>().Cargo,
				oldCrewOwner = oldCrew.Owner.InternalName, braces = Braces(w), ramPosition = ram.Location.ToString(),
				captorPosition = thief.Location.ToString(), captorActivity = thief.CurrentActivity?.GetType().Name,
				captureProgress = thief.Trait<MedievalSiegeCapture>().CapturePercent, crew = Engine.ReadyCrew, initialStones, accountedStones
			}, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SIEGE CAPTURE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(stage < 8 ? ram.CenterPosition : trebuchet.CenterPosition));
			if (tick == 25) { CheckLivery(ram, "Original ram"); CheckLivery(trebuchet, "Original trebuchet"); Shot("01-original-red-ram"); }
			if (Engine.Packed && Engine.ReadyCrew > 0 && stage < 10) Command(trebuchet, "MedievalSiegeDeploy");
			if (stage == 1 && thief.Trait<MedievalSiegeCapture>().CapturePercent >= 35)
			{
				Check(ram.Owner == w.LocalPlayer, "Ownership must not change before the capture delay.");
				CheckLivery(ram, "Capture in progress retains original colors");
				Command(thief, "Stop"); Next(2, 175);
			}
			else if (stage == 2 && tick >= until)
			{
				Check(ram.Owner == w.LocalPlayer && !thief.Trait<MedievalSiegeCapture>().Capturing, "Stopping must cancel the capture and its progress.");
				CheckLivery(ram, "Interrupted capture retains original colors");
				checks.Add("Interrupted capture keeps owner and clears progress"); Capture(thief, ram); Next(3);
			}
			else if (stage == 3 && thief.Trait<MedievalSiegeCapture>().CapturePercent >= 30)
			{ thief.Kill(oldRamCrew); Next(4, 175); }
			else if (stage == 4 && tick >= until)
			{
				Check(ram.Owner == w.LocalPlayer, "A dead soldier must never complete capture."); checks.Add("Death cancels capture");
				w.AddFrameEndTask(_ =>
				{
					thieves = Enumerable.Range(0, 4).Select(i => Spawn(w, "swordsman", 23, 25 + i, rival)).ToArray(); thief = thieves[0];
					foreach (var a in thieves) Capture(a, ram);
				}); Next(5);
			}
			else if (stage == 5 && ram.Owner == rival && ram.Trait<MedievalSiegeEngine>().ReadyCrew == 4)
			{
				Check(!oldRamCrew.Trait<MedievalSiegeCrew>().Assigned && oldRamCrew.Owner == w.LocalPlayer, "The former ram crew must stay in its own army.");
				Check(thieves.All(a => a.IsInWorld && !a.IsDead && a.Trait<MedievalSiegeCrew>().Serves(ram)), "Four surviving captors must become the new crew.");
				CheckLivery(ram, "Captured ram adopts rival colors and flag"); Shot("02-captured-blue-ram");
				Command(ram, "Move", Target.FromCell(w, Cell(28, 27))); Next(6);
			}
			else if (stage == 6 && (ram.CenterPosition - ramStart).HorizontalLength > 2000)
			{ checks.Add("Enemy captures ram with four soldiers and drives it away"); Command(ram, "ForceAttack", Target.FromActor(ramTarget)); Next(7); }
			else if (stage == 7 && ram.Trait<MedievalSiegeEngine>().Shots > 0 && ramTarget.Trait<IHealth>().HP < ramTarget.Trait<IHealth>().MaxHP && Engine.Deployed)
			{
				Check(ramTarget.Trait<IHealth>().HP < ramTarget.Trait<IHealth>().MaxHP, "Captured ram must damage its former owner's buildings.");
				checks.Add("Captured ram attacks former owner's building"); Command(ram, "Stop");
				w.AddFrameEndTask(_ =>
				{
					thief = Spawn(w, "swordsman", 31, 38, rival); Capture(thief, trebuchet);
					cart = Spawn(w, "stonecart", 13, 40); cart.Trait<MedievalStockpile>().Stock.Add(3, 10); cart.Trait<MedievalStoneCart>().Assign(cart, trebuchet); initialStones += 10;
				}); Next(8);
			}
			else if (stage == 8 && thief.Trait<MedievalSiegeCapture>().CapturePercent >= 25)
			{
				Check(trebuchet.Trait<MedievalStockpile>().Stock.Incoming(3) == 10, "Old supply cart must have a real incoming reservation.");
				load = Engine.LoadProgress; Next(9);
			}
			else if (stage == 9 && trebuchet.Owner == rival)
			{
				Check(Engine.Deployed && Braces(w) == 8 && trebuchet.Trait<IHealth>().HP == hp, "Capture must preserve deployed state, eight supports and health.");
				Check(Engine.Stone == 30 && Engine.Sling.Amount(3) == 5 && Engine.LoadProgress == load, "Capture must preserve reserve, loaded stone and winch progress.");
				Check(!oldCrew.Trait<MedievalSiegeCrew>().Assigned && oldCrew.Owner == w.LocalPlayer && oldCrew.Trait<MedievalSiegeCrew>().Cargo == 5, "Old crew must retain their own carried stones and owner.");
				Check(!cart.Trait<MedievalStoneCart>().Active && cart.Owner == w.LocalPlayer && cart.Trait<MedievalStockpile>().Stock.Amount(3) == 10
					&& trebuchet.Trait<MedievalStockpile>().Stock.Incoming(3) == 0, "Old supply must stop, release reservations and keep its real cargo.");
				Check(Engine.Crew.SingleOrDefault() == thief, "Captor must automatically operate the captured trebuchet.");
				CheckLivery(trebuchet, "Captured deployed trebuchet adopts rival colors and flag"); Shot("03-captured-blue-trebuchet");
				checks.Add("Deployed capture preserves health, ammunition and supports; old crew and supply retain cargo");
				transportStart = trebuchet.CenterPosition; Command(trebuchet, "Move", Target.FromCell(w, Cell(37, 38))); Next(10, 80);
			}
			else if (stage == 10 && tick >= until)
			{
				Check(trebuchet.CenterPosition == transportStart && Engine.Deployed, "A stolen deployed trebuchet must remain immobile until dismantled.");
				Command(trebuchet, "ForceAttack", Target.FromActor(target)); Next(11);
			}
			else if (stage == 11 && Engine.Shots > 0)
			{ Command(trebuchet, "Stop"); Next(12, 100); }
			else if (stage == 12 && tick >= until)
			{
				Check(target.Trait<IHealth>().HP < target.Trait<IHealth>().MaxHP, "Captured trebuchet must hit the former owner's building.");
				checks.Add("Captured trebuchet fires real ammunition at former owner"); Command(trebuchet, "MedievalSiegePack"); Next(13);
			}
			else if (stage == 13 && Engine.Packed)
			{ Check(Braces(w) == 0, "Dismantling must release all supports."); CheckLivery(trebuchet, "Packed captured trebuchet keeps rival colors"); Shot("04-captured-blue-transport"); Command(trebuchet, "Move", Target.FromCell(w, Cell(38, 38))); Next(14); }
			else if (stage == 14 && trebuchet.IsIdle && (trebuchet.CenterPosition - transportStart).HorizontalLength > 3000)
			{
				checks.Add("Stolen trebuchet dismantles and travels with its new crew");
				Command(trebuchet, "MedievalSiegeDeploy"); Next(15);
			}
			else if (stage == 15 && Engine.State == 1 && Engine.Progress > 20)
			{
				// An abandoned, unfinished assembly must not magically become a cart on capture.
				Command(thief, "MedievalLeaveSiege"); partialProgress = Engine.Progress;
				Capture(localScout, trebuchet); Next(16);
			}
			else if (stage == 16 && trebuchet.Owner == w.LocalPlayer)
			{
				Check(Engine.State == 1 && Engine.Progress == partialProgress && Braces(w) == 8, "Recapture must preserve unfinished assembly and transfer its supports.");
				Check(localScout.Trait<MedievalSiegeCrew>().Serves(trebuchet) && thief.Owner == rival, "Recapture must assign only the new owner's soldiers.");
				CheckLivery(trebuchet, "Recaptured assembly restores original colors and flag"); Shot("05-recaptured-red-assembly");
				checks.Add("Original owner recaptures unfinished assembly without resetting progress"); Next(17);
			}
			else if (stage == 17 && Engine.Deployed)
			{
				accountedStones = Stones(w);
				Check(accountedStones == initialStones, "Every stone must remain in a stock, sling, carrier of either army or fired projectile.");
				checks.Add("New crew completes assembly and all stones remain accounted for");
				CheckLivery(trebuchet, "Reassembled engine keeps restored national identity");
				destroyedLivery = trebuchet.Trait<MedievalSiegeHeraldry>();
				trebuchet.Kill(trebuchet); Next(18, 10);
			}
			else if (stage == 18 && tick >= until)
			{
				Check(Braces(w) == 0 && !localScout.Trait<MedievalSiegeCrew>().Assigned, "Destroying a recaptured engine must still remove its supports and release its crew.");
				Sync.RunUnsynced(w, () => Check(!((IRender)destroyedLivery).Render(trebuchet, renderer).Any(), "Destroyed engines must not leave floating colors or flags."));
				checks.Add("Destroyed captured engine cleans up crew and supports"); Next(19); Finish(w);
			}
			if (tick % 250 == 0) Console.WriteLine($"CAPTURE status {stage}: thief {thief.Location}, {thief.CurrentActivity?.GetType().Name}, {thief.Trait<MedievalSiegeCapture>().CapturePercent}%, ram {ram.Location}, crew {ram.Trait<MedievalSiegeEngine>().ReadyCrew}, trebuchet {Engine.State}/{Engine.Progress}");
			if (tick > 8000) Finish(w);
		}
	}
}
