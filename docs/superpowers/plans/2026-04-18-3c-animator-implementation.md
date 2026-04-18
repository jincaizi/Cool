# 3C System Animator Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `Character3C.controller` with RpgDuo SwordAndShield animations, extend `CharacterData.cs` with new enums, refactor `CharacterAnimationController.cs` into Driver pattern, and create `AnimationEventHandler.cs`.

**Architecture:** A single AnimatorController (`Character3C.controller`) with a state machine driving all character states. Code-based `CharacterAnimationDriver` sets Animator parameters and handles jump/attack/combat logic. `AnimationEventHandler` bridges Unity AnimationEvents to the Driver.

**Tech Stack:** Unity 2022 LTS, HybridCLR Hotfix, Animator Controller, C# value types.

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `Assets/RpgDuo/Animator/Character3C.controller` | Rebuild | State machine with 12 states bound to RpgDuo animation clips |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs` | Modify | Add `CharacterState`, `JumpPhase`, `AttackPhase` enums; extend `CharacterData` struct |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterAnimationController.cs` | Modify | Full refactor into `CharacterAnimationDriver` class with all animation control methods |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/AnimationEventHandler.cs` | Create | MonoBehaviour on character prefab bridging Unity AnimationEvent to Driver calls |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | Modify | Wire `AnimationEventHandler` component, remove old `CharacterAnimationController` usage |

---

## RpgDuo Animation Clip GUID Reference

Used when setting `m_Motion` on each AnimatorState in the controller YAML:

| State Name | Clip GUID |
|------------|-----------|
| Idle_Normal | `423aabfede0896f4db862ab8e54dde30` |
| Idle_Battle | `0308cf4e83cf517488b60af58b290fe0` |
| Move_Normal | `0791e523b7f1b2740a3b1cde42b6aeac` |
| Run_Battle | `5eee3d6dbfbcef04ab20b548575d7b9d` |
| JumpStart | (from InPlace folder — needs GUID lookup) |
| JumpAir | (from InPlace folder — needs GUID lookup) |
| JumpEnd | (from InPlace folder — needs GUID lookup) |
| Attack01 | `db509ad77f9b4f84a8eb1989f589b24c` |
| Attack02 | `8283fadf2c89507469495f30db8680db` |
| Attack03 | `9a6c3585df66f2e4782635fc7a23494c` |
| Attack04 | `b267a2c210dbd1d4badc3f270df6d12d` |
| Die01 | `5940bb0b55717a746bbbe4d3e47e7e39` |

> **Note:** Jump animation clip GUIDs need to be verified in the asset database after running the asset refresh in Task 1.

---

## Task 1: Rebuild Character3C.controller

**Files:**
- Create: `Assets/RpgDuo/Animator/Character3C.controller` (replace existing)
- Verify: Jump animation clip GUIDs from `Assets/RpgDuo/Animation/SwordAndShield/InPlace/*.fbx`

- [ ] **Step 1: Find jump animation clip GUIDs**

Run via MCP after asset refresh:
```csharp
// Use script-execute to print jump clip GUIDs
var paths = new[] {
    "Assets/RpgDuo/Animation/SwordAndShield/InPlace/JumpStart_Normal_InPlace_SwordAndShield.fbx",
    "Assets/RpgDuo/Animation/SwordAndShield/InPlace/JumpAir_Normal_InPlace_SwordAndShield.fbx",
    "Assets/RpgDuo/Animation/SwordAndShield/InPlace/JumpEnd_Normal_InPlace_SwordAndShield.fbx"
};
foreach (var p in paths) {
    var guid = UnityEditor.AssetDatabase.AssetPathToGUID(p);
    UnityEngine.Debug.Log($"{p} -> {guid}");
}
```

- [ ] **Step 2: Write Character3C.controller YAML**

Create `Assets/RpgDuo/Animator/Character3C.controller` with the following structure (YAML 1.1, Unity 2022 format):

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1102 &-NN State definitions (one per state, with m_Motion pointing to clip via GUID)
--- !u!1101 &-NN Transition definitions (with conditions using parameter comparisons)
--- !u!1107 &-NN AnimatorStateMachine (Base Layer)
--- !u!1107 &-NN AnimatorStateMachine (Attack Layer — for overlay)
--- !u!91 &9100000 AnimatorController
```

States to create (all in Base Layer):
1. `Idle` — motion: `Idle_Normal` clip GUID
2. `BattleIdle` — motion: `Idle_Battle` clip GUID
3. `Move` — motion: `Move_Normal` clip GUID
4. `Run` — motion: `Run_Battle` clip GUID
5. `JumpStart` — motion: `JumpStart` clip GUID
6. `JumpAir` — motion: `JumpAir` clip GUID, loop: true
7. `JumpEnd` — motion: `JumpEnd` clip GUID
8. `Death` — motion: `Die01` clip GUID
9. `Attack1` — motion: `Attack01` clip GUID
10. `Attack2` — motion: `Attack02` clip GUID
11. `Attack3` — motion: `Attack03` clip GUID
12. `Attack4` — motion: `Attack04` clip GUID

Parameters to declare in Controller:
- `State` (Int, default 0)
- `SubState` (Int, default 0)
- `IsBattle` (Bool, default false)
- `IsMoving` (Bool, default false)
- `IsJumping` (Bool, default false)
- `IsDead` (Bool, default false)
- `JumpPhase` (Int, default 0)
- `AttackPhase` (Int, default 0)
- `Speed` (Float, default 0)

Key transitions (with conditions):
- Any state → Death: `IsDead == true`
- Idle → BattleIdle: `IsBattle == true`
- Idle → Move: `IsMoving == true && IsBattle == false`
- Idle → Run: `IsMoving == true && IsBattle == true`
- Idle → JumpStart: `IsJumping == true`
- BattleIdle → Idle: `IsBattle == false && !IsMoving`
- BattleIdle → Run: `IsMoving == true`
- BattleIdle → JumpStart: `IsJumping == true`
- BattleIdle → Attack1: `SubState == 1`
- Move → Idle: `IsMoving == false`
- Move → Run: `IsBattle == true`
- Move → JumpStart: `IsJumping == true`
- Run → BattleIdle: `IsMoving == false`
- Run → JumpStart: `IsJumping == true`
- JumpStart → JumpAir: (hasExitTime, no condition — driven by animation event)
- JumpAir → JumpEnd: `JumpPhase == 0` (landing sets JumpPhase to 0)
- JumpAir → Attack1: `SubState == 1` (overlay via blend tree or additive layer)
- JumpEnd → Idle/BattleIdle: `JumpPhase == 0 && !IsJumping`
- Attack1 → Attack2: `SubState == 2` (combo)
- Attack1 → BattleIdle: (attack complete, no combo)
- etc. for Attack2→Attack3, Attack3→Attack4, Attack4→BattleIdle

- [ ] **Step 3: Add Attack Layer for overlay**

Create a second Animator layer called "Attack Layer":
- Weight: 1
- Blending: Additive or Override (set to Additive for jump+attack overlay)
- This layer holds the Attack states and blends additively over Base Layer
- When `SubState > 0`, this layer plays the attack animation on top of jump/movement

- [ ] **Step 4: Refresh assets and verify**

Run `mcp__ai-game-developer__assets-refresh` and verify `Character3C.controller` appears in asset database with 12 states.

- [ ] **Step 5: Commit**

```bash
git add "Assets/RpgDuo/Animator/Character3C.controller"
git commit -m "feat(animator): rebuild Character3C.controller with RpgDuo SwordAndShield animations"
```

---

## Task 2: Extend CharacterData.cs with New Enums

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs`

- [ ] **Step 1: Add new enums after existing `CharacterState` enum**

```csharp
/// <summary>
/// Jump animation phase (drives JumpStart → JumpAir → JumpEnd)
/// </summary>
public enum JumpPhase
{
    None = 0,
    Start = 1,
    Air = 2,
    End = 3
}

/// <summary>
/// Attack combo phase (0 = not attacking, 1-4 = attack index)
/// </summary>
public enum AttackPhase
{
    None = 0,
    Attack1 = 1,
    Attack2 = 2,
    Attack3 = 3,
    Attack4 = 4
}
```

- [ ] **Step 2: Update `CharacterState` enum values to match spec**

```csharp
public enum CharacterState
{
    Idle = 0,
    BattleIdle = 1,
    Move = 2,
    Run = 3,
    JumpStart = 4,
    JumpAir = 5,
    JumpEnd = 6,
    Death = 7
}
```

- [ ] **Step 3: Extend `CharacterData` struct with new fields**

```csharp
public struct CharacterData
{
    // ... existing fields ...

    /// <summary>
    /// Current jump phase (Start/Air/End)
    /// </summary>
    public JumpPhase JumpPhase;

    /// <summary>
    /// Current attack phase (None or Attack1-4)
    /// </summary>
    public AttackPhase AttackPhase;

    /// <summary>
    /// Whether combat mode is active (set true on any attack)
    /// </summary>
    public bool IsBattle;

    /// <summary>
    /// Combo window active — next attack input queues next combo
    /// </summary>
    public bool ComboWindowActive;
}
```

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs"
git commit -m "feat(3c): add CharacterState, JumpPhase, AttackPhase enums and extend CharacterData"
```

---

## Task 3: Refactor CharacterAnimationController.cs into CharacterAnimationDriver

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterAnimationController.cs`
- (Rename class from `CharacterAnimationController` to `CharacterAnimationDriver`)

- [ ] **Step 1: Replace class declaration**

```csharp
namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色动画驱动器 — 通过 Animator 参数驱动角色动画状态机
    /// 负责：跳跃三段、攻击连招、战斗状态切换、移动/奔跑状态
    /// </summary>
    public class CharacterAnimationDriver
    {
        private readonly Animator _animator;

        // Cached parameter hashes
        private static readonly int HASH_State = Animator.StringToHash("State");
        private static readonly int HASH_SubState = Animator.StringToHash("SubState");
        private static readonly int HASH_IsBattle = Animator.StringToHash("IsBattle");
        private static readonly int HASH_IsMoving = Animator.StringToHash("IsMoving");
        private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int HASH_IsDead = Animator.StringToHash("IsDead");
        private static readonly int HASH_JumpPhase = Animator.StringToHash("JumpPhase");
        private static readonly int HASH_AttackPhase = Animator.StringToHash("AttackPhase");
        private static readonly int HASH_Speed = Animator.StringToHash("Speed");

        // Current phase tracking
        private JumpPhase _currentJumpPhase = JumpPhase.None;
        private AttackPhase _currentAttackPhase = AttackPhase.None;
        private bool _isInCombat = false;
        private int _comboCount = 0;
        private const int MAX_COMBO = 4;

        public CharacterAnimationDriver(Animator animator)
        {
            _animator = animator ?? throw new System.ArgumentNullException(nameof(animator));
        }
```

- [ ] **Step 2: Add `Update` method**

```csharp
        /// <summary>
        /// 每帧更新 — 驱动基础 Animator 参数
        /// </summary>
        public void Update(CharacterData data)
        {
            if (_animator == null) return;

            _animator.SetFloat(HASH_Speed, data.Velocity.magnitude, 0.1f, Time.deltaTime);
            _animator.SetBool(HASH_IsBattle, data.IsBattle);
            _animator.SetBool(HASH_IsMoving, data.IsMoving);
            _animator.SetBool(HASH_IsDead, data.IsGrounded == false && data.State == CharacterState.Death);

            // Sync JumpPhase
            if (_currentJumpPhase != data.JumpPhase)
            {
                _currentJumpPhase = data.JumpPhase;
                _animator.SetInteger(HASH_JumpPhase, (int)data.JumpPhase);
            }

            // Sync AttackPhase
            if (_currentAttackPhase != data.AttackPhase)
            {
                _currentAttackPhase = data.AttackPhase;
                _animator.SetInteger(HASH_AttackPhase, (int)data.AttackPhase);
            }
        }
```

- [ ] **Step 3: Add `EnterBattle` and `ExitBattle`**

```csharp
        /// <summary>
        /// 进入战斗状态（攻击时自动调用）
        /// </summary>
        public void EnterBattle()
        {
            _isInCombat = true;
            _animator.SetBool(HASH_IsBattle, true);
        }

        /// <summary>
        /// 退出战斗状态（预留，后续扩展）
        /// </summary>
        public void ExitBattle()
        {
            _isInCombat = false;
            _animator.SetBool(HASH_IsBattle, false);
        }
```

- [ ] **Step 4: Add `SetMoving`**

```csharp
        /// <summary>
        /// 设置移动状态
        /// </summary>
        public void SetMoving(bool moving)
        {
            _animator.SetBool(HASH_IsMoving, moving);
        }
```

- [ ] **Step 5: Add `OnJumpStart`**

```csharp
        /// <summary>
        /// 开始跳跃 — 驱动 JumpStart 状态
        /// </summary>
        public void OnJumpStart()
        {
            _animator.SetBool(HASH_IsJumping, true);
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.Start);
            _currentJumpPhase = JumpPhase.Start;
            _animator.SetInteger(HASH_State, (int)CharacterState.JumpStart);
        }
```

- [ ] **Step 6: Add `OnJumpToAir` (called by AnimationEvent)**

```csharp
        /// <summary>
        /// 跳跃过渡到空中（动画事件触发）
        /// </summary>
        public void OnJumpToAir()
        {
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.Air);
            _currentJumpPhase = JumpPhase.Air;
            _animator.SetInteger(HASH_State, (int)CharacterState.JumpAir);
        }
```

- [ ] **Step 7: Add `OnJumpEnd` / landing**

```csharp
        /// <summary>
        /// 落地 — 驱动 JumpEnd 状态
        /// </summary>
        public void OnLanding(bool returnToBattle)
        {
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.End);
            _currentJumpPhase = JumpPhase.End;
            _animator.SetInteger(HASH_State, (int)CharacterState.JumpEnd);
            _animator.SetBool(HASH_IsJumping, false);

            // After JumpEnd animation, return to appropriate idle
            // This is handled by AnimationEvent "JumpEndComplete" calling OnJumpEndComplete
        }

        /// <summary>
        /// 跳跃结束，回到 Idle 或 BattleIdle（动画事件触发）
        /// </summary>
        public void OnJumpEndComplete()
        {
            _currentJumpPhase = JumpPhase.None;
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.None);
            _animator.SetInteger(HASH_State, _isInCombat
                ? (int)CharacterState.BattleIdle
                : (int)CharacterState.Idle);
        }
```

- [ ] **Step 8: Add `OnAttack` and combo logic**

```csharp
        /// <summary>
        /// 开始攻击（支持地面和空中）
        /// </summary>
        public void OnAttack(int attackIndex)
        {
            if (attackIndex < 1 || attackIndex > MAX_COMBO) return;

            // Enter combat on first attack
            if (!_isInCombat)
                EnterBattle();

            _comboCount = attackIndex;
            _currentAttackPhase = (AttackPhase)attackIndex;

            _animator.SetInteger(HASH_SubState, attackIndex);
            _animator.SetInteger(HASH_AttackPhase, attackIndex);
        }

        /// <summary>
        /// 空中攻击（不打断跳跃状态，叠加动画）
        /// </summary>
        public void OnAttackInAir(int attackIndex)
        {
            OnAttack(attackIndex);
            // SubState drives Attack Layer which blends additively over JumpAir
        }

        /// <summary>
        /// 攻击完成（动画事件触发）
        /// </summary>
        public void OnAttackComplete()
        {
            _currentAttackPhase = AttackPhase.None;
            _animator.SetInteger(HASH_SubState, 0);
            _animator.SetInteger(HASH_AttackPhase, 0);
        }

        /// <summary>
        /// 尝试连击下一击（在 ComboWindowActive 期间调用）
        /// </summary>
        public void TryComboNext()
        {
            if (_comboCount < MAX_COMBO)
            {
                _comboCount++;
                OnAttack(_comboCount);
            }
        }
```

- [ ] **Step 9: Add `OnDeath`**

```csharp
        /// <summary>
        /// 死亡 — 停止所有状态，播放死亡动画
        /// </summary>
        public void OnDeath()
        {
            _animator.SetBool(HASH_IsDead, true);
            _animator.SetBool(HASH_IsJumping, false);
            _animator.SetBool(HASH_IsMoving, false);
            _animator.SetInteger(HASH_State, (int)CharacterState.Death);
        }
```

- [ ] **Step 10: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterAnimationController.cs"
git commit -m "refactor(3c): rename CharacterAnimationController to CharacterAnimationDriver with full jump/attack/combat logic"
```

---

## Task 4: Create AnimationEventHandler

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/AnimationEventHandler.cs`

- [ ] **Step 1: Write AnimationEventHandler MonoBehaviour**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 挂载到角色 Prefab 上，接收 Unity AnimationEvent 并转发给 CharacterAnimationDriver
    /// 需要在 Animator Controller 的动画 Clip 上添加 AnimationEvent：
    /// - "OnJumpToAir" → 绑定 JumpStart 动画末尾，触发从 JumpStart 切换到 JumpAir
    /// - "OnJumpEndComplete" → 绑定 JumpEnd 动画末尾，触发回到 Idle/BattleIdle
    /// - "OnAttackComplete" → 绑定 Attack01~04 动画末尾，触发攻击完成
    /// </summary>
    public class AnimationEventHandler : MonoBehaviour
    {
        [SerializeField] private CharacterAnimationDriver _driver;

        private void Awake()
        {
            if (_driver == null)
                _driver = GetComponent<CharacterAnimationDriver>();
        }

        /// <summary>
        /// 动画事件：跳跃过渡到空中
        /// </summary>
        public void OnJumpToAir()
        {
            _driver?.OnJumpToAir();
        }

        /// <summary>
        /// 动画事件：跳跃结束
        /// </summary>
        public void OnJumpEndComplete()
        {
            _driver?.OnJumpEndComplete();
        }

        /// <summary>
        /// 动画事件：攻击完成
        /// </summary>
        public void OnAttackComplete()
        {
            _driver?.OnAttackComplete();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/AnimationEventHandler.cs"
git commit -m "feat(3c): add AnimationEventHandler to bridge Unity AnimationEvent to CharacterAnimationDriver"
```

---

## Task 5: Wire Sys3CEntry.cs to New Animation System

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Update field declaration**

Change:
```csharp
private CharacterAnimationController _animationController;
```
To:
```csharp
private CharacterAnimationDriver _animationDriver;
private AnimationEventHandler _animationEventHandler;
```

- [ ] **Step 2: Update initialization in Start()**

Replace:
```csharp
if (_animator != null)
    _animationController = new CharacterAnimationController(_animator);
```

With:
```csharp
if (_animator != null)
{
    _animationDriver = new CharacterAnimationDriver(_animator);
    _animationEventHandler = GetComponent<AnimationEventHandler>();
    if (_animationEventHandler == null)
        _animationEventHandler = gameObject.AddComponent<AnimationEventHandler>();
    // Inject driver reference so events can call back
    _animationEventHandler.SetDriver(_animationDriver);
}
```

- [ ] **Step 3: Add SetDriver method to AnimationEventHandler**

Add to `AnimationEventHandler`:
```csharp
public void SetDriver(CharacterAnimationDriver driver)
{
    _driver = driver;
}
```

Also make the `_driver` field settable:
```csharp
[SerializeField] private CharacterAnimationDriver _driver;
```

- [ ] **Step 4: Update Update() loop**

Replace:
```csharp
_animationController?.Update(_characterController.Data);
```

With:
```csharp
_animationDriver?.Update(_characterController.Data);
```

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs"
git commit -m "feat(3c): wire Sys3CEntry to CharacterAnimationDriver and AnimationEventHandler"
```

---

## Task 6: Final Verification

- [ ] **Step 1: Refresh assets**

Run `mcp__ai-game-developer__assets-refresh`

- [ ] **Step 2: Open scene and verify**

1. Open `Assets/RpgDuo/Scene/PBRScene.unity` (or any scene with the character)
2. Select `MaleCharacterPBR.prefab` in Hierarchy
3. Verify `Animator` component has `Character3C.controller` assigned
4. Verify `AnimationEventHandler` component is attached
5. Verify Animator Parameters show all 9 parameters (State, SubState, IsBattle, IsMoving, IsJumping, IsDead, JumpPhase, AttackPhase, Speed)
6. Open Animator window, verify state machine has 12 states with correct transitions

- [ ] **Step 3: Play test in editor**

With the scene open and playing:
- Character should start in Idle (State=0, IsBattle=false, IsMoving=false)
- Press WASD → character should move (State=2 or 3 depending on battle mode)
- Press attack key → IsBattle becomes true, Attack1 plays, returns to BattleIdle
- Press Jump → JumpStart → JumpAir (loops) → JumpEnd → Idle
- In air, press attack → Attack plays overlaid on JumpAir

---

## Spec Coverage Check

| Spec Section | Task |
|--------------|------|
| AnimatorController with 12 states | Task 1 |
| 8 Animator Parameters | Task 1 |
| Jump three-phase system (Start/Air/End) | Tasks 1, 3 |
| Attack combo 1-4 | Tasks 1, 3 |
| Combat state (IsBattle) | Tasks 2, 3 |
| CharacterState/JumpPhase/AttackPhase enums | Task 2 |
| AnimationEventHandler for event bridge | Task 4 |
| Sys3CEntry wiring | Task 5 |

All spec requirements covered.

---

## Type Consistency Check

| Item | Definition Location |
|------|-------------------|
| `CharacterState` enum values (0-7) | Task 2 |
| `JumpPhase` enum (0=None, 1=Start, 2=Air, 3=End) | Task 2 |
| `AttackPhase` enum (0=None, 1-4) | Task 2 |
| `CharacterAnimationDriver` class | Task 3 |
| `AnimationEventHandler` class | Task 4 |
| `HASH_State`, `HASH_SubState`, etc. | Task 3 |
| `SetDriver()` method on AnimationEventHandler | Task 5 |

All types consistent across tasks.

---

**Plan complete and saved to `docs/superpowers/plans/2026-04-18-3c-animator-implementation.md`**

Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
