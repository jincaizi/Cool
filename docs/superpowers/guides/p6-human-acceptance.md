# PENDING-HUMAN — P6 最终人工验收清单

> **状态：全部待执行。** 本清单合并了 P6 计划 10 个任务累积的**全部人工专有验证项**
> （子代理不能执行：不得启动 GUI 程序、无法注入输入、需要目视判断）。
>
> 生成于 Task 10（10/10，最后一个任务）。**出包与三套自动化测试均已完成**（数字见 §E），
> 所以下面每一格都是"拿到就能照着做"的最终形态，不需要再等任何前置。
>
> **共 17 项人工项**：A 组 8 项 + B 组 2 项 + C 组 5 项 + D 组 2 项。§E 是只读参考，不需复做。

**执行顺序建议**：A（单机，一次启动做完 A1–A6）→ B（接着在单机模式里做）→ C（联网）→ D（穿插在 A4 里）。

---

## 0. 前置：确认你跑的是本次构建的包

| 项 | 值 |
|---|---|
| 包路径 | `E:\CodeForJob\Cool\Build\Windows\CoolP6.exe` |
| 构建时刻 | 2026-10-07 **18:24**（**最终修复轮**后的最后一次构建，覆盖 C1 与 I1–I6） |
| exe | 666,624 B（Unity 启动桩，与历次构建**同 md5** `25d71b7ab14278f0c907c59b5a69b4a7` —— exe 本身不是判据） |
| 包内容总大小 | **141,067,432 B**（与上一轮**逐字节相同**——本轮只改代码，`GameSys.dll` 大小未跨 512B 对齐边界） |
| **判据（用这个）** | `CoolP6_Data\Managed\GameSys.dll` 修改时间 = **18:24**（大小仍是 334,336 B，**不要用大小判据**） |
| 场景顺序 | `level0` = CharacterSelect（启动即选人界面），`level1` = DemoDay |

> ✅ **本轮已对包内 `GameSys.dll` 做反编译核对**（`ilspycmd`，最强证据）：
> `FSMConfig.HitDuration/KnockbackDuration = 0.5f`、`FSMManager.Update` 内有 `_hitFSM.Update(deltaTime)`、
> `HitFSM.TransitionTo` 的 `Knockback` 分支内有 `_animator.SetTrigger(AnimHashes.Hit)`。
> 包内文件 **362** 个（集合与基线一致），构建时**只重写了 2 个文件**
> （`GameSys.dll` + `boot.config`，增量拷贝的预期形态）。

> ⚠️ **`Build\Windows-baseline-pre-task10\` 是 Task 10 之前的旧包（00:45），必须忽略/删除。**
> 它的 exe 与本次**同名同 md5**，双击它你会验到 9 处 P6 修复**全部缺失**的版本，而它看起来"能跑"。
> 只有 `Build\Windows\` 是本次交付物。（子代理删不动该目录，留给人类清理。）

**日志位置**（排查失败第一步）：
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Cool\Player.log`

**本批次 9 处改动已确认进入包内**（以下为 Task 10 轮的核对；**最终修复轮的核对见上方 ✅**）：其中 **6 处为符号级**证据
（在 `CoolP6_Data\Managed\GameSys.dll` 中检索到：`EvaluateLocalDeath`(Task 4)、
`SpawnLedger`/`DespawnAll`(Task 5)、`_executionTimingCursor`(Task 6)、`Physics.SphereCastAll`(Task 7)、
`SaveConnection`/`NetworkModeService`(Task 8)、`_pendingReturnToMenu`(Task 9)），
另 3 处为**间接**证据（Task 1 构建脚本属 Editor 程序集不进包；Task 2 改的是 `.asset`，
落在 `resources.assets`；Task 3 调的是既有方法、无新符号）——旁证是包内文件集合与基线完全一致、
`GameSys.dll` 按预期增大 5,120 B。
但**静态确认 ≠ 运行验证**——这正是 A/C 组的用途。

