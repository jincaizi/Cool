# 命中判定时刻对齐与绕背躲怪 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把"这一刀挥出去"（决策）与"这一刀打没打中"（判定）拆成两个时刻，让服务端的判定时刻落在画面上的刀刃命中处，并让怪物攻击第一次可被走位躲开（跑出射程，或贴脸冲刺绕背）。

**架构：** `CombatRules` 表新增逐技能的判定延迟；`CombatResolver` 拆成 `Decide`（校验 + 冷却 + 广播挥砍 + 入队）与 `DrainResolved`（判定帧几何 + 掷骰 + 扣血）；`Server3C` 每 tick 先 drain 判定、再 `TickRespawns`；怪物在决策 tick 起进入前摇（站定 + 90°/s 转向目标），前摇期锁住行为树；`HitResolver` 的显式目标路径补上锥角检查。**客户端零逻辑改动**。

**技术栈：** C# / .NET 8（`Server/`）· Unity 2022.3 LTS + IL2CPP（共享核心 `SyncCore` 双端同源编译）· xUnit（`Server/Server.Tests`）· NUnit（Unity EditMode）· 无新文件、无协议改动

**规格：** `docs/superpowers/specs/2026-10-16-hit-judgment-timing-design.md`（F1–F20 事实基础、D1–D14 决策、R1–R8 风险）

---

## 开工前：基线与工具（**必须先做**）

- [ ] **服务端基线**：`dotnet test Server/Server.Tests/Server.Tests.csproj`（在仓库根跑）→ 记录 `Passed! - Failed: 0, Passed: N` 的 N
- [ ] **Unity 基线**：MCP `tests-run`（`testMode=EditMode`，不筛类）→ 记录通过/失败数
- [ ] **Unity 重编译**：编辑任何 `.cs` 之后、跑 Unity 测试之前，先调 MCP `assets-refresh`（项目既有纪律：工具外改脚本必须刷新）
- [ ] 记下两个数字，本计划每个任务结束时都要与它们对比

---

## 任务与规格编号的对应

| 本计划的任务 | 规格 §6 编号 | 差异与理由 |
|---|---|---|
| **任务 1**（N1a） | N1（前半） | 拆成两个任务/提交（表 / 锥角），互不依赖 |
| **任务 2**（N1b） | N1（后半） | 同上 |
| **任务 3**（N2） | N2 | **重切**：只做"两段化 API + 测试迁移"，**行为零变化**（临时保留 `ResolveCast` 包装，迁移完即删）——这是 R1 要求的"先纯迁移、再叠行为" |
| **任务 4**（N3） | N3 | **承接任务 3 的行为变化**：延迟生效 + 广播拆分 + tick 顺序 + 新行为用例 |
| **任务 5**（N4） | N4 | 怪物前摇（含机制简化，见下） |
| **任务 6**（N5） | N5 | 收敛为"新增守护用例 + 全量回归"（各任务的既有测试迁移已就地完成，否则任务边界处不可能保持绿色） |
| **任务 7**（N6） | N6 | 手动端到端验收 |

> 下文各节标题写作 `任务 <数字>（规格 <N 编号>）`：括号里的编号与规格 §6 一一对应，正文中引用步骤时用**规格编号**（如"任务 5 步骤 3"= 规格 N4 步骤 3）。

**对规格的三处实现级修正**（规格意图不变，机制更简单）：

1. **`AttackPending` + `NotifyCastResolved` → 单个 `AttackResolveTick`（uint?）**：怪物模块自己就知道决策 tick，用 `CombatRules.HitDelayTicks(entry)` 算出到期 tick，在 `Tick` 里自清理——少一条跨模块接线与一个可漂移的布尔。规格 C5/C6 的"清 `AttackPending`"由这条取代。
2. **`HitDelaySeconds ≤ CooldownSeconds` 只约束怪物技能（50001–50004）**：玩家普攻 10001 冷却为 **0**、判定帧 0.30s，天然不满足该不等式；该约束的本意是"防怪物前摇结束就接着挥第二刀"，与玩家无关。守护用例只遍历怪物技能。
3. **测试命名沿用既有风格**：`Server.Tests` 与 Unity EditMode 现有 132 个用例全部是 `PascalCase_With_Underscores`（如 `Reject_Unknown_Skill`）。`.claude/rules/test-standards.md` 要求 `test_` 前缀——**本计划按既有文件风格写**（CLAUDE.md §3"匹配既有风格"），提交前请人工拍板。

---

## 文件结构

| 文件 | 职责 | 改动 |
|---|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 技能表 + 形状校验 + tick 换算 | 修改：`SkillEntry` 加 `HitDelaySeconds`、表加值、加 `HitDelayTicks` |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs` | 服务端权威命中几何（纯核心，零 UnityEngine 依赖） | 修改：抽 `InCone` 私有助手，显式目标路径补锥角 |
| `Server/Handler/CastHandler.cs` | `CombatResolver`（纯核心裁决）+ `CastHandler`（广播薄壳） | 修改：拆 `Decide`/`DrainResolved` + pending 队列；广播拆 `BroadcastSwingAsync`/`BroadcastDamageAsync` |
| `Server/Server3C.cs` | 主循环 tick 顺序 + 包路由 | 修改：tick 内 drain 判定，早于 `TickRespawns` |
| `Server/Monster/MonsterState.cs` | 怪物 AI 黑板 | 修改：加 `AttackResolveTick` |
| `Server/Monster/SimpleMoveSystem.cs` | 平面移动 + 转身数学 | 修改：加 `TurnTowards`（复用 `RotateTowards`） |
| `Server/Monster/MonsterBtActions.cs` | 行为树动作 | 修改：`Attack` 前摇锁 + 转身 |
| `Server/Monster/MonsterModule.cs` | 怪物 tick / 行为树装配 | 修改：前摇到期清理 + Hostile 条件放宽 |
| `Server/Server.Tests/SyncCore/CombatRulesTests.cs` | 表用例（xUnit） | 修改：加延迟用例 + 怪物约束用例 |
| `Server/Server.Tests/SyncCore/HitResolverTests.cs` | 几何用例（xUnit） | 修改：加显式目标锥角用例 |
| `Server/Server.Tests/Combat/CastHandlerTests.cs` | 裁决用例（xUnit） | 修改：两步化迁移 |
| `Server/Server.Tests/Combat/CombatResolverNpcTests.cs` | 怪物裁决用例（xUnit） | 修改：两步化迁移 + 前摇窗口用例 |
| `Server/Server.Tests/Monster/MonsterCombatIntegrationTests.cs` | 怪物施法全链（xUnit） | 修改：两步化迁移 |
| `Server/Server.Tests/Monster/MonsterModuleTests.cs` | 怪物 AI 用例（xUnit） | 修改：两步化迁移 + 前摇锁用例 |
| `Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs` | 表用例（NUnit，客户端） | 修改：镜像新用例 |
| `Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs` | 几何用例（NUnit，客户端） | 修改：镜像新用例 |

**无新文件**（D-13 的红利）；**协议零改动**（F15）。

---

## 任务 1（规格 N1a）：`CombatRules` 判定延迟字段与表值

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`
- 测试：`Server/Server.Tests/SyncCore/CombatRulesTests.cs`、`Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs`

- [ ] **步骤 1：编写失败的测试（服务端 xUnit）**

追加到 `Server/Server.Tests/SyncCore/CombatRulesTests.cs` 的类内：

```csharp
    [Fact]
    public void HitDelay_Ticks_Map_From_Asset_Hit_Frames_At_15Hz()
    {
        // 表值来源：客户端动画资产的判定帧实测（ShapeBlock._hitboxTimings + CastTime）
        Assert.True(CombatRules.TryGet(10001, out var combo));
        Assert.Equal(0.30f, combo.HitDelaySeconds, 4);
        Assert.Equal(5U, CombatRules.HitDelayTicks(combo));   // 0.30 × 15 = 4.5 → 5

        Assert.True(CombatRules.TryGet(10002, out var heavy));
        Assert.Equal(0.35f, heavy.HitDelaySeconds, 4);
        Assert.Equal(6U, CombatRules.HitDelayTicks(heavy));   // 0.35 × 15 = 5.25 → 6

        Assert.True(CombatRules.TryGet(20002, out var spin));
        Assert.Equal(0f, spin.HitDelaySeconds, 4);            // 旋转无判定帧：多段命中未建模（D-9）
        Assert.Equal(0U, CombatRules.HitDelayTicks(spin));
    }

    [Fact]
    public void Monster_HitDelay_Never_Exceeds_Its_Cooldown()
    {
        // 约束只对怪物成立：前摇结束时若冷却已过，怪物会立刻挥第二刀（第一刀的剪辑被截断）。
        // 玩家普攻 10001 冷却为 0 且判定帧 0.30s——玩家侧由动画/连击链把关，不适用本约束。
        foreach (var skillId in new[] { 50001, 50002, 50003, 50004 })
        {
            Assert.True(CombatRules.TryGet(skillId, out var entry), $"缺 {skillId} 表项");
            Assert.True(entry!.HitDelaySeconds <= entry.CooldownSeconds + 0.0001f,
                $"skill {skillId} hitDelay={entry.HitDelaySeconds} > cooldown={entry.CooldownSeconds}");
        }
    }
```

