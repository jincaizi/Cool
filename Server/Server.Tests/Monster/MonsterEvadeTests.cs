using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterEvadeTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State, long Id) Make()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        return (store, module, module.GetState(id)!, id);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    [Fact]
    public void Evade_Returns_Home_Heals_Clears_Threat_And_Enters_Grace()
    {
        var (store, module, state, id) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);

        var entity = store.GetPlayer(id)!;
        entity.ApplyDamage(50f); // 100 -> 50
        state.Position = new Vector3(0f, 0f, 25f); // > Slime leash 23
        state.CurrentPatrolIndex = 2; // 到家必须归零，否则空钉子

        uint t = 0;
        while (t < 600 && state.Mode != MonsterMode.Patrol) { module.Tick(t); t++; }
        uint arrivalTick = t - 1;

        Assert.Equal(MonsterMode.Patrol, state.Mode);
        Assert.Equal(entity.MaxHp, entity.Hp, 3);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
        Assert.Equal(0, state.CurrentPatrolIndex);
        Assert.Equal(30u, MonsterBrain.PostEvadeGraceTicks);
        Assert.Equal(arrivalTick + 30u, state.PostEvadeGraceUntilTick);
    }

    [Fact]
    public void Target_Out_Of_Drop_Radius_Enters_Evade_Immediately()
    {
        var (store, module, state, _) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);

        // Slime drop radius = leash = 23；把玩家移到 24m 外
        store.GetPlayer(1)!.UpdatePosition(new Vector3(0f, 0f, 24f), 0f, 0f, 2L, 0U);
        module.Tick(0);

        Assert.Equal(MonsterMode.Evade, state.Mode); // 出 drop 半径是立即脱战，不等 45 tick grace
    }

    [Fact]
    public void Evade_And_Dead_Ignore_Taunt()
    {
        var (store, module, state, id) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));

        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;
        module.Taunt(id, 1, durationTicks: 30, tick: 0);
        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
        Assert.Equal(0u, state.TauntUntilTick);

        state.Mode = MonsterMode.Dead;
        module.Taunt(id, 1, durationTicks: 30, tick: 0);
        Assert.Equal(MonsterMode.Dead, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
        Assert.Equal(0u, state.TauntUntilTick);

        module.OnDamageTaken(id, 1, 25f, MonsterDamageKind.Direct);
        Assert.Equal(MonsterMode.Dead, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
    }

    [Fact]
    public void Evade_Ignores_Incoming_Damage_Threat()
    {
        var (store, module, state, id) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;

        module.OnDamageTaken(id, 1, 25f, MonsterDamageKind.Direct);

        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
    }

    [Fact]
    public void Evade_Timeout_Despawns_Then_Respawns_After_Short_Delay()
    {
        var (store, module, state, id) = Make();
        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;
        state.SpawnPosition = new Vector3(0f, 0f, 1000f); // 回不到家

        Assert.Equal(300u, MonsterBrain.ReturnTimeoutTicks);
        Assert.Equal(30u, MonsterModule.EvadeRespawnDelayTicks);

        for (uint t = 0; t < 300; t++) module.Tick(t); // t=0..299
        Assert.NotNull(store.GetPlayer(id)); // t=299 尚未超时

        module.Tick(300);
        Assert.Null(store.GetPlayer(id)); // t=300 超时移除
        Assert.Contains(id, module.DrainRemovedNpcs());

        for (uint t = 301; t < 330; t++)
        {
            module.Tick(t);
            Assert.Equal(0, module.Count); // 30 tick 延迟内不得提前重生
        }

        module.Tick(330);
        Assert.Equal(1, module.Count);
        Assert.DoesNotContain(store.All, p => p.PlayerId == id);

        // 重生位置必须是 PatrolCenter（0,0,0）
        foreach (var npc in store.All)
        {
            Assert.Equal(0f, npc.Position.X, 1);
            Assert.Equal(0f, npc.Position.Z, 1);
        }
    }

    [Fact]
    public void Post_Evade_Grace_Blocks_Vision_Acquisition()
    {
        var (store, module, state, _) = Make();
        // 原点出生时 YawTowardOrigin(0,0,0) 面向 -Z；玩家放 -Z 侧才在视野锥内。
        Login(store, 1, new Vector3(0f, 0f, -3f));
        state.Mode = MonsterMode.Patrol;
        state.PostEvadeGraceUntilTick = 30;

        module.Tick(0);
        Assert.Equal(MonsterMode.Patrol, state.Mode);

        module.Tick(30);
        Assert.Equal(MonsterMode.Combat, state.Mode);
    }
}
