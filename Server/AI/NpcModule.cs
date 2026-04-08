using System;
using System.Threading;
using KcpServer;
using KcpServer.AI.Core;
using KcpServer.AI.Network;

namespace KcpServer.AI
{
    /// <summary>
    /// NPC AI module that integrates with the game server.
    /// Manages AI NPCs, target detection, and synchronization.
    /// </summary>
    public class NpcModule : IDisposable
    {
        private readonly NpcComponent _npcComponent;
        private readonly Thread _broadcastThread;
        private bool _running;
        private bool _disposed;

        public NpcModule(RoomManager roomManager, string? configPath = null)
        {
            _npcComponent = new NpcComponent(roomManager, configPath);
            _broadcastThread = new Thread(BroadcastLoop);
        }

        public AiManager AiManager => _npcComponent.AiManager;

        /// <summary>
        /// Register a session for NPC sync messages
        /// </summary>
        public void RegisterSession(long sessionId,
            Action<NpcPosSync>? onPosSync = null,
            Action<NpcAnimSync>? onAnimSync = null,
            Action<NpcSpawn>? onSpawn = null,
            Action<NpcDespawn>? onDespawn = null)
        {
            // Get the broadcaster from NpcComponent
            // For now, we need to expose it or use events
        }

        public void Start()
        {
            _running = true;
            _npcComponent.Start();
            _broadcastThread.Start();
            Console.WriteLine("NpcModule started");
        }

        public void Stop()
        {
            _running = false;
            _npcComponent.Stop();
            _broadcastThread.Join();
            Console.WriteLine("NpcModule stopped");
        }

        private void BroadcastLoop()
        {
            while (_running)
            {
                try
                {
                    _npcComponent.Broadcast();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Broadcast error: {e.Message}");
                }
                Thread.Sleep(100); // 10Hz broadcast
            }
        }

        /// <summary>
        /// Spawn an NPC at a position
        /// </summary>
        public long SpawnNpc(int templateId, Vector3 position, Quaternion rotation)
        {
            var ai = _npcComponent.SpawnNpc("default", templateId, position, rotation);
            return ai.InstanceId;
        }

        /// <summary>
        /// Spawn an NPC in a specific zone
        /// </summary>
        public long SpawnNpc(string zoneId, int templateId, Vector3 position, Quaternion rotation)
        {
            var ai = _npcComponent.SpawnNpc(zoneId, templateId, position, rotation);
            return ai.InstanceId;
        }

        /// <summary>
        /// Despawn an NPC
        /// </summary>
        public void DespawnNpc(long npcInstanceId)
        {
            _npcComponent.DespawnNpc(npcInstanceId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
