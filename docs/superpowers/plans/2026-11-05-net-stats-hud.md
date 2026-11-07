# 联网延迟与帧率 HUD 读数实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 联网模式下在 HUD 下方显示平滑后的 RTT 延迟与帧率；单机模式零影响。

**架构：** 应用层每 1 秒发送协议已有的 `Heartbeat`，服务端原样回显；`LatencyProbe` 在回调线程记录回包到达时刻、在主线程按 `Sequence` 匹配并计算 EMA。`ClientNetworkHost` 用 0.5 秒窗口算 FPS，每 0.5 秒把 `FPS xx  延迟 xxms` 推给 `PlayerHudPanel` 新增的 TMP 子物体。

**技术栈：** Unity 2022.3.25f1、C# 9、TextMeshPro、NUnit（EditMode/PlayMode）、Unity MCP。

**规格：** [2026-11-05-net-stats-hud-design.md](../specs/2026-11-05-net-stats-hud-design.md)

**前置条件：**

- Unity Editor 已打开，Unity MCP 可用（预制体编辑步骤使用 `script-execute`）。
- `bash tools/unity-tests.sh` 可运行。
- 仓库有无关的未提交改动：**每次只 `git add` 本计划列出的具体路径，禁止 `git add -A` / `git add .`**。
- 新增 `.cs` 后 Unity 会生成同名 `.meta`；提交时一并加入。若 `.meta` 尚未出现，先跑一次测试命令触发导入。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/LatencyProbe.cs` | 创建 | 应用层 RTT 探针：节拍、Sequence 匹配、EMA、超时 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs` | 修改 | 订阅 `Heartbeat`、每帧驱动探针、暴露 `RttMs` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 修改 | FPS 窗口、文本推送、显示/隐藏生命周期、`FormatNetStats` |
| `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs` | 修改 | `_netStatsText` 字段与 `SetNetStats` |
| `Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab` | 修改 | 新增 `NetStatsText` 子物体并连线字段 |
| `Assets/Scripts/Tests/Editor/SyncLayer/LatencyProbeTests.cs` | 创建 | 探针单元测试 |
| `Assets/Scripts/Tests/Editor/SyncLayer/ClientLatencyWiringTests.cs` | 创建 | 管理器接线测试 |
| `Assets/Scripts/Tests/Editor/SyncLayer/ClientNetworkHostNetStatsTests.cs` | 创建 | 文本格式测试 |
| `Assets/Scripts/Tests/PlayMode/PlayerHudNetStatsTests.cs` | 创建 | HUD 显隐与预制体连线测试 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md` | 修改 | 新文件与 HUD 读数说明 |
| `Assets/Scripts/Hotfix/GameSystems/UI/README.md` | 修改 | `PlayerHudPanel` 新接口说明 |

---

## 任务 1：LatencyProbe

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/LatencyProbe.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/LatencyProbeTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/SyncLayer/LatencyProbeTests.cs`：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 应用层 RTT 探针。时钟由测试注入：所有时间断言都是确定的毫秒数，
    /// 不依赖真实时间（否则 1 秒节拍/3 秒超时测试会又慢又抖）。
    /// </summary>
    public class LatencyProbeTests
    {
        private double _now;
        private readonly List<Heartbeat> _sent = new List<Heartbeat>();
        private LatencyProbe _probe = null!;

        [SetUp]
        public void SetUp()
        {
            _now = 0.0;
            _sent.Clear();
            _probe = new LatencyProbe(hb => _sent.Add(hb), () => _now);
        }

        [Test]
        public void test_latency_probe_first_tick_sends_immediately()
        {
            _probe.Tick();

            Assert.AreEqual(1, _sent.Count, "进世界后的首个 Tick 必须立即发第一条 Heartbeat");
        }

        [Test]
        public void test_latency_probe_sends_at_one_second_cadence()
        {
            _probe.Tick();          // t=0，发第 1 条
            _now = 0.9;
            _probe.Tick();

            Assert.AreEqual(1, _sent.Count, "1 秒节拍内不得重复发包");

            _now = 1.0;
            _probe.Tick();

            Assert.AreEqual(2, _sent.Count, "满 1 秒必须发下一条");
        }

        [Test]
        public void test_latency_probe_matching_response_reports_round_trip()
        {
            _probe.Tick();                                  // t=0，seq=1
            _now = 0.040;                                   // 回包 40ms 后到达
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();

            Assert.AreEqual(40, _probe.RttMs);
        }

        [Test]
        public void test_latency_probe_ignores_stale_sequence()
        {
            _probe.Tick();                                  // seq=1
            _now = 1.0;
            _probe.Tick();                                  // seq=2，覆盖待回包
            _now = 1.1;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();

            Assert.IsNull(_probe.RttMs, "旧 Sequence 的回包不能产生读数");
        }

        [Test]
        public void test_latency_probe_smooths_samples_with_ema()
        {
            _probe.Tick();
            _now = 0.100;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();
            Assert.AreEqual(100, _probe.RttMs);

            _now = 1.0;
            _probe.Tick();                                  // 发 seq=2
            _now = 1.200;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[1].Sequence });
            _probe.Tick();

            Assert.AreEqual(130, _probe.RttMs, "EMA: 100*0.7 + 200*0.3 = 130");
        }

        [Test]
        public void test_latency_probe_clears_reading_after_three_seconds_without_response()
        {
            _probe.Tick();
            _now = 0.040;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();
            Assert.AreEqual(40, _probe.RttMs);

            _now = 3.041;                                   // 距最后一次成功回包 3.001s
            _probe.Tick();

            Assert.IsNull(_probe.RttMs, "超过 3 秒无匹配回包必须显示 --");
        }

        [Test]
        public void test_latency_probe_accepts_zero_round_trip()
        {
            _probe.Tick();
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();

            Assert.AreEqual(0, _probe.RttMs, "同帧回包是合法的 0ms，不是 null");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode LatencyProbeTests
```

预期：编译失败，报 `CS0246`，找不到类型 `LatencyProbe`。这是 TDD 的红灯。

- [ ] **步骤 3：编写最少实现代码**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/LatencyProbe.cs`：

```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Cool.Protocol;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 应用层 RTT 探针：每 1 秒发一条 Heartbeat（服务端原样回显），按 Sequence 匹配回包并做 EMA。
    ///
    /// 线程模型：<see cref="Tick"/> 只在主线程调用；<see cref="OnResponse"/> 可在任意线程调用，
    /// 它在回调线程读一次 <see cref="Stopwatch"/> 时间戳后把 (回包, 到达时刻) 入队。
    /// 回包时间**不能等主线程下一帧再读**——那会把 0~1 帧的等待算进 RTT，60fps 下凭空多出约 16ms。
    /// 序列匹配、EMA、超时判定仍在主线程完成。
    ///
    /// 时钟：默认 Stopwatch（单调、线程安全、不受 Time.timeScale 影响——打击停顿会把 timeScale 设为 0）。
    /// 测试注入假时钟。
    /// </summary>
    internal sealed class LatencyProbe
    {
        internal const double ProbeIntervalSeconds = 1.0;
        internal const double TimeoutSeconds = 3.0;

        private readonly Action<Heartbeat> _send;
        private readonly Func<double> _clock;
        private readonly ConcurrentQueue<(Heartbeat heartbeat, double receivedAt)> _responses =
            new ConcurrentQueue<(Heartbeat heartbeat, double receivedAt)>();

        private uint _sequence;
        private uint _pendingSequence;
        private bool _hasPending;
        private double _pendingSendTime;
        private double _lastResponseTime;
        private double _lastSendTime;
        private bool _hasSample;
        private double _smoothedMs;

        internal LatencyProbe(Action<Heartbeat> send, Func<double>? clock = null)
        {
            _send = send;
            _clock = clock ?? DefaultClock;
            _lastSendTime = -ProbeIntervalSeconds;   // 首个 Tick 立即发包
        }

        private static double DefaultClock()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>平滑后的 RTT（毫秒）；无样本或超时后为 null。</summary>
        internal int? RttMs => _hasSample ? (int)Math.Round(_smoothedMs) : (int?)null;

        /// <summary>网络回调线程调用：读一次到达时刻并入队，其余留主线程。</summary>
        internal void OnResponse(Heartbeat heartbeat)
            => _responses.Enqueue((heartbeat, _clock()));

        /// <summary>主线程每帧调用（仅 InWorld）：消费回包、判超时、按节拍发包。</summary>
        internal void Tick()
        {
            double now = _clock();

            while (_responses.TryDequeue(out var response))
                Accept(response.heartbeat, response.receivedAt);

            // 1 秒节拍会不断覆盖“待回包”，所以超时按“最后一次成功回包”计时。
            if (_hasSample && now - _lastResponseTime > TimeoutSeconds)
            {
                _hasSample = false;
                _smoothedMs = 0.0;
            }

            if (now - _lastSendTime < ProbeIntervalSeconds) return;

            _lastSendTime = now;
            _sequence++;
            _pendingSequence = _sequence;
            _hasPending = true;
            _pendingSendTime = now;
            _send(new Heartbeat
            {
                ClientTimestamp = DateTime.UtcNow.Ticks,   // 协议字段既有语义，仅作诊断
                Sequence = _sequence
            });
        }

        private void Accept(Heartbeat response, double receivedAt)
        {
            if (!_hasPending || response.Sequence != _pendingSequence) return;

            _hasPending = false;
            double rttMs = (receivedAt - _pendingSendTime) * 1000.0;
            if (rttMs < 0.0) return;   // 时钟回跳：丢弃

            _smoothedMs = _hasSample ? _smoothedMs * 0.7 + rttMs * 0.3 : rttMs;
            _hasSample = true;
            _lastResponseTime = receivedAt;
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode LatencyProbeTests
```

预期：退出码 0，`LatencyProbeTests` 的 7 个测试全部通过。

- [ ] **步骤 5：Commit**

确认 Unity 已生成 `.meta`：

```bash
git status --short Assets/Scripts/Hotfix/GameSystems/SyncLayer/LatencyProbe.cs* \
  Assets/Scripts/Tests/Editor/SyncLayer/LatencyProbeTests.cs*
```

然后：

```bash
git add \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/LatencyProbe.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/LatencyProbe.cs.meta \
  Assets/Scripts/Tests/Editor/SyncLayer/LatencyProbeTests.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/LatencyProbeTests.cs.meta
git commit -m "feat(sync): add application-level latency probe"
```

---

## 任务 2：ClientNetworkManager 接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/ClientLatencyWiringTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/SyncLayer/ClientLatencyWiringTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 管理器接线：进世界后的 Update 必须发出应用层 Heartbeat；回显同 Sequence 后必须暴露 RTT。
    /// 复用 ClientRespawnNoticeTests 的 RecordingClient 模式（与生产同一条 adapter → dispatcher 路）。
    /// </summary>
    public class ClientLatencyWiringTests
    {
        private sealed class RecordingClient : INetworkClient
        {
            public readonly List<(MessageId id, IMessage msg)> Sent = new List<(MessageId, IMessage)>();
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
                => Sent.Add((messageId, message));
            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;
            public void Dispose() { }

            public void Emit(MessageId id, IMessage message) => MessageReceived?.Invoke(id, message);
        }

        private GameObject _root = null!;
        private RecordingClient _client = null!;
        private ClientNetworkManager _manager = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("LatencyWiringTestRoot");
            _client = new RecordingClient();
            _manager = new ClientNetworkManager(
                _client, new RemoteEntityRegistry(_root.transform), "Latency_Tester");
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private void EnterWorld()
        {
            // 直接投递登录响应把状态推到 InWorld（OnLoginResponse 不校验前置状态）。
            // 不调 ConnectAndLogin 的 async void：避免测试依赖 await 的同步续体时机。
            _client.Emit(MessageId.LoginResponse, new LoginResponse { Success = true, PlayerId = 7 });
            _manager.Update(0.016f);
        }

        [Test]
        public void test_client_network_manager_sends_heartbeat_in_world()
        {
            EnterWorld();

            Assert.IsTrue(_client.Sent.Exists(s => s.id == MessageId.Heartbeat),
                "进世界后的第一次 Update 必须发出应用层 Heartbeat");
        }

        [Test]
        public void test_client_network_manager_exposes_rtt_from_echo()
        {
            EnterWorld();

            var entry = _client.Sent.Find(s => s.id == MessageId.Heartbeat);
            Assert.IsNotNull(entry.msg, "前置：进世界后必须已发出 Heartbeat");
            var heartbeat = (Heartbeat)entry.msg;

            _client.Emit(MessageId.Heartbeat, heartbeat);
            _manager.Update(0.016f);

            Assert.IsTrue(_manager.RttMs.HasValue, "回显同 Sequence 后必须暴露 RTT");
            Assert.GreaterOrEqual(_manager.RttMs.Value, 0);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode ClientLatencyWiringTests
```

预期：编译失败，报 `CS1061`，`ClientNetworkManager` 没有 `RttMs`；或断言“没有发出 Heartbeat”。

- [ ] **步骤 3：编写最少实现代码**

修改 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`。

**3a. 字段**（在 `private readonly ClockSyncDriver _clockSync;` 之后）：

```csharp
        private readonly LatencyProbe _latencyProbe;
```

**3b. 构造函数**（在 `_clockSync = new ClockSyncDriver(...)` 之后）：

```csharp
            _latencyProbe = new LatencyProbe(hb => _client.Send(MessageId.Heartbeat, hb));
```

并在既有 `_dispatcher.Subscribe(...)` 列表末尾加：

```csharp
            _dispatcher.Subscribe(MessageId.Heartbeat, OnHeartbeatResponse);
```

**3c. 对外属性**（在 `ClockSmoothedOffsetTicks` 属性之后）：

```csharp
        /// <summary>应用层心跳探测的平滑 RTT（毫秒）；无样本或超时后为 null。</summary>
        public int? RttMs => _latencyProbe.RttMs;
```

**3d. Update**（在 `_clockSync.Tick(deltaSeconds);` 之后）：

```csharp
            _latencyProbe.Tick();
```

**3e. 回包处理器**（在 `OnClockSyncResponse` 方法之后）：

```csharp
        private void OnHeartbeatResponse(IMessage message)
        {
            _latencyProbe.OnResponse((Heartbeat)message);
        }
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode ClientLatencyWiringTests
```

预期：退出码 0，2 个测试全部通过。

- [ ] **步骤 5：Commit**

```bash
git add \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/ClientLatencyWiringTests.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/ClientLatencyWiringTests.cs.meta
git commit -m "feat(sync): wire latency probe into client network manager"
```

---

## 任务 3：PlayerHudPanel 与预制体

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs`
- 修改：`Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab`
- 测试：`Assets/Scripts/Tests/PlayMode/PlayerHudNetStatsTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/PlayMode/PlayerHudNetStatsTests.cs`：

```csharp
using System.Reflection;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// HUD 联网读数：预制体自带默认隐藏的 TMP 子物体，SetNetStats 负责显隐与写入。
    /// 字段连线用反射断言——预制体被改坏时当场报红。
    /// </summary>
    public class PlayerHudNetStatsTests
    {
        private GameObject _panelInstance;
        private PlayerHudPanel _panel;

        [SetUp]
        public void SetUp()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/PlayerHudPanel");
            Assert.IsNotNull(prefab, "HUD 预制体缺失：Resources/Prefabs/UI/PlayerHudPanel");
            _panelInstance = Object.Instantiate(prefab);
            _panel = _panelInstance.GetComponent<PlayerHudPanel>();
            Assert.IsNotNull(_panel, "HUD 预制体上没有 PlayerHudPanel 组件");
        }

        [TearDown]
        public void TearDown()
        {
            if (_panelInstance != null) Object.DestroyImmediate(_panelInstance);
        }

        [Test]
        public void test_player_hud_net_stats_text_is_wired()
        {
            Assert.IsNotNull(GetNetStatsText(), "_netStatsText 未连线（预制体改动？）");
        }

        [Test]
        public void test_player_hud_set_net_stats_shows_text()
        {
            var text = GetNetStatsText();
            Assert.IsFalse(text.gameObject.activeSelf, "预制体里的 NetStatsText 必须默认隐藏");

            _panel.SetNetStats("FPS 60  延迟 23ms");

            Assert.IsTrue(text.gameObject.activeSelf);
            Assert.AreEqual("FPS 60  延迟 23ms", text.text);
        }

        [Test]
        public void test_player_hud_set_net_stats_null_hides_text()
        {
            var text = GetNetStatsText();
            _panel.SetNetStats("FPS 60  延迟 23ms");

            _panel.SetNetStats(null);

            Assert.IsFalse(text.gameObject.activeSelf, "null 必须隐藏读数（断线/回选人路径）");
        }

        private TMP_Text GetNetStatsText()
        {
            var field = typeof(PlayerHudPanel).GetField(
                "_netStatsText", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "找不到 _netStatsText 字段（PlayerHudPanel 改动？）");
            return field.GetValue(_panel) as TMP_Text;
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh PlayMode PlayerHudNetStatsTests
```

预期：编译失败，报 `CS1061`，`PlayerHudPanel` 没有 `SetNetStats`。

- [ ] **步骤 3：编写最少实现代码**

修改 `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs`。

**3a. 字段**（在 `_buffIconPrefab` 之后）：

```csharp
        [SerializeField] private TMP_Text _netStatsText;
```

**3b. 方法**（在 `Unbind` 之后、`UpdateHP` 之前）：

```csharp
        /// <summary>联网读数（FPS + RTT）；null/空串 = 隐藏。与 Bind 的玩家属性数据源无关。</summary>
        public void SetNetStats(string? text)
        {
            if (_netStatsText == null) return;

            bool show = !string.IsNullOrEmpty(text);
            if (_netStatsText.gameObject.activeSelf != show)
                _netStatsText.gameObject.SetActive(show);
            if (show)
                _netStatsText.text = text;
        }
```

- [ ] **步骤 4：运行测试验证失败（预制体尚未连线）**

运行：

```bash
bash tools/unity-tests.sh PlayMode PlayerHudNetStatsTests
```

预期：`test_player_hud_net_stats_text_is_wired` 失败（`_netStatsText` 为 null）；另外两个测试也会失败或抛 `NullReferenceException`——`SetNetStats` 在未连线时静默返回，随后断言对象 active 失败。

- [ ] **步骤 5：用 Unity MCP 编辑预制体**

先确认 Unity 在 **Edit Mode**（不是 Play Mode），并关闭 PlayerHudPanel 预制体编辑窗口。然后调用 Unity MCP `script-execute`：

- `className`: `Script`
- `methodName`: `Main`
- `csharpCode`:

```csharp
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Script
{
    public static string Main()
    {
        const string prefabPath = "Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab";

        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var panelType = Type.GetType("Hotfix.GameSystems.UI.PlayerHudPanel, GameSys");
            if (panelType == null) throw new Exception("找不到 GameSys 里的 PlayerHudPanel 类型");

            var panel = root.GetComponent(panelType);
            if (panel == null) throw new Exception("PlayerHudPanel.prefab 根节点上没有 PlayerHudPanel 组件");

            var old = root.transform.Find("NetStatsText");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var textGo = new GameObject("NetStatsText", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);

            var rect = (RectTransform)textGo.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -100f);
            rect.sizeDelta = new Vector2(360f, 24f);

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/PreRes/Font/ZhunYuan SDF.asset");
            if (font == null) throw new Exception("找不到 ZhunYuan SDF 字体资产");

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.fontSize = 20f;
            text.alignment = TextAlignmentOptions.Left;
            text.color = Color.white;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            // 预览用 ASCII：编辑器渲染中文会触发动态字体补字形、可能脏化 ZhunYuan SDF 资产。
            // 运行时由 SetNetStats 写入真正的中文文本。
            text.text = "FPS 60  PING 23ms";

            var shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(1f, -1f);

            textGo.SetActive(false);   // 单机/未进世界默认不显示

            var so = new SerializedObject(panel);
            var prop = so.FindProperty("_netStatsText");
            if (prop == null) throw new Exception("PlayerHudPanel 上没有 _netStatsText 字段（脚本未编译？）");
            prop.objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "NetStatsText added and wired: " + prefabPath;
    }
}
```

预期返回：`NetStatsText added and wired: Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab`。

**失败回退（仅当 `script-execute` 不可用）：** 在 Unity 里打开 `PlayerHudPanel.prefab`，在根节点下建 `TextMeshPro - Text (UI)` 子物体，命名 `NetStatsText`，设置 RectTransform 锚点左上、pivot `(0,1)`、`Pos X=0 / Pos Y=-100`、宽 360 高 24，字体 `Assets/PreRes/Font/ZhunYuan SDF.asset`、字号 20、左对齐、白色、Raycast Target 关闭，加 `Shadow`（偏移 1,-1、黑色 50%），预览文本留空或写 ASCII（避免编辑器渲染中文时动态字体补字形），默认取消激活；把该组件拖到 `PlayerHudPanel` 的 `_netStatsText` 字段并保存预制体。

- [ ] **步骤 6：运行测试验证通过**

运行：

```bash
bash tools/unity-tests.sh PlayMode PlayerHudNetStatsTests
```

预期：退出码 0，3 个测试全部通过。

- [ ] **步骤 7：Commit**

```bash
git add \
  Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs \
  Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab \
  Assets/Scripts/Tests/PlayMode/PlayerHudNetStatsTests.cs \
  Assets/Scripts/Tests/PlayMode/PlayerHudNetStatsTests.cs.meta
git commit -m "feat(ui): add net stats text to player HUD"
```

---

## 任务 4：ClientNetworkHost 帧率与推送

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/ClientNetworkHostNetStatsTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/SyncLayer/ClientNetworkHostNetStatsTests.cs`：

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// HUD 联网读数的文本格式（`FormatNetStats` 是 internal，测试程序集有 InternalsVisibleTo）。
    /// 数字必须能被断言：延迟有样本带 ms，无样本显示 --。
    /// </summary>
    public class ClientNetworkHostNetStatsTests
    {
        [Test]
        public void test_net_stats_format_includes_fps_and_latency()
        {
            Assert.AreEqual("FPS 60  延迟 23ms", ClientNetworkHost.FormatNetStats(60, 23));
        }

        [Test]
        public void test_net_stats_format_without_sample_shows_dash()
        {
            Assert.AreEqual("FPS 60  延迟 --", ClientNetworkHost.FormatNetStats(60, null));
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode ClientNetworkHostNetStatsTests
```

预期：编译失败，报 `CS0117`，`ClientNetworkHost` 没有 `FormatNetStats`。

- [ ] **步骤 3：编写最少实现代码**

修改 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`。

**3a. 字段**（在 `private NetworkTargetSelector? _networkTargetSelector;` 之后）：

```csharp
        // 联网 HUD 读数（FPS + RTT）：HUD 面板缓存与 0.5 秒统计窗口
        private PlayerHudPanel? _hudPanel;
        private float _fpsWindowSeconds;
        private int _fpsFrames;
        private int _displayedFps;
        private float _netStatsPushTimer;
        private bool _netStatsVisible;
```

**3b. Update**（在 `_manager.Update(Time.deltaTime);` 之后）：

```csharp
            UpdateNetStats(Time.unscaledDeltaTime);
```

**3c. 方法**（在 `FormatTierReadout` 之后、`HandleEnterWorld` 之前插入）：

```csharp
        /// <summary>
        /// 联网 HUD 读数：0.5 秒窗口算 FPS，每 0.5 秒推一次文本（TMP 每帧赋值会触发网格重建）。
        /// 未进世界/单机一律隐藏。HUD 面板懒获取并缓存，UIManager 未就绪时静默跳过。
        /// </summary>
        private void UpdateNetStats(float unscaledDeltaSeconds)
        {
            if (_manager == null || !_inWorld)
            {
                HideNetStats();
                return;
            }

            _fpsWindowSeconds += unscaledDeltaSeconds;
            _fpsFrames++;
            _netStatsPushTimer += unscaledDeltaSeconds;

            if (_fpsWindowSeconds >= 0.5f)
            {
                _displayedFps = Mathf.RoundToInt(_fpsFrames / _fpsWindowSeconds);
                _fpsWindowSeconds = 0f;
                _fpsFrames = 0;
            }

            if (_netStatsPushTimer < 0.5f) return;
            _netStatsPushTimer = 0f;

            var hud = EnsureHudPanel();
            if (hud == null) return;

            hud.SetNetStats(FormatNetStats(_displayedFps, _manager.RttMs));
            _netStatsVisible = true;
        }

        private PlayerHudPanel? EnsureHudPanel()
        {
            if (_hudPanel != null) return _hudPanel;

            var ui = UIManager.Instance;
            if (ui == null) return null;

            _hudPanel = ui.GetPanel<PlayerHudPanel>();
            return _hudPanel;
        }

        /// <summary>隐藏读数并清空统计窗口（幂等；单机/断线/销毁共用）。</summary>
        private void HideNetStats()
        {
            _fpsWindowSeconds = 0f;
            _fpsFrames = 0;
            _netStatsPushTimer = 0f;
            _displayedFps = 0;

            if (!_netStatsVisible) return;
            _netStatsVisible = false;
            _hudPanel?.SetNetStats(null);
        }

        /// <summary>HUD 读数文本。抽成纯函数供单测钉住格式。</summary>
        internal static string FormatNetStats(int fps, int? rttMs)
            => "FPS " + fps + "  延迟 " + (rttMs.HasValue ? rttMs.Value + "ms" : "--");
```

**3d. 断线隐藏**（在 `HandleDisconnected` 的 `OnDisconnected?.Invoke(reason);` 之后）：

```csharp
            HideNetStats();
```

**3e. 断开清理**（在 `Disconnect()` 方法体第一行）：

```csharp
            HideNetStats();
```

**3f. 宿主销毁**（在 `OnDestroy()` 方法体第一行）：

```csharp
            HideNetStats();
```

- [ ] **步骤 4：运行测试验证通过**

先运行新增的格式测试：

```bash
bash tools/unity-tests.sh EditMode ClientNetworkHostNetStatsTests
```

预期：2 个测试通过。

再跑既有调试浮层回归（必须仍通过）：

```bash
bash tools/unity-tests.sh EditMode ClientNetworkHostOverlayTests
```

预期：既有 2 个测试通过，`FormatTierReadout` 行为未变。

- [ ] **步骤 5：Commit**

```bash
git add \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/ClientNetworkHostNetStatsTests.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/ClientNetworkHostNetStatsTests.cs.meta
git commit -m "feat(sync): show fps and rtt below the HUD"
```

---

## 任务 5：文档与最终验证

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md`

- [ ] **步骤 1：更新 SyncLayer README**

在“入口与职责”的文件表里，`ClientNetworkHost.cs` 行之后新增一行：

```markdown
| `LatencyProbe.cs` | 应用层 RTT 探针（Heartbeat 1Hz 往返 + EMA；3 秒无回包显示 `--`） |
```

在“## 远端实体表现”之前新增一节：

```markdown
## 联网 HUD 读数（FPS + 延迟）

- 仅联网进世界后显示；`ClientNetworkHost` 每 0.5 秒把 `FPS xx  延迟 xxms` 推给
  `PlayerHudPanel.SetNetStats`，断线 / 回选人时置空隐藏。单机默认不显示。
- 延迟 = `LatencyProbe` 的平滑 RTT：每 1 秒发一条 `Heartbeat`（服务端原样回显），
  按 `Sequence` 匹配；回包到达时刻在回调线程用 `Stopwatch` 采样，避免把等待下一帧算进 RTT。
  3 秒无匹配回包显示 `延迟 --`。
- 左下角调试浮层保留原有 `FPS=` 与分档读数（任务 10 实测口径不变）。
```

- [ ] **步骤 2：更新 UI README**

在“## 面板”一节末尾新增：

```markdown
HUD 联网读数：`PlayerHudPanel` 在 HP/MP 与 buff 行下方预留 `NetStatsText`（TMP 子物体，默认隐藏）。
联网时由 `ClientNetworkHost` 调用 `SetNetStats(string?)` 写入 `FPS xx  延迟 xxms`；传 null/空串隐藏。
单机模式不显示。
```

- [ ] **步骤 3：跑完整的目标测试集**

依次运行，全部要求退出码 0：

```bash
bash tools/unity-tests.sh EditMode LatencyProbeTests
bash tools/unity-tests.sh EditMode ClientLatencyWiringTests
bash tools/unity-tests.sh EditMode ClientNetworkHostNetStatsTests
bash tools/unity-tests.sh EditMode ClientNetworkHostOverlayTests
bash tools/unity-tests.sh PlayMode PlayerHudNetStatsTests
```

预期：7 + 2 + 2 + 2 + 3 个测试全部通过。

- [ ] **步骤 4：手动验收（环境可用时执行）**

按 [network-playtest.md](../guides/network-playtest.md) 起服务端并进入联网世界：

```bash
dotnet build Server/Server3C.csproj
dotnet Server/bin/Debug/net8.0/Server3C.dll 8000
```

Unity 进入 Play Mode，选联网、连接 `127.0.0.1:8000`，进入世界。逐条确认：

- [ ] HUD 的左下（HP/MP 条下方）出现 `FPS xx  延迟 xxms`，不遮挡头像与血条。
- [ ] 延迟约每秒变化一次；本地回环通常在 0–20ms。帧率约每 0.5 秒变化一次。
- [ ] 选单机模式进入世界：读数不出现。
- [ ] 停掉服务端：断线回选人界面后读数消失。
- [ ] 用 Unity MCP `screenshot-game-view` 截图留证（如环境不可用，明确记录“手动验收未执行”，不要把它说成已通过）。

- [ ] **步骤 5：Commit**

```bash
git add \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
  Assets/Scripts/Hotfix/GameSystems/UI/README.md
git commit -m "docs: document net stats HUD readout"
```

---

## 自检

**规格覆盖度**

| 规格章节 | 对应任务 |
|---|---|
| 3.1 数据流 / 线程模型 | 任务 1（探针）、任务 2（接线）、任务 4（推送） |
| 3.2 LatencyProbe（节拍/回包/EMA/超时） | 任务 1 |
| 3.3 ClientNetworkManager 接线 | 任务 2 |
| 3.4 帧率与文本 | 任务 4 |
| 3.5 HUD 展示与预制体 | 任务 3 |
| 4 错误与边界 | 任务 1/2/4 的代码与测试 |
| 5 测试 | 任务 1–4 |
| 6 验收 | 任务 5 步骤 4 |
| 7 文档更新 | 任务 5 步骤 1–2 |

**类型一致性**

- `LatencyProbe.RttMs` → `int?`
- `ClientNetworkManager.RttMs` → `int?`
- `ClientNetworkHost.FormatNetStats(int fps, int? rttMs)` → `string`
- `PlayerHudPanel.SetNetStats(string? text)`
- 预制体字段名统一为 `_netStatsText`，子物体统一为 `NetStatsText`。

**无占位符**

- 所有代码步骤都给出完整可编译代码；所有测试步骤都给出断言与预期失败/通过结果。
- 唯一的环境依赖（Unity MCP 预制体编辑）给了完整的 `script-execute` 代码与手动词条回退。
