using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalActionParticlesTest
	{
		static VillageActionBurst Burst(VillageActionKind kind) => new(WPos.Zero, kind, Vector2.UnitX, Vector2.Zero, 41);
		[Test]
		public void DenseArmyCannotExceedEmissionOrLiveEffectBudgets()
		{
			var scene = new VillageActionScene();
			for (var tick = 0; tick < 200; tick++)
			{
				scene.Advance(1);
				var accepted = 0;
				for (var i = 0; i < 2000; i++) if (scene.Emit(Burst(VillageActionKind.Snow))) accepted++;
				Assert.That(accepted, Is.LessThanOrEqualTo(VillageActionScene.MaximumPerTick));
				Assert.That(scene.Bursts.Count, Is.LessThanOrEqualTo(VillageActionScene.MaximumBursts));
			}
			scene.Advance(1000);
			Assert.That(scene.Bursts, Is.Empty, "A camera jump or a long render gap must expire every old effect.");
			Assert.That(scene.Emit(Burst(VillageActionKind.Wood)), Is.True, "A saturated scene must recover its capacity.");
		}
		[Test]
		public void PauseKeepsPositionsAgesAndEmissionBudget()
		{
			var scene = new VillageActionScene();
			for (var i = 0; i < VillageActionScene.MaximumPerTick; i++) scene.Emit(Burst(VillageActionKind.Stone));
			scene.Advance(0);
			Assert.That(scene.Bursts.All(b => b.Age == 0 && b.Position == WPos.Zero), Is.True);
			Assert.That(scene.Emit(Burst(VillageActionKind.Dust)), Is.False);
		}
		[Test]
		public void GroundEffectsRespectSnowMeltRainAndPaving()
		{
			Assert.That(VillageActionScene.Footstep("Clear", .8f, 800), Is.EqualTo(VillageActionKind.Snow));
			Assert.That(VillageActionScene.Footstep("Clear", 0, 800), Is.EqualTo(VillageActionKind.Splash));
			Assert.That(VillageActionScene.Footstep("Clear", 0, 0), Is.EqualTo(VillageActionKind.Dust));
			Assert.That(VillageActionScene.Footstep("Road", 0, 0), Is.Null);
			Assert.That(VillageActionScene.Footstep("Rock", 0, 0), Is.Null);
		}
	}
}
