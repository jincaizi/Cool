# Cool.Protocol —— 双端共享协议层（唯一真相源）

Unity 客户端（asmdef `Cool.Protocol`）与服务器（`Server/Server3C.csproj` 的
`Compile Include ..\Assets\Scripts\AOT\Protocol\**\*.cs`）编译**同一批源文件**。

## 目录纪律

- 本目录**禁止**出现 `using UnityEngine` 及任何引擎类型（服务器编不了；
  asmdef `noEngineReferences: true` 已在编译期强制）。
- 字段类型仅限基元类型 / `string` / `byte[]` / 本目录枚举 / 本目录消息类。
- 源码须兼容 **C# 9**（Unity 2022 编译目标）——禁止 file-scoped namespace 与 C# 10+ 语法。
- protobuf 特性 `[ProtoContract]/[ProtoMember]` 沿用现状。

## 协议治理规则（spec §3 唯一口径）

| 规则 | 内容 |
|---|---|
| 分层冻结 | 规则层 v1 已冻结：header 格式、版本号、seq/时间戳约定、增量编码方式（P1 引入位掩码） |
| 只加不删 | 新消息/新字段永远非破坏性追加；废弃字段置零不删 tag；`MessageId` 数值发布后不可变 |
| 版本握手 | `LoginRequest.ProtocolVersion` ≠ `ProtocolConstants.ProtocolVersion` 时拒绝登录并 Kick（已在 LoginHandler 实现） |
| 时间戳语义 | 所有双向时间戳 = 发送时刻换算成的**服务器时钟**；禁直接填客户端墙钟；唯一例外：时钟同步请求/响应对（墙钟用于测偏移，见 spec D3） |
| 序列化对齐 | 两端 protobuf-net 版本锁定 **3.2.56**（Unity: `Assets/Packages/protobuf-net.3.2.56`；Server: NuGet 同版本号） |

## 协议头格式（规则层 v1）

```
偏移  长度  内容
0     1     flags   （MessageFlags 位图）
1     2     messageId（MessageId 枚举，ushort 小端）
3     2     payloadLength（ushort 小端）
5     n     protobuf 序列化负载
```

## 追加字段记录（只加不删，`ProtocolVersion` 保持 1）

多人战斗一致性（任务 2–12）在既有消息上追加了以下字段，全部是 protobuf 高位 tag /
位掩码高位，老客户端/老服务端缺字段时读默认零，因此**未升协议版本**：

| 消息 | 追加内容 | tag / 位 | 语义 |
|---|---|---|---|
| `EntitySnapshot` | `Hp` / `MaxHp` | tag 13 / 14 | 服务端权威当前/最大血量（负数合法——`ApplyDamage` 不钳制） |
| `EntitySnapshot` | `IsDefending` | tag 15 | 服务端权威防御姿态 |
| `EntityFieldMask` | `Health` | `1 << 7` | Hp/MaxHp 有效（**条件字段**：仅 Baseline / 血量翻转置位） |
| `EntityFieldMask` | `Defending` | `1 << 8` | IsDefending 有效（**每次快照都置位**，D-N2） |

> 当前服务端快照 **Dead/Defending 每帧携带**（存活/姿态是状态不是事件，D-N2），只有
> `Health` 是条件字段。快照消费方仍须**按掩码位**取值：`Health` 未置位时沿用 last-known，
> 不能用 protobuf 默认值覆盖（beacon 只带位置位时的 `Rotation=0` 就是同一类陷阱，见
> `RemoteEntityRegistry.ApplySnapshot`）。

| 消息 | 追加内容 | tag | 语义 |
|---|---|---|---|
| `CastBroadcast` | `ImpactX` / `ImpactY` / `ImpactZ` | 10 / 11 / 12 | 服务端裁决的落点（火球爆炸、陨石落点） |
| `CastBroadcast` | `ChargeProgress` | 13 | 蓄力进度 [0,1]（20004 半径/伤害缩放、20003 表现） |

### 炎爆击退

登录能力位 `ProtocolFeatures.AuthoritativeKnockback = 1u << 3`。服务器每次击退更新该玩家的
移动代次，`PositionSyncResponse` 追加 tag 9 `MovementRevision`，客户端在
`PositionSyncRequest` tag 10 回传。首个代次为 0，复活时双方复位。旧客户端仍接收伤害。

