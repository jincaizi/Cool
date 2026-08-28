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

            var runTask = RunAsync(ctxA, ctxB, registryB, root);
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

        private static async System.Threading.Tasks.Task<(bool, string)> RunAsync(Ctx ctxA, Ctx ctxB, RemoteEntityRegistry registryB, GameObject root)
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

            // —— 阶段 3：B 断线 → 重连（全新 Login）→ A 再次看到新实体 ——
            ctxB.Manager.Dispose(); // 全量关闭（Dispose 为同步方法；socket 关闭即断线）

            // A 端应收到 PlayerLeaveRoom 并移除 B 实体（主线程封送后 Remove 生效）
            ctxA.Snapshots.Clear();
            var ctxC = new Ctx();
            ctxC.Manager = new ClientNetworkManager(ctxC.Adapter, registryB, "Player_B_re");
            ctxC.Adapter.MessageReceived += (id, msg) => { if (id == MessageId.WorldSnapshot) ctxC.Snapshots.Add((WorldSnapshot)msg); };
            ctxC.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctxC.Manager)) return (false, "reconnect login failed");

            try
            {
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

                // —— 阶段 3.5：A 应观察到旧 B 实体被移除（服务器 15s 静默检测 → PlayerLeaveRoom 封送 → 快照不再含旧 id）——
                long oldBId = ctxB.Manager.LocalPlayerId;
                bool aSeesOldBRemoved = false;
                for (int i = 0; i < 250 && !aSeesOldBRemoved; i++) // 至多 25s（静默检测 15s + 广播余量）
                {
                    ctxA.Manager.Update(0.05f);
                    ctxC.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    foreach (var snap in ctxA.Snapshots)
                    {
                        if (!snap.Entities.Exists(e => e.PlayerId == oldBId))
                        {
                            aSeesOldBRemoved = true;
                            break;
                        }
                    }
                }
                if (!aSeesOldBRemoved) return (false, "A never saw old B removed after disconnect");

                return (true, "ok");
            }
            finally
            {
                ctxC.Manager.Dispose(); // 重连客户端必须收尾关闭，否则服务器留幽灵会话
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
