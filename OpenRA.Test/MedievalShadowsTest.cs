using System;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalShadowsTest
	{
		static DayClock Day(int phase)
		{
			var clock = new DayClock(12000, 3000);
			for (var i = 0; i < phase; i++) clock.Advance();
			return clock;
		}
		static SeasonClock Season(VillageSeason season)
		{
			var clock = new SeasonClock(45000);
			for (var i = 0; i < (int)season * 45000 + 1000; i++) clock.Advance();
			return clock;
		}
		[Test]
		public void SunChangesSideAndWinterShadowsAreLonger()
		{
			var summer = Season(VillageSeason.Summer); var winter = Season(VillageSeason.Winter);
			Assert.That(SolarShadow.At(Day(1800), summer, 0).Direction.X, Is.GreaterThan(0));
			Assert.That(SolarShadow.At(Day(10200), summer, 0).Direction.X, Is.LessThan(0));
			Assert.That(SolarShadow.At(Day(6000), summer, 0).Direction.X, Is.EqualTo(0).Within(.00001));
			Assert.That(SolarShadow.At(Day(6000), winter, 0).Length, Is.GreaterThan(SolarShadow.At(Day(6000), summer, 0).Length * 1.8));
			Assert.That(SolarShadow.At(Day(1500), summer, 0).Length, Is.GreaterThan(SolarShadow.At(Day(6000), summer, 0).Length));
		}
		[Test]
		public void DayNightAndYearSeamsFadeWithoutPopping()
		{
			var season = Season(VillageSeason.Autumn);
			foreach (var phase in new[] { 0, 11999, 12000, 14999 })
				Assert.That(SolarShadow.At(Day(phase), season, 0).Opacity, Is.LessThan(.00001), phase.ToString());
			Assert.That(SolarShadow.At(Day(13500), season, 0).Opacity, Is.InRange(.03f, .08f));
			var day = Day(6000); var previous = SolarShadow.At(day, season, 0);
			for (var i = 0; i < 45000 * 2; i++)
			{
				season.Advance(); var next = SolarShadow.At(day, season, 0);
				Assert.That(Math.Abs(next.Length - previous.Length), Is.LessThan(.003)); previous = next;
			}
		}
		[Test]
		public void ProjectionKeepsRootsFixedHasBoundedReachAndNeverCollapses()
		{
			var season = Season(VillageSeason.Winter); var day = Day(0);
			for (var i = 0; i < 15000; i++)
			{
				var state = SolarShadow.At(day, season, 0);
				foreach (var height in new[] { 14f, 80f, 240f, 500f })
				{
					var matrix = state.Projection(height);
					Assert.That(Vector3.Transform(Vector3.Zero, matrix), Is.EqualTo(Vector3.Zero));
					Assert.That(Vector3.Transform(new Vector3(0, -height, 0), matrix).Length(), Is.LessThanOrEqualTo(SolarShadow.MaximumReach + .001f));
					Assert.That(Math.Abs(matrix.GetDeterminant()), Is.GreaterThan(.001));
				}
				day.Advance();
			}
		}
		[Test]
		public void CloudsReduceContrastAndCalculationDoesNotAdvanceSimulation()
		{
			var day = Day(6000); var season = Season(VillageSeason.Winter); var phase = season.Phase;
			Assert.That(SolarShadow.At(day, season, 1000).Opacity, Is.LessThan(SolarShadow.At(day, season, 0).Opacity * .2f));
			for (var i = 0; i < 500; i++) SolarShadow.At(day, season, i);
			Assert.That(day.Phase, Is.EqualTo(6000)); Assert.That(season.Phase, Is.EqualTo(phase));
		}
	}
}
