using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Animation;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 分档策略（design §4）：距离迟滞 + 最近优先预算裁剪。
    /// 语义分工：**距离保观感、预算控成本**——低密度全 Full（好看），高密度由 MaxFull 兜底（能跑）。
    /// </summary>
    public class RemoteTierPolicyTests
    {
        private static List<TierCandidate> Candidates(params (long id, float dist, bool full)[] items)
        {
            var list = new List<TierCandidate>();
            foreach (var (id, dist, full) in items)
                list.Add(new TierCandidate { PlayerId = id, DistanceSqr = dist * dist, CurrentlyFull = full });
            return list;
        }

        [Test]
        public void Inside_The_Threshold_Is_Full()
        {
            var policy = new RemoteTierPolicy { FullDistance = 60f, MaxFull = 20 };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 30f, false)), results);
            Assert.AreEqual(RemoteTier.Full, results[0]);
        }

        [Test]
        public void Beyond_The_Exit_Threshold_Is_Proxy()
        {
            var policy = new RemoteTierPolicy { FullDistance = 60f, ExitRatio = 1.15f };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 80f, true)), results);
            Assert.AreEqual(RemoteTier.Proxy, results[0], "超过退出阈值(69m)即使当前是 Full 也要降档");
        }

        [Test]
        public void Hysteresis_Keeps_A_Full_Entity_That_Drifted_Into_The_Band()
        {
            var policy = new RemoteTierPolicy { FullDistance = 60f, ExitRatio = 1.15f };
            var results = new List<RemoteTier>();

            policy.Decide(Candidates((1, 65f, true)), results);
            Assert.AreEqual(RemoteTier.Full, results[0], "65m 在迟滞带(60~69)内：已是 Full 的保持");

            policy.Decide(Candidates((1, 65f, false)), results);
            Assert.AreEqual(RemoteTier.Proxy, results[0], "同一距离但当前是 Proxy：不升档（迟滞的另一半）");
        }

        [Test]
        public void Budget_Keeps_The_Nearest_And_Demotes_The_Rest()
        {
            var policy = new RemoteTierPolicy { FullDistance = 300f, MaxFull = 2 };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 50f, false), (2, 10f, false), (3, 30f, false)), results);

            Assert.AreEqual(RemoteTier.Full, results[1], "10m 最近 ⇒ Full");
            Assert.AreEqual(RemoteTier.Full, results[2], "30m 次近 ⇒ Full");
            Assert.AreEqual(RemoteTier.Proxy, results[0], "50m 超出预算 ⇒ 降档");
        }

        [Test]
        public void Budget_Tie_Breaks_Deterministically_By_Id()
        {
            // 有效距离相同 → 按 PlayerId 升序，否则同距离实体会每帧互换档位（抖动）
            var policy = new RemoteTierPolicy { FullDistance = 300f, MaxFull = 1 };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((7, 25f, false), (3, 25f, false)), results);

            Assert.AreEqual(RemoteTier.Full, results[1], "id 小的赢（确定性）");
            Assert.AreEqual(RemoteTier.Proxy, results[0]);
        }

        [Test]
        public void Existing_Full_Gets_A_Retention_Bias()
        {
            // 已在 Full 的距离打 0.9 折：25m 的老 Full（有效 22.5m）赢过 24m 的新候选
            var policy = new RemoteTierPolicy { FullDistance = 300f, MaxFull = 1, FullRetention = 0.9f };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 25f, true), (2, 24f, false)), results);

            Assert.AreEqual(RemoteTier.Full, results[0], "已在 Full 的占优（预算裁剪迟滞）");
            Assert.AreEqual(RemoteTier.Proxy, results[1]);
        }

        [Test]
        public void Empty_Input_Is_Safe()
        {
            var policy = new RemoteTierPolicy();
            var results = new List<RemoteTier>();
            Assert.DoesNotThrow(() => policy.Decide(new List<TierCandidate>(), results));
            Assert.AreEqual(0, results.Count);
        }

        // ---- registry 级：分档真的落到视图上（换表现、不是只算了档位） ----

        private static EntitySnapshot SnapAt(long id, Vector3 pos, float yaw = 0f)
            => new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Kind),
                X = pos.x, Y = pos.y, Z = pos.z, Rotation = yaw,
                Kind = (byte)EntityKind.PlayerBot, Tick = 1
            };

        [Test]
        public void Registry_Demotes_Entities_Beyond_The_Budget()
        {
            var root = new GameObject("TierRegistryRoot");
            var prefab = new GameObject("TierAvatarPrefab");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            registry.ObserverPositionProvider = () => Vector3.zero;
            registry.TierPolicy.FullDistance = 300f;
            registry.TierPolicy.MaxFull = 20;   // 第一阶段：全不超预算 ⇒ 4 个都建出 Full 表现
            try
            {
                // 4 个实体，距离 10 / 20 / 30 / 40m
                for (int i = 0; i < 4; i++)
                    registry.ApplySnapshot(SnapAt(10 + i, new Vector3((i + 1) * 10f, 0f, 0f)),
                        serverTicks: 1_000_000L);

                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                // 第二阶段：预算收紧到 2 ⇒ 后两个降档（降档断言必须走"曾是 Full 再被裁掉"，
                // 惰性档下从未升过档的视图根本没有 FullRoot 可断言）
                registry.TierPolicy.MaxFull = 2;
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                int full = 0, proxy = 0;
                foreach (var id in registry.PlayerIds)
                {
                    var view = registry.GetView(id)!;
                    if (view.Tier == RemoteTier.Full) full++;
                    else proxy++;
                }
                Assert.AreEqual(2, full, "预算 2 ⇒ 只有最近的 2 个是 Full");
                Assert.AreEqual(2, proxy, "其余降 Proxy");

                Assert.IsFalse(registry.GetView(13)!.FullRoot!.activeInHierarchy,
                    "超预算的实体：Full 表现整棵禁用（Animator/蒙皮/渲染器全停）");
                Assert.IsTrue(registry.GetView(13)!.ProxyRoot!.activeInHierarchy, "代理顶上");
                Assert.IsTrue(registry.GetView(10)!.FullRoot!.activeInHierarchy, "最近的实体保持 Full");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Registry_Without_Observer_Keeps_Everyone_Full()
        {
            var root = new GameObject("NoObserverRoot");
            var prefab = new GameObject("NoObserverAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            try
            {
                registry.ApplySnapshot(SnapAt(1, new Vector3(500f, 0f, 0f)), serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                Assert.AreEqual(RemoteTier.Full, registry.GetView(1)!.Tier,
                    "未注入观察者 = 不分档（现有测试宿主与未接线场景的行为不变）");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// 注入的提供者**本帧返回 null**（本地角色还没装配 —— 正常启动态）= 本帧不分档：
        /// 视图保持原样，等拿到位置后再按距离定档。若把 null 误当 `Vector3.zero` 处理，
        /// 5m 处的实体会被当场升档（这条断言就是防这个）。
        /// </summary>
        [Test]
        public void A_Null_Observer_Position_Skips_Tiering_For_That_Frame()
        {
            var root = new GameObject("NullObserverRoot");
            var prefab = new GameObject("NullObserverAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            Vector3? observer = null;
            registry.ObserverPositionProvider = () => observer;
            registry.TierPolicy.FullDistance = 60f;
            try
            {
                registry.ApplySnapshot(SnapAt(1, new Vector3(5f, 0f, 0f)), serverTicks: 1_000_000L);
                var view = registry.GetView(1)!;
                Assert.AreEqual(RemoteTier.Proxy, view.Tier, "注入但暂无位置：创建时先不建 Full（惰性）");

                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);
                Assert.AreEqual(RemoteTier.Proxy, view.Tier,
                    "本帧拿不到位置 ⇒ 不分档（视图保持原样；被误算成原点的话 5m 处会当场升档）");

                observer = Vector3.zero;   // 本地角色装配好了
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);
                Assert.AreEqual(RemoteTier.Full, view.Tier, "拿到位置后按距离定档");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// 升档必须**重新归零层权重**（控制者裁定，来自任务 3 审查的跨任务指针）：
        /// 角色预制体的 `m_KeepAnimatorStateOnDisable` 是 0 —— Animator 在禁用期间会重置回控制器
        /// 资产的默认状态，而 Attack / Hit / Defend 三层的 `m_DefaultWeight` 是 1（本地玩家靠
        /// `FSMManager` 归零，远程没有）。所以 Proxy → Full 重新启用既有 FullRoot 时，只
        /// `InvalidateDedup()` 不够：不重新归零，远程角色会把攻击层/受击层的默认混进来（可见 bug）。
        ///
        /// 惰性实例化路径（新建 avatar）会跑 `Awake`、本来就归零 —— 本用例专打"重新启用既有
        /// FullRoot"这条（先落在 Proxy，再升档）。
        /// </summary>
        [Test]
        public void Promotion_Re_Zeroes_The_Attack_And_Hit_Layer_Weights()
        {
            const string controllerPath = "Assets/Tests_TempTierLayers.controller";
            var controller = UnityEditor.Animations.AnimatorController
                .CreateAnimatorControllerAtPath(controllerPath);
            controller.AddLayer("Attack");
            controller.AddLayer("Hit");
            controller.AddLayer("Defend");
            // 三层默认权重 = 1（与真实 `Character3C.controller` 同款）——**这是本用例的牙齿**：
            // 禁用/启用时 Animator 按资产默认值重置权重，默认是 0 的话"重置"本身就给出 0，
            // 断言 0 会在实现（ResetLayerWeights）被删掉时依然通过（变异存活，实测过一次）。
            var layers = controller.layers;
            for (int i = 1; i < layers.Length; i++) layers[i].defaultWeight = 1f;
            controller.layers = layers;

            var root = new GameObject("LayerWeightRoot");
            var prefab = new GameObject("LayerWeightAvatar");
            prefab.AddComponent<Animator>().runtimeAnimatorController = controller;
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            var observer = Vector3.zero;
            registry.ObserverPositionProvider = () => observer;
            registry.TierPolicy.FullDistance = 60f;
            try
            {
                registry.ApplySnapshot(SnapAt(10, new Vector3(5f, 0f, 0f)), serverTicks: 1_000_000L);
                var view = registry.GetView(10)!;
                Assert.AreEqual(RemoteTier.Proxy, view.Tier,
                    "观察者已注入 ⇒ 创建时就落在 Proxy 档（本用例要走'重新启用既有 FullRoot'）");
                Assert.IsNull(view.FullRoot,
                    "没升过档 ⇒ Full 表现压根不建（惰性的意义；急切实例化会在这里红）");
                Assert.IsNull(view.Animator, "Full 档才有驱动");

                // 第一次升档：惰性实例化 avatar + 挂驱动
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);
                Assert.AreEqual(RemoteTier.Full, view.Tier);
                Assert.IsNotNull(view.FullRoot, "升档才建 Full 表现");
                Assert.IsNotNull(view.Animator, "升档应挂上动画驱动");
                view.Animator!.ResolveAnimator();   // EditMode：AddComponent 不触发生命周期

                var animator = view.FullRoot!.GetComponent<Animator>()!;
                // 模拟"禁用期间被重置"：把两层权重抬回资产默认的 1
                animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 1f);
                animator.SetLayerWeight(AnimHashes.HitLayerIndex, 1f);
                Assert.AreEqual(1f, animator.GetLayerWeight(AnimHashes.AttackLayerIndex),
                    "前置条件：权重确实被抬到了 1（否则本用例测不到东西）");

                // 降档（整棵禁用）→ 再模拟一次禁用期重置 → 升档（重新启用）
                observer = new Vector3(500f, 0f, 0f);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);
                Assert.AreEqual(RemoteTier.Proxy, view.Tier, "495m 超出退出阈值 ⇒ 降档");

                animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 1f);
                animator.SetLayerWeight(AnimHashes.HitLayerIndex, 1f);

                observer = Vector3.zero;
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                Assert.AreEqual(RemoteTier.Full, view.Tier, "5m 在进入阈值内 ⇒ 升档");
                Assert.AreEqual(0f, animator.GetLayerWeight(AnimHashes.AttackLayerIndex),
                    "升档必须重新归零 Attack 层权重（m_KeepAnimatorStateOnDisable=0 ⇒ 禁用即重置为资产默认 1）");
                Assert.AreEqual(0f, animator.GetLayerWeight(AnimHashes.HitLayerIndex),
                    "Hit 层同理（0 权重时才交还给 Base 层）");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
                UnityEditor.AssetDatabase.DeleteAsset(controllerPath);
            }
        }

        /// <summary>
        /// D19 / spec §7：远端实体是**视图不是物理体**——都不挡人（碰撞只由怪物侧的
        /// `NpcBodyCollider` 提供，且只挡本地玩家）。`CreatePrimitive` 自带 Collider，
        /// Proxy 档的代理胶囊与未接线降级胶囊都必须去掉它。
        /// （`NpcBodyColliderTests.Human_Player_View_Still_Gets_No_Collider` 是空断言——
        /// 那里的空预制体本就无碰撞体，测不到 `StripCollider`；这条才真的测得到。）
        /// </summary>
        [Test]
        public void Placeholder_Capsules_Carry_No_Enabled_Collider()
        {
            var root = new GameObject("ProxyColliderRoot");
            var prefab = new GameObject("ProxyColliderAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            registry.ObserverPositionProvider = () => Vector3.zero;
            registry.TierPolicy.FullDistance = 1f;   // 50m 处 ⇒ 落 Proxy 档
            try
            {
                registry.ApplySnapshot(SnapAt(10, new Vector3(50f, 0f, 0f)), serverTicks: 1_000_000L);
                var view = registry.GetView(10)!;
                Assert.AreEqual(RemoteTier.Proxy, view.Tier);
                Assert.IsNotNull(view.ProxyRoot, "Proxy 档必须建出代理胶囊");
                AssertHasNoEnabledCollider(view.ProxyRoot!, "Proxy 档的代理胶囊");

                // 未接线（无外观目录）的降级胶囊同理
                var fallbackRoot = new GameObject("ProxyColliderFallbackRoot");
                var fallback = new RemoteEntityRegistry(fallbackRoot.transform);
                try
                {
                    fallback.ApplySnapshot(SnapAt(11, new Vector3(1f, 0f, 0f)), serverTicks: 1_000_000L);
                    AssertHasNoEnabledCollider(fallback.GetView(11)!.GameObject, "未接线降级胶囊");
                }
                finally
                {
                    fallback.Clear();
                    Object.DestroyImmediate(fallbackRoot);
                }
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// EditMode 走 `DestroyImmediate` ⇒ 组件直接没了（与 null 相等）；运行时 `Destroy`
        /// 帧末生效 ⇒ 创建帧内组件还在、但必须已禁用 —— 两条都算过（设计只要求"无**启用的** Collider"）。
        /// </summary>
        private static void AssertHasNoEnabledCollider(GameObject go, string what)
        {
            var collider = go.GetComponent<Collider>();
            Assert.IsTrue(collider == null || !collider.enabled,
                what + "不得自带启用的 Collider（D19：远端实体是视图不是物理体）");
            foreach (var child in go.GetComponentsInChildren<Collider>(true))
                Assert.IsFalse(child.enabled, what + "不能带启用的 Collider（含子物体）");
        }
    }
}
