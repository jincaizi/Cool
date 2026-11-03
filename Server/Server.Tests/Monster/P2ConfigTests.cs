using System;
using System.Collections.Generic;
using System.IO;
using Cool.SyncCore;
using KcpServer.Config;
using Xunit;

namespace KcpServer.Tests.Monster;

public class P2ConfigTests
{
    private static Dictionary<int, MonsterData> Published()
        => ConfigLoader.LoadMonsterTemplates(
            Path.Combine(AppContext.BaseDirectory, "Config", "monster_config.json"));

    [Fact]
    public void Published_Leash_Is_Greater_Than_Detection_Plus_Patrol()
    {
        foreach (var template in Published().Values)
        {
            Assert.True(template.LeashRadiusOf > template.detectionRadius + template.patrolRadius,
                $"{template.name}: leash={template.LeashRadiusOf} <= detection+patrol="
                + $"{template.detectionRadius + template.patrolRadius}");
            Assert.True(template.DropAggroRadiusOf >= template.LeashRadiusOf);
        }
    }

    [Fact]
    public void Published_Skills_Exist_And_Fit_Combat_Rules()
    {
        foreach (var template in Published().Values)
        {
            Assert.NotEmpty(template.skills);
            foreach (var skill in template.skills)
            {
                Assert.True(CombatRules.TryGet(skill.skillId, out var entry),
                    $"{template.name}: skill {skill.skillId} missing from CombatRules");
                float maxRange = skill.MaxRangeOf(entry!);
                Assert.True(skill.minRange <= maxRange + 0.01f,
                    $"{template.name}: skill {skill.skillId} minRange={skill.minRange} > maxRange={maxRange}");
                if (entry.ShapeType == 0)
                    Assert.True(maxRange <= entry.MaxRange + 0.01f,
                        $"{template.name}: skill {skill.skillId} maxRange={maxRange} > table {entry.MaxRange}");
            }
        }
    }

    [Fact]
    public void DogKnight_And_SpiderBoss_Ship_Multi_Skill()
    {
        var templates = Published();
        Assert.True(templates[3].skills.Count >= 2);
        Assert.True(templates[4].skills.Count >= 2);
        Assert.Contains(templates[3].skills, s => s.skillId == 50005 && s.hpBelow < 1f);
        Assert.Contains(templates[4].skills, s => s.skillId == 50006 && s.hpBelow < 1f);
    }
}
