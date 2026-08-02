# 击打反馈（命中部位打击感）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 玩家打怪与怪打玩家都获得真实接触点 + 部位分类（头/躯干/腿）的命中火花与飘字，部位差异化击退。

**Architecture:** 事件驱动独立反馈层。`HitContact`（struct，含真实接触点/部位/元素色）随 `IDamageable.TakeDamage` 3 参重载透传；无碰撞箱路径（怪物 AttackShape、技能直伤）由接收方 `HitContact.FromDirection` 方向重建接触点；`HitFeedbackSystem` 单例订阅双向受击事件，8 实例火花池 round-robin（ParticleSystemStopAction.Callback 自动回收，无 Update）。性能：热路径零分配。

**Tech Stack:** Unity 2022.3 LTS、C# Hotfix 层、NUnit EditMode 测试（GameSys.EditorTests）、Unity MCP（assets-refresh / tests-run / 场景操作）。

**Spec:** `docs/superpowers/specs/2026-08-03-hit-feedback-design.md`（已批准 + 计划阶段修订 `6fbecad`）

**测试命令约定**（本计划所有"运行测试"步骤）：
- 改完 `.cs` 后先 `assets-refresh`（触发编译），再用 `tests-run`（EditMode）跑测试。
- 若 tests-run 报"场景有未保存修改"，先保存打开的场景（`scene-save`）再跑。

---

## File Structure

```
Assets/Scripts/Hotfix/GameSystems/
├── Sys3C/Core/Combat/
│   ├── HitContact.cs          (NEW) HitZonePart 枚举 + HitContact struct + FromDirection 工厂
│   ├── HitZoneClassifier.cs   (NEW) 部位分类 + 击退/缩放倍率（纯静态）
│   └── IDamageable.cs         (MOD) +3 参 TakeDamage 重载
├── Sys3C/Core/Events/
│   ├── DamageEvents.cs        (MOD) MonsterTakeDamageEvent +Part +ElementColor
│   └── PlayerTakeDamageEvent.cs (NEW) 玩家受击事件
├── Monster/
│   ├── Monster/Damage/DamageContext.cs (MOD) +Contact 字段
│   ├── Monster/MonsterEntity.cs        (MOD) 3 参实现 + 事件带真实点 + 击退倍率 + FromDirection
│   └── Sys3C/Sys3CEntry.cs             (MOD) 3 参实现 + 玩家事件 + 击退倍率 + collider 缓存
├── Combat/
│   └── HitZone.cs             (MOD) TryGetComponent + 接触点 + 分类 + 元素色 + 3 参调用
│       (PlayerHitZone.cs 不动 —— 休眠代码，见 spec §7)
├── VFX/
│   ├── WeaponVFXController.cs (MOD) +静态 CurrentElementColor
│   ├── HitFeedbackSystem.cs   (NEW) 单例：火花池 + 事件订阅
│   ├── HitSpark.cs            (NEW) 火花实例（stopAction 回调自动回收）
│   └── HitSpark.prefab        (NEW) 粒子预制体
└── Nameplate/
    └── DisplayEventBridge.cs  (MOD) +玩家受击飘字订阅

Assets/Scripts/Tests/Editor/
├── HitZoneClassifierTests.cs  (NEW) 分类边界/倍率/None/FromDirection
└── CharacterAttackHandlerSpinTests.cs (MOD) FakeDeadTarget +3 参实现（接口变更必须）
```

**关键决策（计划阶段确认，已并入 spec）**
- 玩家侧无碰撞箱路径（`MonsterAttackState.ResolveDamage` 用 AttackShape）：`PlayerHitZone` 休眠不动，2 参调用 → `None` → 接收方 `FromDirection` 方向重建（真实部位）。
- `MonsterTakeDamageEvent` 复用现有 `HitPosition`（值改真实点），新增 `Part` + `ElementColor`（事件带色，火花系统不读全局状态）。
- 部位击退/缩放倍率集中 `HitZoneClassifier`（Torso=1.0 即 None 路径恒等，无分支）。
- 接口新增抽象方法 → `FakeDeadTarget`（测试）必须同步实现 3 参。

---

### Task 1: `HitContact` + `HitZoneClassifier` + 单测（纯逻辑，先行）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/HitContact.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/HitZoneClassifier.cs`
- Test: `Assets/Scripts/Tests/Editor/HitZoneClassifierTests.cs`

- [ ] **Step 1: 写失败测试 `HitZoneClassifierTests.cs`**

