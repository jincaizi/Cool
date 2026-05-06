# 技能R持续性技能实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现技能R持续性技能，包含起手动画和循环动画，支持最大时长限制和按键松开取消。

**Architecture:** 在现有AttackFSM基础上增加SkillR_Start和SkillR_Loop两个状态，通过AttackState枚举管理状态流转，StateMachineBehaviour监听动画完成事件触发状态切换。

**Tech Stack:** Unity 2022.3, C#, Animator StateMachineBehaviour, HybridCLR热更新

---

## 文件结构

| 文件 | 操作 | 职责 |
|------|------|------|
| `Character/CharacterData.cs` | 修改 | AttackState枚举新增两个状态 |
| `Skill/SkillConfig.cs` | 修改 | 增加MaxDuration配置字段 |
| `Input/InputManager.cs` | 修改 | 增加IsSkill3Released()方法 |
| `Animation/AnimationDriver.cs` | 修改 | 无需修改（使用现有TriggerSkillR） |
| `Animation/StateBehaviours/AttackStateBehaviour.cs` | 修改 | 支持SkillR_Start/SkillR_Loop状态检测 |
| `FSM/AttackFSM.cs` | 修改 | 核心逻辑：状态管理、持续计时、取消逻辑 |
| `FSM/FSMManager.cs` | 修改 | 事件回调和输入监听 |
| `Sys3CEntry.cs` | 修改 | 监听R键松开事件 |

---

## Task 1: 更新AttackState枚举

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs:22-29`

- [ ] **Step 1: 修改AttackState枚举**

```csharp
public enum AttackState
{
    Idle = 0,
    Attack1 = 1,
    Attack2 = 2,
    SkillQ = 3,
    SkillR_Start = 4,  // 新增：技能R起手阶段
    SkillR_Loop = 5    // 新增：技能R持续循环阶段
}
```

- [ ] **Step 2: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs
git commit -m "feat(3c-fsm): add SkillR_Start and SkillR_Loop to AttackState enum"
```

---

## Task 2: 更新SkillConfig配置

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs`

- [ ] **Step 1: 添加MaxDuration配置字段**

在 `SkillConfig.cs` 的 `CanUseInAir` 字段后添加：

```csharp
[Header("Duration Skill")]
public float MaxDuration = 3f;  // 最大持续时长（秒），0表示无限制
```

- [ ] **Step 2: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs
git commit -m "feat(skill): add MaxDuration field to SkillConfig"
```

---

## Task 3: 更新InputManager

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs:157-166`

- [ ] **Step 1: 添加IsSkill3Released方法**

在 `IsSkill3Pressed()` 方法后添加：

```csharp
/// <summary>
/// 3技能释放（R键松开）- 用于持续技能取消
/// </summary>
public bool IsSkill3Released()
{
    return UnityInput.GetKeyUp(KeyCode.R);
}
```

- [ ] **Step 2: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs
git commit -m "feat(input): add IsSkill3Released method for duration skill cancel"
```

---

