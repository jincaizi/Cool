# 怪物战斗修复（尸体 / 蜘蛛贴脸 / 大体型命中 / 攻击变体）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 修复联网模式下怪物攻击尸体、蜘蛛贴脸不攻击、普攻打大体型 Boss 无判定、多技能共用同一攻击动画四个问题。

**架构：** 服务端把“攻击视线”从移动体型中解耦（0.25m 探针），并让命中判定计入 NPC 体型半径；目标死亡时取消前摇。客户端本地 AI 增加目标存活/超距校验；攻击动画变体按技能区分。所有改动配回归测试。

**技术栈：** Unity 2022.3.25f1 + C# / .NET 8 服务端 / xUnit（服务端）+ NUnit（Unity EditMode）。

**规格：** `docs/superpowers/specs/2026-11-04-monster-combat-fixes-design.md`

---

## 文件结构

### 服务端

| 文件 | 职责 |
|---|---|
| `Server/Monster/AttackLineOfSight.cs`（新建） | 攻击视线：0.25m 探针；与移动 `bodyRadius` 解耦 |
| `Server/Monster/Movement/PathFollower.cs` | 增加 `HasAttackLineOfSight` 转发 |
| `Server/Monster/MonsterBtActions.cs` | Chase 停步 / Attack 出手改用攻击视线 |
| `Server/Monster/Brain/MonsterBrain.cs` | 前摇中目标失效 → 当 tick 取消 |
| `Server/Monster/MonsterModule.cs` | 半径归一提前 + 传给 `AddNpc`；删除不再使用的 `BodyRadiusOf` |
| `Server/Server3C.cs` | 判定帧 LOS 改走 `AttackLineOfSight` |
| `Server/Entity/EntityStore.cs` | `AddNpc` 接收并写入 `BodyRadius` |
| `Server/Player/PlayerState.cs` | 新增 `BodyRadius` |
| `Server/Handler/CastHandler.cs` | `BuildCandidates` 传 NPC 半径 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs` | 双端共享：`CombatCandidate.Radius` + 表面距离 |

### 客户端

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIContext.cs` | 新增 `TargetEntity` |
| `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs` | 目标有效性校验 + `ShouldDropTarget` |
| `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/MonsterAttackState.cs` | 结算跳过死亡目标 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs` | 多技能 → 不同动画变体 |

### 测试

| 文件 | 覆盖 |
|---|---|
| `Server/Server.Tests/Monster/MonsterP2AcceptanceTests.cs` | 死亡取消前摇 |
| `Server/Server.Tests/Monster/AttackLineOfSightTests.cs`（新建） | 探针半径 / 隔墙 |
| `Server/Server.Tests/Monster/MonsterNavigationTests.cs` | 蜘蛛贴脸起手集成 |
| `Server/Server.Tests/SyncCore/HitResolverTests.cs` | 半径表面距离（单目标/线/圆） |
| `Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs` | 同上，客户端镜像 |
| `Server/Server.Tests/Combat/CombatResolverNpcTests.cs` | 大体型 NPC 被普攻命中 |
| `Assets/Scripts/Tests/Editor/Monster/AIBrainTargetValidityTests.cs`（新建） | 本地 AI 目标校验 |
| `Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs` | 变体映射 |
| `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` | 排程路由（NPC 侧改 50006） |

---

## 任务 1：目标死亡时取消前摇

**文件：**
- 修改：`Server/Monster/Brain/MonsterBrain.cs:50-55`
- 测试：`Server/Server.Tests/Monster/MonsterP2AcceptanceTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `MonsterP2AcceptanceTests` 末尾追加：

```csharp
[Fact]
public void Player_Death_During_Windup_Cancels_And_Stops_Casts()
{
    var (store, module, id) = Make();
    var state = module.GetState(id)!;
    var player = Login(store, 1, new Vector3(0f, 0f, 2f)); // 距出生点 3m
    state.Mode = MonsterMode.Combat;
    state.TargetId = 1L;
    state.Threats.AddDamage(1, 10f);
    state.AttackResolveTick = 3;      // 前摇中（本 tick 尚未到期）
    player.ApplyDamage(999f);          // 目标死亡

    module.Tick(0);

    Assert.Null(state.AttackResolveTick);       // 前摇当 tick 取消
    Assert.Null(state.TargetId);                // 目标立即清空
    Assert.Empty(module.DrainPendingCasts());   // 不再产生新施法
    Assert.Equal((byte)NpcAnimationState.Idle, store.GetPlayer(id)!.ActionState);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Player_Death_During_Windup_Cancels_And_Stops_Casts"
```

