# Damage Fluctuation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给所有 `DamageBlock.CalculateFinalDamage` 计算的最终伤害加可配置的全局随机波动（±`GameSettings.DamageFluctuation`），波动后钳制下限 1。

**Architecture:** 配置加在 AOT 层 `GameSettings` ScriptableObject（默认 0，向后兼容）；波动与钳制加在 Hotfix 层 `DamageBlock.CalculateFinalDamage` 方法末尾，只在该方法输出上生效（SkillExecutor 的蓄力乘数、管线修饰符不受影响）。首次为项目搭建 EditMode 测试程序集，用 TDD 验证：零配置逐字节等价、波动边界、下限钳制、暴击交互。

**Tech Stack:** Unity 2022.3.25f1 (LTS)、HybridCLR（AOT `DataDefinition` / Hotfix `GameSys` 程序集）、NUnit EditMode tests、Unity-MCP（`assets-refresh` / `tests-run` / `console-get-logs`）

**Spec:** `docs/superpowers/specs/2026-08-02-damage-fluctuation-design.md`（rev 2）

---

## File Structure

| File | Responsibility | Action |
|------|----------------|--------|
| `Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef` | 测试程序集（引用 GameSys + DataDefinition + TestAssemblies） | Create |
| `Assets/Scripts/Tests/Editor/DamageBlockFluctuationTests.cs` | `CalculateFinalDamage` 波动/钳制/兼容性的 EditMode 测试 | Create |
| `Assets/Scripts/AOT/DataDefinition/GameSettings.cs` | 加 `DamageFluctuation` 配置字段 | Modify |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs` | 方法末尾应用波动 + 钳制 | Modify |

**环境前提（Unity-MCP）：** 项目没有现成测试程序集，Task 1 一次性搭建。所有通过 MCP 执行的 Unity 操作（`assets-refresh`、`tests-run`）要求已连接的 Unity Editor；`tests-run` 还要求所有打开的场景已保存——若报 dirty scene 错误，先 `scene-save` 再重试。

---

### Task 1: 测试程序集基建

**Files:**
- Create: `Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`

- [ ] **Step 1: 创建测试程序集定义**

`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`（目录不存在则创建）：

```json
{
    "name": "GameSys.EditorTests",
    "rootNamespace": "",
    "references": [
        "GameSys",
        "DataDefinition"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false,
    "optionalUnityReferences": [
        "TestAssemblies"
    ]
}
```

说明：`GameSys`（Hotfix）在编辑器中正常编译（asmdef 无 HybridCLR 约束），测试程序集按名字引用即可；`optionalUnityReferences: ["TestAssemblies"]` 是 Unity 2022 支持的测试程序集标记（首次编译后编辑器会自动改写为显式引用，属正常现象）。

- [ ] **Step 2: 刷新并确认无编译错误**

```bash
MCP: assets-refresh
```

预期：返回成功；随后：

```bash
MCP: console-get-logs (logTypeFilter: Error, maxEntries: 20)
```

预期：无新增 Error（空测试程序集不引入编译错误）。

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Tests/Editor/
git commit -m "chore: add GameSys.EditorTests test assembly"
```

注意：Unity 刷新后会为 asmdef 生成 `.meta` 文件，一并 `git add`。

---

### Task 2: 红 — 写失败测试

**Files:**
- Create: `Assets/Scripts/Tests/Editor/DamageBlockFluctuationTests.cs`
- Test: `GameSys.EditorTests.DamageBlockFluctuationTests`

- [ ] **Step 1: 创建测试文件**

```csharp
using System.Reflection;
using DataDefinition;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class DamageBlockFluctuationTests
    {
        private static readonly FieldInfo InstanceField =
            typeof(GameSettings).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        // 注入测试用 GameSettings（不依赖 Resources 资产），TearDown 恢复 null
        private static void SetFluctuation(float value)
        {
            InstanceField.SetValue(null, null);
            var settings = ScriptableObject.CreateInstance<GameSettings>();
            settings.DamageFluctuation = value;
            InstanceField.SetValue(null, settings);
        }

        private static void SetDamageField(DamageBlock block, string name, object value)
        {
            typeof(DamageBlock).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(block, value);
        }

        [TearDown]
        public void TearDown()
        {
            InstanceField.SetValue(null, null);
        }

        [Test]
        public void ZeroFluctuation_ReturnsExactBaseDamage()
        {
            SetFluctuation(0f);
            var block = DamageBlock.CreateDefault(100f);
            Assert.AreEqual(100f, block.CalculateFinalDamage(null), 0.0001f);
        }

        [Test]
        public void ZeroFluctuation_PreservesAttributeScaling()
        {
            SetFluctuation(0f);
            var block = DamageBlock.CreateDefault(100f, 1f);
            var stats = new StubStats(attackPower: 50f);
            Assert.AreEqual(150f, block.CalculateFinalDamage(stats), 0.0001f);
        }

        [Test]
        public void Fluctuation_StaysWithinBounds_AndActuallyVaries()
        {
            SetFluctuation(0.1f);
            var block = DamageBlock.CreateDefault(100f);

            bool varied = false;
            for (int i = 0; i < 200; i++)
            {
                float d = block.CalculateFinalDamage(null);
                Assert.That(d, Is.InRange(90f, 110f), "iteration " + i);
                if (Mathf.Abs(d - 100f) > 0.01f) varied = true;
            }
            Assert.IsTrue(varied, "200 次 roll 应至少出现一次偏离基础值");
        }

        [Test]
        public void Fluctuation_ClampsToMinimumOne()
        {
            SetFluctuation(0.5f);
            var block = DamageBlock.CreateDefault(1f);

            for (int i = 0; i < 200; i++)
            {
                float d = block.CalculateFinalDamage(null);
                Assert.That(d, Is.GreaterThanOrEqualTo(1f), "iteration " + i);
                Assert.That(d, Is.LessThanOrEqualTo(1.5f), "iteration " + i);
            }
        }

        [Test]
        public void Fluctuation_AppliesToCritDamage()
        {
            SetFluctuation(0.1f);
            var block = new DamageBlock();
            SetDamageField(block, "_baseDamage", 100f);
            SetDamageField(block, "_criticalRateBonus", 1f); // 必暴击，基础 1.5 倍

            for (int i = 0; i < 200; i++)
            {
                float d = block.CalculateFinalDamage(null);
                Assert.That(d, Is.InRange(135f, 165f), "iteration " + i);
                Assert.IsTrue(block.WasCritical, "iteration " + i);
            }
        }

        // IEffectStats 测试桩：只实现 AttackPower，其余返回 0
        private class StubStats : IEffectStats
        {
            private readonly float _attackPower;
            public StubStats(float attackPower) { _attackPower = attackPower; }

            public float GetAttribute(AttributeType type)
                => type == AttributeType.AttackPower ? _attackPower : 0f;
            public float GetMaxHealth() => 0f;
            public void AddModifier(AttributeType type, string id, float value, ModifierType modType) { }
            public void RemoveModifier(AttributeType type, string id) { }
        }
    }
}
```

- [ ] **Step 2: 刷新触发编译**

```bash
MCP: assets-refresh
```

预期：返回成功。

- [ ] **Step 3: 运行测试，确认失败（红）**

```bash
MCP: tests-run (testMode: EditMode, testAssembly: GameSys.EditorTests)
```

预期：FAIL — 编译错误：`GameSettings` 不含 `DamageFluctuation` 定义（`assets-refresh` 完成后测试程序集编译失败，或 `tests-run` 直接报错）。失败即预期：实现尚不存在。

- [ ] **Step 4: 提交失败测试**

```bash
git add Assets/Scripts/Tests/Editor/
git commit -m "test: add failing fluctuation tests for DamageBlock"
```

---

### Task 3: 绿 — 实现

**Files:**
- Modify: `Assets/Scripts/AOT/DataDefinition/GameSettings.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs`
- Test: `GameSys.EditorTests.DamageBlockFluctuationTests`

- [ ] **Step 1: 加配置字段**

`Assets/Scripts/AOT/DataDefinition/GameSettings.cs` — 在文件末尾 `HitFlashCD` 字段之后、类结束 `}` 之前插入：

```csharp
        [Header("Combat")]
        [Tooltip("Damage fluctuation range as a fraction. 0.1 = ±10%")]
        [Range(0f, 1f)]
        public float DamageFluctuation = 0f;
```

- [ ] **Step 2: 应用波动 + 钳制**

`Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs` — 把 `CalculateFinalDamage` 末尾的：

```csharp
            return _isDOT ? damage * _tickInterval : damage;
```

替换为：

```csharp
            if (_isDOT) damage *= _tickInterval;

            // 全局伤害波动 —— 最后一步，只作用于本方法输出
            // （不含调用方在返回值之后叠加的乘数，如 SkillExecutor 的蓄力加成）
            float fluctuation = DataDefinition.GameSettings.Instance.DamageFluctuation;
            if (fluctuation > 0f)
            {
                damage *= 1f + UnityEngine.Random.Range(-fluctuation, fluctuation);
                // 钳制：波动不产生 0 伤害，避免 DamageContext.RawDamage 的
                // OverrideDamage == 0 回退到 BaseDamage 的已知 bug 被放大
                damage = Mathf.Max(1f, damage);
            }

            return damage;
```

- [ ] **Step 3: 刷新并确认无编译错误**

```bash
MCP: assets-refresh
```

预期：返回成功；`console-get-logs`（Error 过滤）无新增 Error。

- [ ] **Step 4: 运行测试，确认通过（绿）**

```bash
MCP: tests-run (testMode: EditMode, testAssembly: GameSys.EditorTests, testClass: GameSys.EditorTests.DamageBlockFluctuationTests)
```

预期：5 个测试全部 PASS：
- `ZeroFluctuation_ReturnsExactBaseDamage`
- `ZeroFluctuation_PreservesAttributeScaling`
- `Fluctuation_StaysWithinBounds_AndActuallyVaries`
- `Fluctuation_ClampsToMinimumOne`
- `Fluctuation_AppliesToCritDamage`

- [ ] **Step 5: 回归 — 跑全部 EditMode 测试**

```bash
MCP: tests-run (testMode: EditMode)
```

预期：无失败（本计划新增测试之外的现有测试不应受影响）。

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/AOT/DataDefinition/GameSettings.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs \
        Assets/Scripts/Tests/Editor/
git commit -m "feat: add global damage fluctuation with min-1 clamp"
```

---

### Task 4: 手动冒烟 + 收尾

**Files:** 无代码变更

- [ ] **Step 1: PlayMode 冒烟（可选但推荐）**

Unity Editor 中把 `GameSettings.asset`（`Assets/Resources/Setting/GameSettings.asset`）的 `DamageFluctuation` 设为 `0.1`，进入 PlayMode 打怪，确认伤害飘字在 ±10% 范围内起伏；打完后恢复 0。

- [ ] **Step 2: 核对 spec 覆盖**

逐条核对 `2026-08-02-damage-fluctuation-design.md`：
- 配置字段 `[Range(0,1)]` 默认 0 → Task 3 Step 1 ✅
- 波动为计算链路最后一步、钳制 ≥1 且只在 `fluctuation > 0` 分支 → Task 3 Step 2 ✅
- 向后兼容（零配置逐字节等价）→ `ZeroFluctuation_*` 测试 ✅
- 网络假设 / 性能说明 / 均值无偏 → 文档条目，无需代码，实现不改变同步路径 ✅

- [ ] **Step 3: 预热建议登记（不实现）**

spec 性能节建议"进入战斗前预热 `GameSettings.Instance`"。若冒烟时观察到首次命中卡顿，后续在启动引导处提前访问一次 `GameSettings.Instance`；本计划不实现（规范为建议项）。

---

## Notes for the Executor

- **MCP 代替命令行编译**：这是 Unity 项目，编译由 Unity Editor 完成。所有 `.cs`/`.asmdef` 文件写好后必须 `assets-refresh` 再跑测试，不要尝试用 dotnet/csc 编译。
- **`tests-run` 的 dirty scene 前置条件**：报 `InvalidOperationException` 列出 dirty scenes 时，先对列出的场景执行 `scene-save` 再重试。
- **失败测试阶段的预期**：Task 2 Step 3 的"失败"是编译失败（字段不存在），这是正常 TDD 红阶段，不要跳过 Task 2 直接实现。
- **不要改动** `GameSettings.asset` YAML：新字段缺失时 Unity 反序列化默认 0，等价于向后兼容，无需改资产文件。
- **提交粒度**：每个 Task 独立提交（含 Unity 生成的 `.meta` 文件），不要混入其他工作区改动（当前工作区有未提交的怪物 AI / 技能改动，`git add` 只加本计划的文件）。
