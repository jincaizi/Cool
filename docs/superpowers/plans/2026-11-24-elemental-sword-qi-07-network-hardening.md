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

### 任务 1：状态结束消息驱动契约（无本地时钟移除）

**范围裁定（2026-11-24）：** 生产实现没有本地 `EndTick` 移除路径：`AuthoritativeStatusVfx`
只按 318 列表 diff 发事件，`StatusVFXSystem.Update` 只处理目标销毁与染色，不按时间移除
（火焰脉冲会读 `deltaTime`，但不据此结束状态）。
因此“注入不同客户端时钟偏移”没有可影响的被测对象；真实风险是**未来加入本地计时移除**导致
特效早于权威消息消失。本任务验收改为：证明施加/移除只由 318 驱动，迟到 318 保持到到达，
并把消费端 `StatusVFXSystem` 纳入回路，推进时间也不得自行移除。时钟偏移本身不适用，不再伪造。

**文件：**
- 新建：`Assets/Scripts/Tests/PlayMode/StatusRemovalOnlyOnAuthoritativeProjectionTests.cs`
- 可能修改：`Assets/Scripts/Hotfix/GameSystems/VFX/AuthoritativeStatusVfx.cs`、
  `Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs`、
  `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`

- [x] **步骤 1：写失败用例**

构造两个客户端视图（复用 `RemoteEntityRegistry` + `AuthoritativeStatusVfx` + `StatusVFXSystem`
的生产订阅路径），投递 `CombatStateUpdate(318)`：

1. 施加：只在收到 318 后创建特效/染色；没有 318 时推进时间不创建。
2. 移除：移除 318 晚到（例如 400ms）；`StatusVFXSystem` 在推进时间/Update 后仍保持染色/特效，
   直到 318 到达才各移除一次。
3. 两端移除时刻差只由消息到达差决定（≤排程差），不存在“本地 EndTick 各自消失”的分叉。
4. 重复同版本 318 不重复施加/移除。
5. 会话结束/AOI 退出等非 318 来源只允许走 `Remove(entityId)` 的全量清除，不得伪造单组移除。

失败判据：若 `StatusVFXSystem` 或 `AuthoritativeStatusVfx` 按本地时间/EndTick 移除，第 1/2 条会红。
删除所有“注入时钟公式”与恒真反证断言——它们不属于被测路径。

- [x] **步骤 2：运行确认失败**

```bash
UNITY_MCP_DISABLE=1 timeout 300 "/e/Unity/Editor/2022.3.25f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "<worktree>" \
  -runTests -testPlatform PlayMode -testFilter StatusRemovalOnlyOnAuthoritativeProjectionTests \
  -testResults "<worktree>/TestResults/plan07/task1.xml" \
  -logFile "<worktree>/TestResults/plan07/task1.log"
```

- [x] **步骤 3：最小实现/修复**

若 `StatusVFXSystem` 有时间移除，删掉该路径；移除唯一来源是 318 diff（自然结束）或
`Remove(entityId)`（AOI/销毁全清）。不得新增协议字段或测试专用生产 API。

- [x] **步骤 4：运行通过**

同一命令全绿；同时跑 `StatusVFXSystemTests` 与 `AuthoritativeStatusVfxTests` 回归。

