# 版本号、构建标识与 WebGL 缓存实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 建立 `a.b.c` 版本号真值、构建时追加 git 短 SHA 后 4 位、全局右下角 UGUI 版本标签，并让 WebGL 静态站点在重新部署后自动拿到新资源。

**架构：** `ProjectSettings.bundleVersion` 是唯一真值；Windows/WebGL 出包脚本在 `BuildPlayer` 前临时拼成 `a.b.c.sha4`，结束后还原。WebGL 模板用 `PRODUCT_VERSION` 生成版本化 URL，出包后注入 UTC 时间戳作为缓存键；StaticServer 对 HTML 回 `no-cache`、对带 `?v=` 的 `Build/*` 回长缓存。运行时 `DataDefinition.BuildVersion` 同时服务 UI 与登录 `ClientVersion`。

**技术栈：** Unity 2022.3.25f1、C#、UGUI/TextMeshPro、NUnit EditMode/PlayMode、.NET 8 `StaticServer`、Unity MCP（Prefab 创建/出包）。

**规格：** [2026-11-11-versioning-and-webgl-cache-design.md](../specs/2026-11-11-versioning-and-webgl-cache-design.md)

---

## 全局约束

- 仓库里有大量与本任务无关的未提交改动。每个任务只 `git add` 本任务列出的文件，**不要 `git add -A`，不要回退或整理无关文件**。
- 不提交 `Build/`、`Library/`、`production/deploy/webgl-demo/dist/`（均被 `.gitignore` 忽略）。
- 出包脚本必须在 `finally` 里还原 `PlayerSettings.bundleVersion`（WebGL 还要还原 compression/decompressionFallback），出包后 `ProjectSettings.asset` 的 `bundleVersion` 必须回到 `0.1.0`。
- 不改 `ProtocolConstants.ProtocolVersion`，不改服务端代码；登录只改 `ClientVersion` 字段的值。
- `BuildInfoPanel` 不得拦截输入：根节点无 Graphic，文本 `raycastTarget = false`。
- 版本纯函数放 `DataDefinition`（`GameSys.EditorTests` 能引用）；git 进程调用放默认 `Assembly-CSharp-Editor`（不新增 asmdef）。
- 跑 StaticServer 用 `dotnet <dll>`，不要用 `dotnet run` 的 apphost——HttpListener 会抛“句柄无效”。
- Unity 域重载后 MCP 工具可能消失；重新连接后再继续。WebGL 出包通过 MCP `script-execute` 时把超时设到至少 600 秒。
- 测试用 `bash tools/unity-tests.sh EditMode <Class>` / `bash tools/unity-tests.sh PlayMode <Class>`。

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/AOT/DataDefinition/BuildVersion.cs`（新建） | 版本解析/显示纯函数；UI 与登录共用 |
| `Assets/Scripts/Editor/BuildVersionStamp.cs`（新建） | 读取 git 短 SHA 后 4 位；只在出包时用 |
| `Assets/Scripts/Editor/BuildScript.cs`（修改） | Windows 出包临时追加 SHA，结束还原 |
| `Assets/Scripts/Editor/WebGLBuildScript.cs`（修改） | WebGL 出包追加 SHA、注入时间戳、校验产物 |
| `Assets/WebGLTemplates/MobileLandscape/index.html`（修改） | 版本化 URL、`cacheKey`、`cacheControl` |
| `production/deploy/webgl-demo/StaticServer/Program.cs`（修改） | 分级 `Cache-Control`、`Vary`、404/500、日志 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Panel/BuildInfoPanel.cs`（新建） | 全局版本标签面板 |
| `Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab`（新建） | 右下角 TMP 文本预制体 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`（修改） | 注册并显示 `BuildInfoPanel` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`（修改） | `ClientVersion = BuildVersion.Raw` |
| `Assets/Scripts/Tests/Editor/BuildVersionTests.cs`（新建） | `Format`/`IsBaseVersion`/`Compose` |
| `Assets/Scripts/Tests/Editor/WebGLTemplateCacheTests.cs`（新建） | 模板缓存键静态检查 |
| `Assets/Scripts/Tests/PlayMode/BuildInfoPanelTests.cs`（新建） | 版本标签文本/层/非交互 |
| `docs/superpowers/guides/webgl-demo-runbook.md`（修改） | 版本与缓存验证 Runbook |
| `Assets/Scripts/AOT/DataDefinition/README.md`（修改） | `BuildVersion` 与版本规则 |
| `Assets/Scripts/Hotfix/GameSystems/UI/README.md`（修改） | 版本标签说明 |
| `ProjectSettings/ProjectSettings.asset`（修改） | `bundleVersion: 0.1` → `0.1.0` |

---

## 任务 1：版本解析工具与 EditMode 测试

**文件：**
- 创建：`Assets/Scripts/AOT/DataDefinition/BuildVersion.cs`
- 创建：`Assets/Scripts/Tests/Editor/BuildVersionTests.cs`
- 修改：`Assets/Scripts/AOT/DataDefinition/README.md`
- 测试：`Assets/Scripts/Tests/Editor/BuildVersionTests.cs`

- [ ] **步骤 1：创建可编译的 `BuildVersion` 桩**

```csharp
using System.Text.RegularExpressions;
using UnityEngine;

namespace DataDefinition
{
    public static class BuildVersion
    {
        public static string Raw => Application.version;
        public static string Display => "";
        public static string Format(string raw) => "";
        public static bool IsBaseVersion(string version) => false;
        public static string Compose(string baseVersion, string sha4) => "";
    }
}
```

