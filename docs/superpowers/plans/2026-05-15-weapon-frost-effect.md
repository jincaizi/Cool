# Weapon Frost Effect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a weapon-state-driven frost VFX system: idle frost mist particles cycling around the blade + ice-blue swing trail + shader-based frost surface overlay, controlled by a movement-detecting state machine.

**Architecture:** `WeaponVFXController` state machine detects swing via angular velocity and coordinates 4 sub-modules: `WeaponMaterialProxy` (shared MPB), `WeaponMistParticles` (particle system), `WeaponSurfaceShader` (frost shader lerp), `WeaponTrailRenderer` (TrailRenderer). All visual params in `WeaponElementConfig` ScriptableObject for future element reuse. Modify `SwordGlowVFX` to route through proxy, and extend `SwordGlow.shader` with frost properties.

**Tech Stack:** Unity 2022.3 LTS, C# (Hotfix layer), HLSL/CGPROGRAM, ParticleSystem, TrailRenderer, MaterialPropertyBlock, ScriptableObject

---

### Task 1: WeaponElementConfig (ScriptableObject)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/WeaponElementConfig.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/Configs/IceElementConfig.asset`

- [ ] **Step 1: Write WeaponElementConfig.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public enum VFXQualityLevel { High, Medium, Low }

    [CreateAssetMenu(menuName = "VFX/Weapon Element Config")]
    public class WeaponElementConfig : ScriptableObject
    {
        [Header("Mist Particles")]
        public Color MistStartColor = new Color(0.6f, 0.8f, 1f, 1f);
        public Color MistEndColor = new Color(0.6f, 0.8f, 1f, 0f);
        public float MistEmissionRate = 15f;
        public float MistLifetimeMin = 1f;
        public float MistLifetimeMax = 2f;
        public float MistStartSizeMin = 0.05f;
        public float MistStartSizeMax = 0.15f;

        [Header("Mist Shape")]
        public float MistShapeRadius = 0.3f;
        public float MistShapeHeight = 1.5f;
        public float MistOrbitalSpeedMin = 2f;
        public float MistOrbitalSpeedMax = 5f;
        public float MistNoiseStrength = 0.3f;
        public float MistNoiseFrequency = 0.5f;

        [Header("Trail")]
        public Color TrailColor = new Color(0.2f, 0.5f, 1f, 1f);
        public float TrailTime = 0.15f;
        public float TrailWidth = 0.3f;
        public float TrailMinVertexDistance = 0.1f;

        [Header("Frost Shader")]
        public Color FrostColor = new Color(0.6f, 0.8f, 1f, 1f);
        public float FrostAmount = 0.5f;
        public float FrostFlowSpeed = 0.05f;
        public float FrostBlendTime = 0.3f;

        [Header("Performance")]
        public VFXQualityLevel Quality = VFXQualityLevel.High;
        public int MaxParticlesHigh = 30;
        public int MaxParticlesLow = 15;
        public float EmissionRateLow = 8f;
    }
}
```

- [ ] **Step 2: Create the ScriptableObject asset via script**

Run the following via MCP `script-execute` to create `IceElementConfig.asset`:

```csharp
using UnityEngine;
using UnityEditor;
using Hotfix.GameSystems.VFX;

public class Script
{
    public static void Main()
    {
        var dir = "Assets/Scripts/Hotfix/GameSystems/VFX/Configs";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            var parent = "Assets/Scripts/Hotfix/GameSystems/VFX";
            AssetDatabase.CreateFolder(parent, "Configs");
        }
        var config = ScriptableObject.CreateInstance<WeaponElementConfig>();
        AssetDatabase.CreateAsset(config, dir + "/IceElementConfig.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created IceElementConfig.asset");
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/WeaponElementConfig.cs Assets/Scripts/Hotfix/GameSystems/VFX/WeaponElementConfig.cs.meta
git add Assets/Scripts/Hotfix/GameSystems/VFX/Configs/ Assets/Scripts/Hotfix/GameSystems/VFX/Configs.meta -A
git commit -m "feat: add WeaponElementConfig ScriptableObject with IceElementConfig"
```

---

### Task 2: WeaponMaterialProxy (shared MPB)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/WeaponMaterialProxy.cs`

- [ ] **Step 1: Write WeaponMaterialProxy.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponMaterialProxy : MonoBehaviour
    {
        [SerializeField] private string _weaponBonePath = "weapon_r";

        private Renderer _weaponRenderer;
        private MaterialPropertyBlock _propBlock;

        private bool _dirty;
        private Color _glowEdgeColor = Color.black;
        private float _glowIntensity;
        private Color _frostColor = Color.white;
        private float _frostAmount;
        private float _frostFlowSpeed;

        private static readonly int EdgeColorId     = Shader.PropertyToID("_EdgeColor");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int FrostColorId    = Shader.PropertyToID("_FrostColor");
        private static readonly int FrostAmountId   = Shader.PropertyToID("_FrostAmount");
        private static readonly int FrostFlowSpeedId = Shader.PropertyToID("_FrostFlowSpeed");

        public Renderer WeaponRenderer => _weaponRenderer;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            var t = transform.Find(_weaponBonePath);
            if (t != null)
            {
                _weaponRenderer = t.GetComponent<Renderer>();
                if (_weaponRenderer == null)
                    _weaponRenderer = t.GetComponentInChildren<Renderer>();
            }
            if (_weaponRenderer == null)
            {
                var allRenderers = GetComponentsInChildren<Renderer>();
                foreach (var r in allRenderers)
                {
                    var nm = r.name.ToLower();
                    if (nm.Contains("weapon") || nm.Contains("sword"))
                    { _weaponRenderer = r; break; }
                }
                if (_weaponRenderer == null && allRenderers.Length > 0)
                    _weaponRenderer = allRenderers[0];
            }
        }

