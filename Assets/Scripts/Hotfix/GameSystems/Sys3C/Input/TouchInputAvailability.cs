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
    /// 判定顺序：**URL 参数 > 人工档位 > 自动**。自动档自 2026-10-18 起默认显示触控 UI
    /// （编辑器/PC/移动端一致）；WASD/鼠标由 JoystickInputAdapter 的键盘兜底保留。
    /// 特性检测（WebGL jslib `navigator.maxTouchPoints` / `'ontouchend' in document`，
    /// 其它平台 `Input.touchSupported`）保留给需要按设备细分的未来调用方。
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

        /// <summary>
        /// 最终裁决：URL > 人工档位 > 自动判定。
        ///
        /// 2026-10-18 产品决定：**编辑器、PC 与移动端都显示触控 UI**（摇杆 + 三按钮）。
        /// WASD/鼠标仍可用 —— `MobileInputBinder` 给 JoystickInputAdapter 注入
        /// KeyboardInputAdapter 兜底，摇杆/观察区空闲时回退键鼠。
        /// 需要纯键鼠时用 `ForceNoTouch` 或 URL `?touch=0`。
        /// </summary>
        public static bool Resolve(TouchOverride mode)
        {
            bool? fromUrl = FromUrlQuery();
            if (fromUrl.HasValue) return fromUrl.Value;
            if (mode == TouchOverride.ForceTouch) return true;
            if (mode == TouchOverride.ForceNoTouch) return false;

            // 自动判定：默认显示触控 UI（PC 也可用摇杆；WASD 由混合适配器保留）。
            return true;
        }
    }
}
