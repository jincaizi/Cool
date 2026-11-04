# 联网延迟与帧率 HUD 读数设计

日期：2026-11-05
运行模式：联网（服务端 + WebSocket 客户端）；单机零影响
相关模块：[SyncLayer](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)、[UI](../../../Assets/Scripts/Hotfix/GameSystems/UI/README.md)、[协议](../../../Assets/Scripts/AOT/Protocol/README.md)

## 1. 背景与目标

联网模式下玩家侧目前没有任何延迟反馈；帧率只出现在左下角调试浮层，且该浮层定位为开发读数，不适合作为玩家可见信息。

需求：在 HUD（左上角 HP/MP 面板）下方显示当前 **RTT 延迟** 与 **帧率**，仅联网模式可见。

**目标**

- 联网进世界后，HUD 下方出现 `FPS 60  延迟 23ms` 读数。
- 延迟每秒刷新一次，帧率每 0.5 秒刷新一次。
- 单机模式、未进世界、断线返回选人界面时不显示。
- 不新增协议消息，不改服务端。

**非目标**

- 不做丢包率、抖动（jitter）、单向延迟统计。
- 不做延迟颜色分级/告警。
- 不移动左下角现有调试浮层，也不删除其中的 `FPS=` 读数（任务 10 实测与 `ClientNetworkHostOverlayTests` 仍依赖它）。
- 不为单机模式提供帧率显示。

## 2. 现状

- 协议里已有 `Heartbeat`（`Assets/Scripts/AOT/Protocol/Messages/SystemMessages.cs`），字段为 `ClientTimestamp` 与 `Sequence`；服务端收到后**原样回显**（`Server/Server3C.cs` 的 `case Heartbeat`），不做任何修改。客户端目前只有 KCP 传输层私有的心跳循环，应用层从未发送过 `Heartbeat`。
- `WebSocketClientAdapter` 把所有解码后的消息统一抛到 `MessageReceived`，心跳回包能到达 `MessageDispatcher`。
- `ClientNetworkManager` 已有统一的“网络回调线程只入队、主线程 Update 消费”封送模式（快照、名册、离场、纠偏响应）。
- `PlayerHudPanel` 是左上角 HUD 面板，根节点锚定画面左上 `(40, -40)`，参考分辨率 1920×1080，CanvasScaler `MatchWidthOrHeight = 0.5`。
- 当前 `Sys3CEntryStatsProvider.ActiveBuffs` 恒为 `Array.Empty<BuffInfo>()`，HUD 的 buff 行实际为空。

## 3. 设计

### 3.1 数据流

```
ClientNetworkHost.Update（主线程）
  → ClientNetworkManager.Update(deltaSeconds)          // 仅 InWorld
      → LatencyProbe.Tick()                            // 主线程：节拍 / 发包 / 消费回包 / 算 RTT
  → 每 0.5s：hud.SetNetStats(FormatNetStats(fps, manager.RttMs))

网络回调线程
  Heartbeat 回包 → 读一次 Stopwatch 时间戳 → (hb, receivedAt) 入队
```

- 延迟 = **往返时间 RTT**（毫秒），不是单向延迟。
- 发送时刻与回包到达时刻都用 `Stopwatch`（线程安全、单调、不受 `Time.timeScale` 影响）。回包时间戳在回调线程读一次、随回包入队；序列匹配、RTT 计算、EMA、超时判定在主线程完成。
- **不能等主线程 `Update` 再读回包时间**：那会把“等下一帧”的 0–1 帧延迟算进 RTT，60fps 下凭空多出约 16ms，读数会随帧率变化。其余状态（待回包序列、EMA、超时）只在主线程读写。

### 3.2 `LatencyProbe`

