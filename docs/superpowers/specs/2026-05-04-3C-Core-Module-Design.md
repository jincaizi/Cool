# 3C 系统 Core 模块设计方案

> **状态:** 待实现
> **日期:** 2026-05-04
> **版本:** 1.0

---

## 1. 概述

本设计补充 3C 系统重构设计文档中缺失的 Core 模块：
- EventBus（事件总线）
- StateCoordinator（状态协调器）
- StatePriority（优先级定义）
- DebugWindow（运行时调试窗口）

这些模块构成系统的核心基础设施，为各层 FSM 提供通信和协调能力。

---

## 2. 架构概览

```
┌─────────────────────────────────────────────────────────────────┐
│                      StateCoordinator                            │
│                 (层优先级仲裁 + 事件分发)                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐      │
│   │   BaseFSM    │    │  AttackFSM   │    │   HitFSM     │      │
│   └──────┬───────┘    └──────┬───────┘    └──────┬───────┘      │
│          │                   │                   │               │
│          └───────────────────┼───────────────────┘               │
│                              │                                   ��
│                      ┌───────┴───────┐                          │
│                      │   EventBus    │                          │
│                      │  (事件发布订阅) │                          │
│                      └───────────────┘                          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. EventBus 模块

### 3.1 设计目标

- 轻量级事件总线，解耦各系统
- 支持类型安全的事件发布/订阅
- 兼容同步和异步事件处理

### 3.2 接口定义

```csharp
namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 事件接口
    /// </summary>
    public interface IEvent { }

    /// <summary>
    /// 事件监听器
    /// </summary>
    public interface IEventListener
    {
        Type[] GetEventTypes();
        void OnEvent(IEvent evt);
    }

    /// <summary>
    /// 事件总线
    /// </summary>
    public static class EventBus
    {
        // 订阅
        public static void Subscribe<T>(Action<T> callback) where T : IEvent;
        public static void Subscribe(Type eventType, Action<IEvent> callback);

        // 取消订阅
        public static void Unsubscribe<T>(Action<T> callback) where T : IEvent;
        public static void Unsubscribe(Type eventType, Action<IEvent> callback);

        // 发布
        public static void Emit<T>(T evt) where T : IEvent;

        // 管理
        public static void Clear();
        public static void Pause();
        public static void Resume();
    }
}
```

### 3.3 事件类型定义

```csharp
// 状态变化事件
public struct StateChangedEvent : IEvent
{
    public LayerType Layer;
    public string PreviousState;
    public string CurrentState;
}

// 技能事件
public struct SkillActivatedEvent : IEvent
{
    public int SkillId;
    public string SkillName;
}

public struct SkillCompletedEvent : IEvent
{
    public int SkillId;
    public bool WasInterrupted;
}

public struct SkillInterruptedEvent : IEvent
{
    public int SkillId;
    public InterruptionSource Source;
}

// 伤害事件
public struct DamageEvent : IEvent
{
    public int SourceId;
    public int TargetId;
    public float Damage;
    public bool IsCritical;
}

public struct HitReceivedEvent : IEvent
{
    public float KnockbackForce;
    public bool HasSuperArmor;
}

// 移动事件
public struct JumpEvent : IEvent
{
    public JumpPhase Phase;  // Start, Air, End
}

public struct LandEvent : IEvent
{
    public float FallDistance;
}

// 层锁定事件
public struct LayerLockedEvent : IEvent
{
    public LayerType Layer;
    public bool IsLocked;
}

public enum LayerType { Base, Attack, Hit }
public enum JumpPhase { Start, Air, End }
public enum InterruptionSource { Damage, Stun, Knockback, Skill }
```

### 3.4 使用示例

```csharp
// 订阅事件
EventBus.Subscribe<SkillCompletedEvent>(OnSkillCompleted);

// 发布事件
EventBus.Emit(new SkillCompletedEvent { SkillId = 1001, WasInterrupted = false });

