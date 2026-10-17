# 精力系统（塞尔达式）设计

**Date:** 2026-10-18
**Engine:** Unity 2022.3.25f1
**Layer:** `Hotfix/Sys3C`、`Hotfix/Skills`、`Hotfix/Nameplate`
**状态:** 待实现

---

## 0. 结论摘要

为**本地玩家**增加一套塞尔达式精力：奔跑、举盾、持续型技能按秒消耗；精力归零进入力竭，强制中断当前动作，直到回复 20% 才能重新开始消耗动作。表现层是角色**右上方世界锚定**的绿色粗环：未满时显示并随消耗缩短，回满后淡出。

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| 权威归属 | **纯客户端** | 不改协议、不改服务端；演示/PvE 可接受"改客户端可绕过"的限制 |
| 消耗来源 | 奔跑 / 举盾 / 引导 / 旋转 / 蓄力（含可蓄力投射物） | 用户点名的三类 + 蓄力类完整覆盖 |
| 每帧消耗数 | **1 种**（持续技能 > 举盾 > 奔跑） | 避免双扣；非持续技能期间若**实际冲刺**仍照常扣奔跑（2026-10-18 审查修订） |
| 奔跑判定 | `_cc.Data.BaseState == BaseState.Sprint` | 真实移动状态；站着按 Shift、锁移动、眩晕、死亡都不空扣 |
| 空中冲刺 | **不扣** | `BaseState == Sprint && IsGrounded` 双重判定；Jump/走下平台都不扣，冲刺视为地面动作 |
| 技能扣精阶段 | 仅 `Charging / Channeling / Spinning` | Casting 是前摇、Execution/Recovery 是收尾，不是持续阶段 |
| 持续阶段 rate=0 | 压制奔跑扣精、但不扣精 | 角色在放技能，不是在冲刺 |
| 力竭行为 | 中断当前动作 + **20% 门槛** | 最接近塞尔达的"力竭"；防 0% 附近反复中断/重启 |
| 力竭拦截面 | 只拦"开始新的消耗动作" | 普攻、瞬发技能、移动、跳跃、回复不受影响 |
| 举盾锁存 | 被拒/强制收盾后**必须松开再按** | 防止按住不放时精力回到 20% 自动反复起盾 |
| 回复 | 最后一次消耗尝试后 0.75s 开始，33.3/s | 消耗期间（每帧都尝试扣）绝不回复 |
| 归还策略 | **不退还**已扣精力/已起冷却 | 旋转中断、蓄力提前释放、技能被打断一律不返还 |
| UI 位置 | 角色右上方世界锚定（屏幕投影） | 用户选定 C 方案（塞尔达式），不挂昵称 |
| UI 风格 | 塞尔达式粗环 + 深色底 | 用户选定 A 方案；草地背景下对比度最好 |
| UI 资产 | 运行时生成白环贴图 | 不新增美术资产；不依赖 UGUI 预制体 |

---

## 1. 现状与证据

| # | 事实 | 出处 |
|---|---|---|
| 1 | 精力字段已存在但从未被消费：`SkillData.StaminaCost`，全部技能资产为 0 | `Skills/Data/SkillData.cs:42-44`；`Assets/PreRes/SkillsCfg/*.asset` |
| 2 | 技能资源检查是空实现：`HasEnoughResources` 永远返回 true；`ConsumeResources` 为空 | `Skills/Runtime/SkillCoordinator.cs:506-516` |
| 3 | 持续型技能的子状态：Charged/可蓄力投射物 → `Charging`；Channeled → `Channeling`；Spin → `Spinning` | `SkillStateMachine.cs:88-113,172-219,267-292` |
| 4 | Casting / Execution / Recovery 不属于持续阶段；Spin 没有这些阶段 | `SkillStateMachine.cs:148-170,294-338` |
| 5 | 蓄力/引导/旋转期间允许移动的数据：`CanMoveWhileCharging`、`CanMoveWhileChanneling`、`Spin.MoveSpeedMultiplier` | `ChargedSkillData.cs:33-40`、`ChanneledSkillData.cs:37-40`、`SpinSkillData.cs:26-29` |
| 6 | 当前持续型资产：`Spin_SkillR`（Spin，3s，移速 ×0.5）、`ChargeFire_SkillR`（Charged，2s）、`Fireball_SkillQ`（Projectile，2s 可蓄力） | `Assets/PreRes/SkillsCfg/*.asset` |
| 7 | 打断矩阵与状态机都有默认拒绝分支：漏改任一处，力竭打断会**静默失败** | `SkillInterruptionMatrix.cs:130-154`、`SkillStateMachine.cs:369-380` |
| 8 | `SkillCoordinator.Update` 内 `_currentSkill.Update()` 的完成回调会把 `_currentSkill` 置空并处理缓冲输入（重入） | `SkillCoordinator.cs:393-428,476-504` |
| 9 | 奔跑输入只看 Shift：`GetMoveCommand` 内部 `IsSprintHeld()`，没有门禁 | `InputManager.cs:81-110,174-180` |
| 10 | 防御输入是按住式；进入/退出在 `HandleInput`；格挡成功/盾破有独立退出路径 | `Sys3CEntry.cs:276-291`、`CharacterController.cs:529-568` |
| 11 | 角色移动状态由 `_cc.Update` 每帧写入：`BaseState.Sprint` 只在"有移动输入 + IsSprint + 未锁移动"时成立 | `CharacterController.cs:433-465` |
| 12 | 服务端只校验位置/速度/冷却，没有精力概念；移动速度上报的是**真实水平速度** | `Server/Player/PlayerState.cs`、`SyncLayer/MovementReporter.cs:68-73` |
| 13 | 玩家没有头顶名牌；名牌系统只服务怪物。世界投影已有成熟范式（`NameplateRenderer`） | `Nameplate/NameplateRenderer.cs`、`EntityDisplayManager.cs` |
| 14 | `EntityDisplayCanvas` 是 Screen Space Overlay、sortingOrder 4500、CanvasScaler 参考 1920×1080 | `EntityDisplayManager.cs:43-55`、`GameSettings.cs:22` |
| 15 | 手机端没有冲刺按钮（只有 PC 的 LeftShift）；举盾有 UI 按钮 | `InputManager.cs:174-180`、`MobileInputBinder.cs` |

