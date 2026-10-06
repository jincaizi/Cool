using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// P6 交付构建——Windows x64 独立包。
    /// 后端固定 Mono（出包快、变量少；IL2CPP 留待移动端验证）。
    /// 场景列表显式声明，不依赖 EditorBuildSettings 当前状态。
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

            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputDir + "/" + ExeName,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                // 失败必须让命令行拿到非零退出码（自动化出包依赖）。
                // 交互模式下不得 Exit——会直接关掉编辑器。
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
