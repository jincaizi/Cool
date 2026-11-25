# Monster（客户端）—— 本地怪物 AI、属性与表现

**联网模式下怪物由服务端驱动**：`ClientNetworkHost` 会禁用场景内全部 `MonsterSpawner`，
客户端通过 `SyncLayer` 的 NPC 视图接收快照。本目录主要服务单机 / 本地玩法，
同时提供怪物配置与伤害管线。

## 目录

| 路径 | 内容 |
|---|---|
| `AI/` | `AIBrain`（唯一入口）→ `AIContext` + `AIStateMachine`；状态：Idle / Patrol / Chase / Attack / Hit / Defend / Taunt / Death |
| `Damage/` | `DamagePipeline`、`DamageContext` / `DamageResult`、`IDamageModifier`、`DefendModifier` / `IFrameModifier` |
| `MonsterEntity.cs` | 怪物实体，实现 `IDamageable` / `ITargetable` / `IEffectTarget`；`BodyRadius` 供表面距离选敌 |
| `OfflineMonsterStatuses.cs` | 单机元素状态，接线共享运行时、本地战斗时钟、DoT 与状态表现 |
| `MonsterStats.cs` / `MonsterConfig.cs` | 属性与配置 |
| `MonsterMovement.cs` | 客户端移动 |
| `MonsterSpawner.cs` / `SpawnLedger.cs` | 生成与生成账本（防重复生成）；`Active` / `SnapshotAlive()` 供单机剑气回荡选敌 |
| `MonsterLootTable.cs` | 掉落表 |
| `MonsterEvents.cs` | 怪物事件 |

## 服务端权威怪物

见 [Server/Monster](../../../../../Server/Monster/README.md)：行为树、目标感知、移动与 tick 在服务端；
`Server/Config/monster_config.json` 的 `bodyRadius` 是 ORCA 半径真值。

联网时服务端负责寻路：`Server/Navigation`（三角面 A* + 漏斗）+ `PathFollower` 决定怪物实际路径。
客户端只通过 `SyncLayer` 的 NPC 视图消费快照做位置 / 动画表现，不做本地绕障或导航；联网模式下
不得重新启用场景内 `MonsterSpawner` 或客户端 `AIBrain`。

动作表现消费服务端 `NpcActionEvent`（2004，`LifeId` 参与合并键）与 AOI 进入基线的
`EntityFieldMask.NpcAction`：阶段/取消由 `RemoteEntityRegistry` 按 `(EntityId, LifeId,
InstanceId, Revision)` 合并；攻击起手仍由 `CastBroadcast` → `NpcAttackSchedule` 驱动。
事件只影响表现，本目录的本地 AI 不参与联网动作存储。

## 单机武器元素

- 10002 的首次有效挥击或剑气命中为存活目标附加状态，挥击与剑气共用施法级去重。
- `OfflineMonsterStatuses` 用 Unity 游戏时间推进共享的 15 Hz 状态时钟。冰调整
  `NavMeshAgent.speed`，雷暂停 AI 行动与移动 0.6s，蜘蛛的霸体前摇免疫麻痹、
  普通受击打断和击退。
- 风在直接伤害扣血前读取，首个附加命中按附加前的倍率结算。每个状态 tick 先清除
  到期易伤，再结算灼烧。灼烧会结算一次已排定的末跳，直接扣血并发 DoT 飘字事件。
- 死亡、禁用和复用时清除元素状态，发布对应的状态移除事件。

## 护栏

- NPC 视图上不得挂 `IDamageable`（`NpcViewGameplayStripTests` 锁死）；本地技能不能命中断线上的 NPC 视图。
- 联网模式下客户端 `MonsterSpawner` 必须保持禁用。
- 怪物配置数值有编辑期断言（`MonsterConfigNumbersTests`）。
- 单机怪物被炎爆命中时沿爆心方向受推，位移沿 NavMesh 推进，到导航边界停止。
- 本地 AI 每帧校验目标（`AIBrain.TryFindTarget` → `ShouldDropTarget`）：Transform 销毁 /
  `IDamageable` 死亡 / 超出 `LeaveRange` 立即清空 `Target` 与 `TargetEntity`；攻击结算跳过
  已死亡目标（`MonsterAttackState.ResolveDamage`），不得对尸体继续攻击。

## 测试

Editor：`MonsterConfigNumbersTests`、`MonsterEntityDotDamageTests`、`SpawnLedgerTests`、`StatusEffectWiringTests`、
`AIBrainTargetValidityTests`、`OfflineElementalWeaponTests`。
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
