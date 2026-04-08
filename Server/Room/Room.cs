using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace KcpServer
{
    /// <summary>
    /// 房间状态
    /// </summary>
    public sealed class Room
    {
        public string RoomId { get; }
        public int PlayerCount => _players.Count;
        public DateTime CreatedAt { get; }

        private readonly ConcurrentDictionary<long, PlayerState> _players = new ConcurrentDictionary<long, PlayerState>();
        private readonly ConcurrentDictionary<long, KcpServerSession> _sessions = new ConcurrentDictionary<long, KcpServerSession>();

        public Room(string roomId)
        {
            RoomId = roomId;
            CreatedAt = DateTime.UtcNow;
        }

        public bool AddPlayer(long playerId, KcpServerSession session, PlayerState state)
        {
            if (!_players.TryAdd(playerId, state))
                return false;
            if (!_sessions.TryAdd(playerId, session))
            {
                _players.TryRemove(playerId, out _);
                return false;
            }
            return true;
        }

        public bool RemovePlayer(long playerId)
        {
            _players.TryRemove(playerId, out _);
            _sessions.TryRemove(playerId, out _);
            return true;
        }

        public bool ContainsPlayer(long playerId) => _players.ContainsKey(playerId);

        public PlayerState? GetPlayerState(long playerId)
        {
            return _players.TryGetValue(playerId, out var state) ? state : null;
        }

        public KcpServerSession? GetPlayerSession(long playerId)
        {
            return _sessions.TryGetValue(playerId, out var session) ? session : null;
        }

        public IReadOnlyDictionary<long, PlayerState> GetAllPlayerStates() => _players;

        public IReadOnlyList<KcpServerSession> GetAllPlayerSessions() => _sessions.Values.ToList();

        public IReadOnlyList<KcpServerSession> GetOtherPlayerSessions(long excludePlayerId)
        {
            return _sessions.Values.Where(s => s.SessionId != excludePlayerId).ToList();
        }

        public IReadOnlyDictionary<long, PlayerState> GetOtherPlayerStates(long excludePlayerId)
        {
            return _players.Where(kv => kv.Key != excludePlayerId)
                          .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public bool UpdatePlayerPosition(long playerId, Vector3 position, float rotation, float speed, long timestamp, uint sequence)
        {
            if (!_players.TryGetValue(playerId, out var state))
                return false;

            state.UpdatePosition(position, rotation, speed, timestamp, sequence);
            return true;
        }

        public List<RemotePlayerSync> GetAllRemotePlayerSync()
        {
            var result = new List<RemotePlayerSync>();
            foreach (var kvp in _players)
            {
                var state = kvp.Value;
                result.Add(new RemotePlayerSync
                {
                    PlayerId = state.PlayerId,
                    X = state.Position.X,
                    Y = state.Position.Y,
                    Z = state.Position.Z,
                    Rotation = state.Rotation,
                    Speed = state.Speed,
                    Timestamp = state.LastUpdateTimestamp,
                    Sequence = state.LastReceivedSequence
                });
            }
            return result;
        }
    }
}
