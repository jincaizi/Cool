# 判定帧改造——自动化验收（角度 / 距离 / 毫秒残差）

日期：2026-10-16　执行：Claude Code（子代理）　提交：`test(combat): ...`

把"怪挥到哪、躲不躲得开、血什么时候掉"这类原本只能目测的量，变成**断言里的数字**。
两套件都是**新增文件**，生产代码零改动（本报告里仅有的两处"改"都在测试装置内，见 §4 发现）。

| 套件 | 文件 | 用例数 |
|---|---|---|
| 服务端集成（A） | `Server/Server.Tests/Monster/MonsterAcceptanceTests.cs` | 3 个测试方法 / **5 个用例**（A2、A3 是 `[Theory]` 各 2 格） |
| PlayMode 计时（B） | `Assets/Scripts/Tests/PlayMode/HitJudgmentAlignmentTests.cs` | 2 个 `[UnityTest]` |

---

## 1. 命令与输出摘要

### A：服务端（不需要服务器进程）

```
dotnet test Server/Server.Tests/Server.Tests.csproj
已通过! - 失败: 0，通过: 236，已跳过: 0，总计: 236，持续时间: 300 ms
```

`231 → 236`（+5 = A1 一条 + A2 两格 + A3 两格）。新增用例单独跑（带实测数字输出）：

```
dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MonsterAcceptanceTests" --logger "console;verbosity=detailed"
已通过! - 失败: 0，通过: 5
A1 实测：决策 T=0，判定帧 ART=6（HitDelayTicks=6），击杀 tick=2；判定后玩家 HP=100（期望 100），怪物 DeathTick=2，判定帧命中数=0
A2 偏轴 55°/判定半角 60°：实测夹角 55.00°、距离 1.500m（attackRange 2m，视野半角 45°）、TargetId=空 → 命中 15（期望 命中）
A2 偏轴 65°/判定半角 60°：实测夹角 65.00°、距离 1.500m（attackRange 2m，视野半角 45°）、TargetId=空 → 空挥（期望 空挥）
A3 半径 1.5m：玩家 306°/s（ω=冲刺÷半径）绕过 121.0°，怪物前摇只转了 35.6°（90°/s），判定帧实测夹角 85.37°（半角 60°）、距离 1.500m（attackRange 2.2m） → 空挥（期望 空挥），玩家 HP=100
A3 半径 2.1m：玩家 218°/s（ω=冲刺÷半径）绕过 86.4°，怪物前摇只转了 35.6°（90°/s），判定帧实测夹角 50.79°（半角 60°）、距离 2.100m（attackRange 2.2m） → 命中 30（期望 命中），玩家 HP=70
```

### B：PlayMode（需要**已运行**的服务器：`localhost:8000 --monsters 4`）

```
unity-mcp-cli run-tool assets-refresh --input '{"options":"ForceSynchronousImport"}'
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"HitJudgmentAlignmentTests"}' --timeout 900000
```

绿了以后连跑 3 次，**3/3 全绿**（每次 2 个用例）：

```
"Status": "Passed", "PassedTests": 2, "FailedTests": 0
[HitJudgmentAlignmentTests] B1 渲染起手 → 伤害到达 实测残差 268ms（期望 ≈ 264ms = (6 tick 判定延迟 − 2 tick 渲染) × 66ms；上界 600ms）；来源怪 2000005 skill 50002、伤害 15
[HitJudgmentAlignmentTests] B2 本地判定帧 → 服务端伤害到达 实测残差 70ms（期望 ≈ 30ms = 服务端 5 tick 判定 330ms − 本地判定帧 300ms；上界 400ms）；伤害 75 落在怪 2000006（skill 10001，本地判定帧共 1 次）
```

---

## 2. A 部分：三条结论（实测 vs 期望）

配置数字全部从构建输出目录的 `Config/monster_config.json` 读（Wolf=2：attackRange 2.0 / visionAngle 90；
DogKnight=3：attackRange 2.2 / visionAngle 120），技能表值从 `CombatRules` 读（怪物 4 条 HitDelay 均 0.35s → **6 tick**）。
判定锥半角取**实际入队请求**里的 `Shape.Angle/2`（`MonsterBtActions` 发 120 → **60°**），不在测试里另抄 60。

### A1 前摇期致死 → 这一刀作废（`Windup_Death_Cancels_The_Swing_And_No_Damage_Lands`）

