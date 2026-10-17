# 投射物类技能服务端命中判定 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让剑气（10002）、火球（20003）、蓄力火球（20004）、陨石（30001）在联网模式下由服务端裁决命中：协议补落点/蓄力进度，服务端用线形/圆形几何判定（分段、首中即停、蓄力缩放），客户端弹体不再本地结算伤害。

**架构：** 翻转 D-P4-1 —— 把四个技能加入服务端 `CombatRules` 表，并给 `SkillEntry` 挂 `ProjectileSpec`；`PendingCast` 扩展为**判定组**（多段、可提前终止）；`HitResolver` 新增三个纯函数判定原语；客户端在激活/释放时带上 `ImpactPoint`/`ChargeProgress`。规格见 `docs/superpowers/specs/2026-10-18-projectile-server-judgment-design.md`。

**技术栈：** Unity 2022.3.25f1、C#（Hotfix + AOT/Protocol + SyncCore 双端共享）、protobuf-net、xunit（`Server/Server.Tests`）、NUnit（Unity EditMode/PlayMode）、Unity MCP CLI。

**共享代码警告：** `Assets/Scripts/AOT/Protocol/**` 与 `Assets/Scripts/Hotfix/GameSystems/SyncCore/**` 被 `Server/Server3C.csproj` 以 `<Compile Include>` 链接（同批源文件）。**改它们 = 同时改客户端与服务器**：每步都要跑 `dotnet build Server/Server3C.csproj` 与 Unity 侧编译/测试。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` | 修改 | `CastRequest` 增加 `ImpactX/Y/Z`、`ChargeProgress` |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 修改 | `ProjectileKind`/`ProjectileSpec`、4 条表项、`ValidateProjectileRequest` |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs` | 修改 | 新增 `ResolveLineAll` / `ResolveLineNearest` / `ResolveCircle` |
| `Server/Handler/CastHandler.cs` | 修改 | `PendingCast` → 判定组；分段调度；起点捕获；首中即停；蓄力缩放 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | 修改 | `NotifySkillCast` 新参数；蓄力释放发送 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs` | 修改 | 新增 `OnChargeReleased` 事件 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 修改 | 把 `OnChargeReleased` 接到桥 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs` | 修改 | 联网时跳过本地伤害 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs` | 修改 | 同上 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorStrike.cs` | 修改 | 同上 |
| `Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs` | 修改 | 翻转 IsSynced、新增表项/校验断言 |
| `Server/Server.Tests/SyncCore/CombatRulesTests.cs` | 修改 | 同上（xunit 镜像） |
| `Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs` | 修改 | 三个新原语的用例 |
| `Server/Server.Tests/Handler/CombatResolverProjectileTests.cs` | 创建 | 四个技能的判定组调度（xunit，走 `Decide → DrainResolved`） |
| `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` | 修改 | 发送字段/释放点 |
| `Assets/Scripts/Tests/Editor/SyncLayer/NetworkModeDamageGuardTests.cs` | 创建 | 联网跳过本地弹体伤害 |

---

## 任务 1：协议字段 + 表项 + 请求校验

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Combat.cs:27-35`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs`、`Server/Server.Tests/SyncCore/CombatRulesTests.cs`

- [ ] **步骤 1：编写失败的测试（客户端 EditMode）**

在 `Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs` 中把两条钉死断言翻转为新事实，并新增断言：

