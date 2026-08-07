# 普攻2（竖劈）剑气实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 竖劈（SkillID 10002，`BA_Heavy.asset`，InstantSkillData）挥击中段发出一道竖直月牙剑气：真实伤害弹体，直线宽判定带命中沿途全部怪物，飞满 12m 回池。

**Architecture:** 轻量剑气组件（`SwordQiProjectile`）+ 静态生成器（`SwordQiSpawner`，对象池）挂接到 `SkillExecutor` 判定帧事件（`OnHitboxFrame`）。触发时机复用既有 `ShapeBlock.HitboxTimings` 判定帧，伤害数据从 `SkillData.Damage` 拷贝并经 `SkillDamageHelper.FillDamageContext` 填充（与 `ApplyDamage` 共用同一伤害语义）。不碰 `SkillStateMachine`、不碰 `ProjectileSkillData`。

**Tech Stack:** Unity 2022.3.25f1 LTS / HybridCLR Hotfix（`Assets/Scripts/Hotfix/`）/ NUnit EditMode 测试（`Assets/Scripts/Tests/Editor/`）/ Unity MCP（script-execute 建资产）。

**Spec:** `docs/superpowers/specs/2026-08-07-sword-qi-attack2-design.md`

**与 spec 的偏差（已确认）:** 竖劈 `BA_Heavy.asset` 是 `InstantSkillData` 而非 `ComboSkillData`——`SwordQi` 配置字段挂在 **`SkillData` 基类**（与 `_dashDistance` 同类通用能力），所有技能类型可用，默认 null 不影响其他技能。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs` | 修改 | 加 `Clone()`（剑气持有私有拷贝，防共享实例污染） |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SwordQiConfig.cs` | 新建 | 剑气配置（速度/射程/判定半径/倍率/生成帧/偏移/Effect） |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs` | 修改 | 基类加 `_swordQi` 字段 + `SwordQi` 属性 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillDamageHelper.cs` | 新建 | 伤害上下文填充（`ApplyDamage` 与剑气共用） |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` | 修改 | 加 `OnSwordQiFrame` 订阅 + `_swordQiSpawned` + `TryStart` 复位 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs` | 新建 | 剑气弹体（移动/带判定/去重/回池），`Tick` 公开可测 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs` | 新建 | 静态生成入口（懒初始化 `ComponentPool`，缺配置告警跳过） |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Pool/ComponentPool.cs` | 修改 | `Get()` 加已销毁实例防御（场景切换后池内残留） |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Events/SwordQiSpawnedEvent.cs` | 新建 | 生成事件（网络预留，本次无订阅者） |
| `Assets/Scripts/Tests/Editor/SwordQiDamageBlockCloneTests.cs` | 新建 | Task 1 测试 |
| `Assets/Scripts/Tests/Editor/SkillDamageHelperTests.cs` | 新建 | Task 2 测试 |
| `Assets/Scripts/Tests/Editor/SwordQiProjectileTests.cs` | 新建 | Task 4 测试 |
| `Assets/Scripts/Tests/Editor/ComponentPoolSwordQiTests.cs` | 新建 | Task 5 测试 |
| `Assets/Scripts/Tests/Editor/SwordQiSpawnerTests.cs` | 新建 | Task 6 测试 |
| `Assets/Scripts/Tests/Editor/SwordQiSpawnedEventTests.cs` | 新建 | Task 7 测试 |
| `Assets/Scripts/Tests/Editor/SkillExecutorSwordQiTests.cs` | 新建 | Task 8 测试 |
| `Assets/Prefabs/VFX/SwordQiTrail.mat` | 新建（MCP） | 剑气拖尾材质 |
| `Assets/Prefabs/VFX/SwordQi.prefab` | 新建（MCP） | 剑气预制体（SwordQiProjectile + TrailRenderer） |
| `Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset` | 新建（MCP） | 竖劈剑气配置 |
| `Assets/PreRes/SkillsCfg/BA_Heavy.asset` | 修改（MCP） | `_swordQi` 挂引用 |

---

### Task 1: DamageBlock.Clone()

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs`
- Create: `Assets/Scripts/Tests/Editor/SwordQiDamageBlockCloneTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class SwordQiDamageBlockCloneTests
    {
        [Test]
        public void test_damage_block_clone_returns_independent_instance()
        {
            // Arrange
            var original = DamageBlock.CreateDefault(50f, 1f);

            // Act
            var copy = original.Clone();

            // Assert
            Assert.IsNotNull(copy);
            Assert.AreNotSame(original, copy, "Clone 必须返回独立实例");
        }

        [Test]
        public void test_damage_block_clone_preserves_runtime_context()
        {
            // Arrange
            var original = DamageBlock.CreateDefault(50f, 1f);
            original.SkillId = 10002;
            original.ComboIndex = 2;
            original.CalculatedDamage = 123f;

            // Act
            var copy = original.Clone();

            // Assert
            Assert.AreEqual(10002, copy.SkillId);
            Assert.AreEqual(2, copy.ComboIndex);
            Assert.AreEqual(123f, copy.CalculatedDamage);
        }

        [Test]
        public void test_damage_block_clone_mutation_does_not_affect_original()
        {
            // Arrange
            var original = DamageBlock.CreateDefault(50f, 1f);

            // Act
            var copy = original.Clone();
            copy.CalculatedDamage = 999f;

            // Assert
            Assert.AreEqual(0f, original.CalculatedDamage, "修改拷贝不得影响原实例");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `tests-run` (EditMode, testAssembly: Assembly-CSharp-Editor-testable)
Expected: FAIL — `DamageBlock` 没有 `Clone` 方法（编译错误）。

- [ ] **Step 3: Implement Clone() in DamageBlock.cs**

在 `CalculateFinalDamage` 方法之后、`CreateDefault` 之前插入（文件内同一类可直接访问私有字段）：

```csharp
        /// <summary>
        /// 深拷贝伤害块（剑气等长生命周期弹体持有私有拷贝，防止共享实例被后续施放污染）
        /// </summary>
        public DamageBlock Clone()
        {
            return new DamageBlock
            {
                _baseDamage = _baseDamage,
                _attackRatio = _attackRatio,
                _scalingAttribute = _scalingAttribute,
                _damageType = _damageType,
                _criticalRateBonus = _criticalRateBonus,
                _criticalDamageBonus = _criticalDamageBonus,
                _isTrueDamage = _isTrueDamage,
                _armorPenetration = _armorPenetration,
                _knockbackForce = _knockbackForce,
                _isDOT = _isDOT,
                _tickInterval = _tickInterval,
                _totalTicks = _totalTicks,
                SkillId = SkillId,
                ComboIndex = ComboIndex,
                CalculatedDamage = CalculatedDamage,
            };
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `tests-run` (EditMode, testClass: SwordQiDamageBlockCloneTests)
Expected: 3 PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs Assets/Scripts/Tests/Editor/SwordQiDamageBlockCloneTests.cs
git commit -m "feat: DamageBlock.Clone for sword qi private damage copy"
```

---

### Task 2: SkillDamageHelper + ApplyDamage 重构

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillDamageHelper.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs:519-551`（ApplyDamage 方法）
- Create: `Assets/Scripts/Tests/Editor/SkillDamageHelperTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillDamageHelperTests
    {
        private class FakeStats : IEffectStats
        {
            public float GetAttribute(AttributeType type) => 100f;
            public float GetMaxHealth() => 1000f;
            public void AddModifier(AttributeType type, string id, float value, ModifierType modType) { }
            public void RemoveModifier(AttributeType type, string id) { }
        }

        [Test]
        public void test_skill_damage_helper_fill_context_sets_runtime_fields()
        {
            // Arrange
            var damage = DamageBlock.CreateDefault(50f, 0f); // base 50 + 100*0 = 50（无波动场景，波动默认需为 0）
            // 波动：GameSettings.Instance.DamageFluctuation 默认 0 → 结果确定

            // Act
            SkillDamageHelper.FillDamageContext(damage, 10002, 2, null, new FakeStats());

            // Assert
            Assert.AreEqual(10002, damage.SkillId);
            Assert.AreEqual(2, damage.ComboIndex);
            Assert.AreEqual(50f, damage.CalculatedDamage, 0.001f);
        }

        [Test]
        public void test_skill_damage_helper_effect_knockback_overrides_damage_default()
        {
            // Arrange
            var damage = DamageBlock.CreateDefault(50f, 0f);
            damage.KnockbackForce = 1f;
            var effect = new EffectBlock();
            TestHelpers.SetField(effect, "_knockbackForce", 8f);

            // Act
            SkillDamageHelper.FillDamageContext(damage, 10002, 2, effect, new FakeStats());

            // Assert
            Assert.AreEqual(8f, damage.KnockbackForce, "EffectBlock 击退应覆盖 DamageBlock 默认");
        }

        [Test]
        public void test_skill_damage_helper_effect_knockback_zero_keeps_damage_default()
        {
            // Arrange
            var damage = DamageBlock.CreateDefault(50f, 0f);
            damage.KnockbackForce = 1f;
            var effect = new EffectBlock(); // KnockbackForce = 0

            // Act
            SkillDamageHelper.FillDamageContext(damage, 10002, 2, effect, new FakeStats());

            // Assert
            Assert.AreEqual(1f, damage.KnockbackForce, "击退为 0 时保留 DamageBlock 默认值");
        }

        [Test]
        public void test_skill_damage_helper_null_damage_is_noop()
        {
            // Act & Assert（不应抛异常）
            Assert.DoesNotThrow(() => SkillDamageHelper.FillDamageContext(null, 1, 1, null, null));
        }
    }
}
```

注意：`EffectBlock._knockbackForce` 是私有字段，测试用 `TestHelpers.SetField`（已有工具，`Assets/Scripts/Tests/Editor/TestHelpers.cs:13`）。`GameSettings.Instance.DamageFluctuation` 默认 0（若测试环境非 0，用 `Assert.AreEqual(50f, damage.CalculatedDamage, 5f)` 容差——按实际配置定，默认 0 则精确断言）。

- [ ] **Step 2: Run test to verify it fails**

Run: `tests-run` (EditMode, testClass: SkillDamageHelperTests)
Expected: FAIL — 类型 `SkillDamageHelper` 不存在。

- [ ] **Step 3: Create SkillDamageHelper.cs**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能伤害上下文填充：SkillExecutor.ApplyDamage 与 SwordQiProjectile 共用，
    /// 保证 SkillId/ComboIndex/CalculatedDamage/击退 语义一致。
    /// </summary>
    public static class SkillDamageHelper
    {
        public static void FillDamageContext(DamageBlock damage, int skillId, int comboIndex,
                                             EffectBlock effect, IEffectStats stats)
        {
            if (damage == null) return;

            damage.SkillId = skillId;
            damage.ComboIndex = comboIndex;
            damage.CalculatedDamage = damage.CalculateFinalDamage(stats);

            // 与 ApplyDamage 原逻辑一致：EffectBlock 击退 > 0 时覆盖 DamageBlock 默认值
            if (effect != null && effect.KnockbackForce > 0)
                damage.KnockbackForce = effect.KnockbackForce;
        }
    }
}
```

- [ ] **Step 4: Refactor SkillExecutor.ApplyDamage to use it（行为不变）**

`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` 的 `ApplyDamage`（现 519-551 行）整段替换为：

```csharp
        private void ApplyDamage(IEffectTarget target, int frameIndex)
        {
            var damageBlock = _skillData.Damage;
            if (damageBlock == null) return;

            SkillDamageHelper.FillDamageContext(damageBlock, _skillData.SkillId, frameIndex + 1,
                                                GetEffect(), _owner.Stats);

            // 蓄力/执行阶段伤害加成（技能特有逻辑，保留在执行器内）
            if (CurrentSubState == SkillSubState.Charging || CurrentSubState == SkillSubState.Execution)
                damageBlock.CalculatedDamage *= 1f + GetChargeProgress() * 0.5f;

            _lastDamageBlock = damageBlock;

            // Route through IDamageable for unified feedback path
            if (target is IDamageable damageable)
            {
                Vector3 hitDir = (target.transform.position - _owner.transform.position).normalized;
                damageable.TakeDamage(damageBlock, hitDir);
            }
            else
            {
                target.Heal(-damageBlock.CalculatedDamage);
            }
        }
