# 移动端触摸输入与浏览器横屏修改方案

日期：2026-11-08  
状态：交互方向已确认，已按本文实现；真机验收待执行。

## 目标与已确认决策

1. **所有平台只有普攻按钮能够触发普攻**；鼠标左键点击场景、HUD、弹窗、摇杆和视角区均不得挥刀。
2. 鼠标左键不再作为攻击输入；拖动视角、操作摇杆也不会触发攻击。
3. 手机浏览器竖屏时显示“请横置手机”遮罩，阻断游戏操作；横置后恢复。
4. 不通过旋转整个网页 90° 来模拟横屏。自动横屏仅作为可选增强，不能作为正常使用的前提。

## 当前依据与排查方向

- UI 模块已有 VirtualJoystick、TouchLookArea、MobileActionButtons；`MobileInputBinder` 在
  `Sys3C/Input/` 下（不在 UI 模块）。
- 移动技能输入的既有通路是 MobileActionButtons → MobileInputBinder → InputManager → Sys3CEntry，继续复用。
- 既有规则要求失焦、关闭触控、解绑角色时清理 held 状态，且不得把取消合成为松手施放。
- **点击误攻击根因已确认**：`Sys3C/Input/InputManager.cs` 的 `Update` 把
  `Input.GetMouseButton(0)` 与 UI 攻击按钮直接合并成同一条按下沿。手机浏览器会把触摸合成为
  鼠标左键，因此任意触摸（空白、HUD、摇杆、视角区）都会触发普攻；键鼠模式点击 UI 也走同一分支。
- **产品决定（2026-11-08）**：不再做"触摸/鼠标分流"，而是**所有平台直接取消鼠标左键攻击**，
  只保留普攻按钮通路。因此不再需要触摸指针追踪、UI 射线命中判定或指针归属锁存。
- **发布入口已定位**：构建入口为 `Assets/Scripts/Editor/WebGLBuildScript.cs`，托管入口为
  `production/deploy/webgl-demo/`；`Build/WebGL` 与 `dist/` 均被 gitignore，当前没有受版本管理的
  `Assets/WebGLTemplates`。实现新增 `Assets/WebGLTemplates/MobileLandscape/`，并把项目模板设到它。

## 一、攻击输入来源

### 输入规则

| 场景 | 预期行为 |
| --- | --- |
| 所有平台：点击场景空白区域 | 不攻击 |
| 所有平台：点击/拖动 HUD、弹窗、角色选择、摇杆、视角区 | 仅执行该 UI 的行为，不攻击 |
| 所有平台：操作普攻按钮 | 点按 = 轻击；按住 + 松开 = 重击（保持既有语义） |
| 所有平台：操作技能按钮 | 仅产生对应技能输入 |
| 手机触摸模式：左手摇杆 + 右手普攻/技能 | 多指同时工作 |

### 修改方式

- **删除鼠标左键攻击来源**：`InputManager.Update` 不再读取 `GetMouseButton(0)`，只消费
  `MobileActionButtons.AttackHeld → MobileInputBinder → SetAttackHeld` 的按钮通路（所有平台一致）。
- 鼠标点击因此不会穿透为攻击；不再需要 UI 射线命中判定、指针归属锁存或触摸/鼠标分流。
- 移动普攻只消费既有按钮输入，不在 UI 中直接调用技能协调器。
- 保留多指操作：左手摇杆与右手普攻可同时工作。
- 输入模式切换、失焦、出现横屏遮罩时，取消已有手势并清空移动、视角、普攻和蓄力输入；不合成释放事件，不补放重击或技能。
- 遮罩解除后必须重新按下才接受新的动作；普攻按钮的旧按住由 Binder 在阻断时清除，不会恢复。
- 输入门控应先于采样与消费生效，同时清除 pressed、held、release 和待消费队列。受阻断期间不得在 Binder 的 LateUpdate 中重新写入旧状态。
- “重新按下”适用于移动、视角、技能与外接键鼠；恢复时仍按住的键／指针先释放，再接受下一次按下。
- 取消仅针对尚未消费的输入和输入手势。已经提交的攻击／施法按现有战斗规则继续，不撤回服务器动作或任意中断技能状态机。
- `?touch=0` / `ForceNoTouch` 会隐藏触控 HUD，因此也**没有攻击入口**；该开关仅用于开发调试。

## 二、手机浏览器横屏遮罩

### 展示规则

- 仅对手机浏览器的游戏入口启用横屏要求；桌面窄窗口不应仅因宽高比触发手机遮罩。设备识别与触摸模式覆盖需统一规划，兼顾触屏电脑。
- 方向判定与渲染尺寸分开：方向优先读取可用的 Screen Orientation 信息，缺失时回退布局视口宽高；高度大于宽度时遮挡，宽度大于等于高度时解除。不能直接把 visualViewport 的宽高比作为方向，因为软键盘可能只缩小可视高度，令竖屏被误判为横屏。
- 页面设置正确的 viewport meta（device-width、initial-scale 和安全区域配置），避免默认虚拟页面宽度干扰布局。软键盘开启期间保持方向判断稳定，回退策略须用真机验证。
- 遮罩覆盖整个游戏容器，层级高于 Unity 画布、加载页面和游戏 UI；显示横置手机图示与“请横置手机后继续游戏”。
- 初次竖屏进入即展示，避免先出现失真的可操作 HUD。加载可以继续，但不能操作游戏。
- 游戏过程中转回竖屏，立即重新遮挡并取消本地输入。
- 横屏恢复后重新计算渲染尺寸、Canvas 适配与安全区域，处理刘海、圆角和浏览器地址栏引起的可用区域变化。

