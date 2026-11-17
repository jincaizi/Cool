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
            Assert.AreEqual(5f, first.VerticalVelocity);
            Assert.AreEqual(LocomotionMode.Sprint, first.LocomotionMode);
            Assert.AreEqual(20001, first.DisplacementSkillId);
            Assert.AreEqual(42ul, first.CastId);
            Assert.AreEqual(7u, first.LifeId);
            Assert.AreEqual(2u, first.ClientControlVersion);

            reporter.Report();
            var second = (PositionSyncRequest)client.Sent[1].msg;
            Assert.AreEqual(PlayerJumpPhase.Air, second.JumpPhase, "同一 JumpSeq 的第二次上报必须是 Air");
        }

        [Test]
        public void Jump_End_Then_None_After_Landing()
        {
            var client = new RecordingClient();
            var state = new MovementReportState
            {
                JumpSeq = 3,
                Airborne = true,
                Grounded = false,
                VerticalVelocity = 5f
            };
            var reporter = new MovementReporter(client, () => new Vec3(0f, 0f, 0f),
                () => 0f, () => 6f, stateProvider: () => state);

            reporter.Report();
            Assert.AreEqual(PlayerJumpPhase.Start, ((PositionSyncRequest)client.Sent[0].msg).JumpPhase);

            state.Airborne = false;
            state.Grounded = true;
            state.VerticalVelocity = -8f;
            reporter.Report();
            var end = (PositionSyncRequest)client.Sent[1].msg;
            Assert.AreEqual(PlayerJumpPhase.End, end.JumpPhase);
            Assert.AreEqual(-8f, end.VerticalVelocity);

            reporter.Report();
            Assert.AreEqual(PlayerJumpPhase.None, ((PositionSyncRequest)client.Sent[2].msg).JumpPhase,
                "End 只发一次，落地后同状态不再重复边沿");
        }

        [Test]
        public void Jump_And_Landing_In_Same_Interval_Reports_None()
        {
            var client = new RecordingClient();
            var state = new MovementReportState
            {
                JumpSeq = 4,
                Airborne = false,
                Grounded = true
            };
            var reporter = new MovementReporter(client, () => new Vec3(0f, 0f, 0f),
                () => 0f, () => 0f, stateProvider: () => state);

            reporter.Report();
            Assert.AreEqual(PlayerJumpPhase.None, ((PositionSyncRequest)client.Sent[0].msg).JumpPhase,
                "起跳与落地落在同一上报间隔：不制造幽灵起跳");

            reporter.Report();
            Assert.AreEqual(PlayerJumpPhase.None, ((PositionSyncRequest)client.Sent[1].msg).JumpPhase);
        }

        [Test]
        public void ResetForRespawn_Rebuilds_Jump_Edge()
        {
            var client = new RecordingClient();
            var state = new MovementReportState
            {
                JumpSeq = 1,
                Airborne = true,
                Grounded = false
            };
            var reporter = new MovementReporter(client, () => new Vec3(0f, 0f, 0f),
                () => 0f, () => 6f, stateProvider: () => state);

            reporter.Report();
            Assert.AreEqual(PlayerJumpPhase.Start, ((PositionSyncRequest)client.Sent[0].msg).JumpPhase);

            state.Airborne = false;
            state.Grounded = true;
            reporter.Report();
            Assert.AreEqual(PlayerJumpPhase.End, ((PositionSyncRequest)client.Sent[1].msg).JumpPhase);

            reporter.ResetForRespawn(7);

            // 复活后控制器是重建的新角色，JumpSeq 从 1 重新计数；若不清理
            // `_lastReportedJumpSeq/_wasAirborne`，这次起跳会被压成 Air（漏报 Start）。
            state.JumpSeq = 1;
            state.Airborne = true;
            state.Grounded = false;
            reporter.Report();
            Assert.AreEqual(PlayerJumpPhase.Start, ((PositionSyncRequest)client.Sent[2].msg).JumpPhase,
                "复活后第一次跳跃必须重新报 Start");
        }
    }
}
