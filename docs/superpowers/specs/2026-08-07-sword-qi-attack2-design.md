# 普攻2（竖劈）剑气设计

日期：2026-08-07
状态：已批准（brainstorming 完成，待实现计划）

## 背景与目标

普攻技能2（竖劈，Attack02，SkillID 10002）新增附加机制：挥击中段发出一道竖直月牙剑气，沿角色前向飞行，命中沿途所有目标造成伤害。当前为本地实现，预留网络接入点。

## 决策记录

| 维度 | 决定 |
|---|---|
| 剑气定位 | 真实伤害弹体，继承竖劈伤害数据（`SkillData.Damage`），独立结算 |
| 命中方式 | 直线宽判定带（`PhysicsRegistry.FindNearby`），命中全部目标 |
| 触发时机 | 复用既有判定帧机制（`ShapeBlock.HitboxTimings` → `OnHitboxFrame`），在配置的判定帧生成 |
| 射程 / 速度 | 12m / 18 m/s |
| 形态 | 竖直月牙刀光（VFX 预制体，先用现有材质临时搭建） |
| 双段伤害 | 允许：竖劈近战判定与剑气判定独立结算，重叠区目标吃两段 |
| 生命周期 | 与释放者解耦：释放者被打断/死亡不影响飞行中的剑气，剑气飞满射程后回池 |
| 网络 | 本地实现 + 预留 `SwordQiSpawnedEvent`（本次无订阅者） |

## 架构

方案选型：轻量剑气组件 + 挂接技能执行事件（**不**走 `ProjectileSkillData`——其语义为独立技能弹体且缺 DamageBlock 字段；**不**改 `SkillStateMachine`——触发全部发生在 `SkillExecutor` 订阅层）。

```
ComboSkillData (竖劈)
  └─ SwordQiConfig（可选字段，null = 不发射剑气）
        └─ 预制体 / 速度 / 射程 / 判定半径 / 伤害倍率 / 生成帧索引 / 生成偏移 / EffectBlock
                │ 判定帧时刻触发
SkillExecutor.OnSwordQiFrame(frameIndex)
  ├─ 拷贝 DamageBlock（防共享实例污染）
  ├─ 池取 SwordQiProjectile，origin = 角色 + SpawnOffset，dir = 角色前向
  └─ EventBus.Emit(SwordQiSpawnedEvent)（网络预留）
                │ 每帧 Tick
SwordQiProjectile
  ├─ 移动（水平方向，Y 锁定）
  ├─ PhysicsRegistry.FindNearby(位置, 判定半径, EntityType.Monster)
  ├─ HashSet 去重（每目标只命中一次）
  ├─ SkillDamageHelper.FillDamageContext + IDamageable.TakeDamage（两参数版）
  └─ 累计距离 ≥ 射程 → 回池（OnDestroy 守卫防重复回池）
```

## 组件明细

### 1. `SwordQiConfig`（ScriptableObject，新增）

```csharp
[CreateAssetMenu(menuName = "Game/Skills/Sword Qi Config")]
public class SwordQiConfig : ScriptableObject
{
    GameObject _prefab;        // 剑气预制体（竖直月牙刀光）
    float _speed = 18f;        // 飞行速度 m/s
    float _range = 12f;        // 射程 m（累计飞行距离耗尽 → 回池）
    float _detectRadius = 1f;  // 判定带半径（宽扁带判定，兼作高度差补偿）
    float _damagePercent = 1f; // 伤害倍率（1.0 = 竖劈全额）
    int   _spawnFrameIndex;    // 判定帧数组中的第几帧生成（默认 0 = 第一判定帧）
    Vector3 _spawnOffset;      // 生成位置偏移（相对角色：前方 + 剑高）
    EffectBlock _effect;       // 击退/浮空/眩晕（KnockbackForce > 0 时覆盖 DamageBlock 默认值）
}
```

### 2. `SwordQiProjectile`（MonoBehaviour，新增）

- `OnSpawn(SwordQiConfig cfg, DamageBlock damage, Vector3 origin, Vector3 dir)` 初始化（持有 damage 的**私有拷贝**）
- `Tick(float deltaTime)`：移动 + 带判定 + 去重 + 伤害；`Update()` 内调用，公开供 EditMode 测试手动步进
- 命中：`PhysicsRegistry.FindNearby(position, detectRadius, EntityType.Monster)`，`HashSet<int>`（Transform instanceID）去重，每目标只命中一次
- 伤害：`SkillDamageHelper.FillDamageContext(...)` 后 `target.TakeDamage(damageBlock, hitDir)`（两参数版，hitDir = 目标位置 - 剑气位置，与 `SkillExecutor.ApplyDamage` 路径一致）
- 生命周期：累计飞行距离 ≥ 射程 → 回池；`OnDestroy` 置 `_released` 标志防场景销毁时重复回池

