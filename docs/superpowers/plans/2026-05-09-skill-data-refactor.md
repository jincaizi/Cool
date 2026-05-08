# SkillData Refactor: Lean Base + Release-Type Subclasses

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the 45-field SkillData god object into a slim abstract base class with release-type subclasses and shared config blocks.

**Architecture:** Four [Serializable] config blocks (DamageBlock, ShapeBlock, EffectBlock, PresentationBlock) embedded in each subclass. Subclasses grouped by release mechanism (Combo/Instant/Charged/Channeled/Projectile). Base class holds only cross-cutting fields (~15).

**Tech Stack:** Unity 2022.3 LTS, C# 9, HybridCLR hotfix layer

---

### Task 1: Create DamageBlock — rename DamageData to DamageBlock

**Files:**
- Rename: `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/DamageData.cs` → `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs`
- Modify: all consumers of `DamageData` type

**Note:** This rename touches many files. The class moves from `Hotfix.GameSystems.Skills.Effect` namespace to `Hotfix.GameSystems.Skills.Data`. The dependent enums (`DamageType`, `AttributeType`, `ModifierType`) stay in `Effect/` for now.

- [ ] **Step 1: Create DamageBlock.cs in Skills/Data namespace**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [System.Serializable]
    public class DamageBlock
    {
        [Header("=== Base Damage ===")]
        [Tooltip("缩放前的固定伤害")]
        [SerializeField] private float _baseDamage;
        public float BaseDamage => _baseDamage;

        [Tooltip("应用于缩放属性的乘数 (如 1.0 = 100% AttackPower加成)")]
        [SerializeField] private float _attackRatio = 1f;
        public float AttackRatio => _attackRatio;

        [Tooltip("哪个角色属性缩放此伤害 (AttackPower, SpellPower等)")]
        [SerializeField] private Effect.AttributeType _scalingAttribute = Effect.AttributeType.AttackPower;
        public Effect.AttributeType ScalingAttribute => _scalingAttribute;

        [Header("=== Damage Type ===")]
        [Tooltip("用于抗性/护甲计算的伤害类别")]
        [SerializeField] private Effect.DamageType _damageType = Effect.DamageType.Physical;
        public Effect.DamageType DamageType => _damageType;

        [Header("=== Critical ===")]
        [Tooltip("加到角色基础暴击率的额外暴击几率 (0.05 = +5%)")]
        [SerializeField] private float _criticalRateBonus;
        public float CriticalRateBonus => _criticalRateBonus;

        [Tooltip("加到基础1.5倍的额外暴击伤害乘数 (0.5 = 总计2.0倍)")]
        [SerializeField] private float _criticalDamageBonus;
        public float CriticalDamageBonus => _criticalDamageBonus;

        [Header("=== Special ===")]
        [Tooltip("真伤无视护甲和抗性")]
        [SerializeField] private bool _isTrueDamage;
        public bool IsTrueDamage => _isTrueDamage;

        [Tooltip("被忽略的目标护甲百分比 (0-1, 0.3 = 忽略30%)")]
        [SerializeField] private float _armorPenetration;
        public float ArmorPenetration => _armorPenetration;

        [Header("=== Over Time ===")]
        [Tooltip("是否为持续伤害效果?")]
        [SerializeField] private bool _isDOT;
        public bool IsDOT => _isDOT;

        [Tooltip("DOT检测的间隔秒数")]
        [SerializeField] private float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        [Tooltip("DOT总检测次数")]
        [SerializeField] private int _totalTicks = 5;
        public int TotalTicks => _totalTicks;

        // DOT字段是 DamageBlock 特有的,见 spec; 保留 CalculateFinalDamage 方法
        public float CalculateFinalDamage(Effect.IEffectStats attackerStats)
        {
            if (attackerStats == null)
                return _baseDamage;

            float damage = _baseDamage;
            float scalingValue = attackerStats.GetAttribute(_scalingAttribute);
            damage += scalingValue * _attackRatio;

            if (_criticalRateBonus > 0)
            {
                float critChance = 0.05f + _criticalRateBonus;
                if (Random.value < critChance)
                    damage *= (1f + 1.5f + _criticalDamageBonus);
            }

            return _isDOT ? damage * _tickInterval : damage;
        }

        public static DamageBlock CreateDefault(float baseDamage, float attackRatio = 1f)
        {
            return new DamageBlock { _baseDamage = baseDamage, _attackRatio = attackRatio };
        }
    }
}
```

- [ ] **Step 2: Update all DamageData usages to DamageBlock**

Files to modify (replace `DamageData` with `DamageBlock` and add/change using):
- `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs` — change field type, update `using`
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` — `DamageData` → `DamageBlock`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackHitboxData.cs` — `DamageData` → `DamageBlock`, update using
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs` — `DamageData` → `DamageBlock`, update using
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageable.cs` — parameter type `DamageData` → `DamageBlock`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` — `DamageData` → `DamageBlock`
- `Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs` — `DamageData` → `DamageBlock`
- `Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs` — `DamageData` → `DamageBlock`
- `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs` — `DamageData` → `DamageBlock`
- `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterStats.cs` — `DamageData` → `DamageBlock`
- `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs` — `DamageData` → `DamageBlock`

Each file change is: `DamageData` → `DamageBlock` in type references, and add `using Hotfix.GameSystems.Skills.Data;` (remove `using Hotfix.GameSystems.Skills.Effect;` if it was only for DamageData).

