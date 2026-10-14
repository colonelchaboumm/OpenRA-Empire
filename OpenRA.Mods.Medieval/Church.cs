using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalChurchInfo : TraitInfo, Requires<MedievalBuildingInfo>
	{
		public readonly int MaxPriests = 3;
		public readonly int MaxUndertakers = 3;
		public readonly int TrainingTicks = 150;
		public override object Create(ActorInitializer init) => new MedievalChurch(this);
	}
	public sealed class MedievalChurch : ITick, IResolveOrder, INotifyOwnerChanged, ISync
	{
		public readonly MedievalChurchInfo Info;
		[VerifySync] public int Training { get; private set; }
		[VerifySync] public bool TrainingUndertaker { get; private set; }
		public bool Recruiting => Training > 0;
		public MedievalChurch(MedievalChurchInfo info) { Info = info; }
		public static IEnumerable<Actor> Churches(Player p) => p.World.ActorsHavingTrait<MedievalChurch>().Where(a => a.Owner == p && MedievalStockpile.Active(a));
		public IEnumerable<Actor> Priests(Actor self) => self.World.ActorsHavingTrait<MedievalPriest>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a.Trait<MedievalPriest>().Church == self);
		public IEnumerable<Actor> Undertakers(Actor self) => self.World.ActorsHavingTrait<MedievalUndertaker>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a.Trait<MedievalUndertaker>().Church == self);
		public IEnumerable<Actor> OnDutyPriests(Actor self) => Priests(self).Where(a => a.Trait<MedievalPriest>().Available(a) && MedievalConstructionSupply.Within(a.CenterPosition, self.CenterPosition, new WDist(4096)));
		public int OnDuty(Actor self) => OnDutyPriests(self).Count();
		public bool CanRecruit(Actor self, bool undertaker = false) => MedievalStockpile.Active(self) && !Recruiting
			&& (undertaker ? Undertakers(self).Count() < Info.MaxUndertakers : Priests(self).Count() < Info.MaxPriests)
			&& MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) < MedievalSettlement.Housing(self.Owner)
			&& MedievalSettlement.Cost(self.Owner, undertaker ? "md.undertaker" : "md.priest").CanPay(MedievalSettlement.RecruitmentStores(self.Owner).Select(a => a.Trait<MedievalStockpile>().Stock));
		public string Status(Actor self) => $"Pretres : {Priests(self).Count()}/{Info.MaxPriests} | Sur place : {OnDuty(self)} | Blesses : {self.World.ActorsHavingTrait<MedievalPatient>().Count(a => !a.IsDead && a.Trait<MedievalPatient>().Church == self)}";
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalPlaceCemetery" && order.Target.Type == TargetType.Terrain)
			{
				self.World.WorldActor.Trait<MedievalCemeteries>().Place(self, AgriculturalZone.Between(self.World.Map.CellContaining(order.Target.CenterPosition), order.ExtraLocation)); return;
			}
			if (order.OrderString is not ("MedievalRecruitPriest" or "MedievalRecruitUndertaker")) return;
			var undertaker = order.OrderString == "MedievalRecruitUndertaker";
			if (!CanRecruit(self, undertaker)) return;
			if (MedievalSettlement.Cost(self.Owner, undertaker ? "md.undertaker" : "md.priest").TryPay(MedievalSettlement.RecruitmentStores(self.Owner).Select(a => a.Trait<MedievalStockpile>().Stock)))
			{ TrainingUndertaker = undertaker; Training = Info.TrainingTicks; }
		}
		void ITick.Tick(Actor self)
		{
			if (!Recruiting || !MedievalStockpile.Active(self) || --Training > 0) return;
			Training = 1;
			if (MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) > MedievalSettlement.Housing(self.Owner)) return;
			var cell = MedievalAccess.Perimeter(self).Where(c => self.World.Map.Contains(c) && !self.World.ActorMap.GetActorsAt(c).Any()
				&& new[] { "Clear", "Road", "Rough" }.Contains(self.World.Map.GetTerrainInfo(c).Type)).OrderBy(c => c.Y).ThenBy(c => c.X).Cast<CPos?>().FirstOrDefault();
			if (cell == null) return;
			Training = 25; var owner = self.Owner;
			self.World.AddFrameEndTask(w =>
			{
				if (!Recruiting || !MedievalStockpile.Active(self) || self.WillDispose || self.Owner != owner || w.ActorMap.GetActorsAt(cell.Value).Any()
					|| MedievalSettlement.Population(owner) + MedievalSettlement.PendingPopulation(owner) > MedievalSettlement.Housing(owner)) return;
				var person = w.CreateActor(TrainingUndertaker ? "md.undertaker" : "md.priest", [new OwnerInit(owner), new LocationInit(cell.Value)]);
				if (TrainingUndertaker) person.Trait<MedievalUndertaker>().Assign(person, self);
				else person.Trait<MedievalPriest>().Assign(person, self);
				Training = 0; TrainingUndertaker = false;
				self.TraitOrDefault<MedievalChurchAnimation>()?.Bell.Ring(2);
			});
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Training = 0; TrainingUndertaker = false; }
		public static void Bind(Widget widget, World world, WorldRenderer renderer)
		{
			Actor Selected() => world.Selection.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalChurch>() != null);
			widget.Get("MD_CHURCH_PANEL").IsVisible = () => Selected() != null;
			widget.Get<LabelWidget>("MD_CHURCH_TITLE").GetText = () => "EGLISE - SOINS ET CIMETIERES";
			widget.Get<LabelWidget>("MD_CHURCH_STATUS").GetText = () => Selected() is Actor a ? a.Trait<MedievalChurch>().Status(a) : "";
			widget.Get<LabelWidget>("MD_CHURCH_HINT").GetText = () => "Blesses selectionnes : clic droit sur l'eglise.\nUn pretre sur place soigne 3 personnes a la fois.\nSoins : 5% de sante par seconde, sans depasser 100%.\nPretre proche : +10 moral dans un rayon de 5 cases.\nLes pretres ont besoin de nourriture et de logement.";
			var recruit = widget.Get<ButtonWidget>("MD_CHURCH_RECRUIT");
			recruit.GetText = () => Selected()?.Trait<MedievalChurch>().Recruiting == true ? "Recrutement en cours..." : "Recruter un pretre | 30 nourriture";
			recruit.IsDisabled = () => world.Paused || Selected() is not Actor a || !a.Trait<MedievalChurch>().CanRecruit(a);
			recruit.OnClick = () => { if (Selected() is Actor a) world.IssueOrder(new Order("MedievalRecruitPriest", a, false)); };
			var undertaker = widget.Get<ButtonWidget>("MD_CHURCH_UNDERTAKER");
			undertaker.GetText = () => Selected()?.Trait<MedievalChurch>().Recruiting == true ? "Recrutement en cours..." : "Recruter un croque-mort | 30 nourriture";
			undertaker.IsDisabled = () => world.Paused || Selected() is not Actor a || !a.Trait<MedievalChurch>().CanRecruit(a, true);
			undertaker.OnClick = () => { if (Selected() is Actor a) world.IssueOrder(new Order("MedievalRecruitUndertaker", a, false)); };
			var zone = widget.Get<ButtonWidget>("MD_CHURCH_CEMETERY");
			zone.GetText = () => "Zoner un cimetiere | gratuit";
			zone.IsDisabled = () => world.Paused || Selected() == null;
			zone.OnClick = () => { if (Selected() is Actor a) world.OrderGenerator = new MedievalCemeteryOrderGenerator(world, renderer, a); };
			widget.Get<LabelWidget>("MD_CHURCH_BURIALS").GetText = () =>
			{
				if (Selected() is not Actor a) return "";
				var plots = world.WorldActor.Trait<MedievalCemeteries>().Plots.Where(p => p.Owner == a.Owner).ToArray();
				return $"Croque-morts : {a.Trait<MedievalChurch>().Undertakers(a).Count()}/{a.Trait<MedievalChurch>().Info.MaxUndertakers} | Tombes : {plots.Count(p => p.Buried)}/{plots.Length}";
			};
			widget.Get<LabelWidget>("MD_CHURCH_CEMETERY_HINT").GetText = () => "Deux coins, de 2 a 10 cases par cote. Une stele par corps.\nRamassage automatique des corps visibles, un par un.";
		}
	}
	public class MedievalPriestInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalPriest();
	}
	public sealed class MedievalPriest : ITick, ISync, IRenderAnnotations, IResolveOrder
	{
		public Actor Church { get; private set; }
		[VerifySync] public int ChurchId => (int)(Church?.ActorID ?? 0);
		[VerifySync] int cadence;
		[VerifySync] bool returning;
		// Presentation only: refreshed by an actual health gain, never used to decide care.
		public int HealingTicks { get; private set; }
		public bool Healing(Actor self) => HealingTicks > 0 && Available(self) && !self.Trait<MedievalRest>().Resting
			&& MedievalStockpile.Active(Church) && Church.Owner == self.Owner
			&& MedievalConstructionSupply.Within(self.CenterPosition, Church.CenterPosition, new WDist(4096));
		public void ShowHealing() { HealingTicks = 30; }
		public void Assign(Actor self, Actor church) { if (church.Owner == self.Owner && MedievalStockpile.Active(church)) Church = church; }
		public bool Available(Actor self) => MedievalStockpile.Active(self) && self.Trait<MedievalRest>().Sleeping == false
			&& !self.Trait<MedievalNeedsFood>().Need.Hungry && self.Trait<MedievalPatient>().Church == null;
		public int Bonus(Actor self, WPos position) => Available(self) && MedievalConstructionSupply.Within(self.CenterPosition, position, new WDist(5120)) ? 10 : 0;
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || self.World.FogObscures(self)) yield break;
			foreach (var line in MedievalFoodProvider.Ring(self.CenterPosition, new WDist(5120), Available(self) ? Color.Gold : Color.Gray)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString is "Move" or "Stop" or "Scatter" or "MedievalChurchCare") { returning = false; HealingTicks = 0; }
		}
		void ITick.Tick(Actor self)
		{
			if (HealingTicks > 0) HealingTicks--;
			if (--cadence > 0 || !MedievalStockpile.Active(self)) return; cadence = 50;
			if (self.Trait<MedievalRest>().Resting) { returning = true; return; }
			if (MedievalStockpile.Active(Church) && Church.Owner == self.Owner)
			{
				if (returning && self.IsIdle && self.Trait<MedievalPatient>().Church == null)
				{
					returning = false;
					self.QueueActivity(self.Trait<Mobile>().MoveWithinRange(Target.FromActor(Church), new WDist(2500)));
				}
				return;
			}
			Church = MedievalChurch.Churches(self.Owner).Where(a => a.Trait<MedievalChurch>().Priests(a).Count() < a.Trait<MedievalChurch>().Info.MaxPriests)
				.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
		}
	}
	public class MedievalPatientInfo : TraitInfo, Requires<MobileInfo>, Requires<HealthInfo>
	{
		[GrantedConditionReference] public readonly string Condition = null;
		public override object Create(ActorInitializer init) => new MedievalPatient(this);
	}
	public sealed class MedievalPatient : ITick, IIssueOrder, IResolveOrder, INotifyRemovedFromWorld, IRenderModifier, ISync
	{
		readonly MedievalPatientInfo info;
		[VerifySync] int token = Actor.InvalidConditionToken, cadence, attempts;
		public Actor Church { get; private set; }
		[VerifySync] public int ChurchId => (int)(Church?.ActorID ?? 0);
		[VerifySync] public int Healed { get; private set; }
		public int HealPulse { get; private set; }
		public MedievalPatient(MedievalPatientInfo info) { this.info = info; }
		bool CanAttend(Actor self, Actor church) => self.IsInWorld && !self.IsDead && self.TraitOrDefault<MedievalSiegeEngine>() == null
			&& self.TraitOrDefault<MedievalFoundingCaravan>() == null && church.Owner == self.Owner && MedievalStockpile.Active(church)
			&& church.TraitOrDefault<MedievalChurch>() != null && self.Trait<Health>().HP < self.Trait<Health>().MaxHP;
		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get { yield return new ChurchTargeter(this); }
		}
		sealed class ChurchTargeter : UnitOrderTargeter
		{
			readonly MedievalPatient patient;
			public ChurchTargeter(MedievalPatient patient) : base("MedievalChurchCare", 7, "enter", false, true) { this.patient = patient; }
			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor) => patient.CanAttend(self, target);
			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor) => false;
		}
		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) => new("MedievalChurchCare", self, target, false);
		public void Cancel(Actor self, bool cancelActivity = false)
		{
			if (Church == null) return;
			Church = null; attempts = 0;
			if (token != Actor.InvalidConditionToken) token = self.RevokeCondition(token);
			if (cancelActivity) self.CancelActivity();
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalChurchCare" && order.Target.Type == TargetType.Actor && CanAttend(self, order.Target.Actor))
			{
				if (!self.Trait<MedievalRest>().Wake(self, true, true)) return;
				self.TraitOrDefault<MedievalWorker>()?.PauseForEmergency(); self.TraitOrDefault<MedievalSiegeCrew>()?.Release(self);
				self.TraitOrDefault<MedievalHerding>()?.Cancel(self); self.TraitOrDefault<MedievalPatrol>()?.Stop(self, false);
				self.TraitOrDefault<MedievalSaboteur>()?.Cancel(self);
				self.TraitOrDefault<MedievalCommander>()?.Cancel(self);
				self.TraitOrDefault<MedievalFirefighter>()?.Cancel(self, false);
				Cancel(self); self.CancelActivity(); Church = order.Target.Actor; cadence = attempts = 0;
				if (!string.IsNullOrEmpty(info.Condition)) token = self.GrantCondition(info.Condition);
			}
			else if (order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "EnterTransport" or "MedievalWork" or "MedievalAssignSiege" or "MedievalGatherAnimal" or "MedievalStartPatrol" or "MedievalRest") Cancel(self);
		}
		void ITick.Tick(Actor self)
		{
			if (HealPulse > 0) HealPulse--;
			if (Church == null || --cadence > 0) return; cadence = 25;
			if (!CanAttend(self, Church)) { Cancel(self, true); return; }
			if (!MedievalAccess.Adjacent(self, Church))
			{
				if (attempts >= 6) { Cancel(self, true); return; }
				if (self.IsIdle) MedievalLogistics.Approach(self, Church, ref attempts);
				return;
			}
			attempts = 0;
			var priests = Church.Trait<MedievalChurch>().OnDutyPriests(Church).OrderBy(a => a.ActorID).ToArray();
			var places = priests.Length * 3;
			var ahead = self.World.ActorsHavingTrait<MedievalPatient>().Count(a => a.ActorID < self.ActorID && MedievalStockpile.Active(a)
				&& a.Trait<MedievalPatient>().Church == Church && MedievalAccess.Adjacent(a, Church));
			if (ahead >= places) return;
			var health = self.Trait<Health>(); var amount = Math.Min(health.MaxHP - health.HP, Math.Max(1, health.MaxHP / 20));
			health.InflictDamage(self, Church, new Damage(-amount), true); Healed += amount;
			HealPulse = 24;
			var priest = priests[ahead / 3];
			priest.Trait<MedievalPriest>().ShowHealing();
			if (priest.IsIdle) priest.Trait<Mobile>().Facing = (self.CenterPosition - priest.CenterPosition).Yaw;
			Church.TraitOrDefault<MedievalChurchAnimation>()?.ShowCare();
			if (health.HP >= health.MaxHP) Cancel(self);
		}
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var r in renderables) yield return r;
			if (HealPulse > 0 && MedievalHouseLights.Visible(self) && self.Trait<MedievalRest>().Sleeping == false)
				yield return new ChurchGoldRenderable(self.CenterPosition, 2, ChurchGoldVisual.Healing, HealPulse, 0);
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => bounds;
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Cancel(self); }
	}
}
