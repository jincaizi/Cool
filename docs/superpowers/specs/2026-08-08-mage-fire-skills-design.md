# 法师型技能拓展设计（火系三技能）

- 日期：2026-08-08
- 状态：已批准（经两轮代码审核 + 资深 Unity 视角全审）
- 范围：给现有剑客角色扩展 3 个火系法师技能，替换 Q/R 槽位 + 启用大招位

## 1. 背景与现状

技能数据层已支持 6 种类型：`Combo` / `Instant` / `Charged`(蓄力) / `Channeled`(引导) / `Projectile`(投射物) / `Spin`，但实际只有近战资产（BA_Combo1 / BA_Heavy+剑气 / Instant_SkillQ 突刺 / Spin_SkillR 旋转斩）。蓄力、引导、投射物三种数据类从未被资产使用。

已验证的关键事实：
- Q/R 键映射为 `SkillID` 枚举（`Sys3CEntry.GetSkillQId() => (int)SkillID.SkillQ`），替换槽位 = 换注册资产引用，**零输入改动**
- `SkillInput.ChargingSkill()` 工厂已存在；`SkillStateMachine` 的 Charging 子状态机完整，但**"松手提前释放"是死代码**（`_isCharging` 只置 true 从不置 false）
- `ProjectileSkillData` 数据类字段齐全（prefab/speed/pierce/homing）但**没有任何运行时路径**（TryStart 直接进 Execution，无生成钩子）
- 剑气系统已跑通"投射物 + 碰撞 + 对象池"完整链路，可作为火球/陨石的模式模板
- 大招位 `SkillID.Ultimate = 30001` 已声明，但无 FSM 状态、无键位、无 Animator 状态
- `SkillSubState` 枚举经 `SkillNetworkSync` 线格式序列化，**禁止中部插入**（新增值只能追加末尾）——本批无新增子状态
- 无目标锁定链路（`TryActivateSkill` 中 TargetEntityId 为注释占位）——落点一律用"面向前方固定距离"
- 无法力系统（`HasEnoughResources`/`ConsumeResources` 为 TODO）——本批不实现资源扣减
- 无 HUD 技能槽组件——本批不做大招 HUD 显示
- `DamageBlock.CalculateFinalDamage = base + attribute × attackRatio`，`_scalingAttribute` 资产可配（`SpellPower=50` 属性已存在）——法师伤害零代码按 SpellPower 缩放

## 2. 设计目标与不做清单

### 目标
1. 启用 `ProjectileSkillData` 与 `ChargedSkillData` 两个从未用过的数据类
2. 验证一条完整的新机制：延迟落点 AOE（陨石）
3. 三个技能玩法差异明显：单体爆发 / 蓄力赌博 / 终极控场

### 不做（范围外）
- HUD 技能槽 UI（大招无显示，仅逻辑接入）
- 音效（项目无音频系统证据，不引入）
- 法力/资源扣减（ManaCost 字段保留，未来资源系统）
- 目标锁定（落点 = 面向前方固定距离）
- 新动画资源（复用现有剑系动画 + 粒子表达）
- 网络转发（事件仅承载快照，与 `SwordQiSpawnedEvent` 现状一致）

## 3. 三技能总览

### Q 键 → 火球术（替换原突刺）
- 类型：`ProjectileSkillData`，新资产 `Fireball_SkillQ.asset`，SkillId = `20001`
- 机制：施放动画（Attack03，Execution 起 `_spawnDelay=0.3s` 生成火球）→ 直线飞行 → 命中首个目标即爆（不穿透）→ 爆炸半径 1.5m 小范围伤害
- 数值：射程 15m、速度 18m/s、冷却 3s、伤害 = (基础 + SpellPower×1.0)（爆炸目标 0.5 倍率）
- 空中可用（沿袭原 Q 槽位）
- 原突刺资产 `Instant_SkillQ.asset` 保留不删

### R 键 → 蓄力炎爆（替换原旋转斩）
- 类型：`ChargedSkillData`，新资产 `ChargeFire_SkillR.asset`，SkillId = `20002`
- 机制：按住 R 蓄力（0.3–2s），**可移动可转向**（现有代码已支持，零改动）→ **松开任意时刻释放**（见 4.1 语义）→ 中心爆炸
- 蓄力缩放：伤害 × `ChargeDamageCurve`（0→1.5x，释放瞬间按实际进度结算）、AOE 半径 × `ChargeAreaCurve`（2m→3m）
- 数值：冷却 6s（按下即计，现有行为）；轻点 = 快速弱火，长按 = 满蓄力强爆
- 仅地面（沿袭原 R 槽位）
- 原旋转斩资产 `Spin_SkillR.asset` 保留

