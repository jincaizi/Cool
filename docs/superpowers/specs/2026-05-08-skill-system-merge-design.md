# Skill System Merge: Delete Old, Wire Up New

## Goal

Delete all old skill system code and ScriptableObject configs. Wire up the new `Skills/` runtime system as the single skill execution path. Remove `AnimationDriver` (thin Animator wrapper) and `AttackFSM` (hardcoded skill/animation logic).

## Architecture After Merge

```
Sys3CEntry
├── Animator (direct reference, no AnimationDriver)
├── AnimHashes (static StringToHash constants)
├── FSMManager
│   ├── BaseFSM  → Animator + AnimHashes
│   └── HitFSM   → Animator + AnimHashes
│
├── SkillCoordinator (from Skills.Runtime)
│   ├── SkillData[] (loaded per-character, supports multi-character)
│   ├── CooldownManager
│   ├── SkillInputBuffer
│   ├── SkillInterruptionMatrix
│   └── SkillExecutor (created per skill activation)
│       └── SkillStateMachine
│           └── OnStateChanged → Animator.SetTrigger(data.AnimatorTrigger)
│
└── SkillDashComponent (kept, gameplay mechanics)
```

## Skill Flow (Attack + Skill Q/R unified)

```
Input → SkillCoordinator.HandleInput() / HandleBasicAttackInput()
      → SkillExecutor.TryStart()
      → SkillStateMachine: Ready → [Casting] → Execution → Recovery → Completed
      → Execution enter: Animator.SetTrigger(skillData.AnimatorTrigger)
      → StateBehaviour detects anim end → FSMManager callback
      → SkillStateMachine.Complete()
```

All skill types (normal attack, skill Q, skill R) flow through the same `SkillCoordinator` → `SkillExecutor` → `SkillStateMachine` path. Differentiation comes from `SkillData` configuration (SkillType, ComboIndex, AnimatorTrigger, range, etc.).

## Files to Delete (10)

| File | Reason |
|------|--------|
| `Sys3C/Skill/SkillConfig.cs` | Old SO, replaced by Skills/Data/SkillData |
| `Sys3C/Skill/SkillDefs.cs` | String constants, replaced by Skills/Definition/SkillID enum |
| `Sys3C/Skill/SkillRegistry.cs` | Hybrid registry, CD tracking replaced by CooldownManager |
| `Sys3C/Skill/SkillCoordinatorBridge.cs` | Never instantiated; BuffHandler/ActiveBuff unused |
| `Sys3C/Skill/BuffData.cs` | Old buff SO, replaced by Skills/Effect/EffectData |
| `Sys3C/Animation/AnimationDriver.cs` | Thin Animator wrapper (all methods are 1-line forwards) |
| `Sys3C/Animation/HitManager.cs` | Thin wrapper on AnimationDriver |
| `Sys3C/FSM/AttackFSM.cs` | Hardcoded skill/animation logic, replaced by SkillCoordinator |
| `Editor/SkillConfigGenerator.cs` | Generates old SkillConfig assets |
| `Skills/Runtime/SkillExecutor.cs` lines 347-397 | Placeholder Character/CharacterStats/etc stubs |

## Files to Create (2)

`Sys3C/Animation/AnimHashes.cs` — static class with all `Animator.StringToHash()` constants:

```
BaseState, AttackState, HitState, IsJumping, IsHit, IsDead,
Attack, Hit, Death, Blend
```

`Sys3C/FSM/AttackState.cs` — standalone enum file (moved from AttackFSM.cs):

```csharp
public enum AttackState { Idle = 0, Attacking = 1 }
```

## Files to Modify

## Files to Modify

### Skills/Data/SkillData.cs
- Add dash fields under a new `[Header("=== Dash ===")]` section:
  - `_dashDistance` (float, default 0, no dash when 0)
  - `_dashDuration` (float, default 0)
- No other changes to SkillData

### Skills/Runtime/SkillExecutor.cs
- Remove placeholder classes at bottom (lines 347-397: Character, CharacterStats, ShieldSystem, PhysicsSystem, StatusController)
- Add internal `SkillDashComponent` reference, injectable via constructor or setter
- In `OnStateChanged` callback: when entering `SkillSubState.Execution` and `_skillData.DashDistance > 0`, call `_dashComponent.StartDash(owner.transform.forward, _skillData.DashDistance, _skillData.DashDuration)`

### Sys3CEntry.cs
- Remove `SkillRegistry _skillRegistry`, `HitManager _hitManager`, `AnimationDriver` creation
- Add `SkillCoordinator _skillCoordinator`, `SkillDashComponent _dashComponent`
- Initialize `SkillDashComponent` here (moved from FSMManager)
- Load `SkillData[]` per-character via `[SerializeField]`
- Route all attack/skill input to `_skillCoordinator.HandleBasicAttackInput()` / `HandleInput()`
- Remove `RegisterDefaultSkills()`, `TryUseSkill()` (CD checks handled by CooldownManager)
- Inject `SkillDashComponent` into `SkillExecutor` on skill activation
- Call `_dashComponent.Update()` each frame in `Update()`
- Direct Animator access where AnimationDriver was used

