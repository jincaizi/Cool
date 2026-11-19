# B5 位移/跳跃预算 + PvP 控制实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让服务端只按**接收 tick** 发放玩家位移预算（普通移动/冲刺/防御、技能突进、跳跃惯性），并落地 PvP 玩家 720 HP、控制投影的本地消费，为 Plan 04 的元素状态提供可注入的 `SpeedScale/ActionLock/ControlVersion` 通道。

**架构：** 协议只加不删：`PositionSyncRequest` 追加运动模式、跳跃阶段/序号/垂直速度、位移技能与 `CastId`、控制版本与 `LifeId`；服务端新增 `MovementBudgetMath`（SyncCore 纯函数）与 `PositionSyncHandler` 的严格预算分支；技能突进窗口由 `CombatResolver.Decide` 在权威接受施法时写入 `PlayerState.Displacement`；客户端拆分 `SkillSpeedScale`/`StatusSpeedScale` 并在 `CharacterController` 单点合成，`MovementReporter` 上报同样的字段，`ClientNetworkHost` 消费本人 `CombatStateUpdate.Control`。Plan 03 不实现元素状态运行时——`Server3C.ApplyPlayerControl` 是 Plan 04 的注入入口，本计划用测试直接调用它验证闭环。

**技术栈：** Unity 2022.3.25f1 / C# 9、.NET 8 服务端、protobuf-net 3.2.56、NUnit（Unity EditMode/PlayMode）、xUnit（Server.Tests）。

---

## 全局约束

- **只信服务端接收 tick**：`PositionSyncRequest.Timestamp` 继续保留用于诊断/兼容，但预算一律按 `Server3C.CurrentTick` 计算；客户端时间戳不得参与位移额度。
- **只加不删**：`PositionSyncRequest` 既有 tag 1–9、`PositionSyncResponse` 既有 tag 1–8 不动；新字段全部追加高位 tag。`ProtocolVersion` 保持 1。
- **旧客户端不收紧**：只有登录时携带 `ProtocolFeatures.PvpMovementBudgetV1` 的玩家（`PlayerState.StrictMovementBudget = true`）才走新预算；其余玩家保持现有 `MovementValidator` 路径。
- **`SyncCore` 零 UnityEngine**：`MovementBudgetMath` 与 `CombatRules` 必须可被服务端和客户端同源编译。
- **协议成员禁止 `List<T>`**：新字段只用标量/`IList<T>`；`ProtocolShapeGuardTests` 必须继续通过。
- **控制状态只由服务端写**：Plan 03 用 `Server3C.ApplyPlayerControl(playerId, speedScale, actionLock)` 注入；Plan 04 的状态运行时会调用同一入口。客户端不得自行判定麻痹/减速。
- **PvP HP 720 的作用域**：仅对登录时协商 `ElementalSwordQiV1` 的玩家生效；测试 bot 保持 100 HP。NPC 对玩家的直接伤害按 `target.MaxHp / 100` 缩放，保持现有 PvE TTK；玩家对玩家/怪物的伤害不缩放。
- **技能位移只在权威窗口发放**：窗口由 `CombatResolver.Decide` 在 `CastId` 分配后写入，取消/死亡/复活/新施法都会清掉旧窗口；客户端提前开突进造成的偏差由下一次位置确认收敛。
- **不要改全局 `Animator.speed`**；不要引入第二条位置通道；不要用 `dotnet run` 起服务端。

