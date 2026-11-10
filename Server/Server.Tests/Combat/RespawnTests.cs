using System;
using Cool.Protocol;
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

            const uint deadTick = 1000;
            Assert.False(Server3C.AdvanceRespawn(p, deadTick, TickMs, default));        // 第一遍：打死亡标记
            Assert.False(p.IsAlive);

            Assert.False(Server3C.AdvanceRespawn(p, deadTick + 121, TickMs, default));  // 8 秒前一个 tick
            Assert.False(p.IsAlive);

            Assert.True(Server3C.AdvanceRespawn(p, deadTick + 122, TickMs, default));   // 到点：复活
            Assert.True(p.IsAlive);
            Assert.Equal(p.MaxHp, p.Hp, 3);

            Assert.False(Server3C.AdvanceRespawn(p, deadTick + 200, TickMs, default));  // 活着不再触发
        }

        [Fact]
        public void First_Position_Report_After_Far_Respawn_Is_Not_Corrected_Towards_Death()
        {
            var spawn = new Vector3(-15f, 0.3f, -30f);
            var player = new PlayerState(1) { SupportsAuthoritativeRespawn = true, IsDefending = true };
            player.UpdatePosition(new Vector3(60f, 0f, 0f), 0f, 5f, TimeSpan.TicksPerSecond, 1);
            player.ApplyDamage(150f);
            using var server = new Server3C(new TestLogger());
            var handler = new PositionSyncHandler(server, new TestLogger(), new EntityStore());

            var stale = new PositionSyncRequest { X = 60f, Timestamp = 20_000_000L, Sequence = 2 };
            Assert.Null(handler.ValidateAndApply(stale, player));
            Assert.False(Server3C.AdvanceRespawn(player, 1000, TickMs, spawn));
            Assert.True(Server3C.AdvanceRespawn(player, 1122, TickMs, spawn));
            Assert.Equal(spawn, player.Position);
            Assert.Equal(0f, player.Speed);
            Assert.False(player.IsDefending);
            Assert.Null(handler.ValidateAndApply(stale, player));
            Assert.Equal(spawn, player.Position);

            var result = handler.ValidateAndApply(new PositionSyncRequest
            {
                X = spawn.X, Y = spawn.Y, Z = spawn.Z,
                Timestamp = TimeSpan.TicksPerSecond * 10,
                Sequence = 3, RespawnTick = 1122
            }, player);

            Assert.False(Assert.IsType<Cool.SyncCore.MoveValidationResult>(result).NeedsCorrection);
            Assert.Equal(spawn, player.Position);
        }

        [Fact]
        public void Respawn_Wire_Data_Preserves_The_Spawn_And_Separates_Zero_Tick_From_Legacy()
        {
            var notice = new RespawnNotice
            {
                Hp = 100f, Tick = 7, HasSpawnPosition = true, SpawnX = -15f, SpawnY = 0.3f, SpawnZ = -30f
            };
            var decodedNotice = (RespawnNotice)MessageCodec.Decode(
                MessageCodec.EncodeToArray(MessageId.RespawnNotice, notice)).message;
            Assert.True(decodedNotice.HasSpawnPosition);
            Assert.Equal(7u, decodedNotice.Tick);
            Assert.Equal((-15f, 0.3f, -30f), (decodedNotice.SpawnX, decodedNotice.SpawnY, decodedNotice.SpawnZ));

            var request = (PositionSyncRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.PositionSyncRequest, new PositionSyncRequest { RespawnTick = 0 })).message;
            Assert.Equal(0u, request.RespawnTick);
            var response = (PositionSyncResponse)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.PositionSyncResponse, new PositionSyncResponse())).message;
            Assert.Null(response.RespawnTick);
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
