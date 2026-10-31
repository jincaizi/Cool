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

        /// <summary>EditMode 假视觉宿主：registry/catalog 为 null 时只走测试接缝。</summary>
        private static RemoteCastPresenter NewPresenter(long localPlayerId, IRemoteProjectileVisual visual)
            => new RemoteCastPresenter(
                registry: null, localPlayerId: localPlayerId, catalog: null,
                projectileFactory: (_, __, ___) => visual, castVfxSpawner: null);

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

            visual.StopAtPoint(Vector3.zero);   // 视觉已自行回收（例如飞满射程）
            presenter.Update(0.1f);
            presenter.ApplyDamageForTest(FireballResult(2L));

            Assert.AreEqual(0, visual.Explosions, "非活跃视觉不应再收到爆炸指令");
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

                var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(
                    "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset");
                Assert.NotNull(catalog, "SkillPresentationCatalog.asset 未找到——PreRes 路径核对");

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
                Assert.AreEqual(new Vector3(1f, 0f, 2f), capturedOrigin, "视觉从渲染位置出发（不是服务端快照原始值）");
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

                var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(
                    "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset");
                Assert.NotNull(catalog, "SkillPresentationCatalog.asset 未找到——PreRes 路径核对");

                int factoryCalls = 0;
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, catalog,
                    projectileFactory: (_, __, ___) => { factoryCalls++; return new FakeVisual(); },
                    castVfxSpawner: null);

                presenter.OnCastBroadcast(new CastBroadcast { CasterPlayerId = 1L, SkillId = 20003 }); // 本地
                presenter.OnCastBroadcast(new CastBroadcast { CasterPlayerId = 3L, SkillId = 50004 }); // NPC

                Assert.AreEqual(0, factoryCalls, "本地施法者与 NPC 都不归 presenter 管");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
