using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalDecorationInfo : TraitInfo
	{
		public readonly bool Ground = false;
		public override object Create(ActorInitializer init) => new MedievalDecoration(this);
	}
	public sealed class MedievalDecoration
	{
		public readonly MedievalDecorationInfo Info;
		public MedievalDecoration(MedievalDecorationInfo info) { Info = info; }
	}

	public class MedievalGardenFenceInfo : TraitInfo, Requires<MedievalDecorationInfo>, Requires<WithSpriteBodyInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalGardenFence();
	}
	public sealed class MedievalGardenFence : ITick
	{
		static readonly CVec[] Neighbors = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
		WithSpriteBody body;
		int ticks;
		public int Connections { get; private set; }
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self) || ++ticks % 8 != 1) return;
			if (body == null)
			{
				body = self.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body");
				body.DefaultAnimation.PlayFetchIndex("idle", () => Connections);
			}
			Connections = 0;
			for (var i = 0; i < Neighbors.Length; i++)
			{
				var neighbor = MedievalDecorations.At(self.World, self.Owner, self.Location + Neighbors[i]);
				if (neighbor?.TraitOrDefault<MedievalGardenFence>() != null
					|| neighbor?.Info.Name == (i < 2 ? "md.gardengate" : "md.gardengatey")
					|| neighbor?.Info.Name == (i < 2 ? "md.gildedgate" : "md.gildedgatey")) Connections |= 1 << i;
			}
		}
	}

	public readonly record struct DecorationPlot(string Type, CPos Cell);
	public static class MedievalDecorations
	{
		public const int MaxLength = 64;
		public static readonly string[] Types = ["md.well", "md.gardenfence", "md.gardenfencey", "md.gardengate", "md.gardengatey", "md.flowerbed", "md.banner", "md.stonepath",
			"md.plaza", "md.plazalarge", "md.statueknight", "md.statuelion", "md.plantedoak", "md.plantedcypress",
			"md.gildedknight", "md.gildedlion", "md.gildedplaza", "md.gildedplazalarge", "md.gildedfence", "md.gildedfencey", "md.gildedgate", "md.gildedgatey"];
		public static readonly string[] MenuTypes = ["md.well", "md.gardenfence", "md.gardengate", "md.flowerbed", "md.banner", "md.stonepath"];
		public static readonly string[] Names = ["Puits", "Cloture de jardin", "Portillon ouvert", "Massif de fleurs", "Drapeau", "Chemin de pierre"];
		public static bool Linear(string type) => type is "md.gardenfence" or "md.gardenfencey" or "md.gildedfence" or "md.gildedfencey" or "md.stonepath";
		public static bool Rotatable(string type) => type.StartsWith("md.gardenfence", StringComparison.Ordinal) || type.StartsWith("md.gardengate", StringComparison.Ordinal)
			|| type.StartsWith("md.gildedfence", StringComparison.Ordinal) || type.StartsWith("md.gildedgate", StringComparison.Ordinal);
		public static string Rotate(string type) => !Rotatable(type) ? type : type.EndsWith('y') ? type[..^1] : type + "y";

		// The same bounded plan is used by the preview and by synchronized placement orders.
		public static DecorationPlot[] Plan(string type, CPos start, CPos end)
		{
			if (!Types.Contains(type)) return [];
			if (!Linear(type)) return start == end ? [new(type, start)] : [];
			var dx = end.X - start.X; var dy = end.Y - start.Y;
			if (type != "md.stonepath")
			{
				if (dx == 0 && dy == 0) return [new(type, start)];
				var family = type.EndsWith('y') ? type[..^1] : type;
				if (Math.Abs(dx) >= Math.Abs(dy)) { dy = 0; type = family; }
				else { dx = 0; type = family + "y"; }
			}
			if (Math.Abs((long)dx) + Math.Abs((long)dy) >= MaxLength) return [];
			var cells = new List<DecorationPlot> { new(type, start) };
			for (var i = 1; i <= Math.Abs(dx); i++) cells.Add(new(type, start + new CVec(i * Math.Sign(dx), 0)));
			for (var i = 1; i <= Math.Abs(dy); i++) cells.Add(new(type, start + new CVec(dx, i * Math.Sign(dy))));
			return cells.ToArray();
		}
		public static Actor At(World w, Player p, CPos cell) => w.ActorsWithTrait<MedievalDecoration>().Select(pair => pair.Actor)
			.Where(a => a.Owner == p && a.IsInWorld && !a.IsDead && a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location).Contains(cell))
			.OrderBy(a => a.Trait<MedievalDecoration>().Info.Ground).ThenByDescending(a => a.ActorID).FirstOrDefault();
		public static DecorationPlot[] Missing(World w, Player p, DecorationPlot[] plan) => plan.Where(plot => At(w, p, plot.Cell)?.Info.Name != plot.Type).ToArray();
		public static bool Valid(World w, Player p, DecorationPlot[] plan) => plan.Length > 0
			&& Missing(w, p, plan).All(plot => MedievalSettlement.CanPlace(w, p, plot.Type, plot.Cell));
		public static void Place(Actor self, string type, CPos start, CPos end, Action<string> message)
		{
			var plan = Plan(type, start, end);
			self.World.AddFrameEndTask(w =>
			{
				if (!Valid(w, self.Owner, plan)) { message("Trace invalide : terrain libre requis, 64 cases maximum."); return; }
				var missing = Missing(w, self.Owner, plan);
				foreach (var plot in missing)
					w.CreateActor(plot.Type, [new OwnerInit(self.Owner), new LocationInit(plot.Cell), new MedievalConstructionInit()]);
				message(missing.Length == 0 ? "Ces decorations sont deja en place."
						: $"{missing.Length} decoration(s) : les batisseurs apportent les materiaux, y compris l'or requis. Hors depot, utilisez une charrette.");
			});
		}
		public static void Remove(Actor self, Actor decoration, Action<string> message)
		{
			if (!MedievalDemolition.Owned(decoration, self.Owner) || decoration.TraitOrDefault<MedievalDecoration>() == null || self.World.Paused) return;
			message(decoration.Trait<MedievalDemolition>().Request(decoration));
		}
	}
}
