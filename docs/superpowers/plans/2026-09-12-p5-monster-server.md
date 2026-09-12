# P5 怪物服务端化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 怪物世界服务端权威化——AI 巡逻/仇恨追击/攻击在服务器主循环驱动，怪物 HP/死亡/重生服务器裁决，玩家断线怪物世界持续运转（spec §5-P5 完整版）。

**Architecture:** 怪物 = `PlayerState` + `Kind`/`TemplateId` 字段注册进 EntityStore——AOI 视野/分级节拍/快照广播/客户端过期销毁全链复用（BotRunner 已验证的实体模型）。新 `MonsterModule` 在主循环 tick 驱动行为树（移植旧 `Server/AI` 纯逻辑，删线程/网络层——R2 合规）。怪物攻击复用 P4 `CombatResolver.ResolveCast`（显式仇恨目标）+ `CastHandler` 新广播路径（怪物无 session）；玩家打怪走既有 CastRequest 链（服务端候选集扩展，P4 backlog"施法目标判别"落地）。客户端 Npc 视图占位渲染（球体 + 模板区分），DamageResult → 本地玩家 `ApplyServerDamage`（HitFSM 同步）/怪物视图浮字——既有桥零改动。

**Tech Stack:** net8.0 服务器（Server3C 主循环 15Hz / GameLoop / xunit）、protobuf-net 协议（只加不删）、HybridCLR 客户端热更（SyncLayer / RemoteEntityRegistry / CombatNetworkBridge）、Unity NUnit PlayMode e2e。

---

## 决策记录（执行时不变更；偏差须在 T6 记录）

| # | 决策 | 内容 |
|---|------|------|
| **D-P5-1** | 实体模型 | 怪物 = PlayerState + `Kind`(byte)/`TemplateId`(int) 入 EntityStore（AOI/节拍/快照/过期全链零改动复用）；NPC id 段 `2_000_000+`（BotRunner 占 `1_000_000+`）；怪物无 session、无登录 |
| **D-P5-2** | 怪物技能 | 入 CombatRules 静态表：50001（Slime，Single range 1.5 / dmg 10 / cd 1s / crit 0）、50002（Wolf，range 2.0 / dmg 15 / cd 1s / crit 0）——range 表值 = `Server/Config/monster_config.json` 实测；伤害 Slime 10 / Wolf 15 为 P5 首次生效的差异化平衡选择（旧 AiComponent 硬编码 10 从未生效——死代码 + Chase 缺陷从不攻击，无兼容义务；T1 审查修正出处，见偏差记录）；冷却复用 `PlayerState.RecordCast/IsCastReady`（tick 基准，与 P4 玩家同机制） |
| **D-P5-3** | 施法目标判别（P4 backlog 项落地） | 服务端候选集扩展：**玩家施法候选 = 玩家 + 怪物**（PvE 挥击自动目标按"锥内最近者"自然命中怪物——消除跨界误伤玩家）；**怪物施法候选 = 已登录存活玩家**（怪物互不打；bot 压测实体不可被怪物命中）。客户端桥不变（TargetPlayerId=0）；客户端发送真实本地目标 id 留 backlog（P4 已记录的精确判别项） |
| **D-P5-4** | 怪物攻击 | AI Attack 动作带 `Blackboard.TargetId` 作显式目标 → `CombatResolver.ResolveCast(npcId, ...)` 复用全链校验（存活/冷却/形状/命中/掷骰）；广播：CastBroadcast → 怪物 AOI 视野观察者（Server3C `GetObserversOf` 反查 AoiGrid）、DamageResult → 每个受击玩家（怪物无 session，caster 收件人跳过）。受击玩家走客户端 `ApplyServerDamage` → 既有 HitFSM/浮字链（spec 验收"打玩家触发 HitFSM 同步"） |
| **D-P5-5** | ActionState 填充（P4 backlog 项，只做怪物侧） | Npc 实体快照 `ActionState = NpcAnimationState`（Idle/Run/Attack/Hit/Death）；玩家恒 0。受击窗口（Hit 5 tick）内跳过行为树（短暂硬直）；死亡展示 30 tick 后移除实体 |
| **D-P5-6** | 死亡/重生 | 怪物死亡 → Death 态展示 30 tick（快照持续广播，客户端播死亡态）→ 移除实体（Server3C 清 AOI）→ `RespawnTicks`(450 = 30s，与客户端 MonsterSpawner.RespawnDelay 同值) 后以**新 id** 在巡逻中心重生（满血）。玩家死亡/复活不在 P5（P4 backlog 保留） |
| **D-P5-7** | 客户端渲染 | Npc 视图 = 球体占位（玩家是胶囊）+ 名称 `Monster_{id}_T{template}`；模板区分色不做（遵守"非 MonoBehaviour 类禁用 GetComponent"——渲染器着色需组件访问，留真实模型 backlog 一并落地）。真实怪物模型/动画表现随 P3 backlog"真实角色模型渲染" |
| **D-P5-8** | 旧 AI 代码处置 | 纯逻辑移植（行为树节点/检测/移动/黑板），**删除**线程层与网络层：`Server/AI/` 下 NpcModule、AiManager、AiComponent、NpcComponent、NpcSyncBroadcaster、Skill/SkillSystem/SkillData、Combat/AggroTable（旧代码本就无 AddAggro 调用方——仇恨排名留 backlog）；旧 Protocol `Npc*.cs` 消息保留（只加不删，MessageId 2000-2003 注册不动，无消费方） |
| **D-P5-9** | 仇恨模型 | 仇恨 = 感知内最近玩家（TargetDetector 视野锥最近者，目标丢失才切换）；AggroTable 伤害仇恨排名留 backlog。视野锥丢失（玩家绕后）→ 目标丢失 → Return 回出生点——spec"感知移植"语义 |

**Out of P5（backlog 记录，防范围蔓延）**：怪物真实模型/动画表现（D-P5-7）、玩家死亡/复活、快照血条 MaxHp 消费、命中方向字段、投射物等玩家技能服务端化、客户端真实目标 id 判别、AggroTable 仇恨排名、坐标量化、ClockSync 重连重 arm、production/verify 工件入 VCS 决策。

**验收目标（spec §5-P5 + D 系列）**：① 服务端驱动巡逻 + 仇恨追击 + 攻击正常（e2e T5-1）；② 玩家断线怪物世界持续运转（e2e T5-2 重连同怪物 id + 单测零玩家 tick）；③ 打玩家触发 HitFSM 同步（DamageResult → ApplyServerDamage 链路，T4 桥测试 + e2e 断言）；④ 双端伤害一致且服务器权威（DamageResult 机制复用 P4，怪物 HP 权威在服务器）。

---

### Task 1: 协议扩展 + 怪物技能入表（只加不删）

**Files:**
- Modify: `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`
- Test: `Server/Server.Tests/Protocol/EntityKindSnapshotTests.cs`（新建）
- Test: `Server/Server.Tests/SyncCore/CombatRulesTests.cs`（追加）
- Test: `Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs`（追加——共享源码双端跑惯例）

- [x] **Step 1: 写失败测试**

`Server/Server.Tests/Protocol/EntityKindSnapshotTests.cs`（完整内容）：

```csharp
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

/// <summary>P5 T1：实体种类/模板字段线上编解码保真（只加不删——旧字段不受影响）</summary>
public class EntityKindSnapshotTests
{
    [Fact]
    public void WorldSnapshot_Roundtrip_Preserves_Npc_Kind_TemplateId_ActionState()
    {
        var snapshot = new WorldSnapshot
        {
            Tick = 7,
            ServerTimestamp = 1_700_000_000_000_0000L,
            Entities =
            {
                new EntitySnapshot
                {
                    PlayerId = 2_000_001,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Kind | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                    X = 12f, Y = 0f, Z = 3f, Rotation = 90f, Speed = 3.5f,
                    Kind = (byte)EntityKind.Npc,
                    TemplateId = 2,
                    ActionState = (byte)NpcAnimationState.Run,
                    Tick = 7
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.WorldSnapshot, id);
        var entity = Assert.Single(Assert.IsType<WorldSnapshot>(decoded).Entities);
        Assert.Equal(2_000_001L, entity.PlayerId);
        Assert.Equal((byte)EntityKind.Npc, entity.Kind);
        Assert.Equal(2, entity.TemplateId);
        Assert.Equal((byte)NpcAnimationState.Run, entity.ActionState);
    }

    [Fact]
    public void Player_Snapshot_Defaults_Kind_Zero_After_Roundtrip()
    {
        // 玩家快照不设 Kind → protobuf 默认 0（旧客户端解码兼容——只加不删语义）
        var snapshot = new WorldSnapshot
        {
            Tick = 1,
            Entities = { new EntitySnapshot { PlayerId = 9, FieldMask = (ushort)EntityFieldMask.Position, X = 1f, Z = 2f } }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable);
        var (_, decoded) = MessageCodec.Decode(bytes);
        var entity = Assert.Single(Assert.IsType<WorldSnapshot>(decoded).Entities);
        Assert.Equal(0, entity.Kind);
        Assert.Equal(0, entity.TemplateId);
    }
}
```

`Server/Server.Tests/SyncCore/CombatRulesTests.cs` 追加（xunit 版，完整内容）：

```csharp
    [Fact]
    public void Monster_Skill_Entries_Are_Synced_And_Validated()
    {
        Assert.True(CombatRules.IsSynced(50001));
        Assert.True(CombatRules.IsSynced(50002));

        Assert.True(CombatRules.TryGet(50001, out var slime));
        Assert.Equal(1.5f, slime.MaxRange);
        Assert.Equal(10f, slime.BaseDamage);
        Assert.Equal(0f, slime.CriticalRate);

        Assert.True(CombatRules.TryGet(50002, out var wolf));
        Assert.Equal(2f, wolf.MaxRange);
        Assert.Equal(15f, wolf.BaseDamage);

        Assert.True(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }, slime));
        Assert.False(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.6f, Angle = 120f }, slime));

        Assert.Equal(15u, CombatRules.CooldownTicks(wolf)); // 1s @15Hz
    }
```

`Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs` 追加（NUnit 版——共享源码双端跑惯例，测试体同上仅断言 API 差异）：

```csharp
    [Test]
    public void Monster_Skill_Entries_Are_Synced_And_Validated()
    {
        Assert.IsTrue(CombatRules.IsSynced(50001));
        Assert.IsTrue(CombatRules.IsSynced(50002));

        Assert.IsTrue(CombatRules.TryGet(50001, out var slime));
        Assert.AreEqual(1.5f, slime.MaxRange);
        Assert.AreEqual(10f, slime.BaseDamage);
        Assert.AreEqual(0f, slime.CriticalRate);

        Assert.IsTrue(CombatRules.TryGet(50002, out var wolf));
        Assert.AreEqual(2f, wolf.MaxRange);
        Assert.AreEqual(15f, wolf.BaseDamage);

        Assert.IsTrue(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }, slime));
        Assert.IsFalse(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.6f, Angle = 120f }, slime));

        Assert.AreEqual(15u, CombatRules.CooldownTicks(wolf)); // 1s @15Hz
    }
```

- [x] **Step 2: 运行验证失败**

Run: `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~EntityKindSnapshotTests|FullyQualifiedName~CombatRulesTests"`
Expected: FAIL（EntityKind 枚举不存在 / Kind 属性不存在 / 50001 未进表）

- [x] **Step 3: 实现协议字段**

`Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs` 三处精确改：

① `EntityFieldMask` 枚举末尾（`ActionState = 1 << 3` 之后）追加：

```csharp
        /// <summary>实体种类（EntityKind：0=Player/1=Npc）——P5 怪物入实体快照</summary>
        Kind = 1 << 4,
        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）——P5 客户端怪物渲染区分</summary>
        TemplateId = 1 << 5
```

② `EntityFieldMask` 枚举与 `EntitySnapshot` 之间加枚举：

```csharp
    /// <summary>
    /// 实体种类（EntitySnapshot.Kind）——P5 怪物服务端化。
    /// 0=Player（protobuf 默认，旧客户端兼容），1=Npc 怪物。
    /// </summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Npc = 1
    }
```

③ `EntitySnapshot` 类内 `ActionState`（ProtoMember 8）与 `Tick`（ProtoMember 9）之间追加：

```csharp
        /// <summary>实体种类（EntityKind；protobuf 默认 0=Player）</summary>
        [ProtoMember(10)]
        public byte Kind { get; set; }

        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）</summary>
        [ProtoMember(11)]
        public int TemplateId { get; set; }
```

- [x] **Step 4: 实现怪物技能表项**

`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` 的 `Table` 字典内 `[20002]` 行后追加（`};` 前）：

```csharp
            // P5 怪物技能（表值 = Server/Config/monster_config.json 实测 2026-09-12：
            //   Slime atk range 1.5 / Wolf atk range 2.0；伤害 10/15 与旧 AiComponent SkillData 同值；
            //   cd 1s 与旧 SkillSystem 同值；怪物无暴击）
            [50001] = new SkillEntry(skillId: 50001, shapeType: 0, maxRange: 1.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 10f, criticalRate: 0f),
            [50002] = new SkillEntry(skillId: 50002, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 15f, criticalRate: 0f),
```

