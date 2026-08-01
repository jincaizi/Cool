# SkillR 旋转技能（Spin Skill）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把技能R从"蓄力释放"改为"持续旋转"技能：瞬发起转、min~max 窗口内再按R取消、可减速移动、tick 多段全额伤害（单目标上限）。

**Architecture:** 新建 `SpinSkillData` 专用类型 + `SkillSubState.Spinning` 状态。`SkillStateMachine` 驱动 Spinning 分支（tick 调度/自动完成/取消窗口），`SkillExecutor` 对 spin 走独立 tick 伤害路径（`SpinHitTracker` 计数），`SkillCoordinator` 拦截同键取消（永不入缓冲）。动画/Animator 链路零改动，VFX 复用现有蓄力事件。

**Tech Stack:** Unity 2022.3 LTS、C#、NUnit (EditMode Tests via GameSys.EditorTests)、Unity MCP

**依据:** `docs/superpowers/specs/2026-08-02-skillr-spin-design.md`

---

## 测试运行方式（本计划所有测试步骤）

Unity MCP `tests-run`：`testMode=EditMode`，`testClass=GameSys.EditorTests.<类名>`。
代码改动后先 `assets-refresh`（MCP，等待编译），再用 `console-get-logs`（MCP，filter=Error）确认无编译错误。

---

### Task 1: 枚举 + `SpinSkillData` 数据类（TDD）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SpinSkillData.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs:18`（加 Spinning 枚举）
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillType.cs:12`（加 Spin 枚举）
- Test: `Assets/Scripts/Tests/Editor/SpinSkillDataTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SpinSkillDataTests
    {
        private static void SetField(object target, string name, object value)
        {
            typeof(SpinSkillData).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);
        }

        private static void CallOnValidate(SpinSkillData data)
        {
            typeof(SpinSkillData).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(data, null);
        }

        private static SpinSkillData CreateData()
        {
            return ScriptableObject.CreateInstance<SpinSkillData>();
        }

        [Test]
        public void OnValidate_SetsSkillTypeToSpin()
        {
            var data = CreateData();
            CallOnValidate(data);
            Assert.AreEqual(Definition.SkillType.Spin, data.SkillType);
        }

        [Test]
        public void OnValidate_ClampsTickIntervalToMinimum()
        {
            var data = CreateData();
            SetField(data, "_tickInterval", 0f);
            CallOnValidate(data);
            Assert.AreEqual(0.01f, data.TickInterval, 0.0001f);
        }

        [Test]
        public void OnValidate_ClampsMaxDurationToMinDuration()
        {
            var data = CreateData();
            SetField(data, "_minDuration", 3f);
            SetField(data, "_maxDuration", 1f);
            CallOnValidate(data);
            Assert.AreEqual(3f, data.MaxDuration, 0.0001f);
        }

        [Test]
        public void OnValidate_ClampsMoveSpeedMultiplierToUnitRange()
        {
            var data = CreateData();
            SetField(data, "_moveSpeedMultiplier", 1.5f);
            CallOnValidate(data);
            Assert.AreEqual(1f, data.MoveSpeedMultiplier, 0.0001f);
        }

        [Test]
        public void IsInCancelWindow_Boundaries()
        {
            var data = CreateData();
            SetField(data, "_minDuration", 1f);
            SetField(data, "_maxDuration", 5f);

            Assert.IsFalse(data.IsInCancelWindow(0.999f), "min 之前不可取消");
            Assert.IsTrue(data.IsInCancelWindow(1f), "elapsed == min 可取消");
            Assert.IsTrue(data.IsInCancelWindow(4.999f), "max 之前可取消");
            Assert.IsFalse(data.IsInCancelWindow(5f), "elapsed == max 不可取消（已自动结束）");
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SpinSkillDataTests`)
Expected: 编译失败（`SpinSkillData` 不存在）

- [ ] **Step 3: 修改枚举**

`SkillState.cs` 在 `Charging,` 后插入：

```csharp
        Charging,       // 蓄力中（按压蓄力，松发）
        Spinning,       // 旋转中（持续技能：可移动，窗口内再按取消）
```

`SkillType.cs` 在 `Item` 后追加（**必须加在末尾**，保持既有序列化数值不变）：

```csharp
        Item        // 物品技能
        ,Spin       // 旋转技能 (SpinSkillData)
```

- [ ] **Step 4: 实现 `SpinSkillData`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "SpinSkill", menuName = "Game/Skills/Spin Skill")]
    public class SpinSkillData : SkillData
    {
        [Header("=== Spin Duration ===")]
        [Tooltip("最低持续时长(秒)。该时段内再按技能键无效")]
        [SerializeField] private float _minDuration = 1f;
        public float MinDuration => _minDuration;

        [Tooltip("最大持续时长(秒)。到点自动结束")]
        [SerializeField] private float _maxDuration = 5f;
        public float MaxDuration => _maxDuration;

        [Header("=== Damage Ticks ===")]
        [Tooltip("伤害结算间隔(秒)。第一个tick在起手动画结束后一个间隔")]
        [SerializeField] private float _tickInterval = 0.2f;
        public float TickInterval => _tickInterval;

        [Tooltip("单目标最大命中次数(<=0 = 无上限)")]
        [SerializeField] private int _maxHitsPerTarget = 5;
        public int MaxHitsPerTarget => _maxHitsPerTarget;

        [Header("=== Movement ===")]
        [Tooltip("旋转期间移动速度倍率(0-1)")]
        [SerializeField] private float _moveSpeedMultiplier = 0.5f;
        public float MoveSpeedMultiplier => _moveSpeedMultiplier;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        /// <summary>
        /// 取消窗口：elapsed 在 [MinDuration, MaxDuration) 内可取消
        /// </summary>
        public bool IsInCancelWindow(float elapsed)
        {
            return elapsed >= _minDuration && elapsed < _maxDuration;
        }

        private void OnValidate()
        {
            _skillType = Definition.SkillType.Spin;
            _tickInterval = Mathf.Max(0.01f, _tickInterval);
            _maxDuration = Mathf.Max(_minDuration, _maxDuration);
            _moveSpeedMultiplier = Mathf.Clamp01(_moveSpeedMultiplier);

            if (_castClip != null && _maxDuration <= _castClip.length)
            {
                Debug.LogWarning($"[SpinSkillData] {name}: MaxDuration({_maxDuration}) <= 起手动画时长({_castClip.length})，持续期间不会有任何伤害tick");
            }
        }
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: MCP `assets-refresh`，然后 `tests-run` (EditMode, testClass=`GameSys.EditorTests.SpinSkillDataTests`)
Expected: 6 passed / 0 failed

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/SpinSkillData.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillType.cs \
        Assets/Scripts/Tests/Editor/SpinSkillDataTests.cs
git commit -m "feat: add SpinSkillData with min/max duration, tick, max-hits config"
```

---

### Task 2: `SpinHitTracker` 命中计数器 + 测试辅助类（TDD）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SpinHitTracker.cs`
- Create: `Assets/Scripts/Tests/Editor/TestHelpers.cs`
- Test: `Assets/Scripts/Tests/Editor/SpinHitTrackerTests.cs`

- [ ] **Step 1: 写失败测试**

`SpinHitTrackerTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class SpinHitTrackerTests
    {
        [Test]
        public void TryRecordHit_CountsPerTargetUpToCap()
        {
            var tracker = new SpinHitTracker(2);

            Assert.IsTrue(tracker.TryRecordHit(100), "第一次命中允许");
            Assert.IsTrue(tracker.TryRecordHit(100), "第二次命中允许");
            Assert.IsFalse(tracker.TryRecordHit(100), "第三次命中被上限拒绝");
            Assert.IsTrue(tracker.TryRecordHit(200), "其他目标独立计数");
        }

        [Test]
        public void TryRecordHit_ZeroCap_MeansUnlimited()
        {
            var tracker = new SpinHitTracker(0);
            for (int i = 0; i < 100; i++)
                Assert.IsTrue(tracker.TryRecordHit(100), "上限<=0 时永不拒绝");
        }

        [Test]
        public void TryRecordHit_NegativeCap_MeansUnlimited()
        {
            var tracker = new SpinHitTracker(-1);
            Assert.IsTrue(tracker.TryRecordHit(100));
            Assert.IsTrue(tracker.TryRecordHit(100));
        }

        [Test]
        public void GetHitCount_TracksPerTarget()
        {
            var tracker = new SpinHitTracker(5);
            tracker.TryRecordHit(100);
            tracker.TryRecordHit(100);
            tracker.TryRecordHit(200);

            Assert.AreEqual(2, tracker.GetHitCount(100));
            Assert.AreEqual(1, tracker.GetHitCount(200));
            Assert.AreEqual(0, tracker.GetHitCount(300));
        }

        [Test]
        public void Clear_ResetsAllCounts()
        {
            var tracker = new SpinHitTracker(1);
            tracker.TryRecordHit(100);
            Assert.IsFalse(tracker.TryRecordHit(100));

            tracker.Clear();

            Assert.IsTrue(tracker.TryRecordHit(100), "Clear 后重新计数");
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SpinHitTrackerTests`)
Expected: 编译失败（`SpinHitTracker` 不存在）

