using System;
using System.Numerics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	// Presentation follows the existing clock: sleep, patrols and day length are unchanged.
	public readonly record struct SolarLighting(float Darkness, float Warmth, Vector3 WarmColor)
	{
		public static float Smooth(float value)
		{ value = Math.Clamp(value, 0, 1); return value * value * (3 - 2 * value); }
		public static SolarLighting At(DayClock day, SeasonClock season, int rain)
		{
			var time = day.Phase / (float)day.DayTicks;
			var dark = Smooth(day.Night ? Math.Min(1, day.Remaining / (float)Math.Min(500, day.NightTicks)) : day.Darkness);
			// Dawn starts while the night veil lifts and continues after sunrise.
			// The two halves meet at the same colour and intensity at the clock wrap.
			var morning = day.Night ? 1 : 1 - Smooth(time / .20f);
			var evening = day.Night ? 0 : Smooth((time - .72f) / .28f);
			var clear = 1 - .90f * Math.Clamp(rain / 1000f, 0, 1);
			var warmth = (.23f * morning + .32f * evening) * (1 - dark) * clear;
			var color = Vector3.Lerp(new Vector3(1, .69f, .34f), new Vector3(1, .43f, .23f), evening);
			color = Vector3.Lerp(color, new Vector3(1, .76f, .56f), .22f * (season?.Weight(VillageSeason.Winter) ?? 0));
			return new(dark, warmth, color);
		}
		public Color Shade(float localLight)
		{
			var night = Darkness * (166 - 148 * Math.Clamp(localLight, 0, 1)) / 255f;
			var warm = Warmth * (1 - night);
			var alpha = night + warm;
			if (alpha <= 0) return Color.Transparent;
			var rgb = (new Vector3(12, 23, 46) * night + WarmColor * (255 * warm)) / alpha;
			return Color.FromArgb((int)(255 * alpha), (int)rgb.X, (int)rgb.Y, (int)rgb.Z);
		}
	}
}
