# 属性剑气 Plan 07：网络硬化与残余审计实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 关闭完整首发后仍登记在 Plan 07/backlog 的网络验收缺口，并审计 Plan 04 残余 M 项；不新增战斗机制。

**架构：** 客户端侧用可注入时钟/延迟的适配器与桥层夹具覆盖消费时序；宿主级断线重连用真实 `ClientNetworkHost`/新桥/新 playerId；Plan 04 残余按"可自动修复/需设计决策"分流。

**技术栈：** Unity 2022.3.25f1、NUnit EditMode/PlayMode、KCP/WebSocket 适配器包装、真实 `Server3C:8000`。

---

## 范围与边界

**本计划覆盖：**
- 状态结束时钟偏移注入（两台客户端不同时钟/渲染延迟）。
- KCP/WebSocket 传输层延迟与乱序注入（不是桥层消息重排）。
- 真实连接代次/旧位移预测的宿主级隔离。
- Plan 04 残余 M 项审计与可机械修复项。

**明确不在本计划，保留 Plan 08/backlog：**
- 三端同一次末跳致死的死亡时刻（需要服务器 HP 注入/测试钩子；当前 720 HP 数值下不可确定性构造）。
- 生产 `NpcBody` 推挤（需要产品决策：给 NpcBody 暴露非 `IDamageable` 标记或按层 10 处理）。
- `NpcAction` 直接驱动攻击变体/时间轴（需要单独设计）。
- 离线四元素、Addressables、远端武器模型已在完整首发 `66812544` 完成，不在本计划重复。

## 全局约束

- 只在 worktree `E:/CodeForJob/Cool/.claude/worktrees/elemental-sword-qi-foundation` 工作；不 `cd` 主仓库、不 push。
- 服务端测试：`bash tools/test-server-inprocess.sh`（testhost `Win32Exception(5)` 时）。
- Unity 测试：batchmode，不加 `-quit`；XML 落盘后结束进程；跑完恢复 `Assets/Packages/**`、`ProjectSettings/QualitySettings.asset`、`ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json`、`Assets/PreRes/Font/ZhunYuan SDF.asset`；绝不提交 `TestResults/`。不要动主仓库正在运行的 8000 端口服务（用户手动验收用）。
- 所有断言必须可证伪：精确序号、版本、tick、事件次数；禁止"最终一致"。
- 生产代码改动只允许由失败断言驱动，并在报告中点名触发断言。
- 不回退 `66812544` 的完整首发改动。

---

### 任务 1：状态结束时钟偏移注入

**文件：**
- 新建：`Assets/Scripts/Tests/PlayMode/StatusEndClockOffsetTests.cs`
- 可能修改：`Assets/Scripts/Hotfix/GameSystems/VFX/AuthoritativeStatusVfx.cs`、
  `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`

- [ ] **步骤 1：写失败用例**

构造两个客户端视图（可复用 `RemoteEntityRegistry` + `AuthoritativeStatusVfx` + `ClientNetworkHost` 的订阅路径），给它们注入不同的服务器时钟偏移/渲染延迟（例如 +0ms 与 +400ms）。投递同一条 `CombatStateUpdate(318)`：

1. 状态施加：两端都只在收到 318 后创建特效，本地 `EndTick` 早到不创建。
2. 状态移除：权威 318 的 `EndTick` 已过但消息晚到 400ms；两端特效都必须保持到该 318 到达，移除事件只在到达时各触发一次。
3. 把 318 的 `ServerTimestamp` 与本地时钟偏移打乱，断言移除时刻差 ≤1 个渲染帧，而不是各自按 `EndTick` 差 400ms。
4. 重复同版本 318 不重复施加/移除。

失败判据：当前没有时钟偏移注入用例；若实现按本地 `EndTick` 移除，第 2/3 条会红。

- [ ] **步骤 2：运行确认失败**

```bash
# Unity MCP 指向主仓库，使用 batchmode
UNITY_MCP_DISABLE=1 timeout 300 "/e/Unity/Editor/2022.3.25f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "<worktree>" \
  -runTests -testPlatform PlayMode -testFilter StatusEndClockOffsetTests \
  -testResults "<worktree>/TestResults/plan07/task1.xml" \
  -logFile "<worktree>/TestResults/plan07/task1.log"
```

- [ ] **步骤 3：最小实现/修复**

若 `AuthoritativeStatusVfx` 只按本地 `EndTick` 移除，改为以 318 状态列表 diff 为唯一移除源；本地 `EndTick` 只用于倒计时显示。不得新增协议字段。

- [ ] **步骤 4：运行通过**

同一命令，预期 1/1 或按实际用例数全绿；同时跑 `StatusVFXSystemTests` 与 `AuthoritativeStatusVfxTests` 回归。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/StatusEndClockOffsetTests.cs \
        Assets/Scripts/Hotfix/GameSystems/VFX/AuthoritativeStatusVfx.cs
