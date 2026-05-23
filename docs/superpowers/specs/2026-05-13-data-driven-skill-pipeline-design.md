# Data-Driven Skill Pipeline Design

> **状态:** 设计完成，待实现
> **创建时间:** 2026-05-13
> **依赖:** 当前 SkillData 子类架构 (2026-05-09-skill-data-refactor-design.md)
> **前置条件:** 技能系统合并已完成 (2026-05-08-skill-system-merge-design.md)

---

## 1. Motivation

### 1.1 当前问题

当前技能系统通过 **SkillData 子类 + SkillStateMachine 类型分发** 实现不同技能类型：

```csharp
// SkillStateMachine.TryStart() — 按类型分支
if (_isCharged)       → Casting → Charging → Execution → Recovery
else if (_isChanneled) → Casting → Channeling → Execution → Recovery
else                   → Execution → Recovery
```

每新增一种技能类型，需要：
1. 新建 SkillData 子类
2. 在 SkillStateMachine 中新增分支逻辑
3. 在 SkillExecutor 的类型转换链中新增判断
4. 在 SkillCoordinator 的 CanChainSkill/CanMove/CanRotate 中新增分支
5. 在 SkillInterruptionMatrix 中新增默认规则

这就是 graphify 揭示的 **God Node + 碎片化** 根源：同一个技能概念（"我的执行流程是什么"）分散在 5 个文件中。

### 1.2 核心洞察

技能之间的差异是 **时间轴配置** 的差异，不是 **代码流程** 的差异。

- 蓄力 = 有一个阶段等待玩家松手，有 min/max 时间约束
- 引导 = 有一个长持续阶段，周期性触发判定帧
- 连段 = Recovery 阶段可以立即跳到下一个技能
- 瞬发 = 只有一个 Active + Recovery，没有前置阶段

所有技能走同一条管线。阶段数量、时长、完成条件、移动策略、打断规则全部数据化。

---

## 2. Data Model

### 2.1 SkillPhase

一个技能由若干个阶段（Phase）组成，按顺序执行：

```
[Windup] → [Active/Charge/Channel] → [Recovery]
```

每个 Phase 是自描述的配置块：

```csharp
[System.Serializable]
public class SkillPhase
{
    [Header("=== Timing ===")]
    public string phaseName;              // 调试标识
    public float baseDuration;            // 基础时长（秒，0 = 即刻完成）
    public float minDuration;             // 最短停留（蓄力最低时间）
    public float maxDuration;             // 最长停留（蓄力自动释放时间）

    [Header("=== Completion ===")]
    public PhaseCompletion completion;    // 阶段如何结束

    [Header("=== Hit Detection ===")]
    public HitboxFrame[] hitboxes;        // 此阶段的判定帧列表

    [Header("=== Movement ===")]
    public MovementRule movement;         // 移动权限

    [Header("=== Interruption ===")]
    public CancelFlags cancelFlags;       // 可被什么来源打断

    [Header("=== Actions ===")]
    public bool fireProjectileOnEnter;    // 进入阶段时发射投射物
    public string animTrigger;            // 进入阶段时触发的动画参数
    public float damageMultiplier;        // 此阶段伤害倍率
}
```

### 2.2 PhaseCompletion

```
Duration      → 时间到自动前进
ManualRelease → 等玩家松手（蓄力），受 minDuration/maxDuration 约束
HoldInput     → 按住持续，松手结束（引导的简化版）
Animation     → 等 Animator 回调（用于需要与动画精确同步的阶段）
```

### 2.3 HitboxFrame

判定帧不再隐藏在各子类的 castTime + hitboxTimings 算术中：

```csharp
[System.Serializable]
public struct HitboxFrame
{
    public float triggerTime;       // 从阶段开始的秒数
    public float damageMultiplier;  // 此帧伤害倍率
    public float rangeScale;        // 此帧范围缩放
}
```

### 2.4 MovementRule

```
Free        → 可自由移动
SlowWalk    → 可慢走（蓄力中）
RotateOnly  → 只能转向
Locked      → 完全锁定
```

### 2.5 CancelFlags

```
None           → 不可取消
Movement       → 移动输入取消
BasicAttack    → 普攻取消（连段）
AnySkill       → 其他技能取消
Damage         → 受击取消
HardCC         → 硬控取消
Dodge          → 翻滚取消
```