```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class HitZoneClassifierTests
    {
        // bounds: center (0,0,0) size (1,2,1) → min.y=-1 max.y=1，ratio=(y+1)/2
        private static readonly Bounds Bounds = new Bounds(Vector3.zero, new Vector3(1f, 2f, 1f));

        // ── 分类边界 ──
        [Test]
        public void Classify_RatioAtHeadThreshold_ReturnsHead() =>
            Assert.AreEqual(HitZonePart.Head, HitZoneClassifier.Classify(new Vector3(0f, 0.5f, 0f), Bounds)); // ratio 0.75（含等于）

        [Test]
        public void Classify_RatioAboveHeadThreshold_ReturnsHead() =>
            Assert.AreEqual(HitZonePart.Head, HitZoneClassifier.Classify(new Vector3(0f, 0.6f, 0f), Bounds)); // ratio 0.8

        [Test]
        public void Classify_MidHeight_ReturnsTorso() =>
            Assert.AreEqual(HitZonePart.Torso, HitZoneClassifier.Classify(new Vector3(0f, 0f, 0f), Bounds)); // ratio 0.5

        [Test]
        public void Classify_RatioAtLegsThreshold_ReturnsLegs() =>
            Assert.AreEqual(HitZonePart.Legs, HitZoneClassifier.Classify(new Vector3(0f, -0.4f, 0f), Bounds)); // ratio 0.3

        [Test]
        public void Classify_Bottom_ReturnsLegs() =>
            Assert.AreEqual(HitZonePart.Legs, HitZoneClassifier.Classify(new Vector3(0f, -1f, 0f), Bounds)); // ratio 0

        [Test]
        public void Classify_ZeroHeightBounds_ReturnsTorso() =>
            Assert.AreEqual(HitZonePart.Torso,
                HitZoneClassifier.Classify(Vector3.zero, new Bounds(Vector3.zero, Vector3.zero)));

        // ── 倍率 ──
        [Test]
        public void KnockbackMultiplier_HeadIs15() =>
            Assert.AreEqual(1.5f, HitZoneClassifier.KnockbackMultiplier(HitZonePart.Head));

        [Test]
        public void KnockbackMultiplier_TorsoIsIdentity() =>
            Assert.AreEqual(1.0f, HitZoneClassifier.KnockbackMultiplier(HitZonePart.Torso));

        [Test]
        public void KnockbackMultiplier_LegsIs07() =>
            Assert.AreEqual(0.7f, HitZoneClassifier.KnockbackMultiplier(HitZonePart.Legs));

        [Test]
        public void ScaleMultiplier_HeadIs13() =>
            Assert.AreEqual(1.3f, HitZoneClassifier.ScaleMultiplier(HitZonePart.Head));

        [Test]
        public void ScaleMultiplier_TorsoIsIdentity() =>
            Assert.AreEqual(1.0f, HitZoneClassifier.ScaleMultiplier(HitZonePart.Torso));

        [Test]
        public void ScaleMultiplier_LegsIs08() =>
            Assert.AreEqual(0.8f, HitZoneClassifier.ScaleMultiplier(HitZonePart.Legs));

        // ── HitContact.None 防呆 ──
        [Test]
        public void None_HasNoContactAndIsTorso()
        {
            Assert.IsFalse(HitContact.None.HasContact);
            Assert.AreEqual(HitZonePart.Torso, HitContact.None.Part);
        }

        // ── FromDirection 方向重建（胶囊：center (0,1,0) height 2 radius 0.5 → 底 y=0 顶 y=2）──
        private static CapsuleCollider CreateCapsule()
        {
            var go = new GameObject("TestCapsule");
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.height = 2f;
            col.radius = 0.5f;
            return col;
        }

        [Test]
        public void FromDirection_SideHit_FindsSurfacePointAsTorso()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.right);
                Assert.IsTrue(contact.HasContact);
                Assert.Greater(contact.HitPoint.x, 0f);          // 右侧表面
                Assert.AreEqual(HitZonePart.Torso, contact.Part); // 高度 ≈1 → ratio 0.5
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }

        [Test]
        public void FromDirection_AttackerBelow_ClassifiesLegs()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.up); // 攻击者在下方 → 接触底点
                Assert.AreEqual(HitZonePart.Legs, contact.Part);
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }

        [Test]
        public void FromDirection_AttackerAbove_ClassifiesHead()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.down); // 攻击者在上方 → 接触顶点
                Assert.AreEqual(HitZonePart.Head, contact.Part);
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }

        [Test]
        public void FromDirection_InvalidDirection_FallsBackToTorsoHeight()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.zero);
                Assert.IsTrue(contact.HasContact);
                Assert.AreEqual(HitZonePart.Torso, contact.Part);
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `assets-refresh`，然后 `tests-run`（EditMode，testClass=`GameSys.EditorTests.HitZoneClassifierTests`）
Expected: FAIL —— 编译错误 "HitContact/HitZoneClassifier 不存在"

- [ ] **Step 3: 创建 `HitContact.cs`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 命中部位（相对包围盒高度的计算式分区）
    /// </summary>
    public enum HitZonePart { Head = 0, Torso = 1, Legs = 2 }

    /// <summary>
    /// 命中上下文，随 TakeDamage 透传。注意：HitZonePart.Head = 0，
    /// 禁止用 default(HitContact) 代替 None（默认实例的 Part 会是 Head）。
    /// </summary>
    public struct HitContact
    {
        public Vector3 HitPoint;    // 真实接触点（世界坐标）
        public HitZonePart Part;    // 计算式部位
        public Color ElementColor;  // 攻击元素色（默认白）
        public bool HasContact;     // false = 无命中上下文（接收方用 FromDirection 重建）

        /// <summary>显式空值：Part=Torso、HasContact=false、元素色白</summary>
        public static readonly HitContact None = new HitContact
        {
            Part = HitZonePart.Torso,
            ElementColor = Color.white
        };

        /// <summary>
        /// 从攻击方向重建接触点：取自身 collider 朝攻击反方向的表面最近点，部位照常分类。
        /// 用于无碰撞箱路径（怪物 AttackShape 直调、技能直伤）。方向无效时兜底躯干高度点。
        /// </summary>
        public static HitContact FromDirection(Collider collider, Vector3 hitDirection)
        {
            Vector3 point;
            if (hitDirection.sqrMagnitude < 0.0001f)
            {
                var bounds = collider.bounds;
                point = collider.transform.position + Vector3.up * (bounds.size.y * 0.6f);
            }
            else
            {
                point = collider.ClosestPoint(collider.transform.position - hitDirection * 100f);
            }
            return new HitContact
            {
                HitPoint = point,
                Part = HitZoneClassifier.Classify(point, collider.bounds),
                ElementColor = Color.white,
                HasContact = true
            };
        }
    }
}
```

