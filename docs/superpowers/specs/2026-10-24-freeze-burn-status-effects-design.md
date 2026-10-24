# 冰冻 / 灼烧状态效果设计

日期：2026-10-24
状态：待实现
方案：全局状态管理器（StatusEffectManager）

## 1. 目标

玩家技能可以对怪物施加两种状态效果，并带可辨认的占位视觉：

- **冰冻**：完全定身，不能移动、不能攻击、不能放技能，行为等价于 Stun。
- **灼烧**：按施法者攻击力计算的持续伤害（DoT），每个 tick 结算一次。
- 占位视觉：冰冻蓝白染色 + 动作冻结；灼烧橙红脉冲染色 + 每跳伤害飘字。
- 不引入任何新美术资源，不依赖正式粒子/Shader/贴花。

## 2. 非目标

- 玩家作为效果目标（玩家尚未实现 IEffectTarget 作为被施法方）。
- 驱散、免疫、抗性、减速、破冰、叠层传播等扩展规则。
- 正式美术表现（冰晶、火焰粒子、贴花、材质 Shader）。
- 状态同步到远端客户端；本次只在施加者本地表现。
- 投射物路径（FireballProjectile）施加效果；本次不接火球。
- 怪物对玩家施加状态（怪物攻击仍只结算伤害）。

## 3. 现状与必须修正的问题

### 3.1 多态效果无法序列化（阻塞级）

`EffectBlock._applyEffects` 是 `EffectData[]`，没有 `[SerializeReference]`。Unity 只能按声明类型 `EffectData` 序列化，派生类字段会丢失。

证据：`Assets/Monstor/TurtleShellConfig.asset:64` 的条目只有基类字段，`_type: 8`（Stun）但缺少 `_canBeCleanse`，加载后实际是基类 `EffectData`，`Apply()` 为空实现，从未生效。

修正：给 `_applyEffects` 增加 `[SerializeReference]`。全项目只有这一条非空数据且本来就是无效配置，迁移时清空；以后需要怪物施加眩晕时再重新配置。

### 3.2 IEffectTarget 实现是空壳

- `MonsterEntity` 的 `Stats` / `StatusController` / `ShieldSystem` / `PhysicsSystem` 全部返回 `null`（`MonsterEntity.cs:342-345`）。
- `SkillOwnerProxy` 同样全部返回 `null`（`Sys3CEntry.cs:952-963`）。
- 因此任何依赖这些接口的效果都会空引用或静默失效。

本次只补齐冰冻/灼烧实际需要的部分：

| 目标 | 补齐项 |
|---|---|
| MonsterEntity | `StatusController`（StatusControllerAdapter 实例）、`ApplyDamageOverTime` |
| SkillOwnerProxy | `Stats`、`StatusController`、`PhysicsSystem`、`Heal` |

MonsterEntity 的 `Stats` / `ShieldSystem` / `PhysicsSystem` 本次仍保持 `null`；配置 Buff/Shield/Knockback 到怪物仍不可用，属于已知限制。

### 3.3 持续效果没有运行时

`EffectData.OnTick` 全项目没有调用方；`SkillExecutor.ApplyEffects` 只调用一次 `Apply`，没有持续时间管理。冰冻（持续状态）和灼烧（周期伤害）都必须补一个运行时。

### 3.4 SkillFreezeEffector 不能直接删除

`SkillFreezeEffector` 挂在 `FemalePlayer.prefab` 与 `MaleCharacterPBR.prefab` 上，删除脚本会产生 Missing Script。它做的是"命中点 2m 半径内冻结一个目标"，不是"技能命中目标冻结"。

修正：保留组件，内部改走 `StatusEffectManager.Apply`；保留现有 `break`（只冻结半径内第一个目标）与 2m 半径，避免改变既有行为。Manager 缺失时跳过并打一次错误日志。

### 3.5 冻结期间死亡会卡住死亡动画