- [ ] **步骤 2：运行验证失败**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatRulesTests"`
预期：**编译失败**——`SkillEntry` 没有 `HitDelaySeconds`、`CombatRules` 没有 `HitDelayTicks`

- [ ] **步骤 3：实现（`CombatRules.cs`）**

`SkillEntry` 加字段与构造参数（**追加在末位**，避免打乱既有实参顺序）：

```csharp
    public sealed class SkillEntry
    {
        // getter-only：仅经构造函数赋值，共享表状态不可变
        public int SkillId { get; }
        public int ShapeType { get; }        // HitShape.ShapeType（与客户端 TargetType 同值）
        public float MaxRange { get; }       // Single 距离上限
        public float MaxAreaRadius { get; }  // Circle 半径上限
        public float MaxAngle { get; }       // 锥形全角上限（与 ShapeBlock.Angle 同语义）
        public float CooldownSeconds { get; }
        public float BaseDamage { get; }
        public float CriticalRate { get; }
        /// <summary>判定帧延迟（秒，自技能激活起算）。来源 = 客户端动画资产的判定帧实测
        /// （<c>ShapeBlock._hitboxTimings</c> + CastTime；表注释逐条标注来源）。
        /// 服务端在 决策 tick + HitDelayTicks 处做几何判定。</summary>
        public float HitDelaySeconds { get; }

        public SkillEntry(int skillId, int shapeType, float maxRange, float maxAreaRadius,
            float maxAngle, float cooldownSeconds, float baseDamage, float criticalRate,
            float hitDelaySeconds)
        {
            SkillId = skillId;
            ShapeType = shapeType;
            MaxRange = maxRange;
            MaxAreaRadius = maxAreaRadius;
            MaxAngle = maxAngle;
            CooldownSeconds = cooldownSeconds;
            BaseDamage = baseDamage;
            CriticalRate = criticalRate;
            HitDelaySeconds = hitDelaySeconds;
        }
    }
```

表内每条追加第 9 个实参（玩家四条取**资产实测**，怪物四条为**占位**，注释注明）：

```csharp
            [10001] = new SkillEntry(skillId: 10001, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0f, baseDamage: 50f, criticalRate: 0.15f, hitDelaySeconds: 0.30f),
            [10002] = new SkillEntry(skillId: 10002, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0.5f, baseDamage: 90f, criticalRate: 0.15f, hitDelaySeconds: 0.35f),
            [20001] = new SkillEntry(skillId: 20001, shapeType: 0, maxRange: 3.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 6f, baseDamage: 120f, criticalRate: 0.2f, hitDelaySeconds: 0.30f),
            [20002] = new SkillEntry(skillId: 20002, shapeType: 1, maxRange: 2f, maxAreaRadius: 2.5f, maxAngle: 120f, cooldownSeconds: 5f, baseDamage: 160f, criticalRate: 0.25f, hitDelaySeconds: 0f),
            // 怪物四条：**占位 0.35s**（人类裁定 D-8）。NPC 攻击剪辑无动画事件、判定帧未实测
            //（DogKnight Attack01 时长 0.833s@30fps ⇒ 0.35s ≈ 42% 处，合理但未验证）。
            // 实测后逐条替换：见 docs/superpowers/specs/2026-10-16-hit-judgment-timing-design.md R3。
            [50001] = new SkillEntry(skillId: 50001, shapeType: 0, maxRange: 1.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 10f, criticalRate: 0f, hitDelaySeconds: 0.35f),
            [50002] = new SkillEntry(skillId: 50002, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 15f, criticalRate: 0f, hitDelaySeconds: 0.35f),
            [50003] = new SkillEntry(skillId: 50003, shapeType: 0, maxRange: 2.2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1.5f, baseDamage: 30f, criticalRate: 0f, hitDelaySeconds: 0.35f),
            [50004] = new SkillEntry(skillId: 50004, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 2f, baseDamage: 20f, criticalRate: 0f, hitDelaySeconds: 0.35f),
```

`CombatRules` 加换算方法（紧挨 `CooldownTicks` 放，沿用同一惯例）：

```csharp
        /// <summary>判定延迟换算为 tick 数（15Hz 基准，向上取整，与 CooldownTicks 同惯例）</summary>
        public static uint HitDelayTicks(SkillEntry entry) =>
            (uint)Math.Ceiling(entry.HitDelaySeconds * TickRateHz);
```

- [ ] **步骤 4：运行验证通过**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatRulesTests"`
预期：PASS（含既有 `Table_Values_Match_Client_Assets` 等）

- [ ] **步骤 5：镜像到客户端 NUnit（F16）**

`Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs` 追加同名两用例，NUnit 语法：

```csharp
        [Test]
        public void HitDelay_Ticks_Map_From_Asset_Hit_Frames_At_15Hz()
        {
            Assert.IsTrue(CombatRules.TryGet(10001, out var combo));
            Assert.AreEqual(0.30f, combo.HitDelaySeconds, 0.0001f);
            Assert.AreEqual(5U, CombatRules.HitDelayTicks(combo));

            Assert.IsTrue(CombatRules.TryGet(10002, out var heavy));
            Assert.AreEqual(0.35f, heavy.HitDelaySeconds, 0.0001f);
            Assert.AreEqual(6U, CombatRules.HitDelayTicks(heavy));

            Assert.IsTrue(CombatRules.TryGet(20002, out var spin));
            Assert.AreEqual(0f, spin.HitDelaySeconds, 0.0001f);
            Assert.AreEqual(0U, CombatRules.HitDelayTicks(spin));
        }

        [Test]
        public void Monster_HitDelay_Never_Exceeds_Its_Cooldown()
        {
            foreach (var skillId in new[] { 50001, 50002, 50003, 50004 })
            {
                Assert.IsTrue(CombatRules.TryGet(skillId, out var entry), $"缺 {skillId} 表项");
                Assert.LessOrEqual(entry.HitDelaySeconds, entry.CooldownSeconds + 0.0001f,
                    $"skill {skillId} hitDelay > cooldown");
            }
        }
```

- [ ] **步骤 6：Unity 侧验证**

MCP `assets-refresh` → MCP `tests-run`（`testMode=EditMode`，`testClass=CombatRulesTests`）
预期：PASS，且总数比基线 +2

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs Server/Server.Tests/SyncCore/CombatRulesTests.cs Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs
git commit -m "feat(combat): give every synced skill a hit-frame delay"
```

---

## 任务 2（规格 N1b）：`HitResolver` 显式目标补锥角（绕背躲怪的地基）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs`
- 测试：`Server/Server.Tests/SyncCore/HitResolverTests.cs`、`Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs`

- [ ] **步骤 1：编写失败的测试（服务端 xUnit）**

```csharp
    [Fact]
    public void Single_Explicit_Target_Behind_Caster_Misses()
    {
        // D-6 绕背躲怪：显式目标同样要过锥（此前只查距离，锥角是死参数）
        var candidates = new List<CombatCandidate> { C(2, 0f, -2f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Empty(hits);
    }
```

- [ ] **步骤 2：运行验证失败**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~HitResolverTests"`
预期：`Single_Explicit_Target_Behind_Caster_Misses` **FAIL**（当前身后 2m 也被判命中）；其余 PASS

- [ ] **步骤 3：实现（`HitResolver.cs`）**

把 `Resolve` 中 `forward/halfCos` 的计算**提到两个分支之前**，两个分支共用新助手（锥的唯一定义）：