### 2.6 SkillTimeline

```csharp
[System.Serializable]
public class SkillTimeline
{
    public SkillPhase[] phases;     // 按顺序执行的阶段
    public int nextComboSkillId;    // Recovery 期连段输入跳转的技能 ID
    public float comboWindow;       // 连段输入窗口（秒）
}
```

### 2.7 重构后的 SkillData

将现有 6 个子类统一为一个数据类：

```csharp
public class SkillData : ScriptableObject
{
    [Header("=== Identity ===")]
    [SerializeField] private int _skillId;
    [SerializeField] private string _skillName;
    [SerializeField] private Sprite _icon;

    [Header("=== Cost ===")]
    [SerializeField] private int _manaCost;
    [SerializeField] private int _staminaCost;
    [SerializeField] private float _cooldown;

    [Header("=== Timeline ===")]
    [SerializeField] private SkillTimeline _timeline;

    [Header("=== Config Blocks ===")]
    [SerializeField] private ShapeBlock _shape;
    [SerializeField] private DamageBlock _damage;
    [SerializeField] private EffectBlock _effect;
    [SerializeField] private PresentationBlock _presentation;

    [Header("=== Dash ===")]
    [SerializeField] private float _dashDistance;
    [SerializeField] private float _dashDuration;

    [Header("=== Projectile ===")]
    [SerializeField] private ProjectileConfig _projectile;

    // Properties...
}
```

`SkillType` 枚举不再需要。技能行为完全由 `SkillTimeline.phases` 中的配置决定。

---

## 3. Runtime Pipeline

### 3.1 SkillRunner（替代 SkillExecutor + SkillStateMachine）

```csharp
public class SkillRunner
{
    private readonly SkillData _data;
    private readonly SkillTimeline _timeline;
    private readonly ICombatResolver _combat;
    private readonly IBuffManager _buffManager;
    private readonly IProjectileManager _projectileManager;
    private readonly IDashComponent _dash;

    private int _currentPhaseIndex;
    private float _phaseElapsed;
    private HashSet<int> _consumedHitboxFrames;  // 避免同一帧多次触发

    public SkillPhase CurrentPhase => _timeline.Phases[_currentPhaseIndex];
    public float TotalElapsed { get; private set; }
    public bool IsActive { get; private set; }

    // --- 每帧调用 ---
    public void Update(float dt, SkillRunnerInput input)
    {
        TotalElapsed += dt;
        _phaseElapsed += dt;

        var phase = CurrentPhase;

        // 1. 判定帧检测
        foreach (var hb in phase.Hitboxes)
        {
            if (hb.TriggerTime <= _phaseElapsed && _consumedHitboxFrames.Add(hb.GetHash()))
            {
                float dmgMult = hb.DamageMultiplier * phase.DamageMultiplier;
                _combat.ProcessHit(_data.Shape, _data.Damage, _data.Effect, dmgMult);
            }
        }

        // 2. 阶段完成检查
        if (IsPhaseComplete(phase, input))
            AdvancePhase();

        // 3. 外部输入处理
        if (input.ComboInput && _data.Timeline.NextComboSkillId != 0)
            OnComboRequested?.Invoke(_data.Timeline.NextComboSkillId);

        if (input.InterruptSource != CancelFlags.None)
            TryInterrupt(input.InterruptSource);
    }

    private bool IsPhaseComplete(SkillPhase phase, SkillRunnerInput input)
    {
        return phase.Completion switch
        {
            PhaseCompletion.Duration      => _phaseElapsed >= phase.BaseDuration,
            PhaseCompletion.ManualRelease => _phaseElapsed >= phase.MinDuration
                                          && input.ReleaseCharge,
            PhaseCompletion.HoldInput     => !input.HoldInput
                                          && _phaseElapsed >= phase.MinDuration,
            PhaseCompletion.Animation     => input.AnimCallbackReceived,
            _                             => true
        };
    }

    private void AdvancePhase()
    {
        _currentPhaseIndex++;
        _phaseElapsed = 0f;
        _consumedHitboxFrames.Clear();

        if (_currentPhaseIndex >= _timeline.Phases.Length)
        {
            Complete();
            return;
        }

        var phase = CurrentPhase;

        // 动画
        if (!string.IsNullOrEmpty(phase.AnimTrigger))
            _animator.SetTrigger(phase.AnimTrigger);

        // 投射物
        if (phase.FireProjectileOnEnter)
            _projectileManager.Spawn(_data.Projectile, _owner);

        // 冲刺
        if (_data.DashDistance > 0 && phase.PhaseName == "Active")
            _dash.StartDash(_owner.Forward, _data.DashDistance, _data.DashDuration);

        // 移动策略
        OnMovementChanged?.Invoke(phase.Movement);
    }

    private void TryInterrupt(CancelFlags source)
    {
        if (CurrentPhase.CancelFlags.HasFlag(source))
            Interrupt();
    }
}
```

