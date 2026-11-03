# 服务端怪物 AI P2（模式机 / 仇恨表 / 多技能 / 脱战回巢）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把 P1 的「最小目标保持 + 单技能 + Return」替换为 `Patrol | Combat | Evade | Dead` 模式机、`ThreatTable` 目标选择、模板多技能选择与脱战回巢/重置。

**架构：** 新增 `Server/Monster/Brain/` 纯核心（`ThreatTable` / `TargetSelector` / `SkillSelector` / `MonsterBrain`），`MonsterModule` 只负责 tick 顺序、实体 flush、施法与重生调度；伤害归属经 `CombatResolver` 回调接入；多技能由 `monster_config.json` 的 `skills` 数组驱动，`MonsterCastBuilder` 统一构建并校验 `CastRequest.Shape`。

**技术栈：** .NET 8 / xunit（服务端）、Unity EditMode NUnit（客户端变体映射）、System.Text.Json（`IncludeFields`）。

**范围（用户 2026-11-03 确认）：**
- 只做行为四件套；P1 的 nav 性能债与导出器 tie-break **不混入本轮**。
- 新增 `50005`（DogKnight 中距离）与 `50006`（SpiderBoss 低血 AoE）并在发布配置启用。
- 客户端 `skillId → 攻击动画变体` 映射纳入本轮；未知技能回退时间戳变体。

**规格来源：** `docs/superpowers/specs/2026-11-02-server-monster-navigation-ai-design.md` §5.1–§5.4、§7.2、§9.1、§10.1。规格未改动的行文不重复；本计划只记录实现口径。

---

## 文件结构

### 新增

| 文件 | 职责 |
|---|---|
| `Server/Monster/Brain/ThreatTable.cs` | 仇恨表 + `TargetSelector`（确定性排名、切换滞后、嘲讽覆盖） |
| `Server/Monster/Brain/SkillSelector.cs` | 按距离/血量/冷却/优先级选技能；`NeedsReposition` / `RepositionDistance` |
| `Server/Monster/Brain/MonsterBrain.cs` | 每 tick 有效性清理、感知、目标选择、模式转移、Evade 到家重置 |
| `Server/Monster/MonsterCastBuilder.cs` | 从 `MonsterSkillConfig` + `SkillEntry` 构建并校验 `CastRequest` |
| `Server/Server.Tests/Monster/ThreatTableTests.cs` | 仇恨表/选择器单测 |
| `Server/Server.Tests/Monster/SkillSelectorTests.cs` | 技能选择单测 |
| `Server/Server.Tests/Monster/MonsterCastBuilderTests.cs` | 形状构建/校验单测 |
| `Server/Server.Tests/Monster/BrainTests.cs` | 模式转移/目标选择单测 |
| `Server/Server.Tests/Monster/MonsterEvadeTests.cs` | 脱战/回巢/grace/超时单测 |
| `Server/Server.Tests/Monster/MonsterP2AcceptanceTests.cs` | §7.2 场景级验收 |
| `Server/Server.Tests/Combat/NpcDamageAttributionTests.cs` | 伤害归属回调单测 |
| `Server/Server.Tests/Monster/P2ConfigTests.cs` | 发布配置的 leash/skills 约束 |

### 修改

| 文件 | 改动 |
|---|---|
| `Server/Config/MonsterConfig.cs` | `MonsterSkillConfig`；`leashRadius` / `dropAggroRadius`；`MaxRangeOf` |
| `Server/Config/ConfigLoader.cs` | 默认配置的 skills/leash |
| `Server/Config/monster_config.json` | 四个模板改 `skills` 对象数组；DogKnight/SpiderBoss 加技能 |
| `Server/Monster/MonsterState.cs` | `MonsterMode`/`MonsterDamageKind`；威胁/模式/grace/嘲讽字段 |
| `Server/Monster/MonsterModule.cs` | 移除 `SkillIdOf`/`DetectTarget`/`AlertLevel`；接 `MonsterBrain`；Evade 超时调度 |
| `Server/Monster/MonsterBtActions.cs` | `Attack` 走选择器；`Chase` 用技能射程；新增 `Reposition` / `Evade` |
| `Server/Monster/Movement/PathFollower.cs` | `Reset()` |
| `Server/Handler/CastHandler.cs` | `NpcDamaged` 回调 + `SetNpcDamageObserver` |
| `Server/Server3C.cs` | `EnableMonsters` 接线伤害归属 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 新增 50005/50006 表项 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs` | skillId → 变体映射 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | `ScheduleAttack` 透传 skillId |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 广播接线传 `b.SkillId` |
| `Server/Server.Tests/Monster/MonsterModuleTests.cs` | 迁移 `AlertLevel`、`SkillIdOf`、目标丢失用例 |
| `Server/Server.Tests/Monster/MonsterAcceptanceTests.cs` | A2 改为「偏出视野锥 ≠ 丢目标」 |
| `Server/Server.Tests/Monster/MonsterMovementStateTests.cs` | `MonsterState` 构造签名 + `PrimarySkillId` |
| `Server/Server.Tests/Monster/MonsterBodyRadiusTests.cs` | 坏 JSON fixture 改 skills 对象 |
| `Server/Server.Tests/SyncCore/CombatRulesTests.cs` | 怪物技能延迟约束遍历 50001–50006 |
| `Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs` | 同上（客户端表） |
| `Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs` | 已知/未知技能变体用例 |
| `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` | NPC 广播按 skillId 推变体 |
| `Server/Monster/README.md` | P2 行为、配置字段、测试与已知限制 |

---

## 任务 1：ThreatTable 与 TargetSelector

**文件：**
- 创建：`Server/Monster/Brain/ThreatTable.cs`
- 测试：`Server/Server.Tests/Monster/ThreatTableTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class ThreatTableTests
{
    [Fact]
    public void AddDamage_Accumulates_Per_Player_With_Flat_And_Ratio()
    {
        var table = new ThreatTable();

        table.AddDamage(1, 10f);
        table.AddDamage(1, 5f);
        table.AddDamage(2, 20f);

        Assert.Equal(35f, table.ThreatOf(1), 3); // (10+10) + (5+10)
        Assert.Equal(30f, table.ThreatOf(2), 3); // 20 + 10
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void Initial_Threat_Is_Flat()
    {
        var table = new ThreatTable();
        table.AddInitial(7);
        Assert.Equal(ThreatTable.ThreatInitial, table.ThreatOf(7), 3);
    }

    [Fact]
    public void Highest_Breaks_Ties_By_Smaller_Player_Id()
    {
        var table = new ThreatTable();
        table.Add(2, 10f);
        table.Add(1, 10f);
        table.Add(3, 5f);

        Assert.Equal(1L, table.Highest(_ => true));
    }

    [Fact]
    public void Highest_Skips_Invalid_Entries()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 10f);

        Assert.Equal(2L, table.Highest(id => id != 1));
    }

    [Fact]
    public void Selector_Keeps_Current_Inside_Switch_Threshold()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 110f); // 只高 10%，低于 20% 滞后

        Assert.Equal(1L, TargetSelector.Select(table, currentTarget: 1, _ => true));
    }

    [Fact]
    public void Selector_Switches_When_New_Target_Exceeds_Threshold()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 130f); // 高 30%

        Assert.Equal(2L, TargetSelector.Select(table, currentTarget: 1, _ => true));
    }

    [Fact]
    public void Selector_Taunt_Overrides_Threat_While_Valid()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Ensure(2);

        Assert.Equal(2L, TargetSelector.Select(table, currentTarget: 1, _ => true, tauntTarget: 2));
    }

    [Fact]
    public void Selector_Falls_To_Highest_When_Current_Invalid()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 50f);

        Assert.Equal(2L, TargetSelector.Select(table, currentTarget: 1, id => id != 1));
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ThreatTable"`

预期：编译失败，`ThreatTable` / `TargetSelector` 不存在。

- [ ] **步骤 3：实现 ThreatTable 与 TargetSelector**

创建 `Server/Monster/Brain/ThreatTable.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace KcpServer.Monster
{
    /// <summary>
    /// 怪物仇恨表（P2）。主循环线程独占，无锁。
    /// 威胁来源：直接伤害（damage × ThreatPerDamage + ThreatFlat）、首次感知（ThreatInitial）、嘲讽。
    /// v1 不衰减；脱战 Clear()。同分按 playerId 升序，保证跨进程确定性。
    /// </summary>
    public sealed class ThreatTable
    {
        public const float ThreatPerDamage = 1.0f;
        public const float ThreatFlat = 10f;
        public const float ThreatInitial = 10f;
        public const float TargetSwitchThreshold = 0.2f;

        private readonly Dictionary<long, float> _threat = new Dictionary<long, float>();

        public int Count => _threat.Count;
        public IReadOnlyCollection<long> PlayerIds => _threat.Keys;

        public void Add(long playerId, float amount)
        {
            if (amount < 0f) return;
            _threat.TryGetValue(playerId, out float current);
            _threat[playerId] = current + amount;
        }

        /// <summary>直接伤害的威胁：伤害本身 + 固定值（格挡后按实际伤害调用）。</summary>
        public void AddDamage(long playerId, float damage)
            => Add(playerId, damage * ThreatPerDamage + ThreatFlat);

        public void AddInitial(long playerId) => Add(playerId, ThreatInitial);

        /// <summary>嘲讽目标可能尚未产生伤害：先建 0 威胁条目，再由选择器临时置顶。</summary>
        public void Ensure(long playerId)
        {
            if (!_threat.ContainsKey(playerId)) _threat[playerId] = 0f;
        }

        public bool Remove(long playerId) => _threat.Remove(playerId);
        public void Clear() => _threat.Clear();
        public bool Contains(long playerId) => _threat.ContainsKey(playerId);
        public float ThreatOf(long playerId)
            => _threat.TryGetValue(playerId, out float value) ? value : 0f;

        /// <summary>最高威胁的有效目标；同分取较小 playerId（与字典遍历顺序无关）。</summary>
        public long? Highest(Func<long, bool> isValid)
        {
            long? best = null;
            float bestThreat = float.MinValue;
            foreach (var pair in _threat)
            {
                if (!isValid(pair.Key)) continue;
                if (best == null
                    || pair.Value > bestThreat
                    || (pair.Value == bestThreat && pair.Key < best.Value))
                {
                    best = pair.Key;
                    bestThreat = pair.Value;
                }
            }
            return best;
        }
    }

    /// <summary>目标选择：最高威胁 + 当前目标 20% 滞后 + 嘲讽覆盖。</summary>
    public static class TargetSelector
    {
        public static long? Select(ThreatTable threats, long? currentTarget, Func<long, bool> isValid,
            long? tauntTarget = null, float switchThreshold = ThreatTable.TargetSwitchThreshold)
        {
            if (tauntTarget.HasValue && isValid(tauntTarget.Value))
                return tauntTarget;

            long? highest = threats.Highest(isValid);
            if (!highest.HasValue) return null;
            if (!currentTarget.HasValue || currentTarget.Value == highest.Value) return highest;
            if (!isValid(currentTarget.Value)) return highest;

            // 新目标必须比当前目标高 20% 以上才换；否则保留当前，避免两人抢仇恨来回抖。
            float currentThreat = threats.ThreatOf(currentTarget.Value);
            float highestThreat = threats.ThreatOf(highest.Value);
            return highestThreat > currentThreat * (1f + switchThreshold) ? highest : currentTarget;
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ThreatTable"`

预期：8 个测试全部通过。

- [ ] **步骤 5：Commit**

```bash
git add Server/Monster/Brain/ThreatTable.cs Server/Server.Tests/Monster/ThreatTableTests.cs
git commit -m "feat(monster): add threat table and deterministic target selector"
```

---

## 任务 2：配置 schema（skills / leash / dropAggroRadius）

