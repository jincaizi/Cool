# 版本号、构建标识与 WebGL 缓存失效设计

日期：2026-11-11
状态：**Reviewed — 已按第二轮复审修正安全上下文（公网 http 无 Cache Storage）与验收口径；待用户确认后进入实现计划。**
工作流：Superpowers brainstorming → 逐节确认 → 本 spec → 规格自检 → 用户审查 → writing-plans。
范围：版本号真值源与构建注入、全局 UGUI 版本标签、登录 `ClientVersion`、WebGL 静态站点的缓存失效与 StaticServer 响应头。

相关模块：[UI](../../../Assets/Scripts/Hotfix/GameSystems/UI/README.md)、[DataDefinition](../../../Assets/Scripts/AOT/DataDefinition/README.md)、[SyncLayer](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)、[协议](../../../Assets/Scripts/AOT/Protocol/README.md)、`Assets/Scripts/Editor/`、`production/deploy/webgl-demo/`。

## 1. 目标与范围

### 1.1 用户已确定

- 版本号格式 `a.b.c`：`a` 大版本 / 里程碑 / 不兼容改动，`b` 中等更新 / 新系统 / 新玩法，`c` 小迭代 / 修复 / 数值与表现调整。
- 构建标识取 git 短 SHA 的后 4 位，拼到版本号后，例如 `v1.2.4.cbad`。
- 显示仍是 `v1.2.4.cbad`；缓存键再追加构建时间戳（`1.2.4.cbad-20261111143000`），保证脏工作区重复出包也能失效旧缓存。
- 版本号用 UGUI 常驻显示在右下角，选人界面与游戏内都可见。
- 线上更新后浏览器**自动拿新版本**：不轮询、不弹“请刷新”提示。
- 登录握手 `ClientVersion` 改为真实版本号（`Application.version`）。
- 缓存方案：页面资源 URL 拼版本查询参数 + StaticServer 分级响应头；不重排版本目录、不做内容哈希改名。

> 示例中的 `1.2.4` 是语义示例；本仓库当前基线从 `0.1` 规范为 `0.1.0`，实际显示形如 `v0.1.0.<sha4>`。

### 1.2 非目标

- 不做版本轮询 / 更新提示弹窗。
- 不做版本目录隔离、内容哈希文件名、CDN、Service Worker。
- 不做 ETag / 304；`WebGL.data` 依靠每次构建唯一的 URL 命中浏览器缓存（HTTPS / localhost 下还有 Unity IndexedDB）。
- 不改服务端版本号，不改 `ProtocolConstants.ProtocolVersion`。
- 不覆盖 Addressables 远程 catalog/bundle 或 StreamingAssets（当前 WebGL 构建没有这些产物，见 §2）。
- 不把四段版本用于未来 iOS 构建；`CFBundleShortVersionString` 只接受最多三段数字，届时需把构建标识放到 build number 或单独字段。

### 1.3 交付边界

本规格的“完成”指：代码、预制体、模板、StaticServer、测试与 README 全部落地；§6 的 EditMode/PlayMode/curl/浏览器验收都通过。规格本身不代表实现完成。

## 2. 现状证据

