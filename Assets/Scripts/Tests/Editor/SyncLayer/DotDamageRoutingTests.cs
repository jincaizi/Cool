using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// Plan 04 任务 6：`DamageKind=Dot` 的客户端分流——只更新权威血量与飘字，
    /// **不得**触发受击 FSM/格挡/击退/受击动画/命中特效/震屏；直接命中（Direct）行为不变。
    /// 装置：真 `CombatNetworkBridge` + `RemoteEntityRegistry` 视图（与 CombatNetworkBridgeTests 同款），
    /// 以及裸挂的 `Sys3CEntry`（EditMode 不跑 Start，恰好用来验证 Dot 路径完全不碰 FSM）。
    /// </summary>
    public class DotDamageRoutingTests
    {
        private sealed class RecordingClient : INetworkClient
        {
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable) { }
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
            _root = new GameObject("DotRoutingTestRoot");
            _client = new RecordingClient();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private static DamageResult DotResult(long targetId, float damage, float remainingHp,
            long sourceId = 1, bool isCritical = false)
            => new DamageResult
            {
                SourcePlayerId = sourceId,
                SkillId = 20001,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = targetId,
                        Damage = damage,
                        IsCritical = isCritical,
                        TargetRemainingHp = remainingHp,
                        DamageKind = DamageKind.Dot
                    }
                }
            };

        private static EntitySnapshot PlayerSnapshot(long playerId, float hp = 100f, float maxHp = 100f)
            => new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Health),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Hp = hp, MaxHp = maxHp
            };

        private static EntitySnapshot NpcSnapshot(long npcId, float hp = 100f, float maxHp = 100f)
            => new EntitySnapshot
            {
                PlayerId = npcId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.TemplateId | EntityFieldMask.Health),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Npc,
                TemplateId = 1,
                Hp = hp, MaxHp = maxHp
            };

        [Test]
        public void test_local_dot_routes_to_dot_sink_and_skips_direct_damage_sink()
        {
            // Arrange：本地玩家 id=7 收到 DoT 跳伤
            var registry = new RemoteEntityRegistry(_root.transform);
            int directCalls = 0, dotCalls = 0;
            long dotTarget = 0;
            float dotDamage = 0f, dotRemainingHp = -1f;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer)
                    => directCalls++,
                (targetId, damage, remainingHp) =>
                {
                    dotCalls++;
                    dotTarget = targetId;
                    dotDamage = damage;
                    dotRemainingHp = remainingHp;
                });

            // Act
            bridge.OnMessage(MessageId.DamageResult, DotResult(7, 12f, 88f));
            bridge.Update();

            // Assert
            Assert.AreEqual(0, directCalls, "DoT 不得走直接受击回调（会触发 HitFSM/格挡/击退）");
            Assert.AreEqual(1, dotCalls, "DoT 必须走独立注入点");
            Assert.AreEqual(7L, dotTarget);
            Assert.AreEqual(12f, dotDamage, 0.001f);
            Assert.AreEqual(88f, dotRemainingHp, 0.001f, "服务端权威余血必须原样下传");
        }

        [Test]
        public void test_local_dot_emits_no_hit_reaction_events()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            PlayerTakeDamageEvent? playerEvent = null;
            MonsterTakeDamageEvent? monsterEvent = null;
            BlockSucceededEvent? blockEvent = null;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                onDotDamageToLocalPlayer: (_, __, ___) => { });

            Action<PlayerTakeDamageEvent> playerHandler = e => playerEvent = e;
            Action<MonsterTakeDamageEvent> monsterHandler = e => monsterEvent = e;
            Action<BlockSucceededEvent> blockHandler = e => blockEvent = e;
            EventBus.Subscribe(playerHandler);
            EventBus.Subscribe(monsterHandler);
            EventBus.Subscribe(blockHandler);
            try
            {
                bridge.OnMessage(MessageId.DamageResult, DotResult(7, 12f, 88f));
                bridge.Update();
            }
            finally
            {
                EventBus.Unsubscribe(playerHandler);
                EventBus.Unsubscribe(monsterHandler);
                EventBus.Unsubscribe(blockHandler);
            }

            // 本地飘字由 Sys3CEntry.ApplyDotDamage 发（宿主接线），桥不得重复发；
            // 命中特效/震屏事件（MonsterTakeDamageEvent）与格挡事件更不得出现。
            Assert.IsNull(playerEvent, "桥不得为本地 DoT 发飘字事件（会与 ApplyDotDamage 重复）");
            Assert.IsNull(monsterEvent, "DoT 不得触发命中特效/震屏事件");
            Assert.IsNull(blockEvent, "DoT 不得触发格挡表现");
        }

        [Test]
        public void test_remote_player_dot_updates_vitals_and_float_text_without_hit_animation()
        {
            // Arrange：远程玩家 2 带受击动画驱动
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(PlayerSnapshot(2), serverTicks: 1_000_000L);
            var view = registry.GetView(2);
            Assert.NotNull(view, "视图未创建——ApplySnapshot Position 位应触发");
            var animator = view!.GameObject.AddComponent<RemoteEntityAnimator>();
            view.Animator = animator;   // internal setter（InternalsVisibleTo）

            PlayerTakeDamageEvent? playerEvent = null;
            MonsterTakeDamageEvent? monsterEvent = null;
            MonsterDotDamageEvent? monsterDotEvent = null;
            BlockSucceededEvent? blockEvent = null;
            Action<PlayerTakeDamageEvent> playerHandler = e => playerEvent = e;
            Action<MonsterTakeDamageEvent> monsterHandler = e => monsterEvent = e;
            Action<MonsterDotDamageEvent> monsterDotHandler = e => monsterDotEvent = e;
            Action<BlockSucceededEvent> blockHandler = e => blockEvent = e;
            EventBus.Subscribe(playerHandler);
            EventBus.Subscribe(monsterHandler);
            EventBus.Subscribe(monsterDotHandler);
            EventBus.Subscribe(blockHandler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);

                // Act
                bridge.OnMessage(MessageId.DamageResult, DotResult(2, 12.4f, 88f));
                bridge.Update();
            }
            finally
            {
                EventBus.Unsubscribe(playerHandler);
                EventBus.Unsubscribe(monsterHandler);
                EventBus.Unsubscribe(monsterDotHandler);
                EventBus.Unsubscribe(blockHandler);
            }

            // Assert
            Assert.AreEqual(0.88f, view.Targetable.HPPercent, 0.001f,
                "Targetable 血条必须用服务端余血/最大血量");
            Assert.IsTrue(playerEvent.HasValue, "远程玩家 DoT 必须出灰字飘字");
            Assert.AreEqual(view.GameObject.GetInstanceID(), playerEvent.Value.EntityId);
            Assert.AreEqual(13, playerEvent.Value.Damage); // CeilToInt(12.4)
            Assert.AreEqual((int)HitState.None, animator.LastHitState,
                "DoT 不得调用 PlayHit（受击动画/格挡表现都不允许）");
            Assert.IsNull(monsterEvent, "玩家 DoT 不得发怪物受击事件（命中特效/震屏）");
            Assert.IsNull(monsterDotEvent, "玩家 DoT 不得发怪物 DoT 事件");
            Assert.IsNull(blockEvent, "DoT 不得触发格挡表现");
        }

        [Test]
        public void test_remote_monster_dot_emits_dot_event_instead_of_take_damage_event()
        {
            // Arrange：怪物视图 2_000_001
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(NpcSnapshot(2_000_001), serverTicks: 1_000_000L);
            var view = registry.GetView(2_000_001);
            Assert.NotNull(view, "Npc 视图未创建");

            MonsterDotDamageEvent? dotEvent = null;
            MonsterTakeDamageEvent? takeDamageEvent = null;
            PlayerTakeDamageEvent? playerEvent = null;
            Action<MonsterDotDamageEvent> dotHandler = e => dotEvent = e;
            Action<MonsterTakeDamageEvent> takeHandler = e => takeDamageEvent = e;
            Action<PlayerTakeDamageEvent> playerHandler = e => playerEvent = e;
            EventBus.Subscribe(dotHandler);
            EventBus.Subscribe(takeHandler);
            EventBus.Subscribe(playerHandler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);

                // Act
                bridge.OnMessage(MessageId.DamageResult, DotResult(2_000_001, 9.4f, 90.6f));
                bridge.Update();
            }
            finally
            {
                EventBus.Unsubscribe(dotHandler);
                EventBus.Unsubscribe(takeHandler);
                EventBus.Unsubscribe(playerHandler);
            }

            // Assert
            Assert.IsTrue(dotEvent.HasValue, "怪物 DoT 必须发 MonsterDotDamageEvent（只飘字）");
            Assert.AreEqual(view.GameObject.GetInstanceID(), dotEvent.Value.EntityId);
            Assert.AreEqual(10, dotEvent.Value.Damage); // CeilToInt(9.4)
            Assert.IsNull(takeDamageEvent,
                "怪物 DoT 不得发 MonsterTakeDamageEvent（会触发命中粒子/镜头震动）");
            Assert.IsNull(playerEvent, "怪物 DoT 不得走玩家灰字");
            Assert.AreEqual(0.906f, view.Targetable.HPPercent, 0.001f,
                "Targetable 血条必须用服务端余血/最大血量");
        }

        [Test]
        public void test_direct_damage_still_uses_legacy_damage_sink()
        {
            // 回归护栏：DamageKind 缺省（Direct=0）必须保持既有路径——不落到 DoT 注入点
            var registry = new RemoteEntityRegistry(_root.transform);
            int directCalls = 0, dotCalls = 0;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer)
                    => directCalls++,
                (targetId, damage, remainingHp) => dotCalls++);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1,
                SkillId = 20001,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = 7, Damage = 12f, TargetRemainingHp = 88f
                        // DamageKind 缺省 = Direct
                    }
                }
            });
            bridge.Update();

            Assert.AreEqual(1, directCalls, "直接命中必须仍走 ApplyServerDamage 注入点");
            Assert.AreEqual(0, dotCalls, "直接命中不得落到 DoT 注入点");
        }

        [Test]
        public void test_sys3c_entry_apply_dot_damage_updates_hp_and_emits_one_float_text()
        {
            // Arrange：裸 Sys3CEntry（EditMode 不执行 Start → _fsmManager/_cc 为 null）。
            // 若 ApplyDotDamage 误调 FSM/格挡/击退，这里会直接 NullReferenceException——
            // 用例通过本身就是"没有走受击路径"的证据。
            var go = new GameObject("DotLocalEntry");
            try
            {
                var entry = go.AddComponent<Sys3CEntry>();
                int playerEventCount = 0;
                PlayerTakeDamageEvent captured = default;
                BlockSucceededEvent? blockEvent = null;
                bool playerSourceFlash = false;
                Action<PlayerTakeDamageEvent> playerHandler = e =>
                {
                    playerEventCount++;
                    captured = e;
                };
                Action<BlockSucceededEvent> blockHandler = e => blockEvent = e;
                Action<LocalPlayerHitByPlayerEvent> flashHandler = e => playerSourceFlash = true;
                EventBus.Subscribe(playerHandler);
                EventBus.Subscribe(blockHandler);
                EventBus.Subscribe(flashHandler);
                try
                {
                    // Act
                    entry.ApplyDotDamage(12.4f, 88f);
                }
                finally
                {
                    EventBus.Unsubscribe(playerHandler);
                    EventBus.Unsubscribe(blockHandler);
                    EventBus.Unsubscribe(flashHandler);
                }

                // Assert
                Assert.AreEqual(88f, entry.CurrentHp, 0.001f,
                    "本地 HP 必须采信服务端权威余血（不做本地减法）");
                Assert.AreEqual(1, playerEventCount, "DoT 必须且只能飘一次字");
                Assert.AreEqual(13, captured.Damage); // CeilToInt(12.4)
                Assert.AreEqual(go.GetInstanceID(), captured.EntityId);
                Assert.IsNull(blockEvent, "DoT 不得走格挡表现");
                Assert.IsFalse(playerSourceFlash, "DoT 不得触发玩家来源闪屏");
                Assert.IsFalse(entry.IsDead, "非致死 DoT 不得判死");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
