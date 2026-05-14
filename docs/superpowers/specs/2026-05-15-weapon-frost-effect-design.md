# Weapon Frost Effect Design (武器冰霜特效)

> 为武器添加待机冰霜雾气 + 挥动拖尾效果，由武器自身运动状态驱动，与现有技能事件驱动 VFX（SkillR Frost VFX）互补。

## 一、目标

为武器 Prefab 挂载一个 `WeaponVFXController`，实现：
- **待机状态**：剑身周围不规则冰霜雾气粒子环绕，1-2s 消散后随机重生，循环不断
- **挥动状态**：冰蓝渐变拖尾，由武器实际运动速度触发
- **过渡**：Shader 覆层（剑身 Frost 纹理）在两种状态间平滑切换
- **可复用**：通过 ScriptableObject 配置切换元素（冰/火/雷），不写新代码
- **移动端友好**：Additive 混合 + 低粒子峰值 + QualityLevel 降级

## 二、整体架构

```
WeaponVFXController          (状态机 + 运动检测 + 状态编排)
    ├── WeaponElementConfig   (ScriptableObject, 元素视觉参数)
    ├── WeaponMistParticles   (待机粒子，由 Config 驱动)
    ├── WeaponSurfaceShader   (Shader Frost 覆层，通过 MaterialProxy 写入)
    ├── WeaponTrailRenderer   (挥动拖尾，由 Config 驱动)
    └── WeaponMaterialProxy   (统一持有 MaterialPropertyBlock)
```

### 与现有代码的关系

```
                    WeaponMaterialProxy  (唯一 MPB 持有者)
                    ├── SetGlow(...)       ← SwordGlowVFX (现有，改为调用此方法)
                    ├── SetFrost(...)      ← WeaponVFXController
                    └── Apply()            ← 合并写入一次 SetPropertyBlock
```

`SwordGlowVFX`（现有）和 `WeaponSurfaceShader`（新建）通过同一个 `WeaponMaterialProxy` 写 Shader 属性，避免 MPB 覆盖冲突。

不改动 `FrostAuraVFX`、`SlashTrailVFX` 等现有组件——它们是技能事件驱动的，本设计是武器状态驱动的。

## 三、状态机

```
        ┌──────────┐  角速度 > _swingThreshold   ┌──────────┐
        │   Idle   │ ─────────────────────────→  │ Swinging │
        │          │ ←─────────────────────────  │          │
        └──────────┘  角速度 < _swingThreshold   └──────────┘
                         且持续 _swingCooldown 秒
```

### 状态切换动作

| 子系统 | 进入 Idle | 进入 Swinging |
|--------|----------|---------------|
| WeaponMistParticles | SetVisible(true) | SetVisible(false) |
| WeaponSurfaceShader | FrostAmount → target (0.5) | FrostAmount → 0 |
| WeaponTrailRenderer | SetEmitting(false) | SetEmitting(true) |

Shader `_FrostAmount` 用 0.3s lerp 过渡，避免突变。

## 四、组件设计

### 4.1 WeaponVFXController

文件：`Assets/Scripts/Hotfix/GameSystems/VFX/WeaponVFXController.cs`

```csharp
public class WeaponVFXController : MonoBehaviour
{
    [SerializeField] private WeaponElementConfig _elementConfig;
    [SerializeField] private float _swingThreshold = 120f;   // 角速度阈值 (度/秒)
    [SerializeField] private float _swingCooldown = 0.3f;    // 回落后延迟切回 Idle

    // 子组件引用 (GetComponent 获取)
    private WeaponMistParticles _mistParticles;
    private WeaponSurfaceShader _surfaceShader;
    private WeaponTrailRenderer _trailRenderer;
    private WeaponMaterialProxy _materialProxy;

    // 状态
    private bool _isActive;
    private bool _isSwinging;
    private float _swingTimer;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;

    public void SetActive(bool active);  // 开发者控制开关
}
```

- 每帧计算 Transform 角速度（`Quaternion.Angle`）/ 线速度
- 角速度 > _swingThreshold → Swinging
- 角速度 < _swingThreshold 持续 _swingCooldown 秒 → Idle
- SetActive(false) 时全部子系统关闭

### 4.2 WeaponElementConfig

文件：`Assets/Scripts/Hotfix/GameSystems/VFX/WeaponElementConfig.cs`

```csharp
[CreateAssetMenu(menuName = "VFX/Weapon Element Config")]
public class WeaponElementConfig : ScriptableObject
{
    // Mist Particles
    public Color MistStartColor = Color.white;
    public Color MistEndColor = new Color(1, 1, 1, 0);
    public float MistEmissionRate = 15f;
    public float MistLifetimeMin = 1f;
    public float MistLifetimeMax = 2f;
    public float MistStartSizeMin = 0.1f;
    public float MistStartSizeMax = 0.3f;
    public float MistOrbitalSpeedMin = 2f;
    public float MistOrbitalSpeedMax = 5f;
    public ParticleSystemShapeType MistShape = ParticleSystemShapeType.Cylinder;
    public Vector3 MistShapeScale = new Vector3(0.3f, 0.75f, 0.3f);

    // Trail
    public Color TrailColor = new Color(0.2f, 0.5f, 1f);
    public float TrailTime = 0.15f;
    public float TrailWidth = 0.3f;

    // Frost Shader
    public Color FrostColor = new Color(0.6f, 0.8f, 1f, 1f);
    public float FrostAmount = 0.5f;
    public float FrostFlowSpeed = 0.05f;
    public float FrostBlendTime = 0.3f;

    // Performance
    public QualityLevel Quality = QualityLevel.High;
    public int MistMaxParticlesHigh = 30;
    public int MistMaxParticlesLow = 15;
}
```

