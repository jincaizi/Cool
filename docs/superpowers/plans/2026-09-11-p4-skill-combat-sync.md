# P4 技能与战斗同步 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 玩家技能 PvP 同步闭环：CastRequest → 服务端权威校验（冷却/射程/存活）+ 命中裁决（HitResolver 几何两端同源）→ 服务端掷骰伤害（D4）→ DamageResult 下发，双端伤害数字一致；伪造越射程/未冷却技能被拒绝并留审计日志。协议只加不删。

**Architecture:** 共享 SyncCore 增加 CombatRules（技能参数表：冷却/射程上限/形状类型/伤害/暴击——与客户端资产值一致）与 HitResolver（纯几何命中裁决：Single 自动目标/Circle）；Protocol 增加 HitShape/CastRequest/CastBroadcast/DamageResult（战斗 300 号段）；服务端 CombatResolver 校验+裁决+扣血（PlayerState 增 Hp/冷却记录），CastHandler 广播（caster+视野观察者收 CastBroadcast，caster+受击者收 DamageResult），拒绝静默+审计日志+计数；客户端 CombatNetworkBridge（技能激活发 CastRequest——本地表现即时播放不变；DamageResult → 本地玩家 ApplyServerDamage 走现有受击/浮字链、远程实体 Emit PlayerTakeDamageEvent 走现有 FloatText 链）。客户端本地怪物战斗保持本地权威不动（P5 才服务端化）。

**Tech Stack:** .NET 8 / Unity 2022.3（C#9）/ protobuf-net 3.2.56 / xUnit + NUnit(EditMode) + PlayMode

**Spec:** `docs/superpowers/specs/2026-08-27-network-sync-design.md` §5-P4 + D4（战斗骰只有服务器掷）

---

## 关键环境事实与执行决策（先读）

| 事实/决策 | 影响 |
|---|---|
| 客户端技能数据 = ScriptableObject 资产（`Assets/PreRes/SkillsCfg/*.asset`）——服务器读不到，须在 SyncCore 硬编码同值表 | T2 表值与资产实测一致（下表） |
| `SkillID` 枚举：LightAttack=10001 / HeavyAttack=10002 / BasicAttack3=10003 / SkillQ=20001 / SkillR=20002 / Fireball=20003 / ChargeBlast=20004 / Ultimate=30001 | 表键 |
| 资产实测（2026-09-11 提取，T2 表值唯一来源）：BA_Combo1(10001) Single range2 cd0 dmg50 crit0.15 / BA_Heavy(10002) Single range2.5 cd0.5 dmg90 crit0.15 / Instant_SkillQ(20001) Single range3.5 cd6 dmg120 crit0.2 / Spin_SkillR(20002) **AOE_Circle** areaRadius2.5 cd5 dmg160 crit0.25；manaCost 全 0；`_criticalDamageBonus` 全 0（暴击 = ×1.5） | T2 表 |
| `GameSettings.DamageFluctuation` 默认 **0**（客户端本地伤害波动关闭——服务端掷骰不叠波动即两端公式一致） | T3 RollDamage |
| 客户端 ConeShape 角度语义：`Angle` 为**全角**，半角余弦 `cos(Angle/2)` 与 forward 点积比较；`_targetType` 枚举：Single=0 / AOE_Circle=1 / AOE_Cone=2 / AOE_Sector=3（服务端 HitShape.ShapeType 同值——两端同源） | T2 HitResolver |
| `SkillCoordinator.OnSkillActivated(SkillData)` 在技能激活即触发（本地表现同步启动）；`SkillExecutor.GetShape()` 的取形模式 = Combo/Instant/Charged/Channeled/Spin 依次 as-cast | T4 出站钩子 |
| 远程实体 = 胶囊占位（`RemoteEntityRegistry` 创建 `CreatePrimitive(Capsule)`），**不是** IEffectTarget/IDamageable——客户端无法锁定远程玩家为目标 | 决策 D-P4-2 |
| `DisplayEventBridge` 已订阅 `PlayerTakeDamageEvent` → FloatTextRenderer 飘字（entityId = instance id）——远程伤害飘字直接 Emit 该事件即可挂现有链 | T4 入站 |
| 服务端 handler 主循环线程执行（R2）；`Server3C.ProcessPacket` case-switch 分发；`PositionSyncHandler.ValidateAndApply` 纯核心+薄壳模式是本计划 CombatResolver/CastHandler 的模板 | T3 |
| `MessageTypeRegistry` 静态注册表——**新消息必须注册**，`RegistryCompletenessTests` 全枚举断言兜底 | T1 |
| SyncCore.asmdef `references: []`——CombatRules 用 Protocol 的 HitShape 需加 `"Cool.Protocol"` 引用（Hotfix→AOT 单向合法，spec §2；两侧均 noEngineReferences） | T2 |
| 服务器 csproj / 测试 csproj 已 `Compile Include` SyncCore 与 Protocol 同批源文件——新文件自动双端编译 | T2/T3 |
| PlayMode 测试基线：MovementSync 4 + TwoClientWorld 1 + AoiWorld 1；Server.Tests 79；EditMode 166 | 各任务基准 |
| `unity-mcp-cli run-tool tests-run` 需 `--timeout 300000`（测试 >60s 超 CLI 默认——P3 教训） | T5 |
| **决策 D-P4-1（表范围）**：P4 表只含 4 个瞬发/近战技能（10001/10002/20001/20002）。投射物（Fireball/ChargeBlast 蓄力/Ultimate）不进表——服务器拒收为未知技能+审计；客户端桥对非表技能不发 CastRequest（本地表现照常）。理由：投射物飞行/蓄力阶段/引导 tick 的时序服务端化复杂度高，P4 最小闭环先通 | T2/T3/T4 |
| **决策 D-P4-2（目标）**：CastRequest.TargetPlayerId P4 客户端恒 0（远程胶囊不可锁定）。服务端 Single 形状：显式 targetId → 距离 ≤ Range；无 targetId → Range 内 + 施法者前方 ±Angle/2 锥内**最近**实体。**零距离退化**（双端同出生点 dist=0）视为锥内命中——客户端 `dir.normalized` 零向量会 NaN 漏判，服务端修正（偏差记录 D-P4-2a）；e2e 实测后改走显式 TargetPlayerId（类级连跑 ghost 干扰，见执行偏差记录 6） | T2/T3/T5 |
| **决策 D-P4-3（消耗）**：资产 manaCost 全 0——服务器**不做 mana 池**（YAGNI，无消费者无法测试）；spec"消耗校验"落为偏差记录，待有消耗技能时启用（协议零改动，表加字段即可） | T2 偏差 |
| **决策 D-P4-4（伤害）**：服务器掷骰 = `BaseDamage × (crit ? 1.5 : 1)`；波动 0 与客户端默认对齐（不为 0 时须同步入表）；D4 语义——客户端不掷战斗骰，伤害值随 DamageResult 下发 | T3 |
| **决策 D-P4-5（冷却）**：tick 差判定（15Hz 基准，`Ceil(cooldownS×15)`）；**空挥也记录冷却**（与客户端激活即冷却一致）；冷却状态存 PlayerState（登录重建即重置） | T3 |
| **决策 D-P4-6（广播）**：CastBroadcast → caster + caster AOI 视野内在线观察者（AoiGrid 现成语义）；DamageResult → caster + 每个在线受击者（双端数字一致性载体）。AOE 多命中打包一个 DamageResult | T3 |
| **决策 D-P4-7（施法者状态）**：落地为**存活校验**（服务器 HP 权威，死亡拒施法/不被选中）；快照 ActionState 标志位填充留 backlog（P4 验收不依赖） | T3 偏差 |
| **决策 D-P4-8（客户端边界）**：PvP 伤害走 Sys3CEntry.ApplyServerDamage **直扣**（绕过防御修正器——PvP 格挡减伤服务端化 backlog）；本地怪物战斗（本地权威 HP）不受影响——服务器 HP 只受 PvP 伤害，P5 怪物服务端化后闭合（偏差记录）。死亡无复活（P4 范围外，重登重建满血） | T4 偏差 |

---

### Task 1: Protocol 战斗消息（HitShape/CastRequest/CastBroadcast/DamageResult，300 号段）

**Files:**
- Create: `Assets/Scripts/AOT/Protocol/Messages/Combat.cs`
- Modify: `Assets/Scripts/AOT/Protocol/MessageId.cs`（300-302）
- Modify: `Assets/Scripts/AOT/Protocol/MessageCodec.cs`（MessageTypeRegistry 注册 3 条）
- Test: `Server/Server.Tests/Protocol/CombatMessagesTests.cs`

- [x] **Step 1: 写失败测试**

创建 `Server/Server.Tests/Protocol/CombatMessagesTests.cs`（完整内容；roundtrip 覆盖形状/目标/多命中；注册完整性由既有 RegistryCompletenessTests 全枚举兜底——不注册即红）：

```csharp
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class CombatMessagesTests
{
    [Fact]
    public void CastRequest_Roundtrip_Preserves_Shape_And_Target()
    {
        var request = new CastRequest
        {
            SkillId = 20001,
            TargetPlayerId = 7,
            Shape = new HitShape
            {
                ShapeType = 0, Range = 3.5f, Angle = 120f,
                InnerRadius = 0.8f, InnerAngle = 90f
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.CastRequest, request, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.CastRequest, id);
        var cast = Assert.IsType<CastRequest>(decoded);
        Assert.Equal(20001, cast.SkillId);
        Assert.Equal(7L, cast.TargetPlayerId);
        Assert.Equal(0, cast.Shape.ShapeType);
        Assert.Equal(3.5f, cast.Shape.Range, 4);
        Assert.Equal(120f, cast.Shape.Angle, 4);
        Assert.Equal(0.8f, cast.Shape.InnerRadius, 4);
        Assert.Equal(90f, cast.Shape.InnerAngle, 4);
    }

    [Fact]
    public void DamageResult_Roundtrip_Preserves_Hits()
    {
        var result = new DamageResult
        {
            SourcePlayerId = 1,
            SkillId = 20001,
            ServerTimestamp = 1_700_000_000_000_0000L,
            Hits =
            {
                new HitEntry
                {
                    TargetPlayerId = 2, Damage = 120f, IsCritical = false,
                    TargetRemainingHp = 0f, HitX = 1f, HitZ = 2f
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.DamageResult, result, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.DamageResult, id);
        var damage = Assert.IsType<DamageResult>(decoded);
        Assert.Single(damage.Hits);
        Assert.Equal(2L, damage.Hits[0].TargetPlayerId);
        Assert.Equal(120f, damage.Hits[0].Damage, 4);
        Assert.False(damage.Hits[0].IsCritical);
        Assert.Equal(0f, damage.Hits[0].TargetRemainingHp, 4);
    }

    [Fact]
    public void CastBroadcast_Roundtrip_Preserves_Caster_And_Shape()
    {
        var broadcast = new CastBroadcast
        {
            SkillId = 20002, CasterPlayerId = 3, TargetPlayerId = 0,
            CasterX = 10f, CasterY = 1f, CasterZ = 20f, CasterRotation = 90f,
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f },
            ServerTimestamp = 1_700_000_000_000_0000L
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.CastBroadcast, broadcast, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.CastBroadcast, id);
        var cast = Assert.IsType<CastBroadcast>(decoded);
        Assert.Equal(20002, cast.SkillId);
        Assert.Equal(3L, cast.CasterPlayerId);
        Assert.Equal(10f, cast.CasterX, 4);
        Assert.Equal(90f, cast.CasterRotation, 4);
        Assert.Equal(1, cast.Shape.ShapeType);
        Assert.Equal(2.5f, cast.Shape.AreaRadius, 4);
    }
}
```

