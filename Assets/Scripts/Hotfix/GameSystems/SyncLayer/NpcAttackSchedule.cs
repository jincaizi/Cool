using System.Collections.Generic;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 怪物攻击的**呈现排程**（spec §6.2）。每个客户端都收到**同一个** CastBroadcast 对象
    /// （服务端构造一次、发给全部观察者），所以「第几刀用哪段攻击」必须由时间戳推出，
    /// 而不是客户端自增计数 —— 在 AOI 边缘进出过的客户端计数器会错位，同一刀播不同动画。
    ///
    /// 排程项在渲染时钟越过它时触发，与位置插值共用同一根服务器时间轴。
    /// </summary>
    public sealed class NpcAttackSchedule
    {
        /// <summary>变体分桶：≈1/15 秒（.NET ticks 是 100ns）。**客户端共享常量**，
        /// 不随登录返回的 ServerTickRateMs 变 —— 一致性只要求所有客户端用同一个值。</summary>
        public const long VariantBucketTicks = 666_667L;

        public readonly struct DueAttack
        {
            public readonly long EntityId;
            public readonly int Variant;
            public readonly long ServerTicks;
            public DueAttack(long entityId, int variant, long serverTicks)
            {
                EntityId = entityId;
                Variant = variant;
                ServerTicks = serverTicks;
            }
        }

        private readonly List<(long EntityId, long ServerTicks)> _pending = new List<(long, long)>();

        /// <summary>服务器裁定的第几段攻击（0 → Attack01，1 → Attack02）</summary>
        public static int VariantOf(long serverTicks) => (int)((serverTicks / VariantBucketTicks) % 2);

        public void Schedule(long entityId, long serverTicks) => _pending.Add((entityId, serverTicks));

        /// <summary>取出所有到点的项（timestamp &lt;= renderAt）并出队。迟到的立即触发，不丢弃。</summary>
        public void Drain(long renderAt, List<DueAttack> into)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].ServerTicks > renderAt) continue;
                into.Add(new DueAttack(_pending[i].EntityId, VariantOf(_pending[i].ServerTicks), _pending[i].ServerTicks));
                _pending.RemoveAt(i);
            }
            // 同帧多项时按时间戳升序，保证变体顺序稳定
            into.Sort((a, b) => a.ServerTicks.CompareTo(b.ServerTicks));
        }

        /// <summary>视图销毁时清掉它的待播项（避免播给已不存在的实体）</summary>
        public void Clear(long entityId) => _pending.RemoveAll(p => p.EntityId == entityId);

        public void ClearAll() => _pending.Clear();
    }
}