---

## A 组：单机验收（一次启动覆盖 A1–A6）

双击 `Build\Windows\CoolP6.exe`，选"单机"确认进世界。**中途不要退出**，A1–A6 串行做完。

### A1. 包可运行
- **怎么做**：双击 exe，等 5–15 秒。
- **预期**：出现**选人界面**（男女角色卡片 + 底部"单机/联网"按钮）。无黑屏、无立即崩溃、无报错弹窗。
- **失败时**：看 `Player.log` 末尾。

### A2. 单机零配置可玩
- **怎么做**：选"单机"→ 点"确认"。
- **预期**：进世界（DemoDay 场景），**不触网**（左下角无 `[Net]` 状态浮层，或浮层显示"未连接"）。
  按 `W/A/S/D` 能移动，按鼠标左键能挥砍。
- **注意**：本次**不会**自动弹出服务器设置区（那是联网模式才展开的，见 C1）。

### A3. 打怪有反馈
- **怎么做**：朝出生点附近的两只怪（Slime 史莱姆 / TurtleShell 龟壳）走过去，鼠标左键连续砍。
- **预期**：① 怪**掉血**（头顶血条下降）；② 有**飘字**伤害数字；③ 怪受击有表现（闪白/顿帧/击退）。

### A4. 被打有反馈 ← **P6 新增（Task 3）**
- **怎么做**：站到怪的攻击范围内**不还手**，等它出手。
- **预期**：玩家播放**受击动画**，并且有**击退位移**（角色被推开一小段，不是原地抖）。
  受击结束后（约 0.5 秒）**必须能恢复行动**——能再走、能再被打（不能"漂着不动也不吃第二下"）。
- **若受击动画不播**：先看是否卡在受击态。受击态有**两条**结束路径，任一条断都会有症状：
  ① 动画回调链（`HitStateBehaviour → FSMManager.HandleHitAnimationCompleted → HitFSM.OnAnimationEnd → Recover`）；
  ② 计时兜底（`FSMManager.Update → HitFSM.Update` → 状态计时器走完 → `Recover`，`Hit`/`Knockback` 均 0.5s）。
  （②是本轮新接的线：此前 `HitFSM.Update` 全项目零调用者，计时器是死的，①一断就永久卡在受击态
  → 角色持续滑行 + 之后免疫一切受击。两条都断才会复现该症状。）
- **若无击退**：单机击退只由 `AttackDamage._knockbackForce` 保证
  （Slime=1.5 / TurtleShell=2.5，经 `PlayerHitZone` 的 `hitboxData.KnockbackForce`）；
  怪物配置里的 `AttackEffect._knockbackForce` 在**怪物攻击路径上无消费者**（"上了膛但未接线"）。
- **顺带做 D1/D2**（见 D 组）——这是同一个观察窗口。

### A5. 怪物能打死玩家 + 死亡期间输入门禁 ← **P6 新增（Task 4）**
> 两件事一起做：死亡门禁是**唯一**无法自动化的一项（纯输入驱动，PlayMode 不能注入按键）。
- **怎么做**：继续站着挨打。玩家 HP = 100，Slime 每击 10 / TurtleShell 每击 8。
  1. HP 归零的那一刻起：**按住 `W/A/S/D`**、**连按鼠标左键**、**连按技能键**（`1`/`2`/`3`/`Q`/`R`）。
  2. 单独再死一次，这次**死亡瞬间保持按住鼠标右键**（举盾）。
- **预期**（**判据只有这三条，P6 的死亡承诺就是这些**）：
  - 死亡后**不能移动**，死亡期间按移动键**不产生位移**。
  - 死亡期间**不出现任何攻击/技能动画**（控制台不应出现 `[Sys3CEntry] Skill activated: ...`）。
  - **约 3 秒后满血复活**、回到出生点、恢复可动（这条与 A6 是同一件事，一次做完即可）。
  - 举盾中死亡：盾保持到复活后第一帧自动收起（不按右键时）；仍按住右键则继续举盾。
