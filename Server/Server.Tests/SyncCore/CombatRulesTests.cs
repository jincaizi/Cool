using System;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class CombatRulesTests
{
    [Fact]
    public void Table_Covers_P4_Synced_Skills()
    {
        Assert.True(CombatRules.IsSynced(10001));
        Assert.True(CombatRules.IsSynced(10002));
        Assert.True(CombatRules.IsSynced(20001));
        Assert.True(CombatRules.IsSynced(20002));
        Assert.False(CombatRules.IsSynced(20003)); // Fireball 投射物：P4 不进表（D-P4-1）
        Assert.False(CombatRules.IsSynced(30001)); // Ultimate
        Assert.False(CombatRules.IsSynced(999999));
    }

    [Fact]
    public void Table_Values_Match_Client_Assets()
    {
        // 2026-09-11 资产实测：BA_Combo1/BA_Heavy/Instant_SkillQ/Spin_SkillR（决策表）
        Assert.True(CombatRules.TryGet(10001, out var combo));
        Assert.Equal(0, combo.ShapeType);
        Assert.Equal(2f, combo.MaxRange, 4);
        Assert.Equal(0f, combo.CooldownSeconds, 4);
        Assert.Equal(50f, combo.BaseDamage, 4);
        Assert.Equal(0.15f, combo.CriticalRate, 4);

        Assert.True(CombatRules.TryGet(20001, out var q));
        Assert.Equal(0, q.ShapeType);
        Assert.Equal(3.5f, q.MaxRange, 4);
        Assert.Equal(6f, q.CooldownSeconds, 4);
        Assert.Equal(120f, q.BaseDamage, 4);

        Assert.True(CombatRules.TryGet(20002, out var r));
        Assert.Equal(1, r.ShapeType); // AOE_Circle
        Assert.Equal(2.5f, r.MaxAreaRadius, 4);
        Assert.Equal(5f, r.CooldownSeconds, 4);
        Assert.Equal(160f, r.BaseDamage, 4);
    }

    [Fact]
    public void ValidateShape_Accepts_Asset_Params()
    {
        Assert.True(CombatRules.TryGet(20001, out var q));
        var shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f };
        Assert.True(CombatRules.ValidateShape(shape, q));
    }

    [Fact]
    public void ValidateShape_Rejects_Inflated_Range()
    {
        Assert.True(CombatRules.TryGet(20001, out var q));
        var cheated = new HitShape { ShapeType = 0, Range = 999f, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(cheated, q));
    }

    [Fact]
    public void ValidateShape_Rejects_Wrong_Shape_Type()
    {
        Assert.True(CombatRules.TryGet(20001, out var q));
        var wrong = new HitShape { ShapeType = 1, Range = 3.5f, AreaRadius = 3.5f, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(wrong, q));
    }

    [Fact]
    public void ValidateShape_Rejects_Inflated_AreaRadius()
    {
        Assert.True(CombatRules.TryGet(20002, out var r));
        var cheated = new HitShape { ShapeType = 1, AreaRadius = 50f, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(cheated, r));
    }

    [Fact]
    public void ValidateShape_Rejects_NaN_Range()
    {
        // 防 NaN 绕过：NaN 比较恒 false，可穿透全部上限检查
        Assert.True(CombatRules.TryGet(20001, out var q));
        var nan = new HitShape { ShapeType = 0, Range = float.NaN, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(nan, q));
    }

    [Fact]
    public void ValidateShape_Rejects_NaN_Angle()
    {
        Assert.True(CombatRules.TryGet(20001, out var q));
        var nan = new HitShape { ShapeType = 0, Range = 3.5f, Angle = float.NaN };
        Assert.False(CombatRules.ValidateShape(nan, q));
    }

    [Fact]
    public void ValidateShape_Rejects_Negative_Range()
    {
        // 负值只会削弱自己，但固化不变量：合法形状各字段非负
        Assert.True(CombatRules.TryGet(20001, out var q));
        var neg = new HitShape { ShapeType = 0, Range = -5f, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(neg, q));
    }

    [Fact]
    public void ValidateShape_Accepts_Exact_Boundary_Values()
    {
        // 含边界语义：恰好等于表上限不被拒
        Assert.True(CombatRules.TryGet(20001, out var q));
        var edge = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f };
        Assert.True(CombatRules.ValidateShape(edge, q));

        Assert.True(CombatRules.TryGet(20002, out var r));
        var edgeR = new HitShape { ShapeType = 1, AreaRadius = 2.5f };
        Assert.True(CombatRules.ValidateShape(edgeR, r));
    }

    [Fact]
    public void CooldownTicks_Rounds_Up_At_15Hz()
    {
        Assert.True(CombatRules.TryGet(10001, out var combo));
        Assert.Equal(0U, CombatRules.CooldownTicks(combo));    // 0s → 无冷却
        Assert.True(CombatRules.TryGet(10002, out var heavy));
        Assert.Equal(8U, CombatRules.CooldownTicks(heavy));    // 0.5s × 15 = 7.5 → 8
        Assert.True(CombatRules.TryGet(20001, out var q));
        Assert.Equal(90U, CombatRules.CooldownTicks(q));       // 6s × 15
        Assert.True(CombatRules.TryGet(20002, out var r));
        Assert.Equal(75U, CombatRules.CooldownTicks(r));       // 5s × 15
    }

    [Fact]
    public void Monster_Skill_Entries_Are_Synced_And_Validated()
    {
        Assert.True(CombatRules.IsSynced(50001));
        Assert.True(CombatRules.IsSynced(50002));

        Assert.True(CombatRules.TryGet(50001, out var slime));
        Assert.Equal(1.5f, slime.MaxRange);
        Assert.Equal(10f, slime.BaseDamage);
        Assert.Equal(0f, slime.CriticalRate);

        Assert.True(CombatRules.TryGet(50002, out var wolf));
        Assert.Equal(2f, wolf.MaxRange);
        Assert.Equal(15f, wolf.BaseDamage);

        Assert.True(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }, slime));
        Assert.False(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.6f, Angle = 120f }, slime));

        Assert.Equal(15u, CombatRules.CooldownTicks(wolf)); // 1s @15Hz
    }

    [Fact]
    public void HitDelay_Ticks_Map_From_Asset_Hit_Frames_At_15Hz()
    {
        // 表值来源：客户端动画资产的判定帧实测（ShapeBlock._hitboxTimings + CastTime）
        Assert.True(CombatRules.TryGet(10001, out var combo));
        Assert.Equal(0.30f, combo.HitDelaySeconds, 4);
        Assert.Equal(5U, CombatRules.HitDelayTicks(combo));   // 0.30 × 15 = 4.5 → 5

        Assert.True(CombatRules.TryGet(10002, out var heavy));
        Assert.Equal(0.35f, heavy.HitDelaySeconds, 4);
        Assert.Equal(6U, CombatRules.HitDelayTicks(heavy));   // 0.35 × 15 = 5.25 → 6

        Assert.True(CombatRules.TryGet(20002, out var spin));
        Assert.Equal(0f, spin.HitDelaySeconds, 4);            // 旋转无判定帧：多段命中未建模（D-9）
        Assert.Equal(0U, CombatRules.HitDelayTicks(spin));
    }

    [Fact]
    public void SkillQ_HitDelay_Is_Pinned_To_The_Asset_Measured_Value()
    {
        // 回归钉：20001 是**资产实测值**（0.30s = ShapeBlock._hitboxTimings + CastTime），
        // 与怪物那 4 条占位 0.35s 不同。此前 20001 只有"≤ 冷却"的不等式守着
        //（Monster_HitDelay_Never_Exceeds_Its_Cooldown），把它降级成 0.03 也能全绿。
        // 判定帧 = decideTick + ceil(0.30 × 15) = decideTick + 5。
        Assert.True(CombatRules.TryGet(20001, out var q));
        Assert.Equal(0.30f, q.HitDelaySeconds, 4);
        Assert.Equal(5U, CombatRules.HitDelayTicks(q));
    }

    [Fact]
    public void Monster_HitDelay_Never_Exceeds_Its_Cooldown()
    {
        // 约束只对怪物成立：前摇结束时若冷却已过，怪物会立刻挥第二刀（第一刀的剪辑被截断）。
        // 玩家普攻 10001 冷却为 0 且判定帧 0.30s——玩家侧由动画/连击链把关，不适用本约束。
        foreach (var skillId in new[] { 50001, 50002, 50003, 50004 })
        {
            Assert.True(CombatRules.TryGet(skillId, out var entry), $"缺 {skillId} 表项");
            Assert.True(entry!.HitDelaySeconds <= entry.CooldownSeconds + 0.0001f,
                $"skill {skillId} hitDelay={entry.HitDelaySeconds} > cooldown={entry.CooldownSeconds}");
        }
    }

    // —— PvP 格挡减伤（D-P4-8 服务端化，2026-10-17）——

    [Fact]
    public void Block_Table_Values_Are_Pinned()
    {
        // 表值自持（与判定延迟同一作风）：改这些值 = 平衡改动，必须在这里显式改
        Assert.Equal(0.3f, CombatRules.BlockDamageScale, 4);
        Assert.Equal(60f, CombatRules.BlockHalfAngleDeg, 4);
        Assert.Equal(100f, CombatRules.ShieldMaxDurability, 4);
        Assert.Equal(25f, CombatRules.ShieldDurabilityPerBlock, 4);
    }

    [Fact]
    public void IsBlocked_Inside_Cone_True_Outside_False()
    {
        // yaw=0 → +Z：攻击者在正前 ⇒ 0° ⇒ 格挡；正后 ⇒ 180° ⇒ 不格挡
        Assert.True(CombatRules.IsBlocked(0f, 0f, 1f));
        Assert.False(CombatRules.IsBlocked(0f, 0f, -1f));
        // 斜前方 45°（半角 60 内）；斜后方（约 143°）出锥
        Assert.True(CombatRules.IsBlocked(0f, 0.7f, 0.7f));
        Assert.False(CombatRules.IsBlocked(0f, 0.8f, -0.6f));
    }

    [Fact]
    public void IsBlocked_At_Half_Angle_Boundary_Is_Inclusive()
    {
        // 受击者 yaw=60、攻击者在正 +Z 1m ⇒ 夹角恰好 60°：含边界（与 HitResolver 锥「含边界」同语义）。
        // dist 在此恰好为 1、forwardZ 恰为 float 0.5 ⇒ 断言位级可复现，不是近似运气。
        Assert.True(CombatRules.IsBlocked(60f, 0f, 1f));
        Assert.False(CombatRules.IsBlocked(60.0001f, 0f, 1f)); // 刚出界
    }

    [Fact]
    public void IsBlocked_Zero_Distance_Is_Blocked()
    {
        // 贴脸退化（方向向量趋零、归一化不可靠）⇒ 视为格挡——防"贴脸永远无法格挡"。
        // 与 HitResolver 的"零距离视为锥内"反向配套：攻击方贴脸能命中，防守方贴脸就该能挡。
        Assert.True(CombatRules.IsBlocked(0f, 0f, 0f));
        Assert.True(CombatRules.IsBlocked(180f, 0.00001f, -0.00001f)); // 亚阈值（dist < 0.0001）
    }

    [Fact]
    public void IsBlocked_Yaw_Wraps_Across_Zero()
    {
        // yaw=350 与攻击方向 10° 的夹角 = 20°（跨 0）——sin/cos 展开天然 wrap 安全
        //（朴素"角度相减 + 手工取模"的实现会在这里漏判）
        Assert.True(CombatRules.IsBlocked(350f,
            (float)Math.Sin(10f * Math.PI / 180.0), (float)Math.Cos(10f * Math.PI / 180.0)));
        // 跨 0 的背向：350 与 190 夹角 180° ⇒ 不格挡
        Assert.False(CombatRules.IsBlocked(350f,
            (float)Math.Sin(190f * Math.PI / 180.0), (float)Math.Cos(190f * Math.PI / 180.0)));
    }
}
