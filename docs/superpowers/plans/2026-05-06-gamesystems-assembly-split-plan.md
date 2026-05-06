# GameSystems Assembly Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split GameSystems subdirectories into independent asmdef assemblies with proper dependency hierarchy and HybridCLR AOT/Hotfix boundary configuration.

**Architecture:** Extend Sys3C.Core with shared Combat interfaces to break circular dependencies, create independent asmdef for Bag/Combat/Monster/NpcMirror, configure AOT/Hotfix boundaries.

**Tech Stack:** Unity 2022.3.25f1, HybridCLR, KCP

---

## File Structure Overview

```
Assets/Scripts/Hotfix/GameSystems/
├── Sys3C/Core/
│   └── Combat/                    (NEW - shared interfaces)
│       ├── IDamageable.cs         (MOVED from Combat)
│       ├── IAttackHitbox.cs       (NEW)
│       └── AttackHitboxData.cs    (MOVED from Combat)
├── Bag/
│   └── Bag.asmdef                 (NEW)
├── Combat/
│   ├── Combat.asmdef              (NEW)
│   └── (implementation files)
├── Monster/
│   └── Monster.asmdef              (NEW)
└── NpcMirror/
    └── NpcMirror.asmdef            (NEW)
```

---

## Task 1: Extend Core with Combat Interfaces

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageable.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IAttackHitbox.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackHitboxData.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Combat/IDamageable.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitboxData.cs`

- [ ] **Step 1: Create Combat directory**

```bash
mkdir -p "Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat"
```

- [ ] **Step 2: Create IDamageable.cs in Core/Combat**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IDamageable
    {
        void TakeDamage(DamageData damageData, Vector3 hitDirection);
        bool IsAlive { get; }
        Transform Transform { get; }
    }
}
```

- [ ] **Step 3: Create IAttackHitbox.cs in Core/Combat**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IAttackHitbox
    {
        bool IsActive { get; }
        AttackHitboxData CurrentData { get; }
        void Activate(AttackHitboxData data);
        void Deactivate();
    }
}
```

- [ ] **Step 4: Create AttackHitboxData.cs in Core/Combat**

```csharp
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class AttackHitboxData
    {
        public DamageData DamageData { get; set; }
        public float KnockbackForce { get; set; }
        public float LaunchForce { get; set; }
        public float StunDuration { get; set; }
        public bool IsCritical { get; set; }
        public int SourceId { get; set; }
    }
}
```

- [ ] **Step 5: Delete old IDamageable.cs**

```bash
rm "Assets/Scripts/Hotfix/GameSystems/Combat/IDamageable.cs"
```

- [ ] **Step 6: Delete old AttackHitboxData.cs**

```bash
rm "Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitboxData.cs"
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): add Combat shared interfaces to Core layer"
```

---

## Task 2: Update Core.asmdef References

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Core.asmdef`

- [ ] **Step 1: Verify Core.asmdef has no additional references needed**