        public void SetGlow(Color edgeColor, float intensity)
        {
            _glowEdgeColor = edgeColor;
            _glowIntensity = intensity;
            _dirty = true;
        }

        public void SetFrost(Color color, float amount, float flowSpeed)
        {
            _frostColor = color;
            _frostAmount = amount;
            _frostFlowSpeed = flowSpeed;
            _dirty = true;
        }

        public void Apply()
        {
            if (_weaponRenderer == null || !_dirty) return;
            _weaponRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(EdgeColorId, _glowEdgeColor);
            _propBlock.SetFloat(GlowIntensityId, _glowIntensity);
            _propBlock.SetColor(FrostColorId, _frostColor);
            _propBlock.SetFloat(FrostAmountId, _frostAmount);
            _propBlock.SetFloat(FrostFlowSpeedId, _frostFlowSpeed);
            _weaponRenderer.SetPropertyBlock(_propBlock);
            _dirty = false;
        }

        private void LateUpdate()
        {
            Apply();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/WeaponMaterialProxy.cs Assets/Scripts/Hotfix/GameSystems/VFX/WeaponMaterialProxy.cs.meta
git commit -m "feat: add WeaponMaterialProxy for unified MPB access"
```

---

### Task 3: Extend SwordGlow.shader with Frost

**Files:**
- Modify: `Assets/Shaders/SwordGlow.shader`

- [ ] **Step 1: Add Frost Properties block**

Insert after `_AmbientColor` property line (line 36) and before the closing `}` of Properties:

```hlsl
        [Header(Frost)]
        _FrostAmount ("Frost Amount", Range(0, 1)) = 0.0
        _FrostTex ("Frost Texture", 2D) = "white" {}
        _FrostColor ("Frost Color", Color) = (0.6, 0.8, 1.0, 1)
        _FrostFlowSpeed ("Frost Flow Speed", Range(0, 0.5)) = 0.05
```

- [ ] **Step 2: Add Frost uniforms in CGPROGRAM**

Insert after `half4 _AmbientColor;` line (line 90):

```hlsl
            half  _FrostAmount;
            half4 _FrostColor;
            half  _FrostFlowSpeed;
```

- [ ] **Step 3: Add procedural noise helper**

Insert before `v2f vert(appdata v)`:

```hlsl
            half proceduralFrost(half2 uv)
            {
                half2 p = floor(uv);
                half2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);
                half2 a = p + half2(1.0, 0.0);
                half2 b = p + half2(0.0, 1.0);
                half2 c = p + half2(1.0, 1.0);
                half h0 = frac(sin(dot(p, half2(12.9898, 78.233))) * 43758.5453);
                half h1 = frac(sin(dot(a, half2(12.9898, 78.233))) * 43758.5453);
                half h2 = frac(sin(dot(b, half2(12.9898, 78.233))) * 43758.5453);
                half h3 = frac(sin(dot(c, half2(12.9898, 78.233))) * 43758.5453);
                return lerp(lerp(h0, h1, f.x), lerp(h2, h3, f.x), f.y);
            }
```

- [ ] **Step 4: Add Frost blending in fragment**

Insert before `// ---- Composite ----` (before line 137):

