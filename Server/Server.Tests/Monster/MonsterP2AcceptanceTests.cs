using System;
using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

/// <summary>
/// 规格 §7.2 P2 行为验收（场景级，走真实模块 tick 与裁决链）：
/// ① 两名玩家抢仇恨——超过 20% 切换阈值才换目标，前摇期间锁目标；
/// ② 多技能——距离 / 血量门槛 / 冷却（50005 间隔 ≥ 60 tick）；
/// ③ 脱战——目标死亡后优先切到另一仇恨目标；超 leash 立即 Evade、回家满血/清仇恨、
///    post-evade grace 内不感知。
/// </summary>
public class MonsterP2AcceptanceTests
{
    private static (EntityStore Store, MonsterModule Module, long Id) Make(int templateId = 1)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        // 非原点出生：避免 YawTowardOrigin(0,0,0) 的退化朝向；玩家放 z<5 即在视野锥内。
        long id = module.SpawnMonster(templateId, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 5f));
        return (store, module, id);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    [Fact]
    public void Later_Attacker_Takes_Over_After_Threshold_But_Not_During_Windup()
    {
        var (store, module, id) = Make();
        var state = module.GetState(id)!;
        Login(store, 1, new Vector3(0f, 0f, 2f));   // 距出生点 3m
        Login(store, 2, new Vector3(0f, 0f, 1.8f)); // 距出生点 3.2m

        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 110f);
        module.Tick(0);
        Assert.Equal(1L, state.TargetId); // 10% < 20%

        state.Threats.Add(2, 40f); // 150 > 120
        module.Tick(1);
        Assert.Equal(2L, state.TargetId);

        state.AttackResolveTick = 100;
        state.Threats.Add(1, 1000f); // 1100
        module.Tick(2);
        Assert.Equal(2L, state.TargetId); // 前摇锁
    }

    [Fact]
    public void Multi_Skill_Selection_Uses_Distance_Hp_And_Cooldown()
    {
        const int midRangeSkill = 50005;
        const int meleeSkill = 50003;

        // 距离 = 4m：50003 的 maxRange 2.2m 够不到，只有 50005 的 [2, 6] 可用。
        var fourMeters = new Vector3(0f, 0f, 1f); // 距出生点 (0,0,5) 4m

        // ① 满血同距离：hpBelow=0.5 不满足，60 tick 内不得出现 50005
        //（若血量门槛失效，50005 priority=10 会立刻抢在近战技能前施放）。
        var (fullStore, fullModule, _) = Make(templateId: 3);
        Login(fullStore, 1, fourMeters);
        var fullCasts = new List<int>();
        for (uint t = 0; t < 60; t++)
        {
            fullModule.Tick(t);
            foreach (var (_, request) in fullModule.DrainPendingCasts()) fullCasts.Add(request.SkillId);
        }
        Assert.DoesNotContain(midRangeSkill, fullCasts);
        // 前置：满血场景确实进入了近战并出手——否则“没有 50005”可能只是没打到人
        Assert.Contains(meleeSkill, fullCasts);

        // ② 低血 4m：选择 50005；冷却由 CombatResolver.Decide 的 RecordCast 写入，
        // module-only 宿主不记冷却，必须走真实裁决链，否则 60-tick 间隔断言会假红/假绿。
        var (store, module, id) = Make(templateId: 3);
        var state = module.GetState(id)!;
        var entity = store.GetPlayer(id)!;
        entity.ApplyDamage(200f); // 300 -> 100 (<50%)

        MonsterSkillConfig? midRangeConfig = null;
        foreach (var skill in state.Config.skills)
            if (skill.skillId == midRangeSkill) midRangeConfig = skill;
        Assert.NotNull(midRangeConfig);

        var player = Login(store, 1, fourMeters);
        var resolver = new CombatResolver(store, new Random(42));
        var casts = new List<(uint Tick, int SkillId, float Distance)>();
        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            foreach (var (npcId, request) in module.DrainPendingCasts())
            {
                resolver.Decide(npcId, request, t);
                casts.Add((t, request.SkillId, Vector3.Distance(state.Position, player.Position)));
            }
            resolver.DrainResolved(t);
        }

        Assert.Contains(casts, c => c.SkillId == midRangeSkill);
        Assert.DoesNotContain(casts, c => c.SkillId == meleeSkill);
        Assert.True(Cool.SyncCore.CombatRules.TryGet(midRangeSkill, out var midRangeEntry));
        foreach (var cast in casts)
        {
            if (cast.SkillId != midRangeSkill) continue;
            Assert.InRange(cast.Distance,
                midRangeConfig!.minRange, midRangeConfig.MaxRangeOf(midRangeEntry));
        }

        // 50005 冷却 4s = 60 tick：相邻两次 50005 的 tick 差至少 60
        var midRangeTicks = new List<uint>();
        foreach (var cast in casts)
            if (cast.SkillId == midRangeSkill) midRangeTicks.Add(cast.Tick);
        for (int i = 1; i < midRangeTicks.Count; i++)
            Assert.True(midRangeTicks[i] - midRangeTicks[i - 1] >= 60,
                $"50005 冷却不足：间隔 {midRangeTicks[i] - midRangeTicks[i - 1]} tick");
    }

    [Fact]
    public void Evade_Resets_At_Home_And_Target_Death_Prefers_Other_Threat()
    {
        var (store, module, id) = Make();
        var state = module.GetState(id)!;
        var first = Login(store, 1, new Vector3(0f, 0f, 2f));   // 距出生点 3m
        Login(store, 2, new Vector3(0f, 0f, 1.5f));             // 距出生点 3.5m

        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 50f);
        first.ApplyDamage(999f);

        module.Tick(0);
        Assert.Equal(2L, state.TargetId); // 杀死当前目标后切到另一个
        Assert.Equal(MonsterMode.Combat, state.Mode);

        // ③ 脱战：把怪物推到出生点 23.2m 外（Slime leashRadius=23）→ 立即 Evade、清空仇恨。
        state.Position = new Vector3(0f, 0f, 28.2f);
        module.Tick(1);
        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);

        // Evade 期间受击不重新仇恨（怒气/血量照常，但不得把玩家拉回威胁表）。
        var monster = store.GetPlayer(id)!;
        monster.ApplyDamage(50f); // 100 -> 50，用于验证到家满血
        module.OnDamageTaken(id, 2, 25f, MonsterDamageKind.Direct);
        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);

        uint t = 2;
        while (t < 600 && state.Mode != MonsterMode.Patrol) { module.Tick(t); t++; }
        uint arrivalTick = t - 1;

        Assert.Equal(MonsterMode.Patrol, state.Mode);
        Assert.Equal(monster.MaxHp, monster.Hp, 3); // 到家满血
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
        Assert.Equal(0, state.CurrentPatrolIndex);
        Assert.Equal(30u, MonsterBrain.PostEvadeGraceTicks);
        Assert.Equal(arrivalTick + 30u, state.PostEvadeGraceUntilTick); // 精确 +30

        // grace 内玩家就在视野锥里也不感知
        module.Tick(arrivalTick + 1);
        Assert.Equal(MonsterMode.Patrol, state.Mode);
    }
}
