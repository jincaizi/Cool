# 3C Core 模块实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 3C 系统的 Core 模块（EventBus、StateCoordinator、StatePriority、DebugWindow）

**Architecture:** 基于事件驱动的架构，EventBus 作为中央事件枢纽，StateCoordinator 管理三层 FSM 的优先级仲裁。DebugWindow 提供运行时调试能力。

**Tech Stack:** C#, Unity 2022.3.25f1, UGUI

---

## 文件结构

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Core/
│   ├── Core.asmdef
│   ├── EventBus.cs                    # 事件总线核心
│   ├── Events/
│   │   ├── IEvent.cs                  # 事件接口
│   │   ├── StateEvents.cs             # 状态事件
│   │   ├── SkillEvents.cs             # 技能事件
│   │   ├── DamageEvents.cs            # 伤害事件
│   │   └── MovementEvents.cs          # 移动事件
│   ├── StatePriority.cs               # 优先级定义
│   └── StateCoordinator.cs            # 状态协调器
│
└── Debug/
    ├── Debug.asmdef
    ├── DebugWindowManager.cs          # 调试窗口管理器
    ├── StateLogger.cs                 # 状态日志
    └── ControlsPanel.cs               # 控制面板
```

---

## Task 1: 创建 Core 模块目录和 ASMDEF

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Core.asmdef`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/`

- [ ] **Step 1: 创建 Core.asmdef**

```json
{
    "name": "Hotfix.GameSystems.Sys3C.Core",
    "rootNamespace": "Hotfix.GameSystems.Sys3C.Core",
    "references": [
        "Hotfix.GameSystems.Sys3C"
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

- [ ] **Step 2: 创建 Events 子目录**

创建空目录占位文件 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/.gitkeep`

- [ ] **Step 3: 更新 Sys3C.asmdef 添加 Core 引用**

修改 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C.asmdef` 的 references 数组添加 `"Hotfix.GameSystems.Sys3C.Core"`

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Core.asmdef
git commit -m "feat(3c-core): add Core module asmdef"
```

---

## Task 2: 实现 IEvent 接口和基础类型

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/IEvent.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/Enums.cs`

- [ ] **Step 1: 创建 IEvent.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 事件接口，所有事件必须实现此接口
    /// </summary>
    public interface IEvent
    {
    }
}
```

- [ ] **Step 2: 创建 Enums.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// FSM 层类型
    /// </summary>
    public enum LayerType
    {
        Base,
        Attack,
        Hit
    }

    /// <summary>
    /// 跳跃阶段
    /// </summary>
    public enum JumpPhase
    {
        Start,
        Air,
        End
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

    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/IEvent.cs
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/Enums.cs
git commit -m "feat(3c-core): add IEvent interface and enums"
```

---

## Task 3: 实现事件类型定义

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/StateEvents.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/SkillEvents.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/MovementEvents.cs`

- [ ] **Step 1: 创建 StateEvents.cs**

```csharp
using Hotfix.GameSystems.Sys3C.FSM;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 状态变化事件
    /// </summary>
    public struct StateChangedEvent : IEvent
    {
        public LayerType Layer;
        public string PreviousState;
        public string CurrentState;

        public StateChangedEvent(LayerType layer, string previous, string current)
        {
            Layer = layer;
            PreviousState = previous;
            CurrentState = current;
        }
    }

    /// <summary>
    /// 层锁定事件
    /// </summary>
    public struct LayerLockedEvent : IEvent
    {
        public LayerType Layer;
        public bool IsLocked;

        public LayerLockedEvent(LayerType layer, bool isLocked)
        {
            Layer = layer;
            IsLocked = isLocked;
        }
    }

    /// <summary>
    /// 层解锁事件
    /// </summary>
    public struct LayerUnlockedEvent : IEvent
    {
        public LayerType Layer;

        public LayerUnlockedEvent(LayerType layer)
        {
            Layer = layer;
        }
    }
}
```

- [ ] **Step 2: 创建 SkillEvents.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 技能激活事件
    /// </summary>
    public struct SkillActivatedEvent : IEvent
    {
        public int SkillId;
        public string SkillName;

        public SkillActivatedEvent(int skillId, string skillName)
        {
            SkillId = skillId;
            SkillName = skillName;
        }
    }

    /// <summary>
    /// 技能完成事件
    /// </summary>
    public struct SkillCompletedEvent : IEvent
    {
        public int SkillId;
        public bool WasInterrupted;

        public SkillCompletedEvent(int skillId, bool wasInterrupted = false)
        {
            SkillId = skillId;
            WasInterrupted = wasInterrupted;
        }
    }

    /// <summary>
    /// 技能被打断事件
    /// </summary>
    public struct SkillInterruptedEvent : IEvent
    {
        public int SkillId;
        public InterruptionSource Source;

        public SkillInterruptedEvent(int skillId, InterruptionSource source)
        {
            SkillId = skillId;
            Source = source;
        }
    }
}
```