- [x] **Step 5: 运行验证通过**

Run: 同 Step 2 命令
Expected: PASS（新增 3-4 个测试全绿；既有 Protocol/CombatRules 测试不回归）

- [x] **Step 6: 双端编译验证**

Run: `dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q`
Expected: 成功（协议层与 SyncCore 双端同源编译）

- [x] **Step 7: 提交**

```bash
git add Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs Assets/Scripts/Tests/Editor/SyncCore/CombatRulesTests.cs Server/Server.Tests/Protocol/EntityKindSnapshotTests.cs Server/Server.Tests/SyncCore/CombatRulesTests.cs
git commit -m "feat(proto): p5 t1 - entity kind/template fields (add-only) + monster skills in CombatRules"
```

---

### Task 2: 服务端 MonsterModule（AI 移植 + tick 驱动 + 死亡重生 + CLI）

**Files:**
- Modify: `Server/Player/PlayerState.cs`（Kind/TemplateId/ActionState 字段）
- Modify: `Server/Entity/EntityStore.cs`（AddNpc）
- Modify: `Server/Aoi/AoiBroadcaster.cs`（快照填充 Kind/TemplateId/ActionState）
- Create: `Server/Monster/MonsterState.cs`
- Create: `Server/Monster/MonsterTickContext.cs`
- Create: `Server/Monster/Bt/BtStatus.cs`、`Bt/BtNode.cs`、`Bt/BtSelector.cs`、`Bt/BtSequence.cs`、`Bt/BtCondition.cs`、`Bt/BtAction.cs`
- Create: `Server/Monster/SimpleMoveSystem.cs`（含 Vector3Extensions——放 KcpServer.Monster 命名空间，与旧文件 KcpServer 命名空间的同名扩展不冲突，共存编译安全）
- Create: `Server/Monster/TargetDetector.cs`
- Create: `Server/Monster/MonsterBtActions.cs`
- Create: `Server/Monster/MonsterModule.cs`
- Modify: `Server/Server3C.cs`（EnableMonsters + tick 接线——本任务先只接 Tick 与移除 drain；施法 drain 留 T3）
- Modify: `Server/Program.cs`（--monsters CLI）
- Test: `Server/Server.Tests/Monster/MonsterModuleTests.cs`（新建）

- [x] **Step 1: 写失败测试**

`Server/Server.Tests/Monster/MonsterModuleTests.cs`（完整内容；Server.Tests 直接编译服务端源文件——internal 成员可见）：

```csharp
using Cool.Protocol;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>P5 T2：怪物模块纯核心（无 Server3C/session 依赖——主循环模型直测）</summary>
public class MonsterModuleTests
{
    private static (EntityStore Store, MonsterModule Module) Create(int count = 4)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        module.SpawnDefaultLayout(count);
        return (store, module);
    }

    private static PlayerState LoginPlayer(EntityStore store, long id, Vector3 position)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.IsLoggedIn = true;
        p.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return p;
    }

    [Fact]
    public void Spawn_Registers_Npc_Entities_With_Template_And_Hp()
    {
        var (store, module) = Create();

        Assert.Equal(4, module.Count);
        Assert.Equal(4, store.Count);
        foreach (var p in store.All)
        {
            Assert.Equal((byte)EntityKind.Npc, p.Kind);
            Assert.True(p.TemplateId is 1 or 2);
            Assert.True(p.IsAlive);
        }
        // Wolf hp 150 / Slime hp 100（配置实测）
        var wolf = store.GetPlayer(MonsterModule.NpcIdSeed + 1)!;
        Assert.Equal(2, wolf.TemplateId);
        Assert.Equal(150f, wolf.Hp);
    }

    [Fact]
    public void Tick_With_No_Players_Patrols_Monsters()
    {
        var (store, module) = Create();
        var slime = store.GetPlayer(MonsterModule.NpcIdSeed + 2)!; // 布局 [1]：Slime (40,0,0)
        var start = slime.Position;

        for (uint t = 0; t < 60; t++) module.Tick(t);

        Assert.NotEqual(start, slime.Position);                       // 巡逻移动
        float dx = slime.Position.X - 40f, dz = slime.Position.Z - 0f;
        Assert.True(dx * dx + dz * dz <= 5f * 5f + 0.01f);           // 巡逻半径 5 内（D-P5-9 零玩家持续运转）
    }

    [Fact]
    public void Detection_Aggro_And_Attack_Queue_Targets_Closest_Player()
    {
        var (store, module) = Create();
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f)); // 原点——Wolf[0] (12,0,0) 面向原点，检测 15m ≥ 12m
        var wolf = store.GetPlayer(MonsterModule.NpcIdSeed + 1)!;

        for (uint t = 0; t < 200; t++) module.Tick(t); // 3.5m/s × 200×66ms ≈ 46m 行程——足够追到 2m 内

        float dist = Vector3.Distance(wolf.Position, new Vector3(0f, 0f, 0f));
        Assert.True(dist <= 2.5f, $"wolf should chase to attack range, dist={dist}");

        var casts = module.DrainPendingCasts();
        Assert.NotEmpty(casts);
        Assert.Equal(MonsterModule.NpcIdSeed + 1, casts[0].NpcId);
        Assert.Equal(50002, casts[0].Request.SkillId);
        Assert.Equal(1L, casts[0].Request.TargetPlayerId); // 显式仇恨目标（D-P5-4）
    }

    [Fact]
    public void Damage_Triggers_Hit_Then_Death_Removal_And_Respawn()
    {
        var (store, module) = Create();
        var slimeId = MonsterModule.NpcIdSeed + 2;
        var slime = store.GetPlayer(slimeId)!;

        store.GetPlayer(slimeId)!.ApplyDamage(100f); // 玩家裁决扣血（T3 链）——Hp 0 死亡
        module.Tick(0);

        var state = module.GetState(slimeId);
        Assert.NotNull(state);
        Assert.True(state!.DeathTick.HasValue);
        Assert.Equal((byte)NpcAnimationState.Death, slime.ActionState);

        for (uint t = 1; t < MonsterModule.DeathDisplayTicks; t++) module.Tick(t);
        Assert.NotNull(store.GetPlayer(slimeId)); // 展示窗口内仍在（移除在 t=30）

        module.Tick(MonsterModule.DeathDisplayTicks);
        Assert.Null(store.GetPlayer(slimeId));                    // 移除
        Assert.Contains(slimeId, module.DrainRemovedNpcs());      // Server3C 清 AOI 用

        // 重生窗口：dueTick = DeathDisplayTicks + RespawnTicks = 480，到期后新 id 满血重生在巡逻中心
        for (uint t = MonsterModule.DeathDisplayTicks + 1; t < MonsterModule.DeathDisplayTicks + MonsterModule.RespawnTicks; t++)
            module.Tick(t);
        module.Tick(MonsterModule.DeathDisplayTicks + MonsterModule.RespawnTicks);

        Assert.Equal(4, module.Count); // 重生完成（Count 恢复）
    }
}
```

- [x] **Step 2: 运行验证失败**

Run: `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MonsterModuleTests"`
Expected: FAIL（KcpServer.Monster 命名空间不存在 / 编译失败）

- [x] **Step 3: PlayerState 加字段**

`Server/Player/PlayerState.cs`——`IsDirty` 属性前追加：

```csharp
        /// <summary>实体种类（EntityKind：0=Player 默认，1=Npc）——P5 怪物复用实体存储/AOI/快照链</summary>
        public byte Kind { get; internal set; }

        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）</summary>
        public int TemplateId { get; internal set; }

        /// <summary>动作/战斗状态（Npc=NpcAnimationState；玩家恒 0——D-P5-5 只做怪物侧）</summary>
        public byte ActionState { get; internal set; }
```

- [x] **Step 4: EntityStore.AddNpc**

`Server/Entity/EntityStore.cs`——`MarkDirty` 方法后追加：

```csharp
        /// <summary>P5：注册怪物实体（Kind=Npc + 模板 + 服务器权威满血）——AOI/快照链与玩家同池</summary>
        public PlayerState AddNpc(long npcId, string name, int templateId, float hp, Vector3 position)
        {
            var state = new PlayerState(npcId)
            {
                PlayerName = name,
                Kind = (byte)EntityKind.Npc,
                TemplateId = templateId
            };
            state.MaxHp = hp;
            state.Hp = hp;
            state.UpdatePosition(position, 0f, 0f, 0L, 0U);
            _entities[npcId] = state;
            return state;
        }
```

- [x] **Step 5: 行为树节点（6 个文件）**

`Server/Monster/Bt/BtStatus.cs`：

```csharp
namespace KcpServer.Monster
{
    public enum BtStatus
    {
        Running,
        Success,
        Failure
    }
}
```

`Server/Monster/Bt/BtNode.cs`：

```csharp
namespace KcpServer.Monster
{
    /// <summary>行为树节点（旧 Server/AI 移植——tick 驱动替代 Task.Run 循环，R2 合规）</summary>
    public abstract class BtNode
    {
        public abstract BtStatus Tick(MonsterTickContext ctx);
    }
}
```

`Server/Monster/Bt/BtSelector.cs`：

```csharp
namespace KcpServer.Monster
{
    /// <summary>选择节点：依序尝试子节点，首个非 Failure（Running/Success）即返回；全失败返回 Failure</summary>
    public sealed class BtSelector : BtNode
    {
        private readonly BtNode[] _children;

        public BtSelector(params BtNode[] children) => _children = children;

        public override BtStatus Tick(MonsterTickContext ctx)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ctx);
                if (status != BtStatus.Failure) return status;
            }
            return BtStatus.Failure;
        }
    }
}
```

`Server/Monster/Bt/BtSequence.cs`：

```csharp
namespace KcpServer.Monster
{
    /// <summary>序列节点：依序执行子节点，首个非 Success（Running/Failure）即返回；全成功返回 Success</summary>
    public sealed class BtSequence : BtNode
    {
        private readonly BtNode[] _children;

        public BtSequence(params BtNode[] children) => _children = children;

        public override BtStatus Tick(MonsterTickContext ctx)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ctx);
                if (status != BtStatus.Success) return status;
            }
            return BtStatus.Success;
        }
    }
}
```

`Server/Monster/Bt/BtCondition.cs`：

```csharp
using System;

namespace KcpServer.Monster
{
    /// <summary>条件节点：黑板谓词——true=Success，false=Failure</summary>
    public sealed class BtCondition : BtNode
    {
        private readonly Func<MonsterState, bool> _predicate;

        public BtCondition(Func<MonsterState, bool> predicate) => _predicate = predicate;

        public override BtStatus Tick(MonsterTickContext ctx)
            => _predicate(ctx.State) ? BtStatus.Success : BtStatus.Failure;
    }
}
```

`Server/Monster/Bt/BtAction.cs`：

```csharp
using System;

namespace KcpServer.Monster
{
    /// <summary>动作节点：执行动作函数并透传其状态</summary>
    public sealed class BtAction : BtNode
    {
        private readonly Func<MonsterTickContext, BtStatus> _action;

        public BtAction(Func<MonsterTickContext, BtStatus> action) => _action = action;

        public override BtStatus Tick(MonsterTickContext ctx) => _action(ctx);
    }
}
```

- [x] **Step 6: MonsterState + MonsterTickContext**

`Server/Monster/MonsterState.cs`（完整内容）：

```csharp
using KcpServer.Config;

namespace KcpServer.Monster
{
    /// <summary>
    /// P5 怪物运行时 AI 状态（主循环线程独占，R2）——与 EntityStore.PlayerState 配对：
    /// 位置/HP/冷却权威在 PlayerState；AI 决策状态（黑板/巡逻/仇恨/生死计时）在此。
    /// 移植自旧 Server/AI（AiBlackboard + AiComponent 合并；删线程/网络/技能层——
    /// 攻击冷却走 PlayerState.IsCastReady，技能数据走 CombatRules）。
    /// </summary>
    public sealed class MonsterState
    {
        public long NpcId { get; }
        public int TemplateId { get; }
        public MonsterData Config { get; }

        // 黑板（旧 AiBlackboard 语义）
        public AlertLevel AlertLevel { get; set; } = AlertLevel.PEACE;
        public long? TargetId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public Vector3 PatrolCenter { get; set; }
        public Vector3[] PatrolPoints { get; set; } = System.Array.Empty<Vector3>();
        public int CurrentPatrolIndex { get; set; }
        public Vector3? LastKnownTargetPosition { get; set; }

        // tick 内暂存位置/朝向（BT/移动系统写，模块 tick 末尾 flush 到 PlayerState）
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Moving; // 本 tick 是否在移动（快照 Speed 取值）

        // 攻击技能 id（模板映射：1→50001，2→50002；D-P5-2）
        public int AttackSkillId { get; }

        // 受击硬直窗口（玩家命中 → Hit 态 HitDisplayTicks；窗口内跳过行为树）
        public uint? HitStartTick;

        // 死亡展示窗口（Death 态停驻 DeathDisplayTicks 后移除实体 + 调度重生）
        public uint? DeathTick;

        // 上次观测 HP（裁决在怪物 tick 之后发生——tick+1 察觉玩家命中/死亡转移）
        public float LastHp;

        /// <summary>行为树根（Spawn 时装配）</summary>
        public BtNode? BehaviorTree { get; set; }

        public MonsterState(long npcId, int templateId, MonsterData config, int attackSkillId)
        {
            NpcId = npcId;
            TemplateId = templateId;
            Config = config;
            AttackSkillId = attackSkillId;
        }
    }

    /// <summary>警报等级（旧 Server/AI 移植；HOSTILE 只在获取目标时置位，Return 完成回 PEACE）</summary>
    public enum AlertLevel
    {
        PEACE = 0,
        HOSTILE = 1
    }
}
```

