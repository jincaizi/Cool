using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace KcpServer.AI.Network
{
    public class NpcSyncBroadcaster
    {
        private readonly ConcurrentDictionary<long, Action<NpcPosSync>> _posHandlers = new();
        private readonly ConcurrentDictionary<long, Action<NpcAnimSync>> _animHandlers = new();
        private readonly ConcurrentQueue<(long npcId, Vector3 pos, Quaternion rot, NpcAnimationState anim)> _pendingSync = new();

        public void RegisterSession(long sessionId, Action<NpcPosSync> onPosSync, Action<NpcAnimSync> onAnimSync)
        {
            _posHandlers[sessionId] = onPosSync;
            _animHandlers[sessionId] = onAnimSync;
        }

        public void UnregisterSession(long sessionId)
        {
            _posHandlers.TryRemove(sessionId, out _);
            _animHandlers.TryRemove(sessionId, out _);
        }

        public void QueueSync(long npcId, Vector3 pos, Quaternion rot, NpcAnimationState anim)
        {
            _pendingSync.Enqueue((npcId, pos, rot, anim));
        }

        public void Broadcast()
        {
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
