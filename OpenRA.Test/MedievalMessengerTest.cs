using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalMessengerTest
	{
		[Test]
		public void RecipientMustWaitForDeliveryAndOpenLetter()
		{
			var letter = new MessengerLetter();
			Assert.That(letter.Write("Trêve à midi ?", "A", "B"));
			Assert.That(letter.TextFor("A", false), Is.EqualTo("Trêve à midi ?"));
			Assert.That(letter.TextFor("B", true), Is.Empty);
			Assert.That(letter.Read("B", false), Is.False);
			Assert.That(letter.Read("C", true), Is.False);
			Assert.That(letter.Read("B", true));
			Assert.That(letter.TextFor("B", true), Is.EqualTo("Trêve à midi ?"));
			Assert.That(letter.TextFor("C", true), Is.Empty);
		}
		[Test]
		public void ReplyIsSealedAndCannotBeReadBeforeReturning()
		{
			var letter = new MessengerLetter(); letter.Write("Trêve ?", "A", "B"); letter.Read("B", true);
			var original = letter.Hash;
			Assert.That(letter.Write("D'accord !", "B", "A"));
			Assert.That(letter.Hash, Is.Not.EqualTo(original));
			Assert.That(letter.Opened, Is.False);
			Assert.That(letter.TextFor("A", false), Is.Empty);
			Assert.That(letter.Read("A", true));
			Assert.That(letter.TextFor("A", true), Is.EqualTo("D'accord !"));
			letter.Clear();
			Assert.That(letter.Present, Is.False);
			Assert.That(letter.TextFor("B", true), Is.Empty);
		}
		[TestCase(null)]
		[TestCase(" \n\t\r ")]
		public void EmptyMessagesCannotReplaceAnExistingLetter(string body)
		{
			var letter = new MessengerLetter(); letter.Write("Initial", "A", "B"); var hash = letter.Hash;
			Assert.That(letter.Write(body, "B", "A"), Is.False);
			Assert.That(letter.Hash, Is.EqualTo(hash));
		}
		[Test]
		public void TextLimitPreservesUnicodeAndStripsControls()
		{
			Assert.That(MessengerLetter.Normalize("  Réponse\tà\nmidi\u0001  "), Is.EqualTo("Réponse à midi"));
			Assert.That(MessengerLetter.Normalize(new string('a', 319) + "🕊"), Is.EqualTo(new string('a', 319)));
			Assert.That(MessengerLetter.Normalize(new string('a', 319) + "\t\t\t"), Has.Length.EqualTo(319));
			Assert.That(MessengerLetter.Normalize(string.Concat(Enumerable.Repeat("🕊", 200))), Has.Length.EqualTo(320));
		}
		[Test]
		public void MessengerHasNoEquipmentRequirementAndIsNotACombatUnit()
		{
			var index = System.Array.IndexOf(MedievalEquipment.BarracksRecruits, "md.messenger");
			Assert.That(index, Is.GreaterThanOrEqualTo(0));
			Assert.That(MedievalEquipment.Kits[index].Sum(), Is.Zero);
			Assert.That(MedievalEquipment.Soldiers, Does.Not.Contain("md.messenger"));
		}
	}
}