```

行为等价说明：原代码 `CalculateFinalDamage → 蓄力乘 → 填 CalculatedDamage`；重构后 `FillDamageContext 内算基础伤害填 CalculatedDamage → 蓄力乘`。`CalculateFinalDamage` 只调用一次、波动 Random 一次，数值一致。原 `target.Heal(-damage)` 的 `damage` 即含蓄力后的值 = `-CalculatedDamage` ✓。

- [ ] **Step 5: Run new + existing tests（回归）**

Run: `tests-run` (EditMode)
Expected: SkillDamageHelperTests 4 PASS；SkillExecutorSpinTests、CharacterAttackHandlerSpinTests 等既有测试全部 PASS（无回归）。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillDamageHelper.cs Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs Assets/Scripts/Tests/Editor/SkillDamageHelperTests.cs
git commit -m "refactor: extract SkillDamageHelper.FillDamageContext (shared by executor and sword qi)"
```

---

### Task 3: SwordQiConfig + SkillData 基类扩展

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SwordQiConfig.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs`（_damage 字段之后加 _swordQi）

纯数据类（无行为，无独立测试；行为测试在 Task 4/8 覆盖）。

- [ ] **Step 1: Create SwordQiConfig.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "SwordQiConfig", menuName = "Game/Skills/Sword Qi Config")]
    public class SwordQiConfig : ScriptableObject
    {
        [Header("=== Projectile ===")]
        [Tooltip("剑气预制体（根物体需含 SwordQiProjectile 组件）")]
        [SerializeField] private GameObject _prefab;
        public GameObject Prefab => _prefab;

        [Tooltip("飞行速度(米/秒)")]
        [SerializeField] private float _speed = 18f;
        public float Speed => _speed;

        [Tooltip("射程(米)，累计飞行距离耗尽后回池")]
        [SerializeField] private float _range = 12f;
        public float Range => _range;

        [Tooltip("判定带半径(米)，宽扁带判定，兼作高度差补偿")]
        [SerializeField] private float _detectRadius = 1f;
        public float DetectRadius => _detectRadius;

        [Tooltip("伤害倍率 (1.0 = 继承技能全额伤害)")]
        [SerializeField] private float _damagePercent = 1f;
        public float DamagePercent => _damagePercent;

        [Tooltip("判定帧数组中的第几帧生成剑气 (0 = 第一判定帧)")]
        [SerializeField] private int _spawnFrameIndex;
        public int SpawnFrameIndex => _spawnFrameIndex;

        [Tooltip("生成位置偏移（角色局部坐标：前方 + 剑高）")]
        [SerializeField] private Vector3 _spawnOffset = new Vector3(0.8f, 1.0f, 0.6f);
        public Vector3 SpawnOffset => _spawnOffset;

        [Tooltip("命中击退等效果（KnockbackForce > 0 时覆盖技能默认；当前仅击退生效）")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;
    }
}
```