## 文件结构

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/AOT/Protocol/Messages/MovementMode.cs` | `LocomotionMode` / `PlayerJumpPhase` 枚举 |
| `Assets/Scripts/AOT/Protocol/Messages/PositionSync.cs` | 追加位置请求/响应字段 |
| `Assets/Scripts/AOT/Protocol/Messages/NpcAction.cs` | `ProtocolFeatures.PvpMovementBudgetV1` |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 共享走/跑/防御速度、PvE 伤害基准 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/MovementBudgetMath.cs` | 预算纯函数（tick 差、速度合成、突进/跳跃距离、水平/Y 夹取） |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/*` | 突进与跳跃共享表字段、校验、指纹 |
| `Server/Player/PlayerState.cs` | 控制状态、严格预算开关、最大生命、`MovementBudgetState`/`DisplacementWindow` |
| `Server/Player/MovementBudgetState.cs` | 每玩家预算游标（上次 tick、突进已用距离、跳跃状态） |
| `Server/Player/DisplacementWindow.cs` | 一次施法的权威突进窗口 |
| `Server/Handler/PositionSyncHandler.cs` | 接收 tick 预算、突进/跳跃、Y 夹取、`LifeId`/控制版本门 |
| `Server/Handler/CastHandler.cs` | 控制锁拒绝施法、`Decide` 写突进窗口、取消清窗口 |
| `Server/Server3C.cs` | `ApplyPlayerControl` 注入入口、地面高度接线 |
| `Server/Handler/LoginHandler.cs` | 登录战斗基线（严格预算位、720 HP） |
| `Server/Aoi/CombatProjectionBuilder.cs` | `ControlSummary` 从权威状态填充 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs` | 速度拆分/合成、`ActionLock`、跳跃序号与空中惯性、最大生命 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/LocalControlState.cs` | 本人控制版本/倍率/动作锁的纯状态合并（可单测） |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDashComponent.cs` | 共享起手/收尾窗口、状态速度倍率 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/IDashComponent.cs` / `SkillExecutor.cs` | 突进参数来源改为共享表 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 接线 `SkillSpeedScale`、控制锁、最大生命、突进/技能查询 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs` | 上报新位置字段与跳跃阶段状态机 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | 记录已确认 `CastId`（按技能） |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 本人投影消费、运动状态 provider 接线 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs` | 登录能力位 |
| `Server/Config/elemental_catalog.json` | 突进/跳跃默认表 |

---

### 任务 1：共享表加入突进/跳跃参数与移动常量

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefinition.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefaults.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogValidator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogHasher.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`
- 修改：`Server/Config/elemental_catalog.json`
- 测试：`Server/Server.Tests/Elemental/ElementalCatalogValidatorTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncCore/ElementalCatalogTests.cs`

- [x] **步骤 1：写服务端失败测试**

在 `ElementalCatalogValidatorTests` 追加：

```csharp
[Fact]
public void Defaults_Carry_20001_Displacement_And_Jump_Settings()
{
    var catalog = ElementalCatalogDefaults.Create();

    var dash = Assert.Single(catalog.Displacements);
    Assert.Equal(20001, dash.SkillId);
    Assert.Equal(2f, dash.DashDistance);
    Assert.Equal(0.3f, dash.DashDuration, 4);
    Assert.Equal(0.05f, dash.StartupSeconds, 4);
    Assert.Equal(0.05f, dash.RecoverySeconds, 4);

    Assert.Equal(10.95f, catalog.Jump.JumpForce, 4);
    Assert.Equal(-30f, catalog.Jump.Gravity, 4);
    Assert.Equal(0.5f, catalog.Jump.MaxRiseSeconds, 4);
    Assert.Empty(ElementalCatalogValidator.Validate(catalog));
}

[Fact]
public void Validator_Rejects_Displacement_Whose_Windows_Consume_The_Duration()
{
    var catalog = ElementalCatalogDefaults.Create();
    catalog.Displacements[0].StartupSeconds = 0.2f;
    catalog.Displacements[0].RecoverySeconds = 0.2f;

    Assert.Contains(ElementalCatalogValidator.Validate(catalog),
        e => e.Contains("displacement timing"));
}

[Fact]
public void Validator_Rejects_Jump_Rise_Cap_Below_Natural_Rise()
{
    var catalog = ElementalCatalogDefaults.Create();
    // natural rise = 10.95 / 30 = 0.365s
    catalog.Jump.MaxRiseSeconds = 0.3f;

    Assert.Contains(ElementalCatalogValidator.Validate(catalog),
        e => e.Contains("jump settings"));
}

[Fact]
public void Validator_Rejects_Non_Finite_Jump_Gravity()
{
    var catalog = ElementalCatalogDefaults.Create();
    catalog.Jump.Gravity = float.NegativeInfinity;

    Assert.Contains(ElementalCatalogValidator.Validate(catalog),
        e => e.Contains("jump settings"));
}
```

- [x] **步骤 2：运行测试确认失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ElementalCatalogValidatorTests"`
预期：编译失败（`Displacements`/`Jump` 不存在）。

- [x] **步骤 3：实现共享表与常量**

`ElementalCatalogDefinition.cs` 追加：

```csharp
[Serializable]
public sealed class DisplacementDefinition
{
    public int SkillId;
    public float DashDistance;
    public float DashDuration;
    public float StartupSeconds = 0.05f;
    public float RecoverySeconds = 0.05f;
}

[Serializable]
public sealed class JumpSettings
{
    public float JumpForce = 10.95f;
    public float Gravity = -30f;
    public float MaxRiseSeconds = 0.5f;
}
```

在 `ElementalCatalogDefinition` 的 `SwordQi` 之后追加：

```csharp
public List<DisplacementDefinition> Displacements = new List<DisplacementDefinition>();
public JumpSettings Jump = new JumpSettings();
```

`ElementalCatalogDefaults.cs`：把现有 `Create()` 改名为 `Build()`，`Create()` 返回 `Build()`；新增只读查询：

```csharp
private static readonly ElementalCatalogDefinition Shared = Build();

public static ElementalCatalogDefinition Create() => Build();

public static DisplacementDefinition? FindDisplacement(int skillId)
{
    foreach (var d in Shared.Displacements)
        if (d.SkillId == skillId) return d;
    return null;
}

public static JumpSettings Jump => Shared.Jump;
```

在默认对象的 `SwordQi` 之后、`Pvp` 之前加入：

```csharp
Displacements = new List<DisplacementDefinition>
{
    new DisplacementDefinition
    {
        SkillId = 20001, DashDistance = 2f, DashDuration = 0.3f,
        StartupSeconds = 0.05f, RecoverySeconds = 0.05f
    },
},
Jump = new JumpSettings { JumpForce = 10.95f, Gravity = -30f, MaxRiseSeconds = 0.5f },
```

`ElementalCatalogValidator.cs` 在 `SwordQi` 校验之后加入：

```csharp
var displacementIds = new HashSet<int>();
foreach (var d in catalog.Displacements)
{
    if (d == null) { errors.Add("displacement missing"); continue; }
    if (d.SkillId <= 0 || !displacementIds.Add(d.SkillId))
        errors.Add("displacement duplicate/invalid id");
    if (!Positive(d.DashDistance) || !Positive(d.DashDuration)
        || !Nonnegative(d.StartupSeconds, d.RecoverySeconds)
        || d.DashDuration <= d.StartupSeconds + d.RecoverySeconds)
    {
        errors.Add("displacement timing out of range");
        continue;
    }
    float active = d.DashDuration - d.StartupSeconds - d.RecoverySeconds;
    float speed = d.DashDistance / active;
    if (!Positive(speed) || speed > 60f)
        errors.Add("displacement speed out of range");
}

var jump = catalog.Jump;
if (jump == null || !Positive(jump.JumpForce) || !NegativeFinite(jump.Gravity)
    || !Positive(jump.MaxRiseSeconds)
    || jump.MaxRiseSeconds < jump.JumpForce / -jump.Gravity)
    errors.Add("jump settings out of range");
```

并在 `ElementalCatalogValidator` 的私有 helper 区追加：

```csharp
private static bool NegativeFinite(float v)
    => !float.IsNaN(v) && !float.IsInfinity(v) && v < 0f;
```

`ElementalCatalogHasher.cs` 在写入 `timings` 之后、`g` 之前加入：

```csharp
var displacements = new List<DisplacementDefinition>(catalog.Displacements);
displacements.Sort((a, b) => a.SkillId.CompareTo(b.SkillId));
w.Write(displacements.Count);
foreach (var d in displacements)
{
    w.Write(d.SkillId);
    F(w, d.DashDistance, d.DashDuration, d.StartupSeconds, d.RecoverySeconds);
}

F(w, catalog.Jump.JumpForce, catalog.Jump.Gravity, catalog.Jump.MaxRiseSeconds);
```

`CombatRules.cs` 在 `CritMultiplier` 附近追加：

```csharp
/// <summary>普通移动/冲刺基础速度（与 CharacterController 默认值同源）。</summary>
public const float WalkSpeed = 5f;
public const float SprintSpeed = 8f;

/// <summary>防御姿态水平速度倍率（客户端与服务端预算共用）。</summary>
public const float DefendSpeedMultiplier = 0.4f;

/// <summary>PvE 伤害基准生命：NPC 对玩家的伤害按 target.MaxHp / 该值缩放，保持原 TTK。</summary>
public const float PveDamageBaselineHp = 100f;
```

`Server/Config/elemental_catalog.json` 在 `"SwordQi"` 之后、`"Pvp"` 之前加入：

```json
"Displacements": [
    { "SkillId": 20001, "DashDistance": 2.0, "DashDuration": 0.3, "StartupSeconds": 0.05, "RecoverySeconds": 0.05 }
],
"Jump": { "JumpForce": 10.95, "Gravity": -30.0, "MaxRiseSeconds": 0.5 },
```

- [x] **步骤 4：补 Unity 默认表断言**

在 `ElementalCatalogTests.cs` 追加：

```csharp
[Test]
public void Defaults_Contain_Shared_Displacement_And_Jump_Parameters()
{
    var catalog = ElementalCatalogDefaults.Create();

    Assert.AreEqual(1, catalog.Displacements.Count);
    Assert.AreEqual(20001, catalog.Displacements[0].SkillId);
    Assert.AreEqual(2f, catalog.Displacements[0].DashDistance, 0.0001f);
    Assert.AreEqual(0.3f, catalog.Displacements[0].DashDuration, 0.0001f);

    Assert.AreEqual(10.95f, catalog.Jump.JumpForce, 0.0001f);
    Assert.AreEqual(-30f, catalog.Jump.Gravity, 0.0001f);
    Assert.AreEqual(0.5f, catalog.Jump.MaxRiseSeconds, 0.0001f);
    Assert.IsNotNull(ElementalCatalogDefaults.FindDisplacement(20001));
    Assert.IsNull(ElementalCatalogDefaults.FindDisplacement(10002));
}
```

- [x] **步骤 5：运行测试**

运行：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ElementalCatalogValidatorTests"
bash tools/unity-tests.sh EditMode ElementalCatalogTests
```
预期：全部 PASS。

- [x] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore Server/Config/elemental_catalog.json \
  Server/Server.Tests/Elemental/ElementalCatalogValidatorTests.cs \
  Assets/Scripts/Tests/Editor/SyncCore/ElementalCatalogTests.cs
git commit -m "feat(sync): share displacement and jump parameters"
```

---

### 任务 2：协议追加位移/跳跃/控制字段

**文件：**
- 创建：`Assets/Scripts/AOT/Protocol/Messages/MovementMode.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/PositionSync.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/NpcAction.cs`
- 修改：`Assets/Scripts/AOT/Protocol/README.md`
- 测试：`Server/Server.Tests/Protocol/CodecRoundtripTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs`

- [x] **步骤 1：写失败测试**

服务端 `CodecRoundtripTests` 追加：

```csharp
[Fact]
public void Roundtrip_PositionSyncRequest_Preserves_PvpMovement_Fields()
{
    var source = new PositionSyncRequest
    {
        X = 1f, Y = 2f, Z = 3f, Rotation = 45f, Speed = 6f,
        Sequence = 9,
        LocomotionMode = LocomotionMode.Sprint,
        JumpPhase = PlayerJumpPhase.Air,
        JumpSeq = 7,
        VerticalVelocity = -4.5f,
        DisplacementSkillId = 20001,
        CastId = 123456789UL,
        ClientControlVersion = 3,
        LifeId = 5
    };

    var encoded = MessageCodec.EncodeToArray(MessageId.PositionSyncRequest, source);
    var decoded = MessageCodec.Decode(encoded);

    var got = Assert.IsType<PositionSyncRequest>(decoded.message);
    Assert.Equal(1f, got.X);
    Assert.Equal(LocomotionMode.Sprint, got.LocomotionMode);
    Assert.Equal(PlayerJumpPhase.Air, got.JumpPhase);
    Assert.Equal(7u, got.JumpSeq);
    Assert.Equal(-4.5f, got.VerticalVelocity);
    Assert.Equal(20001, got.DisplacementSkillId);
    Assert.Equal(123456789UL, got.CastId);
    Assert.Equal(3u, got.ClientControlVersion);
    Assert.Equal(5u, got.LifeId);
}

[Fact]
public void Roundtrip_PositionSyncResponse_Carries_ControlVersion()
{
    var source = new PositionSyncResponse { AcknowledgedSequence = 4, ControlVersion = 12 };
    var encoded = MessageCodec.EncodeToArray(MessageId.PositionSyncResponse, source);
    var decoded = MessageCodec.Decode(encoded);
    var got = Assert.IsType<PositionSyncResponse>(decoded.message);
    Assert.Equal(12u, got.ControlVersion);
}
```

Unity `ProtocolShapeGuardTests` 追加：

```csharp
[Test]
public void PositionSyncRequest_PvpMovement_Tags_Are_Pinned()
{
    Assert.AreEqual(10, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("LocomotionMode")!));
    Assert.AreEqual(11, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("JumpPhase")!));
    Assert.AreEqual(12, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("JumpSeq")!));
    Assert.AreEqual(13, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("VerticalVelocity")!));
    Assert.AreEqual(14, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("DisplacementSkillId")!));
    Assert.AreEqual(15, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("CastId")!));
    Assert.AreEqual(16, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("ClientControlVersion")!));
    Assert.AreEqual(17, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncRequest).GetProperty("LifeId")!));
    Assert.AreEqual(9, GetProtoMemberTag(typeof(Cool.Protocol.PositionSyncResponse).GetProperty("ControlVersion")!));
}
```

- [x] **步骤 2：运行测试确认失败**

运行：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CodecRoundtripTests"
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
```
预期：编译失败（新属性不存在）。

- [x] **步骤 3：实现协议**

创建 `MovementMode.cs`：

```csharp
namespace Cool.Protocol
{
    /// <summary>位置上报的水平运动模式（只用于服务端预算；防御仍走 IsDefending）。</summary>
    public enum LocomotionMode
    {
        Walk = 0,
        Sprint = 1
    }

    /// <summary>跳跃阶段（None=0 让旧客户端缺省不产生事件）。</summary>
    public enum PlayerJumpPhase
    {
        None = 0,
        Start = 1,
        Air = 2,
        End = 3
    }
}
```

`PositionSync.cs` 在 `RespawnTick` 之后追加：

```csharp
/// <summary>水平运动模式（Plan 03；缺省 0 = Walk，旧客户端不会发送）。</summary>
[ProtoMember(10)] public LocomotionMode LocomotionMode { get; set; }

/// <summary>跳跃阶段（Start 只在每个新 JumpSeq 出现一次）。</summary>
[ProtoMember(11)] public PlayerJumpPhase JumpPhase { get; set; }

/// <summary>起跳序号：服务端按首次收到的新序号记录起跳 tick；重复 Start 不重置。</summary>
[ProtoMember(12)] public uint JumpSeq { get; set; }

/// <summary>垂直速度（只用于区分上升/下落，不参与预算）。</summary>
[ProtoMember(13)] public float VerticalVelocity { get; set; }

/// <summary>正在释放的位移技能 ID；0 = 无位移技能。</summary>
[ProtoMember(14)] public int DisplacementSkillId { get; set; }

/// <summary>位移技能对应的已确认 CastId；0 = 未确认/非位移。</summary>
[ProtoMember(15)] public ulong CastId { get; set; }

/// <summary>客户端最后应用的控制版本；服务端拒绝低于当前权威版本的包。</summary>
[ProtoMember(16)] public uint ClientControlVersion { get; set; }

/// <summary>生命代次；非 0 且与服务端不一致时整包丢弃。</summary>
[ProtoMember(17)] public uint LifeId { get; set; }
```

`PositionSyncResponse` 在 `RespawnTick` 之后追加：

```csharp
/// <summary>服务端当前控制版本（客户端据此识别旧输入；解除以 CombatStateUpdate 为准）。</summary>
[ProtoMember(9)] public uint ControlVersion { get; set; }
```

`NpcAction.cs` 的 `ProtocolFeatures` 追加：

```csharp
/// <summary>PvP 位移预算 v1：位置包携带运动/跳跃/突进/控制版本字段。</summary>
public const uint PvpMovementBudgetV1 = 1u << 3;
```

- [x] **步骤 4：运行测试**

运行：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CodecRoundtripTests"
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
```
预期：PASS。

- [x] **步骤 5：更新协议 README**

在 `Assets/Scripts/AOT/Protocol/README.md` 的“属性剑气 v1”表格后追加：

```markdown
`PositionSyncRequest` 追加（Plan 03，全部高位 tag）：
tag10 `LocomotionMode`、tag11 `JumpPhase`、tag12 `JumpSeq`、tag13 `VerticalVelocity`、
tag14 `DisplacementSkillId`、tag15 `CastId`、tag16 `ClientControlVersion`、tag17 `LifeId`；
`PositionSyncResponse` tag9 `ControlVersion`。能力位
`ProtocolFeatures.PvpMovementBudgetV1 = 1u << 3`；未携带该位的旧客户端继续走旧
`MovementValidator` 路径（不按接收 tick 收紧）。
```

- [x] **步骤 6：Commit**

```bash
git add Assets/Scripts/AOT/Protocol
git add Server/Server.Tests/Protocol/CodecRoundtripTests.cs
git add Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs
git commit -m "feat(protocol): add pvp movement budget fields"
```

---

### 任务 3：服务端控制状态、突进窗口与投影

**文件：**
- 创建：`Server/Player/MovementBudgetState.cs`
- 创建：`Server/Player/DisplacementWindow.cs`
- 修改：`Server/Player/PlayerState.cs`
- 修改：`Server/Aoi/CombatProjectionBuilder.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Server/Handler/CastHandler.cs`
- 测试：`Server/Server.Tests/Aoi/CombatProjectionBuilderTests.cs`
- 测试：`Server/Server.Tests/Handler/PlayerControlStateTests.cs`（新建）
- 测试：`Server/Server.Tests/Combat/CombatResolverControlLockTests.cs`（新建）

- [x] **步骤 1：写失败测试**

`CombatProjectionBuilderTests` 追加：

```csharp
[Fact]
public void Projection_Carries_Authoritative_Control_State()
{
    var store = new EntityStore();
    var player = store.AddPlayer(1, "P1");
    player.ApplyControl(0.7f, true);

    var projection = CombatProjectionBuilder.Build(player, ElementalCatalogDefaults.Create());

    Assert.NotNull(projection.Control);
    Assert.Equal(0.7f, projection.Control!.SpeedScale, 4);
    Assert.True(projection.Control.ActionLock);
    Assert.Equal(player.ControlVersion, projection.Control.ControlVersion);
}
```

新建 `PlayerControlStateTests`：

```csharp
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class PlayerControlStateTests
{
    [Fact]
    public void ApplyControl_Clamps_And_Bumps_Version_Only_On_Change()
    {
        var player = new PlayerState(1);

        player.ApplyControl(0.7f, false);
        Assert.Equal(0.7f, player.ControlSpeedScale, 4);
        Assert.Equal(1u, player.ControlVersion);

        player.ApplyControl(0.7f, false);
        Assert.Equal(1u, player.ControlVersion);

        player.ApplyControl(float.NaN, true);
        Assert.Equal(0f, player.ControlSpeedScale);
        Assert.True(player.ControlActionLock);
        Assert.Equal(2u, player.ControlVersion);
    }

    [Fact]
    public void MarkDead_Resets_Control_But_Keeps_Death_Version_Advance()
    {
        var player = new PlayerState(1);
        player.ApplyControl(0f, true);
        var before = player.CombatVersion;

        player.MarkDead(10);

        Assert.Equal(1f, player.ControlSpeedScale);
        Assert.False(player.ControlActionLock);
        Assert.True(player.CombatVersion > before);
    }

    [Fact]
    public void SetMaxHp_Fills_Login_Health()
    {
        var player = new PlayerState(1);

        player.SetMaxHp(720f);

        Assert.Equal(720f, player.MaxHp);
        Assert.Equal(720f, player.Hp);
    }
}
```

新建 `CombatResolverControlLockTests`：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CombatResolverControlLockTests
{
    [Fact]
    public void Decide_Rejects_New_Cast_While_ActionLocked()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.ApplyControl(1f, true);
        var resolver = new CombatResolver(store, new System.Random(1));
        var request = new CastRequest
        {
            SkillId = 10001,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        };

        var result = resolver.Decide(1, request, tick: 10);

        Assert.False(result.Accepted);
        Assert.Equal("control-lock", result.RejectReason);
    }

    [Fact]
    public void Decide_Opens_Displacement_Window_For_20001()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        var resolver = new CombatResolver(store, new System.Random(1));
        var request = new CastRequest
        {
            SkillId = 20001,
            Shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f }
        };

        var result = resolver.Decide(1, request, tick: 10);

        Assert.True(result.Accepted);
        Assert.NotNull(caster.Displacement);
        Assert.Equal(result.CastId, caster.Displacement!.CastId);
        Assert.Equal(20001, caster.Displacement.SkillId);
        Assert.Equal(10u, caster.Displacement.StartTick);
    }
}
```

- [x] **步骤 2：运行测试确认失败**

运行（in-process runner 的 `--filter` 只支持单个子串，不要用 `A|B` 复合过滤）：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionBuilderTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PlayerControlStateTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolverControlLockTests"
```
预期：编译失败（`ApplyControl`/`Displacement` 不存在）。

- [x] **步骤 3：实现服务端状态**

`MovementBudgetState.cs`：

```csharp
namespace KcpServer
{
    /// <summary>每玩家位置预算游标（主循环独占）。</summary>
    public sealed class MovementBudgetState
    {
        public uint LastBudgetTick;
        public uint LastJumpSeq;
        public bool JumpActive;
        public long JumpStartTick;
        public float JumpStartY;
        public float JumpHorizontalSpeed;
        public float JumpGrantedDistance;

        public void Reset()
        {
            LastBudgetTick = 0;
            LastJumpSeq = 0;
            JumpActive = false;
            JumpStartTick = 0;
            JumpStartY = 0f;
            JumpHorizontalSpeed = 0f;
            JumpGrantedDistance = 0f;
        }
    }
}
```

`DisplacementWindow.cs`：

```csharp
namespace KcpServer
{
    /// <summary>一次已确认施法的权威技能位移窗口（服务端主循环独占）。</summary>
    public sealed class DisplacementWindow
    {
        public ulong CastId;
        public int SkillId;
        public uint StartTick;
        public uint EndTick;
        public float TotalDistance;
        public float Speed;
        public float GrantedDistance;
    }
}
```

`PlayerState.cs` 追加字段与方法：

```csharp
/// <summary>该会话是否启用接收 tick 位移预算（登录能力位 PvpMovementBudgetV1）。</summary>
public bool StrictMovementBudget { get; internal set; }

/// <summary>权威控制状态（Plan 03 由 ApplyPlayerControl 注入；Plan 04 由状态运行时写）。</summary>
public float ControlSpeedScale { get; internal set; } = 1f;
public bool ControlActionLock { get; internal set; }
public uint ControlVersion { get; internal set; }

public MovementBudgetState Movement { get; } = new MovementBudgetState();

/// <summary>当前权威突进窗口；null = 无。</summary>
public DisplacementWindow? Displacement { get; internal set; }

public void ApplyControl(float speedScale, bool actionLock)
{
    if (!float.IsFinite(speedScale)) speedScale = 0f;
    speedScale = Math.Clamp(speedScale, 0f, 4f);
    if (speedScale == ControlSpeedScale && actionLock == ControlActionLock) return;
    ControlSpeedScale = speedScale;
    ControlActionLock = actionLock;
    ControlVersion++;
    BumpCombatVersion();
}

/// <summary>登录基线用：设置上限并回满（只在会话初始化调用；中途改上限不heal）。</summary>
public void SetMaxHp(float maxHp)
{
    if (!float.IsFinite(maxHp) || maxHp <= 0f) return;
    MaxHp = maxHp;
    Hp = maxHp;
    BumpCombatVersion();
}
```

`MarkDead` 与 `TryRespawn` 在 `BumpCombatVersion()` 之前重置控制（不要调用 `ApplyControl`，避免二次 bump）：

```csharp
ControlSpeedScale = 1f;
ControlActionLock = false;
ControlVersion++;
```

`MarkDead` 与 `TryRespawn` 都要 `Displacement = null; Movement.Reset();`（死亡立即关闭突进/跳跃窗口；
`MarkDead` 只在首次进入时执行，幂等分支不要重复 reset）。

`CombatProjectionBuilder.cs` 把：

```csharp
Control = new ControlSummary { SpeedScale = 1f, ActionLock = false, ControlVersion = 0 },
```

改为：

```csharp
Control = new ControlSummary
{
    SpeedScale = entity.ControlSpeedScale,
    ActionLock = entity.ControlActionLock,
    ControlVersion = entity.ControlVersion
},
```

`Server3C.cs` 追加：

```csharp
/// <summary>Plan 03/04 的状态运行时入口：写权威控制状态；ActionLock 首次生效时取消未提交施法。
/// 已发射的独立投射物继续飞行（CancelCaster 只清未提交段，Plan 02 语义）。</summary>
public void ApplyPlayerControl(long playerId, float speedScale, bool actionLock)
{
    var player = _entityStore.GetPlayer(playerId);
    if (player == null) return;
    bool wasLocked = player.ControlActionLock;
    player.ApplyControl(speedScale, actionLock);
    if (!wasLocked && actionLock)
    {
        var cancels = _castHandler.CancelCaster(playerId, CurrentTick, NpcActionCancelReason.Hit);
        _castHandler.BroadcastCancel(cancels);
    }
}
```

`CastHandler.cs` 的 `CombatResolver.Decide`：在 `if (!caster.IsAlive)` 之后加：

```csharp
if (caster.ControlActionLock) return Reject(resolution, caster, request, "control-lock");
```

在 `ulong castId = ++_nextCastId;` 之后、构造 `Broadcast` 之前，插入（把下面 `var casterState` 的声明提前，避免重复）：

```csharp
var casterState = _store.GetPlayer(casterId);
var displacement = _elementalCatalog.Displacements.Find(d => d.SkillId == request.SkillId);
if (displacement != null && casterState != null)
{
    float activeSeconds = displacement.DashDuration
        - displacement.StartupSeconds - displacement.RecoverySeconds;
    casterState.Displacement = new DisplacementWindow
    {
        CastId = castId,
        SkillId = request.SkillId,
        StartTick = tick,
        // 状态减速会把同一段距离拉长；4× 窗口覆盖 SlowRatio ≤ 0.75，总距离仍按
        // DashDistance + 1 帧封顶——窗口只防旧 CastId 在很久以后被复用。
        EndTick = tick + SecondsToTicks(displacement.DashDuration * 4f),
        TotalDistance = displacement.DashDistance,
        Speed = activeSeconds > 0f ? displacement.DashDistance / activeSeconds : 0f
    };
}
```

`ClearActiveCast` 里追加（同一 `castId` 才清）：

```csharp
if (caster.Displacement != null && caster.Displacement.CastId == castId)
    caster.Displacement = null;
```

- [x] **步骤 4：运行测试**

运行：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionBuilderTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PlayerControlStateTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolverControlLockTests"
```
预期：PASS；再跑一次全量确认没有破坏 Plan 02 的取消路径。

- [x] **步骤 5：Commit**

```bash
git add Server/Player Server/Aoi/CombatProjectionBuilder.cs Server/Server3C.cs Server/Handler/CastHandler.cs
git add Server/Server.Tests/Aoi/CombatProjectionBuilderTests.cs Server/Server.Tests/Handler/PlayerControlStateTests.cs Server/Server.Tests/Combat/CombatResolverControlLockTests.cs
git commit -m "feat(server): expose authoritative control and displacement window"
```

---

### 任务 4：共享位移预算纯函数

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/MovementBudgetMath.cs`
- 测试：`Server/Server.Tests/SyncCore/MovementBudgetFormulaTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncCore/MovementBudgetMathTests.cs`（新建）

- [x] **步骤 1：写失败测试**

服务端 `MovementBudgetFormulaTests`：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class MovementBudgetFormulaTests
{
    [Fact]
    public void NormalDistance_Composes_Walk_Defend_And_Status()
    {
        float distance = MovementBudgetMath.NormalDistance(
            LocomotionMode.Walk, defending: true, statusScale: 0.7f, seconds: 0.1f);

        Assert.Equal(5f * 0.4f * 0.7f * 0.1f, distance, 4);
    }

    [Fact]
    public void DeltaSeconds_Is_Capped_At_Half_A_Second()
    {
        Assert.Equal(0.5f, MovementBudgetMath.ClampDeltaSeconds(100u, 90u), 4);
        Assert.Equal(0f, MovementBudgetMath.ClampDeltaSeconds(10u, 10u), 4);
        Assert.Equal(0f, MovementBudgetMath.ClampDeltaSeconds(9u, 10u), 4);
    }

    [Fact]
    public void DashDistance_Is_Capped_By_Remaining_Distance()
    {
        float dash = MovementBudgetMath.DashSpeed(2f, 0.3f, 0.05f, 0.05f);
        Assert.Equal(10f, dash, 4);

        float granted = MovementBudgetMath.DashDistance(dash, 0.7f, 0.5f, remaining: 1.2f);
        Assert.Equal(1.2f, granted, 4);
    }

    [Fact]
    public void ClampHorizontal_Preserves_Direction_And_Length()
    {
        var prev = new Vec3(0f, 0f, 0f);
        var next = new Vec3(3f, 0f, 4f);

        var clamped = MovementBudgetMath.ClampHorizontal(prev, next, allowedDistance: 2.5f);

        Assert.Equal(1.5f, clamped.X, 4);
        Assert.Equal(2f, clamped.Z, 4);
        Assert.Equal(0f, clamped.Y, 4);
    }

    [Fact]
    public void MaxJumpY_Follows_Projectile_Motion_To_Apex()
    {
        float apex = 10.95f / 30f;
        float y = MovementBudgetMath.MaxJumpY(0.3f, 10.95f, -30f, apex);
        Assert.Equal(0.3f + 10.95f * 10.95f / (2f * 30f), y, 4);

        float after = MovementBudgetMath.MaxJumpY(0.3f, 10.95f, -30f, 10f);
        Assert.Equal(y, after, 4);
    }

    [Fact]
    public void DeltaSeconds_Uses_Tick_Rate_Below_The_Cap()
    {
        Assert.Equal(3f / 15f, MovementBudgetMath.ClampDeltaSeconds(4u, 1u), 4);
    }

    [Fact]
    public void NormalDistance_Covers_Sprint_Status_Clamp_And_Zero_Seconds()
    {
        Assert.Equal(8f * 0.7f * 0.5f,
            MovementBudgetMath.NormalDistance(LocomotionMode.Sprint, false, 0.7f, 0.5f), 4);
        Assert.Equal(8f * 4f * 0.5f,
            MovementBudgetMath.NormalDistance(LocomotionMode.Sprint, false, 99f, 0.5f), 4);
        Assert.Equal(0f,
            MovementBudgetMath.NormalDistance(LocomotionMode.Walk, false, 1f, 0f), 4);
    }

    [Fact]
    public void DashDistance_Uses_Product_When_Not_Capped()
    {
        Assert.Equal(10f * 0.7f * 0.1f,
            MovementBudgetMath.DashDistance(10f, 0.7f, 0.1f, remaining: 5f), 4);
    }

    [Fact]
    public void JumpInertiaDistance_Caps_At_Remaining()
    {
        Assert.Equal(2f, MovementBudgetMath.JumpInertiaDistance(10f, 0.5f, 2f), 4);
    }

    [Fact]
    public void NonFinite_Budget_Inputs_Return_Zero()
    {
        Assert.Equal(0f, MovementBudgetMath.DashDistance(
            float.PositiveInfinity, 0f, 0.1f, remaining: 2f), 4);
        Assert.Equal(0f, MovementBudgetMath.JumpInertiaDistance(
            float.PositiveInfinity, 0.1f, 2f), 4);
    }

    [Fact]
    public void ClampHorizontal_Freezes_Horizontal_On_NonFinite_Budget_And_Keeps_Y()
    {
        var clamped = MovementBudgetMath.ClampHorizontal(
            new Vec3(0f, 0f, 0f), new Vec3(3f, 1.7f, 4f), float.NaN);

        Assert.Equal(0f, clamped.X, 4);
        Assert.Equal(1.7f, clamped.Y, 4);
        Assert.Equal(0f, clamped.Z, 4);
    }
}
```

Unity `MovementBudgetMathTests`（同源代码的编辑器编译/行为冒烟）：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using NUnit.Framework;

namespace Cool.SyncCore.Tests
{
    public class MovementBudgetMathTests
    {
        [Test]
        public void NormalDistance_Composes_Walk_Defend_And_Status()
        {
            float distance = MovementBudgetMath.NormalDistance(
                LocomotionMode.Walk, defending: true, statusScale: 0.7f, seconds: 0.1f);

            Assert.AreEqual(5f * 0.4f * 0.7f * 0.1f, distance, 0.0001f);
        }

        [Test]
        public void ClampHorizontal_Preserves_Direction_And_Length()
        {
            var clamped = MovementBudgetMath.ClampHorizontal(
                new Vec3(0f, 0f, 0f), new Vec3(3f, 0f, 4f), allowedDistance: 2.5f);

            Assert.AreEqual(1.5f, clamped.X, 0.0001f);
            Assert.AreEqual(2f, clamped.Z, 0.0001f);
        }

        [Test]
        public void MaxJumpY_Follows_Projectile_Motion_To_Apex()
        {
            float apex = 10.95f / 30f;
            float expected = 0.3f + 10.95f * 10.95f / (2f * 30f);

            Assert.AreEqual(expected,
                MovementBudgetMath.MaxJumpY(0.3f, 10.95f, -30f, apex), 0.0001f);
        }

        [Test]
        public void JumpInertiaDistance_Caps_At_Remaining()
        {
            Assert.AreEqual(2f,
                MovementBudgetMath.JumpInertiaDistance(10f, 0.5f, 2f), 0.0001f);
        }

        [Test]
        public void ClampHorizontal_Freezes_Horizontal_On_NonFinite_Budget_And_Keeps_Y()
        {
            var clamped = MovementBudgetMath.ClampHorizontal(
                new Vec3(0f, 0f, 0f), new Vec3(3f, 1.7f, 4f), float.NaN);

            Assert.AreEqual(0f, clamped.X, 0.0001f);
            Assert.AreEqual(1.7f, clamped.Y, 0.0001f);
            Assert.AreEqual(0f, clamped.Z, 0.0001f);
        }
    }
}
```

- [x] **步骤 2：运行测试确认失败**

运行：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MovementBudgetFormulaTests"
bash tools/unity-tests.sh EditMode MovementBudgetMathTests
```
预期：编译失败（`MovementBudgetMath` 不存在）。

- [x] **步骤 3：实现 `MovementBudgetMath`**

```csharp
using System;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>PvP 位移预算纯函数（Plan 03）：只接收服务端 tick，客户端时间戳不参与。</summary>
    public static class MovementBudgetMath
    {
        /// <summary>长间隔累积上限（秒）。</summary>
        public const float MaxAccumulatedSeconds = 0.5f;

        /// <summary>15Hz 下 1 tick 的秒数（突进按 DashDistance + 1 帧封顶）。</summary>
        public const float OneTickSeconds = 1f / CombatRules.TickRateHz;

        public static float ClampDeltaSeconds(uint nowTick, uint lastTick)
        {
            if (lastTick == 0 || nowTick <= lastTick) return 0f;
            float seconds = (nowTick - lastTick) / CombatRules.TickRateHz;
            return seconds > MaxAccumulatedSeconds ? MaxAccumulatedSeconds : seconds;
        }

        public static float NormalDistance(LocomotionMode mode, bool defending,
            float statusScale, float seconds)
        {
            if (!(seconds > 0f) || !float.IsFinite(seconds)) return 0f;
            float baseSpeed = mode == LocomotionMode.Sprint ? CombatRules.SprintSpeed : CombatRules.WalkSpeed;
            float defendScale = defending ? CombatRules.DefendSpeedMultiplier : 1f;
            float scale = float.IsFinite(statusScale) ? Math.Clamp(statusScale, 0f, 4f) : 0f;
            return baseSpeed * defendScale * scale * seconds;
        }

        public static float DashSpeed(float distance, float duration, float startup, float recovery)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(duration)
                || !float.IsFinite(startup) || !float.IsFinite(recovery)) return 0f;
            float active = duration - startup - recovery;
            if (!(distance > 0f) || !(active > 0f)) return 0f;
            return distance / active;
        }

        public static float DashDistance(float dashSpeed, float statusScale,
            float seconds, float remaining)
        {
            if (!float.IsFinite(dashSpeed) || !float.IsFinite(seconds)
                || !float.IsFinite(remaining)
                || !(dashSpeed > 0f) || !(seconds > 0f) || !(remaining > 0f))
                return 0f;
            float scale = float.IsFinite(statusScale) ? Math.Clamp(statusScale, 0f, 4f) : 0f;
            float granted = dashSpeed * scale * seconds;
            return Math.Min(granted, remaining);
        }

        public static float JumpInertiaDistance(float speed, float seconds, float remaining)
        {
            if (!float.IsFinite(speed) || !float.IsFinite(seconds) || !float.IsFinite(remaining)
                || !(speed > 0f) || !(seconds > 0f) || !(remaining > 0f))
                return 0f;
            return Math.Min(speed * seconds, remaining);
        }

        public static Vec3 ClampHorizontal(Vec3 previous, Vec3 next, float allowedDistance)
        {
            // 预算非有限时 fail-closed：水平冻结在 previous，Y 仍用本次上报值（重力/落地由 Y 夹取负责）。
            if (!float.IsFinite(allowedDistance) || allowedDistance < 0f)
                return new Vec3(previous.X, next.Y, previous.Z);

            float dx = next.X - previous.X;
            float dz = next.Z - previous.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!(distance > allowedDistance) || distance <= 0f) return next;

            float scale = Math.Max(0f, allowedDistance) / distance;
            return new Vec3(previous.X + dx * scale, next.Y, previous.Z + dz * scale);
        }

        public static float MaxJumpY(float startY, float jumpForce, float gravity, float elapsedSeconds)
        {
            if (!float.IsFinite(jumpForce) || !float.IsFinite(gravity)
                || !float.IsFinite(elapsedSeconds)
                || !(gravity < 0f) || !(jumpForce > 0f)) return startY;
            float apex = jumpForce / -gravity;
            float t = Math.Clamp(elapsedSeconds, 0f, apex);
            return startY + jumpForce * t + 0.5f * gravity * t * t;
        }
    }
}
```

- [x] **步骤 4：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MovementBudgetFormulaTests"
bash tools/unity-tests.sh EditMode MovementBudgetMathTests
```
预期：PASS。

- [x] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/MovementBudgetMath.cs
git add Server/Server.Tests/SyncCore/MovementBudgetFormulaTests.cs Assets/Scripts/Tests/Editor/SyncCore/MovementBudgetMathTests.cs
git commit -m "feat(sync): add displacement budget math"
```

---

### 任务 5：服务端位置同步按接收 tick 发放预算

**文件：**
- 修改：`Server/Handler/PositionSyncHandler.cs`
- 修改：`Server/Server3C.cs`（地面高度接线）
- 测试：`Server/Server.Tests/Server/PvpMovementBudgetTests.cs`（新建）
- 测试：`Server/Server.Tests/Server/JumpTrajectoryBudgetTests.cs`（新建）

- [x] **步骤 1：写失败测试**

`PvpMovementBudgetTests`（节选；每个用例都 `player.StrictMovementBudget = true`）：

```csharp
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

internal static class MovementBudgetTestHost
{
    public static PositionSyncHandler Create(out PlayerState player, out Server3C server)
    {
        var store = new EntityStore();
        server = new Server3C(new TestLogger());
        var handler = new PositionSyncHandler(server, new TestLogger(), store);
        player = store.AddPlayer(1, "P1");
        player.StrictMovementBudget = true;
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0, 0);
        return handler;
    }
}

public class PvpMovementBudgetTests
{

    [Fact]
    public void Walk_Budget_Is_One_Report_Interval()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);

        var result = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.32f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            LocomotionMode = LocomotionMode.Walk, LifeId = player.LifeId
        }, player, nowTick: 1);

        Assert.NotNull(result);
        Assert.False(result.Value.NeedsCorrection);
    }

    [Fact]
    public void Same_Tick_Second_Packet_Gets_No_Extra_Budget()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.32f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            LocomotionMode = LocomotionMode.Walk, LifeId = player.LifeId
        }, player, nowTick: 1);

        var second = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.64f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            LocomotionMode = LocomotionMode.Walk, LifeId = player.LifeId
        }, player, nowTick: 1);

        Assert.True(second.Value.NeedsCorrection);
        Assert.Equal(0.32f, second.Value.AuthoritativePosition.X, 3);
    }

    [Fact]
    public void Sprint_Defend_And_Status_Compose()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.ApplyControl(0.7f, false);

        var result = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 0f, Z = 0.5f, Speed = 8f, IsDefending = true, Sequence = 1,
            LocomotionMode = LocomotionMode.Sprint, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 1);

        // 8 × 0.4 × 0.7 × 0.066 = 0.14784m
        Assert.True(result.Value.NeedsCorrection);
        Assert.Equal(0.14784f, result.Value.AuthoritativePosition.Z, 3);
    }

    [Fact]
    public void Old_Control_Version_Is_Rejected_Without_Consuming_Budget()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.ApplyControl(0.7f, false);

        var stale = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 3f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            ClientControlVersion = 0, LifeId = player.LifeId
        }, player, nowTick: 1);

        Assert.True(stale.Value.NeedsCorrection);
        Assert.Equal(0f, stale.Value.AuthoritativePosition.X, 3);

        var fresh = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.23f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            ClientControlVersion = player.ControlVersion, LifeId = player.LifeId
        }, player, nowTick: 2);

        Assert.False(fresh.Value.NeedsCorrection);
    }

    [Fact]
    public void Dash_Window_Only_Grants_For_Matching_CastId_And_Skill()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.Displacement = new DisplacementWindow
        {
            CastId = 42, SkillId = 20001, StartTick = 1, EndTick = 19,
            TotalDistance = 2f, Speed = 10f
        };

        var mismatch = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.5f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            DisplacementSkillId = 20001, CastId = 41, LifeId = player.LifeId
        }, player, nowTick: 2);
        Assert.True(mismatch.Value.NeedsCorrection);

        var match = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.5f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            DisplacementSkillId = 20001, CastId = 42, LifeId = player.LifeId
        }, player, nowTick: 3);
        Assert.False(match.Value.NeedsCorrection);
    }
}
```

`JumpTrajectoryBudgetTests`：

```csharp
[Fact]
public void Jump_Start_Records_Sequence_And_Air_Inertia_Under_ActionLock()
{
    var handler = MovementBudgetTestHost.Create(out var player, out _);

    handler.ValidateAndApply(new PositionSyncRequest
    {
        X = 0f, Y = 0f, Z = 0f, Speed = 6f, Sequence = 1,
        JumpPhase = PlayerJumpPhase.Start, JumpSeq = 3, LifeId = player.LifeId,
        ClientControlVersion = player.ControlVersion
    }, player, nowTick: 1);

    Assert.True(player.Movement.JumpActive);
    Assert.Equal(3u, player.Movement.LastJumpSeq);
    Assert.Equal(6f, player.Movement.JumpHorizontalSpeed, 4);

    player.ApplyControl(0f, true); // 麻痹发生在起跳之后：保留已有空中惯性

    var air = handler.ValidateAndApply(new PositionSyncRequest
    {
        X = 0.4f, Y = 0.7f, Z = 0f, Speed = 6f, Sequence = 2,
        JumpPhase = PlayerJumpPhase.Air, JumpSeq = 3, LifeId = player.LifeId,
        ClientControlVersion = player.ControlVersion
    }, player, nowTick: 2);

    Assert.False(air.Value.NeedsCorrection); // 6 × 1/15 = 0.4m
}

[Fact]
public void Jump_Start_Is_Rejected_While_ActionLocked()
{
    var handler = MovementBudgetTestHost.Create(out var player, out _);
    player.ApplyControl(0f, true);

    handler.ValidateAndApply(new PositionSyncRequest
    {
        X = 0f, Y = 0f, Z = 0f, Speed = 6f, Sequence = 1,
        JumpPhase = PlayerJumpPhase.Start, JumpSeq = 4, LifeId = player.LifeId,
        ClientControlVersion = player.ControlVersion
    }, player, nowTick: 1);

    Assert.False(player.Movement.JumpActive);
    Assert.Equal(4u, player.Movement.LastJumpSeq);
}
```

（`MovementBudgetTestHost` 与 `TestLogger` 都是本仓库现有测试类型；没有 ground provider 时
`IsGrounded` 返回 true，跳跃测试不需要导航。）

- [x] **步骤 2：运行测试确认失败**

运行（分开跑，in-process runner 不做 `|` 复合过滤）：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PvpMovementBudgetTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~JumpTrajectoryBudgetTests"
```
预期：编译失败（`ValidateAndApply(..., nowTick)` 不存在）。

- [x] **步骤 3：实现 `ValidateAndApply` 预算分支**

在 `PositionSyncHandler` 追加：

```csharp
/// <summary>服务端认可的地面高度（与 CastHandler 同一 navmesh 闭包）；null = 无导航宿主，
/// 跳跃接地/高度不额外钳制（沿用客户端自报）。</summary>
public Func<Vec3, float?>? GroundHeight { get; set; }

public void SetGroundHeight(Func<Vec3, float?>? groundHeight) => GroundHeight = groundHeight;

public MoveValidationResult? ValidateAndApply(PositionSyncRequest request, PlayerState player)
    => ValidateAndApply(request, player, _server.CurrentTick);

public MoveValidationResult? ValidateAndApply(PositionSyncRequest request, PlayerState player, uint nowTick)
{
    if (player.SupportsAuthoritativeRespawn && (!player.IsAlive || request.RespawnTick != player.RespawnTick))
        return null;

    if (player.StrictMovementBudget)
        return ValidateWithBudget(request, player, nowTick);

    // —— 既有路径保持原样（旧客户端/未协商能力位）——
    bool firstReportAfterRespawn = player.RespawnTick.HasValue && player.LastUpdateTimestamp == 0L;
    var result = MovementValidator.Validate(/* 现有参数不变 */);
    /* 现有 rotation 回退、UpdatePosition、IsDefending、日志逻辑保持不变 */
    return result;
}
```

新增私有方法：

```csharp
private MoveValidationResult ValidateWithBudget(PositionSyncRequest request, PlayerState player, uint nowTick)
{
    if (!float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z)
        || !float.IsFinite(request.Rotation) || !float.IsFinite(request.Speed))
        return CorrectionToCurrent(player);

    if (request.LifeId != 0 && request.LifeId != player.LifeId)
        return CorrectionToCurrent(player);

    if (request.ClientControlVersion < player.ControlVersion)
        return CorrectionToCurrent(player);

    float dt = player.Movement.LastBudgetTick == 0
        ? Server3C.TickIntervalMs / 1000f
        : MovementBudgetMath.ClampDeltaSeconds(nowTick, player.Movement.LastBudgetTick);
    player.Movement.LastBudgetTick = nowTick;

    float normal = player.ControlActionLock
        ? 0f
        : MovementBudgetMath.NormalDistance(request.LocomotionMode, request.IsDefending,
            player.ControlSpeedScale, dt);

    float dash = GrantDash(request, player, nowTick, dt);
    float jump = UpdateJump(request, player, nowTick, dt);

    float allowed = normal + dash + jump;
    float y = ClampY(request, player, nowTick);
    var clamped = MovementBudgetMath.ClampHorizontal(
        new Vec3(player.Position.X, player.Position.Y, player.Position.Z),
        new Vec3(request.X, y, request.Z), allowed);

    bool corrected = clamped.X != request.X || clamped.Y != request.Y || clamped.Z != request.Z;
    float speed = Math.Clamp(request.Speed, 0f, MovementValidator.MaxSpeed);

    player.UpdatePosition(new Vector3(clamped.X, clamped.Y, clamped.Z),
        request.Rotation, speed, request.Timestamp, request.Sequence);
    if (player.IsDefending != request.IsDefending)
    {
        player.IsDefending = request.IsDefending;
        player.BumpCombatVersion();
    }

    return new MoveValidationResult(corrected, clamped, request.Rotation, speed);
}

