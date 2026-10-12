using System.Collections.Generic;

namespace KcpServer
{
    /// <summary>
    /// 距离带分级节拍（spec §5-P3：近 15Hz/中 8Hz/远 4Hz）——
    /// 移动实体（坐标/旋转自上次发送变化）按带频广播；静止实体统一 4 tick 保活
    /// （近带静止 15Hz 全字段是带宽回归——终审 I5 修正；4 tick ≈ 267ms ≪ 客户端 1.2s 缺席超时）。
    /// 每个 (observer, entity) 记录最近发送 tick、距离带与上次坐标。
    /// 带变化/重见即重置（立即发一帧）；**状态翻转（actionState/dead）立即发**，
    /// 否则静止怪物的翻转只能等 4 tick 保活，而保活相位按 (observer, entity) 各记一份，
    /// 同一翻转会落在不同观察者的不同 tick 上（客户端动画跨端不一致）。主循环独占线程，无锁。
    /// tick 差值用相减（uint wrap 安全：15Hz 下 ~9.1 年才 wrap）。
    /// </summary>
    public sealed class AoiBandScheduler
    {
        public const float MidBandStart = 30f;
        public const float FarBandStart = 60f;
        public const uint StaticKeepaliveInterval = 4; // 静止实体统一 4 tick 保活

        private struct Slot
        {
            public uint LastSentTick;
            public int Band; // 0=近 1=中 2=远
            public float X;
            public float Z;
            public float RotY;
            public byte ActionState;   // 2026-10-12：状态翻转也是变化信号
            public bool Dead;
            public bool HasSent;
        }

        private readonly Dictionary<long, Dictionary<long, Slot>> _state =
            new Dictionary<long, Dictionary<long, Slot>>();

        /// <summary>distance 米、tick 当前服务器 tick、x/z/rotY 实体当前坐标、
        /// actionState/dead 实体当前动作状态（翻转立即发）。
        /// 返回 true = 本 tick 应给该观察者发该实体</summary>
        public bool ShouldSend(long observerId, long entityId, float distance, uint tick,
            float x, float z, float rotY, byte actionState, bool dead)
        {
            int band = BandOf(distance);
            uint interval = IntervalOf(band);

            if (!_state.TryGetValue(observerId, out var entities))
            {
                entities = new Dictionary<long, Slot>();
                _state[observerId] = entities;
            }

            if (!entities.TryGetValue(entityId, out var slot) || !slot.HasSent)
            {
                entities[entityId] = new Slot { LastSentTick = tick, Band = band, X = x, Z = z, RotY = rotY, ActionState = actionState, Dead = dead, HasSent = true };
                return true; // 首帧（或带变化/重见后）立即发
            }

            if (slot.Band != band)
            {
                entities[entityId] = new Slot { LastSentTick = tick, Band = band, X = x, Z = z, RotY = rotY, ActionState = actionState, Dead = dead, HasSent = true };
                return true; // 带变化：重置节拍
            }

            // 状态翻转 → 立即发（跨客户端动画一致性依赖此语义：静止怪物的状态翻转若等保活，
            // 不同观察者的保活相位不同，同一翻转会落在不同 tick，客户端最多差 4 tick ≈267ms）。
            // 注意**不重置 LastSentTick** —— 重置会把位置更新推迟最多一个带间隔（近带 1 / 远带 4 tick）。
            // 坐标锚点一并写回：本 tick 若同时有位移，锚点不跟上的话下一 tick 会因 moved 再补发一帧
            // （同一次的翻转 + 位移被拆成两帧——近带里就是重复帧）。
            if (slot.ActionState != actionState || slot.Dead != dead)
            {
                slot.ActionState = actionState;
                slot.Dead = dead;
                slot.X = x;
                slot.Z = z;
                slot.RotY = rotY;
                entities[entityId] = slot;
                return true;
            }

            bool moved = x != slot.X || z != slot.Z || rotY != slot.RotY;
            uint elapsed = tick - slot.LastSentTick;
            bool due = moved ? elapsed >= interval : elapsed >= StaticKeepaliveInterval;

            if (due)
            {
                entities[entityId] = new Slot { LastSentTick = tick, Band = band, X = x, Z = z, RotY = rotY, ActionState = actionState, Dead = dead, HasSent = true };
                return true;
            }
            return false;
        }

        /// <summary>实体离开视野时调用——清除节拍，重新入场从零开始</summary>
        public void MarkOutOfSight(long observerId, long entityId)
        {
            if (_state.TryGetValue(observerId, out var entities))
                entities.Remove(entityId);
        }

        /// <summary>实体注销——从所有观察者节拍状态清除</summary>
        public void RemoveEntity(long entityId)
        {
            foreach (var entities in _state.Values)
                entities.Remove(entityId);
        }

        /// <summary>移除观察者全部节拍状态（会话断开）</summary>
        public void RemoveObserver(long observerId) => _state.Remove(observerId);

        /// <summary>按距离返回带编号：0 近(&lt;30) / 1 中(30~60) / 2 远(60~80)</summary>
        public static int BandOf(float distance)
        {
            if (distance < MidBandStart) return 0;
            if (distance < FarBandStart) return 1;
            return 2;
        }

        /// <summary>带间隔（tick 数）：近 1 / 中 2 / 远 4</summary>
        public static uint IntervalOf(int band) => band switch
        {
            0 => 1,
            1 => 2,
            _ => 4
        };
    }
}
