# PvP、服务端复活、远程真角色与倒地表现 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让面试官（手机浏览器）与开发者（PC）在同一个世界里互相能打死对方，死亡与复活在**两端都看得见**，且真人玩家渲染成真角色。

**架构：** 协议**一次性**扩展（实体 `Dead` 状态 + 玩家 `ActionState` + 真人/bot 判别 + 点对点复活通知）→ 服务端在**既有主循环 tick** 里做 8 秒死亡计时与复活 → 客户端把服务端的死活接到 `HitFSM` 的 `Death`/`Down`/`GetUp`（补 Animator 入边转移），并按判别决定远程实体渲染成角色还是占位。

**技术栈：** Unity 2022.3.25f1（WebGL + IL2CPP）· protobuf-net 3.2.56 · .NET 8 服务端 · UGUI · 既有 `HitFSM` / `AoiBroadcaster` / `RemoteEntityRegistry` / `GameLoop`

---

## 决策记录（执行时不变更；偏差须记录）

| # | 决策 | 内容 |
|---|---|---|
| **D-N1** | 存活字段命名为 **`Dead`** 而不是 `Alive` | protobuf 的零默认意味着 `false` **不占字节**。命 `Alive` 的话"活着"（true）永远不写、只能靠掩码位推断，读起来是反的；`Dead=false` 默认即"活着"，语义与编码方向一致 |
| **D-N2** | `Dead` **进** `EntityFieldMask`（新位 `1 << 6`），且**每次快照都置位** | 规格 §5 的硬约束：存活是**状态**不是事件——新观察者的**首帧**就要知道。置位方式与既有全字段快照一致 |
| **D-N3** | 玩家动作状态**复用 `NpcAnimationState`**，补 `Down = 7` / `GetUp = 8` | 线上字段是 `ActionState` 那一栏，NPC 侧已用 `NpcAnimationState`。新增第二个枚举会让"同一栏字节按 `Kind` 有两种解释"——更糟 |
| **D-N4** | 复活用**新的点对点消息** `RespawnNotice = 303`（战斗段 300–399） | 规格 F11：本地玩家不在自己的快照里，没有这条消息**被复活者本人**永远停在死亡画面 |
| **D-N5** | 复活后**由客户端自己传送回出生点**，服务端只改存活与血量 | 玩家位置本来就由客户端经 `PositionSyncRequest` 上报，服务端不拥有它。服务端摆位置只会在下一次上报时被覆盖 |
| **D-N6** | 远程动画状态**从快照的 `ActionState` 直接映射**，不做插值 | 它是离散状态；插值没有意义。位置仍走既有插值路径 |
| **D-N7** | 不做击退与浮空（`Knockback`/`Launched`） | 人类裁定：状态不存在、无专门素材、要空中物理 |
| **D-N8** | **不改 PvP 裁决规则** | `CastHandler` 的候选集已是"除施法者外全部实体"，注释明写 bot 不排除。改一个已经在工作的东西只会引入风险 |

**回归基线（每任务后必须保持）**：EditMode **250/250** · Server.Tests **141/141** · PlayMode（三配置并集）**19/19** · WebGL 出包成功

> **数字来源**：Task 13 之后 EditMode 是 250；PlayMode 的 19 条是 T6 之后的口径（并集 = **每个用例至少在一次配置里通过**）。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs` | 修改 | `EntityKind` 加 `PlayerBot`；`EntityFieldMask` 加 `Dead`；`EntitySnapshot` 加 `Dead` |
| `Assets/Scripts/AOT/Protocol/Messages/Npc.cs` | 修改 | `NpcAnimationState` 补 `Down` / `GetUp` |
| `Assets/Scripts/AOT/Protocol/MessageId.cs` | 修改 | 加 `RespawnNotice = 303` |
| `Assets/Scripts/AOT/Protocol/Messages/Respawn.cs` | 创建 | `RespawnNotice` 消息体 |
| `Server/PlayerState.cs`（实际路径见 Step） | 修改 | `DeadTick` + `Respawn()` |
| `Server/Server3C.cs` | 修改 | 主循环 tick 里接死亡计时与复活；对自己发 `RespawnNotice` |
| `Server/Aoi/AoiBroadcaster.cs` | 修改 | 快照带 `Dead` |
| `Server/Aoi/BotRunner.cs` | 修改 | bot 注册为 `PlayerBot` |
| `Hotfix/.../Sys3C/FSM/HitFSM.cs` | 修改 | `Death`/`Down`/`GetUp` 的**入边**与链式推进 |
| `Hotfix/.../Sys3C/Sys3CEntry.cs` | 修改 | 联网下由服务端状态驱动死/活；复活时传送回出生点 |
| `Hotfix/.../SyncLayer/ClientNetworkManager.cs` | 修改 | 处理 `RespawnNotice` |
| `Hotfix/.../SyncLayer/RemoteEntityRegistry.cs` | 修改 | 按 `Kind` 分支渲染；用 `ActionState` 驱动远程 Animator |
| `Assets/RpgDuo/Animator/Character3C.controller` | 修改（**资产**） | `Hit` 层补 `Death`/`Down`/`GetUp` 的**入边转移** |

---

### Task N1: 协议扩展（判别 + 存活 + 动作状态 + 复活通知）

**Files:**
- 修改：`Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Npc.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageId.cs`
- 创建：`Assets/Scripts/AOT/Protocol/Messages/Respawn.cs`
- 测试：`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs`（追加）与既有协议往返测试

- [ ] **Step 1: 写失败的测试**

在 `ProtocolShapeGuardTests.cs` 追加（该文件已有反射遍历 `Cool.Protocol` 的骨架，沿用它的遍历）：

```csharp
    [Test]
    public void EntityKind_Carries_PlayerBot()
    {
        // bot 与真人在伤害路径上同类，只在渲染上分叉（D-N8 / D-P5-7）
        Assert.AreEqual(2, (byte)Cool.Protocol.EntityKind.PlayerBot,
            "PlayerBot 必须是 2——0/1 是已发布的 wire 契约，不能改");
    }

    [Test]
    public void EntitySnapshot_Carries_Dead_With_A_Mask_Bit()
    {
        var prop = typeof(Cool.Protocol.EntitySnapshot).GetProperty("Dead");
        Assert.IsNotNull(prop, "EntitySnapshot 必须有 Dead");
        Assert.AreEqual(typeof(bool), prop!.PropertyType);
        Assert.IsTrue(System.Enum.IsDefined(typeof(Cool.Protocol.EntityFieldMask), 1 << 6),
            "EntityFieldMask 必须新增 1 << 6（只加不删：新字段追加高位）");
    }

    [Test]
    public void RespawnNotice_Roundtrips()
    {
        var sent = new Cool.Protocol.RespawnNotice { Hp = 100f, Tick = 4242u };
        var bytes = Cool.Protocol.MessageCodec.EncodeToArray(
            Cool.Protocol.MessageId.RespawnNotice, sent);
        Assert.IsTrue(Cool.Protocol.MessageCodec.TryDecode(bytes, out var decoded));
        Assert.AreEqual(Cool.Protocol.MessageId.RespawnNotice, decoded.messageId);
        var got = (Cool.Protocol.RespawnNotice)decoded.message;
        Assert.AreEqual(100f, got.Hp);
        Assert.AreEqual(4242u, got.Tick);
    }

    [Test]
    public void NpcAnimationState_Carries_Down_And_GetUp()
    {
        Assert.AreEqual(7, (byte)Cool.Protocol.NpcAnimationState.Down);
        Assert.AreEqual(8, (byte)Cool.Protocol.NpcAnimationState.GetUp);
    }