`Server/Monster/MonsterTickContext.cs`（完整内容）：

```csharp
namespace KcpServer.Monster
{
    /// <summary>行为树 tick 上下文——一个 tick 内共享（状态/实体/模块/移动系统/节拍）</summary>
    public sealed class MonsterTickContext
    {
        public MonsterState State { get; }
        public PlayerState Entity { get; }
        public MonsterModule Module { get; }
        public SimpleMoveSystem Mover { get; }
        public float DeltaTime { get; }
        public uint Tick { get; }

        public MonsterTickContext(MonsterState state, PlayerState entity, MonsterModule module,
            SimpleMoveSystem mover, float deltaTime, uint tick)
        {
            State = state;
            Entity = entity;
            Module = module;
            Mover = mover;
            DeltaTime = deltaTime;
            Tick = tick;
        }
    }
}
```

- [x] **Step 7: SimpleMoveSystem（移植）**

`Server/Monster/SimpleMoveSystem.cs`（完整内容——旧 `Server/AI/Movement/SimpleMoveSystem.cs` 移植，AiComponent → MonsterState）：

```csharp
using System;

namespace KcpServer.Monster
{
    /// <summary>简单平面移动（旧 Server/AI 移植——Quaternion 数学与 Server/Util 同源）</summary>
    public sealed class SimpleMoveSystem
    {
        private const float RotationSpeed = 180f; // 度/秒

        public void MoveTo(MonsterState m, Vector3 targetPosition, float deltaTime)
        {
            Vector3 direction = targetPosition - m.Position;
            direction.Y = 0;

            if (direction.X * direction.X + direction.Z * direction.Z < 0.001f) return;

            direction = direction.Normalize();

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            m.Rotation = RotateTowards(m.Rotation, targetRotation, RotationSpeed * deltaTime);

            Vector3 movement = direction * m.Config.moveSpeed * deltaTime;
            m.Position = m.Position + movement;
        }

        public bool HasReached(MonsterState m, Vector3 target, float threshold = 0.1f)
        {
            float dx = m.Position.X - target.X;
            float dz = m.Position.Z - target.Z;
            return (dx * dx + dz * dz) < (threshold * threshold);
        }

        private static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegrees)
        {
            float angle = Angle(from, to);
            if (angle < 0.0001f) return to;

            float t = (float)Math.Min(1, maxDegrees / angle);
            return Slerp(from, to, t);
        }

        private static float Angle(Quaternion a, Quaternion b)
        {
            float dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            return (float)(Math.Acos(Math.Min(Math.Abs(dot), 1.0)) * 2.0 * 180.0 / Math.PI);
        }

        private static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            float dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            Quaternion tb = b;
            if (dot < 0)
            {
                tb = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
                dot = -dot;
            }

            if (dot > 0.9995f)
            {
                return new Quaternion(
                    a.X + (tb.X - a.X) * t,
                    a.Y + (tb.Y - a.Y) * t,
                    a.Z + (tb.Z - a.Z) * t,
                    a.W + (tb.W - a.W) * t
                ).Normalize();
            }

            float theta0 = (float)Math.Acos(dot);
            float theta = theta0 * t;
            float sinTheta = (float)Math.Sin(theta);
            float sinTheta0 = (float)Math.Sin(theta0);

            float s0 = (float)(Math.Cos(theta) - dot * sinTheta / sinTheta0);
            float s1 = (float)(sinTheta / sinTheta0);

            return new Quaternion(
                s0 * a.X + s1 * tb.X,
                s0 * a.Y + s1 * tb.Y,
                s0 * a.Z + s1 * tb.Z,
                s0 * a.W + s1 * tb.W
            );
        }
    }

    /// <summary>Vector3 归一化扩展（旧 Server/AI 同款——Server/Util/Vector3 无 Normalize 成员）</summary>
    public static class Vector3Extensions
    {
        public static Vector3 Normalize(this Vector3 v)
        {
            float mag = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (mag < 0.0001f) return new Vector3(0, 0, 0);
            return new Vector3(v.X / mag, v.Y / mag, v.Z / mag);
        }
    }
}
```

- [x] **Step 8: TargetDetector（移植，yaw 化）**

`Server/Monster/TargetDetector.cs`（完整内容——旧 Quaternion 感知改 yaw 浮点，语义与 HitResolver 同约定）：

```csharp
using System;

namespace KcpServer.Monster
{
    /// <summary>
    /// 怪物感知检测（旧 Server/AI 移植）——yaw 语义与 HitResolver 一致（yaw=0 → +Z）。
    /// 视野锥 = 前方 ±visionAngle/2（水平）。贴脸（dist≈0）必感知——零向量退化同 D-P4-2a 语义。
    /// </summary>
    public static class TargetDetector
    {
        public static bool CanDetect(float yawDeg, Vector3 aiPosition, Vector3 targetPosition,
            float detectionRadius, float visionAngle)
        {
            float dx = targetPosition.X - aiPosition.X;
            float dz = targetPosition.Z - aiPosition.Z;
            float distSqr = dx * dx + dz * dz;
            if (distSqr > detectionRadius * detectionRadius) return false;
            if (distSqr < 0.0001f) return true;

            float forwardX = (float)Math.Sin(yawDeg * Math.PI / 180.0);
            float forwardZ = (float)Math.Cos(yawDeg * Math.PI / 180.0);
            float dot = (forwardX * dx + forwardZ * dz) / (float)Math.Sqrt(distSqr);
            float halfCos = (float)Math.Cos(visionAngle * 0.5f * Math.PI / 180.0);
            return dot >= halfCos;
        }
    }
}
```

- [x] **Step 9: MonsterBtActions（移植 + Chase 修正）**

`Server/Monster/MonsterBtActions.cs`（完整内容）：

```csharp
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer.Monster
{
    /// <summary>
    /// 怪物行为动作（旧 BtActions 移植）。
    /// P5 修正：Chase 进入攻击距离即 Success——旧树 Chase 恒 Running 会饿死序列后续
    /// Attack 节点（序列被阻塞，怪物永远不攻击——旧代码死代码期缺陷，见偏差记录）。
    /// </summary>
    public static class MonsterBtActions
    {
        public static BtStatus Patrol(MonsterTickContext ctx)
        {
            var m = ctx.State;
            if (m.PatrolPoints.Length == 0) return BtStatus.Success;

            int idx = m.CurrentPatrolIndex % m.PatrolPoints.Length;
            Vector3 target = m.PatrolPoints[idx];

            ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
            if (ctx.Mover.HasReached(m, target))
            {
                m.CurrentPatrolIndex++;
                m.Moving = false;
                ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
            }
            else
            {
                m.Moving = true;
                ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
            }
            return BtStatus.Running;
        }

        public static BtStatus Chase(MonsterTickContext ctx)
        {
            var m = ctx.State;
            Vector3 target = m.LastKnownTargetPosition ?? m.Position;

            // 进入攻击距离即成功（P5 修正）——让序列前进到 Attack
            if (ctx.Mover.HasReached(m, target, m.Config.attackRange)) return BtStatus.Success;

            ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
            m.Moving = true;
            ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
            return BtStatus.Running;
        }

        public static BtStatus Attack(MonsterTickContext ctx)
        {
            var m = ctx.State;
            var entity = ctx.Entity;

            // 目标丢失（死亡/离场）→ 失败回 Chase/Return
            if (!m.TargetId.HasValue) return BtStatus.Failure;

            // 目标移出攻击距离 → 失败 → 序列重头 Chase
            Vector3 target = m.LastKnownTargetPosition ?? m.Position;
            float dx = m.Position.X - target.X, dz = m.Position.Z - target.Z;
            if (dx * dx + dz * dz > m.Config.attackRange * m.Config.attackRange) return BtStatus.Failure;

            m.Moving = false;

            // 冷却门（tick 基准——与 P4 玩家同机制；裁决在怪物 tick 后发生，记录即时生效）
            if (!CombatRules.TryGet(m.AttackSkillId, out var entry)) return BtStatus.Failure; // 表缺失 = 配置错误
            if (!entity.IsCastReady(m.AttackSkillId, ctx.Tick, CombatRules.CooldownTicks(entry)))
                return BtStatus.Running;

            ctx.Module.QueueCast(m.NpcId, new CastRequest
            {
                SkillId = m.AttackSkillId,
                TargetPlayerId = m.TargetId.Value, // 显式仇恨目标（HitResolver 按 id + 距离裁决，D-P5-4）
                Shape = new HitShape
                {
                    ShapeType = 0, // Single——范围 = 表上限（ValidateShape 通过）
                    Range = m.Config.attackRange,
                    Angle = 120f
                }
            });
            entity.ActionState = (byte)NpcAnimationState.Attack;
            return BtStatus.Running; // 保持攻击态；下一 tick 冷却门再判
        }

        public static BtStatus Return(MonsterTickContext ctx)
        {
            var m = ctx.State;
            ctx.Mover.MoveTo(m, m.SpawnPosition, ctx.DeltaTime);
            if (ctx.Mover.HasReached(m, m.SpawnPosition))
            {
                m.AlertLevel = AlertLevel.PEACE;
                m.Moving = false;
                ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
                return BtStatus.Success;
            }
            m.Moving = true;
            ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
            return BtStatus.Running;
        }
    }
}
```

注意：Attack 的冷却门用先 TryGet 再判定（表缺失 = 配置错误返回 Failure）——见上面代码。

- [x] **Step 10: MonsterModule（核心）**

`Server/Monster/MonsterModule.cs`（完整内容）：

