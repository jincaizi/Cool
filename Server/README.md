# Server —— 服务端

## 运行

```bash
dotnet build Server/Server3C.csproj
dotnet Server/bin/Debug/net8.0/Server3C.dll 8000
bash tools/test-server.sh
```

> 用 `dotnet <dll>` 而不是 `dotnet run`：本机 `dotnet run` 的 apphost 会让
> `HttpListener.Start` 抛「句柄无效」；普通环境两者等价。

`Program.cs` 解析端口与压测参数：`--bots N`、`--no-avoid`、`--probe`、
`--monsters N`、`--map <id>`、`--no-nav`。

## 架构

- `GameLoop.cs`：单线程主循环（约 15Hz / 66ms）。IO 线程只 Enqueue，唯一消费者独占世界状态。
- `Room/`、`Session/`、`Entity/`：房间、会话与实体存储。
- `Aoi/`：AOI 网格、分带广播、bot 压测。
- `Handler/`：登录、位置同步、施法、时钟同步、echo。
- `Monster/`：服务端权威怪物（行为树）。
- `Movement/`：ORCA 避让。
- [Navigation](Navigation/README.md)：三角面 navmesh（A* + 漏斗）、navbin 加载、每 tick 查询预算。
- `Config/`：`monster_config.json` 等。
- `Message/`、`Util/`：消息与数学工具。
- 共享规则：`Assets/Scripts/Hotfix/GameSystems/SyncCore/`；共享协议：`Assets/Scripts/AOT/Protocol/`。

## 多人战斗一致性

- **权威复活落点**：`Config/maps.json` 的 `playerSpawn` 提供地图复活坐标，默认地图为 DemoDay。
  支持该能力的玩家到点复活时，主循环同时更新 HP、位置、速度、防御和移动校验基准，
  再发送 `RespawnNotice`。位置同步丢弃死亡期间和复活 tick 不匹配的请求，
  首个新请求按一个上报周期的移动预算校验。
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

## 投射物与落点判定（20003 火球）

- 20003 在共享表 `CombatRules` 中是 `ProjectileKind.CircleAtPoint`：`radius=1.5`、
  `MaxImpactDistance=15`、`speed=18`，与客户端 `Fireball_SkillQ.asset` 逐值一致
  （EditMode `FireballAssetTableTests` 锁定）。
- `CastHandler.Decide` 校验落点有限且距施法者 ≤ `MaxImpactDistance`（否则 `impact-not-finite` /
  `impact-too-far`），冷却在**决策**时记录（空挥也冷却）。
- 判定 tick = `tick + HitDelayTicks + ProjectileTravelTicks`；`ProjectileTravelTicks` 按
  `ceil(距离 / speed × 15Hz)` 换算飞行延迟；非 `CircleAtPoint`、速度缺失或落点非有限时为 0。
- `ResolveStage` 在 `ImpactX/Y/Z` 处以 `HitResolver.ResolveCircle` 结算；落点即客户端松手上报的
  `ImpactX/Y/Z`，因此联机下客户端落点圈与服务端受击位置一致。

## 旋转多段判定（20002，2026-11-10）

- `CombatRules` 20002 带 `RepeatSpec`：首判 = 起手剪辑 0.2s + 0.2s，之后每 0.2s 一段，
  `MaxDuration` 3s，每目标最多 5 段（与客户端 `Spin_SkillR` 资产逐值一致，`SpinAssetTableTests` 锁定）。
- `CombatResolver.ResolveStage` 每段按当时的施法者位置与存活目标重新判定 2.5m 圆；
  `RepeatHitCounts` 每目标独立计数，**不走** `GroupHitIds` 的每施法一次去重。
- 施法者死亡/移除，或收到 `CancelCastRequest`，丢弃未到期段；复活后不会继续结算旧旋转。

## 攻击高度与跳跃规避（B1，2026-11-10）

- `CombatRules` 的地面近战（10001/10002 挥击、50001–50005）带 `AttackHeightSpec [-0.3, 1.2]`；
  `CombatCandidate.Height` 提供目标受击高度（玩家 1.8m、NPC = max(1m, bodyRadius×2)）。
- `ResolveSwing` 走高度重叠；剑气/火球/AOE 分支不传高度，保持纯 XZ。跳跃到 2m 时脱离地面近战体积。
- 玩家 Y 以客户端上报为候选；有 navmesh 时 `ResolveCombatHeight` 钳到 `[地面−1m, 地面+MaxJumpHeight]`
  （`Server3C.EnableMonsters` 接线 `GroundHeight`）。无导航宿主沿用客户端 Y。
  **信任面**：限幅只挡离谱高度，挡不住客户端始终上报可达跳高——与既有客户端权威位置模型一致，登记为方案 A 风险。

## 子模块 README

- [Monster](Monster/README.md) —— 行为树与权威怪物
- [Movement](Movement/README.md) —— ORCA 避让

## 测试

`Server/Server.Tests/`（xUnit）：AOI、战斗裁决、怪物、移动、协议、会话等。

```bash
# 官方 testhost（会拉起 VSTest testhost 子进程）
bash tools/test-server.sh
bash tools/test-server.sh --filter "FullyQualifiedName~Orca"

# 本机 workaround：testhost 以 Win32Exception(5) Access denied 退出时用 in-process runner
bash tools/test-server-inprocess.sh
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Navigation"
CONFIG=Release bash tools/test-server-inprocess.sh   # benchmark 数字用 Release
```

**为什么有第二个脚本**：本机（Windows 10 / .NET SDK 10）`dotnet test` 的 testhost 在
`Process.EnableRaisingEvents` 上抛 `Win32Exception(5): 拒绝访问`，测试一个都不跑。
`tools/ServerTestRunner` 用 xunit 自带的 `AssemblyRunner` 在**同一进程**里发现/执行
`Server.Tests.dll`（不经过 VSTest），输出与 `dotnet test` 等价的 PASS/FAIL 摘要。
它引用 `Server.Tests.csproj`，所以 `dotnet build` + runner 会复制全部依赖；
干净克隆可直接复现 321/321（修复轮后 348/348；P2 后 395/395）。脚本把 `TEMP` 指到仓库内
`.tmp/server-tests`，避免受限环境阻止测试写 `Path.GetTempPath()`。