**文件：**
- 修改：`Server/Config/MonsterConfig.cs`、`Server/Config/ConfigLoader.cs`、`Server/Config/monster_config.json`
- 修改：`Server/Server.Tests/Monster/MonsterBodyRadiusTests.cs:108-111`
- 测试：`Server/Server.Tests/Monster/P2ConfigTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Cool.SyncCore;
using KcpServer.Config;
using Xunit;

namespace KcpServer.Tests.Monster;

public class P2ConfigTests
{
    private static Dictionary<int, MonsterData> Published()
        => ConfigLoader.LoadMonsterTemplates(
            Path.Combine(AppContext.BaseDirectory, "Config", "monster_config.json"));

    [Fact]
    public void Published_Leash_Is_Greater_Than_Detection_Plus_Patrol()
    {
        foreach (var template in Published().Values)
        {
            Assert.True(template.LeashRadiusOf > template.detectionRadius + template.patrolRadius,
                $"{template.name}: leash={template.LeashRadiusOf} <= detection+patrol="
                + $"{template.detectionRadius + template.patrolRadius}");
            Assert.True(template.DropAggroRadiusOf >= template.LeashRadiusOf);
        }
    }

    [Fact]
    public void Published_Skills_Exist_And_Fit_Combat_Rules()
    {
        foreach (var template in Published().Values)
        {
            Assert.NotEmpty(template.skills);
            foreach (var skill in template.skills)
            {
                Assert.True(CombatRules.TryGet(skill.skillId, out var entry),
                    $"{template.name}: skill {skill.skillId} missing from CombatRules");
                float maxRange = skill.MaxRangeOf(entry!);
                Assert.True(skill.minRange <= maxRange + 0.01f,
                    $"{template.name}: skill {skill.skillId} minRange={skill.minRange} > maxRange={maxRange}");
                if (entry.ShapeType == 0)
                    Assert.True(maxRange <= entry.MaxRange + 0.01f,
                        $"{template.name}: skill {skill.skillId} maxRange={maxRange} > table {entry.MaxRange}");
            }
        }
    }

    [Fact]
    public void DogKnight_And_SpiderBoss_Ship_Multi_Skill()
    {
        var templates = Published();
        Assert.True(templates[3].skills.Count >= 2);
        Assert.True(templates[4].skills.Count >= 2);
        Assert.Contains(templates[3].skills, s => s.skillId == 50005 && s.hpBelow < 1f);
        Assert.Contains(templates[4].skills, s => s.skillId == 50006 && s.hpBelow < 1f);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~P2Config"`

预期：编译失败，`LeashRadiusOf` / `MonsterSkillConfig` 不存在。

- [ ] **步骤 3：实现配置结构**

修改 `Server/Config/MonsterConfig.cs`：

```csharp
using System;
using System.Collections.Generic;
using Cool.SyncCore;

namespace KcpServer.Config
{
    public class MonsterConfig
    {
        public List<MonsterData> monsters { get; set; } = new();
    }

    /// <summary>模板技能条目（P2）。缺省值：minRange=0、maxRange=0（取 CombatRules 表值）、
    /// hpBelow=1、hpAbove=0、priority=0。</summary>
    public class MonsterSkillConfig
    {
        public int skillId;
        public int priority;
        public float minRange;
        public float maxRange;      // 0 = 用 CombatRules 表值（Circle/投射物取 engagement 距离）
        public float hpBelow = 1f;  // 自身血量比例 <= 此值才可用
        public float hpAbove;       // 自身血量比例 >= 此值才可用

        /// <summary>技能可用距离上限。Single 默认取表 MaxRange；Circle/投射物默认取
        /// max(MaxRange, MaxAreaRadius)，避免 Circle 表 MaxRange=0 时永远不可用。</summary>
        public float MaxRangeOf(SkillEntry entry)
            => maxRange > 0f
                ? maxRange
                : (entry.ShapeType == 0 ? entry.MaxRange : Math.Max(entry.MaxRange, entry.MaxAreaRadius));
    }

    public class MonsterData
    {
        public int templateId;
        public string name;
        public float hp;
        public float moveSpeed;
        public float bodyRadius;
        public float detectionRadius;
        public float visionAngle;
        public float attackRange;
        public float patrolRadius;

        public float leashRadius;       // 0 = detectionRadius + patrolRadius + 10
        public float dropAggroRadius;   // 0 = LeashRadiusOf
        public List<MonsterSkillConfig> skills = new();

        public float LeashRadiusOf
            => leashRadius > 0f ? leashRadius : detectionRadius + patrolRadius + 10f;

        public float DropAggroRadiusOf
            => dropAggroRadius > 0f ? dropAggroRadius : LeashRadiusOf;
    }
}
```

> 注意：`MonsterData` 的既有字段（templateId/name/hp/moveSpeed/bodyRadius/detectionRadius/visionAngle/attackRange/patrolRadius）原样保留，本步骤只追加字段并移除 `List<string> skills`。

修改 `Server/Config/ConfigLoader.cs` 的 `CreateDefaultConfig()`：每个模板加 `leashRadius` 与对象数组 `skills`。以 Slime 为例，其余三个模板同法：

```csharp
new MonsterData
{
    templateId = 1,
    name = "Slime",
    hp = 100,
    moveSpeed = 2.0f,
    bodyRadius = 0.5f,
    detectionRadius = 8,
    visionAngle = 120,
    attackRange = 1.5f,
    patrolRadius = 5,
    leashRadius = 23f, // 8 + 5 + 10
    skills = new List<MonsterSkillConfig>
    {
        new MonsterSkillConfig { skillId = 50001, priority = 0, minRange = 0f, maxRange = 1.5f }
    }
},
```

默认配置四个模板的取值：

| 模板 | leashRadius | skills |
|---|---|---|
| Slime(1) | 23 | `{50001, maxRange=1.5}` |
| Wolf(2) | 35 | `{50002, maxRange=2.0}` |
| DogKnight(3) | 34 | `{50003, maxRange=2.2}`、`{50005, minRange=2, maxRange=6, hpBelow=0.5, priority=10}` |
| SpiderBoss(4) | 47 | `{50004, maxRange=2.5}`、`{50006, maxRange=4, hpBelow=0.5, priority=10}` |

修改 `Server/Config/monster_config.json` 为同样的对象数组。DogKnight 与 SpiderBoss 的完整条目：

```json
{
  "templateId": 3,
  "name": "DogKnight",
  "hp": 300,
  "moveSpeed": 2.8,
  "bodyRadius": 0.88,
  "detectionRadius": 18,
  "visionAngle": 120,
  "attackRange": 2.2,
  "patrolRadius": 6,
  "leashRadius": 34,
  "skills": [
    { "skillId": 50003, "priority": 0, "minRange": 0, "maxRange": 2.2 },
    { "skillId": 50005, "priority": 10, "minRange": 2, "maxRange": 6.0, "hpBelow": 0.5 }
  ]
}
```

```json
{
  "templateId": 4,
  "name": "SpiderBoss",
  "hp": 1500,
  "moveSpeed": 3.2,
  "bodyRadius": 1.74,
  "detectionRadius": 25,
  "visionAngle": 140,
  "attackRange": 2.5,
  "patrolRadius": 12,
  "leashRadius": 47,
  "skills": [
    { "skillId": 50004, "priority": 0, "minRange": 0, "maxRange": 2.5 },
    { "skillId": 50006, "priority": 10, "minRange": 0, "maxRange": 4.0, "hpBelow": 0.5 }
  ]
}
```

修改 `Server/Server.Tests/Monster/MonsterBodyRadiusTests.cs` 的坏 JSON fixture（只改 `skills` 片段）：

```csharp
"\"detectionRadius\":5,\"visionAngle\":90,\"attackRange\":1.0,\"patrolRadius\":2," +
"\"skills\":[{\"skillId\":50003,\"priority\":0,\"maxRange\":1.0}]}]}"
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~P2Config"`

预期：3 个测试通过。再跑 `MonsterBodyRadiusTests` 确认坏 JSON 仍按兜底逻辑工作。

- [ ] **步骤 5：Commit**

```bash
git add Server/Config/MonsterConfig.cs Server/Config/ConfigLoader.cs Server/Config/monster_config.json \
  Server/Server.Tests/Monster/P2ConfigTests.cs Server/Server.Tests/Monster/MonsterBodyRadiusTests.cs
git commit -m "feat(monster): add multi-skill and leash config schema"
```

---

## 任务 3：SkillSelector、MonsterCastBuilder 与 50005/50006 表项

**文件：**
- 创建：`Server/Monster/Brain/SkillSelector.cs`、`Server/Monster/MonsterCastBuilder.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs:145-152`
- 修改：`Server/Server.Tests/SyncCore/CombatRulesTests.cs:185`
- 修改：`Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs`（同名循环）
- 修改：`Server/Server.Tests/Monster/MonsterModuleTests.cs:289-303`
- 测试：`Server/Server.Tests/Monster/SkillSelectorTests.cs`、`Server/Server.Tests/Monster/MonsterCastBuilderTests.cs`

- [ ] **步骤 1：编写失败的测试**

