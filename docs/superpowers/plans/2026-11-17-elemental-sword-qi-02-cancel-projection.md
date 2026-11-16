# 属性剑气 02：B1 取消 + NpcAction 全链 + B2 战斗投影/带宽 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 交付属性剑气 Plan 02：施法取消广播、NpcAction 全链、战斗投影与 AOI 基线/带宽门禁，使玩家与怪物在打断、晚进视野和状态变更时仍看到同一份权威结果。

**架构：** Part A 先补 `CastCancelBroadcast`、`NpcActionEvent.LifeId` 与按 `CastId` 的取消/动作存储；Part B 再补 `CombatProjection` 信封、`CombatStateUpdate`、AOI 发送原因与条件字段，最后接客户端版本合并和在途剑气补发。Part A、Part B 各自可编译、可测试、独立提交。

**技术栈：** Unity 2022.3.25f1、C#、protobuf-net、xUnit、NUnit、KCP/WebSocket。

---

## 范围与非目标

**本计划交付：**
- `CastCancelBroadcast` 与 `CancelCastRequest.CastId`；服务端按 `CastId`/最新实例取消未提交施法，并在死亡、离场、客户端取消时广播。
- `NpcActionEvent` 的 `LifeId`、服务端阶段广播、AOI 进入基线 `NpcAction`、客户端按 `(EntityId, LifeId, InstanceId, Revision)` 合并的动作存储与取消路径。
- `CombatStateUpdate` + `CombatProjection` 信封：`CombatVersion`、`LifeId`、HP/死亡/防御、`EquipmentSummary`、`ControlSummary`、`ActiveCast`、`Statuses[]`。
- `EntityFieldMask.Combat`、`EntitySnapshot.CombatVersion/LifeId/Combat`；AOI `Baseline/Keepalive/Changed` 发送原因与条件字段。
- AOI 进入基线完整投影；晚进观察者在途剑气补发；100 KB/s 带宽门禁测试。

**本计划不做（由后续计划实现）：**
- 元素状态结算、DoT、元素 VFX（Plan 04）。本计划只携带空的 `Statuses[]` 并验证清除语义。
- PvP 控制规则与移动预算（Plan 03）。`ControlSummary` 首版固定 `SpeedScale=1 / ActionLock=false / ControlVersion=0`。
- 三客户端完整一致性与带宽实测收口（Plan 05）。Plan 02 只交付服务端/客户端的单点和版本合并测试，不新增 PlayMode 三端夹具。
- 远端手持武器模型切换（元素 VFX 的一部分，Plan 04/05）。本计划只保存 `EquipmentSummary`，不替换远端模型。
- 血量/状态分版本合并。Plan 02 的 `Statuses[]` 为空，客户端按单一 `CombatVersion` 原子合并；等到 Plan 04 引入状态运行时时再拆分为 `AppliedHealthVersion`/`AppliedStatusVersion`，否则当前拆分没有可观察行为。

## 文件结构

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` | `CastCancelBroadcast`、`CancelCastRequest.CastId` |
| `Assets/Scripts/AOT/Protocol/Messages/NpcAction.cs` | `NpcActionEvent.LifeId` |
| `Assets/Scripts/AOT/Protocol/Messages/CombatProjection.cs`（新建） | `CombatStateUpdate`、`CombatProjection`、`StatusProjection`、`EquipmentSummary`、`ControlSummary`、`ActiveCastProjection` |
| `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs` | `EntityFieldMask.Combat`、`EntitySnapshot.Combat/CombatVersion/LifeId` |
| `Assets/Scripts/AOT/Protocol/MessageId.cs` / `MessageCodec.cs` | 310/311 消息 ID 与注册 |
| `Server/Player/PlayerState.cs` | `CombatVersion`、`CombatDirty`、`ActiveCast`（Part B） |
| `Server/Handler/CastHandler.cs` | `CombatResolver` 取消 API、活动施法生命周期、取消广播 |
| `Server/Aoi/AoiBandScheduler.cs` | `IsFirstSend`、`AoiSendReason`/`AoiChangeFlags`、基线/变更/保活判定 |
| `Server/Aoi/AoiBroadcaster.cs` | 条件字段、基线 `NpcAction`/`CombatProjection`、`NpcActionProvider` |
| `Server/Aoi/CombatProjectionBuilder.cs`（新建） | `PlayerState` → `CombatProjection` |
| `Server/Server3C.cs` | 死亡/离场取消、NpcActionEvent、CombatStateUpdate、AOI 投影接线、在途剑气补发 |
| `Server/Monster/MonsterModule.cs` | `GetActionSnapshots()` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | 路由 `CastCancelBroadcast`/`NpcActionEvent`/`CombatStateUpdate` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 订阅与转发、ActiveCast 表现 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | NpcAction 存储/合并、CombatProjection 版本合并、Dead 位守卫 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs` | `PlayAttack(..., castId)`、`CancelAttack(castId)` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs` | `ApplyAction`、`CancelAttack(instanceId)` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs` | 取消广播、`_cancelledCasts`、`OnActiveCast` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs` | `ServerTick` 读数（ActiveCast 剩余时长） |
| 测试 | 见各任务文件清单 |
| 文档 | `Server/README.md`、`Assets/Scripts/AOT/Protocol/README.md`、`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`、`Assets/Scripts/Hotfix/GameSystems/Monster/README.md`、路线图 |

## 测试命令

服务端每个测试类单独跑（自定义 runner 不支持 `|` 交替）：

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatCancelMessagesTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastCancelBroadcastTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~NpcActionChainTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionMessagesTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionBuilderTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastResendTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BandwidthBudgetTests"
```

Unity 每个类单独跑：

```bash
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
bash tools/unity-tests.sh EditMode NpcActionConsumptionTests
bash tools/unity-tests.sh EditMode RemoteCastPresenterCastIdTests
bash tools/unity-tests.sh EditMode RemoteEntityAnimatorArbiterTests
bash tools/unity-tests.sh EditMode NpcEntityAnimatorTests
bash tools/unity-tests.sh EditMode RemoteEntityRegistryTests
bash tools/unity-tests.sh EditMode CombatStateProjectionTests
```

---

# Part A：B1 取消 + NpcAction 全链

## 任务 1：取消与 NpcAction 协议信封

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Combat.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/NpcAction.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageId.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageCodec.cs`
- 测试：`Server/Server.Tests/Protocol/CombatCancelMessagesTests.cs`（新建）

- [ ] **步骤 1：编写失败的协议测试**

创建 `Server/Server.Tests/Protocol/CombatCancelMessagesTests.cs`：

```csharp
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class CombatCancelMessagesTests
{
    [Fact]
    public void CastCancel_Roundtrips()
    {
        var decoded = (CastCancelBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CastCancelBroadcast,
            new CastCancelBroadcast
            {
                CastId = 42,
                CasterLifeId = 3,
                CasterPlayerId = 7,
                EffectiveTick = 77,
                Reason = NpcActionCancelReason.Hit
            })).message;

        Assert.Equal(42UL, decoded.CastId);
        Assert.Equal(3u, decoded.CasterLifeId);
        Assert.Equal(7L, decoded.CasterPlayerId);
        Assert.Equal(77u, decoded.EffectiveTick);
        Assert.Equal(NpcActionCancelReason.Hit, decoded.Reason);
    }

    [Fact]
    public void CancelCastRequest_Carries_Optional_CastId()
    {
        var decoded = (CancelCastRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CancelCastRequest,
            new CancelCastRequest { SkillId = 20002, CastId = 99 })).message;

        Assert.Equal(20002, decoded.SkillId);
        Assert.Equal(99UL, decoded.CastId);
    }

    [Fact]
    public void NpcActionEvent_Carries_LifeId()
    {
        var decoded = (NpcActionEvent)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.NpcActionEvent,
            new NpcActionEvent
            {
                EntityId = 7,
                LifeId = 5,
                Action = new NpcActionSnapshot { InstanceId = 3, Revision = 2 },
                ServerTimestamp = 123
            })).message;

        Assert.Equal(7L, decoded.EntityId);
        Assert.Equal(5u, decoded.LifeId);
        Assert.Equal(3UL, decoded.Action.InstanceId);
        Assert.Equal(2u, decoded.Action.Revision);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatCancelMessagesTests"`

预期：编译失败，`CastCancelBroadcast`、`CasterPlayerId`、`CastId`、`LifeId` 不存在。

- [ ] **步骤 3：添加协议字段与消息**

在 `Combat.cs` 的 `CancelCastRequest` 增加：

```csharp
    /// <summary>可选：只取消该 CastId；0 = 取消该技能最新实例（旧客户端）。</summary>
    [ProtoMember(2)] public ulong CastId { get; set; }
```

在 `Combat.cs` 的 `ProjectileStopBroadcast` 之后增加：

```csharp
    /// <summary>取消尚未提交的前摇/持续施法。已发射的独立投射物继续飞行。</summary>
    [ProtoContract]
    public class CastCancelBroadcast : IMessage
    {
        [ProtoMember(1)] public ulong CastId { get; set; }
        [ProtoMember(2)] public uint CasterLifeId { get; set; }
        [ProtoMember(3)] public uint EffectiveTick { get; set; }
        [ProtoMember(4)] public NpcActionCancelReason Reason { get; set; }
        /// <summary>施法者 id（服务端收件人路由与客户端动作存储使用；只加不删 tag 5）</summary>
        [ProtoMember(5)] public long CasterPlayerId { get; set; }
    }
```

在 `NpcAction.cs` 的 `NpcActionEvent` 增加：

```csharp
        /// <summary>怪物生命代次（与 EntityId/InstanceId/Revision 一起作为客户端合并键）</summary>
        [ProtoMember(4)] public uint LifeId { get; set; }
```

在 `MessageId.cs` 的战斗段追加：

```csharp
        /// <summary>取消未提交施法（服务器 → 施法者与观察者；Plan 02）</summary>
        CastCancelBroadcast = 310,
```

`CombatStateUpdate = 311` 留到任务 6 与 `CombatProjection` 类型一起添加，避免 Part A 出现未注册的消息 ID。

在 `MessageCodec.cs` 的注册表先追加（`CombatStateUpdate` 在任务 6 注册）：

```csharp
            RegisterMessageType(MessageId.CastCancelBroadcast, typeof(CastCancelBroadcast));
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatCancelMessagesTests"`

