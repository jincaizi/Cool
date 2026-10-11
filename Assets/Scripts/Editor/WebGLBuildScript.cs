using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// WebGL 出包 —— 面试演示的交付入口（Task 9）。
    /// 与 BuildScript.BuildWindows 同构：菜单 + 静态入口、场景列表显式声明、失败在批处理下非零退出。
    ///
    /// 三处 WebGL 专有决定：
    /// ① 切构建目标是**两步**：切换会改变 UNITY_WEBGL 定义并触发重编译 + 域重载，
    ///    本方法的后续语句不保证执行——切完重新调用一次即可（第二次走构建分支）。
    /// ② 压缩选 **Disabled**：部署层（`production/deploy/webgl-demo/StaticServer/`）
    ///    边传边 gzip，Unity 侧再预压缩就是**双重压缩**（客户端拿到 .gz 字节当 wasm 执行，报
    ///    "Response has unsupported MIME type" / 编译失败），且预压缩要求服务器把
    ///    `WebGL.wasm` 映射到 `WebGL.wasm.gz` 并自带 Content-Encoding —— 多一处只能靠约定维系的
    ///    环节。Disabled + 传输层 gzip 的收益是：连 loader.js/framework.js/index.html 一起压，
    ///    产物与服务器的耦合只剩"是原文件"这一条。
    ///    这一步**临时改写**项目设置里的 `webGLCompressionFormat`（仓库值是 Gzip），
    ///    构建结束（无论成败）用 finally 还原 —— 出包不得把项目设置永久改掉，
    ///    否则下一次"直接点 Build Player"或 CI 构建会拿 Disabled 出包而无人知道原因。
    /// ③ 出包后**自查产物形状**：Build 目录里出现 .gz/.br 就说明 ② 失效（旧值回来了），
    ///    此时静态服务器会 404 掉 WebGL.wasm —— 这是静默失败，必须在这里拦住。
    /// </summary>
    public static class WebGLBuildScript
    {
        private const string OutputDir = "Build/WebGL";
        private const string BuildSubDir = OutputDir + "/Build";

        // 静态服务器按这四个名字找文件（见 Assets/.../index.html 的 config）
        private static readonly string[] Payload =
        {
            "WebGL.loader.js",
            "WebGL.framework.js",
            "WebGL.wasm",
            "WebGL.data",
        };

        private static readonly string[] Scenes =
        {
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity",
        };

        [MenuItem("Cool/Build/WebGL")]
        public static void BuildWebGL() => Build();

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

            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            var previousFallback = PlayerSettings.WebGL.decompressionFallback;

            Debug.Log("[WebGLBuild] compression " + previousCompression + " -> Disabled"
                + "（部署层边传边 gzip；Unity 侧预压缩会双重压缩）");

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            bool succeeded;
            BuildSummary summary;
            try
            {
                Directory.CreateDirectory(OutputDir);

                var options = new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = OutputDir,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                };

                summary = BuildPipeline.BuildPlayer(options).summary;
                succeeded = summary.result == BuildResult.Succeeded;
            }
            finally
            {
                // 还原项目设置（含把改动落盘）——出包的副作用必须止于出包
                PlayerSettings.WebGL.compressionFormat = previousCompression;
                PlayerSettings.WebGL.decompressionFallback = previousFallback;
                AssetDatabase.SaveAssets();
            }

            if (!succeeded)
            {
                // 失败必须让命令行拿到非零退出码（自动化出包依赖）。
                // 交互模式下不得 Exit——会直接关掉编辑器。
                Debug.LogError("[WebGLBuild] 出包失败: result=" + summary.result
                    + " errors=" + summary.totalErrors
                    + " size=" + summary.totalSize);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[WebGLBuild] 出包成功: " + summary.outputPath
                + " size=" + summary.totalSize + "B"
                + " time=" + summary.totalTime);

            VerifyPayloadShape();
        }

        /// <summary>
        /// 断言产物是"静态服务器能直接托管"的形状，并把首屏体积打进日志。
        /// 这里报 error（批处理下非零退出）而不是 warning：形状不对的包能构建成功、
        /// 能在 file:// 下打开，只在浏览器里 404 —— 留到部署时才发现，代价是一次面试。
        /// </summary>
        private static void VerifyPayloadShape()
        {
            long total = 0;
            bool broken = false;

            foreach (var name in Payload)
            {
                var path = BuildSubDir + "/" + name;
                if (!File.Exists(path))
                {
                    Debug.LogError("[WebGLBuild] 产物缺失: " + path);
                    broken = true;
                    continue;
                }
                long size = new FileInfo(path).Length;
                total += size;
                Debug.Log("[WebGLBuild]   " + name + " = " + size + "B (" + (size / 1048576.0).ToString("F1") + " MB)");
            }

            foreach (var stray in Directory.GetFiles(BuildSubDir))
            {
                var ext = Path.GetExtension(stray).ToLowerInvariant();
                if (ext == ".gz" || ext == ".br")
                {
                    Debug.LogError("[WebGLBuild] 发现预压缩产物 " + stray
                        + " —— Unity 侧压缩没关掉，静态服务器会 404 掉 WebGL.wasm");
                    broken = true;
                }
            }

            if (broken)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[WebGLBuild] 首屏体积合计 " + total + "B (" + (total / 1048576.0).ToString("F1")
                + " MB，未压缩；gzip 后约 1/4 —— 见 production/verify/t9 的实测)");
        }
    }
}
