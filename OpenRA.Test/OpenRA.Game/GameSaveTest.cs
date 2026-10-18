using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class GameSaveTest
	{
		string directory;
		Session lobby;
		[SetUp]
		public void SetUp()
		{
			directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "game-save-" + Guid.NewGuid());
			Directory.CreateDirectory(directory);
			lobby = new Session { GlobalSettings = new Session.Global { MultiplayerGameSave = true } };
			lobby.Slots.Add("A", new Session.Slot { PlayerReference = "A" });
			lobby.Slots.Add("B", new Session.Slot { PlayerReference = "B" });
			lobby.Clients.Add(new Session.Client { Index = 4, Slot = "A", Name = "Host", IsAdmin = true, GameSaveOrder = 0 });
			lobby.Clients.Add(new Session.Client { Index = 9, Slot = "B", Name = "Guest", GameSaveOrder = 1 });
		}

		[TearDown]
		public void TearDown() => Directory.Delete(directory, true);

		GameSave EmptySave()
		{
			var path = Path.Combine(directory, "empty.orasav");
			using (var file = File.Create(path))
			{
				file.Write(GameSave.MetadataMarker);
				file.Write(0);
				file.Write(0);
				file.Write(new byte[Order.SyncHashOrderLength]);
				file.WriteLengthPrefixedString(Encoding.UTF8, new List<MiniYamlNode> { lobby.GlobalSettings.Serialize() }.WriteToString());
				file.WriteLengthPrefixedString(Encoding.UTF8, lobby.Slots.Values.Select(s => s.Serialize()).ToList().WriteToString());
				file.WriteLengthPrefixedString(Encoding.UTF8, lobby.NonBotPlayers.Select(c => new SlotClient(c).Serialize(c.Slot)).ToList().WriteToString());
				file.WriteLengthPrefixedString(Encoding.UTF8, "");
				var traitOffset = (int)file.Position;
				file.Write(GameSave.TraitDataMarker);
				file.WriteLengthPrefixedString(Encoding.UTF8, "");
				file.Write(0);
				file.Write(traitOffset);
				file.Write(GameSave.EOFMarker);
			}
			var save = new GameSave(path);
			save.StartGame(lobby, MapCache.UnknownMap);
			return save;
		}

		static byte[] SyncPacket()
		{
			var data = new byte[Order.SyncHashOrderLength];
			data[0] = (byte)OrderType.SyncHash;
			return data;
		}

		[Test]
		public void SavesEveryClientPacketAndExcludesIncompleteFutureFrames()
		{
			var save = EmptySave();
			save.RecordOrders(4, 1, [11]);
			save.RecordOrders(4, 2, [12]);
			save.RecordOrders(4, 1, SyncPacket());
			Assert.That(save.CanSave, Is.False);
			save.RecordOrders(9, 1, [21]);
			Assert.That(save.CanSave, Is.True);
			var path = Path.Combine(directory, "snapshot.orasav");
			save.Save(path);
			var restored = new GameSave(path);
			lobby.Clients[0].Index = 7;
			lobby.Clients[1].Index = 2;
			lobby.Clients.Reverse();
			restored.StartGame(lobby, MapCache.UnknownMap);
			var packets = new List<(int Frame, int Client, byte Data)>();
			restored.ParseOrders(lobby, (f, c, d) => { if (d.Length == 1) packets.Add((f, c, d[0])); });
			Assert.That(restored.LastOrdersFrame, Is.EqualTo(1));
			Assert.That(packets, Is.EqualTo(new[] { (1, 7, (byte)11), (1, 2, (byte)21) }));
			Assert.That(restored.SlotClients["A"].OrderIndex, Is.EqualTo(0));
			Assert.That(File.Exists(path + ".tmp"), Is.False);
		}

		[Test]
		public void DisconnectCannotProduceATruncatedSaveOrOverwriteAnExistingOne()
		{
			var save = EmptySave();
			save.RecordOrders(4, 1, []);
			save.RecordOrders(9, 1, []);
			save.RecordOrders(4, 1, SyncPacket());
			var path = Path.Combine(directory, "safe.orasav");
			save.Save(path);
			var original = File.ReadAllBytes(path);
			save.ClientDisconnected(9);
			Assert.That(save.CanSave, Is.False);
			Assert.Throws<InvalidOperationException>(() => save.Save(path));
			Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
		}

		[Test]
		public void SavedNationDoesNotTransferAdministratorPrivileges()
		{
			var saved = new SlotClient(lobby.Clients[0]);
			saved.ApplyTo(lobby.Clients[1]);
			Assert.That(lobby.Clients[1].IsAdmin, Is.False);
			Assert.That(lobby.Clients[1].Name, Is.EqualTo("Host"));
		}

		[Test]
		public void RejectsTruncatedFilesAndInvalidMetadataOffsets()
		{
			var path = Path.Combine(directory, "broken.orasav");
			File.WriteAllBytes(path, [1, 2, 3]);
			Assert.Throws<InvalidDataException>(() => new GameSave(path));
			using (var file = File.Create(path))
			{
				file.Write(int.MaxValue);
				file.Write(int.MaxValue);
				file.Write(GameSave.EOFMarker);
			}
			Assert.Throws<InvalidDataException>(() => new GameSave(path));
		}

		[Test]
		public void RestoresObservingHostAndAllowsANewSpectator()
		{
			lobby.Clients[0].Slot = null;
			var save = EmptySave();
			save.RecordOrders(4, 1, [11]);
			save.RecordOrders(9, 1, [21]);
			save.RecordOrders(4, 1, SyncPacket());
			var path = Path.Combine(directory, "observer.orasav");
			save.Save(path);
			var restored = new GameSave(path);
			lobby.Clients[0].Index = 17;
			lobby.Clients[1].Index = 3;
			lobby.Clients.Add(new Session.Client { Index = 25, Name = "New observer" });
			restored.StartGame(lobby, MapCache.UnknownMap);
			var packets = new List<(int Frame, int Client, int Length)>();
			restored.ParseOrders(lobby, (f, c, d) => { if (d.Length != Order.SyncHashOrderLength) packets.Add((f, c, d.Length)); });
			Assert.That(packets, Is.EqualTo(new[] { (1, 17, 1), (1, 3, 1), (1, 25, 0) }));
		}
	}
}
