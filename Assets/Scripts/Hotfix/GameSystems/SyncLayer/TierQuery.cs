namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 分档的 URL 旋钮（design §4）：`?full=&lt;N&gt;` 覆盖每帧预算、`?fulldist=&lt;m&gt;` 覆盖进入阈值。
    ///
    /// 为什么走 URL：出包的 WebGL 产物没有 Inspector，而**实测要在手机上扫参数**——
    /// 重新打包一轮几分钟，扫一组参数要来回十几轮（与 `?touch=1` / `?host=` 同源）。
    ///
    /// 解析复用 `UrlQuery.TryGetValue`（同一个实现）；畸形/越界一律忽略（返回 false，调用方保持默认）。
    /// </summary>
    public static class TierQuery
    {
        /// <summary>预算上限（挡住 `?full=999999` 把整屏塞满 Full）</summary>
        public const int MaxBudget = 200;
        /// <summary>距离上限（米）</summary>
        public const float MaxDistance = 200f;

        public static bool TryFull(string? url, out int budget)
        {
            budget = 0;
            if (!UrlQuery.TryGetValue(url, "full", out string raw)) return false;
            if (!int.TryParse(raw.Trim(), out int value) || value < 0 || value > MaxBudget) return false;
            budget = value;
            return true;
        }

        public static bool TryFullDistance(string? url, out float distance)
        {
            distance = 0f;
            if (!UrlQuery.TryGetValue(url, "fulldist", out string raw)) return false;
            if (!float.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value)) return false;
            if (value <= 0f || value > MaxDistance) return false;
            distance = value;
            return true;
        }
    }
}
