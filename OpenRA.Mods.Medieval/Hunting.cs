using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	[TraitLocation(SystemActors.World)]
	public class MedievalHuntingGroundsInfo : TraitInfo
	{
		public readonly int MaximumAnimals = 600;
		public override object Create(ActorInitializer init) => new MedievalHuntingGrounds(this);
	}
	public sealed class MedievalHuntingGrounds : ITick, ISync
	{
		readonly MedievalHuntingGroundsInfo info;
		[VerifySync] bool spawned;
		public MedievalHuntingGrounds(MedievalHuntingGroundsInfo info) { this.info = info; }
		void ITick.Tick(Actor self)
		{
			var w = self.World;
			if (spawned || w.Type != WorldType.Regular) return;
			spawned = true;
			w.AddFrameEndTask(world =>
			{
				var neutral = world.Players.First(p => p.InternalName == "Neutral");
				var trees = world.ActorsWithTrait<MedievalSource>().Where(p => p.Trait.StandingTree).Select(p => p.Actor).OrderBy(a => a.ActorID).ToArray();
				var occupied = new HashSet<CPos>();
				var sectors = new HashSet<int2>();
				var count = 0;
				// One finite prey per small woodland sector, not one expensive actor per tree.
				foreach (var tree in trees)
				{
					if (count >= info.MaximumAnimals) break;
					var sector = new int2(tree.Location.X / 6, tree.Location.Y / 6);
					if (sectors.Contains(sector)) continue;
					var cell = world.Map.FindTilesInAnnulus(tree.Location, 1, 4).Where(c => world.Map.Contains(c) && !occupied.Contains(c)
						&& world.Map.GetTerrainInfo(c).Type is "Clear" or "Rough"
						&& !world.ActorMap.GetActorsAt(c).Any()).Select(c => (CPos?)c).FirstOrDefault();
					if (cell == null) continue;
					occupied.Add(cell.Value); sectors.Add(sector);
					world.CreateActor(count++ % 3 == 0 ? "md.game-deer" : "md.game-rabbit", [new OwnerInit(neutral), new LocationInit(cell.Value)]);
				}
			});
		}
	}
	public class MedievalGameAnimalInfo : TraitInfo, Requires<MobileInfo>, Requires<MedievalSourceInfo>
	{
		public readonly string Image = "md.wild-deer";
		public override object Create(ActorInitializer init) => new MedievalGameAnimal(init, this);
	}
	public sealed class MedievalGameAnimal : ITick, IRender, ISync
	{
		readonly MedievalGameAnimalInfo info;
		readonly CPos home;
		readonly Mobile mobile;
		Actor hunter;
		[VerifySync] int cadence;
		[VerifySync] public bool Down { get; private set; }
		[VerifySync] public int Fled { get; private set; }
		[VerifySync] public int HunterId => (int)(hunter?.ActorID ?? 0);
		public int FallAge { get; private set; } = -1;
		int FacingFrame => ((6 - ((mobile.Facing.Angle + 64) / 128)) + 8) % 8;
		public MedievalGameAnimal(ActorInitializer init, MedievalGameAnimalInfo info)
		{ this.info = info; home = init.Self.Location; mobile = init.Self.Trait<Mobile>(); cadence = (int)(init.Self.ActorID % 75) + 25; }
		public bool Available(Actor claimant) => hunter == null || hunter == claimant || !hunter.IsInWorld || hunter.IsDead;
		public void Claim(Actor claimant) { hunter = claimant; }
		public void ReleaseClaim() { hunter = null; }
		public void Harvested(Actor self)
		{
			if (!Down) FallAge = 0;
			Down = true; self.CancelActivity(); ReleaseClaim();
			if (self.Trait<MedievalSource>().Remaining == 0)
			{
				var remains = new HuntingRemainsEffect(self.CenterPosition, info.Image, FacingFrame, FallAge);
				self.World.AddFrameEndTask(w => { w.Add(remains); if (!self.Disposed) self.Dispose(); });
			}
		}
		void ITick.Tick(Actor self)
		{
			if (Down && FallAge < 35) FallAge++;
			if (hunter != null && (!hunter.IsInWorld || hunter.IsDead)) hunter = null;
			if (Down || --cadence > 0 || !self.IsIdle) return;
			cadence = 100 + (int)(self.ActorID % 75);
			if (hunter != null) { self.CancelActivity(); return; }
			var threats = self.World.ActorMap.ActorsInBox(self.CenterPosition - new WVec(2200, 2200, 0), self.CenterPosition + new WVec(2200, 2200, 0));
			var threat = threats.Where(a => a.IsInWorld && !a.IsDead && a.TraitOrDefault<MedievalNeedsFood>() != null)
				.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
			if (threat != null)
			{
				var escape = self.World.Map.FindTilesInAnnulus(self.Location, 2, 4).Where(c => mobile.CanEnterCell(c, check: BlockedByActor.All)
					&& (c - home).LengthSquared < 100).OrderByDescending(c => (self.World.Map.CenterOfCell(c) - threat.CenterPosition).HorizontalLengthSquared)
					.Select(c => (CPos?)c).FirstOrDefault();
				if (escape != null) { Fled++; self.QueueActivity(mobile.MoveTo(escape.Value, 0)); return; }
			}
			var cells = self.World.Map.FindTilesInAnnulus(home, 1, 3).Where(c => mobile.CanEnterCell(c, check: BlockedByActor.All)).ToArray();
			if (cells.Length != 0) self.QueueActivity(mobile.MoveTo(cells[self.World.SharedRandom.Next(cells.Length)], 0));
		}
		public int VisualFrame(Actor self)
		{
			var moving = mobile.CurrentMovementTypes.HasMovementType(MovementType.Horizontal);
			// Wildlife sheets face east first; convert the engine's counterclockwise yaw.
			var facing = FacingFrame;
			return (moving ? 96 : 0) + facing * 12 + (self.World.WorldTick / (moving ? 4 : 9) + (int)(self.ActorID % 12)) % 12;
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (self.World.FogObscures(self)) yield break;
			var sprite = Down ? self.World.Map.Sequences.GetSequence(info.Image + "-fall", "idle").GetSprite(FacingFrame * 12 + Math.Clamp(FallAge / 3, 0, 11))
				: self.World.Map.Sequences.GetSequence(info.Image, "idle").GetSprite(VisualFrame(self));
			yield return new SpriteRenderable(sprite, self.CenterPosition, WVec.Zero, 0, null, 1, 1,
				Down ? new Vector3(.85f, .82f, .78f) : Vector3.One, TintModifiers.None, false);
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			var p = wr.ScreenPxPosition(self.CenterPosition);
			yield return new Rectangle(p.X - 36, p.Y - 40, 72, 60);
		}
	}
	public partial class MedievalWorker
	{
		public string HuntingSequence => Info.Profession != "hunter" || phase != 3 || source == null ? null
			: !IsWorking ? "hunt-stalk" : source.Trait<MedievalGameAnimal>().Down ? "hunt-gather" : countdown <= 2 ? "hunt-release" : "hunt-aim";
		public int HuntingFrame => HuntingSequence == "hunt-release" ? Math.Clamp((2 - countdown) * 4 + (cadence <= 4 ? 3 : 0), 0, 7)
			: Math.Clamp((Info.WorkTicks - countdown) * 8 / Math.Max(1, Info.WorkTicks - 2), 0, 7);

		bool ApproachGame(Actor self)
		{
			if (source == null) return false;
			if (source.Trait<MedievalGameAnimal>().Down) return Approach(self, source);
			if ((source.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= 2560L * 2560)
			{ failedMoves = 0; source.CancelActivity(); return true; }
			if (++failedMoves > 3) { Release(); cadence = 125; Status = "Gibier inaccessible"; return false; }
			self.QueueActivity(self.Trait<Mobile>().MoveWithinRange(Target.FromActor(source), new WDist(2048)));
			Status = "Piste le gibier près de la cabane"; IsWorking = false; WorkTarget = null;
			return false;
		}
	}
}
