# Skill System Merge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete old skill system, AnimationDriver, HitManager, and AttackFSM. Wire up new Skills.Runtime.SkillCoordinator as the single skill execution path. Add dash fields to SkillData.

**Architecture:** Sys3CEntry creates SkillCoordinator + SkillDashComponent → routes all attack/skill input → SkillExecutor drives SkillStateMachine → OnStateChanged triggers Animator directly via AnimHashes. BaseFSM/HitFSM use Animator directly instead of AnimationDriver.

**Tech Stack:** Unity 2022.3, C#, Hotfix layer

---

### Task 1: Sweep — Delete 10 deprecated files and placeholder code

**Files:**
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDefs.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillRegistry.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillCoordinatorBridge.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/BuffData.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimationDriver.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/HitManager.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs`
- Delete: `Assets/Scripts/Editor/SkillConfigGenerator.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` (remove lines 347-397)

- [ ] **Step 1: Delete the 9 files**

```bash
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDefs.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillRegistry.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillCoordinatorBridge.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/BuffData.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimationDriver.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/HitManager.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs"
rm "Assets/Scripts/Editor/SkillConfigGenerator.cs"
```

- [ ] **Step 2: Remove placeholder classes from SkillExecutor.cs**

Delete lines 347-397 from `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`. These are the placeholder classes `Character`, `CharacterStats`, `ShieldSystem`, `PhysicsSystem`, and `StatusController` at the bottom of the file.

- [ ] **Step 3: Verify no compile errors from orphaned references**

```bash
# After Unity recompiles, check for CS0246 (type not found) errors
# These will be fixed in subsequent tasks
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor: delete old skill system, AnimationDriver, HitManager, AttackFSM

Remove SkillConfig/SkillDefs/SkillRegistry/SkillCoordinatorBridge/BuffData
(Sys3C/Skill), AnimationDriver/HitManager (Sys3C/Animation), AttackFSM
(Sys3C/FSM), SkillConfigGenerator (Editor), and SkillExecutor placeholder
stubs. All replaced by new Skills/ runtime system.
EOF
)"
```

---

### Task 2: Create AnimHashes.cs and AttackState.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimHashes.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackState.cs`

- [ ] **Step 1: Create AnimHashes.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation
{
    public static class AnimHashes
    {
        public static readonly int BaseState = Animator.StringToHash("BaseState");
        public static readonly int AttackState = Animator.StringToHash("AttackState");
        public static readonly int HitState = Animator.StringToHash("HitState");
        public static readonly int IsJumping = Animator.StringToHash("IsJumping");
        public static readonly int IsHit = Animator.StringToHash("IsHit");
        public static readonly int IsDead = Animator.StringToHash("IsDead");
        public static readonly int Attack = Animator.StringToHash("Attack");
        public static readonly int Hit = Animator.StringToHash("Hit");
        public static readonly int Death = Animator.StringToHash("Death");
        public static readonly int Blend = Animator.StringToHash("Blend");

        public const int BaseLayerIndex = 0;
        public const int AttackLayerIndex = 1;
        public const int HitLayerIndex = 2;
    }
}
```

- [ ] **Step 2: Create AttackState.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.FSM
{
    public enum AttackState
    {
        Idle = 0,
        Attacking = 1
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimHashes.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackState.cs
git commit -m "feat: add AnimHashes static class and standalone AttackState enum"
```

---

### Task 3: Add dash fields to SkillData

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs`

- [ ] **Step 1: Add dash header and fields to SkillData**

After the "=== Interruption ===" section (before "=== Cancellation ==="), insert:

```csharp
        [Header("=== Dash ===")]
        [SerializeField] protected float _dashDistance;
        public float DashDistance => _dashDistance;

        [SerializeField] protected float _dashDuration;
        public float DashDuration => _dashDuration;
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs
git commit -m "feat: add DashDistance/DashDuration fields to SkillData"
```

---

### Task 4: Modify SkillExecutor — remove placeholders, add dash integration

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

Note: placeholder removal was done in Task 1 Step 2. This task adds dash support.

- [ ] **Step 1: Add SkillDashComponent field and injection to SkillExecutor**

In the field declarations area (near `_targetCharacter`), add:

```csharp
        private SkillDashComponent _dashComponent;
