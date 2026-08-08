# 火系三技能（火球 / 蓄力炎爆 / 陨石）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给现有剑客角色添加 3 个火系法师技能：Q=火球术（投射物）、R=蓄力炎爆（蓄力 AOE）、U=陨石术（延迟落点 AOE），替换 Q/R 槽位并启用大招位。

**Architecture:** 完全复用现有技能系统：`SkillStateMachine` 补 Release 接口（修复蓄力提前释放死代码）；`SkillExecutor` 加投射物/陨石生成钩子（Execution 进入时同步生成，与剑气 `OnSwordQiFrame` 同构）；新增 `FireballProjectile`/`FireballSpawner`/`MeteorStrike`/`MeteorSpawner` 四个运行时文件（镜像 SwordQi 三件套模式：静态懒加载池 + GameRoot 容器 + `DisposeForTests`）；目标检测统一走 `PhysicsRegistry.FindNearby`（剑气同款，EntityType.Monster）；伤害经 `Damage.Clone()` + `SkillDamageHelper.FillDamageContext` 填充（克隆语义与剑气一致）。Animator 新增 `ChargeBlast`/`Ultimate` 两个 trigger 参数与两个状态。

**Tech Stack:** Unity 2022.3.25f1、Hotfix 层 C#（HybridCLR）、NUnit EditMode 测试（`Assets/Scripts/Tests/Editor/`，命名空间 `GameSys.EditorTests`）、Unity MCP（tests-run / animator-modify / script-execute）。

---

## 与 spec 的偏差（实现前已定稿，原因说明）

1. **火球/陨石生成时机改为 Execution 进入时同步生成**（spec §4.3 原定 `_spawnDelay` 延迟生成）。原因：`SkillExecutor` 无注入时钟（状态机用 `Time.time`），延迟生成不可测；同步生成与剑气模式同构、可直接在 `TryStart` 后断言。火球的"施法前摇"由 Execution 动画时长表达（资产 `_releaseClip` 为空时 0.5s 兜底）。
2. **取消 `_targetMask` 字段**（spec §4.3）：目标检测统一走 `PhysicsRegistry.FindNearby`（剑气同款），不引入层掩码。
3. **爆炸伤害统一全额**（spec §3 的"爆炸目标 0.5 倍率"拆分取消）：命中即爆炸，爆炸半径内所有目标吃同一份伤害；调优交给资产 DamageBlock。
4. **`AnimHashes` 不新增常量**（spec §4.5）：Animator trigger 由资产 `AnimatorTrigger` 字符串经 `Sys3CEntry` 的 `SetTrigger(_lastSkillTrigger)` 驱动，无需 hash 常量。
5. **删除 `_isCharging` 死字段**：Release() 取代其语义后该字段无读者，随修复一并清理。

**运行测试命令约定**（每个任务）：Unity MCP `tests-run`，`testMode=EditMode`，`testClass=GameSys.EditorTests.<TestClass>`。前提：所有已打开场景必须已保存。

---

## 文件结构

**新增（运行时）**：
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs` — 火球弹体：直线飞行 + 命中即爆 + 池化回收
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballSpawner.cs` — 火球池静态入口
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorStrike.cs` — 陨石：预警 + 延迟落点爆炸 + 池化
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorSpawner.cs` — 陨石池静态入口
- `Assets/Scripts/Hotfix/GameSystems/Skills/Data/MeteorConfig.cs` — 陨石配置 SO
- `Assets/Scripts/Hotfix/GameSystems/Skills/Events/FireballSpawnedEvent.cs`、`MeteorSpawnedEvent.cs` — 生成快照事件（无订阅者，同 SwordQiSpawnedEvent）

**新增（测试）**：`Assets/Scripts/Tests/Editor/` 下 10 个 `*Tests.cs`（各任务内定义）

**修改**：
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs` — Release() + ChargeProgressAtRelease + 清理 _isCharging
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` — 蓄力结算修复 + 投射物/陨石生成钩子 + 打断补发事件
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs` — TryReleaseCharge
- `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs` — +_spawnOffset/_explosionRadius + OnValidate
- `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs` — +Meteor hook
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs` — +IsSkill3Held/IsSkillUltimatePressed
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` — R 松开释放 + U 键分支

**资产**：3 个技能 SO、MeteorConfig、火球 prefab、陨石预警 prefab、爆炸 VFX prefab、Animator controller 改造

---

### Task 1: SkillStateMachine — Release() + ChargeProgressAtRelease

**Files:**
- Create: `Assets/Scripts/Tests/Editor/SkillStateMachineChargeTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillStateMachineChargeTests
    {
        /// <summary>假时间状态机：override GetCurrentTime() 注入可控时钟（同 SkillStateMachineSpinTests 模式）</summary>
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        private ChargedSkillData CreateChargedData(float min, float max)
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            TestHelpers.SetField(data, "_minChargeTime", min);
            TestHelpers.SetField(data, "_maxChargeTime", max);
            return data;
        }

        /// <summary>TryStart 后推进到蓄力阶段（Casting 0.1s 硬编码）</summary>
        private static void AdvanceToCharging(FakeTimeMachine machine)
        {
            machine.TryStart();
            machine.Now = 0.15f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, machine.CurrentState);
        }

        [Test]
        public void test_charge_release_below_min_fires_immediately()
        {
            // Arrange（min 0.3 / max 2.0，蓄力 0.25s < min）
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));
            AdvanceToCharging(machine);
            machine.Now = 0.40f;
            machine.Update(0.01f);

            // Act（未满 min 就松开）
            bool released = machine.Release();

            // Assert（任意时刻释放，立即进 Execution，进度 = 实际蓄力/最大）
            Assert.IsTrue(released, "未满 min 也应立即释放");
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(0.125f, machine.ChargeProgressAtRelease, 0.001f); // 0.25/2.0
        }

        [Test]
        public void test_charge_release_records_partial_progress()
        {
            // Arrange
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));
            AdvanceToCharging(machine);
            machine.Now = 1.15f; // 蓄力 1.0s
            machine.Update(0.01f);

            // Act
            Assert.IsTrue(machine.Release());

            // Assert
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(0.5f, machine.ChargeProgressAtRelease, 0.001f);
        }

        [Test]
        public void test_charge_auto_release_at_max_sets_full_progress()
        {
            // Arrange
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 1f));
            AdvanceToCharging(machine);
            machine.Now = 1.15f; // 蓄力 ≥ max 1.0
            machine.Update(0.01f);

            // Assert（自动释放，进度 = 1）
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(1f, machine.ChargeProgressAtRelease, 0.001f);
        }

        [Test]
        public void test_charge_release_when_not_charging_rejected()
        {
            // Arrange（Ready 态，未开始）
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));

            // Act & Assert
            Assert.IsFalse(machine.Release(), "未开始蓄力时 Release 无效");
            Assert.AreEqual(SkillSubState.Ready, machine.CurrentState);

            // Arrange（Casting 阶段）
            machine.TryStart();

            // Act & Assert
            Assert.IsFalse(machine.Release(), "读条阶段 Release 无效");
        }

        [Test]
        public void test_charge_release_after_release_is_noop()
        {
            // Arrange
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));
            AdvanceToCharging(machine);
            machine.Now = 0.65f;
            machine.Update(0.01f);
            Assert.IsTrue(machine.Release());
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);

            // Act（再次 Release）
            Assert.IsFalse(machine.Release(), "已释放后 Release 幂等无效");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillStateMachineChargeTests`)
Expected: FAIL — `SkillStateMachine` 无 `Release`/`ChargeProgressAtRelease` 编译错误。

- [ ] **Step 3: 实现**

`SkillStateMachine.cs` 修改 3 处：

```csharp
        private bool _isCharging;   // ← 删除此行（死字段，见 Step 4 关联删除）
        private float _chargeStartTime;
```

（1）TryStart 中删除 `_isCharging = true;`（第 84 行），新增属性与 Release 方法（放在 `Cancel()` 上方）：

```csharp
        /// <summary>
        /// 释放瞬间的蓄力进度 [0,1]（Charging → Execution 转换前写入，供结算倍率使用）
        /// </summary>
        public float ChargeProgressAtRelease { get; private set; }

        /// <summary>
        /// 蓄力技能：松开按钮提前释放（任意时刻有效，进度按实际蓄力缩放）。
        /// 仅在 Charging 态生效，其余状态返回 false（幂等）。
        /// </summary>
        public bool Release()
        {
            if (_currentState != SkillSubState.Charging || _chargedData == null)
                return false;
            ChargeProgressAtRelease = Mathf.Clamp01(
                (GetCurrentTime() - _chargeStartTime) / _chargedData.MaxChargeTime);
            TransitionTo(SkillSubState.Execution);
            return true;
        }