```

- [ ] **Step 2: 运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"ProtocolShapeGuardTests"}' --timeout 300000
```
预期：**编译失败**（`PlayerBot` / `Dead` / `RespawnNotice` / `Down` / `GetUp` 都还不存在）。编译失败即 RED，继续。

- [ ] **Step 3: 实现**

`WorldSnapshot.cs` 的 `EntityKind`：

```csharp
    /// <summary>
    /// 实体种类（EntitySnapshot.Kind）——P5 怪物服务端化。
    /// 0=Player（protobuf 默认，旧客户端兼容），1=Npc 怪物，
    /// 2=PlayerBot 压测 bot（走玩家的伤害路径，客户端渲染成占位——D-P5-7）。
    /// **0/1 是已发布的 wire 契约，只加不删。**
    /// </summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Npc = 1,
        PlayerBot = 2
    }
```

`WorldSnapshot.cs` 的 `EntityFieldMask`，在 `TemplateId` 之后追加：

```csharp
        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）——P5 客户端怪物渲染区分</summary>
        TemplateId = 1 << 5,
        /// <summary>
        /// 实体是否已死（D-N1：用 Dead 而非 Alive——protobuf 零默认让"活着"不占字节）。
        /// 每次快照都置位（D-N2）：存活是**状态**不是事件，新观察者首帧就要知道。
        /// </summary>
        Dead = 1 << 6
```

`WorldSnapshot.cs` 的 `EntitySnapshot`，在 `Tick` 之后追加（**`ProtoMember(12)`，不要用 9/10/11**——它们已被占用）：

```csharp
        /// <summary>实体是否已死（服务器权威；仅在 FieldMask 的 Dead 位被置位时有效）</summary>
        [ProtoMember(12)]
        public bool Dead { get; set; }
```

`Npc.cs` 的 `NpcAnimationState`：

```csharp
    /// <summary>
    /// 实体的动作/战斗状态（EntitySnapshot.ActionState）。
    /// **玩家与 NPC 共用**（D-N3）——线上只有 `ActionState` 一栏，若按 Kind 分两种解释
    /// 客户端就得按 Kind 分支解码，更糟。名字保留 "Npc" 是为了不动已发布的源码符号。
    /// </summary>
    public enum NpcAnimationState : byte
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Attack = 3,
        Skill = 4,
        Hit = 5,
        Death = 6,
        /// <summary>倒地（死亡后躺卧）</summary>
        Down = 7,
        /// <summary>起身</summary>
        GetUp = 8
    }
```

`MessageId.cs` 的战斗段，在 `CastBroadcast = 301` 之后：

```csharp
        /// <summary>复活通知（服务器 → **被复活者本人**；D-N4）</summary>
        RespawnNotice = 303,
```

> **303 已核实空闲**（写计划时查过：战斗段已占用的是 `CastRequest = 300`、`CastBroadcast = 301`、`DamageResult = 302`）。执行时**仍请复核一次** `grep -n "= 30[0-9]" Assets/Scripts/AOT/Protocol/MessageId.cs`——若期间有人占用了 303，取下一个空闲值并在报告里记录实际取值。

