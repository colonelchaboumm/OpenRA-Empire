using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalCombatProbeInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCombatProbe();
	}
	public sealed class MedievalCombatProbe : INotifyAttack
	{
		public int Shots;
		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament armament, Barrel barrel) { }
		void INotifyAttack.Attacking(Actor self, in Target target, Armament armament, Barrel barrel) { Shots++; }
	}
	public class MedievalCombatValidationInfo : TraitInfo
	{
		public readonly int FirstWave = 0;
		public override object Create(ActorInitializer init) => new MedievalCombatValidation(this);
	}
	public sealed class MedievalCombatValidation : IWorldLoaded, ITick
	{
		sealed class ContextOrders(World world) : UnitOrderGenerator(world)
		{
			public Target AtPixel(Actor victim, WorldRenderer wr)
			{
				var pixel = wr.ScreenPxPosition(victim.CenterPosition);
				var mouse = new MouseInput(MouseInputEvent.Down, ActionButton, wr.Viewport.WorldToViewPx(pixel), int2.Zero, Modifiers.None, 1);
				return TargetForInput(victim.World, victim.Location, pixel, mouse);
			}
			public Order Against(Actor self, Actor victim, Modifiers modifiers = Modifiers.None)
			{
				var target = Target.FromActor(victim);
				var chosen = OrderForUnit(self, target, victim.Location, new MouseInput(MouseInputEvent.Down, ActionButton, int2.Zero, int2.Zero, modifiers, 1));
				return chosen?.Trait.IssueOrder(self, chosen.Order, chosen.Target, false);
			}
		}
		sealed class Case
		{
			public string Name, Kind;
			public Actor Victim, Main, Cart, Previous;
			public Health Health, MainHealth, PreviousHealth;
			public readonly List<Actor> Attackers = [];
			public readonly List<MedievalCombatProbe> Probes = [];
			public int Age, InitialHp, InitialMainHp, InitialStones, FirstDamage, Death, Secondary;
			public bool Ordered, Done, Screenshot;
			public CPos Start;
		}
		WorldRenderer renderer;
		ContextOrders orders;
		Player enemy;
		string output;
		int tick, wave = -1, waveStart, nextWave;
		bool done;
		int targetingChecks;
		readonly List<Case> cases = [];
		readonly List<Actor> actors = [];
		readonly List<object> results = [];
		readonly List<string> errors = [];
		public MedievalCombatValidation(MedievalCombatValidationInfo info) { wave = info.FirstWave - 1; }
		static readonly string[][] Structures =
		[
			["house", "warehouse", "market", "sawmill", "quarry", "farm", "ranch", "barracks", "forge"],
			["siegeworkshop", "sheepfold", "tentweaver", "marketstall", "gardenfence", "gardenfencey", "gardengate", "gardengatey", "well"],
			["palisade", "palisadey", "woodgate", "woodgatey", "woodtower", "militarytent", "field.4x4", "pasture.4x4", "sheeppasture.4x4"]
		];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) { errors.Add(message); Console.WriteLine("COMBAT CHECK: " + message); } }
		Actor Spawn(World w, string name, int x, int y, Player owner = null)
		{
			var a = w.CreateActor("md." + name, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]); actors.Add(a);
			a.TraitOrDefault<AutoTarget>()?.SetStance(a, UnitStance.HoldFire);
			if (a.TraitOrDefault<MedievalWorker>() is { } worker) worker.Automatic = false;
			return a;
		}
		Case Add(string name, string kind, Actor main, Actor victim, params Actor[] more)
		{
			var c = new Case { Name = name, Kind = kind, Main = main, Victim = victim, Health = victim.Trait<Health>(), MainHealth = main.Trait<Health>(), Start = main.Location };
			c.InitialHp = c.Health.HP; c.InitialMainHp = c.MainHealth.HP; c.Attackers.Add(main); c.Attackers.AddRange(more);
			c.Probes.AddRange(c.Attackers.Select(a => a.Trait<MedievalCombatProbe>())); cases.Add(c); return c;
		}
		void Attack(World w, Actor a, Actor target, Modifiers modifiers = Modifiers.None)
		{
			Order order = null; Sync.RunUnsynced(w, () => order = orders.Against(a, target, modifiers));
			Check(order?.OrderString is "Attack" or "ForceAttack", $"Context attack {a.Info.Name} -> {target.Info.Name}: {order?.OrderString ?? "none"}");
			if (order != null) w.IssueOrder(order);
		}
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; orders = new ContextOrders(w); output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "combat"); Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"status\":\"running\"}");
		}
		void Setup(World w)
		{
			foreach (var a in actors.Where(a => !a.Disposed).ToArray()) a.Dispose(); actors.Clear(); cases.Clear(); wave++; waveStart = tick; nextWave = 0;
			Console.WriteLine("COMBAT WAVE " + wave);
			if (wave == 0)
			{
				var pairs = new[] { ("militia", "militia"), ("swordsman", "manatarms"), ("archer", "archer"), ("elitearcher", "royalguard"), ("commander", "manatarms"), ("saboteur", "builder") };
				for (var i = 0; i < pairs.Length; i++)
				{
					var x = 13 + i % 3 * 18; var y = 12 + i / 3 * 18;
					Add(pairs[i].Item1 + "-vs-" + pairs[i].Item2, "duel", Spawn(w, pairs[i].Item1, x - 4, y), Spawn(w, pairs[i].Item2, x + 1, y, enemy));
				}
				var offensive = Spawn(w, "militia", 9, 48); offensive.Trait<AutoTarget>().SetStance(offensive, UnitStance.AttackAnything);
				Add("offensive-pursuit", "offensive", offensive, Spawn(w, "builder", 14, 48, enemy));
				var defensive = Spawn(w, "archer", 27, 48); defensive.Trait<AutoTarget>().SetStance(defensive, UnitStance.Defend);
				Add("defensive-archer", "defensive", defensive, Spawn(w, "builder", 31, 48, enemy));
				Add("neutral-then-manual", "neutral", Spawn(w, "militia", 45, 48), Spawn(w, "builder", 47, 48, enemy));
			}
			else if (wave <= 3)
			{
				for (var i = 0; i < Structures[wave - 1].Length; i++)
				{
					var x = 13 + i % 3 * 18; var y = 12 + i / 3 * 18; var name = Structures[wave - 1][i];
					var victim = Spawn(w, name, x, y, enemy);
					if (name == "woodtower") victim.Trait<AutoTarget>().SetStance(victim, UnitStance.Defend);
					Add("destroy-" + name, "building", Spawn(w, "royalguard", x - 5, y), victim, Spawn(w, "royalguard", x + 1, y - 5));
				}
			}
			else if (wave == 4)
			{
				for (var i = 0; i < 3; i++)
				{
					var y = 12 + i * 18; var ram = Spawn(w, "ram", 14, y);
					var c = Add("ram-" + i, "ram", ram, Spawn(w, i == 0 ? "warehouse" : i == 1 ? "woodgate" : "woodgatey", 22, y, enemy));
					for (var n = 0; n < 4; n++) { var crew = Spawn(w, "swordsman", 12 + n, y + 3); crew.Trait<AutoTarget>().SetStance(crew, UnitStance.AttackAnything); crew.Trait<MedievalSiegeCrew>().Assign(crew, ram); c.Attackers.Add(crew); c.Probes.Add(crew.Trait<MedievalCombatProbe>()); }
				}
				var trebuchet = Spawn(w, "trebuchet", 40, 31);
				var t = Add("trebuchet-real-stone-supply", "trebuchet", trebuchet, Spawn(w, "warehouse", 51, 31, enemy));
				for (var n = 0; n < 4; n++) { var crew = Spawn(w, "swordsman", 38 + n, 34); crew.Trait<MedievalSiegeCrew>().Assign(crew, trebuchet); t.Attackers.Add(crew); t.Probes.Add(crew.Trait<MedievalCombatProbe>()); }
				t.Cart = Spawn(w, "stonecart", 36, 31); t.Cart.Trait<MedievalStockpile>().Stock.Add(3, 40); t.InitialStones = 40;
				t.Cart.Trait<MedievalStoneCart>().Assign(t.Cart, trebuchet);
			}
			else if (wave == 5)
			{
				Add("archers-destroy-building", "building", Spawn(w, "elitearcher", 23, 20), Spawn(w, "house", 28, 20, enemy), Spawn(w, "elitearcher", 23, 21), Spawn(w, "elitearcher", 23, 22), Spawn(w, "elitearcher", 24, 22));
				Add("tower-kills-attacker", "tower", Spawn(w, "woodtower", 40, 36), Spawn(w, "militia", 44, 37, enemy));
				cases[^1].Main.Trait<AutoTarget>().SetStance(cases[^1].Main, UnitStance.Defend);
				Add("stop-attack", "stop", Spawn(w, "archer", 21, 42), Spawn(w, "royalguard", 25, 42, enemy));
				var ally = Spawn(w, "builder", 20, 45); var neutral = Spawn(w, "builder", 20, 47, w.Players.Single(p => p.InternalName == "Neutral"));
				Sync.RunUnsynced(w, () =>
				{
					Check(orders.Against(cases[^1].Main, ally)?.OrderString is not ("Attack" or "ForceAttack"), "Normal contextual click must not attack own citizens.");
					Check(orders.Against(cases[^1].Main, neutral)?.OrderString is not ("Attack" or "ForceAttack"), "Normal contextual click must not attack neutral citizens.");
				});
			}
			else if (wave == 6)
			{
				var crew = Spawn(w, "swordsman", 11, 12);
				crew.Trait<MedievalSiegeCrew>().Assign(crew, Spawn(w, "ram", 13, 14));
				Add("attack-leaves-siege", "interrupt", crew, Spawn(w, "builder", 17, 12, enemy));
				var militia = Spawn(w, "militia", 29, 12); var target = Spawn(w, "builder", 35, 12, enemy);
				var well = Spawn(w, "well", 26, 16); var fire = Spawn(w, "house", 31, 16);
				fire.Trait<MedievalBurnable>().Ignite(fire, target, fire.Location, false);
				Check(militia.Trait<MedievalFirefighter>().Start(militia, fire, well), "Firefighter setup.");
				Add("attack-interrupts-firefighting", "interrupt", militia, target);
				var herder = Spawn(w, "swordsman", 47, 12);
				Check(herder.Trait<MedievalHerding>().Start(herder, Spawn(w, "cow", 48, 15, enemy), Spawn(w, "warehouse", 44, 17)), "Herding setup.");
				Add("attack-interrupts-herding", "interrupt", herder, Spawn(w, "builder", 53, 12, enemy));
				var sleeper = Spawn(w, "swordsman", 11, 30); var tent = Spawn(w, "militarytent", 13, 30); tent.Trait<MedievalCampArea>().RestEnabled = true;
				Check(sleeper.Trait<MedievalRest>().CampRest(sleeper), "Camp rest setup.");
				Add("attack-wakes-soldier", "wake", sleeper, Spawn(w, "builder", 18, 30, enemy));
				var commander = Spawn(w, "commander", 29, 30); commander.Trait<MedievalCampStore>().Stock.Add(1, 1);
				w.IssueOrder(new Order("MedievalCommanderDeploy", commander, Target.FromCell(w, Cell(31, 31)), false));
				Add("attack-interrupts-camp-building", "interrupt", commander, Spawn(w, "builder", 35, 30, enemy));
				var saboteur = Spawn(w, "saboteur", 47, 30);
				w.IssueOrder(new Order("MedievalSabotageFire", saboteur, Target.FromActor(Spawn(w, "house", 51, 33, enemy)), false));
				Add("attack-interrupts-sabotage", "interrupt", saboteur, Spawn(w, "builder", 53, 30, enemy));
				var advancing = Spawn(w, "swordsman", 11, 48); advancing.Trait<AutoTarget>().SetStance(advancing, UnitStance.AttackAnything);
				Add("attack-move-engages-enemy", "advance", advancing, Spawn(w, "builder", 17, 48, enemy));
				var retarget = Add("retarget-another-enemy", "retarget", Spawn(w, "archer", 29, 48), Spawn(w, "builder", 33, 51, enemy));
				retarget.Previous = Spawn(w, "royalguard", 33, 48, enemy); retarget.PreviousHealth = retarget.Previous.Trait<Health>();
			}
			else if (wave == 7)
			{
				var wall = Spawn(w, "palisade", 14, 16, enemy);
				for (var y = 13; y <= 19; y++) for (var x = 14; x <= 19; x++)
					if ((y == 13 || y == 19 || x == 14 || x == 19) && (x != 14 || y != 16)) Spawn(w, "palisade", x, y, enemy);
				var breach = Add("destroy-wall-opens-passage", "breach", Spawn(w, "royalguard", 10, 16), wall);
				breach.Start = Cell(16, 16);
				Check(!breach.Main.Trait<Mobile>().CanEnterCell(wall.Location, check: BlockedByActor.Immovable), "Intact wall must block passage.");
				Add("force-attack-enemy-ram", "force", Spawn(w, "royalguard", 28, 16), Spawn(w, "ram", 33, 16, enemy));
				Add("force-attack-enemy-trebuchet", "force", Spawn(w, "royalguard", 46, 16), Spawn(w, "trebuchet", 51, 16, enemy));
			}
		}
		void Record(Case c, bool success)
		{
			c.Done = true; Check(success, $"{c.Name}: HP {c.Health.HP}/{c.InitialHp}, attacker {c.MainHealth.HP}/{c.InitialMainHp}, shots {c.Probes[0].Shots}, tick {c.Age}.");
			if (c.Kind == "defensive") Check(c.Main.Location == c.Start, "Defensive archer must hold position.");
			if (c.Kind is "interrupt" or "wake")
			{
				Check(c.Main.TraitOrDefault<MedievalSiegeCrew>()?.Assigned != true && c.Main.TraitOrDefault<MedievalFirefighter>()?.Busy != true
					&& c.Main.TraitOrDefault<MedievalHerding>()?.Busy != true && c.Main.Trait<MedievalRest>().Resting != true
					&& c.Main.TraitOrDefault<MedievalCommander>()?.Busy != true && c.Main.TraitOrDefault<MedievalSaboteur>()?.Busy != true, c.Name + ": previous task must be released.");
			}
			if (c.Kind == "retarget") Check(c.PreviousHealth.HP == c.Secondary && c.Secondary < c.PreviousHealth.MaxHP, "Retargeting must stop damage to the previous enemy.");
			if (c.Kind == "duel" && c.Victim.Info.HasTraitInfo<ArmamentInfo>()) Check(c.MainHealth.HP < c.InitialMainHp && c.Health.HP < c.InitialHp, c.Name + ": both sides must really fight.");
			if (c.Kind is "ram" or "trebuchet")
			{
				Check(c.Probes.Skip(1).All(p => p.Shots == 0), c.Name + ": assigned soldiers must operate the engine instead of attacking independently.");
				if (c.Kind == "trebuchet")
				{
					var engine = c.Main.Trait<MedievalSiegeEngine>();
					var stone = engine.Stone + engine.Sling.Amount(3) + engine.Crew.Sum(a => a.Trait<MedievalSiegeCrew>().Cargo) + c.Cart.Trait<MedievalStockpile>().Stock.Amount(3);
					Check(stone + engine.Shots * SiegeRules.StonePerShot == c.InitialStones && c.Cart.Trait<MedievalStoneCart>().Delivered > 0, "Trebuchet must consume only real delivered stones.");
				}
			}
			results.Add(new { c.Name, c.Kind, success, ticks = c.Age, targetHp = c.Health.HP, attackerHp = c.MainHealth.HP, shots = c.Probes.Select(p => p.Shots).ToArray(), c.FirstDamage, c.Death });
			Console.WriteLine($"COMBAT {(success ? "PASS" : "FAIL")}: {c.Name}, {c.Age} ticks, HP {c.Health.HP}");
		}
		void Finish()
		{
			Check(targetingChecks > 0 && TargetingReference.MatchedTargets > 0, "Target reference checks must include real chosen enemies.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, wave, targetingChecks,
				matchedTargets = TargetingReference.MatchedTargets, results }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL COMBAT: " + (errors.Count == 0 ? "PASS" : "FAIL")); done = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1)
			{
				enemy = w.Players.Single(p => p.InternalName == "Rival"); w.LocalPlayer.EnemyPlayersMask = enemy.PlayerMask; enemy.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
				w.LocalPlayer.Shroud.Disabled = true; enemy.Shroud.Disabled = true;
				w.AddFrameEndTask(world => { foreach (var a in world.Actors.Where(a => a.Info.Name.StartsWith("md.")).ToArray()) a.Dispose(); Setup(world); }); return;
			}
			var focus = cases.FirstOrDefault(c => !c.Done) ?? cases.First();
			if (tick % 125 == 0)
				foreach (var actor in actors) targetingChecks += TargetingReference.Verify(actor);
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(focus.Victim.CenterPosition));
			foreach (var c in cases.Where(c => !c.Done))
			{
				c.Age++;
				if (c.Age == 10 && c.Victim.Info.HasTraitInfo<BuildingInfo>())
					Sync.RunUnsynced(w, () => { var hit = orders.AtPixel(c.Victim, renderer); Check(hit.Type == TargetType.Actor && hit.Actor == c.Victim, c.Name + ": clicking the building footprint must target that building."); });
				if (c.Age == 20 && c.Kind is "duel" or "building" or "stop")
				{
					foreach (var a in c.Attackers) Attack(w, a, c.Victim);
					if (c.Kind == "duel" && c.Victim.Info.HasTraitInfo<ArmamentInfo>()) w.IssueOrder(new Order("Attack", c.Victim, Target.FromActor(c.Main), false));
					c.Ordered = true;
				}
				if (c.Age == 30 && c.Kind == "interrupt")
				{
					Check(c.Main.TraitOrDefault<MedievalSiegeCrew>()?.Assigned == true || c.Main.TraitOrDefault<MedievalFirefighter>()?.Busy == true
						|| c.Main.TraitOrDefault<MedievalHerding>()?.Busy == true || c.Main.TraitOrDefault<MedievalCommander>()?.Busy == true
						|| c.Main.TraitOrDefault<MedievalSaboteur>()?.Busy == true, c.Name + ": task must still be active when the attack is ordered.");
					Attack(w, c.Main, c.Victim);
				}
				if (c.Kind == "wake" && !c.Ordered && c.Main.Trait<MedievalRest>().Sleeping) { c.Ordered = true; Attack(w, c.Main, c.Victim); }
				if (c.Age == 20 && c.Kind == "advance") w.IssueOrder(new Order("AttackMove", c.Main, Target.FromCell(w, Cell(23, 48)), false));
				if (c.Age == 20 && c.Kind is "breach" or "force") Attack(w, c.Main, c.Victim, c.Kind == "force" ? Modifiers.Ctrl : Modifiers.None);
				if (c.Kind == "retarget")
				{
					if (c.Age == 20) Attack(w, c.Main, c.Previous);
					if (c.Age == 100) Attack(w, c.Main, c.Victim);
					if (c.Age == 150) c.Secondary = c.PreviousHealth.HP;
				}
				if (c.Kind == "neutral" && c.Age == 120)
				{ Check(c.Health.HP == c.InitialHp && c.Probes[0].Shots == 0, "Neutral stance must wait for a manual attack."); Attack(w, c.Main, c.Victim); }
				if (c.Kind is "ram" or "trebuchet")
				{
					var engine = c.Main.Trait<MedievalSiegeEngine>();
					if (c.Age == 25) Check(c.Attackers.Skip(1).All(a => a.Trait<AutoTarget>().IsTraitDisabled), c.Name + ": crew auto-targeting must be disabled during assignment.");
					if (c.Kind == "trebuchet" && engine.Packed && !engine.Busy && engine.ReadyCrew == 4 && c.Main.IsIdle) w.IssueOrder(new Order("MedievalSiegeDeploy", c.Main, false));
					if (!c.Ordered && engine.CanFire) { Attack(w, c.Main, c.Victim); c.Ordered = true; }
				}
				if (c.Health.HP < c.InitialHp && c.FirstDamage == 0) c.FirstDamage = c.Age;
				if (!c.Screenshot && c.FirstDamage > 0 && c == focus) { c.Screenshot = true; Game.Renderer.SaveScreenshot(Path.Combine(output, c.Name + ".png")); }
				if (c.Kind == "stop")
				{
					if (c.Age == 90) w.IssueOrder(new Order("Stop", c.Main, false));
					if (c.Age == 130) c.Secondary = c.Health.HP;
					if (c.Age == 190) { Check(c.Health.HP == c.Secondary && c.Main.IsIdle, "Stop must end attacks after projectiles already in flight land."); Record(c, c.FirstDamage > 0); }
				}
				else if (c.Kind == "breach" && c.Health.HP <= 0)
				{
					if (c.Death == 0) { c.Death = c.Age; w.IssueOrder(new Order("Move", c.Main, Target.FromCell(w, c.Start), false)); }
					if (c.Main.Location == c.Start) Record(c, true);
					else if (c.Age > c.Death + 300) Record(c, false);
				}
				else if (c.Health.HP <= 0 || c.Kind == "duel" && c.MainHealth.HP <= 0)
				{ c.Death = c.Age; Record(c, c.FirstDamage > 0); }
				else if (c.Age > (c.Kind is "building" or "ram" or "trebuchet" or "force" ? 1800 : 1100)) Record(c, false);
			}
			if (cases.All(c => c.Done) && nextWave == 0) nextWave = tick + 80;
			if (nextWave != 0 && tick >= nextWave) { if (wave == 7) Finish(); else w.AddFrameEndTask(Setup); }
			if (tick > 12000) { Check(false, "Combat suite timed out."); Finish(); }
		}
	}
}
