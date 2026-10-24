# 冰冻 / 灼烧状态效果实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让玩家技能可以对怪物施加冰冻（完全定身）与灼烧（按施法者攻击力的持续伤害），并用现有染色机制提供占位视觉。

**架构：** 新增纯 C# 的 `StatusEffectRegistry` 负责状态生命周期与 tick，`StatusEffectManager` 是场景中的单例驱动器；`EffectData` 增加 `FreezeEffectData` / `BurnEffectData`，由 `SkillExecutor` 按持续时间路由；怪物通过 `StatusControllerAdapter` 接受定身，通过新接口方法结算 DoT；表现层由 EventBus 解耦，使用 `BodyTintRenderer` 染色。

**技术栈：** Unity 2022.3.25f1、C#、NUnit EditMode 测试、EventBus、`BodyTintRenderer`、Unity Test Framework。

**规格：** `docs/superpowers/specs/2026-10-24-freeze-burn-status-effects-design.md`

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `Skills/Effect/StatusEffectRegistry.cs` | 状态运行时：Apply / Tick / Remove / RemoveAll，纯 C# 可单测 |
| `Skills/Effect/StatusEffectManager.cs` | MonoBehaviour 单例，每帧驱动 Registry |
| `Skills/Effect/BurnEffectData.cs` | 灼烧配置与每跳伤害计算 |
| `Skills/Effect/EffectData.cs` | `EffectType` 扩展、`OnTick` 签名、`IEffectTarget.ApplyDamageOverTime`、`FreezeEffectData`、Stun 空引用保护 |
| `Skills/Events/StatusEffectEvents.cs` | Applied / Removed 事件 |
| `Skills/Data/EffectBlock.cs` | `_applyEffects` 加 `[SerializeReference]` |
| `Skills/Runtime/SkillExecutor.cs` | 按 Duration 路由到 Manager |
| `Sys3C/Sys3CEntry.cs` | `SkillOwnerProxy` 接入真实 Stats / Status / Physics / Heal |
| `Monster/MonsterEntity.cs` | StatusController、定身门控、DoT 伤害、死亡恢复 |
| `Sys3C/Core/Events/DamageEvents.cs` | `MonsterDotDamageEvent` |
| `Nameplate/DisplayEventBridge.cs` | DoT 飘字订阅 |
| `VFX/StatusVFXSystem.cs` | 冰冻/灼烧占位染色 |
| `VFX/SkillFreezeEffector.cs` | 改走 StatusEffectManager |
| `Tests/Editor/TestHelpers.cs` | 测试替身与计数效果 |
| `Tests/Editor/StatusEffectRegistryTests.cs` | Registry 行为测试 |
| `Tests/Editor/BurnEffectDataTests.cs` | 灼烧数值测试 |
| `Tests/Editor/StatusEffectRoutingTests.cs` | 技能路由测试 |
| `Tests/Editor/MonsterEntityDotDamageTests.cs` | 怪物 DoT 测试 |
| `Tests/Editor/StatusVFXSystemTests.cs` | 染色计数测试 |

---