冻结时设置 `Animator.speed = 0`。若此时死亡，`HandleDeath` 会强制进入死亡状态，但动画停在原地。必须在 `HandleDeath` 中恢复 `Animator.speed` 并清除状态。

## 4. 架构

### 4.1 StatusEffectRegistry（纯 C#，可单测）

状态运行时的唯一事实来源，不依赖 MonoBehaviour 生命周期。

```csharp
public sealed class StatusEffectRegistry
{
    public void Apply(IEffectTarget target, EffectData effect, IEffectTarget caster);
    public void Tick(float deltaTime);
    public void Remove(IEffectTarget target, string effectId);
    public void RemoveAll(IEffectTarget target);
}
```

内部条目：

```csharp
private sealed class ActiveEffect
{
    public IEffectTarget Target;
    public GameObject TargetGo;
    public IEffectTarget Caster;
    public GameObject CasterGo;
    public EffectData Effect;
    public float Remaining;
    public float NextTick;
    public int Stacks;
}
```

关键规则：

- **运行时状态只存在 Registry**。`EffectData` 挂在 `SkillData`（ScriptableObject）上，是跨目标共享的配置，禁止在 `EffectData` 上保存每目标快照。
- 唯一键 = `(TargetGo.GetInstanceID(), effectKey)`；`effectKey` 优先用 `Effect.EffectId`，为空时用 `RuntimeHelpers.GetHashCode(Effect)`。
- `Apply` 新建条目时立即调用一次 `effect.Apply(caster, target)`，并广播 `StatusEffectAppliedEvent`。
- `Tick` 倒序遍历：
  1. `TargetGo == null` → 静默移除，不调用 `Remove`。
  2. `target is IDamageable d && !d.IsAlive` → 调用 `effect.Remove`、广播移除事件、移除条目。
  3. `Remaining -= deltaTime`；`IsTickEffect` 到点执行 `effect.OnTick(caster, target, stacks)`，`NextTick += TickInterval`（`TickInterval <= 0` 视为不 tick）。
  4. `Remaining <= 0` → 调用 `effect.Remove`、广播移除事件、移除条目。
- 施法者销毁时，后续 tick 传 `null` 作为 caster，由效果自行兜底。
- `RemoveAll` 用于死亡/驱散，逐条 `Remove` 并广播事件。

### 4.2 StatusEffectManager（MonoBehaviour 单例）

- 挂到 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 的现有管理节点（与 `HitFeedbackSystem` 同处），`DontDestroyOnLoad`。
- `Awake` 中做重复实例保护：已有 `Instance` 时销毁自身。
- `Update` 只调用 `_registry.Tick(Time.deltaTime)`。
- 对外 API：`Apply` / `Remove` / `RemoveAll`，转发给 Registry。
- **场景漏挂时**：`SkillExecutor` 检测 `Instance == null`，打一次错误日志并**跳过带持续时间的效果**（不执行一次性 Apply，避免冻结永久不解除）；瞬时效果不受影响。

### 4.3 EffectData 扩展

- `EffectType` 增加 `Freeze`、`Burn`。
- `EffectData.OnTick` 签名改为：

```csharp
public virtual void OnTick(IEffectTarget caster, IEffectTarget target, int stacks) { }
```

当前全项目没有重写，改动安全。

- `FreezeEffectData : StunEffectData`
  - 构造函数：`_type = EffectType.Freeze`，`_effectId = "freeze"`（可在资产中覆盖）。
  - Apply / Remove 完全复用父类。
  - `StunEffectData` 的 `Apply` 与 `Remove` 都增加 `target.StatusController == null` 保护。
- `BurnEffectData : EffectData`
  - 构造函数：`_type = EffectType.Burn`、`_isTickEffect = true`、`_tickInterval = 1f`、`_effectId = "burn"`。
  - 字段：`_attackPowerRatio`（默认 0.2）、`_baseDamage`（默认 5）。
  - `OnTick`：
    - 施法者可用且 `Stats != null`：每跳伤害 = `caster.Stats.GetAttribute(AttackPower) * _attackPowerRatio * stacks`；
    - 否则：`_baseDamage * stacks`。
    - 调用 `target.ApplyDamageOverTime(damage)`。
  - 提供 `Initialize(float ratio, float baseDamage, float duration, float interval)` 供代码构造（单测与程序化施加）。
