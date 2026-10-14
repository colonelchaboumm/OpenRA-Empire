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
	public sealed class SiegePushNetworkAuditInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new SiegePushNetworkAudit();
	}
	public sealed class SiegePushNetworkAudit : IWorldLoaded, ITick
	{
		readonly List<Actor> engines = [], crew = [];
		readonly Dictionary<Actor, Actor> assigned = [];
		readonly Dictionary<Actor, WPos> previous = [];
		readonly List<string> errors = [];
		readonly List<object> checkpoints = [];
		Player[] players;
		int tick, movingContact;
		bool finished;
		void Check(bool ok, string error) { if (!ok && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			players = w.Players.Where(p => p.PlayerReference.Playable).OrderBy(p => p.InternalName).ToArray();
			Check(w.LobbyInfo.NonBotPlayers.Count() == 2 && players.Length == 2, "Two independent human clients required.");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", StringComparison.Ordinal)).ToArray()) a.Dispose();
			for (var p = 0; p < players.Length; p++)
			{
				players[p].Shroud.Disabled = true;
				Actor Spawn(string type, CPos cell) => w.CreateActor("md." + type, [new OwnerInit(players[p]), new LocationInit(cell)]);
				var types = new[] { "ram", "trebuchet", "cauldron" };
				for (var i = 0; i < types.Length; i++)
				{
					var cell = new CPos(47 + i * 12, -14 + p * 24);
					var engine = Spawn(types[i], cell); engines.Add(engine);
					engine.TraitOrDefault<AutoTarget>()?.SetStance(engine, UnitStance.HoldFire);
					for (var j = 0; j < engine.Trait<MedievalSiegeEngine>().CrewLimit; j++)
					{
						var soldier = Spawn(j % 2 == 0 ? "swordsman" : "militia", cell + new CVec(j - 1, 3));
						soldier.Trait<AutoTarget>().SetStance(soldier, UnitStance.HoldFire);
						crew.Add(soldier); assigned[soldier] = engine; previous[soldier] = soldier.CenterPosition;
					}
				}
			}
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			Check(!NetworkAuditLoadScreen.Orders.IsOutOfSync, "Native TCP synchronization mismatch.");
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (tick == 20)
				foreach (var soldier in crew.Where(a => a.Owner == w.LocalPlayer))
					w.IssueOrder(new Order("MedievalAssignSiege", soldier, Target.FromActor(assigned[soldier]), false));
			if (tick is 500 or 800 or 1100 or 1400)
			{
				Check(crew.All(a => a.Trait<MedievalSiegeCrew>().Pushing), "Every servant reaches its handhold before the next leg: " + tick);
				var offset = tick == 500 ? new CVec(3, 0) : tick == 800 ? new CVec(0, 3) : tick == 1100 ? new CVec(-3, 0) : new CVec(0, -3);
				foreach (var engine in engines.Where(a => a.Owner == w.LocalPlayer))
					w.IssueOrder(new Order("Move", engine, Target.FromCell(w, engine.Location + offset), false));
			}
			foreach (var soldier in crew)
			{
				var step = (soldier.CenterPosition - previous[soldier]).HorizontalLength; previous[soldier] = soldier.CenterPosition;
				Check(step < 200, "A servant teleported during network movement.");
				if (step > 0 && soldier.Trait<MedievalSiegeCrew>().Pushing) movingContact++;
			}
			if (tick % 100 == 0) checkpoints.Add(new { tick, hash = w.SyncHash(), movingContact, pushing = crew.Count(a => a.Trait<MedievalSiegeCrew>().Pushing) });
			if (tick < 1800) return;
			Check(movingContact > 500, "Both players' real servants push moving machines over TCP.");
			Check(crew.All(a => a.Trait<MedievalSiegeCrew>().Pushing), "All servants regain contact after turns.");
			File.WriteAllText(Path.Combine(Platform.SupportDir, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick,
				local = w.LocalPlayer.InternalName, movingContact, checkpoints }, new JsonSerializerOptions { WriteIndented = true }));
			finished = true; Game.RunAfterDelay(2000, Game.Exit);
		}
	}
}