## Task 4: 更新AttackStateBehaviour

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs`

- [ ] **Step 1: 更新哈希值和状态检测**

将当前的 `HASH_SkillR` 改为两个独立的哈希：

```csharp
private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");
private static readonly int HASH_SkillQ = Animator.StringToHash("AttackQ");  // SkillQ在Animator中叫AttackQ
private static readonly int HASH_SkillR_Start = Animator.StringToHash("SkillR_Start");  // 新增
private static readonly int HASH_SkillR_Loop = Animator.StringToHash("SkillR_Loop");      // 新增
```

- [ ] **Step 2: 更新IsAttackState方法**

```csharp
private bool IsAttackState(AnimatorStateInfo stateInfo)
{
    var hash = stateInfo.shortNameHash;
    return hash == HASH_Attack1 || hash == HASH_Attack2 ||
           hash == HASH_SkillQ ||
           hash == HASH_SkillR_Start || hash == HASH_SkillR_Loop;
}
```

- [ ] **Step 3: 更新GetStateName方法**

```csharp
private string GetStateName(AnimatorStateInfo stateInfo)
{
    var hash = stateInfo.shortNameHash;
    if (hash == HASH_Attack1) return "Attack1";
    if (hash == HASH_Attack2) return "Attack2";
    if (hash == HASH_SkillQ) return "AttackQ";
    if (hash == HASH_SkillR_Start) return "SkillR_Start";
    if (hash == HASH_SkillR_Loop) return "SkillR_Loop";
    return "Unknown";
}
```

- [ ] **Step 4: 更新OnStateUpdate中的完成检测**

当前 `IsAttackState(stateInfo) && stateInfo.normalizedTime >= 0.95f` 的检测会触发所有攻击状态的完成回调。需要确保 SkillR_Loop 不会触发完成（只有SkillR_Start会）。

在 `OnStateUpdate` 方法中，将技能状态排除在完成检测外：

```csharp
// 检查动画是否接近完成（normalizedTime >= 0.95）
// SkillR_Start 需要触发完成回调，但 SkillR_Loop 不需要
if (IsAttackState(stateInfo) && stateInfo.normalizedTime >= 0.95f && stateInfo.normalizedTime < 1.1f)
{
    // 排除 SkillR_Loop - 它会循环播放直到被取消
    var hash = stateInfo.shortNameHash;
    if (hash == HASH_SkillR_Loop) return;  // 不触发完成回调

    string stateName = GetStateName(stateInfo);
    Debug.Log($"[AttackBehaviour] {stateName} near completion, normalizedTime={stateInfo.normalizedTime}");

    if (_onAnimationCompleted != null)
    {
        _onAnimationCompleted.Invoke(stateName);
    }
}
```

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs
git commit -m "feat(animation): support SkillR_Start and SkillR_Loop states"
```

---

