using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.FSM;
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
        /// 视图落到全 Full，等拿到位置后再按距离定档。（任务 7 审查修正：此前"不分档"被实现成
        /// "视图保持原样"——而注入态下 `CreateView` 先建的是 Proxy ⇒ 观察者模式/装配窗口里
        /// 远程玩家会永久停在胶囊、无动画；第一段断言随之从 Proxy 改成 Full。）
        /// 若把 null 误当 `Vector3.zero` 处理，远处的实体会按原点距离被降档。
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
                Assert.AreEqual(RemoteTier.Full, view.Tier,
                    "本帧拿不到位置 ⇒ 不分档 = 视图落到全 Full（\"保持全 Full\"说的是视图状态，不只是读数）");

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
        /// 任务 7 审查（Important）：provider **已注入但恒返回 null**（观察者模式：场景里没有
        /// `Sys3CEntry`，宿主自身支持的降级）= 不分档 ⇒ 玩家类视图必须**落在 Full**，
        /// 而不是停在 `CreateView` 的惰性 Proxy 胶囊上（那会让远程玩家永久无动画）。
        /// 实体放 500m 外：若把 null 误当 `Vector3.zero`（按原点算距离）它会被降档——这条抓的就是它。
        /// </summary>
        [Test]
        public void Provider_Returning_Null_Keeps_Every_View_Full()
        {
            var root = new GameObject("NullProviderRoot");
            var prefab = new GameObject("NullProviderAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            registry.ObserverPositionProvider = () => (Vector3?)null;
            registry.TierPolicy.FullDistance = 60f;
            try
            {
                registry.ApplySnapshot(SnapAt(1, new Vector3(500f, 0f, 0f)), serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                var view = registry.GetView(1)!;
                Assert.AreEqual(RemoteTier.Full, view.Tier,
                    "拿不到观察者位置 = 不分档 ⇒ 视图全 Full（不是停在惰性 Proxy 胶囊）");
                Assert.AreEqual(1, registry.FullCount, "读数与所见一致：视图是 Full，读数也是");
                Assert.AreEqual(0, registry.ProxyCount);
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        private static EntitySnapshot NpcSnapAt(long id, Vector3 pos)
            => new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                X = pos.x, Y = pos.y, Z = pos.z, Kind = (byte)EntityKind.Npc, TemplateId = 7, Tick = 1
            };

        /// <summary>
        /// 任务 7：计数口径必须与分档口径一致 —— Npc 不进任何计数（它是玩法实体，从不分档）；
        /// 玩家类视图按**实际档位**记：真落在 Proxy 的进 `ProxyCount`，不是"非 Npc 一律记 Full"。
        /// </summary>
        [Test]
        public void Tier_Counts_Follow_The_Actual_Tier_And_Exclude_Npcs()
        {
            var root = new GameObject("TierCountRoot");
            var prefab = new GameObject("TierCountAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            registry.ObserverPositionProvider = () => Vector3.zero;
            registry.TierPolicy.FullDistance = 60f;
            try
            {
                registry.ApplySnapshot(SnapAt(1, new Vector3(10f, 0f, 0f)), serverTicks: 1_000_000L);
                registry.ApplySnapshot(SnapAt(2, new Vector3(500f, 0f, 0f)), serverTicks: 1_000_000L);
                registry.ApplySnapshot(NpcSnapAt(3, new Vector3(5f, 0f, 0f)), serverTicks: 1_000_000L);

                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                Assert.AreEqual(RemoteTier.Full, registry.GetView(1)!.Tier, "10m 在阈值内 ⇒ Full");
                Assert.AreEqual(RemoteTier.Proxy, registry.GetView(2)!.Tier, "500m 出阈值 ⇒ Proxy");
                Assert.AreEqual(1, registry.FullCount);
                Assert.AreEqual(1, registry.ProxyCount, "真落在 Proxy 的玩家类视图进 ProxyCount");
                Assert.AreEqual(3, registry.Count, "三个视图都在——Npc 只是不进任何计数");
                Assert.AreEqual((byte)EntityKind.Npc, registry.GetView(3)!.Kind);
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
        /// 升档必须**当场回放锁存的状态**（规格 §5："升档：失效 `_lastKey` 并强制重写一次"）。
        /// 实现若只"失效"、不重写，重写就要等**下一条快照**——静止实体保活 4 tick（≈264ms），
        /// 而角色预制体的 `m_KeepAnimatorStateOnDisable` 是 0（禁用即重置）⇒ 死亡/受击中的
        /// 实体从 Proxy 升 Full 时会**先站直最多 264ms**。
        /// 判据取在驱动的观测面 `LastHitState` 上（测试里的空 Animator 没有控制器，
        /// 驱动不写参数但照样记映射结果——与 `RemoteRenderBranchTests` 同惯例）。
        /// </summary>
        [Test]
        public void Promotion_Replays_The_Latched_Dead_State_Immediately()
        {
            var root = new GameObject("ReplayDeadRoot");
            var prefab = new GameObject("ReplayDeadAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            var observer = new Vector3(500f, 0f, 0f);   // 先让实体落在 Proxy 档
            registry.ObserverPositionProvider = () => observer;
            registry.TierPolicy.FullDistance = 60f;
            try
            {
                var dead = SnapAt(10, new Vector3(5f, 0f, 0f));
                dead.Dead = true;
                registry.ApplySnapshot(dead, serverTicks: 1_000_000L);

                var view = registry.GetView(10)!;
                Assert.AreEqual(RemoteTier.Proxy, view.Tier,
                    "前置：观察者远 ⇒ 视图先落 Proxy（权威状态只锁存，Proxy 档不驱动）");
                Assert.IsNull(view.Animator, "Proxy 档没有驱动");

                observer = Vector3.zero;   // 本地角色就位 ⇒ 升档
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                Assert.AreEqual(RemoteTier.Full, view.Tier, "5m 在进入阈值内 ⇒ 升档");
                Assert.IsNotNull(view.Animator, "升档挂上驱动");
                Assert.IsTrue(view.Dead, "视图锁存了权威存活状态");
                Assert.AreEqual((int)HitState.Death, view.Animator!.LastHitState,
                    "升档那一帧就必须回放锁存的死亡状态——只失效去重、不重写的话要等下一条快照（最多 264ms 站直）");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
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

        /// <summary>测试专用的代理标记组件：证明 Proxy 表现确实来自目录条目的 `Proxy` 预制体
        /// （`CreatePrimitive` 的胶囊上不可能有这个组件）。</summary>
        private sealed class ProxyMarker : MonoBehaviour { }

        /// <summary>
        /// `AppearanceEntry.Proxy` 是**可插拔接缝**（规格 §4）：美术产出 billboard/impostor 后
        /// 填进条目的 `Proxy`，代理表现就换成它——"换档机制一行都不用改"。本用例把这条接缝
        /// 钉成可执行事实（此前它是一句谎：全仓无任何代码读这个字段）。
        /// </summary>
        [Test]
        public void Proxy_Tier_Uses_The_Catalog_Proxy_Prefab_When_Configured()
        {
            var root = new GameObject("ProxyPrefabRoot");
            var full = new GameObject("ProxyPrefabFull");
            full.AddComponent<Animator>();
            var proxyPrefab = new GameObject("ProxyPrefabDouble");
            proxyPrefab.AddComponent<ProxyMarker>();
            proxyPrefab.AddComponent<BoxCollider>();                  // 预制体自带的碰撞体（根上）
            var child = new GameObject("Deco");
            child.transform.SetParent(proxyPrefab.transform, false);
            child.AddComponent<CapsuleCollider>();                    // 子物体上的也要处理

            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = full, Proxy = proxyPrefab } });
            registry.ObserverPositionProvider = () => Vector3.zero;
            registry.TierPolicy.FullDistance = 1f;   // 50m 处 ⇒ Proxy 档
            try
            {
                registry.ApplySnapshot(SnapAt(10, new Vector3(50f, 0f, 0f)), serverTicks: 1_000_000L);
                var proxy = registry.GetView(10)!.ProxyRoot;
                Assert.IsNotNull(proxy, "Proxy 档必须有代理表现");
                Assert.AreEqual("Proxy", proxy!.name, "代理仍叫 Proxy（验收/诊断靠名字）");
                Assert.IsNotNull(proxy.GetComponent<ProxyMarker>(),
                    "ProxyRoot 必须来自目录条目的 Proxy 预制体（胶囊上没有这个标记组件）");
                Assert.IsNotNull(proxy.GetComponentInChildren<ProxyMarker>(true),
                    "标记（连同整棵预制体子树）都该在");
                AssertHasNoEnabledCollider(proxy, "Proxy 预制体（含子物体）");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(full);
                Object.DestroyImmediate(proxyPrefab);
            }
        }

        /// <summary>对照：条目**未配** `Proxy` ⇒ 仍是运行时胶囊（今天的路径不变）。</summary>
        [Test]
        public void Proxy_Tier_Still_Uses_The_Capsule_When_No_Proxy_Prefab_Is_Configured()
        {
            var root = new GameObject("CapsuleProxyRoot");
            var full = new GameObject("CapsuleProxyFull");
            full.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = full } });   // Proxy 留空
            registry.ObserverPositionProvider = () => Vector3.zero;
            registry.TierPolicy.FullDistance = 1f;
            var reference = GameObject.CreatePrimitive(PrimitiveType.Capsule);   // 对照组（同一内建 mesh）
            try
            {
                registry.ApplySnapshot(SnapAt(10, new Vector3(50f, 0f, 0f)), serverTicks: 1_000_000L);
                var proxy = registry.GetView(10)!.ProxyRoot!;
                Assert.IsNull(proxy.GetComponent<ProxyMarker>(), "没配 Proxy 就不该有预制体标记");
                Assert.AreSame(reference.GetComponent<MeshFilter>()!.sharedMesh,
                    proxy.GetComponent<MeshFilter>()!.sharedMesh,
                    "未配 Proxy ⇒ 仍是胶囊（与对照组同一个内建 mesh）");
                AssertHasNoEnabledCollider(proxy, "运行时胶囊");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(full);
                Object.DestroyImmediate(reference);
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
