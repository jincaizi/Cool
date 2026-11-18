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
        private System.Action<float, bool>? _controlSink;
        public StatusRuntime Runtime { get; } = new();

        public ServerStatusManager(PlayerState owner, ElementalCatalogDefinition catalog,
            System.Action<float, bool>? controlSink = null)
        {
            _owner = owner;
            _controlSink = controlSink;
            Runtime.Configure(catalog.Pvp);
        }

        /// <summary>补装/替换控制 sink。延迟配置（<see cref="PlayerState.ConfigureStatuses"/> 的
        /// first-configure-wins 语义）的实体在宿主接线后靠这里接上唯一控制入口。</summary>
        public void SetControlSink(System.Action<float, bool>? controlSink) => _controlSink = controlSink;

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

        /// <summary>DoT 推进下一跳；末跳/到期被移除时同步投影版本（客户端据此撤状态表现）。</summary>
        public void AdvanceDot(ActiveStatus status, uint tick)
        {
            bool removed = status.PendingRemoval || tick >= status.EndTick;
            Runtime.AdvanceDot(status, tick);
            if (!removed) return;
            SyncControl();
            _owner.BumpCombatVersion();
        }

        /// <summary>DoT 到期移除（EndTick 无排定跳：刷新错相/间隔不整除）；删除也须 bump 版本，
        /// 否则无 dirty 的实体不发 CombatStateUpdate，客户端旧状态表现会滞留。</summary>
        public void RemoveExpiredDots(uint tick)
        {
            if (Runtime.RemoveExpiredDots(tick) > 0)
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
            if (speed == _owner.ControlSpeedScale && locked == _owner.ControlActionLock) return;
            // 有 sink 时走宿主唯一入口（Server3C.ApplyPlayerControl：首次 ActionLock 取消未提交施法、
            // 死亡拒绝控制写入）；无 sink（怪物/测试）退回直写。
            if (_controlSink != null) _controlSink(speed, locked);
            else _owner.ApplyControl(speed, locked);
        }
    }
}
