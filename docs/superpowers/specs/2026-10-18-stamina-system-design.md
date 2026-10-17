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
| 每帧消耗数 | **1 种**（持续技能 > 举盾 > 奔跑） | 避免双扣；技能与举盾本就互斥，只有奔跑会重叠 |
| 奔跑判定 | `_cc.Data.BaseState == BaseState.Sprint` | 真实移动状态；站着按 Shift、锁移动、眩晕、死亡都不空扣 |
| 空中冲刺 | **不扣** | `JumpStart/JumpAir` 时 BaseState 不是 Sprint；冲刺视为地面动作 |
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
4. 纯客户端，不新增协议字段、不改服务端、不改场景/预制体。
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
| `TryConsume(amount)` | `amount <= 0` 直接返回 true 且**不重置回复延迟**；`Current < amount` ⇒ `Current = 0`、`IsExhausted = true`、重置延迟、返回 **false**；否则扣减、重置延迟、返回 true |
| `Tick(dt)` | 累加"距上次消耗尝试"的时间；≥ `RegenDelay` 后按 `RegenPerSecond × dt` 回复；回复到阈值时清除 `IsExhausted` |
| `Refill()` | `Current = Max`、清 `IsExhausted`、重置延迟 |

**契约**
- 消耗失败会**把当前值清零**（UI 显示空环），并返回 false；调用方必须立即停止动作。
- 回复计时使用内部累加器，不读 `Time.time`，EditMode 测试可确定性模拟。
- 构造时钳制配置：`Max >= 1`、各速率 `>= 0`、`RegenDelay >= 0`、`ExhaustRecoverRatio ∈ [0,1]`。

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

1. **持续技能阶段**（`Charging / Channeling / Spinning`）→ 由 `SkillCoordinator` 在自身 `Update` 内扣技能耗率。
2. **任何其他活跃技能**（Casting/Execution/Recovery 等）→ 不扣举盾/奔跑。
3. **举盾中**（`_cc.IsDefending && !眩晕 && !死亡`）→ 扣举盾。
4. **实际地面冲刺**（`_cc.Data.BaseState == BaseState.Sprint && !举盾 && !眩晕 && !死亡`）→ 扣奔跑。
5. 都没有 → 只 `Tick` 回复。

若本帧技能扣精已把精力置为力竭，跳过 3/4 分支，只 `Tick`。最后**每帧只调用一次** `_stamina.Tick(dt)`。技能扣精保持在内聚位置（`SkillCoordinator`），优先级由 `Sys3CEntry` 统一裁决。

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
| 地面冲刺（BaseState=Sprint） | 20/s | 否 |
| 空中保持 Shift | 否 | 是 |
| 按住 Shift 但站着 | 否 | 是 |
| 举盾中（未眩晕） | 10/s | 否 |
| 举盾中眩晕 | 否 | 是 |
| 持续技能阶段 rate>0 | rate/s | 否 |
| 持续技能阶段 rate=0 | 否 | 是 |
| 技能 Casting / Execution / Recovery | 否 | 是 |
| 普攻 / 瞬发技能期间 | 否 | 是 |
| 锁移动 / 眩晕 / 受击 | 否 | 是 |
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
- `Sys3CEntry.Update` 在 `GetMoveCommand` 之前：`_inputManager.SprintAllowed = !_stamina.IsExhausted;`
- 扣精条件：`!_skillCoordinator.IsSkillActive && !_cc.IsDefending && !StatusControllerAdapter.IsStunned && !_isDead && _cc.Data.BaseState == BaseState.Sprint`。
- 扣精量：`SprintDrainPerSecond × dt`。
- 精力归零当帧仍可能以冲刺速度移动一帧；下一帧 `SprintAllowed=false`，自动回到走路。
- 力竭门禁只影响"是否进入冲刺"；举盾限速、技能移速倍率、空中控制等既有逻辑**不变**。

### 5.2 举盾

