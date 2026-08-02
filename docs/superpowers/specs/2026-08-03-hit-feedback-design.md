# 击打反馈（命中部位打击感）设计

**日期:** 2026-08-03
**状态:** 已批准，待实现

## 1. 问题陈述

当前普攻/技能的命中反馈只有扣血飘字（`FloatTextRenderer`）+ 怪物受击闪烁（`HitFlashVFX`）+ 击退。存在三个缺口：

1. **命中点是假的**：`MonsterEntity.EmitDamageEvents` 用 `transform.position + Vector3.up * 1.2f` 作为飘字位置，不是真实接触点。
2. **没有部位概念**：头/躯干/腿命中表现完全相同（一个 `HitZone` 碰撞体），没有任何部位差异。
3. **命中点无打击感特效**：没有火花/粒子表现；`HitStopManager` + `HitFeedbackProfile` 存在但 `EnableVFX = false` 整套停用。

## 2. 需求（用户确认）

| 项 | 决策 |
|---|---|
| 方向 | **双向**：玩家打怪物 + 怪物打玩家 |
| 元素 | 命中火花 + 部位区分；暴击/元素特效强化 |
| 不做 | 打击停顿/屏幕震动（用户未选）、部位影响怪物 AI 反应（用户未选） |
| 部位实现 | **计算式分区**：真实接触点相对怪物包围盒高度分类（不新增碰撞体） |
| 玩家侧 | 接触点火花 + 部位区分 + **玩家受击飘字**（当前没有） |
| 火花风格 | 卡通迸发火花（粒子簇 + 元素染色），暴击更大更亮 |
| 击退 | **部位差异化击退**：头 1.5x / 躯干 1.0x / 腿 0.7x |
| 架构 | 事件驱动独立反馈层（方案 A，用户选定） |
| 性能 | 性能优先（用户强调） |

## 3. 目标架构（TO-BE）

```
HitZone / PlayerHitZone (命中检测)
  ├─ TryGetComponent (零分配) + IsActive + 去重 (前置, 廉价)
  ├─ ownCollider.ClosestPoint(hitbox bounds center) → 真实接触点 (仅首次命中执行一次)
  ├─ HitZoneClassifier.Classify → Head/Torso/Legs (纯数学)
  ├─ 元素色: HitZone=玩家武器色(WeaponVFXController.CurrentElementColor), PlayerHitZone=白
  └─ 组装 HitContact { HitPoint, Part, ElementColor, HasContact }
       └─ IDamageable.TakeDamage(data, hitDir, contact)   ← 3 参重载
            ├─ [MonsterEntity]
            │    ├─ 击退 × 部位倍率 → KnockbackEvent
            │    └─ MonsterTakeDamageEvent{ HitPosition(真实接触点), Part, ... }
            └─ [Sys3CEntry]  (注: PlayerHitZone 现为直连 HandleDamage, 本设计统一改走 TakeDamage)
                 ├─ 击退 × 部位倍率 → HitFSM
                 └─ PlayerTakeDamageEvent{ HitPosition, Part, ... }  ← 新增
                      ↓
            HitFeedbackSystem (单例, 订阅双向事件)
              ├─ 火花池: 8 实例 round-robin, stopAction=Callback 自动回收
              │   位置=HitPoint, 缩放=基础×部位×暴击×技能, 颜色=元素色
              └─ 飘字桥: DisplayEventBridge 新增玩家事件订阅
```

## 4. 新接口与数据

### 4.1 `HitContact` 结构体（新文件 `Sys3C/Core/Combat/HitContact.cs`）

```csharp
public enum HitZonePart { Head = 0, Torso = 1, Legs = 2 }

public struct HitContact
{
    public Vector3 HitPoint;    // 真实接触点（世界坐标）
    public HitZonePart Part;    // 计算式部位
    public Color ElementColor;  // 攻击元素色（默认白）
    public bool HasContact;     // false = 无命中上下文（走默认回退）
    public static readonly HitContact None;  // 显式构造: Part=Torso, HasContact=false
}
```

**关键约束（防呆）**：
- **禁止用 `default(HitContact)` 代替 `None`** —— `HitZonePart.Head = 0`，默认结构体实例的 Part 会是 Head。所有消费端必须显式检查 `HasContact`。
- `None` 的 `HitPoint` 为 (0,0,0) 无意义，消费端在 `HasContact=false` 时必须用接收方自身默认点补算：`transform.position + up × collider.bounds.size.y × 0.6`（躯干高度），再正常分类（Torso）。

### 4.2 `IDamageable` 3 参重载（修改）