- [x] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatMessagesTests"
```

Expected: CS0246（找不到 `CastRequest`/`HitShape`/`DamageResult`）。

- [x] **Step 3: 实现协议消息 + 注册**

创建 `Assets/Scripts/AOT/Protocol/Messages/Combat.cs`（完整内容；注释即语义契约）：

```csharp
using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 技能命中形状（spec §5-P4：判定几何两端同源）——数值契约：
    /// ShapeType 与客户端 ShapeBlock.TargetType 枚举同值（Single=0 / AOE_Circle=1 /
    /// AOE_Cone=2 / AOE_Sector=3），Angle 为全角。只加不删：新形状字段追加高位 tag。
    /// </summary>
    [ProtoContract]
    public class HitShape : IMessage
    {
        [ProtoMember(1)] public int ShapeType { get; set; }
        [ProtoMember(2)] public float Range { get; set; }
        [ProtoMember(3)] public float Angle { get; set; }
        [ProtoMember(4)] public float AngleStart { get; set; }
        [ProtoMember(5)] public float AngleEnd { get; set; }
        [ProtoMember(6)] public float InnerRadius { get; set; }
        [ProtoMember(7)] public float InnerAngle { get; set; }
        [ProtoMember(8)] public float AreaRadius { get; set; }
    }

    /// <summary>施法请求（客户端 → 服务器）。形状随包上线；服务端按 CombatRules 表校验上限（防篡改）。</summary>
    [ProtoContract]
    public class CastRequest : IMessage
    {
        [ProtoMember(1)] public int SkillId { get; set; }

        /// <summary>锁定目标（P4 客户端恒 0——服务端 Single 形状自动选最近前方实体，决策 D-P4-2）</summary>
        [ProtoMember(2)] public long TargetPlayerId { get; set; }

        [ProtoMember(3)] public HitShape Shape { get; set; } = new HitShape();
    }

    /// <summary>施法广播（服务器 → caster 及视野内观察者；远程表现驱动，P4 表现占位）</summary>
    [ProtoContract]
    public class CastBroadcast : IMessage
    {
        [ProtoMember(1)] public int SkillId { get; set; }
        [ProtoMember(2)] public long CasterPlayerId { get; set; }
        [ProtoMember(3)] public long TargetPlayerId { get; set; }
        [ProtoMember(4)] public float CasterX { get; set; }
        [ProtoMember(5)] public float CasterY { get; set; }
        [ProtoMember(6)] public float CasterZ { get; set; }
        [ProtoMember(7)] public float CasterRotation { get; set; }
        [ProtoMember(8)] public HitShape Shape { get; set; } = new HitShape();
        [ProtoMember(9)] public long ServerTimestamp { get; set; }
    }

    /// <summary>单次命中结果（D4：战斗骰只有服务器掷——伤害值随消息下发，客户端不重算）</summary>
    [ProtoContract]
    public class HitEntry : IMessage
    {
        [ProtoMember(1)] public long TargetPlayerId { get; set; }
        [ProtoMember(2)] public float Damage { get; set; }
        [ProtoMember(3)] public bool IsCritical { get; set; }

        /// <summary>扣血后目标剩余 HP（服务器权威）</summary>
        [ProtoMember(4)] public float TargetRemainingHp { get; set; }

        [ProtoMember(5)] public float HitX { get; set; }
        [ProtoMember(6)] public float HitZ { get; set; }
    }

    /// <summary>伤害结果（服务器 → caster + 每个受击者；双端伤害数字一致性载体）。AOE 多命中打包。</summary>
    [ProtoContract]
    public class DamageResult : IMessage
    {
        [ProtoMember(1)] public long SourcePlayerId { get; set; }
        [ProtoMember(2)] public int SkillId { get; set; }
        [ProtoMember(3)] public long ServerTimestamp { get; set; }
        [ProtoMember(4)] public List<HitEntry> Hits { get; set; } = new List<HitEntry>();
    }
}
```

`Assets/Scripts/AOT/Protocol/MessageId.cs`：`WorldSnapshot = 110,` 段后追加战斗段（精确改——注释段 `// ========== 聊天 (200-299) ==========` 之前插入）：

```csharp
        // ========== 战斗 (300-399) ==========
        /// <summary>施法请求（P4 追加；客户端 → 服务器）</summary>
        CastRequest = 300,

        /// <summary>施法广播（P4 追加；服务器 → caster 及视野观察者）</summary>
        CastBroadcast = 301,

        /// <summary>伤害结果（P4 追加；服务器 → caster + 受击者）</summary>
        DamageResult = 302,
```

同时把文件头注释的分段约定 `// 200-299 聊天 / 1000-1999 远程同步与房间 / 2000-2099 NPC` 更新为 `// 200-299 聊天 / 300-399 战斗 / 1000-1999 远程同步与房间 / 2000-2099 NPC`。

`Assets/Scripts/AOT/Protocol/MessageCodec.cs`：`MessageTypeRegistry` 静态构造内（`RegisterMessageType(MessageId.WorldSnapshot, typeof(WorldSnapshot));` 行后）追加：

```csharp
            RegisterMessageType(MessageId.CastRequest, typeof(CastRequest));
            RegisterMessageType(MessageId.CastBroadcast, typeof(CastBroadcast));
            RegisterMessageType(MessageId.DamageResult, typeof(DamageResult));
```

- [x] **Step 4: 运行确认通过 + 注册完整性**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatMessagesTests|FullyQualifiedName~RegistryCompletenessTests"
```

Expected: 3 + 2 passed（RegistryCompleteness 兜底新消息已注册——若漏注册此处即红）。

- [x] **Step 5: Commit**

```bash
git add Assets/Scripts/AOT/Protocol Server/Server.Tests/Protocol
git commit -m "feat(proto): combat messages 300-range - hit shape, cast request/broadcast, damage result"
```

---

### Task 2: SyncCore CombatRules（技能表）+ HitResolver（几何裁决）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncCore/SyncCore.asmdef`（references 加 `"Cool.Protocol"`）
- Test: `Server/Server.Tests/SyncCore/CombatRulesTests.cs` + `Server/Server.Tests/SyncCore/HitResolverTests.cs`
- Test: `Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs` + `Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs`（同份 NUnit 版——SyncCore 测试双端跑惯例）

- [x] **Step 1: 写失败测试（服务端 xUnit 版）**

创建 `Server/Server.Tests/SyncCore/CombatRulesTests.cs`（完整内容；表值 = 资产实测，见决策表）：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class CombatRulesTests
{
    [Fact]
    public void Table_Covers_P4_Synced_Skills()
    {
        Assert.True(CombatRules.IsSynced(10001));
        Assert.True(CombatRules.IsSynced(10002));
        Assert.True(CombatRules.IsSynced(20001));
        Assert.True(CombatRules.IsSynced(20002));
        Assert.False(CombatRules.IsSynced(20003)); // Fireball 投射物：P4 不进表（D-P4-1）
        Assert.False(CombatRules.IsSynced(30001)); // Ultimate
        Assert.False(CombatRules.IsSynced(999999));
    }

    [Fact]
    public void Table_Values_Match_Client_Assets()
    {
        // 2026-09-11 资产实测：BA_Combo1/BA_Heavy/Instant_SkillQ/Spin_SkillR（决策表）
        Assert.True(CombatRules.TryGet(10001, out var combo));
        Assert.Equal(0, combo.ShapeType);
        Assert.Equal(2f, combo.MaxRange, 4);
        Assert.Equal(0f, combo.CooldownSeconds, 4);
        Assert.Equal(50f, combo.BaseDamage, 4);
        Assert.Equal(0.15f, combo.CriticalRate, 4);

        Assert.True(CombatRules.TryGet(20001, out var q));
        Assert.Equal(0, q.ShapeType);
        Assert.Equal(3.5f, q.MaxRange, 4);
        Assert.Equal(6f, q.CooldownSeconds, 4);
        Assert.Equal(120f, q.BaseDamage, 4);

        Assert.True(CombatRules.TryGet(20002, out var r));
        Assert.Equal(1, r.ShapeType); // AOE_Circle
        Assert.Equal(2.5f, r.MaxAreaRadius, 4);
        Assert.Equal(5f, r.CooldownSeconds, 4);
        Assert.Equal(160f, r.BaseDamage, 4);
    }

    [Fact]
    public void ValidateShape_Accepts_Asset_Params()
    {
        Assert.True(CombatRules.TryGet(20001, out var q));
        var shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f };
        Assert.True(CombatRules.ValidateShape(shape, q));
    }

    [Fact]
    public void ValidateShape_Rejects_Inflated_Range()
    {
        Assert.True(CombatRules.TryGet(20001, out var q));
        var cheated = new HitShape { ShapeType = 0, Range = 999f, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(cheated, q));
    }

    [Fact]
    public void ValidateShape_Rejects_Wrong_Shape_Type()
    {
        Assert.True(CombatRules.TryGet(20001, out var q));
        var wrong = new HitShape { ShapeType = 1, Range = 3.5f, AreaRadius = 3.5f, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(wrong, q));
    }

    [Fact]
    public void ValidateShape_Rejects_Inflated_AreaRadius()
    {
        Assert.True(CombatRules.TryGet(20002, out var r));
        var cheated = new HitShape { ShapeType = 1, AreaRadius = 50f, Angle = 120f };
        Assert.False(CombatRules.ValidateShape(cheated, r));
    }

    [Fact]
    public void CooldownTicks_Rounds_Up_At_15Hz()
    {
        Assert.True(CombatRules.TryGet(10001, out var combo));
        Assert.Equal(0U, CombatRules.CooldownTicks(combo));    // 0s → 无冷却
        Assert.True(CombatRules.TryGet(10002, out var heavy));
        Assert.Equal(8U, CombatRules.CooldownTicks(heavy));    // 0.5s × 15 = 7.5 → 8
        Assert.True(CombatRules.TryGet(20001, out var q));
        Assert.Equal(90U, CombatRules.CooldownTicks(q));       // 6s × 15
        Assert.True(CombatRules.TryGet(20002, out var r));
        Assert.Equal(75U, CombatRules.CooldownTicks(r));       // 5s × 15
    }
}
```

创建 `Server/Server.Tests/SyncCore/HitResolverTests.cs`（完整内容；yaw 语义 = Unity 欧拉 Y：yaw=0 → forward=(0,0,+1)）：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class HitResolverTests
{
    private static CombatCandidate C(long id, float x, float z, bool alive = true)
        => new CombatCandidate(id, new Vec3(x, 0f, z), alive);

    // Single 形状：SkillQ range 3.5 / angle 120（全角 → 半角 60°）
    private static readonly HitShape Single35 = new HitShape
    {
        ShapeType = 0, Range = 3.5f, Angle = 120f
    };

    private static readonly HitShape Circle25 = new HitShape
    {
        ShapeType = 1, AreaRadius = 2.5f
    };

    [Fact]
    public void Single_Explicit_Target_In_Range_Hits()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 2f), C(3, 0f, 5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_Explicit_Target_Out_Of_Range_Misses()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 5f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Single_Explicit_Target_Dead_Misses()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 2f, alive: false) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
        Assert.Empty(hits);
    }

    [Fact]
    public void Single_AutoTarget_Picks_Nearest_In_Cone()
    {
        var candidates = new List<CombatCandidate> { C(2, 0f, 3f), C(3, 0f, 1f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
        Assert.Equal(new long[] { 3 }, hits); // 最近者
    }

    [Fact]
    public void Single_AutoTarget_Ignores_Behind_Caster()
    {
        // 身后实体（-Z）在 range 内但不在前方 ±60° 锥内；前方实体命中
        var candidates = new List<CombatCandidate> { C(2, 0f, -2f), C(3, 0f, 2f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
        Assert.Equal(new long[] { 3 }, hits);
    }

    [Fact]
    public void Single_AutoTarget_ZeroDistance_Degenerate_Hits()
    {
        // 双端同出生点（D-P4-2a 退化修正）：dist=0 视为锥内
        var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Single_AutoTarget_Respects_Caster_Yaw()
    {
        // yaw=90° → forward=(1,0,0)（+X）；仅 +X 侧实体在锥内
        var candidates = new List<CombatCandidate> { C(2, 2f, 0f), C(3, 0f, 2f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 90f, 0, Single35, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }

    [Fact]
    public void Circle_Hits_All_Within_Radius_Excludes_Far()
    {
        var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f), C(4, 10f, 0f) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
        Assert.Equal(new long[] { 2, 3 }, hits);
    }

    [Fact]
    public void Circle_Ignores_Dead()
    {
        var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f, alive: false) };
        var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
        Assert.Equal(new long[] { 2 }, hits);
    }
}
```

