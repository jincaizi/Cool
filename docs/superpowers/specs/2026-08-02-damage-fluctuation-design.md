# Damage Fluctuation — Design Spec

**Date:** 2026-08-02  
**Status:** draft (rev 2 — 已按性能与边界情况审核修订)

## Motivation

角色对怪物的伤害计算完全由固定公式得出（base + scaling × ratio），相同的攻击打相同的目标永远是同一个数字。加入可配置的随机波动让伤害有自然起伏，提升战斗反馈感。

## Scope

- 全局百分比伤害波动，作用于所有 `DamageBlock.CalculateFinalDamage` 的调用方（技能伤害、怪物→玩家伤害、Heal 回退路径）
- 配置放在 `GameSettings` ScriptableObject，Inspector 直接调
- 默认值 0（向后兼容）
- 波动后钳制下限 1：任何命中至少造成 1 点伤害

## Design

### Configuration: `GameSettings.DamageFluctuation`

```csharp
// Assets/Scripts/AOT/DataDefinition/GameSettings.cs

[Header("Combat")]
[Tooltip("Damage fluctuation range as a fraction. 0.1 = ±10%")]
[Range(0f, 1f)]
public float DamageFluctuation = 0f;
```

`[Range(0, 1)]` 的 1.0（±100%）是退化配置，钳制下限使其安全；正常使用范围 0.05 ~ 0.2。

### Calculation: `DamageBlock.CalculateFinalDamage`

```csharp
public float CalculateFinalDamage(Effect.IEffectStats attackerStats)
{
    float damage = _baseDamage;

    if (attackerStats != null)
    {
        float scalingValue = attackerStats.GetAttribute(_scalingAttribute);
        damage += scalingValue * _attackRatio;
    }

    // Critical hit — skill-defined rate, no external base chance
    if (_criticalRateBonus > 0 && UnityEngine.Random.value < _criticalRateBonus)
    {
        WasCritical = true;
        damage *= 1.5f + _criticalDamageBonus;
    }
    else
    {
        WasCritical = false;
    }

    if (_isDOT) damage *= _tickInterval;

    // 全局伤害波动 —— 最后一步，只作用于本方法输出
    // （不含调用方在返回值之后叠加的乘数，如 SkillExecutor 的蓄力加成）
    float fluctuation = DataDefinition.GameSettings.Instance.DamageFluctuation;
    if (fluctuation > 0f)
    {
        damage *= 1f + UnityEngine.Random.Range(-fluctuation, fluctuation);
        // 钳制：波动不产生 0 伤害。原因见 Edge Cases §0 伤害钳制
        damage = Mathf.Max(1f, damage);
    }

    return damage;
}
```

**钳制只在 `fluctuation > 0` 分支内** — `fluctuation == 0` 时与现状逐字节等价，向后兼容不变。

完整计算链路：

```
CalculateFinalDamage(attackerStats):
  1. damage = _baseDamage
  2. damage += scalingValue * _attackRatio          // 属性缩放
  3. Critical hit? → damage *= 1.5 + critBonus      // 暴击
  4. DOT? → damage *= _tickInterval                 // DOT
  5. Fluctuation? → damage *= (1 ± fluctuation)     // [NEW] 波动
  6. Clamp → damage = Max(1, damage)                // [NEW] 下限钳制
  7. return damage
```

### Data Flow

```
GameSettings.asset (Resources/Setting/)
  └─ DamageFluctuation = 0.1
       │
       ▼
DamageBlock.CalculateFinalDamage(stats)
  └─ reads GameSettings.Instance.DamageFluctuation
  └─ applies Random.Range(-0.1, +0.1) → clamp ≥ 1
       │
       ├─ SkillExecutor.ApplyDamage() → CalculatedDamage
       │    ├─ IDamageable.TakeDamage(damageBlock, hitDir)
       │    │    ├─ MonsterEntity.TakeDamage → DamagePipeline → HP -= result
       │    │    └─ Sys3CEntry.TakeDamage → DefendModifier → HP -= result
       │    └─ 非 IDamageable 目标 → target.Heal(-damage)     // 同样吃波动
       │
       └─ PlayerHitZone.OnTriggerStay → CalculateFinalDamage(null)
            └─ 怪物→玩家伤害（_fsmManager.HandleDamage）       // 同样吃波动
```

波动对玩家与怪物对称生效 — "所有 DamageBlock 计算的最终伤害"的全局语义。

### Behavior Examples

