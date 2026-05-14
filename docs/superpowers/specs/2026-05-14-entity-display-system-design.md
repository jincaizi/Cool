# Entity Display System Design (铭牌 + 飘字统一系统)

> 替代现有 `NameplateManager` + `FloatingTextPool` + `NpcMirror` 三套独立方案

## 一、目标

将铭牌（头顶名字）和飘字（伤害/技能文字）统一为一个 ScreenSpaceOverlay Canvas 下的池化系统，提供 MMO 级飘字表现力，并支持未来扩展职业图标、称号等元素。

## 二、整体架构

```
EntityDisplayManager (MonoBehaviour Singleton, ScreenSpaceOverlay Canvas)
├── NameplatePool (持久租借 TMP 元素 x N)
├── FloatTextPool  (临时租借 TMP 元素 x M，动画结束自动归还)
├── DisplayEntry Registry (Dictionary<int, Entry>)  // 铭牌注册表
└── MergeTracker (Dictionary<(id, type), MergeEntry>)  // 飘字合并窗口
```

- 铭牌和飘字共享同一个 ScreenSpaceOverlay Canvas，单 Draw Call
- 每帧 LateUpdate 做 WorldToScreenPoint 批量位置更新
- 池化：NameplatePool 持久租借（注册→注销），FloatTextPool 临时租借（飘出→自动归还）

## 三、铭牌子系统

### 3.1 生命周期

```
实体创建 → Register(id, transform, config)
  → 从 NameplatePool 租 TMP 实例
  → 记录 DisplayEntry

每帧 LateUpdate:
  → WorldToScreenPoint → 更新 anchoredPosition
  → 距离 > 剔除距离 → SetActive(false)
  → 距离 ∈ [衰减区间] → alpha 渐变

实体销毁 → Unregister(id)
  → 归还 TMP 到池
  → 移除 DisplayEntry
```

### 3.2 配置

```csharp
public struct NameplateConfig
{
    public string DisplayName;
    public Color NameColor;
    public Sprite ClassIcon;      // null = 不显示图标
    public float VerticalOffset;  // 默认 2.5
    public float CullDistance;    // 覆盖全局值
}
```

### 3.3 模板化元素

池中存储的是轻量模板实例（非裸 TMP）：

```
NameplateTemplate
├── Image (职业图标，配置为 null 时不创建)
├── TMP_Text (名字，必须)
└── LayoutGroup (水平排列，自动布局图标和文字)
```

### 3.4 视觉规格

- 字体：现有 TMP Font Asset，屏幕等效字号约 18pt
- 描边：outlineWidth=0.15，纯黑 `#000000`
- 距离剔除：默认 50f
- 距离衰减：30f 起 alpha 降低 → 50f alpha=0
- 职业颜色：Palette 静态类定义（战士红、法师蓝、牧师白等）

### 3.5 注册策略

| 实体 | 注册铭牌 | 配置来源 |
|------|---------|---------|
| 本地玩家 | 不注册 | — |
| 其他玩家 | 注册 | 网络消息（角色名 + 职业） |
| Monster | 注册 | MonsterConfig.DisplayName |
| NPC | 注册 | 服务端 NpcSpawn.DisplayName（新增字段） |

## 四、飘字子系统

### 4.1 类型与效果

| FloatTextType | 触发条件 | 文字 | 颜色 | 动画 | 时长 |
|---|---|---|---|---|---|
| Normal | 普攻、技能 | 数值 | 白色 | 上飘 40px + 淡出 | 0.8s |
| Crit | 暴击 | 数值 | 橙黄 #FF8C00 | 弹入(1.3x) + 上飘 + 淡出 + 屏幕震动 | 1.2s |
| Heal | 治疗 | +数值 | 绿色 #00FF7F | 缓上飘 30px + 淡出 | 1.0s |
| Dodge | 闪避 | "闪避" | 白色 | 斜右上飘 + 淡出 | 0.6s |
| Block | 格挡 | "格挡" | 黄色 #FFD700 | 斜右上飘 + 淡出 | 0.6s |
| DOT | 持续伤害每跳 | 数值 | 小号白色 | 快速上飘 20px + 淡出 | 0.5s |
| SkillName | 技能释放 | 技能名 | 技能色 | 居中放大→缩小→淡出 | 1.5s |