```csharp
            // 锥的唯一定义（自动目标与显式目标共用）——yaw 语义 = Unity 欧拉 Y：yaw=0 → +Z
            float forwardX = (float)Math.Sin(casterYawDeg * Math.PI / 180.0);
            float forwardZ = (float)Math.Cos(casterYawDeg * Math.PI / 180.0);
            float halfCos = (float)Math.Cos(shape.Angle * 0.5f * Math.PI / 180.0);

            // Single
            if (targetId > 0)
            {
                foreach (var c in candidates)
                {
                    if (c.Id != targetId || !c.Alive) continue;
                    if (DistXZ(casterPos, c.Position) > shape.Range) continue;
                    if (!InCone(casterPos, forwardX, forwardZ, halfCos, c.Position)) continue;
                    hits.Add(c.Id);
                    break;
                }
                return hits;
            }

            // 自动目标：Range 内 + 前方锥内最近者
            long best = 0;
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                if (!c.Alive) continue;
                float dist = DistXZ(casterPos, c.Position);
                if (dist > shape.Range) continue;
                if (!InCone(casterPos, forwardX, forwardZ, halfCos, c.Position)) continue;
                if (dist < bestDist) { best = c.Id; bestDist = dist; }
            }
            if (best != 0) hits.Add(best);
            return hits;
        }

        /// <summary>前方 ±Angle/2 锥内（含边界）；零距离退化视为锥内（D-P4-2a——双端同出生点）。</summary>
        private static bool InCone(Vec3 casterPos, float forwardX, float forwardZ, float halfCos, Vec3 target)
        {
            float dx = target.X - casterPos.X;
            float dz = target.Z - casterPos.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 0.0001f) return true;
            return (forwardX * dx + forwardZ * dz) / dist >= halfCos;
        }
```

**注意**：`DistXZ` 保持不变；自动目标分支的**判据语义一字未改**（`dist > Range` 提前 continue、`dot < halfCos` 拒绝、零距离放行），类注释里"语义与客户端 AttackShape 对齐"一段要补一句"显式目标同样过锥（D-6）"。

- [ ] **步骤 4：运行验证通过（含护栏）**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~HitResolverTests"`
预期：PASS。**特别确认这三条仍绿**（它们钉住锥的边界语义，是这次重构的护栏）：
`Single_AutoTarget_Exactly_At_Cone_Edge_Hits`、`Single_AutoTarget_ZeroDistance_Degenerate_Hits`、`Single_Explicit_Target_Exactly_At_Range_Hits`

- [ ] **步骤 5：补护栏用例（服务端）**

```csharp
    [Fact]
    public void Single_Explicit_Target_At_Cone_Edge_Hits()
    {
        // 锥边界恰 60°：(0.8660254, 0.5) → dot == halfCos，含边界
        var candidates = new List<CombatCandidate> { C(2, 0.8660254f, 0.5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_Explicit_Target_Zero_Distance_Hits()
    {
        // 与自动目标同一条零距离护栏
        var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }
```

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~HitResolverTests"` → 预期 PASS

- [ ] **步骤 6：镜像到客户端 NUnit（F16）**

`Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs` 追加三条同构用例，NUnit 断言写法：

```csharp
        [Test]
        public void Single_Explicit_Target_Behind_Caster_Misses()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, -2f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void Single_Explicit_Target_At_Cone_Edge_Hits()
        {
            var candidates = new List<CombatCandidate> { C(2, 0.8660254f, 0.5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_Explicit_Target_Zero_Distance_Hits()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }
```

MCP `assets-refresh` → `tests-run`（EditMode，类名 `HitResolverTests`）→ 预期 PASS（+3）

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs Server/Server.Tests/SyncCore/HitResolverTests.cs Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs
git commit -m "feat(combat): the cone applies to explicit targets too"
```

---

## 任务 3（规格 N2）：`CombatResolver` 两段化（**行为零变化**）+ 测试迁移

> 本任务结束时，所有既有测试的**断言一字不改**，只是从"一步到位"变成"两步调用"。R1 要求的纯迁移就是它。

**文件：**
- 修改：`Server/Handler/CastHandler.cs`
- 测试：`Server/Server.Tests/Combat/CastHandlerTests.cs`、`Server/Server.Tests/Combat/CombatResolverNpcTests.cs`、`Server/Server.Tests/Monster/MonsterCombatIntegrationTests.cs`、`Server/Server.Tests/Monster/MonsterModuleTests.cs`

- [ ] **步骤 1：写新 API 的失败测试（服务端 xUnit）**

新建 `Server/Server.Tests/Combat/CastResolutionSchedulingTests.cs`（**这是本任务唯一的新文件，且是测试文件**）：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>N2：决策 / 判定两段化——本任务内两段在同 tick 完成（行为零变化），
/// N3 起 resolveTick = decideTick + HitDelayTicks。</summary>
public class CastResolutionSchedulingTests
{
    private static (EntityStore Store, CombatResolver Resolver, long Caster, long Target) World(float targetZ)
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P");
        caster.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        var target = store.AddPlayer(2, "T");
        target.UpdatePosition(new Vector3(0f, 0f, targetZ), 0f, 0f, 1L, 0U);
        return (store, new CombatResolver(store, new System.Random(42)), 1, 2);
    }

    private static CastRequest Swing(long targetId = 0) => new CastRequest
    {
        SkillId = 10001, // BA_Combo1：Single range 2
        TargetPlayerId = targetId,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
    };

    [Fact]
    public void Decide_Returns_Broadcast_But_No_Hits()
    {
        var (store, resolver, caster, target) = World(1.5f);

        var decision = resolver.Decide(caster, Swing(), 10);

        Assert.True(decision.Accepted);
        Assert.NotNull(decision.Broadcast);
        Assert.Equal(caster, decision.Broadcast!.CasterPlayerId);
        Assert.Equal(10001, decision.Broadcast.SkillId);
        Assert.NotEqual(0L, decision.Broadcast.ServerTimestamp);  // 决策时刻（wall-clock）
        Assert.Empty(decision.Hits);                              // 判定还没发生
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);       // 目标未掉血
    }

    [Fact]
    public void DrainResolved_At_DecideTick_Yields_This_Casts_Hits()
    {
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);

        var resolved = resolver.DrainResolved(10);          // N2：同 tick 到期

        var one = Assert.Single(resolved);
        Assert.Equal(caster, one.Broadcast!.CasterPlayerId);
        var hit = Assert.Single(one.Hits);
        Assert.Equal(target, hit.TargetPlayerId);
        Assert.Equal(100f - hit.Damage, store.GetPlayer(target)!.Hp, 4);
    }
}
```

- [ ] **步骤 2：运行验证失败**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CastResolutionSchedulingTests"`
预期：**编译失败**——`CombatResolver` 没有 `Decide` / `DrainResolved`

- [ ] **步骤 3：实现（`Server/Handler/CastHandler.cs` 的 `CombatResolver` 类）**

将 `ResolveCast` 拆成三段（**校验与几何分离，队列在 resolver 内部**）；`CastResolution` 加一个字段：

