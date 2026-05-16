# Target Selection Ring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a visual selection ring under the current attack target, re-parented to the target transform for zero-cost following.

**Architecture:** One ring GameObject on the player (SelectionRing component). On hit, re-parent the ring to the target transform at `localPosition = (0, yOffset, 0)`. On target switch/death, re-parent back to player and hide. Subscription management stays in CharacterAttackHandler — SelectionRing is purely visual.

**Tech Stack:** Unity MonoBehaviour, SpriteRenderer, Unlit/Transparent material, existing `role_picSele.png` texture

---

### Task 1: MonsterConfig + MonsterEntity — ring Y offset config

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: Add `RingYOffset` to MonsterConfig**

In `MonsterConfig.cs`, add after the `[Header("Loot & Death")]` block:

```csharp
[Header("Selection Ring")]
[Tooltip("选中光环的脚底Y轴偏移量，用于调整光环在目标脚下的高度位置")]
public float RingYOffset = -0.9f;
```

- [ ] **Step 2: Expose Config property on MonsterEntity**

In `MonsterEntity.cs`, add after `private MonsterConfig _config;`:

```csharp
public MonsterConfig Config => _config;
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "feat: add RingYOffset to MonsterConfig, expose Config on MonsterEntity"
```

---

### Task 2: SelectionRing — new component (visual only)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/SelectionRing.cs`

