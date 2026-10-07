# P6 第一个可交付包 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 产出一个可发给他人运行的 Windows PC 独立包——离线单机能打怪、被打有受击反馈、被打死能复活；联网可在游戏内配置服务器地址并连接；出错与断线都有屏幕可见出口。

**Architecture:** 先打通构建管线拿到"当前代码能出包"的基线（最大未知风险前置），再按依赖顺序修 7 处缺陷：④ 单机怪物伤害与数值对齐 → ① 受击反馈接线（落点 `FSMManager.HandleDamage`，复用其既有 `HandleShieldBreak`/`TriggerHit` 模式）→ ⑦ 本地死亡复活 → ⑤⑥ 刷怪记账与幽灵怪清理 → ② 判定窗口帧率无关化 → ③ 相机接线。最后补 3 件交付物（地址可配 / 断线返回）并重新出包验收。

**Tech Stack:** Unity 2022.3.25f1（Hotfix 汇编，无 HybridCLR）、NUnit EditMode/PlayMode、Unity MCP（`tests-run` / `script-execute` / `assets-refresh`）、protobuf-net + KCP 客户端、net8.0 服务端（仅联网验收用）。

---

## 决策记录（执行时不变更；偏差须在 Task 10 记录）

| # | 决策 | 内容 |
|---|------|------|
| **D-P6-1** | 交付形态 | Windows PC 独立安装包（**Mono** 后端）。移动端输入/触屏相机**不做** |
| **D-P6-2** | 构建脚本先行 | 动任何战斗代码前先出包成功一次，建立"当前代码能出包"基线——否则 7 处改动与首次打包失败无法二分定位 |
| **D-P6-3** | 受击接线落点 | `FSMManager.HandleDamage`（**不是** `StateCoordinator.HandleDamage`）。三条伤害路径（`PlayerHitZone.cs:35` / `Sys3CEntry.cs:410` / `Sys3CEntry.cs:512`）**全部经由它**；`FSMManager` 持具体类型 `_hitFSM`（免反射）、已订阅 `OnHitComplete`（`:55`）、已有现成同款模式（`HandleShieldBreak` :132-142） |
| **D-P6-4** | 死亡契约 | 受击走 `HitFSM.EnterHit(HitData)`；**死亡不走** `EnterHit(Damage=9999)`（秒杀旁路，不执行 `LockState`），走 `StateCoordinator.HandleDeath()` |
| **D-P6-5** | 层归还 | `HandleHitComplete` 补 `UnlockAndReturnToBase()`。**不夸大影响**：`_activeLayer` 卡在 Hit 不会让玩家永久失控（下次攻击 `Sys3CEntry.cs:339` 会重置），真正问题是受击到下次攻击之间 `FSMManager.Update:67` 的 `isAttacking` 失真 |
| **D-P6-6** | 死属性警告 | `StateCoordinator.CanMove` / `CanAttack`（`:18-20`）**全项目无消费者**，实现时不得依赖其语义 |
| **D-P6-7** | HP 权威 | 单机玩家 HP = `Sys3CEntry._currentHP`（`_maxHP = 100`）。`CharacterStatsAdapter.Health`（1000）唯一消费方 `CharacterController.ApplyDamage` **全项目无调用者**，是死路径 |
| **D-P6-8** | 路径纪律 | `PlayerHitZone` 路径**保持不扣 HP**（只负责受击反应与韧性），不补扣血逻辑——否则与 `IDamageable.TakeDamage` 双扣 |
| **D-P6-9** | 客户端怪物名册 | 客户端只有 **Slime + TurtleShell**（`Assets/Monstor/`），**没有 Wolf 配置**。只有 Slime 与服务端 `CombatRules` 50001 有对应关系 |
| **D-P6-10** | 数值对齐值 | Slime `MaxHP 600→100`、`_baseDamage 0→10`；TurtleShell `MaxHP 1500→250`、`_baseDamage 2→8`（服务端无此模板，按其"厚而钝"原定位压缩到同一量级：Slime 100/10 脆快、Wolf 150/15 均衡、TurtleShell 250/8 厚钝） |
| **D-P6-11** | 击退 | 两个 `_knockbackForce` 字段（`AttackDamage` 与 `AttackEffect`）都设：Slime **1.5**、TurtleShell **2.5**。理由：验收要求"被打有受击/**击退**表现"，而 `_knockbackForce = 0` 时 `EnterHit` 只进 `HitState.Hit`（最轻反应）。两条路径读不同来源（`TakeDamage` 读 `AttackDamage`、`PlayerHitZone` 读 `AttackHitboxData`），故两处都设 |
| **D-P6-12** | 刷怪记账抽取 | ⑤ 的记账逻辑抽为**纯类 `SpawnLedger`**（无 GameObject 依赖）→ EditMode 可测。本次 bug 正是记账错误（杀 1 补 11），必须有回归测试锁死；PlayMode 测 `MonsterSpawner` 需真实 Prefab 资产，成本过高 |
| **D-P6-13** | 重生点 | 重生回**初始生成点**（生成时记录），**不是**死亡位置——怪会追击玩家，按死亡位置重生会让怪群漂移出刷怪区 |
| **D-P6-14** | 幽灵怪清理 | `DespawnAll()` 只做 `Destroy` + 清集合，**不手动注销** `PhysicsRegistry`/`EntityDisplayManager`（`MonsterEntity.OnDestroy` 已负责） |
| **D-P6-15** | 帧率修复 | `UpdateExecution` 新增**独立字段** `_executionTimingCursor`，**不复用** `_currentTick`——后者已被 `UpdateCharging`（蓄力游标）与 `UpdateSpinning`（每 tick 自增）两种语义共用；蓄力技能链是 Charging→Execution，复用会跳过判定窗口 |
| **D-P6-16** | 不改旋转 | `UpdateSpinning`（`:192-207`）走独立时间门控（`_nextTickTime += TickInterval`），本就帧率无关，不动 |
| **D-P6-17** | 地址读取时机 | 在**每次 `Connect()` 调用时**读 `PlayerPrefs`，**不能只在 `Start()` 读一次**——直接跑 DemoDay 时选人确认发生在 `Start()` 之后（`ClientNetworkHost.cs:102-108` 既有注释与 `_connectRequested` 守卫正是为此打的补丁） |
| **D-P6-18** | 死亡守门 | ⑦ 由"是否联网模式"守卫，联网下**不自行判定死亡**（联网 HP 由服务端 `PlayerState.Hp` 权威持有，客户端自作判断会与服务端打架） |
| **D-P6-19** | 复活参数 | 倒计时 **3 秒**，回满 `_maxHP`，传送回出生点 |
| **D-P6-20** | 打断链 | **本批次不接线** `SkillCoordinator.OnDamageTaken`。受击期间技能继续执行——明确接受，属 P8 范围 |
| **D-P6-21** | 归还层守卫 | `HandleHitComplete` 的 `UnlockAndReturnToBase()` **必须加守卫**：仅当 `ActiveLayer == Hit` **且** `HitFSM.CurrentState != Death` 时归还。**修订 D-P6-5**（原为无条件归还）。由 Task 3 审查发现，人类裁定"两处都修"。原因：受击期间技能激活会把层设为 `Attack`（`Sys3CEntry.cs:339`，该路径无受击态门禁），无条件归还会把攻击层打回 Base → `isAttacking` 失真 → 攻击可被跳跃取消。<br>**执行期更正（实现者实测）**：`ActiveLayer == Hit` 那半句是**实际生效**的守卫；`CurrentState != Death` 那半句**当前不可达**——`OnHitComplete` 唯一触发点 `HitFSM.cs:284` 在 `Recover()` 内且被 `if (_currentState != HitState.Death)` 包住、并**先** `TransitionTo(None)` 再 invoke，故 `HandleHitComplete` 执行时状态必为 `None`。**死亡路径的真正防护是 D-P6-22**（Task 4 调 `EnterDeath()` 让 `Recover()` 自行短路），不是这里。该半句作为防御性检查保留（无损），已记入延后 minor 清单待最终审查甄别 |
| **D-P6-22** | 死亡态分离 | 本地死亡时**必须同时**调 `FSMManager.HitFSM.EnterDeath()`，不能只调 `RequestDeath()`。否则受击剪辑播完时 `HitFSM.Recover()` 仍会执行（它只挡 `HitState.Death`），触发 `OnHitComplete` → 清掉死亡锁 → 死者被解锁。**与 D-P6-4 不冲突**：D-P6-4 禁的是 `EnterHit(Damage=9999)` 秒杀旁路；`EnterDeath()`（`HitFSM.cs:244-251`）是专门方法，会正确发出 `DeathEvent` |
| **D-P6-23** | 死亡门禁 | 本地死亡必须**显式**锁移动与战斗输入：`_cc.LockMovement = _isDead \|\| !_skillCoordinator.CanMove();` + `HandleInput()` 开头 `if (_isDead) return;`。**为什么必须显式**：`StateCoordinator.CanMove` 是全项目无消费者的死属性（D-P6-6），既有的 `LockMovement` 只读 `_skillCoordinator.CanMove()`，而 `HandleInput` 无死亡门禁 → "进了 Death 态但照样能跑能放技能"。<br>**由 Task 4 实现者的 DONE_WITH_CONCERNS 暴露**：原 Task 4 设计只规定了"进 Death 态"，未规定如何阻止移动/施法，导致测试全绿而验收表第 5 条（"HP 归零后不能移动"）不达标——属计划自身的需求—验收缺口，非范围扩张 |
| **D-P6-24** | 连接失败也要有返回路径 | `HandleError`（连接失败/登录被拒）**也必须**置 `_pendingReturnToMenu = true;`，复用与断线相同的"提示 3 秒后回选人界面"机制；文案须带**本次实际连接的** `<地址>:<端口>`。**仍不做重试按钮**（YAGNI 禁的是那个）。<br>**由 Task 9 审查暴露**：原 Task 9 Step 4 把"最小做法"定为"让玩家手动重进选人界面"，但该前提**在本仓库不成立**——全仓 `LoadScene` 只有 Addressable 资源加载两处、`SelectStageController.cs:44`（选人→世界）、以及本任务新增的断线返回，**从世界场景回选人界面的唯一路径就是刚加的那条**。于是连接失败时玩家停在 DemoDay、提示却指向一个到不了的界面，只能重开进程——正是 §6 要消灭的"卡住"。<br>人类裁定"加，复用现有机制"。地址取"本次实际连接值"而非 `PlayerPrefs` 重读（`Connect` 参数可被显式覆盖，重读会显示错值） |

**Out of P6（显式排除，防范围蔓延）**：属性缩放接线（`_attackRatio`/`Stats` 注入）、等级/经验/掉落（P7）、投射物/蓄力/大招服务端化、**联网的**死亡/复活/胜负判定（P8）、移动端输入与摇杆、真实角色/怪物模型与动画、自动重连、AggroTable、反作弊收紧、`PlayerHitZone` 补扣血逻辑、旋转技能改造、打断链接线。

**验收目标（spec §8）**：① 出包并双击 exe 可运行；② 单机打怪 10 分钟刷怪数量不爆炸；③ 被打有受击/击退表现；④ 被打死能复活；⑤ 联网可游戏内配地址并连上；⑥ 断线有提示并能返回主界面。

**回归基线（必须保持全绿）**：Server.Tests 132/132、EditMode 200/200、PlayMode 现有用例。任何任务后若基线转红，立即停止并修复。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/Editor/BuildScript.cs` | 创建 | Windows x64 出包入口（菜单 + 命令行），场景列表显式声明 |
| `Assets/Monstor/SlimeConfig.asset` | 修改 | 单机史莱姆数值对齐服务端 |
| `Assets/Monstor/TurtleShellConfig.asset` | 修改 | 单机龟壳数值压缩到同一量级 |
| `Assets/Scripts/Tests/Editor/MonsterConfigNumbersTests.cs` | 创建 | **数值一致性断言**，锁死 ④，防再次漂移 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs` | 修改 | ① 受击接线 + 层归还；⑦ 复活入口 |
| `Assets/Scripts/Tests/Editor/FSMManagerHitFeedbackTests.cs` | 创建 | ① 的行为测试（进 Hit 态 / 切层 / 恢复回 Base） |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 修改 | ⑦ 死亡判定 + 复活倒计时 |
| `Assets/Scripts/Tests/PlayMode/PlayerDeathRespawnTests.cs` | 创建 | ⑦ 的端到端行为 |
| `Assets/Scripts/Hotfix/GameSystems/Monster/SpawnLedger.cs` | 创建 | ⑤ 刷怪记账纯类（EditMode 可测） |
| `Assets/Scripts/Tests/Editor/SpawnLedgerTests.cs` | 创建 | ⑤ 的回归测试（杀 1 补 1、重生点=生成点） |
| `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs` | 修改 | ⑤ 接入 ledger；⑥ `DespawnAll()` |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs` | 修改 | ② 判定窗口游标化 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` | 修改 | ② 删除 `consecutiveRequired` |
| `Assets/Scripts/Tests/Editor/SkillStateMachineHitboxWindowTests.cs` | 创建 | ② 的帧率无关性测试 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs` | 修改 | ③ `LateUpdate` 驱动 + SphereCast 防穿墙 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 修改 | 3.1 地址读 PlayerPrefs；3.3 断线订阅；⑥ 调用 `DespawnAll()` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs` | 修改 | 3.1 服务器地址/端口/昵称的 PlayerPrefs 读写 |
| 选人面板（`Assets/Scenes/CharacterSelect.unity` 及其脚本） | 修改 | 3.1 地址输入区 |
| `Assets/Scripts/Hotfix/GameSystems/UI/...`（复用既有 Tips/Confirm） | 修改 | 3.3 断线提示面板 |
| `.gitignore` | 修改 | 忽略 `Build/` |

---

### Task 1: 构建管线基线（硬门槛）

**Files:**
- 创建：`Assets/Scripts/Editor/BuildScript.cs`
- 修改：`.gitignore`

**为什么先做**：打包是本次最大未知风险（KCP/protobuf 在 Player 下、场景资源完整性、依赖裁剪全未验证），且项目从未构建过 Player。先拿到基线，后续任何改动才有对照。

