using System.Collections.Generic;
using Cool.Protocol;
using KcpServer;
using KcpServer.Config;
using Xunit;
using Xunit.Abstractions;

namespace KcpServer.Tests;

public class BandwidthBudgetTests
{
    private readonly ITestOutputHelper _output;

    public BandwidthBudgetTests(ITestOutputHelper output) => _output = output;

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
        // 下限只兜住"整块 Combat 投影被静默丢弃"（兜不住投影内 Statuses 丢失——状态/DoT 流
        // 由 Hundred_Entities_Status_And_Dot_60s 的流下限兜底），上限挡住字段膨胀。
        Assert.InRange(bytes.Length, 80, 300);
    }

    [Fact]
    public void Hundred_Entities_Status_And_Dot_60s_Stay_Under_100_KBps_Per_Observer()
    {
        // Plan 05 验收口径：100 实体全近带 15Hz 跑满 60s，叠加状态/DoT 可靠流。
        // 回放内容（每 tick）：100 实体的全字段 EntitySnapshot（位置/旋转/速度/Dead/Defending）；
        // 每 15 tick 抽一个实体带 Health + CombatVersion/LifeId（血量翻转），同 tick 再发一条
        // CombatStateUpdate（4 状态）与一条 DamageResult（DoT HitEntry）；每 30 tick 抽一个实体带
        // 完整 Combat 投影（4 StatusProjection + ActiveCast）。全部用 MessageCodec 实际编码累加。
        // 预算不上调：超 100KB/s 就是超了，须降测试中的近带实体数/保活频率并记录原因。
        const int Ticks = 900;       // 15Hz × 60s
        const int EntityCount = 100;

        long totalBytes = 0;
        long statusUpdateTotal = 0;
        long damageResultTotal = 0;
        int perTickMax = 0;
        int perTickMaxWorldSnapshot = 0;
        var entities = new List<EntitySnapshot>(EntityCount);

        for (int tick = 0; tick < Ticks; tick++)
        {
            var snapshot = BuildReplayWorldSnapshot(tick, entities);
            int tickBytes = MessageCodec.EncodeToArray(
                MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable).Length;
            int worldSnapshotBytes = tickBytes;
            int healthIndex = (tick / 15) % EntityCount;   // 每 15 tick 一个实体血量翻转

            if (tick % 15 == 0)
            {
                var statusUpdate = BuildCombatStateUpdate(entities[healthIndex].PlayerId, tick);
                int statusBytes = MessageCodec.EncodeToArray(
                    MessageId.CombatStateUpdate, statusUpdate, MessageFlags.Reliable).Length;
                statusUpdateTotal += statusBytes;
                tickBytes += statusBytes;

                var dot = BuildDotDamageResult(entities[healthIndex].PlayerId, tick);
                int dotBytes = MessageCodec.EncodeToArray(
                    MessageId.DamageResult, dot, MessageFlags.Reliable).Length;
                damageResultTotal += dotBytes;
                tickBytes += dotBytes;
            }

            totalBytes += tickBytes;
            if (tickBytes > perTickMax) perTickMax = tickBytes;
            if (worldSnapshotBytes > perTickMaxWorldSnapshot)
                perTickMaxWorldSnapshot = worldSnapshotBytes;
        }

        // 状态/DoT 流下限：两条流合计仅占总字节约 0.44%，回放块整体消失时总字节/单 tick
        // 断言都不会红——用实测值（311≈182B/条、DoT≈56B/条 × 60 条）留约 20% 余量兜底。
        Assert.InRange(statusUpdateTotal, 60L * 150, 60L * 400);
        Assert.InRange(damageResultTotal, 60L * 40, 60L * 120);

        // 投影基线单实体沿用现有 [80, 300] 上下限。此 fixture 特意带 4 个 StatusProjection +
        // ActiveCast：下限只兜住"整块 Combat 投影被丢弃"，兜不住投影内 Statuses 丢失——
        // 状态/DoT 流另由上面的流下限兜底；上限挡住字段膨胀。
        var baseline = new WorldSnapshot
        {
            Tick = 1, ServerTimestamp = 1L,
            Entities = new List<EntitySnapshot>
            {
                new EntitySnapshot
                {
                    PlayerId = 1,
                    Tick = 1,
                    X = 1f, Y = 0.5f, Z = 2f, Rotation = 90f, Speed = 4.5f,
                    Dead = true, IsDefending = true,
                    Hp = 680f, MaxHp = 720f,
                    CombatVersion = 1, LifeId = 1,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Speed | EntityFieldMask.Dead
                        | EntityFieldMask.Health | EntityFieldMask.Defending
                        | EntityFieldMask.Combat),
                    Combat = BuildFullCombatProjection(1, 0)
                }
            }
        };
        int baselineBytes = MessageCodec.EncodeToArray(
            MessageId.WorldSnapshot, baseline, MessageFlags.Reliable).Length;
        Assert.InRange(baselineBytes, 80, 300);

        long budgetTotal = (long)(GlobalGameSettings.BandwidthBudgetBytesPerSec * 60f);
        double avgBytesPerSec = totalBytes / 60.0;
        double kBps = avgBytesPerSec / 1000.0;
        _output.WriteLine("perTickMax=" + perTickMax + "B, totalBytes=" + totalBytes
            + "B/60s, avg=" + avgBytesPerSec.ToString("F0") + "B/s, kBps="
            + kBps.ToString("F1") + ", budget=" + GlobalGameSettings.BandwidthBudgetBytesPerSec
            + "B/s, perTickMaxWorldSnapshot=" + perTickMaxWorldSnapshot
            + "B, statusUpdateTotal=" + statusUpdateTotal
            + "B, damageResultTotal=" + damageResultTotal + "B, baseline=" + baselineBytes + "B");

        Assert.True(totalBytes <= budgetTotal,
            "60s total " + totalBytes + "B exceeds budget " + budgetTotal + "B (perTickMax="
            + perTickMax + "B)");
        Assert.True(perTickMax <= 150 * 1024,
            "single tick " + perTickMax + "B exceeds 150KB cap");
    }

    [Fact]
    public void Hundred_Entities_Simultaneous_Dot_60s_Stay_Under_100_KBps_Per_Observer()
    {
        // 最坏变体：100 实体同时中 DoT（每实体 1Hz 跳伤）。初稿口径"每 tick 100 条 311 +
        // 100 条 DamageResult"实测单 tick ≈ 3.7KB 快照 + 100×(182+56)B ≈ 27.5KB，×15Hz
        // ≈ 413KB/s，是 100KB/s 预算的 4 倍——每 tick 全量状态更新不是真实频率
        // （DoT 的 IntervalTicks=15，即 1Hz）。故按"每 15 tick 一次 100 实体全量 DoT 跳伤"
        // 降频回放（已记录降频原因）；预算不上调。
        const int Ticks = 900;             // 15Hz × 60s
        const int EntityCount = 100;
        const int DotIntervalTicks = 15;   // = StatusProjection.IntervalTicks（1Hz）

        long totalBytes = 0;
        long statusUpdateTotal = 0;
        long damageResultTotal = 0;
        int perTickMax = 0;
        var entities = new List<EntitySnapshot>(EntityCount);

        for (int tick = 0; tick < Ticks; tick++)
        {
            var snapshot = BuildReplayWorldSnapshot(tick, entities);
            int tickBytes = MessageCodec.EncodeToArray(
                MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable).Length;

            if (tick % DotIntervalTicks == 0)
            {
                for (int i = 0; i < EntityCount; i++)
                {
                    long entityId = entities[i].PlayerId;
                    int statusBytes = MessageCodec.EncodeToArray(
                        MessageId.CombatStateUpdate, BuildCombatStateUpdate(entityId, tick),
                        MessageFlags.Reliable).Length;
                    statusUpdateTotal += statusBytes;
                    tickBytes += statusBytes;

                    int dotBytes = MessageCodec.EncodeToArray(
                        MessageId.DamageResult, BuildDotDamageResult(entityId, tick),
                        MessageFlags.Reliable).Length;
                    damageResultTotal += dotBytes;
                    tickBytes += dotBytes;
                }
            }

            totalBytes += tickBytes;
            if (tickBytes > perTickMax) perTickMax = tickBytes;
        }

        // 状态/DoT 流下限（最坏变体：60 轮 × 100 实体 = 6000 条 311 + 6000 条 DoT）：
        // 回放块整体消失时总字节/单 tick 断言都不会红——镜像常规变体按 6000 条缩放，
        // 用实测值（311≈182B/条、DoT≈56B/条）留约 20% 余量兜底。
        Assert.InRange(statusUpdateTotal, 6000L * 150, 6000L * 400);
        Assert.InRange(damageResultTotal, 6000L * 40, 6000L * 120);

        long budgetTotal = (long)(GlobalGameSettings.BandwidthBudgetBytesPerSec * 60f);
        double avgBytesPerSec = totalBytes / 60.0;
        double kBps = avgBytesPerSec / 1000.0;
        _output.WriteLine("worstDot: perTickMax=" + perTickMax + "B, totalBytes=" + totalBytes
            + "B/60s, avg=" + avgBytesPerSec.ToString("F0") + "B/s, kBps="
            + kBps.ToString("F1") + ", budget=" + GlobalGameSettings.BandwidthBudgetBytesPerSec
            + "B/s, statusUpdateTotal=" + statusUpdateTotal
            + "B, damageResultTotal=" + damageResultTotal + "B");

        Assert.True(totalBytes <= budgetTotal,
            "worst-case 60s total " + totalBytes + "B exceeds budget " + budgetTotal
            + "B (perTickMax=" + perTickMax + "B)");
        Assert.True(perTickMax <= 150 * 1024,
            "worst-case single tick " + perTickMax + "B exceeds 150KB cap");
    }

    /// <summary>每 tick 的 100 实体全字段快照（位置/旋转/速度/Dead/Defending）；每 15 tick 一个实体
    /// 带 Health + CombatVersion/LifeId，每 30 tick 一个实体带完整 Combat 投影（回放两变体共用）</summary>
    private static WorldSnapshot BuildReplayWorldSnapshot(int tick, List<EntitySnapshot> entities)
    {
        const int EntityCount = 100;
        int healthIndex = (tick / 15) % EntityCount;   // 每 15 tick 一个实体血量翻转
        int combatIndex = (tick / 30) % EntityCount;   // 每 30 tick 一个实体带完整战斗投影
        entities.Clear();

        for (int i = 0; i < EntityCount; i++)
        {
            var entity = new EntitySnapshot
            {
                PlayerId = i + 1,
                Tick = (uint)(tick + 1),
                X = i * 1.5f,
                Y = 0.5f,
                Z = -i * 1.25f,
                Rotation = (i * 17f) % 360f,
                Speed = 4.5f,
                Dead = i % 10 == 0,
                IsDefending = i % 7 == 0,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                    | EntityFieldMask.Speed | EntityFieldMask.Dead
                    | EntityFieldMask.Defending)
            };

            if (tick % 15 == 0 && i == healthIndex)
            {
                entity.Hp = 720f - (tick % 150) * 3f;
                entity.MaxHp = 720f;
                entity.CombatVersion = (uint)(tick / 15 + 1);
                entity.LifeId = 1;
                entity.FieldMask |= (ushort)EntityFieldMask.Health;
            }

            if (tick % 30 == 0 && i == combatIndex)
            {
                entity.CombatVersion = (uint)(tick / 30 + 1);
                entity.LifeId = 1;
                entity.Combat = BuildFullCombatProjection(entity.PlayerId, tick);
                entity.FieldMask |= (ushort)EntityFieldMask.Combat;
            }

            entities.Add(entity);
        }

        return new WorldSnapshot
        {
            Tick = (uint)(tick + 1),
            ServerTimestamp = 1_700_000_000_000_0000L + tick * 66_667L,
            Entities = entities
        };
    }

    /// <summary>完整战斗投影：4 个状态 + 装备/控制/活动施法（状态/DoT 回放与投影基线共用）</summary>
    private static CombatProjection BuildFullCombatProjection(long entityId, int tick)
    {
        var statuses = new List<StatusProjection>(4);
        for (int s = 0; s < 4; s++)
        {
            statuses.Add(new StatusProjection
            {
                EffectId = 1001 + s,
                GroupId = 1,
                EndTick = (uint)(tick + 90 + s * 15),
                SourceId = entityId,
                SlowRatio = 0.7f,
                DamagePerTick = 12.5f,
                IntervalTicks = 15,
                IncomingDamageMultiplier = 1.15f
            });
        }

        return new CombatProjection
        {
            CombatVersion = (uint)(tick / 15 + 1),
            LifeId = 1,
            Hp = 680f,
            MaxHp = 720f,
            Dead = false,
            IsDefending = true,
            Statuses = statuses,
            Equipment = new EquipmentSummary
            {
                WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
            },
            Control = new ControlSummary { SpeedScale = 0.7f, ControlVersion = 3 },
            ActiveCast = new ActiveCastProjection
            {
                CastId = 9, SkillId = 10002,
                Phase = NpcActionPhase.Windup, EndTick = (uint)(tick + 30)
            }
        };
    }

    /// <summary>消息 311：可靠完整投影（4 状态）</summary>
    private static CombatStateUpdate BuildCombatStateUpdate(long entityId, int tick)
    {
        return new CombatStateUpdate
        {
            EntityId = entityId,
            ServerTimestamp = 1_700_000_000_000_0000L + tick * 66_667L,
            Projection = BuildFullCombatProjection(entityId, tick)
        };
    }

    /// <summary>单条 DoT 跳伤（DamageKind.Dot）</summary>
    private static DamageResult BuildDotDamageResult(long targetId, int tick)
    {
        return new DamageResult
        {
            SourcePlayerId = 1,
            SkillId = 10002,
            ServerTimestamp = 1_700_000_000_000_0000L + tick * 66_667L,
            Hits = new List<HitEntry>
            {
                new HitEntry
                {
                    TargetPlayerId = targetId,
                    Damage = 12.5f,
                    TargetRemainingHp = 667.5f,
                    HitX = 10f,
                    HitZ = -20f,
                    CastId = 9,
                    EventSeq = (uint)(tick / 15 + 1),
                    EventId = (ulong)(tick + 1),
                    DamageKind = DamageKind.Dot,
                    TargetLifeId = 1,
                    CombatVersion = (uint)(tick / 15 + 1)
                }
            }
        };
    }
}
