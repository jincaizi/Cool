# UI 框架重构设计

**Date:** 2026-10-14
**Engine:** Unity 2022.3.25f1
**Layer:** Hotfix（`Assets/Scripts/Hotfix/GameSystems/UI/`）
**UI System:** UGUI
**Async Model:** UniTask
**Resource:** Addressables（本地组，经 `Core.Resource.Res`）
**Input:** 旧输入栈（`UnityEngine.Input` + `StandaloneInputModule`）
**状态:** 待实现

---

## 0. 结论摘要

框架的"形状"（分层 + 显隐模式 + 生命周期钩子）本身是对的；问题在于**三条主干（动画、栈、模态）的实现与接线都是空的**，而真正会咬人的是三处运行时契约缺陷：

1. **异步化会把同步初始化掀成异步** —— 换 Addressables 的那一刻，几个静默依赖"启动即就绪"的消费方会失效（详见 §3.3）
2. **框架所有可选组件都是静默降级的** —— 面板预制体没有一个带 Canvas 组件，框架却提供 `CanvasSwitch` 模式（详见 §3.2）
3. **层与重建边界被绑成同一个东西** —— 同层面板共享 Canvas 重建根（详见 §2.2）

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| 重构策略 | 分阶段重构（5 步），非一次性重写 | 仓库里跑着正在验收的链路（WebGL 面试 demo 是当前里程碑），必须每步可独立验证 |
| Addressables | **纳入**，但验收判据改写 | 真实收益是"可卸载 + 将来可远端"，不是首包（Resources 实测仅 7 文件 / 208KB） |
| 目标规模 | 15+ 面板（完整 MMO 客户端） | 懒加载/分类缓存/完整层级与模态模型都是必要投入，非过度设计 |
| 模态语义 | **默认模态**，面板可声明例外 | 与主流手游一致（点开背包角色站住） |
| 输入栈 | **本次收口到旧输入** | 新输入系统 0 处使用、0 个 action 资产；收口目的是消除未决状态，不是升级能力 |
| Bag UI 三件套 | **收编进框架**，作为第 4 个面板样板 | 它们代表框架外的另一套写法，是检验新框架的好样本 |

---

## 1. 审核发现（分级）

严重度按"会不会咬人"排，不按"好不好看"排。

| # | 发现 | 证据 | 级别 |
|---|---|---|---|
| R1 | Addressables 迁移会把同步初始化变成异步，消费方静默失效 | `RegisterFromPrefab` 体内零 `await`；`Resources.Load` 同步 | 高 |
| R2 | 面板预制体无 Canvas 组件，`CanvasSwitch` 模式静默失效（关不掉且无日志） | 4 个预制体 Canvas 组件数=0；`UIPanel.ApplyVisibilityOff` 判空 | 高 |
| R3 | 同层所有面板共享一个 Canvas 重建根 | HUD 与 TargetPanel 同属 Base 层 | 高 |
| G | 无模态/输入闸门 | 全库无 `IsPointerOverGameObject` / `EventSystem.current` | 高 |
| R6 | 安全区施加于 Canvas 根，且三种处理状态并存 | `ScreenAdapter.ApplySafeAreaOffset`；`MobileUiRoot` 完全无安全区 | 高 |
| R4 | 每次数值变化分配字符串 + 触发 TMP 重建 | `$"{current}/{max}"` | 中 |
| R5 | 每 BuffIcon 一个 `Update()`；两面板逐字重复约 45 行池化逻辑 | `PlayerHudPanel` vs `TargetPanel` | 中 |
| R7 | 输入栈未收口：`activeInputHandler: 2`(Both)，场景用 `StandaloneInputModule`，代码读旧 API，却引了 Input System 包 | GUID `4f231c4f…` 实测；manifest `com.unity.inputsystem: 1.7.0` | 中 |
| R8 | 两场景各带一份 UIManager 与 EventSystem，靠 Awake 顺序裁决单例；`Instance` 未在 OnDestroy 清空 | 两处 `m_Name: UIManager` | 中 |
| R9 | Addressables 收益与立项理由不符 | `Assets/Resources/` 实测 7 文件 / 208KB | 中 |
| R10 | 测试反射进私有字段/方法，测实现而非行为 | `GetPrivateField` / `InvokePrivate` | 中 |
| R11 | 名牌画布挂 `GraphicRaycaster`，但其下所有 Graphic `raycastTarget=false` | `EntityDisplayManager.cs:54` vs `NameplateRenderer.cs:218` | 低 |
| R12 | 死代码：`UI/Animation/` 4 文件、Stack 三方法、`_activeOverlays` 只写不读、`Bag/UI/` 三件套零引用 | GUID 与调用点全库搜索 | 低 |
| R13 | 层数硬编码 3 处；面板寻址靠魔法字符串 | `UIManager.cs:13/77`、`UIConst.SortOrders` | 低 |

