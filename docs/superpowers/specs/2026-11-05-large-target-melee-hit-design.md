# 大体型怪物近战命中修复（锥形判定计入目标半径）——设计

日期：2026-11-05
运行模式：联网（Editor 客户端 + 本地新构建服务端 `Server3C`）
相关模块：[SyncCore](../../../Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md)、
[SyncLayer](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)、
[Combat](../../../Assets/Scripts/Hotfix/GameSystems/Combat/README.md)、
[服务端怪物](../../../Server/Monster/README.md)

## 1. 背景

联网模式下玩家普攻蜘蛛 Boss 出现「偶尔命中，站位不同结果不同」。用户怀疑是怪物体型放大后碰撞体没有同步放大。

排查结论：**不是碰撞体尺寸问题**。

- 客户端网络视图的身体碰撞体由 `NpcBodyCollider` 从 `SkinnedMeshRenderer` 的世界包围盒生成，
  蜘蛛预制体根节点的 2× 缩放会被 `lossyScale` 除法吸收（`NpcBodyCollider.BuildCollider`）。
  该碰撞体只负责挡住本地玩家，不参与命中判定。
- 服务端命中裁决在 2026-11-04 已改为「表面距离」，且 `CombatCandidate.Radius` 已由
  `PlayerState.BodyRadius` 带入（蜘蛛 `bodyRadius = 1.74`）。射程不再按目标中心点计算。
- 仍然按中心点计算的是**锥形方向判定**：`HitResolver.InCone` 只比较施法者朝向与
  「施法者 → 目标中心」方向的夹角。玩家贴在大体型目标侧面、面朝切线方向挥击时，
  目标中心方向可能超过半锥角（60°），但目标身体外缘实际落在挥击扇形内，于是判不中。
  2026-11-04 的设计文档 §4.4「近似与残余」已登记这一限制。

这与「正面能中、侧面/背面看站位」的现象一致。

客户端 `NetworkTargetSelector` 同样只用中心点做射程/锥形校验，因此贴脸时（例如 10001
射程 2.0m、玩家被身体碰撞体挡在中心距约 2.2m 处）会清掉锁定环。虽然服务端会回退到
自动目标，但锁定表现会闪烁，且目标面板会消失。

## 2. 目标与非目标

**目标**

- 大体型目标贴侧身时，只要身体外缘进入攻击扇形且最近表面在射程内，服务端判定命中。
- 半径 0（玩家目标）的行为逐值不变，PvP 不受影响。
- 客户端锁定/选中环使用与服务端同口径的体表距离与锥角，贴脸时不丢锁。

**非目标**

- 不改单机离线链路：`ConeShape` / `RectShape` / `CircleShape` 等客户端攻击形状保持现状
  （蜘蛛不在单机场景）。
- 不改玩家半径（保持 0）、不改命中延迟、动画判定帧、伤害数值。
- 不修部署副本 `production/deploy/webgl-demo/dist/server/Config/monster_config.json`
  缺失 `bodyRadius` 的问题（该目录被 `.gitignore` 忽略，属部署同步事项，单独跟进）。
- 不做物理 LOS、不做自动面向目标、不做技能形状的客户端预测。

## 3. 设计

### 3.1 服务端：圆 vs 锥的精确相交

`HitResolver` 是双端共享的纯几何文件，服务端在 `CastHandler.ResolveStage` 调用
`HitResolver.Resolve`。改动集中在 `InCone`：

```csharp
// 现有：只比较中心方向
return (forwardX * dx + forwardZ * dz) / dist >= halfCos;

// 新：半径 <= 0 时保持上面的快路径；半径 > 0 时按目标圆的角半径扩展
if (!(radius > 0f)) return (forwardX * dx + forwardZ * dz) / dist >= halfCos;
if (dist <= radius) return true;                       // 施法者已在目标身体内
float angle = (float)Math.Atan2(
    Math.Abs(forwardX * dz - forwardZ * dx),
    forwardX * dx + forwardZ * dz);                    // [0, π]
float alpha = (float)Math.Asin(Math.Min(1f, radius / dist));
return angle <= halfAngleRad + alpha;
```

要点：

- `halfAngleRad = shape.Angle * 0.5 * π / 180`；`shape.Angle` 是全角，与现有语义一致。
- 半径 0 走原快路径，避免 `Atan2/Asin` 的浮点差异改变既有边界用例（PvP 逐值不变）。
- `radius` 为 NaN 或负数时 `radius > 0` 为 false，同样落回快路径，不会放大判定。
- `dist < 0.0001` 的零距离退化保持现有 `return true`。
- 直接用 `Atan2(|cross|, dot)` 而不是 `Acos(dot/dist)`：在 0 和 π 附近更稳定。
- 射程判定保持 `max(0, dist - radius) <= shape.Range`。本次只改角度，不改距离语义。
  当身体侧对时，扇形内实际相交点可能比最近表面点更远，但以当前近战数值
  （蜘蛛半径 1.74、射程 2.0、中心距约 2.16）相交点约 0.51m，远小于射程；
  这是既有「表面距离」语义的有界近似，不阻塞本次修复。