- [ ] **Step 1: 确认 `.gitignore` 忽略构建产物**

打开仓库根 `.gitignore`，确认包含 `Build/`。若无则追加：

```
# P6 构建产物
Build/
```

- [ ] **Step 2: 编写构建脚本**

创建 `Assets/Scripts/Editor/BuildScript.cs`：

```csharp
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// P6 交付构建——Windows x64 独立包。
    /// 后端固定 Mono（出包快、变量少；IL2CPP 留待移动端验证）。
    /// 场景列表显式声明，不依赖 EditorBuildSettings 当前状态。
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDir = "Build/Windows";
        private const string ExeName = "CoolP6.exe";

        private static readonly string[] Scenes =
        {
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity",
        };

        [MenuItem("Cool/Build/Windows x64")]
        public static void BuildWindows()
        {
            foreach (var scene in Scenes)
            {
                if (!File.Exists(scene))
                {
                    Debug.LogError("[Build] 构建场景缺失: " + scene);
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                    return;
                }
            }

            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputDir + "/" + ExeName,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                // 失败必须让命令行拿到非零退出码（自动化出包依赖）。
                // 交互模式下不得 Exit——会直接关掉编辑器。
                Debug.LogError("[Build] 出包失败: result=" + summary.result
                    + " errors=" + summary.totalErrors
                    + " size=" + summary.totalSize);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Build] 出包成功: " + summary.outputPath
                + " size=" + summary.totalSize + "B"
                + " time=" + summary.totalTime);
        }
    }
}
```

- [ ] **Step 3: 刷新资产数据库**

运行 Unity MCP：`assets-refresh`
预期：编译无错误（若有报错，先修完再继续）。

- [ ] **Step 4: 执行构建**

运行 Unity MCP `script-execute`，调用：

```csharp
Cool.EditorTools.BuildScript.BuildWindows();
```

预期：Console 出现 `[Build] 出包成功: Build/Windows/CoolP6.exe ...`

- [ ] **Step 5: 验证产物存在**

```bash
ls -la Build/Windows/CoolP6.exe Build/Windows/CoolP6_Data/
```

预期：`CoolP6.exe` 与 `CoolP6_Data/` 目录均存在。

- [ ] **Step 6: 手工验证包可运行**

双击 `Build/Windows/CoolP6.exe`。
预期：出现选人面板（CharacterSelect 场景）；无黑屏、无立即崩溃。
若失败：查看 `%USERPROFILE%\AppData\LocalLow\<公司名>\<产品名>\Player.log` 定位缺失依赖。

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Editor/BuildScript.cs .gitignore
git commit -m "build(p6): Windows x64 build script + baseline package

Sets up the delivery pipeline before any combat changes. Scene list is
declared explicitly rather than read from EditorBuildSettings, and the
exit-on-failure path is guarded by Application.isBatchMode so interactive
use does not close the editor."
```

---

### Task 2: 单机怪物伤害修复与数值对齐（④）

**Files:**
- 修改：`Assets/Monstor/SlimeConfig.asset`（`:18` `:19` `:51` `:59` `:65`）
- 修改：`Assets/Monstor/TurtleShellConfig.asset`（`:18` `:19` `:51` `:59` `:72`）
- 创建：`Assets/Scripts/Tests/Editor/MonsterConfigNumbersTests.cs`

**背景**：怪物攻击经 `PlayerHitZone.cs:29` 的 `CalculateFinalDamage(null)` 取伤害，`Stats` 传 null 使 `_attackRatio` 被跳过，**唯一伤害来源是 `_baseDamage`**——而它是 0。同时 `_knockbackForce = 0` 使受击只进最轻的 `Hit` 态。

- [ ] **Step 1: 编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/MonsterConfigNumbersTests.cs`：

```csharp
using Hotfix.GameSystems.Monster;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ④：单机怪物数值一致性断言——锁死"客户端资产与服务端表同值"，
    /// 防止再次出现"三源不一致"漂移（Slime 曾 600HP/0伤害 vs 服务端 100HP/10伤害）。
    /// </summary>
    public class MonsterConfigNumbersTests
    {
        private const string SlimePath = "Assets/Monstor/SlimeConfig.asset";
        private const string TurtlePath = "Assets/Monstor/TurtleShellConfig.asset";

        private static MonsterConfig Load(string path)
        {
            var config = AssetDatabase.LoadAssetAtPath<MonsterConfig>(path);
            Assert.IsNotNull(config, "怪物配置资产缺失: " + path);
            return config;
        }

        [Test]
        public void test_slime_damage_is_nonzero()
        {
            // Arrange
            var config = Load(SlimePath);

            // Assert（服务端 CombatRules 50001 = 10）
            Assert.Greater(config.AttackDamage.BaseDamage, 0f,
                "单机史莱姆伤害必须大于 0（曾为 0，导致单机毫无威胁）");
            Assert.AreEqual(10f, config.AttackDamage.BaseDamage, 0.001f,
                "单机史莱姆伤害须与服务端 CombatRules 50001 一致");
        }

        [Test]
        public void test_slime_hp_matches_server()
        {
            // Arrange
            var config = Load(SlimePath);

            // Assert（服务端 monster_config.json = 100）
            Assert.AreEqual(100f, config.MaxHP, 0.001f,
                "单机史莱姆血量须与服务端一致（曾为 600）");
        }

        [Test]
        public void test_turtle_damage_is_nonzero_and_hp_rescaled()
        {
            // Arrange
            var config = Load(TurtlePath);

            // Assert（服务端无此模板，按同量级定值 250/8）
            Assert.AreEqual(8f, config.AttackDamage.BaseDamage, 0.001f,
                "龟壳伤害须大于 0（曾为 2 且 _baseDamage 路径不同）");
            Assert.AreEqual(250f, config.MaxHP, 0.001f,
                "龟壳血量须压缩到同量级（曾为 1500），保留厚而钝定位");
        }

        [Test]
        public void test_both_monsters_have_knockback()
        {
            // Arrange
            var slime = Load(SlimePath);
            var turtle = Load(TurtlePath);

            // Assert（击退为 0 时 EnterHit 只进最轻的 Hit 态，验收要求可见击退）
            Assert.Greater(slime.AttackDamage.KnockbackForce, 0f, "史莱姆攻击须有击退");
            Assert.Greater(turtle.AttackDamage.KnockbackForce, 0f, "龟壳攻击须有击退");
        }
    }
}
```

> **注**：`DamageBlock.BaseDamage` / `KnockbackForce` 为既有公开属性（`Sys3CEntry.cs:436` 已使用 `data?.BaseDamage`）。若 `KnockbackForce` 不是公开属性，将其改为公开只读属性后再跑测试。

- [ ] **Step 2: 运行测试验证失败**

运行 Unity MCP：`tests-run`，`mode=EditMode`，`testClass=MonsterConfigNumbersTests`
预期：4 个测试全部 FAIL（`BaseDamage` 为 0 / `MaxHP` 为 600 / 龟壳 2 与 1500 / `KnockbackForce` 为 0）。

- [ ] **Step 3: 修改 SlimeConfig**

编辑 `Assets/Monstor/SlimeConfig.asset`，改 5 处：

| 行 | 原值 | 新值 |
|---|---|---|
| 18 | `MaxHP: 600` | `MaxHP: 100` |
| 19 | `AttackPower: 15` | `AttackPower: 10` |
| 51 | `_baseDamage: 0` | `_baseDamage: 10` |
| 59 | `_knockbackForce: 0`（`AttackDamage` 内） | `_knockbackForce: 1.5` |
| 65 | `_knockbackForce: 0`（`AttackEffect` 内） | `_knockbackForce: 1.5` |

> `AttackPower` 一并改：它目前无消费者（`MonsterEntity.Stats` 恒 null），但留着 15 会重新制造"同一件事两个数"的问题。

- [ ] **Step 4: 修改 TurtleShellConfig**

编辑 `Assets/Monstor/TurtleShellConfig.asset`，改 5 处：

| 行 | 原值 | 新值 |
|---|---|---|
| 18 | `MaxHP: 1500` | `MaxHP: 250` |
| 19 | `AttackPower: 10` | `AttackPower: 8` |
| 51 | `_baseDamage: 2` | `_baseDamage: 8` |
| ~66 | `_knockbackForce: 0`（`AttackDamage` 内） | `_knockbackForce: 2.5` |
| 72 | `_knockbackForce: 0`（`AttackEffect` 内） | `_knockbackForce: 2.5` |

> `AttackDamage` 内的 `_knockbackForce` 行号需就近确认（`TurtleShellConfig.asset` 中 `AttackDamage` 段紧邻 `:51`）。

- [ ] **Step 5: 刷新资产并重跑测试**

运行 `assets-refresh`，然后 `tests-run`，`mode=EditMode`，`testClass=MonsterConfigNumbersTests`
预期：4/4 PASS。

- [ ] **Step 6: 跑全量 EditMode 回归**

运行 `tests-run`，`mode=EditMode`
预期：全绿（基线 200 + 4 新增）。

- [ ] **Step 7: Commit**

```bash
git add Assets/Monstor/SlimeConfig.asset Assets/Monstor/TurtleShellConfig.asset Assets/Scripts/Tests/Editor/MonsterConfigNumbersTests.cs
git commit -m "fix(monster): single-player monsters deal damage + numbers aligned to server

DamageBlock.CalculateFinalDamage is called with null stats by PlayerHitZone,
so _attackRatio is skipped and _baseDamage is the only damage source - and it
was 0, making single-player combat completely harmless. Slime had 600 HP
against the server's 100, so melee felt like a different game per mode.

Also sets knockback (0 -> 1.5/2.5) on both the AttackDamage and AttackEffect
blocks: at 0 the hit reaction resolves to the lightest Hit state, and the
acceptance criteria call for visible knockback. Both blocks are set because the
two damage paths read different sources."
```

---

### Task 3: 受击反馈接线（①）

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`（`:94-107` 的 `HandleDamage`；`:177-182` 的 `HandleHitComplete`）
- 创建：`Assets/Scripts/Tests/Editor/FSMManagerHitFeedbackTests.cs`

**落点理由（D-P6-3）**：三条伤害路径全部经由 `FSMManager.HandleDamage`；该类持具体类型 `_hitFSM`（免反射）、已订阅 `OnHitComplete`、已有现成同款模式 `HandleShieldBreak`（`:132-142`）。

- [ ] **Step 1: 编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/FSMManagerHitFeedbackTests.cs`：

```csharp
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ①：受击反馈接线——受击必须真正进入 HitFSM 并切换活动层，
    /// 受击结束后必须把活动层交还 Base（否则 isAttacking 读数失真）。
    /// 用 null Animator 构造（FSMManager 仅在构造时 SetLayerWeight，需容忍 null）。
    /// </summary>
    public class FSMManagerHitFeedbackTests
    {
        private FSMManager _fsm;

        [SetUp]
        public void SetUp()
        {
            _fsm = new FSMManager(null, null);
        }

        [Test]
        public void test_handled_damage_enters_hit_state()
        {
            // Arrange
            Assert.AreEqual(HitState.None, _fsm.HitFSM.CurrentState, "初始应为 None");

            // Act
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);

            // Assert
            Assert.AreNotEqual(HitState.None, _fsm.HitFSM.CurrentState,
                "受击后必须进入 HitFSM——曾完全不调用 EnterHit，受击链空转");
        }

        [Test]
        public void test_handled_damage_sets_active_layer_to_hit()
        {
            // Arrange & Act
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);

            // Assert
            Assert.AreEqual(LayerType.Hit, _fsm.Coordinator.ActiveLayer,
                "受击必须切换活动层，否则 Hit 层与 Attack 层同时驱动 Animator");
        }

        [Test]
        public void test_knockback_damage_enters_knockback_state()
        {
            // Arrange & Act（有击退 → 应进 Knockback 而非最轻的 Hit）
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 2f);

            // Assert
            Assert.AreEqual(HitState.Knockback, _fsm.HitFSM.CurrentState,
                "有击退力度时应进入 Knockback 态");
        }

        [Test]
        public void test_hit_recovery_returns_layer_to_base()
        {
            // Arrange
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);
            Assert.AreEqual(LayerType.Hit, _fsm.Coordinator.ActiveLayer);

            // Act（受击动画播完）
            _fsm.HitFSM.Recover();

            // Assert
            Assert.AreEqual(LayerType.Base, _fsm.Coordinator.ActiveLayer,
                "受击结束必须把活动层交还 Base");
        }

        [Test]
        public void test_hit_recovery_does_not_override_a_layer_that_moved_on()
        {
            // Arrange（受击期间技能激活把层设为 Attack——该路径无受击态门禁，D-P6-20 明确接受）
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);
            _fsm.Coordinator.SetAttackLayerActive();
            Assert.AreEqual(LayerType.Attack, _fsm.Coordinator.ActiveLayer);

            // Act（受击动画播完）
            _fsm.HitFSM.Recover();

            // Assert（D-P6-21 守卫：层已走掉时不得覆盖，否则攻击可被跳跃取消）
            Assert.AreEqual(LayerType.Attack, _fsm.Coordinator.ActiveLayer,
                "受击结束时若层已切到 Attack，不得打回 Base");
        }
    }
}
```

> **D-P6-22（死亡态分离）的测试不在本任务**：它属于 Task 4——Task 4 的 PlayMode 测试已断言"HP 归零必须进入 `HitState.Death`"，而 `StateCoordinator.HandleDeath()` 单独并不会让 `HitFSM` 进 Death 态，所以那条断言本身就是 D-P6-22 的 red-first 测试。不要在本任务重复覆盖。

> **注**：`FSMManager` 构造会调 `Animator.SetLayerWeight`。若 `null` Animator 抛 `NullReferenceException`，改用 `new GameObject().AddComponent<Animator>()` 构造，并在 `TearDown` 中 `Object.DestroyImmediate`。实现时以实际行为为准，勿猜。

- [ ] **Step 2: 运行测试验证失败**

