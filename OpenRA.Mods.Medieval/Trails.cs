using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct TrailWear(int Wear, int LastPass, int LastRecovery)
	{
		public int Level => Wear < 2 ? 0 : Wear < 5 ? 1 : Wear < 9 ? 2 : Wear < 14 ? 3 : Wear < 20 ? 4 : Wear < 28 ? 5 : 6;
	}

	// A fine world-space grid follows actual footsteps, independently of tile
	// boundaries. It does not modify terrain costs, production or collision.
	public sealed class FootfallTrails
	{
		public const int Spacing = 256;
		public const int MaximumWear = 32;
		readonly Dictionary<int2, TrailWear> marks = [];
		readonly Dictionary<uint, WPos> walkers = [];
		public event Action<int2> VisualChanged;
		public IReadOnlyDictionary<int2, TrailWear> Marks => marks;
		public int StateHash { get; private set; }
		public static int2 Key(WPos pos) => new(pos.X >> 8, pos.Y >> 8);
		public static WPos Position(int2 key) => new(key.X * Spacing + Spacing / 2, key.Y * Spacing + Spacing / 2, 0);
		static int Hash(int2 key, TrailWear mark) => unchecked(key.X * 73856093 ^ key.Y * 19349663 ^ mark.Wear * 83492791 ^ mark.LastPass * 31 ^ mark.LastRecovery * 17);
		void Set(int2 key, TrailWear value)
		{
			if (marks.TryGetValue(key, out var old)) StateHash ^= Hash(key, old);
			if (value.Wear <= 0) marks.Remove(key);
			else { marks[key] = value; StateHash ^= Hash(key, value); }
			if (old.Level != value.Level || value.Wear <= 0) VisualChanged?.Invoke(key);
		}
		public void Pass(int2 key, int tick, int weight)
		{
			if (weight <= 0) return;
			marks.TryGetValue(key, out var old);
			Set(key, new TrailWear(Math.Min(MaximumWear, old.Wear + weight), tick, tick));
		}
		public int Track(uint id, WPos position, int tick, int weight, Func<WPos, bool> accepts)
		{
			var known = walkers.TryGetValue(id, out var previous);
			walkers[id] = position;
			if (!known || previous == position) return 0;
			var delta = position - previous;
			// Spawning, teleports and layer changes must not draw across the map.
			if (delta.HorizontalLengthSquared > 2048L * 2048 || delta.Z != 0) return 0;
			var steps = Math.Max(1, (Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)) + 127) / 128);
			var last = Key(previous);
			var count = 0;
			for (var i = 1; i <= steps; i++)
			{
				var sample = WPos.Lerp(previous, position, i, steps);
				var key = Key(sample);
				if (key == last) continue;
				last = key;
				if (!accepts(Position(key))) continue;
				Pass(key, tick, weight);
				count++;
			}
			return count;
		}
		public void ForgetAbsent(ISet<uint> present)
		{
			foreach (var id in walkers.Keys.Where(id => !present.Contains(id)).ToArray()) walkers.Remove(id);
		}
		public void Recover(int tick, int delay, int interval)
		{
			foreach (var pair in marks.ToArray())
			{
				var mark = pair.Value;
				var start = Math.Max(mark.LastPass + delay, mark.LastRecovery);
				var loss = (tick - start) / interval;
				if (loss > 0) Set(pair.Key, mark with { Wear = mark.Wear - loss, LastRecovery = start + loss * interval });
			}
		}
	}

	[TraitLocation(SystemActors.World)]
	public class MedievalTrailsInfo : TraitInfo
	{
		public readonly int RecoveryDelay = 4500;
		public readonly int RecoveryInterval = 750;
		public readonly int CartWeight = 2;
		public override object Create(ActorInitializer init) => new MedievalTrails(init.Self, this);
	}

	public sealed class MedievalTrails : ITick, IWorldLoaded, ITickRender, IRenderOverlay, ISync, INotifyActorDisposing
	{
		readonly World world;
		readonly MedievalTrailsInfo info;
		readonly Dictionary<int2, (Vector3 Pixel, CPos Cell, int Frame)> visible = [];
		readonly HashSet<CPos> protectedCells = [];
		readonly Dictionary<uint, CPos[]> footprints = [];
		readonly Dictionary<CPos, int> buildingCoverage = [];
		readonly HashSet<int2> dirty = [];
		readonly Dictionary<CPos, HashSet<int2>> marksByCell = [];
		readonly Dictionary<PPos, HashSet<int2>> marksByShroud = [];
		Shroud shroud;
		Sprite[] sprites;
		int tick;
		int lastRenderTick = -1;
		public FootfallTrails Trails { get; } = new();
		public int ObservedLevel(int2 key) => visible.TryGetValue(key, out var mark) ? mark.Frame / 8 + 1 : 0;
		public IEnumerable<int2> ObservedKeys => visible.Keys;
		[VerifySync] public int StateHash => Trails.StateHash;
		public MedievalTrails(Actor self, MedievalTrailsInfo info)
		{
			world = self.World;
			this.info = info;
			world.ActorAdded += BuildingAdded;
			world.ActorRemoved += BuildingRemoved;
			world.RenderPlayerChanged += RenderPlayerChanged;
			Trails.VisualChanged += QueueMark;
			if (info.RecoveryDelay < 0 || info.RecoveryInterval <= 0 || info.CartWeight < 1)
				throw new ArgumentException("Trail recovery and cart weight must be positive.");
		}
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			foreach (var a in w.ActorsHavingTrait<Building>()) BuildingAdded(a);
			var sequence = w.Map.Sequences.GetSequence("md.trails", "idle");
			sprites = Enumerable.Range(0, sequence.Length).Select(i => sequence.GetSprite(i)).ToArray();
			RenderPlayerChanged(w.RenderPlayer);
		}
		void QueueMark(int2 key)
		{
			var cell = world.Map.CellContaining(FootfallTrails.Position(key));
			if (!marksByCell.TryGetValue(cell, out var keys)) marksByCell[cell] = keys = [];
			if (keys.Add(key))
				foreach (var projected in world.Map.ProjectedCellsCovering(cell.ToMPos(world.Map)))
				{
					if (!marksByShroud.TryGetValue(projected, out var projectedKeys)) marksByShroud[projected] = projectedKeys = [];
					projectedKeys.Add(key);
				}
			dirty.Add(key);
		}
		void ForgetMark(int2 key, CPos cell)
		{
			if (marksByCell.TryGetValue(cell, out var keys)) { keys.Remove(key); if (keys.Count == 0) marksByCell.Remove(cell); }
			foreach (var projected in world.Map.ProjectedCellsCovering(cell.ToMPos(world.Map)))
				if (marksByShroud.TryGetValue(projected, out var projectedKeys))
				{ projectedKeys.Remove(key); if (projectedKeys.Count == 0) marksByShroud.Remove(projected); }
		}
		void ShroudChanged(PPos cell) { if (marksByShroud.TryGetValue(cell, out var keys)) dirty.UnionWith(keys); }
		void InvalidateCell(CPos cell) { if (marksByCell.TryGetValue(cell, out var keys)) dirty.UnionWith(keys); }
		void RenderPlayerChanged(Player player)
		{
			if (shroud != null) shroud.OnShroudChanged -= ShroudChanged;
			shroud = player?.Shroud;
			if (shroud != null) shroud.OnShroudChanged += ShroudChanged;
			visible.Clear(); lastRenderTick = -1;
			foreach (var keys in marksByCell.Values) dirty.UnionWith(keys);
		}
		void BuildingAdded(Actor a)
		{
			if (footprints.ContainsKey(a.ActorID) || a.TraitOrDefault<Building>() is not Building building) return;
			var cells = building.Info.Tiles(a.Location).ToArray(); footprints.Add(a.ActorID, cells);
			foreach (var c in cells) { buildingCoverage.TryGetValue(c, out var count); buildingCoverage[c] = count + 1; InvalidateCell(c); }
		}
		void BuildingRemoved(Actor a)
		{
			if (!footprints.Remove(a.ActorID, out var cells)) return;
			foreach (var c in cells) { if (buildingCoverage[c] == 1) buildingCoverage.Remove(c); else buildingCoverage[c]--; InvalidateCell(c); }
		}
		bool Protected(CPos c) => buildingCoverage.ContainsKey(c) || protectedCells.Contains(c);
		public bool ProtectsCell(CPos c) => Protected(c);
		void INotifyActorDisposing.Disposing(Actor self)
		{
			world.ActorAdded -= BuildingAdded; world.ActorRemoved -= BuildingRemoved;
			world.RenderPlayerChanged -= RenderPlayerChanged; Trails.VisualChanged -= QueueMark;
			if (shroud != null) shroud.OnShroudChanged -= ShroudChanged;
		}
		bool Accepts(WPos pos)
		{
			var cell = world.Map.CellContaining(pos);
			return world.Map.Contains(cell) && !Protected(cell)
				&& world.Map.GetTerrainInfo(cell).Type is "Clear" or "Road" or "Rough";
		}
		void ITick.Tick(Actor self)
		{
			tick++;
			// Fixed footprints follow actor add/remove events. Only editable stockyards
			// need the periodic refresh, regardless of the number of trees on the map.
			if (tick % 25 == 1)
			{
				foreach (var cell in protectedCells) InvalidateCell(cell);
				protectedCells.Clear();
				foreach (var yard in world.ActorsWithTrait<MedievalStockyard>())
					if (yard.Trait.HasArea && MedievalStockpile.Active(yard.Actor))
						foreach (var zone in yard.Trait.Zones) foreach (var cell in zone.Area.Cells()) protectedCells.Add(cell);
				foreach (var cell in protectedCells) InvalidateCell(cell);
			}
			// The liveness set is only consumed by the periodic cleanup.
			var present = tick % 25 == 0 ? new HashSet<uint>() : null;
			foreach (var pair in world.ActorsWithTrait<Mobile>())
			{
				var a = pair.Actor;
				if (!a.IsInWorld || a.IsDead || a.TraitOrDefault<MedievalNeedsFood>() == null) continue;
				present?.Add(a.ActorID);
				Trails.Track(a.ActorID, a.CenterPosition, tick, a.TraitOrDefault<MedievalSupplyCart>() != null
					|| a.TraitOrDefault<MedievalTradeCart>() != null || a.TraitOrDefault<MedievalFoundingCaravan>() != null ? info.CartWeight : 1, Accepts);
			}
			if (tick % 25 != 0) return;
			Trails.ForgetAbsent(present);
			Trails.Recover(tick, info.RecoveryDelay, info.RecoveryInterval);
		}
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			if (sprites == null || lastRenderTick == tick) return;
			lastRenderTick = tick;
			// Only changed wear levels, footprints and visibility need refreshing. Do not
			// traverse the entire continent's accumulated footsteps every render tick.
			foreach (var key in dirty)
			{
				var exists = Trails.Marks.TryGetValue(key, out var mark);
				var level = exists ? mark.Level : 0;
				var variant = unchecked((key.X * 73 ^ key.Y * 151) & 7);
				var frame = (level - 1) * 8 + variant;
				var known = visible.TryGetValue(key, out var previous);
				// A world-space mark never moves. Reuse the observed projection while its
				// visible wear level is unchanged, including under fog. Hidden changes still
				// wait for visibility, so this cache reveals no unseen traffic.
				if (known && previous.Frame == frame && !Protected(previous.Cell)) continue;
				var pos = FootfallTrails.Position(key);
				var cell = known ? previous.Cell : world.Map.CellContaining(pos);
				if (!known && level == 0) { if (!exists) ForgetMark(key, cell); continue; }
				if (world.FogObscures(cell)) continue;
				if (level == 0 || Protected(cell)) { visible.Remove(key); if (!exists) ForgetMark(key, cell); continue; }
				var pixel = known ? previous.Pixel : wr.Screen3DPosition(pos);
				// Stable subpixel-free offsets break up regular sampling patterns.
				if (!known) pixel += new Vector3((variant % 3) - 1, (variant / 3) - 1, 0);
				visible[key] = (pixel, cell, frame);
			}
			dirty.Clear();
		}
		void IRenderOverlay.Render(WorldRenderer wr)
		{
			if (sprites == null) return;
			var tl = wr.Viewport.TopLeft;
			var br = wr.Viewport.BottomRight;
			foreach (var mark in visible.Values)
			{
				if (mark.Pixel.X < tl.X - 20 || mark.Pixel.X > br.X + 20 || mark.Pixel.Y < tl.Y - 12 || mark.Pixel.Y > br.Y + 12) continue;
				var sprite = sprites[mark.Frame];
				Game.Renderer.WorldSpriteRenderer.DrawSprite(sprite, null, mark.Pixel - sprite.Size * .5f);
			}
		}
	}
}
