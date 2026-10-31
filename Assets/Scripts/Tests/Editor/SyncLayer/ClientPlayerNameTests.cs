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
    /// 名字名册的线程封送：`PlayerEnterRoom` 在**网络回调线程**到达（契约见
    /// `INetworkClient.MessageReceived`），而名册是主线程写给表现系统的数据源——
    /// 只在网络线程入队、由主线程 `Update` 落库，否则名牌读到的字典随时可能被并发改写。
    /// </summary>
    public class ClientPlayerNameTests
    {
        private sealed class RecordingClient : INetworkClient
        {
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;
            public void Dispose() { }

            /// <summary>模拟网络回调线程投递一条服务端消息（与生产同一条路：adapter → dispatcher）</summary>
            public void Emit(MessageId id, IMessage message) => MessageReceived?.Invoke(id, message);
        }

        private GameObject _root = null!;
        private RecordingClient _client = null!;
        private ClientNetworkManager _manager = null!;
        private bool _disposed;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PlayerNameTestRoot");
            _client = new RecordingClient();
            _manager = new ClientNetworkManager(
                _client, new RemoteEntityRegistry(_root.transform), "Name_Tester");
        }

        [TearDown]
        public void TearDown()
        {
            if (!_disposed) _manager.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private void EnterWorld()
        {
            _client.Emit(MessageId.LoginResponse, new LoginResponse
            {
                Success = true,
                PlayerId = 1L,
                ZoneId = "Global",
                ServerTickRateMs = 66
            });
            _manager.Update(0.016f);
            Assert.AreEqual(ClientNetworkManager.State.InWorld, _manager.CurrentState,
                "前置：登录成功才消费名册");
        }

        [Test]
        public void Enter_Room_Names_Are_Queued_On_The_Network_Thread_And_Drained_On_Update()
        {
            EnterWorld();
            _client.Emit(MessageId.PlayerEnterRoom,
                new PlayerEnterRoom { PlayerId = 3L, PlayerName = "Alice" });

            Assert.IsFalse(_manager.Names.TryGetName(3L, out _),
                "网络线程只入队——名册必须在主线程写");

            _manager.Update(0.016f);

            Assert.IsTrue(_manager.Names.TryGetName(3L, out var name));
            Assert.AreEqual("Alice", name);
        }

        [Test]
        public void Leave_Room_Removes_The_Name_And_Dispose_Clears_The_Registry()
        {
            EnterWorld();
            _client.Emit(MessageId.PlayerEnterRoom,
                new PlayerEnterRoom { PlayerId = 4L, PlayerName = "Bob" });
            _manager.Update(0.016f);
            Assert.IsTrue(_manager.Names.TryGetName(4L, out _));

            _client.Emit(MessageId.PlayerLeaveRoom, new PlayerLeaveRoom { PlayerId = 4L });
            _manager.Update(0.016f);
            Assert.IsFalse(_manager.Names.TryGetName(4L, out _), "离场要同步删掉名字");

            _manager.Names.Set(5L, "Carol");
            _manager.Dispose();
            _disposed = true;
            Assert.IsFalse(_manager.Names.TryGetName(5L, out _), "Dispose 要清空名册");
        }
    }
}
