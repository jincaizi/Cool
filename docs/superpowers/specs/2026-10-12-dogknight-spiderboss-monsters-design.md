# DogKnight 与 SpiderBoss 接入刷怪列表 —— 设计

日期：2026-10-12
状态：待实现（尚未写码）
上游：[P5 怪物服务端化](../../plans/2026-09-12-p5-monster-server.md)（服务端权威 + 客户端 Npc 快照视图）

## 1. 目标

把两个已有美术资源接入服务端刷怪列表，并让它们在客户端上「看得见、动得对」：

- `Assets/Monstor/DogKnight/`（Modular Animal Knights 套件的动物骑士，10 段 .anim + 现成预制体）→ **精英怪 t3**
- `Assets/Monstor/fantasySpider/`（FBX 内嵌 13 段片段，无预制体无控制器）→ **Boss 怪 t4**

验收口径：**自动化测试为主 + 人工截图**。

## 2. 决策记录

| # | 决策 | 来源 |
|---|---|---|
| D1 | 服务端配置 + 客户端真模型/动画，两套都做 | 用户选择 |
| D2 | **新增**模板 t3/t4，布局尾部追加点位，前四点不动 | 用户选择 |
| D3 | 验收＝自动化测试 + 人工截图 | 用户选择 |
| D4 | 骑士＝精英、**蜘蛛＝Boss**（覆盖同轮早先的「蜘蛛＝杂兵」） | 用户选择（两轮，后者胜） |
| D5 | 客户端形态：控制器按**动画语义**建 + 新增怪物专用驱动组件 | 用户选择 |
| D6 | 蜘蛛体型：**预制体缩放 ×2（3.3m 宽）**，攻击距离同步到 2.5m | 用户选择（FBX `globalScale` 是 0.01，改成 0.5 等于 50× = 83m，已排除） |
| D7 | 跨客户端一致性：**全做**（原列三件；其中「循环相位」由「状态走同一时间轴」带出，另需服务端补发送节拍 → D9） | 用户选择 → 自查后修正 |
| D8 | `ConfigLoader.CreateDefaultConfig` 同步补 t3/t4 | 用户选择 |
| D9 | **服务端补「状态变化即发送」**（`ShouldSend` 纳入 `ActionState`/`Dead`）—— 否则跨客户端状态翻转最多差 4 tick | 严格自查发现（§6.1）→ 用户批准 |
| D10 | 控制器**保留标准转移图**（含 `MonsterState=3` 攻击中），废弃早先的「驱动 CrossFade + 控制器不放转移」 | 严格自查修正（§5.2） |

## 3. 服务端接线（5 处 + 1 处保底表）

| 位置 | 改动 |
|---|---|
| `Server/Config/monster_config.json` | 见下表新增两条 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 新增 50003 / 50004（**双端共享文件**，`Server3C.csproj:14` 显式编译） |
| `Server/Monster/MonsterModule.cs` `SkillIdOf` | `3 => 50003`、`4 => 50004` |
| `Server/Monster/MonsterModule.cs` `SpawnDefaultLayout` | 尾部追加 `[4]` `[5]`，前四点原样 |
| `Aoi/AoiBandScheduler.cs` `ShouldSend` + `Aoi/AoiBroadcaster.cs` | 把 `ActionState`/`Dead` 纳入变化判定，**变化即立即发送** —— 见 §6.1；不做则状态翻转跨客户端最多差 4 tick |
| `Server/Config/ConfigLoader.cs` `CreateDefaultConfig` | 同步补 t3/t4（D8：否则配置文件缺失 + `--monsters 6` 会在启动时抛 `Unknown monster template: 3`） |

