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

## 双端一致性校验（人工/CI 各跑一次）

```bash
# 服务器侧测试（xUnit）：codec 回环 + registry 完备性 + 守卫（21 项）
dotnet test Server/Server.Tests/Server.Tests.csproj

# Unity 侧（NUnit EditMode）：同样断言跑于编辑器环境
#   via unity-mcp tests-run(EditMode, class=ProtocolRoundtripTests)

# protobuf 版本核对
ls Assets/Packages | grep protobuf
grep 'protobuf-net' Server/Server3C.csproj
```

## 快速联调（echo 冒烟）

```bash
# 终端 1：启动服务器（默认参数即 8000）
dotnet run --project Server/Server3C.csproj -- 8000
# 终端 2（Unity 内）：unity-mcp tests-run(PlayMode, class=EchoSmokeTests)
```
