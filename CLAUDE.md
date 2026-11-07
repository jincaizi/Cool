# CLAUDE.md

Unity 2022 LTS MMO 客户端（服务端集成中），发布目标为移动平台。

## 先读模块 README（强制）

本文件只保留全局约定与模块索引。**修改任何模块前，先读该模块目录下的 `README.md`**；
模块的设计、关键文件、护栏与测试命令以模块 README 为准。改动模块行为时同步更新该 README。

## 技术栈

- Unity 2022.3.25f1 (LTS)
- 网络：移动平台（KCP + Protobuf） WebGL（websocket）后续可能会改动
- 资源：Addressable
- 程序集分层：`Assets/Scripts/AOT/`（IL2CPP 编译的基础设施）+ `Assets/Scripts/Hotfix/GameSystems/`（业务系统）
- 热更：无。HybridCLR 未安装（`Packages/manifest.json` 无该包，`Assets/HybridCLRData` 不存在）；
  `Hotfix.*` 只是普通 asmdef 程序集，没有代码热重载能力。当前未实现热更，保留能快速接入热更的可能性

## 常用命令

- Unity Editor：`start Unity.exe -projectPath E:\CodeForJob\Cool`；构建用 `Ctrl+Shift+B`
- Git：`git add <files>` 后 `git commit -m "<message>"`；**未启用 LFS**，二进制裸存
- 服务端测试：`bash tools/test-server.sh`（minimal logger；可追加 `--filter "FullyQualifiedName~X"`）；
  本机 testhost 抛 `Win32Exception(5)` 时改用 `bash tools/test-server-inprocess.sh`（同参数）
- Unity 测试：`bash tools/unity-tests.sh EditMode [Class]` / `bash tools/unity-tests.sh PlayMode Class`
  （封装了 `--input` JSON 与 300s 超时；原始命令见脚本）

## 模块索引