- [ ] **Step 3: 实现 `SpinHitTracker`**

```csharp
using System.Collections.Generic;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 旋转技能单目标命中计数器 - 每个目标独立计数，达到上限后拒绝再次命中。
    /// 计数贯穿整个施放过程（目标离开再回来继续累计）。
    /// </summary>
    public class SpinHitTracker
    {
        private readonly int _maxHitsPerTarget;
        private readonly Dictionary<int, int> _hitCounts = new Dictionary<int, int>();

        public SpinHitTracker(int maxHitsPerTarget)
        {
            _maxHitsPerTarget = maxHitsPerTarget;
        }

        /// <summary>
        /// 尝试记录一次命中。上限 &lt;= 0 表示不设上限。
        /// </summary>
        public bool TryRecordHit(int instanceId)
        {
            _hitCounts.TryGetValue(instanceId, out int count);
            if (_maxHitsPerTarget > 0 && count >= _maxHitsPerTarget)
                return false;
            _hitCounts[instanceId] = count + 1;
            return true;
        }

        public int GetHitCount(int instanceId)
        {
            return _hitCounts.TryGetValue(instanceId, out int count) ? count : 0;
        }

        public void Clear()
        {
            _hitCounts.Clear();
        }
    }
}
```

- [ ] **Step 4: 创建测试辅助类 `TestHelpers.cs`**（Task 4/5 复用）