- [ ] **步骤 2：编写失败的测试**

```csharp
using DataDefinition;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class BuildVersionTests
    {
        [Test]
        public void test_format_adds_v_prefix_to_three_segments()
        {
            Assert.AreEqual("v0.1.0", BuildVersion.Format("0.1.0"));
        }

        [Test]
        public void test_format_keeps_four_segment_build_suffix()
        {
            Assert.AreEqual("v0.1.0.cbad", BuildVersion.Format("0.1.0.cbad"));
        }

        [Test]
        public void test_format_blank_returns_placeholder()
        {
            Assert.AreEqual("v?", BuildVersion.Format(null));
            Assert.AreEqual("v?", BuildVersion.Format("   "));
        }

        [Test]
        public void test_base_version_accepts_three_numeric_segments()
        {
            Assert.IsTrue(BuildVersion.IsBaseVersion("0.1.0"));
            Assert.IsTrue(BuildVersion.IsBaseVersion("12.34.56"));
        }

        [Test]
        public void test_base_version_rejects_two_and_four_segments()
        {
            Assert.IsFalse(BuildVersion.IsBaseVersion("0.1"));
            Assert.IsFalse(BuildVersion.IsBaseVersion("0.1.0.cbad"));
            Assert.IsFalse(BuildVersion.IsBaseVersion("v0.1.0"));
            Assert.IsFalse(BuildVersion.IsBaseVersion("0.1.x"));
        }

        [Test]
        public void test_compose_appends_sha4()
        {
            Assert.AreEqual("0.1.0.cbad", BuildVersion.Compose("0.1.0", "cbad"));
        }
    }
}
```

- [ ] **步骤 3：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode BuildVersionTests`

预期：6 个用例失败（`Display` 返回空、`IsBaseVersion` 永远 false、`Compose` 返回空）。若编译失败，说明 Unity 还没导入新文件，等资产刷新后重跑。

- [ ] **步骤 4：实现最小版本工具**

```csharp
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
```

- [ ] **步骤 5：运行测试确认通过**

运行：`bash tools/unity-tests.sh EditMode BuildVersionTests`

预期：6 个用例全部 PASS。

- [ ] **步骤 6：更新 DataDefinition README**

在 `Assets/Scripts/AOT/DataDefinition/README.md` 的文件列表里加：

```markdown
- `BuildVersion.cs`：构建版本号的运行时门面。`Raw` 是 `Application.version`，`Display` 加 `v` 前缀；`IsBaseVersion` / `Compose` 供出包脚本使用。版本规则：`a` 大版本 / 里程碑 / 不兼容改动，`b` 中等更新 / 新系统 / 新玩法，`c` 小迭代 / 修复 / 数值与表现调整；WebGL/Windows 出包时临时追加 git 短 SHA 后 4 位。
```

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/AOT/DataDefinition/BuildVersion.cs Assets/Scripts/AOT/DataDefinition/BuildVersion.cs.meta \
        Assets/Scripts/AOT/DataDefinition/README.md \
        Assets/Scripts/Tests/Editor/BuildVersionTests.cs Assets/Scripts/Tests/Editor/BuildVersionTests.cs.meta
git commit -m "feat: add build version formatting helper"
```

---

## 任务 2：Editor git SHA 读取与 Windows 出包注入

**文件：**
- 创建：`Assets/Scripts/Editor/BuildVersionStamp.cs`
- 修改：`Assets/Scripts/Editor/BuildScript.cs`
- 修改：`ProjectSettings/ProjectSettings.asset:142`
- 测试：编译 + Windows 出包日志

- [ ] **步骤 1：创建 git SHA 读取工具**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// 出包用的 git 构建标识。只负责读 git；纯函数在 DataDefinition.BuildVersion。
    /// 放在默认 Editor 程序集，与两个出包脚本同程序集，不新增 asmdef。
    /// </summary>
    public static class BuildVersionStamp
    {
        /// <summary>取 git 短 SHA 最后 4 位；失败时 sha4 = "0000" 并返回 false。</summary>
        public static bool TryGetGitShortSha4(out string sha4)
        {
            sha4 = "0000";
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    UnityEngine.Debug.LogWarning("[BuildVersion] 找不到项目根目录，使用 SHA 0000");
                    return false;
                }

                var startInfo = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return false;

                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch { }
                        UnityEngine.Debug.LogWarning("[BuildVersion] git rev-parse 超时，使用 SHA 0000");
                        return false;
                    }

                    string output = process.StandardOutput.ReadToEnd().Trim();
                    if (process.ExitCode != 0 || output.Length < 4)
                    {
                        UnityEngine.Debug.LogWarning("[BuildVersion] 无法读取 git HEAD，使用 SHA 0000");
                        return false;
                    }

                    sha4 = output.Substring(output.Length - 4);
                    return true;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[BuildVersion] 读取 git SHA 失败，使用 0000：" + e.Message);
                return false;
            }
        }
    }
}
```

- [ ] **步骤 2：规范基版本**

把 `ProjectSettings/ProjectSettings.asset` 第 142 行：

```yaml
  bundleVersion: 0.1
```

改成：

```yaml
  bundleVersion: 0.1.0