```csharp
        /// <summary>施放裁决结果（输出载体）——决策阶段只填 Accepted/RejectReason/Broadcast/IsNpcCaster，
        /// 判定阶段（DrainResolved）才填 Hits</summary>
        public sealed class CastResolution
        {
            public bool Accepted;
            public string RejectReason = string.Empty;
            public CastBroadcast? Broadcast;
            public bool IsNpcCaster;
            public readonly List<HitEntry> Hits = new List<HitEntry>();
        }

        private readonly struct PendingCast
        {
            public readonly long CasterId;
            public readonly CastRequest Request;
            public readonly uint ResolveTick;
            public readonly CastBroadcast Broadcast;
            public readonly bool IsNpcCaster;

            public PendingCast(long casterId, CastRequest request, uint resolveTick,
                CastBroadcast broadcast, bool isNpcCaster)
            {
                CasterId = casterId; Request = request; ResolveTick = resolveTick;
                Broadcast = broadcast; IsNpcCaster = isNpcCaster;
            }
        }

        private readonly List<PendingCast> _pending = new List<PendingCast>();

        /// <summary>决策：校验 + 冷却记录 + 生成广播 + 入队。判定（几何/掷骰/扣血）推迟到 ResolveTick。</summary>
        public CastResolution Decide(long casterId, CastRequest request, uint tick)
        {
            var resolution = new CastResolution();
            var caster = _store.GetPlayer(casterId);
            if (caster == null) return Reject(resolution, "caster-not-found");
            if (!caster.IsAlive) return Reject(resolution, "caster-dead");
            if (!CombatRules.TryGet(request.SkillId, out var entry))
                return Reject(resolution, "unknown-skill");
            if (!CombatRules.ValidateShape(request.Shape, entry))
                return Reject(resolution, "shape-tampered");
            if (!caster.IsCastReady(request.SkillId, tick, CombatRules.CooldownTicks(entry)))
                return Reject(resolution, "cooldown");

            // D-3：冷却记录在决策（空挥也冷却，D-P4-5）
            caster.RecordCast(request.SkillId, tick);

            resolution.Accepted = true;
            resolution.IsNpcCaster = caster.Kind == (byte)EntityKind.Npc;
            resolution.Broadcast = new CastBroadcast
            {
                SkillId = request.SkillId,
                CasterPlayerId = casterId,
                TargetPlayerId = request.TargetPlayerId,
                CasterX = caster.Position.X,
                CasterY = caster.Position.Y,
                CasterZ = caster.Position.Z,
                CasterRotation = caster.Rotation,
                Shape = request.Shape,
                ServerTimestamp = ServerClock.NowTicks()
            };
            // N2：同 tick 到期（行为零变化）。N3 改为 tick + CombatRules.HitDelayTicks(entry)
            _pending.Add(new PendingCast(casterId, request, tick, resolution.Broadcast, resolution.IsNpcCaster));
            return resolution;
        }

        /// <summary>取出到期（含本 tick）的待判定施法并裁决。单遍压缩，出队顺序 = 入队顺序（确定性）。</summary>
        public List<CastResolution> DrainResolved(uint tick)
        {
            List<CastResolution>? resolved = null;
            int write = 0;
            for (int read = 0; read < _pending.Count; read++)
            {
                var pending = _pending[read];
                if (pending.ResolveTick > tick) { _pending[write++] = pending; continue; }
                var resolution = ResolveHits(pending);
                if (resolution == null) continue;          // 施法者已死/已移除：丢弃这一刀（D-4）
                (resolved ??= new List<CastResolution>()).Add(resolution);
            }
            if (write < _pending.Count) _pending.RemoveRange(write, _pending.Count - write);
            return resolved ?? new List<CastResolution>();
        }

        /// <summary>判定帧：复查施法者存活 → 几何 → 服务端掷骰 → 扣血。返回 null 表示丢弃。</summary>
        private CastResolution? ResolveHits(PendingCast pending)
        {
            var caster = _store.GetPlayer(pending.CasterId);
            if (caster == null || !caster.IsAlive) return null;
            if (!CombatRules.TryGet(pending.Request.SkillId, out var entry)) return null; // 决策已校验，防御分支

            var candidates = new List<CombatCandidate>();
            foreach (var p in _store.All)
            {
                if (p.PlayerId == pending.CasterId) continue;
                if (pending.IsNpcCaster && (p.Kind != (byte)EntityKind.Player || !p.IsLoggedIn)) continue;
                candidates.Add(new CombatCandidate(p.PlayerId,
                    new Vec3(p.Position.X, p.Position.Y, p.Position.Z), p.IsAlive));
            }

            var hitIds = HitResolver.Resolve(
                new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z),
                caster.Rotation, pending.Request.TargetPlayerId, pending.Request.Shape, candidates);

            var resolution = new CastResolution
            {
                Accepted = true,
                IsNpcCaster = pending.IsNpcCaster,
                Broadcast = pending.Broadcast
            };
            foreach (var targetId in hitIds)
            {
                var target = _store.GetPlayer(targetId);
                if (target == null) continue;
                float damage = RollDamage(entry, out bool isCritical);
                target.ApplyDamage(damage);
                resolution.Hits.Add(new HitEntry
                {
                    TargetPlayerId = targetId,
                    Damage = damage,
                    IsCritical = isCritical,
                    TargetRemainingHp = target.Hp,
                    HitX = target.Position.X,
                    HitZ = target.Position.Z
                });
            }
            return resolution;
        }

        /// <summary>N2 兼容包装：决策 + 立即结算**本次这一条**（行为与拆分前逐位相同；**N3 迁移完成后删除**）。
        /// 只按引用取出本请求对应的那一项——同 tick 多次施法不会互相吞噬（其余项留给 DrainResolved）。</summary>
        public CastResolution ResolveCast(long playerId, CastRequest request, uint tick)
        {
            var decision = Decide(playerId, request, tick);
            if (!decision.Accepted) return decision;
            for (int i = 0; i < _pending.Count; i++)
            {
                var pending = _pending[i];
                if (pending.CasterId != playerId || !ReferenceEquals(pending.Request, request)) continue;
                _pending.RemoveAt(i);                       // 兼容路径一次性使用：无 O(n²) 顾虑
                return ResolveHits(pending) ?? decision;    // 施法者已死/已移除 → Accepted=true、Hits 空
            }
            return decision;
        }
```

`RollDamage` 与 `Reject` **原样保留**；`ResolveCast` 上方的旧注释补一行"（内部已拆为 `Decide` + `ResolveHits`）"。

**`CastHandler` 本任务完全不动**——`Resolve` 仍转发到 `_resolver.ResolveCast`（即兼容包装），`HandleCastAsync` / `HandleNpcCastAsync` 一字未改：

```csharp
        /// <summary>裁决入口（P5 怪物施法复用——怪物无 session，Server3C tick 内直调）</summary>
        public CombatResolver.CastResolution Resolve(long casterId, CastRequest request, uint tick)
            => _resolver.ResolveCast(casterId, request, tick);
```

⇒ 本任务对生产行为零影响：`Server3C` 的调用方式、广播时机、扣血时刻全部与拆分前一致。

- [ ] **步骤 4：运行验证通过**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj`
预期：**全绿**，且通过数 = 基线 + 2（新增两条）。既有 132 条一行未改

- [ ] **步骤 5：迁移 `CastHandlerTests.cs`（纯迁移）**

文件顶部注解区加一个两步助手，并把全部 `resolver.ResolveCast(...)` 调用替换为它：

```csharp
    /// <summary>N2 迁移助手：决策 → 推进到到期 → 取回该次施法的结果。
    /// 断言一字未改，只是把"一步到位"拆成两步——N3 起延迟生效后本助手**无需修改**。</summary>
    private static CombatResolver.CastResolution Resolve(CombatResolver resolver, long casterId,
        CastRequest request, uint decideTick)
    {
        var decision = resolver.Decide(casterId, request, decideTick);
        if (!decision.Accepted) return decision;
        foreach (var r in resolver.DrainResolved(decideTick + 8))
            if (r.Broadcast!.CasterPlayerId == casterId && r.Broadcast.SkillId == request.SkillId)
                return r;
        return decision;
    }
```

> `+8` = 表内最大 `HitDelayTicks`（10002 的 6，20001/Q 的 5）之上留裕量；N3 起仍成立。

全部调用点改写示例（原 `resolver.ResolveCast(caster.PlayerId, request, 10)` → `Resolve(resolver, caster.PlayerId, request, 10)`）。
**逐条对照**：`Reject_Unknown_Skill`、`Reject_Caster_Dead`、`Reject_On_Cooldown`（这条要注意：`Resolve(...)` 内部两次调用分别推进 `10 / 11 / 100`，断言不变）、`Reject_Inflated_Range_Shape`、`Reject_Wrong_Shape_Type`、`Accept_Single_Hits_Target_And_Applies_Server_Damage`、`Circle_Hits_Multiple_Excludes_Dead_And_Far`、`Whiff_No_Target_Accepted_With_No_Hits`、`Cooldown_Records_Even_On_Whiff`、`Zero_Distance_AutoTarget_Hits`、`Dead_Target_Excluded_From_Circle`

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CastHandlerTests"` → 预期 PASS

- [ ] **步骤 6：迁移 `CombatResolverNpcTests.cs`（纯迁移）**

（先修正一个既有测试的隐患：`Npc_Cast_Auto_Target_Skips_Non_Logged_In_Entities` 里的 `MonsterBite(0, 1.5f)` 是声明了未用的形参 `target`，保留原样、不动。）

加同款助手（怪物用的 `target` 参数保留）：

