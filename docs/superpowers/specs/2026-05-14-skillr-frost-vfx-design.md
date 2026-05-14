# SkillR Frost VFX System Design (大招 R 冰霜特效系统)

> 为蓄力技能 SkillR（挥剑转圈）添加冰霜主题视觉特效 + 蓄满冻结 gameplay 效果

## 一、目标

通过 EventBus 驱动的分层 VFX 架构，为 SkillR 添加五层冰霜特效：蓄力寒气、剑身发光、挥砍拖尾、地面冰裂、命中冰爆。蓄满释放时附带冻结状态效果。架构支持未来任意技能复用。

## 二、整体架构

```
                       EventBus
               ─────────────────────
               SkillChargingStartedEvent (skillId)
               SkillChargeTickEvent (skillId, progress)
               SkillReleasedEvent (skillId, isFullCharge, casterId)
               SkillHitTargetEvent (skillId, casterId, hitPosition, isFullCharge)
               ─────────────────────
                    │       │       │       │       │
                    ▼       ▼       ▼       ▼       ▼
               ┌────────┬────────┬────────┬────────┬──────────┐
               │Frost   │Sword   │Slash   │Ice     │Freeze    │
               │Aura    │Glow    │Trail   │Decal+  │Effector  │
               │VFX     │VFX     │VFX     │Burst   │(gameplay)│
               └────────┴────────┴────────┴────────┴──────────┘
```

- SkillExecutor 在状态切换时 emit 事件，不改动核心逻辑
- 每个 VFX 组件独立订阅事件，通过 `_watchSkillIds` 过滤只关心自己的技能
- Freeze effector 订阅 `SkillHitTargetEvent`，`IsFullCharge=true` 时施加冻结

## 三、事件定义

文件：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/SkillVFXEvents.cs`

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    public struct SkillChargingStartedEvent : IEvent
    {
        public int SkillId;
    }

    public struct SkillChargeTickEvent : IEvent
    {
        public int SkillId;
        public float Progress;   // 0.0 → 1.0
    }

    public struct SkillReleasedEvent : IEvent
    {
        public int SkillId;
        public bool IsFullCharge;
        public int CasterId;
    }

    public struct SkillHitTargetEvent : IEvent
    {
        public int SkillId;
        public int CasterId;
        public Vector3 HitPosition;
        public bool IsFullCharge;
    }
}
```

### 事件发出点

| 事件 | 发出位置 | 时机 |
|------|---------|------|
| SkillChargingStarted | SkillStateMachine → TryStart(), 检测到 isChargedSkill 时 | 按下 SkillR 键 |
| SkillChargeTick | SkillStateMachine → UpdateCharging() | 蓄力每帧 |
| SkillReleased | SkillStateMachine → 从 Charging 转 Execution | 松手释放 |
| SkillHitTarget | SkillExecutor → OnHitConfirm() | 命中目标时 |

## 四、VFX 组件

所有组件放在 `Assets/Scripts/Hotfix/GameSystems/VFX/`，新程序集 `VFX.asmdef`，引用 `Hotfix.GameSystems.Sys3C.Core`。

### 4.1 FrostAuraVFX（蓄力寒气聚集）

- **订阅：** SkillChargingStarted（激活）、SkillChargeTick（更新强度）、SkillReleased（关闭）
- **效果：** 角色周身冰霜粒子旋涡，强度随 Progress 递增
- **实现：** 一个 ParticleSystem 预制体，挂载在角色根节点下，修改 emission rate / startColor / orbitalVelocity

```
Progress 0.0 → 粒子 5/s,  半径 0.8m, 浅白,  转速 1x
Progress 0.5 → 粒子 20/s, 半径 1.2m, 浅蓝,  转速 2x
Progress 1.0 → 粒子 50/s, 半径 1.5m, 深蓝,  转速 3x
```

### 4.2 SwordGlowVFX（剑身发光）

- **订阅：** SkillChargingStarted（启用）、SkillChargeTick（更新强度）、SkillReleased（关闭）
- **效果：** 剑身自发光蓝色渐变，强度 0.3 → 2.0
- **实现：** 找到 weapon_r 骨骼下的 MeshRenderer，用 MaterialPropertyBlock 修改 `_EmissionColor`，不依赖粒子系统

### 4.3 SlashTrailVFX（挥砍拖尾）

