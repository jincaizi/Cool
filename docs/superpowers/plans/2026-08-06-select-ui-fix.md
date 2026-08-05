# 选人界面修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复选人界面点击无响应（场景缺 EventSystem）与布局不合理（卡片过大），零代码逻辑改动。

**Architecture:** 选人场景照搬 DemoDay 的 EventSystem 结构（EventSystem 组件 + StandaloneInputModule）；面板 prefab 按设计分辨率（1920×1080）调整卡片/高亮/确认按钮尺寸与位置。验证改用真实射线点击（EventSystem 输入链路），不再用 onClick.Invoke 绕过。

**Tech Stack:** Unity 2022.3 (built-in pipeline), UGUI, Unity MCP

---

### Task 1: CharacterSelect 场景添加 EventSystem

**Files:**
- Modify: `Assets/Scenes/CharacterSelect.unity`

- [ ] **Step 1: 添加 EventSystem 对象（script-execute，UnityEditor 完整类模式）**

`EditorSceneManager.OpenScene("Assets/Scenes/CharacterSelect.unity", OpenSceneMode.Single)` → 创建空对象 `EventSystem`（场景根，layer 0）→ `AddComponent<UnityEngine.EventSystems.EventSystem>()` → `AddComponent<UnityEngine.EventSystems.StandaloneInputModule>()` → 保存场景。

（与 DemoDay 的 EventSystem 结构一致：GO + Transform + EventSystem + StandaloneInputModule。输入模块 GUID：EventSystem=`76c392e42b5098c458856cdf6ecaaaa1`、StandaloneInputModule=`4f231c4fb786f3946a6b90b886c48677`，均为 Unity 内置组件，无需资产引用。）

- [ ] **Step 2: 验证 + 提交**

- Read `Assets/Scenes/CharacterSelect.unity`：`m_Name: EventSystem` 1 处；`76c392e4...` 和 `4f231c4f...` 各 1 处
- `git status`：只应有 CharacterSelect.unity 改动；**任何 prefab/代码变脏 → 停下报告**
- `console-get-logs`：无新增 Error

```bash
git add Assets/Scenes/CharacterSelect.unity
git commit -m "fix: add EventSystem to select scene (UI clicks had no input processing)"
```

---

### Task 2: CharacterSelectPanel.prefab 布局调整

**Files:**
- Modify: `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`

- [ ] **Step 1: 布局调整（script-execute，PrefabUtility.LoadPrefabContents 方案，幂等）**

对 `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`：
1. MaleCard RectTransform：sizeDelta (360,520) → **(200,280)**，anchoredPosition (-460,40) → **(-130,-80)**
2. FemaleCard：sizeDelta → **(200,280)**，anchoredPosition (460,40) → **(130,-80)**
3. MaleHighlight RectTransform：sizeDelta (392,552) → **(220,300)**，anchoredPosition (-460,40) → **(-130,-80)**（与卡片同锚，套在外面）
4. FemaleHighlight：sizeDelta → **(220,300)**，anchoredPosition (460,40) → **(130,-80)**
5. ConfirmButton：sizeDelta (280,90) → **(220,70)**，anchoredPosition (0,-430) → **(0,-330)**
6. 文本字号：「男」「女」Label 的 fontSize 48 → **40**；「确认」Label fontSize 48 → **36**
7. `PrefabUtility.SaveAsPrefabAsset` + `UnloadPrefabContents`

（用 SerializedObject 按属性路径写 m_SizeDelta / m_AnchoredPosition / m_FontSize，或直接反射 RectTransform/Text 组件属性；坐标是设计分辨率单位，锚点保持现状——卡片锚点居中（anchorMin/Max=0.5）则 anchoredPosition 是相对中心的偏移；若锚点不是居中，按比例换算保持视觉位置。改完用 Read YAML 复核数值。）

- [ ] **Step 2: 验证 + 提交**

- Read prefab YAML：6 项数值全部为新值；无其他字段变化（git diff 只看这些行）
- `mcp__ai-game-developer__assets-refresh`；`console-get-logs` 无新增 Error
- `git status`：只应有面板 prefab 改动

```bash
git add Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab
git commit -m "fix: compact select panel layout — smaller cards bottom-centered (design-res units)"
```

---

### Task 3: 端到端验证（真实射线点击）

**Files:**
- None（验证任务）

- [ ] **Step 1: EditMode 回归**

Run: `mcp__ai-game-developer__tests-run`（EditMode）→ 66/66 全绿

- [ ] **Step 2: 正式流程 PlayMode（真实输入链路）**

1. 打开 `Assets/Scenes/CharacterSelect.unity` → PlayMode
2. 验证 EventSystem 存在且 active（`EventSystem.current != null`）
3. **真实射线点击**（script-execute，不 bypass UI）：
   - 构造 `PointerEventData(EventSystem.current)`，position = MaleCard 屏幕坐标（`RectTransformUtility.WorldToScreenPoint` 或 UI 坐标换算）
   - `EventSystem.current.RaycastAll(pointerData, results)` → 确认命中的第一个是 MaleCard（GraphicRaycaster 链路）
   - `ExecuteEvents.Execute(maleCardGo, pointerData, ExecuteEvents.pointerDownHandler)` + `pointerUpHandler` → 走真实按钮按下/抬起
   - 验证：MaleHighlight 亮 / FemaleHighlight 灭 / 预览切换为男（Preview_Male active）
4. 同法点 FemaleCard → FemaleHighlight 亮 + Preview_Female active；截图（select_ui_1_female_clicked.png）
5. 点 ConfirmButton（同法）→ 面板关闭 → DemoDay 加载 → 玩家生成（性别=女）
6. `screenshot-game-view` → select_ui_2_playing.png
7. 退出 PlayMode

- [ ] **Step 3: 布局视觉确认**

- 截图程序化校验：卡片/确认按钮在画面底部居中、角色预览无遮挡（像素采样或坐标对比）
- 记录到会话日志（production/session-logs/session-log.md）：EventSystem 修复、布局调整、真实点击验证矩阵

- [ ] **Step 4: 收尾**

`git status` 复核：本特性文件零意外改动；截图不入库

---

## 自检记录（执行前）

- **Spec §1** EventSystem → Task 1 ✓；**§2** 布局数值 → Task 2 ✓；**§3** 验证（真实点击）→ Task 3 ✓
- **占位符扫描**：无；数值精确到设计分辨率单位
- **类型一致性**：GUID 引用与 DemoDay 一致；布局数值与 spec 表格一致
- **教训应用**：场景/prefab 操作后 git 复核；验证用真实输入链路（本次事故根因就是验证绕过了输入系统）

## 执行记录（2026-08-06，执行后追加）

- 3 个任务完成（提交 5648f8e → 2f08d43，落 master）：EventSystem 添加（+69 行，与 DemoDay 逐字段一致）+ 布局调整（13 值 + 3 行 TMP 伴生字段）
- 端到端验证**全程真实射线链路**：EventSystem.current + RaycastAll 首命中断言（MaleCard/FemaleCard/ConfirmButton 各命中）+ pointerDown/Up/Click → Button.onClick → Select → OnSelectionChanged → 预览切换全链通；确认 → 6 秒内 DemoDay 加载 → 玩家生成
- 布局视觉确认（PIL 数值校验）：三控件底部水平居中（|Δx| ≤ 88px）、顶部预览区无遮挡、卡片面积约为原 30%
- 已知编辑器副作用（非本特性）：ZhunYuan SDF.asset 在 PlayMode 后产生 TMP 动态字形（未提交，可 git checkout）