```csharp
        [Test]
        public void test_projectile_skills_are_now_synced()
        {
            // D-P4-1 翻转（2026-10-18 规格）：投射物/蓄力/大招进表
            Assert.IsTrue(CombatRules.IsSynced(20003), "火球应进表（服务端线形判定）");
            Assert.IsTrue(CombatRules.IsSynced(20004), "蓄力火球应进表（释放点圆判定）");
            Assert.IsTrue(CombatRules.IsSynced(30001), "陨石应进表（落点圆判定）");
            Assert.IsFalse(CombatRules.IsSynced(999999), "未知技能仍不进表");
        }

        [Test]
        public void test_projectile_shape_caps_accept_the_shapes_clients_actually_send()
        {
            // 20003：ProjectileSkillData 无 ShapeBlock ⇒ 客户端发全零形状
            Assert.IsTrue(CombatRules.TryGet(20003, out var fireball));
            Assert.IsTrue(CombatRules.ValidateShape(new HitShape { ShapeType = 0 }, fireball));

            // 20004：资产 _targetType=1 / _range=2 / _areaRadius=2 / _angle=120
            Assert.IsTrue(CombatRules.TryGet(20004, out var charge));
            Assert.IsTrue(CombatRules.ValidateShape(
                new HitShape { ShapeType = 1, Range = 2f, AreaRadius = 2f, Angle = 120f }, charge));
            Assert.IsFalse(CombatRules.ValidateShape(
                new HitShape { ShapeType = 1, Range = 2f, AreaRadius = 2.01f, Angle = 120f }, charge),
                "放大 AOE 半径必须被拒");

            // 30001：资产 _targetType=0 / _range=2 / _angle=120
            Assert.IsTrue(CombatRules.TryGet(30001, out var meteor));
            Assert.IsTrue(CombatRules.ValidateShape(
                new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }, meteor));
        }

        [Test]
        public void test_projectile_table_values_match_client_assets()
        {
            // 表值 = 资产 _baseDamage / _criticalRateBonus / _cooldown（见规格 §5 的实证）
            Assert.IsTrue(CombatRules.TryGet(20003, out var fb));
            Assert.AreEqual(20f, fb.BaseDamage, 0.001f);
            Assert.AreEqual(0f, fb.CriticalRate, 0.001f);
            Assert.AreEqual(3f, fb.CooldownSeconds, 0.001f);

            Assert.IsTrue(CombatRules.TryGet(20004, out var cf));
            Assert.AreEqual(40f, cf.BaseDamage, 0.001f);
            Assert.AreEqual(6f, cf.CooldownSeconds, 0.001f);

            Assert.IsTrue(CombatRules.TryGet(30001, out var mt));
            Assert.AreEqual(80f, mt.BaseDamage, 0.001f);
            Assert.AreEqual(25f, mt.CooldownSeconds, 0.001f);
        }

        [Test]
        public void test_validate_projectile_request_checks_finite_and_distance()
        {
            Assert.IsTrue(CombatRules.TryGet(30001, out var meteor));
            var ok = new CastRequest { SkillId = 30001, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f };
            Assert.IsNull(CombatRules.ValidateProjectileRequest(ok, meteor, casterX: 0f, casterZ: 0f));

            var tooFar = new CastRequest { SkillId = 30001, ImpactX = 0f, ImpactY = 0f, ImpactZ = 31f };
            Assert.AreEqual("impact-too-far",
                CombatRules.ValidateProjectileRequest(tooFar, meteor, 0f, 0f));

            var nan = new CastRequest { SkillId = 30001, ImpactX = float.NaN, ImpactY = 0f, ImpactZ = 1f };
            Assert.AreEqual("impact-not-finite",
                CombatRules.ValidateProjectileRequest(nan, meteor, 0f, 0f));

            // 线形技能不校验落点
            Assert.IsTrue(CombatRules.TryGet(10002, out var heavy));
            Assert.IsNull(CombatRules.ValidateProjectileRequest(
                new CastRequest { SkillId = 10002, ImpactX = float.NaN }, heavy, 0f, 0f));
        }
```

服务端镜像 `Server/Server.Tests/SyncCore/CombatRulesTests.cs` 加同义用例（xunit，`Assert.True/Equal`），并把旧的 `Assert.False(CombatRules.IsSynced(20003))` / `(30001)` 两行删除或改为 `True`。

- [ ] **步骤 2：运行测试验证失败（红）**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"CombatRulesTests"}' --timeout 240000
dotnet test Server/Server.Tests --filter "FullyQualifiedName~CombatRulesTests"
```

预期：客户端至少 3 条失败（`IsSynced(20003)` 为 false、`TryGet(20003)` 为 false、`ValidateProjectileRequest` 不存在 → 编译失败）。**编译失败是预期的红**：先在步骤 3 落类型，再回到这里看断言级红。

- [ ] **步骤 3：实现**

`Combat.cs` 的 `CastRequest`：

```csharp
        [ProtoMember(3)] public HitShape Shape { get; set; } = new HitShape();

        /// <summary>落点（仅 CircleAtPoint 类技能使用：20004/30001）。服务端校验有限性与距施法者上限。</summary>
        [ProtoMember(4)] public float ImpactX { get; set; }
        [ProtoMember(5)] public float ImpactY { get; set; }
        [ProtoMember(6)] public float ImpactZ { get; set; }

        /// <summary>蓄力进度 [0,1]（仅 20004 使用；服务端钳制，NaN 视为 0）。</summary>
        [ProtoMember(7)] public float ChargeProgress { get; set; }
```

`CombatRules.cs` 增加类型与表项：

```csharp
    public enum ProjectileKind
    {
        LineAll = 0,            // 剑气：沿途全中（每目标一次）
        LineNearestSplash = 1,  // 火球：段内最近者 → 以其为中心溅射，后续段作废
        CircleAtPoint = 2,      // 蓄力火球 / 陨石：落点圆判定
    }

    public sealed class ProjectileSpec
    {
        public ProjectileKind Kind { get; }
        public float Length { get; }
        public float HalfWidth { get; }
        public float Radius { get; }
        public int Stages { get; }
        public float FirstDelaySeconds { get; }
        public float TotalFlightSeconds { get; }
        public float MaxImpactDistance { get; }
        public bool ChargeScales { get; }
        public float ChargeAreaMaxScale { get; }
        public float ChargeDamageMaxScale { get; }

        public ProjectileSpec(ProjectileKind kind, float length = 0f, float halfWidth = 0f, float radius = 0f,
            int stages = 1, float firstDelaySeconds = 0f, float totalFlightSeconds = 0f,
            float maxImpactDistance = 0f, bool chargeScales = false,
            float chargeAreaMaxScale = 1f, float chargeDamageMaxScale = 1f)
        {
            Kind = kind; Length = length; HalfWidth = halfWidth; Radius = radius;
            Stages = stages; FirstDelaySeconds = firstDelaySeconds; TotalFlightSeconds = totalFlightSeconds;
            MaxImpactDistance = maxImpactDistance; ChargeScales = chargeScales;
            ChargeAreaMaxScale = chargeAreaMaxScale; ChargeDamageMaxScale = chargeDamageMaxScale;
        }
    }
