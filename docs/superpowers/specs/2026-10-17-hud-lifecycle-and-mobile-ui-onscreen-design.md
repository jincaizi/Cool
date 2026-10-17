# HUD 显隐生命周期 & 移动端 UI 上屏验收 设计

**Date:** 2026-10-17
**Engine:** Unity 2022.3.25f1
**Layer:** Hotfix（`Sys3C/`、`Sys3C/Input/`、`UI/`）
**UI System:** UGUI
**状态:** 待实现

---

## 0. 结论摘要

三件事，一件是缺陷、一件是验收缺口、一件是产品决定。都是小改动，但**判据必须变成断言里的数字**。

| # | 事项 | 性质 |
|---|---|---|
| 1 | HUD 在「确认角色时场上已有一个玩家」这条路上**永久消失** | 缺陷（PlayMode 实测复现 + A/B 对照） |
| 2 | 移动端 UI 的验收只到「root 激活」，没有任何断言管「真的画在屏上、射线打得中、拖了有值」 | 验收缺口（T6 只做了接线探针） |
| 3 | 编辑器里默认看不到摇杆（`Auto` 判成键鼠） | 产品决定：编辑器默认按触屏 |

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| HUD 隐藏判据 | 换成**状态判据**：场上还有「已启动的本地角色」就不收 | 与帧序无关（新旧谁先跑都对）；不对 `PlayerSpawner` 塞等待帧 |
| HUD 回归测试形态 | 新 PlayMode 用例，**不加载 DemoDay** | 避开「第二个 LoadScene 用例」的场景秩序债；不需要服务器，跑得快 |
| 摇杆「上屏」验收 | 补进 `MobileTouchInputTests`（独立用例，自带场景加载与清理） | 现有用例已是四段式叙事，塞第五段会让失败定位变浑 |
| 编辑器触控 | `TouchInputAvailability.Resolve` 的 `Auto` 分支在编辑器判真 | 编辑器可看可用；`UNITY_EDITOR` 分支**不进包** ⇒ 桌面浏览器的面试 demo 不受影响 |
| 编辑器快捷键（F9 之类） | **不做** | 现有 Inspector 档位已能切回键鼠，够用 |
| 摇杆外观（方块 → 圆形摇杆头） | **不做** | 美术决定，列入 backlog |

---

## 1. 缺陷一：HUD 消失在「确认角色时已有一个玩家」

### 1.1 现象与复现（PlayMode 实测）

| 路径 | 结果 |
|---|---|
| 两场景流程（`CharacterSelect` 场景 → 联网 → `DemoDay`），即面试 demo 那条 | HUD **正常**：实测 `alpha=1.00 vis=True live=1` |
| **直接 Play `DemoDay` → 选人面板确认**（编辑器里最省事的测法） | HUD **永久消失**：确认后逐帧 `alpha=0.00`，`live` 实例在同一帧里从旧 ID 换成新 ID |
| 同上，但**先手动销毁旧玩家、等 3 帧再生成**新玩家（A/B 对照） | `alpha=1.00`，稳定保持到采样结束 |

- **单机与联网都复现** —— 判据与网络模式无关。「联网」只是这台机器的存档模式（`NetworkModeService.Load()=Online`）。
- 摇杆在编辑器里看不到，正是因为这条路径上的「直接 Play」是编辑器里的默认测法。

### 1.2 机制

```
Confirm → EventBus.Emit(CharacterSelectedEvent)
        → PlayerSpawner.OnCharacterSelected:
              Destroy(旧玩家)        ← 延迟到帧末才生效
              Instantiate(新玩家)    ← Awake 立即，Start 同帧内跑完（实测）
                  └─ Start → BindHudWhenReady → hud.Bind() + ShowAlways → alpha=1   ← 先显示
帧末     → 旧玩家 Sys3CEntry.OnDestroy
              → hud.Unbind() + HideAlways → alpha=0                                  ← 后被藏
```

新玩家只在自己的 `Start` 里显示过一次；旧玩家的 `OnDestroy` 是它之后的**最后一次写入**，而没有任何人再写回 ⇒ HUD 永久隐藏（且已 `Unbind`，血条也不再更新）。

