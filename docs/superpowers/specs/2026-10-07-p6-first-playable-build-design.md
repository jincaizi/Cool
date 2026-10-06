# P6 · 第一个可交付包 设计

> **它是什么**：把项目从"开发者本机 Editor 里能跑"推进到"**一个 PC 安装包发给别人能玩**"。
> 这是"打怪升级 → PvP"路线的第一个子项目，目标不是把玩法做完，而是**先打通交付管道并修掉最伤可玩性的缺陷**，让后续每个子项目（P7 升级闭环、P8 PvP）完成时都能立刻出一个包。

## 0. 前置结论（本设计的事实基础）

六个领域的只读评估结论汇总（证据见各节）：

| 领域 | 状态 | 关键事实 |
|---|---|---|
| 3C 系统 | 勉强 | 骨架完整，但**受击反馈链断开**（`HitFSM` 近乎空转）、相机永不旋转 |
| 技能系统 | 勉强 | 8 个技能只有 4 个服务端化；**伤害结算次数随帧率变化** |
| 伤害与数值 | 有硬伤 | **单机怪物实际造成 0 伤害**；数值三源不一致 |
| 怪物刷新 | 有缺陷 | **单机刷怪爆炸**；模式切换产生幽灵怪 |
| 联网测试 | 仅限本机 | 服务端就绪，但**地址硬编码、无可交付包、无鉴权** |
| PvP | 最接近 | 服务端伤害链已完全贯通，缺死亡/复活/胜负 |

**贯穿性判断**：绝大多数缺口是"**做了但没接线**"，而非"没做"。真正从零开始的只有等级经验系统（归 P7）与移动端输入（本批次不做）。

## 1. 交付物定义

一个 **Windows PC 独立安装包**，收件人：

1. **零配置可玩**：默认单机模式，双击即玩——能打怪、被打有受击反馈、怪物能打死你、死后能复活
2. **可选联网**：能在**游戏内输入**服务器地址/端口/昵称（不需要 Unity、不需要改文件）
3. **出错有出口**：连接失败、被拒、断线都有**屏幕可见提示**，断线能退回主界面

**成功判据**：一个不懂技术的人，拿到包和一句"服务器地址是 X"，能自己连上并玩起来。

## 2. 范围边界

### 做（6 处改造点 + 3 件交付物）

| # | 项 | 类型 |
|---|---|---|
| ① | 受击反馈接线（含动画层切换） | 接线 |
| ② | 伤害结算帧率无关化 | 修复 |
| ③ | 相机旋转接线 + 防穿墙 | 接线 |
| ④ | 单机怪物伤害修复 + 数值对齐 | 修复 |
| ⑤ | 刷怪爆炸修复 | 修复 |
| ⑥ | 幽灵怪清理 | 修复 |
| ⑦ | 单机本地死亡 / 复活闭环 | 接线 |
| 3.1 | 服务器地址可配（游戏内输入） | 新建 |
| 3.2 | 构建脚本 | 新建 |
| 3.3 | 断线返回主界面 | 新建 |

### 明确不做

| 不做 | 归属 |
|---|---|
| 属性缩放接线（`_attackRatio` / `Stats` 注入） | P7 升级闭环 |
| 等级 / 经验 / 掉落 / 升级面板 | P7 升级闭环 |
| 火球 / 蓄力 / 大招服务端化 | 延后 |
| **联网的**死亡 / 复活 / 胜负判定 | P8 PvP |
| 移动端输入、摇杆、触屏相机 | 砍掉（PC 形态不需要） |
| 真实角色 / 怪物模型与动画 | 延后 |
| 自动重连、AggroTable、反作弊收紧、服务端随机数加固 | 延后 |
| 打断链接线（`SkillCoordinator.OnDamageTaken`） | 见 §9 风险 R4 |

## 3. 现状诊断

证据均为 2026-10-07 实测代码行。

### 3.1 受击反馈链断开（最严重）

三条伤害路径（`Sys3CEntry.cs:512`、`Combat/PlayerHitZone.cs:35`、`Sys3CEntry.cs:410` 服务端伤害）都汇聚到 `StateCoordinator.HandleDamage`（`Sys3C/Core/StateCoordinator.cs:54-63`）。该方法：

