using KcpServer;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

/// <summary>
/// B4：贴身攻击决策。动作 V1 的 Attack 不能在正常选招之前被 50005 的
/// “后撤到 minRange” setup 抢占——玩家贴脸时，合法就绪的 50003 近战必须优先。
/// </summary>
public class MonsterAttackCloseRangeTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State) Fixture()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, actionV1: true);
        long id = module.SpawnMonster(3, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f)); // DogKnight
        var self = store.GetPlayer(id)!;
        self.Hp = self.MaxHp * 0.4f; // 半血以下：50005 的血量条件满足
        self.Rotation = 0f;           // 面向 +Z

        var player = store.AddPlayer(1, "P");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(0f, 0f, 1.5f), 180f, 0f, 1L, 0U); // 贴身 < 50005 minRange 2

        var state = module.GetState(id)!;
        state.TargetId = 1;
        state.LastKnownTargetPosition = player.Position;
        state.LastHp = self.Hp;
        return (store, module, state);
    }

    private static BtStatus TickAttack(EntityStore store, MonsterModule module, MonsterState state, uint tick)
    {
        var ctx = new MonsterTickContext(state, store.GetPlayer(state.NpcId)!,
            module, new SimpleMoveSystem(), 0.066f, tick);
        return MonsterBtActions.Attack(ctx);
    }

    [Fact]
    public void Ready_Melee_Is_Cast_Instead_Of_Backing_Off_For_Out_Of_Range_Special()
    {
        var (store, module, state) = Fixture();

        var status = TickAttack(store, module, state, tick: 100);

        Assert.Equal(BtStatus.Running, status);
        var pending = Assert.Single(module.DrainPendingCasts());
        Assert.Equal(50003, pending.Request.SkillId); // 不得被 50005 的后撤 setup 抢占
    }

    [Fact]
    public void Setup_Still_Runs_When_No_Ready_In_Range_Skill_Exists()
    {
        var (store, module, state) = Fixture();
        store.GetPlayer(state.NpcId)!.RecordCast(50003, 100); // 近战冷却中

        var status = TickAttack(store, module, state, tick: 100);

        Assert.Equal(BtStatus.Running, status);
        Assert.Empty(module.DrainPendingCasts()); // 没有可施放的技能
        Assert.Equal(50005, state.DesiredSkillId); // 但进入合法 setup（而不是站定放弃）
    }
}
