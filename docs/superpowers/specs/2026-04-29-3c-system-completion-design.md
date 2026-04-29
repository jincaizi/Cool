# 3C 系统完善设计文档

**Date:** 2026-04-29
**Status:** Approved
**Version:** 2.0

---

## 一、设计决策总结

| 问题 | 决策 | 理由 |
|------|------|------|
| 状态转换决策权 | FSMManager 作为唯一决策者 | 清晰分层，易于调试 |
| StateMachineBehaviour 通信 | AnimatorParameters 实例回调 | 支持多角色 |
| IState 实现方式 | 纯数据状态 + StateTransitionTable | 可配置，易测试 |
| 网络同步集成 | 内部集成（Phase 1），后续外置为 MovementPolicy | 先跑通，后续可重构 |

---

## 二、架构总览

```
┌─────────────────────────────────────────────────────────────┐
│                         Animator                              │
│  Layer 0 (Base) | Layer 1 (Attack) | Layer 2 (Hit)           │
└─────────────────────────────────────────────────────────────┘
                              ▲ 参数
                              │
┌─────────────────────────────────────────────────────────────┐
│                    AnimationDriver                            │
│  - 实例化 AnimatorParameters                                │
│  - 持有所有参数引用                                          │
│  - 订阅 StateMachineBehaviour 事件                           │
└──────────────────────────────────────────────────────────���──┘
                              ▲ 状态请求
                              │
┌─────────────────────────────────────────────────────────────┐
│                     FSMManager                               │
│  - 协调者：管理 BaseFSM 和 AttackFSM                        │
│  - 不直接处理状态逻辑                                        │
└─────────────────────────────────────────────────────────────┘
                    ┌─────────────────┐
                    │                 │
                    ▼                 ▼
         ┌──────────────────┐  ┌──────────────────┐
         │     BaseFSM      │  │    AttackFSM     │
         │  - 移动/跳跃/死亡 │  │  - 普攻/技能     │
         │  - 状态规则表    │  │  - 连击窗口     │
         └──────────────────┘  └──────────────────┘
                    │                 │
                    └────────┬────────┘
                             ▼
         ┌──────────────────────────────────────┐
         │         StateTransitionTable          │
         │  - 外部化的状态转换规则              │
         │  - 可配置，易编辑                    │
         └──────────────────────────────────────┘
                              ▲ 物理事件
                              │
┌─────────────────────────────────────────────────────────────┐
│                  CharacterController                        │
│  - 物理计算：移动/跳跃/重力                                 │
│  - 事件通知：OnJumpRequested, OnLanded, OnDeath            │
│  - 内部集成 NetworkPrediction (Phase 1)                    │
└─────────────────────────────────────────────────────────────┘
                              ▲ 网络同步
                              │
┌─────────────────────────────────────────────────────────────┐
│                      NetworkBridge                           │
│  - 发送输入到服务端                                         │
│  - 接收服务端校正                                           │
│  - 驱动 PositionInterpolator 平滑                          │
└─────────────────────────────────────────────────────────────┘
```

---

## 三、核心组件详细设计

### 3.1 AnimationDriver / AnimatorParameters

**文件：** `AnimationDriver.cs`（已创建）