运行 `tests-run`，`mode=EditMode`，`testClass=FSMManagerHitFeedbackTests`
预期：`test_handled_damage_enters_hit_state`、`test_handled_damage_sets_active_layer_to_hit`、`test_knockback_damage_enters_knockback_state`、`test_hit_recovery_returns_layer_to_base`、`test_hit_recovery_does_not_override_a_layer_that_moved_on` 全部 FAIL（`HandleDamage` 目前完全不碰 HitFSM；最后一条同时锁定 D-P6-21 的守卫）。

- [ ] **Step 3: 在 HandleDamage 中接线**

修改 `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs` 的 `HandleDamage`（`:94-107`），在既有 `_stateCoordinator.HandleDamage(damageEvent)` **之后**追加：

```csharp
        public void HandleDamage(int sourceId, float damage, Vector3 hitDirection,
            float knockbackForce = 0, float launchForce = 0, float stunDuration = 0, bool isCritical = false)
        {
            var damageEvent = new Core.Events.DamageEvent(sourceId, 0, damage, isCritical)
            {
                HitDirection = hitDirection,
                KnockbackForce = knockbackForce,
                LaunchForce = launchForce,
                StunDuration = stunDuration
            };

            // 既有：免疫判定 / 扣韧性 / 发事件
            _stateCoordinator.HandleDamage(damageEvent);

            // P6 ①：受击表现接线。DamageEvent 的字段与 HitData 同构（Damage/KnockbackForce/
            // LaunchForce/StunDuration/HitDirection），直接映射，无需新增事件或协议。
            // 与既有 HandleShieldBreak(:132)/TriggerHit(:144) 同款模式：EnterHit + 切 Hit 层。
            var hitData = new HitData
            {
                Damage = damage,
                HitDirection = hitDirection,
                KnockbackForce = knockbackForce,
                LaunchForce = launchForce,
                StunDuration = stunDuration
            };

            // 返回值 false = 被更高优先级状态拒绝（已在更高优先级状态 / 已死亡）——尊重既有优先级，不切层
            if (_hitFSM.EnterHit(hitData))
                _stateCoordinator.SetActiveLayer(LayerType.Hit);
        }
```

- [ ] **Step 4: 在 HandleHitComplete 中归还活动层**

修改同文件的 `HandleHitComplete`（`:177-182`）：

```csharp
        private void HandleHitComplete()
        {
            _animator.SetBool(AnimHashes.IsHit, false);
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);

            // P6 ①：把活动层交还 Base——与攻击流 Sys3CEntry.cs:390 的既有做法一致。
            // 不归还则 ActiveLayer 滞留在 Hit，FSMManager.Update:67 的 isAttacking 读数失真。
            //
            // 守卫（D-P6-21）：只在受击层**仍是当前活动层**且**未死亡**时归还。
            // 少了这个守卫会覆盖已经走掉的层——受击期间技能激活会把层设为 Attack
            // （Sys3CEntry.cs:339，该路径无受击态门禁，D-P6-20 明确接受），
            // 此时无条件归还会把攻击层打回 Base，使攻击可被跳跃取消。
            if (_stateCoordinator.ActiveLayer == LayerType.Hit
                && _hitFSM.CurrentState != HitState.Death)
            {
                _stateCoordinator.UnlockAndReturnToBase();
            }

            OnHitCompleted?.Invoke();
        }
```

> **为何要同时判 `HitState.Death`**：死亡后 `StateCoordinator.HandleDeath()` 自己就会 `SetActiveLayer(Hit)`（`StateCoordinator.cs:67`），所以单看 `ActiveLayer == Hit` 守卫会放行，仍会把死亡锁清掉。死亡态的拦截由 Task 4 的 `HitFSM.EnterDeath()`（D-P6-22）与这里的 `CurrentState != Death` 共同保证。

- [ ] **Step 5: 运行测试验证通过**

运行 `tests-run`，`mode=EditMode`，`testClass=FSMManagerHitFeedbackTests`
预期：5/5 PASS。

- [ ] **Step 6: 跑全量 EditMode 回归**

运行 `tests-run`，`mode=EditMode`
预期：全绿。

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs Assets/Scripts/Tests/Editor/FSMManagerHitFeedbackTests.cs
git commit -m "fix(3c): wire hit feedback into HitFSM (was never called)

All three damage paths (PlayerHitZone, server damage, IDamageable.TakeDamage)
funnel through FSMManager.HandleDamage, which built a DamageEvent and handed it
to StateCoordinator for resistance plus events - and never touched HitFSM. The
485-line hit state machine and the third Animator layer were effectively idle,
so getting hit played nothing.

Wires at FSMManager rather than StateCoordinator because it holds a concrete
HitFSM (no reflection needed), already subscribes to OnHitComplete, and already
contains this exact EnterHit + SetActiveLayer(Hit) pattern in HandleShieldBreak.

HandleHitComplete now also returns the active layer to Base, matching what the
attack flow already does at Sys3CEntry.cs:390."
```

---

### Task 4: 单机本地死亡 / 复活闭环（⑦）

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/PlayerDeathRespawnTests.cs`

**现状**：`HitFSM.Resurrect()`（`HitFSM.cs:291-298`）与 `FSMManager.RequestResurrect()`（`:113-116`）**均无调用者**；`_currentHP <= 0` 后玩家仍能移动施法。

- [ ] **Step 1: 编写失败的测试**

创建 `Assets/Scripts/Tests/PlayMode/PlayerDeathRespawnTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// P6 ⑦：单机本地死亡/复活闭环。HP 归零必须进 Death 态并锁移动，
    /// 倒计时结束后回满 HP、回 Base 层、回到出生点。
    /// </summary>
    public class PlayerDeathRespawnTests
    {
        private GameObject _goInstance;
        private Sys3CEntry _entry;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab,
                "测试用玩家 Prefab 缺失：需在 Assets/Resources/ 放置含 Sys3CEntry 的 Prefab 并命名 PlayerEntryForTest");
            _goInstance = Object.Instantiate(prefab);
            _entry = _goInstance.GetComponent<Sys3CEntry>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_goInstance != null) Object.DestroyImmediate(_goInstance);
        }

        [UnityTest]
        public IEnumerator test_hp_zero_enters_death_state()
        {
            // Arrange & Act（服务端伤害入口直接扣到 0；单机模式下走同一 HP 字段）
            _entry.ApplyServerDamage(9999f, Vector3.back);
            yield return null;

            // Assert
            Assert.AreEqual(HitState.Death, _entry.Controller.HitFSM.CurrentState,
                "HP 归零必须进入 Death 态——曾什么都不发生，玩家仍能移动施法");
        }

        [UnityTest]
        public IEnumerator test_dead_player_has_movement_locked()
        {
            // Arrange & Act
            _entry.ApplyServerDamage(150f, Vector3.back);
            yield return null;

            // Assert（验收表第 5 条："HP 归零后……不能移动"）
            Assert.IsTrue(_entry.IsDead, "应处于死亡状态");
            Assert.IsTrue(_entry.CoreController.LockMovement,
                "死亡时必须锁定移动——StateCoordinator.CanMove 无消费者（D-P6-6），靠 _isDead 显式并入");
        }

        [UnityTest]
        public IEnumerator test_respawn_restores_hp_and_layer()
        {
            // Arrange
            float maxHp = _entry.MaxHp;
            _entry.ApplyServerDamage(9999f, Vector3.back);
            yield return null;

            // Act（等待复活倒计时 3s + 缓冲）
            yield return new WaitForSeconds(3.5f);

            // Assert
            Assert.AreEqual(maxHp, _entry.CurrentHp, 0.01f, "复活应回满 HP");
            Assert.AreEqual(HitState.None, _entry.Controller.HitFSM.CurrentState, "复活应回到 None 态");
            Assert.AreEqual(Hotfix.GameSystems.Sys3C.Core.LayerType.Base,
                _entry.Controller.Coordinator.ActiveLayer, "复活应把活动层交还 Base");
        }
    }
}
```

> **前置**：本测试需要 `Assets/Resources/PlayerEntryForTest.prefab`（含 `Sys3CEntry` 的最小玩家 Prefab）。用 Unity MCP `assets-prefab-create` 从 DemoDay 中玩家对象创建并复制到 `Assets/Resources/` 后重命名为 `PlayerEntryForTest`。同时需要 `Sys3CEntry` 暴露 `MaxHp` / `CurrentHp` / `Controller` 三个只读访问器（Step 3 一并加）。

- [ ] **Step 2: 运行测试验证失败**

运行 `tests-run`，`mode=PlayMode`，`testClass=PlayerDeathRespawnTests`
预期：编译失败（`MaxHp` / `CurrentHp` / `Controller` 不存在）或 FAIL（HP 归零后未进 Death 态）。

- [ ] **Step 3: 暴露只读访问器并实现死亡判定**

修改 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`：

在字段区（`:22-24` 附近）之后增加复活参数与状态：

```csharp
        [Header("Respawn")]
        [Tooltip("死亡后复活倒计时（秒）")]
        [SerializeField] private float _respawnDelay = 3f;

        private float _respawnTimer;
        private bool _isDead;

        public float MaxHp => _maxHP;
        public float CurrentHp => _currentHP;
        public FSMManager Controller => _fsmManager;
        public bool IsDead => _isDead;
```

在 `ApplyServerDamage`（`:405-427`）与 `IDamageable.TakeDamage` 的**扣血之后**，统一调用一个新方法 `EvaluateLocalDeath()`。同时在 `Update`（或既有每帧入口）中驱动倒计时：

```csharp
        /// <summary>
        /// P6 ⑦：单机本地死亡判定。仅单机模式生效——联网模式下玩家 HP 由服务端
        /// PlayerState 权威持有，客户端自作判断会与服务端打架（D-P6-18）。
        /// </summary>
        private void EvaluateLocalDeath()
        {
            if (IsOnlineMode()) return;      // 联网：交给服务端，见 D-P6-18
            if (_isDead) return;
            if (_currentHP > 0f) return;

            _isDead = true;
            _respawnTimer = _respawnDelay;

            // 走 HandleDeath（含 SetActiveLayer(Hit) + LockState），不走 EnterHit(Damage=9999)
            // ——后者是秒杀旁路，不执行 LockState，会留下"死亡但能动"的坏状态（D-P6-4）。
            _fsmManager.RequestDeath();

            // D-P6-22：**必须同时**让 HitFSM 进入 Death 态。
            // 否则受击剪辑播完时 HitFSM.Recover() 仍会执行（它只挡 HitState.Death，此刻不是），
            // 触发 OnHitComplete → HandleHitComplete → 把死亡锁清掉——死者被解锁。
            // 这与 D-P6-4 不冲突：被禁的是 EnterHit(Damage=9999) 那条秒杀旁路；
            // EnterDeath()（HitFSM.cs:244-251）是专门方法，会正确发出 DeathEvent。
            _fsmManager.HitFSM.EnterDeath();
        }

        private void TickRespawn(float deltaTime)
        {
            if (!_isDead) return;

            _respawnTimer -= deltaTime;
            if (_respawnTimer > 0f) return;

            _isDead = false;
            _currentHP = _maxHP;
            _fsmManager.HitFSM.Resurrect();
            _fsmManager.RequestResurrect();
            transform.position = SpawnPointProvider.PlayerSpawnPosition;
        }

        private static bool IsOnlineMode()
            => NetworkModeService.Load() == NetworkMode.Online;
```

> `SpawnPointProvider` 与 `NetworkModeService` 的命名空间按实际引用补 `using`。若不存在现成的出生点常量，用 `LoginHandler` 下发到客户端的出生点值，或在 `Sys3CEntry` 上以 `[SerializeField] private Vector3 _spawnPoint;` 配置并由场景赋值。

- [ ] **Step 4: 运行测试验证通过**

运行 `tests-run`，`mode=PlayMode`，`testClass=PlayerDeathRespawnTests`
预期：2/2 PASS。

- [ ] **Step 5: 跑全量 PlayMode 回归**

运行 `tests-run`，`mode=PlayMode`
预期：既有用例 + 2 新增全绿。若既有网络用例失败，先确认服务器是否需以 `--bots` 启动（见 `docs/superpowers/guides/network-playtest.md`）。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs Assets/Scripts/Tests/PlayMode/PlayerDeathRespawnTests.cs Assets/Resources/PlayerEntryForTest.prefab
git commit -m "feat(player): single-player death and respawn loop

HitFSM.Resurrect() and FSMManager.RequestResurrect() existed but nothing
called them, and HP reaching zero did nothing at all - the player kept moving
and casting. Once monsters actually deal damage (previous commit) that reads as
a broken game rather than an unfinished one.

Death routes through RequestDeath/HandleDeath rather than EnterHit(Damage=9999)
because the latter is a one-shot-kill bypass that skips LockState. Guarded by
mode so it never fires while online, where the server owns player HP."
```

---

### Task 5: 刷怪记账修复 + 幽灵怪清理（⑤⑥）

**Files:**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/SpawnLedger.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:253-258`
- 创建：`Assets/Scripts/Tests/Editor/SpawnLedgerTests.cs`

**两个 bug**：① `HandleMonsterDeath`（`:136-148`）按 `MonsterId` 记一个计时器，到期在 `Update`（`:51-62`）调 `SpawnGroupInternal` **重生整组**，且 `Spawn`（`:129`）每次都 `OnDeathComplete += ...` 形成**订阅累积** → 杀 1 只 30 秒后 +11 只，只增不减。② `ClientNetworkHost.cs:253-258` 只 `SetActive(false)`，而 `PhysicsRegistry.FindNearby` 不查 `activeInHierarchy` → 幽灵怪可被打、名牌残影。

- [ ] **Step 1: 编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/SpawnLedgerTests.cs`：

