# Hit Feedback Fix — 击退/特效/事件数据修复

## 问题分析

当前命中反馈系统代码已存在（见 `2026-05-28-hit-feedback-system-design.md`），但存在以下问题导致效果不可见：

### 1. 击退（Knockback）无效

**根因链路：**
- `MeleeWeapon.Attack()` → `t.TakeDamage(_config.Damage, dir)`
- `MonsterEntity.TakeDamage()` → `_ai.NotifyHit(data, hitDirection)`
- `MonsterAI.NotifyHit()` → `_movement.ApplyKnockback(hitDirection, _lastKnockbackForce)`
- `_lastKnockbackForce = damageData?.KnockbackForce ?? 0f` → **默认为 0**

`WeaponConfig.Damage`（DamageBlock）的 `_knockbackForce` 序列化字段默认值为 0，且未在 Inspector 中配置。

**另外**：`HitZone.OnTriggerStay` 只传递 `DamageData`，未将 `AttackHitboxData.KnockbackForce` 合并到 `DamageBlock` 中。

### 2. 事件数据不完整

`MonsterEntity.TakeDamage()` 发射 `MonsterTakeDamageEvent` 时只传了 5 个参数：
```csharp
EventBus.Emit(new MonsterTakeDamageEvent(
    GetInstanceID(),
    transform.position + Vector3.up * 2f,
    hitDirection,
    Mathf.CeilToInt(data.BaseDamage),
    data.WasCritical
    // 缺少 skillId 和 comboIndex
));
```

VFX 系统用 `e.SkillId > 0` 区分普攻/技能，缺少字段导致普攻路径的分层逻辑失效。

### 3. VFX 预制体未赋值

`HitParticleController` 的序列化字段（`_normalHitParticles`, `_criticalHitParticles` 等）在场景中未赋值。预制体文件已存在于 `Assets/Prefabs/VFX/`。

---

## 修复方案：事件驱动击退

### 新增 KnockbackEvent

在 `DamageEvents.cs` 中新增击退事件：

```csharp
public struct KnockbackEvent : IEvent
{
    public int EntityId;
    public Vector3 Direction;
    public float Force;

    public KnockbackEvent(int entityId, Vector3 direction, float force)
    {
        EntityId = entityId;
        Direction = direction;
        Force = force;
    }
}
```

### MonsterEntity.TakeDamage() 发射 KnockbackEvent

```csharp
void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
{
    if (_stats.IsDead) return;
    _stats.TakeDamage(data);
    _ai.NotifyHit(data, hitDirection);

    // 补全事件数据
    EventBus.Emit(new MonsterTakeDamageEvent(
        GetInstanceID(),
        transform.position + Vector3.up * 2f,
        hitDirection,
        Mathf.CeilToInt(data.BaseDamage),
        data.WasCritical,
        0,  // skillId = 0 for normal attacks
        1   // comboIndex = 1
    ));

    // 发射击退事件
    if (data.KnockbackForce > 0)
    {
        EventBus.Emit(new KnockbackEvent(
            GetInstanceID(),
            hitDirection,
            data.KnockbackForce
        ));
    }
}
```

### MonsterEntity 转发 KnockbackEvent

`MonsterAI` 不是 MonoBehaviour，无法直接订阅 EventBus。由 `MonsterEntity` 订阅并转发：

```csharp
// MonsterEntity.cs - OnEnable/OnEnable 中订阅
private void OnEnable()
{
    EventBus.Subscribe<KnockbackEvent>(OnKnockback);
}

private void OnDisable()
{
    EventBus.Unsubscribe<KnockbackEvent>(OnKnockback);
}

private void OnKnockback(KnockbackEvent e)
{
    if (e.EntityId != GetInstanceID()) return;
    _movement.ApplyKnockback(e.Direction, e.Force);
}
```

从 `MonsterAI.NotifyHit()` 中移除 `_movement.ApplyKnockback()` 调用（保留 Hit 状态转换和动画触发）。

### HitZone 传递 KnockbackForce

在 `HitZone.OnTriggerStay` 中，将 `AttackHitboxData.KnockbackForce` 合并到 `DamageBlock`：

```csharp
private void OnTriggerStay(Collider other)
{
    var hitbox = other.GetComponent<IAttackHitbox>();
    if (hitbox == null || !hitbox.IsActive) return;
    if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;

    Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;

    var data = hitbox.CurrentData;
    if (data != null && data.DamageData != null)
    {
        // 将 hitbox 的击退力传递给 DamageBlock
        data.DamageData.KnockbackForce = data.KnockbackForce;
        _owner?.TakeDamage(data.DamageData, hitDir);
    }
}
```

### WeaponConfig.Damage 设置默认击退力

在 Unity Editor 中将 `SwordShieldConfig`（或其他武器配置）的 `Damage.KnockbackForce` 设为 5。

---

## 文件变更汇总

| 文件 | 变更类型 | 描述 |
|------|----------|------|
| `Sys3C/Core/Events/DamageEvents.cs` | 修改 | 新增 `KnockbackEvent` 结构体 |
| `Monster/MonsterEntity.cs` | 修改 | 发射 `KnockbackEvent`，补全 `MonsterTakeDamageEvent` 参数 |
| `Monster/MonsterAI.cs` | 修改 | 订阅 `KnockbackEvent`，从 `NotifyHit` 移除直接击退调用 |
| `Combat/HitZone.cs` | 修改 | 传递 `AttackHitboxData.KnockbackForce` 到 `DamageBlock` |
| `WeaponConfig` asset | Editor | 设置 `Damage.KnockbackForce = 5` |
| `HitParticleController` scene | Editor | 赋值 VFX 预制体引用 |

## 验证步骤

1. 普攻命中怪物 → 怪物应被击退
2. 技能命中怪物 → 怪物应被击退（如果 EffectBlock 有 KnockbackForce）
3. 命中后应看到粒子特效（需在 Editor 中赋值预制体）
4. 暴击应触发更大的粒子和 camera shake