- [ ] **Step 3: Delete old DamageData.cs**, then refresh assets.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: rename DamageData to DamageBlock, move to Skills/Data namespace"
```

---

### Task 2: Create ShapeBlock — superset of AttackShapeConfig

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ShapeBlock.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs` (eventually deleted)
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeFactory.cs`

- [ ] **Step 1: Create ShapeBlock.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    public enum TargetType
    {
        [Tooltip("单体目标，射线或锁定检测")]
        Single,
        [Tooltip("圆形AOE")]
        AOE_Circle,
        [Tooltip("锥形AOE")]
        AOE_Cone,
        [Tooltip("扇形AOE")]
        AOE_Sector,
        [Tooltip("以自身为中心")]
        Self
    }

    [System.Serializable]
    public class ShapeBlock
    {
        [Header("=== Target ===")]
        [Tooltip("打击目标类型")]
        [SerializeField] private TargetType _targetType = TargetType.Single;
        public TargetType TargetType => _targetType;

        [Header("=== Dimensions ===")]
        [Tooltip("攻击范围（距离/半径）")]
        [SerializeField] private float _range = 2f;
        public float Range => _range;

        [Tooltip("锥形角度（仅 Cone 有效）")]
        [SerializeField] private float _angle = 120f;
        public float Angle => _angle;

        [Tooltip("扇形起始角度（仅 Sector 有效）")]
        [SerializeField] private float _angleStart;
        public float AngleStart => _angleStart;

        [Tooltip("扇形终止角度（仅 Sector 有效）")]
        [SerializeField] private float _angleEnd = 90f;
        public float AngleEnd => _angleEnd;

        [Tooltip("AOE半径（世界单位，0 = 单体）")]
        [SerializeField] private float _areaRadius;
        public float AreaRadius => _areaRadius;

        [Tooltip("矩形宽度（仅 Rect 有效）")]
        [SerializeField] private float _width = 1f;
        public float Width => _width;

        [Header("=== Collision ===")]
        [Tooltip("碰到第一个目标后是否停止")]
        [SerializeField] private bool _stopAtFirst;
        public bool StopAtFirst => _stopAtFirst;

        [Tooltip("目标检测的物理层遮罩")]
        [SerializeField] private LayerMask _targetMask = ~0;
        public LayerMask TargetMask => _targetMask;

        [Header("=== Hit Timings ===")]
        [Tooltip("从技能开始算起的判定帧时间（秒）。每个值是一次独立的伤害检测。")]
        [SerializeField] private float[] _hitboxTimings = new float[] { 0.3f };
        public float[] HitboxTimings => _hitboxTimings;
    }
}
```

- [ ] **Step 2: Update AttackShapeFactory to use ShapeBlock instead of AttackShapeConfig**

```csharp
using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeFactory
    {
        public static IAttackShape Create(ShapeBlock config,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            if (config == null)
                return new ConeShape(2f, 120f, registry, targetType);

            return config.TargetType switch
            {
                TargetType.AOE_Cone => new ConeShape(config.Range, config.Angle, registry, targetType),
                TargetType.AOE_Circle => new CircleShape(config.Range, registry, targetType),
                TargetType.AOE_Sector => new SectorShape(config.Range, config.AngleStart, config.AngleEnd, registry, targetType),
                _ => new ConeShape(config.Range, config.Angle, registry, targetType),
            };
        }
    }
}
```

- [ ] **Step 3: Update WeaponConfig to use ShapeBlock instead of AttackShapeConfig**

```csharp
// In WeaponConfig.cs: replace `public AttackShapeConfig AttackShape;` with `public ShapeBlock AttackShape;`
```

- [ ] **Step 4: Update MonsterConfig to use ShapeBlock instead of AttackShapeConfig**

```csharp
// In MonsterConfig.cs: replace `public AttackShapeConfig AttackShape;` with `public ShapeBlock AttackShape;`
```

- [ ] **Step 5: Delete old AttackShapeConfig.cs**

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: create ShapeBlock, replace AttackShapeConfig across weapon/monster systems"
```

---

### Task 3: Create EffectBlock and PresentationBlock

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/EffectBlock.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/PresentationBlock.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs` (simplified, then deleted later)

- [ ] **Step 1: Create EffectBlock.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Skills.Data
{
    [System.Serializable]
    public class EffectBlock
    {
        [Header("=== On-Hit Effects ===")]
        [Tooltip("命中时施加的状态效果 (buff, debuff, stun, knockback等)")]
        [SerializeField] private EffectData[] _applyEffects;
        public EffectData[] ApplyEffects => _applyEffects;

        [Header("=== Force ===")]
        [Tooltip("击退力度")]
        [SerializeField] private float _knockbackForce;
        public float KnockbackForce => _knockbackForce;

        [Tooltip("浮空力度")]
        [SerializeField] private float _launchForce;
        public float LaunchForce => _launchForce;

        [Header("=== Status ===")]
        [Tooltip("硬直持续时间 (秒)")]
        [SerializeField] private float _stunDuration;
        public float StunDuration => _stunDuration;

        [Tooltip("附带的状态效果类型")]
        [SerializeField] private StatusEffectType _statusType;
        public StatusEffectType StatusType => _statusType;

        [Tooltip("状态效果持续时间 (秒)")]
        [SerializeField] private float _statusDuration;
        public float StatusDuration => _statusDuration;

        [Tooltip("状态效果数值 (中毒伤害/减速百分比等)")]
        [SerializeField] private float _statusValue;
        public float StatusValue => _statusValue;
    }

    // StatusEffectType moved here from AttackEffectConfig
    public enum StatusEffectType
    {
        None = 0,
        Poison = 1,
        Bleed = 2,
        Slow = 3,
        Stun = 4,
    }
}
```

- [ ] **Step 2: Create PresentationBlock.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [System.Serializable]
    public class PresentationBlock
    {
        [Header("=== VFX ===")]
        [Tooltip("施法阶段生成的VFX预制体")]
        [SerializeField] private GameObject _castVFX;
        public GameObject CastVFX => _castVFX;

        [Tooltip("技能释放/命中时生成的VFX预制体")]
        [SerializeField] private GameObject _releaseVFX;
        public GameObject ReleaseVFX => _releaseVFX;

        [Header("=== SFX ===")]
        [Tooltip("施法时播放的SFX")]
        [SerializeField] private AudioClip _castSFX;
        public AudioClip CastSFX => _castSFX;

        [Header("=== Hit ===")]
        [Tooltip("命中时的冻结帧持续时间(秒)")]
        [SerializeField] private float _hitStopDuration;
        public float HitStopDuration => _hitStopDuration;

        [Header("=== Casting Bar ===")]
        [Tooltip("在HUD上显示此技能的引导条?")]
        [SerializeField] private bool _showCastingBar = true;
        public bool ShowCastingBar => _showCastingBar;

        [Tooltip("HUD上引导条的颜色")]
        [SerializeField] private Color _castingBarColor = Color.blue;
        public Color CastingBarColor => _castingBarColor;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add EffectBlock and PresentationBlock as shared config types"
```

---

### Task 4: Update WeaponConfig and MonsterConfig to use new blocks

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/WeaponConfig.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs` (AttackEffectConfig references)
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs`

- [ ] **Step 1: Rewrite WeaponConfig.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    [CreateAssetMenu(menuName = "Game/Weapon/Config")]
    public class WeaponConfig : ScriptableObject
    {
        [Header("Basic")]
        public string WeaponId;
        public WeaponType WeaponType;

        [Header("Attack")]
        public ShapeBlock AttackShape;
        public DamageBlock Damage;
        public float AttackSpeed = 1f;

        [Header("Skills")]
        public string[] SkillIds;
    }
}
```

- [ ] **Step 2: Rewrite MonsterConfig.cs Attack section**

Replace the `AttackShapeConfig AttackShape` and `AttackEffectConfig[] AttackEffects` fields with:
```csharp
[Header("Attack")]
[Tooltip("攻击判定形状配置")]
public ShapeBlock AttackShape;