```csharp
void TakeDamage(DamageBlock data, Vector3 hitDirection);                      // 保留, 转发 3 参 + HitContact.None
void TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact);  // 新增
```

- **禁用 C# default interface method**（HybridCLR + IL2CPP 下 AOT 兼容风险）——两个实现类（MonsterEntity、Sys3CEntry）显式实现两个重载，2 参转发 3 参传 `HitContact.None`。
- 既有调用点（SkillExecutor:545、MeleeWeapon:38、MonsterAttackState:79、MonsterEntity:334 自伤）**全部不动**，走 2 参 → `None` → 接收方默认点回退。

### 4.3 `HitZoneClassifier`（新文件 `Sys3C/Core/Combat/HitZoneClassifier.cs`）

```csharp
public static class HitZoneClassifier
{
    public static float HeadRatio = 0.75f;   // 相对高度 ≥0.75 → Head
    public static float LegsRatio = 0.35f;   // 相对高度 <0.35 → Legs
    public static float HeadKnockbackMultiplier = 1.5f;
    public static float TorsoKnockbackMultiplier = 1.0f;
    public static float LegsKnockbackMultiplier = 0.7f;

    public static HitZonePart Classify(Vector3 point, Bounds bounds)
    {
        float ratio = (point.y - bounds.min.y) / bounds.size.y;  // 世界空间 AABB
        if (ratio >= HeadRatio) return Head;
        if (ratio < LegsRatio) return Legs;
        return Torso;
    }
    public static float KnockbackMultiplier(HitZonePart part) { ... }
}
```

纯数学、零依赖、公开静态字段调参（5 个常数不值得 ScriptableObject）。

## 5. 事件

### 5.1 `MonsterTakeDamageEvent` 扩展（修改）

**复用现有 `HitPosition` 字段，不新增位置字段**（`DisplayEventBridge` 已在消费 `HitPosition`，加第二个位置字段是冗余）：语义改为真实接触点——`HasContact=true` 填 `contact.HitPoint`，否则填接收方默认点。只新增 `Part` 字段（HitZonePart）。`WasBlocked` 早退逻辑不变。

### 5.2 `PlayerTakeDamageEvent`（新文件 `Sys3C/Core/Events/PlayerTakeDamageEvent.cs`）

字段：`EntityId, HitPosition, Part, HitDirection, Damage`（int，CeilToInt）。字段命名与 `MonsterTakeDamageEvent` 对齐（`HitPosition`）。

**发射点**：`Sys3CEntry.IDamageable.TakeDamage` 非格挡分支（现状没有 `OnDamageProcessed` 回调——那是 damage-pipeline-unification 的未来设计，本 spec 不依赖它）。具体位置：`_fsmManager.HandleDamage(...)` 调用之后、`_currentHP <= 0` 死亡判定之前，加 `_currentHP > 0` 守卫。被格挡（`ReactLevel == None`）/死亡不发射，与怪物侧 `WasBlocked` 早退一致。

## 6. `HitFeedbackSystem`（新单例 `VFX/HitFeedbackSystem.cs`）

- MonoBehaviour 单例（同 `EntityDisplayManager` 模式），`DontDestroyOnLoad`。
- 订阅：`MonsterTakeDamageEvent` + `PlayerTakeDamageEvent`。
- **火花池**：8 个 `HitSpark` 粒子预制体实例（程序化创建，场景初始化时预填充，**运行时永不 new**），round-robin 复用；`ParticleSystemStopAction.Callback` 回调自动回收，**无每帧轮询、无 Update**。
- 每次命中：`SetPosition(HitPoint)`；缩放 = 基础 × 部位倍率（Head 1.3 / Torso 1.0 / Legs 0.8）× 暴击 ×2 × 技能(SkillId>0) ×1.5；颜色 = `contact.ElementColor`（事件带色，避免全局状态流转）。
- 全局 `EnableVFX` 静态开关（沿用 `FloatTextRenderer.EnableVFX` / `HitStopManager.EnableVFX` 先例）。
- **预制体来源与挂载点**：`HitSpark.prefab` 用 `[SerializeField] GameObject` 引用（场景中挂 `HitFeedbackSystem` 的节点上拖引用），不 Addressable、不 Resources.Load——避免异步加载复杂度和资源路径魔法。
- **场景挂载**：`HitFeedbackSystem` 挂场景独立节点（与 `EntityDisplayManager` 同场景、各自独立 GameObject），`DontDestroyOnLoad`。

### 元素色来源

- `WeaponVFXController` 加 `public static Color CurrentElementColor => _currentElementColor`（私有 static 字段 + 只读属性，防外部写；OnEnable 从 `_elementConfig.TrailColor` 读，无配置=白）。单武器客户端够用。
- `HitZone`（玩家打怪）填 `WeaponVFXController.CurrentElementColor`；`PlayerHitZone`（怪打玩家）显式填白。