创建 `Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs`（完整内容；NUnit 版同份测试——SyncCore 测试双端跑惯例）：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class CombatRulesTests
    {
        [Test]
        public void Table_Covers_P4_Synced_Skills()
        {
            Assert.IsTrue(CombatRules.IsSynced(10001));
            Assert.IsTrue(CombatRules.IsSynced(10002));
            Assert.IsTrue(CombatRules.IsSynced(20001));
            Assert.IsTrue(CombatRules.IsSynced(20002));
            Assert.IsFalse(CombatRules.IsSynced(20003)); // Fireball 投射物：P4 不进表（D-P4-1）
            Assert.IsFalse(CombatRules.IsSynced(30001)); // Ultimate
            Assert.IsFalse(CombatRules.IsSynced(999999));
        }

        [Test]
        public void Table_Values_Match_Client_Assets()
        {
            // 2026-09-11 资产实测：BA_Combo1/BA_Heavy/Instant_SkillQ/Spin_SkillR（决策表）
            Assert.IsTrue(CombatRules.TryGet(10001, out var combo));
            Assert.AreEqual(0, combo.ShapeType);
            Assert.AreEqual(2f, combo.MaxRange, 0.0001f);
            Assert.AreEqual(0f, combo.CooldownSeconds, 0.0001f);
            Assert.AreEqual(50f, combo.BaseDamage, 0.0001f);
            Assert.AreEqual(0.15f, combo.CriticalRate, 0.0001f);

            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            Assert.AreEqual(0, q.ShapeType);
            Assert.AreEqual(3.5f, q.MaxRange, 0.0001f);
            Assert.AreEqual(6f, q.CooldownSeconds, 0.0001f);
            Assert.AreEqual(120f, q.BaseDamage, 0.0001f);

            Assert.IsTrue(CombatRules.TryGet(20002, out var r));
            Assert.AreEqual(1, r.ShapeType); // AOE_Circle
            Assert.AreEqual(2.5f, r.MaxAreaRadius, 0.0001f);
            Assert.AreEqual(5f, r.CooldownSeconds, 0.0001f);
            Assert.AreEqual(160f, r.BaseDamage, 0.0001f);
        }

        [Test]
        public void ValidateShape_Accepts_Asset_Params()
        {
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f };
            Assert.IsTrue(CombatRules.ValidateShape(shape, q));
        }

        [Test]
        public void ValidateShape_Rejects_Inflated_Range()
        {
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var cheated = new HitShape { ShapeType = 0, Range = 999f, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(cheated, q));
        }

        [Test]
        public void ValidateShape_Rejects_Wrong_Shape_Type()
        {
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var wrong = new HitShape { ShapeType = 1, Range = 3.5f, AreaRadius = 3.5f, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(wrong, q));
        }

        [Test]
        public void ValidateShape_Rejects_Inflated_AreaRadius()
        {
            Assert.IsTrue(CombatRules.TryGet(20002, out var r));
            var cheated = new HitShape { ShapeType = 1, AreaRadius = 50f, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(cheated, r));
        }

        [Test]
        public void CooldownTicks_Rounds_Up_At_15Hz()
        {
            Assert.IsTrue(CombatRules.TryGet(10001, out var combo));
            Assert.AreEqual(0U, CombatRules.CooldownTicks(combo));    // 0s → 无冷却
            Assert.IsTrue(CombatRules.TryGet(10002, out var heavy));
            Assert.AreEqual(8U, CombatRules.CooldownTicks(heavy));    // 0.5s × 15 = 7.5 → 8
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            Assert.AreEqual(90U, CombatRules.CooldownTicks(q));       // 6s × 15
            Assert.IsTrue(CombatRules.TryGet(20002, out var r));
            Assert.AreEqual(75U, CombatRules.CooldownTicks(r));       // 5s × 15
        }
    }
}
```

创建 `Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs`（完整内容；NUnit 版）：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class HitResolverTests
    {
        private static CombatCandidate C(long id, float x, float z, bool alive = true)
            => new CombatCandidate(id, new Vec3(x, 0f, z), alive);

        // Single 形状：SkillQ range 3.5 / angle 120（全角 → 半角 60°）
        private static readonly HitShape Single35 = new HitShape
        {
            ShapeType = 0, Range = 3.5f, Angle = 120f
        };

        private static readonly HitShape Circle25 = new HitShape
        {
            ShapeType = 1, AreaRadius = 2.5f
        };

        [Test]
        public void Single_Explicit_Target_In_Range_Hits()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 2f), C(3, 0f, 5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_Explicit_Target_Out_Of_Range_Misses()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.IsEmpty(hits);
        }

        [Test]
        public void Single_Explicit_Target_Dead_Misses()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 2f, alive: false) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.IsEmpty(hits);
        }

        [Test]
        public void Single_AutoTarget_Picks_Nearest_In_Cone()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 3f), C(3, 0f, 1f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 3 })); // 最近者
        }

        [Test]
        public void Single_AutoTarget_Ignores_Behind_Caster()
        {
            // 身后实体（-Z）在 range 内但不在前方 ±60° 锥内；前方实体命中
            var candidates = new List<CombatCandidate> { C(2, 0f, -2f), C(3, 0f, 2f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 3 }));
        }

        [Test]
        public void Single_AutoTarget_ZeroDistance_Degenerate_Hits()
        {
            // 双端同出生点（D-P4-2a 退化修正）：dist=0 视为锥内
            var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_AutoTarget_Respects_Caster_Yaw()
        {
            // yaw=90° → forward=(1,0,0)（+X）；仅 +X 侧实体在锥内
            var candidates = new List<CombatCandidate> { C(2, 2f, 0f), C(3, 0f, 2f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 90f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Circle_Hits_All_Within_Radius_Excludes_Far()
        {
            var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f), C(4, 10f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2, 3 }));
        }

        [Test]
        public void Circle_Ignores_Dead()
        {
            var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f, alive: false) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }
    }
}
```

- [x] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatRulesTests|FullyQualifiedName~HitResolverTests"
```

Expected: CS0246（找不到 `CombatRules`/`HitResolver`）。

- [x] **Step 3: 实现 CombatRules + HitResolver**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`（完整内容；表值与客户端资产一致——决策表为唯一来源）：

```csharp
using System;
using System.Collections.Generic;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>
    /// P4 技能规则表（spec §5-P4：CombatRules 冷却/消耗/射程判定，双端同源）。
    /// 表值 = 客户端 PreRes/SkillsCfg 资产实测（2026-09-11）：
    ///   BA_Combo1 (10001) Single  range 2    cd 0s   dmg 50  crit 15%
    ///   BA_Heavy  (10002) Single  range 2.5  cd 0.5s dmg 90  crit 15%
    ///   SkillQ    (20001) Single  range 3.5  cd 6s   dmg 120 crit 20%
    ///   Spin R    (20002) Circle  radius 2.5 cd 5s   dmg 160 crit 25%
    /// 其余技能（投射物/蓄力/引导/大招）P4 不进表——服务器拒收为未知技能（决策 D-P4-1）。
    /// 消耗（mana）资产全 0——服务器无 mana 池（YAGNI，决策 D-P4-3）。
    /// 伤害公式（D4：战斗骰只有服务器掷）：BaseDamage × (crit ? 1.5 : 1)；波动 0 与
    /// 客户端 GameSettings.DamageFluctuation 默认对齐（改该值须同步此处，见 T6 文档）。
    /// </summary>
    public sealed class SkillEntry
    {
        public int SkillId;
        public int ShapeType;        // HitShape.ShapeType（与客户端 TargetType 同值）
        public float MaxRange;       // Single 距离上限
        public float MaxAreaRadius;  // Circle 半径上限
        public float MaxAngle;       // 锥形全角上限（与 ShapeBlock.Angle 同语义）
        public float CooldownSeconds;
        public float BaseDamage;
        public float CriticalRate;
    }

    public static class CombatRules
    {
        public const float CritMultiplier = 1.5f;
        public const float TickRateHz = 15f;

        private static readonly Dictionary<int, SkillEntry> Table = new Dictionary<int, SkillEntry>
        {
            [10001] = new SkillEntry { SkillId = 10001, ShapeType = 0, MaxRange = 2f, MaxAreaRadius = 0f, MaxAngle = 120f, CooldownSeconds = 0f, BaseDamage = 50f, CriticalRate = 0.15f },
            [10002] = new SkillEntry { SkillId = 10002, ShapeType = 0, MaxRange = 2.5f, MaxAreaRadius = 0f, MaxAngle = 120f, CooldownSeconds = 0.5f, BaseDamage = 90f, CriticalRate = 0.15f },
            [20001] = new SkillEntry { SkillId = 20001, ShapeType = 0, MaxRange = 3.5f, MaxAreaRadius = 0f, MaxAngle = 120f, CooldownSeconds = 6f, BaseDamage = 120f, CriticalRate = 0.2f },
            [20002] = new SkillEntry { SkillId = 20002, ShapeType = 1, MaxRange = 2f, MaxAreaRadius = 2.5f, MaxAngle = 120f, CooldownSeconds = 5f, BaseDamage = 160f, CriticalRate = 0.25f },
        };

        public static bool IsSynced(int skillId) => Table.ContainsKey(skillId);

        public static bool TryGet(int skillId, out SkillEntry entry)
        {
            if (Table.TryGetValue(skillId, out var found)) { entry = found; return true; }
            entry = null!;
            return false;
        }

        /// <summary>形状合法性（防篡改扩大判定范围）：类型必须一致，Range/AreaRadius/Angle 不得超表上限</summary>
        public static bool ValidateShape(HitShape shape, SkillEntry entry)
        {
            if (shape == null) return false;
            if (shape.ShapeType != entry.ShapeType) return false;
            if (shape.Range > entry.MaxRange + 0.01f) return false;
            if (shape.AreaRadius > entry.MaxAreaRadius + 0.01f) return false;
            if (shape.Angle > entry.MaxAngle + 0.01f) return false;
            return true;
        }

        /// <summary>冷却换算为 tick 数（15Hz 基准，向上取整）</summary>
        public static uint CooldownTicks(SkillEntry entry) =>
            (uint)Math.Ceiling(entry.CooldownSeconds * TickRateHz);
    }
}
```