```

（2）`UpdateCharging()` 重写为（删除 `!_isCharging` 死分支，满蓄力写入进度 1）：

```csharp
        private void UpdateCharging()
        {
            if (_chargedData == null) { Complete(); return; }

            float chargeTime = GetCurrentTime() - _chargeStartTime;

            // 蓄力期判定帧（ChargeBlast 资产配置空数组 = 蓄力期不结算伤害）
            float[] hitboxTimings = GetHitboxTimings();
            if (hitboxTimings != null)
            {
                for (int i = _currentTick; i < hitboxTimings.Length; i++)
                {
                    if (chargeTime >= hitboxTimings[i])
                    {
                        _currentTick = i + 1;
                        _onHitboxFrame?.Invoke(i);
                        _onHitConfirm?.Invoke();
                    }
                }
            }

            if (chargeTime >= _chargedData.MaxChargeTime)
            {
                ChargeProgressAtRelease = 1f;
                TransitionTo(SkillSubState.Execution);
            }
        }
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillStateMachineChargeTests`)
Expected: PASS（5/5）

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillStateMachineSpinTests`)
Expected: PASS（回归，重写 UpdateCharging 不影响 Spin 路径）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs Assets/Scripts/Tests/Editor/SkillStateMachineChargeTests.cs
git commit -m "feat: charge release — Release() + ChargeProgressAtRelease (fix dead release path)"
```

---

### Task 2: SkillExecutor — 蓄力结算修复

**Files:**
- Create: `Assets/Scripts/Tests/Editor/SkillExecutorChargeTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorChargeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;
        private int _releasedCount;

        [SetUp]
        public void SetUp()
        {
            _releasedCount = 0;
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private void OnReleased(SkillReleasedEvent e) { _releasedCount++; }

        /// <summary>
        /// 物理命中假目标：执行器 AOE 检测走 Physics.OverlapSphere（碰撞体 + IEffectTarget 组件），
        /// 因此必须挂真实 Collider 与 MonoBehaviour 组件（不同于剑气测试的 PhysicsRegistry 假目标）
        /// </summary>
        private class FakeHitTarget : MonoBehaviour, IEffectTarget, IDamageable
        {
            public bool IsAlive => true;
            public int HitCount { get; private set; }
            public DamageBlock LastDamage;
            public IEffectStats Stats => null;
            public IShieldSystem ShieldSystem => null;
            public IPhysicsSystem PhysicsSystem => null;
            public IStatusController StatusController => null;
            public void Heal(float amount) { }
            public void TakeDamage(DamageBlock d, Vector3 dir) { HitCount++; LastDamage = d; }
            public void TakeDamage(DamageBlock d, Vector3 dir, HitContact c) { TakeDamage(d, dir); }
        }

        /// <summary>假时间状态机（同 Task 1，SkillExecutor 测试用反射注入替换内部机器）</summary>
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        /// <summary>用假时钟机器替换执行器内部机器（readonly 字段反射可写）</summary>
        private static void InjectMachine(SkillExecutor executor, FakeTimeMachine machine)
        {
            TestHelpers.SetField(executor, "_stateMachine", machine);
        }

        private static void RaiseHitboxFrame(SkillExecutor executor, int frameIndex)
        {
            var stateMachine = TestHelpers.GetField(executor, "_stateMachine");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var eventField = stateMachine.GetType().GetField("_onHitboxFrame", flags);
            Assert.IsNotNull(eventField);
            var del = (System.Action<int>)eventField.GetValue(stateMachine);
            del?.Invoke(frameIndex);
        }

        /// <summary>充电技能数据：AOE 圆 2m，命中帧 {0f}（Execution 进入即炸），伤害 100</summary>
        private ChargedSkillData CreateChargedData()
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_minChargeTime").floatValue = 0.3f;
            so.FindProperty("_maxChargeTime").floatValue = 2f;
            so.FindProperty("_shape/_targetType").enumValueIndex = (int)TargetType.AOE_Circle;
            so.FindProperty("_shape/_areaRadius").floatValue = 2f;
            so.FindProperty("_shape/_hitboxTimings").arraySize = 1;
            so.FindProperty("_shape/_hitboxTimings").GetArrayElementAtIndex(0).floatValue = 0f;
            so.ApplyModifiedProperties();
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(100f, 0f));
            return data;
        }

        private SkillExecutor CreateExecutor(ChargedSkillData data)
        {
            _ownerGo = new GameObject("ChargeOwner");
            _ownerGo.transform.position = Vector3.zero;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            InjectMachine(executor, new FakeTimeMachine(data));
            return executor;
        }

        private static void DriveToCharging(SkillExecutor executor, FakeTimeMachine machine, float at = 0.15f)
        {
            executor.TryStart();
            machine.Now = at;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
        }

        /// <summary>物理命中假目标：带 SphereCollider 与 FakeHitTarget 组件（AOE 物理检测用）</summary>
        private FakeHitTarget NewTarget(string name, Vector3 position)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            go.transform.position = position;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.5f;
            return go.AddComponent<FakeHitTarget>();
        }

        [Test]
        public void test_charge_blast_no_damage_while_charging()
        {
            // Arrange
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            var target = NewTarget("Target", new Vector3(1.5f, 0f, 0f));

            // Act（蓄力中判定帧）
            RaiseHitboxFrame(executor, 0);

            // Assert（蓄力期不结算伤害）
            Assert.AreEqual(0, target.HitCount, "蓄力期间不得造成伤害");
        }

        [Test]
        public void test_charge_blast_damage_scales_by_curve_at_release()
        {
            // Arrange（默认曲线 Linear(0,1,1,2)，进度 0.5 → 1.5x）
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            machine.Now = 1.15f; // 蓄力 1.0s → 进度 0.5
            executor.Update(0.01f); // → Execution
            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);
            var target = NewTarget("Target", new Vector3(1.5f, 0f, 0f));

            // Act（Execution 判定帧 → AOE 命中）
            RaiseHitboxFrame(executor, 0);

            // Assert（100 × curve(0.5)=1.5 → 150）
            Assert.AreEqual(1, target.HitCount);
            Assert.AreEqual(150f, target.LastDamage.CalculatedDamage, 0.001f);
        }

        [Test]
        public void test_charge_blast_full_charge_marks_was_full_charge()
        {
            // Arrange
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            machine.Now = 2.15f; // 蓄力 ≥ max → 自动释放，进度 1
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);

            // Assert（满蓄力标志写入，不再是死字段恒 false）
            Assert.IsTrue((bool)TestHelpers.GetField(executor, "_wasFullCharge"), "满蓄力应标记 _wasFullCharge");
        }

        [Test]
        public void test_charge_blast_aoe_radius_scales_with_charge()
        {
            // Arrange（基础半径 2m，满蓄力 AreaScale(1)=1.5 → 3m；目标 2.6m：未缩放够不到，缩放后命中）
            var data = CreateChargedData();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_chargeAreaCurve").animationCurveValue =
                AnimationCurve.Linear(0f, 1f, 1f, 1.5f);
            so.ApplyModifiedProperties();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            machine.Now = 2.15f; // 满蓄力
            executor.Update(0.01f);
            var target = NewTarget("Target", new Vector3(2.6f, 0f, 0f));

            // Act
            RaiseHitboxFrame(executor, 0);

            // Assert（缩放后半径 3m + 碰撞体半径 0.5m → 触及 2.6m 目标）
            Assert.AreEqual(1, target.HitCount, "满蓄力半径放大后 2.6m 处目标应命中");
        }

        [Test]
        public void test_charge_blast_interrupt_emits_skill_released_event()
        {
            // Arrange
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);

            // Act（蓄力中受击打断）
            Assert.IsTrue(executor.TryInterrupt(InterruptionSource.DamageTaken));

            // Assert（打断必须补发释放事件，VFX 才能熄灭）
            Assert.AreEqual(1, _releasedCount, "蓄力打断应补发 SkillReleasedEvent");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorChargeTests`)
Expected: FAIL（4 个行为断言失败；`_wasFullCharge` 恒 false、蓄力期会误伤、伤害 100 而非 150、打断不发事件）

- [ ] **Step 3: 实现**（`SkillExecutor.cs` 4 处修改）

（1）`OnHitboxTriggered` 开头（`DetectTargets` 之后、`_spinData` 判断之前）加蓄力期守卫：

```csharp
        private void OnHitboxTriggered(int frameIndex)
        {
            var targets = DetectTargets();

            // 蓄力期判定帧仅用于动画/VFX 同步，不结算伤害（ChargeBlast 爆炸只在 Execution 判定帧）
            if (CurrentSubState == SkillSubState.Charging)
                return;

            if (_spinData != null)
```

（2）`ApplyDamage` 蓄力加成替换（第 573-575 行）：

```csharp
            // 蓄力技能：释放瞬间按实际进度 × 曲线结算（修复原硬编码 1.5x 且 Execution 态取 0 的 bug）
            if (_skillData is ChargedSkillData)
                damageBlock.CalculatedDamage *= GetDamageScaleForCharge(_chargeProgressAtRelease);
```

并在类中新增私有方法（放在 `GetChargeProgress()` 附近）：

```csharp
        /// <summary>
        /// 蓄力伤害倍率：按释放进度查数据曲线（1.0 → 曲线末端值）
        /// </summary>
        private float GetDamageScaleForCharge(float progress)
        {
            if (!(_skillData is ChargedSkillData charged)) return 1f;
            return charged.GetDamageScaleForCharge(progress);
        }
```

（3）`OnStateChanged` 的 Execution 分支（第 413-421 行，Charged 处理处）补 `_wasFullCharge` 写入：

```csharp
            if (newState == SkillSubState.Execution && _skillData is ChargedSkillData)
            {
                _wasFullCharge = _stateMachine.ChargeProgressAtRelease >= 1f;
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = _wasFullCharge,
                    CasterId = _owner.transform.GetInstanceID()
                });
            }
```

（4）`OnSkillInterrupt`（第 390-404 行）对 Charged 补发释放事件：

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
            else if (_skillData is ChargedSkillData)
            {
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = false,
                    CasterId = _owner.transform.GetInstanceID()
                });
            }
            _consecutiveHits?.Clear();
            OnSkillInterrupted?.Invoke(source);
        }
```

（5）`DetectAOETargets` 半径缩放（第 526-545 行，`center` 计算后）：

```csharp
            // 蓄力技能：Execution 阶段按释放进度放大 AOE 半径
            float radius = shape.AreaRadius;
            if (_skillData is ChargedSkillData && CurrentSubState == SkillSubState.Execution)
            {
                float areaScale = ((ChargedSkillData)_skillData).GetAreaScaleForCharge(_stateMachine.ChargeProgressAtRelease);
                radius = shape.AreaRadius * areaScale;
            }

            if (shape.TargetType == TargetType.AOE_Cone)
                DetectConeTargets(center, targets, shape);
            else
            {
                int count = Physics.OverlapSphereNonAlloc(center, radius, s_AoeBuffer, shape.TargetMask);
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorChargeTests`)
Expected: PASS（5/5）

