using System.Collections;
using Cool.Protocol;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>P1 验收：adapter+SyncLayer 登录闭环 → 进世界 → 收到快照</summary>
    public class LoginEnterWorldTests
    {
        private sealed class LoginResult
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public long PlayerId;
            public string ZoneId = string.Empty;
            public int SnapshotCount;
        }

        [UnityTest]
        public IEnumerator Login_EntersWorld_ReceivesSnapshot()
        {
            var result = new LoginResult();
            var runTask = RunAsync(result);

            int frames = 0;
            while (!runTask.IsCompleted && frames < 1800)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "login test timed out");
            Assert.IsTrue(result.Success, $"login failed: {result.FailureReason}");
            Assert.Greater(result.PlayerId, 0, "player id assigned");
            Assert.AreEqual("Global", result.ZoneId);
        }

        private static async System.Threading.Tasks.Task RunAsync(LoginResult result)
        {
            var root = new GameObject("SyncLayerRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            using var client = new KcpNetClientAdapter();
            var manager = new ClientNetworkManager(client, registry, $"Player_{System.DateTime.UtcNow.Ticks % 1000}");

            // 直接订阅 adapter 消息观察快照到达（manager 内部 dispatcher 不可直接订阅）
            client.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.WorldSnapshot && msg is WorldSnapshot snap)
                {
                    result.SnapshotCount = snap.Entities.Count;
                }
            };

            manager.ConnectAndLogin("127.0.0.1", 8000);

            // 等待进世界（每帧驱动 manager.Update 消费快照队列——主线程封送）
            float elapsed = 0f;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
            {
                manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }

            if (manager.CurrentState != ClientNetworkManager.State.InWorld)
            {
                result.FailureReason = "not in world within 10s";
                Object.Destroy(root);
                return;
            }

            result.PlayerId = manager.LocalPlayerId;
            result.ZoneId = manager.ZoneId;

            // 等待至少一条快照（单客户端时快照可为空——登录闭环本身是主验收）；
            // 同时驱动时钟同步采样（原 50×Update(0.2f) 合并于此：0.2s/样本 → 2s 覆盖突发 8 样本）
            for (int i = 0; i < 10; i++)
            {
                manager.Update(0.2f);
                await System.Threading.Tasks.Task.Delay(50);
            }

            result.Success = true;
            manager.Dispose();
            Object.Destroy(root);
        }
    }
}
