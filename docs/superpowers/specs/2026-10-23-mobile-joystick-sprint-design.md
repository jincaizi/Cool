# 移动端摇杆冲刺设计（满推自动）

**Date:** 2026-10-23
**Engine:** Unity 2022.3.25f1
**Layer:** `Hotfix/Sys3C`、`Hotfix/UI`
**状态:** 设计已确认，待实现

---

## 0. 结论摘要

手机端**不新增按钮**：摇杆推满（模长 ≥ 0.9）并保持 0.2s 自动进入冲刺；回落到 ≤ 0.8 或松手退出。冲刺期间固定 8 m/s，忽略推杆深度；精力扣减与力竭门禁沿用现有系统，并新增“力竭后必须释放一次”的锁存，键盘与摇杆一致。

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| 入口形态 | 摇杆满推 + 保持 0.2s | 用户选定；不加按钮，保持右下三键布局 |
| 阈值 / 迟滞 | 进入 0.9，退出 0.8 | 0.2s 过滤误触；0.1 迟滞防止阈值附近抖动 |
| 冲刺速度 | 固定 8 m/s，忽略推杆深度 | 否则半推冲刺 4 m/s 比满推走路 5 m/s 还慢 |
| 力竭锁存 | 键盘 + 摇杆都生效 | 防止精力回到 20% 自动再冲刺，形成“跑两步停一下”的抖动 |
| 视觉反馈 | 冲刺时手柄 / 外环变色（可选引用） | 手势入口需要状态反馈；不配 Graphic 也能工作 |
| 权威归属 | 纯客户端 | 不改协议 / 服务端，与精力系统一致 |

---

## 1. 现状与证据

| # | 事实 | 出处 |
|---|---|---|
| 1 | 冲刺目前只有键盘 LeftShift 入口 | `InputManager.cs:189-192` |
| 2 | `SetSprintHeld(bool)` 已存在，但手机端无调用者 | `InputManager.cs:25-29` |
| 3 | 摇杆是模拟量，`Value.magnitude` 为 0–1 | `VirtualJoystick.cs:17-34` |
| 4 | 移动速度 = `MoveDir × (IsSprint ? 8 : 5)`，而 `MoveDir` 保留摇杆模长 | `CharacterController.cs:260-271` |
| 5 | 精力在地面 Sprint 状态按 20/s 扣；力竭后 `SprintAllowed=false` | `Sys3CEntry.cs:244,463-471` |
| 6 | 手机 UI 只有摇杆 + 观察区 + 3 个按钮；有测试断言恰好 3 个 | `MobileTouchInputTests.cs:234-253` |
| 7 | `MobileInputBinder.LateUpdate` 当前在 `_buttons == null` 时提前返回 | `MobileInputBinder.cs:125-133` |
| 8 | 键盘输入已归一化，摇杆输入没有 | `KeyboardInputAdapter.cs:29-40`、`JoystickInputAdapter.cs:38-44` |

---

## 2. 目标与非目标

### 目标

1. 手机端只用摇杆就能进入 / 退出冲刺。
2. 不新增按钮，三键布局与既有“恰好 3 个 `*Button`”测试不变。
3. 冲刺速度可预测（固定 8 m/s），精力消耗可控。
4. 力竭后不出现自动反复冲刺的抖动。
5. 键盘 Shift 语义保持可用，行为与摇杆一致。

### 非目标

- 冲刺按钮、双击 / 长按手势、无输入自动奔跑。
- 冲刺特效、音效、新动画。
- 服务端精力校验、网络同步。
- 改变冲刺与防御 / 技能 / 空中状态的既有优先级。

---

## 3. 交互模型

### 3.1 状态机

| 状态 | 条件 | 结果 |
|---|---|---|
| 走路 | 模长 0–0.9 | 现有模拟量速度 0–4.5 m/s |
| 冲刺确认中 | 模长 ≥ 0.9，计时 < 0.2s | 仍按走路速度；计时器累加 |
| 冲刺 | 计时 ≥ 0.2s | 固定 8 m/s |
| 退出 | 模长 ≤ 0.8 或松手 / UI 隐藏 | 回走路 |