- [ ] **Step 3: 创建 DamageEvents.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 伤害事件
    /// </summary>
    public struct DamageEvent : IEvent
    {
        public int SourceId;
        public int TargetId;
        public float Damage;
        public bool IsCritical;

        public DamageEvent(int sourceId, int targetId, float damage, bool isCritical = false)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Damage = damage;
            IsCritical = isCritical;
        }
    }

    /// <summary>
    /// 受击事件
    /// </summary>
    public struct HitReceivedEvent : IEvent
    {
        public float KnockbackForce;
        public bool HasSuperArmor;

        public HitReceivedEvent(float knockbackForce = 0f, bool hasSuperArmor = false)
        {
            KnockbackForce = knockbackForce;
            HasSuperArmor = hasSuperArmor;
        }
    }

    /// <summary>
    /// 死亡事件
    /// </summary>
    public struct DeathEvent : IEvent
    {
        public int EntityId;
        public int KillerId;

        public DeathEvent(int entityId, int killerId = 0)
        {
            EntityId = entityId;
            KillerId = killerId;
        }
    }
}
```

- [ ] **Step 4: 创建 MovementEvents.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 跳跃事件
    /// </summary>
    public struct JumpEvent : IEvent
    {
        public JumpPhase Phase;

        public JumpEvent(JumpPhase phase)
        {
            Phase = phase;
        }

        public static JumpEvent Start => new JumpEvent(JumpPhase.Start);
        public static JumpEvent Air => new JumpEvent(JumpPhase.Air);
        public static JumpEvent End => new JumpEvent(JumpPhase.End);
    }

    /// <summary>
    /// 落地事件
    /// </summary>
    public struct LandEvent : IEvent
    {
        public float FallDistance;

        public LandEvent(float fallDistance)
        {
            FallDistance = fallDistance;
        }
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/StateEvents.cs
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/SkillEvents.cs
git add Assets/Shotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/MovementEvents.cs
git commit -m "feat(3c-core): add event type definitions"
```

---

