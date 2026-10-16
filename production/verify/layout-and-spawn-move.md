# 布局收紧 + 出生点第三次搬迁（2026-10-17）

任务：DemoDay 的怪物固定布局（`MonsterModule.SpawnDefaultLayout`）收紧收进陆地，出生点同批
从 `(10, 0.21, -40)` 搬到 `(-15, 0.3, -30)`。坐标由任务给定（实测选定）——**未自行改动任何数值**。
不变量（每个槽位到出生点的 XZ 距离 > 该模板「感知半径 + 巡逻半径」⇒ 出生点挂机不会被任何怪发现）不变。

---

## 1. 改了什么

### 1.1 服务端布局 —— `Server/Monster/MonsterModule.cs`（`SpawnDefaultLayout`）

| 槽位 | 模板 | 旧坐标 | 新坐标 | 距出生点 | 阈值（感知+巡逻） | 余量 |
|---|---|---|---|---|---|---|
| [0] | Wolf(2) | (12,0,0) | **(-30,0,0)** | 33.54 | 15+10 = 25 | +8.54 |
| [1] | Slime(1) | (40,0,0) | **(0,0,-20)** | 18.03 | 8+5 = 13 | +5.03 |
| [2] | Slime(1) | (-40,0,0) | **(0,0,-35)** | 15.81 | 8+5 = 13 | **+2.81（最小）** |
| [3] | Wolf(2) | (0,0,40) | **(10,0,-15)** | 29.15 | 15+10 = 25 | +4.15 |
| [4] | DogKnight(3) | (-20,0,20) | **(10,0,15)** | 51.48 | 18+6 = 24 | +27.48 |
| [5] | SpiderBoss(4) | (-40,0,-25) | **(25,0,20)** | 64.03 | 25+12 = 37 | +27.03 |

- 布局数组按给定重写，槽位顺序不变（[0..5] 模板序列仍是 2/1/1/2/3/4——`DefaultLayout_Carries_Six_Templates_In_Order` 绿）。
- 源码注释保留每槽位「距出生点 / 需 > 的阈值」，旧调试注释（对原点观察者的距离/感知）已按新坐标重算；
  其中「[0] 开局即感知原点观察者」这条**不再成立**，注释里就地改写成新事实并指向本报告（见 §5）。

### 1.2 玩家出生点 —— `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`（经 MCP 改，未手改 YAML）

- 对象：`PlayerSpawnPoint`（`PlayerSpawner._spawnPoint` 指向的 Transform，无父节点 ⇒ local == world）。
  **注意**：`_spawnPoint` 字段类型是 `Transform` 引用（不是 Vector3）——直接对着它打 pathPatches 只会改引用；
  实际改的是该 Transform 的 `localPosition`。
- 命令链：
  1. `gameobject-find {"gameObjectRef":{"name":"PlayerSpawner"}}` → 组件 `Hotfix…PlayerSpawner` 找到
  2. `gameobject-component-get`（`paths:["_spawnPoint"]`）→ 引用指向 `PlayerSpawnPoint`（场景里 `m_Name: PlayerSpawnPoint`）
  3. `gameobject-component-modify {"gameObjectRef":{"name":"PlayerSpawnPoint"},"componentRef":{"typeName":"UnityEngine.Transform"},"jsonPatch":{"localPosition":{"x":-15,"y":0.3,"z":-30}}}`
     → `[Success] was 10 → -15 / 0.2115555 → 0.3 / -40 → -30`（首次试 `m_LocalPosition` 被拒：Reflector 对 Transform 只暴露属性，改用 `localPosition`）
  4. `scene-save {"openedSceneName":"DemoDay"}` → 复核：`scene-list-opened` 报 `IsDirty:false`；
     `gameobject-component-get` 读回 `(-15, 0.3, -30)`；磁盘 YAML `git diff` 仅 1 行：
     `m_LocalPosition: {x: 10, y: 0.2115555, z: -40}` → `{x: -15, y: 0.3, z: -30}`
