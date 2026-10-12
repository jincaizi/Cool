using System;
using System.Collections.Generic;
using KcpServer.Movement;

namespace KcpServer
{
    /// <summary>
    /// 压测 bot 群（spec §5-P3：100~200 移动 bot）——注册为 EntityStore 假玩家
    /// （无 session，只被观察者看），每 tick 直线游走 + 边界反弹。确定性随机
    /// （固定种子）保证测试可复现。主循环线程独占调用。
    /// 2026-10-13：接上 ORCA 避让——期望位移 → 解算 → 积分；**边界反弹仍按意图方向判**。
    /// </summary>
    public sealed class BotRunner
    {
        public const float WorldHalfExtent = 500f;  // ±500m 世界
        public const float BotSpeedPerTick = 0.53f; // ~8m/s @ 66ms tick

        /// <summary>
        /// 机器人身体半径（米，ORCA 避让用）——对应客户端那条**占位胶囊**
        /// （RemoteEntityRegistry 给 Kind=PlayerBot 建的 `CreatePrimitive(Capsule)`：单位胶囊
        /// 半径 0.5、根缩放 1，见 p5-monster-server / D-P5-7 的降级路径），所以取 0.5。
        /// 机器人不是真角色模型（100 个蒙皮网格会压垮手机浏览器），半径也就跟着占位体走。
        /// </summary>
        public const float BotRadius = 0.5f;

        /// <summary>主循环 tick 秒数（Server3C.TickIntervalMs = 66）——
        /// 只用于把 m/s 的上报速度换算成快照里的米/tick（ORCA 邻居速度）</summary>
        public const float TickDeltaSeconds = 0.066f;

        private readonly EntityStore _store;
        private readonly Func<long> _nextPlayerId;
        private readonly Random _random;
        private readonly AvoidanceSystem? _avoidance;
        private readonly List<Bot> _bots = new List<Bot>();
        private long _tickCounter;

        private sealed class Bot
        {
            public long PlayerId;
            public float DirX;
            public float DirZ;
            public Vector3 Velocity;   // 上一 tick 的实际位移（米/tick）——ORCA 的"当前速度"
        }

        /// <param name="avoidance">与怪物模块共用的避让系统（Server3C 注入）。null = 不接避让</param>
        public BotRunner(EntityStore store, Func<long> nextPlayerId, int seed = 12345,
            AvoidanceSystem? avoidance = null)
        {
            _store = store;
            _nextPlayerId = nextPlayerId;
            _random = new Random(seed);
            _avoidance = avoidance;
        }

        /// <summary>生成 count 个 bot（随机位置 + 随机方向）</summary>
        public void Spawn(int count)
        {
            for (int i = 0; i < count; i++)
            {
                long id = _nextPlayerId();
                float x = (float)(_random.NextDouble() * 2 - 1) * WorldHalfExtent * 0.8f;
                float z = (float)(_random.NextDouble() * 2 - 1) * WorldHalfExtent * 0.8f;
                var player = _store.AddPlayer(id, $"Bot_{id}");
                // 与真人走同一条伤害/复活路径，只在**渲染**上分叉（D-N8 / D-P5-7）
                player.Kind = (byte)EntityKind.PlayerBot;
                player.UpdatePosition(new Vector3(x, 0, z), 0f, 0f, 1L, 0U);
                // MarkDirty 保留（P2 测试语义）；AOI 可见性靠 grid 首帧 + scheduler 首槽，不依赖脏标记
                var bot = new Bot { PlayerId = id };
                RandomDir(bot);
                _bots.Add(bot);
            }
        }

        public void DespawnAll()
        {
            foreach (var bot in _bots)
                _store.RemovePlayer(bot.PlayerId);
            _bots.Clear();
        }

        /// <summary>推进一 tick（服务器主循环每 tick 调用）</summary>
        public void Tick(uint tick)
        {
            _tickCounter++;
            foreach (var bot in _bots)
            {
                var player = _store.GetPlayer(bot.PlayerId);
                if (player == null) continue;

                // 期望位移 = 意图方向 × 每 tick 步长（接线前 x/z 就是这么加的——无邻居时逐位不变）
                Vector3 step = new Vector3(bot.DirX * BotSpeedPerTick, 0f, bot.DirZ * BotSpeedPerTick);
                if (_avoidance != null)
                    step = _avoidance.Solve(bot.PlayerId, null, player.Position, bot.Velocity,
                        step, BotRadius, BotSpeedPerTick);
                bot.Velocity = step;

                float x = player.Position.X + step.X;
                float z = player.Position.Z + step.Z;

                // 边界反弹：**按意图方向判**（不是按"新位置越界"判）。解出的位移可能被避让改向/改小，
                // 用解出的位置判界会让贴着墙、被邻居往外推的 bot 把方向翻反（甚至翻来覆去卡在界外）。
                // 无避让时两者等价：step 恒沿 Dir，越界 ⟺ 意图朝界外（旧行为逐字保留）。
                if ((bot.DirX > 0f && x > WorldHalfExtent) || (bot.DirX < 0f && x < -WorldHalfExtent))
                {
                    bot.DirX = -bot.DirX;
                    x = Clamp(x);
                }
                if ((bot.DirZ > 0f && z > WorldHalfExtent) || (bot.DirZ < 0f && z < -WorldHalfExtent))
                {
                    bot.DirZ = -bot.DirZ;
                    z = Clamp(z);
                }

                // 速度上报（快照 Speed，m/s）：取**精确**的每 tick 步长换算值而不是四舍五入的 8f——
                // 快照是 ORCA 邻居速度的唯一来源（AvoidanceSystem.EnsureSnapshot 按 Speed × 朝向 还原位移）。
                // 客户端无消费方（只有 MovementReporter 写它，读的地方是插值位置）。
                player.UpdatePosition(new Vector3(x, 0, z), YawOf(bot.DirX, bot.DirZ),
                    BotSpeedPerTick / TickDeltaSeconds, tick * 10_000L + 1L, (uint)_tickCounter);
            }
        }

        private static float Clamp(float v) => Math.Clamp(v, -WorldHalfExtent, WorldHalfExtent);

        private static float YawOf(float dx, float dz) => (float)(System.Math.Atan2(dx, dz) * 180.0 / System.Math.PI);

        /// <summary>随机四向移动（单轴 ±1——保持 ~8m/s 标称速度；双向斜走是 √2× 超速）</summary>
        private void RandomDir(Bot bot)
        {
            if (_random.NextDouble() < 0.5)
            {
                bot.DirX = _random.NextDouble() < 0.5 ? 1f : -1f;
                bot.DirZ = 0f;
            }
            else
            {
                bot.DirX = 0f;
                bot.DirZ = _random.NextDouble() < 0.5 ? 1f : -1f;
            }
        }
    }
}
