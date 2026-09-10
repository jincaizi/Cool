using System.Collections;
using System.Collections.Generic;
using Cool.Protocol;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P4 核心验收（spec §5-P4）：
    /// ① 双客户端伤害数字一致且以服务器为准（A 施法 → 双方 DamageResult 同值，B 余血 = 100 − 伤害）；
    /// ② 伪造越射程/未冷却技能被服务器拒绝（客户端无任何战斗消息到达；
    ///    服务器审计日志另留档 production/verify/cast-audit.log，见 Step 4）。
    /// 双客户端同出生点 (0,0,0)。施法用显式 TargetPlayerId（服务端显式目标按 id 精确匹配 +
    /// 距离校验，防作弊不变）——类级连跑时上一测试的 ghost 实体（15s 静默窗口）会干扰
    /// 自动目标选择（test-order interference）。
    /// </summary>
    public class CombatSyncTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public readonly List<CastBroadcast> Casts = new List<CastBroadcast>();
            public readonly List<DamageResult> Damages = new List<DamageResult>();
        }

        [UnityTest]
        public IEnumerator TwoClients_Damage_Numbers_Match_Server_Authority()
        {
            var root = new GameObject("CombatSyncRoot");
            var ctxA = new Ctx();
            var ctxB = new Ctx();
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, new RemoteEntityRegistry(root.transform), "Caster_A");
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, new RemoteEntityRegistry(root.transform), "Target_B");
            Capture(ctxA);
            Capture(ctxB);

            var runTask = RunCastScenario(ctxA, ctxB, cheat: false);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 90f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "combat sync test timed out");
            var (ok, reason) = runTask.Result;

            ctxA.Manager.Dispose();
            ctxB.Manager.Dispose();
            Object.Destroy(root);
            Assert.IsTrue(ok, reason);
        }

        [UnityTest]
        public IEnumerator Cheat_Casts_Are_Rejected_Server_Side()
        {
            var root = new GameObject("CombatCheatRoot");
            var ctxA = new Ctx();
            var ctxB = new Ctx();
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, new RemoteEntityRegistry(root.transform), "Caster_A");
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, new RemoteEntityRegistry(root.transform), "Target_B");
            Capture(ctxA);
            Capture(ctxB);

            var runTask = RunCastScenario(ctxA, ctxB, cheat: true);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 90f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "combat cheat test timed out");
            var (ok, reason) = runTask.Result;

            ctxA.Manager.Dispose();
            ctxB.Manager.Dispose();
            Object.Destroy(root);
            Assert.IsTrue(ok, reason);
        }

        private static void Capture(Ctx ctx)
        {
            ctx.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.CastBroadcast) ctx.Casts.Add((CastBroadcast)msg);
                if (id == MessageId.DamageResult) ctx.Damages.Add((DamageResult)msg);
            };
        }

        private static HitShape SkillQShape(float range = 3.5f) => new HitShape
        {
            ShapeType = 0, Range = range, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f
        };

        private static async System.Threading.Tasks.Task<(bool, string)> RunCastScenario(
            Ctx ctxA, Ctx ctxB, bool cheat)
        {
            ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctxA.Manager)) return (false, "A failed to enter world");
            ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctxB.Manager)) return (false, "B failed to enter world");

            long targetId = ctxB.Manager.LocalPlayerId;

            if (!cheat)
            {
                // —— 正常施法：Q（Single 3.5m，显式目标 B）——
                ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
                {
                    SkillId = 20001, TargetPlayerId = targetId, Shape = SkillQShape()
                });

                // 断言窗口 20s：A 收到 CastBroadcast，双方收到 DamageResult
                for (int i = 0; i < 100 && (ctxA.Damages.Count == 0 || ctxB.Damages.Count == 0); i++)
                {
                    ctxA.Manager.Update(0.05f);
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                }

                if (ctxA.Casts.Count == 0) return (false, "A never received CastBroadcast");
                if (ctxA.Damages.Count == 0) return (false, "A never received DamageResult");
                if (ctxB.Damages.Count == 0) return (false, "B never received DamageResult");

                var hitA = FindHit(ctxA.Damages, targetId);
                var hitB = FindHit(ctxB.Damages, targetId);
                if (hitA == null || hitB == null) return (false, $"B({targetId}) missing from damage results");
                if (System.Math.Abs(hitA.Damage - hitB.Damage) > 0.001f)
                    return (false, $"damage mismatch: A={hitA.Damage} B={hitB.Damage}");
                if (System.Math.Abs(hitA.TargetRemainingHp - hitB.TargetRemainingHp) > 0.001f)
                    return (false, $"remaining hp mismatch: A={hitA.TargetRemainingHp} B={hitB.TargetRemainingHp}");
                if (hitA.Damage != 120f && hitA.Damage != 180f)
                    return (false, $"damage {hitA.Damage} outside server roll domain {{120, 180}}");
                if (System.Math.Abs(hitA.TargetRemainingHp - (100f - hitA.Damage)) > 0.001f)
                    return (false, $"remaining hp {hitA.TargetRemainingHp} != 100 - damage");
                return (true, $"ok: server damage {hitA.Damage} consistent on both clients (B remaining {hitA.TargetRemainingHp})");
            }

            // —— 作弊 1：越射程（range 999）→ 服务器拒绝，双方 5s 内无任何战斗消息 ——
            ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001, TargetPlayerId = 0, Shape = SkillQShape(range: 999f)
            });
            for (int i = 0; i < 25; i++) // 5s
            {
                ctxA.Manager.Update(0.05f);
                ctxB.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }
            if (ctxA.Casts.Count > 0 || ctxA.Damages.Count > 0 || ctxB.Casts.Count > 0 || ctxB.Damages.Count > 0)
                return (false, "cheat(range=999) produced combat messages — server did not reject");

            // —— 作弊 2：连发两次 Q（冷却 6s 内）→ 仅第一次生效 ——
            ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001, TargetPlayerId = targetId, Shape = SkillQShape()
            });
            ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001, TargetPlayerId = targetId, Shape = SkillQShape()
            });
            for (int i = 0; i < 100; i++) // 20s
            {
                ctxA.Manager.Update(0.05f);
                ctxB.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }
            if (ctxA.Casts.Count == 0) return (false, "legit cast produced no CastBroadcast");
            if (ctxA.Casts.Count != 1)
                return (false, $"expected exactly 1 CastBroadcast after double-cast, got {ctxA.Casts.Count}");
            if (ctxA.Damages.Count != 1)
                return (false, $"expected exactly 1 DamageResult after double-cast, got {ctxA.Damages.Count}");
            if (ctxB.Damages.Count != 1)
                return (false, $"B should receive exactly 1 DamageResult, got {ctxB.Damages.Count}");
            return (true, $"ok: both cheats rejected (range + cooldown) — {ctxA.Damages.Count} damage delivered");
        }

        private static HitEntry? FindHit(List<DamageResult> results, long targetId)
        {
            foreach (var r in results)
                foreach (var h in r.Hits)
                    if (h.TargetPlayerId == targetId) return h;
            return null;
        }

        private static async System.Threading.Tasks.Task<bool> WaitInWorld(ClientNetworkManager m)
        {
            float elapsed = 0f;
            while (m.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
            {
                m.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }
            return m.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