- [ ] **Step 4: 创建 `HitZoneClassifier.cs`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 命中部位分类与倍率（纯数学、零依赖；公开静态字段便于调参与测试覆写）
    /// </summary>
    public static class HitZoneClassifier
    {
        public static float HeadRatio = 0.75f;   // 相对高度 ≥0.75 → Head
        public static float LegsRatio = 0.35f;   // 相对高度 <0.35 → Legs
        public static float HeadKnockbackMultiplier = 1.5f;
        public static float TorsoKnockbackMultiplier = 1.0f;
        public static float LegsKnockbackMultiplier = 0.7f;
        public static float HeadScaleMultiplier = 1.3f;
        public static float TorsoScaleMultiplier = 1.0f;
        public static float LegsScaleMultiplier = 0.8f;

        public static HitZonePart Classify(Vector3 point, Bounds bounds)
        {
            float sizeY = bounds.size.y;
            if (sizeY <= 0.0001f) return HitZonePart.Torso;  // 退化保护
            float ratio = (point.y - bounds.min.y) / sizeY;   // 世界空间 AABB 相对高度
            if (ratio >= HeadRatio) return HitZonePart.Head;
            if (ratio < LegsRatio) return HitZonePart.Legs;
            return HitZonePart.Torso;
        }

        public static float KnockbackMultiplier(HitZonePart part)
        {
            switch (part)
            {
                case HitZonePart.Head: return HeadKnockbackMultiplier;
                case HitZonePart.Legs: return LegsKnockbackMultiplier;
                default: return TorsoKnockbackMultiplier;
            }
        }

        public static float ScaleMultiplier(HitZonePart part)
        {
            switch (part)
            {
                case HitZonePart.Head: return HeadScaleMultiplier;
                case HitZonePart.Legs: return LegsScaleMultiplier;
                default: return TorsoScaleMultiplier;
            }
        }
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `assets-refresh`，然后 `tests-run`（EditMode）
Expected: PASS —— HitZoneClassifierTests 全部通过（含 3 个 FromDirection 几何测试）

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/HitContact.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/HitContact.cs.meta Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/HitZoneClassifier.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/HitZoneClassifier.cs.meta Assets/Scripts/Tests/Editor/HitZoneClassifierTests.cs Assets/Scripts/Tests/Editor/HitZoneClassifierTests.cs.meta
git commit -m "feat: hit contact + zone classifier with FromDirection reconstruction + tests"
```

---

### Task 2: 事件扩展（MonsterTakeDamageEvent + PlayerTakeDamageEvent）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/PlayerTakeDamageEvent.cs`

- [ ] **Step 1: 扩展 `MonsterTakeDamageEvent`（DamageEvents.cs）**

在文件头部添加 `using Hotfix.GameSystems.Sys3C.Core.Combat;`，然后替换整个 MonsterTakeDamageEvent struct：

```csharp
    /// <summary>
    /// 怪物受伤事件（给浮字/火花系统使用）
    /// </summary>
    public struct MonsterTakeDamageEvent : IEvent
    {
        public int EntityId;
        public Vector3 HitPosition;   // 真实接触点（或 FromDirection 重建点）
        public Vector3 HitDirection;
        public int Damage;
        public bool IsCritical;
        public int SkillId;
        public int ComboIndex;
        public HitZonePart Part;
        public Color ElementColor;

        public MonsterTakeDamageEvent(
            int entityId, Vector3 hitPos, Vector3 hitDir,
            int damage, bool isCritical = false,
            int skillId = 0, int comboIndex = 1,
            HitZonePart part = HitZonePart.Torso, Color elementColor = default)
        {
            EntityId = entityId;
            HitPosition = hitPos;
            HitDirection = hitDir;
            Damage = damage;
            IsCritical = isCritical;
            SkillId = skillId;
            ComboIndex = comboIndex;
            Part = part;
            ElementColor = elementColor;
        }
    }
```

- [ ] **Step 2: 创建 `PlayerTakeDamageEvent.cs`**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 玩家受击事件（给浮字/火花系统使用，字段命名与 MonsterTakeDamageEvent 对齐）
    /// </summary>
    public struct PlayerTakeDamageEvent : IEvent
    {
        public int EntityId;
        public Vector3 HitPosition;
        public HitZonePart Part;
        public Vector3 HitDirection;
        public int Damage;
        public Color ElementColor;

        public PlayerTakeDamageEvent(
            int entityId, Vector3 hitPos, HitZonePart part, Vector3 hitDir,
            int damage, Color elementColor)
        {
            EntityId = entityId;
            HitPosition = hitPos;
            Part = part;
            HitDirection = hitDir;
            Damage = damage;
            ElementColor = elementColor;
        }
    }
}
```

- [ ] **Step 3: 编译验证**

Run: `assets-refresh` → 无编译错误即可（事件 struct 无独立测试，Task 7 集成验证）
Expected: 编译通过（现有 43 测试不受影响——本任务无行为改动，事件只是加字段/新类型）

- [ ] **Step 4: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/PlayerTakeDamageEvent.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/PlayerTakeDamageEvent.cs.meta
git commit -m "feat: extend MonsterTakeDamageEvent with Part/ElementColor, add PlayerTakeDamageEvent"
```

