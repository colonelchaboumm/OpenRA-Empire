using System.Linq;

namespace OpenRA.Mods.Medieval
{
	// Read-only presentation: beds include people already resting and their reserved arrivals.
	public sealed record MedievalOccupancy(string Title, string Details)
	{
		static string Rate(int used, int capacity) => capacity > 0 ? $" · {(100L * used + capacity / 2) / capacity} %" : "";
		static string Free(int used, int capacity) => used <= capacity ? $"{capacity - used} libres" : $"{used - capacity} places manquantes";
		public static MedievalOccupancy Read(Actor actor)
		{
			if (actor == null || !actor.IsInWorld || actor.IsDead) return null;
			var shelter = actor.TraitOrDefault<MedievalShelter>();
			if (shelter == null && actor.TraitOrDefault<MedievalMarket>() == null) return null;
			if (!MedievalStockpile.Active(actor))
				return new("Occupation indisponible", actor.TraitOrDefault<MedievalBuilding>()?.Demolishing == true
					? "Bâtiment en démolition."
					: shelter != null ? $"En construction · {shelter.Info.Beds} lits après achèvement." : "Marché en construction.");
			if (shelter != null)
			{
				var occupants = actor.World.ActorsWithTrait<MedievalRest>()
					.Where(a => a.Actor.Owner == actor.Owner && MedievalStockpile.Active(a.Actor) && a.Trait.Shelter == actor).ToArray();
				var present = occupants.Count(a => a.Trait.Sleeping);
				return new($"Occupation au repos : {occupants.Length}/{shelter.Info.Beds}{Rate(occupants.Length, shelter.Info.Beds)}",
					$"{present} à l’abri · {occupants.Length - present} en route · {Free(occupants.Length, shelter.Info.Beds)}");
			}

			// Ordinary markets recruit but provide no beds: show the nation's recruitment limit explicitly.
			var population = MedievalSettlement.Population(actor.Owner);
			var pending = MedievalSettlement.PendingPopulation(actor.Owner);
			var capacity = MedievalSettlement.Housing(actor.Owner);
			return new($"Logements nation : {population + pending}/{capacity}{Rate(population + pending, capacity)}",
				$"{population} habitants · {pending} en formation · {Free(population + pending, capacity)}");
		}
	}
}
