using System.Reflection;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 2026-11-24 回归：`OnPlayerSpawned` 先设置 `_localEntry`，`Sys3CEntry.Start` 才创建
    /// `SkillCoordinator`。这个窗口里 `TickEchoRequest` 必须安全跳过——原实现直接解引用
    /// `_localEntry.SkillCoordinator`，在角色重生成时每帧抛 NullReferenceException。
    /// </summary>
    public class ClientNetworkHostEchoTickTests
    {
        [Test]
        public void test_echo_tick_with_unstarted_local_entry_does_not_throw()
        {
            var hostGo = new GameObject("Host");
            var entryGo = new GameObject("Entry");
            try
            {
                var host = hostGo.AddComponent<ClientNetworkHost>();
                var entry = entryGo.AddComponent<Sys3CEntry>();

                typeof(ClientNetworkHost)
                    .GetField("_localEntry", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(host, entry);

                Assert.DoesNotThrow(() =>
                {
                    typeof(ClientNetworkHost)
                        .GetMethod("TickEchoRequest", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(host, null);
                }, "未 Start 的本地角色（SkillCoordinator 尚未创建）不得抛 NRE");
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
                Object.DestroyImmediate(entryGo);
            }
        }
    }
}
