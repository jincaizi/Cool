using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 2026-10-13：怪物视图的运行时碰撞体（**纯客户端**——服务端不模拟玩家移动、也不校验重叠，
    /// 改客户端仍可穿模；见 docs/superpowers/guides/webgl-demo-boundaries.md 的边界条目）。
    ///
    /// 规则：
    ///   · Kind==Npc 且命中真模型目录 → 从 SkinnedMeshRenderer 的 bounds 推一个竖直棱柱
    ///     （BoxCollider：XZ 跨度 + Y 跨度；不是胶囊——穹顶会被 CC 爬上去），挂在子物体 `NpcBody` 上，
    ///     层 = `NpcBody`；
    ///   · 层矩阵只允许它与**本地玩家层**碰撞（其余层全关）——尺寸/层都随模型与预制体走，不写死；
    ///   · 真人 / bot 视图不碰（RemoteEntityAnimator 那套禁用碰撞体的语义保持原样）；
    ///   · 预制体本身保持纯美术（无碰撞体）——资产断言。
    /// </summary>
    public class NpcBodyColliderTests
    {
        private const string DogKnightPrefab = "Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab";
        private const string SpiderPrefab = "Assets/Monstor/fantasySpider/SpiderMonster.prefab";

        private GameObject _root = null!;

        [SetUp]
        public void SetUp() => _root = new GameObject("NpcBodyColliderTestRoot");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        /// <summary>假怪物预制体：一个 Animator（装配契约）+ 一个 SMR，bounds 由调用方指定。</summary>
        private static GameObject FakeMonsterPrefab(Bounds localBounds)
        {
            var prefab = new GameObject("FakeMonsterPrefab");
            prefab.AddComponent<Animator>();
            var smr = prefab.AddComponent<SkinnedMeshRenderer>();
            smr.localBounds = localBounds;
            return prefab;
        }

        private static RemoteEntityView CreateNpcView(RemoteEntityRegistry registry, int templateId, long playerId)
        {
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                X = 1f, Y = 0f, Z = 2f, Kind = (byte)EntityKind.Npc, TemplateId = templateId, Tick = 1
            }, serverTicks: 1_000_000L);
            return registry.GetView(playerId)!;
        }

        [Test]
        public void Monster_View_Gets_A_Body_Collider_Sized_From_The_Model()
        {
            // 模型 bounds：中心 (0.5,1,-0.25)、尺寸 (2,3,1) → 棱柱 2×3×1（XZ 跨度 + Y 跨度，一步不差）
            var prefab = FakeMonsterPrefab(new Bounds(new Vector3(0.5f, 1f, -0.25f), new Vector3(2f, 3f, 1f)));
            var registry = new RemoteEntityRegistry(_root.transform, null,
                new Dictionary<int, GameObject> { { 3, prefab } });
            try
            {
                var view = CreateNpcView(registry, 3, 2_000_001);
                var bodyCollider = view.GameObject.GetComponentInChildren<NpcBodyCollider>(true);
                Assert.IsNotNull(bodyCollider, "怪物视图应补上运行时身体碰撞体组件");
                bodyCollider!.BuildCollider();   // EditMode：AddComponent 不触发生命周期

                var box = bodyCollider.Collider;
                Assert.IsNotNull(box, "应从模型 bounds 建出碰撞体");
                Assert.AreEqual(new Vector3(2f, 3f, 1f), box!.size, "棱柱尺寸 = 模型 bounds 的 XZ/Y 跨度");
                Assert.IsFalse(box.isTrigger, "要挡人就必须是实心碰撞体");

                var body = box.gameObject;
                Assert.AreEqual("NpcBody", body.name, "碰撞体挂在具名子物体上，便于验收/诊断");
                Assert.AreEqual(NpcBodyCollider.BodyLayer, body.layer,
                    "碰撞体应落在 NpcBody 层（层矩阵按层放行）");
                Assert.AreEqual(new Vector3(0.5f, 1f, -0.25f), body.transform.localPosition,
                    "碰撞体中心对齐模型 bounds 中心（视图本地空间）");

                // 造出来的子物体在视图根之下 —— 随插值定位一起移动
                Assert.AreSame(view.GameObject.transform, body.transform.parent);
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Body_Collider_Is_Sized_From_Each_Real_Monster_Prefab()
        {
            // 尺寸**随模型**：两个真预制体（DogKnight 缩放 1×、SpiderBoss 缩放 2×）用同一公式各自算，
            // 断言用测试侧独立推导的期望值（不抄实现里的常量）。
            foreach (var (path, templateId) in new[] { (DogKnightPrefab, 3), (SpiderPrefab, 4) })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                var registry = new RemoteEntityRegistry(_root.transform, null,
                    new Dictionary<int, GameObject> { { templateId, prefab } });
                try
                {
                    var view = CreateNpcView(registry, templateId, 2_000_010L + templateId);
                    var bodyCollider = view.GameObject.GetComponentInChildren<NpcBodyCollider>(true);
                    Assert.IsNotNull(bodyCollider, path + "：怪物视图应补上运行时身体碰撞体组件");
                    bodyCollider!.BuildCollider();

                    var box = bodyCollider.Collider;
                    Assert.IsNotNull(box, path + "：真模型应能推出一具身体碰撞体");

                    // 独立推导期望尺寸：合并 SMR 的**世界** bounds → 除回视图根 lossyScale = 本地尺寸
                    var renderers = view.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Assert.Greater(renderers.Length, 0, path + "：应有 SkinnedMeshRenderer");
                    var world = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);
                    var scale = view.GameObject.transform.lossyScale;
                    var localSize = new Vector3(world.size.x / scale.x, world.size.y / scale.y, world.size.z / scale.z);

                    Assert.AreEqual(localSize.x, box!.size.x, 1e-2f, path + "：X 跨度应随模型");
                    Assert.AreEqual(localSize.y, box.size.y, 1e-2f, path + "：Y 跨度应随模型");
                    Assert.AreEqual(localSize.z, box.size.z, 1e-2f, path + "：Z 跨度应随模型");
                    Assert.Greater(box.size.x, 0.2f, path + "：尺寸不该是退化值");
                    Assert.Greater(box.size.y, 0.2f, path + "：尺寸不该是退化值");
                }
                finally
                {
                    registry.Clear();
                }
            }
        }

        [Test]
        public void Body_Collider_Collides_Only_With_The_Local_Player_Layer()
        {
            int bodyLayer = NpcBodyCollider.BodyLayer;

            // name ↔ index 配对：**读工程设置文件本身**（随构建/随 clone 走的那份）。
            // 不走 AssetDatabase/LayerMask.LayerToName：ProjectSettings 资产只在编辑器会话启动时读一次，
            // 之后（域重载也不）重读——本会话里那两个视图会是陈旧值（实测），文件不会。
            var settingsPath = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "ProjectSettings", "TagManager.asset");
            Assert.IsTrue(System.IO.File.Exists(settingsPath), "应能读到 " + settingsPath);
            var layerLines = System.IO.File.ReadAllLines(settingsPath);
            // layers: 列表的第 N 项 = 层索引 N（前面是 `layers:` 头与 `- Default` 起的一串条目）
            int layersHeader = System.Array.FindIndex(layerLines, l => l.TrimEnd() == "  layers:");
            Assert.GreaterOrEqual(layersHeader, 0, "TagManager.asset 里应能找到 layers: 列表");
            string entry = layerLines[layersHeader + 1 + bodyLayer].Trim();
            Assert.AreEqual("- " + NpcBodyCollider.BodyLayerName, entry,
                $"TagManager 第 {bodyLayer} 层的名字应是 '{NpcBodyCollider.BodyLayerName}'（代码用的是索引常量）");

            // 只放行本地玩家层；其余（含 Default/场景静态）全部关掉——层矩阵是持久化设置，由本用例钉住
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(bodyLayer, NpcBodyCollider.LocalPlayerLayer),
                "NpcBody × 玩家层必须可碰撞（否则挡不住玩家）");
            for (int l = 0; l < 32; l++)
            {
                if (l == NpcBodyCollider.LocalPlayerLayer) continue;
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(bodyLayer, l),
                    $"NpcBody × 层 {l} 应被层矩阵关掉（只与本地玩家交互）");
            }

            // 玩家层号从预制体来：换了层就得同步改常量（否则本用例红，提示两处一起改）
            foreach (var path in new[]
            {
                "Assets/Prefabs/Character/FemalePlayer.prefab",
                "Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab",
            })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                Assert.AreEqual(NpcBodyCollider.LocalPlayerLayer, prefab!.layer, path + " 根节点所在层");
                var cc = prefab.GetComponentInChildren<UnityEngine.CharacterController>(true);
                Assert.IsNotNull(cc, path + " 应有 CharacterController");
                Assert.AreEqual(NpcBodyCollider.LocalPlayerLayer, cc!.gameObject.layer, path + " CharacterController 所在层");
            }
        }

        [Test]
        public void Human_Player_View_Still_Gets_No_Collider()
        {
            // 回归：碰撞体只加给怪物分支。真人视图（含 bot 那条）一条碰撞体都不许多出来
            var avatarPrefab = new GameObject("FakeAvatarPrefab");
            var registry = new RemoteEntityRegistry(_root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = avatarPrefab } });
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 42,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 0f, Y = 0f, Z = 0f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);

                var view = registry.GetView(42);
                Assert.IsNotNull(view);
                Assert.IsNull(view!.GameObject.GetComponentInChildren<NpcBodyCollider>(true),
                    "真人视图不该拿到怪物身体碰撞体");
                Assert.IsNull(view.GameObject.GetComponentInChildren<Collider>(true),
                    "真人视图不该多出碰撞体（远程玩家的碰撞体禁用逻辑在 RemoteEntityAnimator 里）");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(avatarPrefab);
            }
        }

        [Test]
        public void Monster_Prefabs_Stay_Collider_Free()
        {
            // 资产断言：预制体保持纯美术——碰撞体只在实例化后由 NpcBodyCollider 加
            foreach (var path in new[] { DogKnightPrefab, SpiderPrefab })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                Assert.AreEqual(0, prefab!.GetComponentsInChildren<Collider>(true).Length,
                    path + " 预制体不该带碰撞体（运行时才加）");
                Assert.IsNull(prefab.GetComponentInChildren<NpcBodyCollider>(true),
                    path + " 预制体不该挂 NpcBodyCollider（由 registry 实例化后添加）");
            }
        }

        [Test]
        public void Camera_Collision_Mask_Excludes_The_Body_Layer()
        {
            // 相机防穿墙是 SphereCastAll(~0)：层矩阵管不到查询（实测），只能靠掩码自己排除怪物身体层。
            // 否则贴着 Boss 时相机会被拽到最近距离（2026-10-13 实测：画面只剩后脑）。
            var def = ThirdPersonCameraController.DefaultCollisionMask;
            Assert.AreEqual(0, def.value & (1 << NpcBodyCollider.BodyLayer),
                "默认掩码必须排除 NpcBody 层（改回 ~0 本用例就红）");
            Assert.AreNotEqual(0, def.value & 1, "默认层（地形/静态遮挡）必须留在掩码里");

            // 场景资产里可能还序列化着旧版的 ~0 —— Awake 的兜底剔除要把层 10 拿掉（去掉这段逻辑本用例红）
            var go = new GameObject("CameraMaskProbe");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.CameraCollisionMask = ~0;   // 模拟"场景里存的是旧值"
                cam.ApplyLayerExclusions();     // EditMode：AddComponent 不触发 Awake

                int bits = cam.CameraCollisionMask.value;
                Assert.AreEqual(0, bits & (1 << NpcBodyCollider.BodyLayer),
                    "兜底剔除后掩码不许含 NpcBody 层");
                Assert.AreNotEqual(0, bits & 1, "兜底剔除不该误删其它层");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Body_Collider_Is_Rejected_By_Attack_Detection_Filters()
        {
            // 攻击/AOE 检测（SkillExecutor 扩散扫描、PhysicsRegistry）按组件筛选目标：
            // 怪物视图没有 IEffectTarget / IDamageable ⇒ 命中也被丢弃，判定不变。
            // 这条用例把"为什么不需要为攻击检测加掩码"钉成可执行事实。
            var prefab = FakeMonsterPrefab(new Bounds(new Vector3(0f, 1f, 0f), new Vector3(2f, 3f, 2f)));
            var registry = new RemoteEntityRegistry(_root.transform, null,
                new Dictionary<int, GameObject> { { 4, prefab } });
            try
            {
                var view = CreateNpcView(registry, 4, 2_000_020);
                view.GameObject.GetComponentInChildren<NpcBodyCollider>(true)!.BuildCollider();
                var bodyPos = view.GameObject.transform.position + new Vector3(0f, 1f, 0f);
                // 项目设置里 m_AutoSyncTransforms=0：新建/移动碰撞体后要显式同步，物理查询才看得到新位姿
                Physics.SyncTransforms();

                // 查询**能**看到它（掩码 ~0）——所以"看不见"不是防线，组件筛选才是
                var hits = Physics.OverlapSphere(bodyPos, 3f, ~0);
                Assert.Greater(hits.Length, 0, "OverlapSphere 应命中新碰撞体（证明查询确实看得见它）");
                bool sawBody = false;
                foreach (var hit in hits)
                {
                    // 碰撞体在视图根的**子物体**上：按 SkillExecutor / PhysicsRegistry 的取法向上找
                    if (hit.GetComponentInParent<NpcBodyCollider>() != null) sawBody = true;
                    Assert.IsNull(hit.GetComponent<IEffectTarget>(), "怪物视图不该提供 IEffectTarget（AOE 筛选条件）");
                    Assert.IsNull(hit.GetComponentInParent<IEffectTarget>(), "怪物视图不该提供 IEffectTarget（技能筛选条件）");
                    Assert.IsNull(hit.GetComponent<IDamageable>(), "怪物视图不该提供 IDamageable（自身）");
                    Assert.IsNull(hit.GetComponentInParent<IDamageable>(), "怪物视图不该提供 IDamageable（物理注册表筛选条件）");
                }
                Assert.IsTrue(sawBody, "结果里应包含我们刚建的身体碰撞体");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(prefab);
            }
        }
    }
}
