using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalAnimalOrdersInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalAnimalOrders();
	}
	public sealed class MedievalAnimalOrders : IResolveOrder
	{
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.Target.Type != TargetType.Actor || order.Target.Actor.TraitOrDefault<MedievalAnimal>() is not { } animal) return;
			var target = order.Target.Actor;
			if (!MedievalStockpile.Active(target) || !target.CanBeViewedByPlayer(self.Owner)) return;
			if (order.OrderString == "MedievalTransferAnimal" && order.ExtraActors is { Length: 1 })
				animal.Request(target, order.ExtraActors[0], self.Owner);
			else if (order.OrderString == "MedievalCancelAnimalTransfer") animal.CancelTransfer(target, self.Owner);
		}
	}
	public sealed class MedievalAnimalTransferGenerator : OrderGenerator
	{
		readonly Actor animal;
		readonly WorldRenderer renderer;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalAnimalTransferGenerator(World world, WorldRenderer renderer, Actor animal) : base(world) { this.animal = animal; this.renderer = renderer; }
		bool Valid(World w) => MedievalStockpile.Active(animal) && (animal.Owner == w.LocalPlayer || animal.Owner.NonCombatant) && animal.CanBeViewedByPlayer(w.LocalPlayer);
		Actor At(World w, CPos cell) => w.ActorsHavingTrait<MedievalLivestock>().FirstOrDefault(a =>
			a.Trait<MedievalLivestock>().CanReceive(animal, w.LocalPlayer) && a.Trait<MedievalAgriculturalArea>().Farm(a) != null
			&& new AgriculturalZone(a.Location, a.Trait<MedievalAgriculturalArea>().Size).Cells().Contains(cell));
		protected override void Tick(World w) { if (!Valid(w)) w.CancelInputMode(); }
		protected override IEnumerable<Order> OrderInner(World w, CPos cell, int2 pixel, MouseInput mi)
		{
			if (!Valid(w) || At(w, cell) is not { } pen) yield break;
			yield return new Order("MedievalTransferAnimal", w.LocalPlayer.PlayerActor, Target.FromActor(animal), false) { ExtraActors = [pen] };
			w.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World w)
		{
			if (!Valid(w) || At(w, renderer.Viewport.ViewToWorld(Viewport.LastMousePos)) is not { } pen) yield break;
			foreach (var cell in new AgriculturalZone(pen.Location, pen.Trait<MedievalAgriculturalArea>().Size).Cells())
				foreach (var line in MedievalPlaceOrderGenerator.CellOutline(w, cell, Color.LimeGreen)) yield return line;
		}
		protected override string GetCursor(World w, CPos cell, int2 pixel, MouseInput mi) => Valid(w) && At(w, cell) != null ? "enter" : "move-blocked";
	}
	public sealed class MedievalHerdLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalHerdLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			Actor Selected() => world.Selection.Actors.Count() == 1 ? world.Selection.Actors.FirstOrDefault(a => MedievalStockpile.Active(a)
				&& !world.FogObscures(a) && (a.TraitOrDefault<MedievalAnimal>() != null || a.Owner == world.LocalPlayer && a.TraitOrDefault<MedievalLivestock>() != null)) : null;
			widget.Get("MD_HERD_PANEL").IsVisible = () => world.LocalPlayer != null && Selected() != null;
			widget.Get<LabelWidget>("MD_HERD_TITLE").GetText = () => Selected()?.TraitOrDefault<MedievalLivestock>() != null ? "ELEVAGE - ENCLOS" : "BETAIL - ANIMAL";
			widget.Get<LabelWidget>("MD_HERD_STATUS").GetText = () => Selected() is not { } a ? "" : a.TraitOrDefault<MedievalLivestock>() is { } pen ? pen.Status
				: a.Trait<MedievalAnimal>().Description + "\n" + (a.Owner.NonCombatant ? "Animal sauvage : capture par le berger ou l'eleveur." : "Appartient a " + a.Owner.PlayerName);
			var transfer = widget.Get<ButtonWidget>("MD_HERD_TRANSFER");
			transfer.GetText = () => Selected()?.Owner.NonCombatant == true ? "Capturer vers un enclos" : "Transferer vers un autre enclos";
			transfer.IsVisible = () => Selected()?.TraitOrDefault<MedievalAnimal>() != null;
			transfer.IsDisabled = () => world.Paused || Selected() is not { } a || !(a.Owner == world.LocalPlayer || a.Owner.NonCombatant)
				|| MedievalHerding.NearestPen(a, world.LocalPlayer, a.CenterPosition) == null;
			transfer.OnClick = () => { if (!transfer.IsDisabled()) world.OrderGenerator = new MedievalAnimalTransferGenerator(world, worldRenderer, Selected()); };
			var cancel = widget.Get<ButtonWidget>("MD_HERD_CANCEL"); cancel.GetText = () => "Annuler le transfert";
			cancel.IsVisible = transfer.IsVisible;
			cancel.IsDisabled = () => world.Paused || Selected()?.TraitOrDefault<MedievalAnimal>()?.Claimed(world.LocalPlayer) != true;
			cancel.OnClick = () => { if (!cancel.IsDisabled()) world.IssueOrder(new Order("MedievalCancelAnimalTransfer", world.LocalPlayer.PlayerActor, Target.FromActor(Selected()), false)); };
			widget.Get<LabelWidget>("MD_HERD_HINT").GetText = () => world.OrderGenerator is MedievalAnimalTransferGenerator ? "Cliquez votre enclos de la meme espece avec une place libre.\nClic droit : annuler le choix."
				: Selected()?.TraitOrDefault<MedievalLivestock>() is { } pen ? "Deux adultes : reproduction, jusqu'a la capacite de l'enclos.\nLes arrivees reservent leur place. Les jeunes doivent grandir.\n" + (pen.Sheep ? "La tonte conserve les moutons vivants." : "Viande : seuls les adultes en surplus sont abattus.")
				: "Transfert : un berger/eleveur disponible viendra le chercher.\nVol : selectionnez votre soldat, clic droit sur un animal adverse.\nEnclos plein ? Agrandissez votre elevage.";
		}
	}
}
