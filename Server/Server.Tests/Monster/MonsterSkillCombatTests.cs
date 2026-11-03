using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterSkillCombatTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State) Make(int templateId)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        // 出生点不要用原点：YawTowardOrigin(0,0,0) 是 atan2(-0,-0) 退化角，
        // 怪会面向 -Z；放在 (0,0,5) 让朝向稳定为 -Z，玩家放 z<5 即在视野锥内。
        long id = module.SpawnMonster(templateId, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 5f));
        return (store, module, module.GetState(id)!);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    [Fact]
    public void DogKnight_Below_Half_Hp_Uses_MidRange_Skill_At_4m()
    {
        var (store, module, state) = Make(3);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(200f); // 300 -> 100（< 50%）
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m，在 -Z 视野锥内

        var casts = new List<CastRequest>();
        float firstMidRangeDistance = -1f;
        for (uint t = 0; t < 30; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts())
            {
                if (request.SkillId == 50005 && firstMidRangeDistance < 0f)
                    firstMidRangeDistance = Vector3.Distance(state.Position, store.GetPlayer(1)!.Position);
                casts.Add(request);
            }
        }

        Assert.Contains(casts, r => r.SkillId == 50005);
        Assert.DoesNotContain(casts, r => r.SkillId == 50003);
        Assert.True(firstMidRangeDistance > 2.2f + 0.01f,
            $"50005 应在 50003 射程外决策，实测首次距离 {firstMidRangeDistance:F2}m");
    }

    [Fact]
    public void DogKnight_Above_Half_Hp_Chases_Into_Melee_Range()
    {
        var (store, module, state) = Make(3);
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m

        var casts = new List<CastRequest>();
        float firstMeleeDistance = -1f;
        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts())
            {
                if (request.SkillId == 50003 && firstMeleeDistance < 0f)
                    firstMeleeDistance = Vector3.Distance(state.Position, store.GetPlayer(1)!.Position);
                casts.Add(request);
            }
        }

        Assert.Contains(casts, c => c.SkillId == 50003);
        Assert.DoesNotContain(casts, c => c.SkillId == 50005);
        Assert.True(firstMeleeDistance <= state.Config.attackRange + 0.05f,
            $"50003 应在近战距离决策，实测首次距离 {firstMeleeDistance:F2}m"
            + $"（attackRange {state.Config.attackRange}）");
    }

    [Fact]
    public void SpiderBoss_Below_Half_Hp_Uses_Aoe()
    {
        var (store, module, state) = Make(4);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(1000f); // 1500 -> 500（< 50%）
        Login(store, 1, new Vector3(0f, 0f, 2f)); // 距出生点 3m

        var casts = new List<CastRequest>();
        for (uint t = 0; t < 30; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts()) casts.Add(request);
        }

        Assert.Contains(casts, r => r.SkillId == 50006);
        Assert.Equal(1, casts.Find(r => r.SkillId == 50006)!.Shape.ShapeType);
    }

    [Fact]
    public void Attack_Does_Not_Switch_Skill_During_Windup()
    {
        var (store, module, state) = Make(3);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(200f);
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m

        uint decideTick = 0;
        CastRequest? first = null;
        for (uint t = 0; t < 30 && first == null; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts())
            {
                decideTick = t;
                first = request;
            }
        }
        Assert.NotNull(first);
        Assert.True(state.AttackResolveTick.HasValue);

        // 前摇期间给另一目标 1000 威胁：TargetId 不变
        Login(store, 2, new Vector3(0f, 0f, 0.5f)); // 距出生点 4.5m
        state.Threats.Add(2, 1000f);
        module.Tick(decideTick + 1);
        Assert.Equal(1L, state.TargetId);
    }

    /// <summary>I1 回归：目标消失（离场/死亡/出 drop 半径）后 brain 清空
    /// <c>LastKnownTargetPosition</c>，Chase 不得用自身位置冒充目标——距离 0 落进近战技能区间
    /// 会 `return Success` 且不写移动态，上一 tick 的 Run 与 moveSpeed 继续广播到 Evade（最长 45 tick）。
    /// Wolf 生成在 (0,0,5)（非原点，朝向稳定），玩家在 (0,0,1)（4m，-Z 视野锥内）。</summary>
    [Fact]
    public void Chase_Without_LastKnown_Target_Writes_Idle_And_Zero_Speed()
    {
        var (store, module, state) = Make(2);
        var wolf = store.GetPlayer(state.NpcId)!;
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m（Wolf 攻击距离 2m）

        // 前置：Wolf 确实在追击（Moving 且位置变化）——否则下一 tick 的 Idle 可能只是"本来就没跑"
        var start = wolf.Position;
        bool chasing = false;
        for (uint t = 0; t < 5 && !chasing; t++)
        {
            module.Tick(t);
            chasing = state.Moving && Vector3.Distance(start, wolf.Position) > 0.01f;
        }
        Assert.True(chasing, "前置：Wolf 应在追击中");

        store.RemovePlayer(1); // 目标消失：brain 下一 tick 清 LastKnownTargetPosition
        module.Tick(5);

        Assert.False(state.Moving);
        Assert.Equal(0f, wolf.Speed);
        Assert.Equal((byte)NpcAnimationState.Idle, wolf.ActionState);
    }
}
