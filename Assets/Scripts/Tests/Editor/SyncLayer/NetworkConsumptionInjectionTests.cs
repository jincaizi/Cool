using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// Plan 05 任务 4：收包消费层的重复/乱序注入回归。
    /// 正常 KCP/WebSocket 流程不会重复投递；本文件把「万一重复/乱序」的后果钉死：
    ///  - 同一 <c>HitEntry.EventId</c> 的 DamageResult 重投只生效一次（旧服务端 EventId=0 不去重）；
    ///  - 新版本 CombatStateUpdate 之后到达的旧版本不得让 registry 的状态/装备回退；
    ///  - ProjectileStop 先于 Launch / 重复发射终止按 CastId 幂等；
    ///  - 重复 CastBroadcast 同一 CastId 只起手一次攻击。
    /// 全部经 <see cref="CombatNetworkBridge"/> 的入队 → Update 消费路径（宿主真实路径），
    /// 不直接调下游；presenter 用 <c>RemoteCastPresenterCastIdTests</c> 的假视觉接缝。
    /// </summary>
    public class NetworkConsumptionInjectionTests
    {
        private const string CatalogPath = "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset";
        private const long LocalPlayerId = 7L;

        private sealed class RecordingClient : INetworkClient
        {
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable) { }
            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;
            public void Dispose() { }
        }

        private sealed class FakeVisual : IRemoteProjectileVisual
        {
            public bool IsActive { get; private set; } = true;
            public int Stops;
            public Vector3 LastPoint;

            public void ExplodeAt(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
            public void StopAtPoint(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
        }

        private GameObject _root = null!;
        private RecordingClient _client = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("NetworkConsumptionInjectionTests");
            _client = new RecordingClient();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private static DamageResult DirectHit(ulong eventId, long targetId = LocalPlayerId,
            float damage = 10f, float remainingHp = 90f)
            => new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 20001,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = targetId,
                        Damage = damage,
                        TargetRemainingHp = remainingHp,
                        EventId = eventId
                    }
                }
            };

        private static EntitySnapshot PlayerSnapshot(long playerId)
            => new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Dead | EntityFieldMask.Health),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 1, LifeId = 1
            };

        private static CombatStateUpdate CombatUpdate(long entityId, uint version, float hp,
            long weaponInstanceId, bool defending = false, bool dead = false)
            => new CombatStateUpdate
            {
                EntityId = entityId,
                Projection = new CombatProjection
                {
                    CombatVersion = version,
                    LifeId = 1,
                    Hp = hp,
                    MaxHp = 720f,
                    Dead = dead,
                    IsDefending = defending,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = weaponInstanceId, TemplateId = 90002, ElementId = 1
                    }
                }
            };

        // ------------------------------------------------------------------------------------
        // ① DamageResult 重投：同一 EventId 只生效一次，不同 EventId 各自生效
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_duplicate_damage_result_invokes_local_hit_sink_once()
        {
            // Arrange
            var registry = new RemoteEntityRegistry(_root.transform);
            int calls = 0;
            float lastRemainingHp = -1f;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    calls++;
                    lastRemainingHp = remainingHp;
                });

            // Act：同一条命中（同一 EventId，独立解码副本）投递两次
            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9001));
            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9001));
            bridge.Update();

            // Assert
            Assert.AreEqual(1, calls,
                "同一 EventId 重投不得重复走本地受击注入点（受击 FSM/飘字会二次触发）");
            Assert.AreEqual(90f, lastRemainingHp, 0.001f, "权威余血应按首次投递生效");

            // Act：不同 EventId 是新的命中
            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9002, damage: 5f, remainingHp: 85f));
            bridge.Update();

            // Assert
            Assert.AreEqual(2, calls, "不同 EventId 必须各自生效");
            Assert.AreEqual(85f, lastRemainingHp, 0.001f);
        }

        [Test]
        public void test_duplicate_damage_result_emits_float_text_once()
        {
            // Arrange：远程怪物视图（MonsterTakeDamageEvent 是红字飘字来源）
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_001,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.TemplateId | EntityFieldMask.Health),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Npc, TemplateId = 1,
                Hp = 100f, MaxHp = 100f
            }, serverTicks: 1_000_000L);

            int floatTextEvents = 0;
            Action<MonsterTakeDamageEvent> handler = _ => floatTextEvents++;
            EventBus.Subscribe(handler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);

                // Act
                bridge.OnMessage(MessageId.DamageResult,
                    DirectHit(eventId: 9101, targetId: 2_000_001, damage: 57.4f, remainingHp: 42.6f));
                bridge.OnMessage(MessageId.DamageResult,
                    DirectHit(eventId: 9101, targetId: 2_000_001, damage: 57.4f, remainingHp: 42.6f));
                bridge.Update();

                // Assert
                Assert.AreEqual(1, floatTextEvents, "同一 EventId 重投只能飘一次伤害数字");
                var view = registry.GetView(2_000_001);
                Assert.NotNull(view);
                Assert.AreEqual(0.426f, view!.Targetable.HPPercent, 0.001f,
                    "重复投递不得让权威余血二次写入");

                // Act：新 EventId
                bridge.OnMessage(MessageId.DamageResult,
                    DirectHit(eventId: 9102, targetId: 2_000_001, damage: 10f, remainingHp: 32.6f));
                bridge.Update();

                // Assert
                Assert.AreEqual(2, floatTextEvents, "不同 EventId 必须各自飘字");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }

        [Test]
        public void test_duplicate_dot_damage_result_applies_once()
        {
            // DoT 走独立注入点（飘字/权威余血），同一跳的重投同样只能生效一次
            var registry = new RemoteEntityRegistry(_root.transform);
            int dotCalls = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                onDotDamageToLocalPlayer: (_, __, ___) => dotCalls++);

            DamageResult DotHit() => new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 30001,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = LocalPlayerId, Damage = 12f, TargetRemainingHp = 88f,
                        DamageKind = DamageKind.Dot, EventId = 9201
                    }
                }
            };

            bridge.OnMessage(MessageId.DamageResult, DotHit());
            bridge.OnMessage(MessageId.DamageResult, DotHit());
            bridge.Update();

            Assert.AreEqual(1, dotCalls, "同一 EventId 的 DoT 跳伤重投只能生效一次");
        }

        [Test]
        public void test_clear_dedupe_resets_event_and_cast_windows()
        {
            // 断线/重建会换新桥；ClearDedupe 保证即使桥对象被复用，旧会话的
            // EventId / CastId 窗口都不再拦截。
            var registry = new RemoteEntityRegistry(_root.transform);
            int calls = 0, castEvents = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) => calls++);
            bridge.OnCastBroadcast += _ => castEvents++;

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9301));
            bridge.OnMessage(MessageId.CastBroadcast,
                new CastBroadcast { CastId = 930, CasterPlayerId = 2L, SkillId = 10002 });
            bridge.OnMessage(MessageId.CastBroadcast,
                new CastBroadcast { CastId = 930, CasterPlayerId = 2L, SkillId = 10002 });
            bridge.Update();
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, castEvents, "重复 CastBroadcast 在 ClearDedupe 前只触发一次");

            bridge.ClearDedupe();
            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9301));
            bridge.OnMessage(MessageId.CastBroadcast,
                new CastBroadcast { CastId = 930, CasterPlayerId = 2L, SkillId = 10002 });
            bridge.Update();

            Assert.AreEqual(2, calls, "ClearDedupe 后同一 EventId 必须重新生效");
            Assert.AreEqual(2, castEvents, "ClearDedupe 后同一 CastId 必须重新生效");
        }

        [Test]
        public void test_duplicate_damage_result_raises_bridge_event_once()
        {
            // 宿主把 OnDamageResult 转给 presenter 做投射物停点对齐；整条命中的重投
            // 不得再次触发（重复火球消息会把下一发视觉消费掉）。
            var registry = new RemoteEntityRegistry(_root.transform);
            int events = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnDamageResult += _ => events++;

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9401));
            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9401));
            bridge.Update();
            Assert.AreEqual(1, events, "整条命中全重复时不得触发 OnDamageResult");

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9402));
            bridge.Update();
            Assert.AreEqual(2, events, "出现新 EventId 时 OnDamageResult 必须再次触发");
        }

        [Test]
        public void test_damage_result_with_one_new_hit_still_raises_event()
        {
            // 同一消息里既有重复 hit 又有新 hit：消息仍须交给上层，且只跳过重复的那条
            var registry = new RemoteEntityRegistry(_root.transform);
            int calls = 0, events = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) => calls++);
            bridge.OnDamageResult += _ => events++;

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9501, damage: 10f, remainingHp: 90f));
            bridge.Update();
            Assert.AreEqual(1, calls);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 20001,
                Hits =
                {
                    new HitEntry { TargetPlayerId = LocalPlayerId, Damage = 10f, TargetRemainingHp = 90f, EventId = 9501 },
                    new HitEntry { TargetPlayerId = LocalPlayerId, Damage = 5f, TargetRemainingHp = 85f, EventId = 9502 }
                }
            });
            bridge.Update();

            Assert.AreEqual(2, calls, "重复 hit 跳过，新 hit 仍须生效");
            Assert.AreEqual(2, events, "含新 hit 的消息仍须触发 OnDamageResult");
        }

        [Test]
        public void test_hitless_damage_result_still_raises_bridge_event()
        {
            // 无 hit 的消息没有可去重的命中（旧服务端停点/空结果），不得被“全重复”判据吞掉
            var registry = new RemoteEntityRegistry(_root.transform);
            int events = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnDamageResult += _ => events++;

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2L,
                SkillId = 10002,
                ProjectileStopped = true,
                StopX = 0f,
                StopZ = 6f
            });
            bridge.Update();

            Assert.AreEqual(1, events, "无 hit 的消息不是重复消息，仍须交给上层");
        }

        [Test]
        public void test_zero_event_id_keeps_legacy_no_dedupe_behavior()
        {
            // EventId=0 = 旧服务端未分配：不得去重（否则两下同值命中会被误吞）
            var registry = new RemoteEntityRegistry(_root.transform);
            int calls = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) => calls++);

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 0));
            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 0));
            bridge.Update();

            Assert.AreEqual(2, calls, "EventId=0（旧服务端）必须保持不去重的既有行为");
        }

        // ------------------------------------------------------------------------------------
        // ② CombatStateUpdate 乱序：旧版本必须在 registry 版本门前被丢弃
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_older_combat_state_update_after_newer_does_not_roll_back()
        {
            // Arrange：基线版本 1
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(PlayerSnapshot(2L), serverTicks: 1_000_000L);
            var view = registry.GetView(2L);
            Assert.NotNull(view, "视图未创建——ApplySnapshot Position/Health 位应触发");

            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            // 宿主同款接线：远端投影交 registry 版本合并
            bridge.OnCombatStateUpdate += registry.ApplyCombatStateUpdate;

            // Act：版本 5 先到，版本 4 后到（乱序注入）
            bridge.OnMessage(MessageId.CombatStateUpdate,
                CombatUpdate(2L, version: 5, hp: 500f, weaponInstanceId: 42, defending: true));
            bridge.OnMessage(MessageId.CombatStateUpdate,
                CombatUpdate(2L, version: 4, hp: 1f, weaponInstanceId: 7, defending: false, dead: true));
            bridge.Update();

            // Assert
            Assert.AreEqual(5u, view!.CombatVersion, "旧版本必须在版本门前被丢弃");
            Assert.AreEqual(500f, view.Hp, 0.001f, "血量不得被旧版本回退");
            Assert.IsFalse(view.Dead, "死亡标志不得被旧版本回退");
            Assert.IsTrue(view.IsDefending, "格挡状态不得被旧版本回退");
            Assert.NotNull(view.Equipment);
            Assert.AreEqual(42L, view.Equipment!.WeaponInstanceId, "装备不得被旧版本回退");
        }

        // ------------------------------------------------------------------------------------
        // ③ 剑气发射/终止乱序与重复：按 CastId 幂等（复用 presenter 假视觉接缝）
        // ------------------------------------------------------------------------------------

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
        public void test_stop_before_launch_suppresses_late_visual()
        {
            // 终止先到 = 该发剑气已经结束：晚到的发射（含重复）不得补建视觉
            int factoryCalls = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) =>
                {
                    factoryCalls++;
                    return new FakeVisual();
                },
                sceneIdProvider: () => "room-a");
            var bridge = new CombatNetworkBridge(_client, () => 1L, new RemoteEntityRegistry(_root.transform));
            bridge.OnProjectileLaunch += presenter.OnProjectileLaunch;
            bridge.OnProjectileStop += presenter.OnProjectileStop;

            bridge.OnMessage(MessageId.ProjectileStopBroadcast, Stop(200));
            bridge.OnMessage(MessageId.ProjectileLaunchBroadcast, Launch(200));
            bridge.OnMessage(MessageId.ProjectileLaunchBroadcast, Launch(200)); // 重复发射
            bridge.Update();

            Assert.AreEqual(0, factoryCalls, "终止先到后，晚到的发射不得补生成剑气");
            Assert.AreEqual(0, presenter.CastVisualCountForTest);
        }

        [Test]
        public void test_duplicate_launch_and_stop_are_idempotent()
        {
            var visual = new FakeVisual();
            int factoryCalls = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) =>
                {
                    factoryCalls++;
                    return visual;
                },
                sceneIdProvider: () => "room-a");
            var bridge = new CombatNetworkBridge(_client, () => 1L, new RemoteEntityRegistry(_root.transform));
            bridge.OnProjectileLaunch += presenter.OnProjectileLaunch;
            bridge.OnProjectileStop += presenter.OnProjectileStop;

            bridge.OnMessage(MessageId.ProjectileLaunchBroadcast, Launch(201));
            bridge.OnMessage(MessageId.ProjectileLaunchBroadcast, Launch(201));
            bridge.OnMessage(MessageId.ProjectileStopBroadcast, Stop(201));
            bridge.OnMessage(MessageId.ProjectileStopBroadcast, Stop(201));
            bridge.Update();

            Assert.AreEqual(1, factoryCalls, "同一 CastId 的重复发射只能创建一个视觉");
            Assert.AreEqual(1, visual.Stops, "重复终止必须幂等（只停一次）");
            Assert.AreEqual(new Vector3(0f, 0f, 5f), visual.LastPoint, "终止必须按停点结束");
            Assert.AreEqual(0, presenter.CastVisualCountForTest);
        }

        // ------------------------------------------------------------------------------------
        // ④ CastBroadcast 重复：同一 CastId 只起手一次攻击
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_duplicate_cast_broadcast_starts_attack_once()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2L,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                X = 0f, Y = 0f, Z = 0f, Kind = (byte)EntityKind.Player, Tick = 1
            }, serverTicks: 1_000_000L);
            var view = registry.GetView(2L);
            Assert.NotNull(view, "视图未创建");
            var animator = view!.GameObject.AddComponent<RemoteEntityAnimator>();
            view.Animator = animator;   // internal setter（InternalsVisibleTo）

            var presenter = new RemoteCastPresenter(registry, localPlayerId: LocalPlayerId, LoadCatalog(),
                sceneIdProvider: () => "room-a");
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            // 宿主同款接线：presenter 播玩家表现 + registry 排 NPC 挥砍（玩家在这里是 no-op）
            bridge.OnCastBroadcast += b =>
            {
                presenter.OnCastBroadcast(b);
                registry.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp, b.SkillId);
            };

            CastBroadcast Cast(ulong castId) => new CastBroadcast
            {
                CastId = castId, CasterPlayerId = 2L, SkillId = 10002,
                AttackSpeedMultiplier = 1f
            };

            bridge.OnMessage(MessageId.CastBroadcast, Cast(88));
            bridge.OnMessage(MessageId.CastBroadcast, Cast(88));
            bridge.Update();

            Assert.AreEqual(1, animator.AttackStartsForTest,
                "同一 CastId 的重复 CastBroadcast 只能起手一次攻击");

            bridge.OnMessage(MessageId.CastBroadcast, Cast(89));
            bridge.Update();

            Assert.AreEqual(2, animator.AttackStartsForTest, "不同 CastId 必须各自起手");
        }

        [Test]
        public void test_duplicate_cast_broadcast_raises_bridge_event_once()
        {
            // 桥层门：重复包不再触发 OnCastBroadcast——NPC 排程、玩家 presenter、
            // CastVFX/投射物工厂/environment 表现全部只消费一次。
            var registry = new RemoteEntityRegistry(_root.transform);
            int raised = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnCastBroadcast += _ => raised++;

            CastBroadcast Cast(ulong castId) => new CastBroadcast
            {
                CastId = castId, CasterPlayerId = 2L, SkillId = 10002
            };

            bridge.OnMessage(MessageId.CastBroadcast, Cast(800));
            bridge.OnMessage(MessageId.CastBroadcast, Cast(800));
            bridge.Update();
            Assert.AreEqual(1, raised, "同一非零 CastId 只能触发一次 OnCastBroadcast");

            bridge.OnMessage(MessageId.CastBroadcast, Cast(801));
            bridge.Update();
            Assert.AreEqual(2, raised, "不同 CastId 必须各自触发");
        }

        [Test]
        public void test_duplicate_cast_broadcast_reaches_npc_registry_once()
        {
            // NPC 分支：重复 CastBroadcast 不得给怪物排第二次挥砍（跨帧重排会把正在播的攻击
            // 重新起手）。真预制体 + 真 Animator，与 DemoDay 的 templateId 4 接线同款。
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Monstor/fantasySpider/SpiderMonster.prefab");
            Assert.IsNotNull(prefab, "真预制体未找到");
            var root = new GameObject("NpcCastDedupeRoot");
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { 4, prefab } });
            try
            {
                long npcId = 2_000_006L;
                long baseTicks = 638_000_000_000_000_000L;
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = npcId,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                        | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                    X = 1f, Y = 0f, Z = 2f,
                    Kind = (byte)EntityKind.Npc, TemplateId = 4,
                    ActionState = (byte)NpcAnimationState.Idle, Tick = 1
                }, serverTicks: baseTicks);

                var view = registry.GetView(npcId);
                Assert.IsNotNull(view?.NpcAnimator, "怪物视图应带驱动");
                view!.NpcAnimator!.ResolveAnimator();
                var animator = view.GameObject.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator);

                var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
                int scheduled = 0;
                // 宿主同款接线：CastBroadcast → registry 排程
                bridge.OnCastBroadcast += b =>
                {
                    scheduled++;
                    registry.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp, b.SkillId);
                };

                long castTicks = baseTicks + 2L * NpcAttackSchedule.VariantBucketTicks;
                int variant = NpcAttackSchedule.VariantOf(castTicks, 50006);
                string expected = variant == 0 ? "Attack01" : "Attack02";
                CastBroadcast NpcCast() => new CastBroadcast
                {
                    CastId = 900, CasterPlayerId = npcId, SkillId = 50006, ServerTimestamp = castTicks
                };

                bridge.OnMessage(MessageId.CastBroadcast, NpcCast());
                bridge.OnMessage(MessageId.CastBroadcast, NpcCast());
                bridge.Update();

                Assert.AreEqual(1, scheduled,
                    "重复 CastBroadcast 不得给 NPC 排第二次挥砍（registry 分支必须收到一次）");

                // 唯一一次排程真的播到 NpcAnimator 上（证明 NPC 分支确实被执行，而不是被跳过）
                registry.UpdateRender(nowServerTicks: castTicks + 1L, renderDelayTicks: 0L);
                Settle(animator!);
                Assert.IsTrue(animator!.GetCurrentAnimatorStateInfo(0).IsName(expected),
                    "唯一一次排程必须真的播到 NpcAnimator 上");
            }
            finally
            {
                registry.Clear();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>推帧直到过渡落定（与 CombatNetworkBridgeTests.Settle 同款）。</summary>
        private static void Settle(Animator animator)
        {
            int guard = 0;
            do
            {
                animator.Update(0.05f);
            } while (++guard < 20 && animator.IsInTransition(0));
        }
    }
}
