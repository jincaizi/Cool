using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 属性元素三客户端验收（A 装元素剑施法、B 受击本人、C 旁观）。
    ///
    /// 雷场景（Plan 05 任务 2）：
    /// ① A 的 CastBroadcast 必须携带雷元素（ElementId=3）且 CastConfirm 接受；
    /// ② A/B/C 收到的同一 HitEntry 的 CastId/EventSeq/CombatVersion/TargetRemainingHp 逐值相等
    ///    （挥击 + 剑气两个独立伤害实例，共两条命中）；
    /// ③ B 本人的 311 与 C 应用后的远程视图都必须看到麻痹状态
    ///    （Statuses 含 EffectId=301 且 SourceId=A、Control.ActionLock=true、ControlVersion>0）；
    /// ④ 施加与清空两条 311 的 ServerTimestamp 差值必须落在 0.7–1.05s（0.8s ± 0.25s），
    ///    施加后约 0.65s 仍必须锁定（防“立即清空”也通过）；B/C 收到“无 301 且 ActionLock=false”
    ///    的投影，且 EffectId=301 的移除只发生一次（按 CombatVersion 递增）。
    ///
    /// 火场景（Plan 06 任务 1）：一条 10002 的 3 跳灼烧（EffectId=201）在三端逐值一致、
    /// 无格挡/无控制推进，末跳后 201 恰好移除一次；随后连续火剑命中直到 B 死亡
    /// （致死来源不限），A/B/C 的死讯投影 CombatVersion/LifeId 与致死 HitEntry 严格相等，
    /// 死后不再收到 B 的 DoT 命中。三端“末跳致死时刻”本身不在本用例范围（见方法注释）。
    ///
    /// 麻痹取消场景（Plan 06 任务 2）：A 先起手雷、B 后起手 10002；A 的命中落在 B 的前摇里，
    /// 三端收到同一 CastCancelBroadcast（同一 CastId/EffectiveTick/生命代次），B 的施法整组
    /// 作废（无命中、无发射），A 的雷仍结算 2 条命中，旁观者 C 按 CastId 停掉攻击表现。
    ///
    /// 三客户端同出生点，等待 C 的 AOI 先看到 B 再施法。
    /// 运行前需先启动：dotnet Server/bin/Debug/net8.0/Server3C.dll 8000
    /// </summary>
    public class ThreeClientElementalAcceptanceTests
    {
        private const string Host = "127.0.0.1";
        private const int Port = 8000;
        private const int SkillId = 10002;
        private const int ThunderElementId = 3;
        private const int ThunderEffectId = 301;
        private const int FireElementId = 2;
        private const int FireEffectId = 201;

        /// <summary>火焰 3s / 1s 间隔，末跳排在 EndTick ⇒ 恰好 3 跳。</summary>
        private const int ExpectedFireDotTicks = 3;

        /// <summary>击杀阶段施法轮数上界（每刀 180 直伤 + 8/秒灼烧；B 720 HP）。</summary>
        private const int MaxKillRounds = 12;

        /// <summary>10002 的挥击与剑气是两个独立伤害实例，同一目标各结算一次。</summary>
        private const int ExpectedHitsPerTarget = 2;

        /// <summary>10002 前摇 0.35s × 15Hz 向上取整 = 6 tick（攻速倍率 1 时）。</summary>
        private const uint LightningWindupTicks = 6;

        /// <summary>
        /// 共享运行闸门：120s 总 deadline（内层等待全部受它约束）、取消标志与当前阶段。
        /// 外层协程超时先 <see cref="Cancel"/>，等场景任务（RunThunderAsync / RunFireAsync）
        /// 退出后才 Dispose，避免后台任务继续 Pump 已释放的 manager。
        /// </summary>
        private sealed class RunGate
        {
            private readonly long _deadlineTicks =
                DateTime.UtcNow.Ticks + TimeSpan.TicksPerSecond * 120;
            private volatile bool _cancelled;
            public volatile string Stage = "start";

            public void SetStage(string name) => Stage = name;
            public void Cancel() => _cancelled = true;
            public bool Cancelled => _cancelled;
            public bool DeadlineExpired => DateTime.UtcNow.Ticks >= _deadlineTicks;
        }

        private sealed class Ctx
        {
            public readonly KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
            // adapter 回调在网络线程，断言在主线程：用并发队列避免 List 枚举竞态。
            public readonly ConcurrentQueue<CastBroadcast> Broadcasts = new ConcurrentQueue<CastBroadcast>();
            public readonly ConcurrentQueue<CastConfirm> Confirms = new ConcurrentQueue<CastConfirm>();
            public readonly ConcurrentQueue<EquipWeaponConfirm> EquipConfirms = new ConcurrentQueue<EquipWeaponConfirm>();
            public readonly ConcurrentQueue<DamageResult> Damages = new ConcurrentQueue<DamageResult>();
            public readonly ConcurrentQueue<CombatStateUpdate> CombatUpdates = new ConcurrentQueue<CombatStateUpdate>();
            public readonly ConcurrentQueue<CastCancelBroadcast> Cancels = new ConcurrentQueue<CastCancelBroadcast>();
            public readonly ConcurrentQueue<ProjectileLaunchBroadcast> Launches = new ConcurrentQueue<ProjectileLaunchBroadcast>();
        }

        [UnityTest]
        public IEnumerator Thunder_Status_Is_Consistent_Across_Caster_Victim_And_Observer_And_Expires()
            => RunScenario("ThreeClientElementalRoot", "Elem", RunThunderAsync);

        /// <summary>
        /// 分层覆盖（Plan 06 范围裁定）：
        /// ① 服务端“末跳致死”由 `DotDamageTests.Burn_Tick_That_Kills_Player_Clears_Statuses_And_Stops`
        ///    单独证明（MaxHp=8、一跳致死、状态清空、死后不再跳）；
        /// ② 本三端用例只证明每跳数值一致与死亡投影一致，**致死来源不限**（直伤或 DoT 均可）；
        /// ③ 三端“末跳致死时刻”需要服务器测试钩子/可注入低血量，生产数值下不可确定复现，
        ///    登记为 Plan 07 gap，本用例不宣称覆盖。
        /// </summary>
        [UnityTest]
        public IEnumerator FireDot_Ticks_Are_Consistent_And_Death_Projection_Is_Consistent()
            => RunScenario("ThreeClientFireRoot", "Fire", RunFireAsync);

        /// <summary>
        /// 麻痹取消前摇（Plan 06 任务 2）：A 先起手雷 10002（命中 tick = AcceptedTick + 6），
        /// B 在 1–3 tick 后起手 10002（首段 = 自己 AcceptedTick + 6）——A 的判定必然落在
        /// B 的前摇里。命中触发麻痹 → <c>ApplyPlayerControl(ActionLock)</c> → <c>CancelCaster(B)</c>，
        /// B 的施法在发射前整组作废。断言见方法内注释。
        /// </summary>
        [UnityTest]
        public IEnumerator Thunder_Cancels_Windup_Cast_On_All_Clients()
            => RunScenario("ThreeClientCancelRoot", "Cancel", RunThunderCancelAsync);

        /// <summary>
        /// 共用三客户端外壳：建场景根/三个 manager → 跑场景 → 120s 外层超时（先取消再等待）
        /// → 断言结果 → 统一 Dispose。场景内部的所有等待都受同一 <see cref="RunGate"/> 约束。
        /// </summary>
        private static IEnumerator RunScenario(string rootName, string namePrefix,
            Func<Ctx, Ctx, Ctx, RunGate, Task<(bool ok, string reason)>> scenario)
        {
            var root = new GameObject(rootName);
            var a = NewClient(root, namePrefix + "_A");
            var b = NewClient(root, namePrefix + "_B");
            var c = NewClient(root, namePrefix + "_C");
            var gate = new RunGate();

            var task = scenario(a, b, c, gate);
            float elapsed = 0f;
            while (!task.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!task.IsCompleted)
            {
                // 先取消：内层等待每 ≤50ms 检查一次取消标志，退出后场景不再触碰 Unity 对象。
                gate.Cancel();
                float grace = 0f;
                while (!task.IsCompleted && grace < 1f)
                {
                    grace += Time.deltaTime;
                    yield return null;
                }
            }

            try
            {
                Assert.IsTrue(task.IsCompleted, "three-client elemental test timed out at stage " + gate.Stage);
                var (ok, reason) = task.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                a.Manager.Dispose();
                b.Manager.Dispose();
                c.Manager.Dispose();
                UnityEngine.Object.Destroy(root);
            }
        }

        private static Ctx NewClient(GameObject root, string name)
        {
            var ctx = new Ctx();
            ctx.Registry = new RemoteEntityRegistry(root.transform);
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, ctx.Registry, name);
            ctx.Adapter.MessageReceived += (id, msg) =>
            {
                switch (id)
                {
                    case MessageId.CastBroadcast:
                        ctx.Broadcasts.Enqueue((CastBroadcast)msg);
                        break;
                    case MessageId.CastConfirm:
                        ctx.Confirms.Enqueue((CastConfirm)msg);
                        break;
                    case MessageId.EquipWeaponConfirm:
                        ctx.EquipConfirms.Enqueue((EquipWeaponConfirm)msg);
                        break;
                    case MessageId.DamageResult:
                        ctx.Damages.Enqueue((DamageResult)msg);
                        break;
                    case MessageId.CombatStateUpdate:
                        ctx.CombatUpdates.Enqueue((CombatStateUpdate)msg);
                        break;
                    case MessageId.CastCancelBroadcast:
                        ctx.Cancels.Enqueue((CastCancelBroadcast)msg);
                        break;
                    case MessageId.ProjectileLaunchBroadcast:
                        ctx.Launches.Enqueue((ProjectileLaunchBroadcast)msg);
                        break;
                }
            };
            return ctx;
        }

        private static async Task<(bool ok, string reason)> RunThunderAsync(Ctx a, Ctx b, Ctx c, RunGate gate)
        {
            var all = new[] { a, b, c };

            var login = await LoginThreeAsync(a, b, c, gate);
            if (!login.ok) return (false, login.reason);
            long aId = login.aId;
            long bId = login.bId;
            var baseline = login.inventory!;

            gate.SetStage("baseline-weapon");
            var thunder = FindWeapon(baseline, ThunderElementId);
            if (thunder == null)
                return (false, "baseline: A has no thunder (ElementId=3) weapon");

            var equip = await EquipElementAsync(all, a, gate, ThunderElementId, 1);
            if (!equip.ok) return (false, equip.reason);
            var equipConfirm = equip.confirm!;

            var cast = await CastAsync(all, a, gate, bId, 1,
                equipConfirm.EquipmentRevision, baseline.LifeId,
                "cast-request", "cast-confirm");
            if (!cast.ok) return (false, cast.reason);
            var castConfirm = cast.confirm!;
            if (!castConfirm.Accepted)
                return (false, "cast-confirm: rejected: " + castConfirm.RejectReason);
            ulong castId = castConfirm.CastId;
            if (castId == 0)
                return (false, "cast-confirm: accepted but CastId=0");

            gate.SetStage("cast-broadcast");
            CastBroadcast? broadcast = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    broadcast = a.Broadcasts.ToArray().FirstOrDefault(x => x.CastId == castId);
                    return broadcast != null;
                }, 10f))
                return (false, "cast-broadcast: A received no CastBroadcast for CastId " + castId);
            if (broadcast!.ElementId != ThunderElementId)
                return (false, "cast-broadcast: ElementId=" + broadcast.ElementId + " != " + ThunderElementId);

            // 受击者 B 与旁观者 C 收到同一条广播：同一 CastId 且同样携带雷元素。
            gate.SetStage("cast-broadcast-victim-observer");
            CastBroadcast? victimBroadcast = null;
            CastBroadcast? observerBroadcast = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    victimBroadcast = b.Broadcasts.ToArray().FirstOrDefault(x => x.CastId == castId);
                    observerBroadcast = c.Broadcasts.ToArray().FirstOrDefault(x => x.CastId == castId);
                    return victimBroadcast != null && observerBroadcast != null;
                }, 10f))
                return (false, "cast-broadcast: B/C did not both receive CastBroadcast for CastId "
                    + castId + " (B=" + (victimBroadcast != null) + " C="
                    + (observerBroadcast != null) + ")" + Summarize(a, b, c, bId));
            if (victimBroadcast!.CastId != castId || observerBroadcast!.CastId != castId)
                return (false, "cast-broadcast: B/C CastId differs from A's " + castId
                    + " (B=" + victimBroadcast.CastId + " C=" + observerBroadcast.CastId + ")");
            if (victimBroadcast.ElementId != ThunderElementId
                || observerBroadcast.ElementId != ThunderElementId)
                return (false, "cast-broadcast: B/C ElementId=(" + victimBroadcast.ElementId + ","
                    + observerBroadcast.ElementId + ") != " + ThunderElementId);

            gate.SetStage("damage-result");
            var aHits = new List<HitEntry>();
            var bHits = new List<HitEntry>();
            var cHits = new List<HitEntry>();
            if (!await WaitUntil(all, gate, () =>
                {
                    aHits = HitsFor(a, castId, bId);
                    bHits = HitsFor(b, castId, bId);
                    cHits = HitsFor(c, castId, bId);
                    return aHits.Count >= ExpectedHitsPerTarget
                        && bHits.Count >= ExpectedHitsPerTarget
                        && cHits.Count >= ExpectedHitsPerTarget;
                }, 15f))
                return (false, "damage-result: caster/victim/observer did not all receive "
                    + ExpectedHitsPerTarget + " hits (swing + sword qi) for B"
                    + Summarize(a, b, c, bId));

            // 同一 HitEntry 逐值一致（按 EventSeq 对齐；挥击 + 剑气两条命中都核对）。
            if (aHits.Count != ExpectedHitsPerTarget || bHits.Count != ExpectedHitsPerTarget
                || cHits.Count != ExpectedHitsPerTarget)
                return (false, "damage-result: hit counts A=" + aHits.Count + " B=" + bHits.Count
                    + " C=" + cHits.Count + " != " + ExpectedHitsPerTarget + " (swing + sword qi)"
                    + Summarize(a, b, c, bId));
            for (int i = 0; i < aHits.Count; i++)
            {
                if (aHits[i].CastId != bHits[i].CastId || aHits[i].CastId != cHits[i].CastId)
                    return (false, "damage-result: CastId differs across clients at hit " + i
                        + " (A=" + aHits[i].CastId + " B=" + bHits[i].CastId
                        + " C=" + cHits[i].CastId + ")");
                if (aHits[i].EventSeq != bHits[i].EventSeq || aHits[i].EventSeq != cHits[i].EventSeq)
                    return (false, "damage-result: EventSeq differs across clients at hit " + i
                        + " (A=" + aHits[i].EventSeq + " B=" + bHits[i].EventSeq
                        + " C=" + cHits[i].EventSeq + ")");
                if (aHits[i].CombatVersion != bHits[i].CombatVersion
                    || aHits[i].CombatVersion != cHits[i].CombatVersion)
                    return (false, "damage-result: CombatVersion differs across clients at hit " + i
                        + " (A=" + aHits[i].CombatVersion + " B=" + bHits[i].CombatVersion
                        + " C=" + cHits[i].CombatVersion + ")");
                // 三端解码的是同一条消息，余血必须位级相等（不放宽容差）。
                if (aHits[i].TargetRemainingHp != bHits[i].TargetRemainingHp
                    || aHits[i].TargetRemainingHp != cHits[i].TargetRemainingHp)
                    return (false, "damage-result: TargetRemainingHp differs across clients at hit " + i
                        + " (A=" + aHits[i].TargetRemainingHp + " B=" + bHits[i].TargetRemainingHp
                        + " C=" + cHits[i].TargetRemainingHp + ")");
            }

            // B 本人 311：麻痹状态（来源必须是 A）+ 动作锁 + 控制版本推进。
            gate.SetStage("status-apply-b");
            CombatStateUpdate? bApply = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    bApply = UpdatesFor(b, bId).FirstOrDefault(u => HasThunderFrom(u.Projection, aId));
                    return bApply != null;
                }, 15f))
                return (false, "status-apply: B received no CombatStateUpdate with EffectId="
                    + ThunderEffectId + " from A(source=" + aId + ")" + Summarize(a, b, c, bId));
            if (bApply!.Projection.Control == null || !bApply.Projection.Control.ActionLock)
                return (false, "status-apply: B Control.ActionLock is not true" + Summarize(a, b, c, bId));
            if (bApply.Projection.Control!.ControlVersion == 0)
                return (false, "status-apply: B Control.ControlVersion == 0");

            // C 旁观收到的同一条 311，喂给 registry 后远程视图必须一致。
            gate.SetStage("status-apply-c");
            CombatStateUpdate? cApply = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    cApply = UpdatesFor(c, bId).FirstOrDefault(u => HasThunderFrom(u.Projection, aId));
                    return cApply != null;
                }, 15f))
                return (false, "status-apply: C received no CombatStateUpdate with EffectId="
                    + ThunderEffectId + " from A(source=" + aId + ")" + Summarize(a, b, c, bId));
            if (cApply!.Projection.CombatVersion != bApply.Projection.CombatVersion)
                return (false, "status-apply: C projection CombatVersion=" + cApply.Projection.CombatVersion
                    + " != B " + bApply.Projection.CombatVersion);
            if (cApply.ServerTimestamp != bApply.ServerTimestamp)
                return (false, "status-apply: C ServerTimestamp=" + cApply.ServerTimestamp
                    + " != B " + bApply.ServerTimestamp);

            gate.SetStage("status-view-c");
            c.Registry.ApplyCombatStateUpdate(cApply);
            var applyView = c.Registry.GetView(bId);
            if (applyView == null)
                return (false, "status-view: C has no view for B");
            if (!HasThunderFrom(applyView.Statuses, aId))
                return (false, "status-view: C view Statuses missing EffectId=" + ThunderEffectId
                    + " from A(source=" + aId + ")");
            if (applyView.Control == null || !applyView.Control.ActionLock)
                return (false, "status-view: C view Control.ActionLock is not true");

            // 施加后约 0.65s 仍必须锁定（服务端 0.8s 到期；防“立即清空”也通过）。
            // 同机验收：ServerTimestamp = DateTime.UtcNow.Ticks，可直接与本地墙钟比较。
            gate.SetStage("status-still-locked");
            long lockCheckTicks = bApply.ServerTimestamp + (long)(TimeSpan.TicksPerSecond * 0.65);
            if (!await WaitUntilServerTicks(all, gate, lockCheckTicks))
                return (false, "status-still-locked: gate closed before 0.65s after apply");
            var bLatest = UpdatesFor(b, bId).LastOrDefault();
            var cLatest = UpdatesFor(c, bId).LastOrDefault();
            if (bLatest == null || !HasThunder(bLatest.Projection)
                || bLatest.Projection.Control == null || !bLatest.Projection.Control.ActionLock)
                return (false, "status-still-locked: B projection no longer locked 0.65s after apply"
                    + Summarize(a, b, c, bId));
            if (cLatest == null || !HasThunder(cLatest.Projection)
                || cLatest.Projection.Control == null || !cLatest.Projection.Control.ActionLock)
                return (false, "status-still-locked: C projection no longer locked 0.65s after apply"
                    + Summarize(a, b, c, bId));

            // 清空判定：无 301 且 ActionLock=false（不要求整个 Statuses 为空——B 可能带其他状态）。
            gate.SetStage("status-clear-b");
            CombatStateUpdate? bClear = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    bClear = UpdatesFor(b, bId).FirstOrDefault(u =>
                        u.Projection.CombatVersion > bApply.Projection.CombatVersion
                        && !HasThunder(u.Projection)
                        && u.Projection.Control != null && !u.Projection.Control.ActionLock);
                    return bClear != null;
                }, 10f))
                return (false, "status-clear: B received no projection without 301 and unlocked after apply version "
                    + bApply.Projection.CombatVersion + Summarize(a, b, c, bId));
            if (bClear!.Projection.Control == null || bClear.Projection.Control.ActionLock)
                return (false, "status-clear: B Control.ActionLock is not false");

            gate.SetStage("status-clear-c");
            CombatStateUpdate? cClear = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    cClear = UpdatesFor(c, bId).FirstOrDefault(u =>
                        u.Projection.CombatVersion > cApply.Projection.CombatVersion
                        && !HasThunder(u.Projection)
                        && u.Projection.Control != null && !u.Projection.Control.ActionLock);
                    return cClear != null;
                }, 10f))
                return (false, "status-clear: C received no projection without 301 and unlocked after apply version "
                    + cApply.Projection.CombatVersion + Summarize(a, b, c, bId));
            if (cClear!.Projection.Control == null || cClear.Projection.Control.ActionLock)
                return (false, "status-clear: C Control.ActionLock is not false");

            // 0.8s（12 tick）持续时间：施加→清空两条 311 的 ServerTimestamp 间隔 0.7–1.05s。
            gate.SetStage("status-expiry-timing");
            double gapSeconds = (bClear.ServerTimestamp - bApply.ServerTimestamp)
                / (double)TimeSpan.TicksPerSecond;
            Debug.Log("[ElementalAcceptance] apply->clear ServerTimestamp gap="
                + gapSeconds.ToString("F3", CultureInfo.InvariantCulture) + "s");
            if (gapSeconds < 0.7 || gapSeconds > 1.05)
                return (false, "expiry-timing: apply→clear ServerTimestamp gap "
                    + gapSeconds.ToString("F3", CultureInfo.InvariantCulture)
                    + "s outside [0.700, 1.050]s (apply=" + bApply.ServerTimestamp
                    + " clear=" + bClear.ServerTimestamp + ")");
            if (cApply!.ServerTimestamp != bApply.ServerTimestamp
                || cClear.ServerTimestamp != bClear.ServerTimestamp)
                return (false, "expiry-timing: C apply/clear timestamps differ from B's projection");

            c.Registry.ApplyCombatStateUpdate(cClear);
            var clearedView = c.Registry.GetView(bId);
            if (clearedView == null)
                return (false, "status-clear: C view for B disappeared");
            if (HasThunder(clearedView.Statuses))
                return (false, "status-clear: C view Statuses still has EffectId=" + ThunderEffectId);
            if (clearedView.Control == null || clearedView.Control.ActionLock)
                return (false, "status-clear: C view Control.ActionLock is not false");

            // 只发生一次 301 的移除，且移除版本必须大于施加版本（CombatVersion 递增）。
            gate.SetStage("status-removal-count");
            if (bClear.Projection.CombatVersion <= bApply.Projection.CombatVersion)
                return (false, "removal-count: B clear version not after apply version");
            if (cClear.Projection.CombatVersion <= cApply.Projection.CombatVersion)
                return (false, "removal-count: C clear version not after apply version");

            int bApplies = CountThunderApplications(b, bId);
            int bRemovals = CountThunderRemovals(b, bId);
            int cApplies = CountThunderApplications(c, bId);
            int cRemovals = CountThunderRemovals(c, bId);
            if (bApplies != 1)
                return (false, "removal-count: B applied EffectId=301 " + bApplies + " times, expected 1"
                    + Summarize(a, b, c, bId));
            if (cApplies != 1)
                return (false, "removal-count: C applied EffectId=301 " + cApplies + " times, expected 1"
                    + Summarize(a, b, c, bId));
            if (bRemovals != 1)
                return (false, "removal-count: B removed EffectId=301 " + bRemovals + " times, expected 1"
                    + Summarize(a, b, c, bId));
            if (cRemovals != 1)
                return (false, "removal-count: C removed EffectId=301 " + cRemovals + " times, expected 1"
                    + Summarize(a, b, c, bId));

            return (true, "ok");
        }

        /// <summary>
        /// 火场景：A 装火剑（ElementId=2）→ 10002 命中 B → 一条施法的 3 跳灼烧三端一致 →
        /// 末跳后 201 恰好移除一次 → 连续火剑命中致死，三端死讯一致且死后不再有 B 的 DoT 命中。
        /// </summary>
        private static async Task<(bool ok, string reason)> RunFireAsync(Ctx a, Ctx b, Ctx c, RunGate gate)
        {
            var all = new[] { a, b, c };

            var login = await LoginThreeAsync(a, b, c, gate);
            if (!login.ok) return (false, login.reason);
            long aId = login.aId;
            long bId = login.bId;
            var baseline = login.inventory!;

            // A 也必须是 B 的观察者：DoT 广播与死亡投影都要到达施法端。
            gate.SetStage("fire/aoi-a-sees-b");
            if (!await WaitUntil(all, gate, () => a.Registry.GetView(bId) != null, 15f))
                return (false, "fire/aoi: A never saw B (bId=" + bId + ")");

            gate.SetStage("fire/baseline");
            var fire = FindWeapon(baseline, FireElementId);
            if (fire == null)
                return (false, "baseline: A has no fire (ElementId=2) weapon");

            var equip = await EquipElementAsync(all, a, gate, FireElementId, 1);
            if (!equip.ok) return (false, equip.reason);
            uint equipmentRevision = equip.confirm!.EquipmentRevision;

            gate.SetStage("fire/first-cast");
            var first = await CastAsync(all, a, gate, bId, 1, equipmentRevision, baseline.LifeId,
                "fire/cast-request-1", "fire/cast-confirm-1");
            if (!first.ok) return (false, first.reason);
            if (!first.confirm!.Accepted)
                return (false, "fire/cast-confirm: rejected: " + first.confirm.RejectReason);
            if (first.confirm.CastId == 0)
                return (false, "fire/cast-confirm: accepted but CastId=0");
            ulong firstCastId = first.confirm.CastId;

            // 施加投影：B 本人与旁观者 C 都收到 201（来源 A）后才开始计跳。
            gate.SetStage("fire/status-apply");
            CombatStateUpdate? bApply = null;
            CombatStateUpdate? cApply = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    bApply = UpdatesFor(b, bId).FirstOrDefault(u =>
                        HasEffectFrom(u.Projection, FireEffectId, aId));
                    cApply = UpdatesFor(c, bId).FirstOrDefault(u =>
                        HasEffectFrom(u.Projection, FireEffectId, aId));
                    return bApply != null && cApply != null;
                }, 15f))
                return (false, "fire/status-apply: B/C did not both receive EffectId="
                    + FireEffectId + " from A(source=" + aId + ")" + SummarizeFire(a, b, c, bId));

            // 3 跳 DoT：三端都收到后再逐条核对（绝不因“最终一致”放宽）。
            gate.SetStage("fire/dot-ticks");
            var aDots = new List<(DamageResult Result, HitEntry Hit)>();
            var bDots = new List<(DamageResult Result, HitEntry Hit)>();
            var cDots = new List<(DamageResult Result, HitEntry Hit)>();
            if (!await WaitUntil(all, gate, () =>
                {
                    aDots = DotHitsFor(a, bId);
                    bDots = DotHitsFor(b, bId);
                    cDots = DotHitsFor(c, bId);
                    return aDots.Count >= ExpectedFireDotTicks
                        && bDots.Count >= ExpectedFireDotTicks
                        && cDots.Count >= ExpectedFireDotTicks;
                }, 15f))
                return (false, "fire/dot-ticks: caster/victim/observer did not all receive "
                    + ExpectedFireDotTicks + " DoT ticks for B" + SummarizeFire(a, b, c, bId));
            if (aDots.Count != ExpectedFireDotTicks || bDots.Count != ExpectedFireDotTicks
                || cDots.Count != ExpectedFireDotTicks)
                return (false, "fire/dot-ticks: tick counts A=" + aDots.Count + " B=" + bDots.Count
                    + " C=" + cDots.Count + " != " + ExpectedFireDotTicks
                    + SummarizeFire(a, b, c, bId));

            for (int i = 0; i < aDots.Count; i++)
            {
                string? mismatch = CompareDotHitsAcrossClients("fire/dot-ticks", i,
                    aDots[i], bDots[i], cDots[i], aId);
                if (mismatch != null) return (false, mismatch);
                // 这一批跳伤必须属于首刀的施法实例（灼烧保留首次来源 CastId）。
                if (aDots[i].Hit.CastId != firstCastId)
                    return (false, "fire/dot-ticks: tick " + i + " CastId=" + aDots[i].Hit.CastId
                        + " != first cast " + firstCastId);
            }

            // 相邻跳 ServerTimestamp 间隔 0.85–1.15s（1s ± 0.15s）。
            for (int i = 1; i < aDots.Count; i++)
            {
                double gap = (aDots[i].Result.ServerTimestamp - aDots[i - 1].Result.ServerTimestamp)
                    / (double)TimeSpan.TicksPerSecond;
                if (gap < 0.85 || gap > 1.15)
                    return (false, "fire/dot-ticks: tick interval "
                        + gap.ToString("F3", CultureInfo.InvariantCulture)
                        + "s outside [0.850, 1.150]s at tick " + i);
            }

            // EndTick 对齐：末跳必须落在施加投影 201 的 EndTick 上。施加 311 与施加同 tick
            // （tick 末发送），末跳在 EndTick 的 pre-tick；EndTick − 施加 tick = 服务的
            // ceil(DurationSeconds × 15Hz)，因此两端墙钟差必须等于该时长（±1 个服务端 tick）。
            var burnProjection = bApply.Projection.Statuses.First(s => s.EffectId == FireEffectId);
            uint burnTicks = Cool.SyncCore.StatusRuntime.SecondsToTicks(
                Cool.SyncCore.ElementalCatalogDefaults.FindStatus(FireEffectId)!.DurationSeconds);
            double tickSeconds = a.Manager.ServerTickRateMs / 1000.0;
            double endTickOffset = (aDots[ExpectedFireDotTicks - 1].Result.ServerTimestamp
                    - bApply.ServerTimestamp) / (double)TimeSpan.TicksPerSecond
                - burnTicks * tickSeconds;
            if (burnTicks == 0 || endTickOffset < -tickSeconds || endTickOffset > tickSeconds)
                return (false, "fire/dot-ticks: terminal tick is "
                    + endTickOffset.ToString("F3", CultureInfo.InvariantCulture)
                    + "s off the apply projection EndTick=" + burnProjection.EndTick
                    + " (±" + tickSeconds.ToString("F3", CultureInfo.InvariantCulture) + "s)");

            // 末跳（EndTick）后移除：B/C 都收到“无 201”的更高版本投影。
            gate.SetStage("fire/status-remove");
            CombatStateUpdate? bRemove = null;
            CombatStateUpdate? cRemove = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    bRemove = UpdatesFor(b, bId).FirstOrDefault(u =>
                        u.Projection.CombatVersion > bApply!.Projection.CombatVersion
                        && !HasEffect(u.Projection, FireEffectId)
                        && u.Projection.Control != null && !u.Projection.Control.ActionLock);
                    cRemove = UpdatesFor(c, bId).FirstOrDefault(u =>
                        u.Projection.CombatVersion > cApply!.Projection.CombatVersion
                        && !HasEffect(u.Projection, FireEffectId)
                        && u.Projection.Control != null && !u.Projection.Control.ActionLock);
                    return bRemove != null && cRemove != null;
                }, 10f))
                return (false, "fire/status-remove: B/C received no projection without EffectId="
                    + FireEffectId + " after apply version " + bApply!.Projection.CombatVersion
                    + SummarizeFire(a, b, c, bId));

            // EndTick 与末跳同一 tick：末跳在 EndTick 的 pre-tick，移除投影在 EndTick 末发送，
            // 两者墙钟差必须落在 [0, 1 tick]——即末跳确实发生在 EndTick 这个 tick。
            double removalDelay = (bRemove!.ServerTimestamp
                    - aDots[ExpectedFireDotTicks - 1].Result.ServerTimestamp)
                / (double)TimeSpan.TicksPerSecond;
            if (removalDelay < 0 || removalDelay > tickSeconds)
                return (false, "fire/status-remove: removal arrives "
                    + removalDelay.ToString("F3", CultureInfo.InvariantCulture)
                    + "s after the terminal tick (expected within one tick "
                    + tickSeconds.ToString("F3", CultureInfo.InvariantCulture) + "s)");

            // 201 只施加一次、只移除一次。
            int bApplies = CountEffectApplications(b, bId, FireEffectId);
            int cApplies = CountEffectApplications(c, bId, FireEffectId);
            int bRemovals = CountEffectRemovals(b, bId, FireEffectId);
            int cRemovals = CountEffectRemovals(c, bId, FireEffectId);
            if (bApplies != 1 || cApplies != 1 || bRemovals != 1 || cRemovals != 1)
                return (false, "fire/status-remove: apply/remove counts B=(" + bApplies + ","
                    + bRemovals + ") C=(" + cApplies + "," + cRemovals + "), expected (1,1)"
                    + SummarizeFire(a, b, c, bId));

            // 跳伤不触发受击硬直：施加→移除之间 B 的 ControlVersion 不变且 ActionLock=false。
            uint applyControlVersion = bApply.Projection.Control!.ControlVersion;
            foreach (var update in UpdatesFor(b, bId))
            {
                if (update.Projection.CombatVersion < bApply.Projection.CombatVersion
                    || update.Projection.CombatVersion > bRemove!.Projection.CombatVersion) continue;
                var control = update.Projection.Control;
                if (control == null || control.ActionLock)
                    return (false, "fire/control: B ActionLock during DoT at version "
                        + update.Projection.CombatVersion + SummarizeFire(a, b, c, bId));
                if (control.ControlVersion != applyControlVersion)
                    return (false, "fire/control: B ControlVersion advanced from " + applyControlVersion
                        + " to " + control.ControlVersion + " during DoT at version "
                        + update.Projection.CombatVersion);
            }

            // 末跳后不得补跳：移除投影到达后再确认仍是 3 条（首刀跳数快照，供后续延续性比较）。
            var aPhaseOneDots = DotHitsFor(a, bId);
            var bPhaseOneDots = DotHitsFor(b, bId);
            var cPhaseOneDots = DotHitsFor(c, bId);
            if (aPhaseOneDots.Count != ExpectedFireDotTicks
                || bPhaseOneDots.Count != ExpectedFireDotTicks
                || cPhaseOneDots.Count != ExpectedFireDotTicks)
                return (false, "fire/dot-ticks: extra ticks after removal A=" + aPhaseOneDots.Count
                    + " B=" + bPhaseOneDots.Count + " C=" + cPhaseOneDots.Count
                    + SummarizeFire(a, b, c, bId));

            // 击杀阶段：连续火剑命中 B，直到三端都观察到 Dead=true。
            gate.SetStage("fire/kill");
            CombatStateUpdate? deadA = null;
            CombatStateUpdate? deadB = null;
            CombatStateUpdate? deadC = null;
            uint castSeq = 1;
            int acceptedCasts = 1;
            for (int round = 0; round < MaxKillRounds; round++)
            {
                if (FindDeadProjections(a, b, c, bId, out deadA, out deadB, out deadC)) break;

                castSeq++;
                var cast = await CastAsync(all, a, gate, bId, castSeq, equipmentRevision,
                    baseline.LifeId, "fire/cast-request", "fire/cast-confirm");
                if (!cast.ok) return (false, cast.reason);
                var confirm = cast.confirm!;
                if (!confirm.Accepted)
                {
                    if (confirm.RejectReason == "cooldown")
                    {
                        // 上一跳恰好落在施法决策附近：等下一跳（或死讯）后重试，仍受共享 deadline 约束。
                        int before = DotHitsFor(a, bId).Count;
                        await WaitUntil(all, gate, () =>
                            DotHitsFor(a, bId).Count > before
                            || FindDeadProjections(a, b, c, bId, out deadA, out deadB, out deadC), 2.5f);
                        continue;
                    }
                    return (false, "fire/kill: cast seq " + castSeq + " rejected: "
                        + confirm.RejectReason);
                }
                acceptedCasts++;

                // 每轮等一个 DoT 跳（或死讯）：跳伤覆盖 + 自然越过 10002 的 0.5s 冷却。
                int dotsBefore = DotHitsFor(a, bId).Count;
                if (!await WaitUntil(all, gate, () =>
                    {
                        if (FindDeadProjections(a, b, c, bId, out deadA, out deadB, out deadC))
                            return true;
                        return DotHitsFor(a, bId).Count > dotsBefore;
                    }, 5f))
                    return (false, "fire/kill: no DoT tick or death after accepted cast seq="
                        + castSeq + SummarizeFire(a, b, c, bId));
            }

            gate.SetStage("fire/death");
            if (!FindDeadProjections(a, b, c, bId, out deadA, out deadB, out deadC))
                return (false, "fire/kill: B did not die within " + MaxKillRounds + " rounds ("
                    + acceptedCasts + " accepted casts)" + SummarizeFire(a, b, c, bId));

            var deadOnA = deadA!;
            var deadOnB = deadB!;
            var deadOnC = deadC!;
            // 三端死讯逐值一致（同一条 311 解码）。
            if (!deadOnA.Projection.Dead || !deadOnB.Projection.Dead || !deadOnC.Projection.Dead)
                return (false, "fire/death: Dead projection is not Dead=true on all clients");
            if (deadOnA.Projection.CombatVersion != deadOnB.Projection.CombatVersion
                || deadOnA.Projection.CombatVersion != deadOnC.Projection.CombatVersion)
                return (false, "fire/death: Dead CombatVersion differs across clients (A="
                    + deadOnA.Projection.CombatVersion + " B=" + deadOnB.Projection.CombatVersion
                    + " C=" + deadOnC.Projection.CombatVersion + ")");
            if (deadOnA.Projection.LifeId != deadOnB.Projection.LifeId
                || deadOnA.Projection.LifeId != deadOnC.Projection.LifeId)
                return (false, "fire/death: Dead LifeId differs across clients (A="
                    + deadOnA.Projection.LifeId + " B=" + deadOnB.Projection.LifeId
                    + " C=" + deadOnC.Projection.LifeId + ")");
            // 死亡发生在与 DoT 跳伤相同的生命代次（未复活）。
            if (deadOnA.Projection.LifeId != aPhaseOneDots[0].Hit.TargetLifeId)
                return (false, "fire/death: Dead LifeId=" + deadOnA.Projection.LifeId
                    + " != DoT TargetLifeId=" + aPhaseOneDots[0].Hit.TargetLifeId);
            // 死亡立即清状态：死讯投影不得再携带 201。
            if (HasEffect(deadOnA.Projection, FireEffectId)
                || HasEffect(deadOnB.Projection, FireEffectId)
                || HasEffect(deadOnC.Projection, FireEffectId))
                return (false, "fire/death: Dead projection still carries EffectId=" + FireEffectId);

            // 致死命中（来源不限：直伤或 DoT）——三端逐字段一致，来源钉死为 A。
            var fatalA = FatalHitFor(a, bId);
            var fatalB = FatalHitFor(b, bId);
            var fatalC = FatalHitFor(c, bId);
            if (!fatalA.HasValue || !fatalB.HasValue || !fatalC.HasValue)
                return (false, "fire/death: no fatal hit (TargetRemainingHp<=0) for B on A/B/C"
                    + SummarizeFire(a, b, c, bId));
            string? fatalMismatch = CompareFatalHitsAcrossClients(fatalA.Value, fatalB.Value,
                fatalC.Value, aId);
            if (fatalMismatch != null) return (false, fatalMismatch);
            Debug.Log("[ElementalAcceptance] fatal hit kind=" + fatalA.Value.Hit.DamageKind
                + " event=" + fatalA.Value.Hit.EventId + " hp=" + fatalA.Value.Hit.TargetRemainingHp);

            // 击杀阶段的跳伤同样纳入三端逐字段比较：EventId 序必须是首刀 3 跳的延续。
            var aAllDots = DotHitsFor(a, bId);
            var bAllDots = DotHitsFor(b, bId);
            var cAllDots = DotHitsFor(c, bId);
            if (aAllDots.Count < ExpectedFireDotTicks || aAllDots.Count != bAllDots.Count
                || aAllDots.Count != cAllDots.Count)
                return (false, "fire/kill-dots: tick counts A=" + aAllDots.Count
                    + " B=" + bAllDots.Count + " C=" + cAllDots.Count + SummarizeFire(a, b, c, bId));
            for (int i = 0; i < ExpectedFireDotTicks; i++)
                if (aAllDots[i].Hit.EventId != aPhaseOneDots[i].Hit.EventId)
                    return (false, "fire/kill-dots: EventId sequence is not a continuation of the "
                        + "first cast's ticks at index " + i + " (all=" + aAllDots[i].Hit.EventId
                        + " first-cast=" + aPhaseOneDots[i].Hit.EventId + ")");
            for (int i = 0; i < aAllDots.Count; i++)
            {
                string? mismatch = CompareDotHitsAcrossClients("fire/kill-dots", i,
                    aAllDots[i], bAllDots[i], cAllDots[i], aId);
                if (mismatch != null) return (false, mismatch);
            }

            // 死后不再收到 B 的 DoT 命中：跨两个跳间隔复核 EventId 集合不变。
            gate.SetStage("fire/post-death");
            var aAfter = DotEventIds(a, bId);
            var bAfter = DotEventIds(b, bId);
            var cAfter = DotEventIds(c, bId);
            await WaitSeconds(all, gate, 1.6f);
            if (gate.Cancelled || gate.DeadlineExpired)
                return (false, "fire/post-death: gate closed during post-death window");
            if (!aAfter.SetEquals(DotEventIds(a, bId))
                || !bAfter.SetEquals(DotEventIds(b, bId))
                || !cAfter.SetEquals(DotEventIds(c, bId)))
                return (false, "fire/post-death: new DoT hits arrived for B after death"
                    + SummarizeFire(a, b, c, bId));

            return (true, "ok");
        }

        /// <summary>
        /// 麻痹取消前摇：A 先起手雷（等 A 的 CastConfirm 落定后再发 B 的请求 ⇒ 服务端
        /// B.AcceptedTick &gt; A.AcceptedTick，A 的判定 tick（+6）必然早于 B 的首段（+6），
        /// 即命中时 B 仍在 0.35s 前摇且未发射）。
        ///
        /// 断言：
        /// ① A/B/C 收到同一 CastId（B 的施法）的 CastCancelBroadcast，逐字段一致
        ///    （CasterLifeId=B 的生命代次、EffectiveTick=A 的命中 tick、Reason=Hit）；
        /// ② B 的取消施法全程没有 DamageResult 命中、没有 ProjectileLaunchBroadcast；
        /// ③ A 的雷仍按自己的 CastId 产生挥击 + 剑气 2 条命中（三端逐值一致）；
        /// ④ 旁观者 C 的 RemoteCastPresenter 按 CastId 停掉已起手的攻击，后续投影不重播。
        ///    （B 是本地施法者，RemoteCastPresenter 按设计跳过 localPlayerId——本地表现归
        ///    SkillExecutor，B 端证据是接收到的取消消息本身。）
        /// </summary>
        private static async Task<(bool ok, string reason)> RunThunderCancelAsync(
            Ctx a, Ctx b, Ctx c, RunGate gate)
        {
            var all = new[] { a, b, c };

            var login = await LoginThreeAsync(a, b, c, gate);
            if (!login.ok) return (false, login.reason);
            long aId = login.aId;
            long bId = login.bId;
            var aInventory = login.inventory!;
            var bInventory = b.Manager.WeaponInventory;
            if (bInventory == null)
                return (false, "baseline: B has no weapon inventory (ElementalSwordQiV1 disabled?)");

            // 取消广播的收件人 = B + B 的 AOI 观察者；等 A/C 都看到 B 再起手。
            gate.SetStage("cancel/aoi-a-sees-b");
            if (!await WaitUntil(all, gate,
                    () => a.Registry.GetView(bId) != null && c.Registry.GetView(bId) != null, 15f))
                return (false, "cancel/aoi: A/C did not both see B (bId=" + bId + ")");

            gate.SetStage("cancel/baseline");
            if (FindWeapon(aInventory, ThunderElementId) == null)
                return (false, "baseline: A has no thunder (ElementId=3) weapon");

            var equip = await EquipElementAsync(all, a, gate, ThunderElementId, 1);
            if (!equip.ok) return (false, equip.reason);
            uint equipmentRevision = equip.confirm!.EquipmentRevision;

            // A 先施法：确认到达客户端即证明服务端已完成 Decide（AcceptedTick 已落定），
            // 之后再发 B 的请求 ⇒ B 的 AcceptedTick 必然更大。
            gate.SetStage("cancel/a-cast");
            var aCast = await CastAsync(all, a, gate, bId, 1, equipmentRevision,
                aInventory.LifeId, "cancel/a-request", "cancel/a-confirm");
            if (!aCast.ok) return (false, aCast.reason);
            var aConfirm = aCast.confirm!;
            if (!aConfirm.Accepted)
                return (false, "cancel/a-confirm: rejected: " + aConfirm.RejectReason);
            if (aConfirm.CastId == 0)
                return (false, "cancel/a-confirm: accepted but CastId=0");
            if (Math.Abs(aConfirm.AttackSpeedMultiplier - 1f) > 0.001f)
                return (false, "cancel/timing: A attack speed "
                    + aConfirm.AttackSpeedMultiplier.ToString("F3", CultureInfo.InvariantCulture)
                    + " != 1; the +6 tick windup assumption no longer holds");

            gate.SetStage("cancel/b-cast");
            var bCast = await CastAsync(all, b, gate, aId, 1, bInventory.EquipmentRevision,
                bInventory.LifeId, "cancel/b-request", "cancel/b-confirm");
            if (!bCast.ok) return (false, bCast.reason);
            var bConfirm = bCast.confirm!;
            if (!bConfirm.Accepted)
                return (false, "cancel/b-confirm: rejected: " + bConfirm.RejectReason);
            if (bConfirm.CastId == 0)
                return (false, "cancel/b-confirm: accepted but CastId=0");
            if (Math.Abs(bConfirm.AttackSpeedMultiplier - 1f) > 0.001f)
                return (false, "cancel/timing: B attack speed "
                    + bConfirm.AttackSpeedMultiplier.ToString("F3", CultureInfo.InvariantCulture)
                    + " != 1; the +6 tick windup assumption no longer holds");

            // 用真实 AcceptedTick 校验时间关系（不靠 sleep）：
            // A 命中 = A.AcceptedTick + 6；B 首段 = B.AcceptedTick + 6 必须更晚。
            if (bConfirm.AcceptedTick <= aConfirm.AcceptedTick)
                return (false, "cancel/tick-order: B AcceptedTick=" + bConfirm.AcceptedTick
                    + " is not after A's " + aConfirm.AcceptedTick);
            uint aHitTick = aConfirm.AcceptedTick + LightningWindupTicks;
            if (bConfirm.AcceptedTick + LightningWindupTicks <= aHitTick)
                return (false, "cancel/tick-order: B first stage tick "
                    + (bConfirm.AcceptedTick + LightningWindupTicks)
                    + " is not after A's hit tick " + aHitTick);

            // A 的雷命中：三端各收到挥击 + 剑气两条命中（命中即触发麻痹取消）。
            gate.SetStage("cancel/a-hits");
            var aHitsOnA = new List<HitEntry>();
            var aHitsOnB = new List<HitEntry>();
            var aHitsOnC = new List<HitEntry>();
            if (!await WaitUntil(all, gate, () =>
                {
                    aHitsOnA = HitsFor(a, aConfirm.CastId, bId);
                    aHitsOnB = HitsFor(b, aConfirm.CastId, bId);
                    aHitsOnC = HitsFor(c, aConfirm.CastId, bId);
                    return aHitsOnA.Count >= ExpectedHitsPerTarget
                        && aHitsOnB.Count >= ExpectedHitsPerTarget
                        && aHitsOnC.Count >= ExpectedHitsPerTarget;
                }, 15f))
                return (false, "cancel/a-hits: A/B/C did not all receive "
                    + ExpectedHitsPerTarget + " hits from A's cast "
                    + SummarizeCancel(a, b, c, bId, bConfirm.CastId));
            if (aHitsOnA.Count != ExpectedHitsPerTarget
                || aHitsOnB.Count != ExpectedHitsPerTarget
                || aHitsOnC.Count != ExpectedHitsPerTarget)
                return (false, "cancel/a-hits: hit counts A=" + aHitsOnA.Count
                    + " B=" + aHitsOnB.Count + " C=" + aHitsOnC.Count
                    + " != " + ExpectedHitsPerTarget + SummarizeCancel(a, b, c, bId, bConfirm.CastId));

            // ① 三端收到同一条取消：同一 CastId / 生命代次 / 生效 tick / 原因。
            gate.SetStage("cancel/broadcast");
            CastCancelBroadcast? cancelA = null;
            CastCancelBroadcast? cancelB = null;
            CastCancelBroadcast? cancelC = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    cancelA = a.Cancels.ToArray().FirstOrDefault(x => x.CastId == bConfirm.CastId);
                    cancelB = b.Cancels.ToArray().FirstOrDefault(x => x.CastId == bConfirm.CastId);
                    cancelC = c.Cancels.ToArray().FirstOrDefault(x => x.CastId == bConfirm.CastId);
                    return cancelA != null && cancelB != null && cancelC != null;
                }, 10f))
                return (false, "cancel/broadcast: A/B/C did not all receive CastCancelBroadcast"
                    + " for B's cast " + bConfirm.CastId
                    + SummarizeCancel(a, b, c, bId, bConfirm.CastId));

            foreach (var cancel in new[] { cancelA!, cancelB!, cancelC! })
            {
                if (cancel.CasterPlayerId != bId)
                    return (false, "cancel/broadcast: CasterPlayerId=" + cancel.CasterPlayerId
                        + " != B " + bId);
                if (cancel.CasterLifeId != bInventory.LifeId)
                    return (false, "cancel/broadcast: CasterLifeId=" + cancel.CasterLifeId
                        + " != B LifeId " + bInventory.LifeId);
                if (cancel.Reason != NpcActionCancelReason.Hit)
                    return (false, "cancel/broadcast: Reason=" + cancel.Reason + " != Hit");
                if (cancel.EffectiveTick != aHitTick)
                    return (false, "cancel/broadcast: EffectiveTick=" + cancel.EffectiveTick
                        + " != A hit tick " + aHitTick + " (A AcceptedTick="
                        + aConfirm.AcceptedTick + ")");
            }
            if (cancelA!.EffectiveTick != cancelB!.EffectiveTick
                || cancelA.EffectiveTick != cancelC!.EffectiveTick)
                return (false, "cancel/broadcast: EffectiveTick differs across clients (A="
                    + cancelA.EffectiveTick + " B=" + cancelB.EffectiveTick
                    + " C=" + cancelC.EffectiveTick + ")");
            Debug.Log("[ElementalAcceptance] cancel: A accepted=" + aConfirm.AcceptedTick
                + " B accepted=" + bConfirm.AcceptedTick
                + " A hit/cancel tick=" + cancelA.EffectiveTick
                + " B first stage=" + (bConfirm.AcceptedTick + LightningWindupTicks));

            // ② B 的施法在发射前被整组作废：等到“本来该发射”的 tick 之后，三端仍无
            //    该 CastId 的 DamageResult 命中与 ProjectileLaunchBroadcast。
            gate.SetStage("cancel/b-never-launched");
            uint bFirstStageTick = bConfirm.AcceptedTick + LightningWindupTicks;
            if (!await WaitUntilServerTick(all, gate, bFirstStageTick + 1))
                return (false, "cancel/b-never-launched: three clients did not all reach"
                    + " server tick " + (bFirstStageTick + 1));
            await WaitSeconds(all, gate, 0.2f); // 有界观察窗：让在途消息落地（取消点已由 tick 断言钉死）
            if (gate.Cancelled || gate.DeadlineExpired)
                return (false, "cancel/b-never-launched: gate closed during observation window");

            var bHitsOnA = AllHitsForCast(a, bConfirm.CastId);
            var bHitsOnB = AllHitsForCast(b, bConfirm.CastId);
            var bHitsOnC = AllHitsForCast(c, bConfirm.CastId);
            if (bHitsOnA.Count != 0 || bHitsOnB.Count != 0 || bHitsOnC.Count != 0)
                return (false, "cancel/b-never-launched: cancelled cast produced damage hits"
                    + " (A=" + bHitsOnA.Count + " B=" + bHitsOnB.Count
                    + " C=" + bHitsOnC.Count + ")"
                    + SummarizeCancel(a, b, c, bId, bConfirm.CastId));
            foreach (var ctx in all)
            {
                var launched = ctx.Launches.ToArray()
                    .FirstOrDefault(x => x.CastId == bConfirm.CastId);
                if (launched != null)
                    return (false, "cancel/b-never-launched: cancelled cast produced"
                        + " ProjectileLaunchBroadcast on a client (caster="
                        + launched.CasterPlayerId + ")");
            }

            // ③ A 的雷剑不受影响：上面按 A 的 CastId 过滤后每端恰好 2 条（挥击 + 剑气）。

            // ④ 旁观者 C 的 RemoteCastPresenter：先按 C 实际收到的 CastBroadcast 起手，
            //    再喂取消——按 CastId 停掉攻击；后续任何 ActiveCast 投影都不得重播。
            //    （PlayMode 程序集不能引用 UnityEditor.AssetDatabase，展示目录用运行期实例构造。）
            gate.SetStage("cancel/presenter-c");
            var view = c.Registry.GetView(bId);
            if (view == null)
                return (false, "cancel/presenter: C has no view for B");
            var cAnimator = view.GameObject.AddComponent<RemoteEntityAnimator>();
            view.Animator = cAnimator; // internal setter（InternalsVisibleTo KcpNet.PlayModeTests）

            CastBroadcast? bBroadcast = null;
            if (!await WaitUntil(all, gate, () =>
                {
                    bBroadcast = c.Broadcasts.ToArray()
                        .FirstOrDefault(x => x.CastId == bConfirm.CastId);
                    return bBroadcast != null;
                }, 10f))
                return (false, "cancel/presenter: C received no CastBroadcast for B's cast "
                    + bConfirm.CastId);

            var catalog = CreateRuntimeAttackCatalog(SkillId, out var skillData);
            try
            {
                var presenter = new RemoteCastPresenter(c.Registry, c.Manager.LocalPlayerId,
                    catalog, projectileFactory: (_, __, ___) => null);

                // 真实到达顺序：CastBroadcast（起手）先于取消。
                presenter.OnCastBroadcast(bBroadcast!);
                if (cAnimator.AttackRemainingForTest <= 0f)
                    return (false, "cancel/presenter: C did not present B's attack"
                        + " from CastBroadcast " + bConfirm.CastId);
                int startsAfterBroadcast = cAnimator.AttackStartsForTest;

                presenter.OnCastCancel(cancelC);
                if (cAnimator.AttackRemainingForTest != 0f)
                    return (false, "cancel/presenter: C's attack is still playing after cancel"
                        + " (remaining=" + cAnimator.AttackRemainingForTest
                        + "s, CastId=" + bConfirm.CastId + ")");

                // 服务端后续（含取消前的 Windup/Active/Recovery）投影不得让 C 重新起手。
                foreach (var update in UpdatesFor(c, bId))
                {
                    if (update.Projection.ActiveCast == null) continue;
                    presenter.OnActiveCast(bId, update.Projection.ActiveCast);
                }
                if (cAnimator.AttackStartsForTest != startsAfterBroadcast
                    || cAnimator.AttackRemainingForTest != 0f)
                    return (false, "cancel/presenter: C replayed B's attack after cancel"
                        + " (starts=" + cAnimator.AttackStartsForTest + " expected "
                        + startsAfterBroadcast + ", remaining=" + cAnimator.AttackRemainingForTest + ")");
            }
            finally
            {
                UnityEngine.Object.Destroy(catalog);
                UnityEngine.Object.Destroy(skillData);
            }

            return (true, "ok");
        }

        // —— 共用三客户端夹具（雷/火场景一致）：登录 → 换装 → 施法。——

        private static async Task<(bool ok, string reason, long aId, long bId,
            WeaponInventoryBaseline? inventory)> LoginThreeAsync(
            Ctx a, Ctx b, Ctx c, RunGate gate)
        {
            var all = new[] { a, b, c };

            gate.SetStage("login-A");
            a.Manager.ConnectAndLogin(Host, Port);
            if (!await WaitInWorld(new[] { a }, gate, a, 10f))
                return (false, "login-A: A failed to enter world", 0, 0, null);
            long aId = a.Manager.LocalPlayerId;

            gate.SetStage("login-B");
            b.Manager.ConnectAndLogin(Host, Port);
            if (!await WaitInWorld(new[] { a, b }, gate, b, 10f))
                return (false, "login-B: B failed to enter world", 0, 0, null);

            gate.SetStage("login-C");
            c.Manager.ConnectAndLogin(Host, Port);
            if (!await WaitInWorld(all, gate, c, 10f))
                return (false, "login-C: C failed to enter world", 0, 0, null);

            long bId = b.Manager.LocalPlayerId;

            // 三客户端同出生点；等 C 的 AOI 看到 B（C 的 311/DamageResult 依赖观察者关系）。
            gate.SetStage("aoi-c-sees-b");
            if (!await WaitUntil(all, gate, () => c.Registry.GetView(bId) != null, 15f))
                return (false, "aoi: C never saw B (bId=" + bId + ")", 0, 0, null);

            gate.SetStage("baseline");
            var inventory = a.Manager.WeaponInventory;
            if (inventory == null)
                return (false, "baseline: A has no weapon inventory (ElementalSwordQiV1 disabled?)",
                    0, 0, null);

            return (true, string.Empty, aId, bId, inventory);
        }

        private static WeaponInstanceSummary? FindWeapon(WeaponInventoryBaseline inventory, int elementId)
        {
            foreach (var weapon in inventory.Weapons)
                if (weapon.ElementId == elementId) return weapon;
            return null;
        }

        private static async Task<(bool ok, string reason, EquipWeaponConfirm? confirm)>
            EquipElementAsync(Ctx[] ctxs, Ctx a, RunGate gate, int elementId, uint clientEquipSeq)
        {
            var inventory = a.Manager.WeaponInventory!;
            var weapon = FindWeapon(inventory, elementId);
            if (weapon == null)
                return (false, "equip-request: A has no element " + elementId + " weapon", null);

            gate.SetStage("equip-request");
            a.Adapter.Send(MessageId.EquipWeaponRequest, new EquipWeaponRequest
            {
                ClientEquipSeq = clientEquipSeq,
                WeaponInstanceId = weapon.InstanceId,
                KnownEquipmentRevision = inventory.EquipmentRevision
            });

            gate.SetStage("equip-confirm");
            EquipWeaponConfirm? confirm = null;
            if (!await WaitUntil(ctxs, gate, () =>
                {
                    confirm = a.EquipConfirms.ToArray()
                        .FirstOrDefault(x => x.ClientEquipSeq == clientEquipSeq);
                    return confirm != null;
                }, 10f))
                return (false, "equip-confirm: A received no EquipWeaponConfirm seq="
                    + clientEquipSeq, null);
            if (!confirm!.Accepted)
                return (false, "equip-confirm: rejected: " + confirm.RejectReason, null);
            if (confirm.ElementId != elementId)
                return (false, "equip-confirm: ElementId=" + confirm.ElementId + " != " + elementId, null);
            return (true, string.Empty, confirm);
        }

        private static async Task<(bool ok, string reason, CastConfirm? confirm)> CastAsync(
            Ctx[] ctxs, Ctx caster, RunGate gate, long targetId, uint clientCastSeq,
            uint equipmentRevision, uint casterLifeId, string requestStage, string confirmStage)
        {
            gate.SetStage(requestStage);
            caster.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = SkillId,
                TargetPlayerId = targetId,
                Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f },
                ClientCastSeq = clientCastSeq,
                EquipmentRevision = equipmentRevision,
                CasterLifeId = casterLifeId
            });

            gate.SetStage(confirmStage);
            CastConfirm? confirm = null;
            if (!await WaitUntil(ctxs, gate, () =>
                {
                    confirm = caster.Confirms.ToArray()
                        .FirstOrDefault(x => x.ClientCastSeq == clientCastSeq);
                    return confirm != null;
                }, 10f))
                return (false, "cast-confirm: caster received no CastConfirm seq="
                    + clientCastSeq, null);
            return (true, string.Empty, confirm);
        }

        /// <summary>
        /// PlayMode 程序集不能引用 UnityEditor.AssetDatabase（见 RemoteRenderBranchTests 注释）；
        /// 用运行期实例 + 反射填私有序列化字段，构造只含该技能起手触发器的最小展示目录，
        /// 让 RemoteCastPresenter 走真实的 TryGet → PlayAttack 路径。
        /// </summary>
        private static SkillPresentationCatalog CreateRuntimeAttackCatalog(int skillId,
            out SkillData skillData)
        {
            var catalog = ScriptableObject.CreateInstance<SkillPresentationCatalog>();
            var skill = ScriptableObject.CreateInstance<InstantSkillData>();
            SetPrivateField(typeof(SkillData), skill, "_skillId", skillId);
            SetPrivateField(typeof(SkillData), skill, "_animatorTrigger", "Attack");
            SetPrivateField(typeof(SkillPresentationCatalog), catalog, "_skills", new[] { skill });
            skillData = skill;
            return catalog;
        }

        private static void SetPrivateField(Type type, object target, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, type.Name + "." + name + " (serialized field) not found");
            field!.SetValue(target, value);
        }

        private static CombatStateUpdate? FindDeadProjection(Ctx ctx, long entityId)
            => UpdatesFor(ctx, entityId).FirstOrDefault(u => u.Projection.Dead);

        /// <summary>三端都收到目标 Dead=true 投影时返回 true（任一端缺失即继续等待）。</summary>
        private static bool FindDeadProjections(Ctx a, Ctx b, Ctx c, long entityId,
            out CombatStateUpdate? deadA, out CombatStateUpdate? deadB, out CombatStateUpdate? deadC)
        {
            deadA = FindDeadProjection(a, entityId);
            deadB = FindDeadProjection(b, entityId);
            deadC = FindDeadProjection(c, entityId);
            return deadA != null && deadB != null && deadC != null;
        }

        /// <summary>目标收到的 DoT 跳伤：按全局 EventId 去重（传输重复投递只算一次）并按 EventId
        /// 升序排列（分配序 = 时间序）。</summary>
        private static List<(DamageResult Result, HitEntry Hit)> DotHitsFor(Ctx ctx, long targetId)
        {
            var byEvent = new Dictionary<ulong, (DamageResult Result, HitEntry Hit)>();
            foreach (var result in ctx.Damages.ToArray())
            {
                if (result.Hits == null) continue;
                foreach (var hit in result.Hits)
                {
                    if (hit.DamageKind != DamageKind.Dot || hit.TargetPlayerId != targetId) continue;
                    if (!byEvent.ContainsKey(hit.EventId)) byEvent[hit.EventId] = (result, hit);
                }
            }
            var hits = byEvent.Values.ToList();
            hits.Sort((x, y) => x.Hit.EventId.CompareTo(y.Hit.EventId));
            return hits;
        }

        private static HashSet<ulong> DotEventIds(Ctx ctx, long targetId)
        {
            var ids = new HashSet<ulong>();
            foreach (var (_, hit) in DotHitsFor(ctx, targetId)) ids.Add(hit.EventId);
            return ids;
        }

        /// <summary>三端同一跳 DoT 逐字段相等（按 EventId 对齐）；来源必须钉死为施法者 A；
        /// 返回 null = 一致，否则返回失败原因。</summary>
        private static string? CompareDotHitsAcrossClients(string phase, int index,
            (DamageResult Result, HitEntry Hit) aDot,
            (DamageResult Result, HitEntry Hit) bDot,
            (DamageResult Result, HitEntry Hit) cDot,
            long sourceId)
        {
            var aHit = aDot.Hit;
            var bHit = bDot.Hit;
            var cHit = cDot.Hit;
            if (aHit.EventId != bHit.EventId || aHit.EventId != cHit.EventId)
                return phase + ": EventId differs across clients at tick " + index
                    + " (A=" + aHit.EventId + " B=" + bHit.EventId + " C=" + cHit.EventId + ")";
            if (aHit.Damage != bHit.Damage || aHit.Damage != cHit.Damage)
                return phase + ": Damage differs across clients at tick " + index
                    + " (A=" + aHit.Damage + " B=" + bHit.Damage + " C=" + cHit.Damage + ")";
            if (aHit.TargetRemainingHp != bHit.TargetRemainingHp
                || aHit.TargetRemainingHp != cHit.TargetRemainingHp)
                return phase + ": TargetRemainingHp differs across clients at tick " + index
                    + " (A=" + aHit.TargetRemainingHp + " B=" + bHit.TargetRemainingHp
                    + " C=" + cHit.TargetRemainingHp + ")";
            if (aHit.TargetLifeId != bHit.TargetLifeId || aHit.TargetLifeId != cHit.TargetLifeId)
                return phase + ": TargetLifeId differs across clients at tick " + index
                    + " (A=" + aHit.TargetLifeId + " B=" + bHit.TargetLifeId
                    + " C=" + cHit.TargetLifeId + ")";
            if (aHit.CombatVersion != bHit.CombatVersion || aHit.CombatVersion != cHit.CombatVersion)
                return phase + ": CombatVersion differs across clients at tick " + index
                    + " (A=" + aHit.CombatVersion + " B=" + bHit.CombatVersion
                    + " C=" + cHit.CombatVersion + ")";
            if (aHit.EventSeq != bHit.EventSeq || aHit.EventSeq != cHit.EventSeq)
                return phase + ": EventSeq differs across clients at tick " + index
                    + " (A=" + aHit.EventSeq + " B=" + bHit.EventSeq + " C=" + cHit.EventSeq + ")";
            if (aHit.CastId != bHit.CastId || aHit.CastId != cHit.CastId)
                return phase + ": CastId differs across clients at tick " + index
                    + " (A=" + aHit.CastId + " B=" + bHit.CastId + " C=" + cHit.CastId + ")";
            if (aDot.Result.SourcePlayerId != sourceId || bDot.Result.SourcePlayerId != sourceId
                || cDot.Result.SourcePlayerId != sourceId)
                return phase + ": SourcePlayerId is not the caster A(" + sourceId + ") at tick "
                    + index + " (A=" + aDot.Result.SourcePlayerId + " B=" + bDot.Result.SourcePlayerId
                    + " C=" + cDot.Result.SourcePlayerId + ")";
            // 跳伤不得带格挡标记（格挡只属于直接命中）。
            if (aHit.IsBlocked || bHit.IsBlocked || cHit.IsBlocked)
                return phase + ": DoT hit " + index + " carries IsBlocked=true";
            return null;
        }

        /// <summary>致死命中（直伤或 DoT）三端逐字段一致，来源钉死为 A。</summary>
        private static string? CompareFatalHitsAcrossClients(
            (DamageResult Result, HitEntry Hit) aFatal,
            (DamageResult Result, HitEntry Hit) bFatal,
            (DamageResult Result, HitEntry Hit) cFatal,
            long sourceId)
        {
            var a = aFatal.Hit;
            var b = bFatal.Hit;
            var c = cFatal.Hit;
            if (a.EventId != b.EventId || a.EventId != c.EventId)
                return "fire/death: fatal EventId differs across clients (A=" + a.EventId
                    + " B=" + b.EventId + " C=" + c.EventId + ")";
            if (a.Damage != b.Damage || a.Damage != c.Damage)
                return "fire/death: fatal Damage differs across clients (A=" + a.Damage
                    + " B=" + b.Damage + " C=" + c.Damage + ")";
            if (a.IsCritical != b.IsCritical || a.IsCritical != c.IsCritical)
                return "fire/death: fatal IsCritical differs across clients";
            if (a.IsBlocked != b.IsBlocked || a.IsBlocked != c.IsBlocked)
                return "fire/death: fatal IsBlocked differs across clients";
            if (a.TargetRemainingHp != b.TargetRemainingHp
                || a.TargetRemainingHp != c.TargetRemainingHp)
                return "fire/death: fatal TargetRemainingHp differs across clients (A="
                    + a.TargetRemainingHp + " B=" + b.TargetRemainingHp
                    + " C=" + c.TargetRemainingHp + ")";
            if (a.HitX != b.HitX || a.HitX != c.HitX || a.HitZ != b.HitZ || a.HitZ != c.HitZ)
                return "fire/death: fatal HitX/HitZ differs across clients";
            if (a.CastId != b.CastId || a.CastId != c.CastId)
                return "fire/death: fatal CastId differs across clients (A=" + a.CastId
                    + " B=" + b.CastId + " C=" + c.CastId + ")";
            if (a.EventSeq != b.EventSeq || a.EventSeq != c.EventSeq)
                return "fire/death: fatal EventSeq differs across clients (A=" + a.EventSeq
                    + " B=" + b.EventSeq + " C=" + c.EventSeq + ")";
            if (a.DamageKind != b.DamageKind || a.DamageKind != c.DamageKind)
                return "fire/death: fatal DamageKind differs across clients";
            if (a.TargetLifeId != b.TargetLifeId || a.TargetLifeId != c.TargetLifeId)
                return "fire/death: fatal TargetLifeId differs across clients (A=" + a.TargetLifeId
                    + " B=" + b.TargetLifeId + " C=" + c.TargetLifeId + ")";
            if (a.CombatVersion != b.CombatVersion || a.CombatVersion != c.CombatVersion)
                return "fire/death: fatal CombatVersion differs across clients (A="
                    + a.CombatVersion + " B=" + b.CombatVersion + " C=" + c.CombatVersion + ")";
            if (aFatal.Result.SourcePlayerId != sourceId || bFatal.Result.SourcePlayerId != sourceId
                || cFatal.Result.SourcePlayerId != sourceId)
                return "fire/death: fatal SourcePlayerId is not the caster A(" + sourceId
                    + ") (A=" + aFatal.Result.SourcePlayerId + " B=" + bFatal.Result.SourcePlayerId
                    + " C=" + cFatal.Result.SourcePlayerId + ")";
            return null;
        }

        /// <summary>致死命中：目标全部命中（直伤或 DoT）中按全局 EventId 序第一条
        /// TargetRemainingHp≤0 的记录（跨零的那一击）。</summary>
        private static (DamageResult Result, HitEntry Hit)? FatalHitFor(Ctx ctx, long targetId)
        {
            var byEvent = new Dictionary<ulong, (DamageResult Result, HitEntry Hit)>();
            foreach (var result in ctx.Damages.ToArray())
            {
                if (result.Hits == null) continue;
                foreach (var hit in result.Hits)
                {
                    if (hit.TargetPlayerId != targetId) continue;
                    if (!byEvent.ContainsKey(hit.EventId)) byEvent[hit.EventId] = (result, hit);
                }
            }
            var ordered = byEvent.Values.OrderBy(x => x.Hit.EventId).ToList();
            foreach (var entry in ordered)
                if (entry.Hit.TargetRemainingHp <= 0f) return entry;
            return null;
        }

        private static List<HitEntry> HitsFor(Ctx ctx, ulong castId, long targetId)
        {
            var hits = new List<HitEntry>();
            foreach (var result in ctx.Damages.ToArray())
            {
                if (result.SkillId != SkillId) continue;
                foreach (var hit in result.Hits)
                {
                    if (hit.CastId == castId && hit.TargetPlayerId == targetId)
                        hits.Add(hit);
                }
            }
            hits.Sort((x, y) => x.EventSeq.CompareTo(y.EventSeq));
            return hits;
        }

        /// <summary>某施法实例的全部命中（不限目标）——用于证明被取消的施法没有留下任何伤害。</summary>
        private static List<HitEntry> AllHitsForCast(Ctx ctx, ulong castId)
        {
            var hits = new List<HitEntry>();
            foreach (var result in ctx.Damages.ToArray())
            {
                if (result.Hits == null) continue;
                foreach (var hit in result.Hits)
                    if (hit.CastId == castId) hits.Add(hit);
            }
            return hits;
        }

        /// <summary>某实体收到的完整投影，按 CombatVersion 递增（同版本重复投递不产生额外状态迁移）。</summary>
        private static List<CombatStateUpdate> UpdatesFor(Ctx ctx, long entityId)
        {
            var updates = ctx.CombatUpdates.ToArray().Where(u => u.EntityId == entityId).ToList();
            updates.Sort((x, y) => x.Projection.CombatVersion.CompareTo(y.Projection.CombatVersion));
            return updates;
        }

        private static bool HasEffect(CombatProjection projection, int effectId)
            => HasEffect(projection.Statuses, effectId);

        private static bool HasEffect(IList<StatusProjection>? statuses, int effectId)
        {
            if (statuses == null) return false;
            foreach (var status in statuses)
                if (status.EffectId == effectId) return true;
            return false;
        }

        /// <summary>311 识别：状态必须存在且来源是施法者 A（防把别人/客户端的同 ID 状态当成本次施加）。</summary>
        private static bool HasEffectFrom(CombatProjection projection, int effectId, long sourceId)
        {
            var statuses = projection.Statuses;
            if (statuses == null) return false;
            foreach (var status in statuses)
                if (status.EffectId == effectId) return status.SourceId == sourceId;
            return false;
        }

        private static bool HasThunder(CombatProjection projection)
            => HasEffect(projection, ThunderEffectId);

        private static bool HasThunder(IList<StatusProjection>? statuses)
            => HasEffect(statuses, ThunderEffectId);

        private static bool HasThunderFrom(CombatProjection projection, long sourceId)
            => HasEffectFrom(projection, ThunderEffectId, sourceId);

        private static bool HasThunderFrom(IList<StatusProjection>? statuses, long sourceId)
        {
            if (statuses == null) return false;
            foreach (var status in statuses)
                if (status.EffectId == ThunderEffectId) return status.SourceId == sourceId;
            return false;
        }

        private static int CountEffectApplications(Ctx ctx, long entityId, int effectId)
        {
            bool previous = false;
            int count = 0;
            foreach (var update in UpdatesFor(ctx, entityId))
            {
                bool has = HasEffect(update.Projection, effectId);
                if (!previous && has) count++;
                previous = has;
            }
            return count;
        }

        private static int CountEffectRemovals(Ctx ctx, long entityId, int effectId)
        {
            bool previous = false;
            int count = 0;
            foreach (var update in UpdatesFor(ctx, entityId))
            {
                bool has = HasEffect(update.Projection, effectId);
                if (previous && !has) count++;
                previous = has;
            }
            return count;
        }

        private static int CountThunderApplications(Ctx ctx, long entityId)
            => CountEffectApplications(ctx, entityId, ThunderEffectId);

        private static int CountThunderRemovals(Ctx ctx, long entityId)
            => CountEffectRemovals(ctx, entityId, ThunderEffectId);

        /// <summary>失败诊断：CastBroadcast 的 CastId:ElementId、DamageResult 命中、B 实体 311 摘要。</summary>
        private static string Summarize(Ctx a, Ctx b, Ctx c, long bId)
        {
            var casts = a.Broadcasts.ToArray();
            string castText = casts.Length == 0
                ? "-"
                : string.Join(" ", casts.Select(x => x.CastId + ":e" + x.ElementId));
            return " | casts(A)=" + castText
                + " dmg(B) A=" + DescribeHits(a, bId)
                + " B=" + DescribeHits(b, bId)
                + " C=" + DescribeHits(c, bId)
                + " 311(B)=" + Describe(b, bId)
                + " 311(C)=" + Describe(c, bId);
        }

        private static string DescribeHits(Ctx ctx, long targetId)
        {
            var parts = new List<string>();
            foreach (var result in ctx.Damages.ToArray())
                foreach (var hit in result.Hits)
                    if (hit.TargetPlayerId == targetId)
                        parts.Add(hit.CastId + ":e" + hit.EventSeq + ":v" + hit.CombatVersion
                            + ":hp" + hit.TargetRemainingHp);
            return "[" + string.Join(" ", parts) + "]";
        }

        /// <summary>取消场景失败诊断：三端取消广播、该 CastId 的命中与 B 的完整投影摘要。</summary>
        private static string SummarizeCancel(Ctx a, Ctx b, Ctx c, long bId, ulong castId)
            => " | cancel(A)=" + DescribeCancels(a, castId)
                + " cancel(B)=" + DescribeCancels(b, castId)
                + " cancel(C)=" + DescribeCancels(c, castId)
                + " hits(" + castId + ") A=" + DescribeAllHits(a, castId)
                + " B=" + DescribeAllHits(b, castId)
                + " C=" + DescribeAllHits(c, castId)
                + " 311(B)=" + Describe(b, bId);

        private static string DescribeCancels(Ctx ctx, ulong castId)
        {
            var parts = ctx.Cancels.ToArray()
                .Where(x => x.CastId == castId)
                .Select(x => "t" + x.EffectiveTick + ":life" + x.CasterLifeId + ":" + x.Reason);
            return "[" + string.Join(" ", parts) + "]";
        }

        private static string DescribeAllHits(Ctx ctx, ulong castId)
        {
            var parts = AllHitsForCast(ctx, castId)
                .Select(h => h.CastId + ":e" + h.EventSeq + ":v" + h.CombatVersion
                    + ":hp" + h.TargetRemainingHp);
            return "[" + string.Join(" ", parts) + "]";
        }

        /// <summary>火场景失败诊断：三端 DoT 命中与 B 的完整投影摘要。</summary>
        private static string SummarizeFire(Ctx a, Ctx b, Ctx c, long bId)
            => " | dot(A)=" + DescribeDots(a, bId)
                + " dot(B)=" + DescribeDots(b, bId)
                + " dot(C)=" + DescribeDots(c, bId)
                + " 311(B)=" + Describe(b, bId)
                + " 311(C)=" + Describe(c, bId);

        private static string DescribeDots(Ctx ctx, long targetId)
        {
            var parts = new List<string>();
            foreach (var (_, hit) in DotHitsFor(ctx, targetId))
                parts.Add(hit.EventId + ":seq" + hit.EventSeq + ":v" + hit.CombatVersion
                    + ":hp" + hit.TargetRemainingHp + ":cast" + hit.CastId);
            return "[" + string.Join(" ", parts) + "]";
        }

        private static string Describe(Ctx ctx, long entityId)
        {
            var parts = new List<string>();
            foreach (var update in UpdatesFor(ctx, entityId))
            {
                var control = update.Projection.Control;
                string effects = "-";
                if (update.Projection.Statuses != null && update.Projection.Statuses.Count > 0)
                    effects = string.Join("+",
                        update.Projection.Statuses.Select(s => s.EffectId.ToString(CultureInfo.InvariantCulture)));
                parts.Add("v" + update.Projection.CombatVersion
                    + ":" + effects
                    + (control != null && control.ActionLock ? ":lock" : ":free")
                    + (update.Projection.Dead ? ":dead" : ""));
            }
            return "[" + string.Join(" ", parts) + "]";
        }

        private static void Pump(Ctx[] ctxs)
        {
            for (int i = 0; i < ctxs.Length; i++)
                ctxs[i].Manager.Update(0.05f);
        }

        private static async Task<bool> WaitUntil(Ctx[] ctxs, RunGate gate, Func<bool> condition, float seconds)
        {
            float elapsed = 0f;
            while (!gate.Cancelled && !gate.DeadlineExpired && elapsed < seconds)
            {
                Pump(ctxs);
                if (condition()) return true;
                await Task.Delay(50);
                elapsed += 0.05f;
            }
            if (gate.Cancelled) return false;
            Pump(ctxs);
            return condition();
        }

        /// <summary>泵固定时长（受共享 deadline/取消约束）——用于“死后不再有新命中”的观察窗。</summary>
        private static async Task WaitSeconds(Ctx[] ctxs, RunGate gate, float seconds)
        {
            float elapsed = 0f;
            while (!gate.Cancelled && !gate.DeadlineExpired && elapsed < seconds)
            {
                Pump(ctxs);
                await Task.Delay(50);
                elapsed += 0.05f;
            }
        }

        /// <summary>泵到本地墙钟越过 targetTicks（同机验收：ServerTimestamp = DateTime.UtcNow.Ticks）。</summary>
        private static async Task<bool> WaitUntilServerTicks(Ctx[] ctxs, RunGate gate, long targetTicks)
        {
            while (!gate.Cancelled && !gate.DeadlineExpired && DateTime.UtcNow.Ticks < targetTicks)
            {
                Pump(ctxs);
                await Task.Delay(20);
            }
            Pump(ctxs);
            return !gate.Cancelled && !gate.DeadlineExpired;
        }

        /// <summary>泵到三端收到的服务器快照 tick 都越过 targetTick（证明该 tick 已被服务端处理）。</summary>
        private static async Task<bool> WaitUntilServerTick(Ctx[] ctxs, RunGate gate, uint targetTick)
        {
            float elapsed = 0f;
            while (!gate.Cancelled && !gate.DeadlineExpired && elapsed < 10f)
            {
                Pump(ctxs);
                bool allPast = true;
                for (int i = 0; i < ctxs.Length; i++)
                {
                    if (ctxs[i].Manager.ServerTick <= targetTick)
                    {
                        allPast = false;
                        break;
                    }
                }
                if (allPast) return true;
                await Task.Delay(50);
                elapsed += 0.05f;
            }
            return false;
        }

        private static async Task<bool> WaitInWorld(Ctx[] ctxs, RunGate gate, Ctx target, float seconds)
        {
            float elapsed = 0f;
            while (!gate.Cancelled && !gate.DeadlineExpired && elapsed < seconds)
            {
                Pump(ctxs);
                if (target.Manager.CurrentState == ClientNetworkManager.State.InWorld) return true;
                await Task.Delay(50);
                elapsed += 0.05f;
            }
            return !gate.Cancelled && target.Manager.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
