# 剑气格挡与举盾精力统合实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让 `LineAll` 剑气在第一个格挡成功的玩家处被吸收，删除盾耐久、把盾寿命完全交给精力（每次格挡 15），并让联机客户端在服务端停点处结束剑气特效。

**架构：** 服务端 `CombatResolver` 在每段线形命中上按距离排序、用 `pending.Origin` 判格挡、遇到第一个格挡者置 `Spent` 并把停点写进 `DamageResult`；客户端 `ApplyServerDamage` 扣 15 精力，`CombatNetworkBridge` 把停点转给 `SwordQiSpawner`，剑气飞到停点回池。盾耐久从服务端与单机客户端两侧删除。

**技术栈：** Unity 2022.3.25f1、NUnit EditMode/PlayMode、.NET 8 xUnit、protobuf-net、KCP、Unity MCP。

**规格：** `docs/superpowers/specs/2026-10-21-sword-qi-shield-block-design.md`

---

## 文件结构

| 文件 | 职责 / 本次改动 |
|---|---|
| `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` | `DamageResult` 追加 `ProjectileStopped/StopX/StopZ`（tag 5/6/7） |
| `Server/Player/PlayerState.cs` | 删除 `ShieldDurability`/`ConsumeShieldDurability`/复活回满盾 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 删除 `ShieldMaxDurability`/`ShieldDurabilityPerBlock`，更新格挡注释 |
| `Server/Handler/CastHandler.cs` | `ApplyHit` 增加攻击者位置/存活判断/返回 blocked；`LineAll` 距离排序与吸收；停点字段；广播 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs` | 删除单机盾耐久字段/属性/`AbsorbDamage`/`OnShieldBreak` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs` | 删除 `HandleShieldBreak`，更新注释 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 删除盾破分支；新增 `_blockStaminaCost=15`/`ConsumeBlockStamina`；死亡/复活收盾；注释 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 订阅/退订剑气停点事件；更新盾耐久注释 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs` | 停点字段/`StopAtPoint`/只读面；`Tick` 停点释放 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs` | 活跃列表/`StopNearest`/`DisposeForTests` 清理 |
| `Server/Server.Tests/Handler/CombatResolverSwordQiBlockTests.cs`（新建） | 8 条剑气阻挡/停点/几何回归 |
| `Server/Server.Tests/Combat/BlockMitigationTests.cs` | 删除耐久断言/旧四击用例；新增连续 8 次格挡回归 |
| `Server/Server.Tests/SyncCore/CombatRulesTests.cs` | 删除两条耐久常量断言 |
| `Server/Server.Tests/Protocol/CombatMessagesTests.cs` | 新字段往返断言 |
| `Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs` | 新字段 tag 钉死 |
| `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` | 停点事件 5 条 |
| `Assets/Scripts/Tests/Editor/SwordQiProjectileTests.cs` | 停点 3 条 |
| `Assets/Scripts/Tests/Editor/SwordQiSpawnerTests.cs` | 停点选择 3 条 |
| `Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs`（新建 + `.meta`） | 格挡扣精/力竭/眩晕/致死顺序/离线无盾破/死亡复活收盾 |
| `Assets/Scripts/Tests/PlayMode/BlockMitigationE2ETests.cs` | 联机剑气格挡 E2E |

---

## 任务 1：服务端 LineAll 吸收 + 协议停点 + 删除服务端耐久

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Combat.cs:83-92`
- 修改：`Server/Player/PlayerState.cs:20-76`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs:104-121`
- 修改：`Server/Handler/CastHandler.cs:25-34,148-263,380-404`
- 创建：`Server/Server.Tests/Handler/CombatResolverSwordQiBlockTests.cs`
- 修改：`Server/Server.Tests/Combat/BlockMitigationTests.cs:18-136`
- 修改：`Server/Server.Tests/SyncCore/CombatRulesTests.cs:195-203`
- 修改：`Server/Server.Tests/Protocol/CombatMessagesTests.cs:36-73`
- 修改：`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs`（在 `HitEntry.IsBlocked` 用例后追加）

- [ ] **步骤 1：写服务端剑气阻挡失败测试（新文件）**

创建 `Server/Server.Tests/Handler/CombatResolverSwordQiBlockTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class CombatResolverSwordQiBlockTests
{
    private const long Caster = 1L;
    private const long TargetNpc = 2L;
    private const long Defender = 3L;
    private const long FarDefender = 4L;

    private static PlayerState AddPlayerAt(EntityStore store, long id,
        float x, float z, float yaw, bool defending)
    {
        var p = store.AddPlayer(id, "p" + id);
        p.UpdatePosition(new Vector3(x, 0f, z), yaw, 0f, 0L, 0U);
        p.IsDefending = defending;
        return p;
    }

    private static CastRequest SwordQi() => new CastRequest
    {
        SkillId = 10002,
        Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
    };

    private static CombatResolver MakeResolver(EntityStore store)
        => new CombatResolver(store, new Random(1));

    private static List<CombatResolver.CastResolution> DrainTo(CombatResolver resolver, uint tick)
    {
        var all = new List<CombatResolver.CastResolution>();
        for (uint t = 1; t <= tick; t++) all.AddRange(resolver.DrainResolved(t));
        return all;
    }

    private static void AssertBlockedDamage(float damage)
        => Assert.True(Math.Abs(damage - 27f) < 0.01f || Math.Abs(damage - 40.5f) < 0.01f,
            "格挡伤害应落在 {27, 40.5}：实际 " + damage);

    [Fact]
    public void SwordQi_Stops_At_Blocking_Player_And_Skips_Targets_Behind()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var blocked = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(blocked.IsBlocked);
        AssertBlockedDamage(blocked.Damage);
        Assert.DoesNotContain(hits, h => h.TargetPlayerId == TargetNpc);

        var stop = Assert.Single(results.Where(r => r.ProjectileStopped));
        Assert.Equal(0f, stop.StopPosition.X, 0.001f);
        Assert.Equal(5f, stop.StopPosition.Z, 0.001f);
    }

    [Fact]
    public void SwordQi_Passes_Through_Defender_Facing_Away()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 0f, defending: true); // 背对：yaw 0
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var defenderHit = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.False(defenderHit.IsBlocked);
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Passes_Through_NonDefending_Player()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: false);
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        Assert.Contains(hits, h => h.TargetPlayerId == Defender);
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Closer_Targets_Before_Blocker_Are_Still_Hit()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        store.AddNpc(TargetNpc, "near", 1, 100f, new Vector3(0f, 0f, 2f));
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        const long farNpc = 5L;
        store.AddNpc(farNpc, "far", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        var blocked = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(blocked.IsBlocked);
        Assert.DoesNotContain(hits, h => h.TargetPlayerId == farNpc);
    }

    [Fact]
    public void SwordQi_Stops_At_Nearest_Blocker_When_Far_Blocker_Is_Added_First()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, FarDefender, 0f, 6f, 180f, defending: true);
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var near = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(near.IsBlocked);
        Assert.DoesNotContain(hits, h => h.TargetPlayerId == FarDefender);

        var stop = Assert.Single(results.Where(r => r.ProjectileStopped));
        Assert.Equal(5f, stop.StopPosition.Z, 0.001f);
    }

    [Fact]
    public void SwordQi_Block_Uses_Captured_Origin_After_Caster_Moves()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        var resolver = MakeResolver(store);
        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);

        resolver.DrainResolved(6); // 首段：捕获 pending.Origin
        store.GetPlayer(Caster)!.UpdatePosition(new Vector3(10f, 0f, 0f), 0f, 0f, 0L, 0U);
        var results = resolver.DrainResolved(10); // 第二段

        var blocked = Assert.Single(results.SelectMany(r => r.Hits)
            .Where(h => h.TargetPlayerId == Defender));
        Assert.True(blocked.IsBlocked);
        Assert.Contains(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Swing_Block_Outside_Line_Band_Does_Not_Stop_Projectile()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        // 距原点 2.482m、与 +Z 夹角 55.66°（挥击锥内），横向 2.05m（剑气半宽 2m 外）
        AddPlayerAt(store, Defender, 2.05f, 1.4f, -124.34f, defending: true);
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var swingHit = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(swingHit.IsBlocked);
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Does_Not_Stop_On_Dead_Defender_Killed_By_Swing()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        var defender = AddPlayerAt(store, Defender, 0f, 2f, 180f, defending: true);
        defender.Hp = 10f;
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();
        var defenderHits = hits.Where(h => h.TargetPlayerId == Defender).ToList();

        Assert.Equal(2, defenderHits.Count);            // 挥击 + 线形
        Assert.True(defenderHits[0].IsBlocked);         // 挥击先结算且格挡
        Assert.False(defenderHits[1].IsBlocked);        // 线形对死者不再格挡
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }
}
```

