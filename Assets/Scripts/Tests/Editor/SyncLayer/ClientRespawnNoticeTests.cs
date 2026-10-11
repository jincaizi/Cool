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
    /// N5：服务端复活通知的客户端出口。
    ///
    /// `RespawnNotice` 是被复活者**本人**唯一的"你活了"信号：本地玩家不在自己的 AOI 快照里
    /// （`ClientNetworkManager` 应用快照时跳过 `LocalPlayerId`），而客户端在联网态有意短路本地复活
    /// （`Sys3CEntry.TickRespawn` 的联网早退）。这条通知转不出去，躺下就永远起不来——
    /// 服务端 8 秒后复活了他，客户端却停在死亡画面。
    /// </summary>
    public class ClientRespawnNoticeTests
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

            /// <summary>模拟网络回调线程投递一条服务端消息（与生产同一条路：adapter → dispatcher）</summary>
            public void Emit(MessageId id, IMessage message) => MessageReceived?.Invoke(id, message);
        }

        private GameObject _root = null!;
        private RecordingClient _client = null!;
        private ClientNetworkManager _manager = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("RespawnNoticeTestRoot");
            _client = new RecordingClient();
            _manager = new ClientNetworkManager(
                _client, new RemoteEntityRegistry(_root.transform), "Respawn_Tester");
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void Respawn_Notice_Is_Forwarded_With_The_Servers_Hp()
        {
            // 服务端权威：客户端不自行判活，只采信这条通知里的血量（D-N4/D-5）
            float seen = -1f;
            _manager.OnServerRespawn += hp => seen = hp;

            _client.Emit(MessageId.RespawnNotice, new RespawnNotice { Hp = 100f, Tick = 7u });

            Assert.AreEqual(100f, seen, 0.0001f, "RespawnNotice 必须被转出去——否则躺下就起不来");
        }
    }
}