### 取消施法（2026-11-10）

`MessageId.CancelCastRequest = 304`（客户端 → 服务器）：tag1 `SkillId`，tag2 可选 `CastId`。
携带 `CastId` 时只取消该实例；为 0 时取消该技能最新实例（旧客户端语义）。服务端丢弃对应的
未到期判定段（旋转 20002 取消/换命）；已发射的投射物继续飞行，`CastCancelBroadcast` 只停止
攻击表现；无匹配实例幂等忽略。`ProtocolVersion` 保持 1。

### 属性剑气 v1（2026-11-14）

状态：Plan 01–05 已完成（Part A 取消/NpcAction；Part B 战斗投影/AOI 发送原因/带宽；
Plan 03 位移/跳跃预算与 PvP 控制基线；Plan 04 状态结算/DoT/元素表现；Plan 05 完成
AOI 反查/三客户端/带宽/注入验收）。验收证据见路线图与 `Server/README.md`。
`Statuses[]`、实际控制来源（状态运行时）与 `DamageKind=Dot`
均已在 Plan 04 启用；Plan 05 未改动协议，`ProtocolVersion` 保持 1，仅消费既有字段。

能力位 `ProtocolFeatures.ElementalSwordQiV1 = 1u << 4`（合并 mainline 剑气回荡的
`SwordQiEcho = 1u << 2` 后从 bit2 顺延；`PvpMovementBudgetV1` 仍为 `1u << 3`）。登录请求 tag9
`ElementalCatalogFingerprint`（共享目录 SHA-256 小写十六进制）；登录响应 tag14 同指纹、
tag15 `WeaponInventoryBaseline`（`LifeId/EquipmentRevision/当前装备/IList<WeaponInstanceSummary>`）。
指纹不匹配时服务器拒绝登录。`RespawnNotice` tag7 追加 `LifeId`，客户端复活后继续用新生命代次施法。

| 消息 | ID | 方向 | 关键 tag |
|---|---|---|---|
> 合并说明（2026-11-20）：mainline 剑气回荡已占用 `SwordQiEchoState = 305`，属性剑气 7 条消息
> 顺延到 312–318；`CastRequest` 的 8/9 被回声占用，属性剑气字段顺延到 10–12。

| 消息 | ID | 方向 | 关键 tag |
|---|---|---|---|
| `EquipWeaponRequest` | 312 | C→S | 1 `ClientEquipSeq`、2 `WeaponInstanceId`、3 `KnownEquipmentRevision` |
| `EquipWeaponConfirm` | 313 | S→C | 1 seq、2 `Accepted`、3 `RejectReason`、4 版本、5 实例、6 模板、7 元素；拒绝也带当前摘要 |
| `CastConfirm` | 314 | S→C 本人 | 1 `ClientCastSeq`、2 `Accepted`、3 原因、4 `CastId`、5 技能、6 `CasterLifeId`、7 装备版本、8 攻速、9 `AcceptedTick`、10–12 装备摘要 |
| `ProjectileLaunchBroadcast` | 315 | S→C | 1 `CastId`、2 施法者、3 生命代次、4 场景、5 `LaunchTick`、6–8 原点、9 `Yaw`、10 速度、11 射程、12 元素、13 技能、14 服务器时间戳 |
| `ProjectileStopBroadcast` | 316 | S→C | 1 `CastId`、2 施法者、3 生命代次、4 场景、5 `StopTick`、6 原因、7–8 停点 |
| `CastCancelBroadcast` | 317 | S→C | 1 `CastId`、2 `CasterLifeId`、3 `EffectiveTick`、4 `Reason`（`NpcActionCancelReason`）、5 `CasterPlayerId` |
| `CombatStateUpdate` | 318 | S→C | 1 `EntityId`、2 `CombatProjection`、3 `ServerTimestamp` |

`CastCancelBroadcast` 取消尚未提交的前摇/持续施法（以及 NPC 技能动作）；已发射的独立投射物
（剑气）继续飞行。收件人为施法者本人与视野内观察者，按 `ZoneId` 过滤。

