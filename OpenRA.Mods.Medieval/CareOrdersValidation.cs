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
	public class MedievalCareOrdersValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalCareOrdersValidation();
	}
	public sealed class MedievalCareOrdersValidation : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<string> checks = [];
		Actor church, guard, cart, saboteur, enemy, commander, captor, ram;
		CPos site;
		string output;
		int tick;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool passed, string message)
		{ checks.Add(message); if (!passed) errors.Add(message); Console.WriteLine($"CARE ORDERS {(passed ? "PASS" : "FAIL")}: {message}"); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ output = Path.Combine(Platform.EngineDir, "analysis/medieval/care-orders"); Directory.CreateDirectory(output); }
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y, Player owner = null) => w.CreateActor("md." + name,
				[new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
			var rival = w.Players.Single(p => p.InternalName == "Rival");
			w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask; rival.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			church = Spawn("church", 26, 25); guard = Spawn("militia", 24, 25);
			cart = Spawn("tradecart", 22, 23); saboteur = Spawn("saboteur", 25, 29);
			commander = Spawn("commander", 30, 25); enemy = Spawn("house", 44, 25, rival);
			captor = Spawn("swordsman", 30, 29); ram = Spawn("ram", 36, 33, rival);
			commander.Trait<MedievalCampStore>().Stock.Add(1, 1);
			foreach (var a in new[] { guard, saboteur, commander, captor })
			{
				a.Trait<Health>().InflictDamage(a, a, new Damage(a.Trait<Health>().MaxHP / 2), true);
				a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire);
			}
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); }
			if (tick == 10)
				foreach (var a in new[] { guard, saboteur, commander, captor })
					w.IssueOrder(new Order("MedievalChurchCare", a, Target.FromActor(church), false));
			if (tick == 30)
			{
				foreach (var a in new[] { guard, saboteur, commander, captor })
					Check(a.Trait<MedievalPatient>().Church == church && a.Trait<AutoTarget>().IsTraitDisabled, a.Info.Name + " starts care and suspends combat");
				w.IssueOrder(new Order("MedievalEscort", guard, Target.FromActor(enemy), false));
				w.IssueOrder(new Order("MedievalSabotageFire", saboteur, Target.FromActor(church), false));
				w.IssueOrder(new Order("MedievalCommanderDeploy", commander, Target.FromCell(w, church.Location), false));
				w.IssueOrder(new Order("CaptureActor", captor, Target.FromActor(church), false));
			}
			if (tick == 50)
			{
				foreach (var a in new[] { guard, saboteur, commander, captor })
					Check(a.Trait<MedievalPatient>().Church == church, a.Info.Name + " retains care when its new mission is invalid");
				site = w.Map.FindTilesInCircle(commander.Location, 6).Where(c => commander.Trait<MedievalCommander>().CanDeploy(commander, c))
					.OrderBy(c => (c - commander.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).First();
				w.IssueOrder(new Order("MedievalEscort", guard, Target.FromActor(cart), false));
				w.IssueOrder(new Order("MedievalSabotageFire", saboteur, Target.FromActor(enemy), false));
				w.IssueOrder(new Order("MedievalCommanderDeploy", commander, Target.FromCell(w, site), false));
				w.IssueOrder(new Order("CaptureActor", captor, Target.FromActor(ram), false));
			}
			if (tick == 70)
			{
				foreach (var a in new[] { guard, saboteur, commander, captor })
					Check(a.Trait<MedievalPatient>().Church == null, a.Info.Name + " leaves care after accepting a new mission");
				Check(guard.Trait<MedievalEscort>().Protected == cart && !guard.Trait<AutoTarget>().IsTraitDisabled, "Escort can defend its cart after leaving care");
				Check(saboteur.Trait<MedievalSaboteur>().Busy, "Sabotage starts after care");
				Check(commander.Trait<MedievalCommander>().Busy, "Tent deployment starts after care");
				church.Dispose();
			}
			if (tick != 1200) return;
			Check(guard.Trait<MedievalEscort>().Protected == cart, "Losing the former church does not cancel the escort");
			Check(ram.Owner == captor.Owner && captor.Trait<MedievalSiegeCrew>().Serves(ram), "Wounded soldier completes capture and joins the crew after leaving care");
			Check(w.Actors.Any(a => a.Info.Name == "md.militarytent" && a.Owner == commander.Owner && a.Location == site), "Commander completes the tent after leaving care");
			Check(commander.Trait<MedievalCampStore>().Stock.Amount(1) == 0, "Completed tent consumes exactly one carried tent");
			Check(!commander.Trait<AutoTarget>().IsTraitDisabled && !saboteur.Trait<AutoTarget>().IsTraitDisabled, "Finished missions release all combat restrictions");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, checks, tick }, new JsonSerializerOptions { WriteIndented = true }));
			Game.RunAfterDelay(500, Game.Exit);
		}
	}
}
