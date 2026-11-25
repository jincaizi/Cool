using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Plan 08 任务 2（M19）：断开会话必须清空权威状态投影表。
    ///
    /// 缺口：`ClientNetworkHost.Disconnect()` 只拆战斗桥/视图，`AuthoritativeStatusVfx._entities`
    /// 与 `StatusVFXSystem._tints` 跨会话存活。重连同 id 的视图因 target 变化会把旧组的
    /// Removed 重新发一遍（旧染色也可能留在已销毁模型上）。
    ///
    /// 装配与 SessionIsolationTests 同款：真实 <see cref="ClientNetworkHost"/>（手工跑生产 Awake），
    /// 远端状态经 registry 基线投影 → StatusesChanged → AuthoritativeStatusVfx → EventBus →
    /// StatusVFXSystem；断开走宿主真实 `Disconnect()`，不直接调 Clear()。
    /// </summary>
    public class StatusVfxDisconnectCleanupTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const long OldPlayerId = 7L;
        private const long NewPlayerId = 70L;
        private const long EntityId = 2L;
        private const float MaxHp = 720f;

        private const int FreezeGroup = 1;
        private const int FreezeEffect = 101;
        private const int BurnGroup = 2;
        private const int BurnEffect = 201;
        private const int StunGroup = 3;
        private const int StunEffect = 301;

        /// <summary>与生产同款 adapter 接缝：按需向消费者投递服务端消息（无 socket）。</summary>
        private sealed class RecordingClient : INetworkClient
        {
            private Action<MessageId, IMessage>? _received;

            public bool IsConnected => true;

            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);

            public Task DisconnectAsync(string reason) => Task.CompletedTask;

            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable) { }

            public event Action<MessageId, IMessage>? MessageReceived
            {
                add => _received += value;
                remove => _received -= value;
            }

            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;

            public void Dispose() { }

            /// <summary>模拟服务端包到达（adapter → manager dispatcher/战斗桥的真实消费路径）。</summary>
            public void Emit(MessageId id, IMessage message) => _received?.Invoke(id, message);
        }

        private sealed class Session
        {
            public RecordingClient Client = null!;
            public ClientNetworkManager Manager = null!;
        }

        private static readonly MethodInfo StatusSystemOnEnable = typeof(StatusVFXSystem).GetMethod(
            "OnEnable", PrivateInstance);
        private static readonly MethodInfo StatusSystemOnDisable = typeof(StatusVFXSystem).GetMethod(
            "OnDisable", PrivateInstance);

        private GameObject _root = null!;
        private ClientNetworkHost _host = null!;
        private GameObject _systemGo = null!;
        private StatusVFXSystem _system = null!;

        private readonly List<StatusEffectAppliedEvent> _applied = new List<StatusEffectAppliedEvent>();
        private readonly List<StatusEffectRemovedEvent> _removed = new List<StatusEffectRemovedEvent>();

        [SetUp]
        public void SetUp()
        {
            _applied.Clear();
            _removed.Clear();

            _root = new GameObject("StatusVfxDisconnectCleanupTests");
            _host = _root.AddComponent<ClientNetworkHost>();
            // EditMode 不会自动派发 Awake（宿主没有 ExecuteAlways）：手工跑一次生产 Awake，
            // 让 registry / _statusVfx 与真实运行时一致。
            if (HostField<object?>("_remoteEntities") == null) InvokeHost("Awake");

            _systemGo = new GameObject("StatusVFX");
            _system = _systemGo.AddComponent<StatusVFXSystem>();
            // 同一原因：EditMode 不会自动派发 OnEnable——手工跑生产订阅。
            Assert.NotNull(StatusSystemOnEnable, "StatusVFXSystem 缺少 OnEnable");
            StatusSystemOnEnable!.Invoke(_system, null);

            EventBus.Subscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Subscribe<StatusEffectRemovedEvent>(OnRemoved);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);

            if (_system != null)
            {
                Assert.NotNull(StatusSystemOnDisable, "StatusVFXSystem 缺少 OnDisable");
                StatusSystemOnDisable!.Invoke(_system, null);
                UnityEngine.Object.DestroyImmediate(_systemGo);
                _system = null!;
                _systemGo = null!;
            }

            // 先销毁宿主（OnDestroy 会退订/Dispose 当前会话），再销毁其余对象。
            if (_root != null)
            {
                InvokeHost("OnDestroy");
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null!;
            }
        }

        private void OnApplied(StatusEffectAppliedEvent e) => _applied.Add(e);

        private void OnRemoved(StatusEffectRemovedEvent e) => _removed.Add(e);

        // ------------------------------------------------------------------------------------
        // ① 断开发一次 Removed/组、投影表与染色清空；同 id 新会话不承接旧状态
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_status_vfx_disconnect_clears_entity_table_and_prevents_cross_session_events()
        {
            // Arrange：旧会话基线投影带冰（组 1）+ 火（组 2），经 registry → 宿主真实状态路径
            var old = OpenSession("StatusVfxDisconnect_Old", OldPlayerId);
            var registry = _host.RemoteEntities!;
            registry.ApplySnapshot(
                PlayerSnapshot(EntityId, Statuses((FreezeEffect, FreezeGroup), (BurnEffect, BurnGroup))),
                serverTicks: 1_000_000L);

            var view = registry.GetView(EntityId);
            Assert.NotNull(view);
            var oldTarget = view!.GameObject.transform;
            Assert.AreEqual(2, _applied.Count, "前置：冰/火两组必须经 EventBus 应用");
            CollectionAssert.AreEquivalent(
                new[] { EffectType.Freeze, EffectType.Burn },
                new[] { _applied[0].EffectType, _applied[1].EffectType });
            Assert.AreEqual(0, _removed.Count);
            Assert.AreEqual(1, EntityTable().Count, "前置：投影表必须有该实体");
            var tints = Tints();
            Assert.AreEqual(1, tints.Count, "前置：StatusVFXSystem 必须有该实体染色条目");

            // Act：宿主真实断线路径（ResetAttachment → _statusVfx.Clear → manager.Dispose）
            _applied.Clear();
            _host.Disconnect();

            // Assert：每个活动组各发一次 Removed，目标仍是旧视图根
            Assert.AreEqual(2, _removed.Count, "断开必须为每个活动组各发一次 Removed");
            CollectionAssert.AreEquivalent(
                new[] { EffectType.Freeze, EffectType.Burn },
                new[] { _removed[0].EffectType, _removed[1].EffectType });
            Assert.AreEqual((int)EntityId, _removed[0].EntityId);
            Assert.AreEqual((int)EntityId, _removed[1].EntityId);
            Assert.AreSame(oldTarget, _removed[0].Target, "Removed 必须针对旧视图根发出");
            Assert.AreSame(oldTarget, _removed[1].Target);
            Assert.AreEqual(0, _applied.Count, "断开不得再发 Applied");
            Assert.AreEqual(0, EntityTable().Count, "断开必须清空实体投影表");
            Assert.AreEqual(0, tints.Count, "StatusVFXSystem 不得残留该实体染色");
            Assert.AreEqual(0, registry.Count, "前置：断线必须清空旧会话视图（证明走的是生产断开路径）");

            // Arrange：新会话同 EntityId 建视图——旧状态不得承接
            _applied.Clear();
            _removed.Clear();
            var fresh = OpenSession("StatusVfxDisconnect_New", NewPlayerId);
            registry.ApplySnapshot(PlayerSnapshot(EntityId, statuses: null),
                serverTicks: 2_000_000L);
            Assert.NotNull(registry.GetView(EntityId), "新会话必须为同 id 建出新视图");

            // Assert：旧表已清 ⇒ 不得冒出旧 Applied/Removed
            Assert.AreEqual(0, _applied.Count, "新会话同 id 视图不得承接旧状态（无 Applied）");
            Assert.AreEqual(0, _removed.Count, "旧投影表已清 ⇒ 新会话不得收到旧组 Removed");
            Assert.AreEqual(0, tints.Count);

            // Act：新会话 318 携带新状态（雷，组 3），走真实桥路径
            fresh.Client.Emit(MessageId.CombatStateUpdate,
                CombatUpdate(EntityId, version: 2, Statuses((StunEffect, StunGroup))));
            HostField<CombatNetworkBridge>("_combatBridge").Update();

            // Assert：后续新状态正常挂载
            Assert.AreEqual(1, _applied.Count, "新会话 318 状态必须正常应用");
            Assert.AreEqual(EffectType.Stun, _applied[0].EffectType);
            Assert.AreEqual((int)EntityId, _applied[0].EntityId);
            Assert.AreEqual(0, _removed.Count);
            Assert.AreEqual(1, tints.Count, "新状态必须建立染色条目");
        }

        // ------------------------------------------------------------------------------------
        // ② 角色重建（保留连接）不得清状态投影表——Clear 只能挂在真正的断开路径
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_status_vfx_character_rebuild_keeps_entity_table()
        {
            // Arrange：状态已应用
            OpenSession("StatusVfxRebuild_Session", OldPlayerId);
            var registry = _host.RemoteEntities!;
            registry.ApplySnapshot(
                PlayerSnapshot(EntityId, Statuses((FreezeEffect, FreezeGroup), (BurnEffect, BurnGroup))),
                serverTicks: 1_000_000L);
            Assert.AreEqual(2, _applied.Count);
            Assert.AreEqual(1, EntityTable().Count);
            _applied.Clear();

            // Act：生产角色重建路径（OnPlayerSpawned → ResetAttachment(keepConnection: true)）
            var reset = typeof(ClientNetworkHost).GetMethod("ResetAttachment", PrivateInstance);
            Assert.NotNull(reset, "ClientNetworkHost 缺少 ResetAttachment");
            reset!.Invoke(_host, new object[] { true });

            // Assert：状态投影必须保留（换根由 Apply 的 target diff 处理，不得整体清空）
            Assert.AreEqual(0, _removed.Count, "角色重建不得发 Removed");
            Assert.AreEqual(1, EntityTable().Count, "角色重建不得清空实体投影表");
            Assert.AreEqual(1, Tints().Count, "角色重建不得清染色条目");
        }

        // ------------------------------------------------------------------------------------
        // 会话夹具（SessionIsolationTests / EchoSessionResetTests 同款）
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
            // 宿主自己的战斗桥/施法表现装配（与生产进世界后同一条路径）
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

        /// <summary>宿主真实 AuthoritativeStatusVfx 的实体投影表（私有字段，沿用既有反射模式）。</summary>
        private IDictionary EntityTable()
        {
            var vfx = HostField<AuthoritativeStatusVfx>("_statusVfx");
            Assert.NotNull(vfx, "宿主必须持有 AuthoritativeStatusVfx");
            var field = typeof(AuthoritativeStatusVfx).GetField("_entities", PrivateInstance);
            Assert.NotNull(field, "AuthoritativeStatusVfx 缺少 _entities");
            return (IDictionary)field!.GetValue(vfx)!;
        }

        private IDictionary Tints()
            => (IDictionary)TestHelpers.GetField(_system, "_tints");

        // ------------------------------------------------------------------------------------
        // 协议夹具
        // ------------------------------------------------------------------------------------

        private static IList<StatusProjection> Statuses(params (int EffectId, int GroupId)[] statuses)
        {
            var list = new List<StatusProjection>();
            foreach (var (effectId, groupId) in statuses)
                list.Add(new StatusProjection { EffectId = effectId, GroupId = groupId });
            return list;
        }

        /// <summary>statuses == null ⇒ 快照不带 Combat 位（新会话建视图，不产生状态事件）。</summary>
        private static EntitySnapshot PlayerSnapshot(long playerId, IList<StatusProjection>? statuses)
        {
            ushort mask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                | EntityFieldMask.Dead | EntityFieldMask.Health);
            if (statuses != null) mask |= (ushort)EntityFieldMask.Combat;
            return new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = mask,
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Dead = false, Hp = MaxHp, MaxHp = MaxHp,
                CombatVersion = 1, LifeId = 1,
                Combat = statuses == null ? null : new CombatProjection
                {
                    CombatVersion = 1,
                    LifeId = 1,
                    Hp = MaxHp,
                    MaxHp = MaxHp,
                    Statuses = statuses
                }
            };
        }

        private static CombatStateUpdate CombatUpdate(long entityId, uint version,
            IList<StatusProjection> statuses)
            => new CombatStateUpdate
            {
                EntityId = entityId,
                Projection = new CombatProjection
                {
                    CombatVersion = version,
                    LifeId = 1,
                    Hp = MaxHp,
                    MaxHp = MaxHp,
                    Statuses = statuses
                }
            };
    }
}