```

- [ ] **步骤 3：Windows 出包临时追加 SHA**

把 `Assets/Scripts/Editor/BuildScript.cs` 改为：

```csharp
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
```

- [ ] **步骤 4：编译并跑 Windows 出包验证**

如果当前构建目标不是 Windows x64，先在 Build Settings 切到 Windows x64（或按 Unity 提示切换）；本任务不改脚本的目标切换逻辑。

在 Unity Editor 执行菜单 `Cool/Build/Windows x64`（或用 MCP `script-execute` 调 `Cool.EditorTools.BuildScript.BuildWindows()`）。

预期日志：

```text
[Build] version 0.1.0 -> 0.1.0.<sha4>
[Build] 出包成功: ...
```

出包后运行：

```bash
grep -n "bundleVersion" ProjectSettings/ProjectSettings.asset
```

预期仍是 `bundleVersion: 0.1.0`，不是 `0.1.0.<sha4>`。本步骤必须实际跑一次 Windows 出包。

如果 Windows 出包因 `bundleVersion` 字母后缀失败，或 exe 文件属性报错：把 `BuildScript.cs` 里的 `PlayerSettings.bundleVersion = stampedVersion;` 改成 `PlayerSettings.bundleVersion = baseVersion;`（Windows 不追加 SHA），把日志改成打印 `baseVersion`，并在 UI README/Runbook 记录“Windows 显示 `v0.1.0`”。这是规格 §7 允许的退路；WebGL 的注入不受影响。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Editor/BuildVersionStamp.cs Assets/Scripts/Editor/BuildVersionStamp.cs.meta \
        Assets/Scripts/Editor/BuildScript.cs ProjectSettings/ProjectSettings.asset
git commit -m "feat: stamp git sha into windows build version"
```

---

## 任务 3：WebGL 模板缓存键与模板测试

**文件：**
- 修改：`Assets/WebGLTemplates/MobileLandscape/index.html:233-254`、`:289-290`
- 创建：`Assets/Scripts/Tests/Editor/WebGLTemplateCacheTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/WebGLTemplateCacheTests.cs`

- [ ] **步骤 1：改写模板配置段**

把：

```js
      var buildUrl = "Build";
      var loaderUrl = buildUrl + "/{{{ LOADER_FILENAME }}}";
      var config = {
        dataUrl: buildUrl + "/{{{ DATA_FILENAME }}}",
        frameworkUrl: buildUrl + "/{{{ FRAMEWORK_FILENAME }}}",
#if USE_THREADS
        workerUrl: buildUrl + "/{{{ WORKER_FILENAME }}}",
#endif
#if USE_WASM
        codeUrl: buildUrl + "/{{{ CODE_FILENAME }}}",
#endif
#if MEMORY_FILENAME
        memoryUrl: buildUrl + "/{{{ MEMORY_FILENAME }}}",
#endif
#if SYMBOLS_FILENAME
        symbolsUrl: buildUrl + "/{{{ SYMBOLS_FILENAME }}}",
#endif
        streamingAssetsUrl: "StreamingAssets",
        companyName: {{{ JSON.stringify(COMPANY_NAME) }}},
        productName: {{{ JSON.stringify(PRODUCT_NAME) }}},
        productVersion: {{{ JSON.stringify(PRODUCT_VERSION) }}},
        showBanner: unityShowBanner,
      };
```

改成：

```js
      var buildVersion = {{{ JSON.stringify(PRODUCT_VERSION) }}};
      var buildStamp = "__BUILD_STAMP__";
      if (buildStamp.charAt(0) === "_") buildStamp = String(Date.now());
      var cacheKey = buildVersion + "-" + buildStamp;
      var cacheBust = "?v=" + encodeURIComponent(cacheKey);

      var buildUrl = "Build";
      var dataUrl = buildUrl + "/{{{ DATA_FILENAME }}}" + cacheBust;
      var loaderUrl = buildUrl + "/{{{ LOADER_FILENAME }}}" + cacheBust;
      var config = {
        dataUrl: dataUrl,
        frameworkUrl: buildUrl + "/{{{ FRAMEWORK_FILENAME }}}" + cacheBust,
#if USE_THREADS
        workerUrl: buildUrl + "/{{{ WORKER_FILENAME }}}" + cacheBust,
#endif
#if USE_WASM
        codeUrl: buildUrl + "/{{{ CODE_FILENAME }}}" + cacheBust,
#endif
#if MEMORY_FILENAME
        memoryUrl: buildUrl + "/{{{ MEMORY_FILENAME }}}" + cacheBust,
#endif
#if SYMBOLS_FILENAME
        symbolsUrl: buildUrl + "/{{{ SYMBOLS_FILENAME }}}" + cacheBust,
#endif
        streamingAssetsUrl: "StreamingAssets",
        companyName: {{{ JSON.stringify(COMPANY_NAME) }}},
        productName: {{{ JSON.stringify(PRODUCT_NAME) }}},
        productVersion: cacheKey,
        cacheControl: function (url) {
          if (url === dataUrl) return "immutable";
          return url.match(/\.bundle/) ? "must-revalidate" : "no-store";
        },
        showBanner: unityShowBanner,
      };
```

- [ ] **步骤 2：背景图 URL 也拼缓存键**

在 `#if BACKGROUND_FILENAME` 块里把：

```js
      canvas.style.background = "url('" + buildUrl + "/{{{ BACKGROUND_FILENAME.replace(/'/g, '%27') }}}') center / cover";
```

改成：

```js
      canvas.style.background = "url('" + buildUrl + "/{{{ BACKGROUND_FILENAME.replace(/'/g, '%27') }}}" + cacheBust + "') center / cover";
```

