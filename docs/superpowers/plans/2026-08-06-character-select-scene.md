# 独立选人场景重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新建独立选人场景（营地背景 + 单体预览切换），确认后加载 DemoDay；DemoDay 移除预览残留，玩家由 PlayerSpawner 按保存选择生成。

**Architecture:** 选人场景（场景 0，含 UIManager 复制品 + 两个剥离预览体 + SelectStageController）→ 确认后面板关闭 → `SceneManager.LoadScene("DemoDay")` → PlayerSpawner.Start 从 `CharacterSelectionService.Load()` 生成玩家（复用现有逻辑，PlayerSpawnedEvent 照发）。面板加 `OnSelectionChanged`/`OnClosed` 事件与 `_closing` 重入保护；StageController 轮询等面板注册、切换预览、守卫加载。

**Tech Stack:** Unity 2022.3 (built-in pipeline), UGUI, UniTask, SceneManager, Unity MCP

---

### Task 1: CharacterSelectPanel 事件 + 重入保护

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs`

- [ ] **Step 1: 修改代码**

在 `using UnityEngine.UI;` 前加 `using System;`。类内新增/修改：

```csharp
        /// <summary>选人切换事件（场景侧 SelectStageController 用于切换预览体）</summary>
        public event Action<CharacterGender> OnSelectionChanged;
        /// <summary>面板完全关闭后触发（用于触发游戏场景加载）</summary>
        public event Action OnClosed;

        private CharacterGender _selected;
        private bool _closing;   // 重入保护：防双击确认导致重复关闭/重复加载

        private void Select(CharacterGender gender)
        {
            _selected = gender;
            RefreshHighlight();
            OnSelectionChanged?.Invoke(gender);
        }

        private void Confirm()
        {
            if (_closing) return;
            _closing = true;
            CharacterSelectionService.Save(_selected);
            EventBus.Emit(new CharacterSelectedEvent(_selected));
            CloseSelf();
        }

        private async void CloseSelf()
        {
            if (UIManager.Instance != null)
                await UIManager.Instance.CloseAsync(PanelId);
            OnClosed?.Invoke();
        }
```

（`Select` 的旧实现加一行 `OnSelectionChanged?.Invoke(gender);`；`Confirm` 加 `_closing` 守卫；`CloseSelf` 末尾加 `OnClosed?.Invoke();`。其余逻辑不动。）

- [ ] **Step 2: 编译验证**

Run: `mcp__ai-game-developer__assets-refresh` + `console-get-logs`（无新增 Error）

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs
git commit -m "feat: panel selection-changed/closed events + confirm re-entry guard"
```

---

### Task 2: SelectStageController

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/SelectStageController.cs`

- [ ] **Step 1: 创建文件**

```csharp
using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.Sys3C.Character;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 选人舞台控制器：切换预览体展示所选性别；面板关闭后加载游戏场景。
    /// 全序列化引用，无 GetComponent。
    /// </summary>
    public class SelectStageController : MonoBehaviour
    {
        [SerializeField] private GameObject _malePreview;
        [SerializeField] private GameObject _femalePreview;

        private bool _loading;   // 双保险：防重复加载场景
        private CharacterSelectPanel _panel;

        private async void Start()
        {
            // UIManager.Awake 异步注册面板（await Resources.Load），轮询等待
            while (UIManager.Instance == null)
                await UniTask.Delay(50);
            while (UIManager.Instance.GetPanel<CharacterSelectPanel>("CharacterSelectPanel") == null)
                await UniTask.Delay(50);

            _panel = UIManager.Instance.GetPanel<CharacterSelectPanel>("CharacterSelectPanel");
            ApplyPreview(CharacterSelectionService.Load());
            _panel.OnSelectionChanged += ApplyPreview;
            _panel.OnClosed += LoadGameScene;
        }

        private void ApplyPreview(CharacterGender gender)
        {
            if (_malePreview != null) _malePreview.SetActive(gender == CharacterGender.Male);
            if (_femalePreview != null) _femalePreview.SetActive(gender == CharacterGender.Female);
        }

        private void LoadGameScene()
        {
            if (_loading) return;
            _loading = true;
            SceneManager.LoadScene("DemoDay");
        }

        private void OnDestroy()
        {
            if (_panel != null)
            {
                _panel.OnSelectionChanged -= ApplyPreview;
                _panel.OnClosed -= LoadGameScene;
            }
        }
    }
}
```

- [ ] **Step 2: 编译验证**

Run: `mcp__ai-game-developer__assets-refresh` + `console-get-logs`（无新增 Error）

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/SelectStageController.cs Assets/Scripts/Hotfix/GameSystems/UI/SelectStageController.cs.meta
git commit -m "feat: select stage controller — preview switching + game scene load"
```

