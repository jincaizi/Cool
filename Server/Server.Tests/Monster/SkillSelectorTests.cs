using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class SkillSelectorTests
{
    private static (MonsterState State, PlayerState Self) Make(float hp, float maxHp = 100f)
    {
        var config = new MonsterData
        {
            templateId = 3, name = "DogKnight", hp = maxHp, moveSpeed = 2.8f, bodyRadius = 0.88f,
            detectionRadius = 18f, visionAngle = 120f, attackRange = 2.2f, patrolRadius = 6f,
            leashRadius = 34f,
            skills = new List<MonsterSkillConfig>
            {
                new MonsterSkillConfig { skillId = 50003, priority = 0, minRange = 0f, maxRange = 2.2f },
                new MonsterSkillConfig { skillId = 50005, priority = 10, minRange = 2f, maxRange = 6f, hpBelow = 0.5f }
            }
        };
        var store = new EntityStore();
        var self = store.AddNpc(1, "self", 3, maxHp, new Vector3(0f, 0f, 0f));
        self.ApplyDamage(maxHp - hp);
        var state = new MonsterState(1, 3, config);
        return (state, self);
    }

    [Fact]
    public void Selects_Higher_Priority_Skill_When_Both_In_Range()
    {
        var (state, self) = Make(hp: 40f); // 低于 50%
        var selected = SkillSelector.Select(state, self, distance: 3f, tick: 0);
        Assert.NotNull(selected);
        Assert.Equal(50005, selected!.Value.Entry.SkillId);
    }

    [Fact]
    public void Skips_High_Priority_Skill_Above_Hp_Threshold()
    {
        var (state, self) = Make(hp: 80f);
        var selected = SkillSelector.Select(state, self, distance: 3f, tick: 0);
        Assert.Null(selected); // 50003 只够 2.2m，50005 血量条件不满足
    }

    [Fact]
    public void Skips_Skill_On_Cooldown()
    {
        var (state, self) = Make(hp: 40f);
        self.RecordCast(50005, tick: 0);
        var selected = SkillSelector.Select(state, self, distance: 3f, tick: 5);
        Assert.Null(selected); // 50005 冷却 4s=60 tick；50003 够不到 3m
    }

    [Fact]
    public void Selects_Melee_When_Target_Inside_Melee_Range()
    {
        var (state, self) = Make(hp: 40f);
        var selected = SkillSelector.Select(state, self, distance: 1.5f, tick: 0);
        Assert.NotNull(selected);
        Assert.Equal(50003, selected!.Value.Entry.SkillId); // 50005 minRange=2 排除了 1.5m
    }

    [Fact]
    public void NeedsReposition_When_Target_Inside_All_MinRanges()
    {
        var config = new MonsterData
        {
            templateId = 9, name = "RangedOnly", hp = 100f, moveSpeed = 2f, bodyRadius = 0.5f,
            detectionRadius = 10f, visionAngle = 120f, attackRange = 1f, patrolRadius = 2f,
            skills = new List<MonsterSkillConfig>
            {
                new MonsterSkillConfig { skillId = 50005, priority = 0, minRange = 2f, maxRange = 6f }
            }
        };
        var store = new EntityStore();
        var self = store.AddNpc(1, "self", 9, 100f, new Vector3(0f, 0f, 0f));
        var state = new MonsterState(1, 9, config);

        Assert.True(SkillSelector.NeedsReposition(state, self, distance: 1f));
        Assert.Equal(2f, SkillSelector.RepositionDistance(state, self, distance: 1f), 3);
        Assert.False(SkillSelector.NeedsReposition(state, self, distance: 3f));
    }
}