- **⚠️ 不要按"有没有倒地/死亡动画"判定**（原判据写错了，已订正）：
  `Character3C.controller` 的 Hit 层 `Death` 状态**没有任何入边转移**，且 `AnimHashes.Death` /
  `AnimHashes.IsDead` 这两个参数在 controller 里**根本不存在**（参数共 16 个，无这两个）
  → **死亡无任何视觉表现**：角色保持站立（Base 层照常播 Idle），不会倒地、不会变灰。
  这不是本次回归，是 P6 未做的部分——**死亡/倒地/起身/浮空四项表现层属 backlog**
  （`HitAnimatorContractTests` 已把这条现状登记为 `KnownBacklogStates` 并锁死）。
- **背景**：移动锁有自动化断言（`PlayerDeathRespawnTests.test_dead_player_has_movement_locked`），
  但**施法/攻击门禁无自动化覆盖**（`Sys3CEntry.HandleInput()` 首行 `if (_isDead) return;`）。
  门禁前的探针实测过反例：死亡时仍能起手 `Basic Attack 1 (id=10001)`。

### A6. 死后能复活 ← **P6 新增（Task 4）**
- **怎么做**：死亡后原地等。
- **预期**：**约 3 秒**（`_respawnDelay = 3f`）后**满血复活**、可移动、**回到出生点**。

### A7. 相机可转 + 不穿墙 ← **P6 新增（Task 7）**
- **怎么做**：① **移动鼠标**（无需按键——`KeyboardInputAdapter` 读 `Mouse X/Y`）看视角是否跟随；
  ② 角色贴着一堵墙/一根柱子，**绕墙转动**鼠标；③ 被 2 只以上怪贴脸围住时看相机。
- **预期**：
  - ① 鼠标能平滑转动视角，**不抖动**（本次把相机挪到 `LateUpdate` 并去掉重复驱动——
    "不抖动"正是为验证这一点）。
  - ② 贴墙转动时相机**不穿进几何体**（会被拉近）。
  - ③ 不出现相机突然**贴到角色脑后**的过度拉近。
    （已知边界：非玩家重叠体会把距离压到 `MinCameraDistance`——若出现，根因在此，
    属已登记的次要项而非新 bug。）

### A8. 刷怪不爆炸 ← **P6 新增（Task 5）**
- **怎么做**：**连续打怪 10 分钟**（不用一直认真打，挂着也行，但别离开怪物活动区）。
  观察怪物总数的变化。三种观察法任选：
  1. **目视**：Hierarchy 里展开两个 spawner（`SlimeSpawner` / `TurtleShellSpawner`），
     数各自 `Monsters` 子节点的**激活**子对象数。
  2. **精确读数**：Inspector 右上角 `⋮` → **Debug** 模式，选中 spawner，
     直接看私有字段 `_aliveMonsters` 的 `Count`。
  3. 在**出包**里没有 Hierarchy —— 那就目视场景中怪的数量是否越来越多。
- **预期**：怪物总数**围绕刷怪组配置的数量上下波动，不持续增长**
  （死亡 → 延迟 30 秒重生，但重生受记账上限约束）。
- **失败形态**：数量单调上升 / 越打越多（这正是本批次修复的"刷怪爆炸"bug 的原始形态）。

---

## B 组：数值节奏复核（Task 10 Step 4）

在**单机模式**下实测并记录。

| # | 项目 | 怎么做 | 预期 |
|---|---|---|---|
| B1 | 打死 Slime 需要几刀 | 连续普攻（鼠标左键）打一只史莱姆，数刀数并计时 | **2 刀**（HP 100 ÷ 普攻 50） |
| B2 | 打死 TurtleShell 需要几刀 | 同上，打龟壳 | **5 刀**（HP 250 ÷ 普攻 50） |

