using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed class SoldierFatigue
	{
		public int Value { get; private set; }
		public int Speed => 100 - Value * 35 / 10000;
		public int Firepower => 100 - Value * 25 / 10000;
		// Once per second, fixed-point values are independent of render rate and local weather particles.
		public void Advance(int distance, bool night, int rain, bool sheltered, bool cold = false)
		{
			var delta = sheltered ? -400 : Math.Max(0, distance) * 100 / 6144 + (night ? 25 : 0) + Math.Clamp(rain, 0, 1000) * 35 / 1000
				+ (!sheltered && cold ? 30 : 0) - (distance == 0 && !night && rain == 0 && !cold ? 35 : 0);
			Value = Math.Clamp(Value + delta, 0, 10000);
		}
	}
	public class MedievalShelterInfo : TraitInfo
	{
		public readonly int Beds = 8;
		public readonly bool Military = false;
		public override object Create(ActorInitializer init) => new MedievalShelter(this);
	}
	public sealed class MedievalShelter : IResolveOrder
	{
		public readonly MedievalShelterInfo Info;
		public MedievalShelter(MedievalShelterInfo info) { Info = info; }
		public int Occupants(Actor self) => self.World.ActorsHavingTrait<MedievalRest>().Count(a => MedievalStockpile.Active(a) && a.Trait<MedievalRest>().Shelter == self);
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "MedievalWakeShelter" || Info.Military) return;
			foreach (var a in self.World.ActorsHavingTrait<MedievalRest>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a.Trait<MedievalRest>().Shelter == self))
				a.Trait<MedievalRest>().Wake(a, true, true);
		}
	}
	public class MedievalRestInfo : TraitInfo, Requires<MobileInfo>
	{
		[GrantedConditionReference] public readonly string SleepingCondition = "sleeping";
		public override object Create(ActorInitializer init) => new MedievalRest(init.Self, this);
	}
	public sealed class MedievalRest : ITick, IResolveOrder, ISpeedModifier, IFirepowerModifier, IRenderModifier, ISync
	{
		readonly MedievalRestInfo info;
		readonly SoldierFatigue fatigue = new();
		WPos lastPosition;
		[VerifySync] int distance, ticks, condition = Actor.InvalidConditionToken, retry, manualUntil;
		[VerifySync] public bool Sleeping { get; private set; }
		[VerifySync] public bool OrderedMilitaryRest { get; private set; }
		[VerifySync] public bool StormSheltering { get; private set; }
		[VerifySync] int stormManualUntil, stormFailedMoves, avoidUntil;
		[VerifySync] bool stormHeard;
		Actor home, avoidedHome;
		[VerifySync] public int HomeId => (int)(home?.ActorID ?? 0);
		[VerifySync] int AvoidedHomeId => (int)(avoidedHome?.ActorID ?? 0);
		public Actor Shelter { get; private set; }
		[VerifySync] public int ShelterId => (int)(Shelter?.ActorID ?? 0);
		[VerifySync] public int Fatigue => fatigue.Value;
		public bool Resting => Shelter != null;
		public bool RunningForShelter => StormSheltering && Resting && !Sleeping;
		public string Description => StormSheltering ? Sleeping ? "À l’abri de l’orage" : "Court se mettre à l’abri"
			: Sleeping ? "Dort a l'abri" : Resting ? "Rejoint son lit" : $"Fatigue : {Fatigue / 100}%";
		public MedievalRest(Actor self, MedievalRestInfo info) { this.info = info; lastPosition = self.CenterPosition; }
		public bool Wake(Actor self, bool manual = false, bool stormOverride = false)
		{
			if (Sleeping)
			{
				var mobile = self.Trait<Mobile>();
				var exits = (Shelter?.IsInWorld != true ? Enumerable.Empty<CPos>() : MedievalAccess.Perimeter(Shelter))
					.Concat(self.World.Map.FindTilesInAnnulus(self.Location, 1, 8));
				var cell = exits.Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.All)).Select(c => (CPos?)c).FirstOrDefault();
				if (cell == null) return false;
				mobile.SetPosition(self, cell.Value); lastPosition = self.CenterPosition;
				condition = self.RevokeCondition(condition); Sleeping = false;
			}
			Shelter = null; OrderedMilitaryRest = false; StormSheltering = false; stormFailedMoves = 0; retry = 50;
			if (manual) manualUntil = self.World.WorldTick + 750;
			if (stormOverride) stormManualUntil = self.World.WorldTick + 750;
			return true;
		}
		bool Seek(Actor self, bool military, bool nearby)
		{
			var occupied = self.World.ActorsHavingTrait<MedievalRest>().Where(MedievalStockpile.Active)
				.Select(a => a.Trait<MedievalRest>().Shelter).Where(a => a != null).GroupBy(a => a).ToDictionary(g => g.Key, g => g.Count());
			var chosen = self.World.ActorsHavingTrait<MedievalShelter>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a)
				&& a.Trait<MedievalShelter>().Info.Military == military && occupied.GetValueOrDefault(a) < a.Trait<MedievalShelter>().Info.Beds
				&& (!military || MedievalCampArea.Accepts(a, self))
				&& (!nearby || (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared < 8192L * 8192))
				.OrderBy(a => a == home ? 0 : 1).ThenBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared + (a.Info.Name == "md.house" ? 0 : 8192L * 8192)).ThenBy(a => a.ActorID)
				.FirstOrDefault();
			if (chosen == null) return false;
			Shelter = chosen; if (chosen.Info.Name == "md.house") home = chosen;
			self.TraitOrDefault<MedievalUndertaker>()?.Cancel(self, false);
			self.CancelActivity(); self.TraitOrDefault<MedievalWorker>()?.PauseForSleep(); return true;
		}
		bool SeekStormHome(Actor self, MedievalWeather weather)
		{
			var chosen = weather.StormHomes.Reserve(self, home, self.World.WorldTick < avoidUntil ? avoidedHome : null);
			if (chosen == null) return false;
			// Keep an interrupted animal transfer queued, without dragging the animal
			// into the house or keeping its guide/pen reservation twice.
			var guide = self.TraitOrDefault<MedievalHerding>();
			if (guide?.Busy == true)
			{
				var animal = guide.Animal; var pen = guide.Destination; guide.Cancel(self);
				if (MedievalStockpile.Active(animal) && MedievalStockpile.Active(pen) && pen.TraitOrDefault<MedievalLivestock>() != null)
					animal.Trait<MedievalAnimal>().Request(animal, pen, self.Owner);
			}
			Shelter = home = chosen; StormSheltering = true; stormFailedMoves = 0;
			self.TraitOrDefault<MedievalUndertaker>()?.Cancel(self, false);
			self.CancelActivity(); self.TraitOrDefault<MedievalWorker>()?.PauseForStorm(); return true;
		}
		public bool CampRest(Actor self)
		{
			if (self.TraitOrDefault<MedievalPatient>()?.Church != null) return false;
			if (!self.IsInWorld || self.TraitOrDefault<Passenger>()?.ReservedCargo != null) return false;
			if (self.TraitOrDefault<MedievalFirefighter>()?.Busy == true) return false;
			if (self.TraitOrDefault<MedievalMorale>() == null || self.TraitOrDefault<MedievalCommander>()?.Busy == true) return false;
			if (Resting) { OrderedMilitaryRest = true; return true; }
			self.TraitOrDefault<MedievalHerding>()?.Cancel(self);
			self.TraitOrDefault<MedievalSaboteur>()?.Cancel(self);
			self.TraitOrDefault<MedievalSiegeCrew>()?.Release(self);
			var found = Seek(self, true, true);
			if (found) OrderedMilitaryRest = true;
			return found;
		}
		void ITick.Tick(Actor self)
		{
			var step = (self.CenterPosition - lastPosition).HorizontalLength;
			if (step < 1024 && !Sleeping) distance += step;
			lastPosition = self.CenterPosition;
			var clock = self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock;
			var night = clock?.Night == true;
			var soldier = self.TraitOrDefault<MedievalMorale>() != null;
			var worker = self.TraitOrDefault<MedievalWorker>();
			var weather = self.World.WorldActor.TraitOrDefault<MedievalWeather>();
			var civilian = worker != null || self.TraitOrDefault<MedievalPriest>() != null || self.TraitOrDefault<MedievalUndertaker>() != null;
			var storm = civilian && !soldier && weather?.Storm.ShelterTicks > 0 && self.World.WorldTick >= stormManualUntil;
			if (storm && !stormHeard) { retry = (int)(self.ActorID % 13); stormHeard = true; }
			else if (!storm) stormHeard = false;
			if (++ticks >= 25)
			{
				if (soldier) fatigue.Advance(distance, night, self.World.WorldActor.TraitOrDefault<MedievalWeather>()?.RainPermille ?? 0, Sleeping,
					self.World.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Season == VillageSeason.Winter);
				distance = 0; ticks = 0;
			}
			if (!self.IsInWorld || self.TraitOrDefault<MedievalPatient>()?.Church != null || self.TraitOrDefault<MedievalFirefighter>()?.Busy == true) return;
			if (Shelter != null && (!MedievalStockpile.Active(Shelter) || Shelter.Owner != self.Owner
				|| StormSheltering && Shelter.TraitOrDefault<MedievalBurnable>()?.Burning == true)) { self.CancelActivity(); Wake(self); return; }
			if (Resting && (!soldier && !night && !storm || soldier && Shelter.TraitOrDefault<MedievalCampArea>()?.RestEnabled != true)) { self.CancelActivity(); Wake(self); return; }
			if (Resting && !soldier) StormSheltering = storm;
			if (Sleeping) return;
			if (--retry > 0) return;
			retry = 25;
			if (Shelter == null)
			{
				if (storm) SeekStormHome(self, weather);
				else if (self.World.WorldTick < manualUntil) return;
				else if (civilian && night && self.TraitOrDefault<MedievalHerding>()?.Busy != true) Seek(self, false, false);
				else if (soldier && self.IsIdle && self.TraitOrDefault<MedievalSaboteur>()?.Busy != true && self.TraitOrDefault<MedievalHerding>()?.Busy != true && self.TraitOrDefault<MedievalPatrol>()?.Active != true && self.TraitOrDefault<MedievalSiegeCrew>()?.Assigned != true && self.TraitOrDefault<MedievalCommander>()?.Busy != true) Seek(self, true, true);
			}
			if (Shelter == null || !self.IsIdle) return;
			var mobile = self.Trait<Mobile>();
			if (MedievalAccess.Adjacent(self, Shelter))
			{
				Sleeping = true; condition = self.GrantCondition(info.SleepingCondition);
				mobile.SetPosition(self, Shelter.Location); lastPosition = self.CenterPosition; return;
			}
			if (StormSheltering && ++stormFailedMoves > 3)
			{
				avoidedHome = Shelter; avoidUntil = self.World.WorldTick + 375;
				Wake(self); return;
			}
			var entry = MedievalAccess.Perimeter(Shelter).Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.All))
				.OrderBy(c => (c - self.Location).LengthSquared).Select(c => (CPos?)c).FirstOrDefault();
			if (entry != null) self.QueueActivity(mobile.MoveTo(entry.Value, 0));
			else { avoidedHome = Shelter; avoidUntil = self.World.WorldTick + 375; Shelter = null; OrderedMilitaryRest = StormSheltering = false; retry = 125; }
		}
		public void ResumeAfterEmergency(Actor self, Actor home, bool ordered)
		{
			if (!MedievalStockpile.Active(home) || home.Owner != self.Owner) return;
			var military = self.TraitOrDefault<MedievalMorale>() != null;
			var weather = self.World.WorldActor.TraitOrDefault<MedievalWeather>();
			if (!military && self.TraitOrDefault<MedievalWorker>() != null && weather?.Storm.ShelterTicks > 0 && self.World.WorldTick >= stormManualUntil)
			{ SeekStormHome(self, weather); return; }
			if (military ? home.TraitOrDefault<MedievalCampArea>()?.RestEnabled != true : self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.Night != true) return;
			Shelter = home; OrderedMilitaryRest = ordered; retry = 0;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalRest" && self.TraitOrDefault<MedievalWorker>() != null)
			{ if (Wake(self)) Seek(self, false, false); }
			else if (order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "CaptureActor" or "MedievalWork" or "MedievalAssignSiege" or "MedievalGatherAnimal" or "EnterTransport")
			{ if (!Wake(self)) self.CancelActivity(); manualUntil = stormManualUntil = self.World.WorldTick + 750; }
		}
		int ISpeedModifier.GetSpeedModifier() => RunningForShelter ? fatigue.Speed * 165 / 100 : fatigue.Speed;
		int IFirepowerModifier.GetFirepowerModifier() => fatigue.Firepower;
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables) => Sleeping ? [] : renderables;
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => Sleeping ? [] : bounds;
	}
}
