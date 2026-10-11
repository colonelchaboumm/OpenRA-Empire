using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalSiegeWorkshopInfo : TraitInfo, Requires<MedievalBuildingInfo>
	{
		public readonly int WorkSteps = 65;
		public override object Create(ActorInitializer init) => new MedievalSiegeWorkshop(this);
	}
	public sealed class MedievalSiegeWorkshop : IResolveOrder, INotifyOwnerChanged, ISync
	{
		readonly MedievalSiegeWorkshopInfo info;
		public readonly Inventory Inputs = new([150, 20, 0, 50]);
		[VerifySync] public int InputsHash => Inputs.Hash;
		[VerifySync] public int Product { get; private set; } = -1;
		[VerifySync] public int Progress { get; private set; }
		[VerifySync] public int LastWorkTick { get; private set; } = -1000000;
		[VerifySync] bool spawning;
		public bool Working => Product >= 0;
		public int WorkSteps => info.WorkSteps;
		public bool Operating(Actor self) => Working && Ready && Progress < info.WorkSteps && self.World.WorldTick - LastWorkTick < 24;
		public int PendingPopulation => SiegeRules.NeedsHousing(Product) ? 1 : 0;
		public int[] Required => Working ? SiegeRules.Costs[Product] : [0, 0, 0, 0];
		public bool Ready => Working && Enumerable.Range(0, 4).All(r => Inputs.Amount(r) >= Required[r]);
		public string Status { get; private set; } = "Commandez un engin. Les batisseurs apportent les materiaux.";
		public MedievalSiegeWorkshop(MedievalSiegeWorkshopInfo info) { this.info = info; }
		public static IEnumerable<Actor> Workshops(Player p) => p.World.ActorsWithTrait<MedievalSiegeWorkshop>().Select(p => p.Actor)
			.Where(a => SiegeRules.Live(a, p) && MedievalStockpile.Active(a)).OrderBy(a => a.ActorID);
		public bool CanRequest(Actor self, int product) => product >= 0 && product < SiegeRules.Products.Length && MedievalStockpile.Active(self) && !Working
			&& (!SiegeRules.NeedsHousing(product) || MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) < MedievalSettlement.Housing(self.Owner));
		public bool Request(Actor self, int product)
		{
			if (!CanRequest(self, product)) return false;
			Product = product; Progress = 0; LastWorkTick = -1000000; Status = "Commande : " + SiegeRules.Names[product]; return true;
		}
		public string Supplies => string.Join(" / ", Enumerable.Range(0, 4).Where(r => Required[r] > 0)
			.Select(r => $"{Inputs.Amount(r)}/{Required[r]} {MedievalStockDisplay.Labels[r]}"));
		public bool Work(Actor self)
		{
			if (!Ready || spawning || !MedievalStockpile.Active(self)) return false;
			if (PendingPopulation > 0 && MedievalSettlement.Population(self.Owner) + MedievalSettlement.PendingPopulation(self.Owner) > MedievalSettlement.Housing(self.Owner))
			{ Status = "Conducteur en attente d'un logement."; return false; }
			LastWorkTick = self.World.WorldTick;
			Progress = Math.Min(info.WorkSteps, Progress + 1); Status = $"Fabrication : {Progress}/{info.WorkSteps}";
			if (Progress < info.WorkSteps) return true;
			var cell = MedievalAccess.Perimeter(self).Concat(self.World.Map.FindTilesInAnnulus(self.Location, 4, 7))
				.Where(c => self.World.Map.Contains(c) && !self.World.ActorMap.GetActorsAt(c).Any()
					&& new[] { "Clear", "Road", "Rough" }.Contains(self.World.Map.GetTerrainInfo(c).Type)).Select(c => (CPos?)c).FirstOrDefault();
			if (cell == null) { Status = "Sortie de l'atelier bloquee."; return false; }
			spawning = true; var product = Product; var owner = self.Owner;
			self.World.AddFrameEndTask(w =>
			{
				spawning = false;
				if (!SiegeRules.Live(self, owner) || !Ready || Product != product || w.ActorMap.GetActorsAt(cell.Value).Any()) return;
				if (PendingPopulation > 0 && MedievalSettlement.Population(owner) + MedievalSettlement.PendingPopulation(owner) > MedievalSettlement.Housing(owner)) return;
				for (var r = 0; r < 4; r++) Inputs.Take(r, Required[r]);
				w.CreateActor(SiegeRules.Products[Product], [new OwnerInit(owner), new LocationInit(cell.Value)]);
				MedievalAudio.Notify(self, "ready");
				Product = -1; Progress = 0; Status = "Engin termine, disponible a la sortie de l'atelier.";
			});
			return false;
		}
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "MedievalSiegeProduce") Request(self, Array.IndexOf(SiegeRules.Products, order.TargetString));
			else if (order.OrderString == "MedievalSiegeCancel") { Product = -1; Progress = 0; Status = "Commande annulee. Materiaux conserves dans l'atelier."; }
		}
		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { Product = -1; Progress = 0; }
	}

	public class MedievalSiegeWorkshopAnimationInfo : TraitInfo, Requires<WithSpriteBodyInfo>, Requires<MedievalSiegeWorkshopInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalSiegeWorkshopAnimation();
	}

	// Presentation follows orders and actual builder work. It never advances fabrication or inventory.
	public sealed class MedievalSiegeWorkshopAnimation : ITick
	{
		WithSpriteBody body;
		int clock;
		public string Sequence { get; private set; }
		public int Frame { get; private set; }
		public bool Operating { get; private set; }
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self)) return;
			body ??= self.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body");
			var job = self.Trait<MedievalSiegeWorkshop>();
			Operating = job.Operating(self);
			var next = !job.Working ? "idle" : job.Progress == 0 ? "ordered"
				: $"work-{Math.Min(2, job.Product)}-{Math.Min(2, job.Progress * 3 / job.WorkSteps)}";
			if (Sequence != next || body.DefaultAnimation.CurrentSequence.Name != next)
			{
				Sequence = next; clock = 0; Frame = 0;
				body.DefaultAnimation.PlayFetchIndex(next, () => Frame);
			}
			if (next == "ordered" || Operating)
			{
				clock = (clock + 1) % (body.DefaultAnimation.CurrentSequence.Length * 3);
				Frame = clock / 3;
			}
		}
	}
	public partial class MedievalWorker
	{
		bool FindSiegeJob(Actor self)
		{
			foreach (var workshop in MedievalSiegeWorkshop.Workshops(self.Owner).OrderBy(a => (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID))
			{
				var job = workshop.Trait<MedievalSiegeWorkshop>();
				if (!job.Working) continue;
				if (job.Ready) { destination = workshop; phase = 6; Status = "Fabrique un engin de siege"; return true; }
				for (var r = 0; r < 4; r++)
				{
					var missing = job.Required[r] - job.Inputs.Amount(r) - job.Inputs.Incoming(r);
					if (missing <= 0) continue;
					foreach (var source in MedievalConstructionSupply.Sources(self.Owner, workshop.CenterPosition))
						if (ReserveTrip(source, source.Trait<MedievalStockpile>().Stock, workshop, job.Inputs, r, Math.Min(Info.Capacity, missing))) return true;
				}
			}
			return false;
		}
	}
}