**推算依据（已逐项核对，不是猜的）**：
- 普攻 `BA_Combo1` 的 `_baseDamage = 50`（`Assets/PreRes/SkillsCfg/BA_Combo1.asset:35`）
- Slime `MaxHP = 100`、TurtleShell `MaxHP = 250`（`Assets/Monstor/*.asset`）
- 怪物的**生产伤害路径**（`MonsterEntity.TakeDamage → DamagePipeline.Process → MonsterStats.ApplyDamage`）
  是**纯 HP 减法，不含防御减免**（`Defense` 只在 `MonsterStats.TakeDamage` 这条**非生产**路径里被消费）
- 伤害**无浮动**：`GameSettings.DamageFluctuation = 0f`

**若实测是 3 刀 / 6 刀**：说明有未发现的伤害来源在扣减（例如某处仍按
`BaseDamage - Defense*0.3` 计算：50 − 5×0.3 = 48.5 → 100÷48.5 = 3 刀）。
**请把这个偏差写进人类反馈**——Step 4 明确要求记录这个信号。

---

## C 组：联网验收

先做 C1（不需要服务器），再做 C2/C4（需要服务器），然后 C5（杀服务器），最后 C3（需改一行代码）。

**起服务器**（另开一个终端）：
```bash
cd E:/CodeForJob/Cool
dotnet run --project Server/Server3C.csproj -- 8000
```
看到 `Server3C is running on port 8000` 即就绪。停服：Ctrl+C，或
`netstat -ano | grep ":8000"` 取 PID → `taskkill //F //PID <pid>`。

### C1. 选人界面地址可配 ← **P6 新增（Task 8）**
- **怎么做**：双击 exe，在选人界面点底部"**联网**"按钮。
- **预期**（5 个子项，逐条勾）：
  1. 选"联网"后，面板下方**展开服务器设置区**（地址 / 端口 / 昵称三个输入框）。
     （已知瑕疵：首次打开可能有一瞬间闪现烘焙态的可见性，属已登记次要项。）
  2. 选回"单机"时，设置区**隐藏**。
  3. 默认值 `127.0.0.1` / `8000` / `Player`；**上次填过的值会被回填**。
  4. **非法值不崩**：端口输入 `99999` 或 `0` → 点确认不崩溃，实际连接端口**回落 8000**
     （TMP 的 IntegerNumber 只过滤非数字字符，"99999" 敲得进去，由 `parsed > 65535` 拦下）。
  5. **重进场景值仍在**：确认进世界 → 断线/杀服务返回选人界面 → 设置区仍显示上次填的值
     （持久化在 PlayerPrefs `net.host`/`net.port`/`net.username`）。

### C2. 地址填错 → 有失败提示（Task 10 Step 3 #9 后半）
- **怎么做**：**先不要起服务器**。联网 → 地址填一个没人监听的端口（如 `127.0.0.1:9999`）→ 确认。
- **预期**：屏幕中部出现 **`连接失败：127.0.0.1:9999 ...`**
  （**必须带 `<地址>:<端口>`**，这是本次专门修进去的），**约 3 秒后自动回选人界面**，可再连。
- **若没有任何提示**：看 `Player.log` 是否有 `[NetHost] error:`。
  （提示框已**不再**依赖调试浮层——原先它被关在 `_showDebugOverlay` 守卫之内，关掉调试位会连带
  关掉全部屏幕出口，本轮已搬出；且 `_pendingReturnToMenu` 已提到 `ShowStatus` 之前，本回调
  即使跑在网络线程上抛异常，返回路径也已先立起来。）

### C3. 登录被拒 → 也有出口 ← **Task 9 复审新增，务必做**
> 这是 Task 9 复审点名的**唯一可能整条路径失效**的人工用例：
> `HandleError` 可能跑在**网络线程**上（`KcpNetClientAdapter` 用 `DirectMessageExecutor`
> 内联同步执行），而 `ShowStatus` 读 `Time.unscaledTime` 是 Unity API。
> 若 Unity 线程检查抛异常，"登录被拒"的文案**连显示都到不了**，返回路径也不会被置位。
> **本轮已按此修**：`_pendingReturnToMenu = true;` 提到 `ShowStatus(...)` **之前**
> （`ClientNetworkHost.HandleError`），即使 `ShowStatus` 抛异常，返回路径也已生效
> ——最坏情况是"无提示但 3 秒后仍回到选人界面"，不再是"卡住"。