### 1.3 不是 UI 重构引入的（避免误判归因）

| 证据 | 结论 |
|---|---|
| `04bada5` 的 diff：重构前后 `Start` 里的「绑 HUD + ShowAlways」与 `OnDestroy` 里的「Unbind + HideAlways」**逐字相同** | 时序结构未变 |
| 层 Canvas 的创建（`CreateCanvasLayers`/`BuildLayerCanvas`，`6e6cf1e`）前后等价 | 面板层级无关 |
| `0d769fb` 的提交信息：「UIAnimation 从未挂到任何预制体……面板过渡从来没有动画过」 | 不存在「重构删掉动画 ⇒ 帧序变了」 |
| 真正常年潜伏的两个前提：`e7260ea`（`PlayerSpawner.Start` 用存档性别先生成一个玩家）+ `b4b2f4b`（OnDestroy 收起 HUD） | 两者相遇才中招 |

**两场景流程为什么不中招**：两个场景文件里都**没有直接挂载 `Sys3CEntry` 的对象**（按脚本 guid 全场景核对）——DemoDay 的玩家来自预制体实例化（不写在场景里），选人场景的预览体是场景内烘焙的剥离对象、也不是玩家预制体的实例。于是 DemoDay 的玩家是一次性生成的，**没有「旧玩家被销毁」这一步**。

### 1.4 设计：把判据从「实例被销毁」换成状态边沿

`Sys3CEntry.OnDestroy` 只在**场上再没有别的「已启动」本地角色**时才收起 HUD：

```csharp
private void OnDestroy()
{
    PhysicsRegistry.Instance.Unregister(this);

    if (_statsProvider == null) return;
    if (HasOtherStartedLocalEntry()) return;   // ← 新增：还有别人在管 HUD，别插手（也不 Unbind）
    // …其余原样：取面板 → Unbind → HideAlwaysAsync
}

private bool HasOtherStartedLocalEntry()
{
    foreach (var entry in UnityEngine.Object.FindObjectsOfType<Sys3CEntry>())
    {
        if (entry == this) continue;
        if (!entry.enabled) continue;
        if (entry._statsProvider == null) continue;   // 同类私有字段，可访问
        return true;
    }
    return false;
}
```

**为什么判据是 `_statsProvider` 而不是 `enabled`**：只有 `Start` 跑完的 entry 才绑过 HUD、也才会在 `Start` 里重新显示 HUD；`_statsProvider` 正是在 `Start` 里 `BindHudWhenReady()` 之前建立的。而 `enabled` 单独用不够——远端头像预制体自带的 `Sys3CEntry` 是 `enabled=false`（bot 视图），但 `FindObjectsOfType` **会返回「激活对象上的禁用组件」**（本项目已在 `MobileInputBinder.PickLocalEntry` 注释里记录过这个陷阱）。两者叠加：`enabled` 挡 bot 视图，`_statsProvider` 兜住「enabled 但 Start 还没跑完」的窗口。

**边界情形**（改动不得越界）：

| 情形 | 销毁那一刻场上还有已启动的本地角色吗 | 行为 | 与今天相比 |
|---|---|---|---|
| 确认角色重建玩家 | 有（新玩家已 Start，实测同帧内完成） | 不收、不解绑，HUD 归新玩家 | **行为改变（本缺陷的修复）** |
| 阵亡 → `LoadScene("CharacterSelect")` 回选人 | 没有（选人场景无 `Sys3CEntry`） | 收起 + 解绑 | 不变 |
| 退出 PlayMode / 关卡切换 | 没有 | 收起 + 解绑 | 不变 |
| bot 远端视图被销毁 | —— | 该视图 `_statsProvider == null`，首行即返回 | 不变（T12 的守卫保持有效） |
| 断线（`Disconnect()` 不销毁玩家） | —— | `OnDestroy` 根本不跑 | 不变 |

**明确不声称**：这条不变量只管「本地角色被销毁」这条边沿。开场时 HUD 先显示、纯观察者模式（无本地角色）下 HUD 该不该显示 —— 现状保留，不在本设计范围。