- [ ] **步骤 2：更新耐久/协议失败测试**

`Server/Server.Tests/Combat/BlockMitigationTests.cs`：

0. 类摘要删除“扣耐久/盾耗尽”措辞，改为：

```csharp
/// PvP 格挡减伤（D-P4-8 服务端化）：防御中 + 攻击者落在正面 BlockHalfAngleDeg 锥内
/// ⇒ 伤害 × BlockDamageScale；锥外 / 未防御 ⇒ 全额。盾耐久已删除（2026-10-21），
/// 盾寿命只由客户端精力控制。
```

1. `World` 帮助方法改为：

```csharp
private static (EntityStore Store, CombatResolver Resolver) World(
    float npcZ, float playerYaw = 0f, bool defending = false)
{
    var store = new EntityStore();
    store.AddPlayer(Player, "P");
    var player = store.GetPlayer(Player)!;
    player.IsLoggedIn = true;
    player.UpdatePosition(new Vector3(0f, 0f, 0f), playerYaw, 0f, 0L, 0U);
    player.IsDefending = defending;
    store.AddNpc(Npc, "Slime#1", 1, 100f, new Vector3(0f, 0f, npcZ));
    store.GetPlayer(Npc)!.Rotation = 180f;
    var resolver = new CombatResolver(store, new Random(42));
    return (store, resolver);
}
```

2. 用例 ① 删除：

```csharp
Assert.Equal(CombatRules.ShieldMaxDurability - CombatRules.ShieldDurabilityPerBlock,
    player.ShieldDurability, 4);                                   // 100 − 25 = 75
```

3. 用例 ② 删除：

```csharp
Assert.Equal(CombatRules.ShieldMaxDurability, player.ShieldDurability, 4);
```

并把用例 ② 的注释“且不耗耐久”改为“且不格挡”。

4. 删除 `Depleted_Shield_Stops_Mitigating_From_The_Fifth_Hit` 整个用例，替换为：

```csharp
/// <summary>④ 耐久删除后：连续格挡不再有上限（旧的“第 5 击不再减伤”已不适用）。</summary>
[Fact]
public void Defending_Blocks_Repeatedly_Without_Durability_Limit()
{
    var (store, resolver) = World(npcZ: 1.2f, defending: true);
    var player = store.GetPlayer(Player)!;

    for (int i = 0; i < 8; i++)
    {
        var blocked = Assert.Single(Resolve(resolver, Npc, MonsterBite(), (uint)(10 + i * 30)).Hits);
        Assert.True(blocked.IsBlocked, $"第 {i + 1} 次格挡应命中");
        Assert.Equal(3f, blocked.Damage, 4);
    }
    Assert.Equal(100f - 8 * 3f, player.Hp, 4);   // 8 × 3 = 24
}
```

`Server/Server.Tests/SyncCore/CombatRulesTests.cs` 的 `Block_Table_Values_Are_Pinned` 删除：

```csharp
Assert.Equal(100f, CombatRules.ShieldMaxDurability, 4);
Assert.Equal(25f, CombatRules.ShieldDurabilityPerBlock, 4);
```

`Server/Server.Tests/Protocol/CombatMessagesTests.cs` 的 `DamageResult_Roundtrip_Preserves_Hits`：

- `new DamageResult` 初始化器追加：

```csharp
ProjectileStopped = true,
StopX = 1.5f,
StopZ = 6f,
```

- 断言追加：

```csharp
Assert.True(damage.ProjectileStopped, "ProjectileStopped 未上线——客户端剑气停点整条链静默失效");
Assert.Equal(1.5f, damage.StopX, 4);
Assert.Equal(6f, damage.StopZ, 4);
```

`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs` 在 `HitEntry.IsBlocked` 用例后追加：

```csharp
[Test]
public void DamageResult_ProjectileStop_Tags_Are_Pinned()
{
    var stopped = typeof(Cool.Protocol.DamageResult).GetProperty("ProjectileStopped");
    Assert.IsNotNull(stopped, "DamageResult 必须有 ProjectileStopped");
    Assert.AreEqual(typeof(bool), stopped!.PropertyType);
    Assert.AreEqual(5, GetProtoMemberTag(stopped),
        "ProjectileStopped 必须挂在 tag 5（1..4 已发布）");

    var stopX = typeof(Cool.Protocol.DamageResult).GetProperty("StopX");
    Assert.IsNotNull(stopX);
    Assert.AreEqual(typeof(float), stopX!.PropertyType);
    Assert.AreEqual(6, GetProtoMemberTag(stopX), "StopX 必须挂在 tag 6");

    var stopZ = typeof(Cool.Protocol.DamageResult).GetProperty("StopZ");
    Assert.IsNotNull(stopZ);
    Assert.AreEqual(typeof(float), stopZ!.PropertyType);
    Assert.AreEqual(7, GetProtoMemberTag(stopZ), "StopZ 必须挂在 tag 7");
}
```