- 计时中模长跌破 0.9 → 计时器归零（必须持续推满 0.2s）。
- 冲刺中模长在 (0.8, 0.9) 之间 → 保持冲刺（迟滞）。
- 方向仍由摇杆决定；冲刺中推杆深度不再影响速度。

### 3.2 视觉

- `VirtualJoystick` 新增可选的 `Graphic _sprintIndicator`（手柄或外环的 Image）。
- 冲刺时颜色变为 `_sprintColor`（默认橙色），退出恢复 `_idleColor`（`Awake` 时记录）。
- 引用为空 → 纯逻辑，无视觉，不影响测试。

### 3.3 参数

| 字段 | 默认 | 说明 |
|---|---|---|
| `_sprintEnterThreshold` | 0.9 | 进入阈值 |
| `_sprintExitThreshold` | 0.8 | 退出阈值 |
| `_sprintHoldSeconds` | 0.2 | 保持时间（秒） |
| `_sprintIndicator` | null | 可选变色目标（`UnityEngine.UI.Graphic`） |
| `_sprintColor` | (1, 0.6, 0.1, 1) | 冲刺颜色 |

---

## 4. 核心模型

`SprintGestureState`（纯 C#，无 MonoBehaviour）：

```csharp
public sealed class SprintGestureState
{
    public SprintGestureState(float enter = 0.9f, float exit = 0.8f, float hold = 0.2f);
    public bool IsSprinting { get; }
    public bool Tick(float magnitude, float deltaTime);
    public void Reset();
}
```

- 非有限 `magnitude` / `deltaTime` 按 0 处理（与 `StaminaSystem` 的守卫同风格）。
- 构造时钳制：`enter ∈ (0,1]`、`exit ∈ [0, enter)`、`hold ≥ 0`。
- `Tick` 返回 `IsSprinting`，便于调用方直接使用。
- 状态只用 `_held` + `_timer` 两个字段；无 Unity 依赖。

---

## 5. 集成点

### 5.1 VirtualJoystick

- `Awake` 创建 `SprintGestureState`，记录 `_idleColor`。
- `Update`：`_state.Tick(_value.magnitude, Time.deltaTime)` + 按结果变色。
  - **必须每帧 Tick，不能只在 `OnDrag` 里计时**：手指按住不动时不会再有拖拽事件，只靠事件会永远进不了冲刺。
- 暴露 `public bool IsSprintHeld => _state.IsSprinting;`。
- `OnPointerUp` / `OnDisable`：`_state.Reset()`，恢复颜色。
- **不改** `Value` 的模拟量语义；`OnDrag` 只更新 `_value`。

### 5.2 MobileInputBinder

- `LateUpdate` 改为先喂冲刺、再处理按钮：

```csharp
if (_inputManager == null) return;
_inputManager.SetSprintHeld(_joystick != null && _joystick.IsSprintHeld);
if (_buttons == null) return;
// 既有攻击 / 防御 / 跳跃
```

- `!_touch` 切回键鼠分支：先 `SetSprintHeld(false)`，再 `SetAdapter(new KeyboardInputAdapter())`。
- `OnDisable`：`_inputManager?.SetSprintHeld(false)`（与 `MobileActionButtons.OnDisable` 同思路）。

### 5.3 InputManager

- `SetSprintHeld` 注释更新：由摇杆手势 / UI 调用（不再是“未来挂钩”）。
- 新增私有字段 `bool _sprintBlockedUntilRelease;`（力竭锁存，默认 false）。
- `SprintAllowed` 仍在 `GetMoveCommand` 之前由 `Sys3CEntry.Update` 写入（现有顺序，不改）。
- `GetMoveCommand`：