```
扣除韧性 → 发 DamageEvent → 发 HitReceivedEvent
```

**从不调用 `HitFSM.EnterHit`**。`HitReceivedEvent`（`Sys3C/Core/Events/DamageEvents.cs:37`）全项目无订阅者。唯一进入 `HitFSM` 的入口是盾破（`FSMManager.cs:135`）与无人调用的 `TriggerHit`（`FSMManager.cs:152`）。

**后果**：`HitFSM`（485 行）与 Animator 第 3 层近乎空转——玩家被打不播受击 / 击退 / 浮空 / 倒地 / 死亡。

### 3.2 伤害结算随帧率变化

`SkillStateMachine.UpdateExecution`（`Skills/Runtime/SkillStateMachine.cs:286-318`）中：

```csharp
float window = Mathf.Max(0.05f, _lastDeltaTime * 3f);   // 帧率自适应窗口
if (_elapsedTime >= hitboxTime && _elapsedTime < hitboxTime + window)
    _onHitboxFrame?.Invoke(i);        // 窗口内每帧都触发
```

窗口内**每帧重复触发**。下游 `SkillExecutor.OnHitboxTriggered`（`Skills/Runtime/SkillExecutor.cs:376-412`）用 `consecutiveRequired = 3`（连续 3 次调用才结算）来"抵消"重复触发：

| 帧率 | 窗口帧数 | 结算次数 |
|---|---|---|
| 60fps | 3 | 1 ✅ |
| 120fps | 6 | **2** ❌ |
| 30fps | 3 | 1 ✅ |

**后果**：高帧率设备伤害翻倍；掉帧跳过窗口则伤害静默丢失。

**对照**：同文件的 `UpdateCharging`（`:174-182`）已用 `_currentTick` 游标保证每窗口只触发一次；`UpdateSpinning`（`:192-207`）用 `_nextTickTime` 时间门控（每帧最多一个 tick）。**旋转技能不受本缺陷影响。**

### 3.3 相机永不旋转

- `ThirdPersonCameraController.HandleRotationInput`（`Sys3C/Camera/ThirdPersonCameraController.cs:60`）**全项目零调用者** → 相机角度停在初始值（`:46`）
- `Sys3CEntry.cs:210` 手动调 `_camera.Update()`，而该类是 MonoBehaviour 且 `public void Update()`（`ThirdPersonCameraController.cs:74`）→ **每帧更新两次**
- 相机仅 Lerp 跟随，无 SphereCast/Raycast（`:74-100`）→ 贴墙穿模

### 3.4 单机怪物造成 0 伤害

`DamageBlock.CalculateFinalDamage`（`Skills/Data/DamageBlock.cs:83-90`）取伤害的路径是：

```csharp
float damage = _baseDamage;
if (attackerStats != null) damage += attackerStats.GetAttribute(_scalingAttribute) * _attackRatio;
```

而客户端怪物资产的 `_baseDamage = 0`（`Assets/Monstor/SlimeConfig.asset:51`），且 `Stats` 恒 null——`PlayerHitZone` 甚至**显式传 `null`**（`PlayerHitZone.cs:29` 的 `CalculateFinalDamage(null)`），`Sys3CEntry.cs:571` 与 `MonsterEntity.cs:333` 的 `Stats` 属性也恒为 null → **最终伤害 0**。

**后果**：单机模式完全没有威胁。

### 3.4b 单机"怪物→玩家"存在两条并行路径，其一不扣血

| 路径 | 机制 | 是否扣玩家 HP |
|---|---|---|
| (a) 怪物 `AttackShape` → `IDamageable.TakeDamage`（`Sys3CEntry.cs:431`） | 扣 `_currentHP`，含防御修正器 | ✅ 扣 |
| (b) `PlayerHitZone.OnTriggerStay`（`Combat/PlayerHitZone.cs:19-42`） | 调 `FSMManager.HandleDamage` → `StateCoordinator.HandleDamage`——**只扣韧性 + 发事件** | ❌ **不扣** |

`FSMManager.HandleDamage`（`FSM/FSMManager.cs:93-107`）通读确认：构造 `DamageEvent` 后直接转交 `StateCoordinator`，**全程不触碰任何 HP 字段**。

**玩家 HP 权威判定**：

