using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalNationalPrideTest
	{
		static readonly NationalPrideFacts Prosperous = new(10, 100, 80, 20, 300, 0, 0);
		[Test]
		public void EveryFactorMattersAndPopulationScalesRequirements()
		{
			var state = new NationalPrideState();
			state.Advance(Prosperous, 0); Assert.That(state.Target, Is.EqualTo(100));
			state.Advance(Prosperous with { Gold = 0 }, 0); Assert.That(state.Target, Is.EqualTo(85));
			state.Advance(Prosperous with { Food = 0 }, 0); Assert.That(state.Target, Is.EqualTo(80));
			state.Advance(Prosperous with { ArmyStrength = 0 }, 0); Assert.That(state.Target, Is.EqualTo(85));
			state.Advance(Prosperous with { Hungry = 10 }, 0); Assert.That(state.Target, Is.EqualTo(80));
			state.Advance(Prosperous with { Population = 20, FoodPerMinute = 40 }, 0); Assert.That(state.Target, Is.LessThan(100));
		}
		[Test]
		public void FamineCannotBeHiddenByWealthOrTheDeathOfTheHungry()
		{
			var state = new NationalPrideState();
			state.Advance(Prosperous with { Hungry = 1, Starving = 1 }, 0);
			Assert.That(state.Target, Is.EqualTo(60));
			state.Advance(Prosperous, 1);
			Assert.That(state.Target, Is.EqualTo(50)); Assert.That(state.FamineMemory, Is.EqualTo(120));
			for (var i = 0; i < 119; i++) state.Advance(Prosperous, 1);
			Assert.That(state.Target, Is.EqualTo(50));
			state.Advance(Prosperous, 1);
			Assert.That(state.Target, Is.EqualTo(100)); Assert.That(state.Value, Is.EqualTo(51));
		}
		[Test]
		public void BurialsRestoreRespectButNeverRewardManufacturingDeaths()
		{
			var state = new NationalPrideState();
			Assert.That(state.RecordBurial(), Is.False);
			state.RecordDeath(); state.RecordDeath(); state.Advance(Prosperous, 0);
			Assert.That(state.BurialPoints, Is.Zero); Assert.That(state.Unburied, Is.EqualTo(2));
			Assert.That(state.RecordBurial(), Is.True); state.Advance(Prosperous, 0);
			Assert.That(state.BurialPoints, Is.EqualTo(5));
			Assert.That(state.RecordBurial(), Is.True); Assert.That(state.RecordBurial(), Is.False);
			state.Advance(Prosperous, 0); Assert.That(state.Target, Is.EqualTo(100));
			var otherNation = new NationalPrideState();
			Assert.That(otherNation.Deaths, Is.Zero); Assert.That(otherNation.Burials, Is.Zero);
		}
		[Test]
		public void PrideMovesGraduallyAndItsCombatEffectIsBounded()
		{
			var state = new NationalPrideState(); var morale = new MoraleState();
			state.Advance(Prosperous, 0); Assert.That(state.Value, Is.EqualTo(51));
			for (var i = 0; i < 100; i++) state.Advance(Prosperous, 0);
			Assert.That(state.Value, Is.EqualTo(100)); Assert.That(state.MoraleBonus, Is.EqualTo(10));
			for (var i = 0; i < 100; i++) morale.Advance(false, false, 0, 0, state.MoraleBonus);
			Assert.That(morale.Value, Is.EqualTo(80)); Assert.That(morale.Firepower, Is.EqualTo(105));
			state.RecordDeath();
			state.Advance(new(10, 0, 0, 20, 0, 10, 10), 1); Assert.That(state.Value, Is.EqualTo(98));
			for (var i = 0; i < 100; i++) state.Advance(new(10, 0, 0, 20, 0, 10, 10), 1);
			Assert.That(state.Value, Is.Zero); Assert.That(state.MoraleBonus, Is.EqualTo(-10));
			for (var i = 0; i < 100; i++) morale.Advance(true, true, 0, 0, state.MoraleBonus);
			Assert.That(morale.Value, Is.EqualTo(10));
		}
		[Test]
		public void ArmyStrengthUsesHealthAndFoodRatherThanMoraleFeedback()
		{
			Assert.That(NationalPrideState.Strength(10000, false, false), Is.EqualTo(100));
			Assert.That(NationalPrideState.Strength(5000, false, false), Is.EqualTo(50));
			Assert.That(NationalPrideState.Strength(10000, true, false), Is.EqualTo(50));
			Assert.That(NationalPrideState.Strength(10000, true, true), Is.Zero);
		}
		[Test]
		public void ExtremeInputsRemainBoundedIncludingAnEmptyNation()
		{
			var state = new NationalPrideState();
			state.Advance(new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue), 0);
			Assert.That(state.Target, Is.InRange(0, 100));
			state.Advance(default, 0); Assert.That(state.Target, Is.EqualTo(50));
		}
	}
}