[Tooltip("攻击伤害配置")]
public DamageBlock AttackDamage;

[Tooltip("攻击效果配置")]
public EffectBlock AttackEffect;
```

- [ ] **Step 3: Update MeleeWeapon.cs** — Change `_config.Effects[i].Damage` to `_config.Damage`, since WeaponConfig now has `DamageBlock Damage` directly.

```csharp
// In Attack() method:
foreach (var t in _hitBuffer)
{
    if (_config.Damage != null)
    {
        Vector3 dir = (t.Transform.position - transform.position).normalized;
        t.TakeDamage(_config.Damage, dir);
    }
}
```

- [ ] **Step 4: Update MonsterAI.cs** — change AttackEffectConfig references to use DamageBlock + EffectBlock. Replace `public event Action<AttackEffectConfig> OnAttackHitboxActivate` with `public event Action<DamageBlock, EffectBlock> OnAttackHitboxActivate`.

- [ ] **Step 5: Delete AttackEffectConfig.cs**

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: replace AttackEffectConfig with DamageBlock + EffectBlock in weapon/monster"
```

---

### Task 5: Rewrite SkillData as slim abstract base class

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs`

- [ ] **Step 1: Rewrite SkillData.cs**

```csharp
using UnityEngine;
using Definition = Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Data
{
    public abstract class SkillData : ScriptableObject
    {
        [Header("=== Identity ===")]
        [Tooltip("唯一数字ID，映射到SkillID枚举值")]
        [SerializeField] protected int _skillId;
        public int SkillId => _skillId;

        [Tooltip("UI和调试日志中显示的名称")]
        [SerializeField] protected string _skillName;
        public string SkillName => _skillName;

        [Tooltip("风格文本/机制描述（可选）")]
        [SerializeField, TextArea(2, 5)] protected string _description;
        public string Description => _description;

        [Tooltip("技能栏/HUD上显示的图标")]
        [SerializeField] protected Sprite _icon;
        public Sprite Icon => _icon;

        [Tooltip("技能类别")]
        [SerializeField] protected Definition.SkillType _skillType = Definition.SkillType.Special;
        public Definition.SkillType SkillType => _skillType;

        [Tooltip("稀有度等级")]
        [SerializeField] protected Definition.SkillQuality _quality = Definition.SkillQuality.Common;
        public Definition.SkillQuality Quality => _quality;

        [Header("=== Cost ===")]
        [Tooltip("每次施放的法力/能量消耗")]
        [SerializeField] protected int _manaCost;
        public int ManaCost => _manaCost;

        [Tooltip("激活后冷却时间(秒)")]
        [SerializeField] protected float _cooldown;
        public float Cooldown => _cooldown;

        [Tooltip("每次施放的体力消耗")]
        [SerializeField] protected int _staminaCost;
        public int StaminaCost => _staminaCost;

        [Header("=== Animation ===")]
        [Tooltip("Animator Trigger参数名")]
        [SerializeField] protected string _animatorTrigger;
        public string AnimatorTrigger => _animatorTrigger;

        [Tooltip("施法动画片段（持续时间参考）")]
        [SerializeField] protected AnimationClip _castClip;
        public AnimationClip CastClip => _castClip;

        [Tooltip("释放/执行动画片段（持续时间参考）")]
        [SerializeField] protected AnimationClip _releaseClip;
        public AnimationClip ReleaseClip => _releaseClip;

        [Header("=== Dash (Cross-cutting) ===")]
        [Tooltip("冲刺距离(世界单位) (0 = 不冲刺)")]
        [SerializeField] protected float _dashDistance;
        public float DashDistance => _dashDistance;

        [Tooltip("冲刺持续时间(秒)")]
        [SerializeField] protected float _dashDuration;
        public float DashDuration => _dashDuration;

        [Header("=== Interruption ===")]
        [Tooltip("受伤害是否可以中断此技能?")]
        [SerializeField] protected bool _canBeInterruptedByDamage = true;
        public bool CanBeInterruptedByDamage => _canBeInterruptedByDamage;

        [Tooltip("移动输入是否可以中断此技能?")]
        [SerializeField] protected bool _canBeInterruptedByMovement;
        public bool CanBeInterruptedByMovement => _canBeInterruptedByMovement;

        [Tooltip("与其他技能竞争时的优先级")]
        [SerializeField] protected int _interruptionPriority = 50;
        public int InterruptionPriority => _interruptionPriority;

        [Header("=== Cancellation ===")]
        [Tooltip("能否在恢复帧中取消为普通攻击?")]
        [SerializeField] protected bool _canCancelIntoBasicAttack = true;
        public bool CanCancelIntoBasicAttack => _canCancelIntoBasicAttack;

        [Tooltip("能否在恢复帧中取消为另一个技能?")]
        [SerializeField] protected bool _canCancelIntoOtherSkill;
        public bool CanCancelIntoOtherSkill => _canCancelIntoOtherSkill;

        [Header("=== Damage ===")]
        [Tooltip("伤害配置（纯Buff技能可留空）")]
        [SerializeField] protected DamageBlock _damage;
        public DamageBlock Damage => _damage;

        // --- Removed from base (moved to subclasses or blocks) ---
        // ReleaseType, CastTime, ChannelDuration, MinChargeTime, MaxChargeTime
        // CanMoveWhileCasting, CanMoveWhileChanneling, CanRotateWhileCasting
        // Range, Angle, AreaRadius, TargetMask, HitboxTimings
        // --> moved to ShapeBlock
        // ApplyEffects, CastVFX, ReleaseVFX, CastSFX
        // --> moved to EffectBlock / PresentationBlock

        public AnimationClip GetMainAnimationClip()
        {
            return _releaseClip ?? _castClip;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs
git commit -m "refactor: slim SkillData to abstract base with ~18 cross-cutting fields"
```

---

### Task 6: Create ComboSkillData (replaces BasicAttackData)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ComboSkillData.cs`
- Delete (later): `Assets/Scripts/Hotfix/GameSystems/Skills/Data/BasicAttackData.cs`

- [ ] **Step 1: Create ComboSkillData.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ComboAttack", menuName = "Game/Skills/Combo Attack")]
    public class ComboSkillData : SkillData
    {
        [Header("=== Combo ===")]
        [Tooltip("连击链中第几次打击 (1 = 第一击)")]
        [SerializeField] private int _comboIndex;
        public int ComboIndex => _comboIndex;

        [Tooltip("接受下一个连击输入的时间窗口(秒)")]
        [SerializeField] private float _comboWindow = 0.5f;
        public float ComboWindow => _comboWindow;

        [Tooltip("无输入时连击链重置时间")]
        [SerializeField] private float _comboResetTime = 3f;
        public float ComboResetTime => _comboResetTime;

        [Tooltip("连击链中下一个ComboSkillData的引用")]
        [SerializeField] private ComboSkillData _nextCombo;
        public ComboSkillData NextCombo => _nextCombo;

        [Header("=== Movement ===")]
        [Tooltip("此攻击期间角色是否可以移动?")]
        [SerializeField] private bool _enableMovement = true;
        public bool EnableMovement => _enableMovement;

        [Tooltip("此攻击期间的速度倍率 (1 = 正常速度)")]
        [SerializeField] private float _movementSpeed;
        public float MovementSpeed => _movementSpeed;

        [Header("=== Hit FX ===")]
        [Tooltip("打击类型")]
        [SerializeField] private AttackHitType _hitType = AttackHitType.Slash;
        public AttackHitType HitType => _hitType;

        [Tooltip("命中时施加在目标上的击退力")]
        [SerializeField] private float _impactForce;
        public float ImpactForce => _impactForce;

        [Tooltip("相对于攻击者朝向的击退方向偏移")]
        [SerializeField] private Vector3 _impactDirection;
        public Vector3 ImpactDirection => _impactDirection;

        [Tooltip("此攻击是否可以被目标格挡?")]
        [SerializeField] private bool _canBeParried = true;
        public bool CanBeParried => _canBeParried;

        [Header("=== Recovery Cancel ===")]
        [Tooltip("此攻击的恢复帧是否可以取消到下一个动作?")]
        [SerializeField] private bool _allowRecoveryCancel = true;
        public bool AllowRecoveryCancel => _allowRecoveryCancel;

        [Tooltip("取消窗口开始的归一化时间 (0-1)")]
        [SerializeField] private float _cancelableWindowStart;
        public float CancelableWindowStart => _cancelableWindowStart;

        [Tooltip("取消窗口结束的归一化时间 (0-1)")]
        [SerializeField] private float _cancelableWindowEnd;
        public float CancelableWindowEnd => _cancelableWindowEnd;

        [Header("=== Animation Override ===")]
        [Tooltip("可选动画片段，用于覆盖基础技能的动画")]
        [SerializeField] private AnimationClip _overrideClip;
        public AnimationClip OverrideClip => _overrideClip;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        // Methods ported from BasicAttackData
        public AnimationClip GetAnimationClip()
        {
            return _overrideClip ?? base.GetMainAnimationClip();
        }

        public bool IsInCancelableWindow(float elapsedTime, float totalDuration)
        {
            if (!_allowRecoveryCancel) return false;
            float normalizedTime = elapsedTime / totalDuration;
            return normalizedTime >= _cancelableWindowStart && normalizedTime <= _cancelableWindowEnd;
        }

        public int GetNextComboId()
        {
            return _nextCombo?.SkillId ?? 0;
        }

        private void OnValidate()
        {
            _skillType = Definition.SkillType.BasicAttack;
        }
    }

    public enum AttackHitType
    {
        [Tooltip("斩击/切割伤害")]
        Slash,
        [Tooltip("刺穿/穿刺伤害")]
        Pierce,
        [Tooltip("钝器/撞击伤害")]
        Blunt,
        [Tooltip("空手踢伤害")]
        Kick
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/ComboSkillData.cs
git commit -m "feat: add ComboSkillData subclass with Shape + Presentation blocks"
```

---

### Task 7: Create InstantSkillData, ChargedSkillData, ChanneledSkillData, ProjectileSkillData

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/InstantSkillData.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ChargedSkillData.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ChanneledSkillData.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs`

- [ ] **Step 1: Create InstantSkillData.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "InstantSkill", menuName = "Game/Skills/Instant Skill")]
    public class InstantSkillData : SkillData
    {
        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;
    }
}
```

- [ ] **Step 2: Create ChargedSkillData.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ChargedSkill", menuName = "Game/Skills/Charged Skill")]
    public class ChargedSkillData : SkillData
    {
        [Header("=== Charge ===")]
        [Tooltip("按住按钮继续蓄力?")]
        [SerializeField] private bool _holdToCharge = true;
        public bool HoldToCharge => _holdToCharge;

        [Tooltip("松开按钮发射?")]
        [SerializeField] private bool _releaseToFire = true;
        public bool ReleaseToFire => _releaseToFire;

        [Tooltip("技能可以释放的最小蓄力时间")]
        [SerializeField] private float _minChargeTime = 0.3f;
        public float MinChargeTime => _minChargeTime;

        [Tooltip("技能自动释放的最大蓄力时间")]
        [SerializeField] private float _maxChargeTime = 2f;
        public float MaxChargeTime => _maxChargeTime;

        [Tooltip("蓄力时间上的伤害倍率曲线")]
        [SerializeField] private AnimationCurve _chargeDamageCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
        public AnimationCurve ChargeDamageCurve => _chargeDamageCurve;

        [Tooltip("蓄力时间上的AOE半径倍率曲线")]
        [SerializeField] private AnimationCurve _chargeAreaCurve = AnimationCurve.Linear(0f, 1f, 1f, 1.5f);
        public AnimationCurve ChargeAreaCurve => _chargeAreaCurve;

        [Header("=== Movement ===")]
        [Tooltip("蓄力阶段是否可以移动?")]
        [SerializeField] private bool _canMoveWhileCharging = true;
        public bool CanMoveWhileCharging => _canMoveWhileCharging;

        [Tooltip("蓄力阶段是否可以旋转?")]
        [SerializeField] private bool _canRotateWhileCharging = true;
        public bool CanRotateWhileCharging => _canRotateWhileCharging;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        public float GetDamageScaleForCharge(float chargeProgress)
        {
            if (chargeProgress <= 0) return 1f;
            return _chargeDamageCurve?.Evaluate(chargeProgress) ?? (1f + chargeProgress);
        }

        public float GetAreaScaleForCharge(float chargeProgress)
        {
            if (chargeProgress <= 0) return 1f;
            return _chargeAreaCurve?.Evaluate(chargeProgress) ?? 1f;
        }

        private void OnValidate()
        {
            if (_skillType == Definition.SkillType.BasicAttack)
                _skillType = Definition.SkillType.Special;
        }
    }
}
```

- [ ] **Step 3: Create ChanneledSkillData.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ChanneledSkill", menuName = "Game/Skills/Channeled Skill")]
    public class ChanneledSkillData : SkillData
    {
        [Header("=== Channel ===")]
        [Tooltip("技能释放前的引导时间(秒)")]
        [SerializeField] private float _castTime;
        public float CastTime => _castTime;

        [Tooltip("引导持续时间(秒)")]
        [SerializeField] private float _channelDuration;
        public float ChannelDuration => _channelDuration;

        [Tooltip("引导动画片段")]
        [SerializeField] private AnimationClip _channelClip;
        public AnimationClip ChannelClip => _channelClip;

        [Tooltip("引导期间伤害检测的间隔秒数")]
        [SerializeField] private float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        [Tooltip("每次检测的基础伤害百分比 (0-1)")]
        [SerializeField][Range(0f, 1f)] private float _tickDamagePercent = 0.2f;
        public float TickDamagePercent => _tickDamagePercent;

        [Tooltip("引导效果是否跟随目标移动?")]
        [SerializeField] private bool _channelFollowsTarget;
        public bool ChannelFollowsTarget => _channelFollowsTarget;

        [Tooltip("目标移出范围时中断引导?")]
        [SerializeField] private bool _breakOnTargetMove;
        public bool BreakOnTargetMove => _breakOnTargetMove;

        [Header("=== Movement ===")]
        [Tooltip("引导阶段是否可以移动?")]
        [SerializeField] private bool _canMoveWhileChanneling = true;
        public bool CanMoveWhileChanneling => _canMoveWhileChanneling;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        public int GetTotalChannelTicks()
        {
            if (_channelDuration <= 0 || _tickInterval <= 0) return 0;
            return Mathf.FloorToInt(_channelDuration / _tickInterval);
        }

        private void OnValidate()
        {
            if (_skillType == Definition.SkillType.BasicAttack)
                _skillType = Definition.SkillType.Special;
        }
    }
}
```

- [ ] **Step 4: Create ProjectileSkillData.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ProjectileSkill", menuName = "Game/Skills/Projectile Skill")]
    public class ProjectileSkillData : SkillData
    {
        [Header("=== Projectile ===")]
        [Tooltip("施放时生成的投射物预制体")]
        [SerializeField] private GameObject _projectilePrefab;
        public GameObject ProjectilePrefab => _projectilePrefab;

        [Tooltip("投射物速度(世界单位/秒)")]
        [SerializeField] private float _projectileSpeed = 20f;
        public float ProjectileSpeed => _projectileSpeed;

        [Tooltip("投射物是否穿透目标?")]
        [SerializeField] private bool _projectilePierce;
        public bool ProjectilePierce => _projectilePierce;

        [Tooltip("投射物可以穿透的最大目标数")]
        [SerializeField] private int _maxPierceTargets = 3;
        public int MaxPierceTargets => _maxPierceTargets;

        [Tooltip("投射物是否追踪目标?")]
        [SerializeField] private bool _homing;
        public bool Homing => _homing;

        [Header("=== Config Blocks ===")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;
        // Note: no ShapeBlock — projectile handles its own collision
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/InstantSkillData.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Data/ChargedSkillData.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Data/ChanneledSkillData.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs
git commit -m "feat: add Instant/Charged/Channeled/Projectile skill data subclasses"
```

