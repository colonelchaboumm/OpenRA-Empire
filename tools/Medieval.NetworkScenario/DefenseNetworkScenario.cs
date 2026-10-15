using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Medieval;
using OpenRA.Traits;

namespace OpenRA.Platforms.NetworkTest
{
	public sealed class DefenseNetworkAuditInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new DefenseNetworkAudit();
	}
	public sealed class DefenseNetworkAudit : IWorldLoaded, ITick
	{
		readonly List<string> errors = [];
		readonly List<object> checkpoints = [];
		Player[] players;
		Actor guard, cart, attacker, defender, secondAttacker;
		Health guardHp, cartHp, defenderHp;
		MedievalCombatProbe guardProbe, defenderProbe;
		int tick;
		bool finished;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string error) { if (!value && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			players = w.Players.Where(p => p.PlayerReference.Playable).OrderBy(p => p.InternalName).ToArray();
			Check(players.Length == 2 && w.LobbyInfo.NonBotPlayers.Count() == 2, "Two independent TCP clients.");
		}
		Actor Spawn(World w, string type, int p, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(players[p]), new LocationInit(Cell(x, y))]);
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			foreach (var p in players) p.Shroud.Disabled = true;
			guard = Spawn(w, "royalguard", 0, 20, 30); cart = Spawn(w, "tradecart", 0, 20, 28); attacker = Spawn(w, "archer", 1, 24, 28);
			defender = Spawn(w, "royalguard", 1, 40, 42); secondAttacker = Spawn(w, "archer", 0, 44, 42);
			guardHp = guard.Trait<Health>(); cartHp = cart.Trait<Health>(); defenderHp = defender.Trait<Health>();
			guardProbe = guard.Trait<MedievalCombatProbe>(); defenderProbe = defender.Trait<MedievalCombatProbe>();
			foreach (var a in new[] { guard, defender }) a.Trait<AutoTarget>().SetStance(a, UnitStance.ReturnFire);
			foreach (var a in new[] { attacker, secondAttacker })
			{
				a.Trait<AutoTarget>().SetStance(a, UnitStance.HoldFire);
				var hp = a.Trait<Health>(); hp.InflictDamage(a, a, new Damage(hp.HP * 2 / 3), true);
			}
		}
		void Send(World w, Order order) { if (order.Subject.Owner == w.LocalPlayer) w.IssueOrder(order); }
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			Check(!NetworkAuditLoadScreen.Orders.IsOutOfSync, "No TCP synchronization mismatch.");
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 20) Send(w, new Order("MedievalEscort", guard, Target.FromActor(cart), false));
			if (tick == 50)
			{
				Send(w, new Order("Attack", attacker, Target.FromActor(cart), false));
				Send(w, new Order("Attack", secondAttacker, Target.FromActor(defender), false));
			}
			if (tick == 700)
			{
				Check(attacker.IsDead && secondAttacker.IsDead && guardProbe.Shots > 0 && defenderProbe.Shots > 0, "Both nations retaliate with real weapons, including protection of an attacked cart.");
				Check(cartHp.HP < cartHp.MaxHP && defenderHp.HP < defenderHp.MaxHP, "Actual enemy hits triggered the responses.");
				Check((defender.Location - Cell(40, 42)).LengthSquared <= 2, "Ordinary defender returns to original position.");
				Check(guard.Trait<MedievalEscort>().Protected == cart, "Escort survives combat without losing its mission.");
				Send(w, new Order("Move", cart, Target.FromCell(w, Cell(28, 28)), false));
			}
			if (tick == 1200)
			{
				Check((guard.CenterPosition - cart.CenterPosition).HorizontalLengthSquared <= 3072L * 3072, "Escort follows its moving cart after combat.");
				w.AddFrameEndTask(_ =>
				{
					attacker = Spawn(w, "archer", 1, 31, 28); attacker.Trait<AutoTarget>().SetStance(attacker, UnitStance.HoldFire);
				});
			}
			if (tick == 1220) Send(w, new Order("Attack", attacker, Target.FromActor(guard), false));
			if (tick == 1450)
			{
				Check(guardHp.HP < guardHp.MaxHP && guardProbe.Shots > 1, "Escort also defends itself while following.");
				Send(w, new Order("Move", guard, Target.FromCell(w, Cell(20, 36)), false));
				if (!attacker.IsDead) Send(w, new Order("Stop", attacker, false));
			}
			if (tick % 50 == 0) checkpoints.Add(new { tick, hash = w.SyncHash(), guardHp = guardHp.HP, defenderHp = defenderHp.HP,
				cartHp = cartHp.HP, guardShots = guardProbe.Shots, defenderShots = defenderProbe.Shots, escort = guard.Trait<MedievalEscort>().ProtectedId });
			if (tick < 2000) return;
			Check(guard.Trait<MedievalEscort>().Protected == null && (guard.Location - Cell(20, 36)).LengthSquared <= 2, "An explicit move ends escort and permits escape on both clients.");
			File.WriteAllText(Path.Combine(Platform.SupportDir, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, local = w.LocalPlayer.InternalName, checkpoints }, new JsonSerializerOptions { WriteIndented = true }));
			finished = true; Game.RunAfterDelay(2000, Game.Exit);
		}
	}
}
