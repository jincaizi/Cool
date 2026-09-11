using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Events;
using KcpNet;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// P4：战斗网络桥——出站（表内技能发 CastRequest）/入站（DamageResult → 本地扣血回调、
    /// 远程实体飘字事件走现有 PlayerTakeDamageEvent 链）。
    /// </summary>
    public class CombatNetworkBridgeTests
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

        private GameObject _root = null!;
        private RecordingClient _client = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BridgeTestRoot");
            _client = new RecordingClient();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private static HitShape SkillQShape() => new HitShape
        {
            ShapeType = 0, Range = 3.5f, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f
        };

        [Test]
        public void NotifySkillCast_Sends_CastRequest_For_Synced_Skill()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);

            bridge.NotifySkillCast(20001, SkillQShape());

            Assert.AreEqual(1, _client.Sent.Count);
            Assert.AreEqual(MessageId.CastRequest, _client.Sent[0].id);
            var request = (CastRequest)_client.Sent[0].msg;
            Assert.AreEqual(20001, request.SkillId);
            Assert.AreEqual(0L, request.TargetPlayerId); // D-P4-2：客户端恒 0
            Assert.AreEqual(3.5f, request.Shape.Range, 0.001f);
        }

        [Test]
        public void NotifySkillCast_Ignores_Non_Synced_Skill()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);

            bridge.NotifySkillCast(30001, SkillQShape()); // Ultimate 不进表（D-P4-1）

            Assert.AreEqual(0, _client.Sent.Count);
        }

        [Test]
        public void HitShapeConverter_Maps_Asset_Values()
        {
            var combo = UnityEditor.AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Skills.Data.ComboSkillData>(
                "Assets/PreRes/SkillsCfg/BA_Combo1.asset");
            Assert.NotNull(combo, "BA_Combo1.asset 未找到——PreRes 路径核对");
            var shape = HitShapeConverter.FromSkillData(combo);
            Assert.AreEqual(0, shape.ShapeType);       // Single
            Assert.AreEqual(2f, shape.Range, 0.001f);
            Assert.AreEqual(120f, shape.Angle, 0.001f);
            Assert.AreEqual(0.8f, shape.InnerRadius, 0.001f);

            var spin = UnityEditor.AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Skills.Data.SpinSkillData>(
                "Assets/PreRes/SkillsCfg/Spin_SkillR.asset");
            Assert.NotNull(spin, "Spin_SkillR.asset 未找到——PreRes 路径核对");
            var spinShape = HitShapeConverter.FromSkillData(spin);
            Assert.AreEqual(1, spinShape.ShapeType);   // AOE_Circle
            Assert.AreEqual(2.5f, spinShape.AreaRadius, 0.001f);
        }

        [Test]
        public void OnDamageResult_LocalTarget_Invokes_Damage_Sink()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            long sinkTarget = 0; float sinkDamage = 0; bool sinkCrit = false;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir) =>
                {
                    sinkTarget = targetId;
                    sinkDamage = damage;
                    sinkCrit = isCritical;
                });

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1, SkillId = 20001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 120f, IsCritical = false, TargetRemainingHp = 20f } }
            });
            bridge.Update();

            Assert.AreEqual(7L, sinkTarget);
            Assert.AreEqual(120f, sinkDamage, 0.001f);
            Assert.False(sinkCrit);
        }

        [Test]
        public void OnDamageResult_RemoteTarget_Emits_PlayerTakeDamageEvent()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            // 建立远程视图（ApplySnapshot 带 Position 位触发 CreateView）
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = 1f, Y = 0f, Z = 2f
            }, serverTicks: 1_000_000L);
            var view = registry.GetView(2);
            Assert.NotNull(view, "视图未创建——ApplySnapshot Position 位应触发");

            PlayerTakeDamageEvent? captured = null;
            Action<PlayerTakeDamageEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 20001,
                    Hits = { new HitEntry { TargetPlayerId = 2, Damage = 57.4f, IsCritical = true, TargetRemainingHp = 42.6f } }
                });
                bridge.Update();

                Assert.NotNull(captured, "远程受击未发 PlayerTakeDamageEvent");
                Assert.AreEqual(view.GameObject.GetInstanceID(), captured.Value.EntityId);
                Assert.AreEqual(58, captured.Value.Damage); // CeilToInt(57.4)
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }

        [Test]
        public void OnCastBroadcast_Raises_Event()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);
            CastBroadcast captured = null!; // CastBroadcast 是 class——项目未开 nullable 上下文，不能用 struct 式 `?`/`.Value`
            bridge.OnCastBroadcast += b => captured = b;

            bridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
            {
                SkillId = 20001, CasterPlayerId = 2, CasterX = 1f, CasterZ = 2f
            });
            bridge.Update();

            Assert.NotNull(captured, "CastBroadcast 未触发事件");
            Assert.AreEqual(20001, captured.SkillId);
            Assert.AreEqual(2L, captured.CasterPlayerId);
        }

        [Test]
        public void OnMessage_Ignores_Non_Combat_Messages()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);

            bridge.OnMessage(MessageId.WorldSnapshot, new WorldSnapshot());
            bridge.Update(); // 不抛、无消费

            Assert.AreEqual(0, _client.Sent.Count);
        }

        [Test]
        public void Monster_Source_Damage_To_Local_Player_Invokes_Local_Damage_Callback()
        {
            // 怪物攻击本地玩家（spec"打玩家触发 HitFSM 同步"链路）：SourcePlayerId=怪物 id
            // → onDamageToLocalPlayer 回调（宿主接 ApplyServerDamage——HitFSM 入口）。
            // 桥按 TargetPlayerId 匹配本地玩家——来源种类无关（既有逻辑，锁定回归）。
            var registry = new RemoteEntityRegistry(_root.transform);
            float sinkDamage = 0;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir) => sinkDamage = damage);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2_000_001, SkillId = 50001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, IsCritical = false, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(10f, sinkDamage, 0.001f);
        }

        [Test]
        public void Monster_Target_Damage_Emits_Float_Text_Over_Npc_View()
        {
            // 玩家打怪：DamageResult.TargetPlayerId = 怪物 id → 怪物视图（Npc 快照创建）飘字
            // ——既有 PlayerTakeDamageEvent 链对 Npc 视图零改动生效（锁定回归）。
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_001,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Npc,
                TemplateId = 1
            }, serverTicks: 1_000_000L);
            var view = registry.GetView(2_000_001);
            Assert.NotNull(view, "Npc 视图未创建");

            PlayerTakeDamageEvent? captured = null;
            Action<PlayerTakeDamageEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 10001,
                    Hits = { new HitEntry { TargetPlayerId = 2_000_001, Damage = 50f, IsCritical = false, TargetRemainingHp = 50f } }
                });
                bridge.Update();

                Assert.NotNull(captured, "怪物受击未发 PlayerTakeDamageEvent");
                Assert.AreEqual(view.GameObject.GetInstanceID(), captured.Value.EntityId);
                Assert.AreEqual(50, captured.Value.Damage);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }
    }
}
