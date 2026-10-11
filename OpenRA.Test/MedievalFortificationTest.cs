using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalFortificationTest
	{
		[TestCase(12, 3, "md.palisade", 13)]
		[TestCase(3, -12, "md.palisadey", 13)]
		[TestCase(-12, 3, "md.palisade", 13)]
		public void WallLinesSnapToTheDominantAxis(int x, int y, string type, int count)
		{
			var plan = MedievalFortifications.Plan("md.palisade", new CPos(0, 0), new CPos(x, y));
			Assert.That(plan.Length, Is.EqualTo(count));
			Assert.That(plan.All(p => p.Type == type), Is.True);
			Assert.That(plan.Select(p => p.Cell).Distinct().Count(), Is.EqualTo(count));
			Assert.That(plan.All(p => type == "md.palisade" ? p.Cell.Y == 0 : p.Cell.X == 0), Is.True);
		}
		[Test]
		public void OrderPlannerRejectsUnknownActorsAndOversizedLines()
		{
			Assert.That(MedievalFortifications.Plan("md.house", new CPos(0, 0), new CPos(0, 0)), Is.Empty);
			Assert.That(MedievalFortifications.Plan("md.woodtower", new CPos(0, 0), new CPos(1, 0)), Is.Empty);
			Assert.That(MedievalFortifications.Plan("md.palisade", new CPos(0, 0), new CPos(64, 0)), Is.Empty);
			Assert.That(MedievalFortifications.Plan("md.palisade", new CPos(0, 0), new CPos(63, 0)).Length, Is.EqualTo(64));
		}
		[Test]
		public void RotationPreservesTheGateAndStandaloneWallVariants()
		{
			foreach (var type in new[] { "md.woodgate", "md.palisade" })
			{
				Assert.That(MedievalFortifications.Rotate(type), Is.EqualTo(type + "y"));
				Assert.That(MedievalFortifications.Rotate(MedievalFortifications.Rotate(type)), Is.EqualTo(type));
			}
			Assert.That(MedievalFortifications.Rotate("md.woodtower"), Is.EqualTo("md.woodtower"));
		}
	}
}
