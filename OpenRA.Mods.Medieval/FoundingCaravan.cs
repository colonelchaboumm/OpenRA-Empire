using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalFoundingCaravanInfo : TraitInfo, Requires<MobileInfo>, Requires<MedievalStockpileInfo>
	{
		[ActorReference] public readonly string Market = "md.foundingmarket";
		[ActorReference] public readonly string[] Crew = ["md.builder", "md.builder", "md.hauler"];
		public readonly int DeployTicks = 75;
		public override object Create(ActorInitializer init) => new MedievalFoundingCaravan(this);
	}
	public sealed class MedievalFoundingCaravan : IIssueOrder, IIssueDeployOrder, IResolveOrder, IRenderAnnotations, ISync
	{
		public readonly MedievalFoundingCaravanInfo Info;
		[VerifySync] public int Progress;
		public Actor FoundedMarket { get; private set; }
		public string Status { get; private set; } = "Choisissez un terrain libre, puis Fonder le marche (ou Deploy).";
		public MedievalFoundingCaravan(MedievalFoundingCaravanInfo info) { Info = info; }
		public CPos Foundation(Actor self) => self.Location - new CVec(1, 1);
		public IEnumerable<CPos> CrewCells(Actor self)
		{
			var origin = Foundation(self);
			var dimensions = self.World.Map.Rules.Actors[Info.Market].TraitInfo<BuildingInfo>().Dimensions;
			for (var y = -1; y <= dimensions.Y; y++)
				for (var x = -1; x <= dimensions.X; x++)
				{
					if (x != -1 && x != dimensions.X && y != -1 && y != dimensions.Y) continue;
					var c = origin + new CVec(x, y);
					if (self.World.Map.Contains(c) && self.World.Map.GetTerrainInfo(c).Type is "Clear" or "Road" or "Rough"
						&& !self.World.ActorMap.GetActorsAt(c).Any(a => a != self)) yield return c;
				}
		}
		public bool CanFound(Actor self)
		{
			if (!self.IsInWorld || self.IsDead || self.WillDispose) return false;
			var actor = self.World.Map.Rules.Actors[Info.Market]; var building = actor.TraitInfo<BuildingInfo>();
			var cells = building.Tiles(Foundation(self)).ToArray();
			return cells.All(c => self.World.Map.Contains(c) && self.Owner.Shroud.IsExplored(c) && !MedievalStockyard.Covers(self.World, c) && !MedievalCemeteries.Covers(self.World, c))
				&& self.World.CanPlaceBuilding(Foundation(self), actor, building, self)
				&& !cells.SelectMany(self.World.ActorMap.GetActorsAt).Any(a => a != self && a.Info.HasTraitInfo<BuildingInfo>())
				&& CrewCells(self).Take(Info.Crew.Length).Count() == Info.Crew.Length;
		}
		public IEnumerable<IOrderTargeter> Orders
		{
			get { yield return new DeployOrderTargeter("MedievalFoundMarket", 5, () => "deploy"); }
		}
		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued) =>
			order.OrderID == "MedievalFoundMarket" ? new Order(order.OrderID, self, queued) : null;
		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued) => new("MedievalFoundMarket", self, queued);
		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued) => true;
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "MedievalFoundMarket" || Progress > 0 || self.World.Paused) return;
			if (!order.Queued && !CanFound(self)) { Status = "Il faut 3 x 2 cases libres et de la place autour pour les fondateurs."; return; }
			self.QueueActivity(order.Queued, new FoundMarket(this));
		}
		sealed class FoundMarket : Activity
		{
			readonly MedievalFoundingCaravan caravan;
			bool scheduled;
			public FoundMarket(MedievalFoundingCaravan caravan) { this.caravan = caravan; }
			protected override void OnLastRun(Actor self)
			{
				if (!scheduled) { caravan.Progress = 0; caravan.Status = "Installation annulee. La caravane conserve ses reserves."; }
			}
			public override bool Tick(Actor self)
			{
				if (IsCanceling || !caravan.CanFound(self))
				{
					caravan.Progress = 0; caravan.Status = "Installation interrompue. Deplacez la caravane vers un terrain libre."; return true;
				}
				caravan.Status = $"Installation du marche : {caravan.Progress * 100 / caravan.Info.DeployTicks}%";
				if (++caravan.Progress < caravan.Info.DeployTicks) return false;
				if (!scheduled)
				{
					scheduled = true; var owner = self.Owner;
					self.World.AddFrameEndTask(w =>
					{
						// The site may have been claimed by an earlier frame-end order.
						if (self.Owner != owner || !caravan.CanFound(self)) { caravan.Progress = 0; return; }
						var positions = caravan.CrewCells(self).Take(caravan.Info.Crew.Length).ToArray();
						var contents = Enumerable.Range(0, Inventory.ResourceCount).Select(self.Trait<MedievalStockpile>().Stock.Amount).ToArray();
						var health = self.Trait<Health>(); var hp = Math.Max(1, (int)(health.HP * 100L / health.MaxHP));
						var selected = w.Selection.Contains(self); var group = w.ControlGroups.GetControlGroupForActor(self);
						var location = caravan.Foundation(self);
						var hunger = self.Trait<MedievalNeedsFood>().Need;
						self.Dispose();
						var market = w.CreateActor(caravan.Info.Market, [new OwnerInit(owner), new LocationInit(location), new MedievalStockInit(contents), new HealthInit(hp)]);
						for (var i = 0; i < positions.Length; i++)
						{
							var settler = w.CreateActor(caravan.Info.Crew[i], [new OwnerInit(owner), new LocationInit(positions[i]), new HealthInit(hp)]);
							settler.Trait<MedievalNeedsFood>().Need.Inherit(hunger);
						}
						caravan.FoundedMarket = market; self.ReplacedByActor = market;
						if (selected) w.Selection.Add(market);
						if (group.HasValue) w.ControlGroups.AddToControlGroup(market, group.Value);
						MedievalAudio.Notify(market, "complete");
					});
				}
				return true;
			}
		}
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || self.World.FogObscures(self)) yield break;
			var color = CanFound(self) ? Color.LimeGreen : Color.Red;
			foreach (var c in self.World.Map.Rules.Actors[Info.Market].TraitInfo<BuildingInfo>().Tiles(Foundation(self)))
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(self.World, c, color)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
	public static class MedievalCaravanInterface
	{
		public static void Bind(Widget widget, World world)
		{
			Actor Selected() => world.Selection.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead && a.TraitOrDefault<MedievalFoundingCaravan>() != null);
			widget.Get("MD_FOUNDING_PANEL").IsVisible = () => Selected() != null;
			widget.Get<LabelWidget>("MD_FOUNDING_TITLE").GetText = () => "CARAVANE DE FONDATION";
			var button = widget.Get<ButtonWidget>("MD_FOUNDING_DEPLOY");
			button.GetText = () => Selected()?.Trait<MedievalFoundingCaravan>().Progress > 0 ? "Installation en cours..." : "Fonder le marche";
			button.IsDisabled = () => world.Paused || Selected() is not Actor a || a.Trait<MedievalFoundingCaravan>().Progress > 0;
			button.OnClick = () => { if (Selected() is Actor a) world.IssueOrder(new Order("MedievalFoundMarket", a, false)); };
			widget.Get<LabelWidget>("MD_FOUNDING_STATUS").GetText = () => Selected()?.Trait<MedievalFoundingCaravan>().Status ?? "";
			widget.Get<LabelWidget>("MD_FOUNDING_HINT").GetText = () => "2 batisseurs et 1 transporteur. Installation : 3 secondes.\nCommencez par un entrepot, puis une scierie et un bucheron.\nLe marche conserve les reserves reelles de la caravane.";
		}
	}
}