## 任务 1：接口与序列化基础

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Data/EffectBlock.cs:11`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Effect/EffectData.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 修改：`Assets/Scripts/Tests/Editor/TestHelpers.cs`
- 修改：`Assets/Scripts/Tests/Editor/SkillExecutorChargeTests.cs:44-57`
- 测试：`Assets/Scripts/Tests/Editor/StatusEffectSerializationTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/StatusEffectSerializationTests.cs`：

```csharp
using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusEffectSerializationTests
    {
        [Test]
        public void test_effect_block_apply_effects_supports_polymorphic_serialization()
        {
            // Arrange
            var field = typeof(EffectBlock).GetField(
                "_applyEffects", BindingFlags.NonPublic | BindingFlags.Instance);

            // Assert
            Assert.IsNotNull(field, "EffectBlock._applyEffects 字段不存在");
            Assert.IsTrue(field.IsDefined(typeof(SerializeReference), true),
                "EffectBlock._applyEffects 必须标记 [SerializeReference]，否则冰冻/灼烧无法配置在资产上");
        }

        [Test]
        public void test_stun_effect_without_status_controller_does_not_throw()
        {
            // Arrange
            var target = new TestHelpers.FakeTarget(null);
            var effect = new StunEffectData { SetDuration = 1f };

            // Act & Assert
            Assert.DoesNotThrow(() => effect.Apply(null, target));
            Assert.DoesNotThrow(() => effect.Remove(null, target));
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusEffectSerializationTests`

预期：FAIL。`[SerializeReference]` 断言失败；`StunEffectData.Apply` 在 `StatusController == null` 时抛 `NullReferenceException`。

- [ ] **步骤 3：编写实现代码**

修改 `EffectBlock.cs` 的 `_applyEffects` 字段：

```csharp
        [SerializeReference]
        [SerializeField] private EffectData[] _applyEffects;
```

修改 `EffectData.cs`：

1. 在 `EffectType` 枚举末尾追加（不要插在中间，避免改变已有枚举值）：

```csharp
        // 沉默目标(无法施法)
        [Tooltip("沉默目标(无法施法)")]
        Silence,
        // 冰冻目标(完全定身，复用眩晕逻辑)
        [Tooltip("冰冻目标(完全定身，复用眩晕逻辑)")]
        Freeze,
        // 灼烧目标(按施法者攻击力持续掉血)
        [Tooltip("灼烧目标(按施法者攻击力持续掉血)")]
        Burn
```

2. 修改 `EffectData.OnTick` 签名：

```csharp
        public virtual void OnTick(IEffectTarget caster, IEffectTarget target, int stacks) { }
```

3. 在 `IEffectTarget` 中新增：

```csharp
        void ApplyDamageOverTime(float amount);
```

4. 修改 `StunEffectData` 的 Apply / Remove：

```csharp
        public override void Apply(IEffectTarget caster, IEffectTarget target)
        {
            target.StatusController?.AddStun(_duration);
        }

        public override void Remove(IEffectTarget caster, IEffectTarget target)
        {
            target.StatusController?.RemoveStun();
        }
```

修改 `MonsterEntity.cs`，在 `IEffectTarget.Heal` 之后新增空实现（任务 9 再替换为真实逻辑）：

```csharp
        void IEffectTarget.ApplyDamageOverTime(float amount) { }
```

修改 `Sys3CEntry.cs` 的 `SkillOwnerProxy`，新增：

```csharp
        public void ApplyDamageOverTime(float amount) { }
```

修改 `TestHelpers.cs`，完整替换文件内容：

```csharp
using System.Reflection;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 测试辅助：反射设置私有字段 + IEffectTarget / IEffectStats 假实现
    /// </summary>
    public static class TestHelpers
    {
        public static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        public static object GetField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}");
            return field.GetValue(target);
        }

        public class FakeStatusController : IStatusController
        {
            public float StunDuration;
            public int AddCalls;
            public int RemoveCalls;

            public void AddStun(float duration)
            {
                StunDuration = Mathf.Max(StunDuration, duration);
                AddCalls++;
            }

            public void RemoveStun()
            {
                StunDuration = 0f;
                RemoveCalls++;
            }
        }

        public class FakeStats : IEffectStats
        {
            public float AttackPower;

            public float GetAttribute(AttributeType type)
                => type == AttributeType.AttackPower ? AttackPower : 0f;

            public float GetMaxHealth() => 1000f;

            public void AddModifier(AttributeType type, string id, float value, ModifierType modType) { }

            public void RemoveModifier(AttributeType type, string id) { }
        }

        public class FakeTarget : IEffectTarget, IDamageable
        {
            public Transform transform { get; }
            public IEffectStats Stats { get; }
            public IShieldSystem ShieldSystem => null;
            public IPhysicsSystem PhysicsSystem => null;
            public IStatusController StatusController { get; }
            public bool IsAlive { get; set; }
            public float DotDamageTotal { get; private set; }

            public FakeTarget(
                Transform t,
                IStatusController status = null,
                IEffectStats stats = null,
                bool isAlive = true)
            {
                transform = t;
                StatusController = status;
                Stats = stats;
                IsAlive = isAlive;
            }

            public void Heal(float amount) { }

            public void ApplyDamageOverTime(float amount)
            {
                DotDamageTotal += amount;
            }

            public Transform Transform => transform;

            public void TakeDamage(DamageBlock data, Vector3 hitDirection) { }

            public void TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact) { }
        }

        public class CountingEffect : EffectData
        {
            public int ApplyCount;
            public int TickCount;
            public int RemoveCount;
            public int LastStacks;

            public CountingEffect(
                float duration = 0f,
                bool isTick = false,
                float interval = 1f,
                StackingRule stackingRule = StackingRule.Refresh,
                int maxStacks = 1)
            {
                _duration = duration;
                _isTickEffect = isTick;
                _tickInterval = interval;
                _stackingRule = stackingRule;
                _maxStacks = maxStacks;
                _effectId = "counting";
            }

            public override void Apply(IEffectTarget caster, IEffectTarget target)
                => ApplyCount++;

            public override void OnTick(IEffectTarget caster, IEffectTarget target, int stacks)
            {
                TickCount++;
                LastStacks = stacks;
            }

            public override void Remove(IEffectTarget caster, IEffectTarget target)
                => RemoveCount++;
        }
    }
}
```

修改 `SkillExecutorChargeTests.cs` 的 `FakeHitTarget`，在 `Heal` 之后新增：

```csharp
            public void ApplyDamageOverTime(float amount) { }
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusEffectSerializationTests`

预期：PASS（2 个测试）。

- [ ] **步骤 5：运行全量 EditMode 测试确认没有破坏已有用例**

运行：`bash tools/unity-tests.sh EditMode`

预期：全部 PASS。若出现 `IEffectTarget` 缺少 `ApplyDamageOverTime` 的编译错误，说明还有实现类未更新；用 `Grep` 搜索 `IEffectTarget` 并补齐。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/EffectBlock.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Effect/EffectData.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/Editor/TestHelpers.cs \
        Assets/Scripts/Tests/Editor/SkillExecutorChargeTests.cs \
        Assets/Scripts/Tests/Editor/StatusEffectSerializationTests.cs \
        Assets/Scripts/Tests/Editor/StatusEffectSerializationTests.cs.meta
git commit -m "feat(status): 扩展效果接口并支持多态序列化"
```

---

## 任务 2：StatusEffectRegistry 核心

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/Events/StatusEffectEvents.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs`
- 创建：`Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `StatusEffectRegistryTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusEffectRegistryTests
    {
        private StatusEffectRegistry _registry;
        private GameObject _targetGo;
        private TestHelpers.FakeStatusController _status;
        private TestHelpers.FakeTarget _target;

        [SetUp]
        public void SetUp()
        {
            _registry = new StatusEffectRegistry();
            _targetGo = new GameObject("StatusTarget");
            _status = new TestHelpers.FakeStatusController();
            _target = new TestHelpers.FakeTarget(_targetGo.transform, _status);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_targetGo);
        }

        [Test]
        public void test_registry_apply_calls_effect_apply_once()
        {
            var effect = new TestHelpers.CountingEffect(2f);

            _registry.Apply(_target, effect, null);

            Assert.AreEqual(1, effect.ApplyCount);
        }

        [Test]
        public void test_registry_expiry_calls_remove_once()
        {
            var effect = new TestHelpers.CountingEffect(1f);

            _registry.Apply(_target, effect, null);
            _registry.Tick(0.5f);
            Assert.AreEqual(0, effect.RemoveCount);

            _registry.Tick(0.6f);
            Assert.AreEqual(1, effect.RemoveCount);
        }

        [Test]
        public void test_registry_tick_effect_fires_once_per_interval()
        {
            var effect = new TestHelpers.CountingEffect(5f, isTick: true, interval: 1f);

            _registry.Apply(_target, effect, null);
            for (int i = 0; i < 5; i++)
                _registry.Tick(1f);

            Assert.AreEqual(5, effect.TickCount);
        }

        [Test]
        public void test_registry_large_step_does_not_tick_after_expiry()
        {
            var effect = new TestHelpers.CountingEffect(1.5f, isTick: true, interval: 1f);

            _registry.Apply(_target, effect, null);
            _registry.Tick(2f);

            Assert.AreEqual(1, effect.TickCount);
        }

        [Test]
        public void test_registry_duration_zero_applies_immediately_without_registration()
        {
            var effect = new TestHelpers.CountingEffect(0f);

            _registry.Apply(_target, effect, null);
            _registry.Tick(10f);

            Assert.AreEqual(1, effect.ApplyCount);
            Assert.AreEqual(0, effect.RemoveCount);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRegistryTests`

预期：编译失败，`StatusEffectRegistry` 与 `StatusEffectEvents` 不存在。

- [ ] **步骤 3：编写实现代码**

创建 `Assets/Scripts/Hotfix/GameSystems/Skills/Events/StatusEffectEvents.cs`：

