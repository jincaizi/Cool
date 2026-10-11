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