创建 `Assets/Scripts/AOT/Protocol/Messages/Respawn.cs`：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 复活通知（服务器 → 被复活者**本人**）。
    ///
    /// 为什么需要它：本地玩家**不在自己的 AOI 快照里**
    /// （`ClientNetworkManager` 应用快照时 `if (entity.PlayerId == LocalPlayerId) continue;`），
    /// 所以快照里的 `Dead` 只能让**观察者**知道别人的死活，本人收不到任何通知。
    /// 而客户端在联网态**有意短路**本地复活（`Sys3CEntry.EvaluateLocalDeath` 的注释写明：
    /// 免得"客户端站起来了、服务端还当他死了"）——没有这条消息，被复活者会永远停在死亡画面。
    /// </summary>
    [ProtoContract]
    public class RespawnNotice : IMessage
    {
        /// <summary>复活后的血量（**服务端权威**，客户端直接采信，不自算）</summary>
        [ProtoMember(1)]
        public float Hp { get; set; }

        /// <summary>服务器 tick</summary>
        [ProtoMember(2)]
        public uint Tick { get; set; }
    }
}
```

- [ ] **Step 4: 运行测试验证通过**

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"ProtocolShapeGuardTests"}' --timeout 300000
```
预期：**PASS**。

再跑全量 EditMode：预期 **250 + 本次新增**，0 失败。

> **不要跳过这一条**：`EntitySnapshot` 是三方共用的（客户端、服务端、既有契约测试）。全量 EditMode 变红说明破了既有断言——那说明改错了，**停下来查**，不要改既有测试去迁就。

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/AOT/Protocol Assets/Scripts/Tests/Editor/Protocol
git commit -m "feat(protocol): dead state, player action state, bot kind and a respawn notice

Four additions in one pass, because all four are needed by the same feature and
touching the protocol three separate times means rebuilding and re-verifying
three times.

The liveness field is Dead rather than Alive on purpose: protobuf omits a bool
that equals its default, so Alive would mean the common case costs bytes and
only the mask bit could express it. Dead=false is the ordinary case and costs
nothing.

It joins the field mask and every snapshot sets that bit, because liveness has
to be a state rather than an event - a client that enters an observer's view
already dead must learn it from the very first frame.

PlayerBot is a new EntityKind rather than an id range. The load-test bots
already take damage through the same path as players, so the only thing that
needs telling apart is how they are drawn, and an id range is a magic number
that the next producer of ids would break.

RespawnNotice exists because the local player is skipped when snapshots are
applied, so the snapshot's Dead can only inform observers. The networked client
deliberately does not respawn itself - the comment says this is to avoid
standing up while the server still counts it dead - so without a point-to-point
notice the player who was revived would sit on the death screen forever."
```

---

### Task N2: 服务端复活（8 秒）

**Files:**
- 修改：`Server/PlayerState.cs`（实际路径：`find Server -name PlayerState.cs` → 落盘时为 `Server/PlayerState.cs`）
- 修改：`Server/Server3C.cs`
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 测试：`Server/Server.Tests/`

- [ ] **Step 1: 写失败的测试**

在 `Server/Server.Tests/` 下新建 `Combat/RespawnTests.cs`：

```csharp
using System;
using KcpServer;
using Xunit;

namespace KcpServer.Tests
{
    /// <summary>服务端复活（8 秒；D-2）——客户端不自判死，所以这是联网下唯一的复活路径。</summary>
    public class RespawnTests
    {
        private const uint TickMs = 66;   // 与 GameLoop 的 15Hz 一致

        [Fact]
        public void Dead_Player_Respawns_At_Full_Health()
        {
            var p = new PlayerState(1) { PlayerName = "A" };
            p.ApplyDamage(150f);                       // 可到负值（既有语义）
            Assert.False(p.IsAlive);

            p.MarkDead(1000);
            Assert.False(p.IsAlive);

            // 8 秒 = 8000 / 66 ≈ 122 ticks
            Assert.False(p.TryRespawn(1000 + 121, TickMs));
            Assert.False(p.IsAlive);

            Assert.True(p.TryRespawn(1000 + 122, TickMs));
            Assert.True(p.IsAlive);
            Assert.Equal(p.MaxHp, p.Hp, 3);
        }

        [Fact]
        public void Alive_Player_Never_Respawns()
        {
            var p = new PlayerState(1) { PlayerName = "A" };
            Assert.False(p.TryRespawn(999999, TickMs));
        }

        [Fact]
        public void Respawn_Does_Not_Fire_Twice()
        {
            var p = new PlayerState(1) { PlayerName = "A" };
            p.ApplyDamage(150f);
            p.MarkDead(1000);
            Assert.True(p.TryRespawn(1000 + 122, TickMs));
            Assert.False(p.TryRespawn(1000 + 300, TickMs));   // 活着，不再复活
        }
    }
}
```

> **`PlayerState` 的构造签名**以实际为准（落盘时是 `public long PlayerId { get; }` + 无参构造）。若构造签名不同，按实际改测试，**不改测试意图**。

- [ ] **Step 2: 运行测试验证失败**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q --filter RespawnTests
```
预期：**编译失败**（`MarkDead` / `TryRespawn` 不存在）。

- [ ] **Step 3: 实现 `PlayerState` 的复活**

在 `Hp` / `MaxHp` / `ApplyDamage` 那一组旁边追加：

```csharp
        /// <summary>死亡发生的 tick（0 = 活着）。复活计时从它起算（D-2：8 秒）</summary>
        public long DeadTick { get; private set; }

        /// <summary>标记死亡。**由主循环在判定 `!IsAlive` 后调用一次**（幂等）</summary>
        public void MarkDead(long tick)
        {
            if (DeadTick != 0) return;
            DeadTick = tick;
        }

        /// <summary>
        /// 到点则复活：满血 + 清死亡标记，返回 true。
        /// 调用方（主循环 tick）负责在返回 true 时给本人发 `RespawnNotice`。
        /// **位置不在这里改**——玩家位置由客户端上报（D-N5），服务端摆位置会被下一次上报覆盖。
        /// </summary>
        public bool TryRespawn(long nowTick, uint tickMs)
        {
            if (DeadTick == 0) return false;                 // 从未死过 / 已复活
            if (IsAlive) { DeadTick = 0; return false; }     // 兜底：状态不一致时不复活

            long elapsedMs = (nowTick - DeadTick) * tickMs;
            if (elapsedMs < RespawnDelayMs) return false;

            Hp = MaxHp;
            DeadTick = 0;
            return true;
        }

        /// <summary>复活延迟（人类裁定 8 秒；D-2）。**一个常量，演示时可调**</summary>
        public const long RespawnDelayMs = 8_000;
```

