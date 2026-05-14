# Outline Shader System Design（描边 Shader 系统）

> Built-in 渲染管线下顶点外扩法描边 shader，支持动态脉冲宽度，用于武器蓄力发光和角色/NPC 受击闪白

## 一、目标

创建一个通用的描边 shader，通过 MaterialPropertyBlock 控制描边宽度和颜色，在动画中实现脉冲、呼吸和闪白效果。同时用于：
- **SkillR 武器蓄力**：冰蓝描边，呼吸脉冲（周期缩放）
- **受击闪白**：命中瞬间白色描边尖峰→衰减归零

## 二、Shader 设计

### 2.1 技术方案

顶点外扩法双 Pass：

```
Pass 0: 正常渲染（原材质）
Pass 1: Cull Front + 顶点沿法线外扩 + 描边颜色输出
```

- 文件：`Assets/Shaders/Outline.shader`
- 目标：Built-in Render Pipeline
- Shader 路径：`Custom/Outline`

### 2.2 Properties

```hlsl
Properties
{
    _Color ("Main Color", Color) = (1,1,1,1)
    _MainTex ("Main Texture", 2D) = "white" {}
    
    // Outline
    _OutlineColor ("Outline Color", Color) = (0.2, 0.5, 1, 1)  // 默认冰蓝
    _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02       // 世界单位
    _OutlineGlow ("Outline Glow", Range(0, 2)) = 1.0             // 发光强度
}
```

### 2.3 Pass 1（Outline）核心逻辑

```hlsl
// Vertex shader
v2f vert_outline(appdata v)
{
    v2f o;
    float3 normal = normalize(v.normal);
    // 沿法线外扩
    v.vertex.xyz += normal * _OutlineWidth;
    o.vertex = UnityObjectToClipPos(v.vertex);
    return o;
}

// Fragment shader
fixed4 frag_outline(v2f i) : SV_Target
{
    return fixed4(_OutlineColor.rgb, 1.0) * _OutlineGlow;
}
```

- Cull Front 确保只渲染背面（从背面看外扩就是描边）
- 描边宽度以世界单位计，不随屏幕分辨率变化
- `_OutlineGlow` 控制 HDR 发光强度（值 >1 时配合 Bloom 后处理效果更好）

### 2.4 占位材质的制作

创建 `Assets/Materials/Outline.mat`，引用 `Custom/Outline` shader，默认参数：
- `_OutlineColor = (0.2, 0.5, 1, 1)` 冰蓝
- `_OutlineWidth = 0.02`
- `_OutlineGlow = 1.0`

## 三、运行时控制

所有控制通过 `MaterialPropertyBlock` 实现，不创建额外 Material 实例（避免材质泄漏）。

### 3.1 脉冲动画（武器蓄力）

在 `SwordGlowVFX` 中增加描边控制。该组件已订阅 `SkillChargeTickEvent`，扩展为：

```
蓄力开始 → 描边启用
蓄力 progress 0→1:
  _OutlineColor: 浅蓝 (0.2, 0.5, 1) → 深蓝 (0.1, 0.3, 0.9)
  _OutlineWidth: 正弦脉冲，0.01→0.04 周期摆动
  _OutlineGlow: 0.5→1.5
释放 → 描边归零
```

脉冲数学：
```csharp
float pulse = Mathf.Sin(Time.time * _pulseFrequency) * 0.5f + 0.5f; // 0→1 呼吸
float width = Mathf.Lerp(0.01f, 0.04f, pulse);
```

### 3.2 受击闪白（角色/NPC）

新增 `HitFlashVFX` 组件，订阅 `DamageEvent`（玩家受击）/ `MonsterTakeDamageEvent`（怪物受击）：

可调控参数（`[SerializeField]`）：

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `_flashWidth` | 0.05 | 峰值描边宽度（世界单位） |
| `_flashDuration` | 0.15s | 衰减持续时间 |
| `_flashStartColor` | White | 起始颜色 |
| `_flashEndColor` | Red | 结束颜色 |

