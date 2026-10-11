using System;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public static class MedievalStorageInterface
	{
		public static string Totals(Actor a, params int[] goods) => a == null ? "" : string.Join(" | ", goods.Select(g =>
			$"{StorageGoods.Names[g]} {StorageGoods.Amount(a, g)}/{StorageGoods.Stock(a, g)?.Capacity(StorageGoods.Index(g)) ?? 0}"));
		public static void Bind(Widget widget, World world, WorldRenderer renderer, string prefix, Func<Actor> selected)
		{
			Actor previous = null; var index = 0; var seenZone = -1; var kind = StorageKind.Mixed;
			Actor Current()
			{
				var a = selected();
				if (a != previous) { previous = a; index = 0; seenZone = -1; kind = a?.Trait<MedievalStockyard>().Zones.FirstOrDefault()?.Kind ?? StorageKind.Mixed; }
				if (a != null)
				{
					var zones = a.Trait<MedievalStockyard>().Zones; index = Math.Min(index, zones.Count);
					var zone = index < zones.Count ? zones[index] : null;
					if (seenZone != (zone?.Id ?? 0)) { seenZone = zone?.Id ?? 0; if (zone != null) kind = zone.Kind; }
				}
				return a;
			}
			StorageZone Zone(Actor a) => index < a.Trait<MedievalStockyard>().Zones.Count ? a.Trait<MedievalStockyard>().Zones[index] : null;
			var choose = widget.Get<ButtonWidget>(prefix + "_KIND");
			choose.GetText = () => { Current(); return "Type : " + StorageGoods.KindNames[(int)kind] + " >"; };
			choose.GetTooltipText = () => "Choisir les marchandises de la zone";
			choose.GetTooltipDesc = () => "Cliquez pour choisir : mixte, bois, cereales, viande, pierre, or, equipements ou textiles.\nLe changement prend effet apres avoir trace la zone.";
			choose.IsDisabled = () => world.Paused || Current() is not { } a || !MedievalStockpile.Active(a);
			choose.OnClick = () => { if (Current() is not { } a) return; do { kind = (StorageKind)(((int)kind + 1) % 8); } while (!a.Trait<MedievalStockyard>().Supports(a, kind)); };
			var cycle = widget.Get<ButtonWidget>(prefix + "_ZONE");
			cycle.GetText = () => Current() is { } a && Zone(a) is { } z ? $"Zone {index + 1}/{a.Trait<MedievalStockyard>().Zones.Count} >" : "Nouvelle zone >";
			cycle.GetTooltipText = () => "Choisir une zone ou en ajouter une";
			cycle.GetTooltipDesc = () => "Cliquez pour parcourir les zones existantes, puis Nouvelle zone.\nTracer remplace la zone choisie ou ajoute une reserve.";
			cycle.IsDisabled = () => Current() is not { } a || !a.Trait<MedievalStockyard>().HasArea;
			cycle.OnClick = () => { if (Current() is not { } a) return; index = (index + 1) % (a.Trait<MedievalStockyard>().Zones.Count + 1); if (Zone(a) is { } z) kind = z.Kind; };
			var place = widget.Get<ButtonWidget>(prefix + "_PLACE");
			place.GetText = () => Current() is { } a && Zone(a) != null ? "Redessiner / reamenager" : "Tracer la nouvelle zone";
			place.GetTooltipText = () => "Tracer deux coins sur le terrain";
			place.GetTooltipDesc = () => "La surface ajoute une capacite reelle au batiment. Les objets affichent les quantites presentes.\nUn incendie detruit les marchandises combustibles et abime la zone.\nReamenager une zone eteinte retablit sa capacite, sans rendre les marchandises perdues.";
			place.IsDisabled = () => world.Paused || Current() is not { } a || !MedievalStockpile.Active(a) || a.TraitOrDefault<MedievalBurnable>()?.Burning == true;
			place.OnClick = () => { if (!place.IsDisabled() && Current() is { } a) world.OrderGenerator = new MedievalStockyardOrderGenerator(world, renderer, a, kind, Zone(a)?.Id ?? 0); };
			var remove = widget.Get<ButtonWidget>(prefix + "_REMOVE");
			remove.GetText = () => "Retirer cette zone";
			remove.GetTooltipText = () => "Retrait sans perte de marchandises";
			remove.GetTooltipDesc = () => Current() is { } a && Zone(a) is { } z ? a.Trait<MedievalStockyard>().RemoveError(a, z.Id) ?? "La capacite diminue. Les stocks et les livraisons reservees doivent encore tenir dans les autres reserves." : "Choisissez une zone existante.";
			remove.IsDisabled = () => world.Paused || Current() is not { } a || Zone(a) is not { } z || a.Trait<MedievalStockyard>().RemoveError(a, z.Id) != null;
			remove.OnClick = () => { if (!remove.IsDisabled() && Current() is { } a && Zone(a) is { } z) world.IssueOrder(new Order("MedievalRemoveStockyard", a, false) { TargetString = "remove", ExtraData = (uint)z.Id }); };
			widget.Get<LabelWidget>(prefix + "_HINT").GetText = () => Current() is not { } a ? "" : Zone(a) is { Damaged: true }
				? "Reserve endommagee : reamenagez-la apres extinction pour retablir sa capacite. Les biens brules sont perdus."
				: a.Trait<MedievalStockyard>().Status;
		}
	}
}
