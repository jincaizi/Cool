# 男女角色选择系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 游戏启动先选人（男女预览，PlayerPrefs 记住选择），确认后按性别生成玩家 prefab，完整复用现有 3C 系统（3C 代码零改动）。

**Architecture:** 双 prefab（MalePlayer/FemalePlayer 镜像接线）+ 运行时生成（`PlayerSpawner` 订阅 `CharacterSelectedEvent` 事件）。相机绑定由 `Sys3CEntry.Start()` 自动完成（FindObjectOfType + Target + SnapToTarget），生成器不持有相机引用。选人面板（`CharacterSelectPanel`）注册进 `UIManager.LoadBuiltInPanels`，确认后 Save + Emit + 关闭。预览体为场景烘焙的剥离脚本模型（无运行时 GetComponent）。

**Tech Stack:** Unity 2022.3 (built-in pipeline), UGUI, DOTween, UniTask, NUnit EditMode tests, Unity MCP

---

### Task 1: CharacterGender + CharacterSelectionService（TDD）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterGender.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterSelectionService.cs`
- Test: `Assets/Scripts/Tests/Editor/CharacterSelectionServiceTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using Hotfix.GameSystems.Sys3C.Character;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class CharacterSelectionServiceTests
    {
        private const string PrefsKey = "character.gender";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        [Test]
        public void test_character_selection_save_load_roundtrip_male_returns_male()
        {
            CharacterSelectionService.Save(CharacterGender.Male);
            Assert.AreEqual(CharacterGender.Male, CharacterSelectionService.Load());
        }

        [Test]
        public void test_character_selection_save_load_roundtrip_female_returns_female()
        {
            CharacterSelectionService.Save(CharacterGender.Female);
            Assert.AreEqual(CharacterGender.Female, CharacterSelectionService.Load());
        }

        [Test]
        public void test_character_selection_load_no_save_returns_male_default()
        {
            Assert.AreEqual(CharacterGender.Male, CharacterSelectionService.Load());
        }

        [Test]
        public void test_character_selection_load_corrupted_value_returns_male_default()
        {
            PlayerPrefs.SetInt(PrefsKey, 999);
            Assert.AreEqual(CharacterGender.Male, CharacterSelectionService.Load());
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: Unity MCP `tests-run`（testMode=EditMode，testClass=CharacterSelectionServiceTests）
Expected: FAIL —— 编译错误 `CharacterSelectionService`/`CharacterGender` 不存在

- [ ] **Step 3: 最小实现**

`CharacterGender.cs`:

```csharp
namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色性别（Male=0 为默认值，保证非法存档回退男性）
    /// </summary>
    public enum CharacterGender
    {
        Male = 0,
        Female = 1
    }
}
```

`CharacterSelectionService.cs`:

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色选择持久化：读写 PlayerPrefs（纯客户端，网络化时再扩展协议）
    /// </summary>
    public static class CharacterSelectionService
    {
        private const string PrefsKey = "character.gender";

        public static void Save(CharacterGender gender)
        {
            PlayerPrefs.SetInt(PrefsKey, (int)gender);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 无存档 / 非法值 → 默认 Male
        /// </summary>
        public static CharacterGender Load()
        {
            if (!PlayerPrefs.HasKey(PrefsKey)) return CharacterGender.Male;
            int raw = PlayerPrefs.GetInt(PrefsKey);
            return raw == (int)CharacterGender.Female ? CharacterGender.Female : CharacterGender.Male;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: Unity MCP `tests-run`（testMode=EditMode，testClass=CharacterSelectionServiceTests）
Expected: 4/4 PASS

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Tests/Editor/CharacterSelectionServiceTests.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterGender.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterSelectionService.cs
git commit -m "feat: character gender enum + selection persistence service (PlayerPrefs)"
```

---

