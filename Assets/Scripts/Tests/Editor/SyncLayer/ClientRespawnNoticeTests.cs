using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
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
        public void Respawn_Notice_Is_Forwarded_With_The_Servers_Hp_And_Position()
        {
            RespawnNotice seen = null;
            _manager.OnServerRespawn += notice => seen = notice;

            var respawn = new RespawnNotice
            {
                Hp = 100f, Tick = 7u, HasSpawnPosition = true, SpawnX = -15f, SpawnY = 0.3f, SpawnZ = -30f
            };
            _client.Emit(MessageId.RespawnNotice, respawn);

            Assert.AreSame(respawn, seen);
        }

        [Test]
        public void Respawn_Rejects_Old_Corrections_And_Reports_With_The_New_Tick()
        {
            var position = new Vec3(60f, 0f, 0f);
            _manager.AttachMovementReporter(() => position, () => 0f, () => 0f);
            _client.Emit(MessageId.LoginResponse, new LoginResponse { Success = true, ServerTickRateMs = 66 });
            _manager.Reporter.Report();
            var before = (PositionSyncRequest)_client.Sent[_client.Sent.Count - 1].msg;
            _client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = before.Sequence, AuthoritativeX = 50f
            });
            int corrections = 0;
            _manager.OnCorrectionNeeded += (_, _) => corrections++;

            _manager.ResetMovementForRespawn(7);
            position = new Vec3(-15f, 0.3f, -30f);
            _manager.Update(0f);

            Assert.Zero(corrections);
            var after = (PositionSyncRequest)_client.Sent[_client.Sent.Count - 1].msg;
            Assert.AreEqual(before.Sequence + 1, after.Sequence);
            Assert.AreEqual(7u, after.RespawnTick);
            Assert.AreEqual(-15f, after.X);

            _client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = after.Sequence, AuthoritativeX = 60f
            });
            _manager.Update(0f);
            Assert.Zero(corrections);

            _client.Emit(MessageId.PositionSyncResponse, new PositionSyncResponse
            {
                AcknowledgedSequence = after.Sequence, RespawnTick = 7, AuthoritativeX = -16f
            });
            _manager.Update(0f);
            Assert.AreEqual(1, corrections, "本次复活后的正常纠偏仍应生效");
        }
    }
}
