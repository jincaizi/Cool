using System.Linq;
using Cool.Protocol;
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

    [Fact]
    public void Reject_Unknown_Skill()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);
        var request = new CastRequest { SkillId = 30001, Shape = Q().Shape };

        var resolution = resolver.ResolveCast(caster.PlayerId, request, 10);

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

        var resolution = resolver.ResolveCast(1, Q(), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("caster-dead", resolution.RejectReason);
    }

    [Fact]
    public void Reject_On_Cooldown()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        Assert.True(resolver.ResolveCast(1, Q(), 10).Accepted);
        Assert.False(resolver.ResolveCast(1, Q(), 11).Accepted);      // 冷却 90 tick 内
        Assert.Equal("cooldown", resolver.ResolveCast(1, Q(), 11).RejectReason);
        Assert.True(resolver.ResolveCast(1, Q(), 10 + 90).Accepted);  // 冷却到
    }

    [Fact]
    public void Reject_Inflated_Range_Shape()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        var resolution = resolver.ResolveCast(1, Q(range: 999f), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("shape-tampered", resolution.RejectReason);
    }

    [Fact]
    public void Reject_Wrong_Shape_Type()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        var resolution = resolver.ResolveCast(1, Q(shapeType: 1), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("shape-tampered", resolution.RejectReason);
    }

    [Fact]
    public void Accept_Single_Hits_Target_And_Applies_Server_Damage()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        var target = Spawn(store, 2, 0, 2); // Q range 3.5 内

        var resolution = resolver.ResolveCast(1, Q(), 10);

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
        var resolution = resolver.ResolveCast(1, request, 10);

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

        var resolution = resolver.ResolveCast(1, Q(), 10);

        Assert.True(resolution.Accepted);
        Assert.Empty(resolution.Hits);
        Assert.NotNull(resolution.Broadcast); // 空挥也广播表现
    }

    [Fact]
    public void Cooldown_Records_Even_On_Whiff()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);

        Assert.True(resolver.ResolveCast(1, Q(), 10).Accepted);   // 空挥
        Assert.False(resolver.ResolveCast(1, Q(), 11).Accepted);  // 立即再施 → 冷却拒绝（D-P4-5）
    }

    [Fact]
    public void Zero_Distance_AutoTarget_Hits()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 0); // 同出生点（双端 e2e 场景）

        var resolution = resolver.ResolveCast(1, Q(), 10);

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
        var resolution = resolver.ResolveCast(1, request, 10);

        Assert.True(resolution.Accepted); // 施法本身合法
        Assert.Empty(resolution.Hits);
    }
}