```csharp
public class AnimationDriver
{
    private readonly Animator _animator;
    
    // 参数哈希
    private static readonly int HASH_BaseState = Animator.StringToHash("BaseState");
    private static readonly int HASH_AttackState = Animator.StringToHash("AttackState");
    private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
    private static readonly int HASH_IsHit = Animator.StringToHash("IsHit");
    private static readonly int HASH_Attack = Animator.StringToHash("Attack");
    private static readonly int HASH_SkillQ = Animator.StringToHash("SkillQ");
    private static readonly int HASH_SkillR = Animator.StringToHash("SkillR");
    private static readonly int HASH_Hit = Animator.StringToHash("Hit");
    
    // 事件
    public event Action<string> OnAnimationCompleted;
    
    // 公开方法
    public void SetBaseState(BaseState state) { ... }
    public void SetAttackState(AttackState state) { ... }
    public void SetIsJumping(bool v) { ... }
    public void SetIsHit(bool v) { ... }
    public void TriggerAttack() { _animator.SetTrigger(HASH_Attack); }
    public void TriggerSkillQ() { _animator.SetTrigger(HASH_SkillQ); }
    public void TriggerSkillR() { _animator.SetTrigger(HASH_SkillR); }
    public void TriggerHit() { _animator.SetTrigger(HASH_Hit); }
    
    // Hit Layer 管理
    public const int HIT_LAYER_INDEX = 2;
    public void SetHitLayerWeight(float w) { _animator.SetLayerWeight(HIT_LAYER_INDEX, w); }
}
```

**StateMachineBehaviour 调用点：**

```csharp
// 在每个 StateMachineBehaviour 中
public class BaseStateBehaviour : StateMachineBehaviour
{
    private AnimationDriver _driver; // 由 FSMManager 设置
    
    override OnStateUpdate(...)
    {
        if (stateInfo.shortNameHash == HASH_JumpEnd && stateInfo.normalizedTime >= 0.9f)
        {
            _driver?.OnAnimationCompleted?.Invoke("JumpEnd");
        }
    }
}
```

---

### 3.2 StateTransitionTable

**新文件：** `StateTransitionTable.cs`