```csharp
using System;
using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Config;

namespace KcpServer.Monster
{
    /// <summary>
    /// P5 怪物模块（spec §5-P5 完整版）——主循环线程独占（R2），无锁。
    /// 每 tick：受击/死亡转移检测 → 感知（视野锥内最近存活已登录玩家）→ 行为树
    /// （巡逻/追击/攻击/返回）→ 位置 flush（UpdatePosition）→ 待施法队列
    /// （Server3C drain 后经 CombatResolver 裁决，T3）。
    /// 死亡：Death 态展示 DeathDisplayTicks → 移除实体（Server3C 清 AOI）→
    /// RespawnTicks 后新 id 满血重生在巡逻中心。
    /// 断线持续运转：状态全在 EntityStore + 本模块，零 per-session 依赖（spec 验收）。
    /// </summary>
    public sealed class MonsterModule
    {
        public const long NpcIdSeed = 2_000_000;  // NPC id 段（BotRunner 占 1_000_000+）
        public const uint DeathDisplayTicks = 30; // 死亡展示 30 tick ≈ 2s（客户端播完死亡态过期销毁）
        public const uint RespawnTicks = 450;     // 重生 30s（15Hz × 30；与客户端 MonsterSpawner.RespawnDelay 同值）
        public const uint HitDisplayTicks = 5;    // 受击硬直 5 tick
        public const float TickDeltaSeconds = 0.066f; // 主循环 66ms

        private readonly EntityStore _store;
        private readonly Func<long> _nextNpcId;
        private readonly Dictionary<int, MonsterData> _templates = new();
        private readonly Dictionary<long, MonsterState> _monsters = new();
        private readonly SimpleMoveSystem _mover = new();
        private readonly List<(long NpcId, CastRequest Request)> _pendingCasts = new();
        private readonly List<long> _removedNpcs = new();
        private readonly List<(int TemplateId, Vector3 Center, uint DueTick)> _respawns = new();

        /// <summary>活跃怪物数（测试/统计口径）</summary>
        public int Count => _monsters.Count;

        /// <summary>攻击技能 id 模板映射（D-P5-2；配置模板 1/2 与 monster_config.json 对齐）</summary>
        private static int SkillIdOf(int templateId) => templateId switch
        {
            1 => 50001,
            2 => 50002,
            _ => throw new ArgumentException($"No combat skill mapped for monster template {templateId}")
        };

        public MonsterModule(EntityStore store, Func<long> nextNpcId, string? configPath = null)
        {
            _store = store;
            _nextNpcId = nextNpcId;
            // 配置路径按程序集基准目录解析（CWD 相对路径在 dotnet run/test 不同 CWD 下静默落默认值——
            // csproj CopyToOutputDirectory 保证 json 在输出目录；找不到仍落 ConfigLoader 默认值）
            var path = configPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Config", "monster_config.json");
            var templates = ConfigLoader.LoadMonsterTemplates(path);
            foreach (var kvp in templates) _templates[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// 固定布局生成（e2e 确定性依赖——T5 注释同步此表）：
        ///   [0] Wolf(t2)  (12,0,0)  面向原点——开局即感知原点观察者（检测 15m ≥ 12m）
        ///   [1] Slime(t1) (40,0,0)  巡逻可视（观察者视野 80m）、不感知（检测 8m）
        ///   [2] Slime(t1) (-40,0,0) 同 [1]
        ///   [3] Wolf(t2)  (0,0,40)  面向原点（观察者视野 80m 内，不感知——距离 40m &gt; 15m）
        /// count 超过布局长度时循环复用；朝向一律面向原点（感知确定性）。
        /// </summary>
        public void SpawnDefaultLayout(int count)
        {
            var layout = new (int TemplateId, Vector3 Position)[]
            {
                (2, new Vector3(12f, 0f, 0f)),
                (1, new Vector3(40f, 0f, 0f)),
                (1, new Vector3(-40f, 0f, 0f)),
                (2, new Vector3(0f, 0f, 40f)),
            };
            for (int i = 0; i < count; i++)
            {
                var slot = layout[i % layout.Length];
                SpawnMonster(slot.TemplateId, slot.Position, patrolCenter: slot.Position);
            }
        }

        /// <summary>生成怪物（AddNpc 满血 + 行为树装配 + 巡逻点）</summary>
        public long SpawnMonster(int templateId, Vector3 position, Vector3 patrolCenter)
        {
            if (!_templates.TryGetValue(templateId, out var config))
                throw new ArgumentException($"Unknown monster template: {templateId}");

            long npcId = _nextNpcId();
            var entity = _store.AddNpc(npcId, $"{config.name}#{npcId}", templateId, config.hp, position);
            entity.Rotation = YawTowardOrigin(position);

            var state = new MonsterState(npcId, templateId, config, SkillIdOf(templateId))
            {
                Position = position,
                Rotation = Quaternion.LookRotation(ForwardOf(entity.Rotation)),
                SpawnPosition = position,
                PatrolCenter = patrolCenter,
                PatrolPoints = GeneratePatrolPoints(patrolCenter, config.patrolRadius),
                LastHp = config.hp
            };
            state.BehaviorTree = BuildBehaviorTree();
            _monsters[npcId] = state;
            return npcId;
        }

        /// <summary>测试/调试：读怪物 AI 状态</summary>
        public MonsterState? GetState(long npcId) => _monsters.TryGetValue(npcId, out var s) ? s : null;

        /// <summary>推进一 tick（服务器主循环每 tick 调用——先于施法裁决 drain 与快照广播）</summary>
        public void Tick(uint tick)
        {
            var done = new List<long>();
            foreach (var m in _monsters.Values)
            {
                var entity = _store.GetPlayer(m.NpcId);
                if (entity == null) { done.Add(m.NpcId); continue; }

                if (m.DeathTick.HasValue)
                {
                    // 死亡展示窗口：原地停驻 Death 态，到期移除 + 调度重生
                    if (tick - m.DeathTick.Value >= DeathDisplayTicks)
                    {
                        _store.RemovePlayer(m.NpcId);
                        _removedNpcs.Add(m.NpcId); // Server3C 清 AOI 状态
                        _respawns.Add((m.TemplateId, m.PatrolCenter, tick + RespawnTicks));
                        done.Add(m.NpcId);
                    }
                    continue;
                }

                DetectHitOrDeath(m, entity, tick);

                bool deadNow = m.DeathTick.HasValue;
                bool inHitWindow = m.HitStartTick.HasValue && tick - m.HitStartTick.Value < HitDisplayTicks;
                if (!deadNow && !inHitWindow)
                {
                    DetectTarget(m, entity);
                    var ctx = new MonsterTickContext(m, entity, this, _mover, TickDeltaSeconds, tick);
                    m.BehaviorTree?.Tick(ctx);
                }
                else
                {
                    m.Moving = false; // 本 tick 死亡/受击硬直：跳过行为树（ActionState 保持 Death/Hit 不被 BT 覆盖）
                }

                // 位置/朝向/速度 flush（AOI 节拍由调度器按坐标判定——每 tick 写与 BotRunner 同惯例）
                entity.UpdatePosition(m.Position, YawOf(m.Rotation), m.Moving ? m.Config.moveSpeed : 0f,
                    tick * 10_000L + 1L, tick);
            }
            foreach (var id in done) _monsters.Remove(id);

            // 重生到期（同 tick 生成——快照广播在 drain 之后，首帧立即可见）
            for (int i = _respawns.Count - 1; i >= 0; i--)
            {
                var (templateId, center, dueTick) = _respawns[i];
                if (tick - dueTick < uint.MaxValue / 2) // wrap 安全到期判定（tick 差相减惯例）
                {
                    _respawns.RemoveAt(i);
                    SpawnMonster(templateId, center, center);
                }
            }
        }

        /// <summary>受击/死亡转移检测（裁决在怪物 tick 之后发生——本 tick 察觉上一 tick 的 HP 变化）</summary>
        private void DetectHitOrDeath(MonsterState m, PlayerState entity, uint tick)
        {
            if (entity.Hp < m.LastHp)
            {
                m.LastHp = entity.Hp;
                if (!entity.IsAlive)
                {
                    m.DeathTick = tick;
                    m.Moving = false;
                    entity.ActionState = (byte)NpcAnimationState.Death;
                }
                else
                {
                    m.HitStartTick = tick;
                    entity.ActionState = (byte)NpcAnimationState.Hit;
                }
            }
        }

        /// <summary>感知：视野锥内最近的存活已登录玩家（D-P5-9——目标丢失才切换；AggroTable 仇恨排名留 backlog）</summary>
        private void DetectTarget(MonsterState m, PlayerState entity)
        {
            long? closestId = null;
            Vector3? closestPos = null;
            float closestDistSqr = float.MaxValue;

            foreach (var p in _store.All)
            {
                if (p.Kind != (byte)EntityKind.Player || !p.IsAlive || !p.IsLoggedIn) continue;
                if (!TargetDetector.CanDetect(entity.Rotation, m.Position, p.Position,
                        m.Config.detectionRadius, m.Config.visionAngle)) continue;
                float dx = p.Position.X - m.Position.X, dz = p.Position.Z - m.Position.Z;
                float distSqr = dx * dx + dz * dz;
                if (distSqr < closestDistSqr) { closestDistSqr = distSqr; closestId = p.PlayerId; closestPos = p.Position; }
            }

            if (closestId.HasValue)
            {
                if (m.TargetId != closestId)
                {
                    m.TargetId = closestId;
                    m.AlertLevel = AlertLevel.HOSTILE; // 获取目标即敌意（旧语义）
                }
                m.LastKnownTargetPosition = closestPos;
            }
            else if (m.TargetId.HasValue)
            {
                m.TargetId = null; // 目标丢失（离视野/死亡/离线）——树落 Return 分支
            }
        }

        /// <summary>入队攻击施法（BT Attack 动作调用；Server3C 每 tick drain 后裁决+广播——T3）</summary>
        public void QueueCast(long npcId, CastRequest request) => _pendingCasts.Add((npcId, request));

        /// <summary>取出本 tick 待裁决施法（Server3C drain）</summary>
        public List<(long NpcId, CastRequest Request)> DrainPendingCasts()
        {
            var result = new List<(long, CastRequest)>(_pendingCasts);
            _pendingCasts.Clear();
            return result;
        }

        /// <summary>取出本 tick 已移除怪物（Server3C 清 AOI 状态）</summary>
        public List<long> DrainRemovedNpcs()
        {
            var result = new List<long>(_removedNpcs);
            _removedNpcs.Clear();
            return result;
        }

        private BtNode BuildBehaviorTree()
        {
            return new BtSelector(
                new BtSequence(
                    new BtCondition(m => m.AlertLevel == AlertLevel.PEACE && !m.TargetId.HasValue),
                    new BtAction(MonsterBtActions.Patrol)
                ),
                new BtSequence(
                    new BtCondition(m => m.AlertLevel == AlertLevel.HOSTILE && m.TargetId.HasValue),
                    new BtAction(MonsterBtActions.Chase),
                    new BtAction(MonsterBtActions.Attack)
                ),
                new BtSequence(
                    new BtCondition(m => !m.TargetId.HasValue && m.AlertLevel != AlertLevel.PEACE),
                    new BtAction(MonsterBtActions.Return)
                )
            );
        }

        private static Vector3[] GeneratePatrolPoints(Vector3 center, float radius)
        {
            const int count = 4;
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float rad = i * (360f / count) * (float)(Math.PI / 180.0);
                points[i] = new Vector3(
                    center.X + (float)Math.Cos(rad) * radius,
                    center.Y,
                    center.Z + (float)Math.Sin(rad) * radius);
            }
            return points;
        }

        /// <summary>yaw（度）→ 前向量（yaw 语义与 HitResolver 一致：yaw=0 → +Z）</summary>
        private static Vector3 ForwardOf(float yawDeg)
        {
            float rad = yawDeg * (float)(Math.PI / 180.0);
            return new Vector3((float)Math.Sin(rad), 0f, (float)Math.Cos(rad));
        }

        /// <summary>Quaternion → yaw 度（旧 NpcSyncBroadcaster.GetYaw 同公式）</summary>
        private static float YawOf(Quaternion q)
            => (float)(Math.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180.0 / Math.PI);

        /// <summary>面向原点的 yaw（固定布局 e2e 感知确定性）</summary>
        private static float YawTowardOrigin(Vector3 position)
        {
            float rad = (float)Math.Atan2(-position.X, -position.Z);
            return rad * (float)(180.0 / Math.PI);
        }
    }
}
```

注意：`MonsterState` 已含 `BehaviorTree` 属性（Step 6 完整内容内）——Spawn 时装配。

- [x] **Step 11: AoiBroadcaster 快照填充**

`Server/Aoi/AoiBroadcaster.cs`——`BuildForObserver` 内 EntitySnapshot 构造块精确替换：

```csharp
                    var snap = new EntitySnapshot
                    {
                        PlayerId = entity.PlayerId,
                        FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed
                            | EntityFieldMask.ActionState | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                        X = entity.Position.X,
                        Y = entity.Position.Y,
                        Z = entity.Position.Z,
                        Rotation = entity.Rotation,
                        Speed = entity.Speed,
                        Kind = entity.Kind,
                        TemplateId = entity.TemplateId,
                        ActionState = entity.ActionState,
                        Tick = tick
                    };
```

**检查**：若既有测试断言 FieldMask 精确值（Position|Rotation|Speed），同步更新断言并记录（掩码高位追加 = 只加不删语义，旧客户端忽略未知位）。

- [x] **Step 11b: 配置文件复制到输出目录（权威配置生效——否则静默落 ConfigLoader 默认值）**

`Server/Server3C.csproj` 与 `Server/Server.Tests/Server.Tests.csproj` 各加 ItemGroup：

```xml
  <!-- P5：怪物配置随构建复制——运行时按 AppContext.BaseDirectory/Config 解析（dotnet run/test 的 CWD 不可靠） -->
  <ItemGroup>
    <None Include="Config\monster_config.json" Link="Config\monster_config.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

（Server3C.csproj 的 Include 相对其自身目录 `Server/Config/...` ✓；Server.Tests.csproj 用 `..\Config\monster_config.json`。）

- [x] **Step 12: Server3C + Program 接线（本任务只接 Tick/移除；施法 drain 留 T3）**

`Server/Server3C.cs`：
① 字段区（`_castHandler` 声明后）追加：

```csharp
        // P5：服务端权威怪物（AI 决策 + 死亡重生；攻击裁决经 CombatResolver、广播经 CastHandler——T3）
        private MonsterModule? _monsterModule;
        private long _monsterIdSeed = MonsterModule.NpcIdSeed;
```

② `EnableBots` 方法后追加：

```csharp
        /// <summary>怪物模式：注册 N 个怪物（固定布局——T5 e2e 依赖）。必须在 StartAsync 之前调用</summary>
        public void EnableMonsters(int count)
        {
            _monsterModule = new MonsterModule(_entityStore, () => Interlocked.Increment(ref _monsterIdSeed));
            _monsterModule.SpawnDefaultLayout(count);
        }
```

③ 主循环 tick lambda（`_botRunner?.Tick((uint)tick);` 之后）插入：

```csharp
                    // P5：怪物 AI tick + 死亡移除清 AOI（施法裁决 drain 在 T3 接线）
                    if (_monsterModule != null)
                    {
                        _monsterModule.Tick((uint)tick);
                        foreach (var npcId in _monsterModule.DrainRemovedNpcs())
                            _aoi.RemoveEntity(npcId);
                    }
```

`Server/Program.cs`——bot 参数解析块后追加怪物参数解析 + 注册：

```csharp
            // P5 怪物模式：--monsters N（固定布局怪物——供 MonsterWorldTests 验收）
            int monsterCount = 0;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--monsters" && int.TryParse(args[i + 1], out var m))
                {
                    monsterCount = m;
                    break;
                }
            }