## Task 4: 实现 EventBus

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/EventBus.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/EventBus.cs.meta` (Unity 会自动生成)

- [ ] **Step 1: 创建 EventBus.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 事件总线 - 轻量级事件发布订阅系统
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> _subscribers = new();
        private static bool _isPaused;

        /// <summary>
        /// 订阅事件（泛型版本，推荐使用）
        /// </summary>
        public static void Subscribe<T>(Action<T> callback) where T : IEvent
        {
            var type = typeof(T);
            if (!_subscribers.ContainsKey(type))
            {
                _subscribers[type] = new List<Delegate>();
            }
            _subscribers[type].Add(callback);

#if UNITY_3C_DEBUG
            Debug.Log($"[EventBus] Subscribed to {type.Name}");
#endif
        }

        /// <summary>
        /// 订阅事件（Type 版本，用于反射场景）
        /// </summary>
        public static void Subscribe(Type eventType, Action<IEvent> callback)
        {
            if (!_subscribers.ContainsKey(eventType))
            {
                _subscribers[eventType] = new List<Delegate>();
            }
            _subscribers[eventType].Add(callback);
        }

        /// <summary>
        /// 取消订阅（泛型版本）
        /// </summary>
        public static void Unsubscribe<T>(Action<T> callback) where T : IEvent
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                list.Remove(callback);
#if UNITY_3C_DEBUG
                Debug.Log($"[EventBus] Unsubscribed from {type.Name}");
#endif
            }
        }

        /// <summary>
        /// 取消订阅（Type 版本）
        /// </summary>
        public static void Unsubscribe(Type eventType, Action<IEvent> callback)
        {
            if (_subscribers.TryGetValue(eventType, out var list))
            {
                list.Remove(callback);
            }
        }

        /// <summary>
        /// 发布事件（泛型版本，推荐使用）
        /// </summary>
        public static void Emit<T>(T evt) where T : IEvent
        {
            if (_isPaused) return;

            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                // 复制列表以防止在回调中修改列表导致的问题
                var callbacks = list.ToArray();
                foreach (var callback in callbacks)
                {
                    try
                    {
                        ((Action<T>)callback)?.Invoke(evt);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[EventBus] Exception in event callback for {type.Name}: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// 发布事件（非泛型版本）
        /// </summary>
        public static void Emit(IEvent evt)
        {
            if (_isPaused) return;

            var type = evt.GetType();
            if (_subscribers.TryGetValue(type, out var list))
            {
                var callbacks = list.ToArray();
                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback?.Invoke(evt);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[EventBus] Exception in event callback for {type.Name}: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// 清空所有订阅
        /// </summary>
        public static void Clear()
        {
            _subscribers.Clear();
            Debug.Log("[EventBus] Cleared all subscribers");
        }

        /// <summary>
        /// 暂停事件分发
        /// </summary>
        public static void Pause()
        {
            _isPaused = true;
            Debug.Log("[EventBus] Paused");
        }

        /// <summary>
        /// 恢复事件分发
        /// </summary>
        public static void Resume()
        {
            _isPaused = false;
            Debug.Log("[EventBus] Resumed");
        }

        /// <summary>
        /// 获取订阅数量（用于调试）
        /// </summary>
        public static int GetSubscriberCount(Type eventType)
        {
            return _subscribers.TryGetValue(eventType, out var list) ? list.Count : 0;
        }

        /// <summary>
        /// 获取所有已订阅的事件类型（用于调试）
        /// </summary>
        public static Type[] GetSubscribedEventTypes()
        {
            var types = new Type[_subscribers.Count];
            _subscribers.Keys.CopyTo(types, 0);
            return types;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/EventBus.cs
git commit -m "feat(3c-core): add EventBus implementation"
```

---