预期：FAIL，`AttackResolveTick` 仍为 3（前摇锁目标绕过了有效性校验）。

- [ ] **步骤 3：实现最少代码**

把 `MonsterBrain.Update` 里的 `selected` 计算替换为：

```csharp
long? selected;
if (m.AttackResolveTick.HasValue)
{
    selected = m.TargetId;
    if (selected.HasValue && !IsValidTarget(m, selected.Value))
    {
        // 前摇锁目标不能让死亡 / 离线 / 出圈目标继续占用窗口。
        // 已广播的挥砍仍会播完，但 CombatResolver 的 Alive 过滤保证零伤害。
        m.AttackResolveTick = null;
        selected = null;
    }
}
else
{
    selected = TargetSelector.Select(m.Threats, m.TargetId,
        id => IsValidTarget(m, id),
        m.IsTauntActive(tick) ? m.TauntTargetId : null);
}
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Player_Death_During_Windup_Cancels_And_Stops_Casts"
```

预期：PASS。

- [ ] **步骤 5：跑怪物全量回归**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster"
```

预期：全绿（尤其 `Later_Attacker_Takes_Over_After_Threshold_But_Not_During_Windup` 与 `Evade_Resets_At_Home_And_Target_Death_Prefers_Other_Threat`）。

- [ ] **步骤 6：Commit**

```bash
git add Server/Monster/Brain/MonsterBrain.cs Server/Server.Tests/Monster/MonsterP2AcceptanceTests.cs
git commit -m "fix(monster): cancel windup when the locked target dies"
```

---

## 任务 2：攻击视线与移动体型解耦

**文件：**
- 创建：`Server/Monster/AttackLineOfSight.cs`
- 修改：`Server/Monster/Movement/PathFollower.cs`、`Server/Monster/MonsterBtActions.cs`、`Server/Monster/MonsterModule.cs`、`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Monster/AttackLineOfSightTests.cs`（新建）、`Server/Server.Tests/Monster/MonsterNavigationTests.cs`

- [ ] **步骤 1：编写失败的单元测试**

创建 `Server/Server.Tests/Monster/AttackLineOfSightTests.cs`：

```csharp
using KcpServer;
using KcpServer.Monster;
using KcpServer.Navigation;
using KcpServer.Tests.Navigation;
using Xunit;

namespace KcpServer.Tests.Monster;

public class AttackLineOfSightTests
{
    /// <summary>2×2 空洞在 x∈[8,10)、z∈[8,10)；玩家站在东边缘外 1m → 点 clearance≈1.0。</summary>
    private static NavigationService HoleNav()
        => new NavigationService(
            NavMeshFixture.Grid(20, 20, (x, z) => !(x >= 8 && x < 10 && z >= 8 && z < 10)),
            "test");

    [Fact]
    public void Low_Clearance_Target_Is_Visible_With_Probe_Radius()
    {
        var nav = HoleNav();
        var from = new Vector3(13.4f, 0f, 9f);
        var to = new Vector3(11f, 0f, 9f);

        // 旧口径：整条线要容得下 1.74m 身体 → 失败
        Assert.False(nav.HasLineOfSight(from, to, 1.74f));
        // 新口径：0.25m 武器探针 → 通过
        Assert.True(AttackLineOfSight.HasLineOfSight(nav, from, to));
    }

