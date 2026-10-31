using System;
using KcpServer;
using Xunit;

namespace KcpServer.Tests
{
    /// <summary>服务端复活（8 秒；D-2）——客户端不自判死，所以这是联网下唯一的复活路径。</summary>
    public class RespawnTests
    {
        private const uint TickMs = 66;   // 与 GameLoop 的 15Hz 一致

        [Fact]
        public void Dead_Player_Respawns_At_Full_Health()
        {
            var p = new PlayerState(1) { PlayerName = "A" };
            p.ApplyDamage(150f);                       // 可到负值（既有语义）
            Assert.False(p.IsAlive);

            p.MarkDead(1000);
            Assert.False(p.IsAlive);

            // 8 秒 = 8000 / 66 ≈ 122 ticks
            Assert.False(p.TryRespawn(1000 + 121, TickMs));
            Assert.False(p.IsAlive);

            Assert.True(p.TryRespawn(1000 + 122, TickMs));
            Assert.True(p.IsAlive);
            Assert.Equal(p.MaxHp, p.Hp, 3);
        }

        [Fact]
        public void Alive_Player_Never_Respawns()
        {
            var p = new PlayerState(1) { PlayerName = "A" };
            Assert.False(p.TryRespawn(999999, TickMs));
        }

        /// <summary>
        /// 主循环扫描（不只是 PlayerState 本身）必须能把死人推到复活。旧实现是
        /// `if (!IsAlive) { MarkDead; continue; }`——`continue` 让 TryRespawn 永不执行，
        /// 单测只测 PlayerState 时全绿，线上却永远不复活。
        /// </summary>
        [Fact]
        public void Dead_Player_Respawns_Through_The_Tick_Sweep()
        {
            var p = new PlayerState(1) { PlayerName = "A" };
            p.ApplyDamage(150f);

            const long deadTick = 1000;
            Assert.False(Server3C.AdvanceRespawn(p, deadTick, TickMs));        // 第一遍：打死亡标记
            Assert.False(p.IsAlive);

            Assert.False(Server3C.AdvanceRespawn(p, deadTick + 121, TickMs));  // 8 秒前一个 tick
            Assert.False(p.IsAlive);

            Assert.True(Server3C.AdvanceRespawn(p, deadTick + 122, TickMs));   // 到点：复活
            Assert.True(p.IsAlive);
            Assert.Equal(p.MaxHp, p.Hp, 3);

            Assert.False(Server3C.AdvanceRespawn(p, deadTick + 200, TickMs));  // 活着不再触发
        }

        [Fact]
        public void Respawn_Does_Not_Fire_Twice()
        {
            var p = new PlayerState(1) { PlayerName = "A" };
            p.ApplyDamage(150f);
            p.MarkDead(1000);
            Assert.True(p.TryRespawn(1000 + 122, TickMs));
            Assert.False(p.TryRespawn(1000 + 300, TickMs));   // 活着，不再复活
        }
    }
}