### 1.5 回归守卫（两条边沿）

新文件 `Assets/Scripts/Tests/PlayMode/HudLifecycleTests.cs`（PlayMode，**不加载 DemoDay**）：

| 用例 | 装置 | 断言 |
|---|---|---|
| `test_hud_stays_visible_when_player_rebuilt_while_old_dies` | `UIBootstrap.EnsureUIManager()`（真面板，幂等，复用 `UIBootstrapTests` 的接缝）+ `PlayerEntryForTest` 实例化 A，等 A 的 `StatsProvider != null`（此时 HUD 已绑并显示）；**同帧先 `Destroy(A)` 再 `Instantiate(B)`** | 等 B 的 `StatsProvider != null` **再等 2 帧**（确保 A 的帧末销毁已发生；`PlayerHudBindingTests.Frames(2)` 同惯例）→ HUD `IsVisible==true` 且 `alpha==1`。**修复前为红** |
| `test_hud_hides_when_last_local_entry_dies` | 同上，只销毁唯一 entry | HUD 收起（`alpha==0`）——防修复把 T12「回选人收起 HUD」的意图改坏 |

- **不加载场景的理由**：`MobileTouchInputTests` 是全项目唯一 LoadScene 的用例（且它当场清理），再加一个会引入场景秩序债；本用例只需要「真 UIManager + 两个本地角色」，`PlayerEntryForTest` 足够。
- **销毁顺序必须与 `PlayerSpawner` 一致**（先 `Destroy` 后 `Instantiate`，同一帧）：顺序反了就走不到「新玩家已 Start、旧玩家帧末才销毁」这条真实时序，测试会变成假绿。
- `HasOtherStartedLocalEntry()` 保持 **private**：两条用例只复刻 `PlayerSpawner` 的那两次调用（或直接销毁唯一 entry），不访问 helper，不为测试新开内部 API。
- 收尾：`TearDown` 销毁本用例的实例（销毁最后一个时 HUD 会自我收起，正好回到干净状态）。

---

## 2. 缺陷二：移动端 UI 的「上屏」没有验收

### 2.1 现状：覆盖到哪、缺什么

| 已有断言（`MobileTouchInputTests`） | 缺口 |
|---|---|
| ① `ForceTouch` → 适配器换成 Joystick、`MobileUiRoot.activeSelf == true` | 「激活」≠「可见可点」：节点可以是 0 尺寸、在屏外、或被别的 raycast 目标盖住 |
| ② `ForceNoTouch` → 适配器还原成 Keyboard、root 不再激活 | 同上 |
| ③ 切回触屏能重新绑定 | —— |
| ④ 玩家重建后重新绑定到新玩家的 `InputManager` | —— |

`production/verify/t6_*` 一整套验证都是**程序化探针**（EventTrigger 解析、适配器注入），从没有人眼或截图确认过它真的画出来过。手机验收清单（`docs/superpowers/guides/webgl-mobile-acceptance.md` 3.2/3.3）要求人眼确认的正是这一段。

### 2.2 设计：新增用例 `test_touch_ui_lands_on_screen_and_joystick_stays_analog`

在 `MobileTouchInputTests` 里新增一个**独立用例**（自带 `LoadScene("DemoDay")` 与收尾清理，`LogAssert.ignoreFailingMessages` 沿用现有做法；显式档位而非 `Auto`，与本文件既有约定一致）：

| 断言组 | 内容 | 对应的人工验收项 |
|---|---|---|
| 几何 | 摇杆 `RectTransform` 的四个世界角落在 `[0,Screen.width]×[0,Screen.height]` 内；其中心点在**左半屏且下半屏** | 3.2「左下摇杆」 |
| 射线 | `EventSystem.RaycastAll`（合成 `PointerEventData`，位置 = 摇杆背景中心）**命中摇杆背景节点** | 3.2「出现了」的可点版本 |
| 模拟量 | 合成 `pointerDown` + 小位移拖 20px、大位移拖 90px（屏幕像素）：`0 < \|Value(20px)\| < \|Value(90px)\| ≤ 1`。只断言**大小关系**，不断言饱和（Game View 尺寸变了，canvas scaleFactor 跟着变，饱和点会移） | 3.3「半推=慢走、满推=快走」 |
| 抬指 | 合成 `pointerUp` 后 `Value == Vector2.zero` | 3.3「松手立刻停」 |
| 三按钮 | 三个按各自在世界角落在屏内、且中心射线命中自身 | 3.2「右下三个按钮」 |

