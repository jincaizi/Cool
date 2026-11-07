# KcpNet —— KCP 传输与会话

## 结构

- `Client/`：`KcpClient`（会话管理）、`KcpClientTransport`
- `Server/`：`KcpServer`、`KcpServerSession`、`KcpServerTransport`、`KcpListenerWrapper`
- `Common/`：`Kcp`、`KcpOptions`、`KcpSession`、`IKcpTransport`、`IMessageExecutor`、`ILogger`、`MessageDispatcher`
- `KcpNetClientAdapter.cs`：客户端适配器
- `Common/IMessageExecutor.cs` 中的 `UnityMainThreadExecutor`：Unity 主线程调度基类

## 约定

- 业务消息与协议头在 [AOT/Protocol](../Protocol/README.md)；本目录只管传输与会话。
- KCP 保持消息边界：每帧一条完整消息。不要在 KCP 上叠加第二条不可靠通道。
- 使用示例见 [Assets/Scripts/Examples/README.md](../../Examples/README.md)。
- 客户端业务接线在 `SyncLayer/ClientNetworkHost`；服务端在 `Server/Server3C` 与 `Server/Handler/`。

## 测试

Editor：`Assets/Scripts/Tests/Editor/Network/`、`Assets/Scripts/Tests/Editor/WebSocketNet/`。
PlayMode：`EchoSmokeTests`、`TwoClientWorldTests`。

```bash
bash tools/unity-tests.sh PlayMode EchoSmokeTests
```

## 相关模块

- [Protocol](../Protocol/README.md) —— 双端共享协议
- [SyncLayer](../../Hotfix/GameSystems/SyncLayer/README.md) —— 客户端业务接线
- [服务端](../../../../Server/README.md) —— 服务端业务接线