```csharp
using Hotfix.GameSystems.Monster;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ⑤：刷怪记账回归测试。
    /// 原 bug：死亡按 MonsterId 记计时器，到期重生整组 Count 只 → 杀 1 只 30s 后 +11 只，只增不减。
    /// </summary>
    public class SpawnLedgerTests
    {
        private SpawnLedger _ledger;

        [SetUp]
        public void SetUp() => _ledger = new SpawnLedger();

        [Test]
        public void test_record_spawn_increments_group_alive_count()
        {
            // Arrange & Act
            for (int i = 0; i < 11; i++)
                _ledger.RecordSpawn(groupIndex: 0, entityId: 1000 + i, origin: Vector3.zero);

            // Assert
            Assert.AreEqual(11, _ledger.AliveCount(0));
        }

        [Test]
        public void test_one_death_removes_exactly_one_from_alive_count()
        {
            // Arrange（11 只一组）
            for (int i = 0; i < 11; i++)
                _ledger.RecordSpawn(0, 1000 + i, Vector3.zero);

            // Act（杀 1 只）
            bool recorded = _ledger.RecordDeath(entityId: 1000, out int groupIndex, out _);

            // Assert（原 bug 会让存活数反弹到 21）
            Assert.IsTrue(recorded, "死亡应被记账");
            Assert.AreEqual(0, groupIndex);
            Assert.AreEqual(10, _ledger.AliveCount(0), "死一只只能减一只");
        }

        [Test]
        public void test_death_returns_the_initial_spawn_origin_not_death_position()
        {
            // Arrange（怪在 (3,0,4) 生成，被玩家引到远处打死）
            var origin = new Vector3(3f, 0f, 4f);
            _ledger.RecordSpawn(0, 42, origin);

            // Act
            _ledger.RecordDeath(42, out _, out Vector3 respawnOrigin);

            // Assert（按死亡位置重生会让怪群逐步漂移出刷怪区）
            Assert.AreEqual(origin, respawnOrigin, "重生点必须是初始生成点");
        }

        [Test]
        public void test_death_of_unknown_entity_is_ignored()
        {
            // Arrange（未记录的实体）
            _ledger.RecordSpawn(0, 1, Vector3.zero);

            // Act
            bool recorded = _ledger.RecordDeath(entityId: 999, out _, out _);

            // Assert
            Assert.IsFalse(recorded, "未记录实体的死亡应被忽略，不能污染计数");
            Assert.AreEqual(1, _ledger.AliveCount(0));
        }

        [Test]
        public void test_alive_count_is_tracked_per_group()
        {
            // Arrange & Act
            _ledger.RecordSpawn(0, 1, Vector3.zero);
            _ledger.RecordSpawn(0, 2, Vector3.zero);
            _ledger.RecordSpawn(1, 3, Vector3.zero);

            // Assert
            Assert.AreEqual(2, _ledger.AliveCount(0));
            Assert.AreEqual(1, _ledger.AliveCount(1));
        }
    }
}
```

- [ ] **Step 2: 运行测试验证失败**

运行 `tests-run`，`mode=EditMode`，`testClass=SpawnLedgerTests`
预期：编译失败（`SpawnLedger` 不存在）。

- [ ] **Step 3: 实现 SpawnLedger**

创建 `Assets/Scripts/Hotfix/GameSystems/Monster/SpawnLedger.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    /// <summary>
    /// P6 ⑤：刷怪记账（纯逻辑，无 GameObject 依赖——EditMode 可测）。
    /// 职责：按组维护存活数，并记住每只怪的**初始生成点**供重生复位。
    /// 抽出的理由：本次 bug 正是记账错误（死亡按 MonsterId 记、到期重生整组），
    /// 必须有回归测试锁死；而 PlayMode 测 MonsterSpawner 需要真实 Prefab 资产，成本过高。
    /// </summary>
    public sealed class SpawnLedger
    {
        private struct Entry
        {
            public int GroupIndex;
            public Vector3 Origin;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly Dictionary<int, int> _aliveByGroup = new Dictionary<int, int>();

        /// <summary>该组当前存活数（未记录过的组返回 0）</summary>
        public int AliveCount(int groupIndex)
            => _aliveByGroup.TryGetValue(groupIndex, out int count) ? count : 0;

        /// <summary>记录一次生成：存活 +1，并记住该实体的归属组与初始生成点</summary>
        public void RecordSpawn(int groupIndex, int entityId, Vector3 origin)
        {
            _entries[entityId] = new Entry { GroupIndex = groupIndex, Origin = origin };
            _aliveByGroup.TryGetValue(groupIndex, out int count);
            _aliveByGroup[groupIndex] = count + 1;
        }

        /// <summary>
        /// 记录一次死亡：存活 -1，并回吐该实体的归属组与初始生成点。
        /// 未记录的实体返回 false 且不改变任何计数（防重复死亡事件污染计数）。
        /// </summary>
        public bool RecordDeath(int entityId, out int groupIndex, out Vector3 origin)
        {
            groupIndex = 0;
            origin = Vector3.zero;

            if (!_entries.TryGetValue(entityId, out var entry)) return false;

            _entries.Remove(entityId);
            groupIndex = entry.GroupIndex;
            origin = entry.Origin;

            _aliveByGroup.TryGetValue(groupIndex, out int count);
            _aliveByGroup[groupIndex] = Mathf.Max(0, count - 1);
            return true;
        }

        /// <summary>清空全部记账（DespawnAll 用）</summary>
        public void Clear()
        {
            _entries.Clear();
            _aliveByGroup.Clear();
        }
    }
}
```

- [ ] **Step 4: 运行测试验证通过**

运行 `tests-run`，`mode=EditMode`，`testClass=SpawnLedgerTests`
预期：5/5 PASS。

- [ ] **Step 5: 改造 MonsterSpawner 接入 ledger**

修改 `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs`，四处改动：

（a）字段区（`:24-26`）替换为：

```csharp
        private readonly List<MonsterEntity> _aliveMonsters = new();
        private readonly SpawnLedger _ledger = new();
        // 计时器改按实体 id 记（原按 MonsterId 记 → 到期重生整组，是爆炸 bug 的根源）
        private readonly Dictionary<int, float> _respawnTimers = new();
        private readonly Dictionary<int, int> _respawnGroupOf = new();
```

（b）`Spawn`（`:87-134`）—— 改为接收组索引，登记 ledger，并把订阅累积彻底消除。

签名改为三参重载（保留旧两参重载，供外部调用，组索引记 -1）：

```csharp
        public MonsterEntity Spawn(MonsterConfig config, Vector3 position)
            => Spawn(config, position, groupIndex: -1);

        public MonsterEntity Spawn(MonsterConfig config, Vector3 position, int groupIndex)
```

将 `Spawn` 末尾的：

```csharp
            entity.OnDeathComplete += () => HandleMonsterDeath(config, entity);
            _aliveMonsters.Add(entity);
```

替换为：

```csharp
            // 订阅不会累积：MonsterEntity.Init 会先清空自身事件（见 (e)），
            // 每次生成恰好挂一个处理器。原实现每次生成都 += 且从不清，
            // 池化复用后同一只怪死亡会触发多个处理器，放大爆炸 bug。
            entity.OnDeathComplete += () => HandleMonsterDeath(entity.GetInstanceID());

            _aliveMonsters.Add(entity);
            _ledger.RecordSpawn(groupIndex, entity.GetInstanceID(), position);
```

（c）`SpawnGroupInternal`（`:65-85`）—— 传入组索引：

```csharp
                Spawn(config, pos, groupIndex: Array.IndexOf(Groups, group));
```

（d）用下述实现替换 `HandleMonsterDeath`（`:136-148`）：

```csharp
        /// <summary>
        /// P6 ⑤：一次死亡只登记一只待重生（原实现按 MonsterId 记、到期重生整组）。
        /// 重生点 = 该实体的初始生成点（ledger 保存），不是死亡位置（D-P6-13）。
        /// </summary>
        private void HandleMonsterDeath(int entityId)
        {
            if (!_ledger.RecordDeath(entityId, out int groupIndex, out Vector3 origin)) return;

            _aliveMonsters.RemoveAll(m => m == null || m.GetInstanceID() == entityId);

            _respawnGroupOf[entityId] = groupIndex;
            _respawnOrigins[entityId] = origin;
            _respawnTimers[entityId] = Time.time + RespawnDelay;

            // 外部系统注销保留（池化前清理），与既有行为一致
            EntityDisplayManager.Instance?.Unregister(entityId);
            PhysicsRegistry.Instance?.UnregisterByInstanceId(entityId);
        }
```

并在字段区补：

```csharp
        private readonly Dictionary<int, Vector3> _respawnOrigins = new();
```

> `PhysicsRegistry.UnregisterByInstanceId` 若不存在，改为在 `HandleMonsterDeath` 内按 instanceId 反查实体后调既有 `Unregister(entity)`；以实际 API 为准。

（e）在 `MonsterEntity.Init`（`MonsterEntity.cs`）**开头**补一行，保证池化复用时事件被清空：

```csharp
        public void Init(MonsterConfig config, Vector3 position)
        {
            // P6 ⑤：池化复用的对象会带着上一轮的订阅——必须清空，
            // 否则 MonsterSpawner 每次生成 += 会累积成多处理器。
            OnDeathComplete = null;
            ...
        }
```

> `OnDeathComplete` 是 `MonsterEntity` 自身声明的事件，类内可整体赋值；若它被声明为 `public event Action OnDeathComplete;` 则此写法成立。

（d）`Update`（`:42-63`）—— 改为**每次只重生一只**，回到该实体自己的初始生成点：

```csharp
        private void Update()
        {
            if (_respawnTimers.Count == 0) return;

            var toRespawn = new List<int>();
            foreach (var kvp in _respawnTimers)
            {
                if (Time.time >= kvp.Value) toRespawn.Add(kvp.Key);
            }

            foreach (var entityId in toRespawn)
            {
                _respawnTimers.Remove(entityId);
                if (!_respawnGroupOf.TryGetValue(entityId, out int groupIndex)) continue;
                _respawnGroupOf.Remove(entityId);
                if (!_respawnOrigins.TryGetValue(entityId, out Vector3 origin)) continue;
                _respawnOrigins.Remove(entityId);

                if (groupIndex < 0 || groupIndex >= Groups.Length) continue;

                // 只重生这一只，位置 = 它自己的初始生成点（不是整组、不是死亡位置）
                Spawn(Groups[groupIndex], origin);
            }
        }
```

- [ ] **Step 6: 新增 DespawnAll 并接入宿主**

在 `MonsterSpawner` 增加：

```csharp
        /// <summary>
        /// P6 ⑥：彻底销毁本 spawner 的全部怪物（联网模式切走时用）。
        /// 只做 Destroy + 清集合——PhysicsRegistry / EntityDisplayManager 的注销由
        /// MonsterEntity.OnDestroy 负责，在此重复注销有双注销风险（D-P6-14）。
        /// 注意：销毁后本 spawner 不再刷怪；模式在启动时确定、运行期不切换，可接受。
        /// </summary>
        public void DespawnAll()
        {
            foreach (var entity in _aliveMonsters)
            {
                if (entity != null) Destroy(entity.gameObject);
            }

            _aliveMonsters.Clear();
            _pool.Clear();
            _respawnTimers.Clear();
            _respawnGroupOf.Clear();
            _respawnOrigins.Clear();
            _ledger.Clear();
        }
```

修改 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:253-258`：

```csharp
            var localSpawners = FindObjectsOfType<Hotfix.GameSystems.Monster.MonsterSpawner>();
            foreach (var spawner in localSpawners)
            {
                // P6 ⑥：必须真销毁，不能只 SetActive(false)——PhysicsRegistry 只查 IsAlive
                // 不查 activeInHierarchy，停用后怪物仍可被打中，且名牌残影
                spawner.DespawnAll();
                spawner.gameObject.SetActive(false);
                Debug.Log($"[NetHost] 本地 MonsterSpawner 已禁用：{spawner.name}（服务器驱动怪物）");
            }
```

- [ ] **Step 7: 跑全量 EditMode 回归**

运行 `tests-run`，`mode=EditMode`
预期：全绿（含 Task 2/3 新增用例 + `SpawnLedgerTests` 5 个）。

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/SpawnLedger.cs Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs Assets/Scripts/Tests/Editor/SpawnLedgerTests.cs
git commit -m "fix(monster): one death one respawn, and really destroy local spawners

Respawn was keyed by MonsterId and called SpawnGroupInternal, which spawns the
whole group - so killing 1 of 11 slimes added 11 back 30s later, and the alive
count only ever grew. The OnDeathComplete += in Spawn also accumulated across
pool reuse, firing the handler multiple times per death and amplifying it.

Accounting moves into SpawnLedger (pure, EditMode-testable - this bug was an
accounting bug and needs a regression test that PlayMode cannot cheaply
provide). Respawn returns to each monster's initial spawn point rather than
where it died, so chasing a monster away cannot migrate the spawn cluster.

Mode switch now calls DespawnAll instead of SetActive(false): PhysicsRegistry
only checks IsAlive, so deactivated monsters stayed hittable with nameplate
ghosts left behind."
```

---

### Task 6: 伤害结算帧率无关化（②）

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs`（`:286-318` `UpdateExecution`；`TransitionTo`）
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`（`:376-412`）
- 创建：`Assets/Scripts/Tests/Editor/SkillStateMachineHitboxWindowTests.cs`

**机制**：`UpdateExecution` 在判定窗口内**每帧**都 `Invoke(i)`；下游用 `consecutiveRequired = 3` 抵消——60fps 恰好 3 帧结算 1 次，**120fps 6 帧结算 2 次**（伤害翻倍），掉帧跳过窗口则伤害静默丢失。

