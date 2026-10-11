using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalSabotageTest
	{
		[Test]
		public void FireDestroysRealCombustiblesWithoutCreatingGoodsOrBurningStone()
		{
			var stock = new Inventory([100, 100, 100, 100], [40, 31, 9, 80]);
			var lost = 0;
			for (var i = 0; i < 30; i++) lost += SabotageRules.BurnStock(stock);
			Assert.That(lost, Is.EqualTo(80));
			Assert.That(Enumerable.Range(0, 4).Select(stock.Amount), Is.EqualTo(new[] { 0, 0, 0, 80 }));
		}
		[Test]
		public void FirePreservesOutstandingDeliveryContractsAndNeverBurnsIncomingCargo()
		{
			var source = new Inventory([100, 100, 100, 100], [15, 20, 5, 30]);
			var destination = new Inventory([100, 100, 100, 100]);
			source.ReserveOut(0, 10); destination.ReserveIn(0, 10); source.ReserveIn(1, 25);
			var lost = 0;
			for (var i = 0; i < 30; i++) lost += SabotageRules.BurnStock(source);
			Assert.That(lost, Is.EqualTo(30)); Assert.That(source.Amount(0), Is.EqualTo(10));
			source.Load(0, 10); destination.Unload(0, 10); source.Unload(1, 25);
			Assert.That(source.Amount(1), Is.EqualTo(25)); Assert.That(destination.Amount(0), Is.EqualTo(10));
			Assert.That(source.Reserved(0) + destination.Incoming(0), Is.Zero);
		}
		[Test]
		public void SaboteurRecipeUsesExistingRecruitmentIndexesWithoutChangingEarlierTroops()
		{
			Assert.That(MedievalEquipment.BarracksRecruits.Length, Is.EqualTo(MedievalEquipment.Kits.Length));
			Assert.That(MedievalEquipment.BarracksNames.Length, Is.EqualTo(MedievalEquipment.Kits.Length));
			Assert.That(MedievalEquipment.BarracksRecruits[9], Is.EqualTo("md.commander"));
			Assert.That(MedievalEquipment.BarracksRecruits[10], Is.EqualTo("md.saboteur"));
			Assert.That(MedievalEquipment.Kits[10], Is.EqualTo(new[] { 0, 1, 0, 0 }));
		}
	}
}
