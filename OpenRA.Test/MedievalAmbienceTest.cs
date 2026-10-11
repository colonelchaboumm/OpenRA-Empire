using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalAmbienceTest
	{
		static readonly Rectangle View = new(0, 0, 1000, 700);
		static readonly AmbientEmitter[] Meadow =
		[
			new(new Vector2(200, 300), 10, AmbientKind.Petal),
			new(new Vector2(500, 400), 15, AmbientKind.Seed),
			new(new Vector2(700, 300), 66, AmbientKind.Leaf)
		];
		[Test]
		public void LongSessionsKeepBirdsAndDebrisBounded()
		{
			var scene = new VillageAmbience(73);
			for (var t = 0; t < 30000; t++)
			{
				scene.Tick(View, Meadow);
				Assert.That(scene.Particles.Count(p => p.Kind == AmbientKind.Bird), Is.LessThanOrEqualTo(VillageAmbience.MaxBirds));
				Assert.That(scene.Particles.Count(p => p.Kind != AmbientKind.Bird), Is.LessThanOrEqualTo(VillageAmbience.MaxDebris));
				Assert.That(scene.Particles.All(p => p.Age < p.Life && float.IsFinite(p.Ground.X) && p.Opacity >= 0 && p.Opacity <= 1), Is.True);
			}
			Assert.That(scene.SpawnedBirds, Is.GreaterThan(20));
			Assert.That(scene.SpawnedPetals, Is.GreaterThan(20));
			Assert.That(scene.SpawnedSeeds, Is.GreaterThan(20));
		}
		[Test]
		public void LeavingVegetationStopsEmissionsAndOldParticlesExpire()
		{
			var scene = new VillageAmbience(51);
			for (var t = 0; t < 500; t++) scene.Tick(View, Meadow);
			var original = scene.Particles.ToArray();
			var petals = scene.SpawnedPetals;
			var seeds = scene.SpawnedSeeds;
			Assert.That(original.Any(p => p.Kind != AmbientKind.Bird), Is.True);
			for (var t = 0; t < 900; t++) scene.Tick(View, Array.Empty<AmbientEmitter>());
			Assert.That(scene.SpawnedPetals, Is.EqualTo(petals));
			Assert.That(scene.SpawnedSeeds, Is.EqualTo(seeds));
			Assert.That(scene.Particles.All(p => p.Kind == AmbientKind.Bird), Is.True);
			Assert.That(scene.Particles.Intersect(original), Is.Empty);
		}
		[Test]
		public void EmissionsComeFromPlantsAndMovingTheCameraDoesNotMoveExistingParticles()
		{
			var scene = new VillageAmbience(92);
			var source = new AmbientEmitter(new Vector2(200, 300), 10, AmbientKind.Petal);
			for (var t = 0; t < 30; t++) scene.Tick(View, [source]);
			var petals = scene.Particles.Where(p => p.Kind == AmbientKind.Petal).ToArray();
			Assert.That(petals, Is.Not.Empty);
			Assert.That(petals.All(p => Vector2.Distance(p.Ground, source.Ground) < 60), Is.True);
			var before = petals.Select(p => p.Ground).ToArray();
			scene.Tick(new Rectangle(10000, 10000, 1000, 700), []);
			for (var i = 0; i < petals.Length; i++)
				Assert.That(Vector2.Distance(before[i], petals[i].Ground), Is.LessThan(3));
		}
		[Test]
		public void WindVariesSmoothlyAndPlantsDoNotSwayInUnison()
		{
			var scene = new VillageAmbience(11);
			var min = float.MaxValue;
			var max = float.MinValue;
			for (var t = 0; t < 5000; t++)
			{
				var before = scene.Wind;
				scene.Tick(View, []);
				Assert.That(Vector2.Distance(before, scene.Wind), Is.LessThan(.05f));
				min = Math.Min(min, scene.Wind.X);
				max = Math.Max(max, scene.Wind.X);
			}
			Assert.That(max - min, Is.GreaterThan(.5f));
			Assert.That(scene.Sway(1, 2), Is.Not.EqualTo(scene.Sway(2, 2)));
		}
	}
}
