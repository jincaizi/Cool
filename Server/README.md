# Server —— 服务端

## 运行

```bash
dotnet run --project Server/Server3C.csproj -- 8000
bash tools/test-server.sh
```

`Program.cs` 解析端口与压测参数：`--bots N`、`--no-avoid`、`--probe`。

## 架构

- `GameLoop.cs`：单线程主循环（约 15Hz / 66ms）。IO 线程只 Enqueue，唯一消费者独占世界状态。
- `Room/`、`Session/`、`Entity/`：房间、会话与实体存储。
- `Aoi/`：AOI 网格、分带广播、bot 压测。
- `Handler/`：登录、位置同步、施法、时钟同步、echo。
- `Monster/`：服务端权威怪物（行为树）。
- `Movement/`：ORCA 避让。
- `Config/`：`monster_config.json` 等。
- `Message/`、`Util/`：消息与数学工具。
- 共享规则：`Assets/Scripts/Hotfix/GameSystems/SyncCore/`；共享协议：`Assets/Scripts/AOT/Protocol/`。

## 多人战斗一致性

- **DamageResult 观察者扩播**：`CastHandler.CollectDamageRecipients` 的收件人 =
  施法者 + 每个受击者 + 施法者的 AOI 观察者 + 每个受击者的 AOI 观察者（`HashSet` 去重）。
  因此旁观者与施法者/受击者收到**同一条** `DamageResult`（同一掷骰、同一余血），
  不会出现「只有当事人看到伤害」的不一致。怪物施法者无 session，`GetSession` 自然跳过。
- **登录名册**：`LoginHandler.BuildRoster` 把房间内已有玩家以 `PlayerEnterRoom` 发给新登录者，
  并向其他人广播新玩家；断线发 `PlayerLeaveRoom`。客户端 `PlayerNameRegistry` 主线程落库，
  远程名牌据此显示玩家名（缺失回退 `玩家 {id}`）。
- **权威 vitals 进快照**：`EntitySnapshot` 携带 Hp/MaxHp/IsDefending（掩码位 `Health`/`Defending`），
  AOI 分带调度把血量/防御变化视为脏位立即推送；客户端据此更新远程 `Targetable` 与名牌血条。
  `Server.Tests/Aoi/BandwidthBudgetTests` 断言含新字段的全字段快照字节上限。

## 子模块 README

- [Monster](Monster/README.md) —— 行为树与权威怪物
- [Movement](Movement/README.md) —— ORCA 避让

## 测试

`Server/Server.Tests/`（xUnit）：AOI、战斗裁决、怪物、移动、协议、会话等。

```bash
bash tools/test-server.sh
bash tools/test-server.sh --filter "FullyQualifiedName~Orca"
```
