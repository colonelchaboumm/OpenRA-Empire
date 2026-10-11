using System;

namespace OpenRA.Mods.Medieval
{
	// A projectile remains in exactly one place: wagon, soldier, sling, then fired.
	public sealed class SiegeLoadingTrip
	{
		readonly Inventory source, cargo, destination;
		bool awaitingPickup, booked;
		public bool AwaitingPickup => awaitingPickup;
		public int Hash => (awaitingPickup ? 1 : 0) + (booked ? 2 : 0);
		SiegeLoadingTrip(Inventory source, Inventory cargo, Inventory destination, bool pickup)
		{ this.source = source; this.cargo = cargo; this.destination = destination; awaitingPickup = pickup; booked = true; }
		public static SiegeLoadingTrip Book(Inventory source, Inventory cargo, Inventory destination)
		{
			var n = SiegeRules.StonePerShot;
			var pickup = cargo.Amount(3) == 0;
			if ((!pickup && cargo.Amount(3) != n) || !destination.ReserveIn(3, n)) return null;
			if (pickup && (cargo.Free(3) < n || !source.ReserveOut(3, n)))
			{ destination.ReleaseIn(3, n); return null; }
			return new SiegeLoadingTrip(source, cargo, destination, pickup);
		}
		public void PickUp()
		{
			if (!booked || !awaitingPickup) return;
			source.Load(3, SiegeRules.StonePerShot); cargo.Add(3, SiegeRules.StonePerShot); awaitingPickup = false;
		}
		public int Deliver()
		{
			if (!booked || awaitingPickup) return 0;
			var n = cargo.Take(3, SiegeRules.StonePerShot);
			destination.Unload(3, n);
			if (n < SiegeRules.StonePerShot) destination.ReleaseIn(3, SiegeRules.StonePerShot - n);
			booked = false; return n;
		}
		public void Cancel()
		{
			if (!booked) return;
			if (awaitingPickup) source.ReleaseOut(3, SiegeRules.StonePerShot);
			destination.ReleaseIn(3, SiegeRules.StonePerShot); booked = awaitingPickup = false;
		}
	}
}