---

### Task 3: `IDamageable` 3 参重载 + 两个接收方（MonsterEntity / Sys3CEntry）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageable.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageContext.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- Modify: `Assets/Scripts/Tests/Editor/CharacterAttackHandlerSpinTests.cs`（FakeDeadTarget 编译修复）

- [ ] **Step 1: `IDamageable.cs` 加重载**

```csharp
        /// <summary>
        /// 接受伤害（无命中上下文，接收方用 FromDirection 重建）
        /// </summary>
        void TakeDamage(DamageBlock damageData, Vector3 hitDirection);

        /// <summary>
        /// 接受伤害（带命中上下文：真实接触点/部位/元素色）
        /// </summary>
        void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact);
```

- [ ] **Step 2: `DamageContext.cs` 加 Contact 字段**

文件头加 `using Hotfix.GameSystems.Sys3C.Core.Combat;`，在 `OverrideDamage` 字段旁加：

```csharp
        // 命中上下文（真实接触点/部位/元素色）；None 时接收方用 FromDirection 重建
        public HitContact Contact;
```

- [ ] **Step 3: `MonsterEntity.cs` 3 参实现**

3a. 添加字段与 Awake 缓存（在现有 Awake 的 `HitZone` 缓存旁）：

```csharp
        private Collider _collider;
        ...
            if (HitZone == null) HitZone = GetComponent<HitZone>();
            if (_collider == null) _collider = GetComponent<Collider>();
```

3b. 替换 `IDamageable.TakeDamage` 为两个重载（整个方法替换）：