private static MoveValidationResult CorrectionToCurrent(PlayerState player)
    => new MoveValidationResult(true,
        new Vec3(player.Position.X, player.Position.Y, player.Position.Z), player.Rotation, 0f);

private float GrantDash(PositionSyncRequest request, PlayerState player, uint nowTick, float dt)
{
    var window = player.Displacement;
    if (window == null || request.CastId != window.CastId
        || request.DisplacementSkillId != window.SkillId
        || nowTick < window.StartTick || nowTick >= window.EndTick)
        return 0f;

    float remaining = window.TotalDistance
        + window.Speed * MovementBudgetMath.OneTickSeconds
        - window.GrantedDistance;
    float granted = MovementBudgetMath.DashDistance(
        window.Speed, player.ControlSpeedScale, dt, remaining);
    window.GrantedDistance += granted;
    return granted;
}

private float UpdateJump(PositionSyncRequest request, PlayerState player, uint nowTick, float dt)
{
    var m = player.Movement;
    var jump = _server.ElementalCatalog.Jump;
    float maxAir = 2f * jump.MaxRiseSeconds;

    if (request.JumpPhase == PlayerJumpPhase.Start && request.JumpSeq != 0
        && request.JumpSeq != m.LastJumpSeq)
    {
        m.LastJumpSeq = request.JumpSeq;
        // 麻痹禁止新起跳；无导航宿主沿用客户端自报（IsGrounded 默认 true）。
        if (!player.ControlActionLock && IsGrounded(request))
        {
            m.JumpActive = true;
            m.JumpStartTick = nowTick;
            m.JumpStartY = player.Position.Y;
            // 用本次上报速度但按冲刺上限封顶：客户端不能靠伪造 Speed>8 换更远惯性。
            m.JumpHorizontalSpeed = Math.Clamp(request.Speed, 0f, CombatRules.SprintSpeed);
            m.JumpGrantedDistance = 0f;
        }
        else
        {
            m.JumpActive = false;
        }
    }
    else if (request.JumpPhase == PlayerJumpPhase.End)
    {
        m.JumpActive = false;
    }

    if (!m.JumpActive) return 0f;

    // 起跳包只记录，不把起跳前的间隔算成空中惯性。
    if (nowTick == m.JumpStartTick) return 0f;

    float elapsed = (nowTick - m.JumpStartTick) / CombatRules.TickRateHz;
    if (elapsed > maxAir) { m.JumpActive = false; return 0f; }

    // 普通空中移动由 normal 预算覆盖；只有麻痹/动作锁才额外发跳跃惯性，
    // 避免同一段位移被 normal + inertia 双算。
    if (!player.ControlActionLock) return 0f;

    float remaining = m.JumpHorizontalSpeed * maxAir - m.JumpGrantedDistance;
    float granted = MovementBudgetMath.JumpInertiaDistance(
        m.JumpHorizontalSpeed, dt, remaining);
    m.JumpGrantedDistance += granted;
    return granted;
}

