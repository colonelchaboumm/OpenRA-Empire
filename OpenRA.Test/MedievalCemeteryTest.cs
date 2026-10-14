using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalCemeteryTest
	{
		[Test]
		public void GraveReservationsPreventDuplicateBurialsAndForeignCancellation()
		{
			var plot = new CemeteryPlot(CPos.Zero, null);
			Assert.That(plot.Reserve(0), Is.False);
			Assert.That(plot.Reserve(12), Is.True);
			Assert.That(plot.Reserve(13), Is.False);
			plot.Release(13);
			Assert.That(plot.ReservedBy, Is.EqualTo(12));
			Assert.That(plot.Bury(13, 25), Is.False);
			Assert.That(plot.Bury(12, -1), Is.False);
			Assert.That(plot.Bury(12, 25), Is.True);
			Assert.That(plot.ReservedBy, Is.Zero);
			Assert.That(plot.BodyId, Is.EqualTo(25));
			Assert.That(plot.Reserve(13), Is.False);
			Assert.That(plot.Bury(12, 26), Is.False);
			plot.Release(12);
			Assert.That(plot.Buried, Is.True);
		}
		[Test]
		public void InterruptedJobMakesItsEmptyGraveAvailableAgain()
		{
			var plot = new CemeteryPlot(CPos.Zero, null);
			plot.Reserve(12); plot.Release(12);
			Assert.That(plot.Free, Is.True);
			Assert.That(plot.Reserve(13), Is.True);
			Assert.That(plot.Bury(13, 25), Is.True);
		}
		[Test]
		public void CorpseSynchronizationTracksGameplayButNotLocalGraphics()
		{
			var scene = new MedievalBattlefield();
			var body = new BattlefieldRemains { Seed = 25, Position = new WPos(100, 200, 0), Age = 30 };
			scene.Add(body); var before = scene.RemainsHash;
			body.Palette = "another-player"; body.Scale = 2;
			Assert.That(scene.RemainsHash, Is.EqualTo(before));
			body.ClaimedBy = 12;
			Assert.That(scene.RemainsHash, Is.Not.EqualTo(before)); before = scene.RemainsHash;
			body.Carrier = 12;
			Assert.That(scene.RemainsHash, Is.Not.EqualTo(before)); before = scene.RemainsHash;
			body.Position = new WPos(200, 200, 0);
			Assert.That(scene.RemainsHash, Is.Not.EqualTo(before)); before = scene.RemainsHash;
			scene.Remove(body);
			Assert.That(scene.RemainsHash, Is.Not.EqualTo(before));
		}
		[Test]
		public void LongBattlesCannotEvictAReservedOrCarriedCorpse()
		{
			var scene = new MedievalBattlefield();
			var reserved = new BattlefieldRemains { Seed = 5, ClaimedBy = 12 };
			var carried = new BattlefieldRemains { Seed = 6, ClaimedBy = 13, Carrier = 13 };
			scene.Add(reserved); scene.Add(carried);
			for (var i = 0; i < MedievalBattlefield.MaximumRemains + 10; i++) scene.Add(new BattlefieldRemains { Seed = i + 20 });
			Assert.That(scene.Remains.Count, Is.EqualTo(MedievalBattlefield.MaximumRemains));
			Assert.That(scene.Remains.Contains(reserved) && scene.Remains.Contains(carried), Is.True);
			Assert.That(scene.Remove(carried), Is.True);
			Assert.That(scene.Remove(carried), Is.False);
			Assert.That(scene.Remains.Contains(reserved), Is.True);
		}
	}
}
