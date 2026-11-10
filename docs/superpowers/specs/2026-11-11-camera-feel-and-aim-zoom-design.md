# 相机手感与火球瞄准视角设计

- 日期：2026-11-11
- 状态：待实现
- 模块：Sys3C（相机 / 输入）、UI（移动观察区）、Skills（火球蓄力）
- 相关：[3C README](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md)、
  [UI README](../../../Assets/Scripts/Hotfix/GameSystems/UI/README.md)、
  [火球地面瞄准设计](2026-11-07-fireball-ground-aim-design.md)

## 1. 背景

移动端实际体验暴露三个相机问题：

1. 触屏拖拽转视角横向太快，容易“一晃就过头”；
2. 按住移动摇杆时，另一只手点按钮或空白处，镜头在落指瞬间发生一次明显的大角度跳变；
3. 火球蓄力瞄准时视野太近，不方便选择 15m 内的落点。

本规格覆盖三者的修复与验收。三块改动互相独立，但共享同一条真机验收路径。

## 2. 目标与成功判据

| # | 判据 | 验证方式 |
|---|---|---|
| G1 | 触屏横向转 180° 约需 720px 滑动（当前约 450px） | 真机手感 + 数值断言 `0.25°/px` |
| G2 | 按住摇杆时，另一只手点按钮/空白，相机 yaw 不变 | 真机复现；开发日志确认合成鼠标输入未进入相机 |
| G3 | 火球瞄准手势激活时，相机距离 8 → 11.5，约 0.2s 完成主要行程；松手、取消、受击/精力耗尽打断、失焦/横屏、死亡后均回到 8 | PlayMode 距离收敛用例 + 真机 |
| G4 | 死亡时正在蓄力的火球被取消，不发射、不进冷却（现有伤害路径已通过 `InterruptCurrentSkill(Stun)` 保证；死亡分支再显式兜底） | PlayMode 回归用例 |

## 3. 非目标

- 不做按落点距离动态拉远，不改俯仰角，不改 FOV。
- 不按平台写死拉远：只有火球瞄准手势触发拉远，键盘 1 键蓄力不拉远。
- 不引入相机加速度、平滑或新的限速层；问题 1 只降触屏灵敏度。
- 不改防穿墙逻辑、`NpcBody` 层规则、远端实体分档。
- 不改其他技能的视角行为。

## 4. 设计

### 4.1 触屏观察灵敏度

**数值**：`TouchLookArea._sensitivity` 从 `0.2` 降到 `0.125`。相机侧 `MouseSensitivityX = 2.0`
不变，实际灵敏度为 `0.125 × 2.0 = 0.25°/px`（原 `0.4°/px`）。

**三处同步**（漏一处则测试通过但真机不生效）：

1. `TouchLookArea` 字段默认值——新增 `internal const float DefaultSensitivity = 0.125f`，
   字段用该常量初始化；
2. `DemoDay.unity` 中 LookArea 的序列化值 `_sensitivity: 0.125`；
3. 现有测试中硬编码的 `0.2` 改为引用 `DefaultSensitivity` 或按新值计算。

**测试锁**：`TouchLookArea` 增加 `internal float Sensitivity => _sensitivity;`；
`MobileLookAreaLayoutTests` 增加一条场景断言，读取场景里 LookArea 的实际值并等于
`DefaultSensitivity`，防止只改代码默认值。

**已知边界**：

- 横纵共用同一系数，纵向也从 `0.4°/px` 降到 `0.25°/px`。移动端俯仰范围只有 `[-10, 25]`，
  可接受；若未来需要横慢纵快，再拆轴向系数。
- 灵敏度按屏幕像素计，不按 DPI 归一；死区已按 DPI 归一，二者不一致。真机定档以目标手机为准，
  不拿编辑器窗口像素推算。
- 实际触屏灵敏度 = `TouchLookArea._sensitivity × ThirdPersonCameraController.MouseSensitivityX`。
  调 PC 鼠标灵敏度会连带改变触屏手感；本次接受并在模块 README 记录，不新增第二个缩放字段。

### 4.2 第二根手指点按导致的视角跳变

**根因假设（需真机诊断确认）**：手机浏览器会把触摸合成为鼠标输入（项目已确认过同类问题：
普攻曾因 `GetMouseButton(0)` 被合成左键误触发）。摇杆按住时观察区 `Delta == 0`，
`JoystickInputAdapter` 每帧回退到 `KeyboardInputAdapter` 的鼠标视角；第二根手指落下时，
触摸活跃使 `GetMouseButton(1)` 门控短暂成立，`Mouse X`（摇杆手指的移动，`2°/px`）被当成视角输入，
产生一次大角度跳变。该假设能同时解释“落指瞬间、只在手机、幅度很大、点按钮或空白都一样”。