| 候选 | 值 | 判定 |
|---|---|---|
| `Sys3CEntry._currentHP` / `_maxHP`（`Sys3CEntry.cs:23-24`） | 100 | ✅ **权威**——`IDamageable` 与联网 `ApplyServerDamage` 都用它 |
| `CharacterStatsAdapter.Health`（`CharacterAdapters.cs:23`） | 1000 | ❌ 非权威——其消费方 `CharacterController.ApplyDamage` **全项目无调用者** |

**影响**：若 (b) 与 (a) 同时触发，会出现"受击反应两次、HP 只扣一次"的重影；若某类怪物只走 (b)，玩家血永远不掉。

### 3.5 刷怪爆炸

`MonsterSpawner`（`Monster/MonsterSpawner.cs`）：

- `HandleMonsterDeath`（`:136-148`）按 `MonsterId` 记一个计时器
- `Update`（`:51-62`）计时器到期 → 调 `SpawnGroupInternal(group)` → **重生整组 `Count` 只**（`:68`）
- `_aliveMonsters`（`:24`）从未被用作上限

**后果**：11 只史莱姆杀掉 1 只，30 秒后 +11 只，**存活数只增不减**（11→21→32…）。

**叠加缺陷**：`Spawn()` 在 `:129` 每次执行 `entity.OnDeathComplete += ...`，而怪物对象经 `_pool` 复用（`:146-147`）→ **订阅累积**，同一只怪死亡会触发多个处理器，放大爆炸。

### 3.6 幽灵怪

`ClientNetworkHost.TryAttachLocalCharacter`（`SyncLayer/ClientNetworkHost.cs:253-258`）在联网模式下：

```csharp
spawner.gameObject.SetActive(false);   // 只停用，不销毁、不注销
```

而 `PhysicsRegistry.FindNearby` 只检查 `IsAlive`、**不检查 `activeInHierarchy`**（`Sys3C/Core/Combat/PhysicsRegistry.cs:67-79`）→ 怪物虽不可见，**仍占据物理实体、空地能打出伤害数字**，名牌残影。

## 4. 设计 · 六处改造点

### ① 受击反馈接线

**改动点**：`StateCoordinator.HandleDamage`

```
HandleDamage(DamageEvent damage)
  ├─ 免疫判定（既有，保留）
  ├─ 扣韧性（既有，保留）
  ├─ 构造 HitData{ Damage, KnockbackForce, LaunchForce, StunDuration }   ← 新增
  ├─ bool entered = HitFSM.EnterHit(hitData)                              ← 新增
  └─ if (entered) SetActiveLayer(LayerType.Hit)                           ← 新增（关键）
```

**为什么必须切层**：`HitFSM` 是独立层，而 `_activeLayer` 决定 `CanMove` / `CanAttack`（`:18-20`）。若不切层，会出现"`_activeLayer` 仍是 Attack、`HitFSM` 却在播受击"的矛盾态——**Hit 层与 Attack 层同时驱动 Animator**，视觉撕裂。

**字段映射**：`DamageEvent` 已带 `KnockbackForce` / `LaunchForce` / `StunDuration`（`Sys3C/Core/Events/DamageEvents.cs:14-24`），与 `HitData` 所需字段逐一对应，**不需要新增协议或事件**。

**优先级语义**：`HitFSM.EnterHit` 返回 `false` 表示被更高优先级状态拒绝（`HitFSM.cs:200-205`）——此时**不切层**，尊重既有优先级规则。

**死亡路径分离**：`HitFSM.EnterHit` 中 `Damage >= 9999` 才进 Death（`HitFSM.cs:212`），那是"秒杀"语义。**正常死亡不走 `EnterHit`**，走既有 `StateCoordinator.HandleDeath()`（`:65-73`，已含 `SetActiveLayer(Hit)` + `LockState`）。

**受击恢复**：`HitFSM.Recover()` 在结束时触发 `OnHitComplete`（`HitFSM.cs:279-286`）。需订阅该回调 → 调 `StateCoordinator.UnlockAndReturnToBase()`（`:121-130`，已存在）→ 回到 Base 层。

**依赖**：本项**必须在 ④ 之后实施**——单机怪物当前伤害为 0，先做 ① 无法验证。

### ② 伤害结算帧率无关化

**采用同文件已有的游标模式**（与 `UpdateCharging` 同构），让每个判定窗口**只触发一次**：