- 显式目标（`targetId > 0`）与自动目标共用同一个 `InCone`，两条路径都传 `c.Radius`。

几何效果：

| 场景 | 中心角 | 当前 | 修复后 |
|---|---|---|---|
| 目标半径 0，中心在 90° | 90° | 不中 | 不中（半径 0 快路径） |
| 目标半径 1.74、中心距 2.16，中心在 90° | 90° | 不中 | 命中（90° ≤ 60° + 53.7°） |
| 目标半径 1.74，中心在正后方 180° | 180° | 不中 | 不中（上限 113.7°） |
| 目标半径 1.74，中心距 ≤ 半径 | — | — | 命中（施法者在身体内） |

### 3.2 客户端：锁定/选中环同口径

1. `NpcBodyCollider` 增加 `public float BodyRadius { get; private set; }`，在
   `BuildCollider` 合并完 SMR 世界包围盒后赋值：

   ```csharp
   BodyRadius = Mathf.Max(world.size.x, world.size.z) * 0.5f;
   ```

   蜘蛛约 1.66m（服务端配置 1.74 略大；锁定用的半径略小，但玩家被身体碰撞体挡在
   中心距约 2.16m，差异落在不可达区间内，不影响可玩性）。无 SMR 时保持 0。

2. `RemoteEntityView` 增加 `public NpcBodyCollider? BodyCollider { get; internal set; }`
   与 `public float BodyRadius => BodyCollider != null ? BodyCollider.BodyRadius : 0f;`。
   `RemoteEntityRegistry.BuildView` 在 `model.AddComponent<NpcBodyCollider>()` 后回填。
   玩家视图与球体占位没有该组件，半径恒 0。

3. `NetworkTargetSelector`：
   - `Candidate` 增加 `public readonly float Radius`，构造函数参数默认 0，既有测试
     不需要改签名。
   - `BuildCandidates` 用 `view.BodyRadius` 填充。
   - `FilterCandidates`：
     - 射程：`surface = Mathf.Max(0, centerDist - Radius)`，`surface > acquireRange` 才排除；
     - 锥角：`effectiveHalf = Mathf.Min(180, halfAngleDeg + Asin(clamp(Radius/centerDist)) * Rad2Deg)`；
     - 排序：按**表面距离**升序，与服务器自动目标「按表面距离取最近」同口径。
   - `IsWithinSkillShape`：射程同样用表面距离；`omnidirectional`（AOE 圆）只查半径；
     非全向时用同一 `effectiveHalf` 公式。
   - `Cycle` 继续用 `AcquireRange` / ±90°，只是候选的半径参与筛选与排序。

## 4. 数据流

1. 玩家普攻 → `SkillCastReporter` → `CastRequest`（形状 + `TargetPlayerId`）
   → 服务端 `CastHandler.Decide` → `ResolveStage`
   → `BuildCandidates`（`BodyRadius` 来自 `PlayerState.BodyRadius`）
   → `HitResolver.Resolve` → `InCone(radius)` 圆-锥相交 → `ApplyHit`。
2. 客户端快照 → `RemoteEntityView`（NPC 视图上挂着 `NpcBodyCollider`）
   → `BodyRadius` → `NetworkTargetSelector.FilterCandidates` / `IsWithinSkillShape`
   → `LockedTargetId` 随下一次 `CastRequest` 上行。
3. 服务端与客户端选择器使用同一套数值语义：表面距离 + 圆-锥角扩展。

## 5. 边界与降级

| 情况 | 处理 |
|---|---|
| 目标半径 0 / 缺失 / NaN / 负数 | 走原中心点快路径，行为与修复前一致 |
| 施法者已在目标圆内（中心距 ≤ 半径） | 命中（角度无定义时的保守取真） |
| 目标在正后方 180° | 不命中（60° 半锥 + 最大 90° 角扩展 = 150°） |
| NPC 视图没有 `NpcBodyCollider`（球体占位/目录未命中） | `BodyRadius = 0`，锁定行为回退到修复前 |
| 锁定目标死亡 / 出 AOI / 超距 | 既有清锁逻辑不变 |
| AOE 圆技能 | 方向不参与；半径只影响射程（表面距离） |

## 6. 测试与验收

### 6.1 共享几何（两份测试副本同步）

`Server/Server.Tests/SyncCore/HitResolverTests.cs` 与
`Assets/Scripts/Tests/Editor/SyncCore/HitResolverTests.cs`：

- `Side_Target_With_Radius_Is_Hit_By_Angular_Reach`：原点、yaw 0（+Z）、
  `Single range 2.0 / angle 120`；目标中心 `(2.16, 0)`、半径 1.74 → 命中。