---

## 2. 目标与非目标

### 目标

1. 本地玩家拥有精力资源，奔跑/举盾/持续技能按秒消耗。
2. 归零力竭：立即中断当前动作，回到 20% 前不能再开始消耗动作。
3. 角色右上方显示绿色粗环：未满显示、消耗缩短、回满淡出、归零闪红。
4. 纯客户端，不新增协议字段、不改服务端、不改场景；默认值不需要改预制体。
5. 数据可调：消耗/回复数值在 Inspector，技能耗率在技能资产。

### 非目标

- 服务端精力校验与反作弊。
- 远程玩家/怪物头顶精力环；精力网络同步。
- 音效。
- 瞬发技能的 `StaminaCost`（现有字段保留不动，本次只做每秒消耗）。
- 翻滚/闪避/攀爬/游泳等游戏中不存在的动作。
- 手机端冲刺按钮（既有缺口，另一个 UI 任务）。

---

## 3. 核心模型

### 3.1 `StaminaSystem`（纯 C#）

位置：`Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaSystem.cs`

```csharp
public struct StaminaConfig
{
    public float Max;                       // 100
    public float SprintDrainPerSecond;      // 20
    public float DefendDrainPerSecond;      // 10
    public float RegenPerSecond;            // 33.333
    public float RegenDelay;                // 0.75
    public float ExhaustRecoverRatio;       // 0.2
}
```

| 成员 | 语义 |
|---|---|
| `Current` / `Max` / `Ratio` | `Ratio = Current / Max`，钳 [0,1] |
| `IsExhausted` | 曾经扣到 0；回到 `Max × ExhaustRecoverRatio` 时清除 |
| `IsFull` | `Current >= Max - 0.001` |
| `CanStartConsuming` | `!IsExhausted && Current > 0` |
| `TryConsume(amount)` | `amount` 非有限或 `<= 0` 直接返回 true 且**不重置回复延迟**；`Current <= amount` ⇒ `Current = 0`、`IsExhausted = true`、重置延迟、返回 **false**；否则扣减、重置延迟、返回 true |
| `Tick(dt)` | `dt` 非有限或为负按 0；单帧钳到 `[0, 0.1]`。累加"距上次消耗尝试"的时间；≥ `RegenDelay` 后按 `RegenPerSecond × dt` 回复（`Current` 钳到 `Max`）；回复到阈值时清除 `IsExhausted` |
| `Refill()` | `Current = Max`、清 `IsExhausted`、重置延迟 |

**契约**
- 消耗失败会**把当前值清零**（UI 显示空环），并返回 false；调用方必须立即停止动作。
- **恰好扣到 0 也算力竭**（`Current <= amount`，不是 `<`）。否则会出现 `Current == 0` 但 `IsExhausted == false`，`CanStartConsuming` 暂时为 false，回复一点后又绕过 20% 门槛立刻可动。
- 回复计时使用内部累加器，不读 `Time.time`，EditMode 测试可确定性模拟。
- **数值防护**（对齐本项目 `MovementValidator` 的 NaN/∞ 防护纪律）：`TryConsume` 忽略非有限/≤0 的 amount；`Tick` 忽略非有限/负 dt 并把单帧 dt 钳到 `[0, 0.1]`；构造时把非有限配置回退到 §3.2 的默认值，并钳制 `Max >= 1`、各速率 `>= 0`、`RegenDelay >= 0`、`ExhaustRecoverRatio ∈ [0,1]`。
- 消耗侧的 dt 同样钳到 `[0, 0.1]`（见 §5.3），避免一次卡顿把整条精力抽空。

### 3.2 默认数值

