using System;

namespace KcpServer.Movement
{
    /// <summary>
    /// ORCA 避让的**活体对照探针**（CLI <c>--probe</c>，2026-10-13）——每 tick 扫一遍全体实体，
    /// 统计"最小两两间距"与"重叠对数"，每 3 tick（200ms）打一行，用来回答
    /// **"避让真的把最近距离拉开了吗？"**（开避让 vs <c>--no-avoid</c> 两组各跑一次，比数字）。
    ///
    /// 取数口径（与 AvoidanceSystem.EnsureSnapshot 同一套：活着的、登录的真人与机器人 + 怪物；
    /// 半径走 <see cref="AvoidanceSystem.BodyRadiusOf"/>，不在这里另立一套）：
    ///   · <c>min</c>  —— 该采样 tick 的最小两两间距（全体实体，含相距几百米的远端对）；
    ///   · <c>win</c>  —— **本窗口（3 tick）内**出现过的最小间距（连续跟踪，不漏掉两次采样之间的擦身）；
    ///   · <c>close</c>—— 该 tick 距离 &lt; 5m 的实体对数（贴近事件计数：全局 min 由初始散布决定，
    ///                    这个数才反映"有没有真的遇上了"）；
    ///   · <c>ovl</c>  —— 该 tick **穿模**的对数（距离 &lt; 两半径之和）。
    /// 纯诊断：O(n²) 距离比较，只在本开关显式打开时调用。
    /// </summary>
    public sealed class AvoidanceProbe
    {
        /// <summary>打日志的间隔（tick）：3 × 66ms ≈ 200ms</summary>
        private const int LogEveryTicks = 3;

        /// <summary>"贴近"判定阈值（米）——只用于 close 计数</summary>
        private const float CloseThreshold = 5f;

        private readonly EntityStore _store;
        private readonly AvoidanceSystem _avoidance;
        private readonly float _secondsPerTick;

        private float _windowMin = float.MaxValue;
        private int _samples;
        private uint _baseTick = uint.MaxValue;   // 首个 Sample 的 tick（日志里的 t 从 0 起算）

        public AvoidanceProbe(EntityStore store, AvoidanceSystem avoidance, float secondsPerTick = 0.066f)
        {
            _store = store;
            _avoidance = avoidance;
            _secondsPerTick = secondsPerTick;
        }

        /// <summary>每 tick 调一次（Server3C 主循环 tick 起始处，快照重建之后）</summary>
        public void Sample(uint tick)
        {
            if (_baseTick == uint.MaxValue) _baseTick = tick;

            float min = float.MaxValue;
            int close = 0;
            int overlaps = 0;

            foreach (var p in _store.All)
            {
                if (!IsCounted(p)) continue;
                foreach (var q in _store.All)
                {
                    if (q.PlayerId <= p.PlayerId) continue;   // 每对只算一次
                    if (!IsCounted(q)) continue;

                    float dx = q.Position.X - p.Position.X;
                    float dz = q.Position.Z - p.Position.Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist < min) min = dist;
                    if (dist < CloseThreshold) close++;
                    if (dist < _avoidance.BodyRadiusOf(p) + _avoidance.BodyRadiusOf(q)) overlaps++;
                }
            }

            if (min < _windowMin) _windowMin = min;
            _samples++;

            if (_samples % LogEveryTicks != 0) return;

            float seconds = (tick - _baseTick) * _secondsPerTick;
            float window = _windowMin == float.MaxValue ? -1f : _windowMin;
            Console.WriteLine(
                $"[Probe] t={seconds:F2}s min={(min == float.MaxValue ? -1f : min):F3} win={window:F3} close={close} ovl={overlaps}");
            _windowMin = float.MaxValue;
        }

        /// <summary>与避让快照同一口径：活着的、真人在线、机器人/怪物都算</summary>
        private static bool IsCounted(PlayerState e)
        {
            if (!e.IsAlive) return false;
            if (e.Kind == (byte)Cool.Protocol.EntityKind.Player && !e.IsLoggedIn) return false;
            return true;
        }
    }
}
