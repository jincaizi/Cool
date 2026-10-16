# NPC 接入后的"警告刷屏"与"模型陷地" — 复核与修复报告

日期：2026-10-16 ｜ 场景：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 的 `ClientNetworkHost._npcPrefabs`
（模板 1 Slime → `SlimePBR.prefab`、模板 2 Wolf → `TurtleShellPBR.prefab`，两条都是 D-P5-7 的补接）
手段：`unity-mcp-cli run-tool script-execute`（编辑态探针，主线程）＋ 离屏/日志实测 ＋ EditMode 全套

## 0. 结论摘要（TL;DR）

| # | 原始判断 | 实测结论 | 动作 |
|---|---|---|---|
| 1 | 控制器是**空壳**（`layers=0`），每帧抛 NRE + 刷 `Parameter 'Hash …' does not exist` | **半对**：刷屏是真的（玩家日志 171 条 `Hash 54231900`）；但两个控制器其实**各有 1 层 7 参数**，缺的是 `MonsterState` 词表。"空壳"读数是**在未激活/预制体资产的 Animator 上读 API** 得到的假象（实测 inactive 时 `layerCount=0 parameterCount=0`） | **已修**：`ResolveAnimator` 增加"控制器缺驱动参数 ⇒ 整只降级为只渲染"的守卫（保护集中一处），并补 1 条回归用例 |
| 2 | 两个模型脚底在原点之下（`boundsMinY=-0.375 / -0.363`）⇒ 摆在 y=0 会陷地 | **不成立**：`renderer.bounds` 在蒙皮网格上是**滞后的 AABB**，与真实渲染几何无关。两条独立算法（GPU 同款 bindpose×bone 蒙皮公式 / `BakeMesh`×`SMR.localToWorld`）测得脚底世界高度：Slime **0.00000**、TurtleShell **−0.00309**（DogKnight 对照 **0.00000**）——**已经在原点** | **不改资产**：预制体与 HEAD 逐字节一致（`git status` 无 diff）。原方案"偏移渲染网格子物体"实测是**空操作**；若改偏移骨骼根反而会整体抬升 0.375m（真回归，且会被动画每帧覆盖） |

交付：2 个文件（1 处代码修复 + 1 条回归用例）；EditMode **386/386 绿**。

---

## 1. 缺陷 1：每帧刷 `Parameter 'Hash 54231900' does not exist`

### 1.1 玩家实测证据（既有日志，未改动）

`production/verify/nre-logs.txt`（2026-10-16 23:21 实机运行）：

- `Warning "Parameter 'Hash 54231900' does not exist."` **×171**，栈：`NpcEntityAnimator.Write → ApplyState → RemoteEntityRegistry.UpdateRender`；
  另 `Hash 1929421531`（AttackVariant）×13、`646380074`/`1270110494`（玩家 Character3C 的 Death/IsDead，**不在本次范围**）。
- 该日志里唯一的 `NullReferenceException` 出自 `MonsterEntity.TakeDamage`（本地怪被技能打），**与 Animator 无关**——"空控制器会抛 NRE"与此日志对不上号。
- `54231900 = Animator.StringToHash("MonsterState")`（同日志 `hashCheck` 行可证）。

### 1.2 探针实测（本次）

```
Slime.controller        layers=1  params=7  [AIState:Int, Attack:Trigger, AttackIndex:Int, Hit:Trigger, Death:Trigger, Taunt:Trigger, Speed:Float]
TurtleShell.controller  layers=1  params=7  [AIState:Int, Attack:Trigger, AttackIndex:Int, Hit:Trigger, Death:Trigger, IsDefending:Bool, Speed:Float]
```

- 两个控制器是**美术包自带的 AI 控制器**（词表 `AIState/Speed/…`），**没有** `MonsterState`/`AttackVariant` ⇒ 驱动照写就每帧刷警告。
- "空壳（layers=0 params=[]）"的来源已定位：`Animator.layerCount/parameterCount` 在**未激活的 GameObject**上读作 0
  （实测：`active layerCount=1 paramCount=3` → `inactive layerCount=0 paramCount=0`）。在**预制体资产**上读同一 API 也是 0/0（Animator 未初始化）。
  即：旧探针混用了"控制器资产 API"（DogKnight 行）与"未激活 Animator API"（Slime/TurtleShell 行）。