```csharp
    private static CombatResolver.CastResolution Resolve(CombatResolver resolver, long casterId,
        CastRequest request, uint decideTick)
    {
        var decision = resolver.Decide(casterId, request, decideTick);
        if (!decision.Accepted) return decision;
        foreach (var r in resolver.DrainResolved(decideTick + 8))
            if (r.Broadcast!.CasterPlayerId == casterId && r.Broadcast.SkillId == request.SkillId)
                return r;
        return decision;
    }
```

改写六处调用点：`Player_Cast_Explicit_Npc_Target_Damages_Npc_Authoritatively`、`Player_Cast_Auto_Target_Hits_Closer_Npc_Not_Farther_Player`、`Npc_Cast_Hits_Player_With_Exact_Damage_And_Records_Cooldown`（**注意**：该用例连续两次施法断言"同 tick 重施被拒"，迁移后第二次仍调 `Resolve(resolver, npc, MonsterBite(...), 10)` —— 拒绝发生在 `Decide`，断言不变）、`Npc_Cast_Ignores_Other_Npcs`、`Npc_Cast_Auto_Target_Skips_Non_Logged_In_Entities`、`Dead_Npc_Cannot_Cast_And_Cannot_Be_Hit`。

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatResolverNpcTests"` → 预期 PASS

- [ ] **步骤 7：迁移 `MonsterCombatIntegrationTests.cs`（纯迁移）**

把 tick 循环内的 `resolver.ResolveCast(npcId, request, t)` 换成 `resolver.Decide(npcId, request, t)`，并在循环体末尾 drain：

```csharp
        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            foreach (var (npcId, request) in module.DrainPendingCasts())
            {
                var decision = resolver.Decide(npcId, request, t);
                if (!decision.Accepted) continue; // BT 冷却门先行——正常不应发生（防御跳过）
                castCount++;
                if (castCount > 1)
                    Assert.True(t - lastCastTick >= 15, $"casts spaced only {t - lastCastTick} ticks (cooldown not enforced)");
                lastCastTick = t;
            }
            resolver.DrainResolved(t);   // N2：同 tick 到期，结果本用例不消费
        }
```

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MonsterCombatIntegrationTests"` → 预期 PASS

- [ ] **步骤 8：迁移 `MonsterModuleTests.cs` 的两处（纯迁移）**

`Detection_Aggro_And_Attack_Queue_Targets_Closest_Player`（读 `DrainPendingCasts` 断言即可，**无需改**）与 `Hit_Window_Expiry_Restores_Locomotion_While_Target_Stays_In_Attack_Range`（第 198-207 行的前置施法改成 `Decide` + `DrainResolved`）：

```csharp
        uint castTick = 0;
        bool casted = false;
        for (uint t = 0; t < 10 && !casted; t++)
        {
            module.Tick(t);
            foreach (var (id, request) in module.DrainPendingCasts())
            {
                resolver.Decide(id, request, t);
                resolver.DrainResolved(t);      // N2：同 tick 到期
                castTick = t;
                casted = true;
            }
        }
```

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj` → 预期**全绿**（总数 = 基线 + 2）

- [ ] **步骤 9：Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Server.Tests/
git commit -m "refactor(combat): split the cast decision from the hit resolution"
```

---

## 任务 4（规格 N3）：延迟生效 + 广播拆分 + tick 顺序

**文件：**
- 修改：`Server/Handler/CastHandler.cs`、`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Combat/CastResolutionSchedulingTests.cs`（本任务扩写）

- [ ] **步骤 1：写失败测试（前摇窗口的四种结局）**

在 `CastResolutionSchedulingTests` 追加（并**删掉**步骤 1 留下的占位断言那一行）：

```csharp
    [Fact]
    public void Target_Leaving_Range_During_The_Window_Misses()
    {
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);                       // 决策：目标在射程内
        resolver.DrainResolved(10);                                 // 模拟 tick 循环：决策那一 tick 也会 drain
        store.GetPlayer(target)!.UpdatePosition(new Vector3(0f, 0f, 5f), 0f, 0f, 1L, 0U);

        var one = Assert.Single(resolver.DrainResolved(10 + CombatRules.HitDelayTicks(EntryOf(10001))));

        Assert.Empty(one.Hits);                                     // 判定帧已出射程 → 空挥
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Target_Circling_Behind_During_The_Window_Misses()
    {
        // 绕背躲怪：判定帧在身后（夹角 180° > 半锥 60°）
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);
        resolver.DrainResolved(10);                                 // 模拟 tick 循环
        store.GetPlayer(target)!.UpdatePosition(new Vector3(0f, 0f, -1.5f), 0f, 0f, 1L, 0U);

        var one = Assert.Single(resolver.DrainResolved(10 + CombatRules.HitDelayTicks(EntryOf(10001))));

        Assert.Empty(one.Hits);
    }

    [Fact]
    public void Caster_Dying_Before_The_Judgment_Drops_The_Swing()
    {
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);
        resolver.DrainResolved(10);                                 // 模拟 tick 循环（此刻施法者还活着）
        store.GetPlayer(caster)!.ApplyDamage(1000f);                // 打断抬手 = 免伤

        var resolved = resolver.DrainResolved(10 + CombatRules.HitDelayTicks(EntryOf(10001)));

        Assert.Empty(resolved);                                     // 整条结果被丢弃（不下发 DamageResult）
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Zero_Delay_Skill_Resolves_In_The_Same_Tick()
    {
        var (store, resolver, caster, target) = World(1.5f);
        var spin = new CastRequest
        {
            SkillId = 20002,                                        // HitDelay = 0（D-9 向后兼容）
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f }
        };
        resolver.Decide(caster, spin, 10);

        var one = Assert.Single(resolver.DrainResolved(10));

        Assert.Single(one.Hits);
    }

    private static SkillEntry EntryOf(int skillId)
    {
        CombatRules.TryGet(skillId, out var entry);
        return entry!;
    }
```

- [ ] **步骤 2：运行验证失败**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CastResolutionSchedulingTests"`
预期：三条 **FAIL**，且各自的失败点不同（这正是"延迟还没生效"的证据）：
- `Target_Leaving_...` / `Target_Circling_...`：`DrainResolved(10)` 在 N2 语义下**已经结算完毕**，所以最终 drain 拿到空列表 → `Assert.Single` 抛异常
- `Caster_Dying_...`：`DrainResolved(10)` 那一刀已经打出去了 → 最终 `Assert.Equal(100f, Hp, 4)` 失败（目标已掉 50）
`Zero_Delay_...` PASS（零延迟在改动前后都应同 tick 出队）

- [ ] **步骤 3：延迟生效（`CombatResolver.Decide` 一行 + 删兼容包装）**

```csharp
            // D-1/D-2：判定帧延迟由服务端表自持——判定发生在 tick + HitDelayTicks
            _pending.Add(new PendingCast(casterId, request, tick + CombatRules.HitDelayTicks(entry),
                resolution.Broadcast, resolution.IsNpcCaster));
```

**删除** `ResolveCast` 兼容包装方法与其上注释（N2 的迁移已完成）。若此时仍有调用点，编译器会逐一点名——按报错清单把它们改成 `Decide` + `DrainResolved`。

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj` → 预期全绿（含新增四条）

- [ ] **步骤 4：`CastHandler` 广播拆分**

删除 `Resolve` 与 `HandleNpcCastAsync`，改为两个方法 + 一个收件人助手：

```csharp
        /// <summary>裁决入口（怪物：tick 内决策 → 校验 + 冷却 + 入队）</summary>
        public CombatResolver.CastResolution Decide(long casterId, CastRequest request, uint tick)
            => _resolver.Decide(casterId, request, tick);

        /// <summary>到期取出（Server3C 每 tick 在 TickRespawns 之前调用）</summary>
        public System.Collections.Generic.List<CombatResolver.CastResolution> DrainResolved(uint tick)
            => _resolver.DrainResolved(tick);

        /// <summary>决策阶段广播"挥砍开始"（F7：时间戳 = 剪辑起始时刻）。拒绝分支内部处理（日志 + 计数）。</summary>
        public async Task BroadcastSwingAsync(CombatResolver.CastResolution decision)
        {
            if (!decision.Accepted || decision.Broadcast == null)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: caster={decision.Broadcast?.CasterPlayerId} reason={decision.RejectReason}");
                return;
            }

            var broadcast = decision.Broadcast;
            if (decision.IsNpcCaster)
            {
                foreach (var recipientId in _server.GetObserversOf(broadcast.CasterPlayerId))
                {
                    var s = _server.GetSession(recipientId);
                    if (s != null)
                        await _server.SendToSessionAsync(s, MessageId.CastBroadcast, broadcast);
                }
                return;
            }

            var castRecipients = new HashSet<long> { broadcast.CasterPlayerId };
            foreach (var p in _server.GetVisiblePlayers(broadcast.CasterPlayerId))
                castRecipients.Add(p.PlayerId);
            foreach (var recipientId in castRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.CastBroadcast, broadcast);
            }
        }

        /// <summary>判定阶段广播伤害（D-P4-6）。施法者收件人：仅玩家施法时包含（怪物无 session）。</summary>
        public async Task BroadcastDamageAsync(CombatResolver.CastResolution resolution)
        {
            if (resolution.Hits.Count == 0 || resolution.Broadcast == null) return;

            var result = new DamageResult
            {
                SourcePlayerId = resolution.Broadcast.CasterPlayerId,
                SkillId = resolution.Broadcast.SkillId,
                ServerTimestamp = ServerClock.NowTicks(),
                Hits = resolution.Hits
            };

            var damageRecipients = new HashSet<long>();
            if (!resolution.IsNpcCaster) damageRecipients.Add(resolution.Broadcast.CasterPlayerId);
            foreach (var hit in resolution.Hits)
                damageRecipients.Add(hit.TargetPlayerId);

            foreach (var recipientId in damageRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.DamageResult, result);
            }
        }
