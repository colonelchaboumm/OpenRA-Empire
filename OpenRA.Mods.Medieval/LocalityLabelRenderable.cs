using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	public sealed class LocalityLabelRenderable : IRenderable, IFinalizedRenderable
	{
		readonly int height;
		readonly string text;
		public WPos Pos { get; }
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public LocalityLabelRenderable(WPos pos, int height, string text) { Pos = pos; this.height = height; this.text = text; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new LocalityLabelRenderable(Pos + offset, height, text);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) => Rectangle.Empty;
		public void Render(WorldRenderer wr)
		{
			var font = Game.Renderer.Fonts["TinyBold"]; var size = font.Measure(text);
			var pixel = wr.Viewport.WorldToViewPx(wr.ScreenPxPosition(Pos)) + new int2(-size.X / 2, -height);
			var point = new Vector3(pixel.X, pixel.Y, 0); var cr = Game.Renderer.RgbaColorRenderer;
			cr.FillRect(point - new Vector3(5, 3, 0), point + new Vector3(size.X + 5, size.Y + 3, 0), Color.FromArgb(220, 29, 40, 37));
			cr.DrawRect(point - new Vector3(5, 3, 0), point + new Vector3(size.X + 5, size.Y + 3, 0), 1, Color.FromArgb(184, 163, 113));
			font.DrawTextWithContrast(text, new Vector2(pixel.X, pixel.Y), Color.FromArgb(245, 229, 188), Color.Black, 1);
		}
	}
}