```csharp
using System.Reflection;
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 测试辅助：反射设置私有字段 + IEffectTarget 假实现
    /// </summary>
    public static class TestHelpers
    {
        public static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        public class FakeTarget : IEffectTarget
        {
            public Transform transform { get; }

            public FakeTarget(Transform t) { transform = t; }

            public IEffectStats Stats => null;
            public IShieldSystem ShieldSystem => null;
            public IPhysicsSystem PhysicsSystem => null;
            public IStatusController StatusController => null;
            public void Heal(float amount) { }
        }
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: MCP `assets-refresh`，然后 `tests-run` (EditMode, testClass=`GameSys.EditorTests.SpinHitTrackerTests`)
Expected: 5 passed / 0 failed

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SpinHitTracker.cs \
        Assets/Scripts/Tests/Editor/SpinHitTrackerTests.cs \
        Assets/Scripts/Tests/Editor/TestHelpers.cs
git commit -m "feat: add SpinHitTracker per-target hit cap counter"
```

---

### Task 3: `SkillStateMachine` Spinning 分支（TDD，假时间注入）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs`
- Test: `Assets/Scripts/Tests/Editor/SkillStateMachineSpinTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillStateMachineSpinTests
    {
        /// <summary>
        /// 假时间状态机：override GetCurrentTime() 注入可控时钟
        /// </summary>
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        private readonly System.Collections.Generic.List<int> _ticks = new System.Collections.Generic.List<int>();
        private int _completedCount;

        private SpinSkillData CreateSpinData(float min, float max, float tick, AnimationClip startClip = null)
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_minDuration", min);
            TestHelpers.SetField(data, "_maxDuration", max);
            TestHelpers.SetField(data, "_tickInterval", tick);
            TestHelpers.SetField(data, "_castClip", startClip);
            return data;
        }

        private static AnimationClip CreateClip(float length)
        {
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x",
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(length, length)));
            return clip;
        }

        private FakeTimeMachine CreateMachine(SpinSkillData data)
        {
            var machine = new FakeTimeMachine(data);
            machine.OnHitboxFrame += i => _ticks.Add(i);
            machine.OnSkillCompleted += () => _completedCount++;
            return machine;
        }

        [TearDown]
        public void TearDown()
        {
            _ticks.Clear();
            _completedCount = 0;
        }

        [Test]
        public void TryStart_SpinSkill_TransitionsToSpinning()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            Assert.IsTrue(machine.TryStart());
            Assert.AreEqual(SkillSubState.Spinning, machine.CurrentState);
        }

        [Test]
        public void Update_TicksAtFixedInterval()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 0.19f;
            machine.Update(0.01f);
            Assert.AreEqual(0, _ticks.Count, "第一个tick前不触发");

            machine.Now = 0.2f;
            machine.Update(0.01f);
            machine.Now = 0.4f;
            machine.Update(0.01f);
            machine.Now = 0.61f;
            machine.Update(0.01f);

            Assert.AreEqual(new[] { 0, 1, 2 }, _ticks.ToArray(), "tick 按 tickInterval 间隔触发且序号递增");
        }

        [Test]
        public void Update_FirstTickDelayedByCastClipLength()
        {
            var clip = CreateClip(1f);
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f, clip));
            machine.TryStart();

            machine.Now = 1.0f;
            machine.Update(0.01f);
            Assert.AreEqual(0, _ticks.Count, "起手动画结束时刻不触发");

            machine.Now = 1.2f;
            machine.Update(0.01f);
            Assert.AreEqual(new[] { 0 }, _ticks.ToArray(), "第一个tick在 startClip.length + tickInterval");
        }

        [Test]
        public void Update_ReachingMaxDuration_AutoCompletes()
        {
            var machine = CreateMachine(CreateSpinData(1f, 1f, 0.2f));
            machine.TryStart();

            machine.Now = 0.99f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Spinning, machine.CurrentState);

            machine.Now = 1.0f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Completed, machine.CurrentState);
            Assert.AreEqual(1, _completedCount, "自动完成只触发一次 OnSkillCompleted");
        }

        [Test]
        public void Cancel_BeforeMinWindow_Rejected()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 0.5f;
            machine.Update(0.01f);

            Assert.IsFalse(machine.Cancel(), "min 窗口前不可取消");
            Assert.AreEqual(SkillSubState.Spinning, machine.CurrentState);
            Assert.AreEqual(0, _completedCount);
        }

        [Test]
        public void Cancel_InsideWindow_Completes()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 1.2f;
            machine.Update(0.01f);

            Assert.IsTrue(machine.Cancel());
            Assert.AreEqual(SkillSubState.Completed, machine.CurrentState);
            Assert.AreEqual(1, _completedCount, "取消走正常完成语义");
        }

        [Test]
        public void Cancel_AfterCompletion_IsNoOp()
        {
            var machine = CreateMachine(CreateSpinData(1f, 1f, 0.2f));
            machine.TryStart();

            machine.Now = 1f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Completed, machine.CurrentState);

            Assert.IsFalse(machine.Cancel(), "已完成状态下取消无效（幂等）");
            Assert.AreEqual(1, _completedCount, "不重复触发完成事件");
        }

        [Test]
        public void CanCancel_ReflectsCancelWindow()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 0.5f;
            machine.Update(0.01f);
            Assert.IsFalse(machine.CanCancel());

            machine.Now = 1.2f;
            machine.Update(0.01f);
            Assert.IsTrue(machine.CanCancel());
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillStateMachineSpinTests`)
Expected: 编译失败（`SkillStateMachine.Cancel`/`CanCancel` 不存在、`Spinning` 不存在）

