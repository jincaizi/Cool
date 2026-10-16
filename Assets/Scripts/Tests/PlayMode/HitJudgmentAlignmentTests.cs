using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 「判定时刻 = 动画判定帧」（2026-10-16 改造）的**计时验收**：把"看得见的挥砍"与
    /// "血掉下去"之间的时间差测出来，钉进断言带里（人类用户无法估算距离与时间，这两条就是那件事的自动化）。
    ///
    /// 需要**已运行的服务器**（`localhost:8000`，怪物模式）。装置照 CombatSyncTests /
    /// MonsterWorldTests：KcpNetClientAdapter + ClientNetworkManager + RemoteEntityRegistry，
    /// 但**每帧驱动**（Update(Time.deltaTime) + yield null）而不是 50ms 步进——本文件的断言是
    /// 百毫秒级的时序，步进粒度会污染测量。
    ///
    /// 两个观察点（都不改生产代码）：
    ///   · 怪物挥砍的**渲染起手**：`NpcAttackSchedule` 到点（= registry 紧接着调 `PlayAttack`）。
    ///     该表是 registry 的私有字段（`_attacks._pending`），反射只读——**这是本文件唯一的
    ///     测试钩子**，字段改名会在这里红（提示同步观察点）。
    ///   · 伤害到达：`CombatNetworkBridge`（本地受伤回调 = 生产里 `Sys3CEntry.ApplyServerDamage`
    ///     的注入点；怪物中招那一侧 bridge 会 Emit `PlayerTakeDamageEvent` = 飘字源事件）。
    /// </summary>
    public class HitJudgmentAlignmentTests
    {
        // ---------------------------------------------------------------- 装置

        /// <summary>一次会话的全部接线（照 ClientNetworkHost 的接线，只留本文件需要的部分）。</summary>
        private sealed class Rig : IDisposable
        {
            public readonly GameObject Root;
            public readonly GameObject NpcPrefab;
            public readonly KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public readonly RemoteEntityRegistry Registry;
            public readonly ClientNetworkManager Manager;
            public readonly CombatNetworkBridge Bridge;

            /// <summary>每只怪的"渲染起手"时刻（Time.realtimeSinceStartup；同一实体可能多刀）</summary>
            public readonly Dictionary<long, List<double>> SwingTimes = new Dictionary<long, List<double>>();

            /// <summary>本地玩家被应用伤害的次数（bridge 本地注入点 = 生产 Sys3CEntry.ApplyServerDamage 的调用处）</summary>
            public int LocalDamageAppliedCount;

            /// <summary>收到的每条伤害结果：来源 / 目标 / 技能 / 伤害 / 到达时刻（OnDamageResult，主线程）</summary>
            public readonly List<DamageEvent> DamageHits = new List<DamageEvent>();

            /// <summary>一条伤害结果（协议 HitEntry 的子集 + 到达时刻）</summary>
            public struct DamageEvent
            {
                public long Source;
                public long Target;
                public int SkillId;
                public float Damage;
                public double At;
            }

            private static FieldInfo _attacksField;
            private static FieldInfo _pendingField;

            public Rig(string username, bool withNpcPrefabs,
                Func<Cool.SyncCore.Vec3> positionProvider, Func<float> rotationProvider, Func<float> speedProvider)
            {
                Root = new GameObject("HitAlignRoot_" + username);

                // 怪物视图需要 NpcEntityAnimator 非空，`RemoteEntityRegistry.ScheduleAttack` 才会排程
                // （球体占位分支直接 return）——用一个运行时裸 GO 当"怪物预制体替身"：组件由 registry
                // AddComponent，路径与真模型完全一致（同 RemoteRenderBranchTests 的做法）。
                NpcPrefab = new GameObject("NpcPrefabDouble");
                var catalog = new Dictionary<int, GameObject>();
                if (withNpcPrefabs)
                    foreach (int templateId in new[] { 1, 2, 3, 4 })
                        catalog[templateId] = NpcPrefab;

                Registry = new RemoteEntityRegistry(Root.transform, null,
                    catalog.Count > 0 ? catalog : null);
                Manager = new ClientNetworkManager(Adapter, Registry, username);
                Manager.AttachMovementReporter(positionProvider, rotationProvider, speedProvider);

                // 本地受伤注入点（生产接线 = Sys3CEntry.ApplyServerDamage）与 OnDamageResult 在
                // 同一个 Bridge.Update() 里先后触发（微秒级）——时间读数统一取 OnDamageResult 那一笔。
                Bridge = new CombatNetworkBridge(Adapter, () => Manager.LocalPlayerId, Registry,
                    onDamageToLocalPlayer: (_, __, ___, ____, _____, ______) => LocalDamageAppliedCount++);
                Adapter.MessageReceived += Bridge.OnMessage;
                Bridge.OnDamageResult += result =>
                {
                    foreach (var hit in result.Hits)
                        DamageHits.Add(new DamageEvent
                        {
                            Source = result.SourcePlayerId,
                            Target = hit.TargetPlayerId,
                            SkillId = result.SkillId,
                            Damage = hit.Damage,
                            At = Time.realtimeSinceStartup
                        });
                };
                // 宿主对 CastBroadcast 的处理：排到发布者时间戳对应的渲染时刻（ClientNetworkHost 同款）
                Bridge.OnCastBroadcast += broadcast =>
                    Registry.ScheduleAttack(broadcast.CasterPlayerId, broadcast.ServerTimestamp);
            }

            /// <summary>一帧：先读排程快照 → 驱动网络/渲染 → 消费桥 → 对比"本帧到点的挥砍"。
            /// 起手时刻的记录粒度 = 本帧（≤ 1 帧误差），与伤害到达共用 Time.realtimeSinceStartup。</summary>
            public void Tick(float deltaSeconds)
            {
                var before = ReadPendingAttacks();
                Manager.Update(deltaSeconds);
                Bridge.Update();
                var after = ReadPendingAttacks();

                if (after.Count >= before.Count) return;
                double now = Time.realtimeSinceStartup;
                var rest = new List<(long EntityId, long ServerTicks)>(after);
                foreach (var item in before)
                {
                    int kept = rest.IndexOf(item);
                    if (kept >= 0) { rest.RemoveAt(kept); continue; }   // 仍未到点
                    if (!SwingTimes.TryGetValue(item.EntityId, out var list))
                        SwingTimes[item.EntityId] = list = new List<double>();
                    list.Add(now);
                }
            }

            /// <summary>最后一次 ≤ before 时刻的起手（-1 = 该实体没被观察到起手）</summary>
            public double LastSwingBefore(long entityId, double before)
            {
                if (!SwingTimes.TryGetValue(entityId, out var list)) return -1.0;
                double best = -1.0;
                foreach (var at in list)
                    if (at <= before && at > best) best = at;
                return best;
            }

            /// <summary>
            /// 第一条**能配到渲染起手**的本地受击。必须配对：怪物的这一刀可能在**我们进世界之前**
            /// 就决策并广播了（服务端的判定在"判定帧"才选目标——我们入场后照样可能吃这一刀），
            /// 那一刀我们根本收不到 CastBroadcast、也就看不到起手，拿它测残差毫无意义。
            /// </summary>
            public bool TryFindPairedLocalHit(long localId, out double swingAt, out DamageEvent hit)
            {
                foreach (var candidate in DamageHits)
                {
                    if (candidate.Target != localId) continue;
                    double swing = LastSwingBefore(candidate.Source, candidate.At);
                    if (swing < 0.0) continue;   // 入场前起的那一刀——不在观测窗里，跳过
                    swingAt = swing;
                    hit = candidate;
                    return true;
                }
                swingAt = -1.0;
                hit = default;
                return false;
            }

            /// <summary>
            /// 排程表的只读快照（反射）。`_attacks`/`_pending` 是 NpcAttackSchedule 的实现细节——
            /// 这里只读、不改；字段改名会让本文件直接红（观察点需要同步）。
            /// </summary>
            private List<(long EntityId, long ServerTicks)> ReadPendingAttacks()
            {
                _attacksField ??= typeof(RemoteEntityRegistry).GetField("_attacks",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _pendingField ??= typeof(NpcAttackSchedule).GetField("_pending",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(_attacksField,
                    "RemoteEntityRegistry._attacks 字段没了——本文件的'渲染起手'观察点需要同步更新");
                Assert.IsNotNull(_pendingField,
                    "NpcAttackSchedule._pending 字段没了——本文件的'渲染起手'观察点需要同步更新");

                var result = new List<(long, long)>();
                if (_attacksField!.GetValue(Registry) is NpcAttackSchedule schedule
                    && _pendingField!.GetValue(schedule) is List<(long EntityId, long ServerTicks)> pending)
                    result.AddRange(pending);
                return result;
            }

            public void Dispose()
            {
                Manager.Dispose();
                if (NpcPrefab != null) UnityEngine.Object.Destroy(NpcPrefab);
                if (Root != null) UnityEngine.Object.Destroy(Root);
            }
        }

        // ---------------------------------------------------------------- 助手

        private static IEnumerator WaitInWorld(ClientNetworkManager manager, float timeoutSeconds = 15f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld
                   && Time.realtimeSinceStartup < deadline)
            {
                manager.Update(Time.deltaTime);
                yield return null;
            }
            Assert.AreEqual(ClientNetworkManager.State.InWorld, manager.CurrentState,
                "登录/进世界失败（服务器没起？端口 8000 被占？协议版本不匹配？）");
        }

        /// <summary>离给定位置最近的怪物视图（视野内没有怪时返回 null）</summary>
        private static RemoteEntityView NearestNpc(RemoteEntityRegistry registry, Vector3 from,
            int? templateId = null)
        {
            RemoteEntityView best = null;
            float bestDistance = float.MaxValue;
            foreach (var id in registry.PlayerIds)
            {
                var view = registry.GetView(id);
                if (view == null || view.Kind != (byte)EntityKind.Npc) continue;
                if (templateId.HasValue && view.TemplateId != templateId.Value) continue;
                float distance = Vector3.Distance(view.Position, from);
                if (distance < bestDistance) { bestDistance = distance; best = view; }
            }
            return best;
        }

        /// <summary>把本地玩家摆到某只怪的**背后**（-forward，水平面）——怪看不见我们就不还手：
        /// B2 要的是"我们打它"，不能让它的攻击打断我们的挥砍/把我们的角色打死。
        /// 我们自己仍**面朝它**（服务端判定锥 ±60° 需要）。</summary>
        private static void PlaceBehind(Transform target, RemoteEntityView monster, float distanceBehind)
        {
            PlaceRelativeTo(target, monster, -distanceBehind);
        }

        /// <summary>沿怪的水平 forward 偏移（正 = 面前、负 = 背后），并让自己面朝它。</summary>
        private static void PlaceRelativeTo(Transform target, RemoteEntityView monster, float forwardOffset)
        {
            var forward = monster.Rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            target.position = monster.Position + forward.normalized * forwardOffset;
            var toMonster = monster.Position - target.position;
            toMonster.y = 0f;
            if (toMonster.sqrMagnitude > 0.0001f)
                target.rotation = Quaternion.LookRotation(toMonster.normalized);
        }

        /// <summary>技能判定帧延迟（tick）——从共享表读，不在测试里抄数字。
        /// 表里没有该技能时**直接红**：静默返回 0 会让"实测 vs 期望"的期望值变成无意义的负数，
        /// 对照失真却看不出来。</summary>
        private static uint HitDelayTicksOf(int skillId)
        {
            if (!CombatRules.TryGet(skillId, out var entry))
                Assert.Fail($"技能 {skillId} 不在 CombatRules 表内——期望值无从计算");
            return CombatRules.HitDelayTicks(entry);
        }

        // ---------------------------------------------------------------- B1

        /// <summary>
        /// B1：怪物挥砍的渲染起手 **先于** 伤害到达，残差 ≤ 上界。
        ///
        /// 期望的构造（数字来源）：
        ///   · 判定帧 = 决策 tick + HitDelayTicks(怪物技能)——表里 50001..50004 全是 0.35s → 6 tick（400ms）；
        ///   · 渲染起手 = 发布者时间戳对应的渲染时刻 = 决策时刻 + renderDelayTicks（客户端 2 tick ≈ 132ms）；
        ///   · 所以"血掉下去 − 挥砍起手" ≈ (6 − 2) tick ≈ 264ms（上行/下行延迟本地各几毫秒，抵消大半）。
        ///   上界 **600ms（9 tick ≈ 594ms，向上取整）**逐项拆开：4 tick 期望（264ms）+ 1 tick 排程帧量化
        ///   + 1 tick 客户端渲染时钟偏移 + 1 tick 服务端 tick/发送量化 + 2 tick 网络与宿主余量 = 9 tick。
        ///   实测 5 次落在 268~380ms（≈ 4~5.8 tick），余量对得上。
        /// 装置：观察者贴到某只怪的正前方 1.2m（≤ 所有模板的 attackRange），位置经 MovementReporter 上报——
        /// 服务端瞬移校验是双门槛（`distance > MaxSpeed×dt×1.2 && distance > 50m`），贴脸小范围大位移不被纠偏。
        /// </summary>
        [UnityTest]
        public IEnumerator Monster_Swing_Render_Leads_The_Damage_It_Causes()
        {
            var localPosition = Vector3.zero;   // 登录点（服务端实体在原点）
            var rig = new Rig("HitAlign_Monster", withNpcPrefabs: true,
                positionProvider: () => new Cool.SyncCore.Vec3(localPosition.x, localPosition.y, localPosition.z),
                rotationProvider: () => 0f, speedProvider: () => 0f);
            try
            {
                rig.Manager.ConnectAndLogin("127.0.0.1", 8000);
                yield return WaitInWorld(rig.Manager);
                localPosition = Vector3.zero;

                long localId = rig.Manager.LocalPlayerId;
                double swingAt = -1.0;
                Rig.DamageEvent hit = default;
                double deadline = Time.realtimeSinceStartup + 30.0;
                while (!rig.TryFindPairedLocalHit(localId, out swingAt, out hit)
                       && Time.realtimeSinceStartup < deadline)
                {
                    var monster = NearestNpc(rig.Registry, localPosition);
                    if (monster != null)
                        PlaceInFrontOf2D(ref localPosition, monster, 1.2f);   // 持续贴住（怪在动）
                    rig.Tick(Time.deltaTime);
                    yield return null;
                }

                int localHits = rig.DamageHits.Count(h => h.Target == localId);
                Assert.GreaterOrEqual(swingAt, 0.0,
                    $"30s 内没有测到「有起手的」本地受击（视野内实体 {rig.Registry.Count} 个，本地受击 {localHits} 次"
                    + $"（已应用 {rig.LocalDamageAppliedCount}），观察到起手的怪 {string.Join(",", rig.SwingTimes.Keys)}）——"
                    + "怪物没仇恨 / 没进攻击距离 / 起手反射钩子失效？");
                double damageAt = hit.At;

                uint hitDelayTicks = HitDelayTicksOf(hit.SkillId);
                float tickSeconds = rig.Manager.ServerTickRateMs / 1000f;
                double expected = (hitDelayTicks - 2) * tickSeconds;   // 判定帧 tick − 渲染延迟 2 tick
                const double upperBound = 0.60;
                double residual = damageAt - swingAt;

                string numbers = $"渲染起手 → 伤害到达 实测残差 {residual * 1000.0:F0}ms"
                    + $"（期望 ≈ {expected * 1000.0:F0}ms = ({hitDelayTicks} tick 判定延迟 − 2 tick 渲染) × "
                    + $"{tickSeconds * 1000.0:F0}ms；上界 {upperBound * 1000.0:F0}ms）；来源怪 {hit.Source} "
                    + "skill " + hit.SkillId + $"、伤害 {hit.Damage}；观测到的起手队列 {string.Join(",", rig.SwingTimes.Keys)}";
                // 严格 >：等于 0 意味着"起手与掉血同帧"——那正是要防的回归（判定提前到了起手那一刻）。
                // 唯一代价：单帧卡顿 ≥ 264ms 时两次观测会被压进同一帧而假红；真实残差是 264ms 量级、编辑器帧 <20ms，风险可接受。
                Assert.Greater(residual, 0.0,
                    $"{numbers}——挥砍必须在血掉下去**之前**起手（≤ 0 = 掉血不晚于看见挥砍）");
                Assert.LessOrEqual(residual, upperBound,
                    $"{numbers}——残差超上界（上界依据见用例注释）");
                Debug.Log("[HitJudgmentAlignmentTests] B1 " + numbers);
            }
            finally
            {
                rig.Dispose();
            }
        }

        /// <summary>B1 的贴脸助手：只改虚拟上报位置（观察者没有本地角色，身体不参与物理）。</summary>
        private static void PlaceInFrontOf2D(ref Vector3 localPosition, RemoteEntityView monster, float distanceInFront)
        {
            var forward = monster.Rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            localPosition = monster.Position + forward.normalized * distanceInFront;
        }

        // ---------------------------------------------------------------- B2

        /// <summary>
        /// B2：玩家挥砍的本地判定帧 **先于** 服务端伤害到达，残差 = 上行/下行 + 服务端 tick 量化。
        ///
        /// 期望的构造（数字来源）：
        ///   · 本地判定帧 = 技能激活 + CastTime + hitboxTimings[0]（BA_Combo1 资产：0.30s）；
        ///   · 服务端判定帧 = 决策 tick + HitDelayTicks(10001) = 决策 tick + 5 tick（≈333ms，同一张表）；
        ///   · 所以残差 ≈ 333 − 300 + 网络 = 30ms 基准 + 上行/服务端 tick 量化/下行。
        ///   上界 **400ms（6 tick ≈ 396ms，向上取整）**逐项拆开：0.5 tick 基准（30ms）+ 1 tick 上行
        ///   + 1 tick 服务端 tick 量化 + 1 tick 下行 + 2 tick 网络与宿主余量 = 5.5 tick ≈ 363ms。
        ///   实测 5 次落在 63~130ms（≈ 1~2 tick），余量对得上；下界**严格 > 0**（本地判定帧必须先发生）。
        /// 装置：`PlayerEntryForTest`（带 Sys3CEntry 的测试玩家）实例化到场景，摆到怪的正前方 1.2m，
        /// 走**本地输入路径** `SkillCoordinator.HandleLightAttack()`（= 技能 10001）。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_Hitbox_Frame_Precedes_Its_Server_Damage_Result()
        {
            var previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);   // 本地死亡路径确定性（联网分支另有守卫）
            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab（含 Sys3CEntry 的测试玩家）");
            var entryGo = UnityEngine.Object.Instantiate(prefab);
            var entry = entryGo.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(entry, "PlayerEntryForTest 上没有 Sys3CEntry");

            var rig = new Rig("HitAlign_Player", withNpcPrefabs: false,
                positionProvider: () => new Cool.SyncCore.Vec3(
                    entry.transform.position.x, entry.transform.position.y, entry.transform.position.z),
                rotationProvider: () => entry.transform.eulerAngles.y,
                speedProvider: () => 0f);
            try
            {
                rig.Manager.ConnectAndLogin("127.0.0.1", 8000);
                yield return WaitInWorld(rig.Manager);
                yield return null;   // 让 Start 跑完（Sys3CEntry 在 Start 里装配 SkillCoordinator）
                Assert.IsNotNull(entry.SkillCoordinator,
                    "Sys3CEntry 没有装配 SkillCoordinator（Start 提前 return？看 Console 的 LogError）");

                // 本地技能激活 → ① 上行 CastRequest（ClientNetworkHost 的接线：NotifySkillCast 只发表内技能）
                //                    ② 订阅本地判定帧（OnHitboxFrame = hitboxTimings[0] 到点）
                var hitboxFrames = new List<double>();
                entry.SkillCoordinator.OnSkillActivated += data =>
                {
                    rig.Bridge.NotifySkillCast(data.SkillId, HitShapeConverter.FromSkillData(data));
                    if (data.SkillId != 10001) return;
                    var executor = entry.SkillCoordinator.CurrentSkill;
                    if (executor != null)
                        executor.OnHitboxFrame += _ => hitboxFrames.Add(Time.realtimeSinceStartup);
                };

                long localId = rig.Manager.LocalPlayerId;
                bool SawMonsterDamage() => rig.DamageHits.Any(h => h.Source == localId && h.Target != localId);
                double nextSwingAt = double.MaxValue;          // 先等位置被服务端采纳（~0.6s）
                double deadline = Time.realtimeSinceStartup + 25.0;

                while (!SawMonsterDamage() && Time.realtimeSinceStartup < deadline)
                {
                    // 优先挑**没仇恨我们**的 Slime（开局离出生点 15.8m/[2]、18.0m/[1]，检测 8m ⇒ 未仇恨）并从它背后打：
                    // 怪看不见我们就不还手，我们的挥砍不会被打断（本地判定帧才一定触发）。
                    var monster = NearestNpc(rig.Registry, entry.transform.position, templateId: 1)
                                  ?? NearestNpc(rig.Registry, entry.transform.position);
                    if (monster != null)
                    {
                        PlaceBehind(entry.transform, monster, 1.2f);      // ≤ 10001 的 range 2（面朝它）
                        if (nextSwingAt == double.MaxValue) nextSwingAt = Time.realtimeSinceStartup + 0.6;
                    }
                    rig.Tick(Time.deltaTime);
                    if (Time.realtimeSinceStartup >= nextSwingAt)
                    {
                        entry.SkillCoordinator.HandleLightAttack();       // 本地输入路径 → OnSkillActivated → 上行
                        nextSwingAt = Time.realtimeSinceStartup + 1.2;    // 重试节奏 ≫ 结果往返（配对不串刀）
                    }
                    yield return null;
                }

                int hitsTaken = rig.DamageHits.Count(h => h.Target == localId);
                Assert.IsTrue(SawMonsterDamage(),
                    $"25s 内服务端没有把本地玩家的挥砍结算到怪物身上（收到 {rig.DamageHits.Count} 条结果，"
                    + $"其中打本地玩家 {hitsTaken} 条，本地判定帧 {hitboxFrames.Count} 次，"
                    + $"本地 HP={entry.CurrentHp:F0}）——位置/朝向没上报？怪被别的会话打死了？");
                var hit = rig.DamageHits.First(h => h.Source == localId && h.Target != localId);
                double damageAt = hit.At;

                double frameAt = -1.0;
                foreach (var at in hitboxFrames)
                    if (at <= damageAt && at > frameAt) frameAt = at;
                Assert.GreaterOrEqual(frameAt, 0.0,
                    $"收到伤害（{damageAt:F3}s）之前没有任何本地判定帧（共 {hitboxFrames.Count} 次）——观察点接错？");

                float tickSeconds = rig.Manager.ServerTickRateMs / 1000f;
                uint hitDelayTicks = HitDelayTicksOf(hit.SkillId);
                double residual = damageAt - frameAt;
                const double upperBound = 0.40;
                const double localFrameSeconds = 0.30;   // CastTime + hitboxTimings[0]（BA_Combo1 资产实测）
                double expected = hitDelayTicks * tickSeconds - localFrameSeconds;

                string numbers = $"本地判定帧 → 服务端伤害到达 实测残差 {residual * 1000.0:F0}ms"
                    + $"（期望 ≈ {expected * 1000.0:F0}ms = 服务端 {hitDelayTicks} tick 判定 "
                    + $"{hitDelayTicks * tickSeconds * 1000.0:F0}ms − 本地判定帧 {localFrameSeconds * 1000.0:F0}ms；"
                    + $"上界 {upperBound * 1000.0:F0}ms）；伤害 {hit.Damage} 落在怪 {hit.Target} "
                    + $"（skill {hit.SkillId}，本地判定帧共 {hitboxFrames.Count} 次）";
                Assert.Greater(residual, 0.0, $"{numbers}——本地判定帧必须早于服务端结果（负值 = 结果先到）");
                Assert.LessOrEqual(residual, upperBound, $"{numbers}——残差超上界（上界依据见用例注释）");
                Debug.Log("[HitJudgmentAlignmentTests] B2 " + numbers);
            }
            finally
            {
                rig.Dispose();
                UnityEngine.Object.Destroy(entryGo);
                NetworkModeService.Save(previousMode);
            }
        }
    }
}
