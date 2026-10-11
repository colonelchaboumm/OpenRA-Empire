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
	public sealed class MedievalSabotageGenerator : OrderGenerator
	{
		readonly Actor saboteur;
		readonly WorldRenderer renderer;
		readonly int action;
		protected override MouseActionType ActionType => MouseActionType.PlaceBuilding;
		public MedievalSabotageGenerator(Actor saboteur, WorldRenderer renderer, int action) : base(saboteur.World)
		{ this.saboteur = saboteur; this.renderer = renderer; this.action = action; }
		bool Valid(World w) => MedievalStockpile.Active(saboteur) && saboteur.Owner == w.LocalPlayer;
		Actor At(World w, CPos cell)
		{
			if (!Valid(w) || !w.LocalPlayer.Shroud.IsVisible(cell)) return null;
			var actors = action == 0 ? w.ActorsHavingTrait<MedievalBurnable>() : w.ActorsHavingTrait<MedievalAnimal>();
			return actors.Where(a => action == 2 ? MedievalHerding.CanHandle(saboteur, a) : MedievalSaboteur.CanTarget(saboteur, a, action == 1))
				.Where(a => action == 0 ? MedievalSaboteur.YardAt(a, cell) || a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location).Contains(cell)
					: a.Location == cell || (a.Location - cell).LengthSquared <= 1)
				.OrderBy(a => (a.CenterPosition - w.Map.CenterOfCell(cell)).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
		}
		protected override void Tick(World w) { if (!Valid(w)) w.CancelInputMode(); }
		protected override IEnumerable<Order> OrderInner(World w, CPos cell, int2 pixel, MouseInput mi)
		{
			if (At(w, cell) is not { } target) yield break;
			yield return new Order(action == 2 ? "MedievalGatherAnimal" : action == 1 ? "MedievalSabotageKill" : "MedievalSabotageFire", saboteur, Target.FromActor(target), false)
			{ ExtraLocation = cell, ExtraData = action == 0 && MedievalSaboteur.YardAt(target, cell) ? 1u : 0u };
			w.CancelInputMode();
		}
		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World w) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World w)
		{
			var cell = renderer.Viewport.ViewToWorld(Viewport.LastMousePos);
			if (At(w, cell) == null) yield break;
			foreach (var line in MedievalPlaceOrderGenerator.CellOutline(w, cell, Color.OrangeRed)) yield return line;
		}
		protected override string GetCursor(World w, CPos cell, int2 pixel, MouseInput mi) => At(w, cell) == null ? "move-blocked" : action == 2 ? "enter" : "attack";
	}
	public sealed class MedievalSaboteurLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public MedievalSaboteurLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			if (world.LocalPlayer == null) return;
			Actor Selected() => world.Selection.Actors.Count() == 1 ? world.Selection.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalSaboteur>() != null) : null;
			widget.Get("MD_SABOTEUR_PANEL").IsVisible = () => Selected() != null;
			widget.Get<LabelWidget>("MD_SABOTEUR_TITLE").GetText = () => "SABOTEUR - INFILTRATION";
			widget.Get<LabelWidget>("MD_SABOTEUR_HINT").GetText = () => "Cache a distance ; repere par la milice a 4 cases.\nVisible pendant l'action et la conduite d'un animal.";
			var names = new[] { "Incendier un batiment / une reserve (10 s)", "Abattre un animal adverse (8 s)", "Voler et ramener un animal (capture : 8 s)" };
			for (var i = 0; i < 3; i++)
			{
				var action = i; var button = widget.Get<ButtonWidget>("MD_SABOTEUR_ACTION_" + i);
				button.GetText = () => names[action]; button.IsDisabled = () => world.Paused || Selected() == null;
				button.OnClick = () => { if (Selected() is { } a) world.OrderGenerator = new MedievalSabotageGenerator(a, worldRenderer, action); };
			}
			var stop = widget.Get<ButtonWidget>("MD_SABOTEUR_STOP"); stop.GetText = () => "Interrompre et rester sur place";
			stop.IsDisabled = () => world.Paused || Selected() == null;
			stop.OnClick = () => { if (Selected() is { } a) { world.CancelInputMode(); world.IssueOrder(new Order("Stop", a, false)); } };
			widget.Get<LabelWidget>("MD_SABOTEUR_STATUS").GetText = () => Selected() is { } a ? a.Trait<MedievalHerding>().Busy ? a.Trait<MedievalHerding>().Status : a.Trait<MedievalSaboteur>().Status : "";
			var recruit = widget.Get<ButtonWidget>("MD_BARRACKS_SABOTEUR");
			Actor Barracks() => world.Selection.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && MedievalStockpile.Active(a) && a.TraitOrDefault<MedievalMilitaryWorkshop>()?.Info.Forge == false);
			recruit.GetText = () => "Saboteur | 35 nourr. + 10 viande";
			recruit.GetTooltipText = () => "Saboteur : infiltration et incendies";
			recruit.GetTooltipDesc = () => "35 nourriture, 10 viande, une epee. 1 logement.\nLent, fragile, cache a distance. La milice le repere a 4 cases.\nIncendie : 10 s ; abattage / capture : 8 s.\nLes actions et les animaux conduits le rendent visible.";
			recruit.IsDisabled = () => world.Paused || Barracks() is not { } a || !a.Trait<MedievalMilitaryWorkshop>().CanRecruit(a);
			recruit.OnClick = () => { if (!recruit.IsDisabled() && Barracks() is { } a) world.IssueOrder(new Order("MedievalTrainSoldier", a, false) { TargetString = "md.saboteur" }); };
		}
	}
}
