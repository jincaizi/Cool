using System.Collections.Generic;
using Cool.Protocol;
using KcpServer;
using KcpServer.Config;
using Xunit;

namespace KcpServer.Tests;

public class BandwidthBudgetTests
{
    /// <summary>单实体全字段快照平均编码字节（含 WorldSnapshot 头）——门禁公式基线</summary>
    [Fact]
    public void Entity_FullField_Snapshot_Bytes_Measured()
    {
        var snapshot = new WorldSnapshot
        {
            Tick = 1,
            ServerTimestamp = 1_700_000_000_000_0000L,
            Entities = new List<EntitySnapshot>
            {
                new EntitySnapshot
                {
                    PlayerId = 123, X = 100.5f, Y = 2f, Z = -50.25f,
                    Rotation = 90f, Speed = 8f, ActionState = 0, Tick = 1,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed)
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable);
        // 头 5B + protobuf 负载——记录实测值供预算常量校准（断言仅为下限防回归膨胀）
        Assert.InRange(bytes.Length, 20, 120);
    }

    [Fact]
    public void Budget_Formula_Covers_Full_World_Bot_Scenarios()
    {
        // 门禁口径：预算 ≥ Σ(可见实体数 × 单实体字节 × 带频率) 的最坏场景。
        // 实体字节用规划口径 60B/实体（T5 实测 ≈33B——60B 为裕量，见 GlobalGameSettings 注释）；
        // 测试证明预算覆盖最保守的规划估算。
        // 100 bot 全远带（4Hz）：100 × 4 × 60B ≈ 24 KB/s
        // 100 bot 全中带（8Hz）：100 × 8 × 60B ≈ 48 KB/s
        // 100 bot 全近带（15Hz）：100 × 15 × 60B ≈ 90 KB/s（80m 半径内 100 人——主城扎堆场景，
        // 现实可能存在，预算须覆盖；静止实体 4Hz 保活使常态远低于该上界）
        float estimateFar = 100f * GlobalGameSettings.FarHz * 60f;
        float estimateMid = 100f * GlobalGameSettings.MidHz * 60f;
        float estimateNear = 100f * GlobalGameSettings.NearHz * 60f;
        Assert.True(estimateNear <= GlobalGameSettings.BandwidthBudgetBytesPerSec,
            $"近带 100 实体估算 {estimateNear} B/s 必须低于预算 {GlobalGameSettings.BandwidthBudgetBytesPerSec}");
        Assert.True(estimateMid <= GlobalGameSettings.BandwidthBudgetBytesPerSec);
        Assert.True(estimateFar <= GlobalGameSettings.BandwidthBudgetBytesPerSec);
    }
}