```
UpdateExecution
  for (int i = _executionTimingCursor; i < hitboxTimings.Length; i++)
      window = Mathf.Max(0.05f, _lastDeltaTime * 3f)      // 保留既有自适应窗口宽度
      if (_elapsedTime 落在 [hitboxTime, hitboxTime + window) 内)
          _executionTimingCursor = i + 1                  // 游标推进——本窗口不再触发
          _onHitboxFrame?.Invoke(i)
```

**必须新增独立字段 `_executionTimingCursor`，不得复用 `_currentTick`**：`_currentTick` 已被 `UpdateCharging`（`:174-182`，蓄力期判定帧游标）和 `UpdateSpinning`（`:203-206`，每 tick 自增的旋转计数器）**两个语义不同的路径共用**。蓄力技能的状态链是 Charging → Execution，若复用同一字段，Charging 阶段推进过的值会带入 Execution，导致第二个阶段**少触发或跳过判定窗口**。独立字段彻底消除这层跨状态耦合。

进入 Execution 状态时 `_executionTimingCursor` 归零。

**下游同步简化**：`SkillExecutor.OnHitboxTriggered` 的非旋转分支**删除 `consecutiveRequired = 3` 与 `_consecutiveHits` 字典**，改为"每次调用对本帧检测到的每个目标结算一次"——因为调用本身已保证"每窗口恰好一次"。

**保留**：`Mathf.Max(0.05f, _lastDeltaTime * 3f)` 的窗口宽度（它解决的是"低帧率下窗口太窄采不到"的问题，与本次缺陷正交，且注释记录它曾修复 30fps 蓄力无伤害）。

**不改**：`UpdateSpinning`（`:192-207`）与 `HandleSpinTick`（`SkillExecutor.cs:432`）——旋转技能走独立时间门控路径，本就帧率无关。

**行为变化（预期）**：伤害从"窗口内第 3 帧"提前到"窗口进入帧"。打击更跟手，节奏略快。这是修复的必然结果，非副作用。

### ③ 相机接线 + 防穿墙

1. **接线**：在输入更新处调用 `_camera.HandleRotationInput(_inputManager.GetCameraRotationInput())`
2. **消除双更新**：`Sys3CEntry.cs:210` 的手动 `_camera.Update()` **不能直接删除**——它保证了"角色更新完再更新相机"的顺序，裸删会让相机在角色移动前更新而**抖动一帧**。改为把相机跟随移入 `LateUpdate()`（Unity 保证在所有 `Update` 之后执行）
3. **防穿墙**：跟随计算后加一次从目标到期望相机位的 SphereCast，命中则把相机拉到碰撞点内侧

### ④ 单机怪物伤害修复 + 数值对齐

1. **填伤害**：把客户端怪物资产的 `BaseDamage` 从 0 改为实际值
2. **数值对齐服务端**（消除"三源不一致"）

| 怪物 | 客户端现值 | 对齐后 | 服务端来源 |
|---|---|---|---|
| Slime HP | 600 | **100** | `Server/Config/monster_config.json` |
| Slime 伤害 | 0 | **10** | `CombatRules` 50001 |
| Wolf HP | 无配置 | **150** | `monster_config.json` |
| Wolf 伤害 | 无配置 | **15** | `CombatRules` 50002 |
| TurtleShell | HP 1500 / 伤害 2 | **HP 250 / 伤害 8** | 服务端无此模板，按其余两者同一量级定值 |

**对齐理由**：单机是评审的"第一印象"，联网是"真实形态"。若两者手感不同（Slime 客户端 12 刀 vs 服务端 2 刀），评审会以为游戏就是这么慢。

**TurtleShell 例外**：服务端没有对应模板，不强行制造对齐假象。原值 1500HP / 伤害 2 是"高血低伤"的坦克定位，压缩到同一量级时**保留该定位**取 250HP / 伤害 8（三者关系：Slime 100/10 脆而快、Wolf 150/15 均衡、TurtleShell 250/8 厚而钝）。

### ⑤ 刷怪爆炸修复

`MonsterSpawner` 改造：

