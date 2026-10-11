using NUnit.Framework;
using OpenRA.Mods.Medieval;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalHeraldryTest
	{
		[TestCase("#356fad", true, 0x356FADu)]
		[TestCase("ABCDEF", true, 0xABCDEFu)]
		[TestCase("000000", true, 0u)]
		[TestCase("FFFFFF", true, 0xFFFFFFu)]
		[TestCase("##356FAD", false, 0u)]
		[TestCase("123", false, 0u)]
		[TestCase("12345678", false, 0u)]
		[TestCase("GG5566", false, 0u)]
		[TestCase(null, false, 0u)]
		public void HexColorRequiresExactlySixDigits(string text, bool valid, uint expected)
		{
			Assert.That(MedievalHeraldry.TryColor(text, out var rgb), Is.EqualTo(valid));
			if (valid) Assert.That(rgb, Is.EqualTo(expected));
		}
		[Test]
		public void InvalidIdentityCannotPartiallyChangeAnEmpire()
		{
			var identity = new MedievalHeraldry(Color.Red);
			Assert.That(identity.SetEmpire("  Royaume   d'Été  ", 0x123456, 5), Is.True);
			Assert.That(identity.CountryName, Is.EqualTo("Royaume d'Été"));
			var countryHash = identity.CountryHash;
			Assert.That(identity.Set("#123456", 5), Is.True);
			Assert.That(identity.Set("#FFFFFF", 6), Is.False);
			Assert.That(identity.Set("oops", 0), Is.False);
			Assert.That(identity.SetEmpire(" \t\n", 0xFFFFFF, 0), Is.False);
			Assert.That(identity.SetEmpire(null, 0xFFFFFF, 0), Is.False);
			Assert.That(identity.SetEmpire("Autre pays", 0x1000000, 0), Is.False);
			Assert.That(identity.SetEmpire("Autre pays", 0xFFFFFF, 255), Is.False);
			Assert.That(identity.CountryName, Is.EqualTo("Royaume d'Été"));
			Assert.That(identity.CountryHash, Is.EqualTo(countryHash));
			Assert.That(identity.Hex, Is.EqualTo("123456"));
			Assert.That(identity.Emblem, Is.EqualTo(5));
		}
		[Test]
		public void StandardsOnlySupportAlliesAndDoNotStack()
		{
			Assert.That(MoraleState.Standards([(PlayerRelationship.Ally, false, false), (PlayerRelationship.Ally, false, false)]), Is.EqualTo(15));
			Assert.That(MoraleState.Standards([(PlayerRelationship.Enemy, false, false), (PlayerRelationship.Neutral, false, false)]), Is.Zero);
			Assert.That(MoraleState.Standards([(PlayerRelationship.Ally, true, false)]), Is.EqualTo(7));
			Assert.That(MoraleState.Standards([(PlayerRelationship.Ally, true, true)]), Is.Zero);
		}
		[Test]
		public void CombinedSupportIsCappedAndHungerStillWeakensTroops()
		{
			Assert.That(MoraleState.Support(20, 15), Is.EqualTo(30));
			Assert.That(MoraleState.Support(10, 7), Is.EqualTo(17));
			var state = new MoraleState();
			for (var i = 0; i < 100; i++) state.Advance(true, false, MoraleState.Support(20, 15), 0);
			Assert.That(state.Value, Is.EqualTo(70));
			for (var i = 0; i < 100; i++) state.Advance(true, true, 0, 0);
			Assert.That(state.Value, Is.EqualTo(20));
		}
	}
}
