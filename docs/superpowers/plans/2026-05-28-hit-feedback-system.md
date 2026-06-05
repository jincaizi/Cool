# Hit Feedback System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add hitstop, camera shake, time slow, and upgraded particle VFX to make monster hits feel weighty (Souls/Monster Hunter style).

**Architecture:** Event-driven — all feedback components subscribe to `MonsterTakeDamageEvent` independently. A shared `HitFeedbackProfile` ScriptableObject provides tuning parameters. No central orchestrator.

**Tech Stack:** Unity 2022.3 LTS, DOTween, EventBus (existing), ParticleSystem, ScriptableObject

**Spec:** `docs/superpowers/specs/2026-05-28-hit-feedback-system-design.md`

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs` | Modify | Add `SkillId`, `ComboIndex` to `MonsterTakeDamageEvent` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs` | Modify | Add `GetEntity(int entityId)` method |
| `Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackProfile.cs` | Create | ScriptableObject with all tuning params |
| `Assets/Scripts/Hotfix/GameSystems/VFX/HitStopManager.cs` | Create | Animator freeze + crit time slow |
| `Assets/Scripts/Hotfix/GameSystems/VFX/CameraShakeManager.cs` | Create | Perlin noise camera shake |
| `Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs` | Modify | Add shockwave/spark, intensity scaling |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` | Modify | Fill new event fields |
| `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs` | Modify | Fill new event fields |
| `Assets/Data/HitFeedbackProfile.asset` | Create | SO instance with defaults |
| `Assets/Prefabs/VFX/HitShockwave.prefab` | Create | Shockwave ring particle |
| `Assets/Prefabs/VFX/HitSparkBurst.prefab` | Create | Spark burst particle |

---

### Task 1: Extend MonsterTakeDamageEvent

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs`

- [ ] **Step 1: Add SkillId and ComboIndex fields to MonsterTakeDamageEvent**

Open `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs`. Replace the `MonsterTakeDamageEvent` struct with:

```csharp
    /// <summary>
    /// 怪物受伤事件（给浮字系统使用）
    /// </summary>
    public struct MonsterTakeDamageEvent : IEvent
    {
        public int EntityId;
        public Vector3 HitPosition;
        public Vector3 HitDirection;
        public int Damage;
        public bool IsCritical;
        public int SkillId;
        public int ComboIndex;

        public MonsterTakeDamageEvent(
            int entityId, Vector3 hitPos, Vector3 hitDir,
            int damage, bool isCritical = false,
            int skillId = 0, int comboIndex = 1)
        {
            EntityId = entityId;
            HitPosition = hitPos;
            HitDirection = hitDir;
            Damage = damage;
            IsCritical = isCritical;
            SkillId = skillId;
            ComboIndex = comboIndex;
        }
    }
```

New params have defaults (`skillId = 0`, `comboIndex = 1`) so all existing call sites compile without changes.

- [ ] **Step 2: Verify compilation**

In Unity Editor, wait for recompile. Check Console for errors. All existing `MonsterTakeDamageEvent(...)` calls should compile because the new params have default values.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs
git commit -m "feat(events): add SkillId and ComboIndex to MonsterTakeDamageEvent"
```

---

### Task 2: Add GetEntity to PhysicsRegistry

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs`

The `HitStopManager` needs to find a monster's Animator by entity ID. PhysicsRegistry stores entities but has no lookup-by-ID method.

- [ ] **Step 1: Add GetEntity method to PhysicsRegistry**

Open `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs`. Add this method after the `Unregister` method (around line 32):

```csharp
        public IDamageable GetEntity(int entityId)
        {
            foreach (var set in _entities.Values)
            {
                foreach (var entity in set)
                {
                    if (entity != null && entity.Transform != null &&
                        entity.Transform.GetInstanceID() == entityId)
                        return entity;
                }
            }
            return null;
        }
```

- [ ] **Step 2: Verify compilation**

Wait for Unity recompile. No errors expected.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs
git commit -m "feat(combat): add GetEntity lookup by instance ID to PhysicsRegistry"
```

---

### Task 3: Create HitFeedbackProfile ScriptableObject

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackProfile.cs`

- [ ] **Step 1: Create the ScriptableObject class**

