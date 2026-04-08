using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace KcpServer.AI.Network
{
    public class NpcSyncBroadcaster
    {
        private readonly ConcurrentDictionary<long, Action<NpcPosSync>> _posHandlers = new();
        private readonly ConcurrentDictionary<long, Action<NpcAnimSync>> _animHandlers = new();
        private readonly ConcurrentDictionary<long, Action<NpcSpawn>> _spawnHandlers = new();
        private readonly ConcurrentDictionary<long, Action<NpcDespawn>> _despawnHandlers = new();

        private readonly ConcurrentQueue<(long npcId, Vector3 pos, Quaternion rot, NpcAnimationState anim)> _pendingSync = new();
        private readonly ConcurrentQueue<(long npcId, string templateId, Vector3 pos, Quaternion rot)> _pendingSpawn = new();
        private readonly ConcurrentQueue<(long npcId, string reason)> _pendingDespawn = new();

        public void RegisterSession(long sessionId,
            Action<NpcPosSync>? onPosSync = null,
            Action<NpcAnimSync>? onAnimSync = null,
            Action<NpcSpawn>? onSpawn = null,
            Action<NpcDespawn>? onDespawn = null)
        {
            if (onPosSync != null) _posHandlers[sessionId] = onPosSync;
            if (onAnimSync != null) _animHandlers[sessionId] = onAnimSync;
            if (onSpawn != null) _spawnHandlers[sessionId] = onSpawn;
            if (onDespawn != null) _despawnHandlers[sessionId] = onDespawn;
        }

        public void UnregisterSession(long sessionId)
        {
            _posHandlers.TryRemove(sessionId, out _);
            _animHandlers.TryRemove(sessionId, out _);
            _spawnHandlers.TryRemove(sessionId, out _);
            _despawnHandlers.TryRemove(sessionId, out _);
        }

        public void QueueSync(long npcId, Vector3 pos, Quaternion rot, NpcAnimationState anim)
        {
            _pendingSync.Enqueue((npcId, pos, rot, anim));
        }

        public void QueueSpawn(long npcId, string templateId, Vector3 pos, Quaternion rot)
        {
            _pendingSpawn.Enqueue((npcId, templateId, pos, rot));
        }

        public void QueueDespawn(long npcId, string reason)
        {
            _pendingDespawn.Enqueue((npcId, reason));
        }

        public void Broadcast()
        {
            // Broadcast spawns
            while (_pendingSpawn.TryDequeue(out var spawn))
            {
                var msg = new NpcSpawn
                {
                    NpcId = spawn.npcId,
                    NpcTemplateId = spawn.templateId,
                    X = spawn.pos.X,
                    Y = spawn.pos.Y,
                    Z = spawn.pos.Z,
                    Rotation = GetYaw(spawn.rot),
                    ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                foreach (var handler in _spawnHandlers.Values)
                {
                    try { handler(msg); } catch { }
                }
            }

            // Broadcast despawns
            while (_pendingDespawn.TryDequeue(out var despawn))
            {
                var msg = new NpcDespawn
                {
                    NpcId = despawn.npcId,
                    Reason = despawn.reason,
                    ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                foreach (var handler in _despawnHandlers.Values)
                {
                    try { handler(msg); } catch { }
                }
            }

            // Broadcast syncs
            while (_pendingSync.TryDequeue(out var sync))
            {
                var posMsg = new NpcPosSync
                {
                    NpcId = sync.npcId,
                    X = sync.pos.X,
                    Y = sync.pos.Y,
                    Z = sync.pos.Z,
                    Rotation = GetYaw(sync.rot),
                    Speed = 0,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Sequence = 0
                };

                var animMsg = new NpcAnimSync
                {
                    NpcId = sync.npcId,
                    State = sync.anim,
                    StateHash = 0,
                    TransitionDuration = 0,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                foreach (var handler in _posHandlers.Values)
                {
                    try { handler(posMsg); } catch { }
                }

                foreach (var handler in _animHandlers.Values)
                {
                    try { handler(animMsg); } catch { }
                }
            }
        }

        private static float GetYaw(Quaternion q)
        {
            return (float)(System.Math.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180.0 / System.Math.PI);
        }
    }
}