`Server/Server.Tests/Monster/SkillSelectorTests.cs`：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class SkillSelectorTests
{
    private static (MonsterState State, PlayerState Self) Make(float hp, float maxHp = 100f)
    {
        var config = new MonsterData
        {
            templateId = 3, name = "DogKnight", hp = maxHp, moveSpeed = 2.8f, bodyRadius = 0.88f,
            detectionRadius = 18f, visionAngle = 120f, attackRange = 2.2f, patrolRadius = 6f,
            leashRadius = 34f,
            skills = new List<MonsterSkillConfig>
            {
                new MonsterSkillConfig { skillId = 50003, priority = 0, minRange = 0f, maxRange = 2.2f },
                new MonsterSkillConfig { skillId = 50005, priority = 10, minRange = 2f, maxRange = 6f, hpBelow = 0.5f }
            }
        };
        var store = new EntityStore();
        var self = store.AddNpc(1, "self", 3, maxHp, new Vector3(0f, 0f, 0f));
        self.ApplyDamage(maxHp - hp);
        var state = new MonsterState(1, 3, config);
        return (state, self);
    }

    [Fact]
    public void Selects_Higher_Priority_Skill_When_Both_In_Range()
    {
        var (state, self) = Make(hp: 40f); // 低于 50%
        // 2.1m 落在两条技能的重叠区：50003 [0,2.2] 与 50005 [2,6] 都可选，
        // 这样才真的验证 priority 降序（3m 时 50003 先被距离过滤，只剩 50005）。
        var selected = SkillSelector.Select(state, self, distance: 2.1f, tick: 0);
        Assert.NotNull(selected);
        Assert.Equal(50005, selected!.Value.Entry.SkillId);
    }

    [Fact]
    public void Ties_Break_By_Smaller_Skill_Id()
    {
        var config = new MonsterData
        {
            templateId = 10, name = "Tie", hp = 100f, moveSpeed = 2f, bodyRadius = 0.5f,
            detectionRadius = 10f, visionAngle = 120f, attackRange = 2f, patrolRadius = 2f,
            skills = new List<MonsterSkillConfig>
            {
                // 列表顺序故意倒置：选择器必须按 skillId 升序，而不是配置顺序。
                new MonsterSkillConfig { skillId = 50003, priority = 0, maxRange = 2.2f },
                new MonsterSkillConfig { skillId = 50002, priority = 0, maxRange = 2.0f }
            }
        };
        var store = new EntityStore();
        var self = store.AddNpc(1, "self", 10, 100f, new Vector3(0f, 0f, 0f));
        var state = new MonsterState(1, 10, config);

        var selected = SkillSelector.Select(state, self, distance: 1.5f, tick: 0);

        Assert.NotNull(selected);
        Assert.Equal(50002, selected!.Value.Entry.SkillId); // 同为 priority 0，较小 skillId 胜出
    }

    [Fact]
    public void Skips_High_Priority_Skill_Above_Hp_Threshold()
    {
        var (state, self) = Make(hp: 80f);
        var selected = SkillSelector.Select(state, self, distance: 3f, tick: 0);
        Assert.Null(selected); // 50003 只够 2.2m，50005 血量条件不满足
    }

    [Fact]
    public void Skips_Skill_On_Cooldown()
    {
        var (state, self) = Make(hp: 40f);
        self.RecordCast(50005, tick: 0);
        var selected = SkillSelector.Select(state, self, distance: 3f, tick: 5);
        Assert.Null(selected); // 50005 冷却 4s=60 tick；50003 够不到 3m
    }

    [Fact]
    public void Selects_Melee_When_Target_Inside_Melee_Range()
    {
        var (state, self) = Make(hp: 40f);
        var selected = SkillSelector.Select(state, self, distance: 1.5f, tick: 0);
        Assert.NotNull(selected);
        Assert.Equal(50003, selected!.Value.Entry.SkillId); // 50005 minRange=2 排除了 1.5m
    }

    [Fact]
    public void NeedsReposition_When_Target_Inside_All_MinRanges()
    {
        var config = new MonsterData
        {
            templateId = 9, name = "RangedOnly", hp = 100f, moveSpeed = 2f, bodyRadius = 0.5f,
            detectionRadius = 10f, visionAngle = 120f, attackRange = 1f, patrolRadius = 2f,
            skills = new List<MonsterSkillConfig>
            {
                new MonsterSkillConfig { skillId = 50005, priority = 0, minRange = 2f, maxRange = 6f }
            }
        };
        var store = new EntityStore();
        var self = store.AddNpc(1, "self", 9, 100f, new Vector3(0f, 0f, 0f));
        var state = new MonsterState(1, 9, config);

        Assert.True(SkillSelector.NeedsReposition(state, self, distance: 1f));
        Assert.Equal(2f, SkillSelector.RepositionDistance(state, self, distance: 1f), 3);
        Assert.False(SkillSelector.NeedsReposition(state, self, distance: 3f));
    }
}
```

`Server/Server.Tests/Monster/MonsterCastBuilderTests.cs`：

```csharp
using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterCastBuilderTests
{
    private static (MonsterState State, SelectedSkill Selected) Make(int skillId, int priority = 0,
        float minRange = 0f, float maxRange = 0f, float hpBelow = 1f)
    {
        var config = new MonsterData
        {
            templateId = 1, name = "T", hp = 100f, moveSpeed = 2f, bodyRadius = 0.5f,
            detectionRadius = 10f, visionAngle = 120f, attackRange = 2f, patrolRadius = 2f,
            skills = new System.Collections.Generic.List<MonsterSkillConfig>
            {
                new MonsterSkillConfig { skillId = skillId, priority = priority,
                    minRange = minRange, maxRange = maxRange, hpBelow = hpBelow }
            }
        };
        var state = new MonsterState(1, 1, config) { TargetId = 7L, Position = new Vector3(0f, 0f, 0f) };
        Assert.True(CombatRules.TryGet(skillId, out var entry));
        var selected = new SelectedSkill(config.skills[0], entry!);
        return (state, selected);
    }

    [Fact]
    public void Single_Shape_Uses_Config_Range_Within_Table_Limit()
    {
        var (state, selected) = Make(50005, maxRange: 6f);
        Assert.True(MonsterCastBuilder.TryBuild(state, selected, new Vector3(0f, 0f, 3f), out var request));
        Assert.Equal(50005, request.SkillId);
        Assert.Equal(7L, request.TargetPlayerId);
        Assert.Equal(0, request.Shape.ShapeType);
        Assert.Equal(6f, request.Shape.Range, 3);
        Assert.Equal(selected.Entry.MaxAngle, request.Shape.Angle, 3);
    }

    [Fact]
    public void Circle_Shape_Uses_Table_Area_Radius()
    {
        var (state, selected) = Make(50006, maxRange: 4f);
        Assert.True(MonsterCastBuilder.TryBuild(state, selected, new Vector3(0f, 0f, 2f), out var request));
        Assert.Equal(1, request.Shape.ShapeType);
        Assert.Equal(4f, request.Shape.AreaRadius, 3);
        Assert.Equal(0f, request.Shape.Range, 3);
    }

    [Fact]
    public void Projectile_Shape_Is_Zero_And_Impact_Is_Clamped()
    {
        var (state, selected) = Make(30001, maxRange: 2f);
        Assert.True(MonsterCastBuilder.TryBuild(state, selected, new Vector3(0f, 0f, 100f), out var request));
        Assert.Equal(0, request.Shape.ShapeType);
        Assert.Equal(0f, request.Shape.Range, 3);
        float dx = request.ImpactX, dz = request.ImpactZ;
        Assert.Equal(30f, Math.Sqrt(dx * dx + dz * dz), 2); // MaxImpactDistance=30
        Assert.True(CombatRules.TryGet(30001, out var entry));
        Assert.True(CombatRules.ValidateShape(request.Shape, entry!));
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~SkillSelector|FullyQualifiedName~MonsterCastBuilder"`

预期：编译失败，`SkillSelector` / `SelectedSkill` / `MonsterCastBuilder` 不存在；50005/50006 不在表内。

- [ ] **步骤 3：实现 SkillSelector**

创建 `Server/Monster/Brain/SkillSelector.cs`：

```csharp
using System;
using Cool.SyncCore;

namespace KcpServer.Monster
{
    public readonly struct SelectedSkill
    {
        public readonly MonsterSkillConfig Config;
        public readonly SkillEntry Entry;

        public SelectedSkill(MonsterSkillConfig config, SkillEntry entry)
        {
            Config = config;
            Entry = entry;
        }
    }

    /// <summary>
    /// 多技能选择（P2）。冷却只认 CombatRules；距离/血量/优先级来自模板 skills。
    /// 排序：priority 降序 → skillId 升序（确定性）。
    /// </summary>
    public static class SkillSelector
    {
        private const float Epsilon = 0.001f;

        private static bool HpAllowed(MonsterSkillConfig skill, PlayerState self)
        {
            float ratio = self.MaxHp > 0f ? self.Hp / self.MaxHp : 0f;
            return ratio <= skill.hpBelow + 0.0001f && ratio >= skill.hpAbove - 0.0001f;
        }

        /// <summary>就绪且距离/血量满足的最高优先级技能；无则 null。</summary>
        public static SelectedSkill? Select(MonsterState monster, PlayerState self, float distance, uint tick)
        {
            SelectedSkill? best = null;
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance < skill.minRange - Epsilon || distance > maxRange + Epsilon) continue;
                if (!self.IsCastReady(skill.skillId, tick, CombatRules.CooldownTicks(entry))) continue;

                if (best == null
                    || skill.priority > best.Value.Config.priority
                    || (skill.priority == best.Value.Config.priority && skill.skillId < best.Value.Config.skillId))
                {
                    best = new SelectedSkill(skill, entry);
                }
            }
            return best;
        }

        /// <summary>距离是否落在任一（血量条件满足的）技能区间；忽略冷却——用于 Chase 停步。
        /// 与 Select 分开：目标在射程内但技能冷却时，怪物应站定等待而不是继续贴近。</summary>
        public static bool IsWithinAnySkillRange(MonsterState monster, PlayerState self, float distance)
        {
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance >= skill.minRange - Epsilon && distance <= maxRange + Epsilon) return true;
            }
            return false;
        }

        /// <summary>目标太近、所有满足血量条件的技能 minRange 都够不到 → 需要后撤。</summary>
        public static bool NeedsReposition(MonsterState monster, PlayerState self, float distance)
        {
            bool anyTooClose = false;
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance >= skill.minRange - Epsilon && distance <= maxRange + Epsilon) return false;
                if (distance < skill.minRange - Epsilon) anyTooClose = true;
            }
            return anyTooClose;
        }

        /// <summary>后撤目标距离：所有“太近”技能 minRange 的最大值（一次退到都满足）。</summary>
        public static float RepositionDistance(MonsterState monster, PlayerState self, float distance)
        {
            float desired = 0f;
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance <= maxRange + Epsilon && distance < skill.minRange - Epsilon)
                    desired = MathF.Max(desired, skill.minRange);
            }
            return desired;
        }
    }
}
```

创建 `Server/Monster/MonsterCastBuilder.cs`：

```csharp
using System;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer.Monster
{
    /// <summary>把模板技能条目转成 CastRequest（形状按表上限构建，入队前 ValidateShape）。</summary>
    public static class MonsterCastBuilder
    {
        public static bool TryBuild(MonsterState monster, SelectedSkill selected,
            Vector3 targetPosition, out CastRequest request)
        {
            var entry = selected.Entry;
            request = new CastRequest
            {
                SkillId = entry.SkillId,
                TargetPlayerId = monster.TargetId ?? 0L
            };

            if (entry.Projectile != null)
            {
                // 投射物：形状为零（ValidateShape 只要求 ShapeType 一致、上限不超）。
                request.Shape = new HitShape { ShapeType = entry.ShapeType };
                var spec = entry.Projectile;
                if (spec.Kind == ProjectileKind.CircleAtPoint)
                {
                    float dx = targetPosition.X - monster.Position.X;
                    float dz = targetPosition.Z - monster.Position.Z;
                    float distance = MathF.Sqrt(dx * dx + dz * dz);
                    float maxImpact = spec.MaxImpactDistance > 0f ? spec.MaxImpactDistance : distance;
                    float scale = distance > maxImpact && distance > 0.0001f ? maxImpact / distance : 1f;
                    request.ImpactX = monster.Position.X + dx * scale;
                    request.ImpactY = targetPosition.Y;
                    request.ImpactZ = monster.Position.Z + dz * scale;
                }
            }
            else if (entry.ShapeType == 1)
            {
                request.Shape = new HitShape
                {
                    ShapeType = 1,
                    AreaRadius = entry.MaxAreaRadius,
                    Range = 0f,
                    Angle = 0f
                };
            }
            else
            {
                request.Shape = new HitShape
                {
                    ShapeType = 0,
                    Range = MathF.Min(selected.Config.MaxRangeOf(entry), entry.MaxRange),
                    Angle = entry.MaxAngle
                };
            }

            return CombatRules.ValidateShape(request.Shape, entry);
        }
    }
}
```

- [ ] **步骤 4：新增 CombatRules 表项**

在 `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` 的 50004 之后追加：

```csharp
// P2 多技能：50005 DogKnight 中距离重击（复用 Attack01/02 动画，HitDelay 沿用 0.35s）、
// 50006 SpiderBoss 低血踏地 AoE（shapeType=1，表 MaxRange=0，范围由 MaxAreaRadius=4 决定）。
// 两者 HitDelay 必须 <= CooldownSeconds（CombatRulesTests 有约束）。
[50005] = new SkillEntry(50005, shapeType: 0, maxRange: 6f, maxAreaRadius: 0f, maxAngle: 60f,
    cooldownSeconds: 4f, baseDamage: 25f, criticalRate: 0f, hitDelaySeconds: 0.35f),
[50006] = new SkillEntry(50006, shapeType: 1, maxRange: 0f, maxAreaRadius: 4f, maxAngle: 0f,
    cooldownSeconds: 5f, baseDamage: 40f, criticalRate: 0f, hitDelaySeconds: 0.35f),
```

同步把两个 `CombatRulesTests` 中的循环改为 `new[] { 50001, 50002, 50003, 50004, 50005, 50006 }`。

把 `MonsterModuleTests.Template_AttackRange_Fits_Its_Combat_Rule` 替换为遍历配置技能：

```csharp
[Theory]
[InlineData(1)]
[InlineData(2)]
[InlineData(3)]
[InlineData(4)]
public void Template_Skills_Fit_Their_Combat_Rules(int templateId)
{
    var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Config", "monster_config.json");
    var config = ConfigLoader.LoadMonsterTemplates(path)[templateId];

    Assert.NotEmpty(config.skills);
    foreach (var skill in config.skills)
    {
        Assert.True(CombatRules.TryGet(skill.skillId, out var rule),
            $"template {templateId} 缺 {skill.skillId} 表项");
        float maxRange = skill.MaxRangeOf(rule!);
        if (rule.ShapeType == 0)
            Assert.True(maxRange <= rule.MaxRange + 0.01f,
                $"template {templateId} skill {skill.skillId} maxRange={maxRange} > {rule.MaxRange}");
    }
}
```

- [ ] **步骤 5：运行测试验证通过**

运行：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~SkillSelector"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterCastBuilder"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatRules"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterModuleTests"
```

预期：全部通过（`MonsterModuleTests` 中旧的 `SkillIdOf` 调用已在本步骤替换）。

- [ ] **步骤 6：Commit**

```bash
git add Server/Monster/Brain/SkillSelector.cs Server/Monster/MonsterCastBuilder.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs \
  Server/Server.Tests/SyncCore/CombatRulesTests.cs Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs \
  Server/Server.Tests/Monster/SkillSelectorTests.cs Server/Server.Tests/Monster/MonsterCastBuilderTests.cs \
  Server/Server.Tests/Monster/MonsterModuleTests.cs
git commit -m "feat(monster): add skill selector, cast builder, and mid-range skills"
```

---

## 任务 4：伤害归属回调

**文件：**
- 修改：`Server/Handler/CastHandler.cs`
- 测试：`Server/Server.Tests/Combat/NpcDamageAttributionTests.cs`
- （Server3C 的接线推迟到任务 5：`MonsterModule.OnDamageTaken` 与 `MonsterDamageKind` 在那里落地，任务 4 保持可独立编译。）

- [ ] **步骤 1：编写失败的测试**

```csharp
using System;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests.Combat;

public class NpcDamageAttributionTests
{
    private static (EntityStore Store, PlayerState Player, PlayerState Npc) Make()
    {
        var store = new EntityStore();
        var npc = store.AddNpc(2_000_001, "npc", 1, 100f, new Vector3(0f, 0f, 1f));
        npc.Rotation = 180f; // 面向 -Z（朝向玩家）
        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        return (store, player, npc);
    }

    [Fact]
    public void Player_Hit_On_Npc_Raises_Callback_With_Final_Damage()
    {
        var (store, _, _) = Make();
        var resolver = new CombatResolver(store, new Random(42));
        long victim = 0, attacker = 0;
        float damage = 0f;
        resolver.NpcDamaged = (npcId, playerId, amount) =>
        {
            victim = npcId; attacker = playerId; damage = amount;
        };

        Assert.True(CombatRules.TryGet(10001, out var entry));
        var request = new CastRequest
        {
            SkillId = 10001,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        };
        Assert.True(resolver.Decide(1, request, 0).Accepted);
        resolver.DrainResolved(CombatRules.HitDelayTicks(entry!));

        Assert.Equal(2_000_001L, victim);
        Assert.Equal(1L, attacker);
        Assert.Equal(entry!.BaseDamage, damage, 3);
    }

    [Fact]
    public void Blocked_Hit_Reports_Reduced_Damage()
    {
        var (store, _, npc) = Make();
        npc.IsDefending = true;
        var resolver = new CombatResolver(store, new Random(42));
        float damage = 0f;
        resolver.NpcDamaged = (_, _, amount) => damage = amount;

        Assert.True(CombatRules.TryGet(10001, out var entry));
        var request = new CastRequest
        {
            SkillId = 10001,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        };
        resolver.Decide(1, request, 0);
        resolver.DrainResolved(CombatRules.HitDelayTicks(entry!));

        Assert.Equal(entry!.BaseDamage * CombatRules.BlockDamageScale, damage, 3);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~NpcDamageAttribution"`

预期：编译失败，`CombatResolver.NpcDamaged` 不存在。

- [ ] **步骤 3：实现回调**

在 `CombatResolver`（`Server/Handler/CastHandler.cs`）的 `NpcLineOfSight` 属性旁追加：

```csharp
/// <summary>NPC 受玩家伤害回调（参数：npcId, attackerId, 实际伤害）。
/// 在 ApplyHit 扣血后调用；格挡分支传减伤后的伤害。null = 不归属（测试/无怪物宿主）。</summary>
public Action<long, long, float>? NpcDamaged { get; set; }
```

在 `ApplyHit` 的 `target.ApplyDamage(damage);` 之后追加：

```csharp
if (target.Kind == (byte)EntityKind.Npc)
    NpcDamaged?.Invoke(target.PlayerId, pending.CasterId, damage);
```

在 `CastHandler` 的 `SetNpcLineOfSight` 之后追加：

```csharp
/// <summary>接线 NPC 受击归属（Server3C 在 EnableMonsters 后调用；null = 关闭）。</summary>
public void SetNpcDamageObserver(Action<long, long, float>? observer)
    => _resolver.NpcDamaged = observer;
```

> 本任务只落裁决核心回调与 setter，`Server3C.EnableMonsters` 的接线在任务 5（等 `MonsterModule.OnDamageTaken` 落地）完成。

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~NpcDamageAttribution"`

预期：2 个测试通过。

- [ ] **步骤 5：Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Server.Tests/Combat/NpcDamageAttributionTests.cs
git commit -m "feat(monster): expose npc damage attribution callback"
```

---

## 任务 5：模式机与目标选择（MonsterBrain）

**文件：**
- 创建：`Server/Monster/Brain/MonsterBrain.cs`
- 修改：`Server/Monster/MonsterState.cs`
- 修改：`Server/Monster/MonsterModule.cs`
- 修改：`Server/Monster/Movement/PathFollower.cs`
- 修改：`Server/Server3C.cs:82-96`（伤害归属接线）
- 测试：`Server/Server.Tests/Monster/BrainTests.cs`
- 迁移：`MonsterModuleTests` / `MonsterAcceptanceTests` / `MonsterMovementStateTests`

- [ ] **步骤 1：编写失败的核心测试**

`Server/Server.Tests/Monster/BrainTests.cs`：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class BrainTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State) Make(
        Vector3 spawn, float hp = 100f)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, spawn, spawn);
        var entity = store.GetPlayer(id)!;
        entity.Hp = hp;
        return (store, module, module.GetState(id)!);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.IsLoggedIn = true;
        p.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return p;
    }

    [Fact]
    public void Perception_Transitions_Patrol_To_Combat_With_Initial_Threat()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f)); // 视野锥内（朝向原点 = +Z）

        module.Tick(0);

        Assert.Equal(MonsterMode.Combat, state.Mode);
        Assert.Equal(1L, state.TargetId);
        Assert.Equal(ThreatTable.ThreatInitial, state.Threats.ThreatOf(1), 3);
    }

    [Fact]
    public void Damage_While_Patrolling_Transitions_To_Combat_With_Attacker()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Patrol;
        state.Threats.Clear();
        state.TargetId = null;

        // 直接走模块公开入口（生产链由任务 4 的 CombatResolver 回调调用）
        module.OnDamageTaken(state.NpcId, 1, 20f, MonsterDamageKind.Direct);
        module.Tick(0);

        Assert.Equal(MonsterMode.Combat, state.Mode);
        Assert.Equal(1L, state.TargetId);
    }

    [Fact]
    public void Current_Target_Kept_Until_New_Threat_Exceeds_Threshold()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        Login(store, 2, new Vector3(0f, 0f, 3.5f));
        state.Threats.Clear();
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 110f);

        module.Tick(0);
        Assert.Equal(1L, state.TargetId); // 10% 不换

        state.Threats.Add(2, 30f); // 总计 140 > 120
        module.Tick(1);
        Assert.Equal(2L, state.TargetId);
    }

    [Fact]
    public void Windup_Locks_Target_Even_When_Threat_Flips()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        Login(store, 2, new Vector3(0f, 0f, 3.5f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 10f);
        state.Threats.Add(2, 1000f);
        state.AttackResolveTick = 10;

        module.Tick(0);
        Assert.Equal(1L, state.TargetId);
    }

    [Fact]
    public void Leaving_Leash_Enters_Evade()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);

        state.Position = new Vector3(0f, 0f, 100f); // > Slime leash 23
        module.Tick(0);

        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
    }

    [Fact]
    public void Target_Death_With_No_Other_Threat_Enters_Evade_After_Grace()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        var player = Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);
        player.ApplyDamage(999f);

        for (uint t = 0; t < MonsterBrain.TargetLostGraceTicks; t++)
        {
            module.Tick(t);
            Assert.Equal(MonsterMode.Combat, state.Mode);
        }
        module.Tick(MonsterBrain.TargetLostGraceTicks);
        Assert.Equal(MonsterMode.Evade, state.Mode);
    }

    [Fact]
    public void Target_Death_With_Another_Threat_Switches_Instead_Of_Evading()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        var first = Login(store, 1, new Vector3(0f, 0f, 3f));
        Login(store, 2, new Vector3(0f, 0f, 3.5f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 50f);
        first.ApplyDamage(999f);

        module.Tick(0);
        Assert.Equal(2L, state.TargetId);
        Assert.Equal(MonsterMode.Combat, state.Mode);
    }

    [Fact]
    public void Post_Evade_Grace_Blocks_Perception_But_Damage_Reaggros()
    {
        var (store, module, state) = Make(new Vector3(0f, 0f, 0f));
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Patrol;
        state.PostEvadeGraceUntilTick = 30;

        module.Tick(0);
        Assert.Equal(MonsterMode.Patrol, state.Mode);

        module.OnDamageTaken(state.NpcId, 1, 20f, MonsterDamageKind.Direct);
        Assert.Equal(MonsterMode.Combat, state.Mode);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BrainTests"`

预期：编译失败，`MonsterMode` / `MonsterBrain` / `OnDamageTaken` 不存在。

- [ ] **步骤 3：迁移 `MonsterState`**

`Server/Monster/MonsterState.cs` 改为：

```csharp
using KcpServer.Config;

namespace KcpServer.Monster
{
    public enum MonsterMode
    {
        Patrol = 0,
        Combat = 1,
        Evade = 2,
        Dead = 3
    }

    public enum MonsterDamageKind
    {
        Direct = 0,
        Dot = 1
    }

    public sealed class MonsterState
    {
        public long NpcId { get; }
        public int TemplateId { get; }
        public MonsterData Config { get; }

        public MonsterMode Mode { get; set; } = MonsterMode.Patrol;
        public ThreatTable Threats { get; } = new ThreatTable();

        public long? TargetId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public Vector3 PatrolCenter { get; set; }
        public Vector3[] PatrolPoints { get; set; } = System.Array.Empty<Vector3>();
        public int CurrentPatrolIndex { get; set; }
        public Vector3? LastKnownTargetPosition { get; set; }

        public uint? NoValidTargetSinceTick { get; set; }
        public uint? EvadeStartTick { get; set; }
        public uint PostEvadeGraceUntilTick { get; set; }
        public long? TauntTargetId { get; set; }
        public uint TauntUntilTick { get; set; }
        public bool NeedsReposition { get; set; }

        public bool IsTauntActive(uint tick)
            => TauntTargetId.HasValue && tick < TauntUntilTick;

        /// <summary>配置里第一条技能（测试/兼容用；生产选择走 SkillSelector）。</summary>
        public int PrimarySkillId
            => Config.skills.Count > 0 ? Config.skills[0].skillId : 0;

        public PathFollower? Follower { get; set; }

        public Vector3 Position;
        public Quaternion Rotation;
        public bool Moving;
        public Vector3 CurrentVelocity;

        public uint? AttackResolveTick { get; set; }
        public uint? HitStartTick;
        public uint? DeathTick;
        public float LastHp;

        public BtNode? BehaviorTree { get; set; }

        public MonsterState(long npcId, int templateId, MonsterData config)
        {
            NpcId = npcId;
            TemplateId = templateId;
            Config = config;
        }
    }
}
```

> 删除 `AlertLevel` 枚举与 `AttackSkillId`。

`PathFollower` 增加：

```csharp
/// <summary>清空跟随状态（进入 Evade / 到家重置时用，强制下一 tick 重寻）。</summary>
public void Reset()
{
    _path = null;
    _index = 0;
    _goal = default;
    _nextRepathTick = 0;
    _nextGoalRepathTick = 0;
    _nextAttemptTick = 0;
    _stuckCheckPosition = default;
    _stuckCheckTick = 0;
    _stuckWindows = 0;
}
```

- [ ] **步骤 4：实现 MonsterBrain**

创建 `Server/Monster/Brain/MonsterBrain.cs`：

```csharp
using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer.Monster
{
    /// <summary>
    /// 顶层模式机（P2）。每 tick 在行为树之前运行：有效性清理 → 目标选择 → 模式转移。
    /// 不移动、不施法；移动/施法仍由 BT 动作负责。
    /// </summary>
    public sealed class MonsterBrain
    {
        public const uint TargetLostGraceTicks = 45;   // 3s
        public const uint PostEvadeGraceTicks = 30;    // 2s
        public const uint ReturnTimeoutTicks = 300;    // 20s

        private readonly EntityStore _store;

        public MonsterBrain(EntityStore store) => _store = store;

        public void Update(MonsterState m, PlayerState entity, uint tick, bool allowEvadeTransition)
        {
            if (m.Mode == MonsterMode.Dead || m.DeathTick.HasValue) return;

            bool currentOutOfRange = CurrentTargetOutOfRange(m);
            PruneInvalidThreat(m);

            if (m.Mode == MonsterMode.Patrol)
            {
                if (tick < m.PostEvadeGraceUntilTick) return;
                if (TryAcquireBySight(m, entity)) m.Mode = MonsterMode.Combat;
                return;
            }

            if (m.Mode == MonsterMode.Evade)
            {
                if (DistanceXZ(m.Position, m.SpawnPosition) <= 0.5f)
                    ArriveHome(m, entity, tick);
                return;
            }

            // Combat
            if (allowEvadeTransition && DistanceXZ(m.Position, m.SpawnPosition) > m.Config.LeashRadiusOf)
            {
                EnterEvade(m, tick);
                return;
            }

            // 前摇锁目标（spec §5.1）：只锁 TargetId，不换目标；LastKnownTargetPosition 仍按
            // spec §5.2 每 tick 从目标实体更新（怪物在前摇期会以 90°/s 朝目标转向）。
            long? selected = m.AttackResolveTick.HasValue
                ? m.TargetId
                : TargetSelector.Select(m.Threats, m.TargetId,
                    id => IsValidTarget(m, id),
                    m.IsTauntActive(tick) ? m.TauntTargetId : null);

            if (selected.HasValue)
            {
                var target = _store.GetPlayer(selected.Value);
                if (target == null) { selected = null; }
                else
                {
                    m.TargetId = selected;
                    m.LastKnownTargetPosition = target.Position;
                    m.NoValidTargetSinceTick = null;
                    m.NeedsReposition = SkillSelector.NeedsReposition(
                        m, entity, DistanceXZ(m.Position, target.Position));
                    return;
                }
            }

            m.TargetId = null;
            m.LastKnownTargetPosition = null;
            m.NeedsReposition = false;
            if (!allowEvadeTransition) return;

            if (currentOutOfRange) { EnterEvade(m, tick); return; }
            m.NoValidTargetSinceTick ??= tick;
            if (tick - m.NoValidTargetSinceTick.Value >= TargetLostGraceTicks)
                EnterEvade(m, tick);
        }

        public void OnDamageTaken(MonsterState m, long attackerId, float damage, MonsterDamageKind kind)
        {
            if (m.Mode == MonsterMode.Dead || m.Mode == MonsterMode.Evade || m.DeathTick.HasValue) return;
            var attacker = _store.GetPlayer(attackerId);
            if (attacker == null || attacker.Kind != (byte)EntityKind.Player
                || !attacker.IsAlive || !attacker.IsLoggedIn) return;

            m.Threats.AddDamage(attackerId, damage);
            if (m.Mode == MonsterMode.Patrol) m.Mode = MonsterMode.Combat;
            m.NoValidTargetSinceTick = null;

            if (!m.TargetId.HasValue)
            {
                m.TargetId = attackerId;
                m.LastKnownTargetPosition = attacker.Position;
            }
        }

        public void Taunt(MonsterState m, long playerId, uint durationTicks, uint tick)
        {
            if (m.Mode == MonsterMode.Dead || m.Mode == MonsterMode.Evade || m.DeathTick.HasValue) return;
            var player = _store.GetPlayer(playerId);
            if (player == null || player.Kind != (byte)EntityKind.Player
                || !player.IsAlive || !player.IsLoggedIn) return;

            m.Threats.Ensure(playerId);
            m.TauntTargetId = playerId;
            m.TauntUntilTick = tick + durationTicks;
            if (m.Mode == MonsterMode.Patrol) m.Mode = MonsterMode.Combat;
            m.NoValidTargetSinceTick = null;
            if (!m.AttackResolveTick.HasValue)
            {
                m.TargetId = playerId;
                m.LastKnownTargetPosition = player.Position;
            }
        }

        public void EnterEvade(MonsterState m, uint tick)
        {
            m.Mode = MonsterMode.Evade;
            m.EvadeStartTick = tick;
            m.Threats.Clear();
            m.TargetId = null;
            m.LastKnownTargetPosition = null;
            m.NoValidTargetSinceTick = null;
            m.NeedsReposition = false;
            m.TauntTargetId = null;
            m.TauntUntilTick = 0;
            m.Follower?.Reset();
        }

        public void ArriveHome(MonsterState m, PlayerState entity, uint tick)
        {
            entity.Hp = entity.MaxHp;
            entity.ActionState = (byte)NpcAnimationState.Idle;
            m.LastHp = entity.Hp;
            m.Mode = MonsterMode.Patrol;
            m.EvadeStartTick = null;
            m.Threats.Clear();
            m.TargetId = null;
            m.LastKnownTargetPosition = null;
            m.CurrentPatrolIndex = 0;
            m.PostEvadeGraceUntilTick = tick + PostEvadeGraceTicks;
            m.NoValidTargetSinceTick = null;
            m.NeedsReposition = false;
            m.TauntTargetId = null;
            m.TauntUntilTick = 0;
            m.Follower?.Reset();
        }

        private void PruneInvalidThreat(MonsterState m)
        {
            List<long>? remove = null;
            foreach (long playerId in m.Threats.PlayerIds)
                if (!IsValidTarget(m, playerId)) (remove ??= new List<long>()).Add(playerId);
            if (remove == null) return;
            foreach (long playerId in remove) m.Threats.Remove(playerId);
        }

        private bool IsValidTarget(MonsterState m, long playerId)
        {
            var player = _store.GetPlayer(playerId);
            if (player == null || player.Kind != (byte)EntityKind.Player
                || !player.IsAlive || !player.IsLoggedIn) return false;
            return DistanceXZ(player.Position, m.Position) <= m.Config.DropAggroRadiusOf;
        }

        private bool CurrentTargetOutOfRange(MonsterState m)
        {
            if (!m.TargetId.HasValue) return false;
            var player = _store.GetPlayer(m.TargetId.Value);
            if (player == null || player.Kind != (byte)EntityKind.Player
                || !player.IsAlive || !player.IsLoggedIn) return false;
            return DistanceXZ(player.Position, m.Position) > m.Config.DropAggroRadiusOf;
        }

        private bool TryAcquireBySight(MonsterState m, PlayerState entity)
        {
            long? closestId = null;
            Vector3 closestPosition = default;
            float closestSqr = float.MaxValue;

            foreach (var player in _store.All)
            {
                if (player.Kind != (byte)EntityKind.Player || !player.IsAlive || !player.IsLoggedIn)
                    continue;
                if (!TargetDetector.CanDetect(entity.Rotation, m.Position, player.Position,
                        m.Config.detectionRadius, m.Config.visionAngle)) continue;

                float dx = player.Position.X - m.Position.X;
                float dz = player.Position.Z - m.Position.Z;
                float sqr = dx * dx + dz * dz;
                if (sqr < closestSqr
                    || (sqr == closestSqr && closestId.HasValue && player.PlayerId < closestId.Value))
                {
                    closestSqr = sqr;
                    closestId = player.PlayerId;
                    closestPosition = player.Position;
                }
            }

            if (!closestId.HasValue) return false;
            m.Threats.Clear();
            m.Threats.AddInitial(closestId.Value);
            m.TargetId = closestId;
            m.LastKnownTargetPosition = closestPosition;
            m.NoValidTargetSinceTick = null;
            return true;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
```

- [ ] **步骤 5：接 `MonsterModule`**

`MonsterModule.cs` 改动：

1. 字段：`private readonly MonsterBrain _brain;`，构造函数末尾 `_brain = new MonsterBrain(store);`。
2. 删除 `SkillIdOf` 静态方法。
3. `SpawnMonster`：
   - 删除 `int attackSkillId = SkillIdOf(templateId);` 与对应 `CombatRules.TryGet` 校验，改为 `ValidateSkills(config);`
   - `new MonsterState(npcId, templateId, config)`。
4. 新增：

```csharp
private static void ValidateSkills(MonsterData config)
{
    if (config.skills == null || config.skills.Count == 0)
        throw new ArgumentException($"Monster template {config.templateId} has no skills");

    foreach (var skill in config.skills)
    {
        if (!CombatRules.TryGet(skill.skillId, out var entry))
            throw new ArgumentException(
                $"Monster skill {skill.skillId} (template {config.templateId}) missing from CombatRules table");

        float maxRange = skill.MaxRangeOf(entry);
        if (skill.minRange > maxRange + 0.01f)
            throw new ArgumentException(
                $"Monster skill {skill.skillId} minRange={skill.minRange} > maxRange={maxRange}");
        if (entry.ShapeType == 0 && maxRange > entry.MaxRange + 0.01f)
            throw new ArgumentException(
                $"Monster skill {skill.skillId} maxRange={maxRange} exceeds table {entry.MaxRange}");
    }
}
```

5. 删除 `DetectTarget` 与 `DropAggroRadius` 方法。
6. `DetectHitOrDeath` 死亡分支追加：

```csharp
m.Mode = MonsterMode.Dead;
m.Threats.Clear();
m.TargetId = null;
m.LastKnownTargetPosition = null;
```

7. `Tick` 中替换感知/BT 段：

```csharp
DetectHitOrDeath(m, entity, tick);

if (!m.DeathTick.HasValue)
{
    bool inHitWindow = m.HitStartTick.HasValue
        && tick - m.HitStartTick.Value < HitDisplayTicks;

    _brain.Update(m, entity, tick, allowEvadeTransition: !inHitWindow);

    if (m.Mode == MonsterMode.Evade && m.EvadeStartTick.HasValue
        && tick - m.EvadeStartTick.Value >= MonsterBrain.ReturnTimeoutTicks)
    {
        _store.RemovePlayer(m.NpcId);
        _removedNpcs.Add(m.NpcId);
        _respawns.Add((m.TemplateId, m.PatrolCenter, tick + EvadeRespawnDelayTicks));
        done.Add(m.NpcId);
        continue;
    }

    if (!inHitWindow)
    {
        var ctx = new MonsterTickContext(m, entity, this, _mover, TickDeltaSeconds, tick);
        m.BehaviorTree?.Tick(ctx);
    }
    else
    {
        m.Moving = false;
    }
}
else
{
    m.Moving = false;
}
```

> 注意保留既有「前摇到期清理」与「硬直窗口刚过期补状态」两段逻辑（在 `if (!m.DeathTick.HasValue)` 之外照旧执行）。死亡分支原本会 `continue` 进入下一只怪，改造后由外层 `if` 控制，确保死亡展示窗口内的 `continue` 仍生效。

8. 新增常量：`public const uint EvadeRespawnDelayTicks = 30;`
9. 新增公开入口：

```csharp
/// <summary>伤害归属（Server3C 经 CombatResolver.NpcDamaged 接线；测试可直接调）。</summary>
public void OnDamageTaken(long npcId, long attackerId, float damage, MonsterDamageKind kind)
{
    if (_monsters.TryGetValue(npcId, out var state))
        _brain.OnDamageTaken(state, attackerId, damage, kind);
}

/// <summary>嘲讽 API（v1 无玩家技能，仅单测）。</summary>
public void Taunt(long npcId, long playerId, uint durationTicks, uint tick)
{
    if (_monsters.TryGetValue(npcId, out var state))
        _brain.Taunt(state, playerId, durationTicks, tick);
}
```

10. `Server3C.EnableMonsters` 接线（放在现有 LOS 接线之外，无条件执行）：

```csharp
MonsterModule damageModule = _monsterModule;
_castHandler.SetNpcDamageObserver((npcId, attackerId, damage) =>
    damageModule.OnDamageTaken(npcId, attackerId, damage, MonsterDamageKind.Direct));
```

11. `BuildBehaviorTree` 改为：

```csharp
private BtNode BuildBehaviorTree()
{
    return new BtSelector(
        new BtSequence(
            new BtCondition(m => m.Mode == MonsterMode.Patrol),
            new BtAction(MonsterBtActions.Patrol)
        ),
        new BtSequence(
            new BtCondition(m => m.Mode == MonsterMode.Combat),
            new BtSelector(
                new BtSequence(new BtAction(MonsterBtActions.Attack)),
                new BtSequence(
                    new BtCondition(m => m.NeedsReposition),
                    new BtAction(MonsterBtActions.Reposition)
                ),
                new BtAction(MonsterBtActions.Chase)
            )
        ),
        new BtSequence(
            new BtCondition(m => m.Mode == MonsterMode.Evade),
            new BtAction(MonsterBtActions.Evade)
        )
    );
}
```

> `Reposition` / `Evade` 与改造后的 `Attack` / `Chase` 在本任务第 12 项落地，因此任务 5 结束即可 `dotnet build Server/Server3C.csproj` 并通过 `BrainTests`。任务 6 只负责多技能战斗场景的验证与边界修复，不再重复实现动作代码。

12. **实现 `MonsterBtActions` 的 `Chase` / `Attack` / `Reposition` / `Evade`**（任务 6 的最终形态，本任务先落下以保证任务 5 可独立编译与测试）：

```csharp
public static BtStatus Chase(MonsterTickContext ctx)
{
    var m = ctx.State;

    if (m.AttackResolveTick.HasValue) return BtStatus.Success;

    // 目标丢失（死亡/离线/出 drop 半径）后 brain 会清 LastKnown；此时 Chase 必须
    // 显式写 Idle/0 速再返回，否则会保留上一 tick 的 Run/Attack 与 moveSpeed 广播，
    // 一路假跑到进入 Evade（最长 45 tick）。
    if (!m.LastKnownTargetPosition.HasValue)
    {
        SetMovementState(ctx, moved: false);
        return BtStatus.Success;
    }

    Vector3 target = m.LastKnownTargetPosition.Value;
    float targetDistance = DistanceXZ(m.Position, target);
    bool inRange = SkillSelector.IsWithinAnySkillRange(m, ctx.Entity, targetDistance);
    if (inRange && (m.Follower == null
        || m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius)))
        return BtStatus.Success;

    float moved = m.Follower != null
        ? (m.Follower.Tick(m, target, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
        : ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
    SetMovementState(ctx, moved > MovementEpsilon);
    return BtStatus.Running;
}

public static BtStatus Attack(MonsterTickContext ctx)
{
    var m = ctx.State;
    var entity = ctx.Entity;

    if (m.AttackResolveTick.HasValue)
    {
        m.Moving = false;
        if (m.LastKnownTargetPosition.HasValue)
            ctx.Mover.TurnTowards(m, m.LastKnownTargetPosition.Value, ctx.DeltaTime,
                WindupTurnSpeedDegPerSecond);
        return BtStatus.Running;
    }

    if (!m.TargetId.HasValue) return BtStatus.Failure;

    Vector3 target = m.LastKnownTargetPosition ?? m.Position;
    float distance = DistanceXZ(m.Position, target);
    if (!SkillSelector.IsWithinAnySkillRange(m, entity, distance)) return BtStatus.Failure;

    if (m.Follower != null && !m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius))
    {
        m.Moving = false;
        entity.ActionState = (byte)NpcAnimationState.Idle;
        return BtStatus.Failure;
    }

    m.Moving = false;

    var selected = SkillSelector.Select(m, entity, distance, ctx.Tick);
    if (selected == null) return BtStatus.Running; // 射程内但冷却：站定等待

    if (!MonsterCastBuilder.TryBuild(m, selected.Value, target, out var request))
        return BtStatus.Failure;

    ctx.Module.QueueCast(m.NpcId, request);
    m.AttackResolveTick = ctx.Tick + CombatRules.HitDelayTicks(selected.Value.Entry);
    entity.ActionState = (byte)NpcAnimationState.Attack;
    return BtStatus.Running;
}

public static BtStatus Reposition(MonsterTickContext ctx)
{
    var m = ctx.State;
    if (!m.LastKnownTargetPosition.HasValue) return BtStatus.Failure;

    Vector3 target = m.LastKnownTargetPosition.Value;
    float distance = DistanceXZ(m.Position, target);
    float desired = SkillSelector.RepositionDistance(m, ctx.Entity, distance);
    if (desired <= 0f || distance >= desired)
    {
        SetMovementState(ctx, moved: false);
        return BtStatus.Success;
    }

    Vector3 away = m.Position - target;
    away.Y = 0f;
    if (away.X * away.X + away.Z * away.Z < 0.0001f) away = new Vector3(0f, 0f, -1f);
    away = away.Normalize();
    Vector3 goal = target + away * desired;

    float moved = m.Follower != null
        ? (m.Follower.Tick(m, goal, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
        : ctx.Mover.MoveTo(m, goal, ctx.DeltaTime);
    SetMovementState(ctx, moved > MovementEpsilon);
    return BtStatus.Running;
}

public static BtStatus Evade(MonsterTickContext ctx)
{
    var m = ctx.State;
    float moved = m.Follower != null
        ? (m.Follower.Tick(m, m.SpawnPosition, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
        : ctx.Mover.MoveTo(m, m.SpawnPosition, ctx.DeltaTime);

    if (ctx.Mover.HasReached(m, m.SpawnPosition, 0.5f))
    {
        SetMovementState(ctx, moved: false);
        return BtStatus.Success;
    }
    SetMovementState(ctx, moved: moved > MovementEpsilon);
    return BtStatus.Running;
}

private static float DistanceXZ(Vector3 a, Vector3 b)
{
    float dx = a.X - b.X, dz = a.Z - b.Z;
    return MathF.Sqrt(dx * dx + dz * dz);
}
```

13. **删除 `MonsterBtActions.Return`**：它引用已删除的 `AlertLevel`，且旧 `Return` 已被 Evade 取代。`grep -rn "MonsterBtActions.Return" Server` 应为空。

14. 死亡移除分支改用统一调度（可选重构）：

```csharp
_store.RemovePlayer(m.NpcId);
_removedNpcs.Add(m.NpcId);
_respawns.Add((m.TemplateId, m.PatrolCenter, tick + RespawnTicks));
done.Add(m.NpcId);
```

- [ ] **步骤 6：迁移既有测试**

- `MonsterModuleTests`：
  - `Target_Lost_Triggers_Return_To_Spawn_And_Peace_Reset` 改名 `Target_Lost_After_Grace_Returns_To_Spawn_And_Resets_To_Patrol`；断言从 `AlertLevel` 改为 `Mode == MonsterMode.Patrol`；循环上限从 400 提到 600（45 tick grace + 回程）。
  - `Template_AttackRange_Fits_Its_Combat_Rule` 已在任务 3 替换。
- `MonsterAcceptanceTests.A2`：
  - 改名为 `OffAxis_Target_Is_Not_Lost_But_Attack_Still_Uses_Judgment_Cone`。
  - 删除 `Assert.Null(state.TargetId);`，改为 `Assert.Equal(1L, state.TargetId);`（P2 ThreatTable 不再因离开视野锥丢目标）。
  - **角度数据改为 `[InlineData(55f, true)]` / `[InlineData(110f, false)]`**：P2 前摇期 LastKnownTargetPosition 每 tick 更新，Wolf 以 90°/s 转向 6 tick ≈ 35.6°。原 65° 在转向后只剩 ~29°，会被判定锥（半角 60°）命中；110° 转向后 ~74° 仍在外 ⇒ 空挥。注释写清这条几何换算，避免后人把 65° 改回来。
  - 注释更新为「目标仍在威胁表内并持续跟踪；命中与否只由判定帧夹角与判定锥决定」。
- `MonsterMovementStateTests`：
  - `MakeMonster` 的 `MonsterData` 补集合初始化 `skills = { new MonsterSkillConfig { skillId = 50001, maxRange = attackRange } }`，再改为 `new MonsterState(1, 1, config)`；否则 `PrimarySkillId=0`，冷却用例会落到 `Attack` 的「无技能」分支。
  - `state.AttackSkillId` 改为 `state.PrimarySkillId`（两处：`Attack_Cooldown_Sets_Moving_False` 与 `Attack_Cooldown_Flushes_Zero_Speed`）。
- 所有 `AlertLevel` 引用清零（`grep -rn "AlertLevel" Server` 应为空）。

- [ ] **步骤 7：运行测试验证通过**

运行：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BrainTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~NpcDamageAttribution"
```

预期：通过。若 `Monster` 过滤下仍有旧 `AlertLevel`/`SkillIdOf` 编译错误，按步骤 6 清零后重跑。

- [ ] **步骤 8：Commit**

```bash
git add Server/Monster/Brain/MonsterBrain.cs Server/Monster/MonsterState.cs Server/Monster/MonsterModule.cs \
  Server/Monster/Movement/PathFollower.cs Server/Server3C.cs Server/Server.Tests/Monster/BrainTests.cs \
  Server/Server.Tests/Monster/MonsterModuleTests.cs Server/Server.Tests/Monster/MonsterAcceptanceTests.cs \
  Server/Server.Tests/Monster/MonsterMovementStateTests.cs
git commit -m "feat(monster): replace alert level with patrol/combat/evade mode machine"
```

---

## 任务 6：战斗行为（Attack / Chase / Reposition）

**文件：**
- 修改：`Server/Monster/MonsterBtActions.cs`
- 测试：`Server/Server.Tests/Monster/MonsterSkillCombatTests.cs`
- 修改：`Server/Server.Tests/Monster/MonsterMovementStateTests.cs`（冷却用例）

- [ ] **步骤 1：编写失败的测试**

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterSkillCombatTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State) Make(int templateId)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        // 出生点不要用原点：YawTowardOrigin(0,0,0) 是 atan2(-0,-0) 退化角，
        // 怪会面向 -Z；放在 (0,0,5) 让朝向稳定为 -Z，玩家放 z<5 即在视野锥内。
        long id = module.SpawnMonster(templateId, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 5f));
        return (store, module, module.GetState(id)!);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    [Fact]
    public void DogKnight_Below_Half_Hp_Uses_MidRange_Skill_At_4m()
    {
        var (store, module, state) = Make(3);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(200f); // 300 -> 100（< 50%）
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m，在 -Z 视野锥内

        var casts = new List<CastRequest>();
        float firstMidRangeDistance = -1f;
        for (uint t = 0; t < 30; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts())
            {
                if (request.SkillId == 50005 && firstMidRangeDistance < 0f)
                    firstMidRangeDistance = Vector3.Distance(state.Position, store.GetPlayer(1)!.Position);
                casts.Add(request);
            }
        }

        Assert.Contains(casts, r => r.SkillId == 50005);
        Assert.DoesNotContain(casts, r => r.SkillId == 50003);
        // 距离主题必须真的钉住：50005 必须在 50003 的射程（2.2m）外决策。
        Assert.True(firstMidRangeDistance > 2.2f + 0.01f,
            $"50005 应在 50003 射程外决策，实测首次距离 {firstMidRangeDistance:F2}m");
    }

    [Fact]
    public void DogKnight_Above_Half_Hp_Chases_Into_Melee_Range()
    {
        var (store, module, state) = Make(3);
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m

        var casts = new List<CastRequest>();
        float firstMeleeDistance = -1f;
        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts())
            {
                if (request.SkillId == 50003 && firstMeleeDistance < 0f)
                    firstMeleeDistance = Vector3.Distance(state.Position, store.GetPlayer(1)!.Position);
                casts.Add(request);
            }
        }

        Assert.Contains(casts, c => c.SkillId == 50003);
        Assert.DoesNotContain(casts, c => c.SkillId == 50005);
        Assert.True(firstMeleeDistance <= state.Config.attackRange + 0.05f,
            $"50003 应在近战距离决策，实测首次距离 {firstMeleeDistance:F2}m"
            + $"（attackRange {state.Config.attackRange}）");
    }

    [Fact]
    public void SpiderBoss_Below_Half_Hp_Uses_Aoe()
    {
        var (store, module, state) = Make(4);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(1000f); // 1500 -> 500（< 50%）
        Login(store, 1, new Vector3(0f, 0f, 2f)); // 距出生点 3m

        var casts = new List<CastRequest>();
        for (uint t = 0; t < 30; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts()) casts.Add(request);
        }

        Assert.Contains(casts, r => r.SkillId == 50006);
        Assert.Equal(1, casts.Find(r => r.SkillId == 50006).Shape.ShapeType);
    }

    [Fact]
    public void Attack_Does_Not_Switch_Skill_During_Windup()
    {
        var (store, module, state) = Make(3);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(200f);
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m

        uint decideTick = 0;
        CastRequest? first = null;
        for (uint t = 0; t < 30 && first == null; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts())
            {
                decideTick = t;
                first = request;
            }
        }
        Assert.NotNull(first);
        Assert.True(state.AttackResolveTick.HasValue);

        // 前摇期间给另一目标 1000 威胁：TargetId 不变
        Login(store, 2, new Vector3(0f, 0f, 0.5f)); // 距出生点 4.5m
        state.Threats.Add(2, 1000f);
        module.Tick(decideTick + 1);
        Assert.Equal(1L, state.TargetId);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterSkillCombat"`

预期：任务 5 已完成动作实现，本任务测试应大部分通过；若失败，按失败断言定位边界问题（实现见任务 5 第 12 项）。任务 6 是验证/硬化任务，不再要求先红后绿。

- [ ] **步骤 3：核对实现（任务 5 第 12 项已落地）**

以下代码是任务 5 第 12 项的同一份实现，保留在本任务作为审查对照；不要重复写第二份。若测试暴露边界问题，只改这一份并同步任务 5 的代码。

- `Chase` 的 `inRange` 改为：

```csharp
float targetDistance = DistanceXZ(m.Position, target);
bool inRange = SkillSelector.IsWithinAnySkillRange(m, ctx.Entity, targetDistance);
```

其中新增私有助手：

```csharp
private static float DistanceXZ(Vector3 a, Vector3 b)
{
    float dx = a.X - b.X, dz = a.Z - b.Z;
    return MathF.Sqrt(dx * dx + dz * dz);
}
```

- `Attack` 改为：

```csharp
public static BtStatus Attack(MonsterTickContext ctx)
{
    var m = ctx.State;
    var entity = ctx.Entity;

    if (m.AttackResolveTick.HasValue)
    {
        m.Moving = false;
        if (m.LastKnownTargetPosition.HasValue)
            ctx.Mover.TurnTowards(m, m.LastKnownTargetPosition.Value, ctx.DeltaTime,
                WindupTurnSpeedDegPerSecond);
        return BtStatus.Running;
    }

    if (!m.TargetId.HasValue) return BtStatus.Failure;

    Vector3 target = m.LastKnownTargetPosition ?? m.Position;
    float distance = DistanceXZ(m.Position, target);
    if (!SkillSelector.IsWithinAnySkillRange(m, entity, distance)) return BtStatus.Failure;

    if (m.Follower != null && !m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius))
    {
        m.Moving = false;
        entity.ActionState = (byte)NpcAnimationState.Idle;
        return BtStatus.Failure;
    }

    m.Moving = false;

    var selected = SkillSelector.Select(m, entity, distance, ctx.Tick);
    if (selected == null) return BtStatus.Running; // 射程内但冷却：站定等待

    if (!MonsterCastBuilder.TryBuild(m, selected.Value, target, out var request))
        return BtStatus.Failure;

    ctx.Module.QueueCast(m.NpcId, request);
    m.AttackResolveTick = ctx.Tick + CombatRules.HitDelayTicks(selected.Value.Entry);
    entity.ActionState = (byte)NpcAnimationState.Attack;
    return BtStatus.Running;
}
```

- 新增 `Reposition`：

```csharp
public static BtStatus Reposition(MonsterTickContext ctx)
{
    var m = ctx.State;
    if (!m.LastKnownTargetPosition.HasValue) return BtStatus.Failure;

    Vector3 target = m.LastKnownTargetPosition.Value;
    float distance = DistanceXZ(m.Position, target);
    float desired = SkillSelector.RepositionDistance(m, ctx.Entity, distance);
    if (desired <= 0f || distance >= desired)
    {
        SetMovementState(ctx, moved: false);
        return BtStatus.Success;
    }

    Vector3 away = m.Position - target;
    away.Y = 0f;
    if (away.X * away.X + away.Z * away.Z < 0.0001f) away = new Vector3(0f, 0f, -1f);
    away = away.Normalize();
    Vector3 goal = target + away * desired;

    float moved = m.Follower != null
        ? (m.Follower.Tick(m, goal, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
        : ctx.Mover.MoveTo(m, goal, ctx.DeltaTime);
    SetMovementState(ctx, moved > MovementEpsilon);
    return BtStatus.Running;
}
```

- 新增 `Evade`：

```csharp
public static BtStatus Evade(MonsterTickContext ctx)
{
    var m = ctx.State;
    float moved = m.Follower != null
        ? (m.Follower.Tick(m, m.SpawnPosition, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
        : ctx.Mover.MoveTo(m, m.SpawnPosition, ctx.DeltaTime);

    if (ctx.Mover.HasReached(m, m.SpawnPosition, 0.5f))
    {
        SetMovementState(ctx, moved: false);
        return BtStatus.Success;
    }
    SetMovementState(ctx, moved: moved > MovementEpsilon);
    return BtStatus.Running;
}
```

- 确认 `Return` 已在任务 5 删除（不再被树引用；`grep -rn "MonsterBtActions.Return" Server` 应为空）。

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterSkillCombat"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterMovementState"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterAcceptance"
```

预期：通过。

- [ ] **步骤 5：Commit**

```bash
git add Server/Monster/MonsterBtActions.cs Server/Server.Tests/Monster/MonsterSkillCombatTests.cs \
  Server/Server.Tests/Monster/MonsterMovementStateTests.cs
git commit -m "feat(monster): select multi-skills in combat and reposition inside min range"
```

---

## 任务 7：脱战回巢、重置与超时

**文件：**
- 修改：`Server/Monster/MonsterModule.cs`（超时调度已在任务 5 落下；本任务补测试与细节）
- 测试：`Server/Server.Tests/Monster/MonsterEvadeTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterEvadeTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State, long Id) Make()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        return (store, module, module.GetState(id)!, id);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    [Fact]
    public void Evade_Returns_Home_Heals_Clears_Threat_And_Enters_Grace()
    {
        var (store, module, state, id) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);

        var entity = store.GetPlayer(id)!;
        entity.ApplyDamage(50f); // 100 -> 50
        state.Position = new Vector3(0f, 0f, 25f); // > Slime leash 23
        state.CurrentPatrolIndex = 2; // 非零：到家必须归零，否则断言是空钉子

        uint t = 0;
        while (t < 600 && state.Mode != MonsterMode.Patrol) { module.Tick(t); t++; }
        uint arrivalTick = t - 1;

        Assert.Equal(MonsterMode.Patrol, state.Mode);
        Assert.Equal(entity.MaxHp, entity.Hp, 3);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
        Assert.Equal(0, state.CurrentPatrolIndex);
        Assert.Equal(30u, MonsterBrain.PostEvadeGraceTicks); // 规格常量
        Assert.Equal(arrivalTick + 30u, state.PostEvadeGraceUntilTick); // 精确 +30，不是「大于」
    }

    [Fact]
    public void Target_Out_Of_Drop_Radius_Enters_Evade_Immediately()
    {
        var (store, module, state, _) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);

        // Slime drop radius = leash = 23；把玩家移到 24m 外
        store.GetPlayer(1)!.UpdatePosition(new Vector3(0f, 0f, 24f), 0f, 0f, 2L, 0U);
        module.Tick(0);

        Assert.Equal(MonsterMode.Evade, state.Mode); // 出 drop 半径是立即脱战，不等 45 tick grace
    }

    [Fact]
    public void Evade_And_Dead_Ignore_Taunt()
    {
        var (store, module, state, id) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));

        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;
        module.Taunt(id, 1, durationTicks: 30, tick: 0);
        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
        Assert.Equal(0u, state.TauntUntilTick);

        state.Mode = MonsterMode.Dead;
        module.Taunt(id, 1, durationTicks: 30, tick: 0);
        Assert.Equal(MonsterMode.Dead, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
        Assert.Equal(0u, state.TauntUntilTick);

        // Dead 也必须忽略直接伤害归属（任务 5 审查补的守卫钉子）
        module.OnDamageTaken(id, 1, 25f, MonsterDamageKind.Direct);
        Assert.Equal(MonsterMode.Dead, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
        Assert.Null(state.TargetId);
    }

    [Fact]
    public void Evade_Ignores_Incoming_Damage_Threat()
    {
        var (store, module, state, id) = Make();
        Login(store, 1, new Vector3(0f, 0f, 3f));
        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;

        module.OnDamageTaken(id, 1, 25f, MonsterDamageKind.Direct);

        Assert.Equal(MonsterMode.Evade, state.Mode);
        Assert.Empty(state.Threats.PlayerIds);
    }

    [Fact]
    public void Evade_Timeout_Despawns_Then_Respawns_After_Short_Delay()
    {
        var (store, module, state, id) = Make();
        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;
        state.SpawnPosition = new Vector3(0f, 0f, 1000f); // 回不到家

        Assert.Equal(300u, MonsterBrain.ReturnTimeoutTicks);       // 规格常量
        Assert.Equal(30u, MonsterModule.EvadeRespawnDelayTicks);   // 规格常量

        for (uint t = 0; t < 300; t++) module.Tick(t); // t=0..299
        Assert.NotNull(store.GetPlayer(id)); // t=299 尚未超时

        module.Tick(300);
        Assert.Null(store.GetPlayer(id)); // t=300 超时移除
        Assert.Contains(id, module.DrainRemovedNpcs());

        for (uint t = 301; t < 330; t++)
        {
            module.Tick(t);
            Assert.Equal(0, module.Count); // 30 tick 延迟内不得提前重生
        }

        module.Tick(330);
        Assert.Equal(1, module.Count); // 新 id 重生
        Assert.DoesNotContain(store.All, p => p.PlayerId == id);

        // 重生位置必须是 PatrolCenter（0,0,0），不是原地/超时位置
        foreach (var npc in store.All)
        {
            Assert.Equal(0f, npc.Position.X, 1);
            Assert.Equal(0f, npc.Position.Z, 1);
        }
    }

    [Fact]
    public void Post_Evade_Grace_Blocks_Vision_Acquisition()
    {
        var (store, module, state, _) = Make();
        // 原点出生时 YawTowardOrigin(0,0,0) 面向 -Z；玩家放 -Z 侧才在视野锥内。
        Login(store, 1, new Vector3(0f, 0f, -3f));
        state.Mode = MonsterMode.Patrol;
        state.PostEvadeGraceUntilTick = 30;

        module.Tick(0);
        Assert.Equal(MonsterMode.Patrol, state.Mode);

        module.Tick(30);
        Assert.Equal(MonsterMode.Combat, state.Mode);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterEvade"`

预期：失败（Evade 行为/超时/grace 未完全落地）。

- [ ] **步骤 3：补齐实现细节**

- `MonsterBrain.ArriveHome` 已在任务 5 实现，确认：
  - `m.LastHp = entity.Hp`（防治疗后误判受击）；
  - `PostEvadeGraceUntilTick = tick + PostEvadeGraceTicks`；
  - `Follower.Reset()`。
- `MonsterModule.Tick` 的超时分支已在任务 5 实现，确认 `EvadeRespawnDelayTicks = 30` 且 `_respawns` 用 `m.PatrolCenter`。
- `MonsterBrain.OnDamageTaken` 在 `Mode == Evade` 直接 return（测试 `Evade_Ignores_Incoming_Damage_Threat` 钉住）。
- 受击窗口内不触发 `Combat → Evade`：`MonsterModule` 传 `allowEvadeTransition: !inHitWindow`，已实现。

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterEvade"`

预期：4 个测试通过。

- [ ] **步骤 5：Commit**

```bash
git add Server/Monster/MonsterModule.cs Server/Monster/Brain/MonsterBrain.cs \
  Server/Server.Tests/Monster/MonsterEvadeTests.cs
git commit -m "feat(monster): evade home, heal, reset threat, and timeout respawn"
```

---

## 任务 8：客户端 skillId → 攻击动画变体映射

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs:402`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:679`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs`
- 修改：`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs:682-697`

- [ ] **步骤 1：编写失败的测试**

在 `NpcAttackScheduleTests.cs` 追加：

```csharp
[Test]
public void Known_Monster_Skill_Maps_To_A_Fixed_Variant()
{
    Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L, 50001));
    Assert.AreEqual(1, NpcAttackSchedule.VariantOf(0L, 50002));
    Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L, 50003));
    Assert.AreEqual(1, NpcAttackSchedule.VariantOf(0L, 50004));
    Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L, 50005));
    Assert.AreEqual(1, NpcAttackSchedule.VariantOf(0L, 50006));

    // 与时间戳无关（所有客户端必须同结论）
    long t = 638_000_000_000_000_000L;
    Assert.AreEqual(0, NpcAttackSchedule.VariantOf(t, 50001));
    Assert.AreEqual(1, NpcAttackSchedule.VariantOf(t, 50002));

    // 奇桶的时间戳回退是 1；0 映射必须仍返回 0，否则「固定 0」与「漏登记走回退」不可区分。
    Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks, 50001));
    Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks, 50003));
    Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks, 50005));
}