    [Fact]
    public void Wall_Still_Blocks_Attack_Line_Of_Sight()
    {
        var nav = new NavigationService(
            NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10)), "test");

        Assert.False(AttackLineOfSight.HasLineOfSight(nav,
            new Vector3(5f, 0f, 3.4f), new Vector3(5f, 0f, 5.6f)));
    }
}
```

- [ ] **步骤 2：编写失败的集成测试**

在 `MonsterNavigationTests` 末尾追加：

```csharp
[Fact]
public void Big_Boss_Attacks_At_Point_Blank_When_Body_Clearance_Is_Too_Small()
{
    // 2×2 空洞；玩家站在东边缘外 1m（clearance≈1.0），蜘蛛在 2.4m 外。
    NavMeshGraph graph = NavMeshFixture.Grid(20, 20,
        (x, z) => !(x >= 8 && x < 10 && z >= 8 && z < 10));
    var nav = new NavigationService(graph, "test");
    var store = new EntityStore();
    long seed = MonsterModule.NpcIdSeed;
    var module = new MonsterModule(store, () => ++seed, navigation: nav);
    long bossId = module.SpawnMonster(4, new Vector3(13.4f, 0f, 9f), new Vector3(13.4f, 0f, 9f));

    var player = store.AddPlayer(1, "P1");
    player.IsLoggedIn = true;
    player.UpdatePosition(new Vector3(11f, 0f, 9f), 0f, 0f, 1L, 0U);

    bool sawBossAttack = false;
    for (uint t = 0; t < 10; t++)
    {
        module.Tick(t);
        foreach (var (_, request) in module.DrainPendingCasts())
            if (request.SkillId == 50004) sawBossAttack = true;
    }

    Assert.True(sawBossAttack,
        "贴脸低 clearance 时蜘蛛应起手 50004（旧 1.74m LOS 会退化成 Chase 推人）");
}
```

- [ ] **步骤 3：运行测试验证失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AttackLineOfSight"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Big_Boss_Attacks_At_Point_Blank"
```

预期：编译失败（`AttackLineOfSight` 不存在）或断言失败（无 50004）。

- [ ] **步骤 4：创建 `AttackLineOfSight`**

创建 `Server/Monster/AttackLineOfSight.cs`：

```csharp
using KcpServer.Navigation;

namespace KcpServer.Monster
{
    /// <summary>
    /// 攻击视线（2026-11-04）：与移动体型解耦。
    /// 只回答“武器到目标之间有没有 navmesh 空洞”——0.25m 探针。
    /// 移动/避让仍用 MonsterData.bodyRadius；这里的探针不是身体 clearance。
    /// 不做目标点投影：TryProject 返回网格边界点（clearance 0），0.25m 探针仍会失败。
    /// </summary>
    public static class AttackLineOfSight
    {
        public const float ProbeRadius = 0.25f;

        public static bool HasLineOfSight(NavigationService navigation, Vector3 from, Vector3 to)
            => navigation.HasLineOfSight(from, to, ProbeRadius);
    }
}
```

- [ ] **步骤 5：接入 PathFollower 与行为树**

`Server/Monster/Movement/PathFollower.cs` 在 `HasLineOfSight` 后追加：

```csharp
/// <summary>攻击视线：0.25m 探针；无导航恒 true。</summary>
public bool HasAttackLineOfSight(Vector3 from, Vector3 to)
    => _navigation == null || AttackLineOfSight.HasLineOfSight(_navigation, from, to);
```

`Server/Monster/MonsterBtActions.cs` 两处替换：

```csharp
// Chase
if (inRange && (m.Follower == null
    || m.Follower.HasAttackLineOfSight(m.Position, target)))
    return BtStatus.Success;

// Attack
if (m.Follower != null && !m.Follower.HasAttackLineOfSight(m.Position, target))
{
    m.Moving = false;
    entity.ActionState = (byte)NpcAnimationState.Idle;
    return BtStatus.Failure;
}
```

- [ ] **步骤 6：接入判定帧 LOS 并清理孤儿 API**

`Server/Server3C.cs` 的 `SetNpcLineOfSight` 改为：

```csharp
_castHandler.SetNpcLineOfSight((npcId, from, to) =>
    AttackLineOfSight.HasLineOfSight(navigation, from, to));
```

同时删除该 `if (navigation != null)` 块里已不再使用的 `MonsterModule module = _monsterModule;`。
删除 `MonsterModule.BodyRadiusOf`（`Server/Monster/MonsterModule.cs:187-189`）——本次改动后已无调用方；
`RadiusOfTemplate` 保留（ORCA 仍用）。

- [ ] **步骤 7：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AttackLineOfSight"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Big_Boss_Attacks_At_Point_Blank"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster_Does_Not_Attack_Through_Wall"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Navigation"
```

预期：全绿。若 `Monster_Does_Not_Attack_Through_Wall` 变红，说明探针太粗，
先停下修 helper，不要改测试。

- [ ] **步骤 8：Commit**

```bash
git add Server/Monster/AttackLineOfSight.cs Server/Monster/Movement/PathFollower.cs Server/Monster/MonsterBtActions.cs Server/Monster/MonsterModule.cs Server/Server3C.cs Server/Server.Tests/Monster/AttackLineOfSightTests.cs Server/Server.Tests/Monster/MonsterNavigationTests.cs
git commit -m "fix(monster): decouple attack line-of-sight from body radius"
```

---

## 任务 3：命中判定计入目标体型（共享 SyncCore）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs`
- 测试：`Server/Server.Tests/SyncCore/HitResolverTests.cs`、`Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs`