```

`EnableBots` 调用块后追加：

```csharp
            if (monsterCount > 0)
            {
                _server.EnableMonsters(monsterCount);
                Console.WriteLine($"Monster mode: {monsterCount} monsters spawned");
            }
```

- [x] **Step 13: 运行验证通过**

Run: `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MonsterModuleTests"`
Expected: PASS（4 个测试全绿；其中追击测试限 200 tick——若因 RotateTowards 转向消耗超时，把上限调到 400 并在偏差记录注明）

- [x] **Step 14: 全量回归 + 编译**

Run: `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj`（全量——P4 基线 115 不回归）
Run: `dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q`
Expected: 全绿 + 编译成功（旧 Server/AI 代码仍共存编译——T6 删除）

- [x] **Step 15: 提交**

```bash
git add Server/Player/PlayerState.cs Server/Entity/EntityStore.cs Server/Aoi/AoiBroadcaster.cs Server/Monster/ Server/Server3C.cs Server/Program.cs Server/Server.Tests/Monster/
git commit -m "feat(server): p5 t2 - MonsterModule with tick-driven behavior tree (patrol/chase/attack/return), death-respawn, --monsters CLI"
```

---

### Task 3: 服务端战斗集成（怪物攻击裁决 + 玩家打怪 + 广播）

**Files:**
- Modify: `Server/Handler/CastHandler.cs`（P4 落地文件——`CombatResolver` 与 `CastHandler` 两类同文件：候选集策略改 CombatResolver、加 Resolve 入口 + HandleNpcCastAsync）
- Modify: `Server/Server3C.cs`（GetObserversOf + tick 施法 drain 接线）
- Test: `Server/Server.Tests/Combat/CombatResolverNpcTests.cs`（新建）
- Test: `Server/Server.Tests/Monster/MonsterCombatIntegrationTests.cs`（新建——T2 审查补项）

- [x] **Step 1: 写失败测试**

`Server/Server.Tests/Combat/CombatResolverNpcTests.cs`（完整内容——种子 RNG 确定性；CombatResolver 构造带 rng 注入）：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>P5 T3：怪物参与的战斗裁决（CombatResolver 纯核心直测，无 Server3C/session 依赖）</summary>
public class CombatResolverNpcTests
{
    private static readonly Random SeededRng = new Random(42);

    private static (EntityStore Store, CombatResolver Resolver, long Player, long Npc) World(
        float npcZ, int templateId = 1)
    {
        var store = new EntityStore();
        long player = 1;
        long npc = MonsterModule.NpcIdSeed + 1;
        store.AddPlayer(player, "P");
        store.GetPlayer(player)!.IsLoggedIn = true;
        // 怪物在玩家正前方 +Z（yaw=0 锥内——自动目标用例依赖方向语义；显式目标用例方向无关）
        store.AddNpc(npc, $"Slime#{npc}", templateId, 100f, new Vector3(0f, 0f, npcZ));
        var resolver = new CombatResolver(store, SeededRng);
        return (store, resolver, player, npc);
    }

    private static CastRequest PlayerSwing(long target) => new CastRequest
    {
        SkillId = 10001, // BA_Combo1：Single range 2，dmg 50 crit 15%（种子固定 → 掷骰确定性）
        TargetPlayerId = target,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
    };

    private static CastRequest MonsterBite(long target, float range) => new CastRequest
    {
        SkillId = 50001, // Slime：dmg 10 crit 0——伤害恒 10（纯确定性）
        TargetPlayerId = target,
        Shape = new HitShape { ShapeType = 0, Range = range, Angle = 120f }
    };

    [Fact]
    public void Player_Cast_Explicit_Npc_Target_Damages_Npc_Authoritatively()
    {
        var (store, resolver, player, npc) = World(1.5f); // 玩家前方 1.5m（10001 射程 2 内）
        var npcState = store.GetPlayer(npc)!;

        var result = resolver.ResolveCast(player, PlayerSwing(npc), 10);

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(npc, hit.TargetPlayerId);
        Assert.Equal(100f - hit.Damage, npcState.Hp);      // 服务器权威扣血
        Assert.True(hit.Damage is 50f or 75f);             // 种子 42 下暴击域 {50, 75}
        Assert.Equal(npcState.Hp, hit.TargetRemainingHp);  // 余血 = 扣后 HP（双端一致载体）
    }

    [Fact]
    public void Player_Cast_Auto_Target_Hits_Closer_Npc_Not_Farther_Player()
    {
        var (store, resolver, player, npc) = World(1.5f);
        var other = store.AddPlayer(2, "B");
        other.IsLoggedIn = true;
        other.UpdatePosition(new Vector3(0f, 0f, 3f), 0f, 0f, 1L, 0U); // 前方 3m——锥内但射程 2 外

        var result = resolver.ResolveCast(player, PlayerSwing(0), 10); // 自动目标（P4 backlog 跨界判别落地）

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(npc, hit.TargetPlayerId); // 锥内射程内最近者 = 怪物（挥击打怪不再误伤附近玩家）
        Assert.Equal(100f, store.GetPlayer(2)!.Hp); // 射程外玩家不误伤
    }

    [Fact]
    public void Npc_Cast_Hits_Player_With_Exact_Damage_And_Records_Cooldown()
    {
        var (store, resolver, player, npc) = World(1.5f);

        var result = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(player, hit.TargetPlayerId);
        Assert.Equal(10f, hit.Damage);                 // crit 0 → 掷骰恒 10
        Assert.Equal(90f, store.GetPlayer(player)!.Hp);
        Assert.Equal(npc, result.Broadcast!.CasterPlayerId); // 客户端表现/浮字源 id

        // 冷却：同 tick 重施被拒（RecordCast 即时生效）
        var second = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);
        Assert.False(second.Accepted);
        Assert.Equal("cooldown", second.RejectReason);
    }

    [Fact]
    public void Npc_Cast_Ignores_Other_Npcs()
    {
        var (store, resolver, player, npc) = World(1.5f);
        store.AddNpc(npc + 1, "Wolf#2", 2, 150f, new Vector3(1.6f, 0f, 0f)); // 贴邻怪物

        var result = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);

        var hit = Assert.Single(result.Hits); // 邻怪不被命中（D-P5-3 怪物互不打——回归锁定）
        Assert.Equal(player, hit.TargetPlayerId);
    }

    [Fact]
    public void Npc_Cast_Auto_Target_Skips_Non_Logged_In_Entities()
    {
        var (store, resolver, player, npc) = World(1.5f);
        var bot = store.AddPlayer(99, "Bot");
        bot.UpdatePosition(new Vector3(0f, 0f, 0.8f), 0f, 0f, 1L, 0U); // 未登录 bot 比玩家更近（锥内）

        var result = resolver.ResolveCast(npc, MonsterBite(0, 1.5f), 10); // 自动目标

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(player, hit.TargetPlayerId); // bot 不可被怪物命中（候选集过滤——本用例是策略变更的失败驱动）
        Assert.Equal(100f, bot.Hp);
    }

    [Fact]
    public void Dead_Npc_Cannot_Cast_And_Cannot_Be_Hit()
    {
        var (store, resolver, player, npc) = World(1.5f);
        var npcState = store.GetPlayer(npc)!;
        npcState.ApplyDamage(100f); // 死亡（死亡展示窗口内仍在 store）

        Assert.False(npcState.IsAlive);

        var cast = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);
        Assert.False(cast.Accepted);
        Assert.Equal("caster-dead", cast.RejectReason);

        var swing = resolver.ResolveCast(player, PlayerSwing(npc), 10);
        Assert.True(swing.Accepted);   // 施法合法（空挥冷却语义）
        Assert.Empty(swing.Hits);      // 死亡怪物不可被命中（Alive 过滤）
    }
}
```

另建 `Server/Server.Tests/Monster/MonsterCombatIntegrationTests.cs`（T2 质量审查补项：BT 冷却门 + 裁决 RecordCast 的全链间距锁定——T2 测试无裁决时冷却门从未真正生效，此测试堵住该缺口）：

```csharp
using System;
using Cool.Protocol;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>P5 T3：怪物施法全链集成（MonsterModule tick + CombatResolver drain——冷却记录闭环，T2 审查补项）</summary>
public class MonsterCombatIntegrationTests
{
    [Fact]
    public void Npc_Casts_Are_Spaced_By_Cooldown_After_Resolver_Drain()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        module.SpawnDefaultLayout(4);
        var player = store.AddPlayer(1, "P");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);

        var resolver = new CombatResolver(store, new Random(42));
        uint lastCastTick = 0;
        int castCount = 0;

        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            foreach (var (npcId, request) in module.DrainPendingCasts())
            {
                var resolution = resolver.ResolveCast(npcId, request, t);
                if (!resolution.Accepted) continue; // BT 冷却门先行——正常不应发生（防御跳过）
                castCount++;
                if (castCount > 1)
                    Assert.True(t - lastCastTick >= 15, $"casts spaced only {t - lastCastTick} ticks (cooldown not enforced)");
                lastCastTick = t;
            }
        }

        // 120 tick：追击 ~45 tick 进入射程，此后每 15 tick 一次——期望 ≥3 次间隔施法
        Assert.True(castCount >= 3, $"expected multiple cooldown-spaced casts, got {castCount}");
    }
}
```

- [x] **Step 2: 运行验证失败**

Run: `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatResolverNpcTests|FullyQualifiedName~MonsterCombatIntegrationTests"`
Expected: FAIL（`Npc_Cast_Auto_Target_Skips_Non_Logged_In_Entities`——候选集无过滤时自动目标命中 bot；`Npc_Casts_Are_Spaced_By_Cooldown` 为 T2 代码的回归锁定（测试内自行 drain+裁决，T2 已具备该闭环）——它验证的是本任务**不改坏**冷却语义，可能已过）

- [x] **Step 3: CombatResolver 候选集策略**

`Server/Handler/CastHandler.cs` 的 `CombatResolver.ResolveCast` 内候选构建块精确替换（P4 原文为 `foreach (var p in _store.All)` 全量−caster）：

```csharp
            // 候选（P4：全部在线实体 − caster；P5 D-P5-3 扩展）：
            //  玩家施法 → 玩家 + 怪物（PvE 挥击自动目标按锥内最近者自然命中怪物——
            //    跨界误伤玩家 backlog 项落地；bot 压测实体同 P4 语义不排除）
            //  怪物施法 → 仅已登录玩家（怪物互不打；bot 不可被怪物命中）
            bool isNpcCaster = caster.Kind == (byte)EntityKind.Npc;
            var candidates = new List<CombatCandidate>();
            foreach (var p in _store.All)
            {
                if (p.PlayerId == playerId) continue;
                if (isNpcCaster && (p.Kind != (byte)EntityKind.Player || !p.IsLoggedIn)) continue;
                candidates.Add(new CombatCandidate(p.PlayerId,
                    new Vec3(p.Position.X, p.Position.Y, p.Position.Z), p.IsAlive));
            }
```

- [x] **Step 4: CastHandler 加 Resolve 入口 + HandleNpcCastAsync**

`Server/Handler/CastHandler.cs` 的 `CastHandler` 类内、`HandleCastAsync` 前追加：

```csharp
        /// <summary>裁决入口（P5 怪物施法复用——怪物无 session，Server3C tick 内直调）</summary>
        public CombatResolver.CastResolution Resolve(long casterId, CastRequest request, uint tick)
            => _resolver.ResolveCast(casterId, request, tick);
```

`HandleCastAsync` 方法后追加：

```csharp
        /// <summary>
        /// P5 怪物施放广播（D-P5-4）：CastBroadcast → 怪物 AOI 视野内在线观察者
        /// （第三方看技能表现）；DamageResult → 每个受击玩家（怪物无 session，caster 收件人跳过）。
        /// 拒绝不广播（怪物被拒 = 服务器内部逻辑错误——审计日志 + 计数）。
        /// </summary>
        public async Task HandleNpcCastAsync(long npcId, CombatResolver.CastResolution resolution)
        {
            if (!resolution.Accepted || resolution.Broadcast == null)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] npc cast rejected: npc={npcId} reason={resolution.RejectReason}");
                return;
            }

            var castRecipients = _server.GetObserversOf(npcId);
            foreach (var recipientId in castRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.CastBroadcast, resolution.Broadcast);
            }

            if (resolution.Hits.Count > 0)
            {
                var result = new DamageResult
                {
                    SourcePlayerId = npcId,
                    SkillId = resolution.Broadcast.SkillId,
                    ServerTimestamp = DateTime.UtcNow.Ticks,
                    Hits = resolution.Hits
                };
                foreach (var hit in resolution.Hits)
                {
                    var s = _server.GetSession(hit.TargetPlayerId);
                    if (s != null)
                        await _server.SendToSessionAsync(s, MessageId.DamageResult, result);
                }
            }
        }
```

