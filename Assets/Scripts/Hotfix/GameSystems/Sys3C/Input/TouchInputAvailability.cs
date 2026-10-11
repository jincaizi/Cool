using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>触屏判定的人工档位（Inspector 可见、可在运行时切换）</summary>
    public enum TouchOverride
    {
        Auto = 0,
        ForceTouch = 1,
        ForceNoTouch = 2
    }

    /// <summary>
    /// 触屏可用性判定 —— **不能只看 Application.isMobilePlatform**。
    ///
    /// Unity 官方文档明确写了 WebGL 下该属性"由于隐私与匿名化原因，可能不会在所有浏览器上
    /// 报告准确信息"；已知失效场景：iPad 默认桌面视图下 `isMobilePlatform == false` 且
    /// `SystemInfo.deviceType == Desktop` —— 手机形态会静默退化成键鼠。
    ///
    /// 因此判定顺序为：**URL 参数 > 人工档位 > (isMobilePlatform || 特性检测)**。
    /// 特性检测在 WebGL 走 jslib（`navigator.maxTouchPoints` / `'ontouchend' in document`），
    /// 其它平台走 `Input.touchSupported`。
    /// </summary>
    public static class TouchInputAvailability
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int WebGLTouch_HasTouch();
#endif

        /// <summary>
        /// 浏览器/设备的触摸能力特性检测。
        /// 注意：**特性检测会误判触屏笔记本**（有 `ontouchend` 但用户用鼠标）—— 这是已知代价，
        /// 多认的后果只是多显示一层触控 UI；判定不对时可用人工档位或 URL 参数覆盖。
        /// </summary>
        public static bool TouchFeatureDetected()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebGLTouch_HasTouch() != 0;
#else
            // 必须全限定：本文件所在命名空间以 .Input 结尾，裸 Input 会被解析成命名空间
            return UnityEngine.Input.touchSupported;
#endif
        }

        /// <summary>
        /// 从 URL 解析 `?touch=1` / `?touch=0`（无该参数返回 null）。抽成纯函数以便单测 ——
        /// 出包后的 URL 是浏览器边界，本地测不到。
        /// 取值走 `UrlQuery.TryGetValue`（与 `?host=` 共用同一条解析路径）；**只看值首字符**是既有语义
        /// （`?touch=10` 判开），抽取共享实现不得改变它 —— 见 `TouchInputAvailabilityTests` 的锁定用例。
        /// </summary>
        public static bool? ParseTouchQuery(string url)
        {
            if (!UrlQuery.TryGetValue(url, "touch", out string value) || value.Length == 0) return null;
            if (value[0] == '1') return true;
            if (value[0] == '0') return false;
            return null;
        }

        /// <summary>WebGL 运行时的 URL 覆盖（面试现场的救命绳）；编辑器/其它平台恒 null。</summary>
        public static bool? FromUrlQuery()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseTouchQuery(Application.absoluteURL);
#else
            return null;
#endif
        }

        /// <summary>最终裁决：URL > 人工档位 > 自动判定</summary>
        public static bool Resolve(TouchOverride mode)
        {
            bool? fromUrl = FromUrlQuery();
            if (fromUrl.HasValue) return fromUrl.Value;
            if (mode == TouchOverride.ForceTouch) return true;
            if (mode == TouchOverride.ForceNoTouch) return false;
            return Application.isMobilePlatform || TouchFeatureDetected();
        }
    }
}