创建 `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs`（完整内容；几何语义与客户端 ConeShape 对齐——全角半角余弦、XZ 平面判定）：

```csharp
using System;
using System.Collections.Generic;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>命中判定候选（服务端从权威实体状态映射——纯数据，双端同源编译）</summary>
    public readonly struct CombatCandidate
    {
        public readonly long Id;
        public readonly Vec3 Position;
        public readonly bool Alive;

        public CombatCandidate(long id, Vec3 position, bool alive)
        {
            Id = id;
            Position = position;
            Alive = alive;
        }
    }

    /// <summary>
    /// 服务端权威命中裁决（spec §5-P4：判定几何两端同源）——纯几何，零 UnityEngine 依赖。
    /// 语义与客户端 AttackShape 对齐（XZ 平面；Angle 全角，半角余弦比较）：
    ///  - Single（显式 targetId）：距离 ≤ Range；
    ///  - Single（targetId=0，P4 客户端恒 0）：Range 内 + 施法者前方 ±Angle/2 锥内最近实体；
    ///    零距离退化（dist≈0）视为锥内命中——客户端 normalized 零向量 NaN 漏判，服务端修正（D-P4-2a）；
    ///  - AOE_Circle：水平距离 ≤ AreaRadius。
    /// 只返回 Alive 候选；caster 由调用方排除（不传入自身）。
    /// </summary>
    public static class HitResolver
    {
        public static List<long> Resolve(Vec3 casterPos, float casterYawDeg, long targetId,
            HitShape shape, IReadOnlyList<CombatCandidate> candidates)
        {
            var hits = new List<long>();
            if (shape == null || candidates == null) return hits;

            if (shape.ShapeType == 1) // AOE_Circle
            {
                foreach (var c in candidates)
                {
                    if (!c.Alive) continue;
                    if (DistXZ(casterPos, c.Position) <= shape.AreaRadius)
                        hits.Add(c.Id);
                }
                return hits;
            }

            // Single
            if (targetId > 0)
            {
                foreach (var c in candidates)
                {
                    if (c.Id == targetId && c.Alive && DistXZ(casterPos, c.Position) <= shape.Range)
                    {
                        hits.Add(c.Id);
                        break;
                    }
                }
                return hits;
            }

            // 自动目标：Range 内 + 前方锥内最近者（yaw 语义 = Unity 欧拉 Y：yaw=0 → +Z）
            float forwardX = (float)Math.Sin(casterYawDeg * Math.PI / 180.0);
            float forwardZ = (float)Math.Cos(casterYawDeg * Math.PI / 180.0);
            float halfCos = (float)Math.Cos(shape.Angle * 0.5f * Math.PI / 180.0);
            long best = 0;
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                if (!c.Alive) continue;
                float dx = c.Position.X - casterPos.X;
                float dz = c.Position.Z - casterPos.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist > shape.Range) continue;
                if (dist >= 0.0001f)
                {
                    float dot = (forwardX * dx + forwardZ * dz) / dist;
                    if (dot < halfCos) continue;
                }
                if (dist < bestDist) { best = c.Id; bestDist = dist; }
            }
            if (best != 0) hits.Add(best);
            return hits;
        }

        private static float DistXZ(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
```

`Assets/Scripts/Hotfix/GameSystems/SyncCore/SyncCore.asmdef`：`"references": []` 改为：

```json
    "references": [
        "Cool.Protocol"
    ],
```

（Hotfix→AOT 单向引用合法（spec §2）；两 asmdef 均 `noEngineReferences: true`，SyncCore 保持零引擎依赖。服务器侧 csproj 已同时编译两目录，无改动。）

- [x] **Step 4: 运行确认通过（双端）**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatRulesTests|FullyQualifiedName~HitResolverTests"
```

Expected: 7 + 9 = 16 passed。

```bash
unity-mcp-cli run-tool assets-refresh --input '{}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"CombatRulesTests"}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"HitResolverTests"}'
```

Expected: 7 + 9 = 16 passed（同份测试 NUnit 版）。

- [x] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore Server/Server.Tests/SyncCore Assets/Scripts/Tests/Editor/SyncCore
git commit -m "feat(synccore): combat rules table + hit resolver - shared geometry, asset-matched values"
```

---

### Task 3: 服务端 CombatResolver + CastHandler（权威校验/裁决/掷骰/广播）

**Files:**
- Create: `Server/Handler/CastHandler.cs`（含 CombatResolver 纯核心 + CastResolution + RollDamage）
- Modify: `Server/Player/PlayerState.cs`（Hp/MaxHp/ApplyDamage/RecordCast/IsCastReady）
- Modify: `Server/Aoi/AoiBroadcaster.cs`（public GetVisible）
- Modify: `Server/Server3C.cs`（CurrentTick / GetVisiblePlayers / _castHandler 接线 / ProcessPacket case）
- Test: `Server/Server.Tests/Combat/CastHandlerTests.cs`

- [x] **Step 1: 写失败测试**

创建 `Server/Server.Tests/Combat/CastHandlerTests.cs`（完整内容；CombatResolver 纯核心直测——无 Server3C/session 依赖，同 PositionSyncHandler.ValidateAndApply 测试模式）：

```csharp
using System.Linq;
using Cool.Protocol;
using Xunit;

namespace KcpServer.Tests;

public class CastHandlerTests
{
    private static (EntityStore store, CombatResolver resolver) Setup(int seed = 12345)
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new System.Random(seed));
        return (store, resolver);
    }

    private static PlayerState Spawn(EntityStore store, long id, float x, float z, float rotY = 0f)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.UpdatePosition(new Vector3(x, 0f, z), rotY, 0f, 1L, 0U);
        return p;
    }

    private static CastRequest Q(float range = 3.5f, int shapeType = 0, long targetId = 0)
        => new CastRequest
        {
            SkillId = 20001,
            TargetPlayerId = targetId,
            Shape = new HitShape { ShapeType = shapeType, Range = range, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f }
        };

    [Fact]
    public void Reject_Unknown_Skill()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);
        var request = new CastRequest { SkillId = 30001, Shape = Q().Shape };

        var resolution = resolver.ResolveCast(caster.PlayerId, request, 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("unknown-skill", resolution.RejectReason);
    }

    [Fact]
    public void Reject_Caster_Dead()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);
        caster.ApplyDamage(100f); // 击杀

        var resolution = resolver.ResolveCast(1, Q(), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("caster-dead", resolution.RejectReason);
    }

    [Fact]
    public void Reject_On_Cooldown()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        Assert.True(resolver.ResolveCast(1, Q(), 10).Accepted);
        Assert.False(resolver.ResolveCast(1, Q(), 11).Accepted);      // 冷却 90 tick 内
        Assert.Equal("cooldown", resolver.ResolveCast(1, Q(), 11).RejectReason);
        Assert.True(resolver.ResolveCast(1, Q(), 10 + 90).Accepted);  // 冷却到
    }

    [Fact]
    public void Reject_Inflated_Range_Shape()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        var resolution = resolver.ResolveCast(1, Q(range: 999f), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("shape-tampered", resolution.RejectReason);
    }

    [Fact]
    public void Reject_Wrong_Shape_Type()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 2);

        var resolution = resolver.ResolveCast(1, Q(shapeType: 1), 10);

        Assert.False(resolution.Accepted);
        Assert.Equal("shape-tampered", resolution.RejectReason);
    }

    [Fact]
    public void Accept_Single_Hits_Target_And_Applies_Server_Damage()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        var target = Spawn(store, 2, 0, 2); // Q range 3.5 内

        var resolution = resolver.ResolveCast(1, Q(), 10);

        Assert.True(resolution.Accepted);
        var hit = Assert.Single(resolution.Hits);
        Assert.Equal(2L, hit.TargetPlayerId);
        Assert.True(hit.Damage == 120f || hit.Damage == 180f,
            $"damage {hit.Damage} 不在服务端掷骰域 {{120, 180}}");
        Assert.Equal(hit.Damage > 120f, hit.IsCritical);
        Assert.Equal(100f - hit.Damage, hit.TargetRemainingHp, 4);
        Assert.Equal(100f - hit.Damage, target.Hp, 4); // 服务器权威扣血
        Assert.NotNull(resolution.Broadcast);
        Assert.Equal(1L, resolution.Broadcast!.CasterPlayerId);
        Assert.Equal(20001, resolution.Broadcast.SkillId);
    }

    [Fact]
    public void Circle_Hits_Multiple_Excludes_Dead_And_Far()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 1, 0);
        Spawn(store, 3, 0, 2);
        var far = Spawn(store, 4, 10, 0);
        var dead = Spawn(store, 5, 0, 1);
        dead.ApplyDamage(100f);

        var request = new CastRequest
        {
            SkillId = 20002,
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f }
        };
        var resolution = resolver.ResolveCast(1, request, 10);

        Assert.True(resolution.Accepted);
        Assert.Equal(new long[] { 2, 3 }, resolution.Hits.Select(h => h.TargetPlayerId).OrderBy(id => id).ToArray());
        Assert.Equal(100f, far.Hp, 4); // 圈外不受伤
    }

    [Fact]
    public void Whiff_No_Target_Accepted_With_No_Hits()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 10, 0); // range 外

        var resolution = resolver.ResolveCast(1, Q(), 10);

        Assert.True(resolution.Accepted);
        Assert.Empty(resolution.Hits);
        Assert.NotNull(resolution.Broadcast); // 空挥也广播表现
    }

    [Fact]
    public void Cooldown_Records_Even_On_Whiff()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);

        Assert.True(resolver.ResolveCast(1, Q(), 10).Accepted);   // 空挥
        Assert.False(resolver.ResolveCast(1, Q(), 11).Accepted);  // 立即再施 → 冷却拒绝（D-P4-5）
    }

    [Fact]
    public void Zero_Distance_AutoTarget_Hits()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        Spawn(store, 2, 0, 0); // 同出生点（双端 e2e 场景）

        var resolution = resolver.ResolveCast(1, Q(), 10);

        Assert.True(resolution.Accepted);
        var hit = Assert.Single(resolution.Hits);
        Assert.Equal(2L, hit.TargetPlayerId);
    }

    [Fact]
    public void Dead_Target_Excluded_From_Circle()
    {
        var (store, resolver) = Setup();
        var caster = Spawn(store, 1, 0, 0);
        var dead = Spawn(store, 2, 1, 0);
        dead.ApplyDamage(100f);

        var request = new CastRequest
        {
            SkillId = 20002,
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f }
        };
        var resolution = resolver.ResolveCast(1, request, 10);

        Assert.True(resolution.Accepted); // 施法本身合法
        Assert.Empty(resolution.Hits);
    }
}
```