预期：3/3 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/Messages/Combat.cs Assets/Scripts/AOT/Protocol/Messages/NpcAction.cs Assets/Scripts/AOT/Protocol/MessageId.cs Assets/Scripts/AOT/Protocol/MessageCodec.cs Server/Server.Tests/Protocol/CombatCancelMessagesTests.cs
git commit -m "feat(protocol): add cast cancel and npc action life id"
```

---

## 任务 2：服务端按 CastId 取消与广播

**文件：**
- 修改：`Server/Handler/CastHandler.cs`（`CombatResolver` 与 `CastHandler` 同文件）
- 修改：`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Handler/CastCancelBroadcastTests.cs`（新建）
- 修改：`Server/Server.Tests/Combat/CombatResolverSpinTests.cs`（`CancelPending` 返回类型从 int 改为 List，旧断言需改为 `Assert.Single`）

- [ ] **步骤 1：编写失败的服务端测试**

创建 `Server/Server.Tests/Handler/CastCancelBroadcastTests.cs`：

```csharp
using System.Linq;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CastCancelBroadcastTests
{
    private static (EntityStore Store, CombatResolver Resolver) NewWorld()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        return (store, new CombatResolver(store, new System.Random(1)));
    }

    private static CastRequest Cast(int skillId = 10002) => new CastRequest
    {
        SkillId = skillId,
        // 10001 的 Range 上限是 2f（2.5f 会被判 shape-tampered）；ClientCastSeq=0 避免两次
        // Decide 共用序号触发幂等重放，确保两个取消用例都真正入队。
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f },
        ClientCastSeq = 0
    };

    [Fact]
    public void CancelPending_By_CastId_Returns_One_Authoritative_Cancel()
    {
        var (store, resolver) = NewWorld();
        var cast = resolver.Decide(1, Cast(), 10);
        Assert.True(cast.Accepted);

        var cancels = resolver.CancelPending(1, 10002, cast.CastId, 12, NpcActionCancelReason.Forced);

        Assert.Single(cancels);
        Assert.Equal(cast.CastId, cancels[0].CastId);
        Assert.Equal(1L, cancels[0].CasterPlayerId);
        Assert.Equal(12u, cancels[0].EffectiveTick);
        Assert.Equal(NpcActionCancelReason.Forced, cancels[0].Reason);
        Assert.Empty(resolver.CancelPending(1, 10002, cast.CastId, 13));
    }

    [Fact]
    public void CancelCaster_Removes_All_Pending()
    {
        var (store, resolver) = NewWorld();
        var first = resolver.Decide(1, Cast(10001), 0);
        var second = resolver.Decide(1, Cast(10002), 100);
        Assert.True(first.Accepted);
        Assert.True(second.Accepted);

        var cancels = resolver.CancelCaster(1, 120, NpcActionCancelReason.Death);

        Assert.Equal(2, cancels.Count);
        Assert.Contains(cancels, c => c.CastId == first.CastId);
        Assert.Contains(cancels, c => c.CastId == second.CastId);
        Assert.Empty(resolver.CancelCaster(1, 121, NpcActionCancelReason.Death));
    }

    [Fact]
    public void Cancel_After_Launch_Keeps_Pending_And_Deduplicates()
    {
        var (store, resolver) = NewWorld();
        var cast = resolver.Decide(1, Cast(), 10);
        Assert.True(cast.Accepted);

        // 10002 首段 = decide + ceil(0.35s*15) = 16；三段的段末在 16/20/23。
        var firstStages = resolver.DrainResolved(21);
        Assert.Contains(firstStages, r => r.Launch != null);
        Assert.True(cast.CastId != 0);

        var cancels = resolver.CancelPending(1, 10002, cast.CastId, 22,
            NpcActionCancelReason.Hit);
        Assert.Single(cancels);
        Assert.Empty(resolver.CancelPending(1, 10002, cast.CastId, 23,
            NpcActionCancelReason.Hit));
        Assert.NotEmpty(resolver.DrainResolved(30));
    }

    [Fact]
    public void CancelPending_Without_CastId_Cancels_The_Requested_Skill()
    {
        var (store, resolver) = NewWorld();
        var first = resolver.Decide(1, Cast(10001), 0);
        var second = resolver.Decide(1, Cast(10002), 100);

        var cancels = resolver.CancelPending(1, 10002, 0, 120, NpcActionCancelReason.Forced);

        Assert.Single(cancels);
        Assert.Equal(second.CastId, cancels[0].CastId);
        Assert.NotEqual(first.CastId, cancels[0].CastId);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastCancelBroadcastTests"`

预期：编译失败，`CancelPending` 仍返回 `int`。

- [ ] **步骤 3：实现取消 API 与广播**

在 `PendingCast` 增加 `public bool CancelBroadcast;`（已发射且已广播过取消的实例不重复广播）。

将 `CombatResolver.CancelPending` 替换为：

```csharp
        public List<CastCancelBroadcast> CancelPending(long casterId, int skillId, ulong castId = 0,
            uint tick = 0, NpcActionCancelReason reason = NpcActionCancelReason.Forced)
        {
            var cancels = new List<CastCancelBroadcast>();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pending = _pending[i];
                if (pending.CasterId != casterId || pending.Request.SkillId != skillId) continue;
                if (castId != 0 && pending.CastId != castId) continue;
                if (pending.Launched)
                {
                    if (pending.CancelBroadcast) continue;
                    pending.CancelBroadcast = true;
                }
                else _pending.RemoveAt(i);
                cancels.Add(new CastCancelBroadcast
                {
                    CastId = pending.CastId,
                    CasterLifeId = pending.Broadcast.CasterLifeId,
                    CasterPlayerId = casterId,
                    EffectiveTick = tick,
                    Reason = reason
                });
            }
            return cancels;
        }

        public List<CastCancelBroadcast> CancelCaster(long casterId, uint tick, NpcActionCancelReason reason)
        {
            var cancels = new List<CastCancelBroadcast>();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pending = _pending[i];
                if (pending.CasterId != casterId) continue;
                if (pending.Launched)
                {
                    if (pending.CancelBroadcast) continue;
                    pending.CancelBroadcast = true;
                }
                else _pending.RemoveAt(i);
                cancels.Add(new CastCancelBroadcast
                {
                    CastId = pending.CastId,
                    CasterLifeId = pending.Broadcast.CasterLifeId,
                    CasterPlayerId = casterId,
                    EffectiveTick = tick,
                    Reason = reason
                });
            }
            return cancels;
        }
```

已发射的 `PendingCast` 保留（不 `RemoveAt`），让 `DrainResolved` 继续结算/在死亡时发 `ProjectileStopBroadcast`；`CastCancelBroadcast` 只停止攻击表现。

在 `CastHandler` 增加：

```csharp
        public async Task BroadcastCancelAsync(IReadOnlyList<CastCancelBroadcast> cancels)
        {
            // 先把全部收件人取好：UnregisterEntity 在 BroadcastCancel 返回后会立刻移除实体，
            // 首个 await 之后的 GetEntity 会返回 null，后续取消会静默丢失。
            var sends = new List<(IServerSession Session, CastCancelBroadcast Cancel)>();
            foreach (var cancel in cancels)
            {
                var caster = _server.GetEntity(cancel.CasterPlayerId);
                if (caster == null) continue;
                var recipients = CollectCastRecipients(cancel.CasterPlayerId,
                    caster.Kind == (byte)EntityKind.Npc);
                foreach (var recipientId in recipients)
                {
                    var session = _server.GetSession(recipientId);
                    if (session != null) sends.Add((session, cancel));
                }
            }

            foreach (var (session, cancel) in sends)
                await _server.SendToSessionAsync(session,
                    MessageId.CastCancelBroadcast, cancel);
        }

        public void BroadcastCancel(IReadOnlyList<CastCancelBroadcast> cancels)
            => _ = BroadcastCancelAsync(cancels);

        public List<CastCancelBroadcast> CancelCaster(long casterId, uint tick, NpcActionCancelReason reason)
            => _resolver.CancelCaster(casterId, tick, reason);
```

将 `HandleCancel` 改为：

```csharp
        public void HandleCancel(IServerSession session, CancelCastRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue) return;
            var cancels = _resolver.CancelPending(playerId.Value, request.SkillId,
                request.CastId, _server.CurrentTick + 1, NpcActionCancelReason.Forced);
            BroadcastCancel(cancels);
        }
```

在 `Server3C.TickRespawns` 的死亡标记处改为：

```csharp
                    if (!p.IsAlive && p.DeadTick == 0)
                    {
                        p.MarkDead(tick);
                        var cancels = _castHandler.CancelCaster(p.PlayerId, tick,
                            NpcActionCancelReason.Death);
                        _castHandler.BroadcastCancel(cancels);
                    }
```

在 `Server3C.UnregisterEntity` 移除实体前追加：

```csharp
            var cancels = _castHandler.CancelCaster(playerId, CurrentTick,
                NpcActionCancelReason.Forced);
            _castHandler.BroadcastCancel(cancels);
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastCancelBroadcastTests"`

预期：4/4 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Server3C.cs Server/Server.Tests/Combat/CombatResolverSpinTests.cs Server/Server.Tests/Handler/CastCancelBroadcastTests.cs
git commit -m "feat(server): broadcast cast cancellation by cast id"
```

---

## 任务 3：服务端 NpcActionEvent 与 AOI 基线

**文件：**
- 修改：`Server/Monster/MonsterModule.cs`
- 修改：`Server/Aoi/AoiBandScheduler.cs`
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Monster/NpcActionChainTests.cs`（新建）

- [ ] **步骤 1：编写失败的服务端测试**

创建 `Server/Server.Tests/Monster/NpcActionChainTests.cs`：

```csharp
using System.Linq;
using Cool.Protocol;
using KcpServer;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

public class NpcActionChainTests
{
    [Fact]
    public void Spawned_Monster_Exposes_Continuous_Action_Snapshot()
    {
        var store = new EntityStore();
        long seed = 2_000_000;
        var module = new MonsterModule(store, () => ++seed, actionV1: true);
        long id = module.SpawnMonster(1, new Vector3(0, 0, 0), new Vector3(0, 0, 0));

        var snapshot = module.GetActionSnapshot(id);

        Assert.NotNull(snapshot);
        Assert.Equal(NpcActionKind.Idle, snapshot!.Kind);
        Assert.Equal(NpcActionPhase.Continuous, snapshot.Phase);
    }

    [Fact]
    public void Aoi_Baseline_Carries_NpcAction_And_Mask()
    {
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "P1");
        observer.IsLoggedIn = true;
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        long seed = 2_000_000;
        var module = new MonsterModule(store, () => ++seed, actionV1: true);
        long id = module.SpawnMonster(1, new Vector3(1, 0, 1), new Vector3(1, 0, 1));

        var broadcaster = new AoiBroadcaster
        {
            NpcActionProvider = npcId => module.GetActionSnapshot(npcId)
        };
        var snapshots = broadcaster.BuildForObserver(observer, 1, store);

        var snapshot = snapshots.Single(s => s.PlayerId == id);
        Assert.NotNull(snapshot.NpcAction);
        Assert.NotEqual(0, snapshot.FieldMask & (ushort)EntityFieldMask.NpcAction);
    }

    [Theory]
    [InlineData(NpcActionKind.Idle, false)]
    [InlineData(NpcActionKind.Move, false)]
    [InlineData(NpcActionKind.Skill, true)]
    [InlineData(NpcActionKind.HitReaction, true)]
    [InlineData(NpcActionKind.Death, true)]
    public void Only_Non_Continuous_Actions_Broadcast(NpcActionKind kind, bool expected)
        => Assert.Equal(expected, Server3C.IsNpcActionBroadcastKind(kind));

    [Fact]
    public void Same_Instance_And_Revision_Is_Deduplicated()
    {
        Assert.True(Server3C.IsSameNpcAction(5, 2,
            new NpcActionSnapshot { InstanceId = 5, Revision = 2 }));
        Assert.False(Server3C.IsSameNpcAction(5, 2,
            new NpcActionSnapshot { InstanceId = 5, Revision = 3 }));
        Assert.False(Server3C.IsSameNpcAction(5, 2,
            new NpcActionSnapshot { InstanceId = 6, Revision = 2 }));
    }

    [Fact]
    public void Different_Zone_Is_Not_The_Same_Combat_Scene()
    {
        var a = new PlayerState(1) { ZoneId = "room-a" };
        var b = new PlayerState(2) { ZoneId = "room-b" };
        Assert.True(Server3C.IsSameCombatScene(a, a));
        Assert.False(Server3C.IsSameCombatScene(a, b));
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~NpcActionChainTests"`

预期：编译失败，`NpcActionProvider`、`GetActionSnapshot` 不存在。

- [ ] **步骤 3：实现服务端动作快照与基线**

在 `MonsterModule` 增加：

```csharp
        public List<(long NpcId, NpcActionSnapshot Snapshot)> GetActionSnapshots()
        {
            var result = new List<(long, NpcActionSnapshot)>();
            foreach (var pair in _monsters)
            {
                var action = pair.Value.ActionRunner?.Current;
                if (action != null) result.Add((pair.Key, action));
            }
            return result;
        }
```

在 `AoiBandScheduler` 增加（任务 7 会扩展为完整发送原因）：

```csharp
        /// <summary>该 (observer, entity) 是否尚未发送过（首次/重见）。</summary>
        public bool IsFirstSend(long observerId, long entityId)
            => !_state.TryGetValue(observerId, out var entities)
               || !entities.TryGetValue(entityId, out var slot)
               || !slot.HasSent;
```

在 `AoiBroadcaster` 增加：

```csharp
        /// <summary>怪物动作快照提供者（Server3C 接线 MonsterModule.GetActionSnapshot）。</summary>
        public System.Func<long, NpcActionSnapshot?>? NpcActionProvider { get; set; }
```

在 `BuildForObserver` 调用 `_scheduler.ShouldSend(...)` **之前**记录基线（`ShouldSend` 首次发送会把 `HasSent` 置 true，之后再问会得到 false）：

```csharp
                bool baseline = _scheduler.IsFirstSend(observer.PlayerId, entity.PlayerId);
```

然后在 `var snap = new EntitySnapshot { ... }` 之后追加：

```csharp
                    if (baseline)
                    {
                        var action = NpcActionProvider?.Invoke(entity.PlayerId);
                        if (action != null)
                        {
                            snap.NpcAction = action;
                            snap.FieldMask |= (ushort)EntityFieldMask.NpcAction;
                        }
                    }
```

在 `Server3C` 构造或 `EnableMonsters` 后接线：

```csharp
            _aoi.NpcActionProvider = id => _monsterModule?.GetActionSnapshot(id);
```

在 `Server3C` 增加 `SendNpcActionEvents`：

```csharp
        private readonly Dictionary<long, (ulong InstanceId, uint Revision)> _lastNpcActions =
            new Dictionary<long, (ulong, uint)>();

        private void SendNpcActionEvents(uint tick)
        {
            if (_monsterModule == null) return;
            foreach (var (npcId, action) in _monsterModule.GetActionSnapshots())
            {
                if (!IsNpcActionBroadcastKind(action.Kind)) continue;
                if (_lastNpcActions.TryGetValue(npcId, out var last)
                    && IsSameNpcAction(last.InstanceId, last.Revision, action))
                    continue;

                var entity = _entityStore.GetPlayer(npcId);
                if (entity == null) continue;
                _lastNpcActions[npcId] = (action.InstanceId, action.Revision);

                var message = new NpcActionEvent
                {
                    EntityId = npcId,
                    LifeId = entity.LifeId,
                    Action = action.Clone(),
                    ServerTimestamp = ServerClock.NowTicks()
                };

                foreach (var observerId in GetObserversOf(npcId))
                {
                    if (!IsSameCombatScene(entity, _entityStore.GetPlayer(observerId))) continue;
                    var session = GetSession(observerId);
                    if (session != null)
                        _ = SendToSessionAsync(session, MessageId.NpcActionEvent, message);
                }
            }
        }

        internal static bool IsNpcActionBroadcastKind(NpcActionKind kind)
            => kind is not (NpcActionKind.Idle or NpcActionKind.Move);

        internal static bool IsSameNpcAction(ulong instanceId, uint revision,
            NpcActionSnapshot action)
            => instanceId == action.InstanceId && revision == action.Revision;

        internal static bool IsSameCombatScene(PlayerState entity, PlayerState? observer)
            => entity.ZoneId.Length == 0 || observer?.ZoneId == entity.ZoneId;
```

在 tick lambda 的 `_monsterModule.Tick((uint)tick);` 之后、`DrainPendingCasts` 之前调用：

```csharp
                        SendNpcActionEvents((uint)tick);
                        _monsterModule.CaptureActionHistory((uint)tick);
```

同时在既有 `DrainRemovedNpcs` 循环里追加 `_lastNpcActions.Remove(npcId)`，避免怪物死亡换 id 重生后版本缓存无界增长：

```csharp
                        foreach (var npcId in _monsterModule.DrainRemovedNpcs())
                        {
                            _aoi.RemoveEntity(npcId);
                            _lastNpcActions.Remove(npcId);
                        }
```

- [ ] **步骤 4：运行测试验证通过**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~NpcActionChainTests"`

预期：2/2 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Server/Monster/MonsterModule.cs Server/Aoi/AoiBandScheduler.cs Server/Aoi/AoiBroadcaster.cs Server/Server3C.cs Server/Server.Tests/Monster/NpcActionChainTests.cs
git commit -m "feat(server): broadcast npc action phases and aoi baseline"
```

---

## 任务 4：客户端 NpcAction 动作存储与取消表现

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcActionConsumptionTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteCastPresenterCastIdTests.cs`（追加）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityAnimatorArbiterTests.cs`（追加）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcEntityAnimatorTests.cs`（追加）

- [ ] **步骤 1：编写失败的客户端测试**

创建 `Assets/Scripts/Tests/Editor/SyncLayer/NpcActionConsumptionTests.cs`：

```csharp
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class NpcActionConsumptionTests
    {
        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("NpcActionConsumptionTests");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void test_npc_action_applies_to_existing_view_and_ignores_old_revision()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 9, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead),
                X = 1f, Z = 1f, Dead = false, LifeId = 1
            }, 1);

            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 9, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 5, Revision = 2, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            });

            var view = _registry.GetView(9);
            Assert.IsNotNull(view);
            Assert.AreEqual(5UL, view!.ActionInstanceId);
            Assert.AreEqual(2u, view.ActionRevision);

            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 9, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 5, Revision = 1, Phase = NpcActionPhase.Active
                }
            });

            Assert.AreEqual(2u, view.ActionRevision, "older revision must not overwrite");
        }

        [Test]
        public void test_npc_action_before_view_is_buffered_and_replayed()
        {
            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 10, LifeId = 4,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 8, Revision = 3, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            });
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 10, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead),
                X = 0f, Z = 0f, Dead = false, LifeId = 4
            }, 1);

            var view = _registry.GetView(10);
            Assert.IsNotNull(view);
            Assert.AreEqual(8UL, view!.ActionInstanceId);
            Assert.AreEqual(3u, view.ActionRevision);
        }
    }
}
```

在 `RemoteCastPresenterCastIdTests` 追加：

```csharp
        [Test]
        public void Cancel_Before_Launch_Skips_Later_Visual()
        {
            int calls = 0;
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => { calls++; return new FakeVisual(); },
                sceneIdProvider: () => "room-a");

            presenter.OnCastCancel(new CastCancelBroadcast { CastId = 500 });
            presenter.OnProjectileLaunch(Launch(500));

            Assert.AreEqual(0, calls, "cancel arrived first must suppress the later launch");
        }

        [Test]
        public void Cancel_After_Launch_Keeps_Projectile_Flying()
        {
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) => visual,
                sceneIdProvider: () => "room-a");

            presenter.OnProjectileLaunch(Launch(501));
            presenter.OnCastCancel(new CastCancelBroadcast { CastId = 501 });

            Assert.AreEqual(1, presenter.CastVisualCountForTest,
                "an already launched projectile keeps flying after cancel");
            Assert.AreEqual(0, visual.Stops);
        }
```

在 `RemoteEntityAnimatorArbiterTests` 追加：

```csharp
        [Test]
        public void test_cancel_attack_clears_only_the_matching_cast()
        {
            _driver.PlayAttack("Attack", 1f, 1f, castId: 7);
            Assert.Greater(_driver.AttackRemainingForTest, 0f);

            _driver.CancelAttack(8);
            Assert.Greater(_driver.AttackRemainingForTest, 0f,
                "a different cast must not cancel the current attack");

            _driver.CancelAttack(7);
            Assert.AreEqual(0f, _driver.AttackRemainingForTest);
        }
```

在 `NpcEntityAnimatorTests` 追加：

```csharp
        [Test]
        public void ApplyAction_Cancel_Clears_The_Matching_Instance()
        {
            _driver.ApplyAction(new NpcActionSnapshot
            {
                InstanceId = 3, Revision = 1, Kind = NpcActionKind.Skill,
                Phase = NpcActionPhase.Windup, PresentationVariant = 0
            });
            Assert.AreEqual(NpcEntityAnimator.StateAttacking,
                _animator.GetInteger("MonsterState"));

            _driver.ApplyAction(new NpcActionSnapshot
            {
                InstanceId = 3, Revision = 2, Kind = NpcActionKind.Skill,
                Phase = NpcActionPhase.Cancelled
            });
            Assert.AreEqual(NpcEntityAnimator.StateIdle,
                _animator.GetInteger("MonsterState"));
        }
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode NpcActionConsumptionTests
bash tools/unity-tests.sh EditMode RemoteCastPresenterCastIdTests
```

预期：编译失败，`ApplyNpcActionEvent`、`ActionInstanceId`、`OnCastCancel` 不存在。

- [ ] **步骤 3：实现客户端动作存储与取消**

在 `RemoteEntityView` 增加（`LifeId` 本任务首次加入，Part B 不重复加）：

```csharp
        public ulong ActionInstanceId { get; set; }
        public uint ActionRevision { get; set; }
        public uint LifeId { get; set; }
```

在 `RemoteEntityRegistry` 增加：

```csharp
        private sealed class PendingAction
        {
            public long EntityId;
            public uint LifeId;
            public NpcActionSnapshot Action = new NpcActionSnapshot();
        }

        private readonly Dictionary<(long EntityId, uint LifeId, ulong InstanceId), NpcActionSnapshot>
            _npcActions = new Dictionary<(long, uint, ulong), NpcActionSnapshot>();
        private readonly List<PendingAction> _pendingNpcActions = new List<PendingAction>();

        public void ApplyNpcActionEvent(NpcActionEvent evt)
        {
            if (evt?.Action == null) return;
            if (!_views.TryGetValue(evt.EntityId, out var view))
            {
                CacheNpcAction(evt);
                return;
            }
            if (view.LifeId != 0 && evt.LifeId != 0 && view.LifeId != evt.LifeId) return;
            ApplyNpcAction(evt.EntityId, view, evt.LifeId, evt.Action);
        }

        private void CacheNpcAction(NpcActionEvent evt)
        {
            if (_pendingNpcActions.Count >= 64) _pendingNpcActions.RemoveAt(0);
            _pendingNpcActions.Add(new PendingAction
            {
                EntityId = evt.EntityId,
                LifeId = evt.LifeId,
                Action = evt.Action.Clone()
            });
        }

        private void ApplyNpcAction(long entityId, RemoteEntityView view, uint lifeId,
            NpcActionSnapshot action)
        {
            if (view.ActionInstanceId == action.InstanceId && action.Revision <= view.ActionRevision)
                return;
            view.ActionInstanceId = action.InstanceId;
            view.ActionRevision = action.Revision;
            if (lifeId != 0) view.LifeId = lifeId;
            _npcActions[(entityId, lifeId, action.InstanceId)] = action.Clone();
            if (_npcActions.Count > 256) _npcActions.Clear();
            view.NpcAnimator?.ApplyAction(action);
            if (action.Phase == NpcActionPhase.Cancelled)
                view.NpcAnimator?.CancelAttack(action.InstanceId);
        }
```

在 `ApplySnapshot` 的 `if (created) ViewCreated?.Invoke(...)` 之前追加：

```csharp
            if (snapshot.LifeId != 0) view.LifeId = snapshot.LifeId;
            for (int i = _pendingNpcActions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingNpcActions[i];
                if (pending.EntityId != snapshot.PlayerId) continue;
                _pendingNpcActions.RemoveAt(i);
                ApplyNpcAction(pending.EntityId, view, pending.LifeId, pending.Action);
            }
```

在 `NpcEntityAnimator` 增加：

```csharp
        private ulong _actionInstanceId;

        public void ApplyAction(NpcActionSnapshot action)
        {
            _actionInstanceId = action.InstanceId;
            switch (action.Kind)
            {
                case NpcActionKind.Skill:
                    if (action.Phase is NpcActionPhase.Accepted or NpcActionPhase.Windup)
                        PlayAttack(action.PresentationVariant);
                    else if (action.Phase == NpcActionPhase.Cancelled)
                        CancelAttack(action.InstanceId);
                    break;
                case NpcActionKind.HitReaction:
                    ApplyState((byte)NpcAnimationState.Hit, false);
                    break;
                case NpcActionKind.Death:
                    ApplyState((byte)NpcAnimationState.Death, true);
                    break;
            }
        }

        public void CancelAttack(ulong instanceId)
        {
            if (_actionInstanceId != instanceId) return;
            _busy = false;
            _awaitingConfirm = false;
            Write(StateIdle);
        }
```

在 `RemoteEntityAnimator` 增加 `_attackCastId`，并修改 `PlayAttack`：

```csharp
        private ulong _attackCastId;

        internal void PlayAttack(string trigger, float duration, float attackSpeed, ulong castId = 0)
        {
            _attackCastId = castId;
            // 现有实现保持：Death/Down 守卫、_attackSpeed、_attackRemaining、Attack trigger
        }

        internal void CancelAttack(ulong castId)
        {
            if (castId != 0 && _attackCastId != castId) return;
            _attackCastId = 0;
            _attackRemaining = 0f;
            Reapply();
        }
```

在 `RemoteCastPresenter` 增加：

```csharp
        private readonly Dictionary<ulong, long> _castCasters = new Dictionary<ulong, long>();
        private readonly HashSet<ulong> _cancelledCasts = new HashSet<ulong>();

        public void OnCastCancel(CastCancelBroadcast cancel)
        {
            if (cancel == null || cancel.CastId == 0) return;
            _cancelledCasts.Add(cancel.CastId);
            if (_cancelledCasts.Count > 256) _cancelledCasts.Clear();
            if (_castCasters.TryGetValue(cancel.CastId, out var casterId)
                && _registry?.GetView(casterId) is { } view)
                view.Animator?.CancelAttack(cancel.CastId);
            // 已发射的 _castVisuals 保留飞行；取消只停止攻击表现。
        }
```

把 `RememberCastSkill` 改为：

```csharp
        private void RememberCastSkill(ulong castId, int skillId, long casterId)
        {
            _castSkillIds[castId] = skillId;
            _castCasters[castId] = casterId;
            if (_castSkillIds.Count > 256)
            {
                _castSkillIds.Clear();
                _castCasters.Clear();
            }
        }
```

`OnCastBroadcast` 的调用改为 `RememberCastSkill(broadcast.CastId, broadcast.SkillId, broadcast.CasterPlayerId);`；`view.Animator?.PlayAttack(...)` 传入 `broadcast.CastId`；`_cancelledCasts.Contains(broadcast.CastId)` 时跳过挥砍表现。`OnProjectileLaunch` 开头增加：

```csharp
            if (_cancelledCasts.Contains(launch.CastId)) return;
```

在 `CombatNetworkBridge` 增加事件与路由：

```csharp
        public event Action<CastCancelBroadcast>? OnCastCancel;
        public event Action<NpcActionEvent>? OnNpcAction;
```

`OnMessage` 的 `switch` 增加 `CastCancelBroadcast`、`NpcActionEvent`；`Update` 增加对应 `case` 调用事件。

在 `ClientNetworkHost` 的 `TryAttachLocalCharacter` 订阅：

```csharp
            _combatBridge.OnCastCancel += HandleCastCancel;
            _combatBridge.OnNpcAction += HandleNpcAction;
```

实现：

```csharp
        private void HandleCastCancel(CastCancelBroadcast cancel)
            => _remoteCastPresenter?.OnCastCancel(cancel);

        private void HandleNpcAction(NpcActionEvent evt)
            => _remoteEntities?.ApplyNpcActionEvent(evt);
```

并在 `DetachCombatBridge` 退订。

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode NpcActionConsumptionTests
bash tools/unity-tests.sh EditMode RemoteCastPresenterCastIdTests
bash tools/unity-tests.sh EditMode RemoteEntityAnimatorArbiterTests
bash tools/unity-tests.sh EditMode NpcEntityAnimatorTests
```

预期：全部 PASS（新增用例至少 4 个通过）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs Assets/Scripts/Tests/Editor/SyncLayer/NpcActionConsumptionTests.cs Assets/Scripts/Tests/Editor/SyncLayer/RemoteCastPresenterCastIdTests.cs Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityAnimatorArbiterTests.cs Assets/Scripts/Tests/Editor/SyncLayer/NpcEntityAnimatorTests.cs
git commit -m "feat(client): consume cast cancel and npc action events"
```

---

## 任务 5：Part A 回归与提交检查

- [ ] **步骤 1：跑 Part A 服务端与 Unity 回归**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatCancelMessagesTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastCancelBroadcastTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~NpcActionChainTests"
bash tools/unity-tests.sh EditMode NpcActionConsumptionTests
bash tools/unity-tests.sh EditMode RemoteCastPresenterCastIdTests
bash tools/unity-tests.sh EditMode RemoteEntityAnimatorArbiterTests
bash tools/unity-tests.sh EditMode NpcEntityAnimatorTests
```

预期：全部 PASS。若既有类因 `NpcActionEvent.LifeId`/`CancelCastRequest.CastId` 的追加字段失败，先修测试夹具再继续。

- [ ] **步骤 2：更新协议与 SyncLayer README**

在 `Assets/Scripts/AOT/Protocol/README.md` 的属性剑气段追加 `CastCancelBroadcast`（310）与 `CancelCastRequest.CastId` 的说明；在 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md` 的属性剑气段追加取消/NpcAction 消费口径。只写已实现内容，不写 Plan 03/04 的状态。

- [ ] **步骤 3：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/README.md Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md
git commit -m "docs: record part a cancel and npc action wiring"
```

---

# Part B：B2 战斗投影/带宽

## 任务 6：战斗投影协议信封

**文件：**
- 创建：`Assets/Scripts/AOT/Protocol/Messages/CombatProjection.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageCodec.cs`
- 测试：`Server/Server.Tests/Protocol/CombatProjectionMessagesTests.cs`（新建）

- [ ] **步骤 1：编写失败的协议测试**

创建 `Server/Server.Tests/Protocol/CombatProjectionMessagesTests.cs`：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class CombatProjectionMessagesTests
{
    [Fact]
    public void CombatStateUpdate_Roundtrips_All_Plan02_Fields()
    {
        var decoded = (CombatStateUpdate)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CombatStateUpdate,
            new CombatStateUpdate
            {
                EntityId = 7,
                ServerTimestamp = 100,
                Projection = new CombatProjection
                {
                    CombatVersion = 4,
                    LifeId = 2,
                    Hp = 720f,
                    MaxHp = 720f,
                    Dead = false,
                    IsDefending = true,
                    Statuses = new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 1, GroupId = 1, EndTick = 30 }
                    },
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                    },
                    Control = new ControlSummary
                    {
                        SpeedScale = 0.7f, ActionLock = false, ControlVersion = 3
                    },
                    ActiveCast = new ActiveCastProjection
                    {
                        CastId = 9, SkillId = 10002,
                        Phase = NpcActionPhase.Windup, EndTick = 44
                    }
                }
            })).message;

        Assert.Equal(7L, decoded.EntityId);
        Assert.Equal(4u, decoded.Projection.CombatVersion);
        Assert.Equal(2u, decoded.Projection.LifeId);
        Assert.Equal(720f, decoded.Projection.Hp);
        Assert.Single(decoded.Projection.Statuses);
        Assert.Equal(42L, decoded.Projection.Equipment!.WeaponInstanceId);
        Assert.Equal(0.7f, decoded.Projection.Control!.SpeedScale);
        Assert.Equal(9UL, decoded.Projection.ActiveCast!.CastId);
    }

    [Fact]
    public void EntitySnapshot_Carries_CombatVersion_And_LifeId()
    {
        var decoded = ((WorldSnapshot)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.WorldSnapshot,
            new WorldSnapshot
            {
                Entities = new List<EntitySnapshot>
                {
                    new EntitySnapshot
                    {
                        PlayerId = 1, FieldMask = (ushort)EntityFieldMask.Health,
                        CombatVersion = 9, LifeId = 3
                    }
                }
            })).message).Entities[0];

        Assert.Equal(9u, decoded.CombatVersion);
        Assert.Equal(3u, decoded.LifeId);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionMessagesTests"`

预期：编译失败，`CombatStateUpdate` 等类型不存在。

- [ ] **步骤 3：实现投影类型与快照字段**

创建 `Assets/Scripts/AOT/Protocol/Messages/CombatProjection.cs`：

```csharp
using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>状态/装备/控制/活动施法的可靠完整投影；Plan 02 只填 HP/装备/活动施法。</summary>
    [ProtoContract]
    public class CombatStateUpdate : IMessage
    {
        [ProtoMember(1)] public long EntityId { get; set; }
        [ProtoMember(2)] public CombatProjection Projection { get; set; } = new CombatProjection();
        [ProtoMember(3)] public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class CombatProjection : IMessage
    {
        [ProtoMember(1)] public uint CombatVersion { get; set; }
        [ProtoMember(2)] public uint LifeId { get; set; }
        [ProtoMember(3)] public float Hp { get; set; }
        [ProtoMember(4)] public float MaxHp { get; set; }
        [ProtoMember(5)] public bool Dead { get; set; }
        [ProtoMember(6)] public bool IsDefending { get; set; }
        /// <summary>IList：IL2CPP tuple 探测缺口，禁止 List&lt;T&gt;。</summary>
        [ProtoMember(7)] public IList<StatusProjection> Statuses { get; set; } = new List<StatusProjection>();
        [ProtoMember(8)] public EquipmentSummary? Equipment { get; set; }
        [ProtoMember(9)] public ControlSummary? Control { get; set; }
        [ProtoMember(10)] public ActiveCastProjection? ActiveCast { get; set; }
    }

    [ProtoContract]
    public class StatusProjection : IMessage
    {
        [ProtoMember(1)] public int EffectId { get; set; }
        [ProtoMember(2)] public int GroupId { get; set; }
        [ProtoMember(3)] public uint EndTick { get; set; }
        [ProtoMember(4)] public long SourceId { get; set; }
        [ProtoMember(5)] public float SlowRatio { get; set; }
        [ProtoMember(6)] public float DamagePerTick { get; set; }
        [ProtoMember(7)] public uint IntervalTicks { get; set; }
        [ProtoMember(8)] public float IncomingDamageMultiplier { get; set; }
    }

    [ProtoContract]
    public class EquipmentSummary : IMessage
    {
        [ProtoMember(1)] public long WeaponInstanceId { get; set; }
        [ProtoMember(2)] public int TemplateId { get; set; }
        [ProtoMember(3)] public int ElementId { get; set; }
    }

    [ProtoContract]
    public class ControlSummary : IMessage
    {
        [ProtoMember(1)] public float SpeedScale { get; set; } = 1f;
        [ProtoMember(2)] public bool ActionLock { get; set; }
        [ProtoMember(3)] public uint ControlVersion { get; set; }
    }

    [ProtoContract]
    public class ActiveCastProjection : IMessage
    {
        [ProtoMember(1)] public ulong CastId { get; set; }
        [ProtoMember(2)] public int SkillId { get; set; }
        [ProtoMember(3)] public NpcActionPhase Phase { get; set; }
        [ProtoMember(4)] public uint EndTick { get; set; }
    }
}
```

在 `WorldSnapshot.cs` 的 `EntityFieldMask` 增加：

```csharp
        /// <summary>状态/装备/控制/活动施法投影（仅 Baseline 与 CombatStateUpdate 使用）。</summary>
        Combat = 1 << 10
```

在 `EntitySnapshot` 增加：

```csharp
        [ProtoMember(18)] public uint CombatVersion { get; set; }
        [ProtoMember(19)] public uint LifeId { get; set; }
        [ProtoMember(20)] public CombatProjection? Combat { get; set; }
```

在 `MessageId.cs` 战斗段追加 `CombatStateUpdate = 311`，然后在 `MessageCodec.cs` 注册：

```csharp
            RegisterMessageType(MessageId.CombatStateUpdate, typeof(CombatStateUpdate));
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionMessagesTests"
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
```

预期：协议测试 2/2 PASS；形状守卫 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/Messages/CombatProjection.cs Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs Assets/Scripts/AOT/Protocol/MessageCodec.cs Server/Server.Tests/Protocol/CombatProjectionMessagesTests.cs
git commit -m "feat(protocol): add combat projection envelope"
```

---

## 任务 7：AOI 发送原因与条件字段

**文件：**
- 修改：`Server/Aoi/AoiBandScheduler.cs`
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- 测试：`Server/Server.Tests/Aoi/AoiBandSchedulerTests.cs`
- 测试：`Server/Server.Tests/Aoi/AoiBroadcasterTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs`

- [ ] **步骤 1：编写失败的调度器测试**

在 `AoiBandSchedulerTests` 追加：

```csharp
    [Fact]
    public void First_Send_Is_Baseline_Reentry_Is_Baseline()
    {
        var scheduler = new AoiBandScheduler();
        var first = scheduler.ShouldSend(1, 2, 5f, 1, 0f, 0f, 0f, 0, false, 100f, 100f, false);
        Assert.Equal(AoiSendReason.Baseline, first.Reason);

        scheduler.MarkOutOfSight(1, 2);
        var reentry = scheduler.ShouldSend(1, 2, 5f, 30, 0f, 0f, 0f, 0, false, 100f, 100f, false);
        Assert.Equal(AoiSendReason.Baseline, reentry.Reason);
    }

    [Fact]
    public void Health_Change_Is_Changed_With_Health_Flag()
    {
        var scheduler = new AoiBandScheduler();
        scheduler.ShouldSend(1, 2, 5f, 1, 0f, 0f, 0f, 0, false, 100f, 100f, false);
        var changed = scheduler.ShouldSend(1, 2, 5f, 2, 0f, 0f, 0f, 0, false, 80f, 100f, false);
        Assert.Equal(AoiSendReason.Changed, changed.Reason);
        Assert.True((changed.Changes & AoiChangeFlags.Health) != 0);
    }

    [Fact]
    public void Static_Keepalive_Is_Keepalive()
    {
        var scheduler = new AoiBandScheduler();
        scheduler.ShouldSend(1, 2, 5f, 1, 0f, 0f, 0f, 0, false, 100f, 100f, false);
        var keepalive = scheduler.ShouldSend(1, 2, 5f, 5, 0f, 0f, 0f, 0, false, 100f, 100f, false);
        Assert.Equal(AoiSendReason.Keepalive, keepalive.Reason);
    }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiBandSchedulerTests"`

预期：编译失败，`AoiSendReason` 不存在。

- [ ] **步骤 3：实现发送原因与条件字段**

在 `AoiBandScheduler.cs` 顶部增加：

```csharp
    public enum AoiSendReason
    {
        None = 0,
        Baseline = 1,
        Keepalive = 2,
        Changed = 3
    }

    [System.Flags]
    public enum AoiChangeFlags
    {
        None = 0,
        Health = 1,
        Defending = 2,
        Action = 4,
        Dead = 8
    }

    public readonly struct AoiSendDecision
    {
        public readonly AoiSendReason Reason;
        public readonly AoiChangeFlags Changes;
        public AoiSendDecision(AoiSendReason reason, AoiChangeFlags changes)
        { Reason = reason; Changes = changes; }
    }
```

把 `ShouldSend` 的返回类型改为 `AoiSendDecision`：

- 首次/重见分支返回 `Baseline`，`Changes = Health | Defending | Action | Dead`。
- 带变化分支返回 `Changed`，`Changes` 按 `slot` 与当前值逐项比较。
- 保活/移动到期分支返回 `moved ? Changed : Keepalive`，`Changes = None`。
- 未发送返回 `None`。

关键片段：

```csharp
            var changes = AoiChangeFlags.None;
            if (slot.Dead != dead) changes |= AoiChangeFlags.Dead;
            if (slot.Hp != hp || slot.MaxHp != maxHp) changes |= AoiChangeFlags.Health;
            if (slot.IsDefending != defending) changes |= AoiChangeFlags.Defending;
            if (slot.ActionState != actionState) changes |= AoiChangeFlags.Action;
            if (changes != AoiChangeFlags.None)
            {
                slot.ActionState = actionState;
                slot.Dead = dead;
                slot.Hp = hp;
                slot.MaxHp = maxHp;
                slot.IsDefending = defending;
                slot.X = x;
                slot.Z = z;
                slot.RotY = rotY;
                entities[entityId] = slot;
                return new AoiSendDecision(AoiSendReason.Changed, changes);
            }
```

`IsFirstSend` 保留，内部复用 `_state`；它在任务 3 已加入。

现有 `AoiBandSchedulerTests.Send(...)` 帮助方法的参数列表保持不变，只把返回表达式改为：

```csharp
private static bool Send(AoiBandScheduler s, long observerId, long entityId,
    float distance, uint tick, float x, float z, float rotY, byte actionState,
    bool dead, float hp, float maxHp, bool defending)
    => s.ShouldSend(observerId, entityId, distance, tick, x, z, rotY,
        actionState, dead, hp, maxHp, defending).Reason != AoiSendReason.None;
```

在 `AoiBroadcaster.BuildForObserver` 使用 `decision`：

```csharp
                var decision = _scheduler.ShouldSend(...);
                if (decision.Reason == AoiSendReason.None) continue;

                var mask = EntityFieldMask.Position | EntityFieldMask.Rotation
                    | EntityFieldMask.Speed | EntityFieldMask.ActionState
                    | EntityFieldMask.Kind | EntityFieldMask.TemplateId;
                if (decision.Reason == AoiSendReason.Baseline
                    || (decision.Changes & AoiChangeFlags.Dead) != 0)
                    mask |= EntityFieldMask.Dead;
                if (decision.Reason == AoiSendReason.Baseline
                    || (decision.Changes & AoiChangeFlags.Health) != 0)
                    mask |= EntityFieldMask.Health;
                if (decision.Reason == AoiSendReason.Baseline
                    || (decision.Changes & AoiChangeFlags.Defending) != 0)
                    mask |= EntityFieldMask.Defending;
```

基线分支继续追加 `NpcAction`（任务 3）与 `Combat`（任务 8A）。`EntitySnapshot.Dead/Hp/MaxHp/IsDefending` 只在对应位被置位时填值；未置位保持默认。当 `Health` 或 `Combat` 位置位时，同时写 `snap.CombatVersion = entity.CombatVersion; snap.LifeId = entity.LifeId;`。把现有 `new EntitySnapshot { ... FieldMask = 全掩码 }` 的 `FieldMask` 改为 `(ushort)mask`，其余字段保持。把任务 3 的 `_scheduler.IsFirstSend(...)` 改为 `decision.Reason == AoiSendReason.Baseline`。

在 `RemoteEntityRegistry.ApplySnapshot` 把 `view.Dead = snapshot.Dead;` 改为：

```csharp
            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Dead) != 0)
                view.Dead = snapshot.Dead;
```

`view.Targetable.SetVitals(view.Hp, view.MaxHp, view.Dead)` 与 `view.Animator?.ApplySnapshotState(view.Dead)` 改用存值。

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiBandSchedulerTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiBroadcasterTests"
bash tools/unity-tests.sh EditMode RemoteEntityRegistryTests
```

预期：全部 PASS。`AoiBroadcasterTests` 中旧的“全掩码”断言按新的基线/条件字段更新。

- [ ] **步骤 5：Commit**

```bash
git add Server/Aoi/AoiBandScheduler.cs Server/Aoi/AoiBroadcaster.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs Server/Server.Tests/Aoi/AoiBandSchedulerTests.cs Server/Server.Tests/Aoi/AoiBroadcasterTests.cs Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs
git commit -m "feat(aoi): send reasons and conditional combat fields"
```

---

## 任务 8A：服务端战斗投影与更新发送

**文件：**
- 创建：`Server/Aoi/CombatProjectionBuilder.cs`
- 修改：`Server/Player/PlayerState.cs`
- 修改：`Server/Handler/EquipHandler.cs`
- 修改：`Server/Handler/CastHandler.cs`（活动施法生命周期）
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Aoi/CombatProjectionBuilderTests.cs`（新建）

- [ ] **步骤 1：编写失败的投影构建测试**

创建 `Server/Server.Tests/Aoi/CombatProjectionBuilderTests.cs`：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CombatProjectionBuilderTests
{
    [Fact]
    public void Build_Carries_Authoritative_State_And_Equipment()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.EquippedWeaponInstanceId = 42;
        player.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquipmentRevision = 3;
        player.CombatVersion = 7;
        player.Hp = 600f;
        player.MaxHp = 720f;
        player.IsDefending = true;

        var projection = CombatProjectionBuilder.Build(player, ElementalCatalogDefaults.Create());

        Assert.Equal(7u, projection.CombatVersion);
        Assert.Equal(1u, projection.LifeId);
        Assert.Equal(600f, projection.Hp);
        Assert.True(projection.IsDefending);
        Assert.Equal(42L, projection.Equipment!.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, projection.Equipment.TemplateId);
        Assert.Equal(1, projection.Equipment.ElementId);
        Assert.Empty(projection.Statuses);
        Assert.Equal(1f, projection.Control!.SpeedScale);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionBuilderTests"`

预期：编译失败，`CombatProjectionBuilder`、`PlayerState.CombatVersion` 不存在。

- [ ] **步骤 3：实现投影构建、活动施法与发送**

在 `PlayerState` 增加：

```csharp
        public uint CombatVersion { get; internal set; }
        public bool CombatDirty { get; internal set; }
        public ActiveCastProjection? ActiveCast { get; internal set; }

        public void BumpCombatVersion()
        {
            CombatVersion++;
            CombatDirty = true;
        }
```

同时让血量/死亡/防御变化推进同一版本：

```csharp
        public void ApplyDamage(float damage)
        {
            Hp -= damage;
            BumpCombatVersion();
        }

        public void MarkDead(long tick)
        {
            if (DeadTick != 0) return;
            DeadTick = tick;
            BumpCombatVersion();
        }
```

`TryRespawn` 在 `Hp = MaxHp; DeadTick = 0; LifeId++;` 后调用 `BumpCombatVersion()`。
`PositionSyncHandler` 在 `player.IsDefending` 实际变化时调用 `player.BumpCombatVersion()`。

创建 `Server/Aoi/CombatProjectionBuilder.cs`：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    public static class CombatProjectionBuilder
    {
        public static CombatProjection Build(PlayerState entity, ElementalCatalogDefinition catalog)
        {
            var weapon = catalog.Weapons.Find(w => w.TemplateId == entity.EquippedWeaponTemplateId);
            return new CombatProjection
            {
                CombatVersion = entity.CombatVersion,
                LifeId = entity.LifeId,
                Hp = entity.Hp,
                MaxHp = entity.MaxHp,
                Dead = !entity.IsAlive,
                IsDefending = entity.IsDefending,
                Statuses = new List<StatusProjection>(),
                Equipment = entity.EquippedWeaponInstanceId != 0
                    ? new EquipmentSummary
                    {
                        WeaponInstanceId = entity.EquippedWeaponInstanceId,
                        TemplateId = entity.EquippedWeaponTemplateId,
                        ElementId = weapon?.ElementId ?? 0
                    }
                    : null,
                Control = new ControlSummary { SpeedScale = 1f, ActionLock = false, ControlVersion = 0 },
                ActiveCast = entity.ActiveCast
            };
        }
    }
}
```

在 `CombatResolver` 增加活动施法生命周期：

```csharp
        private static void SetActiveCast(PlayerState caster, PendingCast pending, NpcActionPhase phase)
        {
            caster.ActiveCast = new ActiveCastProjection
            {
                CastId = pending.CastId,
                SkillId = pending.Request.SkillId,
                Phase = phase,
                EndTick = pending.StageTicks.Length > 0 ? pending.StageTicks[^1] : 0u
            };
            caster.BumpCombatVersion();
        }

        private static void ClearActiveCast(PlayerState? caster, ulong castId)
        {
            if (caster?.ActiveCast == null || caster.ActiveCast.CastId != castId) return;
            caster.ActiveCast = null;
            caster.BumpCombatVersion();
        }
```

在 `Decide` 的 `_pending.Add(...)` 之后、返回前：

```csharp
            var casterState = _store.GetPlayer(casterId);
            if (casterState != null)
                SetActiveCast(casterState, _pending[^1], NpcActionPhase.Windup);
```

在 `DrainResolved` 的 `pending.NextStage++;` 之后：

```csharp
                    var casterState = _store.GetPlayer(pending.CasterId);
                    if (pending.NextStage >= pending.StageTicks.Length)
                        ClearActiveCast(casterState, pending.CastId);
                    else
                        SetActiveCast(casterState!, pending,
                            pending.NextStage == 1 ? NpcActionPhase.Active : NpcActionPhase.Recovery);
```

在 `DrainResolved` 的 `resolution == null` 分支 `pending.Spent = true;` 之前：

```csharp
                        ClearActiveCast(_store.GetPlayer(pending.CasterId), pending.CastId);
```

在 `CancelPending`/`CancelCaster` 的 `_pending.RemoveAt(i);` 之后：

```csharp
                ClearActiveCast(_store.GetPlayer(casterId), pending.CastId);
```

在 `EquipHandler.ValidateAndApply` 更新装备后追加：

```csharp
            player.BumpCombatVersion();
```

在 `AoiBroadcaster` 增加：

```csharp
        public System.Func<PlayerState, CombatProjection>? CombatProjectionProvider { get; set; }
```

在基线分支追加：

```csharp
                        var projection = CombatProjectionProvider?.Invoke(entity);
                        if (projection != null)
                        {
                            snap.Combat = projection;
                            snap.FieldMask |= (ushort)EntityFieldMask.Combat;
                            snap.CombatVersion = projection.CombatVersion;
                            snap.LifeId = projection.LifeId;
                        }
```

在 `Server3C` 接线：

```csharp
            _aoi.CombatProjectionProvider = entity =>
                CombatProjectionBuilder.Build(entity, _elementalCatalog);
```

在 `Server3C` 增加 `SendCombatStateUpdates`：

```csharp
        private void SendCombatStateUpdates()
        {
            foreach (var entity in _entityStore.All)
            {
                if (!entity.CombatDirty) continue;
                entity.CombatDirty = false;

                var projection = CombatProjectionBuilder.Build(entity, _elementalCatalog);
                var update = new CombatStateUpdate
                {
                    EntityId = entity.PlayerId,
                    Projection = projection,
                    ServerTimestamp = ServerClock.NowTicks()
                };

                var recipients = new HashSet<long>();
                if (entity.Kind == (byte)EntityKind.Npc)
                {
                    foreach (var id in GetObserversOf(entity.PlayerId)) recipients.Add(id);
                }
                else
                {
                    if (_playerIdToSession.ContainsKey(entity.PlayerId))
                        recipients.Add(entity.PlayerId);
                    foreach (var p in GetVisiblePlayers(entity.PlayerId)) recipients.Add(p.PlayerId);
                }

                var scene = entity.ZoneId;
                foreach (var recipientId in recipients)
                {
                    if (_entityStore.GetPlayer(recipientId) is { } recipient
                        && recipient.ZoneId != scene) continue;
                    var session = GetSession(recipientId);
                    if (session != null)
                        _ = SendToSessionAsync(session, MessageId.CombatStateUpdate, update);
                }
            }
        }
```

在 tick lambda 的 `TickRespawns((uint)tick);` 之后、`BroadcastWorldSnapshot` 之前调用：

```csharp
                    SendCombatStateUpdates();
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionBuilderTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiBroadcasterTests"
```

预期：全部 PASS；基线快照的 `FieldMask.Combat` 与 `Combat` 非空。

- [ ] **步骤 5：Commit**

```bash
git add Server/Aoi/CombatProjectionBuilder.cs Server/Player/PlayerState.cs Server/Handler/EquipHandler.cs Server/Handler/CastHandler.cs Server/Aoi/AoiBroadcaster.cs Server/Server3C.cs Server/Server.Tests/Aoi/CombatProjectionBuilderTests.cs Server/Server.Tests/Aoi/AoiBroadcasterTests.cs
git commit -m "feat(server): send combat projection baselines and updates"
```

---

## 任务 8B：客户端投影消费与版本合并

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/CombatStateProjectionTests.cs`（新建）

- [ ] **步骤 1：编写失败的客户端投影测试**

创建 `Assets/Scripts/Tests/Editor/SyncLayer/CombatStateProjectionTests.cs`：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class CombatStateProjectionTests
    {
        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("CombatStateProjectionTests");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void test_projection_applies_higher_combat_version_and_ignores_older()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 7, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 1, LifeId = 1
            }, 1);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 7,
                Projection = new CombatProjection
                {
                    CombatVersion = 3, LifeId = 1, Hp = 500f, MaxHp = 720f,
                    Dead = false, IsDefending = true,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                    }
                }
            });

            var view = _registry.GetView(7);
            Assert.IsNotNull(view);
            Assert.AreEqual(500f, view!.Hp);
            Assert.IsTrue(view.IsDefending);
            Assert.AreEqual(42L, view.Equipment!.WeaponInstanceId);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 7,
                Projection = new CombatProjection
                {
                    CombatVersion = 2, LifeId = 1, Hp = 1f, MaxHp = 720f
                }
            });

            Assert.AreEqual(500f, view.Hp, "older combat version must not overwrite");
        }

        [Test]
        public void test_empty_status_list_clears_existing_statuses()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 9, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 1, LifeId = 1
            }, 1);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 9,
                Projection = new CombatProjection
                {
                    CombatVersion = 2, LifeId = 1,
                    Statuses = new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 1, GroupId = 1 }
                    }
                }
            });
            Assert.AreEqual(1, _registry.GetView(9)!.Statuses.Count);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 9,
                Projection = new CombatProjection
                {
                    CombatVersion = 3, LifeId = 1,
                    Statuses = new List<StatusProjection>()
                }
            });
            Assert.AreEqual(0, _registry.GetView(9)!.Statuses.Count,
                "an empty status list clears the previous statuses");
        }

        [Test]
        public void test_baseline_snapshot_applies_projection()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 8, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health | EntityFieldMask.Combat),
                X = 0f, Z = 0f, Dead = false, Hp = 300f, MaxHp = 720f,
                CombatVersion = 5, LifeId = 2,
                Combat = new CombatProjection
                {
                    CombatVersion = 5, LifeId = 2, Hp = 300f, MaxHp = 720f,
                    Dead = false, IsDefending = true
                }
            }, 1);

            var view = _registry.GetView(8);
            Assert.IsNotNull(view);
            Assert.AreEqual(5u, view!.CombatVersion);
            Assert.IsTrue(view.IsDefending);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode CombatStateProjectionTests`

预期：编译失败，`ApplyCombatStateUpdate`、`Equipment` 不存在。

- [ ] **步骤 3：实现客户端投影合并**

在 `RemoteEntityView` 增加（`LifeId` 已在任务 4 加入，不重复）：

```csharp
        public uint CombatVersion { get; set; }
        public EquipmentSummary? Equipment { get; set; }
        public ControlSummary? Control { get; set; }
        public System.Collections.Generic.IList<StatusProjection> Statuses { get; set; }
            = new System.Collections.Generic.List<StatusProjection>();