```

`SkillEntry` 增加字段并扩展构造函数（**只加一个可选参数，既有 4 条表项调用一字不改**）：

```csharp
        public ProjectileSpec? Projectile { get; }

        public SkillEntry(int skillId, int shapeType, float maxRange, float maxAreaRadius,
            float maxAngle, float cooldownSeconds, float baseDamage, float criticalRate,
            float hitDelaySeconds, ProjectileSpec? projectile = null)
        {
            // …既有 9 个赋值逐字保留…
            Projectile = projectile;
        }
```

表项按规格 §3.1 填（含 10002 的 spec、20003/20004/30001 三条新项）：

```csharp
            // 2026-10-18：投射物/蓄力进表（翻转 D-P4-1）。形状上限必须容纳客户端真实形状。
            [20003] = new SkillEntry(20003, shapeType: 0, maxRange: 0f, maxAreaRadius: 0f, maxAngle: 0f,
                cooldownSeconds: 3f, baseDamage: 20f, criticalRate: 0f, hitDelaySeconds: 0f,
                projectile: new ProjectileSpec(ProjectileKind.LineNearestSplash,
                    length: 15f, halfWidth: 0.6f, radius: 1.5f, stages: 3,
                    firstDelaySeconds: 0f, totalFlightSeconds: 15f / 18f)),
            [20004] = new SkillEntry(20004, shapeType: 1, maxRange: 2f, maxAreaRadius: 2f, maxAngle: 120f,
                cooldownSeconds: 6f, baseDamage: 40f, criticalRate: 0f, hitDelaySeconds: 0f,
                projectile: new ProjectileSpec(ProjectileKind.CircleAtPoint, radius: 2f,
                    maxImpactDistance: 30f, chargeScales: true,
                    chargeAreaMaxScale: 1.5f, chargeDamageMaxScale: 1.5f)),
            [30001] = new SkillEntry(30001, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f,
                cooldownSeconds: 25f, baseDamage: 80f, criticalRate: 0f, hitDelaySeconds: 0.8f,
                projectile: new ProjectileSpec(ProjectileKind.CircleAtPoint, radius: 4f, maxImpactDistance: 30f)),
```

10002 的表项追加 `projectile: new ProjectileSpec(ProjectileKind.LineAll, length: 12f, halfWidth: 2f, stages: 3, firstDelaySeconds: 0.35f, totalFlightSeconds: 12f / 18f)`。

新增校验：

```csharp
        /// <summary>落点/蓄力校验（仅 CircleAtPoint 需要落点）。返回 null = 通过，否则为拒绝原因。</summary>
        public static string ValidateProjectileRequest(CastRequest request, SkillEntry entry,
            float casterX, float casterZ)
        {
            var spec = entry.Projectile;
            if (spec == null || spec.Kind != ProjectileKind.CircleAtPoint) return null;
            if (!float.IsFinite(request.ImpactX) || !float.IsFinite(request.ImpactY)
                || !float.IsFinite(request.ImpactZ)) return "impact-not-finite";
            float dx = request.ImpactX - casterX;
            float dz = request.ImpactZ - casterZ;
            if (Math.Sqrt(dx * dx + dz * dz) > spec.MaxImpactDistance + 0.01f) return "impact-too-far";
            return null;
        }

        /// <summary>蓄力进度钳制：NaN/负 → 0，>1 → 1。</summary>
        public static float ClampChargeProgress(float raw)
            => float.IsFinite(raw) ? Math.Clamp(raw, 0f, 1f) : 0f;
```

- [ ] **步骤 4：运行测试验证通过（绿）**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"CombatRulesTests"}' --timeout 240000
dotnet test Server/Server.Tests --filter "FullyQualifiedName~CombatRulesTests"
```

预期：两边全绿。

- [ ] **步骤 5：全量 EditMode + 服务器全量**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode"}' --timeout 240000
dotnet test Server/Server.Tests
```

预期：0 失败（现有 `CombatRulesTests` 里别的断言不得被破坏）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/Messages/Combat.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs \
        Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs \
        Server/Server.Tests/SyncCore/CombatRulesTests.cs
git commit -m "feat(sync): 投射物技能进表 + 落点/蓄力请求校验（翻转 D-P4-1）"
```

正文写：根因（投射物从未服务端化）、表项数值来源（资产逐条相等）、形状上限容纳客户端真实形状、落点上限 30 是防篡改不是射程。

---

## 任务 2：`HitResolver` 判定原语

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `HitResolverTests.cs` 增加：

