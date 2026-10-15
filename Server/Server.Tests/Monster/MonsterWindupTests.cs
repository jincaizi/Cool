using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>N4：前摇 = 承诺（D-10）——决策后站定、朝目标转向、不受距离变化影响；
/// 目标跑出感知半径也不中途改走（F12 的两条缺口）。</summary>
public class MonsterWindupTests
{
    private static (EntityStore Store, MonsterModule Module) Create()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        return (store, module);
    }

    private static void LoginPlayer(EntityStore store, long id, Vector3 position)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.IsLoggedIn = true;
        p.UpdatePosition(position, 0f, 0f, 1L, 0U);
    }

    [Fact]
    public void Windup_Locks_The_Tree_When_The_Target_Leaves_Range()
    {
        var (store, module) = Create();
        // Wolf（模板 2）攻击距离 2.0m，冷却 15 tick——生成在 1.5m 处且面向原点
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        var wolf = store.GetPlayer(wolfId)!;
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));
        var player = store.GetPlayer(1)!;

        uint decideTick = 0;
        for (uint t = 0; t < 10 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0) decideTick = t;
        }
        Assert.NotEqual(0u, decideTick);                                  // 前置：怪物确实出手了
        var state = module.GetState(wolfId)!;
        Assert.True(state.AttackResolveTick.HasValue);                    // 前摇已开始
        var positionAtDecision = wolf.Position;

        player.UpdatePosition(new Vector3(0f, 0f, 6f), 0f, 0f, 1L, 0U); // 目标跑出攻击距离
        for (uint t = decideTick + 1; t <= decideTick + 3; t++)
        {
            module.Tick(t);
            Assert.Empty(module.DrainPendingCasts());                     // 前摇期不重复入队
        }

        Assert.Equal(positionAtDecision.X, wolf.Position.X, 4);           // 站定（不 Chase）
        Assert.Equal(positionAtDecision.Z, wolf.Position.Z, 4);
        Assert.Equal((byte)NpcAnimationState.Attack, wolf.ActionState);   // 仍是攻击态（客户端忙位靠它）
    }

    [Fact]
    public void Windup_Turns_Toward_The_Target_Slowly_Not_Instantly()
    {
        var (store, module) = Create();
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        var wolf = store.GetPlayer(wolfId)!;
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));

        uint decideTick = 0;
        for (uint t = 0; t < 10 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0) decideTick = t;
        }
        var state = module.GetState(wolfId)!;
        float before = wolf.Rotation;          // 决策那一 tick 的朝向（面向原点 = 180°）

        // 目标移到怪物前方 45° 处——**必须留在 120° 感知锥内**，否则目标丢失、
        // 前摇分支只能朝陈旧的 LastKnownTargetPosition 转（那才是本用例要排除的干扰）。
        // 从 (0,0,1.5) 朝 -Z 偏 45°：(-0.707, -0.707) × 1.5 ≈ (-1.06, 0, 0.44)
        store.GetPlayer(1)!.UpdatePosition(new Vector3(-1.06f, 0f, 0.44f), 0f, 0f, 1L, 0U);
        module.Tick(decideTick + 1);
        float afterOneTick = wolf.Rotation;

        Assert.True(state.AttackResolveTick.HasValue);                    // 仍在用前摇转向
        Assert.True(YawDeltaDegrees(afterOneTick, before) > 1f,
            $"前摇期没有转向：before={before} after={afterOneTick}");
        Assert.True(YawDeltaDegrees(afterOneTick, before) < 20f,
            $"一步转得太多（不像 90°/s 的渐进转向，倒像瞬转）：delta={YawDeltaDegrees(afterOneTick, before)}");
    }

    /// <summary>最短角差（度）——yaw 会环绕：180° 附近转一点就报成 -174°，
    /// 直接相减会得到 354 而不是 6。</summary>
    private static float YawDeltaDegrees(float a, float b)
    {
        float d = (a - b) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return System.Math.Abs(d);
    }

    [Fact]
    public void Windup_Expires_And_Unlocks_After_HitDelayTicks()
    {
        var (store, module) = Create();
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));

        uint decideTick = 0;
        for (uint t = 0; t < 10 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0) decideTick = t;
        }
        Assert.True(CombatRules.TryGet(50002, out var wolfSkill));
        uint resolveTick = decideTick + CombatRules.HitDelayTicks(wolfSkill);
        var state = module.GetState(wolfId)!;

        module.Tick(resolveTick);

        Assert.False(state.AttackResolveTick.HasValue);                   // 到期即解锁
    }
}