- `IEffectTarget` 增加：

```csharp
void ApplyDamageOverTime(float amount);
```

### 4.4 技能路由

`SkillExecutor.ApplyEffects` 改为：

```csharp
foreach (var effectData in effect.ApplyEffects)
{
    if (effectData == null) continue;

    if (effectData.Duration > 0f)
    {
        if (StatusEffectManager.Instance != null)
            StatusEffectManager.Instance.Apply(target, effectData, _owner);
        else
            WarnOnceMissingManager();
    }
    else
    {
        effectData.Apply(_owner, target);
    }
}
```

只按 `Duration > 0` 判定进入状态运行时；`IsTickEffect` 但 duration=0 的效果仍走一次性 Apply。

### 4.5 施法者接线

`SkillOwnerProxy` 构造函数扩展，`Sys3CEntry.cs:157` 传入 `_cc` 的适配器：

| 接口 | 实现 |
|---|---|
| `Stats` | `_cc.StatsAdapter` |
| `StatusController` | `_cc.StatusControllerAdapter` |
| `PhysicsSystem` | `_cc.PhysicsSystemAdapter` |
| `Heal(float)` | `Sys3CEntry.Heal(amount)`（写 `_currentHP`，与 HUD/IsAlive 一致） |
| `ApplyDamageOverTime(float)` | 空实现（玩家本次不作为目标） |

### 4.6 怪物接线

`MonsterEntity`：

- 新增 `private readonly StatusControllerAdapter _statusController = new();`
- `IEffectTarget.StatusController => _statusController;`
- `Update`：

```csharp
if (_stats == null || _stats.IsDead || _brain == null) return;

bool stunned = _statusController.IsStunned;
if (stunned != _wasStunned)
{
    if (stunned) { Animator.speed = 0f; _movement.Stop(); }
    else          { Animator.speed = 1f; }
    _wasStunned = stunned;
}

if (!stunned)
    _brain.Update(Time.deltaTime);
```

- 冻结期间：
  - 跳过 `_brain.OnDamageReceived`（不播放受击反应）；
  - `OnKnockback` 直接返回（不被击退）。
- 怪物的 `StatusControllerAdapter` **不由 Update 驱动**；冻结时长完全由 Registry 管理，Registry 移除效果时调用 `RemoveStun`。
- `HandleDeath`：
  - `Animator.speed = 1f;`
  - `_wasStunned = false;`
  - **不调用 `RemoveAll`**：死亡可能在 Registry.Tick 的 `OnTick` 内部触发，`RemoveAll` 会在遍历中修改列表。状态由 Registry 在下一帧检测 `IsAlive == false` 后清理。
- `ApplyDamageOverTime(float amount)`：

```csharp
if (_stats == null || _stats.IsDead || amount <= 0f) return;

_stats.ApplyDamage(amount);          // 纯扣血，绕过防御
EventBus.Emit(new MonsterDotDamageEvent(
    GetInstanceID(),
    transform.position + Vector3.up * 1.2f,
    Mathf.CeilToInt(amount)));
```

不使用 `MonsterTakeDamageEvent`，因此不会触发顿帧、震屏、命中粒子与受击闪红。

### 4.7 灼烧飘字

`DisplayEventBridge` 增加订阅：

```csharp
EventBus.Subscribe<MonsterDotDamageEvent>(OnMonsterDotDamaged);

private void OnMonsterDotDamaged(MonsterDotDamageEvent e)
{
    _floatText.ShowDamageText(e.EntityId, e.HitPosition, _damageSettings, e.Damage);
}
```

复用现有红字配置，不新增 Inspector 字段。

### 4.8 占位视觉：StatusVFXSystem（MonoBehaviour）