| 参数 | 值 | 推导 |
|---|---|---|
| 上限 | 100 | — |
| 奔跑消耗 | 20/s | 满精力 5.0s |
| 举盾消耗 | 10/s | 满精力 10.0s |
| 回复延迟 | 0.75s | 停手后短暂停顿 |
| 回复速度 | 33.333/s | 约 3.0s 回满 |
| 力竭解锁 | 20% | 0 → 20% 约 1.35s（0.75 + 0.6） |
| 旋转耗率 | 20/s | 3s 共 60 |
| 蓄力耗率 | 15/s | 2s 共 30 |
| 火球耗率 | 15/s | 2s 共 30 |

所有数值以 `[SerializeField]` 暴露在 `Sys3CEntry` 的 Inspector（默认值即上表），技能耗率写在技能资产。

---

## 4. 消耗时机与优先级

### 4.1 每帧只允许一种消耗

在 `Sys3CEntry.Update` 中、`_skillCoordinator.Update()` **之后**调用 `UpdateStamina(dt)`：

1. **持续技能阶段**（`Charging / Channeling / Spinning`）→ 由 `SkillCoordinator` 在自身 `Update` 内扣技能耗率；本帧不再考虑举盾/奔跑（rate=0 也压制）。
2. **举盾中**（`_cc.IsDefending && !_cc.StatusControllerAdapter.IsStunned && !_isDead`）→ 扣举盾。
3. **实际地面冲刺**（`_cc.Data.BaseState == BaseState.Sprint && _cc.Data.IsGrounded && !_cc.LockMovement && !举盾 && !眩晕 && !死亡 && _fsmManager.Coordinator.ActiveLayer != LayerType.Hit`）→ 扣奔跑。
4. 都没有 → 只 `Tick` 回复。

**非持续技能（Casting/Execution/Recovery）本身不扣精，但期间若实际冲刺（BaseState=Sprint），仍按第 3 条扣奔跑**——这是 2026-10-18 审查修订：旧版"任何活跃技能期间都不扣奔跑"会让长前摇/收尾期间按住 Shift 以冲刺速度移动却完全不付精力。

若本帧技能扣精已把精力置为力竭，跳过 2/3 分支，只 `Tick`。最后**每帧只调用一次** `_stamina.Tick(dt)`。技能扣精保持在内聚位置（`SkillCoordinator`），优先级由 `Sys3CEntry` 统一裁决。

**插入位置**：`UpdateStamina` 必须在 `_skillCoordinator.Update(dt)` 之后、`_cc.LockMovement = ...` 赋值之前调用。此时 `_cc.LockMovement` 还是**本帧 `_cc.Update` 实际使用的值**；放到末尾会读到下一帧的值，导致技能起手帧的冲刺扣精判定错一帧。

```csharp
private void UpdateStamina(float dt)
{
    if (_isDead) { _stamina.Tick(dt); return; }

    var sub = _skillCoordinator.CurrentSubState;
    bool sustained = sub == SkillSubState.Charging
                  || sub == SkillSubState.Channeling
                  || sub == SkillSubState.Spinning;
    float staminaDt = float.IsFinite(dt) ? Mathf.Clamp(dt, 0f, 0.1f) : 0f;

    if (!sustained && !_stamina.IsExhausted)
    {
        if (_cc.IsDefending && !_cc.StatusControllerAdapter.IsStunned)
        {
            if (!_stamina.TryConsume(_defendStaminaPerSecond * staminaDt))
                ForceExitDefendByExhaustion();      // 强制收盾 + 置锁存
        }
        else if (!_cc.IsDefending
                 && !_cc.StatusControllerAdapter.IsStunned
                 && _cc.Data.BaseState == BaseState.Sprint
                 && _cc.Data.IsGrounded
                 && !_cc.LockMovement
                 && _fsmManager.Coordinator.ActiveLayer != LayerType.Hit)
        {
            _stamina.TryConsume(_sprintStaminaPerSecond * staminaDt);
        }
    }

    _stamina.Tick(dt);
}
```

### 4.2 每种技能 × 每个阶段

| 技能类型 | 状态轨迹 | 扣精阶段 | 精力归零时 |
|---|---|---|---|
| Combo 普攻 | Execution → Recovery | **无** | 不影响，普攻照常 |
| Instant（Q / 陨石） | Execution → Recovery | **无** | 照常施放 |
| Charged（蓄力炎爆） | Casting 0.1s → **Charging** → Execution → Recovery | Charging | 按当前进度自动释放 + 上报 CastRequest |
| Projectile 可蓄力（火球） | Casting 0.1s → **Charging** → Execution → Recovery | Charging | 同上 |
| Projectile 瞬发（MaxChargeTime=0） | Execution → Recovery | **无** | — |
| Channeled（暂无资产） | Casting(CastTime) → **Channeling** → Execution → Recovery | Channeling | 中断（Cancelled） |
| Spin（R） | **Spinning**（含起手段）→ Completed | Spinning | 中断（Cancelled） |
| Ultimate 若为 Instant 数据 | Execution → Recovery | **无** | — |
| Ultimate 若改为持续型数据 | 按实际类型 | 按实际类型 | 按实际类型 |
| Passive / Item | 无持续状态 | **无** | — |

