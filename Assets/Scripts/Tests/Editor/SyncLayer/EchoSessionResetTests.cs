using System;
using System.Reflection;
using System.Threading.Tasks;
using Cool.Protocol;
using GameSys.EditorTests;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// Plan 08 任务 1：echo 最后应用状态必须随连接代次复位。
    ///
    /// 缺口：`ClearEchoRequest` 只清请求字段，`_lastAppliedEchoCastId/_lastAppliedEchoStartTick/
    /// _endedEchoCastId` 跨会话残留。服务器重启后 StartTick 从新值域重新计时（如 500 → 1），
    /// 旧状态会把新 Started 永久判旧；旧 Ended 也会压制新会话同 CastId 的 Started。
    ///
    /// 装配复用 SessionIsolationTests 的宿主反射夹具：真实 <see cref="ClientNetworkHost"/> +
    /// 测试 transport（无 socket），`LoginResponse` 走 manager 真实 dispatcher 分配 playerId，
    /// 战斗桥/施法表现由宿主自己的 `AttachConnectionCombat()` 装配。Echo 状态从
    /// `MessageId.SwordQiEchoState` 经真实 <see cref="CombatNetworkBridge"/> 泵入宿主处理器——
    /// 与生产收包同一条路，不直接调私有处理器。
    ///
    /// `Connect()` 会建真实 WebSocket 拨号（本任务不得触网），新会话按生产 Connect 的
    /// 装配结果复现（`_manager`/`_adapter` + `HandleEnterWorld` + `AttachConnectionCombat`）；
    /// 复位由宿主 `Disconnect()` 的真实会话清理路径驱动——它与 `Connect()` 共用
    /// `ClearEchoRequest()` 这一复位点。
    /// </summary>
    public class EchoSessionResetTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const long OldPlayerId = 7L;
        private const long NewPlayerId = 70L;
        private const ulong OldCastId = 100ul;
        private const ulong NewCastId = 1ul;
        private const uint OldStartTick = 500u;
        private const uint NewStartTick = 1u;
        private const uint EchoWindowTicks = 20u;   // 1.33s @ 15Hz

        /// <summary>与生产同款 adapter 接缝：记录出站，按需向消费者投递服务端消息。</summary>
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

            /// <summary>模拟服务端包到达（adapter → manager dispatcher/战斗桥的消费路径）。</summary>
            public void Emit(MessageId id, IMessage message) => _received?.Invoke(id, message);
        }

        private sealed class Session
        {
            public RecordingClient Client = null!;
            public ClientNetworkManager Manager = null!;
        }

        private GameObject _root = null!;
        private ClientNetworkHost _host = null!;
        private GameObject _localEntryObject = null!;
        private SwordQiEchoSkillData _echoSkill = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("EchoSessionResetTests");
            _host = _root.AddComponent<ClientNetworkHost>();
            // EditMode 不会自动派发 Awake（宿主没有 ExecuteAlways）：手工跑一次生产 Awake，
            // 让 registry / WeaponBagController 与真实运行时一致。
            if (HostField<object?>("_remoteEntities") == null) InvokeHost("Awake");

            // 真实本地角色组件（Start 未跑）：宿主 echo 处理器只读 SkillCoordinator，
            // 注入一个带 SwordQiEcho 数据的真实协调器（与 Start 里同类；FakeTarget 见 TestHelpers）。
            _localEntryObject = new GameObject("EchoSessionResetLocalEntry");
            var localEntry = _localEntryObject.AddComponent<Sys3CEntry>();
            _echoSkill = ScriptableObject.CreateInstance<SwordQiEchoSkillData>();
            TestHelpers.SetField(_echoSkill, "_skillId", (int)SkillID.SwordQiEcho);
            TestHelpers.SetField(_echoSkill, "_cooldown", 12f);
            var coordinator = new SkillCoordinator(new TestHelpers.FakeTarget(localEntry.transform));
            coordinator.RegisterSkill(_echoSkill);
            TestHelpers.SetField(localEntry, "_skillCoordinator", coordinator);
            SetHostField("_localEntry", localEntry);
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
            _localEntryObject = null!;
            if (_echoSkill != null) UnityEngine.Object.DestroyImmediate(_echoSkill);
            _echoSkill = null!;
        }

        // ------------------------------------------------------------------------------------
        // ① 断线 → 新会话：旧最后应用状态归零，服务器重启后的低 StartTick 状态必须被接受
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_echo_session_reset_accepts_new_state_after_reconnect()
        {
            // Arrange：旧会话真实应用 Started（CastId=100 / StartTick=500）
            var old = OpenSession("EchoReset_Old", OldPlayerId);
            Deliver(old.Client, Started(OldPlayerId, OldCastId, OldStartTick));
            Assert.AreEqual(OldCastId, HostField<ulong>("_lastAppliedEchoCastId"),
                "前置：旧会话 Started 必须经真实桥路径应用一次");
            Assert.AreEqual(OldStartTick, HostField<uint>("_lastAppliedEchoStartTick"));

            // Act：宿主真实断线路径（ResetAttachment → DetachCombatBridge → ClearEchoRequest）
            _host.Disconnect();

            // Assert：三个应用状态全部归零（请求字段之外的门也必须复位）
            Assert.AreEqual(0ul, HostField<ulong>("_lastAppliedEchoCastId"),
                "断线必须复位最后应用的 CastId");
            Assert.AreEqual(0u, HostField<uint>("_lastAppliedEchoStartTick"),
                "断线必须复位最后应用的 StartTick");
            Assert.AreEqual(0ul, HostField<ulong>("_endedEchoCastId"),
                "断线必须复位 Ended 去重窗口");

            // Arrange：新会话（新 playerId）
            var fresh = OpenSession("EchoReset_New", NewPlayerId);
            Assert.AreEqual(NewPlayerId, fresh.Manager.LocalPlayerId);

            // Act：服务器重启后的新状态——StartTick=1 小于旧会话的 500
            Deliver(fresh.Client, Started(NewPlayerId, NewCastId, NewStartTick));

            // Assert：必须被接受（不得被旧 StartTick=500 永久判旧）
            Assert.AreEqual(NewCastId, HostField<ulong>("_lastAppliedEchoCastId"),
                "服务器重启后的低 StartTick 状态必须在新会话被接受");
            Assert.AreEqual(NewStartTick, HostField<uint>("_lastAppliedEchoStartTick"));
        }

        // ------------------------------------------------------------------------------------
        // ② 旧会话 Ended 不得压制新会话同 CastId 的 Started
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_echo_session_reset_clears_ended_cast_id()
        {
            // Arrange：旧会话记录 Ended（CastId=100）
            var old = OpenSession("EchoReset_Old", OldPlayerId);
            Deliver(old.Client, Ended(OldPlayerId, OldCastId));
            Assert.AreEqual(OldCastId, HostField<ulong>("_endedEchoCastId"),
                "前置：旧会话 Ended 必须记录到去重窗口");

            // Act：断线重连
            _host.Disconnect();
            Assert.AreEqual(0ul, HostField<ulong>("_endedEchoCastId"),
                "断线必须清空旧会话的 Ended CastId");

            var fresh = OpenSession("EchoReset_New", NewPlayerId);

            // 新会话同 CastId 的 Started（服务器重启后 CastId 也会从 1 重新发放）
            Deliver(fresh.Client, Started(NewPlayerId, OldCastId, NewStartTick));

            // Assert：不得被旧会话的 Ended 压制
            Assert.AreEqual(OldCastId, HostField<ulong>("_lastAppliedEchoCastId"),
                "复位后新会话同 CastId 的 Started 必须被接受（旧 Ended 不得压制）");
            Assert.AreEqual(NewStartTick, HostField<uint>("_lastAppliedEchoStartTick"));
        }

        // ------------------------------------------------------------------------------------
        // ③ 同一会话内仍按 (StartTick, CastId) 单调判新——复位不得破坏去重
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_echo_session_reset_preserves_monotonic_state_within_session()
        {
            // Arrange
            var session = OpenSession("EchoReset_Mono", OldPlayerId);
            Deliver(session.Client, Started(OldPlayerId, 200ul, 600u));
            Assert.AreEqual(200ul, HostField<ulong>("_lastAppliedEchoCastId"));
            Assert.AreEqual(600u, HostField<uint>("_lastAppliedEchoStartTick"));

            // Act/Assert：更早的 StartTick → 判旧，门保持
            Deliver(session.Client, Started(OldPlayerId, 201ul, 599u));
            Assert.AreEqual(200ul, HostField<ulong>("_lastAppliedEchoCastId"),
                "同会话更早 StartTick 的状态必须被拒");
            Assert.AreEqual(600u, HostField<uint>("_lastAppliedEchoStartTick"));

            // 同 StartTick、更低 CastId → 判旧
            Deliver(session.Client, Started(OldPlayerId, 199ul, 600u));
            Assert.AreEqual(200ul, HostField<ulong>("_lastAppliedEchoCastId"),
                "同 StartTick 下更低 CastId 的状态必须被拒");
            Assert.AreEqual(600u, HostField<uint>("_lastAppliedEchoStartTick"));

            // 同 StartTick、更高 CastId → 接受（单调语义保持）
            Deliver(session.Client, Started(OldPlayerId, 201ul, 600u));
            Assert.AreEqual(201ul, HostField<ulong>("_lastAppliedEchoCastId"),
                "同 StartTick 下更高 CastId 的状态必须被接受");
            Assert.AreEqual(600u, HostField<uint>("_lastAppliedEchoStartTick"));
        }

        // ------------------------------------------------------------------------------------
        // 会话夹具（SessionIsolationTests 同款）
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

        /// <summary>服务端包 → 真实 CombatNetworkBridge → 宿主 echo 处理器（生产同一条消费路径）。</summary>
        private void Deliver(RecordingClient client, SwordQiEchoState state)
        {
            var bridge = HostField<CombatNetworkBridge>("_combatBridge");
            client.Emit(MessageId.SwordQiEchoState, state);
            bridge.Update();
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

        // ------------------------------------------------------------------------------------
        // 协议夹具
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Started 状态。ServerTimestamp 取 10 秒前 ⇒ 权威窗口已过去：宿主只对齐冷却、
        /// 不建 SkillExecutor/presenter（本测试只钉单调门，不钉表现）。
        /// </summary>
        private static SwordQiEchoState Started(long casterId, ulong castId, uint startTick)
            => new SwordQiEchoState
            {
                Phase = SwordQiEchoPhase.Started,
                CasterId = casterId,
                CastId = castId,
                EchoBindingId = 1,
                StartTick = startTick,
                EndTick = startTick + EchoWindowTicks,
                CooldownTicks = 180,
                ServerTimestamp = DateTime.UtcNow.Ticks - 10L * TimeSpan.TicksPerSecond
            };

        private static SwordQiEchoState Ended(long casterId, ulong castId)
            => new SwordQiEchoState
            {
                Phase = SwordQiEchoPhase.Ended,
                CasterId = casterId,
                CastId = castId,
                EchoBindingId = 1
            };
    }
}
