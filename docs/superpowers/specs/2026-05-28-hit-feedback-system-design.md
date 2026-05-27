# Hit Feedback System Redesign — 怪猎风格打击感

## Overview

当前命中反馈系统各组件独立运作，缺乏统筹。视觉粒子效果质量差，缺少 hitstop 和 camera shake。本设计重新梳理命中反馈的完整链路，以怪猎/魂系风格为目标：精确、克制、有重量感。

## Current State

| 组件 | 文件 | 状态 |
|------|------|------|
| HitFlashVFX | `VFX/HitFlashVFX.cs` | Working，outline 闪白 |
| HitParticleController | `VFX/HitParticleController.cs` | Working，但粒子质量差 |
| FloatTextRenderer | `Nameplate/FloatTextRenderer.cs` | Working，伤害浮字 |
| DamageScreenEffect | `Nameplate/DamageScreenEffect.cs` | Working，仅玩家受伤时触发 |
| PresentationBlock.HitStopDuration | `Skills/Data/PresentationBlock.cs` | **字段存在但从未使用** |
| Camera Shake | — | **完全缺失** |
| HitStop | — | **完全缺失** |
| Time Slow | — | **完全缺失** |

## Design Goals

1. **HitStop** — 命中瞬间冻结攻击者+受击者 Animator，怪猎风格单段停顿
2. **Camera Shake** — 命中时镜头震动，强度随攻击类型递增
3. **Time Slow** — 暴击/满蓄力时短暂全局慢动作
4. **粒子特效升级** — 暗黑能量风格，深红+暗金配色，冲击波+火花分层
5. **自动分层** — 普攻/技能/暴击自动差异化反馈强度
6. **最小侵入** — 不改 SkillExecutor 核心逻辑，只新增事件字段

## Architecture

```
SkillExecutor (minimal change: fill SkillId + ComboIndex)
    │
    ▼  EventBus.Emit(MonsterTakeDamageEvent)
    │
    ├── HitFlashVFX          (existing, unchanged)
    ├── HitParticleController (existing, extend)
    ├── FloatTextRenderer     (existing, unchanged)
    ├── HitStopManager        (NEW)
    └── CameraShakeManager    (NEW)

HitFeedbackProfile (ScriptableObject) ──── tuning parameters
```

**Principle:** All feedback components subscribe to `MonsterTakeDamageEvent` independently. No central orchestrator. Each component reads its parameters from the shared `HitFeedbackProfile` SO.

## Event Data Extension

### MonsterTakeDamageEvent — Add 2 Fields

```csharp
public struct MonsterTakeDamageEvent : IEvent
{
    // existing fields...
    public int EntityId;
    public Vector3 HitPosition;
    public Vector3 HitDirection;
    public int Damage;
    public bool IsCritical;

    // NEW
    public int SkillId;       // 0 = normal attack, >0 = skill ID
    public int ComboIndex;    // combo hit number (1, 2, 3...)

    public MonsterTakeDamageEvent(
        int entityId, Vector3 hitPos, Vector3 hitDir,
        int damage, bool isCritical = false,
        int skillId = 0, int comboIndex = 1)  // new params with defaults
    {
        // ... existing assignments ...
        SkillId = skillId;
        ComboIndex = comboIndex;
    }
}
```

### SkillExecutor Change — 2 Lines

In `OnHitboxTriggered`, when emitting `MonsterTakeDamageEvent`, add:
```csharp
_skillData.SkillId,    // new arg
_comboIndex             // new arg
```

`_comboIndex` is already tracked in `SkillStateMachine` via hit frame sequencing. The executor reads it from the current hit frame index. For `ComboSkillData`, it maps to the combo step number (1-based).

## HitFeedbackProfile (ScriptableObject)

**Path:** `Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackProfile.cs`
**Asset:** `Assets/Data/HitFeedbackProfile.asset`

```csharp
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
```

## HitStopManager (NEW)

**Path:** `Assets/Scripts/Hotfix/GameSystems/VFX/HitStopManager.cs`

**职责：** 命中时冻结攻击者+受击者的 Animator，暴击时短暂 Time.timeScale 慢动作。