**边界（写进用例注释，避免过度声称）**：
- 本用例验的是**触控 UI 这一层**（几何/命中/模拟量）。「`Value` → `JoystickInputAdapter` → `InputManager`」那条链已由本文件 ① 与 `MobileInputAdapterTests` 覆盖，不在此重复。
- 摇杆的**视觉样式**（当前是半透明方块背景 + 方块手柄，不是圆形摇杆头）不在断言范围。
- 真手指只在手机上能验；本用例走的是合成指针事件（与 3.2 的「`EventSystem.RaycastAll` 那条路」同一条）。

---

## 3. 产品决定：编辑器默认按触屏

### 3.1 改动

`TouchInputAvailability.Resolve` 的自动分支加编辑器例外（全项目**只有 `MobileInputBinder.Update` 一个调用点**，影响面单点）：

```csharp
public static bool Resolve(TouchOverride mode)
{
    bool? fromUrl = FromUrlQuery();          // 出包后唯一的救命绳，优先级最高（不变）
    if (fromUrl.HasValue) return fromUrl.Value;
    if (mode == TouchOverride.ForceTouch) return true;
    if (mode == TouchOverride.ForceNoTouch) return false;
#if UNITY_EDITOR
    return true;                             // 编辑器默认按触屏：摇杆 UI 可看可用，方便迭代
#else
    return Application.isMobilePlatform || TouchFeatureDetected();
#endif
}
```

- **编辑器效果**：摇杆与三按钮默认出现，且**能用**（鼠标拖摇杆走路、点三按钮攻击/防御/跳跃）。
- **代价（接受）**：编辑器里**整条键鼠通道**都不再驱动角色 —— 不只 WASD/空格：适配器被**整体替换**成 `JoystickInputAdapter`（移动 = 摇杆、视角 = 观察区拖拽），而键盘适配器的视角读的是 `Input.GetAxisRaw("Mouse X/Y")`。因此**不存在两套输入叠加**（同一时刻只有一个适配器），但编辑器里也只剩「鼠标拖摇杆 + 点三按钮」。要切回键鼠：Play 时把 `MobileInput` 上的 `TouchOverride` 拨到 `ForceNoTouch`（运行时改，退出即还原）。
- **附带**：编辑器里自动判定不再经过 `TouchFeatureDetected()`（其编辑器分支读 `Input.touchSupported`）；显式档位与 URL 覆盖的判定分支不受影响。
- **出包不受影响**：`#if UNITY_EDITOR` 是编译期的，WebGL 产物里 `Auto` 仍是「`isMobilePlatform` || 特性检测」⇒ 面试 demo 在桌面浏览器保持键鼠，「PC 浏览器维持键鼠」这条不变量只在**编辑器**被放开。

### 3.2 同批必须更新的话术与注释（否则留下假话）