- [ ] **步骤 3：编写模板静态检查测试**

```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class WebGLTemplateCacheTests
    {
        private static string TemplatePath =>
            Path.Combine(Application.dataPath, "WebGLTemplates/MobileLandscape/index.html");

        [Test]
        public void test_template_has_build_stamp_marker()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("__BUILD_STAMP__", html,
                "WebGLBuildScript 需要在产物里替换这个占位符");
        }

        [Test]
        public void test_template_builds_cache_key_from_version_and_stamp()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("var cacheKey = buildVersion + \"-\" + buildStamp;", html);
            StringAssert.Contains("var cacheBust = \"?v=\" + encodeURIComponent(cacheKey);", html);
            StringAssert.Contains("if (buildStamp.charAt(0) === \"_\") buildStamp = String(Date.now());", html);
            StringAssert.Contains("productVersion: cacheKey", html);
        }

        [Test]
        public void test_template_versioned_urls_cover_all_payloads()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("var dataUrl = buildUrl + \"/{{{ DATA_FILENAME }}}\" + cacheBust;", html);
            StringAssert.Contains("frameworkUrl: buildUrl + \"/{{{ FRAMEWORK_FILENAME }}}\" + cacheBust", html);
            StringAssert.Contains("loaderUrl = buildUrl + \"/{{{ LOADER_FILENAME }}}\" + cacheBust", html);
            StringAssert.Contains("codeUrl: buildUrl + \"/{{{ CODE_FILENAME }}}\" + cacheBust", html);
        }

        [Test]
        public void test_template_data_url_is_immutable_and_bundles_keep_default()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("if (url === dataUrl) return \"immutable\";", html);
            StringAssert.Contains("url.match(/\\.bundle/) ? \"must-revalidate\" : \"no-store\"", html);
        }
    }
}
```

- [ ] **步骤 4：运行模板测试**

运行：`bash tools/unity-tests.sh EditMode WebGLTemplateCacheTests`

预期：4 个用例 PASS。若失败，按报错补齐模板字符串，不要放宽断言。

- [ ] **步骤 5：Commit**

```bash
git add Assets/WebGLTemplates/MobileLandscape/index.html \
        Assets/Scripts/Tests/Editor/WebGLTemplateCacheTests.cs Assets/Scripts/Tests/Editor/WebGLTemplateCacheTests.cs.meta
git commit -m "feat: version webgl build urls"
```

---

## 任务 4：WebGL 出包注入、时间戳替换与产物校验

**文件：**
- 修改：`Assets/Scripts/Editor/WebGLBuildScript.cs`
- 测试：WebGL 出包日志 + `Build/WebGL/index.html` 检查

- [ ] **步骤 1：加入版本常量与命名空间**

在 `WebGLBuildScript.cs` 顶部改成：

```csharp
using System;
using System.IO;
using DataDefinition;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
```

在类里加常量：

```csharp
private const string BuildStampMarker = "__BUILD_STAMP__";
```

- [ ] **步骤 2：在 Build 里加版本守卫与注入**

在模板校验之后、`var previousCompression = ...` 之前插入：

```csharp
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
```

把设置捕获改成：

```csharp
var previousCompression = PlayerSettings.WebGL.compressionFormat;
var previousFallback = PlayerSettings.WebGL.decompressionFallback;
var previousVersion = PlayerSettings.bundleVersion;
```

把设置写入改成：

```csharp
PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
PlayerSettings.WebGL.decompressionFallback = false;
PlayerSettings.bundleVersion = stampedVersion;
```

把 `finally` 改成：

```csharp
finally
{
    // 还原项目设置（含把改动落盘）——出包的副作用必须止于出包
    PlayerSettings.WebGL.compressionFormat = previousCompression;
    PlayerSettings.WebGL.decompressionFallback = previousFallback;
    PlayerSettings.bundleVersion = previousVersion;
    AssetDatabase.SaveAssets();
}
```

在出包成功日志之后、`VerifyPayloadShape()` 之前插入：

```csharp
var buildStamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var cacheKey = stampedVersion + "-" + buildStamp;
if (!InjectBuildStamp(OutputDir + "/index.html", buildStamp))
{
    Debug.LogError("[WebGLBuild] index.html 中未找到 " + BuildStampMarker
        + "，缓存键无法注入");
    if (Application.isBatchMode) EditorApplication.Exit(1);
    return;
}

Debug.Log("[WebGLBuild] cacheKey=" + cacheKey);
VerifyPayloadShape(stampedVersion, buildStamp);
```

- [ ] **步骤 3：加时间戳注入方法**

```csharp
/// <summary>
/// 把出包时间戳写进产物 index.html 的占位符。只改产物，不改模板。
/// </summary>
private static bool InjectBuildStamp(string indexPath, string buildStamp)
{
    if (!File.Exists(indexPath)) return false;

    string html = File.ReadAllText(indexPath);
    if (!html.Contains(BuildStampMarker)) return false;

    File.WriteAllText(indexPath, html.Replace(BuildStampMarker, buildStamp));
    return true;
}
```

- [ ] **步骤 4：扩展产物校验**

把 `VerifyPayloadShape` 的签名改成：

```csharp
private static void VerifyPayloadShape(string stampedVersion, string buildStamp)
```

把原来的 index.html 检查段：

```csharp
var indexPath = OutputDir + "/index.html";
if (!File.Exists(indexPath) || !File.ReadAllText(indexPath).Contains(LandscapeGateMarker))
{
    Debug.LogError("[WebGLBuild] index.html 未包含横屏遮罩 —— 出包没有使用 "
        + TemplateSetting + " 模板");
    broken = true;
}
```