---

### Task 3: PlayerSpawner Start 兜底生成

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs`

- [ ] **Step 1: 修改代码**

新增方法（类内任意位置）：

```csharp
        private void Start()
        {
            // 场景加载后从保存的选择生成（选人场景发的事件本场景收不到）
            if (_currentPlayer == null)
                OnCharacterSelected(new CharacterSelectedEvent(CharacterSelectionService.Load()));
        }
```

（`CharacterGender`/`CharacterSelectionService` 与本类同命名空间无需 using；`CharacterSelectedEvent` 已导入 `Hotfix.GameSystems.Sys3C.Core.Events`。）

- [ ] **Step 2: 编译验证**

Run: `mcp__ai-game-developer__assets-refresh` + `console-get-logs`

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs
git commit -m "feat: player spawner Start fallback — spawn from saved selection on scene load"
```

---

### Task 4: CharacterSelect 场景搭建 + Build Settings

**Files:**
- Create: `Assets/Scenes/CharacterSelect.unity`
- Modify: `ProjectSettings/EditorBuildSettings.asset`（CharacterSelect=场景 0，DemoDay=场景 1）

- [ ] **Step 1: 创建场景并搭建营地背景**

用 `mcp__ai-game-developer__script-execute`（UnityEditor 完整类模式）执行：
1. `EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)` 创建新场景
2. `EditorSceneManager.OpenScene("Assets/SimpleLowPolyNature/Scenes/DemoDay.unity", OpenSceneMode.Additive)` 只读参考
3. 从 DemoDay 复制环境子集到新场景（`Instantiate` 副本 + 改名 + 移到新场景根）：
   - `LowpolyTerrain`（地面）
   - `Tent`（帐篷，舞台背景）
   - `CampFire`（篝火，含 Fire 粒子）
   - `Trees` 下选 3-5 棵（围绕舞台后方）
   - `Plants` 下选 4-6 棵（点缀）
   - `Rocks` 下选 2-3 块（可选）
   - 副本统一摆放到新场景舞台区域（以预览位为中心），朝向相机
4. 添加灯光：`Directional Light`（active，旋转约 (50,-30,0)，强度 1）+ 篝火处 `Point Light`（暖色，范围 8，强度 2）
5. 添加 `Main Camera`（普通相机，不挂 ThirdPersonCameraController）：面向舞台中央，FOV 40，位置以能看到舞台全景为准（预览位在画面中央，帐篷在后方背景）
6. 预览位：`assets-prefab-instantiate`（`Assets/Prefabs/Character/Preview_Male.prefab` 和 `Preview_Female.prefab`）到**同一位置**（舞台中央，面向相机）；各挂 `PreviewRotator`（script AddComponent）；初始：`Preview_Male` 默认 active、`Preview_Female` 默认 inactive（`SelectStageController` 启动时会按 Load() 修正）
7. UIManager：创建空对象 + `Hotfix.GameSystems.UI.UIManager` 组件（Canvas 层由 Awake 运行时创建，无需场景子对象）
8. `SelectStageController`：创建空对象 + 组件，序列化引用 `_malePreview` → Preview_Male 实例、`_femalePreview` → Preview_Female 实例

- [ ] **Step 2: 保存场景**

`EditorSceneManager.SaveScene(activeScene, "Assets/Scenes/CharacterSelect.unity")`（文件夹不存在则先 `AssetDatabase.CreateFolder("Assets", "Scenes")`）；`mcp__ai-game-developer__assets-refresh`

- [ ] **Step 3: Build Settings**

script-execute：

```csharp
using UnityEditor;
using UnityEngine;

public class SetBuildScenes
{
    public static void Main()
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/CharacterSelect.unity", true),
            new EditorBuildSettingsScene("Assets/SimpleLowPolyNature/Scenes/DemoDay.unity", true)
        };
        EditorBuildSettings.scenes = scenes;
        Debug.Log("[SetBuildScenes] done");
    }
}
```

验证：Read `ProjectSettings/EditorBuildSettings.asset` 确认两场景顺序。

- [ ] **Step 4: 提交**

```bash
git add Assets/Scenes/CharacterSelect.unity Assets/Scenes/CharacterSelect.unity.meta ProjectSettings/EditorBuildSettings.asset
git commit -m "feat: character select scene (camp backdrop, preview slot, stage controller wiring) + build settings"
```

---

### Task 5: DemoDay 清理

