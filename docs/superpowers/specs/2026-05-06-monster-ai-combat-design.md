# Monster AI & Combat System Design

**Date:** 2026-05-06
**Status:** Approved
**Scope:** Slime + TurtleShell monster AI, character-monster combat, weapon extensibility

---

## 1. Overview

为两个怪物（Slime、TurtleShell）实现差异化 AI 行为，在 DemoDay 场景中刷新，并建立角色↔怪物完整的战斗管道。同时为未来的多武器类型（近战/远程）和多技能扩展预留抽象层。

**核心原则：** 行为差异通过 ScriptableObject 配置驱动，不在代码中硬编码。

---

## 2. Monster Behavior Design

### 2.1 Slime — 高速攻击型

- 速度快、HP 低、攻击频率高
- 两个攻击变体：Attack01（快咬，权重 70%）/ Attack02（重砸，权重 30%）
- AttackShape.Resolve() 返回空（攻击没打到目标）时，概率触发 Taunt
- Idle 状态下，玩家进入 AlertRange 但未到 DetectRange 时播放 SenseSomething 警戒动画

### 2.2 TurtleShell — 防御反击型

- 速度慢、HP 高、防御高
- HP < 50% 或进入 Chase 状态后超过 DefendChaseTimeThreshold 秒 → 进入 Defend 状态
- Defend 期间：正面 180° 减伤 80%，不可移动，持续 2 秒
- 格挡 N 次后触发反击（Attack02 + 伤害加成 50%）
- 防御有冷却时间

### 2.3 AI FSM 状态

在现有 6 状态基础上增加 3 个：

```csharp
public enum MonsterAIState
{
    Idle    = 0,  // 原地等待
    Patrol  = 1,  // 巡逻点间移动
    Chase   = 2,  // 追击玩家
    Attack  = 3,  // 攻击
    Hit     = 4,  // 受击硬直
    Death   = 5,  // 死亡
    Defend  = 6,  // 防御 (TurtleShell)
    Taunt   = 7,  // 嘲讽 (Slime)
    Alert   = 8,  // 警戒
}
```

### 2.4 AI 行为策略化

特殊行为通过 `IAIBehaviour` 接口实现，MonsterAI 在 Init 时根据 Config 组合：

```csharp
public interface IAIBehaviour
{
    bool CanEnter(MonsterAIContext ctx);
    void Enter(MonsterAIContext ctx);
    void Update(MonsterAIContext ctx, float dt);
    void Exit(MonsterAIContext ctx);
    MonsterAIState StateType { get; }
}

// 实现类
class DefendBehaviour : IAIBehaviour { /* TurtleShell 防御 */ }
class TauntBehaviour  : IAIBehaviour { /* Slime 嘲讽 */ }
class AlertBehaviour  : IAIBehaviour { /* 通用警戒 */ }
```

MonsterAI 核心 FSM（Idle/Patrol/Chase/Attack/Hit/Death）保持不变，行为模块按需挂载。新增行为无需修改 MonsterAI 代码。

---

## 3. MonsterConfig Extension

### 3.1 新增 Section

在现有 `MonsterConfig` 基础上增加以下字段（旧 `AttackDamage` 被 `AttackEffects` 取代）：

```csharp
// [Attack] 攻击行为
public int AttackAnimCount;              // 可用攻击动画数量
public float[] AttackWeights;            // 随机权重 (与 AttackEffects 一一对应)
public float AttackAnimSpeed = 1f;       // 攻击动画速度

// [AttackShape] 攻击形状
public AttackShapeConfig AttackShape;    // 攻击判定形状

// [AttackEffects] 每个攻击变体的效果列表 (取代旧 AttackDamage)
// AttackEffects[i] 对应第 i 个攻击动画的伤害+击退+状态效果
public AttackEffectConfig[] AttackEffects;

// [Defend] 防御行为
public bool EnableDefend;
public float DefendHPThreshold = 0.5f;
public float DefendChaseTimeThreshold = 3f; // 追击超过此时间进入防御
public float DefendDuration = 2f;
public float DefendDamageReduction = 0.8f;
public float DefendAngle = 180f;
public int DefendBlockCountToCounter = 2;
public float DefendCounterDamageMultiplier = 1.5f;
public float DefendCooldown = 8f;

// [Taunt] 嘲讽行为
public bool EnableTaunt;
public float TauntChance = 0.6f;
public float TauntDuration = 1.5f;

// [Alert] 警戒行为
public float AlertRange = 15f;

// [Movement] 移动风格
public bool ChaseAnimIsRun = true;
public float RotationSpeed = 10f;
```

### 3.2 Config 资产

创建两个 ScriptableObject：

