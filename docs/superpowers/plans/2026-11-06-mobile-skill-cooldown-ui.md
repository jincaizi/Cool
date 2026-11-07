# WebGL 手机技能冷却与普攻剑气提示实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在 WebGL 手机 HUD 上实现 5 个可触摸技能按钮、径向冷却/数字倒计时、蓄力反馈，以及普攻剑气就绪金边。

**架构：** 输入继续通过 `MobileActionButtons → MobileInputBinder → InputManager → Sys3CEntry` 汇入现有技能系统；`MobileSkillButton` 只表达触摸和显示状态。`MobileSkillBarPresenter` 从本地 `Sys3CEntry.SkillCoordinator` 读取冷却与蓄力真值，并读取 `CanFireHeavy` 驱动普攻金边，不维护第二套战斗状态。

**技术栈：** Unity 2022.3.25f1、UGUI、TextMeshPro、NUnit EditMode/PlayMode、WebGL 触摸输入。

**规格：** `docs/superpowers/specs/2026-11-06-mobile-skill-cooldown-ui-design.md`

## 全局约束

- 基于当前未提交的 `DemoDay.unity` 与 UI 贴图工作；不得覆盖或回退用户已有场景、美术和 HUD 改动。
- 技能输入不得从 UI 直接调用 `SkillCoordinator`，必须沿用现有移动输入桥。
- 冷却真值只能来自 `SkillCoordinator.GetCooldownInfo`；火球/炎爆在实际释放时才开始冷却。
- 普攻金边只能读取现有重击就绪状态，不得按固定按住时长推算。
- WebGL 失焦、UI 停用、触控关闭和角色重建必须清除全部触摸状态。
- 不新增第三方包；复用 `Assets/PreRes/Texture/Com/` 中现有图标与圆框。
- 修改 UI、Skills、Sys3C 行为后同步各模块 README。

## 审查重点（Review Focus）

- 同一根手指快速按下并松开火球/炎爆时，pressed 边沿不能因 `LateUpdate` 桥接而丢失。
- R 在旋转期间再次触摸必须进入现有取消分支，不能被冷却门禁吞掉或进入延迟缓冲。
- 页面失焦或触控 UI 隐藏时，普攻、火球、炎爆不得残留 held 状态。
- 本地角色销毁重建后，技能栏必须重新绑定新 `Sys3CEntry`，不能继续显示旧角色冷却。
- 冷却剩余不足 1 秒时必须显示 `1`，不得提前显示 `0`；结束时遮罩和文字必须一起隐藏。

---

