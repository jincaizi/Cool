# P0 网络同步工程底座 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 spec《网络同步系统设计》的 P0 阶段交付物：双端共享 Protocol 程序集（唯一真相源）、协议规则层 v1 冻结、Unity↔Server 双端 echo 往返真连通、Hotfix 死件清理。

**Architecture:** 现状是"双份平行协议"——Unity 侧 `Assets/Scripts/AOT/KcpNet/Common/{Message,MessageCodec}.cs`（namespace KcpNet）与服务器侧 `Server/{Messages,IMessage,MessageCodec}.cs`（namespace KcpServer）字段互不兼容（Login 字段都不一致），收编为单份共享源码目录 `Assets/Scripts/AOT/Protocol/`（asmdef `Cool.Protocol`），服务器 csproj 用 `<Compile Include>` 编译同一批文件。传输层零改动：Unity 侧 KcpNet 自研 KCP 实现保留；服务器侧 NuGet Kcp 2.7.0 保留。

**Tech Stack:** Unity 2022.3.25f1 / HybridCLR / protobuf-net 3.2.56（两端同版本）/ .NET SDK 10.0.105（服务器目标 net8.0）/ xUnit + NUnit(Unity EditMode/PlayMode)

**Spec:** `docs/superpowers/specs/2026-08-27-network-sync-design.md` §2 处置清单 + §3 协议治理 + §5-P0

---

## 关键环境事实（先读）

| 事实 | 影响 |
|---|---|
| dotnet SDK 10.0.105 已安装 | 所有 `dotnet build/test` 直接可用 |
| `Server/bin` 已被 git 追踪 | Task 1 需要 `git rm -r --cached` |
| Unity 侧 protobuf-net 3.2.56 位于 `Assets/Packages/protobuf-net.3.2.56/`，Auto Reference 生效 | `Cool.Protocol.asmdef` references 留空即可见 |
| **P0 完成前 wire 格式允许破坏性变更**（spec §3 的"只加不删"自 v1 冻结后生效） | 双端字段号重排无需迁移层 |
| KcpClient.ConnectAsync 内部自带接收循环（`KcpClient.cs:59` 启动 `StartReceiveLoopAsync`），客户端只需订阅 `MessageReceived` 事件 | PlayMode 冒烟不需要手写 ReceiveLoop |
| 双端默认端口不一致：Program 默认 8000，ClientExample 默认 8888 | 本计划全程统一用 **8000** |
| 服务器 `Server/Util/Vector3.cs` 是独立文件 | 收编删除不伤及 |

**协议 v1 合并决策记录**（执行者必读，来源=两端现状并集 + 无兼容负担窗口期）：
- `LoginRequest` 合并：Username(1), PasswordHash(2), ZoneId(3), ClientVersion(4), DeviceInfo(5), ProtocolVersion(6 long)。**丢弃服务器旧版的 X/Y/Z 出生点字段**（tag 冲突历史遗留）；Task 6 同步修改 LoginHandler 出生点逻辑。
- `LoginResponse` 重排：Success(1), Message(2), PlayerId(3), SessionToken(4), ZoneId(5), ServerTimestamp(6)。Unity 旧版 `UserId` 语义并入 `PlayerId`。
- 其余全部消息类照抄权威方现有定义；MessageId 枚举值沿用服务器既有值（1/2/3/100/101/1000-1003/2000-2003），Kick/DisconnectRequest/ShutdownNotification 沿用 Unity 值（4/5/6），Chat 沿用 Unity 值（200/201），新增 EchoRequest=7 / EchoResponse=8。
- 协议头格式维持现存事实标准：`flags(1) + messageId(ushort小端,2) + payloadLength(ushort小端,2)` + protobuf 负载（`ProtocolConstants.HeaderSize = 5`）。

---

### Task 1: Server 工程卫生与 .NET 8 升级

**Files:**
- Modify: `.gitignore`
- Modify: `Server/Server3C.csproj`

- [ ] **Step 1: 把 Server 构建产物移出 git 追踪**

```bash
cd "E:/CodeForJob/Cool"
printf '\n# .NET build artifacts\nServer/bin/\nServer/obj/\n' >> .gitignore
git rm -r --cached Server/bin Server/obj -q
```

Expected output: `rm 'Server/bin/...'` 若干行（静默即代表路径已被忽略，属正常）。

- [ ] **Step 2: 目标框架 net6.0 → net8.0**

编辑 `Server/Server3C.csproj`，把：

```xml
    <TargetFramework>net6.0</TargetFramework>
```

改为：

```xml
    <TargetFramework>net8.0</TargetFramework>
```

- [ ] **Step 3: 构建验证**

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
```

Expected: `Build succeeded.`，0 Warning(s) 允许少量预存警告，**0 Error(s)** 必须成立。

- [ ] **Step 4: Commit**

```bash
git add .gitignore Server/Server3C.csproj
git commit -m "chore(server): ignore build artifacts, retarget net8.0"
```

---

### Task 2: Cool.Protocol 程序集骨架 + 常量（TDD）

**Files:**
- Create: `Assets/Scripts/AOT/Protocol/Cool.Protocol.asmdef`
- Create: `Assets/Scripts/AOT/Protocol/ProtocolConstants.cs`
- Create: `Assets/Scripts/AOT/Protocol/IMessage.cs`
- Create: `Assets/Scripts/Tests/Editor/Protocol/FoundationTests.cs`
- Test: `Server/Server.Tests/`（新建 xUnit 项目）

- [ ] **Step 1: 新建 xUnit 项目并接管共享源编译**

创建 `Server/Server.Tests/Server.Tests.csproj`（完整内容）：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>Cool.Protocol.Tests</RootNamespace>
  </PropertyGroup>

  <!-- 共享源码唯一真相源：编译 Assets 内 Protocol 目录的同批文件 -->
  <ItemGroup>
    <Compile Include="..\..\Assets\Scripts\AOT\Protocol\**\*.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <!-- 版本必须与 Unity 侧 Assets/Packages/protobuf-net.3.2.56 一致（spec §3） -->
    <PackageReference Include="protobuf-net" Version="3.2.56" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 写失败测试**

创建 `Server/Server.Tests/Protocol/FoundationTests.cs`（完整内容）：

```csharp
using Xunit;

namespace Cool.Protocol.Tests;

public class FoundationTests
{
    [Fact]
    public void ProtocolVersion_Is_1()
    {
        Assert.Equal(1, ProtocolConstants.ProtocolVersion);
    }

    [Fact]
    public void ProtoBufVersion_Is_Locked()
    {
        Assert.Equal("3.2.56", ProtocolConstants.ProtoBufVersion);
    }

    [Fact]
    public void HeaderSize_Is_Five()
    {
        Assert.Equal(5, ProtocolConstants.HeaderSize);
    }
}
```

- [ ] **Step 3: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 编译错误 `CS0103: 当前上下文中不存在名称"ProtocolConstants"`（此时 Protocol 目录还是空的）。这就是 Red。

- [ ] **Step 4: 写最小实现**

创建 `Assets/Scripts/AOT/Protocol/ProtocolConstants.cs`（完整内容）：

```csharp
namespace Cool.Protocol
{
    /// <summary>
    /// 协议常量 —— 规则层 v1（spec §3 分层冻结；修改本文件任何常量都必须在
    /// docs/superpowers/specs 的对应章节留痕并升 ProtocolVersion）
    /// </summary>
    public static class ProtocolConstants
    {
        /// <summary>协议版本号（登录握手校验用，v1 冻结起点）</summary>
        public const int ProtocolVersion = 1;

