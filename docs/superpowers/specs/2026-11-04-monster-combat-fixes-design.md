# 怪物战斗修复设计（尸体攻击 / 蜘蛛贴脸 / 大体型命中 / 攻击变体）

日期：2026-11-04
运行模式：联网（服务端 `--monsters 6`，客户端连服）
相关模块：[服务端怪物](../../../Server/Monster/README.md)、[SyncLayer](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)、[SyncCore](../../../Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md)、[客户端 Monster](../../../Assets/Scripts/Hotfix/GameSystems/Monster/README.md)

## 1. 背景

联网模式下确认到四个问题：

1. 角色死亡后，怪物持续反复播放攻击动作，攻击躺在地上的尸体。
2. 蜘蛛 Boss 会正常普攻，但玩家贴到脸上后它只推着玩家移动，不再攻击。
3. 角色普攻蜘蛛没有命中判定；剑气（10002 的投射物）能命中。
4. 怪物攻击动作单一：同一只怪的不同技能播同一段攻击动画。

已确认运行模式为联网；服务端权威战斗在 `Server/Handler/CastHandler.cs` 的 `CombatResolver`，
命中几何在双端共享的 `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs`。

## 2. 现状与根因

### 2.1 尸体攻击

- 服务端已有目标清理：`MonsterBrain.PruneInvalidThreat` 每 tick 移除死亡/离线/超出 `dropAggroRadius`
  的威胁；唯一目标死亡后 45 tick 进 Evade（`Server/Monster/Brain/MonsterBrain.cs`）。
- 服务端唯一的多打窗口是前摇锁目标（`AttackResolveTick`，0.35s），不足以产生“持续反复”。
- 客户端本地 AI 存在确定缺陷：`AIBrain.TryFindTarget` 在 `Target != null` 时永不重查
  （`Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs`）；`ChaseState` 与
  `MonsterAttackState.ResolveDamage` 也不校验目标存活，本地怪会对着尸体无限循环攻击。
- 因此“持续反复”优先怀疑本地 AI 或旧服务端构建；服务端本身需要用断言钉死“死亡后不再产生新施法”。

### 2.2 蜘蛛贴脸不攻击

- `MonsterBtActions.Chase` 的停步条件与 `Attack` 的出手条件都调用
  `PathFollower.HasLineOfSight(..., m.Config.bodyRadius)`（`Server/Monster/MonsterBtActions.cs`）。
- `NavMeshGraph.HasLineOfSight` 每 0.25m 采样，要求每个采样点 navmesh clearance ≥ 传入半径
  （`Server/Navigation/NavMeshGraph.cs`）。蜘蛛 `bodyRadius = 1.74`，贴脸时 LOS 失败：
  `Attack` 返回 Failure → 行为树回退到 `Chase` 继续前推，表现为“推着玩家走但不攻击”。
- 判定帧 `NpcLineOfSight` 也用 `BodyRadiusOf(npcId)`（`Server/Server3C.cs`），起手后同样可能被过滤。
- `HasLineOfSight` 还要求目标点本身在 navmesh 上；玩家不受 navmesh 约束，站在网格外时即使半径改小也会一直失败。

### 2.3 普攻不中蜘蛛

- 玩家 `CharacterController` 半径 0.5m（`Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`），
  蜘蛛模型包围盒约 3.48m 宽（`bodyRadius = 1.74`，客户端 `NpcBodyCollider` 从 SMR bounds 生成）。
  玩家被挡在离蜘蛛中心约 2.24m 外。
- 普攻 10001 射程 2.0m（`CombatRules`）；服务端命中只比较目标**中心点**距离
  （`HitResolver.Resolve`），因此普攻必然落空。重击 10002 的挥击 2.5m 只在正面可能命中，
  剑气 12m 线性判定不受影响——与“普攻不中、剑气中”一致。
- 大体型目标没有参与命中距离计算，是根因。

### 2.4 攻击动作单一

- `NpcAttackSchedule.SkillVariants`（`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs`）
  把 DogKnight 的 50003 / 50005 都映射到变体 0，把 SpiderBoss 的 50004 / 50006 都映射到变体 1。
- 四只怪的 NPC 控制器都具备 Attack01 / Attack02 两段不同剪辑，资源侧没有缺口。

