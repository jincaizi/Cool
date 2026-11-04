using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 管理器接线：进世界后的 Update 必须发出应用层 Heartbeat；回显同 Sequence 后必须暴露 RTT。
    /// 复用 ClientRespawnNoticeTests 的 RecordingClient 模式（与生产同一条 adapter → dispatcher 路）。
    /// </summary>
    public class ClientLatencyWiringTests
    {
        private sealed class RecordingClient : INetworkClient
        {
            public readonly List<(MessageId id, IMessage msg)> Sent = new List<(MessageId, IMessage)>();
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
                => Sent.Add((messageId, message));
            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;
            public void Dispose() { }

            public void Emit(MessageId id, IMessage message) => MessageReceived?.Invoke(id, message);
        }

        private GameObject _root = null!;
        private RecordingClient _client = null!;
        private ClientNetworkManager _manager = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("LatencyWiringTestRoot");
            _client = new RecordingClient();
            _manager = new ClientNetworkManager(
                _client, new RemoteEntityRegistry(_root.transform), "Latency_Tester");
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private void EnterWorld()
        {
            // 直接投递登录响应把状态推到 InWorld（OnLoginResponse 不校验前置状态）。
            // 不调 ConnectAndLogin 的 async void：避免测试依赖 await 的同步续体时机。
            _client.Emit(MessageId.LoginResponse, new LoginResponse { Success = true, PlayerId = 7 });
            _manager.Update(0.016f);
        }

        [Test]
        public void test_client_network_manager_sends_heartbeat_in_world()
        {
            EnterWorld();

            Assert.IsTrue(_client.Sent.Exists(s => s.id == MessageId.Heartbeat),
                "进世界后的第一次 Update 必须发出应用层 Heartbeat");
        }

        [Test]
        public void test_client_network_manager_exposes_rtt_from_echo()
        {
            EnterWorld();

            var entry = _client.Sent.Find(s => s.id == MessageId.Heartbeat);
            Assert.IsNotNull(entry.msg, "前置：进世界后必须已发出 Heartbeat");
            var heartbeat = (Heartbeat)entry.msg;

            _client.Emit(MessageId.Heartbeat, heartbeat);
            _manager.Update(0.016f);

            Assert.IsTrue(_manager.RttMs.HasValue, "回显同 Sequence 后必须暴露 RTT");
            Assert.GreaterOrEqual(_manager.RttMs.Value, 0);
        }
    }
}