```

After the existing `SetTargetPosition` method, add:

```csharp
        public void SetDashComponent(SkillDashComponent dashComponent)
        {
            _dashComponent = dashComponent;
        }
```

- [ ] **Step 2: Add dash trigger in OnStateChanged**

Modify the constructor to subscribe to `_stateMachine.OnStateChanged`:

Replace the constructor with:

```csharp
        public SkillExecutor(
            IEffectTarget owner,
            SkillData data,
            SkillInterruptionMatrix interruptionMatrix = null)
        {
            _owner = owner;
            _skillData = data;
            _interruptionMatrix = interruptionMatrix ?? new SkillInterruptionMatrix();
            _stateMachine = new SkillStateMachine(data);

            _stateMachine.OnHitboxFrame += OnHitboxTriggered;
            _stateMachine.OnHitConfirm += OnHitConfirm;
            _stateMachine.OnSkillCompleted += OnSkillComplete;
            _stateMachine.OnSkillInterrupted += OnSkillInterrupt;
            _stateMachine.OnStateChanged += OnStateChanged;
        }

        private void OnStateChanged(SkillSubState newState)
        {
            if (newState == SkillSubState.Execution &&
                _dashComponent != null &&
                _skillData.DashDistance > 0)
            {
                Vector3 dashDir = _owner.transform.forward;
                _dashComponent.StartDash(dashDir, _skillData.DashDistance, _skillData.DashDuration);
            }
        }
```

- [ ] **Step 3: Add using for Sys3C.Skill namespace**

At the top of SkillExecutor.cs, add:

```csharp
using Hotfix.GameSystems.Sys3C.Skill;
```

- [ ] **Step 4: Modify SkillCoordinator to support dash injection**

In `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`, add a dash component field and inject it into executors:

Add field:

```csharp
private SkillDashComponent _dashComponent;
```

Add setter method after the `GetSkillData` method:

```csharp
public void SetDashComponent(SkillDashComponent dashComponent)
{
    _dashComponent = dashComponent;
}
```

In `TryActivateSkill`, after `var executor = new SkillExecutor(_owner, skillData, _interruptionMatrix);`, add:

```csharp
if (_dashComponent != null)
{
    executor.SetDashComponent(_dashComponent);
}
```

Add using at top:

```csharp
using Hotfix.GameSystems.Sys3C.Skill;
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs
git commit -m "feat: add SkillDashComponent injection and dash trigger to SkillExecutor"
```

---

### Task 5: Refactor BaseFSM and HitFSM — replace AnimationDriver with Animator+AnimHashes

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/BaseFSM.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs`

- [ ] **Step 1: Refactor BaseFSM.cs**

Replace all `using Hotfix.GameSystems.Sys3C.Animation;` with `using Hotfix.GameSystems.Sys3C.Animation;` (keep it, AnimHashes is in this namespace). Replace constructor and all `_driver.` calls.

Full replacement file changes:

**Constructor** — change from `AnimationDriver driver` to `Animator animator`:

```csharp
private readonly Animator _animator;
private readonly StateTransitionTable _table;

private BaseState _currentState;
private BaseState? _lockedState;

public BaseState CurrentState => _currentState;
public event Action<BaseState> OnStateChanged;

public BaseFSM(Animator animator, StateTransitionTable table)
{
    _animator = animator;
    _table = table;
    _currentState = BaseState.Idle;
}
```

**Update method** — change `AttackState attackState` param to `bool isAttacking`:

```csharp
public void Update(CharacterData data, bool isAttacking)
```

And in the body, replace `attackState != AttackState.Idle` with `isAttacking`.

