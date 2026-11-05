# 大体型怪物近战命中修复 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让玩家普攻在大体型怪物（蜘蛛 Boss）贴侧身时按身体外缘命中，并让客户端锁定在贴脸时不丢锁。

**架构：** 在双端共享的 `HitResolver` 中把锥形方向判定从「目标中心点」改为「圆 vs 锥精确相交」（目标半径参与角度）；客户端 `NetworkTargetSelector` 复用同一套表面距离 + 目标半径角扩展，半径由 `NpcBodyCollider` 的模型包围盒推导。半径 0（玩家）走原快路径，PvP 逐值不变。

**技术栈：** Unity 2022.3 / C# / xUnit（服务端测试）/ NUnit（Unity EditMode 测试）

---

## 文件结构

| 文件 | 职责 | 本次改动 |
|---|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs` | 双端共享的权威命中几何 | `InCone` 增加目标半径角扩展；`Resolve` 两条 Single 路径传入半径 |
| `Server/Server.Tests/SyncCore/HitResolverTests.cs` | 服务端几何单测 | 新增 5 条侧身/背后/显式目标用例 |
| `Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs` | Unity 侧同源几何单测 | 同步新增 5 条用例（NUnit 断言） |
| `Server/Server.Tests/Combat/CombatResolverNpcTests.cs` | 服务端完整判定链集成测试 | 新增侧面大 NPC 命中 / 半径 0 不中两条 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcBodyCollider.cs` | 怪物视图运行时身体碰撞体 | 暴露 `BodyRadius`（模型 XZ 较大半宽） |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | 远程视图创建与元数据 | 视图回填 `BodyCollider` / `BodyRadius` |
| `Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs` | 身体碰撞体资产/尺寸测试 | 新增 `BodyRadius` 断言 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs` | 联网目标锁定 | 候选半径、表面距离、角扩展、按表面距离排序 |
| `Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs` | 锁定纯函数与 `EnsureTargetFor` | 新增半径筛选、排序、贴脸保锁用例 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md` | SyncCore 模块说明 | 命中几何补角扩展 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md` | SyncLayer 模块说明 | 锁定改表面距离；`NpcBodyCollider.BodyRadius` |
| `Server/Monster/README.md` | 服务端怪物说明 | 命中条目补锥形扩展 |

任务依赖：任务 1 独立；任务 2 独立；任务 3 依赖任务 2（需要 `view.BodyRadius`）；任务 4 依赖前三者。

---

## 任务 1：服务端体表锥形命中（共享几何 + 集成）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs:43-102`（`Resolve` 与 `InCone`）
- 修改：`Server/Server.Tests/SyncCore/HitResolverTests.cs`（文件末尾追加 5 条 `[Fact]`）
- 修改：`Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs`（文件末尾追加 5 条 `[Test]`）
- 修改：`Server/Server.Tests/Combat/CombatResolverNpcTests.cs`（`Player_Melee_Misses_Same_Distance_Without_Radius` 之后追加 2 条）
- 测试：上述三个测试文件

- [ ] **步骤 1：在服务端 `HitResolverTests.cs` 写入失败的几何测试**

在 `Circle_Hit_Expands_By_Target_Radius` 方法之后、类结束大括号之前追加：

