using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 任务 10：锁定目标 id 必须随 CastRequest.TargetPlayerId 上线（激活与蓄力释放两条路径）。
    /// </summary>
    public class SkillCastReporterTargetTests
    {
        private sealed class Client : INetworkClient
        {
            public readonly List<IMessage> Sent = new List<IMessage>();
            public bool IsConnected => true;

            public System.Threading.Tasks.Task<bool> ConnectAsync(string host, int port)
                => System.Threading.Tasks.Task.FromResult(true);

            public System.Threading.Tasks.Task DisconnectAsync(string reason)
                => System.Threading.Tasks.Task.CompletedTask;

            public void Send(MessageId id, IMessage msg, MessageFlags flags = MessageFlags.Reliable)
                => Sent.Add(msg);

            public event System.Action<MessageId, IMessage>? MessageReceived;
            public event System.Action<string>? Disconnected;
            public event System.Action<System.Exception>? Error;
            public void Dispose() { }
        }

        [Test]
        public void Activation_Forwards_The_Locked_Target_Id()
        {
            var client = new Client();
            var bridge = new CombatNetworkBridge(client, () => 1L, null!);
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(
                "Assets/PreRes/SkillsCfg/BA_Combo1.asset");
            Assert.NotNull(data, "BA_Combo1.asset 未找到——PreRes 路径核对");

            SkillCastReporter.ReportActivation(bridge, data, Vector3.zero, null!, targetId: 42L);

            var cast = (CastRequest)client.Sent.Single();
            Assert.AreEqual(42L, cast.TargetPlayerId, "锁定目标 id 必须写入 CastRequest");
        }

        [Test]
        public void Activation_Without_A_Lock_Still_Sends_Zero()
        {
            var client = new Client();
            var bridge = new CombatNetworkBridge(client, () => 1L, null!);
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(
                "Assets/PreRes/SkillsCfg/BA_Combo1.asset");
            Assert.NotNull(data, "BA_Combo1.asset 未找到——PreRes 路径核对");

            SkillCastReporter.ReportActivation(bridge, data, Vector3.zero, null!);

            var cast = (CastRequest)client.Sent.Single();
            Assert.AreEqual(0L, cast.TargetPlayerId, "无锁定时保持 0（服务端自动目标）");
        }

        [Test]
        public void ChargeRelease_Forwards_The_Locked_Target_Id()
        {
            var client = new Client();
            var bridge = new CombatNetworkBridge(client, () => 1L, null!);
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ChargedSkillData>(
                "Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset");
            Assert.NotNull(data, "ChargeFire_SkillR.asset 未找到——PreRes 路径核对");

            SkillCastReporter.ReportChargeRelease(bridge, data, chargeProgress: 0.5f,
                impactPoint: new Vector3(0f, 0f, 3f), targetId: 7L);

            var cast = (CastRequest)client.Sent.Single();
            Assert.AreEqual(7L, cast.TargetPlayerId, "蓄力释放同样要带锁定目标 id");
            Assert.AreEqual(20004, cast.SkillId);
        }
    }
}