替换成：

```csharp
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
```

- [ ] **步骤 5：保存改造前的旧 index 供端到端模拟**

在出包覆盖之前，保存当前的（改造前）页面：

```bash
mkdir -p production/verify/version-cache
cp Build/WebGL/index.html production/verify/version-cache/old-index.html
```

若 `Build/WebGL/index.html` 不存在，先按现有项目状态出一次 WebGL 包再保存。这个文件只用于任务 8 的“旧页面请求无版本化资源”验证。

- [ ] **步骤 6：编译并出 WebGL 包**

在 Unity Editor 执行菜单 `Cool/Build/WebGL`。若用 MCP `script-execute` 调 `Cool.EditorTools.WebGLBuildScript.BuildWebGL()`：

- 若当前构建目标不是 WebGL，第一次调用只切换目标并返回；等域重载、重连 MCP 后再调用一次。
- 超时设为至少 600 秒。

预期日志：

```text
[WebGLBuild] version 0.1.0 -> 0.1.0.<sha4>
[WebGLBuild] 出包成功: ...
[WebGLBuild] cacheKey=0.1.0.<sha4>-<yyyyMMddHHmmss>
```

- [ ] **步骤 7：检查产物与设置还原**

```bash
grep -n "buildVersion\|buildStamp\|productVersion\|cacheControl\|?v=" Build/WebGL/index.html | head -10
grep -n "bundleVersion" ProjectSettings/ProjectSettings.asset
```

预期：

- `buildVersion = "0.1.0.<sha4>"`
- `buildStamp = "<14 位时间戳>"`，且页面中不再出现 `__BUILD_STAMP__`
- `productVersion: cacheKey`（变量引用；实际值由 `buildVersion` + `buildStamp` 在运行时拼出）
- 含 `cacheControl` 与 `?v=`
- `bundleVersion` 仍是 `0.1.0`

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Editor/WebGLBuildScript.cs
git commit -m "feat: stamp version and cache key into webgl build"
```

---

## 任务 5：StaticServer 分级缓存响应头

**文件：**
- 修改：`production/deploy/webgl-demo/StaticServer/Program.cs`
- 测试：`dotnet build` + `curl -I`

- [ ] **步骤 1：加缓存头辅助方法**

在 `Program` 类里、`WriteBody` 之前加：

```csharp
/// <summary>
/// 版本化资源长缓存；入口 HTML 与未版本化资源走 no-cache。
/// 不需要 ETag/304：静态资源靠构建时间戳生成新 URL。
/// </summary>
private static void AddCacheHeaders(
    HttpListenerRequest req, HttpListenerResponse res, string file, string ext)
{
    bool isHtml = ext.Equals(".html", StringComparison.OrdinalIgnoreCase);
    string buildRoot = Path.Combine(_root, "Build") + Path.DirectorySeparatorChar;
    bool inBuild = file.StartsWith(buildRoot, StringComparison.OrdinalIgnoreCase);
    bool versioned = inBuild && !string.IsNullOrEmpty(req.QueryString["v"]);

    res.AddHeader("Cache-Control",
        isHtml || !versioned
            ? "no-cache"
            : "public, max-age=31536000, immutable");
    res.AddHeader("Vary", "Accept-Encoding");
}
```

- [ ] **步骤 2：在 Handle 里接线**

在 `string urlPath = req.Url?.AbsolutePath ?? "/";` 后加：

```csharp
string vTag = req.QueryString["v"] ?? "-";
```

把 404 分支改成：

```csharp
if (file == null)
{
    Console.WriteLine(req.HttpMethod + " " + urlPath + " -> 404 v=" + vTag);
    res.StatusCode = 404;
    res.AddHeader("Cache-Control", "no-store");
    return;
}
```

在 `res.ContentType = type;` 后加：

```csharp
AddCacheHeaders(req, res, file, ext);
```

- [ ] **步骤 3：日志带 v，并移除重复 Vary**

206 日志改成：

```csharp
Console.WriteLine(req.HttpMethod + " " + urlPath + " 206 " + slice.Length + "B range v=" + vTag);
```

明文日志改成：

```csharp
Console.WriteLine(req.HttpMethod + " " + urlPath + " 200 " + raw.Length + "B plain v=" + vTag
    + " " + (Environment.TickCount64 - started) + "ms");
```

gzip 日志改成：

```csharp
Console.WriteLine(req.HttpMethod + " " + urlPath + " 200 " + body.Length + "B gzip"
    + " (" + size + "B raw, " + (size == 0 ? 0 : body.Length * 100 / size) + "%) v=" + vTag
    + " " + (Environment.TickCount64 - started) + "ms");