```csharp
    [Fact]
    public void Side_Target_With_Radius_Is_Hit_By_Angular_Reach()
    {
        // 中心距 2.16、半径 1.74 → 表面 0.42 ≤ 2.0；
        // 中心方向 90°，半锥 60° + asin(1.74/2.16) ≈ 53.7° → 命中
        var candidates = new List<CombatCandidate> { C(2, 2.16f, 0f, radius: 1.74f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Side_Target_Without_Radius_Is_Missed()
    {
        // 同一中心距、半径 0：射程与锥角都不满足
        var candidates = new List<CombatCandidate> { C(2, 2.16f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Side_Target_In_Range_Without_Radius_Is_Missed()
    {
        // 表面距 1.5 ≤ 2.0，仅中心角 90° > 60° 被拒——隔离角判定
        var candidates = new List<CombatCandidate> { C(2, 1.5f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Target_Directly_Behind_Is_Missed_Even_With_Radius()
    {
        // 正后方 180°：角扩展上限（60° + 90° = 150°）仍不足以命中
        var candidates = new List<CombatCandidate> { C(2, 0f, -2.16f, radius: 1.74f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Caster_Inside_Target_Body_Hits()
    {
        // 中心距 1.0 < 半径 1.74，且中心在侧面 90°：施法者已在身体内，角度无定义时取命中
        var candidates = new List<CombatCandidate> { C(2, 1f, 0f, radius: 1.74f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Explicit_Target_Uses_Angular_Reach()
    {
        var withRadius = new List<CombatCandidate> { C(2, 2.16f, 0f, radius: 1.74f) };
        Assert.Equal(new long[] { 2 },
            HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, withRadius));

        var noRadius = new List<CombatCandidate> { C(2, 1.5f, 0f) };
        Assert.Empty(HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, noRadius));
    }
```

- [ ] **步骤 2：在 Unity `HitResolverTests.cs` 写入同源的 5 条测试**

在 `Circle_Hit_Expands_By_Target_Radius` 方法之后、类结束大括号之前追加（NUnit 断言）：

```csharp
        [Test]
        public void Side_Target_With_Radius_Is_Hit_By_Angular_Reach()
        {
            var candidates = new List<CombatCandidate> { C(2, 2.16f, 0f, radius: 1.74f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Side_Target_Without_Radius_Is_Missed()
        {
            var candidates = new List<CombatCandidate> { C(2, 2.16f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void Side_Target_In_Range_Without_Radius_Is_Missed()
        {
            var candidates = new List<CombatCandidate> { C(2, 1.5f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void Target_Directly_Behind_Is_Missed_Even_With_Radius()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, -2.16f, radius: 1.74f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void Caster_Inside_Target_Body_Hits()
        {
            var candidates = new List<CombatCandidate> { C(2, 1f, 0f, radius: 1.74f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single20, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Explicit_Target_Uses_Angular_Reach()
        {
            var withRadius = new List<CombatCandidate> { C(2, 2.16f, 0f, radius: 1.74f) };
            Assert.That(HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, withRadius),
                Is.EqualTo(new long[] { 2 }));

            var noRadius = new List<CombatCandidate> { C(2, 1.5f, 0f) };
            Assert.That(HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, noRadius),
                Is.Empty);
        }
```

- [ ] **步骤 3：在 `CombatResolverNpcTests.cs` 写入集成测试**

在 `Player_Melee_Misses_Same_Distance_Without_Radius` 方法之后追加：

```csharp
    [Fact]
    public void Player_Melee_Hits_Large_Npc_From_Side()
    {
        var store = new EntityStore();
        long player = 1;
        long npc = MonsterModule.NpcIdSeed + 1;
        store.AddPlayer(player, "P");
        store.GetPlayer(player)!.IsLoggedIn = true;
        // 玩家在原点朝 +Z；NPC 在 +X 侧 90°、中心距 2.16，半径 1.74
        store.AddNpc(npc, $"Spider#{npc}", 4, 1500f, new Vector3(2.16f, 0f, 0f), 1.74f);
        store.GetPlayer(npc)!.Rotation = 180f;
        var resolver = new CombatResolver(store, new Random(42));

        var result = Resolve(resolver, player, PlayerSwing(npc), 10);

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(npc, hit.TargetPlayerId);
        Assert.Equal(1500f - hit.Damage, store.GetPlayer(npc)!.Hp);
    }

    [Fact]
    public void Player_Melee_Misses_Side_Npc_Without_Radius_In_Range()
    {
        var store = new EntityStore();
        long player = 1;
        long npc = MonsterModule.NpcIdSeed + 1;
        store.AddPlayer(player, "P");
        store.GetPlayer(player)!.IsLoggedIn = true;
        // 表面距 1.5 ≤ 2.0，但半径 0、中心角 90° > 60° → 仅角判定拒绝
        store.AddNpc(npc, $"Side#{npc}", 4, 100f, new Vector3(1.5f, 0f, 0f));
        var resolver = new CombatResolver(store, new Random(42));

        var result = Resolve(resolver, player, PlayerSwing(npc), 10);

        Assert.True(result.Accepted);
        Assert.Empty(result.Hits);
        Assert.Equal(100f, store.GetPlayer(npc)!.Hp);
    }
```