### Task 2: CharacterSelectedEvent + PlayerSpawner

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/CharacterSelectedEvent.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs`

- [ ] **Step 1: 创建事件类**

```csharp
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 角色选择确认事件（选人面板 → 玩家生成器）
    /// </summary>
    public struct CharacterSelectedEvent : IEvent
    {
        public CharacterGender Gender;

        public CharacterSelectedEvent(CharacterGender gender)
        {
            Gender = gender;
        }
    }
}
```

- [ ] **Step 2: 创建 PlayerSpawner**

```csharp
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 玩家生成器：订阅角色选择事件，按性别实例化对应 prefab。
    /// 相机/HUD 绑定由 Sys3CEntry.Start 自动完成，本组件不持有相机引用。
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject _malePlayerPrefab;
        [SerializeField] private GameObject _femalePlayerPrefab;
        [SerializeField] private Transform _spawnPoint;

        private GameObject _currentPlayer;

        private void OnEnable()
        {
            EventBus.Subscribe<CharacterSelectedEvent>(OnCharacterSelected);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<CharacterSelectedEvent>(OnCharacterSelected);
        }

        private void OnCharacterSelected(CharacterSelectedEvent e)
        {
            var prefab = e.Gender == CharacterGender.Female ? _femalePlayerPrefab : _malePlayerPrefab;
            if (prefab == null)
            {
                Debug.LogError("[PlayerSpawner] Player prefab missing for gender " + e.Gender);
                return;
            }

            // 幂等：重复确认先销毁旧玩家
            if (_currentPlayer != null)
                Destroy(_currentPlayer);

            var spawnPos = _spawnPoint != null ? _spawnPoint.position : Vector3.zero;
            _currentPlayer = Instantiate(prefab, spawnPos, Quaternion.identity);
        }
    }
}
```

- [ ] **Step 3: 编译验证**

Run: Unity MCP `assets-refresh`
Expected: 无编译错误（tests-run 会顺带验证；或 console-get-logs 无 Error）

- [ ] **Step 4: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/CharacterSelectedEvent.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs
git commit -m "feat: character selected event + player spawner (event-driven, idempotent)"
```

---

### Task 3: CharacterSelectPanel + PreviewRotator（代码）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/PreviewRotator.cs`

- [ ] **Step 1: 创建 PreviewRotator**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 选人预览体慢速自转（纯 Transform 操作，无 GetComponent）
    /// </summary>
    public class PreviewRotator : MonoBehaviour
    {
        [SerializeField] private float _speed = 30f;

        private void Update()
        {
            transform.Rotate(0f, _speed * Time.deltaTime, 0f);
        }
    }
}
```

- [ ] **Step 2: 创建 CharacterSelectPanel**

```csharp
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 角色选择面板：男女选择（上次选择高亮，默认男），确认后持久化并广播事件。
    /// 预览体为场景烘焙的剥离模型（本面板不持有预览引用，无 GetComponent）。
    /// </summary>
    public class CharacterSelectPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Popup;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "CharacterSelectPanel";

        [SerializeField] private Button _maleButton;
        [SerializeField] private Button _femaleButton;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private GameObject _maleHighlight;
        [SerializeField] private GameObject _femaleHighlight;

        private CharacterGender _selected;

        protected override void Awake()
        {
            base.Awake();
            if (_maleButton != null) _maleButton.onClick.AddListener(() => Select(CharacterGender.Male));
            if (_femaleButton != null) _femaleButton.onClick.AddListener(() => Select(CharacterGender.Female));
            if (_confirmButton != null) _confirmButton.onClick.AddListener(Confirm);
        }

        internal override void OnShow()
        {
            base.OnShow();
            _selected = CharacterSelectionService.Load();
            RefreshHighlight();
        }

        private void Select(CharacterGender gender)
        {
            _selected = gender;
            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            if (_maleHighlight != null) _maleHighlight.SetActive(_selected == CharacterGender.Male);
            if (_femaleHighlight != null) _femaleHighlight.SetActive(_selected == CharacterGender.Female);
        }

        private void Confirm()
        {
            CharacterSelectionService.Save(_selected);
            EventBus.Emit(new CharacterSelectedEvent(_selected));
            CloseSelf();
        }

        private async void CloseSelf()
        {
            if (UIManager.Instance != null)
                await UIManager.Instance.CloseAsync(PanelId);
        }
    }
}
```

- [ ] **Step 3: 编译验证**

Run: Unity MCP `assets-refresh`
Expected: 无编译错误

- [ ] **Step 4: 提交**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/PreviewRotator.cs
git commit -m "feat: character select panel + preview rotator (UI code)"
```

