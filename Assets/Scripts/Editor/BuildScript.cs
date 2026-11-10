using System.IO;
using DataDefinition;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// P6 交付构建——Windows x64 独立包。
    /// 后端固定 Mono（出包快、变量少；IL2CPP 留待移动端验证）。
    /// 场景列表显式声明，不依赖 EditorBuildSettings 当前状态。
    /// 出包期间临时把 bundleVersion 追加为 a.b.c.sha4，结束（无论成败）还原。
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDir = "Build/Windows";
        private const string ExeName = "CoolP6.exe";

        private static readonly string[] Scenes =
        {
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity",
        };

        [MenuItem("Cool/Build/Windows x64")]
        public static void BuildWindows()
        {
            foreach (var scene in Scenes)
            {
                if (!File.Exists(scene))
                {
                    Debug.LogError("[Build] 构建场景缺失: " + scene);
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                    return;
                }
            }

            var baseVersion = PlayerSettings.bundleVersion;
            if (!BuildVersion.IsBaseVersion(baseVersion))
            {
                Debug.LogError("[Build] bundleVersion 必须是三段数字（a.b.c），当前是 '" + baseVersion + "'");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            if (!BuildVersionStamp.TryGetGitShortSha4(out var sha4))
                Debug.LogWarning("[Build] git SHA 不可用，使用 0000");

            Directory.CreateDirectory(OutputDir);

            var previousVersion = PlayerSettings.bundleVersion;
            var stampedVersion = BuildVersion.Compose(baseVersion, sha4);
            PlayerSettings.bundleVersion = stampedVersion;
            Debug.Log("[Build] version " + previousVersion + " -> " + stampedVersion);

            BuildSummary summary = default;
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = OutputDir + "/" + ExeName,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };

                summary = BuildPipeline.BuildPlayer(options).summary;
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
                AssetDatabase.SaveAssets();
            }

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Build] 出包失败: result=" + summary.result
                    + " errors=" + summary.totalErrors
                    + " size=" + summary.totalSize);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Build] 出包成功: " + summary.outputPath
                + " size=" + summary.totalSize + "B"
                + " time=" + summary.totalTime);
        }
    }
}
