using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalCampAreaInfo : TraitInfo, Requires<MedievalShelterInfo>
	{
		public readonly WDist Radius = new(6144);
		public override object Create(ActorInitializer init) => new MedievalCampArea(this);
	}
	public sealed class MedievalCampArea : ISync, IRenderAnnotations
	{
		public readonly MedievalCampAreaInfo Info;
		[VerifySync] public bool RestEnabled;
		public MedievalCampArea(MedievalCampAreaInfo info) { Info = info; }
		public bool Contains(Actor tent, Actor unit) => tent.Owner == unit.Owner && MedievalStockpile.Active(tent)
			&& (tent.CenterPosition - unit.CenterPosition).HorizontalLengthSquared <= (long)Info.Radius.Length * Info.Radius.Length;
		public static bool Accepts(Actor tent, Actor unit) => tent.TraitOrDefault<MedievalCampArea>() is { RestEnabled: true } area && area.Contains(tent, unit);
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (self.Owner != self.World.LocalPlayer || !MedievalStockpile.Active(self) || self.World.FogObscures(self)) yield break;
			foreach (var line in MedievalFoodProvider.Ring(self.CenterPosition, Info.Radius, RestEnabled ? Color.LightGreen : Color.Goldenrod)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
	public class MedievalCommanderInfo : TraitInfo, Requires<MobileInfo>, Requires<MedievalCampStoreInfo>
	{
		public readonly WDist CommandRadius = new(12288);
		public readonly WDist CartRadius = new(8192);
		public readonly int BuildTicks = 75;
		[GrantedConditionReference] public readonly string WorkingCondition = "commander-working";
		public override object Create(ActorInitializer init) => new MedievalCommander(this);
	}
	// One real tent is reserved, picked up, carried and consumed only on successful assembly.
	public sealed class MedievalCommander : ITick, IResolveOrder, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		public readonly MedievalCommanderInfo Info;
		Actor source;
		Inventory sourceStock;
		[VerifySync] CPos site;
		[VerifySync] int stage, attempts, work, cadence, condition = Actor.InvalidConditionToken;
		[VerifySync] bool reserved;
		[VerifySync] public int SourceId => (int)(source?.ActorID ?? 0);
		public bool Busy => stage != 0;
		public bool Building => stage == 3;
		public int Progress => work / 100;
		public string Status { get; private set; } = "Rassemblez une charrette chargee pour installer le camp.";
		public MedievalCommander(MedievalCommanderInfo info) { Info = info; }
		public static bool GroundFree(World world, Player owner, CPos cell)
		{
			var actor = world.Map.Rules.Actors["md.militarytent"]; var bi = actor.TraitInfo<BuildingInfo>();
			var cells = bi.Tiles(cell).ToHashSet();
			return cells.All(c => world.Map.Contains(c) && owner.Shroud.IsExplored(c) && !MedievalStockyard.Covers(world, c) && !MedievalCemeteries.Covers(world, c))
				&& world.CanPlaceBuilding(cell, actor, bi, null)
				&& !world.ActorsHavingTrait<Building>().Any(a => a.IsInWorld && !a.IsDead && a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location).Any(cells.Contains));
		}
		public IEnumerable<Actor> Carts(Actor self) => self.World.ActorsHavingTrait<MedievalCampCart>()
			.Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a.IsIdle
				&& a.Trait<MedievalConstructionConvoy>().Active == false && a.Trait<MedievalCampStore>().Stock.Available(1) > 0
				&& (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= (long)Info.CartRadius.Length * Info.CartRadius.Length)
			.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID);
		public bool HasTent(Actor self) => self.Trait<MedievalCampStore>().Stock.Available(1) > 0 || Carts(self).Any();
		public bool CanDeploy(Actor self, CPos cell) => MedievalStockpile.Active(self) && !Busy
			&& GroundFree(self.World, self.Owner, cell)
			&& (self.Trait<MedievalCampStore>().Stock.Available(1) > 0 ? (cell - self.Location).LengthSquared <= 64
				: Carts(self).Any(c => (c.Location - cell).LengthSquared <= 25));
		public IEnumerable<Actor> Tents(Actor self) => self.World.ActorsHavingTrait<MedievalCampArea>()
			.Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a)
				&& (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= (long)Info.CommandRadius.Length * Info.CommandRadius.Length)
			.OrderBy(a => a.ActorID);
		public void Cancel(Actor self)
		{
			if (reserved) sourceStock.ReleaseOut(1, 1);
			reserved = false; source = null; sourceStock = null; stage = work = attempts = cadence = 0;
			if (condition != Actor.InvalidConditionToken) condition = self.RevokeCondition(condition);
		}
		IEnumerable<CPos> Perimeter()
		{
			for (var y = -1; y <= 2; y++) for (var x = -1; x <= 2; x++)
				if (x is -1 or 2 || y is -1 or 2) yield return site + new CVec(x, y);
		}
		bool Approach(Actor self, IEnumerable<CPos> perimeter)
		{
			var cells = perimeter.ToArray(); if (cells.Contains(self.Location)) { attempts = 0; return true; }
			var mobile = self.Trait<Mobile>();
			var entry = cells.Where(c => self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.All))
				.OrderBy(c => (c - self.Location).LengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).Select(c => (CPos?)c).FirstOrDefault();
			if (entry == null || ++attempts > 4) { Cancel(self); Status = "Acces bloque. La tente portee reste disponible."; return false; }
			self.QueueActivity(mobile.MoveTo(entry.Value, 0)); return false;
		}
		void ITick.Tick(Actor self)
		{
			if (!Busy) return;
			if (self.Trait<MedievalRest>().Resting) { Cancel(self); return; }
			if (stage == 1 && (!MedievalStockpile.Active(source) || source.Owner != self.Owner || !source.IsIdle))
			{ self.CancelActivity(); Cancel(self); Status = "Chargement annule : la charrette est indisponible."; return; }
			if (!self.IsIdle) return;
			if (stage == 3)
			{
				if (!Perimeter().Contains(self.Location)) { Cancel(self); return; }
				work += self.Trait<MedievalNeedsFood>().WorkPercent;
				if (work < Info.BuildTicks * 100) return;
				self.World.AddFrameEndTask(w =>
				{
					if (!MedievalStockpile.Active(self) || stage != 3) return;
					if (!GroundFree(w, self.Owner, site) || self.Trait<MedievalCampStore>().Stock.Available(1) == 0)
					{ Cancel(self); Status = "Emplacement occupe. La tente reste portee par le commandant."; return; }
					self.Trait<MedievalCampStore>().Stock.Take(1, 1);
					w.CreateActor("md.militarytent", [new OwnerInit(self.Owner), new LocationInit(site)]);
					Cancel(self); Status = "Pavillon installe : donnez l'ordre Repos du camp.";
				});
				return;
			}
			if (--cadence > 0) return; cadence = 8;
			if (stage == 1 && Approach(self, MedievalAccess.Perimeter(source)))
			{
				sourceStock.Load(1, 1); reserved = false;
				self.Trait<MedievalCampStore>().Stock.Add(1, 1); source = null; sourceStock = null;
				stage = 2; Status = "Transporte le pavillon jusqu'au campement.";
			}
			if (stage == 2 && Approach(self, Perimeter()))
			{
				if (!GroundFree(self.World, self.Owner, site)) { Cancel(self); Status = "Terrain occupe : tente conservee."; return; }
				var direction = self.World.Map.CenterOfCell(site) - self.CenterPosition;
				if (direction.HorizontalLengthSquared > 0) self.Trait<Mobile>().Facing = direction.Yaw;
				stage = 3; work = 0; Status = "Monte les mats et tend la toile du pavillon.";
			}
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (self.World.Paused) return;
			if (order.OrderString == "MedievalCommanderDeploy" && order.Target.Type == TargetType.Terrain)
			{
				var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
				if (!CanDeploy(self, cell) || !self.Trait<MedievalRest>().Wake(self, true)) return;
				self.TraitOrDefault<MedievalPatient>()?.Cancel(self);
				self.CancelActivity(); site = cell; attempts = work = cadence = 0;
				if (self.Trait<MedievalCampStore>().Stock.Available(1) > 0) stage = 2;
				else
				{
					source = Carts(self).FirstOrDefault(c => (c.Location - cell).LengthSquared <= 25);
					if (source == null) return;
					sourceStock = source.Trait<MedievalCampStore>().Stock;
					if (!sourceStock.ReserveOut(1, 1)) { source = null; sourceStock = null; return; }
					reserved = true; stage = 1;
				}
				condition = self.GrantCondition(Info.WorkingCondition); Status = "Va chercher une tente dans la charrette.";
			}
			else if (order.OrderString is "MedievalCommanderRest" or "MedievalCommanderWake")
			{
				if (!self.Trait<MedievalRest>().Wake(self, true)) return;
				self.CancelActivity(); Cancel(self);
				var tents = Tents(self).ToArray(); var rest = order.OrderString == "MedievalCommanderRest";
				foreach (var tent in tents) tent.Trait<MedievalCampArea>().RestEnabled = rest;
				foreach (var unit in self.World.ActorsHavingTrait<MedievalRest>().Where(a => a.Owner == self.Owner && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalMorale>() != null).OrderBy(a => a.ActorID))
				{
					var needs = unit.Trait<MedievalRest>();
					if (rest && tents.Any(t => t.Trait<MedievalCampArea>().Contains(t, unit))) needs.CampRest(unit);
					else if (!rest && tents.Contains(needs.Shelter)) { unit.CancelActivity(); needs.Wake(unit, true); }
				}
				Status = tents.Length == 0 ? "Aucun pavillon du pays a moins de 12 cases." : rest ? "Repos ordonne dans les cercles verts. 6 places par pavillon." : "Fin du repos. Les soldats quittent les pavillons.";
			}
			else if (order.OrderString is "Move" or "Stop" or "Attack" or "ForceAttack" or "AttackMove" or "AssaultMove" or "Scatter" or "MedievalRest") Cancel(self);
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) => Cancel(self);
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) => Cancel(self);
	}
}