| | DogKnight (t3) | SpiderBoss (t4) |
|---|---|---|
| hp | 300 | 1500 |
| moveSpeed | 2.8 | 3.2 |
| detectionRadius | 18 | 25 |
| visionAngle | 120 | 140 |
| attackRange | 2.2 | 2.5 |
| patrolRadius | 6 | 12 |
| 技能表项 | 50003：maxRange 2.2 / cd 1.5s / dmg 30 / crit 0 | 50004：maxRange 2.5 / cd 2.0s / dmg 20 / crit 0 |
| 布局点位 | `[4] (0, 0, -20)` | `[5] (-30, 0, -30)` |

**两条硬约束**：

1. `config.attackRange` 必须 **≤** 对应表项的 `maxRange`。怪物施法的形状范围直接取 `Config.attackRange`（`MonsterBtActions.cs:78`），`CombatRules.ValidateShape` 会拒收超限请求 —— 填不一致的表现是**怪物在攻击距离上静默冻结**（零日志、零计数）。上表两列已对齐。
2. `monsters[].skills` 是**装饰性字段**：全仓库无任何代码读取它（服务端走 `SkillIdOf` 硬映射）。照抄 `["Attack"]` 只为保持形制，不产生行为。

**参照**：`PlayerState` 默认 `Hp = MaxHp = 100`（`Server/Player/PlayerState.cs:20`），玩家基础攻击 50 伤害 / 无 CD、重击 90 / 0.5s。SpiderBoss 1500 血 = 约 10–15 秒的持续输出；20 伤害 / 2.0s = 挨 5 下死。

## 4. 客户端视图

| 位置 | 改动 |
|---|---|
| `NpcAnimator_DogKnight.controller` / `NpcAnimator_Spider.controller` | 新资产，内容见 §5 |
| `DogKnightMonster.prefab` | 基于 `DogPBR.prefab` 的 **Prefab Variant**：换控制器 + 挂 `NpcEntityAnimator`。原预制体不动，Asset Store 自带 `Scene01` 不受影响 |
| `SpiderMonster.prefab` | 从 FBX 新建：移除 legacy `Animation` 组件、根节点缩放 ×2、挂 Animator + `NpcEntityAnimator` |
| `NpcPrefabCatalog.asset` | 新 ScriptableObject：templateId → prefab |
| `RemoteEntityRegistry.CreateView` | Npc 分支查目录，命中实例化真模型；**未命中退回球体**（保持今天的降级行为） |
| `RemoteInterpolator` + `RemoteEntityRegistry.UpdateRender` | 快照缓冲增加 `actionState`/`dead`，按 `renderAt` 采样出状态并喂给驱动 —— 见 §6.1 |
| `RemoteEntityView` | 现在只挂 `RemoteEntityAnimator?`（真人用，`RemoteEntityRegistry.cs:210`）。怪物走新组件，视图需要并行的字段（或抽一个小接口）供渲染时驱动 |
| `ClientNetworkHost` | 新增 `_npcCatalog` 字段；订阅 `CombatNetworkBridge.OnCastBroadcast` 并路由给 registry |
| `DemoDay.unity` 的 `Net` 对象 | 把 `_npcCatalog` 指向新资产。**用 MCP 接线，不手改场景 YAML**（外部改场景会触发模态框阻塞主线程） |

### 4.1 蜘蛛的一个硬前提：动画类型必须从 Legacy 改成 Generic

`spider_myOldOne.FBX` 的 `animationType: 1` 是 **Legacy**（所以模型根上挂着 legacy `Animation` 组件，实测确认）。**legacy 片段不能给 AnimatorController 用** —— 必须改成 Generic 并让 Unity 生成 Avatar，13 段片段才成为 Mecanim 可用的。

两条路：

- **(a) 复制一份 FBX**（如 `spider_myOldOne_Mecanim.FBX`）改它的导入设置 —— 原资产与其演示场景 `fantasySpider.unity` 保持可用。**推荐**，与 DogKnight 走 Variant 保持同一原则：不动源资产。
- **(b) 直接改原 FBX** 的导入设置 —— 省一份资产，但 Asset Store 自带的演示场景会失去播放能力。

本设计按 (a) 走。

