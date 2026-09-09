using System;
using System.Collections.Generic;

namespace KcpServer
{
    /// <summary>
    /// 压测 bot 群（spec §5-P3：100~200 移动 bot）——注册为 EntityStore 假玩家
    /// （无 session，只被观察者看），每 tick 直线游走 + 边界反弹。确定性随机
    /// （固定种子）保证测试可复现。主循环线程独占调用。
    /// </summary>
    public sealed class BotRunner
    {
        public const float WorldHalfExtent = 500f;  // ±500m 世界
        public const float BotSpeedPerTick = 0.53f; // ~8m/s @ 66ms tick

        private readonly EntityStore _store;
        private readonly Func<long> _nextPlayerId;
        private readonly Random _random;
        private readonly List<Bot> _bots = new List<Bot>();
        private long _tickCounter;

        private sealed class Bot
        {
            public long PlayerId;
            public float DirX;
            public float DirZ;
        }

        public BotRunner(EntityStore store, Func<long> nextPlayerId, int seed = 12345)
        {
            _store = store;
            _nextPlayerId = nextPlayerId;
            _random = new Random(seed);
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
                player.UpdatePosition(new Vector3(x, 0, z), 0f, 0f, 1L, 0U);
                _store.MarkDirty(player); // 注册即标脏——首个快照携带（保 P2 语义）
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

                float x = player.Position.X + bot.DirX * BotSpeedPerTick;
                float z = player.Position.Z + bot.DirZ * BotSpeedPerTick;

                // 边界反弹
                if (x > WorldHalfExtent || x < -WorldHalfExtent) { bot.DirX = -bot.DirX; x = Clamp(x); }
                if (z > WorldHalfExtent || z < -WorldHalfExtent) { bot.DirZ = -bot.DirZ; z = Clamp(z); }

                player.UpdatePosition(new Vector3(x, 0, z), YawOf(bot.DirX, bot.DirZ), 8f, tick * 10_000L + 1L, (uint)_tickCounter);
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
