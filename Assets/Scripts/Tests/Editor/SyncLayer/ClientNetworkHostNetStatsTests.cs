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
    }
}