Create file `Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackProfile.cs`:

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    [CreateAssetMenu(menuName = "Game/HitFeedbackProfile")]
    public class HitFeedbackProfile : ScriptableObject
    {
        [Header("=== HitStop (Animator Freeze) ===")]
        [Tooltip("普通攻击 hitstop 时长(秒)")]
        public float NormalHitStop = 0.03f;

        [Tooltip("技能命中 hitstop 时长(秒)")]
        public float SkillHitStop = 0.08f;

        [Tooltip("暴击额外 hitstop 时长(秒)")]
        public float CritHitStopBonus = 0.04f;

        [Tooltip("hitstop 最大时长上限(秒)")]
        public float MaxHitStop = 0.15f;

        [Tooltip("连击段数加成 (每段 +N 秒)")]
        public float ComboHitStopBonus = 0.01f;

        [Header("=== Camera Shake ===")]
        [Tooltip("普通攻击震动强度")]
        public float NormalShakeIntensity = 0.5f;

        [Tooltip("技能命中震动强度")]
        public float SkillShakeIntensity = 1.5f;

        [Tooltip("暴击震动倍率")]
        public float CritShakeMultiplier = 1.5f;

        [Tooltip("震动持续时间(秒)")]
        public float ShakeDuration = 0.15f;

        [Header("=== Time Slow (Crit / Full Charge) ===")]
        [Tooltip("暴击时时间缩放 (0.3 = 30% 速度)")]
        public float CritTimeSlowScale = 0.3f;

        [Tooltip("暴击慢动作持续时间(秒)")]
        public float CritTimeSlowDuration = 0.3f;

        [Header("=== Particle Intensity ===")]
        [Tooltip("普通攻击粒子缩放")]
        public float NormalParticleScale = 1.0f;

        [Tooltip("技能命中粒子缩放")]
        public float SkillParticleScale = 1.5f;

        [Tooltip("暴击粒子缩放")]
        public float CritParticleScale = 2.0f;
    }
}
```

- [ ] **Step 2: Create the SO asset in Unity**

In Unity Editor:
1. Right-click `Assets/Data/` folder
2. Create → Game → HitFeedbackProfile
3. Name it `HitFeedbackProfile`
4. Verify default values in Inspector match the spec

- [ ] **Step 3: Verify compilation**

Wait for recompile. No errors expected.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackProfile.cs
git add Assets/Data/HitFeedbackProfile.asset
git add Assets/Data/HitFeedbackProfile.asset.meta
git commit -m "feat(vfx): add HitFeedbackProfile ScriptableObject for tuning params"
```

---

### Task 4: Create HitStopManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/HitStopManager.cs`

- [ ] **Step 1: Create HitStopManager**

Create file `Assets/Scripts/Hotfix/GameSystems/VFX/HitStopManager.cs`:

```csharp
using System.Collections;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class HitStopManager : MonoBehaviour
    {
        [SerializeField] private HitFeedbackProfile _profile;
        [SerializeField] private Animator _playerAnimator;

        private bool _playerFrozen;
        private bool _timeSlowing;

        private void OnEnable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnHit);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnHit);
            StopAllCoroutines();
            _playerFrozen = false;
            _timeSlowing = false;
            if (_playerAnimator != null) _playerAnimator.speed = 1f;
            Time.timeScale = 1f;
        }

        private void OnHit(MonsterTakeDamageEvent e)
        {
            float duration = e.SkillId > 0 ? _profile.SkillHitStop : _profile.NormalHitStop;
            if (e.IsCritical) duration += _profile.CritHitStopBonus;
            duration += (e.ComboIndex - 1) * _profile.ComboHitStopBonus;
            duration = Mathf.Min(duration, _profile.MaxHitStop);

            if (_playerAnimator != null && !_playerFrozen)
                StartCoroutine(FreezePlayerAnimator(duration));

            var targetAnim = FindAnimatorById(e.EntityId);
            if (targetAnim != null)
                StartCoroutine(FreezeAnimator(targetAnim, duration));

            if (e.IsCritical && !_timeSlowing)
                StartCoroutine(TimeSlowRoutine(_profile.CritTimeSlowScale, _profile.CritTimeSlowDuration));
        }

        private IEnumerator FreezePlayerAnimator(float duration)
        {
            _playerFrozen = true;
            _playerAnimator.speed = 0f;
            yield return new WaitForSecondsRealtime(duration);
            if (_playerAnimator != null) _playerAnimator.speed = 1f;
            _playerFrozen = false;
        }

        private IEnumerator FreezeAnimator(Animator anim, float duration)
        {
            anim.speed = 0f;
            yield return new WaitForSecondsRealtime(duration);
            if (anim != null) anim.speed = 1f;
        }

        private IEnumerator TimeSlowRoutine(float scale, float duration)
        {
            _timeSlowing = true;
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = 1f;
            _timeSlowing = false;
        }

        private Animator FindAnimatorById(int entityId)
        {
            var entity = PhysicsRegistry.Instance.GetEntity(entityId);
            if (entity is MonsterEntity monster)
                return monster.Animator;
            return null;
        }
    }
}
```