[Test]
public void Unknown_Skill_Falls_Back_To_Timestamp_Variant()
{
    long t = NpcAttackSchedule.VariantBucketTicks;
    Assert.AreEqual(1, NpcAttackSchedule.VariantOf(t, 99999));
    Assert.AreEqual(NpcAttackSchedule.VariantOf(t), NpcAttackSchedule.VariantOf(t, 99999));
}

[Test]
public void Scheduled_Attack_Uses_Skill_Variant()
{
    var schedule = new NpcAttackSchedule();
    schedule.Schedule(entityId: 7, serverTicks: 1_000L, skillId: 50002);
    var due = new List<NpcAttackSchedule.DueAttack>();

    schedule.Drain(renderAt: 1_000L, due);

    Assert.AreEqual(1, due.Count);
    Assert.AreEqual(1, due[0].Variant); // 50002 → Attack02
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode NpcAttackScheduleTests`

预期：编译失败，`VariantOf(long,int)` / `Schedule(..., int)` 不存在。

- [ ] **步骤 3：实现映射**

`NpcAttackSchedule.cs`：

```csharp
/// <summary>已知怪物技能的动画变体（v1 只映射现有 Attack01/02 两段）。
/// 未登记技能回退时间戳变体，保证旧服务端/未来技能仍能播放。</summary>
private static readonly Dictionary<int, int> SkillVariants = new Dictionary<int, int>
{
    { 50001, 0 }, { 50002, 1 }, { 50003, 0 },
    { 50004, 1 }, { 50005, 0 }, { 50006, 1 },
};

public static int VariantOf(long serverTicks) => TimestampVariant(serverTicks);

public static int VariantOf(long serverTicks, int skillId)
    => SkillVariants.TryGetValue(skillId, out int variant) ? variant : TimestampVariant(serverTicks);

private static int TimestampVariant(long serverTicks)
    => (int)((serverTicks / VariantBucketTicks) % 2);
```

`_pending` 改为 `List<(long EntityId, long ServerTicks, int SkillId)>`；`Schedule` 增加可选参数：

```csharp
public void Schedule(long entityId, long serverTicks, int skillId = 0)
    => _pending.Add((entityId, serverTicks, skillId));
```

`Drain` 中改为：

```csharp
into.Add(new DueAttack(_pending[i].EntityId,
    VariantOf(_pending[i].ServerTicks, _pending[i].SkillId), _pending[i].ServerTicks));
```

`RemoteEntityRegistry.ScheduleAttack`：

```csharp
public void ScheduleAttack(long casterId, long serverTimestamp, int skillId = 0)
{
    if (!_views.TryGetValue(casterId, out var view) || view.Kind != (byte)EntityKind.Npc) return;
    if (view.NpcAnimator == null) return;
    _attacks.Schedule(casterId, serverTimestamp, skillId);
}
```

`ClientNetworkHost.cs:679`：

```csharp
_remoteEntities!.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp, b.SkillId);
```

`CombatNetworkBridgeTests.cs`：

- 第 678 行宿主接线改为 `b => registry.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp, b.SkillId)`。
- 第 682 行改为 `int npcVariant = NpcAttackSchedule.VariantOf(npcCastTicks, 50004);`（50004 固定映射 1）。
- 第 696 行玩家时间戳改为 `long playerCastTicks = baseTicks + 4L * NpcAttackSchedule.VariantBucketTicks;`——`baseTicks` 在桶上奇偶为 0，+4 桶回退变体是 0，与 NPC 的映射变体 1 不同；原来的 +3 桶回退是 1，会和新映射撞上，导致「错排看不出来」的前提失效。
- 第 697 行改为 `Assert.AreNotEqual(npcVariant, NpcAttackSchedule.VariantOf(playerCastTicks, 20001), ...)`。

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode NpcAttackScheduleTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
```