**持续型判定**（唯一真值源）：

```csharp
static bool IsSustained(SkillData d) =>
    d is ChargedSkillData
    || d is ChanneledSkillData
    || d is SpinSkillData
    || (d is ProjectileSkillData p && p.MaxChargeTime > 0f);
```

只有 `IsSustained(d) && d.StaminaDrainPerSecond > 0` 的技能会在力竭时被拒绝激活、才会扣精。

**持续性优先但不豁免非持续技能期间的冲刺**：`Charging / Channeling / Spinning` 期间压制举盾/奔跑；`Casting / Execution / Recovery` 期间若玩家实际在冲刺，照常扣奔跑。

### 4.3 各技能连招/中断语义

- **不退还**：已扣精力与已起冷却在任何中断路径都不返还。
- **蓄力归零**：走 `TryReleaseCharge`（与松手同源），按当前进度释放并照常上报一条 `CastRequest`；冷却按释放起算（现有规则）。
- **旋转归零**：`TryInterrupt(StaminaExhausted)`，进入 `Cancelled`；旋转的冷却在激活时已起算，不退。
- **引导归零**：`TryInterrupt(StaminaExhausted)`，剩余 tick/收尾伤害不再发生。
- **技能被眩晕/伤害打断**：不额外退还；力竭状态照常由精力值决定。
- **缓冲输入**：力竭时缓冲中的带消耗技能会被 `HasEnoughResources` 拒绝并按现有逻辑丢弃，不会在回复后自动重放。

### 4.4 扣精 / 回复全表

| 场景 | 扣精 | 回复 |
|---|---|---|
| 站立 / 行走 | 否 | 是（延迟后） |
| 地面冲刺（BaseState=Sprint，含非持续技能期间） | 20/s | 否 |
| 非持续技能（Casting/Execution/Recovery）期间实际冲刺 | 20/s | 否 |
| 空中保持 Shift | 否 | 是 |
| 按住 Shift 但站着 | 否 | 是 |
| 举盾中（未眩晕） | 10/s | 否 |
| 举盾中眩晕 | 否 | 是 |
| 持续技能阶段 rate>0 | rate/s | 否 |
| 持续技能阶段 rate=0 | 否 | 是 |
| 技能 Casting / Execution / Recovery（未冲刺） | 否 | 是 |
| 普攻 / 瞬发技能期间（未冲刺） | 否 | 是 |
| 锁移动 / 眩晕 / 非格挡受击 | 否 | 是 |
| 举盾格挡命中（DefendHit） | 10/s | 否 |
| 死亡 | 否（环隐藏） | 是；复活回满 |
| 力竭后未到 20% | 否（消耗动作被拒） | 是 |

**回复计时**：最后一次 `TryConsume`（成功或失败）之后 0.75s 开始回复，33.333/s。持续消耗每帧都会重置延迟，因此消耗期间绝不回复。

**力竭**：`Current == 0` 时置位；`Current >= Max × 0.2` 清除。只拦截**开始新的消耗动作**（奔跑、举盾、带消耗的持续技能）；普攻、瞬发技能、移动、跳跃、回复不受影响。

---

## 5. 各系统接线

### 5.1 奔跑

- `InputManager` 新增运行时开关：
  ```csharp
  public bool SprintAllowed { get; set; } = true;
  // GetMoveCommand:
  bool sprintHeld = IsSprintHeld() && SprintAllowed;
  ```
  默认 true，现有调用方/测试行为不变。