```csharp
public class HitStopManager : MonoBehaviour
{
    [SerializeField] private HitFeedbackProfile _profile;
    [SerializeField] private Animator _playerAnimator;  // assign in inspector

    private bool _playerFrozen;
    private bool _timeSlowing;

    private void OnEnable()  => EventBus.Subscribe<MonsterTakeDamageEvent>(OnHit);
    private void OnDisable() => EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnHit);

    private void OnHit(MonsterTakeDamageEvent e)
    {
        // Calculate hitstop duration
        float duration = e.SkillId > 0 ? _profile.SkillHitStop : _profile.NormalHitStop;
        if (e.IsCritical) duration += _profile.CritHitStopBonus;
        duration += (e.ComboIndex - 1) * _profile.ComboHitStopBonus;
        duration = Mathf.Min(duration, _profile.MaxHitStop);

        // Freeze player animator (replace, don't stack)
        if (_playerAnimator != null && !_playerFrozen)
            StartCoroutine(FreezePlayerAnimator(duration));

        // Freeze target animator
        var targetAnim = FindAnimatorById(e.EntityId);
        if (targetAnim != null)
            StartCoroutine(FreezeAnimator(targetAnim, duration));

        // Crit time slow (only if not already slowing)
        if (e.IsCritical && !_timeSlowing)
            StartCoroutine(TimeSlowRoutine(
                _profile.CritTimeSlowScale,
                _profile.CritTimeSlowDuration));
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
        // Use PhysicsRegistry to find entity by ID (avoids FindObjectsOfType)
        var entity = PhysicsRegistry.Instance.GetEntity(entityId);
        if (entity is MonsterEntity monster)
            return monster.Animator;
        return null;
    }
}
```

**Design decisions:**
- Uses `WaitForSecondsRealtime` to work correctly when Time.timeScale is modified
- Animator freeze = `speed = 0`, restore = `speed = 1`
- Time slow only on crits, not on every hit
- Uses `PhysicsRegistry.Instance.GetEntity()` to find monster by ID (no FindObjectsOfType)

**Hit stacking during hitstop:**
- If a new hit arrives while hitstop is active, the new hit's duration replaces the current one (not additive)
- `_playerFrozen` flag prevents multiple coroutines fighting over the same player Animator
- Each monster has its own coroutine (independent freeze per entity)
- Camera shake uses `Mathf.Max` to keep the strongest intensity during rapid hits
- `_timeSlowing` flag prevents overlapping slow-motion effects

## CameraShakeManager (NEW)

**Path:** `Assets/Scripts/Hotfix/GameSystems/VFX/CameraShakeManager.cs`

**职责：** 命中时震动摄像机。使用 Perlin noise 实现平滑震动。

```csharp
public class CameraShakeManager : MonoBehaviour
{
    [SerializeField] private HitFeedbackProfile _profile;
    [SerializeField] private Transform _camera;  // main camera transform

    private Vector3 _originalLocalPos;
    private float _shakeEndTime;
    private float _currentIntensity;

    private void Start()
    {
        if (_camera == null) _camera = Camera.main.transform;
        _originalLocalPos = _camera.localPosition;
    }

    private void OnEnable()  => EventBus.Subscribe<MonsterTakeDamageEvent>(OnHit);
    private void OnDisable() => EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnHit);

    private void OnHit(MonsterTakeDamageEvent e)
    {
        float intensity = e.SkillId > 0
            ? _profile.SkillShakeIntensity
            : _profile.NormalShakeIntensity;
        if (e.IsCritical) intensity *= _profile.CritShakeMultiplier;

        // Accumulate intensity for rapid hits
        _currentIntensity = Mathf.Max(_currentIntensity, intensity);
        _shakeEndTime = Time.unscaledTime + _profile.ShakeDuration;
    }

    private void LateUpdate()
    {
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
```

**Design decisions:**
- Perlin noise for smooth, non-jittery shake
- Uses `unscaledTime` so shake works during hitstop/time slow
- Accumulates intensity for rapid successive hits (takes max)
- Shake in LateUpdate to avoid fighting other camera code

## HitParticleController — Extend

**Changes to existing file:**

1. Add references to new particle prefabs
2. Add intensity scaling based on attack type
3. Add shockwave and spark burst spawning
4. Fix hit position (use `e.HitPosition` directly, not `+ Vector3.up * 2f`)

