# Combo Hold-Charge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wait for Attack1 to complete naturally, then start charging if button still held — instead of forcefully cancelling Attack1 mid-animation.

**Architecture:** SkillCoordinator fires `OnLightAttackCompleted` when Attack1 finishes. Sys3CEntry listens and checks button state: if held → charge Attack2, if released → do nothing. HandleHeavyAttack removes ForceComplete and instead bails if any skill is active.

**Tech Stack:** Unity 2022.3 LTS, C# Hotfix

---

### Task 1: Add OnLightAttackCompleted event to SkillCoordinator

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`

- [ ] **Step 1: Add event declaration**

After the event declarations (after line 32, before properties):

```csharp
        public event Action OnLightAttackCompleted;
```

- [ ] **Step 2: Fire event in OnExecutorCompleted**

Replace `OnExecutorCompleted` (line 416-419):

```csharp
        private void OnExecutorCompleted(int skillId)
        {
            if (skillId == (int)Definition.SkillID.LightAttack)
                OnLightAttackCompleted?.Invoke();
            CleanupExecutor(skillId);
        }
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs
git commit -m "feat(skill): add OnLightAttackCompleted event to SkillCoordinator"
```

---

### Task 2: Remove ForceComplete from HandleHeavyAttack, add active-skill guard

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`

- [ ] **Step 1: Replace HandleHeavyAttack body**

Replace the entire method (lines 162-190):

```csharp
        /// <summary>
        /// 重击（竖劈蓄力）——仅在无活跃技能时激活
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

            // 有其他活跃技能，不打断
            if (_currentSkill != null && _currentSkill.IsActive)
                return;

            if (_cooldownManager.IsOnCooldown(skillId))
                return;

            if (!HasEnoughResources(skillData))
                return;

            var input = SkillInput.ChargingSkill(skillId, _owner.transform.forward);
            TryActivateSkill(skillId, input);
        }
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs
git commit -m "refactor(skill): remove ForceComplete from HandleHeavyAttack, bail if any skill active"
```

---

### Task 3: Rewire Sys3CEntry — Attack1 completion → charge if held

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Subscribe to OnLightAttackCompleted in Start()**

Add after `_skillCoordinator.OnTargetHit` subscription (after line 74):

```csharp
            _skillCoordinator.OnLightAttackCompleted += () =>
            {
                if (_inputManager.IsAttackHeld())
                {
                    _skillCoordinator.HandleHeavyAttack();
                }
            };
```

- [ ] **Step 2: Replace HandleInput attack block**

Replace lines 173-189 with the simplified version:

```csharp
            // Press → light attack. Light attack completes while held → heavy charge. Release → fire heavy.
            if (_inputManager.IsAttackJustPressed())
            {
                _skillCoordinator.HandleLightAttack();
            }

            float attackDuration = _inputManager.GetAttackReleaseDuration();
            if (attackDuration >= 0f)
            {
                _skillCoordinator.HandleHeavyRelease();
            }
```

- [ ] **Step 3: Remove `_heavyActivatedThisHold` field and all references**

Delete line 42:
```csharp
        private bool _heavyActivatedThisHold;
```

- [ ] **Step 4: Verify no compilation errors in the Editor**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat(entry): wire Attack1 completion to heavy charge via OnLightAttackCompleted callback"
```

---

### Task 4: Update BA_Heavy asset — allow movement/rotation during charge, no auto-release

**Files:**
- Modify: `Assets/PreRes/SkillsCfg/BA_Heavy.asset` (via MCP)

- [ ] **Step 1: Set CanMoveWhileCharging = true, CanRotateWhileCharging = true**

```json
[
  {"Path": "_canMoveWhileCharging", "Value": {"typeName": "System.Boolean", "value": true}},
  {"Path": "_canRotateWhileCharging", "Value": {"typeName": "System.Boolean", "value": true}},
  {"Path": "_maxChargeTime", "Value": {"typeName": "System.Single", "value": 999}}
]
```

Use `assets-modify` MCP tool with `assetPath: "Assets/PreRes/SkillsCfg/BA_Heavy.asset"` and the `pathPatches` above.

- [ ] **Step 2: Verify with assets-get-data**

Check `_canMoveWhileCharging`, `_canRotateWhileCharging`, `_maxChargeTime` are all set correctly.

- [ ] **Step 3: Commit**

```bash
git add Assets/PreRes/SkillsCfg/BA_Heavy.asset
git commit -m "feat(skill): BA_Heavy allow movement/rotation during charge, no auto-release (maxCharge=999)"
```

---

### Task 5: Verify and test

- [ ] **Step 1: Tap attack** → Attack1 plays and finishes naturally

- [ ] **Step 2: Hold attack** → Attack1 plays → completes → charging starts → release → Attack2 fires

- [ ] **Step 3: Tap then release quickly** → Attack1 plays → completes → no charge → nothing more

- [ ] **Step 4: Rapid taps** → Only one Attack1 at a time, extra taps dropped

- [ ] **Step 5: Move during charge** → Character moves freely while holding charge

- [ ] **Step 6: Check Console for errors** → No NullReferenceException or warnings

- [ ] **Step 7: Commit any final fixes**
