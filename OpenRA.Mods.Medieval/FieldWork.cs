using System;
using System.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Medieval
{
	public partial class MedievalWorker
	{
		[VerifySync] CPos fieldCell;
		[VerifySync] bool hasFieldCell;
		[VerifySync] int fieldVisit;
		public CPos? FieldCell => hasFieldCell ? fieldCell : null;
		// Reserve distinct working positions inside the crop, independent of rendering.
		void ChooseFieldCell(Actor self)
		{
			hasFieldCell = false;
			if (source?.TraitOrDefault<MedievalAgriculturalArea>() is not { } area) return;
			var taken = self.World.ActorsHavingTrait<MedievalWorker>().Where(a => a != self && a.IsInWorld && !a.IsDead)
				.Select(a => a.Trait<MedievalWorker>()).Where(w => w.source == source && w.hasFieldCell).Select(w => w.fieldCell).ToHashSet();
			var mobile = self.Trait<Mobile>(); var visit = fieldVisit++;
			var cells = new AgriculturalZone(source.Location, area.Size).Cells()
				.Where(c => !taken.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.All)).ToArray();
			var chosen = cells.OrderBy(c => c == fieldCell ? 1 : 0)
				.ThenBy(c => LivestockMotion.Hash(unchecked((uint)(c.X * 617 + c.Y * 2099 + visit * 7919)) ^ self.ActorID * 97))
				.Select(c => (CPos?)c).FirstOrDefault();
			if (chosen == null) return;
			fieldCell = chosen.Value; hasFieldCell = true;
		}
		bool ApproachField(Actor self)
		{
			if (!hasFieldCell) ChooseFieldCell(self);
			if (!hasFieldCell) { Release(); cadence = 40; Status = "Attend une place libre dans la parcelle"; return false; }
			if (self.Location == fieldCell) { failedMoves = 0; return true; }
			if (++failedMoves > 3) { ChooseFieldCell(self); failedMoves = 0; }
			if (!hasFieldCell) return false;
			self.QueueActivity(self.Trait<Mobile>().MoveTo(fieldCell, 0));
			IsWorking = false; WorkTarget = null; Status = "Rejoint un rang de la parcelle";
			return false;
		}
		void FaceFieldRow(Actor self)
		{
			var area = source.Trait<MedievalAgriculturalArea>();
			var forward = fieldCell.X < source.Location.X + area.Size.X - 1 ? 1 : -1;
			var direction = self.World.Map.CenterOfCell(fieldCell + new CVec(forward, 0)) - self.CenterPosition;
			self.Trait<Mobile>().Facing = direction.Yaw;
			Status = source.Trait<MedievalSource>().Remaining > 0 ? "Coupe les epis et lie les gerbes" : "Prepare et seme son rang";
		}
	}
}
