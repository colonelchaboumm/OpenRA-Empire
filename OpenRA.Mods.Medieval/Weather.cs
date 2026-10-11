using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed class RainDrop
	{
		public Vector2 Ground;
		public float Height, Speed, Size, Phase, Depth;
		public int Splash, Age;
		public bool Snow;
		public float Opacity => Math.Clamp(Age / 12f, 0, 1) * (Splash == 0 ? 1 : Math.Clamp(1 - Splash / (Snow ? 24f : 8f), 0, 1));
	}
	public sealed class VillageWeather
	{
		public const int MaxDrops = 440;
		readonly Random random;
		readonly List<RainDrop> drops = [];
		readonly int dryTicks, rainTicks;
		int untilChange;
		float target;
		public float Intensity { get; private set; }
		public int Showers { get; private set; }
		public int Splashes { get; private set; }
		public IReadOnlyList<RainDrop> Drops => drops;
		public VillageWeather(int seed, int firstRain = 1100, int dryTicks = 3000, int rainTicks = 950)
		{
			if (firstRain < 1 || dryTicks < 1 || rainTicks < 1) throw new ArgumentOutOfRangeException(nameof(firstRain));
			random = new Random(seed); untilChange = firstRain; this.dryTicks = dryTicks; this.rainTicks = rainTicks;
		}
		float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
		public void Tick(Rectangle view, Vector2 wind, float? synchronizedIntensity = null, bool snow = false)
		{
			if (--untilChange <= 0)
			{
				if (target == 0) { target = Range(.55f, .95f); untilChange = Math.Max(1, (int)(rainTicks * Range(.8f, 1.25f))); Showers++; }
				else { target = 0; untilChange = Math.Max(1, (int)(dryTicks * Range(.8f, 1.25f))); }
			}
			Intensity += Math.Clamp(target - Intensity, -.005f, .005f);
			if (synchronizedIntensity.HasValue) Intensity = synchronizedIntensity.Value;
			for (var i = drops.Count - 1; i >= 0; i--)
			{
				var drop = drops[i];
				drop.Age++;
				if (drop.Ground.X < view.Left - 100 || drop.Ground.X > view.Right + 100 || drop.Ground.Y < view.Top - 100 || drop.Ground.Y > view.Bottom + 260)
				{ drops.RemoveAt(i); continue; }
				if (drop.Height > 0)
				{
					drop.Height -= drop.Speed * (drop.Snow ? .045f + drop.Depth * .075f : 1);
					var flutter = drop.Snow ? MathF.Sin(drop.Age * (.035f + drop.Depth * .02f) + drop.Phase) * .55f : 0;
					drop.Ground += new Vector2(wind.X * (drop.Snow ? .6f + drop.Depth : 1.8f) + flutter, wind.Y * .6f);
					if (drop.Height <= 0) { drop.Height = 0; drop.Splash = 1; Splashes++; }
				}
				else if (++drop.Splash > (drop.Snow ? 24 : 7)) drops.RemoveAt(i);
			}
			var desired = (int)(Math.Min(MaxDrops, (long)view.Width * view.Height / 1900) * Intensity);
			for (var i = 0; i < 22 && drops.Count < desired; i++)
				drops.Add(new RainDrop { Ground = new Vector2(Range(view.Left, view.Right), Range(view.Top, view.Bottom + 180)),
					Height = Range(80, 240), Speed = Range(11, 19), Snow = snow, Size = Range(.65f, 1.9f), Depth = Range(.25f, 1), Phase = Range(0, MathF.Tau) });
		}
	}
	[TraitLocation(SystemActors.World)]
	public class MedievalWeatherInfo : TraitInfo
	{
		public readonly int FirstRainTicks = 1100;
		public readonly int DryTicks = 3000;
		public readonly int RainTicks = 950;
		public readonly int MinimumRainPermille = 550;
		public override object Create(ActorInitializer init) => new MedievalWeather(init.World, this);
	}
	// The schedule is synchronized, while drops, splashes and sound remain local.
	public sealed class MedievalWeather : IWorldLoaded, ITick, ITickRender, IRenderAboveWorld, INotifyActorDisposing, ISync
	{
		readonly World world;
		readonly MedievalWeatherInfo info;
		[VerifySync] int untilChange, rainTarget;
		[VerifySync] public int RainPermille { get; private set; }
		WorldRenderer renderer;
		ISound sound;
		public VillageWeather Scene { get; }
		public StormClock Storm { get; } = new();
		public StormPresentation StormScene { get; }
		public StormHousing StormHomes { get; }
		[VerifySync] public int StormStrikes => Storm.Strikes;
		[VerifySync] public int StormAge => Storm.Age;
		[VerifySync] public int StormCountdown => Storm.UntilStrike;
		[VerifySync] public int StormShelterTicks => Storm.ShelterTicks;
		public bool Snowing => world.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Season == VillageSeason.Winter;
		public MedievalWeather(World world, MedievalWeatherInfo info)
		{
			if (info.MinimumRainPermille < 1 || info.MinimumRainPermille > 950) throw new ArgumentOutOfRangeException(nameof(info.MinimumRainPermille));
			this.world = world; this.info = info; untilChange = info.FirstRainTicks;
			Scene = new VillageWeather(world.LocalRandom.Next(), info.FirstRainTicks, info.DryTicks, info.RainTicks);
			StormScene = new StormPresentation(world.LocalRandom.Next());
			StormHomes = new StormHousing(world);
		}
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr) { renderer = wr; }
		void ITick.Tick(Actor self)
		{
			if (world.Type != WorldType.Regular) return;
			if (--untilChange <= 0)
			{
				rainTarget = rainTarget == 0 ? world.SharedRandom.Next(info.MinimumRainPermille, 951) : 0;
				var duration = rainTarget == 0 ? info.DryTicks : info.RainTicks;
				var season = world.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Season;
				if (season == VillageSeason.Summer) duration = duration * (rainTarget == 0 ? 150 : 60) / 100;
				else if (season == VillageSeason.Autumn) duration = duration * (rainTarget == 0 ? 65 : 130) / 100;
				untilChange = Math.Max(1, duration * world.SharedRandom.Next(80, 126) / 100);
			}
			RainPermille += Math.Clamp(rainTarget - RainPermille, -5, 5);
			Storm.Advance(RainPermille, Snowing);
			if (renderer == null) return;
			var view = Rectangle.FromLTRB(renderer.Viewport.TopLeft.X, renderer.Viewport.TopLeft.Y, renderer.Viewport.BottomRight.X, renderer.Viewport.BottomRight.Y);
			Scene.Tick(view, world.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene.Wind ?? Vector2.Zero, RainPermille / 1000f, Snowing);
			StormScene.Advance(renderer, Storm, Snowing);
		}
		void StopSound() { if (sound != null) Game.Sound.StopSound(sound); sound = null; }
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			if (world.Type != WorldType.Regular || world.Paused || Scene.Intensity < .02f || Snowing) { StopSound(); return; }
			if (sound == null || sound.Complete) sound = Game.Sound.PlayLooped(SoundType.World, "medieval|audio/rain-1.wav");
			if (sound != null) sound.Volume = .24f * Scene.Intensity * Game.Sound.SoundVolume * Game.Sound.SoundVolumeModifier;
		}
		void INotifyActorDisposing.Disposing(Actor self) { StopSound(); }
		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			if (world.Type != WorldType.Regular || Scene.Intensity <= 0 && Scene.Drops.Count == 0) return;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var left = wr.Viewport.TopLeft; var right = wr.Viewport.BottomRight;
			cr.FillRect(new Vector3(left.X, left.Y, 0), new Vector3(right.X, right.Y, 0), Color.FromArgb((int)((Storm.Active ? 48 : 30) * Scene.Intensity), 24, 38, 49));
			var wind = world.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene.Wind ?? Vector2.Zero;
			foreach (var drop in Scene.Drops)
			{
				var pos = wr.ProjectedPosition(new int2((int)drop.Ground.X, (int)drop.Ground.Y));
				var cell = world.Map.CellContaining(pos);
				if (!world.Map.Contains(cell) || world.FogObscures(cell)) continue;
				var alpha = (int)((85 + 70 * drop.Depth) * drop.Opacity);
				if (drop.Snow)
				{
					// Every flake keeps its depth and phase across season/camera changes.
					if (drop.Splash > 0 && world.ActorMap.GetActorsAt(cell).Any(a => a.TraitOrDefault<OpenRA.Mods.Common.Traits.Building>() != null)) continue;
					var at = new Vector3(drop.Ground.X, drop.Ground.Y - drop.Height, 0);
					var size = new Vector3(drop.Size, drop.Size * (drop.Splash > 0 ? .4f : .65f + .15f * MathF.Sin(drop.Age * .13f + drop.Phase)), 0);
					cr.FillEllipse(at - size * 1.8f, at + size * 1.8f, Color.FromArgb(alpha / 5, 219, 234, 247));
					cr.FillEllipse(at - size, at + size, Color.FromArgb(Math.Min(245, (int)(alpha * 1.4f)), 242, 248, 253));
					if (drop.Size > 1.5f && drop.Splash == 0)
						cr.DrawLine(at - Vector3.UnitY * drop.Size, at + Vector3.UnitY * drop.Size, .7f, Color.FromArgb(alpha, 249, 251, 254));
					continue;
				}
				if (drop.Height > 0)
				{
					var at = new Vector3(drop.Ground.X, drop.Ground.Y - drop.Height, 0);
					cr.DrawLine(at - new Vector3(wind.X * 3, 13 + drop.Speed * .55f, 0), at, 1.1f,
						Color.FromArgb(alpha / 4, 175, 197, 209), Color.FromArgb(alpha, 186, 208, 217));
				}
				else
				{
					// Ground splashes never appear on roofs, farms or storage piles.
					if (world.ActorMap.GetActorsAt(cell).Any(a => a.TraitOrDefault<OpenRA.Mods.Common.Traits.Building>() != null)) continue;
					var at = new Vector3(drop.Ground, 0); var radius = .5f + drop.Splash * .48f;
					var color = Color.FromArgb(alpha * (8 - drop.Splash) / 8, 183, 206, 211);
					if (world.Map.GetTerrainInfo(cell).Type == "Water")
					{
						cr.DrawLine(at + new Vector3(-radius, 0, 0), at + new Vector3(0, -radius * .4f, 0), 1, color);
						cr.DrawLine(at + new Vector3(0, -radius * .4f, 0), at + new Vector3(radius, 0, 0), 1, color);
						cr.DrawLine(at + new Vector3(radius, 0, 0), at + new Vector3(0, radius * .4f, 0), 1, color);
						cr.DrawLine(at + new Vector3(0, radius * .4f, 0), at + new Vector3(-radius, 0, 0), 1, color);
					}
					else
					{
						cr.DrawLine(at, at + new Vector3(-radius, -.8f, 0), 1, color);
						cr.DrawLine(at, at + new Vector3(radius, -1.5f, 0), 1, color);
					}
				}
			}
			StormScene.Render(wr, Storm, Snowing);
		}
	}
}