- [x] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CastHandlerTests"
```

Expected: CS0246（找不到 `CombatResolver`）。

- [x] **Step 3: 实现 PlayerState 战斗状态**

`Server/Player/PlayerState.cs`：`LastReceivedSequence` 属性后、`IsDirty` 前插入（完整内容）：

```csharp
        /// <summary>P4 战斗状态（服务器权威；登录重建 PlayerState 即满血——无复活，见 D-P4-8）</summary>
        public float Hp { get; internal set; } = 100f;
        public float MaxHp { get; internal set; } = 100f;
        public bool IsAlive => Hp > 0f;
        private readonly System.Collections.Generic.Dictionary<int, uint> _lastCastTicks =
            new System.Collections.Generic.Dictionary<int, uint>();

        /// <summary>服务器权威扣血（命中裁决调用；钳 0）</summary>
        public void ApplyDamage(float damage) => Hp = Hp - damage < 0f ? 0f : Hp - damage;

        /// <summary>记录技能最后施放 tick（冷却判定；主循环独占访问）</summary>
        public void RecordCast(int skillId, uint tick) => _lastCastTicks[skillId] = tick;

        /// <summary>冷却就绪判定（tick 差 ≥ 冷却 tick 数；无记录 = 就绪。uint wrap 安全：15Hz 下 ~9.1 年）</summary>
        public bool IsCastReady(int skillId, uint tick, uint cooldownTicks)
            => !_lastCastTicks.TryGetValue(skillId, out var last) || tick - last >= cooldownTicks;
```

- [x] **Step 4: 实现 CombatResolver + CastHandler**

创建 `Server/Handler/CastHandler.cs`（完整内容；ResolveCast 纯核心可单测，HandleCastAsync 薄壳——同 PositionSyncHandler 模式）：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// P4 服务端权威战斗裁决核心（spec §5-P4）——校验（已知技能/存活/冷却/形状上限）
    /// → HitResolver 几何命中 → D4 服务端掷骰 → 权威扣血 + 冷却记录。
    /// 纯核心不依赖 Server3C/session（单测直用）；主循环独占线程（R2），无锁。
    /// </summary>
    public sealed class CombatResolver
    {
        private readonly EntityStore _store;
        private readonly Random _rng;

        public CombatResolver(EntityStore store, Random? rng = null)
        {
            _store = store;
            _rng = rng ?? new Random();
        }

        /// <summary>施放裁决结果（输出载体）</summary>
        public sealed class CastResolution
        {
            public bool Accepted;
            public string RejectReason = string.Empty;
            public CastBroadcast? Broadcast;
            public readonly List<HitEntry> Hits = new List<HitEntry>();
        }

        /// <summary>校验 + 裁决 + 状态变更（冷却记录/扣血）。广播发送由 CastHandler 负责。</summary>
        public CastResolution ResolveCast(long playerId, CastRequest request, uint tick)
        {
            var resolution = new CastResolution();
            var caster = _store.GetPlayer(playerId);
            if (caster == null) return Reject(resolution, "caster-not-found");
            if (!caster.IsAlive) return Reject(resolution, "caster-dead");
            if (!CombatRules.TryGet(request.SkillId, out var entry))
                return Reject(resolution, "unknown-skill");
            if (!CombatRules.ValidateShape(request.Shape, entry))
                return Reject(resolution, "shape-tampered");
            if (!caster.IsCastReady(request.SkillId, tick, CombatRules.CooldownTicks(entry)))
                return Reject(resolution, "cooldown");

            // 候选 = 全部在线实体 − caster（数百实体规模毫秒级；AOI 粗筛留待实体量大时再上）
            var candidates = new List<CombatCandidate>();
            foreach (var p in _store.All)
            {
                if (p.PlayerId == playerId) continue;
                candidates.Add(new CombatCandidate(p.PlayerId,
                    new Vec3(p.Position.X, p.Position.Y, p.Position.Z), p.IsAlive));
            }
            var hitIds = HitResolver.Resolve(
                new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z),
                caster.Rotation, request.TargetPlayerId, request.Shape, candidates);

            // 冷却记录在裁决后（D-P4-5：空挥也冷却——与客户端激活即冷却一致）
            caster.RecordCast(request.SkillId, tick);

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

            resolution.Accepted = true;
            resolution.Broadcast = new CastBroadcast
            {
                SkillId = request.SkillId,
                CasterPlayerId = playerId,
                TargetPlayerId = request.TargetPlayerId,
                CasterX = caster.Position.X,
                CasterY = caster.Position.Y,
                CasterZ = caster.Position.Z,
                CasterRotation = caster.Rotation,
                Shape = request.Shape,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };
            return resolution;
        }

        /// <summary>D4：战斗骰只有服务器掷——暴击 ×1.5；波动 0 与客户端 GameSettings.DamageFluctuation 默认对齐（D-P4-4）</summary>
        private float RollDamage(SkillEntry entry, out bool isCritical)
        {
            isCritical = entry.CriticalRate > 0f && _rng.NextDouble() < entry.CriticalRate;
            return entry.BaseDamage * (isCritical ? CombatRules.CritMultiplier : 1f);
        }

        private static CastResolution Reject(CastResolution resolution, string reason)
        {
            resolution.RejectReason = reason;
            return resolution;
        }
    }

    /// <summary>
    /// P4 施放处理器（spec §5-P4）——CombatResolver 薄壳：session→playerId 映射 +
    /// 广播发送（D-P4-6：CastBroadcast → caster + 视野观察者；DamageResult → caster + 受击者）。
    /// 拒绝静默 + 审计日志 + 计数（验收口径）。主循环线程执行。
    /// </summary>
    public sealed class CastHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;
        private readonly CombatResolver _resolver;

        /// <summary>拒绝计数（验收/统计口径，与 AoiBroadcaster.TotalEntitySends 同惯例）</summary>
        public long RejectedCasts { get; private set; }

        public CastHandler(Server3C server, ILogger logger, CombatResolver resolver)
        {
            _server = server;
            _logger = logger;
            _resolver = resolver;
        }

        public async Task HandleCastAsync(KcpServerSession session, CastRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: session={session.SessionId} skill={request.SkillId} reason=not-logged-in");
                return;
            }

            var resolution = _resolver.ResolveCast(playerId.Value, request, _server.CurrentTick);
            if (!resolution.Accepted)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: player={playerId.Value} skill={request.SkillId} reason={resolution.RejectReason}");
                return;
            }

            // CastBroadcast → caster + caster AOI 视野内观察者（第三方看到技能表现；AoiGrid 现成语义）
            var castRecipients = new HashSet<long> { playerId.Value };
            foreach (var p in _server.GetVisiblePlayers(playerId.Value))
                castRecipients.Add(p.PlayerId);
            foreach (var recipientId in castRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.CastBroadcast, resolution.Broadcast!);
            }

            // DamageResult → caster + 每个在线受击者（双端伤害数字一致性载体；AOE 打包）
            if (resolution.Hits.Count > 0)
            {
                var result = new DamageResult
                {
                    SourcePlayerId = playerId.Value,
                    SkillId = request.SkillId,
                    ServerTimestamp = DateTime.UtcNow.Ticks,
                    Hits = resolution.Hits
                };
                var damageRecipients = new HashSet<long> { playerId.Value };
                foreach (var hit in resolution.Hits)
                    damageRecipients.Add(hit.TargetPlayerId);
                foreach (var recipientId in damageRecipients)
                {
                    var s = _server.GetSession(recipientId);
                    if (s != null)
                        await _server.SendToSessionAsync(s, MessageId.DamageResult, result);
                }
            }
        }
    }
}
```

- [x] **Step 5: AoiBroadcaster + Server3C 接线**

`Server/Aoi/AoiBroadcaster.cs`：`RemoveObserver` 方法后追加：

```csharp
        /// <summary>观察者当前视野内实体（P4 CastHandler 广播范围复用 AoiGrid 迟滞视野语义）</summary>
        public List<PlayerState> GetVisible(long observerId, EntityStore store)
            => _grid.GetVisible(observerId, store.All);
```

`Server/Server3C.cs` 精确改（读文件后核对行号）：
- 文件头 using 区（`using System.Net.Sockets;` 行后）加 `using System.Collections.Generic;`
- 字段区（`private readonly AoiBroadcaster _aoi = new AoiBroadcaster();` 行后）加：

```csharp
        // P4：服务端权威战斗裁决（校验/命中/掷骰/扣血；广播经 CastHandler）
        private readonly CastHandler _castHandler;
```

- 属性区（`public int ActiveSessionCount => ...;` 行后）加：

```csharp
        /// <summary>当前主循环 tick（P4 冷却判定基准——tick 回调首行更新）</summary>
        public uint CurrentTick { get; private set; }
```

- 构造器（`_clockSyncHandler = new ClockSyncHandler(this, _logger);` 行后）加：

```csharp
            _castHandler = new CastHandler(this, _logger, new CombatResolver(_entityStore));
```

- tick 回调（`tick =>` lambda 块首行）加：

```csharp
                    CurrentTick = (uint)tick;
```

- `RemoveObserver` 方法后加：

```csharp
        /// <summary>观察者当前 AOI 可见实体（CastHandler 广播范围）</summary>
        public List<PlayerState> GetVisiblePlayers(long observerId) => _aoi.GetVisible(observerId, _entityStore);
```

- `ProcessPacket` switch（`case ClockSyncRequest clockSync:` 块后）加：

```csharp
                    case CastRequest cast:
                        await _castHandler.HandleCastAsync(session, cast);
                        break;
```

- [x] **Step 6: 运行确认通过 + 全量**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CastHandlerTests"
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
```

Expected: CastHandlerTests 11 passed；全量绿（79 → T1 +3 → T2 +16 → T3 +11 = **109**，以实际为准）；0 Errors。

- [x] **Step 7: Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Player/PlayerState.cs Server/Aoi/AoiBroadcaster.cs Server/Server3C.cs Server/Server.Tests/Combat
git commit -m "feat(server): authoritative cast resolution - validation, hit resolve, server-side damage roll, broadcast"
```

---

### Task 4: 客户端 CombatNetworkBridge + Sys3CEntry/Host 接线

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`（含 HitShapeConverter）
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`（SkillCoordinator 访问器 + ApplyServerDamage）
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（桥装配 + Update 驱动 + 清理）
- Test: `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs`

- [x] **Step 1: 写失败测试**

