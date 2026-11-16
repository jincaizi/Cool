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
                    Hp = 100f, MaxHp = 100f, IsDefending = false,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Speed | EntityFieldMask.Dead
                        | EntityFieldMask.Health | EntityFieldMask.Defending)
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable);
        // 头 5B + protobuf 负载——记录实测值供预算常量校准（断言仅为上下限防回归膨胀/收缩）
        // T12：新增 Hp/MaxHp/IsDefending 三字段（tag 13–15 + Health/Defending 掩码位）约 +12B，
        // 上限从 120 抬到 140 作裕量；下限仍为 20（防止字段被误删后测试静默变绿）。
        Assert.InRange(bytes.Length, 20, 140);
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

    [Fact]
    public void Hundred_Entity_Fifteen_Hertz_Sixty_Seconds_Stays_Under_100_KBps()
    {
        // 实测口径：100 个实体编码进**同一个** WorldSnapshot（近带每 tick 全量最坏场景），
        // × 15Hz 即每秒字节。预算不因本测试上调——超了就是超了，须降频/降实体。
        var entities = new List<EntitySnapshot>();
        for (int i = 0; i < 100; i++)
        {
            entities.Add(new EntitySnapshot
            {
                PlayerId = i + 1, X = i, Z = i,
                Rotation = i * 10f, Speed = 4.5f,
                Hp = 720f, MaxHp = 720f, Dead = true, IsDefending = true,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                    | EntityFieldMask.Speed | EntityFieldMask.Dead | EntityFieldMask.Health
                    | EntityFieldMask.Defending),
                CombatVersion = (uint)(i + 1), LifeId = 1
            });
        }
        var snapshot = new WorldSnapshot
        {
            Tick = 1, ServerTimestamp = 1L, Entities = entities
        };
        int perTick = MessageCodec.EncodeToArray(
            MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable).Length;
        int totalPerSecond = perTick * 15;
        Assert.True(totalPerSecond <= GlobalGameSettings.BandwidthBudgetBytesPerSec,
            $"estimated {totalPerSecond} B/s (perTick={perTick}, near-band 15Hz)");
    }

    [Fact]
    public void Projection_Baseline_Stays_Within_Combat_Tick_Budget()
    {
        // 属性剑气 v1：基线快照携带完整战斗投影（装备/血量/控制/活动施法位）。
        // 单实体基线包须落在合理区间——低于下限说明投影被静默丢弃，高于上限说明字段膨胀。
        var baseline = new WorldSnapshot
        {
            Tick = 1, ServerTimestamp = 1L,
            Entities = new List<EntitySnapshot>
            {
                new EntitySnapshot
                {
                    PlayerId = 1,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Speed | EntityFieldMask.Dead
                        | EntityFieldMask.Health | EntityFieldMask.Defending
                        | EntityFieldMask.NpcAction | EntityFieldMask.Combat),
                    X = 1f, Z = 2f, Rotation = 90f, Hp = 720f, MaxHp = 720f,
                    CombatVersion = 1, LifeId = 1,
                    Combat = new CombatProjection
                    {
                        CombatVersion = 1, LifeId = 1, Hp = 720f, MaxHp = 720f,
                        IsDefending = false,
                        Equipment = new EquipmentSummary
                        {
                            WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                        },
                        Control = new ControlSummary { SpeedScale = 1f },
                        ActiveCast = new ActiveCastProjection
                        {
                            CastId = 7, SkillId = 10002,
                            Phase = NpcActionPhase.Windup, EndTick = 100
                        }
                    }
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, baseline,
            MessageFlags.Reliable);
        // 2026-11-17 实测：基础投影 81B，补 ActiveCast 后 92B（含 5B 帧头）；
        // 下限挡住投影被静默丢弃，上限挡住字段膨胀。
        Assert.InRange(bytes.Length, 80, 300);
    }
}