```

`HandleCastAsync` 改为（**F17：包处理早于 tick 处理，`CurrentTick` 还是上一 tick**）：

```csharp
        public async Task HandleCastAsync(IServerSession session, CastRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: session={session.SessionId} skill={request.SkillId} reason=not-logged-in");
                return;
            }

            // F17：包在 tick 之间处理，此刻 CurrentTick 仍是上一 tick 的值——
            // 本次决策归属"本轮即将运行的 tick"，+1 后与怪物路径（tick 内决策）口径一致。
            var decision = _resolver.Decide(playerId.Value, request, _server.CurrentTick + 1);
            await BroadcastSwingAsync(decision);
        }
```

（`HandleCastAsync` 内的旧伤害广播段整段删除——它现在由 `BroadcastDamageAsync` 承担。）

- [ ] **步骤 5：`Server3C` tick 顺序**

替换 tick 回调里的怪物段（原 145-151 行）：

```csharp
                    // P5：怪物 AI tick → 攻击决策入队（校验 + 冷却 + 广播挥砍）
                    if (_monsterModule != null)
                    {
                        _monsterModule.Tick((uint)tick);
                        foreach (var (npcId, request) in _monsterModule.DrainPendingCasts())
                        {
                            var decision = _castHandler.Decide(npcId, request, (uint)tick);
                            _ = _castHandler.BroadcastSwingAsync(decision); // 拒绝分支内部处理（日志+计数）——审计口径须可证伪
                        }

                        // 判定帧到期（F13：必须早于 TickRespawns——本 tick 打死的实体即时进死亡流程）
                        foreach (var resolution in _castHandler.DrainResolved((uint)tick))
                            _ = _castHandler.BroadcastDamageAsync(resolution);

                        foreach (var npcId in _monsterModule.DrainRemovedNpcs())
                            _aoi.RemoveEntity(npcId);
                    }
```

- [ ] **步骤 6：跑全量服务端测试**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj`
预期：全绿。**若 `MonsterCombatIntegrationTests` 红**：把它的 `resolver.DrainResolved(t)` 改为 `resolver.DrainResolved(t)`（不变）但断言间距的那条 `t - lastCastTick >= 15` 依然成立——间距由决策 tick 决定，与判定无关。若红在别处，先查 `Decide` 的 tick 参数是不是从 `t` 变成了别的值。

- [ ] **步骤 7：Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Server3C.cs Server/Server.Tests/Combat/CastResolutionSchedulingTests.cs
git commit -m "feat(combat): the judgment happens on the hit frame, not on keypress"
```

---

## 任务 5（规格 N4）：怪物前摇（站定 + 转向 + 锁住行为树）

**文件：**
- 修改：`Server/Monster/MonsterState.cs`、`Server/Monster/SimpleMoveSystem.cs`、`Server/Monster/MonsterBtActions.cs`、`Server/Monster/MonsterModule.cs`
- 测试：`Server/Server.Tests/Monster/MonsterModuleTests.cs`

- [ ] **步骤 1：写失败测试**

新建 `Server/Server.Tests/Monster/MonsterWindupTests.cs`：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>N4：前摇 = 承诺（D-10）——决策后站定、朝目标转向、不受距离变化影响；
/// 目标跑出感知半径也不中途改走（F12 的两条缺口）。</summary>
public class MonsterWindupTests
{
    private static (EntityStore Store, MonsterModule Module) Create()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        return (store, module);
    }

    private static void LoginPlayer(EntityStore store, long id, Vector3 position)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.IsLoggedIn = true;
        p.UpdatePosition(position, 0f, 0f, 1L, 0U);
    }

    [Fact]
    public void Windup_Locks_The_Tree_When_The_Target_Leaves_Range()
    {
        var (store, module) = Create();
        // Wolf（模板 2）攻击距离 2.0m，冷却 15 tick——生成在 1.5m 处且面向原点
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        var wolf = store.GetPlayer(wolfId)!;
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));
        var player = store.GetPlayer(1)!;

        uint decideTick = 0;
        for (uint t = 0; t < 10 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0) decideTick = t;
        }
        Assert.NotEqual(0u, decideTick);                                  // 前置：怪物确实出手了
        var state = module.GetState(wolfId)!;
        Assert.True(state.AttackResolveTick.HasValue);                    // 前摇已开始
        var positionAtDecision = wolf.Position;

        player.UpdatePosition(new Vector3(0f, 0f, 6f), 0f, 0f, 1L, 0U); // 目标跑出攻击距离
        for (uint t = decideTick + 1; t <= decideTick + 3; t++)
        {
            module.Tick(t);
            Assert.Empty(module.DrainPendingCasts());                     // 前摇期不重复入队
        }

        Assert.Equal(positionAtDecision.X, wolf.Position.X, 4);           // 站定（不 Chase）
        Assert.Equal(positionAtDecision.Z, wolf.Position.Z, 4);
        Assert.Equal((byte)NpcAnimationState.Attack, wolf.ActionState);   // 仍是攻击态（客户端忙位靠它）
    }

    [Fact]
    public void Windup_Turns_Toward_The_Target_Slowly_Not_Instantly()
    {
        var (store, module) = Create();
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        var wolf = store.GetPlayer(wolfId)!;
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));

        uint decideTick = 0;
        for (uint t = 0; t < 10 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0) decideTick = t;
        }
        var state = module.GetState(wolfId)!;
        float before = wolf.Rotation;          // 决策那一 tick 的朝向（面向原点 = 180°）

        // 目标移到怪物前方 45° 处——**必须留在 120° 感知锥内**，否则目标丢失、
        // 前摇分支只能朝陈旧的 LastKnownTargetPosition 转（那才是本用例要排除的干扰）。
        // 从 (0,0,1.5) 朝 -Z 偏 45°：(-0.707, -0.707) × 1.5 ≈ (-1.06, 0, 0.44)
        store.GetPlayer(1)!.UpdatePosition(new Vector3(-1.06f, 0f, 0.44f), 0f, 0f, 1L, 0U);
        module.Tick(decideTick + 1);
        float afterOneTick = wolf.Rotation;

        Assert.True(state.AttackResolveTick.HasValue);                    // 仍在用前摇转向
        Assert.True(YawDeltaDegrees(afterOneTick, before) > 1f,
            $"前摇期没有转向：before={before} after={afterOneTick}");
        Assert.True(YawDeltaDegrees(afterOneTick, before) < 20f,
            $"一步转得太多（不像 90°/s 的渐进转向，倒像瞬转）：delta={YawDeltaDegrees(afterOneTick, before)}");
    }

    /// <summary>最短角差（度）——yaw 会环绕：180° 附近转一点就报成 -174°，
    /// 直接相减会得到 354 而不是 6。</summary>
    private static float YawDeltaDegrees(float a, float b)
    {
        float d = (a - b) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return System.Math.Abs(d);
    }

    [Fact]
    public void Windup_Expires_And_Unlocks_After_HitDelayTicks()
    {
        var (store, module) = Create();
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));

        uint decideTick = 0;
        for (uint t = 0; t < 10 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0) decideTick = t;
        }
        Assert.True(CombatRules.TryGet(50002, out var wolfSkill));
        uint resolveTick = decideTick + CombatRules.HitDelayTicks(wolfSkill);
        var state = module.GetState(wolfId)!;

        module.Tick(resolveTick);

        Assert.False(state.AttackResolveTick.HasValue);                   // 到期即解锁
    }
}
```