- y 取 0.3 的原因：该点地面实测 **0.256**（探针，见 §2）——0.3 让胶囊落在其上，重力自己落稳。

### 1.3 测试与文档同步（全部为坐标常量/注释，无增删用例）

| 文件 | 改动 |
|---|---|
| `Server/Server.Tests/Monster/MonsterModuleTests.cs` | `DemoSpawnPosition` → `(-15,0,-30)`（XML 注释补第三次搬迁一句：为什么、引本报告）；`Tick_With_No_Players_Patrols_Monsters` 巡逻中心 40→(0,-20)；`Detection_Aggro_And_Attack_Queue_Targets_Closest_Player` / `Target_Lost_Triggers_Return_To_Spawn_And_Peace_Reset` / `Attack_Decision_And_Action_State_Happen_In_The_Same_Tick` 的玩家夹具 `(0,0,0)`→`(-20,0,0)`（Wolf[0] 移到 (-30,0,0) 后面向原点，原点不再是它的感知锥内）；`Target_Lost…` 的"回巢"期望坐标 (12,0,0)→(-30,0,0)；两处 Slime 注释坐标 |
| `Server/Server.Tests/Monster/MonsterCombatIntegrationTests.cs` | 玩家夹具 `(0,0,0)`→`(-20,0,0)`；追击时长注释 45→35 tick |
| `Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs` | 顶注/观察窗注释里的旧布局表 → 新坐标；**点名 e2e 夹具语义变了**（[0] 不再开局感知，靠 [3] 巡逻进圈；wolfId 取"最先看到的 Wolf"存在假红风险，需起服复核） |
| `Assets/Scripts/Tests/PlayMode/HitJudgmentAlignmentTests.cs` | 注释"开局离出生点 40m" → 15.8m/[2]、18.0m/[1] |
| `docs/superpowers/guides/p5-monster-server.md` | 固定布局表坐标+行为列按新表重填；避让约束注改"出生点 (-15,0,-30)"；加 2026-10-17 第三次搬迁段（含 e2e 语义变化提醒） |
| `docs/superpowers/guides/webgl-demo-runbook.md` | 加 2026-10-17 搬迁段（挂机安全不变/最近怪换成 [2] Slime/80m AOI 波动消失） |
| `docs/superpowers/guides/network-playtest.md` | 两处（远程实体读数、开局无仇恨）各加 2026-10-17 更新行 |
| `docs/superpowers/guides/webgl-demo-boundaries.md` | 面试话术②后加"第三次搬迁更新这句"；"挂机多久会死"口径块加一句（口径不变） |
| `production/deploy/webgl-demo/start-game-server.cmd` | REM 块加 2026-10-17 第三次搬迁 3 行（新点、新最近怪、AOI 全落 80m 内） |
| `CLAUDE.md` | 「出生点」条：坐标 → `(-15, 0.3, -30)`（地面实测 y≈0.256；2026-10-17 第三次搬迁） |

历史文档**不动**（保留当时事实）：`docs/superpowers/specs/2026-10-12-*`、`docs/superpowers/plans/2026-09-12-*`、
`plans/2026-10-12-*`、`plans/2026-10-13-spawn-and-collision-report.md`、`production/verify/npc-anim-and-ground-fix.md`、
`production/verify/ground-probe.json`。

---

## 2. 探针复核（只读）：6 槽位 + 出生点向下 Raycast

命令：`script-execute`（body-only，主线程；从 y=200 向下、maxDistance 500）→ `console-get-logs` 读回：