## 3. 目标与非目标

**目标**

- 服务端保证：目标死亡/失效后，不再产生任何针对它的新施法；本地 AI 不再锁定死亡或超距目标。
- 大体型 Boss 贴脸时能正常起手攻击，且攻击能命中。
- 玩家近战命中判定计入目标体型半径；剑气等线形判定同样计入。
- 多技能映射到不同攻击动画；单技能怪物行为不变。

**非目标**

- 不改判定帧时序（`HitDelaySeconds` 仍是 0.35s 占位）、不接动画事件。
- 不新增技能、VFX 或动画资产。
- 不做物理 LOS / 动态障碍；继续用 navmesh 空洞作为遮挡代理。
- 不改变 PvP 平衡：玩家实体半径保持 0。

## 4. 设计

### 4.1 服务端：目标死亡时取消前摇

**改动**

`MonsterBrain.Update` 在“前摇锁目标”分支增加有效性校验：

- 当 `m.AttackResolveTick.HasValue` 且当前 `m.TargetId` 已不在 `IsValidTarget`（死亡 / 离线 / 出 drop 半径）时，
  立即清空 `m.AttackResolveTick`，让后续逻辑走“无目标”分支：清 `TargetId`、`LastKnownTargetPosition`，
  行为树当 tick 写 `Idle`。
- 已在 `CombatResolver` 队列里的施法仍会到期结算；目标已死时 `HitResolver` 的 Alive 过滤保证零伤害。
  已广播的挥砍动画会播完一次，但不会继续施法。

**为什么不是“只依赖现有清理”**

现有清理在目标失效后的下一 tick 生效；前摇锁目标会多保留 6 tick。显式取消把窗口收紧到当 tick，
并给“死亡后零新施法”一个可断言的出口。

**验收**

服务端集成测试：怪物处于前摇，玩家 HP 归零；推进 tick 后断言
`AttackResolveTick == null`、`TargetId == null`、`ActionState == Idle`、`DrainPendingCasts()` 为空。
若该测试通过而线上症状仍在，按 §6 的手工排查确认本地 spawner / 构建版本，不继续改服务端。

### 4.2 服务端：攻击视线与移动体型解耦

**新增** `Server/Monster/AttackLineOfSight.cs`：

```csharp
public static class AttackLineOfSight
{
    public const float ProbeRadius = 0.25f;

    public static bool HasLineOfSight(NavigationService navigation, Vector3 from, Vector3 to)
        => navigation.HasLineOfSight(from, to, ProbeRadius);
}
```

**改动**

- `PathFollower` 增加 `HasAttackLineOfSight(from, to)`：无导航返回 true；有导航转调
  `AttackLineOfSight.HasLineOfSight`。
- `MonsterBtActions.Chase` 与 `Attack` 改用 `m.Follower.HasAttackLineOfSight(...)`；
  `bodyRadius` 只保留给寻路 / ORCA / 巡逻点 clearance。
- `Server3C.EnableMonsters` 的 `SetNpcLineOfSight` 闭包改为
  `AttackLineOfSight.HasLineOfSight(navigation, from, to)`，判定帧过滤与起手判断同源。

**语义**

- 攻击视线表达“武器到目标之间没有 navmesh 空洞”，用 0.25m 探测半径，不再要求整条线容得下 1.74m 的身体。
- 0.25m 小于 navmesh 烘焙的 0.5m agent 半径：它只回答“武器线有没有洞”，不保证身体能站在线上；
  身体位置仍由寻路保证。
- 不做目标点投影：`TryProject` 返回的点在 navmesh 边界上、clearance 为 0，0.25m 探针仍会失败，
  投影是无效代码。玩家站在网格外“挥不到”的既有已知限制继续保留（本轮不做物理 LOS）。
- 墙 / 岩石仍会在 navmesh 空洞采样处被拦住；`Monster_Does_Not_Attack_Through_Wall` 保持绿。

**验收**

合成 navmesh（大网格 + 2×2 空洞）：蜘蛛在距玩家 2.4m、玩家点 clearance 约 1.0m
（旧 1.74 半径失败、新 0.25 半径通过）时，`DrainPendingCasts()` 必须出现 50004。
另测隔墙时 `AttackLineOfSight.HasLineOfSight` 返回 false（`AttackLineOfSightTests`）。