Wolf 在 (0,0,1.5) 面向原点、玩家在原点 → 首次决策 **T=0**，判定帧 **ART=6**（= T + `HitDelayTicks(50002)`）。
在 **kill=2**（落在 T+1..ART-1 内）`ApplyDamage(9999)` 打死 Wolf，再推进到 ART，然后走**真实裁决链**
（`Decide` 在决策 tick、`DrainResolved` 在判定 tick，与 `Server3C` 主循环同序）。

| 断言 | 期望 | 实测 |
|---|---|---|
| ① 玩家 HP | 100（不掉血） | **100** ✓ |
| ② 怪物死亡窗口 | `DeathTick.HasValue` | **DeathTick=2** ✓ |
| ③ `DrainResolved(ART)` 对该 caster | 无命中 | **命中数=0**（施法者已死 → D-4 丢弃） ✓ |

### A2 丢目标 ≠ 打不中（`Lost_Target_Still_Hits_When_Inside_The_Judgment_Cone`，Theory ×2）

Wolf 视野半角 45° < 判定半角 60°：这条带子里"目标已丢失、这一刀照样落地"。
玩家**只在决策之后搬一次**（决策必须发生在原点，怪要先拿到目标才有前摇），此后不动——
前摇期的 90°/s 转向在本例中恰好是零（最后已知目标位置就在怪正前方），朝向冻结在决策那一刻。

| 偏轴 | 期望 | 实测夹角 | 实测距离 | TargetId | 结果 |
|---|---|---|---|---|---|
| **55°** | 命中（>45° 视野、<60° 判定） | **55.00°** | 1.500m | **空**（目标丢失） | **命中 15** ✓ |
| **65°** | 空挥 | **65.00°** | 1.500m | 空 | **空挥**（玩家 100 HP） ✓ |

两次距离都在 attackRange 2.0 内 ⇒ 差异**只能**来自锥角；两次 `TargetId` 都已被清空
⇒ "怪丢了目标，这一刀照样落地"这条设计真相被钉住（同时钉住 65° 时并不含糊——出锥即空挥）。

### A3 贴脸绕圈空挥 / 拉开一点绕圈命中（`Circling_At_Attack_Range_Whiffs_Only_When_Relative_Turn_Exceeds_The_Cone`，Theory ×2）

DogKnight 在 (0,0,2.0) 面向原点，玩家从决策后每 tick 沿以怪为圆心的圆周走，角速度
**ω = 冲刺速度 8 m/s ÷ 半径**，`Δt = MonsterModule.TickDeltaSeconds`（0.066，不硬编码 0.0667）；
怪的转向唯一变量 = 前摇分支的 **90°/s**。

| 半径 | 玩家角速度 | 6 tick 绕过 | 怪前摇转过 | 判定帧夹角 | 距离（attackRange 2.2） | 结果 |
|---|---|---|---|---|---|---|
| **1.5m** | 306°/s | **121.0°** | 35.6° | **85.37°** > 60° | 1.500m（在射程内） | **空挥** ✓（玩家 100 HP） |
| **2.1m** | 218°/s | **86.4°** | 35.6° | **50.79°** < 60° | 2.100m（在射程内） | **命中 30** ✓（玩家 70 HP） |

两次的判定帧距离都 ≤ attackRange ⇒ **空挥的成因只可能是锥角，不是跑出射程**（用例里专门断言了这点）。
相对转角（≈ 玩家绕过 − 怪转过：85.4° / 50.8°）与实测夹角一致，符合"怪追的是**陈旧的**最后已知位置"的模型。

---

## 3. B 部分：两条实测残差

装置：`KcpNetClientAdapter` + `ClientNetworkManager` + `RemoteEntityRegistry`，**每帧驱动**
（`Update(Time.deltaTime)` + `yield null`，不是 50ms 步进——断言是百毫秒级的时序，步进会污染测量）。
两个观察点都不改生产代码：

* **渲染起手** = `NpcAttackSchedule` 到点（registry 紧接着就是 `PlayAttack`）。
  该表是 registry 的私有字段（`_attacks._pending`），测试只读反射——**全文件唯一的测试钩子**，
  字段改名会带明确消息失败（提示同步观察点）。
* **伤害到达** = `CombatNetworkBridge.OnDamageResult`（本地受伤注入回调 = 生产里
  `Sys3CEntry.ApplyServerDamage` 的调用处，两者在同一次 `Bridge.Update()` 内先后触发）。

### B1 怪物挥砍 → 玩家受击（`Monster_Swing_Render_Leads_The_Damage_It_Causes`）

观察者贴在某只怪的正前方 1.2m（位置经 `MovementReporter` 上报；服务端瞬移校验是双门槛
`distance > MaxSpeed×dt×1.2 && distance > 50m`，贴脸位移不被纠偏）。**5 次实测**：