**All `_driver.Xxx()` calls** — replace with direct Animator calls:

| Old | New |
|---|---|
| `_driver.SetBaseState(target)` | `_animator.SetInteger(AnimHashes.BaseState, (int)target)` |
| `_driver.SetIsJumping(isJumping)` | `_animator.SetBool(AnimHashes.IsJumping, isJumping)` |
| `_driver.SetBlend(0f)` | `_animator.SetFloat(AnimHashes.Blend, 0f)` |

All other method bodies unchanged, only the `_driver` → `_animator` replacement.

- [ ] **Step 2: Refactor HitFSM.cs**

Replace `AnimationDriver _driver` with `Animator _animator`. Update constructor. Replace all `_driver.` calls.

**Constructor:**

```csharp
private readonly Animator _animator;
// ... other fields unchanged

public HitFSM(Animator animator)
    : this(animator, FSMConfig.Default)
{
}

public HitFSM(Animator animator, FSMConfig config)
{
    _animator = animator;
    _config = config;
    _currentState = HitState.None;
}
```

**TransitionTo method** — replace all `_driver.Xxx()` calls:

| Old | New |
|---|---|
| `_driver.TriggerHit()` | `_animator.SetTrigger(AnimHashes.Hit); _animator.SetBool(AnimHashes.IsHit, true); _animator.SetInteger(AnimHashes.HitState, (int)HitState.Hit);` |
| `_driver.SetHitState(HitState.X)` | `_animator.SetInteger(AnimHashes.HitState, (int)HitState.X)` |
| `_driver.SetHitLayerWeight(x)` | `_animator.SetLayerWeight(AnimHashes.HitLayerIndex, x)` |
| `_driver.TriggerDeath()` | `_animator.SetTrigger(AnimHashes.Death); _animator.SetBool(AnimHashes.IsDead, true); _animator.SetInteger(AnimHashes.HitState, (int)HitState.Death);` |

Remove `using Hotfix.GameSystems.Sys3C.Animation;` import (AnimHashes is in same namespace but we already have it). Keep `using Hotfix.GameSystems.Sys3C.Animation;` since AnimHashes is there.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/BaseFSM.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs
git commit -m "refactor: replace AnimationDriver with Animator+AnimHashes in BaseFSM and HitFSM"
```

---

### Task 6: Refactor StateCoordinator — remove AttackFSM

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs`

- [ ] **Step 1: Rewrite StateCoordinator**