### 大招 → 陨石术（新输入位 U 键）
- 类型：InstantSkillData + `MeteorConfig`（仿 `SwordQiConfig` 挂法），新资产 `Meteor_Ultimate.asset`，SkillId = `30001`
- 机制：施放动画（Attack03）→ 生成预警红圈（落点 = 施放时面前 12m，**定格**）→ 0.8s 后陨石落地爆炸
- 数值：爆炸半径 4m（检测中心在落点 y+1m 脚踝高度）、伤害 = (基础 + SpellPower×2.5)、冷却 25s
- 仅地面；预警期目标可走出红圈躲开（玩法）
- 移动端无 U 键：仅 Editor/PC 生效，接口沿用 `SetDefendHeld` UI 按钮模式预留

## 4. 运行时改造

### 4.1 蓄力释放链路（修复死代码 + 定义语义）
- `SkillStateMachine` 新增 `Release()`：`_currentState == Charging` 时置 `_isCharging = false` 并转 Execution
- **语义修正**：Release() 任意时刻有效（含低于 MinChargeTime）——轻点立即释放、伤害按实际进度缩放；MinChargeTime 降级为资产参考值（不再拦截）
- `SkillExecutor` 新增 `Release()` 转发
- `SkillCoordinator` 新增 `TryReleaseCharge(int skillId)`：仅当当前技能是该 id 且处于 Charging
- `InputManager` 新增 `IsSkill3Held()`（GetKey R）+ 边缘触发 `IsSkill3Pressed()` 照旧
- `Sys3CEntry` R 分支：GetKeyDown → 照旧激活；GetKeyUp 且当前技能为该 id 且蓄力中 → `TryReleaseCharge`

### 4.2 蓄力结算修复（顺带修 2 个现有 bug）
- **修复 1（爆炸打 1.0x）**：`OnStateChanged` 的 Execution 分支缓存 `_chargeProgressAtRelease`（Charging 转 Execution 瞬间的进度，满蓄力 = 1）；`ApplyDamage` 中蓄力加成改为 `damage *= GetDamageScaleForCharge(_chargeProgressAtRelease)`（替代硬编码 `1 + progress×0.5`，且 Execution 态不再取到 0）
- **修复 2（死字段）**：同处计算 `_wasFullCharge = progress >= 1 - epsilon` 并写入，`SkillHitTargetEvent.IsFullCharge` 恢复有效
- **半径缩放**：`DetectAOETargets` 中 `_skillData is ChargedSkillData` 且 Execution 时 `radius = shape.AreaRadius × GetAreaScaleForCharge(progress)`
- **打断补发**：`OnSkillInterrupt` 对 ChargedSkillData 同样补发 `SkillReleasedEvent`（修复武器特效卡发光态）

### 4.3 火球投射物（复用剑气链路模式）
- **`ProjectileSkillData` 数据类扩展**（首个真实用户的最小集）：
  - `_spawnDelay`（默认 0.3s，Execution 计时到点生成，解决"球先于动画飞出"错位）
  - `_spawnOffset`（默认 (0, 1.2, 0.5)，胸前发射）
  - `_targetMask`（命中层掩码——数据类目前无层掩码来源）
  - `_explosionRadius`（命中爆炸半径，默认 1.5，0 = 仅单体）
  - `OnValidate { _skillType = Projectile }`（与 Combo/Charged/Channeled 一致）