### 1.3 修复

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs`：

- `ResolveAnimator()` 里把"不可驱动"的三种情形**统一降级**为 `_animator = null`（`Write`/`PlayAttack` 维持原有 `_animator == null` 守卫，**未新增**分支）：
  没有控制器；`layerCount == 0`（空壳，SetInteger 会 NRE）；**控制器缺 `MonsterState`/`AttackVariant`/`Attack` 任一参数**（← 本次真因；`HasDriverParameters` 读控制器资产数据，与宿主激活状态无关）。
- XML doc 补齐前提说明；不动忙位、状态映射、`_lastWritten` 去重等任何既有行为。
- 回归用例：`NpcEntityAnimatorTests.A_Controller_Missing_The_Driver_Params_Is_Not_Written_To`
  （有层、参数只配 1/3 的控制器上，`ApplyState(Run)` 后 `MonsterState` 必须保持默认 0 = 整只拒绝写入）。

### 1.4 端到端验收（真实预制体实例，EditMode）

```
VERIFY-DRIVER loadedAsm=GameSys
VERIFY-DRIVER SlimePBR(Clone)        _animator=<null> 降级(只渲染) 写参结果: -
VERIFY-DRIVER TurtleShellPBR(Clone)  _animator=<null> 降级(只渲染) 写参结果: -
VERIFY-DRIVER DogKnightMonster(Clone) _animator=非空(可驱动) 写参结果: MonsterState=3   ← 非降级对照，行为不变
```

配合 `console-clear-logs` 后重放 `ApplyState/PlayAttack`：**"does not exist" 警告 = 0 条**（修前同一路径 171 条/次）。

---

## 2. 缺陷 2：模型"陷地"复核 —— 前提不成立，资产无需改动

### 2.1 两个仪器，一张表（复核后，`assets-refresh` 之后实测）

| 模型 | `renderer.bounds.min.y`（旧判据，**不可信**） | 蒙皮脚底世界高度 (bind pose) | 蒙皮脚底（默认片段采样 t=0 / t=L/2） |
|---|---|---|---|
| `SlimePBR.prefab` | −0.37505 | **0.00000** | 0.00000 / 0.00033 |
| `TurtleShellPBR.prefab` | −0.36288 | **−0.00309** | −0.00309 / +0.00760 |
| `DogKnightMonster.prefab`（对照） | −0.06288 | **0.00000** | +0.00658 / +0.00153 |

- 两个独立算法互相印证到 5 位小数，两个都是逐帧 GPU 同款语义：
  ① 蒙皮公式 `world = Σ wᵢ · (boneᵢ.localToWorld × bindposeᵢ) · v`；② `SkinnedMeshRenderer.BakeMesh()` 的顶点 × `SMR.localToWorldMatrix`。
- 为什么 `renderer.bounds` 骗人：蒙皮网格的 AABB 只在**渲染时**随姿态刷新；编辑态读到的是陈旧值，且**对 transform 变化不响应**（实测：移动 SMR 物体 ±0.375、移动骨骼根 `Body` ±0.375，`bounds.min.y` 都纹丝不动）。
  **⇒ 蒙皮模型上不要拿 `renderer.bounds` 做"脚底对原点"的验收值。**
- `applyRootMotion=False`（两个预制体都关，和 `NpcAnimatorAssetTests` 对 DogKnight/Spider 的要求一致）；根 scale=1。

### 2.2 原方案"偏移渲染网格子物体"为什么是空操作（实测）

两者的渲染网格子物体就是 `SkinnedMeshRenderer` 所在节点（`Slime` / `TurtleShell`），而**蒙皮顶点由骨骼矩阵决定，渲染结果与 SMR 自身 transform 无关**：

```
clip + SMR.localPosition.y +0.375 : bake×SMR矩阵 minY = 0.0003 → 0.0003   （世界几何完全不动）
clip + Body.localPosition.y +0.375: bake×SMR矩阵 minY = 0.0003 → 0.3753   （骨骼根才是渲染锚点）
Body 偏移后再采样片段             : 回到 −0.5550（与未偏移一致）—— 静态骨骼偏移会被动画每帧覆盖
```

- 骨骼结构：`rootBone = Body`（Slime bones=10 / TurtleShell bones=30），`Body` 是骨骼树根；
  **每一帧的每个动画片段都对全部骨骼做 `m_LocalPosition.y` 关键帧**（Slime 8 条路径、TurtleShell 16 条，都含 `Body`）。
- 所以：① 偏移 SMR 节点 = 白改；② 偏移 `Body` = 模型整体悬空 0.375m，且播放动画时会被覆盖成"时灵时不灵"。
- 偏移的是**哪一级**（如果当初真要做）：只能是骨骼根 `Body`；安全性依据只有"当前控制器不驱动动画"这一条——而本报告已证明这条也不成立（美术包控制器自带 IdleNormal 等片段，Play 时会播）。

### 2.3 那"陷地"的观感可能来自哪？

本次范围内**没有**模型级偏移可做。若实机上确实看到脚陷进地面，最可能的来源是**放置高度**（服务器快照 y 与地形高度不匹配），而非预制体：
同批探针（`GroundProbe`，见 `production/verify/nre-logs.txt`）实测槽位地面高度 0.07–0.28m，而怪物根节点按 y=0 落位时，脚会埋 0.07–0.28m。
这属于**布局/出生点待议项**，本报告不越界处理。

---

## 3. 命令与输出（可复现）

```bash
# 1) 编译 C# 修复（并复核资产未被回滚）
unity-mcp-cli run-tool assets-refresh --path "E:/CodeForJob/Cool" --input '{"options":"ForceSynchronousImport"}' --timeout 300000
#   → [Success] Assets refresh completed: AssetDatabase

