using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalConstructionConvoyInfo : TraitInfo, Requires<MedievalSupplyCartInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalConstructionConvoy();
	}
	public sealed class MedievalConstructionConvoy : ITick, IResolveOrder, INotifyOwnerChanged, ISync
	{
		Actor site;
		Actor market;
		[VerifySync] int cadence;
		[VerifySync] int attempts;
		[VerifySync] public bool Active { get; private set; }
		[VerifySync] public bool Loading { get; private set; }
		[VerifySync] public int Trips { get; private set; }
		[VerifySync] public int SiteId => unchecked((int)(site?.ActorID ?? 0));
		[VerifySync] public int MarketId => unchecked((int)(market?.ActorID ?? 0));
		public string Status { get; private set; } = "Choisissez un chantier pour organiser des allers-retours.";
		public bool CanLoad(Actor self) => Active && Loading && MedievalLogistics.Market(market, self.Owner) && MedievalAccess.Adjacent(self, market);
		public bool Supplies(WPos position) => Active && !Loading && site?.IsInWorld == true && site.CenterPosition == position;
		int Needed(int r) => site?.IsInWorld == true && site.TraitOrDefault<MedievalBuilding>() is { Demolishing: false } building
			? Math.Max(0, building.Materials.Capacity(r) - building.Materials.Amount(r) - building.Materials.Incoming(r)) : 0;
		public int Desired(Actor self, int r) => r == 1 ? 20 : r is 0 or 3 or 4 ? Math.Min(self.Trait<MedievalStockpile>().Stock.Capacity(r), Needed(r)) : 0;
		void Stop(string status) { Active = false; Loading = false; attempts = 0; Status = status; }
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString is "Move" or "Stop" or "MedievalReloadCart") { Stop("Convoi arrete. Cargaison conservee."); return; }
			if (order.OrderString == "MedievalStopConvoy") { self.CancelActivity(); Stop("Convoi arrete. Cargaison conservee."); return; }
			if (order.OrderString != "MedievalSupplySite" || order.Target.Type != TargetType.Actor) return;
			var target = order.Target.Actor;
			if (!target.IsInWorld || target.IsDead || target.Owner != self.Owner || target.TraitOrDefault<MedievalBuilding>() is not { Complete: false, Demolishing: false }) return;
			market = MedievalMarket.Markets(self.Owner)
				.OrderBy(m => MedievalSettlement.Warehouses(self.Owner).Any(d => MedievalLogistics.Local(d, m, 16)
					&& (d.Trait<MedievalStockpile>().Stock.Available(0) > 0 || d.Trait<MedievalStockpile>().Stock.Available(3) > 0)) ? 0 : 1)
				.ThenBy(m => (m.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(m => m.ActorID).FirstOrDefault();
			if (market == null) { Status = "Un marche termine est necessaire au depart."; return; }
			self.TraitOrDefault<MedievalManualTransfer>()?.ResumeAutomatic(self);
			site = target; Active = true; Loading = true; cadence = 0; attempts = 0; Trips = 0;
			self.CancelActivity(); self.Trait<MedievalSupplyCart>().PrepareConvoy();
			Status = "Retour au marche : chargement du convoi.";
		}
		void ITick.Tick(Actor self)
		{
			if (!Active || --cadence > 0) return;
			cadence = 25;
			if (site == null || !site.IsInWorld || site.IsDead || site.Owner != self.Owner)
			{ self.CancelActivity(); Stop("Chantier perdu. Cargaison conservee."); return; }
			if (MedievalDemolition.Pending(site)) { self.CancelActivity(); Stop("Chantier a demonter. Cargaison conservee."); return; }
			if (site.Trait<MedievalBuilding>().Complete) { self.CancelActivity(); Stop("Chantier termine. Charrette disponible sur place."); return; }
			if (!MedievalLogistics.Market(market, self.Owner)) { self.CancelActivity(); Stop("Marche de depart indisponible. Cargaison conservee."); return; }
			if (!self.IsIdle) return;
			var stock = self.Trait<MedievalStockpile>().Stock;
			if (Loading)
			{
				if (!MedievalLogistics.Approach(self, market, ref attempts)) { Status = "Rejoint le marche de depart."; return; }
				if (Needed(0) + Needed(3) + Needed(4) == 0) { Loading = false; attempts = 0; return; }
				if (stock.Available(0) < Desired(self, 0) || stock.Available(3) < Desired(self, 3) || stock.Available(4) < Desired(self, 4) || stock.Available(1) < 10)
				{
					Status = $"Chargement : bois {stock.Amount(0)}/{Desired(self, 0)}, pierre {stock.Amount(3)}/{Desired(self, 3)}, or {stock.Amount(4)}/{Desired(self, 4)}, repas {stock.Amount(1)}/10";
					return;
				}
				Loading = false; attempts = 0; Trips++;
				Status = "Convoi charge : rejoint le chantier.";
			}
			else
			{
				if (!MedievalLogistics.Approach(self, site, ref attempts)) { Status = "Transporte les materiaux vers le chantier."; return; }
				Status = "Les batisseurs prennent les materiaux dans la charrette.";
				if (Needed(0) + Needed(3) + Needed(4) == 0) return;
				if (stock.Reserved(0) + stock.Reserved(3) + stock.Reserved(4) > 0) return;
				if ((Needed(0) == 0 || stock.Available(0) == 0) && (Needed(3) == 0 || stock.Available(3) == 0) && (Needed(4) == 0 || stock.Available(4) == 0))
				{ Loading = true; attempts = 0; Status = "Retour chercher la prochaine charge."; }
			}
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { self.CancelActivity(); Stop("Nouvel ordre de convoi requis."); }
	}

	public sealed class MedievalLogisticsTargetGenerator : OrderGenerator
	{
		readonly Actor origin;
		readonly string command;
		readonly WorldRenderer renderer;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalLogisticsTargetGenerator(World world, WorldRenderer renderer, Actor origin, string command) : base(world)
		{ this.origin = origin; this.command = command; this.renderer = renderer; }
		bool ValidOrigin => origin.IsInWorld && !origin.IsDead && origin.Owner == origin.World.LocalPlayer;
		Actor At(World w, CPos cell) => command == "MedievalCollectCargo"
			? w.ActorsHavingTrait<MedievalDroppedCargo>().Where(a => a.Location == cell && MedievalManualTransfer.CanCollect(origin, a)).OrderBy(a => a.ActorID).FirstOrDefault()
			: w.ActorMap.GetActorsAt(cell).Where(a => a.IsInWorld && !a.IsDead && a.Owner == origin.Owner && a != origin)
			.FirstOrDefault(a => command == "MedievalSupplySite" ? a.TraitOrDefault<MedievalBuilding>()?.Complete == false
				: MedievalLogistics.Market(a, origin.Owner) && (command != "MedievalAssignTrader" || a.Trait<MedievalTradeMarket>().Linked(a)));
		public string PreviewText
		{
			get
			{
				if (command == "MedievalCollectCargo") return "Cliquez une cargaison abandonnée visible. Chargement sur place ; le surplus reste au sol. Clic droit : annuler.";
				if (ValidOrigin && command != "MedievalSupplySite" && At(origin.World, renderer.Viewport.ViewToWorld(Viewport.LastMousePos)) is Actor target)
					return command == "MedievalAssignTrader" ? "Affecter la charrette a " + MedievalLogistics.Name(target) + ". Cliquez pour confirmer."
						: command == "MedievalLinkStall" ? MedievalLogistics.Name(target) + " -> " + MedievalLogistics.Name(origin) + ". Cliquez pour relier."
						: MedievalLogistics.Name(origin) + " <-> " + MedievalLogistics.Name(target) + ". Cliquez pour relier.";
				return command == "MedievalSupplySite" ? "Cliquez le chantier a approvisionner. Clic droit : annuler."
					: command == "MedievalLinkStall" ? "Cliquez le grand marche qui approvisionnera cet etal. Clic droit : annuler."
					: command == "MedievalAssignTrader" ? "Cliquez un marche relie pour affecter cette charrette. Clic droit : annuler."
					: "Cliquez votre second marche termine pour creer la route. Clic droit : annuler.";
			}
		}
		protected override void Tick(World world) { if (!ValidOrigin) world.CancelInputMode(); }
		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (world.Paused || !ValidOrigin || At(world, cell) is not Actor target) yield break;
			yield return new Order(command, origin, Target.FromActor(target), false);
			world.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (!ValidOrigin) yield break;
			var cell = renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
			if (At(world, cell) is not Actor target) yield break;
			foreach (var tile in target.Info.TraitInfo<BuildingInfo>().Tiles(target.Location))
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(world, tile, Color.LimeGreen)) yield return line;
			foreach (var line in MedievalTradeMarket.Connection(origin, target)) yield return line;
		}
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi) => ValidOrigin && At(world, cell) != null ? "ability" : "move-blocked";
	}
}