Remove `_attackFSM` field and all AttackFSM-related code. The remaining class:

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Sys3C.Core
{
    public class StateCoordinator
    {
        private readonly object _baseFSM;
        private readonly object _hitFSM;

        private LayerType _activeLayer = LayerType.Base;
        private LayerType _lockedLayer = LayerType.Base;
        private float _resistance = 100f;

        public LayerType ActiveLayer => _activeLayer;
        public bool CanMove => _activeLayer != LayerType.Hit;
        public bool CanAttack => _activeLayer != LayerType.Hit;

        public bool IsImmune
        {
            get
            {
                var hitProp = _hitFSM?.GetType().GetProperty("HasSuperArmor");
                return (bool)(hitProp?.GetValue(_hitFSM) ?? false);
            }
        }

        public StateCoordinator(object baseFSM, object hitFSM)
        {
            _baseFSM = baseFSM;
            _hitFSM = hitFSM;
        }

        public void Initialize()
        {
        }

        public void Update(float deltaTime)
        {
        }

        public bool TryRequestJump()
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (_activeLayer == LayerType.Attack) return false;

            EventBus.Emit(JumpEvent.Start);
            return true;
        }

        public void HandleDamage(DamageEvent damage)
        {
            if (IsImmune) return;

            _resistance -= damage.Damage * 0.5f;
            if (_resistance < 0) _resistance = 0;

            EventBus.Emit(damage);
            EventBus.Emit(new HitReceivedEvent());
        }

        public void HandleDeath()
        {
            SetActiveLayer(LayerType.Hit);

            var lockMethod = _baseFSM?.GetType().GetMethod("LockState");
            lockMethod?.Invoke(_baseFSM, new object[] { 0 });

            LockLayer(LayerType.Base);
        }

        public void HandleResurrect()
        {
            _resistance = 100f;
            UnlockAndReturnToBase();
        }

        public float GetResistance() => _resistance;

        public void RestoreResistance(float amount)
        {
            _resistance = Mathf.Min(_resistance + amount, 100f);
        }

        public Vector3 GetKnockbackDisplacement()
        {
            var method = _hitFSM?.GetType().GetMethod("GetKnockbackDisplacement");
            return (Vector3)(method?.Invoke(_hitFSM, null) ?? Vector3.zero);
        }

        public bool IsInAirHit()
        {
            var stateProp = _hitFSM?.GetType().GetProperty("CurrentState");
            var state = (int)(stateProp?.GetValue(_hitFSM) ?? 0);
            return state == 3; // Launched
        }

        public string GetActiveStateDescription()
        {
            var baseState = _baseFSM?.GetType().GetProperty("CurrentState")?.GetValue(_baseFSM)?.ToString() ?? "null";
            var hitState = _hitFSM?.GetType().GetProperty("CurrentState")?.GetValue(_hitFSM)?.ToString() ?? "null";
            return $"[Layer: {_activeLayer}] Base={baseState}, Hit={hitState}";
        }

        public string GetActiveState()
        {
            if (_activeLayer == LayerType.Base)
            {
                return _baseFSM?.GetType().GetProperty("CurrentState")?.GetValue(_baseFSM)?.ToString() ?? "null";
            }
            else if (_activeLayer == LayerType.Hit)
            {
                return _hitFSM?.GetType().GetProperty("CurrentState")?.GetValue(_hitFSM)?.ToString() ?? "null";
            }
            return "Unknown";
        }

        public void UnlockAndReturnToBase()
        {
            _lockedLayer = LayerType.Base;
            SetActiveLayer(LayerType.Base);

            var unlockMethod = _baseFSM?.GetType().GetMethod("Unlock");
            unlockMethod?.Invoke(_baseFSM, new object[] { 0 });

            EventBus.Emit(new LayerUnlockedEvent(LayerType.Base));
        }

        public void SetActiveLayer(LayerType layer)
        {
            if (_activeLayer != layer)
            {
                var previous = _activeLayer;
                _activeLayer = layer;
                EventBus.Emit(new StateChangedEvent(layer, previous.ToString(), layer.ToString()));
            }
        }

        public void SetAttackLayerActive()
        {
            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);
        }

        private void LockLayer(LayerType layer)
        {
            if (_lockedLayer != layer)
            {
                _lockedLayer = layer;
                EventBus.Emit(new LayerLockedEvent(layer, true));
            }
        }
    }
}
```

Key changes from original:
- Removed `_attackFSM` field, constructor param
- Removed `TryRequestAttack()`, `TryRequestSkill()`, `HasSuperArmor` (AttackFSM part)
- `HandleDamage()`: no `SuperArmorRemaining` check
- `HandleDeath()`: no `ForceIdle` call
- `GetActiveStateDescription()`: no AttackFSM state
- `GetActiveState()`: no AttackFSM layer
- Added `SetAttackLayerActive()` — allows FSMManager/Sys3CEntry to lock into Attack layer without going through AttackFSM

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs
git commit -m "refactor: remove AttackFSM from StateCoordinator, add SetAttackLayerActive"
```

---

### Task 7: Refactor StateBehaviours — remove AnimationDriver static field

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/BaseStateBehaviour.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs`

- [ ] **Step 1: Refactor BaseStateBehaviour.cs**

Remove `private static AnimationDriver _driver;` field. Change SetCallback signature:

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class BaseStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_JumpStart = Animator.StringToHash("JumpStart");
        private static readonly int HASH_JumpAir = Animator.StringToHash("JumpAir");
        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");

        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(Action<string> callback)
        {
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    _onAnimationCompleted?.Invoke("JumpEnd");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }
    }
}
```