- [x] **Step 5: Server3C.GetObserversOf + 施法 drain 接线**

`Server/Server3C.cs`：
① `GetVisiblePlayers` 方法后追加：

```csharp
        /// <summary>能看到指定实体的在线观察者（P5 怪物 CastBroadcast 范围——AoiGrid 迟滞视野复用；
        /// O(N) 每施法一次——怪物规模小可接受；实体量大时换反向索引（注释留痕））</summary>
        public List<long> GetObserversOf(long entityId)
        {
            var result = new List<long>();
            foreach (var observer in _entityStore.All)
            {
                if (observer.Kind != (byte)EntityKind.Player) continue;
                if (!_playerIdToSession.ContainsKey(observer.PlayerId)) continue;
                var visible = _aoi.GetVisible(observer.PlayerId, _entityStore);
                foreach (var e in visible)
                    if (e.PlayerId == entityId) { result.Add(observer.PlayerId); break; }
            }
            return result;
        }
```

② T2 的 tick lambda 怪物块替换为（施法 drain 接线）：

```csharp
                    // P5：怪物 AI tick → 攻击裁决 drain（同 tick 冷却记录即时生效）→ 死亡移除清 AOI
                    if (_monsterModule != null)
                    {
                        _monsterModule.Tick((uint)tick);
                        foreach (var (npcId, request) in _monsterModule.DrainPendingCasts())
                        {
                            var resolution = _castHandler.Resolve(npcId, request, (uint)tick);
                            if (resolution.Accepted)
                                _ = _castHandler.HandleNpcCastAsync(npcId, resolution);
                        }
                        foreach (var npcId in _monsterModule.DrainRemovedNpcs())
                            _aoi.RemoveEntity(npcId);
                    }
```

- [x] **Step 6: 运行验证通过**

Run: `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CombatResolverNpcTests|FullyQualifiedName~MonsterCombatIntegrationTests|FullyQualifiedName~CastHandlerTests|FullyQualifiedName~MonsterModuleTests"`
Expected: PASS（7 个新测试 + P4 Combat/CastHandler/怪物模块回归全绿）

注意：MonsterCombatIntegrationTests 测试内自行 drain+裁决——不依赖 Server3C 接线，验证的是"BT 冷却门 + 裁决 RecordCast"闭环语义。

- [x] **Step 7: 全量回归 + 编译**

Run: `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj`
Run: `dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q`
Expected: 全绿（P4 基线 115 + T1 3 + T2 4 + T3 7 = 129）

- [x] **Step 8: 提交**

```bash
git add Server/Handler/CastHandler.cs Server/Server3C.cs Server/Server.Tests/Combat/CombatResolverNpcTests.cs Server/Server.Tests/Monster/MonsterCombatIntegrationTests.cs
git commit -m "feat(server): p5 t3 - npc combat integration - candidate policy (players hit npcs, npcs hit players only), npc cast broadcast to AOI observers"
```

---

### Task 4: 客户端 Npc 视图 + 桥验证 + 本地怪物开关

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（联网禁用本地 MonsterSpawner）
- Test: `Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs`（追加）
- Test: `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs`（追加）

- [x] **Step 1: 写失败测试**

`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs` 追加（NUnit 风格按现有文件）：

```csharp
    [Test]
    public void Npc_Snapshot_Creates_Monster_View_With_Template()
    {
        var root = new GameObject("NpcRoot");
        var registry = new RemoteEntityRegistry(root.transform);
        try
        {
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_001,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                    | EntityFieldMask.Kind | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                X = 12f, Z = 3f, Rotation = 90f,
                Kind = (byte)EntityKind.Npc,
                TemplateId = 2,
                ActionState = (byte)NpcAnimationState.Run
            }, serverTicks: 1_000L);

            var view = registry.GetView(2_000_001);
            Assert.IsNotNull(view);
            Assert.AreEqual((byte)EntityKind.Npc, view!.Kind);
            Assert.AreEqual(2, view.TemplateId);
            Assert.AreEqual((byte)NpcAnimationState.Run, view.ActionState);
            Assert.IsTrue(view.GameObject.name.Contains("Monster"), $"view name '{view.GameObject.name}' should mark monster");
        }
        finally
        {
            registry.Clear();
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Player_Snapshot_Still_Creates_Player_View()
    {
        var root = new GameObject("PlayerRoot");
        var registry = new RemoteEntityRegistry(root.transform);
        try
        {
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 5,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Kind),
                X = 1f, Z = 2f,
                Kind = (byte)EntityKind.Player
            }, serverTicks: 1_000L);

            var view = registry.GetView(5);
            Assert.IsNotNull(view);
            Assert.AreEqual((byte)EntityKind.Player, view!.Kind);
            Assert.IsTrue(view.GameObject.name.Contains("RemotePlayer"));
        }
        finally
        {
            registry.Clear();
            Object.DestroyImmediate(root);
        }
    }
```

`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` 追加（现有夹具 `_root`/`_client`/`RecordingClient` 直接复用——两测试完整内容）：

```csharp
        [Test]
        public void Monster_Source_Damage_To_Local_Player_Invokes_Local_Damage_Callback()
        {
            // 怪物攻击本地玩家（spec"打玩家触发 HitFSM 同步"链路）：SourcePlayerId=怪物 id
            // → onDamageToLocalPlayer 回调（宿主接 ApplyServerDamage——HitFSM 入口）。
            // 桥按 TargetPlayerId 匹配本地玩家——来源种类无关（既有逻辑，锁定回归）。
            var registry = new RemoteEntityRegistry(_root.transform);
            float sinkDamage = 0;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir) => sinkDamage = damage);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2_000_001, SkillId = 50001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, IsCritical = false, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(10f, sinkDamage, 0.001f);
        }

        [Test]
        public void Monster_Target_Damage_Emits_Float_Text_Over_Npc_View()
        {
            // 玩家打怪：DamageResult.TargetPlayerId = 怪物 id → 怪物视图（Npc 快照创建）飘字
            // ——既有 PlayerTakeDamageEvent 链对 Npc 视图零改动生效（锁定回归）。
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_001,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Npc,
                TemplateId = 1
            }, serverTicks: 1_000_000L);
            var view = registry.GetView(2_000_001);
            Assert.NotNull(view, "Npc 视图未创建");

            PlayerTakeDamageEvent? captured = null;
            Action<PlayerTakeDamageEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 10001,
                    Hits = { new HitEntry { TargetPlayerId = 2_000_001, Damage = 50f, IsCritical = false, TargetRemainingHp = 50f } }
                });
                bridge.Update();

                Assert.NotNull(captured, "怪物受击未发 PlayerTakeDamageEvent");
                Assert.AreEqual(view.GameObject.GetInstanceID(), captured.Value.EntityId);
                Assert.AreEqual(50, captured.Value.Damage);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }
```

- [x] **Step 2: 运行验证失败**

Run: `unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"RemoteEntityRegistryTests"}'` 与 `CombatNetworkBridgeTests`
Expected: FAIL（view.Kind 属性不存在 / Monster 视图未创建）

- [x] **Step 3: RemoteEntityRegistry Npc 视图**

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`：
① `ApplySnapshot` 内视图创建调用改为传快照：

```csharp
            if (!_views.TryGetValue(snapshot.PlayerId, out var view))
            {
                view = CreateView(snapshot.PlayerId, snapshot);
                _views[snapshot.PlayerId] = view;
            }
```

② `CreateView` 替换为：

```csharp
        private RemoteEntityView CreateView(long playerId, EntitySnapshot snapshot)
        {
            // P5：Npc 视图 = 球体占位（玩家胶囊区分）；名称携带模板 id。
            // 真实怪物模型/动画表现随 P3 backlog"真实角色模型渲染"落地（D-P5-7）。
            // 注：不访问 Renderer 组件着色——非 MonoBehaviour 类禁用 GetComponent（项目规则）。
            if (snapshot.Kind == (byte)EntityKind.Npc)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Monster_{playerId}_T{snapshot.TemplateId}";
                go.transform.SetParent(_root, false);
                return new RemoteEntityView(go, snapshot.Kind, snapshot.TemplateId);
            }

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = $"RemotePlayer_{playerId}";
            capsule.transform.SetParent(_root, false);
            return new RemoteEntityView(capsule);
        }
```

③ `ApplySnapshot` 内 Position 位处理块之前追加 ActionState 更新：

```csharp
            // P5：Npc 动作状态（占位期仅存储供测试/上层观察——动画表现随真实模型 backlog）
            view.ActionState = snapshot.ActionState;
```

- [x] **Step 4: RemoteEntityView 加字段**

`RemoteEntityView` 类：

```csharp
    /// <summary>远程实体渲染视图（P1 占位；P5 加 Npc 元数据）</summary>
    public sealed class RemoteEntityView
    {
        public GameObject GameObject { get; }
        /// <summary>实体种类（EntityKind；getter-only——Unity 无 IsExternalInit，禁用 init，P4 偏差同教训）</summary>
        public byte Kind { get; }
        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）</summary>
        public int TemplateId { get; }
        /// <summary>动作/战斗状态（Npc=NpcAnimationState；玩家恒 0）</summary>
        public byte ActionState { get; set; }

        public Vector3 Position
        {
            get => GameObject.transform.position;
            set => GameObject.transform.position = value;
        }
        public Quaternion Rotation
        {
            get => GameObject.transform.rotation;
            set => GameObject.transform.rotation = value;
        }

        public RemoteEntityView(GameObject go, byte kind = 0, int templateId = 0)
        {
            GameObject = go;
            Kind = kind;
            TemplateId = templateId;
        }
    }
```

- [x] **Step 5: ClientNetworkHost 禁用本地 MonsterSpawner**

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`——`TryAttachLocalCharacter` 成功装配后（`Debug.Log("[NetHost] 本地角色已接入移动上报...")` 前）插入：

```csharp
            // P5：联网模式下怪物由服务器驱动——禁用场景内本地 MonsterSpawner（单机模式零影响）
            var localSpawner = FindObjectOfType<Hotfix.GameSystems.Monster.MonsterSpawner>();
            if (localSpawner != null)
            {
                localSpawner.gameObject.SetActive(false);
                Debug.Log("[NetHost] 本地 MonsterSpawner 已禁用（服务器驱动怪物）");
            }
```

- [x] **Step 6: 运行验证通过**

Run: `unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"RemoteEntityRegistryTests"}'`
Run: `unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"CombatNetworkBridgeTests"}'`
Expected: PASS（P4 基线 195 不回归 + 新增 ≈ 3 测试）

