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
    }
}
