# 验收报告：出生点搬移（Part A）+ 怪物视图碰撞体（Part B）

日期：2026-10-13　分支：master（两个提交，按 Part 分开）　基线：Unity EditMode 296/296、服务端 162/162
结果：**EditMode 302/302**（296 + 6 新用例）、**服务端 166/166**（162 + 理论用例 2→6 槽位的 +4）

---

## Part A —— 出生点搬到「挂机不会被任何怪发现」的地方

### A0 实机验证候选点位（先验证，再落笔）

用户给的候选 `(0, 0, -40)` **实机不成立**：那片地在水下。证据（Unity 内 Raycast + 场景扫描）：

```
--- raycast down at candidates (scene: DemoDay) ---
  (10,-15.52) hit=True y=0.161 collider=MeshCollider onGO='LowpolyTerrain'   ← 旧出生点
  (0,-40)     hit=True y=-0.874                                          ← 候选点：地面在水面之下
  (10,-40)    hit=True y=0.212
  (-10,-40)   hit=True y=0.016
--- water ---
  Water pos=(0.00, -0.38, 0.00) scale=(10,10,10) rendererBounds Extents=(50,0,50) hasCollider=False
```

即：水面 y=-0.38、无碰撞体，而 `(0,-40)` 的地面是 **-0.874（水下 0.49m）**。
另做了 2.5m 网格扫描（`groundY / waterMargin / minConstraintMargin / flatness` 四列），
在满足六槽位距离约束的点里挑了**离用户候选点最近、又干又平**的一个：

**新出生点 = `(10, 0.2115555, -40)`**（同 z、东移 10m，湖东岸台地）

| 槽位 | 模板 | 位置 | 到新出生点 | 需要（感知+巡逻） | 余量 |
|---|---|---|---|---|---|
| [0] | Wolf(2) | (12,0,0) | 40.05 | > 25 | +15.05 |
| [1] | Slime(1) | (40,0,0) | 50.00 | > 13 | +37.0 |
| [2] | Slime(1) | (-40,0,0) | 64.03 | > 13 | +51.0 |
| [3] | Wolf(2) | (0,0,40) | 80.62 | > 25 | +55.6 |
| [4] | DogKnight(3) | (-20,0,20) | 67.08 | > 24 | +43.1 |
| [5] | SpiderBoss(4) | (-40,0,-25) | 52.20 | > 37 | +15.2 |

地面高度 y=0.2115555 取自射线实测；脚下方圆 r=0.5（CC 半径）采样 8 点的高度差 **0.029m**（很平）、**高于水面 0.59m**（干燥）。
视线：出生朝向 +Z（identity 旋转，正对原点）——[0] Wolf 在正前方 40m，DogKnight 左前 27°/67m，SpiderBoss 左侧 73°/52m。

**AOI 副作用（如实记）**：新点距 [3] Wolf 80.62m，**恰在 80m 视距外 0.6m**（`AoiGrid.SightRadius=80`，按 XZ 距离判）。
所以 `--monsters 4` 开屏看到 3 只（不是 4）、`--monsters 6` 看到 5 只；走近一点它就进视野。`network-playtest.md` 已按此改写。

### A1 场景改动（MCP，不手改 YAML）

```
gameobject-component-modify (PlayerSpawnPoint/Transform, pathPatch position)
  was: (10.00, 0.16, -15.52) → new: (10.00, 0.21, -40.00)
scene-save
```

**字节级证据**（`git diff Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，**只有一行**）：

```diff
-  m_LocalPosition: {x: 10, y: 0.1609524, z: -15.52}
+  m_LocalPosition: {x: 10, y: 0.2115555, z: -40}
```

**视图级证据**（保存后经 Unity 读回，两者一致）：

```
gameobject-component-get Transform.position → {x: 10, y: 0.211555496, z: -40}
PlayerSpawner._spawnPoint 仍指向同一 Transform（fileID 1037136103，未动）
```

### A2 服务端几何守卫测试（TDD，RED→GREEN）

`Server/Server.Tests/Monster/MonsterModuleTests.cs`：
1. **RED（先改测试、后改常量）**：`[Theory]` 从 `[4]/[5]` 扩到 **全部 6 个槽位**，常量仍是旧出生点 → 2 条红：

```
[FAIL] New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn(slot: 0)
   槽位 [0] 模板 2 距出生点 15.6m ≤ 感知+巡逻 25m——挂机玩家会被它开局追杀