- `Side_Target_Without_Radius_Is_Missed`：同一位置、半径 0 → 不中。
- `Target_Directly_Behind_Is_Missed_Even_With_Radius`：目标 `(0, -2.16)`、半径 1.74 → 不中。
- `Explicit_Target_Uses_Angular_Reach`：`targetId` 指向侧面大目标 → 命中；半径 0 → 不中。
- 既有半径 0 用例全部保持绿色（回归）。

### 6.2 服务端集成

`Server/Server.Tests/Combat/CombatResolverNpcTests.cs`：

- `Player_Melee_Hits_Large_Npc_From_Side`：大半径 NPC 位于施法者侧面 90°、
  中心距 2.16m，10001 命中并扣血；同一位置换成半径 0 的候选不扣血。
  覆盖 `BuildCandidates` 的半径携带与 `ResolveStage` 的完整链路。

### 6.3 客户端

`Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs`：

- `FilterCandidates` 保留半径 1.74、中心距 2.2、射程 2.0 的候选；半径 0 时排除。
- 侧面 90°、半径 1.74、半角 60 的候选保留；半径 0 时排除。
- 排序按表面距离：较远中心距的大目标可以排在较近中心距的小目标之前。
- 既有半径默认 0 的用例保持绿色。

`Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs`：

- 假 SMR bounds `(2, 3, 1)` → `BodyRadius == 1.0`。
- 真蜘蛛预制体：`BodyRadius` 用测试侧独立推导的 `max(world.size.x, world.size.z) * 0.5`
  断言（不抄实现常量）。

### 6.4 命令

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HitResolver"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolver"
bash tools/unity-tests.sh EditMode HitResolverTests
bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests
bash tools/unity-tests.sh EditMode NpcBodyColliderTests
```

### 6.5 手工 E2E（联网）

1. Editor 连本地新构建的 `Server3C`，走到蜘蛛 `(18.333, 18.389)`。
2. 贴在蜘蛛侧面，面朝与「玩家→蜘蛛中心」约 90° 的切线方向挥普攻 → 出红字伤害。
3. 面朝正后方挥普攻 → 不出伤害。
4. 贴脸时目标选中环/目标面板不消失。
5. 打 DogKnight 等小体型怪物：正面命中行为与修复前一致。

## 7. 风险与兼容

- **侧身命中更宽松**：目标半径越大，允许的中心角越大（60° 半锥 + 最大 90°）。
  对 3.5m 宽 Boss 贴脸的情形，身体确实环绕到玩家侧后方，判定符合「剑碰到身体」；
  若实战手感过宽，可再讨论给角扩展加封顶，但本轮按精确圆-锥相交实现。
- **自动目标可能选中侧面大怪**：排序仍按表面距离，与既有「表面距离取最近」一致；
  大怪表面更近时会优先被选中，属预期。
- **客户端半径略小于服务端**：`NpcBodyCollider` 用实际网格半宽 1.66，服务端配置 1.74。
  由于玩家被挡在中心距约 2.16m，差异落在不可达区间；只影响锁定筛选的极窄边界。
- **部署副本配置**：`dist/server/Config/monster_config.json` 缺 `bodyRadius`，本次不修。
  若用旧 DLL/旧配置部署，服务端仍可能按中心距判定；发布前必须重新生成或同步该目录。
- **仍未验证的变量**：如果修复后仍有随站位变化的漏判，下一步在判定帧打印
  施法者位置/朝向、目标中心/半径、中心角与表面距离，确认是否是 0.3s 判定延迟期间
  朝向变化导致；本轮不做日志改动。

## 8. 决策记录

- 选择「圆 vs 锥精确相交」而不是「自动面向目标」或「显式目标免检」：
  保持服务端权威几何、不改变操作手感、PvP 半径 0 逐值不变。
- 客户端锁定同步使用表面距离与角扩展，而不是维持中心点：
  否则贴脸时 10001 会清锁，选中环闪烁，且与服务端判定口径不一致。
- 射程语义保持「最近表面距离」，不改为「扇形内相交点距离」：
  既有 2026-11-04 设计已确定表面距离，本轮只补角度；当前数值下差异有界且远小于射程。

## 9. 验收清单

- [ ] `HitResolver` 两份测试副本新增用例通过，既有半径 0 用例不回归。
- [ ] `CombatResolverNpcTests` 侧面大目标命中、半径 0 不命中。
- [ ] `NetworkTargetSelectorTests` 与 `NpcBodyColliderTests` 新增用例通过。
- [ ] 手工 E2E：侧面命中、正后方不中、贴脸不丢锁、小怪行为不变。
- [ ] README 同步更新（见 §10）。

## 10. 文档同步

按 `CLAUDE.md`「改动模块行为时同步更新 README」：

- `Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md`：命中裁决说明补
  「锥形判定计入目标半径（圆-锥相交）；半径 0 行为不变」。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`：目标锁定改用
  体表距离 + 目标半径角扩展；`NpcBodyCollider` 暴露 `BodyRadius`。
- `Server/Monster/README.md`：命中条目补「锥形方向按目标半径扩展」。
