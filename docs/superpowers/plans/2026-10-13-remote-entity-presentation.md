# 远端实体表现统一与表示分级 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 远端真人玩家与压测 bot 收敛到一条真模型渲染路径（外观目录 + 统一创建 + 行走动画驱动），并在其上建立"距离 + 每帧预算"的表示分级骨架（Full / Proxy 两档），最后用密集压测实测出预算数字。

**架构：** `RemoteEntityRegistry.CreateView` 删除 bot 分支，玩家类实体（`Player` / `PlayerBot`）统一走"外观目录查表 → 实例化 → 挂 `RemoteEntityAnimator`"；视图根下挂 `FullRoot`（模型）/ `ProxyRoot`（代理）两个表现，由每帧的 `RemoteTierPolicy`（距离迟滞 + 最近优先预算）决定启用哪一个；行走动画的速度由**渲染位移反推**（不进协议、不动插值器）。

**技术栈：** Unity 2022.3.25f1（内置渲染管线）、C#、NUnit（EditMode / PlayMode）、Unity MCP（`tests-run`，**testClass 必须用裸类名**）

**上游规格：** [2026-10-13-remote-entity-presentation-design.md](../specs/2026-10-13-remote-entity-presentation-design.md)

---

## 验证前置（每个任务都要用）

**跑 EditMode 测试**（裸类名，全限定名会返回 "No tests found"）：

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"EditMode","testClass":"<裸类名>"}'
```

**跑 PlayMode 测试**（长测试必须带 timeout）：

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --timeout 600000 \
  --input '{"testMode":"PlayMode","testClass":"<裸类名>"}'
```

**PlayMode 端到端测试需要匹配的服务器**（2026-09-12 实测：不匹配会**假绿**）：

| 测试 | 服务器 |
|---|---|
| `AoiWorldTests` | `--bots 100` |
| `MonsterWorldTests` | `--monsters 4` |
| `CombatSyncTests` / `MovementSyncTests` / `TwoClientWorldTests` | 干净服务器（无 `--bots` / `--monsters`） |

```bash
# 起服（构建后运行；> log 2>&1 & 后台）
dotnet build Server/Server3C.csproj
dotnet run --no-build --project Server/Server3C.csproj -- 8000 --bots 100 > /tmp/srv.log 2>&1 &

# 停服（netstat 的 PID 列在本沙箱不可信，会报成 4 —— 用 tasklist 取）
tasklist | grep -i server3c
taskkill //F //PID <pid>
```

**编译**：`unity-mcp-cli run-tool assets-refresh --path "E:/CodeForJob/Cool"`；编译错误用 `console-get-logs`（`logTypeFilter:Error`）。

**提交**：直落 `master`（项目既有惯例，远程只有 master）。

---

## 文件结构

### 新建

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Quality/MobileQualityBootstrap.cs` | 移动端质量档判定（纯函数）+ 启动时应用一次 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/AppearanceEntry.cs` | 外观目录条目 + 外观解析纯函数（哈希选型、查表） |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RenderSpeedEstimator.cs` | 渲染位移反推速度（钳位 + 指数平滑） |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteTierPolicy.cs` | 分档策略（`RemoteTier` 枚举 / `TierCandidate` / 距离迟滞 + 预算裁剪） |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/TierQuery.cs` | `?full=` / `?fulldist=` URL 解析纯函数 |
| `Assets/Scripts/Tests/Editor/Quality/MobileQualityBootstrapTests.cs` | 任务 1 |
| `Assets/Scripts/Tests/Editor/SyncLayer/RenderSpeedEstimatorTests.cs` | 任务 2 |
| `Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityAnimatorLocomotionTests.cs` | 任务 3 |
| `Assets/Scripts/Tests/Editor/SyncLayer/AppearanceCatalogTests.cs` | 任务 4 |
| `Assets/Scripts/Tests/Editor/SyncLayer/RemoteTierPolicyTests.cs` | 任务 6 |
| `Assets/Scripts/Tests/Editor/SyncLayer/TierQueryTests.cs` | 任务 7 |

### 修改

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | 构造签名（外观目录）、`CreateView` 合并 bot 分支、速度反推、分档 |
| 同上（`RemoteEntityView` 类在同文件） | `Tier` / `FullRoot` / `ProxyRoot` / 速度记账 / `Appearance` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs` | `ApplyLocomotion` / `InvalidateDedup` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 外观目录字段、观察者注入、overlay 读数、URL 旋钮 |
| `Server/Aoi/BotRunner.cs`、`Server/Program.cs` | `--bots-radius` |
| `Assets/Scripts/Tests/PlayMode/RemoteRenderBranchTests.cs` | **反转** bot 占位断言（detail 见任务 5） |
| `Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs:187` | 构造调用点适配 |
| `Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs` | 追加半径守护用例（任务 8） |
| `production/verify/2026-10-13-tier-sweep.md` | 手机实测记录（任务 10 产出） |

### 与 spec 的三处偏差（实现层决定，已在本文档记录）

1. **`UpdateRender` 签名不变**（spec §4 说"签名加观察者位置"）。改用注入属性 `ObserverPositionProvider`：4 个测试文件在用命名参数调 `UpdateRender`，改签名要连带动它们；属性注入还天然表达"未注入 = 全 Full"。
2. **换档断言按 `activeInHierarchy` 而不是 `Renderer.enabled`**（spec §7）。降档实现是 `FullRoot.SetActive(false)`——整棵禁用后 `Renderer.enabled` 仍是 `true`，断言写在 `SetActive` 上才成立。
3. **玩家类视图多一层包装根**（`ViewRoot` 下挂 `FullRoot` / `ProxyRoot`）。怪物视图**不加包装**（不分档，模型仍是视图根），因此 `NpcBodyColliderTests` 里 `AreSame(view.GameObject.transform, body.transform.parent)` 那条不受影响。

---

## 任务 1：移动端定档（T0）

