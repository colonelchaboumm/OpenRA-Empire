using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalThreatInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalThreat();
	}
	public sealed class MedievalThreat : INotifyDamage, INotifyOwnerChanged, ISync
	{
		Actor aggressor;
		[VerifySync] int lastHit = -1;
		[VerifySync] public int AggressorId => unchecked((int)(aggressor?.ActorID ?? 0));
		public Actor RecentAttacker(Actor observer) => lastHit >= 0 && observer.World.WorldTick - lastHit <= 150
			&& aggressor?.IsInWorld == true && !aggressor.IsDead && !aggressor.WillDispose
			&& aggressor.AppearsHostileTo(observer) && aggressor.CanBeViewedByPlayer(observer.Owner) ? aggressor : null;
		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (e.Damage.Value <= 0 || e.Attacker == null || e.Attacker.Disposed || e.Attacker.AppearsFriendlyTo(self)) return;
			var attacker = e.Attacker;
			if (!attacker.IsInWorld) attacker = attacker.TraitOrDefault<Passenger>()?.Transport;
			if (attacker?.IsInWorld != true || attacker.IsDead) return;
			aggressor = attacker; lastHit = self.World.WorldTick;
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { aggressor = null; lastHit = -1; }
	}
}
