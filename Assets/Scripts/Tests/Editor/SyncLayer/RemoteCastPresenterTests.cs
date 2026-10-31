using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 任务 9：远程施法表现 —— 投射物视觉按 (casterId, skillId) 跟踪，服务端
    /// DamageResult 的 HitX/HitZ、StopX/StopZ 是唯一停点来源；本地施法者与已删除视图不参与。
    /// </summary>
    public class RemoteCastPresenterTests
    {
        private sealed class FakeVisual : IRemoteProjectileVisual
        {
            public bool IsActive { get; private set; } = true;
            public int Explosions;
            public int Stops;
            public Vector3 LastPoint;

            public void ExplodeAt(Vector3 point) { Explosions++; LastPoint = point; IsActive = false; }
            public void StopAtPoint(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
        }

        private const string CatalogPath = "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset";

        /// <summary>EditMode 假视觉宿主：registry/catalog 为 null 时只走测试接缝。</summary>
        private static RemoteCastPresenter NewPresenter(long localPlayerId, IRemoteProjectileVisual visual)
            => new RemoteCastPresenter(
                registry: null, localPlayerId: localPlayerId, catalog: null,
                projectileFactory: (_, __, ___) => visual, castVfxSpawner: null);

        private static SkillPresentationCatalog LoadCatalog()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(CatalogPath);
            Assert.NotNull(catalog, "SkillPresentationCatalog.asset 未找到——PreRes 路径核对");
            return catalog;
        }

        /// <summary>Vector3 逐分量容差比较（NUnit 的 Equals 对旋转算出的浮点值过严）。</summary>
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 0.0001f);
            Assert.AreEqual(expected.y, actual.y, 0.0001f);
            Assert.AreEqual(expected.z, actual.z, 0.0001f);
        }

        private static DamageResult FireballResult(long sourceId, float hitX = 5f, float hitZ = 6f)
            => new DamageResult
            {
                SourcePlayerId = sourceId,
                SkillId = 20003,
                Hits = { new HitEntry { TargetPlayerId = 3L, HitX = hitX, HitZ = hitZ } }
            };

        [Test]
        public void Fireball_DamageResult_Explodes_At_The_Server_Hit_Point()
        {
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, visual);

            presenter.ApplyDamageForTest(FireballResult(2L));

            Assert.AreEqual(1, visual.Explosions);
            Assert.AreEqual(new Vector3(5f, 0f, 6f), visual.LastPoint);
        }

        [Test]
        public void Blocked_SwordQi_Stops_At_The_Server_Stop_Point()
        {
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 2L, skillId: 10002, visual);

            presenter.ApplyDamageForTest(new DamageResult
            {
                SourcePlayerId = 2L,
                SkillId = 10002,
                ProjectileStopped = true,
                StopX = 4f,
                StopZ = 2f
            });

            Assert.AreEqual(1, visual.Stops);
            Assert.AreEqual(new Vector3(4f, 0f, 2f), visual.LastPoint);
        }

        [Test]
        public void Local_Caster_Is_Ignored()
        {
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 1L, skillId: 20003, visual);

            presenter.ApplyDamageForTest(FireballResult(1L, hitX: 9f, hitZ: 9f));

            Assert.AreEqual(0, visual.Explosions, "本地施法者的视觉由本地 SkillExecutor 负责");
        }

        [Test]
        public void ClearForEntity_Drops_Tracked_Visual()
        {
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, visual);

            presenter.ClearForEntity(2L);
            presenter.ApplyDamageForTest(FireballResult(2L));

            Assert.AreEqual(0, visual.Explosions, "视图删除后跟踪表必须清理，迟到的 DamageResult 不得再动视觉");
        }

        [Test]
        public void ClearForEntity_Leaves_Other_Casters_Alone()
        {
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, visual);

            presenter.ClearForEntity(3L);   // 清的是别人
            presenter.ApplyDamageForTest(FireballResult(2L));

            Assert.AreEqual(1, visual.Explosions, "只清被删除实体的跟踪项");
        }

        [Test]
        public void DamageResult_Consumes_Tracked_Visual_Once()
        {
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, visual);

            presenter.ApplyDamageForTest(FireballResult(2L));
            presenter.ApplyDamageForTest(FireballResult(2L));

            Assert.AreEqual(1, visual.Explosions, "同一发火球只爆一次（第二次已无跟踪项）");
        }

        [Test]
        public void Earliest_Tracked_Visual_Is_Consumed_First()
        {
            var first = new FakeVisual();
            var second = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, first);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, first);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, second);

            presenter.ApplyDamageForTest(FireballResult(2L));
            Assert.AreEqual(1, first.Explosions, "同键多发的第一发先到服务端结果");
            Assert.AreEqual(0, second.Explosions);

            presenter.ApplyDamageForTest(FireballResult(2L));
            Assert.AreEqual(1, second.Explosions, "第二次结果命中下一发仍活跃的视觉");
        }

        [Test]
        public void Update_Prunes_Inactive_Visual()
        {
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, visual);
            Assert.AreEqual(1, presenter.TrackedCountForTest);

            visual.StopAtPoint(Vector3.zero);   // 视觉已自行回收（例如飞满射程）
            presenter.Update(0.1f);

            Assert.AreEqual(0, presenter.TrackedCountForTest, "Update 必须把失效视觉移出跟踪表");
            presenter.ApplyDamageForTest(FireballResult(2L));
            Assert.AreEqual(0, visual.Explosions, "非活跃视觉不应再收到爆炸指令");
        }

        [Test]
        public void SwordQi_Unblocked_Hit_Does_Not_Consume_Tracked_Visual()
        {
            // 普通命中不消费：剑气还要继续飞，后续被格挡时同一个视觉才该停在 StopX/StopZ
            var visual = new FakeVisual();
            var presenter = NewPresenter(localPlayerId: 1L, visual);
            presenter.TrackForTest(casterId: 2L, skillId: 10002, visual);

            presenter.ApplyDamageForTest(new DamageResult
            {
                SourcePlayerId = 2L,
                SkillId = 10002,
                ProjectileStopped = false,
                Hits = { new HitEntry { TargetPlayerId = 3L, Damage = 27f, HitX = 2f, HitZ = 3f } }
            });

            Assert.AreEqual(0, visual.Stops, "普通命中不得停剑气");
            Assert.AreEqual(1, presenter.TrackedCountForTest, "普通命中不得消费跟踪项");

            presenter.ApplyDamageForTest(new DamageResult
            {
                SourcePlayerId = 2L,
                SkillId = 10002,
                ProjectileStopped = true,
                StopX = 4f,
                StopZ = 2f
            });

            Assert.AreEqual(1, visual.Stops, "随后被格挡时应命中同一个视觉");
            Assert.AreEqual(new Vector3(4f, 0f, 2f), visual.LastPoint);
            Assert.AreEqual(0, presenter.TrackedCountForTest);
        }

        [Test]
        public void ComputeProjectileOrigin_Applies_Rotated_Asset_Offsets()
        {
            // 本地 SkillExecutor 用 owner.position + TransformDirection(SpawnOffset)；
            // 远程起点必须同源，否则火球把地面算低 1.2m、剑气在脚踝高度飞行。
            var catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGet(20003, out var fireball), "20003 不在目录");
            Assert.IsTrue(catalog.TryGet(10002, out var swordQi), "10002 不在目录");

            var position = new Vector3(1f, 0f, 2f);
            AssertVector(new Vector3(1f, 1.2f, 2.5f),
                RemoteCastPresenter.ComputeProjectileOrigin(fireball, position, Vector3.forward));
            AssertVector(new Vector3(1.8f, 0.6f, 2.6f),
                RemoteCastPresenter.ComputeProjectileOrigin(swordQi, position, Vector3.forward));

            // 偏移随朝向旋转：+X 朝向时局部 +Z 落到世界 +X
            AssertVector(new Vector3(1.5f, 1.2f, 2f),
                RemoteCastPresenter.ComputeProjectileOrigin(fireball, position, Vector3.right));
            AssertVector(new Vector3(1.6f, 0.6f, 1.2f),
                RemoteCastPresenter.ComputeProjectileOrigin(swordQi, position, Vector3.right));
        }

        [Test]
        public void OnCastBroadcast_Remote_Player_Spawns_Projectile_At_Rendered_Pose()
        {
            var root = new GameObject("RemoteCastPresenterTestRoot");
            try
            {
                var registry = new RemoteEntityRegistry(root.transform);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 1f, Y = 0f, Z = 2f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);
                // 视图位置由插值渲染写入（ApplySnapshot 只入缓冲）——表现必须用渲染位姿
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                var catalog = LoadCatalog();

                int factoryCalls = 0;
                SkillData capturedData = null;
                Vector3 capturedOrigin = Vector3.zero;
                Vector3 capturedForward = Vector3.zero;
                var visual = new FakeVisual();
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, catalog,
                    projectileFactory: (data, origin, forward) =>
                    {
                        factoryCalls++;
                        capturedData = data;
                        capturedOrigin = origin;
                        capturedForward = forward;
                        return visual;
                    },
                    castVfxSpawner: null);

                presenter.OnCastBroadcast(new CastBroadcast { CasterPlayerId = 2L, SkillId = 20003 });

                Assert.AreEqual(1, factoryCalls, "远程玩家 CastBroadcast 必须在收到时就生成投射物视觉");
                Assert.NotNull(capturedData);
                Assert.AreEqual(20003, capturedData.SkillId);
                Assert.AreEqual(new Vector3(1f, 1.2f, 2.5f), capturedOrigin,
                    "起点 = 渲染位置 + 旋转后的 SpawnOffset（不是脚底，也不是服务端快照原始值）");
                Assert.AreEqual(Vector3.forward, capturedForward, "前向取渲染朝向");

                presenter.ApplyDamageForTest(FireballResult(2L));
                Assert.AreEqual(1, visual.Explosions, "CastBroadcast 建立的跟踪项要能被 DamageResult 消费");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OnCastBroadcast_SwordQi_Starts_At_Rotated_SpawnOffset()
        {
            var root = new GameObject("RemoteCastSwordQiRoot");
            try
            {
                var registry = new RemoteEntityRegistry(root.transform);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 1f, Y = 0f, Z = 2f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                Vector3 capturedOrigin = Vector3.zero;
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, LoadCatalog(),
                    projectileFactory: (_, origin, __) => { capturedOrigin = origin; return new FakeVisual(); },
                    castVfxSpawner: null);

                presenter.OnCastBroadcast(new CastBroadcast { CasterPlayerId = 2L, SkillId = 10002 });

                Assert.AreEqual(new Vector3(1.8f, 0.6f, 2.6f), capturedOrigin,
                    "10002 剑气必须从 SwordQiConfig.SpawnOffset 出发，不能在脚踝高度穿地");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OnCastBroadcast_Spawns_CastVfx_At_Rendered_Pose()
        {
            var root = new GameObject("RemoteCastVfxRoot");
            var vfxPrefab = new GameObject("TestCastVfxPrefab");
            SkillData skillClone = null;
            SkillPresentationCatalog runtimeCatalog = null;
            try
            {
                var registry = new RemoteEntityRegistry(root.transform);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 1f, Y = 0f, Z = 2f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                // 现有 7 个技能资产都没有 CastVFX——用运行期克隆给 20001 挂一个，保留
                // "真实 view + 真实 TryGet 路径"的测试形态，不写资产文件。
                var source = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillData>(
                    "Assets/PreRes/SkillsCfg/Instant_SkillQ.asset");
                Assert.NotNull(source, "Instant_SkillQ.asset 未找到——PreRes 路径核对");
                skillClone = UnityEngine.Object.Instantiate(source);
                var skillSo = new UnityEditor.SerializedObject(skillClone);
                var presentation = skillSo.FindProperty("_presentation");
                Assert.NotNull(presentation, "InstantSkillData._presentation 序列化路径变了");
                var castVfx = presentation.FindPropertyRelative("_castVFX");
                Assert.NotNull(castVfx, "PresentationBlock._castVFX 序列化路径变了");
                castVfx.objectReferenceValue = vfxPrefab;
                skillSo.ApplyModifiedPropertiesWithoutUndo();

                runtimeCatalog = ScriptableObject.CreateInstance<SkillPresentationCatalog>();
                var catalogSo = new UnityEditor.SerializedObject(runtimeCatalog);
                var skills = catalogSo.FindProperty("_skills");
                skills.arraySize = 1;
                skills.GetArrayElementAtIndex(0).objectReferenceValue = skillClone;
                catalogSo.ApplyModifiedPropertiesWithoutUndo();

                int calls = 0;
                GameObject capturedPrefab = null;
                Vector3 capturedPosition = Vector3.zero;
                Quaternion capturedRotation = Quaternion.identity;
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, runtimeCatalog,
                    projectileFactory: (_, __, ___) => null,
                    castVfxSpawner: (prefab, position, rotation) =>
                    {
                        calls++;
                        capturedPrefab = prefab;
                        capturedPosition = position;
                        capturedRotation = rotation;
                    });

                presenter.OnCastBroadcast(new CastBroadcast { CasterPlayerId = 2L, SkillId = 20001 });

                Assert.AreEqual(1, calls, "CastVFX 非空时必须在收到广播时生成");
                Assert.AreSame(vfxPrefab, capturedPrefab);
                Assert.AreEqual(new Vector3(1f, 0f, 2f), capturedPosition, "施法特效在渲染位置");
                Assert.AreEqual(Quaternion.identity, capturedRotation, "施法特效取渲染朝向");
            }
            finally
            {
                if (runtimeCatalog != null) Object.DestroyImmediate(runtimeCatalog);
                if (skillClone != null) Object.DestroyImmediate(skillClone);
                Object.DestroyImmediate(vfxPrefab);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OnCastBroadcast_ChargeBlast_Spawns_ReleaseVfx_At_Server_Impact()
        {
            var root = new GameObject("RemoteCastReleaseVfxRoot");
            try
            {
                var registry = new RemoteEntityRegistry(root.transform);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 1f, Y = 0f, Z = 2f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                int calls = 0;
                GameObject capturedPrefab = null;
                Vector3 capturedPosition = Vector3.zero;
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, LoadCatalog(),
                    projectileFactory: (_, __, ___) => null,
                    releaseVfxSpawner: (prefab, position, rotation) =>
                    {
                        calls++;
                        capturedPrefab = prefab;
                        capturedPosition = position;
                    });

                presenter.OnCastBroadcast(new CastBroadcast
                {
                    CasterPlayerId = 2L, SkillId = 20004,
                    ImpactX = 3f, ImpactY = 0.25f, ImpactZ = 7f
                });

                Assert.AreEqual(1, calls, "20004 必须在服务端落点生成 ReleaseVFX");
                Assert.NotNull(capturedPrefab, "ChargeFire_SkillR.asset 应配置 ReleaseVFX");
                Assert.AreEqual(new Vector3(3f, 0.25f, 7f), capturedPosition);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OnCastBroadcast_Ultimate_Spawns_Meteor_At_Server_Impact()
        {
            var root = new GameObject("RemoteCastMeteorRoot");
            try
            {
                var registry = new RemoteEntityRegistry(root.transform);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 1f, Y = 0f, Z = 2f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                int calls = 0;
                MeteorConfig capturedConfig = null;
                Vector3 capturedImpact = Vector3.zero;
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, LoadCatalog(),
                    projectileFactory: (_, __, ___) => null,
                    meteorSpawner: (config, damage, impact) =>
                    {
                        calls++;
                        capturedConfig = config;
                        capturedImpact = impact;
                    });

                presenter.OnCastBroadcast(new CastBroadcast
                {
                    CasterPlayerId = 2L, SkillId = 30001,
                    ImpactX = -2f, ImpactY = 0f, ImpactZ = 4f
                });

                Assert.AreEqual(1, calls, "30001 必须在服务端落点生成陨石");
                Assert.NotNull(capturedConfig, "Meteor_Ultimate.asset 应配置 Meteor");
                Assert.AreEqual(new Vector3(-2f, 0f, 4f), capturedImpact);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OnCastBroadcast_Ignores_Local_Caster_And_Npc()
        {
            var root = new GameObject("RemoteCastPresenterFilterRoot");
            try
            {
                var registry = new RemoteEntityRegistry(root.transform);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 1L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 1f, Y = 0f, Z = 1f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 3L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 4f, Y = 0f, Z = 4f, Kind = (byte)EntityKind.Npc, Tick = 1
                }, serverTicks: 1_000_000L);

                var catalog = LoadCatalog();

                int factoryCalls = 0;
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, catalog,
                    projectileFactory: (_, __, ___) => { factoryCalls++; return new FakeVisual(); },
                    castVfxSpawner: null);

                presenter.OnCastBroadcast(new CastBroadcast { CasterPlayerId = 1L, SkillId = 20003 }); // 本地
                // NPC 用目录内的合法 id（20003）：即使 Kind 过滤失效，也会走 TryGet + 工厂——
                // 用 50004 的话 TryGet 会先失败，断言就区分不出是哪条守卫拦下的。
                presenter.OnCastBroadcast(new CastBroadcast { CasterPlayerId = 3L, SkillId = 20003 });

                Assert.AreEqual(0, factoryCalls, "本地施法者与 NPC 都不归 presenter 管");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
