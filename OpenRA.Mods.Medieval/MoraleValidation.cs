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
	public class MedievalMoraleValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalMoraleValidation();
	}
	public sealed class MedievalMoraleValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		int tick, alliedDamage, enemyDamage;
		Actor soldier, enemy, neutral, remote, drummer, duplicate, friendlyTarget, enemyTarget;
		Player foe, bystander;
		readonly List<string> errors = [];
		readonly HashSet<int> playingFrames = [];
		bool finished;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool test, string message) { if (!test && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "morale-preview"); Directory.CreateDirectory(output);
		}
		Actor Spawn(World w, string type, Player owner, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(owner), new LocationInit(Cell(x, y))]);
		void Market(World w, Player owner, int x, int y) => Spawn(w, "market", owner, x, y).Trait<MedievalStockpile>().Stock.Add(1, 200);
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			foe = w.Players.First(p => p.InternalName == "Creeps"); bystander = w.Players.First(p => p.InternalName == "Neutral");
			w.LocalPlayer.EnemyPlayersMask = foe.PlayerMask; foe.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			Market(w, w.LocalPlayer, 22, 28); Market(w, foe, 37, 32); Market(w, bystander, 27, 36);
			soldier = Spawn(w, "moraletroop", w.LocalPlayer, 29, 30); enemy = Spawn(w, "moraletroop", foe, 34, 30);
			neutral = Spawn(w, "moraletroop", bystander, 30, 33); remote = Spawn(w, "moraletroop", w.LocalPlayer, 54, 52);
			drummer = Spawn(w, "drummer", w.LocalPlayer, 30, 30); duplicate = Spawn(w, "drummer", w.LocalPlayer, 30, 31);
			friendlyTarget = Spawn(w, "moraledummy", bystander, 28, 30); enemyTarget = Spawn(w, "moraledummy", bystander, 35, 30);
		}
		void Select(World w, params Actor[] actors) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, actors, false, true));
		void Move(World w, Actor actor, int x, int y) => w.IssueOrder(new Order("Move", actor, Target.FromCell(w, Cell(x, y)), false));
		static int Moral(Actor a) => a.Trait<MedievalMorale>().Value;
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			renderer.Viewport.Center(w.Map.CenterOfCell(tick < 3500 ? Cell(31, 32) : Cell(52, 52)));
			if (drummer.Trait<WithMedievalInfantryBody>().Sequence == "work") playingFrames.Add(drummer.Trait<WithMedievalInfantryBody>().Frame);
			if (tick == 550) { Select(w, drummer, soldier, enemy); renderer.Viewport.AdjustZoom((float)Math.Log(1.5f / renderer.Viewport.Zoom)); }
			if (tick == 600) Game.Renderer.SaveScreenshot(Path.Combine(output, "drummer-and-morale.png"));
			if (tick == 650)
			{
				Check(Moral(soldier) == 90 && soldier.Trait<MedievalMorale>().Encouragement == 20, "Two friendly drummers must grant +20 morale once.");
				Check(Moral(enemy) == 60, "Enemy within five cells must lose ten morale.");
				Check(Moral(neutral) == 70, "Neutral troop must not be intimidated or encouraged.");
				w.IssueOrder(new Order("ForceAttack", soldier, Target.FromActor(friendlyTarget), false));
				w.IssueOrder(new Order("ForceAttack", enemy, Target.FromActor(enemyTarget), false));
			}
			if (tick > 650 && alliedDamage == 0 && friendlyTarget.Trait<Health>().HP < 100000)
			{
				alliedDamage = 100000 - friendlyTarget.Trait<Health>().HP;
				Check(alliedDamage == 1100, "Encouraged soldier must actually deal 110 percent damage.");
				w.IssueOrder(new Order("Stop", soldier, false));
			}
			if (tick > 650 && enemyDamage == 0 && enemyTarget.Trait<Health>().HP < 100000)
			{
				enemyDamage = 100000 - enemyTarget.Trait<Health>().HP;
				Check(enemyDamage == 950, "Intimidated soldier must actually deal 95 percent damage.");
				w.IssueOrder(new Order("Stop", enemy, false));
			}
			if (tick == 700) { Move(w, drummer, 46, 22); Move(w, duplicate, 47, 22); }
			if (tick == 1300)
			{
				Check(Moral(soldier) == 70 && Moral(enemy) == 70, "Music effects must decay after musicians leave the area.");
				Check(Moral(remote) == 40 && remote.Trait<MedievalMorale>().State.Firepower == 85, "Unfed isolated soldier must lose morale and damage.");
				Check(((IFirepowerModifier)remote.Trait<MedievalNeedsFood>()).GetFirepowerModifier() == 100, "Food must not apply another military damage penalty.");
				Move(w, drummer, 30, 30);
				duplicate.Trait<Health>().InflictDamage(duplicate, duplicate, new Damage(100000), true);
			}
			if (tick == 2000) w.AddFrameEndTask(world => Market(world, world.LocalPlayer, 52, 55));
			if (tick == 2250)
			{
				Check(Moral(soldier) == 90 && Moral(enemy) == 60, "One returning drummer must restore the same non-stacking aura.");
				w.AddFrameEndTask(_ => drummer.ChangeOwner(foe));
			}
			if (tick == 2820)
			{
				Check(Moral(soldier) == 60 && enemy.Trait<MedievalMorale>().Target == 90 && Moral(enemy) >= 80,
					"A captured drummer must support its new side gradually and intimidate the old one.");
				Check(Moral(remote) == 70 && !remote.Trait<MedievalNeedsFood>().Need.Hungry, "A real market meal must let morale recover gradually.");
				Check(Moral(neutral) == 70, "Neutral relations must stay unaffected after capture.");
			}
			if (tick == 2850) Move(w, drummer, 51, 51);
			if (tick == 4600) Select(w, remote, drummer);
			if (tick == 4700)
			{
				Check(drummer.Trait<MedievalNeedsFood>().Need.Hungry && !drummer.Trait<MedievalNeedsFood>().Need.Starving, "The isolated musician must become hungry.");
				Check(remote.Trait<MedievalMorale>().Intimidation == 5 && Moral(remote) == 65, "A hungry drummer must have half-strength intimidation.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "hungry-drummer.png"));
			}
			if (tick == 7250) Game.Renderer.SaveScreenshot(Path.Combine(output, "silent-drummer.png"));
			if (tick == 7300)
			{
				Check(drummer.Trait<MedievalNeedsFood>().Need.Starving && !drummer.Trait<MedievalDrummer>().Playing(drummer), "A starving musician must stop playing.");
				Check(remote.Trait<MedievalMorale>().Intimidation == 0 && Moral(remote) == 70, "A silent drummer must stop affecting nearby troops.");
				Check(playingFrames.Count == 8, "Drumming must use all eight articulated animation poses.");
				Check(alliedDamage > 0 && enemyDamage > 0, "Both real damage checks must run.");
				File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors,
					alliedDamage, enemyDamage, drummerFrames = playingFrames.Count, remoteMorale = Moral(remote), starvingMusician = drummer.Trait<MedievalNeedsFood>().Need.Starving
				}, new JsonSerializerOptions { WriteIndented = true }));
				Console.WriteLine("MEDIEVAL MORALE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
				finished = true; Game.RunAfterDelay(1000, Game.Exit);
			}
		}
	}
}