Run: Unity MCP `tests-run` (EditMode) — 全量回归
Expected: PASS（含既有 SkillExecutorSpin/SwordQi 测试）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs Assets/Scripts/Tests/Editor/SkillExecutorChargeTests.cs
git commit -m "fix: charge blast settlement — curve-scaled damage, charge-period guard, interrupt releases VFX event, radius scaling, _wasFullCharge"
```

---

### Task 3: SkillCoordinator — TryReleaseCharge

**Files:**
- Create: `Assets/Scripts/Tests/Editor/SkillCoordinatorChargeTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillCoordinatorChargeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        private ChargedSkillData CreateChargedData()
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            TestHelpers.SetField(data, "_skillId", 20002);
            TestHelpers.SetField(data, "_minChargeTime", 0.3f);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            return data;
        }

        /// <summary>构造协调器并把"当前技能"置为蓄力中的执行器（反射注入）</summary>
        private SkillCoordinator CreateCoordinatorWithChargingSkill()
        {
            _ownerGo = new GameObject("ChargeOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            var data = CreateChargedData();
            coordinator.RegisterSkill(data);

            var executor = new SkillExecutor(owner, data);
            var machine = new FakeTimeMachine(data);
            TestHelpers.SetField(executor, "_stateMachine", machine);
            executor.TryStart();
            machine.Now = 0.15f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
            TestHelpers.SetField(coordinator, "_currentSkill", executor);
            return coordinator;
        }

        [Test]
        public void test_try_release_charge_current_charging_skill_releases()
        {
            // Arrange
            var coordinator = CreateCoordinatorWithChargingSkill();

            // Act
            bool released = coordinator.TryReleaseCharge(20002);

            // Assert
            Assert.IsTrue(released, "蓄力中的当前技能应被释放");
            Assert.AreEqual(SkillSubState.Execution, coordinator.CurrentSubState);
        }

        [Test]
        public void test_try_release_charge_wrong_skill_id_ignored()
        {
            // Arrange
            var coordinator = CreateCoordinatorWithChargingSkill();

            // Act
            bool released = coordinator.TryReleaseCharge(9999);

            // Assert
            Assert.IsFalse(released, "非当前技能 id 不得释放");
            Assert.AreEqual(SkillSubState.Charging, coordinator.CurrentSubState);
        }

        [Test]
        public void test_try_release_charge_when_no_skill_active_is_noop()
        {
            // Arrange（无当前技能）
            _ownerGo = new GameObject("ChargeOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);

            // Act & Assert
            Assert.IsFalse(coordinator.TryReleaseCharge(20002), "无技能执行时 Release 无效");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillCoordinatorChargeTests`)
Expected: FAIL — `TryReleaseCharge` 不存在，编译错误。

- [ ] **Step 3: 实现**（`SkillCoordinator.cs`，放在 `HandleInput` 上方）

```csharp
        /// <summary>
        /// 蓄力技能：松开按钮提前释放（由输入层在键松开时调用）。
        /// 仅当指定技能正在蓄力时生效。
        /// </summary>
        public bool TryReleaseCharge(int skillId)
        {
            if (_currentSkill != null
                && _currentSkill.SkillId == skillId
                && _currentSkill.CurrentSubState == SkillSubState.Charging)
            {
                return _currentSkill.Release();
            }
            return false;
        }
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillCoordinatorChargeTests`)
Expected: PASS（3/3）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs Assets/Scripts/Tests/Editor/SkillCoordinatorChargeTests.cs
git commit -m "feat: SkillCoordinator.TryReleaseCharge — release charged skill on key up"
```

---

### Task 4: ProjectileSkillData — 扩展字段 + OnValidate

**Files:**
- Create: `Assets/Scripts/Tests/Editor/ProjectileSkillDataTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class ProjectileSkillDataTests
    {
        [Test]
        public void test_projectile_skill_data_onvalidate_forces_type_projectile()
        {
            // Arrange（模拟 Inspector 里被改错类型）
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            TestHelpers.SetField(data, "_skillType", Definition.SkillType.Combo);

            // Act（OnValidate 为私有方法，反射调用）
            var method = typeof(ProjectileSkillData).GetMethod("OnValidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "OnValidate 必须存在");
            method.Invoke(data, null);

            // Assert（与 Combo/Charged/Channeled 同款约束：类型强制正确）
            Assert.AreEqual(Definition.SkillType.Projectile, data.SkillType);
        }

        [Test]
        public void test_projectile_skill_data_defaults()
        {
            // Arrange
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();

            // Assert（默认值：胸前发射位、命中爆炸 1.5m、射程 15m）
            Assert.AreEqual(1.5f, data.ExplosionRadius, 0.001f);
            Assert.AreEqual(new Vector3(0f, 1.2f, 0.5f), data.SpawnOffset);
            Assert.AreEqual(15f, data.ProjectileRange, 0.001f);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.ProjectileSkillDataTests`)
Expected: FAIL — 编译错误（`ExplosionRadius`/`SpawnOffset`/`OnValidate`/`SetTypeForTest` 不存在）。

- [ ] **Step 3: 实现**（`ProjectileSkillData.cs` 重写）

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ProjectileSkill", menuName = "Game/Skills/Projectile Skill")]
    public class ProjectileSkillData : SkillData
    {
        [Header("=== Projectile ===")]
        [Tooltip("施放时生成的投射物预制体")]
        [SerializeField] private GameObject _projectilePrefab;
        public GameObject ProjectilePrefab => _projectilePrefab;

        [Tooltip("投射物速度(世界单位/秒)")]
        [SerializeField] private float _projectileSpeed = 20f;
        public float ProjectileSpeed => _projectileSpeed;

        [Tooltip("投射物最大飞行射程(米)，耗尽后回池")]
        [SerializeField] private float _projectileRange = 15f;
        public float ProjectileRange => _projectileRange;

        [Tooltip("投射物是否穿透目标?")]
        [SerializeField] private bool _projectilePierce;
        public bool ProjectilePierce => _projectilePierce;

        [Tooltip("投射物可以穿透的最大目标数")]
        [SerializeField] private int _maxPierceTargets = 3;
        public int MaxPierceTargets => _maxPierceTargets;

        [Tooltip("投射物是否追踪目标?")]
        [SerializeField] private bool _homing;
        public bool Homing => _homing;

        [Tooltip("生成位置偏移（角色局部坐标：前方 + 胸前高度）")]
        [SerializeField] private Vector3 _spawnOffset = new Vector3(0f, 1.2f, 0.5f);
        public Vector3 SpawnOffset => _spawnOffset;

        [Tooltip("命中首个目标后的爆炸半径（0 = 仅单体，不产生范围伤害）")]
        [SerializeField] private float _explosionRadius = 1.5f;
        public float ExplosionRadius => _explosionRadius;

        [Header("=== Config Blocks ===")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;
        // Note: no ShapeBlock — projectile handles its own collision detection

        private void OnValidate()
        {
            _skillType = Definition.SkillType.Projectile;
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.ProjectileSkillDataTests`)
Expected: PASS（2/2）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs Assets/Scripts/Tests/Editor/ProjectileSkillDataTests.cs
git commit -m "feat: ProjectileSkillData — spawnOffset/range/explosionRadius + OnValidate type guard"
```

---

### Task 5: FireballProjectile + 测试

**Files:**
- Create: `Assets/Scripts/Tests/Editor/FireballProjectileTests.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class FireballProjectileTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private readonly List<IDamageable> _registered = new List<IDamageable>();

        private class FakeDamageable : IDamageable
        {
            public bool IsAlive => true;
            public Transform Transform { get; }
            public int HitCount { get; private set; }
            public DamageBlock LastDamage;
            public FakeDamageable(Transform t) { Transform = t; }
            public void TakeDamage(DamageBlock d, Vector3 dir) { HitCount++; LastDamage = d; }
            public void TakeDamage(DamageBlock d, Vector3 dir, HitContact c) { TakeDamage(d, dir); }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var e in _registered)
                PhysicsRegistry.Instance.Unregister(e);
            _registered.Clear();
            foreach (var go in _gos)
                if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private void Register(IDamageable target)
        {
            PhysicsRegistry.Instance.Register(target, EntityType.Monster);
            _registered.Add(target);
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static ProjectileSkillData CreateData(float speed, float range, float explosionRadius = 1.5f)
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_projectileSpeed").floatValue = speed;
            so.FindProperty("_explosionRadius").floatValue = explosionRadius;
            so.ApplyModifiedProperties();
            return data;
        }

        private FireballProjectile CreateProjectile(ProjectileSkillData data, Vector3 origin, Vector3 dir)
        {
            var go = NewGo("Fireball").AddComponent<FireballProjectile>();
            go.OnSpawn(data, DamageBlock.CreateDefault(30f, 0f), origin, dir, null);
            return go;
        }

        [Test]
        public void test_fireball_moves_forward_at_speed()
        {
            // Arrange
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act
            fb.Tick(0.5f);

            // Assert
            Assert.AreEqual(9f, fb.transform.position.z, 0.001f); // 18 * 0.5
        }

        [Test]
        public void test_fireball_explodes_on_first_target_and_damages_radius()
        {
            // Arrange（目标 A 在正前方 3m 飞行路径上；目标 B 在 A 旁 1m，处于 1.5m 爆炸半径内）
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = new Vector3(0f, 0f, 3f);
            Register(targetA);
            var targetB = new FakeDamageable(NewGo("B").transform);
            targetB.Transform.position = new Vector3(0.8f, 0f, 3.6f);
            Register(targetB);
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act（飞过 A 处）
            fb.Tick(3f / 18f);

            // Assert（A 被命中；B 在爆炸半径内也被命中）
            Assert.AreEqual(1, targetA.HitCount, "命中首个目标");
            Assert.AreEqual(1, targetB.HitCount, "爆炸波及爆炸半径内的其他目标");
            Assert.AreEqual(30f, targetA.LastDamage.CalculatedDamage, 0.001f);
        }

        [Test]
        public void test_fireball_does_not_double_hit_same_target()
        {
            // Arrange
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = new Vector3(0f, 0f, 3f);
            Register(targetA);
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act（多帧停留在命中位置）
            for (int i = 0; i < 20; i++)
                fb.Tick(4f / 18f / 20f);

            // Assert
            Assert.AreEqual(1, targetA.HitCount, "同一火球同一目标只结算一次");
        }

        [Test]
        public void test_fireball_releases_after_max_range()
        {
            // Arrange
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act（超过 15m 射程）
            fb.Tick(16f / 18f);

            // Assert
            Assert.IsFalse(fb.gameObject.activeSelf, "飞满射程应回池（SetActive false）");
            fb.Tick(0.1f); // 回池后再步进不抛异常
        }

        [Test]
        public void test_fireball_with_zero_explosion_radius_hits_single_target_only()
        {
            // Arrange（爆炸半径 0 = 纯单体）
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = new Vector3(0f, 0f, 3f);
            Register(targetA);
            var targetB = new FakeDamageable(NewGo("B").transform);
            targetB.Transform.position = new Vector3(0.8f, 0f, 3.6f);
            Register(targetB);
            var fb = CreateProjectile(CreateData(18f, 15f, 0f), Vector3.zero, Vector3.forward);

            // Act
            fb.Tick(3f / 18f);

            // Assert
            Assert.AreEqual(1, targetA.HitCount);
            Assert.AreEqual(0, targetB.HitCount, "爆炸半径 0 时只命中直接目标");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.FireballProjectileTests`)
Expected: FAIL — 编译错误（类型不存在）。

- [ ] **Step 3: 实现**（`FireballProjectile.cs`）

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Projectile
{
    /// <summary>
    /// 火球弹体：直线飞行 + 命中首个目标即爆（爆炸半径内全体结算一次）+ 池化回收。
    /// 生命周期与释放者解耦；飞满射程回池。目标检测走 PhysicsRegistry（同剑气）。
    /// </summary>
    public class FireballProjectile : MonoBehaviour
    {
        /// <summary>命中检测半径（米）</summary>
        private const float DetectRadius = 0.6f;

        private ProjectileSkillData _data;
        private DamageBlock _damage;
        private Vector3 _direction;
        private float _traveled;
        private bool _released;
        private ComponentPool<FireballProjectile> _pool;

        public void OnSpawn(ProjectileSkillData data, DamageBlock damage, Vector3 origin, Vector3 direction,
                            ComponentPool<FireballProjectile> pool)
        {
            _data = data;
            _damage = damage;
            _direction = direction;
            _pool = pool;
            _released = false;
            _traveled = 0f;
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(direction);
            gameObject.SetActive(true);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>每帧步进（公开供 EditMode 测试手动驱动）</summary>
        public void Tick(float deltaTime)
        {
            if (_data == null || _released) return;

            float step = _data.ProjectileSpeed * deltaTime;
            _traveled += step;
            transform.position += _direction * step;

            DetectAndExplode();

            if (_traveled >= _data.ProjectileRange)
                Release();
        }

        private void DetectAndExplode()
        {
            // 固定目标类型：火球为玩家攻击侧技能，只命中怪物
            var targets = PhysicsRegistry.Instance.FindNearby(transform.position, DetectRadius, EntityType.Monster);
            if (targets.Count == 0) return;

            // 命中最近目标，其位置为爆炸中心
            Vector3 hitPos = targets[0].Transform.position;
            float minSqr = (hitPos - transform.position).sqrMagnitude;
            for (int i = 1; i < targets.Count; i++)
            {
                float sqr = (targets[i].Transform.position - transform.position).sqrMagnitude;
                if (sqr < minSqr)
                {
                    minSqr = sqr;
                    hitPos = targets[i].Transform.position;
                }
            }
            Explode(hitPos);
        }

        private void Explode(Vector3 hitPos)
        {
            if (_data.ExplosionRadius > 0f)
            {
                var splash = PhysicsRegistry.Instance.FindNearby(hitPos, _data.ExplosionRadius, EntityType.Monster);
                foreach (var target in splash)
                {
                    if (!target.IsAlive) continue;
                    Vector3 dir = (target.Transform.position - transform.position).normalized;
                    if (dir.sqrMagnitude < 0.0001f) dir = _direction;
                    target.TakeDamage(_damage, dir);
                }
            }
            Release();
        }

        /// <summary>飞满射程/命中后回收；幂等</summary>
        public void Release()
        {
            if (_released) return;
            _released = true;
            gameObject.SetActive(false);
            _pool?.Return(this);
        }

        private void OnDestroy()
        {
            _released = true;
            _pool = null;
        }
    }
}
```

> 注：`ProjectileRange` 字段已在 Task 4 的 `ProjectileSkillData.cs` 中定义（默认 15f），此处直接使用。

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.FireballProjectileTests`)
Expected: PASS（5/5）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs Assets/Scripts/Tests/Editor/FireballProjectileTests.cs
git commit -m "feat: fireball projectile — linear flight, explode-on-hit, pooled"
```

---

### Task 6: FireballSpawner + 测试

**Files:**
- Create: `Assets/Scripts/Tests/Editor/FireballSpawnerTests.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballSpawner.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class FireballSpawnerTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            FireballSpawner.DisposeForTests();
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static ProjectileSkillData CreateData(GameObject prefab)
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_projectilePrefab").objectReferenceValue = prefab;
            so.FindProperty("_projectileSpeed").floatValue = 18f;
            so.FindProperty("_projectileRange").floatValue = 15f;
            so.ApplyModifiedProperties();
            return data;
        }

        [Test]
        public void test_fireball_spawner_creates_flying_projectile_from_pool()
        {
            // Arrange
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var data = CreateData(prefab);

            // Act
            FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            // Assert（池懒初始化，预热实例未取出前不激活——活跃数 = 模板 + Spawn 的 1 个）
            Assert.AreEqual(2, Object.FindObjectsOfType<FireballProjectile>().Length);
            int moving = 0;
            foreach (var fb in Object.FindObjectsOfType<FireballProjectile>())
            {
                if (!fb.gameObject.activeSelf) continue;
                fb.Tick(0.5f);
                if (fb.transform.position.z > 5f) moving++;
            }
            Assert.AreEqual(1, moving, "应恰好生成 1 个飞行的火球");
        }

        [Test]
        public void test_fireball_spawner_skips_when_prefab_missing()
        {
            // Arrange（预制体为空）
            var data = CreateData(null);

            // Act（不抛异常，跳过）
            FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<FireballProjectile>().Length, "缺预制体不生成");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.FireballSpawnerTests`)
Expected: FAIL — 类型不存在。

- [ ] **Step 3: 实现**（`FireballSpawner.cs`，镜像 SwordQiSpawner）

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Projectile
{
    /// <summary>
    /// 火球生成入口：懒初始化对象池。缺配置/缺预制体时跳过并告警（不影响技能本体）。
    /// 池挂在场景容器 GameRoot 下（运行时创建，不落场景文件）；场景卸载守卫同剑气。
    /// </summary>
    public static class FireballSpawner
    {
        /// <summary>池预热实例数</summary>
        public const int PrewarmCount = 2;

        /// <summary>场景容器名</summary>
        private const string ContainerName = "GameRoot";

        private static ComponentPool<FireballProjectile> _pool;
        private static Transform _root;
        private static Transform _container;
        private static bool _ownsContainer;

        public static void Spawn(ProjectileSkillData data, DamageBlock damage, Vector3 origin, Vector3 direction)
        {
            if (data == null || data.ProjectilePrefab == null)
            {
                Debug.LogWarning("[FireballSpawner] data or prefab is null, skip spawn");
                return;
            }

            if (_root == null || _pool == null)
            {
                var prefabComp = data.ProjectilePrefab.GetComponent<FireballProjectile>();
                if (prefabComp == null)
                {
                    Debug.LogWarning("[FireballSpawner] prefab missing FireballProjectile component, skip spawn");
                    return;
                }

                if (_container == null)
                {
                    var go = GameObject.Find(ContainerName);
                    if (go == null)
                    {
                        go = new GameObject(ContainerName);
                        _ownsContainer = true;
                    }
                    _container = go.transform;
                }

                _root = new GameObject("FireballPool").transform;
                _root.SetParent(_container, false);
                _pool = new ComponentPool<FireballProjectile>(prefabComp, _root, PrewarmCount);
            }

            var fb = _pool.Get();
            fb.OnSpawn(data, damage, origin, direction, _pool);
        }

        /// <summary>测试专用：清空静态池状态（避免 EditMode 测试间串扰）</summary>
        public static void DisposeForTests()
        {
            if (_root != null)
                Object.DestroyImmediate(_root.gameObject);
            _pool = null;
            _root = null;
            if (_ownsContainer && _container != null)
                Object.DestroyImmediate(_container.gameObject);
            _container = null;
            _ownsContainer = false;
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.FireballSpawnerTests`)
Expected: PASS（2/2）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballSpawner.cs Assets/Scripts/Tests/Editor/FireballSpawnerTests.cs
git commit -m "feat: FireballSpawner — lazy pooled spawn entry (mirror SwordQiSpawner)"
```

---

### Task 7: SkillExecutor — 火球生成钩子 + FireballSpawnedEvent

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Events/FireballSpawnedEvent.cs`
- Create: `Assets/Scripts/Tests/Editor/SkillExecutorFireballTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorFireballTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;
        private int _spawnEventCount;

        [SetUp]
        public void SetUp()
        {
            _spawnEventCount = 0;
            EventBus.Subscribe<FireballSpawnedEvent>(OnFireballSpawned);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<FireballSpawnedEvent>(OnFireballSpawned);
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            FireballSpawner.DisposeForTests();
        }

        // 事件订阅必须用字段方法（lambda 两次引用不同，Unsubscribe 无法移除）
        private void OnFireballSpawned(FireballSpawnedEvent e) { _spawnEventCount++; }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static ProjectileSkillData CreateFireballData(GameObject prefab)
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_projectilePrefab").objectReferenceValue = prefab;
            so.FindProperty("_projectileSpeed").floatValue = 18f;
            so.FindProperty("_projectileRange").floatValue = 15f;
            so.ApplyModifiedProperties();
            TestHelpers.SetField(data, "_skillId", 20001);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(50f, 0f));
            return data;
        }

        private SkillExecutor CreateExecutor(SkillData data)
        {
            _ownerGo = new GameObject("FireballOwner");
            _ownerGo.transform.position = Vector3.zero;
            _ownerGo.transform.rotation = Quaternion.identity;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            return executor;
        }

        [Test]
        public void test_skill_executor_spawns_fireball_on_execution_entry()
        {
            // Arrange
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var executor = CreateExecutor(CreateFireballData(prefab));
            var before = Object.FindObjectsOfType<FireballProjectile>().Length;

            // Act（TryStart → Execution 同步生成）
            Assert.IsTrue(executor.TryStart());

            // Assert（池懒初始化：活跃数 = 模板 + 生成的 1 个）
            Assert.AreEqual(before + 1, Object.FindObjectsOfType<FireballProjectile>().Length,
                            "Execution 进入应触发火球生成");
            Assert.AreEqual(1, _spawnEventCount, "应发出 FireballSpawnedEvent 快照");
        }

        [Test]
        public void test_skill_executor_fireball_spawns_only_once_per_activation()
        {
            // Arrange
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var data = CreateFireballData(prefab);
            var executor = CreateExecutor(data);
            var before = Object.FindObjectsOfType<FireballProjectile>().Length;

            // Act（完成后再 TryStart：状态机已完成态拒绝重复启动（连击复用由协调器新建执行器），不得重复生成）
            executor.TryStart();
            executor.ForceComplete();
            bool second = executor.TryStart();

            // Assert
            Assert.IsFalse(second, "Completed 后不可重复启动同一执行器");
            Assert.AreEqual(before + 1, Object.FindObjectsOfType<FireballProjectile>().Length,
                            "每次施放恰好 1 个火球，不重复生成");
            Assert.AreEqual(1, _spawnEventCount, "事件只发一次");
        }

        [Test]
        public void test_skill_executor_no_fireball_without_projectile_data()
        {
            // Arrange（InstantSkillData 无投射物字段）
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 10002);
            var executor = CreateExecutor(data);

            // Act
            executor.TryStart();

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<FireballProjectile>().Length, "非投射物技能不生成火球");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorFireballTests`)
Expected: FAIL — 编译错误（事件类 `FireballSpawnedEvent` 与生成逻辑不存在）。

- [ ] **Step 3: 实现**

（1）`FireballSpawnedEvent.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    /// <summary>
    /// 火球生成事件（网络预留：与 SwordQiSpawnedEvent 一致，仅承载生成快照，当前无订阅者）。
    /// </summary>
    public class FireballSpawnedEvent : IEvent
    {
        public int CasterId;
        public int SkillId;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Speed;
        public float Range;
    }
}
```

（2）`SkillExecutor.cs`：

- 构造器缓存投射物数据：`private readonly ProjectileSkillData _projectileData;` + `private bool _projectileSpawned;`，构造器中 `_projectileData = data as ProjectileSkillData;`
- `TryStart()` 中复位：`_projectileSpawned = false;`（与 `_swordQiSpawned` 同处）
- `OnStateChanged` 新增分支（放在 Charged 处理之后）：

```csharp
            if (newState == SkillSubState.Execution && _projectileData != null && !_projectileSpawned)
            {
                _projectileSpawned = true;
                SpawnFireball();
            }
```

- 新私有方法（仿 OnSwordQiFrame 的伤害填充与方向处理）：

```csharp
        /// <summary>
        /// 火球生成：Execution 进入时同步生成（施法前摇由 Execution 动画时长表达）。
        /// 伤害从 SkillData.Damage 克隆并填充上下文，防共享实例被后续施放污染（同剑气）。
        /// </summary>
        private void SpawnFireball()
        {
            var damage = _skillData.Damage?.Clone();
            if (damage == null)
            {
                Debug.LogWarning("[SkillExecutor] Projectile skill has no DamageBlock, skip spawn");
                return;
            }

            SkillDamageHelper.FillDamageContext(damage, _skillData.SkillId, 1, GetEffect(), _owner.Stats);

            Vector3 origin = _owner.transform.position
                             + _owner.transform.TransformDirection(_projectileData.SpawnOffset);
            Vector3 dir = _owner.transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _owner.transform.forward;

            FireballSpawner.Spawn(_projectileData, damage, origin, dir);

            EventBus.Emit(new FireballSpawnedEvent
            {
                CasterId = _owner.transform.GetInstanceID(),
                SkillId = _skillData.SkillId,
                Origin = origin,
                Direction = dir,
                Speed = _projectileData.ProjectileSpeed,
                Range = _projectileData.ProjectileRange
            });
        }
```

- 顶部 `using` 增加：`using Hotfix.GameSystems.Skills.Runtime.Projectile;`

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorFireballTests`)
Expected: PASS（3/3）

Run: Unity MCP `tests-run` (EditMode) — 全量回归
Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Events/FireballSpawnedEvent.cs Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs Assets/Scripts/Tests/Editor/SkillExecutorFireballTests.cs
git commit -m "feat: fireball spawn hook on execution entry + FireballSpawnedEvent snapshot"
```

---

### Task 8: MeteorConfig + SkillData.Meteor hook

**Files:**
- Create: `Assets/Scripts/Tests/Editor/SkillDataMeteorTests.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/MeteorConfig.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillDataMeteorTests
    {
        [Test]
        public void test_skill_data_meteor_defaults_to_null()
        {
            // Arrange
            var data = ScriptableObject.CreateInstance<InstantSkillData>();

            // Assert（默认不发射陨石，与 SwordQi 同款语义）
            Assert.IsNull(data.Meteor);
        }

        [Test]
        public void test_skill_data_meteor_returns_assigned_config()
        {
            // Arrange
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_meteor", cfg);

            // Act & Assert
            Assert.AreSame(cfg, data.Meteor);
        }

        [Test]
        public void test_meteor_config_defaults()
        {
            // Arrange
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();

            // Assert（数值草稿默认值）
            Assert.AreEqual(0.8f, cfg.WarnDuration, 0.001f);
            Assert.AreEqual(4f, cfg.ExplosionRadius, 0.001f);
            Assert.AreEqual(1f, cfg.DamagePercent, 0.001f);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillDataMeteorTests`)