- [x] **Step 7: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs Assets/Scripts/Tests/Editor/SyncLayer/
git commit -m "feat(client): p5 t4 - npc remote views (sphere placeholder + template metadata), disable local MonsterSpawner in networked mode"
```

---

### Task 5: e2e 验收（MonsterWorldTests）

**Files:**
- Create: `Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs`
- 验收留档: `production/verify/`（审计日志，见 Step 4）

**前置**：服务器 `--monsters 4` 模式启动（同 P4 e2e 惯例——测试不自行起服）。

- [x] **Step 1: 写测试**

`Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs`（完整内容）：

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
    /// P5 核心验收（spec §5-P5）：
    /// ① 服务端驱动巡逻 + 仇恨追击 + 攻击：观察者看到怪物（Kind=Npc 视图）、
    ///    巡逻怪物位置漂移、Wolf 追击至攻击距离、收到 DamageResult（dmg 15 恒值）；
    /// ② 玩家断线怪物世界持续运转：重连后同一怪物 id 仍在（HP 权威保留）、继续攻击。
    /// 服务器 --monsters 4 模式启动（固定布局：Wolf(12,0,0) 面向原点——开局即感知观察者；
    /// Slime(±40,0,0) 巡逻可视不感知；Wolf(0,0,40) 可视不感知）。
    /// 统计口径（非时序脆弱断言——同 AoiWorldTests 惯例）。
    /// </summary>
    public class MonsterWorldTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
            public readonly List<DamageResult> Damages = new List<DamageResult>();
        }

        [UnityTest]
        public IEnumerator Monsters_Patrol_Chase_And_Attack_Server_Authoritative()
        {
            var root = new GameObject("MonsterWorldRoot");
            var ctx = new Ctx();
            ctx.Registry = new RemoteEntityRegistry(root.transform);
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, ctx.Registry, "Monster_Observer");
            ctx.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctx.Damages.Add((DamageResult)msg);
            };

            var runTask = RunPatrolChaseAttack(ctx);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            try
            {
                Assert.IsTrue(runTask.IsCompleted, "monster world test timed out");
                var (ok, reason) = runTask.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                ctx.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator Reconnect_Monster_World_Persists()
        {
            var root = new GameObject("MonsterReconnectRoot");
            var ctxA = new Ctx();
            ctxA.Registry = new RemoteEntityRegistry(root.transform);
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, ctxA.Registry, "Reconnect_A");
            ctxA.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctxA.Damages.Add((DamageResult)msg);
            };

            var runTask = RunReconnect(ctxA);
            float elapsed = 0f;
            while (!runTask.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            try
            {
                Assert.IsTrue(runTask.IsCompleted, "reconnect test timed out");
                var (ok, reason) = runTask.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                ctxA.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunPatrolChaseAttack(Ctx ctx)
        {
            if (!await WaitInWorld(ctx.Manager)) return (false, "login failed");

            // —— 观察 60s（统计口径）——
            // ① Wolf(12,0,0) 开局感知：追击 → 攻击 → 观察者收到 DamageResult（dmg 15 恒值，
            //    SourcePlayerId = 怪物 id）——"打玩家触发 HitFSM 同步"链路的协议侧证据
            // ② Slime(40,0,0) 巡逻：视图位置漂移 ≥ 1m（样本间隔采样）
            var monsterIds = new HashSet<long>();
            long? wolfId = null;
            Vector3? slimeFirstPos = null;
            float slimeDrift = 0f;
            Vector3? lastSlimePos = null;

            for (int i = 0; i < 300 && ctx.Damages.Count == 0; i++) // 300 × 200ms = 60s（收到伤害即提前出）
            {
                ctx.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(200);
                if (ctx.Manager.CurrentState != ClientNetworkManager.State.InWorld)
                    return (false, "disconnected mid-observation");

                foreach (var view in ctx.Registry.PlayerIds)
                {
                    var v = ctx.Registry.GetView(view);
                    if (v == null) continue;
                    if (v.Kind == (byte)EntityKind.Npc)
                    {
                        monsterIds.Add(view);
                        if (v.TemplateId == 2 && !wolfId.HasValue) wolfId = view;
                        if (v.TemplateId == 1)
                        {
                            if (!slimeFirstPos.HasValue) slimeFirstPos = v.Position;
                            if (lastSlimePos.HasValue) slimeDrift += Vector3.Distance(lastSlimePos.Value, v.Position);
                            lastSlimePos = v.Position;
                        }
                    }
                }
            }

            if (monsterIds.Count == 0) return (false, "no monster views observed in 60s (Npc snapshot not working?)");
            if (wolfId == null) return (false, "wolf (template 2) never seen");
            if (slimeDrift < 1f) return (false, $"slime patrol drift only {slimeDrift:F2}m in 60s (patrol not running?)");
            if (ctx.Damages.Count == 0) return (false, "observer never took monster damage (aggro/attack not working?)");

            var first = ctx.Damages[0];
            var hit = FindHit(first, ctx.Manager.LocalPlayerId);
            if (hit == null) return (false, $"damage result {first.SourcePlayerId} did not target local player {ctx.Manager.LocalPlayerId}");
            if (System.Math.Abs(hit.Damage - 15f) > 0.001f)
                return (false, $"monster damage {hit.Damage} != server-rolled 15 (wolf dmg)");
            if (first.SourcePlayerId != wolfId.Value)
                return (false, $"damage source {first.SourcePlayerId} != wolf id {wolfId.Value}");
            if (System.Math.Abs(hit.TargetRemainingHp - (100f - hit.Damage)) > 0.001f)
                return (false, $"local remaining hp {hit.TargetRemainingHp} inconsistent");

            return (true, $"ok: {monsterIds.Count} monsters seen, slime patrol drift {slimeDrift:F2}m, " +
                          $"wolf {wolfId} hit observer for {hit.Damage} (hp {hit.TargetRemainingHp})");
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunReconnect(Ctx ctx)
        {
            if (!await WaitInWorld(ctx.Manager)) return (false, "login failed");
            long localId = ctx.Manager.LocalPlayerId;

            // —— 阶段 1：观察 Wolf 并记录 id ——
            long? wolfId = null;
            for (int i = 0; i < 100 && !wolfId.HasValue; i++) // 20s
            {
                ctx.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(200);
                foreach (var id in ctx.Registry.PlayerIds)
                {
                    var v = ctx.Registry.GetView(id);
                    if (v != null && v.Kind == (byte)EntityKind.Npc && v.TemplateId == 2) { wolfId = id; break; }
                }
            }
            if (!wolfId.HasValue) return (false, "wolf never seen before disconnect");

            // —— 阶段 2：断线（怪物世界应持续运转——服务器零 per-session 依赖）——
            ctx.Manager.Dispose();
            await System.Threading.Tasks.Task.Delay(3000);

            // —— 阶段 3：重连（新会话新玩家 id）——同一怪物 id 仍在，且继续攻击重连者 ——
            var root2 = new GameObject("ReconnectRoot2");
            var ctxB = new Ctx();
            ctxB.Registry = new RemoteEntityRegistry(root2.transform);
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, ctxB.Registry, "Reconnect_B");
            ctxB.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctxB.Damages.Add((DamageResult)msg);
            };
            try
            {
                if (!await WaitInWorld(ctxB.Manager)) return (false, "reconnect login failed");

                bool sameWolfSeen = false;
                for (int i = 0; i < 150; i++) // 30s
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(200);
                    if (ctxB.Manager.CurrentState != ClientNetworkManager.State.InWorld)
                        return (false, "reconnect dropped mid-observation");
                    var v = ctxB.Registry.GetView(wolfId.Value);
                    if (v != null && v.Kind == (byte)EntityKind.Npc) { sameWolfSeen = true; break; }
                    if (ctxB.Damages.Count > 0) break; // 被追击即证明世界持续运转
                }
                if (!sameWolfSeen && ctxB.Damages.Count == 0)
                    return (false, "wolf did not persist across disconnect (world not continuous)");
                if (ctxB.Damages.Count > 0)
                {
                    var hit = FindHit(ctxB.Damages[0], ctxB.Manager.LocalPlayerId);
                    if (hit != null && System.Math.Abs(hit.TargetRemainingHp - (100f - hit.Damage)) > 0.001f)
                        return (false, "reconnect damage hp inconsistent");
                }
                return (true, $"ok: wolf {wolfId} persisted across disconnect (local {localId} → B), " +
                              $"damage results on reconnect: {ctxB.Damages.Count}");
            }
            finally
            {
                ctxB.Manager.Dispose();
                Object.Destroy(root2);
            }
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

执行注意：`RemoteEntityRegistry.PlayerIds` 返回 `IEnumerable<long>`（现有实现）——循环内 `GetView` 取视图；`ClientNetworkManager.LocalPlayerId` 存在性以 P4 桥为准（`Func<long> localPlayerId` 已用——T4 桥构造传 `() => _manager!.LocalPlayerId`，存在）。`Object.Destroy` 在 UnityTest 中按 AoiWorldTests 惯例（`Object.Destroy(root)` 无 finally 判断）。

- [x] **Step 2: 起服务器**

```bash
cd E:/CodeForJob/Cool && dotnet run --project Server/Server3C.csproj -- 8000 --monsters 4 > production/verify/monster-audit.log 2>&1 &
```
Expected: 日志出现 `Monster mode: 4 monsters spawned` 与 `Server3C is running on port 8000`

- [x] **Step 3: 运行 PlayMode 验收**

Run: `unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"MonsterWorldTests"}'`（必要时 `--timeout 300000`——P3 教训：测试 >60s 超 CLI 默认）
Expected: 2/2 PASS ×2 次类级连跑（test-order 干扰检查——同 P4 惯例）

回归（**干净服务器**——无 `--monsters`，避免怪物实体污染 P2-P4 测试的世界假设）：
1. 停怪物服务器，起干净服务器：`dotnet run --project Server/Server3C.csproj -- 8000`
2. `MovementSyncTests`、`TwoClientWorldTests`、`AoiWorldTests`、`CombatSyncTests` 类级全跑 → 全绿（P5 服务器改动不回归 P2-P4 链）
3. 共存抽查：重启 `--monsters 4` 服务器，`CombatSyncTests` ×1 跑——双客户端显式目标伤害一致性在怪物在场时仍成立（怪物作为候选不干扰显式路径）

- [x] **Step 4: 审计日志留档**

```bash
grep -c "npc cast rejected" production/verify/monster-audit.log   # 期望 0（怪物施法零拒绝）
grep "Monster mode\|monsters spawned" production/verify/monster-audit.log | head -3
```
留档 `production/verify/monster-audit.log`（同 P4 cast-audit 惯例）。

- [x] **Step 5: 提交**

```bash
git add Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs
git commit -m "test(monster): p5 e2e - server-driven patrol/chase/attack + reconnect world persistence"
```

---

### Task 6: 收尾（旧 AI 死代码删除 + 文档同步 + backlog）

**Files:**
- Delete: `Server/AI/`（整目录——D-P5-8）
- Modify: `e:\CodeForJob\Cool\CLAUDE.md`（Current Status）
- Modify: `docs/superpowers/plans/2026-09-12-p5-monster-server.md`（勾选 + 偏差/验收记录）
- Create: `docs/superpowers/guides/p5-monster-server.md`
- Modify: `production/session-logs/session-log.md`

- [ ] **Step 1: 确认无引用后删除旧 AI**

```bash
grep -rn "KcpServer.AI" Server --include="*.cs" | grep -v "Server/AI/" | head   # 期望空（无外部引用）
git rm -r Server/AI
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q                    # 期望成功
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj         # 期望全绿
```
注意：旧 Protocol `Npc*.cs` 消息**不删**（MessageId 2000-2003 注册保留——只加不删）。

- [ ] **Step 2: CLAUDE.md 状态更新**

`e:\CodeForJob\Cool\CLAUDE.md` Current Status 区：
- `- [ ] NPC Mirror system` → `- [x] NPC Mirror system（P5 怪物服务端化：服务器 MonsterModule 行为树驱动 + 客户端 Npc 快照视图）`

- [ ] **Step 3: guide 文档**

`docs/superpowers/guides/p5-monster-server.md`（新建，内容要点）：
- 怪物模式启动：`dotnet run --project Server/Server3C.csproj -- 8000 --monsters 4`
- 固定布局表（T2 SpawnDefaultLayout 注释同款——e2e 依赖契约）
- 怪物生命周期：生成 → 巡逻/感知/追击/攻击 → 死亡展示 2s → 移除 → 30s 重生（新 id）
- 客户端接线：联网自动禁用本地 MonsterSpawner；Npc 视图球体占位
- 扩展点：新怪物模板（monster_config.json + CombatRules 技能行 + SkillIdOf 映射 + 客户端真实模型）

- [ ] **Step 4: 计划偏差/验收记录（本节落地后回填）**

本计划文件"执行偏差与验收记录"节回填：逐任务偏差、测试计数（Server.Tests / EditMode / PlayMode）、审计日志路径、backlog 新增项（含 D-P5 系列未落项）。

- [ ] **Step 5: 提交**

```bash
git add -A Server/ CLAUDE.md docs/superpowers/guides/p5-monster-server.md docs/superpowers/plans/2026-09-12-p5-monster-server.md production/session-logs/session-log.md
git commit -m "docs(monster): p5 closeout - remove dead AI code, guides, session log, backlog record"
```

---

## 执行偏差与验收记录（执行时回填，T6 Step 4）

**Commits**: T1 cbc54f6 → T2 f9ff103 → T3 0e114e4 → T4 4f99433 → T5 68371cc → 修复 18c25b6 → T6 bacaa44 → 终审修复 d3115ee