| 点 | 模板 | 坐标 | 命中面 | 地面高度 y | 距出生点 | 阈值 | 判定 |
|---|---|---|---|---|---|---|---|
| [0] | Wolf(2) | (-30, 0) | **LowpolyTerrain** | +0.624 | 33.54 | >25 | 命中陆地 ✓ |
| [1] | Slime(1) | (0, -20) | **LowpolyTerrain** | 0.000 | 18.03 | >13 | 命中陆地 ✓ |
| [2] | Slime(1) | (0, -35) | **LowpolyTerrain** | −0.113 | 15.81 | >13 | 命中陆地 ✓（高于水面 −0.38） |
| [3] | Wolf(2) | (10, -15) | **LowpolyTerrain** | +0.162 | 29.15 | >25 | 命中陆地 ✓ |
| [4] | DogKnight(3) | (10, 15) | **LowpolyTerrain** | +0.372 | 51.48 | >24 | 命中陆地 ✓ |
| [5] | SpiderBoss(4) | (25, 20) | **LowpolyTerrain** | +0.490 | 64.03 | >37 | 命中陆地 ✓ |
| 出生点 | — | (-15, -30) | **LowpolyTerrain** | **+0.256** | 0 | — | 命中陆地 ✓（y=0.3 落在其上） |

**结论：7/7 全命中 `LowpolyTerrain`**，没有 Tree*/其它；没有湖底、没有出图。距离与阈值的关系与 §1.1 表一致
（这 7 个距离/阈值数字就是脚本里现算的，不是抄的）。

---

## 3. 三套验证命令与输出摘要

### 3.1 服务端单测

```
dotnet test Server/Server.Tests/Server.Tests.csproj
已通过! - 失败: 0，通过: 236，已跳过: 0，总计: 236，持续时间: 300 ms - Server.Tests.dll (net8.0)
```

条数不变（本次只改坐标与注释，未增删用例）。点名复核（`--filter` 逐条打印，全绿）：

- `New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn` **6 个 InlineData（slot 0..5）全绿**——用新坐标重算，未改阈值
- `Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster`（2700 tick = 3 分钟挂机零施法）绿
- `Template_AttackRange_Fits_Its_Combat_Rule`（4 个 InlineData）绿——未改
- 其余布局相关：`Tick_With_No_Players_Patrols_Monsters`、`Detection_Aggro_And_Attack_Queue_Targets_Closest_Player`、
  `Target_Lost_Triggers_Return_To_Spawn_And_Peace_Reset`、`Attack_Decision_And_Action_State_Happen_In_The_Same_Tick`、
  `MonsterCombatIntegrationTests.Npc_Casts_Are_Spaced_By_Cooldown_After_Resolver_Drain`、`AvoidanceSystemTests` 全绿

### 3.2 探针（见 §2）—— 7/7 LowpolyTerrain

### 3.3 Unity EditMode

```
unity-mcp-cli run-tool assets-refresh --input '{"options":"ForceSynchronousImport"}'   → Success
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}' --timeout 900000
Summary: Status=Passed, Total=386, Passed=386, Failed=0, Skipped=0 (00:00:05.8)
```

当时 **386/386**；没有 EditMode/PlayMode 用例断言旧出生点坐标（PlayMode 里两处只是注释，已更新）。
（二轮复跑：并行任务落地后基线涨到 **397/397** 全绿——见 §7.4。）

---

## 4. 全仓 grep 到的其它引用点（已核对/处理）

查法：`10f, 0f, -40f` / `10, ?0\.21` / `10,0,[-−]40` / `DemoSpawnPosition` / `_spawnPoint` / `PlayerSpawnPoint` / `湖东岸` / `出生点`。

