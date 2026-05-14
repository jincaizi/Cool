# Outline Shader System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a double-pass vertex-extrusion outline shader for Built-in RP, with configurable pulse control and hit-flash effect.

**Architecture:** Outline.shader (Pass 0 normal + Pass 1 Cull Front vertex extrusion) controlled by MaterialPropertyBlock from SwordGlowVFX (pulse) and HitFlashVFX (one-shot flash). All parameters exposed as [SerializeField].

**Tech Stack:** Unity 2022.3 Built-in RP, HLSL/CGPROGRAM, DOTween

---

## File Structure

| Action | File | Purpose |
|--------|------|---------|
| Create | `Assets/Shaders/Outline.shader` | Double-pass outline shader |
| Create | `Assets/Materials/Outline.mat` | Default outline material |
| Modify | `Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs` | Add outline pulse control |
| Create | `Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs` | Hit flash with configurable timing |

---

### Task 1: Create Outline.shader

**Files:**
- Create: `Assets/Shaders/Outline.shader`

- [ ] **Step 1: Write the shader**

```hlsl
Shader "Custom/Outline"
{
    Properties
    {
        _Color ("Main Color", Color) = (1, 1, 1, 1)
        _MainTex ("Main Texture", 2D) = "white" {}

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.2, 0.5, 1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        _OutlineGlow ("Outline Glow", Range(0, 3)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        // Pass 0: Normal rendering
        Pass
        {
            Name "BASE"
            Cull Back
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDCG
        }

        // Pass 1: Outline
        Pass
        {
            Name "OUTLINE"
            Cull Front
            Lighting Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert_outline
            #pragma fragment frag_outline
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineGlow;

            v2f vert_outline(appdata v)
            {
                v2f o;
                float3 normal = normalize(v.normal);
                v.vertex.xyz += normal * _OutlineWidth;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag_outline(v2f i) : SV_Target
            {
                return fixed4(_OutlineColor.rgb * _OutlineGlow, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error — must be zero.

- [ ] **Step 3: Commit**

```bash
git add Assets/Shaders/Outline.shader*
git commit -m "feat: add Custom/Outline double-pass vertex extrusion shader"
```

---

### Task 2: Create Outline.mat

**Files:**
- Create: `Assets/Materials/Outline.mat`

- [ ] **Step 1: Create the material**

Use `assets-material-create` with shader name "Custom/Outline" at path `Assets/Materials/Outline.mat`.

- [ ] **Step 2: Set default values**

Use `assets-modify` with jsonPatch to set:
```json
{
  "_OutlineColor": {"r": 0.2, "g": 0.5, "b": 1.0, "a": 1.0},
  "_OutlineWidth": 0.02,
  "_OutlineGlow": 1.0
}
```

- [ ] **Step 3: Refresh and verify**

Run `assets-refresh`. Check for errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Materials/Outline.mat*
git commit -m "feat: add default Outline material (ice blue, 0.02 width)"
```

---

### Task 3: Modify SwordGlowVFX.cs — add outline pulse

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs`

- [ ] **Step 1: Add shader property IDs and pulse fields**

After `private static readonly int EmissionColorId` (line 17), add:

```csharp
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineGlowId = Shader.PropertyToID("_OutlineGlow");

        [Header("Outline Pulse")]
        [SerializeField] private Color _outlineColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _outlineWidthMin = 0.01f;
        [SerializeField] private float _outlineWidthMax = 0.04f;
        [SerializeField] private float _outlineGlowMin = 0.5f;
        [SerializeField] private float _outlineGlowMax = 1.5f;
        [SerializeField] private float _pulseFrequency = 3f;

        private float _pulseTime;