- [ ] **Step 2: Refactor HitStateBehaviour.cs**

Remove `private static AnimationDriver _driver;` field. Change SetCallback signature:

```csharp
public static void SetCallback(Action<string> callback)
{
    _onAnimationCompleted = callback;
    _hasTriggeredHitComplete = false;
    _lastNormalizedTime = 0f;
}
```

Everything else unchanged.

- [ ] **Step 3: Refactor AttackStateBehaviour.cs**

Remove `private static AnimationDriver _driver;` field. Change SetCallback. Remove SkillQ/SkillR state hashes and mappings.

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
        private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");

        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(Action<string> callback)
        {
            _onAnimationCompleted = callback;
        }

        private bool IsAttackState(AnimatorStateInfo stateInfo)
        {
            var hash = stateInfo.shortNameHash;
            return hash == HASH_Attack1 || hash == HASH_Attack2;
        }

        private string GetStateName(AnimatorStateInfo stateInfo)
        {
            var stateHash = stateInfo.shortNameHash;
            if (stateHash == HASH_Attack1) return "Attack1";
            if (stateHash == HASH_Attack2) return "Attack2";
            return "Unknown";
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (IsAttackState(stateInfo) && stateInfo.normalizedTime >= 0.95f && stateInfo.normalizedTime < 1.1f)
            {
                _onAnimationCompleted?.Invoke(GetStateName(stateInfo));
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (IsAttackState(stateInfo))
            {
                _onAnimationCompleted?.Invoke(GetStateName(stateInfo));
            }
        }
    }
}
```

Removed: `HASH_SkillQ`, `HASH_SkillR_Start`, `HASH_SkillR_Loop`, and their entries in `IsAttackState()` and `GetStateName()`. Skill completion callbacks now come from SkillStateMachine, not animation state detection.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/BaseStateBehaviour.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs
git commit -m "refactor: remove AnimationDriver static field from StateBehaviours, clean AttackStateBehaviour"
```

---