```
380ms / 285ms / 268ms / 288ms / 291ms   （期望 ≈ 264ms，上界 600ms，全部 ≥ 0）
```

| 项 | 值 |
|---|---|
| 期望 | (6 tick 判定延迟 − 2 tick 渲染延迟) × 66ms ≈ **264ms** |
| 上界依据 | 6 tick 判定 − 2 tick 渲染 + 1 tick 排程帧量化 + 2 tick 网络/时钟余量 → **600ms** |
| 实测 | **268~380ms**（中位 ≈ 288ms），**渲染起手恒先于伤害到达** ✓ |
| 来源 | Wolf(t2) skill 50002、伤害 15（服务端表值，与 `monster_config.json` 一致） |

结论：**"看得见的那一刀"确实领先于"血掉下去"约 4 个 tick**，模型（判定帧 = 决策 + 6 tick、
渲染 = 决策 + 2 tick）与实测吻合到 ±10%。

### B2 玩家挥砍 → 本地判定帧 vs 服务端结果（`Player_Hitbox_Frame_Precedes_Its_Server_Damage_Result`）

`PlayerEntryForTest`（带 Sys3CEntry）实例化进场景，摆到怪的正前方 1.2m，走**本地输入路径**
`SkillCoordinator.HandleLightAttack()`（技能 10001）。**5 次实测**：

```
75ms / 63ms / 70ms / 69ms / 130ms   （期望 ≈ 30ms，上界 400ms，全部 > 0）
```

| 项 | 值 |
|---|---|
| 期望 | 服务端 5 tick 判定（`HitDelayTicks(10001)`=5 → 330ms）− 本地判定帧 300ms（`CastTime + hitboxTimings[0]`，BA_Combo1 资产实测） ≈ **30ms + 网络** |
| 上界依据 | 1 tick 上行 + 1 tick 服务端 tick 量化 + 1 tick 下行 + 余量 → **400ms** |
| 实测 | **63~130ms**，**本地判定帧恒先于服务端结果** ✓（超出理论值的那 30~100ms = 上行 + tick 量化 + 下行） |
| 伤害 | 50 或 75 = 服务端暴击域 {50, 75}（15% 暴击），落在怪（NPC id）上 |

结论：**服务端判定帧与本地判定帧对齐**（同一张表的 0.30s 照进了两端），偏差只剩网络往返 + tick 量化。

---

## 4. 阻塞项与过程中的两个发现（都已在**测试装置**内解决，生产代码未动）

1. **B2 首跑失败 → 装置漏接上行**：本地 `HandleLightAttack()` 只播了动画，**没有发 CastRequest**
   （生产里这条线是 `ClientNetworkHost` 的 `OnSkillActivated → CombatNetworkBridge.NotifySkillCast`）。
   测试装置补上该接线后通过。**这不是生产缺陷**，是装置照抄宿主接线时的遗漏——
   但它顺带证明了"本地打击感与服务端判定是两条线，只有上行才产生伤害"。
2. **B1 偶发失败 → 入场前的旧刀**：服务端在**判定帧**才选目标（`DrainResolved → ResolveHits`），
   所以怪在**我们进世界之前**决策并广播的那一刀，入场后照样可能打到我们——而那一刀的
   `CastBroadcast` 我们收不到、也就看不到它的渲染起手（残差无从测起）。
   现在每笔本地受击都与"观测到的起手"配对（配不到的跳过）。这是**服务端的既有语义**（判定帧选目标），
   不是缺陷；测试按它对齐。
3. 环境：`localhost:8000` 是常驻服务器（`--monsters 4`），历史会话会留下"幽灵实体"，
   怪可能已经贴在我们出生点附近——这解释了 PlayMode 两条用例跑得飞快（每次 ~1s）：
   它们不依赖"怪慢慢走过来"，只依赖"怪确实打我们"。

两条 PlayMode 用例都**需要真实服务器**，因此不进入离线 CI；A 部分不需要任何外部进程。

---

## 5. 修复轮（第 1 轮审查，2026-10-16）

审查判定"通过"，本轮收掉 1 条硬要求违规 + 5 条次要项。**只动测试，生产代码照旧零改动**。

### 改了什么

