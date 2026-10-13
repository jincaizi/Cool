using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 2026-10-13：怪物**身体半径**的跨端一致性守卫。
    ///
    /// 背景：服务端原本没有"身体半径"这个概念（AOI/快照/移动都不需要它）。ORCA 避让接线后
    /// `monster_config.json` 的 `bodyRadius` 成了服务端唯一的半径来源，而客户端那条身体碰撞体
    /// （<c>NpcBodyCollider</c>）是**从模型 bounds 推**的——两边各记一份就会漂移，
    /// 表现是"客户端挡在 1.74m、服务端认为 1.60m"：贴着怪走会被服务端轻微来回推。
    ///
    /// 本项目例钉住两边同值：
    ///   · 目录里有真模型的模板（3 DogKnight / 4 SpiderBoss）→ 预制体模型的 **XZ 半宽**
    ///     （合并 SkinnedMeshRenderer 世界 bounds 的 **X/Z 跨度取大者** / 2）≈ json 的 bodyRadius；
    ///   · 目录里没有模型的模板（1 Slime / 2 Wolf）→ 客户端退化为球体占位
    ///     （半径 0.5，RemoteEntityRegistry.CreateView 的 PrimitiveType.Sphere）≈ json 的 bodyRadius。
    ///
    /// 目录来源：那 4 个模板 → 预制体的映射不在资产里，配在**场景**上
    /// （`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 的 `ClientNetworkHost._npcPrefabs`，
    /// 只有 TemplateId 3 / 4 两条）——所以这里显式列出，与场景同步改（同 NpcBodyColliderTests 的风格）。
    /// </summary>
    public class MonsterBodyRadiusTests
    {
        /// <summary>容差：两侧手工录入的漂移警戒线（不是精度上限——实测两侧差值 ≤ 0.004）。
        /// **0.05 是刻意的**：X 跨度与 XZ 取大者之间的差是 0.08m，容差放宽到 0.1 就分不出
        /// "服务端抄的是哪条约定"了（实测：0.1 时按 X-only 量也能过）。</summary>
        private const float Tolerance = 0.05f;

        /// <summary>球体占位的半径（RemoteEntityRegistry 用 CreatePrimitive(Sphere)：单位球 → 0.5，缩放 1）</summary>
        private const float PlaceholderRadius = 0.5f;

        /// <summary>(模板 id, 客户端预制体路径)；null = 该模板在场景目录里没有模型，走球体占位</summary>
        private static readonly (int TemplateId, string? PrefabPath)[] Catalog =
        {
            (1, null),                                                                 // Slime   —— 场景目录未接线
            (2, null),                                                                 // Wolf    —— 场景目录未接线
            (3, "Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab"),            // DogKnight
            (4, "Assets/Monstor/fantasySpider/SpiderMonster.prefab"),                  // SpiderBoss
        };

        [Serializable]
        private class MonsterConfigDto
        {
            public MonsterEntryDto[] monsters = Array.Empty<MonsterEntryDto>();
        }

        [Serializable]
        private class MonsterEntryDto
        {
            public int templateId;
            public string name = string.Empty;
            public float bodyRadius;
        }

        private static string ConfigPath =>
            Path.Combine(Directory.GetCurrentDirectory(), "Server", "Config", "monster_config.json");

        private static MonsterConfigDto LoadServerConfig()
        {
            Assert.IsTrue(File.Exists(ConfigPath), "服务端怪物配置应存在于仓库相对路径：" + ConfigPath);
            var dto = JsonUtility.FromJson<MonsterConfigDto>(File.ReadAllText(ConfigPath));
            Assert.IsNotNull(dto, "monster_config.json 应能解析");
            Assert.IsNotEmpty(dto.monsters, "monster_config.json 应至少有一个模板");
            return dto;
        }

        /// <summary>
        /// 模型推出来的 XZ 半宽（**世界米**——服务端的位置/半径都在世界坐标系里）：
        /// 合并 SkinnedMeshRenderer 的**世界** bounds，取 X/Z 两个跨度里**大的那个**的一半。
        /// 取 max 而不是只取 X：客户端那条身体碰撞体（NpcBodyCollider）建的是 **X/Z 方盒包络**
        /// （`box.size = (X 跨度, Y 跨度, Z 跨度)`）——只按 X 算会让服务端圆比客户端身体每侧小一截
        /// （实测 SpiderBoss 半宽：X 1.6619 < Z 1.7419，差 0.08m），表现是贴着怪走仍被服务端轻微推。
        /// 根上的缩放（Boss 2×）已经折在世界 bounds 里，**不再除回 lossyScale**——NpcBodyCollider
        /// 除回是为了拿本地尺寸建 BoxCollider，它的世界跨度仍然是 3.48m，与本用例同一把尺子。
        /// </summary>
        private static float ModelHalfWidth(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.Greater(renderers.Length, 0, root.name + "：应有 SkinnedMeshRenderer");

            var world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

            return Mathf.Max(world.size.x, world.size.z) * 0.5f;
        }

        [Test]
        public void Server_Body_Radius_Matches_The_Client_Model_For_Every_Monster_Template()
        {
            // Arrange
            var config = LoadServerConfig();

            // Act + Assert：每个模板都要在服务端表里有一条，且与客户端"渲染出来的东西"同宽
            foreach (var (templateId, prefabPath) in Catalog)
            {
                var entry = Array.Find(config.monsters, m => m.templateId == templateId);
                Assert.IsNotNull(entry, $"服务端表缺少模板 {templateId}（客户端目录里有它）");
                Assert.Greater(entry.bodyRadius, 0f, $"模板 {templateId} 的 bodyRadius 必须 > 0（ORCA 拿它当半径）");

                float clientHalfWidth;
                if (prefabPath == null)
                {
                    // 无模型 ⇒ 球体占位：半径就是 CreatePrimitive(Sphere) 的单位球半径
                    clientHalfWidth = PlaceholderRadius;
                }
                else
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    Assert.IsNotNull(prefab, "预制体缺失：" + prefabPath);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab!);
                    try
                    {
                        clientHalfWidth = ModelHalfWidth(instance);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }

                Assert.AreEqual(clientHalfWidth, entry.bodyRadius, Tolerance,
                    $"{entry.name}（模板 {templateId}）：客户端半宽 {clientHalfWidth:F3} vs 服务端 bodyRadius " +
                    $"{entry.bodyRadius:F3} —— 两边漂移了（客户端挡在 {clientHalfWidth:F2}m、服务端按 " +
                    $"{entry.bodyRadius:F2}m 避让，贴着走会被轻微来回推）");
            }
        }

        [Test]
        public void Placeholder_Radius_Is_Really_Half_A_Meter()
        {
            // 上一条把"目录未命中的模板"按 0.5 判——这条钉住那个 0.5 不是拍脑袋来的：
            // RemoteEntityRegistry 的降级路径就是 GameObject.CreatePrimitive(PrimitiveType.Sphere) 的原尺寸
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                Assert.AreEqual(1f, sphere.transform.localScale.x, "占位球应是原尺寸（缩放 1）");
                var mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh, "占位球应带内置 Sphere 网格");
                Assert.AreEqual(PlaceholderRadius, mesh!.bounds.extents.x, 1e-4f,
                    "内置 Sphere 的半径 = 0.5（CreatePrimitive 的约定）");
                Assert.AreEqual(PlaceholderRadius, mesh.bounds.extents.z, 1e-4f,
                    "XZ 两个方向都取 0.5");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sphere);
            }
        }
    }
}