### Task 8: Refactor FSMManager — remove AttackFSM, use Animator directly

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`

- [ ] **Step 1: Rewrite FSMManager**

Full rewritten file:

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    public class FSMManager
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _characterController;
        private readonly Animator _animator;

        private readonly BaseFSM _baseFSM;
        private readonly HitFSM _hitFSM;
        private readonly StateCoordinator _stateCoordinator;

        public event Action OnJumpEndCompleted;
        public event Action OnHitCompleted;
        public event Action OnDeath;

        public StateCoordinator Coordinator => _stateCoordinator;
        public HitFSM HitFSM => _hitFSM;

        public FSMManager(
            Hotfix.GameSystems.Sys3C.Character.CharacterController characterController,
            Animator animator)
        {
            _characterController = characterController;
            _animator = animator;

            var transitionTable = new StateTransitionTable();
            _baseFSM = new BaseFSM(_animator, transitionTable);
            _hitFSM = new HitFSM(_animator);

            _stateCoordinator = new StateCoordinator(_baseFSM, _hitFSM);
            _stateCoordinator.Initialize();

            _characterController.SetStateCoordinator(_stateCoordinator);

            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnLeftGround += HandleLeftGround;
            _characterController.OnDeath += HandleDeath;

            _baseFSM.OnStateChanged += HandleBaseStateChanged;
            _hitFSM.OnHitComplete += HandleHitComplete;
            _hitFSM.OnDeathComplete += HandleDeathComplete;

            BaseStateBehaviour.SetCallback(HandleAnimationCompleted);
            AttackStateBehaviour.SetCallback(HandleAnimationCompleted);
            HitStateBehaviour.SetCallback(HandleHitAnimationCompleted);
        }

        public void Update(float deltaTime)
        {
            var data = _characterController.Data;

            // Determine if skill is active by checking the active layer from coordinator
            bool isAttacking = _stateCoordinator.ActiveLayer == LayerType.Attack;

            _baseFSM.Update(data, isAttacking);
            _stateCoordinator.Update(deltaTime);

            UpdateBlendParameter(data);
        }

        private void UpdateBlendParameter(CharacterData data)
        {
            if (data.BaseState == BaseState.Idle ||
                data.BaseState == BaseState.Move ||
                data.BaseState == BaseState.Sprint ||
                data.BaseState == BaseState.Locomotion)
            {
                _animator.SetFloat(AnimHashes.Blend, data.MovementSpeed);
            }
        }

        public bool TryJump()
        {
            return _stateCoordinator.TryRequestJump();
        }

        public void HandleDamage(int sourceId, float damage, Vector3 hitDirection,
            float knockbackForce = 0, float launchForce = 0, float stunDuration = 0, bool isCritical = false)
        {
            var damageEvent = new Core.Events.DamageEvent(sourceId, 0, damage, isCritical)
            {
                HitDirection = hitDirection,
                KnockbackForce = knockbackForce,
                LaunchForce = launchForce,
                StunDuration = stunDuration
            };

            _stateCoordinator.HandleDamage(damageEvent);
        }

        public void RequestDeath()
        {
            _stateCoordinator.HandleDeath();
        }

        public void RequestResurrect()
        {
            _stateCoordinator.HandleResurrect();
        }

        public void TriggerHit(float knockbackForce = 0f)
        {
            var hitData = new HitData
            {
                Damage = 10,
                KnockbackForce = knockbackForce,
                HitDirection = Vector3.back
            };
            _hitFSM.EnterHit(hitData);
            _stateCoordinator.SetActiveLayer(LayerType.Hit);
        }

        private void HandleAnimationCompleted(string stateName)
        {
            switch (stateName)
            {
                case "JumpEnd":
                    _characterController.FinishJump();
                    OnJumpEndCompleted?.Invoke();
                    break;
                case "Attack1":
                case "Attack2":
                    _animator.ResetTrigger(AnimHashes.Attack);
                    _characterController.LockMovement = false;
                    _characterController.LockRotation = false;
                    break;
            }
        }

        private void HandleHitAnimationCompleted(string stateName)
        {
            _hitFSM.OnAnimationEnd(stateName);
        }

        private void HandleHitComplete()
        {
            _animator.SetBool(AnimHashes.IsHit, false);
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);
            OnHitCompleted?.Invoke();
        }

        private void HandleDeathComplete()
        {
            OnDeath?.Invoke();
        }

        private void HandleJumpRequested() { }
        private void HandleLanded() { }
        private void HandleLeftGround() { }
        private void HandleDeath()
        {
            _stateCoordinator.HandleDeath();
        }

        private void HandleBaseStateChanged(BaseState state) { }
    }
}
```

