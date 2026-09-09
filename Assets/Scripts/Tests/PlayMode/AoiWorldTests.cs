using System.Collections;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P3 核心验收：bot 走近（客户端生成视图）→ bot 走远（缺席超时销毁）；
    /// 服务器 --bots 模式启动（100 bot ±500m 游走）。
    /// 统计口径见 RunAsync 注释——非时序脆弱断言。
    /// </summary>
    public class AoiWorldTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
        }

        [UnityTest]
        public IEnumerator Bots_Enter_And_Leave_Sight_Over_Time()
        {
            var runTask = RunAsync();
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "aoi test timed out");
            var (ok, reason) = runTask.Result;
            Assert.IsTrue(ok, reason);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunAsync()
        {
            var root = new GameObject("AoiRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            var ctx = new Ctx();
            ctx.Registry = registry;
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, registry, "Aoi_Observer");
            try
            {
                ctx.Manager.ConnectAndLogin("127.0.0.1", 8000);
                float elapsed = 0f;
                while (ctx.Manager.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
                {
                    ctx.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(50);
                    elapsed += 0.05f;
                }
                if (ctx.Manager.CurrentState != ClientNetworkManager.State.InWorld)
                    return (false, "login failed");

                // —— 观察 60s：bot 群在 ±500m 世界 8m/s 游走，静止观察者视野 80m ——
                // 统计口径（AOI 整链证据，非时序脆弱断言）：
                //   ① seenIds ≥ 2：生成路径工作（bot 走近被创建视图）
                //   ② 观察到 count 跌破峰值：销毁路径工作（bot 走远→停流→缺席超时删除）
                // 密度预期：100 bot / 1km² → 80m 视野期望 ~2 个；60s 位移 480m 必反复扫过视野。
                var seenIds = new System.Collections.Generic.HashSet<long>();
                int peakCount = 0;
                int dropCount = 0;
                for (int i = 0; i < 300; i++) // 300 × 200ms = 60s
                {
                    ctx.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(200);
                    int count = ctx.Registry.Count;
                    if (count > peakCount)
                    {
                        peakCount = count;
                    }
                    else if (count < peakCount)
                    {
                        dropCount++; // 视野内实体减少（离开→超时销毁）
                        peakCount = count;
                    }
                    foreach (var id in ctx.Registry.PlayerIds)
                        seenIds.Add(id);
                }
                if (seenIds.Count < 2)
                    return (false, $"observer saw only {seenIds.Count} distinct bot(s) in 60s (AOI spawn not working?)");
                if (dropCount == 0)
                    return (false, $"registry count never dropped below peak {peakCount} in 60s (AOI despawn/expiry not working?)");

                return (true, $"ok: saw {seenIds.Count} distinct bots, peak {peakCount}, {dropCount} drops");
            }
            finally
            {
                ctx.Manager.Dispose();
                Object.Destroy(root);
            }
        }
    }
}