| Base Damage | Fluctuation | Possible Range |
|-------------|-------------|----------------|
| 100 | 0 (default) | 100 |
| 100 | 0.05 | 95 ~ 105 |
| 100 | 0.1 | 90 ~ 110 |
| 100 | 0.2 | 80 ~ 120 |
| 1 | 0.1 | 1（roll 到 0.9 被钳制回 1） |

## Performance

- **零分配、无每帧工作**：波动只在伤害结算瞬间执行，不进 Update
- **每 hit 至多 2 次 Random**（暴击 roll + 波动 roll），与现状（暴击已 ≥1 次）同量级；`Random.Range(-f, f)` 与 `Random.value` 同代价
- **`GameSettings.Instance` 首次访问是同步 `Resources.Load`**：若第一次命中发生在战斗中可能瞬时卡顿，建议进入战斗前预热一次；之后的每次访问只是静态属性空检查 + 字段读取，成本可忽略
- **不缓存波动值到 static**：保持 Inspector 即时调参（见 Hot-reload 条目），缓存收益 ≈ 0，不值得

## Network & Determinism

- **现状**：伤害在攻击方本地结算，`CalculatedDamage` 走本地事件（`MonsterTakeDamageEvent`），单机 / 演示环境一致
- **风险**：`UnityEngine.Random` 无种子。客户端预测、服务器权威结算、回放三者对同一命中会 roll 出不同数值 → 双方 HP 漂移
- **约定**：服务器权威化时，波动在服务器侧 roll，命中只同步最终伤害值；客户端仅展示。确定性回放/测试需要时见 Future Work
- 本 spec 不改变现有伤害同步路径，只保证波动发生在计算阶段、随 `CalculatedDamage` 传递

## Edge Cases

- **0 伤害钳制**：波动后 `Mathf.Max(1f, damage)`。两个原因：
  1. 小伤害 hit 不消失 — 1 点伤害 ±10% 可能 roll 成 0.9，`MonsterStats.ApplyDamage` 对 `damage <= 0` early-return，造成无事件、无飘字、无击退的"空挥"
  2. 规避 `DamageContext.RawDamage => OverrideDamage > 0 ? OverrideDamage : RawData.BaseDamage` 的 0 值回退 bug — 波动把 `CalculatedDamage` 压到 0 时，管线会回退读取原始 `_baseDamage`，与设计意图相反（0 变成吃满基础伤害）。钳制后该路径不可能触发
- **DOT**：`DamageBlock` 的 `_isDOT/_tickInterval` 目前没有任何 tick 循环驱动（实际 DOT 在 `EffectData.BuffEffectData` 系，不走此方法）。波动位于方法末尾，未来 DOT 若按 tick 调用此方法，自动获得每 tick 独立波动
- **每 hit 独立波动**：`CalculateFinalDamage` 每次调用各自 roll。AOE 技能对每个目标独立波动（每目标一次 Random），不是"每技能一次"的统一乘数 — 与现状暴击 roll 的行为一致
- **Critical**：暴击先乘、波动后乘（乘法可交换，顺序无实际影响）；暴击伤害同样被波动
- **Charge 边界**：`SkillExecutor.ApplyDamage` 在 `CalculateFinalDamage` 返回后乘 `(1 + chargeProgress × 0.5)`，蓄力加成部分不受波动；波动只作用于 DamageBlock 输出
- **Heal 回退**：非 IDamageable 目标走 `target.Heal(-damage)`，同样吃波动 — 属统一语义，可接受
- **Zero fluctuation**：完全跳过 Random 与钳制，与现有行为一致
- **均值无偏**：uniform ±f 的乘数均值 = 1.0，长期 DPS 不变（波动不改总伤害期望）— 这是均匀分布的设计保证
- **Hot-reload**：`GameSettings` 是 AOT ScriptableObject，每次调用实时读取 `Instance`，Inspector 修改即时生效

## Files Touched

| File | Change |
|------|--------|
| `Assets/Scripts/AOT/DataDefinition/GameSettings.cs` | Add `DamageFluctuation` field |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs` | Apply fluctuation + clamp at end of `CalculateFinalDamage` |

## Future Work

- **可种子 `DamageRoller`**：服务器权威 / 回放 / 确定性测试需要共享种子或服务器侧 roll 时引入，现在不实现
- **三角分布（可选）**：若手感需要"更稳定"（方差减半、均值仍 1.0），可换 `Random.value + Random.value` 的三角分布 — 一行变更，不阻塞当前设计
