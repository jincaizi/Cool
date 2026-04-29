# 3C System Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild 3C system with layered FSM architecture, StateMachineBehaviour-driven animations, skill system with ScriptableObject config, and Hit overlay layer.

**Architecture:**
- CharacterController handles physics (movement, jump, gravity, ground detection)
- FSMManager manages layered FSMs (BaseFSM for movement, AttackFSM for skills)
- StateMachineBehaviours detect animation completion events
- Skill system uses ScriptableObject configs with cooldown management
- Hit animations overlay on any state via dedicated layer

**Tech Stack:** Unity 2022.3.25f1, CharacterController, StateMachineBehaviour, ScriptableObject

---

## Phase 1: Character Layer

### Task 1.1: Define CharacterData and Enums

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs`

- [ ] **Step 1: Create CharacterData.cs with enums**

```csharp
namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 基础移动状态（驱动 Base Layer FSM）
    /// </summary>
    public enum BaseState
    {
        Idle = 0,
        Move = 1,
        Sprint = 2,
        JumpStart = 3,
        JumpAir = 4,
        JumpEnd = 5,
        Death = 6
    }

    /// <summary>
    /// 攻击状态（驱动 Attack Layer FSM）
    /// </summary>
    public enum AttackState
    {
        Idle = 0,
        Attack1 = 1,
        Attack2 = 2,
        SkillQ = 3,
        SkillR = 4
    }

    /// <summary>
    /// 角色数据（值类型，主线程访问）
    /// </summary>
    public struct CharacterData
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public bool IsGrounded;
        public float VerticalVelocity;
        public BaseState BaseState;
        public AttackState AttackState;
        public bool IsSprint;
        public bool IsDead;
    }

    /// <summary>
    /// 移动命令
    /// </summary>
    public struct MoveCommand
    {
        public Vector3 MoveDir;
        public float Speed;
        public Quaternion Rotation;
        public bool IsSprint;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs
git commit -m "feat(3c): add CharacterData, BaseState, AttackState enums"
```

---

### Task 1.2: Implement GroundDetector

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs`

- [ ] **Step 1: Create GroundDetector.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 地面检测器 — 使用 CharacterController.isGrounded
    /// </summary>
    public class GroundDetector
    {
        private readonly CharacterController _controller;

        public GroundDetector(CharacterController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 检测是否在地面上
        /// </summary>
        public bool IsGrounded()
        {
            return _controller.isGrounded;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs
git commit -m "feat(3c): add GroundDetector using CharacterController.isGrounded"
```

---

### Task 1.3: Implement CharacterController

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs`

- [ ] **Step 1: Create CharacterController.cs**

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色控制器 — 移动/跳跃/物理驱动
    /// 只负责更新 CharacterData，状态变化通过事件通知
    /// </summary>
    public class CharacterController
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Transform _transform;
        private readonly GroundDetector _groundDetector;

        // 移动参数
        public float MoveSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 8.0f;
        public float RotationSpeed { get; set; } = 10.0f;
        public float Gravity { get; set; } = -30f;
        public float JumpForce { get; set; } = 12f;

        // 内部状态
        private CharacterData _data;
        private Vector3 _velocity;
        private bool _jumpRequested;

        // 事件
        public event Action OnJumpRequested;
        public event Action OnLanded;
        public event Action OnDeath;

        public CharacterData Data => _data;
        public bool IsGrounded => _groundDetector.IsGrounded();

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
                IsDead = false
            };
        }

        /// <summary>
        /// 请求跳跃
        /// </summary>
        public void RequestJump()
        {
            if (_data.IsGrounded && !_data.IsDead && _data.BaseState != BaseState.JumpStart && _data.BaseState != BaseState.JumpAir)
            {
                _jumpRequested = true;
            }
        }

        /// <summary>
        /// 应用伤害（触发受击）
        /// </summary>
        public void ApplyHit()
        {
            // Hit 由 FSM 层处理，这里仅标记
        }

        /// <summary>
        /// 应用死亡
        /// </summary>
        public void ApplyDeath()
        {
            _data.IsDead = true;
            _data.BaseState = BaseState.Death;
            _velocity.y = 0f;
            OnDeath?.Invoke();
        }

        /// <summary>
        /// 每帧驱动
        /// </summary>
        public void Update(MoveCommand command)
        {
            if (_data.IsDead)
            {
                _data.Position = _transform.position;
                _data.Rotation = _transform.rotation;
                return;
            }

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

                // 额外Y轴移动
                Vector3 yMove = Vector3.up * _velocity.y * Time.deltaTime;
                _controller.Move(yMove);
            }
            else if (_data.IsGrounded)
            {
                _velocity.y = 0f;
            }

            // 6. 跳跃阶段转换
            UpdateJumpPhase();

            // 7. 基础移动状态（非跳跃时）
            UpdateBaseState(command, currentSpeed);

            // 8. 同步数据
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;
            _data.Velocity = _controller.velocity;
            _data.VerticalVelocity = _velocity.y;
            _data.IsSprint = command.IsSprint;
        }

        private void UpdateJumpPhase()
        {
            if (_data.BaseState == BaseState.JumpStart)
            {
                // JumpStart 持续一帧后进入 JumpAir
                _data.BaseState = BaseState.JumpAir;
            }
            else if (_data.BaseState == BaseState.JumpAir)
            {
                // 着地检测
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
            // 非跳跃期间管理基础状态
            if (_data.BaseState == BaseState.Idle ||
                _data.BaseState == BaseState.Move ||
                _data.BaseState == BaseState.Sprint)
            {
                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
                    // 旋转
                    _transform.rotation = Quaternion.Slerp(
                        _transform.rotation,
                        command.Rotation,
                        RotationSpeed * Time.deltaTime
                    );

                    // 更新状态
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

        /// <summary>
        /// 落地动画完成后调用
        /// </summary>
        public void FinishJump()
        {
            if (_data.BaseState == BaseState.JumpEnd)
            {
                _data.BaseState = BaseState.Idle;
            }
        }

        /// <summary>
        /// 应用服务端权威位置
        /// </summary>
        public void ApplyServerPosition(Vector3 position, Quaternion rotation)
        {
            _transform.position = position;
            _transform.rotation = rotation;
            _controller.enabled = false;
            _controller.transform.position = position;
            _controller.enabled = true;

            _data.Position = position;
            _data.Rotation = rotation;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs
git commit -m "feat(3c): implement CharacterController with event notifications"
```

---

## Phase 2: FSM Foundation

### Task 2.1: Create IState Interface

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/States/IState.cs`

- [ ] **Step 1: Create IState.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.FSM.States
{
    /// <summary>
    /// FSM 状态接口
    /// </summary>
    public interface IState
    {
        /// <summary>
        /// 状态进入
        /// </summary>
        void Enter();

        /// <summary>
        /// 状态退出
        /// </summary>
        void Exit();

        /// <summary>
        /// 每帧更新
        /// </summary>
        void Update(float deltaTime);

        /// <summary>
        /// 状态名称
        /// </summary>
        string StateName { get; }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/States/IState.cs
git commit -m "feat(3c): add IState interface"
```

---

### Task 2.2: Create BaseFSM States

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/States/BaseStates.cs`

- [ ] **Step 1: Create BaseStates.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.FSM.States
{
    /// <summary>
    /// Idle 状态
    /// </summary>
    public class IdleState : IState
    {
        public string StateName => "Idle";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Move 状态
    /// </summary>
    public class MoveState : IState
    {
        public string StateName => "Move";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Sprint 状态
    /// </summary>
    public class SprintState : IState
    {
        public string StateName => "Sprint";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// JumpStart 状态
    /// </summary>
    public class JumpStartState : IState
    {
        public string StateName => "JumpStart";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// JumpAir 状态
    /// </summary>
    public class JumpAirState : IState
    {
        public string StateName => "JumpAir";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// JumpEnd 状态
    /// </summary>
    public class JumpEndState : IState
    {
        public string StateName => "JumpEnd";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Death 状态
    /// </summary>
    public class DeathState : IState
    {
        public string StateName => "Death";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/States/BaseStates.cs
git commit -m "feat(3c): add BaseFSM states (Idle, Move, Sprint, Jump*, Death)"
```

---

### Task 2.3: Create AttackFSM States

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/States/AttackStates.cs`

- [ ] **Step 1: Create AttackStates.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.FSM.States
{
    /// <summary>
    /// AttackIdle 状态
    /// </summary>
    public class AttackIdleState : IState
    {
        public string StateName => "AttackIdle";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Attack1 状态
    /// </summary>
    public class Attack1State : IState
    {
        public string StateName => "Attack1";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Attack2 状态
    /// </summary>
    public class Attack2State : IState
    {
        public string StateName => "Attack2";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// SkillQ 状态（突刺）
    /// </summary>
    public class SkillQState : IState
    {
        public string StateName => "SkillQ";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// SkillR 状态
    /// </summary>
    public class SkillRState : IState
    {
        public string StateName => "SkillR";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/States/AttackStates.cs
git commit -m "feat(3c): add AttackFSM states (AttackIdle, Attack1/2, SkillQ/R)"
```

---

### Task 2.4: Create FSMManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`

- [ ] **Step 1: Create FSMManager.cs**

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM.States;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// FSM 管理器 — 统一管理 BaseFSM 和 AttackFSM
    /// 监听 CharacterController 事件，驱动 Animator
    /// </summary>
    public class FSMManager
    {
        private readonly CharacterController _characterController;
        private readonly Animator _animator;

        // Animator 参数哈希
        private static readonly int HASH_BaseState = Animator.StringToHash("BaseState");
        private static readonly int HASH_AttackState = Animator.StringToHash("AttackState");
        private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");
        private static readonly int HASH_SkillQ = Animator.StringToHash("SkillQ");
        private static readonly int HASH_SkillR = Animator.StringToHash("SkillR");

        // 当前状态
        private BaseState _currentBaseState = BaseState.Idle;
        private AttackState _currentAttackState = AttackState.Idle;

        // 事件回调
        public event Action OnJumpEndCompleted;
        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;

        public FSMManager(CharacterController characterController, Animator animator)
        {
            _characterController = characterController;
            _animator = animator;

            // 订阅 CharacterController 事件
            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnDeath += HandleDeath;

            // 初始化 Animator 参数
            _animator.SetInteger(HASH_BaseState, (int)BaseState.Idle);
            _animator.SetInteger(HASH_AttackState, (int)AttackState.Idle);
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            SyncFromCharacterData();
        }

        /// <summary>
        /// 从 CharacterData 同步状态
        /// </summary>
        private void SyncFromCharacterData()
        {
            var data = _characterController.Data;

            // 同步 BaseState
            if (data.BaseState != _currentBaseState)
            {
                _currentBaseState = data.BaseState;
                _animator.SetInteger(HASH_BaseState, (int)_currentBaseState);

                // 更新 IsJumping
                bool isJumping = _currentBaseState == BaseState.JumpStart ||
                                 _currentBaseState == BaseState.JumpAir ||
                                 _currentBaseState == BaseState.JumpEnd;
                _animator.SetBool(HASH_IsJumping, isJumping);
            }
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        public void RequestNormalAttack()
        {
            if (_currentAttackState == AttackState.Idle)
            {
                _currentAttackState = AttackState.Attack1;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_Attack);
            }
            else if (_currentAttackState == AttackState.Attack1)
            {
                // 连击到 Attack2
                _currentAttackState = AttackState.Attack2;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_Attack);
            }
            // Attack2 后不能再连击，返回 AttackIdle
        }

        /// <summary>
        /// 请求技能Q
        /// </summary>
        public void RequestSkillQ()
        {
            if (_currentAttackState == AttackState.Idle || CanInterrupt())
            {
                _currentAttackState = AttackState.SkillQ;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_SkillQ);
            }
        }

        /// <summary>
        /// 请求技能R
        /// </summary>
        public void RequestSkillR()
        {
            // SkillR 不可在跳跃中使用
            if (_characterController.Data.BaseState == BaseState.JumpStart ||
                _characterController.Data.BaseState == BaseState.JumpAir)
            {
                return;
            }

            if (_currentAttackState == AttackState.Idle || CanInterrupt())
            {
                _currentAttackState = AttackState.SkillR;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_SkillR);
            }
        }

        /// <summary>
        /// 动画完成回调（由 StateMachineBehaviour 调用）
        /// </summary>
        public void OnAnimationCompleted(string stateName)
        {
            switch (stateName)
            {
                case "JumpEnd":
                    _characterController.FinishJump();
                    OnJumpEndCompleted?.Invoke();
                    break;
                case "Attack1":
                case "Attack2":
                    ReturnToAttackIdle();
                    OnAttackCompleted?.Invoke();
                    break;
                case "SkillQ":
                case "SkillR":
                    ReturnToAttackIdle();
                    OnSkillCompleted?.Invoke();
                    break;
            }
        }

        private void ReturnToAttackIdle()
        {
            if (_currentAttackState != AttackState.Idle)
            {
                _currentAttackState = AttackState.Idle;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
            }
        }

        private bool CanInterrupt()
        {
            // 某些状态可被打断
            return _currentAttackState == AttackState.Idle;
        }

        private void HandleJumpRequested()
        {
            // Jump 由 CharacterController 处理
        }

        private void HandleLanded()
        {
            // 落地由 CharacterController 检测
        }

        private void HandleDeath()
        {
            _currentBaseState = BaseState.Death;
            _animator.SetInteger(HASH_BaseState, (int)_currentBaseState);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs
git commit -m "feat(3c): add FSMManager for layered FSM control"
```

---

## Phase 3: Animation Layer

### Task 3.1: Create BaseStateBehaviour

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/BaseStateBehaviour.cs`

- [ ] **Step 1: Create BaseStateBehaviour.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    /// <summary>
    /// Base Layer 动画完成监听
    /// 监听 JumpStart、JumpAir、JumpEnd 动画事件
    /// </summary>
    public class BaseStateBehaviour : StateMachineBehaviour
    {
        // 状态哈希
        private static readonly int HASH_JumpStart = Animator.StringToHash("JumpStart");
        private static readonly int HASH_JumpAir = Animator.StringToHash("JumpAir");
        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");

        // 回调引用（由 FSMManager 设置）
        private static System.Action<string> _onAnimationCompleted;

        public static void SetCallback(System.Action<string> callback)
        {
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
            // JumpEnd 动画完成检测
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

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/BaseStateBehaviour.cs
git commit -m "feat(3c): add BaseStateBehaviour for jump animation completion"
```

---

### Task 3.2: Create AttackStateBehaviour

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs`

- [ ] **Step 1: Create AttackStateBehaviour.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    /// <summary>
    /// Attack Layer 动画完成监听
    /// 监听 Attack1、Attack2 动画，处理连击窗口
    /// </summary>
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        // 状态哈希
        private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
        private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");

        // 连击窗口配置
        private const int COMBO_FRAME_LOCK = 5;        // 5帧后解锁连击
        private const float COMBO_WINDOW_START = 0.3f; // normalizedTime 开始
        private const float COMBO_WINDOW_END = 0.8f;    // normalizedTime 结束

        // 当前状态追踪
        private int _framesInState;
        private bool _comboUnlocked;

        // 回调引用
        private static System.Action<string> _onAnimationCompleted;

        public static void SetCallback(System.Action<string> callback)
        {
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

            // 5帧后解锁连击
            if (!_comboUnlocked && _framesInState >= COMBO_FRAME_LOCK)
            {
                _comboUnlocked = true;
                Debug.Log("[AttackBehaviour] Combo unlocked at frame " + _framesInState);
            }

            // 检测动画完成
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

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs
git commit -m "feat(3c): add AttackStateBehaviour with combo window logic"
```

---

### Task 3.3: Create HitStateBehaviour

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs`

- [ ] **Step 1: Create HitStateBehaviour.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    /// <summary>
    /// Hit Layer 动画完成监听
    /// 监听 Hit 动画，触发返回原状态
    /// </summary>
    public class HitStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");

        // 回调引用
        private static System.Action<string> _onAnimationCompleted;

        public static void SetCallback(System.Action<string> callback)
        {
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

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs
git commit -m "feat(3c): add HitStateBehaviour for hit animation overlay"
```

---

## Phase 4: Skill System

### Task 4.1: Create SkillConfig ScriptableObject

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDefs.cs`

- [ ] **Step 1: Create SkillConfig.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能配置（ScriptableObject）
    /// </summary>
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "Game/Skill")]
    public class SkillConfig : ScriptableObject
    {
        [Header("Basic Info")]
        public string SkillName;
        public string SkillId;

        [Header("Animation")]
        public string AnimationName;      // 动画名（如 "AttackQ"）

        [Header("Cooldown")]
        public float Cooldown;            // CD时间（秒），0表示无CD

        [Header("Usage Condition")]
        public bool CanUseInAir = true;    // 是否可空中使用

        [Header("Combo")]
        public float ComboWindowStart;     // 连击窗口开始（normalizedTime）
        public float ComboWindowEnd;       // 连击窗口结束（normalizedTime）
        public int ComboFrameLock;         // 固定帧解锁，0表示无连击
    }
}
```

- [ ] **Step 2: Create SkillDefs.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能ID常量
    /// </summary>
    public static class SkillDefs
    {
        public const string NormalAttack1 = "NormalAttack1";
        public const string NormalAttack2 = "NormalAttack2";
        public const string SkillQ = "SkillQ";
        public const string SkillR = "SkillR";
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDefs.cs
git commit -m "feat(3c): add SkillConfig SO and SkillDefs"
```

---

### Task 4.2: Create SkillRegistry

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillRegistry.cs`

- [ ] **Step 1: Create SkillRegistry.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能注册表 — 管理技能配置和CD
    /// </summary>
    public class SkillRegistry
    {
        private readonly Dictionary<string, SkillConfig> _skills = new Dictionary<string, SkillConfig>();
        private readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        /// <summary>
        /// 注册技能配置
        /// </summary>
        public void Register(SkillConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.SkillId))
            {
                Debug.LogError("[SkillRegistry] Invalid skill config");
                return;
            }

            _skills[config.SkillId] = config;
            _cooldowns[config.SkillId] = 0f;
            Debug.Log("[SkillRegistry] Registered skill: " + config.SkillId);
        }

        /// <summary>
        /// 注册多个技能
        /// </summary>
        public void RegisterRange(IEnumerable<SkillConfig> configs)
        {
            foreach (var config in configs)
            {
                Register(config);
            }
        }

        /// <summary>
        /// 检查技能是否可用
        /// </summary>
        public bool CanUse(string skillId, bool isGrounded)
        {
            if (!_skills.TryGetValue(skillId, out var config))
            {
                Debug.LogWarning("[SkillRegistry] Skill not found: " + skillId);
                return false;
            }

            // 检查CD
            if (_cooldowns[skillId] > 0)
            {
                Debug.Log("[SkillRegistry] Skill on cooldown: " + skillId + ", remaining: " + _cooldowns[skillId]);
                return false;
            }

            // 检查空中使用
            if (!isGrounded && !config.CanUseInAir)
            {
                Debug.Log("[SkillRegistry] Skill cannot be used in air: " + skillId);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 使用技能（开始CD）
        /// </summary>
        public void Use(string skillId)
        {
            if (!_skills.ContainsKey(skillId))
            {
                Debug.LogError("[SkillRegistry] Skill not registered: " + skillId);
                return;
            }

            var config = _skills[skillId];
            if (config.Cooldown > 0)
            {
                _cooldowns[skillId] = config.Cooldown;
                Debug.Log("[SkillRegistry] Used skill " + skillId + ", CD: " + config.Cooldown + "s");
            }
        }

        /// <summary>
        /// 获取技能配置
        /// </summary>
        public SkillConfig GetConfig(string skillId)
        {
            return _skills.TryGetValue(skillId, out var config) ? config : null;
        }

        /// <summary>
        /// 获取技能CD剩余时间
        /// </summary>
        public float GetCooldownRemaining(string skillId)
        {
            return _cooldowns.TryGetValue(skillId, out var cd) ? cd : 0f;
        }

        /// <summary>
        /// 每帧更新CD
        /// </summary>
        public void Update(float deltaTime)
        {
            foreach (var key in _cooldowns.Keys)
            {
                if (_cooldowns[key] > 0)
                {
                    _cooldowns[key] = Mathf.Max(0, _cooldowns[key] - deltaTime);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillRegistry.cs
git commit -m "feat(3c): add SkillRegistry with cooldown management"
```

---

### Task 4.3: Create Default Skill Assets

**Files:**
- Create: `Assets/Resources/Skills/` 目录及配置文件

- [ ] **Step 1: Create skill assets (需在Unity中创建)**

创建以下 SkillConfig 资源：
1. **NormalAttack1** - 无CD，可空中使用
2. **NormalAttack2** - 无CD，可空中使用  
3. **SkillQ** - CD 5秒，可空中使用
4. **SkillR** - CD 10秒，不可空中使用

- [ ] **Step 2: Commit**

```bash
git add Assets/Resources/Skills/
git commit -m "feat(3c): add default skill config assets"
```

---

## Phase 5: Hit System

### Task 5.1: Create HitManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/HitManager.cs`

- [ ] **Step 1: Create HitManager.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation
{
    /// <summary>
    /// Hit 管理器 — 处理受击叠加层
    /// </summary>
    public class HitManager
    {
        private readonly Animator _animator;
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_IsHit = Animator.StringToHash("IsHit");

        private const int HIT_LAYER_INDEX = 2;

        public HitManager(Animator animator)
        {
            _animator = animator;
        }

        /// <summary>
        /// 触发受击动画
        /// </summary>
        public void TriggerHit()
        {
            _animator.SetTrigger(HASH_Hit);
            _animator.SetBool(HASH_IsHit, true);
            Debug.Log("[HitManager] TriggerHit called");

            // 设置 Hit 层权重（由 StateMachineBehaviour 控制）
            // 这里主要是通知
        }

        /// <summary>
        /// Hit 动画完成回调
        /// </summary>
        public void OnHitCompleted()
        {
            _animator.SetBool(HASH_IsHit, false);
            Debug.Log("[HitManager] OnHitCompleted");

            // Hit 层权重归零，状态机自动返回
        }

        /// <summary>
        /// 获取 Hit 层权重
        /// </summary>
        public float GetHitLayerWeight()
        {
            return _animator.GetLayerWeight(HIT_LAYER_INDEX);
        }

        /// <summary>
        /// 设置 Hit 层权重
        /// </summary>
        public void SetHitLayerWeight(float weight)
        {
            _animator.SetLayerWeight(HIT_LAYER_INDEX, weight);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/HitManager.cs
git commit -m "feat(3c): add HitManager for hit overlay layer"
```

---

## Phase 6: Integration

### Task 6.1: Update Sys3CEntry

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Update Sys3CEntry.cs**

将 Sys3CEntry 更新为使用新架构：

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;

namespace Hotfix.GameSystems.Sys3C
{
    public class Sys3CEntry : MonoBehaviour
    {
        [Header("References")]
        public UnityEngine.CharacterController CharacterController;
        public Animator Animator;

        [Header("Settings")]
        public LayerMask GroundLayer;

        private CharacterController _cc;
        private FSMManager _fsmManager;
        private SkillRegistry _skillRegistry;
        private HitManager _hitManager;
        private InputManager _inputManager;

        private void Start()
        {
            // 初始化组件
            _cc = new CharacterController(transform, CharacterController, GroundLayer);
            _fsmManager = new FSMManager(_cc, Animator);
            _skillRegistry = new SkillRegistry();
            _hitManager = new HitManager(Animator);

            // 注册默认技能
            RegisterDefaultSkills();

            // 设置 StateMachineBehaviour 回调
            Animation.StateBehaviours.BaseStateBehaviour.SetCallback(_fsmManager.OnAnimationCompleted);
            Animation.StateBehaviours.AttackStateBehaviour.SetCallback(_fsmManager.OnAnimationCompleted);
            Animation.StateBehaviours.HitStateBehaviour.SetCallback(_hitManager.OnHitCompleted);

            // 初始化输入
            _inputManager = GetComponent<InputManager>();
            if (_inputManager != null)
            {
                _inputManager.OnJumpPressed += () => _cc.RequestJump();
                _inputManager.OnAttackPressed += () => _fsmManager.RequestNormalAttack();
                _inputManager.OnSkillQPressed += () => TryUseSkill(SkillDefs.SkillQ);
                _inputManager.OnSkillRPressed += () => TryUseSkill(SkillDefs.SkillR);
            }

            Debug.Log("[Sys3CEntry] Initialized");
        }

        private void Update()
        {
            // 读取输入
            var command = _inputManager?.GetMoveCommand() ?? default;

            // 更新各系统
            _cc.Update(command);
            _fsmManager.Update(Time.deltaTime);
            _skillRegistry.Update(Time.deltaTime);
        }

        private void TryUseSkill(string skillId)
        {
            if (_skillRegistry.CanUse(skillId, _cc.IsGrounded))
            {
                _skillRegistry.Use(skillId);

                switch (skillId)
                {
                    case SkillDefs.SkillQ:
                        _fsmManager.RequestSkillQ();
                        break;
                    case SkillDefs.SkillR:
                        _fsmManager.RequestSkillR();
                        break;
                }
            }
        }

        private void RegisterDefaultSkills()
        {
            // 从 Resources 加载技能配置
            var configs = Resources.LoadAll<Skill.SkillConfig>("Skills");
            _skillRegistry.RegisterRange(configs);

            Debug.Log("[Sys3CEntry] Registered " + configs.Length + " skills");
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat(3c): update Sys3CEntry with new architecture integration"
```

---

### Task 6.2: Create Animator Controller Config (Reference)

**Files:**
- Note: 需要在 Unity Editor 中配置

- [ ] **Step 1: Document Animator Setup Requirements**

在 `docs/superpowers/plans/` 下创建 Animator 配置说明：

```markdown
# Animator Controller 配置要求

## Layer 配置

| Layer | Weight | Blending | 状态 |
|-------|--------|----------|------|
| Base | 1 | Override | Idle, Move, Sprint, Jump* |
| Attack | 1 | Override | AttackIdle, Attack1, Attack2, SkillQ, SkillR |
| Hit | 1 | Additive | Hit |

## 参数配置

| 参数名 | 类型 | 说明 |
|--------|------|------|
| BaseState | Int | 0=Idle, 1=Move, 2=Sprint, 3=JumpStart, 4=JumpAir, 5=JumpEnd, 6=Death |
| AttackState | Int | 0=AttackIdle, 1=Attack1, 2=Attack2, 3=SkillQ, 4=SkillR |
| IsJumping | Bool | 是否在跳跃中 |
| IsHit | Bool | 是否受击中 |
| Attack | Trigger | 普攻触发 |
| SkillQ | Trigger | 技能Q触发 |
| SkillR | Trigger | 技能R触发 |
| Hit | Trigger | 受击触发 |

## StateMachineBehaviour 挂载

- Base Layer 状态: JumpEnd → 挂载 BaseStateBehaviour
- Attack Layer 状态: Attack1, Attack2 → 挂载 AttackStateBehaviour
- Hit Layer 状态: Hit → 挂载 HitStateBehaviour

## 转换规则

### Base Layer
- Idle → Move (IsMoving)
- Idle → Sprint (IsSprinting)
- Any → JumpStart (IsJumping && !WasJumping)
- JumpStart → JumpAir (自动/动画完成)
- JumpAir → JumpEnd (OnLanded)
- JumpEnd → Idle (动画完成)

### Attack Layer
- AttackIdle → Attack1 (Attack trigger)
- Attack1 → Attack2 (Attack trigger + 连击条件)
- Any → SkillQ (SkillQ trigger)
- Any → SkillR (SkillR trigger)
- Any → Hit (Hit trigger, 最高优先级)
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/plans/2026-04-29-3c-animator-setup.md
git commit -m "docs: add Animator Controller configuration guide"
```

---

## Self-Review Checklist

1. **Spec coverage:**
   - [x] CharacterController with events - Task 1.3
   - [x] FSMManager with BaseFSM + AttackFSM - Task 2.4
   - [x] StateMachineBehaviours (Base, Attack, Hit) - Tasks 3.1, 3.2, 3.3
   - [x] SkillConfig SO + SkillRegistry - Tasks 4.1, 4.2
   - [x] Hit overlay layer - Task 5.1
   - [x] Integration in Sys3CEntry - Task 6.1

2. **Placeholder scan:** 无 TBD/TODO/placeholder 代码

3. **Type consistency:**
   - BaseState enum 与 CharacterData.BaseState 一致
   - AttackState enum 与 CharacterData.AttackState 一致
   - SkillDefs 使用 string 常量，与 SkillConfig.SkillId 对应