private bool IsGrounded(PositionSyncRequest request)
{
    var ground = GroundHeight?.Invoke(new Vec3(request.X, request.Y, request.Z));
    // 出生点 Y=0.3（胶囊中心），navmesh 地面为 0；0.5m 容差覆盖皮肤宽度/台阶。
    return !ground.HasValue || request.Y <= ground.Value + 0.5f;
}

private float ClampY(PositionSyncRequest request, PlayerState player, uint nowTick)
{
    var ground = GroundHeight?.Invoke(new Vec3(request.X, request.Y, request.Z));
    if (!ground.HasValue) return request.Y;

    float maxY = ground.Value + MovementValidator.MaxJumpHeight;
    if (player.Movement.JumpActive)
    {
        float elapsed = (nowTick - player.Movement.JumpStartTick) / CombatRules.TickRateHz;
        var jump = _server.ElementalCatalog.Jump;
        float arc = MovementBudgetMath.MaxJumpY(
            player.Movement.JumpStartY, jump.JumpForce, jump.Gravity, elapsed);
        maxY = Math.Min(maxY, arc + 0.05f);
    }
    return Math.Clamp(request.Y, ground.Value - 1f, maxY);
}
```

`HandlePositionSyncAsync` 改为：

```csharp
if (ValidateAndApply(request, player, _server.CurrentTick) is not MoveValidationResult result) return;
```

并在 `PositionSyncResponse` 初始化里加：

```csharp
ControlVersion = player.ControlVersion,
```

`Server3C.EnableMonsters` 的 `if (navigation != null)` 块里追加：

```csharp
_positionSyncHandler.SetGroundHeight(pos =>
{
    if (!navigation.TryProject(new Vector3(pos.X, pos.Y, pos.Z), 10f,
            out Vector3 projected, out _))
        return (float?)null;
    return projected.Y;
});
```

- [x] **步骤 4：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PvpMovementBudgetTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~JumpTrajectoryBudgetTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PositionSyncEntityStoreTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~RespawnTests"
```
预期：新测试 PASS；既有位置/复活测试保持 PASS（`StrictMovementBudget` 默认 false）。