| 资产 | MonsterId | 关键数值差异 |
|------|-----------|-------------|
| SlimeConfig.asset | "slime" | HP=60, Speed=5, EnableTaunt=true, AttackWeights=[0.7,0.3] |
| TurtleShellConfig.asset | "turtleshell" | HP=150, Speed=2, EnableDefend=true |

---

## 4. Attack System Architecture

### 4.1 IAttackShape — 攻击形状抽象

所有攻击判定统一接口，角色和怪物共用同一套实现：

```csharp
public interface IAttackShape
{
    IReadOnlyList<IDamageable> Resolve(
        Vector3 origin, Vector3 forward, LayerMask targetMask);
}
```

四种内置实现：

| Shape | 判定方式 | 当前用途 | 未来用途 |
|-------|---------|---------|---------|
| ConeShape | 前方扇形 | 剑普攻、Slime 咬 | 斧劈砍、龙爪 |
| CircleShape | 自身圆形 | TurtleShell 旋转、Skill R | Boss 践踏、冰环 |
| RectShape | 前方矩形 | Skill Q 突刺 | 冲锋、穿透箭 |
| RayShape | 射线/弹道 | —（未来） | 弓、枪、Boss 喷吐 |

### 4.2 AttackShapeConfig — 可序列化配置

```csharp
[Serializable]
public class AttackShapeConfig
{
    public ShapeType Type;    // Cone / Circle / Rect / Ray
    public float Range;       // 扇形半径 / 圆形半径 / 矩形长度 / 射线最远距离
    public float Angle;       // 扇形角度 (仅 ConeShape)
    public float Width;       // 矩形宽度 / 弹道宽度 (RectShape / RayShape)
    public bool StopAtFirst;  // 命中第一个目标后停止 (RayShape)
}

// 工厂方法 — 从 Config 创建 Shape 实例
public static class AttackShapeFactory
{
    public static IAttackShape Create(AttackShapeConfig config)
    {
        return config.Type switch
        {
            ShapeType.Cone   => new ConeShape(config.Range, config.Angle),
            ShapeType.Circle => new CircleShape(config.Range),
            ShapeType.Rect   => new RectShape(config.Range, config.Width),
            ShapeType.Ray    => new RayShape(config.Range, config.Width, config.StopAtFirst),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
```

### 4.3 AttackEffectConfig

```csharp
[Serializable]
public class AttackEffectConfig
{
    public DamageData Damage;
    public float KnockbackForce;
    public float StunDuration;
    public StatusEffectType Status;       // None / Poison / Bleed / Slow
    public float StatusDuration;
    public float StatusValue;
}
```

### 4.4 统一 HitZone

替换现有的 `MonsterHitZone`，角色和怪物共用同一个通用组件：

```csharp
[RequireComponent(typeof(Collider))]
public class HitZone : MonoBehaviour
{
    private IDamageable _owner;
    private HashSet<int> _hitInstanceIds;  // instanceID 去重，避免 GC

    public void Init(IDamageable owner) { _owner = owner; }

    // 每次新攻击开始前调用，清空 hit 记录（允许同一攻击源多次命中）
    public void ResetHits() => _hitInstanceIds.Clear();

    private void OnTriggerStay(Collider other)
    {
        var hitbox = other.GetComponent<IAttackHitbox>();
        if (hitbox == null || !hitbox.IsActive) return;
        if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;

        _owner.TakeDamage(
            hitbox.CurrentData.DamageData,
            (transform.position - hitbox.GetBounds().center).normalized);
    }
}
```

### 4.5 MonsterAttackHitbox 调整

- 保留现有 `IAttackHitbox` 实现
- `Activate()` 接受 `AttackEffectConfig` 替代裸 `DamageData`
- 激活/停用时机：动画帧事件驱动（OnAttackFrame → Activate，OnStateExit → Deactivate）

---

## 5. Weapon System

### 5.1 IWeapon 接口

```csharp
public interface IWeapon
{
    WeaponType WeaponType { get; }
    bool CanAttack();
    void Attack(Vector3 forward, LayerMask targetMask);
    WeaponConfig Config { get; }
}
```

### 5.2 WeaponConfig

```csharp
[CreateAssetMenu(menuName = "Game/Weapon/Config")]
public class WeaponConfig : ScriptableObject
{
    public string WeaponId;
    public WeaponType WeaponType;          // Melee / Ranged
    public AttackShapeConfig AttackShape;
    public AttackEffectConfig[] Effects;
    public float AttackSpeed = 1f;
    public string[] SkillIds;              // 关联技能ID
}
```

### 5.3 MeleeWeapon