- [ ] **Step 1: Create SelectionRing.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C
{
    /// <summary>
    /// 唯一天地光环 — 视觉层
    /// 挂在玩家角色的 RingVisual 子物体上
    /// </summary>
    public class SelectionRing : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _ringRenderer;
        private Transform _originalParent;

        private void Awake()
        {
            _originalParent = transform;
            if (_ringRenderer == null)
                _ringRenderer = GetComponentInChildren<SpriteRenderer>();
            if (_ringRenderer != null)
                _ringRenderer.enabled = false;
        }

        /// <summary>re-parent 到目标脚下并显示</summary>
        public void AttachTo(Transform target, float yOffset)
        {
            if (_ringRenderer == null) return;
            transform.SetParent(target);
            transform.localPosition = new Vector3(0, yOffset, 0);
            transform.localRotation = Quaternion.identity;
            _ringRenderer.enabled = true;
        }

        /// <summary>re-parent 回玩家并隐藏</summary>
        public void Detach()
        {
            if (_ringRenderer != null)
                _ringRenderer.enabled = false;
            transform.SetParent(_originalParent);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/SelectionRing.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/SelectionRing.cs.meta
git commit -m "feat: add SelectionRing component"
```

---

### Task 3: IWeapon + MeleeWeapon — return hit targets

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IWeapon.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs`

- [ ] **Step 1: Change IWeapon.Attack return type to `List<IDamageable>`**

In `IWeapon.cs`:

```csharp
using System.Collections.Generic;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum WeaponType
    {
        Melee = 0,
        Ranged = 1,
    }

    public interface IWeapon
    {
        WeaponType WeaponType { get; }
        bool CanAttack();
        List<IDamageable> Attack(Vector3 forward, LayerMask targetMask);
        WeaponConfig Config { get; }
    }
}
```

- [ ] **Step 2: Update MeleeWeapon.Attack to return hit buffer**

In `MeleeWeapon.cs`, change `Attack` signature and return:

```csharp
public List<IDamageable> Attack(Vector3 forward, LayerMask targetMask)
{
    if (_config == null) return _hitBuffer;

    var shape = AttackShapeFactory.Create(_config.AttackShape, PhysicsRegistry.Instance, EntityType.Monster);
    _hitBuffer.Clear();
    shape.ResolveNonAlloc(transform.position, forward, targetMask, _hitBuffer);

    if (_hitBuffer.Count == 0)
    {
        Debug.Log("[Attack] Miss - no target in range");
        return _hitBuffer;
    }

    if (_config.Damage == null) return _hitBuffer;

    foreach (var t in _hitBuffer)
    {
        Vector3 dir = (t.Transform.position - transform.position).normalized;
        t.TakeDamage(_config.Damage, dir);
        Debug.Log($"[Attack] Hit {t.Transform.name} for {_config.Damage.BaseDamage} damage");
    }

    _attackCooldownTimer = 1f / _config.AttackSpeed;
    return _hitBuffer;
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IWeapon.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs
git commit -m "feat: IWeapon.Attack returns list of hit targets"
```

---

### Task 4: CharacterAttackHandler — integrate selection ring

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs`

- [ ] **Step 1: Rewrite CharacterAttackHandler**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Monster;

namespace Hotfix.GameSystems.Sys3C
{
    public class CharacterAttackHandler : MonoBehaviour
    {
        [SerializeField] private LayerMask _targetMask = -1;
        [SerializeField] private SelectionRing _selectionRing;

        private IWeapon _currentWeapon;
        private ITargetable _currentTarget;
        private Action _onTargetDeath;

        private void Start()
        {
            _currentWeapon = GetComponent<IWeapon>();
            if (_currentWeapon == null)
                _currentWeapon = GetComponentInChildren<IWeapon>();
        }

        public void EquipWeapon(IWeapon weapon) => _currentWeapon = weapon;

        public void OnAttackActivated()
        {
            if (_currentWeapon == null || !_currentWeapon.CanAttack()) return;
            var hits = _currentWeapon.Attack(transform.forward, _targetMask);
            if (hits.Count > 0)
                SelectTarget(hits[0]);
        }

        public void SelectTarget(IDamageable target)
        {
            if (!(target is ITargetable targetable) || targetable == _currentTarget) return;
            if (_selectionRing == null) return;

            // Unsubscribe old target's death event
            if (_currentTarget != null && _onTargetDeath != null)
            {
                _currentTarget.OnDeath -= _onTargetDeath;
                _onTargetDeath = null;
            }

            _selectionRing.Detach();

            float yOffset = -0.9f;
            if (target is MonsterEntity monster && monster.Config != null)
                yOffset = monster.Config.RingYOffset;

            _currentTarget = targetable;
            _onTargetDeath = () =>
            {
                if (_currentTarget != null)
                    _currentTarget.OnDeath -= _onTargetDeath;
                _selectionRing.Detach();
                _currentTarget = null;
                _onTargetDeath = null;
            };
            _currentTarget.OnDeath += _onTargetDeath;

            _selectionRing.AttachTo(target.Transform, yOffset);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs
git commit -m "feat: integrate selection ring into CharacterAttackHandler"
```

---

### Task 5: SkillCoordinator + Sys3CEntry — wire skill hits to ring

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Add OnTargetHit event to SkillCoordinator**

In `SkillCoordinator.cs`, add after line 36 (`public event Action<int, float> OnCooldownUpdate;`):

```csharp
public event Action<IEffectTarget> OnTargetHit;
```

In `TryActivateSkill`, after the line `executor.OnSkillInterrupted += (source) => OnExecutorInterrupted(skillId, source);`:

```csharp
executor.OnTargetHit += (target) => OnTargetHit?.Invoke(target);
```

- [ ] **Step 2: Wire skill hits in Sys3CEntry**

In `Sys3CEntry.cs`, in `Start()`, after the line `_skillCoordinator.OnSkillActivated += HandleSkillActivated;`:

```csharp
_skillCoordinator.OnTargetHit += (target) =>
{
    if (target is IDamageable damageable)
        _attackHandler.SelectTarget(damageable);
};
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat: wire skill hits to target selection ring"
```

---

### Task 6: Scene setup — add ring GameObject to player

**Files:**
- Modify player GameObject in scene/prefab (manual Unity Editor operation)

- [ ] **Step 1: Create ring child object on player**

In Unity Editor:
1. Select the player GameObject (the one with `Sys3CEntry`)
2. Create child: `GameObject > Create Empty` → name "RingVisual"
3. Add `SpriteRenderer` component
4. Set `Sprite` to `Assets/PreRes/Texture/Com/role_picSele.png`
5. Set material to `Sprites/Default` (or create an Unlit/Transparent material)
6. Set `Transform > Scale` to `(1.5, 1.5, 1)` — adjust to taste after testing
7. Set `Transform > Position` to `(0, 0, 0)` (local, relative to player)
8. Disable the SpriteRenderer (uncheck in Inspector)
9. Add `SelectionRing` component on "RingVisual"
10. Drag the SpriteRenderer into `_ringRenderer` field
11. On the player's `CharacterAttackHandler`, drag "RingVisual" into `_selectionRing` field

- [ ] **Step 2: Verify in Play mode**

Attack an enemy → ring appears at target's feet. Attack a different enemy → ring switches. Enemy dies → ring disappears.

- [ ] **Step 3: Commit scene/prefab**

```bash
git add <player-prefab-or-scene>
git commit -m "feat: add SelectionRing to player"
```