- [ ] **步骤 1：编写失败的服务端测试**

在 `Server/Server.Tests/SyncCore/HitResolverTests.cs`：

1. 把候选工厂改为可选半径：

```csharp
private static CombatCandidate C(long id, float x, float z, bool alive = true, float radius = 0f)
    => new CombatCandidate(id, new Vec3(x, 0f, z), alive, radius);
```

2. 追加形状与用例：

```csharp
private static readonly HitShape Single20 = new HitShape
{
    ShapeType = 0, Range = 2f, Angle = 120f
};

[Fact]
public void Single_Explicit_Target_With_Radius_Hits_By_Surface_Distance()
{
    var withRadius = new List<CombatCandidate> { C(2, 0f, 3f, radius: 1.74f) };
    var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, withRadius);
    Assert.Equal(new long[] { 2 }, hits);

    var noRadius = new List<CombatCandidate> { C(2, 0f, 3f) };
    Assert.Empty(HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single20, noRadius));
}

[Fact]
public void Line_Hit_Expands_By_Target_Radius()
{
    var withRadius = new List<CombatCandidate> { C(2, 2.5f, 5f, radius: 1.74f) };
    Assert.Equal(new long[] { 2 }, HitResolver.ResolveLineAll(
        new Vec3(0, 0, 0), 0f, 0f, 10f, 1f, null, withRadius));

    var noRadius = new List<CombatCandidate> { C(2, 2.5f, 5f) };
    Assert.Empty(HitResolver.ResolveLineAll(
        new Vec3(0, 0, 0), 0f, 0f, 10f, 1f, null, noRadius));
}

[Fact]
public void Circle_Hit_Expands_By_Target_Radius()
{
    var withRadius = new List<CombatCandidate> { C(2, 0f, 3f, radius: 1.74f) };
    Assert.Equal(new long[] { 2 },
        HitResolver.ResolveCircle(new Vec3(0, 0, 0), 2f, withRadius));

    var noRadius = new List<CombatCandidate> { C(2, 0f, 3f) };
    Assert.Empty(HitResolver.ResolveCircle(new Vec3(0, 0, 0), 2f, noRadius));
}
```

- [ ] **步骤 2：编写失败的客户端镜像测试**

在 `Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs` 做同样三件事（工厂加可选半径；
`Single20`；三个用例），断言改用 NUnit 风格：

```csharp
Assert.That(hits, Is.EqualTo(new long[] { 2 }));
Assert.That(hits, Is.Empty);
```

- [ ] **步骤 3：运行测试验证失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HitResolverTests"
bash tools/unity-tests.sh EditMode HitResolverTests
```

预期：编译失败（`CombatCandidate` 没有 4 参数构造函数）。

- [ ] **步骤 4：实现 `CombatCandidate.Radius` 与表面距离**

`HitResolver.cs` 的 `CombatCandidate` 改为：

```csharp
public readonly struct CombatCandidate
{
    public readonly long Id;
    public readonly Vec3 Position;
    public readonly bool Alive;
    /// <summary>目标身体半径（米）；玩家恒 0，NPC 用 bodyRadius。命中按表面距离。</summary>
    public readonly float Radius;

    public CombatCandidate(long id, Vec3 position, bool alive, float radius = 0f)
    {
        Id = id;
        Position = position;
        Alive = alive;
        Radius = radius;
    }
}
```

在 `HitResolver` 内加：

```csharp
private static float SurfaceDistance(Vec3 a, Vec3 b, float radius)
    => Math.Max(0f, DistXZ(a, b) - radius);
```

`Resolve` 的三处距离替换：

```csharp
// AOE_Circle
if (SurfaceDistance(casterPos, c.Position, c.Radius) <= shape.AreaRadius)

// 显式目标
if (SurfaceDistance(casterPos, c.Position, c.Radius) > shape.Range) continue;

