# P2 客户端网络宿主接线指南（ClientNetworkHost）

> **它是什么**：一个挂在 Unity 客户端世界场景里的 MonoBehaviour，负责把 P2 已建好但只在测试宿主里运行的网络组件（ClientNetworkManager / MovementReporter / NetMovementPolicy / RemoteEntityRegistry + RemoteInterpolator）接进真实游戏运行时。**它不是服务器**——服务器是 `Server/` 的独立 .NET 进程。

## 为什么需要它

P2 全部验收（MovementSyncTests 等）都是测试代码自己 `new ClientNetworkManager(...)` 手动接线。真实游戏场景里没有任何东西：
- 创建并驱动 `ClientNetworkManager`（时钟/快照封送/上报节流/插值渲染）
- 登录进世界
- 把本地角色（Sys3CEntry）接上移动上报与平滑纠偏

`ClientNetworkHost` 补齐这三件事。服务器侧无需任何改动。

## 场景接线步骤

1. **服务器先跑起来**（见 `docs/superpowers/guides/p1-dual-client-dev.md`）：
   ```bash
   dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000
   ```
2. **把 `ClientNetworkHost` 组件挂到世界场景任意 GameObject**（建议挂到一个空 "Net" 根物体）。
3. **Inspector 配置**：
   - `Server Host` / `Server Port`：默认 `127.0.0.1:8000`（真机联调改局域网 IP）
   - `Username`：登录名（当前服务器无重名校验/鉴权）
   - `Local Entry`：**可选**——拖入本地玩家的 Sys3CEntry；留空则运行时自动查找场景内第一个 Sys3CEntry
4. **触发登录**：调用 `Connect()`（代码里，例如进世界/开始按钮回调）：
   ```csharp
   netHost.Connect();                       // 用 Inspector 默认值
   netHost.Connect("PlayerA", "10.0.0.5", 8000);  // 或覆盖
   ```
5. 完成后自动发生：
   - 登录 → `OnEnterWorld` 触发
   - 本地角色就绪后自动装配：`MovementPolicy` 换成 `NetMovementPolicy`（本地物理照常 + 服务器纠偏平滑拉回）、每 `ServerTickRateMs` 上报位置/旋转/真实速度、预测帧对账
   - 远程玩家以胶囊占位渲染（`RemoteEntityRegistry`），2-tick 延迟插值平滑位移

## 行为细节与边界

| 项 | 行为 |
|---|---|
| 单机模式 | 不挂组件或只挂不 `Connect()` 即可纯本地游玩——零影响 |
| 本地角色异步生成 | 进世界后组件每帧重试装配，10s 内角色未就绪则**降级为观察者模式**（仅渲染远程实体，告警日志） |
| 无本地角色（纯观察者） | 场景无 Sys3CEntry 时直接观察者模式——能看远程玩家移动 |
| 上报速度源 | 读 `CharacterData.Velocity` 水平幅度（真实 m/s，来自 `_controller.velocity`）。**勿改用 `MovementSpeed`（0-1 动画参数）**——超速钳制链路依赖真实值 |
| 位置起点 | 本地角色留在场景初始位即可：服务器接受距上次权威位 ≤50m 的首报，不会误伤瞬移判定 |
| 断线/重连 | 断开触发 `OnDisconnected`；当前协议 = 重新 `Connect()`（全新 Login 全流程，P1 D4）。`Disconnect()` 可主动断开并复位 |
| 远程渲染占位 | 胶囊体 + 名字，无外观——真实角色模型渲染留后续；视野 = 服务器 AOI 80m（进出驱动生成/销毁：进视野快照到达即生成；出视野停流 1.2s 后客户端缺席超时销毁） |
| 纠偏手感 | 服务器判瞬移/超速时 ack 带权威位，客户端 8/s blend 收敛（不瞬移）；无纠偏时本地手感与单机一致 |

## 事件（可选接线）

```csharp
netHost.OnEnterWorld   += () => { /* 进世界：例如切 HUD/隐藏 Loading */ };
netHost.OnDisconnected += reason => { /* 断线提示 */ };
```

## 已知边界（P3）

- 登录 UI/主城流程不存在——`Connect()` 的调用点由未来启动流提供
- 远程实体断线残留：服务器静默检测（15s）前实体停流——客户端 1.2s 缺席超时先销毁（比服务器 15s 注销更快）
- 真实角色（重力/物理/受击）下纠偏收敛需在此宿主上实机验证——P2 纯模拟测试覆盖不到
