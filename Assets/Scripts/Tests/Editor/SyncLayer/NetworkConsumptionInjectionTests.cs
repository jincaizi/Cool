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
            /// <summary>出站消息（任务 3 断言换装/施法请求携带的版本基准）。</summary>
            public readonly List<(MessageId Id, IMessage Message)> Sent = new();

            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable) => Sent.Add((messageId, message));
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
            float damage = 10f, float remainingHp = 90f, uint targetLifeId = 0)
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
                        EventId = eventId,
                        TargetLifeId = targetLifeId
                    }
                }
            };

        /// <summary>DoT 跳伤（EventId / TargetLifeId / CombatVersion 随包下发）。</summary>
        private static DamageResult DotResult(ulong eventId, long targetId,
            float damage = 10f, float remainingHp = 90f, uint targetLifeId = 0,
            uint combatVersion = 0)
            => new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 30001,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = targetId,
                        Damage = damage,
                        TargetRemainingHp = remainingHp,
                        DamageKind = DamageKind.Dot,
                        EventId = eventId,
                        TargetLifeId = targetLifeId,
                        CombatVersion = combatVersion
                    }
                }
            };

        private static EntitySnapshot PlayerSnapshot(long playerId, uint lifeId = 1)
            => new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Dead | EntityFieldMask.Health),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 1, LifeId = lifeId
            };

        private static CombatStateUpdate CombatUpdate(long entityId, uint version, float hp,
            long weaponInstanceId, bool defending = false, bool dead = false,
            uint lifeId = 1, IList<StatusProjection>? statuses = null)
            => new CombatStateUpdate
            {
                EntityId = entityId,
                Projection = new CombatProjection
                {
                    CombatVersion = version,
                    LifeId = lifeId,
                    Hp = hp,
                    MaxHp = 720f,
                    Dead = dead,
                    IsDefending = defending,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = weaponInstanceId, TemplateId = 90002, ElementId = 1
                    },
                    // 默认空列表；StatusesEqual 把 null 与空列表同样按 0 项处理，无需单独区分。
                    Statuses = statuses ?? new List<StatusProjection>()
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

        // ------------------------------------------------------------------------------------
        // ⑤ 延迟/交错注入（Plan 06 任务 3）：只经桥的 OnMessage/Update 入队，
        //    用确定性的消息顺序模拟"迟到"，不引入真实等待。
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_equip_confirm_after_cast_confirm_keeps_revision_and_equipment()
        {
            // 施法确认（携带服务端冻结的装备版本 5）先到，版本 5 的换装确认延迟到达，
            // 旧版本 4 的换装确认再晚到后。乱序不得让旧装备回退，也不得改写施法确认的版本。
            var registry = new RemoteEntityRegistry(_root.transform);
            int castConfirms = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnCastConfirm += _ => castConfirms++;

            bridge.OnMessage(MessageId.CastConfirm, new CastConfirm
            {
                ClientCastSeq = 1, Accepted = true, CastId = 501, SkillId = 10002,
                CasterLifeId = 1, EquipmentRevision = 5
            });
            bridge.OnMessage(MessageId.EquipWeaponConfirm, new EquipWeaponConfirm
            {
                ClientEquipSeq = 1, Accepted = true, EquipmentRevision = 5,
                WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
            });
            bridge.OnMessage(MessageId.EquipWeaponConfirm, new EquipWeaponConfirm
            {
                ClientEquipSeq = 2, Accepted = true, EquipmentRevision = 4,
                WeaponInstanceId = 7, TemplateId = 90001, ElementId = 1
            });
            bridge.Update();

            // CastConfirm 是原样转发（桥不改写任何字段，其 EquipmentRevision 只证明消息被
            // 转发一次，不构成交错保护证据）；交错保护的价值在下面的版本门与出站基准。
            Assert.AreEqual(1, castConfirms, "施法确认必须转发一次");
            Assert.AreEqual(5u, bridge.Equipment.Revision, "旧换装确认不得回退新装备");
            Assert.AreEqual(42L, bridge.Equipment.WeaponInstanceId);
            Assert.IsTrue(bridge.TryGetAcceptedCastId(10002, lifeId: 1, out ulong castId));
            Assert.AreEqual(501UL, castId, "施法确认必须按序号被记住");

            // 下一次出站请求仍以已确认的版本 5 为基准（旧确认没有污染版本门）
            bridge.RequestEquip(43);
            var sent = _client.Sent[_client.Sent.Count - 1];
            Assert.AreEqual(MessageId.EquipWeaponRequest, sent.Id);
            var request = (EquipWeaponRequest)sent.Message;
            Assert.AreEqual(5u, request.KnownEquipmentRevision);
            Assert.AreEqual(43L, request.WeaponInstanceId);
        }

        [Test]
        public void test_launch_delayed_after_stop_does_not_rebuild_visual()
        {
            // 停止结果先被消费（视觉尚未建），发射在下一帧才迟到：不得补建；
            // 反过来"建视觉 → 停止 → 迟到重发发射"同样不得重建。
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

            bridge.OnMessage(MessageId.ProjectileStopBroadcast, Stop(210));
            bridge.Update();
            bridge.OnMessage(MessageId.ProjectileLaunchBroadcast, Launch(210));
            bridge.Update();

            Assert.AreEqual(0, factoryCalls, "停止结果消费后，晚到的发射不得重建视觉");
            Assert.AreEqual(0, presenter.CastVisualCountForTest);

            bridge.OnMessage(MessageId.ProjectileLaunchBroadcast, Launch(211));
            bridge.Update();
            bridge.OnMessage(MessageId.ProjectileStopBroadcast, Stop(211));
            bridge.Update();
            Assert.AreEqual(1, factoryCalls);

            bridge.OnMessage(MessageId.ProjectileLaunchBroadcast, Launch(211));
            bridge.Update();
            Assert.AreEqual(1, factoryCalls, "已停止的 CastId 迟到重发发射不得重建视觉");
        }

        [Test]
        public void test_old_life_damage_after_respawn_is_dropped()
        {
            // RespawnNotice 由宿主消费（ApplyServerRespawn → Equipment.SetLifeId）；
            // 桥层可见的新代次是 Equipment.LifeId。此后旧 LifeId 的命中不得扣血/飘字。
            var registry = new RemoteEntityRegistry(_root.transform);
            int calls = 0, events = 0;
            float lastRemaining = -1f;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    calls++;
                    lastRemaining = remainingHp;
                });
            bridge.OnDamageResult += _ => events++;

            bridge.Equipment.SetLifeId(2);

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9701, targetLifeId: 1));
            bridge.Update();
            Assert.AreEqual(0, calls, "复活后旧 LifeId 的命中不得再走本地受击注入");
            Assert.AreEqual(0, events, "整条被代次门丢弃时不得触发 OnDamageResult");

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9702, targetLifeId: 2,
                damage: 5f, remainingHp: 85f));
            bridge.Update();
            Assert.AreEqual(1, calls, "当前世代的命中必须正常生效");
            Assert.AreEqual(85f, lastRemaining, 0.001f);

            // TargetLifeId=0 = 旧服务端未分配：保持原有行为，不按代次丢弃
            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9703, targetLifeId: 0,
                damage: 5f, remainingHp: 80f));
            bridge.Update();
            Assert.AreEqual(2, calls);
        }

        [Test]
        public void test_older_projection_after_newer_baseline_does_not_roll_back_state()
        {
            // 较新的血量/状态由基线快照先落（版本 7）：更早版本的 318 晚到时
            // 不得回退血量/死亡/格挡/装备，也不得清空状态。
            var registry = new RemoteEntityRegistry(_root.transform);
            int statusEvents = 0;
            registry.StatusesChanged += (_, __, ___) => statusEvents++;

            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2L,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Dead | EntityFieldMask.Health | EntityFieldMask.Defending
                    | EntityFieldMask.Combat),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Dead = false, Hp = 300f, MaxHp = 720f, IsDefending = true,
                CombatVersion = 7, LifeId = 1,
                Combat = new CombatProjection
                {
                    CombatVersion = 7, LifeId = 1, Hp = 300f, MaxHp = 720f,
                    Dead = false, IsDefending = true,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                    },
                    Statuses = new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 1, GroupId = 1 }
                    }
                }
            }, serverTicks: 1_000_000L);

            var view = registry.GetView(2L);
            Assert.NotNull(view);
            int eventsAfterBaseline = statusEvents;
            Assert.AreEqual(1, eventsAfterBaseline, "基线状态必须挂载一次");

            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnCombatStateUpdate += registry.ApplyCombatStateUpdate;

            bridge.OnMessage(MessageId.CombatStateUpdate,
                CombatUpdate(2L, version: 6, hp: 1f, weaponInstanceId: 7, defending: false, dead: true));
            bridge.Update();

            Assert.AreEqual(7u, view!.CombatVersion, "旧版本必须在版本门前被丢弃");
            Assert.AreEqual(300f, view.Hp, 0.001f, "已应用的血量不得被旧版本回退");
            Assert.IsFalse(view.Dead);
            Assert.IsTrue(view.IsDefending);
            Assert.AreEqual(42L, view.Equipment!.WeaponInstanceId, "装备不得被旧版本回退");
            Assert.AreEqual(1, view.Statuses.Count, "状态不得被旧版本清空");
            Assert.AreEqual(eventsAfterBaseline, statusEvents, "旧版本不得触发状态变化");
        }

        [Test]
        public void test_delayed_duplicate_event_id_is_consumed_once_across_updates()
        {
            // 同一 EventId 的独立解码副本跨帧迟到：去重窗口跨 Update 有效。
            var registry = new RemoteEntityRegistry(_root.transform);
            int calls = 0;
            float lastRemaining = -1f;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    calls++;
                    lastRemaining = remainingHp;
                });

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9801, damage: 10f, remainingHp: 90f));
            bridge.Update();
            Assert.AreEqual(1, calls);

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9802, damage: 5f, remainingHp: 85f));
            bridge.Update();
            Assert.AreEqual(2, calls);

            bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9801, damage: 10f, remainingHp: 90f));
            bridge.Update();
            Assert.AreEqual(2, calls, "延迟到达的同一 EventId 仍只消费一次");
            Assert.AreEqual(85f, lastRemaining, 0.001f, "重投不得改写权威余血");
        }

        // ------------------------------------------------------------------------------------
        // ⑥ 换命（respawn / change-life）后的旧世代包。注意：这不是断线重连——重连会由
        //    服务端分配新 playerId，跨会话隔离靠宿主（退订 adapter + 新桥 + registry.Clear，
        //    见本段末尾的新会话用例）。桥没有会话代次字段，同一会话的换命靠 LifeId 门
        //    丢弃旧包；位移预测属宿主（MovementReporter/SyncCore），不在桥的职责内。
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_respawn_drops_old_generation_damage_and_cast()
        {
            // 换命（复活，LifeId 1→2）：同一会话、同一实体 id，换命前的包迟到。
            // RespawnNotice 由宿主消费（ApplyServerRespawn → Equipment.SetLifeId）；
            // 桥层可见的新代次是 Equipment.LifeId，远程视图由新快照更新 LifeId。
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(PlayerSnapshot(2L), serverTicks: 1_000_000L);
            Assert.NotNull(registry.GetView(2L));

            int localCalls = 0, damageEvents = 0, castEvents = 0;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) => localCalls++);
            bridge.OnDamageResult += _ => damageEvents++;
            bridge.OnCastBroadcast += _ => castEvents++;

            bridge.Equipment.SetLifeId(2);
            registry.ApplySnapshot(PlayerSnapshot(2L, lifeId: 2), serverTicks: 2_000_000L);
            var view = registry.GetView(2L);
            Assert.NotNull(view);
            Assert.AreEqual(2u, view!.LifeId);

            int takeDamageEvents = 0;
            Action<PlayerTakeDamageEvent> handler = _ => takeDamageEvents++;
            EventBus.Subscribe(handler);
            try
            {
                // 换命前（LifeId=1）的包迟到：生命代次门必须挡住
                bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9901, targetLifeId: 1));
                bridge.OnMessage(MessageId.DamageResult,
                    DirectHit(eventId: 9902, targetId: 2L, targetLifeId: 1, remainingHp: 1f));
                bridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
                {
                    CastId = 990, CasterPlayerId = 2L, SkillId = 10002, CasterLifeId = 1
                });
                bridge.Update();

                Assert.AreEqual(0, localCalls, "换命前命中不得走本地受击注入");
                Assert.AreEqual(0, damageEvents, "旧世代命中不得触发 OnDamageResult");
                Assert.AreEqual(0, castEvents, "旧世代施法广播不得触发桥层事件（NPC 排程/玩家表现）");
                Assert.AreEqual(0, takeDamageEvents, "旧世代命中不得在远程视图上飘字");
                Assert.AreEqual(1f, view.Targetable.HPPercent, 0.001f,
                    "旧世代命中不得回退新生命血条");

                // 新世代的包（复用同一 ID 数值）必须正常生效：旧包不得占用去重窗口
                bridge.OnMessage(MessageId.DamageResult, DirectHit(eventId: 9901, targetLifeId: 2,
                    damage: 5f, remainingHp: 85f));
                bridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
                {
                    CastId = 990, CasterPlayerId = 2L, SkillId = 10002, CasterLifeId = 2
                });
                bridge.Update();

                Assert.AreEqual(1, localCalls, "新世代命中必须生效");
                Assert.AreEqual(1, damageEvents);
                Assert.AreEqual(1, castEvents, "新世代施法广播必须生效");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }

        [Test]
        public void test_respawn_old_projection_does_not_override_new_life_view()
        {
            // 换命（LifeId 1→2）后，旧生命的高版本 318 迟到：registry 的 LifeId 门必须丢弃
            //（版本高也不行）。同一会话、同一实体 id，不经过 registry.Clear。
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2L,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Dead | EntityFieldMask.Health | EntityFieldMask.Combat),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Hp = 200f, MaxHp = 720f, CombatVersion = 5, LifeId = 1,
                Combat = new CombatProjection
                {
                    CombatVersion = 5, LifeId = 1, Hp = 200f, MaxHp = 720f,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                    }
                }
            }, serverTicks: 1_000_000L);
            var view = registry.GetView(2L);
            Assert.NotNull(view);
            Assert.AreEqual(1u, view!.LifeId);

            // 换命后的新基线（LifeId=2）落在同一视图上
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2L,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Dead | EntityFieldMask.Health | EntityFieldMask.Combat),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Hp = 720f, MaxHp = 720f, CombatVersion = 1, LifeId = 2,
                Combat = new CombatProjection
                {
                    CombatVersion = 1, LifeId = 2, Hp = 720f, MaxHp = 720f,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 99, TemplateId = 90002, ElementId = 2
                    }
                }
            }, serverTicks: 2_000_000L);
            Assert.AreEqual(2u, view.LifeId);
            Assert.AreEqual(720f, view.Hp, 0.001f);

            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnCombatStateUpdate += registry.ApplyCombatStateUpdate;

            bridge.OnMessage(MessageId.CombatStateUpdate,
                CombatUpdate(2L, version: 9, hp: 1f, weaponInstanceId: 7, defending: false,
                    dead: true, lifeId: 1));
            bridge.Update();

            Assert.AreEqual(2u, view.LifeId, "换命前投影不得改写新生命代次");
            Assert.AreEqual(1u, view.CombatVersion, "即使版本更高，旧代次也必须丢弃");
            Assert.AreEqual(720f, view.Hp, 0.001f, "换命前血量不得覆盖新生命");
            Assert.IsFalse(view.Dead);
            Assert.AreEqual(99L, view.Equipment!.WeaponInstanceId, "换命前装备不得覆盖新生命");
        }

        [Test]
        public void test_new_session_bridge_owns_fresh_equipment_and_dedupe_state()
        {
            // 真正的跨会话隔离在宿主：DetachCombatBridge 退订旧 adapter → 新登录分配新
            // playerId → 新 CombatNetworkBridge + 新 Equipment + registry.Clear。
            // 桥没有会话代次字段，不能独自识别"旧会话的包"；本用例只钉宿主重建后桥实际
            // 拥有的状态：装备摘要/去重窗口从空开始，旧桥的消费记录不会带进新会话。
            const long oldLocalId = 7L;
            const long newLocalId = 70L;
            const long oldEntityId = 2L;
            const long newEntityId = 22L;

            // 旧会话：桥消费过 EventId=9901 / CastId=990，随后宿主退订并丢弃旧桥
            var oldRegistry = new RemoteEntityRegistry(_root.transform);
            oldRegistry.ApplySnapshot(PlayerSnapshot(oldEntityId), serverTicks: 1_000_000L);
            var oldBridge = new CombatNetworkBridge(_client, () => oldLocalId, oldRegistry);
            oldBridge.OnMessage(MessageId.DamageResult,
                DirectHit(eventId: 9901, targetId: oldEntityId, remainingHp: 1f));
            oldBridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
            {
                CastId = 990, CasterPlayerId = oldEntityId, SkillId = 10002
            });
            oldBridge.Update();
            oldBridge.ClearDedupe();
            oldBridge.ClearEchoDedupe();
            oldRegistry.Clear();

            // 新会话：新 playerId、新 registry、新桥（装备摘要由登录基线初始化）
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(PlayerSnapshot(newEntityId), serverTicks: 2_000_000L);
            var view = registry.GetView(newEntityId);
            Assert.NotNull(view);
            int localCalls = 0, castEvents = 0;
            var bridge = new CombatNetworkBridge(_client, () => newLocalId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) => localCalls++);
            bridge.OnCastBroadcast += _ => castEvents++;
            bridge.Equipment.InitializeFromBaseline(new WeaponInventoryBaseline
            {
                LifeId = 1, EquipmentRevision = 3, WeaponInstanceId = 99,
                TemplateId = 90002, ElementId = 2
            });

            // 旧会话的包（旧 id / 旧 CastId）若真的到达新桥：桥无法识别它们属于旧会话，
            // 但不得改写新会话的实体/装备/本地血量（隔离由宿主的新 playerId/新桥保证）。
            bridge.OnMessage(MessageId.DamageResult,
                DirectHit(eventId: 8801, targetId: oldEntityId, remainingHp: 1f));
            bridge.OnMessage(MessageId.DamageResult,
                DirectHit(eventId: 8802, targetId: oldLocalId, remainingHp: 1f));
            bridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
            {
                CastId = 880, CasterPlayerId = oldEntityId, SkillId = 10002
            });
            bridge.Update();

            Assert.AreEqual(0, localCalls, "旧会话命中不得命中新会话的本地玩家（playerId 已换）");
            Assert.AreEqual(1f, view!.Targetable.HPPercent, 0.001f,
                "旧会话命中不得改写新会话实体血条");
            Assert.AreEqual(3u, bridge.Equipment.Revision, "旧会话包不得改写新会话装备摘要");
            Assert.AreEqual(99L, bridge.Equipment.WeaponInstanceId);
            Assert.AreEqual(1, registry.Count, "旧会话包不得凭空创建视图");
            Assert.AreEqual(1, castEvents,
                "桥不能独自识别旧会话的施法广播（宿主隔离保证它不会到达）；无视图时下游 no-op");

            // 旧桥消费过的 EventId/CastId 不得到新桥：新会话复用同值必须正常生效
            bridge.OnMessage(MessageId.DamageResult,
                DirectHit(eventId: 9901, targetId: newLocalId, remainingHp: 85f));
            bridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
            {
                CastId = 990, CasterPlayerId = newEntityId, SkillId = 10002
            });
            bridge.Update();

            Assert.AreEqual(1, localCalls, "新桥的去重窗口必须从空开始（不继承旧桥消费记录）");
            Assert.AreEqual(2, castEvents, "新会话的施法广播必须照常触发");
        }

        // ------------------------------------------------------------------------------------
        // ⑦ 模型未加载：视图未建时先到的 318 / DoT 由缓存 + 基线补齐，不得丢失/重复。
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_missing_view_projection_replays_after_same_version_health_baseline()
        {
            // 视图未建：318 先进 registry 缓存；基线快照建视图后回放。
            // 基线只带 Health（无 Combat 位）且版本相同：缓存里的装备/格挡/状态仍须补挂。
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnCombatStateUpdate += registry.ApplyCombatStateUpdate;
            int statusEvents = 0;
            registry.StatusesChanged += (_, __, ___) => statusEvents++;

            long npcId = 2_000_021L;
            bridge.OnMessage(MessageId.CombatStateUpdate, CombatUpdate(npcId, version: 5, hp: 500f,
                weaponInstanceId: 42, defending: true, lifeId: 1,
                statuses: new List<StatusProjection>
                {
                    new StatusProjection { EffectId = 1, GroupId = 1 }
                }));
            bridge.Update();
            Assert.IsNull(registry.GetView(npcId), "视图未建前不得凭空创建");

            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = npcId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.TemplateId | EntityFieldMask.Health | EntityFieldMask.Dead),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Npc, TemplateId = 1,
                Hp = 500f, MaxHp = 720f, CombatVersion = 5, LifeId = 1
            }, serverTicks: 1_000_000L);

            var view = registry.GetView(npcId);
            Assert.NotNull(view);
            Assert.AreEqual(5u, view!.CombatVersion, "同版本缓存投影必须应用（基线未携带 Combat 位）");
            Assert.AreEqual(500f, view.Hp, 0.001f);
            Assert.IsTrue(view.IsDefending, "缓存里的格挡状态必须补挂");
            Assert.NotNull(view.Equipment);
            Assert.AreEqual(42L, view.Equipment!.WeaponInstanceId, "缓存里的装备必须补挂");
            Assert.AreEqual(1, view.Statuses.Count, "缓存里的状态必须补挂");
            Assert.AreEqual(1, statusEvents, "状态变化只触发一次");
        }

        [Test]
        public void test_missing_view_dot_is_filled_by_baseline_and_not_double_applied()
        {
            // 视图未建：DoT 跳伤的表层表现可丢（EventId 仍被消费），权威余血由
            // 后续基线补齐；同一 EventId 重投不得补扣/补飘，新跳必须正常生效一次。
            var registry = new RemoteEntityRegistry(_root.transform);
            long npcId = 2_000_031L;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            bridge.OnCombatStateUpdate += registry.ApplyCombatStateUpdate;
            int damageEvents = 0;
            bridge.OnDamageResult += _ => damageEvents++;
            int dotFloats = 0;
            Action<MonsterDotDamageEvent> handler = _ => dotFloats++;
            EventBus.Subscribe(handler);
            try
            {
                bridge.OnMessage(MessageId.CombatStateUpdate, CombatUpdate(npcId, version: 5, hp: 100f,
                    weaponInstanceId: 42));
                bridge.OnMessage(MessageId.DamageResult, DotResult(eventId: 311, targetId: npcId,
                    damage: 10f, remainingHp: 90f, targetLifeId: 1, combatVersion: 6));
                bridge.Update();
                Assert.IsNull(registry.GetView(npcId));
                Assert.AreEqual(1, damageEvents, "无视图的 DoT 仍须交给上层（表现可丢）");
                Assert.AreEqual(0, dotFloats, "无视图时不得凭空飘字");

                // 建视图的基线（命中后版本 6）携带 DoT 后的权威余血与完整投影
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = npcId,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                        | EntityFieldMask.TemplateId | EntityFieldMask.Health | EntityFieldMask.Dead
                        | EntityFieldMask.Combat),
                    X = 1f, Y = 0f, Z = 2f,
                    Kind = (byte)EntityKind.Npc, TemplateId = 1,
                    Hp = 90f, MaxHp = 720f, CombatVersion = 6, LifeId = 1,
                    Combat = new CombatProjection
                    {
                        CombatVersion = 6, LifeId = 1, Hp = 90f, MaxHp = 720f,
                        Equipment = new EquipmentSummary
                        {
                            WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                        }
                    }
                }, serverTicks: 1_000_000L);

                var view = registry.GetView(npcId);
                Assert.NotNull(view);
                Assert.AreEqual(6u, view!.CombatVersion, "缓存里的旧版本（v5）必须被基线（v6）丢弃");
                Assert.AreEqual(90f, view.Hp, 0.001f,
                    "基线必须补齐无视图期间丢失的 DoT 权威余血");
                Assert.AreEqual(90f / 720f, view.Targetable.HPPercent, 0.001f);

                // 延迟重投同一 EventId：视图建好也不得补扣/补飘
                bridge.OnMessage(MessageId.DamageResult, DotResult(eventId: 311, targetId: npcId,
                    damage: 10f, remainingHp: 90f, targetLifeId: 1, combatVersion: 6));
                bridge.Update();
                Assert.AreEqual(0, dotFloats, "无视图时已消费的 EventId 重投不得补飘字");
                Assert.AreEqual(90f / 720f, view.Targetable.HPPercent, 0.001f, "重投不得二次扣减");

                // 下一跳正常生效一次
                bridge.OnMessage(MessageId.DamageResult, DotResult(eventId: 312, targetId: npcId,
                    damage: 10f, remainingHp: 80f, targetLifeId: 1, combatVersion: 7));
                bridge.Update();
                Assert.AreEqual(1, dotFloats, "视图建好后的新 DoT 跳必须飘一次");
                Assert.AreEqual(80f / 720f, view.Targetable.HPPercent, 0.001f);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
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
