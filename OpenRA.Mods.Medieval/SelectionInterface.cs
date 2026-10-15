using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalSelectionLogic : ChromeLogic
	{
		readonly World world;
		readonly Widget panel, status;
		readonly ButtonWidget[] buttons = new ButtonWidget[6];
		readonly LabelWidget[] statusLabels;
		readonly Dictionary<Sprite, Sprite> portraits = [];
		Actor[][] groups = [];
		int selectionHash = -1, page, slots = 6, total;
		public static Actor[][] Group(IEnumerable<Actor> actors) => actors
			.Where(a => a.IsInWorld && !a.IsDead && a.Info.HasTraitInfo<MobileInfo>())
			.GroupBy(a => (a.Info.Name, a.Owner.InternalName)).OrderBy(g => g.Key.Name, StringComparer.Ordinal)
			.ThenBy(g => g.Key.InternalName, StringComparer.Ordinal).Select(g => g.OrderBy(a => a.ActorID).ToArray()).ToArray();
		// Simulation can remove a selected actor between the UI tick and drawing.
		Actor[] At(int index) => page * slots + index < groups.Length
			? groups[page * slots + index].Where(a => a.IsInWorld && !a.IsDead).ToArray() : [];
		static string Name(Actor actor) => actor.Info.TraitInfoOrDefault<TooltipInfo>()?.Name is { } key ? FluentProvider.GetMessage(key) : actor.Info.Name;
		Sprite Portrait(Actor actor)
		{
			var image = actor.TraitOrDefault<MedievalAnimal>()?.VisualImage
				?? (actor.Info.TraitInfoOrDefault<MedievalHeraldicImageInfo>() is { } heraldry
					? heraldry.ImagePrefix + MedievalHeraldry.For(actor.Owner).Emblem : actor.TraitOrDefault<RenderSprites>()?.GetImage(actor));
			if (image == null) return null;
			var sequences = world.Map.Sequences;
			var sequence = sequences.HasSequence(image, "stand") ? "stand" : sequences.HasSequence(image, "idle") ? "idle" : sequences.Sequences(image).First();
			var sprite = sequences.GetSequence(image, sequence).GetSprite(0);
			if (portraits.TryGetValue(sprite, out var portrait)) return portrait;
			// World sprites include ground anchors and generous transparent margins.
			// Crop once per distinct picture, retaining the original texture and palette.
			var bounds = sprite.Bounds;
			if (sprite.Channel == TextureChannel.RGBA && bounds.Width > 0 && bounds.Height > 0)
			{
				var buffered = sprite.Sheet.Buffered; var pixels = sprite.Sheet.GetData();
				var left = bounds.Right; var top = bounds.Bottom; var right = bounds.Left; var bottom = bounds.Top;
				for (var y = bounds.Top; y < bounds.Bottom; y++)
					for (var x = bounds.Left; x < bounds.Right; x++)
						if (pixels[(y * sprite.Sheet.Size.Width + x) * 4 + 3] >= 16)
						{
							left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
						}
				if (right > left && bottom > top) bounds = Rectangle.FromLTRB(left, top, right, bottom);
				if (!buffered) sprite.Sheet.ReleaseBuffer();
			}
			portrait = new Sprite(sprite.Sheet, bounds, 0, Vector3.Zero, sprite.Channel, sprite.BlendMode);
			portraits.Add(sprite, portrait); return portrait;
		}
		void Select(int index, bool remove)
		{
			var actors = At(index).Where(a => a.IsInWorld && !a.IsDead && world.Selection.Contains(a)).ToArray();
			if (actors.Length == 0) return;
			world.Selection.Combine(world, remove ? world.Selection.Actors.Except(actors).ToArray() : actors, false, false);
			Refresh();
		}
		[ObjectCreator.UseCtor]
		public MedievalSelectionLogic(Widget widget, World world)
		{
			this.world = world;
			panel = widget.Get("MD_UNIT_SELECTION"); status = widget.Get("MD_STATUS_PANEL");
			panel.IsVisible = () => total > 0;
			widget.Get<LabelWidget>("MD_UNIT_TOTAL").GetText = () => $"Sélection\n{total} unité{(total == 1 ? "" : "s")}";
			var template = panel.Get<ButtonWidget>("MD_UNIT_TEMPLATE"); panel.RemoveChild(template);
			for (var i = 0; i < buttons.Length; i++)
			{
				var index = i; var button = (ButtonWidget)template.Clone(); button.Id = "MD_UNIT_" + i;
				buttons[i] = button; panel.AddChild(button); button.Bounds.X += i * 46;
				button.IsVisible = () => index < slots && At(index).Length > 0;
				button.GetText = () => "";
				button.GetTooltipText = () => At(index) is { Length: > 0 } actors ? $"{Name(actors[0])} × {actors.Length}" : "";
				button.GetTooltipDesc = () => "Clic : garder ce type dans la sélection.\nMaj + clic : retirer ce type de la sélection.";
				button.OnClick = () => Select(index, false);
				button.OnMouseUp = input => Select(index, input.Modifiers.HasModifier(Modifiers.Shift));
				button.Get<LabelWidget>("COUNT").GetText = () => At(index).Length.ToString();
				var portrait = button.Get<SpriteWidget>("ICON");
				portrait.GetSprite = () => At(index) is { Length: > 0 } actors ? Portrait(actors[0]) : null;
				portrait.GetScale = () => portrait.GetSprite() is { } sprite ? Math.Min(2f, Math.Min(38f / sprite.Size.X, 34f / sprite.Size.Y)) : 1;
				portrait.GetPalette = () => At(index) is { Length: > 0 } actors && actors[0].TraitOrDefault<RenderSprites>() is { } render
					? render.Info.Palette ?? render.Info.PlayerPalette + actors[0].Owner.InternalName : "chrome";
			}
			var previous = panel.Get<ButtonWidget>("MD_UNIT_PREV"); var next = panel.Get<ButtonWidget>("MD_UNIT_NEXT");
			previous.GetText = () => "‹"; next.GetText = () => "›";
			previous.IsDisabled = () => page == 0; next.IsDisabled = () => (page + 1) * slots >= groups.Length;
			previous.OnClick = () => { if (!previous.IsDisabled()) page--; };
			next.OnClick = () => { if (!next.IsDisabled()) page++; };
			panel.Get<LabelWidget>("MD_UNIT_PAGE").GetText = () => $"{page + 1}/{Math.Max(1, (groups.Length + slots - 1) / slots)}";
			statusLabels = [widget.Get<LabelWidget>("MD_MESSAGE"), widget.Get<LabelWidget>("MD_SELECTION")];
			foreach (var label in statusLabels)
			{
				var text = label.GetText;
				label.GetText = () => WidgetUtils.TruncateText(text(), Math.Max(1, label.Bounds.Width), Game.Renderer.Fonts[label.Font]);
				((LabelWithTooltipWidget)label).GetTooltipText = text;
			}
			Refresh();
		}
		void Refresh()
		{
			if (selectionHash != world.Selection.Hash)
			{
				selectionHash = world.Selection.Hash; groups = Group(world.Selection.Actors); total = groups.Sum(g => g.Length); page = 0;
			}
			var width = Math.Max(230, Game.Renderer.Resolution.Width - 462);
			slots = Math.Clamp((width / 2 - 140) / 46, 1, buttons.Length);
			page = Math.Clamp(page, 0, Math.Max(0, (groups.Length - 1) / slots));
			panel.Bounds.Width = 140 + slots * 46; panel.Bounds.Y = Game.Renderer.Resolution.Height - 100;
			panel.Get("MD_UNIT_PREV").Bounds.X = panel.Bounds.Width - 64;
			panel.Get("MD_UNIT_PAGE").Bounds.X = panel.Bounds.Width - 43;
			panel.Get("MD_UNIT_NEXT").Bounds.X = panel.Bounds.Width - 23;
			var occupied = total > 0 ? panel.Bounds.Width + 6 : 0;
			status.Bounds.X = 8 + occupied; status.Bounds.Width = width - occupied;
			foreach (var label in statusLabels) label.Bounds.Width = Math.Max(1, status.Bounds.Width - 14);
		}
		public override void Tick() => Refresh();
	}
}
