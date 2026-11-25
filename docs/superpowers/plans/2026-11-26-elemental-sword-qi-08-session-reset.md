# 属性剑气 Plan 08：会话复位与断开清理实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法来跟踪进度。

**目标：** 关闭完整首发/Plan 07 收口后仍登记的两个会话复位缺口：echo 最后应用状态跨服务器重启残留、断开时状态 VFX 实体表滞留；并做 backlog 收口。

**架构：** 两个缺口都在 `ClientNetworkHost` 的会话生命周期：`Connect`/`Disconnect` 复位 echo 的单调门；`Disconnect` 清空 `AuthoritativeStatusVfx` 表并发出 Removed，让 `StatusVFXSystem` 清染色。测试走真实宿主/registry/VFX 路径。

**技术栈：** Unity 2022.3.25f1、NUnit EditMode、真实 `ClientNetworkHost`/`RemoteEntityRegistry`/`AuthoritativeStatusVfx`/`StatusVFXSystem`。

---

## 范围与边界

**本计划覆盖：**
- `_lastAppliedEchoCastId/_lastAppliedEchoStartTick/_endedEchoCastId` 在 `Connect`/`Disconnect` 复位；服务器重启后低 `StartTick` 的 echo 回包不得被永久判旧。
- `AuthoritativeStatusVfx.Clear()` + 断开时清表/发 Removed（M19 断开侧）；重连新视图不得承接旧染色。
- Plan 08/backlog 收口文档。

**明确不在本计划，保留 Plan 09/backlog：**
- 三端同一次末跳致死的死亡时刻（需服务器 HP 注入/测试钩子）。
- 真实 KCP/WebSocket 传输-socket 延迟注入（Plan 07 只覆盖消费层装饰器）。
- 生产 `NpcBody` 推挤（产品决策）。
- `NpcAction` 驱动攻击变体/时间轴（设计决策）。
- M4/M6/M8/M11/M18（设计/产品决策）。

## 全局约束

- 只在 worktree `E:/CodeForJob/Cool/.claude/worktrees/elemental-plan08` 工作；不 `cd` 主仓库、不 push。
- Unity 测试：batchmode，不加 `-quit`；XML 落盘后结束进程；跑完恢复 `Assets/Packages/**`、`ProjectSettings/QualitySettings.asset`、`ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json`、`Assets/PreRes/Font/ZhunYuan SDF.asset`；绝不提交 `TestResults/`。
- 不要触碰主仓库正在运行的 8000 端口服务（用户手动验收用）；本计划不需要真实服务器。
- 所有断言必须可证伪：精确 CastId/StartTick/事件计数；禁止"最终一致"。
- 生产代码改动只允许由失败断言驱动，并在报告中点名触发断言。
- 不回退完整首发 `66812544` 与 Plan 07 `76a8c833`。

---

### 任务 1：Echo 最后应用状态跨会话复位

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 新建：`Assets/Scripts/Tests/Editor/SyncLayer/EchoSessionResetTests.cs`

- [x] **步骤 1：写失败用例**

复用 `SessionIsolationTests` 的宿主反射装配（`_manager/_adapter/_localEntry` + `Awake/HandleEnterWorld/AttachConnectionCombat`）。场景：

1. 宿主应用旧会话 echo 状态：`CastId=100`、`StartTick=500`（通过真实 `_combatBridge.HandleSwordQiEchoState` 路径或宿主私有处理器）。
2. `Disconnect()` 后 `Connect()`（新连接代次）；`_lastAppliedEchoCastId/_lastAppliedEchoStartTick/_endedEchoCastId` 必须归零。
3. 投递服务器重启后的新状态：`CastId=1`、`StartTick=1`；必须被接受（`IsNewerEchoState` 为 true），不能因旧 StartTick=500 永久丢弃。
4. 旧会话的 `Ended` 状态（`_endedEchoCastId=100`）不得压制新会话同 CastId 的 `Started`；复位后 `_endedEchoCastId=0`。
5. 正常同会话内仍按 `(StartTick, CastId)` 单调判新，不能因复位破坏去重。

失败判据：当前 `ClearEchoRequest` 只清请求字段，不重置三个应用状态；第 3/4 条应红。

- [x] **步骤 2：运行确认失败**

```bash
UNITY_MCP_DISABLE=1 timeout 300 "/e/Unity/Editor/2022.3.25f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "E:/CodeForJob/Cool/.claude/worktrees/elemental-plan08" \
  -runTests -testPlatform EditMode -testFilter EchoSessionResetTests \
  -testResults "<worktree>/TestResults/plan08/task1.xml" \
  -logFile "<worktree>/TestResults/plan08/task1.log"
```

- [x] **步骤 3：最小修复**

