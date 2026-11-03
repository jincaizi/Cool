using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterCastBuilderTests
{
    private static (MonsterState State, SelectedSkill Selected) Make(int skillId, int priority = 0,
        float minRange = 0f, float maxRange = 0f, float hpBelow = 1f)
    {
        var config = new MonsterData
        {
            templateId = 1, name = "T", hp = 100f, moveSpeed = 2f, bodyRadius = 0.5f,
            detectionRadius = 10f, visionAngle = 120f, attackRange = 2f, patrolRadius = 2f,
            skills = new System.Collections.Generic.List<MonsterSkillConfig>
            {
                new MonsterSkillConfig { skillId = skillId, priority = priority,
                    minRange = minRange, maxRange = maxRange, hpBelow = hpBelow }
            }
        };
        var state = new MonsterState(1, 1, config) { TargetId = 7L, Position = new Vector3(0f, 0f, 0f) };
        Assert.True(CombatRules.TryGet(skillId, out var entry));
        var selected = new SelectedSkill(config.skills[0], entry!);
        return (state, selected);
    }

    [Fact]
    public void Single_Shape_Uses_Config_Range_Within_Table_Limit()
    {
        var (state, selected) = Make(50005, maxRange: 6f);
        Assert.True(MonsterCastBuilder.TryBuild(state, selected, new Vector3(0f, 0f, 3f), out var request));
        Assert.Equal(50005, request.SkillId);
        Assert.Equal(7L, request.TargetPlayerId);
        Assert.Equal(0, request.Shape.ShapeType);
        Assert.Equal(6f, request.Shape.Range, 3);
        Assert.Equal(selected.Entry.MaxAngle, request.Shape.Angle, 3);
    }

    [Fact]
    public void Circle_Shape_Uses_Table_Area_Radius()
    {
        var (state, selected) = Make(50006, maxRange: 4f);
        Assert.True(MonsterCastBuilder.TryBuild(state, selected, new Vector3(0f, 0f, 2f), out var request));
        Assert.Equal(1, request.Shape.ShapeType);
        Assert.Equal(4f, request.Shape.AreaRadius, 3);
        Assert.Equal(0f, request.Shape.Range, 3);
    }

    [Fact]
    public void Projectile_Shape_Is_Zero_And_Impact_Is_Clamped()
    {
        var (state, selected) = Make(30001, maxRange: 2f);
        Assert.True(MonsterCastBuilder.TryBuild(state, selected, new Vector3(0f, 0f, 100f), out var request));
        Assert.Equal(0, request.Shape.ShapeType);
        Assert.Equal(0f, request.Shape.Range, 3);
        float dx = request.ImpactX, dz = request.ImpactZ;
        Assert.Equal(30f, Math.Sqrt(dx * dx + dz * dz), 2); // MaxImpactDistance=30
        Assert.True(CombatRules.TryGet(30001, out var entry));
        Assert.True(CombatRules.ValidateShape(request.Shape, entry!));
    }
}
