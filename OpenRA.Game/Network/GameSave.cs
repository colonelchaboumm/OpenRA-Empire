#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenRA.Primitives;
using OpenRA.Server;

namespace OpenRA.Network
{
	public class SlotClient
	{
		public readonly Color Color;
		public readonly string Faction;
		public readonly int SpawnPoint;
		public readonly int Team;
		public readonly int Handicap;
		public readonly string Slot;
		public readonly string Bot;
		public readonly bool IsAdmin;
		public readonly string Name;
		public readonly int OrderIndex = int.MaxValue;

		public readonly string BotName;

		public SlotClient() { }

		public SlotClient(Session.Client client)
		{
			Color = client.Color;
			Faction = client.Faction;
			SpawnPoint = client.SpawnPoint;
			Team = client.Team;
			Handicap = client.Handicap;
			Slot = client.Slot;
			Bot = client.Bot;
			IsAdmin = client.IsAdmin;
			Name = client.Name;
			OrderIndex = client.GameSaveOrder;

			if (client.Bot != null)
				BotName = client.Name;
		}

		public void ApplyTo(Session.Client client)
		{
			client.Color = Color;
			client.Faction = Faction;
			client.SpawnPoint = SpawnPoint;
			client.Team = Team;
			client.Handicap = Handicap;
			client.Slot = Slot;
			client.Bot = Bot;
			client.GameSaveOrder = OrderIndex;
			if (!string.IsNullOrEmpty(Name))
				client.Name = Name;

			if (Bot != null)
				client.Name = BotName;
		}

		public static SlotClient Deserialize(MiniYaml data)
		{
			return FieldLoader.Load<SlotClient>(data);
		}

		public MiniYamlNode Serialize(string key)
		{
			return new MiniYamlNode($"SlotClient@{key}", FieldSaver.Save(this));
		}
	}

	public class GameSave
	{
		public const int EOFMarker = -2;
		public const int MetadataMarker = -1;
		public const int TraitDataMarker = -3;

		readonly MemoryStream ordersStream = new();

		// Loaded from file and updated during gameplay
		public int LastOrdersFrame { get; private set; }
		public int LastSyncFrame { get; private set; }
		byte[] lastSyncPacket = new byte[Order.SyncHashOrderLength];

		// Loaded from file or set on game start
		public Session.Global GlobalSettings { get; private set; }
		public Dictionary<string, Session.Slot> Slots { get; private set; }
		public Dictionary<string, SlotClient> SlotClients { get; private set; }
		public Dictionary<int, MiniYaml> TraitData = [];
		public MapGenerationArgs MapGenerationArgs;

		// Set on game start
		int[] clientsBySlotIndex = [];
		int hostClientIndex;
		readonly Dictionary<int, int> framesByClient = [];
		public bool IsMultiplayer => GlobalSettings.MultiplayerGameSave || SlotClients.Values.Count(c => c.Bot == null) > 1;
		public bool CanSave => LastSyncFrame > 0 && LastOrdersFrame >= LastSyncFrame && !PlayerDisconnected;
		public bool PlayerDisconnected { get; private set; }

		public void ClientDisconnected(int clientIndex)
		{
			if (framesByClient.ContainsKey(clientIndex))
				PlayerDisconnected = true;
		}

		public GameSave()
		{
			LastOrdersFrame = -1;
			Slots = [];
		}