在 `Connect`/`Disconnect` 的会话复位路径（`ClearEchoRequest` 或紧邻调用点）把 `_lastAppliedEchoCastId=0`、`_lastAppliedEchoStartTick=0`、`_endedEchoCastId=0`。不得改变同会话 `IsNewerEchoState` 的 `(StartTick, CastId)` 语义。

- [x] **步骤 4：运行通过**

同一命令全绿；回归 `SessionIsolationTests`、`SwordQiEchoPresenterTests`、`EchoSmokeTests`（若不需要服务器）。

- [x] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/Editor/SyncLayer/EchoSessionResetTests.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs
git commit -m "fix(sync): reset applied echo state on session change"
```

---

### 任务 2：断开清空状态 VFX 实体表（M19）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/AuthoritativeStatusVfx.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 新建：`Assets/Scripts/Tests/Editor/VFX/StatusVfxDisconnectCleanupTests.cs`

- [x] **步骤 1：写失败用例**

1. 用真实 `AuthoritativeStatusVfx` + `StatusVFXSystem`（或直接 EventBus 计数）给实体应用冰/火两组状态。
2. 调用宿主 `Disconnect()`（或直接 `_statusVfx.Clear()` 的生产路径）。
3. 断言：每组各发一次 `StatusEffectRemovedEvent`、内部表为空；`StatusVFXSystem._tints` 不残留该实体。
4. 重连新会话同 id 视图：不得承接旧状态（无 Applied 事件、无旧染色）；后续 318 新状态正常挂载。

失败判据：当前 `AuthoritativeStatusVfx` 无 `Clear()`，宿主断开只置空 `_remoteCastPresenter`，`_statusVfx` 跨会话存活；第 2/3 条应红。

- [x] **步骤 2：最小实现**

给 `AuthoritativeStatusVfx` 加 `Clear()`：遍历实体表，对每组 `EmitRemoved`，清表。`ClientNetworkHost.Disconnect()`/`ResetAttachment` 的断开路径调用 `_statusVfx?.Clear()`（仅断开，不用于角色重建；换根仍走 `Apply` 的 target diff）。不得改变正常 318 diff 语义。

- [x] **步骤 3：运行**

```bash
UNITY_MCP_DISABLE=1 timeout 300 "/e/Unity/Editor/2022.3.25f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "E:/CodeForJob/Cool/.claude/worktrees/elemental-plan08" \
  -runTests -testPlatform EditMode \
  -testFilter "StatusVfxDisconnectCleanupTests;AuthoritativeStatusVfxTests;StatusVFXSystemTests" \
  -testResults "<worktree>/TestResults/plan08/task2.xml" \
  -logFile "<worktree>/TestResults/plan08/task2.log"
```

预期全绿（新增用例 + 既有 12 条回归）。

- [x] **步骤 4：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/AuthoritativeStatusVfx.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
        Assets/Scripts/Tests/Editor/VFX/StatusVfxDisconnectCleanupTests.cs
git commit -m "fix(vfx): clear authoritative statuses on disconnect"
```

---

### 任务 3：Plan 08/backlog 文档收口

**文件：**
- 修改：`docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/README.md`

- [x] **步骤 1：更新证据**

路线图标记 Plan 08 任务 1/2 完成与测试证据；把 M19 断开侧从 backlog 标为已解决；仍将三端末跳致死、真实 KCP/WS 传输-socket 延迟、生产 NpcBody 推挤、NpcAction 攻击变体、M4/M6/M8/M11/M18 保留 Plan 09/backlog。

- [x] **步骤 2：模块 README**

SyncLayer README 补 echo 会话复位护栏；VFX README 补 `AuthoritativeStatusVfx.Clear()` 的断开语义（与 318 diff、换根重放的区别）。

- [x] **步骤 3：全量验证**

```bash
# 服务端不受影响，但跑一次确认 707/707
bash tools/test-server-inprocess.sh
# Unity 目标类
# EchoSessionResetTests、StatusVfxDisconnectCleanupTests、SessionIsolationTests、NetworkConsumptionInjectionTests
```

- [x] **步骤 4：Commit**

```bash
git add docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
        Assets/Scripts/Hotfix/GameSystems/VFX/README.md
git commit -m "docs(plan08): close session reset and disconnect cleanup"
```

---

## 完成定义

- 两个会话复位缺口都有可证伪测试并通过；生产改动最小且由失败断言驱动。
- 同会话 echo 单调语义、正常 318 diff、换根重放均不回归。
- 未把 Plan 09/backlog（三端末跳致死、真实传输-socket 延迟、生产 NpcBody 推挤、NpcAction 变体、M4/M6/M8/M11/M18）写成已完成。
- 服务端全量、Unity 目标类通过；batchmode 副作用未提交。