- [ ] **步骤 4：运行测试确认失败**

运行：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HitResolver"
```

预期：`Side_Target_With_Radius_Is_Hit_By_Angular_Reach`、`Caster_Inside_Target_Body_Hits` 与
`Explicit_Target_Uses_Angular_Reach` **FAIL**（当前锥形只认中心方向，90° > 60°）；
另外三条断言「不中」的用例保持 PASS。

运行：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolverNpcTests"
```

预期：`Player_Melee_Hits_Large_Npc_From_Side` **FAIL**；`Player_Melee_Misses_Side_Npc_Without_Radius_In_Range` PASS。

- [ ] **步骤 5：修改 `HitResolver` 的锥形判定**

在 `Resolve` 中，`halfCos` 计算之后追加半角弧度：

```csharp
            float halfCos = (float)Math.Cos(shape.Angle * 0.5f * Math.PI / 180.0);
            float halfAngleRad = (float)(shape.Angle * 0.5 * Math.PI / 180.0);
```

把显式目标路径的：

```csharp
                    if (!InCone(casterPos, forwardX, forwardZ, halfCos, c.Position)) continue;
```

改为：

```csharp
                    if (!InCone(casterPos, forwardX, forwardZ, halfCos, halfAngleRad,
                            c.Position, c.Radius)) continue;
```

把自动目标路径的同一行也改为上面的形式。

用以下实现替换 `InCone` 整个方法：

```csharp
        /// <summary>前方 ±Angle/2 锥内（含边界）；零距离退化视为锥内（D-P4-2a——双端同出生点）。
        /// 目标半径 > 0 时按圆 vs 锥精确相交扩展角：中心角 ≤ 半锥角 + asin(半径/中心距)；
        /// 半径 ≤ 0 / NaN 时保持中心点快路径（PvP 逐值不变）；
        /// 施法者已在目标圆内（中心距 ≤ 半径）视为命中。</summary>
        private static bool InCone(Vec3 casterPos, float forwardX, float forwardZ,
            float halfCos, float halfAngleRad, Vec3 target, float targetRadius)
        {
            float dx = target.X - casterPos.X;
            float dz = target.Z - casterPos.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 0.0001f) return true;

            float dot = forwardX * dx + forwardZ * dz;
            if (!(targetRadius > 0f)) return dot / dist >= halfCos;
            if (dist <= targetRadius) return true;

            float angle = (float)Math.Atan2(Math.Abs(forwardX * dz - forwardZ * dx), dot);
            float alpha = (float)Math.Asin(Math.Min(1f, targetRadius / dist));
            return angle <= halfAngleRad + alpha;
        }
```

在 `HitResolver` 类注释（`/// <summary>` 段）追加一行：

```text
    /// 锥形：目标半径 > 0 时按圆 vs 锥精确相交（中心角 ≤ 半锥角 + asin(半径/中心距)）；
    /// 半径 0 / NaN 走中心点快路径，PvP 逐值不变。
```

- [ ] **步骤 6：运行服务端测试确认通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HitResolver"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolverNpcTests"
```

预期：两条命令全部 PASS；既有半径 0 用例（锥边界、零距离、背后、PvP）不回归。

- [ ] **步骤 7：运行 Unity 同源测试确认通过**

```bash
bash tools/unity-tests.sh EditMode HitResolverTests
```

预期：PASS（共享文件同一份实现；Unity 测试副本只换了断言语法）。

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs \
        Server/Server.Tests/SyncCore/HitResolverTests.cs \
        Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs \
        Server/Server.Tests/Combat/CombatResolverNpcTests.cs
git commit -m "fix(combat): widen cone by target radius for large monsters"
```