```csharp
        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
            => TakeDamage(data, hitDirection, HitContact.None);

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact)
        {
            if (_stats.IsDead) return;

            // None → 方向重建（无碰撞箱路径也能拿真实部位）
            HitContact resolved = contact.HasContact || _collider == null
                ? contact
                : HitContact.FromDirection(_collider, hitDirection);

            int myId = GetInstanceID();

            var ctx = new DamageContext
            {
                RawData = data,
                HitDirection = hitDirection,
                AttackerId = 0,
                Flags = data.WasCritical ? DamageFlags.IsCritical : DamageFlags.None,
                OverrideDamage = data.CalculatedDamage,
                Contact = resolved,
            };

            var result = _damagePipeline.Process(ref ctx);

            _brain.OnDamageReceived(result, hitDirection);

            EmitDamageEvents(myId, data, hitDirection, result, resolved);

            if (result.ShouldKnockback && data.KnockbackForce > 0)
            {
                float force = data.KnockbackForce * HitZoneClassifier.KnockbackMultiplier(resolved.Part);
                EventBus.TargetedEmit(myId, new KnockbackEvent(myId, hitDirection, force));
            }
        }

        private void EmitDamageEvents(int entityId, DamageBlock data, Vector3 hitDirection,
            DamageResult result, HitContact contact)
        {
            // Blocked hits don't show float text
            if (result.WasBlocked) return;

            var displayDamage = Mathf.CeilToInt(result.FinalDamage);
            var damageEvent = new MonsterTakeDamageEvent(
                entityId,
                contact.HitPoint,
                hitDirection,
                displayDamage,
                data.WasCritical,
                data.SkillId,
                data.ComboIndex,
                contact.Part,
                contact.ElementColor
            );
            EventBus.Emit(damageEvent);
            EventBus.TargetedEmit(entityId, damageEvent);
        }
```

注意：原 `EmitDamageEvents` 的签名（5 参）与 `TakeDamage` 里的调用点要同步替换。文件头确认 `using Hotfix.GameSystems.Sys3C.Core.Combat;` 存在（IDamageable 已在用）。

- [ ] **Step 4: `Sys3CEntry.cs` 3 参实现**

4a. 添加字段 + Start 缓存（在现有 Start 组件缓存区，`_inputManager = GetComponent<InputManager>();` 附近）：

```csharp
        private Collider _bodyCollider;
        ...
            _bodyCollider = GetComponent<Collider>();  // 玩家根节点：CharacterController 即 Collider，用于默认接触点
```

4b. 替换 `IDamageable.TakeDamage`（整个方法替换为两个重载）：

```csharp
        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
            => TakeDamage(data, hitDirection, HitContact.None);

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact)
        {
            if (_currentHP <= 0) return;

            float baseDamage = (data != null && data.CalculatedDamage > 0)
                ? data.CalculatedDamage
                : (data?.BaseDamage ?? 10f);

            // None → 方向重建（怪物 AttackShape 直调 2 参路径）
            HitContact resolved = contact.HasContact || _bodyCollider == null
                ? contact
                : HitContact.FromDirection(_bodyCollider, hitDirection);

            // 构建伤害上下文
            var ctx = new DamageContext
            {
                RawData = data,
                HitDirection = hitDirection,
                OverrideDamage = data?.CalculatedDamage ?? 0f,
                CurrentDamage = baseDamage,
                Contact = resolved
            };

            // 防御修正器介入
            var result = _defendModifier?.Modify(ref ctx) ?? new DamageResult
            {
                FinalDamage = baseDamage,
                ShouldKnockback = true,
                ReactLevel = HitReactLevel.Flinch
            };

            float finalDamage = result.FinalDamage;
            bool wasBlocked = result.ReactLevel == HitReactLevel.None;

            // 防御未被触发（背面受击或不在防御状态）→ 退出防御
            if (_cc.IsDefending && !wasBlocked)
            {
                _cc.TryExitDefend();
                _fsmManager.ExitDefend();
            }

            // 格挡成功 → 扣耐久 + 播 DefendHit
            if (_cc.IsDefending && wasBlocked)
            {
                float absorbed = baseDamage - finalDamage;
                bool broken = _cc.AbsorbDamage(absorbed);
                if (broken)
                {
                    _cc.OnShieldBreak();
                    _fsmManager.HandleShieldBreak(new HitData
                    {
                        Damage = finalDamage,
                        HitDirection = hitDirection
                    });
                    // 扣血（盾破时穿透伤害仍要扣除）
                    _currentHP -= finalDamage;
                    if (_currentHP <= 0)
                    {
                        _currentHP = 0;
                        _skillCoordinator.ClearInputBuffer();
                        _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
                    }
                    return;
                }

                _fsmManager.HitFSM.EnterDefendHit(new HitData
                {
                    Damage = finalDamage,
                    HitDirection = hitDirection
                });
                _fsmManager.Coordinator.SetActiveLayer(LayerType.Hit);
            }

            // 扣血
            _currentHP -= finalDamage;

            // 正常受击路由（非格挡时走 HitFSM，击退 × 部位倍率）
            if (!wasBlocked)
            {
                float kb = (data?.KnockbackForce ?? 0f) * HitZoneClassifier.KnockbackMultiplier(resolved.Part);
                _fsmManager.HandleDamage(sourceId: -1, damage: finalDamage, hitDirection: hitDirection,
                    knockbackForce: kb);

                // 玩家受击事件（死亡不发射——死亡判定在下方，用 _currentHP > 0 守卫）
                if (_currentHP > 0)
                {
                    EventBus.Emit(new PlayerTakeDamageEvent(
                        gameObject.GetInstanceID(),
                        resolved.HitPoint,
                        resolved.Part,
                        hitDirection,
                        Mathf.CeilToInt(finalDamage),
                        resolved.ElementColor
                    ));
                }
            }

            if (_currentHP <= 0)
            {
                _currentHP = 0;
                _skillCoordinator.ClearInputBuffer();
                _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
            }
        }
```

