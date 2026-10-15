using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalDefenseValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalDefenseValidation();
	}
	public sealed class MedievalDefenseValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [], completed = [];
		readonly List<Actor> actors = [];
		static readonly string[] Names = ["idle-melee-defend", "idle-melee-return-fire", "idle-archer-return-fire", "hold-fire",
			"defend-cart", "defend-escort", "moving-convoy", "protected-death", "protected-capture", "patrol-return-fire",
			"manual-escape", "limited-pursuit", "friendly-healing-zero-damage", "attacker-captured", "escort-hold-fire", "hold-fire-during-escort"];
		Actor unit, attacker, cart;
		Health unitHealth, enemyHealth, cartHealth;
		MedievalCombatProbe unitProbe, enemyProbe;
		WorldRenderer renderer;
		Player enemy;
		int tick, phase = -1, age, shotsBeforeHold;
		bool done, special, shot, damaged;
		CPos origin;
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) { errors.Add(message); Console.WriteLine("DEFENSE CHECK: " + message); } }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis/medieval/defense/native"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string name, int x, int y, Player owner = null)
		{
			var a = w.CreateActor("md." + name, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]); actors.Add(a); return a;
		}
		void Order(World w, Order order)
		{
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = OpenRA.Order.Deserialize(w, reader); received.Subject.ResolveOrder(received);
		}
		void Setup(World w)
		{
			foreach (var a in actors.Where(a => !a.Disposed)) a.Dispose(); actors.Clear(); phase++; age = 0; special = shot = damaged = false;
			if (phase >= Names.Length) { Finish(); return; }
			unit = Spawn(w, phase == 2 ? "archer" : phase == 9 ? "militia" : "royalguard", 20, 30);
			attacker = Spawn(w, "archer", 24, 30, enemy); cart = Spawn(w, "tradecart", 20, 28);
			unitHealth = unit.Trait<Health>(); enemyHealth = attacker.Trait<Health>(); cartHealth = cart.Trait<Health>();
			unitProbe = unit.Trait<MedievalCombatProbe>(); enemyProbe = attacker.Trait<MedievalCombatProbe>(); origin = unit.Location;
			attacker.Trait<AutoTarget>().SetStance(attacker, UnitStance.HoldFire);
			unit.Trait<AutoTarget>().SetStance(unit, phase == 0 ? UnitStance.Defend : phase is 3 or 14 ? UnitStance.HoldFire : UnitStance.ReturnFire);
			// Short fights still use actual weapons and damage, with enough time to observe the response.
			if (phase is not (13 or 15)) enemyHealth.InflictDamage(attacker, attacker, new Damage(enemyHealth.HP * 2 / 3), true);
			if (phase is >= 4 and <= 8 or >= 13) Order(w, new Order("MedievalEscort", unit, Target.FromActor(cart), false));
			if (phase == 9)
			{
				Order(w, new Order("MedievalPatrolNightOnly", unit, false) { ExtraData = 0 });
				Order(w, new Order("MedievalStartPatrol", unit, false) { TargetString = PatrolRoute.Encode([Cell(20, 30), Cell(27, 30)]) });
			}
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(unit.CenterPosition));
			Console.WriteLine("DEFENSE " + phase + " " + Names[phase]);
		}
		void Finish()
		{
			Check(completed.Count == Names.Length, "All defense scenarios completed.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, completed, errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL DEFENSE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(500, Game.Exit);
		}
		void Next(World w) { completed.Add(Names[phase]); w.AddFrameEndTask(Setup); }
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1)
			{
				enemy = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.Shroud.Disabled = enemy.Shroud.Disabled = true;
				w.LocalPlayer.EnemyPlayersMask = enemy.PlayerMask; enemy.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
				foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
				w.AddFrameEndTask(Setup); return;
			}
			age++; shot |= unitProbe.Shots > 0; damaged |= unitHealth.HP < unitHealth.MaxHP || cartHealth.HP < cartHealth.MaxHP;
			if (age is 100 or 300) Console.WriteLine($"DEFENSE state {Names[phase]} age={age} unit={unit.Location} hp={unitHealth.HP} shots={unitProbe.Shots} activity={unit.CurrentActivity?.GetType().Name} enemy={attacker.Location} hp={enemyHealth.HP} shots={enemyProbe.Shots} cart={cart.Location} hp={cartHealth.HP}");
			if (age == 20 && phase != 0) Check(unitProbe.Shots == 0, Names[phase] + ": no spontaneous attack in ReturnFire/HoldFire.");
			if (age == 30 && phase != 12)
			{
				Order(w, new Order("Attack", attacker, Target.FromActor(phase is 4 or 6 or 7 or 8 or >= 13 ? cart : unit), false));
				if (phase == 6) Order(w, new Order("Move", cart, Target.FromCell(w, Cell(27, 28)), false));
			}
			if (phase <= 2 && attacker.IsDead && unit.IsIdle)
			{
				Check(shot && damaged && enemyProbe.Shots > 0, Names[phase] + ": a real hit triggers an effective response.");
				Check((unit.Location - origin).LengthSquared <= 2, Names[phase] + ": returns to defensive position after combat."); Next(w); return;
			}
			if (phase == 3 && age == 180)
			{ Check(damaged && enemyProbe.Shots > 0 && !shot && unit.Location == origin, "HoldFire remains respected even under fire."); Next(w); return; }
			if (phase is >= 4 and <= 8 or 13)
			{
				if (!special && shot)
				{
					Check(damaged && enemyProbe.Shots > 0, Names[phase] + ": escort reacts to real hostile damage."); special = true;
					if (phase == 7) cart.Kill(attacker);
					if (phase == 8) w.AddFrameEndTask(_ => cart.ChangeOwner(enemy));
					if (phase == 13) w.AddFrameEndTask(_ => { attacker.ChangeOwner(w.LocalPlayer); attacker.Trait<AutoTarget>().SetStance(attacker, UnitStance.HoldFire); });
					else if (!attacker.IsDead) attacker.Kill(unit);
					if (phase == 4) Game.Renderer.SaveScreenshot(Path.Combine(output, "escort-response.png"));
				}
				if (special && age > 220)
				{
					if (phase is 7 or 8) Check(unit.Trait<MedievalEscort>().Protected == null, Names[phase] + ": loss of protected unit ends mission.");
					else
					{
						Check(unit.Trait<MedievalEscort>().Protected == cart && (unit.CenterPosition - cart.CenterPosition).HorizontalLengthSquared <= 3072L * 3072,
							Names[phase] + ": guard returns to the same cart after combat.");
						if (phase == 6) Check((cart.Location - Cell(27, 28)).LengthSquared <= 2, "Escort combat does not cancel cart movement.");
						if (phase == 13) Check(!attacker.IsDead, "An enemy becoming friendly is no longer attacked.");
					}
					Next(w); return;
				}
			}
			if (phase == 9 && shot && !special) { special = true; if (!attacker.IsDead) attacker.Kill(unit); }
			if (phase == 9 && special && unit.Trait<MedievalPatrol>().Visits >= 2)
			{ Check(damaged && unit.Trait<MedievalPatrol>().Active, "An attacked patrol fights and resumes its circuit."); Next(w); return; }
			if (phase is 10 or 11 && !special && unit.CurrentActivity is Retaliation)
			{
				special = true; Check(damaged, Names[phase] + ": retaliation begins after damage.");
				if (phase == 10)
				{
					Order(w, new Order("Move", unit, Target.FromCell(w, Cell(12, 36)), false)); Order(w, new Order("Stop", attacker, false));
				}
				else Order(w, new Order("Move", attacker, Target.FromCell(w, Cell(36, 30)), false));
			}
			if (phase == 10 && special && unit.IsIdle)
			{ Check((unit.Location - Cell(12, 36)).LengthSquared <= 2, "Explicit escape overrides retaliation and automatic return."); Next(w); return; }
			if (phase == 11 && special && unit.IsIdle)
			{ Check(!attacker.IsDead && (unit.Location - origin).LengthSquared <= 2, "Defenders abandon long pursuit and return to their position."); Next(w); return; }
			if (phase == 12 && age == 30)
			{
				unitHealth.InflictDamage(unit, cart, new Damage(10), true);
				unitHealth.InflictDamage(unit, attacker, new Damage(0), true);
				unitHealth.InflictDamage(unit, attacker, new Damage(-10), true);
			}
			if (phase == 12 && age == 100)
			{ Check(!shot && unit.IsIdle && unit.Trait<MedievalThreat>().RecentAttacker(unit) == null, "Friendly, healing and zero damage never trigger retaliation."); Next(w); return; }
			if (phase == 14 && age == 180)
			{ Check(damaged && !shot && unit.Trait<MedievalEscort>().Protected == cart, "An escort in HoldFire keeps following without attacking."); Next(w); return; }
			if (phase == 15 && shot && !special)
			{
				special = true; shotsBeforeHold = unitProbe.Shots;
				Order(w, new Order("SetUnitStance", unit, false) { ExtraData = (uint)UnitStance.HoldFire });
				Order(w, new Order("Stop", attacker, false));
			}
			if (phase == 15 && special && age > 250)
			{
				Check(unitProbe.Shots == shotsBeforeHold && unit.Trait<MedievalEscort>().Protected == cart
					&& (unit.CenterPosition - cart.CenterPosition).HorizontalLengthSquared <= 3072L * 3072, "HoldFire during combat stops further attacks and restores escort following."); Next(w); return;
			}
			if (age > 1400) { Check(false, "Timeout: " + Names[phase] + " activity=" + unit.CurrentActivity?.GetType().Name + " shots=" + unitProbe.Shots); Next(w); }
		}
	}
}