## Task 5: 更新AttackFSM核心逻辑

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs`

- [ ] **Step 1: 添加SkillR持续相关字段**

在类开头添加：

```csharp
// 技能R持续状态
private float _skillRDuration;  // 当前持续时间
private float _skillRMaxDuration = 3f;  // 最大持续时间（可配置）
private bool _isSkillRActive;  // 是否正在持续技能中
```

- [ ] **Step 2: 添加设置最大持续时间的方法**

```csharp
/// <summary>
/// 设置技能R的最大持续时间
/// </summary>
public void SetSkillRMaxDuration(float duration)
{
    _skillRMaxDuration = duration;
}
```

- [ ] **Step 3: 添加取消技能R的方法**

```csharp
/// <summary>
/// 取消技能R（松开按键或超时）
/// </summary>
public void CancelSkillR()
{
    if (_currentState == AttackState.SkillR_Start || _currentState == AttackState.SkillR_Loop)
    {
        Debug.Log("[AttackFSM] CancelSkillR called");
        _isSkillRActive = false;
        _skillRDuration = 0f;
        ReturnToIdle();
        OnSkillCompleted?.Invoke();
        OnSkillOrAttackEnded?.Invoke();
    }
}
```

- [ ] **Step 4: 修改RequestSkillR方法**

将状态改为 `SkillR_Start`：

```csharp
public void RequestSkillR(bool isGrounded)
{
    Debug.Log($"[AttackFSM] RequestSkillR called, current state: {_currentState}, isGrounded: {isGrounded}");
    if (!isGrounded) return;

    if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
    {
        _currentState = AttackState.SkillR_Start;
        _comboCount = 0;
        _framesInState = 0;
        _comboUnlocked = false;
        _isSkillRActive = true;
        _skillRDuration = 0f;
        _driver.SetAttackState(_currentState);
        _driver.TriggerSkillR();

        Debug.Log("[AttackFSM] RequestSkillR: changed to SkillR_Start");
    }
    else
    {
        Debug.Log("[AttackFSM] RequestSkillR blocked, current state is not Idle/Attack1/2");
    }
}
```

- [ ] **Step 5: 添加SkillR_Start完成处理方法**

```csharp
/// <summary>
/// SkillR起手动画完成，进入循环阶段
/// </summary>
public void OnSkillRStartCompleted()
{
    if (_currentState == AttackState.SkillR_Start && _isSkillRActive)
    {
        Debug.Log("[AttackFSM] SkillR_Start completed, entering SkillR_Loop");
        _currentState = AttackState.SkillR_Loop;
        _driver.SetAttackState(_currentState);
        // SkillR_Loop动画会循环播放
    }
}
```

- [ ] **Step 6: 修改Update方法**

在现有Update逻辑中添加SkillR持续时间检测：

```csharp
public void Update(float deltaTime)
{
    // 更新霸体计时器
    if (_superArmorTime > 0)
    {
        _superArmorTime -= deltaTime;
        if (_superArmorTime < 0) _superArmorTime = 0;
    }

    // 更新技能状态计时器（SkillQ一次性技能）
    if (_currentState == AttackState.SkillQ)
    {
        _skillStateTimer += deltaTime;
        if (_skillStateTimer >= SKILL_TIMEOUT)
        {
            Debug.LogWarning("[AttackFSM] Skill state timeout, forcing return to idle");
            ReturnToIdle();
        }
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

- [ ] **Step 7: 修改OnAnimationCompleted方法**

将现有的 `"SkillR"` 改为处理 `"SkillR_Start"`：

```csharp
public void OnAnimationCompleted(string stateName)
{
    switch (stateName)
    {
        case "Attack1":
        case "Attack2":
            ReturnToIdle();
            OnAttackCompleted?.Invoke();
            OnSkillOrAttackEnded?.Invoke();
            break;
        case "AttackQ":
            ReturnToIdle();
            OnSkillCompleted?.Invoke();
            OnSkillOrAttackEnded?.Invoke();
            break;
        case "SkillR_Start":
            // 起手动画完成，进入循环阶段
            OnSkillRStartCompleted();
            break;
        case "SkillR_Loop":
            // Loop状态下不应触发完成，除非被取消
            break;
    }
}
```

- [ ] **Step 8: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs
git commit -m "feat(3c-fsm): implement SkillR duration state management with start and loop phases"
```

---

## Task 6: 更新FSMManager

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`

- [ ] **Step 1: 修改HandleAnimationCompleted**

将现有的 `"SkillR"` case 改为 `"SkillR_Start"`：

```csharp
case "Attack1":
case "Attack2":
case "AttackQ":
    _attackFSM.OnAnimationCompleted(stateName);
    Debug.Log($"[FSMManager] After OnAnimationCompleted, AttackFSM state: {_attackFSM.CurrentState}");
    _driver.ResetAttackTrigger();
    _driver.ResetSkillQTrigger();
    _driver.ResetSkillRTrigger();
    break;
case "SkillR_Start":
    _attackFSM.OnAnimationCompleted(stateName);
    Debug.Log($"[FSMManager] After OnAnimationCompleted, AttackFSM state: {_attackFSM.CurrentState}");
    // 不重置SkillR trigger，保持状态
    break;
```

- [ ] **Step 2: 添加R键松开检测属性**

添加一个属性供外部检测是否应该取消技能R：

```csharp
/// <summary>
/// 是否应该取消技能R（由输入系统检测松开R键）
/// </summary>
public bool ShouldCancelSkillR { get; private set; }

private void Update()
{
    // 检测R键释放
    ShouldCancelSkillR = false;
}
```

- [ ] **Step 3: 添加取消SkillR的公共方法**

```csharp
/// <summary>
/// 取消技能R（由Sys3CEntry调用）
/// </summary>
public void CancelSkillR()
{
    _attackFSM.CancelSkillR();
}
```

- [ ] **Step 4: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs
git commit -m "feat(3c-fsm): handle SkillR_Start animation completion in FSMManager"
```

---

## Task 7: 更新Sys3CEntry

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: 在HandleInput中添加R键松开检测**

在 `HandleInput()` 方法中添加：

```csharp
private void HandleInput()
{
    // 跳跃
    if (_inputManager.IsJumpPressed())
    {
        _cc.RequestJump();
    }

    // 攻击
    if (_inputManager.IsAttackPressed())
    {
        _fsmManager.RequestNormalAttack();
    }

    // 技能Q（普通攻击升级）
    if (_inputManager.IsSkill2Pressed())
    {
        TryUseSkill(SkillDefs.SkillQ);
    }

    // 技能R按下（仅在地面）
    if (_inputManager.IsSkill3Pressed())
    {
        TryUseSkill(SkillDefs.SkillR);
    }

    // 技能R松开检测 - 取消持续技能
    if (_inputManager.IsSkill3Released())
    {
        var state = _fsmManager.Coordinator?.AttackState;
        if (state == AttackState.SkillR_Loop)
        {
            Debug.Log("[Sys3CEntry] R key released, canceling SkillR");
            _fsmManager.CancelSkillR();
        }
    }
}
```

注意：需要在文件顶部添加对 `AttackState` 的引用。

- [ ] **Step 2: 从SkillConfig读取MaxDuration并设置**

在 `RegisterDefaultSkills()` 或 Start() 中添加：

```csharp
private void RegisterDefaultSkills()
{
    var configs = Resources.LoadAll<Skill.SkillConfig>("Skills");
    _skillRegistry.RegisterRange(configs);

    // 设置技能R的最大持续时间
    foreach (var config in configs)
    {
        if (config.SkillId == SkillDefs.SkillR)
        {
            _fsmManager.SetSkillRMaxDuration(config.MaxDuration);
            Debug.Log($"[Sys3CEntry] SkillR MaxDuration set to {config.MaxDuration}s");
            break;
        }
    }

    Debug.Log("[Sys3CEntry] Registered " + configs.Length + " skills");
}
```

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat(3c-entry): handle R key release to cancel duration skill"
```

---

## Task 8: 测试验证

**Files:**
- 在Unity Editor中测试

- [ ] **Step 1: 测试技能R开始**

1. 运行游戏
2. 按R键施放技能R
3. 观察日志应显示：`RequestSkillR: changed to SkillR_Start`
4. 观察角色播放SkillR_Start动画

- [ ] **Step 2: 测试起手动画完成进入循环**

1. 等待起手动画播放完成
2. 观察日志应显示：`SkillR_Start completed, entering SkillR_Loop`
3. 观察角色播放SkillR_Loop循环动画

- [ ] **Step 3: 测试松开R键取消**

1. 在SkillR_Loop状态下松开R键
2. 观察日志应显示：`R key released, canceling SkillR`
3. 观察角色停止播放循环动画，返回Idle状态

- [ ] **Step 4: 测试最大时长限制**

1. 在SkillConfig中设置较小的MaxDuration值（如1秒）
2. 施放技能R
3. 观察达到最大时长后自动取消

---

## 验证清单

- [ ] AttackState枚举包含Idle, Attack1, Attack2, SkillQ, SkillR_Start, SkillR_Loop
- [ ] SkillConfig包含MaxDuration字段
- [ ] InputManager包含IsSkill3Released方法
- [ ] AttackStateBehaviour正确识别SkillR_Start和SkillR_Loop状态
- [ ] AttackFSM正确管理状态流转
- [ ] Sys3CEntry在R键松开时调用CancelSkillR
- [ ] Animator Controller中SkillR_Start和SkillR_Loop状态已创建
- [ ] 技能R可正常施放、进入循环、松开取消

---

## 注意事项

1. **Animator Controller配置**：需要确保SkillR_Start和SkillR_Loop两个状态在Animator中已创建，并且：
   - SkillR_Start: Loop Time = false
   - SkillR_Loop: Loop Time = true
   - 从Idle到SkillR_Start的转换条件使用SkillR trigger

2. **SkillConfig更新**：运行 `SkillConfigGenerator` 或手动在Inspector中设置SkillR的MaxDuration值

3. **HybridCLR热更新**：所有修改的脚本都会在运行时重新加载，无需额外操作