创建 `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs`（完整内容；RecordingClient 假实现 INetworkClient；桥无网络依赖——消息处理直调）：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Events;
using KcpNet;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// P4：战斗网络桥——出站（表内技能发 CastRequest）/入站（DamageResult → 本地扣血回调、
    /// 远程实体飘字事件走现有 PlayerTakeDamageEvent 链）。
    /// </summary>
    public class CombatNetworkBridgeTests
    {
        private sealed class RecordingClient : INetworkClient
        {
            public readonly List<(MessageId id, IMessage msg)> Sent = new List<(MessageId, IMessage)>();
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
                => Sent.Add((messageId, message));
            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;
            public void Dispose() { }
        }

        private GameObject _root = null!;
        private RecordingClient _client = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BridgeTestRoot");
            _client = new RecordingClient();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private static HitShape SkillQShape() => new HitShape
        {
            ShapeType = 0, Range = 3.5f, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f
        };

        [Test]
        public void NotifySkillCast_Sends_CastRequest_For_Synced_Skill()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);

            bridge.NotifySkillCast(20001, SkillQShape());

            Assert.AreEqual(1, _client.Sent.Count);
            Assert.AreEqual(MessageId.CastRequest, _client.Sent[0].id);
            var request = (CastRequest)_client.Sent[0].msg;
            Assert.AreEqual(20001, request.SkillId);
            Assert.AreEqual(0L, request.TargetPlayerId); // D-P4-2：客户端恒 0
            Assert.AreEqual(3.5f, request.Shape.Range, 0.001f);
        }

        [Test]
        public void NotifySkillCast_Ignores_Non_Synced_Skill()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);

            bridge.NotifySkillCast(30001, SkillQShape()); // Ultimate 不进表（D-P4-1）

            Assert.AreEqual(0, _client.Sent.Count);
        }

        [Test]
        public void HitShapeConverter_Maps_Asset_Values()
        {
            var combo = UnityEditor.AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Skills.Data.ComboSkillData>(
                "Assets/PreRes/SkillsCfg/BA_Combo1.asset");
            Assert.NotNull(combo, "BA_Combo1.asset 未找到——PreRes 路径核对");
            var shape = HitShapeConverter.FromSkillData(combo);
            Assert.AreEqual(0, shape.ShapeType);       // Single
            Assert.AreEqual(2f, shape.Range, 0.001f);
            Assert.AreEqual(120f, shape.Angle, 0.001f);
            Assert.AreEqual(0.8f, shape.InnerRadius, 0.001f);

            var spin = UnityEditor.AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Skills.Data.SpinSkillData>(
                "Assets/PreRes/SkillsCfg/Spin_SkillR.asset");
            Assert.NotNull(spin, "Spin_SkillR.asset 未找到——PreRes 路径核对");
            var spinShape = HitShapeConverter.FromSkillData(spin);
            Assert.AreEqual(1, spinShape.ShapeType);   // AOE_Circle
            Assert.AreEqual(2.5f, spinShape.AreaRadius, 0.001f);
        }

        [Test]
        public void OnDamageResult_LocalTarget_Invokes_Damage_Sink()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            long sinkTarget = 0; float sinkDamage = 0; bool sinkCrit = false;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir) =>
                {
                    sinkTarget = targetId;
                    sinkDamage = damage;
                    sinkCrit = isCritical;
                });

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 1, SkillId = 20001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 120f, IsCritical = false, TargetRemainingHp = 20f } }
            });
            bridge.Update();

            Assert.AreEqual(7L, sinkTarget);
            Assert.AreEqual(120f, sinkDamage, 0.001f);
            Assert.False(sinkCrit);
        }

        [Test]
        public void OnDamageResult_RemoteTarget_Emits_PlayerTakeDamageEvent()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            // 建立远程视图（ApplySnapshot 带 Position 位触发 CreateView）
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = 1f, Y = 0f, Z = 2f
            }, serverTicks: 1_000_000L);
            var view = registry.GetView(2);
            Assert.NotNull(view, "视图未创建——ApplySnapshot Position 位应触发");

            PlayerTakeDamageEvent? captured = null;
            Action<PlayerTakeDamageEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 20001,
                    Hits = { new HitEntry { TargetPlayerId = 2, Damage = 57.4f, IsCritical = true, TargetRemainingHp = 42.6f } }
                });
                bridge.Update();

                Assert.NotNull(captured, "远程受击未发 PlayerTakeDamageEvent");
                Assert.AreEqual(view.GameObject.GetInstanceID(), captured.Value.EntityId);
                Assert.AreEqual(58, captured.Value.Damage); // CeilToInt(57.4)
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }

        [Test]
        public void OnMessage_Ignores_Non_Combat_Messages()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => 1, registry);

            bridge.OnMessage(MessageId.WorldSnapshot, new WorldSnapshot());
            bridge.Update(); // 不抛、无消费

            Assert.AreEqual(0, _client.Sent.Count);
        }
    }
}
```

- [x] **Step 2: 运行确认失败**

```bash
unity-mcp-cli run-tool assets-refresh --input '{}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"CombatNetworkBridgeTests"}'
```

Expected: CS0246（找不到 `CombatNetworkBridge`）。

- [x] **Step 3: 实现 CombatNetworkBridge**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`（完整内容）：

```csharp
using System;
using System.Collections.Concurrent;
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using KcpNet;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// P4 战斗网络桥（spec §5-P4 客户端侧）：
    ///  - 出站：本地技能激活 → 表内技能发 CastRequest（形状随包上线，服务端校验上限）。
    ///    本地表现即时播放不变（预测反馈）；伤害数字等服务端结果（D-P4-8）。
    ///  - 入站：CastBroadcast → OnCastBroadcast 事件（P4 表现占位：宿主日志）；
    ///    DamageResult → 本地玩家走 onDamageToLocalPlayer 注入（宿主接 Sys3CEntry.ApplyServerDamage），
    ///    远程实体直接 Emit PlayerTakeDamageEvent——现有 DisplayEventBridge/FloatText 链自动飘字。
    /// 线程模型：adapter 回调线程只入队，Update（主线程）消费——与 ClientNetworkManager 同纪律。
    /// </summary>
    public sealed class CombatNetworkBridge
    {
        private readonly INetworkClient _client;
        private readonly Func<long> _localPlayerId;
        private readonly RemoteEntityRegistry _registry;
        private readonly Action<long, float, bool, Vector3>? _onDamageToLocalPlayer;
        private readonly ConcurrentQueue<IMessage> _incoming = new ConcurrentQueue<IMessage>();

        /// <summary>收到其他玩家施法（表现接线点；P4 宿主仅日志占位——远程实体为胶囊）</summary>
        public event Action<CastBroadcast>? OnCastBroadcast;

        /// <summary>收到伤害结果（扣血/飘字已内部处理；测试与上层观察用）</summary>
        public event Action<DamageResult>? OnDamageResult;

        public CombatNetworkBridge(INetworkClient client, Func<long> localPlayerId,
            RemoteEntityRegistry registry, Action<long, float, bool, Vector3>? onDamageToLocalPlayer = null)
        {
            _client = client;
            _localPlayerId = localPlayerId;
            _registry = registry;
            _onDamageToLocalPlayer = onDamageToLocalPlayer;
        }

        /// <summary>技能激活 → 表内技能发施法请求（非表技能静默忽略——本地表现不受影响，D-P4-1）</summary>
        public void NotifySkillCast(int skillId, HitShape shape)
        {
            if (!Cool.SyncCore.CombatRules.IsSynced(skillId)) return;
            _client.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = skillId,
                TargetPlayerId = 0, // D-P4-2：客户端不锁定远程目标
                Shape = shape
            });
        }

        /// <summary>网络线程回调入口（宿主接线 adapter.MessageReceived）——只入队</summary>
        public void OnMessage(MessageId messageId, IMessage message)
        {
            if (messageId == MessageId.CastBroadcast || messageId == MessageId.DamageResult)
                _incoming.Enqueue(message);
        }

        /// <summary>主线程消费（宿主每帧调用）</summary>
        public void Update()
        {
            while (_incoming.TryDequeue(out var message))
            {
                switch (message)
                {
                    case CastBroadcast broadcast:
                        OnCastBroadcast?.Invoke(broadcast);
                        break;
                    case DamageResult result:
                        ApplyDamageResult(result);
                        OnDamageResult?.Invoke(result);
                        break;
                }
            }
        }

        private void ApplyDamageResult(DamageResult result)
        {
            long localId = _localPlayerId();
            foreach (var hit in result.Hits)
            {
                // P4：协议无方向字段——飘字/受击不依赖朝向（方向字段留待命中细节阶段，偏差记录）
                if (hit.TargetPlayerId == localId)
                {
                    _onDamageToLocalPlayer?.Invoke(hit.TargetPlayerId, hit.Damage, hit.IsCritical, Vector3.zero);
                    continue;
                }
                var view = _registry.GetView(hit.TargetPlayerId);
                if (view == null) continue; // 视野外无视图（AOI 下不应发生——防御跳过）
                EventBus.Emit(new PlayerTakeDamageEvent(
                    view.GameObject.GetInstanceID(),
                    view.Position + Vector3.up * 1.5f,
                    HitZonePart.Torso,
                    Vector3.zero,
                    Mathf.CeilToInt(hit.Damage),
                    Color.white));
            }
        }
    }

    /// <summary>ShapeBlock → HitShape 协议映射（判定几何两端同源——数值直传，枚举值同）</summary>
    public static class HitShapeConverter
    {
        public static HitShape FromShapeBlock(ShapeBlock block) => new HitShape
        {
            ShapeType = (int)block.TargetType,
            Range = block.Range,
            Angle = block.Angle,
            AngleStart = block.AngleStart,
            AngleEnd = block.AngleEnd,
            InnerRadius = block.InnerRadius,
            InnerAngle = block.InnerAngle,
            AreaRadius = block.AreaRadius
        };

        /// <summary>SkillData → ShapeBlock（与 SkillExecutor.GetShape 同款取形模式）</summary>
        public static ShapeBlock? GetShape(SkillData data)
            => (data as ComboSkillData)?.Shape
                ?? (data as InstantSkillData)?.Shape
                ?? (data as ChargedSkillData)?.Shape
                ?? (data as ChanneledSkillData)?.Shape
                ?? (data as SpinSkillData)?.Shape;

        public static HitShape FromSkillData(SkillData data)
            => FromShapeBlock(GetShape(data) ?? new ShapeBlock());
    }
}
```

- [x] **Step 4: Sys3CEntry 访问器 + ApplyServerDamage**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`：
- `CoreController` 属性后加：

```csharp
        /// <summary>技能协调器（P4 网络宿主装配战斗桥接入点）</summary>
        public SkillCoordinator SkillCoordinator => _skillCoordinator;
```

- `IDamageable.TakeDamage` 两参方法前加（完整内容）：

```csharp
        /// <summary>
        /// P4 网络伤害入口（服务端权威——DamageResult 驱动）：绕过防御修正器直扣
        /// （PvP 格挡减伤服务端化是 P4 backlog，D-P4-8），受击表现/HitFSM/浮字事件复用
        /// 现有链路。怪物战斗（本地权威）不受影响。
        /// </summary>
        public void ApplyServerDamage(float damage, Vector3 hitDirection)
        {
            if (_currentHP <= 0) return;
            _currentHP = Mathf.Max(0f, _currentHP - damage);

            _fsmManager.HandleDamage(sourceId: -1, damage: damage, hitDirection: hitDirection,
                knockbackForce: 0f);
            if (_currentHP > 0)
            {
                EventBus.Emit(new PlayerTakeDamageEvent(
                    gameObject.GetInstanceID(),
                    transform.position + Vector3.up * 1.5f,
                    HitZonePart.Torso,
                    hitDirection,
                    Mathf.CeilToInt(damage),
                    Color.white));
            }
            if (_currentHP <= 0)
            {
                _skillCoordinator.ClearInputBuffer();
                _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
            }
        }
```

- [x] **Step 5: ClientNetworkHost 装配**

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` 精确改：
- 文件头 using 区加 `using Hotfix.GameSystems.Skills.Data;`
- 字段区（`private bool _entryLookedUp;` 行后）加：

```csharp
        private CombatNetworkBridge? _combatBridge;
```

- `Update()` 方法（`TryAttachLocalCharacter();` 行后）加：

```csharp
            _combatBridge?.Update();
```

- `TryAttachLocalCharacter` 成功路径（`_manager.OnCorrectionNeeded += _netPolicy.ApplyServerCorrection;` 行后、`Debug.Log("[NetHost] ...")` 行前）插入：