新文件：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/LatencyProbe.cs`，`internal sealed class`。

**构造**

```csharp
internal LatencyProbe(Action<Heartbeat> send, Func<double>? clock = null)
```

- `clock` 默认 `() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency`（秒）。`Stopwatch` 单调、线程安全、不受 `Time.timeScale` 影响（`HitStopManager` 会把 timeScale 设为 0），WebGL 可用。
- 测试注入假时钟，不依赖真实时间。

**节拍与发包**

- 首次 `Tick()` 立即发送（`_lastSendTime` 初始为 `-ProbeIntervalSeconds`）。
- 之后每 1 秒发送一条 `Heartbeat`：
  - `Sequence` 自增（`uint`，允许回绕）。
  - `ClientTimestamp = DateTime.UtcNow.Ticks`（保持协议字段既有语义，仅作诊断；RTT 不依赖回显的时间戳）。
  - 发送前记录 `_pendingSequence` 与 `_pendingSendTime = clock()`。
- 同一时刻只保留一个待回包样本；若 RTT 超过 1 秒，下一次发送覆盖旧样本，旧回包按 `Sequence` 不匹配丢弃——属于可接受的极端网络情况。

**回包与 RTT**

- `OnResponse(Heartbeat hb)` 在回调线程读 `receivedAt = clock()`，把 `(hb, receivedAt)` 入队（`ConcurrentQueue<(Heartbeat, double)>`）；默认时钟 `Stopwatch` 线程安全。
- `Tick()` 在主线程排空队列：仅当 `hb.Sequence == _pendingSequence` 且存在待回包时才接受。
- `rttMs = (receivedAt - _pendingSendTime) * 1000.0`；允许 0ms，负数丢弃。
- EMA：首个样本直接采纳；之后 `smoothed = smoothed * 0.7 + rttMs * 0.3`。

**超时**

- 节拍固定每 1 秒发一次，每次发送都会覆盖“待回包”，因此超时不能按“发请求后 3 秒”判断。
- 改为记录 `_lastResponseTime`（最后一次**匹配成功**的回包时刻）：已获得过样本后，若 `clock() - _lastResponseTime > 3.0`，清空读数（`RttMs = null`）。从未获得样本时本来就是 null，显示器显示 `延迟 --`。
- 超时不影响发包节拍；下一次匹配回包会重新建立样本并重置 EMA。

**对外 API**

```csharp
public int? RttMs { get; }   // 无样本或超时后为 null；否则为 EMA 四舍五入的毫秒数
```

### 3.3 `ClientNetworkManager` 接线

- 构造函数里 `_dispatcher.Subscribe(MessageId.Heartbeat, OnHeartbeatResponse)`；处理器转调 `_latencyProbe.OnResponse`。
- 构造函数创建 `_latencyProbe = new LatencyProbe(hb => _client.Send(MessageId.Heartbeat, hb))`。
- `Update` 在 `InWorld` 分支内、`_clockSync.Tick` 之后调用 `_latencyProbe.Tick()`。`Update` 的既有早退保证：未进世界不发包。
- 暴露：

```csharp
public int? RttMs => _latencyProbe.RttMs;
```

- 传入的 `deltaSeconds` 不参与探针计时；探针只读自己的时钟。
- `Dispose` 无需额外处理：探针随 manager 一起丢弃。

### 3.4 帧率与文本

`ClientNetworkHost` 负责帧率与推送：

- 维护 0.5 秒窗口：`_fpsWindowSeconds += Time.unscaledDeltaTime`、`_fpsFrames++`；窗口 ≥ 0.5s 时 `fps = Mathf.RoundToInt(_fpsFrames / _fpsWindowSeconds)`，然后清零。
- 每 0.5 秒推送一次文本（TMP 每帧赋值会触发网格重建，2Hz 足够）。进入世界后第一次推送发生在第一个 0.5 秒窗口完成时，因此读数在进世界后约 0.5 秒出现。
- 仅在 `_manager != null && _inWorld` 时显示；否则调用一次 `HideNetStats()`。
- HUD 面板懒获取并缓存：`UIManager.Instance?.GetPanel<PlayerHudPanel>()`；未就绪时静默跳过（与 `TargetPanel` 的现有用法一致）。
- 断线 / 回选人 / 宿主销毁：`HideNetStats()`（`HandleDisconnected`、`Disconnect`、`OnDestroy`）。
- 重新进入世界时重新开始统计窗口，不沿用上次会话的 FPS。

格式化抽成纯函数，供 EditMode 测试钉住：

```csharp
internal static string FormatNetStats(int fps, int? rttMs)
// rttMs 有值 → "FPS 60  延迟 23ms"
// rttMs 为 null → "FPS 60  延迟 --"
```

### 3.5 HUD 展示

**`PlayerHudPanel`**

- 新增序列化字段：`[SerializeField] private TMP_Text _netStatsText;`
- 新增方法：

```csharp
public void SetNetStats(string? text)
// null / 空串 → 隐藏 _netStatsText.gameObject
// 非空 → 显示并写入 text
```

- 方法不依赖 `Bind` / `_provider`，联网读数与玩家属性绑定互不影响。

**预制体 `PlayerHudPanel.prefab`**

- 在 HUD 根节点下新增 `NetStatsText`（`TextMeshProUGUI`），默认 `m_IsActive: 0`。
- 布局：锚定左上、pivot `(0, 1)`、左对齐，放在 HP/MP 条下方（约 `anchoredPosition = (0, -100)`，尺寸约 `360×24`，参照分辨率单位）。实现时打开预制体截图校准，确保不遮挡头像与 HP/MP 条。
- buff 行当前恒为空（`ActiveBuffs` 返回空数组），所以该位置可用；若将来启用 buff，读数会与 buff 行重叠，需要重新校准位置——记为已知限制。
- 字号 **20** 参照像素（HUD 现有文本为 9–13，读数略大以便桌面看清）。
- 字体：`Assets/PreRes/Font/ZhunYuan SDF.asset`（与 HUD 名字文本同一动态字体，中文可运行时补字形）。
- 颜色白色；用 **TMP 材质 underlay 阴影**保证亮背景可读：`_UnderlayColor` 黑色 50%、`_UnderlayOffsetX/Y` 约 0.5/-0.5、`_UnderlaySoftness` 约 0.1。材质变体存为 `Assets/PreRes/Font/ZhunYuan SDF - NetStats Underlay.mat`，赋给文本的 `fontSharedMaterial`。
  **必须在材质上启用 `UNDERLAY_ON` 关键字**（等价于 TMP 材质 Inspector 的 Underlay Type = Normal）。未启用时 `TMP_SDF.shader` 的 underlay 分支会被编译掉，所有 `_Underlay*` 属性都不生效。
  **不要用 uGUI `Shadow`**——TMP 走自己的 mesh 生成路径、不处理 `IMeshModifier`，该组件对 TMP 文本完全无效。`Raycast Target = false`。
- 单机模式该子物体保持 inactive，不需要任何代码路径去打开它。

**可读性限制**

- HUD 使用 1920×1080 参考分辨率；在约 390px 竖屏下整个画布缩到约 0.4×，读数会与 HUD 其他文本一样偏小。本次不额外做移动端字号适配，验收只要求不遮挡、不溢出。

## 4. 错误与边界

| 情况 | 行为 |
|---|---|
| 未进世界 / 单机 | 不发包，文本隐藏 |
| 进世界首个 Tick | 立即发第一条 Heartbeat；通常 <100ms 内拿到首个样本 |
| 回包丢失 | 下一条继续探测；3 秒无匹配回包后显示 `延迟 --` |
| 迟到/重复回包 | `Sequence` 不匹配，丢弃 |
| 断线 / 连接失败 | 管理器回到 Disconnected，宿主隐藏文本并走既有返回选人流程 |
| HUD 面板尚未加载 | 静默跳过，下一帧重试 |
| `Time.timeScale = 0`（打击停顿） | 探针使用 `Stopwatch`，节拍与 RTT 都不受影响 |
| 服务端本地回环 | 应用层 RTT 含服务端约 66ms tick，常见约 60–130ms，属正常（不是纯网络 RTT） |

## 5. 测试

### EditMode

`Assets/Scripts/Tests/Editor/SyncLayer/LatencyProbeTests.cs`（命名按 `.claude/rules/test-standards.md` 的 `test_[system]_[scenario]_[expected_result]`）：

1. `test_latency_probe_first_tick_sends_immediately` —— 首个 Tick 发一条 Heartbeat。
2. `test_latency_probe_sends_at_one_second_cadence` —— 0.9s 内不发第二条，1.0s 后发。
3. `test_latency_probe_matching_response_reports_round_trip` —— 注入时钟推进 40ms，回包后 `RttMs == 40`。
4. `test_latency_probe_ignores_stale_sequence` —— 旧 Sequence 回包不改变读数。
5. `test_latency_probe_smooths_samples_with_ema` —— 首样本 100ms、次样本 200ms → 130ms。
6. `test_latency_probe_clears_reading_after_three_seconds_without_response` —— 先取得一个样本，再推进 3 秒无回包，`RttMs == null`。
7. `test_latency_probe_accepts_zero_round_trip` —— 同时刻回包得到 0ms，不是 null。

`Assets/Scripts/Tests/Editor/SyncLayer/ClientLatencyWiringTests.cs`（复用 `ClientRespawnNoticeTests` 的 `RecordingClient` 模式）：

8. `test_client_network_manager_sends_heartbeat_in_world` —— 登录回包 → Update → `Sent` 中出现 `Heartbeat`。
9. `test_client_network_manager_exposes_rtt_from_echo` —— 回显同 Sequence → Update → `RttMs.HasValue`。

`ClientNetworkHostOverlayTests` 增加（或新建同目录测试）：

10. `test_net_stats_format_includes_fps_and_latency` —— `"FPS 60  延迟 23ms"`。
11. `test_net_stats_format_without_sample_shows_dash` —— `"FPS 60  延迟 --"`。

### PlayMode

`Assets/Scripts/Tests/PlayMode/PlayerHudNetStatsTests.cs`：

12. `test_player_hud_set_net_stats_shows_text` —— 实例化预制体，`SetNetStats("FPS 60  延迟 23ms")` 后 TMP 文本正确且对象 active；预制体默认 inactive。
13. `test_player_hud_set_net_stats_null_hides_text` —— `SetNetStats(null)` 后对象 inactive。
14. `test_player_hud_net_stats_text_is_wired` —— 反射取 `_netStatsText` 断言非 null，预制体改坏时当场报红。
15. `test_player_hud_net_stats_uses_tmp_underlay_shadow` —— 断言 `_netStatsText.fontSharedMaterial.IsKeywordEnabled("UNDERLAY_ON")`；这个 bug 曾因关键字未启用而让 underlay 属性全部失效，必须有能抓住它的回归测试。

## 6. 验收

- 联网进世界后 1 秒内 HUD 下方出现 `FPS xx  延迟 xxms`；延迟约每秒变化一次，帧率约每 0.5 秒变化一次。
- 单机模式不出现该读数。
- 断线返回选人界面后读数消失；重新联网进世界后重新出现。
- 服务端进程卡住、连接仍存活且不再回包时，3 秒后延迟变为 `--`，帧率继续更新。
- 约 390px 竖屏下读数不遮挡头像、HP/MP 条，也不与顶部中央的目标面板重叠（buff 行当前恒空，不参与判据）。
- 左下角调试浮层保持原样（含 `FPS=` 与分档读数）。

## 7. 文档更新

- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`：新增 `LatencyProbe.cs` 行；补充“HUD 联网读数（FPS + RTT）”说明。
- `Assets/Scripts/Hotfix/GameSystems/UI/README.md`：`PlayerHudPanel` 新增 `_netStatsText` / `SetNetStats` 说明。