// 自动目标
float dist = SurfaceDistance(casterPos, c.Position, c.Radius);
if (dist > shape.Range) continue;
```

`ResolveLineAll` 与 `ResolveLineNearest` 的过滤改为：

```csharp
float lateral = Math.Abs(dx * fz - dz * fx);
if (lateral > halfWidth + c.Radius) continue;
if (along + c.Radius < startDistance || along - c.Radius > startDistance + length) continue;
```

`ResolveCircle`：

```csharp
if (SurfaceDistance(center, c.Position, c.Radius) <= radius) hits.Add(c.Id);
```

- [ ] **步骤 5：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HitResolverTests"
bash tools/unity-tests.sh EditMode HitResolverTests
```

预期：两份测试全绿；既有 `Single_Explicit_Target_Out_Of_Range_Misses` 等不回归。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs Server/Server.Tests/SyncCore/HitResolverTests.cs Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs
git commit -m "feat(synccore): account for target radius in hit resolution"
```

---

## 任务 4：服务端把 NPC 半径送入裁决

**文件：**
- 修改：`Server/Player/PlayerState.cs`、`Server/Entity/EntityStore.cs`、`Server/Monster/MonsterModule.cs`、`Server/Handler/CastHandler.cs`
- 测试：`Server/Server.Tests/Combat/CombatResolverNpcTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `CombatResolverNpcTests` 的 `World` 工厂加可选半径：

```csharp
private static (EntityStore Store, CombatResolver Resolver, long Player, long Npc) World(
    float npcZ, int templateId = 1, float npcRadius = 0f)
{
    var store = new EntityStore();
    long player = 1;
    long npc = MonsterModule.NpcIdSeed + 1;
    store.AddPlayer(player, "P");
    store.GetPlayer(player)!.IsLoggedIn = true;
    store.AddNpc(npc, $"Slime#{npc}", templateId, 100f, new Vector3(0f, 0f, npcZ), npcRadius);
    store.GetPlayer(npc)!.Rotation = 180f;
    var resolver = new CombatResolver(store, SeededRng);
    return (store, resolver, player, npc);
}
```

追加：

```csharp
[Fact]
public void Player_Melee_Hits_Large_Npc_By_Surface_Distance()
{
    var (store, resolver, player, npc) = World(3.2f, templateId: 4, npcRadius: 1.74f);

    var result = Resolve(resolver, player, PlayerSwing(npc), 10);

    Assert.True(result.Accepted);
    var hit = Assert.Single(result.Hits);
    Assert.Equal(npc, hit.TargetPlayerId); // 3.2 - 1.74 = 1.46 <= 2.0
}

[Fact]
public void Player_Melee_Misses_Same_Distance_Without_Radius()
{
    var (store, resolver, player, npc) = World(3.2f, templateId: 4); // 半径 0

    var result = Resolve(resolver, player, PlayerSwing(npc), 10);

    Assert.True(result.Accepted);
    Assert.Empty(result.Hits);
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Player_Melee_Hits_Large_Npc"
```

预期：编译失败（`AddNpc` 没有 `bodyRadius` 参数 / `BodyRadius` 不存在）。

- [ ] **步骤 3：实现数据贯通**

`PlayerState.cs` 在 `TemplateId` 后加：

```csharp
/// <summary>NPC 身体半径（米；玩家恒 0）。命中判定按表面距离使用。</summary>
public float BodyRadius { get; internal set; }
```

`EntityStore.AddNpc`：

```csharp
public PlayerState AddNpc(long npcId, string name, int templateId, float hp, Vector3 position,
    float bodyRadius = 0f)
{
    var state = new PlayerState(npcId)
    {
        PlayerName = name,
        Kind = (byte)EntityKind.Npc,
        TemplateId = templateId,
        BodyRadius = bodyRadius
    };
    state.MaxHp = hp;
    state.Hp = hp;
    state.UpdatePosition(position, 0f, 0f, 0L, 0U);
    _entities[npcId] = state;
    return state;
}
```

`MonsterModule.SpawnMonster`：把 `config.bodyRadius = RadiusOfTemplate(templateId);`
（连同其注释）移到 `long npcId = _nextNpcId();` 之前，并把半径传给 `AddNpc`：

```csharp
config.bodyRadius = RadiusOfTemplate(templateId);

long npcId = _nextNpcId();
var entity = _store.AddNpc(npcId, $"{config.name}#{npcId}", templateId, config.hp, spawn,
    config.bodyRadius);
entity.Rotation = YawTowardOrigin(position);
```

`CastHandler.BuildCandidates` 的候选构造改为：

```csharp
candidates.Add(new CombatCandidate(p.PlayerId,
    new Vec3(p.Position.X, p.Position.Y, p.Position.Z), p.IsAlive, p.BodyRadius));
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Player_Melee_Hits_Large_Npc"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolver"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster"
```