Key changes:
- Constructor: takes `Animator` instead of `AnimationDriver`+`Animator`+`AnimationDriver`
- Removed: `AttackFSM`, `SkillDashComponent`, skill/attack methods, `UnlockRotation`
- `Update`: passes `_stateCoordinator.ActiveLayer == LayerType.Attack` as `isAttacking`
- `HandleAnimationCompleted`: simplified, only handles JumpEnd and Attack1/Attack2. Skill animations handled by SkillCoordinator callbacks from Sys3CEntry.
- Skill trigger reset: uses `_animator.ResetTrigger(AnimHashes.Attack)` instead of `_driver.ResetAttackTrigger()`

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs
git commit -m "refactor: remove AttackFSM from FSMManager, use Animator directly"
```

---

### Task 9: Rewrite Sys3CEntry — wire up SkillCoordinator and SkillDashComponent

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Rewrite Sys3CEntry**

Full rewritten file:

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime;

namespace Hotfix.GameSystems.Sys3C
{
    public class Sys3CEntry : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField] private float _maxHP = 100f;
        private float _currentHP;

        bool IDamageable.IsAlive => _currentHP > 0;
        Transform IDamageable.Transform => transform;

        [Header("References")]
        public UnityEngine.CharacterController CharacterController;
        public Animator Animator;

        [Header("Settings")]
        public LayerMask GroundLayer;

        [Header("Skills")]
        [SerializeField] private SkillData[] _characterSkills;

        private Hotfix.GameSystems.Sys3C.Character.CharacterController _cc;
        private FSMManager _fsmManager;
        private SkillCoordinator _skillCoordinator;
        private SkillDashComponent _dashComponent;
        private InputManager _inputManager;
        private ThirdPersonCameraController _camera;
        private CharacterAttackHandler _attackHandler;

        private void Start()
        {
            _currentHP = _maxHP;
            PhysicsRegistry.Instance.Register(this, EntityType.Player);

            if (CharacterController == null)
            {
                Debug.LogError("[Sys3CEntry] CharacterController is null!");
                return;
            }
            if (Animator == null)
            {
                Debug.LogError("[Sys3CEntry] Animator is null!");
                return;
            }

            _cc = new Hotfix.GameSystems.Sys3C.Character.CharacterController(
                transform, CharacterController, GroundLayer);

            _fsmManager = new FSMManager(_cc, Animator);

            _dashComponent = new SkillDashComponent(CharacterController, transform);

            _skillCoordinator = new SkillCoordinator(null); // IEffectTarget placeholder
            _skillCoordinator.SetDashComponent(_dashComponent);
            foreach (var skill in _characterSkills)
            {
                if (skill != null)
                    _skillCoordinator.RegisterSkill(skill);
            }

            _inputManager = GetComponent<InputManager>();
            if (_inputManager == null)
                _inputManager = gameObject.AddComponent<InputManager>();

            _camera = FindObjectOfType<ThirdPersonCameraController>();
            if (_camera != null && _cc != null)
            {
                _camera.Target = transform;
                _camera.SnapToTarget();
            }

            _attackHandler = GetComponent<CharacterAttackHandler>();
            if (_attackHandler == null)
                _attackHandler = gameObject.AddComponent<CharacterAttackHandler>();
        }

        private void Update()
        {
            _inputManager.Update();
            HandleInput();

            Vector3 cameraForward = _camera != null ? _camera.transform.forward : Vector3.forward;
            var command = _inputManager.GetMoveCommand(cameraForward);

            _cc.Update(command);
            _fsmManager.Update(Time.deltaTime);
            _skillCoordinator.Update(Time.deltaTime);
            _dashComponent.Update();

            if (_camera != null)
                _camera.Update();
        }

        private void HandleInput()
        {
            if (_inputManager.IsJumpPressed())
            {
                _cc.RequestJump();
            }

            if (_inputManager.IsAttackPressed())
            {
                int attackId = GetBasicAttackSkillId();
                if (attackId > 0)
                {
                    var input = SkillInput.BasicAttack(attackId, transform.forward);
                    _skillCoordinator.HandleBasicAttackInput(input);
                    _fsmManager.Coordinator.SetAttackLayerActive();
                }
            }

            if (_inputManager.IsSkill2Pressed())
            {
                int skillQId = GetSkillQId();
                if (skillQId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillQId, transform.position + transform.forward * 5f);
                    if (TryActivateSkill(input))
                    {
                        _fsmManager.Coordinator.SetAttackLayerActive();
                        _cc.LockRotation = true;
                        _cc.LockMovement = true;
                    }
                }
            }

            if (_inputManager.IsSkill3Pressed())
            {
                int skillRId = GetSkillRId();
                if (skillRId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillRId, transform.position + transform.forward * 5f);
                    if (TryActivateSkill(input))
                    {
                        _fsmManager.Coordinator.SetAttackLayerActive();
                        _cc.LockRotation = true;
                    }
                }
            }

            if (_inputManager.IsSkill3Released())
            {
                if (_skillCoordinator.CurrentSkill != null &&
                    _skillCoordinator.CurrentSkill.CurrentSubState == Skills.Definition.SkillSubState.Charging)
                {
                    _skillCoordinator.CurrentSkill.ReleaseCharge();
                }
            }
        }

        private bool TryActivateSkill(SkillInput input)
        {
            _skillCoordinator.HandleInput(input);
            return _skillCoordinator.IsSkillActive;
        }

        private int GetBasicAttackSkillId()
        {
            foreach (var skill in _characterSkills)
            {
                if (skill != null && skill.SkillType == Skills.Definition.SkillType.BasicAttack)
                    return skill.SkillId;
            }
            return 0;
        }

        private int GetSkillQId()
        {
            foreach (var skill in _characterSkills)
            {
                if (skill != null && skill.SkillType == Skills.Definition.SkillType.Special)
                    return skill.SkillId;
            }
            return 0;
        }

        private int GetSkillRId()
        {
            bool foundFirst = false;
            foreach (var skill in _characterSkills)
            {
                if (skill != null && skill.SkillType == Skills.Definition.SkillType.Special)
                {
                    if (!foundFirst) { foundFirst = true; continue; }
                    return skill.SkillId;
                }
            }
            return 0;
        }

        void IDamageable.TakeDamage(DamageData data, Vector3 hitDirection)
        {
            if (_currentHP <= 0) return;

            float damage = data != null ? data.BaseDamage : 10f;
            _currentHP -= damage;
            Debug.Log($"[Player] Took {damage} damage, HP: {_currentHP}/{_maxHP}");

            _fsmManager.HandleDamage(sourceId: -1, damage: damage, hitDirection: hitDirection);

            if (_currentHP <= 0)
            {
                _currentHP = 0;
                Debug.Log("[Player] Died!");
            }
        }

        private void OnDestroy()
        {
            PhysicsRegistry.Instance.Unregister(this);
        }
    }
}
```

