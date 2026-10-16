# 游戏内网络联调指南（单机 / 联网模式）

> **它是什么**：把 P1-P5 网络同步接进真实游戏后的**实机联调步骤**——起服务器、选模式、进世界、看什么算成功。前置背景见 `p2-client-network-host.md`（宿主组件）与 `p5-monster-server.md`（服务器怪物）。

## 一、起服务器

```bash
# 编译（一次）
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj

# 联网 + 服务器怪物（推荐联调用）
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000 --monsters 4

# 纯联网（无服务器怪物，怪物走本地 MonsterSpawner）
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000

# 压测模式（100 个游走 bot——用于验证远程玩家渲染）
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000 --bots 100
```

日志出现 `Server3C is running on port 8000` 即就绪。停止：Ctrl+C，或按端口找 PID 后 `taskkill //F //PID <pid>`。

## 二、进游戏选模式

1. Unity 打开 `Assets/Scenes/CharacterSelect.unity` 或直接打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 后 Play（两条路径都会弹出选人面板）
2. 面板底部新增**单机 / 联网**两个按钮（男女卡片下方）——默认记住上次选择（PlayerPrefs `net.mode`，首次默认**单机**）
3. 选"联网" → 面板下方展开**服务器设置区**：填写**服务器地址 / 端口 / 昵称**
   （默认 `127.0.0.1` / `8000` / `Player`；非法输入回落默认值——端口非 1-65535 或地址留空时）
4. 点"确认"进世界

连接参数持久化在 PlayerPrefs（`net.host` / `net.port` / `net.username`），下次打开自动回填。
**注意**：DemoDay 的 `Net` 对象 Inspector 上的 `_serverHost/_serverPort/_username` **不再是连接来源**
（D-P6-17）——`Connect()` 每次从 PlayerPrefs 解析，Inspector 值仅作默认值参考。

### 两种模式的差异

| | 单机模式 | 联网模式 |
|---|---|---|
| 连接服务器 | 完全不触网 | 进世界后自动 `Connect()`（地址/端口/昵称来自选人面板填写并持久化，默认 `127.0.0.1:8000` / `Player`） |
| 本地怪物 | `MonsterSpawner`（SlimeSpawner / TurtleShellSpawner）照常生成 | **全部禁用**——怪物由服务器驱动（球体占位渲染） |
| 移动 | 纯本地 | 服务端纠偏（`NetMovementPolicy` 平滑拉回，超速/瞬移被拒） |
| 战斗 | 全本地权威 | 表内技能（10001/10002/20001/20002）发 `CastRequest` → 服务器裁决 → `DamageResult` 驱动伤害/浮字 |

## 三、进世界后看什么算成功

屏幕**左下角调试浮层**（`Net` 对象 Inspector 的 `Show Debug Overlay` 可关——
**它只关这一条浮层**；屏幕中部的错误/断线提示不在此开关内，见 §四）：

```
[Net] 模式=联网  状态=InWorld  玩家id=2  远程实体=4
```

- `状态`：`未连接` → `Connecting` → `LoggingIn` → **`InWorld`**（成功）
- `远程实体`：服务器 AOI 视野内的实体数（视距 80m，按 XZ 距离判）。出生点 2026-10-17 第三次搬迁到 **(-15,0.3,-30)**（历史：10-13 湖东岸 (10,0,-40)），布局同批收紧后六槽位**全在出生点 80m 内**（最远 [5] SpiderBoss 64.0m）⇒ `--monsters 4` 稳定 **4**、`--monsters 6` 稳定 **6**，不再有 80m 边缘进出波动。（旧读数留档：10-13~10-17 期间 `--monsters 4` 曾看到 **3↔4、6 曾看到 5↔6** 波动——那是旧槽位 [3] Wolf (0,0,40) 距旧出生点 80.6m、恰在视距边缘外 0.6m，那些坐标已不存在；别再按"波动是正常"读这条。）
- 控制台应出现（关键日志）：
  - `[NetHost] 联网模式——连接 127.0.0.1:8000`
  - `[NetHost] 本地 MonsterSpawner 已禁用：SlimeSpawner` / `TurtleShellSpawner`
  - `[NetHost] 本地角色已接入移动上报（repoter + NetMovementPolicy）`
  - 走近怪物后：`[NetHost] cast broadcast: caster=2000001 skill=50002`（怪物攻击的施法广播）

