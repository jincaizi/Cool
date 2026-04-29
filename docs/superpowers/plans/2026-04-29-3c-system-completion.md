# 3C System Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the 3C system by implementing FSMManager split (BaseFSM + AttackFSM), StateTransitionTable, AnimationDriver integration, and full network prediction.

**Architecture:** 
- FSMManager becomes a coordinator managing BaseFSM and AttackFSM
- State rules externalized to StateTransitionTable
- AnimationDriver provides instance-based callbacks for StateMachineBehaviour
- CharacterController integrates NetworkPrediction for client-side prediction

**Tech Stack:** Unity 2022 LTS, C#, HybridCLR Hotfix

---

## File Structure

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Animation/
│   ├── AnimationDriver.cs         [CREATE] - Already exists
│   ├── HitManager.cs              [MODIFY] - Integrate with AnimationDriver
│   └── StateBehaviours/
│       ├── BaseStateBehaviour.cs  [MODIFY] - Instance callbacks
│       ├── AttackStateBehaviour.cs [MODIFY] - Instance callbacks
│       └── HitStateBehaviour.cs  [MODIFY] - Instance callbacks
├── Character/
│   ├── CharacterController.cs     [MODIFY] - Network integration + RequestJump flag
│   ├── CharacterData.cs          [MODIFY] - Add RequestJump field
│   └── GroundDetector.cs         [NO CHANGE]
├── FSM/
│   ├── FSMManager.cs             [MODIFY] - Coordinator pattern
│   ├── BaseFSM.cs                [CREATE] - Base layer state machine
│   ├── AttackFSM.cs              [CREATE] - Attack layer state machine
│   ├── StateTransitionTable.cs   [CREATE] - Externalized transition rules
│   └── States/
│       ├── IState.cs             [NO CHANGE] - Keep for compatibility
│       └── (other state files)   [NO CHANGE] - Keep for compatibility
├── Network/
│   ├── NetworkBridge.cs          [MODIFY] - Add input methods for CharacterController
│   ├── NetworkPrediction.cs      [NO CHANGE]
│   ├── PositionInterpolator.cs   [NO CHANGE]
│   └── MovementPolicy.cs         [CREATE] - Movement policy interface + implementations
└── Sys3CEntry.cs                 [MODIFY] - Update initialization
```

---

## Task 1: Update CharacterData - Add RequestJump Flag

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs:1-57`

- [ ] **Step 1: Read existing CharacterData.cs**

Read the current file to understand its structure.

- [ ] **Step 2: Add RequestJump to CharacterData struct**

Add after `public bool IsDead;`:
```csharp
        public bool IsDead;
        public bool RequestJump;  // 新增：跳跃请求标记
    }
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs
git commit -m "feat(3c): add RequestJump flag to CharacterData"
```

---