## 7. 命中点组件改造（`HitZone.cs` / `PlayerHitZone.cs`）

```csharp
private Collider _collider;  // Awake 缓存（现代码只设 isTrigger 未缓存）

private void OnTriggerStay(Collider other)
{
    if (!other.TryGetComponent<IAttackHitbox>(out var hitbox)) return;  // 零分配
    if (!hitbox.IsActive) return;
    if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;   // 去重成功才进入热路径

    Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;
    Vector3 hitPoint = _collider.ClosestPoint(hitbox.GetBounds().center);  // 仅首次命中 1 次
    var contact = new HitContact
    {
        HitPoint = hitPoint,
        Part = HitZoneClassifier.Classify(hitPoint, _collider.bounds),
        ElementColor = <HitZone: 玩家武器色 | PlayerHitZone: 白>,
        HasContact = true
    };
    var data = hitbox.CurrentData;
    if (data != null && data.DamageData != null)
    {
        data.DamageData.KnockbackForce = data.KnockbackForce;  // 保留既有功能行
        _owner?.TakeDamage(data.DamageData, hitDir, contact);
    }
}
```

`[RequireComponent(typeof(Collider))]` 保证 `_collider` 非空。`ClosestPoint` 对胶囊/球/盒是廉价图元数学。

**⚠ PlayerHitZone 现状与图不同**：`PlayerHitZone.OnTriggerStay` 现在**不走 `IDamageable.TakeDamage`**，直连 `_fsmManager.HandleDamage(...)`（且 `_hitSources` 永不清理）。本 spec 要求改造为与 `HitZone` 对称：`PlayerHitZone` 改为调用玩家 `IDamageable.TakeDamage(data.DamageData, hitDir, contact)`（与 `MonsterAttackState:79` 直调 `TakeDamage` 的路径统一到同一入口），去重改用 `_hitInstanceIds` + `ResetHits` 模式（对齐 `HitZone`）。元素色显式填白。

## 8. 击退差异化（两处乘法）

- 怪物侧：`MonsterEntity.TakeDamage` 发 `KnockbackEvent` 时 `force × HitZoneClassifier.KnockbackMultiplier(contact.Part)`。
- 玩家侧：`Sys3CEntry.IDamageable.TakeDamage` 非格挡分支，`knockbackForce × KnockbackMultiplier(contact.Part)` 后再传入 `_fsmManager.HandleDamage`。
- 方向不变，只乘力度。死亡击退（`_brain.LastHitDirection`）不受影响。

## 9. 玩家受击飘字

`DisplayEventBridge` 增加 `PlayerTakeDamageEvent` 订阅 → `_floatText.ShowDamageText(e.EntityId, e.HitPoint, settings, e.Damage)`。复用既有 FloatTextRenderer 池与 merge 逻辑（entityId 用 instanceID，与怪物 ID 空间不冲突）。玩家飘字 settings **复用 `_damageSettings`（与怪物受击同款）**，后续需要差异化时再新增配置。

## 10. 性能设计（审核结论，性能优先）

### 每帧固定开销
| 路径 | 成本 |
|---|---|
| `OnTriggerStay` 空转（接触期间每物理帧） | `TryGetComponent`（零分配原生 API）+ `HashSet.Add` 失败，O(1) 微秒级 |
| 8 个空闲 ParticleSystem | 无存活粒子时近零 |
| HitFeedbackSystem | 零（纯事件驱动，无 Update/轮询） |

### 命中瞬间峰值（最坏：10 目标同时命中）
`ClosestPoint`（胶囊体图元数学，微秒级）+ 部位分类（O(1)）+ 事件（字典+委托）+ 粒子模拟峰值 8×12=96 粒子（低端机 ~100-200μs，Low 档减半）。

### GC 分配
热路径全零分配：`TryGetComponent`、struct `HitContact`、池预填充运行时永不 new；事件对象每次命中 1 个（既有行为，世代 0）。

### 已知边界（写死为限制，不追加复杂度）
1. **火花覆盖折中**：池 8 < 单帧命中 >8 时最旧火花被 round-robin 顶掉（火花寿命 0.2s，视觉可忽略；未来调池大小一个数字）。
2. **MeshCollider 风险**：`ClosestPoint` 对 mesh collider 贵。当前怪物全为胶囊体；未来高模怪需降级（transform 估算），设计保持 collider-agnostic。
3. **OnTriggerStay 空转是既有设计**：已用 `TryGetComponent` 压到最低；`OnTriggerEnter` + `ResetHits` 改造列为后续优化项。
4. **技能直伤路径无部位**：经 `AttackHitbox` 命中的（普攻、Q、R）有真实接触点+部位；`SkillExecutor.ApplyDamage` 直伤路径（远程/范围，若有）v1 只有目标默认点火花，无部位区分。
5. **DOT/自伤路径**（`MonsterEntity:334` 2 参调用）→ `None` → 默认躯干点火花+飘字，无部位。