```

在 `RemoteEntityRegistry` 增加：

```csharp
        private readonly List<(long EntityId, CombatProjection Projection)> _pendingCombat =
            new List<(long, CombatProjection)>();

        public void ApplyCombatStateUpdate(CombatStateUpdate update)
        {
            if (update?.Projection == null) return;
            if (!_views.TryGetValue(update.EntityId, out var view))
            {
                if (_pendingCombat.Count >= 64) _pendingCombat.RemoveAt(0);
                _pendingCombat.Add((update.EntityId, update.Projection));
                return;
            }
            ApplyProjection(update.EntityId, view, update.Projection);
        }

        private void ApplyProjection(long entityId, RemoteEntityView view, CombatProjection projection)
        {
            if (projection.LifeId != 0 && view.LifeId == 0) view.LifeId = projection.LifeId;
            if (projection.LifeId != 0 && view.LifeId != 0 && projection.LifeId < view.LifeId) return;
            if (projection.LifeId > view.LifeId)
            {
                if (_pendingCombat.Count >= 64) _pendingCombat.RemoveAt(0);
                _pendingCombat.Add((entityId, projection));
                return;
            }
            if (projection.CombatVersion < view.CombatVersion) return;

            view.CombatVersion = projection.CombatVersion;
            view.Hp = projection.Hp;
            view.MaxHp = projection.MaxHp;
            view.Dead = projection.Dead;
            view.IsDefending = projection.IsDefending;
            view.Equipment = projection.Equipment;
            view.Control = projection.Control;
            view.Statuses = projection.Statuses;
            view.Targetable.SetVitals(view.Hp, view.MaxHp, view.Dead);
            view.Animator?.ApplySnapshotState(view.Dead);
        }