（`EffectBlock` 与 `SwordQiConfig` 同在 `Hotfix.GameSystems.Skills.Data` 命名空间，无需额外 using。）

- [ ] **Step 2: Extend SkillData.cs 基类**

`Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs`，在 `_damage` 字段（第 92 行）之后追加：

```csharp
        [Header("=== Sword Qi ===")]
        [Tooltip("技能挥出时附加的剑气配置（null = 不发射剑气）")]
        [SerializeField] protected SwordQiConfig _swordQi;
        public SwordQiConfig SwordQi => _swordQi;
```

- [ ] **Step 3: Compile check**

Run: `assets-refresh`（触发编译），`console-get-logs` 确认无编译错误。
Expected: 无 Error 日志。

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/SwordQiConfig.cs Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs
git commit -m "feat: SwordQiConfig data + SkillData.SwordQi hook (base class, null = disabled)"
```

---

### Task 4: SwordQiProjectile（弹体核心）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs`
- Create: `Assets/Scripts/Tests/Editor/SwordQiProjectileTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SwordQiProjectileTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        private class FakeDamageable : IDamageable
        {
            public bool IsAlive => true;
            public Transform Transform { get; }
            public int HitCount { get; private set; }
            public DamageBlock LastDamage;

            public FakeDamageable(Transform t) { Transform = t; }

            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection)
            {
                HitCount++;
                LastDamage = damageData;
            }

            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact)
            {
                TakeDamage(damageData, hitDirection);
            }
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos)
                if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private static SwordQiConfig CreateConfig()
        {
            var cfg = ScriptableObject.CreateInstance<SwordQiConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_speed").floatValue = 18f;
            so.FindProperty("_range").floatValue = 12f;
            so.FindProperty("_detectRadius").floatValue = 1f;
            so.FindProperty("_damagePercent").floatValue = 1f;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private SwordQiProjectile CreateProjectile(SwordQiConfig cfg, DamageBlock damage,
                                                   Vector3 origin, Vector3 dir)
        {
            var qi = NewGo("Qi").AddComponent<SwordQiProjectile>();
            qi.OnSpawn(cfg, damage, origin, dir, null);
            return qi;
        }

        [Test]
        public void test_sword_qi_projectile_moves_forward_at_config_speed()
        {
            // Arrange
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act
            qi.Tick(0.5f);

            // Assert
            Assert.AreEqual(0f, qi.transform.position.x, 0.001f);
            Assert.AreEqual(0f, qi.transform.position.y, 0.001f);
            Assert.AreEqual(9f, qi.transform.position.z, 0.001f); // 18 * 0.5
        }

        [Test]
        public void test_sword_qi_projectile_hits_target_within_detect_radius()
        {
            // Arrange
            var targetGo = NewGo("Target");
            targetGo.transform.position = new Vector3(0f, 0f, 3f);
            var target = new FakeDamageable(targetGo.transform);
            PhysicsRegistry.Instance.Register(target, EntityType.Monster);
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act（飞 3m 越过目标）
            qi.Tick(3f / 18f);

            // Assert
            Assert.AreEqual(1, target.HitCount, "目标在判定带内应被命中");
        }

        [Test]
        public void test_sword_qi_projectile_hits_each_target_only_once()
        {
            // Arrange
            var targetGo = NewGo("Target");
            targetGo.transform.position = new Vector3(0f, 0f, 3f);
            var target = new FakeDamageable(targetGo.transform);
            PhysicsRegistry.Instance.Register(target, EntityType.Monster);
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act（多帧停留在目标附近，共飞 4m）
            for (int i = 0; i < 20; i++)
                qi.Tick(4f / 18f / 20f);

            // Assert
            Assert.AreEqual(1, target.HitCount, "同一目标每道剑气只命中一次");
        }

        [Test]
        public void test_sword_qi_projectile_releases_after_max_range()
        {
            // Arrange
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act（超过 12m 射程）
            qi.Tick(13f / 18f);

            // Assert
            Assert.IsFalse(qi.gameObject.activeSelf, "飞满射程后应回池（SetActive false）");
            qi.Tick(0.1f); // 回池后再步进不抛异常
        }

        [Test]
        public void test_sword_qi_projectile_applies_calculated_damage()
        {
            // Arrange
            var targetGo = NewGo("Target");
            targetGo.transform.position = new Vector3(0f, 0f, 2f);
            var target = new FakeDamageable(targetGo.transform);
            PhysicsRegistry.Instance.Register(target, EntityType.Monster);
            var damage = DamageBlock.CreateDefault(30f, 0f);
            damage.CalculatedDamage = 42f; // 预填充（真实流程由 FillDamageContext 填充）
            var qi = CreateProjectile(CreateConfig(), damage, Vector3.zero, Vector3.forward);

            // Act
            qi.Tick(2f / 18f);

            // Assert
            Assert.AreEqual(42f, target.LastDamage.CalculatedDamage, 0.001f);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `tests-run` (EditMode, testClass: SwordQiProjectileTests)
Expected: FAIL — `SwordQiProjectile` 类型不存在。

- [ ] **Step 3: Implement SwordQiProjectile.cs**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.SwordQi
{
    /// <summary>
    /// 剑气弹体：直线飞行 + 每帧宽判定带检测 + 每目标一次命中。
    /// 生命周期与释放者解耦；飞满射程回池。
    /// </summary>
    public class SwordQiProjectile : MonoBehaviour
    {
        private SwordQiConfig _config;
        private DamageBlock _damage;
        private Vector3 _direction;
        private float _traveled;
        private readonly HashSet<int> _hitIds = new HashSet<int>();
        private bool _released;
        private ComponentPool<SwordQiProjectile> _pool;

        public void OnSpawn(SwordQiConfig config, DamageBlock damage, Vector3 origin, Vector3 direction,
                            ComponentPool<SwordQiProjectile> pool)
        {
            _config = config;
            _damage = damage;
            _direction = direction;
            _pool = pool;
            _released = false;
            _traveled = 0f;
            _hitIds.Clear();
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
            if (_config == null || _released) return;

            float step = _config.Speed * deltaTime;
            _traveled += step;
            transform.position += _direction * step;

            DetectAndDamage();

            if (_traveled >= _config.Range)
                Release();
        }

        private void DetectAndDamage()
        {
            var targets = PhysicsRegistry.Instance.FindNearby(transform.position,
                                                              _config.DetectRadius,
                                                              EntityType.Monster);
            foreach (var target in targets)
            {
                int id = target.Transform.GetInstanceID();
                if (!_hitIds.Add(id)) continue;

                Vector3 hitDir = (target.Transform.position - transform.position).normalized;
                target.TakeDamage(_damage, hitDir);
            }
        }

        /// <summary>飞满射程/手动回收；幂等</summary>
        public void Release()
        {
            if (_released) return;
            _released = true;
            gameObject.SetActive(false);
            _pool?.Return(this);
        }

        private void OnDestroy()
        {
            // 场景销毁时置位，防止后续 Release 回池已销毁实例
            _released = true;
            _pool = null;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `tests-run` (EditMode, testClass: SwordQiProjectileTests)
Expected: 5 PASS（PhysicsRegistry 注册的无碰撞体假目标走"补充路径"距离判断，Editor 可跑）

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs Assets/Scripts/Tests/Editor/SwordQiProjectileTests.cs
git commit -m "feat: SwordQiProjectile — linear flight, band detection, per-target dedup, pool release"
```

