# Sword Glow Shader Design（大师剑风格剑身发光 Shader）

> 单 Pass ForwardBase，UV 距离渐变 + 时间脉冲 + 符文流动，替代原有的 Custom/Outline shader

## 一、目标

创建一个类似塞尔达传说大师剑的剑身发光 shader：
- 剑身核心亮白，向边缘渐变为主题色（冰蓝）
- 呼吸脉冲，亮度周期性波动
- 可选符文纹理沿剑脊流动
- 保留 Lambert 漫反射 + Rim Light 保持武器立体感
- 通过 MaterialPropertyBlock 控制所有参数，供 SwordGlowVFX 驱动

## 二、Shader 规格

### 2.1 文件

`Assets/Shaders/SwordGlow.shader`，shader 名 `Custom/SwordGlow`

### 2.2 Properties

```hlsl
Properties
{
    // Base
    _Color ("Main Color", Color) = (1, 1, 1, 1)
    _MainTex ("Main Texture", 2D) = "white" {}

    [Header(Gradient)]
    _CoreColor ("Core Color (center)", Color) = (1, 1, 1, 1)
    _EdgeColor ("Glow Color (edge)", Color) = (0.2, 0.5, 1, 1)
    _GlowIntensity ("Glow Intensity", Range(0, 5)) = 1.5
    _GradientPower ("Gradient Power", Range(0.5, 4)) = 1.5

    [Header(Pulse)]
    _PulseSpeed ("Pulse Speed", Range(0, 10)) = 3.0
    _PulseMin ("Pulse Min", Range(0, 1)) = 0.3

    [Header(Flow)]
    [NoScaleOffset] _FlowTex ("Flow Texture", 2D) = "black" {}
    _FlowSpeed ("Flow Speed", Range(-2, 2)) = 0.5
    _FlowIntensity ("Flow Intensity", Range(0, 3)) = 0.8

    [Header(Rim)]
    _RimColor ("Rim Color", Color) = (0.3, 0.5, 0.8, 1)
    _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
}
```

### 2.3 渐变计算

```
UV.x = 0.0 → 剑脊一侧边缘, distToCenter = 1.0
UV.x = 0.5 → 剑脊中心线,  distToCenter = 0.0
UV.x = 1.0 → 剑脊另一侧, distToCenter = 1.0
```

```hlsl
float distToCenter = abs(i.uv.x - 0.5) * 2.0;   // 0→1
float glowFactor = pow(distToCenter, _GradientPower);
float4 glowColor = lerp(_CoreColor, _EdgeColor, glowFactor);
```

### 2.4 脉冲公式

```hlsl
float pulse = sin(_Time.y * _PulseSpeed * 2 * PI) * 0.5 + 0.5;  // 0→1
pulse = lerp(_PulseMin, 1.0, pulse);                              // 最低→满亮
```

### 2.5 最终混合

```hlsl
// 基础光照 (Lambert + Ambient + Shadow + Rim)
fixed4 finalColor = albedo * (ambient + diffuse * atten);
finalColor.rgb += rimLight;

// 发光层叠加
float3 glow = glowColor.rgb * _GlowIntensity * pulse;
if (_FlowTex is configured)
    glow += flowMask * _EdgeColor.rgb * _FlowIntensity * pulse;

finalColor.rgb += glow;
```

## 三、SwordGlowVFX 适配

当前 SwordGlowVFX 通过 MaterialPropertyBlock 控制 `_EmissionColor`。需改为控制新 shader 的属性：

| Shader Property | SwordGlowVFX 控制 |
|-----------------|-------------------|
| `_EdgeColor` | SkillR 蓄力时 → 冰蓝 (0.2, 0.5, 1)；平时 → 无辉光 (0,0,0) |
| `_GlowIntensity` | 随 charge progress 0→1.5 |
| `_PulseSpeed` | 不变，取 Inspector 配置 |
| `_PulseMin` | 蓄力中 = 0.3，未蓄力 = 0 |

控制逻辑：
```
蓄力开始: _EdgeColor = 冰蓝, _GlowIntensity = 0.15 (最低可见)
蓄力 progress 0→1: _GlowIntensity = 0.15 → 1.5, _EdgeColor 不变
释放: _EdgeColor = 黑, _GlowIntensity = 0 (辉光消失)
```

## 四、文件清单

### 新建

| 文件 | 说明 |
|------|------|
| `Assets/Shaders/SwordGlow.shader` | 大师剑发光 shader |
| `Assets/Materials/SwordGlow.mat` | 对应材质（可选，shader 即可） |

### 修改

| 文件 | 改动 |
|------|------|
| `Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs` | Property ID 改为新 shader 属性：`_EdgeColor` / `_GlowIntensity`，移除 outline 相关属性 |

### 废弃

| 文件 | 说明 |
|------|------|
| `Assets/Shaders/Outline.shader` | 不再使用 |
| `Assets/Materials/Outline.mat` | 不再使用 |

## 五、不在此设计范围

- 顶点外扩描边 — 描述的是完全不同的视觉效果，本 shader 不涉及
- Flow Texture 的绘制 — 需要美术提供一张剑脊符文遮罩贴图，本设计只定义贴图如何采样
- Post-Processing Bloom — 全局后处理如果需要，独立配置，不影响此 shader
