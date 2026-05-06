# SkillQ 突刺位移实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 SkillQ 添加突刺位移效果，角色在播放突刺动画时向正前方突进 3 米，碰障碍物停止。

**Architecture:** 新增 `SkillDashComponent` 组件处理突进位移逻辑，复用已有的 `CharacterController` 进行移动。在 `AttackFSM` 中触发突进，通过碰撞检测实现障碍物停止。

**Tech Stack:** Unity 2022 LTS, C#, 已有 3C 系统

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDashComponent.cs` | 新增：突进位移组件 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs` | 修改：添加 `LockMovement` 属性 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs` | 修改：集成 SkillDashComponent |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs` | 修改：初始化 SkillDashComponent |

---

## 实现任务

### Task 1: 创建 SkillDashComponent

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDashComponent.cs`

- [ ] **Step 1: 创建 SkillDashComponent.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能突进组件 — 处理技能位移逻辑
    /// </summary>
    public class SkillDashComponent
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Transform _transform;

        // 突进状态
        private bool _isDashing;
        private float _dashTimer;
        private float _dashDuration;
        private Vector3 _dashDirection;
        private float _dashSpeed;

        // 阶段时间（秒）
        private const float STARTUP_TIME = 0.05f;
        private const float RECOVERY_TIME = 0.05f;

        // 碰撞检测参数
        private float _checkRadius = 0.3f;
        private int _obstacleLayerMask;

        public bool IsDashing => _isDashing;

        public SkillDashComponent(UnityEngine.CharacterController controller, Transform transform)
        {
            _controller = controller;
            _transform = transform;

            // 默认只检测静态障碍物（Wall, Floor, Obstacle）
            _obstacleLayerMask = LayerMask.GetMask("Default", "Wall", "Floor");
        }

        /// <summary>
        /// 开始突进
        /// </summary>
        /// <param name="direction">突进方向（单位向量）</param>
        /// <param name="distance">突进距离（米）</param>
        /// <param name="duration">突进持续时间（秒）</param>
        public void StartDash(Vector3 direction, float distance, float duration)
        {
            _isDashing = true;
            _dashTimer = 0f;
            _dashDuration = duration;
            _dashDirection = direction.normalized;
            _dashSpeed = distance / (duration - STARTUP_TIME - RECOVERY_TIME);

            Debug.Log($"[SkillDashComponent] StartDash: dir={direction}, distance={distance}, duration={duration}, speed={_dashSpeed}");
        }

        /// <summary>
        /// 立即停止突进
        /// </summary>
        public void StopDash()
        {
            if (_isDashing)
            {
                Debug.Log("[SkillDashComponent] StopDash");
                _isDashing = false;
                _dashTimer = 0f;
            }
        }

        /// <summary>
        /// 每帧更新突进逻辑
        /// </summary>
        /// <returns>本次更新移动的距离</returns>
        public float Update()
        {
            if (!_isDashing) return 0f;

            _dashTimer += Time.deltaTime;
            float movedDistance = 0f;

            // 起手阶段：不动
            if (_dashTimer < STARTUP_TIME)
            {
                return 0f;
            }

            // 突进阶段：移动
            float dashTime = _dashTimer - STARTUP_TIME;
            float maxDashTime = _dashDuration - STARTUP_TIME - RECOVERY_TIME;

            if (dashTime < maxDashTime)
            {
                // 计算本帧移动量
                float frameMove = _dashSpeed * Time.deltaTime;

                // 碰撞检测
                Vector3 targetPos = _transform.position + _dashDirection * frameMove;
                if (!CheckCollision(targetPos))
                {
                    _controller.Move(_dashDirection * frameMove);
                    movedDistance = frameMove;
                }
                else
                {
                    // 碰到障碍物，停止突进
                    StopDash();
                    return movedDistance;
                }
            }

            // 收尾阶段：不动
            if (_dashTimer >= _dashDuration)
            {
                StopDash();
            }

            return movedDistance;
        }

        /// <summary>
        /// 检测目标位置是否会发生碰撞
        /// </summary>
        private bool CheckCollision(Vector3 targetPosition)
        {
            Vector3 checkOrigin = _transform.position + Vector3.up * 0.5f;
            Vector3 checkDirection = (targetPosition - _transform.position).normalized;
            float checkDistance = Vector3.Distance(_transform.position, targetPosition) + _checkRadius;

            if (checkDistance < 0.01f) return false;

            return Physics.SphereCast(checkOrigin, _checkRadius, checkDirection, out RaycastHit hit, checkDistance, _obstacleLayerMask);
        }
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDashComponent.cs
git commit -m "feat(SkillDashComponent): add dash movement component"
```

