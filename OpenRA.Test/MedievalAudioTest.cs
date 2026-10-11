using NUnit.Framework;
using OpenRA.Mods.Medieval;

namespace OpenRA.Test
{
	[TestFixture]
	public class MedievalAudioTest
	{
		[Test]
		public void DrumEnsembleGrowsWithoutBecomingDeafening()
		{
			Assert.That(MedievalAudio.DrumGain(0), Is.Zero);
			Assert.That(MedievalAudio.DrumGain(1), Is.EqualTo(1));
			for (var count = 2; count <= 10000; count++)
			{
				var gain = MedievalAudio.DrumGain(count);
				Assert.That(gain, Is.GreaterThanOrEqualTo(MedievalAudio.DrumGain(count - 1)));
				Assert.That(gain * MedievalAudio.Bank["drum-group"].Volume, Is.LessThanOrEqualTo(.65f));
			}
			Assert.That(MedievalAudio.DrumGain(3), Is.GreaterThan(MedievalAudio.DrumGain(1)));
		}
	}
}