- [ ] **Step 3: 修改 `SkillStateMachine.cs`**

a) 类字段区（`_isChanneled`/`_channeledData` 之后）加：

```csharp
        // Cached type checks
        private readonly bool _isCharged;
        private readonly bool _isChanneled;
        private readonly bool _isSpin;
        private readonly ChargedSkillData _chargedData;
        private readonly ChanneledSkillData _channeledData;
        private readonly SpinSkillData _spinData;
        private float _nextTickTime;
```

b) 构造函数缓存（`_isChanneled = _channeledData != null;` 之后）加：

```csharp
            _spinData = data as SpinSkillData;
            _isSpin = _spinData != null;
```

c) `TryStart()` 中 `else if (_isChanneled)` 分支之后加：

```csharp
            else if (_isSpin)
            {
                // 旋转技能：瞬发，无 Casting 阶段
                TransitionTo(SkillSubState.Spinning);
            }
```

d) `Update()` 的 switch 加 case（`case SkillSubState.Charging: UpdateCharging(); break;` 之后）：

```csharp
                case SkillSubState.Spinning: UpdateSpinning(); break;
```

e) `UpdateCharging()` 之后新增：

```csharp
        private void UpdateSpinning()
        {
            if (_spinData == null) { Complete(); return; }

            float startOffset = _spinData.CastClip != null ? _spinData.CastClip.length : 0f;
            if (_nextTickTime < 0f)
                _nextTickTime = startOffset + _spinData.TickInterval;

            // 每帧最多触发一个tick（大卡顿后按帧追tick，避免一帧内爆发多次伤害）
            if (_elapsedTime >= _nextTickTime)
            {
                _onHitboxFrame?.Invoke(_currentTick);
                _onHitConfirm?.Invoke();
                _currentTick++;
                _nextTickTime += _spinData.TickInterval;
            }

            if (_elapsedTime >= _spinData.MaxDuration)
                Complete();
        }

        /// <summary>
        /// 旋转技能：窗口内主动取消（正常完成语义，冷却不受影响）
        /// </summary>
        public bool Cancel()
        {
            if (_currentState != SkillSubState.Spinning || _spinData == null)
                return false;
            if (!_spinData.IsInCancelWindow(_elapsedTime))
                return false;
            TransitionTo(SkillSubState.Completed);
            _onSkillCompleted?.Invoke();
            return true;
        }

        /// <summary>
        /// 旋转技能：当前是否在可取消窗口内
        /// </summary>
        public bool CanCancel()
        {
            return _currentState == SkillSubState.Spinning
                && _spinData != null
                && _spinData.IsInCancelWindow(_elapsedTime);
        }
```

f) `TransitionTo` 中加 Spinning 状态重置（`_elapsedTime = 0f;` 之后）：

```csharp
            if (newState == SkillSubState.Spinning)
            {
                _currentTick = 0;
                _nextTickTime = -1f;
            }
```

g) `GetCurrentTime` 改可覆写（测试缝）：

```csharp
        protected virtual float GetCurrentTime() => UnityEngine.Time.time;
```

- [ ] **Step 4: 运行测试确认通过**

Run: MCP `assets-refresh`，然后 `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillStateMachineSpinTests`)
Expected: 8 passed / 0 failed

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs \
        Assets/Scripts/Tests/Editor/SkillStateMachineSpinTests.cs
git commit -m "feat: add Spinning state with tick scheduling, auto-complete, cancel window"
```

---

### Task 4: `SkillExecutor` 旋转 tick 伤害路径 + 撤销 `_hitThisSwing` hack

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`
- Test: `Assets/Scripts/Tests/Editor/SkillExecutorSpinTests.cs`

