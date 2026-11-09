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

### NPC 共享目录与动作时间（2026-11-08）

`Npc/` 包含可由 Unity JsonUtility 序列化的 `[Serializable]` 公共字段 DTO：
`NpcCatalogDefinition`、模板、碰撞、AI、技能绑定/定义、表现定义；不依赖 Unity 或平台资源包。
`NpcCatalogValidator.Validate` 返回错误列表，拒绝非有限数值、非法引用/距离带、零长度阶段、
非递增 activeOffsets 及 >=2^31 的累计时间窗。`NpcTickMath` 提供环绕安全比较；tick0 合法。

`NpcCatalogHasher.Compute` 先校验，再将模板/技能/表现/绑定的副本按稳定 ID 排序，写入
版本化 little-endian UTF8 语义流并计算 SHA256 小写十六进制；不修改作者列表，排除 ContentHash，
不接收平台二进制、绝对路径或导出时间。浮点 +/-0 归一化；所有影响运行的 DTO 字段纳入 hash。

`NpcCatalogDefaults.Create()` 仅提供既有4模板/6技能的迁移作者种子，保留 Wolf 逻辑名与显式
placeholder，0.35s 前摇按15Hz向上取6 tick。碰撞尺寸/阶段映射为待校准初值，`Approved=false`；
不能将种子或校验通过解释为人工批准，不允许正式 ActionV1 启动缺产物时 fallback。
正式发布审批、平台 BuildHash/资源新鲜度由 Editor 发布门禁负责。

霸体为 `NpcWindupArmorOverride.Inherit/Interruptible/Armor`；模板默认在
`Ai.DefaultWindupArmor`，SpiderBoss 种子默认霸体，普通怪默认可打断。
NPC CombatRules 接线由服务端从共享发布定义注册；玩家规则及几何保持原有语义。

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
