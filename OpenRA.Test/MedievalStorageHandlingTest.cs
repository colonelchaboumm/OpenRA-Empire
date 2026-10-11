using System;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalStorageHandlingTest
	{
		[TestCase(false)]
		[TestCase(true)]
		public void ATransferChangesOnlyThePileTouchedByTheWorker(bool pickup)
		{
			StorageZone[] zones = [new(1, new(new CPos(1, 2), new CVec(4, 4)), StorageKind.Mixed),
				new(2, new(new CPos(8, 2), new CVec(2, 2)), StorageKind.Wood), new(3, new(new CPos(8, 8), new CVec(3, 2)), StorageKind.Equipment),
				new(4, new(new CPos(12, 8), new CVec(2, 2)), StorageKind.Gold), new(5, new(new CPos(12, 12), new CVec(2, 2)), StorageKind.Camp)];
			// Include a partly scorched last pallet, zone boundaries and indoor overflow.
			zones[0].Scorch(0, 37); zones[4].Scorch(9, 3);
			for (var good = 0; good < StorageGoods.Count; good++)
				for (var total = 1; total <= zones.Sum(z => z.Capacity(good)) + 30; total++)
				{
					var contact = StorageHandling.Plan(zones, good, total, 10, pickup, CPos.Zero);
					Assert.That(contact.Amount, Is.InRange(1, 10));
					var after = total + (pickup ? -contact.Amount : contact.Amount);
					int[] Piles(int n) => zones.SelectMany(z => z.Stacks(g => g == good ? StorageAllocation.Outside(zones, z.Id, g, n) : 0))
						.Where(s => s.Good == good).Select(s => s.Amount).Append(StorageAllocation.Inside(zones, good, n)).ToArray();
					var beforePiles = Piles(total); var afterPiles = Piles(after);
					var changes = beforePiles.Zip(afterPiles, (a, b) => b - a).Where(n => n != 0).ToArray();
					Assert.That(changes, Is.EqualTo(new[] { pickup ? -contact.Amount : contact.Amount }));
					Assert.That(afterPiles.Sum(), Is.EqualTo(after));
					if (contact.Outside)
					{
						var zone = zones.Single(z => z.Id == contact.ZoneId);
						int At(int n) => zone.Stacks(g => StorageAllocation.Outside(zones, zone.Id, g, n)).Single(s => s.Cell == contact.Cell).Amount;
						Assert.That(At(after) - At(total), Is.EqualTo(changes[0]));
					}
				}
		}
		[Test]
		public void EmptyAndFullPalletsRouteToTheNextPhysicalLocation()
		{
			StorageZone[] zones = [new(1, new(new CPos(5, 5), new CVec(2, 2)), StorageKind.Wood)];
			Assert.That(StorageHandling.Plan(zones, 0, 0, 10, true, CPos.Zero).Amount, Is.Zero);
			Assert.That(StorageHandling.Plan(zones, 0, 97, 10, false, CPos.Zero), Is.EqualTo(new StorageContact(1, new CPos(5, 5), 3)));
			Assert.That(StorageHandling.Plan(zones, 0, 100, 7, false, CPos.Zero), Is.EqualTo(new StorageContact(1, new CPos(6, 5), 7)));
			Assert.That(StorageHandling.Plan(zones, 0, 102, 10, true, CPos.Zero), Is.EqualTo(new StorageContact(1, new CPos(6, 5), 2)));
			Assert.That(StorageHandling.Plan(zones, 0, 400, 10, false, CPos.Zero).Outside, Is.False);
			Assert.That(StorageHandling.Plan(zones, 0, 405, 10, true, CPos.Zero), Is.EqualTo(new StorageContact(0, CPos.Zero, 5)));
		}
	}
}
