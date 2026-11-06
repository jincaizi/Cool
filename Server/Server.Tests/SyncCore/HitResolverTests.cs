using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class HitResolverTests
{
    private static CombatCandidate C(long id, float x, float z, bool alive = true, float radius = 0f)
        => new CombatCandidate(id, new Vec3(x, 0f, z), alive, radius);

    // Single 形状：SkillQ range 3.5 / angle 120（全角 → 半角 60°）
    private static readonly HitShape Single35 = new HitShape
    {
        ShapeType = 0, Range = 3.5f, Angle = 120f
    };

    private static readonly HitShape Circle25 = new HitShape
    {
        ShapeType = 1, AreaRadius = 2.5f
    };

    private static readonly HitShape Single20 = new HitShape
    {
        ShapeType = 0, Range = 2f, Angle = 120f
    };

    private static readonly HitShape Single25 = new HitShape
    {
        ShapeType = 0, Range = 2.5f, Angle = 120f
    };

    [Fact]
    public void Single_Explicit_Target_In_Range_Hits()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 2f), C(3, 0f, 5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_Explicit_Target_Exactly_At_Range_Hits()
    {
        // 距离恰为 Range（3.5，含边界）：dist == Range 不被拒
        var candidates = new List<CombatCandidate> { C(2, 0f, 3.5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_Explicit_Target_Out_Of_Range_Misses()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Single_Explicit_Target_Dead_Misses()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 2f, alive: false) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Single_Explicit_Target_Behind_Caster_Misses()
    {
        // D-6 绕背躲怪：显式目标同样要过锥（此前只查距离，锥角是死参数）
        var candidates = new List<CombatCandidate> { C(2, 0f, -2f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Single_Explicit_Target_At_Cone_Edge_Hits()
    {
        // 锥边界恰 60°：(0.8660254, 0.5) → dot == halfCos，含边界
        var candidates = new List<CombatCandidate> { C(2, 0.8660254f, 0.5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_Explicit_Target_Zero_Distance_Hits()
    {
        // 与自动目标同一条零距离护栏
        var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_AutoTarget_Picks_Nearest_In_Cone()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 3f), C(3, 0f, 1f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
        Assert.Equal(new long[] { 3 }, hits); // 最近者
    }

    [Fact]
    public void Single_AutoTarget_Picks_Nearest_By_Surface_Distance()
    {
        // spec §4.4：自动目标按表面距离取最近——A 表面 3.0−1.5=1.5 < B 表面 2.0，即使 B 中心更近
        var candidates = new List<CombatCandidate> { C(2, 0f, 3f, radius: 1.5f), C(3, 0f, 2f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single25, candidates);
        Assert.Equal(new long[] { 2 }, hits);

        // 负对照：A 无半径时中心距 3.0 > Range 2.5 被拒，B 中心距 2.0 命中
        var noRadius = new List<CombatCandidate> { C(2, 0f, 3f), C(3, 0f, 2f) };
        Assert.Equal(new long[] { 3 },
            HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single25, noRadius));
    }

    [Fact]
    public void Single_AutoTarget_Ignores_Behind_Caster()
    {
        // 身后实体（-Z）在 range 内但不在前方 ±60° 锥内；前方实体命中
        var candidates = new List<CombatCandidate> { C(2, 0f, -2f), C(3, 0f, 2f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
        Assert.Equal(new long[] { 3 }, hits);
    }

    [Fact]
    public void Single_AutoTarget_ZeroDistance_Degenerate_Hits()
    {
        // 双端同出生点（D-P4-2a 退化修正）：dist=0 视为锥内
        var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_AutoTarget_Respects_Caster_Yaw()
    {
        // yaw=90° → forward=(1,0,0)（+X）；仅 +X 侧实体在锥内
        var candidates = new List<CombatCandidate> { C(2, 2f, 0f), C(3, 0f, 2f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 90f, 0, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_AutoTarget_Exactly_At_Cone_Edge_Hits()
    {
        // 锥边界恰 60°：(x,z)=(sin60°,cos60°) → dot == halfCos == 0.5f 位级相等（含边界语义）；
        // 该点 dist=1.0 在 Range 内，隔离验证锥含边界（仅差 1 ulp 靠外的点会被拒）
        var candidates = new List<CombatCandidate> { C(2, 0.8660254f, 0.5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Circle_Hits_All_Within_Radius_Excludes_Far()
    {
        var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f), C(4, 10f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
        Assert.Equal(new long[] { 2, 3 }, hits);
    }

    [Fact]
    public void Circle_Ignores_Dead()
    {
        var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f, alive: false) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_Explicit_Target_With_Radius_Hits_By_Surface_Distance()
    {
        var withRadius = new List<CombatCandidate> { C(2, 0f, 3f, radius: 1.74f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, withRadius);
        Assert.Equal(new long[] { 2 }, hits);

        var noRadius = new List<CombatCandidate> { C(2, 0f, 3f) };
        Assert.Empty(HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, noRadius));
    }

    [Fact]
    public void Line_Hit_Expands_By_Target_Radius()
    {
        var withRadius = new List<CombatCandidate> { C(2, 2.5f, 5f, radius: 1.74f) };
        Assert.Equal(new long[] { 2 }, HitResolver.ResolveLineAll(
            new Vec3(0, 0, 0), 0f, 0f, 10f, 1f, null, withRadius));

        var noRadius = new List<CombatCandidate> { C(2, 2.5f, 5f) };
        Assert.Empty(HitResolver.ResolveLineAll(
            new Vec3(0, 0, 0), 0f, 0f, 10f, 1f, null, noRadius));
    }

    [Fact]
    public void Circle_Hit_Expands_By_Target_Radius()
    {
        var withRadius = new List<CombatCandidate> { C(2, 0f, 3f, radius: 1.74f) };
        Assert.Equal(new long[] { 2 },
            HitResolver.ResolveCircle(new Vec3(0, 0, 0), 2f, withRadius));

        var noRadius = new List<CombatCandidate> { C(2, 0f, 3f) };
        Assert.Empty(HitResolver.ResolveCircle(new Vec3(0, 0, 0), 2f, noRadius));
    }

    [Fact]
    public void Side_Target_With_Radius_Is_Hit_By_Angular_Reach()
    {
        // 中心距 2.16、半径 1.74 → 表面 0.42 ≤ 2.0；
        // 中心方向 90°，半锥 60° + asin(1.74/2.16) ≈ 53.7° → 命中
        var candidates = new List<CombatCandidate> { C(2, 2.16f, 0f, radius: 1.74f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Side_Target_Without_Radius_Is_Missed()
    {
        // 同一中心距、半径 0：射程与锥角都不满足
        var candidates = new List<CombatCandidate> { C(2, 2.16f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Side_Target_In_Range_Without_Radius_Is_Missed()
    {
        // 表面距 1.5 ≤ 2.0，仅中心角 90° > 60° 被拒——隔离角判定
        var candidates = new List<CombatCandidate> { C(2, 1.5f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Target_Directly_Behind_Is_Missed_Even_With_Radius()
    {
        // 正后方 180°：角扩展上限（60° + asin(1.74/2.16) ≈ 113.7°）仍不足以命中
        var candidates = new List<CombatCandidate> { C(2, 0f, -2.16f, radius: 1.74f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Caster_Inside_Target_Body_Hits()
    {
        // 中心距 1.0 < 半径 1.74，且中心在侧面 90°：施法者已在身体内，角度无定义时取命中
        var candidates = new List<CombatCandidate> { C(2, 1f, 0f, radius: 1.74f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Explicit_Target_Uses_Angular_Reach()
    {
        var withRadius = new List<CombatCandidate> { C(2, 2.16f, 0f, radius: 1.74f) };
        Assert.Equal(new long[] { 2 },
            HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, withRadius));

        var noRadius = new List<CombatCandidate> { C(2, 1.5f, 0f) };
        Assert.Empty(HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, noRadius));
    }
}
