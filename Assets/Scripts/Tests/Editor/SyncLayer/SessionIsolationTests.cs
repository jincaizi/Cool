using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// Plan 07 任务 3：真实 <see cref="ClientNetworkHost"/> 的会话隔离（断线重连 = 新会话）。
    ///
    /// 装配方式与生产一致（无真实 socket、无测试专用生产 API）：
    ///  - 用测试本地 <see cref="INetworkClient"/> 构造真实 <see cref="ClientNetworkManager"/>，
    ///    经反射写入宿主的 `_manager` / `_adapter`（等价于 `Connect()` 建出的两个字段）；
    ///  - `LoginResponse` 走 manager 的真实 dispatcher（与生产收包同一条路）分配 playerId；
    ///  - 战斗桥/施法表现由宿主自己的 `AttachConnectionCombat()` 装配
    ///    （桥的 localPlayerId 闭包、presenter、adapter 订阅都是生产代码）；
    ///  - 拆装走宿主的 `Disconnect()` → `ResetAttachment()` → `DetachCombatBridge()`，
    ///    不手工解绑任何订阅——隔离必须是宿主做出来的。
    ///
    /// 本文件断言（对应任务 brief 的 4 条 + final review 补充的 NpcAction 缓存隔离）：
    ///  1. 旧会话的 DamageResult / CastBroadcast / CombatStateUpdate（含视图未建时缓存的 318）
    ///     不触发新会话桥事件、不改新视图；
    ///  2. 旧位移预测帧随旧 reporter 丢弃，新会话第一帧不对旧预测对账（帧数/纠偏计数/L2 目标）；
    ///  3. 重连分配新 playerId；旧 playerId 的本地伤害回调不触发（DoT 注入点 = 生产同一条路）；
    ///  4. 旧 presenter 的模型表现与旧位移预测在重连后不得复活任何表现；
    ///  5. 旧会话视图未建时缓存的 NpcAction 不得在重连后回放到同 EntityId/LifeId 的新视图。
    /// </summary>
    public class SessionIsolationTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const long OldPlayerId = 7L;
        private const long NewPlayerId = 70L;
        private const long RemoteEntityId = 2L;
        private const long MissingViewEntityId = 99L;
        private const float MaxHp = 720f;
        private const float AdvancePastIntervalSeconds = 0.1f;

        /// <summary>与生产同款 adapter 接缝：记录出站，按需向消费者投递服务端消息；统计真实订阅者数。</summary>
        private sealed class RecordingClient : INetworkClient
        {
            public readonly List<(MessageId Id, IMessage Message)> Sent = new List<(MessageId, IMessage)>();

            private Action<MessageId, IMessage>? _received;

            /// <summary>MessageReceived 的真实订阅者（manager dispatcher + 战斗桥）。断线后必须归零。</summary>
            public int MessageReceivedSubscriberCount
                => _received?.GetInvocationList().Length ?? 0;

            public bool IsConnected => true;

            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);

            public Task DisconnectAsync(string reason) => Task.CompletedTask;

            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable)
                => Sent.Add((messageId, message));

            public event Action<MessageId, IMessage>? MessageReceived
            {
                add => _received += value;
                remove => _received -= value;
            }

            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;

            public void Dispose() { }

            /// <summary>模拟服务端包到达（与生产同一条 adapter → dispatcher/桥 的消费路径）。</summary>
            public void Emit(MessageId id, IMessage message) => _received?.Invoke(id, message);
        }

        private sealed class FakeVisual : IRemoteProjectileVisual
        {
            public bool IsActive { get; private set; } = true;
            public int Stops;
            public Vector3 LastPoint;

            public void ExplodeAt(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
            public void StopAtPoint(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
        }

        private sealed class Session
        {
            public RecordingClient Client = null!;
            public ClientNetworkManager Manager = null!;
        }

        private GameObject _root = null!;
        private ClientNetworkHost _host = null!;
        private GameObject? _localEntryObject;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SessionIsolationTests");
            _host = _root.AddComponent<ClientNetworkHost>();
            // EditMode 不会自动派发 Awake（宿主没有 ExecuteAlways）：手工跑一次生产 Awake，
            // 让 registry / WeaponBagController 与真实运行时一致。
            if (HostField<object?>("_remoteEntities") == null) InvokeHost("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            // 先销毁宿主（OnDestroy 会退订/Dispose 当前会话），再销毁本地角色替身。
            // EditMode 同样不自动派发 OnDestroy：手工收尾（OnDestroy 对重复调用幂等）。
            if (_root != null)
            {
                InvokeHost("OnDestroy");
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null!;
            }
            if (_localEntryObject != null) UnityEngine.Object.DestroyImmediate(_localEntryObject);
            _localEntryObject = null;
        }

        // ------------------------------------------------------------------------------------
        // ① 旧会话包不得触发新会话桥事件或改新视图
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_old_session_packets_do_not_reach_new_session_bridge_or_views()
        {
            // Arrange：旧会话按生产装配并真实消费三种包（证明旧会话是活的，负断言不是空转）
            var old = OpenSession("SessionIsolation_Old", OldPlayerId);
            Assert.AreEqual(OldPlayerId, old.Manager.LocalPlayerId);
            Assert.AreEqual(2, old.Client.MessageReceivedSubscriberCount,
                "旧会话装配后 adapter 应只有 manager dispatcher 与战斗桥两个订阅者");

            var registry = _host.RemoteEntities!;
            registry.ApplySnapshot(PlayerSnapshot(RemoteEntityId), serverTicks: 1_000_000L);
            var oldView = registry.GetView(RemoteEntityId);
            Assert.NotNull(oldView);
            var oldViewObject = oldView!.GameObject;

            var oldBridge = HostField<CombatNetworkBridge>("_combatBridge");
            int oldDamage = 0, oldCast = 0, oldUpdate = 0;
            oldBridge.OnDamageResult += _ => oldDamage++;
            oldBridge.OnCastBroadcast += _ => oldCast++;
            oldBridge.OnCombatStateUpdate += _ => oldUpdate++;

            old.Client.Emit(MessageId.DamageResult,
                HitResult(RemoteEntityId, eventId: 8801, remainingHp: 360f));
            old.Client.Emit(MessageId.CastBroadcast, Cast(880));
            old.Client.Emit(MessageId.CombatStateUpdate,
                CombatUpdate(RemoteEntityId, version: 2, hp: 360f, weaponInstanceId: 42));
            // 视图未建的 318：registry 先进 _pendingCombat，重连后的新视图不得被它“补挂”旧状态
            old.Client.Emit(MessageId.CombatStateUpdate,
                CombatUpdate(MissingViewEntityId, version: 5, hp: 500f, weaponInstanceId: 42));
            oldBridge.Update();

            Assert.AreEqual(1, oldDamage, "旧会话 DamageResult 必须被消费一次");
            Assert.AreEqual(1, oldCast, "旧会话 CastBroadcast 必须被消费一次");
            Assert.AreEqual(2, oldUpdate,
                "旧会话两条 CombatStateUpdate（实体 2 + 未建视图实体 99 的缓存路径）都必须被消费");
            Assert.AreEqual(0.5f, oldView.Targetable.HPPercent, 0.001f,
                "旧会话命中必须真实改到旧视图（360/720）");
            Assert.AreEqual(360f, oldView.Hp, 0.001f);
            Assert.AreEqual(2u, oldView.CombatVersion);
            Assert.AreEqual(42L, oldView.Equipment!.WeaponInstanceId);
            Assert.IsNull(registry.GetView(MissingViewEntityId),
                "前置：旧会话的 318 面对未建视图只能进缓存，不能凭空建视图");

            // Act：走宿主的真实断线路径（ResetAttachment → DetachCombatBridge → manager.Dispose）
            _host.Disconnect();

            // Assert：旧会话装配被宿主完全拆掉
            Assert.IsNull(HostField<object?>("_manager"), "断线必须丢弃旧 manager（新会话重新登录）");
            Assert.IsNull(HostField<object?>("_adapter"), "断线必须丢弃旧 adapter");
            Assert.IsNull(HostField<object?>("_combatBridge"), "断线必须丢弃旧战斗桥");
            Assert.AreEqual(0, old.Client.MessageReceivedSubscriberCount,
                "断线后旧 adapter 不得再有任何消费者（manager 与桥都必须退订）");
            Assert.AreEqual(0, registry.Count, "断线必须清空旧会话视图");
            Assert.IsTrue(oldViewObject == null, "旧视图 GameObject 必须已销毁");

            // Arrange：新会话（新 playerId、同宿主 registry）
            var fresh = OpenSession("SessionIsolation_New", NewPlayerId);
            Assert.AreEqual(NewPlayerId, fresh.Manager.LocalPlayerId);
            Assert.AreEqual(2, fresh.Client.MessageReceivedSubscriberCount);

            registry.ApplySnapshot(PlayerSnapshot(RemoteEntityId), serverTicks: 2_000_000L);
            registry.ApplySnapshot(PlayerSnapshot(MissingViewEntityId), serverTicks: 2_000_000L);
            var view = registry.GetView(RemoteEntityId);
            var missingView = registry.GetView(MissingViewEntityId);
            Assert.NotNull(view);
            Assert.NotNull(missingView);

            var newBridge = HostField<CombatNetworkBridge>("_combatBridge");
            int newDamage = 0, newCast = 0, newUpdate = 0;
            newBridge.OnDamageResult += _ => newDamage++;
            newBridge.OnCastBroadcast += _ => newCast++;
            newBridge.OnCombatStateUpdate += _ => newUpdate++;

            // Act：旧会话的迟到包从旧 transport 投递（宿主已退订）+ 新桥泵一帧
            old.Client.Emit(MessageId.DamageResult,
                HitResult(RemoteEntityId, eventId: 8901, remainingHp: 1f));
            old.Client.Emit(MessageId.CastBroadcast, Cast(880));
            old.Client.Emit(MessageId.CombatStateUpdate,
                CombatUpdate(RemoteEntityId, version: 9, hp: 1f, weaponInstanceId: 7, dead: true));
            newBridge.Update();

            // Assert：新会话桥事件为零、视图状态一点没动；旧缓存 318 也没有回放到新视图
            Assert.AreEqual(0, newDamage, "旧会话 DamageResult 不得触发新会话桥事件");
            Assert.AreEqual(0, newCast, "旧会话 CastBroadcast 不得触发新会话桥事件");
            Assert.AreEqual(0, newUpdate, "旧会话 CombatStateUpdate 不得触发新会话桥事件");
            Assert.AreEqual(MaxHp, view!.Hp, 0.001f, "旧会话命中不得改写新会话视图血量");
            Assert.AreEqual(1f, view.Targetable.HPPercent, 0.001f);
            Assert.AreEqual(1u, view.CombatVersion, "旧会话高版本 318 不得改写新会话视图");
            Assert.IsFalse(view.Dead, "旧会话死亡标志不得带入新会话");
            Assert.IsNull(view.Equipment, "旧会话装备不得带入新会话");
            Assert.AreEqual(MaxHp, missingView!.Hp, 0.001f,
                "旧会话缓存 318（此前无视图）不得回放到新会话实体");
            Assert.AreEqual(1u, missingView.CombatVersion);
            Assert.IsNull(missingView.Equipment);
            Assert.AreEqual(2, registry.Count, "旧会话包不得凭空创建视图");
            Assert.AreEqual(0, old.Client.MessageReceivedSubscriberCount);

            // Act：正向对照——新会话自己的包（复用旧会话消费过的 EventId/CastId）必须全部生效
            fresh.Client.Emit(MessageId.DamageResult,
                HitResult(RemoteEntityId, eventId: 8801, remainingHp: 360f));
            fresh.Client.Emit(MessageId.CastBroadcast, Cast(880));
            fresh.Client.Emit(MessageId.CombatStateUpdate,
                CombatUpdate(RemoteEntityId, version: 5, hp: 500f, weaponInstanceId: 42));
            newBridge.Update();

            Assert.AreEqual(1, newDamage, "新会话命中必须生效（去重窗口不得继承旧会话）");
            Assert.AreEqual(1, newCast, "新会话施法广播必须生效（CastId 去重窗口必须全新）");
            Assert.AreEqual(1, newUpdate, "新会话 318 必须生效");
            Assert.AreEqual(500f, view.Hp, 0.001f);
            Assert.AreEqual(5u, view.CombatVersion);
            Assert.AreEqual(42L, view.Equipment!.WeaponInstanceId);
        }

        // ------------------------------------------------------------------------------------
        // ② 旧预测帧随旧 reporter 丢弃；新会话第一帧不对旧预测对账
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_old_prediction_frames_do_not_reconcile_after_reconnect()
        {
            // Arrange：旧会话上报 3 帧（seq 1..3 @ x=100/101/102），并真实发生一次纠偏
            var old = OpenSession("SessionIsolation_Old", OldPlayerId);
            var oldPosition = new Vec3(100f, 0f, 0f);
            old.Manager.AttachMovementReporter(() => oldPosition, () => 0f, () => 0f);
            var oldReporter = old.Manager.Reporter;
            Assert.NotNull(oldReporter);

            old.Manager.Update(AdvancePastIntervalSeconds);           // seq 1
            oldPosition = new Vec3(101f, 0f, 0f);
            old.Manager.Update(AdvancePastIntervalSeconds);           // seq 2
            oldPosition = new Vec3(102f, 0f, 0f);
            old.Manager.Update(AdvancePastIntervalSeconds);           // seq 3
            Assert.AreEqual(3, ReconcilerFrameCount(oldReporter!),
                "旧会话应记录 3 个预测帧（每会话 seq 从 1 开始）");

            int oldCorrections = 0;
            old.Manager.OnCorrectionNeeded += (_, __) => oldCorrections++;
            old.Client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = 2, AuthoritativeX = 200f
            });
            old.Manager.Update(0f);                                   // 只 drain，不产生新帧
            Assert.AreEqual(1, oldCorrections, "旧会话 ack 必须真的触发纠偏（证明旧 reconciler 在工作）");
            Assert.AreEqual(200f, oldReporter!.LastCorrectionTarget.X, 0.001f);
            Assert.AreEqual(2, ReconcilerFrameCount(oldReporter),
                "ack seq=2 后 Prune 掉 seq<2 的帧（3 → 2）");

            // Act：宿主断线重连
            _host.Disconnect();

            Assert.IsNull(old.Manager.Reporter,
                "断线必须摘除旧 MovementReporter（旧预测帧随实例丢弃；重连应新建实例——PredictionReconciler 类注释）");
            Assert.AreEqual(0, old.Client.MessageReceivedSubscriberCount,
                "断线后旧 transport 不得再有消费者");
            old.Client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = 3, AuthoritativeX = 500f
            });
            old.Manager.Update(0f);                                   // 若旧订阅仍在，封送的 ack 必须在这里被消费
            Assert.AreEqual(1, oldCorrections, "重连后旧 transport 的 ack 不得再驱动旧会话纠偏");
            Assert.AreEqual(200f, oldReporter.LastCorrectionTarget.X, 0.001f,
                "旧预测目标不得被重连后迟到的 ack 改写");

            // Arrange：新会话（新 manager = 新 reporter = 空 reconciler）
            var fresh = OpenSession("SessionIsolation_New", NewPlayerId);
            var newPosition = new Vec3(60f, 0f, 0f);
            fresh.Manager.AttachMovementReporter(() => newPosition, () => 0f, () => 0f);
            var newReporter = fresh.Manager.Reporter;
            Assert.NotNull(newReporter);
            Assert.AreNotSame(oldReporter, newReporter);
            Assert.AreEqual(0, ReconcilerFrameCount(newReporter!),
                "新会话预测队列必须从空开始（旧帧不得带进来）");

            int newCorrections = 0;
            fresh.Manager.OnCorrectionNeeded += (_, __) => newCorrections++;

            // Act：新会话第一帧（x=60；首次 Update 即上报 seq=1）
            fresh.Manager.Update(AdvancePastIntervalSeconds);
            Assert.AreEqual(1, ReconcilerFrameCount(newReporter!));

            // 旧会话独有的 seq=3——若旧帧仍在，旧预测 x=102 会对 500 触发纠偏；
            // 新 reconciler 里没有 seq=3 ⇒ 不对账。
            fresh.Client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = 3, AuthoritativeX = 500f
            });
            fresh.Manager.Update(0f);
            Assert.AreEqual(0, newCorrections,
                "新会话不得对旧会话预测帧做对账（seq=3 在新 reconciler 里不存在）");

            // 新会话第一帧被 ack 为同一权威位置——若旧 seq=1（x=100）仍在，
            // |100-60|=40 > 0.5 会纠偏；正确实现对的是新帧 (60) ⇒ 偏差 0，不纠偏。
            fresh.Client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = 1, AuthoritativeX = 60f
            });
            fresh.Manager.Update(0f);
            Assert.AreEqual(0, newCorrections, "新会话必须只对账自己的帧（新 seq=1 @ 60）");

            // 正向对照：新会话自己的偏差照常纠偏，目标为服务端权威位置
            fresh.Manager.Update(AdvancePastIntervalSeconds);          // seq 2 @ x=60
            fresh.Client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = 2, AuthoritativeX = 200f
            });
            fresh.Manager.Update(0f);
            Assert.AreEqual(1, newCorrections, "新会话自己的超阈值偏差必须触发纠偏");
            Assert.AreEqual(200f, newReporter!.LastCorrectionTarget.X, 0.001f);
            Assert.AreEqual(1, ReconcilerFrameCount(newReporter), "ack seq=2 后只剩 seq=2 一帧");
        }

        // ------------------------------------------------------------------------------------
        // ③ 新会话新 playerId；旧 LocalPlayerId 的本地伤害回调不触发
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_reconnect_uses_new_player_id_and_drops_old_local_damage_callbacks()
        {
            // Arrange：旧会话 playerId=7，断线后新会话 playerId=70
            var old = OpenSession("SessionIsolation_Old", OldPlayerId);
            Assert.AreEqual(OldPlayerId, old.Manager.LocalPlayerId);

            _host.Disconnect();

            var fresh = OpenSession("SessionIsolation_New", NewPlayerId);
            Assert.AreEqual(NewPlayerId, fresh.Manager.LocalPlayerId, "新登录必须分配新 playerId");
            Assert.AreEqual(NewPlayerId, _host.Manager!.LocalPlayerId,
                "宿主当前会话必须指向新 manager（不是旧 LocalPlayerId）");
            Assert.AreNotEqual(old.Manager.LocalPlayerId, fresh.Manager.LocalPlayerId);

            // 生产同一条本地伤害回调：宿主 AttachConnectionCombat 的闭包读宿主 _localEntry。
            // 用“未 Start 的角色”承载：DoT 注入点只写 _currentHP + 发飘字事件，不碰 FSM。
            _localEntryObject = new GameObject("SessionIsolationLocalEntry");
            var localEntry = _localEntryObject.AddComponent<Sys3CEntry>();
            typeof(Sys3CEntry).GetField("_currentHP", PrivateInstance)!
                .SetValue(localEntry, 100f);
            SetHostField("_localEntry", localEntry);

            var newBridge = HostField<CombatNetworkBridge>("_combatBridge");
            int dotEvents = 0;
            Action<PlayerDotDamageEvent> handler = _ => dotEvents++;
            EventBus.Subscribe(handler);
            try
            {
                // Act：旧 playerId 的包（旧 transport 已死；新桥也会因 localId 不匹配而视为远程）
                old.Client.Emit(MessageId.DamageResult,
                    HitResult(OldPlayerId, eventId: 10101, remainingHp: 10f, kind: DamageKind.Dot));
                fresh.Client.Emit(MessageId.DamageResult,
                    HitResult(OldPlayerId, eventId: 10102, remainingHp: 20f, kind: DamageKind.Dot));
                // 直接伤害与 DoT 共用同一 `hit.TargetPlayerId == LocalPlayerId` 分流门；
                // 旧 id 的直接命中也不得走本地注入（未 Start 角色上若被误判为本地会 NRE）。
                fresh.Client.Emit(MessageId.DamageResult,
                    HitResult(OldPlayerId, eventId: 10103, remainingHp: 5f));
                newBridge.Update();

                // Assert：旧 LocalPlayerId 的本地伤害回调一次都没触发
                Assert.AreEqual(100f, localEntry.CurrentHp, 0.001f,
                    "旧 playerId 的命中不得写新会话本地角色血量");
                Assert.AreEqual(0, dotEvents, "旧 playerId 的 DoT 不得走本地飘字注入");

                // 正向对照：新 playerId 的 DoT 必须走宿主真实注入点
                fresh.Client.Emit(MessageId.DamageResult,
                    HitResult(NewPlayerId, eventId: 10104, remainingHp: 40f, kind: DamageKind.Dot));
                newBridge.Update();
                Assert.AreEqual(40f, localEntry.CurrentHp, 0.001f,
                    "新 playerId 的 DoT 必须经宿主 onDotDamageToLocalPlayer 落到本地角色");
                Assert.AreEqual(1, dotEvents, "新 playerId 的 DoT 必须飘字一次");

                // 旧 transport 上新 playerId 的包也没有消费者（旧桥不会用新 playerId 复活）
                old.Client.Emit(MessageId.DamageResult,
                    HitResult(NewPlayerId, eventId: 10105, remainingHp: 30f, kind: DamageKind.Dot));
                newBridge.Update();
                Assert.AreEqual(40f, localEntry.CurrentHp);
                Assert.AreEqual(1, dotEvents);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            // 收尾路径必须容忍“角色已生成但 Start 未跑（SkillCoordinator 尚未建立）”的真实窗口：
            // OnPlayerSpawned 在 Start 前触发 Connect/Disconnect 时会走到这里（曾 NRE）。
            Assert.DoesNotThrow(() => _host.Disconnect(),
                "断线清理不得因未 Start 角色的空 SkillCoordinator 抛出");
            Assert.IsNull(HostField<object?>("_manager"));
            Assert.IsNull(HostField<object?>("_combatBridge"));
            Assert.AreEqual(0, old.Client.MessageReceivedSubscriberCount,
                "断线后旧 transport 不得再有消费者");
        }

        // ------------------------------------------------------------------------------------
        // ④ 旧模型回调/旧位移预测在重连后不得复活旧表现
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_old_model_and_prediction_callbacks_do_not_revive_presentation()
        {
            // Arrange：旧会话 presenter 里塞一条“旧模型正在播”的投射物跟踪项
            var old = OpenSession("SessionIsolation_Old", OldPlayerId);
            var oldPresenter = HostField<RemoteCastPresenter>("_remoteCastPresenter");
            var visual = new FakeVisual();
            oldPresenter.TrackForTest(RemoteEntityId, 10002, visual);
            Assert.AreEqual(1, oldPresenter.TrackedCountForTest,
                "前置：旧 presenter 必须真的跟踪着一条表现");

            // 旧会话位移预测也真实工作一次
            var oldPosition = new Vec3(50f, 0f, 0f);
            old.Manager.AttachMovementReporter(() => oldPosition, () => 0f, () => 0f);
            var oldReporter = old.Manager.Reporter;
            Assert.NotNull(oldReporter);
            int oldCorrections = 0;
            old.Manager.OnCorrectionNeeded += (_, __) => oldCorrections++;
            old.Manager.Update(AdvancePastIntervalSeconds);            // seq 1 @ x=50
            old.Client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = 1, AuthoritativeX = 60f
            });
            old.Manager.Update(0f);
            Assert.AreEqual(1, oldCorrections);
            Assert.AreEqual(60f, oldReporter!.LastCorrectionTarget.X, 0.001f);

            // Act：宿主断线重连
            _host.Disconnect();

            // Assert：旧 presenter 被宿主 Clear 并丢弃；旧 report 被摘除
            Assert.IsNull(HostField<object?>("_remoteCastPresenter"),
                "断线必须丢弃旧施法表现 presenter");
            Assert.AreEqual(0, oldPresenter.TrackedCountForTest,
                "DetachCombatBridge 必须 Clear 旧 presenter 的跟踪项（旧模型不得继续被驱动）");
            Assert.IsNull(old.Manager.Reporter);

            // Arrange：新会话
            var fresh = OpenSession("SessionIsolation_New", NewPlayerId);
            var newPresenter = HostField<RemoteCastPresenter>("_remoteCastPresenter");
            Assert.AreNotSame(oldPresenter, newPresenter, "新会话必须是全新 presenter");
            Assert.AreEqual(0, newPresenter.TrackedCountForTest,
                "新 presenter 不得继承旧会话的跟踪项");

            // Act：模拟旧桥/旧模型在重连后仍回调一次（stale callback）
            oldPresenter.OnDamageResult(
                HitResult(RemoteEntityId, eventId: 10201, remainingHp: 1f));

            // Assert：旧表现不被“停点/爆炸”，新表现也不被触碰
            Assert.AreEqual(0, visual.Stops,
                "旧模型回调不得再操作已 Clear 的表现（无跟踪项 ⇒ 不得停点/爆炸）");
            Assert.AreEqual(0, newPresenter.TrackedCountForTest,
                "旧模型回调不得把表现挂到新 presenter 上");

            // 旧位移预测同样不得复活：重连后旧 transport 的 ack 不驱动任何纠偏
            Assert.AreEqual(0, old.Client.MessageReceivedSubscriberCount,
                "断线后旧 transport 不得再有消费者");
            old.Client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = 1, AuthoritativeX = 70f
            });
            old.Manager.Update(0f);                                   // 若旧订阅仍在，封送的 ack 必须在这里被消费
            Assert.AreEqual(1, oldCorrections, "重连后旧预测链路必须完全失活");
            Assert.AreEqual(60f, oldReporter.LastCorrectionTarget.X, 0.001f,
                "旧纠偏目标不得被重连后的迟到 ack 改写");

            // 新会话没有 MovementReporter（角色尚未装配）⇒ 旧预测不可能借新 manager 复活
            Assert.IsNull(fresh.Manager.Reporter);
        }

        // ------------------------------------------------------------------------------------
        // ⑤ 旧会话缓存的 NpcAction 不得回放到新会话同 Id/LifeId 的视图
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_old_session_cached_npc_action_does_not_replay_into_new_session_view()
        {
            // Arrange：旧会话期间，视图未建的 NpcAction 进 registry 有界缓存
            var old = OpenSession("SessionIsolation_Old", OldPlayerId);
            var registry = _host.RemoteEntities!;
            const long npcEntityId = 77L;
            Assert.IsNull(registry.GetView(npcEntityId));
            registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = npcEntityId,
                LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 4242,
                    Revision = 1,
                    Kind = NpcActionKind.HitReaction,
                    Phase = NpcActionPhase.Active
                }
            });
            Assert.IsNull(registry.GetView(npcEntityId),
                "前置：NpcAction 面对未建视图只能进缓存，不能凭空建视图");

            // Act：宿主真实断线（ResetAttachment → DetachCombatBridge → manager.Dispose → registry.Clear）
            _host.Disconnect();

            // Arrange：新会话用同一 EntityId / LifeId 建视图（旧缓存若未清，会在 ApplySnapshot 里回放）
            var fresh = OpenSession("SessionIsolation_New", NewPlayerId);
            Assert.AreEqual(NewPlayerId, fresh.Manager.LocalPlayerId);
            registry.ApplySnapshot(PlayerSnapshot(npcEntityId), serverTicks: 2_000_000L);
            var view = registry.GetView(npcEntityId);
            Assert.NotNull(view);
            Assert.AreEqual(1u, view!.LifeId,
                "前置：新视图必须与旧缓存同 LifeId（否则回放条件本身不成立）");

            // Assert：旧动作实例一点没写进新视图
            Assert.AreEqual(0ul, view.ActionInstanceId,
                "旧会话缓存的 NpcAction 不得在重连后回放到同 Id/LifeId 的新视图");
            Assert.AreEqual(0u, view.ActionRevision);
        }

        // ------------------------------------------------------------------------------------
        // 会话夹具
        // ------------------------------------------------------------------------------------

        private Session OpenSession(string username, long playerId)
        {
            var client = new RecordingClient();
            var manager = new ClientNetworkManager(client, _host.RemoteEntities!, username);
            // 生产 Connect() 的同一条事件接线（宿主自己的 HandleEnterWorld）
            manager.OnEnterWorld += () => InvokeHost("HandleEnterWorld");
            SetHostField("_manager", manager);
            SetHostField("_adapter", client);

            // 进入世界：真实 dispatcher 分配 LocalPlayerId（不调 ConnectAndLogin 的 async void）
            client.Emit(MessageId.LoginResponse, new LoginResponse
            {
                Success = true,
                PlayerId = playerId,
                ServerTickRateMs = 66
            });
            // 宿主自己的战斗桥/施法表现装配（与 TryAttachLocalCharacter 内同一条生产路径）
            InvokeHost("AttachConnectionCombat");
            return new Session { Client = client, Manager = manager };
        }

        private T HostField<T>(string name)
        {
            var field = typeof(ClientNetworkHost).GetField(name, PrivateInstance);
            Assert.NotNull(field, "ClientNetworkHost 缺少字段 " + name);
            return (T)field!.GetValue(_host)!;
        }

        private void SetHostField(string name, object? value)
        {
            var field = typeof(ClientNetworkHost).GetField(name, PrivateInstance);
            Assert.NotNull(field, "ClientNetworkHost 缺少字段 " + name);
            field!.SetValue(_host, value);
        }

        private void InvokeHost(string methodName)
        {
            var method = typeof(ClientNetworkHost).GetMethod(methodName, PrivateInstance);
            Assert.NotNull(method, "ClientNetworkHost 缺少方法 " + methodName);
            method!.Invoke(_host, null);
        }

        /// <summary>读 MovementReporter 私有 reconciler 的帧数（包内反射沿用既有测试模式）。</summary>
        private static int ReconcilerFrameCount(MovementReporter reporter)
        {
            var field = typeof(MovementReporter).GetField("_reconciler", PrivateInstance);
            Assert.NotNull(field, "MovementReporter 缺少 _reconciler 字段");
            return ((PredictionReconciler)field!.GetValue(reporter)!).FrameCount;
        }

        // ------------------------------------------------------------------------------------
        // 协议夹具
        // ------------------------------------------------------------------------------------

        private static EntitySnapshot PlayerSnapshot(long playerId)
            => new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Dead | EntityFieldMask.Health),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Dead = false, Hp = MaxHp, MaxHp = MaxHp,
                CombatVersion = 1, LifeId = 1
            };

        private static CombatStateUpdate CombatUpdate(long entityId, uint version, float hp,
            long weaponInstanceId, bool dead = false)
            => new CombatStateUpdate
            {
                EntityId = entityId,
                Projection = new CombatProjection
                {
                    CombatVersion = version,
                    LifeId = 1,
                    Hp = hp,
                    MaxHp = MaxHp,
                    Dead = dead,
                    IsDefending = false,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = weaponInstanceId, TemplateId = 90002, ElementId = 1
                    },
                    Statuses = new List<StatusProjection>()
                }
            };

        private static DamageResult HitResult(long targetId, ulong eventId, float remainingHp,
            DamageKind kind = DamageKind.Direct)
            => new DamageResult
            {
                SourcePlayerId = 2L,
                SkillId = kind == DamageKind.Dot ? 30001 : 10002,
                SourceKind = (int)EntityKind.Npc,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = targetId,
                        Damage = 10f,
                        TargetRemainingHp = remainingHp,
                        DamageKind = kind,
                        EventId = eventId
                    }
                }
            };

        private static CastBroadcast Cast(ulong castId)
            => new CastBroadcast
            {
                CastId = castId,
                CasterPlayerId = RemoteEntityId,
                SkillId = 10002,
                AttackSpeedMultiplier = 1f
            };
    }
}