文件头确认 `using Hotfix.GameSystems.Sys3C.Core.Combat;`（IDamageable 已在用）与 `using Hotfix.GameSystems.Sys3C.Core.Events;`（EventBus/PlayerTakeDamageEvent）存在。

- [ ] **Step 5: `FakeDeadTarget` 编译修复（CharacterAttackHandlerSpinTests.cs）**

在现有 2 参 TakeDamage 旁加：

```csharp
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection) { }
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact) { }
```

- [ ] **Step 6: 编译 + 全量回归**

Run: `assets-refresh`，然后 `tests-run`（EditMode）
Expected: PASS —— 43 个既有测试全部通过（接口变更无行为回归；FakeDeadTarget 已修）

- [ ] **Step 7: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageable.cs Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageContext.cs Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs Assets/Scripts/Tests/Editor/CharacterAttackHandlerSpinTests.cs
git commit -m "feat: 3-param TakeDamage with HitContact on both receivers, part-scaled knockback, player damage event"
```

---

### Task 4: `WeaponVFXController` 静态元素色

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/VFX/WeaponVFXController.cs`

- [ ] **Step 1: 加静态属性**

在字段区（`_elementConfig` 附近）加：

```csharp
        private static Color _currentElementColor = Color.white;

        /// <summary>当前武器元素色（火花染色用；单武器客户端够用，只读防外部写）</summary>
        public static Color CurrentElementColor => _currentElementColor;
```

- [ ] **Step 2: Awake 末尾写入**

在现有 Awake 的 `_elementConfig == null` 检查之后（`enabled = false; return;` 之后）加：

```csharp
            _currentElementColor = _elementConfig != null ? _elementConfig.TrailColor : Color.white;
```

- [ ] **Step 3: 编译验证**

Run: `assets-refresh` → 无编译错误
Expected: 编译通过（无测试——纯读取属性，Task 7 播放验证）

- [ ] **Step 4: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/WeaponVFXController.cs
git commit -m "feat: expose current weapon element color for hit sparks"
```

---

### Task 5: `HitZone` 改造（真实接触点 + 部位 + 元素色）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs`
- （`PlayerHitZone.cs` 不动——休眠代码，spec §7）

- [ ] **Step 1: 替换 `HitZone.cs` 内容**

```csharp
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.VFX;

namespace Hotfix.GameSystems.Combat
{
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour
    {
        private IDamageable _owner;
        private Collider _collider;
        private readonly HashSet<int> _hitInstanceIds = new();

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
        }

        public void Init(IDamageable owner)
        {
            _owner = owner;
        }

        public void ResetHits()
        {
            _hitInstanceIds.Clear();
        }

        private void OnTriggerStay(Collider other)
        {
            if (!other.TryGetComponent<IAttackHitbox>(out var hitbox)) return;
            if (!hitbox.IsActive) return;
            if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;

            Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;
            Vector3 hitPoint = _collider.ClosestPoint(hitbox.GetBounds().center);  // 仅首次命中 1 次
            var contact = new HitContact
            {
                HitPoint = hitPoint,
                Part = HitZoneClassifier.Classify(hitPoint, _collider.bounds),
                ElementColor = WeaponVFXController.CurrentElementColor,
                HasContact = true
            };

            var data = hitbox.CurrentData;
            if (data != null && data.DamageData != null)
            {
                data.DamageData.KnockbackForce = data.KnockbackForce;  // 保留既有功能行
                _owner?.TakeDamage(data.DamageData, hitDir, contact);
            }
        }
    }
}
```

