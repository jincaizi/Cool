# 精力系统实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为本地玩家增加塞尔达式精力：奔跑/举盾/持续型技能按秒消耗；归零力竭并中断动作，回复到 20% 才能重新开始；角色右上方显示绿色粗环。

**架构：** `StaminaSystem` 是纯 C# 资源模型（无 Unity UI 依赖）；技能持续扣精与激活门禁放在 `SkillCoordinator`；奔跑/举盾/优先级在 `Sys3CEntry.UpdateStamina` 统一裁决；`StaminaRingRenderer` 由 `EntityDisplayManager` 持有，用 UGUI 运行时生成的白环贴图绘制。

**技术栈：** Unity 2022.3.25f1、NUnit EditMode/PlayMode、UGUI（Screen Space Overlay + CanvasScaler）、Unity MCP `tests-run` / `assets-refresh` / `script-execute` / `screenshot-game-view`、纯 C#（无 HybridCLR）。

**规格：** `docs/superpowers/specs/2026-10-18-stamina-system-design.md`

**执行前置：**
- 所有打开的 Unity 场景必须已保存（`tests-run` 会拒绝 dirty scene）。
- 每个任务的测试命令都在仓库根目录 `E:/CodeForJob/Cool` 执行。
- 修改 `.cs` 后先让 Unity 编译（`assets-refresh` 或等编辑器自动刷新），再跑测试。
- 修改 `.asset` 后必须 `assets-refresh`，并用资产测试钉死值。

---

## 文件结构

### 新增

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaSystem.cs` | `StaminaConfig` + `StaminaSystem`：数值、消耗、回复、力竭、Refill |
| `Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaRingRenderer.cs` | 运行时生成环贴图、UGUI 环、淡入淡出、红闪、世界投影定位 |
| `Assets/Scripts/Tests/Editor/StaminaSystemTests.cs` | 纯逻辑单元测试 |
| `Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs` | 技能扣精/门禁/打断测试 |
| `Assets/Scripts/Tests/Editor/StaminaSkillAssetTests.cs` | 三个技能资产耗率断言 |
| `Assets/Scripts/Tests/PlayMode/StaminaRingTests.cs` | 环的填充/可见/位置/raycast/红闪/重绑/解绑/出屏 |
| `Assets/Scripts/Tests/PlayMode/StaminaIntegrationTests.cs` | `Sys3CEntry` 优先级、奔跑/举盾/死亡/复活、输入门禁 |

### 修改

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs` | 新增 `_staminaDrainPerSecond` |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs` | `InterruptionSource.StaminaExhausted` |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs` | 放行力竭打断 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs` | 放行力竭打断 + 优先级 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs` | 注入 `StaminaSystem`、激活门禁、持续扣精、耗尽处理 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs` | `SprintAllowed`、`SetSprintHeld` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 创建/注入/优先级/防御锁存/复活回满/懒绑定/解绑 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs` | 环的序列化参数、Bind/Unbind、LateUpdate、Cleanup |
| `Assets/PreRes/SkillsCfg/Spin_SkillR.asset` | `_staminaDrainPerSecond: 20` |
| `Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset` | `_staminaDrainPerSecond: 15` |
| `Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset` | `_staminaDrainPerSecond: 15` |

---