与 `StatusEffectManager` 挂在同一管理节点。

- 订阅 `StatusEffectAppliedEvent` / `StatusEffectRemovedEvent`。
- 每个目标维护：

```csharp
private sealed class TargetTint
{
    public BodyTintRenderer Tint;
    public int FreezeCount;
    public int BurnCount;
    public float Phase;
}
```

- Applied：计数 +1；首次出现时用 `target.GetComponentsInChildren<Renderer>()` 构建 `BodyTintRenderer`。
- Removed：计数 -1；两个计数都为 0 时 `Tint.Clear()` 并移除条目。
- 每帧 `Update` 重算染色：
  - `FreezeCount > 0`：`(0.55, 0.75, 1.0)`，强度 0.5；
  - 否则 `BurnCount > 0`：`(1.0, 0.35, 0.1)`，强度 `0.12 + 0.18 * (0.5 + 0.5 * sin(Phase))`，Phase 以 2Hz 推进；
  - 否则清除。
- 目标 Transform 为 null 或被销毁时清理条目；移除事件到达时目标可能已销毁，`Clear()` 前必须判空。
- `BodyTintRenderer` 只处理拥有 `_Color` 属性的材质；其他材质跳过，不报错。

## 5. 数据流

```text
技能命中
  → SkillExecutor.ApplyEffects
    → StatusEffectManager.Apply
      → effect.Apply
         Freeze: target.StatusController.AddStun(duration)
         Burn:   无
      → EventBus.Emit(StatusEffectAppliedEvent)
        → StatusVFXSystem：开始染色
  → 每帧 StatusEffectRegistry.Tick
    → Burn.OnTick → MonsterEntity.ApplyDamageOverTime
      → MonsterStats.ApplyDamage（绕过防御）
      → EventBus.Emit(MonsterDotDamageEvent)
        → DisplayEventBridge：红字飘字
    → 到期 → effect.Remove → StatusEffectRemovedEvent → 撤销染色
```

## 6. 状态规则

| 场景 | 行为 |
|---|---|
| 重复施加 + `Refresh`（默认） | 重置剩余时间，**不重复 Apply**（避免治疗/伤害重复结算），**保留 tick 相位**（不重置计时器；否则高频刷新会让 DoT 永不跳） |
| 重复施加 + `Stack` | 层数 +1（上限 `MaxStacks`），重置剩余时间；灼烧每跳伤害 × 层数 |
| 重复施加 + `Ignore` | 已存在时直接忽略 |
| 多个控制类效果 | 只有最后一个控制类效果移除时才 `RemoveStun`；否则用最长剩余时间重新 `AddStun` |
| 目标死亡/销毁 | 下一帧清理；销毁时不调用 `Remove` |
| 施法者死亡/销毁 | 后续 tick 传 `null` caster，灼烧回退 `_baseDamage` |
| 冻结期间受击 | 正常扣血，但不触发受击反应/击退 |
| 冻结期间死亡 | 恢复 `Animator.speed`、清除全部状态、正常播放死亡 |

控制类效果判定：`effect is StunEffectData`（`FreezeEffectData` 是其子类）。

## 7. 序列化迁移

1. 给 `EffectBlock._applyEffects` 增加 `[SerializeReference]`。
2. 清空 `Assets/Monstor/TurtleShellConfig.asset` 中无效的 `_applyEffects` 条目。
3. 重新打开相关 SkillData 资产确认数组为空、无丢失引用。

## 8. 测试与验收

### 8.1 EditMode 单测（StatusEffectRegistryTests）

1. 持续效果到期调用且只调用一次 `Remove`。
2. 灼烧 5s / 1s 间隔 → 恰好 5 次 `OnTick`。
3. 施法者 AttackPower=100、ratio=0.2 → 每跳 20 伤害（stacks=1）。
4. `Refresh` 重置时间且不重复 `Apply`；`Ignore` 不新增；`Stack` 层数递增且灼烧伤害 × 层数。
5. 目标 `IsAlive == false` 后停止 tick 并清理。
6. 短冰冻到期不解除长冰冻（两个 `StunEffectData`）。
7. `StackingRule.Refresh` 多次施加只产生一个条目。
8. 真实帧长（0.016s）下 3s/1s → 恰好 3 次 `OnTick`。
9. `Refresh` 保留 tick 相位：0.5s 时刷新，0.6s 后仍应 tick 一次。
10. 灼烧 3s/1s + AttackPower 100 → 累计 60 伤害。