### 1.1 已确认干净（避免无效工作）

- HUD / TargetPanel / BuffIcon **无 LayoutGroup** → 重建便宜
- 名牌、飘字、伤害遮罩的 `raycastTarget` 全部为 false
- 全项目 prefab/scene/asset **零未解析脚本 GUID**（console 里 4 条 "script is missing" 不来自序列化资产）
- `com.unity.addressables: 1.21.21` **已在 manifest** —— 缺的只是配置、分组与标记，不是包本身

### 1.2 关键源码事实（注释与实现相矛盾，以实现为准）

UGUI 的 `Graphic.canvas` 由 `CacheCanvas()` 决定：

```csharp
gameObject.GetComponentsInParent(false, list);   // 从自身向上
// 取第一个 isActiveAndEnabled 的 Canvas
```

即**最近的** active+enabled Canvas。而 `GraphicRaycaster.Raycast()` 只查自己那个 Canvas 的注册表（`GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas)`）。

**结论：嵌进嵌套 Canvas 的交互元素会退出父 Canvas 的射线检测，点不动** —— 除非嵌套 Canvas 自带 `GraphicRaycaster`。

⚠️ 同处 XML 注释写的是 *"the Canvas closest to the root will be used"*，**与实现相反**。照注释设计会得到一个"看得见、点不动"的面板且无任何报错。

---

## 2. 层级与重建边界

### 2.1 单一真值源

```csharp
public enum UILayerId { Background, Hud, MobileInput, World, Main, Popup, Top, Guide }

internal static class UILayers {
    public static readonly (UILayerId id, int sortOrder)[] Table = {
        (UILayerId.Background,  1000),  // 全屏不透明背景（选人 Bg）
        (UILayerId.Hud,         2000),  // 血条/小地图
        (UILayerId.MobileInput, 2500),  // 摇杆/三按钮（场景 MobileUiRoot）
        (UILayerId.World,       3000),  // 名牌 + 飘字（EntityDisplayManager）
        (UILayerId.Main,        4000),  // 功能面板：背包/技能/角色/设置
        (UILayerId.Popup,       4500),  // 确认框/详情
        (UILayerId.Top,         5000),  // Loading/Toast/重连/死亡屏
        (UILayerId.Guide,       6000),  // 新手引导蒙版
    };
}
```

- `UIManager` 按 `Table.Length` 建 Canvas —— 消掉 `new Canvas[5]` 与 `for (i < 5)`
- **修正遮挡关系**：名牌原为 4500，**高于** Popup(3000)/Top(4000)，打开背包时名牌会浮在背包上面。新表把 `World` 压到功能面板之下
- **层内有序**：`Open` 时 `SetAsLastSibling()`，后开的一定在上面

**执行注意**：阶段 1 只收敛到表驱动，**保持现有数值不变**；数值重排在阶段 3（见 §8）。

### 2.2 层 ≠ 重建边界

层决定遮挡，Canvas 决定重建根，两者当前被绑成同一个东西。同层面板共享重建根 ⇒ 任一血条跳动连带重建另一个。

**规则：**

| 放什么 | 放哪 | 原因 |
|---|---|---|
| **高频变化的纯展示簇**（血条 / 数字 / buff 条） | **嵌套 Canvas** 内，且簇内所有 Graphic `raycastTarget=false` | 隔离重建；不涉及射线检测，规避 §1.2 的陷阱 |
| 交互元素（按钮 / 输入框） | **不进**嵌套 Canvas | 进去会退出父 Canvas 射线检测 |

嵌套 Canvas 不设 `overrideSorting`，也不需要额外 `GraphicRaycaster`。

---

## 3. 契约与生命周期

### 3.1 就绪信号

```csharp
UniTask WhenReady();      // 一次性、可重复 await、带超时
bool IsReady { get; }
```

`UIManager.Awake` **不再 `async void`**，改为显式 `InitializeAsync()`，由 bootstrap 调用（同时处理 R8：两场景各带一份 UIManager/EventSystem，改为一处创建）。

### 3.2 注册时校验，而非运行期判空