```csharp
Vector3 moveInput = _adapter.GetMoveInput();
bool rawSprintHeld = IsSprintHeld();

if (!SprintAllowed && rawSprintHeld) _sprintBlockedUntilRelease = true;
else if (!rawSprintHeld) _sprintBlockedUntilRelease = false;

bool sprintHeld = rawSprintHeld && SprintAllowed && !_sprintBlockedUntilRelease;
float speed = sprintHeld ? SprintSpeed : MoveSpeed;

if (moveInput.sqrMagnitude > 0.01f)
{
    Vector3 worldMoveDir = ConvertToWorldDirection(moveInput, cameraForward);
    if (sprintHeld) worldMoveDir = worldMoveDir.normalized;
    // ...
}
```

- 冲刺命令的 `MoveDir.magnitude == 1`；`Speed == SprintSpeed`（字段保留，`CharacterController` 仍自行取速）。
- 锁存清空条件：原始输入释放（`rawSprintHeld == false`）。摇杆回落到 0.8 以下或松手都会让 `VirtualJoystick.IsSprintHeld` 变 false，因此“回杆一次”即可解除。

### 5.4 Sys3CEntry

- 不改。`SprintAllowed = _stamina.CanStartConsuming` 与地面 Sprint 扣精逻辑原样复用。

---

## 6. 速度与精力语义

### 6.1 速度

- 冲刺：固定 8 m/s；防御 `×0.4`、技能 `MoveSpeedScale` 仍照常叠加（保持既有优先级）。
- 走路：0–5 m/s 随推杆深度线性变化。
- 键盘：WASD 已归一化，行为不变。

### 6.2 精力

| 场景 | 扣精 | 说明 |
|---|---|---|
| 地面冲刺（含摇杆自动进入） | 20/s | 现有条件不变 |
| 站立 / 走路 | 0 | 回复 |
| 空中、锁移动、受击 | 0 | 现有守卫不变 |
| 力竭后仍推满 | 0 | `SprintAllowed=false`，且锁存阻止恢复后自动再冲刺 |

- 力竭锁存是**行为变更**：键盘按住 Shift 力竭后必须松开再按；摇杆必须回落到 0.8 以下或松手。
- 更新 `docs/superpowers/specs/2026-10-18-stamina-system-design.md` 的“精力归零……自动回到走路”描述，加 2026-10-23 修订行。

---

## 7. 边界与错误处理

| 场景 | 行为 |
|---|---|
| 触控 UI 隐藏 / `?touch=0` | binder 切键鼠前清 `SetSprintHeld(false)`；摇杆 `OnDisable` 重置 |
| 浏览器失焦、抬指事件丢失 | 依赖现有 `VirtualJoystick.OnDisable` 兜底（与移动值同一路径）；binder 不再喂 true |
| 玩家重建（确认角色） | 新 `InputManager` 由 binder 重新绑定；旧状态随旧对象销毁 |
| 摇杆空闲、键鼠兜底 | 移动回退到 WASD；Shift 仍可冲刺（`IsSprintHeld` 直接读键） |
| 快速甩杆 / 微调方向 | 0.2s 保持门槛挡住，不进入冲刺 |
| 推满但顶墙 | 仍会扣精力（既有 Shift 行为，不在本次修改） |
| 力竭后一直推满 | 锁存保持，直到回杆 / 松手；不会自动恢复冲刺 |
| 非有限 dt / 模长 | 状态机按 0 处理，不抛异常 |

---

## 8. 测试与验收

### 8.1 EditMode：`SprintGestureStateTests`

- `test_sprint_gesture_below_enter_threshold_stays_walking`
- `test_sprint_gesture_requires_hold_time`
- `test_sprint_gesture_enters_after_hold_and_keeps_through_hysteresis`
- `test_sprint_gesture_exits_at_exit_threshold`
- `test_sprint_gesture_below_enter_resets_hold_timer`
- `test_sprint_gesture_reset_clears_state`
- `test_sprint_gesture_ignores_non_finite_inputs`

