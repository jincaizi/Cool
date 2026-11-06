# SyncCore —— 客户端 / 服务端共享的同步规则

纯 C#、零 `UnityEngine` 依赖；同一批源文件被客户端 asmdef 和 `Server/Server3C.csproj` 编译。
`Vec3` 是共享向量类型。

| 文件 | 职责 |
|---|---|
| `ClockSyncAlgorithm.cs` | 时钟同步：min-filter + 一阶低通；`Offset = 服务器时钟 − 客户端墙钟` |
| `MovementValidator.cs` | 移动合理性校验：瞬移门槛、速度钳制、纠偏目标 |
| `PredictionReconciler.cs` | 客户端预测帧对账；位置阈值 0.5m、旋转 5°、最多 60 帧 |
| `HitResolver.cs` | 服务端权威命中裁决；几何与客户端 `AttackShape` 对齐；锥形按目标半径扩展（圆-锥相交） |
| `CombatRules.cs` | 投射物判定种类与参数（剑气 / 火球 / 落点圆） |
| `Vec3.cs` | 共享向量 |

## 约束

- 不能引用 `UnityEngine`；两端同源编译，改规则必须两端测试同时通过。
- 非线程安全：`ClockSyncAlgorithm`、`PredictionReconciler` 由单消费者串行调用。
- 判定几何改动要同步客户端 `AttackShape` 与服务端裁决的测试。

## 测试

`Assets/Scripts/Tests/Editor/SyncCore/`：`ClockSyncAlgorithmTests`、`MovementValidatorTests`、
`PredictionReconcilerTests`、`HitResolverTests`、`CombatRulesTests`。

```bash
bash tools/unity-tests.sh EditMode HitResolverTests
```

## 相关模块

- [SyncLayer](../SyncLayer/README.md) —— 客户端使用方
- [服务端](../../../../../Server/README.md) —— 服务端使用方
- [Combat](../Combat/README.md) —— 客户端命中检测