**阶段 1 之前**，所有可选组件都静默降级：

```csharp
// UIPanel.Awake（阶段 1 之前）—— 可选组件都可能为 null
Canvas = GetComponent<Canvas>();
CanvasGroup = GetComponent<CanvasGroup>();
// ApplyVisibilityOff 里：if (Canvas != null) { ... }  ← CanvasSwitch 静默变成"什么都不做"
```

**改为**：注册时校验声明的 `Mode` 与实际组件是否匹配，不匹配**报错**（开发期抛出；发行期禁用该面板并打日志）。同类补齐：声明了 `Mode` 需要 `CanvasGroup` 而预制体没有 → 报错。

**同时保证注册后面板根一定有一个 `CanvasGroup`**（预制体没有则 `AddComponent` 补齐）。这是 §4.1 事件层实现的前提 —— 模态遮挡统一用 `CanvasGroup.interactable`，不能依赖"恰好面板选了 CanvasGroup 模式"。

### 3.3 显式依赖（R1 的正面修法）

**背景**：`RegisterFromPrefab` 声明为 `async UniTask` 但体内**零 `await`**，`Resources.Load` 是同步 API ⇒ `LoadBuiltInPanels()` 在 `Awake` 里**同步跑完**。推论：

- `SelectStageController` 里两个 `while (…) await UniTask.Delay(50)` **从未循环过一次**（是仪式，不是同步机制）
- `Sys3CEntry.Start` 的 `GetPanel<PlayerHudPanel>()` **必然**拿得到面板

换成 `Res.LoadAsync` 后两条推论同时失效。修法：

| 消费方 | 今天 | 改为 |
|---|---|---|
| `Sys3CEntry.Start` 绑 HUD | 假设面板已注册 | `await UIManager.WhenReady()` 后取 |
| `CharacterAttackHandler` | 运行期取（相对安全） | `await OpenAsync<TargetPanel>()` |
| `SelectStageController` | 无超时轮询 | `await WhenReady()` + 超时 |

### 3.4 状态机与幂等

```
Unloaded →(加载成功)→ Closed → Opening → Open → Closing → Closed
              └→(失败)→ Unloaded + 返回 false
```

```csharp
UniTask<bool> OpenAsync<T>() where T : UIPanel;   // 幂等，见下表
UniTask CloseAsync<T>() where T : UIPanel;
bool IsOpen<T>();
```

**`OpenAsync` 的幂等语义（逐状态）**：

| 调用时的状态 | 行为 |
|---|---|
| `Closed` | 启动加载，走完整流程 |
| `Loading` / `Opening` | **复用同一次操作**，不重复加载、不重复播放动画 |
| `Open` | 立即返回 `true`，不做任何事 |
| `Closing` | 等当前关闭完成后重新打开（避免"关到一半又被打开"的中间态） |
| `Unloaded`（上次加载失败） | 重试加载 |

- **删掉 Stack 那套**（`PushAsync` / `PopAsync` / `PopTo`）。理由不是"现在没人用"，而是：MMO 里面板遮挡由**层 + 模态**决定，栈会额外强加"关闭顺序必须与打开顺序相反"这一约束，而玩家点 X 关任意面板时就破了。今天零调用者。
- 加载失败 → 回 `Unloaded` 并返回 `false`，**不是** `Open`
- 加载超时 → `CancellationToken` 取消，避免永久卡在 `Opening`

### 3.5 注册表

```csharp
private static readonly PanelEntry[] Registry = {
    new(typeof(PlayerHudPanel),       "UI/PlayerHudPanel",       preload: true),
    new(typeof(TargetPanel),          "UI/TargetPanel",          preload: false),
    new(typeof(CharacterSelectPanel), "UI/CharacterSelectPanel", preload: false),
    // BagPanel 等阶段 5 收编
};
```

**加载策略只做两种**：`preload: true` 在启动期（有 Loading 出口）批量加载常驻；其余首次 `Open` 时懒加载。

**第一版不做 LRU 自动卸载**：重的是贴图与预制体句柄而非面板实例，UI 图集常驻本就是常规做法，自动卸载换来的是"下次打开卡一下"。切场景或显式 `UnloadAsync<T>()` 时才 `Res.Release(key)`。

---

## 4. 模态与输入闸门

### 4.1 两条防线（解决两类不同问题）

