using KcpServer.Config;
using KcpServer.Monster;
using KcpServer.Movement;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// 2026-10-13（接线审查 Important A）：**服务端侧**的身体半径钉子。
///
/// 客户端的 `MonsterBodyRadiusTests`（EditMode）钉的是"json ↔ 客户端模型"，钉不住
/// "json 被服务端正确读进 MonsterData"——`MonsterData` 全是 public 字段，只有
/// `ConfigLoader.IncludeFields = true` 才反序列化得到（本仓库踩过整表静默落 0 的坑）。
/// 一旦这个开关被去掉、json 键改名、或新模板漏填，半径会**静默**退化成兜底 0.5
/// （SpiderBoss 从 1.74 → 0.5，客户端身体还是 3.48m 宽）⇒ 可见穿模，而当时全部测试仍然全绿。
/// 所以这里从**构建输出目录**的同一份 json 开始，逐层断言到模块的查表结果。
/// </summary>
public class MonsterBodyRadiusTests
{
    /// <summary>四模板的实测半径：0.5/0.5 是客户端球体占位，0.88/1.74 是模型 XZ 跨度取大者的一半
    /// （量法见客户端 MonsterBodyRadiusTests 与 docs/superpowers/plans/2026-10-13-orca-avoidance-report.md）。</summary>
    [Theory]
    [InlineData(1, 0.5f)]
    [InlineData(2, 0.5f)]
    [InlineData(3, 0.88f)]
    [InlineData(4, 1.74f)]
    public void Shipped_Json_Carries_The_Measured_Body_Radius(int templateId, float expected)
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Config", "monster_config.json");
        var config = ConfigLoader.LoadMonsterTemplates(path)[templateId];

        Assert.True(config.bodyRadius > 0f,
            $"模板 {templateId} 的 bodyRadius 落 0 —— 多半是 IncludeFields 被关掉 / json 键改名（静默失效）");
        Assert.Equal(expected, config.bodyRadius, 0.001f);
    }

    [Theory]
    [InlineData(1, 0.5f)]
    [InlineData(2, 0.5f)]
    [InlineData(3, 0.88f)]
    [InlineData(4, 1.74f)]
    public void Module_Resolves_The_Same_Radius_It_Shipped(int templateId, float expected)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);

        Assert.Equal(expected, module.RadiusOfTemplate(templateId), 0.001f);
    }

    /// <summary>表里查不到 → 兜底 0.5（不抛：新模板漏填不该让整台服务器停摆），且**归一**：
    /// 生成出来的怪身上读到的半径与邻居侧查表结果必须同值——两条路一旦不一致，合并半径就减半。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Spawned_Monster_Self_Radius_Equals_What_Neighbours_Resolve(int templateId)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long npcId = module.SpawnMonster(templateId, new Vector3(5f, 0f, 5f), new Vector3(5f, 0f, 5f));

        var state = module.GetState(npcId)!;
        Assert.Equal(module.RadiusOfTemplate(templateId), state.Config.bodyRadius, 0.0001f);
    }

    /// <summary>
    /// 兜底必须**落到配置对象上**（装配时归一）：非法值（0）时自身半径与邻居侧查表都要是 0.5。
    /// 只覆盖"合法值"是钉不住归一的——两条路径本来就同值；这里用一份临时的坏 json
    /// （bodyRadius 缺失 ⇒ 反序列化落 0）把"自身 0 / 邻居 0.5"的不对称逼出来
    /// （审查 Important A：合并半径减半，避让形同虚设）。
    /// </summary>
    [Fact]
    public void Invalid_Config_Is_Normalised_So_Self_And_Neighbour_Radii_Agree()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"monsters_bad_{System.Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path,
            "{\"monsters\":[{\"templateId\":3,\"name\":\"NoRadius\",\"hp\":10,\"moveSpeed\":1.0," +
            "\"detectionRadius\":5,\"visionAngle\":90,\"attackRange\":1.0,\"patrolRadius\":2,\"skills\":[\"Attack\"]}]}");
        try
        {
            var store = new EntityStore();
            long seed = MonsterModule.NpcIdSeed;
            var module = new MonsterModule(store, () => ++seed, configPath: path);
            long npcId = module.SpawnMonster(3, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));  // 3 有技能映射，能生成

            // 坏表不污染别的模块（各自反序列化自己的副本，归一只写自己那份）
            var goodModule = new MonsterModule(new EntityStore(), () => ++seed);
            Assert.Equal(1.74f, goodModule.RadiusOfTemplate(4), 0.001f);

            var state = module.GetState(npcId)!;
            Assert.Equal(0.5f, module.RadiusOfTemplate(3), 0.0001f);   // 漏填 ⇒ 兜底
            Assert.Equal(module.RadiusOfTemplate(3), state.Config.bodyRadius, 0.0001f);  // 归一：两条路同值
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Unknown_Template_Falls_Back_To_The_Placeholder_Radius()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);

        Assert.Equal(0.5f, AvoidanceSystem.DefaultMonsterRadius);   // 兜底值本身也是被钉住的契约
        Assert.Equal(0.5f, module.RadiusOfTemplate(9999), 0.0001f);
    }
}
