using System.Collections.Generic;

namespace KcpServer
{
    /// <summary>
    /// 实体存储 —— 世界内玩家权威状态的唯一持有者。
    /// 主循环单线程独占访问（R2）：非并发字典。
    /// </summary>
    public sealed class EntityStore
    {
        private readonly Dictionary<long, PlayerState> _entities = new Dictionary<long, PlayerState>();

        public int Count => _entities.Count;
        public IReadOnlyCollection<PlayerState> All => _entities.Values;

        public PlayerState AddPlayer(long playerId, string playerName)
        {
            var state = new PlayerState(playerId) { PlayerName = playerName };
            _entities[playerId] = state;
            return state;
        }

        public bool RemovePlayer(long playerId) => _entities.Remove(playerId);

        public PlayerState? GetPlayer(long playerId)
            => _entities.TryGetValue(playerId, out var state) ? state : null;

        public void MarkDirty(PlayerState state) => state.IsDirty = true;
    }
}