- [ ] **Step 1: 编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/SkillStateMachineHitboxWindowTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ②：判定窗口触发次数必须与帧率无关。
    /// 原实现窗口内每帧触发，下游靠 consecutiveRequired=3 抵消——60fps 结算 1 次、
    /// 120fps 结算 2 次（伤害翻倍）。
    /// </summary>
    public class SkillStateMachineHitboxWindowTests
    {
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        /// <summary>一个判定窗口落在 0.3s 的连击技能</summary>
        private static ComboSkillData CreateComboWithSingleWindow()
        {
            var shape = new ShapeBlock();
            // 执行期更正：原写 0.05f 与采样起点 Now=0.15f 自相矛盾——窗口 (0.05, 0.10) 落在
            // 采样区间之前，逐字版 60/120/30fps 全部采不到（对本次修复完全不敏感，不是有效
            // 回归测试）。取 0.3f = ShapeBlock 出厂默认值，也与实装资产 BA_Combo1 的
            // _hitboxTimings 同值。判别力来自窗口宽度 max(0.05, 3dt)（未改动），与 timing 偏移量无关。
            TestHelpers.SetField(shape, "_hitboxTimings", new[] { 0.3f });

            var data = ScriptableObject.CreateInstance<ComboSkillData>();
            TestHelpers.SetField(data, "_shape", shape);
            return data;
        }

        /// <summary>以给定帧间隔推进 0.3s，返回判定帧触发次数</summary>
        private static int CountFires(float deltaTime)
        {
            var machine = new FakeTimeMachine(CreateComboWithSingleWindow());
            int fires = 0;
            machine.OnHitboxFrame += _ => fires++;

            machine.TryStart();
            machine.Now = 0.15f;      // 越过 Casting（硬编码 0.1s）进入 Execution
            machine.Update(deltaTime);

            float elapsed = 0f;
            while (elapsed < 0.3f)
            {
                machine.Now += deltaTime;
                machine.Update(deltaTime);
                elapsed += deltaTime;
            }
            return fires;
        }

        [Test]
        public void test_single_window_fires_exactly_once_at_60fps()
        {
            Assert.AreEqual(1, CountFires(1f / 60f), "一个判定窗口只应触发一次");
        }

        [Test]
        public void test_single_window_fires_exactly_once_at_120fps()
        {
            Assert.AreEqual(1, CountFires(1f / 120f),
                "窗口内每帧触发会让 120fps 结算两次——伤害翻倍");
        }

        [Test]
        public void test_fire_count_is_frame_rate_independent()
        {
            // Arrange & Act
            int at60 = CountFires(1f / 60f);
            int at120 = CountFires(1f / 120f);
            int at30 = CountFires(1f / 30f);

            // Assert
            Assert.AreEqual(at60, at120, "60fps 与 120fps 的判定次数必须相同");
            Assert.AreEqual(at60, at30, "60fps 与 30fps 的判定次数必须相同");
        }
    }
}
```

- [ ] **Step 2: 运行测试验证失败**

运行 `tests-run`，`mode=EditMode`，`testClass=SkillStateMachineHitboxWindowTests`
预期：`test_single_window_fires_exactly_once_at_120fps` 与 `test_fire_count_is_frame_rate_independent` **FAIL**（120fps 触发 2 次）；60fps 用例可能已 PASS（这正是 bug 被掩盖的原因）。

- [ ] **Step 3: 给状态机加独立游标**

修改 `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs`。

在 `_currentTick` 字段附近**新增独立字段**（**不复用 `_currentTick`**——它已被 `UpdateCharging` 与 `UpdateSpinning` 两种语义共用，见 D-P6-15）：

```csharp
        /// <summary>
        /// P6 ②：Execution 阶段的判定窗口游标——保证每个窗口只触发一次。
        /// 独立于 _currentTick：后者被 UpdateCharging（蓄力期游标）与
        /// UpdateSpinning（每 tick 自增）共用，复用会让蓄力链 Charging→Execution
        /// 之间串味（蓄力阶段推进的值带入 Execution 会跳过判定窗口）。
        /// </summary>
        private int _executionTimingCursor;
```

在 `TransitionTo`（`:343`）中，进入 Execution 时归零：

```csharp
        private void TransitionTo(SkillSubState newState)
        {
            if (newState == SkillSubState.Execution)
                _executionTimingCursor = 0;   // P6 ②：每次进入 Execution 重新开始判定
            ...
        }
```

- [ ] **Step 4: 改造 UpdateExecution 使用游标**

将 `UpdateExecution`（`:286-318`）中的判定循环替换为：

```csharp
            float[] hitboxTimings = GetHitboxTimings();
            if (hitboxTimings != null)
            {
                float castTime = _channeledData?.CastTime ?? 0f;
                // P6 ②：游标从上次触发的下一个窗口开始，保证每个窗口只触发一次。
                // 原实现在窗口内每帧都 Invoke，靠下游 consecutiveRequired=3 抵消——
                // 60fps 恰好 3 帧结算 1 次，120fps 6 帧结算 2 次（伤害翻倍）。
                for (int i = _executionTimingCursor; i < hitboxTimings.Length; i++)
                {
                    float hitboxTime = castTime + hitboxTimings[i];
                    // 帧率自适应判定窗口：保证低帧率下窗口仍能被采到（保留既有语义）
                    float window = Mathf.Max(0.05f, _lastDeltaTime * 3f);
                    if (Approximately(_elapsedTime, hitboxTime) ||
                        (_elapsedTime > hitboxTime && _elapsedTime < hitboxTime + window))
                    {
                        _executionTimingCursor = i + 1;   // 本窗口已消费，不再触发
                        _onHitboxFrame?.Invoke(i);
                        _onHitConfirm?.Invoke();
                    }
                }
            }
```

- [ ] **Step 5: 删除下游的帧计数补偿**

修改 `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` 的 `OnHitboxTriggered`（`:362-430`）。将 `:376-412` 的整段（`consecutiveRequired` / `_consecutiveHits` / `toRemove` 清理 / 计数判定）替换为：

```csharp
            // P6 ②：删除 consecutiveRequired=3 与 _consecutiveHits。
            // 状态机已保证"每个判定窗口恰好触发一次"，此处只需对本帧检测到的目标各结算一次。
            // 原实现依赖连续 3 帧检测，使结算次数随帧率变化（60fps 1 次 / 120fps 2 次），
            // 且掉帧跳过窗口时会静默丢失伤害。
            var damagedTargets = _cachedDamagedTargets;
            damagedTargets.Clear();
            foreach (var target in targets)
            {
                ApplyDamage(target, frameIndex);
                ApplyEffects(target);
                OnTargetHit?.Invoke(target);
                damagedTargets.Add(target);
            }
```

同时删除**仅**被上述逻辑使用的字段（`_consecutiveHits`、`_cachedToRemove`）——若无其他引用则一并删除；若有其他引用则保留。删除前用 grep 确认引用计数，勿凭感觉删。

- [ ] **Step 6: 运行测试验证通过**

运行 `tests-run`，`mode=EditMode`，`testClass=SkillStateMachineHitboxWindowTests`
预期：3/3 PASS。

- [ ] **Step 7: 跑全量 EditMode 回归**

运行 `tests-run`，`mode=EditMode`
预期：全绿。**特别关注既有 `SkillExecutor*Tests` / `SkillStateMachine*Tests` / `SkillCoordinator*Tests`**——本次改动会改变判定时机（从"窗口内第 3 帧"提前到"窗口进入帧"），既有断言若依赖旧时机需按新语义更新，并在 Task 10 偏差记录登记。

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs Assets/Scripts/Tests/Editor/SkillStateMachineHitboxWindowTests.cs
git commit -m "fix(skills): hitbox window fires once per window, independent of frame rate

UpdateExecution invoked OnHitboxFrame every frame while inside the window, and
the executor compensated with consecutiveRequired=3. That happened to net one
damage per window at 60fps but two at 120fps, and a dropped frame could skip a
window entirely and silently lose the damage.

The state machine now owns the invariant with a cursor, mirroring the pattern
UpdateCharging already uses. The cursor is a new field rather than _currentTick,
which UpdateCharging and UpdateSpinning already share with different meanings -
charged skills run Charging then Execution, so the charge-phase value would have
skipped execution windows.

Note: damage now lands on the window's entry frame instead of its third, so
strikes land marginally earlier."
```

---

### Task 7: 相机接线 + LateUpdate + 防穿墙（③）

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`（`:149` 附近接线；`:209-210` 删除手动 Update）
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs`（`:74` `Update` → `LateUpdate`；`HandleRotationInput` 调用点；防穿墙）

**两个问题**：① `HandleRotationInput`（`ThirdPersonCameraController.cs:60`）与 `InputManager.GetCameraRotationInput()`（`InputManager.cs:109`）**全项目零调用者** → 相机永不旋转。② `Sys3CEntry.cs:209-210` 手动调 `_camera.Update()`，而该类是 MonoBehaviour 且 `public void Update()`（`:74`）→ **每帧更新两次**。

- [ ] **Step 1: 接线旋转输入**

修改 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`。在 `_inputManager.Update()`（`:149`）**之后**加入：

```csharp
            // P6 ③：相机旋转接线——HandleRotationInput 此前全项目零调用者，相机角度永远停在初始值。
            // 键鼠/手柄的观察输入由 InputManager 的 adapter 提供（PC 形态无需触屏适配）。
            if (_camera != null)
                _camera.HandleRotationInput(_inputManager.GetCameraRotationInput());
```

- [ ] **Step 2: 消除每帧双更新，改为 LateUpdate 驱动**

（a）删除 `Sys3CEntry.cs:209-210` 的手动调用：

```csharp
            if (_camera != null)
                _camera.Update();
```

（b）修改 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs`，把 `:74` 的：

```csharp
        public void Update()
```

改为：

```csharp
        /// <summary>
        /// P6 ③：改为 LateUpdate 由 Unity 驱动。
        /// 原来由 Sys3CEntry 手动调用以"保证角色更新完再更新相机"，但该类本身是 MonoBehaviour
        /// 且方法是 Update → 每帧执行两次（插值在低帧手机上行为漂移）。
        /// LateUpdate 由 Unity 保证在所有 Update 之后执行，既保留顺序语义又消除双更新。
        /// </summary>
        private void LateUpdate()
```

> 改 `public void Update()` 为 `private void LateUpdate()` 后，原先的外部调用点（已在 (a) 删除）不再编译——若编译报错说明还有别的调用者，需一并处理。

- [ ] **Step 3: 加相机防穿墙**

在 `ThirdPersonCameraController` 的内部命名空间补 `using UnityEngine;`（若已存在则跳过），并在跟随位置计算完成、写入 `transform.position` **之前**插入：

```csharp
            // P6 ③：防穿墙——从跟随目标向期望相机位投射，命中则把相机拉到碰撞点内侧。
            // 无此处理时贴墙/被怪包围会穿模，PC 上尤其刺眼。
            Vector3 targetPos = _target.position + Vector3.up * _heightOffset;
            Vector3 desired = /* 既有的期望相机位置计算式，勿改其数值 */;
            Vector3 dir = desired - targetPos;
            float dist = dir.magnitude;
            if (dist > 0.01f)
            {
                if (Physics.SphereCast(targetPos, CameraCollisionRadius, dir.normalized,
                        out RaycastHit hit, dist, CameraCollisionMask, QueryTriggerInteraction.Ignore))
                {
                    desired = targetPos + dir.normalized * Mathf.Max(MinCameraDistance, hit.distance);
                }
            }
```

并新增两个常量：

```csharp
        private const float CameraCollisionRadius = 0.25f;
        private const float MinCameraDistance = 0.8f;
        /// <summary>相机碰撞层——需要排除玩家自身与触发器层；在 Inspector 可覆盖</summary>
        [SerializeField] private LayerMask CameraCollisionMask = ~0;
```

> `_heightOffset`、期望位置计算式的实际名称以文件内既有实现为准（`ThirdPointCameraController.cs:74-100` 的 Lerp 跟随逻辑）。**只插入防穿墙段，不改动既有跟随数值**——避免引入未经评审的手感变化。

- [ ] **Step 4: 刷新并跑 EditMode 回归**

运行 `assets-refresh`，然后 `tests-run`，`mode=EditMode`
预期：全绿（相机改动不涉及既有单测）。

- [ ] **Step 5: 手工验证（PlayMode 实机）**

在 Unity 中打开 `DemoDay` 场景并 Play：
预期：① 移动鼠标/手柄右摇杆时相机**能旋转**（此前完全不能）；② 相机移动平滑、**无抖动**（验证 LateUpdate 未破坏顺序）；③ 把角色贴到墙/树旁转动相机，**相机不穿进物体**（验证防穿墙）。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs
git commit -m "fix(3c): wire camera rotation, single update, and collision

HandleRotationInput and InputManager.GetCameraRotationInput had zero callers
anywhere, so the camera angle was pinned at its initial value and the player
could never look around. Sys3CEntry.Update also called _camera.Update()
manually while the controller is a MonoBehaviour with its own Update, running
the interpolation twice per frame.

The manual call existed to guarantee the camera updated after the character;
LateUpdate preserves that ordering without the double run. Adds a SphereCast so
the camera pulls in instead of clipping through geometry."
```

---

### Task 8: 服务器地址可配（3.1）

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（`:141-153` `Connect`）
- 修改：`Assets/Scenes/CharacterSelect.unity`（新增三个 TMP 输入框）

**为什么用游戏内输入**（D-P6-17 + spec §5.1）：收件人联网必须知道服务器地址。配置文件对非技术用户有"找错文件/编码/权限"三重风险；输入框零风险，且 `NetworkModeService` 已用 `PlayerPrefs` 存模式，沿用同一机制。

> **注意**：项目现有代码**没有任何 `InputField` 用法**——这是全新的 UI 组件引入。TMPro 已在用（Nameplate 系统）。

- [ ] **Step 1: 扩展 NetworkModeService**

修改 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs`，追加：

```csharp
        private const string HostKey = "net.host";
        private const string PortKey = "net.port";
        private const string UserKey = "net.username";

        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 8000;
        public const string DefaultUsername = "Player";

        /// <summary>服务器地址（无存档 → 默认值）</summary>
        public static string LoadHost()
            => PlayerPrefs.HasKey(HostKey) ? PlayerPrefs.GetString(HostKey) : DefaultHost;

        public static int LoadPort()
            => PlayerPrefs.HasKey(PortKey) ? PlayerPrefs.GetInt(PortKey) : DefaultPort;

        public static string LoadUsername()
            => PlayerPrefs.HasKey(UserKey) ? PlayerPrefs.GetString(UserKey) : DefaultUsername;

        /// <summary>保存连接参数；端口非法或地址为空时回落到默认值（防手输错误导致无法连接）</summary>
        public static void SaveConnection(string host, string portText, string username)
        {
            string safeHost = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();

            int safePort = DefaultPort;
            if (int.TryParse(portText, out int parsed) && parsed > 0 && parsed <= 65535)
                safePort = parsed;

            string safeUser = string.IsNullOrWhiteSpace(username) ? DefaultUsername : username.Trim();

            PlayerPrefs.SetString(HostKey, safeHost);
            PlayerPrefs.SetInt(PortKey, safePort);
            PlayerPrefs.SetString(UserKey, safeUser);
            PlayerPrefs.Save();
        }
```