### 4.2 其它实现要点

- 蜘蛛 FBX 的 `globalScale: 0.01`（源数据按厘米），实测导入尺寸 1.662 × 1.741 × 0.748 m。**缩放做在预制体根 Transform 上，不动导入设置**。
- 两只怪的 Animator 一律 `Apply Root Motion = false`：位置由服务端权威、客户端只插值（DogKnight 原预制体已是 0，实测确认）。
- 怪物预制体是**纯美术**（DogKnight 实测 0 碰撞体、0 脚本），因此 `NpcEntityAnimator` **不做** `RemoteEntityAnimator` 那套「禁用实例上其它 MonoBehaviour」—— 但要把这个前提写进注释：将来谁往怪物预制体里加脚本，得自己处理。

## 5. 动画状态机

### 5.1 状态与片段

**DogKnight**（7 状态）

| 状态 | 片段 | 类型 |
|---|---|---|
| Idle | Idle_Battle | 循环 |
| Walk | WalkForwardBattle | 循环 |
| Run | RunForwardBattle | 循环 |
| Attack01 / Attack02 | Attack01 / Attack02 | 一次性 |
| Hit | GetHit | 一次性 |
| Death | Die | 一次性，**无出边、定格** |

未接入：`Defend`、`Dizzy`、`DieRecover` —— 协议里没有对应状态，服务端 BT 也没有防御/眩晕行为。

**SpiderBoss**（7 状态）

| 状态 | 片段 | | 状态 | 片段 |
|---|---|---|---|---|
| Idle | idle（循环） | | Attack01 / Attack02 | attack1 / attack2（一次性） |
| Walk | walk（循环） | | Hit | hit1（一次性） |
| Run | run（循环） | | Death | death1（一次性，定格） |

未接入：`hit2`、`death2`、`taunt`、`jump`、`refpose`、`allanims`。均为协议与行为都没有对应物；片段留在资产里，将来要接只需在本表加行。

### 5.2 控制器保留标准转移图，驱动侧写参数

**参数**：`MonsterState`(int：0=Idle 1=Walk 2=Run **3=攻击中** 4=Hit 5=Death)、`AttackVariant`(int 0/1)、`Attack`(Trigger)。

**为什么必须有 3=攻击中**：AnyState 转移**每帧评估**，条件持续为真就会反复触发。怪物交战中服务端**全程**保持 `ActionState = Attack`（`MonsterBtActions.cs:83`），若不把攻击窗口表达成一个「不接任何 locomotion 转移」的整型值，已经播上的攻击动画会被 locomotion 条件当场打断。

**转移图**（单层 Base Layer）：

| 从 | 到 | 条件 / 过渡 |
|---|---|---|
| AnyState | Idle / Walk / Run | `MonsterState == 0 / 1 / 2`，0.15s |
| AnyState | Hit | `MonsterState == 4`，0.1s |
| AnyState | Death | `MonsterState == 5`，0.1s |
| AnyState | Attack01 / Attack02 | `Attack` Trigger 且 `AttackVariant == 0 / 1`，0.1s |
| Attack01 / Attack02 | Idle | **exit-time 0.95** |
| Hit / Death | — | **不放任何出边** |

**所有 AnyState 转移一律 `canTransitionToSelf = false`** —— 否则条件持续为真就会反复重入：受击窗口内 `MonsterState == 4` 一直成立，受击动画会被一帧一帧地重播。

`Hit` / `Death` 不放出边是为了避开两个坑：受击片段比服务端硬直窗口短时，AnyState 会顺着仍然成立的 `MonsterState == 4` **反复重播受击**；而死亡本就该定格在末帧。两者的退出**由时间轴驱动** —— 服务端窗口结束、`ActionState` 变回 Idle/Run 时，AnyState 自然接管。

攻击的退出用 exit-time 交给 Animator 自己算，**因此不需要片段长度表**。

**驱动侧四条规则**（按此优先级执行）：

