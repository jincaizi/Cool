using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class CastHandlerTests
{
    private static (EntityStore store, CombatResolver resolver) Setup(int seed = 12345)
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new System.Random(seed));
        return (store, resolver);
    }

    private static PlayerState Spawn(EntityStore store, long id, float x, float z, float rotY = 0f)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.UpdatePosition(new Vector3(x, 0f, z), rotY, 0f, 1L, 0U);
        return p;
    }

    private static CastRequest Q(float range = 3.5f, int shapeType = 0, long targetId = 0)
        => new CastRequest
        {
            SkillId = 20001,
            TargetPlayerId = targetId,
            Shape = new HitShape { ShapeType = shapeType, Range = range, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f }
        };

    /// <summary>N2 迁移助手：决策 → 推进到判定帧（decideTick + 本请求的 HitDelayTicks）→ 取回该次施法的结果。
    /// 断言一字未改，只是把"一步到位"拆成两步；判定 tick 按表推导而非常数——
    /// 硬编码裕量一旦小于表值，助手会静默返回"决策"对象（Hits 空），失败会指错地方。</summary>
    private static CombatResolver.CastResolution Resolve(CombatResolver resolver, long casterId,
        CastRequest request, uint decideTick)
    {
        var decision = resolver.Decide(casterId, request, decideTick);
        if (!decision.Accepted) return decision;
        CombatRules.TryGet(request.SkillId, out var entry); // 决策已保证表命中（防御分支）
        foreach (var r in resolver.DrainResolved(decideTick + CombatRules.HitDelayTicks(entry!)))
            if (r.Broadcast!.CasterPlayerId == casterId && r.Broadcast.SkillId == request.SkillId)
                return r;
        return decision;
    }

    [Fact]
    public void Reject_Unknown_Skill()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);
        // 999999 永不进表；30001 自 2026-10-18 起已同步，不能再作未知技能样本
        var request = new CastRequest { SkillId = 999999, Shape = Q().Shape };

        var resolution = Resolve(resolver, caster.PlayerId, request, 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("unknown-skill", resolution.RejectReason);
    }

    [Fact]
    public void Reject_Caster_Dead()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);
        caster.ApplyDamage(100f); // 击杀

        var resolution = Resolve(resolver, 1, Q(), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("caster-dead", resolution.RejectReason);
    }

    [Fact]
    public void Reject_On_Cooldown()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        Assert.True(Resolve(resolver, 1, Q(), 10).Accepted);
        Assert.False(Resolve(resolver, 1, Q(), 11).Accepted);      // 冷却 90 tick 内
        Assert.Equal("cooldown", Resolve(resolver, 1, Q(), 11).RejectReason);
        Assert.True(Resolve(resolver, 1, Q(), 10 + 90).Accepted);  // 冷却到
    }

    [Fact]
    public void Reject_Inflated_Range_Shape()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        var resolution = Resolve(resolver, 1, Q(range: 999f), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("shape-tampered", resolution.RejectReason);
    }

    [Fact]
    public void Reject_Wrong_Shape_Type()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        var resolution = Resolve(resolver, 1, Q(shapeType: 1), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("shape-tampered", resolution.RejectReason);
    }

    [Fact]
    public void Accept_Single_Hits_Target_And_Applies_Server_Damage()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        var target = Spawn(store, 2, 0, 2); // Q range 3.5 内

        var resolution = Resolve(resolver, 1, Q(), 10);

        Assert.True(resolution.Accepted);
        var hit = Assert.Single(resolution.Hits);
        Assert.Equal(2L, hit.TargetPlayerId);
        Assert.True(hit.Damage == 120f || hit.Damage == 180f,
            $"damage {hit.Damage} 不在服务端掷骰域 {{120, 180}}");
        Assert.Equal(hit.Damage > 120f, hit.IsCritical);
        Assert.Equal(100f - hit.Damage, hit.TargetRemainingHp, 4);
        Assert.Equal(100f - hit.Damage, target.Hp, 4); // 服务器权威扣血
        Assert.NotNull(resolution.Broadcast);
        Assert.Equal(1L, resolution.Broadcast!.CasterPlayerId);
        Assert.Equal(20001, resolution.Broadcast.SkillId);
    }

    [Fact]
    public void Circle_Hits_Multiple_Excludes_Dead_And_Far()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 1, 0);
        Spawn(store, 3, 0, 2);
        var far = Spawn(store, 4, 10, 0);
        var dead = Spawn(store, 5, 0, 1);
        dead.ApplyDamage(100f);

        var request = new CastRequest
        {
            SkillId = 20002,
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f }
        };
        var resolution = Resolve(resolver, 1, request, 10);

        Assert.True(resolution.Accepted);
        Assert.Equal(new long[] { 2, 3 }, resolution.Hits.Select(h => h.TargetPlayerId).OrderBy(id => id).ToArray());
        Assert.Equal(100f, far.Hp, 4); // 圈外不受伤
    }

    [Fact]
    public void Whiff_No_Target_Accepted_With_No_Hits()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 10, 0); // range 外

        var resolution = Resolve(resolver, 1, Q(), 10);

        Assert.True(resolution.Accepted);
        Assert.Empty(resolution.Hits);
        Assert.NotNull(resolution.Broadcast); // 空挥也广播表现
    }

    [Fact]
    public void Cooldown_Records_Even_On_Whiff()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);

        Assert.True(Resolve(resolver, 1, Q(), 10).Accepted);   // 空挥
        Assert.False(Resolve(resolver, 1, Q(), 11).Accepted);  // 立即再施 → 冷却拒绝（D-P4-5）
    }

    [Fact]
    public void Zero_Distance_AutoTarget_Hits()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 0); // 同出生点（双端 e2e 场景）

        var resolution = Resolve(resolver, 1, Q(), 10);

        Assert.True(resolution.Accepted);
        var hit = Assert.Single(resolution.Hits);
        Assert.Equal(2L, hit.TargetPlayerId);
    }

    [Fact]
    public void Dead_Target_Excluded_From_Circle()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        var dead = Spawn(store, 2, 1, 0);
        dead.ApplyDamage(100f);

        var request = new CastRequest
        {
            SkillId = 20002,
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f }
        };
        var resolution = Resolve(resolver, 1, request, 10);

        Assert.True(resolution.Accepted); // 施法本身合法
        Assert.Empty(resolution.Hits);
    }
}