1. **按组维护存活数**：新增 `Dictionary<SpawnGroup, int> _aliveByGroup`，`Spawn` 时 +1、死亡时 -1
2. **一次死亡只补一只**：`HandleMonsterDeath` 改为登记"该组待重生 1 只"，而非按 `MonsterId` 记整组计时
3. **重生回初始生成点**：怪会追击玩家，若被引到远处打死，按死亡位置重生会让怪群逐步漂移出刷怪区。**生成时记录该怪所属组与初始坐标，重生时复用**
4. **修订阅累积**：`OnDeathComplete` 改为先 `-=` 再 `+=`（或改为非累积绑定），消除池化复用导致的重复订阅

### ⑥ 幽灵怪清理

给 `MonsterSpawner` 新增 `DespawnAll()`：

```
DespawnAll()
  foreach (entity in _aliveMonsters) Destroy(entity.gameObject)
  清空 _aliveMonsters / _respawnTimers / _pool
```

**不手动注销 `PhysicsRegistry` / `EntityDisplayManager`**——`MonsterEntity.OnDestroy`（`MonsterEntity.cs:159-160`）已负责注销，手动再注销一遍是冗余且有双重注销风险。实现时需确认该注销对重复调用幂等。

`ClientNetworkHost.cs:253-258` 调用 `DespawnAll()` 替代裸 `SetActive(false)`。

**已知限制**：`DespawnAll()` 后该 spawner 不再刷怪。因为模式在启动时确定、运行期不切换，可接受。

### ⑦ 单机本地死亡 / 复活闭环

**为什么需要**：④ 修好之后怪物能打死玩家，但当前玩家 `HP <= 0` **什么都不发生**——仍能移动、仍能施法。这比"打不死"更糟：评审会认为游戏坏了。

**现状（三处零件已存在，全都没接线）**：

| 零件 | 位置 | 状态 |
|---|---|---|
| `HitFSM.EnterDeath()` | `FSM/HitFSM.cs:244-251` | 已实现，会发 `DeathEvent` |
| `HitFSM.Resurrect()` | `FSM/HitFSM.cs:291-298` | 已实现，**无任何调用者** |
| `StateCoordinator.HandleDeath()` | `Core/StateCoordinator.cs:65-73` | 已实现（切层 + `LockState`） |
| `StateCoordinator.HandleResurrect()` | `Core/StateCoordinator.cs:75-79` | 已实现（回韧性 + 解锁回 Base），**无调用者** |

**设计**：

```
死亡判定点：Sys3CEntry._currentHP 变为 <= 0 处（§3.4b 已定权威 = _currentHP / _maxHP = 100）
  → 尚未死亡守卫
      → StateCoordinator.HandleDeath()     // 切 Hit 层 + 锁 Base 层
      → HitFSM.EnterDeath()                // 播死亡动画（既有，不经 EnterHit）
      → 启动复活倒计时

复活倒计时到期
  → _currentHP = _maxHP
  → HitFSM.Resurrect()                     // 回 None 态
  → StateCoordinator.HandleResurrect()     // 回韧性 100 + 解锁回 Base 层
  → 传送回玩家出生点
```

**HP 权威与路径纪律（依据 §3.4b）**：

- 单机玩家 HP 权威 = **`Sys3CEntry._currentHP`**（`_maxHP = 100`）。`CharacterStatsAdapter.Health = 1000` 与 `CharacterController.ApplyDamage` 无调用者，**本批次不改动、不依赖**
- **`PlayerHitZone` 路径保持不扣 HP**——不去给它补扣血逻辑，否则会与 `IDamageable.TakeDamage` 形成**双扣**。该路径继续只负责受击反应与韧性
- 死亡只在 `_currentHP` 变化处判定，保证"谁扣血谁判死"单一入口
- 实现时须确认怪物攻击确实经由 `IDamageable.TakeDamage` 扣到 `_currentHP`——这由 §8 的"单机怪物造成的伤害 > 0"测试兜底

**必须走 `HandleDeath()` 而非 `EnterHit(Damage=9999)`**：后者是"秒杀"语义的旁路，不会执行 `LockState` 与切层，会留下"死亡但能动"的坏状态。

**与服务端死亡严格分离**：本项只处理**单机模式**的本地 HP。联网模式下玩家 HP 由服务端权威（`PlayerState.Hp`）持有，服务端的死亡/复活属 **P8** 范围。因此本实现必须由"是否联网模式"守卫，不得在联网模式下自行判定死亡——否则会与服务端判断打架。