- [ ] **Step 1: 写失败测试（薄执行器：CanCancel/Cancel 转发）**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorSpinTests
    {
        private GameObject _ownerGo;

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null)
                Object.DestroyImmediate(_ownerGo);
        }

        private SkillExecutor CreateSpinExecutor(float min, float max)
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_minDuration", min);
            TestHelpers.SetField(data, "_maxDuration", max);
            TestHelpers.SetField(data, "_tickInterval", 0.2f);

            _ownerGo = new GameObject("SpinOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            executor.TryStart();
            return executor;
        }

        [Test]
        public void CancelWindow_ZeroMin_AllowsImmediateCancel()
        {
            var executor = CreateSpinExecutor(0f, 5f);
            Assert.AreEqual(SkillSubState.Spinning, executor.CurrentSubState);

            Assert.IsTrue(executor.CanCancel(), "min=0 时按下即进入取消窗口");
            Assert.IsTrue(executor.Cancel());
            Assert.IsFalse(executor.IsActive, "取消后技能不再激活");
        }

        [Test]
        public void CancelWindow_OneMin_RejectsImmediateCancel()
        {
            var executor = CreateSpinExecutor(1f, 5f);
            Assert.IsFalse(executor.CanCancel(), "min=1 时刚按下不可取消");
            Assert.IsFalse(executor.Cancel());
            Assert.IsTrue(executor.IsActive, "取消被拒绝时技能保持激活");
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorSpinTests`)
Expected: 编译失败（`SkillExecutor.CanCancel`/`Cancel` 不存在）

- [ ] **Step 3: 撤销工作区未提交的 `_hitThisSwing` hack**

当前工作区在 `SkillExecutor.cs` 有未提交改动（每个目标每cast最多一次伤害）。逐行恢复为 HEAD 版本：

a) 删除字段 `private readonly System.Collections.Generic.HashSet<int> _hitThisSwing = new System.Collections.Generic.HashSet<int>();`
b) `if (count >= consecutiveRequired && _hitThisSwing.Add(id))` 恢复为 `if (count >= consecutiveRequired)`
c) `OnSkillComplete` 中删除 `_hitThisSwing.Clear();`
d) `OnSkillInterrupt` 中删除 `_hitThisSwing.Clear();`

- [ ] **Step 4: 实现旋转 tick 路径**

a) 字段区（`_lastDamageBlock` 之后）加：

```csharp
        private readonly SpinSkillData _spinData;
        private SpinHitTracker _spinHitTracker;
```

b) 构造函数（`_stateMachine = new SkillStateMachine(data);` 之前）加：

```csharp
            _spinData = data as SpinSkillData;
```

c) `OnHitboxTriggered` 开头加旋转分支：

```csharp
        private void OnHitboxTriggered(int frameIndex)
        {
            var targets = DetectTargets();

            if (_spinData != null)
            {
                HandleSpinTick(targets, frameIndex);
                return;
            }
            // 以下为现有非旋转逻辑（保持不变）……
```

d) `OnHitboxTriggered` 之后新增：

```csharp
        private void HandleSpinTick(List<IEffectTarget> targets, int tickIndex)
        {
            if (_spinHitTracker == null)
                _spinHitTracker = new SpinHitTracker(_spinData.MaxHitsPerTarget);

            var damagedTargets = _cachedDamagedTargets;
            damagedTargets.Clear();
            foreach (var target in targets)
            {
                int id = target.transform.GetInstanceID();
                if (!_spinHitTracker.TryRecordHit(id)) continue;
                ApplyDamage(target, tickIndex);
                ApplyEffects(target);
                OnTargetHit?.Invoke(target);
                damagedTargets.Add(target);
            }

            OnHitboxFrame?.Invoke(tickIndex);

            if (damagedTargets.Count > 0)
            {
                var hitPos = damagedTargets[0].transform.position;
                foreach (var t in damagedTargets)
                {
                    EventBus.Emit(new SkillHitTargetEvent
                    {
                        SkillId = _skillData.SkillId,
                        CasterId = _owner.transform.GetInstanceID(),
                        HitPosition = hitPos,
                        IsFullCharge = _wasFullCharge
                    });
                }
            }

            // 旋转tick进度事件（VFX 复用蓄力事件，Progress 供剑光/拖尾强度）
            EventBus.Emit(new SkillChargeTickEvent
            {
                SkillId = _skillData.SkillId,
                Progress = Mathf.Clamp01(_stateMachine.ElapsedTime / _spinData.MaxDuration)
            });
        }
```

e) `OnStateChanged` 开头加旋转开始事件：

```csharp
        private void OnStateChanged(SkillSubState newState)
        {
            if (newState == SkillSubState.Spinning && _spinData != null)
            {
                EventBus.Emit(new SkillChargingStartedEvent { SkillId = _skillData.SkillId });
            }
            // ……原有逻辑保持不变
```

f) `OnSkillComplete` 加旋转结束事件与计数清理：

```csharp
        private void OnSkillComplete()
        {
            if (_spinData != null)
            {
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = false,
                    CasterId = _owner.transform.GetInstanceID()
                });
                _spinHitTracker?.Clear();
            }
            _consecutiveHits?.Clear();
            OnSkillCompleted?.Invoke();
        }
```

g) `OnSkillInterrupt` 同样加：

```csharp
        private void OnSkillInterrupt(InterruptionSource source)
        {
            if (_spinData != null)
            {
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = false,
                    CasterId = _owner.transform.GetInstanceID()
                });
                _spinHitTracker?.Clear();
            }
            _consecutiveHits?.Clear();
            OnSkillInterrupted?.Invoke(source);
        }
```

h) `GetShape()` / `GetEffect()` / `GetPresentation()` 各追加一行 Spin 分支：

```csharp
                ?? (_skillData as SpinSkillData)?.Shape
```

```csharp
                ?? (_skillData as SpinSkillData)?.Effect
```

```csharp
                ?? (_skillData as SpinSkillData)?.Presentation
```

i) `TryStart()` 之后新增转发方法：

```csharp
        /// <summary>
        /// 旋转技能：当前是否在可取消窗口内
        /// </summary>
        public bool CanCancel()
        {
            return _stateMachine.CanCancel();
        }

        /// <summary>
        /// 旋转技能：窗口内主动取消（正常完成语义）
        /// </summary>
        public void Cancel()
        {
            _stateMachine.Cancel();
        }
```

- [ ] **Step 5: 运行测试确认通过 + 回归**

Run: MCP `assets-refresh`，然后：
1. `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorSpinTests`) — 2 passed
2. `tests-run` (EditMode) — 全量 EditMode 回归，Expected: 全部通过（含既有 DamageBlockFluctuationTests）

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs \
        Assets/Scripts/Tests/Editor/SkillExecutorSpinTests.cs
git commit -m "feat: route spin ticks through SpinHitTracker, emit spin VFX events"
```

---