- [ ] **Step 2: Add to scene in Unity**

In Unity Editor:
1. Find or create an empty GameObject named `HitFeedbackManager` in the scene
2. Add `HitStopManager` component
3. Assign `HitFeedbackProfile` asset to the `_profile` field
4. Assign the player's Animator to `_playerAnimator` field
5. Verify no errors in Console

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitStopManager.cs
git commit -m "feat(vfx): add HitStopManager for animator freeze and crit time slow"
```

---

### Task 5: Create CameraShakeManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/CameraShakeManager.cs`

- [ ] **Step 1: Create CameraShakeManager**

Create file `Assets/Scripts/Hotfix/GameSystems/VFX/CameraShakeManager.cs`:

```csharp
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class CameraShakeManager : MonoBehaviour
    {
        [SerializeField] private HitFeedbackProfile _profile;
        [SerializeField] private Transform _camera;

        private Vector3 _originalLocalPos;
        private float _shakeEndTime;
        private float _currentIntensity;

        private void Start()
        {
            if (_camera == null) _camera = Camera.main.transform;
            _originalLocalPos = _camera.localPosition;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnHit);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnHit);
            if (_camera != null)
                _camera.localPosition = _originalLocalPos;
            _currentIntensity = 0f;
        }

        private void OnHit(MonsterTakeDamageEvent e)
        {
            float intensity = e.SkillId > 0
                ? _profile.SkillShakeIntensity
                : _profile.NormalShakeIntensity;
            if (e.IsCritical) intensity *= _profile.CritShakeMultiplier;

            _currentIntensity = Mathf.Max(_currentIntensity, intensity);
            _shakeEndTime = Time.unscaledTime + _profile.ShakeDuration;
        }

        private void LateUpdate()
        {
            if (_camera == null) return;

            if (Time.unscaledTime >= _shakeEndTime)
            {
                if (_currentIntensity > 0f)
                {
                    _camera.localPosition = _originalLocalPos;
                    _currentIntensity = 0f;
                }
                return;
            }

            float t = (_shakeEndTime - Time.unscaledTime) / _profile.ShakeDuration;
            float shake = _currentIntensity * t;

            float x = (Mathf.PerlinNoise(Time.unscaledTime * 25f, 0f) - 0.5f) * 2f * shake * 0.05f;
            float y = (Mathf.PerlinNoise(0f, Time.unscaledTime * 25f) - 0.5f) * 2f * shake * 0.05f;

            _camera.localPosition = _originalLocalPos + new Vector3(x, y, 0f);
        }
    }
}
```

- [ ] **Step 2: Add to scene in Unity**

In Unity Editor:
1. On the same `HitFeedbackManager` GameObject from Task 4
2. Add `CameraShakeManager` component
3. Assign `HitFeedbackProfile` asset to `_profile`
4. Assign Main Camera's Transform to `_camera` (or leave empty — it auto-finds Camera.main)
5. Verify no errors

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/CameraShakeManager.cs
git commit -m "feat(vfx): add CameraShakeManager with Perlin noise shake"
```

---

### Task 6: Update SkillExecutor Event Emission

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

- [ ] **Step 1: Pass SkillId and frameIndex to MonsterTakeDamageEvent**

Open `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`. In the `OnHitboxTriggered` method (line 225-231), change the `MonsterTakeDamageEvent` emission from:

```csharp
                    EventBus.Emit(new MonsterTakeDamageEvent(
                        t.transform.GetInstanceID(),
                        hitPos + Vector3.up * 2f,
                        _owner.transform.forward,
                        Mathf.CeilToInt(Mathf.Abs(_lastDamageBlock?.BaseDamage ?? 0f)),
                        wasCrit
                    ));