### 4.3 WeaponMaterialProxy

文件：`Assets/Scripts/Hotfix/GameSystems/VFX/WeaponMaterialProxy.cs`

```csharp
public class WeaponMaterialProxy : MonoBehaviour
{
    private Renderer _weaponRenderer;
    private MaterialPropertyBlock _propBlock;

    private Color _glowEdgeColor = Color.black;
    private float _glowIntensity;
    private Color _frostColor = Color.white;
    private float _frostAmount;
    private float _frostFlowSpeed;

    public void SetGlow(Color edgeColor, float intensity);
    public void SetFrost(Color color, float amount, float speed);
    public void Apply();  // 合并写入 SetPropertyBlock
}
```

### 4.4 WeaponMistParticles

```csharp
public class WeaponMistParticles : MonoBehaviour
{
    private ParticleSystem _ps;
    public void Init(WeaponElementConfig config);
    public void SetVisible(bool visible);
}
```

内部管理 ParticleSystem，根据 Config 设置 Main/Emission/Shape/ColorOverLifetime/VelocityOverLifetime/Noise 模块参数。

### 4.5 WeaponSurfaceShader

```csharp
public class WeaponSurfaceShader : MonoBehaviour
{
    private WeaponMaterialProxy _proxy;
    private WeaponElementConfig _config;
    private Coroutine _blendCoroutine;

    public void Init(WeaponMaterialProxy proxy, WeaponElementConfig config);
    public void SetFrostActive(bool active);  // true→target amount, false→0
}
```

### 4.6 WeaponTrailRenderer

```csharp
public class WeaponTrailRenderer : MonoBehaviour
{
    private TrailRenderer _trail;
    public void Init(WeaponElementConfig config);
    public void SetEmitting(bool emitting);
}
```

## 五、Shader 扩展

在 `Custom/SwordGlow` 基础上新增 Frost 段，不改动已有属性。

### 新增 Properties

```hlsl
[Header(Frost)]
_FrostAmount ("Frost Amount", Range(0, 1)) = 0.5
_FrostTex ("Frost Texture", 2D) = "white" {}
_FrostColor ("Frost Color", Color) = (0.6, 0.8, 1.0, 1)
_FrostFlowSpeed ("Frost Flow Speed", Range(0, 0.5)) = 0.05
```

### Fragment 扩展

```
frost = tex2D(_FrostTex, uv + _Time.y * _FrostFlowSpeed).r
       // 无贴图时走程序化噪声
frost *= (1 - NdotV * 0.5)   // 边缘更易结霜
finalColor = lerp(finalColor, _FrostColor, frost * _FrostAmount)
```

### 移动端降级

通过 `#pragma multi_compile _ FROST_TEXTURE` 控制，低端机不勾 `FROST_TEXTURE` 宏走程序化噪声。

## 六、性能与降级

### 粒子系统

| 参数 | High (PC) | Low (移动端) |
|------|-----------|-------------|
| 峰值粒子数 | 30 | 15 |
| 发射率 | 15/s | 8/s |
| 混合模式 | Additive | Additive |

### TrailRenderer

移动端 `time` 从 0.15s 降至 0.1s，`minVertexDistance` 从 0.1 增至 0.15。

### Shader

移动端走程序化噪声（0 额外纹理采样），PC 可用贴图。

### 降级选档

Controller 初始化时根据 `SystemInfo.graphicsDeviceType` 自动选档，支持代码覆盖。

## 七、数据流

```
Transform delta (每帧 Update)
        │
        ▼
WeaponVFXController.Update()
  角速度/线速度计算 → 判断状态切换
        │
   ┌────┼────┐
   ▼    ▼    ▼
Mist   Surf  Trail
       │
       ▼
WeaponMaterialProxy.Apply() → Renderer.SetPropertyBlock()
```

## 八、文件清单

### 新建

| 文件 | 位置 |
|------|------|
| WeaponVFXController.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| WeaponElementConfig.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| WeaponMaterialProxy.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| WeaponMistParticles.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| WeaponSurfaceShader.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| WeaponTrailRenderer.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| IceElementConfig.asset | `Assets/Scripts/Hotfix/GameSystems/VFX/Configs/` |

### 修改

| 文件 | 改动 |
|------|------|
| SwordGlowVFX.cs | 删除自有 `_propBlock`，改为通过 `WeaponMaterialProxy.SetGlow()` 写入 |
| SwordGlow.shader | 新增 Frost 段（_FrostAmount, _FrostTex, _FrostColor, _FrostFlowSpeed） |

### 不改动

- `FrostAuraVFX.cs` / `SlashTrailVFX.cs` / `IceBurstVFX.cs` / `IceDecalVFX.cs` — 技能事件驱动 VFX 保持不变

## 九、不在设计范围内

- 粒子预制体/材质的美术制作 — 本设计定义参数接口，实际 Asset 在 Unity Editor 中制作
- 音效
- 其他元素（火/雷）的 Config 文件 — 架构支持，后续创建 `.asset` 即可
