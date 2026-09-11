using System.Collections.Generic;
using Cool.Protocol;

namespace KcpServer
{
    /// <summary>
    /// per-observer 分级广播组装（spec §5-P3）——取代 SnapshotAssembler 的
    /// store 级 dirty/beacon 广播：每个 observer 每 tick 经 AoiGrid 取可见集、
    /// AoiBandScheduler 按距离带节拍 + 移动/静止判定选出应发实体，组装当前全字段快照。
    /// 发送由 Server3C 注入（OnEntityBroadcast）；本类纯组装可单测。
    /// 主循环独占线程，无锁。实体注销经 RemoveEntity 清理 AOI 状态。
    /// </summary>
    public sealed class AoiBroadcaster
    {
        private readonly AoiGrid _grid = new AoiGrid();
        private readonly AoiBandScheduler _scheduler = new AoiBandScheduler();
        private readonly Dictionary<long, HashSet<long>> _lastVisible =
            new Dictionary<long, HashSet<long>>();

        /// <summary>每发一个实体快照回调（Server3C 注入：写入该 observer 的 WorldSnapshot.Entities）</summary>
        public System.Action<long, EntitySnapshot>? OnEntityBroadcast { get; set; }

        /// <summary>累计发送实体数（Server3C 每 tick 增量；日志统计用——T7）</summary>
        public long TotalEntitySends;

        /// <summary>
        /// 为观察者组装本 tick 应广播的实体快照列表（AoiGrid 可见 + 带节拍/移动判定）。
        /// 每 tick 每 observer 调用一次。语义：移动实体按带频、静止实体 4 tick 保活
        /// （决策 D4——客户端缺席超时 1.2s 靠 4Hz 保活闭合）。
        /// </summary>
        public List<EntitySnapshot> BuildForObserver(PlayerState observer, uint tick, EntityStore store)
        {
            var result = new List<EntitySnapshot>();
            float ox = observer.Position.X, oz = observer.Position.Z;

            var visible = _grid.GetVisible(observer.PlayerId, store.All);

            // 视野离场实体（迟滞出界）→ 清节拍槽，重见立即发。elapsed 兜底只在离场 ≥4 tick 时
            // 成立（保活窗口内快速往返/瞬移会停流——重见重置须显式，AoiBandScheduler 测试
            // Out_Of_Sight_Clears_Schedule + 本类 Out_Of_Sight_..._Reentry_Resets 锁定语义）
            var current = new HashSet<long>();
            foreach (var e in visible) current.Add(e.PlayerId);
            if (_lastVisible.TryGetValue(observer.PlayerId, out var previous))
            {
                foreach (var id in previous)
                    if (!current.Contains(id))
                        _scheduler.MarkOutOfSight(observer.PlayerId, id);
            }
            _lastVisible[observer.PlayerId] = current;

            foreach (var entity in visible)
            {
                float dx = entity.Position.X - ox, dz = entity.Position.Z - oz;
                float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

                if (_scheduler.ShouldSend(observer.PlayerId, entity.PlayerId, distance, tick,
                    entity.Position.X, entity.Position.Z, entity.Rotation))
                {
                    var snap = new EntitySnapshot
                    {
                        PlayerId = entity.PlayerId,
                        FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed
                            | EntityFieldMask.ActionState | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                        X = entity.Position.X,
                        Y = entity.Position.Y,
                        Z = entity.Position.Z,
                        Rotation = entity.Rotation,
                        Speed = entity.Speed,
                        Kind = entity.Kind,
                        TemplateId = entity.TemplateId,
                        ActionState = entity.ActionState,
                        Tick = tick
                    };
                    result.Add(snap);
                    OnEntityBroadcast?.Invoke(entity.PlayerId, snap);
                }
            }
            return result;
        }

        /// <summary>实体注销——清理其全部 AOI 状态（视野迟滞 + 节拍槽）</summary>
        public void RemoveEntity(long entityId)
        {
            _grid.RemoveEntity(entityId);
            _scheduler.RemoveEntity(entityId);
            foreach (var seen in _lastVisible.Values)
                seen.Remove(entityId);
        }

        /// <summary>观察者会话断开——清理其 AOI 状态</summary>
        public void RemoveObserver(long observerId)
        {
            _grid.RemoveObserver(observerId);
            _scheduler.RemoveObserver(observerId);
            _lastVisible.Remove(observerId);
        }

        /// <summary>观察者当前视野内实体（P4 CastHandler 广播范围复用 AoiGrid 迟滞视野语义）</summary>
        public List<PlayerState> GetVisible(long observerId, EntityStore store)
            => _grid.GetVisible(observerId, store.All);
    }
}