```

To:

```csharp
                    EventBus.Emit(new MonsterTakeDamageEvent(
                        t.transform.GetInstanceID(),
                        hitPos + Vector3.up * 2f,
                        _owner.transform.forward,
                        Mathf.CeilToInt(Mathf.Abs(_lastDamageBlock?.BaseDamage ?? 0f)),
                        wasCrit,
                        _skillData.SkillId,
                        frameIndex + 1
                    ));
```

`frameIndex` is 0-based from `SkillStateMachine.OnHitboxFrame`, so `+1` makes it 1-based combo index.

- [ ] **Step 2: Verify compilation**

Wait for Unity recompile. Check Console.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs
git commit -m "feat(skills): pass SkillId and ComboIndex in MonsterTakeDamageEvent"
```

---

### Task 7: Update MonsterEntity Event Emission

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: Pass SkillId in MonsterEntity's MonsterTakeDamageEvent**

Open `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`. In the `IDamageable.TakeDamage` method (line 152-158), the event is emitted without SkillId/ComboIndex. Since this path is for direct damage (not through SkillExecutor), it uses the defaults (SkillId=0, ComboIndex=1) which is correct — no code change needed here.

However, verify the call compiles. The new params have defaults so it should compile as-is.

- [ ] **Step 2: Verify compilation**

Wait for Unity recompile. Confirm no errors from MonsterEntity.cs.

- [ ] **Step 3: Commit (only if changes were needed)**

No commit needed — existing code compiles with default params.

---

### Task 8: Extend HitParticleController

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs`

- [ ] **Step 1: Add new serialized fields and SpawnAtHit method**

Open `Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs`. Add new fields after `_slashBloodTrailPrefab` (line 12):

```csharp
        [SerializeField] private GameObject _hitShockwavePrefab;
        [SerializeField] private GameObject _hitSparkBurstPrefab;
        [SerializeField] private HitFeedbackProfile _profile;
```

Add a new private method at the end of the class:

```csharp
        private void SpawnAtHit(GameObject prefab, Vector3 pos, Vector3 dir)
        {
            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (dir != Vector3.zero)
                go.transform.forward = dir;
            Destroy(go, 1f);
        }
```

- [ ] **Step 2: Replace OnMonsterDamaged with expanded version**

Replace the existing `OnMonsterDamaged` method with:

```csharp
        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            // 1. Main blood particles
            var prefab = e.IsCritical && _criticalHitParticles != null
                ? _criticalHitParticles : _normalHitParticles;

            if (prefab != null)
            {
                var pool = GetOrCreatePool(prefab, e.IsCritical);
                var ps = pool.Get();
                ps.transform.position = e.HitPosition;
                if (e.HitDirection != Vector3.zero)
                    ps.transform.forward = e.HitDirection;

                float scale = _profile != null
                    ? (e.IsCritical ? _profile.CritParticleScale
                        : e.SkillId > 0 ? _profile.SkillParticleScale
                        : _profile.NormalParticleScale)
                    : 1f;
                ps.transform.localScale = Vector3.one * scale;

                var pooled = ps.GetComponent<PooledParticle>();
                if (pooled != null) pooled.SetPool(pool);
                ps.Play();
            }
            else if (!_warnedMissingPrefab)
            {
                Debug.LogWarning("[HitParticleController] No particle prefab assigned on " + name, this);
                _warnedMissingPrefab = true;
            }

            // 2. Spark burst (all hits)
            if (_hitSparkBurstPrefab != null)
                SpawnAtHit(_hitSparkBurstPrefab, e.HitPosition, e.HitDirection);

            // 3. Shockwave (skill or crit)
            if ((e.IsCritical || e.SkillId > 0) && _hitShockwavePrefab != null)
                SpawnAtHit(_hitShockwavePrefab, e.HitPosition, e.HitDirection);

            // 4. Slash trail (crit only)
            if (e.IsCritical && _slashBloodTrailPrefab != null)
            {
                var trailPool = GetOrCreateTrailPool(_slashBloodTrailPrefab);
                var trail = trailPool.Get();
                trail.SetPool(trailPool);
                trail.Activate(e.HitPosition, e.HitDirection);
            }
            else if (e.IsCritical && !_warnedMissingTrail)
            {
                Debug.LogWarning("[HitParticleController] No slash blood trail prefab assigned", this);
                _warnedMissingTrail = true;
            }
        }
```

- [ ] **Step 3: Verify compilation**

Wait for Unity recompile. Check Console.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs
git commit -m "feat(vfx): extend HitParticleController with shockwave, spark, and intensity scaling"
```

---

### Task 9: Create HitShockwave Particle Prefab