---

### Task 8: Update SkillStateMachine to dispatch on concrete type

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs`

- [ ] **Step 1: Rewrite SkillStateMachine to use subclass patterns**

Key changes:
- `_skillData.ReleaseType` → dispatch on concrete type with `is` checks
- `_skillData.CastTime` → from `ChanneledSkillData.CastTime`
- `_skillData.MaxChargeTime` → from `ChargedSkillData.MaxChargeTime`
- `_skillData.MinChargeTime` → from `ChargedSkillData.MinChargeTime`
- `_skillData.ChannelDuration` → from `ChanneledSkillData.ChannelDuration`
- `_skillData.HitboxTimings` → from `ShapeBlock.HitboxTimings`

```csharp
using System;
using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Skills.Runtime
{
    public class SkillStateMachine
    {
        private readonly SkillData _skillData;
        private SkillSubState _currentState;
        private float _stateStartTime;
        private float _elapsedTime;
        private float _chargeStartTime;
        private bool _isCharging;
        private int _currentTick;
        private float _lastTickTime;

        // Cached type checks
        private readonly bool _isCharged;
        private readonly bool _isChanneled;

        private event Action<SkillSubState> _onStateChanged;
        private event Action<int> _onHitboxFrame;
        private event Action _onHitConfirm;
        private event Action _onSkillCompleted;
        private event Action<InterruptionSource> _onSkillInterrupted;

        public SkillSubState CurrentState => _currentState;
        public float ElapsedTime => _elapsedTime;
        public float StateDuration => GetTotalDuration();

        public event Action<SkillSubState> OnStateChanged
        {
            add => _onStateChanged += value;
            remove => _onStateChanged -= value;
        }
        public event Action<int> OnHitboxFrame
        {
            add => _onHitboxFrame += value;
            remove => _onHitboxFrame -= value;
        }
        public event Action OnHitConfirm
        {
            add => _onHitConfirm += value;
            remove => _onHitConfirm -= value;
        }
        public event Action OnSkillCompleted
        {
            add => _onSkillCompleted += value;
            remove => _onSkillCompleted -= value;
        }
        public event Action<InterruptionSource> OnSkillInterrupted
        {
            add => _onSkillInterrupted += value;
            remove => _onSkillInterrupted -= value;
        }

        public SkillStateMachine(SkillData data)
        {
            _skillData = data;
            _isCharged = data is ChargedSkillData;
            _isChanneled = data is ChanneledSkillData;
            _currentState = SkillSubState.Ready;
            _stateStartTime = -1f;
        }

        public bool TryStart()
        {
            if (_currentState != SkillSubState.Ready && _currentState != SkillSubState.Cooldown)
                return false;

            if (_isCharged)
            {
                TransitionTo(SkillSubState.Casting);
                _isCharging = true;
                _chargeStartTime = GetCurrentTime();
            }
            else if (_isChanneled)
            {
                TransitionTo(SkillSubState.Casting);
            }
            else
            {
                // Instant — skip cast, go straight to execution
                TransitionTo(SkillSubState.Execution);
            }

            return true;
        }

        public void Update(float deltaTime)
        {
            if (_stateStartTime < 0) return;
            _elapsedTime = GetCurrentTime() - _stateStartTime;

            switch (_currentState)
            {
                case SkillSubState.Casting:
                    UpdateCasting();
                    break;
                case SkillSubState.Charging:
                    UpdateCharging();
                    break;
                case SkillSubState.Channeling:
                    UpdateChanneling();
                    break;
                case SkillSubState.Execution:
                    UpdateExecution();
                    break;
                case SkillSubState.Recovery:
                    UpdateRecovery();
                    break;
            }
        }

        public void ReleaseCharge()
        {
            if (_currentState == SkillSubState.Charging)
                _isCharging = false;
        }

        public bool Interrupt(InterruptionSource source)
        {
            if (!CanBeInterrupted(source)) return false;
            TransitionTo(SkillSubState.Cancelled);
            _onSkillInterrupted?.Invoke(source);
            return true;
        }

        public void Complete()
        {
            TransitionTo(SkillSubState.Completed);
            _onSkillCompleted?.Invoke();
        }

        private void UpdateCasting()
        {
            float castTime = (_skillData as ChanneledSkillData)?.CastTime ?? 0f;
            if (_elapsedTime >= castTime)
            {
                if (_isCharged)
                {
                    TransitionTo(SkillSubState.Charging);
                    _chargeStartTime = GetCurrentTime();
                }
                else if (_isChanneled)
                {
                    TransitionTo(SkillSubState.Channeling);
                    _currentTick = 0;
                    _lastTickTime = GetCurrentTime();
                }
                else
                {
                    TransitionTo(SkillSubState.Execution);
                }
            }
        }

        private void UpdateCharging()
        {
            var charged = _skillData as ChargedSkillData;
            if (charged == null) { Complete(); return; }

            float chargeTime = GetCurrentTime() - _chargeStartTime;
            if (chargeTime >= charged.MaxChargeTime)
                TransitionTo(SkillSubState.Execution);
            else if (!_isCharging && chargeTime >= charged.MinChargeTime)
                TransitionTo(SkillSubState.Execution);
        }

        private void UpdateChanneling()
        {
            var channeled = _skillData as ChanneledSkillData;
            if (channeled == null) { Complete(); return; }

            float[] hitboxTimings = GetHitboxTimings();
            if (hitboxTimings == null || hitboxTimings.Length == 0)
            {
                if (_elapsedTime >= channeled.ChannelDuration)
                    TransitionTo(SkillSubState.Execution);
                return;
            }

            for (int i = _currentTick; i < hitboxTimings.Length; i++)
            {
                float tickTime = channeled.CastTime + hitboxTimings[i];
                if (_elapsedTime >= tickTime)
                {
                    _currentTick = i + 1;
                    _onHitboxFrame?.Invoke(i);
                    _onHitConfirm?.Invoke();
                }
            }

            if (_elapsedTime >= channeled.ChannelDuration)
                TransitionTo(SkillSubState.Execution);
        }

        private void UpdateExecution()
        {
            float[] hitboxTimings = GetHitboxTimings();
            if (hitboxTimings != null)
            {
                for (int i = 0; i < hitboxTimings.Length; i++)
                {
                    float castTime = (_skillData as ChanneledSkillData)?.CastTime ?? 0f;
                    float hitboxTime = castTime + hitboxTimings[i];
                    if (Approximately(_elapsedTime, hitboxTime) ||
                        (_elapsedTime > hitboxTime && _elapsedTime < hitboxTime + 0.05f))
                    {
                        _onHitboxFrame?.Invoke(i);
                        _onHitConfirm?.Invoke();
                    }
                }
            }

            float executionDuration = GetExecutionDuration();
            if (executionDuration > 0)
            {
                float castTime = (_skillData as ChanneledSkillData)?.CastTime ?? 0f;
                if (_elapsedTime >= castTime + executionDuration)
                    TransitionTo(SkillSubState.Recovery);
            }
            else
            {
                TransitionTo(SkillSubState.Recovery);
            }
        }

        private void UpdateRecovery()
        {
            float recoveryDuration = 0.1f;
            float castTime = (_skillData as ChanneledSkillData)?.CastTime ?? 0f;
            float recoveryStartTime = castTime + GetExecutionDuration();
            if (_elapsedTime >= recoveryStartTime + recoveryDuration)
                Complete();
        }

        private float[] GetHitboxTimings()
        {
            return (_skillData is ComboSkillData combo) ? combo.Shape?.HitboxTimings :
                   (_skillData is InstantSkillData inst) ? inst.Shape?.HitboxTimings :
                   (_skillData is ChargedSkillData chg) ? chg.Shape?.HitboxTimings :
                   (_skillData is ChanneledSkillData chn) ? chn.Shape?.HitboxTimings :
                   null;
        }

        private void TransitionTo(SkillSubState newState)
        {
            _currentState = newState;
            _stateStartTime = GetCurrentTime();
            _elapsedTime = 0f;
            _onStateChanged?.Invoke(newState);
        }

        private bool CanBeInterrupted(InterruptionSource source)
        {
            return source switch
            {
                InterruptionSource.DamageTaken => _skillData.CanBeInterruptedByDamage,
                InterruptionSource.MovementInput => _skillData.CanBeInterruptedByMovement,
                InterruptionSource.Stun => true,
                InterruptionSource.RollDodge => true,
                InterruptionSource.Parry => true,
                _ => false
            };
        }

        private float GetCurrentTime() => UnityEngine.Time.time;

        private float GetExecutionDuration()
        {
            var clip = _skillData.ReleaseClip ?? _skillData.GetMainAnimationClip();
            return clip != null ? clip.length : 0.5f;
        }

        private float GetTotalDuration()
        {
            if (_skillData is ChargedSkillData charged)
                return charged.MaxChargeTime;
            if (_skillData is ChanneledSkillData channeled)
                return channeled.CastTime + channeled.ChannelDuration;
            return 0f;
        }

        private bool Approximately(float a, float b) => UnityEngine.Mathf.Approximately(a, b);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs
git commit -m "refactor: SkillStateMachine dispatches on concrete SkillData subclass type"
```

---

### Task 9: Update SkillExecutor for new data shapes

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

- [ ] **Step 1: Rewrite SkillExecutor field accesses**

The main changes: `_skillData.DamageData` → `_skillData.Damage`, `_skillData.AreaRadius` → via ShapeBlock, etc.

Key patterns changed:

```csharp
// DetectTargets() — use ShapeBlock from subclass
private List<IEffectTarget> DetectTargets()
{
    var targets = new List<IEffectTarget>();
    ShapeBlock shape = GetShape();
    if (shape == null) return targets;

    if (shape.AreaRadius > 0)
        DetectAOETargets(targets, shape);
    else
        DetectSingleTarget(targets, shape);

    return targets;
}

private ShapeBlock GetShape()
{
    return (_skillData as ComboSkillData)?.Shape
        ?? (_skillData as InstantSkillData)?.Shape
        ?? (_skillData as ChargedSkillData)?.Shape
        ?? (_skillData as ChanneledSkillData)?.Shape;
}

private void DetectSingleTarget(List<IEffectTarget> targets, ShapeBlock shape)
{
    if (_targetCharacter != null && _targetCharacter != _owner)
    {
        float distance = Vector3.Distance(_owner.transform.position, _targetCharacter.transform.position);
        if (distance <= shape.Range)
            targets.Add(_targetCharacter);
    }
    else
    {
        Ray ray = new Ray(_owner.transform.position, _owner.transform.forward);
        if (Physics.Raycast(ray, out var hit, shape.Range, shape.TargetMask))
        {
            if (hit.collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                targets.Add(target);
        }
    }
}

private void DetectAOETargets(List<IEffectTarget> targets, ShapeBlock shape)
{
    Vector3 center = _targetCharacter != null
        ? _targetCharacter.transform.position
        : _targetPosition;

    if (shape.TargetType == TargetType.AOE_Cone)
        DetectConeTargets(center, targets, shape);
    else
    {
        var colliders = Physics.OverlapSphere(center, shape.AreaRadius, shape.TargetMask);
        foreach (var collider in colliders)
        {
            if (collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                targets.Add(target);
        }
    }
}

private void DetectConeTargets(Vector3 center, List<IEffectTarget> targets, ShapeBlock shape)
{
    Vector3 ownerPos = _owner.transform.position;
    Vector3 directionToCenter = (center - ownerPos).normalized;
    float halfAngle = shape.Angle / 2f;

    var colliders = Physics.OverlapSphere(ownerPos, shape.Range, shape.TargetMask);
    foreach (var collider in colliders)
    {
        if (collider.TryGetComponent(out IEffectTarget target) && target != _owner)
        {
            Vector3 dirToTarget = (target.transform.position - ownerPos).normalized;
            float angle = Vector3.Angle(directionToCenter, dirToTarget);
            if (angle <= halfAngle)
                targets.Add(target);
        }
    }
}

// ApplyDamage() — use _skillData.Damage instead of _skillData.DamageData
private void ApplyDamage(IEffectTarget target, int frameIndex)
{
    var damageBlock = _skillData.Damage;
    if (damageBlock == null) return;

    float damage = damageBlock.CalculateFinalDamage(_owner.Stats);

    if (_currentState == SkillSubState.Charging || _currentState == SkillSubState.Execution)
        damage *= 1f + GetChargeProgress() * 0.5f;

    target.Heal(-damage);
}

// ApplyEffects() — use EffectBlock
private void ApplyEffects(IEffectTarget target)
{
    EffectBlock effect = (_skillData as InstantSkillData)?.Effect
        ?? (_skillData as ChargedSkillData)?.Effect
        ?? (_skillData as ChanneledSkillData)?.Effect
        ?? (_skillData as ProjectileSkillData)?.Effect;

    if (effect?.ApplyEffects == null) return;
    foreach (var effectData in effect.ApplyEffects)
        effectData?.Apply(_owner, target);
}

// PlayHitEffects() — use PresentationBlock
private void PlayHitEffects()
{
    var pres = GetPresentation();
    if (pres?.ReleaseVFX != null)
        Object.Instantiate(pres.ReleaseVFX, _targetPosition, Quaternion.identity);
}

private PresentationBlock GetPresentation()
{
    return (_skillData as ComboSkillData)?.Presentation
        ?? (_skillData as InstantSkillData)?.Presentation
        ?? (_skillData as ChargedSkillData)?.Presentation
        ?? (_skillData as ChanneledSkillData)?.Presentation
        ?? (_skillData as ProjectileSkillData)?.Presentation;
}

// GetChargeProgress() — use ChargedSkillData
public float GetChargeProgress()
{
    if (CurrentSubState != SkillSubState.Charging)
        return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

    var charged = _skillData as ChargedSkillData;
    if (charged == null) return 1f;
    return Mathf.Clamp01(_stateMachine.ElapsedTime / charged.MaxChargeTime);
}

// GetChannelProgress() — use ChanneledSkillData
public float GetChannelProgress()
{
    if (CurrentSubState != SkillSubState.Channeling)
        return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

    var channeled = _skillData as ChanneledSkillData;
    if (channeled == null) return 1f;
    float elapsed = _stateMachine.ElapsedTime - channeled.CastTime;
    return Mathf.Clamp01(elapsed / channeled.ChannelDuration);
}

// GetCastProgress() — use ChanneledSkillData
public float GetCastProgress()
{
    if (CurrentSubState != SkillSubState.Casting)
        return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

    var channeled = _skillData as ChanneledSkillData;
    float castTime = channeled?.CastTime ?? 0f;
    if (castTime <= 0) return 1f;
    return Mathf.Clamp01(_stateMachine.ElapsedTime / castTime);
}

// OnStateChanged handler — dash
private void OnStateChanged(SkillSubState newState)
{
    if (newState == SkillSubState.Execution
        && _dashComponent != null
        && _skillData.DashDistance > 0)
    {
        Vector3 dashDir = _owner.transform.forward;
        _dashComponent.StartDash(dashDir, _skillData.DashDistance, _skillData.DashDuration);
    }
}
```

Full file rewrite — update the complete SkillExecutor.cs with all above method replacements.

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs
git commit -m "refactor: SkillExecutor uses ShapeBlock/EffectBlock/PresentationBlock from subclasses"
```

---

### Task 10: Update SkillCoordinator for new types

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`

- [ ] **Step 1: Fix SkillCoordinator compilation errors**

Key changes needed in SkillCoordinator:

1. `CanChainSkill` — `skillData.ReleaseType` no longer exists. Since we dispatch on subclass type instead:
```csharp
private bool CanChainSkill(int nextSkillId)
{
    if (_currentSkill == null || !_currentSkill.IsActive)
        return true;

    var nextData = GetSkillData(nextSkillId);
    if (nextData == null) return false;

    var currentState = _currentSkill.CurrentSubState;
    switch (currentState)
    {
        case SkillSubState.Execution:
        case SkillSubState.HitConfirm:
            return false;
        case SkillSubState.Cancelled:
        case SkillSubState.Completed:
            return true;
    }

    if (currentState == SkillSubState.Recovery)
        return nextData.CanCancelIntoBasicAttack || nextData.CanCancelIntoOtherSkill;

    // Dispatch on current skill type
    return currentState switch
    {
        SkillSubState.Casting => nextData is InstantSkillData, // Instant can chain during cast
        SkillSubState.Channeling => nextData is InstantSkillData
            && (_currentSkill.Data is ChanneledSkillData ch && ch.CanMoveWhileChanneling),
        SkillSubState.Charging => false,
        _ => false
    };
}
```

2. `TryChainCombo` — `BasicAttackData` → `ComboSkillData`:
```csharp
private bool TryChainCombo(int nextSkillId)
{
    if (_currentSkill == null) return false;
    var currentCombo = _currentSkill.Data as ComboSkillData;
    if (currentCombo == null) return false;

    var nextCombo = GetSkillData(nextSkillId) as ComboSkillData;
    if (nextCombo != null && nextCombo.ComboIndex == currentCombo.ComboIndex + 1)
    {
        TryCancelCurrentSkill(InterruptionSource.BasicAttack);
        return TryActivateSkill(nextSkillId);
    }
    return false;
}
```

3. `CanMove()` — no more `CanMoveInState()` because that was on SkillData base:
```csharp
public bool CanMove()
{
    if (_currentSkill == null || !_currentSkill.IsActive)
        return true;

    return _currentSkill.CurrentSubState switch
    {
        SkillSubState.Casting => true,
        SkillSubState.Channeling =>
            (_currentSkill.Data as ChanneledSkillData)?.CanMoveWhileChanneling ?? true,
        _ => true
    };
}
```

4. `CanRotate()`:
```csharp
public bool CanRotate()
{
    if (_currentSkill == null || !_currentSkill.IsActive)
        return true;

    return _currentSkill.CurrentSubState switch
    {
        SkillSubState.Casting => true,
        SkillSubState.Charging =>
            (_currentSkill.Data as ChargedSkillData)?.CanRotateWhileCharging ?? true,
        _ => false
    };
}
```

5. `IsInSafeCastState()`:
```csharp
public bool IsInSafeCastState()
{
    return _currentSkill?.CurrentSubState switch
    {
        SkillSubState.Casting => true,
        SkillSubState.Channeling =>
            (_currentSkill.Data as ChanneledSkillData)?.CanMoveWhileChanneling ?? false,
        SkillSubState.Charging => true,
        _ => false
    };
}
```

6. `OnExecutorCompleted` — remove `_lastAttackTime`, `_comboWindowEndTime`, `COMBO_WINDOW` logic (these were copied from BasicAttackData into ComboSkillData; the combo window is now per-attack from `ComboSkillData.ComboWindow`).

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs
git commit -m "refactor: SkillCoordinator uses ComboSkillData and concrete subclass dispatch"
```

---

### Task 11: Update SkillInterruptionMatrix and remaining consumers

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackHitboxData.cs` (if not already done)

- [ ] **Step 1: Update AttackHitboxData** — change `DamageData` → `DamageBlock`

```csharp
using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    [System.Serializable]
    public class AttackHitboxData
    {
        public DamageBlock DamageData;
        public float KnockbackForce;
        public float LaunchForce;
        public float StunDuration;
        public bool IsCritical;
        public int SourceId;
    }
}
```

- [ ] **Step 2: Update Sys3CEntry.cs references**

The `SkillData[]` array and `HandleSkillActivated` still work because `SkillData` is the base type. But `GetBasicAttackSkillId` etc. references should change to use `ComboSkillData` type if relevant. For now, the enum IDs stay the same.

Remove `using Hotfix.GameSystems.Skills.Effect;` if no longer needed (DamageBlock moved to Data namespace). Ensure `using Hotfix.GameSystems.Skills.Data;` is present.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackHitboxData.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "refactor: update AttackHitboxData and Sys3CEntry for DamageBlock + new types"
```

---

### Task 12: Delete old files and update SkillType enum

**Files:**
- Delete: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/BasicAttackData.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SpecialSkillData.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/DamageData.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/EffectData.cs` 中删除 `EffectDataList` 内联类 (已由 EffectBlock 替代)
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillType.cs`

Wait — EffectData.cs should NOT be deleted. It contains the EffectData hierarchy (BuffEffectData, HealEffectData, etc.) which is still used by EffectBlock.

`EffectDataList` was an inner class in SkillData.cs — it was removed when we rewrote SkillData.cs.

- [ ] **Step 1: Delete the old files**

```bash
rm "Assets/Scripts/Hotfix/GameSystems/Skills/Data/BasicAttackData.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Skills/Data/SpecialSkillData.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Skills/Effect/DamageData.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs"
```

Also remove their `.meta` files.

- [ ] **Step 2: Update SkillType enum**

```csharp
namespace Hotfix.GameSystems.Skills.Definition
{
    public enum SkillType
    {
        Combo,      // 连击技能 (ComboSkillData)
        Instant,    // 瞬发技能 (InstantSkillData)
        Charged,    // 蓄力技能 (ChargedSkillData)
        Channeled,  // 引导技能 (ChanneledSkillData)
        Projectile, // 投射物技能 (ProjectileSkillData)
        Ultimate,   // 大招
        Passive,    // 被动
        Item        // 物品技能
    }
}
```

- [ ] **Step 3: Update SkillID enum** to match new class names (no functional change needed — IDs map to which data class you choose in `CreateAssetMenu`).

- [ ] **Step 4: Refresh assets and verify compilation**

If Unity MCP is connected, run:
```
mcp__ai-game-developer__assets-refresh
```
Then check console for errors:
```
mcp__ai-game-developer__console-get-logs (filter errors)
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: delete old data files, update SkillType enum for new subclasses"
```

---

### Task 13: Final verification — rebuild .asset files

**Files:**
- Create new `.asset` files via Unity Editor (manual)

- [ ] **Step 1: Create new skill .asset files in Unity Inspector**

For each existing skill, right-click → Create → Game → Skills → [appropriate type], configure fields:
- BasicAttack1-3 → ComboSkillData
- SkillQ → InstantSkillData
- SkillR → ChargedSkillData

Delete old `Assets/PreRes/SkillsCfg/New Skill.asset` and `Basic_2.asset`.

- [ ] **Step 2: Assign new assets to Sys3CEntry._characterSkills in Inspector**

- [ ] **Step 3: Enter playmode and verify skills work**

Verify: Basic attack combo triggers, SkillQ instant cast, SkillR hold-to-charge, and animations play correctly.

- [ ] **Step 4: Commit**

```bash
git add Assets/PreRes/SkillsCfg/
git commit -m "config: recreate skill assets with new subclass types"
```
