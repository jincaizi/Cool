using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// WebGL 出包（最小版 —— 部署与自动化留给 Task 9）。
    /// 与 BuildScript 同款：场景列表显式声明、失败在批处理下非零退出。
    ///
    /// 两处 WebGL 专有决定：
    /// ① 切构建目标是**两步**：切换会改变 UNITY_WEBGL 定义并触发重编译 + 域重载，
    ///    本方法的后续语句不保证执行——切完重新调用一次即可（第二次走构建分支）。
    /// ② 压缩必须关掉：静态托管用 `python -m http.server`（Step 10 与 Task 9 都这么起），
    ///    它不会给 .br/.gz 补 Content-Encoding，Brotli 包在那里一律加载失败。
    /// </summary>
    public static class WebGLBuildScript
    {
        private const string OutputDir = "Build/WebGL";

        private static readonly string[] Scenes =
        {
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity",
        };

        public static void Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[WebGLBuild] switching build target to WebGL...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[WebGLBuild] switch to WebGL failed");
                    return;
                }
                Debug.Log("[WebGLBuild] now on WebGL - re-run Build() to produce the player");
                return;
            }

            foreach (var scene in Scenes)
            {
                if (!File.Exists(scene))
                {
                    Debug.LogError("[WebGLBuild] 构建场景缺失: " + scene);
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                    return;
                }
            }

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[WebGLBuild] 出包失败: result=" + summary.result
                    + " errors=" + summary.totalErrors
                    + " size=" + summary.totalSize);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[WebGLBuild] 出包成功: " + summary.outputPath
                + " size=" + summary.totalSize + "B"
                + " time=" + summary.totalTime);
        }
    }
}