Expected: FAIL — 编译错误（类型不存在）。

- [ ] **Step 3: 实现**

（1）`MeteorConfig.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "MeteorConfig", menuName = "Game/Skills/Meteor Config")]
    public class MeteorConfig : ScriptableObject
    {
        [Header("=== Meteor ===")]
        [Tooltip("陨石预制体（根物体需含 MeteorStrike 组件；子物体预警红圈在落地时关闭）")]
        [SerializeField] private GameObject _prefab;
        public GameObject Prefab => _prefab;

        [Tooltip("预警持续时间(秒)，之后落地爆炸")]
        [SerializeField] private float _warnDuration = 0.8f;
        public float WarnDuration => _warnDuration;

        [Tooltip("落地爆炸半径(米)，检测中心在落点 +1m 脚踝高度")]
        [SerializeField] private float _explosionRadius = 4f;
        public float ExplosionRadius => _explosionRadius;

        [Tooltip("伤害倍率 (1.0 = 继承技能全额伤害)")]
        [SerializeField] private float _damagePercent = 1f;
        public float DamagePercent => _damagePercent;

        [Tooltip("落地瞬间播放的一次性爆炸特效（自动销毁，不走池）")]
        [SerializeField] private GameObject _impactVFX;
        public GameObject ImpactVFX => _impactVFX;

        [Tooltip("命中击退等效果（KnockbackForce > 0 时覆盖技能默认）")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;
    }
}
```