[FAIL] Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster
   Assert.Empty() Failure: Collection was not empty  Collection: [50002, 50002, ...]   ← Wolf 真的来打了
```
2. **GREEN**：`DemoSpawnPosition = (10, 0, -40)`；行为侧用例从"只盯 50003/50004"扩成 **6 只怪、2700 tick（3 分钟，与活体验证同窗口）、零施法**，并改名 `Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster`。
   `MonsterModule.SpawnDefaultLayout` 的注释同步重写（每个槽位的新距离、全六槽位达标、旧点的排除历史）。

```
已通过! - 失败: 0，通过: 22，总计: 22（MonsterModuleTests）
```

### A3 文档口径 sweep（用事实说话）

| 文件 | 改动 |
|---|---|
| `production/deploy/webgl-demo/start-game-server.cmd` | 删掉"别原地挂机 / 调小数量躲不开 / 十几秒起"整段；改为「新点对六只怪全有余量、挂机 3 分钟 0 伤害」+「**离开出生点朝原点走约 15m** 就进 [0] Wolf 感知圈」+ 开屏取景的新方位 |
| `docs/.../webgl-demo-boundaries.md` ② | 主动接的话术改成"出生点安全 **但地图不保证安全**" |
| 同上「演示操作建议」两行 | `--monsters` 不是安全开关（安全与数量无关）；出生点安全**只管出生点** |
| 同上「约 7 秒」注 | 改为"**站在出生点不会死**（实测 0 伤害）；**离开出生点**才回到旧的危险里" |
| `docs/.../webgl-demo-runbook.md` §4.1 | 同上口径 |
| `docs/.../p5-monster-server.md` 布局表 | 补 [0] 的避让距离；避让约束注改为"出生点 (10,0,-40)、全六槽位达标" |
| `docs/.../network-playtest.md` | 开局仇恨段重写；`远程实体` 计数按新点改（4→3、6→5，含 80.6m 的解释） |
| `docs/plans/2026-10-12-dogknight-spiderboss-monsters.md` | 历史文档**不改正文**，在"已知限制"块下加一行 **2026-10-13 更正**（②已不成立；③客户端已加碰撞体） |

### A4 活体验证（起服 + PlayMode 挂机 3 分钟以上）

方式：`dotnet run --no-build --project Server/Server3C.csproj -- 8000 --monsters 6` →
`EditorApplication.EnterPlaymode()`（**注**：MCP 的 `editor-application-set-state` 本次会话里点了 Play 但 `IsPlaying` 不起作用，
改用脚本调 `EnterPlaymode()` 才进去）→ 玩家按新出生点生成 → **不做任何输入**，用探针读 `Sys3CEntry.CurrentHp / IsDead / position / ClientNetworkHost.IsInWorld`：

```
[T13IDLE] t=10.3  inWorld=True remotes=6 pos=(10.00, 0.31, -40.00) hp=100.0/100 dead=False grounded=True
[T13IDLE] t=90.5  inWorld=True remotes=5 pos=(10.00, 0.31, -40.00) hp=100.0/100 dead=False grounded=True
[T13IDLE] t=146.1 inWorld=True remotes=6 pos=(10.00, 0.31, -40.00) hp=100.0/100 dead=False grounded=True
[T13IDLE] t=201.8 inWorld=True remotes=5 pos=(10.00, 0.31, -40.00) hp=100.0/100 dead=False grounded=True
[T13IDLE] t=262.2 inWorld=True remotes=6 pos=(10.00, 0.31, -40.00) hp=100.0/100 dead=False grounded=True
```

- **4 分 22 秒（t=10→262）满血未阵亡**，位置一动不动、y 稳定在 0.31（地面 0.21 + CC 偏移，没有下坠/卡住）。
- 客户端日志 **0 条 cast broadcast**（怪物一次攻击广播都没发生）。
- 截图：`production/verify/t13_spawn_open.png`（进世界开屏，含选人浮层）、`production/verify/t13_spawn_idle_3min.png`（挂机 3.5 分钟后，站在干燥的湖东岸草地上、HUD 满血）。
- 跑完已停服（`tasklist | grep server3c` 为空、8000 端口无监听）。

---

## Part B —— 怪物视图加碰撞体（纯客户端）

### B1 排查：客户端还有哪些物理查询会撞上它

`grep Physics.` 全量清点（`Assets/Scripts`），逐个判定：

| 查询 | 掩码/取法 | 判定 |
|---|---|---|
| `SkillExecutor` 命中/AOE（3 处 `OverlapSphereNonAlloc`） | `shape.TargetMask`（技能资产可配）+ **`GetComponentInParent<IEffectTarget>()` / `TryGetComponent<IEffectTarget>` 筛目标** | **免疫**：怪物视图没有 `IEffectTarget` → 命中也被丢弃。已写成用例 `Body_Collider_Is_Rejected_By_Attack_Detection_Filters`（顺带断言查询**确实看得见**这个碰撞体——"看不见"不是防线，组件筛选才是） |
| `PhysicsRegistry.FindNearby` | `LayerMask.GetMask("Character"/"Monster")`，**这两个层在 TagManager 里不存在 → 掩码=0**（实测 `NameToLayer` 返回 -1），物理路径本来就返回空；补充路径走注册表 `IDamageable` | 免疫（同上） |
| 攻击形状 `CircleShape/ConeShape/SectorShape/RectShape` | 同上 `GetComponentInParent<IDamageable>` | 免疫 |
| `SkillDashComponent.CheckCollision` | `SphereCast(..., GetMask("Default","Wall","Floor"))`，"Wall/Floor" 不存在 → **掩码只含 Default(0)** | 碰撞体在层 10 ⇒ **看不见**，冲刺的提前停止判定行为不变（物理上仍会被 CC 挡住，这是期望） |
| `SkillFreezeEffector` | 无掩码 `OverlapSphere` + `TryGetComponent<IEffectTarget>` | 免疫（组件筛选） |
| `ThirdPersonCameraController` 防穿墙 | `SphereCastAll(..., ~0)` | **会命中**（保持现状，见 B4 取舍） |
| `GroundDetector` | 只读 `CharacterController.isGrounded` | 只在"玩家站到怪头顶"时才会为真——是实心碰撞体的正常物理后果，不影响平地判定 |
| 玩家自己的 `PlayerHitZone`（trigger）与怪物身上无 trigger | `other.GetComponent<IAttackHitbox>()` | 免疫（怪物视图无该组件） |

**结论/取舍**：
1. 攻击与地面这条线**结构上免疫**（组件筛选 + 掩码不含），所以没有为它们加掩码；已用测试钉住。
2. **只在接触层做隔离**：新碰撞体放在独立层 `NpcBody`，层矩阵**只放行本地玩家层**。
3. **相机保留命中**：`CameraCollisionMask = ~0`（场景里序列化的就是 4294967295），怪体会被相机防穿墙算作遮挡物——
   与该段代码自述的意图一致（"贴墙/**被怪包围**会穿模，PC 上尤其刺眼"），且它不是逻辑判定。
   实测可见代价：贴着 Boss 时相机会被拉近（见 `t13_boss_blocked.png`）。若不要这个行为，改相机掩码一行即可（未做，避免越界）。
4. **静态碰撞体、无 Rigidbody**：怪物视图位置由快照插值写，物理不能反过来推它；也意味着**怪朝你走过来时挡不住它**（它会穿进你身体，CharacterController 只会被刚性体/CC 推）。实测见 B5 第一次尝试。

### B2 实现

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcBodyCollider.cs`（新文件，registry 在真模型分支 `AddComponent`；预制体保持纯美术）：