> `Hp` / `MaxHp` 现在是 `internal set`——`Hp = MaxHp;` 在同一个程序集内合法。

- [ ] **Step 4: 把它接进主循环 tick**

`Server/Server3C.cs` 的 `new GameLoop<...>` 那个 `tick =>` lambda 里，**在 `_botRunner?.Tick(...)` 之后**追加：

```csharp
                    // P8：死亡计时与复活（D-2，8 秒）。客户端不自判死（F10），
                    // 所以这里是联网下唯一的复活路径。
                    TickRespawns((uint)tick);
```

并新增方法：

```csharp
        /// <summary>
        /// 每 tick 扫一遍实体：新死的打标记，到点的复活并**给本人**发通知。
        /// 复杂度 O(实体数)/tick——与既有的 AOI 广播同量级，不额外引入遍历。
        /// </summary>
        private void TickRespawns(uint tick)
        {
            foreach (var p in _entityStore.All)
            {
                if (!p.IsAlive)
                {
                    p.MarkDead(tick);
                    continue;
                }
                if (!p.TryRespawn(tick, TickIntervalMs)) continue;

                // 只发给**有会话**的实体（bot 没有 session，没人可通知）
                if (!p.IsLoggedIn) continue;
                var session = GetSessionByPlayerId(p.PlayerId);
                if (session == null) continue;
                _ = SendToSessionAsync(session, MessageId.RespawnNotice,
                    new RespawnNotice { Hp = p.Hp, Tick = tick });
            }
        }
```

> **`GetSessionByPlayerId` 可能不存在**——落盘时用既有的 `GetSession(long playerId)`（T2 已抽成 `IServerSession?`）。`TickIntervalMs` 取 `GameLoop` 构造里那个 66 的同一常量；若它现在是个字面量，提成一个 `private const int TickIntervalMs = 66;` 并让两处共用。
>
> **`p.IsLoggedIn` 的成员名以实际为准**（`CastHandler` 里用的是 `p.IsLoggedIn`，落盘时确认）。

- [ ] **Step 5: 快照带上 `Dead`**

`Server/Aoi/AoiBroadcaster.cs` 组装 `EntitySnapshot` 那里：

```csharp
                        FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed
                            | EntityFieldMask.ActionState | EntityFieldMask.Kind | EntityFieldMask.TemplateId
                            | EntityFieldMask.Dead),      // D-N2：每次快照都置位（存活是状态不是事件）
```

并在初始化器里加一行：

```csharp
                        Dead = !entity.IsAlive,
```

- [ ] **Step 6: 运行测试验证通过**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q
```
预期：**141 + 3 = 144/144**，0 失败。

- [ ] **Step 7: Commit**

```bash
git add Server
git commit -m "feat(server): respawn after eight seconds, and tell the player who came back

Until now dying was permanent on the server, and the only way back was to
reconnect - which reads as a crash in a demo where two people are fighting. The
timer lives on PlayerState and is swept once per tick alongside the bot and
monster work that already runs there, so it adds no new traversal.

The snapshot now carries Dead rather than leaving observers to infer it, and
sets that mask bit on every snapshot because liveness is a state: a client that
enters your view while you are down has to see it on the first frame, not on the
frame you happen to die or stand up.

Position is deliberately untouched here. Players report their own position, so
anything the server wrote would be overwritten by the next report - the client
teleports itself instead, and the invalidation of that decision is recorded in
the design."
```

---

### Task N3: bot 走同一条伤害与复活路径

**Files:**
- 修改：`Server/Aoi/BotRunner.cs`
- 测试：`Server/Server.Tests/Aoi/`（追加）

- [ ] **Step 1: 写失败的测试**

在既有 `AoiBroadcasterTests`（或新建 `BotDamageTests.cs`）追加：

```csharp
    [Fact]
    public void Bot_Is_Registered_As_PlayerBot()
    {
        var store = new EntityStore();
        var bot = new BotRunner(store, NewId, seed: 1);
        bot.Spawn(3);
        foreach (var p in store.All)
            Assert.Equal((byte)EntityKind.PlayerBot, p.Kind);
    }

    [Fact]
    public void Bot_Can_Be_Damaged_And_Respawns()
    {
        var store = new EntityStore();
        var bot = new BotRunner(store, NewId, seed: 1);
        bot.Spawn(1);
        var target = store.All.First();

        target.ApplyDamage(9999f);
        Assert.False(target.IsAlive);

        target.MarkDead(1000);
        Assert.True(target.TryRespawn(1000 + 122, 66));
        Assert.True(target.IsAlive);
    }
```

> `NewId` 用具自增的 `Func<long>`；`EntityStore` 的构造与 `All` 以实际为准。

- [ ] **Step 2: 运行测试验证失败**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q --filter "Bot_"
```
预期：**FAIL** —— `Bot_Is_Registered_As_PlayerBot` 断言失败（现在是 `Player = 0`）。

- [ ] **Step 3: 实现**

`BotRunner.Spawn` 里 `AddPlayer` 之后，把 kind 设成 `PlayerBot`：

```csharp
                var player = _store.AddPlayer(id, $"Bot_{id}");
                // 与真人走同一条伤害/复活路径，只在**渲染**上分叉（D-N8 / D-P5-7）
                player.Kind = (byte)EntityKind.PlayerBot;
                player.UpdatePosition(new Vector3(x, 0, z), 0f, 0f, 1L, 0U);
```