`CombatStateUpdate` 是可靠的完整战斗投影（发本人与同场景观察者）；嵌套类型固定 tag：
`CombatProjection` 1 `CombatVersion`、2 `LifeId`、3/4 `Hp/MaxHp`、5 `Dead`、6 `IsDefending`、
7 `IList<StatusProjection> Statuses`、8 `EquipmentSummary`、9 `ControlSummary`、
10 `ActiveCastProjection`；`StatusProjection` 1–8 `EffectId/GroupId/EndTick/SourceId/SlowRatio/
DamagePerTick/IntervalTicks/IncomingDamageMultiplier`；`EquipmentSummary` 1–3
`WeaponInstanceId/TemplateId/ElementId`；`ControlSummary` 1–3 `SpeedScale/ActionLock/
ControlVersion`；`ActiveCastProjection` 1–4 `CastId/SkillId/Phase/EndTick`。
Plan 04 起：`Statuses` 由服务端状态运行时（`ServerStatusManager`）按组填充——
冰/火/雷/风各占一组，`EffectId/GroupId/EndTick/SourceId` 与按类型参数
（`SlowRatio`、`DamagePerTick/IntervalTicks`、`IncomingDamageMultiplier`）均为权威值；
`Control` 取 `PlayerState.ControlSpeedScale/ControlActionLock/ControlVersion`，
由 `Server3C.ApplyPlayerControl` 在状态变化时写入，冰使 `SpeedScale` 变 0.7、雷使
`ActionLock=true`，`ControlVersion` 随之递增。本人 318 与 AOI 基线走同一份投影。

追加字段（只加不删）：

| 消息 | 追加内容 | tag | 语义 |
|---|---|---|---|
| `CastRequest` | `ClientCastSeq` / `EquipmentRevision` / `CasterLifeId` | 10 / 11 / 12 | 幂等序号、装备版本与生命代次校验（8/9 为剑气回声字段） |
| `CastBroadcast` | `CastId` / `CasterLifeId` / `AttackSpeedMultiplier` / `ElementId` / `EquipmentRevision` | 15–19 | 权威施法标识与冻结的施法快照 |
| `HitEntry` | `CastId` / `EventSeq` / `EventId` / `DamageKind` / `TargetLifeId` / `CombatVersion` | 8–13 | 命中归属与战斗投影；Plan 04 起逐事件填充 `EventSeq/EventId/CombatVersion`，`DamageKind=Dot` 用于灼烧跳 |
| `CancelCastRequest` | `CastId` | 2 | 非 0 只取消该次施法；0 = 取消该技能最新实例（兼容旧客户端） |
| `NpcActionEvent` | `LifeId` | 4 | 怪物生命代次；与 `EntityId/InstanceId/Revision` 组成客户端合并键 |
| `EntitySnapshot` | `LifeId` | 19 | 实体生命代次（NpcAction 合并键；随基线/Health 快照携带） |
| `EntitySnapshot` | `CombatVersion` / `Combat` | 18 / 20 | 战斗版本戳与完整投影（`Combat` 仅 AOI Baseline 置位；Health 位只带版本） |
| `EntityFieldMask` | `Combat` | `1 << 10` | 快照带完整 `CombatProjection`（仅 AOI Baseline 置位；增量走 `CombatStateUpdate`；Dead/Defending 不依赖本位，每快照携带） |

客户端消费 NpcAction 时以 `(EntityId, LifeId, InstanceId, Revision)` 为合并键：实例号只增，
同实例只接受更大的 `Revision`；生命代次不匹配的事件丢弃。取消事件与 `CastCancelBroadcast`
同义——只停攻击表现，不回收已发射的剑气。

`DamageKind`：`Direct=0`、`Dot=1`（Plan 04 已启用）。`HitEntry` 的
`EventSeq` 按目标单调、`EventId` 为服务端世界级去重序号、`CombatVersion` 为结算后版本，
三者均由 Plan 04 的伤害/状态提交填充；灼烧跳与直接命中走同一条 `DamageResult` 通道，
客户端按 `DamageKind` 选择表现（Dot 只对齐权威血量与飘字）。
所有新消息已在 `MessageTypeRegistry` 注册；
新增字段沿用 `IList<T>` 规则（`WeaponInventoryBaseline.Weapons` 不是 `List<T>`）。