**Files:**
- Create: `Assets/Prefabs/VFX/HitShockwave.prefab`

- [ ] **Step 1: Create the prefab in Unity Editor**

1. Create empty GameObject in scene, name it `HitShockwave`
2. Add `ParticleSystem` component
3. Configure Main module:
   - Duration: 0.3
   - Start Lifetime: 0.3
   - Start Speed: 0
   - Start Size: 0.1 → (use curve: 0→2 over 0.2s, then hold)
   - Start Color: `#B8860B` (dark gold), alpha 180
   - Simulation Space: World
   - Max Particles: 1
4. Configure Emission module:
   - Rate over Time: 0
   - Bursts: 1 burst, Count=1, Time=0
5. Configure Shape module:
   - Shape: Donut (or Circle)
   - Radius: 0.1
6. Configure Size over Lifetime:
   - Curve: linear 0→2 over first 0.2s, then 2→2 for remaining 0.1s
7. Configure Color over Lifetime:
   - Gradient: alpha 180→0 over 0.3s (fade out)
8. Configure Renderer:
   - Material: Use existing `Mobile/Particles` shader, dark gold color
9. Add `PooledParticle` component (if using pool) or leave as Instantiate/Destroy

Right-click the GameObject → Prefab → Create Prefab. Save to `Assets/Prefabs/VFX/HitShockwave.prefab`.

- [ ] **Step 2: Delete the scene instance**

Delete the `HitShockwave` GameObject from the scene (prefab is saved).

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/VFX/HitShockwave.prefab Assets/Prefabs/VFX/HitShockwave.prefab.meta
git commit -m "feat(vfx): create HitShockwave particle prefab (dark gold ring)"
```

---

### Task 10: Create HitSparkBurst Particle Prefab

**Files:**
- Create: `Assets/Prefabs/VFX/HitSparkBurst.prefab`

- [ ] **Step 1: Create the prefab in Unity Editor**

1. Create empty GameObject in scene, name it `HitSparkBurst`
2. Add `ParticleSystem` component
3. Configure Main module:
   - Duration: 0.2
   - Start Lifetime: 0.15 → 0.2 (random between two constants)
   - Start Speed: 4 → 6 (random between two constants)
   - Start Size: 0.02 → 0.04 (random between two constants)
   - Start Color: `#B8860B` (dark gold)
   - Simulation Space: World
   - Gravity Modifier: 0
   - Max Particles: 8
4. Configure Emission module:
   - Rate over Time: 0
   - Bursts: 1 burst, Count=6, Time=0
5. Configure Shape module:
   - Shape: Sphere
   - Radius: 0.05
6. Configure Color over Lifetime:
   - Gradient: alpha 255→0 over lifetime (fast fade)
7. Configure Renderer:
   - Material: `Mobile/Particles` shader, dark gold

Create prefab at `Assets/Prefabs/VFX/HitSparkBurst.prefab`.

- [ ] **Step 2: Delete the scene instance**

Delete the `HitSparkBurst` GameObject from the scene.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/VFX/HitSparkBurst.prefab Assets/Prefabs/VFX/HitSparkBurst.prefab.meta
git commit -m "feat(vfx): create HitSparkBurst particle prefab (dark gold sparks)"
```

---

### Task 11: Update Existing Blood Splatter Prefabs

**Files:**
- Modify: `Assets/Prefabs/VFX/BloodSplatterNormal.prefab`
- Modify: `Assets/Prefabs/VFX/BloodSplatterCritical.prefab`

- [ ] **Step 1: Update BloodSplatterNormal colors**

Open `Assets/Prefabs/VFX/BloodSplatterNormal.prefab` in prefab edit mode:
1. Select the ParticleSystem
2. Main module → Start Color: change to `#8B0000` (deep blood red)
3. Main module → Start Size: 0.08 → 0.15 (random)
4. Main module → Start Speed: 3 → 6 (random)
5. Main module → Gravity Modifier: 0.8
6. Main module → Start Lifetime: 0.3 → 0.5 (random)
7. Color over Lifetime → alpha gradient: 255→0 over 0.3s
8. Save prefab

- [ ] **Step 2: Update BloodSplatterCritical colors and add spark layer**

Open `Assets/Prefabs/VFX/BloodSplatterCritical.prefab` in prefab edit mode:
1. Update the existing particle system colors same as Normal (`#8B0000`)
2. Increase Start Size by ×1.5 (so 0.12 → 0.225)
3. If a second sub-particle system exists, configure it:
   - Color: `#B8860B` (dark gold)
   - Count: 10, Speed: 5-8
