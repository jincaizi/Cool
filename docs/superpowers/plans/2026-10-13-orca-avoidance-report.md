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