Current Core.asmdef:
```json
{
    "name": "Hotfix.GameSystems.Sys3C.Core",
    "rootNamespace": "Hotfix.GameSystems.Sys3C.Core",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Core needs reference to Skills for DamageData. Update references:

```json
{
    "name": "Hotfix.GameSystems.Sys3C.Core",
    "rootNamespace": "Hotfix.GameSystems.Sys3C.Core",
    "references": [
        "Hotfix.GameSystems.Skills"
    ],
    ...
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Core.asmdef"
git commit -m "feat(core): add Skills reference for DamageData dependency"
```

---

## Task 3: Create Combat.asmdef

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Combat/Combat.asmdef`

- [ ] **Step 1: Create Combat.asmdef**

```json
{
    "name": "Hotfix.GameSystems.Combat",
    "rootNamespace": "Hotfix.GameSystems.Combat",
    "references": [
        "Hotfix.GameSystems.Sys3C.Core"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Combat/Combat.asmdef"
git commit -m "feat(combat): add Combat.asmdef assembly definition"
```

---

## Task 4: Create Bag.asmdef

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Bag/Bag.asmdef`

- [ ] **Step 1: Create Bag.asmdef**

```json
{
    "name": "Hotfix.GameSystems.Bag",
    "rootNamespace": "Hotfix.GameSystems.Bag",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Bag/Bag.asmdef"
git commit -m "feat(bag): add Bag.asmdef assembly definition"
```

---

## Task 5: Create Monster.asmdef

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/Monster.asmdef`

- [ ] **Step 1: Create Monster.asmdef**

```json
{
    "name": "Hotfix.GameSystems.Monster",
    "rootNamespace": "Hotfix.GameSystems.Monster",
    "references": [
        "Hotfix.GameSystems.Sys3C.Core",
        "Hotfix.GameSystems.Combat"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Monster/Monster.asmdef"
git commit -m "feat(monster): add Monster.asmdef assembly definition"
```

---

## Task 6: Create NpcMirror.asmdef

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirror.asmdef`

- [ ] **Step 1: Create NpcMirror.asmdef**

```json
{
    "name": "Hotfix.GameSystems.NpcMirror",
    "rootNamespace": "Hotfix.GameSystems.NpcMirror",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirror.asmdef"
git commit -m "feat(npc): add NpcMirror.asmdef assembly definition"
```

---

## Task 7: Update Using Statements for IDamageable

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: Update namespace import**

Change:
```csharp
using Hotfix.GameSystems.Combat;
```

To:
```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
```

And update class declaration from `IDamageable` to use fully qualified name.

- [ ] **Step 2: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs"
git commit -m "refactor(combat): update IDamageable reference to Core namespace"
```

---

## Task 8: Update Using Statements for AttackHitboxData

**Files:**
- Modify: Multiple files referencing AttackHitboxData

- [ ] **Step 1: Find all files using AttackHitboxData**

```bash
grep -r "using.*AttackHitboxData\|Hotfix.GameSystems.Combat.AttackHitboxData" Assets/Scripts/Hotfix
```

- [ ] **Step 2: Update each file's using statement**

Change `using Hotfix.GameSystems.Combat;` to `using Hotfix.GameSystems.Sys3C.Core.Combat;` in files that reference AttackHitboxData.

Typical files to update:
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDashComponent.cs`
- `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/EffectData.cs`
- `Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs`
- `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAttackHitbox.cs`
- `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor(core): update AttackHitboxData reference to Core namespace"
```

---

## Task 9: Update Combat Files to Use Core Interfaces

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs`

- [ ] **Step 1: Update AttackHitbox.cs**

Add interface implementation:
```csharp
public class AttackHitbox : MonoBehaviour, IAttackHitbox
```

- [ ] **Step 2: Update PlayerHitZone.cs**

Remove direct Monster reference, use IAttackHitbox:
```csharp
private readonly HashSet<IAttackHitbox> _hitSources = new();
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor(combat): implement IAttackHitbox interface"
```

---

## Task 10: Update Monster Files to Use Core Interfaces

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterHitZone.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAttackHitbox.cs`

- [ ] **Step 1: Update MonsterHitZone.cs**

Change to use IAttackHitbox instead of direct AttackHitbox:
```csharp
private readonly HashSet<IAttackHitbox> _hitSources = new();
var hitbox = other.GetComponent<IAttackHitbox>();
```

- [ ] **Step 2: Update MonsterAttackHitbox.cs**

Implement IAttackHitbox interface:
```csharp
public class MonsterAttackHitbox : MonoBehaviour, IAttackHitbox
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor(monster): implement IAttackHitbox interface"
```

---

## Task 11: Configure HybridCLR AOT/Hotfix Boundary

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Core.asmdef`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Combat/Combat.asmdef`

- [ ] **Step 1: Update Core.asmdef for AOT exclusion**

Add to Core.asmdef:
```json
{
    "name": "Hotfix.GameSystems.Sys3C.Core",
    "hotfixAfterAssemblyLoaded": false,
    ...
}
```

Note: The exact configuration depends on your HybridCLR setup. Typical settings in hybridclr_data/AssemblyExtensionGlobalConfig:

```json
{
    "assemblyDefines": {
        "Hotfix.GameSystems.Sys3C.Core": {
            "hotfixAfterAssemblyLoaded": false,
            "allowCallInheritVirtual": false
        },
        "Hotfix.GameSystems.Combat": {
            "hotfixAfterAssemblyLoaded": false,
            "allowCallInheritVirtual": false
        }
    }
}
```

- [ ] **Step 2: Document HybridCLR configuration**

Add comments to asmdef files or create `docs/hybridclr-config.md` with configuration details.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "config(hybridclr): mark Core and Combat as AOT assemblies"
```

---

## Task 12: Verify Assembly Dependencies

**Files:**
- No file changes, verification only

- [ ] **Step 1: Open Unity Editor**

```bash
start Unity.exe -projectPath "E:\CodeForJob\Cool"
```

- [ ] **Step 2: Check Assembly Definition References**

In Unity Editor:
1. Select each new asmdef file
2. Verify References list shows correct dependencies
3. Check for any circular reference warnings in Console

- [ ] **Step 3: Test compilation**

Build the project and verify all assemblies compile without errors.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: verify assembly dependencies"
```

---

## Verification Checklist

After all tasks complete:

- [ ] Core.asmdef references Skills
- [ ] Combat.asmdef references Core
- [ ] Monster.asmdef references Core and Combat
- [ ] Bag.asmdef has no references
- [ ] NpcMirror.asmdef has no references
- [ ] All using statements updated to Core namespaces
- [ ] No circular dependencies remain
- [ ] Unity compiles without errors
- [ ] HybridCLR loads assemblies correctly

---

## Spec Coverage Check

| Spec Section | Task(s) |
|--------------|---------|
| Core层扩展 Combat 接口 | Task 1-2 |
| 创建新 asmdef | Task 3-6 |
| 更新引用关系 | Task 7-10 |
| HybridCLR 配置 | Task 11 |
| 验证 | Task 12 |

All spec requirements covered. No placeholders found.