- [ ] **Step 2: 编译 + 回归**

Run: `assets-refresh`，然后 `tests-run`（EditMode）
Expected: PASS（HitZone 是运行时组件，测试由既有 43 项回归覆盖编译）

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs
git commit -m "feat: HitZone computes real contact point, part classification, element color"
```

---

### Task 6: `HitSpark` 预制体 + `HitFeedbackSystem` + 飘字桥 + 场景挂载

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.prefab`（Unity MCP 创建）
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs`
- Modify: 场景 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`（挂 HitFeedbackSystem）

- [ ] **Step 1: 创建 `HitSpark.cs`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 火花粒子实例：播放一次后由 ParticleSystemStopAction.Callback 自动置为 inactive（池复用，无轮询）
    /// </summary>
    public class HitSpark : MonoBehaviour
    {
        private ParticleSystem _ps;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
        }

        public void Play(Vector3 worldPos, Color color, float scale)
        {
            transform.position = worldPos;
            transform.localScale = Vector3.one * scale;
            var main = _ps.main;
            main.startColor = color;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);  // 池槽位复用前先清
            gameObject.SetActive(true);
            _ps.Play();
        }

        private void OnParticleSystemStopped()
        {
            gameObject.SetActive(false);
        }
    }
}
```

- [ ] **Step 2: 创建 `HitFeedbackSystem.cs`**

```csharp
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 击打反馈系统：订阅双向受击事件播放命中火花。
    /// 8 实例池 round-robin，stopAction 回调自动回收，纯事件驱动无 Update/轮询。
    /// </summary>
    public class HitFeedbackSystem : MonoBehaviour
    {
        [SerializeField] private GameObject _sparkPrefab;
        [SerializeField] private int _poolSize = 8;

        public static bool EnableVFX = true;
        public static HitFeedbackSystem Instance { get; private set; }

        private HitSpark[] _sparks;
        private int _cursor;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _sparks = new HitSpark[_poolSize];
            for (int i = 0; i < _poolSize; i++)
            {
                var go = Instantiate(_sparkPrefab, transform);
                go.SetActive(false);
                _sparks[i] = go.GetComponent<HitSpark>();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterHit);
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnPlayerHit);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterHit);
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnPlayerHit);
        }

        private void OnMonsterHit(MonsterTakeDamageEvent e)
        {
            Spawn(e.HitPosition, e.Part, e.ElementColor, e.IsCritical, e.SkillId);
        }

        private void OnPlayerHit(PlayerTakeDamageEvent e)
        {
            Spawn(e.HitPosition, e.Part, e.ElementColor, isCritical: false, skillId: 0);
        }

        private void Spawn(Vector3 worldPos, HitZonePart part, Color color, bool isCritical, int skillId)
        {
            if (!EnableVFX) return;
            if (_sparks == null || _sparks.Length == 0) return;

            // 缩放 = 基础(1) × 部位倍率 × 暴击 ×2 × 技能 ×1.5
            float scale = HitZoneClassifier.ScaleMultiplier(part);
            if (isCritical) scale *= 2f;
            if (skillId > 0) scale *= 1.5f;

            Color c = color.a <= 0f ? Color.white : color;  // 防御：事件未带有效色时用白

            _sparks[_cursor % _sparks.Length].Play(worldPos, c, scale);
            _cursor++;
        }
    }
}
```

- [ ] **Step 3: `DisplayEventBridge.cs` 加玩家飘字订阅**

```csharp
        public void Enable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
        }

        private void OnPlayerDamaged(PlayerTakeDamageEvent e)
        {
            // 玩家飘字复用怪物同款 settings（后续需要差异化时再新增配置）
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _damageSettings, e.Damage);
        }
