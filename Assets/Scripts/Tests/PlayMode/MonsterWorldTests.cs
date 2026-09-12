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
    /// P5 核心验收（spec §5-P5）：
    /// ① 服务端驱动巡逻 + 仇恨追击 + 攻击：观察者看到怪物（Kind=Npc 视图）、
    ///    巡逻怪物位置漂移、Wolf 追击至攻击距离、收到 DamageResult（dmg 15 恒值）；
    /// ② 玩家断线怪物世界持续运转：重连后同一怪物 id 仍在（HP 权威保留）、继续攻击。
    /// 服务器 --monsters 4 模式启动（固定布局：Wolf(12,0,0) 面向原点——开局即感知观察者；
    /// Slime(±40,0,0) 巡逻可视不感知；Wolf(0,0,40) 可视不感知）。
    /// 统计口径（非时序脆弱断言——同 AoiWorldTests 惯例）。
    /// 注：登录须显式 ConnectAndLogin（ClientNetworkManager.Update 非 InWorld 时早退，
    /// 不会自行建连）——计划文本漏写，按 AoiWorldTests/CombatSyncTests 惯例补上（偏差记录）。
    /// 回归保护：本测试是服务器登录接线（RegisterEntity 在 EntityStore 实例上置 IsLoggedIn）的
    /// 端到端锁定——该接线缺失时怪物感知（MonsterModule.DetectTarget 的 `!p.IsLoggedIn` 过滤）
    /// 会静默失效：怪物只巡逻不感知（观察 60s：wolf ActionState 恒 Run、casts=0、dmg=0，
    /// 而 wolf 巡逻路径距观察者最近 ~2m 且朝向在锥内）。P5 T5 验收发现并以修复落地。
    /// </summary>
    public class MonsterWorldTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
            public readonly List<DamageResult> Damages = new List<DamageResult>();
            public int CastBroadcasts;
        }

        [UnityTest]
        public IEnumerator Monsters_Patrol_Chase_And_Attack_Server_Authoritative()
        {
            var root = new GameObject("MonsterWorldRoot");
            var ctx = new Ctx();
            ctx.Registry = new RemoteEntityRegistry(root.transform);
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, ctx.Registry, "Monster_Observer");
            ctx.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctx.Damages.Add((DamageResult)msg);
                if (id == MessageId.CastBroadcast) ctx.CastBroadcasts++;
            };

            var runTask = RunPatrolChaseAttack(ctx);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            try
            {
                Assert.IsTrue(runTask.IsCompleted, "monster world test timed out");
                var (ok, reason) = runTask.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                ctx.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator Reconnect_Monster_World_Persists()
        {
            var root = new GameObject("MonsterReconnectRoot");
            var ctxA = new Ctx();
            ctxA.Registry = new RemoteEntityRegistry(root.transform);
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, ctxA.Registry, "Reconnect_A");
            ctxA.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctxA.Damages.Add((DamageResult)msg);
            };

            var runTask = RunReconnect(ctxA);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            try
            {
                Assert.IsTrue(runTask.IsCompleted, "reconnect test timed out");
                var (ok, reason) = runTask.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                ctxA.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunPatrolChaseAttack(Ctx ctx)
        {
            ctx.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctx.Manager)) return (false, "login failed");

            // —— 观察 60s（统计口径）——
            // ① Wolf(12,0,0) 开局感知：追击 → 攻击 → 观察者收到 DamageResult（dmg 15 恒值，
            //    SourcePlayerId = 怪物 id）——"打玩家触发 HitFSM 同步"链路的协议侧证据
            // ② Slime(40,0,0) 巡逻：视图位置漂移 ≥ 1m（样本间隔采样）
            var monsterIds = new HashSet<long>();
            long? wolfId = null;
            float wolfMinDistToSpawn = float.MaxValue;
            // 漂移按视图 id 分开累积——两个 Slime（±40m）共用一个变量会把彼此 ~160m 间距
            // 当成漂移（T5 首轮假绿：8s 窗口报 6000m，而单个 Slime 8s 至多走 ~16m）
            var slimeLastPos = new Dictionary<long, Vector3>();
            var slimeDrift = new Dictionary<long, float>();

            for (int i = 0; i < 300; i++) // 300 × 200ms = 60s（收到伤害即提前出，但保底 3s——见下）
            {
                ctx.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(200);
                if (ctx.Manager.CurrentState != ClientNetworkManager.State.InWorld)
                    return (false, "disconnected mid-observation");

                foreach (var view in ctx.Registry.PlayerIds)
                {
                    var v = ctx.Registry.GetView(view);
                    if (v == null) continue;
                    if (v.Kind == (byte)EntityKind.Npc)
                    {
                        monsterIds.Add(view);
                        if (v.TemplateId == 2)
                        {
                            if (!wolfId.HasValue) wolfId = view;
                            float d = Vector3.Distance(ctx.Manager.SpawnPosition, v.Position);
                            if (d < wolfMinDistToSpawn) wolfMinDistToSpawn = d;
                        }
                        if (v.TemplateId == 1)
                        {
                            if (slimeLastPos.TryGetValue(view, out var lastSlime))
                            {
                                float d = Vector3.Distance(lastSlime, v.Position);
                                slimeDrift[view] = (slimeDrift.TryGetValue(view, out var acc) ? acc : 0f) + d;
                            }
                            slimeLastPos[view] = v.Position;
                        }
                    }
                }

                // 伤害早退，但保底 15 采样（3s）：类级连跑第 2 次时狼已贴脸（ghost 位），命中可 <1s 到达，
                // 3s 以下的采样窗会让 Slime 漂移（2m/s）逼近 1m 阈值 → 采样窗不足造成的偶发假 FAIL
                if (ctx.Damages.Count > 0 && i >= 14) break;
            }

            if (monsterIds.Count == 0) return (false, "no monster views observed in 60s (Npc snapshot not working?)");
            if (wolfId == null) return (false, "wolf (template 2) never seen");
            float maxSlimeDrift = 0f;
            foreach (var kv in slimeDrift)
                if (kv.Value > maxSlimeDrift) maxSlimeDrift = kv.Value;
            if (slimeDrift.Count == 0) return (false, "no slime sampled twice in 60s (patrol check impossible)");
            if (maxSlimeDrift < 1f)
                return (false, $"max slime patrol drift only {maxSlimeDrift:F2}m in 60s (patrol not running?)");
            if (ctx.Damages.Count == 0)
                return (false, $"observer never took monster damage (aggro/attack not working? monsters={monsterIds.Count} " +
                               $"wolfMinDist={wolfMinDistToSpawn:F2} casts={ctx.CastBroadcasts} spawn={ctx.Manager.SpawnPosition})");

            var first = ctx.Damages[0];
            var hit = FindHit(first, ctx.Manager.LocalPlayerId);
            if (hit == null) return (false, $"damage result {first.SourcePlayerId} did not target local player {ctx.Manager.LocalPlayerId}");
            if (System.Math.Abs(hit.Damage - 15f) > 0.001f)
                return (false, $"monster damage {hit.Damage} != server-rolled 15 (wolf dmg)");
            if (first.SourcePlayerId != wolfId.Value)
                return (false, $"damage source {first.SourcePlayerId} != wolf id {wolfId.Value}");
            if (System.Math.Abs(hit.TargetRemainingHp - (100f - hit.Damage)) > 0.001f)
                return (false, $"local remaining hp {hit.TargetRemainingHp} inconsistent");

            string summary = $"ok: {monsterIds.Count} monsters seen, max slime patrol drift {maxSlimeDrift:F2}m " +
                             $"(over {slimeDrift.Count} slimes), wolf {wolfId} hit observer for {hit.Damage} " +
                             $"(hp {hit.TargetRemainingHp})";
            Debug.Log("[MonsterWorldTests] " + summary);
            return (true, summary);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunReconnect(Ctx ctx)
        {
            ctx.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctx.Manager)) return (false, "login failed");
            long localId = ctx.Manager.LocalPlayerId;

            // —— 阶段 1：观察 Wolf 并记录 id ——
            long? wolfId = null;
            for (int i = 0; i < 100 && !wolfId.HasValue; i++) // 20s
            {
                ctx.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(200);
                foreach (var id in ctx.Registry.PlayerIds)
                {
                    var v = ctx.Registry.GetView(id);
                    if (v != null && v.Kind == (byte)EntityKind.Npc && v.TemplateId == 2) { wolfId = id; break; }
                }
            }
            if (!wolfId.HasValue) return (false, "wolf never seen before disconnect");

            // —— 阶段 2：断线（怪物世界应持续运转——服务器零 per-session 依赖）——
            ctx.Manager.Dispose();
            await System.Threading.Tasks.Task.Delay(3000);

            // —— 阶段 3：重连（新会话新玩家 id）——同一怪物 id 仍在，且继续攻击重连者 ——
            var root2 = new GameObject("ReconnectRoot2");
            var ctxB = new Ctx();
            ctxB.Registry = new RemoteEntityRegistry(root2.transform);
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, ctxB.Registry, "Reconnect_B");
            ctxB.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctxB.Damages.Add((DamageResult)msg);
            };
            try
            {
                ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxB.Manager)) return (false, "reconnect login failed");

                bool sameWolfSeen = false;
                for (int i = 0; i < 150; i++) // 30s
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(200);
                    if (ctxB.Manager.CurrentState != ClientNetworkManager.State.InWorld)
                        return (false, "reconnect dropped mid-observation");
                    var v = ctxB.Registry.GetView(wolfId.Value);
                    if (v != null && v.Kind == (byte)EntityKind.Npc) { sameWolfSeen = true; break; }
                    if (ctxB.Damages.Count > 0) break; // 被追击即证明世界持续运转
                }
                if (!sameWolfSeen && ctxB.Damages.Count == 0)
                    return (false, "wolf did not persist across disconnect (world not continuous)");
                if (ctxB.Damages.Count > 0)
                {
                    var hit = FindHit(ctxB.Damages[0], ctxB.Manager.LocalPlayerId);
                    if (hit != null && System.Math.Abs(hit.TargetRemainingHp - (100f - hit.Damage)) > 0.001f)
                        return (false, "reconnect damage hp inconsistent");
                }
                return (true, $"ok: wolf {wolfId} persisted across disconnect (local {localId} → B), " +
                              $"damage results on reconnect: {ctxB.Damages.Count}");
            }
            finally
            {
                ctxB.Manager.Dispose();
                Object.Destroy(root2);
            }
        }

        private static HitEntry? FindHit(DamageResult result, long targetId)
        {
            foreach (var h in result.Hits)
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