- [ ] **步骤 3：运行服务端测试确认失败**

运行：

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatResolverSwordQiBlockTests|FullyQualifiedName~BlockMitigationTests|FullyQualifiedName~CombatRulesTests|FullyQualifiedName~CombatMessagesTests"
```

预期：**编译失败**，报 `DamageResult` 没有 `ProjectileStopped/StopX/StopZ`、`CastResolution` 没有 `ProjectileStopped/StopPosition`；`PlayerState` 仍有 `ShieldDurability`（旧代码尚可编译的部分不报错）。

- [ ] **步骤 4：加协议字段**

`Assets/Scripts/AOT/Protocol/Messages/Combat.cs` 的 `DamageResult` 在 `Hits` 后追加：

```csharp
/// <summary>本次结果是否包含 LineAll 线形吸收（服务端只在剑气被格挡时置位）</summary>
[ProtoMember(5)] public bool ProjectileStopped { get; set; }

/// <summary>吸收点世界 X（ProjectileStopped=true 时有效）</summary>
[ProtoMember(6)] public float StopX { get; set; }

/// <summary>吸收点世界 Z（ProjectileStopped=true 时有效）</summary>
[ProtoMember(7)] public float StopZ { get; set; }
```

- [ ] **步骤 5：实现服务端吸收与停点**

`Server/Handler/CastHandler.cs`：

1. `CastResolution` 增加：

```csharp
/// <summary>LineAll 线形命中被格挡吸收时的停点（2026-10-21）</summary>
public bool ProjectileStopped;
public Vec3 StopPosition;
```

2. `ApplyHit` 改为（去掉 `caster` 参数，返回 blocked）：

```csharp
private bool ApplyHit(PendingCast pending, SkillEntry entry, ProjectileSpec? spec,
    long targetId, Vec3 attackerPos, CastResolution resolution)
{
    var target = _store.GetPlayer(targetId);
    if (target == null) return false;

    float damage = RollDamage(entry, out bool isCritical);
    if (spec != null && spec.ChargeScales)
        damage *= 1f + (spec.ChargeDamageMaxScale - 1f) * pending.Charge;

    bool blocked = target.IsAlive
        && target.IsDefending
        && CombatRules.IsBlocked(target.Rotation,
            attackerPos.X - target.Position.X,
            attackerPos.Z - target.Position.Z);
    if (blocked)
        damage *= CombatRules.BlockDamageScale;

    target.ApplyDamage(damage);
    resolution.Hits.Add(new HitEntry
    {
        TargetPlayerId = targetId,
        Damage = damage,
        IsCritical = isCritical,
        IsBlocked = blocked,
        TargetRemainingHp = target.Hp,
        HitX = target.Position.X,
        HitZ = target.Position.Z
    });
    return blocked;
}
```

3. `ResolveStage` 的结算段替换为：

```csharp
var resolution = new CastResolution
{
    Accepted = true, IsNpcCaster = pending.IsNpcCaster, Broadcast = pending.Broadcast
};

var casterPos = new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z);
if (swingHitIds != null)
{
    foreach (var targetId in swingHitIds)
        ApplyHit(pending, entry, spec, targetId, casterPos, resolution);
}

if (spec != null && spec.Kind == ProjectileKind.LineAll)
    hitIds.Sort((a, b) => CompareAlong(pending, a, b));

var lineAttackerPos = spec != null && spec.Kind == ProjectileKind.LineAll
    ? pending.Origin
    : casterPos;

foreach (var targetId in hitIds)
{
    if (!pending.GroupHitIds.Add(targetId)) continue;
    bool blocked = ApplyHit(pending, entry, spec, targetId, lineAttackerPos, resolution);
    if (blocked && spec != null && spec.Kind == ProjectileKind.LineAll)
    {
        var blocker = _store.GetPlayer(targetId);
        if (blocker != null)
            resolution.StopPosition = new Vec3(
                blocker.Position.X, blocker.Position.Y, blocker.Position.Z);
        resolution.ProjectileStopped = true;
        pending.Spent = true;
        break;
    }
}

if (spec != null && spec.Kind == ProjectileKind.LineNearestSplash && hitIds.Count > 0)
    pending.Spent = true;
return resolution;
```

4. 在 `ResolveStage` 后追加两个私有方法：

```csharp
private int CompareAlong(PendingCast pending, long a, long b)
{
    float da = AlongDistance(pending, a);
    float db = AlongDistance(pending, b);
    int byDistance = da.CompareTo(db);
    return byDistance != 0 ? byDistance : a.CompareTo(b);
}

private float AlongDistance(PendingCast pending, long targetId)
{
    var target = _store.GetPlayer(targetId);
    if (target == null) return float.MaxValue;
    float fx = (float)Math.Sin(pending.Yaw * Math.PI / 180.0);
    float fz = (float)Math.Cos(pending.Yaw * Math.PI / 180.0);
    return (target.Position.X - pending.Origin.X) * fx
         + (target.Position.Z - pending.Origin.Z) * fz;
}
```

5. `BroadcastDamageAsync` 的 `DamageResult` 初始化器追加：

```csharp
ProjectileStopped = resolution.ProjectileStopped,
StopX = resolution.StopPosition.X,
StopZ = resolution.StopPosition.Z
```

- [ ] **步骤 6：删除服务端盾耐久**

`Server/Player/PlayerState.cs`：

- 删除 `ShieldDurability` 属性、`ConsumeShieldDurability` 方法。
- 删除 `TryRespawn` 中的 `ShieldDurability = CombatRules.ShieldMaxDurability;` 行。
- 删除文件顶部 `using Cool.SyncCore;`（`Vector3` 是 `KcpServer.Vector3`，不依赖该 using）。
- `IsDefending` 注释改为：

```csharp
/// <summary>
/// 防御姿态（客户端经 PositionSyncRequest.IsDefending 上报，每次位置同步刷新——
/// PositionSyncHandler；**不进 MovementValidator**：位置校验与防御无关）。
/// 服务端只做姿态 + 几何判断；盾资源只由客户端精力控制（2026-10-21 删除耐久）。
/// </summary>
```

`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`：

- 删除 `ShieldMaxDurability` 与 `ShieldDurabilityPerBlock` 两个常量及其 XML 注释。
- 格挡注释改为：

```csharp
// —— PvP 格挡减伤（2026-10-17；服务端裁决，客户端表现复用既有 DefendHit 链路）——
// 防御中且攻击者落在正面 BlockHalfAngleDeg 锥内 ⇒ 伤害 × BlockDamageScale。
// 盾资源只由客户端精力控制（2026-10-21 删除耐久）：服务端不持有盾耐久，
// 只做姿态 + 几何判断。
```

`Server/Handler/CastHandler.cs` 的 `ApplyHit` 已在步骤 5 去掉耐久条件；确认没有 `ShieldDurability`/`ConsumeShieldDurability` 残留。

- [ ] **步骤 7：运行服务端定向测试**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatResolverSwordQiBlockTests|FullyQualifiedName~BlockMitigationTests|FullyQualifiedName~CombatRulesTests|FullyQualifiedName~CombatMessagesTests"
```