- [x] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/StatusRemovalOnlyOnAuthoritativeProjectionTests.cs
git commit -m "test(vfx): keep statuses until authoritative projection"
```

---

### 任务 2：传输层延迟注入（装饰器夹具）

**范围裁定（2026-11-24）：** 8000 端口正被用户主仓库的验收服务占用，测试不得停它，也不把
真实服务器前置塞进本任务。KCP 与 WebSocket 的**正常流程**已由 `ElementalTwoClientTests`
2/2 覆盖；本任务补的是传输消费层的延迟/重复/乱序，使用测试本地 `INetworkClient` 装饰器
确定性地排程，不依赖真实 socket。

**文件：**
- 新建：`Assets/Scripts/Tests/Editor/SyncLayer/TransportDelayInjectionTests.cs`
- 可能修改：`Assets/Scripts/Tests/PlayMode/ElementalTwoClientTests.cs`（仅注释指向本夹具）

- [x] **步骤 1：写延迟装饰器**

实现测试本地 `DelayedNetworkClient : INetworkClient`，包装任意 `INetworkClient`（测试用
fake client；不包装真实 socket），把发送/收到的消息放入可按测试时间释放的队列；支持延迟、
重复、乱序三种注入。不得改生产传输层。

- [x] **步骤 2：写五个用例**

1. 换装确认延迟于施法确认：施法 `EquipmentRevision` 与装备版本门不回退。
2. 发射晚于格挡结果：晚到 `ProjectileLaunchBroadcast` 不重建已停止视觉。
3. 旧伤害晚于复活：旧 `LifeId` 的 `DamageResult` 不扣血/不飘字。
4. 较新血量早于状态消息：旧 `CombatStateUpdate` 不回退新血量/状态。
5. 延迟但同版本消息只消费一次。

断言每个场景的桥层/视图事件次数与权威值，不用 sleep 排序；KCP/WS 正常流程引用
`ElementalTwoClientTests` 既有证据，不重复造真实 socket。

- [x] **步骤 3：运行**

```bash
UNITY_MCP_DISABLE=1 timeout 300 "/e/Unity/Editor/2022.3.25f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "<worktree>" \
  -runTests -testPlatform EditMode -testFilter TransportDelayInjectionTests \
  -testResults "<worktree>/TestResults/plan07/task2.xml" \
  -logFile "<worktree>/TestResults/plan07/task2.log"
```

- [x] **步骤 4：Commit**

```bash
git add Assets/Scripts/Tests/Editor/SyncLayer/TransportDelayInjectionTests.cs
git commit -m "test(net): inject transport delay at the consumption layer"
```

---

### 任务 3：真实连接代次/旧位移预测宿主级隔离

**文件：**
- 新建：`Assets/Scripts/Tests/Editor/SyncLayer/SessionIsolationTests.cs`
- 可能修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`、
  `MovementReporter.cs`、`PredictionReconciler.cs`

- [x] **步骤 1：写宿主级用例**

用真实 `ClientNetworkHost` 反射装配/拆装 `_combatBridge`、`_adapter`、`MovementReporter`：

1. 旧会话的 `DamageResult`/`CastBroadcast`/`CombatStateUpdate` 在新会话不得触发桥事件或改新视图。
2. 旧会话的 reporter/reconciler 实例在重连后被丢弃，新会话新建空实例；旧 `seq` 的 ack
   不得对新预测做对账（生产不调用 `PredictionReconciler.Clear()`，隔离靠丢弃旧实例 +
   新实例空队列，测试按此断言）。
3. 新会话使用新 `playerId`；旧 `LocalPlayerId` 的本地伤害回调不触发。
4. 旧模型回调/旧位移预测在重连后不得复活旧表现。

断言事件计数、预测队列长度、`LastCorrectionTarget`、视图状态；不伪造新会话隔离。

- [x] **步骤 2：运行**

```bash
# 若夹具不依赖真实服务器，可直接 EditMode；否则用 PlayMode + 测试自建服务器端口
```

预期新增用例全绿；若宿主缺少清理路径，修最小清理并点名断言。

- [x] **步骤 3：Commit**

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

- [x] **步骤 1：逐项审计**

对 M2/M3/M4/M6/M7/M8/M10/M11/M16/M17/M18/M19 逐项给出：已由完整首发解决 / 可机械修复 / 仍需设计决策。至少验证：

- M16 离线 provider：完整首发 `OfflineMonsterStatuses`/`OfflineWeaponCast` 已解决，指测试。
- M17 ThunderDuration：完整首发 PvP/PvE 雷时长已分开，指测试与配置。
- M3 `StatusTickService.Tick` 列表缓冲复用：若仍每次分配，做最小复用并加分配/行为测试。
- M7 未用参数、M8 死 API：只清理本次触碰路径，不做无关重构。

- [x] **步骤 2：修复可机械项**

每项一个可证伪测试；不得借机重构未要求代码。

- [x] **步骤 3：更新文档**

路线图把已完成项标注证据，仍需设计的项保留 Plan 08/backlog；不得把未做项写成已打通。

- [x] **步骤 4：全量验证**

```bash
bash tools/test-server-inprocess.sh
# Unity 目标类 + 本计划新增 PlayMode 类
```

- [x] **步骤 5：Commit**

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