- [x] **步骤 5：Commit**

```bash
git add Server/Handler/PositionSyncHandler.cs Server/Server3C.cs
git add Server/Server.Tests/Server/PvpMovementBudgetTests.cs Server/Server.Tests/Server/JumpTrajectoryBudgetTests.cs
git commit -m "feat(server): validate movement with receive-tick budgets"
```

---

### 任务 6：PvP 720 HP 与 PvE 伤害基准

**文件：**
- 修改：`Server/Handler/LoginHandler.cs`
- 修改：`Server/Handler/CastHandler.cs`
- 测试：`Server/Server.Tests/Handler/LoginCombatBaselineTests.cs`（新建）
- 测试：`Server/Server.Tests/Combat/PveDamageScaleTests.cs`（新建）

- [x] **步骤 1：写失败测试**

`LoginCombatBaselineTests`：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class LoginCombatBaselineTests
{
    [Fact]
    public void Elemental_Session_Gets_720_Hp_And_Strict_Budget()
    {
        var player = new PlayerState(1);
        LoginHandler.ApplyLoginCombatBaseline(player, ElementalCatalogDefaults.Create(),
            ProtocolFeatures.ElementalSwordQiV1 | ProtocolFeatures.PvpMovementBudgetV1);

        Assert.Equal(720f, player.MaxHp);
        Assert.Equal(720f, player.Hp);
        Assert.True(player.StrictMovementBudget);
    }

    [Fact]
    public void Legacy_Session_Keeps_100_Hp_And_Old_Validation()
    {
        var player = new PlayerState(1);
        LoginHandler.ApplyLoginCombatBaseline(player, ElementalCatalogDefaults.Create(), 0);

        Assert.Equal(100f, player.MaxHp);
        Assert.False(player.StrictMovementBudget);
    }
}
```

`PveDamageScaleTests` 复用现有 `CombatResolver` 测试的 NPC/玩家构造方式；核心断言：

```csharp
[Fact]
public void Npc_Damage_To_720_Hp_Player_Is_Scaled_By_7_2()
{
    var store = new EntityStore();
    var npc = store.AddPlayer(100, "Slime");
    npc.Kind = (byte)EntityKind.Npc;
    npc.TemplateId = 1;
    npc.BodyRadius = 0.5f;
    var target = store.AddPlayer(1, "P1");
    target.SetMaxHp(720f);
    target.IsLoggedIn = true;
    target.UpdatePosition(new Vector3(1f, 0f, 0f), 0f, 0f, 0, 0);
    var resolver = new CombatResolver(store, new System.Random(1));

    var decision = resolver.Decide(100, new CastRequest
    {
        SkillId = 50001,
        Shape = new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }
    }, tick: 0);

    Assert.True(CombatRules.TryGet(50001, out var entry));
    var resolved = resolver.DrainResolved(CombatRules.HitDelayTicks(entry)).Single();
    Assert.Equal(72f, resolved.Hits.Single().Damage, 3);
}
```

- [x] **步骤 2：运行测试确认失败**

运行（分开跑）：
```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~LoginCombatBaselineTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PveDamageScaleTests"
```
预期：编译失败（`ApplyLoginCombatBaseline` 不存在 / 伤害仍是 10）。

- [x] **步骤 3：实现**

`LoginHandler` 添加静态方法与调用：

```csharp
/// <summary>登录战斗基线：能力位决定严格预算；元素会话按共享表给 720 HP。</summary>
public static void ApplyLoginCombatBaseline(PlayerState player,
    ElementalCatalogDefinition catalog, uint supportedFeatures)
{
    player.StrictMovementBudget =
        (supportedFeatures & ProtocolFeatures.PvpMovementBudgetV1) != 0;
    if ((supportedFeatures & ProtocolFeatures.ElementalSwordQiV1) != 0)
        player.SetMaxHp(catalog.Pvp.PlayerMaxHp);
}
```

`HandleLoginAsync` 在 `storePlayer.ZoneId = zoneId;` 之后调用：

```csharp
ApplyLoginCombatBaseline(storePlayer, _server.ElementalCatalog, request.SupportedFeatures);
```

`LoginResponse.EnabledFeatures` 改为：

```csharp
loginResponse.EnabledFeatures = ProtocolFeatures.PvpMovementBudgetV1;
if (wantsElemental)
{
    loginResponse.EnabledFeatures |= ProtocolFeatures.ElementalSwordQiV1;
    loginResponse.ElementalCatalogFingerprint = _server.ElementalCatalogFingerprint;
    loginResponse.WeaponInventory = EquipHandler.BuildBaseline(storePlayer, _server.ElementalCatalog);
}
```

`CombatRules` 追加：

```csharp
/// <summary>NPC 对玩家直接伤害的 PvE 基准缩放：保持 100→720 HP 后的 TTK 不变。</summary>
public static float ScaleNpcDamageToPlayer(float damage, float targetMaxHp)
    => targetMaxHp > 0f && float.IsFinite(targetMaxHp)
        ? damage * (targetMaxHp / PveDamageBaselineHp)
        : damage;