```csharp
        private static CombatCandidate C(long id, float x, float z) => new CombatCandidate(id, new Vec3(x, 0f, z), true);

        [Test]
        public void test_line_all_hits_band_and_skips_outside()
        {
            // yaw=0 → +Z；线从 (0,0) 起、长 10、半宽 1
            var cands = new List<CombatCandidate>
            {
                C(1, 0f, 5f),    // 带内
                C(2, 0.9f, 2f),  // 带内（贴边）
                C(3, 1.2f, 5f),  // 带外
                C(4, 0f, -3f),   // 背后
                C(5, 0f, 11f),   // 超出长度
            };
            var hits = HitResolver.ResolveLineAll(new Vec3(0f, 0f, 0f), 0f, 0f, 10f, 1f, null, cands);
            CollectionAssert.AreEquivalent(new long[] { 1, 2 }, hits);
        }

        [Test]
        public void test_line_all_respects_banned_ids()
        {
            var cands = new List<CombatCandidate> { C(1, 0f, 5f), C(2, 0f, 3f) };
            var hits = HitResolver.ResolveLineAll(new Vec3(0f, 0f, 0f), 0f, 0f, 10f, 1f, new HashSet<long> { 1 }, cands);
            CollectionAssert.AreEqual(new long[] { 2 }, hits);
        }

        [Test]
        public void test_line_nearest_returns_only_nearest_in_band()
        {
            var cands = new List<CombatCandidate> { C(1, 0f, 8f), C(2, 0.5f, 3f), C(3, 3f, 1f) };
            var nearest = HitResolver.ResolveLineNearest(new Vec3(0f, 0f, 0f), 0f, 0f, 8f, 1f, null, cands, out var hitPos);   // 带区 [0,8]：id=2 在 z=3 才是带内最近者
            Assert.AreEqual(2L, nearest);
            Assert.AreEqual(3f, hitPos.Z, 0.001f, "溅射中心 = 命中者位置");
        }

        [Test]
        public void test_circle_at_point_hits_inside_radius_only()
        {
            var cands = new List<CombatCandidate> { C(1, 3f, 0f), C(2, 4.5f, 0f), C(3, 0f, 4f) };
            var hits = HitResolver.ResolveCircle(new Vec3(0f, 0f, 0f), 4f, cands);
            CollectionAssert.AreEquivalent(new long[] { 1, 3 }, hits);
        }
```

- [ ] **步骤 2：运行验证失败**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"HitResolverTests"}' --timeout 240000
```

预期：编译失败（方法不存在）→ 视为红。

- [ ] **步骤 3：实现三个纯函数（追加到 `HitResolver`）**

```csharp
        /// <summary>线形判定：从 origin 沿 yaw、在 [startDistance, startDistance+length] × ±halfWidth 的带内全中。
        /// banned 里的 id 跳过（分组去重用）。纯 XZ。</summary>
        public static List<long> ResolveLineAll(Vec3 origin, float yawDeg, float startDistance, float length,
            float halfWidth, IReadOnlyCollection<long>? banned, IReadOnlyList<CombatCandidate> candidates)
        {
            var hits = new List<long>();
            if (candidates == null) return hits;
            float fx = (float)Math.Sin(yawDeg * Math.PI / 180.0);
            float fz = (float)Math.Cos(yawDeg * Math.PI / 180.0);
            foreach (var c in candidates)
            {
                if (!c.Alive) continue;
                if (banned != null && banned.Contains(c.Id)) continue;
                float dx = c.Position.X - origin.X;
                float dz = c.Position.Z - origin.Z;
                float along = dx * fx + dz * fz;
                if (along < startDistance || along > startDistance + length) continue;
                float lateral = Math.Abs(dx * fz - dz * fx);   // 2D 叉积的模 = 横向距离
                if (lateral > halfWidth) continue;
                hits.Add(c.Id);
            }
            return hits;
        }

        /// <summary>线形找最近者（同 LineAll 的带内过滤），返回 id 与命中位置；无命中返回 0。</summary>
        public static long ResolveLineNearest(Vec3 origin, float yawDeg, float startDistance, float length,
            float halfWidth, IReadOnlyCollection<long>? banned, IReadOnlyList<CombatCandidate> candidates,
            out Vec3 hitPosition)
        {
            hitPosition = default;
            if (candidates == null) return 0;
            float fx = (float)Math.Sin(yawDeg * Math.PI / 180.0);
            float fz = (float)Math.Cos(yawDeg * Math.PI / 180.0);
            long best = 0;
            float bestAlong = float.MaxValue;
            foreach (var c in candidates)
            {
                if (!c.Alive) continue;
                if (banned != null && banned.Contains(c.Id)) continue;
                float dx = c.Position.X - origin.X;
                float dz = c.Position.Z - origin.Z;
                float along = dx * fx + dz * fz;
                if (along < startDistance || along > startDistance + length) continue;
                if (Math.Abs(dx * fz - dz * fx) > halfWidth) continue;
                if (along < bestAlong) { best = c.Id; bestAlong = along; hitPosition = c.Position; }
            }
            return best;
        }

        /// <summary>以 center 为圆心的 AOE（与 ShapeType=1 同语义，供落点判定直接调用）。</summary>
        public static List<long> ResolveCircle(Vec3 center, float radius, IReadOnlyList<CombatCandidate> candidates)
        {
            var hits = new List<long>();
            if (candidates == null) return hits;
            foreach (var c in candidates)
            {
                if (!c.Alive) continue;
                if (DistXZ(center, c.Position) <= radius) hits.Add(c.Id);
            }
            return hits;
        }
```

- [ ] **步骤 4：运行验证通过**

同上命令，预期全绿。再跑 `dotnet build Server/Server3C.csproj`（共享代码必须双端编译）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs \
        Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs
git commit -m "feat(sync): HitResolver 增加线形/最近者/落点圆三种判定原语"
```

---

## 任务 3：服务端判定组调度（四个技能）