---

### Task 4: 男玩家 prefab 接线基准（场景实例 vs prefab diff）

**Files:**
- Verify: `Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`（只读核对）
- Verify: DemoDay 场景 `MaleCharacterPBR` 实例

- [ ] **Step 1: 读取场景实例组件清单**

Run: Unity MCP `gameobject-find`（DemoDay 场景 `MaleCharacterPBR`，includeComponents=true，includeData=true）
Expected: 得到全部组件 + 各组件 instanceID（区分 prefab 资产侧 vs 场景侧）

- [ ] **Step 2: 读取男 prefab 资产组件清单**

Run: Unity MCP `assets-get-data`（`Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`）
Expected: 得到 prefab 内置组件 + 序列化字段（含 MonoBehaviour 的 m_Script 引用）

- [ ] **Step 3: 对比 diff**

对比 Step 1/Step 2 组件集合。**场景侧独有组件**（实例有而 prefab 没有的）需要：
- Run: `assets-prefab-open`（男 prefab）
- Run: `gameobject-component-add`（补场景侧组件）→ `gameobject-component-modify`（从场景实例复制字段值）
- Run: `assets-prefab-close`（save=true）

Expected: 合并后 prefab 包含场景实例的全部组件（生成出的男玩家与现状行为一致）

- [ ] **Step 4: 记录 diff 结论并提交**

```bash
git add Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab
git commit -m "fix: merge scene-side components into male player prefab (spawn parity baseline)"
```

（若 Step 3 无差异，跳过提交并在会话日志记录「无场景侧组件，无需合并」）

---

### Task 5: FemalePlayer.prefab 镜像接线

**Files:**
- Create: `Assets/Prefabs/Character/FemalePlayer.prefab`（从 `Assets/RpgDuo/Prefab/FemaleCharacterPBR.prefab` 派生）

- [ ] **Step 1: 复制女模 prefab 到项目目录**

Run: Unity MCP `assets-copy`（`Assets/RpgDuo/Prefab/FemaleCharacterPBR.prefab` → `Assets/Prefabs/Character/FemalePlayer.prefab`）
Expected: 新 prefab 创建成功

- [ ] **Step 2: 打开 prefab 编辑**

Run: `assets-prefab-open`（`Assets/Prefabs/Character/FemalePlayer.prefab`）

- [ ] **Step 3: 添加运行时组件**

Run: `gameobject-component-add`（根节点，与男 prefab 相同组件集）：
- `Hotfix.GameSystems.Sys3C.Sys3CEntry`
- `UnityEngine.CharacterController`
- `Hotfix.GameSystems.Sys3C.MeleeWeapon`
- `Hotfix.GameSystems.Sys3C.CharacterAttackHandler`
- `Hotfix.GameSystems.VFX.HitFlashVFX`
- `Hotfix.GameSystems.VFX.FrostAuraVFX`
- `Hotfix.GameSystems.VFX.IceDecalVFX`
- `Hotfix.GameSystems.VFX.IceBurstVFX`
- `Hotfix.GameSystems.VFX.SkillFreezeEffector`

Expected: 全部添加成功（CharacterAttackHandler/InputManager 若 Sys3CEntry 自动补挂，以男 prefab 为准保持同构）

- [ ] **Step 4: 复制组件序列化字段**

