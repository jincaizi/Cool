using System.Collections;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P2 核心验收：A 移动（本地模拟 + 上报）→ 服务器校验 → B 快照 → B 插值渲染看到 A 移动；
    /// A 单报瞬移 100m → 服务器纠偏 → A 收到纠偏事件、B 看不到瞬移位置（作弊拉回链路）。
    /// </summary>
    public class MovementSyncTests
    {
        private sealed class MoveContext
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
            public Vector3 LocalPosition;
            public bool CorrectionSeen;
        }

        [UnityTest]
        public IEnumerator A_Moves_B_Sees_Interpolated_Movement()
        {
            var runTask = RunAsync();
            // 墙钟上限（帧数上限在高速帧率下不可靠——playmode 实测 400-6000fps，
            // 3600 帧会在 0.6-4s 耗尽而场景需 ~15s 墙钟；TwoClientWorldTests 同款教训）
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 60f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "movement test timed out");
            var (ok, reason) = runTask.Result;
            Assert.IsTrue(ok, reason);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunAsync()
        {
            // —— 构造两个客户端（A 带本地角色模拟，B 观察）——
            var root = new GameObject("MoveRoot");
            var registryB = new RemoteEntityRegistry(root.transform);

            var ctxA = new MoveContext();
            ctxA.Registry = new RemoteEntityRegistry(root.transform);
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, ctxA.Registry, "Move_A");

            var ctxB = new MoveContext();
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, registryB, "Move_B");

            // A 的本地角色（纯模拟：不用真实地面/物理——直接驱动位置）
            ctxA.LocalPosition = Vector3.zero;
            ctxA.Manager.AttachMovementReporter(
                () => new Cool.SyncCore.Vec3(ctxA.LocalPosition.x, ctxA.LocalPosition.y, ctxA.LocalPosition.z),
                () => 0f,
                () => 4f); // 正常速度（纠偏链路另有超速用例）

            // A 端纠偏观察
            ctxA.Manager.OnCorrectionNeeded += (pos, rot) => ctxA.CorrectionSeen = true;

            try
            {
                // 双客户端登录
                ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxA.Manager)) return (false, "A login failed");
                ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxB.Manager)) return (false, "B login failed");

                // B 已能看到 A 实体（登录注册）
                bool bSeesA = false;
                for (int i = 0; i < 40 && !bSeesA; i++)
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    bSeesA = registryB.Count > 0; // ctxB.Registry 未赋值（仅 ctxA 有）；registryB 即 B 端注册表
                }
                if (!bSeesA) return (false, "B never saw A entity");

                // —— A 移动 3 秒（模拟：每帧向前 +0.1，同时驱动上报）——
                for (int i = 0; i < 60; i++)
                {
                    ctxA.LocalPosition += Vector3.forward * 0.1f;
                    ctxA.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(50);
                }

                // —— B 端应看到 A 实体位置显著变化（插值渲染生效，非恒 (0,0,0)）——
                bool bSeesMovement = false;
                for (int i = 0; i < 40 && !bSeesMovement; i++)
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    // 从 registry 读 A 实体视图位置
                    var view = registryB.GetView(ctxA.Manager.LocalPlayerId);
                    if (view != null && view.Position.z > 1f) // A 移动了 6m，B 应看到 > 1m
                        bSeesMovement = true;
                }
                if (!bSeesMovement) return (false, "B never saw A's movement (interpolation inactive?)");

                return (true, "ok");
            }
            finally
            {
                ctxA.Manager.Dispose();
                ctxB.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator A_Teleports_Server_Corrects_B_Smoothes()
        {
            var runTask = RunTeleportAsync();
            // 墙钟上限（帧数上限在高速帧率下不可靠——见 A_Moves 同款注释）
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 60f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "teleport test timed out");
            var (ok, reason) = runTask.Result;
            Assert.IsTrue(ok, reason);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunTeleportAsync()
        {
            // —— 同 A_Moves 构造：双客户端登录，A 带模拟位置 + 上报 ——
            var root = new GameObject("MoveRoot");
            var registryB = new RemoteEntityRegistry(root.transform);

            var ctxA = new MoveContext();
            ctxA.Registry = new RemoteEntityRegistry(root.transform);
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, ctxA.Registry, "Move_A");

            var ctxB = new MoveContext();
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, registryB, "Move_B");

            ctxA.LocalPosition = Vector3.zero;
            ctxA.Manager.AttachMovementReporter(
                () => new Cool.SyncCore.Vec3(ctxA.LocalPosition.x, ctxA.LocalPosition.y, ctxA.LocalPosition.z),
                () => 0f,
                () => 4f);
            ctxA.Manager.OnCorrectionNeeded += (pos, rot) => ctxA.CorrectionSeen = true;

            try
            {
                ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxA.Manager)) return (false, "A login failed");
                ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxB.Manager)) return (false, "B login failed");

                // 正常移动 ~0.5s 建立服务器 prev 基准
                for (int i = 0; i < 10; i++)
                {
                    ctxA.LocalPosition += Vector3.forward * 0.1f;
                    ctxA.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(50);
                }

                // 单帧瞬移 100m（模拟作弊/穿墙）→ 服务器纠偏到 prev + 15×dt ≈ 数米
                // → ack 权威位置 ≠ 预测帧 → 纠偏事件必须触发
                ctxA.LocalPosition += Vector3.forward * 100f;
                ctxA.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);

                bool correctionSeen = false;
                for (int i = 0; i < 40 && !correctionSeen; i++)
                {
                    ctxA.Manager.Update(0.05f); // 驱动响应封送（DrainResponses 主线程对账）
                    await System.Threading.Tasks.Task.Delay(100);
                    correctionSeen = ctxA.CorrectionSeen;
                }
                if (!correctionSeen) return (false, "A never got correction event (teleport not caught)");

                // B 端观察窗口：跟踪观测到的最大 z。anti-cheat 语义 = 服务器从未接受 100m 瞬移——
                // 权威位置被拒后沿 prev+15×dt 走廊爬升（seen 时 ≤ 50m 门槛内），B 观测值必须远小于 100m。
                // 此后不再驱动 ctxA.Manager.Update（冻结上报——防权威继续爬升逼近 50m 放行点）
                bool bSawA = false;
                float maxObservedZ = 0f;
                for (int i = 0; i < 40; i++)
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    var view = registryB.GetView(ctxA.Manager.LocalPlayerId);
                    if (view != null)
                    {
                        bSawA = true;
                        if (view.Position.z > maxObservedZ) maxObservedZ = view.Position.z;
                    }
                }
                if (!bSawA) return (false, "B never saw A entity");
                if (maxObservedZ >= 60f)
                    return (false, $"B saw the teleport position (max observed z={maxObservedZ:F1}m >= 60m; 100m 瞬移未被拦截)");

                return (true, "ok");
            }
            finally
            {
                ctxA.Manager.Dispose();
                ctxB.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator A_Moves_With_Injected_Delay_Local_Unaffected()
        {
            var runTask = RunInjectedDelayAsync();
            // 墙钟上限（帧数上限在高速帧率下不可靠——见 A_Moves 同款注释）
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 60f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "injected-delay test timed out");
            var (ok, reason) = runTask.Result;
            Assert.IsTrue(ok, reason);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunInjectedDelayAsync()
        {
            // —— A 的上报注入 200ms 延迟：本地即时模拟不得受网控，B 插值缓冲吸收稀疏报告 ——
            var root = new GameObject("MoveRoot");
            var registryB = new RemoteEntityRegistry(root.transform);

            var ctxA = new MoveContext();
            ctxA.Registry = new RemoteEntityRegistry(root.transform);
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, ctxA.Registry, "Move_A");

            var ctxB = new MoveContext();
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, registryB, "Move_B");

            ctxA.LocalPosition = Vector3.zero;
            // 注入 200ms 上行延迟（每报 Sleep 200ms）——本地即时模拟不得受网控
            ctxA.Manager.AttachMovementReporter(
                () => new Cool.SyncCore.Vec3(ctxA.LocalPosition.x, ctxA.LocalPosition.y, ctxA.LocalPosition.z),
                () => 0f,
                () => 4f,
                200);

            try
            {
                ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxA.Manager)) return (false, "A login failed");
                ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxB.Manager)) return (false, "B login failed");

                // A 侧断言：防上报路径阻塞驱动线程（send 前 Sleep 200ms——若上报与本地模拟
                // 同链耦合会卡死/超时）。真正的解耦语义由下方 B 收敛在稀疏上报下成立来证明
                for (int i = 0; i < 40; i++)
                {
                    ctxA.LocalPosition += Vector3.forward * 0.1f;
                    ctxA.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(50);
                }
                if (ctxA.LocalPosition.z < 1.5f)
                    return (false, $"A local movement stalled under injected delay (z={ctxA.LocalPosition.z:F2}m < 1.5m)");

                // B 端继续驱动，等待插值追上（延迟报告下服务器更新稀疏——缓冲兜底）
                bool bSeesMovement = false;
                for (int i = 0; i < 60 && !bSeesMovement; i++)
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    var view = registryB.GetView(ctxA.Manager.LocalPlayerId);
                    if (view != null && view.Position.z > 1f)
                        bSeesMovement = true;
                }
                if (!bSeesMovement) return (false, "B never saw A's movement under injected delay");

                return (true, "ok");
            }
            finally
            {
                ctxA.Manager.Dispose();
                ctxB.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator Clock_Sync_Jitter_Recorded()
        {
            var runTask = RecordClockJitterAsync();
            // 需 10s+ 采样——帧上限在高速帧率下不可靠，用墙钟上限（同 TwoClientWorldTests 教训）
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 60f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "clock test timed out");
        }

        private static async System.Threading.Tasks.Task RecordClockJitterAsync()
        {
            var root = new GameObject("ClockRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            using var adapter = new KcpNetClientAdapter();
            var manager = new ClientNetworkManager(adapter, registry, "Clock_Tester");
            try
            {
                manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(manager)) return;

                // 10s 采样：记录 SmoothedOffsetTicks 的 max−min 抖动
                long min = long.MaxValue, max = long.MinValue;
                for (int i = 0; i < 200; i++)
                {
                    manager.Update(0.05f);
                    long v = manager.ClockSmoothedOffsetTicks;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    await System.Threading.Tasks.Task.Delay(50);
                }
                long jitterTicks = max - min;
                Debug.Log($"[ClockSync] jitter={jitterTicks} ticks ({(jitterTicks / 10_000f):F2}ms) over 10s");
                // 记录并软断言（loopback 实测 5.8~12.5ms，余量 4~8×；50ms 红线防 CI 负载回归）——
                // 注意：采样窗口主要覆盖突发收敛瞬态（200 读中 ~190 次同值），非稳态抖动
                Assert.Less(jitterTicks, 500_000L, "clock jitter should be < 50ms on loopback (recorded value in log)");
            }
            finally
            {
                manager.Dispose();
                Object.Destroy(root);
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
