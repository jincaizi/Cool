using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Xunit;

namespace KcpServer.Tests;

/// <summary>I1：判定帧 LOS。起手后目标躲到墙后，NPC 的这一刀不能命中；玩家施法不受影响。</summary>
public class CombatResolverLineOfSightTests
{
    private const float NpcHp = 100f;

    private static (EntityStore Store, PlayerState Target) NpcCasterAndPlayer()
    {
        var store = new EntityStore();
        store.AddNpc(100, "monster", templateId: 1, hp: NpcHp, position: new Vector3(0f, 0f, 0f));
        var target = store.AddPlayer(1, "P1");
        target.IsLoggedIn = true;
        target.UpdatePosition(new Vector3(0f, 0f, 1f), 0f, 0f, 1L, 0U);
        return (store, target);
    }

    private static CastRequest Melee()
    {
        return new CastRequest
        {
            SkillId = 50001,
            TargetPlayerId = 1,
            Shape = new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }
        };
    }

    private static List<HitEntry> DrainAll(CombatResolver resolver, uint untilTick)
    {
        var hits = new List<HitEntry>();
        for (uint t = 1; t <= untilTick; t++)
            foreach (var resolution in resolver.DrainResolved(t))
                hits.AddRange(resolution.Hits);
        return hits;
    }

    [Fact]
    public void Npc_Cast_Misses_When_Target_Hides_Behind_Wall_At_Resolve_Tick()
    {
        var (store, target) = NpcCasterAndPlayer();
        bool wall = false;
        var resolver = new CombatResolver(store, new System.Random(1))
        {
            NpcLineOfSight = (_, _, _) => !wall
        };

        Assert.True(resolver.Decide(100, Melee(), tick: 0).Accepted);
        wall = true; // 起手（tick 0）后玩家躲到墙后；判定帧 = 0 + ceil(0.35*15) = 6

        Assert.Empty(DrainAll(resolver, 6));
        Assert.Equal(100f, target.Hp, 0.001f);
    }

    [Fact]
    public void Npc_Cast_Hits_When_No_Wall_At_Resolve_Tick()
    {
        var (store, target) = NpcCasterAndPlayer();
        var resolver = new CombatResolver(store, new System.Random(1))
        {
            NpcLineOfSight = (_, _, _) => true
        };

        Assert.True(resolver.Decide(100, Melee(), tick: 0).Accepted);
        var hits = DrainAll(resolver, 6);
        Assert.Single(hits);
        Assert.Equal(1L, hits[0].TargetPlayerId);
        Assert.True(target.Hp < 100f);
    }

    [Fact]
    public void Player_Cast_Is_Not_Filtered_By_Npc_Los_Callback()
    {
        var store = new EntityStore();
        store.AddPlayer(1, "caster");
        store.AddNpc(2, "target", templateId: 1, hp: 100f, position: new Vector3(0f, 0f, 2f));
        var resolver = new CombatResolver(store, new System.Random(1))
        {
            NpcLineOfSight = (_, _, _) => false // 玩家施法绝不能走这个回调
        };

        var request = new CastRequest
        {
            SkillId = 10002,
            Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
        };
        Assert.True(resolver.Decide(1, request, tick: 0).Accepted);
        var hits = DrainAll(resolver, 40);
        Assert.Contains(hits, h => h.TargetPlayerId == 2L);
    }
}