（2）`SkillData.cs` 追加（`_swordQi` 字段下方，`GetMainAnimationClip` 上方）：

```csharp
        [Header("=== Meteor ===")]
        [Tooltip("技能施放时附加的陨石配置（null = 不召唤陨石）")]
        [SerializeField] protected MeteorConfig _meteor;
        public MeteorConfig Meteor => _meteor;
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillDataMeteorTests`)
Expected: PASS（3/3）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/MeteorConfig.cs Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs Assets/Scripts/Tests/Editor/SkillDataMeteorTests.cs
git commit -m "feat: MeteorConfig + SkillData.Meteor hook (mirror SwordQi config pattern)"
```

---

### Task 9: MeteorStrike + 测试

**Files:**
- Create: `Assets/Scripts/Tests/Editor/MeteorStrikeTests.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorStrike.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MeteorStrikeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private readonly List<IDamageable> _registered = new List<IDamageable>();

        private class FakeDamageable : IDamageable
        {
            public bool IsAlive => true;
            public Transform Transform { get; }
            public int HitCount { get; private set; }
            public DamageBlock LastDamage;
            public FakeDamageable(Transform t) { Transform = t; }
            public void TakeDamage(DamageBlock d, Vector3 dir) { HitCount++; LastDamage = d; }
            public void TakeDamage(DamageBlock d, Vector3 dir, HitContact c) { TakeDamage(d, dir); }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var e in _registered)
                PhysicsRegistry.Instance.Unregister(e);
            _registered.Clear();
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private void Register(IDamageable target)
        {
            PhysicsRegistry.Instance.Register(target, EntityType.Monster);
            _registered.Add(target);
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static MeteorConfig CreateConfig(float warn, float radius)
        {
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_warnDuration").floatValue = warn;
            so.FindProperty("_explosionRadius").floatValue = radius;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private MeteorStrike CreateStrike(MeteorConfig cfg, Vector3 impactPoint)
        {
            var go = NewGo("Meteor").AddComponent<MeteorStrike>();
            go.OnSpawn(cfg, DamageBlock.CreateDefault(60f, 0f), impactPoint, null);
            return go;
        }

        [Test]
        public void test_meteor_no_explosion_before_warn_duration()
        {
            // Arrange
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(0f, 1f, 0f); // 落点 +1m
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act（0.5s，未到预警时长）
            strike.Tick(0.5f);

            // Assert
            Assert.AreEqual(0, target.HitCount, "预警期未结束不造成伤害");
            Assert.IsTrue(strike.gameObject.activeSelf);
        }

        [Test]
        public void test_meteor_explodes_at_impact_after_warn_duration()
        {
            // Arrange
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(0f, 1f, 0f);
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act（0.9s，预警结束）
            strike.Tick(0.9f);

            // Assert（落点 +1m 中心爆炸命中；回池）
            Assert.AreEqual(1, target.HitCount, "预警结束后落地爆炸");
            Assert.AreEqual(60f, target.LastDamage.CalculatedDamage, 0.001f);
            Assert.IsFalse(strike.gameObject.activeSelf, "爆炸后回池");
            strike.Tick(0.1f); // 回池后再步进不抛异常
        }

        [Test]
        public void test_meteor_explosion_center_is_one_meter_above_impact()
        {
            // Arrange（目标站在地面上，y=0；爆炸中心在落点 y+1 → 必须命中）
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(3.5f, 0f, 0f); // 水平 3.5m < 半径 4m
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act
            strike.Tick(0.9f);

            // Assert（水平距离 3.5m + 垂直差 1m → 距爆炸中心 √(3.5²+1²)≈3.64m < 4m）
            Assert.AreEqual(1, target.HitCount, "爆炸中心在脚踝高度，站地目标必须命中");
        }

        [Test]
        public void test_meteor_does_not_double_hit_same_target()
        {
            // Arrange
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(0f, 1f, 0f);
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act（多帧越过预警点）
            for (int i = 0; i < 20; i++)
                strike.Tick(0.1f);

            // Assert
            Assert.AreEqual(1, target.HitCount, "同一陨石同一目标只结算一次");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.MeteorStrikeTests`)
Expected: FAIL — 类型不存在。

- [ ] **Step 3: 实现**（`MeteorStrike.cs`，Update 时间累加，禁协程）

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Meteor
{
    /// <summary>
    /// 陨石：预警（红圈）→ 预警结束落地爆炸（落点 +1m 中心 AOE）→ 池化回收。
    /// Update 时间累加计时（池化对象禁用协程，防回池后泄漏）。
    /// </summary>
    public class MeteorStrike : MonoBehaviour
    {
        private MeteorConfig _config;
        private DamageBlock _damage;
        private Vector3 _impactPoint;
        private float _elapsed;
        private bool _exploded;
        private ComponentPool<MeteorStrike> _pool;
        private readonly System.Collections.Generic.HashSet<int> _hitIds = new();

        public void OnSpawn(MeteorConfig config, DamageBlock damage, Vector3 impactPoint,
                            ComponentPool<MeteorStrike> pool)
        {
            _config = config;
            _damage = damage;
            _impactPoint = impactPoint;
            _pool = pool;
            _exploded = false;
            _elapsed = 0f;
            _hitIds.Clear();
            transform.position = impactPoint;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>每帧步进（公开供 EditMode 测试手动驱动）</summary>
        public void Tick(float deltaTime)
        {
            if (_config == null || _exploded) return;

            _elapsed += deltaTime;
            if (_elapsed >= _config.WarnDuration)
                Explode();
        }

        private void Explode()
        {
            if (_exploded) return;

            // 爆炸检测中心：落点 +1m（脚踝高度），否则站地目标打不到
            Vector3 center = _impactPoint + Vector3.up * 1f;
            var targets = PhysicsRegistry.Instance.FindNearby(center, _config.ExplosionRadius, EntityType.Monster);
            foreach (var target in targets)
            {
                if (!target.IsAlive) continue;
                int id = target.Transform.GetInstanceID();
                if (!_hitIds.Add(id)) continue;

                Vector3 dir = (target.Transform.position - center).normalized;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
                target.TakeDamage(_damage, dir);
            }

            if (_config.ImpactVFX != null)
            {
                var vfx = Object.Instantiate(_config.ImpactVFX, _impactPoint, Quaternion.identity);
                Destroy(vfx, 1.5f);
            }

            Release();
        }

        /// <summary>爆炸后回收；幂等</summary>
        public void Release()
        {
            if (_exploded) return;
            _exploded = true;
            gameObject.SetActive(false);
            _pool?.Return(this);
        }

        private void OnDestroy()
        {
            _exploded = true;
            _pool = null;
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.MeteorStrikeTests`)
Expected: PASS（4/4）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorStrike.cs Assets/Scripts/Tests/Editor/MeteorStrikeTests.cs
git commit -m "feat: meteor strike — warn decal, delayed impact AOE at ankle height, pooled"
```

---

### Task 10: MeteorSpawner + 测试

**Files:**
- Create: `Assets/Scripts/Tests/Editor/MeteorSpawnerTests.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorSpawner.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MeteorSpawnerTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            MeteorSpawner.DisposeForTests();
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static MeteorConfig CreateConfig(GameObject prefab)
        {
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.ApplyModifiedProperties();
            return cfg;
        }

        [Test]
        public void test_meteor_spawner_creates_strike_from_pool()
        {
            // Arrange
            var prefab = NewGo("MeteorPrefab");
            prefab.AddComponent<MeteorStrike>();
            var cfg = CreateConfig(prefab);

            // Act
            MeteorSpawner.Spawn(cfg, DamageBlock.CreateDefault(60f, 0f), new Vector3(0f, 0f, 12f));

            // Assert（池懒初始化：活跃数 = 模板 + Spawn 的 1 个）
            Assert.AreEqual(2, Object.FindObjectsOfType<MeteorStrike>().Length);
            int active = 0;
            foreach (var s in Object.FindObjectsOfType<MeteorStrike>())
                if (s.gameObject.activeSelf) active++;
            Assert.AreEqual(1, active, "应恰好 1 个活跃预警");
        }

        [Test]
        public void test_meteor_spawner_skips_when_prefab_missing()
        {
            // Arrange（预制体为空）
            var cfg = CreateConfig(null);

            // Act（不抛异常，跳过）
            MeteorSpawner.Spawn(cfg, DamageBlock.CreateDefault(60f, 0f), Vector3.zero);

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<MeteorStrike>().Length, "缺预制体不生成");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.MeteorSpawnerTests`)
Expected: FAIL — 类型不存在。

- [ ] **Step 3: 实现**（`MeteorSpawner.cs`，镜像 FireballSpawner）

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Meteor
{
    /// <summary>
    /// 陨石生成入口：懒初始化对象池（镜像 SwordQiSpawner/FireballSpawner）。
    /// 池挂在场景容器 GameRoot 下；场景卸载守卫同剑气。
    /// </summary>
    public static class MeteorSpawner
    {
        public const int PrewarmCount = 1;

        private const string ContainerName = "GameRoot";

        private static ComponentPool<MeteorStrike> _pool;
        private static Transform _root;
        private static Transform _container;
        private static bool _ownsContainer;

        public static void Spawn(MeteorConfig config, DamageBlock damage, Vector3 impactPoint)
        {
            if (config == null || config.Prefab == null)
            {
                Debug.LogWarning("[MeteorSpawner] config or prefab is null, skip spawn");
                return;
            }

            if (_root == null || _pool == null)
            {
                var prefabComp = config.Prefab.GetComponent<MeteorStrike>();
                if (prefabComp == null)
                {
                    Debug.LogWarning("[MeteorSpawner] prefab missing MeteorStrike component, skip spawn");
                    return;
                }

                if (_container == null)
                {
                    var go = GameObject.Find(ContainerName);
                    if (go == null)
                    {
                        go = new GameObject(ContainerName);
                        _ownsContainer = true;
                    }
                    _container = go.transform;
                }

                _root = new GameObject("MeteorPool").transform;
                _root.SetParent(_container, false);
                _pool = new ComponentPool<MeteorStrike>(prefabComp, _root, PrewarmCount);
            }

            var strike = _pool.Get();
            strike.OnSpawn(config, damage, impactPoint, _pool);
        }

        /// <summary>测试专用：清空静态池状态</summary>
        public static void DisposeForTests()
        {
            if (_root != null)
                Object.DestroyImmediate(_root.gameObject);
            _pool = null;
            _root = null;
            if (_ownsContainer && _container != null)
                Object.DestroyImmediate(_container.gameObject);
            _container = null;
            _ownsContainer = false;
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.MeteorSpawnerTests`)
Expected: PASS（2/2）

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorSpawner.cs Assets/Scripts/Tests/Editor/MeteorSpawnerTests.cs
git commit -m "feat: MeteorSpawner — lazy pooled spawn entry (mirror FireballSpawner)"
```

---

### Task 11: SkillExecutor — 陨石生成钩子 + MeteorSpawnedEvent

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Events/MeteorSpawnedEvent.cs`
- Create: `Assets/Scripts/Tests/Editor/SkillExecutorMeteorTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorMeteorTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;
        private int _spawnEventCount;

        [SetUp]
        public void SetUp()
        {
            _spawnEventCount = 0;
            EventBus.Subscribe<MeteorSpawnedEvent>(OnMeteorSpawned);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<MeteorSpawnedEvent>(OnMeteorSpawned);
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            MeteorSpawner.DisposeForTests();
        }

        // 事件订阅必须用字段方法（lambda 两次引用不同，Unsubscribe 无法移除）
        private void OnMeteorSpawned(MeteorSpawnedEvent e) { _spawnEventCount++; }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static MeteorConfig CreateConfig(GameObject prefab)
        {
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_damagePercent").floatValue = 2.5f;
            so.ApplyModifiedProperties();
            return cfg;
        }

        /// <summary>找活跃（已取出未回池）的陨石实例——池预热实例不激活，需过滤</summary>
        private static MeteorStrike FindActiveStrike()
        {
            foreach (var s in Object.FindObjectsOfType<MeteorStrike>())
                if (s.gameObject.activeSelf) return s;
            return null;
        }

        private SkillExecutor CreateExecutor(InstantSkillData data)
        {
            _ownerGo = new GameObject("MeteorOwner");
            _ownerGo.transform.position = Vector3.zero;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            executor.SetTargetPosition(new Vector3(0f, 0f, 12f));
            return executor;
        }

        [Test]
        public void test_skill_executor_spawns_meteor_on_execution_entry()
        {
            // Arrange
            var prefab = NewGo("MeteorPrefab");
            prefab.AddComponent<MeteorStrike>();
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 30001);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(100f, 0f));
            TestHelpers.SetField(data, "_meteor", CreateConfig(prefab));
            var executor = CreateExecutor(data);

            // Act（TryStart → Execution 同步生成，落点 = SetTargetPosition）
            Assert.IsTrue(executor.TryStart());

            // Assert
            Assert.AreEqual(1, _spawnEventCount, "应发出 MeteorSpawnedEvent 快照");
            int active = 0;
            foreach (var s in Object.FindObjectsOfType<MeteorStrike>())
                if (s.gameObject.activeSelf) active++;
            Assert.AreEqual(1, active, "应生成 1 个预警");
        }

        [Test]
        public void test_skill_executor_meteor_applies_damage_percent()
        {
            // Arrange
            var prefab = NewGo("MeteorPrefab");
            prefab.AddComponent<MeteorStrike>();
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 30001);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(100f, 0f));
            TestHelpers.SetField(data, "_meteor", CreateConfig(prefab));
            var executor = CreateExecutor(data);

            // Act
            executor.TryStart();

            // Assert（活跃预警携带伤害 = 100 × 2.5 = 250）
            var strike = FindActiveStrike();
            Assert.IsNotNull(strike, "应生成 1 个活跃预警");
            Assert.AreEqual(250f,
                            ((DamageBlock)TestHelpers.GetField(strike, "_damage")).CalculatedDamage,
                            0.001f);
        }

        [Test]
        public void test_skill_executor_no_meteor_without_config()
        {
            // Arrange（无 Meteor 配置）
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 10002);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(10f, 0f));
            var executor = CreateExecutor(data);

            // Act
            executor.TryStart();

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<MeteorStrike>().Length, "无配置不生成陨石");
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorMeteorTests`)
Expected: FAIL — 编译错误（事件类 `MeteorSpawnedEvent` 与生成逻辑不存在）。