### Task 5: `SkillCoordinator` 同键取消特例 + 规则 + 移动倍率（TDD）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`
- Test: `Assets/Scripts/Tests/Editor/SkillCoordinatorSpinTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillCoordinatorSpinTests
    {
        private const int SpinId = 20002;
        private const int QId = 20001;

        private GameObject _ownerGo;
        private SkillCoordinator _coordinator;
        private SkillInputBuffer _buffer;

        private SpinSkillData CreateSpinData()
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_skillId", SpinId);
            TestHelpers.SetField(data, "_minDuration", 1f);
            TestHelpers.SetField(data, "_maxDuration", 5f);
            TestHelpers.SetField(data, "_tickInterval", 0.2f);
            TestHelpers.SetField(data, "_moveSpeedMultiplier", 0.5f);
            return data;
        }

        private InstantSkillData CreateInstantData()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", QId);
            return data;
        }

        [SetUp]
        public void SetUp()
        {
            _ownerGo = new GameObject("CoordOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            _coordinator = new SkillCoordinator(owner);
            _coordinator.RegisterSkill(CreateSpinData());
            _coordinator.RegisterSkill(CreateInstantData());

            // 反射取出私有输入缓冲，验证入队行为
            var field = typeof(SkillCoordinator).GetField("_inputBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
            _buffer = (SkillInputBuffer)field.GetValue(_coordinator);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_ownerGo);
        }

        [Test]
        public void HandleInput_SameSkillWhileSpinning_IsNotBuffered()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));
            Assert.AreEqual(SkillSubState.Spinning, _coordinator.CurrentSubState, "按下后立即进入旋转");

            // 旋转期间（冷却中！）再按R —— 必须走取消特例而非缓冲
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));

            Assert.AreEqual(0, _buffer.Count, "spin 期间按R永不入缓冲（否则会延迟重施放）");
        }

        [Test]
        public void HandleInput_OtherSkillWhileSpinning_IsBuffered()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));

            _coordinator.HandleInput(SkillInput.SkillToPosition(QId, Vector3.zero));

            Assert.AreEqual(1, _buffer.Count, "其他技能在旋转期间被缓冲（既有行为）");
        }

        [Test]
        public void GetMoveSpeedMultiplier_IsMultiplierWhileSpinning_ElseOne()
        {
            Assert.AreEqual(1f, _coordinator.GetMoveSpeedMultiplier(), 0.0001f, "无技能时倍率为1");

            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));
            Assert.AreEqual(0.5f, _coordinator.GetMoveSpeedMultiplier(), 0.0001f, "旋转时返回配置倍率");
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillCoordinatorSpinTests`)
Expected: 编译失败（`SkillCoordinator.GetMoveSpeedMultiplier` 不存在；`HandleInput` 同键会走缓冲路径 → `Buffer_NotBuffered` 断言失败）

- [ ] **Step 3: 实现 `SkillCoordinator` 改动**

a) `HandleInput` 方法**最顶部**（`_skillDatabase.TryGetValue` 之前）加同键取消特例：

```csharp
        public void HandleInput(SkillInput input)
        {
            // R 同键取消特例：旋转期间再按同技能键 = 取消（必须位于冷却检查之前，
            // 因为旋转中技能自身处于冷却；永不入缓冲，防延迟重施放）
            if (_currentSkill != null
                && _currentSkill.CurrentSubState == SkillSubState.Spinning
                && input.SkillId == _currentSkill.SkillId)
            {
                if (_currentSkill.CanCancel())
                    _currentSkill.Cancel();
                return;
            }

            // 检查技能是否存在
            if (!_skillDatabase.TryGetValue(input.SkillId, out var skillData))
            {
                return;
            }
            // ……其余逻辑保持不变
```

b) `CanChainSkill` 的 switch（`SkillSubState.Charging => false,` 之后）加：

```csharp
                SkillSubState.Spinning => false,
```

c) `CanMove()` 的 switch（`SkillSubState.Charging => ...` 之后）加：

```csharp
                SkillSubState.Spinning => true,
```

d) `CanRotate()` 的 switch（`SkillSubState.Charging => ...` 之后）加：

```csharp
                SkillSubState.Spinning => true,
```

e) `CanRotate()` 方法之后新增：

```csharp
        /// <summary>
        /// 当前移动速度倍率（旋转期间按配置减速，其余为1）
        /// </summary>
        public float GetMoveSpeedMultiplier()
        {
            if (_currentSkill != null
                && _currentSkill.CurrentSubState == SkillSubState.Spinning
                && _currentSkill.Data is SpinSkillData spin)
            {
                return spin.MoveSpeedMultiplier;
            }
            return 1f;
        }
```

- [ ] **Step 4: 运行测试确认通过**

Run: MCP `assets-refresh`，然后 `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillCoordinatorSpinTests`)
Expected: 3 passed / 0 failed

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs \
        Assets/Scripts/Tests/Editor/SkillCoordinatorSpinTests.cs
git commit -m "feat: handle same-key cancel for spin skill, add move speed multiplier"
```

---

### Task 6: `Sys3CEntry` + `InputManager`（去蓄力输入、移动减速、死亡中断）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`

- [ ] **Step 1: `Sys3CEntry.Update` 移动减速**

`Sys3CEntry.cs:132-134`，在 `_cc.Update(command)` 前插入：