预期：**全部 PASS**（8 条剑气用例 + 格挡/耐久回归 + 规则表 + 协议往返）。

- [ ] **步骤 8：运行服务端全量测试**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj
```

预期：**全部 PASS**。

- [ ] **步骤 9：运行协议 EditMode 门禁**

```bash
unity-mcp-cli run-tool assets-refresh
unity-mcp-cli run-tool tests-run --testMode EditMode --testClass ProtocolShapeGuardTests
```

预期：**PASS**（tag 5/6/7 钉死）。

- [ ] **步骤 10：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/Messages/Combat.cs \
        Server/Player/PlayerState.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs \
        Server/Handler/CastHandler.cs \
        Server/Server.Tests/Handler/CombatResolverSwordQiBlockTests.cs \
        Server/Server.Tests/Combat/BlockMitigationTests.cs \
        Server/Server.Tests/SyncCore/CombatRulesTests.cs \
        Server/Server.Tests/Protocol/CombatMessagesTests.cs \
        Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs
git commit -m "feat(combat): 剑气在格挡成功者处吸收并删除服务端盾耐久"
```

---

## 任务 2：客户端盾模型（删耐久 + 格挡 15 精力 + 死亡/复活收盾）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs:41-56,529-568`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs:108-167`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:72-85,560-742,752-820`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:567-570`
- 创建：`Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs.meta`

- [ ] **步骤 1：写客户端格挡扣精/耐久删除失败测试**

创建 `Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs`：

```csharp
using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class BlockStaminaTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private Hotfix.GameSystems.Sys3C.Character.CharacterController _cc;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab");
            _player = Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            _cc = _entry.CoreController;
            Assert.IsNotNull(_entry.Stamina);
            Assert.IsNotNull(_cc);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) Object.DestroyImmediate(_player);
        }

        private void SetData(System.Func<CharacterData, CharacterData> mutate)
        {
            var field = typeof(Hotfix.GameSystems.Sys3C.Character.CharacterController)
                .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(_cc, mutate((CharacterData)field.GetValue(_cc)));
        }

        private void SetStamina(float value)
        {
            _entry.Stamina.Refill();
            _entry.Stamina.TryConsume(_entry.Stamina.Max - value);
        }

        private void EnterDefend()
        {
            SetData(d => { d.IsGrounded = true; return d; });
            Assert.IsTrue(_cc.TryEnterDefend(), "前置失败：应能进入防御");
        }

        [Test]
        public void test_block_stamina_online_blocked_hit_consumes_fifteen()
        {
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
            Assert.IsFalse(_entry.Stamina.IsExhausted);
        }

        [Test]
        public void test_block_stamina_block_cost_exhausts_and_forces_shield_down()
        {
            EnterDefend();
            SetStamina(15f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);

            Assert.AreEqual(0f, _entry.Stamina.Current, 0.01f);
            Assert.IsTrue(_entry.Stamina.IsExhausted);
            Assert.IsFalse(_cc.IsDefending, "力竭必须强制收盾");
            Assert.IsFalse(_entry.Stamina.CanStartConsuming);
        }

        [Test]
        public void test_block_stamina_non_blocked_hit_consumes_nothing()
        {
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: false);
            Assert.AreEqual(50f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_two_blocked_hits_consume_thirty()
        {
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            _entry.ApplyServerDamage(10f, Vector3.zero, 80f, isBlocked: true);
            Assert.AreEqual(20f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_charges_even_when_local_shield_already_released()
        {
            _cc.TryExitDefend();
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_charges_while_stunned()
        {
            _cc.StatusControllerAdapter.AddStun(1f);
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_lethal_block_still_charges_before_death()
        {
            NetworkModeService.Save(NetworkMode.Online);
            SetStamina(15f);
            float staminaAtDeath = -1f;
            _entry.OnServerDeath += () => staminaAtDeath = _entry.Stamina.Current;

            _entry.ApplyServerDamage(200f, Vector3.zero, 0f, isBlocked: true);

            Assert.AreEqual(0f, staminaAtDeath, 0.01f,
                "致死格挡也必须先扣精，再走死亡回调");
        }

        [Test]
        public void test_block_stamina_offline_block_consumes_fifteen()
        {
            EnterDefend();
            SetStamina(50f);
            ((IDamageable)_entry).TakeDamage(
                DamageBlock.CreateDefault(50f, 0f), -_entry.transform.forward);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_offline_repeated_blocks_never_break_shield()
        {
            EnterDefend();
            SetStamina(100f);
            for (int i = 0; i < 4; i++)
                ((IDamageable)_entry).TakeDamage(
                    DamageBlock.CreateDefault(50f, 0f), -_entry.transform.forward);

            Assert.IsTrue(_cc.IsDefending, "耐久删除后，连续格挡不应强制收盾");
            Assert.AreEqual(40f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_defend_is_cleared_on_death()
        {
            EnterDefend();
            _entry.ApplyServerDamage(200f, Vector3.zero, 0f);
            Assert.IsFalse(_cc.IsDefending, "死亡必须收盾");
        }

        [Test]
        public void test_defend_is_cleared_on_respawn()
        {
            EnterDefend();
            _entry.ApplyServerRespawn(100f);
            Assert.IsFalse(_cc.IsDefending, "复活必须收盾");
        }
    }
}
```

创建 `Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs.meta`：

```yaml
fileFormatVersion: 2
guid: 5f7c2b1e9a4d4f6c8b0e2d3a4c5b6d7e
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

（`guid` 由 Unity 首次导入时生成即可；上面的值是合法的 32 位十六进制。若编辑器已有同名 `.meta`，保留编辑器生成的版本。）

- [ ] **步骤 2：运行 PlayMode 测试确认失败**

```bash
unity-mcp-cli run-tool assets-refresh
unity-mcp-cli run-tool tests-run --testMode PlayMode --testClass BlockStaminaTests
```

预期：`test_block_stamina_*` 断言失败（精力未扣）；`test_block_stamina_offline_repeated_blocks_never_break_shield` 失败（旧耐久第 2 击破盾）；`test_defend_is_cleared_on_death/respawn` 失败（旧代码不收盾）。

- [ ] **步骤 3：删除单机盾耐久**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs`：