```
命中瞬间:
  _OutlineColor = _flashStartColor
  _OutlineWidth = _flashWidth
_flashDuration 内:
  _OutlineWidth → 0（线性衰减）
  _OutlineColor → _flashEndColor（线性插值）
触发方式: DOTween 快速 Tween
```

```csharp
// HitFlashVFX
[SerializeField] private float _flashWidth = 0.05f;
[SerializeField] private float _flashDuration = 0.15f;
[SerializeField] private Color _flashStartColor = Color.white;
[SerializeField] private Color _flashEndColor = Color.red;

private void TriggerFlash()
{
    if (_hitRenderer == null) return;
    
    // Kill any active flash tween first
    _flashTween?.Kill();
    
    _hitRenderer.GetPropertyBlock(_propBlock);
    _propBlock.SetColor("_OutlineColor", _flashStartColor);
    _propBlock.SetFloat("_OutlineWidth", _flashWidth);
    _hitRenderer.SetPropertyBlock(_propBlock);
    
    _flashTween = DOTween.To(() => _flashWidth, width =>
    {
        if (_hitRenderer == null) return;
        _hitRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetFloat("_OutlineWidth", width);
        float t = 1f - width / _flashWidth;
        _propBlock.SetColor("_OutlineColor", Color.Lerp(_flashStartColor, _flashEndColor, t));
        _hitRenderer.SetPropertyBlock(_propBlock);
    }, 0f, _flashDuration).SetTarget(_hitRenderer);
}
```

### 3.3 描边启用/禁用

武器描边只在 `SkillChargingStarted` 到 `SkillReleased` 之间启用。受击描边在每次命中时自动触发。为防止两个效果冲突：
- `SwordGlowVFX` 在蓄力期间设置 `_OutlineColor` → 冰蓝
- `HitFlashVFX` 在受击时覆盖 `_OutlineColor` → 白→红，0.15s 后自然清空
- 两者不互斥——蓄力期间被击中，描边先闪白，0.15s 后回到冰蓝呼吸

## 四、文件清单

### 新建

| 文件 | 说明 |
|------|------|
| `Assets/Shaders/Outline.shader` | 双 Pass 描边 shader |
| `Assets/Materials/Outline.mat` | 占位材质 |
| `Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs` | 受击闪白组件（4 个可调控参数：width/duration/startColor/endColor） |

### 修改

| 文件 | 改动 |
|------|------|
| `Assets/Scripts/Hotfix/GameSystems/VFX/SwordGlowVFX.cs` | 增加 `_OutlineColor`/`_OutlineWidth`/`_OutlineGlow` 的脉冲控制 |

### 场景配置

| 对象 | 操作 |
|------|------|
| 武器（weapon_r/OHS03） | 材质替换为 Outline.mat |
| Player（MaleCharacterPBR） | 挂载 HitFlashVFX 组件 |
| Monster 预制体 | 挂载 HitFlashVFX 组件（可选，后续） |

## 五、兼容性约束

- 描边 shader 替换武器的原始材质——如果武器有特殊贴图/颜色，需要在 Outline.shader 的 Pass 0 中正确采样 `_MainTex`
- 受击描边需要目标模型有 `Renderer` 组件。MonsterEntity 已有 SkinnedMeshRenderer，Player 有多个子 Renderer——`HitFlashVFX` 需要找到主 Renderer
- 顶点外扩法的前提：模型顶点法线平滑。Low-poly 模型描边效果最好，高面模型也没问题
- 描边宽度以世界单位计。远处物体描边变细是预期行为（视觉效果自然），如果需要固定屏幕宽度可改为 NDC 空间缩放

## 六、不在此设计范围

- 模型替换/新建——不涉及武器模型或角色模型修改
- 描边的后处理（Bloom 配合）——依赖项目是否启用 ImageEffect，基础描边不依赖它
- 多 LOD 的描边适配——当前项目无 LOD 系统