| 现状 | 证据 |
|---|---|
| 版本号目前是 `bundleVersion: 0.1`，不是合法的三段格式，且全工程没有显示 | [ProjectSettings.asset:142](../../../ProjectSettings/ProjectSettings.asset#L142) |
| 运行时没有任何版本标签；HUD 只有联网读数 `NetStatsText` | [PlayerHudPanel.cs:25](../../../Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs#L25)、[PlayerHudPanel.prefab](../../../Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab) |
| WebGL 产物文件名固定，不带内容哈希 | `Build/WebGL/Build/{WebGL.loader.js,WebGL.framework.js,WebGL.wasm,WebGL.data}` |
| 页面模板把 `Build/...` 写死，`productVersion` 来自 `PRODUCT_VERSION` | [index.html:233](../../../Assets/WebGLTemplates/MobileLandscape/index.html#L233)、[index.html:253](../../../Assets/WebGLTemplates/MobileLandscape/index.html#L253) |
| StaticServer 返回响应时没有 `Cache-Control`、`ETag`、`Last-Modified` | [Program.cs:140-203](../../../production/deploy/webgl-demo/StaticServer/Program.cs#L140-L203) |
| StaticServer 的 `Vary: Accept-Encoding` 只加在 gzip 分支，明文与 Range 分支没有 | [Program.cs:178-199](../../../production/deploy/webgl-demo/StaticServer/Program.cs#L178-L199) |
| 当前 `Build/WebGL` 没有 Addressables catalog/bundle，也没有 StreamingAssets；资源全在 `WebGL.data` | `Build/WebGL` 文件列表；`Assets/StreamingAssets` 不存在 |
| Unity 2022.3 WebGL loader 自带 IndexedDB 缓存，按 `config.productVersion` 判版本并清理旧条目 | `Build/WebGL/Build/WebGL.loader.js` 的 `cacheControl` / `cleanUpCache` |
| loader 对 `dataUrl` 默认 `must-revalidate`；没有 ETag/Last-Modified 时会重新 GET 整个 data | `Build/WebGL/Build/WebGL.loader.js` 的 `cachedFetch` |
| 演示站点是明文 `http://<公网IP>:8080`，没有 HTTPS；浏览器不提供 Cache Storage，UnityCache 自动降级 | [webgl-demo-runbook.md](../guides/webgl-demo-runbook.md) 的“为什么不需要域名 / 证书 / 备案”一节 |
| framework 的 wasm URL 就是 `config.codeUrl`（`locateFile("build.wasm")`），不会再拼第二个查询串 | `Build/WebGL/Build/WebGL.framework.js` 的 `locateFile` / `wasmBinaryFile` |
| 登录 `ClientVersion` 硬编码 `"1.0.0"`，协议默认值也是 `"1.0.0"` | [ClientNetworkManager.cs:162](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs#L162)、[Login.cs:26](../../../Assets/Scripts/AOT/Protocol/Messages/Login.cs#L26) |
| UI 层级：HUD `Base(1000)`、选人 `Popup(3000)`、`Top(4000)` 可用 | [UIConst.cs:27-34](../../../Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs#L27-L34)、[CharacterSelectPanel.cs:18](../../../Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs#L18) |
| 移动端攻击按钮距右下角 `(-220, 196)`，尺寸 124×124；屏幕最右下角空闲 | [DemoDay.unity:13534](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity#L13534) |
| 每个 UI 层 Canvas 都带 `ScreenAdapter`，默认开启安全区 | [UIManager.cs:141-155](../../../Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs#L141-L155)、[ScreenAdapter.cs:9](../../../Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs#L9) |
| 已有 Windows 与 WebGL 两个出包入口 | [BuildScript.cs:25](../../../Assets/Scripts/Editor/BuildScript.cs#L25)、[WebGLBuildScript.cs:50](../../../Assets/Scripts/Editor/WebGLBuildScript.cs#L50) |

## 3. 决策摘要

| 决策 | 选择 | 理由 |
|---|---|---|
| 版本真值源 | `ProjectSettings.bundleVersion`，严格 `a.b.c` | Unity 原生；`Application.version`、WebGL 模板 `PRODUCT_VERSION`、登录 `ClientVersion` 都能直接消费，不需要额外生成文件 |
| 构建标识 | git 短 SHA 后 4 位，构建时临时拼成 `a.b.c.sha4` | 满足“显示 v1.2.4.cbad”；构建后还原仓库值，不弄脏 git |
| 缓存键 | `a.b.c.sha4-<UTC 构建时间戳>` | 脏工作区 SHA 不变时仍能失效旧缓存 |
| Unity IndexedDB 版本 | `config.productVersion = cacheKey` | 安全上下文下每次构建都清掉旧 data 缓存；公网 http 降级为 HTTP 缓存 |
| dataUrl 缓存控制 | 模板覆盖 `cacheControl`，dataUrl 返回 `immutable` | 安全上下文下避免 loader 每次重新读取 20 MB data；公网 http 由 HTTP 缓存头负责；不需要 ETag/304 |
| StaticServer | HTML `no-cache`；带 `?v=` 的 `Build/*` 长缓存；其余 `no-cache` | 新增资源强缓存，入口永远新鲜；旧页面不带 `?v=` 也不会命中陈旧资源 |
| 版本显示 | 全局 `BuildInfoPanel`，`Top` 层，右下角 | 选人界面与游戏内都可见；不依赖 HUD 生命周期 |
| 登录客户端版本 | `BuildVersion.Raw`（`Application.version`） | 服务端当前未使用该字段，不参与校验；排查问题时能看到真实版本 |
| Windows 构建 | 同样追加 SHA4，不追加时间戳 | 显示与登录版本一致；时间戳只服务 WebGL 缓存 |
| iOS 未来构建 | 不直接套四段版本 | `CFBundleShortVersionString` 最多三段数字 |

## 4. 详细设计

### 4.1 版本号与构建注入

#### 4.1.1 基版本

- 仓库唯一真值：`ProjectSettings.asset` 的 `bundleVersion`，本实现先把它从 `0.1` 规范为 `0.1.0`。
- 两个出包入口都严格校验 `^\d+\.\d+\.\d+$`；不合法直接失败，不自动猜测。
- `a/b/c` 的语义写在 [DataDefinition README](../../../Assets/Scripts/AOT/DataDefinition/README.md) 与 WebGL Runbook 里，供人工升版本时对照。

#### 4.1.2 版本解析工具

纯函数放在 `Assets/Scripts/AOT/DataDefinition/BuildVersion.cs`（AOT，可被 EditMode 测试直接引用）：

```csharp
public static class BuildVersion
{
    public static bool IsBaseVersion(string version);              // ^\d+\.\d+\.\d+$
    public static string Compose(string baseVersion, string sha4); // base + "." + sha4
    public static string Format(string raw);                       // 空值 -> "v?"；否则 "v" + raw.Trim()
    public static string Raw { get; }                              // Application.version
    public static string Display { get; }                          // Format(Raw)
}
```

git 读取放在 `Assets/Scripts/Editor/BuildVersionStamp.cs`（Editor 侧，只做进程调用）：

```csharp
public static class BuildVersionStamp
{
    public static bool TryGetGitShortSha4(out string sha4); // 失败时返回 "0000" 并 warning
}
```

- `TryGetGitShortSha4` 在项目根目录执行 `git rev-parse --short HEAD`，取输出最后 4 位；进程超时 3 秒。
- git 不可用 / 不是仓库 / 无提交：`sha4 = "0000"`，构建继续并打 warning。
- 纯函数不放进 `Assembly-CSharp-Editor`，否则 `GameSys.EditorTests` 无法引用（测试 asmdef 不引用预定义 Editor 程序集）。

#### 4.1.3 出包流程

**Windows**（[BuildScript.cs](../../../Assets/Scripts/Editor/BuildScript.cs)）：

1. 校验基版本。
2. `previous = PlayerSettings.bundleVersion`。
3. `PlayerSettings.bundleVersion = Compose(base, sha4)`。
4. `try { BuildPlayer } finally { 还原 previous; AssetDatabase.SaveAssets(); }`。

**WebGL**（[WebGLBuildScript.cs](../../../Assets/Scripts/Editor/WebGLBuildScript.cs)）：

1. 校验基版本、模板、场景（沿用现有护栏）。
2. 取 `baseVersion` 与 `sha4`，`stampedVersion = a.b.c.sha4`。
3. 临时设置 `bundleVersion = stampedVersion`；与现有 compression 设置一起放进 `try/finally`，结束时全部还原并落盘。
4. 出包成功后：
   - 生成 UTC 时间戳 `yyyyMMddHHmmss`。
   - 把 `Build/WebGL/index.html` 里的 `__BUILD_STAMP__` 替换成该时间戳；替换失败（找不到占位符）即出包失败。
   - 调 `VerifyPayloadShape` 校验版本串、时间戳、`?v=` 与横屏遮罩。
5. 日志打印 `baseVersion`、`sha4`、`cacheKey`、产物路径。

#### 4.1.4 运行时值

运行时值由 §4.1.2 的 `BuildVersion` 提供：`Raw` = `Application.version`（完整版本串），`Display` = `Format(Raw)`（带 `v` 前缀）。

| 环境 | `Application.version` | UI 显示 | 登录 `ClientVersion` |
|---|---|---|---|
| 编辑器 PlayMode | `0.1.0` | `v0.1.0` | `0.1.0` |
| Windows 构建 | `0.1.0.<sha4>` | `v0.1.0.<sha4>` | `0.1.0.<sha4>` |
| WebGL 构建 | `0.1.0.<sha4>` | `v0.1.0.<sha4>` | `0.1.0.<sha4>` |

时间戳只存在于 WebGL 的 `index.html` 缓存键里，不进入 `Application.version`、UI 与登录包。

### 4.2 页面缓存键与 Unity IndexedDB

#### 4.2.1 模板变量

修改 [index.html](../../../Assets/WebGLTemplates/MobileLandscape/index.html) 的配置段：

```js
var buildVersion = {{{ JSON.stringify(PRODUCT_VERSION) }}};   // 0.1.0.cbad
var buildStamp = "__BUILD_STAMP__";                            // WebGLBuildScript 出包后替换
if (buildStamp === "__BUILD_STAMP__") buildStamp = String(Date.now()); // 直接点 Build 的兜底
var cacheKey = buildVersion + "-" + buildStamp;                // 0.1.0.cbad-20261111143000
var cacheBust = "?v=" + encodeURIComponent(cacheKey);

var buildUrl = "Build";
var dataUrl = buildUrl + "/{{{ DATA_FILENAME }}}" + cacheBust;
var loaderUrl = buildUrl + "/{{{ LOADER_FILENAME }}}" + cacheBust;
```

- `dataUrl` / `frameworkUrl` / `loaderUrl` / `codeUrl` / 背景图 URL 都拼 `cacheBust`；`#if USE_THREADS` 等条件块里的 `workerUrl`、`memoryUrl`、`symbolsUrl` 同样处理。
- `streamingAssetsUrl` 保持 `"StreamingAssets"`；当前没有该目录，未来加入时需要另行版本化。
- `config.productVersion = cacheKey`，而不是 `buildVersion`。
- 覆盖 `cacheControl`：

```js
cacheControl: function (url) {
  if (url === dataUrl) return "immutable";
  return url.match(/\.bundle/) ? "must-revalidate" : "no-store";
}
```

**为什么需要覆盖**：loader 对 `dataUrl` 默认 `must-revalidate`；服务端不返回 ETag/Last-Modified 时，它每次加载都会重新 GET data（即使浏览器 HTTP 缓存命中，也要重新读取并写回 IndexedDB）。dataUrl 的 URL 已按构建唯一化，`immutable` 让 IndexedDB 直接命中；`.bundle` 保留 Unity 默认行为。

**安全上下文限制（重要）**：`UnityCache` 同时依赖 `indexedDB` 与 `window.caches`，后者只在 HTTPS 或 localhost 可用。当前演示形态是 `http://<公网IP>:8080`，loader 会连接失败并自动降级为普通 `fetchWithProgress`——此时 `cacheControl` 覆盖不产生副作用，正确性与重复加载性能由**浏览器 HTTP 缓存 + 版本化 URL** 保证。若以后启用 HTTPS，UnityCache 才会按 `productVersion = cacheKey` 生效并清理旧条目。

**直接点 Build Player 的兜底**：占位符未替换时用 `Date.now()`，每次加载都生成新 key——正确但完全不缓存。正式产物必须走 `WebGLBuildScript`，出包校验会拦住漏替换。

#### 4.2.2 缓存层次

| 层 | 键 | 失效方式 |
|---|---|---|
| 浏览器 HTTP 缓存 | 完整 URL（含 `?v=`） | 构建时间戳变化 → 新 URL；HTML `no-cache` |
| Unity IndexedDB（`UnityCache`） | URL + `productVersion` | `productVersion = cacheKey`，每次构建清理旧条目；**仅 HTTPS / localhost 可用**，公网 http 自动降级为普通 fetch |
| 浏览器 bfcache / 已打开页面 | 页面实例 | 刷新或重新导航；不做强制处理 |

### 4.3 StaticServer 响应头

修改 [Program.cs](../../../production/deploy/webgl-demo/StaticServer/Program.cs) 的 `Handle`：

```csharp
bool isHtml = ext.Equals(".html", StringComparison.OrdinalIgnoreCase);
string buildRoot = Path.Combine(_root, "Build") + Path.DirectorySeparatorChar;
bool inBuild = file.StartsWith(buildRoot, StringComparison.OrdinalIgnoreCase);
bool versioned = inBuild && !string.IsNullOrEmpty(req.QueryString["v"]);

res.AddHeader("Cache-Control",
    isHtml || !versioned
        ? "no-cache"
        : "public, max-age=31536000, immutable");
res.AddHeader("Vary", "Accept-Encoding");
```

| 请求 | 响应头 |
|---|---|
| `/` 或 `*.html` | `Cache-Control: no-cache` |
| `<root>/Build/*` 且 `?v=` 非空 | `Cache-Control: public, max-age=31536000, immutable` |
| 其余（含不带 `?v=` 的 `Build/*`） | `Cache-Control: no-cache` |

补充要求：

- 这段头写入要放在 200 明文、200 gzip、206 Range 三条分支共用的位置；移除 gzip 分支里现有的单独 `Vary` 添加，避免重复。
- 404 / 500 响应加 `Cache-Control: no-store`，避免缓存瞬时错误。
- 日志行带上 `v` 查询值（没有则 `-`），便于确认客户端请求的是哪次构建。
- 不实现 ETag / Last-Modified / 304。

### 4.4 UGUI 全局版本标签

新增 `Assets/Scripts/Hotfix/GameSystems/UI/Panel/BuildInfoPanel.cs`：

```csharp
public class BuildInfoPanel : UIPanel
{
    public override LayerType Layer => LayerType.Top;
    public override VisibilityMode Mode => VisibilityMode.CanvasGroup;

    [SerializeField] private TMP_Text _versionText;

    protected override void Awake()
    {
        base.Awake();
        if (_versionText != null) _versionText.text = BuildVersion.Display;
        Debug.Log("[Build] version=" + BuildVersion.Raw);
    }
}
```

文本在 `Awake` 里设置：PlayMode 实例化预制体即可断言，不依赖 `UIManager` 是否已显示面板；UIManager 只负责注册与显示。

预制体 `Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab`：

- 根节点全屏拉伸，无 Graphic，挂 `CanvasGroup`。
- 子节点 `VersionText`（TMP）：锚点 `(1,0)`、pivot `(1,0)`、`anchoredPosition (-12, 8)`、右对齐、字号约 20、半透明白色、`raycastTarget = false`、带轻微描边或阴影。
- 预制体路径加入 [UIManager.PanelRegistry](../../../Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs#L22-L27)；`LoadBuiltInPanels` 在注册后 `await ShowPanelAsync(versionPanel)`。
- `Top(4000)` 高于 HUD `Base(1000)` 与选人 `Popup(3000)`；层 Canvas 的 `ScreenAdapter` 默认套安全区。
- 移动端攻击按钮在 `(-220, 196)`，版本号贴 `(-12, 8)`，不重叠；实现时用 16:9 与 20:9 截图验收。

### 4.5 登录 ClientVersion

- [ClientNetworkManager.cs:162](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs#L162)：`ClientVersion = BuildVersion.Raw`。
- `Login.cs` 的默认值 `"1.0.0"` 保留，作为协议层兜底；不改 `ProtoMember` 编号，不改 `ProtocolVersion`。
- 服务端当前未使用该字段（只在协议里透传），不需要改服务端代码。

### 4.6 文件影响清单

**新增**：

- `Assets/Scripts/AOT/DataDefinition/BuildVersion.cs`
- `Assets/Scripts/Editor/BuildVersionStamp.cs`
- `Assets/Scripts/Hotfix/GameSystems/UI/Panel/BuildInfoPanel.cs`
- `Assets/Resources/Prefabs/UI/BuildInfoPanel.prefab`
- `Assets/Scripts/Tests/Editor/BuildVersionTests.cs`
- `Assets/Scripts/Tests/Editor/WebGLTemplateCacheTests.cs`
- `Assets/Scripts/Tests/PlayMode/BuildInfoPanelTests.cs`
- 本规格文档

**修改**：

- `ProjectSettings/ProjectSettings.asset`（`bundleVersion: 0.1` → `0.1.0`）
- `Assets/Scripts/Editor/BuildScript.cs`（Windows 追加 SHA4）
- `Assets/Scripts/Editor/WebGLBuildScript.cs`（版本注入、时间戳替换、产物校验）
- `Assets/WebGLTemplates/MobileLandscape/index.html`（缓存键、`cacheControl`、`dataUrl` 变量）
- `production/deploy/webgl-demo/StaticServer/Program.cs`（缓存头、Vary、404/500、日志）
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`（`ClientVersion`）
- `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`（注册并显示 `BuildInfoPanel`）
- `docs/superpowers/guides/webgl-demo-runbook.md`（版本与缓存验证一节）
- `Assets/Scripts/Hotfix/GameSystems/UI/README.md`、`Assets/Scripts/AOT/DataDefinition/README.md`

## 5. 失败处理与护栏

| 情况 | 行为 |
|---|---|
| `bundleVersion` 不是 `a.b.c` | 两个出包入口在 `BuildPlayer` 前失败；批处理非零退出 |
| git 不可用 | `sha4 = "0000"` + warning；时间戳仍保证缓存失效 |
| WebGL 模板占位符缺失 / 未替换 | 出包后失败，不发布 |
| 产物 `index.html` 缺版本串、时间戳或 `?v=` | `VerifyPayloadShape` 报错；批处理非零退出 |
| 出包过程抛异常 | `finally` 还原 `bundleVersion` 与 WebGL compression 设置 |
| StaticServer 无法判断是否版本化 | 默认 `no-cache`（正确性优先） |
| 旧 `index.html` 已被浏览器缓存 | 第一次可能需要强制刷新一次；之后 `no-cache` 不再复发 |

## 6. 测试与验收

### 6.1 自动化测试

| 测试 | 类型 | 断言 |
|---|---|---|
| `BuildVersionTests` | EditMode | `Format("0.1.0") == "v0.1.0"`；四段原样；空串为 `v?`；`IsBaseVersion` 接受三段、拒绝四段；`Compose("0.1.0","cbad") == "0.1.0.cbad"` |
| `WebGLTemplateCacheTests` | EditMode | 模板含 `__BUILD_STAMP__`、`cacheBust`、`cacheControl`、`dataUrl` 变量；占位符替换由出包后的 `VerifyPayloadShape` 覆盖 |
| `UIPanelRegistryTests` / `UIPanelContractTests` | EditMode | `BuildInfoPanel` 已注册、预制体存在、层与模式匹配 |
| `BuildInfoPanelTests` | PlayMode | 文本等于 `BuildVersion.Display`；`Layer == Top`；有 `CanvasGroup`；文本 `raycastTarget == false` |

命令：

```bash
bash tools/unity-tests.sh EditMode BuildVersionTests
bash tools/unity-tests.sh EditMode WebGLTemplateCacheTests
bash tools/unity-tests.sh EditMode UIPanelRegistryTests
bash tools/unity-tests.sh PlayMode BuildInfoPanelTests
```

### 6.2 出包验收

1. Unity 执行 `Cool/Build/WebGL`。
2. 构建日志含基版本、SHA4、`cacheKey`。
3. `Build/WebGL/index.html` 含 `0.1.0.<sha4>` 与注入的时间戳；不含 `__BUILD_STAMP__`；含 `?v=`。
4. 出包后 `ProjectSettings.asset` 的 `bundleVersion` 仍为 `0.1.0`。

### 6.3 StaticServer 头验收

```bash
curl -I http://127.0.0.1:8080/
curl -I "http://127.0.0.1:8080/Build/WebGL.wasm?v=0.1.0.cbad-20261111143000"
curl -I "http://127.0.0.1:8080/Build/WebGL.wasm"
curl -I -H "Accept-Encoding: identity" "http://127.0.0.1:8080/Build/WebGL.wasm?v=0.1.0.cbad-20261111143000"
curl -I -H "Range: bytes=0-10" "http://127.0.0.1:8080/Build/WebGL.wasm?v=0.1.0.cbad-20261111143000"
```

期望：

- `/` → `Cache-Control: no-cache` + `Vary: Accept-Encoding`
- 带 `?v=` 的 Build 资源（gzip 与 identity）→ `Cache-Control: public, max-age=31536000, immutable` + `Vary`
- 不带 `?v=` 的 Build 资源 → `Cache-Control: no-cache`
- 206 Range → 同样带 `Cache-Control` 与 `Vary`
- 404 → `Cache-Control: no-store`

### 6.4 浏览器端到端

1. 用 `publish-server.sh` 发布并启动 StaticServer/Server3C。
2. DevTools Network：loader / framework / wasm / data 带同一个 `?v=0.1.0.<sha4>-<timestamp>`；响应头符合 §6.3。
3. 选人界面与游戏内右下角均显示 `v0.1.0.<sha4>`。
4. 刷新页面：第二次 data 不重新下载 20 MB——localhost 下命中 IndexedDB；公网 http 下命中浏览器 HTTP 缓存（DevTools 显示 from disk/memory cache）。
5. 保持同一 `a.b.c` 再出一次包（脏工作区）：新时间戳产生新 `?v=`；刷新后请求新资源，标签 SHA 可相同。
6. 模拟升级前旧缓存：用旧 `index.html` 打开一次，确认第一次可能需要硬刷新，之后恢复正常且不再混用。

## 7. 风险与边界

- **旧缓存 HTML 的过渡期**：本方案上线前浏览器可能已缓存旧 `index.html`；第一次可能仍用旧页面。这类旧页面的 Build 请求不带 `?v=`，服务器对其回 `no-cache`，不会命中陈旧资源；刷新一次后进入新链路。
- **已打开旧页面的会话内混用**：部署后仍开着的旧页面继续请求旧 `?v=`（或旧页面不带 `?v=`），服务器会把新文件按旧 URL 返回，存在一次会话内混用的可能；刷新即恢复。彻底隔离需要版本目录（已否决）。
- **Windows 文件版本资源**：Windows 可执行文件的文件版本资源对字母后缀可能不友好；这只影响 exe 文件属性，不影响 `Application.version` 与 UI。若实测出包或文件属性报错，Windows 构建退回三段版本（显示 `v0.1.0`），WebGL 不受影响。
- **公网 http 没有 Cache Storage**：`UnityCache` 在 `http://<公网IP>` 下不可用，loader 自动降级为普通 fetch；重复加载依赖浏览器 HTTP 缓存。若切换到 HTTPS，`UnityCache` 才按 `productVersion` 生效。
- **Addressables / StreamingAssets**：当前构建没有独立 catalog/bundle 或 StreamingAssets；以后启用远程内容时，需要给 catalog/bundle 单独加版本与缓存策略，本方案不覆盖。
- **iOS 版本格式**：四段版本不能直接用于 `CFBundleShortVersionString`；未来 iOS 构建需把 SHA 移到 build number 或单独字段。
- **浏览器缓存策略差异**：Safari 对 `immutable` 支持较晚；即使忽略 `immutable`，`max-age` 仍生效，正确性不受影响。
- **直接点 Build Player**：模板兜底为每次加载生成新 key，正确但无缓存；正式产物必须走 `WebGLBuildScript`。

## 8. 规格自检

- 占位符：无 TODO / 待定；`__BUILD_STAMP__` 是设计中的构建期占位符，不是未决项。
- 内部一致性：版本串、缓存键、`productVersion`、显示文本的关系在 §4.1–§4.4 一致；时间戳只进入缓存键，不进入版本号。
- 范围：一个实现计划可覆盖（版本工具、UI、模板、StaticServer、测试、文档）。
- 模糊性：缓存规则按“解析后的文件路径”判断；`no-cache` 与 `immutable` 的适用对象明确；iOS 边界明确。
- 安全上下文：已明确公网 http 下 UnityCache 不可用，正确性由 HTTP 缓存 + 版本化 URL 保证；验收口径按 localhost/HTTPS 与公网 http 分开。
