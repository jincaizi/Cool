using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class BrainTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State) Make(
        Vector3 spawn, float hp = 100f)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, spawn, spawn);
        var entity = store.GetPlayer(id)!;
        entity.Hp = hp;
        return (store, module, module.GetState(id)!);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.IsLoggedIn = true;
        p.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return p;
    }

    [Fact]
    public void Perception_Transitions_Patrol_To_Combat_With_Initial_Threat()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        // 出生在原点时 YawTowardOrigin 退化（atan2(-0,-0) = -π）→ 面向 -Z；玩家放 -Z 方向才在视野锥内
        Login(store, 1, new Vector3(0f, 0f, -3f));

        module.Tick(0);

        Assert.Equal(MonsterMode.Combat, state.Mode);
        Assert.Equal(1L, state.TargetId);
        Assert.Equal(ThreatTable.ThreatInitial, state.Threats.ThreatOf(1), 3);
    }

    [Fact]
    public void Damage_While_Patrolling_Transitions_To_Combat_With_Attacker()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Patrol;
        state.Threats.Clear();
        state.TargetId = null;

        // 直接走模块公开入口（生产链由任务 4 的 CombatResolver 回调调用）
        module.OnDamageTaken(state.NpcId, 1, 20f, MonsterDamageKind.Direct);
        module.Tick(0);

        Assert.Equal(MonsterMode.Combat, state.Mode);
        Assert.Equal(1L, state.TargetId);
    }

    [Fact]
    public void Current_Target_Kept_Until_New_Threat_Exceeds_Threshold()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        Login(store, 2, new Vector3(0f, 0f, 3.5f));
        state.Threats.Clear();
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 110f);

        module.Tick(0);
        Assert.Equal(1L, state.TargetId); // 10% 不换

        state.Threats.Add(2, 30f); // 总计 140 > 120
        module.Tick(1);
        Assert.Equal(2L, state.TargetId);
    }

    [Fact]
    public void Windup_Locks_Target_Even_When_Threat_Flips()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        Login(store, 2, new Vector3(0f, 0f, 3.5f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 10f);
        state.Threats.Add(2, 1000f);
        state.AttackResolveTick = 10;

        module.Tick(0);
        Assert.Equal(1L, state.TargetId);
    }

    [Fact]
    public void Leaving_Leash_Enters_Evade()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);

        state.Position = new Vector3(0f, 0f, 100f); // > Slime leash 23
        module.Tick(0);

        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
    }

    [Fact]
    public void Target_Death_With_No_Other_Threat_Enters_Evade_After_Grace()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        var player = Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);
        player.ApplyDamage(999f);

        for (uint t = 0; t < MonsterBrain.TargetLostGraceTicks; t++)
        {
            module.Tick(t);
            Assert.Equal(MonsterMode.Combat, state.Mode);
        }
        module.Tick(MonsterBrain.TargetLostGraceTicks);
        Assert.Equal(MonsterMode.Evade, state.Mode);
    }

    [Fact]
    public void Target_Death_With_Another_Threat_Switches_Instead_Of_Evading()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        var first = Login(store, 1, new Vector3(0f, 0f, 3f));
        Login(store, 2, new Vector3(0f, 0f, 3.5f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 50f);
        first.ApplyDamage(999f);

        module.Tick(0);
        Assert.Equal(2L, state.TargetId);
        Assert.Equal(MonsterMode.Combat, state.Mode);
    }

    [Fact]
    public void Post_Evade_Grace_Blocks_Perception_But_Damage_Reaggros()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Patrol;
        state.PostEvadeGraceUntilTick = 30;

        module.Tick(0);
        Assert.Equal(MonsterMode.Patrol, state.Mode);

        module.OnDamageTaken(state.NpcId, 1, 20f, MonsterDamageKind.Direct);
        Assert.Equal(MonsterMode.Combat, state.Mode);
    }
}
