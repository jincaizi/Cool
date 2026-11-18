using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// 每实体权威状态包装（Plan 04）：应用/刷新/到期清理后同步 PlayerState 控制状态与战斗版本。
    /// 主循环线程独占（R2），无锁。
    /// </summary>
    public sealed class ServerStatusManager
    {
        private readonly PlayerState _owner;
        public StatusRuntime Runtime { get; } = new();

        public ServerStatusManager(PlayerState owner, ElementalCatalogDefinition catalog)
        {
            _owner = owner;
            Runtime.Configure(catalog.Pvp);
        }

        public StatusApplyResult Apply(StatusEffectDefinition def, long sourceId, int skillId,
            ulong castId, uint tick, ElementalCatalogDefinition catalog)
        {
            var result = Runtime.Apply(def, sourceId, skillId, castId, tick,
                _owner.Kind == (byte)EntityKind.Player, sourceIsPlayer: true);
            if (result != StatusApplyResult.Ignored)
            {
                SyncControl();
                _owner.BumpCombatVersion();
            }
            return result;
        }

        public void RemoveExpired(uint tick)
        {
            if (Runtime.RemoveExpiredModifiers(tick) > 0)
            {
                SyncControl();
                _owner.BumpCombatVersion();
            }
        }

        public void ClearAll(uint tick)
        {
            if (Runtime.Count == 0) return;
            Runtime.ClearAll();
            SyncControl();
            _owner.BumpCombatVersion();
        }

        public void SyncControl()
        {
            Runtime.DeriveControl(out float speed, out bool locked);
            if (speed != _owner.ControlSpeedScale || locked != _owner.ControlActionLock)
                _owner.ApplyControl(speed, locked);
        }
    }
}