// 取消订阅
EventBus.Unsubscribe<SkillCompletedEvent>(OnSkillCompleted);
```

---

## 4. StateCoordinator 模块

### 4.1 设计目标

- 管理三层 FSM 的激活状态
- 处理层间优先级仲裁
- 提供统一的输入/事件分发接口

### 4.2 接口定义

```csharp
namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 状态协调器 - 管理三层 FSM 的协调
    /// </summary>
    public class StateCoordinator
    {
        /// <summary>
        /// 当前激活的层
        /// </summary>
        public LayerType ActiveLayer { get; }

        /// <summary>
        /// 是否允许基础移动
        /// </summary>
        public bool CanMove { get; }

        /// <summary>
        /// 是否允许攻击
        /// </summary>
        public bool CanAttack { get; }

        /// <summary>
        /// 是否处于霸体状态
        /// </summary>
        public bool HasSuperArmor { get; }

        // 初始化
        public void Initialize(BaseFSM baseFSM, AttackFSM attackFSM, HitFSM hitFSM);

        // 请求处理
        public bool TryRequestAttack(int skillId);
        public bool TryRequestJump();
        public bool TryRequestMove(Vector2 input);

        // 伤害处理
        public void HandleDamage(DamageEvent damage);
        public bool HasSuperArmorAgainst(InterruptionSource source);

        // 层状态查询
        public bool IsLayerLocked(LayerType layer);
        public string GetCurrentState(LayerType layer);
    }
}
```

### 4.3 优先级规则

| 优先级 | 层 | 说明 |
|--------|-----|------|
| P1 | Hit | 最高，受击/死亡可打断一切 |
| P2 | Attack | 攻击/技能，可锁定 Base 层 |
| P3 | Base | 基础移动，最低优先级 |

**打断规则：**
- Hit 层激活 → Attack 和 Base 立即退出
- Attack 层激活 → Base 层被锁定（动画保留）
- 高优先级请求 → 检查当前状态霸体标记

---

## 5. StatePriority 模块

### 5.1 设计目标

- 定义层间优先级常量
- 提供打断条件矩阵

### 5.2 定义

```csharp
namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 层优先级定义
    /// </summary>
    public static class StatePriority
    {
        public const int HitLayer = 3;   // 最高
        public const int AttackLayer = 2;
        public const int BaseLayer = 1; // 最低

        /// <summary>
        /// 获取优先级数值
        /// </summary>
        public static int GetPriority(LayerType layer)
        {
            return layer switch
            {
                LayerType.Hit => HitLayer,
                LayerType.Attack => AttackLayer,
                LayerType.Base => BaseLayer,
                _ => 0
            };
        }

        /// <summary>
        /// 比较优先级
        /// </summary>
        public static bool IsHigherPriority(LayerType a, LayerType b)
        {
            return GetPriority(a) > GetPriority(b);
        }
    }

    /// <summary>
    /// 打断源
    /// </summary>
    public enum InterruptionSource
    {
        None,
        Damage,
        Stun,
        Knockback,
        Skill,
        Movement
    }
}
```

---

## 6. DebugWindow 模块

### 6.1 设计目标

- 运行时显示各层 FSM 状态
- 记录事件日志
- 提供手动测试工具

### 6.2 UI 布局

```
┌─────────────────────────────────────────────────────────────────┐
│                    3C System Debug                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─ BaseLayer ─────────────────────────────────────────────┐   │
│  │ State: [Move]  │ Speed: 5.2  │ Grounded: true          │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─ AttackLayer ──────────────────────────────────────────┐   │
│  │ State: [Attack1] │ Combo: 1 │ SkillState: Execution     │   │
│  │ Cooldown: 0.3s   │ SuperArmor: false                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─ HitLayer ──────────────────────────────────────────────┐   │
│  │ State: [None]   │ Invincible: false                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─ Event Log ─────────────────────────────────────────────┐  │
│  │ [0.00] SkillActivated: SkillQ                          │  │
│  │ [0.12] StateChanged: Base.Idle → Base.Move              │  │
│  │ [0.45] SkillCompleted: SkillQ                          │  │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  [Lock Base] [Lock Attack] [Force Hit] [Clear All]             │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 6.3 接口定义

```csharp
namespace Hotfix.GameSystems.Sys3C.Debug
{
    /// <summary>
    /// 调试窗口管理器
    /// </summary>
    public class DebugWindowManager
    {
        /// <summary>
        /// 显示/隐藏窗口
        /// </summary>
        public void Toggle();

        /// <summary>
        /// 添加日志条目
        /// </summary>
        public void Log(string message, LogLevel level = LogLevel.Info);

        /// <summary>
        /// 更新状态显示
        /// </summary>
        public void UpdateStateDisplay(StateCoordinator coordinator);
    }

    public enum LogLevel { Debug, Info, Warning, Error }

    /// <summary>
    /// 状态日志记录器
    /// </summary>
    public static class StateLogger
    {
        public static void LogStateChange(LayerType layer, string from, string to);
        public static void LogEvent(string eventName, object data);
        public static List<StateLogEntry> GetRecentLogs(int count = 100);
        public static void DumpToFile(string path);
    }
}
```

### 6.4 快捷键

| 按键 | 功能 |
|------|------|
| F3 | 切换调试窗口 |
| F4 | 锁定/解锁 Base 层 |
| F5 | 锁定/解锁 Attack 层 |
| F6 | 强制触发 Hit |

---

## 7. 文件结构

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Core/
│   ├── EventBus.cs
│   ├── Events/
│   │   ├── IEvent.cs
│   │   ├── StateEvents.cs
│   │   ├── SkillEvents.cs
│   │   ├── DamageEvents.cs
│   │   └── MovementEvents.cs
│   ├── StateCoordinator.cs
│   └── StatePriority.cs
│
└── Debug/
    ├── DebugWindowManager.cs
    ├── StateLogger.cs
    └── ControlsPanel.cs
```

---

## 8. 实现计划

### Phase 1: EventBus
1. 创建 Core 目录结构
2. 实现 IEvent 接口和事件基类
3. 实现 EventBus 静态类
4. 定义所有事件类型
5. 单元测试

### Phase 2: StatePriority
1. 定义优先级常量
2. 实现辅助方法

### Phase 3: StateCoordinator
1. 实现协调器类
2. 集成各层 FSM
3. 处理层间通信

### Phase 4: DebugWindow
1. 创建 UI 布局
2. 实现状态显示
3. 实现事件日志
4. 添加快捷键支持

---

**文档版本:** 1.0
**创建日期:** 2026-05-04
**状态:** 待实现