- [ ] **步骤 2：运行验证失败**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MonsterWindupTests"`
预期：**编译失败**——`MonsterState` 没有 `AttackResolveTick`

- [ ] **步骤 3：实现（四个文件）**

`MonsterState.cs`（放在 `HitStartTick` 上方，同属"攻击/受击窗口"）：

```csharp
        /// <summary>前摇到期 tick（决策 tick + CombatRules.HitDelayTicks）——HasValue 表示
        /// **前摇中**：站定、朝目标转向、锁住行为树（D-10 前摇 = 承诺）。由模块在到期 tick 自清理。</summary>
        public uint? AttackResolveTick { get; set; }
```

`SimpleMoveSystem.cs`（加在 `HasReached` 之后；角速度由调用方给，移动态仍用 180）：

```csharp
        /// <summary>前摇期原地转向：改朝向、不动位置。复用移动的转身数学（同一套 Slerp/角差）。</summary>
        public void TurnTowards(MonsterState m, Vector3 targetPosition, float deltaTime, float degreesPerSecond)
        {
            Vector3 direction = targetPosition - m.Position;
            direction.Y = 0;
            if (direction.X * direction.X + direction.Z * direction.Z < 0.001f) return; // 零向量：保持当前朝向
            direction = direction.Normalize();
            m.Rotation = RotateTowards(m.Rotation, Quaternion.LookRotation(direction), degreesPerSecond * deltaTime);
        }
```

`MonsterBtActions.cs` —— `Attack` 顶部插前摇分支，末尾记到期 tick。**新增常量**放在类顶部：

```csharp
        /// <summary>前摇期转身速度（度/秒）——**比移动态 180 慢**（D-7 人类裁定）：
        /// 把冲刺绕圈的半径门槛从 1.2m 放宽到 1.75m，绕背才是可执行的操作。</summary>
        public const float WindupTurnSpeedDegPerSecond = 90f;
```

```csharp
        public static BtStatus Attack(MonsterTickContext ctx)
        {
            var m = ctx.State;
            var entity = ctx.Entity;

            // 前摇 = 承诺（D-10）：站定 + 朝目标转向，不受距离/目标丢失影响；
            // 只有死亡能取消（死亡后实体被移除/IsAlive=false，判定阶段自然丢弃这一刀）。
            if (m.AttackResolveTick.HasValue)
            {
                m.Moving = false;
                if (m.LastKnownTargetPosition.HasValue)
                    ctx.Mover.TurnTowards(m, m.LastKnownTargetPosition.Value, ctx.DeltaTime, WindupTurnSpeedDegPerSecond);
                return BtStatus.Running;
            }

            // 目标丢失（死亡/离场）→ 失败回 Chase/Return
            if (!m.TargetId.HasValue) return BtStatus.Failure;

            // 目标移出攻击距离 → 失败 → 序列重头 Chase
            Vector3 target = m.LastKnownTargetPosition ?? m.Position;
            float dx = m.Position.X - target.X, dz = m.Position.Z - target.Z;
            if (dx * dx + dz * dz > m.Config.attackRange * m.Config.attackRange) return BtStatus.Failure;

            m.Moving = false;

            // 冷却门（tick 基准——与 P4 玩家同机制；冷却在**决策**时记录，D-3）
            // 表缺失校验已前移 SpawnMonster（fail fast——避免在攻击距离静默冻结）；此处为防御分支，正常不可达
            if (!CombatRules.TryGet(m.AttackSkillId, out var entry)) return BtStatus.Failure;
            if (!entity.IsCastReady(m.AttackSkillId, ctx.Tick, CombatRules.CooldownTicks(entry)))
                return BtStatus.Running;

            ctx.Module.QueueCast(m.NpcId, new CastRequest
            {
                SkillId = m.AttackSkillId,
                TargetPlayerId = m.TargetId.Value, // 显式仇恨目标（HitResolver 按 id + 距离 + 锥角裁决，D-P5-4/D-6）
                Shape = new HitShape
                {
                    ShapeType = 0, // Single——范围 = 配置 attackRange（须 ≤ 表上限——ValidateShape 校验）
                    Range = m.Config.attackRange,
                    Angle = 120f
                }
            });
            m.AttackResolveTick = ctx.Tick + CombatRules.HitDelayTicks(entry); // 前摇到期（判定帧）
            entity.ActionState = (byte)NpcAnimationState.Attack;
            return BtStatus.Running; // 保持攻击态；下一 tick 前摇分支再判
        }
```

`MonsterModule.cs` 两处：

1) `Tick` 里在 BT 之前清理到期前摇（紧接 `DetectHitOrDeath(m, entity, tick);` 之后、`bool deadNow = ...` 之前）：

```csharp
                // 前摇到期 → 解锁（D-10：到期即恢复行为树；本 tick 的判定由 Server3C 的
                // DrainResolved 负责，二者互不依赖——解锁只影响"怪物下一步做什么"）
                if (m.AttackResolveTick.HasValue && tick >= m.AttackResolveTick.Value)
                    m.AttackResolveTick = null;
```

2) `BuildBehaviorTree` 的 Hostile 条件放宽（F12）：

```csharp
                new BtSequence(
                    // 前摇期允许目标丢失（F12）：否则"目标跑出感知半径"会让怪挥到一半改走回家
                    new BtCondition(m => m.AlertLevel == AlertLevel.HOSTILE
                                         && (m.TargetId.HasValue || m.AttackResolveTick.HasValue)),
                    new BtAction(MonsterBtActions.Chase),
                    new BtAction(MonsterBtActions.Attack)
                ),
```

- [ ] **步骤 4：运行验证通过**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MonsterWindupTests"`
预期：PASS（三条）。若 `Windup_Turns_...` 红：确认 `LastKnownTargetPosition` 在目标移出**感知**后仍保留（本用例目标在 3m 处、感知 15m 内，应保留）。

- [ ] **步骤 5：跑全量服务端测试（找回归）**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj`
预期：全绿。**重点关注**：`MonsterModuleTests.Hit_Window_Expiry_...`（受击窗口 + 前摇锁的交互）、`Detection_Aggro_And_Attack_Queue_Targets_Closest_Player`（200 tick 内仍能追到 2.5m）、`MonsterCombatIntegrationTests`（施法间隔 ≥15 tick）

- [ ] **步骤 6：Commit**

```bash
git add Server/Monster/ Server/Server.Tests/Monster/MonsterWindupTests.cs
git commit -m "feat(combat): monsters telegraph their swings and can be circled"
```

---

## 任务 6（规格 N5）：守护用例 + 全量回归

**文件：**
- 测试：`Server/Server.Tests/Monster/MonsterModuleTests.cs`、`Server/Server.Tests/Combat/CastResolutionSchedulingTests.cs`

- [ ] **步骤 1：忙位不变式守护用例（F19）**

追加到 `Server/Server.Tests/Monster/MonsterModuleTests.cs`：

```csharp
    /// <summary>F19：客户端 `NpcEntityAnimator` 的忙位解除**依赖**"决策与置 ActionState=Attack
    /// 发生在同一次 BT 动作内"（否则挥砍被锁定在 Attack 态不释放）。这条不变量此前只活在注释里——
    /// 本用例把它钉成可证伪的断言。</summary>
    [Fact]
    public void Attack_Decision_And_Action_State_Happen_In_The_Same_Tick()
    {
        var (store, module) = Create();
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));
        var wolf = store.GetPlayer(MonsterModule.NpcIdSeed + 1)!;

        uint decideTick = 0;
        for (uint t = 0; t < 200 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0)
            {
                decideTick = t;
                // 同一次 BT 动作的产物：入队的那一 tick 就必须已经是 Attack 态
                Assert.Equal((byte)NpcAnimationState.Attack, wolf.ActionState);
            }
        }
        Assert.NotEqual(0u, decideTick);
        Assert.True(module.GetState(MonsterModule.NpcIdSeed + 1)!.AttackResolveTick.HasValue);
    }
