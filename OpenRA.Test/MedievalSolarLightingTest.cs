using System;
using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalSolarLightingTest
	{
		static DayClock Day(int phase)
		{
			var day = new DayClock(12000, 3000);
			for (var i = 0; i < phase; i++) day.Advance();
			return day;
		}
		static Vector4 Overlay(SolarLighting light)
		{
			var color = light.Shade(0);
			return new Vector4(color.R, color.G, color.B, color.A) / 255;
		}
		[Test]
		public void GoldenHoursHaveDistinctColorsAndNoNoonVeil()
		{
			var dawn = SolarLighting.At(Day(500), null, 0);
			var dusk = SolarLighting.At(Day(11000), null, 0);
			var noon = SolarLighting.At(Day(6000), null, 0);
			Assert.That(dawn.Warmth, Is.GreaterThan(.15));
			Assert.That(dusk.Warmth, Is.GreaterThan(.20));
			Assert.That(dusk.WarmColor.Y, Is.LessThan(dawn.WarmColor.Y));
			Assert.That(noon.Shade(0).A, Is.Zero);
		}
		[TestCase(12000, 3000)]
		[TestCase(1000, 500)]
		[TestCase(100, 50)]
		public void CompleteCycleAndBothClockSeamsAreContinuous(int dayTicks, int nightTicks)
		{
			var day = new DayClock(dayTicks, nightTicks);
			var previous = SolarLighting.At(day, null, 0);
			for (var i = 0; i < dayTicks + nightTicks; i++)
			{
				day.Advance(); var next = SolarLighting.At(day, null, 0);
				Assert.That(Math.Abs(next.Darkness - previous.Darkness), Is.LessThan(2f / Math.Min(500, nightTicks)));
				// Premultiplied colour is what is visible; RGB under zero alpha is irrelevant.
				var a = Overlay(previous); var b = Overlay(next);
				Assert.That(Vector3.Distance(new Vector3(a.X, a.Y, a.Z) * a.W, new Vector3(b.X, b.Y, b.Z) * b.W), Is.LessThan(.06f));
				previous = next;
			}
		}
		[Test]
		public void CloudsMuteGoldenLightAndTorchesStillLiftNightShade()
		{
			var day = Day(11000);
			var clear = SolarLighting.At(day, null, 0);
			Assert.That(SolarLighting.At(day, null, 1000).Warmth, Is.LessThan(clear.Warmth * .11f));
			var night = SolarLighting.At(Day(13500), null, 0);
			Assert.That(night.Shade(1).A, Is.LessThan(night.Shade(0).A / 4));
			Assert.That(day.Phase, Is.EqualTo(11000));
		}
		[Test]
		public void LowSunHasLongerSofterShadowsWithLessContrast()
		{
			var low = SolarShadow.At(Day(900), null, 0); var noon = SolarShadow.At(Day(6000), null, 0);
			Assert.That(low.Length, Is.GreaterThan(noon.Length * 2));
			Assert.That(low.Softness, Is.GreaterThan(noon.Softness * 2));
			Assert.That(low.Opacity, Is.LessThan(noon.Opacity));
			Assert.That(SolarShadow.At(Day(11750), null, 0).Opacity, Is.LessThan(.02f));
		}
	}
}