- `HandleInput` 进入防御前检查 `_stamina.CanStartConsuming` 与锁存：
  ```csharp
  if (_inputManager.IsDefendHeld())
  {
      if (!_defendStaminaLatch && _fsmManager.CanDefend && _cc.TryEnterDefend())
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
- 举盾期间每帧 `TryConsume(DefendDrainPerSecond × dt)`；失败 ⇒ `TryExitDefend()` + `ExitDefend()` + 置锁存。
- 眩晕/死亡期间不扣；盾破/背面受击的既有退出路径不变。
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
    if (!_stamina.TryConsume(rate * deltaTime))
        HandleStaminaDepleted(skill, sub);
}

private void HandleStaminaDepleted(SkillExecutor skill, SkillSubState sub)
{
    switch (sub)
    {
        case Charging:   TryReleaseCharge(skill.SkillId); break;
        case Channeling:
        case Spinning:   skill.TryInterrupt(InterruptionSource.StaminaExhausted); break;
    }
}
```

处理完立即返回；被中断后的清理/缓冲输入由现有事件链负责。

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
- 无 `EntityDisplayManager` 的场景（测试）只跑逻辑，不报错。

### 6.2 视觉规格（风格 A）

| 项 | 规格 |
|---|---|
| 外径 | 64 参考像素（CanvasScaler 缩放） |
| 环宽 | 约 10px（128×128 贴图，厚 20px） |
| 背景环 | 同形状白环染 `rgba(10,20,12,0.55)` |
| 填充环 | 绿色 `#5DFF6A` |
| 填充方式 | `Image.Type.Filled` + `Radial360`，`fillOrigin = Top`，`fillClockwise = false`（12 点起逆时针缩短） |
| 贴图 | 运行时生成 128×128 抗锯齿白环；一次创建，`Cleanup` 销毁 |
| 定位 | `owner.position + up × 1.7m` 投影到屏幕；相机后方（z≤0）隐藏 |
| 偏移 | `(64, 24) × canvas.scaleFactor`（保证不同分辨率下与环同比例） |
| 安全边距 | 环心钳制在屏幕内，边距 `(半径 + 4) × scaleFactor` |
| 显示 | 精力未满：0.1s 淡入；回满：0.25s 淡出后停用；淡出中重新消耗则反向淡入 |
| 力竭 | `IsExhausted` 上升沿整环闪红 0.35s，只触发一次 |
| 死亡 | 绑定传入 `() => !_isDead`；死亡期间整环隐藏 |
| 交互 | 两张 `Image.raycastTarget = false`；`CanvasGroup.blocksRaycasts = false` |
| 层级 | 显示时 `SetAsLastSibling()`，压在名牌/浮动文字之上 |

### 6.3 测试可见状态

`StaminaRingRenderer` 暴露只读 `FillAmount` 与 `IsVisible` 供测试断言，不额外增加运行时代价。

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

---

## 8. 生命周期

| 时机 | 行为 |
|---|---|
| `Sys3CEntry.Start` | **最先**创建 `StaminaSystem`（早于引用判空）；`_skillCoordinator` 创建后再注入 `SetStamina(...)`；随后绑定 UI |
| 死亡 | 不消耗；环隐藏；资源继续回复（不可见） |
| 单机复活 `TickRespawn` | `Refill()` + 清防御锁存 |
| 联网复活 `ApplyServerRespawn` | `Refill()` + 清防御锁存 |
| 角色重选 | 旧角色 `OnDestroy` 解绑（按引用匹配，不会误清新角色的环）；新角色 `Start` 重新绑定 |
| 场景切换 | `EntityDisplayManager` 常驻；环随解绑隐藏 |

---

## 9. 测试与验收

### 9.1 EditMode：`StaminaSystemTests`

- `test_stamina_sprint_rate_empties_in_five_seconds`
- `test_stamina_exhausted_blocks_start_until_twenty_percent`
- `test_stamina_regen_starts_after_delay`
- `test_stamina_consume_attempt_resets_regen_delay_even_when_insufficient`
- `test_stamina_try_consume_insufficient_clamps_to_zero_and_returns_false`
- `test_stamina_zero_consume_does_not_reset_regen_delay`
- `test_stamina_refill_restores_full_and_clears_exhausted`