预期：通过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs
git commit -m "feat(sync): map known monster skills to attack animation variants"
```

---

## 任务 9：P2 验收、文档与全量验证

**文件：**
- 创建：`Server/Server.Tests/Monster/MonsterP2AcceptanceTests.cs`
- 修改：`Server/Monster/README.md`
- 修改：`docs/superpowers/specs/2026-11-02-server-monster-navigation-ai-design.md`（状态行与 §11 追加 P2 备注）

- [ ] **步骤 1：编写 §7.2 场景级验收测试**

```csharp
using System;
using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

/// <summary>规格 §7.2 P2 行为验收：目标切换、多技能、脱战、目标死亡后的切换/脱战。</summary>
public class MonsterP2AcceptanceTests
{
    private static (EntityStore Store, MonsterModule Module, long Id) Make(int templateId = 1)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        // 非原点出生：避免 YawTowardOrigin(0,0,0) 的退化朝向；玩家放 z<5 即在视野锥内。
        long id = module.SpawnMonster(templateId, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 5f));
        return (store, module, id);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    [Fact]
    public void Later_Attacker_Takes_Over_After_Threshold_But_Not_During_Windup()
    {
        var (store, module, id) = Make();
        var state = module.GetState(id)!;
        var first = Login(store, 1, new Vector3(0f, 0f, 2f));   // 距出生点 3m
        var second = Login(store, 2, new Vector3(0f, 0f, 1.8f)); // 距出生点 3.2m

        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 110f);
        module.Tick(0);
        Assert.Equal(1L, state.TargetId); // 10% < 20%

        state.Threats.Add(2, 40f); // 150 > 120
        module.Tick(1);
        Assert.Equal(2L, state.TargetId);

        state.AttackResolveTick = 100;
        state.Threats.Add(1, 1000f); // 1100
        module.Tick(2);
        Assert.Equal(2L, state.TargetId); // 前摇锁
    }

    [Fact]
    public void Multi_Skill_Selection_Uses_Distance_Hp_And_Cooldown()
    {
        var (store, module, id) = Make(templateId: 3);
        var state = module.GetState(id)!;
        var entity = store.GetPlayer(id)!;
        entity.ApplyDamage(200f); // 300 -> 100 (<50%)
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m

        // 冷却由 CombatResolver.Decide 的 RecordCast 写入；module-only 宿主不记冷却，
        // 必须走真实裁决链，否则这条 60-tick 间隔断言会假红/假绿。
        var resolver = new CombatResolver(store, new Random(42));
        var casts = new List<(uint Tick, int SkillId)>();
        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            foreach (var (npcId, request) in module.DrainPendingCasts())
            {
                resolver.Decide(npcId, request, t);
                casts.Add((t, request.SkillId));
            }
            resolver.DrainResolved(t);
        }

        Assert.Contains(casts, c => c.SkillId == 50005);
        // 50005 冷却 4s = 60 tick：相邻两次 50005 的 tick 差至少 60
        var midRangeTicks = new List<uint>();
        foreach (var cast in casts)
            if (cast.SkillId == 50005) midRangeTicks.Add(cast.Tick);
        Assert.True(midRangeTicks.Count >= 2,
            $"需要至少两次 50005 才能验证冷却间隔，实际 {midRangeTicks.Count} 次");
        for (int i = 1; i < midRangeTicks.Count; i++)
            Assert.True(midRangeTicks[i] - midRangeTicks[i - 1] >= 60,
                $"50005 冷却不足：间隔 {midRangeTicks[i] - midRangeTicks[i - 1]} tick");
    }

    [Fact]
    public void Evade_Resets_At_Home_And_Target_Death_Prefers_Other_Threat()
    {
        var (store, module, id) = Make();
        var state = module.GetState(id)!;
        var first = Login(store, 1, new Vector3(0f, 0f, 2f));   // 距出生点 3m
        Login(store, 2, new Vector3(0f, 0f, 1.5f));             // 距出生点 3.5m

        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.Add(1, 100f);
        state.Threats.Add(2, 50f);
        first.ApplyDamage(999f);

        module.Tick(0);
        Assert.Equal(2L, state.TargetId); // 杀死当前目标后切到另一个
        Assert.Equal(MonsterMode.Combat, state.Mode);
    }

    /// <summary>§7.2 ③ 的另一半：唯一威胁死亡、没有其他有效目标时，
    /// 必须先熬过 45 tick grace 才 Evade，而不是立即脱战。</summary>
    [Fact]
    public void Sole_Target_Death_Enters_Evade_After_Grace()
    {
        var (store, module, id) = Make();
        var state = module.GetState(id)!;
        var player = Login(store, 1, new Vector3(0f, 0f, 2f)); // 距出生点 3m
        state.Mode = MonsterMode.Combat;
        state.TargetId = 1L;
        state.Threats.AddDamage(1, 10f);
        player.ApplyDamage(999f);

        for (uint t = 0; t <= MonsterBrain.TargetLostGraceTicks; t++)
        {
            module.Tick(t);
            if (t < MonsterBrain.TargetLostGraceTicks)
                Assert.Equal(MonsterMode.Combat, state.Mode);
            else
                Assert.Equal(MonsterMode.Evade, state.Mode);
        }
    }
}
```

- [ ] **步骤 2：运行验收测试**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterP2Acceptance"`