所有技能类型都走同样的 `Update`。没有类型分支。

### 3.2 SkillRunnerInput

```csharp
public struct SkillRunnerInput
{
    public bool HoldInput;          // 按住中（引导/蓄力）
    public bool ReleaseCharge;      // 松手释放（蓄力）
    public bool ComboInput;         // 连段输入（普攻在 Recovery 期再按）
    public CancelFlags InterruptSource;
    public bool AnimCallbackReceived;
}
```

### 3.3 SkillCastService（替代 SkillCoordinator 的大部分职责）

```csharp
public class SkillCastService
{
    private readonly ISkillRegistry _registry;
    private readonly IResourceService _resources;
    private readonly CooldownService _cooldowns;
    private readonly SkillInputBuffer _inputBuffer;

    private SkillRunner _currentRunner;

    public void HandleInput(SkillInput input)
    {
        var data = _registry.GetSkill(input.SkillId);
        if (data == null) return;
        if (_cooldowns.IsOnCooldown(data.SkillId)) { _inputBuffer.Enqueue(input); return; }
        if (!_resources.CanAfford(data, _owner)) return;

        // 打断检查
        if (_currentRunner?.IsActive == true)
        {
            if (!_currentRunner.CurrentPhase.CancelFlags.HasFlag(CancelFlags.AnySkill))
            {
                _inputBuffer.Enqueue(input);
                return;
            }
            _currentRunner.TryInterrupt(CancelFlags.AnySkill);
        }

        ActivateSkill(data, input);
    }

    private void ActivateSkill(SkillData data, SkillInput input)
    {
        _resources.Consume(data, _owner);
        _cooldowns.StartCooldown(data.SkillId, data.Cooldown);

        _currentRunner = new SkillRunner(data, _combat, _buffManager, _projectileManager, _dash);
        _currentRunner.OnCompleted += HandleCompletion;
        _currentRunner.OnComboRequested += HandleCombo;
        _currentRunner.Start();
    }
}
```

---

## 4. 配置示例

### 4.1 连段普攻 (Combo Attack 1)

```
Timeline:
  Phases:
    [0] Active:
        baseDuration: 0.3
        completion: Duration
        hitboxes: [{triggerTime: 0.15, damageMultiplier: 1.0}]
        movement: Free
        cancelFlags: HardCC
        animTrigger: "Attack1"
    [1] Recovery:
        baseDuration: 0.2
        completion: Duration
        movement: Free
        cancelFlags: HardCC | BasicAttack | Movement
  nextComboSkillId: 10002   (→ Combo Attack 2)
  comboWindow: 0.3
```

### 4.2 蓄力技能 (Skill R)

```
Timeline:
  Phases:
    [0] Windup:
        baseDuration: 0.1
        completion: Duration
        movement: Locked
        cancelFlags: None
        animTrigger: "SkillR_Charge"
    [1] Charge:
        minDuration: 0.3
        maxDuration: 2.0
        completion: ManualRelease
        movement: SlowWalk
        cancelFlags: HardCC | Damage
        damageMultiplier: curve(0.5 → 2.0)    // 随蓄力时间增长
    [2] Active:
        baseDuration: 0.3
        completion: Duration
        hitboxes: [{triggerTime: 0.15, damageMultiplier: 1.0}]
        movement: Locked
        cancelFlags: None
    [3] Recovery:
        baseDuration: 0.4
        completion: Duration
        movement: RotateOnly
        cancelFlags: Movement
```

### 4.3 引导技能 (Skill Q)