对上述每个组件：
- Run: `gameobject-component-get`（男 prefab 对应组件，deepSerialization=true）→ 记录字段值
- Run: `gameobject-component-modify`（女 prefab 对应组件，写入相同值）

**必须一致的字段**（从男 prefab 读取）：
- `Sys3CEntry`: `_maxHP`、`GroundLayer`、`CharacterController`/`Animator` 引用（指向女 prefab 自身）、`_characterSkills`（**共享引用**同一组 SkillData 资产，如 `Assets/PreRes/SkillsCfg/Spin_SkillR.asset`，不复制资产）
- `MeleeWeapon`: `_config`（共享引用同一 `WeaponConfig` 资产）
- `HitFlashVFX` / 冰系 VFX：全部序列化字段
- `CharacterController`: center/radius/height/slopeLimit 等与男一致

Expected: 无未赋值字段（可对照男 prefab 逐一核对）

- [ ] **Step 5: Animator 控制器指定**

Run: `gameobject-component-modify`（Animator 组件的 `runtimeAnimatorController` → `Assets/RpgDuo/Animator/Character3C.controller`）
Expected: 与男 prefab 一致（若 stock 已是该控制器则跳过）

- [ ] **Step 6: 部件可见性（女外观）**

- Run: `gameobject-modify`：启用女部件（`Head02_Female` + stock 女 prefab 默认活动的 Cloak/Body 组合），禁用男部件（`Head01_Male` 等）
- 以 stock `FemaleCharacterPBR.prefab` 的**默认活动状态**为准（先读取 stock 各部件 activeSelf 再照搬）
Expected: 预览可见女角色外观

- [ ] **Step 7: RingVisual + 男 prefab 特有子物体**

若男 prefab/场景实例有 `RingVisual` 等行为相关子物体：从男 prefab 复制结构（`assets-copy` 或手动创建）并挂到女 prefab 根节点（位置/旋转对齐骨骼根）
Expected: 与男玩家结构同构

- [ ] **Step 8: 保存并提交**

Run: `assets-prefab-close`（save=true）

```bash
git add Assets/Prefabs/Character/FemalePlayer.prefab
git commit -m "feat: female player prefab — full 3C wiring mirror of male player"
```

---

### Task 6: 预览 prefab ×2（剥离脚本）+ 场景摆放

**Files:**
- Create: `Assets/Prefabs/Character/Preview_Male.prefab`、`Assets/Prefabs/Character/Preview_Female.prefab`

- [ ] **Step 1: 复制预览 prefab**

Run: Unity MCP `assets-copy`：
- `Assets/Prefabs/Character/FemalePlayer.prefab` → `Assets/Prefabs/Character/Preview_Female.prefab`
- 男预览：以男 prefab（`Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`）复制 → `Assets/Prefabs/Character/Preview_Male.prefab`
- `assets-prefab-open` 各预览 prefab

- [ ] **Step 2: 剥离脚本组件**

Run: `gameobject-component-destroy`（每个预览 prefab）：
- `Hotfix.GameSystems.Sys3C.Sys3CEntry`、`UnityEngine.CharacterController`、`Hotfix.GameSystems.Sys3C.MeleeWeapon`、`Hotfix.GameSystems.Sys3C.CharacterAttackHandler`、`Hotfix.GameSystems.VFX.HitFlashVFX`、`FrostAuraVFX`、`IceDecalVFX`、`IceBurstVFX`、`SkillFreezeEffector`

Run: `gameobject-component-modify`（Animator `m_Enabled` → false，静态摆姿；若 Animator 移除后模型不渲染则保留并禁用）

Expected: 预览 prefab 无任何 MonoBehaviour，仅模型部件 + 部件可见性状态

- [ ] **Step 3: 保存预览 prefab**

Run: `assets-prefab-close`（save=true，两个预览 prefab）

- [ ] **Step 4: 场景摆放 + 旋转组件**