```hlsl
                // ---- Frost overlay ----
                // 程序化噪声生成冰霜纹理，无需贴图即可工作。
                // _FrostTex 保留用于未来美术替换噪声为手绘霜纹。
                half noise = proceduralFrost(i.uv * 8.0 + _Time.y * _FrostFlowSpeed);
                half edgeFrost = noise * (1.0 - NdotV * 0.5);
                finalColor = lerp(finalColor, _FrostColor.rgb, edgeFrost * _FrostAmount * _FrostColor.a);
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Shaders/SwordGlow.shader
git commit -m "feat: add Frost overlay section to SwordGlow shader"
```

---

### Task 4: WeaponMistParticles

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/WeaponMistParticles.cs`

- [ ] **Step 1: Write WeaponMistParticles.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponMistParticles : MonoBehaviour
    {
        private ParticleSystem _ps;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.ShapeModule _shape;
        private ParticleSystem.VelocityOverLifetimeModule _velOverLifetime;
        private ParticleSystem.NoiseModule _noise;
        private ParticleSystem.ColorOverLifetimeModule _colorOverLifetime;

        public void Init(WeaponElementConfig config)
        {
            var child = new GameObject("_frostMistParticles");
            child.transform.SetParent(transform, false);
            _ps = child.AddComponent<ParticleSystem>();

            _main = _ps.main;
            _main.startLifetime = new ParticleSystem.MinMaxCurve(config.MistLifetimeMin, config.MistLifetimeMax);
            _main.startSize = new ParticleSystem.MinMaxCurve(config.MistStartSizeMin, config.MistStartSizeMax);
            _main.startSpeed = 0f;
            _main.simulationSpace = ParticleSystemSimulationSpace.Local;
            _main.startColor = config.MistStartColor;
            int maxP = config.Quality == VFXQualityLevel.Low ? config.MaxParticlesLow : config.MaxParticlesHigh;
            _main.maxParticles = maxP;
            _main.duration = 999f;
            _main.loop = true;

            _emission = _ps.emission;
            float rate = config.Quality == VFXQualityLevel.Low ? config.EmissionRateLow : config.MistEmissionRate;
            _emission.rateOverTime = rate;

            _shape = _ps.shape;
            _shape.shapeType = ParticleSystemShapeType.Cylinder;
            _shape.radius = config.MistShapeRadius;
            _shape.radiusThickness = 0.3f;
            _shape.arc = 360f;
            _shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            var shapeScale = _shape.scale;
            shapeScale = new Vector3(1f, config.MistShapeHeight / 2f, 1f);
            _shape.scale = shapeScale;

            _velOverLifetime = _ps.velocityOverLifetime;
            _velOverLifetime.enabled = true;
            _velOverLifetime.orbitalZ = new ParticleSystem.MinMaxCurve(config.MistOrbitalSpeedMin, config.MistOrbitalSpeedMax);
            _velOverLifetime.radial = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            _velOverLifetime.space = ParticleSystemSimulationSpace.Local;

            _colorOverLifetime = _ps.colorOverLifetime;
            _colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(config.MistStartColor, 0f), new GradientColorKey(config.MistStartColor, 0.3f),
                        new GradientColorKey(config.MistEndColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) });
            _colorOverLifetime.color = grad;

            _noise = _ps.noise;
            _noise.enabled = true;
            _noise.strength = config.MistNoiseStrength;
            _noise.frequency = config.MistNoiseFrequency;
            _noise.scrollSpeed = 0.3f;

            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetDefaultAdditiveMaterial();

            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetVisible(bool visible)
        {
            if (_ps == null) return;
            if (visible && !_ps.isPlaying)
                _ps.Play();
            else if (!visible && _ps.isPlaying)
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private static Material GetDefaultAdditiveMaterial()
        {
            // Unity built-in particle additive material
            var mat = new Material(Shader.Find("Particles/Standard Unlit"));
            mat.SetInt("_BlendOp", 0); // Add
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.color = Color.white;
            mat.SetColor("_EmissionColor", Color.white);
            return mat;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/WeaponMistParticles.cs Assets/Scripts/Hotfix/GameSystems/VFX/WeaponMistParticles.cs.meta
git commit -m "feat: add WeaponMistParticles for idle frost mist"
```

---