预期：3 个测试通过。

- [ ] **步骤 3：更新文档**

`Server/Monster/README.md`：
- 顶部 tick 描述改为「状态/死亡 → 控制锁（P3 预留）→ 仇恨有效性 → 模式转移 → 模式行为 → flush → 施法 drain → 判定 drain → 快照」。
- 行为规则改为 `Patrol | Combat | Evade | Dead`；新增 `ThreatTable` / `SkillSelector` / `MonsterBrain` / `MonsterCastBuilder` 文件行。
- 导航小节后新增「P2 行为」：
  - 威胁来源/切换滞后/嘲讽 API；
  - 多技能配置字段 `skills[]` 与 50005/50006；
  - 脱战三种触发、到家满血、post-evade grace 30 tick、超时 300 tick despawn + 30 tick respawn；
  - 目标丢失 grace 45 tick；
  - `leashRadius` 默认 detection + patrol + 10。
- 已知限制：P2 不含控制/击退/状态（P3）；客户端新技能无专属 VFX（复用 Attack01/02）；A2 语义已变为「偏出视野锥 ≠ 丢目标」。
- 测试命令补 `BrainTests` / `MonsterEvadeTests` / `MonsterP2Acceptance` / `NpcAttackScheduleTests`。
- 清理陈旧引用（任务 5 审查 Minor，统一在本任务做）：
  - `Server/Server3C.cs` 主循环注释仍写 `MonsterModule.DetectTarget` → 改 `MonsterBrain.TryAcquireBySight`；
  - `MonsterAcceptanceTests.cs` A2 小节标题仍写「丢目标 ≠ 打不中」→ 改「偏出视野锥 ≠ 丢目标」；
  - `MonsterWindupTests.cs` 注释仍以「离开视野锥就丢目标」解释用例 → 改为「ThreatTable 保留目标，只有 drop 半径/死亡/离线才丢」；
  - `Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs` 若仍引用 `MonsterModule.DetectTarget`，改为威胁表/`MonsterBrain` 语义（只改注释，不改行为）。