`PositionSyncRequest` 追加（Plan 03，全部高位 tag）：
tag10 `LocomotionMode`、tag11 `JumpPhase`、tag12 `JumpSeq`、tag13 `VerticalVelocity`、
tag14 `DisplacementSkillId`、tag15 `CastId`、tag16 `ClientControlVersion`、tag17 `LifeId`；
`PositionSyncResponse` tag9 `ControlVersion`。能力位
`ProtocolFeatures.PvpMovementBudgetV1 = 1u << 3`。

**Plan 03 已启用**：携带该能力位的会话由服务端按接收 tick 发位移预算，上述字段实际参与
校验（`LocomotionMode` 决定 Walk 5 / Sprint 8；`JumpPhase/JumpSeq` 只在每个新序号发一次
`Start`；`DisplacementSkillId/CastId` 须匹配服务端突进窗口；`ClientControlVersion` 低于当前
权威版本整包纠偏；`LifeId` 非 0 且不匹配整包纠偏）。未携带该位的旧客户端继续走旧
`MovementValidator` 路径（不按接收 tick 收紧）；新字段缺省为零：`None/Walk`、`JumpSeq=0`、
`DisplacementSkillId=0`、`CastId=0`、`LifeId=0`（不校验生命代次）、`ClientControlVersion=0`。

### 剑气回荡（2026-11-18）

`MessageId.SwordQiEchoState = 305`（服务器 → 施法者/目标/观察者）；能力位
`ProtocolFeatures.SwordQiEcho = 1u << 2`，登录请求 tag7 `SupportedFeatures` / 响应 tag12
`EnabledFeatures` 协商。旧客户端不收 305，旧服务端不置该位时客户端禁用联网入口。

| 消息 | 追加内容 | tag | 语义 |
|---|---|---|---|
| `CastRequest` | `EchoRequestId` / `EchoBindingId` | 8 / 9（`ulong`） | 会话内递增请求号 / 角色绑定代次；0 = 旧请求 |
| `DamageResult` | `EchoCastId` | 9（`ulong`） | 同一次剑气回荡实例；0 = 其他技能 |
| `SwordQiEchoState` | `Phase` / `CasterId` / `CastId` / `RequestId` / `EchoBindingId` / `RejectReason` | 1–6 | Rejected=1 / Started=2 / Ended=3；结束优先于同实例重放开始 |
| `SwordQiEchoState` | `StartTick` / `EndTick` / `ServerTimestamp` / `CooldownTicks` | 7–10 | 服务器权威时间轴（15Hz）与剩余冷却 |
| `SwordQiEchoState` | `OriginX/Y/Z` / `Rotation` / `Radius` / `SwordCount` / `OvershootMeters` | 11–17 | 固定圆心与表现参数 |
| `SwordQiEchoState` | `Contacts`（`IList<SwordQiEchoContactMessage>`） | 18 | 目标 ID / 剑气编号 / 相对 tick / 初始 XYZ |

`SwordQiEchoState` 的接触列表必须声明为 `IList<T>`（IL2CPP tuple 探测约束）。
开始/结束按 `(连接代次, CastId)` 去重；伤害按 `(连接代次, EchoCastId, TargetPlayerId)` 去重。
视觉结束后保留有界接收窗口（首版 2s / 64 个 `EchoCastId`）。

二次释放由能力位 `SwordQiEchoRecast = 1u << 4` 协商。客户端用 `CastRequest.EchoAction`（tag 10）
区分 Prepare 和 Release，`EchoPreparedCastId`（tag 11）引用待发实例。
`SwordQiEchoState.Phase` 追加 Prepared、Expired 和 Completed（4、5、6），
tag 19 `OrbitStartedTimestamp` 在整个实例中保持相同。新观察者的首帧 `EntitySnapshot`
可携带 tag 18 `SwordQiEcho`，在创建实体视图后从当前进度播放。

## 双端一致性校验（人工/CI 各跑一次）

### 权威复活

