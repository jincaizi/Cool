using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CombatResolverControlLockTests
{
    [Fact]
    public void Decide_Rejects_New_Cast_While_ActionLocked()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.ApplyControl(1f, true);
        var resolver = new CombatResolver(store, new System.Random(1));
        var request = new CastRequest
        {
            SkillId = 10001,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        };

        var result = resolver.Decide(1, request, tick: 10);

        Assert.False(result.Accepted);
        Assert.Equal("control-lock", result.RejectReason);
    }

    [Fact]
    public void Decide_Opens_Displacement_Window_For_20001()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        var resolver = new CombatResolver(store, new System.Random(1));
        var request = new CastRequest
        {
            SkillId = 20001,
            Shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f }
        };

        var result = resolver.Decide(1, request, tick: 10);

        Assert.True(result.Accepted);
        Assert.NotNull(caster.Displacement);
        Assert.Equal(result.CastId, caster.Displacement!.CastId);
        Assert.Equal(20001, caster.Displacement.SkillId);
        Assert.Equal(10u, caster.Displacement.StartTick);
        // EndTick = tick + ⌈0.3×4×15⌉ = 10 + 18；Speed = 2 / (0.3−0.05−0.05) = 10。
        Assert.Equal(28u, caster.Displacement.EndTick);
        Assert.Equal(10f, caster.Displacement.Speed, 4);
        Assert.Equal(2f, caster.Displacement.TotalDistance, 4);
    }

    [Fact]
    public void Normal_Resolve_Keeps_Displacement_Window_Until_EndTick()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        var resolver = new CombatResolver(store, new System.Random(1));
        var request = new CastRequest
        {
            SkillId = 20001,
            Shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f }
        };

        var result = resolver.Decide(1, request, tick: 10);
        Assert.True(result.Accepted);
        Assert.NotNull(caster.ActiveCast);
        Assert.NotNull(caster.Displacement);

        // 20001 单段：HitDelay 0.30s → ⌈4.5⌉ = 5 tick，首判即最后一段（正常完成）。
        var resolved = resolver.DrainResolved(15);

        Assert.NotEmpty(resolved);
        Assert.Null(caster.ActiveCast);
        Assert.NotNull(caster.Displacement);
        Assert.Equal(result.CastId, caster.Displacement!.CastId);
        Assert.Equal(28u, caster.Displacement.EndTick);
    }
}