        /// <summary>两端必须使用同一版本的 protobuf-net（spec §3 序列化对齐规则）</summary>
        public const string ProtoBufVersion = "3.2.56";

        /// <summary>协议头长度：flags(1) + messageId(2, ushort 小端) + length(2, ushort 小端)</summary>
        public const int HeaderSize = 5;
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/IMessage.cs`（完整内容）：

```csharp
namespace Cool.Protocol
{
    /// <summary>
    /// 消息基接口 —— 全部线上消息必须实现；禁止向本接口添加成员，
    /// 保持标记接口形态（与两端现有编解码机制耦合）
    /// </summary>
    public interface IMessage
    {
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Cool.Protocol.asmdef`（完整内容）：

```json
{
    "name": "Cool.Protocol",
    "rootNamespace": "Cool.Protocol",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "versionDefines": [],
    "noEngineReferences": false
}
```

> 注意：`noEngineReferences` 故意保持 false —— 需要引用 Auto Referenced 的预编译 dll `protobuf-net.dll`（位于 `Assets/Packages/protobuf-net.3.2.56/lib/netstandard2.1/`）。"纯 C# 零 UnityEngine"由目录纪律保证（见 Task 9 README），不由 asmdef 强制。

- [ ] **Step 5: 运行确认通过**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: 刷新 Unity 并验证编译**

调用 Unity MCP 技能 `assets-refresh`。

Expected: 返回成功且无编译错误（新 asmdef 生成 .meta）。

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/AOT/Protocol Server/Server.Tests .gitignore
git commit -m "feat(protocol): shared assembly skeleton - rule-layer constants v1 frozen"
```

---

### Task 3: 消息协议 v1 全量收编（消息类 + 编解码器，TDD）

**Files:**
- Create: `Assets/Scripts/AOT/Protocol/MessageId.cs`
- Create: `Assets/Scripts/AOT/Protocol/Messages/Login.cs`, `Messages/Echo.cs`, `Messages/SystemMessages.cs`, `Messages/PositionSync.cs`, `Messages/Chat.cs`, `Messages/Room.cs`, `Messages/Npc.cs`
- Create: `Assets/Scripts/AOT/Protocol/MessageCodec.cs`
- Test: `Server/Server.Tests/Protocol/CodecRoundtripTests.cs`, `Server/Server.Tests/Protocol/RegistryCompletenessTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Server/Server.Tests/Protocol/CodecRoundtripTests.cs`（完整内容）：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using ProtoBuf;
using Xunit;

namespace Cool.Protocol.Tests;

public class CodecRoundtripTests
{
    [Fact]
    public void Roundtrip_EchoResponse_PreservesFields()
    {
        var source = new EchoResponse
        {
            ClientTimestamp = 123456789L,
            ServerTimestamp = 987654321L
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.EchoResponse, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var response = Assert.IsType<EchoResponse>(decoded);
        Assert.Equal(source.ClientTimestamp, response.ClientTimestamp);
        Assert.Equal(source.ServerTimestamp, response.ServerTimestamp);
    }

    [Fact]
    public void Roundtrip_LoginRequest_MergedFields()
    {
        var source = new LoginRequest
        {
            Username = "jincaizi",
            PasswordHash = "md5_hash_placeholder",
            ZoneId = "Global",
            ClientVersion = "1.0.0",
            DeviceInfo = "PC",
            ProtocolVersion = ProtocolConstants.ProtocolVersion
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.LoginRequest, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var request = Assert.IsType<LoginRequest>(decoded);
        Assert.Equal(source.Username, request.Username);
        Assert.Equal(source.ZoneId, request.ZoneId);
        Assert.Equal(source.DeviceInfo, request.DeviceInfo);
        Assert.Equal(source.ProtocolVersion, request.ProtocolVersion);
    }

    [Fact]
    public void Roundtrip_PositionSyncRequest_PreservesFields()
    {
        var source = new PositionSyncRequest
        {
            X = 1.5f, Y = 0.25f, Z = -3.75f,
            Rotation = 90f, Speed = 4.2f,
            Timestamp = 555L, Sequence = 7U
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.PositionSyncRequest, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var sync = Assert.IsType<PositionSyncRequest>(decoded);
        Assert.Equal(source.X, sync.X, 4);
        Assert.Equal(source.Y, sync.Y, 4);
        Assert.Equal(source.Z, sync.Z, 4);
        Assert.Equal(source.Sequence, sync.Sequence);
    }

    [Fact]
    public void HeaderLayout_MatchesRuleLayerV1()
    {
        // flags(1) + messageId(2, LE) + length(2, LE) + protobuf payload
        var source = new EchoRequest { ClientTimestamp = 42L };
        var bytes = MessageCodec.EncodeToArray(MessageId.EchoRequest, source, MessageFlags.Reliable);

        Assert.Equal((byte)MessageFlags.Reliable, bytes[0]);
        Assert.Equal((ushort)MessageId.EchoRequest, BitConverter.ToUInt16(bytes, 1));
        int payloadLength = BitConverter.ToUInt16(bytes, 3);
        Assert.Equal(bytes.Length - ProtocolConstants.HeaderSize, payloadLength);
    }

    [Fact]
    public void Decode_UnregisteredId_Throws()
    {
        var bytes = new byte[ProtocolConstants.HeaderSize]; // 全零头 = 无效消息
        Assert.ThrowsAny<Exception>(() => MessageCodec.Decode(bytes));
    }
}
```

创建 `Server/Server.Tests/Protocol/RegistryCompletenessTests.cs`（完整内容）：

```csharp
using System;
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class RegistryCompletenessTests
{
    /// <summary>
    /// 规则层约束：每个 MessageId 枚举成员必须有对应类型注册，
    /// 否则线上收到该消息会被静默丢失
    /// </summary>
    [Fact]
    public void Every_MessageId_HasRegisteredType()
    {
        foreach (MessageId id in Enum.GetValues(typeof(MessageId)))
        {
            Assert.True(
                MessageTypeRegistry.IsRegistered(id),
                $"MessageId.{id} 未在 MessageTypeRegistry 注册");
        }
    }

    [Fact]
    public void EchoPair_Ids_DoNotCollide()
    {
        Assert.NotEqual((ushort)MessageId.EchoRequest, (ushort)MessageId.EchoResponse);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 编译错误 CS0246（找不到 `EchoResponse`、`MessageCodec`、`MessageFlags` 等）。

- [ ] **Step 3: 写消息 ID 枚举**

创建 `Assets/Scripts/AOT/Protocol/MessageId.cs`（完整内容）：

```csharp
namespace Cool.Protocol
{
    /// <summary>
    /// 消息 ID 枚举 —— wire 格式的数值契约，**值一旦发布不可更改**
    /// （spec §3：只加不删）。分段约定：1-99 系统 / 100-199 位置 /
    /// 200-299 聊天 / 1000-1999 远程同步与房间 / 2000-2099 NPC。
    /// </summary>
    public enum MessageId : ushort
    {
        // ========== 系统 (1-99) ==========
        LoginRequest = 1,
        LoginResponse = 2,
        Heartbeat = 3,
        Kick = 4,
        DisconnectRequest = 5,
        ShutdownNotification = 6,

        /// <summary>P0 验收载体：回环往返</summary>
        EchoRequest = 7,
        EchoResponse = 8,

        // ========== 位置同步 (100-199) ==========
        PositionSyncRequest = 100,
        PositionSyncResponse = 101,

        // ========== 聊天 (200-299) ==========
        ChatMessage = 200,
        BroadcastMessage = 201,

        // ========== 远程同步与房间 (1000-1999) ==========
        RemotePlayerSync = 1000,
        PlayerEnterRoom = 1001,
        PlayerLeaveRoom = 1002,
        RoomSync = 1003,

        // ========== NPC (2000-2099) ==========
        NpcSpawn = 2000,
        NpcDespawn = 2001,
        NpcPosSync = 2002,
        NpcAnimSync = 2003
    }
}
```

- [ ] **Step 4: 写消息类（按域分文件）**

创建 `Assets/Scripts/AOT/Protocol/Messages/Login.cs`（完整内容，合并决策见本计划头部）：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 登录请求（v1 合并版：原 KcpNet 版与 KcpServer 版字段并集；
    /// 丢弃服务端旧版 X/Y/Z 出生点字段，出生点由 P1 EnterWorld 承担）
    /// </summary>
    [ProtoContract]
    public class LoginRequest : IMessage
    {
        /// <summary>用户名</summary>
        [ProtoMember(1)]
        public string Username { get; set; } = string.Empty;

        /// <summary>密码（MD5哈希）</summary>
        [ProtoMember(2)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>目标区服</summary>
        [ProtoMember(3)]
        public string ZoneId { get; set; } = "Global";

        /// <summary>客户端版本</summary>
        [ProtoMember(4)]
        public string ClientVersion { get; set; } = "1.0.0";

        /// <summary>设备信息</summary>
        [ProtoMember(5)]
        public string DeviceInfo { get; set; } = string.Empty;

        /// <summary>协议版本（握手校验，来自 ProtocolConstants.ProtocolVersion）</summary>
        [ProtoMember(6)]
        public long ProtocolVersion { get; set; }
    }

    /// <summary>
    /// 登录响应（v1 重排：PlayerId 吸收原 UserId 语义）
    /// </summary>
    [ProtoContract]
    public class LoginResponse : IMessage
    {
        /// <summary>是否成功</summary>
        [ProtoMember(1)]
        public bool Success { get; set; }

        /// <summary>结果描述</summary>
        [ProtoMember(2)]
        public string Message { get; set; } = string.Empty;

        /// <summary>玩家 ID（原 Client 版 UserId 与 Server 版 PlayerId 合并语义）</summary>
        [ProtoMember(3)]
        public long PlayerId { get; set; }

        /// <summary>会话令牌（断线重连用）</summary>
        [ProtoMember(4)]
        public string SessionToken { get; set; } = string.Empty;

        /// <summary>分配到的区服</summary>
        [ProtoMember(5)]
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>服务器时间戳（时间戳语义：发送时刻的服务器时钟，spec §3）</summary>
        [ProtoMember(6)]
        public long ServerTimestamp { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/Echo.cs`（完整内容）：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>P0 连通性验收载体：回环请求（KCP over UDP 真 socket 往返）</summary>
    [ProtoContract]
    public class EchoRequest : IMessage
    {
        /// <summary>客户端发起时刻的服务器时钟换算值（P0 阶段允许填本地墙钟）</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }
    }

    /// <summary>P0 连通性验收载体：回环响应</summary>
    [ProtoContract]
    public class EchoResponse : IMessage
    {
        /// <summary>原样回传请求中的 ClientTimestamp</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        /// <summary>响应生成时刻的服务器时钟</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/SystemMessages.cs`（完整内容，照抄 Unity 版现状）：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>心跳消息</summary>
    [ProtoContract]
    public class Heartbeat : IMessage
    {
        /// <summary>客户端时间戳</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        /// <summary>序列号</summary>
        [ProtoMember(2)]
        public uint Sequence { get; set; }
    }

    /// <summary>踢出消息（服务器 → 客户端）</summary>
    [ProtoContract]
    public class Kick : IMessage
    {
        /// <summary>原因</summary>
        [ProtoMember(1)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>踢出类型（0=正常踢出，1=违规，2=服务器维护）</summary>
        [ProtoMember(2)]
        public int KickType { get; set; }

        /// <summary>允许重连时间（秒，0=不允许重连）</summary>
        [ProtoMember(3)]
        public int AllowReconnectAfter { get; set; }
    }

    /// <summary>断开连接请求（客户端 → 服务器）</summary>
    [ProtoContract]
    public class DisconnectRequest : IMessage
    {
        /// <summary>原因</summary>
        [ProtoMember(1)]
        public string Reason { get; set; } = "ClientDisconnect";

        /// <summary>是否优雅断开</summary>
        [ProtoMember(2)]
        public bool Graceful { get; set; } = true;
    }

    /// <summary>关闭通知（服务器 → 客户端）</summary>
    [ProtoContract]
    public class ShutdownNotification : IMessage
    {
        /// <summary>延迟秒数</summary>
        [ProtoMember(1)]
        public int DelaySeconds { get; set; }

        /// <summary>原因</summary>
        [ProtoMember(2)]
        public string Reason { get; set; } = "ServerMaintenance";

        /// <summary>预计恢复时间</summary>
        [ProtoMember(3)]
        public long EstimatedRecoveryTime { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/PositionSync.cs`（完整内容，照抄两端一致的现状）：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>位置同步请求（B+ 模型：客户端报位置 + 序列号对账）</summary>
    [ProtoContract]
    public class PositionSyncRequest : IMessage
    {
        /// <summary>位置 X</summary>
        [ProtoMember(1)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(2)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(3)]
        public float Z { get; set; }

        /// <summary>旋转角度（Y轴欧拉角）</summary>
        [ProtoMember(4)]
        public float Rotation { get; set; }

        /// <summary>速度</summary>
        [ProtoMember(5)]
        public float Speed { get; set; }

        /// <summary>时间戳（服务器时钟语义，spec §3）</summary>
        [ProtoMember(6)]
        public long Timestamp { get; set; }

        /// <summary>序列号（单调递增，用于 ack 对账）</summary>
        [ProtoMember(7)]
        public uint Sequence { get; set; }
    }

    /// <summary>位置同步响应（含权威纠偏数据）</summary>
    [ProtoContract]
    public class PositionSyncResponse : IMessage
    {
        /// <summary>确认的序列号</summary>
        [ProtoMember(1)]
        public uint AcknowledgedSequence { get; set; }

        /// <summary>服务器时间戳</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }

        /// <summary>权威位置 X（偏差过大时下发）</summary>
        [ProtoMember(3)]
        public float AuthoritativeX { get; set; }

        /// <summary>权威位置 Y</summary>
        [ProtoMember(4)]
        public float AuthoritativeY { get; set; }

        /// <summary>权威位置 Z</summary>
        [ProtoMember(5)]
        public float AuthoritativeZ { get; set; }

        /// <summary>权威旋转角度</summary>
        [ProtoMember(6)]
        public float AuthoritativeRotation { get; set; }

        /// <summary>是否有位置校正（true 则客户端平滑修正到权威位置）</summary>
        [ProtoMember(7)]
        public bool HasPositionCorrection { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/RemoteSync.cs`（完整内容）：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>远程玩家位置同步（服务器 → 客户端广播）</summary>
    [ProtoContract]
    public class RemotePlayerSync : IMessage
    {
        /// <summary>玩家 ID</summary>
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>位置 X</summary>
        [ProtoMember(2)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(3)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(4)]
        public float Z { get; set; }

        /// <summary>旋转角度（Y轴）</summary>
        [ProtoMember(5)]
        public float Rotation { get; set; }

        /// <summary>移动速度</summary>
        [ProtoMember(6)]
        public float Speed { get; set; }

        /// <summary>时间戳（服务器时钟语义）</summary>
        [ProtoMember(7)]
        public long Timestamp { get; set; }

        /// <summary>序列号</summary>
        [ProtoMember(8)]
        public uint Sequence { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/Chat.cs`（完整内容，照抄 Unity 版现状）：

```csharp
using System;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>聊天消息</summary>
    [ProtoContract]
    public class ChatMessage : IMessage
    {
        /// <summary>发送者 ID</summary>
        [ProtoMember(1)]
        public long SenderId { get; set; }

        /// <summary>发送者名称</summary>
        [ProtoMember(2)]
        public string SenderName { get; set; } = string.Empty;

        /// <summary>消息内容</summary>
        [ProtoMember(3)]
        public string Content { get; set; } = string.Empty;

        /// <summary>频道类型（0=世界，1=私聊，2=队伍，3=公会）</summary>
        [ProtoMember(4)]
        public int ChannelType { get; set; }

        /// <summary>目标 ID（私聊时使用）</summary>
        [ProtoMember(5)]
        public long TargetId { get; set; }

        /// <summary>时间戳</summary>
        [ProtoMember(6)]
        public long Timestamp { get; set; }
    }

    /// <summary>系统广播消息</summary>
    [ProtoContract]
    public class BroadcastMessage : IMessage
    {
        /// <summary>消息类型</summary>
        [ProtoMember(1)]
        public int MessageType { get; set; }

        /// <summary>消息内容</summary>
        [ProtoMember(2)]
        public string Content { get; set; } = string.Empty;

        /// <summary>参数列表</summary>
        [ProtoMember(3)]
        public string[] Parameters { get; set; } = Array.Empty<string>();

        /// <summary>优先级（0=低，1=中，2=高）</summary>
        [ProtoMember(4)]
        public int Priority { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/Room.cs`（完整内容，照抄 Server 版现状）：

```csharp
using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>玩家进入房间通知（服务器 → 其他客户端）</summary>
    [ProtoContract]
    public class PlayerEnterRoom : IMessage
    {
        /// <summary>玩家 ID</summary>
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>玩家名称</summary>
        [ProtoMember(2)]
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>区服 ID</summary>
        [ProtoMember(3)]
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>出生位置 X</summary>
        [ProtoMember(4)]
        public float X { get; set; }

        /// <summary>出生位置 Y</summary>
        [ProtoMember(5)]
        public float Y { get; set; }

        /// <summary>出生位置 Z</summary>
        [ProtoMember(6)]
        public float Z { get; set; }

        /// <summary>朝向</summary>
        [ProtoMember(7)]
        public float Rotation { get; set; }

        /// <summary>服务器时间戳</summary>
        [ProtoMember(8)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>玩家离开房间通知（服务器 → 其他客户端）</summary>
    [ProtoContract]
    public class PlayerLeaveRoom : IMessage
    {
        /// <summary>玩家 ID</summary>
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>离开原因</summary>
        [ProtoMember(2)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>服务器时间戳</summary>
        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>房间全量快照（登录成功后下发）</summary>
    [ProtoContract]
    public class RoomSync : IMessage
    {
        /// <summary>区服 ID</summary>
        [ProtoMember(1)]
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>房间内其他玩家快照列表</summary>
        [ProtoMember(2)]
        public List<RemotePlayerSync> Players { get; set; } = new List<RemotePlayerSync>();

        /// <summary>服务器时间戳</summary>
        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/Npc.cs`（完整内容，照抄 Server 版现状）：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>NPC 动画状态</summary>
    public enum NpcAnimationState : byte
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Attack = 3,
        Skill = 4,
        Hit = 5,
        Death = 6
    }

    /// <summary>NPC 生成通知</summary>
    [ProtoContract]
    public class NpcSpawn : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>NPC 模板 ID</summary>
        [ProtoMember(2)]
        public string NpcTemplateId { get; set; } = string.Empty;

        /// <summary>位置 X</summary>
        [ProtoMember(3)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(4)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(5)]
        public float Z { get; set; }

        /// <summary>朝向</summary>
        [ProtoMember(6)]
        public float Rotation { get; set; }

        /// <summary>服务器时间戳</summary>
        [ProtoMember(7)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>NPC 销毁通知</summary>
    [ProtoContract]
    public class NpcDespawn : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>原因</summary>
        [ProtoMember(2)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>服务器时间戳</summary>
        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>NPC 位置同步</summary>
    [ProtoContract]
    public class NpcPosSync : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>位置 X</summary>
        [ProtoMember(2)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(3)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(4)]
        public float Z { get; set; }

        /// <summary>朝向</summary>
        [ProtoMember(5)]
        public float Rotation { get; set; }

        /// <summary>速度</summary>
        [ProtoMember(6)]
        public float Speed { get; set; }

        /// <summary>时间戳</summary>
        [ProtoMember(7)]
        public long Timestamp { get; set; }

        /// <summary>序列号</summary>
        [ProtoMember(8)]
        public uint Sequence { get; set; }
    }

    /// <summary>NPC 动画同步</summary>
    [ProtoContract]
    public class NpcAnimSync : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>动画状态</summary>
        [ProtoMember(2)]
        public NpcAnimationState State { get; set; }

        /// <summary>状态哈希</summary>
        [ProtoMember(3)]
        public int StateHash { get; set; }

        /// <summary>过渡时长</summary>
        [ProtoMember(4)]
        public float TransitionDuration { get; set; }

        /// <summary>时间戳</summary>
        [ProtoMember(5)]
        public long Timestamp { get; set; }
    }
}
```

- [ ] **Step 5: 写编解码器（单份收编）**

创建 `Assets/Scripts/AOT/Protocol/MessageCodec.cs`（完整内容；以 Unity 侧现役实现为蓝本迁入 `Cool.Protocol`，Registry 扩至全集）：

```csharp
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>消息标志位（wire 格式第 1 字节）</summary>
    [Flags]
    public enum MessageFlags : byte
    {
        None = 0,
        Reliable = 1 << 2,
        Encrypted = 1 << 0,
        Compressed = 1 << 1,
        HighPriority = 1 << 3,

        Reserved4 = 1 << 4,
        Reserved5 = 1 << 5,
        Reserved6 = 1 << 6,
        Reserved7 = 1 << 7
    }

    /// <summary>
    /// 消息编解码器 —— 全仓库唯一一份（规则层 v1：
    /// header[flags(1)|messageId(2 LE)|length(2 LE)] + protobuf 负载）
    /// </summary>
    public static class MessageCodec
    {
        private const int HeaderSize = ProtocolConstants.HeaderSize;

        /// <summary>编码消息到缓冲区</summary>
        public static int Encode(MessageId messageId, object message, IBufferWriter<byte> writer,
            MessageFlags flags = MessageFlags.None, byte[]? encryptionKey = null, bool compress = false)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            byte[] payload;
            using (var memoryStream = new MemoryStream())
            {
                Serializer.NonGeneric.Serialize(memoryStream, message);
                payload = memoryStream.ToArray();
            }

            if (compress && payload.Length > 0)
            {
                payload = Compress(payload);
                flags |= MessageFlags.Compressed;
            }

            if ((flags & MessageFlags.Encrypted) != 0 && encryptionKey != null && encryptionKey.Length > 0)
            {
                payload = Encrypt(payload, encryptionKey);
            }

            var headerSpan = writer.GetSpan(HeaderSize);
            headerSpan[0] = (byte)flags;
            BitConverter.TryWriteBytes(headerSpan.Slice(1), (ushort)messageId);
            BitConverter.TryWriteBytes(headerSpan.Slice(3), (ushort)payload.Length);
            writer.Advance(HeaderSize);

            var payloadSpan = writer.GetSpan(payload.Length);
            payload.CopyTo(payloadSpan);
            writer.Advance(payload.Length);

            return HeaderSize + payload.Length;
        }

        /// <summary>解码消息</summary>
        public static (MessageId messageId, IMessage message) Decode(byte[] data, byte[]? encryptionKey = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < HeaderSize) throw new ArgumentException($"Data too short: {data.Length} < {HeaderSize}");

            var flags = (MessageFlags)data[0];
            var messageId = (MessageId)BitConverter.ToUInt16(data, 1);
            var length = BitConverter.ToUInt16(data, 3);

            if (data.Length < HeaderSize + length)
                throw new ArgumentException($"Data length mismatch: {data.Length} < {HeaderSize + length}");

            var payload = new byte[length];
            Array.Copy(data, HeaderSize, payload, 0, length);

            if ((flags & MessageFlags.Encrypted) != 0 && encryptionKey != null && encryptionKey.Length > 0)
            {
                payload = Decrypt(payload, encryptionKey);
            }

            if ((flags & MessageFlags.Compressed) != 0)
            {
                payload = Decompress(payload);
            }

            var messageType = MessageTypeRegistry.GetMessageType(messageId);
            if (messageType == null)
                throw new InvalidOperationException($"Unknown message ID: {(ushort)messageId}");

            using (var memoryStream = new MemoryStream(payload))
            {
                var message = Serializer.NonGeneric.Deserialize(messageType, memoryStream) as IMessage;
                if (message == null)
                    throw new InvalidOperationException($"Failed to deserialize message type: {messageType.Name}");

                return (messageId, message);
            }
        }

        /// <summary>尝试解码（失败返回 false 不抛异常）</summary>
        public static bool TryDecode(byte[] data, out (MessageId messageId, IMessage message) result, byte[]? encryptionKey = null)
        {
            result = default;
            try
            {
                result = Decode(data, encryptionKey);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>便捷编码到字节数组</summary>
        public static byte[] EncodeToArray(MessageId messageId, object message,
            MessageFlags flags = MessageFlags.None, byte[]? encryptionKey = null, bool compress = false)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            Encode(messageId, message, bufferWriter, flags, encryptionKey, compress);
            return bufferWriter.WrittenMemory.ToArray();
        }

        // ========== 加密解密（XOR 占位实现，沿袭现状；正式加密另行任务） ==========

        private static byte[] Encrypt(byte[] data, byte[] key)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }
            return result;
        }

        private static byte[] Decrypt(byte[] data, byte[] key) => Encrypt(data, key);

        // ========== 压缩解压（直通占位实现，沿袭现状） ==========

        private static byte[] Compress(byte[] data) => data;

        private static byte[] Decompress(byte[] data) => data;
    }

    /// <summary>消息类型注册表（id ↔ Type 双向映射）</summary>
    public static class MessageTypeRegistry
    {
        private static readonly Dictionary<ushort, Type> _messageIdToType = new Dictionary<ushort, Type>();
        private static readonly Dictionary<Type, ushort> _typeToMessageId = new Dictionary<Type, ushort>();

        static MessageTypeRegistry()
        {
            RegisterMessageType(MessageId.LoginRequest, typeof(LoginRequest));
            RegisterMessageType(MessageId.LoginResponse, typeof(LoginResponse));
            RegisterMessageType(MessageId.Heartbeat, typeof(Heartbeat));
            RegisterMessageType(MessageId.Kick, typeof(Kick));
            RegisterMessageType(MessageId.DisconnectRequest, typeof(DisconnectRequest));
            RegisterMessageType(MessageId.ShutdownNotification, typeof(ShutdownNotification));
            RegisterMessageType(MessageId.EchoRequest, typeof(EchoRequest));
            RegisterMessageType(MessageId.EchoResponse, typeof(EchoResponse));
            RegisterMessageType(MessageId.PositionSyncRequest, typeof(PositionSyncRequest));
            RegisterMessageType(MessageId.PositionSyncResponse, typeof(PositionSyncResponse));
            RegisterMessageType(MessageId.ChatMessage, typeof(ChatMessage));
            RegisterMessageType(MessageId.BroadcastMessage, typeof(BroadcastMessage));
            RegisterMessageType(MessageId.RemotePlayerSync, typeof(RemotePlayerSync));
            RegisterMessageType(MessageId.PlayerEnterRoom, typeof(PlayerEnterRoom));
            RegisterMessageType(MessageId.PlayerLeaveRoom, typeof(PlayerLeaveRoom));
            RegisterMessageType(MessageId.RoomSync, typeof(RoomSync));
            RegisterMessageType(MessageId.NpcSpawn, typeof(NpcSpawn));
            RegisterMessageType(MessageId.NpcDespawn, typeof(NpcDespawn));
            RegisterMessageType(MessageId.NpcPosSync, typeof(NpcPosSync));
            RegisterMessageType(MessageId.NpcAnimSync, typeof(NpcAnimSync));
        }

        /// <summary>注册消息类型（重复注册同一 id 覆盖旧值）</summary>
        public static void RegisterMessageType(MessageId messageId, Type messageType)
        {
            if (!typeof(IMessage).IsAssignableFrom(messageType))
                throw new ArgumentException($"Type must implement IMessage: {messageType.Name}");

            ushort id = (ushort)messageId;
            _messageIdToType[id] = messageType;
            _typeToMessageId[messageType] = id;
        }

        public static void RegisterMessageType<T>(MessageId messageId) where T : IMessage
            => RegisterMessageType(messageId, typeof(T));

        public static Type? GetMessageType(MessageId messageId)
            => _messageIdToType.TryGetValue((ushort)messageId, out var type) ? type : null;

        public static MessageId? GetMessageId(Type messageType)
            => _typeToMessageId.TryGetValue(messageType, out var messageId) ? (MessageId)messageId : null;

        public static MessageId? GetMessageId<T>() where T : IMessage
            => GetMessageId(typeof(T));

        public static bool IsRegistered(MessageId messageId)
            => _messageIdToType.ContainsKey((ushort)messageId);

        public static bool IsRegistered(Type messageType)
            => _typeToMessageId.ContainsKey(messageType);

        public static IEnumerable<MessageId> GetAllMessageIds()
            => _messageIdToType.Keys.Select(id => (MessageId)id);
    }

    /// <summary>ArrayBufferWriter 实现（.NET Standard 2.0 兼容，沿袭现状）</summary>
    public sealed class ArrayBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer;
        private int _written;

        public ArrayBufferWriter(int initialCapacity = 1024)
        {
            _buffer = new byte[initialCapacity];
            _written = 0;
        }

        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
        public int WrittenCount => _written;
        public int Capacity => _buffer.Length;
        public int FreeCapacity => _buffer.Length - _written;

        public void Advance(int count)
        {
            if (count < 0) throw new ArgumentException(nameof(count));
            if (_written + count > _buffer.Length) throw new InvalidOperationException("Cannot advance beyond buffer capacity");
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public void Clear() => _written = 0;

        public byte[] ToArray()
        {
            var result = new byte[_written];
            Array.Copy(_buffer, 0, result, 0, _written);
            return result;
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentException(nameof(sizeHint));

            var sizeHintOrMinimum = sizeHint > 0 ? sizeHint : 256;
            if (sizeHintOrMinimum <= FreeCapacity) return;

            var currentLength = _buffer.Length;
            var growBy = Math.Max(sizeHintOrMinimum, currentLength);
            var newSize = currentLength + growBy;

            if ((uint)newSize > int.MaxValue)
            {
                newSize = currentLength + sizeHintOrMinimum;
                if ((uint)newSize > int.MaxValue)
                    throw new OutOfMemoryException($"Cannot allocate buffer of size {newSize}");
            }

            Array.Resize(ref _buffer, newSize);
        }
    }
}
```

- [ ] **Step 6: 运行确认通过**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 9`（Foundation 3 + Roundtrip 5 + Completeness 2）。

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/AOT/Protocol Server/Server.Tests
git commit -m "feat(protocol): unified message set v1 - single-source codec, 20 registered messages, rules tests"
```

---

### Task 4: Unity 侧接线切换 + EditMode 回归测试

**Files:**
- Modify: `Assets/Scripts/AOT/KcpNet/KcpNet.asmdef`
- Create: `Assets/Scripts/AOT/KcpNet/GlobalUsings.cs`
- Delete: `Assets/Scripts/AOT/KcpNet/Common/Message.cs`(+meta), `Common/MessageCodec.cs`(+meta)
- Modify: `Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`
- Create: `Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`

- [ ] **Step 1: 改 KcpNet.asmdef 引用共享程序集**

编辑 `Assets/Scripts/AOT/KcpNet/KcpNet.asmdef`，将 `"references": [],` 改为：

```json
    "references": [
        "Cool.Protocol"
    ],
```

- [ ] **Step 2: KcpNet 全局引入共享命名空间**

创建 `Assets/Scripts/AOT/KcpNet/GlobalUsings.cs`（完整内容）：

```csharp
// KcpNet 会话/客户端代码原先与本目录内 Message/MessageCodec 同命名空间，
// 收编进 Cool.Protocol 后通过全局 using 维持源码级零改动
global using Cool.Protocol;
```

- [ ] **Step 3: 删除被收编的两件**

```bash
cd "E:/CodeForJob/Cool"
git rm Assets/Scripts/AOT/KcpNet/Common/Message.cs \
       Assets/Scripts/AOT/KcpNet/Common/Message.cs.meta \
       Assets/Scripts/AOT/KcpNet/Common/MessageCodec.cs \
       Assets/Scripts/AOT/KcpNet/Common/MessageCodec.cs.meta
```

- [ ] **Step 4: EditorTests 引用共享程序集并写回归测试**

编辑 `Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`，`references` 数组加入 `"Cool.Protocol"`：

```json
    "references": [
        "GameSys",
        "DataDefinition",
        "Cool.Protocol"
    ],
```

创建 `Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`（完整内容；与 xUnit 侧同一批断言语义，横切约定①："同批断言两端各跑一次"）：

```csharp
using System;
using NUnit.Framework;
using Cool.Protocol;

namespace Hotfix.GameSystems.Tests.Network
{
    /// <summary>
    /// 协议层回归测试（Unity 编辑器环境版本）
    /// 与 Server.Tests 的 CodecRoundtripTests 断言语义一致
    /// </summary>
    public class ProtocolRoundtripTests
    {
        [Test]
        public void Roundtrip_EchoResponse_PreservesFields()
        {
            var source = new EchoResponse
            {
                ClientTimestamp = 123456789L,
                ServerTimestamp = 987654321L
            };

            var encoded = MessageCodec.EncodeToArray(MessageId.EchoResponse, source);
            var (_, decoded) = MessageCodec.Decode(encoded);

            var response = Assert.IsAssignableFrom<EchoResponse>(decoded);
            Assert.AreEqual(source.ClientTimestamp, response.ClientTimestamp);
            Assert.AreEqual(source.ServerTimestamp, response.ServerTimestamp);
        }

        [Test]
        public void Every_MessageId_HasRegisteredType()
        {
            foreach (MessageId id in Enum.GetValues(typeof(MessageId)))
            {
                Assert.IsTrue(
                    MessageTypeRegistry.IsRegistered(id),
                    $"MessageId.{id} 未在 MessageTypeRegistry 注册");
            }
        }
    }
}
```

- [ ] **Step 5: 刷新 Unity 并跑 EditMode 测试**

调用 Unity MCP 技能顺序：

1. `assets-refresh` → 等待编译完成，Expected: 无编译错误（Examples 层引用的字段均存在于合并版）
2. `tests-run`（EditMode，class 过滤 `ProtocolRoundtripTests`）→ Expected: 2 passed

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/AOT/KcpNet Assets/Scripts/Tests
git commit -m "refactor(net): kcpnet consumes shared Cool.Protocol - delete parallel codec, editmode regression green"
```

---

### Task 5: Server 侧接线 + EchoHandler

**Files:**
- Modify: `Server/Server3C.csproj`
- Create: `Server/GlobalUsings.cs`
- Delete: `Server/IMessage.cs`, `Server/Messages.cs`, `Server/MessageCodec.cs`
- Create: `Server/Handler/EchoHandler.cs`
- Modify: `Server/Server3C.cs`（分发注册）
- Modify: `Server/Handler/LoginHandler.cs`（移除已弃字段引用）

- [ ] **Step 1: csproj 编译共享源**

编辑 `Server/Server3C.csproj`，在 `<ItemGroup>`（PackageReference 组之前）插入：

```xml
  <ItemGroup>
    <Compile Include="..\Assets\Scripts\AOT\Protocol\**\*.cs" />
  </ItemGroup>
```

- [ ] **Step 2: 全局引入共享命名空间**

创建 `Server/GlobalUsings.cs`（完整内容）：

```csharp
// 服务端代码原先与本目录内 Messages/MessageCodec/IMessage 同命名空间，
// 收编进 Cool.Protocol 后通过全局 using 维持 Handler/AI/Room 各模块源码级零改动
global using Cool.Protocol;
global using Cool.Protocol.Messages;
```

- [ ] **Step 3: 删除被收编的三件**

```bash
cd "E:/CodeForJob/Cool"
git rm Server/IMessage.cs Server/Messages.cs Server/MessageCodec.cs
```

- [ ] **Step 4: 创建 EchoHandler**

创建 `Server/Handler/EchoHandler.cs`（完整内容，模式对齐 `PositionSyncHandler`）：

```csharp
using System;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// P0 连通性验收处理器：echo 回环（演示时间戳语义 —— ServerTimestamp 由服务器填充）
    /// </summary>
    public class EchoHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;

        public EchoHandler(Server3C server, ILogger logger)
        {
            _server = server;
            _logger = logger;
        }

        public async Task HandleEchoAsync(KcpServerSession session, EchoRequest request)
        {
            var response = new EchoResponse
            {
                ClientTimestamp = request.ClientTimestamp,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };

            await _server.SendToSessionAsync(session, MessageId.EchoResponse, response);
            _logger.LogInformation($"Echo roundtrip for session {session.SessionId}");
        }
    }
}
```

- [ ] **Step 5: 在 Server3C 注册**

读取 `Server/Server3C.cs`，定位两处锚点做最小插入：

锚点 A（第 41-42 行附近，紧随现有两个 Handler 字段初始化）追加一行：

```csharp
_echoHandler = new EchoHandler(this, _logger);
```

并在同类中添加字段声明（与 `_positionSyncHandler` 声明相邻）：

```csharp
private readonly EchoHandler _echoHandler;
```

锚点 B（第 227 行附近的 `switch (message)` 内，参照现有 `case PositionSyncRequest posSync:` 形态）追加 case：

```csharp
case EchoRequest echoRequest:
    await _echoHandler.HandleEchoAsync(session, echoRequest);
    break;
```

- [ ] **Step 6: 修正 LoginHandler 对已弃字段的引用**

`Server/Handler/LoginHandler.cs:29` 原：

```csharp
                Position = new Vector3(request.X, request.Y, request.Z)
```

改为（出生点暂定原点，P1 EnterWorld 接管）：

```csharp
                Position = new Vector3(0f, 0f, 0f)
```

同时在 LoginHandler.HandleLoginAsync 的响应构造处（`loginResponse = new LoginResponse {...}` 块）补填协议版本校验前置检查 —— 在方法开头、`long playerId = ...` 之前插入：

```csharp
            if (request.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                var reject = new LoginResponse
                {
                    Success = false,
                    Message = $"Protocol version mismatch: client={request.ProtocolVersion}, server={ProtocolConstants.ProtocolVersion}",
                    ServerTimestamp = DateTime.UtcNow.Ticks
                };
                await _server.SendToSessionAsync(session, MessageId.LoginResponse, reject);
                _logger.LogWarning($"Rejected login from {session.RemoteEndPoint}: protocol version {request.ProtocolVersion}");
                return;
            }
```

> 这是 spec §3「版本握手」规则的第一个落地点。此改动同时给 PlayMode 冒烟提供了正确的 Login 字段用法。

- [ ] **Step 7: 构建验证**

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
```

Expected: `Build succeeded.`，0 Error(s)。

- [ ] **Step 8: Commit**

```bash
git add Server
git commit -m "feat(server): consume shared Cool.Protocol - single codec, echo handler, protocol version handshake"
```

---

### Task 6: 双端 echo 真连接验收（PlayMode 冒烟）

**Files:**
- Create: `Assets/Scripts/Tests/PlayMode/KcpNet.PlayModeTests.asmdef`
- Create: `Assets/Scripts/Tests/PlayMode/EchoSmokeTests.cs`

- [ ] **Step 1: 建 PlayMode 测试程序集**

创建 `Assets/Scripts/Tests/PlayMode/KcpNet.PlayModeTests.asmdef`（完整内容）：

```json
{
    "name": "KcpNet.PlayModeTests",
    "rootNamespace": "KcpNet.PlayModeTests",
    "references": [
        "KcpNet",
        "Cool.Protocol"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "versionDefines": [],
    "noEngineReferences": false,
    "autoReferenced": false,
    "optionalUnityReferences": [
        "TestAssemblies"
    ]
}
```

- [ ] **Step 2: 写冒烟测试**

创建 `Assets/Scripts/Tests/PlayMode/EchoSmokeTests.cs`（完整内容）：

```csharp
using System;
using System.Collections;
using System.Net;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpNet;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P0 核心验收：Unity 客户端 ↔ Server3C 的 KCP over UDP 真连接 echo 往返
    /// 前置：服务器进程已在 127.0.0.1:8000 运行（见计划正文执行须知）
    /// </summary>
    public class EchoSmokeTests
    {
        private sealed class SmokeResult
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public EchoResponse? Response;
            public long SentClientTimestamp;
        }

        [UnityTest]
        public IEnumerator EchoRoundtrip_ThroughRealKcpSocket()
        {
            var runTask = RunEchoAsync();

            // ~30 秒帧等待上限（EditMode 之外的真实 socket IO 场景足够宽容）
            const int maxFrames = 1800;
            int frames = 0;
            while (!runTask.IsCompleted && frames < maxFrames)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "echo roundtrip timed out after 30s");
            var result = runTask.Result;

            Assert.IsTrue(result.Success,
                $"echo failed: {result.FailureReason}");
            Assert.IsNotNull(result.Response, "no EchoResponse captured");

            Assert.AreEqual(result.SentClientTimestamp, result.Response!.ClientTimestamp,
                "server must echo ClientTimestamp verbatim");
            Assert.Greater(result.Response.ServerTimestamp, 0,
                "server timestamp semantics violated (spec §3)");
        }

        private static async Task<SmokeResult> RunEchoAsync()
        {
            var result = new SmokeResult();
            KcpClient? client = null;
            var completionSource = new TaskCompletionSource<EchoResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var options = new KcpOptions
                {
                    SendWindowSize = 32,
                    ReceiveWindowSize = 32,
                    NoDelay = true,
                    Interval = 10,
                    Mtu = 1400,
                    ConnectionTimeout = 30,
                    HeartbeatInterval = 5,
                    HeartbeatTimeout = 30,
                    MaxReconnectAttempts = 3,
                    BaseReconnectDelay = 1,
                    MaxReconnectDelay = 30,
                    UseExponentialBackoff = true,
                    SendBufferSize = 65536,
                    ReceiveBufferSize = 65536
                };
                // ConnectAsync 内部自启动接收循环（KcpClient.cs:59），只需订阅事件
                client = new KcpClient(
                    DateTime.UtcNow.Ticks,                 // sessionId（示例约定：临时值即可）
                    new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000),
                    new KcpClientTransport(options, ConsoleLogger.Instance),
                    options,
                    ConsoleLogger.Instance,
                    DirectMessageExecutor.Instance);

                EventHandler<IMessage>? onReceived = null;
                onReceived = (_, message) =>
                {
                    if (message is EchoResponse echoResponse)
                    {
                        client.MessageReceived -= onReceived;
                        completionSource.TrySetResult(echoResponse);
                    }
                };
                client.MessageReceived += onReceived;

                await client.ConnectAsync("127.0.0.1", 8000);

                result.SentClientTimestamp = DateTime.UtcNow.Ticks;
                result.Response = await AwaitWithTimeout(completionSource.Task, TimeSpan.FromSeconds(15));
                result.Success = result.Response != null;
                if (!result.Success) result.FailureReason = "no EchoResponse within 15s";
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
            }
            finally
            {
                try
                {
                    if (client != null && client.IsConnected)
                        await client.CloseAsync("smoke-test-done");
                }
                catch { /* teardown 尽力而为 */ }
                client?.Dispose();
            }

            return result;
        }

        private static async Task<EchoResponse?> AwaitWithTimeout(Task<EchoResponse> task, TimeSpan timeout)
        {
            var finished = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            return finished == task && task.Status == TaskStatus.RanToCompletion
                ? await task.ConfigureAwait(false)
                : null;
        }
    }
}
```

> 兼容提示：若 `CloseAsync` 签名为 `CloseAsync(string reason, CancellationToken token)`，请在finally里改为传 `_` 忽略 token——以编译器报错为准逐一对照签名（`KcpSession.cs` 中均为 public 成员，可直读）。

- [ ] **Step 3: 启动服务器（后台）**

```bash
cd "E:/CodeForJob/Cool" && dotnet run --project Server/Server3C.csproj -- 8000
```

（工具指示：以后台任务方式启动；Expected 输出 `Server3C is running on port 8000`。）

- [ ] **Step 4: 刷新 Unity 并跑 PlayMode 测试**

1. 调用 `assets-refresh` → Expected 无编译错误
2. 按 unity-mcp 内存备忘：先确认所有已打开场景均已保存（`scene-save` 当前活动场景或 `editor-application-get-state` 检查 dirty）
3. 调用 `tests-run`（PlayMode，class 过滤 `EchoSmokeTests`）

Expected: 1 passed。（失败排查第一顺位：服务器进程是否存活；第二顺位：防火墙拦截 UDP 8000 回环——本机回环通常放行。）

- [ ] **Step 5: 停止服务器**

停止步骤 3 启动的后台任务（TaskStop 或终止对应 shell 进程）。Expected: 进程退出且无残留占用 8000 端口：

```bash
netstat -ano | grep ":8000 " || echo "port 8000 free"
```

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Tests/PlayMode
git commit -m "test(net): p0 acceptance - real-kcp echo roundtrip playmode smoke against live server"
```

---

### Task 7: Hotfix 死件清理 + 文档同步

**Files:**
- Delete: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkBridge.cs`(+meta), `NetworkPrediction.cs`(+meta), `PositionInterpolator.cs`(+meta), `SkillNetworkSync.cs`(+meta)
- Keep: `MovementPolicy.cs`（spec §2：保留至 P2 由 NetMovementPolicy 接管注入面）
- Modify: `CLAUDE.md`（架构描述同步）

- [ ] **Step 1: 删除前安全核查**

```bash
cd "E:/CodeForJob/Cool"
grep -rln "NetworkBridge\|NetworkPrediction\|PositionInterpolator\|SkillNetworkSync" \
  Assets/Scripts --include=*.cs | grep -v "Sys3C/Network/"
```

Expected: 空输出（此前 codegraph callers 审计结论一致）。若有输出，先处理引用方再继续——不得跳过。

- [ ] **Step 2: 删除四死件**

```bash
git rm \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkBridge.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkBridge.cs.meta \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkPrediction.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkPrediction.cs.meta \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/PositionInterpolator.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/PositionInterpolator.cs.meta \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/SkillNetworkSync.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/SkillNetworkSync.cs.meta
```

- [ ] **Step 3: CLAUDE.md 架构段同步**

在 CLAUDE.md 的「Sys3C Network」相关行（若存在 NetworkBridge 等条目表格行/列表项）替换为一行：

```markdown
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/` - MovementPolicy 注入点（其余草稿桩已于 P0 清理，见 docs/superpowers/specs/2026-08-27-network-sync-design.md）
```

（若无对应条目则在 Sys3C 表格末尾追加上述一行。）

- [ ] **Step 4: 刷新 + 全量 EditMode 回归**

1. `assets-refresh` → Expected 无编译错误
2. `tests-run`（EditMode，不过滤 class）→ Expected: 全绿（原有套件 + ProtocolRoundtripTests）

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore(hotfix): remove network draft stubs per spec disposal list - movementpolicy retained for p2"
```

---

### Task 8: Protocol README（治理规则落地）+ 终验

**Files:**
- Create: `Assets/Scripts/AOT/Protocol/README.md`

- [ ] **Step 1: 写 README**

创建 `Assets/Scripts/AOT/Protocol/README.md`（完整内容）：

```markdown
# Cool.Protocol —— 双端共享协议层（唯一真相源）

Unity 客户端（asmdef `Cool.Protocol`）与服务器（`Server/Server3C.csproj` 的
`Compile Include ..\Assets\Scripts\AOT\Protocol\**\*.cs`）编译**同一批源文件**。

## 目录纪律

- 本目录**禁止**出现 `using UnityEngine` 及任何引擎类型（服务器编不了）。
- 字段类型仅限基元类型 / `string` / `byte[]` / 本目录枚举 / 本目录消息类。
- protobuf 特性 `[ProtoContract]/[ProtoMember]` 沿用现状。

## 协议治理规则（spec §3 唯一口径）

| 规则 | 内容 |
|---|---|
| 分层冻结 | 规则层 v1 已冻结：header 格式、版本号、seq/时间戳约定、增量编码方式（P1 引入位掩码） |
| 只加不删 | 新消息/新字段永远非破坏性追加；废弃字段置零不删 tag；`MessageId` 数值发布后不可变 |
| 版本握手 | `LoginRequest.ProtocolVersion` ≠ `ProtocolConstants.ProtocolVersion` 时拒绝登录（已在 LoginHandler 实现） |
| 时间戳语义 | 所有双向时间戳 = 发送时刻换算成的**服务器时钟**；禁直接填客户端墙钟 |
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
# 服务器侧测试（xUnit）：codec 回环 + registry 完备性
dotnet test Server/Server.Tests/Server.Tests.csproj

# Unity 侧（NUnit EditMode）：同样断言跑于编辑器环境
#   via unity-mcp tests-run(EditMode, class=ProtocolRoundtripTests)

# protobuf 版本核对
grep -o 'protobuf-net.3\.2\.56' Assets/Packages/protobuf-net.nuspec 2>/dev/null || ls Assets/Packages | grep protobuf
grep 'protobuf-net' Server/Server3C.csproj
```

## 快速联调（echo 冒烟）

```bash
# 终端 1：启动服务器（默认参数即 8000）
dotnet run --project Server/Server3C.csproj -- 8000
# 终端 2（Unity 内）：unity-mcp tests-run(PlayMode, class=EchoSmokeTests)
```
```

- [ ] **Step 2: 终验三连**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
```

外加 unity-mcp：`assets-refresh` → `tests-run`(EditMode 全量) → Expected 全绿。

对照 spec §5-P0 验收清单逐条打勾（写入最终 commit message 或会话日志）：

1. ☑ 服务器进程启动 → Unity 客户端连接 → echo 往返成功（Task 6）
2. ☑ 两端编译同一批共享源文件（Server.Tests xUnit + GameSys.EditorTests NUnit 同批断言双绿，Task 6/8）
3. ☑ 协议规则层 v1 冻结（ProtocolConstants + MessageId + README 治理表，Task 2/3/8）
4. ☑ 处置清单执行完毕（四死件删除 + 两份 Messages 收编 + MovementPolicy 保留，Task 4/5/7）

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/AOT/Protocol/README.md
git commit -m "docs(protocol): governance readme - rule layer v1, wire format, dual-end verification quickstart"
```

---

## Self-Review 记录（计划完成后已自查）

1. **Spec 覆盖（P0 范围逐项）**: §5-P0 四行验收 → Task 6（echo 往返）、Task 4/8（双端同源编译+双绿）、Task 2/3/8（规则层冻结）、Task 4/5/7（处置清单）。§2 交付物（Server 骨架/R2 主循环规定/asmdef/KCP 消息模式）→ Task 1（骨架现改造为复用存量，主循环规定延后至 P1 SessionManager 任务落实——存量 Program.cs 已是单线程事件循环形态，风险 R2 的强制条款标注在 P1 计划必办）。§3 时间戳语义首个落地点 → Task 5 Step 6 + Task 3 EchoResponse。
2. **占位符扫描**: 无 TBD/TODO；所有代码块完整可粘贴；两处"以编译器报错为准对照签名"为有意的容错指引而非缺省实现。
3. **类型一致性**: `EchoResponse.ClientTimestamp/ServerTimestamp`、`MessageCodec.EncodeToArray(MessageId, object, MessageFlags, ...)`、`IMessage`、`ProtocolConstants.HeaderSize` 全文统一；LoginRequest/LoginResponse 使用合并版字段并在所有引用点（Tests/PlayMode/LoginHandler）同步。

## P1-P5 后续计划边界

本计划不含：TickLoop/SnapshotAssembler/PerceptionFilter/ClockSync/预测和解/AOI/CombatRules/BotRunner（各属 P1-P5 独立计划）。R2 强制条款与 D2 KCP 消息模式确认 → **列入 P1 计划首两项任务**。