`LoginRequest.SupportedFeatures` 的 bit 1 表示客户端支持权威复活落点。
`RespawnNotice` 新增 tag 3–6（落点标志和 XYZ），标志为真时用已有的 `Tick` 标记本次复活。
`PositionSyncRequest` tag 9 和 `PositionSyncResponse` tag 8 回传该 tick。
缺省值与 tick 0 可区分。任一端为旧版本时保留旧的复活通知和位置同步行为。

### NPC ActionV1 追加合同（2026-11-08）

`ProtocolVersion` 仍为 1；能力位 `ProtocolFeatures.NpcActionTimelineV1 = 1u`。
登录请求 tag7/8 为 `SupportedFeatures/NpcContentHash`，响应 tag12/13 为
`EnabledFeatures/NpcContentHash`。仅协商成功的 ActionV1 世界消费以下字段：

- `EntitySnapshot` tag16/17：`NpcAction/NpcActionHistory`，同受 `NpcAction = 1 << 9` 掩码控制；缺位保留已有值。
- `WorldSnapshot` tag4 为 `NpcExits`，tag5/6/7 为 `SnapshotSequence/PartIndex/PartCount`，tag8 为 `TickAnchorServerTimestamp`。
- `TickAnchorServerTimestamp` 是所有观察者/分片共用的模拟状态采样时刻；旧 `ServerTimestamp` 继续表示发送时刻。
- `CastBroadcast` tag14 为 `NpcAction`；`MessageId.NpcActionEvent = 2004` 已在 codec registry 注册，tag1/2/3 为 `EntityId/Action/ServerTimestamp`，tag4 为 `LifeId`（Plan 02 Part A 追加，参与客户端合并键）。

`NpcActionSnapshot` 固定 tag1–20，Kind/Phase/CancelReason 是 byte 底层具名枚举；
`Clone()` 复制全部标量，供状态冻结使用。tick 0 合法，不作缺省哨兵。所有协议序列均声明
`IList<T>`，避免 IL2CPP tuple 探测问题。老消息缺追加字段时得到零、null 或空序列。
服务器测试 `NpcActionProtocolTests` 验证注册、20字段回环、序列形状与 legacy 缺省；
它不代替 IL2CPP 真机构建解码门禁。

```bash
# 服务器侧测试（xUnit）：codec 回环 + registry 完备性 + 守卫（21 项）
# 官方 testhost 在本机 Win32Exception(5) 时用 in-process runner（见 Server/README.md）
dotnet test Server/Server.Tests/Server.Tests.csproj
bash tools/test-server-inprocess.sh

# Unity 侧（NUnit EditMode）：同样断言跑于编辑器环境
#   via unity-mcp tests-run(EditMode, class=ProtocolRoundtripTests)

# protobuf 版本核对
ls Assets/Packages | grep protobuf
grep 'protobuf-net' Server/Server3C.csproj
```

## 快速联调（echo 冒烟）

```bash
# 终端 1：启动服务器（默认参数即 8000）
# 先 dotnet build；用 dotnet <dll> 而不是 dotnet run —— 本机 apphost 会让
# HttpListener.Start 抛「句柄无效」。
dotnet build Server/Server3C.csproj
dotnet Server/bin/Debug/net8.0/Server3C.dll 8000
# 终端 2（Unity 内）：unity-mcp tests-run(PlayMode, class=EchoSmokeTests)
```

## D2 消息模式确认记录（P0 MUST-DO-2 关闭项，2026-08-28）

**KCP 传输保持消息边界**：本协议层依赖"每帧 = 一条完整消息"（Decode 单帧契约）。
实证：P1 双客户端验收（TwoClientWorldTests 两连绿 + 服务器日志）在 Kcp 2.7.0 上
运行消息边界保持成立——KCP 分段发送/重组不破坏单帧语义，Decode 未遇跨帧。

- 服务器单消息上限：wire 格式硬限 `ushort.MaxValue`（65535B，MessageCodec.Encode 守卫）；
  设计上限另有 KCP `wndSize × MTU`（32×1400 = 44800B）约束——超限消息 KCP 分段，
  重组后仍保持边界。P2 如需收紧上限，改动点：`MessageCodec.Encode` 的 payload 守卫。
- 反模式禁令：禁止在 KCP 上叠加第二条不可靠通道（spec D2 反贪心条款）。
