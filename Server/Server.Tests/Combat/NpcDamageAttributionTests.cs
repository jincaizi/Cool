using System;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests.Combat;

/// <summary>
/// 任务 4：NPC 伤害归属回调——玩家命中怪物时，战斗裁决核心把「哪个玩家打了哪只怪、
/// 实际伤害多少」通知给怪物模块（仇恨表用）。
/// </summary>
public class NpcDamageAttributionTests
{
    private static (EntityStore Store, PlayerState Player, PlayerState Npc) Make()
    {
        var store = new EntityStore();
        var npc = store.AddNpc(2_000_001, "npc", 1, 100f, new Vector3(0f, 0f, 1f));
        npc.Rotation = 180f; // 面向 -Z（朝向玩家）
        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        return (store, player, npc);
    }

    [Fact]
    public void Player_Hit_On_Npc_Raises_Callback_With_Final_Damage()
    {
        var (store, _, _) = Make();
        var resolver = new CombatResolver(store, new Random(42));
        long victim = 0, attacker = 0;
        float damage = 0f;
        resolver.NpcDamaged = (npcId, playerId, amount) =>
        {
            victim = npcId; attacker = playerId; damage = amount;
        };

        Assert.True(CombatRules.TryGet(10001, out var entry));
        var request = new CastRequest
        {
            SkillId = 10001,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        };
        Assert.True(resolver.Decide(1, request, 0).Accepted);
        resolver.DrainResolved(CombatRules.HitDelayTicks(entry!));

        Assert.Equal(2_000_001L, victim);
        Assert.Equal(1L, attacker);
        Assert.Equal(entry!.BaseDamage, damage, 3);
    }

    [Fact]
    public void Blocked_Hit_Reports_Reduced_Damage()
    {
        var (store, _, npc) = Make();
        npc.IsDefending = true;
        var resolver = new CombatResolver(store, new Random(42));
        float damage = 0f;
        resolver.NpcDamaged = (_, _, amount) => damage = amount;

        Assert.True(CombatRules.TryGet(10001, out var entry));
        var request = new CastRequest
        {
            SkillId = 10001,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        };
        resolver.Decide(1, request, 0);
        resolver.DrainResolved(CombatRules.HitDelayTicks(entry!));

        Assert.Equal(entry!.BaseDamage * CombatRules.BlockDamageScale, damage, 3);
    }
}