```

在 `ApplySnapshot` 的 `Health` 分支补 `if (snapshot.CombatVersion != 0) view.CombatVersion = snapshot.CombatVersion;`（血量变化与投影共用同一版本）。

在 `ApplySnapshot` 的 `if (created)` 之前：

```csharp
            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Combat) != 0
                && snapshot.Combat != null)
                ApplyProjection(snapshot.PlayerId, view, snapshot.Combat);
            for (int i = _pendingCombat.Count - 1; i >= 0; i--)
            {
                if (_pendingCombat[i].EntityId != snapshot.PlayerId) continue;
                var projection = _pendingCombat[i].Projection;
                _pendingCombat.RemoveAt(i);
                ApplyProjection(snapshot.PlayerId, view, projection);
            }
```

在 `CreateView` 末尾初始化（`LifeId` 已由任务 4 的 `ApplySnapshot` 写入）：

```csharp
            view.CombatVersion = snapshot.CombatVersion;
```

在 `CombatNetworkBridge` 增加 `OnCombatStateUpdate` 事件与路由（同任务 4 的 `OnNpcAction` 模式）。在 `ClientNetworkHost` 订阅并转发：

```csharp
        private void HandleCombatStateUpdate(CombatStateUpdate update)
        {
            _remoteEntities?.ApplyCombatStateUpdate(update);
            if (update.Projection.ActiveCast != null)
                _remoteCastPresenter?.OnActiveCast(update.EntityId, update.Projection.ActiveCast);
        }