测试替身：实现 `IEffectTarget` + `IDamageable` 的测试类，使用真实 `GameObject` 提供 Transform；用计数型 `EffectData` 子类统计 Apply/OnTick/Remove。

### 8.2 验收判据（可断言）

- 冰冻 2s：施加后 1 帧内 NavMeshAgent 速度为 0；1.9s 时仍未恢复；2.1s 后恢复。
- 灼烧 3s / 1s / AttackPower 100 / ratio 0.2：怪物恰好掉 60 HP。
- 灼烧期间不触发顿帧与震屏（结构上由独立事件保证）。
- 视觉手动检查：怪物蓝白静止 2s；灼烧橙红脉冲 + 每秒红字。

## 9. 改动清单

### 新增

- `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs`
- `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectManager.cs`
- `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/BurnEffectData.cs`
- `Assets/Scripts/Hotfix/GameSystems/Skills/Events/StatusEffectEvents.cs`
- `Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs`
- `Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs`

### 修改

- `Skills/Data/EffectBlock.cs`：`_applyEffects` 加 `[SerializeReference]`
- `Skills/Effect/EffectData.cs`：`EffectType`、`OnTick` 签名、`IEffectTarget.ApplyDamageOverTime`、`FreezeEffectData`、Stun 空引用保护
- `Skills/Runtime/SkillExecutor.cs`：效果路由
- `Sys3C/Sys3CEntry.cs`：`SkillOwnerProxy` 接线
- `Monster/MonsterEntity.cs`：StatusController、定身门控、`ApplyDamageOverTime`、死亡清理
- `Sys3C/Core/Events/DamageEvents.cs`：`MonsterDotDamageEvent`
- `Nameplate/DisplayEventBridge.cs`：DoT 飘字订阅
- `VFX/SkillFreezeEffector.cs`：改走 `StatusEffectManager`，保留 2m/单目标行为
- `Assets/Monstor/TurtleShellConfig.asset`：清空无效效果条目
- `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`：挂 `StatusEffectManager` 与 `StatusVFXSystem`

### 不改

- 怪物预制体
- 玩家预制体（`SkillFreezeEffector` 保留）
- 投射物路径（火球不会触发灼烧）
- 现有技能资产（冰冻/灼烧通过 `EffectBlock.ApplyEffects` 配置，挂到哪个技能由使用者决定）

## 10. 风险与已知限制

- 染色只对拥有 `_Color` 属性的材质生效；内置 Standard 已确认可用，个别怪物材质若不支持则跳过。
- `StatusVFXSystem` 与 `HitFlashVFX` 共用 `_Color` 的 `MaterialPropertyBlock`；HitFlashVFX 默认关闭，若启用，状态染色会压制受击闪红。
- `SkillFreezeEffector` 保持"2m 内只冻结第一个目标"；如需全体冻结，去掉循环里的 `break` 即可（一行）。
- 灼烧伤害绕过防御与护盾；这是本次明确的简化。
- 怪物 `Stats` / `ShieldSystem` / `PhysicsSystem` 仍为 `null`，Buff/Shield/Knockback 不能作用于怪物。
- 状态不同步到远端客户端。
- 投射物技能（火球）不触发效果。
- `EffectId` 默认相同（`"freeze"` / `"burn"`）时，同类效果会互相刷新而不是并存；如需多套独立数值，在资产里配置不同 `EffectId`。
- 灼烧需要手动在技能资产上配置 `BurnEffectData`；本次未改现有技能资产，PlayMode 验收时需临时添加。