### FSMManager.cs
- Remove `AttackFSM _attackFSM`, `SkillDashComponent _dashComponent`, `AnimationDriver` parameter
- Constructor takes `Animator` directly
- Remove `TryAttack()`, `TrySkill()`, `RequestNormalAttack()`, `RequestSkillQ/R()`, `CancelSkillR()`, `CanPlaySkill`, `IsInSkillRState`, `UnlockRotation()`
- Keep: `TryJump()`, `HandleDamage()`, `RequestDeath()`, `RequestResurrect()`
- Remove `OnAttackCompleted`, `OnSkillCompleted`, `OnAttackActivated` events — move to SkillCoordinator callbacks
- StateBehaviour callbacks: keep as-is (callbacks use `Action<string>`, no driver dependency)

### StateCoordinator.cs
- Remove `_attackFSM` field and all AttackFSM reflection
- Remove `TryRequestAttack()`, `TryRequestSkill()` methods
- Remove `HasSuperArmor` (AttackFSM part), `CanAttack` properties
- `HandleDamage()` — remove `SuperArmorRemaining` check (super armor now managed by SkillInterruptionMatrix)
- `HandleDeath()` — remove `ForceIdle` call on AttackFSM
- Constructor: take only `baseFSM` and `hitFSM`

### BaseFSM.cs
- Replace `AnimationDriver _driver` with `Animator _animator`
- All `_driver.SetXxx(y)` → `_animator.SetXxx(AnimHashes.Xxx, y)`
- `Update(CharacterData data, AttackState attackState)` — replace `AttackState` param with `bool isAttacking` (only needs idle vs. busy distinction)

### HitFSM.cs
- Replace `AnimationDriver _driver` with `Animator _animator`
- All `_driver.SetXxx(y)` → `_animator.SetXxx(AnimHashes.Xxx, y)`
- Inline `TriggerHit()`, `TriggerDeath()` logic (currently forwarded through HitManager then AnimationDriver)

### StateBehaviours (AttackStateBehaviour, BaseStateBehaviour, HitStateBehaviour)
- Remove static `AnimationDriver _driver` field (stored but never read — dead field)
- `SetCallback` signature: keep only `Action<string> callback` param. Example: `SetCallback(Action<string> callback)`
- AttackStateBehaviour: remove hardcoded state-name-to-hash mappings for skill states (SkillQ, SkillR_Start, SkillR_Loop); keep only Attack1/Attack2 normal attack states. Skill completion callbacks now come from SkillStateMachine, not from animation state name detection.

### AttackState enum
- Move to `Sys3C/FSM/AttackState.cs` (standalone file) — listed in "Files to Create"
- Reduce to `Idle, Attacking` — specific skill identity driven by triggers from SkillData.AnimatorTrigger, not AttackState integer

### SkillDashComponent integration
- Move initialization from FSMManager → Sys3CEntry (currently FSMManager uses private reflection on CharacterController to get `UnityEngine.CharacterController`)
- Sys3CEntry creates `SkillDashComponent` and injects into `SkillExecutor` when skills are activated
- Dash is triggered by `SkillExecutor` when entering `Execution` state and `SkillData.DashDistance > 0`
- Dash direction uses owner's forward vector; `SkillDashComponent.Update()` is called each frame by Sys3CEntry (same pattern as before)

## Multi-Character Support

Skill loading is per-character-instance. Each `Sys3CEntry` holds its own `SkillData[]`:

```csharp
[SerializeField] private SkillData[] _characterSkills;
```

`SkillCoordinator` is created per-instance with its own skill set. `SkillData` ScriptableObjects are shared assets. No architecture change needed beyond the loading method.

## What Stays

- `Sys3C/Skill/SkillDashComponent.cs` — dash movement mechanics, not skill system
- All `Skills/` files — the new runtime system
- `Sys3C/Network/` — network sync layer
- `BaseFSM.cs`, `HitFSM.cs` — modified but kept
- `FSMManager.cs` — modified but kept
- StateBehaviour classes — modified but kept

## Risks

- **StateCoordinator reflection**: currently accesses AttackFSM via reflection. These calls will be removed. Verify no hidden consumers of StateCoordinator's attack-related methods.
- **Animator parameter names**: must match between AnimHashes constants, SkillData.AnimatorTrigger strings, and StateBehaviour hash checks. The `AttackState` Animator parameter (integer) maps to `Idle=0, Attacking=1`.
- **SkillDashComponent**: currently initialized in FSMManager via private reflection on CharacterController. Move initialization to Sys3CEntry.
- **SkillData dash fields**: added to `SkillData` as part of this merge (`_dashDistance`, `_dashDuration`). `SkillExecutor` reads these and triggers `SkillDashComponent` when entering Execution state.
- **AttackStateBehaviour hardcoded state names**: currently maps Animator state hashes to string names for SkillQ/SkillR. After merge, only Attack1/Attack2 remain; skill completion callbacks come from SkillStateMachine instead.