```csharp
public class HitParticleController : MonoBehaviour
{
    // existing fields...
    [SerializeField] private GameObject _hitShockwavePrefab;   // NEW
    [SerializeField] private GameObject _hitSparkBurstPrefab;  // NEW
    [SerializeField] private HitFeedbackProfile _profile;      // NEW

    private void OnMonsterDamaged(MonsterTakeDamageEvent e)
    {
        // 1. Main blood particles
        var prefab = e.IsCritical ? _criticalHitParticles : _normalHitParticles;
        if (prefab != null)
        {
            var pool = GetOrCreatePool(prefab, e.IsCritical);
            var ps = pool.Get();
            ps.transform.position = e.HitPosition;  // FIX: use HitPosition directly
            if (e.HitDirection != Vector3.zero)
                ps.transform.forward = e.HitDirection;

            // Scale by attack type
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
    }

    private void SpawnAtHit(GameObject prefab, Vector3 pos, Vector3 dir)
    {
        var go = Instantiate(prefab, pos, Quaternion.identity);
        if (dir != Vector3.zero)
            go.transform.forward = dir;
        Destroy(go, 1f);  // auto cleanup
    }
}
```

## Particle Prefab Specifications

### Visual Style: Dark Energy

**Color Palette:**

| Color | Hex | Use |
|-------|-----|-----|
| Deep Blood Red | `#8B0000` | Main blood color |
| Dark Gold | `#B8860B` | Energy/spark highlights |
| Dark Red-Brown | `#3D0000` | Blood shadow/ground residue |
| Platinum White | `#E8D8C0` | Hit flash accent |

### BloodSplatterNormal (Modify Existing)

- Emitter: Cone shape, 30 degree angle
- Particles: 15-20, deep blood red (`#8B0000`)
- Initial speed: 3-6 m/s
- Gravity modifier: 0.8
- Size: 0.08-0.15 (random)
- Lifetime: 0.3-0.5s
- Alpha: fast fade out (1→0 over 0.3s)
- Material: Mobile/Particles shader, no emission

### BloodSplatterCritical (Modify Existing)

- Same as Normal, plus:
- Dark gold spark layer: 10 particles, speed 5-8 m/s
- Hit flash: single large particle, platinum white, 0.05s
- Particle size ×1.5

### HitShockwave (NEW Prefab)

- Flat ring (Quad mesh or particle ring)
- Color: Dark gold (`#B8860B`), alpha 0.6→0
- Expands from 0→2m radius over 0.2s
- Edge fade (soft edges)
- Only triggers on skill/crit hits
- Lifetime: 0.3s

### HitSparkBurst (NEW Prefab)

- 5-8 tiny spark particles (size 0.02-0.04)
- Color: Dark gold (`#B8860B`)
- Random outward direction, speed 4-6 m/s
- No gravity
- Lifetime: 0.15-0.2s
- Fast fade out

## File Changes Summary

| File | Type | Description |
|------|------|-------------|
| `Sys3C/Core/Events/DamageEvents.cs` | Modify | Add `SkillId`, `ComboIndex` to `MonsterTakeDamageEvent` |
| `Skills/Runtime/SkillExecutor.cs` | Modify | Fill new event fields (2 lines) |
| `VFX/HitFeedbackProfile.cs` | **NEW** | ScriptableObject for all tuning params |
| `VFX/HitStopManager.cs` | **NEW** | Animator freeze + crit time slow |
| `VFX/CameraShakeManager.cs` | **NEW** | Camera shake via Perlin noise |
| `VFX/HitParticleController.cs` | Modify | Add shockwave/spark, intensity scaling, fix position |
| `Assets/Data/HitFeedbackProfile.asset` | **NEW** | SO instance with default values |
| `Assets/Prefabs/VFX/HitShockwave.prefab` | **NEW** | Shockwave particle prefab |
| `Assets/Prefabs/VFX/HitSparkBurst.prefab` | **NEW** | Spark burst particle prefab |
| `Assets/Prefabs/VFX/BloodSplatterNormal.prefab` | Modify | Update to dark energy style |
| `Assets/Prefabs/VFX/BloodSplatterCritical.prefab` | Modify | Update to dark energy style |

## Dependencies

- DOTween (already present, used by HitStopManager for potential tween-based approach)
- EventBus (existing)
- GameSettings (existing, for any shared settings)
- No new package dependencies

## Testing Plan

1. **HitStop:** Attack a monster → verify Animator freezes for correct duration
2. **Camera Shake:** Attack → verify camera shakes with correct intensity
3. **Crit Time Slow:** Land a crit → verify slow-motion effect
4. **Particle Scaling:** Attack with different skill types → verify particle size varies
5. **Shockwave:** Skill hit → verify shockwave ring appears
6. **Spark Burst:** Any hit → verify sparks appear
7. **Rapid Hits:** Combo attack → verify effects stack correctly
8. **Edge Cases:** Hit during hitstop, hit a dead monster, hit with no skillId