```

- [ ] **步骤 2：`decideTick` 口径守护用例（F17）**

追加到 `CastResolutionSchedulingTests`：

```csharp
    [Fact]
    public void Packet_Path_Decides_On_The_Tick_That_Is_About_To_Run()
    {
        // F17：包处理发生在 tick 处理之前，`CurrentTick` 此刻仍是上一 tick 的值——
        // CastHandler.HandleCastAsync 因此传 CurrentTick + 1。本用例钉住这个换算口径本身：
        // 决策 tick = T 时，判定发生在 T + HitDelayTicks（早一 tick 或晚一 tick 都是缺陷）。
        var (store, resolver, caster, target) = World(1.5f);
        const uint decideTick = 37;
        resolver.Decide(caster, Swing(), decideTick);

        Assert.Empty(resolver.DrainResolved(decideTick + CombatRules.HitDelayTicks(EntryOf(10001)) - 1));
        var one = Assert.Single(resolver.DrainResolved(decideTick + CombatRules.HitDelayTicks(EntryOf(10001))));
        Assert.Single(one.Hits);
    }
```

- [ ] **步骤 3：运行验证通过**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MonsterModuleTests|FullyQualifiedName~CastResolutionSchedulingTests"`
预期：PASS

- [ ] **步骤 4：全量回归（三套件）**

- [ ] 服务端：`dotnet test Server/Server.Tests/Server.Tests.csproj` → 全绿，通过数 = 基线 + 2(N1a) + 3(N1b) + 4(N3) + 3(N4) + 2(N5) = **基线 + 14**
- [ ] Unity EditMode：`assets-refresh` → `tests-run`（EditMode）→ 全绿，通过数 = 基线 + 2(N1a) + 3(N1b)
- [ ] Unity PlayMode：`tests-run`（PlayMode）→ 与基线一致（本设计不动客户端行为；若有波动先查是否既有 flaky，见 `docs/superpowers/plans/` 里 PlayMode 基线的记法）

- [ ] **步骤 5：Commit**

```bash
git add Server/Server.Tests/
git commit -m "test(combat): pin the busy-flag invariant and the decide-tick convention"
```

---

## 任务 7（规格 N6）：手动端到端验收（唯一能证明"对齐"成立的验收）

**无文件改动**（只做验收，问题回填到对应任务）。

- [ ] **步骤 1：起服 + 起客户端**

服务端：`dotnet run --project Server/Server.csproj`（观察日志里的 `[Combat] cast rejected` 计数与 `[Aoi]` 统计）
客户端：Unity 打开 `DemoDay` 场景进 PlayMode（或 WebGL 出包按项目既有 e2e 装置跑）

- [ ] **步骤 2：逐条确认 §1 的四条成功判据**

1. **怪挥砍命中帧与受击飘字同帧**：站进 Wolf 的攻击距离挨一刀——刀刃到肉的瞬间出飘字（残差 ≈132ms 一轮渲染延迟；若明显早于或晚于刀刃，按 R3 复核 50002 的 `HitDelaySeconds` 占位值）
2. **玩家挥砍：伤害落在本地判定帧上**：砍 Slime——伤害数字出现时本地挥砍动画的判定帧刚好过去（BA_Heavy 0.35s 的一刀最能看出）
3. **贴脸冲刺绕圈 → 空挥**：贴到怪物 1.5m 内按冲刺绕圈，至少能绕出一次 miss（观察狼的挥砍打空、自己不掉血）；正面站定 → 必中
4. **前摇期打死怪 → 无伤害**：在怪抬手（`AttackResolveTick` 窗口）内把它打死——自己不掉血

- [ ] **步骤 3：记录实测结果**

把四条判据的实测结论（含各自残差的目测量级、绕背成功率）写进 `docs/superpowers/plans/2026-10-16-hit-judgment-timing.md` 的"验收记录"小节或 `production/verify/` 下（按项目既有惯例）：

```markdown
## 验收记录（2026-10-XX 实测）

| 判据 | 结果 | 备注（残差目测 / 绕背成功率 / 待调表值） |
|---|---|---|
| 怪挥砍 ↔ 飘字同帧 | | |
| 玩家挥砍 ↔ 本地判定帧 | | |
| 躲怪：跑出射程 | | |
| 躲怪：贴脸冲刺绕背 | | |
| 打断：前摇期打死怪 | | |
```

- [ ] **步骤 4：Commit 验收记录**

```bash
git add production/verify/ docs/superpowers/plans/2026-10-16-hit-judgment-timing.md
git commit -m "docs(combat): record the hit-frame alignment acceptance run"
```

---

## 自检

**规格覆盖度**（逐条对回 §0 事实与 §9 决策）：

| 规格要求 | 落点 |
|---|---|
| 逐技能判定延迟（F4、D-2） | N1a |
| 显式目标锥角（F9、D-6） | N1b |
| `decideTick` 口径（F17、D-12） | N3 步骤 4 + N5 步骤 2 |
| 冷却起点 = 决策（D-3） | N2 步骤 3（`Decide` 内 `RecordCast`） |
| 死亡取消、受击不取消（D-4） | N2 步骤 3（`ResolveHits` 复查）+ N3 步骤 1 用例 |
| 广播提前到决策（F7、D-5） | N3 步骤 4（`BroadcastSwingAsync`） |
| tick 顺序 `DrainResolved` 早于 `TickRespawns`（F13） | N3 步骤 5 |
| 零延迟向后兼容（D-9） | N3 步骤 1 用例 `Zero_Delay_Skill_Resolves_In_The_Same_Tick` |
| 前摇 = 承诺（D-10、F12） | N4 全部三条用例 |
| 队列三约束（D-13） | N2 步骤 3（单遍压缩 + 定序） |
| 客户端零逻辑改动（F15、D-11/D-14） | 无客户端任务；仅两处 NUnit 镜像 |
| 怪延迟实测（R3、F20） | N1a 注释标注占位 + N6 步骤 2 第 1 条复核 |
| 忙位不变式（F19） | N5 步骤 1 |
| 三套件回归 + 手动验收（§1） | N5 步骤 4 + N6 |

**占位符扫描**：无"待定/TODO"；唯一显式的占位是**表值 0.35s**，它是规格 D-8 的人类裁定且带来源注释与复核步骤（N6 步骤 2），不是计划缺陷。

**类型一致性**：`CastResolution`（含新字段 `IsNpcCaster`）、`PendingCast`、`CombatResolver.Decide/DrainResolved/ResolveHits`、`CastHandler.Decide/DrainResolved/BroadcastSwingAsync/BroadcastDamageAsync`、`SkillEntry.HitDelaySeconds`、`CombatRules.HitDelayTicks`、`MonsterState.AttackResolveTick`、`SimpleMoveSystem.TurnTowards`、`MonsterBtActions.WindupTurnSpeedDegPerSecond` —— 各任务只使用前面任务已定义的名字，无同物异名。

**已知覆盖缺口（如实登记，不假装有覆盖）**：规格 §7 写作"`CastHandlerTests` + `MonsterCombatIntegrationTests`：广播的两个时刻、tick 顺序"——**这两个文件不具备该能力**：它们直测 `CombatResolver`，而 `Server3C` / `CastHandler` 没有 session 级测试装置（`Server.Tests/Server/` 下只有 GameLoop / Session / AOI / 时钟等单点用例，无一构造 `Server3C` 并驱动其 tick）。因此：

| 缺口 | 兜底 |
|---|---|
| 挥砍广播在决策 tick、伤害广播在判定 tick | `CastResolutionSchedulingTests` 钉住**判定**的时机 + `BroadcastSwingAsync`/`BroadcastDamageAsync` 的调用点（Server3C 内三行）**代码审查** + N6 步骤 2 实测 |
| `DrainResolved` 早于 `TickRespawns` | 三行顺序的代码审查 + N6 步骤 2 第 4 条（前摇期打死怪 → 无伤害）间接验证 |
| `decideTick = CurrentTick + 1`（包路径） | N5 步骤 2 钉住**换算口径**；`HandleCastAsync` 里那一行 `+1` 本体仍靠审查（无 session 装置就无法驱动包路径） |

要补自动化，得先建 `Server3C`/session 测试装置——那是独立的一项工作，**不在本计划内**（规格 §7 的原表述应在规格里同步更正）。