## Task 5: 实现 StatePriority

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StatePriority.cs`

- [ ] **Step 1: 创建 StatePriority.cs**

```csharp
namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 层优先级定义
    /// </summary>
    public static class StatePriority
    {
        public const int HitLayer = 3;    // 最高
        public const int AttackLayer = 2;
        public const int BaseLayer = 1;  // 最低

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

        /// <summary>
        /// 判断 a 是否可以打断 b
        /// </summary>
        public static bool CanInterrupt(LayerType interrupter, LayerType target)
        {
            return IsHigherPriority(interrupter, target);
        }

        /// <summary>
        /// 获取打断目标层列表
        /// </summary>
        public static LayerType[] GetInterruptibleLayers(LayerType interrupter)
        {
            var result = new System.Collections.Generic.List<LayerType>();

            foreach (LayerType layer in System.Enum.GetValues(typeof(LayerType)))
            {
                if (IsHigherPriority(interrupter, layer))
                {
                    result.Add(layer);
                }
            }

            return result.ToArray();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StatePriority.cs
git commit -m "feat(3c-core): add StatePriority"
```

---

## Task 6: 实现 StateCoordinator

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs`

- [ ] **Step 1: 创建 StateCoordinator.cs**

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.FSM;

namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 状态协调器 - 管理三层 FSM 的协调
    /// </summary>
    public class StateCoordinator
    {
        private readonly FSM.BaseFSM _baseFSM;
        private readonly FSM.AttackFSM _attackFSM;
        private FSM.HitFSM _hitFSM; // 暂时为 null，等 HitFSM 实现后完善

        private LayerType _activeLayer = LayerType.Base;
        private LayerType _lockedLayer = LayerType.Base;

        public LayerType ActiveLayer => _activeLayer;
        public bool CanMove => _activeLayer == LayerType.Base || _activeLayer == LayerType.Attack;
        public bool CanAttack => _activeLayer != LayerType.Hit && _activeLayer != LayerType.Base;
        public bool HasSuperArmor => _attackFSM?.HasSuperArmor ?? false;

        public StateCoordinator(FSM.BaseFSM baseFSM, FSM.AttackFSM attackFSM)
        {
            _baseFSM = baseFSM;
            _attackFSM = attackFSM;
        }

        /// <summary>
        /// 初始化协调器
        /// </summary>
        public void Initialize(FSM.HitFSM hitFSM)
        {
            _hitFSM = hitFSM;

            // 订阅各层事件
            _baseFSM.OnStateChanged += OnBaseStateChanged;
            _attackFSM.OnAttackCompleted += OnAttackCompleted;
            _attackFSM.OnSkillCompleted += OnSkillCompleted;
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        public bool TryRequestAttack()
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (_activeLayer == LayerType.Attack) return true; // 已经在攻击

            // 锁定 Base 层
            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);

            _attackFSM.RequestNormalAttack();
            EventBus.Emit(new SkillActivatedEvent(0, "NormalAttack"));
            return true;
        }

        /// <summary>
        /// 请求技能
        /// </summary>
        public bool TryRequestSkill(int skillId, string skillName)
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (!_attackFSM.CanPlaySkill) return false;

            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);

            // 根据 skillId 触发对应技能
            if (skillName == "SkillQ")
            {
                _attackFSM.RequestSkillQ();
            }
            else if (skillName == "SkillR")
            {
                _attackFSM.RequestSkillR(_baseFSM.CurrentState == BaseState.JumpAir ||
                                         _baseFSM.CurrentState == BaseState.JumpEnd);
            }

            EventBus.Emit(new SkillActivatedEvent(skillId, skillName));
            return true;
        }

        /// <summary>
        /// 请求跳跃
        /// </summary>
        public bool TryRequestJump()
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (_activeLayer == LayerType.Attack) return false; // 攻击中不能跳跃

            EventBus.Emit(JumpEvent.Start);
            return true;
        }

        /// <summary>
        /// 处理伤害
        /// </summary>
        public void HandleDamage(DamageEvent damage)
        {
            if (HasSuperArmor) return; // 有霸体不处理

            // Hit 层打断一切
            SetActiveLayer(LayerType.Hit);
            _attackFSM.ForceIdle();

            EventBus.Emit(damage);
            EventBus.Emit(new HitReceivedEvent());

            Debug.Log($"[StateCoordinator] Damage handled: {damage.Damage}");
        }

        /// <summary>
        /// 霸体检查
        /// </summary>
        public bool HasSuperArmorAgainst(InterruptionSource source)
        {
            return HasSuperArmor;
        }

        /// <summary>
        /// 检查层是否被锁定
        /// </summary>
        public bool IsLayerLocked(LayerType layer)
        {
            return _lockedLayer == layer;
        }

        /// <summary>
        /// 获取当前状态
        /// </summary>
        public string GetCurrentState(LayerType layer)
        {
            return layer switch
            {
                LayerType.Base => _baseFSM.CurrentState.ToString(),
                LayerType.Attack => _attackFSM.CurrentState.ToString(),
                LayerType.Hit => _hitFSM?.CurrentState.ToString() ?? "None",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// 获取当前状态枚举值
        /// </summary>
        public T GetCurrentState<T>(LayerType layer) where T : Enum
        {
            if (layer == LayerType.Base)
                return (T)(object)_baseFSM.CurrentState;
            if (layer == LayerType.Attack)
                return (T)(object)_attackFSM.CurrentState;
            if (layer == LayerType.Hit && _hitFSM != null)
                return (T)(object)_hitFSM.CurrentState;

            return default;
        }

        /// <summary>
        /// 解锁层并返回 Base
        /// </summary>
        public void UnlockAndReturnToBase()
        {
            _lockedLayer = LayerType.Base;
            SetActiveLayer(LayerType.Base);
            EventBus.Emit(new LayerUnlockedEvent(LayerType.Base));
        }

        private void SetActiveLayer(LayerType layer)
        {
            if (_activeLayer != layer)
            {
                var previous = _activeLayer;
                _activeLayer = layer;
                EventBus.Emit(new StateChangedEvent(layer, previous.ToString(), layer.ToString()));
            }
        }

        private void LockLayer(LayerType layer)
        {
            if (_lockedLayer != layer)
            {
                _lockedLayer = layer;
                EventBus.Emit(new LayerLockedEvent(layer, true));
            }
        }

        private void OnBaseStateChanged(BaseState state)
        {
            EventBus.Emit(new StateChangedEvent(LayerType.Base, _baseFSM.CurrentState.ToString(), state.ToString()));

            // 跳跃结束后解锁
            if (state == BaseState.Idle && _activeLayer != LayerType.Attack)
            {
                UnlockAndReturnToBase();
            }
        }

        private void OnAttackCompleted()
        {
            if (_attackFSM.CurrentState == AttackState.Idle)
            {
                UnlockAndReturnToBase();
            }
            EventBus.Emit(new SkillCompletedEvent(0, false));
        }

        private void OnSkillCompleted()
        {
            UnlockAndReturnToBase();
            EventBus.Emit(new SkillCompletedEvent(0, false));
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs
git commit -m "feat(3c-core): add StateCoordinator"
```

---

## Task 7: 创建 Debug 模块目录和 ASMDEF

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Debug/Debug.asmdef`

- [ ] **Step 1: 创建 Debug.asmdef**

```json
{
    "name": "Hotfix.GameSystems.Sys3C.Debug",
    "rootNamespace": "Hotfix.GameSystems.Sys3C.Debug",
    "references": [
        "Hotfix.GameSystems.Sys3C",
        "Hotfix.GameSystems.Sys3C.Core"
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

- [ ] **Step 2: 更新 Sys3C.asmdef 添加 Debug 引用**

在 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C.asmdef` 的 references 添加 `"Hotfix.GameSystems.Sys3C.Debug"`

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Debug/Debug.asmdef
git commit -m "feat(3c-debug): add Debug module asmdef"
```

---

## Task 8: 实现 StateLogger

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Debug/StateLogger.cs`

- [ ] **Step 1: 创建 StateLogger.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.Debug
{
    /// <summary>
    /// 状态日志条目
    /// </summary>
    public struct StateLogEntry
    {
        public float Timestamp;
        public LogLevel Level;
        public string Message;
        public string Category;

        public StateLogEntry(string category, string message, LogLevel level = LogLevel.Info)
        {
            Timestamp = Time.time;
            Category = category;
            Message = message;
            Level = level;
        }

        public override string ToString()
        {
            return $"[{Timestamp:F2}] [{Level}] [{Category}] {Message}";
        }
    }

    /// <summary>
    /// 状态日志记录器
    /// </summary>
    public static class StateLogger
    {
        private static readonly List<StateLogEntry> _logs = new();
        private static readonly int _maxLogs = 500;
        private static StreamWriter _fileWriter;
        private static string _logFilePath;

        /// <summary>
        /// 记录状态变化
        /// </summary>
        public static void LogStateChange(LayerType layer, string from, string to)
        {
            var entry = new StateLogEntry("State", $"{layer}: {from} → {to}", LogLevel.Info);
            AddLog(entry);
        }

        /// <summary>
        /// 记录事件
        /// </summary>
        public static void LogEvent(string eventName, object data = null)
        {
            var message = data != null ? $"{eventName}: {data}" : eventName;
            var entry = new StateLogEntry("Event", message, LogLevel.Debug);
            AddLog(entry);
        }

        /// <summary>
        /// 记录信息
        /// </summary>
        public static void Log(string category, string message, LogLevel level = LogLevel.Info)
        {
            var entry = new StateLogEntry(category, message, level);
            AddLog(entry);
        }

        /// <summary>
        /// 获取最近的日志
        /// </summary>
        public static List<StateLogEntry> GetRecentLogs(int count = 100)
        {
            var start = Mathf.Max(0, _logs.Count - count);
            var result = new List<StateLogEntry>();
            for (int i = start; i < _logs.Count; i++)
            {
                result.Add(_logs[i]);
            }
            return result;
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public static void Clear()
        {
            _logs.Clear();
            Debug.Log("[StateLogger] Cleared");
        }

        /// <summary>
        /// 导出到文件
        /// </summary>
        public static void DumpToFile(string path)
        {
            try
            {
                using var writer = new StreamWriter(path);
                foreach (var log in _logs)
                {
                    writer.WriteLine(log.ToString());
                }
                Debug.Log($"[StateLogger] Dumped {_logs.Count} entries to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StateLogger] Failed to dump to file: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始文件记录
        /// </summary>
        public static void StartFileLogging(string directory = null)
        {
            if (directory == null)
            {
                directory = Application.persistentDataPath;
            }

            var fileName = $"state_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            _logFilePath = Path.Combine(directory, fileName);

            _fileWriter = new StreamWriter(_logFilePath);
            _fileWriter.WriteLine("Timestamp,Level,Category,Message");
            Debug.Log($"[StateLogger] Started file logging: {_logFilePath}");
        }

        /// <summary>
        /// 停止文件记录
        /// </summary>
        public static void StopFileLogging()
        {
            _fileWriter?.Close();
            _fileWriter = null;
            Debug.Log("[StateLogger] Stopped file logging");
        }

        private static void AddLog(StateLogEntry entry)
        {
            _logs.Add(entry);

            // 限制日志数量
            while (_logs.Count > _maxLogs)
            {
                _logs.RemoveAt(0);
            }

            // 输出到控制台
            var logMessage = entry.ToString();
            switch (entry.Level)
            {
                case LogLevel.Debug:
                    Debug.Log(logMessage);
                    break;
                case LogLevel.Info:
                    Debug.Log(logMessage);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(logMessage);
                    break;
                case LogLevel.Error:
                    Debug.LogError(logMessage);
                    break;
            }

            // 写入文件
            if (_fileWriter != null)
            {
                _fileWriter.WriteLine($"{entry.Timestamp},{entry.Level},{entry.Category},{entry.Message.Replace(",", ";")}");
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Debug/StateLogger.cs
git commit -m "feat(3c-debug): add StateLogger"
```

---

## Task 9: 实现 DebugWindowManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Debug/DebugWindowManager.cs`

- [ ] **Step 1: 创建 DebugWindowManager.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.Debug
{
    /// <summary>
    /// 调试窗口管理器 - 基于 UGUI 的运行时调试窗口
    /// </summary>
    public class DebugWindowManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _windowRoot;
        [SerializeField] private Text _baseLayerText;
        [SerializeField] private Text _attackLayerText;
        [SerializeField] private Text _hitLayerText;
        [SerializeField] private Text _eventLogText;
        [SerializeField] private ScrollRect _eventLogScrollRect;

        [Header("Buttons")]
        [SerializeField] private Button _lockBaseButton;
        [SerializeField] private Button _lockAttackButton;
        [SerializeField] private Button _forceHitButton;
        [SerializeField] private Button _clearLogButton;

        [Header("Settings")]
        [SerializeField] private int _maxLogLines = 50;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F3;

        private bool _isVisible = true;
        private StateCoordinator _coordinator;
        private readonly List<string> _logLines = new();

        private void Awake()
        {
            // 默认显示窗口
            if (_windowRoot != null)
            {
                _windowRoot.SetActive(_isVisible);
            }

            SetupButtons();
        }

        private void Update()
        {
            // 快捷键切换
            if (Input.GetKeyDown(_toggleKey))
            {
                Toggle();
            }
        }

        /// <summary>
        /// 显示/隐藏窗口
        /// </summary>
        public void Toggle()
        {
            _isVisible = !_isVisible;
            if (_windowRoot != null)
            {
                _windowRoot.SetActive(_isVisible);
            }
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize(StateCoordinator coordinator)
        {
            _coordinator = coordinator;

            // 订阅事件
            EventBus.Subscribe<Core.Events.StateChangedEvent>(OnStateChanged);
            EventBus.Subscribe<Core.Events.SkillActivatedEvent>(OnSkillActivated);
            EventBus.Subscribe<Core.Events.SkillCompletedEvent>(OnSkillCompleted);
            EventBus.Subscribe<Core.Events.DamageEvent>(OnDamage);

            UpdateDisplay();
        }

        /// <summary>
        /// 添加日志
        /// </summary>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            var timestamp = Time.time.ToString("F2");
            var line = $"[{timestamp}] {message}";
            _logLines.Add(line);

            // 限制行数
            while (_logLines.Count > _maxLogLines)
            {
                _logLines.RemoveAt(0);
            }

            UpdateLogDisplay();
        }

        /// <summary>
        /// 更新状态显示
        /// </summary>
        public void UpdateDisplay()
        {
            if (_coordinator == null) return;

            // 更新层状态
            if (_baseLayerText != null)
            {
                var baseState = _coordinator.GetCurrentState(LayerType.Base);
                var isLocked = _coordinator.IsLayerLocked(LayerType.Base);
                _baseLayerText.text = $"Base: {baseState} {(isLocked ? "[LOCKED]" : "")}";
            }

            if (_attackLayerText != null)
            {
                var attackState = _coordinator.GetCurrentState(LayerType.Attack);
                var isLocked = _coordinator.IsLayerLocked(LayerType.Attack);
                _attackLayerText.text = $"Attack: {attackState} {(isLocked ? "[LOCKED]" : "")}";
            }

            if (_hitLayerText != null)
            {
                var hitState = _coordinator.GetCurrentState(LayerType.Hit);
                _hitLayerText.text = $"Hit: {hitState}";
            }
        }

        private void SetupButtons()
        {
            if (_lockBaseButton != null)
            {
                _lockBaseButton.onClick.AddListener(() => Log("Base layer lock toggled"));
            }

            if (_lockAttackButton != null)
            {
                _lockAttackButton.onClick.AddListener(() => Log("Attack layer lock toggled"));
            }

            if (_forceHitButton != null)
            {
                _forceHitButton.onClick.AddListener(OnForceHit);
            }

            if (_clearLogButton != null)
            {
                _clearLogButton.onClick.AddListener(OnClearLog);
            }
        }

        private void OnForceHit()
        {
            Log("Force Hit triggered!", LogLevel.Warning);
            var damage = new Core.Events.DamageEvent(0, 0, 10f);
            _coordinator?.HandleDamage(damage);
        }

        private void OnClearLog()
        {
            _logLines.Clear();
            UpdateLogDisplay();
            StateLogger.Clear();
        }

        private void UpdateLogDisplay()
        {
            if (_eventLogText != null)
            {
                _eventLogText.text = string.Join("\n", _logLines);
            }

            // 滚动到底部
            if (_eventLogScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                _eventLogScrollRect.verticalNormalizedPosition = 0;
            }
        }

        private void OnStateChanged(Core.Events.StateChangedEvent evt)
        {
            Log($"State: {evt.Layer} {evt.PreviousState} → {evt.CurrentState}");
            UpdateDisplay();
        }

        private void OnSkillActivated(Core.Events.SkillActivatedEvent evt)
        {
            Log($"Skill: {evt.SkillName} activated (ID: {evt.SkillId})");
        }

        private void OnSkillCompleted(Core.Events.SkillCompletedEvent evt)
        {
            var status = evt.WasInterrupted ? "interrupted" : "completed";
            Log($"Skill {evt.SkillId} {status}");
            UpdateDisplay();
        }

        private void OnDamage(Core.Events.DamageEvent evt)
        {
            Log($"Damage: {evt.Damage} (Crit: {evt.IsCritical})", LogLevel.Warning);
        }

        private void OnDestroy()
        {
            // 取消订阅（需要保存回调引用）
            // EventBus.Unsubscribe<...>(callback);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Debug/DebugWindowManager.cs
git commit -m "feat(3c-debug): add DebugWindowManager"
```

---

## Task 10: 集成到 Sys3CEntry

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: 查看当前 Sys3CEntry.cs**

读取 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` 以确定需要修改的位置

- [ ] **Step 2: 添加必要的 using**

在文件顶部添加：
```csharp
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Debug;
```

- [ ] **Step 3: 添加新成员变量**

在类中添加：
```csharp
private DebugWindowManager _debugWindow;
private StateCoordinator _stateCoordinator;
```

- [ ] **Step 4: 在 Start 中初始化**

在 Start 方法的初始化部分添加：
```csharp
// 初始化 StateCoordinator
_stateCoordinator = new StateCoordinator(_fsmManager.BaseFSM, _fsmManager.AttackFSM);

// 初始化调试窗口
var debugWindow = FindObjectOfType<DebugWindowManager>();
if (debugWindow != null)
{
    _debugWindow = debugWindow;
    _debugWindow.Initialize(_stateCoordinator);
}
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat(3c): integrate StateCoordinator and DebugWindow"
```

---

## Task 11: 添加 HitFSM 占位符（为后续完善）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs`

- [ ] **Step 1: 创建 HitFSM.cs**

```csharp
using System;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// Hit 层状态
    /// </summary>
    public enum HitState
    {
        None,
        Hit,
        Knockback,
        Down,
        Death
    }

    /// <summary>
    /// 受击状态机 - 管理受击/击退/倒地/死亡
    /// </summary>
    public class HitFSM
    {
        private HitState _currentState;

        public HitState CurrentState => _currentState;
        public bool HasSuperArmor => _currentState == HitState.Death;

        public event Action<HitState> OnStateChanged;

        public HitFSM()
        {
            _currentState = HitState.None;
        }

        public void Update(float deltaTime)
        {
            // 根据状态处理逻辑
        }

        /// <summary>
        /// 进入受击状态
        /// </summary>
        public void EnterHit(float knockbackForce = 0f)
        {
            var target = knockbackForce > 0 ? HitState.Knockback : HitState.Hit;
            TransitionTo(target);
        }

        /// <summary>
        /// 进入死亡状态
        /// </summary>
        public void EnterDeath()
        {
            TransitionTo(HitState.Death);
        }

        /// <summary>
        /// 重置到无受击状态
        /// </summary>
        public void Reset()
        {
            TransitionTo(HitState.None);
        }

        private void TransitionTo(HitState target)
        {
            if (_currentState == target) return;

            var previous = _currentState;
            _currentState = target;
            OnStateChanged?.Invoke(target);
            UnityEngine.Debug.Log($"[HitFSM] {_currentState}");
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs
git commit -m "feat(3c-hit): add HitFSM placeholder"
```

---

## Task 12: 更新 StateCoordinator 支持 HitFSM

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs`

- [ ] **Step 1: 修改 Initialize 调用**

在 Sys3CEntry.Start 中添加 HitFSM 初始化：
```csharp
var hitFSM = new HitFSM();
_stateCoordinator.Initialize(hitFSM);
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs
git commit -m "feat(3c-core): integrate HitFSM into StateCoordinator"
```

---

## 验证清单

- [ ] EventBus.Subscribe/Unsubscribe/Emit 功能正常
- [ ] EventBus.Pause/Resume 正常工作
- [ ] StatePriority 优先级判断正确
- [ ] StateCoordinator 层锁定/解锁正常
- [ ] StateLogger 日志记录正常
- [ ] DebugWindow 窗口显示正确
- [ ] 快捷键 F3 切换窗口正常

---

**文档版本:** 1.0
**创建日期:** 2026-05-04
**状态:** 待实现