```

在 `ClientNetworkManager` 增加：

```csharp
        public uint ServerTick { get; private set; }
```

在 `Update` 消费快照时：

```csharp
                ServerTick = snapshot.Tick;
```

在 `RemoteCastPresenter.cs` 顶部补 `using Cool.SyncCore;`（`CombatRules` 所在命名空间）。

在 `RemoteCastPresenter` 构造函数增加可选参数：

```csharp
            Func<uint>? serverTickProvider = null)
```

并保存：

```csharp
        private readonly Func<uint>? _serverTickProvider;
        ...
        _serverTickProvider = serverTickProvider;
```

增加：

```csharp
        public void OnActiveCast(long entityId, ActiveCastProjection activeCast)
        {
            if (_registry == null || _catalog == null || activeCast == null) return;
            if (entityId == _localPlayerId) return;
            var view = _registry.GetView(entityId);
            if (view == null || view.Kind != (byte)EntityKind.Player) return;
            if (!_catalog.TryGet(activeCast.SkillId, out var data)) return;

            float remaining = DefaultAttackDuration;
            if (_serverTickProvider != null && activeCast.EndTick > _serverTickProvider())
                remaining = (activeCast.EndTick - _serverTickProvider())
                            / (float)CombatRules.TickRateHz;

            float duration = (data is ChargedSkillData ? data.ReleaseClip : data.CastClip)?.length
                             ?? DefaultAttackDuration;
            view.Animator?.PlayAttack(data.AnimatorTrigger,
                Mathf.Min(duration, Mathf.Max(0.1f, remaining)), 1f, activeCast.CastId);
        }
