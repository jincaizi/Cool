# SkillR Frost VFX System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add frost-themed VFX (aura, sword glow, slash trail, ice decal, ice burst) + freeze gameplay effect to SkillR via EventBus-driven layered architecture.

**Architecture:** Four new event types emitted from SkillExecutor at skill lifecycle points (start charge, charge tick, release, hit target). Six independent VFX components subscribe to relevant events, filtered by `_watchSkillIds`. Freeze gameplay effect via separate effector component.

**Tech Stack:** Unity 2022.3, EventBus (existing Sys3C.Core), DOTween, ParticleSystem, TrailRenderer, MaterialPropertyBlock

**Note:** No Unity Test Framework infrastructure exists. Verification is via compilation (`assets-refresh`) and play-mode observation.

---

## File Structure

| Action | File | Purpose |
|--------|------|---------|
| Create | `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/SkillVFXEvents.cs` | 4 new event structs |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/VFX.asmdef` | Assembly definition |
| Modify | `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` | Emit VFX events at lifecycle points |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/FrostAuraVFX.cs` | Charging frost particle aura |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs` | Sword emissive glow |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/SlashTrailVFX.cs` | Slash TrailRenderer |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/IceDecalVFX.cs` | Ground ice crack decal |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/IceBurstVFX.cs` | Hit impact ice burst |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/SkillFreezeEffector.cs` | Freeze status on full-charge hit |

---

### Task 1: Create SkillVFXEvents.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/SkillVFXEvents.cs`

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 技能开始蓄力
    /// </summary>
    public struct SkillChargingStartedEvent : IEvent
    {
        public int SkillId;
    }

    /// <summary>
    /// 蓄力进度更新（每帧）
    /// </summary>
    public struct SkillChargeTickEvent : IEvent
    {
        public int SkillId;
        public float Progress;   // 0.0 → 1.0
    }

    /// <summary>
    /// 技能释放（松手/蓄满）
    /// </summary>
    public struct SkillReleasedEvent : IEvent
    {
        public int SkillId;
        public bool IsFullCharge;
        public int CasterId;
    }

    /// <summary>
    /// 技能命中目标
    /// </summary>
    public struct SkillHitTargetEvent : IEvent
    {
        public int SkillId;
        public int CasterId;
        public Vector3 HitPosition;
        public bool IsFullCharge;
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error — no compilation errors expected.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/SkillVFXEvents.cs*
git commit -m "feat: add SkillVFXEvents (charge/chargeTick/release/hitTarget)"
```

---

### Task 2: Create VFX.asmdef

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/VFX.asmdef`

- [ ] **Step 1: Write the file**

```json
{
    "name": "Hotfix.GameSystems.VFX",
    "rootNamespace": "Hotfix.GameSystems.VFX",
    "references": [
        "Hotfix.GameSystems.Sys3C.Core",
        "Hotfix.GameSystems.Skills",
        "DOTween.Modules",
        "Unity.TextMeshPro"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/VFX.asmdef*
git commit -m "feat: add VFX assembly definition"
```

---

### Task 3: Emit VFX events from SkillExecutor

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

Add `using Hotfix.GameSystems.Sys3C.Core.Events;` and `using Hotfix.GameSystems.Sys3C.Core;` to SkillExecutor.cs. Add events at specific points.

- [ ] **Step 1: Add field for tracking full charge**

Add to the class fields (after `private IDashComponent _dashComponent;`):

```csharp
        private bool _wasFullCharge;
```

- [ ] **Step 2: Emit SkillChargingStarted in TryStart()**

In `TryStart()`, after `return _stateMachine.TryStart();` succeeds but the method only returns a bool. Modify to:

```csharp
        public bool TryStart()
        {
            bool started = _stateMachine.TryStart();
            if (started && _skillData is ChargedSkillData)
            {
                EventBus.Emit(new SkillChargingStartedEvent { SkillId = _skillData.SkillId });
            }
            return started;
        }
```

- [ ] **Step 3: Emit SkillChargeTick in Update()**

In `Update(float deltaTime)`, after `_stateMachine.Update(deltaTime);`, add:

```csharp
            if (CurrentSubState == SkillSubState.Charging)
            {
                EventBus.Emit(new SkillChargeTickEvent
                {
                    SkillId = _skillData.SkillId,
                    Progress = GetChargeProgress()
                });
            }
```

- [ ] **Step 4: Record WasFullCharge and emit SkillReleased in ReleaseCharge()**

Replace `ReleaseCharge()`:

```csharp
        public void ReleaseCharge()
        {
            if (CurrentSubState == SkillSubState.Charging)
            {
                _wasFullCharge = GetChargeProgress() >= 1f;
                _stateMachine.ReleaseCharge();
            }
        }
```

In `OnStateChanged`, when transitioning from Charging to Execution, emit SkillReleasedEvent. Replace the entire `OnStateChanged` method:

```csharp
        private void OnStateChanged(SkillSubState newState)
        {
            // Emit release event when charger exits charging
            if (newState == SkillSubState.Execution && _skillData is ChargedSkillData)
            {
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = _wasFullCharge,
                    CasterId = _owner.transform.GetInstanceID()
                });
            }

            // Existing dash logic
            if (newState == SkillSubState.Execution &&
                _dashComponent != null &&
                _skillData.DashDistance > 0)
            {
                Vector3 dashDir = _owner.transform.forward;
                _dashComponent.StartDash(dashDir, _skillData.DashDistance, _skillData.DashDuration);
            }
        }
```

- [ ] **Step 5: Emit SkillHitTarget in OnHitboxTriggered**

In `OnHitboxTriggered`, after the foreach loop that processes targets, add:

```csharp
            // Emit hit events for VFX system
            if (targets.Count > 0)
            {
                var hitPos = targets[0].Transform.position;
                foreach (var target in targets)
                {
                    EventBus.Emit(new SkillHitTargetEvent
                    {
                        SkillId = _skillData.SkillId,
                        CasterId = _owner.transform.GetInstanceID(),
                        HitPosition = hitPos,
                        IsFullCharge = _wasFullCharge
                    });
                }
            }
```

- [ ] **Step 6: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error. Must be zero errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs
git commit -m "feat: emit SkillVFX events from SkillExecutor lifecycle"
```

---

### Task 4: Create FrostAuraVFX.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/FrostAuraVFX.cs`

- [ ] **Step 1: Write the file**

```csharp
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 蓄力寒气聚集 — 角色周身冰霜粒子旋涡，强度随蓄力进度递增
    /// </summary>
    public class FrostAuraVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private ParticleSystem _auraParticles;
        [SerializeField] private float _maxEmissionRate = 50f;
        [SerializeField] private float _maxRadius = 1.5f;
        [SerializeField] private float _maxOrbitalSpeed = 3f;

        private bool _isActive;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.VelocityOverLifetimeModule _velocityOverLifetime;

        private void Awake()
        {
            if (_auraParticles == null)
            {
                _auraParticles = GetComponentInChildren<ParticleSystem>();
            }
            if (_auraParticles != null)
            {
                _emission = _auraParticles.emission;
                _main = _auraParticles.main;
                _velocityOverLifetime = _auraParticles.velocityOverLifetime;
                _auraParticles.Stop();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<SkillChargingStartedEvent>(OnChargingStarted);
            EventBus.Subscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillChargingStartedEvent>(OnChargingStarted);
            EventBus.Unsubscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnChargingStarted(SkillChargingStartedEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;
            _isActive = true;
            if (_auraParticles != null && !_auraParticles.isPlaying)
                _auraParticles.Play();
            UpdateIntensity(0f);
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            UpdateIntensity(e.Progress);
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            _isActive = false;
            if (_auraParticles != null)
                _auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void UpdateIntensity(float t)
        {
            if (_auraParticles == null) return;

            // Emission rate: 5 → _maxEmissionRate
            _emission.rateOverTime = Mathf.Lerp(5f, _maxEmissionRate, t);

            // Color: white → deep blue
            _main.startColor = Color.Lerp(
                new Color(1f, 1f, 1f, 0.5f),
                new Color(0.2f, 0.5f, 1f, 0.8f),
                t);

            // Shape radius: 0.8m → _maxRadius
            var shape = _auraParticles.shape;
            shape.radius = Mathf.Lerp(0.8f, _maxRadius, t);

            // Orbital velocity: 1x → _maxOrbitalSpeed
            _velocityOverLifetime.orbitalZ = Mathf.Lerp(1f, _maxOrbitalSpeed, t);
        }
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/FrostAuraVFX.cs*
git commit -m "feat: add FrostAuraVFX (charging frost particle vortex)"
```

---

### Task 5: Create SwordGlowVFX.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs`

- [ ] **Step 1: Write the file**

```csharp
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 剑身发光 — 蓄力时剑身冰蓝色自发光渐变
    /// </summary>
    public class SwordGlowVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private string _weaponBonePath = "weapon_r";
        [SerializeField] private Color _glowColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _maxGlowIntensity = 2f;

        private Renderer _weaponRenderer;
        private MaterialPropertyBlock _propBlock;
        private bool _isActive;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            FindWeaponRenderer();
        }

        private void FindWeaponRenderer()
        {
            var t = transform.Find(_weaponBonePath);
            if (t == null)
            {
                // Try recursive search
                var allRenderers = GetComponentsInChildren<Renderer>();
                foreach (var r in allRenderers)
                {
                    if (r.name.ToLower().Contains("weapon") || r.name.ToLower().Contains("sword"))
                    {
                        _weaponRenderer = r;
                        return;
                    }
                }
                // Fallback: first renderer on a child
                if (allRenderers.Length > 0)
                    _weaponRenderer = allRenderers[0];
            }
            else
            {
                _weaponRenderer = t.GetComponent<Renderer>();
                if (_weaponRenderer == null)
                    _weaponRenderer = t.GetComponentInChildren<Renderer>();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<SkillChargingStartedEvent>(OnChargingStarted);
            EventBus.Subscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillChargingStartedEvent>(OnChargingStarted);
            EventBus.Unsubscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnChargingStarted(SkillChargingStartedEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;
            _isActive = true;
            UpdateGlow(0f);
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            UpdateGlow(e.Progress);
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            _isActive = false;
            UpdateGlow(0f);
        }

        private void UpdateGlow(float t)
        {
            if (_weaponRenderer == null) return;

            _weaponRenderer.GetPropertyBlock(_propBlock);
            float intensity = Mathf.Lerp(0.3f, _maxGlowIntensity, t);
            _propBlock.SetColor(EmissionColorId, _glowColor * intensity);
            _weaponRenderer.SetPropertyBlock(_propBlock);
        }
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs*
git commit -m "feat: add SwordGlowVFX (emissive sword glow during charge)"
```

---

### Task 6: Create SlashTrailVFX.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/SlashTrailVFX.cs`

- [ ] **Step 1: Write the file**

```csharp
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 挥砍拖尾 — 剑刃挥砍路径上的冰蓝渐变拖尾
    /// </summary>
    public class SlashTrailVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private TrailRenderer _trailRenderer;
        [SerializeField] private string _weaponBonePath = "weapon_r";
        [SerializeField] private Color _trailColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _earlyActivateProgress = 0.8f;

        private bool _isActive;
        private bool _earlyActivated;

        private void Awake()
        {
            if (_trailRenderer == null)
            {
                // Try to find existing TrailRenderer on weapon bone
                var weaponBone = transform.Find(_weaponBonePath);
                if (weaponBone != null)
                {
                    _trailRenderer = weaponBone.GetComponent<TrailRenderer>();
                }
            }

            if (_trailRenderer != null)
            {
                _trailRenderer.emitting = false;
                SetupTrail();
            }
        }

        private void SetupTrail()
        {
            _trailRenderer.time = 0.1f;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(_trailColor, 0f),
                    new GradientColorKey(new Color(_trailColor.r, _trailColor.g, _trailColor.b, 0f), 1f)
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            _trailRenderer.colorGradient = gradient;

            var widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.3f),
                new Keyframe(1f, 0f));
            _trailRenderer.widthCurve = widthCurve;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
            EventBus.Subscribe<SkillChargeTickEvent>(OnChargeTickLate); // second sub for early activate check
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;

            // Early activate when close to full charge
            if (!_earlyActivated && e.Progress >= _earlyActivateProgress)
            {
                _earlyActivated = true;
                _isActive = true;
                if (_trailRenderer != null)
                    _trailRenderer.emitting = true;
            }
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;

            if (!_earlyActivated)
            {
                // Normal release: activate trail now
                _isActive = true;
                if (_trailRenderer != null)
                    _trailRenderer.emitting = true;
            }

            // Disable trail after a short delay to let it fade
            if (_trailRenderer != null)
                Invoke(nameof(StopTrail), _trailRenderer.time + 0.05f);
        }

        private void StopTrail()
        {
            _isActive = false;
            _earlyActivated = false;
            if (_trailRenderer != null)
                _trailRenderer.emitting = false;
        }
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/SlashTrailVFX.cs*
git commit -m "feat: add SlashTrailVFX (ice blue slash trail on weapon)"
```

---

### Task 7: Create IceDecalVFX.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/IceDecalVFX.cs`

- [ ] **Step 1: Write the file**

```csharp
using System.Collections.Generic;
using DG.Tweening;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 地面冰裂贴花 — 蓄满释放时施法者脚底出现冰裂纹理
    /// </summary>
    public class IceDecalVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private GameObject _decalPrefab;
        [SerializeField] private float _duration = 3f;
        [SerializeField] private float _fadeDuration = 0.5f;

        private readonly Stack<GameObject> _pool = new();
        private readonly List<ActiveDecal> _active = new();

        private class ActiveDecal
        {
            public GameObject Root;
            public Material Mat;
            public Tween FadeTween;
            public float SpawnTime;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!e.IsFullCharge || !WatchesSkill(e.SkillId)) return;
            SpawnDecal(e.CasterId);
        }

        private void SpawnDecal(int casterId)
        {
            if (_decalPrefab == null) return;

            // Find caster position
            var go = GameObject.Find(casterId.ToString());
            Vector3 pos;
            if (go != null)
            {
                pos = go.transform.position;
                pos.y = 0.01f; // ground level
            }
            else
            {
                pos = transform.position;
                pos.y = 0.01f;
            }

            GameObject decal;
            if (_pool.Count > 0)
            {
                decal = _pool.Pop();
                decal.SetActive(true);
                decal.transform.position = pos;
            }
            else
            {
                decal = Instantiate(_decalPrefab, pos, Quaternion.Euler(90f, 0f, 0f));
            }

            var renderer = decal.GetComponent<Renderer>();
            Material mat = null;
            if (renderer != null)
            {
                mat = renderer.material;
                var c = mat.color;
                c.a = 1f;
                mat.color = c;
            }

            var entry = new ActiveDecal { Root = decal, Mat = mat, SpawnTime = Time.time };

            // Fade out before returning to pool
            if (mat != null)
            {
                entry.FadeTween = mat.DOFade(0f, _fadeDuration)
                    .SetDelay(_duration - _fadeDuration)
                    .OnComplete(() => ReturnToPool(entry));
            }
            else
            {
                // No material to fade, just return after duration
                Invoke(nameof(() => ReturnToPool(entry)), _duration);
            }

            _active.Add(entry);
        }

        private void ReturnToPool(ActiveDecal entry)
        {
            _active.Remove(entry);
            entry.FadeTween?.Kill();
            entry.Root.SetActive(false);
            _pool.Push(entry.Root);
        }

        private void Update()
        {
            // Cleanup expired entries without fade materials
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Mat == null && Time.time - _active[i].SpawnTime > _duration)
                    ReturnToPool(_active[i]);
            }
        }

        private void OnDestroy()
        {
            foreach (var entry in _active)
            {
                entry.FadeTween?.Kill();
                Destroy(entry.Root);
            }
            while (_pool.Count > 0)
                Destroy(_pool.Pop());
        }
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/IceDecalVFX.cs*
git commit -m "feat: add IceDecalVFX (ground ice crack decal on full charge)"
```

---

### Task 8: Create IceBurstVFX.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/IceBurstVFX.cs`

- [ ] **Step 1: Write the file**

```csharp
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 命中冰爆 — 命中点产生冰晶炸裂粒子
    /// </summary>
    public class IceBurstVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private GameObject _iceBurstPrefab;

        private void OnEnable()
        {
            EventBus.Subscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnHitTarget(SkillHitTargetEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;

            if (_iceBurstPrefab != null)
            {
                var instance = Instantiate(_iceBurstPrefab, e.HitPosition, Quaternion.identity);
                var ps = instance.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ps.Play();
                    // Auto-destroy after particles finish
                    Destroy(instance, ps.main.duration + ps.main.startLifetime.constantMax);
                }
                else
                {
                    Destroy(instance, 2f);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/IceBurstVFX.cs*
git commit -m "feat: add IceBurstVFX (ice crystal burst on hit)"
```

---

### Task 9: Create SkillFreezeEffector.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/SkillFreezeEffector.cs`

- [ ] **Step 1: Write the file**

```csharp
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Skills.Effect;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 蓄满冻结 — 蓄满释放命中目标时施加冻结状态
    /// </summary>
    public class SkillFreezeEffector : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private float _freezeDuration = 2f;

        private void OnEnable()
        {
            EventBus.Subscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnHitTarget(SkillHitTargetEvent e)
        {
            if (!e.IsFullCharge || !WatchesSkill(e.SkillId)) return;

            // Find the target GameObject and apply freeze
            // IEffectTarget is the standard interface for skill targets
            var hitPos = e.HitPosition;
            var colliders = Physics.OverlapSphere(hitPos, 2f);
            foreach (var col in colliders)
            {
                if (col.TryGetComponent(out IEffectTarget target))
                {
                    ApplyFreeze(target);
                    break; // Freeze one target
                }
            }
        }

        private void ApplyFreeze(IEffectTarget target)
        {
            // Freeze via existing Stun system (immobilize with ice visuals)
            var freezeEffect = new StunEffectData
            {
                Duration = _freezeDuration,
                CanBeCleanse = false
            };
            freezeEffect.Apply(null, target);
        }
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/SkillFreezeEffector.cs*
git commit -m "feat: add SkillFreezeEffector (freeze status on full-charge hit)"
```

---

### Task 10: Scene setup — add VFX components to player

- [ ] **Step 1: Find player GameObject in scene**

Run `gameobject-find` with name "Sys3CEntry" or the player root to identify the player GameObject.

- [ ] **Step 2: Add VFX components**

Add these components to the player GameObject:
1. `Hotfix.GameSystems.VFX.FrostAuraVFX` — configure `_watchSkillIds` = [20002]
2. `Hotfix.GameSystems.VFX.SwordGlowVFX` — configure `_watchSkillIds` = [20002]
3. `Hotfix.GameSystems.VFX.SlashTrailVFX` — configure `_watchSkillIds` = [20002]
4. `Hotfix.GameSystems.VFX.IceDecalVFX` — configure `_watchSkillIds` = [20002]
5. `Hotfix.GameSystems.VFX.IceBurstVFX` — configure `_watchSkillIds` = [20002]
6. `Hotfix.GameSystems.VFX.SkillFreezeEffector` — configure `_watchSkillIds` = [20002]

- [ ] **Step 3: Save scene**

Use `scene-save`.

- [ ] **Step 4: Enter play mode, verify no errors**

Use `editor-application-set-state` to enter play mode. Hold SkillR key, observe Console for errors. Release and verify. Exit play mode.

- [ ] **Step 5: Commit**

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: add frost VFX components to player for SkillR"
```

---

### Task 11: Create placeholder particle/mesh assets (editor work)

> **Note:** This task requires Unity Editor interaction. The actual particle effects need art design, but placeholder prefabs can be created to prevent null-reference errors.

- [ ] **Step 1: Create placeholder FrostAura particle prefab**

Create a new GameObject → ParticleSystem. Configure as circular burst around origin, save as `Assets/Prefabs/VFX/FrostAuraParticle.prefab`. Assign `_auraParticles` reference on FrostAuraVFX component.

- [ ] **Step 2: Create placeholder IceBurst particle prefab**

Create a new ParticleSystem with short burst, save as `Assets/Prefabs/VFX/IceBurstParticle.prefab`. Assign `_iceBurstPrefab` on IceBurstVFX component.

- [ ] **Step 3: Create placeholder IceDecal prefab**

Create a Quad → rotate 90° on X → save as `Assets/Prefabs/VFX/IceDecal.prefab`. Assign `_decalPrefab` on IceDecalVFX component.

- [ ] **Step 4: Add TrailRenderer to weapon bone**

Find `weapon_r` bone on player model, add `TrailRenderer` component, configure with `Trail21bcg.mat` from Hovl Studio assets. Assign as `_trailRenderer` on SlashTrailVFX component.

- [ ] **Step 5: Save scene and commit**

```bash
git add Assets/Prefabs/VFX/
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: add placeholder VFX prefabs and TrailRenderer for SkillR"
```