# 2) 真值探针（脚本见 %TEMP%/npcfix/*.cs，全文内联于 run-tool 的 csharpCode，isMethodBody=true）
unity-mcp-cli run-tool script-execute     --input-file <probe.json> --timeout 300000
unity-mcp-cli run-tool console-get-logs   --input '{"maxEntries":30,"lastMinutes":3}'
#   → VERIFY-FOOT / VERIFY-DRIVER 行（见 §1.4 / §2.1）

# 3) 回归 + 全量 EditMode
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode"}' --timeout 600000
#   → Status=Passed  TotalTests=386  PassedTests=386  FailedTests=0  SkippedTests=0
#     （385 条既有用例全绿 + 新增 1 条回归用例；原始输出 production/verify/npc-anim-and-ground-fix-tests.txt）

# 4) 资产无足迹复核
git status --short -- Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/      # → 空（与 HEAD 逐字节一致）
```

## 4. 遗留 / 疑虑（不含本次改动）

1. **"陷地"观感的真实来源**：见 §2.3，指向放置高度/地形（布局待议项）。本报告只能证明"模型自身脚底在原点"。
2. `NpcBodyCollider`（运行时按模型 bounds 生成碰撞体）若同样取 `renderer.bounds`，会拿到同一个滞后 AABB——建议单独复核它的量测口径（本次未动）。
3. 这两个 NPC 现在是"**只渲染、不动画**"（驱动降级 + 美术包控制器缺词表）；要动起来需把它们指向 `NpcAnimator_*` 词表的控制器或另配。这是任务既定方向，非本次新增行为。
4. 玩家 `Character3C` 控制器缺 `Death`/`IsDead` 参数（本批日志里各 1 条警告）——CLAUDE.md 已记为 backlog，未处理。
5. 旧探针结论（"空壳控制器"）的判断方法已在 §1.2 说明；后续在**未激活对象/预制体资产**上读 `Animator.layerCount/parameterCount` 都会得到 0，别据此判断控制器内容。

## 5. 变更文件

```
M Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs     （降级守卫 + 前提注释）
M Assets/Scripts/Tests/Editor/SyncLayer/NpcEntityAnimatorTests.cs      （回归用例 +34 行）
```

资产（`SlimePBR.prefab` / `TurtleShellPBR.prefab`）**零改动**；另新增本报告与配套测试日志。