| 防线 | 位置 | 挡住什么 |
|---|---|---|
| **事件层** | 模态面板打开时，同层及更低层其他面板 `CanvasGroup.interactable = false` | "点在面板背后的按钮上"（顺带修掉 Push 不阻断下层） |
| **语义层** | `UIInputGate.IsBlocked` → `InputManager` 短接 | "面板打开期间累积的输入在关闭瞬间释放" |

### 4.2 语义层实现

```csharp
public static class UIInputGate {
    public static int ModalCount { get; internal set; }
    public static bool IsBlocked => ModalCount > 0;
}
```

- UIManager **重算**而非累加：`ModalCount = _openPanels.Count(p => p.IsModal)` —— 累加必然在某个异常路径漏减，重算把整类 bug 消灭
- `InputManager` 在**访问器层**短接（`IsJumpPressed()` / `GetMoveCommand()` / `GetCameraRotationInput()` 提前返回零值）。只在 `Update` 清标志不够 —— `IsJumpPressed()` 直读 `UnityInput.GetButtonDown`，被挡的那一帧照样返回 true
- **锁存必须在被挡住的边沿清掉**：`_attackHeld` / `_attackJustPressed` / `_jumpPressedFromUI` / `_lastReleaseDuration`

### 4.3 移动端专有的一条

`MobileActionButtons.AttackHeld` 由 UI 按钮回调置位。面板盖住按钮时 `OnPointerUp` **收不到** ⇒ 锁存为 true ⇒ 关面板后 `MobileInputBinder.LateUpdate` 重新喂进去 ⇒ **自动连续攻击**。摇杆同理（抬指事件丢失 ⇒ 角色一直跑）。

**修法**：把今天写在 `OnDisable` 里的清理提成 `ResetState()`，由 `MobileInputBinder` 在**进入被挡状态的那个边沿**调用。每个组件管自己的状态，`InputManager` 管自己的锁存。

### 4.4 面板声明

`UIPanel.IsModal` 虚属性，**默认 `true`**，预制体可声明例外。

---

## 5. 安全区与显示适配

**现状三种状态并存**：5 个根 Canvas 全部缩进（`ScreenAdapter`）、移动控件完全不管（`MobileUiRoot` 不在 UIManager 管辖内）、名牌另有自建 Canvas。

**设计：**

- **根 Canvas 撑满全屏，不做安全区**（回到 2026-05-08 设计文档 §4.2 原意）
- 新增 `SafeAreaFitter`，挂**内容根**（子 RectTransform），**不动 Canvas 本身**
- 换算抽成纯函数 `Compute(screenSize, safeArea) → (anchorMin, anchorMax)`，EditMode 可测多组（无刘海 / 左刘海 / 右刘海 / 竖屏 / **零尺寸**）
- `ScreenAdapter` 职责收窄为 CanvasScaler 配置；`ApplySafeArea` 字段**删除**，不留没人用的旋钮
- `MobileUiRoot` 显式挂一个（场景对象，框架管不到，必须点名处理）

**两个实现细节**：① 写入前比较，值不变不写 —— 否则 `OnRectTransformDimensionsChange` 里改自身 anchor 会自激；② `screenSize` 为零时跳过（今天 `safeArea.position / screenSize` 会产出 NaN，只是恰好没触发）。

---

## 6. 面板寻址与元数据

- **删掉 `PanelId` 字符串**，改类型寻址：`GetPanel<T>()` / `OpenAsync<T>()`。消掉那 6 处 `GetPanel<T>("PlayerHudPanel")` 的静默失败（`as T` 返回 null）
- 面板自声明**行为**（沿用现有风格）：`Layer` / `Mode` / `IsModal`
- **编辑器校验器**：扫描所有 `UIPanel` 子类，不在注册表里的直接报错 —— 15+ 面板时最省事的一道闸

---

## 7. 共享视图组件

新建 `UI/Common/`：

| 组件 | 消掉什么 |
|---|---|
| `StatBar`（fill + 文本 + 变化缓动 + **脏值比较**） | HUD 与目标面板逐字重复的血/蓝条逻辑（约 45 行 ×2） |
| `BuffBar`（池化图标 + **集中 tick**） | 两处逐字重复的 `UpdateBuffs`/`GetOrCreateBuffIcon`/`ClearBuffIcons`；N 个 `BuffIcon.Update()` 收敛为一次 |

脏值比较顺带解决 R4：数字没变就不写文本，避免字符串分配与 TMP 重建。

---

## 8. 阶段划分与验收

