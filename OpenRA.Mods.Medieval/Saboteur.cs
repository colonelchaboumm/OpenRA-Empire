using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class SabotageRules
	{
		public const int IgniteTicks = 250, SlaughterTicks = 200, BurnTicks = 750;
		// Booked shipments keep their existing contract. Only physically present,
		// uncommitted goods burn; incoming deliveries are never subtracted remotely.
		public static int BurnStock(Inventory stock) => stock == null ? 0 : stock.Take(0, 2) + stock.Take(1, 2) + stock.Take(2, 2);
	}
	public class MedievalBurnableInfo : TraitInfo, Requires<HealthInfo>, Requires<BuildingInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalBurnable();
	}
	public sealed class MedievalBurnable : ITick, IRender, INotifyOwnerChanged, ISync
	{
		[VerifySync] public int Remaining { get; private set; }
		[VerifySync] public int LostGoods { get; private set; }
		[VerifySync] int cadence;
		[VerifySync] bool yardOnly;
		[VerifySync] WPos origin;
		Actor attacker;
		[VerifySync] public int AttackerId => (int)(attacker?.ActorID ?? 0);
		public bool Burning => Remaining > 0;
		public WPos Origin => origin;
		public bool YardOnly => yardOnly;
		public FireSmoke Smoke { get; } = new();
		[VerifySync] public int WaterBuckets { get; private set; }
		public void Douse(Actor self)
		{
			if (!Burning) return;
			Remaining = FirefightingRules.Douse(Remaining); WaterBuckets++;
			if (!Burning) self.World.ScreenMap.AddOrUpdate(self);
		}
		public void Ignite(Actor self, Actor source, CPos cell, bool yard)
		{
			// Repeated torches refresh a single fire instead of multiplying its damage.
			if (!Burning) { cadence = 25; yardOnly = yard; origin = yard ? self.World.Map.CenterOfCell(cell) : self.CenterPosition; }
			else if (!yard) { yardOnly = false; origin = self.CenterPosition; }
			Remaining = SabotageRules.BurnTicks; attacker = source;
			self.World.ScreenMap.AddOrUpdate(self);
			MedievalAudio.At(self.World, "fire", origin);
		}
		void ITick.Tick(Actor self)
		{
			Smoke.Tick(self.World.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene.Wind ?? new Vector2(.45f, .1f),
				Burning && MedievalStockpile.Active(self), yardOnly ? 18 : 52, self.ActorID);
			if (!Burning || !MedievalStockpile.Active(self)) return;
			Remaining--;
			if (Remaining == 0) self.World.ScreenMap.AddOrUpdate(self);
			if (--cadence > 0) return;
			cadence = 25;
			if (yardOnly) LostGoods += self.TraitOrDefault<MedievalStockyard>()?.BurnAt(self, self.World.Map.CellContaining(origin)) ?? 0;
			else
			{
				LostGoods += SabotageRules.BurnStock(self.TraitOrDefault<MedievalStockpile>()?.Stock);
				if (self.TraitOrDefault<MedievalCampStore>() is { } camp) LostGoods += camp.Stock.Take(0, 2) + camp.Stock.Take(1, 1) + camp.Stock.Take(MedievalCampStore.Cloth, 2);
				// Stone, steel weapons and armor do not burn. Bows do.
				if (self.TraitOrDefault<MedievalEquipmentStore>() is { } equipment) LostGoods += equipment.Stock.Take(0, 1);
			}
			if (Remaining % 100 == 0) MedievalAudio.At(self.World, "fire", origin);
			if (!yardOnly)
			{
				var health = self.Trait<Health>();
				health.InflictDamage(self, attacker, new Damage(Math.Max(1, health.MaxHP / 50)), false);
			}
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Remaining = 0; attacker = null; }
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if ((!Burning && Smoke.Clouds.Count == 0) || !MedievalStockpile.Active(self) || self.World.FogObscures(origin)) yield break;
			yield return new FirePlumeRenderable(origin, Smoke, Burning, yardOnly, Remaining, self.World.WorldTick, self.ActorID);
			yield return new MedievalGroundLayer(new SmokeShadowRenderable(Origin, Smoke, self.World.WorldActor.TraitOrDefault<MedievalSunlight>()?.Shadow ?? default), 2);
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			if (!Burning && Smoke.Clouds.Count == 0) yield break;
			var p = wr.ScreenPxPosition(origin); yield return new Rectangle(p.X - 200, p.Y - 280, 400, 320);
		}
	}

	public class MedievalSaboteurInfo : TraitInfo, Requires<MobileInfo>, Requires<CloakInfo>, Requires<MedievalHerdingInfo>
	{
		[GrantedConditionReference] public readonly string BusyCondition = "sabotaging";
		public override object Create(ActorInitializer init) => new MedievalSaboteur(this);
	}
	public sealed class MedievalSaboteur : ITick, IIssueOrder, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		readonly MedievalSaboteurInfo info;
		public Actor Victim { get; private set; }
		[VerifySync] public int VictimId => (int)(Victim?.ActorID ?? 0);
		[VerifySync] public int Progress { get; private set; }
		[VerifySync] public bool Working { get; private set; }
		[VerifySync] public bool Slaughter { get; private set; }
		[VerifySync] CPos targetCell;
		[VerifySync] bool yard;
		[VerifySync] int condition = Actor.InvalidConditionToken, cadence, stalled, attempts;
		[VerifySync] WPos previous;
		public bool Busy => Victim != null;
		public int Percent => Math.Clamp(Progress / (Slaughter ? SabotageRules.SlaughterTicks : SabotageRules.IgniteTicks), 0, 100);
		public string Status { get; private set; } = "Discret en deplacement. Choisissez une action.";
		public MedievalSaboteur(MedievalSaboteurInfo info) { this.info = info; }
		public static bool Hostile(Actor self, Actor target) => target != null && target.Owner != self.Owner && self.Owner.RelationshipWith(target.Owner) == PlayerRelationship.Enemy;
		public static bool CanTarget(Actor self, Actor target, bool slaughter) => MedievalStockpile.Active(self) && MedievalStockpile.Active(target)
			&& (Hostile(self, target) || MedievalDiplomacy.Nation(target.Owner) && target.Owner != self.Owner
				&& self.Owner.RelationshipWith(target.Owner) == PlayerRelationship.Neutral) && target.CanBeViewedByPlayer(self.Owner)
			&& (slaughter ? target.TraitOrDefault<MedievalAnimal>() != null : target.TraitOrDefault<MedievalBurnable>() != null);
		public static bool YardAt(Actor target, CPos cell) => target is { Disposed: false } && target.TraitOrDefault<MedievalStockyard>()?.At(cell) != null;
		public bool Start(Actor self, Actor target, bool slaughter, CPos cell, bool stockyard = false)
		{
			if (!CanTarget(self, target, slaughter) || stockyard && (!YardAt(target, cell) || !self.Owner.Shroud.IsVisible(cell))) return false;
			if (self.TraitOrDefault<MedievalRest>() is { } rest && !rest.Wake(self, true)) return false;
			self.World.WorldActor.Trait<MedievalDiplomacy>().DeclareWar(self.Owner, target.Owner);
			self.TraitOrDefault<MedievalPatient>()?.Cancel(self);
			Cancel(self);
			self.TraitOrDefault<MedievalHerding>()?.Cancel(self);
			self.TraitOrDefault<MedievalSiegeCrew>()?.Release(self);
			self.CancelActivity(); Victim = target; Slaughter = slaughter; targetCell = cell; yard = stockyard;
			condition = self.GrantCondition(info.BusyCondition); previous = self.CenterPosition;
			Status = slaughter ? "Approche de l'animal" : "Approche de la cible a incendier"; return true;
		}
		public void Cancel(Actor self, bool cancelActivity = true)
		{
			if (Victim is { Disposed: false } && Victim.TraitOrDefault<MedievalAnimal>() is { } beast && beast.Calling == self) beast.Calling = null;
			if (Busy && cancelActivity) self.CancelActivity();
			Victim = null; Progress = cadence = stalled = attempts = 0; Working = false;
			if (condition != Actor.InvalidConditionToken) condition = self.RevokeCondition(condition);
		}
		bool Reach(Actor self)
		{
			var mobile = self.Trait<Mobile>();
			if (Slaughter)
			{
				var beast = Victim.Trait<MedievalAnimal>();
				if (beast.Pen is { } pen)
				{
					var herd = pen.Trait<MedievalLivestock>();
					if (self.Location != herd.Outside)
					{
						if (self.IsIdle) { attempts++; self.QueueActivity(mobile.MoveTo(herd.Outside)); }
						return false;
					}
					if (beast.Calling != null && beast.Calling != self) return false;
					beast.Calling = self;
				}
				if ((Victim.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= 1400L * 1400) return true;
				if (self.IsIdle) { attempts++; self.QueueActivity(mobile.MoveWithinRange(Target.FromActor(Victim), new WDist(1100))); }
				return false;
			}
			if (yard)
			{
				if ((self.Location - targetCell).LengthSquared <= 1) return true;
				if (self.IsIdle) { attempts++; self.QueueActivity(mobile.MoveTo(targetCell, 1)); }
				return false;
			}
			return MedievalAccess.Adjacent(self, Victim) || self.IsIdle && MedievalLogistics.Approach(self, Victim, ref attempts);
		}
		void ITick.Tick(Actor self)
		{
			var herding = self.Trait<MedievalHerding>();
			if (herding.Working || herding.Leading) self.Trait<Cloak>().Uncloak();
			if (!Busy) return;
			if (!CanTarget(self, Victim, Slaughter) || yard && !YardAt(Victim, targetCell) || self.TraitOrDefault<MedievalRest>()?.Resting == true)
			{ Cancel(self); Status = "Action annulee : cible perdue ou indisponible"; return; }
			if (self.CenterPosition == previous) stalled++; else { stalled = attempts = 0; previous = self.CenterPosition; }
			if (stalled > 1000 || attempts > 15) { Cancel(self); Status = "Cible inaccessible : liberez le passage"; return; }
			if (Working) self.Trait<Cloak>().Uncloak();
			if (--cadence > 0) return; cadence = 5;
			if (!Reach(self)) { Progress = 0; Working = false; return; }
			self.CancelActivity(); Working = true; self.Trait<Cloak>().Uncloak();
			var at = yard ? self.World.Map.CenterOfCell(targetCell) : Victim.CenterPosition;
			if ((at - self.CenterPosition).HorizontalLengthSquared > 0) self.Trait<Mobile>().Facing = (at - self.CenterPosition).Yaw;
			Progress += 5 * (self.TraitOrDefault<MedievalNeedsFood>()?.WorkPercent ?? 100);
			Status = (Slaughter ? "Abattage de l'animal" : "Preparation de l'incendie") + $" : {Percent}%";
			if (Percent < 100) return;
			if (Slaughter) Victim.Kill(self);
			else Victim.Trait<MedievalBurnable>().Ignite(Victim, self, targetCell, yard);
			var killed = Slaughter; Cancel(self); Status = killed ? "Animal abattu" : "Incendie allume : repliez-vous";
		}
		IEnumerable<IOrderTargeter> IIssueOrder.Orders { get { yield return new FireTargeter(); } }
		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) => order.OrderID == "MedievalSabotageFire" ? new Order(order.OrderID, self, target, false) : null;
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString is "MedievalSabotageFire" or "MedievalSabotageKill")
			{
				if (order.Target.Type == TargetType.Actor) Start(self, order.Target.Actor, order.OrderString == "MedievalSabotageKill", order.ExtraLocation, order.ExtraData == 1);
			}
			else if (order.OrderString is "MedievalGatherAnimal" or "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "MedievalAssignSiege" or "MedievalCommanderRest")
			// Mobile/Attack/Herding already replaced the activity for this order.
			// Release the sabotage without cancelling the newly requested retreat.
			{ Cancel(self, false); Status = "Action interrompue"; }
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Cancel(self); }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Cancel(self); }
		sealed class FireTargeter : UnitOrderTargeter
		{
			public FireTargeter() : base("MedievalSabotageFire", 7, "attack", true, false) { ForceAttack = false; }
			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor) => !modifiers.HasModifier(TargetModifiers.ForceMove) && MedievalSaboteur.CanTarget(self, target, false);
			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor) => false;
		}
	}
}