- 删除字段：

```csharp
private float _shieldDurability;
private const float MaxShieldDurability = 50f;
```

- 删除属性：

```csharp
public float ShieldDurabilityPercent =>
    _shieldDurability / MaxShieldDurability;

public bool IsShieldBroken => _shieldDurability <= 0f;
```

- `TryEnterDefend` 中删除：

```csharp
_shieldDurability = MaxShieldDurability;
```

- 删除方法 `AbsorbDamage` 与 `OnShieldBreak`（整段）。

`Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`：

- 删除 `HandleShieldBreak` 方法整段。
- 把引用它的注释：

```csharp
// 与既有 HandleShieldBreak(:132)/TriggerHit(:144) 同款模式：EnterHit + 切 Hit 层。
```

改为：

```csharp
// 与既有 TriggerHit(:144) 同款模式：EnterHit + 切 Hit 层。
```

- [ ] **步骤 4：给 Sys3CEntry 接格挡扣精、删除盾破分支、死亡/复活收盾**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`：

1. 在 `_staminaExhaustRecoverRatio` 后追加：

```csharp
[SerializeField, Tooltip("每次成功格挡额外消耗的精力")]
private float _blockStaminaCost = 15f;
```

2. 在 `ForceExitDefendByExhaustion` 后追加：

```csharp
/// <summary>成功格挡的离散精力成本（与持盾每秒消耗叠加）。
/// 以服务端 IsBlocked 为准；精力不足也扣，随后由 ForceExit 收盾。</summary>
private void ConsumeBlockStamina()
{
    if (_stamina == null) return;
    if (!_stamina.TryConsume(_blockStaminaCost))
        ForceExitDefendByExhaustion();
}
```

3. `ApplyServerDamage` 的 `if (isBlocked)` 分支，在 `SetActiveLayer(LayerType.Hit);` 后追加：

```csharp
ConsumeBlockStamina();
```

并把该方法的 XML 注释中：

```csharp
/// 本地盾耐久**不**在此扣——联网裁决的盾在服务端
/// （PlayerState.ShieldDurability），本地盾那套只服务单机权威战斗，两值域不互抄。
```

改为：

```csharp
/// 格挡的 15 点精力成本在本分支扣除（以服务端 IsBlocked 为准）。
```

4. `IDamageable.TakeDamage` 的格挡分支整段替换为：

```csharp
if (_cc.IsDefending && wasBlocked)
{
    ConsumeBlockStamina();
    _fsmManager.HitFSM.EnterDefendHit(new HitData
    {
        Damage = finalDamage,
        HitDirection = hitDirection
    });
    _fsmManager.Coordinator.SetActiveLayer(LayerType.Hit);
}
```

（删除 `absorbed`、`broken`、`_cc.AbsorbDamage`、`_cc.OnShieldBreak`、`_fsmManager.HandleShieldBreak` 与盾破 `return`。）

5. `EvaluateLocalDeath` 在 `if (_currentHP > 0f) return;` 后追加：

```csharp
_cc.TryExitDefend();
_fsmManager.ExitDefend();
```

6. `ApplyServerRespawn` 与 `TickRespawn` 在 `_stamina.Refill();` 后各追加：

```csharp
_cc.TryExitDefend();
_fsmManager.ExitDefend();
```

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` 的注释：

```csharp
// 防御状态 = 3C 的 `IsDefending` 真值源（服务端格挡减伤据此裁决——本地盾耐久不同步，
// 服务端 PlayerState.ShieldDurability 是权威）
```

改为：

```csharp
// 防御状态 = 3C 的 `IsDefending` 真值源（服务端格挡减伤据此裁决——
// 盾资源只由客户端精力控制，服务端不持有盾耐久）
```

- [ ] **步骤 5：运行 PlayMode 定向测试**

```bash
unity-mcp-cli run-tool assets-refresh
unity-mcp-cli run-tool tests-run --testMode PlayMode --testClass BlockStaminaTests
```

预期：**11/11 PASS**。

- [ ] **步骤 6：运行 EditMode 全量（确认客户端编译与既有测试）**

```bash
unity-mcp-cli run-tool tests-run --testMode EditMode
```

预期：**全部 PASS**（`BlockStaminaTests` 是 PlayMode，不在本步）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
        Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs \
        Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs.meta
git commit -m "feat(stamina): 删除盾耐久，格挡成功扣 15 精力并在死亡/复活收盾"
```

---

## 任务 3：客户端剑气停点视觉

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs:30-38,98-137`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:586-651`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs:14-92`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs:12-73`
- 修改：`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs`（文件末尾追加）
- 修改：`Assets/Scripts/Tests/Editor/SwordQiProjectileTests.cs`（文件末尾追加）
- 修改：`Assets/Scripts/Tests/Editor/SwordQiSpawnerTests.cs`（文件末尾追加）

- [ ] **步骤 1：写停点视觉失败测试**

`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` 末尾追加（文件已有 `System.Linq`、`Cool.Protocol`、`UnityEngine`）：

```csharp
[Test]
public void OnDamageResult_ProjectileStopped_LocalCaster_Raises_Stop_Event()
{
    var registry = new RemoteEntityRegistry(_root.transform);
    var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
    int raised = 0;
    Vector3 stop = Vector3.zero;
    bridge.OnLineProjectileStopped += p => { raised++; stop = p; };

    bridge.OnMessage(MessageId.DamageResult, new DamageResult
    {
        SourcePlayerId = 1L,
        SkillId = 10002,
        ProjectileStopped = true,
        StopX = 0f,
        StopZ = 6f
    });
    bridge.Update();

    Assert.AreEqual(1, raised);
    Assert.AreEqual(0f, stop.x, 0.001f);
    Assert.AreEqual(6f, stop.z, 0.001f);
}

[Test]
public void OnDamageResult_ProjectileStopped_RemoteCaster_Does_Not_Raise()
{
    var registry = new RemoteEntityRegistry(_root.transform);
    var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
    int raised = 0;
    bridge.OnLineProjectileStopped += _ => raised++;

    bridge.OnMessage(MessageId.DamageResult, new DamageResult
    {
        SourcePlayerId = 2L,
        SkillId = 10002,
        ProjectileStopped = true,
        StopX = 0f,
        StopZ = 6f
    });
    bridge.Update();

    Assert.AreEqual(0, raised);
}

[Test]
public void OnDamageResult_ProjectileStopped_NonLineAllSkill_Does_Not_Raise()
{
    var registry = new RemoteEntityRegistry(_root.transform);
    var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
    int raised = 0;
    bridge.OnLineProjectileStopped += _ => raised++;

    bridge.OnMessage(MessageId.DamageResult, new DamageResult
    {
        SourcePlayerId = 1L,
        SkillId = 10001,
        ProjectileStopped = true,
        StopX = 0f,
        StopZ = 6f
    });
    bridge.Update();

    Assert.AreEqual(0, raised);
}