每阶段结束都能出包、跑既有验收。**顺序上唯一的硬约束：阶段 4 必须整体做** —— `WhenReady()`（阶段 2）是它成立的前提，中间任何提交点都必须可运行。

| 阶段 | 内容 | 验收 |
|---|---|---|
| **1 清创**（零行为变化） | 删死代码（`UI/Animation/` 4 文件 / Stack 三方法 / `_activeOverlays`）；层数硬编码收敛成表驱动，**排序号数值先不动** | 既有测试全绿 + 选人→世界链路跑通 |
| **2 显式契约** | 就绪信号 `WhenReady()`；`Awake` 去 `async void`；bootstrap 统一创建 UIManager/EventSystem；注册校验（Mode vs 组件）+ CanvasGroup 补齐；类型寻址 + 注册表；R7 输入栈收口（删包 + 删 `GameSys.asmdef` 的 `Unity.InputSystem` 引用 + `activeInputHandler: 0`） | **负向验收**：故意改错一个预制体地址 → 必须有明确报错且**不挂死** |
| **3 模态 + 重建边界 + 安全区 + 层重排** | 输入闸门（含锁存清理）；展示簇嵌套 Canvas + `raycastTarget` 归位；`SafeAreaFitter`；层序号重排（动 `DemoDay.unity` 与名牌系统） | 模拟器四向刘海截图；**按住攻击→开面板→关面板不得自动攻击** |
| **4 异步化 + Addressables** | settings/分组/标记；状态机 + 幂等 + 超时 + 卸载；预载组分离 | `Res.GetRefCount(address)` 归零断言 + WebGL 出包 |
| **5 收编与消重** | Bag UI 三件套收编进框架（第 4 个面板样板）；`StatBar`/`BuffBar`；测试改 `InternalsVisibleTo` 替代反射 | 场景 A/B 数值一致；面板测试不再依赖私有字段名 |

### 8.1 阶段 3 的执行顺序建议

层序号重排是五步里唯一"可见行为会变"的一步，放在阶段 3 最后做 —— 前面的闸门与安全区稳定后再动它。

### 8.2 实现计划的粒度

**每个阶段一份独立的实现计划**，不要合并成一份。理由：五阶段各自的验收条件不同（阶段 1 是"零行为变化"、阶段 2 是负向验收、阶段 3 是截图与手测、阶段 4 是引用计数断言、阶段 5 是 A/B 数值一致），合并后验收标准会互相污染；且阶段之间的提交点必须是可运行状态，合并计划容易诱导跨阶段的大提交。

---

## 9. 明确不做

- UI Toolkit 迁移（UGUI + 移动端 + 已有投入，换栈等于重开一局）
- MVVM / 数据绑定引擎（沿用 2026-05-08 的 YAGNI 决定：反射绑定在 IL2CPP 下成本高、调试难）
- 面板 LRU 自动卸载
- 新手引导系统、多语言
- **留缝不实现**：Android 返回键的 `CloseTopmost()`（移动端迟早要，今天无需求）

---

## 10. 风险

| 风险 | 缓解 |
|---|---|
| 阶段 4 的异步化引入竞态 | 阶段 2 先落地 `WhenReady()`，阶段 4 才有前提；负向验收覆盖加载失败路径 |
| 阶段 3 层重排打破既有观感 | 重排放在阶段 3 最后；四向刘海截图 + 既有链路复跑 |
| 嵌套 Canvas 的射线陷阱（§1.2） | 设计上只把全 `raycastTarget=false` 的展示簇放进去；交互元素一律不进 |
| R7 删包后被其他包重新拉回 | 删后核对 `Packages/packages-lock.json`，确认无包声明对它依赖 |
| Addressables 在 WebGL 上的 catalog 行为 | 阶段 4 的验收含 WebGL 出包；本地组不引远端 |

---

## 11. 关联文档

- `docs/superpowers/specs/2026-10-14-ui-framework-refactor-design.md`（本文）
- `docs/superpowers/specs/2026-05-08-ui-framework-design.md`（框架原始设计，§4.2 安全区原意、§13 YAGNI 边界）
- `docs/superpowers/specs/2026-05-08-addressable-resource-system-design.md`（`Res`/`ResourceManager`）
- `docs/superpowers/specs/2026-05-22-player-hud-and-target-panel-design.md`
- `docs/superpowers/specs/2026-10-07-webgl-interview-demo-design.md`（当前里程碑与验收清单）
- `docs/superpowers/guides/p6-human-acceptance.md`（人工验收基线）