## 任务 1：StaminaSystem 纯逻辑

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaSystem.cs`
- 测试：`Assets/Scripts/Tests/Editor/StaminaSystemTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/StaminaSystemTests.cs`：

```csharp
using Hotfix.GameSystems.Stamina;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StaminaSystemTests
    {
        private const float Dt = 1f / 60f;

        private static StaminaSystem Create() => new StaminaSystem(StaminaConfig.Default);

        private static void TickFor(StaminaSystem stamina, float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < frames; i++) stamina.Tick(Dt);
        }

        private static void DrainToExhaustion(StaminaSystem stamina)
        {
            for (int i = 0; i < 400 && !stamina.IsExhausted; i++)
            {
                stamina.TryConsume(20f * Dt);
                stamina.Tick(Dt);
            }
            Assert.IsTrue(stamina.IsExhausted, "前置条件：应已力竭");
        }

        [Test]
        public void test_stamina_sprint_rate_empties_in_five_seconds()
        {
            var stamina = Create();
            int frames = 0;
            while (!stamina.IsExhausted && frames < 400)
            {
                stamina.TryConsume(20f * Dt);
                stamina.Tick(Dt);
                frames++;
            }

            Assert.IsTrue(stamina.IsExhausted);
            Assert.AreEqual(0f, stamina.Current, 0.001f);
            Assert.LessOrEqual(Mathf.Abs(frames - 300), 1, "20/s 抽空 100 应约 5.0s");
        }

        [Test]
        public void test_stamina_exhausted_blocks_start_until_twenty_percent()
        {
            var stamina = Create();
            DrainToExhaustion(stamina);
            Assert.IsFalse(stamina.CanStartConsuming);

            TickFor(stamina, 1.30f);
            Assert.Less(stamina.Current, 20f);
            Assert.IsFalse(stamina.CanStartConsuming, "未到 20% 前不能开始消耗动作");

            TickFor(stamina, 0.10f);
            Assert.GreaterOrEqual(stamina.Current, 20f);
            Assert.IsFalse(stamina.IsExhausted);
            Assert.IsTrue(stamina.CanStartConsuming);
        }

        [Test]
        public void test_stamina_regen_starts_after_delay()
        {
            var stamina = Create();
            stamina.TryConsume(10f);
            float afterConsume = stamina.Current;

            TickFor(stamina, 0.70f);
            Assert.AreEqual(afterConsume, stamina.Current, 0.001f, "0.75s 延迟内不得回复");

            TickFor(stamina, 0.10f);
            Assert.Greater(stamina.Current, afterConsume, "延迟结束后应开始回复");
        }

        [Test]
        public void test_stamina_consume_attempt_resets_regen_delay_even_when_insufficient()
        {
            var stamina = Create();
            stamina.TryConsume(95f);
            TickFor(stamina, 0.70f);

            Assert.IsFalse(stamina.TryConsume(10f));
            Assert.AreEqual(0f, stamina.Current, 0.001f);

            TickFor(stamina, 0.70f);
            Assert.AreEqual(0f, stamina.Current, 0.001f, "失败尝试也必须重置回复延迟");

            TickFor(stamina, 0.10f);
            Assert.Greater(stamina.Current, 0f);
        }

        [Test]
        public void test_stamina_try_consume_insufficient_clamps_to_zero_and_returns_false()
        {
            var stamina = Create();
            stamina.TryConsume(95f);

            Assert.IsFalse(stamina.TryConsume(10f));
            Assert.AreEqual(0f, stamina.Current, 0.001f);
            Assert.IsTrue(stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_try_consume_exact_remaining_triggers_exhaustion()
        {
            var stamina = Create();
            stamina.TryConsume(95f);

            Assert.IsFalse(stamina.TryConsume(5f), "恰好扣到 0 也算力竭");
            Assert.AreEqual(0f, stamina.Current, 0.001f);
            Assert.IsTrue(stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_zero_consume_does_not_reset_regen_delay()
        {
            var stamina = Create();
            stamina.TryConsume(10f);
            TickFor(stamina, 0.70f);

            Assert.IsTrue(stamina.TryConsume(0f));
            TickFor(stamina, 0.10f);
            Assert.Greater(stamina.Current, 90f, "0 消耗不得重置延迟");
        }

        [Test]
        public void test_stamina_refill_restores_full_and_clears_exhausted()
        {
            var stamina = Create();
            DrainToExhaustion(stamina);

            stamina.Refill();

            Assert.AreEqual(stamina.Max, stamina.Current, 0.001f);
            Assert.IsFalse(stamina.IsExhausted);
            Assert.IsTrue(stamina.CanStartConsuming);
        }

        [Test]
        public void test_stamina_non_finite_consume_is_noop()
        {
            var stamina = Create();
            float before = stamina.Current;

            Assert.IsTrue(stamina.TryConsume(float.NaN));
            Assert.IsTrue(stamina.TryConsume(float.PositiveInfinity));

            Assert.AreEqual(before, stamina.Current, 0.001f);
            Assert.IsFalse(stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_non_finite_and_negative_dt_is_ignored()
        {
            var stamina = Create();
            stamina.TryConsume(10f);
            float before = stamina.Current;

            stamina.Tick(float.NaN);
            stamina.Tick(-1f);

            Assert.AreEqual(before, stamina.Current, 0.001f);
        }

        [Test]
        public void test_stamina_construct_clamps_non_finite_config()
        {
            var stamina = new StaminaSystem(new StaminaConfig
            {
                Max = float.NaN,
                SprintDrainPerSecond = 20f,
                DefendDrainPerSecond = 10f,
                RegenPerSecond = float.NaN,
                RegenDelay = float.NaN,
                ExhaustRecoverRatio = float.NaN
            });

            Assert.AreEqual(StaminaConfig.Default.Max, stamina.Max, 0.001f);

            stamina.TryConsume(10f);
            TickFor(stamina, 0.80f);
            Assert.Greater(stamina.Current, 90f, "非有限回复配置应回退到默认值");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"StaminaSystemTests"}' --timeout 240000
```

预期：编译失败，`Hotfix.GameSystems.Stamina` / `StaminaSystem` / `StaminaConfig` 不存在。

- [ ] **步骤 3：实现 StaminaSystem**

创建 `Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaSystem.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Stamina
{
    public struct StaminaConfig
    {
        public float Max;
        public float SprintDrainPerSecond;
        public float DefendDrainPerSecond;
        public float RegenPerSecond;
        public float RegenDelay;
        public float ExhaustRecoverRatio;

        public static StaminaConfig Default => new StaminaConfig
        {
            Max = 100f,
            SprintDrainPerSecond = 20f,
            DefendDrainPerSecond = 10f,
            RegenPerSecond = 100f / 3f,
            RegenDelay = 0.75f,
            ExhaustRecoverRatio = 0.2f
        };
    }

    /// <summary>
    /// 精力资源模型（纯 C#，无 UI 依赖）。调用方每帧先 TryConsume，最后 Tick 一次。
    /// </summary>
    public sealed class StaminaSystem
    {
        private const float FullEpsilon = 0.001f;
        private const float MaxDeltaTime = 0.1f;

        private readonly float _max;
        private readonly float _regenPerSecond;
        private readonly float _regenDelay;
        private readonly float _exhaustRecoverRatio;

        private float _current;
        private float _sinceLastConsume;
        private bool _exhausted;

        public StaminaSystem(StaminaConfig config)
        {
            var defaults = StaminaConfig.Default;
            _max = FiniteOrDefault(config.Max, defaults.Max, 1f);
            _regenPerSecond = FiniteOrDefault(config.RegenPerSecond, defaults.RegenPerSecond, 0f);
            _regenDelay = FiniteOrDefault(config.RegenDelay, defaults.RegenDelay, 0f);
            _exhaustRecoverRatio = Mathf.Clamp01(
                FiniteOrDefault(config.ExhaustRecoverRatio, defaults.ExhaustRecoverRatio, 0f));

            _current = _max;
            _sinceLastConsume = float.PositiveInfinity;
        }

        public float Max => _max;
        public float Current => _current;
        public float Ratio => _max > 0f ? Mathf.Clamp01(_current / _max) : 0f;
        public bool IsExhausted => _exhausted;
        public bool IsFull => _current >= _max - FullEpsilon;
        public bool CanStartConsuming => !_exhausted && _current > 0f;

        /// <summary>
        /// 扣精。非有限或 &lt;=0 的 amount 是 no-op（不重置回复延迟）。
        /// 恰好扣到 0 也视为力竭并返回 false（调用方必须立即停止动作）。
        /// </summary>
        public bool TryConsume(float amount)
        {
            if (!IsFinite(amount) || amount <= 0f) return true;

            _sinceLastConsume = 0f;
            if (_current <= amount)
            {
                _current = 0f;
                _exhausted = true;
                return false;
            }

            _current -= amount;
            return true;
        }

        /// <summary>每帧调用一次；消耗/失败尝试之后调用。</summary>
        public void Tick(float deltaTime)
        {
            float dt = IsFinite(deltaTime) && deltaTime > 0f
                ? Mathf.Min(deltaTime, MaxDeltaTime)
                : 0f;
            if (dt <= 0f) return;

            _sinceLastConsume += dt;
            if (_sinceLastConsume < _regenDelay) return;

            if (_current < _max)
                _current = Mathf.Min(_max, _current + _regenPerSecond * dt);

            if (_exhausted && _current >= _max * _exhaustRecoverRatio)
                _exhausted = false;
        }

        /// <summary>复活时回满并清除力竭。</summary>
        public void Refill()
        {
            _current = _max;
            _exhausted = false;
            _sinceLastConsume = 0f;
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float FiniteOrDefault(float value, float fallback, float min)
            => IsFinite(value) ? Mathf.Max(min, value) : fallback;
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
unity-mcp-cli run-tool assets-refresh --path "E:/CodeForJob/Cool" --timeout 240000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"StaminaSystemTests"}' --timeout 240000
```

预期：全部 PASS（11 条）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaSystem.cs Assets/Scripts/Tests/Editor/StaminaSystemTests.cs
git commit -m "feat(stamina): 新增 StaminaSystem 纯逻辑与单元测试"
```

---

## 任务 2：力竭打断来源

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs`
- 测试：`Assets/Scripts/Tests/Editor/SkillInterruptionMatrixSpinTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/SkillStateMachineSpinTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `SkillInterruptionMatrixSpinTests.cs` 末尾加入：

```csharp
[Test]
public void test_skill_spin_stamina_exhausted_is_interruptible()
{
    var matrix = new SkillInterruptionMatrix();
    Assert.IsTrue(matrix.CanBeInterrupted(CreateSpinData(), InterruptionSource.StaminaExhausted),
        "力竭必须能打断旋转，无视霸体");
}
```

在 `SkillStateMachineSpinTests.cs` 末尾加入：

```csharp
[Test]
public void test_skill_state_machine_stamina_exhausted_interrupts()
{
    var data = ScriptableObject.CreateInstance<SpinSkillData>();
    TestHelpers.SetField(data, "_skillId", 20002);
    TestHelpers.SetField(data, "_skillType", SkillType.Spin);

    var machine = new SkillStateMachine(data);
    Assert.IsTrue(machine.TryStart());
    Assert.AreEqual(SkillSubState.Spinning, machine.CurrentState);

    Assert.IsTrue(machine.Interrupt(InterruptionSource.StaminaExhausted));
    Assert.AreEqual(SkillSubState.Cancelled, machine.CurrentState);
}
```

> 若 `SkillStateMachineSpinTests` 没有 `using Hotfix.GameSystems.Skills.Data;` / `Definition` / `Runtime`，补齐。

- [ ] **步骤 2：运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"SkillInterruptionMatrixSpinTests"}' --timeout 240000
```

预期：编译失败，`InterruptionSource.StaminaExhausted` 不存在。

- [ ] **步骤 3：实现枚举与两处放行**

`SkillState.cs` 的 `InterruptionSource` 末尾追加：

```csharp
TimeOut,            // 超时
StaminaExhausted    // 精力力竭（无视霸体）
```

`SkillInterruptionMatrix.CanBeInterrupted` 方法开头加入：

```csharp
if (source == InterruptionSource.StaminaExhausted) return true;
```

`SkillInterruptionMatrix.GetInterruptionPriority` 的 switch 加入：

```csharp
InterruptionSource.StaminaExhausted => 85,
```

`SkillStateMachine.CanBeInterrupted` 的 switch 加入：

```csharp
InterruptionSource.StaminaExhausted => true,
```

- [ ] **步骤 4：运行测试验证通过**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"SkillInterruptionMatrixSpinTests"}' --timeout 240000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"SkillStateMachineSpinTests"}' --timeout 240000
```

预期：全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs \
        Assets/Scripts/Tests/Editor/SkillInterruptionMatrixSpinTests.cs \
        Assets/Scripts/Tests/Editor/SkillStateMachineSpinTests.cs
git commit -m "feat(stamina): 新增力竭打断来源并放行状态机/矩阵"
```

---

## 任务 3：技能持续扣精与激活门禁

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`
- 测试：`Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Stamina;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillCoordinatorStaminaTests
    {
        private GameObject _ownerGo;

        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { }
            protected override float GetCurrentTime() => Now;
        }

        private static void InjectMachine(SkillExecutor executor, FakeTimeMachine machine)
        {
            var oldMachine = TestHelpers.GetField(executor, "_stateMachine");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            string[] events =
            {
                "_onStateChanged", "_onHitboxFrame", "_onHitConfirm",
                "_onSkillCompleted", "_onSkillInterrupted"
            };
            foreach (var name in events)
            {
                var field = typeof(SkillStateMachine).GetField(name, flags);
                Assert.IsNotNull(field, name);
                field.SetValue(machine, field.GetValue(oldMachine));
            }
            TestHelpers.SetField(executor, "_stateMachine", machine);
        }

        private ChargedSkillData CreateChargedData(float drain = 15f)
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            TestHelpers.SetField(data, "_skillId", 20004);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            TestHelpers.SetField(data, "_staminaDrainPerSecond", drain);
            TestHelpers.SetField(data, "_skillType", SkillType.Charged);
            return data;
        }

        private SpinSkillData CreateSpinData(float drain = 20f)
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_skillId", 20002);
            TestHelpers.SetField(data, "_minDuration", 1f);
            TestHelpers.SetField(data, "_maxDuration", 3f);
            TestHelpers.SetField(data, "_staminaDrainPerSecond", drain);
            TestHelpers.SetField(data, "_skillType", SkillType.Spin);
            return data;
        }

        private ChanneledSkillData CreateChanneledData(float drain = 20f)
        {
            var data = ScriptableObject.CreateInstance<ChanneledSkillData>();
            TestHelpers.SetField(data, "_skillId", 20005);
            TestHelpers.SetField(data, "_castTime", 0.2f);
            TestHelpers.SetField(data, "_channelDuration", 2f);
            TestHelpers.SetField(data, "_staminaDrainPerSecond", drain);
            TestHelpers.SetField(data, "_skillType", SkillType.Channeled);
            return data;
        }

        private InstantSkillData CreateInstantData()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 20001);
            TestHelpers.SetField(data, "_skillType", SkillType.Instant);
            return data;
        }

        private (SkillCoordinator, StaminaSystem, SkillExecutor, FakeTimeMachine)
            CreateWithExecutor(SkillData data)
        {
            _ownerGo = new GameObject("StaminaSkillOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(data);

            var stamina = new StaminaSystem(StaminaConfig.Default);
            coordinator.SetStamina(stamina);

            var executor = new SkillExecutor(owner, data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            Assert.IsTrue(executor.TryStart());
            TestHelpers.SetField(coordinator, "_currentSkill", executor);
            return (coordinator, stamina, executor, machine);
        }

        private (SkillCoordinator, StaminaSystem, SkillExecutor, FakeTimeMachine) CreateAtCharging()
        {
            var tuple = CreateWithExecutor(CreateChargedData());
            tuple.Item4.Now = 0.15f;
            tuple.Item3.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, tuple.Item3.CurrentSubState);
            return tuple;
        }

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
        }

        [Test]
        public void test_skill_charged_drains_only_during_charging()
        {
            var (coordinator, stamina, _, _) = CreateAtCharging();
            float before = stamina.Current;

            coordinator.Update(0.1f);

            Assert.AreEqual(before - 15f * 0.1f, stamina.Current, 0.01f);
        }

        [Test]
        public void test_skill_casting_does_not_drain()
        {
            var (coordinator, stamina, executor, machine) = CreateWithExecutor(CreateChargedData());
            machine.Now = 0.05f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Casting, executor.CurrentSubState);

            float before = stamina.Current;
            coordinator.Update(0.1f);

            Assert.AreEqual(before, stamina.Current, 0.001f);
        }

        [Test]
        public void test_skill_execution_recovery_skill_drain_is_zero()
        {
            var (coordinator, stamina, executor, machine) = CreateAtCharging();
            machine.Now = 2.2f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);

            float before = stamina.Current;
            coordinator.Update(0.1f);

            Assert.AreEqual(before, stamina.Current, 0.001f);
        }

        [Test]
        public void test_skill_charged_at_zero_stamina_auto_releases_and_reports_cast()
        {
            var (coordinator, stamina, executor, _) = CreateAtCharging();
            stamina.TryConsume(99f); // 剩 1
            int released = 0;
            coordinator.OnChargeReleased += (_, _, _) => released++;

            coordinator.Update(0.1f);

            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);
            Assert.AreEqual(1, released, "力竭自动释放也必须上报一次");
        }

        [Test]
        public void test_skill_spin_at_zero_stamina_interrupts_despite_super_armor()
        {
            var (coordinator, stamina, executor, _) = CreateWithExecutor(CreateSpinData());
            Assert.AreEqual(SkillSubState.Spinning, executor.CurrentSubState);
            stamina.TryConsume(99f);

            coordinator.Update(0.1f);

            Assert.AreEqual(SkillSubState.Cancelled, executor.CurrentSubState,
                "旋转霸体不得阻止力竭打断");
        }

        [Test]
        public void test_skill_channeled_at_zero_stamina_interrupts()
        {
            var (coordinator, stamina, executor, machine) = CreateWithExecutor(CreateChanneledData());
            machine.Now = 0.3f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Channeling, executor.CurrentSubState);
            stamina.TryConsume(99f);

            coordinator.Update(0.1f);

            Assert.AreEqual(SkillSubState.Cancelled, executor.CurrentSubState);
        }

        [Test]
        public void test_skill_combo_zero_drain_activates_while_exhausted()
        {
            _ownerGo = new GameObject("StaminaSkillOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(CreateInstantData());
            var stamina = new StaminaSystem(StaminaConfig.Default);
            coordinator.SetStamina(stamina);
            stamina.TryConsume(100f);

            coordinator.HandleInput(SkillInput.SkillToPosition(20001, Vector3.forward));

            Assert.IsNotNull(coordinator.CurrentSkill, "0 消耗技能在力竭时仍可施放");
        }

        [Test]
        public void test_skill_sustained_with_drain_cannot_activate_while_exhausted()
        {
            _ownerGo = new GameObject("StaminaSkillOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(CreateChargedData());
            var stamina = new StaminaSystem(StaminaConfig.Default);
            coordinator.SetStamina(stamina);
            stamina.TryConsume(100f);

            coordinator.HandleInput(SkillInput.SkillToPosition(20004, Vector3.forward));

            Assert.IsNull(coordinator.CurrentSkill, "有消耗的持续技能在力竭时必须拒绝激活");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"SkillCoordinatorStaminaTests"}' --timeout 240000
```

预期：编译失败，`StaminaDrainPerSecond` / `SetStamina` 不存在。

- [ ] **步骤 3：新增 SkillData 字段**

在 `SkillData.cs` 的 `=== Cost ===` 区、`StaminaCost` 之后加入：

```csharp
[Tooltip("持续型技能（蓄力/引导/旋转）每秒消耗的精力；0 = 不消耗")]
[SerializeField] protected float _staminaDrainPerSecond;
public float StaminaDrainPerSecond => _staminaDrainPerSecond;
```

- [ ] **步骤 4：修改 SkillCoordinator**

文件顶部加入：

```csharp
using Hotfix.GameSystems.Stamina;
```

在字段区加入：

```csharp
private StaminaSystem _stamina;
```

在构造函数之后加入：

```csharp
/// <summary>可选注入；不注入时技能系统行为完全不变（测试/无精力宿主）。</summary>
public void SetStamina(StaminaSystem stamina) => _stamina = stamina;
```

替换 `HasEnoughResources`：

```csharp
private bool HasEnoughResources(SkillData skillData)
{
    if (_stamina == null) return true;
    if (!IsSustained(skillData) || skillData.StaminaDrainPerSecond <= 0f) return true;
    return _stamina.CanStartConsuming;
}

private static bool IsSustained(SkillData data) =>
    data is ChargedSkillData
    || data is ChanneledSkillData
    || data is SpinSkillData
    || (data is ProjectileSkillData projectile && projectile.MaxChargeTime > 0f);
```

在 `Update(float deltaTime)` 中，把：

```csharp
_cooldownManager.Update(deltaTime);
```

之前插入：

```csharp
UpdateSustainedStamina(deltaTime);
```

在 `HasEnoughResources` 附近加入：

```csharp
private void UpdateSustainedStamina(float deltaTime)
{
    var skill = _currentSkill; // 抓局部：完成回调可能把字段置空
    if (skill == null || !skill.IsActive || _stamina == null) return;

    var sub = skill.CurrentSubState;
    if (sub != SkillSubState.Charging
        && sub != SkillSubState.Channeling
        && sub != SkillSubState.Spinning)
        return;

    float rate = skill.Data.StaminaDrainPerSecond;
    if (rate <= 0f) return;

    float staminaDt = float.IsFinite(deltaTime)
        ? Mathf.Clamp(deltaTime, 0f, 0.1f)
        : 0f;
    if (!_stamina.TryConsume(rate * staminaDt))
        HandleStaminaDepleted(skill, sub);
}

private void HandleStaminaDepleted(SkillExecutor skill, SkillSubState sub)
{
    bool resolved = sub switch
    {
        SkillSubState.Charging => TryReleaseCharge(skill.SkillId),
        SkillSubState.Channeling or SkillSubState.Spinning
            => skill.TryInterrupt(InterruptionSource.StaminaExhausted),
        _ => false
    };

    // 保险丝：两处放行是主防线；万一未来改动让打断被拒，
    // ForceComplete 防技能卡在持续态、每帧扣 0 精、玩家被软锁。
    if (!resolved) skill.ForceComplete();
}
```

> `Mathf` 需要 `using UnityEngine;`（文件已有）。

- [ ] **步骤 5：运行测试验证通过**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"SkillCoordinatorStaminaTests"}' --timeout 240000
```

预期：全部 PASS。

- [ ] **步骤 6：回归既有技能测试**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"SkillCoordinatorChargeTests"}' --timeout 240000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"SkillCoordinatorSpinTests"}' --timeout 240000
```

预期：全部 PASS（不注入 `StaminaSystem` 时行为不变）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs \
        Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs
git commit -m "feat(stamina): 技能持续扣精、激活门禁与力竭处理"
```

---

## 任务 4：精力环渲染与 EntityDisplayManager 绑定

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaRingRenderer.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`
- 测试：`Assets/Scripts/Tests/PlayMode/StaminaRingTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/PlayMode/StaminaRingTests.cs`：

```csharp
using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Stamina;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameSys.PlayModeTests
{
    public class StaminaRingTests
    {
        private GameObject _canvasGo;
        private GameObject _cameraGo;
        private GameObject _targetGo;
        private Canvas _canvas;
        private Camera _camera;
        private StaminaSystem _stamina;
        private StaminaRingRenderer _ring;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _canvasGo = new GameObject("StaminaRingTestCanvas");
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasGo.AddComponent<CanvasScaler>();
            _canvasGo.AddComponent<GraphicRaycaster>();

            _cameraGo = new GameObject("StaminaRingTestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 1f, -5f);
            _camera.transform.rotation = Quaternion.identity;

            _targetGo = new GameObject("StaminaRingTarget");
            _targetGo.transform.position = Vector3.zero;

            _stamina = new StaminaSystem(StaminaConfig.Default);
            _ring = new StaminaRingRenderer(_canvas);
            _ring.Bind(_targetGo.transform, _stamina, () => true);
            yield return null; // 等 CanvasScaler 生效
        }

        [TearDown]
        public void TearDown()
        {
            _ring?.Cleanup();
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            if (_targetGo != null) Object.Destroy(_targetGo);
        }

        private GameObject Root =>
            (GameObject)typeof(StaminaRingRenderer)
                .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_ring);

        [Test]
        public void test_stamina_ring_fill_matches_ratio()
        {
            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);

            Assert.AreEqual(0.6f, _ring.FillAmount, 0.01f);
        }

        [Test]
        public void test_stamina_ring_hidden_at_full_visible_when_not_full()
        {
            _ring.Tick(_camera, 0.3f);
            Assert.IsFalse(_ring.IsVisible, "满精力应隐藏");

            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsVisible, "未满应显示");
        }

        [Test]
        public void test_stamina_ring_position_is_right_of_head_and_scales_with_canvas()
        {
            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);

            Vector3 head = _targetGo.transform.position + Vector3.up * 1.7f;
            Vector3 screen = _camera.WorldToScreenPoint(head);
            float scale = _canvas.scaleFactor;

            Assert.AreEqual(screen.x + 64f * scale, Root.transform.position.x, 1f);
            Assert.AreEqual(screen.y + 24f * scale, Root.transform.position.y, 1f);
        }

        [Test]
        public void test_stamina_ring_images_disable_raycast()
        {
            var images = Root.GetComponentsInChildren<Image>();
            Assert.IsNotEmpty(images);
            foreach (var image in images)
                Assert.IsFalse(image.raycastTarget, "精力环不得拦截触屏/鼠标");
        }

        [Test]
        public void test_stamina_ring_exhaust_flash_triggers_once()
        {
            _stamina.TryConsume(100f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsFlashing, "力竭上升沿应闪红");

            _ring.Tick(_camera, 0.4f);
            Assert.IsFalse(_ring.IsFlashing, "0.35s 后应停止");

            _ring.Tick(_camera, 0.1f);
            Assert.IsFalse(_ring.IsFlashing, "力竭持续期间不得重复闪");
        }

        [Test]
        public void test_stamina_ring_rebind_resets_visual_state()
        {
            _stamina.TryConsume(100f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsFlashing);

            var fresh = new StaminaSystem(StaminaConfig.Default);
            _ring.Bind(_targetGo.transform, fresh, () => true);

            Assert.AreEqual(1f, _ring.FillAmount, 0.001f);
            Assert.AreEqual(0f, _ring.Alpha, 0.001f);
            Assert.IsFalse(_ring.IsFlashing);
            Assert.IsFalse(_ring.IsVisible);
        }

        [Test]
        public void test_stamina_ring_unbind_ignores_other_owner()
        {
            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsVisible);

            var other = new GameObject("OtherOwner");
            _ring.Unbind(other.transform);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsVisible, "别的 Transform 不得解绑当前环");
            Object.Destroy(other);

            _ring.Unbind(_targetGo.transform);
            _ring.Tick(_camera, 0.1f);
            Assert.IsFalse(_ring.IsVisible);
        }

        [Test]
        public void test_stamina_ring_hides_when_anchor_offscreen()
        {
            _stamina.TryConsume(40f);
            _targetGo.transform.position = new Vector3(0f, 0f, -10f); // 相机背后
            _ring.Tick(_camera, 0.1f);

            Assert.IsFalse(_ring.IsVisible);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"PlayMode","testClass":"StaminaRingTests"}' --timeout 240000
```

预期：编译失败，`StaminaRingRenderer` 不存在。

- [ ] **步骤 3：实现 StaminaRingRenderer**

创建 `Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaRingRenderer.cs`：

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.Stamina
{
    /// <summary>
    /// 角色右上方的精力环：运行时生成白环贴图，Image.Filled 径向填充。
    /// 由 EntityDisplayManager 持有；每帧 Tick 读取 StaminaSystem。
    /// </summary>
    public sealed class StaminaRingRenderer
    {
        private const float FadeInSeconds = 0.1f;
        private const float FadeOutSeconds = 0.25f;
        private const float FlashSeconds = 0.35f;
        private const int TextureResolution = 128;

        private static readonly Color FillColor = new Color(0.365f, 1f, 0.416f, 1f); // #5DFF6A
        private static readonly Color TrackColor = new Color(0.04f, 0.08f, 0.05f, 0.55f);
        private static readonly Color FlashColor = new Color(1f, 0.25f, 0.2f, 1f);

        private readonly Canvas _canvas;
        private readonly float _headHeight;
        private readonly Vector2 _offset;

        private readonly GameObject _root;
        private readonly RectTransform _rect;
        private readonly CanvasGroup _group;
        private readonly Image _fill;
        private readonly Sprite _sprite;

        private Transform _owner;
        private StaminaSystem _stamina;
        private Func<bool> _visible;
        private float _alpha;
        private float _flashTimer;
        private bool _wasExhausted;
        private bool _active;

        public StaminaRingRenderer(Canvas canvas, float diameter = 64f, float ringWidth = 10f,
            Vector2? offset = null, float headHeight = 1.7f)
        {
            _canvas = canvas;
            _headHeight = Mathf.Max(0f, headHeight);
            _offset = offset ?? new Vector2(64f, 24f);

            float size = Mathf.Max(8f, diameter);
            float thickness = Mathf.Clamp(ringWidth, 1f, size * 0.45f);

            _root = new GameObject("StaminaRing", typeof(RectTransform));
            _root.transform.SetParent(canvas.transform, false);

            _rect = (RectTransform)_root.transform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = new Vector2(size, size);

            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _sprite = CreateRingSprite(size, thickness);
            CreateImage("Track", Image.Type.Simple, TrackColor);
            _fill = CreateImage("Fill", Image.Type.Filled, FillColor);
            _fill.fillMethod = Image.FillMethod.Radial360;
            _fill.fillOrigin = (int)Image.Origin360.Top;
            _fill.fillClockwise = false;
            _fill.fillAmount = 1f;

            _root.SetActive(false);
        }

        public float FillAmount => _fill != null ? _fill.fillAmount : 0f;
        public bool IsVisible => _active && _alpha > 0.001f;
        public float Alpha => _alpha;
        public bool IsFlashing => _flashTimer > 0f;

        public void Bind(Transform owner, StaminaSystem stamina, Func<bool> visible)
        {
            _owner = owner;
            _stamina = stamina;
            _visible = visible;

            _alpha = 0f;
            _flashTimer = 0f;
            _wasExhausted = stamina != null && stamina.IsExhausted;
            _group.alpha = 0f;
            _fill.color = FillColor;
            _fill.fillAmount = stamina != null ? stamina.Ratio : 0f;
            _root.SetActive(false);
            _active = false;
        }

        public void Unbind(Transform owner)
        {
            if (_owner != owner) return;
            _owner = null;
            _stamina = null;
            _visible = null;
            HideImmediate();
        }

        public void Tick(Camera camera, float deltaTime)
        {
            // 先判空/谓词，再访问任何可能已销毁的对象
            if (_owner == null || _stamina == null || camera == null
                || (_visible != null && !_visible()))
            {
                HideImmediate();
                return;
            }

            Vector3 world = _owner.position + Vector3.up * _headHeight;
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f
                || screen.x < 0f || screen.x > Screen.width
                || screen.y < 0f || screen.y > Screen.height)
            {
                HideImmediate();
                return;
            }

            float dt = !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;

            float ratio = _stamina.Ratio;
            bool notFull = ratio < 1f - 0.001f;
            float targetAlpha = notFull ? 1f : 0f;
            float duration = targetAlpha > _alpha ? FadeInSeconds : FadeOutSeconds;
            _alpha = Mathf.MoveTowards(_alpha, targetAlpha, duration > 0f ? dt / duration : 1f);

            if (_stamina.IsExhausted && !_wasExhausted)
                _flashTimer = FlashSeconds;
            _wasExhausted = _stamina.IsExhausted;
            if (_flashTimer > 0f) _flashTimer = Mathf.Max(0f, _flashTimer - dt);

            if (_alpha > 0f)
            {
                if (!_root.activeSelf)
                {
                    _root.SetActive(true);
                    _root.transform.SetAsLastSibling();
                }
                _active = true;
            }
            else
            {
                _root.SetActive(false);
                _active = false;
                return;
            }

            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rect.position = screen + new Vector3(_offset.x * scale, _offset.y * scale, 0f);
            _fill.fillAmount = ratio;
            _fill.color = _flashTimer > 0f ? FlashColor : FillColor;
            _group.alpha = _alpha;
        }

        public void Cleanup()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_sprite != null)
            {
                var texture = _sprite.texture;
                UnityEngine.Object.Destroy(_sprite);
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
        }

        private void HideImmediate()
        {
            _alpha = 0f;
            _flashTimer = 0f;
            _active = false;
            if (_group != null) _group.alpha = 0f;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            if (_stamina != null) _wasExhausted = _stamina.IsExhausted;
        }

        private Image CreateImage(string name, Image.Type type, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var image = go.AddComponent<Image>();
            image.sprite = _sprite;
            image.type = type;
            image.color = color;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        private static Sprite CreateRingSprite(float diameter, float ringWidth)
        {
            float thickness = TextureResolution * (ringWidth / diameter);
            float outer = TextureResolution * 0.5f - 1f;
            float inner = Mathf.Max(0f, outer - thickness);

            var texture = new Texture2D(TextureResolution, TextureResolution,
                TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[TextureResolution * TextureResolution];
            for (int y = 0; y < TextureResolution; y++)
            {
                for (int x = 0; x < TextureResolution; x++)
                {
                    float dx = x + 0.5f - TextureResolution * 0.5f;
                    float dy = y + 0.5f - TextureResolution * 0.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(outer - distance + 0.5f)
                                * Mathf.Clamp01(distance - inner + 0.5f);
                    pixels[y * TextureResolution + x] =
                        new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture,
                new Rect(0f, 0f, TextureResolution, TextureResolution),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
```

- [ ] **步骤 4：修改 EntityDisplayManager**

文件顶部加入：

```csharp
using System;
using Hotfix.GameSystems.Stamina;
```

字段区加入：

```csharp
[Header("Stamina Ring")]
[SerializeField] private float _staminaRingDiameter = 64f;
[SerializeField] private float _staminaRingWidth = 10f;
[SerializeField] private Vector2 _staminaRingOffset = new Vector2(64f, 24f);
[SerializeField] private float _staminaRingHeadHeight = 1.7f;

private StaminaRingRenderer _staminaRing;
```

新增方法：

```csharp
public void BindStamina(Transform owner, StaminaSystem stamina, Func<bool> visible)
{
    if (_staminaRing == null)
    {
        _staminaRing = new StaminaRingRenderer(
            _canvas, _staminaRingDiameter, _staminaRingWidth,
            _staminaRingOffset, _staminaRingHeadHeight);
    }
    _staminaRing.Bind(owner, stamina, visible);
}

public void UnbindStamina(Transform owner)
{
    _staminaRing?.Unbind(owner);
}
```

`LateUpdate` 中 `_nameplate.Tick(_camera);` 之后加入：

```csharp
_staminaRing?.Tick(_camera, Time.deltaTime);
```

`OnDestroy` 中 `_damageScreenEffect?.Cleanup();` 之后加入：

```csharp
_staminaRing?.Cleanup();
```

- [ ] **步骤 5：运行测试验证通过**

```bash
unity-mcp-cli run-tool assets-refresh --path "E:/CodeForJob/Cool" --timeout 240000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"PlayMode","testClass":"StaminaRingTests"}' --timeout 240000
```

预期：全部 PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaRingRenderer.cs \
        Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs \
        Assets/Scripts/Tests/PlayMode/StaminaRingTests.cs
git commit -m "feat(stamina): 世界锚定精力环渲染与 EntityDisplayManager 绑定"
```

---

## 任务 5：奔跑/举盾/优先级接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 测试：`Assets/Scripts/Tests/PlayMode/StaminaIntegrationTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/PlayMode/StaminaIntegrationTests.cs`：

```csharp
using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Stamina;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class StaminaIntegrationTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private Hotfix.GameSystems.Sys3C.Character.CharacterController _cc;
        private InputManager _input;

        private sealed class FakeInputAdapter : IInputAdapter
        {
            public string AdapterName => "Fake";
            public Vector3 GetMoveInput() => Vector3.forward;
            public Vector2 GetCameraRotationInput() => Vector2.zero;
            public bool HasMoveInput() => true;
            public bool HasCameraRotationInput() => false;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab");
            _player = Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            _cc = _entry.CoreController;
            _input = _player.GetComponent<InputManager>();
            Assert.IsNotNull(_entry.Stamina);
            Assert.IsNotNull(_cc);
            Assert.IsNotNull(_input);
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null) Object.DestroyImmediate(_player);
        }

        private void SetData(System.Func<CharacterData, CharacterData> mutate)
        {
            var field = typeof(Hotfix.GameSystems.Sys3C.Character.CharacterController)
                .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(_cc, mutate((CharacterData)field.GetValue(_cc)));
        }

        private void InvokeUpdateStamina(float deltaTime)
        {
            var method = typeof(Sys3CEntry)
                .GetMethod("UpdateStamina", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "UpdateStamina 不存在");
            method.Invoke(_entry, new object[] { deltaTime });
        }

        [Test]
        public void test_stamina_sprint_drains_when_grounded_and_unlocked()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = false;

            InvokeUpdateStamina(1f); // 内部钳到 0.1s

            Assert.AreEqual(98f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_stamina_sprint_does_not_drain_in_air()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = false; return d; });
            _cc.LockMovement = false;

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_stamina_sprint_does_not_drain_when_locked()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = true;

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_stamina_sprint_does_not_drain_in_hit_layer()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = false;
            _entry.Controller.Coordinator.SetActiveLayer(LayerType.Hit);

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
            _entry.Controller.Coordinator.SetActiveLayer(LayerType.Base);
        }

        [Test]
        public void test_stamina_defend_drains_and_forced_drop()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.IsGrounded = true; return d; });
            Assert.IsTrue(_cc.TryEnterDefend());

            InvokeUpdateStamina(1f);
            Assert.AreEqual(99f, _entry.Stamina.Current, 0.01f, "举盾 10/s × 0.1s");

            _entry.Stamina.TryConsume(98.5f); // 99 → 剩 0.5，下一次 1.0 的消耗扣不动
            InvokeUpdateStamina(1f);

            Assert.IsFalse(_cc.IsDefending, "力竭必须强制收盾");
            Assert.IsTrue(_entry.Stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_dead_does_not_drain()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = false;
            typeof(Sys3CEntry).GetField("_isDead", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_entry, true);

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
            typeof(Sys3CEntry).GetField("_isDead", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_entry, false);
        }

        [Test]
        public void test_stamina_respawn_refills()
        {
            _entry.Stamina.TryConsume(50f);
            Assert.AreEqual(50f, _entry.Stamina.Current, 0.01f);

            _entry.ApplyServerRespawn(100f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
            Assert.IsFalse(_entry.Stamina.IsExhausted);
        }

        [Test]
        public void test_input_sprint_gate_respects_allowed()
        {
            _input.SetAdapter(new FakeInputAdapter());
            _input.SetSprintHeld(true);

            _input.SprintAllowed = true;
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint);

            _input.SprintAllowed = false;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint);
        }
    }
}
```

> 执行复审修复时追加（`fd87066`）：`test_stamina_defend_cannot_start_while_exhausted`——力竭时按住防御不进入、松手再按仍不进入、`Refill` 后松开再按可正常起盾；通过反射 `InvokeHandleInput` 调用私有 `HandleInput`，不依赖地面/整帧 `Update`。这正是起盾条件必须包含 `_stamina.CanStartConsuming` 的回归保护。

- [ ] **步骤 2：运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"PlayMode","testClass":"StaminaIntegrationTests"}' --timeout 240000
```

预期：编译失败，`Sys3CEntry.Stamina` / `UpdateStamina` / `InputManager.SprintAllowed` / `SetSprintHeld` 不存在。

- [ ] **步骤 3：修改 InputManager**

在 `SprintSpeed`/`MoveSpeed` 属性附近加入：

```csharp
/// <summary>力竭门禁：false 时 GetMoveCommand 不会产出冲刺。</summary>
public bool SprintAllowed { get; set; } = true;

private bool _sprintHeldFromUI;

/// <summary>测试/未来 UI 挂钩（与 SetDefendHeld 同形）；不新增手机按钮。</summary>
public void SetSprintHeld(bool held)
{
    _sprintHeldFromUI = held;
}
```

> `SetSprintHeld` 是为了让 PlayMode 集成测试能模拟冲刺（`Input.GetKey` 在测试里不可注入），同时给未来手机冲刺按钮留同形入口；它不改变 PC 输入，也不新增任何 UI。

把 `IsSprintHeld` 改为：

```csharp
public bool IsSprintHeld()
{
    return _sprintHeldFromUI || UnityInput.GetKey(KeyCode.LeftShift);
}
```

把 `GetMoveCommand` 中的：

```csharp
bool sprintHeld = IsSprintHeld();
```

改为：

```csharp
bool sprintHeld = IsSprintHeld() && SprintAllowed;
```

- [ ] **步骤 4：修改 Sys3CEntry**

文件顶部加入：

```csharp
using Hotfix.GameSystems.Stamina;
using Hotfix.GameSystems.Nameplate;
```

字段区加入：

```csharp
[Header("Stamina")]
[SerializeField] private float _maxStamina = 100f;
[SerializeField] private float _sprintStaminaPerSecond = 20f;
[SerializeField] private float _defendStaminaPerSecond = 10f;
[SerializeField] private float _staminaRegenPerSecond = 100f / 3f;
[SerializeField] private float _staminaRegenDelay = 0.75f;
[SerializeField, Range(0f, 1f)] private float _staminaExhaustRecoverRatio = 0.2f;

private StaminaSystem _stamina;
private bool _defendStaminaLatch;
private bool _staminaBound;

public StaminaSystem Stamina => _stamina;
```

在 `Start()` 的**第一行**创建系统（早于引用判空）：

```csharp
_stamina = new StaminaSystem(new StaminaConfig
{
    Max = _maxStamina,
    SprintDrainPerSecond = _sprintStaminaPerSecond,
    DefendDrainPerSecond = _defendStaminaPerSecond,
    RegenPerSecond = _staminaRegenPerSecond,
    RegenDelay = _staminaRegenDelay,
    ExhaustRecoverRatio = _staminaExhaustRecoverRatio
});
```

在 `_skillCoordinator.SetDashComponent(_dashComponent);` 之后加入：

```csharp
_skillCoordinator.SetStamina(_stamina);
```

在 `Update()` 开头、`TickRespawn(Time.deltaTime);` 之后加入：

```csharp
TryBindStaminaUi();
```

在 `_inputManager.Update();` 之后、`HandleInput();` 之前加入：

```csharp
_inputManager.SprintAllowed = _stamina.CanStartConsuming;
```

在 `_skillCoordinator.Update(Time.deltaTime);` 之后、`_dashComponent.Update();` 之前加入：

```csharp
UpdateStamina(Time.deltaTime);
```

替换 `HandleInput()` 的防御块为：

```csharp
if (_inputManager.IsDefendHeld())
{
    if (!_defendStaminaLatch && _stamina.CanStartConsuming
        && _fsmManager.CanDefend && _cc.TryEnterDefend())
    {
        _fsmManager.EnterDefend();
    }
    else if (!_stamina.CanStartConsuming)
    {
        _defendStaminaLatch = true;
    }
}
else
{
    _defendStaminaLatch = false;
    if (_cc.IsDefending)
    {
        _cc.TryExitDefend();
        _fsmManager.ExitDefend();
    }
}
```

新增方法（放在 `HandleInput` 附近）：

```csharp
private void TryBindStaminaUi()
{
    if (_staminaBound) return;
    var display = EntityDisplayManager.Instance;
    if (display == null) return;

    display.BindStamina(transform, _stamina, () => !_isDead && isActiveAndEnabled);
    _staminaBound = true;
}

private void UpdateStamina(float deltaTime)
{
    if (_isDead)
    {
        _stamina.Tick(deltaTime);
        return;
    }

    var sub = _skillCoordinator.CurrentSubState;
    bool sustained = sub == SkillSubState.Charging
                  || sub == SkillSubState.Channeling
                  || sub == SkillSubState.Spinning;

    float staminaDt = float.IsFinite(deltaTime)
        ? Mathf.Clamp(deltaTime, 0f, 0.1f)
        : 0f;

    if (!sustained && !_stamina.IsExhausted)
    {
        if (_cc.IsDefending && !_cc.StatusControllerAdapter.IsStunned)
        {
            if (!_stamina.TryConsume(_defendStaminaPerSecond * staminaDt))
                ForceExitDefendByExhaustion();
        }
        else if (!_cc.IsDefending
                 && !_cc.StatusControllerAdapter.IsStunned
                 && _cc.Data.BaseState == BaseState.Sprint
                 && _cc.Data.IsGrounded
                 && !_cc.LockMovement
                 && _fsmManager.Coordinator.ActiveLayer != LayerType.Hit)
        {
            _stamina.TryConsume(_sprintStaminaPerSecond * staminaDt);
        }
    }

    _stamina.Tick(deltaTime);
}

private void ForceExitDefendByExhaustion()
{
    _defendStaminaLatch = true;
    if (!_cc.IsDefending) return;
    _cc.TryExitDefend();
    _fsmManager.ExitDefend();
}
```

在 `TickRespawn` 的 `_currentHP = _maxHP;` 之后加入：

```csharp
_stamina.Refill();
_defendStaminaLatch = false;
```

在 `ApplyServerRespawn` 的 `_currentHP = hp;` 之后加入：

```csharp
_stamina.Refill();
_defendStaminaLatch = false;
```

在 `OnDestroy` 的**第一行**（早于 `_statsProvider == null` 等 early return）加入：

```csharp
EntityDisplayManager.Instance?.UnbindStamina(transform);
```

- [ ] **步骤 5：运行测试验证通过**

```bash
unity-mcp-cli run-tool assets-refresh --path "E:/CodeForJob/Cool" --timeout 240000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"PlayMode","testClass":"StaminaIntegrationTests"}' --timeout 240000
```

预期：全部 PASS。

- [ ] **步骤 6：回归死亡/复活测试**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"PlayMode","testClass":"PlayerDeathRespawnTests"}' --timeout 240000
```

预期：全部 PASS（`Refill` 在两条复活路径上，不改变 HP/位置语义）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/PlayMode/StaminaIntegrationTests.cs
git commit -m "feat(stamina): 奔跑/举盾/消耗优先级与复活回满接线"
```

---

## 任务 6：技能资产耗率

**文件：**
- 修改：`Assets/PreRes/SkillsCfg/Spin_SkillR.asset`
- 修改：`Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset`
- 修改：`Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset`
- 测试：`Assets/Scripts/Tests/Editor/StaminaSkillAssetTests.cs`

- [ ] **步骤 1：编写失败的资产测试**

创建 `Assets/Scripts/Tests/Editor/StaminaSkillAssetTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    public class StaminaSkillAssetTests
    {
        private const string Dir = "Assets/PreRes/SkillsCfg/";

        private static T Load<T>(string name) where T : SkillData
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(Dir + name + ".asset");
            Assert.IsNotNull(asset, name + " 未找到");
            return asset;
        }

        [Test]
        public void test_skill_assets_stamina_drain_values()
        {
            Assert.AreEqual(20f, Load<SpinSkillData>("Spin_SkillR").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(15f, Load<ChargedSkillData>("ChargeFire_SkillR").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(15f, Load<ProjectileSkillData>("Fireball_SkillQ").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(0f, Load<InstantSkillData>("Instant_SkillQ").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(0f, Load<InstantSkillData>("Meteor_Ultimate").StaminaDrainPerSecond, 0.001f);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"StaminaSkillAssetTests"}' --timeout 240000
```

预期：FAIL，三个资产的 `StaminaDrainPerSecond` 仍是 0。

- [ ] **步骤 3：修改三个资产**

在三个 `.asset` 的 `_staminaCost: 0` 之后各加一行（注意两个空格缩进）：

`Spin_SkillR.asset`：

```yaml
  _staminaCost: 0
  _staminaDrainPerSecond: 20
```

`ChargeFire_SkillR.asset`：

```yaml
  _staminaCost: 0
  _staminaDrainPerSecond: 15
```

`Fireball_SkillQ.asset`：

```yaml
  _staminaCost: 0
  _staminaDrainPerSecond: 15
```

- [ ] **步骤 4：刷新并运行资产测试**

```bash
unity-mcp-cli run-tool assets-refresh --path "E:/CodeForJob/Cool" --timeout 240000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"StaminaSkillAssetTests"}' --timeout 240000
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/PreRes/SkillsCfg/Spin_SkillR.asset \
        Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset \
        Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset \
        Assets/Scripts/Tests/Editor/StaminaSkillAssetTests.cs
git commit -m "chore(stamina): 配置持续技能每秒耗率"
```

---

## 任务 7：整体验收

**文件：**
- 不新增文件；运行全部测试 + MCP 截图/探针。

- [ ] **步骤 1：全部 EditMode 测试**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode"}' --timeout 240000
```

预期：全部 PASS。

- [ ] **步骤 2：全部 PlayMode 测试**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"PlayMode"}' --timeout 240000
```

预期：全部 PASS。

- [ ] **步骤 3：PlayMode 数值探针**

在 Unity Editor 进入 PlayMode（DemoDay 场景，选人后进入世界），用 MCP `script-execute` 注入一个探针脚本，读取 `Object.FindObjectOfType<Sys3CEntry>().Stamina`：

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Stamina;

public class StaminaProbe
{
    public static string Read()
    {
        var entry = Object.FindObjectOfType<Sys3CEntry>();
        if (entry == null || entry.Stamina == null) return "no-player";
        var s = entry.Stamina;
        return "Current=" + s.Current.ToString("F1")
             + " Max=" + s.Max.ToString("F1")
             + " Exhausted=" + s.IsExhausted;
    }
}
```

按规格 §9.5 逐项验证：

| 场景 | 期望 |
|---|---|
| 满精力地面冲刺 5s | `Current` 到 0、`Exhausted=True`；角色回到走路 |
| 停止后 1.35s | `Exhausted=False`，可再次冲刺 |
| 满精力举盾 10s | `Current` 到 0、强制收盾 |
| 满精力旋转 3s | 约扣 60，剩约 40 |
| 满精力蓄力 2s | 约扣 30 |
| 先冲刺 3s 再旋转 | 旋转约 2s 时力竭并中断 |
| 非持续技能收尾期间实际冲刺 | 照常扣 20/s |
| 技能锁移动期间按住 Shift | 不扣 |
| 满精力 / 死亡 / 角色出屏 | 环不可见 |
| 环收缩方向 | 12 点起逆时针；若反了改 `fillClockwise` |

- [ ] **步骤 4：MCP 截图**

用 `screenshot-game-view` 截取并保存到 `production/verify/`：

- `stamina_sprint.png` — 冲刺中，环可见且未满
- `stamina_full.png` — 满精力，环不可见
- `stamina_defend.png` — 举盾中，环下降
- `stamina_spin.png` — 旋转中，环下降

- [ ] **步骤 5：手机竖屏检查**

若当前 Game 视图可切竖屏（或 WebGL 构建后手机访问），确认：

- 环清晰可读（默认 64 参考像素约 25 物理像素；偏小则调 `EntityDisplayManager` 的 `_staminaRingDiameter` 到 72-80）。
- 环不挡摇杆/按钮（`raycastTarget=false`）。
- 位置在角色右上方，不粘屏幕边缘。

- [ ] **步骤 6：Commit（如有截图/调参变更）**

```bash
git add production/verify/stamina_*.png
git commit -m "test(stamina): 精力系统截图验收"
```

---

## 自检

- **规格覆盖度**
  - §3 核心模型 → 任务 1。
  - §4 消耗时机/技能表 → 任务 3（技能）、任务 5（奔跑/举盾/优先级）。
  - §5 各系统接线 → 任务 2/3/5。
  - §6 表现层 → 任务 4。
  - §7 数据/配置 → 任务 3（字段）、任务 5（玩家配置）、任务 6（资产）、任务 4（表现参数）。
  - §8 生命周期 → 任务 5（复活/懒绑定/OnDestroy 解绑）。
  - §9 测试与验收 → 任务 1/2/3/4/5/6/7。
  - §11 已知限制 → 任务 7 的验收与规格文档；无需代码。
- **占位符扫描**：本计划没有"待定/TODO/后续实现/类似任务 N"；每个测试与实现步骤都有可复制的代码或明确的 diff 位置。
- **类型一致性**：
  - `StaminaConfig` / `StaminaSystem` / `TryConsume` / `Tick` / `Refill` / `CanStartConsuming` / `IsExhausted` 在任务 1 定义，任务 3/4/5 使用一致。
  - `SetStamina` / `UpdateSustainedStamina` / `HandleStaminaDepleted` / `IsSustained` 在任务 3 定义并使用。
  - `StaminaRingRenderer` 的 `Bind` / `Unbind` / `Tick` / `Cleanup` / `FillAmount` / `IsVisible` / `Alpha` / `IsFlashing` 在任务 4 定义，任务 4 的测试使用一致。
  - `Sys3CEntry.Stamina` / `UpdateStamina` / `TryBindStaminaUi` / `ForceExitDefendByExhaustion` 在任务 5 定义，任务 5 的测试使用一致。
  - `InputManager.SprintAllowed` / `SetSprintHeld` 在任务 5 定义，任务 5 的测试使用一致。
  - `SkillData.StaminaDrainPerSecond` 在任务 3 定义，任务 6 的资产测试使用一致。