[Test]
public void OnDamageResult_BlockedHit_Without_StopFlag_Does_Not_Raise()
{
    var registry = new RemoteEntityRegistry(_root.transform);
    var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
    int raised = 0;
    bridge.OnLineProjectileStopped += _ => raised++;

    bridge.OnMessage(MessageId.DamageResult, new DamageResult
    {
        SourcePlayerId = 1L,
        SkillId = 10002,
        ProjectileStopped = false,
        Hits =
        {
            new HitEntry
            {
                TargetPlayerId = 3L, Damage = 27f, IsBlocked = true,
                HitX = 0f, HitZ = 5f
            }
        }
    });
    bridge.Update();

    Assert.AreEqual(0, raised, "客户端不得回退到扫描 IsBlocked 来停剑气");
}

[Test]
public void OnDamageResult_MultipleBlockedHits_Raises_StopEvent_Once()
{
    var registry = new RemoteEntityRegistry(_root.transform);
    var bridge = new CombatNetworkBridge(_client, () => 1L, registry);
    int raised = 0;
    bridge.OnLineProjectileStopped += _ => raised++;

    bridge.OnMessage(MessageId.DamageResult, new DamageResult
    {
        SourcePlayerId = 1L,
        SkillId = 10002,
        ProjectileStopped = true,
        StopX = 0f,
        StopZ = 1.5f,
        Hits =
        {
            new HitEntry { TargetPlayerId = 3L, Damage = 27f, IsBlocked = true },
            new HitEntry { TargetPlayerId = 3L, Damage = 27f, IsBlocked = true }
        }
    });
    bridge.Update();

    Assert.AreEqual(1, raised);
}
```

`Assets/Scripts/Tests/Editor/SwordQiProjectileTests.cs` 末尾追加：

```csharp
[Test]
public void test_sword_qi_stop_at_point_releases_at_point()
{
    var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                              Vector3.zero, Vector3.forward);

    qi.StopAtPoint(new Vector3(0f, 0f, 5f));
    for (int i = 0; i < 10; i++) qi.Tick(1f / 18f);

    Assert.IsFalse(qi.gameObject.activeSelf, "飞到停点后应回池");
}

[Test]
public void test_sword_qi_stop_at_point_behind_releases_immediately()
{
    var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                              Vector3.zero, Vector3.forward);

    qi.Tick(3f / 18f);                         // 已飞 3m
    qi.StopAtPoint(new Vector3(0f, 0f, 1f));   // 停点在身后
    qi.Tick(1f / 18f);

    Assert.IsFalse(qi.gameObject.activeSelf);
}

[Test]
public void test_sword_qi_stop_distance_resets_on_respawn()
{
    var cfg = CreateConfig();
    var qi = CreateProjectile(cfg, DamageBlock.CreateDefault(10f, 0f),
                              Vector3.zero, Vector3.forward);

    qi.StopAtPoint(new Vector3(0f, 0f, 5f));
    for (int i = 0; i < 6; i++) qi.Tick(1f / 18f);   // 6m > 5 → 回池
    Assert.IsFalse(qi.gameObject.activeSelf);

    qi.OnSpawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward, null);
    for (int i = 0; i < 6; i++) qi.Tick(1f / 18f);   // 旧停点不得残留

    Assert.IsTrue(qi.gameObject.activeSelf, "OnSpawn 必须重置停点");
}
```

`Assets/Scripts/Tests/Editor/SwordQiSpawnerTests.cs` 末尾追加（文件顶部补 `using System.Linq;`）：

```csharp
[Test]
public void test_sword_qi_spawner_stop_nearest_picks_projectile_on_its_line()
{
    var prefab = CreateQiPrefab();
    _gos.Add(prefab);
    var cfg = CreateConfig(prefab);
    var template = prefab.GetComponent<SwordQiProjectile>();

    SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
    SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), new Vector3(10f, 0f, 0f), Vector3.forward);

    var qis = Object.FindObjectsOfType<SwordQiProjectile>().Where(q => q != template).ToArray();
    Assert.AreEqual(2, qis.Length);
    var first = qis.Single(q => Mathf.Abs(q.transform.position.x) < 0.1f);
    var second = qis.Single(q => Mathf.Abs(q.transform.position.x - 10f) < 0.1f);
    first.Tick(1f / 18f);
    second.Tick(1f / 18f);

    SwordQiSpawner.StopNearest(new Vector3(10f, 0f, 5f));

    Assert.AreEqual(5f, second.StopDistance, 0.01f);
    Assert.AreEqual(cfg.Range, first.StopDistance, 0.01f);
}

[Test]
public void test_sword_qi_spawner_stop_nearest_prefers_projectile_not_passed()
{
    var prefab = CreateQiPrefab();
    _gos.Add(prefab);
    var cfg = CreateConfig(prefab);
    var template = prefab.GetComponent<SwordQiProjectile>();

    SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
    var first = Object.FindObjectsOfType<SwordQiProjectile>()
        .Single(q => q != template);
    first.Tick(7f / 18f);                     // 已飞 7m，越过 5m 停点 2m

    SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
    var second = Object.FindObjectsOfType<SwordQiProjectile>()
        .Single(q => q != template && q != first);
    second.Tick(1f / 18f);                    // 只飞 1m

    SwordQiSpawner.StopNearest(new Vector3(0f, 0f, 5f));

    Assert.AreEqual(5f, second.StopDistance, 0.01f);
    Assert.AreEqual(cfg.Range, first.StopDistance, 0.01f,
        "已飞过停点的旧发不应被选中");
}