### 页面与 Unity 协作

- 优先在受版本管理的 WebGL 页面模板中实现 DOM 遮罩，使 Unity 尚未加载完成时也能显示提示。
- 页面负责视口检测、遮罩显示和阻止触摸透传；Unity 负责阻断游戏输入与清理已锁存的状态。仅盖住网页不足以保证角色停止操作。
- Unity 初始化前缓存方向状态，初始化完成后同步；恢复可见、调整窗口及方向变化时重新同步，避免漏掉启动期事件。
- 页面到 Unity 的输入门控必须覆盖角色尚未创建、销毁重建和场景切换情况；遮罩存在时不能因重绑而恢复输入。
- 输入阻断采用多个原因合并（竖屏、失焦等），仅移除横屏限制不能解除仍存在的其他阻断。页面状态同步应幂等，初始化同步完成前移动 WebGL 的游戏操作默认关闭。
- 横屏解除按顺序执行：刷新容器／渲染与 UI 尺寸，确认 Unity 接收当前状态并清空旧输入，然后恢复交互。桥接未就绪时保持阻断，避免画面先恢复而输入状态仍陈旧。
- 遮罩只阻断本地操作，不暂停联网世界、服务器时钟或网络消息处理。玩家仍可能受到伤害。
- 如保留“进入游戏／全屏”按钮，可在用户点击时尝试全屏和横屏锁定；拒绝或不支持时继续使用横置提示，无错误弹窗循环。
- 不依赖 Unity 的 Screen.orientation 单独解决浏览器方向问题。
- 自动全屏与方向锁定不列为首版必做项；首版以横置提示与遮罩完成验收，避免扩大用户已确认的范围。

