# Light/Heavy Attack System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace single basic attack with single-button light/heavy attack system: tap = light (horizontal AOE), hold = heavy (vertical charged single-target).

**Architecture:** InputManager tracks attack button hold duration. Sys3CEntry routes to SkillCoordinator.HandleLightAttack or HandleHeavyAttack based on hold time (threshold: 0.2s). SkillCoordinator drops old combo-chain logic and enforces cancel-window-gated attack pacing.

**Tech Stack:** Unity 2022.3 LTS, C# Hotfix layer, existing SkillCoordinator/SkillExecutor/SkillStateMachine

---

### Task 1: Add SkillID for Heavy Attack

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillType.cs:24-33`

- [ ] **Step 1: Add HeavyAttack to SkillID enum**

```csharp
    public enum SkillID
    {
        None = 0,
        LightAttack = 10001,    // renamed from BasicAttack1
        HeavyAttack = 10002,    // renamed from BasicAttack2, now charged vertical strike
        BasicAttack3 = 10003,
        SkillQ = 20001,
        SkillR = 20002,
        Ultimate = 30001,
    }
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillType.cs
git commit -m "refactor(skill): rename BasicAttack1/2 to LightAttack/HeavyAttack in SkillID enum"
```

---

### Task 2: Add attack hold tracking to InputManager

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`

- [ ] **Step 1: Add hold-tracking fields and constants**

Replace the existing `_attackConsumed` field (line 26) with hold-tracking fields:

```csharp
        // === 一次性事件 ===
        private bool _jumpConsumed;
        private bool _skill2Consumed;
        private bool _skill3Consumed;

        // === 攻击键长按追踪 ===
        private bool _attackHeld;
        private float _attackHoldStart = -1f;
        private const float HeavyThreshold = 0.2f;
```

- [ ] **Step 2: Add hold tracking to Update()**

Replace `_attackConsumed = false;` in Update() (line 42):

```csharp
        public void Update()
        {
            _jumpConsumed = false;
            _skill2Consumed = false;
            _skill3Consumed = false;

            // Track attack button hold state
            if (UnityInput.GetMouseButtonDown(0))
            {
                _attackHeld = true;
                _attackHoldStart = Time.time;
            }
            if (UnityInput.GetMouseButtonUp(0))
            {
                _attackHeld = false;
                _attackHoldStart = -1f;
            }
        }
```

- [ ] **Step 3: Replace IsAttackPressed, add hold/release methods**

Replace `IsAttackPressed()` (lines 114-123) with:

```csharp
        /// <summary>
        /// 攻击键刚松开，返回按住时长。未松开返回 -1。
        /// </summary>
        public float GetAttackReleaseDuration()
        {
            if (!_attackHeld && _attackHoldStart > 0f)
            {
                float duration = Time.time - _attackHoldStart;
                _attackHoldStart = -1f;
                return duration;
            }
            return -1f;
        }

        /// <summary>
        /// 攻击键是否正在按住且超过指定秒数
        /// </summary>
        public bool IsAttackHeldOver(float seconds)
        {
            return _attackHeld && _attackHoldStart > 0f && (Time.time - _attackHoldStart) >= seconds;
        }

        /// <summary>
        /// 攻击键是否正在按住（持续状态）
        /// </summary>
        public bool IsAttackHeld()
        {
            return _attackHeld;
        }
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs
git commit -m "feat(input): add attack button hold/release tracking for light/heavy attack"
```

---

### Task 3: Clean up old combo logic in SkillCoordinator

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`

- [ ] **Step 1: Remove old combo tracking fields**

Remove lines 29-31 (fields between `_queuedSkill` and events):

```csharp
        // Delete these three lines:
        // 普攻连段追踪
        // private int _currentComboIndex;
        // private float _comboWindowEndTime;
        // private int _lastCompletedComboSkillId;
```

- [ ] **Step 2: Remove TryChainCombo method**

Delete the entire `TryChainCombo()` method (lines 294-311).

- [ ] **Step 3: Remove old HandleBasicAttackInput method**

Delete the entire `HandleBasicAttackInput()` method (lines 139-192).

- [ ] **Step 4: Simplify OnExecutorCompleted**

Replace lines 417-428 with a clean version:

```csharp
        private void OnExecutorCompleted(int skillId)
        {
            CleanupExecutor(skillId);
        }
```

- [ ] **Step 5: Remove combo timeout logic from Update()**

In `Update()` (lines 350-377), remove the combo window timeout block (lines 359-363):

```csharp
            // Delete these lines from Update():
            // 检查连段窗口超时
            // if (_currentComboIndex > 0 && UnityEngine.Time.time > _comboWindowEndTime)
            // {
            //     _currentComboIndex = 0;
            //     _lastCompletedComboSkillId = 0;
            // }
```

- [ ] **Step 6: Remove GetComboCount method**

Delete `GetComboCount()` (lines 477-478):

```csharp
        // Delete:
        // public int GetComboCount() => _currentComboIndex;
