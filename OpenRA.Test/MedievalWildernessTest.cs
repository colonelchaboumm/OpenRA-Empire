using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalWildernessTest
	{
		[Test]
		public void AnimalsStayBoundedAndNeverCrossBlockedGround()
		{
			var scene = new WildlifeScene(17);
			var meadow = Enumerable.Range(0, 40).Select(i => new Vector2(i % 8 * 1900, i / 8 * 1900)).ToArray();
			bool Allowed(Vector2 p) => p.X >= 0 && p.X < 16000 && p.Y >= 0 && p.Y < 11000 && (p.X < 4500 || p.X > 6000);
			var sawDeer = false; var sawRabbit = false; var moving = false;
			for (var i = 0; i < 7000; i++)
			{
				scene.Tick(meadow, [], Allowed, _ => true);
				Assert.That(scene.Animals.Count, Is.LessThanOrEqualTo(WildlifeScene.MaxAnimals));
				Assert.That(scene.Animals.All(a => Allowed(a.Position) && a.Frame >= 0 && a.Frame < 192), Is.True);
				sawDeer |= scene.Animals.Any(a => a.Kind == WildlifeKind.Deer);
				sawRabbit |= scene.Animals.Any(a => a.Kind == WildlifeKind.Rabbit);
				moving |= scene.Animals.Any(a => a.Moving);
			}
			Assert.That(sawDeer && sawRabbit && moving, Is.True);
		}
		[Test]
		public void NearbyPeopleScareAnimalsAndCameraChangesDoNotTeleportThem()
		{
			var scene = new WildlifeScene(3);
			for (var i = 0; i < 100; i++) scene.Tick([new Vector2(8000, 8000)], [], _ => true, _ => true);
			var animal = scene.Animals.First(); var start = animal.Position;
			var threat = start - new Vector2(600, 0);
			for (var i = 0; i < 15; i++) scene.Tick([], [threat], _ => true, _ => true);
			Assert.That(scene.Fled, Is.GreaterThan(0));
			Assert.That(Vector2.Distance(animal.Position, threat), Is.GreaterThan(600));
			var before = animal.Position;
			scene.Tick([new Vector2(50000, 50000)], [], _ => true, _ => true);
			Assert.That(Vector2.Distance(before, animal.Position), Is.LessThanOrEqualTo(93));
			for (var i = 0; i < 25; i++) scene.Tick([new Vector2(50000, 50000)], [], _ => true, p => p.X > 40000);
			Assert.That(scene.Animals.Contains(animal), Is.False);
			Assert.That(scene.Animals.Count, Is.LessThanOrEqualTo(WildlifeScene.MaxAnimals));
		}
		[Test]
		public void RainCyclesFadeSmoothlyAndKeepParticlesBoundedAtEveryZoom()
		{
			var scene = new VillageWeather(94, 15, 380, 280);
			var wet = false; var dryAfterRain = false;
			for (var i = 0; i < 7000; i++)
			{
				var before = scene.Intensity;
				scene.Tick(i % 800 < 400 ? new Rectangle(0, 0, 1280, 800) : new Rectangle(40000, 40000, 5000, 3500), new Vector2(1, .2f));
				Assert.That(Math.Abs(scene.Intensity - before), Is.LessThanOrEqualTo(.00501f));
				Assert.That(scene.Drops.Count, Is.LessThanOrEqualTo(VillageWeather.MaxDrops));
				Assert.That(scene.Drops.All(d => float.IsFinite(d.Ground.X) && d.Height >= 0 && d.Splash <= 7), Is.True);
				wet |= scene.Intensity > .4f;
				dryAfterRain |= wet && scene.Intensity == 0 && scene.Drops.Count == 0;
			}
			Assert.That(wet && dryAfterRain, Is.True);
			Assert.That(scene.Showers, Is.GreaterThan(4));
			Assert.That(scene.Splashes, Is.GreaterThan(1000));
		}
	}
}
