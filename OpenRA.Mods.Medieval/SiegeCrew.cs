using System;
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed partial class MedievalSiegeCrew
	{
		public readonly Inventory StoneCargo = new([0, 0, 0, SiegeRules.StonePerShot]);
		SiegeLoadingTrip loading;
		TradeShipment returning;
		Actor returnStore;
		[VerifySync] int handling, returnAttempts, moveAttempts;
		[VerifySync] public int Deliveries { get; private set; }
		[VerifySync] public int CargoHash => StoneCargo.Hash;
		[VerifySync] public int LoadingHash => unchecked((loading?.Hash ?? 0) * 31 + (returning?.Hash ?? 0) + (int)(returnStore?.ActorID ?? 0));
		public int Cargo => StoneCargo.Amount(3);
		public bool Working { get; private set; }
		public bool Handling => handling > 0;
		public int RamStroke(Actor self) => Assigned && !Trebuchet && Near(self) && engine.Trait<MedievalSiegeEngine>().AttackTicks > 0
			? RamMotion.FireTicks - engine.Trait<MedievalSiegeEngine>().AttackTicks : -1;
		public string Description => !Assigned ? Cargo > 0 ? "Pierre conservee : retour au stockage des que disponible." : "Choisissez un engin a servir."
			: !Trebuchet ? engine?.Trait<MedievalSiegeEngine>().Info.Cauldron == true ? "Pousse le chaudron." : "Pousse le belier." : Cargo > 0 ? "Porte le projectile jusqu'au trebuchet."
			: loading?.AwaitingPickup == true ? "Va prendre une pierre sur la charrette." : Working ? "Aide aux manoeuvres et au treuil." : "Rejoint son poste autour du trebuchet.";
		void CancelLoading()
		{
			loading?.Cancel(); loading = null; returning?.Cancel(); returning = null; returnStore = null;
			handling = moveAttempts = returnAttempts = 0; Working = Pushing = false;
		}
		// Same ground projection as the native art: f = forward, s = sideways, in pixels.
		public static WPos ModelPoint(Actor engine, int f, int s)
		{
			var scale = engine.World.Map.Grid.TileScale;
			var width = engine.World.Map.Rules.TerrainInfo.TileSize.Width;
			var angle = new WAngle(Util.IndexFacing(engine.Trait<Mobile>().Facing, 8) * 128);
			return engine.CenterPosition + new WVec((angle.Cos() * s - angle.Sin() * f) * scale / (1024 * width),
				(-angle.Cos() * f - angle.Sin() * s) * scale / (1024 * width), 0);
		}
		bool ReachPost(Actor self, WPos point)
		{
			var mobile = self.Trait<Mobile>();
			// Stay outside the real 3x3 footprint even while packed, so crew never block deployment.
			var cells = SiegeRules.Braces(engine.Location).SelectMany(c => new[] { c + new CVec(-1, 0), c + new CVec(1, 0), c + new CVec(0, -1), c + new CVec(0, 1) })
				.Distinct().Where(c => c != engine.Location && !SiegeRules.Braces(engine.Location).Contains(c)
					&& self.World.Map.Contains(c) && mobile.CanEnterCell(c, check: BlockedByActor.Immovable))
				.OrderBy(c => (self.World.Map.CenterOfCell(c) - point).HorizontalLengthSquared).ThenBy(c => c.Y).ThenBy(c => c.X).Take(3).ToArray();
			if (cells.Length == 0) return false;
			if (cells.Contains(self.Location)) { moveAttempts = 0; mobile.Facing = (engine.CenterPosition - self.CenterPosition).Yaw; return true; }
			// Retry another accessible side after a failed move, without treating distance as arrival.
			if (moveAttempts >= cells.Length) moveAttempts = 0;
			var cell = cells[moveAttempts++];
			if (self.Location == cell) { moveAttempts = 0; return true; }
			self.QueueActivity(mobile.MoveTo(cell)); return false;
		}
		void TickTrebuchet(Actor self)
		{
			var siege = engine.Trait<MedievalSiegeEngine>();
			if (!self.IsIdle) return;
			if (siege.Deployed && loading == null && siege.Sling.Free(3) >= SiegeRules.StonePerShot)
				loading = SiegeLoadingTrip.Book(engine.Trait<MedievalStockpile>().Stock, StoneCargo, siege.Sling);
			if (loading != null)
			{
				var pickup = loading.AwaitingPickup;
				var point = ModelPoint(engine, pickup ? -38 : 46, pickup ? -36 : 20);
				if (!ReachPost(self, point)) { handling = 0; return; }
				Working = true; handling += Math.Max(1, siege.WorkPercent);
				if (handling < 1400) return;
				handling = moveAttempts = 0;
				if (pickup) loading.PickUp();
				else { if (loading.Deliver() > 0) Deliveries++; loading = null; }
				return;
			}
			if (--cadence > 0) { Working = Near(self) && (siege.Busy || siege.Deployed && siege.LoadProgress < SiegeRules.LoadingTicks); return; }
			cadence = 5;
			var slot = Array.IndexOf(siege.Crew.ToArray(), self);
			var post = ModelPoint(engine, slot / 2 == 0 ? -24 : 30, slot % 2 == 0 ? -42 : 42);
			if (ReachPost(self, post)) Working = siege.Busy || siege.Deployed && siege.LoadProgress < SiegeRules.LoadingTicks;
		}
		void ReturnStone(Actor self)
		{
			if (Cargo == 0 || !self.IsIdle || --cadence > 0) return; cadence = 8;
			if (returnStore != null && !SiegeRules.Live(returnStore, self.Owner)) { returning?.Cancel(); returning = null; returnStore = null; }
			if (returnStore == null)
			{
				returnStore = MedievalStoneCart.Stores(self.Owner).Where(a => a.Trait<MedievalStockpile>().Stock.Free(3) >= Cargo)
					.OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
				if (returnStore == null) return;
				returning = new TradeShipment(StoneCargo, returnStore.Trait<MedievalStockpile>().Stock); returning.BookCarriedCargo();
			}
			if (!MedievalLogistics.Approach(self, returnStore, ref returnAttempts)) return;
			returning.Unload(); returning = null; returnStore = null;
		}
	}
}
