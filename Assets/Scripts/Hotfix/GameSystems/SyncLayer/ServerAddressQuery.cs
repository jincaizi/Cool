using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 服务器地址的 URL 参数（`?host=&lt;IP&gt;`）——把地址写进投递链接，面试官打开后**不用手填**。
    ///
    /// 与 `?touch=1` 同源：解析走 `UrlQuery.TryGetValue`（同一个实现），URL 读的是
    /// `Application.absoluteURL`（WebGL 的页面 URL），**不需要新的 jslib**。
    ///
    /// **作用范围：只用于预填**（`NetworkModeService.PrefillHost/PrefillPort` 把值填进选人面板的地址框）。
    /// 连接的端点在那一刻由框里的值决定，不再回头看这个参数 —— 见 `ServerAddressQueryTests` 的
    /// "手打值压过 URL"用例与 `NetworkModeService.LoadHost` 的注释。
    ///
    /// 规则（每种输入都有确定行为；畸形一律**忽略**、绝不抛 —— 现场多打一个字符的代价
    /// 不能是"打不开游戏"）。逐条有单测：`ServerAddressQueryTests`。
    ///
    /// | URL | host | port |
    /// |---|---|---|
    /// | `?host=1.2.3.4` | `1.2.3.4` | `defaultPort`（链接自洽，不吃浏览器里的旧存档端口） |
    /// | `?host=1.2.3.4:9000` | `1.2.3.4` | `9000` |
    /// | 无该参数 / 畸形 | 不覆盖（返回 false，调用方保持既有行为） | — |
    ///
    /// 不支持 IPv6 字面量（`[::1]:8000`）：演示用的是 IPv4 与主机名，如实记在 T13 报告里。
    /// </summary>
    public static class ServerAddressQuery
    {
        private const string Key = "host";

        /// <summary>
        /// 纯解析（本地可单测，不需要真连接）。成功带出 host/port；失败时 host 为空串、port 为 0。
        /// `?host=` 不带端口时用 `defaultPort` 补齐。
        /// </summary>
        public static bool TryParse(string? url, int defaultPort, out string host, out int port)
        {
            host = string.Empty;
            port = 0;

            if (!UrlQuery.TryGetValue(url, Key, out string value)) return false;

            string candidate = value.Trim();
            if (candidate.Length == 0) return false;

            // 端口是可选的：最后一个冒号之后整段是 1..65535 的数字就当端口，否则整串都算畸形
            // （所以 `1.2.3.4` 这种没有冒号的形态、以及 `:abc` 这种都由同一条判断收口）
            // 注意端口先落在局部变量里：失败路径上不得留下"半个 port"，那会让调用方以为解析成功
            int parsedPort = 0;
            int colon = candidate.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(candidate.Substring(colon + 1), out parsedPort) || parsedPort < 1 || parsedPort > 65535)
                    return false;
                candidate = candidate.Substring(0, colon);
            }

            if (!IsPlausibleHost(candidate)) return false;

            host = candidate;
            port = parsedPort == 0 ? defaultPort : parsedPort;
            return true;
        }

        /// <summary>
        /// 当前页面 URL —— 浏览器里是 `Application.absoluteURL`，**编辑器与其它平台恒空串**
        /// （与 `TouchInputAvailability.FromUrlQuery` 同一条路径，没有新 jslib）。
        /// 这是整条链上唯一的浏览器边界：解析层是纯字符串，所以本地可测、出包才验得到真实 URL。
        /// </summary>
        public static string CurrentPageUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Application.absoluteURL ?? string.Empty;
#else
            return string.Empty;
#endif
        }

        /// <summary>地址里不允许空白与斜杠（首尾空白已被 Trim 掉，弦内的都是畸形）</summary>
        private static bool IsPlausibleHost(string candidate)
        {
            if (candidate.Length == 0) return false;
            foreach (char c in candidate)
            {
                if (char.IsWhiteSpace(c) || c == '/') return false;
            }
            return true;
        }
    }
}