git commit -m "test(vfx): remove statuses only on authoritative update"
```

---

### 任务 2：KCP/WebSocket 传输层延迟注入

**文件：**
- 新建：`Assets/Scripts/Tests/PlayMode/TransportDelayInjectionTests.cs`
- 可能修改：`Assets/Scripts/Tests/PlayMode/ElementalTwoClientTests.cs`（复用登录/装备/施法夹具）

- [ ] **步骤 1：写延迟适配器**

实现测试本地 `DelayedNetworkClient : INetworkClient`，包装真实 `KcpNetClientAdapter` 与 `WebSocketClientAdapter`，把发送/接收消息放入可按 tick/时间释放的队列；支持延迟、重复、乱序三种注入。不得改生产传输层。

- [ ] **步骤 2：写两组传输用例**

对 KCP 与 WebSocket 各跑一遍：

1. 换装确认延迟于施法确认：施法 `EquipmentRevision` 与装备版本门不回退。
2. 发射晚于格挡结果：晚到 `ProjectileLaunchBroadcast` 不重建已停止视觉。
3. 旧伤害晚于复活：旧 `LifeId` 的 `DamageResult` 不扣血/不飘字。
4. 较新血量早于状态消息：旧 `CombatStateUpdate` 不回退新血量/状态。
5. 延迟但同版本消息只消费一次。

断言每个场景的桥层/视图事件次数与权威值，不用 sleep 排序。

- [ ] **步骤 3：运行**

真实 Server3C 需由测试自身在 8000 之外端口启动（不要杀用户主仓库服务）；若测试夹具只能 8000，先与用户确认再停主服务。预期 KCP/WS 两组全绿。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/TransportDelayInjectionTests.cs
git commit -m "test(net): inject kcp and websocket transport delay"
```

---

### 任务 3：真实连接代次/旧位移预测宿主级隔离

**文件：**
- 新建：`Assets/Scripts/Tests/Editor/SyncLayer/SessionIsolationTests.cs`
- 可能修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`、
  `MovementReporter.cs`、`PredictionReconciler.cs`

- [ ] **步骤 1：写宿主级用例**

用真实 `ClientNetworkHost` 反射装配/拆装 `_combatBridge`、`_adapter`、`MovementReporter`：

1. 旧会话的 `DamageResult`/`CastBroadcast`/`CombatStateUpdate` 在新会话不得触发桥事件或改新视图。
2. 旧会话的预测帧在 `DetachCombatBridge`/重连后清空；新会话第一帧不对旧预测做对账。
3. 新会话使用新 `playerId`；旧 `LocalPlayerId` 的本地伤害回调不触发。
4. 旧模型回调/旧位移预测在重连后不得复活旧表现。

断言事件计数、预测队列长度、`LastCorrectionTarget`、视图状态；不伪造新会话隔离。

- [ ] **步骤 2：运行**

```bash
# 若夹具不依赖真实服务器，可直接 EditMode；否则用 PlayMode + 测试自建服务器端口
```

预期新增用例全绿；若宿主缺少清理路径，修最小清理并点名断言。

- [ ] **步骤 3：Commit**

```bash
git add Assets/Scripts/Tests/Editor/SyncLayer/SessionIsolationTests.cs
git commit -m "test(sync): isolate old session packets and predictions"
```

---

### 任务 4：Plan 04 残余 M 项审计与文档收口

**文件：**
- 修改：`docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md`
- 按审计结果修改：`Server/Status/StatusTickService.cs`、`Server/Handler/CastHandler.cs`、
  `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/StatusRuntime.cs` 等

- [ ] **步骤 1：逐项审计**

对 M2/M3/M4/M6/M7/M8/M10/M11/M16/M17/M18/M19 逐项给出：已由完整首发解决 / 可机械修复 / 仍需设计决策。至少验证：

- M16 离线 provider：完整首发 `OfflineMonsterStatuses`/`OfflineWeaponCast` 已解决，指测试。
- M17 ThunderDuration：完整首发 PvP/PvE 雷时长已分开，指测试与配置。
- M3 `StatusTickService.Tick` 列表缓冲复用：若仍每次分配，做最小复用并加分配/行为测试。
- M7 未用参数、M8 死 API：只清理本次触碰路径，不做无关重构。

- [ ] **步骤 2：修复可机械项**

每项一个可证伪测试；不得借机重构未要求代码。

- [ ] **步骤 3：更新文档**

路线图把已完成项标注证据，仍需设计的项保留 Plan 08/backlog；不得把未做项写成已打通。

- [ ] **步骤 4：全量验证**

```bash
bash tools/test-server-inprocess.sh
# Unity 目标类 + 本计划新增 PlayMode 类
```

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md <修复文件>
git commit -m "chore(status): audit plan 04 residual findings"
```

---

## 完成定义

- 任务 1–3 的新增验收用例全部存在并通过；失败必须由生产缺陷修复，不得放宽断言。
- 任务 4 的 M 项逐项有处置与证据；未做项保留 Plan 08/backlog。
- 没有把三端末跳致死、生产 NpcBody 推挤、NpcAction 攻击变体写成已完成。
- 服务端全量、Unity 目标类通过（环境失败单独列出）；batchmode 副作用未提交。