Key changes:
- Removed: `SkillRegistry`, `HitManager`, `AnimationDriver` creation, `RegisterDefaultSkills()`, `TryUseSkill()`, `SetSkillRMaxDuration()`
- Added: `[SerializeField] SkillData[] _characterSkills`, `SkillCoordinator _skillCoordinator`, `SkillDashComponent _dashComponent`
- `Start()`: creates `SkillCoordinator`, registers skills from `_characterSkills`, creates `SkillDashComponent`
- `Update()`: calls `_skillCoordinator.Update()`, `_dashComponent.Update()`
- `HandleInput()`: routes attack → `_skillCoordinator.HandleBasicAttackInput()`, skill Q → `_skillCoordinator.HandleInput()`, skill R → `_skillCoordinator.HandleInput()`, skill R release → `ReleaseCharge()`
- `TryActivateSkill()`: injects `SkillDashComponent` into executor when dash configured

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat: wire up SkillCoordinator and SkillDashComponent in Sys3CEntry"
```

---

### Task 10: Build verification

- [ ] **Step 1: Refresh Unity assets**

```bash
# Trigger Unity asset refresh and script compilation via MCP
```

Use `assets-refresh` MCP tool, then `console-get-logs` to check for compilation errors.

- [ ] **Step 2: Fix any compilation errors**

Check Unity console for errors. Common issues:
- Missing `using` for `Skills.Runtime.SkillInput` (separate from `Skills.Runtime.SkillCoordinator`)
- `SkillInput` defined in `SkillInputBuffer.cs` under `Hotfix.GameSystems.Skills.Runtime`
- `AnimHashes` is in `Hotfix.GameSystems.Sys3C.Animation` namespace

- [ ] **Step 3: Run edit mode tests**

```bash
# Via MCP: tests-run EditMode
```

- [ ] **Step 4: Final commit if fixes needed**

```bash
git add -A
git commit -m "fix: resolve compilation errors from skill system merge"
```
