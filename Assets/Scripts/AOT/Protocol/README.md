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
| `EntityFieldMask` | `Health` | `1 << 7` | Hp/MaxHp 有效 |
| `EntityFieldMask` | `Defending` | `1 << 8` | IsDefending 有效 |

> 快照消费方必须**按掩码位**取值：未置位的字段沿用 last-known，不能用 protobuf 默认值覆盖
>（beacon 只带位置位时的 `Rotation=0` 就是同一类陷阱，见 `RemoteEntityRegistry.ApplySnapshot`）。

| 消息 | 追加内容 | tag | 语义 |
|---|---|---|---|
| `CastBroadcast` | `ImpactX` / `ImpactY` / `ImpactZ` | 10 / 11 / 12 | 服务端裁决的落点（火球爆炸、陨石落点） |
| `CastBroadcast` | `ChargeProgress` | 13 | 蓄力进度 [0,1]（20004 半径/伤害缩放、20003 表现） |

## 双端一致性校验（人工/CI 各跑一次）

### NPC ActionV1 追加合同（2026-11-08）

`ProtocolVersion` 仍为 1；能力位 `ProtocolFeatures.NpcActionTimelineV1 = 1u`。
登录请求 tag7/8 为 `SupportedFeatures/NpcContentHash`，响应 tag12/13 为
`EnabledFeatures/NpcContentHash`。仅协商成功的 ActionV1 世界消费以下字段：

- `EntitySnapshot` tag16/17：`NpcAction/NpcActionHistory`，同受 `NpcAction = 1 << 9` 掩码控制；缺位保留已有值。
- `WorldSnapshot` tag4 为 `NpcExits`，tag5/6/7 为 `SnapshotSequence/PartIndex/PartCount`，tag8 为 `TickAnchorServerTimestamp`。
- `TickAnchorServerTimestamp` 是所有观察者/分片共用的模拟状态采样时刻；旧 `ServerTimestamp` 继续表示发送时刻。
- `CastBroadcast` tag14 为 `NpcAction`；`MessageId.NpcActionEvent = 2004` 已在 codec registry 注册，tag1/2/3 为 `EntityId/Action/ServerTimestamp`。

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