### 3. `SkillDamageHelper`（静态帮助类，新增）

抽取 `SkillExecutor.ApplyDamage`（现 519-539 行）的伤害上下文填充逻辑：

```csharp
public static class SkillDamageHelper
{
    // CalculateFinalDamage + SkillId/ComboIndex/CalculatedDamage 填充 + EffectBlock 击退覆盖
    public static void FillDamageContext(DamageBlock damage, int skillId, int comboIndex,
                                         EffectBlock effect, CharacterStats stats);
}
```

`SkillExecutor.ApplyDamage` 重构为调用此帮助类（**行为不变**），`SwordQiProjectile` 复用，保证 `MonsterTakeDamageEvent` 等依赖字段语义一致。

### 4. `ComboSkillData` 扩展

```csharp
[SerializeField] SwordQiConfig _swordQi;  // null = 普攻不发射剑气
public SwordQiConfig SwordQi => _swordQi;
```

### 5. `SkillExecutor` 扩展

- 新增第二订阅者：`_stateMachine.OnHitboxFrame += OnSwordQiFrame;`
- `OnSwordQiFrame(int frameIndex)`：`!_swordQiSpawned && _skillData is ComboSkillData combo && combo.SwordQi != null && frameIndex == combo.SwordQi.SpawnFrameIndex` 时生成剑气，置 `_swordQiSpawned = true`（判定帧同一帧内可能多次触发，防重复生成）
- `_swordQiSpawned` 在 `TryStart()` 复位（连击链多次施放）

### 6. `SwordQiSpawnedEvent`（EventBus 事件，新增）

字段：`CasterId` / `SkillId` / `Origin` / `Direction` / `Speed` / `Range`。本次无订阅者；`SkillNetworkSync` 将来订阅即可转发（远端玩家看到剑气并受击）。

## 数据流

```
普攻2执行 → Shape.HitboxTimings 判定帧时刻
  → SkillStateMachine.UpdateExecution → OnHitboxFrame 事件
  → SkillExecutor.OnSwordQiFrame(SpawnFrameIndex)
  → 拷贝 DamageBlock → 池取 SwordQiProjectile（origin=角色+偏移，dir=角色前向）
  → EventBus.Emit(SwordQiSpawnedEvent)
  → 每帧 Tick：移动 + FindNearby 宽判定带
  → 命中 → FillDamageContext → TakeDamage（击退走 EffectBlock）
  → 飞满 12m → 回池
```

## 错误处理与边界

- 剑气生成时 `SwordQiConfig` 缺预制体 → `Debug.LogWarning` 并跳过生成（不崩溃、不影响竖劈本体）
- 场景切换直接销毁剑气（挂场景根/池容器下）→ `OnDestroy` 守卫，不回池
- 目标已死亡：`FindNearby` 已过滤 `IsAlive`
- 高度差：水平飞行 + 判定半径 1m 自然补偿；不做追踪（非 homing）
- 并发上限：普攻2 施放间隔 > 剑气飞行时长，同屏剑气 ≤ 2 道；每帧 ≤ 2 次 OverlapSphere（buffer 64），移动端可接受

## 测试

### EditMode（`Assets/Scripts/Tests/Editor/`）

1. `SwordQiProjectile`：`Tick` 步进——移动距离 = 速度 × dt；命中注册到 `PhysicsRegistry` 的无碰撞体假目标（走补充路径距离判断）；同一目标只命中一次；累计距离 ≥ 射程后回池
2. 伤害数值：`DamagePercent × 竖劈全额`（`FillDamageContext` 后 `CalculatedDamage` 断言）
3. `SwordQiConfig` 校验：速度/射程/倍率 > 0，预制体非空（编辑器警告逻辑）
4. `SkillExecutor.OnSwordQiFrame`：满足条件时恰好生成一次（同一帧多次事件防重）；`TryStart` 复位

### PlayMode 冒烟

- 竖劈打出剑气命中假人扣血
- 双段伤害：近处目标被竖劈近战 + 剑气各扣一次
- 打断（受击/取消）后飞行中的剑气继续命中

## 不做的事（YAGNI）

- 不碰 `ProjectileSkillData`（保持闲置；其语义为独立技能弹体）
- 不改 `SkillStateMachine`（触发全在 `SkillExecutor` 订阅层）
- 不发网络消息（只预留事件）
- 不做 homing/追踪、不做穿透计数（带判定命中全部目标即用户确认语义）
- 剑气目标类型硬编码 `EntityType.Monster`（当前唯一场景：玩家打怪），注释说明
- 不做正式美术（先用现有 VFX 材质搭临时月牙，机制先行）
