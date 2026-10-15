using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public partial class MedievalWorker : IIssueOrder
	{
		public Actor AssignedWorkplace { get; private set; }
		[VerifySync] public int AssignedWorkplaceId => unchecked((int)(AssignedWorkplace?.ActorID ?? 0));
		bool Agricultural => Info.Profession is "farmer" or "breeder" or "shepherd";
		string FarmType => Info.Profession == "farmer" ? "md.farm" : Info.Profession == "breeder" ? "md.ranch" : "md.sheepfold";
		public string AssignmentDescription => AssignedWorkplace == null ? "" : "Affecté : "
			+ (MedievalConstructionMenuLogic.Categories.SelectMany(c => c.Items).FirstOrDefault(e => e.Type == AssignedWorkplace.Info.Name).Name
				?? (Agricultural ? "Zone agricole" : "Chantier"));

		public bool CanAssignWorkplace(Actor self, Actor target)
		{
			if (!MedievalStockpile.Active(self) || !Valid(self, target) || target.Disposed) return false;
			if (Info.Profession == "builder") return target.TraitOrDefault<MedievalBuilding>() is { Complete: false, Demolishing: false };
			if (!MedievalStockpile.Active(target)) return false;
			if (Agricultural) return target.Info.Name == FarmType
				|| target.TraitOrDefault<MedievalAgriculturalArea>() is { } zone && zone.Info.Profession == Info.Profession && zone.Farm(target) != null;
			return Info.Profession is "stonecutter" or "goldminer" or "lumberjack" or "hunter"
				&& target.TraitOrDefault<MedievalStockpile>()?.Info.Profession == Info.Profession;
		}

		// The player's choice constrains both harvesting and automatic animal collection.
		public bool AllowsWorkArea(Actor area) => AssignedWorkplace == null || area == AssignedWorkplace
			|| area is { Disposed: false } && area.TraitOrDefault<MedievalAgriculturalArea>()?.Farm(area) == AssignedWorkplace;

		public bool AssignWorkplace(Actor self, Actor target)
		{
			// Reject first: a malformed or foreign target must not cancel care, cargo or reservations.
			if (!CanAssignWorkplace(self, target)) return false;
			if (self.TraitOrDefault<MedievalRest>() is { } rest && !rest.Wake(self, true, true)) return false;
			self.TraitOrDefault<MedievalPatient>()?.Cancel(self);
			self.TraitOrDefault<MedievalFirefighter>()?.Cancel(self, false);
			self.TraitOrDefault<MedievalHerding>()?.Cancel(self);
			self.CancelActivity(); Release(); WorkArea = null;
			AssignedWorkplace = target; Automatic = true; cadence = 0;
			Status = "Rejoint son affectation";
			return true;
		}

		void ValidateAssignment(Actor self)
		{
			if (AssignedWorkplace == null || CanAssignWorkplace(self, AssignedWorkplace)) return;
			AssignedWorkplace = null; WorkArea = null;
			self.TraitOrDefault<MedievalHerding>()?.Cancel(self);
			if (self.TraitOrDefault<MedievalRest>()?.Resting != true && self.TraitOrDefault<MedievalPatient>()?.Church == null
				&& self.TraitOrDefault<MedievalFirefighter>()?.Busy != true) self.CancelActivity();
			Release();
			Status = "Affectation terminée : reprise automatique";
		}

		void WaitAtWorkplace(Actor self)
		{
			if (AssignedWorkplace != null && self.IsIdle) Approach(self, AssignedWorkplace);
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders { get { yield return new WorkplaceTargeter(this); } }
		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) =>
			order.OrderID == "MedievalAssignWorkplace" ? new Order(order.OrderID, self, target, false) : null;
		sealed class WorkplaceTargeter : UnitOrderTargeter
		{
			readonly MedievalWorker worker;
			public WorkplaceTargeter(MedievalWorker worker) : base("MedievalAssignWorkplace", 6, "enter", false, true)
			{ this.worker = worker; ForceAttack = false; }
			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor) =>
				!modifiers.HasModifier(TargetModifiers.ForceMove) && worker.CanAssignWorkplace(self, target);
			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor) => false;
		}
	}
}
