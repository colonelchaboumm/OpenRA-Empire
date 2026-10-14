using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalStockInit : ValueActorInit<int[]>, ISingleInstanceInit
	{
		public MedievalStockInit(int[] value) : base(value) { }
	}

	public class MedievalStockpileInfo : TraitInfo
	{
		public readonly int[] Capacity = [200, 200, 200, 200, 200];
		public readonly int[] Initial = [0, 0, 0, 0];
		public readonly bool Warehouse = false;
		public readonly string Profession = "";
		public override object Create(ActorInitializer init) => new MedievalStockpile(init, this);
	}
	public class MedievalStockpile : ISync
	{
		public readonly MedievalStockpileInfo Info;
		public readonly Inventory Stock;
		[VerifySync] public int StockHash => Stock.Hash;
		public MedievalStockpile(ActorInitializer init, MedievalStockpileInfo info)
		{ Info = info; Stock = new Inventory(info.Capacity, init.GetValue<MedievalStockInit, int[]>(info.Initial)); }
		public static bool Active(Actor a) => a != null && a.IsInWorld && !a.IsDead
			&& (a.TraitOrDefault<MedievalBuilding>() is not { } b || b.Complete && !b.Demolishing);
	}

	public class MedievalConstructionInit : RuntimeFlagInit { }
	public class MedievalBuildingInfo : TraitInfo
	{
		[GrantedConditionReference] public readonly string CompletedCondition = "built";
		public readonly int[] Cost = [40, 0, 0, 10];
		public readonly int Cloth = 0;
		public readonly int Work = 40;
		public readonly int Population = 0;
		public readonly string ExpansionOf = "";
		public override object Create(ActorInitializer init) => new MedievalBuilding(init, this);
	}
	public class MedievalBuilding : ISync, INotifyCreated
	{
		public readonly MedievalBuildingInfo Info;
		public readonly Inventory Materials;
		public readonly Inventory CampMaterials;
		[VerifySync] public bool Complete;
		[VerifySync] public bool Demolishing;
		[VerifySync] public int Progress;
		[VerifySync] public int MaterialsHash => Materials.Hash;
		[VerifySync] public int CampMaterialsHash => CampMaterials.Hash;
		public MedievalBuilding(ActorInitializer init, MedievalBuildingInfo info)
		{
			Info = info;
			Materials = new Inventory(info.Cost);
			CampMaterials = new Inventory([0, 0, info.Cloth, 0]);
			Complete = !init.Contains<MedievalConstructionInit>(info);
		}
		void INotifyCreated.Created(Actor self)
		{
			if (Complete) self.GrantCondition(Info.CompletedCondition);
		}
		public bool Supplied => Enumerable.Range(0, Inventory.ResourceCount).All(r => Materials.Amount(r) == Materials.Capacity(r))
			&& CampMaterials.Amount(MedievalCampStore.Cloth) == Info.Cloth;
		public void Work(Actor self)
		{
			if (Complete || Demolishing || !Supplied) return;
			if (++Progress < Info.Work) return;
			Complete = true;
			// The materials remain recorded as incorporated, not spendable inventory.
			self.GrantCondition(Info.CompletedCondition);
		}
	}

	public enum NaturalResourceKind { None, Tree, Stone }
	public class MedievalSourceInfo : TraitInfo
	{
		public readonly Material Resource = Material.Wood;
		public readonly int Amount = 250;
		public readonly bool Renewable = false;
		public readonly int GrowthTicks = 900;
		public readonly int CultivationWork = 1;
		public readonly NaturalResourceKind Natural = NaturalResourceKind.None;
		public readonly int FallTicks = 40;
		public readonly int ExhaustedTicks = 50;
		[ActorReference] public readonly string RemnantActor = null;
		public override object Create(ActorInitializer init) => new MedievalSource(this);
	}
	public class MedievalSource : ITick, ISync, IRenderModifier, INotifyCreated
	{
		WithSpriteBody body;
		MedievalBuilding building;
		MedievalLivestock livestock;
		public readonly MedievalSourceInfo Info;
		[VerifySync] public int Remaining;
		[VerifySync] public int Growth;
		[VerifySync] public int Prepared;
		[VerifySync] public int FallAge = -1;
		[VerifySync] public int EmptyAge;
		[VerifySync] public int HarvestEffectTicks;
		[VerifySync] int growthRemainder;
		public MedievalSource(MedievalSourceInfo info)
		{
			if (info.Amount <= 0 || info.FallTicks < 1 || info.ExhaustedTicks < 1)
				throw new ArgumentException("Resource quantity and depletion durations must be positive.");
			if (info.Natural != NaturalResourceKind.None && info.Renewable)
				throw new ArgumentException("Natural deposits cannot regenerate automatically.");
			Info = info; Remaining = info.Renewable ? 0 : info.Amount;
		}
		void INotifyCreated.Created(Actor self)
		{
			body = self.TraitsImplementing<WithSpriteBody>().FirstOrDefault(b => b.Info.Name == "body");
			building = self.TraitOrDefault<MedievalBuilding>();
			livestock = self.TraitOrDefault<MedievalLivestock>();
		}
		public bool Falling => Info.Natural == NaturalResourceKind.Tree && FallAge >= 0 && FallAge < Info.FallTicks;
		public bool StandingTree => Info.Natural == NaturalResourceKind.Tree && FallAge < 0 && Remaining > 0;
		public bool NeedsWork => !Falling && (Remaining > 0 || (livestock != null ? livestock.CanProduce : Info.Renewable && Growth == 0));
		public float Fraction => Math.Clamp((float)Remaining / Info.Amount, 0, 1);
		public string Description => $"{MedievalStockDisplay.Labels[(int)Info.Resource]} : {Remaining}/{Info.Amount}"
			+ (Falling ? " | Abattage en cours" : Remaining == 0 ? " | Ressource epuisee" : Info.Natural == NaturalResourceKind.Tree && !StandingTree ? " | Tronc a debiter" : " | Reserve naturelle limitee")
			+ ". Aucune regeneration naturelle.";
		public int Harvest(int amount)
		{
			if (amount <= 0 || Falling) return 0;
			var result = Math.Min(amount, Remaining);
			Remaining -= result;
			if (result > 0)
			{
				HarvestEffectTicks = 12;
				if (Info.Natural == NaturalResourceKind.Tree && FallAge < 0) FallAge = 0;
			}
			return result;
		}
		public void Cultivate()
		{
			if (livestock != null) { if (Remaining == 0) Remaining = livestock.Produce(); return; }
			if (!Info.Renewable || Remaining != 0 || Growth != 0) return;
			if (++Prepared < Info.CultivationWork) return;
			Prepared = 0;
			Growth = Math.Max(1, Info.GrowthTicks);
		}
		public void Advance(int growthPercent = 100)
		{
			if (HarvestEffectTicks > 0) HarvestEffectTicks--;
			if (Falling) FallAge++;
			if (livestock == null && Info.Renewable && Growth > 0)
			{
				growthRemainder += Math.Clamp(growthPercent, 0, 200);
				Growth = Math.Max(0, Growth - growthRemainder / 100);
				growthRemainder %= 100;
				if (Growth == 0) { Remaining = Info.Amount; growthRemainder = 0; }
			}
			if (Info.Natural != NaturalResourceKind.None && Remaining == 0 && !Falling) EmptyAge++;
		}
		void ITick.Tick(Actor self)
		{
			// Untouched finite deposits have no growth, countdown or animation change.
			// Leave active felling, extraction, crops and exhaustion at full tick rate.
			if (Info.Natural != NaturalResourceKind.None && Remaining == Info.Amount && HarvestEffectTicks == 0 && FallAge < 0 && EmptyAge == 0) return;
			if (!self.IsInWorld || self.IsDead || building?.Complete == false) return;
			var season = self.World.WorldActor.TraitOrDefault<MedievalSeasons>();
			Advance(Info.Resource == Material.Food && season != null ? SeasonClock.Crops(season.Clock.Season) : 100);
			var sequence = Info.Natural == NaturalResourceKind.Tree ? StandingTree || Falling ? "idle" : "empty"
				: Remaining > 0 ? "idle" : Growth > 0 ? "growing" : "empty";
			if (body != null && body.DefaultAnimation.CurrentSequence.Name != sequence)
				body.PlayCustomAnimationRepeating(self, sequence);
			if (EmptyAge == Info.ExhaustedTicks && Info.RemnantActor != null)
				self.World.AddFrameEndTask(w =>
				{
					if (!self.IsInWorld || self.IsDead || Remaining != 0) return;
					w.CreateActor(Info.RemnantActor, [new OwnerInit(self.Owner), new LocationInit(self.Location)]);
					self.Dispose();
				});
		}
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			foreach (var renderable in renderables)
				if (Info.Natural != NaturalResourceKind.None && !StandingTree && renderable is SpriteRenderable sprite)
					yield return new NaturalResourceRenderable(self.Trait<WithSpriteBody>().DefaultAnimation.Image,
						self.World.Map.Sequences.GetSequence(Info.RemnantActor, "idle").GetSprite(0), sprite.Pos, sprite.ZOffset,
						sprite.Alpha, sprite.Tint, Info.Natural, Fraction, Falling ? (float)FallAge / Info.FallTicks : -1,
						HarvestEffectTicks, self.ActorID);
				else yield return renderable;
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			foreach (var b in bounds) yield return Info.Natural == NaturalResourceKind.Tree
				? Rectangle.FromLTRB(b.Left - 110, b.Top, b.Right + 110, b.Bottom + 85) : b;
		}
	}

	public class MedievalStockDisplayInfo : TraitInfo
	{
		public readonly int LabelHeight = 96;
		public override object Create(ActorInitializer init) => new MedievalStockDisplay(this);
	}
	public class MedievalStockDisplay : IRenderAnnotations, IRender
	{
		public static readonly string[] Labels = ["Bois", "Nourriture", "Viande", "Pierre", "Or"];
		readonly MedievalStockDisplayInfo info;
		public MedievalStockDisplay(MedievalStockDisplayInfo info) { this.info = info; }
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (self.World.FogObscures(self) || !MedievalStockpile.Active(self)) yield break;
			var stock = self.TraitOrDefault<MedievalStockpile>();
			if (stock != null)
			{
				var cart = self.TraitOrDefault<MedievalSupplyCart>() != null || self.TraitOrDefault<MedievalTradeCart>() != null;
				var resources = Enumerable.Range(0, Inventory.ResourceCount).Where(r => stock.Stock.Capacity(r) > 0).ToArray();
				for (var i = 0; i < resources.Length; i++)
				{
					var r = resources[i];
					var amount = MedievalStockyard.IndoorAmount(self, r);
					if (amount <= 0) continue;
					var capacity = self.TraitOrDefault<MedievalStockyard>()?.HasArea == true ? StorageGoods.BaseCapacity(self, r) : stock.Stock.Capacity(r);
					var level = Math.Clamp((amount * 4 + capacity - 1) / Math.Max(1, capacity), 1, 4);
					var sprite = self.World.Map.Sequences.GetSequence("md.piles", StockBadgeRenderable.ResourceSequences[r]).GetSprite(level - 1);
					var footprint = self.Info.TraitInfoOrDefault<BuildingInfo>()?.Dimensions ?? CVec.Zero;
					var offset = cart ? new WVec((i * 200) - 200, -400, 0) : new WVec((i * 700) - (resources.Length - 1) * 350, (footprint.X + footprint.Y) * 362, 0);
					if (self.TraitOrDefault<MedievalSiegeEngine>() is { Info.Trebuchet: true } siege)
						offset = MedievalSiegeCrew.ModelPoint(self, siege.Packed ? 0 : -32, siege.Packed ? 0 : -32) - self.CenterPosition + new WVec(0, 0, 650);
					yield return new SpriteRenderable(sprite, self.CenterPosition, offset, 1024, null,
						cart ? .5f : .75f, 1f, Vector3.One, TintModifiers.None, true);
				}
			}
			var worker = self.TraitOrDefault<MedievalWorker>();
			var crew = self.TraitOrDefault<MedievalSiegeCrew>();
			if (self.TraitOrDefault<MedievalSiegeEngine>() is { Deployed: true } loaded && loaded.Sling.Amount(3) > 0)
			{
				var stone = self.World.Map.Sequences.GetSequence("md.siegestone", "idle").GetSprite(0);
				var pos = SiegeShotMotion.SlingPosition(self);
				yield return new SpriteRenderable(stone, self.CenterPosition, pos - self.CenterPosition, 1024, null, .75f, 1f, Vector3.One, TintModifiers.None, true);
			}
			if (worker?.HandlingStock == true)
				yield return new StockHandlingRenderable(self, worker);
			else if ((worker?.Cargo > 0 || crew?.Cargo > 0) && self.TraitOrDefault<MedievalFirefighter>()?.Busy != true)
			{
				var equipment = worker?.EquipmentCargo == true;
				var resource = worker?.Resource ?? 3;
				var camp = worker?.CampCargo == true;
				var sprite = self.World.Map.Sequences.GetSequence(camp ? "md.campgoods" : equipment ? "md.equipment" : "md.resources", camp ? MedievalCampStore.Sequences[resource] : equipment ? MedievalEquipment.Sequences[resource] : StockBadgeRenderable.ResourceSequences[resource]).GetSprite(0);
				var body = self.TraitOrDefault<WithMedievalInfantryBody>();
				var phase = (body?.Frame ?? 0) * Math.PI / 4;
				var bob = body?.Sequence == "carry-run" ? (int)Math.Round(Math.Abs(Math.Sin(phase))) : 0;
				var ground = wr.ScreenPxPosition(self.CenterPosition);
				var carried = wr.ProjectedPosition(ground + new int2(0, -10 + bob));
				yield return new SpriteRenderable(sprite, self.CenterPosition, carried - self.CenterPosition, 1024, null,
					.5f, 1f, Vector3.One, TintModifiers.None, true);
			}
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr) { yield break; }
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (MedievalInfluence.StrategicView(wr) || self.World.FogObscures(self) || self.TraitOrDefault<MedievalRest>()?.Sleeping == true) yield break;
			var stock = self.TraitOrDefault<MedievalStockpile>();
			var building = self.TraitOrDefault<MedievalBuilding>();
			var worker = self.TraitOrDefault<MedievalWorker>();
			var food = self.TraitOrDefault<MedievalNeedsFood>();
			var source = self.TraitOrDefault<MedievalSource>();
			var selected = self.World.Selection.Contains(self);
			if (worker is { HandlingStock: true, HandlingCommitted: true } && wr.Viewport.Zoom >= .8f)
			{
				var age = Math.Clamp(worker.HandlingProgress * 2 - 1, 0, 1);
				var point = wr.Viewport.WorldToViewPx(wr.ScreenPxPosition(self.CenterPosition)) + new int2(16, -43 - (int)(age * 9));
				yield return new UITextRenderable(Game.Renderer.Fonts["TinyBold"], self.CenterPosition, point.ToVector2(), 1,
					Color.FromArgb((int)(240 * (1 - age)), 239, 218, 156), Color.FromArgb((int)(180 * (1 - age)), 0, 0, 0), Color.Transparent,
					(worker.HandlingPickup ? "-" : "+") + worker.HandlingAmount);
			}
			var plot = self.TraitOrDefault<MedievalAgriculturalArea>();
			var height = plot == null ? info.LabelHeight : (plot.Size.X + plot.Size.Y) * 6 + 26;
			var labelOffset = new int2(0, worker != null || food != null ? -34 : -height);
			if (self.TraitOrDefault<MedievalCartHeraldry>() is { } cartLivery)
				labelOffset = new int2(0, -(int)(cartLivery.LabelHeight * wr.Viewport.Zoom));
			if (self.TraitOrDefault<MedievalSiegeEngine>() is { Info.Trebuchet: true } siege) labelOffset = new int2(0, siege.Packed ? -62 : -135);
			var pos = wr.Viewport.WorldToViewPx(wr.ScreenPxPosition(self.CenterPosition)) + labelOffset;
			var text = "";
			if (building != null && !building.Complete)
				text = building.Supplied ? $"Construction {building.Progress}/{building.Info.Work}"
					: string.Join(" ", Enumerable.Range(0, Inventory.ResourceCount).Where(r => building.Materials.Capacity(r) > 0)
						.Select(r => $"{Labels[r]} {building.Materials.Amount(r)}/{building.Materials.Capacity(r)}"))
						+ (building.Info.Cloth > 0 ? $" Tissu {building.CampMaterials.Amount(MedievalCampStore.Cloth)}/{building.Info.Cloth}" : "");
			else if (stock != null && MedievalHudState.StockVisible(self))
			{
				var hasYard = self.TraitOrDefault<MedievalStockyard>()?.HasArea == true;
				var resources = Enumerable.Range(0, Inventory.ResourceCount).Where(r => stock.Stock.Capacity(r) > 0 && (!hasYard || MedievalStockyard.IndoorAmount(self, r) > 0)).ToArray();
				if (resources.Length > 0)
					yield return new StockBadgeRenderable(self.World, self.CenterPosition, labelOffset, resources,
						resources.Select(r => MedievalStockyard.IndoorAmount(self, r)).ToArray(), resources.Select(r => hasYard ? StorageGoods.BaseCapacity(self, r) : stock.Stock.Capacity(r)).ToArray());
				pos += new int2(0, -18);
			}
			else if (worker != null && (worker.Cargo > 0 || selected))
			{
				if (selected) text = worker.Cargo > 0 ? $"{worker.CargoName} {worker.Cargo}" : "";
				else if (worker.Cargo > 0 && MedievalHudState.StockVisible(self))
				{
					yield return new StockBadgeRenderable(self.World, self.CenterPosition, labelOffset,
						[worker.Resource], [worker.Cargo], [worker.Info.Capacity], worker.EquipmentCargo, worker.CampCargo);
					pos += new int2(0, -18);
				}
			}
			else if (source != null && selected)
				text = source.Falling ? "Abattage..." : source.Remaining > 0 ? $"{(plot?.Info.Profession == "shepherd" ? "Laine" : Labels[(int)source.Info.Resource])} {source.Remaining}/{source.Info.Amount}"
					: source.Growth > 0 ? $"Croissance {100 - source.Growth * 100 / source.Info.GrowthTicks}%" : source.Info.Renewable ? "Travail requis" : "Epuise";
			// Detailed assignments stay in the selection panel, avoiding a long
			// banner over adjacent buildings or underneath the sidebar.
			if (plot != null && selected)
				text = $"{plot.Size.X}x{plot.Size.Y}" + (text.Length > 0 ? " | " + text : "");
			if (food?.Need.Hungry == true) text = (food.Need.Starving ? "FAMINE" : "FAIM") + (text.Length > 0 ? " - " + text : "");
			if (text.Length == 0) yield break;
			var font = Game.Renderer.Fonts["TinyBold"];
			yield return new UITextRenderable(font, self.CenterPosition, (pos - font.Measure(text) / 2).ToVector2(), 0,
				food?.Need.Starving == true ? Color.OrangeRed : food?.Need.Hungry == true ? Color.Orange : Color.White, text);
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
}