```csharp
using System;
using System.Collections.Generic;

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
        public BaseState TargetState;      // 目标状态
        public TransitionCondition Condition; // 条件
        public float Priority;             // 优先级（高优先级优先判断）

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

        /// <summary>
        /// 初始化转换规则
        /// </summary>
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
                new StateTransition(BaseState.JumpAir, d => true, 0), // 无条件转换
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpAir → JumpEnd（落地检测）
            _transitions[BaseState.JumpAir] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpEnd, d => d.IsGrounded && d.Velocity.y <= 0, 0),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpEnd（动画完成后由 FSMManager 处理转换）
            _transitions[BaseState.JumpEnd] = new List<StateTransition>
            {
                new StateTransition(BaseState.Idle, d => true, 1),
                new StateTransition(BaseState.Move, d => d.MoveDir.sqrMagnitude > 0.01f, 2),
                new StateTransition(BaseState.Sprint, d => d.MoveDir.sqrMagnitude > 0.01f && d.IsSprint, 3)
            };

            // Death（终止状态）
            _transitions[BaseState.Death] = new List<StateTransition>();
        }

        /// <summary>
        /// 获取当前状态可转换的目标
        /// </summary>
        public BaseState? Evaluate(BaseState current, CharacterData data, AttackState attackState)
        {
            if (!_transitions.TryGetValue(current, out var transitions))
                return null;

            // 按优先级排序
            transitions.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (var t in transitions)
            {
                if (t.Condition(data, attackState))
                    return t.TargetState;
            }

            return null;
        }

        /// <summary>
        /// 检查是否可以进入某状态
        /// </summary>
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

---

### 3.3 BaseFSM

**新文件：** `BaseFSM.cs`

```csharp
using Hotfix.GameSystems.Sys3C.Character;

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
        private BaseState? _lockedState; // 被外部锁定（如死亡）

        public BaseState CurrentState => _currentState;
        public event Action<BaseState> OnStateChanged;

        public BaseFSM(AnimationDriver driver, StateTransitionTable table)
        {
            _driver = driver;
            _table = table;
            _currentState = BaseState.Idle;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(CharacterData data, AttackState attackState)
        {
            // 死亡锁定
            if (_lockedState.HasValue)
            {
                if (_currentState != _lockedState.Value)
                    ForceState(_lockedState.Value);
                return;
            }

            // 检查状态转换
            var target = _table.Evaluate(_currentState, data, attackState);
            if (target.HasValue && target.Value != _currentState)
            {
                // 检查能否进入目标状态
                if (_table.CanEnter(target.Value, data))
                {
                    TransitionTo(target.Value);
                }
            }
        }

        /// <summary>
        /// 强制转换到某状态
        /// </summary>
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

        /// <summary>
        /// 锁定状态（死亡时调用）
        /// </summary>
        public void LockState(BaseState state)
        {
            _lockedState = state;
            ForceState(state);
        }

        /// <summary>
        /// 解锁（复活时调用）
        /// </summary>
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
            
            // 更新 IsJumping 标志
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

---

### 3.4 AttackFSM

**新文件：** `AttackFSM.cs`

```csharp
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 攻击状态机 — 管理普攻连击和技能
    /// </summary>
    public class AttackFSM
    {
        private readonly AnimationDriver _driver;
        
        private AttackState _currentState;
        
        // 连击状态
        private int _comboCount;
        private int _framesInState;
        private bool _comboUnlocked;
        
        // 回调
        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;

        public AttackState CurrentState => _currentState;

        public AttackFSM(AnimationDriver driver)
        {
            _driver = driver;
            _currentState = AttackState.Idle;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
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
                
                // 5帧后解锁连击
                if (!_comboUnlocked && _framesInState >= 5)
                {
                    _comboUnlocked = true;
                }
            }
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
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
            // Attack2 后不能连击，等待返回 Idle
        }

        /// <summary>
        /// 请求技能Q
        /// </summary>
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

        /// <summary>
        /// 请求技能R
        /// </summary>
        public void RequestSkillR(bool isGrounded)
        {
            if (!isGrounded) return; // R 技能不可空中使用
            
            if (_currentState == AttackState.Idle || CanInterrupt())
            {
                _currentState = AttackState.SkillR;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillR();
                Debug.Log("[AttackFSM] RequestSkillR");
            }
        }

        /// <summary>
        /// 动画完成回调
        /// </summary>
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

        /// <summary>
        /// 返回 AttackIdle
        /// </summary>
        public void ReturnToIdle()
        {
            if (_currentState != AttackState.Idle)
            {
                _currentState = AttackState.Idle;
                _driver.SetAttackState(_currentState);
                Debug.Log("[AttackFSM] ReturnToIdle");
            }
        }

        /// <summary>
        /// 被外部中断（如死亡）
        /// </summary>
        public void ForceIdle()
        {
            ReturnToIdle();
        }

        /// <summary>
        /// 是否可被打断
        /// </summary>
        private bool CanInterrupt()
        {
            return _currentState == AttackState.Idle;
        }
    }
}
```

---

### 3.5 FSMManager（协调者）

**改进文件：** `FSMManager.cs`

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;

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

        // 事件
        public event Action OnJumpEndCompleted;
        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;

        public FSMManager(CharacterController characterController, Animator animator)
        {
            _characterController = characterController;
            _driver = new AnimationDriver(animator);

            // 初始化子 FSM
            _transitionTable = new StateTransitionTable();
            _baseFSM = new BaseFSM(_driver, _transitionTable);
            _attackFSM = new AttackFSM(_driver);

            // 订阅 CharacterController 事件
            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnDeath += HandleDeath;

            // 订阅子 FSM 事件
            _baseFSM.OnStateChanged += HandleBaseStateChanged;
            _attackFSM.OnAttackCompleted += () => OnAttackCompleted?.Invoke();
            _attackFSM.OnSkillCompleted += () => OnSkillCompleted?.Invoke();

            // 初始化 StateMachineBehaviour 回调
            StateBehaviours.BaseStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            StateBehaviours.AttackStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            StateBehaviours.HitStateBehaviour.SetCallback(_driver, HandleHitCompleted);

            Debug.Log("[FSMManager] Initialized");
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            var data = _characterController.Data;

            // 更新 BaseFSM
            _baseFSM.Update(data, _attackFSM.CurrentState);

            // 更新 AttackFSM
            _attackFSM.Update(deltaTime);
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        public void RequestNormalAttack()
        {
            _attackFSM.RequestNormalAttack();
        }

        /// <summary>
        /// 请求技能Q
        /// </summary>
        public void RequestSkillQ()
        {
            _attackFSM.RequestSkillQ();
        }

        /// <summary>
        /// 请求技能R
        /// </summary>
        public void RequestSkillR()
        {
            _attackFSM.RequestSkillR(_characterController.IsGrounded);
        }

        /// <summary>
        /// 触发受击
        /// </summary>
        public void TriggerHit()
        {
            // Hit 打断所有状态
            _attackFSM.ForceIdle();
            _driver.TriggerHit();
            _driver.SetIsHit(true);
            _driver.SetHitLayerWeight(1f);
        }

        /// <summary>
        /// 动画完成回调
        /// </summary>
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

        /// <summary>
        /// Hit 动画完成
        /// </summary>
        private void HandleHitCompleted()
        {
            _driver.SetIsHit(false);
            _driver.SetHitLayerWeight(0f);
        }

        private void HandleJumpRequested()
        {
            // Jump 请求由 CharacterController 处理
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

---

### 3.6 StateMachineBehaviour 改造

**改进文件：** `BaseStateBehaviour.cs`

```csharp
public class BaseStateBehaviour : StateMachineBehaviour
{
    private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");
    private static AnimationDriver _driver;

    public static void SetCallback(AnimationDriver driver, Action<string> callback)
    {
        _driver = driver;
        _onAnimationCompleted = callback;
    }

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (stateInfo.shortNameHash == HASH_JumpEnd && stateInfo.normalizedTime >= 0.9f)
        {
            _onAnimationCompleted?.Invoke("JumpEnd");
        }
    }
}
```

类似地改造 `AttackStateBehaviour.cs` 和 `HitStateBehaviour.cs`。

---

### 3.7 CharacterController（网络集成 Phase 1）

**改进文件：** `CharacterController.cs`（保持现有接口，增强网络功能）

```csharp
// 新增字段
private NetworkPrediction _prediction;
private NetworkBridge _bridge;
private uint _currentSequence;

// 新增初始化
public void InitializeNetwork(NetworkBridge bridge)
{
    _bridge = bridge;
    _prediction = new NetworkPrediction();
}

// 改进 Update
public void Update(MoveCommand command)
{
    if (_data.IsDead) return;

    // 1. 应用物理移动
    ApplyMovement(command);

    // 2. 记录预测帧（如果有网络）
    if (_prediction != null && _bridge != null)
    {
        _prediction.RecordPredictedFrame(_currentSequence, _data.Position, _data.Rotation);
        _bridge.SendInput(command, _currentSequence);
        
        // 检查服务端校正
        if (_bridge.HasServerUpdate(out var seq, out var pos, out var rot))
        {
            if (_prediction.ValidateAndCorrect(seq, pos, rot, out var corrected, out _))
            {
                ApplyServerPosition(corrected.Position, corrected.Rotation);
            }
        }
        
        _currentSequence++;
    }
}
```

---

### 3.8 HitManager 集成

**改进文件：** `HitManager.cs`

```csharp
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
    }

    public void OnHitCompleted()
    {
        _driver.SetIsHit(false);
        _driver.SetHitLayerWeight(0f);
    }
}
```

---

## 四、网络同步设计（Phase 2 预留）

### MovementPolicy 架构

```csharp
/// <summary>
/// 移动策略接口
/// </summary>
public interface IMovementPolicy
{
    void Update(MoveCommand command);
    bool HasServerCorrection { get; }
    Vector3 GetCorrectedPosition();
    Quaternion GetCorrectedRotation();
}

/// <summary>
/// 本地模式（无网络）
/// </summary>
public class LocalMovementPolicy : IMovementPolicy { ... }

/// <summary>
/// 预测模式（客户端预测 + 服务端校正）
/// </summary>
public class PredictionMovementPolicy : IMovementPolicy
{
    private CharacterController _purePhysics;
    private NetworkPrediction _prediction;
    
    public void Update(MoveCommand command)
    {
        _purePhysics.Update(command);
        _prediction.Record(_purePhysics.Position, _purePhysics.Rotation);
        
        if (_bridge.HasCorrection(out var corrected))
            _purePhysics.ApplyServerPosition(corrected);
    }
}
```

**CharacterController 后续改造：**

```csharp
public class CharacterController
{
    private IMovementPolicy _movementPolicy;
    
    public void SetMovementPolicy(IMovementPolicy policy)
    {
        _movementPolicy = policy;
    }
    
    public void Update(MoveCommand command)
    {
        _movementPolicy.Update(command);
    }
}
```

---

## 五、文件结构（更新）

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Character/
│   ├── CharacterController.cs     // 增强：网络集成（Phase 1）
│   ├── CharacterData.cs           // 数据结构（已有）
│   └── GroundDetector.cs           // 地面检测（已有）
├── FSM/
│   ├── FSMManager.cs             // 协调者（改进）
│   ├── BaseFSM.cs                 // 新增
│   ├── AttackFSM.cs               // 新增
│   ├── StateTransitionTable.cs    // 新增
│   └── States/
│       ├── IState.cs             // 保留，简化
│       └── （其他状态文件可删除或保留作为文档）
├── Animation/
│   ├── AnimationDriver.cs         // 已创建
│   ├── HitManager.cs             // 改进
│   └── StateBehaviours/
│       ├── BaseStateBehaviour.cs     // 改进
│       ├── AttackStateBehaviour.cs   // 改进
│       └── HitStateBehaviour.cs      // 改进
├── Skill/
│   ├── SkillConfig.cs             // 已有
│   ├── SkillRegistry.cs           // 已有
│   └── SkillDefs.cs              // 已有
├── Network/
│   ├── NetworkPrediction.cs      // 已有
│   ├── NetworkBridge.cs          // 已有
│   ├── PositionInterpolator.cs    // 已有
│   └── MovementPolicy.cs         // Phase 2 新增
└── Sys3CEntry.cs                 // 入口（已有）
```

---

## 六、实现顺序

### Phase 1: 基础完善（优先）

1. **StateTransitionTable** — 新建
2. **BaseFSM** — 新建
3. **AttackFSM** — 新建
4. **FSMManager 改造** — 协调者模式
5. **StateMachineBehaviour 改造** — 实例回调
6. **AnimationDriver** — 已完成
7. **HitManager 集成** — 改进

### Phase 2: 网络同步完善

8. **CharacterController 网络增强** — 内部集成
9. **NetworkBridge 集成** — 通信
10. **测试和调试** — 完整流程

### Phase 3: 可选优化

11. **MovementPolicy 外置** — 架构解耦
12. **StateEventSystem 中断处理** — 优先级管理

---

## 七、测试检查清单

### Phase 1 测试

- [ ] BaseFSM Idle ↔ Move ↔ Sprint 切换
- [ ] BaseFSM 跳跃流程 JumpStart → JumpAir → JumpEnd → 返回
- [ ] AttackFSM 普攻流程 Attack1 → Attack2 → AttackIdle
- [ ] AttackFSM 连击窗口（5帧后 + 0.3~0.8）
- [ ] AttackFSM 技能Q/R 触发和完成
- [ ] FSMManager 协调两个子 FSM
- [ ] Hit 打断并叠加
- [ ] 死亡锁定所有状态

### Phase 2 测试

- [ ] 本地预测正常
- [ ] 服务端校正后位置正确
- [ ] Rubber-band 效果正常
- [ ] 预测错误时平滑恢复

---

**文档版本:** 2.0
**维护者:** Sys3C Team
**最后更新:** 2026-04-29