---

## 任务 2：客户端身体半径（NpcBodyCollider + RemoteEntityView）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcBodyCollider.cs:39-86`（`Collider` 与 `BuildCollider`）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs:627-655`（`BuildView`）与 `:744-824`（`RemoteEntityView`）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs`

- [ ] **步骤 1：在 `NpcBodyColliderTests.cs` 写入失败的断言**

在 `Monster_View_Gets_A_Body_Collider_Sized_From_The_Model` 的
`Assert.AreSame(view.GameObject.transform, body.transform.parent);` 之后追加：

```csharp
                Assert.AreEqual(1f, bodyCollider.BodyRadius, 1e-4f,
                    "目标选择半径 = max(2,1)/2 = 1");
                Assert.AreEqual(1f, view.BodyRadius, 1e-4f,
                    "RemoteEntityView 应暴露同一半径");
                Assert.AreSame(bodyCollider, view.BodyCollider,
                    "RemoteEntityView 应回填运行时身体碰撞体");
```

在 `Body_Collider_Is_Sized_From_Each_Real_Monster_Prefab` 的三条 `box.size` 断言之后追加：

```csharp
                    var expectedRadius = Mathf.Max(world.size.x, world.size.z) * 0.5f;
                    Assert.AreEqual(expectedRadius, bodyCollider.BodyRadius, 1e-2f,
                        path + "：目标选择半径 = SMR 世界包围盒 XZ 较大半宽");
```

- [ ] **步骤 2：运行测试确认编译失败**

```bash
bash tools/unity-tests.sh EditMode NpcBodyColliderTests
```

预期：编译失败，提示 `NpcBodyCollider` 没有 `BodyRadius`、`RemoteEntityView` 没有
`BodyRadius` / `BodyCollider`。

- [ ] **步骤 3：给 `NpcBodyCollider` 增加 `BodyRadius`**

在 `public BoxCollider? Collider { get; private set; }` 之后追加：

```csharp
        /// <summary>建出来的身体碰撞体的 XZ 半径（米；未建成/无渲染器时为 0）——目标选择用。
        /// 取盒体长边的半长（世界空间），与服务端 `bodyRadius` 同口径。</summary>
        public float BodyRadius { get; private set; }
```

在 `BuildCollider` 的退化检查之后、创建 `NpcBody` 子物体之前追加：

```csharp
            if (localSize.x < 0.01f && localSize.y < 0.01f && localSize.z < 0.01f) return; // 未蒙皮的退化 bounds

            // 目标选择半径：盒体 XZ 长边的半长（world 是合并后的世界包围盒）
            BodyRadius = Mathf.Max(world.size.x, world.size.z) * 0.5f;
```

- [ ] **步骤 4：在 `RemoteEntityView` 暴露碰撞体并回填**

在 `RemoteEntityView` 的 `NpcAnimator` 属性之后追加：

```csharp
        /// <summary>怪物视图的运行时身体碰撞体（玩家/球体占位为 null）</summary>
        public NpcBodyCollider? BodyCollider { get; internal set; }
        /// <summary>目标选择用的 XZ 身体半径（米；玩家/占位为 0）</summary>
        public float BodyRadius => BodyCollider != null ? BodyCollider.BodyRadius : 0f;
```

把 `BuildView` NPC 分支的：

```csharp
                    var driver = model.AddComponent<NpcEntityAnimator>();
                    // 纯客户端身体碰撞体（2026-10-13）：让本地玩家撞不过去（Boss 不再能穿过）。
                    // 只挡本地玩家——层矩阵/查询排查见 NpcBodyCollider 的类注释与验收报告。
                    model.AddComponent<NpcBodyCollider>();
                    return new RemoteEntityView(model, snapshot.Kind, snapshot.TemplateId, npcAnimator: driver);
```

改为：