1. **排程的攻击到点**：写 `MonsterState = 3` → 置忙位与「待确认」→ 打 `Attack` Trigger。
   先写值是为了堵竞态：施法广播与状态快照在同一 tick 发出，客户端不保证谁先到；若 Trigger 先到而参数还停在 2(Run)，`AnyState → Run` 会立刻把挥砍打断。
2. **时间轴采出状态后**：若处于「待确认」且采到 `3 / Hit / Death` 任一值 → 清「待确认」（服务端确实进了攻击窗口；后两者是打断场景）。
3. **忙位为真且「待确认」已清**：不写 `MonsterState`（保持 3）。
4. **其余时间**：把采出的状态写进 `MonsterState`。

**为什么忙位不靠 `StateMachineBehaviour`**：草稿曾用攻击状态上的 `OnStateExit` 清忙位。它可行，但需要在建好的控制器资产上额外挂组件 —— MCP 的 `animator-modify` 不支持挂 SMB，只能走 `script-execute` 手改资产，而那条路在本项目有"资产写回后反序列化不正确"的前科。改用「时间轴确认」后：状态机逻辑**全部落在纯 C# 里、可无 Animator 单测**，控制器资产保持纯数据。

代价是驱动依赖一个前提：**服务端在排队施法的同一次 BT 动作里就置了 `ActionState = Attack`**（`MonsterBtActions.Attack` 确实如此，"待确认"必然在 ≤1 tick 内被确认）。若哪天服务端改成"先施法、稍后置状态"，这里会卡在忙位 —— 因为它是隐式契约，规则 2 里的 Hit/Death 分支是唯一的兜底。

**一致性并不依赖「驱动 CrossFade 还是控制器转移」** —— 两者都在同一个 `renderAt` 触发（§6.1）。既然不依赖，就该选可检查、能在 Animator 窗口里看见的标准画法。本节早先版本提出的「控制器不放转移图」是为了解决一致性问题，而那个理由不成立，故废弃。

### 5.3 驱动源：快照管状态，广播管挥砍

服务端在攻击冷却期间**一直保持 `ActionState = Attack`**（`MonsterBtActions.cs:83` 注释），客户端从快照里看不出「这是第几刀」。所以：

| 源 | 内容 | 驱动 |
|---|---|---|
| 快照 | `ActionState` + `Dead` | Idle / Walk / Run / Hit / Death |
| **`CastBroadcast`** | `CasterPlayerId` == 该 npc id | **每次挥砍一次**攻击动画 |

服务端确实广播怪物施法：`CastHandler.HandleNpcCastAsync` → 怪物 AOI 视野内观察者（D-P5-4）。

映射表：

| 来源 | 值 | → 目标状态 |
|---|---|---|
| 快照 `ActionState` | Idle(0) / Walk(1) / Run(2) / Hit(5) / Death(6) | 同名状态 |
| | Attack(3) | 写 `MonsterState = 3`（**攻击中**，该值不接 locomotion 转移）。采样路径与广播路径都会写它 —— 见 §5.2 规则 1 / 3 |
| | Skill(4) / Down(7) / GetUp(8) | 忽略，保持当前 |
| 快照 `Dead` 位 | true | Death（**优先于一切**，与真人侧 `RemoteEntityAnimator` 同惯例） |
| `CastBroadcast` | casterId 命中视图 | 播 Attack 一次，变体见 §6.2 |

## 6. 跨客户端一致性（本设计的核心）

需求：两个都看得见 Boss 的客户端，在同一时刻看到的位置、朝向、动作、状态一致。

**已经达标的**：位置与朝向。`RemoteEntityRegistry.UpdateRender` 对所有视图（含怪物）按「服务器时钟 − 2 tick」插值渲染（`ClientNetworkManager.cs:176` 每帧驱动，`ServerTickRateMs` 默认 67ms → 延迟约 134ms）。时钟收敛后，两个客户端在同一服务器时刻取到同一位置/朝向。