```csharp
            // P4 战斗桥：技能激活 → CastRequest；CastBroadcast/DamageResult → 表现/伤害挂现有事件链
            _combatBridge = new CombatNetworkBridge(_adapter!, () => _manager!.LocalPlayerId, _remoteEntities!,
                (targetId, dmg, crit, dir) => _localEntry.ApplyServerDamage(dmg, dir));
            _adapter!.MessageReceived += _combatBridge.OnMessage;
            _localEntry.SkillCoordinator.OnSkillActivated += OnLocalSkillCast;
            _combatBridge.OnCastBroadcast += b =>
                Debug.Log($"[NetHost] cast broadcast: caster={b.CasterPlayerId} skill={b.SkillId}");
```

- `Disconnect()` 方法（`_manager?.Dispose();` 行后、`_manager = null;` 行前）插入：

```csharp
            if (_combatBridge != null && _adapter != null)
            {
                _adapter.MessageReceived -= _combatBridge.OnMessage;
                if (_localEntry != null)
                    _localEntry.SkillCoordinator.OnSkillActivated -= OnLocalSkillCast;
                _combatBridge = null;
            }
```

- 类末尾（`OnDestroy` 前）加：

```csharp
        private void OnLocalSkillCast(SkillData skillData)
        {
            if (_combatBridge == null) return;
            _combatBridge.NotifySkillCast(skillData.SkillId, HitShapeConverter.FromSkillData(skillData));
        }
```

- [x] **Step 6: 运行确认通过**

```bash
unity-mcp-cli run-tool assets-refresh --input '{}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"CombatNetworkBridgeTests"}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}'
```

Expected: CombatNetworkBridgeTests 6 passed；EditMode 全量 166 → T2 +16 → T4 +6 = **188** 绿（以实际为准）。

- [x] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs
git commit -m "feat(synclayer): combat network bridge - cast request on skill activation, server damage to existing hit/float-text chains"
```

---

### Task 5: 端到端验收（双端伤害一致 + 作弊拒绝 + 审计日志留档）

**Files:**
- Create: `Assets/Scripts/Tests/PlayMode/CombatSyncTests.cs`

- [x] **Step 1: 写失败测试（PlayMode）**

创建 `Assets/Scripts/Tests/PlayMode/CombatSyncTests.cs`（完整内容；模式对齐 TwoClientWorldTests：墙钟上限、`(bool, reason)`、finally 清理；服务器裸起无 bot）：

```csharp
using System.Collections;
using System.Collections.Generic;
using Cool.Protocol;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P4 核心验收（spec §5-P4）：
    /// ① 双客户端伤害数字一致且以服务器为准（A 施法 → 双方 DamageResult 同值，B 余血 = 100 − 伤害）；
    /// ② 伪造越射程/未冷却技能被服务器拒绝（客户端无任何战斗消息到达；
    ///    服务器审计日志另留档 production/verify/cast-audit.log，见 Step 4）。
    /// 双客户端同出生点 (0,0,0)——Single 自动目标零距离退化命中（D-P4-2a）。
    /// </summary>
    public class CombatSyncTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public readonly List<CastBroadcast> Casts = new List<CastBroadcast>();
            public readonly List<DamageResult> Damages = new List<DamageResult>();
        }

        [UnityTest]
        public IEnumerator TwoClients_Damage_Numbers_Match_Server_Authority()
        {
            var root = new GameObject("CombatSyncRoot");
            var ctxA = new Ctx();
            var ctxB = new Ctx();
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, new RemoteEntityRegistry(root.transform), "Caster_A");
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, new RemoteEntityRegistry(root.transform), "Target_B");
            Capture(ctxA);
            Capture(ctxB);

            var runTask = RunCastScenario(ctxA, ctxB, cheat: false);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 90f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "combat sync test timed out");
            var (ok, reason) = runTask.Result;

            ctxA.Manager.Dispose();
            ctxB.Manager.Dispose();
            Object.Destroy(root);
            Assert.IsTrue(ok, reason);
        }

        [UnityTest]
        public IEnumerator Cheat_Casts_Are_Rejected_Server_Side()
        {
            var root = new GameObject("CombatCheatRoot");
            var ctxA = new Ctx();
            var ctxB = new Ctx();
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, new RemoteEntityRegistry(root.transform), "Caster_A");
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, new RemoteEntityRegistry(root.transform), "Target_B");
            Capture(ctxA);
            Capture(ctxB);

            var runTask = RunCastScenario(ctxA, ctxB, cheat: true);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 90f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "combat cheat test timed out");
            var (ok, reason) = runTask.Result;

            ctxA.Manager.Dispose();
            ctxB.Manager.Dispose();
            Object.Destroy(root);
            Assert.IsTrue(ok, reason);
        }

        private static void Capture(Ctx ctx)
        {
            ctx.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.CastBroadcast) ctx.Casts.Add((CastBroadcast)msg);
                if (id == MessageId.DamageResult) ctx.Damages.Add((DamageResult)msg);
            };
        }

        private static HitShape SkillQShape(float range = 3.5f) => new HitShape
        {
            ShapeType = 0, Range = range, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f
        };

        private static async System.Threading.Tasks.Task<(bool, string)> RunCastScenario(
            Ctx ctxA, Ctx ctxB, bool cheat)
        {
            ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctxA.Manager)) return (false, "A failed to enter world");
            ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(ctxB.Manager)) return (false, "B failed to enter world");

            long targetId = ctxB.Manager.LocalPlayerId;

            if (!cheat)
            {
                // —— 正常施法：Q（Single 3.5m，自动目标）——
                ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
                {
                    SkillId = 20001, TargetPlayerId = 0, Shape = SkillQShape()
                });

                // 断言窗口 20s：A 收到 CastBroadcast，双方收到 DamageResult
                for (int i = 0; i < 100 && (ctxA.Damages.Count == 0 || ctxB.Damages.Count == 0); i++)
                {
                    ctxA.Manager.Update(0.05f);
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                }

                if (ctxA.Casts.Count == 0) return (false, "A never received CastBroadcast");
                if (ctxA.Damages.Count == 0) return (false, "A never received DamageResult");
                if (ctxB.Damages.Count == 0) return (false, "B never received DamageResult");

                var hitA = FindHit(ctxA.Damages, targetId);
                var hitB = FindHit(ctxB.Damages, targetId);
                if (hitA == null || hitB == null) return (false, $"B({targetId}) missing from damage results");
                if (System.Math.Abs(hitA.Damage - hitB.Damage) > 0.001f)
                    return (false, $"damage mismatch: A={hitA.Damage} B={hitB.Damage}");
                if (System.Math.Abs(hitA.TargetRemainingHp - hitB.TargetRemainingHp) > 0.001f)
                    return (false, $"remaining hp mismatch: A={hitA.TargetRemainingHp} B={hitB.TargetRemainingHp}");
                if (hitA.Damage != 120f && hitA.Damage != 180f)
                    return (false, $"damage {hitA.Damage} outside server roll domain {{120, 180}}");
                if (System.Math.Abs(hitA.TargetRemainingHp - (100f - hitA.Damage)) > 0.001f)
                    return (false, $"remaining hp {hitA.TargetRemainingHp} != 100 - damage");
                return (true, $"ok: server damage {hitA.Damage} consistent on both clients (B remaining {hitA.TargetRemainingHp})");
            }

            // —— 作弊 1：越射程（range 999）→ 服务器拒绝，双方 5s 内无任何战斗消息 ——
            ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001, TargetPlayerId = 0, Shape = SkillQShape(range: 999f)
            });
            for (int i = 0; i < 25; i++) // 5s
            {
                ctxA.Manager.Update(0.05f);
                ctxB.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }
            if (ctxA.Casts.Count > 0 || ctxA.Damages.Count > 0 || ctxB.Casts.Count > 0 || ctxB.Damages.Count > 0)
                return (false, "cheat(range=999) produced combat messages — server did not reject");

            // —— 作弊 2：连发两次 Q（冷却 6s 内）→ 仅第一次生效 ——
            ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001, TargetPlayerId = 0, Shape = SkillQShape()
            });
            ctxA.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001, TargetPlayerId = 0, Shape = SkillQShape()
            });
            for (int i = 0; i < 100; i++) // 20s
            {
                ctxA.Manager.Update(0.05f);
                ctxB.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }
            if (ctxA.Casts.Count == 0) return (false, "legit cast produced no CastBroadcast");
            if (ctxA.Casts.Count != 1)
                return (false, $"expected exactly 1 CastBroadcast after double-cast, got {ctxA.Casts.Count}");
            if (ctxA.Damages.Count != 1)
                return (false, $"expected exactly 1 DamageResult after double-cast, got {ctxA.Damages.Count}");
            if (ctxB.Damages.Count != 1)
                return (false, $"B should receive exactly 1 DamageResult, got {ctxB.Damages.Count}");
            return (true, $"ok: both cheats rejected (range + cooldown) — {ctxA.Damages.Count} damage delivered");
        }

        private static HitEntry? FindHit(List<DamageResult> results, long targetId)
        {
            foreach (var r in results)
                foreach (var h in r.Hits)
                    if (h.TargetPlayerId == targetId) return h;
            return null;
        }

        private static async System.Threading.Tasks.Task<bool> WaitInWorld(ClientNetworkManager m)
        {
            float elapsed = 0f;
            while (m.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
            {
                m.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }
            return m.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
```

- [x] **Step 2: 运行确认失败（服务器未起时连接失败即红——冒烟前先起服）**

```bash
unity-mcp-cli run-tool assets-refresh --input '{}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"CombatSyncTests"}' --timeout 300000
```

Expected: 连接失败（服务器未启动）——确认为真实网络测试。

- [x] **Step 3: 起服务器（审计日志重定向留档）**

```bash
cd E:/CodeForJob/Cool/Server
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000 > E:/CodeForJob/Cool/production/verify/cast-audit.log 2>&1 &
sleep 4
netstat -ano | grep :8000 | head -3
```

Expected: 端口 8000 LISTENING。

- [x] **Step 4: 冒烟验收**

1. `unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"CombatSyncTests"}' --timeout 300000` → 2 passed
2. 连跑 2 次确认非 flaky
3. 审计日志确认拒绝记录：
```bash
grep -c "cast rejected" E:/CodeForJob/Cool/production/verify/cast-audit.log
grep "reason=" E:/CodeForJob/Cool/production/verify/cast-audit.log | sort | uniq -c
```
Expected: `reason=shape-tampered` ≥1（range 999）且 `reason=cooldown` ≥1（连发）——服务器侧拒绝证据（客户端侧已由测试断言无消息到达）
4. 停服：
```bash
# 找到 dotnet 进程（Server3C）并终止
netstat -ano | grep :8000 | awk '{print $NF}' | sort -u | xargs -r taskkill //PID {} //F
netstat -ano | grep :8000 | wc -l
```
Expected: 0（端口释放）

- [x] **Step 5: 全量回归 + Commit**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj                          # 全绿（≈109，无需服务器）
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}'                               # 全绿（≈188，无需服务器）
```

PlayMode 回归需服务器——先起无 bot 服跑移动/双端回归：

```bash
cd E:/CodeForJob/Cool/Server
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000 > E:/CodeForJob/Cool/production/verify/cast-audit.log 2>&1 &
sleep 4
```

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"MovementSyncTests"}' --timeout 300000   # 4 passed（回归）
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"TwoClientWorldTests"}' --timeout 300000 # 1 passed（回归）
```

停服换 bot 模式跑 AOI 回归（AoiWorldTests 依赖 100 bot 世界）：

