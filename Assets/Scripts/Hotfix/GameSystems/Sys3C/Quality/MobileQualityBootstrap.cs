using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Quality
{
    /// <summary>
    /// 移动端/WebGL 的质量档（design T0）。
    ///
    /// 为什么必须做：工程 `QualitySettings.asset` 的 `m_PerPlatformDefaultQuality` 为**空**，
    /// 且全项目零 `QualitySettings` 调用 ⇒ 手机浏览器实测时跑的是当前档 5 "Ultra"
    /// （软阴影 / 4 级联 / shadowDistance 150 / 4 像素光 / MSAA 2× / 实时反射探针 / 蒙皮权重 4）。
    /// 那会让"同屏能扛多少个角色"的实测测到**别的瓶颈**（design §8-R1）。
    ///
    /// 档位取工程里已有的档 2 "Medium"（硬阴影 / 1 像素光 / shadowDistance 20 / 蒙皮权重 2 /
    /// 无 MSAA / 无实时反射探针）——**不新建档位**：改 `QualitySettings.asset` 会被编辑器回写，
    /// 运行时切换才稳。
    ///
    /// 判定顺序与 <see cref="TouchInputAvailability"/> 同源：**URL 参数 &gt; 自动判定**，
    /// 且畸形值一律忽略（现场多打一个字符的代价不能是"打不开游戏"）。
    /// </summary>
    public static class MobileQualityBootstrap
    {
        /// <summary>移动端目标档位（QualitySettings 索引：0=Very Low 1=Low 2=Medium 3=High 4=Very High 5=Ultra）</summary>
        public const int MobileQualityLevel = 2;

        /// <summary>工程里的档位数量（0..5），URL 越界值按畸形处理</summary>
        public const int MaxQualityLevel = 5;

        private const string UrlKey = "quality";

        /// <summary>
        /// 决定本次运行切到哪一档。返回 null = **不动**（保持工程当前档）。
        /// 纯函数（URL 与平台判定都由参数注入），逐条有单测。
        /// </summary>
        public static int? ResolveLevel(string? url, bool isMobileDevice)
        {
            if (UrlQuery.TryGetValue(url, UrlKey, out string raw))
            {
                string value = raw.Trim().ToLowerInvariant();
                if (value == "desktop") return null;   // 人工兜底：手机上强制不切档（对比实测用）
                // `mobile` 别名 = 强制切到移动档（人工实测用的就是 `?quality=mobile`，见
                // webgl-mobile-acceptance / 计划任务 10）。**必须无条件生效**：它是自动判定失准时
                // 唯一可靠的出口——iPad 桌面 UA 会让 isMobilePlatform 与 deviceType 两个判据都失效，
                // 别名若落进"畸形 ⇒ 自动判定"，那种设备会静默跑 Ultra，实测数字全部无效。
                if (value == "mobile") return MobileQualityLevel;
                if (int.TryParse(value, out int level) && level >= 0 && level <= MaxQualityLevel)
                    return level;
                // 畸形值落到自动判定——与 TouchInputAvailability 的"URL 优先但不阻塞"同源
            }

            return isMobileDevice ? MobileQualityLevel : (int?)null;
        }

        /// <summary>
        /// 平台判定。`Application.isMobilePlatform` 在 WebGL 下**可能不准**（项目已有实锤：
        /// iPad 默认桌面视图会返回 false，见 TouchInputAvailability 的注释），叠加
        /// `SystemInfo.deviceType` 兜底。出包产物没有 Inspector，唯一的现场开关是 `?quality=`。
        /// </summary>
        public static bool IsMobileDevice()
            => Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld;

        /// <summary>启动时应用一次（不依赖任何场景接线——Launch / CharacterSelect / DemoDay 都覆盖）</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            int? level = ResolveLevel(Application.absoluteURL, IsMobileDevice());
            if (level == null || level.Value == QualitySettings.GetQualityLevel()) return;

            QualitySettings.SetQualityLevel(level.Value, applyExpensiveChanges: true);
            UnityEngine.Debug.Log($"[Quality] 移动端定档：切到档 {level.Value}");
        }
    }
}
