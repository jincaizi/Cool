using System;
using System.IO;
using DataDefinition;
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
        private const string TemplateDir = "Assets/WebGLTemplates/MobileLandscape";
        private const string TemplateSetting = "PROJECT:MobileLandscape";
        private const string LandscapeGateMarker = "id=\"landscape-gate\"";
        private const string BuildStampMarker = "__BUILD_STAMP__";

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

            // 移动端横屏遮罩在页面模板里；模板没设对时，画面能出来但手机竖屏仍可操作。
            // 产物检查（VerifyPayloadShape）是第二道闸门。
            if (!File.Exists(TemplateDir + "/index.html"))
            {
                Debug.LogError("[WebGLBuild] WebGL 模板缺失: " + TemplateDir + "/index.html");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            if (PlayerSettings.WebGL.template != TemplateSetting)
            {
                Debug.LogError("[WebGLBuild] webGLTemplate 应为 " + TemplateSetting
                    + "，当前是 " + PlayerSettings.WebGL.template);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var baseVersion = PlayerSettings.bundleVersion;
            if (!BuildVersion.IsBaseVersion(baseVersion))
            {
                Debug.LogError("[WebGLBuild] bundleVersion 必须是三段数字（a.b.c），当前是 '" + baseVersion + "'");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            if (!BuildVersionStamp.TryGetGitShortSha4(out var sha4))
                Debug.LogWarning("[WebGLBuild] git SHA 不可用，使用 0000");

            var stampedVersion = BuildVersion.Compose(baseVersion, sha4);
            Debug.Log("[WebGLBuild] version " + baseVersion + " -> " + stampedVersion);

            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            var previousFallback = PlayerSettings.WebGL.decompressionFallback;
            var previousVersion = PlayerSettings.bundleVersion;

            Debug.Log("[WebGLBuild] compression " + previousCompression + " -> Disabled"
                + "（部署层边传边 gzip；Unity 侧预压缩会双重压缩）");

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.bundleVersion = stampedVersion;

            var indexPath = OutputDir + "/index.html";
            var backupPath = indexPath + ".bak";

            bool succeeded = false;
            bool injected = false;
            bool verified = false;
            bool backupCleanupFailed = false;
            BuildSummary summary = default;
            try
            {
                try
                {
                    Directory.CreateDirectory(OutputDir);

                    // Unity 不会覆盖已存在的 index.html，上次注入过时间戳的产物会让
                    // __BUILD_STAMP__ 找不到；先移走，强制本次从模板重新生成。
                    // 移走的旧页面同时是失败/取消时的回退，成功链路走完才删除。
                    PrepareIndexBackup(indexPath, backupPath);

                    var options = new BuildPlayerOptions
                    {
                        scenes = Scenes,
                        locationPathName = OutputDir,
                        target = BuildTarget.WebGL,
                        options = BuildOptions.None,
                    };

                    summary = BuildPipeline.BuildPlayer(options).summary;
                    succeeded = summary.result == BuildResult.Succeeded;

                    if (succeeded)
                    {
                        Debug.Log("[WebGLBuild] 出包成功: " + summary.outputPath
                            + " size=" + summary.totalSize + "B"
                            + " time=" + summary.totalTime);

                        var buildStamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                        var cacheKey = stampedVersion + "-" + buildStamp;
                        if (InjectBuildStamp(indexPath, buildStamp, out var injectFailure))
                        {
                            injected = true;
                            Debug.Log("[WebGLBuild] cacheKey=" + cacheKey);
                            verified = VerifyPayloadShape(stampedVersion, buildStamp);
                        }
                        else
                        {
                            Debug.LogError("[WebGLBuild] " + injectFailure + "，缓存键无法注入");
                        }
                    }
                }
                finally
                {
                    // 三个条件（构建成功 + 注入成功 + 产物校验通过）缺一就把旧入口页面放回来，
                    // 否则失败/取消的 Build/WebGL 会没有 index.html，publish-server.sh 会原样发布一个 404 站点。
                    try
                    {
                        if (succeeded && injected && verified)
                        {
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                                Debug.Log("[WebGLBuild] 产物校验通过，删除 index.html.bak");
                            }
                        }
                        else
                        {
                            RestoreIndexBackup(indexPath, backupPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        backupCleanupFailed = true;
                        Debug.LogError("[WebGLBuild] index.html 备份处理失败: " + ex.Message);
                    }
                }
            }
            finally
            {
                // 还原项目设置（含把改动落盘）——出包的副作用必须止于出包
                PlayerSettings.WebGL.compressionFormat = previousCompression;
                PlayerSettings.WebGL.decompressionFallback = previousFallback;
                PlayerSettings.bundleVersion = previousVersion;
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

            // 注入或产物校验失败的具体原因已在上面的 LogError 里。
            if (!injected || !verified || backupCleanupFailed)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
        }

        /// <summary>
        /// 出包前把旧 index.html 移到 .bak，强制本次构建从模板重新拷贝入口页。
        /// 若发现上次中断留下的 .bak：有 index.html 时删掉陈旧备份，没有时先还原，
        /// 避免上一次崩溃把唯一的入口页留在 .bak 里。
        /// </summary>
        private static void PrepareIndexBackup(string indexPath, string backupPath)
        {
            if (File.Exists(backupPath))
            {
                if (File.Exists(indexPath))
                {
                    File.Delete(backupPath);
                    Debug.Log("[WebGLBuild] 删除上次中断遗留的 index.html.bak");
                }
                else
                {
                    File.Move(backupPath, indexPath);
                    Debug.Log("[WebGLBuild] 上次中断遗留的 index.html.bak 已恢复为 index.html");
                }
            }

            if (!File.Exists(indexPath))
                return;

            File.Move(indexPath, backupPath);
            Debug.Log("[WebGLBuild] 备份旧 index.html -> index.html.bak（出包失败/取消时恢复）");
        }

        /// <summary>
        /// 出包没有完整成功（构建失败/取消、注入失败、产物校验失败或异常）时，
        /// 把备份的入口页放回去；成功路径不调用，.bak 由成功分支删除。
        /// </summary>
        private static void RestoreIndexBackup(string indexPath, string backupPath)
        {
            if (!File.Exists(backupPath))
                return;

            File.Copy(backupPath, indexPath, true);
            File.Delete(backupPath);
            Debug.LogWarning("[WebGLBuild] 出包未完成，已恢复备份的 index.html");
        }

        /// <summary>
        /// 断言产物是"静态服务器能直接托管"的形状，并把首屏体积打进日志。
        /// 这里报 error 而不是 warning：形状不对的包能构建成功、能在 file:// 下打开，
        /// 只在浏览器里 404 —— 留到部署时才发现，代价是一次面试。
        /// 返回 false 时由 Build 恢复 index.html 备份并让批处理非零退出。
        /// </summary>
        private static bool VerifyPayloadShape(string stampedVersion, string buildStamp)
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

            var indexPath = OutputDir + "/index.html";
            string indexHtml = File.Exists(indexPath) ? File.ReadAllText(indexPath) : string.Empty;

            if (!indexHtml.Contains(LandscapeGateMarker))
            {
                Debug.LogError("[WebGLBuild] index.html 未包含横屏遮罩 —— 出包没有使用 "
                    + TemplateSetting + " 模板");
                broken = true;
            }

            if (!indexHtml.Contains(stampedVersion))
            {
                Debug.LogError("[WebGLBuild] index.html 未包含构建版本 " + stampedVersion
                    + " —— 模板没有消费 PRODUCT_VERSION");
                broken = true;
            }

            if (!indexHtml.Contains(buildStamp))
            {
                Debug.LogError("[WebGLBuild] index.html 未包含构建时间戳 " + buildStamp
                    + " —— 占位符替换失败");
                broken = true;
            }

            if (indexHtml.Contains(BuildStampMarker))
            {
                Debug.LogError("[WebGLBuild] index.html 仍包含占位符 " + BuildStampMarker);
                broken = true;
            }

            if (!indexHtml.Contains("?v="))
            {
                Debug.LogError("[WebGLBuild] index.html 未包含缓存查询参数 ?v=");
                broken = true;
            }

            if (broken)
            {
                return false;
            }

            Debug.Log("[WebGLBuild] 首屏体积合计 " + total + "B (" + (total / 1048576.0).ToString("F1")
                + " MB，未压缩；gzip 后约 1/4 —— 见 production/verify/t9 的实测)");
            return true;
        }

        /// <summary>
        /// 把出包时间戳写进产物 index.html 的占位符。只改产物，不改模板。
        /// 失败时用 failure 区分"产物缺失"与"占位符不存在"（增量构建两种都可能遇到）。
        /// </summary>
        private static bool InjectBuildStamp(string indexPath, string buildStamp, out string failure)
        {
            failure = null;
            if (!File.Exists(indexPath))
            {
                failure = "产物 index.html 不存在: " + indexPath;
                return false;
            }

            string html = File.ReadAllText(indexPath);
            if (!html.Contains(BuildStampMarker))
            {
                failure = "index.html 中未找到占位符 " + BuildStampMarker;
                return false;
            }

            File.WriteAllText(indexPath, html.Replace(BuildStampMarker, buildStamp));
            return true;
        }
    }
}