- **尺寸从模型推**：合并实例里全部 `SkinnedMeshRenderer.bounds`（世界空间）→ `InverseTransformPoint` 得本地中心、除以 `lossyScale` 得本地尺寸 →
  **竖直棱柱 `BoxCollider`**（size = XZ 跨度 + Y 跨度）。
  *为什么不是胶囊*：胶囊的**穹顶**会被 CharacterController 顺坡爬上去——**实测玩家真的站到了 Boss 头顶**（`player=(-19.89, **2.58**, -29.12)`，中心距 0）。
  棱柱侧面竖直（90° > slopeLimit 40°）爬不上、顶是平的；尺寸仍然完全从模型来（DogKnight 1×、SpiderBoss 2× 共用一个公式）。
- 挂在子物体 `NpcBody`（**不动美术层的 layer**，渲染剔除与视觉零风险；Hierarchy 里可认名字），**层 = 10**，`isTrigger=false`。
- 用**层索引常量**（`BodyLayer = 10`）而不是 `LayerMask.NameToLayer("NpcBody")`：见 B3 的 Unity 层名缓存坑。

`ProjectSettings/TagManager.asset`：第 10 层命名 `NpcBody`。
`ProjectSettings/DynamicsManager.asset`：层矩阵 `NpcBody(10) × 其余 31 层全关，只放开玩家层 9`。
**为什么是层 9**：两个玩家预制体（`FemalePlayer.prefab`、`MaleCharacterPBR.prefab`）的根与 `CharacterController` 都在第 9 层（实测），
且它们的 `Preview_*` 变体同样在 9——层号由用例钉住（改了层，`Body_Collider_Collides_Only_With_The_Local_Player_Layer` 会红）。