软键盘可能只改变可视视口而不改变布局视口，参考：[MDN VisualViewport](https://developer.mozilla.org/en-US/docs/Web/API/VisualViewport)。

浏览器方向锁定的支持和全屏条件存在差异，参考：[MDN ScreenOrientation.lock](https://developer.mozilla.org/en-US/docs/Web/API/ScreenOrientation/lock)。

## 预计修改范围

| 模块／入口 | 工作内容 |
| --- | --- |
| Sys3C/Input/InputManager.cs | 删除鼠标左键攻击来源；只保留普攻按钮通路；输入阻断与取消 |
| Sys3C/Input/InputBlockGate.cs（新增） | 竖屏／失焦多原因合并的输入阻断门 |
| Sys3C/Input/MobileLandscapeGate.cs（新增） | 轮询页面方向状态并对接输入阻断门 |
| Sys3C/Input/MobileInputBinder.cs | 遮罩期间释放绑定、停写 LateUpdate，恢复时等待新输入 |
| UI/Core/ScreenAdapter.cs | 补安全区域／尺寸变化刷新 |
| WebGL 页面模板与桥接 | `Assets/WebGLTemplates/MobileLandscape/`、`WebGLLandscapeGate.jslib` |
| WebGLBuildScript.cs / ProjectSettings | 出包使用并自检 MobileLandscape 模板 |
| UI、Sys3C 模块 README | 同步输入规则、横屏限制与测试方式 |

以上路径相对 Assets/Scripts/Hotfix/GameSystems；WebGL 页面与 Editor 脚本除外。修改前读取对应模块 README，遵守技术偏好与现有接口边界。

## 实现决策（复审后）

- **所有平台取消鼠标左键攻击**：`InputManager.Update` 只消费普攻按钮的
  `MobileActionButtons → MobileInputBinder → SetAttackHeld` 通路。鼠标点击没有攻击信号，
  UI 点击不会穿透，也不再需要 `EventSystem.RaycastAll` / 指针归属锁存。
- **阻断门是静态多原因合并**：`Landscape` / `FocusLost` 任一生效即阻断；`InputManager` 订阅
  状态变化立即取消按住，不合成释放；解除后移动、视角、防御、冲刺、火球/炎爆按住都要求先释放
  再接受新的按下。普攻按钮状态由 Binder 在阻断时清除，解除后必须重新按下。
- **WebGL 方向状态以页面为准**：页面在 Unity 加载前写入 `window.CoolLandscape`，C# 侧用
  常驻 `DontDestroyOnLoad` 组件逐帧轮询；页面状态未就绪时保持阻断（fail-closed），
  构建脚本会检查产物 `index.html` 必须包含遮罩标记。
- **手机识别**：`navigator.maxTouchPoints > 0` 且 `(pointer: coarse)` 或移动 UA；`?touch=1/0`
  与既有触控覆盖共用同一优先级。桌面窄窗口不带触摸能力时不会触发遮罩。
- **方向判定**：优先 `screen.orientation.type`，缺失时回退布局视口（`documentElement` 宽高），
  不用 `visualViewport`；viewport meta 增加 `viewport-fit=cover` 与
  `interactive-widget=resizes-visual`，降低软键盘误判。
- **横屏恢复顺序**：页面立即隐藏遮罩，Unity 轮询到解除后先解除输入阻断；解锁需求保证
  旧输入不会因画面先恢复而漏出。渲染尺寸与安全区域由浏览器 resize + `ScreenAdapter` 刷新。

## 实施顺序

1. 定位攻击输入与 WebGL 发布入口，确认真实根因和可复用的输入取消接口。
2. 删除鼠标左键攻击来源，保留普攻按钮为唯一攻击入口。
3. 接入页面方向遮罩及 Unity 输入门控，补齐生命周期取消与恢复。
4. 修正横屏恢复后的尺寸和安全区域适配。
5. 补充关键回归测试、真机验收并同步模块 README。

## 验收条件

- 手机连续点击空白、HUD、弹窗、摇杆和视角区域，角色均不普攻。
- 普攻按钮原有操作正常，摇杆加普攻、摇杆加技能等多指组合正常。
- **所有平台**鼠标左键点击/拖动场景或 UI 均不攻击；普攻按钮是唯一攻击入口。
- 普攻长按或技能蓄力期间转竖屏、切后台、失焦，恢复后无补放、无持续移动或转向。
- 首次竖屏打开即有完整遮罩；横置解除；反复旋转和浏览器地址栏伸缩后布局正确。
- 遮罩出现期间，触摸及外接键鼠均不能操作游戏；联网状态仍正常更新。
- Android Chrome 与 iPhone Safari 真机分别验证，自动旋转锁定开启时也能明确看到横置提示。
- 桌面浏览器与 Editor 左键点击场景/UI 均不攻击，普攻按钮正常；桌面缩窄窗口不误触手机遮罩。
- 自动化测试覆盖“普攻按钮为唯一攻击来源”、取消不产生释放、遮罩门控与恢复等待新按下；浏览器方向、全屏和真实触摸行为以真机证据验收。
- 竖屏打开软键盘不能错误解除遮罩；关闭软键盘、地址栏展开收起后方向状态稳定。
- 失焦与竖屏同时存在时，单独横置不能解除失焦阻断；角色重建及桥接延迟不会短暂放行输入。
- 遮罩出现前已经提交的攻击可正常结束，但未消费的连击、重击或蓄力释放不得在恢复后补发。

## 复审结论（2026-11-08）

方案方向可行。根因、发布模板与手机识别策略已在实现前定位并写入"实现决策"。

**后续决定（同日）**：不再做触摸/鼠标攻击来源分流，改为所有平台直接删除鼠标左键攻击，
只保留普攻按钮。原方案中的触摸追踪、UI 射线拦截与指针归属锁存相应移除；横屏遮罩方案不变。
本文不构成真机兼容性已通过的证据。

## 本次范围

实现聚焦输入误触和手机浏览器横屏体验，不重做 HUD、不新增战斗机制，也不改变服务端规则。
自动化测试覆盖输入分流、阻断取消与恢复等待；浏览器方向、全屏和真实触摸仍需按验收条件在
Android Chrome / iPhone Safari 真机执行。

## 实现与验证记录（2026-11-08）

- **攻击来源**：`InputManager` 已删除 `GetMouseButton(0)` 采样，普攻只消费普攻按钮通路；
  `MobileUiPointer`、页面触摸追踪与 `WebGLTouch_GetActiveTouchState` 已移除。
- **火球施法表现**：移除 `Character3C.controller` 中 `Fireball → SkillQ`（Attack03 挥剑）过渡；
  施法期间 Attack 层保持 `AttackIdle`，不再出现像普攻的挥剑动作；Q 技能动画不变。
  `Fireball` 参数保留（`SetTrigger` 仍安全），清理时由 `CleanupSkillAnimation` 复位。
- **EditMode**：输入相关用例全部通过（`InputBlockGateTests`、`InputManagerAttackSourceTests` 等）；
  动画契约 `HitAnimatorContractTests` 6/6。全量 `bash tools/unity-tests.sh EditMode` 为 730/731；
  唯一失败为既有的 `NavMeshExportFreshnessTests`（全量顺序依赖，单跑通过），与本次改动无关。
- **WebGL 出包**：`Cool/Build/WebGL` 成功；`index.html` 含 `landscape-gate`，不再含触摸追踪；
  `WebGL.framework.js` 含 `WebGLLandscapeGate_GetState`；压缩设置还原为 Gzip。
  火球动画修复已包含在最终出包中（`Fireball` 条件 0 条、`SkillQ` 条件 1 条）。
- **真机**：未执行。Android Chrome / iPhone Safari 需按
  `docs/superpowers/guides/webgl-mobile-acceptance.md` §3.7 验收。
- **PlayMode**：当前编辑器活动构建目标为 WebGL，`KcpNet.PlayModeTests` 受
  `!UNITY_WEBGL` 约束被排除，未执行；需切回非 WebGL 目标后补跑。