```

在 `ClientNetworkHost` 构造 `RemoteCastPresenter` 时补：

```csharp
                serverTickProvider: () => _manager?.ServerTick ?? 0);
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode CombatStateProjectionTests
bash tools/unity-tests.sh EditMode RemoteEntityRegistryTests
```

预期：全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs Assets/Scripts/Tests/Editor/SyncLayer/CombatStateProjectionTests.cs
git commit -m "feat(client): merge combat projection by version"
```

---

## 任务 9：晚进观察者在途剑气与带宽门禁

**文件：**
- 修改：`Server/Handler/CastHandler.cs`
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Server/Server.Tests/Aoi/BandwidthBudgetTests.cs`
- 测试：`Server/Server.Tests/Handler/CastResendTests.cs`（新建）

- [ ] **步骤 1：编写失败的补发测试**

创建 `Server/Server.Tests/Handler/CastResendTests.cs`：

```csharp
using System.Linq;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CastResendTests
{
    [Fact]
    public void Active_Projectile_Is_Retained_For_A_Late_Observer()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.IsLoggedIn = true;
        caster.ZoneId = "room-a";
        var resolver = new CombatResolver(store, new System.Random(1));

        var cast = resolver.Decide(1, new CastRequest
        {
            SkillId = 10002,
            Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f },
            ClientCastSeq = 1
        }, 10);
        Assert.True(cast.Accepted);

        var resolutions = resolver.DrainResolved(100);
        Assert.Contains(resolutions, r => r.Launch != null);

        var launches = resolver.ActiveLaunchesForTest(1).ToList();
        Assert.Single(launches);
        Assert.Equal(cast.CastId, launches[0].CastId);
    }

    [Fact]
    public void Cancel_After_Launch_Keeps_Active_Projectile()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.IsLoggedIn = true;
        var resolver = new CombatResolver(store, new System.Random(1));
        var cast = resolver.Decide(1, new CastRequest
        {
            SkillId = 10002,
            Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f },
            ClientCastSeq = 1
        }, 10);
        resolver.DrainResolved(100);
        Assert.Single(resolver.ActiveLaunchesForTest(1));

        var cancels = resolver.CancelPending(1, 10002, cast.CastId, 120,
            NpcActionCancelReason.Hit);

        Assert.Single(cancels);
        Assert.Single(resolver.ActiveLaunchesForTest(1),
            "a launched projectile must keep flying after the attack is cancelled");
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastResendTests"`