```csharp
                    var driver = model.AddComponent<NpcEntityAnimator>();
                    // 纯客户端身体碰撞体（2026-10-13）：让本地玩家撞不过去（Boss 不再能穿过）。
                    // 只挡本地玩家——层矩阵/查询排查见 NpcBodyCollider 的类注释与验收报告。
                    var body = model.AddComponent<NpcBodyCollider>();
                    var view = new RemoteEntityView(model, snapshot.Kind, snapshot.TemplateId, npcAnimator: driver);
                    view.BodyCollider = body;
                    return view;
```

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode NpcBodyColliderTests
```

预期：PASS；假模型 `(2,3,1)` 的 `BodyRadius == 1.0`；真蜘蛛/骑士的半径等于独立推导的
`max(world.size.x, world.size.z) * 0.5`。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcBodyCollider.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs
git commit -m "feat(sync): expose npc body radius on remote views"
```

---

## 任务 3：客户端锁定体表距离（NetworkTargetSelector）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs:31-45`（`Candidate`）、`:170-205`（`FilterCandidates`）、`:222-259`（`BuildCandidates` / `IsWithinSkillShape`）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs`

- [ ] **步骤 1：写入失败的纯函数测试**

在 `Filter_Orders_By_Planar_Distance` 方法之后追加：

```csharp
        [Test]
        public void Filter_Includes_Large_Npc_By_Surface_Range_And_Angular_Reach()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(2L, new Vector3(2.2f, 0f, 0f),
                    (byte)EntityKind.Npc, dead: false, radius: 1.74f),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 2f, halfAngleDeg: 60f);

            Assert.AreEqual(new[] { 2L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "表面距 0.46 ≤ 2；中心角 90° ≤ 60° + asin(1.74/2.2) ≈ 112°");
        }

        [Test]
        public void Filter_Excludes_Small_Target_At_Same_Side_Angle()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(2L, new Vector3(1.5f, 0f, 0f),
                    (byte)EntityKind.Player, dead: false), // 半径 0
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 2f, halfAngleDeg: 60f);

            Assert.AreEqual(0, result.Count, "半径 0 时中心角 90° > 60°，必须排除");
        }

        [Test]
        public void Filter_Includes_Zero_Distance_Candidate()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(2L, Vector3.zero,
                    (byte)EntityKind.Npc, dead: false, radius: 1.74f),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 2f, halfAngleDeg: 60f);

            Assert.AreEqual(new[] { 2L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "零距离退化：角度无意义，直接入选");
        }

        [Test]
        public void Filter_Orders_By_Surface_Distance()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(1L, new Vector3(0f, 0f, 3f),
                    (byte)EntityKind.Npc, dead: false, radius: 1.5f),   // 表面 1.5
                new NetworkTargetSelector.Candidate(2L, new Vector3(0f, 0f, 2f),
                    (byte)EntityKind.Player, dead: false),              // 表面 2.0
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 15f, halfAngleDeg: 90f);

            Assert.AreEqual(new[] { 1L, 2L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "按表面距离升序，而不是中心距");
        }
```

- [ ] **步骤 2：写入失败的 `EnsureTargetFor` 贴脸保锁测试**

在类顶部常量区追加：

```csharp
        private const string SpiderPrefab = "Assets/Monstor/fantasySpider/SpiderMonster.prefab";
```

在 `EnsureTargetFor_Without_Any_Target_In_Skill_Shape_Leaves_No_Lock` 方法之后追加：