预期：全绿；`Player_Cast_Auto_Target_Hits_Closer_Npc_Not_Farther_Player` 保持通过
（半径 0 的既有测试行为不变）。

- [ ] **步骤 5：Commit**

```bash
git add Server/Player/PlayerState.cs Server/Entity/EntityStore.cs Server/Monster/MonsterModule.cs Server/Handler/CastHandler.cs Server/Server.Tests/Combat/CombatResolverNpcTests.cs
git commit -m "fix(server): carry npc body radius into combat candidates"
```

---

## 任务 5：本地 AI 目标有效性

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIContext.cs`、`.../AIBrain.cs`、`.../States/MonsterAttackState.cs`
- 测试：`Assets/Scripts/Tests/Editor/Monster/AIBrainTargetValidityTests.cs`（新建）

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/Monster/AIBrainTargetValidityTests.cs`：

```csharp
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.Monster
{
    public class AIBrainTargetValidityTests
    {
        private sealed class FakeDamageable : IDamageable
        {
            private readonly Transform _transform;
            private bool _isAlive = true;

            public FakeDamageable(Transform transform) => _transform = transform;

            /// <summary>模拟 Unity 销毁后的严格读取：目标已销毁时任何 IsAlive 访问都抛异常，
            /// 用来证明 ShouldDropTarget 先做 Transform 伪 null 检查。</summary>
            public bool ThrowOnAlive { get; set; }

            public bool IsAlive
            {
                get
                {
                    if (ThrowOnAlive)
                        throw new MissingReferenceException("IsAlive touched after target destroy");
                    return _isAlive;
                }
                set => _isAlive = value;
            }

            public Transform Transform => _transform;
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection) { }
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact) { }
        }

        private GameObject _selfGo;
        private MonsterConfig _config;
        private AIContext _ctx;
        private AIBrain _brain;

        [SetUp]
        public void SetUp()
        {
            _selfGo = new GameObject("ai-self");
            _selfGo.transform.position = new Vector3(1000f, 0f, 1000f); // 远离 PhysicsRegistry 中的实体
            _config = ScriptableObject.CreateInstance<MonsterConfig>();
            _config.DetectRange = 10f;
            _config.LeaveRange = 15f;
            _ctx = new AIContext { Self = _selfGo.transform, Config = _config };
            _brain = new AIBrain(_ctx, null!, _config);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_selfGo);
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void test_ai_target_dead_is_dropped()
        {
            var targetGo = new GameObject("target");
            var fake = new FakeDamageable(targetGo.transform) { IsAlive = false };
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;

            _brain.TryFindTarget();

            Assert.IsTrue(_ctx.Target == null); // 静态类型 Transform → 走 Unity 伪 null 重载
            Assert.IsNull(_ctx.TargetEntity);
            Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void test_ai_target_alive_in_range_is_kept()
        {
            var targetGo = new GameObject("target");
            targetGo.transform.position = _selfGo.transform.position + new Vector3(3f, 0f, 0f);
            var fake = new FakeDamageable(targetGo.transform);
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;

            _brain.TryFindTarget();

            Assert.AreSame(targetGo.transform, _ctx.Target);
            Assert.AreSame(fake, _ctx.TargetEntity);
            Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void test_ai_target_out_of_leave_range_is_dropped()
        {
            var targetGo = new GameObject("target");
            targetGo.transform.position = _selfGo.transform.position + new Vector3(16f, 0f, 0f); // > 15
            var fake = new FakeDamageable(targetGo.transform);
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;

            _brain.TryFindTarget();

            Assert.IsTrue(_ctx.Target == null); // 静态类型 Transform → 走 Unity 伪 null 重载
            Assert.IsNull(_ctx.TargetEntity);
            Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void test_ai_destroyed_target_is_dropped_without_exception()
        {
            var targetGo = new GameObject("target");
            var fake = new FakeDamageable(targetGo.transform) { ThrowOnAlive = true };
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;
            Object.DestroyImmediate(targetGo); // Transform 变 Unity 伪 null，接口引用仍非 null

            // 若实现先读 entity.IsAlive，这里会抛 MissingReferenceException
            Assert.DoesNotThrow(() => _brain.TryFindTarget());
            Assert.IsTrue(_ctx.Target == null); // 静态类型 Transform → 走 Unity 伪 null 重载
            Assert.IsNull(_ctx.TargetEntity);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode AIBrainTargetValidityTests
```