```

从 gzip 分支删掉这行（`AddCacheHeaders` 已经统一添加）：

```csharp
res.AddHeader("Vary", "Accept-Encoding");
```

- [ ] **步骤 4：404/500 不缓存**

把 catch 块改成：

```csharp
catch (Exception ex)
{
    Console.WriteLine("ERROR " + urlPath + ": " + ex.Message);
    try
    {
        res.StatusCode = 500;
        res.AddHeader("Cache-Control", "no-store");
    }
    catch { }
}
```

- [ ] **步骤 5：编译并启动 StaticServer**

```bash
dotnet build production/deploy/webgl-demo/StaticServer/StaticServer.csproj -c Release
dotnet production/deploy/webgl-demo/StaticServer/bin/Release/net8.0/StaticServer.dll --root Build/WebGL --port 8080
```

后台启动后另开一个 shell 跑下面的 curl；测完停止进程。不要用 `dotnet run`。

- [ ] **步骤 6：验收响应头**

```bash
curl -sI http://127.0.0.1:8080/ | grep -iE "cache-control|vary"
curl -sI "http://127.0.0.1:8080/Build/WebGL.wasm?v=0.1.0.cbad-test" | grep -iE "cache-control|vary"
curl -sI "http://127.0.0.1:8080/Build/WebGL.wasm" | grep -iE "cache-control|vary"
curl -sI -H "Accept-Encoding: identity" "http://127.0.0.1:8080/Build/WebGL.wasm?v=0.1.0.cbad-test" | grep -iE "cache-control|vary"
curl -sI -H "Range: bytes=0-10" "http://127.0.0.1:8080/Build/WebGL.wasm?v=0.1.0.cbad-test" | grep -iE "cache-control|vary"
```

预期：

| 请求 | Cache-Control | Vary |
|---|---|---|
| `/` | `no-cache` | `Accept-Encoding` |
| 带 `?v=` 的 wasm | `public, max-age=31536000, immutable` | `Accept-Encoding` |
| 不带 `?v=` 的 wasm | `no-cache` | `Accept-Encoding` |
| identity | 同带 `?v=` | `Accept-Encoding` |
| Range 206 | 同带 `?v=` | `Accept-Encoding` |

再把 404 的 `Cache-Control` 验一下：

```bash
curl -sI "http://127.0.0.1:8080/nope" | grep -i "cache-control"
```

预期：`no-store`。

- [ ] **步骤 7：Commit**

```bash
git add production/deploy/webgl-demo/StaticServer/Program.cs
git commit -m "fix: add cache headers to webgl static server"
```

---

## 任务 6：全局版本标签面板与 UIManager 注册

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Panel/BuildInfoPanel.cs`
- 创建：`Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs:22-27`、`:82-96`
- 创建：`Assets/Scripts/Tests/PlayMode/BuildInfoPanelTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md`
- 测试：`UIPanelRegistryTests`、`UIPanelContractTests`、`BuildInfoPanelTests`

- [ ] **步骤 1：创建面板脚本**

```csharp
using DataDefinition;
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 全局构建版本标签。挂在 Top 层，常驻右下角；不拦输入。
    /// </summary>
    public class BuildInfoPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Top;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;

        [SerializeField] private TMP_Text _versionText;

        protected override void Awake()
        {
            base.Awake();
            if (_versionText == null)
            {
                Debug.LogError("[BuildInfoPanel] _versionText 未绑定，版本号不会显示");
                return;
            }

            _versionText.text = BuildVersion.Display;
            Debug.Log("[Build] version=" + BuildVersion.Raw);
        }
    }
}
```

- [ ] **步骤 2：用 Unity MCP 创建预制体**

需要 Unity Editor 打开且 MCP 已连接，并且**不要在 PlayMode**。文件路径：`Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab`。临时对象创建在活动场景根、保存 prefab 后删除；不要 `scene-save`，避免把临时层级写进 `DemoDay`/`CharacterSelect`。若场景被标脏，先确认没有用户自己的未保存改动再决定是否恢复。

1. `gameobject-create`：`name: "BuildInfoPanel"`（场景根节点）。
2. `gameobject-component-add`：`UnityEngine.RectTransform`（若创建时没有）、`UnityEngine.CanvasGroup`、`Hotfix.GameSystems.UI.BuildInfoPanel`。
3. `gameobject-component-modify` 设置根 RectTransform：

```text
anchorMin: (0, 0)
anchorMax: (1, 1)
offsetMin: (0, 0)
offsetMax: (0, 0)
pivot: (0.5, 0.5)
```

4. `gameobject-create` 子节点：`name: "VersionText"`，`parent: "BuildInfoPanel"`。
5. `gameobject-component-add`：`TMPro.TextMeshProUGUI`。
6. 设置子 RectTransform：

```text
anchorMin: (1, 0)
anchorMax: (1, 0)
pivot: (1, 0)
anchoredPosition: (-12, 8)
sizeDelta: (320, 32)
```

7. `assets-find` 找到 `Assets/PreRes/Font/ZhunYuan SDF.asset`，用 `gameobject-component-modify` 设置 TMP：

```text
font: Assets/PreRes/Font/ZhunYuan SDF.asset
fontSize: 20
alignment: 1028 (BottomRight)
color: (1, 1, 1, 0.6)
raycastTarget: false
text: "v0.0.0"
```

7b. 给 `VersionText` 加 `UnityEngine.UI.Shadow`：`effectColor = (0, 0, 0, 0.5)`、`effectDistance = (1, -1)`，保证亮背景下可读。

8. `gameobject-component-modify` 在 `BuildInfoPanel` 上把 `_versionText` 指向 `VersionText` 的 `TextMeshProUGUI`。
9. `assets-prefab-create`：

```text
prefabAssetPath: "Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab"
gameObjectRef: BuildInfoPanel
```

10. `gameobject-destroy` 删除场景里的 `BuildInfoPanel`。
11. 若 MCP 无法添加 `RectTransform` 或 TMP 字段，改在 Unity Editor 里按同一层级和数值手动创建；下面的 PlayMode 测试会兜底。

- [ ] **步骤 3：注册并显示面板**

在 `UIManager.PanelRegistry` 末尾加：

