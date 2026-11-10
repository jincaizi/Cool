using System;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// B1：攻击高度与跳跃规避。地面近战（50001）高度带 [−0.3, 1.2]；
/// 玩家受击体积 1.8m（脚底起算）。跳跃到 2m 时脱离攻击体积 → 不命中。
/// </summary>
public class CombatResolverHeightTests
{
    private static (EntityStore Store, CombatResolver Resolver, long Caster, long Target) World(float targetY)
    {
        var store = new EntityStore();
        var caster = store.AddNpc(1, "Slime", 1, 100f, new Vector3(0f, 0f, 0f), 0.5f);
        caster.Rotation = 0f; // 面向 +Z
        var target = store.AddPlayer(2, "P");
        target.IsLoggedIn = true;
        target.UpdatePosition(new Vector3(0f, targetY, 1f), 180f, 0f, 1L, 0U);
        return (store, new CombatResolver(store, new Random(42)), 1, 2);
    }

    private static CastRequest SlimeBite() => new CastRequest
    {
        SkillId = 50001, // Single range 1.5 / HitDelay 0.35s（6 tick @15Hz）
        TargetPlayerId = 2,
        Shape = new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }
    };

    private static uint FirstHitTick => CombatRules.HitDelayTicks(EntryOf(50001));

    [Fact]
    public void Ground_Melee_Misses_Jumping_Target()
    {
        var (store, resolver, caster, target) = World(targetY: 2f);
        Assert.True(resolver.Decide(caster, SlimeBite(), 0).Accepted);

        Assert.Empty(resolver.DrainResolved(FirstHitTick).SelectMany(r => r.Hits));
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Ground_Melee_Hits_Target_On_The_Ground()
    {
        var (store, resolver, caster, target) = World(targetY: 0f);
        Assert.True(resolver.Decide(caster, SlimeBite(), 0).Accepted);

        var hit = Assert.Single(Assert.Single(resolver.DrainResolved(FirstHitTick)).Hits);

        Assert.Equal(target, hit.TargetPlayerId);
        Assert.Equal(90f, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Ground_Melee_Misses_Target_Above_The_Height_Band()
    {
        // 2.5m 在服务端钳制范围内（≤3m），但不是合法跳高结果也能被高度带拒绝。
        var (store, resolver, caster, target) = World(targetY: 2.5f);
        resolver.GroundHeight = _ => 0f;
        Assert.True(resolver.Decide(caster, SlimeBite(), 0).Accepted);

        Assert.Empty(resolver.DrainResolved(FirstHitTick).SelectMany(r => r.Hits));
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void ResolveCombatHeight_Clamps_Reported_Y_To_Physics_Envelope()
    {
        var (store, resolver, _, target) = World(targetY: 0f);
        resolver.GroundHeight = _ => 0f;
        var player = store.GetPlayer(target)!;

        player.UpdatePosition(new Vector3(0f, 50f, 1f), 180f, 0f, 1L, 0U);
        Assert.Equal(MovementValidator.MaxJumpHeight, resolver.ResolveCombatHeight(player), 4);

        player.UpdatePosition(new Vector3(0f, -5f, 1f), 180f, 0f, 1L, 0U);
        Assert.Equal(-1f, resolver.ResolveCombatHeight(player), 4);

        player.UpdatePosition(new Vector3(0f, 2f, 1f), 180f, 0f, 1L, 0U);
        Assert.Equal(2f, resolver.ResolveCombatHeight(player), 4);
    }

    [Fact]
    public void ResolveCombatHeight_Leaves_Npc_And_No_Ground_Host_Untouched()
    {
        var (store, resolver, caster, target) = World(targetY: 50f);
        var player = store.GetPlayer(target)!;
        var npc = store.GetPlayer(caster)!;

        // 无地面提供者：客户端自报值原样使用（旧行为）
        Assert.Equal(50f, resolver.ResolveCombatHeight(player), 4);

        // NPC 位置是服务端权威：即使有地面提供者也不钳制
        resolver.GroundHeight = _ => 0f;
        npc.UpdatePosition(new Vector3(0f, 50f, 0f), 0f, 0f, 1L, 0U);
        Assert.Equal(50f, resolver.ResolveCombatHeight(npc), 4);
    }

    private static SkillEntry EntryOf(int skillId)
    {
        CombatRules.TryGet(skillId, out var entry);
        return entry!;
    }
}