- [ ] **Step 3: 实现**

（1）`MeteorSpawnedEvent.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    /// <summary>
    /// 陨石生成事件（网络预留：与 SwordQiSpawnedEvent 一致，仅承载生成快照，当前无订阅者）。
    /// </summary>
    public class MeteorSpawnedEvent : IEvent
    {
        public int CasterId;
        public int SkillId;
        public Vector3 ImpactPoint;
    }
}
```

（2）`SkillExecutor.cs`：

- 构造器：`private bool _meteorSpawned;`（无缓存字段，直接读 `_skillData.Meteor`）
- `TryStart()` 复位：`_meteorSpawned = false;`
- `OnStateChanged` 新增分支（火球分支旁）：

```csharp
            if (newState == SkillSubState.Execution && _skillData.Meteor != null && !_meteorSpawned)
            {
                _meteorSpawned = true;
                SpawnMeteor();
            }
```

- 新私有方法：

```csharp
        /// <summary>
        /// 陨石生成：Execution 进入时同步生成预警（落点 = 施放时目标位置，缺失时面向前方 12m）。
        /// 伤害克隆 + 倍率，防共享实例污染（同剑气）。
        /// </summary>
        private void SpawnMeteor()
        {
            var cfg = _skillData.Meteor;
            var damage = _skillData.Damage?.Clone();
            if (damage == null)
            {
                Debug.LogWarning("[SkillExecutor] Meteor config set but skill has no DamageBlock, skip spawn");
                return;
            }

            SkillDamageHelper.FillDamageContext(damage, _skillData.SkillId, 1, GetEffect(), _owner.Stats);
            damage.CalculatedDamage *= cfg.DamagePercent;

            Vector3 impact = _targetPosition;
            if (impact.sqrMagnitude < 0.0001f)
                impact = _owner.transform.position + _owner.transform.forward * 12f;

            MeteorSpawner.Spawn(cfg, damage, impact);

            EventBus.Emit(new MeteorSpawnedEvent
            {
                CasterId = _owner.transform.GetInstanceID(),
                SkillId = _skillData.SkillId,
                ImpactPoint = impact
            });
        }
```

- 顶部 `using` 增加：`using Hotfix.GameSystems.Skills.Runtime.Meteor;`

- [ ] **Step 4: 运行确认通过**

Run: Unity MCP `tests-run` (EditMode, testClass=`GameSys.EditorTests.SkillExecutorMeteorTests`)
Expected: PASS（3/3）

Run: Unity MCP `tests-run` (EditMode) — 全量回归
Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Events/MeteorSpawnedEvent.cs Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs Assets/Scripts/Tests/Editor/SkillExecutorMeteorTests.cs
git commit -m "feat: meteor spawn hook on execution entry + MeteorSpawnedEvent snapshot"
```

---

### Task 12: 输入接入 — InputManager + Sys3CEntry（R 松开释放 + U 键）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

> 本任务依赖真实输入（`UnityInput` 静态包装），无自动化测试；验收 = Editor 内手动操作（Task 17）。

- [ ] **Step 1: InputManager 新增方法**（`IsSkill3Pressed` 下方）

```csharp
        /// <summary>
        /// 3技能按住（R键，持续状态）— 蓄力技能长按
        /// </summary>
        public bool IsSkill3Held()
        {
            return UnityInput.GetKey(KeyCode.R);
        }

        /// <summary>
        /// 4技能按下（U键）- 大招（移动端后续接 UI 按钮，模式同 SetDefendHeld）
        /// </summary>
        public bool IsSkillUltimatePressed()
        {
            bool pressed = UnityInput.GetKeyDown(KeyCode.U);
            if (pressed && !_skill4Consumed)
            {
                _skill4Consumed = true;
                return true;
            }
            return false;
        }
```

字段：`private bool _skill4Consumed;`（与 `_skill2Consumed`/`_skill3Consumed` 同声明处）。

- [ ] **Step 2: Sys3CEntry 接入**（`HandleInput` 内，`IsSkill3Pressed` 分支之后、`IsDefending` 判断块内）

```csharp
                // R 松开 → 蓄力技能提前释放（按住蓄力，松开发射）
                if (!_inputManager.IsSkill3Held())
                {
                    _skillCoordinator.TryReleaseCharge(GetSkillRId());
                }

                if (_inputManager.IsSkillUltimatePressed())
                {
                    int ultimateId = (int)Skills.Definition.SkillID.Ultimate;
                    if (ultimateId > 0)
                    {
                        var input = SkillInput.SkillToPosition(ultimateId,
                            transform.position + transform.forward * 12f);
                        _skillCoordinator.HandleInput(input);
                    }
                }
