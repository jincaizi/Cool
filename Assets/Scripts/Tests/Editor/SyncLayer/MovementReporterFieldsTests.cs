using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// Plan 03 B5：位置上报携带 PvP 预算字段——模式/跳跃边沿（Start 每次 JumpSeq 只出现一次）/
    /// 位移技能/已确认 CastId/生命代次/控制版本。
    /// </summary>
    public class MovementReporterFieldsTests
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
        }

        [Test]
        public void Report_Carries_PvpMovement_Fields_And_Start_Edge_Once()
        {
            var client = new RecordingClient();
            var state = new MovementReportState
            {
                Mode = LocomotionMode.Sprint,
                JumpSeq = 3,
                Airborne = true,
                Grounded = false,
                VerticalVelocity = 5f,
                DisplacementSkillId = 20001,
                CastId = 42,
                LifeId = 7,
                ControlVersion = 2
            };
            var reporter = new MovementReporter(client, () => new Vec3(1f, 0f, 2f),
                () => 90f, () => 8f, stateProvider: () => state);

            reporter.Report();
            var first = (PositionSyncRequest)client.Sent[0].msg;
            Assert.AreEqual(PlayerJumpPhase.Start, first.JumpPhase);
            Assert.AreEqual(3u, first.JumpSeq);
            Assert.AreEqual(LocomotionMode.Sprint, first.LocomotionMode);
            Assert.AreEqual(20001, first.DisplacementSkillId);
            Assert.AreEqual(42ul, first.CastId);
            Assert.AreEqual(7u, first.LifeId);
            Assert.AreEqual(2u, first.ClientControlVersion);

            reporter.Report();
            var second = (PositionSyncRequest)client.Sent[1].msg;
            Assert.AreEqual(PlayerJumpPhase.Air, second.JumpPhase, "同一 JumpSeq 的第二次上报必须是 Air");
        }
    }
}
