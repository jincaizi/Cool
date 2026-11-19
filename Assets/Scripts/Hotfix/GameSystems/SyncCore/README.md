# SyncCore —— 客户端 / 服务端共享的同步规则

纯 C#、零 `UnityEngine` 依赖；同一批源文件被客户端 asmdef 和 `Server/Server3C.csproj` 编译。
`Vec3` 是共享向量类型。

| 文件 | 职责 |
|---|---|
| `ClockSyncAlgorithm.cs` | 时钟同步：min-filter + 一阶低通；`Offset = 服务器时钟 − 客户端墙钟` |
| `MovementValidator.cs` | 移动合理性校验：瞬移门槛、速度钳制、纠偏目标 |
| `PredictionReconciler.cs` | 客户端预测帧对账；位置阈值 0.5m、旋转 5°、最多 60 帧 |
| `HitResolver.cs` | 服务端权威命中裁决；几何与客户端 `AttackShape` 对齐；锥形按目标半径扩展（圆-锥相交）；可选高度重叠过滤 |
| `CombatRules.cs` | 投射物判定种类与参数（剑气 / 火球 / 落点圆）、旋转重复判定（`RepeatSpec`）、攻击高度带（`AttackHeightSpec`） |
| `MovementBudgetMath.cs` | PvP 位移预算纯函数：接收 tick 差、普通/突进/跳跃惯性距离、水平钳制、跳跃抛物线 |
| `Elemental/StatusRuntime.cs` | 权威状态运行时：应用/刷新/到期/DoT 时序、控制派生与易伤倍率（Plan 04） |
| `Vec3.cs` | 共享向量 |

## 属性剑气共享目录（2026-11-14）

状态：Plan 01–04 已完成（Plan 03 增加 `Displacements`/`Jump` 共享表与 `MovementBudgetMath`；
Plan 04 增加 `StatusRuntime`、状态时序与 PvP 规则）；Plan 05 未开始。
离线单机**尚未接入**共享状态运行时（本地 `StatusEffectManager` 的冰冻/灼烧保持独立），
四元素共享运行时留给 Plan 05；详见下文“离线边界”。

`Elemental/` 是 Plan 01 的双端共享真值：

| 文件 | 职责 |
|---|---|
| `ElementalCatalogDefinition.cs` | 武器/元素/状态/普攻时序/剑气几何/位移/跳跃/PvP 参数 DTO（`[Serializable]` 公共字段） |
| `ElementalCatalogValidator.cs` | 纯函数校验：重复 ID、缺失引用、数值区间、默认剑在初始清单、剑气 `Length/Speed == TotalFlightSeconds` |
| `ElementalCatalogDefaults.cs` | 首版默认表与稳定 ID（90001–90005；10001/10002 时序；12m/2m/18m/s/3 段） |
| `ElementalCatalogHasher.cs` | 稳定排序 + little-endian 语义流 SHA-256；排除 `ContentHash` 自身 |
| `WeaponCastSnapshot.cs` | 施法时冻结的武器实例/模板/元素/版本/攻击力/攻速/剑气倍率 |