- [ ] **Step 2: 选人面板加入地址输入区**

在 `Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs`：

（a）文件顶部补 `using TMPro;` 与 `using Hotfix.GameSystems.SyncLayer;`（`SyncLayer` 若已引用则跳过）。

（b）`[SerializeField]` 区追加三个输入框：

```csharp
        [Header("服务器设置（联网模式用）")]
        [SerializeField] private TMP_InputField _hostInput;
        [SerializeField] private TMP_InputField _portInput;
        [SerializeField] private TMP_InputField _usernameInput;
        [Tooltip("服务器设置整块容器——单机模式下隐藏")]
        [SerializeField] private GameObject _serverSettingsRoot;
```

（c）`OnEnable`（`:43-47` 附近）—— 回填上次保存的值：

```csharp
            if (_hostInput != null) _hostInput.text = NetworkModeService.LoadHost();
            if (_portInput != null) _portInput.text = NetworkModeService.LoadPort().ToString();
            if (_usernameInput != null) _usernameInput.text = NetworkModeService.LoadUsername();
```

（d）`SelectMode`（`:65`）末尾追加显隐：

```csharp
            if (_serverSettingsRoot != null)
                _serverSettingsRoot.SetActive(mode == NetworkMode.Online);
```

（e）`Confirm`（`:85`）—— 落盘连接参数：

```csharp
            NetworkModeService.SaveConnection(
                _hostInput != null ? _hostInput.text : NetworkModeService.DefaultHost,
                _portInput != null ? _portInput.text : NetworkModeService.DefaultPort.ToString(),
                _usernameInput != null ? _usernameInput.text : NetworkModeService.DefaultUsername);
            NetworkModeService.Save(_mode);
```

- [ ] **Step 3: 场景里创建三个输入框并挂上引用**

在 `Assets/Scenes/CharacterSelect.unity` 的选人面板底部、单机/联网按钮下方，新建一个容器 `ServerSettings`，其下建三个 TMP 输入框（`HostInput` / `PortInput` / `UsernameInput`）与配套 `Text` 标签。

用 Unity MCP `gameobject-create` + `gameobject-component-add` 逐个创建，并 `gameobject-component-modify` 把 `CharacterSelectPanel` 的三个引用字段指向它们、把 `_serverSettingsRoot` 指向 `ServerSettings`。

各输入框的 `Content Type`：`HostInput`/`UsernameInput` 用 `Standard`，`PortInput` 用 `Integer Number`。`Character Limit` 依次 64 / 5 / 24。

- [ ] **Step 4: 刷新并验证编译**

运行 `assets-refresh`
预期：编译无错误。

- [ ] **Step 5: ClientNetworkHost 改为每次 Connect 时读取**

修改 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` 的 `Connect`（`:141-153`）：

```csharp
        /// <summary>
        /// 发起登录（幂等）。连接参数**每次调用时**从 PlayerPrefs 读取——
        /// 不能只在 Start() 固化：直接跑 DemoDay 时选人确认发生在 Start() 之后
        /// （见 OnPlayerSpawned 的补连接逻辑），固化会读到旧地址（D-P6-17）。
        /// 显式传入的参数优先，供测试与代码路径覆盖。
        /// </summary>
        public void Connect(string? username = null, string? host = null, int? port = null)
        {
            if (_connectRequested || _remoteEntities == null) return;
            _connectRequested = true;

            string resolvedHost = host ?? NetworkModeService.LoadHost();
            int resolvedPort = port ?? NetworkModeService.LoadPort();
            string resolvedUser = username ?? NetworkModeService.LoadUsername();

            _adapter = new KcpNetClientAdapter();
            _manager = new ClientNetworkManager(_adapter, _remoteEntities, resolvedUser);
            _manager.OnEnterWorld += HandleEnterWorld;
            _manager.OnDisconnected += reason => OnDisconnected?.Invoke(reason);
            _manager.OnError += HandleError;
            _manager.ConnectAndLogin(resolvedHost, resolvedPort);
        }
```

同时把 `Start()`（`:68-72`）与 `OnPlayerSpawned`（`:104-108`）里的 `Debug.Log` 改成打印解析后的值——两处现有日志打印的是 `_serverHost`，会与实际使用值不符：

```csharp
                Debug.Log($"[NetHost] 联网模式——连接 {NetworkModeService.LoadHost()}:{NetworkModeService.LoadPort()}");
```

（两处都要改。）

> `_serverHost` / `_serverPort` / `_username` 三个 SerializeField 由 `PlayerPrefs` 取代后成为死字段。**保留**（只加不删原则，且 Inspector 兜底有用），但在字段上加注释说明它们不再是连接来源。

- [ ] **Step 6: 手工验证**

Play `CharacterSelect` 场景：
预期：① 默认选中"单机"时**看不到**服务器设置区；② 点"联网"后设置区**出现**；③ 三个框回填上次值（首次为 `127.0.0.1` / `8000` / `Player`）；④ 改成 `abc` / `99999` / 空，确认后**不崩溃**，且端口回落 8000；⑤ 重进场景后填的值仍在。

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs Assets/Scripts/Hotfix/GameSystems/UI/Panel/CharacterSelect/CharacterSelectPanel.cs Assets/Scenes/CharacterSelect.unity
git commit -m "feat(net): in-game server address configuration

The host was an Inspector-only field, so a packaged build could only ever dial
127.0.0.1 and nobody outside the editor could point it at a real server. Adds
host/port/username inputs on the character-select panel (shown only in online
mode) persisted through the same PlayerPrefs mechanism the mode already uses.

Connect now resolves these at each call rather than caching in Start - running
DemoDay directly confirms character selection after Start runs, which is the
exact ordering the OnPlayerSpawned reconnect path already exists to handle."
```

---

### Task 9: 断线返回主界面（3.3）

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 修改：选人场景加载逻辑（复用既有场景跳转方式）

**现状**：`OnDisconnected` 事件链路已通（`ClientNetworkHost.cs:56`、`:149`）但**无人订阅**，断线后只能重开进程。

- [ ] **Step 1: 定义返回主界面的场景常量**

在 `ClientNetworkHost` 内新增（**不散落硬编码**，见风险 R9）：

```csharp
        /// <summary>断线返回的目标场景——必须与 BuildScript.Scenes[0] 一致</summary>
        private const string MainMenuSceneName = "CharacterSelect";
```

- [ ] **Step 2: 断线时在屏幕浮层提示并延时返回**

> **已核实的事实**：项目里**没有 Tips / Confirm 面板**（UI 目录下只有 `CharacterSelectPanel` / `PlayerHudPanel` / `TargetPanel`，CLAUDE.md 的描述与代码不符）。因此**不新造面板**，复用 `ClientNetworkHost` 已有的 `OnGUI` 调试浮层（`:172-182`）——零新增 UI 资产，且该浮层本就常驻屏幕。

在 `ClientNetworkHost` 中把断线接线改为走统一处理（替换 `:149` 的匿名 lambda）：

```csharp
            _manager.OnDisconnected += HandleDisconnected;
```

新增字段与成员：

```csharp
        private string? _statusMessage;        // 屏幕浮层上的错误/断线文本
        private float _statusMessageUntil;     // 显示截止时刻（Time.unscaledTime）
        private bool _pendingReturnToMenu;

        /// <summary>
        /// P6 3.3：断线处理——评审看不到 Console，必须有屏幕可见出口。
        /// 先停留 3 秒显示原因（否则立刻跳场景玩家看不到任何反馈），再回选人界面。
        /// 不做自动重连（backlog）。
        /// </summary>
        private void HandleDisconnected(string reason)
        {
            Debug.LogWarning("[NetHost] 断线: " + reason);
            OnDisconnected?.Invoke(reason);
            ShowStatus("与服务器断开：" + reason + "，即将返回主界面");
            _pendingReturnToMenu = true;
        }

        /// <summary>连接失败 / 登录被拒——同样必须有屏幕出口</summary>
        private void ShowStatus(string message)
        {
            _statusMessage = message;
            _statusMessageUntil = Time.unscaledTime + 3f;
        }
```

在 `Update()`（`:126-138`）**开头**加入：

```csharp
            if (_pendingReturnToMenu && Time.unscaledTime >= _statusMessageUntil)
            {
                _pendingReturnToMenu = false;
                Disconnect();                                   // 清理 adapter / 战斗桥 / 装配
                SceneManager.LoadScene(MainMenuSceneName);
                return;
            }
```

> `Update` 在 `_manager == null` 时**提前 return**（`:128`）——上面的代码必须放在该守卫**之前**，否则 `Disconnect()` 置空 manager 后再也不会进入这段逻辑。

- [ ] **Step 3: 连接失败与登录被拒也要有屏幕出口**

把 `HandleError`（`:155-158`）改为：

```csharp
        /// <summary>连接失败/登录被拒——评审看不到 Console，必须显示到屏幕上</summary>
        private void HandleError(string reason)
        {
            Debug.LogError("[NetHost] error: " + reason);
            ShowStatus("连接失败：" + reason + "（可在选人界面修改服务器地址）");
        }
```

- [ ] **Step 4: 浮层显示状态文本（含重连指引）**

修改 `OnGUI`（`:172-182`）——在既有 `GUI.Box` **之后**追加一行错误框：

```csharp
            if (_statusMessage != null && Time.unscaledTime < _statusMessageUntil)
            {
                var style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                GUI.Box(new Rect(0f, Screen.height * 0.35f, Screen.width, 64f), _statusMessage, style);
            }
```

同时把连接失败时的浮层状态补上重连提示：`HandleError` 里若尚未进世界（`!_inWorld`），浮层状态位应能重试——**最小做法**是让玩家手动重进选人界面；若要做"重试"按钮属增强，**不做**（YAGNI）。

- [ ] **Step 5: 手工验证**

（a）单机模式 Play → 不应出现任何连接相关提示（不触网）。
（b）联网模式 Play，服务器**不启动** → 应在数秒内看到屏幕提示（而非只在 Console）。
（c）联网模式 Play，正常连上后**杀掉服务器进程** → 应在 3 秒内看到屏幕提示，随后**自动回到选人界面**；再选一次联网可重新连接（验证 `Disconnect()` 清理干净）。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs
git commit -m "feat(net): surface connection errors on screen and return to menu on disconnect

OnDisconnected had no subscriber, so a dropped connection left the player in a
dead world with no indication and no way back short of restarting the process.
Connection failures likewise only reached the Console, which nobody running a
packaged build ever sees. Both now surface on screen; disconnect returns to the
character-select scene after cleanup."
```

---

### Task 10: 重新出包 + 手工验收 + 文档收尾

**Files:**
- 修改：`docs/superpowers/guides/network-playtest.md`
- 修改：`CLAUDE.md`
- 修改：本计划（回填验收记录）

- [ ] **Step 1: 重新出包**

运行 Unity MCP `script-execute`：

```csharp
Cool.EditorTools.BuildScript.BuildWindows();
```

预期：`[Build] 出包成功: Build/Windows/CoolP6.exe ...`（与 Task 1 基线同样的结果，证明 7 处改动未破坏打包）。

- [ ] **Step 2: 跑全部三套测试**

（a）`dotnet test Server/Server.Tests/Server.Tests.csproj` → 预期 **132/132**
（b）`tests-run`，`mode=EditMode` → 预期全绿（基线 200 + 本计划新增约 12）
（c）`tests-run`，`mode=PlayMode` → 预期全绿（+ `PlayerDeathRespawnTests` 2 个）

- [ ] **Step 3: 手工验收（逐条勾，不可省）**

双击 `Build/Windows/CoolP6.exe`，**在非 Editor 环境下**逐条验证：

| # | 验收项 | 通过判据 |
|---|---|---|
| 1 | 包可运行 | 双击出选人界面，无黑屏/崩溃 |
| 2 | 单机零配置可玩 | 直接确认进世界，能移动、能攻击 |
| 3 | **打怪有反馈** | 打怪掉血、有飘字；怪受击有表现 |
| 4 | **被打有反馈** | 被怪攻击时玩家播受击动画并有**击退位移** |
| 5 | **怪物能打死玩家** | 站着挨打，HP 归零后进入死亡表现、不能移动 |
| 6 | **死后能复活** | 约 3 秒后满血复活、可移动、回到出生点 |
| 7 | **刷怪不爆炸** | 连续打怪 **10 分钟**，观察怪物总数不持续增长（对比 `_aliveMonsters` 或目视） |
| 8 | **相机可转 + 不穿墙** | 鼠标能转视角；贴墙转动相机不穿进几何体 |
| 9 | **联网可配地址** | 选人界面选"联网"→ 出现地址输入区 → 填入错误地址能看到失败提示；填入正确地址能进世界 |
| 10 | **断线有出口** | 联机中杀掉服务器 → 3 秒内屏幕提示 → 自动回选人界面 → 可再连 |

- [ ] **Step 4: 数值节奏复核**

在单机模式下实测并记录：打死 1 只 Slime / TurtleShell 各需几刀、几秒。
预期（按普攻 50 伤害推算）：Slime 100HP → **2 刀**；TurtleShell 250HP → **5 刀**。
若实测与推算差异大，说明还有未发现的数值来源，记入 Step 6 偏差记录。

- [ ] **Step 5: 文档同步**

（a）`docs/superpowers/guides/network-playtest.md` —— 第二节"进游戏选模式"补充：联网模式下需在选人界面填写服务器地址/端口/昵称（默认 `127.0.0.1:8000` / `Player`）；"已知边界"中**删除已修复项**（本地怪物不回收、断线不自动重连改为"断线会返回主界面，但不自动重连"）。

（b）`CLAUDE.md` —— 修正本次工作中**实测证伪**的描述（仅改已确证错误处，不做无关改写）：

| 位置 | 现描述 | 实测 |
|---|---|---|
| 技术栈 | "Hotfix: HybridCLR（代码热重载）" | `Packages/manifest.json` **无 HybridCLR**，`Assets/HybridCLRData` 不存在——那些 `Hotfix.GameSystems.*.csproj` 只是普通 asmdef 程序集 |
| 3C 系统 | "三层 FSM（BaseFSM / AttackFSM / HitFSM）" | **`AttackFSM` 不存在**（`FSM/AttackState.cs` 只剩 2 值枚举），攻击层由 `SkillCoordinator` 驱动 |
| UI 框架 | "UIManager" / "Components: Loading, Toast, Tips, Confirm" | `UIManager` 在 `UI/Core/UIManager.cs`（路径需更正）；**Tips / Confirm / Toast / Loading 组件不存在**（只有 CharacterSelectPanel / PlayerHudPanel / TargetPanel） |

- [ ] **Step 6: 回填执行偏差与验收记录**

在本文件末尾的「执行偏差与验收记录」小节填入：
- 每个 Task 的实际 commit 列表
- 逐任务偏差（凡与计划不符之处，说明现场发现与处理）
- 三套测试的实际通过数
- Step 3 十条验收的实测结果
- 本计划执行中新发现的 backlog 项

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/guides/network-playtest.md CLAUDE.md docs/superpowers/plans/2026-10-07-p6-first-playable-build.md
git commit -m "docs(p6): closeout - playtest guide, CLAUDE.md corrections, acceptance record

Corrects three CLAUDE.md claims that this work proved wrong: HybridCLR is not
installed (the Hotfix.* assemblies are plain asmdefs, so there is no hot
reload), AttackFSM does not exist, and the Loading/Toast/Tips/Confirm UI
components do not exist."
```