Run: `assets-prefab-instantiate`（`Preview_Male.prefab` / `Preview_Female.prefab` 到 DemoDay 场景，选人视角可见位置，两体间隔 ~2m，面向相机）
Run: `gameobject-component-add`（`Hotfix.GameSystems.UI.PreviewRotator`，两个预览体各一）
Run: `scene-save`（DemoDay）

- [ ] **Step 5: 提交**

```bash
git add Assets/Prefabs/Character/Preview_Male.prefab Assets/Prefabs/Character/Preview_Female.prefab Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: stripped preview prefabs + scene placement for character select"
```

---

### Task 7: CharacterSelectPanel.prefab + UIManager 启动接线

**Files:**
- Create: `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`
- Modify: `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs:31-40`

- [ ] **Step 1: 创建面板 prefab**

在 Unity 编辑器手动构建（或 MCP `gameobject-create` + `gameobject-component-add` 组合）：
- 根节点：`CharacterSelectPanel`（`CharacterSelectPanel` 脚本 + `CanvasGroup` + `Image`（全屏半透明黑，Color 0,0,0,0.6，raycastTarget=true））
- 子节点 1：`MaleCard`（`Image` + `Button` + `Text`「男」）
- 子节点 2：`FemaleCard`（同构，Text「女」）
- 子节点 3：`MaleHighlight`（高亮框 Image，默认 off）
- 子节点 4：`FemaleHighlight`（同构，默认 off）
- 子节点 5：`ConfirmButton`（`Image` + `Button` + `Text`「确认」）
- 保存为 `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`

- [ ] **Step 2: 接线序列化引用**

Run: `gameobject-component-modify`（CharacterSelectPanel 组件）：
- `_maleButton` → MaleCard 的 Button
- `_femaleButton` → FemaleCard 的 Button
- `_confirmButton` → ConfirmButton 的 Button
- `_maleHighlight` → MaleHighlight GameObject
- `_femaleHighlight` → FemaleHighlight GameObject

Expected: 无 null 引用

- [ ] **Step 3: UIManager 启动接线**

修改 `UIManager.cs` 的 `LoadBuiltInPanels()`（第 31-40 行），在 HUD 显示之后注册并打开选人面板：

```csharp
private async UniTask LoadBuiltInPanels()
{
    await RegisterFromPrefab("Prefabs/UI/PlayerHudPanel", "PlayerHudPanel");
    await RegisterFromPrefab("Prefabs/UI/TargetPanel", "TargetPanel");
    await RegisterFromPrefab("Prefabs/UI/CharacterSelectPanel", "CharacterSelectPanel");

    if (_registry.TryGetValue("PlayerHudPanel", out var hud))
    {
        await ShowPanelAsync(hud);
    }

    // 启动流程：先选人，再进世界（选人面板全屏覆盖 HUD）
    if (_registry.TryGetValue("CharacterSelectPanel", out var select))
    {
        await OpenAsync("CharacterSelectPanel");
    }
}
```

Expected: 启动时 HUD 显示 + 选人面板覆盖其上

- [ ] **Step 4: 编译验证 + 提交**

Run: Unity MCP `assets-refresh`
Expected: 无编译错误

```bash
git add Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs
git commit -m "feat: character select panel prefab + boot wiring in UIManager"
```

---

### Task 8: 场景重构（移除烘焙玩家 → PlayerSpawner）

**Files:**
- Modify: `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`

- [ ] **Step 1: 移除烘焙玩家**

Run: `gameobject-destroy`（DemoDay 场景 `MaleCharacterPBR` 实例）
Expected: 场景根不再有 MaleCharacterPBR

- [ ] **Step 2: 创建 PlayerSpawner + 出生点**

- Run: `gameobject-create`（`PlayerSpawner`，空对象）
- Run: `gameobject-component-add`（`Hotfix.GameSystems.Sys3C.Character.PlayerSpawner`）
- Run: `gameobject-create`（`PlayerSpawnPoint`，空对象，位置 = 原 MaleCharacterPBR 位置）

- [ ] **Step 3: 接线序列化引用**

