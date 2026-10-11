using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalLivestockTest
	{
		[TestCase(2, 2)]
		[TestCase(2, 10)]
		[TestCase(10, 2)]
		[TestCase(10, 10)]
		public void EntireBodyStaysInsideSmallAndLargeEnclosuresWithoutTeleporting(int w, int h)
		{
			var size = new CVec(w, h);
			for (var animal = 0; animal < Math.Max(1, w * h / 4); animal++)
			{
				var previous = LivestockMotion.Sample(size, 718, animal, 0);
				for (var tick = 1; tick < 2300; tick++)
				{
					var pose = LivestockMotion.Sample(size, 718, animal, tick);
					Assert.That(pose.Position.X, Is.InRange(.67f, w - .67f));
					Assert.That(pose.Position.Y, Is.InRange(.67f, h - .67f));
					Assert.That(Vector2.Distance(pose.Position, previous.Position), Is.LessThan(.06f));
					Assert.That(pose.Frame, Is.InRange(0, 127)); previous = pose;
				}
			}
		}
		[Test]
		public void HerdWandersAcrossThePenWithIndependentWalksAndGrazingPauses()
		{
			var samples = Enumerable.Range(0, 3000).Select(t => LivestockMotion.Sample(new CVec(8, 6), 92, 0, t)).ToArray();
			Assert.That(samples.Any(p => p.Moving) && samples.Any(p => !p.Moving), Is.True);
			Assert.That(samples.Max(p => p.Position.X) - samples.Min(p => p.Position.X), Is.GreaterThan(2));
			Assert.That(samples.Select(p => p.Facing).Distinct().Count(), Is.GreaterThan(2));
			Assert.That(Enumerable.Range(0, 12).Select(i => LivestockMotion.Sample(new CVec(8, 6), 92, i, 700).Position).Distinct().Count(), Is.EqualTo(12));
		}
		[Test]
		public void BreedingNeedsTwoAdultsAndReservesSpaceForIncomingAnimals()
		{
			var clock = new LivestockBreedingClock();
			for (var i = 0; i < 100; i++) Assert.That(clock.Advance(1, 2, 0, 4, 10, true), Is.False);
			for (var i = 0; i < 9; i++) Assert.That(clock.Advance(2, 2, 0, 4, 10, true), Is.False);
			Assert.That(clock.Advance(2, 2, 0, 4, 10, true), Is.True);
			for (var i = 0; i < 100; i++) Assert.That(clock.Advance(2, 2, 2, 4, 10, true), Is.False);
			Assert.That(clock.Progress, Is.Zero);
		}
		[Test]
		public void LosingAParentOrClosingAPenResetsBirthProgress()
		{
			var clock = new LivestockBreedingClock();
			for (var i = 0; i < 9; i++) clock.Advance(2, 2, 0, 4, 10, true);
			Assert.That(clock.Advance(1, 1, 0, 4, 10, true), Is.False);
			Assert.That(clock.Progress, Is.Zero);
			Assert.That(clock.Advance(2, 2, 0, 4, 10, true), Is.False);
			Assert.That(clock.Advance(2, 2, 0, 4, 10, false), Is.False);
			Assert.That(clock.Progress, Is.Zero);
		}
	}
}