> `EntityKind` 在 `Cool.Protocol` 命名空间；`BotRunner` 若尚未 `using Cool.Protocol;`，加上。

- [ ] **Step 4: 运行测试验证通过**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q
```
预期：**144 + 2 = 146/146**。

- [ ] **Step 5: Commit**

```bash
git add Server
git commit -m "feat(server): bots are their own kind, so the client can draw them differently

The damage path already treated them as players - the cast handler's own
comment says the load-test entities are not excluded - so the only thing that
needed a name is how they are rendered. Marking them PlayerBot lets the client
keep drawing them as placeholders while real players get character models,
which is what keeps a hundred skinned meshes off a phone."
```

---

### Task N4: 客户端表现层（`HitFSM` 链式推进 + Animator 入边转移）

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 修改（**资产**）：`Assets/RpgDuo/Animator/Character3C.controller`
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/`（追加）

**本任务的形状（侦察结论，别按直觉走）**：`HitFSM` **已经在设** `HitState` 整型参数（`HitFSM.cs:389` 设的是 `(int)HitState.Down`），`HitState` 枚举也**已含** `Down=5`/`GetUp=6`/`Death=7`。缺的是两件：**FSM 里从 `Down` 推进到 `GetUp`**，与**控制器里那三个状态的入边转移**。

- [ ] **Step 1: 写失败的测试**

在 `Assets/Scripts/Tests/Editor/` 下新建 `Sys3C/DeathSequenceTests.cs`：

```csharp
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 死亡序列的**状态机契约**（不碰 Animator，纯逻辑）。
    /// 存在理由：联网下死亡由服务端驱动，客户端一旦停在 Down 不出来，
    /// 被复活者也永远起不来——而那正是本次要消灭的失败形态。
    /// </summary>
    public class DeathSequenceTests
    {
        [Test]
        public void Death_Advances_To_Down_Then_GetUp_Then_None()
        {
            var fsm = new Cool.GameSystems.Sys3C.HitFSM(animator: null);
            fsm.EnterDeath(new HitData { Damage = 999f });

            Assert.AreEqual(HitState.Death, fsm.CurrentState);
            fsm.ForceAdvanceForTest();                       // 死亡动画结束
            Assert.AreEqual(HitState.Down, fsm.CurrentState);
            fsm.ForceAdvanceForTest();
            Assert.AreEqual(HitState.GetUp, fsm.CurrentState);
            fsm.ForceAdvanceForTest();
            Assert.AreEqual(HitState.None, fsm.CurrentState);
        }

        [Test]
        public void Dead_Entity_Has_Super_Armor_And_Ignores_Later_Hits()
        {
            var fsm = new Cool.GameSystems.Sys3C.HitFSM(animator: null);
            fsm.EnterDeath(new HitData { Damage = 999f });
            fsm.ForceAdvanceForTest();                       // → Down
            fsm.ApplyHit(new HitData { Damage = 50f });
            Assert.AreEqual(HitState.Down, fsm.CurrentState, "倒地期间不应被后续命中断出");
        }
    }
}
```

> `HitFSM` 的实际构造签名、`EnterDeath` / `ApplyHit` 的方法名以落盘代码为准——**沿用既有公开 API**（`ApplyHit`、`OnAnimationEnd`、`HasSuperArmor`、`CurrentState`）。`ForceAdvanceForTest` 若需要新增，就加一个 `internal` 的步进方法，**不要**把它做成公开 API。测试意图（死亡→倒地→起身→归位、且倒地期间不被断出）不得改。

- [ ] **Step 2: 运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode","testClass":"DeathSequenceTests"}' --timeout 300000
```
预期：**FAIL**——卡在 `Death`（没有推进到 `Down` 的路径）。

- [ ] **Step 3: 实现 `HitFSM` 的链式推进**

在 `HitFSM` 里：

1. `Death` 的动画结束回调里，**不要**回到 `None`，而是转 `Down`：

```csharp
            case HitState.Death:
                // 死亡不是终点：打完死亡动画进"躺卧"，再由复活驱动起身（D-2 的 8 秒由服务端计时）
                TransitionTo(HitState.Down);
                break;
```

2. 新增一个由**复活**驱动的入口：

```csharp
        /// <summary>
        /// 复活驱动：从 Down（或 Death）转 GetUp，动画结束后回 None。
        /// **只有服务端说复活了才调用**（D-5/F10：客户端不自行判活）。
        /// </summary>
        public void EnterRespawn()
        {
            if (_currentState != HitState.Down && _currentState != HitState.Death) return;
            TransitionTo(HitState.GetUp);
        }
```

3. `GetUp` 的动画结束回到 `None`（已有一条通往 `None` 的出边则复用它）。

4. **`Down` 期间保持霸体**：`HasSuperArmor` 现在只判 `Death`，扩成两个：

```csharp
        /// <summary>死亡与倒地期间不可被后续命中打断——否则倒地被反复打断，永远起不来</summary>
        public bool HasSuperArmor => _currentState == HitState.Death || _currentState == HitState.Down;