矩阵字节级证据（`DynamicsManager.asset` 的 `m_LayerCollisionMatrix` 128 字节解码）：

```
row10 collides with: [9]            ← 只与玩家层
所有其它行仅多了一个"忽略 10"的位（对称性检查：无不对称对）
```

### B3 测试（TDD，RED→GREEN）

`Assets/Scripts/Tests/Editor/SyncLayer/NpcBodyColliderTests.cs`（6 条）：

| 用例 | 断言 |
|---|---|
| `Monster_View_Gets_A_Body_Collider_Sized_From_The_Model` | 假预制体 bounds (2,3,1) → `BoxCollider.size == (2,3,1)`、非 trigger、子物体名叫 `NpcBody`、层 = 10、中心 = bounds 中心 |
| `Body_Collider_Is_Sized_From_Each_Real_Monster_Prefab` | **两个真预制体**：测试侧独立推导期望尺寸（合并 SMR bounds ÷ lossyScale）与实现对比（1e-2 容差）——钉住"随模型" |
| `Body_Collider_Collides_Only_With_The_Local_Player_Layer` | `Physics.GetIgnoreLayerCollision(10, l)`：l=9 必须 false、其余 31 个必须 true；TagManager 第 10 层名字 = `NpcBody`（**读工程设置文件**，理由见下）；两个玩家预制体根/CC 都必须在层 9 |
| `Human_Player_View_Still_Gets_No_Collider` | 真人视图分支：`NpcBodyCollider` 与任何 `Collider` 都不能多出来（回归） |
| `Monster_Prefabs_Stay_Collider_Free` | 两个怪物预制体资产仍 0 个碰撞体、且不挂 `NpcBodyCollider`（纯美术契约） |
| `Body_Collider_Is_Rejected_By_Attack_Detection_Filters` | `OverlapSphere(…, ~0)` 能命中新碰撞体，但 `IEffectTarget`/`IDamageable`（自身与 `GetComponentInParent`）全为 null |

RED→GREEN：实现前 4 条红（无组件/无碰撞体/无层/查询未见），实现后 6 条全绿；`Human_*`/`Prefabs_*` 两条**实现前后都绿**（回归护栏）。

**踩到的两个非显而易见的坑（都已在代码/测试里注释）**：
1. 项目 `m_AutoSyncTransforms: 0` ⇒ 新建/移动碰撞体后，物理查询要**显式 `Physics.SyncTransforms()`** 才看得到新位姿（测试里补了）。
2. **Unity 2022.3 的层名缓存与工程设置文件会不一致**：用 `SerializedObject` 改 TagManager 后，`LayerMask.NameToLayer` 当场是对的，
   但**下一次 Unity 自己保存工程设置（进 PlayMode 时实测）会把文件覆盖回旧值**——"内存正确、磁盘坏掉"的假绿。
   最终做法：**关掉 Unity 后直接改 `ProjectSettings/TagManager.asset` 的 `layers` 列表**（一行、git diff 可验），再启动；
   之后进 PlayMode、退出、重开都保持（逐次验证）。测试也因此**读文件**而不是读 Unity 的层名缓存。
   代码侧同时用**索引常量**，不依赖层名——即使层名被谁清掉，隔离逻辑仍按层 10 生效。

### B4 完整回归

```
Unity EditMode：302/302（296 基线 + 6 新用例）
服务端 xunit：166/166（162 基线 + 理论用例 2→6 槽位的 +4）
```

