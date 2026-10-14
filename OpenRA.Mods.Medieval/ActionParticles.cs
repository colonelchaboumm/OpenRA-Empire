using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Effects;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public enum VillageActionKind { Dust, Splash, Snow, Wood, Stone, Gold, Straw, Spark }

	public sealed class VillageActionBurst
	{
		public readonly WPos Position;
		public readonly Actor Source;
		public readonly VillageActionKind Kind;
		public readonly Vector2 Direction, Wind;
		public readonly int Seed;
		public int Age { get; internal set; }
		public int Lifetime => Kind is VillageActionKind.Dust or VillageActionKind.Snow ? 24 : 32;
		public float Opacity => Math.Clamp((Lifetime - Age) / 12f, 0, 1);
		public VillageActionBurst(WPos position, VillageActionKind kind, Vector2 direction, Vector2 wind, int seed, Actor source = null)
		{ Position = position; Kind = kind; Direction = direction; Wind = wind; Seed = seed; Source = source; }
	}

	// Local presentation only: bounded storage, no simulation RNG or resource changes.
	public sealed class VillageActionScene
	{
		public const int MaximumBursts = 96;
		public const int MaximumPerTick = 12;
		readonly List<VillageActionBurst> bursts = [];
		int emitted;
		public IReadOnlyList<VillageActionBurst> Bursts => bursts;
		public int[] Totals { get; } = new int[8];
		public bool Emit(VillageActionBurst burst)
		{
			if (emitted >= MaximumPerTick || bursts.Count >= MaximumBursts) return false;
			bursts.Add(burst); emitted++; Totals[(int)burst.Kind]++; return true;
		}
		public void Advance(int ticks)
		{
			if (ticks <= 0) return;
			emitted = 0;
			foreach (var b in bursts) b.Age += Math.Min(ticks, 1000);
			bursts.RemoveAll(b => b.Age >= b.Lifetime);
		}
		public static VillageActionKind? Footstep(string terrain, float snow, int rain) => terrain switch
		{
			"Clear" or "Rough" or "Road" or "Beach" => snow > .28f ? VillageActionKind.Snow
				: rain > 350 ? VillageActionKind.Splash : terrain == "Road" ? null : VillageActionKind.Dust,
			"Water" => VillageActionKind.Splash,
			_ => null
		};
		public static VillageActionKind? Work(string profession, bool harvesting) => profession switch
		{
			"lumberjack" => VillageActionKind.Wood,
			"stonecutter" => VillageActionKind.Stone,
			"goldminer" => VillageActionKind.Gold,
			"farmer" => harvesting ? VillageActionKind.Straw : VillageActionKind.Dust,
			"builder" => VillageActionKind.Stone,
			"blacksmith" => VillageActionKind.Spark,
			_ => null
		};
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalActionParticlesInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalActionParticles();
	}
	public sealed class MedievalActionParticles : IWorldLoaded, ITickRender
	{
		sealed class Observation
		{
			public WPos Position;
			public int Travel, Frame, Seen, Step;
			public string Sequence;
		}
		public const int MaximumObserved = 512;
		readonly Dictionary<uint, Observation> observed = [];
		readonly List<uint> expired = [];
		int lastTick = -1;
		public VillageActionScene Scene { get; } = new();
		public int Observed => observed.Count;
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr) => w.AddFrameEndTask(world => world.Add(new ActionParticleEffect(Scene)));
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			var w = self.World;
			if (w.Type != WorldType.Regular || w.Paused || lastTick == w.WorldTick) return;
			var elapsed = lastTick < 0 ? 1 : Math.Max(1, w.WorldTick - lastTick); lastTick = w.WorldTick;
			Scene.Advance(elapsed);
			if (wr.Viewport.Zoom < .65f) { observed.Clear(); return; }
			expired.Clear();
			foreach (var entry in observed)
				if (lastTick - entry.Value.Seen > 8) expired.Add(entry.Key);
			foreach (var id in expired) observed.Remove(id);
			var wind = w.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene.Wind ?? Vector2.Zero;
			var snow = w.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Snow ?? 0;
			var rain = w.WorldActor.TraitOrDefault<MedievalWeather>()?.RainPermille ?? 0;
			var heat = w.WorldActor.TraitOrDefault<MedievalFireEnvironment>()?.Heat;
			var inspected = 0;
			foreach (var a in w.ScreenMap.RenderableActorsInBox(wr.Viewport.TopLeft, wr.Viewport.BottomRight))
			{
				if (++inspected > 2048) break;
				var body = a.TraitOrDefault<WithMedievalInfantryBody>();
				if (body == null || !Visible(a, w)) continue;
				if (!observed.TryGetValue(a.ActorID, out var previous))
				{
					if (observed.Count < MaximumObserved) observed.Add(a.ActorID, new Observation { Position = a.CenterPosition,
						Frame = body.Frame, Sequence = body.Sequence, Seen = lastTick });
					continue;
				}
				var position = a.CenterPosition; var delta = position - previous.Position;
				var travel = delta.HorizontalLength;
				var mobile = a.Trait<Mobile>();
				var moving = mobile.CurrentMovementTypes.HasMovementType(MovementType.Horizontal);
				if (moving && travel > 0 && travel <= 1024 && delta.Z == 0 && lastTick - previous.Seen <= 8)
				{
					previous.Travel += travel;
					if (previous.Travel >= MedievalGait.CycleDistance / 2)
					{
						previous.Travel %= MedievalGait.CycleDistance / 2; previous.Step++;
						var cell = w.Map.CellContaining(position);
						var cover = snow == 0 ? 0 : SeasonClock.SnowCoverage(position.X, position.Y, snow) * (heat?.SnowRemaining(position) ?? 1);
						var kind = w.Map.Contains(cell) ? VillageActionScene.Footstep(w.Map.GetTerrainInfo(cell).Type, cover, rain) : null;
						if (kind.HasValue)
						{
							var forward = delta * 1024 / travel;
							var foot = position + new WVec(-forward.Y, forward.X, 0) * (previous.Step % 2 == 0 ? 85 : -85) / 1024;
							Emit(a, kind.Value, foot, delta, wind, wr);
						}
					}
				}
				else if (travel > 1024 || !moving) previous.Travel = 0;
				var worker = a.TraitOrDefault<MedievalWorker>();
				// Emission follows the tool's contact pose, including hunger-slowed work.
				if (!moving && worker?.IsWorking == true && body.Sequence is "work" or "harvest"
					&& previous.Sequence == body.Sequence && previous.Frame < 5 && body.Frame >= 5 && body.Frame <= 6)
				{
					var kind = VillageActionScene.Work(worker.Info.Profession, worker.IsHarvesting);
					if (kind.HasValue)
					{
						var forward = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(mobile.Facing));
						Emit(a, kind.Value, position + forward * 320 / 1024, forward, wind, wr);
					}
				}
				previous.Position = position; previous.Frame = body.Frame; previous.Sequence = body.Sequence; previous.Seen = lastTick;
			}
		}
		void Emit(Actor a, VillageActionKind kind, WPos position, WVec direction, Vector2 wind, WorldRenderer wr)
		{
			var projected = wr.Screen3DPosition(position + direction) - wr.Screen3DPosition(position);
			var d = new Vector2(projected.X, projected.Y); if (d.LengthSquared() > .01f) d = Vector2.Normalize(d);
			Scene.Emit(new VillageActionBurst(position, kind, d, wind, unchecked((int)a.ActorID * 397 + lastTick), a));
		}
		internal static bool Visible(Actor a, World w) => a.IsInWorld && !a.IsDead && !w.FogObscures(a)
			&& (w.RenderPlayer == null || a.CanBeViewedByPlayer(w.RenderPlayer))
			&& a.TraitOrDefault<MedievalRest>()?.Sleeping != true && a.TraitOrDefault<MedievalPatient>()?.Church == null;
	}

	sealed class ActionParticleEffect(VillageActionScene scene) : IEffect
	{
		public void Tick(World world) { }
		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			if (wr.Viewport.Zoom < .65f) yield break;
			foreach (var burst in scene.Bursts)
			{
				var p = wr.ScreenPxPosition(burst.Position);
				if (p.X < wr.Viewport.TopLeft.X - 24 || p.X > wr.Viewport.BottomRight.X + 24
					|| p.Y < wr.Viewport.TopLeft.Y - 32 || p.Y > wr.Viewport.BottomRight.Y + 32
					|| wr.World.FogObscures(burst.Position) || burst.Source != null && !MedievalActionParticles.Visible(burst.Source, wr.World)) continue;
				yield return new ActionParticleRenderable(burst.Position, 1, burst);
			}
		}
	}

	sealed class ActionParticleRenderable(WPos position, int z, VillageActionBurst burst) : IRenderable, IFinalizedRenderable
	{
		public WPos Pos => position;
		public int ZOffset => z;
		public bool IsDecoration => true;
		public IRenderable WithZOffset(int value) => new ActionParticleRenderable(position, value, burst);
		public IRenderable OffsetBy(in WVec offset) => new ActionParticleRenderable(position + offset, z, burst);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new Rectangle(p.X - 25, p.Y - 34, 50, 60); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.WorldRgbaColorRenderer; var at = wr.Screen3DPosition(Pos);
			var t = burst.Age; var kind = burst.Kind; var opacity = burst.Opacity;
			var phase = (burst.Seed & 1023) * .031f;
			var snow = kind == VillageActionKind.Snow;
			var splash = kind == VillageActionKind.Splash;
			var spark = kind == VillageActionKind.Spark;
			if (kind == VillageActionKind.Dust || snow)
			{
				for (var i = 0; i < 3; i++)
				{
					var angle = phase + i * 2.1f;
					var p = at + new Vector3(MathF.Cos(angle) * (1 + t * .16f) - burst.Direction.X * t * .07f + burst.Wind.X * t * .1f,
						MathF.Sin(angle) * (1 + t * .07f) - t * .12f, 0);
					var radius = .7f + t * .12f;
					for (var layer = 2; layer >= 0; layer--)
					{
						var size = new Vector3(radius * (1 + layer * .3f), radius * (.45f + layer * .2f), 0);
						cr.FillEllipse(p - size, p + size, Color.FromArgb((int)(opacity * (layer == 0 ? 55 : 17)), snow ? 236 : 168, snow ? 244 : 148, snow ? 250 : 107));
					}
				}
				if (!snow) return;
			}
			if (splash && t < 18)
			{
				var radius = 1.5f + t * .32f;
				var color = Color.FromArgb((int)((1 - t / 18f) * 120), 190, 214, 219);
				// Broken elliptical ripples remain in the isometric ground plane.
				for (var i = 0; i < 8; i++)
				{
					var a = i * MathF.Tau / 8;
					cr.DrawLine(at + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius * .45f, 0),
						at + new Vector3(MathF.Cos(a + .5f) * radius, MathF.Sin(a + .5f) * radius * .45f, 0), .7f, color);
				}
			}
			for (var i = 0; i < (spark ? 5 : 4); i++)
			{
				var angle = phase + i * 2.399f;
				var flight = 10 + (i + (burst.Seed & 7)) % 7;
				var u = t / (float)flight;
				var reach = (spark ? 13 : snow || splash ? 5 : 10) + i;
				var progress = Math.Min(1.16f, u);
				var height = u < 1 ? 4 * u * (1 - u) * (spark ? 10 : splash ? 4 : 6) : u < 1.3f ? MathF.Sin((u - 1) / .3f * MathF.PI) * 1.3f : 0;
				if (!splash && !snow) height += Math.Max(0, 1 - u) * 7;
				var point = at + new Vector3(MathF.Cos(angle) * reach * progress + burst.Wind.X * t * .035f,
					MathF.Sin(angle) * reach * progress * .48f - height, 0);
				var alpha = opacity * (spark ? Math.Max(0, 1 - t / 23f) : 1);
				if (alpha <= 0) continue;
				var rgb = kind switch
				{
					VillageActionKind.Wood => new Vector3(190, 140, 72),
					VillageActionKind.Straw => new Vector3(224, 192, 83),
					VillageActionKind.Gold => i % 2 == 0 ? new Vector3(240, 202, 76) : new Vector3(157, 151, 129),
					VillageActionKind.Spark => new Vector3(255, 212 - t * 3, 104 - Math.Min(70, t * 4)),
					VillageActionKind.Snow => new Vector3(235, 245, 252),
					VillageActionKind.Splash => new Vector3(176, 206, 220),
					_ => new Vector3(184, 181, 166)
				};
				var color = Color.FromArgb((int)(alpha * 210), (int)rgb.X, (int)rgb.Y, (int)rgb.Z);
				if (spark)
				{
					cr.FillEllipse(point - new Vector3(2, 2, 0), point + new Vector3(2, 2, 0), Color.FromArgb((int)(alpha * 35), 255, 123, 30));
					cr.DrawLine(point, point - new Vector3(MathF.Cos(angle) * 2, MathF.Sin(angle) - .8f, 0), 1, color);
				}
				else
				{
					var rotation = angle + Math.Min(u, 1.3f) * 4;
					var tip = new Vector3(MathF.Cos(rotation), MathF.Sin(rotation), 0) * (kind is VillageActionKind.Wood or VillageActionKind.Straw ? 1.6f : .9f);
					var side = new Vector3(-tip.Y, tip.X, 0) * (kind == VillageActionKind.Straw ? .15f : .45f);
					cr.FillRect(point - tip, point + side, point + tip, point - side, color);
				}
			}
		}
	}
}
