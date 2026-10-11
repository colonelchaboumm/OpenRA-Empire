using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalFortificationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalFortification();
	}
	public sealed class MedievalFortification { }
	public readonly record struct FortificationPlot(string Type, CPos Cell);
	public static class MedievalFortifications
	{
		public const int MaxLength = 64;
		public static readonly string[] Types = ["md.palisade", "md.palisadey", "md.woodgate", "md.woodgatey", "md.woodtower"];
		public static readonly string[] MenuTypes = ["md.palisade", "md.woodgate", "md.woodtower"];
		public static readonly string[] Names = ["Palissade en bois", "Porte en bois", "Tour en bois"];
		public static bool Linear(string type) => type is "md.palisade" or "md.palisadey";
		public static bool Rotatable(string type) => Linear(type) || type is "md.woodgate" or "md.woodgatey";
		public static string Rotate(string type) => !Rotatable(type) ? type : type.EndsWith('y') ? type[..^1] : type + "y";
		public static FortificationPlot[] Plan(string type, CPos start, CPos end)
		{
			if (!Types.Contains(type)) return [];
			if (!Linear(type)) return start == end ? [new(type, start)] : [];
			var dx = (long)end.X - start.X; var dy = (long)end.Y - start.Y;
			if (dx == 0 && dy == 0) return [new(type, start)];
			if (Math.Abs(dx) >= Math.Abs(dy)) { dy = 0; type = "md.palisade"; }
			else { dx = 0; type = "md.palisadey"; }
			var length = Math.Abs(dx) + Math.Abs(dy);
			if (length >= MaxLength) return [];
			return Enumerable.Range(0, (int)length + 1).Select(i => new FortificationPlot(type,
				start + new CVec(i * Math.Sign(dx), i * Math.Sign(dy)))).ToArray();
		}
		public static Actor At(World w, Player p, CPos cell) => w.ActorsWithTrait<MedievalFortification>().Select(pair => pair.Actor)
			.FirstOrDefault(a => a.Owner == p && a.IsInWorld && !a.IsDead && a.Location == cell);
		public static FortificationPlot[] Missing(World w, Player p, FortificationPlot[] plan) => plan.Where(plot =>
		{
			var existing = At(w, p, plot.Cell)?.Info.Name;
			return existing != plot.Type && !(Linear(plot.Type) && Linear(existing));
		}).ToArray();
		public static bool Valid(World w, Player p, FortificationPlot[] plan) => plan.Length > 0
			&& Missing(w, p, plan).All(plot => MedievalSettlement.CanPlace(w, p, plot.Type, plot.Cell));
		public static void Place(Actor self, string type, CPos start, CPos end, Action<string> message)
		{
			var plan = Plan(type, start, end);
			self.World.AddFrameEndTask(w =>
			{
				if (!Valid(w, self.Owner, plan)) { message("Fortification : terrain libre requis, 64 cases maximum par ligne."); return; }
				var missing = Missing(w, self.Owner, plan);
				foreach (var plot in missing)
					w.CreateActor(plot.Type, [new OwnerInit(self.Owner), new LocationInit(plot.Cell), new MedievalConstructionInit()]);
				message(missing.Length == 0 ? "Ces fortifications sont deja en place."
					: $"{missing.Length} chantier(s) de fortification. Les batisseurs apportent le bois. Gardez une ouverture pour une porte.");
			});
		}
	}
}
