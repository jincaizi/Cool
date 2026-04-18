# 3C System Animator Integration Design

**Date:** 2026-04-18
**Status:** Approved

---

## 1. Overview

Integrate the 3C system (Character, Camera, Control) with RpgDuo's `SwordAndShield` animation assets to achieve basic RPG character functionality: idle, battle stance, movement, running, jumping, attack combos, and death. Combat state is entered only when an attack is triggered.

**Base Prefab:** `Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`
**Target Controller:** `Assets/RpgDuo/Animator/Character3C.controller`

---

## 2. Animation Assets (RpgDuo SwordAndShield)

| State | Animation File |
|-------|---------------|
| Idle (Normal) | `Idle_Normal_SwordAndShield.fbx` |
| Idle (Battle) | `Idle_Battle_SwordAndShiled.fbx` |
| Move | `MoveFWD_Normal_InPlace_SwordAndShield.fbx` |
| Run | `SprintFWD_Battle_InPlace_SwordAndShield.fbx` |
| JumpStart | `JumpStart_Normal_InPlace_SwordAndShield.fbx` |
| JumpAir | `JumpAir_Normal_InPlace_SwordAndShield.fbx` |
| JumpEnd | `JumpEnd_Normal_InPlace_SwordAndShield.fbx` |
| Attack1 | `Attack01_SwordAndShiled.fbx` |
| Attack2 | `Attack02_SwordAndShiled.fbx` |
| Attack3 | `Attack03_SwordAndShiled.fbx` |
| Attack4 | `Attack04_SwordAndShiled.fbx` |
| Death | `Die01_SwordAndShield.fbx` |

---

## 3. Animator Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `State` | Int | 0=Idle, 1=BattleIdle, 2=Move, 3=Run, 4=JumpStart, 5=JumpAir, 6=JumpEnd, 7=Death |
| `SubState` | Int | 0=None, 1=Attack1, 2=Attack2, 3=Attack3, 4=Attack4 |
| `IsBattle` | Bool | Combat mode active |
| `IsMoving` | Bool | Currently moving |
| `IsJumping` | Bool | Jump in progress |
| `IsDead` | Bool | Death state (highest priority) |
| `JumpPhase` | Int | 0=None, 1=Start, 2=Air, 3=End |
| `AttackPhase` | Int | 0=None, 1~4=Attack1~4 |
| `Speed` | Float | Movement speed for BlendTree |

---

## 4. State Machine Structure

### Base Layer States

```
Idle (Idle_Normal)
  ├─ [Attack] → Attack1
  ├─ [IsMoving + !IsBattle] → Move
  ├─ [IsMoving + IsBattle] → Run
  ├─ [IsJumping] → JumpStart
  └─ [IsBattle trigger] → BattleIdle

BattleIdle (Idle_Battle)
  ├─ [Attack] → Attack1
  ├─ [!IsMoving] → Idle (exit battle)
  ├─ [IsMoving] → Run
  ├─ [IsJumping] → JumpStart
  └─ [IsDead] → Death

Move (MoveFWD_Normal_InPlace)
  ├─ [!IsMoving] → Idle
  ├─ [IsBattle] → Run
  └─ [IsJumping] → JumpStart

Run (SprintFWD_Battle_InPlace)
  ├─ [!IsMoving] → BattleIdle
  ├─ [IsJumping] → JumpStart
  └─ [IsDead] → Death

JumpStart (JumpStart_Normal_InPlace)
  └─ [Animation Event "JumpToAir"] → JumpAir

JumpAir (JumpAir_Normal_InPlace) [Attack overridable]
  ├─ [Landing detected] → JumpEnd
  └─ [Attack] → Attack1 (combo overlay)

JumpEnd (JumpEnd_Normal_InPlace)
  └─ [Animation Event / timeout] → Idle or BattleIdle

Attack1~4 (Attack01~04_SwordAndShield) [Jump overridable]
  ├─ [Combo window] → Next Attack
  ├─ [Attack complete] → Previous state
  └─ [IsDead] → Death

Death (Die01_SwordAndShield)
  └─ (terminal state)
```

---

## 5. Jump Three-Phase System

Jump animation uses three separate clips with animation events for phase transitions:

- **JumpStart**: Plays once on jump initiation, triggers "JumpToAir" event
- **JumpAir**: Loops during airborne, allows attack overlay
- **JumpEnd**: Plays on landing, returns to Idle/BattleIdle

Code-driven jump phase management allows attack combo overlay in Air phase.

---

## 6. Combat State Logic

- **Entry**: Any attack triggers `IsBattle = true`
- **Exit**: Player manually exits (future) or timeout without combat (future)
- **During combat**: Running uses Sprint animation, idle uses BattleIdle
- **Attack combos**: 4-attack chain with auto-combo window

---

## 7. File Changes

| File | Action | Notes |
|------|--------|-------|
| `Assets/RpgDuo/Animator/Character3C.controller` | Rebuild | Bind RpgDuo animations |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs` | Modify | Add CharacterState, JumpPhase, AttackPhase enums |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterAnimationController.cs` | Modify | Add Driver methods for jump/attack/combat |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/AnimationEventHandler.cs` | New | Bridge Unity AnimationEvent to CharacterAnimationController |

---

## 8. Composite Action Example: Jump Attack

1. Player presses Jump → `CharacterController.OnJump()`
2. `CharacterAnimationDriver.OnJumpStart()` → `IsJumping=true`, `State=JumpStart`
3. JumpStart finishes → AnimationEvent "JumpToAir" → `JumpPhase=Air`, `State=JumpAir`
4. Player presses Attack in air → `CharacterAnimationDriver.OnAttackInAir(1)` → `SubState=Attack1`
5. Attack1 plays overlaid with JumpAir (Animator layer blending or BlendTree)
6. AttackComplete event → `AttackPhase=0`, returns to JumpAir
7. Landing detected → `JumpPhase=End`, `State=JumpEnd`
8. JumpEnd finishes → Return to Idle/BattleIdle

---

## 9. Implementation Order

1. Rebuild `Character3C.controller` with RpgDuo animation bindings
2. Extend `CharacterData.cs` with new enums
3. Refactor `CharacterAnimationController.cs` into Driver pattern
4. Create `AnimationEventHandler.cs` for event bridge
5. Wire up `Sys3CEntry.cs` to use new animation system
6. Test state transitions and combat flow