```csharp
        [Test]
        public void EnsureTargetFor_Keeps_Lock_On_Large_Npc_At_Melee_Range()
        {
            var spiderPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SpiderPrefab);
            Assert.NotNull(spiderPrefab, SpiderPrefab + " 未找到");

            var root = new GameObject("SpiderSelectorTestRoot");
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { 4, spiderPrefab } });
            var owner = new GameObject("SpiderSelectorOwner");
            NetworkTargetable? bound = null;
            var selector = owner.AddComponent<NetworkTargetSelector>();
            selector.Initialize(registry, owner.transform, null, null,
                target => { bound = target; },
                () => { bound = null; },
                () => LocalId);
            try
            {
                const long npcId = 9_000_001L;
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = npcId,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                    X = 2.2f, Y = 0f, Z = 0f,
                    Kind = (byte)EntityKind.Npc, TemplateId = 4, Tick = 1
                }, serverTicks: ServerTicks);

                var view = registry.GetView(npcId);
                Assert.NotNull(view);
                view!.Position = new Vector3(2.2f, 0f, 0f);
                Assert.NotNull(view.BodyCollider, "真模型视图应补上运行时身体碰撞体");
                view.BodyCollider!.BuildCollider();   // EditMode：AddComponent 不触发生命周期
                Assert.Greater(view.BodyRadius, 1.5f, "蜘蛛的目标选择半径应接近 1.74");

                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath);
                Assert.NotNull(data, ComboSkillPath + " 未找到");
                selector.EnsureTargetFor(data);

                Assert.AreEqual(npcId, selector.LockedTargetId,
                    "侧面 90°、表面距 0.46m 的大体型目标应建立锁定");
                Assert.IsNotNull(bound);
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnsureTargetFor_Does_Not_Lock_Small_Target_At_Side()
        {
            CreateView(2L, new Vector3(1.5f, 0f, 0f));   // 玩家视图半径 0
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath);
            Assert.NotNull(data, ComboSkillPath + " 未找到");

            _selector.EnsureTargetFor(data);

            Assert.AreEqual(0L, _selector.LockedTargetId,
                "半径 0、中心角 90° > 60° 半锥：不得锁定");
        }
```

- [ ] **步骤 3：运行测试确认失败**

```bash
bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests
```

预期：编译失败（`Candidate` 没有 `radius` 参数），或 `Filter_Orders_By_Surface_Distance` /
`EnsureTargetFor_Keeps_Lock_On_Large_Npc_At_Melee_Range` 失败。

- [ ] **步骤 4：给 `Candidate` 增加半径并填充**

把 `Candidate` 结构体改为（新增字段 + 构造参数默认 0）：

```csharp
        public readonly struct Candidate
        {
            public readonly long EntityId;
            public readonly Vector3 Position;
            public readonly byte Kind;
            public readonly bool Dead;
            /// <summary>目标 XZ 身体半径（米；玩家恒 0）——锁定按表面距离与角扩展筛选</summary>
            public readonly float Radius;

            public Candidate(long entityId, Vector3 position, byte kind, bool dead, float radius = 0f)
            {
                EntityId = entityId;
                Position = position;
                Kind = kind;
                Dead = dead;
                Radius = radius;
            }
        }
```

把 `BuildCandidates` 的：

```csharp
                candidates.Add(new Candidate(id, view.Position, view.Kind, view.Dead));
```

改为：

```csharp
                candidates.Add(new Candidate(id, view.Position, view.Kind, view.Dead, view.BodyRadius));
```

- [ ] **步骤 5：改用表面距离 + 角扩展**

用以下实现替换 `FilterCandidates` 整个方法：

```csharp
        public static List<Candidate> FilterCandidates(Vector3 origin, Vector3 forward,
            IReadOnlyList<Candidate> candidates, float acquireRange, float halfAngleDeg)
        {
            var result = new List<Candidate>();
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float clampedHalf = Mathf.Clamp(halfAngleDeg, 0f, 180f);

            foreach (var candidate in candidates)
            {
                if (candidate.Kind != (byte)EntityKind.Player
                    && candidate.Kind != (byte)EntityKind.Npc) continue;
                if (candidate.Dead) continue;

                Vector3 delta = candidate.Position - origin;
                delta.y = 0f;
                float centerDist = delta.magnitude;
                float surfaceDist = Mathf.Max(0f, centerDist - candidate.Radius);
                if (surfaceDist > acquireRange) continue;

                // 已在目标身体内 / 零距离退化：角度无意义，直接入选
                if (centerDist <= 0.0001f || centerDist <= candidate.Radius)
                {
                    result.Add(candidate);
                    continue;
                }

                float alphaDeg = Mathf.Asin(Mathf.Clamp(candidate.Radius / centerDist, 0f, 1f))
                                 * Mathf.Rad2Deg;
                float effectiveHalf = Mathf.Min(180f, clampedHalf + alphaDeg);
                float halfCos = Mathf.Cos(effectiveHalf * Mathf.Deg2Rad);
                if (Vector3.Dot(forward, delta / centerDist) < halfCos) continue;
                result.Add(candidate);
            }

            result.Sort((a, b) => SurfaceDistance(origin, a).CompareTo(SurfaceDistance(origin, b)));
            return result;
        }

        /// <summary>候选到角色的表面距离（XZ 平面；已在体内时为 0）。</summary>
        private static float SurfaceDistance(Vector3 origin, Candidate candidate)
        {
            Vector3 delta = candidate.Position - origin;
            delta.y = 0f;
            return Mathf.Max(0f, delta.magnitude - candidate.Radius);
        }
```