### 实机体验要点

- **开局没有任何怪会仇恨**：出生点 **(-15,0,−30)**（2026-10-17 第三次搬迁；历史：10-13 湖东岸 (10,0,−40)、更早 (10,0,−15.52)）到六只怪的「感知半径 + 巡逻半径」**全有余量**（最近的是 (0,0,-35) 的 **[2] Slime**：15.8m > 8+5，余量 +2.8m）——站着不动 3 分钟无伤。**但朝 (0,0,-35) 走约 3m** 就会进它的感知圈被追（[0] Wolf 现在在 (-30,0,0)、距出生点 33.5m，已不是最近的那只）
- 远程玩家（真人 + bot）与怪物都是**真模型**：走各自的外观/模型目录（DemoDay 里玩家 = `_playerAvatarPrefab`，怪物配了 templateId 3/4 = DogKnight / SpiderBoss）；目录未命中/未接线才退化为胶囊/球体占位。**60m 外或超每帧预算的远档是代理**（未配 `Proxy` 预制体时是胶囊）——左下角浮层第二行的 `Full= / Proxy= / Q=` 就是这件事的读数
- 单客户端看不到其他玩家——用 `--bots 100` 服务器或第二客户端（见 `p1-dual-client-dev.md`）验证远程玩家链

## 四、已知边界（实机评审时别误判为新 bug）

- **断线会提示并自动返回选人界面，但不自动重连**：服务器重启/断线（或连接失败/登录被拒）时屏幕中部提示 3 秒后自动回选人界面，可重新选模式再连（重连 = 全新登录流程；自动重连是 backlog）。
  该提示**不受 `Show Debug Overlay` 开关影响**（`OnGUI` 里它排在 `_showDebugOverlay` 守卫之前）
  ——关掉调试浮层只会关掉左下角那条状态条，屏幕出口仍在
- **未连上时零日志？** 宿主已接 `OnError` 日志（`[NetHost] error:`），连接失败/登录被拒必有输出
- **玩家重建自动重装配**：DemoDay 直跑时确认选人会触发 `PlayerSpawner` 销毁重建本地角色——宿主订阅 `PlayerSpawnedEvent` 重装配（移动上报/纠偏/战斗桥全部换绑新角色）

## 五、曾修复的关键缺陷（2026-09-13，供排查参考）

集成实机联调时发现并修复：**客户端 KCP 接收路径吞吐被压到 ~7.8 包/秒**（服务器发 15/秒），快照流滞后无界增长（实测 57s → 135s），AOI 远程实体因 1.2s 缺席超时被全部清空——表现为"进世界 3 秒后怪物视图消失"。

根因与修复（`Assets/Scripts/AOT/KcpNet/`）：
1. `KcpClientTransport.ReceiveKcpAsync` 把 `_kcp.TryRecv` 放在 `Socket.Available > 0` 分支内——无新包时 KCP 队列中已就绪的消息不被取走，**接收窗不释放，对端发送受阻**。改为：先把所有可用 UDP 包喂入 KCP，再**无条件** `TryRecv`。
2. `KcpTransportBase.ProcessReceiveAsync` 每次调度只读一个包（旧实现）→ 改为单次调度内**循环排空**（上限 64 包防饥饿发送/更新）。
3. `KcpTransportBase.NetworkDriverLoop` 专用线程模式下 `Task.Delay(1)` 续体需回线程池调度（实测延迟 ~190ms/次）→ 改为同步 `Thread.Sleep(1)`。

修复后实测：客户端接收 **15.4 条/秒**（与服务器发送 15/秒匹配）、快照滞后 **20-39ms**（原 135 秒）、远程实体视图稳定持久。

服务器侧同时补了可观测性：`KcpServerSession` 发送队列满丢包计数 + 警告日志（原先静默丢弃）、`[Aoi] 5s stat` 增列真实发送的 `snapshotMsgs/s`（原先只有实体数，无法区分"组装"与"真发"）。