**强制规则**：**只要有触摸按下，鼠标视角兜底必须为零。** 这是与“删除鼠标左键攻击来源”
同一原则：触摸设备上合成的鼠标事件不得驱动游戏相机。

**实现**：`KeyboardInputAdapter.GateLookToRightDrag` 增加 `touchActive` 形参：

```csharp
public Vector2 GetCameraRotationInput()
{
    float mouseX = UnityEngine.Input.GetAxisRaw("Mouse X");
    float mouseY = UnityEngine.Input.GetAxisRaw("Mouse Y");
    return GateLookToRightDrag(new Vector2(mouseX, mouseY),
        UnityEngine.Input.GetMouseButton(1),
        UnityEngine.Input.touchCount > 0);
}

internal static Vector2 GateLookToRightDrag(Vector2 look, bool rightButtonHeld, bool touchActive)
    => rightButtonHeld && !touchActive ? look : Vector2.zero;
```

**行为边界**：

- `touchCount == 0` 时 PC/编辑器右键拖拽行为完全不变。
- 摇杆按住期间（`touchCount >= 1`）鼠标兜底关闭；观察区自己的拖拽量不经过该门控，仍可转视角。
- 观察区手指按下但未过死区时 `Delta == 0`，兜底同样被关闭；行为无变化。
- 触屏关闭（`?touch=0` / `ForceNoTouch`）的手机上没有鼠标，门控无副作用。
- 抬指后如果浏览器补发合成 mousemove，且右键状态晚一帧释放，仍可能漏一帧。当前先不处理；
  若真机复验在抬指时仍有跳变，再给门控加 0.1s“触摸刚结束”宽限窗。

**诊断检查点（实现计划的第一步）**：在 `KeyboardInputAdapter.GetCameraRotationInput` 中、
**门控之前**加临时开发日志（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）。仅在
`touchCount > 0` 且（右键为 true 或任一鼠标轴非零）时打印，避免刷屏；记录：

```text
touchCount / GetMouseButton(1) / GetAxisRaw("Mouse X") / GetAxisRaw("Mouse Y")
```

真机复现一次后按日志判定：

| 日志表现 | 结论 | 动作 |
|---|---|---|
| 落指帧 `touchCount >= 2`、`rmb=True`、`Mouse X` 很大 | 合成鼠标输入进入兜底 | 保留门控，删除日志 |
| 落指帧 `touchCount >= 2`、`rmb=False`、`Mouse X` 为 0，但视角仍跳 | 病因不在鼠标兜底 | 回到本规格修订，检查观察区首帧位移 |
| 观察区首帧 `total` 异常大 | WebGL 指针位置/归属异常 | 在 `TouchLookArea` 增加“首帧异常大位移 rebase”保护并补测试 |
| 无任何异常日志但仍复现 | 未定位 | 继续诊断，不把门控当作已修复 |

**测试**：`KeyboardInputAdapterTests` 更新现有两条门控用例，并新增：

- `test_keyboard_look_is_zero_while_touch_active`：右键按住 + `touchActive == true` → 零；
- `test_keyboard_look_is_zero_without_right_button`：触摸未激活 + 未按右键 → 零（原用例保留）。

PlayMode 的 `FakeBaseInput` 无法控制 `UnityEngine.Input.touchCount` / `GetAxisRaw`，
因此真实输入路径只能靠上述真机诊断，回归测试覆盖纯函数规则。

### 4.3 火球瞄准拉远

**参数**：

| 参数 | 值 | 说明 |
|---|---|---|
| `Distance` | 8（场景现值） | 普通跟随距离，不变 |
| `AimDistance` | 11.5 | 移动端地面瞄准时的目标距离 |
| `ZoomDamping` | 8 | 与现有 `PositionDamping` 同形，约 0.2s 完成主要行程（≈80%） |

**控制器改动**（`ThirdPersonCameraController`）：

- 新增 `public bool AimZoomActive { get; set; }`；
- 新增内部当前距离 `_currentDistance`，首次使用时初始化为 `Distance`；
- 新增 `internal float TargetDistance => Mathf.Max(MinCameraDistance, AimZoomActive ? AimDistance : Distance)`；
- 新增 `internal void TickZoom(float deltaTime)`：
  `_currentDistance = Mathf.Lerp(_currentDistance, TargetDistance, Mathf.Clamp01(ZoomDamping * deltaTime))`；
- `LateUpdate` 先 `TickZoom(Time.deltaTime)`，球坐标 offset 和防穿墙起投都用 `_currentDistance`；
- `SnapToTarget`（出生/复活）把 `_currentDistance` 直接置为 `Distance`，避免瞬移后下一帧又拉出去；
- `Target == null` 时 `LateUpdate` 早退，不推进缩放；新角色 `SnapToTarget` 会重置距离。