服务端启动从 `Server/Config/elemental_catalog.json` 加载并校验；Unity EditMode
`ElementalCatalogTests` 读取 `SwordQi_Heavy.asset` 与默认表逐项对照，`CombatRules` 10002
的 `Speed=18` 与目录同源。缺表/校验失败时服务端停用该功能（不发放武器、不协商能力位），
客户端指纹不匹配时拒绝登录。构建校验命令：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ElementalCatalogValidatorTests"
bash tools/unity-tests.sh EditMode ElementalCatalogTests
```

## 位移预算与 PvP 基准（Plan 03，2026-11-18）

`MovementBudgetMath` 是双端共享的纯函数；**客户端时间戳不参与计算**，服务端只信
相邻两包到达 tick 的差。`CombatRules.TickRateHz = 15`。

| 函数 | 公式 / 语义 |
|---|---|
| `ClampDeltaSeconds(now, last)` | `(now − last) / 15`，单次上限 `MaxAccumulatedSeconds = 0.5s`；`last == 0` 或 `now <= last` 返回 0 |
| `NormalDistance(mode, defending, statusScale, dt)` | `基础速度 × 防御倍率 × 状态倍率 × dt`；基础速度 Walk 5 / Sprint 8（`CombatRules.WalkSpeed/SprintSpeed`），防御 0.4（`DefendSpeedMultiplier`），状态倍率钳到 `[0,4]`（非有限按 0） |
| `DashSpeed(distance, duration, startup, recovery)` | `distance / (duration − startup − recovery)`；非有限或活动窗 ≤ 0 返回 0 |
| `DashDistance(speed, statusScale, dt, remaining)` | `min(speed × clamp(statusScale,0,4) × dt, remaining)` |
| `JumpInertiaDistance(speed, dt, remaining)` | `min(speed × dt, remaining)` |
| `ClampHorizontal(previous, next, allowed)` | XZ 位移按 `allowed` 等比例截断，Y 保留本次上报；`allowed` 非有限/负值时水平冻结在 `previous`（fail-closed） |
| `MaxJumpY(startY, jumpForce, gravity, elapsed)` | `startY + J·t + ½·G·t²`，`t` 钳到上升顶点 `J / −G`；参数非法时返回 `startY` |

共享目录（`ElementalCatalogDefinition`，作者数据 `Server/Config/elemental_catalog.json`）追加：

- `Displacements[]`：`SkillId / DashDistance / DashDuration / StartupSeconds / RecoverySeconds`。
  首版 20001 = 2m / 0.3s / 起手 0.05s / 收尾 0.05s；客户端 `ResolveDashDistance(skillId, assetFallback)`
  **共享表优先**，资产字段只在表里没有该技能时回退。
- `Jump`：`JumpForce = 10.95`、`Gravity = −30`、`MaxRiseSeconds = 0.5`（服务端 Y 轴抛物线上限、
  客户端跳跃惯性共用）。
- `Pvp.PlayerMaxHp = 720`（元素会话登录基线）。

`CombatRules.PveDamageBaselineHp = 100f`：`ScaleNpcDamageToPlayer(damage, target.MaxHp)`
= `damage × target.MaxHp / 100`（`MaxHp ≤ 0`/非有限时原样返回）。NPC → 720 HP 玩家 = ×7.2，
保持原 PvE TTK。Plan 04 的灼烧是玩家来源，每跳取状态表 `DamagePerTick`、不做 HP 基准缩放；
若将来新增 NPC 来源 DoT，每跳仍须复用同一函数。

验证：服务端 `MovementBudgetFormulaTests`（11 项）锁定上述公式与 fail-closed；
Unity `MovementBudgetMathTests`（5 项）锁定双端同源编译下的同一批断言。

## 权威状态运行时（Plan 04，2026-11-19）

`Elemental/StatusRuntime.cs` 是双端共享的纯 C# 状态机（零 `UnityEngine`），
服务端每个已配置状态运行时的权威实体（登录玩家/怪物）一份；Plan 04 只有服务端接线，
离线单机尚未接入。

| 类型 | 内容 |
|---|---|
| `StatusEffectKind` | `Slow=1` / `Burn=2` / `Paralyze=3` / `Vulnerable=4`（与 `StatusEffectDefinition.EffectKind` 同值） |
| `StatusApplyResult` | `Ignored` / `Applied` / `Refreshed` |
| `ActiveStatus` | `Definition`、`SourceId/SourceSkillId/SourceCastId`、`AppliedTick/EndTick/NextTick`、`PendingRemoval`、`IsPvpControl` |
| `StatusRuntime` | `Configure(PvpCombatSettings)`、`Apply(...)`、`Remove`、`RemoveExpiredModifiers`、`CollectDueDots`、`AdvanceDot`、`RemoveExpiredDots`、`DeriveControl`、`IncomingDamageMultiplier`、`ClearAll`、`SecondsToTicks` |

时序与规则（`CombatRules.TickRateHz = 15`，所有秒数 `SecondsToTicks` 向上取整并做
饱和加法）：

- 作用区间 `[ApplyTick, EndTick)`；灼烧按 `NextTick` 每间隔一跳，**允许在 `EndTick`
  结算已排定的最后一跳**，随后移除。同 tick 顺序固定为：解除到期非 DoT（风先失效）
  → 结算到期灼烧跳 → 处理输入/命中段；`RemoveExpiredDots` 兜底清理没有排定跳落在
  `EndTick` 的 DoT（刷新错相/间隔不整除），必须在 `CollectDueDots`/`AdvanceDot` 之后调用。
- 同 `StackGroupId` 最多一条、不同组可共存；重施刷新 `EndTick`，灼烧保留首次
  `SourceId`，下一跳取原下一跳与新间隔跳中较早者（连续命中不推迟伤害）；
  刷新会清 `PendingRemoval`。
- PvP 控制规则：仅“玩家目标 + 玩家来源”的 `Paralyze` 记为 `IsPvpControl`；
  移除时写入 `ControlImmunityUntilTick = tick + 0.5s`（免疫窗口内拒绝新麻痹）；
  玩家对玩家的 `Vulnerable` 在 4 秒窗口内不刷新（窗口跨状态移除保存）。
- `DeriveControl`：多个减速取最大 `SlowRatio`，`SpeedScale = clamp(1 − slow, 0, 1)`；
  任一 `Paralyze` 置 `ActionLock=true`。
- `IncomingDamageMultiplier(pvp, pvpMultiplier)`：PvE 使用状态表
  `IncomingDamageMultiplier`（首版风 = 2.0），PvP 使用共享 `Pvp.WindVulnerabilityMultiplier`
  （首版 1.28）；PvP 首版不放大灼烧（调用方按目标/来源选 `pvp` 并跳过灼烧）。
- `ClearAll` 清空状态、控制免疫与易伤窗口；调用点为 `MarkDead` / `TryRespawn` /
  `MonsterBrain.ArriveHome`（Evade 到家）与 `StatusTickService` 的死亡路径。

首版默认表（`ElementalCatalogDefaults` / `Server/Config/elemental_catalog.json`）：
冰 `101` 减速 30% / 2s；火 `201` 3s、每 1s 8 点；雷 `301` 0.8s；风 `401` 1.5s、
PvE ×2、PvP ×1.28。`ElementalCatalogDefaults.FindStatus/FindElement` 供两端查表。

验证：服务端与 Unity 各有一份 `StatusRuntimeTests`，锁定应用/刷新/末跳/控制派生与
PvP 免疫/不刷新窗口。

## 离线边界（Plan 05）

Plan 04 只把 `StatusRuntime` 接到**服务端权威实体**（`ServerStatusManager` +
`StatusTickService`）。离线单机仍使用 `Skills/Effect/StatusEffectManager` 的本地
冰冻/灼烧（仅怪物、本地计时、独立效果组）：四元素共享运行时、离线去重与元素 VFX
留给 Plan 05，不能把 Plan 04 的状态/DoT 文档理解为单机已打通。

## 约束

### NPC 共享目录与动作时间（2026-11-08）

`Npc/` 包含可由 Unity JsonUtility 序列化的 `[Serializable]` 公共字段 DTO：
`NpcCatalogDefinition`、模板、碰撞、AI、技能绑定/定义、表现定义；不依赖 Unity 或平台资源包。
`NpcCatalogValidator.Validate` 返回错误列表，拒绝非有限数值、非法引用/距离带、零长度阶段、
非递增 activeOffsets 及 >=2^31 的累计时间窗。`NpcTickMath` 提供环绕安全比较；tick0 合法。

`NpcCatalogHasher.Compute` 先校验，再将模板/技能/表现/绑定的副本按稳定 ID 排序，写入
版本化 little-endian UTF8 语义流并计算 SHA256 小写十六进制；不修改作者列表，排除 ContentHash，
不接收平台二进制、绝对路径或导出时间。浮点 +/-0 归一化；所有影响运行的 DTO 字段纳入 hash。

`NpcCatalogDefaults.Create()` 仅提供既有4模板/6技能的迁移作者种子，保留 Wolf 逻辑名与显式
placeholder，0.35s 前摇按15Hz向上取6 tick。碰撞尺寸/阶段映射为待校准初值，`Approved=false`；
不能将种子或校验通过解释为人工批准，不允许正式 ActionV1 启动缺产物时 fallback。
正式发布审批、平台 BuildHash/资源新鲜度由 Editor 发布门禁负责。

霸体为 `NpcWindupArmorOverride.Inherit/Interruptible/Armor`；模板默认在
`Ai.DefaultWindupArmor`，SpiderBoss 种子默认霸体，普通怪默认可打断。
NPC CombatRules 接线由服务端从共享发布定义注册；玩家规则及几何保持原有语义。

- 旋转 20002 的重复判定（`RepeatSpec`：首判 0.4s、间隔 0.2s、持续 3s、每目标 5 段）由服务端
  按 tick 逐段裁决；时间参数以客户端 `Spin_SkillR` 资产为准，EditMode `SpinAssetTableTests`
  锁定逐值一致。多段不能用 `GroupHitIds` 的“每施法一次”去重。
- 攻击高度（B1，2026-11-10）：`AttackHeightSpec` 定义相对施法者脚底的攻击体积；`CombatCandidate.Height`
  提供目标受击高度（0 = 未知 → 沿用纯 XZ）。第一批只配置地面近战（10001/10002 挥击、50001–50005），
  投射物/AOE 不传高度。服务端对非 NPC 实体的 Y 做 navmesh 地面限幅（见 `Server/Handler/CastHandler.cs`）。
- 不能引用 `UnityEngine`；两端同源编译，改规则必须两端测试同时通过。
- 非线程安全：`ClockSyncAlgorithm`、`PredictionReconciler` 由单消费者串行调用。
  复活落位时由客户端清空上一条命的预测帧。
- 判定几何改动要同步客户端 `AttackShape` 与服务端裁决的测试。

## 测试

`Assets/Scripts/Tests/Editor/SyncCore/`：`ClockSyncAlgorithmTests`、`MovementValidatorTests`、
`PredictionReconcilerTests`、`HitResolverTests`、`CombatRulesTests`、`MovementBudgetMathTests`、
`StatusRuntimeTests`。

```bash
bash tools/unity-tests.sh EditMode HitResolverTests
```

## 相关模块

- [SyncLayer](../SyncLayer/README.md) —— 客户端使用方
- [服务端](../../../../../Server/README.md) —— 服务端使用方
- [Combat](../Combat/README.md) —— 客户端命中检测
