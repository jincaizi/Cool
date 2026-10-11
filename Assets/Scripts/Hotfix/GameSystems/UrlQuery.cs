using System;

namespace Hotfix.GameSystems
{
    /// <summary>
    /// 页面 URL 的查询参数读取 —— `?touch=`（触屏判定）与 `?host=`（服务器地址）共用
    /// **同一条解析路径**：只有一个实现，再加参数一律走这里，别再写第二套（Task 13 的裁定）。
    ///
    /// 语义是**从 `TouchInputAvailability.ParseTouchQuery` 原样抽取**的（逐字保留，含它的宽容处）：
    /// 在整个 URL 里找 `&lt;key&gt;=`（不区分大小写）并取其后的值，值到 `&` / `#` / 串尾为止。
    /// 不做百分号解码 —— 演示用的参数都是 URL 安全字符（IP、端口、0/1）。
    ///
    /// 读者要的是"值"，怎么解读由各自的类型负责（`touch` 只看首字符，`host` 另有形态规则）——
    /// 这正是抽取的那条**共享路径**的边界。
    /// </summary>
    public static class UrlQuery
    {
        private static readonly char[] ValueTerminators = { '&', '#' };

        /// <summary>
        /// 取查询值。无该参数、值在串尾被截断、URL 为空 → 返回 false（`value` 为空串）。
        /// 注意：`key=` 后面紧跟 `&`/`#` 时返回 **true 且 value 为空串**（"参数在、值空"），
        /// 由调用方决定这种空值算不算数。
        /// </summary>
        public static bool TryGetValue(string? url, string key, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key)) return false;

            int at = url.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;

            int start = at + key.Length + 1;
            if (start >= url.Length) return false;   // `...?touch=` 恰好收尾：无值可读

            int end = url.IndexOfAny(ValueTerminators, start);
            if (end < 0) end = url.Length;

            value = url.Substring(start, end - start);
            return true;
        }
    }
}