### 4.3 客户端：本地 AI 目标有效性

**改动**

- `AIContext` 增加 `public IDamageable TargetEntity;`（与 `Target` 同步赋值）。
- `AIBrain.TryFindTarget` 每次调用先校验现有目标，提取
  `internal static bool ShouldDropTarget(Transform target, IDamageable entity, Vector3 selfPosition, float leaveRange)`：
  - 目标 Transform 为 null（Unity 销毁）→ 丢弃；
  - `entity == null || !entity.IsAlive` → 丢弃；
  - 与自身的平方距离 > `leaveRange²` → 丢弃。
  丢弃后清空两个字段并重新走 `PhysicsRegistry.FindNearby` 索敌。
- **Unity 伪 null 顺序**：`target == null` 必须最先判断。`IDamageable` 是接口引用，不参与
  UnityEngine.Object 的伪 null 重载；对象销毁后接口引用仍非 null，先解引用 `entity.IsAlive`
  有 `MissingReferenceException` 风险。`ShouldDropTarget` 内部顺序固定为：Transform 销毁检查
  → 实体存活检查 → 距离检查。
- **外层判断**：`TryFindTarget` 的入口条件必须是 `Target != null || TargetEntity != null`。
  Unity 对象被销毁后 `Target == null` 为真，若只用 `Target != null` 就永远不会走到清理分支，
  接口引用会一直挂着。测试用例必须覆盖“Transform 已销毁、接口引用仍非 null”这一分支。
- `MonsterAttackState.ResolveDamage` 在 `TakeDamage` 前跳过 `!IsAlive` 的目标。

**说明**

联网模式下 `ClientNetworkHost` 会 `DespawnAll` 并禁用场景内 `MonsterSpawner`，本地 AI 不应在线上生效；
本项修复覆盖单机模式与“联网装配失败/本地 spawner 未禁用”的兜底场景，同时消除已确认的潜在缺陷。

**验收**

- EditMode 单元测试覆盖 `ShouldDropTarget` 的真值表：存活且近 → 保留；死亡 → 丢弃；超距 → 丢弃；
  Transform 为 null → 丢弃；销毁对象（Transform 已 null、接口引用非 null）→ 丢弃且不抛异常。
- EditMode 接线测试：把 `AIBrain.TryFindTarget` 改为 `internal`，测试直接构造 `AIContext`
  （真实 `Self`/`Target` GameObject + 假 `IDamageable`），调用 `TryFindTarget`，断言死亡目标被清空、
  存活目标被保留、销毁对象不抛异常。该用例覆盖 `TryFindTarget → 清目标` 的接线，不只测纯函数
  （`GameSys.EditorTests` 已有 `InternalsVisibleTo`）。
- 断言 Unity 对象的伪 null 时用 `Assert.IsTrue(obj == null)`（静态类型走 Unity 重载）；
  不要用 `Assert.IsNull`——它按 `object` 比较，销毁对象不是引用 null。

### 4.4 服务端：命中判定计入目标体型

**数据模型**

- `PlayerState` 增加 `public float BodyRadius { get; internal set; }`。
- `EntityStore.AddNpc(..., float bodyRadius = 0f)` 写入该字段；默认 0 保持既有调用不变。
- `MonsterModule.SpawnMonster` 在 `AddNpc` 前完成 `config.bodyRadius` 归一化，并把归一化值传给 `AddNpc`。
- `CombatCandidate` 增加 `float Radius`（构造函数可选参数，默认 0）；
  `CombatResolver.BuildCandidates` 传入 `p.BodyRadius`（玩家为 0）。

**几何**

`HitResolver` 统一使用“表面距离”：

- 单目标 / 自动目标 / `ResolveCircle`：`max(0, DistXZ - c.Radius)` 与射程比较；
  自动目标按表面距离取最近。
- `ResolveLineAll` / `ResolveLineNearest`：横向 `|叉积| <= halfWidth + c.Radius`；
  纵向区间扩展 `along ± c.Radius`。
- 锥形方向判定保持中心方向不变；显式目标仍过锥（D-6 语义不变）。