### B5 手感验证（起服 + PlayMode，真的朝 Boss 走过去）

方式：PlayMode 内驱动本地 `CharacterController.Move`（与游戏移动同一条物理路径）朝**服务端那只真 Boss** 走，
探针每 0.5~1s 读：玩家位、Boss 位、**中心距**、`Collider.ClosestPoint` 距离（判是否进到体积里）、HP。

**第一次尝试（胶囊版）发现的真问题**：撞上后玩家**顺着胶囊穹顶爬上去站到了 Boss 头顶**
（`player=(-19.89, 2.58, -29.12)`、中心距 0.00）——那不是"挡住"。→ 换成竖直棱柱（B2），重测：

**碰撞体开（phase1，推 6s）**：

```
[T13B3] phase1 start dist=7.00 bossRadiusWorld=1.66 bossCol=on matrix(10,9 ignore)=False hp=100
[T13B3] ON t=0.0 player=(-42.74, 0.19, -23.72) boss=(-49.47, 0.00, -25.60) dist=6.99 cpDist=5.59 hp=100
[T13B3] ON t=1.0 player=(-45.58, 0.27, -24.51) boss=(-47.45, 0.00, -25.03) dist=1.94 cpDist=0.54 hp=80
[T13B3] ON t=2.0 player=(-45.58, 0.27, -24.51) boss=(-47.45, 0.00, -25.03) dist=1.94 cpDist=0.54 hp=80
[T13B3] ON t=3.0 player=(-45.58, 0.27, -24.51) ...  dist=1.94 cpDist=0.54 hp=60
[T13B3] ON t=5.0 player=(-45.58, 0.27, -24.51) ...  dist=1.94 cpDist=0.54 hp=40
[T13B3] PHASE1 END(碰撞体开) minCenterDist=1.94
```

→ **被挡住**：连续 5 秒全力推、位置一动不动，中心距停在 **1.94m ≈ 棱柱半宽 1.66 + CC 半径 0.5 − skin 0.09**，
y 保持 0.27（没有再爬上头顶）。截图 `production/verify/t13_boss_blocked.png`。

**负对照（phase2，碰撞体关，同样推 5s）**——证明这套测量"能失败"：

```
[T13B3] OFF t=1.0 player=(-47.45, 0.31, -25.03) boss=(-47.45, 0.00, -25.03) dist=0.00 cpDist=0.00
[T13B3] OFF t=3.0 player=(-45.82, 0.29, -25.02) boss=(-45.81, 0.00, -25.02) dist=0.01 cpDist=0.00   ← 已经在 Boss 身体里/穿到另一侧
[T13B3] PHASE2 END(负对照,碰撞体关) minCenterDist=0.00
```

截图 `production/verify/t13_boss_passthrough.png`：玩家**站在蜘蛛 Boss 的身体里面**（腿围在人四周）。
跑完已停服。

### B6 已知代价（如实写进文档）

`docs/superpowers/guides/webgl-demo-boundaries.md` 新增 **第 ⑪ 条**（并同步 `p5-monster-server.md` 客户端一节）：

> **能穿过怪/Boss 吗？**——**现在挡得住，但这是纯客户端判定**：服务端不模拟玩家移动、也不校验重叠，
> **改客户端就能穿模**（与伤害不同：伤害是服务端掷骰，客户端算不出来）。
> 怪物视图的碰撞体在运行时按模型包围盒建（`NpcBodyCollider`），只与本地玩家层碰撞。

另有两条**实测到的边界**（写在报告里，未粉饰）：
- **怪朝你走过来时挡不住它**：碰撞体是静态的（无 Rigidbody），Boss 会走进/穿过玩家体内；只有玩家主动撞怪时才是"墙"。
- 服务端纠偏路径（`CharacterController.ApplyServerPosition` / `NetMovementPolicy` 的平滑拉回）**直接写 transform、绕过物理**；
  如果服务端位置与本地不同步，它可以把玩家"拉"过碰撞体（本次实测的第一次胶囊尝试里出现过一次中心距 0 且 y 不变的可疑样本，
  之后用静止靶 + 逐步探针复现并确认了真实原因是**穹顶可攀爬**，与纠偏无关；此处把机制记下来供后续排查）。