## 11. 文件清单

### 新增
| 文件 | 内容 |
|---|---|
| `Sys3C/Core/Combat/HitContact.cs` | `HitContact` struct + `HitZonePart` 枚举 |
| `Sys3C/Core/Combat/HitZoneClassifier.cs` | 部位分类 + 击退倍率（纯静态） |
| `Sys3C/Core/Events/PlayerTakeDamageEvent.cs` | 玩家受击事件 |
| `VFX/HitFeedbackSystem.cs` | 单例：火花池 + 事件订阅 |
| `VFX/HitSpark.prefab` | 粒子预制体（程序化创建，burst 8-12 粒子、寿命 ≤0.3s、禁用 collision/noise/trails） |
| `Tests/Editor/HitZoneClassifierTests.cs` | 分类边界 + 倍率回归测试 |

### 修改
| 文件 | 改动 |
|---|---|
| `Sys3C/Core/Combat/IDamageable.cs` | 3 参重载 |
| `Combat/HitZone.cs` | `TryGetComponent`、缓存 `_collider`、接触点+分类+元素色、3 参调用 |
| `Combat/PlayerHitZone.cs` | 同上（元素色=白） |
| `Monster/MonsterEntity.cs` | 3 参实现、事件带真实点、击退×倍率、`None` 默认点回退 |
| `Sys3C/Core/Events/DamageEvents.cs` | `MonsterTakeDamageEvent` 复用 `HitPosition`（值改真实接触点）、+`Part` |
| `Sys3C/Sys3CEntry.cs` | 3 参实现（contact 存进 `DamageContext` 供 `OnDamageProcessed` 使用）、击退×倍率、发射玩家事件 |
| `Nameplate/DisplayEventBridge.cs` | +玩家事件订阅（飘字） |
| `VFX/WeaponVFXController.cs` | +静态 `CurrentElementColor` |

### 不动
`SkillExecutor` / `MeleeWeapon` / `MonsterAttackState`（2 参重载 → `None` 回退）。

### 实现时验证项
- ~~`PlayerHitZone` 是否在实际使用~~ → 已确认为**明确需求**（§7）：`PlayerHitZone` 改造为走玩家 `IDamageable.TakeDamage`，与 `MonsterAttackState:79` 直调 `TakeDamage` 的路径统一到同一入口；两条路径都要接入玩家事件与击退倍率。
- `DamageContext` 增加 `HitContact` 字段的可行性（ref struct 传参）。

## 12. 测试

- `HitZoneClassifierTests`（GameSys.EditorTests，纯逻辑）：
  - 分类边界：ratio=0.8→Head、ratio=0.75→Head（含等于）、ratio=0.5→Torso、ratio=0.3→Legs、ratio=0→Legs
  - 倍率：Head 1.5 / Torso 1.0 / Legs 0.7
  - `HitContact.None` 的 `HasContact == false`、`Part == Torso`
- 手动验证（Editor 播放）：连招打怪不同高度观察火花大小与位置、暴击火花放大、怪物打玩家火花+飘字、旋风斩群战火花数量不超池上限。

## 13. 实施顺序

1. `HitContact` + `HitZoneClassifier` + 单测（先行，纯逻辑）
2. `IDamageable` 重载 + 两个实现类（MonsterEntity / Sys3CEntry）+ `DamageContext.HitContact`
3. `HitZone` / `PlayerHitZone` 改造（TryGetComponent + 接触点 + 分类）
4. 事件扩展/新增 + 发射点改造（真实点位 + 击退倍率）
5. `HitSpark` 预制体 + `HitFeedbackSystem`（池 + 订阅 + 飘字桥）
6. `WeaponVFXController` 静态元素色
7. 全量 EditMode 测试 + 播放验证

## 14. Out of Scope

- HitStop / 相机震动（`HitStopManager` 保持禁用，`HitFeedbackProfile` 不动）
- 部位影响怪物 AI 反应（受击动画/姿态）——仅表现层差异化
- 怪物攻击的暴击/元素判定
- `OnTriggerEnter` 替代 `OnTriggerStay` 的改造
- 命中 decal（冰面残留等）
