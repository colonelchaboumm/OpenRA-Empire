using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalAnimationTest
	{
		[Test]
		public void FootCycleFollowsDistanceEvenWhenSpeedChanges()
		{
			var normal = new MedievalGait();
			var hungry = new MedievalGait();
			for (var i = 0; i < 7; i++) normal.Advance(64);
			for (var i = 0; i < 14; i++) hungry.Advance(32);
			Assert.That(hungry.Frame, Is.EqualTo(normal.Frame));
			Assert.That(normal.Frame, Is.GreaterThan(0));
		}
		[Test]
		public void WaitingAndTeleportingDoNotAdvanceTheGait()
		{
			var gait = new MedievalGait();
			gait.Advance(300);
			var before = gait.Frame;
			for (var i = 0; i < 100; i++) gait.Advance(0);
			gait.Advance(20000);
			Assert.That(gait.Frame, Is.EqualTo(before));
		}
		[Test]
		public void ContinuousWalkingLoopsThroughEveryPoseWithoutOverflow()
		{
			var gait = new MedievalGait();
			for (var i = 0; i < 80000; i++)
			{
				Assert.That(gait.Frame, Is.EqualTo(i % 8));
				gait.Advance(MedievalGait.CycleDistance / 8);
			}
		}
	}
}