预期：编译失败（`TargetEntity`、`TryFindTarget` 尚不存在）。

- [ ] **步骤 3：实现目标校验**

`AIContext.cs`：加 `using Hotfix.GameSystems.Sys3C.Core.Combat;`，在 `Target` 后加：

```csharp
// ── AIBrain-owned ──
public Transform Target;
/// <summary>与 Target 同步赋值；IDamageable 是接口引用，销毁后不参与 Unity 伪 null。</summary>
public IDamageable TargetEntity;
```

`AIBrain.TryFindTarget` 改为：

```csharp
internal void TryFindTarget()
{
    // 外层必须同时看 TargetEntity：Unity 对象销毁后 Target 为伪 null，
    // 只看 Target 会跳过清理，接口引用会一直挂着。
    if (_ctx.Target != null || _ctx.TargetEntity != null)
    {
        if (!ShouldDropTarget(_ctx.Target, _ctx.TargetEntity, _ctx.Self.position, _config.LeaveRange))
            return;

        _ctx.Target = null;
        _ctx.TargetEntity = null;
    }

    var players = PhysicsRegistry.Instance.FindNearby(
        _ctx.Self.position, _config.DetectRange, EntityType.Player);
    if (players.Count > 0)
    {
        _ctx.TargetEntity = players[0];
        _ctx.Target = players[0].Transform;
    }
}

/// <summary>目标是否应被丢弃（§4.3）。target == null 必须最先判断：接口引用不参与 Unity 伪 null。</summary>
internal static bool ShouldDropTarget(Transform target, IDamageable entity, Vector3 selfPosition,
    float leaveRange)
{
    if (target == null) return true;
    if (entity == null || !entity.IsAlive) return true;
    return (selfPosition - target.position).sqrMagnitude > leaveRange * leaveRange;
}
```

`MonsterAttackState.ResolveDamage` 的循环里加存活过滤：

```csharp
foreach (var t in _hitBuffer)
{
    if (t == null || !t.IsAlive) continue;
    Vector3 dir = (t.Transform.position - ctx.Self.position).normalized;
    t.TakeDamage(damage, dir);
}
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode AIBrainTargetValidityTests
bash tools/unity-tests.sh EditMode MonsterConfigNumbersTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIContext.cs Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/MonsterAttackState.cs Assets/Scripts/Tests/Editor/Monster/AIBrainTargetValidityTests.cs
git commit -m "fix(monster): drop dead or out-of-range ai targets"
```

---

## 任务 6：多技能 → 不同攻击动画变体

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs`、`.../CombatNetworkBridgeTests.cs`

- [ ] **步骤 1：更新失败的测试**

`NpcAttackScheduleTests.Known_Monster_Skill_Maps_To_A_Fixed_Variant` 改为：

```csharp
Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L, 50001));
Assert.AreEqual(1, NpcAttackSchedule.VariantOf(0L, 50002));
Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L, 50003));
Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L, 50004));
Assert.AreEqual(1, NpcAttackSchedule.VariantOf(0L, 50005));
Assert.AreEqual(1, NpcAttackSchedule.VariantOf(0L, 50006));

long t = 638_000_000_000_000_000L;
Assert.AreEqual(0, NpcAttackSchedule.VariantOf(t, 50001));
Assert.AreEqual(1, NpcAttackSchedule.VariantOf(t, 50002));

// 奇桶回退是 1；固定 0 必须仍返回 0。
Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks, 50001));
Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks, 50003));
Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks, 50004));
```

`CombatNetworkBridgeTests.CastBroadcast_From_An_Npc_Schedules_A_Swing_And_From_A_Player_Does_Not`
的 NPC 侧把 50004 改成 50006（两处 `VariantOf` / `SkillId` 与注释）：

```csharp
int npcVariant = NpcAttackSchedule.VariantOf(npcCastTicks, 50006);
// ...
SkillId = 50006, CasterPlayerId = npcId, ServerTimestamp = npcCastTicks
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode NpcAttackScheduleTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
```

预期：FAIL（50004 仍返回 1、50005 仍返回 0；路由用例的前提断言失败）。

- [ ] **步骤 3：更新映射表**

`NpcAttackSchedule.SkillVariants` 改为：

```csharp
private static readonly Dictionary<int, int> SkillVariants = new Dictionary<int, int>
{
    { 50001, 0 }, { 50002, 1 }, { 50003, 0 },
    { 50005, 1 }, { 50004, 0 }, { 50006, 1 },
};
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode NpcAttackScheduleTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/unity-tests.sh EditMode NpcAnimatorAssetTests
```

预期：PASS。`NpcAnimatorAssetTests` 确认四个控制器的 Attack01/02 与参数未被动过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs
git commit -m "fix(sync): map multi-skill monsters to distinct attack variants"
```