**复活参数**：倒计时取 **3 秒**（与 `MonsterModule` 的死亡展示 2s 同量级，给玩家一个明确的"我死了"的观感，又不至于等待过久）。复活位置为玩家出生点。

**复活位置**：出生点坐标复用现有出生点配置（与登录 `EnterWorld` 下发的出生点同源）。

## 5. 设计 · 可交付三件套

### 5.1 服务器地址可配

**现状**：`_serverHost` / `_serverPort` / `_username` 只有 Inspector 值（`ClientNetworkHost.cs:26-28`），但 `Connect(username, host, port)` 已支持覆盖（`:141-153`）——**接线口已存在**。

**设计**：在既有**选人面板**（`Assets/Scenes/CharacterSelect.unity` / DemoDay 内的选人面板）的"单机 / 联网"按钮附近，增加服务器设置输入区：地址 / 端口 / 昵称。值存入 `PlayerPrefs`，默认 `127.0.0.1:8000 / Player`。

**读取时机（关键，勿踩已知坑）**：地址必须在**每次 `Connect()` 被调用的那一刻**从 `PlayerPrefs` 读取，**不能只在 `ClientNetworkHost.Start()` 里读一次**。

原因：直接跑 `DemoDay` 场景时，选人面板的确认动作发生在 `Start()` **之后**——`ClientNetworkHost.cs:102-108` 的既有注释与 `_connectRequested` 守卫正是为这个时序打的补丁（`OnPlayerSpawned` 里补连接）。若地址在 `Start()` 固化，直接跑 DemoDay 的路径会**读到旧地址**，与既有缺陷同构。两条连接路径（`Start` 直连 / `OnPlayerSpawned` 补连）都必须拿到最新值。

**为什么用游戏内输入而非配置文件**：收件人联网必须知道服务器地址。配置文件对非技术用户存在"找错文件 / 编码 / 权限"三重风险；输入框零风险。且 `NetworkModeService` 已用 `PlayerPrefs` 存模式，沿用同一机制，不引入新概念。

### 5.2 构建脚本

**现状**：**不存在**。项目从未构建过 Player。

**设计**：新增 Editor 构建脚本（`Assets/Scripts/Editor/BuildScript.cs`），用 `BuildPipeline.BuildPlayer` 输出 Windows x64 包：

- **脚本后端：Mono**（非 IL2CPP）。PC 评审包以"出包快、风险低"优先；IL2CPP 留待移动端验证
- **场景列表显式声明**，不依赖工程 Build Settings 的当前状态
- 输出到固定目录；提供静态方法供命令行调用，便于每个里程碑反复出包

**风险等级：本批次最高。** KCP / protobuf 在 Player 下是否正常、场景资源引用是否完整、`OnGUI` 浮层是否工作——全部未验证。

### 5.3 断线返回主界面

**现状**：`OnDisconnected` 事件链路已通（`ClientNetworkHost.cs:56`、`:149`），`Disconnect()` 已存在（`:160-169`），但**无人订阅、无 UI、无场景跳转**。

**设计**：

```
ClientNetworkHost 内部订阅自身 OnDisconnected
  → 弹出提示面板（复用 UI 框架既有 Tips / Confirm 组件）
  → 用户确认 → Disconnect() 清理 → SceneManager.LoadScene(选人场景)
```

单机模式不触网，不会触发该路径。

## 6. 错误处理

**PC 包的关键转变：评审看不到 Console。** 现有错误全部只走 `Debug.Log`：

| 场景 | 现状 | 目标行为 |
|---|---|---|
| 连接失败 / 服务器不可达 | `ClientNetworkHost.cs:155-158` 仅日志 | **屏幕提示**："无法连接 `<地址>:<端口>`" + 可返回 |
| 登录被拒 | 同上 | 屏幕提示 + 返回选人界面 |
| 断线 | 有事件无 UI | 提示 + 返回主界面（§5.3） |
| 怪物 Prefab 缺失 | `MonsterSpawner.cs:91` 已有 LogError | 保持日志（开发者问题，非评审可处理） |
| 单机模式 | 不触网 | 无变化（保证离线可玩） |