Run: `gameobject-component-modify`（PlayerSpawner 组件）：
- `_malePlayerPrefab` → `Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`
- `_femalePlayerPrefab` → `Assets/Prefabs/Character/FemalePlayer.prefab`
- `_spawnPoint` → PlayerSpawnPoint

Expected: 三个引用非空

- [ ] **Step 4: 相机取景调整**

Run: `gameobject-modify`（Main Camera）：position/rotation 调整为能看到两个预览体的选人视角（可先 Enter PlayMode 微调）
Expected: 启动时相机画面 = 两个预览体居中，面板半透明覆盖

- [ ] **Step 5: 保存场景 + 提交**

Run: `scene-save`（DemoDay）

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: scene rework — spawner-driven player, spawn point, select-view camera"
```

---

### Task 9: 端到端验证（男女双路径）

**Files:**
- None（验证任务，无代码改动）

- [ ] **Step 1: EditMode 单元测试全绿**

Run: Unity MCP `tests-run`（testMode=EditMode，includePassingTests=false）
Expected: 全部 PASS（含既有测试 + 新增 4 个）

- [ ] **Step 2: 女角色路径（PlayMode）**

Run: `editor-application-set-state`（PlayMode）
1. 启动显示选人面板，半透明下可见两个预览体（男/女外观正确、慢速自转）
2. 点击 FemaleCard → 高亮切换；点击确认
3. 女角色在出生点生成；相机自动绑定并跟随
4. 移动 / 跳跃 / 冲刺 / 防御
5. 普攻连击（Attack1/Attack2）、技能 Q、技能 R（含突进）
6. 受击 / 击退 / 死亡动画、HitFlashVFX 泛红、飘字
7. 退出 PlayMode

- [ ] **Step 3: 持久化验证**

再次进入 PlayMode：
1. 选人面板默认高亮 = 上次选择的「女」
2. 点确认 → 女角色生成
3. 退出 PlayMode

- [ ] **Step 4: 男角色回归**

进入 PlayMode：点 MaleCard → 确认 → 男角色生成，快速路径验证（移动/攻击/受击）；退出 PlayMode

- [ ] **Step 5: 快速连点 / 非法操作**

- 连点确认 2 次 → 场景中只有一个玩家（旧实例被销毁）
- 场景保存后重启编辑器 → 启动无报错

- [ ] **Step 6: 记录结果**

将会话日志追加到 `production/session-logs/session-log.md`：验证日期、双路径结果、遗留问题（若有）

---

## 自检记录（执行前）

- **Spec §3.1** FemalePlayer 镜像 → Task 5 ✓；**§3.2** 预览剥离 → Task 6 ✓；**§4** 全部代码文件 → Task 1/2/3 ✓；**§5** UIManager+场景 → Task 7/8 ✓（Task 4 为接线基准前置）；**§6** 数据流 → 1→2→3→7→8 顺序闭环 ✓；**§7** 错误处理 → 服务默认值/生成器 null 检查/幂等 ✓；**§8** 测试 → Task 1 单测 + Task 9 场景验证 ✓
- **占位符扫描**：所有代码步骤含完整代码；资产步骤含精确 MCP 工具与参数 ✓
- **类型一致性**：`CharacterGender`/`CharacterSelectedEvent`/`PanelId="CharacterSelectPanel"`/Resources 路径 `Prefabs/UI/CharacterSelectPanel` 跨任务一致 ✓；`EventBus.Emit/Subscribe` 签名与现有 `EventBus.cs` 一致 ✓；测试命名遵循 `test_[system]_[scenario]_[expected_result]` 规范 ✓
- **一处实现细化**（相对 spec §4）：预览体由「面板 Instantiate」改为「场景烘焙 + PreviewRotator」—— 面板是 Resources 加载的 prefab，无法序列化场景锚点引用，场景烘焙免去锚点传递管道（无 GetComponent、无生命周期同步），与 spec 意图（剥离模型预览）一致