```

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs
git commit -m "refactor(skill): remove old combo-chain logic from SkillCoordinator"
```

---

### Task 4: Add HandleLightAttack and HandleHeavyAttack to SkillCoordinator

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`

- [ ] **Step 1: Add HandleLightAttack method**

Insert after `HandleBasicAttackInput` removal location (where the old method was):

```csharp
        /// <summary>
        /// 轻击（横劈）——仅当无技能执行或在可取消窗口内才激活
        /// </summary>
        public void HandleLightAttack()
        {
            int skillId = (int)Definition.SkillID.LightAttack;
            if (!_skillDatabase.TryGetValue(skillId, out var skillData))
                return;

            // 有技能正在执行，检查是否在可取消窗口
            if (_currentSkill != null && _currentSkill.IsActive)
            {
                if (!IsInCancelableWindow())
                    return;
                _currentSkill.ForceComplete();
            }

            // 检查冷却
            if (_cooldownManager.IsOnCooldown(skillId))
                return;

            if (!HasEnoughResources(skillData))
                return;

            var input = SkillInput.BasicAttack(skillId, _owner.transform.forward);
            TryActivateSkill(skillId, input);
        }

        private bool IsInCancelableWindow()
        {
            if (_currentSkill == null) return true;
            float totalDuration = _currentSkill.Data.GetMainAnimationClip()?.length ?? 0.5f;
            if (totalDuration <= 0f) return true;

            return _currentSkill.Data switch
            {
                ComboSkillData combo => combo.IsInCancelableWindow(_currentSkill.ElapsedTime, totalDuration),
                _ => false
            };
        }
```

- [ ] **Step 2: Add HandleHeavyAttack method**

```csharp
        /// <summary>
        /// 重击（竖劈蓄力）——开始蓄力
        /// </summary>
        public void HandleHeavyAttack()
        {
            int skillId = (int)Definition.SkillID.HeavyAttack;
            if (!_skillDatabase.TryGetValue(skillId, out var skillData))
                return;

            // 已在蓄力中，跳过
            if (_currentSkill != null && _currentSkill.IsActive
                && _currentSkill.CurrentSubState == SkillSubState.Charging)
                return;

            // 检查冷却
            if (_cooldownManager.IsOnCooldown(skillId))
                return;

            if (!HasEnoughResources(skillData))
                return;

            var input = SkillInput.ChargingSkill(skillId, _owner.transform.forward);
            TryActivateSkill(skillId, input);
        }

        /// <summary>
        /// 释放重击蓄力
        /// </summary>
        public void HandleHeavyRelease()
        {
            if (_currentSkill != null && _currentSkill.IsActive
                && _currentSkill.CurrentSubState == SkillSubState.Charging)
            {
                _currentSkill.ReleaseCharge();
            }
        }
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs
git commit -m "feat(skill): add HandleLightAttack and HandleHeavyAttack to SkillCoordinator"
```

---

### Task 5: Update Sys3CEntry to route light/heavy attacks

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Replace attack input handling in HandleInput()**

Replace the attack block in `HandleInput()` (lines 172-180):

```csharp
        private void HandleInput()
        {
            if (_inputManager.IsJumpPressed())
            {
                _cc.RequestJump();
            }

            // Attack button: release = light, hold = heavy
            float attackDuration = _inputManager.GetAttackReleaseDuration();
            if (attackDuration >= 0f && attackDuration < 0.2f)
            {
                _skillCoordinator.HandleLightAttack();
            }

            // Heavy attack hold → start charging
            if (_inputManager.IsAttackHeldOver(0.2f))
            {
                _skillCoordinator.HandleHeavyAttack();
            }

            // Heavy attack release → fire
            if (attackDuration >= 0.2f)
            {
                _skillCoordinator.HandleHeavyRelease();
            }

            // ... keep existing Q/R handling unchanged
            if (_inputManager.IsSkill2Pressed())
            // ... rest of file unchanged
```

Wait — there's a problem. Rework this to avoid double-firing on release:

```csharp
        private void HandleInput()
        {
            if (_inputManager.IsJumpPressed())
            {
                _cc.RequestJump();
            }

            // Attack: tap → light, hold → heavy charge, release → fire heavy
            float attackDuration = _inputManager.GetAttackReleaseDuration();
            if (attackDuration >= 0f)
            {
                // Button was just released
                if (attackDuration < 0.2f)
                {
                    _skillCoordinator.HandleLightAttack();
                }
                else
                {
                    // Heavy release — the SkillCoordinator already started charging
                    // on the hold-over-threshold check
                    _skillCoordinator.HandleHeavyRelease();
                }
            }
            else if (_inputManager.IsAttackHeldOver(0.2f))
            {
                // Still holding past threshold — start/continue charging
                _skillCoordinator.HandleHeavyAttack();
            }

            if (_inputManager.IsSkill2Pressed())
            {
                int skillQId = GetSkillQId();
                if (skillQId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillQId, transform.position + transform.forward * 5f);
                    _skillCoordinator.HandleInput(input);
                }
            }

            if (_inputManager.IsSkill3Pressed())
            {
                int skillRId = GetSkillRId();
                if (skillRId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillRId, transform.position + transform.forward * 5f);
                    _skillCoordinator.HandleInput(input);
                }
            }

            if (_inputManager.IsSkill3Released())
            {
                var executor = _skillCoordinator.CurrentSkill;
                if (executor != null && executor.CurrentSubState == Skills.Definition.SkillSubState.Charging)
                {
                    executor.ReleaseCharge();
                }
            }
        }