| 位置 | 处理 |
|---|---|
| `CLAUDE.md`（出生点条） | 改坐标（见 §1.3） |
| `docs/.../guides/p5-monster-server.md`、`webgl-demo-runbook.md`、`network-playtest.md`、`webgl-demo-boundaries.md` | 已更新（见 §1.3） |
| `production/deploy/webgl-demo/start-game-server.cmd` | 已加 REM 行 |
| `Server/Server.Tests/…MonsterModuleTests.cs` / `MonsterCombatIntegrationTests.cs` | 已更新（见 §1.3） |
| `Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs` / `HitJudgmentAlignmentTests.cs` | 已更新（注释；见 §5） |
| `docs/superpowers/specs/2026-10-12-dogknight…-design.md`、`plans/2026-10-12-…`、`plans/2026-10-13-spawn-and-collision-report.md`、`plans/2026-09-12-p5-monster-server.md`、`production/verify/ground-probe.json`、`production/verify/npc-anim-and-ground-fix.md` | **历史文档/探针产物，保留原样**（记录当时事实） |
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` / `production/verify/DemoDay.unity`(未入库副本) | 前者已改；后者是历史副本，不动 |
| 其它命中（`ClientNetworkManager.SpawnPosition`、`Login.cs`、`Sys3CEntry`、`HitResolver`、`CombatSyncTests` 等） | 是"出生点"的通用词/登录协议字段，与服务端布局无关，未动 |

---

## 5. 疑虑 / 需要人工复核的点（均未自行"修"）

1. **e2e 观察者（原点）夹具语义变了** → **已在二轮修掉并实跑**（详见 §7：断言改为按"实际发起伤害的那只"
   归属、观察者站位改到 (5,-7.5)，无 bots 服务端 2/2 通过；40-bots 服务端失败的原因也查清了 = 人群挡怪）。
2. **正在跑的演示服务端**：01:22 重新构建、01:23:59 起在跑的是**含新布局**的构建
   （`Server/bin/Debug/net8.0/Server3C.exe`，`--monsters 4 --bots 40 --bots-radius 60`，PID 56268，我全程未动它）。
3. **落地高度差**：怪物根节点按 y=0 落位，探针地面高度
   [0]=+0.624 / [1]=0.000 / [2]=−0.113 / [3]=+0.162 / [4]=+0.372 / [5]=+0.490（旧槽位是 0.07–0.28）——
   [0] Wolf 处地面比根节点高 0.62m，实机上"脚埋进地面"可能更明显。
   （二轮状态：并行任务 e36a734「Npc 视图吸附到客户端地形面」已落地该方向；PlayMode 跑时仍见
   `[Sync] no ground under npc sample at (-40.00…` 警告 = 吸附在无地形处回落，属该任务的范畴。）
4. **AOI 读数**：六只怪全部落在出生点的 80m 视距内（最远 [5] 64.0m）⇒ 演示浮层"远程实体"不再有
   80m 边缘进出波动（旧口径 3↔4 / 5↔6 波动消失）。文档已按此更新；**未做实机复测**（需起服）。

---

## 6. 提交

- 一轮：`fe60f94 fix(demo): 布局收紧 + 出生点第三次搬迁到 (-15,0.3,-30)；挂机安全不变量仍成立`
  ——不变量（全六槽位距出生点 > 感知+巡逻）用新坐标重算后仍成立，服务端 236/236、EditMode 386/386、探针 7/7。
- 二轮（本 §7）：`test(demo): …`——`MonsterWorldTests` 断言归属 + 观察者站位 + 三处活文档就地改写。

---

## 7. 二轮返修（2026-10-17，审查发现 1 + 文档卫生项）

### 7.1 改了哪几行（`Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs`）

1. **断言归属**（审查指出的"假红"点）：`if (first.SourcePlayerId != wolfId.Value) return (false, "damage source X != wolf id Y")`
   → 取首个 `DamageResult` 的 `SourcePlayerId`，断言
   - 该 id 的视图存在且 `Kind==Npc`、`TemplateId==2`（仍是 Wolf，不是"随便一只怪都算"）；
   - 它出手时与观察者的 XZ 距离 ≤ 15m（`WolfDetectionRadius` = `Server/Config/monster_config.json` 模板 2
     `detectionRadius`，与既有的 dmg 15 同款硬编码 + 注释），且**采样期内最小距离**也 ≤ 15m
     （`wolfMinDist` 按视图 id 分别累积；原单值 `wolfMinDistToSpawn` 退役）。
   失败消息新增 `closestWolf=` 诊断。
2. **观察者站位（夹具）**：实测原点已不在任何怪的感知可达范围（§7.2 数据），只改断言归属仍会红。
   观察者改为经**真实移动上报链**（`ctx.Manager.AttachMovementReporter`）站到
   **`ObserverPosition = (5, 0, -7.5)`**：在 [3] Wolf (10,0,-15) 出生朝向（面向原点）的**正前方 9.0m**——
   15m 检测 + 90° 视锥（半角 45°）内，且落在它的巡逻方阵里（任何巡逻相位 60s 内都会被发现）；
   离两只 Slime 巡逻圈最近点 9.0m / 23m（> 8m 检测 ⇒ 不抢"伤害来源必须是 Wolf"）；
   [0] Wolf 远在 26m 外。首报 9m 位移不会被 `MovementValidator` 纠偏（瞬移门槛 = 50m）。

### 7.2 实跑结果（含"是否重启过服务端"）

| 环境 | 用例 | 结果 |
|---|---|---|
| **用户活服 8000**（`--monsters 4 --bots 40 --bots-radius 60`；**未重启、未杀进程**） | `MonsterWorldTests`（原点观察者，仅修断言） | **失败**：60s `casts=0`、`closestWolf=14.01` |
| 同上 | `MonsterWorldTests`（新站位 (5,-7.5)） | **失败**：60s `casts=0`、`closestWolf=8.56` |
| **临时实例 8001**（`--monsters 4`，无 bots；自起自停） | `MonsterWorldTests` | **2/2 通过**（19.1s，连跑两轮）——⚠️ 当时的端口改动只覆盖 2 处字面量（第 127/235 行），reconnect 的 phase 3（`ctxB.…`，第 268 行）**仍连的是用户 8000**（8001 日志里没有 `Reconnect_B`）⇒ 那两轮只有 **test 1 直接可证在 8001 上跑**；干净取证见 **§8.2**（三条会话全落 8001，2/2 通过） |
| 用户活服 8000 | `HitJudgmentAlignmentTests` | **2/2 通过**（2.3s） |

**失败原因（证据链）= 40 个 bot 的人群把狼挡在半路**——不是布局、也不是断言：

- 离线探针（临时 xunit 用例、跑完即删；**真实 MonsterModule、无 bots**；先空跑 40s 让狼巡逻，再登录静止玩家，
  观察 4 分钟）：

  | 观察者位置 | 登录后首次施法 | 攻击者 | 狼最小距离 | Slime[1] 最小距离 |
  |---|---|---|---|---|
  | origin(0,0) | 18.5s | [3] Wolf | 1.84m | 15.03m |
  | **(5,-7.5)** | **0.6s** | [3] Wolf | 1.82m | 9.03m |
  | (10,-10) | 14.5s | [3] Wolf | 1.77m | 10.04m |
  | (2,-7.5) | 1.4s | [3] Wolf | 1.97m | 1.47m（Slime 也进圈 ⇒ 弃用） |
  | demoSpawn(-15,-30) | 无（60s） | — | 20.18m | 11.20m |

- 同一观察者位置对比：无 bots ⇒ 0.6s 被打；40 bots ⇒ 60s 内狼最近只到 8.56m、零施法（ORCA 是**纯局部**避让，
  进不了人群——与 CLAUDE.md 已登记的"被挤进死角不解决"一致）。
- **结论**：这条 e2e 需要**不带 bots** 的服务端（`--monsters 4`）；`--bots 40/100` 的 AOI 演示配置下
  "挂机玩家被怪追上打"不成立（玩家自己走过去仍会被打——被挡的是"怪走向人"那一段）。已写进
  `webgl-demo-runbook.md` / `webgl-demo-boundaries.md` / `start-game-server.cmd`。
- **进程处置**：没有重启、没有杀用户的 8000 服务端；只为对照临时起了一个 8001 无 bots 实例
  （stdout 留档 `production/verify/layout-spawn-nobots-server.log`），跑完已停。指向 8001 时临时改过
  3 处端口字面量（带 `// TEMP-PORT` 标记），**已全部还原为 8000**（`grep -n "8000\|8001"` 复核）。

### 7.3 三处活文档的新正文（就地改写，旧坐标只留一句"历史"）

1. `docs/superpowers/guides/webgl-demo-runbook.md`（§4.1 起服务后）：
   "…出生点自 2026-10-17 第三次搬迁后是 **(-15, 0.3, -30)**（历史：08-27 原点系 → 10-13 湖东岸 (10,0.21,-40)
   → 现在是这里，布局同批收紧收进陆地），对全部六只怪都安全……离出生点最近的怪是 (0,0,-35) 的 [2] Slime：
   朝它走出约 3m 就进它的感知圈被追（[0] Wolf 现在距出生点 33.5m，巡逻圈够不着）……"（末尾附 §7.2 的 bot 实测一段）
2. `docs/superpowers/guides/network-playtest.md`：
   ① 远程实体读数 → "出生点 2026-10-17 第三次搬迁到 (-15,0.3,-30)（历史：10-13 湖东岸 (10,0,-40)）……
   `--monsters 4` 稳定 4、`--monsters 6` 稳定 6，不再有 80m 边缘波动"（旧 3↔4 / 5↔6 波动降为"旧读数留档"）；
   ② 开局无仇恨条 → "出生点 (-15,0,−30)（历史：10-13 湖东岸、更早 (10,0,−15.52)）……最近的 (0,0,-35) [2] Slime
   15.8m > 8+5……朝它走约 3m 进感知圈被追"。
3. `production/deploy/webgl-demo/start-game-server.cmd`（REM 块）：出生点/避让段与开屏取景段整体改写成当前事实
   （新点、最近怪 = [2] Slime、AOI 稳定 6、t8/t13 截图拍于旧点 ⇒ 需重新取景），并加一段 bot 实测警告。

（另外 `docs/superpowers/guides/webgl-demo-boundaries.md` 的两处话术/口径块也一并就地改写：面试话术改成
"2026-10-17 又搬了一次 (-15,0.3,-30)……朝 (0,0,-35) 走约 3m 就进那只 Slime 的感知圈"；演示建议表新增
"bot 人群会把怪挡在半路"一行。）

### 7.4 当前基线复核（二轮时点）

- 服务端：`dotnet test Server/Server.Tests` → **246/246**（基线 246 = 一轮时的 236 + 并行任务
  `982196c feat(combat): 服务端格挡减伤` 的 10 条；本改动不增删用例——临时诊断用例已删除，`git diff` 无用例增删）
- Unity EditMode：**397/397**（并行任务落地后的新基线；本改动不涉 EditMode 逻辑）
- PlayMode：`MonsterWorldTests` 无 bots 服务端 **2/2（干净取证见 §8.2：三条会话全落在 8001、用户 8000 无活动）**；
  活服 8000（40 bots）因人群失败（见 §7.2）；`HitJudgmentAlignmentTests` 活服 8000 2/2

---

## 8. 三轮回修（2026-10-17，复审：取证补正 + 两处一行修正）

### 8.1 复审指出的取证缺陷（承认 + 原因）

上一轮把测试指向 8001 时，替换的字符串是 `ctx.Manager.ConnectAndLogin("127.0.0.1", 8000);`——
它只命中第 127/235 行两处，**没命中 `ctxB.Manager.ConnectAndLogin(...)`（第 268 行，reconnect 的 phase 3）**。
所以那两轮里 reconnect 用例的 phase 3 连的是**用户活服 8000**（8001 日志里确实没有 `Reconnect_B` 会话），
"8001 上 2/2 通过"当时只有 **test 1** 直接可证。§7.2/§7.4 的表述已按此改正。

### 8.2 干净取证（本轮，可审计）

1. 起临时实例：`./Server/bin/Debug/net8.0/Server3C.exe 8001 --monsters 4`（**无 bots**；
   stdout 留档 `production/verify/layout-spawn-nobots-server2.log`；**用户 8000 全程未动、未重启**）。
2. 临时把**全部 3 处**端口字面量改成 8001（带 `// TEMP-PORT` 标记）：

   | 行号 | 语句 | 覆盖 |
   |---|---|---|
   | 127 | `ctx.Manager.ConnectAndLogin("127.0.0.1", 8000);` | test 1 `Monsters_Patrol_Chase_And_Attack_Server_Authoritative` |
   | 235 | `ctx.Manager.ConnectAndLogin("127.0.0.1", 8000);` | test 2 phase 1 `Reconnect_A` |
   | 268 | `ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);` | test 2 phase 3 **`Reconnect_B`**（上一轮漏掉的那处） |

3. `assets-refresh` → `tests-run {"testMode":"PlayMode","testClass":"MonsterWorldTests"}` →
   **Passed：2 passed / 0 failed，11.8s**。
4. 8001 服务端日志里的**全部会话名**（逐条）：
   - `[02:07:46] Login attempt: Monster_Observer from session 2` → test 1
   - `[02:07:54] Login attempt: Reconnect_A from session 3` → test 2 phase 1
   - `[02:07:57] Login attempt: Reconnect_B from session 4` → test 2 phase 3（本次确在 8001）
   对照组：用户 8000 的 stdout（`block-server.log`）在 02:05–02:09 窗口**没有任何 login/连接行**。
5. 还原：3 处字面量全部改回 8000（`grep -n "ConnectAndLogin(\"127\|TEMP-PORT\|8001"` 复核：3 行全是 8000、
   无 `8001`/`TEMP-PORT` 残留）；临时实例已停（8001 无监听；用户 8000 的 PID 56268 全程在跑）。

**两条用例各自的结论**：
- test 1 `Monsters_Patrol_Chase_And_Attack_Server_Authoritative`：**通过** ✓（无 bots、`--monsters 4`）
- test 2 `Reconnect_Monster_World_Persists`：**通过** ✓，且三条会话全落在 8001（可审计，不再是"连到别处"）
- 与**旧布局**的对照：**未对照**——需要回滚 `MonsterModule.SpawnDefaultLayout` 并重建服务端，本轮没做。
  可见的替代口径：该用例只要求"phase 1 看到的某个 Wolf 视图在重连后仍在 AOI 内"，与布局几何无关
  （新布局两只 Wolf 距原点 30m/18m，都在 80m AOI 内）；旧布局下同样成立。

### 8.3 两处一行修正（+ 一处同族算术自查）

1. `docs/superpowers/guides/p5-monster-server.md`（布局表下的注，第 37 行）：把"`MonsterWorldTests`
   （PlayMode e2e，观察者原点）需起服复核"就地改写为当前事实——观察者已改站 `(5,-7.5)`、断言按
   "实际发起伤害的那只 Wolf"归属、无 bots 服已实跑通过、`--bots 40` 的人墙是活服失败原因。
2. `Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs`（`ObserverPosition` 注释）：
   Slime 巡逻圈最近点 `9.0m / 23m` → **`8.46m / 22.95m`**（8.46 = 13.46 − 5：到巡逻**圆**的最近点，
   不是到最近**路点**的距离；" > 8m 检测"的结论不变）。
3. （同族自查，一并修）同一注释里"且落在巡逻方阵内（任何巡逻相位都会在 60s 内被发现）"不准确——
   (5,-7.5) 在方阵**外**（西北边 p3(0,-15)→p0(10,-5) 外侧 2.5m）。改为："离狼巡逻方阵西北边 2.5m，
   狼沿那条边走向 p0 时正对着它（离线探针含 40s 预跑相位：0.6s 内被感知、狼进到 1.82m；巡逻一圈 ~20s
   ⇒ 60s 覆盖多圈）"。