**逐任务偏差**（执行时按 P4 惯例记录——implementer 现场发现 + review 批准）：
1. T1（quality review 轮）：Wolf 伤害 15 注释出处不实（旧 AiComponent 对所有怪物硬编码 10，且死代码从未生效）——保留 15 为 P5 首次生效的差异化平衡选择（D-P5-2 已修正），注释改为准确出处并 amend（cbc54f6）
2. T2：ConfigLoader 加 `IncludeFields = true`——MonsterData 是 public 字段，System.Text.Json 默认不反序列化字段，整表静默落 0（templateId=0 → spawn 抛异常）；JsonOptions 仅服务 MonsterConfig 一处反序列化，唯一其他调用方为死代码 AiManager（同修复）——计划外但必要（plan 偏差 1）
3. T2：Server3C.cs 补 `using KcpServer.Monster;`（计划 Step 12 遗漏，编译必需）
4. T2：提交清单比 Step 15 多含 AoiBroadcasterTests.cs（Step 11 检查项强制——其 FieldMask 精确断言更新为含新掩码）、ConfigLoader.cs、两个 csproj（Step 15 清单不完整）
5. T2（quality review 轮）：`_pendingCasts` 无 drain 时无界增长（T2 过渡态缺口，T3 接线闭合）；测试 3 无裁决时冷却门从未生效（T3 补 MonsterCombatIntegrationTests 间距锁定）；关键路径测试缺口（目标丢失→Return、硬直窗口、重生新 id）留 T3/T5 补测；`done` 列表与 drain 列表每 tick 微分配（P5 规模可接受，已记 backlog）
6. T3：测试 `World()` 助手补 `Rotation = 180f`（计划文本 bug——AddNpc 朝向硬编码 0，怪物背对玩家致自动目标锥空；修复后 RED→GREEN 语义与计划一致，spec review 验证）；测试文件补 `using System;`（计划引用 Random 缺 using，ImplicitUsings=disable）
7. T3（quality review 轮）：drain 处 `if (resolution.Accepted)` 守卫使 HandleNpcCastAsync 拒绝分支（审计日志+计数）成为死代码——验收口径不可证伪；按审查改为直调（拒绝内部处理），GetObserversOf 复杂度注释 O(N)→O(N²)（amend 873e0b6）；static SeededRng 跨测试共享无当前 order 依赖（断言用伤害域非精确值）但后续加精确断言须局部化（留 T6 备注）；GetObserversOf 路由单测留 T5 e2e 覆盖（计划范围）
8. T4（quality review 轮）：`FindObjectOfType<MonsterSpawner>()` 单数只禁用一个实例，但 DemoDay.unity 有**两个**激活 spawner（SlimeSpawner + TurtleShellSpawner——质量审查实测发明）；改为 `FindObjectsOfType` 遍历全禁（amend 4964d40）。计划原文"当前无任何场景使用"事实错误——已修正 backlog 记录
9. T4（quality review 轮）：已生成本地怪物不回收的**具体泄漏面**（计划 backlog 仅笼统记述）：① 名牌残影——MonsterEntity.Init 注册名牌、NameplateRenderer.Tick 只判 Transform 非 null 不判 activeInHierarchy，禁用后名牌冻结在最后位置；② PhysicsRegistry 保留注册且 FindNearby 位置补充不判 active——不可见怪物仍可被本地攻击命中。均已写入 backlog
10. T4（quality review 轮 Minor）：ActionState 无条件覆盖与 Kind/TemplateId 仅创建时读取——当前安全（服务端唯一快照生产者 AoiBroadcaster 恒全掩码，审查实测确认），部分位快照场景不存在；字段掩码守卫注释留 T6（与 `_lastKnownYaw` 位掩码范式对齐）；RemoteEntityView 构造默认参数（kind=0 隐式）可读性留 Minor 不修
11. **T5 抓到产品缺陷（Critical，e2e 价值验证）**：`IsLoggedIn` 置在 LoginHandler 自建实例上（进 RoomManager），而 EntityStore 经 `RegisterEntity → AddPlayer` 另建实例保持 false——MonsterModule.DetectTarget 与 CastHandler 怪物候选过滤读 store 实例 → **线上怪物永远不感知、不攻击任何玩家**。T2/T3 单测直接对 store 实例置 true，掩盖了这条接线缺口。修复：`Server3C.RegisterEntity` 置 `player.IsLoggedIn = true`（登录路径唯一入口；BotRunner 直接 AddPlayer 不经此路径——D-P5-3 语义保持），T5 后续 commit 落地；MonsterWorldTests 成为该接线的端到端锁定
12. T5：测试文件功能等价偏差（计划文本缺陷）：① 补 `ConnectAndLogin("127.0.0.1", 8000)` ×3（计划漏写——不加则 WaitInWorld 永远超时）；② `FindHit` 参数收窄为单条 `DamageResult`（计划调用点传单条 vs 签名收 List，CS1503）；③ 保留证据字段（CastBroadcasts 计数 + 失败 message 附 monsters/wolfMinDist/casts/spawn——正是定位缺陷的关键）
13. T5：回归口径修正——AoiWorldTests 的"干净服务器"回归不成立（无 bot 时吃上一测试 15s 静默窗口的 ghost 假绿，实测 `sends/s=0`）；须 `--bots 100` 服务器重跑为真绿
14. T5：计划执行注意 #4 的 wolfId 脆弱点未触发（首个 wolf = 2_000_001 = Wolf[0]，即唯一能命中观察者那只）；审计留档 `production/verify/monster-audit.log` + 备份 `.monster-run.log`/`.coexist.log`/`.coexist-postfix.log`/`p5-regression-server.log`/`p5-regression-bots.log`
15. T5（修复后）：**Step 3c 共存抽查作废**——修复前通过是因为怪物当时惰性（IsLoggedIn 缺陷）；修复后 `--monsters 4` 下 P4 `CombatSyncTests.Cheat_Casts_Are_Rejected` FAIL（`expected exactly 1 CastBroadcast after double-cast, got 3`——狼仇恨原点两名客户端，其合法 npc CastBroadcast 经 GetObserversOf 广播污染按消息条数的断言；日志 0 拒绝 ⇒ 那 2 条是合法 npc 施法）。**非产品缺陷**（怪物广播语义正确）——P4 测试的全局消息计数断言风格与共存世界不兼容：P4 回归只在干净服务器跑，共存鲁棒性（按 caster id 过滤断言）留 backlog。附：`TwoClients_Damage_Numbers_Match` 共存下 PASS 但脆弱（`TargetRemainingHp == 100 - Damage` 假设无其他伤害源）
16. T5：服务器对 npc 施法**成功路径不打日志**（只记拒绝）——"npc cast rejected = 0" 须与测试 PASS 合读才是完整证据（客户端断言链：npc id 为源 + dmg 恒值 15 + 余血 85 精确——只有 npc 路径能产出）
17. 终审（Important）：e2e 史莱姆巡逻断言**假绿**——`lastSlimePos` 单变量被两只史莱姆（±40m 均在 80m 视野）共用，每采样累积跨距 ~160m（实测打印 6000.10m/4558.61m，真实巡逻 8s 上限 ~16m），`slimeDrift < 1f` 永不触发 → 巡逻冻结仍会绿。修复：漂移按视图 id 分开累积（d3115ee）。**教训：聚合量跨实体共享变量是假绿高发 pattern**
18. 终审（记录一致性）：偏差 10 承诺的字段掩码守卫注释未在 T6 落地（T6 未覆盖该文件）→ d3115ee 补注释钉住"唯一生产者恒全掩码"不变量
19. 终审（记录一致性）：偏差 5 承诺的关键路径测试（Return 分支/受击硬直窗口/重生新 id）未落地（T3 只落冷却间距）→ d3115ee 补齐 3 测试（断言按现行实现语义一次通过，无需弱化）
20. 终审（健壮性）：`MonsterBtActions.Attack` 缺表项时返回 Failure 致怪物在攻击距离永久静默冻结（零日志零计数）→ d3115ee 校验前移 `SpawnMonster` fail fast（抛 ArgumentException）；同文件"范围 = 表上限"注释修正
21. d3115ee 测试测量窗偏差：早退条件由"收到首个伤害即出"改为"保底 15 采样（3s）后早退"——首次狼贴脸 ghost 位时 1.45s 窗测得漂移 1.32m 贴阈值（偶发假 FAIL 风险）；阈值/语义不变，仅保证测量窗公平（快路径 ~3.5s）

**验收证据**（2026-09-12 实测，终审修复后最终态）：
- Server.Tests：**132/132**（P4 基线 115 + T1 3 + T2 4 + T3 7 + 终审补 3）
- EditMode：**200/200**（P4 基线 195 + T1 1 + T4 4）
- PlayMode：**MonsterWorldTests 2/2 ×2 类级连跑全绿**（终审修复后 Run1 9.91s / Run2 12.34s；断言全值通过：SourcePlayerId=2_000_001、Damage=15.0、TargetRemainingHp=85.0 精确——感知→追击→施法→裁决→DamageResult 全链端到端；巡逻漂移真实值 max 9.69m/12.59m——修复前 6000m 系假绿 artifact 见偏差 17）
- 回归（干净服务器）：MovementSync 4/4、TwoClientWorld 1/1、CombatSync 2/2、AoiWorld 1/1 全绿（AoiWorld 须 `--bots 100` 服务器——见偏差 13）
- 服务器审计日志：`production/verify/monster-audit.log`（npc cast rejected = 0、任意 cast rejected = 0、ERROR = 0、Monster mode: 4 monsters spawned；与测试 PASS 合读——成功路径不打日志，见偏差 16）
- 协议只加不删 ✓（EntitySnapshot 10/11 号段追加；MessageId 2000-2003 保留）
- 终审结论：**READY_WITH_NOTES** → 修复轮 d3115ee 落地后全部关闭

**P5 backlog（记录未做）**：
- 客户端真实目标 id 判别（D-P5-3 服务端候选集扩展已消除跨界误伤；客户端精确目标留后续）
- **友伤残余边界**：玩家挥击锥内存在比怪物更近的其他玩家时仍会命中该玩家（自动目标按最近者）——P4"PvP 联调期临时守卫"建议延续有效，PvP 落地前保留开关预案
- **联网场景 MonsterSpawner 清理**：T4 已禁用全部 spawner 组件（FindObjectsOfType——DemoDay 实有两个），但**已生成的本地怪物不回收**，实测两个残留：① 名牌残影（NameplateRenderer.Tick 不判 activeInHierarchy——禁用后名牌冻结在最后位置）；② PhysicsRegistry 保留注册（FindNearby 位置补充不判 active——不可见怪物仍可被本地攻击命中）。双模式共存需完整清理逻辑（隐藏/销毁已生成怪物 + 注销名牌与 PhysRegistry）；单机模式不受影响
- 怪物真实模型/动画表现（D-P5-7——随 P3 backlog"真实角色模型渲染"）；模板区分色（GetComponent 规则约束）
- AggroTable 伤害仇恨排名（D-P5-9——当前最近感知目标切换）
- 玩家死亡/复活；快照血条 MaxHp 消费；命中方向字段；投射物等玩家技能服务端化（P4 backlog 延续）
- GetObserversOf O(N²)（外层遍历 × 每观察者 GetVisible O(N)）反向索引优化（实体量大时——注释已留痕）；MonsterTickContext 每 tick 每怪分配 + DrainPendingCasts 空列表分配（怪物规模小时可接受，规模化时池化）
- 怪物施法被拒热循环守卫（当前防御 = 拒绝日志 + RejectedCasts 计数——服务器自产请求理论上必过校验，若出现即配置错误需修）
- **P4 CombatSyncTests 共存鲁棒性**（T5 偏差 15）：其断言按消息条数全局计数（`expected exactly 1 CastBroadcast`）且 `TargetRemainingHp == 100 - Damage` 假设无外部伤害源——怪物在场即污染/失效；P4 回归只在干净服务器跑。若需共存测试：断言按 caster id 过滤
- 服务器 npc 施法成功路径无日志（只记拒绝——审计口径须与客户端断言合读；加正向日志或计数器可强化证据链）
- production/verify/ 工件入 VCS 决策（延续）

---

## Self-Review（撰写自查——已并入上文）

**1. Spec 覆盖**：§5-P5 逐项 → MonsterAI 决策/感知移植服务端（T2 行为树/检测移植 + 死代码删除 T6）；巡逻/仇恨/攻击走 P2/P4 同步链路（巡逻追击 = EntityStore 实体 + AOI 快照链；攻击 = CombatResolver/CastHandler 复用）；打玩家触发 HitFSM 同步（DamageResult → ApplyServerDamage 既有链，T4 桥测试 + T5 e2e 断言）；验收"服务端驱动巡逻+仇恨追击正常"（T5-1 漂移 + 追击 + 伤害断言）、"玩家断线怪物世界持续运转"（T2 零玩家 tick 单测 + T5-2 重连同 id）。Out-of-Scope 未越界（背包/聊天等零触碰）。

**2. 占位符扫描**：无 TBD/TODO；每步代码完整。T4 Step 1 桥测试的 `client`/`registry` 夹具变量注明"按现有桥测试夹具对齐"——这是唯一引用既有文件惯例处（P4 计划同惯例），执行时读现有测试文件即可。

**3. 类型一致性**：`MonsterState.BehaviorTree`（Step 6 补注 + Step 10 使用一致）；`MonsterModule.GetState/DrainPendingCasts/DrainRemovedNpcs/QueueCast/SpawnDefaultLayout`（T2 定义、T3/T5 调用一致）；`CastHandler.Resolve(long, CastRequest, uint)`（T3 定义、Server3C 调用一致）；`HandleNpcCastAsync(long, CastResolution)`（T3 定义、调用一致）；`Server3C.GetObserversOf(long)`（T3 定义、CastHandler 调用一致）；`EntityKind/EntityFieldMask.Kind/TemplateId/EntitySnapshot.Kind/TemplateId`（T1 定义、T2/T4/T5 使用一致）；`CombatRules` 50001/50002（T1 定义、T2 Attack 动作/T3 测试一致）；`RemoteEntityView.Kind/TemplateId/ActionState`（T4 定义、测试一致）；`MonsterData` 字段（moveSpeed/detectionRadius/visionAngle/attackRange/patrolRadius/hp/name——T2 使用与 `Server/Config/MonsterConfig.cs` 一致）。

**4. 规模说明**：6 个任务——协议（T1）、服务端怪物模块（T2）、战斗集成（T3）、客户端视图（T4）、e2e 验收（T5）、收尾（T6）。风险集中：感知/追击时序确定性（T2 固定布局 + 200 tick 上限备注）、Chase 修正语义（T2 移植注释 + 测试锁定）、候选集策略对 P4 回归影响（T3 全量回归 + T5 类级连跑）、FieldMask 精确值断言（T2 Step 11 检查注）。客户端本地战斗零触碰（本地怪物模式保留——联网才禁用 spawner）。