```csharp
(typeof(BuildInfoPanel),       "Prefabs/UI/BuildInfoPanel"),
```

在 `LoadBuiltInPanels` 里、显示 HUD 之后加：

```csharp
if (_panels.TryGetValue(typeof(BuildInfoPanel), out var buildInfo))
{
    await ShowPanelAsync(buildInfo);
}
```

完整上下文应为：

```csharp
if (_panels.TryGetValue(typeof(PlayerHudPanel), out var hud))
{
    await ShowPanelAsync(hud);
}

if (_panels.TryGetValue(typeof(BuildInfoPanel), out var buildInfo))
{
    await ShowPanelAsync(buildInfo);
}

// 启动流程：先选人，再进世界（选人面板全屏覆盖 HUD）
await OpenAsync<CharacterSelectPanel>();
```

- [ ] **步骤 4：编写 PlayMode 测试**

```csharp
using DataDefinition;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace GameSys.PlayModeTests
{
    public class BuildInfoPanelTests
    {
        private GameObject _instance;

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.Destroy(_instance);
        }

        [Test]
        public void test_prefab_shows_runtime_build_version_without_blocking_input()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/BuildInfoPanel");
            Assert.IsNotNull(prefab, "Resources/Prefabs/UI/BuildInfoPanel 缺失");
            _instance = Object.Instantiate(prefab);

            var panel = _instance.GetComponent<BuildInfoPanel>();
            Assert.IsNotNull(panel, "预制体上没有 BuildInfoPanel");
            Assert.AreEqual(LayerType.Top, panel.Layer);
            Assert.AreEqual(VisibilityMode.CanvasGroup, panel.Mode);
            Assert.IsNotNull(_instance.GetComponent<CanvasGroup>(), "预制体根节点缺 CanvasGroup");

            var text = _instance.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(text, "预制体里没有 TMP 文本");
            Assert.AreEqual(BuildVersion.Display, text.text);
            Assert.IsFalse(text.raycastTarget, "版本文本不能拦截点击");
        }
    }
}
```

- [ ] **步骤 5：跑 UI 测试**

```bash
bash tools/unity-tests.sh EditMode UIPanelRegistryTests
bash tools/unity-tests.sh EditMode UIPanelContractTests
bash tools/unity-tests.sh PlayMode BuildInfoPanelTests
```

预期：全部 PASS。若 `UIPanelRegistryTests` 报“BuildInfoPanel 不在 PanelRegistry”，检查步骤 3 的注册行。

- [ ] **步骤 6：更新 UI README**

在 `Assets/Scripts/Hotfix/GameSystems/UI/README.md` 加：

```markdown
## 构建版本标签

- `BuildInfoPanel` 在 `Top` 层常驻右下角，显示 `BuildVersion.Display`：WebGL 为 `v0.1.0.<sha4>`，编辑器/Windows 为 `v0.1.0`（Windows 也带 SHA 时为 `v0.1.0.<sha4>`）。
- 预制体 `Resources/Prefabs/UI/BuildInfoPanel`；启动时由 `UIManager` 注册并显示。
- 文本 `raycastTarget = false`，根节点无 Graphic，不拦截输入；安全区由层 Canvas 的 `ScreenAdapter` 处理。
```

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/BuildInfoPanel.cs Assets/Scripts/Hotfix/GameSystems/UI/Panel/BuildInfoPanel.cs.meta \
        Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab.meta \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs \
        Assets/Scripts/Tests/PlayMode/BuildInfoPanelTests.cs Assets/Scripts/Tests/PlayMode/BuildInfoPanelTests.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/UI/README.md
git commit -m "feat: show build version in global ui corner"
```

---

## 任务 7：登录 ClientVersion 接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs:1-6`、`:162`
- 测试：编译 + 代码检查

- [ ] **步骤 1：加命名空间**

把文件头改成：

```csharp
using System;
using Cool.Protocol;
using Cool.SyncCore;
using DataDefinition;
using UnityEngine;
```

- [ ] **步骤 2：替换硬编码**

把：

```csharp
ClientVersion = "1.0.0",
```

改成：

```csharp
ClientVersion = BuildVersion.Raw,
```

- [ ] **步骤 3：编译验证**

运行：`bash tools/unity-tests.sh EditMode BuildVersionTests`

预期：PASS（这一步同时确认 `GameSys` 能引用 `DataDefinition` 且没有编译错误）。服务端当前不使用该字段，不需要新增服务端测试。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs
git commit -m "feat: report real client version on login"
```

---

## 任务 8：Runbook 同步与端到端验收

**文件：**
- 修改：`docs/superpowers/guides/webgl-demo-runbook.md`
- 证据：`production/verify/version-cache/`（新建目录，记录 curl/截图/说明）

- [ ] **步骤 1：更新 Runbook**

在 `docs/superpowers/guides/webgl-demo-runbook.md` 末尾加：

```markdown
## 版本号与缓存验证

### 升版本与出包

1. 修改 `ProjectSettings` 的 Version，保持 `a.b.c` 三段数字。含义：`a` 大版本 / 里程碑 / 不兼容改动，`b` 中等更新 / 新系统 / 新玩法，`c` 小迭代 / 修复 / 数值与表现调整。
2. 出 `Cool/Build/WebGL`（或 Windows）。出包脚本会临时把版本拼成 `a.b.c.<git sha4>`，结束后还原仓库值；构建日志会打印 `version ... -> ...` 与 `cacheKey=...`。
3. 出包后检查 `ProjectSettings.asset` 的 `bundleVersion` 仍是 `a.b.c`。