```

- [ ] **Step 3: 编译验证**

Run: Unity MCP `assets-refresh`（触发编译）
Expected: 无编译错误（console-get-logs 无 Error）

- [ ] **Step 4: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat: input wiring — R hold-to-charge release, U ultimate key"
```

---

### Task 13: Animator controller 改造（MCP animator-modify）

**Files:**
- Modify: `Assets/RpgDuo/Animator/Character3C.controller`

- [ ] **Step 1: 新增 2 个 Trigger 参数**

Run: `animator-modify` — animatorRef `{"assetPath": "Assets/RpgDuo/Animator/Character3C.controller"}`

```json
[
  {"type": "AddParameter", "parameterName": "ChargeBlast", "parameterType": "Trigger"},
  {"type": "AddParameter", "parameterName": "Ultimate", "parameterType": "Trigger"}
]
```

Expected: 成功；`animator-get-data` 参数列表含 ChargeBlast/Ultimate。

- [ ] **Step 2: 新增 2 个状态**

```json
[
  {"type": "AddState", "layerName": "Attack", "stateName": "SkillR_charge", "motionAssetPath": "Assets/RpgDuo/Animation/SwordAndShield/Idle_Battle_SwordAndShiled.fbx"},
  {"type": "AddState", "layerName": "Attack", "stateName": "Ult", "motionAssetPath": "Assets/RpgDuo/Animation/SwordAndShield/Attack03_SwordAndShiled.fbx"}
]
```

> 若 MCP 的 SetStateMotion/AddState 不接受 fbx 路径解析内嵌 clip，回退方案：script-execute 用 `AssetDatabase.LoadAllAssetsAtPath("Assets/RpgDuo/Animation/SwordAndShield/Idle_Battle_SwordAndShiled.fbx")` 取 `AnimationClip`（名含 "Idle_Battle"），对 controller 资产调用 `AnimatorController` API 设置 state.motion（见 Step 3 回退脚本）。

- [ ] **Step 3: 新增 4 条过渡**

```json
[
  {"type": "AddTransition", "layerName": "Attack", "sourceStateName": "AttackIdle", "destinationStateName": "SkillR_charge", "hasExitTime": false, "duration": 0.05, "conditions": [{"parameter": "ChargeBlast", "mode": "If", "threshold": 0}]},
  {"type": "AddTransition", "layerName": "Attack", "sourceStateName": "SkillR_charge", "destinationStateName": "AttackIdle", "hasExitTime": false, "duration": 0.05, "conditions": [{"parameter": "AttackState", "mode": "Equals", "threshold": 0}]},
  {"type": "AddTransition", "layerName": "Attack", "sourceStateName": "AttackIdle", "destinationStateName": "Ult", "hasExitTime": false, "duration": 0.05, "conditions": [{"parameter": "Ultimate", "mode": "If", "threshold": 0}]},
  {"type": "AddTransition", "layerName": "Attack", "sourceStateName": "Ult", "destinationStateName": "AttackIdle", "hasExitTime": false, "duration": 0.05, "conditions": [{"parameter": "AttackState", "mode": "Equals", "threshold": 0}]}
]
```

（若 Step 2 走了回退脚本，Step 3 的过渡也在同一脚本内用 `AnimatorStateTransition` API 完成。）

- [ ] **Step 4: 验证**

Run: `animator-get-data`（Attack 层）
Expected: 状态列表含 `SkillR_charge`、`Ult`；AttackIdle 出边含 ChargeBlast/Ultimate 条件过渡；新状态出边含 AttackState==0 → AttackIdle。

- [ ] **Step 5: 提交**

```bash
git add Assets/RpgDuo/Animator/Character3C.controller
git commit -m "feat: animator — ChargeBlast/Ultimate triggers + SkillR_charge/Ult states"
```

---

### Task 14: 技能资产创建（script-execute + 资产陷阱处理）

**Files:**
- Create: `Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset`、`ChargeFire_SkillR.asset`、`Meteor_Ultimate.asset`、`MeteorConfig_Fire.asset`

> ⚠️ 已知陷阱（memory: unity-mcp-script-execute-asset-trap）：script-execute 创建的 SO 资产可能不被正确反序列化——用"创建后立即加载并校验 `name` 与关键字段"判定，失败则删除 + 用 AssetDatabase 写回 + refresh 修复。

- [ ] **Step 1: 写资产创建脚本（script-execute，full code 模式）**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using UnityEditor;
using UnityEngine;