**为什么第一个做**：当前工程 `m_PerPlatformDefaultQuality` 为空、全项目零 `QualitySettings` 调用 ⇒ **手机浏览器跑的是档 5 "Ultra"**（软阴影 / 4 级联 / shadowDistance 150 / 4 像素光 / MSAA 2× / 实时反射探针 / 蒙皮权重 4）。不定档，任务 10 的实测测到的是别的瓶颈。

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Quality/MobileQualityBootstrap.cs`
- 创建：`Assets/Scripts/Tests/Editor/Quality/MobileQualityBootstrapTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
using Hotfix.GameSystems.Sys3C.Quality;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.Quality
{
    /// <summary>T0：移动端质量档判定 + 档位参数的预算守护。</summary>
    public class MobileQualityBootstrapTests
    {
        [Test]
        public void Mobile_Device_Switches_To_The_Mobile_Level()
            => Assert.AreEqual(MobileQualityBootstrap.MobileQualityLevel,
                MobileQualityBootstrap.ResolveLevel(null, isMobileDevice: true));

        [Test]
        public void Desktop_Device_Leaves_The_Level_Alone()
            => Assert.IsNull(MobileQualityBootstrap.ResolveLevel(null, isMobileDevice: false));

        [Test]
        public void Url_Level_Wins_On_Both_Platforms()
        {
            Assert.AreEqual(5, MobileQualityBootstrap.ResolveLevel("?quality=5", isMobileDevice: true));
            Assert.AreEqual(5, MobileQualityBootstrap.ResolveLevel("?quality=5", isMobileDevice: false));
        }

        [Test]
        public void Url_Desktop_Forces_No_Change()
            => Assert.IsNull(MobileQualityBootstrap.ResolveLevel("?quality=desktop", isMobileDevice: true));

        [Test]
        public void Malformed_Url_Falls_Back_To_Detection()
            => Assert.AreEqual(MobileQualityBootstrap.MobileQualityLevel,
                MobileQualityBootstrap.ResolveLevel("?quality=abc", isMobileDevice: true));

        [Test]
        public void Mobile_Level_Stays_Inside_The_Mobile_Budget()
        {
            // 守护用例：有人把目标档改回重型配置时这条要红（照 TagManager 守护用例的范式）。
            int original = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(MobileQualityBootstrap.MobileQualityLevel);
            try
            {
                Assert.LessOrEqual(QualitySettings.shadowDistance, 30f, "移动档阴影距离必须 ≤30m");
                Assert.LessOrEqual(QualitySettings.pixelLightCount, 2, "移动档像素光 ≤2");
                Assert.AreEqual(ShadowQuality.HardOnly, QualitySettings.shadows, "移动档不跑软阴影");
                Assert.LessOrEqual(QualitySettings.skinWeights, 2, "移动档蒙皮权重 ≤2");
                Assert.LessOrEqual(QualitySettings.antiAliasing, 1, "移动档不开 MSAA");
                Assert.IsFalse(QualitySettings.realtimeReflectionProbes, "移动档不开实时反射探针");
            }
            finally
            {
                QualitySettings.SetQualityLevel(original);
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"MobileQualityBootstrapTests"}'`
预期：编译失败——`MobileQualityBootstrap` 不存在。

- [ ] **步骤 3：编写实现**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Quality
{
    /// <summary>
    /// 移动端/WebGL 的质量档（design T0）。
    ///
    /// 为什么必须做：工程 `QualitySettings.asset` 的 `m_PerPlatformDefaultQuality` 为**空**，
    /// 且全项目零 `QualitySettings` 调用 ⇒ 手机浏览器实测时跑的是当前档 5 "Ultra"
    /// （软阴影 / 4 级联 / shadowDistance 150 / 4 像素光 / MSAA 2× / 实时反射探针 / 蒙皮权重 4）。
    /// 那会让"同屏能扛多少个角色"的实测测到**别的瓶颈**（design §8-R1）。
    ///
    /// 档位取工程里已有的档 2 "Medium"（硬阴影 / 1 像素光 / shadowDistance 20 / 蒙皮权重 2 /
    /// 无 MSAA / 无实时反射探针）——**不新建档位**：改 `QualitySettings.asset` 会被编辑器回写，
    /// 运行时切换才稳。
    ///
    /// 判定顺序与 <see cref="TouchInputAvailability"/> 同源：**URL 参数 &gt; 自动判定**，
    /// 且畸形值一律忽略（现场多打一个字符的代价不能是"打不开游戏"）。
    /// </summary>
    public static class MobileQualityBootstrap
    {
        /// <summary>移动端目标档位（QualitySettings 索引：0=Very Low 1=Low 2=Medium 3=High 4=Very High 5=Ultra）</summary>
        public const int MobileQualityLevel = 2;

        /// <summary>工程里的档位数量（0..5），URL 越界值按畸形处理</summary>
        public const int MaxQualityLevel = 5;

        private const string UrlKey = "quality";

        /// <summary>
        /// 决定本次运行切到哪一档。返回 null = **不动**（保持工程当前档）。
        /// 纯函数（URL 与平台判定都由参数注入），逐条有单测。
        /// </summary>
        public static int? ResolveLevel(string? url, bool isMobileDevice)
        {
            if (UrlQuery.TryGetValue(url, UrlKey, out string raw))
            {
                string value = raw.Trim().ToLowerInvariant();
                if (value == "desktop") return null;   // 人工兜底：手机上强制不切档（对比实测用）
                if (int.TryParse(value, out int level) && level >= 0 && level <= MaxQualityLevel)
                    return level;
                // 畸形值落到自动判定——与 TouchInputAvailability 的"URL 优先但不阻塞"同源
            }

            return isMobileDevice ? MobileQualityLevel : (int?)null;
        }

        /// <summary>
        /// 平台判定。`Application.isMobilePlatform` 在 WebGL 下**可能不准**（项目已有实锤：
        /// iPad 默认桌面视图会返回 false，见 TouchInputAvailability 的注释），叠加
        /// `SystemInfo.deviceType` 兜底。出包产物没有 Inspector，唯一的现场开关是 `?quality=`。
        /// </summary>
        public static bool IsMobileDevice()
            => Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld;

        /// <summary>启动时应用一次（不依赖任何场景接线——Launch / CharacterSelect / DemoDay 都覆盖）</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            int? level = ResolveLevel(Application.absoluteURL, IsMobileDevice());
            if (level == null || level.Value == QualitySettings.GetQualityLevel()) return;

            QualitySettings.SetQualityLevel(level.Value, applyExpensiveChanges: true);
            Debug.Log($"[Quality] 移动端定档：切到档 {level.Value}");
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"MobileQualityBootstrapTests"}'`
预期：6 条全 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Quality/ Assets/Scripts/Tests/Editor/Quality/ Assets/Scripts/Tests/Editor/Quality.meta Assets/Scripts/Hotfix/GameSystems/Sys3C/Quality.meta
git commit -m "feat(quality): pin a mobile quality level for the WebGL demo

手机浏览器此前跑的是档 5 Ultra（shadowDistance 150 / 4 级联 / 4 像素光 /
MSAA 2x / 实时反射探针 / 蒙皮权重 4）——m_PerPlatformDefaultQuality 为空
且全项目零 QualitySettings 调用。分级骨架的实测必须先定档，否则测到的是
别的瓶颈（design T0 / R1）。"
```

---

## 任务 2：渲染位移反推速度

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RenderSpeedEstimator.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/RenderSpeedEstimatorTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（`RemoteEntityView` 增记账 + `UpdateRender` 循环里推一步）

- [ ] **步骤 1：编写失败的测试**

```csharp
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// D17：速度由**渲染位移反推**（动画要匹配的是屏幕上的实际移动），不穿协议。
    /// 两条工程约束各有用例：钳位（瞬移不让导数爆掉）、平滑（快照 15Hz 的台阶）。
    /// </summary>
    public class RenderSpeedEstimatorTests
    {
        [Test]
        public void Step_Returns_Zero_Delta_Wins_As_Keep()
        {
            // 首帧（dt<=0）保持上一值——新建视图不该凭空冒出速度
            Assert.AreEqual(0f, RenderSpeedEstimator.Step(0f, Vector3.zero, new Vector3(9f, 0f, 0f), 0f, 12f));
            Assert.AreEqual(3f, RenderSpeedEstimator.Step(3f, Vector3.zero, new Vector3(9f, 0f, 0f), -1f, 12f));
        }

        [Test]
        public void Constant_Motion_Converges_To_The_True_Speed()
        {
            float speed = 0f;
            // 8 m/s 直线移动，每帧 66ms（服务器 tick），跑 1s（15 帧）——平滑应基本收敛
            for (int i = 0; i < 15; i++)
                speed = RenderSpeedEstimator.Step(speed,
                    new Vector3(i * 0.528f, 0f, 0f), new Vector3((i + 1) * 0.528f, 0f, 0f), 0.066f, 12f);
            Assert.That(speed, Is.EqualTo(8f).Within(0.6f), "匀速 8 m/s 应收敛到 8（平滑只该延迟，不该改终值）");
        }

        [Test]
        public void Teleport_Is_Clamped()
        {
            // 重生/服务器校正：一帧内瞬移 100m ⇒ 原始导数约 1500 m/s，必须被钳住
            float speed = RenderSpeedEstimator.Step(0f, Vector3.zero, new Vector3(100f, 0f, 0f), 0.066f, 12f);
            Assert.LessOrEqual(speed, 12f, "瞬移的导数必须钳到 maxSpeed（否则 Blend 会跳到跑档）");
        }

        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SpeedTestRoot");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Clear();
            Object.DestroyImmediate(_root);
        }

        private static EntitySnapshot Snap(long id, float x, long tick)
            => new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = x, Y = 0f, Z = 0f, Tick = (uint)tick
            };

        [Test]
        public void Registry_Derives_Speed_From_Rendered_Motion()
        {
            // 两个样本把渲染时刻夹在中间（**必须插值**：两次渲染都落在最后一个样本之后的话，
            // 位置会保持不动 ⇒ 反推速度为 0，这条用例就测不到东西了）。
            // 样本 A=(1_000_000, x=0) 样本 B=(2_320_000, x=1.056) ⇒ 1.056m / 0.132s = 8 m/s。
            // 渲染时刻取 1_660_000（t=0.5，x=0.528）与 1_990_000（t=0.75，x=0.792）：
            // Δx=0.264m / Δt=0.033s = 8 m/s。
            _registry.ApplySnapshot(Snap(1, 0f, 0), serverTicks: 1_000_000L);
            _registry.ApplySnapshot(Snap(1, 1.056f, 1), serverTicks: 2_320_000L);

            _registry.UpdateRender(nowServerTicks: 1_660_000L, renderDelayTicks: 0L);
            _registry.UpdateRender(nowServerTicks: 1_990_000L, renderDelayTicks: 0L);

            var view = _registry.GetView(1)!;
            Assert.That(view.Speed, Is.GreaterThan(1f), "渲染位移 8 m/s ⇒ 反推速度必须非零");
            Assert.LessOrEqual(view.Speed, 12f, "任何情况下不超过钳位上限");
        }

        [Test]
        public void Stationary_Entity_Has_Zero_Speed()
        {
            _registry.ApplySnapshot(Snap(2, 5f, 0), serverTicks: 1_000_000L);
            _registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);
            _registry.UpdateRender(nowServerTicks: 2_000_000L, renderDelayTicks: 0L);

            Assert.AreEqual(0f, _registry.GetView(2)!.Speed, "位置不动 ⇒ 速度 0（Idle）");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`... --input '{"testMode":"EditMode","testClass":"RenderSpeedEstimatorTests"}'`
预期：编译失败——`RenderSpeedEstimator` 不存在、`RemoteEntityView.Speed` 不存在。

- [ ] **步骤 3：实现 `RenderSpeedEstimator`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 渲染位移反推速度（design D17）。
    ///
    /// 为什么不取协议里的 `Speed`：动画要匹配的是**屏幕上实际的移动**，而渲染位移的导数
    /// 就是这个量；服务端上报的是它的意图（怪物的 `Moving` 标志换算），可能与渲染有偏差。
    /// 顺带免掉穿协议、改 `AddSample` 签名、改插值器测试。
    ///
    /// 两条工程约束：
    ///   · **钳位**：重生/服务器校正会让位置瞬移，导数爆掉 → 钳到 `maxSpeed`（调用方传 1.5×SprintSpeed）；
    ///   · **平滑**：快照 15Hz ⇒ 位移导数呈台阶，指数平滑（时间常数 0.15s）磨平；平滑只延迟，不改终值。
    /// </summary>
    public static class RenderSpeedEstimator
    {
        /// <summary>服务器时钟换算：1ms = 10_000 tick（与 `ClientNetworkManager` 换算 renderDelay 同基准）</summary>
        public const float TicksPerSecond = 10_000_000f;

        /// <summary>平滑时间常数（秒）</summary>
        public const float SmoothingTau = 0.15f;

        /// <summary>
        /// 推进一步。`deltaSeconds &lt;= 0`（首帧 / 时钟未推进）时**保持**上一值。
        /// </summary>
        public static float Step(float previous, Vector3 from, Vector3 to, float deltaSeconds, float maxSpeed)
        {
            if (deltaSeconds <= 0f) return previous;

            float raw = (to - from).magnitude / deltaSeconds;
            if (raw > maxSpeed) raw = maxSpeed;

            float alpha = 1f - Mathf.Exp(-deltaSeconds / SmoothingTau);
            return previous + (raw - previous) * alpha;
        }
    }
}
```

- [ ] **步骤 4：在 `RemoteEntityView` 上加记账，在渲染循环里推一步**

`RemoteEntityView` 类的字段区（`Position` / `Rotation` 属性旁边）加：

```csharp
        /// <summary>平滑后的渲染速度（m/s）——由相邻两次插值位置反推（design D17）</summary>
        public float Speed { get; private set; }

        /// <summary>上一次渲染写下的位置（速度反推用）</summary>
        private Vector3 _lastRenderPosition;

        /// <summary>上一次渲染的服务器时刻（&lt; 0 = 尚无，首帧保持上一速度）</summary>
        private long _lastRenderTicks = -1L;

        /// <summary>速度反推 + 记账（registry 的渲染循环每帧调用一次）</summary>
        internal void TrackSpeed(Vector3 position, long renderAt, float maxSpeed)
        {
            float dt = _lastRenderTicks < 0
                ? 0f
                : (renderAt - _lastRenderTicks) / RenderSpeedEstimator.TicksPerSecond;
            Speed = RenderSpeedEstimator.Step(Speed, _lastRenderPosition, position, dt, maxSpeed);
            _lastRenderPosition = position;
            _lastRenderTicks = renderAt;
        }
```

`RemoteEntityRegistry.UpdateRender` 的位置写入处（`kv.Value.Position = pos; kv.Value.Rotation = rot;` 之后）插入：

```csharp
                    // D17：速度由**渲染位移**反推（动画要匹配屏幕上的移动，且免动协议/插值器）。
                    // 钳位上限 = 1.5 × 冲刺速度：重生/服务器校正的瞬移不该让 Blend 跳到跑档。
                    kv.Value.TrackSpeed(pos, renderAt, SpeedClamp);
```

并在字段区加常量：

```csharp
        /// <summary>速度反推的钳位上限（1.5 × 本地冲刺速度 8.0 —— design §4 速度反推）</summary>
        private const float SpeedClamp = 12f;
```

- [ ] **步骤 5：运行测试验证通过**

运行：`... --input '{"testMode":"EditMode","testClass":"RenderSpeedEstimatorTests"}'`
预期：5 条全 PASS。

再跑一次既有回归：`... --input '{"testMode":"EditMode","testClass":"RemoteEntityRegistryTests"}'` 与 `RemoteInterpolatorTests`——预期保持绿（本任务不动插值语义）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RenderSpeedEstimator.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/RenderSpeedEstimatorTests.cs
git commit -m "feat(sync): derive remote entity speed from rendered motion

动画要匹配的是屏幕上的实际位移，渲染位移的导数就是这个量（服务端上报的
Speed 是它的意图）。钳位 1.5x 冲刺速度挡瞬移，0.15s 指数平滑磨平 15Hz
快照的台阶。不穿协议、不动插值器（design D17）。"
```

---

## 任务 3：行走动画驱动

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityAnimatorLocomotionTests.cs`

**契约**（来自本地路径，见 [FSMManager.cs:85-93](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs)）：`Blend` = Idle/Walk/Sprint 混合值（本地写 0 / 0.5 / 1）；`LocomotionSpeed` = **步频倍率**（本地举盾写 0.4，注释"同步放慢走步循环，避免脚步滑步"）；`BaseState` = 0=Idle / 7=Locomotion。

- [ ] **步骤 1：编写失败的测试**

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 远端行走动画驱动（design §4 行走动画映射）。
    /// 本地是**输入驱动**（0/0.5/1 离散），远端是**速度驱动**（过阈值 + 按比例调步频）——
    /// 同一动作取值可能有细微差异，这是有意的（远端没有输入，只有渲染位移）。
    /// </summary>
    public class RemoteEntityAnimatorLocomotionTests
    {
        private GameObject _go = null!;
        private RemoteEntityAnimator _driver = null!;
        private const string TempControllerPath = "Assets/Tests_TempLocomotion.controller";

        [SetUp]
        public void SetUp()
        {
            var controller = UnityEditor.Animations.AnimatorController
                .CreateAnimatorControllerAtPath(TempControllerPath);
            foreach (var (name, type) in new[]
            {
                ("Blend", AnimatorControllerParameterType.Float),
                ("LocomotionSpeed", AnimatorControllerParameterType.Float),
                ("BaseState", AnimatorControllerParameterType.Int),
                ("HitState", AnimatorControllerParameterType.Int),
            })
                controller.AddParameter(name, type);

            _go = new GameObject("LocomotionDriverTest");
            var animator = _go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            _driver = _go.AddComponent<RemoteEntityAnimator>();
            // EditMode 下 AddComponent 不触发生命周期——必须显式解析一次（与 NpcEntityAnimatorTests 同惯例；
            // 该方法已是 internal，测试程序集有 InternalsVisibleTo）
            _driver.ResolveAnimator();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            UnityEditor.AssetDatabase.DeleteAsset(TempControllerPath);
        }

        [Test]
        public void Stationary_Maps_To_Idle_And_Keeps_The_Cycle_Running()
        {
            _driver.ApplyLocomotion(0f);
            Assert.AreEqual(0f, _driver.LastBlend, "静止 ⇒ Blend=0（Idle）");
            Assert.AreEqual(1f, _driver.LastLocomotionSpeed,
                "静止必须写 1 而不是 0——写 0 会把 idle 冻成静止帧");
        }

        [Test]
        public void Bot_Speed_At_8_mps_Maps_To_Sprint_With_Matched_Foot_Speed()
        {
            // bot 实测 0.53 m/tick ÷ 0.066s ≈ 8.03 m/s（标称跑速 8.0）⇒ Blend=1、步频≈1
            _driver.ApplyLocomotion(8.03f);
            Assert.AreEqual(1f, _driver.LastBlend, "≥ 走跑中点(6.5) ⇒ Blend=1（跑）");
            Assert.That(_driver.LastLocomotionSpeed, Is.EqualTo(1.004f).Within(0.02f),
                "步频 = 速度 ÷ 标称跑速（8.0）——不按比例调就是溜冰");
        }

        [Test]
        public void Walk_Speed_Scales_The_Cycle_Rate()
        {
            // 慢速移动：Blend 落到走档（0.5），步频按 速度 ÷ 标称走速(5.0) 缩放
            _driver.ApplyLocomotion(2.5f);
            Assert.AreEqual(0.5f, _driver.LastBlend, "低于走跑中点 ⇒ 走档");
            Assert.AreEqual(0.5f, _driver.LastLocomotionSpeed, "2.5 ÷ 5.0 = 0.5 步频");
        }

        [Test]
        public void Cycle_Rate_Has_A_Floor()
        {
            // 极慢速（0.2 m/s 仍算移动）：比例为 0.04 ⇒ 必须钳到下限，否则腿几乎不动
            _driver.ApplyLocomotion(0.2f);
            Assert.AreEqual(0.5f, _driver.LastBlend);
            Assert.AreEqual(0.3f, _driver.LastLocomotionSpeed, "下限 0.3");
        }

        [Test]
        public void Parameters_Actually_Land_On_The_Animator()
        {
            _driver.ApplyLocomotion(8.03f);
            var animator = _go.GetComponent<Animator>()!;
            Assert.AreEqual(1f, animator.GetFloat("Blend"), "Blend 必须真的写进控制器");
            Assert.AreEqual(7, animator.GetInteger("BaseState"), "移动态 = Locomotion(7)");
        }

        [Test]
        public void Invalidate_Dedup_Forces_A_Rewrite_After_A_Tier_Promotion()
        {
            _driver.ApplyLocomotion(0f);
            _go.GetComponent<Animator>()!.SetFloat("Blend", 99f);   // 模拟禁用期状态被重置
            _driver.InvalidateDedup();
            _driver.ApplyLocomotion(0f);
            Assert.AreEqual(0f, _go.GetComponent<Animator>()!.GetFloat("Blend"),
                "升档后必须强制重写一次（Animator 禁用即重置状态，去重游标不能拦它）");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`... --input '{"testMode":"EditMode","testClass":"RemoteEntityAnimatorLocomotionTests"}'`
预期：编译失败——`ApplyLocomotion` / `LastBlend` / `InvalidateDedup` / `ResolveAnimatorForTest` 不存在。

- [ ] **步骤 3：在 `RemoteEntityAnimator` 上实现**

在类里加常量、观测属性、平滑状态：

```csharp
        /// <summary>标称走/跑速度（m/s）——**必须与本地 CharacterController.MoveSpeed/SprintSpeed 一致**，
        /// 由 `RemoteEntityAnimatorLocomotionTests` 的守护用例钉住。</summary>
        public const float NominalWalkSpeed = 5f;
        public const float NominalSprintSpeed = 8f;
        /// <summary>走/跑档的分界（两个标称速度的中点）</summary>
        public const float WalkRunSplitSpeed = 6.5f;
        /// <summary>低于这个速度算静止（避免浮点噪声把 Idle 抖成走）</summary>
        public const float IdleSpeedThreshold = 0.1f;
        /// <summary>步频下限——速度趋 0 时比例为 0 会把动画冻成静止帧</summary>
        public const float MinLocomotionSpeed = 0.3f;
        /// <summary>步频上限（防御：反推速度已被钳过，这里只做兜底）</summary>
        public const float MaxLocomotionSpeed = 1.5f;

        /// <summary>最近一次写出的混合值（观察面——测试与调试读数用，与 LastHitState 同惯例）</summary>
        public float LastBlend { get; private set; }
        /// <summary>最近一次写出的步频倍率（观察面）</summary>
        public float LastLocomotionSpeed { get; private set; } = 1f;
        /// <summary>最近一次写出的 Base 层状态（观察面）</summary>
        public int LastBaseState { get; private set; }

        private int _lastLocomotionKey = int.MinValue;   // 去重：参数没变不重写
```

方法：

```csharp
        /// <summary>
        /// 行走动画（design §4 行走动画映射；速度由 registry 从渲染位移反推后传入）。
        ///
        /// 三个参数的分工照抄本地契约（`FSMManager.UpdateBlendParameter` / `CharacterController`）：
        ///   · `Blend` = Idle/Walk/Sprint 混合值（0 / 0.5 / 1）；
        ///   · `LocomotionSpeed` = **步频倍率** = 速度 ÷ 该档标称速度 —— 不按比例调就是脚步溜冰；
        ///   · `BaseState` = 0(Idle) / 7(Locomotion)。
        /// </summary>
        public void ApplyLocomotion(float speed)
        {
            float blend = speed < IdleSpeedThreshold ? 0f
                : speed < WalkRunSplitSpeed ? 0.5f
                : 1f;

            float nominal = blend >= 1f ? NominalSprintSpeed : NominalWalkSpeed;
            float rate = blend <= 0f
                ? 1f                                                   // 静止：写 1（写 0 会冻住 idle）
                : Mathf.Clamp(speed / nominal, MinLocomotionSpeed, MaxLocomotionSpeed);

            int baseState = blend <= 0f
                ? (int)BaseState.Idle
                : (int)BaseState.Locomotion;

            LastBlend = blend;
            LastLocomotionSpeed = rate;
            LastBaseState = baseState;

            int key = Mathf.RoundToInt(blend * 1000f) * 100_000 + Mathf.RoundToInt(rate * 1000f) * 10 + baseState;
            if (key == _lastLocomotionKey) return;
            _lastLocomotionKey = key;

            if (_animator == null) return;   // 无控制器：只记观察值（与 Apply 同款守卫）

            _animator.SetFloat(AnimHashes.Blend, blend);
            _animator.SetFloat(AnimHashes.LocomotionSpeed, rate);
            _animator.SetInteger(AnimHashes.BaseState, baseState);
        }

        /// <summary>
        /// 让去重游标失效：下一次写入必定落到 Animator。
        /// **升档时必须调用**——角色预制体的 `m_KeepAnimatorStateOnDisable` 是 0（禁用即重置状态），
        /// 从 Proxy 档升回 Full 档时 Animator 已回到默认状态，去重游标会把"该重写"误判成"没变"。
        /// </summary>
        public void InvalidateDedup()
        {
            _lastKey = -1;                 // 受击层去重游标（Apply 用）
            _lastLocomotionKey = int.MinValue;
        }
```

（`ResolveAnimator()` 已是 `internal`（既有），测试程序集有 `InternalsVisibleTo("GameSys.EditorTests")`，直接调即可，不需要新加包装方法。）

`Apply` 现有实现里那句 `if (key == _lastKey) return;` 要能被打断：确保 `InvalidateDedup` 把 `_lastKey` 设为 `-1`（类注释里写了"-1 不在取值域内 ⇒ 首个快照必定应用一次"——沿用这个哨兵）。

顶部补 `using Hotfix.GameSystems.Sys3C.Character;`（`BaseState` 枚举在 `Sys3C/Character/CharacterData.cs`）与 `using Hotfix.GameSystems.Sys3C.Animation;`（`AnimHashes`）。

- [ ] **步骤 4：运行测试验证通过**

运行：`... --input '{"testMode":"EditMode","testClass":"RemoteEntityAnimatorLocomotionTests"}'`
预期：6 条全 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityAnimatorLocomotionTests.cs
git commit -m "feat(sync): drive remote locomotion from derived speed

Blend 按 0/0.5/1 映射（分界取走跑标称速度中点 6.5），LocomotionSpeed 按
速度/标称速度缩放并钳在 [0.3, 1.5]——写死 1 会溜冰、写 0 会冻住 idle。
升档时 InvalidateDedup 强制重写（KeepAnimatorStateOnDisable=0，禁用即重置）。"
```

---

## 任务 4：外观目录与解析

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/AppearanceEntry.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/AppearanceCatalogTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>外观目录（design §4 外观解析顺序）：查表 + bot 的确定性哈希选型。</summary>
    public class AppearanceCatalogTests
    {
        private static List<AppearanceEntry> TwoEntries()
        {
            var male = new GameObject("MaleDouble");
            var female = new GameObject("FemaleDouble");
            return new List<AppearanceEntry>
            {
                new AppearanceEntry { Id = 0, Full = male },
                new AppearanceEntry { Id = 1, Full = female },
            };
        }

        [Test]
        public void EntryAt_Returns_The_Matching_Entry()
        {
            var entries = TwoEntries();
            try
            {
                Assert.AreEqual("MaleDouble", AppearanceResolver.EntryAt(entries, 0)!.Full!.name);
                Assert.AreEqual("FemaleDouble", AppearanceResolver.EntryAt(entries, 1)!.Full!.name);
            }
            finally { foreach (var e in entries) Object.DestroyImmediate(e.Full); }
        }

        [Test]
        public void EntryAt_Miss_Returns_Null_For_The_Caller_To_Degrade()
        {
            var entries = TwoEntries();
            try
            {
                Assert.IsNull(AppearanceResolver.EntryAt(entries, 99), "越界必须返回 null（调用方走胶囊降级）");
                Assert.IsNull(AppearanceResolver.EntryAt(entries, -1), "负数同样安全");
                Assert.IsNull(AppearanceResolver.EntryAt(null, 0), "未接线同样返回 null");
            }
            finally { foreach (var e in entries) Object.DestroyImmediate(e.Full); }
        }

        [Test]
        public void FindId_Locates_The_Default_Player_Appearance()
        {
            var entries = TwoEntries();
            try
            {
                Assert.AreEqual("MaleDouble", AppearanceResolver.FindId(entries, 0)!.Full!.name);
                Assert.IsNull(AppearanceResolver.FindId(entries, 99), "没有该 Id 的条目 ⇒ null（调用方降级）");
                Assert.IsNull(AppearanceResolver.FindId(null, 0), "未接线 ⇒ null");
            }
            finally { foreach (var e in entries) Object.DestroyImmediate(e.Full); }
        }

        [Test]
        public void Bot_Appearance_Is_Deterministic()
        {
            // 同一个 bot 每次进视野必须长得一样——随机选会"换脸"
            for (long id = 1; id <= 50; id++)
                Assert.AreEqual(AppearanceResolver.BotIndex(id, 2), AppearanceResolver.BotIndex(id, 2),
                    $"bot {id} 的外观索引必须稳定");
        }

        [Test]
        public void Bot_Appearance_Spreads_Across_Entries()
        {
            // 连续 id 必须打散：直接取模会得到 0,1,0,1…（一眼假），乘法哈希不会
            var seen = new HashSet<int>();
            for (long id = 1; id <= 20; id++) seen.Add(AppearanceResolver.BotIndex(id, 2));
            Assert.AreEqual(2, seen.Count, "连续 id 应覆盖全部条目");

            int firstHalf = 0;
            for (long id = 1; id <= 10; id++) firstHalf += AppearanceResolver.BotIndex(id, 2);
            Assert.That(firstHalf, Is.InRange(3, 7), "前 10 个 id 不该全部落在同一条目");
        }

        [Test]
        public void Bot_Index_Handles_Degenerate_Catalogs()
        {
            Assert.AreEqual(0, AppearanceResolver.BotIndex(7, 0), "空目录不崩（返回 0，调用方走降级）");
            Assert.AreEqual(0, AppearanceResolver.BotIndex(7, 1), "只有一条 ⇒ 恒 0（统一外观）");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`... --input '{"testMode":"EditMode","testClass":"AppearanceCatalogTests"}'`
预期：编译失败——`AppearanceEntry` / `AppearanceResolver` 不存在。

- [ ] **步骤 3：实现**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 外观目录的一条（Inspector 可编辑）。**不能换成 `Dictionary`**：Unity 不序列化字典，
    /// 场景里配的值会静默丢光（与 `ClientNetworkHost.NpcPrefabEntry` 同一条教训）。
    /// </summary>
    [Serializable]
    public class AppearanceEntry
    {
        /// <summary>外观 id（0 = 默认外观：真人玩家在协议带上外观 id 之前走它）</summary>
        public int Id;

        /// <summary>近档（Full）：完整模型预制体。留空 ⇒ 该条目退化为纯代理</summary>
        public GameObject? Full;

        /// <summary>远档（Proxy）代理预制体。**留空 = 运行时生成胶囊**——将来美术产出
        /// billboard/impostor 时填进来即可，换档机制一行都不用改</summary>
        public GameObject? Proxy;
    }

    /// <summary>外观解析（纯函数，便于单测）：实体 → 预制体。</summary>
    public static class AppearanceResolver
    {
        /// <summary>默认外观 id（真人）</summary>
        public const int DefaultId = 0;

        /// <summary>
        /// bot 的外观条脚下标（`EntityKind.PlayerBot`）。
        ///
        /// **乘法哈希后取模，不是 `playerId % count`**：bot id 是连续分配的，取模会让
        /// 男女规律交替（0,1,0,1…），一眼假。哈希保证**确定性**——同一个 bot 每次进视野
        /// 长得一样（随机选会换脸）。
        /// </summary>
        public static int BotIndex(long playerId, int entryCount)
        {
            if (entryCount <= 1) return 0;
            ulong mixed = (ulong)playerId * 2654435761UL;   // Knuth 乘法混合
            return (int)((mixed >> 16) % (ulong)entryCount);
        }

        /// <summary>
        /// 按 id 查表（真人走这条：`Id = DefaultId`）。未接线 / 未命中 ⇒ null，调用方走胶囊降级。
        /// </summary>
        public static AppearanceEntry? FindId(IReadOnlyList<AppearanceEntry>? entries, int id)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].Id == id) return entries[i];
            return null;
        }

        /// <summary>取第 index 条（bot 走这条：索引由 <see cref="BotIndex"/> 算出；越界返回 null）</summary>
        public static AppearanceEntry? EntryAt(IReadOnlyList<AppearanceEntry>? entries, int index)
        {
            if (entries == null || index < 0 || index >= entries.Count) return null;
            return entries[index];
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`... --input '{"testMode":"EditMode","testClass":"AppearanceCatalogTests"}'`
预期：5 条全 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/AppearanceEntry.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/AppearanceCatalogTests.cs
git commit -m "feat(sync): add the remote appearance catalog

Id → {Full, Proxy} 的 Inspector 数组（沿用 NpcPrefabEntry 范式：字典不被
场景序列化）。bot 用乘法哈希选条目——确定性（同 id 恒定同外观）且不会像
取模那样男女规律交替；Proxy 留空 = 运行时胶囊，将来换 impostor 不改代码。"
```

---

## 任务 5：统一创建路径（bot 并入真人分支）

**这一步会**反转**一条既有测试：`RemoteRenderBranchTests.Real_Player_Gets_A_Character_Bot_Gets_A_Placeholder` 断言 `bot 必须是占位`——它守的是 D-P5-7，而本设计按实测（同屏峰值 5~6 个，AOI 本来就按住了数量）推翻了那个前提。**

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（构造签名 + `CreateView`）
- 修改：`Assets/Scripts/Tests/PlayMode/RemoteRenderBranchTests.cs`（反转用例 + 类注释）
- 修改：`Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs:187`（调用点）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:126`（调用点，本任务先用旧字段传一条默认条目，任务 7 换成真实目录）

- [ ] **步骤 1：改既有测试（反转 + 新用例）**

`RemoteRenderBranchTests` 的类注释整段替换：

```csharp
    /// <summary>
    /// 远端实体按 `EntityKind` 分支机构（N5b → 2026-10-13 统一）。
    ///
    /// **反转记录**：这里原先钉的是"bot 必须是胶囊占位"（D-P5-7），理由是"100 个蒙皮网格会
    /// 压垮手机浏览器"。该前提已被实测推翻——AOI（80m 视野）之后客户端的同屏实体数由
    /// **局部密度**决定，`--bots 100` 撒在 ±400m 时的实测峰值只有 5~6 个
    /// （production/verify/t12、t13 的 AoiWorldTests 输出），客户端从来没有同时渲染过 100 个。
    /// 所以 bot 与真人合并到同一条真模型路径（design D1/D6），数量由分档骨架的**每帧预算**兜底。
    ///
    /// 用例自带一个"角色预制体"（运行时 GameObject + Animator）而不是去磁盘上取真预制体：
    /// PlayMode 测试程序集在非 Editor 平台也要能跑，不能引用 `UnityEditor.AssetDatabase`。
    /// 这里要钉的是**路径契约**——两种 Kind 拿到的是同一种东西，而不是某个具体预制体的身份。
    /// </summary>
```

`SetUp` 改为用外观目录（同时保留 `_avatarPrefab` 给"未接线"用例）：

```csharp
            _registry = new RemoteEntityRegistry(_root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = _avatarPrefab } });
```

反转用例替换为：

```csharp
        [UnityTest]
        public IEnumerator Real_Player_And_Bot_Take_The_Same_Model_Path()
        {
            // 两个快照走同一条 ApplySnapshot：只有 Kind 不同
            var real = Snap(playerId: 1, kind: (byte)EntityKind.Player);
            var bot = Snap(playerId: 2, kind: (byte)EntityKind.PlayerBot);

            _registry.ApplySnapshot(real, 0);
            _registry.ApplySnapshot(bot, 0);
            yield return null;

            var realView = _registry.GetView(1)!;
            var botView = _registry.GetView(2)!;

            Assert.IsNotNull(realView.GameObject.GetComponentInChildren<Animator>(true),
                "真人必须拿到带 Animator 的角色");
            Assert.IsNotNull(botView.GameObject.GetComponentInChildren<Animator>(true),
                "bot 走的是同一条真模型路径（D1/D6——旧断言"D-P5-7: bot 必须是占位"已被实测推翻）");
            Assert.IsNotNull(botView.Animator, "bot 也必须挂上行走动画驱动");
            Assert.IsNotNull(realView.Animator, "真人挂驱动");
            Assert.IsNull(botView.GameObject.GetComponentInChildren<NpcBodyCollider>(true),
                "bot 不挡路（D5：远端实体是视图不是物理体）");
        }
```

`Unwired_Avatar_Prefab_Falls_Back_To_A_Placeholder` 保持原样（`new RemoteEntityRegistry(fallbackRoot.transform)` 仍然走降级）。

- [ ] **步骤 2：运行测试验证失败**

运行：`... --timeout 600000 --input '{"testMode":"PlayMode","testClass":"RemoteRenderBranchTests"}'`
预期：编译失败（构造签名变了）。

- [ ] **步骤 3：改构造签名 + `CreateView`**

构造改为：

```csharp
        private readonly IReadOnlyList<AppearanceEntry>? _appearances;

        /// <param name="appearances">
        /// 玩家类实体（真人与 bot）的外观目录。**省略 = 全部退化为胶囊占位**
        /// （未接线的测试宿主与场景走这条，与怪物分支同款"缺配置即降级"）。
        /// </param>
        public RemoteEntityRegistry(Transform root,
            IReadOnlyList<AppearanceEntry>? appearances = null,
            IReadOnlyDictionary<int, GameObject>? npcPrefabs = null)
```

`CreateView` 的玩家两支合并为：

```csharp
            // 玩家类（真人 + bot）：**同一条路径**（design D1/D6）——外观目录查表 → 实例化 → 挂驱动。
            // 旧的两支（真人 = 预制体 / bot = 胶囊）已合并：AOI 之后同屏实体数由局部密度决定，
            // 实测 `--bots 100` 的同屏峰值只有 5~6 个，'100 个蒙皮网格' 从来没发生过。
            var entry = snapshot.Kind == (byte)EntityKind.PlayerBot
                ? AppearanceResolver.EntryAt(_appearances, AppearanceResolver.BotIndex(playerId, _appearances?.Count ?? 0))
                : AppearanceResolver.FindId(_appearances, AppearanceResolver.DefaultId);
            var fullPrefab = entry?.Full;
            if (fullPrefab == null)
            {
                // 未接线 / 未命中：占位降级（**不 `Instantiate(null)`**——那会抛异常并把整条
                // 快照消费链带崩，远程玩家直接不渲染）
                if (!_warnedMissingAvatar)
                {
                    _warnedMissingAvatar = true;
                    Debug.LogWarning("[Sync] no appearance configured; remote players/bots " +
                                     "fall back to capsule placeholders");
                }
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                StripCollider(fallback);   // D19：CreatePrimitive 自带 Collider，远端实体不该挡人
                fallback.name = KindName(playerId, snapshot.Kind);
                fallback.transform.SetParent(_root, false);
                return new RemoteEntityView(fallback, snapshot.Kind);
            }

            // 包装根：位置/朝向写它，两个表现子物体挂它下面（分档在任务 6 接线）
            var viewRoot = new GameObject(KindName(playerId, snapshot.Kind));
            viewRoot.transform.SetParent(_root, false);
            var avatar = Object.Instantiate(fullPrefab, viewRoot.transform, false);
            avatar.name = "Full";
            // AddComponent 同步跑 Awake：3C 装备在同一个 Start/Update 之前就被停掉（见类注释）
            var animator = avatar.AddComponent<RemoteEntityAnimator>();
            var view = new RemoteEntityView(viewRoot, snapshot.Kind, animator: animator);
            view.FullRoot = avatar;
            return view;
```

配套私有助手：

```csharp
        /// <summary>视图名：验收/诊断靠它认实体身份（保留既有命名）</summary>
        private static string KindName(long playerId, byte kind)
            => kind == (byte)EntityKind.PlayerBot ? $"Bot_{playerId}" : $"RemotePlayer_{playerId}";

        /// <summary>去掉 `CreatePrimitive` 自带的碰撞体（D19：远端实体是视图不是物理体）</summary>
        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }
```

`RemoteEntityView` 增加：

```csharp
        /// <summary>近档表现（模型实例）。null = 尚未建/无 Full 资产</summary>
        public GameObject? FullRoot { get; internal set; }
        /// <summary>远档表现（代理）。null = 尚未建</summary>
        public GameObject? ProxyRoot { get; internal set; }
```

怪物分支（`Kind == Npc`）**整条不动**（不加包装根——`NpcBodyColliderTests` 有 `AreSame(view.GameObject.transform, body.transform.parent)`）。

- [ ] **步骤 4：更新其余两处调用点**

`ClientNetworkHost.cs:126`（任务 7 会换成真实目录，这里先接上现有字段）：

```csharp
            _remoteEntities = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = _playerAvatarPrefab } },
                catalog.Count > 0 ? catalog : null);
```

`NpcBodyColliderTests.cs:187`：

```csharp
            var registry = new RemoteEntityRegistry(_root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = avatarPrefab } });
```

- [ ] **步骤 5：运行测试验证通过**

运行：`... --timeout 600000 --input '{"testMode":"PlayMode","testClass":"RemoteRenderBranchTests"}'`
预期：4 条全 PASS（含反转后的新用例与未接线降级用例）。

再跑既有回归（都是 EditMode，构造签名变更的最大风险面）：

```bash
for c in NpcBodyColliderTests RemoteEntityRegistryTests CombatNetworkBridgeTests ClientRespawnNoticeTests; do
  unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input "{\"testMode\":\"EditMode\",\"testClass\":\"$c\"}"
done
```
预期：全绿（`MonsterBodyRadiusTests` 一起跑更稳）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
        Assets/Scripts/Tests/PlayMode/RemoteRenderBranchTests.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs
git commit -m "feat(sync): merge the bot branch into the real-model path

bot 与真人统一走外观目录 → 实例化 → 挂驱动，包装根下挂 Full 表现。
反转 RemoteRenderBranchTests 里守 D-P5-7 的旧断言：AOI 之后同屏实体数由
局部密度决定，实测 --bots 100 的同屏峰值只有 5~6 个，"100 个蒙皮网格"
从来没发生过。降级胶囊去除 CreatePrimitive 自带 Collider（D19）。"
```

---

## 任务 6：分档骨架

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteTierPolicy.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteTierPolicyTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（观察者注入、每帧分档、惰性建表现）

- [ ] **步骤 1：编写失败的测试**

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 分档策略（design §4）：距离迟滞 + 最近优先预算裁剪。
    /// 语义分工：**距离保观感、预算控成本**——低密度全 Full（好看），高密度由 MaxFull 兜底（能跑）。
    /// </summary>
    public class RemoteTierPolicyTests
    {
        private static List<TierCandidate> Candidates(params (long id, float dist, bool full)[] items)
        {
            var list = new List<TierCandidate>();
            foreach (var (id, dist, full) in items)
                list.Add(new TierCandidate { PlayerId = id, DistanceSqr = dist * dist, CurrentlyFull = full });
            return list;
        }

        [Test]
        public void Inside_The_Threshold_Is_Full()
        {
            var policy = new RemoteTierPolicy { FullDistance = 60f, MaxFull = 20 };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 30f, false)), results);
            Assert.AreEqual(RemoteTier.Full, results[0]);
        }

        [Test]
        public void Beyond_The_Exit_Threshold_Is_Proxy()
        {
            var policy = new RemoteTierPolicy { FullDistance = 60f, ExitRatio = 1.15f };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 80f, true)), results);
            Assert.AreEqual(RemoteTier.Proxy, results[0], "超过退出阈值(69m)即使当前是 Full 也要降档");
        }

        [Test]
        public void Hysteresis_Keeps_A_Full_Entity_That_Drifted_Into_The_Band()
        {
            var policy = new RemoteTierPolicy { FullDistance = 60f, ExitRatio = 1.15f };
            var results = new List<RemoteTier>();

            policy.Decide(Candidates((1, 65f, true)), results);
            Assert.AreEqual(RemoteTier.Full, results[0], "65m 在迟滞带(60~69)内：已是 Full 的保持");

            policy.Decide(Candidates((1, 65f, false)), results);
            Assert.AreEqual(RemoteTier.Proxy, results[0], "同一距离但当前是 Proxy：不升档（迟滞的另一半）");
        }

        [Test]
        public void Budget_Keeps_The_Nearest_And_Demotes_The_Rest()
        {
            var policy = new RemoteTierPolicy { FullDistance = 300f, MaxFull = 2 };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 50f, false), (2, 10f, false), (3, 30f, false)), results);

            Assert.AreEqual(RemoteTier.Full, results[1], "10m 最近 ⇒ Full");
            Assert.AreEqual(RemoteTier.Full, results[2], "30m 次近 ⇒ Full");
            Assert.AreEqual(RemoteTier.Proxy, results[0], "50m 超出预算 ⇒ 降档");
        }

        [Test]
        public void Budget_Tie_Breaks_Deterministically_By_Id()
        {
            // 有效距离相同 → 按 PlayerId 升序，否则同距离实体会每帧互换档位（抖动）
            var policy = new RemoteTierPolicy { FullDistance = 300f, MaxFull = 1 };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((7, 25f, false), (3, 25f, false)), results);

            Assert.AreEqual(RemoteTier.Full, results[1], "id 小的赢（确定性）");
            Assert.AreEqual(RemoteTier.Proxy, results[0]);
        }

        [Test]
        public void Existing_Full_Gets_A_Retention_Bias()
        {
            // 已在 Full 的距离打 0.9 折：25m 的老 Full（有效 22.5m）赢过 24m 的新候选
            var policy = new RemoteTierPolicy { FullDistance = 300f, MaxFull = 1, FullRetention = 0.9f };
            var results = new List<RemoteTier>();
            policy.Decide(Candidates((1, 25f, true), (2, 24f, false)), results);

            Assert.AreEqual(RemoteTier.Full, results[0], "已在 Full 的占优（预算裁剪迟滞）");
            Assert.AreEqual(RemoteTier.Proxy, results[1]);
        }

        [Test]
        public void Empty_Input_Is_Safe()
        {
            var policy = new RemoteTierPolicy();
            var results = new List<RemoteTier>();
            Assert.DoesNotThrow(() => policy.Decide(new List<TierCandidate>(), results));
            Assert.AreEqual(0, results.Count);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`... --input '{"testMode":"EditMode","testClass":"RemoteTierPolicyTests"}'`
预期：编译失败——`RemoteTier` / `TierCandidate` / `RemoteTierPolicy` 不存在。

- [ ] **步骤 3：实现 `RemoteTierPolicy`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>表现档位（design §3）。Proxy = 代理：无 Animator、无蒙皮、1 个 draw call。
    /// **出视野仍是销毁，不是第三档**——生命周期与档位是两回事。</summary>
    public enum RemoteTier { Full = 0, Proxy = 1 }

    /// <summary>分档候选（每帧重建——位置每帧在变，缓存没意义）</summary>
    public struct TierCandidate
    {
        public long PlayerId;
        /// <summary>与观察者的 XZ 平面距离的平方（与 AOI 同心）</summary>
        public float DistanceSqr;
        /// <summary>当前是否已是 Full（迟滞的两半都用它）</summary>
        public bool CurrentlyFull;
    }

    /// <summary>
    /// 分档策略（design §4）。**距离保观感、预算控成本**：
    ///   · 进入 60m / 退出 69m（= 进入 × 1.15，与 AOI 的 80/90 同款迟滞）；
    ///   · 每帧最多 MaxFull 个 Full，其余降 Proxy——超出预算的按"有效距离"最近优先。
    ///
    /// 无分配：比较器在构造时建好（每帧 new 闭包会在 WebGL 上堆 GC）。
    /// </summary>
    public sealed class RemoteTierPolicy
    {
        /// <summary>进入 Full 的距离（米）。人形在 60m 处约 25px，切换看不出来</summary>
        public float FullDistance = 60f;
        /// <summary>退出 Full 的距离倍率（迟滞）</summary>
        public float ExitRatio = 1.15f;
        /// <summary>每帧 Full 档预算——**未经实测校准的保守起点**，任务 10 实测后回填</summary>
        public int MaxFull = 20;
        /// <summary>已在 Full 的候选在预算排序里的距离折扣（预算裁剪迟滞：距离相近不来回抢档）</summary>
        public float FullRetention = 0.9f;

        public float ExitDistance => FullDistance * ExitRatio;

        // 复用缓冲 + 缓存的比较器（无每帧分配）
        private readonly List<int> _eligible = new List<int>();
        private readonly Comparison<int> _indexOrder;
        private List<TierCandidate> _candidates = null!;

        public RemoteTierPolicy() => _indexOrder = CompareIndex;

        /// <summary>
        /// 逐候选定档，写入 <paramref name="results"/>（**与 candidates 同序**；调用方复用同一个 List）。
        /// </summary>
        public void Decide(List<TierCandidate> candidates, List<RemoteTier> results)
        {
            _candidates = candidates;
            _eligible.Clear();
            results.Clear();

            for (int i = 0; i < candidates.Count; i++)
            {
                results.Add(RemoteTier.Proxy);   // 默认降档，下面只把够格又排得上的抬回 Full
                var c = candidates[i];
                float limit = c.CurrentlyFull ? ExitDistance : FullDistance;
                if (c.DistanceSqr <= limit * limit) _eligible.Add(i);
            }

            if (_eligible.Count == 0) return;

            int keep = Mathf.Min(MaxFull, _eligible.Count);
            if (keep < _eligible.Count) _eligible.Sort(_indexOrder);

            for (int i = 0; i < keep; i++) results[_eligible[i]] = RemoteTier.Full;
        }

        private int CompareIndex(int a, int b)
        {
            float da = EffectiveDistance(_candidates[a]);
            float db = EffectiveDistance(_candidates[b]);
            int cmp = da.CompareTo(db);
            return cmp != 0 ? cmp : _candidates[a].PlayerId.CompareTo(_candidates[b].PlayerId);
        }

        /// <summary>有效距离：已在 Full 的 × FullRetention（比较用，故开方回距离域——几十次 sqrt 可忽略）</summary>
        private float EffectiveDistance(TierCandidate c)
        {
            float distance = Mathf.Sqrt(c.DistanceSqr);
            return c.CurrentlyFull ? distance * FullRetention : distance;
        }
    }
}
```

- [ ] **步骤 4：接进 `RemoteEntityRegistry`**

字段 + 注入点：

```csharp
        /// <summary>分档策略（宿主可覆盖预算/阈值；URL 旋钮见 TierQuery）</summary>
        public RemoteTierPolicy TierPolicy { get; } = new RemoteTierPolicy();

        /// <summary>
        /// 观察者位置提供者（本地玩家位置，与 AOI 同心）。**未注入、或本帧返回 null = 不分档
        /// （保持全 Full）**——测试宿主与既有行为不受影响（延续"缺配置即降级"的惯例）。
        /// 返回 null 的场景：本地角色还没装配（`_localEntry` 为空）——此时拿不到有意义的距离。
        /// </summary>
        public Func<Vector3?>? ObserverPositionProvider { get; set; }

        private readonly List<TierCandidate> _tierCandidates = new List<TierCandidate>();
        private readonly List<RemoteTier> _tierResults = new List<RemoteTier>();
```

`UpdateRender` 的位置循环之后接分档：

```csharp
            // 分档（design §4）：插值写完位置之后、驱动写入之前——本帧的档位就是这个位置算出来的。
            // 未注入观察者、或本帧拿不到位置（本地角色还没装配）= 不分档（保持全 Full）。
            if (ObserverPositionProvider?.Invoke() is Vector3 observer)
                ApplyTiers(observer);
```

```csharp
        /// <summary>每帧分档：距离（迟滞）+ 预算（最近优先）→ 目标档 → 换档。</summary>
        private void ApplyTiers(Vector3 observer)
        {
            _tierCandidates.Clear();
            foreach (var kv in _views)
            {
                // 怪物不参与分档（design D15）：它们是玩法实体（碰撞体/受击/攻击排程耦合），
                // 且决策点共用——将来要扩，加一个字段即可。
                if (kv.Value.Kind == (byte)EntityKind.Npc) continue;
                if (kv.Value.FullRoot == null && kv.Value.ProxyRoot == null) continue;  // 纯降级占位视图

                float dx = kv.Value.Position.x - observer.x;
                float dz = kv.Value.Position.z - observer.z;
                _tierCandidates.Add(new TierCandidate
                {
                    PlayerId = kv.Key,
                    DistanceSqr = dx * dx + dz * dz,
                    CurrentlyFull = kv.Value.Tier == RemoteTier.Full
                });
            }

            TierPolicy.Decide(_tierCandidates, _tierResults);

            for (int i = 0; i < _tierCandidates.Count; i++)
                if (_views.TryGetValue(_tierCandidates[i].PlayerId, out var view))
                    SetTier(view, _tierResults[i]);
        }

        /// <summary>
        /// 换档（惰性建表现）。降档 = `FullRoot.SetActive(false)`——**整棵禁用**：Animator、
        /// 蒙皮、渲染器全停，这正是表示分级相对于网格 LOD 的价值（LOD 省不到这几项）。
        /// **不销毁**（会再升回来）；升级时强制重写一次驱动参数（Animator 禁用即重置状态）。
        /// </summary>
        private void SetTier(RemoteEntityView view, RemoteTier tier)
        {
            if (view.Tier == tier && (tier == RemoteTier.Full ? view.FullRoot != null : view.ProxyRoot != null))
                return;

            view.Tier = tier;

            if (tier == RemoteTier.Full)
            {
                if (view.FullRoot == null && view.FullPrefab != null)
                {
                    var avatar = Object.Instantiate(view.FullPrefab, view.GameObject.transform, false);
                    avatar.name = "Full";
                    view.FullRoot = avatar;
                    view.Animator = avatar.AddComponent<RemoteEntityAnimator>();
                }
                if (view.FullRoot == null)
                {
                    // 没有 Full 资产（该条目只配了代理）：档位如实落回 Proxy，别让 Tier 与眼前所见脱节
                    view.Tier = RemoteTier.Proxy;
                    return;
                }
                view.FullRoot.SetActive(true);
                view.ProxyRoot?.SetActive(false);
                view.Animator!.InvalidateDedup();
            }
            else
            {
                if (view.ProxyRoot == null)
                {
                    var proxy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    StripCollider(proxy);   // D19
                    proxy.name = "Proxy";
                    proxy.transform.SetParent(view.GameObject.transform, false);
                    view.ProxyRoot = proxy;
                }
                view.ProxyRoot.SetActive(true);
                view.FullRoot?.SetActive(false);
            }
        }
```

`RemoteEntityView` 再补三处（`Animator` 目前是 get-only 只读属性 → 加内部 setter）：

```csharp
        /// <summary>当前档位（null = 表现尚未建立）</summary>
        public RemoteTier? Tier { get; internal set; }
        /// <summary>近档预制体（分档升档时惰性实例化用）</summary>
        public GameObject? FullPrefab { get; internal set; }
        /// <summary>近档的动画驱动（升档时才存在；构造期已有的照旧在构造函数里赋值）</summary>
        public RemoteEntityAnimator? Animator { get; internal set; }
```

（`Animator` 原为 get-only 自动属性、由构造函数赋值——改 `internal set` 后，任务 6 的 `SetTier` 惰性实例化时能挂上驱动。）

**CreateView 的收尾**改为：

```csharp
            view.FullPrefab = fullPrefab;
            // 无观察者（测试宿主/未接线）= 不分档，直接建 Full——既有测试在 ApplySnapshot 之后
            // 立刻断言 Animator，不能等 UpdateRender。
            SetTier(view, ObserverPositionProvider == null ? RemoteTier.Full : RemoteTier.Proxy);
            return view;
```

**驱动的调用点加档位守卫**（`Apply` 是 registry 主动调用的公开方法，禁用 GameObject 挡不住它）：

```csharp
            // 只在 Full 档驱动：降档后写参数是白费（且 Animator 已随 FullRoot 禁用）
            if (view.Tier == RemoteTier.Full)
            {
                view.Animator?.Apply(snapshot.ActionState, snapshot.Dead);
                view.Animator?.ApplyLocomotion(view.Speed);
            }
```

（`ApplySnapshot` 里的 `view.Animator?.Apply(...)` 与 `UpdateRender` 里新增的 `ApplyLocomotion` 都按这条守卫走。）

- [ ] **步骤 5：补一条 registry 级的分档用例（写进 `RemoteTierPolicyTests` 同文件或新建）**

```csharp
        private static EntitySnapshot SnapAt(long id, Vector3 pos, float yaw = 0f)
            => new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Kind),
                X = pos.x, Y = pos.y, Z = pos.z, Rotation = yaw,
                Kind = (byte)EntityKind.PlayerBot, Tick = 1
            };

        [Test]
        public void Registry_Demotes_Entities_Beyond_The_Budget()
        {
            var root = new GameObject("TierRegistryRoot");
            var prefab = new GameObject("TierAvatarPrefab");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            registry.ObserverPositionProvider = () => Vector3.zero;
            registry.TierPolicy.FullDistance = 300f;
            registry.TierPolicy.MaxFull = 2;
            try
            {
                // 4 个实体，距离 10 / 20 / 30 / 40m
                for (int i = 0; i < 4; i++)
                    registry.ApplySnapshot(SnapAt(10 + i, new Vector3((i + 1) * 10f, 0f, 0f)),
                        serverTicks: 1_000_000L);

                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                int full = 0, proxy = 0;
                foreach (var id in registry.PlayerIds)
                {
                    var view = registry.GetView(id)!;
                    if (view.Tier == RemoteTier.Full) full++;
                    else proxy++;
                }
                Assert.AreEqual(2, full, "预算 2 ⇒ 只有最近的 2 个是 Full");
                Assert.AreEqual(2, proxy, "其余降 Proxy");

                Assert.IsFalse(registry.GetView(13)!.FullRoot!.activeInHierarchy,
                    "超预算的实体：Full 表现整棵禁用（Animator/蒙皮/渲染器全停）");
                Assert.IsTrue(registry.GetView(13)!.ProxyRoot!.activeInHierarchy, "代理顶上");
                Assert.IsTrue(registry.GetView(10)!.FullRoot!.activeInHierarchy, "最近的实体保持 Full");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Registry_Without_Observer_Keeps_Everyone_Full()
        {
            var root = new GameObject("NoObserverRoot");
            var prefab = new GameObject("NoObserverAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            try
            {
                registry.ApplySnapshot(SnapAt(1, new Vector3(500f, 0f, 0f)), serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                Assert.AreEqual(RemoteTier.Full, registry.GetView(1)!.Tier,
                    "未注入观察者 = 不分档（现有测试宿主与未接线场景的行为不变）");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }
```

- [ ] **步骤 6：运行测试验证通过**

运行：`... --input '{"testMode":"EditMode","testClass":"RemoteTierPolicyTests"}'`
预期：9 条全 PASS。

再跑回归：`RemoteRenderBranchTests`（PlayMode）+ `NpcBodyColliderTests`、`RemoteEntityRegistryTests`（EditMode）——预期全绿。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteTierPolicy.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/RemoteTierPolicyTests.cs
git commit -m "feat(sync): add the representation tier skeleton (distance + budget)

进入 60m / 退出 69m（迟滞，与 AOI 的 80/90 同源）+ 每帧 Full 预算（最近优先，
已在 Full 的 0.9 距离折扣）。降档 = FullRoot 整棵 SetActive(false)——Animator/
蒙皮/渲染器全停，这正是表示分级相对网格 LOD 的价值所在。观察者未注入 =
不分档（全 Full）。决策无每帧分配，同距离按 id 确定性排序防抖。"
```

---

## 任务 7：诊断读数 + URL 旋钮

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/TierQuery.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/TierQueryTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（`FullCount` / `ProxyCount`）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（外观目录字段 + 观察者注入 + overlay + 旋钮）

- [ ] **步骤 1：编写失败的测试**

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>`?full=` / `?fulldist=`（design §4 诊断读数与旋钮）。规则与 ServerAddressQuery 同源：
    /// 每种输入都有确定行为；畸形一律**忽略**（现场多打一个字符的代价不能是"打不开游戏"）。</summary>
    public class TierQueryTests
    {
        [Test]
        public void Full_Overrides_The_Budget()
        {
            Assert.IsTrue(TierQuery.TryFull("?full=12", out int budget));
            Assert.AreEqual(12, budget);
        }

        [Test]
        public void Full_Zero_Is_Valid_And_Means_All_Proxy()
        {
            // ?full=0 = 回到"纯胶囊"条件：AOI 验证去噪 + 模型太贵时的回滚开关
            Assert.IsTrue(TierQuery.TryFull("?full=0", out int budget));
            Assert.AreEqual(0, budget, "0 是合法值，不是畸形");
        }

        [Test]
        public void FullDistance_Overrides_The_Threshold()
        {
            Assert.IsTrue(TierQuery.TryFullDistance("?fulldist=25.5", out float distance));
            Assert.AreEqual(25.5f, distance, 0.001f);
        }

        [Test]
        public void Missing_Or_Malformed_Params_Are_Ignored()
        {
            Assert.IsFalse(TierQuery.TryFull(null, out _));
            Assert.IsFalse(TierQuery.TryFull("?host=1.2.3.4", out _));
            Assert.IsFalse(TierQuery.TryFull("?full=abc", out _));
            Assert.IsFalse(TierQuery.TryFull("?full=-3", out _));
            Assert.IsFalse(TierQuery.TryFullDistance("?fulldist=abc", out _));
            Assert.IsFalse(TierQuery.TryFullDistance("?fulldist=-1", out _));
        }

        [Test]
        public void Absurdly_Large_Values_Are_Rejected()
        {
            // 上限存在是为了挡住"?full=999999"这种把整屏实体全塞进 Full 的输入
            Assert.IsFalse(TierQuery.TryFull("?full=999999", out _));
            Assert.IsFalse(TierQuery.TryFullDistance("?fulldist=99999", out _));
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`... --input '{"testMode":"EditMode","testClass":"TierQueryTests"}'`
预期：编译失败——`TierQuery` 不存在。

- [ ] **步骤 3：实现 `TierQuery`**

```csharp
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
```

- [ ] **步骤 4：registry 暴露计数 + 宿主接线**

`RemoteEntityRegistry` 加：

```csharp
        /// <summary>当前 Full 档视图数（诊断读数用）</summary>
        public int FullCount { get; private set; }
        /// <summary>当前 Proxy 档视图数（诊断读数用）</summary>
        public int ProxyCount { get; private set; }
```

在 `ApplyTiers` 结尾统计（未分档时 `FullCount = Count - 怪物数`、`ProxyCount = 0`——保持读数与视觉一致）：

```csharp
            FullCount = 0;
            ProxyCount = 0;
            foreach (var kv in _views)
            {
                if (kv.Value.Kind == (byte)EntityKind.Npc) continue;   // 与分档同样的排除口径
                if (kv.Value.Tier == RemoteTier.Full) FullCount++;
                else ProxyCount++;   // Tier 为 null（纯降级占位视图）也算代理口径——读数与所见一致
            }
```

`ClientNetworkHost`：

1. 字段（替换 `_playerAvatarPrefab` 的用法，保留该字段作为"默认外观"的 Inspector 槽）：

```csharp
        [Header("远程玩家外观目录（Id 0 = 默认外观；bot 按 id 哈希选条目）")]
        [SerializeField] private AppearanceEntry[] _appearances = System.Array.Empty<AppearanceEntry>();
```

`Awake` 里的构造改为：

```csharp
            _remoteEntities = new RemoteEntityRegistry(root.transform,
                _appearances.Length > 0
                    ? _appearances
                    : new[] { new AppearanceEntry { Id = 0, Full = _playerAvatarPrefab } },
                catalog.Count > 0 ? catalog : null);

            // 观察者位置 = 本地玩家（与 AOI 同心）。取不到（角色还没装配）⇒ 本帧返回 null，
            // registry 那一帧不分档——**不能返回 Vector3.zero**：那会让分档以原点为中心，距离全错。
            _remoteEntities.ObserverPositionProvider = () =>
            {
                if (_localEntry == null && !_entryLookedUp)
                    _localEntry = FindObjectOfType<Sys3CEntry>();
                return _localEntry != null ? _localEntry.transform.position : (Vector3?)null;
            };

            // 分档旋钮（URL 覆盖——手机上扫参数不用重新打包）
            string url = Application.absoluteURL;
            if (TierQuery.TryFull(url, out int budget)) _remoteEntities.TierPolicy.MaxFull = budget;
            if (TierQuery.TryFullDistance(url, out float distance)) _remoteEntities.TierPolicy.FullDistance = distance;
```

2. overlay 加读数（`RemoteEntityRegistry` 暴露的计数 + `1/deltaTime`）：

```csharp
            int remoteCount = _remoteEntities != null ? _remoteEntities.Count : 0;
            string tiers = _remoteEntities != null
                ? $"  Full={_remoteEntities.FullCount} Proxy={_remoteEntities.ProxyCount} 预算={_remoteEntities.TierPolicy.MaxFull}"
                : string.Empty;
            float fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            GUI.Box(new Rect(10f, Screen.height - 34f, 620f, 26f),
                $"[Net] 模式={mode}  状态={state}  玩家id={playerId}  远程实体={remoteCount}{tiers}  FPS={fps:F0}");
```

- [ ] **步骤 5：运行测试验证通过**

运行：`... --input '{"testMode":"EditMode","testClass":"TierQueryTests"}'`
预期：5 条全 PASS。

再跑 `RemoteTierPolicyTests` + `RemoteRenderBranchTests` 回归——预期全绿。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/TierQuery.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/TierQueryTests.cs
git commit -m "feat(sync): expose tier counts and URL knobs for the on-device sweep

overlay 增加 Full/Proxy 计数、当前预算与 FPS；?full= 覆盖预算（0 = 全代理，
既是 AOI 验证的去噪开关，也是模型太贵时的回滚开关）、?fulldist= 覆盖阈值。
宿主接线外观目录并把观察者位置接到本地角色。"
```

---

## 任务 8：ORCA 半径真值源核对

**背景**：服务端 `BotRunner.BotRadius = 0.5` 的注释写着它"对应客户端那条占位胶囊"——换成模型后这条真值源失效（spec F13）。本任务把真值重新钉住。

**文件：**
- 修改：`Server/Aoi/BotRunner.cs`（注释与常量，二选一）
- 修改：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs`（追加守护用例）

- [ ] **步骤 1：写测量用例（先让它跑，看真实数字）**

追加到 `RemoteEntityRegistryTests`：

```csharp
        /// <summary>
        /// ORCA 半径真值源（design 任务 8）：服务端 `BotRunner.BotRadius = 0.5` 的注释原写"对应
        /// 客户端那条占位胶囊"——bot 换真模型后真值应是**模型半宽**。
        ///
        /// 为什么读源码文本而不是引用常量：`Server/Server3C.csproj` 是 Assets/ 之外的 .NET 工程，
        /// Unity 测试程序集引用不到它。项目已有"断言文件文本"的先例（`NpcBodyColliderTests` 读
        /// TagManager 资产），这里同款处理。
        /// </summary>
        [Test]
        public void Bot_Model_HalfWidth_Matches_The_Orca_Radius()
        {
            const string PrefabPath = "Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab";
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotEmpty(renderers, "角色预制体应有蒙皮网格");
            var local = renderers[0].localBounds;
            for (int i = 1; i < renderers.Length; i++) local.Encapsulate(renderers[i].localBounds);

            float halfWidth = Mathf.Max(local.size.x, local.size.z) * 0.5f;

            string source = System.IO.File.ReadAllText("Server/Aoi/BotRunner.cs");
            var match = System.Text.RegularExpressions.Regex.Match(source, @"BotRadius\s*=\s*([0-9.]+)f");
            Assert.IsTrue(match.Success,
                "没在 Server/Aoi/BotRunner.cs 里找到 BotRadius 常量——常量改名了？同步更新本用例");
            float orcaRadius = float.Parse(match.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.That(halfWidth, Is.EqualTo(orcaRadius).Within(0.15f),
                $"模型半宽 {halfWidth:F3}m 应与 ORCA 半径 {orcaRadius}m 一致（±0.15）——" +
                "不一致就改 BotRunner.BotRadius 并同步注释，别让注释与真相脱节");
        }
```

- [ ] **步骤 2：运行看数字**

运行：`... --input '{"testMode":"EditMode","testClass":"RemoteEntityRegistryTests"}'`
- **PASS** ⇒ 模型半宽落在 0.5 ± 0.15 内：只改注释（步骤 3a）。
- **FAIL** ⇒ 记下实际数字：改 `BotRadius` 为该值（步骤 3b）。

- [ ] **步骤 3a：注释同步（PASS 分支）**

`Server/Aoi/BotRunner.cs` 的 `BotRadius` 注释改为：

```csharp
        /// <summary>
        /// 机器人身体半径（米，ORCA 避让用）——**真值源 = 客户端角色模型的半宽**
        /// （`Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab` 的 SMR localBounds，见
        /// `RemoteEntityRegistryTests.Bot_Model_HalfWidth_Matches_The_Orca_Radius`）。
        /// 2026-10-13 换真模型后复核：与旧占位胶囊（单位胶囊半径 0.5）一致，故数值未变。
        /// </summary>
```

- [ ] **步骤 3b：改数值（FAIL 分支）**

把 `BotRadius` 改成实测值，注释同 3a（把"一致，故数值未变"改成"由占位胶囊的 0.5 改为实测 X"），然后：

```bash
dotnet build Server/Server3C.csproj
dotnet test Server/Server.Tests/Server.Tests.csproj    # 避让/边界用例必须保持绿
```

> 半径只影响**有邻居时**的解算；`AvoidanceSystem` 的"无邻居逐位等于期望速度"护栏与半径无关，不受影响（若它红了，说明改动超出了预期，停下来查）。

- [ ] **步骤 4：跑服务端测试**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj
```
预期：全绿。

- [ ] **步骤 5：Commit**

```bash
git add Server/Aoi/BotRunner.cs Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs
git commit -m "test(movement): re-anchor the ORCA bot radius to the real model

BotRadius 的注释原写'对应客户端那条占位胶囊'——bot 换真模型后真值源变成
模型半宽。新增守护用例量取 SMR bounds 并比对（±0.15m），注释同步为实测结论。"
```

---

## 任务 9：服务端密集压测模式

**为什么需要**：`--bots 100` 撒在 ±400m 时 80m 视野内只有 5~6 个，**测不出预算边界**；密集模式把 bot 全撒在观察者周围，手机上才有 100 个同屏实体可测（顺带补上离散场景测不到的 **AOI 带宽**压力）。

**文件：**
- 修改：`Server/Aoi/BotRunner.cs`（撒点/边界半径参数）
- 修改：`Server/Program.cs`（`--bots-radius R` 解析）
- 修改：`Server/Server.Tests/`（若已有 BotRunner 用例，补一条密集模式的边界用例）

- [ ] **步骤 1：改 `BotRunner`**

```csharp
        /// <summary>
        /// 密集压测模式的半径（米）：`--bots-radius R` 时，bot 的**撒点与反弹边界**都收到 ±R，
        /// 测试者站原点即可拿到满屏实体（实测渲染预算用）。null = 保持现状（撒点 ±0.8×500、边界 ±500）。
        /// </summary>
        private readonly float? _denseRadius;

        public BotRunner(EntityStore store, Func<long> nextPlayerId, int seed = 12345,
            AvoidanceSystem? avoidance = null, float? denseRadius = null)
        {
            _store = store;
            _nextPlayerId = nextPlayerId;
            _random = new Random(seed);
            _avoidance = avoidance;
            _denseRadius = denseRadius;
        }

        private float SpawnExtent => _denseRadius ?? WorldHalfExtent * 0.8f;
        private float BounceExtent => _denseRadius ?? WorldHalfExtent;
```

`Spawn` 里 `WorldHalfExtent * 0.8f` → `SpawnExtent`；`Tick` 里三处 `WorldHalfExtent` → `BounceExtent`；`Clamp` 改成实例方法 `private float Clamp(float v) => Math.Clamp(v, -BounceExtent, BounceExtent);`

> `WorldHalfExtent` 仍是 public const（既有测试/文档引用它）；`ShouldBounce` 是 `internal static` 纯函数，**签名不动**（它的用例继续有效）。

- [ ] **步骤 2：改 `Program.cs` 参数解析**

```csharp
    // P3 压测模式：--bots N（N 个移动 bot 注册进世界——供 AoiWorldTests 带宽/视野验收）
    // 密集压测：--bots-radius R（把 bot 的撒点与反弹边界都收到 ±R，站原点即满屏实体）
    float? botsRadius = null;
    ...
    else if (args[i] == "--bots-radius" && i + 1 < args.Length
             && float.TryParse(args[i + 1], out var r) && r > 0f)
    {
        botsRadius = r;
        i++;
    }
```

构造处传 `denseRadius: botsRadius`；启动日志加一行：

```csharp
            if (botsRadius.HasValue)
                Console.WriteLine($"Dense bot mode: radius ±{botsRadius.Value}m");
```

- [ ] **步骤 3：本机验证密集模式可跑**

```bash
dotnet build Server/Server3C.csproj
dotnet run --no-build --project Server/Server3C.csproj -- 8000 --bots 100 --bots-radius 50 > /tmp/dense.log 2>&1 &
sleep 5 && grep -E "Bot mode|Dense bot mode" /tmp/dense.log
```
预期：两行日志都在（`Bot mode: 100 bots registered` + `Dense bot mode: radius ±50m`）。

若 `Server.Tests` 里已有 bot 边界用例，补一条：

```csharp
        [Test]
        public void Dense_Radius_Shrinks_Spawn_And_Bounce_Bounds()
        {
            var store = new EntityStore();
            long nextId = 1;
            var runner = new BotRunner(store, () => nextId++, seed: 12345,
                avoidance: null, denseRadius: 50f);
            runner.Spawn(20);

            foreach (var player in store.All)
            {
                Assert.LessOrEqual(Math.Abs(player.Position.X), 50f, "撒点必须收在 ±50（默认是 ±400）");
                Assert.LessOrEqual(Math.Abs(player.Position.Z), 50f, "撒点必须收在 ±50（默认是 ±400）");
            }

            // 推 600 tick（≈40s，8m/s 会撞界多次）——反弹边界也必须收到 ±50，否则 bot 会飘出密集区
            for (uint tick = 1; tick <= 600; tick++) runner.Tick(tick);
            foreach (var player in store.All)
            {
                Assert.LessOrEqual(Math.Abs(player.Position.X), 50f, "反弹边界必须收到 ±50");
                Assert.LessOrEqual(Math.Abs(player.Position.Z), 50f, "反弹边界必须收到 ±50");
            }
        }
```

（`EntityStore` / `store.All` 的用法照 `Server.Tests` 现有用例；若构造惯例不同，只改这一行，断言不动。）

- [ ] **步骤 4：全量服务端测试**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj
```
预期：全绿（默认路径逐位不变——密集参数为 null 时所有分支都走原常量）。

- [ ] **步骤 5：Commit**

```bash
git add Server/Aoi/BotRunner.cs Server/Program.cs Server/Server.Tests/
git commit -m "feat(movement): add a dense bot mode for the on-device sweep

--bots-radius R 把撒点与反弹边界一起收到 ±R：默认 >>500 的离散分布下 80m
视野内只有 5~6 个实体，测不出渲染预算边界；密集模式下站原点即 100 个同屏，
顺带补上离散场景测不到的 AOI 带宽压力。不带参数时逐位保持现状。"
```

---

## 任务 10：手机实测 → 回填预算数字

**这是 design S6 的产出**：一个真实数字。**前置条件：任务 1 已合入**（否则测到的是 Ultra 档的阴影/AA/反射探针瓶颈——design R1）。

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteTierPolicy.cs`（默认 `MaxFull` 回填）
- 创建：`production/verify/2026-10-13-tier-sweep.md`（实测记录）

- [ ] **步骤 1：起密集服务器 + 手机打开 WebGL 包**

```bash
dotnet run --no-build --project Server/Server3C.csproj -- 8000 --bots 100 --bots-radius 50 > /tmp/dense.log 2>&1 &
```
手机浏览器打开 `<部署地址>?host=<IP>:8002&quality=mobile`（**是 8002 不是 8000**，见 webgl-mobile-acceptance）。

- [ ] **步骤 2：扫预算参数，记录每个值的观感帧率**

在 URL 上依次加 `&full=0`、`&full=8`、`&full=12`、`&full=20`、`&full=40`，每个值停留 30 秒，记录：
- overlay 上的 `FPS` 读数（中位观感）
- `Full=` / `Proxy=` 计数
- 观感：流畅 / 尚可 / 明显卡顿

- [ ] **步骤 3：写出实测记录**

`production/verify/2026-10-13-tier-sweep.md`：

```markdown
# 表示分级 · 手机实测（T9）

- 日期 / 设备 / 浏览器 / 档位（必须是 `?quality=mobile` 档 2 的结果）
- 服务器：`--bots 100 --bots-radius 50`

| `?full=` | Full 计数 | Proxy 计数 | 帧率观感 | 判定 |
|---|---|---|---|---|
| 0 | 0 | … | … | … |
| 8 | … | … | … | … |
| 12 | … | … | … | … |
| 20 | … | … | … | … |
| 40 | … | … | … | … |

**结论**：预算定为 **N**（依据：……）。
**已知限制**：……（例：`?full=0` 即回滚成纯胶囊条件）
```

- [ ] **步骤 4：回填默认值**

把 `RemoteTierPolicy.MaxFull` 的默认值改为实测结论，注释标注依据：

```csharp
        /// <summary>每帧 Full 档预算。**2026-10-13 手机实测回填**：见
        /// production/verify/2026-10-13-tier-sweep.md（该设备在 N 个 Full 时不掉档）</summary>
        public int MaxFull = N;
```

- [ ] **步骤 5：跑回归并提交**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"RemoteTierPolicyTests"}'
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteTierPolicy.cs production/verify/2026-10-13-tier-sweep.md
git commit -m "chore(sync): set the tier budget from the on-device measurement

?full= 扫参实测回填 MaxFull（记录见 production/verify/2026-10-13-tier-sweep.md）。"
```

---

## 收尾验收（全部任务完成后）

- [ ] **全绿回归**

```bash
for c in MobileQualityBootstrapTests RenderSpeedEstimatorTests RemoteEntityAnimatorLocomotionTests \
         AppearanceCatalogTests RemoteTierPolicyTests TierQueryTests RemoteEntityRegistryTests \
         RemoteInterpolatorTests NpcBodyColliderTests MonsterBodyRadiusTests NpcEntityAnimatorTests \
         CombatNetworkBridgeTests; do
  unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input "{\"testMode\":\"EditMode\",\"testClass\":\"$c\"}"
done
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --timeout 600000 \
  --input '{"testMode":"PlayMode","testClass":"RemoteRenderBranchTests"}'
# AoiWorldTests 需要 --bots 100 服务器（见"验证前置"）
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --timeout 600000 \
  --input '{"testMode":"PlayMode","testClass":"AoiWorldTests"}'
```

- [ ] **人工目视（S1）**：DemoDay 起服 + Play，确认 bot 是**跑动的真模型**（不是胶囊、不是滑行），走近到 60m 内是模型、60m 外是代理，切换不刺眼。
- [ ] **回写文档**：更新 [CLAUDE.md](../../../CLAUDE.md) 的 SyncLayer/表现章节与 [webgl-mobile-acceptance.md](../guides/webgl-mobile-acceptance.md) 的"其他玩家为什么是胶囊"话术（那条已经过时——现在真人/bot 都是真模型，远档才是代理）。