---

### Task 2: 修改 CharacterController 添加 LockMovement

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs:33`

- [ ] **Step 1: 在 CharacterController 中添加 LockMovement 属性**

在第 33 行 `LockRotation` 属性后添加：

```csharp
/// <summary>
/// 锁定移动（突进时使用）
/// </summary>
public bool LockMovement { get; set; }
```

- [ ] **Step 2: 修改 ApplyHorizontalMovement 方法，检测 LockMovement**

将 `ApplyHorizontalMovement` 方法开头修改为：

```csharp
private void ApplyHorizontalMovement(MoveCommand command)
{
    // 突进时不允许普通移动
    if (LockMovement)
    {
        return;
    }

    float currentSpeed = command.IsSprint ? SprintSpeed : MoveSpeed;
    Vector3 moveVelocity = command.MoveDir * currentSpeed;
    moveVelocity.y = _velocity.y;
    _controller.Move(moveVelocity * Time.deltaTime);
}
```

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs
git commit -m "feat(CharacterController): add LockMovement for skill dash"
```

---

### Task 3: 修改 AttackFSM 集成突进逻辑

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs`

- [ ] **Step 1: 在 AttackFSM 中添加 SkillDashComponent 引用和字段**

在 `AttackFSM` 类开头添加字段：

```csharp
public class AttackFSM
{
    private readonly AnimationDriver _driver;
    private Skill.SkillDashComponent _dashComponent;
    
    // ... 现有字段 ...
    
