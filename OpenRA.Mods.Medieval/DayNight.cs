using System;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed class DayClock
	{
		public readonly int DayTicks, NightTicks;
		public int Phase { get; private set; }
		public bool Night => Phase >= DayTicks;
		// The watch begins with the first torch light at dusk and ends at dawn.
		int TwilightTicks => Math.Min(DayTicks, 750);
		public bool NightWatch => Night || Phase > DayTicks - TwilightTicks;
		public int Remaining => (Night ? DayTicks + NightTicks : DayTicks) - Phase;
		public float Darkness => Night ? Math.Min(1f, Remaining / 500f) : Math.Clamp((Phase - DayTicks + TwilightTicks) / (float)TwilightTicks, 0, 1);
		public DayClock(int dayTicks, int nightTicks)
		{
			if (dayTicks < 1 || nightTicks < 1 || (long)dayTicks + nightTicks > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(dayTicks));
			DayTicks = dayTicks; NightTicks = nightTicks;
		}
		public void Advance() { Phase = (Phase + 1) % (DayTicks + NightTicks); }
	}
	[TraitLocation(SystemActors.World)]
	public class MedievalDayNightInfo : TraitInfo
	{
		public readonly int DayTicks = 12000;
		public readonly int NightTicks = 3000;
		public override object Create(ActorInitializer init) => new MedievalDayNight(this);
	}
	public sealed class MedievalDayNight : ITick, IRenderAboveWorld, ISync
	{
		public readonly DayClock Clock;
		public int ShadingQuads { get; private set; }
		[VerifySync] public int Phase => Clock.Phase;
		public MedievalDayNight(MedievalDayNightInfo info) { Clock = new(info.DayTicks, info.NightTicks); }
		void ITick.Tick(Actor self) { if (self.World.Type == WorldType.Regular) Clock.Advance(); }
		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			ShadingQuads = 0;
			var dark = Clock.Darkness;
			if (dark <= 0 || self.World.Type != WorldType.Regular) return;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var left = wr.Viewport.TopLeft; var right = wr.Viewport.BottomRight;
			// Only visible torches participate; neither illumination nor camera changes reveal shroud.
			var lights = self.World.ActorsHavingTrait<MedievalTorch>().Where(MedievalTorch.Lit)
				.Select(a => (Actor: a, Point: wr.ScreenPxPosition(a.CenterPosition), Scale: Math.Clamp(a.Trait<MedievalTorch>().Info.ScalePercent, 20, 100) / 100f))
				.Where(t => t.Point.X > left.X - 130 && t.Point.X < right.X + 130 && t.Point.Y > left.Y - 130 && t.Point.Y < right.Y + 130).ToArray();
			// Restrict house lighting to the viewport and revealed actors. Tall houses
			// use a taller glow while glass remains depth-sorted with the building.
			var homes = self.World.ScreenMap.RenderableActorsInBox(left, right)
				.Select(a => (Actor: a, Light: a.TraitOrDefault<MedievalHouseLights>()))
				.Where(p => p.Light != null && MedievalHouseLights.Brightness(p.Actor) > 0)
				.Select(p => (Point: wr.Screen3DPxPosition(p.Actor.CenterPosition) + p.Light.Center,
					Strength: MedievalHouseLights.Brightness(p.Actor), Height: p.Actor.Trait<MedievalHouseAppearance>().Height > 80 ? 60f : 36f)).ToArray();
			var fireEnvironment = self.World.WorldActor.TraitOrDefault<MedievalFireEnvironment>();
			Color Shade(int x, int y)
			{
				// The spatial light cache includes torches; avoid scanning every soldier
				// for every mesh vertex during a large night battle.
				var strength = fireEnvironment?.StrengthAt(new Vector2(x, y)) ?? 0;
				if (fireEnvironment == null)
					foreach (var light in lights)
					{
						var dx = (x - light.Point.X) / (105f * light.Scale); var dy = (y - light.Point.Y) / (72f * light.Scale);
						strength = Math.Max(strength, Math.Clamp(1 - dx * dx - dy * dy, 0, 1));
					}
				foreach (var home in homes)
				{
					var dx = (x - home.Point.X) / 50f; var dy = (y - home.Point.Y) / home.Height;
					strength = Math.Max(strength, .9f * home.Strength * Math.Clamp(1 - dx * dx - dy * dy, 0, 1));
				}
				return Color.FromArgb((int)(dark * (166 - 148 * strength)), 12, 23, 46);
			}
			// Bound the mesh in screen space. A fixed world-pixel step becomes
			// tens of thousands of quads when viewing an entire continent at night.
			var step = Math.Max(40, Math.Max((right.X - left.X + 63) / 64, (right.Y - left.Y + 47) / 48));
			for (var y = left.Y; y < right.Y; y += step)
				for (var x = left.X; x < right.X; x += step)
				{
					ShadingQuads++;
					var x2 = Math.Min(x + step, right.X); var y2 = Math.Min(y + step, right.Y);
					cr.FillRect(new Vector3(x, y, 0), new Vector3(x2, y, 0), new Vector3(x2, y2, 0), new Vector3(x, y2, 0),
						Shade(x, y), Shade(x2, y), Shade(x2, y2), Shade(x, y2));
				}
			foreach (var light in lights)
			{
				var mobile = light.Actor.TraitOrDefault<OpenRA.Mods.Common.Traits.Mobile>() != null;
				var at = new Vector3(light.Point.X + (mobile ? 7 : 0), light.Point.Y - (mobile ? light.Scale < 1 ? 17 : 23 : 28), 0);
				var flicker = (float)Math.Sin((self.World.WorldTick + light.Actor.ActorID * 7) * .65);
				cr.DrawLine(at + new Vector3(0, 9 * light.Scale, 0), at, Math.Max(1, 2 * light.Scale), Color.FromArgb(129, 88, 41));
				for (var r = 16; r > 4; r -= 4)
					cr.FillEllipse(at - new Vector3(r * light.Scale, r * light.Scale, 0), at + new Vector3(r * light.Scale, r * light.Scale, 0), Color.FromArgb((int)(8 * dark), 255, 169, 55));
				cr.DrawLine(at, at + new Vector3(flicker * 2, -7 - flicker * 2, 0) * light.Scale, Math.Max(1, 4 * light.Scale), Color.Orange);
				cr.DrawLine(at, at + new Vector3(flicker, -4, 0) * light.Scale, Math.Max(1, 2 * light.Scale), Color.LightYellow);
			}
		}
	}
	public class MedievalTorchInfo : TraitInfo
	{
		public readonly int ScalePercent = 100;
		public override object Create(ActorInitializer init) => new MedievalTorch(this);
	}
	public sealed class MedievalTorch
	{
		public readonly MedievalTorchInfo Info;
		public MedievalTorch(MedievalTorchInfo info) { Info = info; }
		public static bool Lit(Actor self) => self.World.WorldActor.TraitOrDefault<MedievalDayNight>()?.Clock.Darkness > 0
			&& MedievalStockpile.Active(self) && self.TraitOrDefault<MedievalRest>()?.Sleeping != true && !self.World.FogObscures(self)
			&& (self.World.RenderPlayer == null || self.CanBeViewedByPlayer(self.World.RenderPlayer));
	}
}
