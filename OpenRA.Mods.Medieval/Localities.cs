using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public static class LocalityNames
	{
		public const int MaxLength = 32;
		public static string Normalize(string value)
		{
			var text = new StringBuilder(); var space = false;
			foreach (var rune in (value ?? "").EnumerateRunes())
			{
				if (Rune.IsWhiteSpace(rune)) { space = text.Length > 0; continue; }
				if (Rune.IsControl(rune)) continue;
				if (text.Length + (space ? 1 : 0) + rune.Utf16SequenceLength > MaxLength) break;
				if (space) text.Append(' ');
				text.Append(rune); space = false;
			}
			return text.ToString();
		}
		public static bool Available(string name, IEnumerable<string> others) => name.Length > 0 && !others.Contains(name, StringComparer.OrdinalIgnoreCase);
		public static int Hash(string name) => name.Aggregate(17, (hash, c) => unchecked(hash * 31 + c));
		public static bool InRange(WPos first, WPos second, WDist radius) => (first - second).HorizontalLengthSquared <= (long)radius.Length * radius.Length;
	}
	public class MedievalLocalitiesInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalLocalities();
	}
	// IDs identify settlements independently of their first market, so a surviving
	// neighborhood retains its name when the founding market is lost or replaced.
	public sealed class MedievalLocalities : ITick, ISync
	{
		readonly SortedDictionary<int, string> names = [];
		[VerifySync] int nextId = 1;
		[VerifySync] int cadence;
		[VerifySync] public int NamesHash => names.Aggregate(17, (hash, item) => unchecked(hash * 31 + item.Key * 17 + LocalityNames.Hash(item.Value)));
		public static MedievalLocalities For(Player p) => p.PlayerActor.Trait<MedievalLocalities>();
		public static IEnumerable<Actor> Markets(Player p) => p.World.ActorsWithTrait<MedievalLocality>().Select(a => a.Actor)
			.Where(a => a.Owner == p && MedievalStockpile.Active(a)).OrderBy(a => a.ActorID);
		public static int Id(Actor a) => a?.IsInWorld == true && !a.IsDead ? a.TraitOrDefault<MedievalLocality>()?.Id ?? 0 : 0;
		public string Name(int id) => names.TryGetValue(id, out var name) ? name : "Localite en cours de fondation";
		public IEnumerable<Actor> Members(Player p, int id) => Markets(p).Where(a => Id(a) == id && id != 0);
		public Actor Anchor(Player p, int id) => Members(p, id).OrderBy(a => a.TraitOrDefault<MedievalMarket>() == null ? 1 : 0).ThenBy(a => a.ActorID).FirstOrDefault();
		public bool City(Player p, int id) => Members(p, id).Any(a => a.TraitOrDefault<MedievalMarket>() != null);
		public string Kind(Player p, int id) => City(p, id) ? "Ville" : "Village";
		public IEnumerable<int> Ids(Player p) => Markets(p).Select(Id).Where(id => id > 0).Distinct().OrderBy(id => id);
		public Actor At(Player p, WPos position) => Markets(p).Where(a => Id(a) > 0 && names.ContainsKey(Id(a))
			&& LocalityNames.InRange(a.CenterPosition, position, a.Trait<MedievalFoodProvider>().Info.Radius))
			.OrderBy(a => (a.CenterPosition - position).HorizontalLengthSquared).ThenBy(a => a.ActorID).FirstOrDefault();
		public string RenameError(Actor market, string text)
		{
			if (!MedievalStockpile.Active(market) || Id(market) == 0 || !names.ContainsKey(Id(market))) return "Attendez la fin du chantier et la fondation.";
			var name = LocalityNames.Normalize(text);
			if (name.Length == 0) return "Saisissez un nom de ville ou de village.";
			return LocalityNames.Available(name, names.Where(n => n.Key != Id(market)).Select(n => n.Value)) ? "" : "Ce nom est deja utilise par une autre localite.";
		}
		public bool Rename(Actor market, string text)
		{
			if (market.Owner.PlayerActor.Trait<MedievalLocalities>() != this || RenameError(market, text).Length > 0) return false;
			names[Id(market)] = LocalityNames.Normalize(text); return true;
		}
		public string Summary(Player p, int id)
		{
			var members = Members(p, id).ToArray();
			var grand = members.Count(a => a.TraitOrDefault<MedievalMarket>() != null);
			return $"{grand} grand(s) marche(s) / {members.Length - grand} petit(s) marche(s)";
		}
		void ITick.Tick(Actor self)
		{
			if (--cadence > 0) return; cadence = 25;
			var markets = Markets(self.Owner).ToArray();
			foreach (var market in markets)
			{
				var locality = market.Trait<MedievalLocality>();
				if (locality.Id != 0 && names.ContainsKey(locality.Id)) continue;
				var neighbor = At(self.Owner, market.CenterPosition);
				if (neighbor != null) { locality.Assign(Id(neighbor)); continue; }
				var id = nextId++;
				var prefix = market.TraitOrDefault<MedievalMarket>() != null ? "Ville " : "Village ";
				var name = prefix + id; var suffix = 2;
				while (!LocalityNames.Available(name, names.Values)) name = prefix + id + " (" + suffix++ + ")";
				names.Add(id, name); locality.Assign(id);
			}
			var present = markets.Select(Id).ToHashSet();
			foreach (var id in names.Keys.Where(id => !present.Contains(id)).ToArray()) names.Remove(id);
		}
		public static string MarketName(Actor market)
		{
			if (market == null || !market.IsInWorld || market.IsDead) return "Marche indisponible";
			var id = Id(market); if (id == 0) return market.TraitOrDefault<MedievalMarketStall>() != null ? "Petit marche en construction" : "Marche en construction";
			var registry = For(market.Owner);
			var ordinal = Array.IndexOf(registry.Members(market.Owner, id).ToArray(), market) + 1;
			return registry.Name(id) + (market.TraitOrDefault<MedievalMarketStall>() != null ? " / etal " : " / marche ") + ordinal;
		}
	}
	public class MedievalLocalityInfo : TraitInfo, Requires<MedievalFoodProviderInfo>
	{
		public readonly int LabelHeight = 166;
		public override object Create(ActorInitializer init) => new MedievalLocality(this);
	}
	public sealed class MedievalLocality : IResolveOrder, INotifyOwnerChanged, IRenderAnnotations, ISync
	{
		readonly MedievalLocalityInfo info;
		[VerifySync] public int Id { get; private set; }
		public MedievalLocality(MedievalLocalityInfo info) { this.info = info; }
		internal void Assign(int id) { Id = id; }
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalRenameLocality" && order.Subject == self) MedievalLocalities.For(self.Owner).Rename(self, order.TargetString);
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Id = 0; }
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (Id == 0 || self.Owner != self.World.LocalPlayer || !MedievalStockpile.Active(self) || self.World.FogObscures(self)) yield break;
			var registry = MedievalLocalities.For(self.Owner);
			if (registry.Anchor(self.Owner, Id) == self)
				yield return new LocalityLabelRenderable(self.CenterPosition, info.LabelHeight, registry.Kind(self.Owner, Id) + " : " + registry.Name(Id));
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
}