```csharp
            var command = _inputManager.GetMoveCommand(cameraForward);
            command.Speed *= _skillCoordinator.GetMoveSpeedMultiplier();
            _cc.Update(command);
```

- [ ] **Step 2: 删除蓄力释放输入分支**

`Sys3CEntry.HandleInput` 中删除 `IsSkill3Released` 整段（原 238-245 行）：

```csharp
                if (_inputManager.IsSkill3Released())
                {
                    var executor = _skillCoordinator.CurrentSkill;
                    if (executor != null && executor.CurrentSubState == Skills.Definition.SkillSubState.Charging)
                    {
                        executor.ReleaseCharge();
                    }
                }
```

- [ ] **Step 3: 死亡中断技能**

`Sys3CEntry.IDamageable.TakeDamage` 中两处 `if (_currentHP <= 0) { _currentHP = 0; }`（盾破分支 ~375-379 与主分支 ~400-403）各追加一行：

```csharp
                    if (_currentHP <= 0)
                    {
                        _currentHP = 0;
                        _skillCoordinator.InterruptCurrentSkill(InterruptionSource.Stun);
                    }
```

（`InterruptionSource` 已有 using：`Hotfix.GameSystems.Skills.Definition`。）

- [ ] **Step 4: 删除 `InputManager.IsSkill3Released`**

`InputManager.cs:217-223` 删除整个方法（grep 确认仅 Sys3CEntry 引用，已在本任务移除）：

```csharp
        /// <summary>
        /// 3技能释放（R键松开）- 用于持续技能取消
        /// </summary>
        public bool IsSkill3Released()
        {
            return UnityInput.GetKeyUp(KeyCode.R);
        }
```

- [ ] **Step 5: 验证**

Run: MCP `assets-refresh`，`console-get-logs` (filter=Error) — 无编译错误；`tests-run` (EditMode) — 全量回归通过。

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs
git commit -m "feat: spin move-speed multiplier, remove charge-release input, interrupt skill on death"
```

---

### Task 7: 资产迁移（Spin_SkillR.asset + 删除 Charged_SkillR + 场景接线）

**Files:**
- Create: `Assets/PreRes/SkillsCfg/Spin_SkillR.asset`
- Delete: `Assets/PreRes/SkillsCfg/Charged_SkillR.asset` (+ .meta)
- Modify: 场景内 `Sys3CEntry._characterSkills` 引用（Inspector/MCP）

- [ ] **Step 1: 取得 SpinSkillData 脚本 GUID**

Read `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SpinSkillData.cs.meta`，记录 `guid:` 值（Task 1 导入后已生成），下文记为 `<SPIN_SCRIPT_GUID>`。

- [ ] **Step 2: 创建 `Spin_SkillR.asset`**

Write `Assets/PreRes/SkillsCfg/Spin_SkillR.asset`（`<SPIN_SCRIPT_GUID>` 替换为 Step 1 的 guid；`_skillType: 8` 为 `SkillType.Spin` 的序列化值）：

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: <SPIN_SCRIPT_GUID>, type: 3}
  m_Name: Spin_SkillR
  m_EditorClassIdentifier: 
  _skillId: 20002
  _skillName: Skill R
  _description: 
  _icon: {fileID: 0}
  _skillType: 8
  _quality: 1
  _manaCost: 0
  _cooldown: 15
  _staminaCost: 0
  _animatorTrigger: SkillR
  _castClip: {fileID: 1827226128182048838, guid: 11cede249330b1744a2bc174ddb6111c, type: 3}
  _releaseClip: {fileID: 1827226128182048838, guid: 5729b70d60fdcac45bd03f32c0b8dbea, type: 3}
  _dashDistance: 0
  _dashDuration: 0
  _canBeInterruptedByDamage: 0
  _canBeInterruptedByMovement: 0
  _interruptionPriority: 80
  _canCancelIntoBasicAttack: 0
  _canCancelIntoOtherSkill: 0
  _damage:
    _baseDamage: 160
    _attackRatio: 0
    _scalingAttribute: 0
    _damageType: 0
    _criticalRateBonus: 0.25
    _criticalDamageBonus: 0
    _isTrueDamage: 0
    _armorPenetration: 0
    _knockbackForce: 4
    _isDOT: 0
    _tickInterval: 1
    _totalTicks: 5
  _minDuration: 1
  _maxDuration: 5
  _tickInterval: 0.2
  _maxHitsPerTarget: 5
  _moveSpeedMultiplier: 0.5
  _shape:
    _targetType: 1
    _range: 2
    _angle: 120
    _angleStart: 0
    _angleEnd: 90
    _innerRadius: 0.5
    _innerAngle: 60
    _areaRadius: 2.5
    _width: 1
    _stopAtFirst: 0
    _targetMask:
      serializedVersion: 2
      m_Bits: 4294967295
    _hitboxTimings: []
  _effect:
    _applyEffects: []
    _knockbackForce: 4
    _launchForce: 0
    _stunDuration: 0
    _statusType: 0
    _statusDuration: 0
    _statusValue: 0
  _presentation:
    _castVFX: {fileID: 0}
    _releaseVFX: {fileID: 0}
    _castSFX: {fileID: 0}
    _hitStopDuration: 0.2
    _showCastingBar: 1
    _castingBarColor: {r: 0, g: 0, b: 1, a: 1}
```

- [ ] **Step 3: 刷新并验证资产导入**

