using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// P3：AOI 下视野外实体自然停流——registry 按缺席超时销毁视图（清插值缓冲）。
    /// 1.2s 无任何快照样本即删（PlayerLeaveRoom 仍是即时删除路径）。
    /// </summary>
    public class RemoteEntityRegistryTests
    {
        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("RegistryTestRoot");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private static EntitySnapshot Snap(long id, float x, float z)
        {
            return new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = x, Y = 0, Z = z,
                Rotation = 0, Tick = 1
            };
        }

        [Test]
        public void Entity_Not_Expired_While_Receiving_Snapshots()
        {
            _registry.ApplySnapshot(Snap(1, 0, 0), serverTicks: 1_000_000L);

            // 持续收快照永不超时（每次推进后刷新样本时间）
            for (int i = 0; i < 100; i++)
            {
                _registry.ApplySnapshot(Snap(1, i, 0), serverTicks: 2_000_000L + i * 100_000L);
                _registry.UpdateExpiry(nowServerTicks: 2_000_000L + i * 100_000L);
            }
            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void Entity_Expired_After_1_2s_Without_Snapshots()
        {
            _registry.ApplySnapshot(Snap(1, 0, 0), serverTicks: 1_000_000L);
            Assert.AreEqual(1, _registry.Count);

            // 服务器时钟推进 1.3s，无新快照
            _registry.UpdateExpiry(nowServerTicks: 1_000_000L + 13_000_000L);

            Assert.AreEqual(0, _registry.Count);
            Assert.IsNull(_registry.GetView(1));
        }

        [Test]
        public void Multi_Entity_Independent_Expiry()
        {
            _registry.ApplySnapshot(Snap(1, 0, 0), serverTicks: 1_000_000L);
            _registry.ApplySnapshot(Snap(2, 10, 0), serverTicks: 1_000_000L);
            _registry.ApplySnapshot(Snap(3, 20, 0), serverTicks: 1_000_000L);

            // 实体 2 持续刷新到最后时刻（19M），1/3 停流于 1M
            _registry.ApplySnapshot(Snap(2, 11, 0), serverTicks: 19_000_000L);
            _registry.UpdateExpiry(nowServerTicks: 20_000_000L); // 1/3 差 19M ticks = 1.9s > 1.2s 删；2 差 1M = 0.1s 保留

            Assert.AreEqual(1, _registry.Count);
            Assert.IsNotNull(_registry.GetView(2));
        }

        [Test]
        public void Npc_Snapshot_Creates_Monster_View_With_Template()
        {
            var root = new GameObject("NpcRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2_000_001,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Kind | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                    X = 12f, Z = 3f, Rotation = 90f,
                    Kind = (byte)EntityKind.Npc,
                    TemplateId = 2,
                    ActionState = (byte)NpcAnimationState.Run
                }, serverTicks: 1_000L);

                var view = registry.GetView(2_000_001);
                Assert.IsNotNull(view);
                Assert.AreEqual((byte)EntityKind.Npc, view!.Kind);
                Assert.AreEqual(2, view.TemplateId);
                Assert.AreEqual((byte)NpcAnimationState.Run, view.ActionState);
                Assert.IsTrue(view.GameObject.name.Contains("Monster"), $"view name '{view.GameObject.name}' should mark monster");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Player_Snapshot_Still_Creates_Player_View()
        {
            var root = new GameObject("PlayerRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 5,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Kind),
                    X = 1f, Z = 2f,
                    Kind = (byte)EntityKind.Player
                }, serverTicks: 1_000L);

                var view = registry.GetView(5);
                Assert.IsNotNull(view);
                Assert.AreEqual((byte)EntityKind.Player, view!.Kind);
                Assert.IsTrue(view.GameObject.name.Contains("RemotePlayer"));
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Npc_View_Uses_The_Catalog_Prefab_When_Present()
        {
            var prefab = new GameObject("FakeMonsterPrefab");
            prefab.AddComponent<Animator>();
            var root = new GameObject("CatalogRoot");
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { 3, prefab } });
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2_000_002,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                    X = 1f, Z = 1f, Kind = (byte)EntityKind.Npc, TemplateId = 3, Tick = 1
                }, serverTicks: 1_000_000L);

                var view = registry.GetView(2_000_002);
                Assert.IsNotNull(view);
                Assert.IsTrue(view!.GameObject.name.StartsWith("FakeMonsterPrefab"),
                    $"命中目录应实例化真预制体，实际是 {view.GameObject.name}");
                Assert.IsNotNull(view.NpcAnimator, "registry 应在实例化出的怪物根上补上驱动组件");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Npc_View_Falls_Back_To_The_Sphere_When_The_Template_Is_Missing()
        {
            var root = new GameObject("FallbackRoot");
            var registry = new RemoteEntityRegistry(root.transform, null, null);
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2_000_003,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                    X = 1f, Z = 1f, Kind = (byte)EntityKind.Npc, TemplateId = 99, Tick = 1
                }, serverTicks: 1_000_000L);

                var view = registry.GetView(2_000_003);
                // StartsWith：降级名是 `Monster_{id}_T{t}`，而真预制体的名字带目录前缀
                //（`DogKnightMonster_…` / `SpiderMonster_…`）——`Contains("Monster_")` 两者都命中，判不出来
                Assert.IsTrue(view!.GameObject.name.StartsWith("Monster_"),
                    $"未命中目录要保持球体降级，实际 {view.GameObject.name}");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Npc_View_Falls_Back_To_The_Sphere_When_The_Catalog_Lacks_The_Template()
        {
            // 目录**非空**但缺该 templateId（球体降级的另一条真分支——传 null 目录覆盖不到）
            var prefab = new GameObject("FakeMonsterPrefab");
            prefab.AddComponent<Animator>();
            var root = new GameObject("MissingTemplateRoot");
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { 3, prefab } });   // 只配了 t3
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2_000_005,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                    X = 1f, Z = 1f, Kind = (byte)EntityKind.Npc, TemplateId = 4, Tick = 1
                }, serverTicks: 1_000_000L);

                var view = registry.GetView(2_000_005);
                Assert.IsNotNull(view);
                Assert.IsTrue(view!.GameObject.name.StartsWith("Monster_"),
                    $"目录缺该 templateId 时应退化为球体，实际 {view.GameObject.name}");
                Assert.IsNull(view.NpcAnimator, "球体降级没有可驱动的东西（排程对它也只能空转）");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void A_Scheduled_Attack_For_A_Destroyed_View_Is_Harmless()
        {
            var root = new GameObject("DropRoot");
            var prefab = new GameObject("FakeMonsterPrefab");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { 3, prefab } });
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2_000_004,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                    X = 0f, Z = 0f, Kind = (byte)EntityKind.Npc, TemplateId = 3, Tick = 1
                }, serverTicks: 1_000_000L);

                registry.ScheduleAttack(2_000_004, serverTimestamp: 1_000_000L + 500L * 10_000L);
                registry.Remove(2_000_004);

                // 排程项到点时视图已不在 —— 不得抛异常，也不得把视图复活
                Assert.DoesNotThrow(() => registry.UpdateRender(
                    nowServerTicks: 1_000_000L + 1_000L * 10_000L, renderDelayTicks: 0L));
                Assert.IsNull(registry.GetView(2_000_004), "排程项不得复活已销毁的视图");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Npc_View_Keeps_Sampled_State_Instead_Of_Arrival_State()
        {
            // 采样路径：视图的 ActionState 由 UpdateRender 在 renderAt 处采出
            //（ApplySnapshot 仍写 view.ActionState 作为"最后已知值"，供测试与观察用）
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_001,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.ActionState | EntityFieldMask.TemplateId),
                X = 0f, Z = 0f, Kind = (byte)EntityKind.Npc, TemplateId = 2,
                ActionState = (byte)NpcAnimationState.Run, Tick = 1
            }, serverTicks: 1_000_000L);

            Assert.IsTrue(_registry.TrySampleNpcState(2_000_001, renderAt: 1_000_000L,
                out var state, out var dead));
            Assert.AreEqual((byte)NpcAnimationState.Run, state);
            Assert.IsFalse(dead);
        }

        /// <summary>
        /// ORCA 半径真值源（design 任务 8；2026-10-13 **人类裁定改锚**）：服务端 `BotRunner.BotRadius`
        /// 必须等于**客户端角色预制体根上的 `CharacterController.radius`**——那是项目对"角色身体半径"
        /// 的既有定义，服务端给**同款模型的真人**用的 `AvoidanceSystem.PlayerRadius` 也是这个口径
        /// （bot 与真人同一个模型 ⇒ 半径必须一致）。
        /// 曾按 SMR 包围盒量出 0.7469（绑定姿态、含张开的双臂）——**已被裁定否决，不采用**。
        ///
        /// 第二条守卫：场景必须**真的**把这个预制体当默认外观（`_playerAvatarPrefab` 的 guid 对得上
        /// `.meta`），否则本用例钉住的是一个不再被 bot 使用的模型而不自知。
        ///
        /// 为什么读源码文本而不是引用常量：`Server/Server3C.csproj` 是 Assets/ 之外的 .NET 工程，
        /// Unity 测试程序集引用不到它。项目已有"断言文件文本"的先例（`NpcBodyColliderTests` 读
        /// TagManager 资产），这里同款处理。
        /// </summary>
        [Test]
        public void Bot_Radius_Matches_The_Character_Controller_Radius()
        {
            const string PrefabPath = "Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab";
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            var rootCollider = prefab.GetComponent<CharacterController>();
            Assert.IsNotNull(rootCollider, PrefabPath + " 根上应有 CharacterController（真值源就是它的 radius）");
            Assert.AreEqual(1, prefab.GetComponentsInChildren<CharacterController>(true).Length,
                PrefabPath + " 上应只有根这一个 CharacterController——多个会让真值源有歧义，先裁定再改本用例");
            float colliderRadius = rootCollider.radius;

            // 路径基座用 Application.dataPath（= Assets/）而不是 CWD：测试宿主不保证 CWD 落在项目根
            string projectRoot = System.IO.Path.Combine(Application.dataPath, "..");

            // 场景接线：默认外观必须就是上面这个预制体（读场景/`.meta` 的**文件文本**，同 NpcBodyColliderTests）
            string scenePath = System.IO.Path.Combine(projectRoot,
                "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity");
            Assert.IsTrue(System.IO.File.Exists(scenePath), "宿主场景应存在：" + scenePath);
            var wiring = System.Text.RegularExpressions.Regex.Match(
                System.IO.File.ReadAllText(scenePath),
                @"_playerAvatarPrefab:\s*\{[^}]*guid:\s*([0-9a-f]{32})");
            Assert.IsTrue(wiring.Success,
                "没在 DemoDay.unity 里解析到 _playerAvatarPrefab 的 guid——字段改名了？同步更新本用例");
            var metaGuid = System.Text.RegularExpressions.Regex.Match(
                System.IO.File.ReadAllText(System.IO.Path.Combine(projectRoot, PrefabPath + ".meta")),
                @"guid:\s*([0-9a-f]{32})");
            Assert.IsTrue(metaGuid.Success, PrefabPath + ".meta 应解析到 guid");
            Assert.AreEqual(metaGuid.Groups[1].Value, wiring.Groups[1].Value,
                "场景的默认外观换了 ⇒ 来更新本守卫的模型路径与半径结论；" +
                "若已引入多外观目录，把本守卫改为遍历目录条目");

            string sourcePath = System.IO.Path.Combine(Application.dataPath, "..",
                "Server", "Aoi", "BotRunner.cs");
            Assert.IsTrue(System.IO.File.Exists(sourcePath), "服务端源码应存在：" + sourcePath);
            string source = System.IO.File.ReadAllText(sourcePath);
            var match = System.Text.RegularExpressions.Regex.Match(source, @"BotRadius\s*=\s*([0-9.]+)f");
            Assert.IsTrue(match.Success,
                "没在 Server/Aoi/BotRunner.cs 里找到 BotRadius 常量——常量改名了？同步更新本用例");
            float orcaRadius = float.Parse(match.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual(colliderRadius, orcaRadius,
                $"ORCA 半径 {orcaRadius}m 必须 == 预制体 CharacterController.radius {colliderRadius}m——" +
                "不一致 ⇒ bot 与真人的避让半径不同（同一个模型却是两个身体半径），间距观感错");
        }
    }
}