规格文档：
- 状态行改为 `**P1、P2 已实现**（P3–P4 未开始）`。
- §11 追加「P2 执行偏差」小节：Hero 记录本计划中与规格行文不同的实际实现（至少：A2 语义变更、`Reposition` 仅按配置 minRange、50005/50006 复用现有动画、P1 nav 债未在本轮偿还）。

- [ ] **步骤 4：全量验证**

```bash
dotnet build Server/Server3C.csproj
bash tools/test-server-inprocess.sh
bash tools/unity-tests.sh EditMode NpcAttackScheduleTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/unity-tests.sh EditMode CombatRulesTests
```

预期：服务端全绿；三个 Unity EditMode 类全绿。若本机 Unity Editor 未启动，记录未执行项与原因，不伪造通过。

- [ ] **步骤 5：Commit**

```bash
git add Server/Server.Tests/Monster/MonsterP2AcceptanceTests.cs Server/Monster/README.md \
  docs/superpowers/specs/2026-11-02-server-monster-navigation-ai-design.md
git commit -m "docs(monster): record P2 behavior, acceptance, and deviations"
```

---

## 风险与回退

| 风险 | 缓解 |
|---|---|
| `MonsterModule` tick 改造打乱「前摇到期清理 / 硬直窗口补状态」顺序 | 任务 5 保留这两段在死亡分支外；`MonsterWindupTests` / `MonsterModuleTests.Hit_Window_*` 全绿才算完成 |
| A2 语义变化被误判为回归 | 任务 5 显式改断言与注释；规格 §11 记录 |
| 新技能 50005/50006 在客户端无专属动画/VFX | 复用现有 Attack01/02；`NpcAttackSchedule` 映射固定变体；后续 P3 再补 VFX catalog |
| `skills` JSON 改型导致旧 fixture 静默落默认值 | 任务 2 更新坏 JSON fixture；`P2ConfigTests` 钉住发布配置 |
| Evade 超时 despawn 与死亡重生共用 `_respawns` 队列互相踩 | 两者都只追加 `(TemplateId, PatrolCenter, dueTick)`，到期统一 `SpawnMonster`；死亡先移除实体，Evade 超时同样先移除 |
| 目标在 navmesh 外导致 LOS 永远失败（P1 已知） | P2 不扩大范围；记录为 P1 遗留，`Reposition`/Chase 行为不受影响 |
| 仇恨表遍历顺序影响确定性 | `Highest` 只用值比较 + playerId 平局规则；测试钉住 |

