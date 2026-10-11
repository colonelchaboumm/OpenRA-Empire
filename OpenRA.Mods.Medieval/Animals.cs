using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// Players command the guide, never move cattle as if they were soldiers.
	public class MedievalAnimalMobileInfo : MobileInfo
	{
		public override object Create(ActorInitializer init) => new MedievalAnimalMobile(init, this);
	}
	public sealed class MedievalAnimalMobile : Mobile, IIssueOrder, IResolveOrder
	{
		public MedievalAnimalMobile(ActorInitializer init, MobileInfo info) : base(init, info) { }
		IEnumerable<IOrderTargeter> IIssueOrder.Orders => [];
		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) => null;
		void IResolveOrder.ResolveOrder(Actor self, Order order) { }
	}
	public class MedievalYoungAnimalInit : RuntimeFlagInit { }
	public class MedievalAnimalInfo : TraitInfo, Requires<MobileInfo>
	{
		public readonly string Image = "md.livestock-cow";
		public readonly int MaturityTicks = 750;
		public readonly int WoolTicks = 1000;
		public override object Create(ActorInitializer init) => new MedievalAnimal(init, this);
	}
	// These are synchronized world actors, independent of the camera and decorative wildlife.
	public sealed class MedievalAnimal : ITick, IRender, IRenderAnnotations, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		readonly MedievalAnimalInfo info;
		readonly HashSet<Actor> claimants = [];
		[VerifySync] CPos home;
		WPos previous;
		bool changingOwner;
		[VerifySync] int age, wool, cadence, gait, facing;
		[VerifySync] bool moving;
		public Actor Pen { get; private set; }
		public Actor Escort { get; private set; }
		public Actor Calling { get; set; }
		public Actor RequestedPen { get; private set; }
		[VerifySync] public int PenId => (int)(Pen?.ActorID ?? 0);
		[VerifySync] public int EscortId => (int)(Escort?.ActorID ?? 0);
		[VerifySync] public int CallingId => (int)(Calling?.ActorID ?? 0);
		[VerifySync] public int RequestedId => (int)(RequestedPen?.ActorID ?? 0);
		[VerifySync] public int ClaimsHash => claimants.OrderBy(a => a.ActorID).Aggregate(17, (h, a) => unchecked(h * 31 + (int)a.ActorID));
		public bool Adult => age >= info.MaturityTicks;
		public string VisualImage => info.Image + (Adult ? "" : "-young");
		public int VisualFrame(Actor self)
		{
			var guided = Escort != null || Calling?.TraitOrDefault<MedievalHerding>()?.Working == true;
			var time = (self.World.WorldTick + (int)(self.ActorID % 100) * 23) % 240;
			var mode = moving ? guided ? 4 : 1 : guided ? 3 : time >= 144 ? 2 : 0;
			var step = moving ? gait * 8 / 720 : mode == 2 ? (time - 144) / 12 : (self.World.WorldTick / 10 + (int)self.ActorID) % 8;
			return mode * 64 + facing * 8 + step;
		}
		public bool CanShear => Adult && wool == 0;
		public bool ClaimedByGuide(Player player) => claimants.Any(a => a.Owner == player && MedievalStockpile.Active(a));
		public bool Claimed(Player player) => RequestedPen?.Owner == player || claimants.Any(a => a.Owner == player && MedievalStockpile.Active(a));
		public string Description => (info.Image.EndsWith("sheep", StringComparison.Ordinal) ? Adult ? "Mouton" : "Agneau" : Adult ? "Vache" : "Veau")
			+ (Escort != null ? " : accompagne vers le nouvel enclos" : RequestedPen != null ? " : transfert demande" : Pen != null ? " : dans son enclos" : " : en liberte")
			+ (!Adult ? $" | Adulte dans {(info.MaturityTicks - age + 24) / 25}s" : "");
		public MedievalAnimal(ActorInitializer init, MedievalAnimalInfo info)
		{
			this.info = info; home = init.Self.Location; previous = init.Self.CenterPosition;
			age = init.Contains<MedievalYoungAnimalInit>(info) ? 0 : info.MaturityTicks;
			cadence = (int)(init.Self.ActorID % 50) + 25;
		}
		public void Shear() { wool = info.WoolTicks; }
		public void Claim(Actor guide) { claimants.Add(guide); }
		public void Unclaim(Actor guide) { claimants.Remove(guide); if (Calling == guide) Calling = null; }
		public void SetPen(Actor self, Actor pen)
		{
			if (Pen == pen) return;
			if (Pen is { Disposed: false }) Pen.Trait<MedievalLivestock>().Remove(self);
			Pen = pen;
			self.CancelActivity(); home = self.Location;
			if (pen != null) { Escort = Calling = null; ClearRequest(self); }
		}
		public bool Request(Actor self, Actor pen, Player player)
		{
			if (!MedievalStockpile.Active(self) || !(self.Owner == player || self.Owner.NonCombatant) || !self.CanBeViewedByPlayer(player)
				|| !MedievalStockpile.Active(pen) || pen.TraitOrDefault<MedievalLivestock>() is not { } herd || !herd.CanReceive(self, player)
				|| pen.Trait<MedievalAgriculturalArea>().Farm(pen) == null) return false;
			ClearRequest(self);
			foreach (var a in claimants.Where(a => a.Owner == player).ToArray()) a.Trait<MedievalHerding>().Cancel(a);
			if (!herd.Reserve(self, player)) return false;
			RequestedPen = pen; return true;
		}
		public void ClearRequest(Actor self)
		{
			if (RequestedPen is { Disposed: false }) RequestedPen.Trait<MedievalLivestock>().Unreserve(self);
			RequestedPen = null;
		}
		public void CancelTransfer(Actor self, Player player)
		{
			if (RequestedPen?.Owner == player) ClearRequest(self);
			foreach (var a in claimants.Where(a => a.Owner == player).ToArray()) a.Trait<MedievalHerding>().Cancel(a);
		}
		public void Lead(Actor self, Actor guide)
		{
			SetPen(self, null); ClearRequest(self); Escort = guide; Calling = null;
			self.CancelActivity();
			if (self.Owner != guide.Owner)
			{
				// The engine briefly removes an actor while changing its owner.
				changingOwner = true;
				try { self.ChangeOwnerSync(guide.Owner); }
				finally { changingOwner = false; }
			}
		}
		public void Release(Actor self, Actor guide)
		{
			Unclaim(guide);
			if (Escort != guide) return;
			Escort = null; self.CancelActivity(); home = self.Location;
		}
		void Slide(Actor self, WPos target)
		{
			var delta = target - self.CenterPosition; var length = delta.HorizontalLength;
			if (length == 0) return;
			var next = self.CenterPosition + delta * Math.Min(18, length) / length;
			var mobile = self.Trait<Mobile>();
			if (self.World.Map.CellContaining(next) != self.Location) mobile.SetPosition(self, next);
			mobile.SetCenterPosition(self, next);
		}
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self)) return;
			if (age < info.MaturityTicks) age++;
			if (wool > 0) wool--;
			if (RequestedPen != null && (!MedievalStockpile.Active(RequestedPen) || !RequestedPen.Trait<MedievalLivestock>().Compatible(self)
				|| !self.Owner.NonCombatant && RequestedPen.Owner != self.Owner)) ClearRequest(self);
			if (Pen != null && (!MedievalStockpile.Active(Pen) || Pen.Owner != self.Owner)) SetPen(self, null);
			if (Escort != null && (!MedievalStockpile.Active(Escort) || Escort.Owner != self.Owner || Escort.Trait<MedievalHerding>().Animal != self)) Release(self, Escort);
			if (Pen != null)
			{
				var herd = Pen.Trait<MedievalLivestock>();
				if (Calling != null && !MedievalStockpile.Active(Calling)) Calling = null;
				var target = Calling != null ? self.World.Map.CenterOfCell(herd.Gate) : herd.Position(Pen, LivestockMotion.Sample(Pen.Trait<MedievalAgriculturalArea>().Size, self.ActorID, 0, self.World.WorldTick));
				Slide(self, target);
			}
			else if (--cadence <= 0)
			{
				cadence = 20;
				if (Escort != null)
				{
					// Livestock occupy a whole cell and cannot stand on their guide's cell.
					// A sub-cell distance can leave no reachable path target when the guide
					// stops at a gate. Stop on a neighboring cell, inside the guide's 1536
					// delivery radius, with a little hysteresis to avoid constant repathing.
					if (!Escort.Trait<MedievalHerding>().Entering && self.IsIdle && (Escort.CenterPosition - self.CenterPosition).HorizontalLengthSquared > 1400L * 1400)
						self.QueueActivity(self.Trait<Mobile>().MoveWithinRange(Target.FromActor(Escort), new WDist(1280)));
				}
				else if (Calling == null && self.IsIdle)
				{
					cadence = 125 + (int)(self.ActorID % 75);
					var hash = LivestockMotion.Hash(self.ActorID + (uint)(self.World.WorldTick / 125) * 31);
					var target = home + new CVec((int)(hash % 7) - 3, (int)(hash / 7 % 7) - 3);
					var mobile = self.Trait<Mobile>();
					if (self.World.Map.Contains(target) && mobile.CanEnterCell(target, check: BlockedByActor.All)
						&& !self.World.ActorMap.GetActorsAt(target).Any(a => a.TraitOrDefault<Building>() != null)) self.QueueActivity(mobile.MoveTo(target));
				}
			}
			var travel = self.CenterPosition - previous; moving = travel.HorizontalLength > 0;
			if (moving)
			{
				facing = ((int)MathF.Round(MathF.Atan2(travel.Y, travel.X) * 4 / MathF.PI) + 8) % 8;
				gait = (gait + travel.HorizontalLength) % 720;
			}
			else if (Calling != null || Escort != null)
			{
				var towards = (Calling ?? Escort).CenterPosition - self.CenterPosition;
				if (towards.HorizontalLengthSquared > 0)
					facing = ((int)MathF.Round(MathF.Atan2(towards.Y, towards.X) * 4 / MathF.PI) + 8) % 8;
			}
			previous = self.CenterPosition;
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			if (changingOwner) return;
			SetPen(self, null); ClearRequest(self);
			foreach (var a in claimants.ToArray()) a.Trait<MedievalHerding>().Cancel(a);
			Escort = Calling = null;
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			if (Pen?.Owner != newOwner) SetPen(self, null);
			if (RequestedPen?.Owner != newOwner) ClearRequest(self);
			if (Escort != null && Escort.Owner != newOwner) Release(self, Escort);
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (self.IsDead || self.World.FogObscures(self)) yield break;
			var sprite = self.World.Map.Sequences.GetSequence(VisualImage, "idle").GetSprite(VisualFrame(self));
			yield return new SpriteRenderable(sprite, self.CenterPosition, WVec.Zero, 0, null, 1, 1, Vector3.One, TintModifiers.None, false);
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			var p = wr.ScreenPxPosition(self.CenterPosition); yield return new Rectangle(p.X - 32, p.Y - 44, 64, 56);
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			var guide = Escort ?? Calling;
			if (guide == null || self.IsDead || self.World.FogObscures(self) || self.World.FogObscures(guide)) yield break;
			var herding = guide.Trait<MedievalHerding>();
			if (Escort == null && (!herding.Working || herding.CaptureFrame < 3)) yield break;
			WVec Pixels(float x, float y, float height) => new((int)(x * wr.TileScale / wr.TileSize.Width),
				(int)(y * wr.TileScale / wr.TileSize.Width), (int)(height * wr.TileScale / wr.TileSize.Height));
			var sheep = info.Image.EndsWith("sheep", StringComparison.Ordinal);
			var nose = sheep ? Adult ? 12 : 9.5f : Adult ? 16 : 13;
			var height = sheep ? Adult ? 11 : 8.5f : Adult ? 16 : 13;
			var angle = facing * MathF.PI / 4;
			var muzzle = self.CenterPosition + Pixels(MathF.Cos(angle) * nose, MathF.Sin(angle) * nose, height);
			var yaw = new WAngle((guide.Trait<Mobile>().Facing.Angle + 64) / 128 * 128);
			var dx = -yaw.Sin() / 1024f; var dy = -yaw.Cos() / 1024f;
			var body = guide.TraitOrDefault<WithMedievalInfantryBody>();
			var step = body?.Frame % 8 ?? 0;
			var catching = herding.Working;
			var reach = catching ? new[] { 3, 4, 6, 8, 9, 9, 8, 6 }[herding.CaptureFrame] : -4;
			var bend = catching ? new[] { 0f, .5f, 1.5f, 2.5f, 3, 3, 2, 1 }[herding.CaptureFrame] : 0;
			var bob = catching ? MathF.Round(bend) : body?.Sequence == "lead" ? MathF.Round(MathF.Abs(MathF.Sin(step * MathF.PI / 4)) * 1.1f) : MathF.Round(MathF.Sin(step * MathF.PI / 4) * .55f);
			var swing = body?.Sequence == "lead" ? MathF.Sin(step * MathF.PI / 4) * 1.2f : 0;
			var hand = guide.CenterPosition + Pixels((catching ? 2 : 5) + dx * reach + MathF.Round(dx * bend * .45f), dy * reach, 10 - bob - swing);
			if (catching) muzzle = hand + (muzzle - hand) * Math.Min(4, herding.CaptureFrame - 2) / 4;
			var distance = (muzzle - hand).HorizontalLength;
			var slack = Math.Clamp(300 - distance / 9, 35, 210);
			var previousPoint = hand;
			for (var i = 1; i <= 8; i++)
			{
				var point = hand + (muzzle - hand) * i / 8 - new WVec(0, 0, slack * i * (8 - i) / 16);
				yield return new LineAnnotationRenderable(previousPoint, point, 1, Color.FromArgb(192, 160, 105));
				previousPoint = point;
			}
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalWildHerdsInfo : TraitInfo
	{
		public readonly bool SpawnAnimals = true;
		public override object Create(ActorInitializer init) => new MedievalWildHerds(this);
	}
	public sealed class MedievalWildHerds : ITick, ISync
	{
		readonly MedievalWildHerdsInfo info;
		[VerifySync] int tick;
		public MedievalWildHerds(MedievalWildHerdsInfo info) { this.info = info; }
		void ITick.Tick(Actor self)
		{
			if (!info.SpawnAnimals || self.World.Type != WorldType.Regular || ++tick != 5) return;
			self.World.AddFrameEndTask(w =>
			{
				var neutral = w.Players.First(p => p.InternalName == "Neutral");
				var anchors = w.Actors.Where(a => !a.Owner.NonCombatant && (a.TraitOrDefault<MedievalFoundingCaravan>() != null
					|| a.TraitOrDefault<MedievalMarket>() != null || a.Info.Name == "md.warehouse")).GroupBy(a => a.Owner)
					.Select(g => g.OrderBy(a => a.ActorID).First()).OrderBy(a => a.ActorID).ToArray();
				bool Free(CPos c) => w.Map.Contains(c) && w.Map.GetTerrainInfo(c).Type is "Clear" or "Rough"
					&& !w.ActorMap.GetActorsAt(c).Any();
				var total = 0;
				foreach (var anchor in anchors)
					foreach (var species in new[] { "md.cow", "md.sheep" })
					{
						var candidates = w.Map.FindTilesInAnnulus(anchor.Location, 10, 22).Where(Free).ToArray();
						if (candidates.Length == 0) continue;
						var center = candidates[w.SharedRandom.Next(candidates.Length)];
						foreach (var cell in w.Map.FindTilesInAnnulus(center, 0, 3).Where(Free).Take(4).ToArray())
						{ w.CreateActor(species, [new OwnerInit(neutral), new LocationInit(cell)]); total++; }
					}
				// A finite, bounded population on huge maps; nothing respawns with the camera.
				var groups = Math.Clamp(w.Map.Bounds.Width * w.Map.Bounds.Height / 12000, 2, 72);
				for (var i = 0; i < groups * 8 && total < groups * 4 + anchors.Length * 8; i++)
				{
					var uv = new MPos(w.SharedRandom.Next(w.Map.Bounds.Left, w.Map.Bounds.Right), w.SharedRandom.Next(w.Map.Bounds.Top, w.Map.Bounds.Bottom));
					var center = uv.ToCPos(w.Map);
					if (!Free(center)) continue;
					var species = i % 2 == 0 ? "md.cow" : "md.sheep";
					foreach (var cell in w.Map.FindTilesInAnnulus(center, 0, 2).Where(Free).Take(4).ToArray())
					{ w.CreateActor(species, [new OwnerInit(neutral), new LocationInit(cell)]); total++; }
				}
			});
		}
	}
}
