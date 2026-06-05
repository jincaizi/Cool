# Blood Slash VFX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace spark-style hit particles with blood splatter and add a TrailRenderer-based slash blood trail on critical hits.

**Architecture:** Extend existing `HitParticleController` with a new `_slashBloodTrailPrefab` slot. Create `SlashBloodTrail` script for TrailRenderer lifecycle management with coroutine-based movement and auto-return to pool. Blood splatter is handled by replacing the existing particle prefabs (no code change needed for splatter itself).

**Tech Stack:** Unity 2022 LTS, C#, DOTween (existing), ComponentPool<T> (existing), TrailRenderer, ParticleSystem

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/SlashBloodTrail.cs` | TrailRenderer lifecycle: activate, move along direction, fade, return to pool |
| Modify | `Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs:8-67` | Add slash trail prefab slot, pool, and critical-hit trigger |
| Create | `Assets/Prefabs/VFX/SlashBloodTrail.prefab` | Blood trail prefab (created via Unity Editor or MCP) |
| Create | `Assets/Prefabs/VFX/BloodSplatterNormal.prefab` | Normal hit blood splatter particle (created via Unity Editor or MCP) |
| Create | `Assets/Prefabs/VFX/BloodSplatterCritical.prefab` | Critical hit blood splatter particle (created via Unity Editor or MCP) |

---

### Task 1: Create SlashBloodTrail Script

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/SlashBloodTrail.cs`

- [ ] **Step 1: Create the SlashBloodTrail script**

```csharp
using System.Collections;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class SlashBloodTrail : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 3f;
        [SerializeField] private float _moveDistance = 0.5f;
        [SerializeField] private float _fadeDelay = 0.3f;

        private TrailRenderer _trail;
        private ComponentPool<SlashBloodTrail> _pool;
        private Coroutine _activeRoutine;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
        }

        public void SetPool(ComponentPool<SlashBloodTrail> pool)
        {
            _pool = pool;
        }

        public void Activate(Vector3 startPos, Vector3 direction)
        {
            if (_activeRoutine != null)
                StopCoroutine(_activeRoutine);

            transform.position = startPos;
            _trail.Clear();
            _trail.emitting = true;

            _activeRoutine = StartCoroutine(SlashRoutine(direction.normalized));
        }

        private IEnumerator SlashRoutine(Vector3 direction)
        {
            float traveled = 0f;
            while (traveled < _moveDistance)
            {
                float step = _moveSpeed * Time.deltaTime;
                if (traveled + step > _moveDistance)
                    step = _moveDistance - traveled;
                transform.position += direction * step;
                traveled += step;
                yield return null;
            }

            _trail.emitting = false;
            yield return new WaitForSeconds(_fadeDelay);

            _activeRoutine = null;
            _pool?.Return(this);
        }
    }
}
```

- [ ] **Step 2: Verify script compiles**

In Unity Editor, check the Console for compilation errors. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/SlashBloodTrail.cs
git commit -m "feat(vfx): add SlashBloodTrail script for critical hit blood trail"
```

---

### Task 2: Extend HitParticleController with Slash Trail Support

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs`

- [ ] **Step 1: Add slash trail fields and pool**

Add after line 11 (`private GameObject _criticalHitParticles;`):

```csharp
[SerializeField] private GameObject _slashBloodTrailPrefab;
```

Add after line 15 (`private bool _warnedMissingPrefab;`):

```csharp
private static ComponentPool<SlashBloodTrail> _trailPool;
private bool _warnedMissingTrail;
```

- [ ] **Step 2: Add trail pool creation method**

Add after the `GetOrCreatePool` method (after line 65):

```csharp
private ComponentPool<SlashBloodTrail> GetOrCreateTrailPool(GameObject prefab)
{
    if (_trailPool == null)
        _trailPool = new ComponentPool<SlashBloodTrail>(
            prefab.GetComponent<SlashBloodTrail>(), null);
    return _trailPool;
}
```

- [ ] **Step 3: Add slash trail trigger in OnMonsterDamaged**

Add at the end of `OnMonsterDamaged`, before the closing brace (after line 49 `ps.Play();`):

```csharp
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
```

- [ ] **Step 4: Verify script compiles**