**Files:**
- Modify: `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`

- [ ] **Step 1: 删除预览残留**

script-execute 或 MCP：删除 DemoDay 场景中的 `Preview_Male` / `Preview_Female` 实例（含根上挂的 `PreviewRotator`）

- [ ] **Step 2: 相机复位**

Main Camera 从选人取景（当前约 (10, 1.675, -23.038) rot (7.6°,0,0)）复位为普通游戏视角：出生点（PlayerSpawnPoint 约 (10, 0.16, -15.52)）后方约 8m、高度约 2.5m、俯角 ~8° 看向出生点。保存。

- [ ] **Step 3: 验证 + 提交**

- Read DemoDay.unity：无 Preview_* 字符串残留、无 PreviewRotator m_Script 引用
- `mcp__ai-game-developer__scene-save`

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "fix: remove preview models from DemoDay, reset camera to game view"
```

---

### Task 6: 端到端验证

**Files:**
- None（验证任务）

- [ ] **Step 1: EditMode 测试回归**

Run: `mcp__ai-game-developer__tests-run`（EditMode）—— 66 个全绿

- [ ] **Step 2: 正式流程（Play CharacterSelect 场景）**

1. 编辑器打开 `Assets/Scenes/CharacterSelect.unity`，`editor-application-set-state` PlayMode
2. 验证：面板出现（UIManager 来自本场景，DontDestroyOnLoad）；营地背景渲染；默认预览 = `Load()` 性别（无存档 = 男预览 active、女预览 inactive）
3. `screenshot-game-view` 截图存档（production/verify/select_scene_*.png）
4. script-execute 模拟点女卡片（反射读 `_femaleButton` → `onClick.Invoke()`）→ 验证：FemaleHighlight active、**女预览 active / 男预览 inactive**（预览切换生效）
5. 点确认（`_confirmButton.onClick.Invoke()`）→ 等场景加载 → 验证：当前场景 = DemoDay
6. **组件全集抽查**：FemalePlayer(Clone) 根 11 组件（含 MeleeWeapon/CharacterAttackHandler/HitFlashVFX）+ weapon_r 7 VFX + RingVisual + `_characterSkills.Length==4`
7. 相机 Target == 玩家；HitStopManager._playerAnimator 非空；PlayerHudPanel 存在
8. **无残留**：场景中无 Preview_* 对象
9. 受击闪红：EventBus.Emit(MonsterTakeDamageEvent) 走真实链路不报错
10. 退出 PlayMode

- [ ] **Step 3: 双击保护**

Play CharacterSelect → 连续两次 `_confirmButton.onClick.Invoke()`（同帧）→ 验证：`SceneManager.GetActiveScene().name == "DemoDay"` 且只加载了一次（无报错、无双重加载日志）；玩家实例唯一

- [ ] **Step 4: 持久化**

再次 Play CharacterSelect → 默认预览 = 女（上次选择）+ FemaleHighlight 亮 → 退出

- [ ] **Step 5: dev 路径（直接 Play DemoDay）**

打开 DemoDay → PlayMode → 面板出现 → 点确认 → 验证：玩家生成（Load() 性别=女）、面板关闭、**场景仍是 DemoDay**（未切换）、无报错 → 退出

- [ ] **Step 6: 会话日志 + 收尾**

追加验证结果到 `production/session-logs/session-log.md`；确认工作树无本特性意外改动

---

## 自检记录（执行前）

- **Spec §3** 场景结构 → Task 4 ✓；**§4.1** 面板事件+重入保护 → Task 1 ✓；**§4.2** StageController → Task 2 ✓；**§4.3** PlayerSpawner Start 兜底 → Task 3 ✓；**§5** DemoDay 清理 → Task 5 ✓；**§6** Build Settings → Task 4 Step 3 ✓；**§7/§8** 数据流/错误处理 → 代码内（_closing/_loading/轮询/Start 兜底）✓；**§9** 测试 → Task 6 ✓
- **占位符扫描**：三个代码任务含完整代码；场景任务含精确工具与步骤 ✓
- **类型一致性**：`OnSelectionChanged`/`OnClosed` 事件签名在 Task 1 定义、Task 2 订阅一致；`CharacterSelectPanel` PanelId 不变；`SceneManager.LoadScene("DemoDay")` 与 Build Settings 场景名一致 ✓
- **历史教训应用**：① prefab/场景文件操作后以 git 为准复核（防 4066722 式破坏）；② 验证必须含组件全集抽查（上轮自动化验证盲区）；③ 场景任务避开 prefab 编辑态（本计划无 prefab 修改，仅场景对象操作）
