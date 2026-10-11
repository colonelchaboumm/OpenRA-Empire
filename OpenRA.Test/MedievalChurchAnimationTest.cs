using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalChurchAnimationTest
	{
		[Test]
		public void BellStrikesAtTheSwingAndThenFallsSilent()
		{
			var bell = new ChurchBellMotion();
			bell.Ring(3);
			var sounded = 0;
			for (var i = 1; i <= 400; i++)
			{
				var strike = bell.Tick();
				Assert.That(strike, Is.EqualTo(i is 12 or 62 or 112));
				if (strike) { sounded++; Assert.That(bell.Swing, Is.GreaterThan(.60f)); }
			}
			Assert.That(sounded, Is.EqualTo(3));
			Assert.That(bell.Ringing, Is.False);
			Assert.That(bell.Swing, Is.Zero);
		}
		[Test]
		public void OverlappingRecruitmentDoesNotRestartTheClapperOrAccumulateRinging()
		{
			var bell = new ChurchBellMotion(); bell.Ring(1);
			for (var i = 0; i < 12; i++) bell.Tick();
			bell.Ring(999);
			Assert.That(bell.Phase, Is.EqualTo(12));
			for (var i = 0; i < 400; i++) bell.Tick();
			Assert.That(bell.Strikes, Is.EqualTo(3));
			bell.Ring(2); bell.Stop();
			for (var i = 0; i < 400; i++) Assert.That(bell.Tick(), Is.False);
		}
		[Test]
		public void SortingStartsOnlyForRealPositiveGoldDeliveries()
		{
			var mine = new MedievalGoldWorkshopAnimation();
			mine.Receive((int)Material.Stone, 10);
			mine.Receive((int)Material.Gold, 0);
			Assert.That(mine.Operating, Is.False);
			mine.Receive((int)Material.Gold, 5);
			Assert.That(mine.Operating, Is.True);
		}
	}
}
