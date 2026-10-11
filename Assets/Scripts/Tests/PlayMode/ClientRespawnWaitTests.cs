using System.Collections;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// N5：死后**不许自己回选人界面**——服务端 8 秒才复活，客户端 3 秒就走的话，
    /// `RespawnNotice` 到达时人已经不在世界里了，服务端复活永远看不见。
    ///
    /// 判据（简报 Step 1 第三条）：死后 5 秒仍在世界里，既没回菜单、也没自行站起来。
    /// 5 秒是**故意取在 3（旧计时器）与 8（服务端复活延迟）之间**的：旧行为在这一刻已经
    /// 把人送回选人界面，新行为还在等。
    ///
    /// 需要本地服务器（`dotnet run --project Server/Server3C.csproj -- 8000`）：宿主只在
    /// 进世界 + 装配本地角色之后才挂上死亡回调，没有服务器就无从谈起"死后"。
    /// </summary>
    public class ClientRespawnWaitTests
    {
        /// <summary>致死伤害（T12/PlayerDeathRespawnTests 同值：> 100 且远小于 9999 的秒杀旁路）。</summary>
        private const float LethalDamage = 150f;

        /// <summary>场景根对象查找（与 MobileTouchInputTests 同款）。</summary>
        private static GameObject FindRoot(string name)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        [UnityTest]
        public IEnumerator Death_Does_Not_Send_The_Player_Home_Before_The_Server_Revives()
        {
            // 3 秒回选人 vs 服务端 8 秒复活 ⇒ 不解除的话复活永远看不见。
            var previousMode = NetworkModeService.Load();
            var previousHost = NetworkModeService.LoadHost();
            var previousPort = NetworkModeService.LoadPort();
            var previousUser = NetworkModeService.LoadUsername();
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;   // 场景自身的网络/表现日志不参与判定

            ClientNetworkHost host = null;
            try
            {
                // 联网 + 显式端点：宿主 Start 读 PlayerPrefs 决定连不连、连哪儿
                NetworkModeService.Save(NetworkMode.Online);
                NetworkModeService.SaveConnection("127.0.0.1", "8000", "RespawnWait");

                SceneManager.LoadScene("DemoDay");
                yield return null;
                yield return null;

                Sys3CEntry player = null;
                for (int i = 0; i < 600 && player == null; i++)
                {
                    player = Object.FindObjectOfType<Sys3CEntry>();
                    if (player == null) yield return null;
                }
                Assert.IsNotNull(player, "DemoDay 应在场景加载时生成玩家（PlayerSpawner）");

                // 宿主：用**场景里本来就有的那个**（根对象 `Net`，随场景加载/销毁），
                // 不另起一个——另起会变成双宿主/双会话，行为无法归因到任一个，
                // 而线上跑的正是这一份场景接线。
                var hostRoot = FindRoot("Net");
                Assert.IsNotNull(hostRoot, "DemoDay 应有一个常驻网络宿主 `Net`（场景根对象）");
                host = hostRoot.GetComponent<ClientNetworkHost>();
                Assert.IsNotNull(host, "`Net` 上应挂着 ClientNetworkHost");

                // 进世界 + 装配本地角色。Reporter 是"装配完成"的公开信号：
                // 死亡回调与它同一个装配块（ClientNetworkHost.TryAttachLocalCharacter），
                // 没装上就是"死亡根本不会通知宿主"，这条用例也就无从谈起。
                for (int i = 0; i < 1800; i++)
                {
                    if (host.IsInWorld && host.Manager != null && host.Manager.Reporter != null) break;
                    yield return null;
                }
                Assert.IsTrue(host.IsInWorld, "30 秒内未进世界——本用例需要 127.0.0.1:8000 上的本地服务器");
                Assert.IsNotNull(host.Manager, "宿主应已建好 ClientNetworkManager");
                Assert.IsNotNull(host.Manager.Reporter,
                    "宿主未装配本地角色——死亡回调没挂上，5 秒判据无从谈起");

                // 打死：走服务端权威余血入口（与联网实战同一条路），客户端只镜像服务端的死
                Assert.Greater(player.CurrentHp, 0f, "前置失败：动手前玩家应是活的");
                player.ApplyServerDamage(LethalDamage, Vector3.back, 0f);
                yield return null;
                Assert.IsTrue(player.IsDead, "前置失败：服务端权威余血归零后玩家应已阵亡");

                // Act：等 5 秒（> 旧的 3 秒回选人，< 服务端 8 秒复活）
                yield return new WaitForSeconds(5f);

                // Assert
                Assert.AreEqual("DemoDay", SceneManager.GetActiveScene().name,
                    "死后 5 秒必须仍在世界场景——3 秒回选人会让服务端的 RespawnNotice 到达时"
                    + "客户端已经离开世界，服务端复活永远看不见");
                Assert.IsTrue(player != null && player.IsDead,
                    "客户端不得自行站起来（D-5：复活只能由服务端的 RespawnNotice 驱动）");

                // 收尾：本用例与 MobileTouchInputTests 是 Tests/ 下仅有的两个 LoadScene 用例，
                // 不卸载就会把共享 play 会话留在 DemoDay 上，给其后所有用例制造顺序依赖。
                // 宿主是场景里的对象——断开即可，随卸载一起销毁。
                host.Disconnect();
                SceneManager.SetActiveScene(SceneManager.CreateScene("N5CleanupScene"));
                var unload = SceneManager.UnloadSceneAsync("DemoDay");
                while (unload != null && !unload.isDone) yield return null;
            }
            finally
            {
                // 断言失败提前退出时的兜底：断开会话（幂等），场景留给下一个用例自己处理
                if (host != null) host.Disconnect();
                NetworkModeService.Save(previousMode);
                NetworkModeService.SaveConnection(previousHost, previousPort.ToString(), previousUser);
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }
    }
}