		public GameSave(string filepath)
		{
			using (var rs = File.OpenRead(filepath))
			{
				if (rs.Length < 12)
					throw new InvalidDataException("Truncated orasav file");
				rs.Seek(-12, SeekOrigin.End);
				var metadataOffset = rs.ReadInt32();
				var traitDataOffset = rs.ReadInt32();
				if (rs.ReadInt32() != EOFMarker || metadataOffset < 0 || traitDataOffset <= metadataOffset || traitDataOffset >= rs.Length - 12)
					throw new InvalidDataException("Invalid orasav file");

				rs.Seek(metadataOffset, SeekOrigin.Begin);
				if (rs.ReadInt32() != MetadataMarker)
					throw new InvalidDataException("Invalid orasav file");

				LastOrdersFrame = rs.ReadInt32();
				LastSyncFrame = rs.ReadInt32();
				if (LastOrdersFrame < 0 || LastSyncFrame < 0 || LastSyncFrame > LastOrdersFrame)
					throw new InvalidDataException("Invalid saved frame range");
				lastSyncPacket = rs.ReadBytes(Order.SyncHashOrderLength);

				var globalSettings = MiniYaml.FromString(rs.ReadLengthPrefixedString(Encoding.UTF8, Connection.MaxOrderLength), $"{filepath}:globalSettings");
				GlobalSettings = Session.Global.Deserialize(globalSettings.First().Value);

				var slots = MiniYaml.FromString(rs.ReadLengthPrefixedString(Encoding.UTF8, Connection.MaxOrderLength), $"{filepath}:slots");
				Slots = [];
				foreach (var s in slots)
				{
					var slot = Session.Slot.Deserialize(s.Value);
					Slots.Add(slot.PlayerReference, slot);
				}

				var slotClients = MiniYaml.FromString(rs.ReadLengthPrefixedString(Encoding.UTF8, Connection.MaxOrderLength), $"{filepath}:slotClients");
				SlotClients = [];
				foreach (var s in slotClients)
				{
					var slotClient = SlotClient.Deserialize(s.Value);
					SlotClients.Add(slotClient.Slot, slotClient);
				}

				var mapGenerationArgs = rs.ReadLengthPrefixedString(Encoding.UTF8, Connection.MaxOrderLength);
				if (!string.IsNullOrEmpty(mapGenerationArgs))
					MapGenerationArgs = FieldLoader.Load<MapGenerationArgs>(new MiniYaml("", MiniYaml.FromString(mapGenerationArgs, $"{filepath}:mapGenerationArgs")));

				if (rs.Position != traitDataOffset || rs.ReadInt32() != TraitDataMarker)
					throw new InvalidDataException("Invalid orasav file");

				var traitData = MiniYaml.FromString(rs.ReadLengthPrefixedString(Encoding.UTF8, Connection.MaxOrderLength), $"{filepath}:traitData");
				foreach (var td in traitData)
					TraitData.Add(Exts.ParseInt32Invariant(td.Key), td.Value);

				rs.Seek(0, SeekOrigin.Begin);
				ordersStream.Write(rs.ReadBytes(metadataOffset), 0, metadataOffset);
			}
		}

		public void StartGame(Session lobbyInfo, MapPreview map)
		{
			if (map.Class == MapClassification.Generated)
				MapGenerationArgs = map.GenerationArgs;

			// Persist nation slots instead of transient connection identifiers.
			clientsBySlotIndex = lobbyInfo.Slots.Keys.Select(s =>
			{
				var client = lobbyInfo.ClientInSlot(s);
				return client != null ? client.Index : -1;
			}).ToArray();
			hostClientIndex = lobbyInfo.NonBotClients.First(c => c.IsAdmin).Index;
			framesByClient.Clear();
			foreach (var c in lobbyInfo.NonBotClients.Where(c => !c.IsObserver || c.IsAdmin))
				framesByClient.Add(c.Index, Math.Max(0, LastOrdersFrame));

			// Keep the original lobby and player order when saving a resumed game again.
			if (GlobalSettings != null)
				return;

			// Perform a deep clone by round-tripping the data
			GlobalSettings = Session.Global.Deserialize(lobbyInfo.GlobalSettings.Serialize().Value);
			GlobalSettings.GameSaveHostOrder = lobbyInfo.ClientWithIndex(hostClientIndex).GameSaveOrder;
			GlobalSettings.GameSaveDisabledSpawns = lobbyInfo.DisabledSpawnPoints.ToArray();
			Slots = [];
			SlotClients = [];
			foreach (var s in lobbyInfo.Slots)
			{
				Slots[s.Key] = Session.Slot.Deserialize(s.Value.Serialize().Value);

				var playerReference = map.Players.Players[s.Value.PlayerReference];
				var client = lobbyInfo.ClientInSlot(s.Key);

				// Only save the client state relevant to the game (faction, team, etc).
				// Admin and bot controller state is inherited and/or reassigned by the server at load time
				if (playerReference.Playable && client != null)
					SlotClients[s.Key] = new SlotClient(client);
			}
		}

		public void DispatchOrders(Connection conn, int frame, byte[] data)
			=> RecordOrders(conn.PlayerIndex, frame, data);

		public void RecordOrders(int clientIndex, int frame, byte[] data)
		{
			// Sync packets must never occupy an orders frame. Use the saving host's hash.
			if (data.Length > 0 && data[0] == (byte)OrderType.SyncHash)
			{
				if (data.Length != Order.SyncHashOrderLength)
				{
					Log.Write("debug", $"Dropped sync order with length {data.Length}. Expected length {Order.SyncHashOrderLength}.");
					return;
				}

				if (clientIndex == hostClientIndex && frame > LastSyncFrame)
				{
					LastSyncFrame = frame;
					lastSyncPacket = data;
				}
				return;
			}

			if (!framesByClient.TryGetValue(clientIndex, out var previousFrame) || frame <= previousFrame)
				return;

			// Ignore immediate orders
			if (data.Length > 0 && data[0] == 0xFE)
				return;

			var clientSlot = clientsBySlotIndex.IndexOf(clientIndex);

			// Handle orders that were sent by spectators
			if (clientSlot == -1)
			{
				// Only the host may issue simulation orders while observing.
				clientSlot = clientsBySlotIndex.Length;
			}

			// A failed disk write may have left the stream cursor inside the history.
			ordersStream.Seek(0, SeekOrigin.End);
			ordersStream.Write(data.Length + 8);
			ordersStream.Write(frame);
			ordersStream.Write(clientSlot);
			ordersStream.Write(data);
			framesByClient[clientIndex] = frame;
			LastOrdersFrame = framesByClient.Values.Min();
		}