### 任务 1：移动端技能输入桥

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileActionButtons.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`
- 测试：`Assets/Scripts/Tests/Editor/MobileInputManagerUiHooksTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/MobileSkillInputBridgeTests.cs`

**Interfaces：**
- `MobileActionButtons` 增加 Q/R/Meteor 一次性按下锁存、Fireball/ChargeBlast 的 pressed + held 状态及消费/清理方法。
- `InputManager` 增加 `SetSkillQPressedFromUI(bool)`、`SetSkillRPressedFromUI(bool)`、`SetSkillFireballPressedFromUI(bool)`、`SetSkillFireballHeldFromUI(bool)`、`SetSkillChargeBlastPressedFromUI(bool)`、`SetSkillChargeBlastHeldFromUI(bool)`、`SetSkillMeteorPressedFromUI(bool)`。
- `MobileInputBinder` 在 `LateUpdate` 汇入上述状态，在消费后清除一次性锁存；`OnDisable`、非触控分支和角色解绑路径调用统一的 `ClearCombatUiInput()`。
- `MobileInputBinder` 暴露只读 `BoundEntry`，供任务 2 的 presenter 读取同一个本地角色。

- [ ] **步骤 1：编写失败的输入桥测试**

  添加以下测试并断言：
  - `test_ui_skill_q_r_and_meteor_each_emit_one_press_edge`
  - `test_ui_fireball_press_hold_release_matches_keyboard_semantics`
  - `test_ui_charge_blast_press_hold_release_matches_keyboard_semantics`
  - `test_mobile_action_buttons_consume_one_shots_but_keep_holds`
  - `test_clear_combat_ui_input_releases_attack_and_charge_holds`

- [ ] **步骤 2：运行测试确认失败**

  运行：`bash tools/unity-tests.sh EditMode MobileInputManagerUiHooksTests` 和 `bash tools/unity-tests.sh EditMode MobileSkillInputBridgeTests`

  预期：FAIL，缺少上述 UI 输入接口。

- [ ] **步骤 3：实现最小输入桥**

  按 Interfaces 增加状态与方法。一次性 pressed 必须保留到消费者读取，不能在每帧 `Update` 开头提前清掉；held 每帧同步。Q/R/Meteor 消费后归零，Fireball/ChargeBlast 的 pressed 消费后归零但 held 保留到 pointer up。

- [ ] **步骤 4：运行聚焦测试验证通过**

  预期：新增和既有移动输入测试全部 PASS。

- [ ] **步骤 5：提交**

  `git commit -m "feat(ui): bridge mobile skill button input"`

### 任务 2：技能按钮冷却、蓄力与普攻金边组件

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillButton.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillBarPresenter.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/AttackHeavyReadyView.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 测试：`Assets/Scripts/Tests/Editor/MobileSkillButtonTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/AttackHeavyReadyViewTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/MobileSkillBarPresenterTests.cs`

**Interfaces：**
- `MobileSkillButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler`，序列化 `SkillID`、`MobileActionButtons`、冷却 `Image`、蓄力 `Image`、倒计时 `TMP_Text`；公开只读 `SkillId`。
- `MobileSkillButton.RenderCooldown(CooldownInfo info)`：冷却时遮罩 `fillAmount = Remaining / Total`，文本为 `CeilToInt(Remaining)`；结束时两者隐藏。
- `MobileSkillButton.RenderCharge(float normalized)`：仅 `normalized > 0` 时显示亮色径向外环。
- `AttackHeavyReadyView.SetReady(bool ready)`：切换默认/暖金图标颜色与金色边框显隐。
- `MobileSkillBarPresenter` 序列化 `MobileInputBinder`、5 个技能按钮和普攻 ready view；每帧从 `BoundEntry.SkillCoordinator` 读取冷却、当前技能 ID/Charging/charge progress。
- `Sys3CEntry` 增加 `public bool CanFireHeavy => _canFireHeavy;`；所有重置 `_canFireHeavy` 的既有路径保持唯一真值。

- [ ] **步骤 1：编写失败的显示状态测试**

  覆盖：冷却 2.01 秒显示 `3`；遮罩按剩余比例；结束同时隐藏；蓄力外环只在 Charging 显示；剑气就绪切金色和金框；解绑/无角色时全部复位；本地角色引用变化后 presenter 使用新角色。

- [ ] **步骤 2：运行测试确认失败**

  运行：`bash tools/unity-tests.sh EditMode MobileSkillButtonTests`、`AttackHeavyReadyViewTests`、`MobileSkillBarPresenterTests`。

  预期：FAIL，组件尚不存在。

- [ ] **步骤 3：实现视图组件与 presenter**

  按 Interfaces 实现；视图只接收状态，不自行计时。`MobileSkillButton.OnDisable` 必须释放 Fireball/ChargeBlast held，避免按钮被隐藏时持续蓄力。

- [ ] **步骤 4：运行聚焦测试验证通过**

  预期：三个测试类全部 PASS，且 `SkillCoordinator*Tests` 无回归。

- [ ] **步骤 5：提交**

  `git commit -m "feat(ui): show skill cooldowns and heavy readiness"`

### 任务 3：DemoDay 双层扇形布局与端到端验收

**文件：**
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 修改：`Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`

- [ ] **步骤 1：先更新场景契约测试**

  将动作按钮检查从“仅 3 个按钮”改为按名字断言 `AttackButton`、`DefendButton`、`JumpButton`、`SkillQButton`、`SkillRButton`、`FireballButton`、`ChargeBlastButton`、`MeteorButton`。每个按钮必须完整在屏内、中心射线首位命中自身，且不存在相互覆盖。

  新增 `MobileSkillHudTests`，断言 5 个 `MobileSkillButton` 的技能 ID、冷却 Image 为 Radial360/顺时针、倒计时文本存在、普攻按钮具有 `AttackHeavyReadyView`。

- [ ] **步骤 2：运行 PlayMode 测试确认失败**

  运行：`bash tools/unity-tests.sh PlayMode MobileTouchInputTests` 和 `bash tools/unity-tests.sh PlayMode MobileSkillHudTests`。

  预期：FAIL，场景尚缺 5 个技能按钮与 presenter 接线。

- [ ] **步骤 3：在 Unity Editor 中完成双层扇形布局**

  保留普攻圆心及现有用户改动：
  - 内层逆时针：Defend、Jump、Q、R，半径 115–125 px，按钮 78–84 px。
  - 外层逆时针：Fireball、ChargeBlast、Meteor，半径 185–200 px，按钮 70–76 px。
  - 按规格表绑定现有图标；每个技能位创建冷却遮罩、倒计时、蓄力外环和角标。
  - 普攻按钮增加默认灰框与 `button-gold-round.png` 金框子对象。
  - `MobileInput` 上挂 `MobileSkillBarPresenter` 并连接 binder、5 个按钮、普攻 ready view。
  - 冷却/蓄力装饰 Image 必须 `raycastTarget = false`，触摸射线只命中按钮根节点。

- [ ] **步骤 4：更新 README**

  记录移动技能输入链、双层扇形、径向冷却、蓄力外环、剑气金边和 WebGL 失焦清理语义。

- [ ] **步骤 5：运行完整验证**

  运行：
  - `bash tools/unity-tests.sh EditMode MobileInputManagerUiHooksTests`
  - `bash tools/unity-tests.sh EditMode MobileSkillInputBridgeTests`
  - `bash tools/unity-tests.sh EditMode MobileSkillButtonTests`
  - `bash tools/unity-tests.sh EditMode AttackHeavyReadyViewTests`
  - `bash tools/unity-tests.sh EditMode MobileSkillBarPresenterTests`
  - `bash tools/unity-tests.sh PlayMode MobileTouchInputTests`
  - `bash tools/unity-tests.sh PlayMode MobileSkillHudTests`

  WebGL 手工验收：Q/R/3 点击，1/2 按住松开，普攻长按至金边后松手，浏览器切后台再返回；不得出现残留 held、误触或冷却显示跳变。

- [ ] **步骤 6：提交**

  `git commit -m "feat(ui): add radial mobile skill layout"`