Run: MCP `assets-refresh`；`console-get-logs` (filter=Error) — 无错误；MCP `assets-find`（filter=`Spin_SkillR`）— 能查到资产。

- [ ] **Step 4: 找到旧资产引用点**

Read `Assets/PreRes/SkillsCfg/Charged_SkillR.asset.meta` 记录旧 GUID，然后：

```bash
grep -rln "<旧GUID>" Assets --include="*.unity" --include="*.prefab" --include="*.asset" --include="*.mat"
```

Expected: 命中放置玩家角色的场景文件（`Sys3CEntry._characterSkills` 数组引用）。

- [ ] **Step 5: 更新场景引用为 Spin_SkillR**

MCP 路径：打开目标场景（`scene-open`）→ `gameobject-find` 定位挂载 `Sys3CEntry` 的对象 → `gameobject-component-get` 找到 `_characterSkills` 数组中旧 GUID 所在下标 → `gameobject-component-modify` 将该元素引用替换为 Spin_SkillR 资产。
手动路径：在 Unity Inspector 中打开该场景，选中角色对象，将 `Sys3CEntry._characterSkills` 中 `Charged_SkillR` 条目拖换为 `Spin_SkillR`，保存场景。

验证：`grep -c "<SPIN_SCRIPT_GUID>" <场景文件>` ≥ 1，且 `<旧GUID>` 不再出现于场景文件。

- [ ] **Step 6: 删除旧资产**

```bash
git rm Assets/PreRes/SkillsCfg/Charged_SkillR.asset Assets/PreRes/SkillsCfg/Charged_SkillR.asset.meta
```

（如 Step 4 无其他引用点，`git rm` 安全。）

- [ ] **Step 7: 提交**

```bash
git add Assets/PreRes/SkillsCfg/Spin_SkillR.asset
git commit -m "feat: migrate SkillR to SpinSkillData asset, remove Charged_SkillR"
```

---

### Task 8: 验收（PlayMode 手动清单）

**Files:** 无代码改动。

- [ ] **Step 1: PlayMode 验证（对照 spec §13）**

进入 PlayMode，逐项验证：

| # | 验证项 | 预期 |
|---|--------|------|
| 1 | 按一次 R | 立即起手转圈（SkillR_start→SkillR_loop），无读条 |
| 2 | 起手期间再按 R（<1s） | 无效，继续转 |
| 3 | 转满 1s 后再按 R | 立即取消，退出 loop 回 Idle |
| 4 | 不取消持续转 | 5s 后自动结束 |
| 5 | 旋转期间按 Q/普攻 | 无法施放；Q 若在结束前 0.5s 内按下会在结束后衔接施放 |
| 6 | 旋转期间移动 | 可移动但速度减半；转向正常 |
| 7 | 旋转期间伤害 | 每 0.2s 对范围内所有敌人结算全额伤害；同一敌人最多受击 5 次；中途进入范围的敌人独立计数 |
| 8 | 敌人受击 | 每 tick 被击退（knockback 4）；2-3 次命中后出圈（数值调优项，可接受） |
| 9 | 取消后冷却 | 冷却 15s 正常开始，不能立刻再转 |
| 10 | VFX | 起手剑光、旋转拖尾、命中冰爆随技能启停 |
| 11 | 被打断（眩晕/翻滚） | 旋转中止，动画退出 |
| 12 | 死亡 | 旋转立即停止（本计划补的死亡中断钩子生效） |
| 13 | 转圈期间开 UI/背包 | 不触发异常（回归项） |

- [ ] **Step 2: 记录结果**

在 `production/session-logs/session-log.md` 追加验证结果（通过/失败项列表）。如有失败项，用 superpowers:systematic-debugging 处理。

---

## 自审记录（writing-plans 自检）

**Spec 覆盖：**
- §4 SpinSkillData 字段/IsInCancelWindow/OnValidate → Task 1 ✓
- §5 状态机 Spinning/tick调度/自动完成/取消幂等/GetCurrentTime 缝 → Task 3 ✓
- §6 executor tick 重写/SpinHitTracker/事件/GetShape等分支/CanCancel → Task 2+4 ✓
- §7 coordinator 同键取消（冷却检查之前）/CanChainSkill/CanMove/CanRotate/倍率 + Sys3CEntry 去蓄力分支 → Task 5+6 ✓
- §8 移动减速（MoveCommand.Speed 乘倍率）/动画零改动 → Task 6 ✓
- §9 边界：缓冲竞态(Task 5 拦截在冷却检查前+测试)、取消幂等(Task 3)、maxHits<=0(Task 2)、死亡中断(Task 6)、击退交互(验收清单) ✓
- §10 资产迁移 → Task 7 ✓
- §11 测试 → Task 1-5 ✓（executor 物理路径不进 EditMode，用 tracker+状态机测试覆盖等价逻辑，spec 表格该行已调整为薄转发测试）
- §13 验收 → Task 8 ✓

**已知取舍：**
- `UpdateSpinning` 每帧最多 1 tick（大卡顿后逐帧追 tick），防一帧爆发多段伤害——比 spec 中"while 追平"更安全，行为等价于固定间隔（tick 序号不因卡顿漂移）
- 死亡中断用 `InterruptionSource.Stun`（`CanBeInterrupted: Stun => true` 硬编码），对任意技能生效
- Animator 无需改动（SkillR trigger → start → loop → AttackState==0 退出链路已存在）