**触发条件**（`Sys3CEntry.Update` 现有相机块内）：

```csharp
_camera.AimZoomActive = IsMobileAimActive() && !_isDead;
```

- `IsMobileAimActive()` = 当前处于 `Charging` + 技能是 `HoldToAim` + 瞄准手势激活；
- 拉远只跟随瞄准手势，不按平台写死：触屏或编辑器里按住火球按钮拖动都会拉远，
  键盘 1 键蓄力（没有瞄准手势）不拉远；
- 火球有约 0.1s `Casting`，进入 `Charging` 后才开始拉远，加缓动约 0.3s 后完成主要行程，可接受；
- 恢复时点为**松手发射的那一帧**（退出 `Charging`），`Execution` 期间完成回位；取消、打断同理。

**死亡边界**：

- 现有的致死伤害路径（`ApplyServerDamage` / `IDamageable.TakeDamage`）在 HP 归零时已经调用
  `InterruptCurrentSkill(Stun)`，`Projectile` 技能的打断矩阵也放行 `Stun`；`EvaluateLocalDeath`
  本身不取消技能。也就是说当前正常死亡**已经**会取消蓄力。
- 拉远条件仍必须带 `!_isDead`：快速死亡帧、服务端权威余血归零、以及未来新增的死亡路径都不应
  让镜头停留在瞄准距离。
- 同时在 `Sys3CEntry.HandleInput` 的死亡分支调用 `CancelAimingSkill()` 作为显式兜底：
  不发射、不进冷却，覆盖所有 `_isDead` 路径；复活后松手不会补发火球。该调用每帧执行，
  `CancelCharge` 在非 `Casting/Charging` 时返回 false，幂等。

**其他边界**：

- 防穿墙无需额外逻辑：它使用 `_currentDistance` 算出的 `targetPosition`，贴墙时只把相机拉近，
  不会改 `_currentDistance` 本身，离墙后恢复瞄准距离。
- `Time.timeScale == 0`（顿帧）时 `deltaTime == 0`，缩放缓动冻结；恢复后继续。
- 玩家被销毁/重建：相机 `Target` 为空时早退，新角色 `SnapToTarget` 重置距离并写入 false，
  不会残留拉远状态。
- `AimDistance` 低于 `MinCameraDistance` 时由 `TargetDistance` 钳住。

**测试接缝**：`TickZoom` 做成 internal，PlayMode/EditMode 用固定 `dt` 连续调用断言
`CurrentDistance`，不要断言相机世界坐标（防穿墙会把实际距离拉近，测试容易变脆）。

### 4.4 死亡取消蓄力

- 位置：`Sys3CEntry.HandleInput` 的 `if (_isDead)` 分支，返回前调用 `CancelAimingSkill()`。
- 语义：与 UI 取消相同——不发射、不进冷却、不上报 `CastRequest`。
- 目的：死亡时显式保证瞄准结束。现有致死伤害路径已通过 `InterruptCurrentSkill(Stun)` 取消技能，
  这条兜底覆盖快速死亡帧与未来新增的 `_isDead` 路径，并保证相机拉远条件必然转为 false。

## 5. 改动清单

### 生产代码

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs` | `DefaultSensitivity` 常量；默认值与 `Sensitivity` 只读属性 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs` | `GateLookToRightDrag` 增加 `touchActive`；临时诊断日志 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs` | `AimDistance` / `ZoomDamping` / `AimZoomActive` / `TickZoom` / 当前距离 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 写入 `AimZoomActive`；死亡分支取消蓄力 |

### 场景

| 文件 | 改动 |
|---|---|
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` | LookArea `_sensitivity: 0.125`；Main Camera `AimDistance: 11.5`、`ZoomDamping: 8` |

### 测试

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs` | 灵敏度期望改引用常量 |
| `Assets/Scripts/Tests/PlayMode/TouchLookAreaEventSystemTests.cs` | 同上 |
| `Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs` | 场景 LookArea 灵敏度断言 |
| `Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs` | 三条门控用例（含触屏活跃为零） |
| `Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs` | `TickZoom` 收敛、目标距离、`SnapToTarget` 重置 |
| `Assets/Scripts/Tests/PlayMode/AimZoomCameraTests.cs`（新建） | 相机 8 ↔ 11.5 收敛；死亡/取消路径回到 8 |

### 文档

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md` | 触屏灵敏度数值；鼠标视角兜底与触摸互斥；火球瞄准拉远与恢复时点；死亡取消蓄力 |
| `Assets/Scripts/Hotfix/GameSystems/UI/README.md` | 观察区灵敏度；触摸活跃时鼠标兜底关闭 |

## 6. 测试矩阵

### EditMode

