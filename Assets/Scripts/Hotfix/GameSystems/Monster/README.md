# Monster（客户端）—— 本地怪物 AI、属性与表现

**联网模式下怪物由服务端驱动**：`ClientNetworkHost` 会禁用场景内全部 `MonsterSpawner`，
客户端通过 `SyncLayer` 的 NPC 视图接收快照。本目录主要服务单机 / 本地玩法，
同时提供怪物配置与伤害管线。

## 目录

| 路径 | 内容 |
|---|---|
| `AI/` | `AIBrain`（唯一入口）→ `AIContext` + `AIStateMachine`；状态：Idle / Patrol / Chase / Attack / Hit / Defend / Taunt / Death |
| `Damage/` | `DamagePipeline`、`DamageContext` / `DamageResult`、`IDamageModifier`、`DefendModifier` / `IFrameModifier` |
| `MonsterEntity.cs` | 怪物实体，实现 `IDamageable` / `ITargetable` / `IEffectTarget` |
| `MonsterStats.cs` / `MonsterConfig.cs` | 属性与配置 |
| `MonsterMovement.cs` | 客户端移动 |
| `MonsterSpawner.cs` / `SpawnLedger.cs` | 生成与生成账本（防重复生成） |
| `MonsterLootTable.cs` | 掉落表 |
| `MonsterEvents.cs` | 怪物事件 |

## 服务端权威怪物

见 [Server/Monster](../../../../../Server/Monster/README.md)：行为树、目标感知、移动与 tick 在服务端；
`Server/Config/monster_config.json` 的 `bodyRadius` 是 ORCA 半径真值。

联网时服务端负责寻路：`Server/Navigation`（三角面 A* + 漏斗）+ `PathFollower` 决定怪物实际路径。
客户端只通过 `SyncLayer` 的 NPC 视图消费快照做位置 / 动画表现，不做本地绕障或导航；联网模式下
不得重新启用场景内 `MonsterSpawner` 或客户端 `AIBrain`。

## 护栏

- NPC 视图上不得挂 `IDamageable`（`NpcViewGameplayStripTests` 锁死）；本地技能不能命中断线上的 NPC 视图。
- 联网模式下客户端 `MonsterSpawner` 必须保持禁用。
- 怪物配置数值有编辑期断言（`MonsterConfigNumbersTests`）。
- 本地 AI 每帧校验目标（`AIBrain.TryFindTarget` → `ShouldDropTarget`）：Transform 销毁 /
  `IDamageable` 死亡 / 超出 `LeaveRange` 立即清空 `Target` 与 `TargetEntity`；攻击结算跳过
  已死亡目标（`MonsterAttackState.ResolveDamage`），不得对尸体继续攻击。

## 测试

Editor：`MonsterConfigNumbersTests`、`MonsterEntityDotDamageTests`、`SpawnLedgerTests`、`StatusEffectWiringTests`、
`AIBrainTargetValidityTests`。
PlayMode：`MonsterWorldTests`、`AoiWorldTests`、`CombatSyncTests`。

```bash
bash tools/unity-tests.sh EditMode MonsterConfigNumbersTests
bash tools/unity-tests.sh PlayMode MonsterWorldTests
```

## 相关模块

- [Sys3C](../Sys3C/README.md) —— 本地角色与伤害接口
- [Combat](../Combat/README.md) —— 命中检测
- [SyncLayer](../SyncLayer/README.md) —— NPC 视图与客户端碰撞
- [VFX](../VFX/README.md) —— 受击与状态特效