全部用固定 `dt`（如 1/60）循环推进，不读真实时间。

### 9.2 EditMode：`SkillCoordinatorStaminaTests`

- `test_skill_charged_drains_only_during_charging`
- `test_skill_charged_at_zero_stamina_auto_releases_and_reports_cast`
- `test_skill_spin_at_zero_stamina_interrupts_despite_super_armor`
- `test_skill_channeled_at_zero_stamina_interrupts`
- `test_skill_combo_zero_drain_activates_while_exhausted`
- `test_skill_sustained_with_drain_cannot_activate_while_exhausted`
- `test_skill_casting_does_not_drain`
- `test_skill_execution_recovery_do_not_drain`

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

### 9.5 验收数字（人工 + MCP 截图）

| 场景 | 期望 |
|---|---|
| 满精力地面冲刺 | 5.0s ± 1 帧抽空；强制走路；环闪红 |
| 停止消耗后 | 1.35s 后回到 20%，可再次冲刺 |
| 满精力举盾 | 10.0s 抽空并强制收盾；松开再按才能重举 |
| 满精力旋转 | 3s 共扣 60，结束剩 40 |
| 满精力蓄力 | 2s 共扣 30 |
| 先冲刺 3s 再旋转 | 旋转约 2s 时力竭并中断 |
| 满精力 | 环不可见 |
| 死亡 | 环隐藏；复活满精力、不可见 |
| 手机竖屏 | 环清晰、不挡摇杆/按钮 |

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

**不改**：协议、服务端、场景、玩家预制体、HUD 预制体。

---

## 11. 已知限制与风险

### 限制（接受）

1. **纯客户端权威**：修改过的客户端可绕过精力限制。
2. 远程玩家/怪物没有精力环，也没有精力同步。
3. 手机端没有冲刺按钮，精力主要作用于举盾与技能。
4. 空中保持 Shift 不扣奔跑精力（`BaseState` 已离开 Sprint）。
5. 持续阶段 `rate=0` 会压制奔跑扣精但不扣精（技能动作优先）。
6. 已扣精力/已起冷却不退还。
7. `StaminaCost`（瞬发一次性消耗）本次不接线。
8. 20% 解锁阈值不在 UI 上标记。
9. 环在角色出屏/相机后方时隐藏，没有屏外指示。
10. 环为屏幕空间投影，不随相机距离缩放。

### 风险与缓解

| 风险 | 缓解 |
|---|---|
| 力竭打断静默失败（矩阵/状态机任一漏改） | 两处放行 + 两条 EditMode 回归测试 |
| 技能完成回调导致 `_currentSkill` 重入 | 扣精前抓局部引用、处理完立即 return |
| 资产字段序列化静默失败 | 资产值 EditMode 断言 |
| 环拦截触屏/鼠标 | `raycastTarget=false` + PlayMode 断言 |
| 角色重选误清新环 | `Unbind(Transform)` 按引用匹配 |
| 双扣 | 单帧单一消耗优先级；技能活跃时跳过举盾/奔跑 |
| UI 在不同分辨率下偏移失真 | 偏移乘 `canvas.scaleFactor`；PlayMode 位置断言 |

---

## 12. 实施顺序

1. `StaminaSystem` + `StaminaSystemTests`（纯逻辑先行）。
2. `SkillData` 字段 + `SkillCoordinator` 门禁/扣精 + 打断两处放行 + 相关 EditMode 测试。
3. `InputManager` / `Sys3CEntry` 接线（奔跑、举盾、优先级、复活、生命周期）。
4. `StaminaRingRenderer` + `EntityDisplayManager` 绑定 + PlayMode 测试。
5. 三个技能资产填值 + 资产测试。
6. 运行时验收：数值、截图、手机竖屏。
