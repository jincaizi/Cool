using Hotfix.GameSystems.Sys3C.Quality;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.Quality
{
    /// <summary>T0：移动端质量档判定 + 档位参数的预算守护。</summary>
    public class MobileQualityBootstrapTests
    {
        [Test]
        public void Mobile_Device_Switches_To_The_Mobile_Level()
            => Assert.AreEqual(MobileQualityBootstrap.MobileQualityLevel,
                MobileQualityBootstrap.ResolveLevel(null, isMobileDevice: true));

        [Test]
        public void Desktop_Device_Leaves_The_Level_Alone()
            => Assert.IsNull(MobileQualityBootstrap.ResolveLevel(null, isMobileDevice: false));

        [Test]
        public void Url_Level_Wins_On_Both_Platforms()
        {
            Assert.AreEqual(5, MobileQualityBootstrap.ResolveLevel("?quality=5", isMobileDevice: true));
            Assert.AreEqual(5, MobileQualityBootstrap.ResolveLevel("?quality=5", isMobileDevice: false));
        }

        [Test]
        public void Url_Desktop_Forces_No_Change()
            => Assert.IsNull(MobileQualityBootstrap.ResolveLevel("?quality=desktop", isMobileDevice: true));

        /// <summary>
        /// `?quality=mobile` 是人工实测（计划任务 10 / webgl-mobile-acceptance）实际使用的 URL。
        /// 别名必须在**自动判定为 false** 时也强制生效——判定失准的设备（iPad 桌面 UA：
        /// `isMobilePlatform` 与 `deviceType` 两个判据都失效）否则会静默跑 Ultra 档，
        /// 实测拿到的是别的瓶颈。本断言就是那条"实测数字有效性"的机器化防线。
        /// </summary>
        [Test]
        public void Url_Mobile_Alias_Forces_The_Mobile_Level_Even_When_Detection_Says_Desktop()
        {
            Assert.AreEqual(MobileQualityBootstrap.MobileQualityLevel,
                MobileQualityBootstrap.ResolveLevel("?quality=mobile", isMobileDevice: false),
                "判定失准（false）时别名也必须切档——否则人工实测在 iPad 桌面 UA 上跑的是 Ultra");
            Assert.AreEqual(MobileQualityBootstrap.MobileQualityLevel,
                MobileQualityBootstrap.ResolveLevel("?quality=mobile", isMobileDevice: true),
                "判定为真时同样切到移动档（与自动判定同值）");
        }

        [Test]
        public void Malformed_Url_Falls_Back_To_Detection()
            => Assert.AreEqual(MobileQualityBootstrap.MobileQualityLevel,
                MobileQualityBootstrap.ResolveLevel("?quality=abc", isMobileDevice: true));

        [Test]
        public void Mobile_Level_Stays_Inside_The_Mobile_Budget()
        {
            // 守护用例：有人把目标档改回重型配置时这条要红（照 TagManager 守护用例的范式）。
            int original = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(MobileQualityBootstrap.MobileQualityLevel);
            try
            {
                Assert.LessOrEqual(QualitySettings.shadowDistance, 30f, "移动档阴影距离必须 ≤30m");
                Assert.LessOrEqual(QualitySettings.pixelLightCount, 2, "移动档像素光 ≤2");
                Assert.AreEqual(ShadowQuality.HardOnly, QualitySettings.shadows, "移动档不跑软阴影");
                Assert.LessOrEqual((int)QualitySettings.skinWeights, 2, "移动档蒙皮权重 ≤2");
                Assert.LessOrEqual(QualitySettings.antiAliasing, 1, "移动档不开 MSAA");
                Assert.IsFalse(QualitySettings.realtimeReflectionProbes, "移动档不开实时反射探针");
            }
            finally
            {
                QualitySettings.SetQualityLevel(original);
            }
        }
    }
}