### Task 5: WeaponTrailRenderer

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/WeaponTrailRenderer.cs`

- [ ] **Step 1: Write WeaponTrailRenderer.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponTrailRenderer : MonoBehaviour
    {
        private TrailRenderer _trail;

        public void Init(WeaponElementConfig config)
        {
            var child = new GameObject("_frostTrail");
            child.transform.SetParent(transform, false);
            _trail = child.AddComponent<TrailRenderer>();

            _trail.time = config.TrailTime;
            _trail.minVertexDistance = config.TrailMinVertexDistance;
            _trail.startWidth = config.TrailWidth;
            _trail.endWidth = 0f;
            _trail.emitting = false;

            var gradient = new Gradient();
            var c = config.TrailColor;
            gradient.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(new Color(c.r, c.g, c.b, 0f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = gradient;

            _trail.material = new Material(Shader.Find("Particles/Standard Unlit"));
            _trail.material.SetInt("_BlendOp", 0);
            _trail.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _trail.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _trail.material.SetInt("_ZWrite", 0);
            _trail.material.EnableKeyword("_EMISSION");
            _trail.material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _trail.material.SetColor("_EmissionColor", c);

            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
        }

        public void SetEmitting(bool emitting)
        {
            if (_trail != null)
                _trail.emitting = emitting;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/WeaponTrailRenderer.cs Assets/Scripts/Hotfix/GameSystems/VFX/WeaponTrailRenderer.cs.meta
git commit -m "feat: add WeaponTrailRenderer for swing ice trail"
```

---

### Task 6: WeaponSurfaceShader (frost overlay control)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/WeaponSurfaceShader.cs`