```

- [ ] **Step 4: 补 Animator 控制器的入边转移**（**资产编辑——按 T6 验证过的落盘路径走**）

在 `Character3C.controller` 的 **Hit 层**，给 `Death` / `Down` / `GetUp` 各加一条入边，条件都是 `HitState` 整型参数的相等判断：

| 目标状态 | 触发条件 |
|---|---|
| `Death` | `HitState == 7` |
| `Down` | `HitState == 5` |
| `GetUp` | `HitState == 6` |

**落盘要求（这是 R6，别省）**：
1. 用 `script-execute` 经 `AnimatorController` API 改**编辑器内存**
2. 用 MCP 的资产保存（**不是**脚本里的文件写入）
3. **跑一次 `assets-refresh`，然后重新读回控制器复核**转移确实在
4. 记下复核前后的 md5；**只有刷新后仍一致才算落盘**

> **为什么这么啰嗦**：本项目已经吃过一次"资产改动保存后读回全绿、下一次刷新整体回滚"（T6 记录的坑）。不跑刷新就验证，得到的是**错误的安心感**。

- [ ] **Step 5: `Sys3CEntry` 接上服务端驱动**

`EvaluateLocalDeath` 附近（联网分支）：

- **进死亡**：维持现状（服务端余血归零即死）——但要把 `Death` 序列**接上**：现在只切了状态，没走 `Death → Down`。
- **出死亡**：新增一个由 `RespawnNotice` 调用的公开方法：

```csharp
        /// <summary>
        /// 服务端说复活了（D-N4/D-5）。客户端**不自行判活**——只有收到这条才起身。
        /// 位置由客户端自己摆：服务端不拥有玩家位置（D-N5）。
        /// </summary>
        public void ApplyServerRespawn(float hp)
        {
            _currentHP = hp;
            _isDead = false;
            _fsmManager.Hit.EnterRespawn();
            TeleportToSpawnPoint();      // 复用单机复活用的那个出生点与落位逻辑
        }
```

> `TeleportToSpawnPoint()` 应复用单机 `TickRespawn` 里**已有**的传送代码——把那段抽成一个私有方法，两处共用。**不要**写第二套出生点。

- [ ] **Step 6: 运行测试验证通过**

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}' --timeout 600000
```
预期：**250 + 本次新增**，0 失败。

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix Assets/Scripts/Tests Editor Assets/RpgDuo
git commit -m "feat(3c): death rolls into lying down, and only the server stands you back up

The states and clips were already there - HitFSM was already writing the
HitState integer for Down, and the enum already carried Down, GetUp and Death.
What was missing is the sequence: death used to end at Death rather than moving
into Down, so there was nothing for a respawn to lift you out of, and the
controller had no inbound transitions to reach either state.

Lying down keeps super armour. Without it a hit while down interrupts the state
and you never get up - which is the same failure this feature exists to remove.

Standing up is driven by the server's notice rather than by a local timer,
because the networked client deliberately does not decide death for itself. A
client that stood up on its own would be rejected by every subsequent cast as
caster-dead."
```

---

### Task N5: 远程渲染分支 + 动作状态同步

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（**N4 现场发现的跨批次冲突，原计划漏了**）
- 修改（**资产**）：`Assets/RpgDuo/Animator/Character3C.controller`（对调 Death/Down 的剪辑）
- 测试：`Assets/Scripts/Tests/PlayMode/`（追加）

> **N4 现场发现的冲突（本任务必须一并解决，否则整套白做）**：`ClientNetworkHost` 在**死后 3 秒**把玩家送回选人界面（`:254-262` → `Update:147-154`），而服务端**8 秒**才复活 ⇒ **`RespawnNotice` 到达前客户端已离开世界**。
> 3 秒那条是**上一个批次**（T12 的"重连当复活"）做的——那时服务端确实没有复活，回选人是唯一出路。现在服务端有了，**它变成了障碍**。
> **要求**：死亡后**不要**再自动回选人；改为等待 `RespawnNotice`（N2 的 8 秒）。若你判断"服务器不可达时需要一个兜底"，**兜底必须长于 8 秒**（例如 15 秒），并在报告里说明理由。

> **同任务内的动作状态提醒**：快照的 `Dead` 是**状态**不是边沿事件——已在视野内的观察者最多等 4 tick（≈264ms）的静止保活窗口才看到翻位。**读状态，不要读变化。**

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Scripts/Tests/PlayMode/RemoteRenderBranchTests.cs`：

```csharp
    [UnityTest]
    public IEnumerator Real_Player_Gets_A_Character_Bot_Gets_A_Placeholder()
    {
        // 两个快照走同一条 ApplySnapshot：只有 Kind 不同
        var real = Snap(playerId: 1, kind: (byte)EntityKind.Player);
        var bot  = Snap(playerId: 2, kind: (byte)EntityKind.PlayerBot);

        _registry.ApplySnapshot(real, 0);
        _registry.ApplySnapshot(bot, 0);
        yield return null;

        Assert.IsNotNull(_registry.GetView(1).GameObject.GetComponentInChildren<Animator>(),
            "真人必须拿到带 Animator 的角色");
        Assert.IsNull(_registry.GetView(2).GameObject.GetComponentInChildren<Animator>(),
            "bot 必须是占位——100 个蒙皮网格会压垮手机（D-P5-7）");
    }
```

> 取 `RemoteEntityView` 的访问器名以实际为准（`_views` 目前是私有；若没有读取入口，加一个 `internal RemoteEntityView? GetView(long playerId)`）。

