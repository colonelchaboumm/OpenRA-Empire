using System;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalNaturalResourceTest
	{
		static MedievalSourceInfo Info(string natural, int amount, bool renewable = false)
		{
			var info = new MedievalSourceInfo();
			FieldLoader.LoadFieldOrProperty(info, "Natural", natural);
			FieldLoader.LoadFieldOrProperty(info, "Amount", amount.ToString());
			FieldLoader.LoadFieldOrProperty(info, "Renewable", renewable.ToString());
			return info;
		}
		[Test]
		public void StoneHasAnExactFiniteBudgetIncludingTheLastPartialLoad()
		{
			var rock = new MedievalSource(Info("Stone", 350));
			var total = 0;
			for (var i = 0; i < 17; i++) total += rock.Harvest(20);
			Assert.That(rock.Remaining, Is.EqualTo(10));
			total += rock.Harvest(20);
			Assert.That(total, Is.EqualTo(350));
			Assert.That(rock.Harvest(20), Is.Zero);
			Assert.That(rock.NeedsWork, Is.False);
			Assert.That(rock.Fraction, Is.Zero);
		}
		[Test]
		public void TheFirstCutFellsATreeAndOtherWorkersWaitWithoutCreatingWood()
		{
			var tree = new MedievalSource(Info("Tree", 200));
			Assert.That(tree.StandingTree, Is.True);
			Assert.That(tree.Harvest(20), Is.EqualTo(20));
			Assert.That(tree.Falling, Is.True);
			Assert.That(tree.NeedsWork, Is.False);
			Assert.That(tree.Harvest(20), Is.Zero);
			for (var t = 0; t < tree.Info.FallTicks; t++) tree.Advance();
			Assert.That(tree.Falling || tree.StandingTree, Is.False);
			Assert.That(tree.Remaining, Is.EqualTo(180));
			Assert.That(tree.NeedsWork, Is.True);
			Assert.That(tree.Harvest(1000), Is.EqualTo(180));
			Assert.That(tree.Falling, Is.False, "A felled trunk must never fall a second time.");
		}
		[Test]
		public void NegativeRequestsCannotCreateResourcesOrTriggerAFall()
		{
			var tree = new MedievalSource(Info("Tree", 200));
			Assert.That(tree.Harvest(-50), Is.Zero);
			Assert.That(tree.Harvest(0), Is.Zero);
			Assert.That(tree.Remaining, Is.EqualTo(200));
			Assert.That(tree.StandingTree, Is.True);
		}
		[Test]
		public void ExhaustedNaturalResourcesNeverRegenerateAfterWorkOrTime()
		{
			foreach (var kind in new[] { "Tree", "Stone" })
			{
				var source = new MedievalSource(Info(kind, 200));
				source.Harvest(1000);
				for (var t = 0; t < 10000; t++) { source.Cultivate(); source.Advance(); }
				Assert.That(source.Remaining + source.Growth + source.Prepared, Is.Zero);
				Assert.That(source.NeedsWork, Is.False);
				Assert.That(source.EmptyAge, Is.GreaterThan(source.Info.ExhaustedTicks));
			}
		}
		[Test]
		public void ManagedAgricultureStillRequiresWorkAndGrowthForEachFiniteHarvest()
		{
			var crop = new MedievalSource(Info("None", 40, true));
			for (var t = 0; t < 1000; t++) crop.Advance();
			Assert.That(crop.Remaining, Is.Zero);
			crop.Cultivate();
			for (var t = 0; t < crop.Info.GrowthTicks; t++) crop.Advance();
			Assert.That(crop.Harvest(100), Is.EqualTo(40));
			Assert.That(crop.Harvest(100), Is.Zero);
			Assert.That(crop.NeedsWork, Is.True);
		}
		[Test]
		public void NaturalRulesRejectAutomaticRegenerationAndInvalidBudgets()
		{
			Assert.Throws<ArgumentException>(() => new MedievalSource(Info("Tree", 200, true)));
			Assert.Throws<ArgumentException>(() => new MedievalSource(Info("Stone", 350, true)));
			Assert.Throws<ArgumentException>(() => new MedievalSource(Info("Stone", 0)));
		}
	}
}