### 4.2 多段伤害合并

```
MergeWindow = 200ms, key = (entityId, FloatTextType)

第一刀 50  → 创建飘字 "50"，记录 MergeEntry { count=1, sum=50 }
第二刀 30  → 150ms 内命中 → sum=80, count=2 → 刷新飘字为 "80"
第三刀 20  → 仍然在窗口内 → sum=100, count=3 → 刷新飘字为 "100"
窗口关闭   → 清除 MergeEntry
```

### 4.3 屏幕震动

- 暴击触发：`Camera.main.DOPunchPosition(Vector3(2,1,0), 0.15f, 5, 0.5f)`
- 防抖：100ms 内不重复触发

### 4.4 调用接口

```csharp
// 基础调用
EntityDisplayManager.Instance.ShowFloatingText(worldPos, FloatTextType.Normal, value: 100);

// 合并 + 类型自动判断
EntityDisplayManager.Instance.ShowDamageText(entityId, worldPos, damageInfo);
```

`NameplateEventBridge` 的逻辑迁移到 `EntityDisplayManager` 内部。

## 五、性能预估

| 场景 | 铭牌数 | 飘字数 | 预估帧开销 |
|------|--------|--------|-----------|
| 城镇 (30 NPC) | 30 | 0 | <0.2ms |
| 战斗 (20 实体) | 20 | 5-10 | <0.8ms |
| 团战 (50 实体) | 50 | 15-20 | <1.5ms |
| Unity 60FPS 预算 | — | — | 16ms |

- 单 Canvas 单 Draw Call（共享 Font Material）
- WorldToScreenPoint 50 次 ~0.01ms
- 仅文字变化时触发 Canvas.BuildBatch（非每帧）
- 池内 TMP 总数 = 铭牌池 N + 飘字池 M，总量可控

## 六、文件改动清单

### 新建

| 文件 | 说明 |
|------|------|
| `EntityDisplayManager.cs` | 统一管理器，铭牌+飘字 |
| `NameplateConfig.cs` | 铭牌配置结构 |
| `FloatTextConfig.cs` | 飘字类型枚举 + 配置 |
| `ColorPalette.cs` | 职业/类型颜色定义 |

### 删除

| 文件 | 原因 |
|------|------|
| `NameplateManager.cs` | 功能迁移到 EntityDisplayManager |
| `NameplateTag.cs` | 不再需要挂组件，配置走代码传参 |
| `FloatingTextPool.cs` | 功能迁移 |
| `FloatingTextConfig.cs` (旧版) | 配置融入新的 FloatTextConfig |
| `NameplateEventBridge.cs` | 逻辑迁移到 Manager 内部 |
| `NpcMirror/NpcMirrorManager.cs` | 死代码 |
| `NpcMirror/NpcMirrorComponent.cs` | 死代码 |
| `NpcMirror/NpcAnimationController.cs` | 死代码 |
| `NpcMirror/NpcMessages.cs` | 死代码，与 Server/Messages.cs 不兼容 |

### 修改

| 文件 | 改动 |
|------|------|
| `MonsterEntity.cs` | Init() 加 Register，OnDestroy 加 Unregister |
| `MonsterConfig.cs` | 无改动（DisplayName 已有） |
| `DamageEvents.cs` | 事件中补充 entityId 信息 |

## 七、不在此设计范围内的内容

- 血条（HP Bar）—— 后续独立设计
- 称号/头衔系统 —— 有需要时 NameplateTemplate 加一个可选 TMP 即可
- NPC Mirror 网络接入 —— 服务端 NPC 同步是另一个独立需求