**文件：**
- 修改：`Server/Handler/CastHandler.cs`（`CombatResolver`）
- 创建：`Server/Server.Tests/Handler/CombatResolverProjectileTests.cs`

- [ ] **步骤 1：编写失败的测试（xunit；走 `Decide → DrainResolved` 真实路径）**

创建 `Server/Server.Tests/Handler/CombatResolverProjectileTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class CombatResolverProjectileTests
{
    /// <summary>施法者 = 玩家 1，位于原点、yaw 0（→ +Z）、满血存活（Position/Rotation 默认值即可）。
    /// 目标 = Npc（`AddNpc` 是 public 的位置入口；`PlayerState.Position` 是 internal set）。</summary>
    private static EntityStore MakeStore((float x, float z) targetAt, (float x, float z)? secondAt = null)
    {
        var store = new EntityStore();
        store.AddPlayer(1, "caster");
        store.AddNpc(2, "target", templateId: 1, hp: 100f, position: new Vector3(targetAt.x, 0f, targetAt.z));
        if (secondAt.HasValue)
            store.AddNpc(3, "second", 1, 100f, new Vector3(secondAt.Value.x, 0f, secondAt.Value.z));
        return store;
    }

    private static List<CombatResolver.CastResolution> Drain(CombatResolver resolver, uint untilTick)
    {
        var all = new List<CombatResolver.CastResolution>();
        for (uint t = 1; t <= untilTick; t++) all.AddRange(resolver.DrainResolved(t));
        return all;
    }

    [Fact]
    public void SwordQi_Line_Hits_Target_Outside_Swing_Cone()
    {
        // 目标在 6m 正前方：挥击锥（2.5m）打不到，剑气的 12m 线可以
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 6f)), new Random(1));
        Assert.True(resolver.Decide(1, new CastRequest { SkillId = 10002 }, tick: 0).Accepted);

        var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitIds);
    }

    [Fact]
    public void Fireball_Explodes_At_First_Target_And_Stops()
    {
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 4f), secondAt: (0f, 9f)), new Random(1));
        Assert.True(resolver.Decide(1, new CastRequest { SkillId = 20003 }, tick: 0).Accepted);

        var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitIds);
        Assert.DoesNotContain(3L, hitIds);   // 首中即停：后方 9m 的目标不该被同一次施法命中
    }

    [Fact]
    public void ChargeBlast_Scales_Radius_And_Damage_By_Charge()
    {
        // 目标距施法者 10m；落点 = 目标位置 ⇒ 40 × 1.5（满蓄力、无暴击）= 60
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var charge = new CastRequest
        {
            SkillId = 20004, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f, ChargeProgress = 1f
        };
        Assert.True(resolver.Decide(1, charge, tick: 0).Accepted);

        var hit = Drain(resolver, 20).SelectMany(r => r.Hits).FirstOrDefault(h => h.TargetPlayerId == 2L);
        Assert.NotNull(hit);
        Assert.Equal(60f, hit!.Damage, 0.001f);
    }

    [Fact]
    public void Meteor_Hits_Only_Inside_Impact_Radius()
    {
        // 落点 (0,0,4)；目标在 (0,0,0) 距落点 4m ⇒ 在半径边界内；另一个 (0,0,10) 距落点 6m ⇒ 外
        var store = MakeStore(targetAt: (0f, 0f), secondAt: (0f, 10f));
        var resolver = new CombatResolver(store, new Random(1));
        var meteor = new CastRequest { SkillId = 30001, ImpactX = 0f, ImpactY = 0f, ImpactZ = 4f };
        Assert.True(resolver.Decide(1, meteor, tick: 0).Accepted);

        var hitIds = Drain(resolver, 30).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitIds);
        Assert.DoesNotContain(3L, hitIds);
    }

    [Fact]
    public void Impact_Too_Far_Is_Rejected()
    {
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var far = new CastRequest { SkillId = 30001, ImpactX = 0f, ImpactY = 0f, ImpactZ = 31f };
        Assert.False(resolver.Decide(1, far, tick: 0).Accepted);
    }
}
```

> `EntityStore.AddPlayer` 只接受 `(id, name)`；`PlayerState.Position/Rotation` 是 `internal set`，**测试程序集不能直接写**——所以施法者固定放在原点（默认值与「原点 + yaw 0 → +Z」一致），目标一律用 `AddNpc` 的 public 位置参数放置。

- [ ] **步骤 2：运行验证失败**

```bash
dotnet test Server/Server.Tests --filter "FullyQualifiedName~CombatResolverTests"
```

预期：4 条失败（剑气不命中、火球无命中、蓄力伤害缺失、过远未拒）。

- [ ] **步骤 3：实现判定组**

`PendingCast` 由 `readonly struct` 改为**可变 class**（同一施法可排多段）：

