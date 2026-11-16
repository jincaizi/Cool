using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>属性剑气 v1：本人/旁观者按 CastId 唯一创建剑气，终止只停指定一发。</summary>
    public class RemoteCastPresenterCastIdTests
    {
        private const string CatalogPath = "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset";

        private sealed class FakeVisual : IRemoteProjectileVisual
        {
            public bool IsActive { get; private set; } = true;
            public int Stops;
            public Vector3 LastPoint;

            public void ExplodeAt(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
            public void StopAtPoint(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
        }

        private static SkillPresentationCatalog LoadCatalog()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(CatalogPath);
            Assert.NotNull(catalog, "SkillPresentationCatalog.asset 未找到");
            return catalog;
        }

        private static ProjectileLaunchBroadcast Launch(ulong castId, long casterId = 2L,
            string scene = "room-a", long timestamp = 1_000_000L)
            => new ProjectileLaunchBroadcast
            {
                CastId = castId,
                CasterPlayerId = casterId,
                CasterLifeId = 1,
                CombatSceneId = scene,
                LaunchTick = 6,
                OriginX = 0f,
                OriginY = 0f,
                OriginZ = 0f,
                Yaw = 0f,
                Speed = 18f,
                Range = 12f,
                ElementId = 1,
                SkillId = 10002,
                ServerTimestamp = timestamp
            };

        private static ProjectileStopBroadcast Stop(ulong castId, string scene = "room-a")
            => new ProjectileStopBroadcast
            {
                CastId = castId,
                CasterPlayerId = 2L,
                CasterLifeId = 1,
                CombatSceneId = scene,
                StopTick = 10,
                Reason = ProjectileStopReason.Blocked,
                StopX = 0f,
                StopZ = 5f
            };

        [Test]
        public void Launch_Creates_One_Visual_Per_CastId_And_Duplicates_Are_Ignored()
        {
            int calls = 0;
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => { calls++; return visual; },
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileLaunch(Launch(77));
            presenter.OnProjectileLaunch(Launch(77));

            Assert.AreEqual(1, calls, "同一 CastId 只能创建一个剑气实例");
            Assert.AreEqual(1, presenter.CastVisualCountForTest);
        }

        [Test]
        public void Stop_Only_Affects_The_Named_CastId()
        {
            var visualA = new FakeVisual();
            var visualB = new FakeVisual();
            int index = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) =>
                    index++ == 0 ? visualA : visualB,
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileLaunch(Launch(100));
            presenter.OnProjectileLaunch(Launch(101));
            presenter.OnProjectileStop(Stop(101));

            Assert.AreEqual(0, visualA.Stops, "连续两发同技能时格挡只停指定的那一发");
            Assert.AreEqual(1, visualB.Stops);
            Assert.AreEqual(new Vector3(0f, 0f, 5f), visualB.LastPoint);
            Assert.AreEqual(1, presenter.CastVisualCountForTest);
        }

        [Test]
        public void Stop_Before_Launch_Skips_Later_Visual()
        {
            int calls = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => { calls++; return new FakeVisual(); },
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileStop(Stop(200));
            presenter.OnProjectileLaunch(Launch(200));

            Assert.AreEqual(0, calls, "终止先到时不应补生成剑气");
        }

        [Test]
        public void Duplicate_Stop_Is_Idempotent()
        {
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => visual,
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileLaunch(Launch(300));
            presenter.OnProjectileStop(Stop(300));
            presenter.OnProjectileStop(Stop(300));

            Assert.AreEqual(1, visual.Stops);
        }

        [Test]
        public void Other_Combat_Scene_Is_Ignored()
        {
            int calls = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => { calls++; return new FakeVisual(); },
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileLaunch(Launch(400, scene: "room-b"));
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Late_Launch_Starts_At_Server_Progress()
        {
            float captured = -1f;
            long now = 1_000_000L + (long)(0.5f * System.TimeSpan.TicksPerSecond);
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, startDistance) =>
                {
                    captured = startDistance;
                    return new FakeVisual();
                },
                sceneIdProvider: () => "room-a",
                serverNowTicksProvider: () => now);

            presenter.OnProjectileLaunch(Launch(500, timestamp: 1_000_000L));

            Assert.AreEqual(9f, captured, 0.01f, "延迟 0.5s 的发射按 18m/s 补 9m 进度");
        }

        [Test]
        public void Local_Caster_Also_Gets_Authoritative_SwordQi()
        {
            int calls = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 2L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => { calls++; return new FakeVisual(); },
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileLaunch(Launch(600, casterId: 2L));
            Assert.AreEqual(1, calls, "本人剑气也由权威发射描述生成（本地执行器不再生成视觉）");
        }

        [Test]
        public void Cancel_Before_Launch_Skips_Later_Visual()
        {
            int calls = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => { calls++; return new FakeVisual(); },
                sceneIdProvider: () => "room-a");

            presenter.OnCastCancel(new CastCancelBroadcast { CastId = 500 });
            presenter.OnProjectileLaunch(Launch(500));

            Assert.AreEqual(0, calls, "cancel arrived first must suppress the later launch");
        }

        [Test]
        public void Cancel_After_Launch_Keeps_Projectile_Flying()
        {
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => visual,
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileLaunch(Launch(501));
            presenter.OnCastCancel(new CastCancelBroadcast { CastId = 501 });

            Assert.AreEqual(1, presenter.CastVisualCountForTest,
                "an already launched projectile keeps flying after cancel");
            Assert.AreEqual(0, visual.Stops);
        }

        [Test]
        public void Cancel_Before_Cast_Broadcast_Skips_All_Presentation()
        {
            var root = new GameObject("CancelBeforeCastBroadcast");
            try
            {
                var registry = new RemoteEntityRegistry(root.transform);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2L,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 0f, Y = 0f, Z = 0f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: 1_000_000L);

                int factoryCalls = 0;
                var presenter = new RemoteCastPresenter(registry, localPlayerId: 1L, LoadCatalog(),
                    projectileFactory: (_, __, ___) => { factoryCalls++; return new FakeVisual(); },
                    sceneIdProvider: () => "room-a");

                presenter.OnCastCancel(new CastCancelBroadcast { CastId = 700 });
                presenter.OnCastBroadcast(new CastBroadcast
                {
                    CastId = 700, CasterPlayerId = 2L, SkillId = 20003,
                    AttackSpeedMultiplier = 1f
                });

                Assert.AreEqual(0, factoryCalls,
                    "cancel arrived first must skip the whole cast presentation");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ActiveCast_Plays_Once_Per_CastId()
        {
            var presenter = new RemoteCastPresenter(
                registry: null, localPlayerId: 1L, catalog: null);

            presenter.OnActiveCast(2L, new ActiveCastProjection { CastId = 91, SkillId = 10002 });
            presenter.OnActiveCast(2L, new ActiveCastProjection
            {
                CastId = 91, SkillId = 10002, Phase = NpcActionPhase.Recovery
            });
            presenter.OnActiveCast(2L, new ActiveCastProjection { CastId = 92, SkillId = 10002 });

            Assert.AreEqual(2, presenter.ActiveCastPlayedCountForTest,
                "phase updates of the same CastId must not replay the attack");

            presenter.Clear();
            Assert.AreEqual(0, presenter.ActiveCastPlayedCountForTest,
                "Clear() must reset the replay dedupe set");
        }
    }
}