- [ ] **Step 2: 运行测试验证失败**

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"RemoteRenderBranchTests"}' --timeout 900000
```
预期：**FAIL**——两者都拿不到 `Animator`（现在全是 `CreatePrimitive`）。

- [ ] **Step 3: 实现渲染分支**

`RemoteEntityRegistry.CreateView` 改为三分支：

```csharp
            if (snapshot.Kind == (byte)EntityKind.Npc)
            {
                // 怪物仍是球体占位（真实怪物模型属 P3 backlog）
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Monster_{playerId}_T{snapshot.TemplateId}";
                go.transform.SetParent(_root, false);
                return new RemoteEntityView(go, snapshot.Kind, snapshot.TemplateId);
            }

            if (snapshot.Kind == (byte)EntityKind.PlayerBot)
            {
                // 压测 bot：胶囊占位。**绝不能上角色模型**——`--bots 100` 是 P3 的演示前提，
                // 100 个蒙皮网格 + Animator 会在手机浏览器上直接压垮帧率（D-P5-7 / R2）
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = $"Bot_{playerId}";
                capsule.transform.SetParent(_root, false);
                return new RemoteEntityView(capsule, snapshot.Kind);
            }

            // 真人玩家：角色预制体 + Animator（D-P5-7）
            var avatar = Object.Instantiate(_playerAvatarPrefab, _root, false);
            avatar.name = $"RemotePlayer_{playerId}";
            return new RemoteEntityView(avatar, snapshot.Kind);
```

> `_playerAvatarPrefab` 从哪来（序列化字段 / `Resources.Load`）以项目既有做法为准——**优先序列化字段**（本任务要改场景，走 T6 的落盘路径）。
>
> **同时删掉对 `GetComponent` 的顾虑**：项目规则禁止在非 MonoBehaviour 上 `GetComponent`；`RemoteEntityView` 是普通类，所以**动画驱动要放在 MonoBehaviour 一侧**（下面的 `RemoteEntityAnimator`）。

- [ ] **Step 4: 加动画驱动（`ActionState` → 远程 Animator）**

新建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs`（MonoBehaviour，挂在角色实例上）：

```csharp
using Cool.Protocol;
using UnityEngine;

namespace GameSys.SyncLayer
{
    /// <summary>
    /// 把快照里的 `ActionState` 映射到 Animator 的 `HitState` 整型参数（D-N6：离散状态不插值）。
    /// 挂载者由 `RemoteEntityRegistry` 在实例化角色时 `AddComponent`。
    /// </summary>
    public sealed class RemoteEntityAnimator : MonoBehaviour
    {
        private static readonly int HitStateHash = Animator.StringToHash("HitState");
        private static readonly int HitLayerIndex = 1;

        private Animator? _animator;
        private byte _lastState = byte.MaxValue;

        private void Awake() => _animator = GetComponent<Animator>();

        internal void Apply(byte actionState)
        {
            if (_animator == null || actionState == _lastState) return;
            _lastState = actionState;

            // 0（Idle）表示"没有受击/死亡状态"——把 Hit 层权重收回，交还给 Base 层
            bool active = actionState != (byte)NpcAnimationState.Idle;
            _animator.SetLayerWeight(HitLayerIndex, active ? 1f : 0f);
            if (active) _animator.SetInteger(HitStateHash, actionState);
        }
    }
}
```

`RemoteEntityView` 加一个字段与转发方法：

```csharp
        /// <summary>远程动画驱动（只有真人角色有；占位为 null）</summary>
        public RemoteEntityAnimator? Animator { get; }
```

并在 `ApplySnapshot` 里：`view.Animator?.Apply(snapshot.ActionState);`

- [ ] **Step 4b: 接上复活的调用链（`RespawnNotice` → `ApplyServerRespawn`）**

`ClientNetworkManager` 订阅 `RespawnNotice`，收到时调用 `Sys3CEntry.ApplyServerRespawn(notice.Hp)`。
**不加这一步，"躺下就永远起不来"**——N4 的 `ApplyServerRespawn` 目前零调用方。

- [ ] **Step 4c: 解除"死后 3 秒回选人"**（见上方的冲突说明）

- [ ] **Step 4d: 对调 `Death` / `Down` 的剪辑**

控制器里 `Death` 挂的是 `Die01_Stay`（0.667s 循环）、`Down` 挂的是 `Die01`（0.5s）——**两个是反的**。按名字，`Death` 该播倒下、`Down` 该播躺卧。**必须走 N4 Step 4 那条已验证的资产落盘路径**（外部写入 + 导入管线 + 刷新后对 md5）——`script-execute` + 脚本保存那条路被 R6 打回过两次。

- [ ] **Step 5: 运行测试验证通过**

```bash
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"RemoteRenderBranchTests"}' --timeout 900000
```
预期：**PASS**。

再跑三配置 PlayMode 并集：预期 **19 + 本次新增**，**每个用例至少在一次配置里通过**。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix Assets/Scripts/Tests
git commit -m "feat(sync): real players get characters, bots keep their placeholders

The split exists for one reason: 100 bots is how the AOI claim is demonstrated,
and 100 skinned meshes with animators would not survive a phone browser. Bots
and monsters stay primitives.