| 位置 | 现在写的 | 改后 |
|---|---|---|
| `MobileInputBinder.cs:12` | 「PC 浏览器与编辑器维持键鼠，不影响既有验收」 | 「PC **浏览器**维持键鼠；**编辑器默认按触屏**（便于迭代），可用 Inspector 档位切回」 |
| `MobileInputBinder.cs:28-35`（`TouchOverride` 文档块，其中 `:33` 是失效句） | 「`Application.isMobilePlatform` 在编辑器恒 false，那条分支按构造测不到」 | 该句的因果已变（编辑器现在默认就走那条分支）；保留「出包后只能靠 URL 覆盖」的原意 |
| `TouchInputAvailability.cs:20` | 判定顺序「URL 参数 > 人工档位 > (isMobilePlatform \|\| 特性检测)」 | 插入一层：「URL 参数 > 人工档位 > **编辑器⇒真** > (isMobilePlatform \|\| 特性检测)」 |
| `MobileTouchInputTests.cs:18-19` | 「在编辑器恒 false，所以这条分支此前按构造测不到」 | 改为「编辑器现在默认进该分支；本用例仍用显式档位断言，不依赖 `Auto` 的结果」 |
| `MobileTouchInputTests.cs:114-117` | 「本用例是 Tests/ 下**唯一** LoadScene 的用例」 | 本文件现在有两个 LoadScene 用例（各自清理），该句作废 |
| `docs/superpowers/guides/webgl-mobile-acceptance.md` §边界表 | 「`Application.isMobilePlatform` 在编辑器恒 false ⇒ 测不到」 | 补一句：编辑器 Play 现在默认就能看到触控 UI（`?touch=1` 仍是出包后的兜底） |
| `docs/superpowers/guides/webgl-demo-boundaries.md:71-76`（面试话术，可选） | 三层判定 + 一个手工兜底 | 可加一句「编辑器里我默认按触屏走，方便迭代，出包不受影响」 |

### 3.3 决策钉：`test_editor_auto_resolves_to_touch`

在 `TouchInputAvailabilityTests` 加一条 EditMode 断言：编辑器里 `Resolve(TouchOverride.Auto) == true`。这是**决策钉**（防止有人把它「修」回 false 而让摇杆在编辑器里消失），注释需写明它是产品决定、不是行为保证。`ForceTouch`/`ForceNoTouch` 的既有断言不变。

---

## 4. 验收判据（可测）

| # | 判据 | 落点 |
|---|---|---|
| 1 | 重建玩家（同帧 `Destroy(旧)` + `Instantiate(新)`）后，HUD `CanvasGroup.alpha == 1` 且 `IsVisible == true` | `test_hud_stays_visible_when_player_rebuilt_while_old_dies`（**修复前为红**） |
| 2 | 销毁**最后一个**本地 entry 后，HUD 收起（alpha == 0） | `test_hud_hides_when_last_local_entry_dies`（防修复把 T12 的意图改坏） |
| 3 | 编辑器里 `Resolve(Auto) == true` | `test_editor_auto_resolves_to_touch` |
| 4 | 摇杆四角在屏内、中心在左下半屏；射线可命中；小拖 < 大拖且 ≤ 1；抬指归零；三按钮在屏内可命中 | `test_touch_ui_lands_on_screen_and_joystick_stays_analog` |
| 5 | 人工只验**可见**（启动态 + 进入世界后各一张截图）；可拖/可点/模拟量由 `MobileTouchInputTests` 的断言承担。确认前被选人面板（3000）压暗、确认后可用 | 人工（截图留档 `production/verify/`） |
| 6 | 全量 EditMode / PlayMode 无新增失败 | 回归 |

---

## 5. 明确不做

- 摇杆/按钮的视觉样式（美术决定，backlog）
- 编辑器快捷键（F9 等）切换档位
- 面板过渡动画（UI 重构的阶段 5）
- 阶段 3a 的模态输入闸门与 `UIInputGate`（另一条工作线，本设计不依赖它）
- 断线 / 纯观察者模式下 HUD 该不该显示的语义（保持现状）
- `ShowAlwaysAsync` / `HideAlwaysAsync` 的 API 形态与 HUD 控制器抽象（单点判据够用）

---

## 6. 风险与已知边界