4. If no sub-system exists, add a child GameObject with its own ParticleSystem for the spark layer
5. Save prefab

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/VFX/BloodSplatterNormal.prefab Assets/Prefabs/VFX/BloodSplatterCritical.prefab
git add Assets/Prefabs/VFX/BloodSplatterNormal.prefab.meta Assets/Prefabs/VFX/BloodSplatterCritical.prefab.meta
git commit -m "feat(vfx): update blood splatter prefabs to dark energy style"
```

---

### Task 12: Wire Up New Prefabs in Scene

**Files:**
- Scene: (current active scene)

- [ ] **Step 1: Assign prefabs to HitParticleController**

In Unity Editor:
1. Find the monster's `HitParticleController` component in the scene (or prefab)
2. Drag `HitShockwave` prefab to `_hitShockwavePrefab` field
3. Drag `HitSparkBurst` prefab to `_hitSparkBurstPrefab` field
4. Drag `HitFeedbackProfile` asset to `_profile` field

- [ ] **Step 2: Verify all component references**

On the `HitFeedbackManager` GameObject:
- `HitStopManager`: verify `_profile` and `_playerAnimator` assigned
- `CameraShakeManager`: verify `_profile` assigned, `_camera` either assigned or will auto-find

- [ ] **Step 3: Save scene**

Save the scene in Unity Editor.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(vfx): wire up hit feedback components and prefabs in scene"
```

---

### Task 13: Integration Test — Hit Feedback

**Files:**
- None (manual testing in Unity Editor)

- [ ] **Step 1: Test basic hitstop**

Enter Play mode. Attack a monster with a normal attack (Attack1).
- Expected: Brief pause (~30ms) on both player and monster Animators
- Verify: Both characters freeze briefly then resume

- [ ] **Step 2: Test camera shake**

Attack a monster.
- Expected: Camera shakes briefly (Perlin noise, smooth)
- Verify: Camera returns to original position after shake

- [ ] **Step 3: Test crit hitstop + time slow**

Land a critical hit (may need to adjust crit chance for testing).
- Expected: Longer hitstop (~70ms), followed by 0.3s slow-motion
- Verify: World slows down briefly, then returns to normal speed

- [ ] **Step 4: Test skill hit feedback**

Use SkillQ or SkillR on a monster.
- Expected: Longer hitstop (~80ms), stronger camera shake, larger particles
- Verify: All effects are visibly stronger than normal attack

- [ ] **Step 5: Test particle effects**

Attack a monster and observe particles.
- Expected: Blood particles + spark burst on all hits
- Expected: Shockwave ring on skill/crit hits
- Verify: Particles spawn at correct position, face correct direction

- [ ] **Step 6: Test rapid combo hits**

Perform a full combo (Attack1 → Attack2 → Attack3).
- Expected: Each hit triggers effects, combo index increases hitstop
- Verify: No stacking bugs, no frozen Animators, no stale time scale

- [ ] **Step 7: Test edge cases**

- Hit a dead monster → verify no crash
- Get hit while attacking → verify player hitstop doesn't conflict
- Multiple monsters hit by AOE → verify each freezes independently

- [ ] **Step 8: Commit test results (if any fixes needed)**

If any bugs were found and fixed during testing:
```bash
git add -A
git commit -m "fix(vfx): resolve hit feedback integration issues"
```

---

### Task 14: Tune Default Values

**Files:**
- Modify: `Assets/Data/HitFeedbackProfile.asset` (via Inspector)

- [ ] **Step 1: Play-test and tune**

Enter Play mode and attack monsters repeatedly. Adjust `HitFeedbackProfile` values in Inspector:
- If hitstop feels too long → reduce `NormalHitStop`, `SkillHitStop`
- If shake is too intense → reduce `NormalShakeIntensity`, `SkillShakeIntensity`
- If time slow is jarring → reduce `CritTimeSlowDuration`
- If particles are too big/small → adjust `*ParticleScale` values

- [ ] **Step 2: Save the SO asset**

Select `HitFeedbackProfile` asset → Ctrl+S to save.

- [ ] **Step 3: Commit**

```bash
git add Assets/Data/HitFeedbackProfile.asset
git commit -m "tune(vfx): adjust hit feedback default values after play-testing"
```