## 自检

**规格覆盖：**

| 规格条目 | 任务 |
|---|---|
| §5.1 模式机（Patrol/Combat/Evade/Dead、感知只在 Patrol、前摇锁目标、目标丢失 grace） | 任务 5 |
| §5.2 ThreatTable（伤害/初始/嘲讽、20% 滞后、死亡/离线/超距移除、不衰减） | 任务 1、4、5 |
| §5.3 多技能（skills 配置、SkillSelector 过滤/排序、CastBuilder 形状、50005/50006、客户端变体映射） | 任务 2、3、6、8 |
| §5.4 脱战（三种触发、清仇恨、到家满血/清状态/grace、超时 despawn/respawn、受击不重新仇恨） | 任务 5、7 |
| §7.2 验收（切换/前摇锁、多技能、脱战、目标死亡切换） | 任务 9 |
| §10.1 配置字段（leashRadius、skills） | 任务 2 |
| §10.3 关键接口（MonsterStatusController 除外，属 P3） | 任务 3、5 |
| P1 债（导出器 tie-break、0.5ms/query） | **不在本轮**（用户确认），规格 §11 记录 |

**占位符扫描：** 无「待定/TODO/类似任务 N」；所有测试/实现代码块可直接执行。任务 5 已包含完整的 `MonsterBtActions` 动作实现（第 12 项），因此任务 5 结束即可编译并通过 `BrainTests`；任务 6/7 是验证与边界硬化，不引入跨任务编译依赖。

**类型一致性：** `MonsterMode`/`MonsterDamageKind`/`ThreatTable`/`TargetSelector`/`SelectedSkill`/`SkillSelector`/`MonsterCastBuilder`/`MonsterBrain` 的签名在各任务中一致；`MonsterState` 构造统一为 `(npcId, templateId, config)`；`MonsterModule.OnDamageTaken(npcId, attackerId, damage, kind)` 与 `Server3C` 接线一致。