```csharp
using Hotfix.GameSystems.Skills.Effect;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    public struct StatusEffectAppliedEvent : IEvent
    {
        public int EntityId;
        public Transform Target;
        public EffectType EffectType;
        public string EffectId;
        public float Duration;
        public int Stacks;
    }

    public struct StatusEffectRemovedEvent : IEvent
    {
        public int EntityId;
        public Transform Target;
        public EffectType EffectType;
        public string EffectId;
    }
}
```

创建 `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs`：

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    public sealed class StatusEffectRegistry
    {
        private sealed class ActiveEffect
        {
            public IEffectTarget Target;
            public GameObject TargetGo;
            public IEffectTarget Caster;
            public GameObject CasterGo;
            public EffectData Effect;
            public float Remaining;
            public float TimeToNextTick;
            public int Stacks;
        }

        private readonly List<ActiveEffect> _active = new();

        public void Apply(IEffectTarget target, EffectData effect, IEffectTarget caster)
        {
            if (target == null || effect == null) return;

            GameObject targetGo = GetGameObject(target);
            if (targetGo == null) return;
            if (target is IDamageable damageable && !damageable.IsAlive) return;

            if (effect.Duration <= 0f)
            {
                effect.Apply(caster, target);
                return;
            }

            int targetId = targetGo.GetInstanceID();
            ActiveEffect existing = Find(targetId, GetKey(effect));

            if (existing != null)
            {
                Refresh(existing, effect, caster);
                return;
            }

            effect.Apply(caster, target);

            _active.Add(new ActiveEffect
            {
                Target = target,
                TargetGo = targetGo,
                Caster = caster,
                CasterGo = GetGameObject(caster),
                Effect = effect,
                Remaining = effect.Duration,
                TimeToNextTick = effect.IsTickEffect ? effect.TickInterval : 0f,
                Stacks = 1,
            });

            EventBus.Emit(new StatusEffectAppliedEvent
            {
                EntityId = targetId,
                Target = target.transform,
                EffectType = effect.Type,
                EffectId = effect.EffectId,
                Duration = effect.Duration,
                Stacks = 1,
            });
        }

        public void Tick(float deltaTime)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveEffect entry = _active[i];

                if (entry.TargetGo == null)
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (entry.CasterGo == null)
                    entry.Caster = null;

                if (IsDead(entry.Target))
                {
                    RemoveAt(i);
                    continue;
                }

                float step = Mathf.Min(deltaTime, entry.Remaining);
                entry.Remaining -= deltaTime;

                if (entry.Effect.IsTickEffect && entry.Effect.TickInterval > 0f)
                {
                    entry.TimeToNextTick -= step;
                    while (entry.TimeToNextTick <= 0f)
                    {
                        entry.Effect.OnTick(entry.Caster, entry.Target, entry.Stacks);
                        entry.TimeToNextTick += entry.Effect.TickInterval;

                        if (IsDead(entry.Target))
                            break;
                    }
                }

                if (IsDead(entry.Target) || entry.Remaining <= 0f)
                    RemoveAt(i);
            }
        }

        public void Remove(IEffectTarget target, string effectId)
        {
            GameObject targetGo = GetGameObject(target);
            if (targetGo == null) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].TargetGo == targetGo && _active[i].Effect.EffectId == effectId)
                    RemoveAt(i);
            }
        }

        public void RemoveAll(IEffectTarget target)
        {
            GameObject targetGo = GetGameObject(target);
            if (targetGo == null) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].TargetGo == targetGo)
                    RemoveAt(i);
            }
        }

        private void Refresh(ActiveEffect entry, EffectData effect, IEffectTarget caster)
        {
            entry.Remaining = effect.Duration;
            entry.TimeToNextTick = effect.IsTickEffect ? effect.TickInterval : 0f;
            entry.Caster = caster;
            entry.CasterGo = GetGameObject(caster);
        }

        private void RemoveAt(int index)
        {
            ActiveEffect entry = _active[index];

            entry.Effect.Remove(entry.Caster, entry.Target);

            EventBus.Emit(new StatusEffectRemovedEvent
            {
                EntityId = entry.TargetGo.GetInstanceID(),
                Target = entry.Target.transform,
                EffectType = entry.Effect.Type,
                EffectId = entry.Effect.EffectId,
            });

            _active.RemoveAt(index);
        }

        private ActiveEffect Find(int targetId, string key)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                ActiveEffect entry = _active[i];
                if (entry.TargetGo == null) continue;
                if (entry.TargetGo.GetInstanceID() != targetId) continue;
                if (GetKey(entry.Effect) == key) return entry;
            }
            return null;
        }

        private static string GetKey(EffectData effect)
        {
            return string.IsNullOrEmpty(effect.EffectId)
                ? RuntimeHelpers.GetHashCode(effect).ToString()
                : effect.EffectId;
        }

        private static GameObject GetGameObject(IEffectTarget target)
        {
            if (target == null) return null;
            if (target is Component component) return component.gameObject;
            Transform t = target.transform;
            return t != null ? t.gameObject : null;
        }

        private static bool IsDead(IEffectTarget target)
        {
            return target is IDamageable damageable && !damageable.IsAlive;
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRegistryTests`

预期：PASS（4 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Events/StatusEffectEvents.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Events/StatusEffectEvents.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs.meta \
        Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs \
        Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs.meta
git commit -m "feat(status): 增加状态运行时注册表"
```

---

## 任务 3：叠层、重复施加与死亡清理

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs`
- 修改：`Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `StatusEffectRegistryTests.cs` 的 `SetUp` 之前新增 4 个测试：

```csharp
        [Test]
        public void test_registry_refresh_resets_duration_without_reapplying()
        {
            var effect = new TestHelpers.CountingEffect(2f);

            _registry.Apply(_target, effect, null);
            _registry.Tick(1f);
            _registry.Apply(_target, effect, null);
            _registry.Tick(1.5f);

            Assert.AreEqual(1, effect.ApplyCount);
            Assert.AreEqual(0, effect.RemoveCount);

            _registry.Tick(0.6f);
            Assert.AreEqual(1, effect.RemoveCount);
        }

        [Test]
        public void test_registry_ignore_does_not_add_second_entry()
        {
            var effect = new TestHelpers.CountingEffect(
                2f, stackingRule: StackingRule.Ignore);

            _registry.Apply(_target, effect, null);
            _registry.Apply(_target, effect, null);
            _registry.Tick(2.1f);

            Assert.AreEqual(1, effect.ApplyCount);
            Assert.AreEqual(1, effect.RemoveCount);
        }

        [Test]
        public void test_registry_stack_increments_up_to_max()
        {
            var effect = new TestHelpers.CountingEffect(
                3f, isTick: true, interval: 1f,
                stackingRule: StackingRule.Stack, maxStacks: 2);

            _registry.Apply(_target, effect, null);
            _registry.Apply(_target, effect, null);
            _registry.Apply(_target, effect, null);
            _registry.Tick(1f);

            Assert.AreEqual(1, effect.TickCount);
            Assert.AreEqual(2, effect.LastStacks);
        }

        [Test]
        public void test_registry_removes_dead_target()
        {
            var effect = new TestHelpers.CountingEffect(10f, isTick: true, interval: 1f);

            _registry.Apply(_target, effect, null);
            _target.IsAlive = false;
            _registry.Tick(1f);

            Assert.AreEqual(0, effect.TickCount);
            Assert.AreEqual(1, effect.RemoveCount);
        }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRegistryTests`

预期：FAIL。`Ignore` 会重复注册，`Stack` 不会累加层数，死亡目标仍会 tick。

- [ ] **步骤 3：编写实现代码**

修改 `StatusEffectRegistry.cs` 的 `Apply` 中 `existing != null` 分支：

```csharp
            if (existing != null)
            {
                switch (effect.StackingRule)
                {
                    case StackingRule.Ignore:
                        return;
                    case StackingRule.Stack:
                        existing.Stacks = Mathf.Min(
                            existing.Stacks + 1, Mathf.Max(1, effect.MaxStacks));
                        Refresh(existing, effect, caster);
                        return;
                    default:
                        Refresh(existing, effect, caster);
                        return;
                }
            }
```

`Tick` 中已有的死亡检查与 `RemoveAt` 保持任务 2 的实现（任务 2 已包含 `IsDead` 分支）。确认 `Refresh` 会重置 `TimeToNextTick`。

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRegistryTests`

预期：PASS（8 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs \
        Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs
git commit -m "feat(status): 支持叠层与死亡清理"
```

---

## 任务 4：多控制效果的眩晕恢复

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs`
- 修改：`Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs`

- [ ] **步骤 1：编写失败的测试**

新增：

```csharp
        [Test]
        public void test_registry_short_stun_does_not_clear_longer_stun()
        {
            var shortStun = new StunEffectData
            {
                SetDuration = 1f,
                SetEffectId = "short_stun"
            };
            var longStun = new StunEffectData
            {
                SetDuration = 3f,
                SetEffectId = "long_stun"
            };

            _registry.Apply(_target, shortStun, null);
            _registry.Apply(_target, longStun, null);
            _registry.Tick(1.1f);

            Assert.Greater(_status.StunDuration, 1.5f);
            Assert.AreEqual(1, _status.RemoveCalls);

            _registry.Tick(2f);
            Assert.AreEqual(0f, _status.StunDuration, 0.001f);
        }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRegistryTests`

预期：FAIL。短眩晕到期调用 `RemoveStun` 后，`StunDuration` 变为 0，长眩晕被错误解除。

- [ ] **步骤 3：编写实现代码**

修改 `StatusEffectRegistry.cs` 的 `RemoveAt`：

```csharp
        private void RemoveAt(int index)
        {
            ActiveEffect entry = _active[index];

            entry.Effect.Remove(entry.Caster, entry.Target);

            if (entry.Effect is StunEffectData)
                RestoreLongestStun(entry);

            EventBus.Emit(new StatusEffectRemovedEvent
            {
                EntityId = entry.TargetGo.GetInstanceID(),
                Target = entry.Target.transform,
                EffectType = entry.Effect.Type,
                EffectId = entry.Effect.EffectId,
            });

            _active.RemoveAt(index);
        }

        private void RestoreLongestStun(ActiveEffect removed)
        {
            float longest = 0f;
            for (int i = 0; i < _active.Count; i++)
            {
                ActiveEffect other = _active[i];
                if (other == removed || other.TargetGo != removed.TargetGo) continue;
                if (!(other.Effect is StunEffectData)) continue;
                longest = Mathf.Max(longest, other.Remaining);
            }

            if (longest > 0f)
                removed.Target.StatusController?.AddStun(longest);
        }
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRegistryTests`

预期：PASS（9 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectRegistry.cs \
        Assets/Scripts/Tests/Editor/StatusEffectRegistryTests.cs
git commit -m "fix(status): 多来源眩晕到期时保留最长剩余时间"
```

---

## 任务 5：BurnEffectData

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/Effect/BurnEffectData.cs`
- 创建：`Assets/Scripts/Tests/Editor/BurnEffectDataTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `BurnEffectDataTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class BurnEffectDataTests
    {
        [Test]
        public void test_burn_defaults()
        {
            var burn = new BurnEffectData();

            Assert.AreEqual(EffectType.Burn, burn.Type);
            Assert.AreEqual("burn", burn.EffectId);
            Assert.IsTrue(burn.IsTickEffect);
            Assert.AreEqual(1f, burn.TickInterval, 0.001f);
            Assert.AreEqual(0.2f, burn.AttackPowerRatio, 0.001f);
            Assert.AreEqual(5f, burn.BaseDamage, 0.001f);
        }

        [Test]
        public void test_burn_tick_damage_uses_caster_attack_power()
        {
            var burn = new BurnEffectData();
            burn.Initialize(0.2f, 5f, 3f, 1f);
            var target = new TestHelpers.FakeTarget(null);
            var caster = new TestHelpers.FakeTarget(
                null, stats: new TestHelpers.FakeStats { AttackPower = 100f });

            burn.OnTick(caster, target, 1);

            Assert.AreEqual(20f, target.DotDamageTotal, 0.001f);
        }

        [Test]
        public void test_burn_tick_damage_falls_back_without_caster_stats()
        {
            var burn = new BurnEffectData();
            burn.Initialize(0.2f, 5f, 3f, 1f);
            var target = new TestHelpers.FakeTarget(null);

            burn.OnTick(null, target, 1);

            Assert.AreEqual(5f, target.DotDamageTotal, 0.001f);
        }

        [Test]
        public void test_burn_tick_damage_multiplies_by_stacks()
        {
            var burn = new BurnEffectData();
            burn.Initialize(0.2f, 5f, 3f, 1f);
            var target = new TestHelpers.FakeTarget(null);
            var caster = new TestHelpers.FakeTarget(
                null, stats: new TestHelpers.FakeStats { AttackPower = 100f });

            burn.OnTick(caster, target, 3);

            Assert.AreEqual(60f, target.DotDamageTotal, 0.001f);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode BurnEffectDataTests`

预期：编译失败，`BurnEffectData` 不存在。

- [ ] **步骤 3：编写实现代码**

创建 `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/BurnEffectData.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    [System.Serializable]
    public class BurnEffectData : EffectData
    {
        [Header("=== Burn Properties ===")]
        [Tooltip("每跳伤害 = 施法者攻击力 × 此比例")]
        [SerializeField] private float _attackPowerRatio = 0.2f;
        public float AttackPowerRatio => _attackPowerRatio;

        [Tooltip("施法者属性不可用时的每跳兜底伤害")]
        [SerializeField] private float _baseDamage = 5f;
        public float BaseDamage => _baseDamage;

        public BurnEffectData()
        {
            _type = EffectType.Burn;
            _effectId = "burn";
            _isTickEffect = true;
            _tickInterval = 1f;
        }

        public void Initialize(
            float attackPowerRatio,
            float baseDamage,
            float duration,
            float tickInterval)
        {
            _attackPowerRatio = attackPowerRatio;
            _baseDamage = baseDamage;
            _duration = duration;
            _tickInterval = tickInterval;
            _isTickEffect = true;
        }

        public override string GetDisplayName()
            => $"Burn: {_attackPowerRatio * 100f}% AP / {_tickInterval}s";

        public override void OnTick(IEffectTarget caster, IEffectTarget target, int stacks)
        {
            float attackPower = caster?.Stats?.GetAttribute(AttributeType.AttackPower) ?? 0f;
            float damage = attackPower > 0f
                ? attackPower * _attackPowerRatio
                : _baseDamage;

            damage *= Mathf.Max(1, stacks);
            target.ApplyDamageOverTime(damage);
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode BurnEffectDataTests`

预期：PASS（4 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Effect/BurnEffectData.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Effect/BurnEffectData.cs.meta \
        Assets/Scripts/Tests/Editor/BurnEffectDataTests.cs \
        Assets/Scripts/Tests/Editor/BurnEffectDataTests.cs.meta
git commit -m "feat(status): 增加灼烧效果与持续伤害计算"
```

---

## 任务 6：FreezeEffectData

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Effect/EffectData.cs`
- 修改：`Assets/Scripts/Tests/Editor/StatusEffectSerializationTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `StatusEffectSerializationTests.cs` 中新增：

```csharp
        [Test]
        public void test_freeze_effect_has_freeze_type_and_default_id()
        {
            var effect = new FreezeEffectData();

            Assert.AreEqual(EffectType.Freeze, effect.Type);
            Assert.AreEqual("freeze", effect.EffectId);
        }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusEffectSerializationTests`

预期：编译失败，`FreezeEffectData` 不存在。

- [ ] **步骤 3：编写实现代码**

在 `EffectData.cs` 的 `StunEffectData` 之后新增：

```csharp
    [System.Serializable]
    public class FreezeEffectData : StunEffectData
    {
        public FreezeEffectData()
        {
            _type = EffectType.Freeze;
            _effectId = "freeze";
        }

        public override string GetDisplayName() => $"Freeze: {_duration}s";
    }
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusEffectSerializationTests`

预期：PASS（3 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Effect/EffectData.cs \
        Assets/Scripts/Tests/Editor/StatusEffectSerializationTests.cs
git commit -m "feat(status): 增加冰冻效果类型"
```

---

## 任务 7：StatusEffectManager

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectManager.cs`
- 创建：`Assets/Scripts/Tests/Editor/StatusEffectManagerTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `StatusEffectManagerTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusEffectManagerTests
    {
        private GameObject _managerGo;
        private GameObject _targetGo;

        [SetUp]
        public void SetUp()
        {
            _managerGo = new GameObject("StatusManager");
            _targetGo = new GameObject("StatusTarget");
        }

        [TearDown]
        public void TearDown()
        {
            if (_managerGo != null) Object.DestroyImmediate(_managerGo);
            if (_targetGo != null) Object.DestroyImmediate(_targetGo);
        }

        [Test]
        public void test_manager_apply_delegates_to_registry()
        {
            var manager = _managerGo.AddComponent<StatusEffectManager>();
            var target = new TestHelpers.FakeTarget(_targetGo.transform);
            var effect = new TestHelpers.CountingEffect(1f);

            manager.Apply(target, effect, null);

            Assert.AreEqual(1, effect.ApplyCount);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusEffectManagerTests`

预期：编译失败，`StatusEffectManager` 不存在。

- [ ] **步骤 3：编写实现代码**

创建 `Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectManager.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    public class StatusEffectManager : MonoBehaviour
    {
        public static StatusEffectManager Instance { get; private set; }

        private readonly StatusEffectRegistry _registry = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            _registry.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Apply(IEffectTarget target, EffectData effect, IEffectTarget caster)
            => _registry.Apply(target, effect, caster);

        public void Remove(IEffectTarget target, string effectId)
            => _registry.Remove(target, effectId);

        public void RemoveAll(IEffectTarget target)
            => _registry.RemoveAll(target);
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusEffectManagerTests`

预期：PASS（1 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectManager.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Effect/StatusEffectManager.cs.meta \
        Assets/Scripts/Tests/Editor/StatusEffectManagerTests.cs \
        Assets/Scripts/Tests/Editor/StatusEffectManagerTests.cs.meta
git commit -m "feat(status): 增加全局状态管理器"
```

---

## 任务 8：技能路由与施法者接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs:728-734`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:157` 与 `949-963`
- 测试：`Assets/Scripts/Tests/Editor/StatusEffectRoutingTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `StatusEffectRoutingTests.cs`：

```csharp
using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusEffectRoutingTests
    {
        private static void ApplyEffects(SkillExecutor executor, IEffectTarget target)
        {
            var method = typeof(SkillExecutor).GetMethod(
                "ApplyEffects", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "SkillExecutor.ApplyEffects 不存在");
            method.Invoke(executor, new object[] { target });
        }

        [Test]
        public void test_duration_effect_is_skipped_when_manager_missing()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            var block = new EffectBlock();
            var effect = new TestHelpers.CountingEffect(duration: 5f);
            TestHelpers.SetField(block, "_applyEffects", new EffectData[] { effect });
            TestHelpers.SetField(data, "_effect", block);

            var owner = new TestHelpers.FakeTarget(null);
            var executor = new SkillExecutor(owner, data);

            ApplyEffects(executor, owner);

            Assert.AreEqual(0, effect.ApplyCount);

            Object.DestroyImmediate(data);
        }

        [Test]
        public void test_instant_effect_is_applied_without_manager()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            var block = new EffectBlock();
            var effect = new TestHelpers.CountingEffect(duration: 0f);
            TestHelpers.SetField(block, "_applyEffects", new EffectData[] { effect });
            TestHelpers.SetField(data, "_effect", block);

            var owner = new TestHelpers.FakeTarget(null);
            var executor = new SkillExecutor(owner, data);

            ApplyEffects(executor, owner);

            Assert.AreEqual(1, effect.ApplyCount);

            Object.DestroyImmediate(data);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRoutingTests`

预期：FAIL。当前 `ApplyEffects` 对所有效果直接调用 `Apply`，带持续时间的 `CountingEffect` 也会被立即应用（`ApplyCount == 1`）。

- [ ] **步骤 3：编写实现代码**

修改 `SkillExecutor.cs`，在类中新增字段：

```csharp
        private static bool _warnedMissingStatusManager;
```

替换 `ApplyEffects`：

```csharp
        private void ApplyEffects(IEffectTarget target)
        {
            var effect = GetEffect();
            if (effect?.ApplyEffects == null) return;

            foreach (var effectData in effect.ApplyEffects)
            {
                if (effectData == null) continue;

                if (effectData.Duration > 0f)
                {
                    if (StatusEffectManager.Instance != null)
                    {
                        StatusEffectManager.Instance.Apply(target, effectData, _owner);
                    }
                    else if (!_warnedMissingStatusManager)
                    {
                        _warnedMissingStatusManager = true;
                        Debug.LogError(
                            "[SkillExecutor] StatusEffectManager 未挂载，带持续时间的效果已跳过");
                    }
                }
                else
                {
                    effectData.Apply(_owner, target);
                }
            }
        }
```

修改 `Sys3CEntry.cs` 第 157 行附近：

```csharp
            _skillCoordinator = new SkillCoordinator(new SkillOwnerProxy(
                transform,
                _cc.StatsAdapter,
                _cc.StatusControllerAdapter,
                _cc.PhysicsSystemAdapter,
                _cc.Heal));
```

替换文件末尾的 `SkillOwnerProxy`：

```csharp
    internal sealed class SkillOwnerProxy : IEffectTarget
    {
        private readonly Transform _transform;
        private readonly IEffectStats _stats;
        private readonly IStatusController _statusController;
        private readonly IPhysicsSystem _physicsSystem;
        private readonly System.Action<float> _heal;

        public Transform transform => _transform;
        public IEffectStats Stats => _stats;
        public IShieldSystem ShieldSystem => null;
        public IPhysicsSystem PhysicsSystem => _physicsSystem;
        public IStatusController StatusController => _statusController;

        public void Heal(float amount) => _heal?.Invoke(amount);

        public void ApplyDamageOverTime(float amount) { }

        public SkillOwnerProxy(
            Transform t,
            IEffectStats stats,
            IStatusController statusController,
            IPhysicsSystem physicsSystem,
            System.Action<float> heal)
        {
            _transform = t;
            _stats = stats;
            _statusController = statusController;
            _physicsSystem = physicsSystem;
            _heal = heal;
        }
    }
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusEffectRoutingTests`

预期：PASS（2 个测试）。

- [ ] **步骤 5：运行全量 EditMode 测试**

运行：`bash tools/unity-tests.sh EditMode`

预期：全部 PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/Editor/StatusEffectRoutingTests.cs \
        Assets/Scripts/Tests/Editor/StatusEffectRoutingTests.cs.meta
git commit -m "feat(status): 技能效果路由到状态管理器并接通施法者属性"
```

---

## 任务 9：怪物定身与 DoT 接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`
- 测试：`Assets/Scripts/Tests/Editor/MonsterEntityDotDamageTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `MonsterEntityDotDamageTests.cs`：

```csharp
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MonsterEntityDotDamageTests
    {
        private GameObject _go;
        private MonsterEntity _entity;
        private MonsterConfig _config;
        private MonsterStats _stats;
        private MonsterDotDamageEvent _lastEvent;
        private bool _received;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("DotTarget");
            _entity = _go.AddComponent<MonsterEntity>();

            _config = ScriptableObject.CreateInstance<MonsterConfig>();
            _config.MaxHP = 100f;
            _config.AttackPower = 10f;
            _config.Defense = 0f;
            _config.MoveSpeed = 1f;

            _stats = new MonsterStats(_config);
            TestHelpers.SetField(_entity, "_stats", _stats);

            _received = false;
            EventBus.Subscribe<MonsterDotDamageEvent>(OnDot);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<MonsterDotDamageEvent>(OnDot);
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_config);
        }

        private void OnDot(MonsterDotDamageEvent e)
        {
            _received = true;
            _lastEvent = e;
        }

        [Test]
        public void test_monster_apply_dot_damage_reduces_hp_and_emits_event()
        {
            ((IEffectTarget)_entity).ApplyDamageOverTime(10f);

            Assert.AreEqual(90f, _stats.HP, 0.001f);
            Assert.IsTrue(_received);
            Assert.AreEqual(10, _lastEvent.Damage);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode MonsterEntityDotDamageTests`

预期：FAIL。任务 1 的 `ApplyDamageOverTime` 是空实现，HP 保持 100、事件未触发。

- [ ] **步骤 3：编写实现代码**

修改 `DamageEvents.cs`，在 `MonsterTakeDamageEvent` 之后新增：

```csharp
    /// <summary>
    /// 持续伤害事件（灼烧等）。与 MonsterTakeDamageEvent 分离，
    /// 只用于飘字，不触发顿帧/震屏/命中粒子。
    /// </summary>
    public struct MonsterDotDamageEvent : IEvent
    {
        public int EntityId;
        public Vector3 HitPosition;
        public int Damage;

        public MonsterDotDamageEvent(int entityId, Vector3 hitPosition, int damage)
        {
            EntityId = entityId;
            HitPosition = hitPosition;
            Damage = damage;
        }
    }
```

修改 `MonsterEntity.cs`，把任务 1 的空实现替换为：

```csharp
        void IEffectTarget.ApplyDamageOverTime(float amount)
        {
            if (_stats == null || _stats.IsDead || amount <= 0f) return;

            _stats.ApplyDamage(amount);          // 纯扣血，绕过防御

            EventBus.Emit(new MonsterDotDamageEvent(
                GetInstanceID(),
                transform.position + Vector3.up * 1.2f,
                Mathf.CeilToInt(amount)));
        }
```

修改 `MonsterEntity.cs`：

1. 在文件顶部 using 区域新增：

```csharp
using Hotfix.GameSystems.Sys3C.Character;
```

2. 在字段区新增：

```csharp
        private readonly StatusControllerAdapter _statusController = new();
        private bool _wasStunned;
```

3. 替换 `IEffectTarget.StatusController`：

```csharp
        IStatusController IEffectTarget.StatusController => _statusController;
```

4. 替换 `Update`：

```csharp
        private void Update()
        {
            if (_stats == null || _stats.IsDead || _brain == null) return;

            bool stunned = _statusController.IsStunned;
            if (stunned != _wasStunned)
            {
                if (Animator != null)
                    Animator.speed = stunned ? 0f : 1f;

                if (stunned)
                    _movement?.Stop();

                _wasStunned = stunned;
            }

            if (!stunned)
                _brain.Update(Time.deltaTime);
        }
```

5. 修改 `OnKnockback`：

```csharp
        private void OnKnockback(KnockbackEvent e)
        {
            if (_stats == null || _stats.IsDead) return;
            if (_statusController.IsStunned) return;
            _movement.ApplyKnockback(e.Direction, e.Force);
        }
```

6. 修改 `IDamageable.TakeDamage` 中的受击反应调用：

```csharp
            if (!_statusController.IsStunned)
                _brain.OnDamageReceived(result, hitDirection);
```

7. 修改 `HandleDeath` 开头：

```csharp
        private void HandleDeath()
        {
            if (Animator != null) Animator.speed = 1f;
            _wasStunned = false;

            if (_movement != null)
            {
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode MonsterEntityDotDamageTests`

预期：PASS（1 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs \
        Assets/Scripts/Tests/Editor/MonsterEntityDotDamageTests.cs \
        Assets/Scripts/Tests/Editor/MonsterEntityDotDamageTests.cs.meta
git commit -m "feat(status): 怪物支持定身与持续伤害"
```

---

## 任务 10：DoT 飘字与占位视觉

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/SkillFreezeEffector.cs`
- 测试：`Assets/Scripts/Tests/Editor/StatusVFXSystemTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `StatusVFXSystemTests.cs`：

```csharp
using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusVFXSystemTests
    {
        private GameObject _targetGo;
        private GameObject _systemGo;
        private Material _material;
        private StatusVFXSystem _system;

        [SetUp]
        public void SetUp()
        {
            _targetGo = new GameObject("TintTarget");
            var renderer = _targetGo.AddComponent<MeshRenderer>();
            _material = new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = _material;

            _systemGo = new GameObject("StatusVFX");
            _system = _systemGo.AddComponent<StatusVFXSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_systemGo);
            Object.DestroyImmediate(_targetGo);
            Object.DestroyImmediate(_material);
        }

        [Test]
        public void test_status_vfx_adds_and_removes_freeze_tint()
        {
            var applied = typeof(StatusVFXSystem).GetMethod(
                "OnApplied", BindingFlags.NonPublic | BindingFlags.Instance);
            var removed = typeof(StatusVFXSystem).GetMethod(
                "OnRemoved", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(applied);
            Assert.IsNotNull(removed);

            applied.Invoke(_system, new object[]
            {
                new StatusEffectAppliedEvent
                {
                    EntityId = _targetGo.GetInstanceID(),
                    Target = _targetGo.transform,
                    EffectType = EffectType.Freeze,
                    EffectId = "freeze",
                    Duration = 2f,
                    Stacks = 1,
                }
            });

            var tints = (IDictionary)TestHelpers.GetField(_system, "_tints");
            Assert.AreEqual(1, tints.Count);

            removed.Invoke(_system, new object[]
            {
                new StatusEffectRemovedEvent
                {
                    EntityId = _targetGo.GetInstanceID(),
                    Target = _targetGo.transform,
                    EffectType = EffectType.Freeze,
                    EffectId = "freeze",
                }
            });

            Assert.AreEqual(0, tints.Count);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode StatusVFXSystemTests`

预期：编译失败，`StatusVFXSystem` 不存在。

- [ ] **步骤 3：编写实现代码**

修改 `DisplayEventBridge.cs`：

在 `Enable` 中新增：

```csharp
            EventBus.Subscribe<MonsterDotDamageEvent>(OnMonsterDotDamaged);
```

在 `Disable` 中新增：

```csharp
            EventBus.Unsubscribe<MonsterDotDamageEvent>(OnMonsterDotDamaged);
```

新增方法：

```csharp
        private void OnMonsterDotDamaged(MonsterDotDamageEvent e)
        {
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _damageSettings, e.Damage);
        }
```

创建 `Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs`：

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class StatusVFXSystem : MonoBehaviour
    {
        private sealed class TargetTint
        {
            public Transform Target;
            public BodyTintRenderer Tint;
            public int FreezeCount;
            public int BurnCount;
            public float Phase;
        }

        private static readonly Color FreezeColor = new Color(0.55f, 0.75f, 1f);
        private static readonly Color BurnColor = new Color(1f, 0.35f, 0.1f);

        private readonly Dictionary<int, TargetTint> _tints = new();
        private readonly List<int> _dead = new();

        private void OnEnable()
        {
            EventBus.Subscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Subscribe<StatusEffectRemovedEvent>(OnRemoved);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);

            foreach (var state in _tints.Values)
                state.Tint.Clear();

            _tints.Clear();
        }

        private void OnApplied(StatusEffectAppliedEvent e)
        {
            if (e.Target == null) return;
            if (e.EffectType != EffectType.Freeze && e.EffectType != EffectType.Burn) return;

            if (!_tints.TryGetValue(e.EntityId, out TargetTint state))
            {
                state = new TargetTint
                {
                    Target = e.Target,
                    Tint = new BodyTintRenderer(e.Target.GetComponentsInChildren<Renderer>()),
                };
                _tints.Add(e.EntityId, state);
            }

            if (e.EffectType == EffectType.Freeze)
                state.FreezeCount++;
            else
                state.BurnCount++;
        }

        private void OnRemoved(StatusEffectRemovedEvent e)
        {
            if (e.EffectType != EffectType.Freeze && e.EffectType != EffectType.Burn) return;
            if (!_tints.TryGetValue(e.EntityId, out TargetTint state)) return;

            if (e.EffectType == EffectType.Freeze)
                state.FreezeCount = Mathf.Max(0, state.FreezeCount - 1);
            else
                state.BurnCount = Mathf.Max(0, state.BurnCount - 1);

            if (state.FreezeCount == 0 && state.BurnCount == 0)
            {
                state.Tint.Clear();
                _tints.Remove(e.EntityId);
            }
        }

        private void Update()
        {
            if (_tints.Count == 0) return;

            float dt = Time.deltaTime;
            _dead.Clear();

            foreach (var pair in _tints)
            {
                TargetTint state = pair.Value;

                if (state.Target == null)
                {
                    state.Tint.Clear();
                    _dead.Add(pair.Key);
                    continue;
                }

                if (state.FreezeCount > 0)
                {
                    state.Tint.Apply(FreezeColor, 0.5f, FreezeColor, 0f);
                }
                else if (state.BurnCount > 0)
                {
                    state.Phase += dt * Mathf.PI * 4f;
                    float amount = 0.12f + 0.18f * (0.5f + 0.5f * Mathf.Sin(state.Phase));
                    state.Tint.Apply(BurnColor, amount, BurnColor, 0f);
                }
                else
                {
                    state.Tint.Clear();
                }
            }

            for (int i = 0; i < _dead.Count; i++)
                _tints.Remove(_dead[i]);
        }
    }
}
```

修改 `SkillFreezeEffector.cs` 的 `OnHitTarget`：

```csharp
        private static bool _warnedMissingManager;

        private void OnHitTarget(SkillHitTargetEvent e)
        {
            if (!e.IsFullCharge || !WatchesSkill(e.SkillId)) return;

            if (StatusEffectManager.Instance == null)
            {
                if (!_warnedMissingManager)
                {
                    _warnedMissingManager = true;
                    Debug.LogError("[SkillFreezeEffector] StatusEffectManager 未挂载，冰冻已跳过");
                }
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(e.HitPosition, 2f, _buffer);
            for (int i = 0; i < count; i++)
            {
                if (_buffer[i].TryGetComponent(out IEffectTarget target))
                {
                    var freezeEffect = new FreezeEffectData
                    {
                        SetDuration = _freezeDuration,
                        SetCanBeCleanse = false
                    };
                    StatusEffectManager.Instance.Apply(target, freezeEffect, null);
                    break;
                }
            }
        }
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode StatusVFXSystemTests`

预期：PASS（1 个测试）。

- [ ] **步骤 5：运行全量 EditMode 测试**

运行：`bash tools/unity-tests.sh EditMode`

预期：全部 PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs \
        Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs \
        Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/VFX/SkillFreezeEffector.cs \
        Assets/Scripts/Tests/Editor/StatusVFXSystemTests.cs \
        Assets/Scripts/Tests/Editor/StatusVFXSystemTests.cs.meta
git commit -m "feat(status): 增加冰冻灼烧占位视觉与 DoT 飘字"
```

---

## 任务 11：场景接线、序列化迁移与验收

**文件：**
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 修改：`Assets/Monstor/TurtleShellConfig.asset`

- [ ] **步骤 1：清理无效的 TurtleShell 效果条目**

在 Unity 编辑器中：

1. 选中 `Assets/Monstor/TurtleShellConfig.asset`。
2. 在 Inspector 找到 `Effect` → `Apply Effects`。
3. 将数组 Size 设为 0（该条目原本就是基类空效果，从未生效）。

- [ ] **步骤 2：给 DemoDay 挂管理器组件**

在 Unity 编辑器中：

1. 打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`。
2. 选中挂有 `HitFeedbackSystem` 的 GameObject。
3. Add Component → `StatusEffectManager`。
4. 在同一个 GameObject 上 Add Component → `StatusVFXSystem`。
5. 保存场景（Ctrl+S）。

> 两个组件必须在同一个根 GameObject 上：`StatusEffectManager.Awake` 会对该对象调用 `DontDestroyOnLoad`，把同对象的 `StatusVFXSystem` 一起保留。

- [ ] **步骤 3：运行全量 EditMode 测试**

运行：`bash tools/unity-tests.sh EditMode`

预期：全部 PASS。

- [ ] **步骤 4：PlayMode 手动验收 —— 冰冻**

1. 打开 DemoDay，Enter Play Mode。
2. 找到玩家已装备的蓄力技能（R）。
3. 对一只怪物蓄力到满并命中。
4. 预期：
   - 怪物在 2 秒内保持蓝白染色；
   - NavMeshAgent 停止移动，怪物不攻击；
   - 2 秒后恢复移动与攻击；
   - Console 无空引用错误。
5. 冰冻期间击杀怪物：死亡动画正常播放，不卡住。

- [ ] **步骤 5：PlayMode 手动验收 —— 灼烧**

1. 在 Unity 编辑器中选中一个玩家技能资产（例如 `Assets/PreRes/SkillsCfg/Instant_SkillQ.asset`）。
2. 在 `Effect` → `Apply Effects` 中点击 `+`，选择 `BurnEffectData`。
3. 设置 `Duration = 3`、`Tick Interval = 1`、`Attack Power Ratio = 0.2`、`Effect Id = burn`。
4. Enter Play Mode，用该技能命中怪物。
5. 预期：
   - 怪物橙红染色并脉冲约 3 秒；
   - 每秒跳出一次红色伤害数字；
   - 3 秒内恰好 3 次跳伤；
   - 没有顿帧与屏幕震动。
6. 验收完成后，按需保留或移除该技能上的 `BurnEffectData`。

- [ ] **步骤 6：Commit**

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity \
        Assets/Monstor/TurtleShellConfig.asset
git commit -m "chore(status): 挂载状态管理器并清理无效效果配置"
```

---

## 最终验证清单

- [ ] `bash tools/unity-tests.sh EditMode` 全部 PASS。
- [ ] `git status --short` 只包含本计划涉及的文件。
- [ ] 冰冻 2s 内怪物无移动/攻击，蓝白染色，2s 后恢复。
- [ ] 灼烧 3s / 1s / 0.2 × AttackPower 恰好 3 跳，飘字与橙红脉冲可见。
- [ ] 灼烧不触发顿帧、震屏、命中粒子。
- [ ] 冻结期间死亡动画正常。
- [ ] Console 无 `NullReferenceException` 与 Missing Script。

## 已知限制（与规格一致）

- 火球等投射物不触发效果。
- 状态不同步到远端客户端。
- 怪物 `Stats` / `ShieldSystem` / `PhysicsSystem` 仍为 null，Buff/Shield/Knockback 不作用于怪物。
- 染色只对拥有 `_Color` 属性的材质生效。
- 默认 `EffectId` 相同的同类效果会互相刷新。
