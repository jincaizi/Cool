using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// Plan 05 任务 2 三客户端元素一致性验收（A 装雷剑施法、B 受击本人、C 旁观）：
    /// ① A 的 CastBroadcast 必须携带雷元素（ElementId=3）且 CastConfirm 接受；
    /// ② A/B/C 收到的同一 HitEntry 的 CastId/EventSeq/CombatVersion/TargetRemainingHp 逐值一致；
    /// ③ B 本人的 311 与 C 应用后的远程视图都必须看到麻痹状态
    ///    （Statuses 含 EffectId=301、Control.ActionLock=true、ControlVersion>0）；
    /// ④ 0.8s（12 tick）后 B/C 必须收到 Statuses 空、ActionLock=false 的投影，
    ///    且 EffectId=301 的移除只发生一次（按 CombatVersion 递增）。
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

        /// <summary>失败时报告当前阶段（协议等待无独立超时异常，只能靠外层 120s 兜底）。</summary>
        private sealed class Stage
        {
            public volatile string Name = "start";
            public void Set(string name) => Name = name;
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
        }

        [UnityTest]
        public IEnumerator Thunder_Status_Is_Consistent_Across_Caster_Victim_And_Observer_And_Expires()
        {
            var root = new GameObject("ThreeClientElementalRoot");
            var a = NewClient(root, "Elem_A");
            var b = NewClient(root, "Elem_B");
            var c = NewClient(root, "Elem_C");
            var stage = new Stage();

            var task = RunAsync(a, b, c, stage);
            float elapsed = 0f;
            while (!task.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            try
            {
                Assert.IsTrue(task.IsCompleted, "three-client elemental test timed out at stage " + stage.Name);
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
                }
            };
            return ctx;
        }

        private static async Task<(bool ok, string reason)> RunAsync(Ctx a, Ctx b, Ctx c, Stage stage)
        {
            var all = new[] { a, b, c };

            stage.Set("login-A");
            a.Manager.ConnectAndLogin(Host, Port);
            if (!await WaitInWorld(new[] { a }, a, 10f))
                return (false, "login-A: A failed to enter world");

            stage.Set("login-B");
            b.Manager.ConnectAndLogin(Host, Port);
            if (!await WaitInWorld(new[] { a, b }, b, 10f))
                return (false, "login-B: B failed to enter world");

            stage.Set("login-C");
            c.Manager.ConnectAndLogin(Host, Port);
            if (!await WaitInWorld(all, c, 10f))
                return (false, "login-C: C failed to enter world");

            long bId = b.Manager.LocalPlayerId;

            // 三客户端同出生点；等 C 的 AOI 看到 B（C 的 311/DamageResult 依赖观察者关系）。
            stage.Set("aoi-c-sees-b");
            if (!await WaitUntil(all, () => c.Registry.GetView(bId) != null, 15f))
                return (false, "aoi: C never saw B (bId=" + bId + ")");

            stage.Set("baseline");
            var baseline = a.Manager.WeaponInventory;
            if (baseline == null)
                return (false, "baseline: A has no weapon inventory (ElementalSwordQiV1 disabled?)");
            WeaponInstanceSummary? thunder = null;
            foreach (var weapon in baseline.Weapons)
            {
                if (weapon.ElementId == ThunderElementId)
                {
                    thunder = weapon;
                    break;
                }
            }
            if (thunder == null)
                return (false, "baseline: A has no thunder (ElementId=3) weapon");

            stage.Set("equip-request");
            a.Adapter.Send(MessageId.EquipWeaponRequest, new EquipWeaponRequest
            {
                ClientEquipSeq = 1,
                WeaponInstanceId = thunder.InstanceId,
                KnownEquipmentRevision = baseline.EquipmentRevision
            });

            stage.Set("equip-confirm");
            EquipWeaponConfirm? equipConfirm = null;
            if (!await WaitUntil(all, () =>
                {
                    equipConfirm = a.EquipConfirms.ToArray()
                        .FirstOrDefault(x => x.ClientEquipSeq == 1);
                    return equipConfirm != null;
                }, 10f))
                return (false, "equip-confirm: A received no EquipWeaponConfirm seq=1");
            if (!equipConfirm!.Accepted)
                return (false, "equip-confirm: rejected: " + equipConfirm.RejectReason);
            if (equipConfirm.ElementId != ThunderElementId)
                return (false, "equip-confirm: ElementId=" + equipConfirm.ElementId + " != " + ThunderElementId);

            stage.Set("cast-request");
            a.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = SkillId,
                TargetPlayerId = bId,
                Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f },
                ClientCastSeq = 1,
                EquipmentRevision = equipConfirm.EquipmentRevision,
                CasterLifeId = baseline.LifeId
            });

            stage.Set("cast-confirm");
            CastConfirm? castConfirm = null;
            if (!await WaitUntil(all, () =>
                {
                    castConfirm = a.Confirms.ToArray().FirstOrDefault(x => x.ClientCastSeq == 1);
                    return castConfirm != null;
                }, 10f))
                return (false, "cast-confirm: A received no CastConfirm seq=1");
            if (!castConfirm!.Accepted)
                return (false, "cast-confirm: rejected: " + castConfirm.RejectReason);
            ulong castId = castConfirm.CastId;
            if (castId == 0)
                return (false, "cast-confirm: accepted but CastId=0");

            stage.Set("cast-broadcast");
            CastBroadcast? broadcast = null;
            if (!await WaitUntil(all, () =>
                {
                    broadcast = a.Broadcasts.ToArray().FirstOrDefault(x => x.CastId == castId);
                    return broadcast != null;
                }, 10f))
                return (false, "cast-broadcast: A received no CastBroadcast for CastId " + castId);
            if (broadcast!.ElementId != ThunderElementId)
                return (false, "cast-broadcast: ElementId=" + broadcast.ElementId + " != " + ThunderElementId);

            stage.Set("damage-result");
            var aHits = new List<HitEntry>();
            var bHits = new List<HitEntry>();
            var cHits = new List<HitEntry>();
            if (!await WaitUntil(all, () =>
                {
                    aHits = HitsFor(a, castId, bId);
                    bHits = HitsFor(b, castId, bId);
                    cHits = HitsFor(c, castId, bId);
                    return aHits.Count > 0 && bHits.Count > 0 && cHits.Count > 0;
                }, 15f))
                return (false, "damage-result: caster/victim/observer did not all receive the HitEntry for B"
                    + Summarize(a, b, c, bId));

            // 同一 HitEntry 逐值一致（按 EventSeq 对齐；同一次施法可能有挥击 + 剑气两条）。
            if (aHits.Count != bHits.Count || aHits.Count != cHits.Count)
                return (false, "damage-result: hit counts differ A=" + aHits.Count
                    + " B=" + bHits.Count + " C=" + cHits.Count);
            for (int i = 0; i < aHits.Count; i++)
            {
                if (aHits[i].CastId != bHits[i].CastId || aHits[i].CastId != cHits[i].CastId)
                    return (false, "damage-result: CastId differs across clients at hit " + i);
                if (aHits[i].EventSeq != bHits[i].EventSeq || aHits[i].EventSeq != cHits[i].EventSeq)
                    return (false, "damage-result: EventSeq differs across clients at hit " + i
                        + " (A=" + aHits[i].EventSeq + " B=" + bHits[i].EventSeq
                        + " C=" + cHits[i].EventSeq + ")");
                if (aHits[i].CombatVersion != bHits[i].CombatVersion
                    || aHits[i].CombatVersion != cHits[i].CombatVersion)
                    return (false, "damage-result: CombatVersion differs across clients at hit " + i
                        + " (A=" + aHits[i].CombatVersion + " B=" + bHits[i].CombatVersion
                        + " C=" + cHits[i].CombatVersion + ")");
                if (Mathf.Abs(aHits[i].TargetRemainingHp - bHits[i].TargetRemainingHp) > 0.001f
                    || Mathf.Abs(aHits[i].TargetRemainingHp - cHits[i].TargetRemainingHp) > 0.001f)
                    return (false, "damage-result: TargetRemainingHp differs across clients at hit " + i
                        + " (A=" + aHits[i].TargetRemainingHp + " B=" + bHits[i].TargetRemainingHp
                        + " C=" + cHits[i].TargetRemainingHp + ")");
            }

            // B 本人 311：麻痹状态 + 动作锁 + 控制版本推进。
            stage.Set("status-apply-b");
            CombatStateUpdate? bApply = null;
            if (!await WaitUntil(all, () =>
                {
                    bApply = UpdatesFor(b, bId).FirstOrDefault(u => HasThunder(u.Projection));
                    return bApply != null;
                }, 15f))
                return (false, "status-apply: B received no CombatStateUpdate with EffectId="
                    + ThunderEffectId + Summarize(a, b, c, bId));
            if (bApply!.Projection.Control == null || !bApply.Projection.Control.ActionLock)
                return (false, "status-apply: B Control.ActionLock is not true" + Summarize(a, b, c, bId));
            if (bApply.Projection.Control!.ControlVersion == 0)
                return (false, "status-apply: B Control.ControlVersion == 0");

            // C 旁观收到的同一条 311，喂给 registry 后远程视图必须一致。
            stage.Set("status-apply-c");
            CombatStateUpdate? cApply = null;
            if (!await WaitUntil(all, () =>
                {
                    cApply = UpdatesFor(c, bId).FirstOrDefault(u => HasThunder(u.Projection));
                    return cApply != null;
                }, 15f))
                return (false, "status-apply: C received no CombatStateUpdate with EffectId="
                    + ThunderEffectId + Summarize(a, b, c, bId));
            if (cApply!.Projection.CombatVersion != bApply!.Projection.CombatVersion)
                return (false, "status-apply: C projection CombatVersion=" + cApply.Projection.CombatVersion
                    + " != B " + bApply.Projection.CombatVersion);

            stage.Set("status-view-c");
            c.Registry.ApplyCombatStateUpdate(cApply);
            var applyView = c.Registry.GetView(bId);
            if (applyView == null)
                return (false, "status-view: C has no view for B");
            if (!HasThunder(applyView.Statuses))
                return (false, "status-view: C view Statuses missing EffectId=" + ThunderEffectId);
            if (applyView.Control == null || !applyView.Control.ActionLock)
                return (false, "status-view: C view Control.ActionLock is not true");

            // 0.8s = 12 tick（雷麻痹持续时间）+ 余量。
            stage.Set("status-expiry-wait");
            await WaitSeconds(all, 0.9f);

            stage.Set("status-clear-b");
            CombatStateUpdate? bClear = null;
            if (!await WaitUntil(all, () =>
                {
                    bClear = UpdatesFor(b, bId).FirstOrDefault(u =>
                        u.Projection.CombatVersion > bApply.Projection.CombatVersion
                        && !HasThunder(u.Projection) && StatusCount(u.Projection) == 0);
                    return bClear != null;
                }, 10f))
                return (false, "status-clear: B received no cleared/unlocked projection after apply version "
                    + bApply.Projection.CombatVersion + Summarize(a, b, c, bId));
            if (bClear!.Projection.Control == null || bClear.Projection.Control.ActionLock)
                return (false, "status-clear: B Control.ActionLock is not false");

            stage.Set("status-clear-c");
            CombatStateUpdate? cClear = null;
            if (!await WaitUntil(all, () =>
                {
                    cClear = UpdatesFor(c, bId).FirstOrDefault(u =>
                        u.Projection.CombatVersion > cApply.Projection.CombatVersion
                        && !HasThunder(u.Projection) && StatusCount(u.Projection) == 0);
                    return cClear != null;
                }, 10f))
                return (false, "status-clear: C received no cleared/unlocked projection after apply version "
                    + cApply.Projection.CombatVersion + Summarize(a, b, c, bId));
            if (cClear!.Projection.Control == null || cClear.Projection.Control.ActionLock)
                return (false, "status-clear: C Control.ActionLock is not false");

            c.Registry.ApplyCombatStateUpdate(cClear);
            var clearedView = c.Registry.GetView(bId);
            if (clearedView == null)
                return (false, "status-clear: C view for B disappeared");
            if (StatusCount(clearedView.Statuses) != 0)
                return (false, "status-clear: C view Statuses not empty ("
                    + StatusCount(clearedView.Statuses) + ")");
            if (clearedView.Control == null || clearedView.Control.ActionLock)
                return (false, "status-clear: C view Control.ActionLock is not false");

            // 只发生一次 301 的移除，且移除版本必须大于施加版本（CombatVersion 递增）。
            stage.Set("status-removal-count");
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

        /// <summary>某实体收到的完整投影，按 CombatVersion 递增（同版本重复投递不产生额外状态迁移）。</summary>
        private static List<CombatStateUpdate> UpdatesFor(Ctx ctx, long entityId)
        {
            var updates = ctx.CombatUpdates.ToArray().Where(u => u.EntityId == entityId).ToList();
            updates.Sort((x, y) => x.Projection.CombatVersion.CompareTo(y.Projection.CombatVersion));
            return updates;
        }

        private static bool HasThunder(CombatProjection projection)
        {
            var statuses = projection.Statuses;
            if (statuses == null) return false;
            foreach (var status in statuses)
                if (status.EffectId == ThunderEffectId) return true;
            return false;
        }

        private static bool HasThunder(IList<StatusProjection>? statuses)
        {
            if (statuses == null) return false;
            foreach (var status in statuses)
                if (status.EffectId == ThunderEffectId) return true;
            return false;
        }

        private static int StatusCount(CombatProjection projection) => projection.Statuses?.Count ?? 0;

        private static int StatusCount(IList<StatusProjection>? statuses) => statuses?.Count ?? 0;

        private static int CountThunderApplications(Ctx ctx, long entityId)
        {
            bool previous = false;
            int count = 0;
            foreach (var update in UpdatesFor(ctx, entityId))
            {
                bool has = HasThunder(update.Projection);
                if (!previous && has) count++;
                previous = has;
            }
            return count;
        }

        private static int CountThunderRemovals(Ctx ctx, long entityId)
        {
            bool previous = false;
            int count = 0;
            foreach (var update in UpdatesFor(ctx, entityId))
            {
                bool has = HasThunder(update.Projection);
                if (previous && !has) count++;
                previous = has;
            }
            return count;
        }

        /// <summary>失败诊断：各端 CastBroadcast 的 CastId:ElementId 与 B 实体 311 的版本/状态/控制摘要。</summary>
        private static string Summarize(Ctx a, Ctx b, Ctx c, long bId)
        {
            var casts = a.Broadcasts.ToArray();
            string castText = casts.Length == 0
                ? "-"
                : string.Join(" ", casts.Select(x => x.CastId + ":e" + x.ElementId));
            return " | casts(A)=" + castText + " 311(B)=" + Describe(b, bId) + " 311(C)=" + Describe(c, bId);
        }

        private static string Describe(Ctx ctx, long entityId)
        {
            var parts = new List<string>();
            foreach (var update in UpdatesFor(ctx, entityId))
            {
                var control = update.Projection.Control;
                parts.Add("v" + update.Projection.CombatVersion
                    + (HasThunder(update.Projection) ? ":301" : ":-")
                    + (control != null && control.ActionLock ? ":lock" : ":free"));
            }
            return "[" + string.Join(" ", parts) + "]";
        }

        private static void Pump(Ctx[] ctxs)
        {
            for (int i = 0; i < ctxs.Length; i++)
                ctxs[i].Manager.Update(0.05f);
        }

        private static async Task<bool> WaitUntil(Ctx[] ctxs, Func<bool> condition, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                Pump(ctxs);
                if (condition()) return true;
                await Task.Delay(50);
                elapsed += 0.05f;
            }
            Pump(ctxs);
            return condition();
        }

        private static async Task WaitSeconds(Ctx[] ctxs, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                Pump(ctxs);
                await Task.Delay(50);
                elapsed += 0.05f;
            }
            Pump(ctxs);
        }

        private static async Task<bool> WaitInWorld(Ctx[] ctxs, Ctx target, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                Pump(ctxs);
                if (target.Manager.CurrentState == ClientNetworkManager.State.InWorld) return true;
                await Task.Delay(50);
                elapsed += 0.05f;
            }
            return target.Manager.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
