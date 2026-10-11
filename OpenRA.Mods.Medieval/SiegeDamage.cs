using System;
using System.Collections.Generic;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSiegeDamageInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeDamage();
	}
	public sealed class MedievalSiegeDamage : INotifyDamage, ITick, IRenderModifier
	{
		static readonly BitSet<DamageType> SiegeDamage = new("MedievalSiegeImpact");
		static readonly BitSet<DamageType> RamDamage = new("MedievalRamImpact");
		public int ShakeTicks { get; private set; }
		public int Impacts { get; private set; }
		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (e.Damage.Value <= 0 || !e.Damage.DamageTypes.Overlaps(SiegeDamage) && !e.Damage.DamageTypes.Overlaps(RamDamage)) return;
			ShakeTicks = 18; Impacts++;
			// Ram dust is emitted at the wall contact by the strike itself.
			if (e.Damage.DamageTypes.Overlaps(RamDamage)) return;
			var size = self.Info.TraitInfo<BuildingInfo>().Dimensions;
			var height = Math.Clamp(22 + (size.X + size.Y) * 4, 26, 60);
			var effect = new SiegeImpactEffect(self.CenterPosition, self.World.LocalRandom.Next(), true, height);
			self.World.AddFrameEndTask(w => w.Add(effect));
		}
		void ITick.Tick(Actor self) { if (ShakeTicks > 0) ShakeTicks--; }
		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> renderables)
		{
			var shift = ShakeTicks == 0 ? 0 : (int)Math.Round(Math.Sin((18 - ShakeTicks) * 2.1) * 2.4 * ShakeTicks / 18);
			var offset = new WVec(shift * self.World.Map.Grid.TileScale / self.World.Map.Rules.TerrainInfo.TileSize.Width, 0, 0);
			foreach (var item in renderables)
			{
				var result = shift == 0 ? item : item.OffsetBy(offset);
				if (ShakeTicks > 14 && result is IModifyableRenderable sprite)
					result = sprite.WithTint(Vector3.Lerp(sprite.Tint, new Vector3(1.28f, 1.17f, .93f), (ShakeTicks - 14) / 8f), sprite.TintModifiers);
				yield return result;
			}
		}
		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{ foreach (var b in bounds) yield return Rectangle.FromLTRB(b.Left - 3, b.Top, b.Right + 3, b.Bottom); }
	}
}
