using System;
using System.Collections.Generic;
using KcpServer;
using KcpServer.AI.Core;
using KcpServer.AI.Network;

namespace KcpServer.AI
{
    /// <summary>
    /// NPC component that integrates AiManager with Room/Zone system.
    /// Provides player position data to AI for target detection.
    /// </summary>
    public class NpcComponent
    {
        private readonly AiManager _aiManager;
        private readonly NpcSyncBroadcaster _broadcaster;
        private readonly RoomManager _roomManager;

        public NpcComponent(RoomManager roomManager, string? configPath = null)
        {
            _roomManager = roomManager;
            _aiManager = new AiManager(configPath);
            _broadcaster = new NpcSyncBroadcaster();

            // Set player position provider for target detection
            _aiManager.SetPlayerPositionProvider(GetPlayersInVicinity);

            // Subscribe to AI state changes
            _aiManager.OnAiStateChanged += OnAiStateChanged;

            Console.WriteLine("NpcComponent initialized");
        }

        public void Start()
        {
            _aiManager.Start();
        }

        public void Stop()
        {
            _aiManager.Stop();
        }

        public AiManager AiManager => _aiManager;

        /// <summary>
        /// Spawn an NPC in a specific zone
        /// </summary>
        public AiComponent SpawnNpc(string zoneId, int templateId, Vector3 position, Quaternion rotation)
        {
            var ai = _aiManager.Spawn(templateId, position, rotation);

            // Queue spawn message for clients
            _broadcaster.QueueSpawn(ai.InstanceId, templateId.ToString(), position, rotation);

            return ai;
        }

        /// <summary>
        /// Despawn an NPC
        /// </summary>
        public void DespawnNpc(long npcInstanceId)
        {
            _broadcaster.QueueDespawn(npcInstanceId, "Despawn");
            _aiManager.Despawn(npcInstanceId);
        }

        /// <summary>
        /// Get all player positions in a zone for target detection
        /// </summary>
        private IEnumerable<(long playerId, Vector3 position)> GetPlayersInVicinity(long npcInstanceId)
        {
            // For now, get all players from all rooms
            // In a more sophisticated implementation, we'd track which zone the NPC is in
            var result = new List<(long, Vector3)>();

            foreach (var room in _roomManager.GetAllRooms())
            {
                foreach (var (playerId, state) in room.GetAllPlayerStates())
                {
                    result.Add((playerId, state.Position));
                }
            }

            return result;
        }

        private void OnAiStateChanged(long npcId, Vector3 pos, Quaternion rot, NpcAnimationState animState)
        {
            _broadcaster.QueueSync(npcId, pos, rot, animState);
        }

        /// <summary>
        /// Broadcast NPC updates to all sessions
        /// </summary>
        public void Broadcast()
        {
            _broadcaster.Broadcast();
        }
    }
}