ActionState drives the remote animator directly, with no interpolation - it is a
discrete state, unlike the position that already interpolates on the same view.
The driver is a MonoBehaviour because the project forbids GetComponent on plain
classes, which RemoteEntityView is."
```

---

### Task N6: 两个浏览器互殴的端到端验收

**Files:** 无（验收；可用 `production/verify/t4-webgl-cdp.js` 等既有装置）
**产出**：`production/verify/n6/` 下的证据 + 报告章节

> **本任务是整个设计里唯一"声称级"的验收。** 它要证明的是**不能靠单客户端证明**的东西：D-5 说死亡由服务端驱动——那必须**两个客户端都看到对方的死亡与复活**才算成立。

- [ ] **Step 1: 出包并起双栈服务器**

```bash
# 出当前 master 的包（走 T9 的构建路径），确认 Console 的 [WebGLBuild] 出包成功
dotnet run --no-build --project Server/Server3C.csproj -- 8000
```
（**不要**带 `--bots 100`：本任务验的是两个真人客户端的互殴，bot 在场只会干扰读数。bot 的那条在 Step 4 单独验。）

- [ ] **Step 2: 开两个独立的浏览器客户端**

用既有装置开**两个** headless Chrome（不同 CDP 端口，例如 9222/9223），各自：
1. 打开同一 URL（带 `?host=127.0.0.1:8000`）
2. 各选一个角色、确认、进世界

- [ ] **Step 3: 驱动互殴并记录**

1. 让 A 走到 B 的攻击范围内（用装置注入的键鼠）
2. A 连续攻击直到 **B 倒下**
3. **两端各截一次图**：A 的屏幕上 B 在倒地；B 的屏幕上自己在死亡画面
4. 等 **8 秒**（`PlayerState.RespawnDelayMs`）
5. **两端各截一次图**：B 站起来了、血条满
6. 记录 `RespawnNotice` 的实际到达时刻与 tick

**判据（缺一不可）**：

| # | 判据 |
|---|---|
| 1 | B 先看到 A 掉血（A 攻击有服务端裁决的伤害） |
| 2 | B 倒下时，**A 的屏幕上** B 是倒地姿态（快照的 `Dead` 生效） |
| 3 | B 倒下时，**B 自己的屏幕上**进了死亡表现（本地余血归零生效） |
| 4 | 约 8 秒后，**两端都**看到 B 站起来、血条满 |
| 5 | 全程 **0** 条 `NotSupportedException` / 解码告警 |

> **任何一条拿不到就如实记"未通过"**，并写下卡在哪。**不要用"控制台没报错"盖住一条没验到的判据。**

- [ ] **Step 4: 单独验 bot 那条**

起 `--bots 100`，重复 Step 3 的 A 侧动作，确认：**A 能打死 bot**、bot 倒地在本端可见、约 8 秒后 bot 复活，且**浮层的远程实体计数不塌**（bot 死后仍在 AOI 集合里，只是 `Dead`）。

- [ ] **Step 5: 写验收记录**

到 `docs/superpowers/plans/2026-10-12-pvp-and-death-presentation.md` 的"执行偏差与验收记录"章节，逐条回填判据与实际读数 + 证据文件路径。

- [ ] **Step 6: 无代码改动则无提交**

本任务只产出验收记录。若发现必修的问题，**另开修复任务**并在报告里写明。

---

## 执行偏差与验收记录（执行时回填）

> 待执行时填写。格式与既有计划一致：逐任务列 commit、偏差（含现场发现与处理）、测试实际通过数、N6 的验收实测结果。

---

## Self-Review（撰写自查）

**1. 规格覆盖度**

| 规格项 | 任务 |
|---|---|
| C1 `EntitySnapshot` 扩展（判别 / 存活 / 动作状态） | N1 |
| C1b `RespawnNotice` | N1 |
| C2 `PlayerState.Respawn()` + 死亡计时 | N2 |
| C3 快照组装带上新字段 | N2 Step 5 |
| C4 bot 的存活与复活 | N3 |
| C5 `HitFSM` 入边转移 | N4 Step 3/4 |
| C6 死亡与复活的客户端驱动 | N4 Step 5 |
| C7 远程渲染分支 | N5 Step 3 |
| C8 远程动作状态同步 | N5 Step 4 |
| §5 错误处理（首帧即死 / 错过复活） | N2 Step 5（每次快照都带 `Dead`）+ N4（`ActionState` 是状态） |
| §8 R1–R6 | R1→N1 Step 4；R2→N5 Step 1 的断言；R3→N4 Step 1 的契约测试；R4→N2 不删 T12 的"重连满血"；R5→N2 Step 5；R6→N4 Step 4 |
| N6 端到端 | N6 |

**2. 占位符扫描**——已清除。两处**刻意留给执行时确认**的都以"若…则以实际为准，**不改测试意图**"的形式写明（`PlayerState` 构造签名、`GetSession` 的成员名），不是 TODO。

**3. 类型一致性**

| 符号 | 定义处 | 使用处 |
|---|---|---|
| `EntityKind.PlayerBot` | N1 Step 3 | N3 Step 3、N5 Step 3 |
| `EntityFieldMask.Dead` | N1 Step 3 | N2 Step 5 |
| `EntitySnapshot.Dead` | N1 Step 3 | N2 Step 5 |
| `NpcAnimationState.Down/GetUp` | N1 Step 3 | N4 Step 3、N5 Step 4 |
| `MessageId.RespawnNotice` | N1 Step 3 | N2 Step 4、N4 Step 5 |
| `PlayerState.MarkDead/TryRespawn/RespawnDelayMs` | N2 Step 3 | N2 Step 4、N3 Step 1 |
| `HitFSM.EnterRespawn` | N4 Step 3 | N4 Step 5 |
| `RemoteEntityAnimator.Apply` | N5 Step 4 | N5 Step 4（`ApplySnapshot` 内调用） |

**4. 已知的落地假设（执行时首次接触须确认）**

| 假设 | 若不符的处理 |
|---|---|
| `RespawnNotice` 取 `303`（写计划时已核实空闲） | 取战斗段下一个空闲值并在报告里记录 |
| `PlayerState` 有无参构造 / `Hp` 是 `internal set` | 按实际改测试与实现，**不改测试意图** |
| `GameLoop` 的 tick 间隔常量是字面量 `66` | 提成一个共用常量，两处引用 |
| `HitFSM` 的公开 API 名（`ApplyHit` / `OnAnimationEnd` / `HasSuperArmor`） | 以实际为准；`ForceAdvanceForTest` 用 `internal`，不得做成公开 API |
| `RemoteEntityView` 没有读取入口 | 加 `internal RemoteEntityView? GetView(long playerId)` |
| 场景里没有现成的"远程玩家角色预制体"字段 | 用**本地玩家**的预制体（`PlayerSpawner` 用的那个），走 T6 的资产落盘路径 |
