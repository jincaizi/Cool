using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// HUD 联网读数的文本格式（`FormatNetStats` 是 internal，测试程序集有 InternalsVisibleTo）。
    /// 数字必须能被断言：延迟有样本带 ms，无样本显示 --。
    /// </summary>
    public class ClientNetworkHostNetStatsTests
    {
        [Test]
        public void test_net_stats_format_includes_fps_and_latency()
        {
            Assert.AreEqual("FPS 60  延迟 23ms", ClientNetworkHost.FormatNetStats(60, 23));
        }

        [Test]
        public void test_net_stats_format_without_sample_shows_dash()
        {
            Assert.AreEqual("FPS 60  延迟 --", ClientNetworkHost.FormatNetStats(60, null));
        }

        [Test]
        public void test_net_stats_visibility_guard_covers_each_condition()
        {
            Assert.IsTrue(ClientNetworkHost.ShouldShowNetStats(true, true, false),
                "联网进世界、且不在回菜单途中时必须显示");

            Assert.IsFalse(ClientNetworkHost.ShouldShowNetStats(true, true, true),
                "断线回菜单途中不得显示——c5e2d04 修的就是这个分支");
            Assert.IsFalse(ClientNetworkHost.ShouldShowNetStats(false, true, false),
                "单机（无 manager）不显示");
            Assert.IsFalse(ClientNetworkHost.ShouldShowNetStats(true, false, false),
                "未进世界不显示");
        }
    }
}
