using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed partial class MedievalInfluence : IRenderAboveWorld, IRenderAnnotations, INotifyViewportZoomExtentsChanged
	{
		float minimumZoom = .25f;
		static readonly CVec[] Neighbors = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];
		public int PaintedCells { get; private set; }
		public int BorderSegments { get; private set; }
		public bool Visible(WorldRenderer wr) => wr.World.Type == WorldType.Regular && wr.Viewport.Zoom <= minimumZoom * 1.06f;
		public static bool StrategicView(WorldRenderer wr) => wr.World.WorldActor.TraitOrDefault<MedievalInfluence>()?.Visible(wr) == true;
		void INotifyViewportZoomExtentsChanged.ViewportZoomExtentsChanged(float minZoom, float maxZoom) { minimumZoom = minZoom; }
		public static bool CanShow(bool explored, bool visible, bool friendly) => explored && (visible || friendly);
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Visible(wr)) yield break;
			foreach (var nation in Field.Claims.GroupBy(c => c.Value.Owner))
			{
				var cell = nation.MaxBy(c => c.Value.Power).Key;
				var friendly = self.World.RenderPlayer == null || self.World.RenderPlayer.RelationshipWith(nation.Key) == PlayerRelationship.Ally;
				if (!CanShow(!self.World.ShroudObscures(cell), !self.World.FogObscures(cell), friendly)) continue;
				var position = self.World.Map.CenterOfCell(cell);
				var pixel = wr.ScreenPxPosition(position);
				var tl = wr.Viewport.TopLeft; var br = wr.Viewport.BottomRight;
				if (pixel.X < tl.X || pixel.X > br.X || pixel.Y < tl.Y || pixel.Y > br.Y) continue;
				var heraldry = MedievalHeraldry.For(nation.Key);
				yield return new LocalityLabelRenderable(position, 20, heraldry.CountryName, heraldry.Color);
			}
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			PaintedCells = BorderSegments = 0;
			if (!Visible(wr)) return;
			var world = self.World; var map = world.Map;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var corners = map.Grid.Ramps[0].Corners;
			Span<Vector3> points = stackalloc Vector3[4];
			var tl = wr.Viewport.TopLeft; var br = wr.Viewport.BottomRight;
			var width = 1.8f / wr.Viewport.Zoom;
			foreach (var pair in Field.Claims)
			{
				var cell = pair.Key; var owner = pair.Value.Owner;
				var friendly = world.RenderPlayer == null || world.RenderPlayer.RelationshipWith(owner) == PlayerRelationship.Ally;
				if (!CanShow(!world.ShroudObscures(cell), !world.FogObscures(cell), friendly)) continue;
				var center = map.CenterOfCell(cell); var pixel = wr.ScreenPxPosition(center);
				if (pixel.X < tl.X - 64 || pixel.X > br.X + 64 || pixel.Y < tl.Y - 32 || pixel.Y > br.Y + 32) continue;
				var color = MedievalHeraldry.For(owner).Color;
				for (var i = 0; i < 4; i++) points[i] = wr.Screen3DPosition(center + corners[i]);
				cr.FillRect(points[0], points[1], points[2], points[3], Color.FromArgb(25, color));
				PaintedCells++;
				for (var i = 0; i < 4; i++)
				{
					var other = cell + Neighbors[i];
					if (Field.At(other).Owner == owner || map.Contains(other) && world.ShroudObscures(other)) continue;
					// Insets keep both national colors readable along contested borders.
					var a = Vector3.Lerp(points[i], wr.Screen3DPosition(center), .10f);
					var b = Vector3.Lerp(points[(i + 1) % 4], wr.Screen3DPosition(center), .10f);
					cr.DrawLine(a, b, width + 3 / wr.Viewport.Zoom, Color.FromArgb(105, 10, 17, 24));
					cr.DrawLine(a, b, width, Color.FromArgb(225, color));
					BorderSegments++;
				}
			}
		}
	}
}
