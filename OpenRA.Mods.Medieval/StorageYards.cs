using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalStockyardInfo : TraitInfo, Requires<MedievalStockpileInfo>, Requires<BuildingInfo>
	{
		public readonly int MaxDistance = 6;
		public readonly int MinimumSize = 4;
		public override object Create(ActorInitializer init) => new MedievalStockyard(this);
	}
	public sealed class MedievalStockyard : IResolveOrder, IRender, IRenderAnnotations, ISync
	{
		public const int MaxZones = 8;
		readonly MedievalStockyardInfo info;
		readonly List<StorageZone> zones = [];
		[VerifySync] int nextId = 1;
		[VerifySync] public int ZonesHash { get { unchecked { return zones.Aggregate(17, (h, z) => h * 31 + z.Hash); } } }
		public IReadOnlyList<StorageZone> Zones => zones;
		public bool HasArea => zones.Count > 0;
		public StockyardArea Area => HasArea ? zones[0].Area : default;
		public int MinimumSize => info.MinimumSize;
		public string Status { get; private set; } = "Choisissez le type de reserve puis tracez ses deux coins.";
		public MedievalStockyard(MedievalStockyardInfo info) { this.info = info; }
		public StorageZone At(CPos cell) => zones.FirstOrDefault(z => z.Area.Contains(cell));
		public StorageZone Find(int id) => zones.FirstOrDefault(z => z.Id == id);
		public int Amount(Actor self, StorageZone zone, int good) => StorageAllocation.Outside(zones, zone.Id, good, StorageGoods.Amount(self, good));
		public int Inside(Actor self, int good) => StorageAllocation.Inside(zones, good, StorageGoods.Amount(self, good));
		public static int IndoorAmount(Actor self, int good) => self.TraitOrDefault<MedievalStockyard>() is { } yard ? yard.Inside(self, good) : StorageGoods.Amount(self, good);
		public static bool Covers(World world, CPos cell, Actor except = null) => world.ActorsWithTrait<MedievalStockyard>()
			.Any(p => p.Actor != except && MedievalStockpile.Active(p.Actor) && p.Trait.At(cell) != null);
		public bool Supports(Actor self, StorageKind kind) => Enum.IsDefined(kind) && StorageGoods.Types(kind).All(g => StorageGoods.Stock(self, g) != null);
		int[] Capacities(Actor self, IEnumerable<StorageZone> proposed, int first, int count)
		{
			var result = new int[Inventory.ResourceCount];
			for (var r = 0; r < count; r++) result[r] = StorageGoods.BaseCapacity(self, first + r) + proposed.Sum(z => z.Capacity(first + r));
			return result;
		}
		bool Fits(Actor self, IEnumerable<StorageZone> proposed)
		{
			foreach (var (first, count) in new[] { (0, 5), (5, 3), (8, MedievalCampStore.GoodsCount) })
				if (StorageGoods.Stock(self, first) is { } stock && !stock.CanSetCapacity(Capacities(self, proposed, first, count))) return false;
			return true;
		}
		void RefreshCapacity(Actor self)
		{
			foreach (var (first, count) in new[] { (0, 5), (5, 3), (8, MedievalCampStore.GoodsCount) })
				StorageGoods.Stock(self, first)?.SetCapacity(Capacities(self, zones, first, count));
			self.World.ScreenMap.AddOrUpdate(self);
		}
		int ResolveReplacement(int replaceId) => replaceId < 0 ? zones.FirstOrDefault()?.Id ?? 0 : replaceId;
		public string PlacementError(Actor self, StockyardArea area, StorageKind kind = StorageKind.Mixed, int replaceId = -1)
		{
			replaceId = ResolveReplacement(replaceId);
			if (!MedievalStockpile.Active(self)) return "Un batiment termine est necessaire.";
			if (self.TraitOrDefault<MedievalBurnable>()?.Burning == true) return "Eteignez l'incendie avant de modifier les reserves.";
			if (!Enum.IsDefined(kind) || StorageGoods.Types(kind).Any(g => StorageGoods.Stock(self, g) == null)) return "Ce batiment ne gere pas ces marchandises.";
			if (replaceId != 0 && Find(replaceId) == null) return "Cette zone n'existe plus.";
			if (replaceId == 0 && zones.Count >= MaxZones) return $"Maximum {MaxZones} zones par batiment.";
			var minimum = kind == StorageKind.Mixed ? MinimumSize : 2;
			if (!area.ValidSizeFor(minimum)) return $"Choisissez de {minimum} a 10 cases par cote.";
			if (MedievalAgriculturalArea.DistanceToRectangle(self.Location, self.Info.TraitInfo<BuildingInfo>().Dimensions, area.Start, area.Size) > info.MaxDistance * info.MaxDistance)
				return $"Rapprochez l'aire : {info.MaxDistance} cases maximum du batiment.";
			var cells = area.Cells().ToHashSet();
			if (cells.Any(c => !self.World.Map.Contains(c) || !self.Owner.Shroud.IsExplored(c))) return "L'aire doit rester sur le terrain explore.";
			if (cells.Any(c => self.World.Map.GetTerrainInfo(c).Type is not ("Clear" or "Road" or "Rough"))) return "Terrain incompatible avec le stockage.";
			if (cells.Any(c => MedievalCemeteries.Covers(self.World, c))) return "Un cimetiere occupe cet emplacement.";
			if (self.World.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Info.TraitInfoOrDefault<BuildingInfo>() is BuildingInfo building && building.Tiles(a.Location).Any(cells.Contains)))
				return "L'aire recouvre un batiment, une culture ou une ressource.";
			if (zones.Any(z => z.Id != replaceId && z.Area.Cells().Any(cells.Contains)) || cells.Any(c => Covers(self.World, c, self))) return "Une autre reserve occupe cet emplacement.";
			var proposed = zones.Where(z => z.Id != replaceId).Append(new StorageZone(replaceId, area, kind)).ToArray();
			if (!Fits(self, proposed)) return "Trop de stock ou de livraisons en route pour reduire cette reserve.";
			return null;
		}
		public string RemoveError(Actor self, int id)
		{
			if (!MedievalStockpile.Active(self) || Find(id) == null) return "Zone indisponible.";
			if (self.TraitOrDefault<MedievalBurnable>()?.Burning == true) return "Eteignez l'incendie avant de retirer une reserve.";
			return Fits(self, zones.Where(z => z.Id != id)) ? null : "Videz le surplus et attendez les livraisons avant de retirer cette zone.";
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString is not ("MedievalPlaceStockyard" or "MedievalRemoveStockyard")) return;
			if (!MedievalStockpile.Active(self)) return;
			var owner = self.Owner;
			var id = order.TargetString == null || order.TargetString.Length == 0 ? ResolveReplacement(-1) : (int)order.ExtraData;
			var kind = StorageKind.Mixed;
			if (!string.IsNullOrEmpty(order.TargetString) && order.TargetString != "remove" && !Enum.TryParse(order.TargetString, out kind)) return;
			if (order.OrderString == "MedievalRemoveStockyard")
			{
				var error = RemoveError(self, id); if (error != null) { Status = error; return; }
				zones.RemoveAll(z => z.Id == id); RefreshCapacity(self); Status = "Zone retiree, toutes les marchandises sont conservees."; return;
			}
			if (order.Target.Type != TargetType.Terrain) return;
			var area = StockyardArea.Between(self.World.Map.CellContaining(order.Target.CenterPosition), order.ExtraLocation);
			self.World.AddFrameEndTask(_ =>
			{
				if (!MedievalStockpile.Active(self) || self.Owner != owner) return;
				var error = PlacementError(self, area, kind, id); if (error != null) { Status = error; return; }
				var zone = new StorageZone(id == 0 ? nextId++ : id, area, kind);
				var at = zones.FindIndex(z => z.Id == id); if (at >= 0) zones[at] = zone; else zones.Add(zone);
				RefreshCapacity(self); Status = $"{StorageGoods.KindNames[(int)kind]} : {area.Size.X}x{area.Size.Y}. Capacite ajoutee.";
			});
		}
		// Burn only goods allocated to the targeted zone. Damaged pallets lose the
		// same capacity: other zones cannot silently refill the burning stack.
		public int BurnAt(Actor self, CPos cell)
		{
			var zone = At(cell); if (zone == null) return 0;
			var lost = 0;
			for (var good = 0; good < StorageGoods.Count; good++)
			{
				if (!StorageGoods.Flammable(good)) continue;
				var stock = StorageGoods.Stock(self, good); if (stock == null) continue;
				var n = stock.Take(StorageGoods.Index(good), Math.Min(good < 5 || good == 8 ? 4 : 1, Amount(self, zone, good)));
				zone.Scorch(good, n); lost += n;
			}
			if (lost > 0) RefreshCapacity(self);
			return lost;
		}
		internal static IEnumerable<IRenderable> RenderArea(Actor warehouse, StockyardArea area, bool preview = false, bool valid = true, StorageKind kind = StorageKind.Mixed)
		{
			var yard = warehouse.Trait<MedievalStockyard>();
			var zone = (preview ? null : yard.Zones.FirstOrDefault(z => z.Area == area)) ?? new StorageZone(0, area, kind);
			return RenderZone(warehouse, zone, preview, valid);
		}
		static IEnumerable<IRenderable> RenderZone(Actor self, StorageZone zone, bool preview = false, bool valid = true)
		{
			var world = self.World; var yard = self.Trait<MedievalStockyard>();
			var tint = preview && !valid ? new Vector3(1, .45f, .4f) : zone.Damaged ? new Vector3(.67f, .60f, .49f) : Vector3.One;
			foreach (var (cell, good, amount) in zone.Stacks(g => preview ? 0 : yard.Amount(self, zone, g)))
			{
				if (!world.Map.Contains(cell) || !preview && world.FogObscures(cell)) continue;
				var pos = world.Map.CenterOfCell(cell);
				var floor = world.Map.Sequences.GetSequence("md.stockyard-floor", "idle").GetSprite(good < 4 ? good : 0);
				yield return new SpriteRenderable(floor, pos, WVec.Zero, -8192, null, 1, preview ? .55f : 1, tint, TintModifiers.None, false);
				if (preview || amount == 0) continue;
				if (good < 5)
				{
					var sprite = world.Map.Sequences.GetSequence(good == 4 ? "md.gold-stock" : "md.stockyard-stock", good == 4 ? "idle" : StockBadgeRenderable.ResourceSequences[good]).GetSprite(amount);
					yield return new SpriteRenderable(sprite, pos, WVec.Zero, 0, null, 1, 1, Vector3.One, TintModifiers.None, false);
				}
				else
				{
					var sprite = world.Map.Sequences.GetSequence(good < 8 ? "md.equipment" : "md.campgoods", good < 8 ? MedievalEquipment.Sequences[good - 5] : MedievalCampStore.Sequences[good - 8]).GetSprite(0);
					for (var i = 0; i < amount; i++)
						yield return new SpriteRenderable(sprite, pos, new WVec((i % 4 - 1) * 190, (i % 16 / 4 - 1) * 190, 150 + i / 16 * 100), 1, null, .8f, 1, Vector3.One, TintModifiers.None, false);
				}
			}
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (!MedievalStockpile.Active(self)) yield break;
			foreach (var zone in zones) foreach (var r in RenderZone(self, zone)) yield return r;
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			foreach (var zone in zones) foreach (var cell in zone.Area.Cells())
			{ var p = wr.ScreenPxPosition(self.World.Map.CenterOfCell(cell)); yield return new Rectangle(p.X - 32, p.Y - 65, 64, 90); }
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!MedievalStockpile.Active(self)) yield break;
			foreach (var zone in zones)
			{
				foreach (var good in StorageGoods.Types(zone.Kind))
				{
					var cell = zone.Slots.FirstOrDefault(s => s.Good == good).Cell;
					if (self.World.FogObscures(cell)) continue;
					yield return new StockBadgeRenderable(self.World, self.World.Map.CenterOfCell(cell), new int2(0, -45), [StorageGoods.Index(good)],
						[Amount(self, zone, good)], [zone.Capacity(good)], equipment: good is >= 5 and < 8, camp: good >= 8);
				}
				if (self.World.Selection.Contains(self)) foreach (var cell in zone.Area.Cells())
					if (!self.World.FogObscures(cell)) foreach (var line in MedievalPlaceOrderGenerator.CellOutline(self.World, cell, Color.FromArgb(90, 219, 195, 129))) yield return line;
			}
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
	public sealed class MedievalStockyardOrderGenerator : OrderGenerator
	{
		readonly Actor warehouse;
		readonly WorldRenderer renderer;
		readonly StorageKind kind;
		readonly int replaceId;
		CPos? first;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalStockyardOrderGenerator(World world, WorldRenderer renderer, Actor warehouse, StorageKind kind = StorageKind.Mixed, int replaceId = -1) : base(world)
		{ this.renderer = renderer; this.warehouse = warehouse; this.kind = kind; this.replaceId = replaceId < 0 ? warehouse.Trait<MedievalStockyard>().Zones.FirstOrDefault()?.Id ?? 0 : replaceId; }
		CPos Cell => renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
		int Minimum => kind == StorageKind.Mixed ? warehouse.Trait<MedievalStockyard>().MinimumSize : 2;
		StockyardArea Area(CPos cell) => StockyardArea.Between(first ?? cell, first.HasValue ? cell : cell + new CVec(Minimum - 1, Minimum - 1));
		bool OwnsWarehouse => MedievalStockpile.Active(warehouse) && warehouse.Owner == warehouse.World.LocalPlayer;
		string Error(StockyardArea area) => warehouse.Trait<MedievalStockyard>().PlacementError(warehouse, area, kind, replaceId);
		bool Valid(StockyardArea area) => OwnsWarehouse && Error(area) == null;
		public string PreviewText()
		{
			if (!OwnsWarehouse) return "Batiment indisponible.";
			var area = Area(Cell); var zone = area.ValidSizeFor(Minimum) ? new StorageZone(0, area, kind) : null;
			var capacity = zone == null ? "" : string.Join(" / ", StorageGoods.Types(kind).Select(g => $"{zone.Capacity(g)} {StorageGoods.Names[g]}"));
			return $"{(first.HasValue ? "Second coin" : "Premier coin")} : {StorageGoods.KindNames[(int)kind]} {area.Size.X}x{area.Size.Y}. " + (Error(area) ?? "Capacite : +" + capacity) + " | Clic droit : annuler.";
		}
		protected override void Tick(World world) { if (!OwnsWarehouse) world.CancelInputMode(); }
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (!OwnsWarehouse || world.Paused) { yield break; }
			if (!first.HasValue) { if (world.Map.Contains(cell) && warehouse.Owner.Shroud.IsExplored(cell)) first = cell; yield break; }
			if (!Valid(Area(cell))) yield break;
			yield return new Order("MedievalPlaceStockyard", warehouse, Target.FromCell(world, first.Value), false) { ExtraLocation = cell, TargetString = kind.ToString(), ExtraData = (uint)replaceId };
			world.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
		{
			if (!OwnsWarehouse) yield break; var area = Area(Cell);
			if (area.ValidSizeFor(Minimum)) foreach (var r in MedievalStockyard.RenderArea(warehouse, area, true, Valid(area), kind)) yield return r;
		}
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (!OwnsWarehouse) yield break; var area = Area(Cell);
			if (area.Size.X > 32 || area.Size.Y > 32) yield break; var valid = Valid(area);
			foreach (var cell in area.Cells()) if (world.Map.Contains(cell)) foreach (var r in MedievalPlaceOrderGenerator.CellOutline(world, cell, valid ? Color.LimeGreen : Color.Red)) yield return r;
		}
		protected override string GetCursor(World world, CPos cell, int2 pixel, MouseInput mi) => Valid(Area(cell)) ? "ability" : "move-blocked";
	}
}