```csharp
        private sealed class PendingCast
        {
            public readonly long CasterId;
            public readonly CastRequest Request;
            public readonly CastBroadcast Broadcast;
            public readonly bool IsNpcCaster;
            public readonly ProjectileSpec? Projectile;
            public readonly float Charge;
            public readonly uint FirstTick;
            public readonly uint[] StageTicks;              // 升序；无 Projectile 时长度 1 = {FirstTick}
            public int NextStage;                            // 下一段下标
            public readonly HashSet<long> GroupHitIds = new();  // LineAll 组内去重
            public bool Spent;                               // 火球首中即停
            public bool OriginCaptured;
            public Vec3 Origin;                              // 首段捕获的施法者位置
            public float Yaw;

            public PendingCast(long casterId, CastRequest request, CastBroadcast broadcast,
                bool isNpcCaster, ProjectileSpec? projectile, float charge, uint firstTick)
            {
                CasterId = casterId; Request = request; Broadcast = broadcast;
                IsNpcCaster = isNpcCaster; Projectile = projectile; Charge = charge;
                FirstTick = firstTick;
                StageTicks = BuildStageTicks(projectile, firstTick);
            }

            private static uint[] BuildStageTicks(ProjectileSpec? spec, uint firstTick)
            {
                if (spec == null || spec.Stages <= 1) return new[] { firstTick };
                var ticks = new uint[spec.Stages];
                for (int k = 0; k < spec.Stages; k++)
                    ticks[k] = firstTick + (uint)Math.Ceiling(
                        k * spec.TotalFlightSeconds / spec.Stages * CombatRules.TickRateHz);
                return ticks;
            }
        }
```

`Decide` 在通过既有校验后追加（并把入队改为 class 构造）：

```csharp
            // 2026-10-18：投射物落点/蓄力校验（只有 CircleAtPoint 需要落点）
            var projectileError = CombatRules.ValidateProjectileRequest(request, entry,
                caster.Position.X, caster.Position.Z);
            if (projectileError != null) return Reject(resolution, projectileError);
            var charge = CombatRules.ClampChargeProgress(request.ChargeProgress);
            var firstTick = tick + CombatRules.HitDelayTicks(entry);
```

```csharp
            _pending.Add(new PendingCast(casterId, request, resolution.Broadcast, resolution.IsNpcCaster,
                entry.Projectile, charge, firstTick));
```

`DrainResolved` 改为按段推进：

```csharp
            for (int read = 0; read < _pending.Count; read++)
            {
                var pending = _pending[read];
                bool keep = false;
                while (!pending.Spent
                       && pending.NextStage < pending.StageTicks.Length
                       && pending.StageTicks[pending.NextStage] <= tick)
                {
                    var resolution = ResolveStage(pending);
                    if (resolution != null && resolution.Hits.Count > 0)
                        (resolved ??= new List<CastResolution>()).Add(resolution);
                    pending.NextStage++;
                }
                if (!pending.Spent && pending.NextStage < pending.StageTicks.Length) keep = true;
                if (keep) _pending[write++] = pending;
            }
```

`ResolveStage(pending)`（替代原 `ResolveHits(pending)`；**无 `Projectile` 时走原逻辑，逐字不变**）：

```csharp
        private CastResolution? ResolveStage(PendingCast pending)
        {
            var caster = _store.GetPlayer(pending.CasterId);
            if (caster == null || !caster.IsAlive) return null;   // 施法者已死：整组丢弃
            if (!CombatRules.TryGet(pending.Request.SkillId, out var entry)) return null;

            var candidates = BuildCandidates(pending);            // 原 ResolveHits 里的候选构造，抽成方法
            var spec = pending.Projectile;
            List<long> hitIds;

            if (spec == null)
            {
                hitIds = HitResolver.Resolve(
                    new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z),
                    caster.Rotation, pending.Request.TargetPlayerId, pending.Request.Shape, candidates);
            }
            else
            {
                if (!pending.OriginCaptured)
                {
                    // 首段捕获一次：剑气/火球沿施法者当时的朝向，不随后续移动改变
                    pending.Origin = new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z);
                    pending.Yaw = caster.Rotation;
                    pending.OriginCaptured = true;
                }
                int stage = pending.NextStage;
                hitIds = spec.Kind switch
                {
                    ProjectileKind.LineAll => HitResolver.ResolveLineAll(
                        pending.Origin, pending.Yaw,
                        stage * spec.Length / spec.Stages, spec.Length / spec.Stages,
                        spec.HalfWidth, pending.GroupHitIds, candidates),
                    ProjectileKind.LineNearestSplash => ResolveNearestStage(pending, spec, stage, candidates),
                    ProjectileKind.CircleAtPoint => HitResolver.ResolveCircle(
                        new Vec3(pending.Request.ImpactX, pending.Request.ImpactY, pending.Request.ImpactZ),
                        ScaledRadius(spec, pending.Charge), candidates),
                    _ => new List<long>(),
                };
            }

            var resolution = new CastResolution
            {
                Accepted = true, IsNpcCaster = pending.IsNpcCaster, Broadcast = pending.Broadcast
            };
            foreach (var targetId in hitIds)
            {
                if (!pending.GroupHitIds.Add(targetId)) continue;   // 组内每目标一次
                var target = _store.GetPlayer(targetId);
                if (target == null) continue;
                float damage = RollDamage(entry, out bool isCritical);
                if (spec != null && spec.ChargeScales)
                    damage *= 1f + (spec.ChargeDamageMaxScale - 1f) * pending.Charge;
                // 把原 ResolveHits 里从 `bool blocked = target.IsDefending` 起、到
                // `resolution.Hits.Add(new HitEntry { … });` 止的整段，**原样搬进本循环体**，
                // 一个字符都不改（格挡条件、RollDamage 调用顺序、HitEntry 字段全部保持）。
            }
            if (spec != null && spec.Kind == ProjectileKind.LineNearestSplash && hitIds.Count > 0)
                pending.Spent = true;
            return resolution;
        }

        private static float ScaledRadius(ProjectileSpec spec, float charge)
            => spec.ChargeScales
                ? spec.Radius * (1f + (spec.ChargeAreaMaxScale - 1f) * charge)
                : spec.Radius;

        private List<long> ResolveNearestStage(PendingCast pending, ProjectileSpec spec, int stage,
            List<CombatCandidate> candidates)
        {
            long nearest = HitResolver.ResolveLineNearest(
                pending.Origin, pending.Yaw,
                stage * spec.Length / spec.Stages, spec.Length / spec.Stages,
                spec.HalfWidth, pending.GroupHitIds, candidates, out var hitPos);
            if (nearest == 0) return new List<long>();
            // 爆炸：以命中者位置为中心做半径 Round；组长已在 GroupHitIds 里，跳过重复
            var splash = HitResolver.ResolveCircle(hitPos, spec.Radius, candidates);
            splash.Remove(nearest);
            splash.Insert(0, nearest);
            return splash;
        }
```

