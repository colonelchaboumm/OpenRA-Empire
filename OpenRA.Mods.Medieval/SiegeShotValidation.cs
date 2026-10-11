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
	public class MedievalSiegeShotValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeShotValidation();
	}
	public sealed class MedievalSiegeShotValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor engine, victim;
		string output;
		int tick, stage, impactTick, initialHp, impactHp, lastZ, apex, observedAge;
		bool rose, fell, attached, released, shaken, finished;
		readonly HashSet<int> poses = [];
		readonly HashSet<string> images = [];
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "siege-shot"); Directory.CreateDirectory(output); }
		void Check(bool value, string error) { if (!value && !errors.Contains(error)) errors.Add(error); }
		void Shot(string name) { if (images.Add(name)) Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png")); }
		void Order(World w, string command, Target target = default) => w.IssueOrder(new Order(command, engine, target, false));
		Actor Spawn(World w, string name, int x, int y, Player owner = null) => w.CreateActor("md." + name, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Setup(World w)
		{
			var rival = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask;
			engine = Spawn(w, "trebuchet", 26, 32); engine.Trait<AutoTarget>().SetStance(engine, UnitStance.HoldFire);
			engine.Trait<MedievalStockpile>().Stock.Add(3, 10); engine.Trait<MedievalSiegeEngine>().Sling.Add(3, 5);
			for (var i = 0; i < 4; i++) { var a = Spawn(w, "swordsman", 23 + i, 35); a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire); a.Trait<MedievalSiegeCrew>().Assign(a, engine); }
			victim = Spawn(w, "siegetarget", 38, 32, rival); initialHp = victim.Trait<IHealth>().HP;
			Spawn(w, "house", 43, 32, rival); Spawn(w, "house", 41, 36, rival); Spawn(w, "market", 43, 25, rival);
			Sync.RunUnsynced(w, () => { renderer.Viewport.Center(WPos.Lerp(engine.CenterPosition, victim.CenterPosition, 1, 2)); w.Selection.Clear(); });
			stage = 1;
		}
		void Finish(World w)
		{
			var audio = w.WorldActor.Trait<MedievalAudio>();
			Check(audio.Played.GetValueOrDefault("trebuchet-release") == 1 && audio.Played.GetValueOrDefault("stone-impact") == 1,
				"The actual stone release and impact must each produce one sound.");
			var siege = engine.Trait<MedievalSiegeEngine>();
			Check(stage == 4 && attached && released && rose && fell && shaken && poses.Count == SiegeShotMotion.FireFrames, "The full launch, ascent, fall, 32 firing poses and damage reaction must run.");
			Check(siege.Shots == 1 && victim.Trait<MedievalSiegeDamage>().Impacts == 1 && victim.Trait<IHealth>().HP == impactHp && impactHp < initialHp, "One stone must apply damage exactly once.");
			Check(siege.Stone + siege.Sling.Amount(3) + siege.Crew.Sum(a => a.Trait<MedievalSiegeCrew>().Cargo) == 10, "Visual effects must not create or consume extra stones.");
			Check(!w.Effects.OfType<SiegeImpactEffect>().Any() && !w.Effects.OfType<MedievalSiegeStone>().Any(), "Projectile and every dust effect must expire.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, errors,
				attached, released, rose, fell, shaken, poses = poses.Count, apex, initialHp, impactHp, shots = siege.Shots, images, sounds = audio.Played }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL SIEGE SHOT: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			// Keep the preview framed even if the desktop cursor triggers edge scrolling.
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(WPos.Lerp(engine.CenterPosition, victim.CenterPosition, 1, 2)));
			var siege = engine.Trait<MedievalSiegeEngine>(); var body = engine.Trait<WithMedievalSiegeBody>();
			if (body.Sequence == "fire") poses.Add(body.Frame);
			if (stage == 1 && siege.Packed && siege.ReadyCrew == 4 && engine.IsIdle) Order(w, "MedievalSiegeDeploy");
			if (stage == 1 && siege.CanFire) { Shot("01-loaded"); stage = 2; observedAge = tick + 6; }
			if (stage == 2 && tick == observedAge) Order(w, "ForceAttack", Target.FromActor(victim));
			if (stage == 2 && siege.Shots == 1) { Order(w, "Stop"); stage = 3; }
			var stone = w.Effects.OfType<MedievalSiegeStone>().FirstOrDefault();
			if (stone != null)
			{
				if (!stone.Released)
				{
					attached |= stone.Position == SiegeShotMotion.SlingPosition(engine, SiegeShotMotion.Frame(stone.Age));
					Check(victim.Trait<IHealth>().HP == initialHp, "No damage may occur while the stone is still in the sling.");
					if (stone.Age >= 4) Shot("02-sling-release");
				}
				else
				{
					released = true;
					if (stone.Age > SiegeShotMotion.ReleaseTick + 2) { rose |= stone.Position.Z > lastZ; fell |= stone.Position.Z < lastZ; }
					lastZ = stone.Position.Z; apex = Math.Max(apex, lastZ);
					if (stone.Age >= 22) Shot("03-flight");
					if (fell && stone.Position.Z < apex / 2) Shot("04-falling");
				}
			}
			var impact = w.Effects.OfType<SiegeImpactEffect>().FirstOrDefault(e => !e.Masonry);
			shaken |= victim.Trait<MedievalSiegeDamage>().ShakeTicks > 0;
			if (stage == 3 && impact != null)
			{ stage = 4; impactTick = tick; impactHp = victim.Trait<IHealth>().HP; }
			if (impact != null)
			{
				if (impact.Age >= 5) Shot("05-impact");
				if (impact.Age >= 24) Shot("06-dust-cloud");
				if (impact.Age >= 62) Shot("07-settling");
			}
			if (stage == 4 && tick > impactTick + SiegeImpactEffect.Lifetime + 5) { Shot("08-cleared"); Finish(w); }
			if (tick > 1500) Finish(w);
		}
	}
}