### 8.2 EditMode：`InputManager`

- 既有 `test_input_sprint_gate_respects_allowed` 保持通过。
- `test_sprint_gate_requires_release_after_exhaustion`：按住 → `SprintAllowed=false` → 恢复 true（仍按住）→ 不冲刺 → 释放再按 → 冲刺。
- `test_sprint_command_normalizes_move_direction`：推 0.5 + `SetSprintHeld(true)` → `MoveDir.magnitude == 1`。

### 8.3 PlayMode：`MobileTouchInputTests`

新增独立用例 `test_joystick_full_push_sprints_after_hold_and_release_exits`（各自 `LoadScene` + 清理）：

1. 载入 DemoDay，`ForceTouch`，关闭选人面板，等待玩家与 Joystick 适配器。
2. `pointerDown` + `drag` 到一个远超半径的屏幕点（保证 `Value` 钳到 1.0，避免依赖 Canvas `scaleFactor`）。
3. 等待 0.2s + 1 帧；断言本地 `InputManager.GetMoveCommand(Vector3.forward).IsSprint == true`。
4. `pointerUp`；等待 1 帧；断言 `IsSprint == false`。
5. 收尾同现有用例（清档位、换空场景、卸载 DemoDay）。

- 迟滞区间（0.85 保持 / 0.79 退出）由 8.1 的纯逻辑用例覆盖，PlayMode 不硬编码屏幕像素。

### 8.4 手工验收（真机）

- 推满保持约 0.2s → 明显加速，摇杆变色，精力环下降。
- 推回一半 → 回到走路，精力停止下降。
- 力竭 → 自动回走路；保持推满不会自动再冲刺；回杆再推 → 恢复冲刺。
- 快速甩杆到满 → 不应误触发冲刺。
- 切到键鼠（`?touch=0`）→ Shift 冲刺仍正常。

### 8.5 不新增的断言

- 三按钮数量、布局不变（无新按钮）。
- 不新增冲刺按钮的射线 / 几何断言。

---

## 9. 需要同步更新的文档

| 文档 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md:138` | 输入表加“手机：摇杆满推保持 0.2s” |
| `docs/superpowers/specs/2026-10-18-stamina-system-design.md` | 顶部加 2026-10-23 修订：力竭锁存；§5.1 的“自动回到走路”改为“需释放一次” |
| `docs/superpowers/guides/webgl-mobile-acceptance.md` §3.3 | “满推=快走”改为“满推保持 0.2s=冲刺”；加冲刺 / 力竭检查项 |
| `InputManager.SetSprintHeld` 注释 | 从“测试 / 未来 UI 挂钩”改为“摇杆手势 / UI 入口” |

---

## 10. 风险与已知限制

| 风险 | 评估 |
|---|---|
| 习惯性推满导致持续耗精力 | 0.2s 保持 + 5 秒精力预算让玩家很快感知；视觉变色提示；参数可调 |
| 阈值手感在真机上偏难 / 偏易 | 三参数在 Inspector，真机验收后调；不改结构 |
| 冲刺时推杆深度失效，玩家觉得“不跟手” | 刻意设计：冲刺是二元状态；方向仍跟手 |
| 力竭锁存让键盘玩家觉得多此一举 | 与举盾锁存一致；避免“跑两步停一下”的抖动 |
| 0.8–0.9 迟滞区内速度固定 8 m/s | 由 0.2s 进入门槛保证是刻意动作；退出落差明显 |
| 冲刺顶墙仍扣精力 | 既有行为，不在本次范围；如要修需独立判定“实际位移” |

---

## 11. 明确不做

- 冲刺按钮 / 双击 / 长按手势
- 无输入自动奔跑
- 冲刺专属特效、音效、动画
- 服务端精力校验与网络同步
- 翻滚 / 闪避 / 攀爬 / 游泳