public static class CreateFireSkillAssets
{
    public static void Main()
    {
        // --- 火球术 (ProjectileSkillData, id=20001) ---
        var fireball = ScriptableObject.CreateInstance<ProjectileSkillData>();
        var soF = new SerializedObject(fireball);
        soF.FindProperty("_skillId").intValue = 20001;
        soF.FindProperty("_skillName").stringValue = "火球术";
        soF.FindProperty("_cooldown").floatValue = 3f;
        soF.FindProperty("_animatorTrigger").stringValue = "SkillQ";
        soF.FindProperty("_projectileSpeed").floatValue = 18f;
        soF.FindProperty("_projectileRange").floatValue = 15f;
        soF.FindProperty("_explosionRadius").floatValue = 1.5f;
        var dmgF = soF.FindProperty("_damage");
        dmgF.FindPropertyRelative("_baseDamage").floatValue = 20f;
        dmgF.FindPropertyRelative("_attackRatio").floatValue = 1f;
        dmgF.FindPropertyRelative("_scalingAttribute").enumValueIndex = (int)AttributeType.SpellPower;
        soF.ApplyModifiedProperties();
        AssetDatabase.CreateAsset(fireball, "Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset");

        // --- 蓄力炎爆 (ChargedSkillData, id=20002) ---
        var charge = ScriptableObject.CreateInstance<ChargedSkillData>();
        var soC = new SerializedObject(charge);
        soC.FindProperty("_skillId").intValue = 20002;
        soC.FindProperty("_skillName").stringValue = "蓄力炎爆";
        soC.FindProperty("_cooldown").floatValue = 6f;
        soC.FindProperty("_animatorTrigger").stringValue = "ChargeBlast";
        soC.FindProperty("_minChargeTime").floatValue = 0.3f;
        soC.FindProperty("_maxChargeTime").floatValue = 2f;
        soC.FindProperty("_chargeDamageCurve").animationCurveValue = AnimationCurve.Linear(0f, 1f, 1f, 1.5f);
        soC.FindProperty("_chargeAreaCurve").animationCurveValue = AnimationCurve.Linear(0f, 1f, 1f, 1.5f);
        soC.FindProperty("_canMoveWhileCharging").boolValue = true;
        soC.FindProperty("_canRotateWhileCharging").boolValue = true;
        soC.FindProperty("_shape/_targetType").enumValueIndex = (int)TargetType.AOE_Circle;
        soC.FindProperty("_shape/_areaRadius").floatValue = 2f;
        soC.FindProperty("_shape/_hitboxTimings").arraySize = 1;
        soC.FindProperty("_shape/_hitboxTimings").GetArrayElementAtIndex(0).floatValue = 0f;
        var dmgC = soC.FindProperty("_damage");
        dmgC.FindPropertyRelative("_baseDamage").floatValue = 40f;
        dmgC.FindPropertyRelative("_attackRatio").floatValue = 1.5f;
        dmgC.FindPropertyRelative("_scalingAttribute").enumValueIndex = (int)AttributeType.SpellPower;
        soC.ApplyModifiedProperties();
        AssetDatabase.CreateAsset(charge, "Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset");

        // --- 陨石术 (InstantSkillData, id=30001) + MeteorConfig ---
        var meteorCfg = ScriptableObject.CreateInstance<MeteorConfig>();
        AssetDatabase.CreateAsset(meteorCfg, "Assets/PreRes/SkillsCfg/MeteorConfig_Fire.asset");

        var meteor = ScriptableObject.CreateInstance<InstantSkillData>();
        var soM = new SerializedObject(meteor);
        soM.FindProperty("_skillId").intValue = 30001;
        soM.FindProperty("_skillName").stringValue = "陨石术";
        soM.FindProperty("_cooldown").floatValue = 25f;
        soM.FindProperty("_animatorTrigger").stringValue = "Ultimate";
        soM.FindProperty("_meteor").objectReferenceValue = meteorCfg;
        var dmgM = soM.FindProperty("_damage");
        dmgM.FindPropertyRelative("_baseDamage").floatValue = 80f;
        dmgM.FindPropertyRelative("_attackRatio").floatValue = 2.5f;
        dmgM.FindPropertyRelative("_scalingAttribute").enumValueIndex = (int)AttributeType.SpellPower;
        soM.ApplyModifiedProperties();
        AssetDatabase.CreateAsset(meteor, "Assets/PreRes/SkillsCfg/Meteor_Ultimate.asset");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
```

- [ ] **Step 2: 资产陷阱校验**

Run: `script-execute` body-only：

```csharp
var fb = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>("Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset");
var ch = AssetDatabase.LoadAssetAtPath<ChargedSkillData>("Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset");
var me = AssetDatabase.LoadAssetAtPath<InstantSkillData>("Assets/PreRes/SkillsCfg/Meteor_Ultimate.asset");
Debug.Log($"FB name={fb?.name} id={fb?.SkillId} speed={fb?.ProjectileSpeed} ratio={fb?.Damage?.AttackRatio}");
Debug.Log($"CH name={ch?.name} id={ch?.SkillId} trigger={ch?.AnimatorTrigger} curve={ch?.GetDamageScaleForCharge(1f)}");
Debug.Log($"ME name={me?.name} id={me?.SkillId} meteor={me?.Meteor != null}");
```

Expected: name 非空、id 正确（20001/20002/30001）、`GetDamageScaleForCharge(1f)` == 1.5、meteor 非 null。
**陷阱修复**：若 name 为空或字段丢失 → `AssetDatabase.DeleteAsset(path)` 后重新执行 Step 1 脚本 + `AssetDatabase.Refresh()`，再校验。

- [ ] **Step 3: 提交**

```bash
git add Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset Assets/PreRes/SkillsCfg/Meteor_Ultimate.asset Assets/PreRes/SkillsCfg/MeteorConfig_Fire.asset
git commit -m "feat: fire skill assets — Fireball_SkillQ / ChargeFire_SkillR / Meteor_Ultimate + MeteorConfig"
```

---

### Task 15: 运行时 prefab 创建（script-execute）

**Files:**
- Create: `Assets/PreRes/Skills/` 下 `Fireball.prefab`、`MeteorStrike.prefab`、`Explosion_Fireball.prefab`、`Explosion_Meteor.prefab`

- [ ] **Step 1: 创建脚本（script-execute，full code 模式）**

```csharp
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using UnityEditor;
using UnityEngine;

public static class CreateFireSkillPrefabs
{
    public static void Main()
    {
        const string dir = "Assets/PreRes/Skills";
        System.IO.Directory.CreateDirectory(dir);

        // --- 火球：球体 + 拖尾粒子 + FireballProjectile ---
        var fb = new GameObject("Fireball");
        var fbSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fbSphere.name = "Core";
        fbSphere.transform.SetParent(fb.transform, false);
        fbSphere.transform.localScale = Vector3.one * 0.35f;
        Object.DestroyImmediate(fbSphere.GetComponent<Collider>());
        var fbMat = new Material(Shader.Find("Sprites/Default"));
        fbMat.color = new Color(1f, 0.55f, 0.15f);
        fbSphere.GetComponent<Renderer>().sharedMaterial = fbMat;
        var trail = fb.AddComponent<TrailRenderer>();
        trail.time = 0.4f; trail.startWidth = 0.3f; trail.endWidth = 0f;
        trail.material = fbMat;
        fb.AddComponent<FireballProjectile>();
        PrefabUtility.SaveAsPrefabAsset(fb, dir + "/Fireball.prefab");

        // --- 陨石预警：地面红圈（Quad 圆形贴图占位用粒子）+ MeteorStrike ---
        var meteor = new GameObject("MeteorStrike");
        var warn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        warn.name = "WarnDecal";
        warn.transform.SetParent(meteor.transform, false);
        warn.transform.localScale = new Vector3(8f, 0.05f, 8f); // 半径 4m 预警圈
        Object.DestroyImmediate(warn.GetComponent<Collider>());
        var warnMat = new Material(Shader.Find("Sprites/Default"));
        warnMat.color = new Color(1f, 0.2f, 0.1f, 0.35f);
        warn.GetComponent<Renderer>().sharedMaterial = warnMat;
        meteor.AddComponent<MeteorStrike>();
        PrefabUtility.SaveAsPrefabAsset(meteor, dir + "/MeteorStrike.prefab");

        // --- 爆炸特效（一次性，自动销毁）---
        var boom = new GameObject("Explosion_Fireball");
        var psBoom = boom.AddComponent<ParticleSystem>();
        var mainB = psBoom.main;
        mainB.startLifetime = 0.6f; mainB.startSpeed = 4f;
        mainB.startSize = 0.5f; mainB.maxParticles = 40;
        var emitB = psBoom.emission; emitB.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
        var shapeB = psBoom.shape; shapeB.shapeType = ParticleSystemShapeType.Sphere;
        var colB = psBoom.colorOverLifetime;
        colB.enabled = true;
        var gradB = new Gradient();
        gradB.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 0.2f), 0f),
                              new GradientColorKey(new Color(1f, 0.2f, 0f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colB.color = gradB;
        var psBoomAuto = boom.AddComponent<AutoDestroy>();
        psBoomAuto.Lifetime = 1.5f;
        PrefabUtility.SaveAsPrefabAsset(boom, dir + "/Explosion_Fireball.prefab");

        var boomM = new GameObject("Explosion_Meteor");
        var psM = boomM.AddComponent<ParticleSystem>();
        var mainM = psM.main;
        mainM.startLifetime = 1f; mainM.startSpeed = 8f;
        mainM.startSize = 1.2f; mainM.maxParticles = 120;
        var emitM = psM.emission; emitM.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
        var shapeM = psM.shape; shapeM.shapeType = ParticleSystemShapeType.Sphere;
        var colM = psM.colorOverLifetime;
        colM.enabled = true;
        var gradM = new Gradient();
        gradM.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f),
                              new GradientColorKey(new Color(1f, 0.25f, 0f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colM.color = gradM;
        var psMAuto = boomM.AddComponent<AutoDestroy>();
        psMAuto.Lifetime = 2f;
        PrefabUtility.SaveAsPrefabAsset(boomM, dir + "/Explosion_Meteor.prefab");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private class AutoDestroy : MonoBehaviour
    {
        public float Lifetime = 1.5f;
        private void Update() { Lifetime -= Time.deltaTime; if (Lifetime <= 0f) Destroy(gameObject); }
    }
}
```

- [ ] **Step 2: 校验**

Run: `assets-find`（filter `Fireball t:Prefab`，searchInFolders `["Assets/PreRes/Skills"]`）
Expected: `Fireball.prefab` / `MeteorStrike.prefab` / `Explosion_Fireball.prefab` / `Explosion_Meteor.prefab` 均存在；`gameobject-find` 确认 Fireball 根含 `FireballProjectile` 组件、MeteorStrike 根含 `MeteorStrike` 组件。

- [ ] **Step 3: 把 prefab 引用写入资产**（script-execute body-only，Assets 路径）

```csharp
var fb = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>("Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset");
var fbPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PreRes/Skills/Fireball.prefab");
var boomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PreRes/Skills/Explosion_Fireball.prefab");
var soF = new SerializedObject(fb);
soF.FindProperty("_projectilePrefab").objectReferenceValue = fbPrefab;
soF.FindProperty("_presentation/_releaseVFX").objectReferenceValue = boomPrefab;
soF.ApplyModifiedProperties(); AssetDatabase.SaveAssets();

var cfg = AssetDatabase.LoadAssetAtPath<MeteorConfig>("Assets/PreRes/SkillsCfg/MeteorConfig_Fire.asset");
var meteorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PreRes/Skills/MeteorStrike.prefab");
var meteorBoom = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PreRes/Skills/Explosion_Meteor.prefab");
var soCfg = new SerializedObject(cfg);
soCfg.FindProperty("_prefab").objectReferenceValue = meteorPrefab;
soCfg.FindProperty("_impactVFX").objectReferenceValue = meteorBoom;
soCfg.ApplyModifiedProperties(); AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
```

- [ ] **Step 4: 提交**

```bash
git add Assets/PreRes/Skills/
git commit -m "feat: fire skill prefabs — fireball / meteor strike / explosion VFX + asset wiring"
```

---

### Task 16: 场景接线 — 新技能资产挂到角色

**Files:**
- Modify: 玩家角色所在场景（含 `Sys3CEntry` 的 GameObject，`_characterSkills` 数组）

- [ ] **Step 1: 定位玩家对象**

Run: `assets-find`（filter `Sys3CEntry t:Prefab` 或 `t:Scene`）+ `scene-list-opened`
Expected: 找到含 `Sys3CEntry` 的玩家 prefab/场景对象（主玩法场景或玩家 prefab）。

- [ ] **Step 2: 替换 `_characterSkills` 引用**（script-execute body-only，先读后写）

```csharp
// 读取当前引用，按 SkillId 映射替换（保留 LightAttack/HeavyAttack）
var fb = AssetDatabase.LoadAssetAtPath<SkillData>("Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset");
var ch = AssetDatabase.LoadAssetAtPath<SkillData>("Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset");
var me = AssetDatabase.LoadAssetAtPath<SkillData>("Assets/PreRes/SkillsCfg/Meteor_Ultimate.asset");
// 场景内查找玩家（名称含 Player 或挂 Sys3CEntry 的根对象），逐项替换：
//   20001 → fb（替换原 Instant_SkillQ）
//   20002 → ch（替换原 Spin_SkillR）
//   append → me（新增 30001）
// 写回后 EditorUtility.SetDirty + 保存场景（scene-save）
```

> 具体对象查找与数组写入需按实际场景结构微调；验收标准：玩家 `_characterSkills` 含 20001/20002/30001 三个新资产，且 20001/20002 原资产已移出。

- [ ] **Step 3: 保存场景**

Run: `scene-save`
Expected: 场景保存成功（tests-run 前置要求：无脏场景）。

- [ ] **Step 4: 提交**

```bash
git add <场景文件或玩家 prefab>
git commit -m "feat: wire fire skills into player _characterSkills (Q/R/Ult slots)"
```

---

### Task 17: 全量回归 + 手动验收

**Files:** 无

- [ ] **Step 1: 全量 EditMode 测试**

Run: Unity MCP `tests-run`（EditMode，无过滤）
Expected: 全部 PASS（既有 19 个测试文件 + 本批新增 10 个）

- [ ] **Step 2: 手动验收（Editor Play 模式）**

| 检查项 | 操作 | 预期 |
|---|---|---|
| Q 火球 | 按 Q | 施法动画 → 胸前出火球直线飞行 → 命中怪物爆炸，怪物扣血 |
| Q 冷却 | 连续按 Q | 3s 冷却，冷却中无球生成 |
| R 蓄力 | 按住 R 1s 后松开 | 蓄力中可移动/转向、武器发光；松手爆炸，伤害 ≈ 1.25x |
| R 满蓄力 | 按住 R 2s | 自动释放，1.5x 伤害 + 3m 半径 |
| R 轻点 | 快速点按 R | 立即释放弱火（不锁 2s） |
| R 打断 | 蓄力中被怪物打 | 技能中断 + 武器光熄灭 |
| U 陨石 | 按 U | 面前 12m 红圈预警 0.8s → 陨石爆炸 4m AOE |
| 动画 | 各技能施放 | SkillR_charge 站姿循环、Ult/火球 Attack03 单次，结束后回 AttackIdle |
| 回归 | 普攻连击/重击 | 剑气照常，未受替换影响 |

- [ ] **Step 3: 记录结果**

将验收结果记入本计划任务勾选状态；若有失败项，按 `superpowers:systematic-debugging` 流程修复并补回归测试后再提交。

---

## 自审记录（对照 spec）

- **Spec 覆盖**：§3 三技能数值 → Task 14 资产 + Task 2/5/9 结算；§4.1 Release 链路 → Task 1/3/12；§4.2 结算修复 → Task 2；§4.3 火球 → Task 4-7；§4.4 陨石 → Task 8-11；§4.5 输入/Animator → Task 12/13；§5 表现层 → Task 15；§6 边界 → Task 2（打断）、Task 5/9（池化守卫）、Task 12（蓄力移动由现有 CanMove 支持，零改动）；§7 测试 → 各任务内嵌 + Task 17。
- **占位符扫描**：Task 16 场景对象定位为"按实际场景结构微调"——唯一非完全确定步骤，已在任务内给出验收标准与回退方式（允许执行时按结构调整，属合理弹性）。
- **类型一致性**：`ChargeProgressAtRelease`/`Release()`/`TryReleaseCharge` 名称跨 Task 1/2/3/12 一致；`ProjectileSkillData` 新字段在 Task 4/5/7/14/15 引用一致（含 Task 5 补充的 `_projectileRange`）；`MeteorConfig` 字段在 Task 8/9/10/11/14/15 一致。
- **风险提示**：`SkillExecutorFireballTests`/`SkillExecutorMeteorTests` 事件订阅一律用字段方法（已直接落在测试代码中，lambda 无法正确退订）；`_shape/_hitboxTimings` 空数组 = 蓄力期不结算（Task 2 守卫 + Task 14 资产配置 `{0f}` 协同生效）；`FireballProjectile` 无私有 `_projectileRange` 前需确认 Task 4 已提交（计划内已按 Task 4 先行排布）。
