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
	public enum WildlifeKind { Rabbit, Deer }
	public sealed class WildAnimal
	{
		public WildlifeKind Kind;
		public Vector2 Position, Direction;
		public int Id, Age, Rest, Journey, Fright;
		public bool Moving { get; internal set; }
		public bool Running => Fright > 0 || Kind == WildlifeKind.Rabbit;
		public int Facing => ((int)MathF.Round(MathF.Atan2(Direction.Y, Direction.X) * 4 / MathF.PI) + 8) % 8;
		public int Frame => (Moving ? 96 : 0) + Facing * 12 + (Age / (Moving ? Running ? 2 : 5 : 9) + Id) % 12;
		public float Opacity => Math.Clamp(Age / 30f, 0, 1);
	}
	// These animals are scenery: no actors, pathfinder jobs, housing or shared RNG.
	public sealed class WildlifeScene
	{
		public const int MaxAnimals = 16;
		readonly Random random;
		readonly List<WildAnimal> animals = [];
		int tick, nextId;
		public IReadOnlyList<WildAnimal> Animals => animals;
		public int Spawned { get; private set; }
		public int Fled { get; private set; }
		public WildlifeScene(int seed) { random = new Random(seed); }
		Vector2 Direction()
		{
			var a = (float)random.NextDouble() * MathF.Tau;
			return new Vector2(MathF.Cos(a), MathF.Sin(a));
		}
		public void Tick(IReadOnlyList<Vector2> habitat, IReadOnlyList<Vector2> threats, Func<Vector2, bool> accepts, Func<Vector2, bool> nearby)
		{
			tick++;
			animals.RemoveAll(a => !nearby(a.Position) || !accepts(a.Position));
			if (tick % 12 == 1 && animals.Count < MaxAnimals && habitat.Count > 0)
			{
				for (var attempt = 0; attempt < 8; attempt++)
				{
					var at = habitat[random.Next(habitat.Count)];
					if (!accepts(at) || threats.Any(t => Vector2.DistanceSquared(t, at) < 3000 * 3000)
						|| animals.Any(a => Vector2.DistanceSquared(a.Position, at) < 900 * 900)) continue;
					var id = nextId++;
					animals.Add(new WildAnimal { Id = id, Kind = id % 3 == 0 ? WildlifeKind.Deer : WildlifeKind.Rabbit,
						Position = at, Direction = Direction(), Rest = random.Next(20, 140) });
					Spawned++; break;
				}
			}
			foreach (var animal in animals)
			{
				animal.Age++;
				var fear = animal.Kind == WildlifeKind.Deer ? 2700f : 1900f;
				if ((tick + animal.Id) % 5 == 0)
				{
					Vector2? nearest = null; var distance = fear * fear;
					foreach (var threat in threats)
					{
						var d = Vector2.DistanceSquared(threat, animal.Position);
						if (d < distance) { distance = d; nearest = threat; }
					}
					if (nearest.HasValue)
					{
						if (animal.Fright == 0) Fled++;
						var away = animal.Position - nearest.Value;
						animal.Direction = away.LengthSquared() < 1 ? Direction() : Vector2.Normalize(away);
						animal.Fright = 80; animal.Rest = 0; animal.Journey = Math.Max(animal.Journey, 80);
					}
				}
				if (animal.Fright > 0) animal.Fright--;
				animal.Moving = false;
				if (animal.Rest > 0) { animal.Rest--; continue; }
				if (animal.Journey <= 0) { animal.Journey = random.Next(35, 110); animal.Direction = Direction(); }
				var speed = animal.Kind == WildlifeKind.Deer ? animal.Running ? 92f : 29f : 61f;
				var next = animal.Position + animal.Direction * speed;
				if (accepts(next)) { animal.Position = next; animal.Moving = true; animal.Journey--; }
				else { animal.Direction = Direction(); animal.Journey = 0; }
				if (animal.Journey <= 0 && animal.Fright == 0) animal.Rest = random.Next(60, 210);
			}
		}
	}
	[TraitLocation(SystemActors.World)]
	public class MedievalWildernessInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalWilderness(init.World);
	}
	public sealed class MedievalWilderness : IWorldLoaded, ITick, IRender
	{
		public const int MaxPlants = 360;
		readonly World world;
		readonly List<(WPos Position, int Variant, uint Seed)> plants = [];
		readonly List<Vector2> habitat = [];
		readonly List<Vector2> threats = [];
		readonly HashSet<CPos> yards = [];
		WorldRenderer renderer;
		Sprite[] rabbits, deer, undergrowth;
		Rectangle lastView, sampledView;
		int tick;
		public WildlifeScene Scene { get; }
		public int PlantCount => plants.Count;
		public MedievalWilderness(World world) { this.world = world; Scene = new WildlifeScene(world.LocalRandom.Next()); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr;
			Sprite[] Load(string name)
			{
				var sequence = w.Map.Sequences.GetSequence("md.wild-" + name, "idle");
				return Enumerable.Range(0, sequence.Length).Select(i => sequence.GetSprite(i)).ToArray();
			}
			rabbits = Load("rabbit"); deer = Load("deer"); undergrowth = Load("undergrowth");
		}
		public static WPos Position(Vector2 at) => new((int)at.X, (int)at.Y, 0);
		static uint Hash(int x, int y)
		{
			var n = unchecked((uint)(x * 73856093 ^ y * 19349663));
			n = (n ^ n >> 16) * 0x45d9f3b; return n ^ n >> 16;
		}
		bool Free(CPos cell)
		{
			if (!world.Map.Contains(cell) || world.Map.GetTerrainInfo(cell).Type is not ("Clear" or "Rough") || yards.Contains(cell)) return false;
			if (world.WorldActor.TraitOrDefault<MedievalTrails>()?.ProtectsCell(cell) == true) return false;
			return !world.ActorMap.GetActorsAt(cell).Any(a => a.IsInWorld && !a.IsDead && a.TraitOrDefault<Building>() != null);
		}
		public bool Walkable(Vector2 at)
		{
			var pos = Position(at);
			return Free(world.Map.CellContaining(pos)) && Free(world.Map.CellContaining(pos + new WVec(150, 150, 0)))
				&& Free(world.Map.CellContaining(pos + new WVec(-150, -150, 0)));
		}
		bool Visible(WPos at) => !world.FogObscures(at) && lastView.Contains(renderer.ScreenPxPosition(at));
		bool Nearby(Vector2 at)
		{
			var pixel = renderer.ScreenPxPosition(Position(at));
			return pixel.X > lastView.Left - 220 && pixel.X < lastView.Right + 220 && pixel.Y > lastView.Top - 220 && pixel.Y < lastView.Bottom + 220;
		}
		bool Worn(WPos pos)
		{
			var trails = world.WorldActor.TraitOrDefault<MedievalTrails>()?.Trails;
			if (trails == null) return false;
			var key = FootfallTrails.Key(pos);
			for (var x = -1; x <= 1; x++)
				for (var y = -1; y <= 1; y++)
					if (trails.Marks.TryGetValue(key + new int2(x, y), out var mark) && mark.Level >= 1) return true;
			return false;
		}
		void Refresh()
		{
			yards.Clear();
			foreach (var pair in world.ActorsWithTrait<MedievalStockyard>())
				if (pair.Trait.HasArea && MedievalStockpile.Active(pair.Actor))
					foreach (var zone in pair.Trait.Zones) foreach (var cell in zone.Area.Cells()) yards.Add(cell);
			plants.Clear(); habitat.Clear();
			// A stable jittered grid grows with the viewport, never with the map area.
			var step = Math.Max(40, (int)MathF.Sqrt((float)lastView.Width * lastView.Height / 1100));
			// Use a fixed grid at the normal zoom; very wide views use its integer multiples.
			step = ((step + 39) / 40) * 40;
			for (var x = lastView.Left / step - 1; x <= lastView.Right / step + 1; x++)
				for (var y = lastView.Top / step - 1; y <= lastView.Bottom / step + 1; y++)
				{
					var hash = Hash(x * step, y * step);
					var pixel = new int2(x * step + (int)(hash % 29) - 14, y * step + (int)(hash >> 9 & 31) - 15);
					var pos = renderer.ProjectedPosition(pixel);
					if (!Free(world.Map.CellContaining(pos)) || world.FogObscures(pos)) continue;
					if (habitat.Count < 400 && hash % 3 == 0 && Walkable(new Vector2(pos.X, pos.Y))) habitat.Add(new Vector2(pos.X, pos.Y));
					if (hash % 100 < 42 && plants.Count < MaxPlants && !Worn(pos)) plants.Add((pos, (int)(hash >> 12) % 12, hash));
				}
		}
		void ITick.Tick(Actor self)
		{
			if (renderer == null || world.Type != WorldType.Regular) return;
			tick++;
			var view = Rectangle.FromLTRB(renderer.Viewport.TopLeft.X, renderer.Viewport.TopLeft.Y, renderer.Viewport.BottomRight.X, renderer.Viewport.BottomRight.Y);
			var changed = Math.Abs(view.Left - sampledView.Left) > 100 || Math.Abs(view.Top - sampledView.Top) > 100 || view.Size != sampledView.Size;
			lastView = view;
			if (tick % 25 == 1 || changed) { Refresh(); sampledView = view; }
			if (tick % 5 == 1 || changed)
			{
				threats.Clear();
				foreach (var actor in world.ScreenMap.RenderableActorsInBox(renderer.Viewport.TopLeft - new int2(140, 140), renderer.Viewport.BottomRight + new int2(140, 140)))
					if (!actor.IsDead && !world.FogObscures(actor) && actor.TraitOrDefault<Mobile>() != null && threats.Count < 256)
						threats.Add(new Vector2(actor.CenterPosition.X, actor.CenterPosition.Y));
			}
			// When hunting is enabled, rabbits and deer are finite synchronized actors.
			// Never show lookalike, replenishing scenery animals beside edible game.
			if (world.WorldActor.TraitOrDefault<MedievalHuntingGrounds>() == null) Scene.Tick(habitat, threats, Walkable, Nearby);
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (undergrowth == null || world.Type != WorldType.Regular) yield break;
			var wind = world.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene;
			foreach (var p in plants)
			{
				if (world.WorldActor.TraitOrDefault<MedievalSeasons>()?.Clock.Snow > 400) continue;
				if (!Visible(p.Position) || !Free(world.Map.CellContaining(p.Position)) || Worn(p.Position)) continue;
				var frame = p.Variant * 4 + (tick / 10 + (int)(p.Seed & 3)) % 4;
				yield return new WindVegetationRenderable(undergrowth[frame], p.Position, 0, 1, Vector3.One, wind?.Sway(p.Seed, .9f) ?? 0);
			}
			foreach (var animal in Scene.Animals)
			{
				var pos = Position(animal.Position);
				if (!Visible(pos)) continue;
				var sprite = (animal.Kind == WildlifeKind.Rabbit ? rabbits : deer)[animal.Frame];
				yield return new SpriteRenderable(sprite, pos, WVec.Zero, 0, null, 1, animal.Opacity, Vector3.One, TintModifiers.None, false);
			}
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr) { yield break; }
	}
}