> **注意**：`ResolveNearestStage` 的落点用**命中者位置**（`hitPos`），与客户端"首目标处爆炸"一致；`hitIds` 里的每个目标仍走一次独立掷骰（与客户端溅射对每个目标各 `TakeDamage` 一次一致）。

- 无 `Projectile` 的技能：`StageTicks = {firstTick}`、`ResolveStage` 走原 `HitResolver.Resolve` 分支 —— **行为逐字不变**（既有 4 条技能的全部断言必须继续绿）。

- [ ] **步骤 4：运行验证通过**

```bash
dotnet test Server/Server.Tests --filter "FullyQualifiedName~CombatResolverTests"
```

预期 4/4 绿。再跑 `dotnet test Server/Server.Tests` 全量 + Unity EditMode 全量（共享代码改动）。

- [ ] **步骤 5：Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Server.Tests/Handler/CombatResolverTests.cs
git commit -m "feat(server): 投射物判定组 —— 剑气分段/火球首中即停/蓄力与陨石落点圆"
```

---

## 任务 4：客户端发送点与本地伤害守卫

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 修改：`SwordQiProjectile.cs` / `FireballProjectile.cs` / `MeteorStrike.cs`
- 测试：`CombatNetworkBridgeTests.cs`、新建 `NetworkModeDamageGuardTests.cs`

- [ ] **步骤 1：编写失败的测试**

`CombatNetworkBridgeTests` 增加：

```csharp
        [Test]
        public void test_notify_skill_cast_carries_impact_and_charge()
        {
            // 沿用文件内既有的 RecordingClient（`Sent` 记录 (MessageId, IMessage)）
            var bridge = new CombatNetworkBridge(_client, () => 1L, _registry);
            bridge.NotifySkillCast(20004,
                new HitShape { ShapeType = 1, Range = 2f, AreaRadius = 2f, Angle = 120f },
                impactPoint: new Vector3(0f, 0f, 10f), chargeProgress: 0.5f);

            var cast = _client.Sent.Where(x => x.id == MessageId.CastRequest)
                                   .Select(x => (CastRequest)x.msg).Single();
            Assert.AreEqual(10f, cast.ImpactZ, 0.001f, "落点 Z 应写入");
            Assert.AreEqual(0.5f, cast.ChargeProgress, 0.001f, "蓄力进度应写入");
        }
```

（`CombatNetworkBridgeTests.cs` 顶部需要 `using System.Linq;`；现有 `RecordingClient.Sent` 是 `List<(MessageId id, IMessage msg)>`。）

新建 `NetworkModeDamageGuardTests`（EditMode）：断言三个弹体在 `NetworkMode.Online` 下 `Tick` 不调用本地伤害（用 `PhysicsRegistry` 注册一个假 `IDamageable` 监听 `TakeDamage` 次数），`Offline` 下调用一次；测试结束时恢复 `NetworkModeService`。

- [ ] **步骤 2：运行验证失败**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"CombatNetworkBridgeTests"}' --timeout 240000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"NetworkModeDamageGuardTests"}' --timeout 240000
```

- [ ] **步骤 3：实现**

1. `NotifySkillCast(int skillId, HitShape shape, Vector3? impactPoint = null, float chargeProgress = 0f)`：写 `ImpactX/Y/Z` 与 `ChargeProgress`；`IsSynced` 门控不变。
2. `SkillCoordinator`：新增 `public event Action<SkillData, float, Vector3> OnChargeReleased;`，在 `TryReleaseCharge` 成功后触发，`impactPoint = _currentSkill.TargetPositionOrOwner()`（在 `SkillExecutor` 上加一个只读属性：`_targetCharacter != null ? _targetCharacter.transform.position : _owner.transform.position`）；取消蓄力不触发。
3. `Sys3CEntry`：把 `OnChargeReleased` 接到桥（`(data, progress, point) => _combatBridge?.NotifySkillCast(data.SkillId, HitShapeConverter.FromSkillData(data), point, progress)`），并在 `OnSkillActivated` 触发处**跳过蓄力类**（`data is ChargedSkillData` 时不发）。
4. 三个弹体：**只在伤害入口加守卫**，移动/生命周期/表现一律不动（每个文件顶部加 `using Hotfix.GameSystems.SyncLayer;`）：