    // SkillQ 突进参数
    private const float SKILLQ_DASH_DISTANCE = 3f;
    private const float SKILLQ_DASH_DURATION = 0.3f;
```

- [ ] **Step 2: 添加 SetDashComponent 方法**

在构造函数后添加：

```csharp
public void SetDashComponent(Skill.SkillDashComponent dashComponent)
{
    _dashComponent = dashComponent;
}
```

- [ ] **Step 3: 修改 RequestSkillQ 方法，启动突进**

将 `RequestSkillQ` 方法修改为：

```csharp
public void RequestSkillQ()
{
    Debug.Log($"[AttackFSM] RequestSkillQ called, current state: {_currentState}");
    if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
    {
        _currentState = AttackState.SkillQ;
        _comboCount = 0;
        _framesInState = 0;
        _comboUnlocked = false;
        _driver.SetAttackState(_currentState);
        _driver.TriggerSkillQ();
        
        // 启动突进（方向由角色朝向决定，Update 中实时获取）
        if (_dashComponent != null)
        {
            // 方向在 Update 中根据角色朝向计算
        }
        
        Debug.Log("[AttackFSM] RequestSkillQ: changed to SkillQ with dash");
    }
    else
    {
        Debug.Log("[AttackFSM] RequestSkillQ blocked, current state is not Attack1/2");
    }
}
```

- [ ] **Step 4: 修改 Update 方法，添加突进更新和方向计算**

将 Update 方法修改为：

```csharp
public void Update(float deltaTime)
{
    // 更新霸体计时器
    if (_superArmorTime > 0)
    {
        _superArmorTime -= deltaTime;
        if (_superArmorTime < 0) _superArmorTime = 0;
    }

    // 更新技能状态计时器
    if (_currentState == AttackState.SkillQ || _currentState == AttackState.SkillR_Start || _currentState == AttackState.SkillR_Loop)
    {
        _skillStateTimer += deltaTime;
        if (_skillStateTimer >= SKILL_TIMEOUT)
        {
            Debug.LogWarning("[AttackFSM] Skill state timeout, forcing return to idle");
            ReturnToIdle();
        }
    }

    // SkillQ 突进更新
    if (_currentState == AttackState.SkillQ && _dashComponent != null && _dashComponent.IsDashing)
    {
        // 获取角色正前方方向（需要从外部传入，这里简化处理）
        // 实际方向由 FSMManager 在调用时传入
    }

    // 更新技能R持续时间检测
    if (_isSkillRActive && _currentState == AttackState.SkillR_Loop)
    {
        _skillRDuration += deltaTime;
        if (_skillRMaxDuration > 0 && _skillRDuration >= _skillRMaxDuration)
        {
            Debug.Log($"[AttackFSM] SkillR duration reached max ({_skillRMaxDuration}s), canceling");
            CancelSkillR();
        }
    }

    if (_currentState == AttackState.Idle)
    {
        _comboCount = 0;
        _framesInState = 0;
        _comboUnlocked = false;
        _skillStateTimer = 0f;
        _skillRDuration = 0f;
        _isSkillRActive = false;
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
```

- [ ] **Step 5: 添加 StartSkillQDash 公共方法**

在 `RequestSkillQ` 后添加：

```csharp
/// <summary>
/// 开始SkillQ突进（由FSMManager调用，传入角色朝向）
/// </summary>
public void StartSkillQDash(Vector3 forwardDirection)
{
    if (_currentState == AttackState.SkillQ && _dashComponent != null)
    {
        _dashComponent.StartDash(forwardDirection, SKILLQ_DASH_DISTANCE, SKILLQ_DASH_DURATION);
    }
}
```

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs
git commit -m "feat(AttackFSM): integrate SkillDashComponent for SkillQ dash"
```

---

### Task 4: 修改 FSMManager 初始化和协调

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`

- [ ] **Step 1: 在 FSMManager 中添加 SkillDashComponent 字段和初始化**

在 `FSMManager` 字段区域添加：

```csharp
private readonly HitFSM _hitFSM;
private readonly StateCoordinator _stateCoordinator;
private readonly Skill.SkillDashComponent _dashComponent;
```

修改构造函数：

```csharp
public FSMManager(Hotfix.GameSystems.Sys3C.Character.CharacterController characterController, Animator animator, AnimationDriver driver)
{
    _characterController = characterController;
    _driver = driver;

    // 初始化 SkillDashComponent
    var unityController = characterController.GetType()
        .GetField("_controller", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?.GetValue(characterController) as UnityEngine.CharacterController;
    _dashComponent = new Skill.SkillDashComponent(unityController, characterController.Transform);

    var transitionTable = new StateTransitionTable();
    _baseFSM = new BaseFSM(_driver, transitionTable);
    _attackFSM = new AttackFSM(_driver);
    _hitFSM = new HitFSM(_driver);

    // 传递 dashComponent 给 AttackFSM
    _attackFSM.SetDashComponent(_dashComponent);

    // ... 其余初始化代码保持不变 ...
}
```

- [ ] **Step 2: 修改 Update 方法，更新突进逻辑**

将 Update 方法修改为：

```csharp
public void Update(float deltaTime)
{
    var data = _characterController.Data;

    _baseFSM.Update(data, _attackFSM.CurrentState);
    _attackFSM.Update(deltaTime);
    _stateCoordinator.Update(deltaTime);

    // 更新 SkillQ 突进
    if (_attackFSM.CurrentState == AttackState.SkillQ && _dashComponent.IsDashing)
    {
        _dashComponent.Update();
    }
}
```

- [ ] **Step 3: 修改 RequestSkillQ 方法，触发突进**

将 `RequestSkillQ` 方法修改为：

```csharp
public void RequestSkillQ()
{
    _characterController.LockRotation = true;
    _characterController.LockMovement = true;  // 锁定移动，防止普通移动干扰突进
    _attackFSM.RequestSkillQ();
    
    // 启动突进，方向为角色正前方
    Vector3 forward = _characterController.Transform.forward;
    _attackFSM.StartSkillQDash(forward);
}
```

- [ ] **Step 4: 修改 HandleAnimationCompleted，恢复 LockMovement**

在 `HandleAnimationCompleted` 的 SkillQ 处理中添加：

```csharp
case "AttackQ":  // SkillQ 动画状态在Animator中叫 AttackQ
    _attackFSM.OnAnimationCompleted(stateName);
    Debug.Log($"[FSMManager] After OnAnimationCompleted, AttackFSM state: {_attackFSM.CurrentState}");
    _driver.ResetAttackTrigger();
    _driver.ResetSkillQTrigger();
    
    // 恢复移动锁定
    _characterController.LockMovement = false;
    _characterController.LockRotation = false;
    break;
```

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs
git commit -m "feat(FSMManager): init SkillDashComponent and coordinate dash movement"
```

---

## 自检清单

- [ ] 所有文件路径正确
- [ ] 没有 TBD/TODO 占位符
- [ ] 类型一致性检查：
  - `SkillDashComponent` 构造函数参数匹配
  - `AttackFSM.StartSkillQDash` 方法签名正确
  - `FSMManager` 中反射获取 `_controller` 字段存在
- [ ] 规格覆盖检查：
  - ✅ 3米突进距离
  - ✅ 0.3秒突进时长
  - ✅ 碰撞停止
  - ✅ 起手/突进/收尾阶段

---

## 执行选项

**Plan complete and saved to `docs/superpowers/plans/2026-05-05-skillq-dash-plan.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**