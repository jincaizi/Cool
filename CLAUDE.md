# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Unity MMO Client Project

## Project Overview
Unity 2022 LTS MMO client with server integration planned. Code assisted by Claude Code. The release target is the mobile platform.

## Tech Stack
- Unity: 2022.3.25f1 (LTS)
- Networking: KCP + Protobuf
- Resource Management: Addressable
- Hotfix: 无 —— HybridCLR 未安装（`Packages/manifest.json` 无该包，`Assets/HybridCLRData` 不存在）；
  `Hotfix.*` 只是普通 asmdef 程序集，**没有代码热重载能力**
- UI: UGUI-based custom framework

## Common Commands
- Unity Editor: `start Unity.exe -projectPath E:\CodeForJob\Cool`；构建用 Ctrl+Shift+B
- Git: `git add <files>` 后 `git commit -m "<message>"`；**未启用 LFS**，二进制裸存
- 服务端测试：`bash tools/test-server.sh`（minimal logger；可追加 `--filter "FullyQualifiedName~X"`）
- Unity 测试（Unity MCP）：`bash tools/unity-tests.sh EditMode [Class]` / `bash tools/unity-tests.sh PlayMode Class`
  （封装了 `--input` JSON 与 300s 超时；原始命令见脚本）

## Architecture at a glance
**AOT**（`Assets/Scripts/AOT/`）：`Core/Resource/`、`KcpNet/`、`DataDefinition/`、`Protocol/`。
**Hotfix**（`Assets/Scripts/Hotfix/GameSystems/`）：`Sys3C/`、`Skills/`、`Bag/`、`UI/`、`Monster/`、`Combat/`、`Nameplate/`、`VFX/`。
**Examples**：`Assets/Scripts/Examples/`。

### 3C（必须知道）
- 三层 Base / Attack / Hit；**只有 BaseFSM、HitFSM 两个 FSM 类**，攻击层由 `StateCoordinator` + `SkillCoordinator` 驱动
  （`FSM/AttackState.cs` 只是 Animator 的 2 值枚举；**没有 AttackFSM**）。
- 关键文件：`Sys3C/FSM/FSMManager.cs`、`Sys3C/Core/StateCoordinator.cs`、`Sys3C/Character/CharacterController.cs`、
  `Sys3C/Animation/AnimationDriver.cs`、`Sys3C/Core/Events/EventBus.cs`。
- Hit 层只有 `Hit` / `DefendHit` / `Dizzy` 可达；`Knockback`/`Launched`/`Death`/`Down`/`GetUp` 无动画表现（backlog）。

### Movement / Collision / Avoidance（改前必读的护栏）
- **碰撞两半分开放**：怪物/bot 彼此避让在**服务端** `Server/Movement/OrcaSolver.cs` + `AvoidanceSystem.cs`；
  玩家 vs 怪物阻挡在**客户端** `SyncLayer/NpcBodyCollider.cs`（挂在 Npc 视图上，预制体不挂碰撞体）。
- ORCA：**无邻居时解算结果必须逐字等于期望速度**（既有布局/e2e 的护栏，动它要同步改“逐位相同”用例）；
  玩家是零速度障碍、责任 1.0，智能体彼此 0.5；**不避让当前攻击目标**；每 tick 按 id 排序 Jacobi；
  半径真值 = `monster_config.json` 的 `bodyRadius`；调试 `--no-avoid` / `--probe`。
- `NpcBody` 层索引 **10**：碰撞矩阵只放行本地玩家层 9；相机防穿墙掩码必须排除它；代码一律用**层索引**；
  改层/矩阵前先关 Unity（2022.3 会按层名缓存回写 `TagManager.asset`）。
- 出生点 `(-15, 0.3, -30)`；几何守卫变红是设计，不是噪音。
- 已知限制：碰撞只在客户端；会移动的怪可走进静止玩家（最坏约 0.48m 交叠）；ORCA 是纯局部避让（对称挤压约 5% 视觉交叠）。