| 风险 | 评估 |
|---|---|
| `FindObjectsOfType` 出现在 `OnDestroy` | 只在**本地角色被销毁**时执行一次（非每帧）；调用方限定在 `_statsProvider != null` 之后 |
| 编辑器默认触屏后，右半屏/左下角的鼠标点击被 `LookArea`/摇杆吃掉 | `MobileUiRoot` 的 Canvas `sortingOrder=**2500**`（2026-10-17 实测；此前误记为 100，来源是 T6 的生成脚本而非场景实际值）——**高于 Base(1000)/Main(2000)、低于 Popup(3000)**。当前只有 Base 层的 HUD/TargetPanel：HUD 在左上、与右半屏/左下不重叠，TargetPanel 无按钮 ⇒ 无实际影响；**将来在 Base/Main 层放带按钮的面板时要当心**：落在触控 UI 区域内的按钮会被吃掉。Popup 层（选人面板）始终在触控层之上，且它启动即全屏打开——触控 UI 在确认前本来就不该可点。 |
| 编辑器默认触屏让「键鼠可玩」这一习惯失效 | 整条键鼠通道（移动/视角/按键）整体被替换，见 §3.1；`ForceNoTouch` 一档可切回。**不叠加**（键盘的视角读鼠标轴、摇杆的读观察区，同一时刻只有一个适配器） |
| 触控 UI 在编辑器默认出现，e2e 用例可能依赖「默认键鼠」 | **两个类**会加载 DemoDay：`MobileTouchInputTests` 与 `ClientRespawnWaitTests`（后者需要本地服务器）；编辑器默认触屏会给 DemoDay 的本地角色装 Joystick 适配器——本次已补跑后者验证。核对命令：`grep -rn 'LoadScene("DemoDay")' Assets/Scripts/Tests` |
| 本设计修不了「玩家的 `Start` 早退（`CharacterController`/`Animator` 为空）」导致的 HUD 不显示 | 已知边沿：那种情况会打 `Debug.LogError`，属另一类问题 |

---

## 7. 复核记录（2026-10-17，自审）

逐条对代码核对后的结论与修正：

| 核对项 | 结论 |
|---|---|
| `FindObjectsOfType` 会不会返回预制体资产 | **不会**（只有 `Resources.FindObjectsOfTypeAll` 会；实机探针里 3 个预制体资产只在后者出现） |
| bot 远端视图会不会被误判成本地角色 | 不会：它们是 `enabled=false` 的组件（`FindObjectsOfType` 会返回它），靠 `enabled` 过滤掉；`_statsProvider` 再兜一层「Start 没跑完」 |
| 同一帧销毁两个本地角色时判据会不会漏 | 不会：Unity 逐个销毁，先销毁的不会出现在后一个对象的查询结果里 ⇒ 最终那个一定能收起 HUD |
| 销毁顺序 | `PlayerSpawner.OnCharacterSelected` 是**先 `Destroy` 后 `Instantiate`**；§1.5 的测试装置已按同一顺序钉死 |
| 两套输入适配器是否叠加 | **不叠加**：`MobileInputBinder` 整体替换适配器；键盘的视角读 `Input.GetAxisRaw("Mouse X/Y")`，摇杆的读观察区 |
| 编辑器里自动判定改真后，`TouchFeatureDetected()` 的编辑器分支 | 成为自动路径的死代码（显式档位/URL 覆盖不受影响）——已记入 §3.1 |
| `IEffectTarget.Heal` / `ITargetable` HP 系列对 `_stats` 的守卫 | 本来就有；§1.4 的判空与它们是同一约定（2026-10-17 的 npc 视图 NRE 修复把 `IsAlive`/`TakeDamage` 补齐，同族已完整） |
| 右半屏点击被 `LookArea` 吃掉时面板是否受影响 | **本条在实现期被推翻**：`MobileUiRoot` 实际 `sortOrder=2500`（Base/Main 之上、Popup 之下）⇒ Base/Main 层落在触控区域内的按钮会被吃掉（当前布局无此情况，见 §6）；Popup 层安全 |

**修正了三处**：①新增 §1.5（原文只在 §4 列断言，没写测试装置/等待配方/private 决定）；②§3.1 的「代价」原文只提 WASD/空格，实际是**整条键鼠通道**；③§3.2 的两处行号收紧（`:28-35`、`:18-19`）。

**实现期补充修正（2026-10-17，任务 3）**：`MobileUiRoot` 的 Canvas 排序号实测为 **2500**（原文按 T6 生成脚本记为 100），据此更正 §6/§7 并补出「Base/Main 层落在触控区域内的按钮会被触控层吃掉」这条约束。同时任务 3 的测试装置补了一个计划遗漏：**选人面板启动即全屏打开（Popup 3000）会盖住触控层**，用例须按真实流程先 `CloseAsync<CharacterSelectPanel>()` 再做上屏/射线断言。