- [ ] **Step 1: Write WeaponSurfaceShader.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponSurfaceShader : MonoBehaviour
    {
        private WeaponMaterialProxy _proxy;
        private WeaponElementConfig _config;
        private Coroutine _blendCoroutine;
        private float _currentAmount;

        public void Init(WeaponMaterialProxy proxy, WeaponElementConfig config)
        {
            _proxy = proxy;
            _config = config;
        }

        public void SetFrostActive(bool active)
        {
            if (_proxy == null || _config == null) return;
            if (_blendCoroutine != null)
                StopCoroutine(_blendCoroutine);
            _blendCoroutine = StartCoroutine(BlendRoutine(active ? _config.FrostAmount : 0f));
        }

        private System.Collections.IEnumerator BlendRoutine(float target)
        {
            float start = _currentAmount;
            float elapsed = 0f;
            float duration = _config.FrostBlendTime;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _currentAmount = Mathf.Lerp(start, target, elapsed / duration);
                _proxy.SetFrost(_config.FrostColor, _currentAmount, _config.FrostFlowSpeed);
                yield return null;
            }
            _currentAmount = target;
            _proxy.SetFrost(_config.FrostColor, _currentAmount, _config.FrostFlowSpeed);
            _blendCoroutine = null;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/WeaponSurfaceShader.cs Assets/Scripts/Hotfix/GameSystems/VFX/WeaponSurfaceShader.cs.meta
git commit -m "feat: add WeaponSurfaceShader for frost shader blending"
```

---

### Task 7: WeaponVFXController (state machine + wiring)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/WeaponVFXController.cs`

- [ ] **Step 1: Write WeaponVFXController.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponVFXController : MonoBehaviour
    {
        [SerializeField] private WeaponElementConfig _elementConfig;
        [SerializeField] private float _swingThreshold = 120f;
        [SerializeField] private float _swingCooldown = 0.3f;

        private WeaponMaterialProxy _materialProxy;
        private WeaponMistParticles _mistParticles;
        private WeaponSurfaceShader _surfaceShader;
        private WeaponTrailRenderer _trailRenderer;

        private bool _isActive;
        private bool _isSwinging;
        private float _swingTimer;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;

        private void Awake()
        {
            _materialProxy = GetComponent<WeaponMaterialProxy>();
            _mistParticles = GetComponent<WeaponMistParticles>();
            _surfaceShader = GetComponent<WeaponSurfaceShader>();
            _trailRenderer = GetComponent<WeaponTrailRenderer>();

            if (_elementConfig == null)
            {
                Debug.LogWarning($"[WeaponVFXController] {name} missing WeaponElementConfig, effect disabled");
                enabled = false;
                return;
            }

            var renderer = _materialProxy?.WeaponRenderer;
            if (renderer != null && !renderer.sharedMaterial.shader.name.Contains("SwordGlow"))
            {
                Debug.LogWarning(
                    $"[WeaponVFXController] {name} weapon material is '{renderer.sharedMaterial.shader.name}', " +
                    $"expected 'Custom/SwordGlow'. Shader effects won't work. " +
                    $"Please assign a material using the 'Custom/SwordGlow' shader.");
            }
        }

        private void Start()
        {
            if (_materialProxy == null)
            {
                Debug.LogWarning($"[WeaponVFXController] {name} missing WeaponMaterialProxy component");
                enabled = false;
                return;
            }
            _mistParticles?.Init(_elementConfig);
            _trailRenderer?.Init(_elementConfig);
            _surfaceShader?.Init(_materialProxy, _elementConfig);

            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
        }

        private void Update()
        {
            if (!_isActive || _elementConfig == null) return;

            float angularSpeed = Quaternion.Angle(_lastRotation, transform.rotation) / Time.deltaTime;

            if (!_isSwinging && angularSpeed > _swingThreshold)
            {
                _isSwinging = true;
                OnEnterSwinging();
            }
            else if (_isSwinging && angularSpeed < _swingThreshold)
            {
                _swingTimer += Time.deltaTime;
                if (_swingTimer >= _swingCooldown)
                {
                    _isSwinging = false;
                    _swingTimer = 0f;
                    OnEnterIdle();
                }
            }
            else if (_isSwinging && angularSpeed >= _swingThreshold)
            {
                _swingTimer = 0f;
            }

            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
        }

        private void OnEnterIdle()
        {
            _mistParticles?.SetVisible(true);
            _surfaceShader?.SetFrostActive(true);
            _trailRenderer?.SetEmitting(false);
        }

        private void OnEnterSwinging()
        {
            _mistParticles?.SetVisible(false);
            _surfaceShader?.SetFrostActive(false);
            _trailRenderer?.SetEmitting(true);
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (!active)
            {
                _mistParticles?.SetVisible(false);
                _surfaceShader?.SetFrostActive(false);
                _trailRenderer?.SetEmitting(false);
                _isSwinging = false;
                _swingTimer = 0f;
            }
            else
            {
                _lastPosition = transform.position;
                _lastRotation = transform.rotation;
                OnEnterIdle();
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/WeaponVFXController.cs Assets/Scripts/Hotfix/GameSystems/VFX/WeaponVFXController.cs.meta
git commit -m "feat: add WeaponVFXController state machine for idle/swing frost effects"
```

---

### Task 8: Modify SwordGlowVFX to use WeaponMaterialProxy

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs`

- [ ] **Step 1: Replace SwordGlowVFX.cs — route through WeaponMaterialProxy**

Replace the entire file content:

```csharp
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class SwordGlowVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;

        [Header("Glow Settings")]
        [SerializeField] private Color _edgeColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _glowIntensityMin = 0.15f;
        [SerializeField] private float _glowIntensityMax = 1.5f;

        private WeaponMaterialProxy _materialProxy;
        private bool _isActive;

        private void Awake()
        {
            _materialProxy = GetComponent<WeaponMaterialProxy>();
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
            if (_materialProxy != null)
                _materialProxy.SetGlow(_edgeColor, _glowIntensityMin);
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            float intensity = Mathf.Lerp(_glowIntensityMin, _glowIntensityMax, e.Progress);
            if (_materialProxy != null)
                _materialProxy.SetGlow(_edgeColor, intensity);
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            _isActive = false;
            if (_materialProxy != null)
                _materialProxy.SetGlow(Color.black, 0f);
        }
    }
}
```

**Changes from original:**
- Removed `_weaponBonePath`, `_weaponRenderer`, `_propBlock` fields — now handled by `WeaponMaterialProxy`
- Removed `EdgeColorId`, `GlowIntensityId` shader property IDs — now in `WeaponMaterialProxy`
- Removed `SetGlow()` private method — replaced by `_materialProxy.SetGlow()`
- `Awake` now just gets the `WeaponMaterialProxy` reference
- All shader interaction goes through `_materialProxy.SetGlow()`

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs
git commit -m "refactor: route SwordGlowVFX through WeaponMaterialProxy"
```

---

### Task 9: Final assembly verification

**Files:**
- Verify all files compile correctly

- [ ] **Step 1: Refresh asset database and check for compilation errors**

```bash
# Via MCP: assets-refresh with ForceUpdate, then check console logs
```

Run via MCP `assets-refresh` with `ForceUpdate`, then `console-get-logs` filtered to `Error` type. Expected: zero compilation errors.

- [ ] **Step 2: Verify component attachment scenario**

The expected setup on a weapon Prefab (e.g., `MagicSword_Ice.prefab`):

```
WeaponRoot (GameObject)
  ├── weapon_r (bone, with MeshRenderer using Custom/SwordGlow material)
  └── Components:
      ├── WeaponMaterialProxy   (_weaponBonePath = "weapon_r")
      ├── WeaponMistParticles
      ├── WeaponSurfaceShader
      ├── WeaponTrailRenderer
      ├── WeaponVFXController   (_elementConfig = IceElementConfig)
      └── SwordGlowVFX          (existing, already attached)
```

- [ ] **Step 3: Commit any remaining .meta files**

```bash
git status
# Add any untracked .meta files
git add -A Assets/Scripts/Hotfix/GameSystems/VFX/
git commit -m "chore: add meta files for weapon frost VFX"
```