- **订阅：** SkillReleased（激活）、SkillChargeTick(Progress≥0.8 提前开启)
- **效果：** 剑刃挥砍路径上的冰蓝渐变拖尾
- **实现：** TrailRenderer 组件挂在 weapon_r 骨骼上，材质复用 `Assets/Hovl Studio/MoonSword/Materials/Trail21bcg.mat`，tint 为冰蓝色
- **配置：** time=0.1s, widthCurve=0.3→0.0, colorGradient=冰蓝→透明白
- **清理：** 技能结束（Recovery 阶段）关闭 TrailRenderer.emitting

### 4.4 IceDecalVFX（地面冰裂贴花）

- **订阅：** SkillReleased（且 IsFullCharge=true）
- **效果：** 施法者脚底出现冰蓝色裂纹圆形贴花，持续 3s 后淡出
- **实现：** 对象池化的 Quad 面片 + 冰裂贴图，放到地面位置，DOTween alpha fade

### 4.5 IceBurstVFX（命中冰爆粒子）

- **订阅：** SkillHitTarget
- **效果：** 命中点冰晶炸裂（30 碎片向外 2m 扩散）+ 0.5s 白色冷雾
- **实现：** 一个 ParticleSystem 预制体，Instantiate 在 hitPosition，AutoDestroy 自动清理

## 五、冻结 Gameplay 效果

### 5.1 SkillFreezeEffector

- **订阅：** SkillHitTarget（且 IsFullCharge=true 且 skillId 匹配）
- **效果：** 调用 `target.ApplyStatus(StatusType.Freeze, duration)`
- **配置：** `[SerializeField] private int[] _watchSkillIds`, `[SerializeField] private float _freezeDuration = 2.0f`

冻结效果的具体实现（角色动画暂停、移动禁止等）走现有的 Status/Effect 系统，不在本设计范围内。

## 六、SkillExecutor 改动

仅增加事件发出，不改动现有逻辑：

```csharp
// TryStart 中，检测到 Charged 类型时：
EventBus.Emit(new SkillChargingStartedEvent { SkillId = data.SkillId });

// UpdateCharging 中：
EventBus.Emit(new SkillChargeTickEvent { SkillId = data.SkillId, Progress = progress });

// 从 Charging → Execution 时：
EventBus.Emit(new SkillReleasedEvent { 
    SkillId = data.SkillId, 
    IsFullCharge = progress >= 1f, 
    CasterId = owner.GetInstanceID() 
});

// OnHitConfirm 中：
EventBus.Emit(new SkillHitTargetEvent { 
    SkillId = data.SkillId, 
    CasterId = owner.GetInstanceID(),
    HitPosition = hitPoint, 
    IsFullCharge = wasFullCharge 
});
```

`ChargeProgress` 和 `wasFullCharge` 在 `SkillExecutor` 内部已经有——前者通过 `GetChargeProgress()` 公开，后者需要在 `ReleaseCharge()` 时记一个布尔标志。

## 七、文件清单

### 新建

| 文件 | 位置 |
|------|------|
| SkillVFXEvents.cs | `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/` |
| FrostAuraVFX.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| SwordGlowVFX.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| SlashTrailVFX.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| IceDecalVFX.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| IceBurstVFX.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| SkillFreezeEffector.cs | `Assets/Scripts/Hotfix/GameSystems/VFX/` |
| VFX.asmdef | `Assets/Scripts/Hotfix/GameSystems/VFX/` |

### 修改

| 文件 | 改动 |
|------|------|
| SkillStateMachine.cs | 状态切换 emit 事件 |
| SkillExecutor.cs | 命中时 emit SkillHitTargetEvent；ReleaseCharge 记录 fullCharge 标志 |

### 预制体 / 资产（需新建）

| 资产 | 说明 |
|------|------|
| FrostAuraParticle.prefab | 寒气粒子系统 |
| IceBurstParticle.prefab | 冰爆粒子系统 |
| IceDecalMat.mat | 冰裂贴花材质 |

## 八、不在此设计范围内的内容

- 冻结状态的具体实现（移动禁止、动画冻结等）—— Status 系统独立处理
- 粒子预制体的美术制作——本设计只定义接口和参数，实际 prefab 由美术/粒子编辑器制作
- 音效（冰裂声、挥砍音效）—— 可仿照 VFX 模式另加 AudioVFX 组件
- 火系等其他元素技能——本设计确保架构支持，但不实现
