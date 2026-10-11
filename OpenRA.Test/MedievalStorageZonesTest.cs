using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalStorageZonesTest
	{
		[Test]
		public void EnlargingASpecializedZoneAddsOnlyItsOwnCommodityCapacity()
		{
			var small = new StorageZone(1, new(new CPos(1, 2), new CVec(2, 2)), StorageKind.Wood);
			var big = new StorageZone(1, new(new CPos(1, 2), new CVec(4, 6)), StorageKind.Wood);
			Assert.That(small.Capacity(0), Is.EqualTo(400)); Assert.That(big.Capacity(0), Is.EqualTo(2400));
			for (var good = 1; good < StorageGoods.Count; good++) Assert.That(big.Capacity(good), Is.Zero);
		}
		[Test]
		public void IndoorAndAllExteriorStacksAlwaysSumToThePhysicalInventory()
		{
			StorageZone[] zones = [new(1, new(new CPos(1, 2), new CVec(3, 5)), StorageKind.Mixed),
				new(2, new(new CPos(8, 2), new CVec(2, 3)), StorageKind.Wood), new(3, new(new CPos(8, 8), new CVec(2, 2)), StorageKind.Equipment),
				new(4, new(new CPos(12, 8), new CVec(2, 3)), StorageKind.Gold), new(5, new(new CPos(12, 12), new CVec(2, 2)), StorageKind.Camp)];
			for (var total = 0; total <= 2000; total += 37)
				for (var good = 0; good < StorageGoods.Count; good++)
				{
					var outside = zones.Sum(z => z.Stacks(g => StorageAllocation.Outside(zones, z.Id, g, total)).Where(s => s.Good == good).Sum(s => s.Amount));
					Assert.That(outside + StorageAllocation.Inside(zones, good, total), Is.EqualTo(total));
					Assert.That(zones.SelectMany(z => z.Stacks(g => StorageAllocation.Outside(zones, z.Id, g, total))).All(s => s.Amount <= StorageGoods.Limit(s.Good)), Is.True);
				}
		}
		[Test]
		public void BurningAPalletCannotRemoveGoodsFromOtherZones()
		{
			StorageZone[] zones = [new(1, new(new CPos(1, 2), new CVec(2, 2)), StorageKind.Wood), new(2, new(new CPos(8, 2), new CVec(2, 2)), StorageKind.Wood)];
			var total = 950;
			var other = StorageAllocation.Outside(zones, 2, 0, total); var indoor = StorageAllocation.Inside(zones, 0, total);
			zones[0].Scorch(0, 37); total -= 37;
			Assert.That(StorageAllocation.Outside(zones, 1, 0, total), Is.EqualTo(363));
			Assert.That(StorageAllocation.Outside(zones, 2, 0, total), Is.EqualTo(other));
			Assert.That(StorageAllocation.Inside(zones, 0, total), Is.EqualTo(indoor));
			Assert.That(zones[0].Damaged, Is.True);
		}
		[Test]
		public void ShrinkingStorageProtectsPhysicalGoodsAndBookedDeliveries()
		{
			var stock = new Inventory([100, 0, 0, 0], [80, 0, 0, 0]);
			stock.ReserveIn(0, 15); stock.ReserveOut(0, 30);
			var hash = stock.Hash;
			Assert.Throws<InvalidOperationException>(() => stock.SetCapacity([90, 0, 0, 0, 0]));
			Assert.That(stock.Hash, Is.EqualTo(hash));
			stock.SetCapacity([95, 0, 0, 0, 0]); stock.Unload(0, 15); stock.Load(0, 30);
			Assert.That(stock.Amount(0), Is.EqualTo(65)); Assert.That(stock.Free(0), Is.EqualTo(30));
			stock.SetCapacity([500, 0, 0, 0, 0]); Assert.That(stock.Amount(0), Is.EqualTo(65));
			Assert.That(stock.Add(0, 999), Is.EqualTo(435));
		}
		[Test]
		public void FireSmokeIsBoundedAdvectsInWindAndDissipatesAfterExtinguishing()
		{
			var smoke = new FireSmoke();
			for (var i = 0; i < 2000; i++) smoke.Tick(new Vector2(1, .2f), true, 30, 17);
			Assert.That(smoke.Clouds.Count, Is.InRange(20, FireSmoke.MaxClouds));
			Assert.That(smoke.Clouds.All(c => c.Height >= 30 && c.Opacity is >= 0 and <= 1), Is.True);
			Assert.That(smoke.Clouds.Any(c => c.Drift.X > 30), Is.True);
			for (var i = 0; i < FireSmoke.Lifetime; i++) smoke.Tick(Vector2.Zero, false, 30, 17);
			Assert.That(smoke.Clouds, Is.Empty);
		}
		[Test]
		public void FallingTreesRetainVolumeAndLandAlongGroundAxes()
		{
			foreach (var direction in new[] { -1, 1 })
			{
				for (var frame = 0; frame <= 100; frame++)
				{
					var phase = frame / 100f;
					Assert.That(NatureGeometry.Fall(Vector3.Zero, phase, direction), Is.EqualTo(Vector3.Zero));
					var x = NatureGeometry.Fall(Vector3.UnitX, phase, direction);
					var y = NatureGeometry.Fall(Vector3.UnitY, phase, direction);
					Assert.That(x.X * y.Y - x.Y * y.X, Is.GreaterThan(.54f), "The crown silhouette must never collapse into a line.");
				}
				var tip = NatureGeometry.Fall(new Vector3(0, -100, 0), 1, direction);
				Assert.That(tip.Y, Is.EqualTo(Math.Abs(tip.X) * .5f).Within(.001f));
			}
		}
		[Test]
		public void GroundAxesRemainTwoToOneAndWindDoesNotDisplaceRoots()
		{
			Assert.That(NatureGeometry.Ground(2, 0), Is.EqualTo(new Vector3(48, 24, 0)));
			Assert.That(NatureGeometry.Ground(0, 2), Is.EqualTo(new Vector3(-48, 24, 0)));
			foreach (var sway in new[] { -5f, 0f, 5f })
			{
				Assert.That(NatureGeometry.Bend(Vector3.Zero, 100, sway), Is.EqualTo(Vector3.Zero));
				Assert.That(NatureGeometry.Bend(new Vector3(4, -5, 0), 100, sway), Is.EqualTo(new Vector3(4, -5, 0)));
			}
		}
	}
}