**原则**：凡是会让评审"卡住且不知道怎么办"的路径，都必须有屏幕可见的出口。

## 7. 实施顺序与依赖

```
④ 单机怪物伤害+数值对齐   ← 必须最先（① 的验证前提）
      ↓
① 受击反馈接线（含切层 + 受击恢复回调）
      ↓
⑦ 单机本地死亡 / 复活闭环        ← 复用 ① 打通的 HitFSM 链路
      ↓
⑤ 刷怪爆炸  +  ⑥ 幽灵怪清理      ← 同一文件（MonsterSpawner），合并改一次
      ↓
② 伤害帧率无关化
      ↓
③ 相机接线 + LateUpdate + 防穿墙
      ↓
3.1 地址可配  →  3.2 构建脚本  →  3.3 断线返回主界面
      ↓
出包 + 手工验收
```

依赖关系：**④ → ①** 是硬依赖（无伤害则无受击可验）；**① → ⑦** 也是硬依赖（⑦ 复用 ① 打通的 `HitFSM` 链路与切层机制）。⑤ 与 ⑥ 同文件，合并改动。

**3.2 构建脚本必须最先跑通一次（硬要求，不是"尽量"）**：在动任何战斗代码之前，先用当前代码出一个包并运行，确认打包链路本身可用。理由有二：① 打包是本批次最大的未知风险，早暴露才能早改路线；② 一次性改 7 处代码 + 首次打包，若打包失败将无法二分定位是"打包问题"还是"改动引入的问题"。**先拿到"当前代码能出包"这个基线**，之后每次改动都有对照。

## 8. 测试策略与验收

### 回归基线（必须保持全绿）

- Server.Tests **132/132**
- EditMode **200/200**
- PlayMode 现有用例

### 新增测试

| 层级 | 测什么 | 锁定 |
|---|---|---|
| PlayMode | 单机怪物造成的伤害 **> 0** | ④ |
| PlayMode | 刷怪数量守恒：杀 N 只后重生总数 **= N** | ⑤ |
| PlayMode | 受击后 `HitFSM` 进入非 None 状态，且 `_activeLayer` 为 Hit | ① |
| PlayMode | HP 归零 → 进入 Death 态且移动被锁；倒计时后回到 Base 层且 HP 满 | ⑦ |
| EditMode | 帧率无关性：同一判定窗口在不同帧间隔下**结算次数相同** | ② |
| EditMode | 数值一致性：客户端怪物资产与 `CombatRules` / `monster_config.json` 数值断言相等 | ④ 防漂移 |

### 手工验收（不可省）

1. **出包 → 双击 exe 运行**（不是 Editor PlayMode）
2. **单机**：打怪 10 分钟不爆；被打有受击 / 击退表现；被打死能复活
3. **联网**：本机起服务器，两台机器连同一服务器；互相可见；打怪正常；怪物攻击有表现
4. **断线**：杀掉服务器进程 → 客户端应在数秒内弹出提示，并能返回主界面
5. **可配置**：在游戏内改地址为错误值 → 应有明确失败提示，不卡死

## 9. 风险清单

| # | 风险 | 影响 | 应对 |
|---|---|---|---|
| R1 | **首次打包失败**（KCP / protobuf / 场景资源 / 依赖缺失） | 高 | 构建脚本尽早跑通一次；Mono 后端降低变量 |
| R2 | 相机改段驱动引入抖动或跟随延迟 | 中 | 改为 `LateUpdate` 而非裸删；实机验证 |
| R3 | ② 的修复改变打击节奏，需重新适应 | 低 | 属预期改善；手工验收确认手感 |
| R4 | **受击接线后暴露"边被打边放技能"**——打断链（`SkillCoordinator.OnDamageTaken`）无调用者，本批次不接线 | 中 | **明确接受**：受击期间技能继续执行。若要修属 P8 范围；本批次只保证不出现双状态崩溃 |
| R5 | 数值对齐后单机手感变化（Slime 600→100 HP） | 低 | 单机当前 0 伤害本就无手感；对齐后与联网一致 |
| R6 | `DespawnAll()` 依赖 `MonsterEntity.OnDestroy` 的注销是否幂等 | 低 | 实现时确认；若不幂等则显式去重 |
| R7 | **韧性双扣**：§3.4b 的两条并行路径若在同一次攻击中都触发，`StateCoordinator.HandleDamage` 会执行两次 → `_resistance` 扣两次（`StateCoordinator.cs:58-59`），破防速度翻倍 | 中 | **既有缺陷**，非本批次引入；但 ① 接线后受击表现变正常，玩家会更容易注意到破防异常。本批次**只登记不改**（改动会牵连韧性平衡，属 P7）。若验收时观察到频繁破防，提回处理 |
| R8 | 判定窗口在极端帧率波动下仍可能被整窗跳过（窗口宽度按 `_lastDeltaTime` 计算，帧间隔突变时可能失配），导致该窗口伤害丢失 | 低 | ② 保留既有 `Mathf.Max(0.05f, deltaTime*3f)` 自适应宽度已覆盖常规波动；PC 帧率稳定，实测风险低。若验收发现丢伤害，再改为"时间点已过即补触发" |
| R9 | §5.3 断线返回主界面若硬编码场景名，构建配置变更后会失效 | 低 | 场景名走显式常量/配置，不散落硬编码 |

