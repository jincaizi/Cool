# Movement —— 服务端 ORCA 避让

服务端负责**怪物 / bot 彼此避让**；玩家 vs 怪物的阻挡在客户端
[SyncLayer/NpcBodyCollider](../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)。
两半分开改，别只改一半。

## 文件

| 文件 | 作用 |
|---|---|
| `OrcaSolver.cs` | 2D ORCA 速度解算，纯算法，有论文对照注释 |
| `AvoidanceSystem.cs` | tick 快照、邻居筛选、接线 |
| `AvoidanceProbe.cs` | 最小两两间距采样（`--probe`） |

## 规则

- 参与者：怪物 + bot。玩家不参与（服务端改不了客户端权威速度）⇒ 对智能体而言玩家是**零速度障碍**，
  责任权重 **1.0**；智能体彼此 **0.5**。
- **邻居速度 = 上一 tick 的实际位移**（快照 `Position` 差分，米/tick），不再用 `Speed × 朝向`
  （路径跟随在拐角处朝向滞后于位移，会喂错方向）。玩家仍恒为零速度。新 id 首 tick 速度为 0；
  已移除实体的历史位置每 300 个快照清理一次。
- **不避让当前攻击目标**（`MonsterState.TargetId`），否则追击的怪会绕着玩家转、永远进不了 `attackRange`。
- 确定性：每 tick 开始按 id 排序拍快照（Jacobi）、逐个解算、统一应用。
- 半径真值：`Server/Config/monster_config.json` 的 `bodyRadius`；客户端有断言“从预制体量出的半宽 ≈ 配置值”。
- 调试开关：`--no-avoid`（负对照）、`--probe`（采样最小两两间距）。

## 护栏

- **无邻居时解算结果必须逐字等于期望速度**。这条护栏保证既有布局 / e2e 断言不受影响；
  动它要同步改“逐位相同”用例。
- 明确没做：空间哈希、静态障碍线（世界边界仍由 `BotRunner` 处理）、预测性避让。
- 纯局部避让：不解决被挤进死角。对称四体收敛会停在**最优环形**（半径 1/√2 ≈ 0.707m、
  相邻间距 = 半径和 1.0、零交叠）——几何上无法再靠近，停住是正确解，不是卡死；
  旧 `Speed × 朝向` 口径下这里是永久漩涡（最小间距 0.9497）。
- 出生点 `(-15, 0.3, -30)`（`PlayerSpawner`）；改点位或出生点时几何守卫会红——那是设计，不是噪音。
- 碰撞只在客户端：服务端不校验玩家与怪物重叠；会移动的怪可走进静止玩家（最坏约 0.48m）。

## 测试

`Server/Server.Tests/Movement/`：`OrcaSolverTests`、`AvoidanceSystemTests`、
`AvoidanceVelocityTests`（实际位移快照）。

`AvoidanceSystemTests.Converging_Agents...` 的末 tick 断言按实际位移语义更新：全体仍在动，或全体停在
最优环形上（不允许单体停住/其余在动的混合态，也不允许 60° 菱形 R≈0.866m）。

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~Orca"
bash tools/test-server.sh --filter "FullyQualifiedName~Avoidance"
```

## 相关

- [Server](../README.md)
- [SyncLayer](../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md) —— 客户端碰撞另一半
