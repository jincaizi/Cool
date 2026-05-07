# Combo Attack Window Design

**Date:** 2026-05-07
**Status:** Approved
**Scope:** AttackFSM combo system — Attack1 → Attack2 chaining with time-based windows and input buffering

## Problem

The current combo system has two issues:

1. **FSM level**: `_comboUnlocked` opens after 5 frames (~0.08s) with no concept of a closing window. If the player doesn't press during the narrow effective window, Attack1 completes and the next press is Attack1 again — Attack2 is never reached.

2. **Animator level**: If the Animator Controller has `Any State → Attack1` with the `Attack` trigger, repeated `TriggerAttack()` calls restart Attack1 mid-animation, causing infinite Attack1 looping.

## Design

### Behavior

- **Attack1 lockout phase (0–20%)**: Input is buffered, not dropped.
- **Attack1 window phase (20%–85%)**: Pressing attack chains to Attack2. Buffered input auto-triggers Attack2 at window start.
- **Attack1 wind-down phase (85%–100%)**: Input is ignored. Animation completes → Idle.
- **Attack2**: End of combo. Always returns to Idle. No further chaining.

### AttackFSM Changes (`FSM/AttackFSM.cs`)

Replace frame-based `_comboUnlocked` with time-based window tracking:

```
New fields:
  _attackAnimStartTime (float)  — Time.time when current attack started
  _attackAnimDuration (float)   — cached AnimatorStateInfo.length for current clip
  _inputBuffered (bool)          — true if player pressed during lockout
  _comboWindowOpen (bool)        — true when normalizedTime in [0.2, 0.85)

Constants:
  COMBO_LOCK_RATIO = 0.2f
  COMBO_WINDOW_END_RATIO = 0.85f
```

**RequestNormalAttack logic:**

```
press attack →
  Idle    → start Attack1, record _attackAnimStartTime
  Attack1 + window open → start Attack2
  Attack1 + lockout     → set _inputBuffered = true
  Attack1 + wind-down   → ignore
  Attack2               → ignore
```

**Update logic (in Attack1 state):**

```
normalizedTime = (Time.time - _attackAnimStartTime) / _attackAnimDuration

lockout (0 - 0.2):   nothing
window  (0.2 - 0.85): _comboWindowOpen = true; if _inputBuffered → auto Attack2
wind-down (0.85+):    _comboWindowOpen = false
```

### Animator Fix

- Remove `Any State → Attack1` transition (if exists) to prevent self-restart
- Ensure Attack1 → Attack2 transition is driven by `AttackState` parameter + `Attack` trigger, not by trigger alone

### Constants (future config)

The window ratios live as constants initially. They can move to `FSMConfig` when per-attack configurability is needed.

## Scope

- **AttackFSM.cs**: ~50 lines changed (fields, RequestNormalAttack, Update)
- **AttackStateBehaviour.cs**: Remove dead `_comboUnlocked` / `_framesInState` tracking (now handled in AttackFSM)
- **Animator Controller**: Remove `Any State → Attack1` transition if present

## Implementation Notes

- `_attackAnimDuration` is obtained once on entering Attack1 via `_animator.GetCurrentAnimatorStateInfo(attackLayer).length`
- `normalizedTime` is computed manually (`(Time.time - startTime) / duration`) rather than reading from AnimatorStateInfo, avoiding delta-time variance
- Attack2 needs no window tracking since it always returns to Idle — no further chaining