```

`CastHandler.ApplyHit` 在 `target.ApplyDamage(damage);` 之前插入：

```csharp
// Plan 03：元素会话玩家 720 HP；NPC 直接伤害按同一基准缩放，保持原 PvE TTK。
// 玩家对玩家/怪物、bot（MaxHp=100）不缩放。
if (pending.IsNpcCaster && target.Kind == (byte)EntityKind.Player && target.IsLoggedIn)
    damage = CombatRules.ScaleNpcDamageToPlayer(damage, target.MaxHp);
```

Plan 04 的怪物 DoT 跳伤必须复用 `ScaleNpcDamageToPlayer`（同一基准），否则 720 HP 下灼烧会相对变弱。

**2026-11-19 实施注记**：Plan 04 的玩家来源灼烧每跳实际取状态表 `DamagePerTick`（PvE 乘风易伤、PvP 不放大），**未**复用 `ScaleNpcDamageToPlayer`；只有未来新增 **NPC 来源** DoT 时，每跳才必须复用该缩放。

- [x] **步骤 4：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~LoginCombatBaselineTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PveDamageScaleTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolver"
```
预期：PASS；既有的 100 HP 测试若不是 `IsLoggedIn` 或 MaxHp=100，缩放因子为 1，结果不变。

- [x] **步骤 5：Commit**

```bash
git add Server/Handler/LoginHandler.cs Server/Handler/CastHandler.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs
git add Server/Server.Tests/Handler/LoginCombatBaselineTests.cs Server/Server.Tests/Combat/PveDamageScaleTests.cs
git commit -m "feat(server): apply pvp hp baseline and pve damage scale"
```

---

### 任务 7：客户端速度合成、控制锁与共享突进窗口

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/LocalControlState.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDashComponent.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/IDashComponent.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/MovementCompositionTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/SkillDashComponentTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/LocalControlStateTests.cs`（新建）

- [x] **步骤 1：写失败测试**

`MovementCompositionTests`：

```csharp
using Hotfix.GameSystems.Sys3C.Character;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.Sys3C
{
    public class MovementCompositionTests
    {
        [Test]
        public void ComposeMoveSpeed_Multiplies_Base_Defend_Skill_And_Status()
        {
            Assert.AreEqual(5f * 0.4f * 0.5f * 0.7f,
                CharacterController.ComposeMoveSpeed(5f, true, 0.5f, 0.7f), 0.0001f);
        }

        [Test]
        public void ComposeMoveSpeed_Clamps_Invalid_Scale_To_Zero()
        {
            Assert.AreEqual(0f, CharacterController.ComposeMoveSpeed(5f, false, 1f, float.NaN), 0.0001f);
            Assert.AreEqual(0f, CharacterController.ComposeMoveSpeed(5f, false, 1f, -1f), 0.0001f);
        }
    }
}
```

`SkillDashComponentTests`：

```csharp
[Test]
public void DashSpeed_Uses_Shared_Startup_And_Recovery()
{
    var component = new SkillDashComponent(null, null);
    component.StartDash(Vector3.forward, distance: 2f, duration: 0.3f,
        startupSeconds: 0.05f, recoverySeconds: 0.05f);

    Assert.AreEqual(10f, component.DashSpeed, 0.0001f);
}

[Test]
public void DashSpeed_Applies_Status_Scale()
{
    var component = new SkillDashComponent(null, null) { StatusSpeedScaleProvider = () => 0.7f };
    component.StartDash(Vector3.forward, 2f, 0.3f, 0.05f, 0.05f);

    Assert.AreEqual(7f, component.CurrentFrameSpeed(1f), 0.0001f);
}

[Test]
public void ResolveDashDistance_Prefers_Shared_Catalog_Over_Asset_Fallback()
{
    Assert.AreEqual(2f,
        Cool.SyncCore.ElementalCatalogDefaults.ResolveDashDistance(20001, 0f), 0.0001f);
    Assert.AreEqual(3f,
        Cool.SyncCore.ElementalCatalogDefaults.ResolveDashDistance(99999, 3f), 0.0001f);
}
```

`LocalControlStateTests`：

```csharp
[Test]
public void Apply_Gates_By_Version_And_Reports_Lock_Edge()
{
    var state = new LocalControlState();

    Assert.IsTrue(state.Apply(0.7f, true, 2), "首次从无锁到锁必须报告边沿");
    Assert.AreEqual(2u, state.Version);
    Assert.AreEqual(0.7f, state.SpeedScale, 0.0001f);
    Assert.IsTrue(state.ActionLock);

    Assert.IsFalse(state.Apply(1f, false, 1), "旧版本必须忽略");
    Assert.AreEqual(2u, state.Version);
    Assert.IsTrue(state.ActionLock);

    Assert.IsFalse(state.Apply(1f, false, 3), "解除锁不是“变为锁”的边沿");
    Assert.AreEqual(3u, state.Version);
    Assert.IsFalse(state.ActionLock);
}

[Test]
public void Apply_Clamps_Invalid_Scale()
{
    var state = new LocalControlState();
    state.Apply(float.NaN, false, 1);
    Assert.AreEqual(0f, state.SpeedScale, 0.0001f);
}
```

- [x] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode MovementCompositionTests` 与 `bash tools/unity-tests.sh EditMode SkillDashComponentTests`
预期：编译失败（`ComposeMoveSpeed`/新签名不存在）。

- [x] **步骤 3：实现客户端移动**

`CharacterController`（同时把既有的 `MoveSpeed = 5.0f` / `SprintSpeed = 8.0f` 改为引用
`CombatRules.WalkSpeed` / `CombatRules.SprintSpeed`，消除服务端预算与客户端实际速度的双源）：