```

- [ ] **Step 2: Replace GetBasicAttackSkillId()**

Replace line 281:

```csharp
        private int GetBasicAttackSkillId() => (int)SkillID.LightAttack;
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat(entry): route tap→light, hold→heavy attack in Sys3CEntry"
```

---

### Task 6: Create BA_Heavy skill asset

**Files:**
- Create: `Assets/PreRes/SkillsCfg/BA_Heavy.asset` (ChargedSkillData)

- [ ] **Step 1: Create the asset via MCP**

Use `assets-material-create` pattern — actually use `script-execute` to create the ScriptableObject:

```csharp
var asset = ScriptableObject.CreateInstance<ChargedSkillData>();
AssetDatabase.CreateAsset(asset, "Assets/PreRes/SkillsCfg/BA_Heavy.asset");
```

- [ ] **Step 2: Configure the asset via MCP assets-modify**

Set these fields with pathPatches:

```json
[
  {"Path": "_skillId", "Value": {"typeName": "System.Int32", "value": 10002}},
  {"Path": "_skillName", "Value": {"typeName": "System.String", "value": "Heavy Attack"}},
  {"Path": "_animatorTrigger", "Value": {"typeName": "System.String", "value": "HeavyAttack"}},
  {"Path": "_cooldown", "Value": {"typeName": "System.Single", "value": 0.5}},
  {"Path": "_interruptionPriority", "Value": {"typeName": "System.Int32", "value": 60}},
  {"Path": "_canBeInterruptedByDamage", "Value": {"typeName": "System.Boolean", "value": true}},
  {"Path": "_canCancelIntoBasicAttack", "Value": {"typeName": "System.Boolean", "value": false}},
  {"Path": "_minChargeTime", "Value": {"typeName": "System.Single", "value": 0.3}},
  {"Path": "_maxChargeTime", "Value": {"typeName": "System.Single", "value": 0.8}},
  {"Path": "_holdToCharge", "Value": {"typeName": "System.Boolean", "value": true}},
  {"Path": "_releaseToFire", "Value": {"typeName": "System.Boolean", "value": true}},
  {"Path": "_canMoveWhileCharging", "Value": {"typeName": "System.Boolean", "value": false}},
  {"Path": "_canRotateWhileCharging", "Value": {"typeName": "System.Boolean", "value": false}},
  {"Path": "_shape/_range", "Value": {"typeName": "System.Single", "value": 2.5}},
  {"Path": "_shape/_angleStart", "Value": {"typeName": "System.Single", "value": -10}},
  {"Path": "_shape/_angleEnd", "Value": {"typeName": "System.Single", "value": 10}},
  {"Path": "_shape/_targetType", "Value": {"typeName": "Hotfix.GameSystems.Skills.Data.TargetType", "value": "Single"}},
  {"Path": "_shape/_hitboxTimings/[0]", "Value": {"typeName": "System.Single", "value": 0.35}}
]
```

- [ ] **Step 3: Commit**

```bash
git add Assets/PreRes/SkillsCfg/BA_Heavy.asset Assets/PreRes/SkillsCfg/BA_Heavy.asset.meta
git commit -m "feat(skill): create BA_Heavy charged skill asset for vertical strike"
```

---

### Task 7: Update Sys3CEntry to register BA_Heavy

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:33`

The `_characterSkills` array on the Sys3CEntry MonoBehaviour needs BA_Heavy added via Inspector. This is a manual step in Unity Editor.

- [ ] **Step 1: In Unity Editor, select the player GameObject**

- [ ] **Step 2: In the Inspector, expand the Sys3CEntry component, find the "Character Skills" array**

- [ ] **Step 3: Add BA_Heavy.asset to the array** (drag from Project view or use the + button and pick it)

Note: This step requires Unity Editor open. The code change in `Start()` (line 66) already loops `_characterSkills` and registers every assigned asset, so no code change needed.

---

### Task 8: Verify and test

- [ ] **Step 1: Play in Unity Editor, tap attack button** — verify light attack (horizontal AOE) triggers

- [ ] **Step 2: Hold attack button for >0.2s** — verify heavy charge starts, release → vertical strike executes

- [ ] **Step 3: Rapidly tap attack button** — verify only one attack plays at a time, spam is discarded

- [ ] **Step 4: Check Console for errors** — no NullReferenceException or missing skill warnings

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "chore: final integration verification for light/heavy attack system"
```