```bash
netstat -ano | grep :8000 | awk '{print $NF}' | sort -u | xargs -r taskkill //PID {} //F
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000 --bots 100 > E:/CodeForJob/Cool/production/verify/cast-audit.log 2>&1 &
sleep 4
```

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"AoiWorldTests"}' --timeout 300000        # 1 passed（回归）
```

停服收尾：

```bash
netstat -ano | grep :8000 | awk '{print $NF}' | sort -u | xargs -r taskkill //PID {} //F
netstat -ano | grep :8000 | wc -l    # Expected: 0
```

```bash
git add Assets/Scripts/Tests/PlayMode/CombatSyncTests.cs
git commit -m "test(combat): skill combat e2e - dual-client damage consistency, cheat rejection, audit log"
```

---

### Task 6: 收尾（偏差记录 + 文档同步 + backlog）

**Files:**
- Modify: `docs/superpowers/plans/2026-09-11-p4-skill-combat-sync.md`（偏差记录——终审后由协调者写）
- Modify: `docs/superpowers/guides/p2-client-network-host.md`（P4 战斗接线段追加）
- Modify: `docs/superpowers/guides/p1-dual-client-dev.md`（P4 验证命令追加）
- Modify: `docs/superpowers/specs/2026-08-27-network-sync-design.md`（P4 完成标注——如惯例）

- [x] **Step 1: 偏差记录（计划文件尾段）**

计划文件追加"执行偏差与验收记录"段骨架（execution 时填充）：
- 逐任务偏差（implementer 现场发现 + review 批准）
- 验收证据（测试计数/审计日志/e2e 输出）
- P4 backlog

- [x] **Step 2: guides 文档同步**

`p2-client-network-host.md` 已知边界表追加 P4 段：技能施放链路（激活即发 CastRequest、表外技能本地表现不联网）、PvP 伤害服务端权威（DamageResult → ApplyServerDamage 直扣/远程浮字）、PvP 格挡减伤未服务端化、无复活。

`p1-dual-client-dev.md` 追加 P4 段：起服命令（`dotnet run ... -- 8000 > production/verify/cast-audit.log 2>&1 &`）、CombatSyncTests 运行命令、审计日志 grep 口径。

- [x] **Step 3: 全量验证**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
unity-mcp-cli run-tool assets-refresh --input '{}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}'
```

Expected: 全绿；0 Errors。

- [x] **Step 4: Commit**

```bash
git add docs/superpowers/guides docs/superpowers/plans docs/superpowers/specs
git commit -m "docs(combat): p4 closeout - plan deviations, guide updates, backlog"
```

**P4 backlog（记录未做，不在本次 P4 范围修）：**
- 投射物/蓄力/引导/大招技能服务端化（表扩展——D-P4-1）
- mana/消耗校验（资产有消耗时启用——D-P4-3）
- PvP 格挡/防御减伤服务端化（服务器需知防御状态——D-P4-8）
- 死亡广播/复活机制（重登重建满血为临时语义）
- 快照 ActionState 标志位填充（spec D5 预留字段——施法者动作状态对外可见）
- 远程实体真实技能动画表现（胶囊占位期仅 CastBroadcast 日志；随角色模型渲染 backlog 一起落地）
- 命中方向/接触点细节（DamageResult 无方向字段——飘字不依赖朝向，加字段时走只加不删）
- 服务端 mana 池/战斗属性（攻击力缩放等——客户端 DamageBlock AttackRatio 未服务端化）

---

## 执行偏差与验收记录（2026-09-11 落地，coordinator 收）

**Commits**: T1 03adbe9 → T2 6d76bd6+504d571+20a704b → T3 2226078 → T4 831affa+3cae680 → T5 434b86f+6d0d6fa（+ T6 收尾 commits）

**逐任务偏差（均经 implementer 现场发现 + spec/quality review 批准）**：
1. T2：SkillEntry 不可变化——private set 与对象初始化器 CS0272、`init` 无 IsExternalInit（CS0518）→ getter-only + 显式构造；锥边测试点 (3.0311f,1.75f) 实测 1 ulp 超界 → 改位精确点 (0.8660254f,0.5f)（dot==halfCos==0.5f 精确成立）
2. T2（quality review 轮）：NaN 绕过 `>` 比较（NaN 比较恒 false）→ ValidateShape 非有限守卫 + 4 边界测试（Critical 修复）
3. T3：ApplyDamage 钳 0 与计划自身测试断言 `Hp == 100 − damage`（伤害 120/180 > 100HP）矛盾——不钳（负 HP 走线，死亡仅 IsAlive > 0）；T5 e2e 同假设。计划 Step 3 文本保留钳 0 描述，以此记录为准
4. T4：EventBus 实际在 `Hotfix.GameSystems.Skills`（计划误写 Sys3C.Core.Events）→ 补 using；GameSys.EditorTests.asmdef 加 "KcpNet" 引用（asmdef 引用非传递）；Object 二义性 → 全限定名
5. T4（quality review 轮）：OnDestroy 桥解挂提取 DetachCombatBridge；CastBroadcast 事件路径补测（`CastBroadcast?` 空注解降级 CS1061 → `null!` 直访问）
6. T5：类级连跑 test-order ghost 干扰（Cheat 先跑，其断开客户端 15s PeerSilence 窗口内滞留出生点，自动目标选 ghost 而非 B）→ 施法改显式 TargetPlayerId（服务端显式路径 id 精确匹配 + 距离校验，反作弊语义不变；cheat-1 保持 0——形状校验先于目标裁决）；类级 2/2 ×3 连跑验证

**验收证据**：
- Server.Tests **115/115**（79 P3 基线 + 3 protocol + 16 SyncCore + 11 CastHandler + 6 修复轮边界）
- EditMode **195/195**（166 基线 + 16 + 6 + 6 桥 + 1 CastBroadcast 补测）
- PlayMode：**CombatSyncTests 2/2 ×3 类级连跑**（双端伤害一致 + 双作弊拒绝）；MovementSync 4 + TwoClientWorld 1 + AoiWorld 1 回归全绿
- 服务器审计日志：每次类级跑 `reason=shape-tampered` ×1 + `reason=cooldown` ×1（`production/verify/cast-audit*.log`；客户端侧测试断言零战斗消息到达——双端证据闭环）
- 协议只加不删 ✓（300-302 追加 + 注册；RegistryCompleteness 双端兜底）

**P4 backlog（记录未做）**：
- **P5 前置：施法目标判别**（PvE 挥击跨界——表内技能打怪也会发 CastRequest，服务端自动目标可能命中附近玩家并消耗冷却；P5 怪物/玩家世界共存时须加 cast-target 判别）
- 投射物/蓄力/引导/大招技能服务端化（D-P4-1 表扩展）；mana 消耗（D-P4-3）
- PvP 格挡/防御减伤服务端化（D-P4-8）；死亡广播/复活；快照 ActionState 填充
- 远程实体真实技能动画（胶囊占位期仅 CastBroadcast 日志）；命中方向字段（只加不删追加）
- 服务端 Random → 密码学随机（掷骰可预测理论面）；CastBroadcast 回显形状未校验字段清零（InnerRadius 等——纯表现面）；MaxHp 未消费（快照血条预留）；circle 技能伤害 bot 注释；production/verify/ 工件入 VCS 决策
- 重登重置冷却（PlayerState 重建副作用——与 D-P4-8 满血同机制的反作弊缺口，P5 前记录）；HitResolver 无客户端内圈必中旁路（InnerRadius/InnerAngle 服务端未复刻——P5 PvP 落地前"两端同源"语义缺口）
- PvP 联调期临时守卫建议（终审）：PvE 挥击跨界致合作玩家被友伤（P5 前置项）——若 P5 落地前有人双端联调，考虑临时 PvP 开关
- 服务端冷却晚于客户端 ~latency+1 tick（授权摩擦：客户端合法重施被静默拒）；e2e 未走生产出站路径（桥/真实 Sys3CEntry 全链路留 P5 full-path e2e）

---

## Self-Review（撰写自查——已并入上文）

**1. Spec 覆盖**：§5-P4 逐项 → 共享 CombatRules 冷却/射程判定（T2 表 + ValidateShape/CooldownTicks）；Hitbox 形状 Protocol 序列化（T1 HitShape + CastRequest/CastBroadcast 携带，判定几何两端同源 = T2 HitResolver 共享源码 + T4 数值直传转换）；CastRequest → 服务端校验（T3 存活/冷却/形状/射程）→ CastBroadcast（T3 广播）→ 服务端权威命中裁决（T3 ResolveCast）→ 伤害结果下发含 D4 RNG（T3 RollDamage 服务端掷骰 + T1 DamageResult）；客户端表现即时播放 + 伤害数字服务端结果驱动（T4 本地表现不动 + DamageResult 走现有受击/浮字事件链）；反作弊（T3 拒绝+审计+计数、T5 双作弊用例）；验收双端一致 + 越射程/未冷却拒绝留日志（T5）。D4：客户端不掷战斗骰 ✓（桥只发请求不掷骰）。协议只加不删 ✓（T1 只追加 300-302 + 注册）。Out-of-Scope 未越界（背包/聊天等零触碰）。

**2. 占位符扫描**：无 TBD/TODO；每步代码完整。T5 起服/停服/审计 grep 命令完整；T6 偏差段由执行时填充（plan 惯例——P3 同）。测试数期望给出基准路径（"以实际为准"与 P3 惯例一致）。

**3. 类型一致性**：`CombatRules.TryGet(int, out SkillEntry)`（T2 定义、T3 调用一致）；`CombatRules.ValidateShape(HitShape, SkillEntry)`（T2 定义、T3 调用一致）；`CombatRules.IsSynced(int)`（T2 定义、T4 桥调用一致）；`HitResolver.Resolve(Vec3, float, long, HitShape, IReadOnlyList<CombatCandidate>) → List<long>`（T2 定义、T3 调用传 server Vector3→Vec3 转换一致）；`CombatResolver.ResolveCast(long, CastRequest, uint) → CastResolution`（T3 定义、CastHandler 调用 + 测试一致）；`PlayerState.ApplyDamage/RecordCast/IsCastReady`（T3 定义、测试调用一致）；`CombatNetworkBridge(INetworkClient, Func<long>, RemoteEntityRegistry, Action<long,float,bool,Vector3>?)` + `NotifySkillCast(int, HitShape)` + `OnMessage(MessageId, IMessage)` + `Update()`（T4 定义、Host 接线 + 测试一致）；`HitShapeConverter.FromSkillData/FromShapeBlock/GetShape`（T4 定义、Host/测试一致）；`Sys3CEntry.ApplyServerDamage(float, Vector3)` + `SkillCoordinator` 访问器（T4 定义、Host 接线一致）；`Server3C.CurrentTick/GetVisiblePlayers`（T3 定义、CastHandler 调用一致）；`AoiBroadcaster.GetVisible(long, EntityStore) → List<PlayerState>`（T3 定义、Server3C 包装一致）；MessageId 300-302 与 MessageTypeRegistry 注册对齐（T1，RegistryCompletenessTests 兜底）。

**4. 规模说明**：6 个任务——协议（T1）、共享规则+几何（T2）、服务端权威裁决（T3）、客户端桥接线（T4）、e2e 验收（T5）、收尾（T6）。风险集中：几何语义两端对齐（T2 测试双端跑 + 资产实测值入表）、冷却/拒绝语义（T3 测试矩阵）、双客户端同出生点退化（D-P4-2a 已修正并有 T2/T3/T5 三层测试）。客户端本地战斗零触碰（怪物战斗本地权威不动——P5 范围）。