```csharp
// SwordQiProjectile.cs —— 整个检测函数短路（位置推进与回池仍照常）
        private void DetectAndDamage()
        {
            if (NetworkModeService.Load() == NetworkMode.Online) return;   // 联网：伤害权在服务端
            // …原代码…
        }

// FireballProjectile.cs —— 只短路伤害；命中/落地仍会走 Explode 放 VFX 与回池
        private void ApplyDamage(IDamageable target)
        {
            if (NetworkModeService.Load() == NetworkMode.Online) return;   // 联网：伤害权在服务端
            // …原代码…
        }

// MeteorStrike.cs —— 只跳过伤害循环；`ImpactVFX` 与回池照旧
            bool online = NetworkModeService.Load() == NetworkMode.Online;
            if (!online)
            {
                // …原 foreach 伤害循环逐字保留…
            }
```

- [ ] **步骤 4：运行验证通过**

同步骤 2 命令 + 全量 EditMode。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs \
        Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorStrike.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/NetworkModeDamageGuardTests.cs
git commit -m "feat(sync): 客户端上报落点/蓄力（释放时发送）+ 联网跳过本地弹体伤害"
```

---

## 任务 5：验收与回归

- [ ] **步骤 1：双端全量**

```bash
dotnet test Server/Server.Tests
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode"}' --timeout 240000
```

预期：0 失败。

- [ ] **步骤 2：变异检查（证明剑气用例有牙齿）**

把 `CombatRules` 里 10002 的 `ProjectileSpec.Length` 临时改成 `1f`，重跑任务 3 的 `SwordQi_Line_Hits_Target_Outside_Swing_Cone`：

```bash
dotnet test Server/Server.Tests --filter "FullyQualifiedName~SwordQi_Line"
```

预期：**红**。确认后还原并再跑一次（绿）。

- [ ] **步骤 3：手动/联机验收（需要你或用户启动服务器）**

1. 起服：`dotnet build Server/Server3C.csproj` → `dotnet run --no-build --project Server/Server3C.csproj -- 8000`（**不要重启用户已有的服务器**；若已有服务器在跑就复用它）。
2. 编辑器 PlayMode：联网进世界 → 站在怪物 3~10m 外放重击（10002）→ 观察：怪物**掉血 + 飘字**，且伤害发生在剑气飞到之后（不是按下瞬间）。
3. 放火球（20003）：首个目标处爆炸并只伤及该点 1.5m 内的目标。
4. 陨石（30001）：0.8s 后落点 4m AOE 命中。
5. 蓄力火球（20004）：满蓄力伤害约为半蓄力的 1.5 倍（可看飘字数字）。
6. 把观察结果（含数值）写进 `production/verify/` 下的短记录（不提交）。

- [ ] **步骤 4：收尾**

```bash
git status --porcelain   # 只应有用户既有改动 + 不提交的 verify 产物
```

---

## 自检（写计划后已执行）

| 规格条目 | 对应任务 |
|---|---|
| §2 协议两字段 + 校验 | 任务 1 |
| §3.1 表项/ProjectileSpec（形状上限、冷却、伤害、crit） | 任务 1 |
| §3.2 判定组、起点捕获、首中即停、蓄力缩放 | 任务 3 |
| §3.3 时序 | 任务 1（表值）+ 任务 3（调度） |
| §4 客户端发送点/GetShape/本地守卫 | 任务 4 |
| §5 伤害对齐 | 任务 1 步骤 1 的 `test_projectile_table_values_match_client_assets` |
| §6 测试计划（含变异检查） | 任务 1-5 |
| §7 不做 | 无任务（正确） |
| §8 边界 | 写进各任务提交正文与步骤 3 的记录 |

**占位符扫描**：本计划无「待定/TODO」；所有表值均来自资产实测（`_cooldown 3/6/25`、`_baseDamage 20/40/80`、`_criticalRateBonus 0`、形状 `targetType 1/0`、`_range 2`、`_areaRadius 2`、`WarnDuration 0.8`）。

**自检修正（写入计划时已做）**：①删除任务 4 测试里互斥的重复断言；②服务端测试落成具体文件（`CombatResolverProjectileTests.cs`），并用 `EntityStore` 的真实 public 入口——`PlayerState.Position/Rotation` 是 `internal set`、测试程序集写不了，所以施法者固定在原点；③补齐 `PendingCast` 的完整调度代码（阶段 tick 计算、起点捕获、首中即停、组内去重）；④三个弹体的守卫写成具体落点（剑气检测函数短路、火球只短路 `ApplyDamage`、陨石只跳过伤害循环），无占位片段。

**类型一致性**：`ProjectileSpec`/`ProjectileKind`/`ValidateProjectileRequest`/`ClampChargeProgress`/`ResolveLineAll`/`ResolveLineNearest`/`ResolveCircle`/`OnChargeReleased`/`NotifySkillCast(…, impactPoint, chargeProgress)` 在各任务间同名同签名。
