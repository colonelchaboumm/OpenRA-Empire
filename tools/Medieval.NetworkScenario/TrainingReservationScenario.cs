using System.Linq;
using OpenRA.Mods.Medieval;
using OpenRA.Traits;

namespace OpenRA.Platforms.NetworkTest
{
	public sealed partial class TransferNetworkAudit
	{
		readonly Actor[] trainingShops = new Actor[2], trainingCarts = new Actor[2];
		readonly ManualCargo[] lateBookings = new ManualCargo[2];
		int Trained(World w, Player owner) => w.Actors.Count(a => a.Owner == owner && a.Info.Name == "md.elitearcher" && a.IsInWorld && !a.IsDead);
		bool TrainingReservations(World w)
		{
			if (tick == 4300) w.AddFrameEndTask(_ =>
			{
				foreach (var a in w.Actors.Where(a => a.Info.Name.StartsWith("md.", System.StringComparison.Ordinal)).ToArray()) a.Dispose();
				for (var p = 0; p < players.Length; p++)
				{
					Actor Spawn(string type, CPos cell) => w.CreateActor("md." + type, [new OwnerInit(players[p]), new LocationInit(cell)]);
					var origin = new CPos(47, -14 + p * 24);
					Spawn("house", origin + new CVec(-5, 0));
					var shop = trainingShops[p] = Spawn("barracks", origin);
					trainingCarts[p] = Spawn("tradecart", origin + new CVec(22, 6));
					var workshop = shop.Trait<MedievalMilitaryWorkshop>();
					// A staffed workshop invokes Work in the same way; isolate material consumption from hauling.
					workshop.Recruit = 4;
					for (var r = 0; r < 4; r++) workshop.Inputs.Add(r, workshop.Required(shop)[r]);
					for (var r = 0; r < 3; r++) shop.Trait<MedievalEquipmentStore>().Stock.Add(r, MedievalEquipment.Kits[4][r]);
					shop.Trait<MedievalCampStore>().Stock.Add(MedievalCampStore.Cloth, workshop.RequiredCloth(shop));
				}
			});
			if (tick == 4330)
				for (var p = 0; p < players.Length; p++)
					if (w.LocalPlayer == players[p]) w.IssueOrder(new Order("MedievalLoadResource", trainingCarts[p], Target.FromActor(trainingShops[p]), false)
						{ ExtraData = 5, TargetString = "1" });
			if (tick == 4360)
				for (var p = 0; p < players.Length; p++)
				{
					var shop = trainingShops[p]; var workshop = shop.Trait<MedievalMilitaryWorkshop>();
					Check(trainingCarts[p].Trait<MedievalManualTransfer>().Busy && shop.Trait<MedievalEquipmentStore>().Stock.Reserved(0) == 1,
						"TCP cart order reserves the barracks bow before pickup: " + p);
					Check(!workshop.Ready(shop) && workshop.MissingSupplies(shop).Contains("Arc", System.StringComparison.Ordinal),
						"Reserved bows are unavailable for training and reported missing: " + p);
					for (var i = 0; i <= workshop.Info.WorkSteps; i++) workshop.Work(shop);
					Check(workshop.Progress == 0, "Training cannot advance using a cart's reserved equipment: " + p);
					if (w.LocalPlayer == players[p]) w.IssueOrder(new Order("Stop", trainingCarts[p], false));
				}
			if (tick == 4370)
				foreach (var player in players) Check(Trained(w, player) == 0, "No free soldier can spawn while its equipment is reserved: " + player.InternalName);
			if (tick == 4400)
				for (var p = 0; p < players.Length; p++)
				{
					var shop = trainingShops[p]; var workshop = shop.Trait<MedievalMilitaryWorkshop>();
					var equipment = shop.Trait<MedievalEquipmentStore>().Stock;
					Check(equipment.Reserved(0) == 0 && workshop.Ready(shop), "Cancelling the cart releases the equipment for training: " + p);
					var armor = ManualCargo.Reserve(equipment, trainingCarts[p].Trait<MedievalEquipmentStore>().Stock, 2, 1);
					Check(armor != null && !workshop.Ready(shop), "Armor reservations also prevent training: " + p);
					for (var i = 0; i <= workshop.Info.WorkSteps; i++) workshop.Work(shop);
					Check(workshop.Progress == 0, "Reserved armor cannot advance training: " + p);
					armor?.Cancel();
					for (var i = 0; i <= workshop.Info.WorkSteps; i++) workshop.Work(shop);
					// Reserve after Work queues the spawn, but before the frame-end callback consumes supplies.
					lateBookings[p] = ManualCargo.Reserve(equipment, trainingCarts[p].Trait<MedievalEquipmentStore>().Stock, 0, 1);
					Check(lateBookings[p] != null, "Late equipment reservation must be exercised: " + p);
				}
			if (tick == 4401)
				for (var p = 0; p < players.Length; p++)
				{
					var shop = trainingShops[p]; var workshop = shop.Trait<MedievalMilitaryWorkshop>();
					Check(Trained(w, players[p]) == 0 && workshop.Recruit == 4
						&& Enumerable.Range(0, 4).All(r => workshop.Inputs.Amount(r) == workshop.Required(shop)[r]),
						"Frame-end spawn rechecks late reservations without spending provisions: " + p);
					lateBookings[p]?.Cancel();
					workshop.Work(shop);
				}
			if (tick == 4402)
				for (var p = 0; p < players.Length; p++)
				{
					var shop = trainingShops[p]; var workshop = shop.Trait<MedievalMilitaryWorkshop>();
					var equipment = shop.Trait<MedievalEquipmentStore>().Stock;
					Check(Trained(w, players[p]) == 1 && workshop.Recruit == -1
						&& Enumerable.Range(0, 4).All(r => workshop.Inputs.Amount(r) == 0)
						&& Enumerable.Range(0, 3).All(r => equipment.Amount(r) == 0 && equipment.Reserved(r) == 0)
						&& shop.Trait<MedievalCampStore>().Stock.Amount(MedievalCampStore.Cloth) == 0,
						"Resuming training creates exactly one soldier and pays all supplies exactly once: " + p);
				}
			// Exercise malformed commands only after the recruitment prerequisites passed.
			if (tick == 4440 && errors.Count == 0)
			{
				w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor, false) { TargetString = "md.house" });
				w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor, Target.FromCell(w, new CPos(60, 0)), false));
				w.IssueOrder(new Order("MedievalPlace", w.LocalPlayer.PlayerActor, Target.FromCell(w, new CPos(60, 0)), false) { TargetString = "not-a-building" });
			}
			if (tick == 4500 && errors.Count == 0) Check(!w.ActorsWithTrait<MedievalBuilding>().Any(a => !a.Trait.Complete)
				&& players.All(p => p.PlayerActor.Trait<MedievalSettlement>().HousesPlaced == 0),
				"Malformed construction orders from both TCP clients neither crash nor create construction sites");
			return tick >= 4500;
		}
	}
}