```

（`PlayerTakeDamageEvent` 与 `MonsterTakeDamageEvent` 同在 `Hotfix.GameSystems.Sys3C.Core.Events`，文件头 using 已覆盖。）

- [ ] **Step 4: 编译验证**

Run: `assets-refresh`
Expected: 编译通过（HitSpark/HitFeedbackSystem 加入 GameSys 程序集）

- [ ] **Step 5: 创建 `HitSpark.prefab`（Unity MCP）**

用 Unity MCP 按以下步骤创建：

5a. `gameobject-create`：名 `HitSpark`，primitiveType 不选（空 GO）
5b. `gameobject-component-add`：`UnityEngine.ParticleSystem`
5c. `particle-system-modify` 配置 Main 模块：
    - duration=0.25、looping=false、playOnAwake=false
    - startLifetime=0.25（constant）、startSpeed=3、startSize=0.15
    - startColor=白色、simulationSpace=World、maxParticles=32
    - **stopAction=Callback**（关键——回收机制）
5d. `particle-system-modify` 配置 Emission：rateOverTime=0，burst 1 个：count=10、time=0
5e. `particle-system-modify` 配置 Shape：Sphere、radius=0.2
5f. （collision / noise / trails 保持默认关闭）
5g. `gameobject-component-add`：`Hotfix.GameSystems.VFX.HitSpark`
5h. `assets-prefab-create`：保存为 `Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.prefab`，然后删除场景中的临时 GO

验证：`assets-find` filter `HitSpark t:Prefab` 能找到；`animation-`/`particle-system-get` 确认 stopAction=Callback 已写入。

- [ ] **Step 6: 场景挂载（DemoDay.unity）**

6a. `gameobject-create`：名 `HitFeedbackSystem`（场景根）
6b. `gameobject-component-add`：`Hotfix.GameSystems.VFX.HitFeedbackSystem`
6c. `gameobject-component-modify`：设置 `_sparkPrefab` = HitSpark.prefab（assetPath `Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.prefab`）
6d. `scene-save`

- [ ] **Step 7: 回归 + 提交**

Run: `assets-refresh`，然后 `tests-run`（EditMode）
Expected: PASS —— 既有 43 + 新增 17 = 60 项全过（HitZoneClassifierTests 17 项）

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.cs Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.cs.meta Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs.meta Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.prefab Assets/Scripts/Hotfix/GameSystems/VFX/HitSpark.prefab.meta Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: hit spark pool + HitFeedbackSystem, player damage float text, scene wiring"
```

---

### Task 7: 全量验证（EditMode + 播放手测）

**Files:** 无代码改动——验证 + 修复发现的问题

- [ ] **Step 1: 全量 EditMode 回归**

Run: `tests-run`（EditMode）
Expected: PASS —— 全部测试通过（43 既有 + 17 新增 = 60）

- [ ] **Step 2: 播放手测清单（Editor Play Mode）**

| # | 检查项 | 预期 |
|---|---|---|
| 1 | 普攻打怪不同高度（低/中/高） | 火花出现在真实接触点；大小腿<躯干<头 |
| 2 | 暴击 | 火花 ×2 放大（比非暴击明显更亮更大） |
| 3 | 技能 Q/R 命中 | 火花 ×1.5 放大，颜色=武器元素色（冰=蓝） |
| 4 | 怪物打玩家 | 玩家出现火花 + 飘字（复用怪物飘字样式） |
| 5 | 玩家受击部位 | 怪物贴地攻击→腿部位火花；高处命中→头部位 |
| 6 | 旋风斩群战（>8 同时命中） | 火花不报错、数量不超 8 实例（最旧被顶掉可接受） |
| 7 | 格挡 | 无飘字无火花（既有行为保持） |
| 8 | 击杀最后一击 | 火花正常，无异常报错 |

- [ ] **Step 3: 检查 Console 无新增报错**

Run: `console-get-logs`（Error/Exception 过滤，lastMinutes=整个手测期间）
Expected: 无与 HitFeedback/HitSpark/HitZone/PlayerTakeDamage 相关的报错

- [ ] **Step 4: 最终提交（如有手测修复）**

```bash
git add -A
git commit -m "fix: hit feedback play-test fixes"
```

---

## Self-Review（写完后执行）

- **Spec 覆盖**：§4.1/4.2/4.3 → Task 1/3；§5.1/5.2 → Task 2/3；§6 → Task 6；§7 → Task 5（PlayerHitZone 不动已在 spec §7 声明）；§8 → Task 3；§9 → Task 6 Step 3；§11 文件清单 → 全部对应；§12 测试 → Task 1 + Task 7；§13 顺序 → Task 1-7 一致。
- **占位符扫描**：无 TBD/TODO；所有代码步骤含完整代码。
- **类型一致性**：`HitZonePart`/`HitContact`/`HitZoneClassifier`/`PlayerTakeDamageEvent`/`MonsterTakeDamageEvent(Part, ElementColor)` 在 Task 1-6 中签名一致；`EmitDamageEvents` 5→6 参签名在 Task 3 同步替换；`FakeDeadTarget` 双重载与接口一致；`FromDirection(Collider, Vector3)` 在 Task 1 定义、Task 3 使用，参数一致。
- **已知注意点**：`Collider.ClosestPoint` 在 EditMode 下为纯几何查询（不依赖物理模拟），Task 1 的 FromDirection 几何测试依赖此行为——若 Step 2 显示物理类失败，改用纯 `Bounds` 构造测试并手动验证运行时行为。