**时间基准（已核实，是本节的基石）**：`WorldSnapshot.ServerTimestamp`（`Server3C.cs:334`）与 `CastBroadcast.ServerTimestamp`（`CastHandler.cs:96`）**同为 `DateTime.UtcNow.Ticks`**，与 `ClockSyncDriver.ToServerTime` 同基准。所以「拿广播时间戳去和快照时间轴比较」这个动作在单位上是成立的。**将来任一处被改成 tick 序号之类的相对量，本节全部结论会静默失效** —— 实现时应加一条断言测试把它钉住。

**必须补的两件**（D7 全做）：

### 6.1 状态从同一根时间轴采样 —— **但只改客户端不够，必须同时改服务端**

**客户端侧**：把快照缓冲从「只存位置」扩成「位置 + 状态」：

- `RemoteInterpolator.AddSample` 增加 `actionState` / `dead` 两个参数
- `TryGetRenderState` 增加两个 out 参数；**状态是阶梯函数**——取「时间戳 ≤ renderAt 的最后一个样本」的状态（位置仍插值）
- `RemoteEntityRegistry.UpdateRender` 每帧用 `renderAt` 采出状态，喂给 `NpcEntityAnimator`

这样每个客户端**渲染出来的状态与它渲染出来的位置落在同一根轴上**，消掉了「消息到达抖动」。

**但仅此不足以跨客户端一致** —— 以下服务端事实已核实：

`AoiBandScheduler.ShouldSend` 的变化判定是 `moved = x != slot.X || z != slot.Z || rotY != slot.RotY`（`AoiBandScheduler.cs:58`）—— **只认坐标/朝向，不认状态**。静止怪物（例如交战中站桩的 Boss）的状态翻转只能等 `StaticKeepaliveInterval = 4` tick 的保活窗口，而保活相位**按 (observer, entity) 各记一份**（`Slot.LastSentTick`）。于是同一个状态翻转，不同观察者会在**不同的 tick** 首次收到 → 各自在自己的 renderAt 施加 → **跨客户端稳态偏差最多 4 tick（≈267ms）**，且不是只在进视野时才有。

代码里其实已经承认过这件事：`RemoteEntityRegistry.ApplySnapshot` 的注释写着「已在视野内的观察者最多要等 4 tick（≈264ms）的静止保活窗口才看到它翻位」。本设计要求比那更高，所以必须动服务端。

**服务端侧（必须补）**：把 `ActionState` / `Dead` 纳入 `ShouldSend` 的变化判定，**变化即立即发送**（照抄现成的「带变化立即发」分支，`AoiBandScheduler.cs:52-56` —— 带变化已经是这个语义）。这样状态翻转在全 AOI 的观察者上落在**同一个 tick**，偏差收敛到 ≤1 tick。

影响面：`ShouldSend` 签名 + `AoiBroadcaster` 调用点 + `Server.Tests/Aoi/AoiBandSchedulerTests.cs`（8 个用例）。带宽增量可忽略——状态翻转是边沿事件（每秒几次量级），远低于保活频率；`BandwidthBudgetTests` 用的是「带频率 × 实体数」的规划口径，且单实体实测 ≈33B 对规划口径 60B 有余量，**预算常量不需要动**。

**循环相位**：状态翻转时刻在所有客户端一致之后，各客户端进入同一状态、从第 0 帧起播、速度都是 1.0 → **相位自然一致**。不需要 `normalizedTime` 数学、不需要烘焙片段长度表、不需要漂移平滑。

### 6.2 攻击：变体确定 + 按服务器时间排程

两个子问题，都在广播里已有的 `ServerTimestamp` 上解决：

**变体确定性** —— `CastHandler.Resolution.Broadcast` 是**构造一次、发给所有观察者的同一个对象**，时间戳完全一致。所以变体不能靠客户端自增计数器（AOI 边缘进出的客户端会错位），而要从时间戳推：

