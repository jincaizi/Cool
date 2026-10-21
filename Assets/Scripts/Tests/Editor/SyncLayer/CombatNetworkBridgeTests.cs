using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Events;
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
        public void test_heavy_attack_reports_sword_qi_cast_with_asset_shape()
        {
            // 10002 的剑气依赖客户端在激活时上报 CastRequest；形状取 BA_Heavy 资产，
            // 服务端沿施法者朝向做 12m 线形判定（不传落点）。
            var heavy = UnityEditor.AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Skills.Data.InstantSkillData>(
                "Assets/PreRes/SkillsCfg/BA_Heavy.asset");
            Assert.NotNull(heavy, "BA_Heavy.asset 未找到——PreRes 路径核对");
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1L, registry);

            SkillCastReporter.ReportActivation(bridge, heavy, Vector3.zero, _root.transform);

            Assert.AreEqual(1, _client.Sent.Count, "10002 激活应发一条 CastRequest");
            Assert.AreEqual(MessageId.CastRequest, _client.Sent[0].id);
            var cast = (CastRequest)_client.Sent[0].msg;
            Assert.AreEqual(10002, cast.SkillId);
            Assert.AreEqual(0, cast.Shape.ShapeType);
            Assert.AreEqual(2.5f, cast.Shape.Range, 0.001f);
            Assert.AreEqual(120f, cast.Shape.Angle, 0.001f);
            Assert.AreEqual(0f, cast.ImpactX, 0.001f, "剑气沿施法者朝向，不传落点");
            Assert.AreEqual(0f, cast.ImpactZ, 0.001f);
        }

        [Test]
        public void NotifySkillCast_Ignores_Non_Synced_Skill()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);

            bridge.NotifySkillCast(999999, SkillQShape()); // 999999 永不进表（D-P4-1 翻转后 30001 已同步）

            Assert.AreEqual(0, _client.Sent.Count);
        }

        [Test]
        public void test_notify_skill_cast_carries_impact_and_charge()
        {
            // 任务 4：落点/蓄力进度必须随 CastRequest 上线（服务端 CircleAtPoint 判定依赖它们）
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
            bridge.NotifySkillCast(20004,
                new HitShape { ShapeType = 1, Range = 2f, AreaRadius = 2f, Angle = 120f },
                impactPoint: new Vector3(0f, 0f, 10f), chargeProgress: 0.5f);

            var cast = _client.Sent.Where(x => x.id == MessageId.CastRequest)
                                   .Select(x => (CastRequest)x.msg).Single();
            Assert.AreEqual(10f, cast.ImpactZ, 0.001f, "落点 Z 应写入");
            Assert.AreEqual(0.5f, cast.ChargeProgress, 0.001f, "蓄力进度应写入");
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
            long sinkTarget = 0; float sinkDamage = 0; bool sinkCrit = false; float sinkRemainingHp = -1f;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    sinkTarget = targetId;
                    sinkDamage = damage;
                    sinkCrit = isCritical;
                    sinkRemainingHp = remainingHp;
                });

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1, SkillId = 20001,
                SourceKind = (int)EntityKind.Player,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 120f, IsCritical = false, TargetRemainingHp = 20f } }
            });
            bridge.Update();

            Assert.AreEqual(7L, sinkTarget);
            Assert.AreEqual(120f, sinkDamage, 0.001f);
            Assert.False(sinkCrit);
            // T12 甲-2：服务端权威余血必须原样带到本地注入点（宿主用它对齐本地 HP）
            Assert.AreEqual(20f, sinkRemainingHp, 0.001f,
                "TargetRemainingHp 未随回调下传——本地 HP 会退回自算，与服务端永远对不上");
        }

        [Test]
        public void OnDamageResult_LocalTarget_Passes_Blocked_Flag()
        {
            // 格挡减伤服务端化的表现侧：IsBlocked 必须随本地受击回调到宿主——
            // 宿主据此走格挡受击（DefendHit）而不是普通受击。Damage 已是减免后的值（3 = 10 × 0.3）
            var registry = new RemoteEntityRegistry(_root.transform);
            bool sinkBlocked = false; float sinkDamage = 0f;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    sinkDamage = damage;
                    sinkBlocked = isBlocked;
                });

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2_000_001, SkillId = 50001,
                SourceKind = (int)EntityKind.Npc,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 3f, IsBlocked = true, TargetRemainingHp = 97f } }
            });
            bridge.Update();

            Assert.IsTrue(sinkBlocked, "格挡标志未随本地受击回调下传——宿主无法区分格挡/普通受击表现");
            Assert.AreEqual(3f, sinkDamage, 0.001f);
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
            float sinkDamage = 0; float sinkRemainingHp = -1f;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    sinkDamage = damage;
                    sinkRemainingHp = remainingHp;
                });

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2_000_001, SkillId = 50001,
                SourceKind = (int)EntityKind.Npc,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, IsCritical = false, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(10f, sinkDamage, 0.001f);
            Assert.AreEqual(90f, sinkRemainingHp, 0.001f, "怪物打玩家这条链同样要带权威余血");
        }

        [Test]
        public void Monster_Target_Damage_Emits_MonsterTakeDamageEvent_With_Crit_Flag()
        {
            // 玩家打怪：DamageResult.TargetPlayerId = 怪物 id → 怪物视图（Npc 快照创建）
            // **必须走 MonsterTakeDamageEvent**（DisplayEventBridge 用它选红字/暴击橙），
            // 不能复用玩家灰字 PlayerTakeDamageEvent。
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

            MonsterTakeDamageEvent? monsterCaptured = null;
            PlayerTakeDamageEvent? playerCaptured = null;
            Action<MonsterTakeDamageEvent> monsterHandler = e => monsterCaptured = e;
            Action<PlayerTakeDamageEvent> playerHandler = e => playerCaptured = e;
            EventBus.Subscribe(monsterHandler);
            EventBus.Subscribe(playerHandler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 10002,
                    Hits = { new HitEntry { TargetPlayerId = 2_000_001, Damage = 57.4f, IsCritical = true, TargetRemainingHp = 42.6f } }
                });
                bridge.Update();

                Assert.NotNull(monsterCaptured, "怪物受击必须发 MonsterTakeDamageEvent（红字/暴击橙）");
                Assert.IsNull(playerCaptured, "怪物受击不得发 PlayerTakeDamageEvent（灰字）");
                Assert.AreEqual(view.GameObject.GetInstanceID(), monsterCaptured.Value.EntityId);
                Assert.AreEqual(58, monsterCaptured.Value.Damage); // CeilToInt(57.4)
                Assert.IsTrue(monsterCaptured.Value.IsCritical, "暴击标志必须保留，DisplayEventBridge 据此选暴击色");
                Assert.AreEqual(10002, monsterCaptured.Value.SkillId);
                Assert.IsTrue(monsterCaptured.Value.IsLocalSource, "本地玩家命中才允许触发本地命中反馈/镜头震动");
            }
            finally
            {
                EventBus.Unsubscribe(monsterHandler);
                EventBus.Unsubscribe(playerHandler);
            }
        }

        // ------------------------------------------------------------------------------------
        // 怪物挥砍的端到端链（spec §8 第 4 条）：CastBroadcast → ScheduleAttack → Drain →
        // PlayAttack → Animator。宿主接线是 ClientNetworkHost 里那一行
        // `_combatBridge.OnCastBroadcast += b => _remoteEntities.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp);`
        // —— 下面两个用例把这条链整条穿起来（此前无任何自动化用例，失效完全静默）。
        // ------------------------------------------------------------------------------------

        /// <summary>目录里的**真预制体** + 真控制器：球体降级没有可驱动的东西，测不出这条链。
        /// 与 DemoDay 场景的 `_npcPrefabs` 接线同款（templateId 3 → DogKnight、4 → SpiderBoss）。</summary>
        private static readonly (string PrefabPath, int TemplateId)[] RealMonsterPrefabs =
        {
            ("Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab", 3),
            ("Assets/Monstor/fantasySpider/SpiderMonster.prefab", 4),
        };

        [Test]
        public void A_Scheduled_Swing_Plays_The_Variant_Derived_From_The_Timestamp()
        {
            // 两个模型 × 两个变体槽（相邻桶 → 两段攻击各覆盖一次；哪段是 Attack01 由常量决定，
            // 所以预期名一律从 NpcAttackSchedule.VariantOf 推，不写死）
            foreach (var (prefabPath, templateId) in RealMonsterPrefabs)
                for (int slot = 0; slot < 2; slot++)
                    Assert_Scheduled_Swing_Plays(prefabPath, templateId, slot);
        }

        private static void Assert_Scheduled_Swing_Plays(string prefabPath, int templateId, int slot)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            var root = new GameObject("CastChainRoot");
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { templateId, prefab } });
            try
            {
                long npcId = 2_000_000L + templateId;
                // ServerClock.NowTicks 同量级（DateTime.UtcNow.Ticks——快照与广播同基准）
                long sampleTicks = 638_000_000_000_000_000L;
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = npcId,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                        | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                    X = 1f, Y = 0f, Z = 2f,
                    Kind = (byte)EntityKind.Npc, TemplateId = templateId,
                    ActionState = (byte)NpcAnimationState.Idle, Tick = 1
                }, serverTicks: sampleTicks);

                var view = registry.GetView(npcId);
                Assert.IsNotNull(view, "Npc 视图未创建");
                Assert.IsTrue(view!.GameObject.name.StartsWith(prefab!.name),
                    $"目录命中就该用真预制体，实际是 {view.GameObject.name}");
                Assert.IsNotNull(view.NpcAnimator, "真预制体上应补上怪物驱动（球体降级没有可驱动的东西）");
                view.NpcAnimator!.ResolveAnimator();   // EditMode：AddComponent 不触发生命周期
                var animator = view.GameObject.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator, prefabPath + " 应带 Animator");

                long castTicks = sampleTicks + slot * NpcAttackSchedule.VariantBucketTicks;
                int variant = NpcAttackSchedule.VariantOf(castTicks);
                string expected = variant == 0 ? "Attack01" : "Attack02";

                registry.ScheduleAttack(npcId, castTicks);

                // 时间闸门：渲染时钟还没越过时间戳 → 不许播（排程不是"立即播"）
                registry.UpdateRender(nowServerTicks: castTicks - 1L, renderDelayTicks: 0L);
                Settle(animator!);
                Assert.IsFalse(animator!.GetCurrentAnimatorStateInfo(0).IsName("Attack01"),
                    prefabPath + "：时间戳未到就播了");
                Assert.IsFalse(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack02"),
                    prefabPath + "：时间戳未到就播了");

                // 越过时间戳 → 播时间戳对应的那一段
                registry.UpdateRender(nowServerTicks: castTicks + 1L, renderDelayTicks: 0L);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(expected),
                    $"{prefabPath}：变体 {variant} 应播 {expected}，实际 " +
                    $"{animator.GetCurrentAnimatorStateInfo(0).shortNameHash}");
            }
            finally
            {
                registry.Clear();   // 连同实例化出的视图（含 Animator 驱动）一起销毁
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CastBroadcast_From_An_Npc_Schedules_A_Swing_And_From_A_Player_Does_Not()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Monstor/fantasySpider/SpiderMonster.prefab");
            Assert.IsNotNull(prefab, "真预制体未找到");
            var root = new GameObject("CastRoutingRoot");
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { 4, prefab } });
            try
            {
                long npcId = 2_000_006L, playerId = 7L;
                long baseTicks = 638_000_000_000_000_000L;
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = npcId,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                        | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                    X = 1f, Y = 0f, Z = 2f,
                    Kind = (byte)EntityKind.Npc, TemplateId = 4,
                    ActionState = (byte)NpcAnimationState.Idle, Tick = 1
                }, serverTicks: baseTicks);
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = playerId,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                    X = 3f, Y = 0f, Z = 3f, Kind = (byte)EntityKind.Player, Tick = 1
                }, serverTicks: baseTicks);

                var view = registry.GetView(npcId);
                Assert.IsNotNull(view?.NpcAnimator, "怪物视图应带驱动");
                view!.NpcAnimator!.ResolveAnimator();
                var animator = view.GameObject.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator);
                Assert.IsNull(registry.GetView(playerId)!.NpcAnimator, "玩家视图不该有怪物驱动");

                var bridge = new CombatNetworkBridge(_client, () => playerId, registry);
                bridge.OnCastBroadcast += b => registry.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp);

                // ① caster 是 npc → 排程 → 越过时间戳 → 真动画播起来
                long npcCastTicks = baseTicks + 2L * NpcAttackSchedule.VariantBucketTicks;
                int npcVariant = NpcAttackSchedule.VariantOf(npcCastTicks);
                string played = npcVariant == 0 ? "Attack01" : "Attack02";
                bridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
                {
                    SkillId = 50004, CasterPlayerId = npcId, ServerTimestamp = npcCastTicks
                });
                bridge.Update();
                registry.UpdateRender(nowServerTicks: npcCastTicks + 1L, renderDelayTicks: 0L);
                Settle(animator!);
                Assert.IsTrue(animator!.GetCurrentAnimatorStateInfo(0).IsName(played),
                    $"npc 的 CastBroadcast 没排到真动画上（期望 {played}）");

                // ② caster 是玩家 → 不排程：同一只怪不得因它再播一刀。
                //    玩家那条广播取**相邻桶**（推另一段），所以"错排到怪物身上"会让状态换成另一段——可判。
                long playerCastTicks = baseTicks + 3L * NpcAttackSchedule.VariantBucketTicks;
                Assert.AreNotEqual(npcVariant, NpcAttackSchedule.VariantOf(playerCastTicks),
                    "本用例的前提：两条广播推出不同变体（否则错排看不出来）");
                bridge.OnMessage(MessageId.CastBroadcast, new CastBroadcast
                {
                    SkillId = 20001, CasterPlayerId = playerId, ServerTimestamp = playerCastTicks
                });
                bridge.Update();
                registry.UpdateRender(nowServerTicks: playerCastTicks + 1L, renderDelayTicks: 0L);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(played),
                    "玩家的 CastBroadcast 被排到了怪物身上——同一只怪因它又起了一刀");
            }
            finally
            {
                registry.Clear();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>推帧直到过渡落定（与 NpcAnimatorAssetTests.Settle 同款）：AnyState 过渡在参数写入后的
        /// 第一次 Update 才开始计时，落定前 <c>GetCurrentAnimatorStateInfo</c> 报的仍是源状态。上限 20 帧防呆。</summary>
        private static void Settle(Animator animator)
        {
            int guard = 0;
            do
            {
                animator.Update(0.05f);
            } while (++guard < 20 && animator.IsInTransition(0));
        }

        [Test]
        public void OnDamageResult_ProjectileStopped_LocalCaster_Raises_Stop_Event()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
            int raised = 0;
            Vector3 stop = Vector3.zero;
            bridge.OnLineProjectileStopped += p => { raised++; stop = p; };

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 10002,
                ProjectileStopped = true,
                StopX = 0f,
                StopZ = 6f
            });
            bridge.Update();

            Assert.AreEqual(1, raised);
            Assert.AreEqual(0f, stop.x, 0.001f);
            Assert.AreEqual(6f, stop.z, 0.001f);
        }

        [Test]
        public void OnDamageResult_ProjectileStopped_RemoteCaster_Does_Not_Raise()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
            int raised = 0;
            bridge.OnLineProjectileStopped += _ => raised++;

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2L,
                SkillId = 10002,
                ProjectileStopped = true,
                StopX = 0f,
                StopZ = 6f
            });
            bridge.Update();

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void OnDamageResult_ProjectileStopped_NonLineAllSkill_Does_Not_Raise()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
            int raised = 0;
            bridge.OnLineProjectileStopped += _ => raised++;

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 10001,
                ProjectileStopped = true,
                StopX = 0f,
                StopZ = 6f
            });
            bridge.Update();

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void OnDamageResult_BlockedHit_Without_StopFlag_Does_Not_Raise()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
            int raised = 0;
            bridge.OnLineProjectileStopped += _ => raised++;

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 10002,
                ProjectileStopped = false,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = 3L, Damage = 27f, IsBlocked = true,
                        HitX = 0f, HitZ = 5f
                    }
                }
            });
            bridge.Update();

            Assert.AreEqual(0, raised, "客户端不得回退到扫描 IsBlocked 来停剑气");
        }

        [Test]
        public void OnDamageResult_MultipleBlockedHits_Raises_StopEvent_Once()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
            int raised = 0;
            bridge.OnLineProjectileStopped += _ => raised++;

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 10002,
                ProjectileStopped = true,
                StopX = 0f,
                StopZ = 1.5f,
                Hits =
                {
                    new HitEntry { TargetPlayerId = 3L, Damage = 27f, IsBlocked = true },
                    new HitEntry { TargetPlayerId = 3L, Damage = 27f, IsBlocked = true }
                }
            });
            bridge.Update();

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void test_combat_bridge_npc_source_is_not_player_source()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            bool? sinkSourceIsPlayer = null;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                    sinkSourceIsPlayer = sourceIsPlayer);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2_000_001,
                SourceKind = (int)EntityKind.Npc,
                SkillId = 50001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(false, sinkSourceIsPlayer, "怪物来源不能标记为玩家攻击，否则会触发红色闪屏");
        }

        [Test]
        public void test_combat_bridge_player_and_bot_sources_are_player_source()
        {
            AssertPlayerSource((int)EntityKind.Player);
            AssertPlayerSource((int)EntityKind.PlayerBot);
        }

        [Test]
        public void test_combat_bridge_unknown_source_kind_is_not_player_source()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            bool? sinkSourceIsPlayer = null;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                    sinkSourceIsPlayer = sourceIsPlayer);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 3,
                SourceKind = 99,
                SkillId = 20001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(false, sinkSourceIsPlayer,
                "未知 SourceKind 必须 fail-closed（不闪屏），不能按玩家来源处理");
        }

        private void AssertPlayerSource(int sourceKind)
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            bool? sinkSourceIsPlayer = null;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                    sinkSourceIsPlayer = sourceIsPlayer);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 3,
                SourceKind = sourceKind,
                SkillId = 20001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(true, sinkSourceIsPlayer, "玩家/bot 来源必须标记为玩家攻击");
        }
    }
}
