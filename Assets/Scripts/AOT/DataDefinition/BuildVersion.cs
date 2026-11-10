using System.Text.RegularExpressions;
using UnityEngine;

namespace DataDefinition
{
    /// <summary>
    /// 版本号真值与显示格式。仓库里的 a.b.c 来自 ProjectSettings.bundleVersion；
    /// 出包脚本会临时追加 git 短 SHA 后 4 位（例如 0.1.0.cbad）。
    /// </summary>
    public static class BuildVersion
    {
        private static readonly Regex BaseVersionPattern =
            new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant);

        /// <summary>运行时完整版本串；编辑器里是 a.b.c，出包后是 a.b.c.sha4。</summary>
        public static string Raw => Application.version;

        /// <summary>UI 显示文本，例如 v0.1.0 或 v0.1.0.cbad。</summary>
        public static string Display => Format(Raw);

        public static string Format(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "v?";
            return "v" + raw.Trim();
        }

        public static bool IsBaseVersion(string version)
        {
            return !string.IsNullOrEmpty(version) && BaseVersionPattern.IsMatch(version);
        }

        public static string Compose(string baseVersion, string sha4)
        {
            return baseVersion + "." + sha4;
        }
    }
}
