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
}