```csharp
public float MoveSpeed { get; set; } = Cool.SyncCore.CombatRules.WalkSpeed;
public float SprintSpeed { get; set; } = Cool.SyncCore.CombatRules.SprintSpeed;
public const float DefendSpeedMultiplier = Cool.SyncCore.CombatRules.DefendSpeedMultiplier;

/// <summary>技能侧速度倍率（旋转等）；与状态倍率分开，禁止互相覆写。</summary>
public float SkillSpeedScale { get; set; } = 1f;

/// <summary>权威状态侧速度倍率（冰减速等）；由 ApplyAuthoritativeControl 写。</summary>
public float StatusSpeedScale { get; set; } = 1f;

/// <summary>权威动作锁（麻痹）：禁止主动水平输入与新起跳，保留空中惯性。</summary>
public bool StatusActionLock { get; private set; }

/// <summary>起跳序号（每次消费跳跃请求 +1；MovementReporter 用）。</summary>
public uint JumpSeq { get; private set; }

public bool IsAirborne => _data.BaseState == BaseState.JumpStart
    || _data.BaseState == BaseState.JumpAir || !_data.IsGrounded;

public static float ComposeMoveSpeed(float baseSpeed, bool defending,
    float skillScale, float statusScale)
{
    float scale = skillScale * statusScale;
    if (!float.IsFinite(scale) || scale < 0f) scale = 0f;
    return baseSpeed * (defending ? DefendSpeedMultiplier : 1f) * scale;
}

public void SetStatusActionLock(bool locked)
{
    if (locked == StatusActionLock) return;
    StatusActionLock = locked;
    if (!locked && _data.IsGrounded) _jumpStartHorizontalVelocity = Vector3.zero;
}

private Vector3 _jumpStartHorizontalVelocity; // 起跳瞬间的水平速度；麻痹空中沿用
```

`ApplyHorizontalMovement` 改为：

```csharp
private void ApplyHorizontalMovement(MoveCommand command)
{
    if (LockMovement) return;
    if (StatusActionLock)
    {
        // 麻痹：不再接受新水平输入；空中保留起跳时的水平惯性到落地（不乘状态倍率）。
        if (IsAirborne && _jumpStartHorizontalVelocity.sqrMagnitude > 0.0001f)
            LastMoveFlags = _controller.Move(_jumpStartHorizontalVelocity * Time.deltaTime);
        return;
    }

    float currentSpeed = command.IsSprint ? SprintSpeed : MoveSpeed;
    currentSpeed = ComposeMoveSpeed(currentSpeed, _isDefending,
        SkillSpeedScale, StatusSpeedScale);

    Vector3 moveVelocity = command.MoveDir * currentSpeed;
    moveVelocity.y = _velocity.y;
    LastMoveFlags = _controller.Move(moveVelocity * Time.deltaTime);
}
```

`RequestJump` 的门控加 `&& !StatusActionLock`；`ProcessJumpRequest` 消费时：

```csharp
JumpSeq++;
_jumpStartHorizontalVelocity = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
```

`ResetAirborneState` 把 `JumpSeq = 0`、`_jumpStartHorizontalVelocity = Vector3.zero`；落地（`JumpEnd`/`IsGrounded`）时也清零 `_jumpStartHorizontalVelocity`，避免下一次地面麻痹误用旧惯性。

`SkillDashComponent`：

```csharp
public Func<float> StatusSpeedScaleProvider { get; set; } = () => 1f;
public float DashSpeed => _dashSpeed;
public float CurrentFrameSpeed(float deltaSeconds)
    => _isDashing ? _dashSpeed * StatusSpeedScaleProvider() * deltaSeconds : 0f;

private float _startupSeconds = 0.05f;
private float _recoverySeconds = 0.05f;

public void StartDash(Vector3 direction, float distance, float duration,
    float startupSeconds, float recoverySeconds)
{
    _startupSeconds = Mathf.Max(0f, startupSeconds);
    _recoverySeconds = Mathf.Max(0f, recoverySeconds);
    float effectiveDuration = duration - _startupSeconds - _recoverySeconds;
    /* 其余参数校验保持原样 */
    _dashSpeed = distance / effectiveDuration;
}
```

`Update` 中把 `STARTUP_TIME`/`RECOVERY_TIME` 换成 `_startupSeconds`/`_recoverySeconds`，`frameMove` 改为 `CurrentFrameSpeed(Time.deltaTime)`。

`IDashComponent` 签名同步改；`SkillExecutor`：

```csharp
var displacement = Cool.SyncCore.ElementalCatalogDefaults.FindDisplacement(_skillData.SkillId);
float distance = Cool.SyncCore.ElementalCatalogDefaults.ResolveDashDistance(
    _skillData.SkillId, _skillData.DashDistance);
float duration = displacement?.DashDuration ?? _skillData.DashDuration;
float startup = displacement?.StartupSeconds ?? 0.05f;
float recovery = displacement?.RecoverySeconds ?? 0.05f;

// 起突进门也用解析后的 distance，而不是旧资产字段：
// 共享表是权威来源；资产只在共享表缺失时作旧兼容回退。
if (newState == SkillSubState.Execution && _dashComponent != null && distance > 0f)
{
    Vector3 dashDir = _owner.transform.forward;
    _dashComponent.StartDash(dashDir, distance, duration, startup, recovery);
}
```

`ElementalCatalogDefaults` 追加（Task 7 的共享表权威门）：

```csharp
/// <summary>突进距离解析：共享表优先；只有共享表没有该技能时才回退旧资产字段。</summary>
public static float ResolveDashDistance(int skillId, float assetFallback)
    => FindDisplacement(skillId)?.DashDistance ?? assetFallback;
```

`LocalControlState.cs`：

```csharp
using System;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>本人控制投影的纯状态合并：版本门 + 倍率钳制 + “变为锁”边沿。</summary>
    public sealed class LocalControlState
    {
        public uint Version { get; private set; }
        public float SpeedScale { get; private set; } = 1f;
        public bool ActionLock { get; private set; }

        /// <summary>应用服务端 ControlSummary；旧版本忽略。返回本次是否从“未锁”跨到“锁”。</summary>
        public bool Apply(float speedScale, bool actionLock, uint version)
        {
            if (version < Version) return false;
            bool becameLocked = actionLock && !ActionLock;
            Version = version;
            SpeedScale = float.IsFinite(speedScale) ? Math.Clamp(speedScale, 0f, 4f) : 0f;
            ActionLock = actionLock;
            return becameLocked;
        }
    }
}
```

`Sys3CEntry`：

```csharp
_cc.SkillSpeedScale = _skillCoordinator.GetMoveSpeedMultiplier();
_dashComponent.StatusSpeedScaleProvider = () => _cc.StatusSpeedScale;
_cc.JumpForce = Cool.SyncCore.ElementalCatalogDefaults.Jump.JumpForce;
_cc.Gravity = Cool.SyncCore.ElementalCatalogDefaults.Jump.Gravity;

private readonly LocalControlState _control = new LocalControlState();
public uint ControlVersion => _control.Version;
public bool IsDashing => _dashComponent != null && _dashComponent.IsDashing;
public int CurrentSkillId => _skillCoordinator?.CurrentSkill?.SkillId ?? 0;

public void ApplyAuthoritativeControl(float speedScale, bool actionLock, uint version)
{
    bool becameLocked = _control.Apply(speedScale, actionLock, version);
    _cc.StatusSpeedScale = _control.SpeedScale;
    _cc.SetStatusActionLock(_control.ActionLock);
    if (becameLocked)
    {
        _skillCoordinator.ClearInputBuffer();
        _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
        _dashComponent.StopDash();
    }
}

public void SetMaxHp(float maxHp, bool fill)
{
    if (!(maxHp > 0f)) return;
    _maxHP = maxHp;
    if (fill || _currentHP > maxHp) _currentHP = maxHp;
}
```

- [x] **步骤 4：运行测试**

```bash
bash tools/unity-tests.sh EditMode MovementCompositionTests
bash tools/unity-tests.sh EditMode SkillDashComponentTests
bash tools/unity-tests.sh EditMode LocalControlStateTests
bash tools/unity-tests.sh EditMode SkillCoordinatorSpinTests
```
预期：PASS。

- [x] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/LocalControlState.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillDashComponent.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/IDashComponent.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git add Assets/Scripts/Tests/Editor/Sys3C/MovementCompositionTests.cs \
  Assets/Scripts/Tests/Editor/Sys3C/SkillDashComponentTests.cs \
  Assets/Scripts/Tests/Editor/Sys3C/LocalControlStateTests.cs
git commit -m "feat(client): compose movement speed and share dash windows"
```

---

### 任务 8：客户端位置上报新字段与已确认 CastId

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/MovementReporterFieldsTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeCastConfirmTests.cs`（新建）

- [x] **步骤 1：写失败测试**

`MovementReporterFieldsTests` 文件内定义 `RecordingClient`（与现有 SyncLayer 测试同形）：

```csharp
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
```

```csharp
[Test]
public void Report_Carries_PvpMovement_Fields_And_Start_Edge_Once()
{
    var client = new RecordingClient();
    var state = new MovementReportState
    {
        Mode = LocomotionMode.Sprint,
        JumpSeq = 3,
        Airborne = true,
        Grounded = false,
        VerticalVelocity = 5f,
        DisplacementSkillId = 20001,
        CastId = 42,
        LifeId = 7,
        ControlVersion = 2
    };
    var reporter = new MovementReporter(client, () => new Vec3(1f, 0f, 2f),
        () => 90f, () => 8f, stateProvider: () => state);

    reporter.Report();
    var first = (PositionSyncRequest)client.Sent[0].msg;
    Assert.AreEqual(PlayerJumpPhase.Start, first.JumpPhase);
    Assert.AreEqual(3u, first.JumpSeq);
    Assert.AreEqual(LocomotionMode.Sprint, first.LocomotionMode);
    Assert.AreEqual(20001, first.DisplacementSkillId);
    Assert.AreEqual(42ul, first.CastId);
    Assert.AreEqual(7u, first.LifeId);
    Assert.AreEqual(2u, first.ClientControlVersion);

    reporter.Report();
    var second = (PositionSyncRequest)client.Sent[1].msg;
    Assert.AreEqual(PlayerJumpPhase.Air, second.JumpPhase, "同一 JumpSeq 的第二次上报必须是 Air");
}
```

`CombatNetworkBridgeCastConfirmTests`：

```csharp
[Test]
public void Accepted_CastConfirm_Is_Queryable_By_Skill_And_Life()
{
    var root = new GameObject("BridgeCastConfirmTestRoot");
    var bridge = new CombatNetworkBridge(
        new RecordingClient(), () => 1L, new RemoteEntityRegistry(root.transform));
    bridge.OnMessage(MessageId.CastConfirm, new CastConfirm
    {
        Accepted = true, SkillId = 20001, ClientCastSeq = 5,
        CastId = 42, CasterLifeId = 7
    });
    bridge.Update();

    Assert.IsTrue(bridge.TryGetAcceptedCastId(20001, 7, out var castId));
    Assert.AreEqual(42ul, castId);
    Assert.IsFalse(bridge.TryGetAcceptedCastId(20001, 8, out _), "生命代次不匹配不得复用");
    UnityEngine.Object.DestroyImmediate(root);
}
```

- [x] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode MovementReporterFieldsTests` 与 `bash tools/unity-tests.sh EditMode CombatNetworkBridgeCastConfirmTests`
预期：编译失败（`MovementReportState`/`TryGetAcceptedCastId` 不存在）。

- [x] **步骤 3：实现上报与确认映射**

在 `MovementReporter.cs` 的命名空间内、`MovementReporter` 类外定义上报状态；类内追加 provider 与跳跃边沿状态机：

```csharp
public struct MovementReportState
{
    public LocomotionMode Mode;
    public uint JumpSeq;
    public bool Airborne;
    public bool Grounded;
    public float VerticalVelocity;
    public int DisplacementSkillId;
    public ulong CastId;
    public uint LifeId;
    public uint ControlVersion;
}

private readonly Func<MovementReportState>? _stateProvider;
private uint _lastReportedJumpSeq;
private bool _wasAirborne;