- **生成钩子**：`SkillExecutor` 在 Execution 内按 `_spawnDelay` 计时，到点调用 `FireballSpawner.Spawn(...)`（伤害经 `Damage.Clone()` + `SkillDamageHelper.FillDamageContext` 填充，防共享实例污染，同剑气）。生成不依赖 `EnableVFX` 标志（gameplay 与表现分离）
- **`FireballProjectile`**（新文件，仿 `SwordQiProjectile`）：直线飞行 → 每帧私有静态 `OverlapSphereNonAlloc` 检测（**不复用** SkillExecutor 的共享静态缓冲）→ 命中首个目标 → 1.5m 爆炸 AOE（`IDamageable.TakeDamage`）→ 超射程/命中后池化回收
- **`FireballSpawner`**（新文件，仿 `SwordQiSpawner`）：懒加载对象池 + GameRoot 容器 + `DisposeForTests`
- **方向**：生成时用 `_owner.transform.forward`（`SkillToPosition` 的 InputDirection 为 zero，同剑气处理）；速度/射程取数据类字段
- **互斥注记**：资产中 `SwordQi` 字段保持为空（否则剑气火球双发）
- **网络**：`FireballSpawnedEvent`（仿 `SwordQiSpawnedEvent`，仅快照，无订阅者）

### 4.4 陨石（仿剑气配置模式）
- `SkillData` 基类新增 `_meteor` 字段（`MeteorConfig`，null = 禁用）——与 `_swordQi` 同款挂法，不新增 SkillData 子类
- **`MeteorConfig`**（新 SO）：预警圈 prefab、预警时长（0.8s）、爆炸半径（4m）、爆炸特效 prefab、伤害倍率、`_targetMask`
- **`MeteorStrike`**（新组件）：生成预警圈（地面 decal 粒子，落点定格于施放时面前 12m）→ **Update 时间累加计时（禁用协程，池化对象防泄漏）** → 0.8s 后爆炸（OverlapSphereNonAlloc，中心在落点 y+1m）→ 私有静态碰撞缓冲 → 池回收
- **`MeteorSpawner`**（新文件，仿 SwordQiSpawner 全套：懒加载/容器/DisposeForTests）
- **生成时机**：`SkillExecutor` 在 Execution 进入时**立即生成预警圈**（0.8s 预警本身就是延迟，无需 spawnDelay）——与 4.3 火球共用同一 Execution 计时钩子
- **网络**：`MeteorSpawnedEvent`（仅快照，无订阅者）

### 4.5 输入 / FSM / Animator
- `InputManager` 新增 `IsSkillUltimatePressed()`（U 键，consumed 模式仿 Q）
- `Sys3CEntry` 新增 U 分支 → `SkillToPosition(30001, transform.position + forward × 12f)`
- Animator Attack 层（Character3C.controller）：
  - 新 Trigger 参数 `ChargeBlast`（蓄力炎爆）——**不沿用 `SkillR` trigger**（该 trigger 已绑定 SkillR_start/loop 旋转动画，复用会播旋转斩）
  - 新 Trigger 参数 `Ultimate`（陨石）
  - 新状态 `SkillR_charge`：motion = **`Idle_Battle_SwordAndShield`**（自然循环站姿，**不循环挥砍**），由 ChargeBlast 进入，AttackState==0 返回 AttackIdle
  - 新状态 `Ult`：motion = Attack03，由 Ultimate 进入，AttackState==0 返回 AttackIdle
- `AnimHashes.cs` 新增 `ChargeBlast` / `Ultimate` 常量
- 蓄力循环动画的完成回调会被 `IsAnimationCompletionIgnored`（覆盖 Charging）正确吞掉，不会误清技能（已验证）
- AttackFSM 空壳状态不动（动画驱动全在 Animator + Sys3CEntry 回调）
- 移动端：`SetDefendHeld` UI 按钮模式预留（本批不实现）

### 4.6 文件清单
**新增**：
- `Skills/Runtime/Projectile/FireballProjectile.cs`、`FireballSpawner.cs`
- `Skills/Runtime/Meteor/MeteorStrike.cs`、`MeteorSpawner.cs`
- `Skills/Data/MeteorConfig.cs`
- `Skills/Events/FireballSpawnedEvent.cs`、`MeteorSpawnedEvent.cs`

**修改**：
- `Skills/Data/SkillData.cs`（+Meteor hook）
- `Skills/Data/ProjectileSkillData.cs`（+4 字段 + OnValidate）
- `Skills/Runtime/SkillStateMachine.cs`（+Release）
- `Skills/Runtime/SkillExecutor.cs`（投射物生成钩子、蓄力倍率/满蓄力修复、打断补发、Release 转发）
- `Skills/Runtime/SkillCoordinator.cs`（+TryReleaseCharge）
- `Sys3C/Input/InputManager.cs`（+IsSkill3Held / IsSkillUltimatePressed）
- `Sys3C/Sys3CEntry.cs`（R 按住/松开、U 分支）
- `Sys3C/Animation/AnimHashes.cs`（+2 常量）
- `RpgDuo/Animator/Character3C.controller`（+2 参数 +2 状态 +过渡）

