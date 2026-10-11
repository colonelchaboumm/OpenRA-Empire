using System;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	sealed class StockHandlingRenderable : IRenderable, IFinalizedRenderable
	{
		readonly Sprite goods;
		readonly WPos target;
		readonly float progress;
		readonly bool pickup, committed;
		readonly int good;
		public WPos Pos { get; }
		public int ZOffset => 1025;
		public bool IsDecoration => true;
		public StockHandlingRenderable(Actor actor, MedievalWorker worker)
		{
			Pos = actor.CenterPosition; target = worker.HandlingPosition; progress = worker.HandlingProgress;
			pickup = worker.HandlingPickup; committed = worker.HandlingCommitted; good = worker.HandlingGood;
			goods = actor.World.Map.Sequences.GetSequence(good < 5 ? "md.resources" : good < 8 ? "md.equipment" : "md.campgoods",
				good < 5 ? StockBadgeRenderable.ResourceSequences[good] : good < 8 ? MedievalEquipment.Sequences[good - 5] : MedievalCampStore.Sequences[good - 8]).GetSprite(0);
		}
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => this;
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) { var p = wr.ScreenPxPosition(Pos); return new(p.X - 35, p.Y - 50, 70, 75); }
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var at = wr.Screen3DPxPosition(Pos); var to = wr.Screen3DPxPosition(target) - at;
			to.Z = 0; var length = to.Length(); var direction = length > .01f ? to / length : new Vector3(0, 1, 0);
			var contact = at + direction * Math.Clamp(length * .42f, 8, 16) + new Vector3(0, -2, 0);
			var hand = at + direction * 3 + new Vector3(0, -11 + MathF.Sin(progress * MathF.PI) * 4, 0);
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			if (pickup ? committed : !committed)
			{
				var t = Math.Clamp(pickup ? progress * 2 - 1 : progress * 2, 0, 1); t = t * t * (3 - 2 * t);
				var position = Vector3.Lerp(pickup ? contact : hand, pickup ? hand : contact, t) - new Vector3(0, MathF.Sin(t * MathF.PI) * 2.5f, 0);
				var ground = position + new Vector3(0, 9, 0);
				cr.FillEllipse(ground - new Vector3(4, 1.6f, 0), ground + new Vector3(4, 1.6f, 0), Color.FromArgb(40, 30, 26, 18));
				Game.Renderer.WorldRgbaSpriteRenderer.DrawSprite(goods, position - goods.Size * .275f, .55f);
			}
			if (!committed || wr.Viewport.Zoom < .8f) return;
			var age = Math.Clamp(progress * 2 - 1, 0, 1); var opacity = 1 - age;
			if (!pickup && good is 0 or 3 or 4)
				for (var i = 0; i < 4; i++)
				{
					var p = contact + new Vector3((i - 1.5f) * (2 + age * 5), age * (i % 2 == 0 ? 3 : -2), 0);
					cr.FillEllipse(p - new Vector3(1 + age * 2, .7f, 0), p + new Vector3(1 + age * 2, .7f, 0), Color.FromArgb((int)(55 * opacity), 169, 153, 120));
				}
		}
	}
}