**效果**

蜘蛛（半径 1.74）在中心距 2.24m 时，普攻有效距离为 0.5m ≤ 2.0m，命中。
PvP 玩家半径 0，行为与现状逐值一致。

**近似与残余**

- 客户端 `NpcBodyCollider` 是盒体（世界半宽约 1.66 / 1.74），服务端用半径 1.74 的圆近似；
  X 向最多多算约 8cm，不影响可玩性。
- 锥形方向仍按目标中心方向判定；玩家贴侧身、朝向与中心方向夹角超过半锥角时仍可能不中
  （圆的最接近点在中心方向上，这一近似在体面收益与锥形语义之间取了简单值）。

**验收**

- `HitResolverTests`（双端两份）：半径 1.74 的目标在中心距 3.0m 被 2.0m 射程命中；
  半径 0 时同距离不命中；线形横向偏移 2.5m 在 halfWidth 2 + 半径 1.74 下命中、半径 0 时不命中。
- `CombatResolverNpcTests`：大半径 NPC 在中心距 3.2m 被 10001 命中并扣血。

### 4.5 客户端：多技能攻击变体

`NpcAttackSchedule.SkillVariants` 调整为：

| skillId | 怪物 | 变体 | 动画 |
|---|---|---|---|
| 50001 | Slime | 0 | Attack01 |
| 50002 | Wolf | 1 | Attack02 |
| 50003 | DogKnight 普攻 | 0 | Attack01 |
| 50005 | DogKnight 突进斩 | 1 | Attack02 |
| 50004 | SpiderBoss 普攻 | 0 | Attack01 |
| 50006 | SpiderBoss 低血 AoE | 1 | Attack02 |

- 映射仍由 skillId 固定推出，所有客户端一致，不引入客户端计数器。
- 未登记技能继续回退时间戳变体。
- `NpcAnimator_*` 的 Attack 状态自带 exit-time → Idle 转移；变体只决定进入哪段剪辑，
  不改状态机结构。两段剪辑的语义需要在手工 E2E 里目视确认（Attack02 对第二技能是否合适）。
- 攻击动画剪辑长度与服务端 0.35s 判定帧仍不匹配，属既有已知限制，本轮不处理。

**验收**

- `NpcAttackScheduleTests.Known_Monster_Skill_Maps_To_A_Fixed_Variant` 按新表更新；
  “固定 0 与回退可区分”的奇桶断言改用仍映射 0 的 50004 验证。
- `CombatNetworkBridgeTests.CastBroadcast_From_An_Npc_Schedules_A_Swing_And_From_A_Player_Does_Not`
  的 NPC 侧改用仍映射 1 的 50006（原用例前提写死 50004 → 1，改表后会红）。
- DogKnight / Spider 的 Attack01/02 由 `NpcAnimatorAssetTests` 继续守护。

## 5. 数据流

1. 玩家普攻 → `SkillCastReporter` → `CastRequest`（形状 + 锁定目标）→ 服务端 `ValidateShape`
   → `BuildCandidates`（带 NPC 半径）→ `HitResolver` 表面距离判定 → `ApplyHit`
   → `DamageResult` → 客户端 `MonsterTakeDamageEvent`（红字）。
2. 蜘蛛 AI → `Attack` 用 0.25m 探测半径判断 LOS → `QueueCast`
   → 同上判定链；判定帧 `NpcLineOfSight` 使用同一 helper。
3. 本地 AI → 每帧 `ShouldDropTarget` 校验 → 清目标后重新索敌。

## 6. 测试与手工验收