用以下实现替换 `IsWithinSkillShape`：

```csharp
        private bool IsWithinSkillShape(RemoteEntityView view, float range, float halfAngleDeg,
            bool omnidirectional)
        {
            if (range <= 0f || _owner == null) return false;

            Vector3 delta = view.Position - _owner.position;
            delta.y = 0f;
            float centerDist = delta.magnitude;
            float surfaceDist = Mathf.Max(0f, centerDist - view.BodyRadius);
            if (surfaceDist > range) return false;
            if (omnidirectional || centerDist <= 0.0001f || centerDist <= view.BodyRadius) return true;

            Vector3 forward = _owner.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            float alphaDeg = Mathf.Asin(Mathf.Clamp(view.BodyRadius / centerDist, 0f, 1f))
                             * Mathf.Rad2Deg;
            float effectiveHalf = Mathf.Min(180f, Mathf.Clamp(halfAngleDeg, 0f, 180f) + alphaDeg);
            float halfCos = Mathf.Cos(effectiveHalf * Mathf.Deg2Rad);
            return Vector3.Dot(forward, delta / centerDist) >= halfCos;
        }
```

同步更新 `FilterCandidates` 的 XML 注释：把「按水平距离升序」改为
「按表面距离升序（中心距 − 目标半径）」，并在锥形说明中补
「目标半径 > 0 时按 asin(半径/中心距) 扩展半角」。

- [ ] **步骤 6：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests
```

预期：PASS；既有半径 0 / 平面距离排序用例不回归；贴脸大蜘蛛保持锁定。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs
git commit -m "fix(sync): keep target lock on large npcs at melee range"
```

---

## 任务 4：文档同步与全量验证

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md:11`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md:86-94`
- 修改：`Server/Monster/README.md:30-32`

- [ ] **步骤 1：更新 `SyncCore/README.md`**

把：

```markdown
| `HitResolver.cs` | 服务端权威命中裁决；几何与客户端 `AttackShape` 对齐 |
```

改为：

```markdown
| `HitResolver.cs` | 服务端权威命中裁决；几何与客户端 `AttackShape` 对齐；锥形按目标半径扩展（圆-锥相交） |
```

- [ ] **步骤 2：更新 `SyncLayer/README.md`**

把「目标锁定」条目的结尾：

```markdown
  施法前宿主调 `EnsureTargetFor(skillData)`：无锁定按技能形状选最近候选，`LockedTargetId` 随
  `CastRequest.TargetPlayerId` 上线，服务端按显式目标裁决（形状/距离校验不变）。
```

改为：

```markdown
  施法前宿主调 `EnsureTargetFor(skillData)`：无锁定按技能形状选最近候选，`LockedTargetId` 随
  `CastRequest.TargetPlayerId` 上线，服务端按显式目标裁决（形状/距离校验不变）。
  候选筛选与施法前复核使用**表面距离**（中心距 − 目标半径）和同一角扩展
  （`半角 + asin(半径/中心距)`）；大体型目标贴脸时不丢锁。
```

把 `NpcBodyCollider` 条目：

```markdown
- `NpcBodyCollider` 在运行时按模型 bounds 生成，挂在怪物视图上；预制体本身不挂碰撞体（有资产断言）。
```

改为：