```
variant = (int)((ServerTimestamp / VariantBucketTicks) % 2)     // 0 → Attack01, 1 → Attack02
VariantBucketTicks = 666_667                                    // 常量，≈1/15 秒（.NET ticks 是 100ns）
```

`VariantBucketTicks` 是**客户端共享的固定常量**，不随登录返回的 `ServerTickRateMs` 变 —— 一致性只要求所有客户端用同一个常量（两个相邻挥砍间隔 ≥ 冷却 1.5s ≫ 1 tick，除以该常量取模必然交替）。

**零协议改动。**

**起播时刻** —— 广播在 `< ServerTimestamp` 对应的渲染时刻到达时才播，即排进一个小队列，每帧 `Drain(renderAt)`，触发 `timestamp <= renderAt` 的项：

- 与插值器共用同一根时间轴（否则动画会比它所属的位置早约 134ms，且各客户端不一致）
- 迟到保护：时间戳已过去很久（卡顿、刚进视野）**立即触发**而非丢弃
- 视图销毁时清掉该视图的待播项

### 6.3 残差（诚实说明，勿按「逐帧相同」验收）

- 时钟同步误差（`ClockSyncDriver` 收敛精度）
- 各自帧率（≤1 帧，约 16ms）
- **状态翻转 ≤1 tick** —— 前提是 §6.1 的服务端改动落地；**不落地则最多 4 tick（≈267ms）**，那是既有代码的已知性质（`ApplySnapshot` 注释自陈）
- Unity Animator 自身采样
- **视图中途创建**：某客户端刚进入 AOI 才生成视图，它的循环动画从第 0 帧开始，而已在视野内的客户端正播到中段。下一次状态切换即重新对齐。这是 AOI 的固有性质，不影响「都看得见 Boss 的两个客户端」这一主场景。

## 7. 错误处理与降级

| 情况 | 处理 |
|---|---|
| 目录里没有该 templateId | 退回球体占位（今天的既有行为）+ 一条 warning |
| 视图没有 Animator / 控制器为空 | 视作「无可驱动」，不写参数（沿用 `RemoteEntityAnimator` 惯例——写了会刷 `Animator is not playing an AnimatorController` 错误日志） |
| 攻击广播到达但视图不存在（AOI 边缘） | 静默忽略；因为变体由时间戳推、不靠计数，**漏播不会导致后续错位** |
| 广播的 casterId 是玩家 | 忽略（只处理 Kind=Npc 的视图） |
| 服务端模板未知 | 既有 fail-fast（`SkillIdOf` 抛 + `CombatRules.TryGet` 校验），保持不变 |

## 8. 测试与验收

**EditMode**

1. `RemoteInterpolatorTests`（追加）：状态采样是阶梯函数（renderAt 落在两样本之间时取较早者）；`dead` 同样采样；位置插值行为不变（回归）
2. `NpcEntityAnimatorTests`（新）：映射表逐值断言（含 Attack 不驱动、Skill/Down/GetUp 忽略、`dead` 优先）；相同状态重复 Apply 不重复 CrossFade；**同一 `ServerTimestamp` 两次调用得出同一变体**；排程队列「到点播一次、重复 Drain 不重播、迟到立即播」
3. `RemoteEntityRegistryTests`（追加）：目录命中 → 视图对象是真预制体（不再是球体）；未命中 → 仍球体；视图销毁清掉待播项
4. `CombatNetworkBridgeTests`（追加）：`CastBroadcast`(casterId = npc) → registry 收到排程；casterId = 玩家 → 不排程
5. **控制器资产断言**（新）：两只控制器存在；状态名集合与片段映射符合 §5.1；**参数集合恰为 `MonsterState` / `AttackVariant` / `Attack`**；**Hit/Death 无出边、Attack 有 exit-time 出边** —— 防止以后被改坏
6. **时间基准断言**（新）：`WorldSnapshot.ServerTimestamp` 与 `CastBroadcast.ServerTimestamp` 必须同源同基准 —— §6 的基石，一旦被改成相对量要立刻红

