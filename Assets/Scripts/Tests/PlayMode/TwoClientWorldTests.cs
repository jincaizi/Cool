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
    /// P1 核心验收（spec §5-P1）：
    /// ① 双客户端登录进同 Zone，互相可见（A 的 WorldSnapshot 含 B）；
    /// ② B 断线 → 重连（全新 Login）→ 重建后 A 再次可见 B。
    /// </summary>
    public class TwoClientWorldTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public readonly List<WorldSnapshot> Snapshots = new List<WorldSnapshot>();
        }

        [UnityTest]
        public IEnumerator TwoClients_See_EachOther_And_Reconnect_Rebuilds()
        {
            var root = new GameObject("TwoClientRoot");
            var registryA = new RemoteEntityRegistry(root.transform);
            var registryB = new RemoteEntityRegistry(root.transform);
            // 拆两行：对象初始化器内不可引用尚未赋值的 ctxA（CS0165）
            var ctxA = new Ctx();
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, registryA, "Player_A");
            var ctxB = new Ctx();
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, registryB, "Player_B");

            ctxA.Adapter.MessageReceived += (id, msg) => { if (id == MessageId.WorldSnapshot) ctxA.Snapshots.Add((WorldSnapshot)msg); };
            ctxB.Adapter.MessageReceived += (id, msg) => { if (id == MessageId.WorldSnapshot) ctxB.Snapshots.Add((WorldSnapshot)msg); };

            var runTask = RunAsync(ctxA, ctxB, registryB);
            float elapsed = 0f;
            // 墙钟上限 120s：阶段 3.5 需等服务器静默检测（15s），帧数上限在高速帧率下不可靠
            while (!runTask.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "two-client test timed out");
            var (ok, reason) = runTask.Result;

            ctxA.Manager.Dispose();
            ctxB.Manager.Dispose();
            Object.Destroy(root);

            Assert.IsTrue(ok, reason);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunAsync(Ctx ctxA, Ctx ctxB, RemoteEntityRegistry registryB)
        {
            // —— 阶段 1：双客户端登录 ——
            ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctxA.Manager)) return (false, "A failed to enter world");
            ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctxB.Manager)) return (false, "B failed to enter world");

            // —— 阶段 2：A 应看到 B（每帧驱动 Update 消费主线程封送）——
            bool aSeesB = false;
            for (int i = 0; i < 40 && !aSeesB; i++) // 至多 4s
            {
                ctxA.Manager.Update(0.05f);
                ctxB.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
                foreach (var snap in ctxA.Snapshots)
                {
                    if (snap.Entities.Exists(e => e.PlayerId == ctxB.Manager.LocalPlayerId))
                    {
                        aSeesB = true;
                        break;
                    }
                }
            }
            if (!aSeesB) return (false, "A never saw B in WorldSnapshot");

            // —— 阶段 3：B 断线 → 重连 → 旧实体消失 → 新实体可见 ——
            ctxB.Manager.Dispose(); // 全量关闭（Dispose 为同步方法；socket 关闭即断线）
            ctxA.Snapshots.Clear();

            Ctx ctxC = null!;
            try
            {
                ctxC = new Ctx();
                ctxC.Manager = new ClientNetworkManager(ctxC.Adapter, registryB, "Player_B_re");
                ctxC.Adapter.MessageReceived += (id, msg) => { if (id == MessageId.WorldSnapshot) ctxC.Snapshots.Add((WorldSnapshot)msg); };
                ctxC.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxC.Manager)) return (false, "reconnect login failed");

                // —— 阶段 3.5：旧 B 必须在最近连续 N 条快照中缺席 ——
                // 快照流有间隙：无脏实体且 beacon 未到期时整 tick 不发（约 3.75Hz beacon 流），
                // 空轮不是缺席证据——只按“实际新到的快照”计数；B 存活时任何快照必含 B
                //（beacon 每 4 tick 含全部实体），故连续 5 条 B-less 快照才能证明
                // PeerSilence → 注销 → 广播停止的整链（单条 {C}-only 脏快照会假绿）
                bool oldBDisappeared = false;
                int consecutiveAbsent = 0;
                const int requiredConsecutive = 5; // 5 条连续快照（≈1.3s@3.75Hz beacon 流）
                float phase35Elapsed = 0f;
                while (!oldBDisappeared && phase35Elapsed < 25f) // 至多 25s（静默检测 15s + 广播余量）
                {
                    ctxA.Manager.Update(0.05f);
                    int countBefore = ctxA.Snapshots.Count;
                    await System.Threading.Tasks.Task.Delay(100);
                    phase35Elapsed += 0.1f;

                    // 只检查本轮新到的快照；空轮（beacon 间隙无快照）不计入缺席计数
                    bool sawOldB = false;
                    for (int i = countBefore; i < ctxA.Snapshots.Count; i++)
                    {
                        if (ctxA.Snapshots[i].Entities.Exists(e => e.PlayerId == ctxB.Manager.LocalPlayerId))
                        {
                            sawOldB = true;
                            break;
                        }
                    }
                    int newSnapshots = ctxA.Snapshots.Count - countBefore;
                    if (newSnapshots > 0)
                        consecutiveAbsent = sawOldB ? 0 : consecutiveAbsent + newSnapshots;

                    if (consecutiveAbsent >= requiredConsecutive)
                        oldBDisappeared = true;
                }
                if (!oldBDisappeared) return (false, "old B entity never disappeared from A snapshots");

                // —— 阶段 4：A 再次看到新实体 ——
                bool aSeesNewB = false;
                for (int i = 0; i < 40 && !aSeesNewB; i++)
                {
                    ctxA.Manager.Update(0.05f);
                    ctxC.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    foreach (var snap in ctxA.Snapshots)
                    {
                        if (snap.Entities.Exists(e => e.PlayerId == ctxC.Manager.LocalPlayerId))
                        {
                            aSeesNewB = true;
                            break;
                        }
                    }
                }
                if (!aSeesNewB) return (false, "A never saw reconnected B");

                return (true, "ok");
            }
            finally
            {
                ctxC?.Manager.Dispose(); // 全失败路径兜底：重连客户端不泄漏（否则服务器留幽灵会话）
            }
        }

        private static async System.Threading.Tasks.Task<bool> WaitInWorld(ClientNetworkManager manager)
        {
            float elapsed = 0f;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
            {
                manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }
            return manager.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