```
Timeline:
  Phases:
    [0] Windup:
        baseDuration: 0.5
        completion: Duration
        movement: Locked
        cancelFlags: HardCC
        animTrigger: "SkillQ_Windup"
    [1] Channel:
        baseDuration: 3.0
        completion: HoldInput
        hitboxes: [
            {triggerTime: 0.5, damageMultiplier: 0.2},
            {triggerTime: 1.0, damageMultiplier: 0.2},
            {triggerTime: 1.5, damageMultiplier: 0.2},
            {triggerTime: 2.0, damageMultiplier: 0.2},
            {triggerTime: 2.5, damageMultiplier: 0.2},
            {triggerTime: 3.0, damageMultiplier: 0.2}
        ]
        movement: Locked
        cancelFlags: HardCC
    [2] Recovery:
        baseDuration: 0.3
        completion: Duration
        movement: Free
        cancelFlags: Movement
```

### 4.4 投射物技能

```
Timeline:
  Phases:
    [0] Windup:
        baseDuration: 0.2
        completion: Duration
        movement: Free
        cancelFlags: HardCC
    [1] Active:
        baseDuration: 0.1
        completion: Duration
        fireProjectileOnEnter: true
        movement: Free
        cancelFlags: Movement
    [2] Recovery:
        baseDuration: 0.3
        completion: Duration
        movement: Free
        cancelFlags: Movement

Projectile:
  prefab: Fireball
  speed: 15
  pierce: false
  homing: false
  lifetime: 5
```

---

## 5. 需要新增的子系统

这些子系统解耦了当前 SkillExecutor 中硬编码的逻辑：

| 模块 | 接口 | 职责 |
|------|------|------|
| **ICombatResolver** | `ProcessHit(shape, damage, effect, multiplier)` | 碰撞检测 + 伤害结算 |
| **IBuffManager** | `Apply(effect)`, `Remove(id)`, `Tick(dt)` | 效果生命周期 |
| **IProjectileManager** | `Spawn(config)`, `Update(dt)` | 投射物创建与飞行 |
| **IResourceService** | `CanAfford(skill, owner)`, `Consume(skill, owner)` | 资源验证与消耗 |

---

## 6. 迁移路径

| 阶段 | 内容 | 依赖 |
|------|------|------|
| **Phase 1** | 实现 SkillPhase / SkillTimeline 数据模型 + 编辑器 | SkillData 现有子类保留 |
| **Phase 2** | 实现 SkillRunner 管线引擎 | Phase 1 |
| **Phase 3** | 实现 ICombatResolver（从 SkillExecutor 迁移碰撞逻辑） | Phase 2 |
| **Phase 4** | 实现 IBuffManager | 独立（可与 Phase 2 并行） |
| **Phase 5** | 实现 IProjectileManager | 独立（可与 Phase 2 并行） |
| **Phase 6** | 实现 IResourceService（替换 TODO 桩） | Phase 2 |
| **Phase 7** | 实现 SkillCastService（替换 SkillCoordinator） | Phase 2-6 |
| **Phase 8** | 停机迁移：SkillData 子类 → 统一 SkillData + Timeline | Phase 7 |
| **Phase 9** | 删除旧代码：SkillStateMachine、类型转换链、SkillType 枚举 | Phase 8 |

Phase 1-6 可与现有系统并存。Phase 8 是唯一的破坏性变更点。

---

## 7. 风险

- **动画同步：** `PhaseCompletion.Animation` 依赖 Animator StateBehaviour 回调。当前回调已改为通过 FSMManager 传递。SkillRunner 需要注册到同一个回调链。
- **连段系统：** 当前 ComboTracker 逻辑在 SkillCoordinator 中（`_comboWindowEndTime` / `_lastCompletedComboSkillId`）。迁移到 SkillCastService 时需要保持行为一致。
- **打断矩阵：** 当前有双重打断逻辑（SkillStateMachine.CanBeInterrupted + SkillInterruptionMatrix.CanBeInterruptedInState）。新设计中打断是 Phase 级别的 CancelFlags 属性，需确保不遗漏现有规则。
- **ScriptableObject 迁移：** Phase 8 中所有现有 `.asset` 文件需要重建。工具脚本可以半自动化这个过程。