## 10. 决策记录

| # | 决策 | 理由 |
|---|---|---|
| D-P6-1 | 交付形态为 **PC 独立安装包** | 用户选定；移动端输入非阻塞，可砍 |
| D-P6-2 | 先做 P6'（战斗有感 + 交付管道）而非直接做完整玩法 | 打包是最大未知风险，早暴露；交付管道早通，后续里程碑天然可交付 |
| D-P6-3 | 单机**做**本地死亡/复活闭环 | 血量归零后什么都不发生比"打不死"更糟 |
| D-P6-4 | 单机怪物数值**对齐服务端** | 单机是评审第一印象，手感应与联网一致 |
| D-P6-5 | 相机**做** SphereCast 防穿墙 | 贴墙穿模在 PC 上很刺眼，改动小 |
| D-P6-6 | 地址配置用**游戏内输入**而非配置文件 | 非技术用户零风险；复用既有 `PlayerPrefs` 机制 |
| D-P6-7 | 构建用 **Mono** 后端 | PC 评审包以出包快、风险低优先；IL2CPP 留待移动端 |
| D-P6-8 | ② 采用游标模式，但**新增独立字段** `_executionTimingCursor` | 同文件 `UpdateCharging` 已有同构先例；但 `_currentTick` 已被 Charging/Spinning 两种语义共用，复用会引入跨状态耦合 |
| D-P6-9 | 旋转技能（`UpdateSpinning`）不改 | 走独立时间门控路径，本就帧率无关 |
| D-P6-10 | 受击**切层**，并在受击结束回 Base 层 | 不切层会让 Hit / Attack 两层同时驱动 Animator |
| D-P6-11 | 怪物重生回**初始生成点**而非死亡位置 | 怪会追击玩家，按死亡位置重生会让怪群漂移出刷怪区 |
| D-P6-12 | `DespawnAll()` **不手动注销**外部系统 | `MonsterEntity.OnDestroy` 已注销，手动重复有双注销风险 |
| D-P6-13 | 打断链**不在本批次接线** | 与受击是不同语义，属 P8 范围；本批次只保证不崩溃 |
| D-P6-14 | 死亡走 `HandleDeath()`，**不走** `EnterHit(Damage=9999)` | 后者是秒杀旁路，不执行 `LockState` 与切层，会留下"死亡但能动"的坏状态 |
| D-P6-15 | ⑦ 由"是否联网模式"守卫，联网下不自行判定死亡 | 联网玩家 HP 由服务端权威持有，客户端自作判断会与服务端打架 |
| D-P6-16 | 复活倒计时 **3 秒**，复活回出生点 | 与 `MonsterModule` 死亡展示 2s 同量级；有明确观感又不至于等待过久 |
| D-P6-17 | 玩家 HP 权威 = `Sys3CEntry._currentHP`（100），**不是** `CharacterStatsAdapter.Health`（1000） | 后者唯一消费方 `CharacterController.ApplyDamage` 全项目无调用者，是死路径 |
| D-P6-18 | `PlayerHitZone` **保持不扣 HP**，不补扣血逻辑 | 它与 `IDamageable.TakeDamage` 并行，补扣会造成双扣；职责划清为"只负责受击反应与韧性" |
