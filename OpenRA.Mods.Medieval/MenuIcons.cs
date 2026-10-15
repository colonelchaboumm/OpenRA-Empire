using System;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalMenuIcons
	{
		public static Sprite Good(World world, int good) => world.Map.Sequences.GetSequence(
			good < 5 ? "md.resources" : good < 8 ? "md.equipment" : "md.campgoods",
			good < 5 ? StockBadgeRenderable.ResourceSequences[good] : good < 8 ? MedievalEquipment.Sequences[good - 5] : MedievalCampStore.Sequences[good - 8]).GetSprite(0);

		public static Sprite Entry(World world, string type)
		{
			if (type == "md.campfire") return world.Map.Sequences.GetSequence("md.fire-icons", "idle").GetSprite(0);
			for (var c = 0; c < MedievalConstructionMenuLogic.Categories.Length; c++)
			{
				var i = Array.FindIndex(MedievalConstructionMenuLogic.Categories[c].Items, e => e.Type == type);
				if (i >= 0) return world.Map.Sequences.GetSequence("md.construction-icons", "idle").GetSprite(
					c == 6 ? 3 * MedievalConstructionMenuLogic.IconSlotsPerCategory + 3 + i : c * MedievalConstructionMenuLogic.IconSlotsPerCategory + i);
			}
			return null;
		}

		public static void Add(Widget parent, WorldRenderer renderer, string id, int x, int y, int size, Func<Sprite> sprite, Func<string> palette)
		{
			var icon = new SpriteWidget(renderer) { Id = id, Bounds = new WidgetBounds(x, y, size, size), GetSprite = sprite, GetPalette = palette };
			icon.GetScale = () => sprite() is { } s ? Math.Min((float)size / s.Bounds.Width, (float)size / s.Bounds.Height) : 1;
			parent.AddChild(icon);
		}
	}
}
