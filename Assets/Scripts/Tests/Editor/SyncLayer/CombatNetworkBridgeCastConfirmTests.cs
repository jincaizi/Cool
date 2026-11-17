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
    /// Plan 03 B5：CastConfirm 的已确认 CastId 映射——按技能 + 生命代次查询，
    /// 位移上报只在同代次复用（旧代次的 CastId 不得复活）。
    /// </summary>
    public class CombatNetworkBridgeCastConfirmTests
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
        }

        [Test]
        public void Accepted_CastConfirm_Is_Queryable_By_Skill_And_Life()
        {
            var root = new GameObject("BridgeCastConfirmTestRoot");
            var bridge = new CombatNetworkBridge(
                new RecordingClient(), () => 1L, new RemoteEntityRegistry(root.transform));
            bridge.OnMessage(MessageId.CastConfirm, new CastConfirm
            {
                Accepted = true, SkillId = 20001, ClientCastSeq = 5,
                CastId = 42, CasterLifeId = 7
            });
            bridge.Update();

            Assert.IsTrue(bridge.TryGetAcceptedCastId(20001, 7, out var castId));
            Assert.AreEqual(42ul, castId);
            Assert.IsFalse(bridge.TryGetAcceptedCastId(20001, 8, out _), "生命代次不匹配不得复用");
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