- `Sys3CEntry.Update` 在 `GetMoveCommand` 之前：`_inputManager.SprintAllowed = _stamina.CanStartConsuming;`
- 扣精条件：`_cc.Data.BaseState == BaseState.Sprint && _cc.Data.IsGrounded && !_cc.LockMovement && !_cc.IsDefending && !_cc.StatusControllerAdapter.IsStunned && !_isDead && _fsmManager.Coordinator.ActiveLayer != LayerType.Hit`。
  - `IsGrounded` 是必须的：走下平台时 `IsGrounded` 会先变 false，但 `BaseState` 可能仍停在 Sprint；没有这个守卫会"空中空扣"。
  - `!LockMovement` 同样是必须的：锁移动时 `UpdateBaseState` 提前返回，`BaseState` 会停在 Sprint；没有这个守卫会"锁移动空扣"。
  - `StatusControllerAdapter` 是 [CharacterController.cs:121-129](Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs#L121-L129) 的**实例属性**，不是静态类；必须写 `_cc.StatusControllerAdapter.IsStunned`。
  - `ActiveLayer != Hit` 用来兑现"受击不扣奔跑"——被打时 BaseState 可能仍停在 Sprint。
- 扣精量：`SprintDrainPerSecond × dt`（dt 钳到 `[0, 0.1]`）。
- 非持续技能（Casting/Execution/Recovery）本身不扣精；但期间若实际冲刺（BaseState=Sprint），仍按本条扣奔跑。持续技能阶段由优先级压制，不会走到这里。
- 精力归零当帧仍可能以冲刺速度移动一帧；下一帧 `SprintAllowed=false`，自动回到走路。
- 力竭门禁只影响"是否进入冲刺"；举盾限速、技能移速倍率、空中控制等既有逻辑**不变**。

### 5.2 举盾

- `HandleInput` 进入防御前检查 `_stamina.CanStartConsuming` 与锁存：
  ```csharp
  if (_inputManager.IsDefendHeld())
  {
      if (!_defendStaminaLatch && _stamina.CanStartConsuming
          && _fsmManager.CanDefend && _cc.TryEnterDefend())
          _fsmManager.EnterDefend();
      else if (!_stamina.CanStartConsuming)
          _defendStaminaLatch = true;   // 力竭时按下/按住：必须松开再按
  }
  else
  {
      _defendStaminaLatch = false;
      if (_cc.IsDefending) { _cc.TryExitDefend(); _fsmManager.ExitDefend(); }
  }
  ```
- 举盾期间每帧 `TryConsume(DefendDrainPerSecond × staminaDt)`（`staminaDt` 为钳制后的 dt，见 §4.1）；失败 ⇒ `TryExitDefend()` + `ExitDefend()` + 置锁存。
- 眩晕/死亡期间不扣；盾破/背面受击的既有退出路径不变。
- **与盾耐久解耦**：现有盾耐久由服务端按格挡次数扣除（`ShieldDurabilityPerBlock`）；精力按持盾时间扣。两者互不抵扣、互不恢复。
- 服务端 `IsDefending` 上报链路不改：强制收盾后下一次位置同步自然上报 `false`。

### 5.3 持续型技能

`SkillCoordinator` 新增可选依赖（不注入时行为完全不变）：

```csharp
private StaminaSystem _stamina;
public void SetStamina(StaminaSystem stamina) => _stamina = stamina;
```

**激活门禁**（复用现有 TODO 钩子）：

```csharp
private bool HasEnoughResources(SkillData skillData)
{
    if (_stamina == null) return true;
    if (!IsSustained(skillData) || skillData.StaminaDrainPerSecond <= 0f) return true;
    return _stamina.CanStartConsuming;
}
```

`StaminaCost`（一次性消耗）本次保持未使用；现有资产全为 0。

**持续扣精**：在 `SkillCoordinator.Update` 中、`_currentSkill?.Update(dt)` 与满蓄力上报之后：

```csharp
private void UpdateSustainedStamina(float deltaTime)
{
    var skill = _currentSkill;                       // 抓局部：防完成回调把字段置空
    if (skill == null || !skill.IsActive || _stamina == null) return;
    var sub = skill.CurrentSubState;
    if (sub != Charging && sub != Channeling && sub != Spinning) return;
    float rate = skill.Data.StaminaDrainPerSecond;
    if (rate <= 0f) return;
    float staminaDt = float.IsFinite(deltaTime) ? Mathf.Clamp(deltaTime, 0f, 0.1f) : 0f;
    if (!_stamina.TryConsume(rate * staminaDt))
        HandleStaminaDepleted(skill, sub);
}

private void HandleStaminaDepleted(SkillExecutor skill, SkillSubState sub)
{
    bool resolved = sub switch
    {
        SkillSubState.Charging => TryReleaseCharge(skill.SkillId),
        SkillSubState.Channeling or SkillSubState.Spinning
            => skill.TryInterrupt(InterruptionSource.StaminaExhausted),
        _ => false
    };
    // 保险丝：两处放行是主防线；万一未来改动让打断被拒，
    // ForceComplete 防技能卡在持续态、每帧扣 0 精、玩家被软锁。
    if (!resolved) skill.ForceComplete();
}
```

处理完立即返回；被中断后的清理/缓冲输入由现有事件链负责。消耗侧 dt 同样钳到 `[0, 0.1]`，避免一次卡顿把整条精力抽空。

### 5.4 新打断来源

- `SkillState.cs` 的 `InterruptionSource` **追加** `StaminaExhausted`（枚举末尾，保持既有序列化次序）。
- **两处**放行，缺一不可：
  - `SkillInterruptionMatrix.CanBeInterrupted`：`if (source == StaminaExhausted) return true;`（无视霸体；先于自定义规则）。
  - `SkillStateMachine.CanBeInterrupted`：`InterruptionSource.StaminaExhausted => true`。
- `GetInterruptionPriority` 追加 `StaminaExhausted => 85`（低于伤害 90、高于招架 80），供未来多源竞争使用。

---

## 6. 表现层：精力环

### 6.1 组件与层级

- 新增 `StaminaRingRenderer`（普通 C# 类），由 `EntityDisplayManager` 持有，挂在现有 `EntityDisplayCanvas`（Screen Space Overlay，sortingOrder 4500）下。
- `EntityDisplayManager` 新增：
  ```csharp
  public void BindStamina(Transform owner, StaminaSystem stamina, Func<bool> visible);
  public void UnbindStamina(Transform owner);   // 按 Transform 引用匹配
  ```
- `LateUpdate` 在 `_nameplate.Tick(_camera)` 之后调用 `_staminaRing?.Tick(_camera, Time.deltaTime)`；`OnDestroy` 调用 `Cleanup()`。
- **懒绑定**：`Sys3CEntry.Start` 先尝试绑定；若 `EntityDisplayManager.Instance` 为空（异步初始化/测试场景），在 `Update` 里用 `_staminaBound` 做一次性重试。避免一次空引用就永远不显示环。
- **复用复位**：`Bind` 必须复位视觉状态——`CanvasGroup.alpha = 0`、红闪计时 = 0、`fillAmount = Current/Max`、可见性重算。否则角色重选时新角色会继承旧角色的红环/半透明环。
- `Tick` 先判 `_owner` / `_stamina` / `camera` 为空并隐藏，**再**调用可见性谓词——避免玩家被销毁后执行捕获了它的 `Func<bool>`。
- 无 `EntityDisplayManager` 的场景（测试）只跑逻辑，不报错。

### 6.2 视觉规格（风格 A）

| 项 | 规格 |
|---|---|
| 外径 | 默认 64 参考像素（CanvasScaler 缩放；`EntityDisplayManager` 序列化可调） |
| 环宽 | 默认约 10px（128×128 贴图，厚 20px；序列化可调） |
| 背景环 | 同形状白环染 `rgba(10,20,12,0.55)` |
| 填充环 | 绿色 `#5DFF6A` |
| 填充方式 | `Image.Type.Filled` + `Radial360`，`fillOrigin = Top`，`fillClockwise = false`（12 点起逆时针缩短）；实现后目视确认一次方向 |
| 贴图 | 运行时生成 128×128 抗锯齿白环；一次创建，`Cleanup` 销毁 |
| 定位 | `owner.position + up × 1.7m`（默认值，序列化可调）投影到屏幕 |
| 偏移 | `(64, 24) × canvas.scaleFactor`（默认值，保证不同分辨率下与环同比例） |
| 出屏处理 | 相机后方（z≤0）或锚点投影出屏 ⇒ **整环隐藏**；**不做边缘钳制**（避免环粘在屏幕边缘像 UI bug） |
| 显示 | 精力未满：0.1s 淡入；回满：0.25s 淡出后停用；淡出中重新消耗则反向淡入 |
| 力竭 | `IsExhausted` 上升沿整环闪红 0.35s，只触发一次 |
| 死亡/禁用/出屏 | 绑定传入 `() => !_isDead && isActiveAndEnabled`；这三种情况**立即隐藏**（不走淡出），避免尸体/禁用角色上残留环 |
| 交互 | 两张 `Image.raycastTarget = false`；`CanvasGroup.blocksRaycasts = false` |
| 层级 | 显示时 `SetAsLastSibling()`，压在名牌/浮动文字之上 |

### 6.3 测试可见状态

`StaminaRingRenderer` 暴露只读 `FillAmount` 与 `IsVisible` 供测试断言，不额外增加运行时代价。尺寸/偏移/头锚点来自 `EntityDisplayManager` 的序列化配置（默认值见 §6.2），测试可直接读取。

---

## 7. 数据与配置

### 7.1 技能数据

`SkillData` 在 `=== Cost ===` 区新增：

```csharp
[Tooltip("持续型技能（蓄力/引导/旋转）每秒消耗的精力；0 = 不消耗")]
[SerializeField] protected float _staminaDrainPerSecond;
public float StaminaDrainPerSecond => _staminaDrainPerSecond;
```

### 7.2 资产值

| 资产 | 类型 | 值 |
|---|---|---|
| `Spin_SkillR.asset` | Spin | 20 |
| `ChargeFire_SkillR.asset` | Charged | 15 |
| `Fireball_SkillQ.asset` | Projectile（可蓄力） | 15 |
| `Instant_SkillQ.asset` / `Meteor_Ultimate.asset` / 普攻 | 非持续 | 0 |

> 资产序列化有静默失败风险（本项目既有教训），必须由 EditMode 资产测试钉死上述值。

### 7.3 玩家配置

`Sys3CEntry` 新增 `[Header("Stamina")]` 序列化字段（默认值见 §3.2），并暴露 `public StaminaSystem Stamina => _stamina;` 供测试/调试。

### 7.4 表现参数

`EntityDisplayManager` 增加 `[Header("Stamina Ring")]` 序列化字段：外径（默认 64）、环宽（默认 10）、屏幕偏移（默认 64,24）、头锚点高度（默认 1.7m）。默认值即 §6.2；真机调参时保存对应场景/预制体即可，不需要改代码重出包。

---

## 8. 生命周期

| 时机 | 行为 |
|---|---|
| `Sys3CEntry.Start` | **最先**创建 `StaminaSystem`（早于引用判空）；`_skillCoordinator` 创建后再注入 `SetStamina(...)`；随后尝试绑定 UI |
| UI 绑定 | `EntityDisplayManager.Instance` 为空时，`Update` 里用 `_staminaBound` 做一次性懒绑定 |
| 死亡 | 不消耗；环隐藏；资源继续回复（不可见） |
| 单机复活 `TickRespawn` | `Refill()` + 清防御锁存 |
| 联网复活 `ApplyServerRespawn` | `Refill()` + 清防御锁存 |
| 角色重选 | 旧角色 `OnDestroy` 解绑（按引用匹配，不会误清新角色的环）；新角色 `Start` 重新绑定，`Bind` 复位视觉状态 |
| 场景切换 | `EntityDisplayManager` 常驻；环随解绑隐藏 |

---

## 9. 测试与验收

### 9.1 EditMode：`StaminaSystemTests`

- `test_stamina_sprint_rate_empties_in_five_seconds`
- `test_stamina_exhausted_blocks_start_until_twenty_percent`
- `test_stamina_regen_starts_after_delay`
- `test_stamina_consume_attempt_resets_regen_delay_even_when_insufficient`
- `test_stamina_try_consume_insufficient_clamps_to_zero_and_returns_false`
- `test_stamina_try_consume_exact_remaining_triggers_exhaustion`（等号边界，防绕过 20% 门槛）
- `test_stamina_zero_consume_does_not_reset_regen_delay`
- `test_stamina_refill_restores_full_and_clears_exhausted`
- `test_stamina_non_finite_consume_is_noop`
- `test_stamina_non_finite_and_negative_dt_is_ignored`
- `test_stamina_construct_clamps_non_finite_config`

全部用固定 `dt`（如 1/60）循环推进，不读真实时间。

### 9.2 EditMode：`SkillCoordinatorStaminaTests`

- `test_skill_charged_drains_only_during_charging`
- `test_skill_charged_at_zero_stamina_auto_releases_and_reports_cast`
- `test_skill_spin_at_zero_stamina_interrupts_despite_super_armor`
- `test_skill_channeled_at_zero_stamina_interrupts`
- `test_skill_combo_zero_drain_activates_while_exhausted`
- `test_skill_sustained_with_drain_cannot_activate_while_exhausted`
- `test_skill_casting_does_not_drain`
- `test_skill_execution_recovery_skill_drain_is_zero`（技能自身不扣；冲刺扣精由 `Sys3CEntry` 另行裁决）

复用现有 `FakeTimeMachine` / `TestHelpers.FakeTarget` 范式。

### 9.3 EditMode：打断与资产

- `test_skill_spin_stamina_exhausted_is_interruptible`（矩阵）
- `test_skill_state_machine_stamina_exhausted_interrupts`（状态机默认分支回归）
- `test_skill_assets_stamina_drain_values`（20/15/15/0，防资产序列化静默失败）

### 9.4 PlayMode：`StaminaRingTests`

- `test_stamina_ring_fill_matches_ratio`
- `test_stamina_ring_hidden_at_full_visible_when_not_full`
- `test_stamina_ring_position_is_right_of_head_and_scales_with_canvas`
- `test_stamina_ring_images_disable_raycast`
- `test_stamina_ring_exhaust_flash_triggers_once`
- `test_stamina_ring_rebind_resets_visual_state`
- `test_stamina_ring_unbind_ignores_other_owner`
- `test_stamina_ring_hides_when_anchor_offscreen`

> 环测试创建 Canvas 后先 `yield return null`，等 CanvasScaler 算出 `scaleFactor` 再断言位置；alpha/红闪等私有状态用现有 `TestHelpers.GetField` 读取，不额外扩大生产 API。

### 9.5 验收数字（人工 + MCP 截图）

| 场景 | 期望 |
|---|---|
| 满精力地面冲刺 | 5.0s ± 1 帧抽空；强制走路；环闪红 |
| 停止消耗后 | 1.35s 后回到 20%，可再次冲刺 |
| 满精力举盾 | 10.0s 抽空并强制收盾；松开再按才能重举 |
| 满精力旋转 | 3s 共扣约 60（±1 帧耗率），结束剩约 40 |
| 满精力蓄力 | 2s 共扣约 30（±1 帧耗率） |
| 先冲刺 3s 再旋转 | 旋转约 2s 时力竭并中断（±1 帧） |
| 允许移动的非持续技能（蓄力/瞬发）Execution/Recovery 期间按住 Shift 移动 | 照常扣奔跑 20/s（验证"实际冲刺付费"） |
| 技能锁移动阶段按住 Shift | **不扣**奔跑（验证 `LockMovement` 守卫，BaseState 可能停在 Sprint） |
| 环收缩方向 | 12 点起逆时针；若反了改 `fillClockwise` |
| 角色出屏 / 相机后方 | 环隐藏（不在屏幕边缘停留） |
| 满精力 | 环不可见 |
| 死亡 | 环隐藏；复活满精力、不可见 |
| 手机竖屏 | 环清晰、不挡摇杆/按钮；默认 64 参考像素约 25 物理像素，偏小则调序列化外径 |

---

## 10. 文件清单

### 新增

- `Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaSystem.cs`（含 `StaminaConfig`）
- `Assets/Scripts/Hotfix/GameSystems/Stamina/StaminaRingRenderer.cs`
- `Assets/Scripts/Tests/Editor/StaminaSystemTests.cs`
- `Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs`
- `Assets/Scripts/Tests/Editor/StaminaSkillAssetTests.cs`
- `Assets/Scripts/Tests/PlayMode/StaminaRingTests.cs`

### 修改

- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs` — `SprintAllowed`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` — 创建/驱动/优先级/守卫/复活/绑定
- `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillData.cs` — 每秒消耗字段
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs` — 注入、门禁、持续扣精、耗尽处理
- `Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs` — `StaminaExhausted`
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs` — 打断放行
- `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs` — 打断放行 + 优先级
- `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs` — 环绑定/Tick/清理
- `Assets/PreRes/SkillsCfg/Spin_SkillR.asset` — 20
- `Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset` — 15
- `Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset` — 15

**不改**：协议、服务端、场景/预制体（默认值即可）。若在 Inspector 调环尺寸/偏移或精力数值，需要保存对应场景/预制体——这是可选的调参动作，不是实现前置。

---

## 11. 已知限制与风险

### 限制（接受）

1. **纯客户端权威**：修改过的客户端可绕过精力限制。
2. 远程玩家/怪物没有精力环，也没有精力同步。
3. 手机端没有冲刺按钮，精力主要作用于举盾与技能。
4. 空中保持 Shift 不扣奔跑精力（`IsGrounded` 为 false；包含跳跃与走下平台）。
5. 持续阶段 `rate=0` 会压制奔跑扣精但不扣精（技能动作优先）。
6. 已扣精力/已起冷却不退还。
7. `StaminaCost`（瞬发一次性消耗）本次不接线。
8. 20% 解锁阈值不在 UI 上标记。
9. 环在锚点出屏/相机后方时隐藏，没有屏外指示。
10. 环为屏幕空间投影，不随相机距离缩放。
11. **在线旋转的既有语义**：服务端把 20002 旋转按**单次圆形判定**结算（`CombatRules` 表值 160 伤害、hitDelay 0）。客户端力竭中断只会停止本地表现/后续 tick，**撤不回服务端已裁决的伤害**；协议没有取消消息，本设计不改这一点。
12. **在线引导技能**：`CombatRules` 明说"引导仍不进表，服务器拒收为未知技能"。即使未来加了引导资产，联网下也不会产生服务端伤害；本设计只保证本地精力语义。
13. **强制收盾的同步窗口**：服务端 `IsDefending` 由下一次位置同步刷新，因此力竭收盾后最多有一个同步间隔（约 1 tick）仍被服务端视为举盾中。
14. 环默认 64 参考像素，手机竖屏约 25 物理像素；已提供序列化外径/偏移供真机调参。
15. **低精力起手**：精力只剩极少量时仍可起手蓄力/旋转，下一帧就会力竭并按当前进度释放/中断（塞尔达同款行为）；不额外加"最低起手消耗"规则。
16. **与盾耐久解耦**：持盾同时消耗精力与盾耐久；精力不替代耐久，低精力不会让盾更容易破，反之亦然。

### 风险与缓解

| 风险 | 缓解 |
|---|---|
| 力竭打断静默失败（矩阵/状态机任一漏改） | 两处放行 + 两条 EditMode 回归测试 + `ForceComplete` 保险丝 |
| 技能完成回调导致 `_currentSkill` 重入 | 扣精前抓局部引用、处理完立即 return |
| 非有限数值污染精力 | `TryConsume`/`Tick`/构造三处防护 + EditMode 断言 |
| 资产字段序列化静默失败 | 资产值 EditMode 断言 |
| 环拦截触屏/鼠标 | `raycastTarget=false` + PlayMode 断言 |
| 角色重选误清新环 | `Unbind(Transform)` 按引用匹配 + PlayMode 断言 |
| 角色重选继承旧环视觉状态 | `Bind` 复位 alpha/红闪/fill + PlayMode 断言 |
| `EntityDisplayManager` 异步初始化导致漏绑 | `_staminaBound` 一次性懒绑定 |
| 双扣 | 单帧单一消耗优先级：持续技能 > 举盾 > 奔跑；非持续技能期间只按 `BaseState.Sprint` 扣奔跑 |
| UI 在不同分辨率下偏移失真 | 偏移乘 `canvas.scaleFactor`；PlayMode 位置断言 |

---

## 12. 实施顺序

1. `StaminaSystem` + `StaminaSystemTests`（纯逻辑先行）。
2. `SkillData` 字段 + `SkillCoordinator` 门禁/扣精 + 打断两处放行 + 相关 EditMode 测试。
3. `InputManager` / `Sys3CEntry` 接线（奔跑、举盾、优先级、复活、生命周期）。
4. `StaminaRingRenderer` + `EntityDisplayManager` 绑定 + PlayMode 测试。
5. 三个技能资产填值 + 资产测试。
6. 运行时验收：数值、截图、手机竖屏。