---

### Task 5: ComponentPool.Get 已销毁实例防御

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Pool/ComponentPool.cs:20-33`
- Create: `Assets/Scripts/Tests/Editor/ComponentPoolSwordQiTests.cs`

背景：场景切换时池内实例被 Unity 销毁（C# 引用非 null 但 `== null` 为 true），`Get()` 直接 `SetActive` 会抛异常。剑气池跨场景存活，必须防御。

- [ ] **Step 1: Write the failing test**

```csharp
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class ComponentPoolSwordQiTests
    {
        [Test]
        public void test_component_pool_get_recreates_destroyed_instance()
        {
            // Arrange
            var prefabGo = new GameObject("QiPrefab");
            var prefabComp = prefabGo.AddComponent<SwordQiProjectile>();
            var pool = new ComponentPool<SwordQiProjectile>(prefabComp);
            var first = pool.Get();
            pool.Return(first);
            Object.DestroyImmediate(first.gameObject); // 模拟场景销毁后池内残留

            // Act
            var second = pool.Get();

            // Assert
            Assert.IsNotNull(second, "池内已销毁实例应被重建");
            Assert.IsTrue(second.gameObject.activeSelf);
        }

        [Test]
        public void test_component_pool_return_reuses_same_instance()
        {
            // Arrange
            var prefabGo = new GameObject("QiPrefab");
            var prefabComp = prefabGo.AddComponent<SwordQiProjectile>();
            var pool = new ComponentPool<SwordQiProjectile>(prefabComp);

            // Act
            var first = pool.Get();
            pool.Return(first);
            var second = pool.Get();

            // Assert
            Assert.AreSame(first, second, "回池后再次 Get 应复用同一实例");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `tests-run` (EditMode, testClass: ComponentPoolSwordQiTests)
Expected: 第一个测试 FAIL（`DestroyImmediate` 后 `Get()` 在 `instance.gameObject` 处抛 MissingReferenceException 或空引用）。

- [ ] **Step 3: Modify ComponentPool.Get**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Pool/ComponentPool.cs:20-33` 替换为：

```csharp
        public T Get()
        {
            T instance;
            if (_pool.Count > 0)
            {
                instance = _pool.Dequeue();
                // 场景切换销毁后池内残留伪实例（Unity 重载 == null 为 true）
                if (instance == null)
                {
                    instance = CreateNew();
                }
                else
                {
                    instance.gameObject.SetActive(true);
                }
            }
            else
            {
                instance = CreateNew();
            }
            return instance;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `tests-run` (EditMode, testClass: ComponentPoolSwordQiTests)
Expected: 2 PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Pool/ComponentPool.cs Assets/Scripts/Tests/Editor/ComponentPoolSwordQiTests.cs
git commit -m "fix: ComponentPool.Get guards destroyed instances (scene-unload residue)"
```

---

### Task 6: SwordQiSpawner（生成入口）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs`
- Create: `Assets/Scripts/Tests/Editor/SwordQiSpawnerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.EditorTests
{
    public class SwordQiSpawnerTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos)
                if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private static SwordQiConfig CreateConfig(GameObject prefab)
        {
            var cfg = ScriptableObject.CreateInstance<SwordQiConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_speed").floatValue = 18f;
            so.FindProperty("_range").floatValue = 12f;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private static GameObject CreateQiPrefab()
        {
            var go = new GameObject("QiPrefab");
            go.AddComponent<SwordQiProjectile>();
            return go;
        }

        [Test]
        public void test_sword_qi_spawner_creates_projectile_from_pool()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);
            var before = Object.FindObjectsOfType<SwordQiProjectile>().Length;

            // Act
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            // Assert
            var spawned = Object.FindObjectsOfType<SwordQiProjectile>();
            Assert.AreEqual(before + 1, spawned.Length, "应生成一道剑气");
            Assert.IsTrue(spawned[spawned.Length - 1].gameObject.activeSelf);
        }

        [Test]
        public void test_sword_qi_spawner_skips_when_prefab_missing()
        {
            // Arrange
            var cfg = ScriptableObject.CreateInstance<SwordQiConfig>(); // _prefab = null

            // Act & Assert（LogWarning + 不生成）
            LogAssert.Expect(LogType.Warning, "*config or prefab is null*");
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            Assert.AreEqual(0, Object.FindObjectsOfType<SwordQiProjectile>().Length);
        }

        [Test]
        public void test_sword_qi_spawner_reuses_pooled_instance()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);

            // Act
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            var first = Object.FindObjectsOfType<SwordQiProjectile>()[0];
            first.Release();
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            var second = Object.FindObjectsOfType<SwordQiProjectile>()[0];

            // Assert
            Assert.AreSame(first, second, "回池后再次 Spawn 应复用实例（池生效）");
        }
    }
}
```

注意：`SwordQiSpawner` 是静态懒初始化池，测试间会残留——TearDown 里 `DestroyImmediate` 掉剑气与 prefab，`_root` 在下次 Spawn 时重建（`_root == null` 检测）。若测试顺序影响静态状态，在 `SwordQiSpawner` 中实现后按需清理（见 Step 3 的 `DisposeForTests` 说明）。

- [ ] **Step 2: Run test to verify it fails**

Run: `tests-run` (EditMode, testClass: SwordQiSpawnerTests)
Expected: FAIL — `SwordQiSpawner` 类型不存在。

- [ ] **Step 3: Implement SwordQiSpawner.cs**

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.SwordQi
{
    /// <summary>
    /// 剑气生成入口：懒初始化对象池。缺配置/缺预制体时跳过并告警（不影响技能本体）。
    /// 剑气池挂在场景根下——场景卸载时剑气随场景销毁（OnDestroy 守卫防回池）。
    /// </summary>
    public static class SwordQiSpawner
    {
        private static ComponentPool<SwordQiProjectile> _pool;
        private static Transform _root;

        public static void Spawn(SwordQiConfig config, DamageBlock damage, Vector3 origin, Vector3 direction)
        {
            if (config == null || config.Prefab == null)
            {
                Debug.LogWarning("[SwordQiSpawner] config or prefab is null, skip spawn");
                return;
            }

            if (_root == null || _pool == null)
            {
                _root = new GameObject("SwordQiPool").transform;
                var prefabComp = config.Prefab.GetComponent<SwordQiProjectile>();
                if (prefabComp == null)
                {
                    Debug.LogWarning("[SwordQiSpawner] prefab missing SwordQiProjectile component, skip spawn");
                    return;
                }
                _pool = new ComponentPool<SwordQiProjectile>(prefabComp, _root, 2);
            }

            var qi = _pool.Get();
            qi.OnSpawn(config, damage, origin, direction, _pool);
        }

        /// <summary>测试专用：清空静态池状态（避免 EditMode 测试间串扰）</summary>
        public static void DisposeForTests()
        {
            if (_root != null)
                Object.DestroyImmediate(_root.gameObject);
            _pool = null;
            _root = null;
        }
    }
}
```

- [ ] **Step 4: Update tests to clean static state**

在 `SwordQiSpawnerTests.TearDown` 中追加一行 `SwordQiSpawner.DisposeForTests();`（在 Destroy 循环之后）。

- [ ] **Step 5: Run test to verify it passes**

Run: `tests-run` (EditMode, testClass: SwordQiSpawnerTests)
Expected: 3 PASS

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs Assets/Scripts/Tests/Editor/SwordQiSpawnerTests.cs
git commit -m "feat: SwordQiSpawner — lazy pooled spawn entry with warn-and-skip"
```

---

### Task 7: SwordQiSpawnedEvent（网络预留事件）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Skills/Events/SwordQiSpawnedEvent.cs`
- Create: `Assets/Scripts/Tests/Editor/SwordQiSpawnedEventTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Hotfix.GameSystems.Skills.Events;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SwordQiSpawnedEventTests
    {
        [Test]
        public void test_sword_qi_spawned_event_emits_with_payload()
        {
            // Arrange
            SwordQiSpawnedEvent received = null;
            EventBus.Subscribe<SwordQiSpawnedEvent>(e => received = e);

            try
            {
                // Act
                EventBus.Emit(new SwordQiSpawnedEvent
                {
                    CasterId = 42,
                    SkillId = 10002,
                    Origin = new Vector3(1f, 2f, 3f),
                    Direction = Vector3.forward,
                    Speed = 18f,
                    Range = 12f
                });

                // Assert
                Assert.IsNotNull(received);
                Assert.AreEqual(42, received.CasterId);
                Assert.AreEqual(10002, received.SkillId);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), received.Origin);
                Assert.AreEqual(12f, received.Range);
            }
            finally
            {
                EventBus.Unsubscribe<SwordQiSpawnedEvent>(e => received = e);
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `tests-run` (EditMode, testClass: SwordQiSpawnedEventTests)
Expected: FAIL — 类型不存在（编译错误）。

- [ ] **Step 3: Create SwordQiSpawnedEvent.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    /// <summary>
    /// 剑气生成事件（网络预留：SkillNetworkSync 将来订阅后转发给其他客户端）。
    /// 本次无订阅者——仅承载生成快照。
    /// </summary>
    public class SwordQiSpawnedEvent : IEvent
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

- [ ] **Step 4: Run test to verify it passes**

Run: `tests-run` (EditMode, testClass: SwordQiSpawnedEventTests)
Expected: 1 PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Events/SwordQiSpawnedEvent.cs Assets/Scripts/Tests/Editor/SwordQiSpawnedEventTests.cs
git commit -m "feat: SwordQiSpawnedEvent (network sync hook, no subscribers yet)"
```

---

### Task 8: SkillExecutor.OnSwordQiFrame 集成

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`
- Create: `Assets/Scripts/Tests/Editor/SkillExecutorSwordQiTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorSwordQiTests
    {
        private GameObject _ownerGo;

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null)
                Object.DestroyImmediate(_ownerGo);
            SwordQiSpawner.DisposeForTests();
        }

        private static SwordQiConfig CreateConfig(GameObject prefab)
        {
            var cfg = ScriptableObject.CreateInstance<SwordQiConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_speed").floatValue = 18f;
            so.FindProperty("_range").floatValue = 12f;
            so.FindProperty("_spawnFrameIndex").intValue = 0;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private static GameObject CreateQiPrefab()
        {
            var go = new GameObject("QiPrefab");
            go.AddComponent<SwordQiProjectile>();
            return go;
        }

        /// <summary>
        /// 反射触发 SkillStateMachine.OnHitboxFrame 事件（C# 事件无 raise 方法，
        /// 通过私有事件字段 delegate 触发）
        /// </summary>
        private static void RaiseHitboxFrame(SkillExecutor executor, int frameIndex)
        {
            var stateMachine = TestHelpers.GetField(executor, "_stateMachine");
            var eventField = stateMachine.GetType().GetField("OnHitboxFrame",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var del = (System.Action<int>)eventField.GetValue(stateMachine);
            del?.Invoke(frameIndex);
        }

        private SkillExecutor CreateExecutor(SwordQiConfig swordQi)
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 10002);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(50f, 0f));
            TestHelpers.SetField(data, "_swordQi", swordQi);

            _ownerGo = new GameObject("SwordQiOwner");
            _ownerGo.transform.position = Vector3.zero;
            _ownerGo.transform.rotation = Quaternion.identity;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            executor.TryStart();
            return executor;
        }

        [Test]
        public void test_skill_executor_spawns_sword_qi_on_matching_frame()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            var executor = CreateExecutor(CreateConfig(prefab));
            var before = Object.FindObjectsOfType<SwordQiProjectile>().Length;

            // Act（反射触发判定帧事件链：OnHitboxFrame → OnSwordQiFrame）
            TestHelpers.SetField(executor, "_swordQiSpawned", false);
            RaiseHitboxFrame(executor, 0);

            // Assert
            var spawned = Object.FindObjectsOfType<SwordQiProjectile>();
            Assert.AreEqual(before + 1, spawned.Length, "匹配的判定帧应生成剑气");
        }

        [Test]
        public void test_skill_executor_sword_qi_spawns_only_once_per_activation()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            var executor = CreateExecutor(CreateConfig(prefab));

            // Act（同一帧触发两次）
            RaiseHitboxFrame(executor, 0);
            RaiseHitboxFrame(executor, 0);

            // Assert
            Assert.AreEqual(1, Object.FindObjectsOfType<SwordQiProjectile>().Length, "同一次施放只生成一道剑气");
        }

        [Test]
        public void test_skill_executor_sword_qi_spawns_at_configured_frame_index()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            var cfg = CreateConfig(prefab);
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_spawnFrameIndex").intValue = 2;
            so.ApplyModifiedProperties();
            var executor = CreateExecutor(cfg);

            // Act
            RaiseHitboxFrame(executor, 1); // 非配置帧
            RaiseHitboxFrame(executor, 2); // 配置帧

            // Assert
            Assert.AreEqual(1, Object.FindObjectsOfType<SwordQiProjectile>().Length, "仅在配置帧索引生成");
        }

        [Test]
        public void test_skill_executor_sword_qi_skipped_when_config_null()
        {
            // Arrange
            var executor = CreateExecutor(null); // 无剑气配置

            // Act
            RaiseHitboxFrame(executor, 0);

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<SwordQiProjectile>().Length, "无配置不生成");
        }
    }
}
```

测试依赖 `TestHelpers.GetField`——现有 `TestHelpers`（`Assets/Scripts/Tests/Editor/TestHelpers.cs`）只有 `SetField`，**需先补充**：

```csharp
        public static object GetField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}");
            return field.GetValue(target);
        }