### 验证缓存键

```bash
grep -n 'var buildVersion =' Build/WebGL/index.html
grep -n 'var buildStamp =' Build/WebGL/index.html
grep -n 'var cacheBust = "?v="' Build/WebGL/index.html
```

预期：`buildVersion` 是 `0.1.0.<sha4>`，`buildStamp` 是 14 位时间戳，`cacheBust` 代码存在。实际请求里的 `?v=0.1.0.<sha4>-<yyyyMMddHHmmss>` 在 DevTools Network 里看。

浏览器 DevTools → Network：

- `WebGL.loader.js` / `WebGL.framework.js` / `WebGL.wasm` / `WebGL.data` 带同一个 `?v=`。
- `index.html` 响应头 `Cache-Control: no-cache`。
- 带 `?v=` 的 `Build/*` 响应头 `Cache-Control: public, max-age=31536000, immutable`。
- 三者都带 `Vary: Accept-Encoding`。

### 更新后的行为

- 正常刷新：先取新 `index.html`，再取新 `?v=` 资源，不会新旧混用。
- 公网 `http://<公网IP>` 没有 Cache Storage，Unity loader 的 IndexedDB 缓存会自动降级；重复加载走浏览器 HTTP 缓存（DevTools 显示 from disk/memory cache）。
- 升级前已被浏览器缓存的旧 `index.html`：第一次可能需要强制刷新一次，之后 `no-cache` 不再复发。
- 当前构建没有 Addressables 远程 catalog/bundle 或 StreamingAssets；以后加入时要另行版本化。
- 直接用 Unity Build Player（不经 `WebGLBuildScript`）时 `__BUILD_STAMP__` 不会被替换，页面每次加载生成新 key——正确但不缓存；正式出包必须走 `Cool/Build/WebGL`。
- 未来 iOS 构建不要把四段版本直接用于 `CFBundleShortVersionString`（最多三段数字）；把 SHA 放到 build number 或单独字段。
```

- [ ] **步骤 2：跑完整相关测试**

```bash
bash tools/unity-tests.sh EditMode BuildVersionTests
bash tools/unity-tests.sh EditMode WebGLTemplateCacheTests
bash tools/unity-tests.sh EditMode UIPanelRegistryTests
bash tools/unity-tests.sh EditMode UIPanelContractTests
bash tools/unity-tests.sh PlayMode BuildInfoPanelTests
```

预期：全部 PASS。

- [ ] **步骤 3：重新出 WebGL 包并发布**

在 Unity Editor 执行 `Cool/Build/WebGL`，然后：

```bash
bash production/deploy/webgl-demo/publish-server.sh
```

- [ ] **步骤 4：启动静态站点并做浏览器验收**

```bash
dotnet production/deploy/webgl-demo/StaticServer/bin/Release/net8.0/StaticServer.dll \
  --root production/deploy/webgl-demo/dist/site --port 8080
```

打开 `http://127.0.0.1:8080/?host=127.0.0.1`（手机形态加 `?touch=1`），确认：

1. 选人界面右下角显示 `v0.1.0.<sha4>`；进入游戏后仍显示。
2. DevTools Network 里四个 payload 带同一个 `?v=`，响应头符合预期。
3. 刷新页面：第二次 data 不重新下载 20 MB。本机 `127.0.0.1` 有 CacheStorage，会走 IndexedDB；公网 `http://<公网IP>` 的降级行为需要在云服务器上按 Runbook 验证，本机不伪造。
4. 保持同一 `a.b.c` 再出一次包（脏工作区）：新时间戳产生新 `?v=`；刷新后请求新资源，标签 SHA 可相同。
5. 旧页面回退验证：

```bash
cp production/verify/version-cache/old-index.html production/deploy/webgl-demo/dist/site/old-index.html
```

打开 `http://127.0.0.1:8080/old-index.html`，确认：

- Network 里 `Build/*` 不带 `?v=`；
- 响应头是 `Cache-Control: no-cache`；
- 页面能加载当前资源，没有 404，也没有新旧混用；
- 右下角标签显示当前版本（因为加载的是当前资源）。

浏览器自身缓存旧 HTML 的“第一次可能需要硬刷新”属于浏览器缓存行为，无法用这个服务端模拟完全复现；按 Runbook 的说明在真实升级后观察一次。

把 curl 输出、Network 截图和结论写到 `production/verify/version-cache/`。

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/guides/webgl-demo-runbook.md
git commit -m "docs: add version and cache verification runbook"
```

---

## 完成判据

- `BuildVersionTests`、`WebGLTemplateCacheTests`、`UIPanelRegistryTests`、`UIPanelContractTests`、`BuildInfoPanelTests` 全部通过。
- WebGL 出包日志含 `version 0.1.0 -> 0.1.0.<sha4>` 与 `cacheKey=...`；`Build/WebGL/index.html` 含版本、时间戳、`?v=`，不含 `__BUILD_STAMP__`。
- 出包后 `ProjectSettings.asset` 的 `bundleVersion` 回到 `0.1.0`。
- `curl -I` 的五条路径响应头符合任务 5 的表；404 为 `no-store`。
- 浏览器里选人界面和游戏内右下角都显示 `v0.1.0.<sha4>`；刷新后拿到新 `?v=` 资源，不出现新旧混用。
- 未提交无关文件；`Build/`、`dist/`、`Library/` 未进入 git。
