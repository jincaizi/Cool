using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

/// <summary>
/// I2 端到端：<c>CombatResolver.NpcDamaged</c> 回调 → <c>MonsterModule.OnDamageTaken</c> → 仇恨/目标。
/// 不经过 Server3C——生产接线点是 <c>CastHandler.SetNpcDamageObserver</c> 注入的同一签名，
/// 这里直接按该签名接回模块，覆盖"回调确实被伤害裁决触发 + 归属写入仇恨表"整条链。
/// </summary>
public class NpcDamageThreatIntegrationTests
{
    private static (EntityStore Store, MonsterModule Module, long NpcId) Make()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        // 非原点出生：避免 YawTowardOrigin(0,0,0) 的退化朝向；玩家在 z=4.5 即在 -Z 视野锥内。
        long npcId = module.SpawnMonster(1, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 5f));
        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(0f, 0f, 4.5f), 0f, 0f, 1L, 0U);
        return (store, module, npcId);
    }

    private static CombatResolver Wire(EntityStore store, MonsterModule module)
    {
        var resolver = new CombatResolver(store, new Random(42));
        resolver.NpcDamaged = (targetNpcId, attackerId, damage) =>
            module.OnDamageTaken(targetNpcId, attackerId, damage, MonsterDamageKind.Direct);
        return resolver;
    }

    private static void Hit(CombatResolver resolver)
    {
        Assert.True(CombatRules.TryGet(50002, out var entry)); // Single，baseDamage 15，crit 0，HitDelay 6 tick
        var request = new CastRequest
        {
            SkillId = 50002,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        };
        Assert.True(resolver.Decide(1, request, 0).Accepted);
        resolver.DrainResolved(CombatRules.HitDelayTicks(entry!));
    }

    [Fact]
    public void Player_Hit_Through_Resolver_Raises_Threat_And_Enters_Combat()
    {
        var (store, module, npcId) = Make();
        var resolver = Wire(store, module);

        Hit(resolver);

        var state = module.GetState(npcId)!;
        Assert.Equal(MonsterMode.Combat, state.Mode);
        Assert.Equal(1L, state.TargetId);
        Assert.Equal(15f + ThreatTable.ThreatFlat, state.Threats.ThreatOf(1), 3);
    }

    [Fact]
    public void Blocked_Hit_Through_Resolver_Raises_Reduced_Threat()
    {
        var (store, module, npcId) = Make();
        var npc = store.GetPlayer(npcId)!;
        npc.IsDefending = true;
        npc.Rotation = 180f; // NPC 在 z=5 面向 -Z 的玩家（正面格挡锥内）
        var resolver = Wire(store, module);

        Hit(resolver);

        var state = module.GetState(npcId)!;
        Assert.Equal(MonsterMode.Combat, state.Mode);
        Assert.Equal(1L, state.TargetId);
        Assert.Equal(15f * CombatRules.BlockDamageScale + ThreatTable.ThreatFlat,
            state.Threats.ThreatOf(1), 3);
    }
}
