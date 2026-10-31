using System;
using System.Collections.Generic;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 玩家名册（服务端 `PlayerEnterRoom` 名册/进场通知 → 远程实体名牌）。
    /// **只在主线程读写**：网络线程由 `ClientNetworkManager` 入队，`Update` 里才落库。
    /// </summary>
    public sealed class PlayerNameRegistry
    {
        private readonly Dictionary<long, string> _names = new Dictionary<long, string>();

        public event Action<long, string>? OnNameChanged;

        public bool TryGetName(long playerId, out string name)
            => _names.TryGetValue(playerId, out name!);

        public string Resolve(long playerId)
            => _names.TryGetValue(playerId, out var name) ? name : "玩家 " + playerId;

        public void Set(long playerId, string name)
        {
            if (string.IsNullOrEmpty(name)) name = "玩家 " + playerId;
            if (_names.TryGetValue(playerId, out var old) && old == name) return;
            _names[playerId] = name;
            OnNameChanged?.Invoke(playerId, name);
        }

        public void Remove(long playerId) => _names.Remove(playerId);

        public void Clear() => _names.Clear();
    }
}