[Test]
public void test_sword_qi_spawner_stop_without_active_is_noop()
{
    var prefab = CreateQiPrefab();
    _gos.Add(prefab);
    var cfg = CreateConfig(prefab);
    var template = prefab.GetComponent<SwordQiProjectile>();

    SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
    var qi = Object.FindObjectsOfType<SwordQiProjectile>().Single(q => q != template);
    qi.Release();

    Assert.DoesNotThrow(() => SwordQiSpawner.StopNearest(new Vector3(0f, 0f, 5f)));
}
```

- [ ] **步骤 2：运行 EditMode 测试确认失败**

```bash
unity-mcp-cli run-tool assets-refresh
unity-mcp-cli run-tool tests-run --testMode EditMode --testClass CombatNetworkBridgeTests
```

预期：**编译失败**，报 `CombatNetworkBridge` 没有 `OnLineProjectileStopped`、`SwordQiProjectile` 没有 `StopAtPoint/StopDistance/IsActive`、`SwordQiSpawner` 没有 `StopNearest`。

- [ ] **步骤 3：实现 SwordQiProjectile 停点**

`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs`：

1. 字段区追加：

```csharp
private Vector3 _spawnPosition;
private float _stopDistance;
```

2. `OnSpawn` 内、`_hitIds.Clear();` 后追加：

```csharp
_spawnPosition = origin;
_stopDistance = config.Range;
```

3. 在 `OnSpawn` 后追加只读面：

```csharp
public bool IsActive => !_released;
public float StopDistance => _stopDistance;
public float Traveled => _traveled;
```

4. `Tick` 中把 `if (_traveled >= _config.Range) Release();` 替换为：

```csharp
if (_traveled >= _stopDistance) Release();
```

5. 在 `Tick` 后追加：

```csharp
/// <summary>联机停点：让剑气飞到服务端给定的沿程距离后回池。
/// 停点在身后时钳到当前已飞距离（下一帧立即释放）。</summary>
public void StopAtPoint(Vector3 worldPoint)
{
    if (_released || _config == null) return;
    Vector3 flat = worldPoint - _spawnPosition;
    flat.y = 0f;
    Vector3 dir = _direction;
    dir.y = 0f;
    if (dir.sqrMagnitude < 0.0001f) return;
    float along = Vector3.Dot(flat, dir.normalized);
    _stopDistance = Mathf.Clamp(along, _traveled, _config.Range);
}

internal float DistanceAlong(Vector3 worldPoint)
{
    if (_config == null) return float.MaxValue;
    Vector3 flat = worldPoint - _spawnPosition;
    flat.y = 0f;
    Vector3 dir = _direction;
    dir.y = 0f;
    if (dir.sqrMagnitude < 0.0001f) return float.MaxValue;
    return Vector3.Dot(flat, dir.normalized);
}

internal float LateralDistance(Vector3 worldPoint)
{
    if (_config == null) return float.MaxValue;
    Vector3 flat = worldPoint - _spawnPosition;
    flat.y = 0f;
    Vector3 dir = _direction;
    dir.y = 0f;
    if (dir.sqrMagnitude < 0.0001f) return float.MaxValue;
    return Vector3.Cross(dir.normalized, flat).magnitude;
}
```

- [ ] **步骤 4：实现 SwordQiSpawner 停点选择**

`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs`：

0. 文件顶部补 `using System.Collections.Generic;`（当前没有，`List<T>` 需要）。

1. 字段区追加：

```csharp
private static readonly List<SwordQiProjectile> _active = new List<SwordQiProjectile>();
```

2. `Spawn` 末尾、`qi.OnSpawn(...)` 后追加：

```csharp
PruneActive();
_active.Add(qi);
```

3. 在 `Spawn` 后追加：

```csharp
private static void PruneActive()
{
    for (int i = _active.Count - 1; i >= 0; i--)
    {
        var qi = _active[i];
        if (qi == null || !qi.IsActive) _active.RemoveAt(i);
    }
}

/// <summary>联机：按服务端停点选择一发在飞的剑气，让它飞到停点后回池。
/// 优先未飞过的；全部已飞过时选刚飞过最少的一发。</summary>
public static void StopNearest(Vector3 worldPoint)
{
    PruneActive();
    if (_active.Count == 0) return;

    SwordQiProjectile best = null;
    float bestScore = float.MaxValue;
    bool anyNotPassed = false;

    foreach (var qi in _active)
    {
        float along = qi.DistanceAlong(worldPoint);
        bool passed = along < qi.Traveled - 1.5f;
        if (!passed) anyNotPassed = true;
        float score = qi.LateralDistance(worldPoint) + (passed ? 1000f : 0f);
        if (best == null
            || score < bestScore
            || (Mathf.Approximately(score, bestScore) && qi.Traveled < best.Traveled))
        {
            best = qi;
            bestScore = score;
        }
    }

    if (!anyNotPassed)
    {
        best = null;
        float leastPast = float.MaxValue;
        foreach (var qi in _active)
        {
            float past = qi.Traveled - qi.DistanceAlong(worldPoint);
            if (past < leastPast)
            {
                leastPast = past;
                best = qi;
            }
        }
    }

    best?.StopAtPoint(worldPoint);
}
```

4. `DisposeForTests` 开头追加：

```csharp
_active.Clear();
```

- [ ] **步骤 5：实现桥接事件与宿主接线**

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`：

1. 事件区（`OnDamageResult` 附近）追加：

```csharp
/// <summary>本机施放的 LineAll 剑气被格挡吸收（停点；客户端据此结束剑气特效）</summary>
public event Action<Vector3>? OnLineProjectileStopped;
```

2. `ApplyDamageResult` 的 `foreach` 循环结束后追加：

```csharp
if (result.ProjectileStopped
    && result.SourcePlayerId == localId
    && Cool.SyncCore.CombatRules.TryGet(result.SkillId, out var stopEntry)
    && stopEntry.Projectile?.Kind == Cool.SyncCore.ProjectileKind.LineAll)
{
    OnLineProjectileStopped?.Invoke(new Vector3(result.StopX, 0f, result.StopZ));
}
```

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`：

1. `HandleEnterWorld` 中 `_combatBridge.OnCastBroadcast += ...` 之后追加：

```csharp
_combatBridge.OnLineProjectileStopped += HandleLineProjectileStopped;
```

2. 在 `OnLocalChargeReleased` 后追加：

```csharp
private void HandleLineProjectileStopped(Vector3 point)
    => Hotfix.GameSystems.Skills.Runtime.SwordQi.SwordQiSpawner.StopNearest(point);
```

3. `DetachCombatBridge` 中 `_combatBridge = null;` 之前追加：

```csharp
_combatBridge.OnLineProjectileStopped -= HandleLineProjectileStopped;
```

- [ ] **步骤 6：运行 EditMode 定向测试**

```bash
unity-mcp-cli run-tool assets-refresh
unity-mcp-cli run-tool tests-run --testMode EditMode --testClass CombatNetworkBridgeTests
unity-mcp-cli run-tool tests-run --testMode EditMode --testClass SwordQiProjectileTests
unity-mcp-cli run-tool tests-run --testMode EditMode --testClass SwordQiSpawnerTests
```

预期：**全部 PASS**（桥接 5 条 + 弹体 3 条 + 生成器 3 条，连同既有用例）。

- [ ] **步骤 7：运行 EditMode 全量**

```bash
unity-mcp-cli run-tool tests-run --testMode EditMode
```

预期：**全部 PASS**。

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs \
        Assets/Scripts/Tests/Editor/SwordQiProjectileTests.cs \
        Assets/Scripts/Tests/Editor/SwordQiSpawnerTests.cs
git commit -m "feat(swordqi): 联机剑气按服务端停点飞到格挡者处消失"
```