| # | 位置 | 改动 | 理由 |
|---|---|---|---|
| 1（重要） | `MonsterAcceptanceTests.cs` A2/A3 | 伤害期望值从字面常量 `100f - 15f` / `100f - 30f` 改为**从表读** `100f - skill.BaseDamage`（`CombatRules.TryGet(request.SkillId, out skill)` + 断言 `CriticalRate == 0`） | 派单硬要求"数字必须从配置/表读"。表本身是调参对象——抄常量会让日后改平衡打红验收，而最省事的"修法"恰是被禁止的调断言 |
| 2 | `MonsterAcceptanceTests.cs` A2 注释 | "Wolf：visionAngle 120 → 视野半角 45°" → **90** → 45° | 原注释与代码（`config.visionAngle * 0.5f`）和配置（json 里 Wolf=90）都矛盾；这行恰是"45° 那条带子"的论据 |
| 3 | `HitJudgmentAlignmentTests.cs` B1 | 下界 `>= 0` → 严格 `Assert.Greater(residual, 0.0)` + 注释写明权衡（单帧卡顿 ≥264ms 才可能假红） | 与 B2 一致，且堵住"起手与掉血同帧"这种回归（判定提前到起手的退化形态） |
| 4 | 两处上界依据 | B1：4 tick 期望 + 1 排程量化 + 1 客户端时钟偏移 + 1 服务端 tick/发送量化 + 2 网络宿主余量 = **9 tick ≈ 594ms**（向上取 600ms）；B2：0.5 tick 基准 + 1 上行 + 1 服务端 tick 量化 + 1 下行 + 2 余量 = **5.5 tick ≈ 363ms**（向上取 400ms） | 原文"6−2+1+2 tick" 算不出 600ms；**上界数值一格未动**（只把依据写成算得出来的账） |
| 5 | `HitDelayTicksOf` | 表未命中不再静默返回 0，改 `Assert.Fail`（带技能 id） | 静默 0 会让 expected 变成无意义负值，对照失真却看不出来 |
| 6 | `MonsterAcceptanceTests.cs` A1 | 击杀并入主循环：`for t in T+1 .. ART-1 { if (t == killTick) ApplyDamage; Tick(t); }` | 原写法 `Tick(killTick)` 后从 `killTick+1` 续跑 ⇒ **T+1 从未被 tick**；当前几何无害，换需要转向/位移的用例会咬人 |

**明确不做（审查已裁定延后）**：B1/B2 配对启发式的两个已知缺陷（同帧出入队会漏记、`Clear` 会记成假起手；B2 无因果键）——
失败方向是**假红而非假绿**，重写需动 PlayMode 装置并重跑，收益不抵成本。

### 命令与输出

```
dotnet test Server/Server.Tests/Server.Tests.csproj
已通过! - 失败: 0，通过: 236，已跳过: 0，总计: 236，持续时间: 298 ms   ← 修复前也是 236/236
```

```
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"HitJudgmentAlignmentTests"}' --timeout 900000
"Status": "Passed", "PassedTests": 2, "FailedTests": 0
[HitJudgmentAlignmentTests] B1 渲染起手 → 伤害到达 实测残差 267ms（期望 ≈ 264ms = (6 tick 判定延迟 − 2 tick 渲染) × 66ms；上界 600ms）；来源怪 2000005 skill 50002、伤害 15
[HitJudgmentAlignmentTests] B2 本地判定帧 → 服务端伤害到达 实测残差 61ms（期望 ≈ 30ms = 服务端 5 tick 判定 330ms − 本地判定帧 300ms；上界 400ms）；伤害 75 落在怪 2000007（skill 10001，本地判定帧共 1 次）
```

A 部分修复后实测（数字与修复前逐项一致；A1 因补上 T+1 的 tick 后依然 DeathTick=2 / 命中数 0）：

```
A1 实测：决策 T=0，判定帧 ART=6（HitDelayTicks=6），击杀 tick=2；判定后玩家 HP=100（期望 100），怪物 DeathTick=2，判定帧命中数=0
A2 偏轴 55°：实测夹角 55.00°、距离 1.500m、TargetId=空 → 命中 15，玩家 HP=85（期望 85，从表读 BaseDamage）
A2 偏轴 65°：实测夹角 65.00°、距离 1.500m、TargetId=空 → 空挥，玩家 HP=100（期望 100）
A3 半径 1.5m：绕过 121.0°、怪只转 35.6° → 夹角 85.37°（半角 60°）→ 空挥，HP=100（期望 100）
A3 半径 2.1m：绕过 86.4°、怪只转 35.6° → 夹角 50.79°（半角 60°）→ 命中 30，HP=70（期望 70）
```

**实测数字有无变化：无。** 两套件数字不变（236/236 与 2/2），B1 267ms（此前 268~380ms 带内）、B2 61ms（此前 63~130ms 带内）；
A 的夹角/距离逐项不变，只有 A2/A3 的期望 HP 改成从表读、A1 的时间轴补齐 T+1。