```

（追加到 `TestHelpers` 类内，`SetField` 之后。）

- [ ] **Step 2: Run test to verify it fails**

Run: `tests-run` (EditMode, testClass: SkillExecutorSwordQiTests)
Expected: FAIL — `SkillExecutor` 未订阅 `OnSwordQiFrame`（`_swordQiSpawned` 字段尚不存在，`SetField` 会断言失败）。

- [ ] **Step 3: Modify SkillExecutor.cs**

三处修改：

**(a) 字段区**（`_spinHitTracker` 之后，第 39 行附近）追加：

```csharp
        private bool _swordQiSpawned;
```

**(b) 构造函数**（`_stateMachine.OnHitboxFrame += OnHitboxTriggered;` 之后）追加：

```csharp
            _stateMachine.OnHitboxFrame += OnSwordQiFrame;
```

**(c) TryStart**（`bool started = _stateMachine.TryStart();` 之前）追加：

```csharp
            _swordQiSpawned = false; // 每次施放复位（连击链多次施放）
```

**(d) 新方法**（`OnHitboxTriggered` 之前插入）：

```csharp
        /// <summary>
        /// 判定帧事件第二订阅者：按 SwordQiConfig 生成剑气（挥击中段，与近战判定同帧）。
        /// 伤害数据从 SkillData.Damage 拷贝并填充上下文，防共享实例被后续施放污染。
        /// </summary>
        private void OnSwordQiFrame(int frameIndex)
        {
            if (_swordQiSpawned) return;

            var cfg = _skillData.SwordQi;
            if (cfg == null || cfg.Prefab == null) return;
            if (frameIndex != cfg.SpawnFrameIndex) return;

            _swordQiSpawned = true;

            var damage = _skillData.Damage?.Clone();
            if (damage == null)
            {
                Debug.LogWarning("[SkillExecutor] SwordQi config set but skill has no DamageBlock, skip spawn");
                return;
            }

            SkillDamageHelper.FillDamageContext(damage, _skillData.SkillId, frameIndex + 1, cfg.Effect, _owner.Stats);
            damage.CalculatedDamage *= cfg.DamagePercent;

            Vector3 origin = _owner.transform.position + _owner.transform.TransformDirection(cfg.SpawnOffset);
            Vector3 dir = _owner.transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _owner.transform.forward;

            SwordQiSpawner.Spawn(cfg, damage, origin, dir);

            EventBus.Emit(new SwordQiSpawnedEvent
            {
                CasterId = _owner.transform.GetInstanceID(),
                SkillId = _skillData.SkillId,
                Origin = origin,
                Direction = dir,
                Speed = cfg.Speed,
                Range = cfg.Range
            });
        }
