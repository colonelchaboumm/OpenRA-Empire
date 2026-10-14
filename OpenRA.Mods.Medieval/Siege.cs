using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class SiegeRules
	{
		public const int RamCrew = 4;
		public const int TrebuchetCrew = 4;
		public const int StonePerShot = 5;
		public const int LoadingTicks = 75;
		public static int CrewWorkPercent(int count, int combinedWork) => count <= 0 ? 0 : (100 + 40 * (Math.Min(count, TrebuchetCrew) - 1)) * combinedWork / (count * 100);
		public static int CrewSpeedPercent(int count) => count <= 0 ? 0 : 65 + 15 * Math.Min(count, TrebuchetCrew);
		public const int AssemblyTicks = 300;
		public const int PackingTicks = 200;
		public static readonly string[] Products = ["md.ram", "md.trebuchet", "md.stonecart", "md.cauldron"];
		public static readonly string[] Names = ["Belier", "Trebuchet sur charrette", "Charrette de pierres", "Chaudron mobile"];
		public static readonly int[][] Costs = [[100, 0, 0, 20], [150, 20, 0, 50], [40, 20, 0, 10], [60, 0, 0, 40]];
		public static bool NeedsHousing(int product) => product is 1 or 2;
		public static bool CanShoot(bool deployed, int stones) => deployed && stones >= StonePerShot;
		public static bool Live(Actor actor, Player owner) => actor?.IsInWorld == true && !actor.IsDead && actor.Owner == owner;
		public static IEnumerable<CPos> Braces(CPos center)
		{
			for (var y = -1; y <= 1; y++)
				for (var x = -1; x <= 1; x++)
					if (x != 0 || y != 0) yield return center + new CVec(x, y);
		}
	}

	public class MedievalSiegeCrewInfo : TraitInfo, Requires<MobileInfo>
	{
		[GrantedConditionReference] public readonly string Condition = "siege-crew";
		public override object Create(ActorInitializer init) => new MedievalSiegeCrew(this);
	}
	public sealed partial class MedievalSiegeCrew : ITick, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		readonly MedievalSiegeCrewInfo info;
		Actor engine;
		int token = Actor.InvalidConditionToken;
		[VerifySync] int cadence;
		[VerifySync] public int EngineId => unchecked((int)(engine?.ActorID ?? 0));
		public MedievalSiegeCrew(MedievalSiegeCrewInfo info) { this.info = info; }
		public bool Assigned => engine != null;
		public bool Serves(Actor target) => engine == target;
		public bool Trebuchet => engine?.Trait<MedievalSiegeEngine>().Info.Trebuchet == true;
		public bool Near(Actor self) => SiegeRules.Live(engine, self.Owner) && (self.CenterPosition - engine.CenterPosition).HorizontalLengthSquared <= (Trebuchet ? 3500L * 3500 : 2048L * 2048);
		public void Assign(Actor self, Actor target)
		{
			if (!SiegeRules.Live(target, self.Owner) || target.TraitOrDefault<MedievalSiegeEngine>() is not MedievalSiegeEngine siege
				|| Serves(target) || siege.Crew.Count() >= siege.CrewLimit) return;
			if (self.TraitOrDefault<MedievalRest>() is { Resting: true } rest && !rest.Wake(self, true)) return;
			self.TraitOrDefault<MedievalPatrol>()?.Stop(self, false);
			Release(self); engine = target; self.CancelActivity();
			token = self.GrantCondition(info.Condition);
		}
		public void Release(Actor self, bool cancelActivity = true)
		{
			CancelLoading();
			if (engine != null && cancelActivity) self.CancelActivity(); engine = null;
			if (token != Actor.InvalidConditionToken) token = self.RevokeCondition(token);
		}
		void ITick.Tick(Actor self)
		{
			Working = false;
			if (!self.IsInWorld) return;
			if (self.TraitOrDefault<MedievalRest>()?.Resting == true) return;
			if (engine == null) { ReturnStone(self); return; }
			if (!SiegeRules.Live(engine, self.Owner)) { Release(self); return; }
			if (Trebuchet && !engine.Trait<MedievalSiegeEngine>().Deployed && loading != null)
			{ loading.Cancel(); loading = null; handling = 0; self.CancelActivity(); }
			if (CanPush)
			{
				if (self.IsIdle) self.QueueActivity(new SiegePush(self, this, engine));
				return;
			}
			if (Trebuchet) { TickTrebuchet(self); return; }
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalAssignSiege" && order.Target.Type == TargetType.Actor) Assign(self, order.Target.Actor);
			else if (order.OrderString == "MedievalLeaveSiege") Release(self);
			else if (order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "CaptureActor" or "EnterTransport") Release(self, false);
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Release(self); }
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Release(self); }
	}

	public class MedievalSiegeEngineInfo : TraitInfo, Requires<MobileInfo>
	{
		public readonly bool Trebuchet = false;
		public readonly bool Cauldron = false;
		[GrantedConditionReference] public readonly string FixedCondition = "siege-fixed";
		[ActorReference] public readonly string BraceActor = "md.siegebrace";
		public override object Create(ActorInitializer init) => new MedievalSiegeEngine(init.Self, this);
	}
	public sealed class MedievalSiegeEngine : ITick, IResolveOrder, INotifyAttack, INotifyRemovedFromWorld, INotifyOwnerChanged, IFirepowerModifier, ISpeedModifier, IReloadModifier, ISync, IRenderAnnotations
	{
		readonly Actor self;
		public readonly MedievalSiegeEngineInfo Info;
		readonly List<Actor> braces = [];
		int fixedToken = Actor.InvalidConditionToken;
		// One actor keeps its health, cargo, owner, meals and identity throughout deployment.
		// 0 = transport, 1 = assembling, 2 = deployed, 3 = packing.
		[VerifySync] public int State { get; private set; }
		[VerifySync] public int Progress { get; private set; }
		[VerifySync] public int Shots { get; private set; }
		[VerifySync] public int AttackTicks { get; private set; }
		[VerifySync] public int LoadProgress { get; private set; }
		[VerifySync] int workRemainder, loadRemainder;
		public readonly Inventory Sling = new([0, 0, 0, SiegeRules.StonePerShot]);
		[VerifySync] public int SlingHash => Sling.Hash;
		[VerifySync] bool deploymentPending;
		[VerifySync] bool packingPending;
		public bool Packed => Info.Trebuchet && State == 0;
		public bool Deployed => Info.Trebuchet && State == 2;
		public bool Busy => State is 1 or 3 || deploymentPending || packingPending;
		public int Stone => Info.Trebuchet ? self.Trait<MedievalStockpile>().Stock.Amount(3) : 0;
		public IEnumerable<Actor> Crew => self.World.ActorsWithTrait<MedievalSiegeCrew>().Where(p => SiegeRules.Live(p.Actor, self.Owner) && p.Trait.Serves(self)).Select(p => p.Actor).OrderBy(a => a.ActorID);
		public int ReadyCrew => Crew.Count(a => a.Trait<MedievalSiegeCrew>().Near(a));
		public int CrewLimit => Info.Cauldron ? 2 : Info.Trebuchet ? SiegeRules.TrebuchetCrew : SiegeRules.RamCrew;
		public int WorkPercent
		{
			get
			{
				var crew = Crew.Where(a => a.Trait<MedievalSiegeCrew>().Near(a)).ToArray();
				return SiegeRules.CrewWorkPercent(crew.Length, crew.Sum(a => a.Trait<MedievalNeedsFood>().WorkPercent));
			}
		}
		public bool CanMove => Info.Trebuchet ? Packed && !deploymentPending && ReadyCrew > 0 : ReadyCrew >= CrewLimit;
		public bool CanFire => !Info.Cauldron && (Info.Trebuchet ? !packingPending && SiegeRules.CanShoot(Deployed, Sling.Available(3)) && ReadyCrew > 0 && LoadProgress >= SiegeRules.LoadingTicks : ReadyCrew >= CrewLimit && AttackTicks == 0);
		public string AssemblyStep
		{
			get
			{
				var progress = State == 1 ? Progress * 100 / SiegeRules.AssemblyTicks : 100 - Progress * 100 / SiegeRules.PackingTicks;
				return progress < 18 ? State == 1 ? "Detache les charges et gare la charrette" : "Arrime les pieces sur la charrette"
					: progress < 42 ? State == 1 ? "Pose le chassis et les traverses" : "Recharge le chassis et les traverses"
					: progress < 74 ? State == 1 ? "Leve les supports avec le treuil" : "Abaisse et range les supports"
					: progress < 91 ? State == 1 ? "Installe le bras et le contrepoids" : "Depose le contrepoids et le bras"
					: State == 1 ? "Fixe les cordages et la fronde" : "Retire les cordages et la fronde";
			}
		}
		public string Status => Info.Cauldron ? $"Soldats presents : {ReadyCrew}/2. " + (CanMove ? "Pret a avancer. Enflamme les fleches des archers allies proches." : "Deux soldats au contact sont requis pour avancer.")
			: !Info.Trebuchet ? $"Soldats presents : {ReadyCrew}/{SiegeRules.RamCrew}. " + (CanMove ? "Pret a avancer et frapper les batiments." : "Appelez des soldats ou attendez leur arrivee.")
			: ReadyCrew == 0 ? "Attend un soldat affecte a proximite pour manoeuvrer."
			: packingPending ? "Termine le lancer avant de demonter le trebuchet."
			: deploymentPending ? "Attend que l'equipage degage l'emplacement des appuis."
			: State == 0 ? "Demonte sur sa charrette. Choisissez un terrain libre pour monter."
			: State == 1 ? $"Montage : {Progress * 100 / SiegeRules.AssemblyTicks}% - {AssemblyStep}."
			: State == 3 ? $"Demontage : {Progress * 100 / SiegeRules.PackingTicks}% - {AssemblyStep}."
			: Sling.Amount(3) > 0 ? LoadProgress < SiegeRules.LoadingTicks ? $"Charge le projectile et arme le treuil : {LoadProgress * 100 / SiegeRules.LoadingTicks}%." : "Projectile charge. Pret a tirer."
			: Sling.Incoming(3) > 0 ? "Un soldat va chercher et porter la pierre depuis la charrette."
			: Stone < SiegeRules.StonePerShot ? "A court de pierres : affectez une charrette de ravitaillement."
			: "Attend le porteur pour charger le prochain projectile.";
		public MedievalSiegeEngine(Actor self, MedievalSiegeEngineInfo info) { this.self = self; Info = info; }
		public void CallCrew(bool one = false)
		{
			var missing = Math.Min(one ? 1 : CrewLimit, CrewLimit - Crew.Count());
			foreach (var pair in self.World.ActorsWithTrait<MedievalSiegeCrew>().Where(p => SiegeRules.Live(p.Actor, self.Owner) && !p.Trait.Assigned
				&& (p.Actor.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= 100L * 1024 * 1024)
				.OrderBy(p => (p.Actor.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(p => p.Actor.ActorID).Take(Math.Max(0, missing)))
				pair.Trait.Assign(pair.Actor, self);
		}
		public bool ClearSite() => SiegeRules.Braces(self.Location).All(c => self.World.Map.Contains(c)
			&& new[] { "Clear", "Road", "Rough" }.Contains(self.World.Map.GetTerrainInfo(c).Type)
			&& !self.World.ActorMap.GetActorsAt(c).Any(a => a != self && a.IsInWorld && !a.IsDead && !(a.Owner == self.Owner && a.TraitOrDefault<MedievalSiegeCrew>()?.Serves(self) == true))
			&& !MedievalStockyard.Covers(self.World, c) && !MedievalCemeteries.Covers(self.World, c));
		bool SiteVacant() => ClearSite() && SiegeRules.Braces(self.Location).All(c => !self.World.ActorMap.GetActorsAt(c).Any(a => a != self && a.IsInWorld && !a.IsDead));
		void Fixed(bool value)
		{
			if (value && fixedToken == Actor.InvalidConditionToken) fixedToken = self.GrantCondition(Info.FixedCondition);
			else if (!value && fixedToken != Actor.InvalidConditionToken) fixedToken = self.RevokeCondition(fixedToken);
		}
		void RemoveBraces()
		{
			var old = braces.ToArray(); braces.Clear();
			self.World.AddFrameEndTask(_ => { foreach (var a in old) if (a.IsInWorld) a.Dispose(); });
		}
		void BeginPacking()
		{
			packingPending = false; self.CancelActivity(); State = 3;
			Progress = workRemainder = LoadProgress = loadRemainder = 0; AttackTicks = 0;
		}
		void IResolveOrder.ResolveOrder(Actor actor, Order order)
		{
			if (!Info.Trebuchet && !order.Queued && order.OrderString is "Stop" or "Move" or "Scatter" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "MedievalSiegeRelease") AttackTicks = 0;
			if (order.OrderString == "MedievalSiegeCrew") CallCrew();
			else if (order.OrderString == "MedievalSiegeAddCrew") CallCrew(true);
			else if (order.OrderString == "MedievalSiegeRelease")
			{ foreach (var a in Crew.ToArray()) a.Trait<MedievalSiegeCrew>().Release(a); self.CancelActivity(); Fixed(!CanMove); }
			else if (order.OrderString == "MedievalSiegeDeploy" && Packed && !Busy && ReadyCrew > 0 && self.IsIdle && ClearSite())
			{
				deploymentPending = true; Fixed(true);
			}
			else if (order.OrderString == "Stop") { deploymentPending = packingPending = false; Fixed(!CanMove); }
			else if (order.OrderString == "MedievalSiegePack" && Deployed)
			{
				self.CancelActivity();
				if (AttackTicks > 0) packingPending = true;
				else BeginPacking();
			}
		}
		void ITick.Tick(Actor actor)
		{
			if (!Info.Trebuchet && ReadyCrew < CrewLimit) AttackTicks = 0;
			Fixed(!CanMove);
			if (deploymentPending && Packed && ReadyCrew > 0 && self.IsIdle && SiteVacant())
			{
				var owner = self.Owner;
				self.World.AddFrameEndTask(w =>
				{
					if (!SiegeRules.Live(self, owner) || !deploymentPending || !Packed || !SiteVacant()) return;
					deploymentPending = false; self.CancelActivity(); State = 1; Progress = workRemainder = 0; Fixed(true);
					self.World.ScreenMap.AddOrUpdate(self);
					foreach (var c in SiegeRules.Braces(self.Location)) braces.Add(w.CreateActor(Info.BraceActor, [new OwnerInit(self.Owner), new LocationInit(c)]));
				});
			}
			if (AttackTicks > 0) AttackTicks--;
			if (packingPending && AttackTicks == 0 && Deployed) BeginPacking();
			if (Deployed && Sling.Amount(3) == SiegeRules.StonePerShot && LoadProgress < SiegeRules.LoadingTicks && AttackTicks == 0)
			{
				loadRemainder += WorkPercent;
				LoadProgress = Math.Min(SiegeRules.LoadingTicks, LoadProgress + loadRemainder / 100); loadRemainder %= 100;
			}
			if (State is not (1 or 3)) return;
			var duration = State == 1 ? SiegeRules.AssemblyTicks : SiegeRules.PackingTicks;
			if (Progress < duration - 1)
			{
				workRemainder += WorkPercent;
				// Keep the last articulated pose for a tick even with a fast crew.
				Progress = Math.Min(duration - 1, Progress + workRemainder / 100); workRemainder %= 100;
				return;
			}
			if (WorkPercent == 0) return;
			if (State == 1) State = 2;
			else { State = 0; RemoveBraces(); }
			Progress = 0; Fixed(!CanMove); self.World.ScreenMap.AddOrUpdate(self);
		}
		void INotifyAttack.PreparingAttack(Actor actor, in Target target, Armament armament, Barrel barrel)
		{
			if (Info.Trebuchet && target.CenterPosition != self.CenterPosition) self.Trait<Mobile>().Facing = (target.CenterPosition - self.CenterPosition).Yaw;
		}
		void INotifyAttack.Attacking(Actor actor, in Target target, Armament armament, Barrel barrel)
		{
			if (Info.Trebuchet) { Sling.Take(3, SiegeRules.StonePerShot); LoadProgress = loadRemainder = 0; }
			Shots++; AttackTicks = Info.Trebuchet ? SiegeShotMotion.FireTicks : RamMotion.FireTicks;
		}
		int IFirepowerModifier.GetFirepowerModifier() => Info.Trebuchet ? 100 : Crew.Any() ? (int)Crew.Average(a => a.TraitOrDefault<MedievalMorale>()?.State.Firepower ?? 100) : 100;
		int IReloadModifier.GetReloadModifier() => Info.Trebuchet ? 10000 / Math.Max(1, WorkPercent) : 100;
		int ISpeedModifier.GetSpeedModifier() => Info.Trebuchet ? SiegeRules.CrewSpeedPercent(ReadyCrew)
			: !Crew.Any() ? 100 : Crew.Min(a => a.Trait<MedievalNeedsFood>().Need.Hungry ? a.Trait<MedievalNeedsFood>().Info.HungrySpeedPercent : 100);
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor actor)
		{
			foreach (var a in Crew.ToArray()) a.Trait<MedievalSiegeCrew>().Release(a);
			// ChangeOwnerSync temporarily removes and re-adds the same actor. Keep its
			// physical supports during that transfer; clean them only after real removal.
			self.World.AddFrameEndTask(_ => { if (!self.IsInWorld || self.IsDead) RemoveBraces(); });
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor actor, Player oldOwner, Player newOwner)
		{
			self.CancelActivity(); deploymentPending = packingPending = false; AttackTicks = 0;
			foreach (var a in braces) if (a.IsInWorld) a.ChangeOwnerSync(newOwner);
			foreach (var p in self.World.ActorsWithTrait<MedievalStoneCart>()) p.Trait.LostEngine(p.Actor, self);
			Fixed(!CanMove); self.World.ScreenMap.AddOrUpdate(self);
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor actor, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || self.World.FogObscures(self)) yield break;
			if (Info.Trebuchet)
			{
				foreach (var line in MedievalFoodProvider.Ring(self.CenterPosition, new WDist(16 * 1024), Color.Goldenrod)) yield return line;
				foreach (var line in MedievalFoodProvider.Ring(self.CenterPosition, new WDist(4 * 1024), Color.OrangeRed)) yield return line;
			}
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}

	// Check at the instant of firing, including if a crew member died earlier this tick.
	public class MedievalSiegeArmamentInfo : ArmamentInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeArmament(init.Self, this);
	}
	public sealed class MedievalSiegeArmament : Armament
	{
		public MedievalSiegeArmament(Actor self, MedievalSiegeArmamentInfo info) : base(self, info) { }
		protected override bool CanFire(Actor self, in Target target)
		{
			var engine = self.Trait<MedievalSiegeEngine>();
			if (!engine.Info.Trebuchet && target.Type == TargetType.Actor
				&& (RamMotion.Contact(self, target.Actor) - self.CenterPosition).HorizontalLength is < 1250 or > 1950) return false;
			return engine.CanFire && base.CanFire(self, target);
		}
	}
	public class MedievalSiegeAttackInfo : AttackFrontalInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeAttack(init.Self, this);
	}
	public sealed class MedievalSiegeAttack : AttackFrontal
	{
		public MedievalSiegeAttack(Actor self, MedievalSiegeAttackInfo info) : base(self, info) { }
		public override Activity GetAttackActivity(Actor self, AttackSource source, in Target target, bool allowMove, bool forceAttack, Color? targetLineColor = null)
			=> self.Trait<MedievalSiegeEngine>().Info.Trebuchet ? base.GetAttackActivity(self, source, target, false, forceAttack, targetLineColor)
				: new RamAttack(self, target, allowMove, forceAttack, targetLineColor);
	}
	public class WithMedievalSiegeBodyInfo : WithSpriteBodyInfo
	{
		public override object Create(ActorInitializer init) => new WithMedievalSiegeBody(init, this);
	}
	public sealed class WithMedievalSiegeBody : WithSpriteBody, ITick
	{
		readonly MedievalGait gait = new();
		WPos previous;
		string sequence;
		int frame;
		public string Sequence => sequence;
		public int Frame => frame;
		public WithMedievalSiegeBody(ActorInitializer init, WithMedievalSiegeBodyInfo info) : base(init, info,
			() => SiegePushMotion.SpriteFacing(init.Self.Info.TraitInfoOrDefault<MedievalSiegeEngineInfo>()?.Cauldron == true, init.Self.Trait<Mobile>().Facing)) { previous = init.Self.CenterPosition; }
		void ITick.Tick(Actor self)
		{
			var travel = (self.CenterPosition - previous).HorizontalLength; previous = self.CenterPosition; gait.Advance(travel);
			var engine = self.TraitOrDefault<MedievalSiegeEngine>();
			var next = engine?.Info.Trebuchet == true ? engine.State == 0 ? travel > 0 ? "run" : "idle" : engine.State == 2 ? engine.AttackTicks > 0 ? "fire" : "deployed" : "assemble"
				: engine?.AttackTicks > 0 ? "fire" : travel > 0 ? "run" : "idle";
			var length = DefaultAnimation.GetSequence(next).Length;
			frame = next == "fire" ? Math.Clamp((engine.Info.Trebuchet ? SiegeShotMotion.FireTicks - engine.AttackTicks : RamMotion.FireTicks - engine.AttackTicks) * length / (engine.Info.Trebuchet ? SiegeShotMotion.FireTicks : RamMotion.FireTicks), 0, length - 1)
				: next == "assemble" ? engine.State == 1 ? Math.Min(length - 1, engine.Progress * length / SiegeRules.AssemblyTicks)
					: length - 1 - Math.Min(length - 1, engine.Progress * length / SiegeRules.PackingTicks)
				: travel > 0 ? gait.Frame % length : 0;
			if (sequence != next) { sequence = next; DefaultAnimation.PlayFetchIndex(next, () => frame); }
		}
	}
}