```markdown
- `NpcBodyCollider` 在运行时按模型 bounds 生成，挂在怪物视图上；预制体本身不挂碰撞体（有资产断言）。
  `BodyRadius` 由模型 XZ 较大半宽推导，供目标锁定使用。
```

- [ ] **步骤 3：更新 `Server/Monster/README.md`**

把「命中按目标表面距离」条目的最后一句：

```markdown
  半径为 0，PvP 行为不变。
```

改为：

```markdown
  半径为 0，PvP 行为不变；锥形方向同样按目标半径扩展（圆-锥相交），半径 0 时逐值不变。
```

- [ ] **步骤 4：运行全量验证**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HitResolver"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolver"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster"
bash tools/unity-tests.sh EditMode HitResolverTests
bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests
bash tools/unity-tests.sh EditMode NpcBodyColliderTests
```

预期：全部 PASS。若 `Monster` 过滤出现与本次无关的既有失败，记录失败用例名并在
commit message 里注明，不要顺手改。

- [ ] **步骤 5：Commit 文档**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
        Server/Monster/README.md
git commit -m "docs: document body-aware cone hit and target lock"
```

- [ ] **步骤 6：手工 E2E（联网，不属于自动化门禁）**

1. 启动服务端（不要用 `dotnet run`，apphost 会让 `HttpListener.Start` 抛「句柄无效」）：

   ```bash
   dotnet Server/bin/Debug/net8.0/Server3C.dll 8000 --monsters 6 --map demoday
   ```

2. Unity Editor 选「联网」进 DemoDay，走到蜘蛛 `(18.333, 18.389)`。
3. 贴侧面、面朝与「玩家→蜘蛛中心」约 90° 的切线方向挥普攻 → 出红字伤害。
4. 面朝正后方挥普攻 → 不出伤害。
5. 贴脸时目标选中环 / 目标面板不消失。
6. 打 DogKnight / Slime：正面命中行为与修复前一致。

---

## 自检记录

**规格覆盖度：**

| 规格章节 | 对应任务 |
|---|---|
| §3.1 服务端圆 vs 锥精确相交 | 任务 1 |
| §3.2 客户端 `NpcBodyCollider` / `RemoteEntityView` | 任务 2 |
| §3.2 客户端 `NetworkTargetSelector` | 任务 3 |
| §4 数据流 | 任务 1（服务端） + 任务 2/3（客户端） |
| §5 边界与降级 | 任务 1（半径 0/NaN/体内/背后） + 任务 3（零距离） |
| §6.1 共享几何测试 | 任务 1 步骤 1/2/4/6/7 |
| §6.2 服务端集成测试 | 任务 1 步骤 3/4/6 |
| §6.3 客户端测试 | 任务 2 步骤 1/5；任务 3 步骤 1/2/6 |
| §6.4 命令 | 任务 4 步骤 4 |
| §6.5 手工 E2E | 任务 4 步骤 6 |
| §9 验收清单 | 任务 4 步骤 4/6 |
| §10 文档同步 | 任务 4 步骤 1-3 |

**占位符扫描：** 无「待定 / TODO / 后续实现 / 类似任务 N」；每个代码步骤都给了可粘贴代码。

**类型一致性：**
- `InCone(Vec3, float, float, float, float, Vec3, float)` 在任务 1 定义，两处调用点参数一致。
- `NpcBodyCollider.BodyRadius`（`float { get; private set; }`）在任务 2 定义；
  `RemoteEntityView.BodyCollider` / `BodyRadius` 在任务 2 定义；任务 3 读取 `view.BodyRadius`，一致。
- `Candidate(long, Vector3, byte, bool, float radius = 0f)` 在任务 3 定义；测试用命名参数 `radius:`，一致。
- `FilterCandidates` / `IsWithinSkillShape` 签名不变，任务 3 的调用点无需改。
- 测试命名沿用仓库现有 `PascalCase_With_Underscores`（用户已确认，不采用 `.claude/rules/test-standards.md`
  的 `test_` 前缀，避免同一测试文件内两种风格并存）。