```csharp
public class MeleeWeapon : MonoBehaviour, IWeapon
{
    [SerializeField] private WeaponConfig _config;
    public WeaponConfig Config => _config;
    public WeaponType WeaponType => WeaponType.Melee;

    public void Attack(Vector3 forward, LayerMask mask)
    {
        var shape = AttackShapeFactory.Create(_config.AttackShape);
        var targets = shape.Resolve(transform.position, forward, mask);
        foreach (var t in targets)
            foreach (var e in _config.Effects)
                ApplyEffect(t, e);
    }
}
```

### 5.4 CharacterAttackHandler

角色攻击入口，持有 IWeapon，响应 FSM 攻击帧回调：

```csharp
public class CharacterAttackHandler : MonoBehaviour
{
    [SerializeField] private LayerMask _targetMask;
    private IWeapon _currentWeapon;

    public void EquipWeapon(IWeapon weapon) => _currentWeapon = weapon;

    // 由 Sys3CEntry 的攻击帧回调触发
    public void OnAttackFrame()
    {
        _currentWeapon?.Attack(transform.forward, _targetMask);
    }
}
```

### 5.5 接入 Sys3CEntry

```csharp
// Sys3CEntry.Start() 中新增
_attackHandler = GetComponent<CharacterAttackHandler>();
_fsmManager.OnAttackFrame += () => _attackHandler?.OnAttackFrame();
```

---

## 6. Skill × AttackShape Integration

技能复用 `IAttackShape` 做命中判定，额外维度通过 SkillConfig 配置：

```csharp
// SkillConfig 新增字段
public AttackShapeConfig AttackShape;    // 复用 AttackShape 体系
public AttackEffectConfig[] Effects;     // 命中效果
public ExecutePattern Pattern;          // Instant / Pulse / Channel / Combo
public MoveBehaviour MoveLock;          // Root / Free / Dash
public TargetingMode Targeting;         // Forward / Self / Target / Ground

// Dash 参数 (MoveLock==Dash)
public float DashDistance;
public float DashDuration;
// Pulse 参数 (Pattern==Pulse)
public float PulseInterval;
public float PulseDuration;
```

| 技能 | Shape | Pattern | Move | Targeting |
|------|-------|---------|------|-----------|
| Skill Q (突刺) | RectShape (3×1.5) | Pulse (0.1s) | Dash (3m/0.3s) | Forward |
| Skill R (旋转) | CircleShape (r=3) | Pulse (0.5s/3s) | Free | Self |
| 普攻 | ConeShape (2/120°) | Instant | Root | Forward |

---

## 7. Combat Pipeline

### 7.1 角色→怪物

```
玩家按键 → FSMManager.RequestAttack
    → AttackFSM 状态切换
    → 动画帧触发 OnAttackFrame 回调
    → CharacterAttackHandler.OnAttackFrame()
    → IWeapon.Attack(forward, mask)
    → IAttackShape.Resolve(origin, forward, mask)
    → 返回 IDamageable[]
    → foreach: 应用 AttackEffects (伤害/击退/状态)
    → MonsterStats.TakeDamage()
    → MonsterAI.NotifyHit()
```

### 7.2 怪物→角色

```
MonsterAI → TransitionTo(Attack)
    → 动画帧触发 OnAttackFrame 回调
    → MonsterAttackHitbox.Activate(effects)
    → Collider.enabled = true (Trigger)
    → OnTriggerStay 检测角色 HitZone
    → HitZone 读取 IAttackHitbox.CurrentData
    → IDamageable.TakeDamage()
    → 角色 Stats 扣血 + HitFSM 受击状态
    → OnStateExit → MonsterAttackHitbox.Deactivate()
```

### 7.3 Layer 隔离

| Layer | 用途 |
|-------|------|
| Monster | 怪物 HitZone + 怪物本体 |
| Character | 角色 HitZone + 角色本体 |
| MonsterAttack | 怪物 AttackHitbox（只与 Character 碰撞） |
| CharacterAttack | 角色 AttackHitbox（只与 Monster 碰撞） |

---

## 8. Animator Controller Design

### 8.1 参数

| 参数 | 类型 | Slime | TurtleShell |
|------|------|-------|-------------|
| AIState | Int | 0-8 | 0-8 |
| Attack | Trigger | ✓ | ✓ |
| AttackIndex | Int | 0-1 | 0-1 |
| Hit | Trigger | ✓ | ✓ |
| Death | Trigger | ✓ | ✓ |
| Taunt | Trigger | ✓ | — |
| IsDefending | Bool | — | ✓ |
| Speed | Float | ✓ (BlendTree) | ✓ (BlendTree) |

### 8.2 状态结构