```

- [ ] **Step 2: Modify UpdateGlow to include outline pulse**

Replace the `UpdateGlow` method:

```csharp
        private void UpdateGlow(float t)
        {
            if (_weaponRenderer == null) return;
            _weaponRenderer.GetPropertyBlock(_propBlock);

            // Emission glow
            float intensity = Mathf.Lerp(0.3f, _maxGlowIntensity, t);
            _propBlock.SetColor(EmissionColorId, _glowColor * intensity);

            // Outline pulse
            if (_isActive)
            {
                _pulseTime += Time.deltaTime;
                float pulse = Mathf.Sin(_pulseTime * _pulseFrequency * Mathf.PI * 2f) * 0.5f + 0.5f;
                float width = Mathf.Lerp(_outlineWidthMin, _outlineWidthMax, pulse);
                float glow = Mathf.Lerp(_outlineGlowMin, _outlineGlowMax, pulse);
                _propBlock.SetColor(OutlineColorId, _outlineColor);
                _propBlock.SetFloat(OutlineWidthId, width);
                _propBlock.SetFloat(OutlineGlowId, glow);
            }
            else
            {
                _propBlock.SetFloat(OutlineWidthId, 0f);
            }

            _weaponRenderer.SetPropertyBlock(_propBlock);
        }
```

- [ ] **Step 3: Reset pulse time on charge start/end**

In `OnChargingStarted`, add `_pulseTime = 0f;` after `_isActive = true;`.
In `OnReleased`, after `_isActive = false;`, ensure UpdateGlow(0f) zeros the width.

- [ ] **Step 4: Refresh and verify**

Run `assets-refresh`. Check for compilation errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs
git commit -m "feat: add outline pulse control to SwordGlowVFX"
```

---

### Task 4: Create HitFlashVFX.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs`

- [ ] **Step 1: Write the file**

```csharp
using DG.Tweening;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 受击闪白 — 命中瞬间描边尖峰→衰减归零，参数全部可调控
    /// </summary>
    public class HitFlashVFX : MonoBehaviour
    {
        [SerializeField] private float _flashWidth = 0.05f;
        [SerializeField] private float _flashDuration = 0.15f;
        [SerializeField] private Color _flashStartColor = Color.white;
        [SerializeField] private Color _flashEndColor = Color.red;
        [SerializeField] private Renderer _targetRenderer;

        private MaterialPropertyBlock _propBlock;
        private Tween _flashTween;
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            if (_targetRenderer == null)
                _targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_targetRenderer == null)
                _targetRenderer = GetComponentInChildren<MeshRenderer>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            _flashTween?.Kill();
        }

        private void OnPlayerDamaged(DamageEvent e)
        {
            TriggerFlash();
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            TriggerFlash();
        }

        private void TriggerFlash()
        {
            if (_targetRenderer == null) return;

            _flashTween?.Kill();

            _targetRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorId, _flashStartColor);
            _propBlock.SetFloat(OutlineWidthId, _flashWidth);
            _targetRenderer.SetPropertyBlock(_propBlock);

            _flashTween = DOTween.To(() => _flashWidth, width =>
            {
                if (_targetRenderer == null) return;
                _targetRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(OutlineWidthId, width);
                float t = 1f - width / _flashWidth;
                _propBlock.SetColor(OutlineColorId, Color.Lerp(_flashStartColor, _flashEndColor, t));
                _targetRenderer.SetPropertyBlock(_propBlock);
            }, 0f, _flashDuration).SetTarget(_targetRenderer);
        }

        private void OnDestroy()
        {
            _flashTween?.Kill();
        }
    }
}
```

- [ ] **Step 2: Refresh and verify**

Run `assets-refresh`. Check `console-get-logs` filter Error — must be zero.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs*
git commit -m "feat: add HitFlashVFX with configurable flash timing"
```

---

### Task 5: Scene setup — apply outline material and add HitFlashVFX

- [ ] **Step 1: Apply Outline.mat to weapon**

Find `weapon_r/OHS03` renderer on the player, replace its material with `Assets/Materials/Outline.mat`.

- [ ] **Step 2: Add HitFlashVFX to player**

Add `Hotfix.GameSystems.VFX.HitFlashVFX` component to `MaleCharacterPBR` GameObject.

- [ ] **Step 3: Save scene**

Use `scene-save`.

- [ ] **Step 4: Enter play mode, verify**

Press SkillR key, observe outline pulse. Trigger a hit on the player, observe flash. Check console for errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: apply outline material to weapon, add HitFlashVFX to player"
```
