using System;
using System.Linq;

namespace OpenRA.Mods.Medieval
{
	// Resource order is part of the YAML format and of the deterministic state.
	public enum Material { Wood, Food, Meat, Stone, Gold }

	public sealed class Inventory
	{
		public const int ResourceCount = 5;
		readonly int[] amount = new int[ResourceCount];
		readonly int[] outgoing = new int[ResourceCount];
		readonly int[] incoming = new int[ResourceCount];
		readonly int[] capacity;
		public Inventory(int[] capacity, int[] initial = null)
		{
			if (capacity.Length is not (4 or ResourceCount) || capacity.Any(n => n < 0))
				throw new ArgumentException("An inventory needs four legacy or five non-negative capacities.");
			this.capacity = new int[ResourceCount];
			Array.Copy(capacity, this.capacity, capacity.Length);
			if (initial != null)
			{
				if (initial.Length is not (4 or ResourceCount) || initial.Where((n, r) => n < 0 || n > this.capacity[r]).Any())
					throw new ArgumentException("Initial contents exceed capacity.");
				Array.Copy(initial, amount, initial.Length);
			}
		}
		public int Amount(int r) => amount[r];
		public int Available(int r) => amount[r] - outgoing[r];
		public int Reserved(int r) => outgoing[r];
		public int Incoming(int r) => incoming[r];
		public int Capacity(int r) => capacity[r];
		public int Free(int r) => capacity[r] - amount[r] - incoming[r];
		public bool CanSetCapacity(int[] next) => next.Length == ResourceCount
			&& Enumerable.Range(0, ResourceCount).All(r => next[r] >= amount[r] + incoming[r]);
		public void SetCapacity(int[] next)
		{
			if (!CanSetCapacity(next)) throw new InvalidOperationException("Storage must fit physical goods and booked arrivals.");
			Array.Copy(next, capacity, ResourceCount);
		}
		public bool ReserveOut(int r, int n)
		{
			if (n <= 0 || Available(r) < n) return false;
			outgoing[r] += n;
			return true;
		}
		public bool ReserveIn(int r, int n)
		{
			if (n <= 0 || Free(r) < n) return false;
			incoming[r] += n;
			return true;
		}
		public void ReleaseOut(int r, int n)
		{
			if (n < 0 || n > outgoing[r]) throw new InvalidOperationException("Invalid departure reservation.");
			outgoing[r] -= n;
		}
		public void ReleaseIn(int r, int n)
		{
			if (n < 0 || n > incoming[r]) throw new InvalidOperationException("Invalid arrival reservation.");
			incoming[r] -= n;
		}
		public void Load(int r, int n) { ReleaseOut(r, n); amount[r] -= n; }
		public void Unload(int r, int n) { ReleaseIn(r, n); amount[r] += n; }
		public int Take(int r, int n)
		{
			var result = Math.Min(Math.Max(n, 0), Available(r));
			amount[r] -= result;
			return result;
		}
		public int Add(int r, int n)
		{
			var result = Math.Min(Math.Max(n, 0), Free(r));
			amount[r] += result;
			return result;
		}
		public int Hash
		{
			get
			{
				var hash = 17;
				for (var r = 0; r < ResourceCount; r++)
					unchecked { hash = (((hash * 31 + amount[r]) * 31 + outgoing[r]) * 31 + incoming[r]) * 31 + capacity[r]; }
				return hash;
			}
		}
	}
}
