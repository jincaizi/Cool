# 报告：ORCA 2D 速度解算器（第一步：纯算法 + 单测）

日期：2026-10-13　分支：master
交付：`Server/Movement/OrcaSolver.cs`（新）、`Server/Server.Tests/Movement/OrcaSolverTests.cs`（新）
测试：`dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~OrcaSolverTests" --nologo -v q`
结果：**服务端 166 → 179 全绿**（13 条新用例，零回归）

---

## 1. 实现了什么

按论文（J. van den Berg, S. J. Guy, M. Lin, D. Manocha, "Reciprocal n-Body Collision
Avoidance", ISRR 2011）自己实现，**没有 vendor/移植 RVO2 源码**：半平面用「锚点 + 单位外法线」
表示（论文的 `(v − point)·n ≥ 0`），锥的几何、两条锥腿的法线、LP1/LP2/LP3 都是自己推的式子，
注释里给了推导依据。

对外 API（3 个小结构体 + 1 个解算器，全在 `OrcaSolver.cs`）：

| 成员 | 说明 |
|---|---|
| `OrcaAgent` | 自身：位置/速度/半径/maxSpeed/**期望速度 v_pref**（XZ） |
| `OrcaNeighbor` | 邻居：位置/速度/半径/**责任权重 α** |
| `OrcaVelocity` | 结果：解出的 XZ 速度 |
| `OrcaSolver(float timeHorizon, float timeStep)` | τ 与 Δt；非线程安全（持有复用缓冲） |
| `Solve(in OrcaAgent, ReadOnlySpan<OrcaNeighbor>)` | 邻居顺序 = 约束顺序（确定性） |
| `LastSolveUsedFallback` | 诊断：本次是否走了 LP3 回退 |

**没有**做的事（按 YAGNI，留给后续任务）：没有空间哈希、没有预测性避让、没有 k-d 树、
没有静态障碍线（`numObstLines` 相关分支整块省略——世界几何由调用方以邻居形式喂进来）、
没有接线到 `SimpleMoveSystem` / `BotRunner`。

## 2. 算法与论文量的对应

记号（代码注释里同名）：`p = p_neighbor − p_self`、`v_rel = v_self − v_neighbor`、
`r = r_self + r_neighbor`、`τ = timeHorizon`、锥顶点 `p/τ`、锥帽半径 `r/τ`。

| 论文 | 代码 | 说明 |
|---|---|---|
| VO 截断锥的 `u`、`n` | `BuildOrcaLine` | 未重叠：最近点要么在**锥帽**（`u = (r/τ − |w|)·ŵ`，`n = ŵ`），要么在**锥腿**（过原点、与锥帽相切的直线：`n = (−r·p̂ ∓ leg·p̂⊥)/|p|`，`u = −(v_rel·n)n`）。分支判据 `(w·p)² > r²|w|² ∧ w·p < 0` —— 等价于「v_rel 在帽圆上的径向投影落在**朝向原点的弧段**上」，我按几何独立推过一遍并用数值例子核对 |
| —— | 同上 else 分支 | 已重叠（`|p| ≤ r`）：τ 锥退化，改用 **Δt** 做顶点/帽半径（`p/Δt`、`r/Δt`），保证一个步长内被推开 |
| §3.2 责任分摊 α | `OrcaNeighbor.Responsibility` | 半平面锚点 = `v_self + α·u`。**α 是参数**，不是常数：0.5 = 互惠（双方各让一半），1.0 = 对方不参与（我们全责）。提供常量 `ReciprocalResponsibility` / `FullResponsibility` |
| 附录 A `linearProgram1` | `LinearProgram1` | 单条边界线上求极值（满足已处理约束 + `|v| ≤ maxSpeed` 圆）；t 区间由圆的判别式 + 各约束收缩而来 |
| 附录 A `linearProgram2` | `LinearProgram2` | 增量求解：从「maxSpeed 圆上离 v_pref 最近的点」出发逐条检查，违反就在该线上重求；失败返回失败约束下标 |
| 附录 A 回退 | `LinearProgram3` | **约束集不可行**时最小化**最大违反量**：把 `lines[0..i)` 投影到违反约束 i 的边界线上构造降维 LP，再沿线 i 的外法线方向最大化 |

**命名坑（给后来者）**：任务里写的 `linearProgram1..4` 是 **3D 表述**的编号；**2D 表述只有三段**
（LP1 单线 / LP2 增量 / LP3 最小化最大违反量）。同一套东西在 RVO2-3D 里叫
`linearProgram3`（增量，返回 `planeFail`）+ `linearProgram4`（最小化最大违反量）。本实现按 2D 编号，
类注释里写明了这个对应关系，没有为了凑「4」造一个冗余例程。

其它工程约束：
- **无邻居恒等**：`neighbors.Length == 0` 且 `|v_pref| ≤ maxSpeed` 时**逐位**返回 `v_pref`
  （LP2 的第一个分支直接 `result = optVelocity`，不经过任何算术）。超速时只做一次均匀缩放。
- **无分配热路径**：邻居走 `ReadOnlySpan`，内部两个约束缓冲随实例复用（只在邻居数变大时扩容），
  全程无 LINQ、无 List、无闭包。
- **确定性**：没有随机数，邻居顺序 = 约束顺序；同一输入必得同一输出（有用例钉住）。
- **退化保护**：只在一个真正可达的地方加了守卫——已重叠分支里 `|w| < 1e-6`（相对速度恰好等于
  `p/Δt`，或位置完全重合且相对速度为零）时**不产生约束**，而不是编一个方向或让 NaN 污染位置。
  另一处（既有重叠分支）我确认过数学上除零不可达，就没写多余的守卫。

## 3. 七条验收测试的实际输出

仿真设定：`dt = 0.05 s`、`τ = 4 s`、半径 `0.5`（合并 1.0）、`maxSpeed = 1.5`。
下表的数字是**实测值**（临时探针打印后删除，探针文件不在提交里）。

| # | 用例 | 关键实测值 | 判定 |
|---|---|---|---|
| 1 | `No_Neighbors_Returns_Preferred_Velocity_Bit_Exact` | v = (1.2345678, −0.7654321)（逐位相等） | ✅ |
| 1b | `No_Neighbors_Clamps_Overspeed_...` | v_pref (3,4)、maxSpeed 2.5 → (1.5, 2)（精确） | ✅ |
| 1c | `Non_Threatening_Neighbor_...` | 50m 外邻居 → v_pref 逐位不变 | ✅ |
| 2 | `Head_On_Agents_...`（12s） | **最小间距 +0.0003**（相切掠过，不穿入）；A 走到 x=+12.95、B 走到 −12.95（**双双穿过**，各带 0.5m 侧向错位）；\|v\|max = 1.5 | ✅ |
| 2b | `Squeezed_Agent_...`（12s，中间人 + 两个不同速冲锋者） | **fallback=True**（非重叠几何下也走到 LP3）；最小间距 **+0.0001**；中间人被推开到 (−0.66, 1.18)；无再次穿入 | ✅ |
| 3 | `Crossing_Paths_...`（12s，两条垂直线同时到交点） | **最小间距 0.0000**；两人都穿过交叉点（12.57, −0.32）/（0.71, 12.95）；末速回到期望方向（\|Δv\| < 0.05） | ✅ |
| 4 | `Stationary_Non_Participating_Obstacle_...`（10s，α=1） | 轨迹最小间距 **+0.0001**；**每个 tick 解出的速度对障碍的 τ 视界最小间距 ≥ −0.01**（α=1 的半平面与速度障碍相切 ⇒ 整段 τ 无冲突，这是与实现无关的行为断言）；绕过障碍后继续前进到 (9.80, −1.00) | ✅ |
| 5a | `Overlapping_Agents_Separate_Without_Jitter`（初始间距 0.2，共线） | **fallback=True**（首帧就断言了）；间距由 −0.80 回到 **+0.000**（恰好分离到相切）；分开后**无再次穿入** | ✅ |
| 5b | `Overlapping_Agents_With_Lateral_Offset_Pass_Each_Other`（错位 0.19m） | 间距 −0.808 → **+15.2**（互相穿过）；无再次穿入；两人末速均 > 0.75（没僵住） | ✅ |
| 6 | `Stationary_Agent_Yields_...`（8s） | 最小间距 **+0.0001**；停住的人被让到 (0.10, 0.50)（侧向 0.5m），来者侧向让到 z = −0.50（**两边都在让，不是双双不动**） | ✅ |
| 7 | `Solved_Speed_Never_Exceeds_Max_Speed`（5 体混合：对撞+交叉+重叠+障碍） | **\|v\|max = 1.5000**（300 tick 内从未越界）；无 NaN | ✅ |
| + | `Fully_Coincident_Agents_Do_Not_Produce_NaN` | 完全重合 + 相对速度为零 → 无 NaN、不越界、逐位返回 v_pref | ✅ |
| + | `Solve_Is_Deterministic_For_Identical_Input` | 两次独立解算逐位相同 | ✅ |

容差说明（怕被质疑太松）：穿入容差取 **0.02 m**。实测的正确实现全程最小间距是
**+0.0000 ~ +0.0003**（即几乎精确相切），容差是噪声的约 60 倍；而"坏掉"的量级是
**−0.5 ~ −2.0**（满穿入 = −2.0；下面两个变异实验分别是 −0.50 和 −0.88 ~ −0.98），
所以判别力充足——不是为了放水，是为了跨平台浮点/离散化留余量。

## 4. 变异实验（实际做过的两个，都还原了）

**变异 1：责任权重写死成 0.5**（`u * other.Responsibility` → `u * ReciprocalResponsibility`）

- 结果：**第 4 条红**（`Stationary_Non_Participating_Obstacle_Is_Never_Entered`）：
  `有解出的速度会在 τ 内撞上不参与的障碍：最小视界间距 -0.502568`（半责只让一半 ⇒ 0.5m 的穿入）。
  其余 12 条全绿。
- **过程中的一个真发现**：第一版第 4 条只看**轨迹**间距，变异 1 **逃逸了**——因为下一 tick
  重新解算又把位置纠回来了（轨迹最小间距仍 ≥ −0.02）。把断言换成「**每个 tick 解出的速度**
  对 α=1 邻居的 τ 视界无冲突」之后才抓住。教训：责任权重这类"当帧约束强度"的参数，只测轨迹是测不到的。

**变异 2：`LinearProgram3` 换成直接返回 `v_pref`**

- 结果：**第 5 条的两条用例红**（`Overlapping_Agents_Separate_Without_Jitter`：穿入被加深到 −0.975；
  `Overlapping_Agents_With_Lateral_Offset_Pass_Each_Other`：−0.88）。其余 11 条绿。
- **第 2 条（常规对撞）不红**，这跟任务里的预期（"第 2/5 条应红"）不一致，原因是可证明的：
  对称对撞时那条半平面的切线**恰好过原点**（我按几何算的 `point·n = 0`），所以 `v = 0` 永远可行
  ⇒ LP2 不会失败 ⇒ 根本不进回退。我拿 5 组候选构型（对称夹击 / 不对称夹击 / 左右夹 / 窄缝障碍 /
  三人挤中心）扫过一遍验证：**非重叠 + 单邻居在 α ∈ {0.5, 1} 下必然可行**，不可行来自
  接触/被挤的构型。为了让"非重叠也能覆盖回退路径"有个钉子，我补了 **2b（被两侧冲锋者挤住）**：
  实测 `fallback=True` 且最小间距 +0.0001，正是任务里说的"已经被挤在中间"。
  诚实标注 2b 的判别力边界：它钉的是**回退被触发的路径**与挤压结果（穿入/叠住会红），
  对变异 2 **不红**——因为被挤者自己那半边约束仍然生效，轨迹还能救回来；真正对"回退算得对不对"
  敏感的是 5a/5b。

## 5. 全量计数

- 基线：`166/166`（改动前实测）
- 改动后：**`179/179`**（166 + 13 条新用例），`dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q` 全绿
- 新文件零编译警告（既有 warning 噪声未动）

## 6. 疑虑 / 已知边界（都写进了代码注释）

1. **完全重合且相对速度为零**：几何上不存在"该往哪边让"的答案，本实现**不产生约束**（退回 v_pref）。
   好处是不产生 NaN；代价是两个完全叠在一起、速度也完全一样的智能体会一直叠着。真实刷怪不会
   是精确同点（浮点上必有差），有差就正常分离（测试 5a/5b 覆盖）。若将来真需要，属于后续接线任务
   的 tie-break 决定，不该藏在解算器里。
2. **共线重叠收在"相切站住"**：测试 5a 的两体完全共线（`det(p, w) = 0`）时，ORCA 的推挤是纯轴向的，
   结果是两人贴住、速度趋 0（实测最低 −0.80 → 0.000，间距恰好 = 合并半径）。这是该退化构型的
   固有行为，不是抖动也不是发散；偏 0.1m 就是正常互相穿过（5b）。要不要给"贴住"加人为甩开是
   玩法决策，本任务不擅自加论文外的东西。
3. **容差 0.02m** 的取舍见 §3 末尾；判别力已用两个变异证明。
4. **`Responsibility` 是"自身对该邻居"的权重**（不是对方的属性）。接线时注意：玩家/不可控对象
   作为邻居要传 `FullResponsibility`，怪物互相之间传 `ReciprocalResponsibility`。
5. 解算器**非线程安全**（复用缓冲 + `LastSolveUsedFallback`）；服务端主循环单线程调用即可，
   真要并行得每线程一个实例。
6. 本次只到算法层：没有接线、没有时间加速/空间哈希（明确不做，属后续任务）。

---

# 报告：ORCA 避让**接线**（第二步：世界接线 + 半径数据源 + 活体对照）

日期：2026-10-13　分支：master
交付：`Server/Movement/AvoidanceSystem.cs`（新）、`Server/Movement/AvoidanceProbe.cs`（新）、
`Assets/Scripts/Tests/Editor/SyncLayer/MonsterBodyRadiusTests.cs`（新，客户端侧）、
`Server/Server.Tests/Movement/AvoidanceSystemTests.cs`（新，8 用例）、
改动 `MonsterConfig.cs` / `monster_config.json` / `ConfigLoader.cs` / `SimpleMoveSystem.cs` /
`MonsterState.cs` / `MonsterModule.cs` / `BotRunner.cs` / `Server3C.cs` / `Program.cs` /
`OrcaSolver.cs`（仅类注释加一节"不要加沿 v_pref 拉满"的告诫）
测试：`dotnet test Server/Server.Tests/Server.Tests.csproj` → **179 → 187 全绿**；
Unity EditMode（`unity-mcp-cli run-tool tests-run`）→ **303 → 305 全绿**（零回归）

## 1. 半径数据源（Part 0）

服务端原本没有"身体半径"概念。补一条真值链：`MonsterData.bodyRadius`（json + ConfigLoader 保底表
同值），客户端侧由 EditMode 用例**反向**守住。四模板的取值与来源：

| 模板 | bodyRadius | 来源 |
|---|---|---|
| 1 Slime | 0.5 | 客户端目录未接线 ⇒ 退化为 `CreatePrimitive(Sphere)` 单位球（半径 0.5，用例另钉住这条） |
| 2 Wolf | 0.5 | 同上 |
| 3 DogKnight | **0.88** | 本次实测：`DogKnightMonster.prefab` 合并 SkinnedMeshRenderer **世界** bounds 的 X 跨度 1.7535 ÷ 2 = 0.8768 |
| 4 SpiderBoss | 1.66 | 同法：世界 X 跨度 3.3236 ÷ 2 = 1.6618（根上 2× 缩放已折进世界尺寸），与上一轮审查的 3.3/2 对齐 |

DogKnight 的实测方法（与 `NpcBodyCollider.BuildCollider` **同一把尺子**）：Unity MCP
`script-execute` 里 `PrefabUtility.InstantiatePrefab` → 合并子物体 SMR 的 `Renderer.bounds` →
取世界 X 跨度的一半。机器人半径 `BotRunner.BotRadius = 0.5`（对应客户端 `CreatePrimitive(Capsule)`
占位体，代码注释里写明了来源）；玩家半径 `AvoidanceSystem.PlayerRadius = 0.5`（两份玩家预制体
`CharacterController.m_Radius` 实测值）。

**客户端一致性断言**：`MonsterBodyRadiusTests` 读仓库相对路径的 `Server/Config/monster_config.json`，
对目录里有真模型的模板量预制体、对没模型的模板量占位球，逐一比对（容差 0.1m）。
RED→GREEN 都有实测：先写用例（json 无字段 ⇒ `bodyRadius=0` ⇒ 红），再补 json ⇒ 绿。
**过程中它抓到了我自己的一个错误**：半宽一开始除回了 `lossyScale`（那是本地尺寸，服务端要的是世界米），
SpiderBoss 量出 0.83 vs 服务端 1.66 ⇒ 红，改回世界 X 跨度后绿——这条断言确实在干活。

## 2. 接线（Part 1）

`AvoidanceSystem`（新的世界接线层）：
- **快照**：`EnsureSnapshot(tick)` 从 EntityStore 重建"位置+速度+半径+是否参与"（每 tick 一次，
  按 id 排序 ⇒ 约束顺序只由世界状态决定）。Server3C 在主循环 tick **起始处**调它，机器人 tick 与
  怪物 tick 都对着同一份解算（Jacobi）；MonsterModule 独立运行时（单测/无 Server3C 宿主）在自己
  Tick 顶部再叫一次（同 tick no-op）。
- **速度单位 = 每 tick 位移**（米/tick），τ 同单位。这样"期望速度"就是各移动系统原本要走的位移，
  **没有邻居时逐字返回**，积分算式与接线前逐位相同。
- **责任权重**：怪物/机器人 0.5，真人玩家 1.0（玩家速度用其上报的 `Speed` + 朝向推，是"会动的障碍"）。
- **两个系统**：`SimpleMoveSystem.MoveTo`（期望位移 → 解算 → 积分；邻居提供者由 MonsterModule 注入）
  与 `BotRunner.Tick`（同样接；**边界反弹改按意图方向判**——用解出的位移判界会让贴着墙、被邻居
  往外推的 bot 把方向翻反）。攻击目标 `MonsterState.TargetId` 在解算里排除。

### 与任务规格的三处偏离（都有实测支撑，建议照此定稿）

1. **距离筛不是固定 1~2m，而是 `r_i + r_j + 1.5m + (|v_i|+|v_j|)·τ`**。
   固定 1.5m 余量实测**不够**：8m/s 的两个 bot 正面对撞时接触前 1~3 tick 才看见对方，
   解出来的速度来不及让开——实测最小间距 **0.3455**（半径和 1.0，即穿 0.65m），收敛用例 **0.9004**。
   后一项就是"τ 内双方能接近的最大距离"（速度与 τ 都是每 tick 单位，量纲自洽）。
   代价可忽略：n²/2 次比较本来就要做，空旷处 10m 筛程内的期望邻居数 ≈ 0.05。
   改完：迎面 0.2361→**1.0785**（无穿模）、玩家用例视界 0.5120→**1.0000**（恰相切）。
2. **τ = 20 tick（≈1.3s）**，不是算法单测里的量级。理由同上：本服务器的离散化很粗
   （每 tick 位移 0.53m vs 受保护距离 1.0m），视界短了每个 tick 的修正就过冲。RVO2 默认量级 2~2.5s。
3. **两个确定性 tie-break**（都写在 `AvoidanceSystem` 里，解算器不动）：
   ① 位置**精确相同** ⇒ 按 id 取 8 罗盘方向之一的 1e-4 偏移（任务规格要求的那条）；
   ② 正面对撞**横向完全对称**（p 与相对速度共线到浮点噪声级）⇒ 沿 p 的**固定手性**垂线偏 1e-4。
   ②是实测逼出来的：不处理时"走哪条锥腿"完全由噪声决定，实测两个相向的 bot 会挑到**同一绝对侧**，
   横向互相抵消、径直对穿（最小间距 0.2361；连狼追路人也会在 7m 处僵住）。手性固定 ⇒
   双方的偏移在绝对坐标里相反 ⇒ 各让一侧（迎面组实测 0.0271 → 1.0785）。

### 实现中修掉的两个真问题（都是测试逼出来的）

- **筛程不能只看当前速度**：刚生成/刚停下的智能体当前速度是 0 ⇒ 筛程退化成 2.5m ⇒ 前方 8m 的
  不动玩家被筛掉，首 tick 直冲过去（视界检查 0.0）。改成 `max(当前速度, 期望速度)`。
- **tie-break ② 的接近判据符号写反过一次**（`v_rel = v_self − v_other` 顺着 p 才是接近 ⇒ 点积 > 0），
  写反时整个 tie-break 静默失效（测试全绿但现象照旧）——是拿轨迹打印逐 tick 对出来的。

## 3. 验收测试（8 条）与变异实验

`AvoidanceSystemTests`：

| # | 用例 | 钉住什么 |
|---|---|---|
| 1a | `No_Neighbors_Monster_Movement_Is_Bit_For_Bit_The_Unwired_Formula` | 接线前公式（抄进测试）vs 接线后，**300 tick 逐位相等** |
| 1b | `No_Neighbors_Bot_Movement_Is_Bit_For_Bit_The_Unwired_Runner` | 同种子两套 BotRunner（无避让 / 有避让但无邻居），**位置逐位相等**；并断言快照非空（防"空转假绿"） |
| 2 | `Converging_Agents_Do_Not_Overlap_And_Still_Get_To_The_Point` | 四体奔向同一点：最小间距 ≥ 0.9（实测 0.9497，见下）、都回到 3m 内、末 tick 都还在动 |
| 3 | `Player_Is_Never_Moved_Server_Side_And_Never_Penetrated` | 玩家位置**逐位不变** + 轨迹 ≥ 0.98 + **每 tick 解出的速度在 τ 内不撞玩家**（α=1 语义） |
| 4a | `Chasing_Monster_Reaches_Its_Target_But_Goes_Around_A_Bystander` | 目标排除（追得到）+ 路人照绕（最小 1.024） |
| 4b | `Chase_Ignores_Its_Own_Target_And_Therefore_Never_Brakes` | 追击途中**不减速**（速度全程 = 配置速度） |
| 5a/5b | `Disabled_..._Pass_Through` / `Enabled_..._Head_On_Pair_Apart` | `--no-avoid` 真的对穿（0.34）vs 开避让 ≥ 0.98（实测 1.0785） |

**变异实验（3 个，全部还原）**：

| 变异 | 结果 |
|---|---|
| 1. 责任权重全传 0.5（玩家也 0.5） | **红**：`Player_Is_Never_Moved...` 的**视界**断言（0.8254 < 0.99）。**轨迹断言没红**——与上一轮审查同一教训：责任权重是"当帧约束强度"，只看轨迹会被下一 tick 的纠正掩盖 |
| 2. 去掉攻击目标排除 | **首次逃逸**（186/186 全绿）！攻击距离（1.5~2.5）都大于半径和（1.0~2.16），光看"进没进攻击距离"抓不住。补了 4b（追击途中不减速：不排除时实测最慢一步 **0.1129** < 满速 0.2310，到达从 43 tick 拖到 49 tick）后**红** |
| 3. 去掉对称 tie-break ② | **红 2 条**：迎面二人组 0.0271；狼追路人僵在 7.000m |

容差说明（怕被质疑太松）：收敛用例的 0.1 是**唯一**放松的一处（半径和的 10%），因为四体对称同点
是本构型里最难的：实测它们会绕成漩涡（400 tick 都在动、不僵），全程最小 0.9497（漩涡后期），
收敛那一段一直 ≥ 0.99；关掉避让是**对穿**（0.34），真正僵死会掉到 0 附近（算法单测的变异量级
−0.5 ~ −1.0）。判别力充足。

## 4. 活体对照（`--bots 100`，30s，同种子 12345 ⇒ 同布局）

命令：`dotnet run --no-build --project Server/Server3C.csproj -- 8000 --bots 100 --probe [--no-avoid]`
（探针每 3 tick ≈ 200ms 一行，日志：`production/verify/orca-avoid.log` / `orca-noavoid.log`；
跑完都确认停服）。取数口径与前 30s 对齐（两组各 151 个采样点）：

| 指标（前 30s） | **开避让** | `--no-avoid` |
|---|---|---|
| 最小两两间距（采样最小值） | **1.047 m** | **0.262 m** |
| 最小两两间距（中位数） | 7.527 m | 7.527 m |
| 出现"穿模对"（间距 < 两半径和）的采样点数 | **0** | **1** |
| 距离 < 5m 的贴近对数（累计） | 40 | 40 |

**结论："避让真的把最近距离拉开了吗？" —— 是。** 同一次相遇：
t=13.79s 时最近的一对，无避让下是 **0.262m（已穿模）**，开避让是 **1.047m（刚好在半径和外侧）**；
t=31.61s 的第二次相遇 0.437 → 1.409。其余采样点两组**完全相同**（远端对不受影响）——
避让只在"本来会撞"的地方改变行为，中位数一动不动，这也正是期望的形状。

## 5. 疑虑 / 已知边界

1. **只跑了两处移动系统**：玩家位置仍然纯客户端权威（服务端不模拟、不校验重叠）⇒ 改客户端
   照样能穿怪（沿用 `webgl-demo-boundaries.md` 的既有边界）。玩家在本实现里只是**会动的障碍**。
2. **`--no-avoid` 的等价性是"逐位"级**：怪物与机器人两条路径在无邻居时都逐位相同（用例钉住）。
   唯一的行为面变化是给 bot 上报的 `Speed` 从四舍五入的 `8f` 改成精确的 `BotSpeedPerTick/0.066`
   （8.0303）——快照是 ORCA 邻居速度的唯一来源，客户端**无消费方**（只有 `MovementReporter` 写它）。
3. **对称多体挤压仍会有 5% 的视觉体交叠**（收敛用例实测 0.9497）：这是离散化 + α=0.5 的结构性
   结果，不是回归。要彻底消除得给"已重叠"分支加论文外的东西，属于玩法决策，本次没加。
4. **τ 与筛程是一对参数**：τ 调大 ⇒ 筛程自动跟着大（`(v_i+v_j)·τ` 项），早期绕行更明显；
   反过来只调筛程不改 τ 是无效的。两处注释里都写明了这层耦合。
5. **PlayMode 世界测试（`AoiWorldTests` / `MonsterWorldTests`）本次没跑**——按任务要求只回归
   EditMode，且它们要起服。怪物巡逻/追击的模块级行为由 187 条服务端用例覆盖（含全部既有布局断言，
   一字未改）。
6. 解算器**未改**（除类注释加了"不要加沿 v_pref 拉满到 maxSpeed"的告诫）。所有不变量与容差
   口径与上一轮报告一致。