---

## 任务 7：文档同步与全量验证

**文件：**
- 修改：`Server/Monster/README.md`、`Server/Navigation/README.md`、
  `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`、
  `Assets/Scripts/Hotfix/GameSystems/Monster/README.md`

- [ ] **步骤 1：更新 README**

1. `Server/Monster/README.md`
   - “行为规则 / P2 行为”补一条：目标死亡/离线/出圈时前摇当 tick 取消，不再产生新施法；
     已广播的一刀仍会播完但零伤害。
   - “导航 / 已知限制”更新 `HasLineOfSight`：攻击链路改走 `AttackLineOfSight`
     （0.25m 探针），`bodyRadius` 只用于寻路/ORCA；原始 `HasLineOfSight`
     仍要求身体 clearance；不做目标点投影，网格外目标仍可能挥不到。
   - “命中”补一条：玩家施法命中 NPC 按 `PlayerState.BodyRadius` 的表面距离判定。
2. `Server/Navigation/README.md` 的 I1 段补一句：攻击链路走 `AttackLineOfSight`，
   `HasLineOfSight` 原始语义不变。
3. `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md` 的变体映射改为：
   `50001/50003/50004 → Attack01；50002/50005/50006 → Attack02；未登记技能回退时间戳变体。`
4. `Assets/Scripts/Hotfix/GameSystems/Monster/README.md` 的“护栏”补一条：本地 AI 每帧校验目标
   （死亡 / 销毁 / 超 LeaveRange 即清）；攻击结算跳过死亡目标。

- [ ] **步骤 2：全量自动化测试**

```bash
bash tools/test-server-inprocess.sh
bash tools/unity-tests.sh EditMode NpcAttackScheduleTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/unity-tests.sh EditMode HitResolverTests
bash tools/unity-tests.sh EditMode AIBrainTargetValidityTests
bash tools/unity-tests.sh EditMode NpcAnimatorAssetTests
```

预期：全部通过。服务端全量里的 `Monster` / `Navigation` / `CombatResolver` 用例不得回归。

- [ ] **步骤 3：手工 E2E（联网）**

1. 启动服务端（从构建输出目录执行，不要用 `dotnet run`）：
   ```bash
   dotnet Server/bin/Debug/net8.0/Server3C.dll 8000 --monsters 6 --map demoday
   ```
2. Unity 选人面板选“联网”进入 DemoDay。
3. 走到蜘蛛 `(18.333, 18.389)` 贴脸：
   - 应每 ≤2s 看到攻击动作，玩家掉血；不再只推不砍。
   - 用普攻打蜘蛛：服务端红字伤害出现（中心距 2.2–2.5m 也命中）。
4. 让角色被打死：
   - 倒地后怪物不再反复挥砍。
   - Console 出现 `[NetHost] 本地 MonsterSpawner 已禁用`；若没有，说明本地怪未清，
     回到任务 5 复核。
5. 打 DogKnight 到半血：50003 播 Attack01、50005 播 Attack02；蜘蛛 50004 播 Attack01、
   50006 播 Attack02。目视确认两段剪辑确实不同。

- [ ] **步骤 4：Commit**

```bash
git add Server/Monster/README.md Server/Navigation/README.md Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md Assets/Scripts/Hotfix/GameSystems/Monster/README.md
git commit -m "docs(monster): document attack los, surface hits and variant mapping"
```

---

## 自检记录

- 规格覆盖：§4.1 → 任务 1；§4.2 → 任务 2；§4.3 → 任务 5；§4.4 → 任务 3 + 4；§4.5 → 任务 6；
  §6/§10 → 任务 7。无遗漏。
- 类型一致性：`CombatCandidate.Radius`、`PlayerState.BodyRadius`、`EntityStore.AddNpc(..., float bodyRadius = 0f)`、
  `AIContext.TargetEntity`、`AIBrain.ShouldDropTarget` 在任务 3/4/5 中签名一致。
- 无占位符；每个代码步骤都给了可粘贴代码与验证命令。