**自动化**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HitResolver"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolver"
bash tools/unity-tests.sh EditMode NpcAttackScheduleTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/unity-tests.sh EditMode HitResolverTests
```

新增/更新用例：

- 服务端：`Player_Death_During_Windup_Cancels_And_Stops_Casts`、
  `Big_Boss_Attacks_At_Point_Blank_With_Low_Clearance_Target`、
  `Player_Melee_Hits_Large_Npc_By_Surface_Distance`、
  `Line_Hit_Expands_By_Target_Radius`。
- Unity EditMode：`ShouldDropTarget` 真值表、`AIBrain.TryFindTarget` 接线用例、
  `Known_Monster_Skill_Maps_To_A_Fixed_Variant` 更新、`CombatNetworkBridgeTests` NPC 侧改 50006。

**手工 E2E（联网）**

1. 用 `dotnet <dll> 8000 --monsters 6 --map demoday` 起服（不要用 `dotnet run`，apphost 会让
   HttpListener.Start 抛“句柄无效”）。
2. 走到蜘蛛 (`18.333, 18.389`) 贴脸：应每 ≤2s 看到攻击动作且玩家掉血；不再只推不砍。
3. 用普攻打蜘蛛：服务端红字伤害出现（中心距约 2.2–2.5m 也可命中）。
4. 让角色被打死：倒地后怪物不应继续反复挥砍；Console 应有
   `[NetHost] 本地 MonsterSpawner 已禁用`；若没有，说明本地怪没被清掉，按 §4.3 复核。
5. 打 DogKnight 到半血：50005 应播 Attack02，50003 播 Attack01；蜘蛛 50006 播 Attack02、
   50004 播 Attack01。

## 7. 风险与兼容

- **小探测半径是否会隔墙命中**：遮挡仍由 navmesh 空洞采样承担，只是不再要求整条线容得下身体；
  现有 `Monster_Does_Not_Attack_Through_Wall` 与新增隔墙断言共同守住。
- **半径改动影响 PvE 手感**：只对 `BodyRadius > 0` 的 NPC 生效；玩家（0）的 PvP 距离逐值不变。
- **服务端原有 NPC 测试**：`AddNpc` 默认半径 0，既有用例不受影响；新增用例显式传半径。
- **动画映射测试**：两处写死旧映射的断言必须同步更新，否则改表即红。
- **尸体问题残余窗口**：同一 tick 内“怪物先决策、伤害后结算”时，已广播的那一刀仍会播完；
  无伤害、无后续施法，属可接受残余。
- **客户端 `NpcEntityAnimator._awaitingConfirm` 无超时（backlog）**：若确认快照整段落在渲染时钟之后，
  驱动会忽略后续 Walk/Run 写入，表现为怪物滑行；与本轮四个问题无关，不在本轮修改。
- **盒体/圆近似与锥形中心判定**：见 §4.4“近似与残余”，不阻塞验收。

## 8. 决策记录

- 攻击 LOS 选择“小探测半径”，不选“贴脸跳过 LOS”（会隔墙命中）或“调小蜘蛛 bodyRadius”
  （破坏寻路与避让）；目标点投影经验证无效（投影点在网格边界、clearance 为 0），不采用。
- 命中判定选择“目标半径参与表面距离”，不选“提高近战射程”（全局平衡 + 角落仍失配）或
  “缩小客户端碰撞体”（视觉穿模且服务端仍按中心距）。
- 动画变体选择“按技能固定映射”，不选“同一技能按时间戳轮换”（本轮需求明确为不同技能不同动作）。

## 9. 验收清单

- [ ] 服务端：死亡目标前摇取消 + 零新施法测试通过。
- [ ] 服务端：蜘蛛低 clearance 贴脸能起手、能命中；隔墙仍不能攻击。
- [ ] 服务端：大体型 NPC 的表面距离命中测试通过；PvP 用例不变。
- [ ] 客户端：本地 AI 死亡/超距清目标单元测试通过。
- [ ] 客户端：技能 → 动画变体映射测试更新并通过。
- [ ] 手工 E2E 五项全部通过。

## 10. 文档同步

按 `CLAUDE.md` 的“改动模块行为时同步更新 README”：

- `Server/Monster/README.md`：攻击 LOS 探针（0.25m）、命中计入目标半径、
  目标死亡取消前摇；注明网格外目标仍可能挥不到（本轮不做物理 LOS）。
- `Server/Navigation/README.md`：注明 `HasLineOfSight` 原始语义不变；攻击链路经
  `AttackLineOfSight` 用小探针，不做投影。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`：更新 50003/50005/50004/50006 的
  动画变体映射（原“50001/50003/50005 → 0，其余 → 1”）。
- `Assets/Scripts/Hotfix/GameSystems/Monster/README.md`：本地 AI 目标有效性（死亡 / 超距 / 销毁）。
