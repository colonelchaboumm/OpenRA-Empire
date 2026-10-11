using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalConstructionSupplyInfo : TraitInfo, Requires<MedievalStockpileInfo>
	{
		public readonly WDist Radius = new(20480);
		public override object Create(ActorInitializer init) => new MedievalConstructionSupply(this);
	}
	public class MedievalConstructionSupply : IRenderAnnotations
	{
		public readonly MedievalConstructionSupplyInfo Info;
		public MedievalConstructionSupply(MedievalConstructionSupplyInfo info) { Info = info; }
		public static bool Within(WPos source, WPos site, WDist radius) =>
			(source - site).HorizontalLengthSquared <= (long)radius.Length * radius.Length;
		public static bool CanSupply(Actor source, Player owner, WPos site) => source.Owner == owner && MedievalStockpile.Active(source)
			&& source.TraitOrDefault<MedievalConstructionSupply>() is MedievalConstructionSupply supply
			&& (source.TraitOrDefault<MedievalSupplyCart>() == null || source.IsIdle)
			&& (source.TraitOrDefault<MedievalConstructionConvoy>() is not { Active: true } convoy || convoy.Supplies(site))
			&& Within(source.CenterPosition, site, supply.Info.Radius);
		public static IEnumerable<Actor> Sources(Player owner, WPos site) => owner.World.ActorsHavingTrait<MedievalConstructionSupply>().Where(a => CanSupply(a, owner, site))
			.OrderBy(a => (a.CenterPosition - site).HorizontalLengthSquared).ThenBy(a => a.ActorID);
		public static int Demand(Actor source, int resource, IEnumerable<Actor> candidates = null) => (candidates ?? source.World.ActorsHavingTrait<MedievalBuilding>()).Where(a => a.Owner == source.Owner && a.IsInWorld && !a.IsDead
			&& a.TraitOrDefault<MedievalBuilding>() is { Complete: false, Demolishing: false } && CanSupply(source, source.Owner, a.CenterPosition))
			.Sum(a => a.Trait<MedievalBuilding>().Materials.Free(resource));
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || self.World.FogObscures(self)) yield break;
			foreach (var segment in MedievalFoodProvider.Ring(self.CenterPosition, Info.Radius,
				MedievalStockpile.Active(self) ? Color.CornflowerBlue : Color.Gray)) yield return segment;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