预期：编译失败，`ActiveLaunchesForTest` 不存在。

- [ ] **步骤 3：实现在途剑气补发与带宽测试**

在 `CombatResolver` 增加：

```csharp
        private readonly Dictionary<ulong, ProjectileLaunchBroadcast> _activeLaunches =
            new Dictionary<ulong, ProjectileLaunchBroadcast>();

        public IEnumerable<ProjectileLaunchBroadcast> ActiveLaunchesFor(long casterId)
            => _activeLaunches.Values.Where(l => l.CasterPlayerId == casterId);

        internal List<ProjectileLaunchBroadcast> ActiveLaunchesForTest(long casterId)
            => ActiveLaunchesFor(casterId).ToList();
```

在 `ResolveStage` 生成 `resolution.Launch` 后加入 `_activeLaunches[resolution.Launch.CastId] = resolution.Launch;`；生成 `resolution.Stop` 后 `_activeLaunches.Remove(resolution.Stop.CastId);`。正常飞满射程的清理沿用现有 `_projectileRecipients` 窗口；在 `DrainResolved` 丢弃过期 pending 时移除对应 `_activeLaunches`。再加一个有界清理：

```csharp
        private void PruneActiveLaunches()
        {
            const int maxEntries = 128;
            if (_activeLaunches.Count <= maxEntries) return;
            var oldest = _activeLaunches.Values.OrderBy(l => l.LaunchTick).ToList();
            for (int i = 0; i < _activeLaunches.Count - maxEntries; i++)
                _activeLaunches.Remove(oldest[i].CastId);
        }
```