---

## 执行偏差与验收记录（执行时回填，Task 10 Step 6）

**Commits**（BASE `4b8f29f` → HEAD）:
T1 `d4d43d9` → T2 `0b20a41`/修复 `554c060` → T3 `2ad0e8c`/改计划 `6277581`/修复 `062dee6`/订正计划 `839e6fb`
→ T4 `bceca76`/改计划 `44dfb49`/门禁 `e8e34f2`/订正计划 `74b84c0` → T5 `727ab77` → T6 `55af995`/订正计划 `5d607d6`
→ T7 `6954a22` → T8 `a011632` → T9 `56b532e`/修复 `6dca21c`/订正计划 `7e68d3f`
→ T10（本轮文档收尾）

**逐任务偏差**（执行时按 P4/P5 惯例记录——implementer 现场发现 + review 批准）：

1. **T1（关键流程偏差）**：计划的出包路径是 MCP `script-execute`，实测该工具**无重试幂等性**——
   `BuildPipeline.BuildPlayer` 阻塞主线程致 HTTP 响应超时，客户端 60s 超时后**自动重试 7 次**，
   构建实际执行 **8 遍**（耗时 39.93s/13.57s/3.70s…；8 次 `summary.totalSize` 完全相同，产物有效）。
   → 结论：命令行/CI 出包不得依赖该路径。T10 据此改走 **batchmode `-executeMethod`**（见偏差 22）。
2. **T1**：提交比计划多含 `BuildScript.cs.meta`（Unity 刷新生成，含 GUID；`.gitignore` 明确要求入库）。
3. **T1**：构建后 Unity VS Editor 包重生成 `.csproj` 时抛 `IOException`（外部进程持文件锁）；
   与构建结果无关，列为环境观察项。
4. **T2**：review 指出测试只锁了 `D-P6-11` 的一半（`AttackEffect.KnockbackForce` 与具体值 1.5/2.5 未断言）
   → 修复轮 `554c060` 两边都钉死。
5. **T2**：`DuoMonsterConfig.asset` 存在同类 0 伤害，但该资产 guid 在场景/Prefab 中引用数 **0**、不在任何刷怪列表
   → 判定非 P6 范围，不修（已记录）。
6. **T2（给 T10 的核对结论）**：DemoDay 两个刷怪组正好指向本次修的两个 `MonsterConfig`
   （SlimeConfig `071183ac` / TurtleShellConfig `f5e15c08`），单机验收路径成立。
7. **T2（承重提示）**：`MonsterConfig.AttackEffect` 在**怪物攻击路径上无消费者**
   （`MonsterAttackState` 只传 `AttackDamage`）——`AttackEffect._knockbackForce` 是"上了膛未接线"。
   单机可见击退只由 `AttackDamage._knockbackForce` 保证（两处都已设 1.5/2.5，故验收成立）。
8. **T3（落地假设被证伪）**：计划的 `new FSMManager(null, null)` **两处 NRE**——
   `FSMManager.cs:36 _animator.SetLayerWeight` 与 `:47 _characterController.SetStateCoordinator`
   （后者是普通 C# 类，非 MonoBehaviour）。最终宿主换为
   `GameObject + Animator + UnityEngine.CharacterController + 真 CharacterController`。
9. **T3（review 抓到 Important，两条路径）**：无条件 `UnlockAndReturnToBase` 会覆盖已走掉的层——
   (a) 受击期间开技能（层→Attack，当前可达）；(b) 致死受击清掉死亡锁（T4 落地后可达，承重）。
   → 人类裁定"两处都修"：T3 加守卫（`062dee6`）+ T4 死亡时调 `HitFSM.EnterDeath()`。计划据此新增
   `D-P6-21`（归还层守卫）+ `D-P6-22`（死亡态分离）。控制者补充：单看 `ActiveLayer==Hit` 挡不住 (b)，
   因为 `HandleDeath()` 自己就 `SetActiveLayer(Hit)`——必须同时判 `HitState.Death`。
10. **T3（计划理由订正）**：`D-P6-21` 的 `CurrentState != Death` 半句经双方独立核实为**当前不可达**
    （`OnHitComplete` 唯一触发点在 `Recover()` 内、已被 `!= Death` 包住）。保留为惰性防御，
    真实防护是 `D-P6-22`；计划理由已订正（`839e6fb`）。
11. **T3（给 T4 的警告）**：计划 T4 测试用 `ApplyServerDamage(9999f)` 杀玩家，会命中
    `HitFSM.cs:212` 的 `Damage>=9999 → Death` **秒杀旁路**（正是 `D-P6-4` 禁止的那条，绕开 `LockState`），
    从而**掩盖 `D-P6-22` 是否生效** → T4 实际改用 **150 vs maxHP 100**，让死亡走正规路径。
12. **T4（关键现场发现，纠正前置结论）**：实现者探针实测发现——死后
    `dead=True hitState=Death layer=Hit` 但 `ccLockMovement=False`，**按住 W 十帧仍位移 0.12m、
    技能仍能起手**（反例成立）。→ 判定为 correctness-bearing，**在 review 前**即处理：
    新增 `D-P6-23`（死亡期移动/战斗输入门禁，`44dfb49`）并派单落地（`e8e34f2`，红→绿 3/3）。
13. **T4**：计划文本 bug——测试片段误写 `_entry.Controller.LockMovement`（`FSMManager` 上无该成员）
    → 订正为 `_entry.CoreController.LockMovement`（`74b84c0`；实现者本来就用对了）。
14. **T4（三项落地偏差，均已披露）**：① 死亡测试伤害 9999f→150f（见偏差 11）；
    ② `CharacterController` 位置写入需 `enabled=false → 写 → true`（Unity 吞掉了裸写）；
    ③ 测试 prefab 从 `FemalePlayer.prefab` 做成 **Prefab Variant**（DemoDay 场景内没有玩家对象，运行时生成）；
    ④ `EvaluateLocalDeath` 接在 **3 个** HP 写入点（计划 2 个 + 盾破早退分支）。
15. **T5（计划的代码不编译）**：计划写的 `Spawn(Groups[groupIndex], origin)` 类型不符
    （`SpawnGroup` 不是 `MonsterConfig`）。若改用两参重载，ledger 会把重生怪记入 `-1` 组
    → **每只怪只能重生一次、刷怪区单调变空**。实际落地 `Spawn(Groups[groupIndex].Config, origin, groupIndex)`
    ——审查者独立推演确认该写法**优于简报字面**，且与"按组记账"一致。
16. **T5**：按控制者指示删掉 2 行手动 unregister 及其孤立 using（订阅累积改由 `MonsterEntity.Init` 清空事件根治）。
    审查者独立验证：唯一订阅者 + 两条生成路径都过 `Init` + 池永不入池；场景内无手摆 `MonsterEntity`
    （guid 全 `*.unity` 检索零命中）→ `DespawnAll` + `OnDestroy` 覆盖全部本地怪物。
17. **T6（测试取值自相矛盾）**：计划的 `_hitboxTimings = 0.05f` 与采样起点 `Now = 0.15f` 冲突——
    窗口落在采样区间**之前**，逐字版对本次修复**完全不敏感**（审查者独立推演确认，并指出报告里的 0/0/1
    应为 0/0/0，实质结论更强）→ 改为 `0.3f`（= `ShapeBlock` 出厂默认 = 实装资产 `BA_Combo1` 同值），
    判别力来自未改动的窗口宽度，测试意图未变。按新语义更新既有断言 2 条（只去掉旧 `consecutiveRequired=3`
    的仪式调用，断言行逐字节相同——审查者逐字节核对确认**不是削弱**）。
18. **T6（⚠️ 承重潜在影响，见 backlog A）**：删除 `consecutiveRequired = 3` 改变了**引导技能
    （ChanneledSkillData）**路径的伤害频率——旧门控要求"连续 3 次判定事件"，即引导技能原本约
    **每 3 个 tick 结算一次**，删除后变成**每 tick 一次（≥3× 伤害）**。当前无可观察影响
    （`Assets/PreRes/SkillsCfg/` 下 6 个技能资产均单窗口且无引导技能），但**引导技能落地时须据此复核**。
19. **T7（工具链选择有证据）**：防穿墙选 `SphereCastAll` + 按层级剔除 Target 自身，因为项目**无可用玩家层**
    ——`TagManager` 自定义层槽 6-31 全空，玩家根在无名层 9，其 57 个子对象与整个 DemoDay 几何同在 `Default(0)`，
    掩码无法区分。审查者核对：既有相机数值（Distance=8/Height=2/PositionDamping=5/RotationDamping=8、
    Lerp/Slerp 式）**逐行一字未动**；取**最小合法 distance** 而非 `hits[0]`（写错会退化成"贴脑后"，此处没写错）。
20. **T7（流程失败，已记录）**：`task-7-brief.md` **未生成**（控制者遗漏），实现者改从派单正文 + 计划原文取需求
    ——结果正确但流程不合规。已补齐 7/8/9/10 四份简报。
21. **T8（计划路径错误 + 工具链陷阱）**：
    ① 计划说改 `Assets/Scenes/CharacterSelect.unity`，实际场景里**根本没有选人面板**——面板经
    `UIManager.RegisterFromPrefab("Prefabs/UI/CharacterSelectPanel")` 从 Resources 加载，
    落点 `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`（改场景会是无效果空改动，未改）。
    ② 刻意扩展：`SelectMode` 之外补 `RefreshServerSettingsVisibility()` 由 `OnShow` 也调一次
    （否则首次打开时设置区保持烘焙态 = 可见）。
    ③ **工具链陷阱**：`script-execute` + `PrefabUtility.SaveAsPrefabAsset` 写 prefab，保存后读回全绿，
    但下一次 `tests-run`（前置 `AssetDatabase.Refresh`）把文件**整体还原成 HEAD 原始字节**（复现 2 次）。
    解法：YAML 落到 `Assets/` 外、外部写入覆盖、再 `assets-refresh`。
    → 已更新项目记忆 `unity-mcp-script-execute-asset-trap`（原只记 SO 资产，现泛化到 prefab）。
22. **T9（review 两处 Important，人类裁定）**：① 连接失败/登录被拒**无返回路径**且文案指向不可达界面；
    ② 文案缺 `<地址>:<端口>`。裁定"加，复用现有机制"——`HandleError` 也置 `_pendingReturnToMenu`，
    复用断线的"提示 3 秒后回选人界面"；仍不做重试按钮。计划据此新增 `D-P6-24`（`7e68d3f`），修复 `6dca21c`。
23. **T10（本轮，出包方式变更）**：计划的 `script-execute` 路径因偏差 1 弃用，改用
    **batchmode CLI**（`-batchmode -quit -executeMethod Cool.EditorTools.BuildScript.BuildWindows`）。
    实测该实例化方式**不能与已打开的编辑器共存**（`Multiple Unity instances cannot open the same project.`
    → `Fatal Error` + 崩溃退出），故流程为：**先跑完三套测试 → 优雅关闭编辑器 → batchmode 出包**。
    出包**单次执行成功**（无重试、无重复构建），耗时 28.23s。
24. **T10（batchmode 首次编译的瞬时假错）**：日志中有一条
    `MonsterSpawner.cs(23,26): error CS0246: 'SpawnLedger' could not be found`——这是增量构建图
    未把 T5 新增的 `SpawnLedger.cs` 纳入响应文件所致；紧接着 Tundra `requires additional run (2.99 seconds,
    59 items updated)` 重跑，第二次编译成功（仅有 warning），构建继续。**产物有效**：
    包内 `GameSys.dll` 334,336 B（比基线 329,216 B **增大** 5,120 B），且用字符串检索在包内确认了
    `SpawnLedger`/`DespawnAll`/`NetworkModeService`/`SaveConnection`/`_pendingReturnToMenu`/
    `EvaluateLocalDeath`/`Physics.SphereCastAll` 七个符号——**9 处改动确实进了包**。
25. **T10（出包前后的陈旧产物防护）**：因偏差 1 的遗留风险（`BuildScript` 失败时不清输出目录，
    旧产物留存 → 人工可能验到陈旧二进制），本轮出包**先把旧产物改名为
    `Build/Windows-baseline-pre-task10/`** 再构建，确保"构建失败 = 目录为空"而不是"留着旧包"。
    新包验证通过后尝试删除该备份目录——**被权限系统拒绝，目录仍在**，已在人工清单 §0 顶部显式警告。