**Server (xUnit)**

7. t3/t4 在四处齐备：`monster_config.json`、`SkillIdOf`、`CombatRules`、`CreateDefaultConfig`；并断言 `attackRange ≤ maxRange`（防「静默冻结」陷阱回归）
8. `SpawnDefaultLayout(6)` 生成 6 只、模板序列为 `2,1,1,2,3,4`
9. `AoiBandSchedulerTests`（追加）：**状态变化立即发送**（不受带间隔与保活间隔约束）；状态未变时既有的带频率/保活语义不变（回归）
10. `AoiBroadcasterTests`（追加）：同一状态翻转在全 AOI 观察者上落在**同一个 tick**

**PlayMode / 人工**

11. 跑现有 e2e（`MonsterWorldTests` 等）确认不回归 —— 它们用 `--monsters 4`，只碰前四点位
12. 人工截图：两只怪可见、动画在动、Boss 体型明显大于骑士

## 9. 明确不做

- **不扩 `ActionState` 协议**：Defend / Dizzy / Taunt / Jump 无服务端行为支撑，接了也是死状态
- **不动离线本地怪物模式**（`MonsterSpawner` / 旧 `AIBrain`）
- **不改 Slime/Wolf 与前四个布局点位**
- 不做命中特效、音效、名条改动
- **不动 AOI 的视野判定**（`GetObserversOf` / 迟滞）—— §6.1 只改发送**节拍**（状态变化即发），不改谁能看见谁
- **不给 Boss 加碰撞体**：服务端没有碰撞（`SimpleMoveSystem` 是点到点移动），客户端视图的碰撞体又被刻意禁掉（位置由快照插值写，见 `RemoteEntityAnimator` 注释）。所以玩家可以**穿过 Boss 的身体**。这是既有性质（球体占位时代也一样），只是 3.3m 的 Boss 更显眼。**记录为已知限制，本次不修**

## 10. 部署收尾（易漏）

- `production/deploy/webgl-demo/start-game-server.cmd` 的 `--monsters 2` → 6，并改掉那条「4 只怪会围住出生点」的注释（这是把新怪放进演示的唯一开关；6 只里新增的两只在距原点 20m 与 42m 处，开局不仇恨）
- 部署产物里有一份**独立副本** `production/deploy/webgl-demo/dist/server/Config/monster_config.json`（当前与源一致）—— 需跑 `publish-server.sh` 重生成或同步，否则部署出去的演示服务器没有新怪

## 11. 风险

| 风险 | 说明 |
|---|---|
| 一致性依赖时钟收敛 | 收敛前（约 1.6s 突发期）动画与位置都会短暂退化——既有性质，非本设计引入 |
| **服务端节拍改动会碰 P3 语义** | 往 `ShouldSend` 加「状态变化即发」会动到带宽/节拍回归面（`AoiBandSchedulerTests` 8 例 + `BandwidthBudgetTests`）；预算常量不需动，但必须用回归测试锁住带宽口径 |
| **时间基准是隐式契约，无编译期保护** | 快照与广播的时间戳靠「两处都写 `DateTime.UtcNow.Ticks`」维持一致；谁改了一处不会编译报错，只会静默破坏一致性。第 6 条测试是唯一防线 |
| 攻击竞态 | 广播与状态快照同 tick 发出、到达顺序不保证；靠 §5.2 规则 1「先写 3 再打 Trigger + 当场置忙位」堵住。实现时不要"简化"掉这一步 |
| 蜘蛛改 Generic 需要重新导入 | 复制 FBX 的方案下不影响原资产，但多一份网格/片段资产 |
| Walk 状态实际不可达 | 服务端 `MonsterBtActions` 只写 `Idle` 与 `Run`，从不写 `Walk`。控制器里的 Walk 是预留位，不要误以为它该动 |
