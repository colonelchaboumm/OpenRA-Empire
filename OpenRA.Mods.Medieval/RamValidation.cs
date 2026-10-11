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
	public class MedievalRamValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalRamValidation();
	}
	public sealed class MedievalRamValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor ram, victim;
		string output;
		int tick, scenario, initialHp, impactHp, impactFrame, endTick;
		bool finished, stopped, interrupted;
		readonly List<Actor> actors = [];
		readonly List<string> errors = [];
		readonly List<object> results = [];
		readonly HashSet<int> poses = [], crewPoses = [];
		readonly HashSet<string> images = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "ram-strike"); Directory.CreateDirectory(output); }
		void Check(bool value, string error) { if (!value && !errors.Contains(error)) errors.Add(error); }
		void Shot(string name) { name = scenario + "-" + name; if (images.Add(name)) Game.Renderer.SaveScreenshot(Path.Combine(output, name + ".png")); }
		void Command(string command, Target target = default)
		{
			var order = new Order(command, ram, target, false);
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = Order.Deserialize(ram.World, reader);
			foreach (var t in ram.TraitsImplementing<IResolveOrder>()) t.ResolveOrder(ram, received);
		}
		Actor Spawn(World w, string type, int x, int y, Player owner = null)
		{
			var a = w.CreateActor("md." + type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire); actors.Add(a); return a;
		}
		void Setup(World w)
		{
			foreach (var a in actors) if (a.IsInWorld) a.Dispose(); actors.Clear();
			poses.Clear(); crewPoses.Clear(); stopped = interrupted = false; impactFrame = -1; endTick = 0;
			var rival = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask;
			ram = Spawn(w, "ram", scenario == 1 ? 40 : 28, 32);
			for (var i = 0; i < 4; i++) { var a = Spawn(w, "swordsman", (scenario == 1 ? 40 : 26) + i, 35); a.Trait<MedievalSiegeCrew>().Assign(a, ram); }
			victim = Spawn(w, scenario == 1 ? "woodgatey" : "siegetarget", 34, 32, rival);
			initialHp = impactHp = victim.Trait<IHealth>().HP;
			Command("ForceAttack", Target.FromActor(victim));
		}
		void Finish(World w)
		{
			var audio = w.WorldActor.Trait<MedievalAudio>();
			Check(audio.Played.GetValueOrDefault("ram-hit") == 2, "Only the two completed strikes may emit an impact sound.");
			Check(scenario == 4, "Warehouse, wide gate, manual stop and crew-loss scenarios must complete.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, scenario, errors, results, images, sounds = audio.Played }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL RAM: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(WPos.Lerp(ram.CenterPosition, victim.CenterPosition, 1, 2)));
			var engine = ram.Trait<MedievalSiegeEngine>(); var body = ram.Trait<WithMedievalSiegeBody>();
			var hp = victim.Trait<IHealth>().HP; var damage = victim.Trait<MedievalSiegeDamage>();
			if (body.Sequence == "fire")
			{
				poses.Add(body.Frame);
				foreach (var a in engine.Crew) { var b = a.Trait<WithMedievalInfantryBody>(); if (b.Sequence == "push") crewPoses.Add(b.Frame); }
				if (body.Frame >= 6) Shot("01-windup");
				if (body.Frame < RamMotion.Frame(RamMotion.ContactTick)) Check(hp == initialHp, "Damage must wait until the head reaches the wall.");
			}
			if (hp < initialHp && impactFrame < 0)
			{ impactHp = hp; impactFrame = body.Frame; Shot("02-contact"); Check(damage.ShakeTicks > 0, "The target must react at impact."); }
			var dust = w.Effects.OfType<SiegeImpactEffect>().FirstOrDefault(e => e.Ram);
			if (dust != null && dust.Age >= 14) Shot("03-dust");
			if (scenario < 2 && engine.Shots == 1 && engine.AttackTicks == 0 && !stopped)
			{ Command("Stop"); stopped = true; endTick = tick + 85; }
			var strike = w.Effects.OfType<MedievalRamStrike>().FirstOrDefault();
			if (scenario >= 2 && !interrupted && strike?.Age >= 4)
			{
				if (scenario == 2) Command("Stop");
				else { var soldier = engine.Crew.First(); soldier.InflictDamage(victim, new Damage(100000)); }
				interrupted = true; endTick = tick + 85;
			}
			if (endTick > 0 && tick >= endTick)
			{
				if (scenario < 2)
				{
					Check(poses.Count == RamMotion.FireFrames, "The complete 24-pose stroke must play.");
					Check(crewPoses.Count >= 5, "The soldiers must animate while operating the ram.");
					Check(engine.Shots == 1 && damage.Impacts == 1 && hp == impactHp && hp < initialHp, "A stroke must damage its building exactly once.");
					Check(impactFrame == RamMotion.Frame(RamMotion.ContactTick), "Damage and contact pose must be synchronized.");
					Check((RamMotion.Contact(ram, victim) - ram.CenterPosition).HorizontalLength is >= 1250 and <= 1950, "The ram chassis must remain outside the building while striking.");
				}
				else Check(engine.Shots == 1 && hp == initialHp && damage.Impacts == 0, "An interrupted strike must cause no damage or building reaction.");
				Check(!w.Effects.OfType<SiegeImpactEffect>().Any() && !w.Effects.OfType<MedievalRamStrike>().Any(), "Strike and dust must expire.");
				results.Add(new { scenario, initialHp, hp, impactFrame, poses = poses.Count, crewPoses = crewPoses.Count, shots = engine.Shots, impacts = damage.Impacts,
					ram = ram.CenterPosition.ToString(), contact = RamMotion.Contact(ram, victim).ToString(), head = MedievalSiegeCrew.ModelPoint(ram, 56, 0).ToString(), facing = ram.Trait<Mobile>().Facing.Angle });
				if (++scenario == 4) Finish(w); else w.AddFrameEndTask(Setup);
			}
			if (tick > 2200) Finish(w);
		}
	}
}