### Remote Entity Presentation（SyncLayer）
- 玩家 + bot 走同一条真模型路径（`_appearances` / `_playerAvatarPrefab` + `RemoteEntityAnimator`）；
  Npc 走 `_npcPrefabs` + `NpcEntityAnimator` + `NpcBodyCollider`；缺失时胶囊/球体占位。
- 分档：进入 60m / 退出 69m（迟滞）+ 每帧 `MaxFull` 预算；升档要当场 `Apply(view.ActionState, view.Dead)`
  （`m_KeepAnimatorStateOnDisable: 0` 会重置 Animator）。
- URL 旋钮：`?full=`、`?fulldist=`、`?quality=mobile`；调试浮层第二行
  `Full=… Proxy=… B=… Q=… FPS=…` 在 ~390px 竖屏也必须完整可见。

### Other systems（路径索引）
- Skills：`Skills/Definition|Data|Runtime|Effect|Events`；Combat：`Combat/` + `Sys3C/Core/Combat/`；
  Monster：`Monster/`；KCP：`AOT/KcpNet/`；UI：`UI/`（3 个面板从 `Resources/Prefabs/UI/` 加载，不在场景里）；
  Nameplate：`Nameplate/`；VFX：`VFX/`。
- 适配器：`CharacterStatsAdapter`、`ShieldSystemAdapter`、`PhysicsSystemAdapter`、`StatusControllerAdapter`。

## Development Standards
- Interface-oriented、高内聚低耦合；适配器解耦；EventBus 层间通信；Addressable 异步加载 + 对象池高频对象；
  Git 未启用 LFS（二进制裸存）。

## C# Syntax Rules
- **禁止**在 MonoBehaviour 非内部类上使用 GetComponent。
- 简单字符串插值用 `+`，避免复杂表达式塞进 `Debug.Log`。

## Current Status（压缩版）
KCP/protobuf、3C、Skills、Hit（表现只打通 Hit/DefendHit/Dizzy）、UI、Bag、Monster、Combat、Camera、VFX、Nameplate、
NPC Mirror、Movement/ORCA、Remote Entity 分档。完整状态与逐系统细节见 `docs/agent-architecture.md`。

## Token Economy（2026-10-21）
- **一个功能一个会话**；上下文到 20–30 万 token 就 `/compact`，不要让单会话跑到 70–80 万（缓存重读是主要消耗）。
- pre-compact hook 只输出摘要；`production/verify/` 与 `.superpowers/` 已加入 `.gitignore`。
- **不要 `Read` 全分辨率截图**；用 `python tools/shrink_image.py <in> <out>` 先缩到 ≤1024px，或用 MCP 截图的分辨率参数。
- 大文档用 `Read` 的 `offset/limit`；结构查询优先 `codegraph`；写大文档用 `cat > file <<'EOF'`，避免 `Write` 结果回显整份文件。
- 小功能（≤2–3 个任务）优先内联执行；不要为每个小任务重复加载 brainstorming/writing-plans/SDD。
  评审批量给反馈、只读 diff/改动段；子代理用能胜任的最便宜模型，审查者只回“结论 + file:line”，详情写报告文件。
- 工具输出：`git status --short`、`dotnet test --logger "console;verbosity=minimal"`、`Grep` 加 `head_limit`。
- MCP：Unity MCP 只保留 7 个工具（`tests-run` / `assets-refresh` / `script-execute` / `screenshot-game-view` /
  `console-get-logs` / `assets-get-data` / `editor-application-get-state`），配置在
  `UserSettings/AI-Game-Developer-Config.json`；其余在 Unity 的 AI Game Developer 窗口按需开。
  `agentmemory` 已从用户配置移除；`codegraph` 保留（`impact`/`trace`/`status` 无独立开关）。
- 完整指南：`docs/agent-token-hygiene.md`；详细架构：`docs/agent-architecture.md`。