In Unity Editor, check the Console for compilation errors. Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs
git commit -m "feat(vfx): extend HitParticleController with slash blood trail on critical hits"
```

---

### Task 3: Create Blood Splatter Particle Prefabs

**Files:**
- Create: `Assets/Prefabs/VFX/BloodSplatterNormal.prefab`
- Create: `Assets/Prefabs/VFX/BloodSplatterCritical.prefab`

These prefabs need to be created in the Unity Editor with the following configurations. If using the AI Game Developer MCP tools, create them programmatically. Otherwise, create manually in the Editor.

**BloodSplatterNormal.prefab setup:**
- ParticleSystem with:
  - Start Lifetime: 0.3–0.5
  - Start Speed: 2–4
  - Start Size: 0.03–0.08
  - Start Color: dark red (0.6, 0.05, 0.05, 1)
  - Emission: burst of 8–12 particles
  - Shape: Sphere, radius 0.05
  - Gravity Modifier: 0.8
  - Renderer: default particle material (or Particles/Standard Unlit alpha blend)
- Add `PooledParticle` component (existing, for auto-return to pool)

**BloodSplatterCritical.prefab setup:**
- Same as normal but:
  - Start Speed: 3–6
  - Start Size: 0.04–0.1
  - Emission: burst of 15–25 particles
  - Start Color: brighter red (0.8, 0.08, 0.08, 1)
- Add `PooledParticle` component

- [ ] **Step 1: Create BloodSplatterNormal prefab**

Use Unity Editor or MCP tools to create the prefab at `Assets/Prefabs/VFX/BloodSplatterNormal.prefab` with the ParticleSystem configuration above. Ensure `PooledParticle` component is attached and `Main.stopAction` is set to `Callback`.

- [ ] **Step 2: Create BloodSplatterCritical prefab**

Use Unity Editor or MCP tools to create the prefab at `Assets/Prefabs/VFX/BloodSplatterCritical.prefab` with the ParticleSystem configuration above. Ensure `PooledParticle` component is attached and `Main.stopAction` is set to `Callback`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/VFX/BloodSplatterNormal.prefab Assets/Prefabs/VFX/BloodSplatterCritical.prefab
git commit -m "feat(vfx): add blood splatter particle prefabs for normal and critical hits"
```

---

### Task 4: Create Slash Blood Trail Prefab

**Files:**
- Create: `Assets/Prefabs/VFX/SlashBloodTrail.prefab`

**SlashBloodTrail.prefab setup:**
- Empty GameObject with:
  - TrailRenderer:
    - Time: 0.4
    - Min Vertex Distance: 0.01
    - Start Width: 0.08
    - End Width: 0
    - Color Gradient: opaque red (0.7, 0.05, 0.05) → transparent red
    - Material: Particles/Standard Unlit (alpha blend)
    - Shadow Casting: Off
    - Receive Shadows: false
  - `SlashBloodTrail` component (from Task 1)

- [ ] **Step 1: Create SlashBloodTrail prefab**

Use Unity Editor or MCP tools to create the prefab at `Assets/Prefabs/VFX/SlashBloodTrail.prefab` with the TrailRenderer and SlashBloodTrail component configuration above.

- [ ] **Step 2: Commit**

```bash
git add Assets/Prefabs/VFX/SlashBloodTrail.prefab
git commit -m "feat(vfx): add slash blood trail prefab with TrailRenderer"
```

---

### Task 5: Wire Up Prefabs in Scene and Test

**Files:**
- Modify: Scene object with `HitParticleController` component (assign new prefabs)

- [ ] **Step 1: Assign prefabs to HitParticleController**

In Unity Editor, find the GameObject with `HitParticleController` (likely on the player or a VFX manager). Assign:
- `_normalHitParticles` → `BloodSplatterNormal.prefab`
- `_criticalHitParticles` → `BloodSplatterCritical.prefab`
- `_slashBloodTrailPrefab` → `SlashBloodTrail.prefab`

- [ ] **Step 2: Test normal hit**

Enter Play Mode, attack a monster with a normal hit. Verify:
- Blood splatter particles appear at the hit position
- No slash trail appears
- Particles auto-despawn after lifetime

- [ ] **Step 3: Test critical hit**

Trigger a critical hit on a monster. Verify:
- Larger blood splatter appears
- Slash blood trail draws along the hit direction
- Trail fades after ~0.7s total (0.4s move + 0.3s fade)
- All objects return to pool (no leaks)

- [ ] **Step 4: Commit final state**

```bash
git add -A
git commit -m "feat(vfx): complete blood slash VFX — splatter on all hits, trail on crit"
```

---

## Tuning Reference

After initial test, adjust these values in the Inspector:

| Component | Field | Default | Notes |
|-----------|-------|---------|-------|
| `SlashBloodTrail` | `_moveSpeed` | 3.0 | Faster = longer trail per frame |
| `SlashBloodTrail` | `_moveDistance` | 0.5 | Longer = bigger slash mark |
| `SlashBloodTrail` | `_fadeDelay` | 0.3 | Longer = trail persists longer |
| TrailRenderer | `time` | 0.4 | Trail lifetime (shorter = thinner trail) |
| TrailRenderer | `startWidth` | 0.08 | Thickness of the blood mark |
| ParticleSystem | `startSpeed` | 2-4 / 3-6 | How far blood sprays |
| ParticleSystem | `gravityModifier` | 0.8 | How fast droplets fall |