		public void ParseOrders(Session lobbyInfo, Action<int, int, byte[]> packetFn)
		{
			// Send the trait data first to guarantee that it is available when needed
			foreach (var kv in TraitData)
			{
				var data = new List<MiniYamlNode>() { new(kv.Key.ToStringInvariant(), kv.Value) }.WriteToString();
				packetFn(0, 0, Order.FromTargetString("SaveTraitData", data, true).Serialize());
			}

			ordersStream.Seek(0, SeekOrigin.Begin);
			while (ordersStream.Position < ordersStream.Length)
			{
				var dataLength = ordersStream.ReadInt32() - 8;
				var frame = ordersStream.ReadInt32();
				var slot = ordersStream.ReadInt32();
				var data = ordersStream.ReadBytes(dataLength);

				// Remap bot orders to their controller client
				var clientIndex = slot == clientsBySlotIndex.Length ? hostClientIndex : clientsBySlotIndex[slot];
				var client = lobbyInfo.ClientWithIndex(clientIndex);
				if (client.Bot != null)
					clientIndex = client.BotControllerClientIndex;

				packetFn(frame, clientIndex, data);
			}

			// Observers do not contribute simulation orders, but each connected peer needs
			// an empty packet for every restored frame before normal networking can resume.
			foreach (var observer in lobbyInfo.NonBotClients.Where(c => c.IsObserver && !c.IsAdmin))
				for (var frame = 1; frame <= LastOrdersFrame; frame++)
					packetFn(frame, observer.Index, []);

			// Send sync hash to validate restore
			packetFn(LastSyncFrame, 0, lastSyncPacket);
		}

		public void AddTraitData(int traitIndex, MiniYaml data)
		{
			TraitData[traitIndex] = data;
		}

		public void Save(string path)
		{
			if (!CanSave)
				throw new InvalidOperationException("A complete synchronized frame is required to save the game.");

			// Pending future packets can be incomplete across peers. Only restore through
			// the last frame that the host actually simulated and supplied a hash for.
			var saveFrame = IsMultiplayer ? LastSyncFrame : LastOrdersFrame;
			var temporaryPath = path + ".tmp";
			// File format:
			// - List of orders in network frame format
			// - Metadata start marker
			//   - Last frame number containing orders (int32)
			//   - Last frame number containing sync hash (int32)
			//   - Last sync packet (13 x byte)
			//   - Lobby global settings (yaml)
			//   - Lobby slots (yaml)
			//   - Lobby slot-client data (yaml)
			// - Trait data start marker
			//   - Custom trait yaml
			// - File offset of metadata start marker
			// - File offset of custom trait data
			// - Metadata end marker
			using (var file = File.Create(temporaryPath))
			{
				ordersStream.Seek(0, SeekOrigin.Begin);
				while (ordersStream.Position < ordersStream.Length)
				{
					var length = ordersStream.ReadInt32();
					var frame = ordersStream.ReadInt32();
					var packet = ordersStream.ReadBytes(length - 4);
					if (frame > saveFrame)
						continue;
					file.Write(length);
					file.Write(frame);
					file.Write(packet);
				}
				var metadataOffset = file.Position;
				file.Write(MetadataMarker);
				file.Write(saveFrame);
				file.Write(LastSyncFrame);
				file.Write(lastSyncPacket, 0, Order.SyncHashOrderLength);

				var globalSettingsNodes = new List<MiniYamlNode>() { GlobalSettings.Serialize() };
				file.WriteLengthPrefixedString(Encoding.UTF8, globalSettingsNodes.WriteToString());

				var slotNodes = Slots
					.Select(s => s.Value.Serialize())
					.ToList();
				file.WriteLengthPrefixedString(Encoding.UTF8, slotNodes.WriteToString());

				var slotClientNodes = SlotClients
					.Select(s => s.Value.Serialize(s.Key))
					.ToList();
				file.WriteLengthPrefixedString(Encoding.UTF8, slotClientNodes.WriteToString());

				file.WriteLengthPrefixedString(Encoding.UTF8, MapGenerationArgs?.Serialize().WriteToString() ?? string.Empty);

				var traitDataOffset = file.Length;
				file.Write(TraitDataMarker);

				var traitDataNodes = TraitData
					.Select(kv => new MiniYamlNode(kv.Key.ToStringInvariant(), kv.Value))
					.ToList();
				file.WriteLengthPrefixedString(Encoding.UTF8, traitDataNodes.WriteToString());

				file.Write((int)metadataOffset);
				file.Write((int)traitDataOffset);
				file.Write(EOFMarker);
				file.Flush(true);
			}
			File.Move(temporaryPath, path, true);
		}
	}
}