- **怎么做**（关键是**顺序**：先冻结服务器二进制，再改客户端版本号）：
  1. 先把服务器**编译好**（此时协议版本 = 1）：
     `cd E:/CodeForJob/Cool && dotnet build Server/Server3C.csproj`
  2. 改**客户端**协议版本：`Assets/Scripts/AOT/Protocol/ProtocolConstants.cs:10`
     `ProtocolVersion = 1;` → `= 2;`
     （该文件被客户端与服务器工程**共享编译**，所以只能靠"服务器不重编译"来制造不一致。）
  3. 让 **Unity 编辑器**重新编译（打开编辑器 / 触发刷新即可）——
     **⚠️ 不要重新出包**，交付物 `Build\Windows\` 必须保持 **18:24** 那份（最终修复轮后的构建）。
  4. **起服务器时加 `--no-build`**（用步骤 1 那个版本 = 1 的二进制，别让它重建）：
     `dotnet run --no-build --project Server/Server3C.csproj -- 8000`
  5. 在**编辑器 PlayMode** 里：选人界面选"联网" → 确认（客户端版本 = 2 → 服务器版本 = 1）。
- **预期**：屏幕出现**登录被拒/登录失败**提示（服务器返回
  `Protocol version mismatch: client=2, server=1`），**约 3 秒后自动回选人界面**。
  服务器终端会打印 `Rejected login from ...: protocol version 2`。
- **失败形态（重点看这个）**：客户端**静默挂起**——既没提示、**且 3 秒后也没回选人界面**，
  控制台可能有一条 Unity API 线程异常。此时才说明"网络线程"这条路仍有未堵住的异常点，请回报。
  （只有"没提示但回了选人界面" = 上面的顺序修复生效，属可接受的最坏情况。）
- **做完务必还原**：
  - `ProtocolConstants.cs` 改回 `ProtocolVersion = 1;`
  - 顺带知晓：改成 2 期间 `Server.Tests` 的 `FoundationTests.ProtocolVersion_Is_1` 会失败（1 条），
    这是预期的，不是回归。

### C4. 地址填对 → 能进世界（Task 10 Step 3 #9 前半）
- **怎么做**：起服务器（上面那条命令）→ 联网 → 地址 `127.0.0.1:8000` → 确认。
- **预期**：
  - 左下角浮层 `[Net] 模式=联网  状态=InWorld  玩家id=N  远程实体=M`（`状态` 走到 **InWorld**）。
  - 控制台出现 `[NetHost] 联网模式——连接 127.0.0.1:8000`、
    `[NetHost] 本地 MonsterSpawner 已禁用：...`。
  - **本地怪物消失**，走进视野能看到**球体占位**的服务器怪物。

### C5. 断线有出口 ← **P6 新增（Task 9）**
- **怎么做**：C4 进世界后，**杀掉服务器进程**（Ctrl+C 或 `taskkill //F //PID <pid>`）。
- **预期**：**3 秒内**屏幕中部提示 **`与服务器断开：<原因>，即将返回主界面`**
  → **自动回到选人界面**（不卡死、不需要退出重进）→ 可重新选模式再连。
- **不自动重连**是设计（重连 = 全新登录流程）——回选人界面后需手动再确认一次，这是预期的。
- **顺带验两件事**（Task 5 的修复都在这条路径上）：
  - 联网进世界后**本地怪物应当消失**（`DespawnAll` + `SetActive(false)`）。
    旧版本表现为"名牌残影 + 仍能被本地攻击命中"——**这是已修复项，若复现请当回归报**。
  - 返回选人界面后再进单机，刷怪应当正常（不是空的）。