- `TouchLookAreaTests`：死区、指针归属、鼠标隔离保持；期望值引用 `DefaultSensitivity`。
- `KeyboardInputAdapterTests`：
  - `test_keyboard_look_is_zero_without_right_button`
  - `test_keyboard_look_passes_through_with_right_button`
  - `test_keyboard_look_is_zero_while_touch_active`
- `ThirdPersonCameraControllerTests`：
  - `test_aim_zoom_eases_from_distance_to_aim_distance`
  - `test_aim_zoom_returns_to_distance_when_disabled`
  - `test_aim_zoom_target_is_clamped_to_min_camera_distance`
  - `test_snap_to_target_resets_aim_zoom_distance`

### PlayMode

- `AimZoomCameraTests`：真实生命周期下 `LateUpdate` 推进缩放；设置
  `AimZoomActive = true` 后若干帧 `CurrentDistance` 收敛到 11.5，置 false 后回到 8；
  `SnapToTarget` 立即重置为 8。
- `MobileLookAreaLayoutTests`：场景 LookArea 的 `Sensitivity == TouchLookArea.DefaultSensitivity`。
- 现有 `PlayerDeathRespawnTests`：确认死亡取消蓄力不破坏复活链（必要时补断言）。

### 真机验收（WebGL / 手机）

1. 观察区横向拖拽：180° 约半屏，不再一划过头；松手立即停。
2. 按住摇杆，另一手依次点普攻/技能按钮/右上空白：相机不跳；移动方向随视角正常变化。
3. 按住火球按钮拖动落点：相机平滑拉远到约 11.5，地面圈可辨；松手发射、拖到取消区松手、
   受击打断、切后台/横屏、死亡：都能平滑回到 8。
4. 触屏关闭（`?touch=0`）或 PC：右键拖拽视角不变；键盘 1 键蓄力不拉远
   （编辑器里按住火球按钮拖动仍会拉远，因为它走的是同一条瞄准手势）。

## 7. 边界与失败模式

| 场景 | 期望 |
|---|---|
| 摇杆按住 + 第二指落点 | 无 yaw 变化；观察区自己的拖拽仍生效 |
| 摇杆按住 + 观察区手指未过死区 | 鼠标兜底关闭，无视角变化 |
| 抬指后合成鼠标事件晚到 | 当前不处理；若复验出现，加 0.1s 宽限窗 |
| 火球按下但冷却/精力不足 | 不进入 `Charging`，不拉远 |
| 瞄准中受击打断/精力耗尽 | 技能取消 → 拉远条件 false → 平滑回 8 |
| 瞄准中死亡（服务端/本地） | 现有伤害打断 + 死亡分支兜底取消蓄力；不拉远，复活后不会补发 |
| 瞄准中失焦/横屏 | `MobileInputBinder` 取消瞄准 → 回 8 |
| 瞄准中贴墙 | 防穿墙把实际相机拉近，`_currentDistance` 不变；离墙回到 11.5 |
| 顿帧 `timeScale == 0` | 缩放冻结，恢复后继续 |
| 玩家销毁/重建 | `Target == null` 早退；新角色 `SnapToTarget` 重置为 8 |
| `AimDistance < MinCameraDistance` | 目标距离钳到 `MinCameraDistance` |
| PC/编辑器 | 右键拖拽不变；键盘 1 蓄力不拉远；按住火球按钮拖动会拉远（同一条瞄准手势） |

## 8. 风险与待验证

1. **问题 2 的根因未在真机证实**。门控是正确规则，但若日志显示病因不在鼠标兜底，
   必须按 §4.2 的判定表回到规格修订，不得把门控当作已修复。
2. **触屏手感是主观验收**。`0.125` 是计算档位，最终以目标手机试玩为准；若仍偏快，
   只调场景值并在 README 记录。
3. **死亡取消蓄力的兜底是行为保证**。现有致死伤害路径已经在打断矩阵里取消技能；
   死亡分支的显式取消是幂等兜底。若后续发现某些死亡演出依赖蓄力状态，以 `!_isDead`
   的相机回位为准，再单独评估取消时机。

## 9. 决策记录

| 决策 | 结果 | 来源 |
|---|---|---|
| 触屏灵敏度档位 | `0.25°/px`（`_sensitivity = 0.125`） | 用户选择 |
| 火球拉远方式 | 固定 `8 → 11.5`，约 0.2s | 用户选择 |
| 火球拉远触发源 | 只跟随瞄准手势（触屏或编辑器火球按钮拖动）；键盘 1 不拉远 | 设计约束 |
| 恢复时点 | 松手/取消那一帧开始恢复 | 用户选择 |
| 死亡处理 | 取消蓄力（伤害打断已有；死亡分支显式兜底）+ 相机回位 | 用户选择 |
| 问题 2 修法 | 触摸活跃时禁用鼠标视角兜底，先真机诊断 | 设计审核结论 |