## Task 2: Create StateTransitionTable

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/StateTransitionTable.cs`

- [ ] **Step 1: Create StateTransitionTable.cs**

```csharp
using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 状态转换条件委托
    /// </summary>
    public delegate bool TransitionCondition(CharacterData data, AttackState attackState);

    /// <summary>
    /// 单个转换规则
    /// </summary>
    public struct StateTransition
    {
        public BaseState TargetState;
        public TransitionCondition Condition;
        public float Priority;

        public StateTransition(BaseState target, TransitionCondition condition, float priority = 0)
        {
            TargetState = target;
            Condition = condition;
            Priority = priority;
        }
    }

    /// <summary>
    /// 状态转换表 — 外部化的状态规则配置
    /// </summary>
    public class StateTransitionTable
    {
        private readonly Dictionary<BaseState, List<StateTransition>> _transitions;

        public StateTransitionTable()
        {
            _transitions = new Dictionary<BaseState, List<StateTransition>>();
            Initialize();
        }

        private void Initialize()
        {
            // Idle
            _transitions[BaseState.Idle] = new List<StateTransition>
            {
                new StateTransition(BaseState.Move, d => d.MoveDir.sqrMagnitude > 0.01f, 1),
                new StateTransition(BaseState.Sprint, d => d.MoveDir.sqrMagnitude > 0.01f && d.IsSprint, 2),
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // Move
            _transitions[BaseState.Move] = new List<StateTransition>
            {
                new StateTransition(BaseState.Idle, d => d.MoveDir.sqrMagnitude < 0.01f, 1),
                new StateTransition(BaseState.Sprint, d => d.IsSprint, 2),
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // Sprint
            _transitions[BaseState.Sprint] = new List<StateTransition>
            {
                new StateTransition(BaseState.Idle, d => d.MoveDir.sqrMagnitude < 0.01f, 1),
                new StateTransition(BaseState.Move, d => !d.IsSprint, 2),
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpStart → JumpAir（自动）
            _transitions[BaseState.JumpStart] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpAir, d => true, 0),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpAir → JumpEnd（落地检测）
            _transitions[BaseState.JumpAir] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpEnd, d => d.IsGrounded && d.Velocity.y <= 0, 0),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpEnd
            _transitions[BaseState.JumpEnd] = new List<StateTransition>
            {
                new StateTransition(BaseState.Idle, d => true, 1),
                new StateTransition(BaseState.Move, d => d.MoveDir.sqrMagnitude > 0.01f, 2),
                new StateTransition(BaseState.Sprint, d => d.MoveDir.sqrMagnitude > 0.01f && d.IsSprint, 3)
            };

            // Death
            _transitions[BaseState.Death] = new List<StateTransition>();
        }

        public BaseState? Evaluate(BaseState current, CharacterData data, AttackState attackState)
        {
            if (!_transitions.TryGetValue(current, out var transitions))
                return null;

            transitions.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (var t in transitions)
            {
                if (t.Condition(data, attackState))
                    return t.TargetState;
            }

            return null;
        }

        public bool CanEnter(BaseState target, CharacterData data)
        {
            switch (target)
            {
                case BaseState.Idle:
                case BaseState.Move:
                case BaseState.Sprint:
                    return data.IsGrounded && !data.IsDead;

                case BaseState.JumpStart:
                    return data.IsGrounded && !data.IsDead && data.RequestJump;

                case BaseState.JumpAir:
                    return !data.IsDead;

                case BaseState.JumpEnd:
                    return !data.IsDead;

                case BaseState.Death:
                    return true;

                default:
                    return false;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/StateTransitionTable.cs
git commit -m "feat(3c): add StateTransitionTable for externalized state rules"
```

---

## Task 3: Create BaseFSM

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C\FSM\BaseFSM.cs`

- [ ] **Step 1: Create BaseFSM.cs**

```csharp
using System;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 底层状态机 — 管理移动/跳跃/死亡
    /// </summary>
    public class BaseFSM
    {
        private readonly AnimationDriver _driver;
        private readonly StateTransitionTable _table;
        
        private BaseState _currentState;
        private BaseState? _lockedState;

        public BaseState CurrentState => _currentState;
        public event Action<BaseState> OnStateChanged;

        public BaseFSM(AnimationDriver driver, StateTransitionTable table)
        {
            _driver = driver;
            _table = table;
            _currentState = BaseState.Idle;
        }

        public void Update(CharacterData data, AttackState attackState)
        {
            if (_lockedState.HasValue)
            {
                if (_currentState != _lockedState.Value)
                    ForceState(_lockedState.Value);
                return;
            }

            var target = _table.Evaluate(_currentState, data, attackState);
            if (target.HasValue && target.Value != _currentState)
            {
                if (_table.CanEnter(target.Value, data))
                {
                    TransitionTo(target.Value);
                }
            }
        }

        public void ForceState(BaseState target)
        {
            if (_currentState != target)
            {
                _currentState = target;
                _driver.SetBaseState(target);
                OnStateChanged?.Invoke(target);
                Debug.Log($"[BaseFSM] ForceState: {_currentState}");
            }
        }

        public void LockState(BaseState state)
        {
            _lockedState = state;
            ForceState(state);
        }

        public void Unlock(BaseState defaultState = BaseState.Idle)
        {
            _lockedState = null;
            if (_currentState == BaseState.Death)
            {
                _currentState = defaultState;
                _driver.SetBaseState(_currentState);
            }
        }

        private void TransitionTo(BaseState target)
        {
            _currentState = target;
            _driver.SetBaseState(target);
            
            bool isJumping = target == BaseState.JumpStart 
                          || target == BaseState.JumpAir 
                          || target == BaseState.JumpEnd;
            _driver.SetIsJumping(isJumping);
            
            OnStateChanged?.Invoke(target);
            Debug.Log($"[BaseFSM] Transition: {_currentState}");
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/BaseFSM.cs
git commit -m "feat(3c): add BaseFSM for base layer state management"
```

---

## Task 4: Create AttackFSM

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C\FSM\AttackFSM.cs`

- [ ] **Step 1: Create AttackFSM.cs**

```csharp
using System;
using Hotfix.GameSystems.Sys3C.Animation;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 攻击状态机 — 管理普攻连击和技能
    /// </summary>
    public class AttackFSM
    {
        private readonly AnimationDriver _driver;
        
        private AttackState _currentState;
        
        private int _comboCount;
        private int _framesInState;
        private bool _comboUnlocked;
        
        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;

        public AttackState CurrentState => _currentState;

        public AttackFSM(AnimationDriver driver)
        {
            _driver = driver;
            _currentState = AttackState.Idle;
        }

        public void Update(float deltaTime)
        {
            if (_currentState == AttackState.Idle)
            {
                _comboCount = 0;
                _framesInState = 0;
                _comboUnlocked = false;
            }
            else
            {
                _framesInState++;
                
                if (!_comboUnlocked && _framesInState >= 5)
                {
                    _comboUnlocked = true;
                }
            }
        }

        public void RequestNormalAttack()
        {
            if (_currentState == AttackState.Idle)
            {
                _currentState = AttackState.Attack1;
                _comboCount = 1;
                _driver.SetAttackState(_currentState);
                _driver.TriggerAttack();
                Debug.Log("[AttackFSM] RequestAttack: Attack1");
            }
            else if (_currentState == AttackState.Attack1 && _comboUnlocked)
            {
                _currentState = AttackState.Attack2;
                _comboCount = 2;
                _driver.SetAttackState(_currentState);
                _driver.TriggerAttack();
                Debug.Log("[AttackFSM] RequestAttack: Attack2");
            }
        }

        public void RequestSkillQ()
        {
            if (_currentState == AttackState.Idle || CanInterrupt())
            {
                _currentState = AttackState.SkillQ;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillQ();
                Debug.Log("[AttackFSM] RequestSkillQ");
            }
        }

        public void RequestSkillR(bool isGrounded)
        {
            if (!isGrounded) return;
            
            if (_currentState == AttackState.Idle || CanInterrupt())
            {
                _currentState = AttackState.SkillR;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillR();
                Debug.Log("[AttackFSM] RequestSkillR");
            }
        }

        public void OnAnimationCompleted(string stateName)
        {
            switch (stateName)
            {
                case "Attack1":
                case "Attack2":
                    ReturnToIdle();
                    OnAttackCompleted?.Invoke();
                    break;
                case "SkillQ":
                case "SkillR":
                    ReturnToIdle();
                    OnSkillCompleted?.Invoke();
                    break;
            }
        }

        public void ReturnToIdle()
        {
            if (_currentState != AttackState.Idle)
            {
                _currentState = AttackState.Idle;
                _driver.SetAttackState(_currentState);
                Debug.Log("[AttackFSM] ReturnToIdle");
            }
        }

        public void ForceIdle()
        {
            ReturnToIdle();
        }

        private bool CanInterrupt()
        {
            return _currentState == AttackState.Idle;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs
git commit -m "feat(3c): add AttackFSM for attack layer state management"
```

---

## Task 5: Refactor FSMManager as Coordinator

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C\FSM\FSMManager.cs:1-188`

- [ ] **Step 1: Read existing FSMManager.cs**

Read the current implementation to understand what needs to change.

- [ ] **Step 2: Replace FSMManager.cs content**

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// FSM 协调者 — 管理 BaseFSM 和 AttackFSM
    /// 只负责协调，不处理具体状态逻辑
    /// </summary>
    public class FSMManager
    {
        private readonly CharacterController _characterController;
        private readonly AnimationDriver _driver;
        
        private readonly BaseFSM _baseFSM;
        private readonly AttackFSM _attackFSM;
        private readonly StateTransitionTable _transitionTable;

        public event Action OnJumpEndCompleted;
        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;

        public FSMManager(CharacterController characterController, Animator animator)
        {
            _characterController = characterController;
            _driver = new AnimationDriver(animator);

            _transitionTable = new StateTransitionTable();
            _baseFSM = new BaseFSM(_driver, _transitionTable);
            _attackFSM = new AttackFSM(_driver);

            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnDeath += HandleDeath;

            _baseFSM.OnStateChanged += HandleBaseStateChanged;
            _attackFSM.OnAttackCompleted += () => OnAttackCompleted?.Invoke();
            _attackFSM.OnSkillCompleted += () => OnSkillCompleted?.Invoke();

            BaseStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            AttackStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            HitStateBehaviour.SetCallback(_driver, HandleHitCompleted);

            Debug.Log("[FSMManager] Initialized");
        }

        public void Update(float deltaTime)
        {
            var data = _characterController.Data;

            _baseFSM.Update(data, _attackFSM.CurrentState);
            _attackFSM.Update(deltaTime);
        }

        public void RequestNormalAttack()
        {
            _attackFSM.RequestNormalAttack();
        }

        public void RequestSkillQ()
        {
            _attackFSM.RequestSkillQ();
        }

        public void RequestSkillR()
        {
            _attackFSM.RequestSkillR(_characterController.IsGrounded);
        }

        public void TriggerHit()
        {
            _attackFSM.ForceIdle();
            _driver.TriggerHit();
            _driver.SetIsHit(true);
            _driver.SetHitLayerWeight(1f);
        }

        private void HandleAnimationCompleted(string stateName)
        {
            Debug.Log($"[FSMManager] AnimationCompleted: {stateName}");
            
            switch (stateName)
            {
                case "JumpEnd":
                    _characterController.FinishJump();
                    OnJumpEndCompleted?.Invoke();
                    break;
                case "Attack1":
                case "Attack2":
                case "SkillQ":
                case "SkillR":
                    _attackFSM.OnAnimationCompleted(stateName);
                    break;
            }
        }

        private void HandleHitCompleted()
        {
            _driver.SetIsHit(false);
            _driver.SetHitLayerWeight(0f);
        }

        private void HandleJumpRequested()
        {
            // Jump 由 CharacterController 处理
        }

        private void HandleLanded()
        {
            // 落地由 BaseFSM 检测
        }

        private void HandleDeath()
        {
            Debug.Log("[FSMManager] HandleDeath");
            _baseFSM.LockState(BaseState.Death);
            _attackFSM.ForceIdle();
        }

        private void HandleBaseStateChanged(BaseState state)
        {
            // 可扩展：通知其他系统
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs
git commit -m "refactor(3c): FSMManager as coordinator managing BaseFSM + AttackFSM"
```

---

## Task 6: Update BaseStateBehaviour - Instance Callbacks

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C\Animation\StateBehaviours\BaseStateBehaviour.cs:1-54`

- [ ] **Step 1: Read existing BaseStateBehaviour.cs**

- [ ] **Step 2: Replace content**

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

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                Debug.Log("[BaseBehaviour] JumpEnd entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[BaseBehaviour] JumpEnd completed, normalizedTime=" + stateInfo.normalizedTime);
                    _onAnimationCompleted?.Invoke("JumpEnd");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                Debug.Log("[BaseBehaviour] JumpEnd exited");
            }
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/BaseStateBehaviour.cs
git commit -m "refactor(3c): BaseStateBehaviour uses instance callbacks via AnimationDriver"
```

---

## Task 7: Update AttackStateBehaviour - Instance Callbacks

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C\Animation\StateBehaviours\AttackStateBehaviour.cs:1-78`

- [ ] **Step 1: Read existing AttackStateBehaviour.cs**

- [ ] **Step 2: Replace content**

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
        private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");

        private const int COMBO_FRAME_LOCK = 5;
        private const float COMBO_WINDOW_START = 0.3f;
        private const float COMBO_WINDOW_END = 0.8f;

        private int _framesInState;
        private bool _comboUnlocked;

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Attack1 || stateInfo.shortNameHash == HASH_Attack2)
            {
                _framesInState = 0;
                _comboUnlocked = false;
                Debug.Log("[AttackBehaviour] " + stateInfo.shortNameHash + " entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            _framesInState++;

            if (!_comboUnlocked && _framesInState >= COMBO_FRAME_LOCK)
            {
                _comboUnlocked = true;
                Debug.Log("[AttackBehaviour] Combo unlocked at frame " + _framesInState);
            }

            if (stateInfo.shortNameHash == HASH_Attack1)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[AttackBehaviour] Attack1 completed");
                    _onAnimationCompleted?.Invoke("Attack1");
                }
            }
            else if (stateInfo.shortNameHash == HASH_Attack2)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[AttackBehaviour] Attack2 completed");
                    _onAnimationCompleted?.Invoke("Attack2");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            Debug.Log("[AttackBehaviour] " + stateInfo.shortNameHash + " exited");
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs
git commit -m "refactor(3c): AttackStateBehaviour uses instance callbacks via AnimationDriver"
```

---

## Task 8: Update HitStateBehaviour - Instance Callbacks

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C\Animation\StateBehaviours\HitStateBehaviour.cs:1-47`

- [ ] **Step 1: Read existing HitStateBehaviour.cs**

- [ ] **Step 2: Replace content**

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class HitStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Hit)
            {
                Debug.Log("[HitBehaviour] Hit entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Hit)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[HitBehaviour] Hit completed");
                    _onAnimationCompleted?.Invoke("Hit");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // Hit 动画结束后，Layer 权重归零，自然返回
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs
git commit -m "refactor(3c): HitStateBehaviour uses instance callbacks via AnimationDriver"
```

---

## Task 9: Update HitManager - Integrate with AnimationDriver

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C\Animation\HitManager.cs:1-62`

- [ ] **Step 1: Read existing HitManager.cs**

- [ ] **Step 2: Replace content**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation
{
    public class HitManager
    {
        private readonly AnimationDriver _driver;
        private const int HIT_LAYER_INDEX = 2;

        public HitManager(AnimationDriver driver)
        {
            _driver = driver;
        }

        public void TriggerHit()
        {
            _driver.TriggerHit();
            _driver.SetIsHit(true);
            _driver.SetHitLayerWeight(1f);
            Debug.Log("[HitManager] TriggerHit called");
        }

        public void OnHitCompleted()
        {
            _driver.SetIsHit(false);
            _driver.SetHitLayerWeight(0f);
            Debug.Log("[HitManager] OnHitCompleted");
        }

        public float GetHitLayerWeight()
        {
            return _driver.GetHitLayerWeight();
        }

        public void SetHitLayerWeight(float weight)
        {
            _driver.SetHitLayerWeight(weight);
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/HitManager.cs
git commit -m "refactor(3c): HitManager integrated with AnimationDriver"
```

---

## Task 10: Update CharacterController - Network Integration + RequestJump Flag

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C\Character\CharacterController.cs:1-227`

- [ ] **Step 1: Read existing CharacterController.cs**

- [ ] **Step 2: Replace content**

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Network;

namespace Hotfix.GameSystems.Sys3C.Character
{
    public class CharacterController
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Transform _transform;
        private readonly GroundDetector _groundDetector;

        public float MoveSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 8.0f;
        public float RotationSpeed { get; set; } = 10.0f;
        public float Gravity { get; set; } = -30f;
        public float JumpForce { get; set; } = 12f;

        private CharacterData _data;
        private Vector3 _velocity;
        private bool _jumpRequested;

        public event Action OnJumpRequested;
        public event Action OnLanded;
        public event Action OnDeath;

        public CharacterData Data => _data;
        public bool IsGrounded => _groundDetector.IsGrounded();

        // Network integration
        private NetworkPrediction _prediction;
        private NetworkBridge _bridge;
        private uint _currentSequence;

        public CharacterController(
            Transform transform,
            UnityEngine.CharacterController controller,
            LayerMask groundLayer)
        {
            _transform = transform;
            _controller = controller;
            _groundDetector = new GroundDetector(controller);

            _data = new CharacterData
            {
                Position = transform.position,
                Rotation = transform.rotation,
                BaseState = BaseState.Idle,
                IsGrounded = true,
                IsDead = false,
                RequestJump = false
            };
        }

        public void InitializeNetwork(NetworkBridge bridge)
        {
            _bridge = bridge;
            _prediction = new NetworkPrediction();
            Debug.Log("[CharacterController] Network initialized");
        }

        public void RequestJump()
        {
            if (_data.IsGrounded && !_data.IsDead && _data.BaseState != BaseState.JumpStart && _data.BaseState != BaseState.JumpAir)
            {
                _jumpRequested = true;
            }
        }

        public void ApplyHit()
        {
            // Hit 由 FSM 层处理
        }

        public void ApplyDeath()
        {
            _data.IsDead = true;
            _data.BaseState = BaseState.Death;
            _velocity.y = 0f;
            OnDeath?.Invoke();
        }

        public void Update(MoveCommand command)
        {
            if (_data.IsDead)
            {
                _data.Position = _transform.position;
                _data.Rotation = _transform.rotation;
                return;
            }

            // 设置 RequestJump 标记
            _data.RequestJump = _jumpRequested;

            bool wasGrounded = _data.IsGrounded;

            // 1. 应用水平移动
            float currentSpeed = command.IsSprint ? SprintSpeed : MoveSpeed;
            Vector3 moveVelocity = command.MoveDir * currentSpeed;
            moveVelocity.y = _velocity.y;
            _controller.Move(moveVelocity * Time.deltaTime);

            // 2. 检测地面
            _data.IsGrounded = _groundDetector.IsGrounded();

            // 3. 走下悬崖检测
            if (wasGrounded && !_data.IsGrounded && _data.BaseState != BaseState.JumpStart && _data.BaseState != BaseState.JumpAir)
            {
                _velocity.y = 0f;
            }

            // 4. 处理跳跃请求
            if (_jumpRequested && _data.IsGrounded)
            {
                _velocity.y = JumpForce;
                _jumpRequested = false;
                _data.BaseState = BaseState.JumpStart;
                OnJumpRequested?.Invoke();
            }

            // 5. 应用重力
            if (_data.BaseState == BaseState.JumpStart || _data.BaseState == BaseState.JumpAir || !_data.IsGrounded)
            {
                _velocity.y += Gravity * Time.deltaTime;
                _velocity.y = Mathf.Max(_velocity.y, -50f);

                Vector3 yMove = Vector3.up * _velocity.y * Time.deltaTime;
                _controller.Move(yMove);
            }
            else if (_data.IsGrounded)
            {
                _velocity.y = 0f;
            }

            // 6. 跳跃阶段转换
            UpdateJumpPhase();

            // 7. 基础移动状态
            UpdateBaseState(command, currentSpeed);

            // 8. 同步数据
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;
            _data.Velocity = _controller.velocity;
            _data.VerticalVelocity = _velocity.y;
            _data.IsSprint = command.IsSprint;

            // 9. 网络预测
            if (_prediction != null && _bridge != null)
            {
                _prediction.RecordPredictedFrame(_currentSequence, _data.Position, _data.Rotation);
                _bridge.SendInput(command, _currentSequence);
                
                if (_bridge.HasServerUpdate(out var seq, out var pos, out var rot))
                {
                    if (_prediction.ValidateAndCorrect(seq, pos, rot, out var corrected, out var correctedRot))
                    {
                        ApplyServerPosition(corrected.Position, correctedRot);
                    }
                }
                
                _currentSequence++;
            }
        }

        private void UpdateJumpPhase()
        {
            if (_data.BaseState == BaseState.JumpStart)
            {
                _data.BaseState = BaseState.JumpAir;
            }
            else if (_data.BaseState == BaseState.JumpAir)
            {
                if (_data.IsGrounded && _velocity.y <= 0)
                {
                    _data.BaseState = BaseState.JumpEnd;
                    _velocity.y = 0f;
                    OnLanded?.Invoke();
                }
            }
        }

        private void UpdateBaseState(MoveCommand command, float currentSpeed)
        {
            if (_data.BaseState == BaseState.Idle ||
                _data.BaseState == BaseState.Move ||
                _data.BaseState == BaseState.Sprint)
            {
                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
                    _transform.rotation = Quaternion.Slerp(
                        _transform.rotation,
                        command.Rotation,
                        RotationSpeed * Time.deltaTime
                    );

                    if (command.IsSprint)
                        _data.BaseState = BaseState.Sprint;
                    else
                        _data.BaseState = BaseState.Move;
                }
                else
                {
                    _data.BaseState = BaseState.Idle;
                }
            }
        }

        public void FinishJump()
        {
            if (_data.BaseState == BaseState.JumpEnd)
            {
                _data.BaseState = BaseState.Idle;
            }
        }

        public void ApplyServerPosition(Vector3 position, Quaternion rotation)
        {
            // Rubber-band 平滑校正
            _transform.position = Vector3.Lerp(_transform.position, position, 0.5f);
            _transform.rotation = Quaternion.Slerp(_transform.rotation, rotation, 0.5f);

            _controller.enabled = false;
            _controller.transform.position = position;
            _controller.enabled = true;

            _data.Position = position;
            _data.Rotation = rotation;
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs
git commit -m "feat(3c): CharacterController with network prediction integration"
```

---

## Task 11: Create MovementPolicy

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C\Network\MovementPolicy.cs`

- [ ] **Step 1: Create MovementPolicy.cs**

```csharp
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 移动策略接口
    /// </summary>
    public interface IMovementPolicy
    {
        void Update(MoveCommand command);
        void ApplyServerCorrection(Vector3 position, Quaternion rotation);
    }

    /// <summary>
    /// 本地模式（无网络）
    /// </summary>
    public class LocalMovementPolicy : IMovementPolicy
    {
        private readonly CharacterController _controller;

        public LocalMovementPolicy(CharacterController controller)
        {
            _controller = controller;
        }

        public void Update(MoveCommand command)
        {
            _controller.Update(command);
        }

        public void ApplyServerCorrection(Vector3 position, Quaternion rotation)
        {
            // 本地模式不使用服务端校正
        }
    }

    /// <summary>
    /// 预测模式（客户端预测 + 服务端校正）
    /// </summary>
    public class PredictionMovementPolicy : IMovementPolicy
    {
        private readonly CharacterController _controller;
        private readonly NetworkPrediction _prediction;
        private readonly NetworkBridge _bridge;
        private uint _sequence;

        public PredictionMovementPolicy(CharacterController controller, NetworkBridge bridge)
        {
            _controller = controller;
            _bridge = bridge;
            _prediction = new NetworkPrediction();
        }

        public void Update(MoveCommand command)
        {
            // 执行本地物理
            _controller.Update(command);

            // 记录预测帧
            _prediction.RecordPredictedFrame(_sequence, _controller.Data.Position, _controller.Data.Rotation);

            // 发送输入
            _bridge.SendInput(command, _sequence);

            _sequence++;
        }

        public void ApplyServerCorrection(Vector3 position, Quaternion rotation)
        {
            _controller.ApplyServerPosition(position, rotation);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/MovementPolicy.cs
git commit -m "feat(3c): add MovementPolicy interface and implementations"
```

---

## Task 12: Update Sys3CEntry - Initialize FSMManager Properly

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C\Sys3CEntry.cs`

- [ ] **Step 1: Read Sys3CEntry.cs**

- [ ] **Step 2: Check if update needed**

If the current Sys3CEntry already properly initializes FSMManager with CharacterController and Animator, no changes needed. If it uses old patterns, update accordingly.

- [ ] **Step 3: Commit (if changed)**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "chore(3c): update Sys3CEntry initialization for new FSM architecture"
```

---

## Task 13: Verify Compiles

- [ ] **Step 1: Run Unity build or check for errors**

Open the project in Unity and verify there are no compile errors.

- [ ] **Step 2: Fix any issues**

If compilation fails, fix the issues.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "fix(3c): resolve compilation errors"
```

---

## Implementation Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | CharacterData - RequestJump flag | 1 file modified |
| 2 | StateTransitionTable | 1 file created |
| 3 | BaseFSM | 1 file created |
| 4 | AttackFSM | 1 file created |
| 5 | FSMManager refactor | 1 file modified |
| 6 | BaseStateBehaviour | 1 file modified |
| 7 | AttackStateBehaviour | 1 file modified |
| 8 | HitStateBehaviour | 1 file modified |
| 9 | HitManager | 1 file modified |
| 10 | CharacterController | 1 file modified |
| 11 | MovementPolicy | 1 file created |
| 12 | Sys3CEntry (if needed) | 1 file modified |
| 13 | Verify compilation | - |

---

**Plan complete.** 

Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?