---

## D 组：表现与目视复核

| # | 项目 | 怎么做 | 预期 |
|---|---|---|---|
| D1 | 受击动画**真的在播** | 在 A4 里**录像/截图**受击瞬间 | 角色确实播放受击动画，而不是只切了状态。<br>（自动化已能证明"进受击态后 Hit 层在播一个剪辑"——`HitAnimatorContractTests` 用真实 Animator 驱动；<br>但仍**不能证明它在屏幕上看起来对**，所以这项保留。） |
| D2 | 击退量是否"看得见" | 在 A4 里量角色被推开的距离 | Slime 击退力 1.5 / TurtleShell 2.5。只要**目视有位移**即通过；<br>若完全无位移 → 按 A4 的排查线索走。 |

---

## E. 只读参考（子代理已跑完，**不需要人工复做**）

留档，供你在验收发现异常时对照"这是不是新问题"。

| 套件 | 结果 | 备注 |
|---|---|---|
| `dotnet test Server/Server.Tests/Server.Tests.csproj` | **132 / 132** | 与基线一致 |
| `tests-run mode=EditMode` | **222 / 222** | P6 前是 200；P6 本批次 +17，最终修复轮 +5（4 条 Hit↔Animator 契约 + 1 条 A 半守卫） |
| `tests-run mode=PlayMode`（无服务器） | **6 / 18** | 12 条网络用例必红，**属既有基线** |
| `tests-run mode=PlayMode`（`--bots 100`） | **16 / 18** | 剩 2 条 MonsterWorldTests 需 `--monsters` |
| `tests-run mode=PlayMode`（`--bots 100 --monsters 4`） | **18 / 18** | 三套配置**并集 = 18/18 全绿** |
| 重新出包（batchmode CLI） | **成功** | 见 §0 的构建时刻与大小 |

**PlayMode 为什么必须分三次跑**：不同用例对服务器配置有互斥要求——
`AoiWorldTests` 要 `--bots`，`MonsterWorldTests` 要 `--monsters`，
而 `CombatSyncTests`/`MovementSyncTests`/`TwoClientWorldTests` 按消息条数做全局断言，
**有怪物在场会被污染**。单次通跑拿不到 17/17，这是套件设计使然，不是缺陷。

---

## 记录模板（验完请回填）

| 项 | 结果 | 备注 |
|---|---|---|
| A1 包可运行 | ☐ 通过 ☐ 失败 | |
| A2 单机零配置可玩 | ☐ 通过 ☐ 失败 | |
| A3 打怪有反馈 | ☐ 通过 ☐ 失败 | |
| A4 被打有反馈（动画+击退） | ☐ 通过 ☐ 失败 | |
| A5 死亡期间输入门禁（**判据不含死亡动画**） | ☐ 通过 ☐ 失败 | |
| A6 死后能复活（约 3 秒） | ☐ 通过 ☐ 失败 | |
| A7 相机可转 + 不穿墙 | ☐ 通过 ☐ 失败 | |
| A8 刷怪不爆炸（10 分钟） | ☐ 通过 ☐ 失败 | |
| B1 Slime ___ 刀 | ☐ =2 ☐ 其他：___ | |
| B2 TurtleShell ___ 刀 | ☐ =5 ☐ 其他：___ | |
| C1 地址可配（5 子项） | ☐ 通过 ☐ 失败 | |
| C2 错误地址有失败提示 | ☐ 通过 ☐ 失败 | |
| C3 登录被拒有出口 | ☐ 通过 ☐ 失败 | |
| C4 正确地址能进世界 | ☐ 通过 ☐ 失败 | |
| C5 断线有出口 | ☐ 通过 ☐ 失败 | |
| D1 受击动画在播 | ☐ 通过 ☐ 失败 | |
| D2 击退目视可见 | ☐ 通过 ☐ 失败 | |
