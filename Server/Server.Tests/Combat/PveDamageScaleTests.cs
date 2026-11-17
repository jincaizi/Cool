using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class PveDamageScaleTests
{
    [Fact]
    public void Npc_Damage_To_720_Hp_Player_Is_Scaled_By_7_2()
    {
        var store = new EntityStore();
        var npc = store.AddPlayer(100, "Slime");
        npc.Kind = (byte)EntityKind.Npc;
        npc.TemplateId = 1;
        npc.BodyRadius = 0.5f;
        var target = store.AddPlayer(1, "P1");
        target.SetMaxHp(720f);
        target.IsLoggedIn = true;
        // 怪物在原点、朝向 +Z（yaw=0）；玩家在其正前方 1m（50001 射程 1.5 内）
        target.UpdatePosition(new Vector3(0f, 0f, 1f), 0f, 0f, 0, 0);
        var resolver = new CombatResolver(store, new System.Random(1));

        var decision = resolver.Decide(100, new CastRequest
        {
            SkillId = 50001,
            Shape = new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }
        }, tick: 0);

        Assert.True(decision.Accepted);
        Assert.True(CombatRules.TryGet(50001, out var entry));
        var resolved = resolver.DrainResolved(CombatRules.HitDelayTicks(entry)).Single();
        Assert.Equal(72f, resolved.Hits.Single().Damage, 3);
    }
}