/// <summary>最近一次位置确认携带的服务端控制版本（诊断/请求新基线的依据）。</summary>
public uint LastServerControlVersion { get; private set; }

public MovementReporter(..., Func<bool>? defendStateProvider = null,
    Func<MovementReportState>? stateProvider = null)
{
    ...
    _stateProvider = stateProvider;
}

private PlayerJumpPhase ResolveJumpPhase(MovementReportState state)
{
    if (state.JumpSeq != 0 && state.JumpSeq != _lastReportedJumpSeq)
    {
        _lastReportedJumpSeq = state.JumpSeq;
        if (!state.Airborne && state.Grounded)
            return PlayerJumpPhase.None; // 起跳与落地落在同一上报间隔：不制造幽灵起跳
        _wasAirborne = true;
        return PlayerJumpPhase.Start;
    }
    if (_wasAirborne && state.Grounded && !state.Airborne)
    {
        _wasAirborne = false;
        return PlayerJumpPhase.End;
    }
    return _wasAirborne || state.Airborne ? PlayerJumpPhase.Air : PlayerJumpPhase.None;
}
```

`Report()` 中取 `var state = _stateProvider?.Invoke() ?? default;`，并填充请求字段（`JumpPhase = ResolveJumpPhase(state)` 等）。`OnResponse` 里补 `LastServerControlVersion = response.ControlVersion;`。

`CombatNetworkBridge` 追加：

```csharp
private readonly Dictionary<int, (uint seq, ulong castId, uint lifeId)> _acceptedCasts = new();

public bool TryGetAcceptedCastId(int skillId, uint lifeId, out ulong castId)
{
    if (skillId > 0 && _acceptedCasts.TryGetValue(skillId, out var entry)
        && entry.lifeId == lifeId)
    {
        castId = entry.castId;
        return true;
    }
    castId = 0;
    return false;
}

private void RememberAcceptedCast(CastConfirm confirm)
{
    if (!confirm.Accepted || confirm.ClientCastSeq == 0) return;
    if (_acceptedCasts.TryGetValue(confirm.SkillId, out var last)
        && confirm.ClientCastSeq < last.seq) return;
    _acceptedCasts[confirm.SkillId] = (confirm.ClientCastSeq, confirm.CastId, confirm.CasterLifeId);
}
```

在 `Update` 的 `case CastConfirm` 里先 `RememberAcceptedCast(castConfirm);` 再 `OnCastConfirm?.Invoke(...)`。

`ClientNetworkManager` 登录请求的 `SupportedFeatures` 追加 `| ProtocolFeatures.PvpMovementBudgetV1`。

`ClientNetworkHost` 的 `_manager.AttachMovementReporter` 调用加 state provider：

```csharp
stateProvider: () =>
{
    var d = controller.Data;
    ulong castId = 0;
    if (_localEntry.IsDashing && _combatBridge != null)
    {
        _combatBridge.TryGetAcceptedCastId(_localEntry.CurrentSkillId,
            _combatBridge.Equipment.LifeId, out castId);
    }
    return new MovementReportState
    {
        Mode = d.IsSprint ? LocomotionMode.Sprint : LocomotionMode.Walk,
        JumpSeq = controller.JumpSeq,
        Airborne = controller.IsAirborne,
        Grounded = controller.IsGrounded,
        VerticalVelocity = controller.Data.VerticalVelocity,
        DisplacementSkillId = _localEntry.IsDashing ? _localEntry.CurrentSkillId : 0,
        CastId = castId,
        LifeId = _combatBridge?.Equipment.LifeId ?? 0u,
        ControlVersion = _localEntry.ControlVersion
    };
}
```

`ClientNetworkManager.AttachMovementReporter` 签名追加 `Func<MovementReportState>? stateProvider = null` 并透传。

- [x] **步骤 4：运行测试**

```bash
bash tools/unity-tests.sh EditMode MovementReporterFieldsTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeCastConfirmTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
```
预期：PASS。

- [x] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs
git add Assets/Scripts/Tests/Editor/SyncLayer/MovementReporterFieldsTests.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeCastConfirmTests.cs
git commit -m "feat(client): report pvp movement state and accepted cast ids"
```

---

### 任务 9：客户端消费本人控制投影

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/ClientLocalControlRoutingTests.cs`（新建）

- [x] **步骤 1：写失败测试**

`ClientLocalControlRoutingTests` 测静态分流：

```csharp
[Test]
public void Local_Projection_Is_Not_Forwarded_To_Remote_Registry()
{
    Assert.IsTrue(ClientNetworkHost.IsLocalProjection(7, 7));
    Assert.IsFalse(ClientNetworkHost.IsLocalProjection(7, 8));
}
```

- [x] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode ClientLocalControlRoutingTests`
预期：编译失败（`IsLocalProjection` 不存在）。

- [x] **步骤 3：实现本人投影路由**

`ClientNetworkHost`：

```csharp
internal static bool IsLocalProjection(long localPlayerId, long entityId)
    => localPlayerId != 0 && localPlayerId == entityId;

private void HandleCombatStateUpdate(CombatStateUpdate update)
{
    if (update == null) return;
    if (IsLocalProjection(_manager?.LocalPlayerId ?? 0, update.EntityId))
    {
        var projection = update.Projection;
        if (projection == null) return;
        if (projection.Control != null)
            _localEntry?.ApplyAuthoritativeControl(
                projection.Control.SpeedScale,
                projection.Control.ActionLock,
                projection.Control.ControlVersion);
        if (projection.MaxHp > 0f)
            _localEntry?.SetMaxHp(projection.MaxHp, fill: false);
        return;
    }
    _remoteEntities?.ApplyCombatStateUpdate(update);
}
```

在 `TryAttachLocalCharacter` 里、`NetworkSwordQiGate.SuppressLocalSpawn` 之后加：

```csharp
if (NetworkSwordQiGate.SuppressLocalSpawn)
{
    // 元素会话服务端按共享表给 720 HP；这里先对齐 HUD/本地上限，
    // 权威余血仍由 DamageResult/RespawnNotice 驱动。
    _localEntry.SetMaxHp(ElementalCatalogDefaults.Create().Pvp.PlayerMaxHp, fill: true);
}
```

- [x] **步骤 4：运行测试**

```bash
bash tools/unity-tests.sh EditMode ClientLocalControlRoutingTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
```
预期：PASS。

- [x] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs
git add Assets/Scripts/Tests/Editor/SyncLayer/ClientLocalControlRoutingTests.cs
git commit -m "feat(client): consume local authoritative control projection"
```

---

### 任务 10：文档回写与全量回归

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/README.md`
- 修改：`Server/README.md`
- 修改：`Server/Monster/README.md`
- 修改：`docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md`
- 修改：`docs/superpowers/specs/2026-11-14-elemental-sword-qi-design.md`

- [x] **步骤 1：回写模块 README**

每个 README 必须写清：已打通什么、未启用什么、怎么验证。最低内容：

- Protocol：任务 2 字段表 + `PvpMovementBudgetV1` 能力位；旧客户端缺省语义。
- SyncCore：`MovementBudgetMath` 公式、`Displacements`/`Jump` 表项、`PveDamageBaselineHp`。
- Server：接收 tick 预算、`DisplacementWindow`、720 HP/PvE 缩放、`ApplyPlayerControl` 是 Plan 04 入口；**明确状态运行时未实现**。
- Monster：NPC→玩家直接伤害按 `target.MaxHp / PveDamageBaselineHp` 缩放（720 HP 会话 ×7.2，保持 PvE TTK）；Plan 04 的 DoT 必须复用同一函数。
- SyncLayer：`MovementReportState`、跳跃 Start 边沿只发一次、本人 311 消费 Control/MaxHp、`CastConfirm` 映射。
- Sys3C：`ComposeMoveSpeed = Base × Defend × Skill × Status`、`StatusActionLock` 保留空中起跳水平惯性、`SetStatusActionLock` 捕获速度。
- Skills：突进起手/收尾窗口来自共享表、`SkillDashComponent` 乘状态倍率。
- Roadmap/设计规格：Plan 03 标 ✅（只在全量测试通过后），Plan 04/05 仍未开始。

- [x] **步骤 2：全量回归**

```bash
bash tools/test-server-inprocess.sh
bash tools/unity-tests.sh EditMode MovementBudgetMathTests
bash tools/unity-tests.sh EditMode MovementCompositionTests
bash tools/unity-tests.sh EditMode SkillDashComponentTests
bash tools/unity-tests.sh EditMode LocalControlStateTests
bash tools/unity-tests.sh EditMode MovementReporterFieldsTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/unity-tests.sh EditMode ClientLocalControlRoutingTests
bash tools/unity-tests.sh EditMode RemoteEntityRegistryTests
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
```

预期：服务端全绿；Unity 各目标类全绿。`RemoteEntityRegistryTests` 若仍只有已知
`Bot_Radius` PPtr 环境失败，按既有口径登记，不算回归；其他任何失败都必须修。

- [x] **步骤 3：写验收记录**

在 `Server/README.md` 的 Plan 03 段落下补三行可证伪证据：

```markdown
- 预算：`PvpMovementBudgetTests` 覆盖同 tick 第二包零额度、冲刺×防御×状态合成、
  旧控制版本拒绝、突进 CastId 匹配。
- 跳跃：`JumpTrajectoryBudgetTests` 覆盖麻痹起跳拒绝、空中惯性按起跳水平速度发放。
- PvP：`LoginCombatBaselineTests` 断言 720 HP；`PveDamageScaleTests` 断言 NPC→720 HP
  玩家伤害 ×7.2，保持原 PvE TTK。
```

- [x] **步骤 4：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/README.md \
  Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md \
  Assets/Scripts/Hotfix/GameSystems/Skills/README.md \
  Server/README.md Server/Monster/README.md \
  docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md \
  docs/superpowers/specs/2026-11-14-elemental-sword-qi-design.md
git commit -m "docs: record pvp movement budget and control status"
```

---

## 自检

**1. 规格覆盖度**
- 位移预算公式：任务 4/5；Dash 活动窗：任务 3/5/7；跳跃惯性：任务 5/7；PvP HP：任务 6；控制投影/本地消费：任务 3/9。
- `LocomotionMode/JumpPhase/JumpSeq/VerticalVelocity/DisplacementSkillId/CastId/ClientControlVersion/LifeId`：任务 2。
- 共享表 `DashDistance/DashDuration/起手/收尾` 与 `JumpForce/Gravity/上升上限`：任务 1。
- 旧客户端兼容：任务 2/5/6。
- 未启用项（元素状态结算、DoT、元素 VFX）明确留给 Plan 04；`ApplyPlayerControl` 是唯一注入入口。

**2. 占位符扫描**
- 无“TODO/待定/类似任务 N”；失败测试、实现代码、运行命令与提交命令均已写出。
- `MovementBudgetTestHost` 是本计划新建的测试辅助类型；`RecordingClient`/`TestLogger` 与仓库现有测试同形。实现者按本计划中的断言落地，不改行为。

**3. 类型一致性**
- `LocomotionMode`/`PlayerJumpPhase` 在 `Cool.Protocol`；`MovementBudgetMath` 在 `Cool.SyncCore`；`MovementBudgetState`/`DisplacementWindow` 在 `KcpServer`。
- `MovementReporter`、`CombatNetworkBridge`、`ClientNetworkHost`、`Sys3CEntry` 的签名在上文各任务中保持一致。
- `DisplacementDefinition` 与 `DisplacementWindow` 是不同层的类型（共享表 vs 服务端运行态），不要混用。