每次加入后调用 `PruneActiveLaunches()`。

在 `CastHandler` 增加：

```csharp
        public void ResendActiveProjectiles(long observerId, long casterId)
        {
            var observer = _server.GetEntity(observerId);
            foreach (var launch in _resolver.ActiveLaunchesFor(casterId))
            {
                if (observer != null && observer.ZoneId != launch.CombatSceneId) continue;
                var session = _server.GetSession(observerId);
                if (session != null)
                    _ = _server.SendToSessionAsync(session,
                        MessageId.ProjectileLaunchBroadcast, launch);
            }
        }
```

在 `AoiBroadcaster` 增加：

```csharp
        public System.Action<long, long>? OnBaselineEntity { get; set; }
```

在基线分支末尾调用：

```csharp
                        OnBaselineEntity?.Invoke(observer.PlayerId, entity.PlayerId);
```

在 `Server3C` 接线：

```csharp
            _aoi.OnBaselineEntity = (observerId, entityId) =>
                _castHandler.ResendActiveProjectiles(observerId, entityId);
```

更新 `BandwidthBudgetTests`，新增位置保活/血量包/投影基线尺寸断言，并用 100 个实体放进**同一个** `WorldSnapshot` 编码后乘 15Hz 计算每秒字节：

```csharp
    [Fact]
    public void Hundred_Entity_Fifteen_Hertz_Sixty_Seconds_Stays_Under_100_KBps()
    {
        var entities = new List<EntitySnapshot>();
        for (int i = 0; i < 100; i++)
        {
            entities.Add(new EntitySnapshot
            {
                PlayerId = i + 1, X = i, Z = i, Hp = 720f, MaxHp = 720f,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                    | EntityFieldMask.Speed | EntityFieldMask.Dead | EntityFieldMask.Health),
                CombatVersion = 1, LifeId = 1
            });
        }
        var snapshot = new WorldSnapshot
        {
            Tick = 1, ServerTimestamp = 1L, Entities = entities
        };
        int perTick = MessageCodec.EncodeToArray(
            MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable).Length;
        int totalPerSecond = perTick * 15;
        Assert.True(totalPerSecond <= GlobalGameSettings.BandwidthBudgetBytesPerSec,
            $"estimated {totalPerSecond} B/s");
    }

    [Fact]
    public void Projection_Baseline_Stays_Within_Combat_Tick_Budget()
    {
        var baseline = new WorldSnapshot
        {
            Tick = 1, ServerTimestamp = 1L,
            Entities = new List<EntitySnapshot>
            {
                new EntitySnapshot
                {
                    PlayerId = 1,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Speed | EntityFieldMask.Dead
                        | EntityFieldMask.Health | EntityFieldMask.Defending
                        | EntityFieldMask.NpcAction | EntityFieldMask.Combat),
                    X = 1f, Z = 2f, Hp = 720f, MaxHp = 720f,
                    CombatVersion = 1, LifeId = 1,
                    Combat = new CombatProjection
                    {
                        CombatVersion = 1, LifeId = 1, Hp = 720f, MaxHp = 720f,
                        IsDefending = false,
                        Equipment = new EquipmentSummary
                        {
                            WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                        },
                        Control = new ControlSummary { SpeedScale = 1f }
                    }
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, baseline,
            MessageFlags.Reliable);
        Assert.InRange(bytes.Length, 80, 300);
    }
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastResendTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BandwidthBudgetTests"
```

预期：补发测试 PASS；带宽测试 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Aoi/AoiBroadcaster.cs Server/Server3C.cs Server/Server.Tests/Handler/CastResendTests.cs Server/Server.Tests/Aoi/BandwidthBudgetTests.cs
git commit -m "feat(server): resend active projectiles and gate bandwidth"
```

---

## 任务 10：Part B 回归、README 与提交

- [ ] **步骤 1：跑 Part B 全部相关测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionMessagesTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionBuilderTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiBandSchedulerTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiBroadcasterTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastResendTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BandwidthBudgetTests"
bash tools/unity-tests.sh EditMode CombatStateProjectionTests
bash tools/unity-tests.sh EditMode RemoteEntityRegistryTests
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
```

预期：全部 PASS。若有失败，先修失败用例，不得跳过。

- [ ] **步骤 2：更新 README 与路线图**

- `Server/README.md`：Plan 01–02 已完成；列出 Plan 02 已启用项（取消、NpcAction、投影、AOI 基线/带宽）与 Plan 03–05 未启用项。
- `Assets/Scripts/AOT/Protocol/README.md`：补 310/311、`CancelCastRequest.CastId`、`CombatProjection`、`EntityFieldMask.Combat`、`EntitySnapshot.CombatVersion/LifeId`。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`：补取消/NpcAction/投影消费、`ActiveCast` 首版重放限制、版本合并口径。
- `Assets/Scripts/Hotfix/GameSystems/Monster/README.md`：补 `NpcActionEvent` 全链与 AOI 基线。
- `docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md`：Plan 02 状态改为 ✅，链接本计划；明确 03–05 未开始。

- [ ] **步骤 3：Commit**

```bash
git add Server/README.md Assets/Scripts/AOT/Protocol/README.md Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md Assets/Scripts/Hotfix/GameSystems/Monster/README.md docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md
git commit -m "docs: record elemental plan 02 cancel and projection"
```

---

## 完成定义

- Part A：取消广播、NpcAction 事件与 AOI 基线、客户端动作存储/取消表现全部有测试并通过。
- Part B：战斗投影信封、AOI 发送原因与条件字段、服务端基线/更新、客户端版本合并、在途剑气补发与 100 KB/s 门禁全部有测试并通过。
- 未启用字段（`Statuses` 内容、`ControlSummary` 的实际控制、元素 VFX、远端武器模型）在 README 明确标注为 Plan 03/04/05，不写成已打通。
- 每个任务独立 commit；Part A、Part B 各自至少有一次完整回归。

## 执行说明

- 本计划按 Part A → Part B 顺序执行。Part A 完成后先提交并回归，再进入 Part B；不要在同一未验证状态上叠加投影改造。
- 服务端测试若 testhost 抛 `Win32Exception(5)`，使用 `tools/test-server-inprocess.sh`。
- Unity 测试每个类单独跑；batchmode 不能带 `-quit`。
- 若任务 3/7 的 AOI 发送原因改造让既有 `AoiBroadcasterTests`/`AoiBandSchedulerTests` 变红，先更新测试夹具到新契约（Baseline/Changed/Keepalive），再继续。
- 本计划不新增 PlayMode 夹具；三客户端验收与 PlayMode 取消冒烟属于 Plan 05。