**Slime:**
- Base Layer: AnyState→Die(Death) / AnyState→GetHit(Hit) / AnyState→Taunt(Taunt)
- Locomotion BlendTree: IdleNormal ↔ Walk(Fwd/Back/Left/Right, Speed驱动) → IdleBattle → Run
- Attack: IdleBattle → Attack01/Attack02 (Attack trigger + AttackIndex)
- SenseSomething 作为 Idle 状态下的子状态

**TurtleShell:**
- Base Layer: AnyState→Die(Death) / AnyState→GetHit(Hit) / AnyState→Defend(IsDefending=true)
- Locomotion: 同 Slime 结构
- Attack: 同 Slime 结构
- Defend: Defend(loop) → DefendHit(Hit trigger in defend) → 返回 Defend

---

## 9. Scene Setup

### 9.1 DemoDay 场景

- 烘培 NavMesh（覆盖 Environment/LowpolyTerrain）
- 放置 MonsterSpawner：
  - **固定点位：** 营地附近放 TurtleShell (Count=1, SpawnRadius=2)
  - **区域刷新：** 开阔区域放 Slime (Count=3, SpawnRadius=8, RespawnDelay=30s)

### 9.2 Prefab 装配

每个怪物 Prefab 需添加：

| 组件 | 说明 |
|------|------|
| NavMeshAgent | 寻路移动 |
| Collider (Trigger) | HitZone 受击检测 |
| MonsterEntity | 主 MonoBehaviour |
| MonsterAttackHitbox (子物体) | 攻击碰撞箱 + Collider |
| Animator | 已有，需配 Controller |

---

## 10. File Change Summary

### 新增文件

| 文件 | 路径 |
|------|------|
| IAttackShape.cs | `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/` |
| ConeShape.cs | 同上 |
| CircleShape.cs | 同上 |
| AttackShapeConfig.cs | 同上 |
| AttackShapeFactory.cs | 同上 |
| AttackEffectConfig.cs | 同上 |
| HitZone.cs | 同上 |
| IAIBehaviour.cs | `Assets/Scripts/Hotfix/GameSystems/Monster/` |
| DefendBehaviour.cs | 同上 |
| TauntBehaviour.cs | 同上 |
| IWeapon.cs | `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/` |
| WeaponConfig.cs | 同上 |
| MeleeWeapon.cs | 同上 |
| CharacterAttackHandler.cs | `Assets/Scripts/Hotfix/GameSystems/Sys3C/` |

### 修改文件

| 文件 | 变更内容 |
|------|---------|
| MonsterAI.cs | 新增 3 个状态；IAIBehaviour 组合；攻击流程改用 IAttackShape |
| MonsterConfig.cs | 新增 Attack/Defend/Taunt/Alert/Movement 字段 |
| MonsterEntity.cs | HitZone 替换为通用 HitZone；攻击帧回调传 AttackEffectConfig |
| MonsterAttackHitbox.cs | Activate 接受 AttackEffectConfig |
| MonsterMovement.cs | 新增 RotationSpeed 配置化 |
| Sys3CEntry.cs | 集成 CharacterAttackHandler |
| SkillConfig.cs | 新增 AttackShape/Effects/Pattern/MoveLock/Targeting 字段 |

### 删除文件

| 文件 | 原因 |
|------|------|
| MonsterHitZone.cs | 被通用 HitZone 替代 |

### 资产文件

| 文件 | 说明 |
|------|------|
| SlimeConfig.asset | Slime 配置 |
| TurtleShellConfig.asset | TurtleShell 配置 |
| SwordShieldConfig.asset | 剑盾武器配置 |
| Slime.controller | Slime Animator Controller |
| TurtleShell.controller | TurtleShell Animator Controller |

---

## 11. Implementation Order

1. **AttackShape 基础层** — IAttackShape + ConeShape + CircleShape + AttackShapeConfig + AttackEffectConfig（纯数据，无依赖）
2. **HitZone 统一** — 通用 HitZone 替换 MonsterHitZone
3. **MonsterConfig 扩展** — 新增所有行为字段
4. **MonsterAI 改造** — IAIBehaviour 策略化 + 新增状态
5. **MonsterEntity 调整** — 适配新 HitZone 和攻击流程
6. **Weapon 抽象层** — IWeapon + WeaponConfig + MeleeWeapon + CharacterAttackHandler
7. **Sys3CEntry 集成** — CharacterAttackHandler 接入
8. **Animator Controller** — 创建 Slime + TurtleShell 的 .controller
9. **Prefab 装配** — 给 Prefab 加组件
10. **场景布置** — DemoDay NavMesh + Spawner 放置
11. **Config 资产** — 创建 SlimeConfig / TurtleShellConfig / SwordShieldConfig
12. **测试验证** — 场景中验证 AI 行为 + 战斗管道
