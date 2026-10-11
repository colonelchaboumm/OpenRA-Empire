using NUnit.Framework;
using OpenRA.Mods.Medieval;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalMoraleTest
	{
		[TestCase(false, false, 0, 0, 70, 100)]
		[TestCase(true, false, 0, 0, 40, 85)]
		[TestCase(true, true, 0, 0, 20, 75)]
		[TestCase(false, false, 20, 0, 90, 110)]
		[TestCase(false, false, 0, 10, 60, 95)]
		[TestCase(true, false, 20, 0, 60, 95)]
		[TestCase(false, false, 20, 10, 80, 105)]
		public void FoodAndMusicSetDistinctCombatStrength(bool hungry, bool starving, int friendly, int hostile, int morale, int damage)
		{
			var state = new MoraleState();
			for (var i = 0; i < 100; i++) state.Advance(hungry, starving, friendly, hostile);
			Assert.That(state.Value, Is.EqualTo(morale));
			Assert.That(state.Firepower, Is.EqualTo(damage));
		}
		[Test]
		public void RecoveryIsGradualAndDoesNotPreserveAnExpiredBonus()
		{
			var state = new MoraleState();
			state.Advance(true, false, 0, 0);
			Assert.That(state.Value, Is.EqualTo(68));
			for (var i = 0; i < 20; i++) state.Advance(true, false, 0, 0);
			state.Advance(false, false, 0, 0);
			Assert.That(state.Value, Is.EqualTo(41));
			for (var i = 0; i < 100; i++) state.Advance(false, false, 20, 0);
			for (var i = 0; i < 100; i++) state.Advance(false, false, 0, 0);
			Assert.That(state.Value, Is.EqualTo(70));
		}
		[Test]
		public void MultipleDrummersDoNotStackAndNeutralMusicDoesNotAffectTroops()
		{
			var music = MoraleState.Music([(PlayerRelationship.Ally, false, false), (PlayerRelationship.Ally, false, false),
				(PlayerRelationship.Enemy, false, false), (PlayerRelationship.Enemy, false, false), (PlayerRelationship.Neutral, false, false)]);
			Assert.That(music, Is.EqualTo((20, 10)));
			Assert.That(MoraleState.Music([(PlayerRelationship.Neutral, false, false)]), Is.EqualTo((0, 0)));
		}
		[Test]
		public void HungryMusiciansHaveHalfStrengthAndStarvingMusiciansAreSilent()
		{
			Assert.That(MoraleState.Music([(PlayerRelationship.Ally, true, false), (PlayerRelationship.Enemy, true, false)]), Is.EqualTo((10, 5)));
			Assert.That(MoraleState.Music([(PlayerRelationship.Ally, true, true), (PlayerRelationship.Enemy, true, true)]), Is.EqualTo((0, 0)));
		}
	}
}