```

文件顶部 using 追加：`using Hotfix.GameSystems.Skills.Runtime.SwordQi;`（`SwordQiSpawner`/`SwordQiProjectile` 所在命名空间）。`SwordQiSpawnedEvent` 在 `Hotfix.GameSystems.Skills.Events`（已有 using，第 7 行）✓。`Debug`/`Vector3` 来自 UnityEngine（已有 using）✓。

- [ ] **Step 4: Run test to verify it passes**

Run: `tests-run` (EditMode, testClass: SkillExecutorSwordQiTests)
Expected: 4 PASS

- [ ] **Step 5: Full regression**

Run: `tests-run` (EditMode)
Expected: 全部 PASS（含 Task 1-7 测试与既有测试）。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs Assets/Scripts/Tests/Editor/SkillExecutorSwordQiTests.cs Assets/Scripts/Tests/Editor/TestHelpers.cs
git commit -m "feat: SkillExecutor spawns sword qi on hitbox frame (dedup, reset, network event)"
```

---

### Task 9: 剑气预制体 + 配置资产 + 挂接竖劈（Unity MCP）

**Files:**
- Create: `Assets/Prefabs/VFX/SwordQiTrail.mat`
- Create: `Assets/Prefabs/VFX/SwordQi.prefab`
- Create: `Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset`
- Modify: `Assets/PreRes/SkillsCfg/BA_Heavy.asset`