26. **T10（CLAUDE.md 修正范围超出计划表格 2 处，均为实测证伪）**：计划表列了 4 行，实际改到了 **6 处**——
    多出的两处同属"实测证伪"且与原 4 处同一节：
    ① `UIDataBinding` / `ViewModelBase`（UI 框架节）——`grep -rn` 全 `Assets/Scripts` **零命中**，与
    Loading/Toast/Tips/Confirm 同属"文档有、代码无"；
    ② Current Status 行的"三层FSM"（与 3C 节同一错误表述）。
    另核对**未被证伪、故未改**的表述：`StateCoordinator 通过反射调用各 FSM`（`StateCoordinator.cs:69/90/126`
    确有 `GetType().GetMethod(...).Invoke(...)`）；`MovementPolicy`、`EventBus`、`AnimationDriver` 等均在。

**验收证据**（2026-10-07 实测）：

- **重新出包（T10 Step 1）**：`[Build] 出包成功: E:/CodeForJob/Cool/Build/Windows/CoolP6.exe
  size=141067432B time=00:00:28.2327141`，进程退出码 **0**，**单次执行**（batchmode CLI，无重试）。
  包内文件数 **362**（与基线逐文件比对**完全一致**，无缺失/新增）；仅 8 个文件大小变动，
  其中 `GameSys.dll` +5,120 B（代码净增）、`globalgamemanagers` +15,592 B，其余为二进制资产的
  重序列化/压缩波动 → 净 **−23,340 B**（141,090,772 → 141,067,432）。
  → **结论：9 处改动未破坏打包。**
- **Server.Tests**：**132/132**（`dotnet test Server/Server.Tests/Server.Tests.csproj`；与 P5 基线一致，本批次未动服务端）
- **EditMode**：**217/217**（P5 基线 200 + T2 4 + T3 4 + T4 3 + T5 5 + T6 3 − 更新既有 2）
- **PlayMode**：**17/17（三套服务器配置的并集，全绿）**——
  | 服务器配置 | 结果 | 红的用例 |
  |---|---|---|
  | 无服务器 | **5/17** | 12 条网络用例（**既有基线**，非回归） |
  | `--bots 100` | **15/17** | `MonsterWorldTests` ×2（需 `--monsters`） |
  | `--bots 100 --monsters 4` | **17/17** | 无 |
- **PlayMode 口径说明（供后续复跑者）**：单次通跑**拿不到** 17/17——不同用例对服务器配置**互斥**
  （`AoiWorldTests` 要 bots、`MonsterWorldTests` 要 monsters，而 `CombatSyncTests`/`MovementSyncTests`/
  `TwoClientWorldTests` 按消息条数做**全局**断言，有怪物在场即被污染）。故**并集**才是正确口径。
  经 `unity-mcp-cli` 调用需 `--timeout 900000`（默认 60s 会在 Unity 仍在跑时丢结果）。
  跑完已 `taskkill` 服务器进程（两轮，端口 8000 已确认释放）。
- **与计划预期的差异**：计划 T10 Step 2 预期 PlayMode"全绿（+PlayerDeathRespawnTests 2 个）"——
  实际该套件是 **17 条**（不是 2 条），且有 12 条必红（无服务器时）；本批次新增的是
  `PlayerDeathRespawnTests` **3** 条（计划写 2 条）。Step 2(a)(b) 的预期数字（132 / 200+12）与实际
  （132 / 217）一致。
- **已知的既有红测**：`CombatSyncTests.Cheat_Casts_Are_Rejected_Server_Side` 在 `--bots` 配置下曾红
  （P5 偏差 15 登记的"P4 CombatSyncTests 共存鲁棒性"，非本批次引入）；本轮 `--bots 100` 单独跑时它 **PASS**，
  `--bots 100 --monsters 4` 下也 **PASS** → 表现为**偶发**，与 P5 的"共存污染"归因一致。

**T10 Step 3 / Step 4 — 待人工执行**（子代理不能执行：不得启动 GUI 程序、无法注入输入、需目视判断）：
- **Step 3 十条验收**（包可运行 / 单机零配置 / 打怪反馈 / 被打反馈 / 怪物能打死玩家 / 死后复活 /
  刷怪不爆炸 10 分钟 / 相机可转不穿墙 / 联网可配地址 / 断线有出口）
- **Step 4 数值节奏复核**（Slime 应 2 刀、TurtleShell 应 5 刀）
- 连同前 9 个任务累积的人工项（T1 包可运行、T3 受击动画目视、T4 死亡期输入门禁、T7 相机、T8 服务端设置区、
  T9 断线/连接失败/**登录被拒**），已**合并整理为一份可照做的清单**：
  `.superpowers/sdd/2026-10-07-p6-first-playable-build/PENDING-HUMAN.md`（共 **17 项**人工项 + §E 只读参考）。
  该目录 gitignore，不入库。
- **清单是"最终态"**：出包与三套自动化测试均已在生成清单前完成，拿到即可逐条执行。
  ⚠️ 其中"登录被拒"一项需临时改 `ProtocolConstants.ProtocolVersion` 一行（清单 C3 给了
  "先冻结服务器二进制、再改客户端版本"的顺序说明与还原步骤）。

**P6 执行中新发现的 backlog 项**：

- **A（⚠️ 承重）引导技能伤害频率 ×3**：T6 删除 `consecutiveRequired = 3` 后，
  `ChanneledSkillData` 路径的伤害从"约每 3 个 tick 一次"变为"每 tick 一次"。当前不可观察
  （6 个技能资产均单窗口、无引导技能），**引导技能落地时必须据此复核平衡**。
- **B（工具链）`script-execute` 无重试幂等性**：任何客户端自动重试都会重复执行副作用
  （实测出包跑了 8 遍）。长耗时/有副作用的操作应走 batchmode `-executeMethod`。
- **C（工具链）script-execute + PrefabUtility 写 prefab 会被 `AssetDatabase.Refresh` 整体回滚**
  （复现 2 次）；解法是 YAML 落盘在 `Assets/` 外 + 外部覆盖 + `assets-refresh`。已写进项目记忆。
- **D（出包）`BuildScript` 失败时不清输出目录**：旧产物留存会让"人工验收"验到陈旧二进制。
  本轮靠手工改名规避；**建议后续在 `BuildWindows()` 开头清/校验输出目录**（T10 未改代码，仅文档记录）。
- **E（出包）batchmode 与已打开编辑器互斥**：同一工程不能双开（`Multiple Unity instances...` → 崩溃退出）。
  自动化出包流程须先 `unity-mcp-cli close` 优雅关闭编辑器。
- **F（出包）增量构建图可能漏掉新增 .cs**：batchmode 冷启时出现瞬时 `CS0246`（见偏差 24），
  Tundra 自动重跑后自愈。**判据不要只看日志有没有 error，要看最终 `[Build] 出包成功` +
  包内符号检索**；若要在 CI 上 fail-fast，应让构建前强制一次完整编译。
- **G（代码）`MonsterSpawner` 两参 `Spawn` 重载是静默陷阱**：全仓无 C# 调用者，一旦被调用
  怪会记入 `-1` 组 → 死亡后永不重生且**无日志**。建议删重载 / 加 `LogWarning` / 改 `int?`。
- **H（测试）`MonsterSpawner` "重生再死回吐原组"路径无测试锁死**（改回两参重载时 5 条测试仍全绿而刷怪区静默变空）。
- **I（测试）死亡期的施法/攻击门禁无自动化覆盖**（纯输入驱动，PlayMode 不能注入按键）——
  只能人工验（已在 PENDING-HUMAN A5）。
- **J（代码）`HitFSM.Update(float)` 全项目无调用者**：受击状态只能靠动画回调链结束
  （`HitStateBehaviour → FSMManager.HandleHitAnimationCompleted → HitFSM.OnAnimationEnd → Recover`）。
  链路一断角色卡受击态——已写入人工清单 A4 的排查线索。
- **K（代码）`SkillExecutor._consecutiveHits` 成死字段**（T6 删除逻辑的孤儿，两处 `?.Clear()` 恒空操作）；
  `SkillStateMachine.cs:306` 注释丢了唯一的历史凭据（"30fps 下 3×0.0333≈0.1s"）。
- **L（代码）`HitFSM` 秒杀旁路仍可达**：`HandleDamage` 透传原始 damage，使 `HitFSM.cs:212`
  的 `Damage >= 9999 → Death` 旁路（绕开 `HandleDeath` 的 `LockState`）重新可达；当前无生产调用方。
- **M（代码）死亡期若干边角**：`CleanupSkillAnimation` 无条件清 `LockMovement`（引导/蓄力中被杀可位移一帧）；
  `ApplyPushVector`/`ApplyHitDisplacement` 绕过 `LockMovement`（尸体可被推挤）；复活时 `_velocity` 未重置；
  `CharacterData.IsDead` 全程 false（第二个死亡真值源未闭环）；`LockRotation` 未并入 `_isDead`。
- **N（代码）联网失败路径与调试浮层耦合**：T9 的状态框落在 `if (!_showDebugOverlay) return;` 之内——
  关掉调试位会**连带关掉全部屏幕出口**。当前 DemoDay 序列化为开启，属潜伏耦合。
- **O（代码）单一状态槽被覆盖**：登录被拒时 `LoginResponse{Success:false}` 与 `Kick`、`Close` 三事件在途，
  端点文案毫秒级被"与服务器断开"覆盖（返回路径不受影响）。
- **P（代码）`NetworkModeService.SaveConnection` 回落分支无测试覆盖**（与计划钉死的 217 基线冲突的张力已记录）。
- **Q（相机）每帧一次 `RaycastHit[]` 分配**（移动端目标常态 GC）；非玩家重叠体会把相机压到
  `MinCameraDistance`；`CameraCollisionMask = ~0` 含非几何层；`SnapToTarget()` 不含防穿墙。
- **R（数值）怪物 `AttackEffect._knockbackForce` 无消费者**（"上了膛未接线"，见偏差 7）；
  `MonsterConfig.AttackPower` 改动无断言、仅作 `AttackDamage` 为空时的兜底。
- **S（素材）测试专用 `PlayerEntryForTest.prefab` 放在 `Assets/Resources/` → 随 P6 包发布**
  （增量仅一个文本 prefab，审查判定不构成需修缺陷）。
- **T（继承自 P5）**：`CombatSyncTests` 共存鲁棒性（按消息条数全局断言）——见"已知的既有红测"。

---

## Self-Review（撰写自查）

**1. 规格覆盖度**——spec 的 7 处改造点 + 3 件交付物均有对应任务：

| spec 项 | 任务 |
|---|---|
| ④ 单机怪物伤害 + 数值对齐 | Task 2 |
| ① 受击反馈接线（含切层 + 恢复回调） | Task 3 |
| ⑦ 单机本地死亡 / 复活 | Task 4 |
| ⑤ 刷怪爆炸 + ⑥ 幽灵怪清理 | Task 5 |
| ② 伤害结算帧率无关化 | Task 6 |
| ③ 相机接线 + LateUpdate + 防穿墙 | Task 7 |
| 3.1 地址可配 | Task 8 |
| 3.3 断线返回主界面 | Task 9 |
| 3.2 构建脚本（硬门槛） | Task 1（提前到最前，符合 spec §7 的硬要求） |
| §8 测试策略 | 分布在 Task 2/3/4/5/6 的测试步骤 + Task 10 Step 2 |
| §8 手工验收 5 条 | Task 10 Step 3（扩为 10 条，更细） |

**2. 占位符扫描**——已清除。特别说明两处处理：

- Task 9 初稿曾引用 `UIManager.ShowTips` 与 Confirm/Tips 面板，**实测这些 API/资产不存在**，已改为复用既有的 `OnGUI` 浮层，零新增 UI。
- Task 5 初稿曾留一个空壳方法 `OnMonsterDeathComplete()`，已改为通过 `MonsterEntity.Init` 内清空事件来消除订阅累积的干净做法。

**3. 类型一致性**——跨任务引用的符号已核对：

| 符号 | 定义处 | 使用处 |
|---|---|---|
| `SpawnLedger` / `AliveCount` / `RecordSpawn` / `RecordDeath` | Task 5 Step 3 | Task 5 Step 5/6 |
| `HitData`（`Sys3C/FSM/HitFSM.cs:29` 既有 struct） | 既有 | Task 3 Step 3 |
| `FSMManager.HitFSM` / `Coordinator`（既有只读属性 `:25-26`） | 既有 | Task 3 测试、Task 4 |
| `Sys3CEntry.MaxHp` / `CurrentHp` / `Controller` | Task 4 Step 3 | Task 4 测试 |
| `NetworkModeService.LoadHost/LoadPort/LoadUsername/SaveConnection` | Task 8 Step 1 | Task 8 Step 2/5 |
| `MonsterSpawner.DespawnAll` | Task 5 Step 6 | Task 5 Step 6（宿主调用） |
| `_executionTimingCursor` | Task 6 Step 3 | Task 6 Step 4 |

**4. 已知的落地假设（执行时首次接触须确认，均有兜底）**——诚实标注，非占位符：

| 假设 | 若不符的处理 |
|---|---|
| `FSMManager(null, null)` 构造可行（`Animator` 为 null） | 改用 `new GameObject().AddComponent<Animator>()`，`TearDown` 销毁 |
| `DamageBlock.KnockbackForce` 为公开属性 | 加公开只读属性后再跑测试 |
| `PhysicsRegistry.UnregisterByInstanceId` 存在 | 改用既有 `Unregister(entity)`（按 instanceId 反查） |
| `MonsterEntity.OnDeathComplete` 可在类内整体赋 `null` | 改加 `ClearDeathHandlers()` 方法 |
| `TurtleShellConfig` 中 `AttackDamage._knockbackForce` 行号 | 按 `AttackDamage:` 段就近定位（`_baseDamage` 下一行） |
| `ThirdPersonCameraController` 的 `_heightOffset` / 期望位置计算式命名 | 按文件内既有实现取名，**只插入防穿墙段，不改既有跟随数值** |
| `PlayerEntryForTest.prefab` 需自行创建 | Task 4 Step 1 已给创建方式（MCP `assets-prefab-create` + 复制到 `Assets/Resources/`） |

**5. 风险已在计划中的处置**：Task 6 Step 7 明确提示判定时机变化可能影响既有技能测试；Task 9 Step 2 明确提示 `Update` 的 `_manager == null` 提前 return 陷阱；Task 10 Step 1 用"与基线同样出包成功"验证改动未破坏打包。
