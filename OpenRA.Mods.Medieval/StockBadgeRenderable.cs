using System;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	// Screen-size labels stay legible at every zoom; inventory remains the sole
	// source of truth. This class only reads values and never changes game state.
	public sealed class StockBadgeRenderable : IRenderable, IFinalizedRenderable
	{
		public static readonly string[] ResourceSequences = ["wood", "food", "meat", "stone", "gold"];
		static readonly Color[] ResourceColors = [Color.Tan, Color.Goldenrod, Color.IndianRed, Color.LightGray, Color.Gold];
		readonly int2 offset;
		readonly int[] resources;
		readonly int[] amounts;
		readonly int[] capacities;
		readonly Sprite[] icons;
		readonly int slotWidth;
		readonly SpriteFont font;
		public WPos Pos { get; }
		public int ZOffset => 0;
		public bool IsDecoration => true;

		public StockBadgeRenderable(World world, WPos pos, int2 offset, int[] resources, int[] amounts, int[] capacities, bool equipment = false, bool camp = false)
		{
			Pos = pos;
			this.offset = offset;
			this.resources = resources;
			this.amounts = amounts;
			this.capacities = capacities;
			font = Game.Renderer.Fonts["TinyBold"];
			icons = resources.Select(r => world.Map.Sequences.GetSequence(camp ? "md.campgoods" : equipment ? "md.equipment" : "md.resources", camp ? MedievalCampStore.Sequences[r] : equipment ? MedievalEquipment.Sequences[r] : ResourceSequences[r]).GetSprite(0)).ToArray();
			slotWidth = Math.Max(35, amounts.Max(n => font.Measure(n.ToString()).X) + 22);
		}

		public IRenderable WithZOffset(int newOffset) => this;
		public IRenderable OffsetBy(in WVec vec) => this;
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) => Rectangle.Empty;
		public void Render(WorldRenderer wr)
		{
			var origin = wr.Viewport.WorldToViewPx(wr.ScreenPxPosition(Pos)) + offset - new int2(resources.Length * slotWidth / 2, 10);
			var start = new Vector3(origin.X, origin.Y, 0);
			var end = start + new Vector3(resources.Length * slotWidth, 20, 0);
			var cr = Game.Renderer.RgbaColorRenderer;
			cr.FillRect(start, end, Color.FromArgb(220, 22, 30, 27));
			cr.DrawRect(start, end, 1, Color.FromArgb(180, 135, 127, 94));
			for (var i = 0; i < resources.Length; i++)
			{
				var cell = start + new Vector3(i * slotWidth, 0, 0);
				Game.Renderer.SpriteRenderer.DrawSprite(icons[i], null, cell + new Vector3(2, 1, 0), 1f);
				font.DrawTextWithContrast(amounts[i].ToString(), new Vector2(cell.X + 19, cell.Y + 3),
					amounts[i] == 0 ? Color.Gray : Color.FromArgb(238, 232, 211), Color.Black, 1);
				var fill = capacities[i] == 0 ? 0 : Math.Clamp((float)amounts[i] / capacities[i], 0, 1);
				if (fill > 0)
					cr.FillRect(cell + new Vector3(2, 17, 0), cell + new Vector3(2 + (slotWidth - 4) * fill, 19, 0), ResourceColors[resources[i]]);
			}
		}
	}
}