**新资产**：`Fireball_SkillQ.asset`、`ChargeFire_SkillR.asset`、`Meteor_Ultimate.asset`、`MeteorConfig`、火球 prefab、预警圈 prefab、陨石爆炸 prefab（粒子）

## 5. 表现层

| 技能 | 视觉 |
|---|---|
| 火球 | 飞行火球（球体粒子 + 拖尾）→ 命中爆炸（火焰冲击环 + 火花） |
| 蓄力炎爆 | 蓄力时武器发光（**免费**——WeaponVFXController 已按 SkillId=20002 监听蓄力开始/结束事件）→ 释放中心大爆炸 |
| 陨石 | 落点预警红圈（地面 decal 粒子，0.8s 闪动）→ 落地大范围火焰冲击 |

- 投射物走 `ComponentPool`；一次性爆炸特效直接实例化 + `OnParticleSystemStopped` 自毁（频率低，不过度设计）
- 全部使用现有粒子管线，无新材质/后处理

## 6. 边界情况

- 蓄力中受击 → 打断 + `SkillReleasedEvent` 补发（VFX 熄灭，修复项）
- 蓄力中按其他技能 → `CanChainSkill` Charging=false → 入缓冲，释放后自动执行（现有行为）
- 蓄力中移动/转向 → 现有 `CanMove/CanRotate` 已按 `CanMoveWhileCharging` 处理，零改动
- 火球命中已死目标 → 跳过（剑气同款 `!IsAlive` 守卫）
- 陨石预警期目标走开 → 砸原落点（可躲开 = 玩法）
- 空中限制：Q 空中可用、R/U 仅地面（沿袭原槽位）

## 7. 测试验收（EditMode，仿现有测试风格）

1. **火球**：`FireballProjectile` 直线飞行 / 命中爆炸 / 超射程回收；生成钩子按 `_spawnDelay` 只触发一次；技能完成后投射物继续独立飞行；生成不依赖 `EnableVFX`
2. **蓄力**：`Release()` 任意时刻释放（含 < MinChargeTime）；满蓄力自动释放；打断补发 `SkillReleasedEvent`；伤害按曲线按实际进度结算；半径按曲线缩放；`_wasFullCharge` 写入正确
3. **陨石**：延迟 0.8s 落地、落点正确（面前 12m 定格）、爆炸中心在脚踝高度、池回收
4. **回归**：现有测试全绿；技能替换后 Q/R 动画 trigger、冷却、`IsAnimationCompletionIgnored` 行为正常

可测性说明：`SkillStateMachine.GetCurrentTime()` 为 `protected virtual`，测试可子类重写假时钟，无需改生产代码。

## 8. 审核记录（两轮代码审核 + 专家全审的修正汇总）

| # | 修正 |
|---|---|
| 1 | 蓄力"松手释放"死代码 → 新增 Release() 接口链 |
| 2 | 蓄力爆炸打 1.0x bug → 缓存释放进度按曲线结算 |
| 3 | `_wasFullCharge` 死字段 → 释放时写入 |
| 4 | 蓄力打断不补发 SkillReleasedEvent（VFX 卡发光）→ 补发 |
| 5 | ProjectileSkillData 无运行时钩子 → Execution 计时生成 + 4 字段 |
| 6 | 网络事件"无订阅者" → 与 SwordQiSpawnedEvent 现状一致，不新增转发 |
| 7 | 无法力系统 → 删除耗蓝，ManaCost 保留 |
| 8 | R 槽 trigger 冲突 → 新参数 ChargeBlast（避免播旋转动画） |
| 9 | 蓄力动画循环挥砍错误 → Idle_Battle 站姿 |
| 10 | 轻点蓄力 UX 死路 → Release 任意时刻释放、按进度缩放 |
| 11 | 蓄力移动 → 验证现有代码已支持，零改动 |
| 12 | 共享静态碰撞缓冲并发隐患 → 新代码私有缓冲 |
| 13 | 池化对象协程泄漏 → Update 计时 |
| 14 | 陨石爆炸检测高度 → 落点 y+1m |
| 15 | 伤害按 SpellPower 缩放 → DamageBlock 资产配置，零代码 |