前置：Task 1-8 代码已提交并编译通过（`assets-refresh` 确认无编译错误）。

- [ ] **Step 1: 用 script-execute 创建材质 + 预制体 + 配置资产并挂接**

Run（`mcp__ai-game-developer__script-execute`，body-only 模式，先 `assets-refresh`）：

```csharp
using UnityEditor;

// 1) 材质：蓝白发光拖尾（Sprites/Default 保证 TrailRenderer 兼容）
var mat = new Material(Shader.Find("Sprites/Default"));
mat.color = new Color(0.7f, 0.9f, 1f, 0.8f);
AssetDatabase.CreateAsset(mat, "Assets/Prefabs/VFX/SwordQiTrail.mat");

// 2) 剑气预制体：根(SwordQiProjectile) + 子(TrailRenderer 竖直刀光)
var root = new GameObject("SwordQi");
root.AddComponent<SwordQiProjectile>();
var trailGo = new GameObject("Trail");
trailGo.transform.SetParent(root.transform);
trailGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
var trail = trailGo.AddComponent<TrailRenderer>();
trail.material = mat;
trail.time = 0.25f;
trail.startWidth = 0.5f;
trail.endWidth = 0.05f;
trail.startColor = new Color(0.85f, 0.95f, 1f, 0.9f);
trail.endColor = new Color(0.5f, 0.8f, 1f, 0f);
trail.minVertexDistance = 0.05f;
PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/VFX/SwordQi.prefab");
Object.DestroyImmediate(root);

// 3) 竖劈剑气配置
var cfg = ScriptableObject.CreateInstance<Hotfix.GameSystems.Skills.Data.SwordQiConfig>();
var so = new SerializedObject(cfg);
so.FindProperty("_prefab").objectReferenceValue =
    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/SwordQi.prefab");
so.FindProperty("_speed").floatValue = 18f;
so.FindProperty("_range").floatValue = 12f;
so.FindProperty("_detectRadius").floatValue = 1f;
so.FindProperty("_damagePercent").floatValue = 1f;
so.FindProperty("_spawnFrameIndex").intValue = 0;
so.FindProperty("_spawnOffset").vector3Value = new Vector3(0.8f, 1.0f, 0.6f);
so.ApplyModifiedProperties();
AssetDatabase.CreateAsset(cfg, "Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset");

// 4) 挂到竖劈 BA_Heavy（InstantSkillData, SkillID 10002）
var heavy = AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Skills.Data.InstantSkillData>(
    "Assets/PreRes/SkillsCfg/BA_Heavy.asset");
var so2 = new SerializedObject(heavy);
so2.FindProperty("_swordQi").objectReferenceValue = cfg;
so2.ApplyModifiedProperties();
AssetDatabase.SaveAssets();
```

