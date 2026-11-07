# Agent Architecture Index

> 2026-10-25：逐系统细节已下沉到各模块目录的 `README.md`；本文件只保留模块索引与跨模块流程。
> 修改模块前先读该模块 README；全局约定见 [`CLAUDE.md`](../CLAUDE.md)。

## 模块索引

| 模块 | README | 职责 |
|---|---|---|
| 3C | [Sys3C](../Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md) | 角色、输入、相机、FSM、移动、适配器 |
| 技能 | [Skills](../Assets/Scripts/Hotfix/GameSystems/Skills/README.md) | 技能数据、状态机、执行器、效果与事件 |
| 战斗 | [Combat](../Assets/Scripts/Hotfix/GameSystems/Combat/README.md) | Hitbox、受击区、攻击形状与伤害接口 |
| 怪物（客户端） | [Monster](../Assets/Scripts/Hotfix/GameSystems/Monster/README.md) | 本地怪物 AI、属性、伤害管线、生成 |
| 背包 | [Bag](../Assets/Scripts/Hotfix/GameSystems/Bag/README.md) | 背包数据、物品、管理器 |
| UI | [UI](../Assets/Scripts/Hotfix/GameSystems/UI/README.md) | UIManager、面板、移动端输入 |
| 名字板 / 飘字 | [Nameplate](../Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md) | 名字、飘字、伤害屏效、精力环 |
| VFX | [VFX](../Assets/Scripts/Hotfix/GameSystems/VFX/README.md) | 武器、技能、受击与状态特效 |
| 精力 | [Stamina](../Assets/Scripts/Hotfix/GameSystems/Stamina/README.md) | 冲刺与格挡的精力模型 |
| 同步层 | [SyncLayer](../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md) | 网络接线、远端实体表现、客户端碰撞 |
| 同步规则 | [SyncCore](../Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md) | 双端共享时钟、移动校验、命中裁决 |
| 协议 | [Protocol](../Assets/Scripts/AOT/Protocol/README.md) | 双端共享协议与消息 |
| KCP | [KcpNet](../Assets/Scripts/AOT/KcpNet/README.md) | 传输、会话、消息调度 |
| 数据定义 | [DataDefinition](../Assets/Scripts/AOT/DataDefinition/README.md) | 全局常量与设置 |
| 资源 | [Core/Resource](../Assets/Scripts/AOT/Core/Resource/README.md) | Addressables 异步加载 |
| 服务端 | [Server](../Server/README.md) | 主循环、AOI、房间、会话、怪物 |
| 服务端移动 | [Server/Movement](../Server/Movement/README.md) | ORCA 避让 |
| 服务端怪物 | [Server/Monster](../Server/Monster/README.md) | 行为树驱动的权威怪物逻辑 |

## 跨模块流程

### 输入 → 技能 → 伤害 → 表现

1. `Sys3C/Input/InputManager` 输出 `MoveCommand` 与攻击 / 技能输入。
2. `Skills/Runtime/SkillCoordinator` 处理输入、冷却与打断，交给 `SkillStateMachine`。
3. `SkillExecutor` 在 Hitbox 窗口内用 `Sys3C/Core/Combat` 的攻击形状筛选目标，调用 `IDamageable`。
4. 命中结果经 `Skills/EventBus` 广播：`Nameplate` 显示飘字，`VFX` 播放受击 / 格挡特效，
   `Stamina` 处理格挡消耗。
5. 联网时客户端只上报施法（`SyncLayer/SkillCastReporter`），服务端 `Server/Handler/CastHandler`
   用 `SyncCore/HitResolver` 做权威裁决。

### 网络同步

- 传输：`AOT/KcpNet`（会话与消息边界）。
- 协议：`AOT/Protocol`（双端同源编译）。
- 客户端：`SyncLayer/ClientNetworkHost` → `ClientNetworkManager` / `MessageDispatcher` /
  `RemoteEntityRegistry`；移动预测与对账用 `SyncCore`。
- 服务端：`Server/GameLoop` 单线程消费 → `Server/Handler` / `Server/Monster` / `Server/Aoi`。
- 共享规则：`SyncCore`（时钟、移动校验、命中裁决）。

### 移动与碰撞

- 怪物 / bot 彼此避让：服务端 `Server/Movement/OrcaSolver` + `AvoidanceSystem`。
- 玩家 vs 怪物阻挡：客户端 `SyncLayer/NpcBodyCollider`，`NpcBody` 层索引 10。
- 玩家移动：客户端 `MovementReporter` + `NetMovementPolicy`；服务端 `PositionSyncHandler`
  用 `SyncCore/MovementValidator` 校验。

## 历史说明

2026-10-21 之前的版本把逐系统细节集中在本文件；2026-10-25 起改为模块目录内的 `README.md`，
避免文档与代码漂移。本文件不再复制模块细节。