| 模块 | 路径 | 职责 |
|---|---|---|
| [3C](Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md) | `Assets/Scripts/Hotfix/GameSystems/Sys3C/` | 角色、输入、相机、FSM、移动、适配器 |
| [技能](Assets/Scripts/Hotfix/GameSystems/Skills/README.md) | `Assets/Scripts/Hotfix/GameSystems/Skills/` | 技能数据、状态机、执行器、效果与事件 |
| [战斗](Assets/Scripts/Hotfix/GameSystems/Combat/README.md) | `Assets/Scripts/Hotfix/GameSystems/Combat/` | Hitbox、受击区；攻击形状与伤害接口在 `Sys3C/Core/Combat/` |
| [怪物（客户端）](Assets/Scripts/Hotfix/GameSystems/Monster/README.md) | `Assets/Scripts/Hotfix/GameSystems/Monster/` | 本地怪物 AI、属性、伤害管线、生成 |
| [背包](Assets/Scripts/Hotfix/GameSystems/Bag/README.md) | `Assets/Scripts/Hotfix/GameSystems/Bag/` | 背包数据、物品、管理器 |
| [UI](Assets/Scripts/Hotfix/GameSystems/UI/README.md) | `Assets/Scripts/Hotfix/GameSystems/UI/` | UIManager、面板、移动端输入 |
| [名字板 / 飘字](Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md) | `Assets/Scripts/Hotfix/GameSystems/Nameplate/` | 名字、飘字、伤害屏效、精力环 |
| [VFX](Assets/Scripts/Hotfix/GameSystems/VFX/README.md) | `Assets/Scripts/Hotfix/GameSystems/VFX/` | 武器、技能、受击与状态特效 |
| [精力](Assets/Scripts/Hotfix/GameSystems/Stamina/README.md) | `Assets/Scripts/Hotfix/GameSystems/Stamina/` | 冲刺与格挡的精力模型 |
| [同步层](Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md) | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/` | 网络接线、远端实体表现、客户端碰撞 |
| [同步规则](Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md) | `Assets/Scripts/Hotfix/GameSystems/SyncCore/` | 双端共享时钟、移动校验、命中裁决 |
| [协议](Assets/Scripts/AOT/Protocol/README.md) | `Assets/Scripts/AOT/Protocol/` | 双端共享协议与消息 |
| [KCP](Assets/Scripts/AOT/KcpNet/README.md) | `Assets/Scripts/AOT/KcpNet/` | 传输、会话、消息调度 |
| [数据定义](Assets/Scripts/AOT/DataDefinition/README.md) | `Assets/Scripts/AOT/DataDefinition/` | 全局常量与设置 |
| [资源](Assets/Scripts/AOT/Core/Resource/README.md) | `Assets/Scripts/AOT/Core/Resource/` | Addressables 异步加载 |
| [服务端](Server/README.md) | `Server/` | 主循环、AOI、房间、会话、怪物 |
| [服务端移动](Server/Movement/README.md) | `Server/Movement/` | ORCA 避让 |
| [服务端怪物](Server/Monster/README.md) | `Server/Monster/` | 行为树驱动的权威怪物逻辑 |
| [服务端导航](Server/Navigation/README.md) | `Server/Navigation/` | 三角面 A* + 漏斗、地图加载 |

## 全局护栏（改动前确认）

- **3C 状态层**：只有 `BaseFSM` 与 `HitFSM`；攻击层没有 `AttackFSM`，由 `StateCoordinator` + `SkillCoordinator` 驱动。
- **碰撞分两半**：怪物 / bot 彼此避让在服务端 ORCA；玩家 vs 怪物阻挡在客户端 `NpcBodyCollider`。
  改任一半前读 [服务端移动](Server/Movement/README.md) 与 [SyncLayer](Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)。
- **`NpcBody` 层索引 10**：碰撞矩阵只放行本地玩家层 9；相机防穿墙掩码必须排除它；代码一律用层索引。
- **出生点** `(-15, 0.3, -30)`；几何守卫变红是设计，不是噪音。
- **Hit 表现覆盖**：只有 `Hit` / `DefendHit` / `Dizzy` 可达；`Knockback` / `Launched` / `Death` / `Down` / `GetUp` 无动画表现（backlog）。
- **远端实体分档**：进入 60m / 退出 69m（迟滞）；升档要当场 `Apply(view.ActionState, view.Dead)`。详见 [SyncLayer](Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)。

## 开发约定

- Interface-oriented、高内聚低耦合；适配器解耦；EventBus 层间通信；Addressable 异步加载 + 对象池高频对象。
- **禁止**在 MonoBehaviour 非内部类上使用 GetComponent。
- 简单字符串插值用 `+`，避免复杂表达式塞进 `Debug.Log`。

## Current Status

已打通：KCP / Protobuf、3C、Skills、Combat（含格挡反馈）、UI、Bag、Monster（服务端行为树 + 客户端 NPC 视图）、
Camera、VFX、Nameplate、NPC Mirror、Movement / ORCA、Remote Entity 分档、Stamina。逐模块状态与已知限制见对应 README。

## Token Economy

- 一个功能一个会话；上下文到 20–30 万 token 就 `/compact`。
- 大文件用 `Read` 的 `offset/limit`；结构查询用 `codegraph`；截图先缩到 ≤1024 px。
- 小功能（≤2–3 个任务）内联执行，不为每个小任务重复加载 brainstorming / writing-plans / SDD。
- 工具输出保持短：`git status --short`、minimal logger、`Grep` 加 `head_limit`。
- Unity MCP core 1.3.0：启用 6 个工具；控制台日志查 `rql-query` 的 `console` 源，
  资产与编辑器状态查 `unity.asset(...)` / `editor` 源。完整配置见 `docs/agent-token-hygiene.md`。
- 完整指南：`docs/agent-token-hygiene.md`；跨模块流程：[`docs/agent-architecture.md`](docs/agent-architecture.md)。