- [ ] **Step 2: 验证资产与挂接**

Run: `assets-get-data`（assetPath: `Assets/PreRes/SkillsCfg/BA_Heavy.asset`，paths: `["_swordQi"]`）
Expected: `_swordQi` 引用非空（指向 SwordQi_Heavy.asset）。

Run: `assets-get-data`（assetPath: `Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset`）
Expected: `_prefab` 指向 `Assets/Prefabs/VFX/SwordQi.prefab`，数值 18/12/1/1/0。

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/VFX/SwordQiTrail.mat Assets/Prefabs/VFX/SwordQiTrail.mat.meta Assets/Prefabs/VFX/SwordQi.prefab Assets/Prefabs/VFX/SwordQi.prefab.meta Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset.meta Assets/PreRes/SkillsCfg/BA_Heavy.asset
git commit -m "feat: sword qi prefab + config wired to heavy attack (BA_Heavy)"
```

---

### Task 10: 全量回归 + spec 同步 + 收尾

**Files:**
- Modify: `docs/superpowers/specs/2026-08-07-sword-qi-attack2-design.md`（挂载点修正）

- [ ] **Step 1: 全量 EditMode 测试**

Run: `tests-run` (EditMode)
Expected: 全部 PASS（新增 4+4+5+2+3+1+4 = 23 个剑气相关测试 + 既有测试）。

- [ ] **Step 2: 手动冒烟（Unity 编辑器）**

- 打开游戏场景，运行
- 玩家对怪物打普攻链：普攻1（无剑气）→ 普攻2 竖劈 → 挥击中段发出一道竖直月牙剑气
- 验证：剑气直线飞出 12m 后消失；沿途怪物被命中扣血（伤害 ≈ 竖劈全额）；近处怪物吃双段（竖劈近战 + 剑气）
- 施放中被打断 → 已飞出的剑气继续飞完
- 检查 Console：无 Error/Warning（除预期告警路径外）

- [ ] **Step 3: 同步修正 spec 挂载点**

`docs/superpowers/specs/2026-08-07-sword-qi-attack2-design.md`：

- 第 4 节 `ComboSkillData 扩展` → 改为 `SkillData 基类扩展`，正文：

```markdown
### 4. `SkillData` 基类扩展

```csharp
[SerializeField] protected SwordQiConfig _swordQi;  // null = 技能不发射剑气
public SwordQiConfig SwordQi => _swordQi;
```

竖劈（`BA_Heavy.asset`，SkillID 10002，InstantSkillData）挂上即可；与 `_dashDistance` 同类通用能力，默认 null 不影响其他技能。
```

- 第 5 节 `SkillExecutor` 扩展的判定条件：`_skillData is ComboSkillData combo && combo.SwordQi != null` → `_skillData.SwordQi`（不限技能类型）

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-08-07-sword-qi-attack2-design.md
git commit -m "docs: sync sword qi spec — SwordQi hook on SkillData base (BA_Heavy is InstantSkillData)"
```

---

## 自审记录

- **Spec 覆盖**：SwordQiConfig（Task 3）/ SwordQiProjectile（Task 4）/ SkillDamageHelper（Task 2）/ SkillExecutor 触发（Task 8）/ 事件预留（Task 7）/ 预制体配置挂接（Task 9）/ 测试（各任务）/ 不做的事（无对应任务——YAGNI 项）✓
- **类型一致性**：`SwordQiSpawner.Spawn(cfg, damage, origin, dir)` 签名在 Task 6 定义、Task 8 调用一致；`ComponentPool<SwordQiProjectile>` 在 Task 5/6 一致；`TestHelpers.GetField` 在 Task 8 补充定义 ✓
- **行为等价**：ApplyDamage 重构后 `Heal(-damage)` → `Heal(-CalculatedDamage)`，蓄力乘法保持，CalculateFinalDamage 仅调用一次 ✓
