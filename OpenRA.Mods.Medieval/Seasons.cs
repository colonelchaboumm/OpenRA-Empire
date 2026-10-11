using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum VillageSeason { Spring, Summer, Autumn, Winter }
	public sealed class SeasonClock
	{
		public readonly int SeasonTicks;
		public int Phase { get; private set; }
		public int Year { get; private set; } = 1;
		public VillageSeason Season => (VillageSeason)(Phase / SeasonTicks);
		public int Elapsed => Phase % SeasonTicks;
		public int Remaining => SeasonTicks - Elapsed;
		// A short presentation crossfade preserves the real 30-minute seasons.
		public float Blend { get { var t = Math.Clamp(Elapsed / (float)Math.Min(750, SeasonTicks), 0, 1); return t * t * (3 - 2 * t); } }
		public float Weight(VillageSeason season) => Season == VillageSeason.Spring && Year == 1 ? season == Season ? 1 : 0
			: (Season == season ? Blend : 0) + ((VillageSeason)(((int)Season + 3) % 4) == season ? 1 - Blend : 0);
		public Vector3 FoliageTint(uint variation, bool evergreen)
		{
			var autumn = evergreen ? new Vector3(.92f, .96f, .86f) : new Vector3(1.14f + variation % 4 * .055f, .86f - variation % 3 * .06f, .53f);
			var tint = Vector3.One + (new Vector3(1.04f, 1.01f, .88f) - Vector3.One) * Weight(VillageSeason.Summer)
				+ (autumn - Vector3.One) * Weight(VillageSeason.Autumn);
			return Vector3.Lerp(tint, new Vector3(1.27f, 1.34f, 1.44f), Snow / 1000f);
		}
		public static float SnowCoverage(int x, int y, int snow)
		{
			// Smooth world-space noise is sampled at SHARED terrain vertices. Per-cell
			// opacity or centered diamonds would expose the isometric tile grid.
			var gx = (int)MathF.Floor(x / 4096f); var gy = (int)MathF.Floor(y / 4096f);
			var fx = x / 4096f - gx; var fy = y / 4096f - gy;
			fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
			static float Noise(int a, int b) { var n = unchecked((uint)(a * 374761393 + b * 668265263)); n = (n ^ (n >> 13)) * 1274126177u; return (n & 65535) / 65535f; }
			var top = Noise(gx, gy) * (1 - fx) + Noise(gx + 1, gy) * fx;
			var bottom = Noise(gx, gy + 1) * (1 - fx) + Noise(gx + 1, gy + 1) * fx;
			return Math.Clamp(snow / 1000f * 1.55f - (top * (1 - fy) + bottom * fy) * .55f, 0, 1);
		}
		public int Snow => Season == VillageSeason.Winter ? (int)Math.Min(1000, (long)Elapsed * 1000 / Math.Min(750, SeasonTicks))
			: Season == VillageSeason.Spring && Year > 1 ? (int)Math.Max(0, 1000 - (long)Elapsed * 1000 / Math.Min(750, SeasonTicks)) : 0;
		public static string Name(VillageSeason season) => season switch { VillageSeason.Spring => "Printemps", VillageSeason.Summer => "Été", VillageSeason.Autumn => "Automne", _ => "Hiver" };
		public static int Crops(VillageSeason season) => season switch { VillageSeason.Summer => 115, VillageSeason.Autumn => 75, VillageSeason.Winter => 20, _ => 100 };
		public static int Work(VillageSeason season, string profession) => profession switch
		{
			"farmer" => Crops(season),
			"hunter" => season == VillageSeason.Winter ? 65 : season == VillageSeason.Autumn ? 90 : 100,
			"lumberjack" or "stonecutter" or "goldminer" => season == VillageSeason.Winter ? 75 : 100,
			_ => 100
		};
		public static int Speed(VillageSeason season, int rain) => (season == VillageSeason.Winter ? 80 : season == VillageSeason.Autumn ? 95 : 100) - Math.Clamp(rain, 0, 1000) * 10 / 1000;
		public static int Combat(VillageSeason season, int rain, bool bow) => (season == VillageSeason.Winter ? 90 : 100) - (bow ? Math.Clamp(rain, 0, 1000) * 15 / 1000 : 0);
		public SeasonClock(int seasonTicks)
		{
			if (seasonTicks < 1 || seasonTicks > int.MaxValue / 4) throw new ArgumentOutOfRangeException(nameof(seasonTicks));
			SeasonTicks = seasonTicks;
		}
		public void Advance() { if (++Phase == SeasonTicks * 4) { Phase = 0; Year++; } }
	}
	[TraitLocation(SystemActors.World)]
	public class MedievalSeasonsInfo : TraitInfo
	{
		public readonly int SeasonTicks = 45000;
		public override object Create(ActorInitializer init) => new MedievalSeasons(this);
	}
	public sealed class MedievalSeasons : ITick, ITickRender, IRenderOverlay, ISync
	{
		public readonly SeasonClock Clock;
		[VerifySync] public int Phase => Clock.Phase;
		[VerifySync] public int Year => Clock.Year;
		// Snow tracks are presentation only. Only observed movement is recorded;
		// neither the camera nor these bounded caches influence the simulation.
		readonly Dictionary<uint, WPos> walkers = [];
		readonly Queue<(WPos Pos, WVec Direction, bool Wheels, int Born)> tracks = [];
		int lastTick = -1;
		public int TrackCount => tracks.Count;
		public const int MaximumTracks = 8192;
		public MedievalSeasons(MedievalSeasonsInfo info) { Clock = new(info.SeasonTicks); }
		void ITick.Tick(Actor self) { if (self.World.Type == WorldType.Regular) Clock.Advance(); }
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			var w = self.World;
			if (w.Type != WorldType.Regular || w.Paused || lastTick == w.WorldTick) return;
			lastTick = w.WorldTick;
			if (Clock.Snow == 0) { tracks.Clear(); walkers.Clear(); return; }
			var lifetime = (w.WorldActor.TraitOrDefault<MedievalWeather>()?.RainPermille ?? 0) > 400 ? 900 : 1800;
			while (tracks.Count > 0 && (tracks.Count >= MaximumTracks || lastTick - tracks.Peek().Born > lifetime)) tracks.Dequeue();
			var present = new HashSet<uint>();
			foreach (var a in w.ScreenMap.RenderableActorsInBox(wr.Viewport.TopLeft, wr.Viewport.BottomRight))
			{
				if (a.IsDead || a.TraitOrDefault<Mobile>() == null || w.FogObscures(a) || w.RenderPlayer != null && !a.CanBeViewedByPlayer(w.RenderPlayer)
					|| a.TraitOrDefault<MedievalRest>()?.Sleeping == true) continue;
				present.Add(a.ActorID);
				var position = a.CenterPosition;
				if (!walkers.TryGetValue(a.ActorID, out var previous)) { walkers[a.ActorID] = position; continue; }
				var delta = position - previous;
				if (delta.HorizontalLengthSquared < 230L * 230) continue;
				walkers[a.ActorID] = position;
				if (delta.HorizontalLengthSquared > 2048L * 2048 || delta.Z != 0) continue;
				var cell = w.Map.CellContaining(position);
				if (!w.Map.Contains(cell) || w.Map.GetTerrainInfo(cell).Type is not ("Clear" or "Rough" or "Road")) continue;
				if (w.WorldActor.TraitOrDefault<MedievalTrails>()?.ProtectsCell(cell) == true) continue;
				if (tracks.Count >= MaximumTracks) tracks.Dequeue();
				tracks.Enqueue((position, delta, a.TraitOrDefault<MedievalSupplyCart>() != null || a.TraitOrDefault<MedievalFoundingCaravan>() != null, lastTick));
			}
			foreach (var id in walkers.Keys.Where(id => !present.Contains(id)).ToArray()) walkers.Remove(id);
		}
		void IRenderOverlay.Render(WorldRenderer wr)
		{
			var w = wr.World;
			if (w.Type != WorldType.Regular) return;
			var snow = Clock.Snow;
			var heat = w.WorldActor.TraitOrDefault<MedievalFireEnvironment>()?.Heat;
			var autumn = Clock.Weight(VillageSeason.Autumn);
			var summer = Clock.Weight(VillageSeason.Summer);
			if (snow == 0 && autumn == 0 && summer == 0) return;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			// Only viewport cells are painted, regardless of continent dimensions.
			foreach (var uv in wr.Viewport.VisibleCellsInsideBounds.CandidateMapCoords)
			{
				var cell = uv.ToCPos(w.Map);
				if (!w.Map.Contains(cell) || w.ShroudObscures(cell) || w.Map.GetTerrainInfo(cell).Type is not ("Clear" or "Rough" or "Road")) continue;
				var ramp = w.Map.Grid.Ramps[w.Map.Ramp[cell]];
				var p = w.Map.CenterOfCell(cell) - new WVec(0, 0, ramp.CenterHeightOffset);
				var a = wr.Screen3DPosition(p + ramp.Corners[0]);
				var b = wr.Screen3DPosition(p + ramp.Corners[1]);
				var c = wr.Screen3DPosition(p + ramp.Corners[2]);
				var d = wr.Screen3DPosition(p + ramp.Corners[3]);
				var shade = (unchecked(cell.X * 73 ^ cell.Y * 151) & 7) / 2;
				if (autumn + summer > 0) cr.FillRect(a, b, c, d, Color.FromArgb((int)(18 * summer + 36 * autumn), 162 + shade, (int)(141 - 36 * autumn), 39));
				if (snow > 0)
				{
					Color At(int corner) { var at = p + ramp.Corners[corner]; return Color.FromArgb((int)(SeasonClock.SnowCoverage(at.X, at.Y, snow) * (heat?.SnowRemaining(at) ?? 1) * 245), 234, 241, 245); }
					cr.FillRect(a, b, c, d, At(0), At(1), At(2), At(3));
				}
			}
			if (snow == 0) return;
			var tl = wr.Viewport.TopLeft; var br = wr.Viewport.BottomRight;
			foreach (var mark in tracks)
			{
				var at = wr.Screen3DPosition(mark.Pos);
				if (at.X < tl.X || at.X > br.X || at.Y < tl.Y || at.Y > br.Y || w.FogObscures(mark.Pos)) continue;
				var vector = wr.Screen3DPosition(mark.Pos + mark.Direction) - at;
				var direction = vector.LengthSquared() > 0 ? Vector3.Normalize(vector) : Vector3.UnitX;
				var side = new Vector3(-direction.Y, direction.X, 0) * (mark.Wheels ? 4 : 1.8f);
				var lifetime = (w.WorldActor.TraitOrDefault<MedievalWeather>()?.RainPermille ?? 0) > 400 ? 900f : 1800f;
				var age = Math.Clamp(1f - (w.WorldTick - mark.Born) / lifetime, 0, 1);
				var color = Color.FromArgb((int)(snow / 1000f * age * (heat?.SnowRemaining(mark.Pos) ?? 1) * 155), 74, 99, 116);
				var length = mark.Wheels ? 4 : 1.7f;
				cr.DrawLine(at - side, at - side + direction * length, mark.Wheels ? 1.2f : 1.8f, color);
				cr.DrawLine(at + side + direction * 2, at + side + direction * (2 + length), mark.Wheels ? 1.2f : 1.8f, color);
			}
		}
	}
	public class MedievalSeasonEffectsInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSeasonEffects(init.Self);
	}
	public sealed class MedievalSeasonEffects : ISpeedModifier, IFirepowerModifier
	{
		readonly MedievalSeasons seasons;
		readonly MedievalWeather weather;
		readonly bool bow;
		public MedievalSeasonEffects(Actor self)
		{
			seasons = self.World.WorldActor.TraitOrDefault<MedievalSeasons>();
			weather = self.World.WorldActor.TraitOrDefault<MedievalWeather>();
			bow = self.Info.Name is "md.archer" or "md.elitearcher";
		}
		int ISpeedModifier.GetSpeedModifier() => seasons == null ? 100 : SeasonClock.Speed(seasons.Clock.Season, weather?.RainPermille ?? 0);
		int IFirepowerModifier.GetFirepowerModifier() => seasons == null ? 100 : SeasonClock.Combat(seasons.Clock.Season, weather?.RainPermille ?? 0, bow);
	}
	public class MedievalSeasonAppearanceInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSeasonAppearance();
	}
	public sealed class MedievalSeasonAppearance : IRenderModifier
	{
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			var clock = self.World.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock;
			var tint = clock?.FoliageTint(self.ActorID, self.Info.Name == "md.pine") ?? Vector3.One;
			var alpha = self.Info.Name is "md.grasses" or "md.flowers" ? Math.Clamp(1 - (clock?.Snow ?? 0) / 650f, 0, 1) : 1;
			if (alpha <= 0) yield break;
			foreach (var r in renderables) yield return r is SpriteRenderable sprite ? sprite.WithTint(sprite.Tint * tint, sprite.TintModifiers).WithAlpha(sprite.Alpha * alpha)
				: r is WindVegetationRenderable plant ? plant.WithAppearance(tint, alpha) : r;
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) => bounds;
	}
}