---

## 任务 4：联机 E2E 与全量验证

**文件：**
- 修改：`Assets/Scripts/Tests/PlayMode/BlockMitigationE2ETests.cs`（在 `Blocked_Hit_Is_Mitigated_And_Full_Hit_Is_Not` 后追加）

- [ ] **步骤 1：写联机 E2E 测试**

先在 `Assets/Scripts/Tests/PlayMode/BlockMitigationE2ETests.cs` 顶部补 `using System.Linq;`（新用例使用 `.All(...)`）。然后在 `BlockMitigationE2ETests` 类中追加：

```csharp
[UnityTest]
public IEnumerator SwordQi_Block_Reports_Stop_To_Caster_And_IsBlocked_To_Target()
{
    var rig = new Rig();
    try
    {
        yield return ConnectAndWaitInWorld(rig.A, rig.B);
        Assert.IsTrue(CombatRules.TryGet(10002, out var entry), "10002 不在共享表内");
        long casterId = rig.A.Manager.LocalPlayerId;

        rig.Defending = true;
        yield return DriveFor(rig, 0.5f); // 覆盖至少数个上报周期

        rig.A.Adapter.Send(MessageId.CastRequest, new CastRequest
        {
            SkillId = 10002,
            TargetPlayerId = rig.B.Manager.LocalPlayerId,
            Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
        });

        float deadline = Time.realtimeSinceStartup + 10f;
        while (FindCast(rig.B.Damages, casterId, 0) == null
               && Time.realtimeSinceStartup < deadline)
        {
            DriveOneFrame(rig);
            yield return null;
        }

        var atTarget = FindCast(rig.B.Damages, casterId, 0);
        Assert.IsNotNull(atTarget, "B 没收到剑气伤害结果");
        Assert.AreEqual(2, atTarget!.Hits.Count, "近距离 10002 = 挥击 + 剑气两条命中");
        Assert.IsTrue(atTarget.Hits.All(h => h.IsBlocked), "两条命中都应被格挡");
        foreach (var hit in atTarget.Hits)
            AssertDamageInDomain(hit.Damage, entry!, blocked: true, "剑气格挡");

        yield return AwaitCasterCopy(rig, casterId, expectedCastIndex: 0);
        var atCaster = FindCast(rig.A.Damages, casterId, 0);
        Assert.IsNotNull(atCaster, "A 没收到自己的剑气结果");
        Assert.IsTrue(atCaster!.ProjectileStopped, "服务端未下发剑气停点");
        Assert.AreEqual(0f, atCaster.StopX, 0.01f);
        Assert.AreEqual(1.5f, atCaster.StopZ, 0.01f);
    }
    finally
    {
        rig.Dispose();
    }
}
```

- [ ] **步骤 2：启动服务端并运行 E2E**

终端 A：

```bash
dotnet run --project Server/Server3C.csproj
```

等待日志出现监听 8000 端口的记录。终端 B：

```bash
unity-mcp-cli run-tool tests-run --testMode PlayMode --testClass BlockMitigationE2ETests
```

预期：**2/2 PASS**（既有 10001 用例 + 新 10002 剑气用例）。

- [ ] **步骤 3：运行全量回归**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj
unity-mcp-cli run-tool tests-run --testMode EditMode
unity-mcp-cli run-tool tests-run --testMode PlayMode --testClass BlockStaminaTests
unity-mcp-cli run-tool tests-run --testMode PlayMode --testClass StaminaIntegrationTests
```

预期：服务端全绿、EditMode 全绿、`BlockStaminaTests` 11/11、`StaminaIntegrationTests` 全绿。

- [ ] **步骤 4：手动冒烟（可选但建议）**

1. 双端联机：A 对举盾的 B 施放 10002。
   - B 的精力环掉 30（挥击+剑气各 15）；
   - A 看到的剑气在 B 处消失；
   - B 身后的怪物不掉血。
2. 单机：按住防御挡怪物攻击。
   - 每次格挡精力 −15，持盾继续 −10/s；
   - 连续格挡不会“盾破/眩晕”，直到精力力竭强制收盾；
   - 死亡与复活后盾都是放下状态。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/BlockMitigationE2ETests.cs
git commit -m "test(combat): 联机剑气格挡停点端到端验收"
```

---

## 自检

**规格覆盖度**

| 规格章节 | 对应任务 |
|---|---|
| §3.1 盾资源模型（删耐久、15/次、力竭收盾） | 任务 1（服务端部分）+ 任务 2 |
| §3.2 剑气阻挡（距离排序、首个格挡者吸收、后续段作废、`pending.Origin`） | 任务 1 |
| §3.3 格挡精力成本（联机/单机、眩晕、致死顺序、两次=30） | 任务 2 |
| §3.4 剑气视觉（停点字段、桥接事件、`StopNearest`、`StopAtPoint`） | 任务 3 |
| §3.5 死亡/复活收盾 | 任务 2 |
| §4 架构与组件 | 任务 1–3 |
| §5 协议与兼容性 | 任务 1 |
| §6 边界与不变量 | 任务 1–3 的测试 |
| §7.1 服务端测试 | 任务 1 |
| §7.2 客户端 EditMode | 任务 1（协议 tag）+ 任务 3 |
| §7.3 客户端 PlayMode | 任务 2 |
| §7.4 联机 E2E | 任务 4 |
| §7.5 验收数字 | 任务 1–4 的断言 |
| §7.6 运行命令 | 各任务 + 任务 4 |
| §8 明确不做 | 无任务（守边界） |
| §9 风险与限制 | 规格文档登记 |

**占位符扫描：** 已检查，无“待定/TODO/后续实现/补充细节”，所有代码步骤均含实际代码。

**类型一致性：** `ProjectileStopped`/`StopX`/`StopZ`（协议）↔ `CastResolution.ProjectileStopped`/`StopPosition`（服务端）↔ `OnLineProjectileStopped(Vector3)`（客户端）↔ `SwordQiSpawner.StopNearest` ↔ `SwordQiProjectile.StopAtPoint`；`_blockStaminaCost`/`ConsumeBlockStamina` 在任务 2 内一致；`DistanceAlong`/`LateralDistance`/`Traveled`/`StopDistance`/`IsActive` 在任务 3 内一致。

---

## 执行交接

**计划已完成并保存到 `docs/superpowers/plans/2026-10-21-sword-qi-shield-block.md`。两种执行方式：**

**1. 子代理驱动（推荐）** - 每个任务调度一个新的子代理，任务间进行审查，快速迭代

**2. 内联执行** - 在当前会话中使用 executing-plans 执行任务，批量执行并设有检查点

**选哪种方式？**
