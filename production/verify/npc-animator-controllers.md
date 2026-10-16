# DuoPolyart 两只怪真正播动画 —— NPC 词表控制器 + 预制体改线（2026-10-17）

服务端模板 **1 = Slime**、**2 = TurtleShell**（DemoDay 布局里的 4 只怪全在这两个模板下）。
改前状态：两只怪被 `NpcEntityAnimator.ResolveAnimator` 判为"不可驱动"（控制器用美术包自己的词表
`AIState/Speed/Hit/…`，没有 `MonsterState`），整只降级成**只渲染、不动画**。
本次给它们换上新词表的控制器并把预制体改线；驱动代码（`NpcEntityAnimator.cs`）一行未改。

## 1. 交付物

| 项 | 路径 | 说明 |
|---|---|---|
| 控制器 | `Assets/Monstor/DuoPolyart/Animators/NpcAnimator_Slime.controller` | 新，GUID `3a7bfa23448380b41aae14eddc158ee1` |
| 控制器 | `Assets/Monstor/DuoPolyart/Animators/NpcAnimator_TurtleShell.controller` | 新，GUID `bc07b07dfdf37214aad05612b40ae439` |
| 预制体 | `Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab` | `Animator.m_Controller` 改线（1 行） |
| 预制体 | `Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/TurtleShellPBR.prefab` | 同上 |
| 契约测试 | `Assets/Scripts/Tests/Editor/SyncLayer/NpcAnimatorAssetTests.cs` | 新增 1 条用例 + 1 个数据表 + 1 个驱动 helper |

**未动**：`Slime.controller` / `TurtleShell.controller`（美术包词表的原件，留给别处用）、
`NpcEntityAnimator.cs`、DemoDay 场景、布局/出生点、8000 上的服务端。

## 2. 控制器结构（与 `NpcAnimator_DogKnight.controller` 逐条一致）

两个控制器**结构完全相同**，只有绑定的片段不同。

参数（3 个，名字与类型是硬契约 —— 驱动用 `Animator.StringToHash` 写它们）：

| 参数 | 类型 |
|---|---|
| `MonsterState` | Int |
| `AttackVariant` | Int |
| `Attack` | Trigger |

单层 `Base Layer`，默认状态 `Idle`，7 个状态：

| 状态 | 出边 | 绑定片段（Slime / TurtleShell） |
|---|---|---|
| `Idle` | 无 | `IdleBattle`（0.800s，循环） |
| `Walk` | 无 | `WalkFWD`（1.000s，循环） |
| `Run` | 无 | `RunFWD`（Slime 0.667s / TurtleShell 0.533s，循环） |
| `Attack01` | exit-time 0.95 → `Idle`（0.1s） | `Attack01`（0.833s） |
| `Attack02` | exit-time 0.95 → `Idle`（0.1s） | `Attack02`（0.833s） |
| `Hit` | **无**（受击/死亡不放出边，同 DogKnight） | `GetHit`（0.833s） |
| `Death` | **无** | `Die`（Slime 1.333s / TurtleShell 2.000s） |

AnyState 转移 7 条（全部 `hasExitTime=false`、`canTransitionToSelf=false`、`hasFixedDuration=true`）：

| 目的 | 条件 | 时长 |
|---|---|---|
| `Idle` | `MonsterState Equals 0` | 0.15s |
| `Walk` | `MonsterState Equals 1` | 0.15s |
| `Run` | `MonsterState Equals 2` | 0.15s |
| `Hit` | `MonsterState Equals 4` | 0.10s |
| `Death` | `MonsterState Equals 5` | 0.10s |
| `Attack01` | `AttackVariant Equals 0` **且** `Attack (If)` | 0.10s |
| `Attack02` | `AttackVariant Equals 1` **且** `Attack (If)` | 0.10s |

> **`MonsterState == 3` 本身不驱动任何转移** —— 攻击是 **Trigger 门槛**（与 DogKnight 同语义：
> 驱动 `PlayAttack` 先写 3、再写 `AttackVariant`、最后打 `Attack` trigger）。只写 3 什么都不会发生，
> 这是设计而不是缺陷（探针里两条路都量了，见 §5.2）。

## 3. 剪辑映射表（缺项替代：**无**）

DuoPolyart 每个 FBX **单片段**，7 个状态要的片段全部齐备，**没有任何替代**：

| 状态 | 用的 FBX | 片段真名 | 长度 | 循环 |
|---|---|---|---|---|
| Idle | `IdleBattle_*_Anim.fbx` | `IdleBattle` | 0.800s | 是 |
| Walk | `Walk_*_Anim.fbx` | `WalkFWD` | 1.000s | 是 |
| Run | `Run_*_Anim.fbx` | `RunFWD` | 0.667 / 0.533s | 是 |
| Attack01 | `Attack01_*_Anim.fbx` | `Attack01` | 0.833s | 是* |
| Attack02 | `Attack02_*_Anim.fbx` | `Attack02` | 0.833s | 是* |
| Hit | `GetHit_*_Anim.fbx` | `GetHit` | 0.833s | 是 |
| Death | `Die_*_Anim.fbx` | `Die` | 1.333 / 2.000s | 否 |

\* 攻击片段自带 `m_LoopTime: 1`，但攻击态靠 exit-time 0.95 的出边一定退回 `Idle`，不会真循环
（同 DogKnight 的既有形态；`Attack_States_Return_Via_Exit_Time` 守着这条）。

取 `IdleBattle`（而不是 `IdleNormal`，1.667s）是与 DogKnight 的 `Idle_Battle` 语义对齐 —— **战斗待机**。
未被使用的库存片段：`IdleNormal / Dizzy / Taunt / Victory / WalkBWD / WalkLeft / WalkRight /
SenseSomethingST / SenseSomethingRPT`（驱动词表里没有对应状态，登记在此以免下次当成漏配）。

## 4. 预制体改线证据

`script-execute` + `PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset`，改完 `assets-refresh`
后从磁盘读回（git diff 各 **1 行**，无其它字段漂移）：

```
SlimePBR.prefab:468
-  m_Controller: {fileID: 9100000, guid: 9bbcf96c9a254c5ba654027c656d7b39, type: 2}   # 美术包 Slime
+  m_Controller: {fileID: 9100000, guid: 3a7bfa23448380b41aae14eddc158ee1, type: 2}   # NpcAnimator_Slime

TurtleShellPBR.prefab:559
-  m_Controller: {fileID: 9100000, guid: 1f68948a5bb74f2fa9e9087a872750e9, type: 2}   # 美术包 TurtleShell
+  m_Controller: {fileID: 9100000, guid: bc07b07dfdf37214aad05612b40ae439, type: 2}   # NpcAnimator_TurtleShell
```

改线脚本回读（同一调用内）：`old=Slime -> new=NpcAnimator_Slime rootMotion=False driverOnPrefab=False`、
`old=TurtleShell -> new=NpcAnimator_TurtleShell rootMotion=False driverOnPrefab=False`
（`applyRootMotion` 本来就是 0，未改；预制体上依然没有 `NpcEntityAnimator`——驱动由 registry 实例化后 Add）。
`assets-refresh` 之后重新读盘，GUID 未回滚。

### 4.1 顺带记录：`.controller` 写回的"陈旧实例"陷阱（本次踩到，已修复）

`animator-create` + `animator-modify` 之后**磁盘字节正确、Unity 侧实例是空壳**：
`LoadAssetAtPath<AnimatorController>` 返回 `parameters=0 states=0`（域重载/`tests-run` 的
刷新之后才暴露，恰好把第一次全量 EditMode 打成 1 红：`参数数 Expected: 3 But was: 0`）。
`ImportAsset(ForceUpdate)` 无效。用既有记录里的**唯一可靠解法**修好：

1. 把好字节**拷到新路径**（不拷 `.meta`）→ `assets-refresh` → 新 GUID 导入（验证：`params=3 states=7 any=7`）；
2. `AssetDatabase.DeleteAsset(目标)` → `AssetDatabase.MoveAsset(新路径, 目标)`。

**副作用：两个控制器的 GUID 变了**（所以 §4 的预制体引用是修复后的新 GUID，不是首次生成的）。
修复后再跑全量 EditMode + 进出 PlayMode，视图保持正确。

## 5. 验证

### 5.1 EditMode

| 运行 | 结果 |
|---|---|
| `tests-run EditMode testClass=NpcAnimatorAssetTests` | **12/12 通过**（该文件 11 条 + 新增 1 条） |
| `tests-run EditMode`（全量，修复后） | **392 总数 / 392 通过 / 0 失败**（基线 391 + 新增 1） |
| `tests-run EditMode`（跑完 Play 探针、退出 Play 之后再跑一次） | **392/392，0 失败** |

新增用例 `NpcAnimatorAssetTests.Slime_And_TurtleShell_Controllers_Speak_The_Driver_Vocabulary` 断的是
本次的硬契约（改名/换片段/改语义都会红）：3 个参数名与类型逐字、单层 `Base Layer`、默认 `Idle`、
7 个状态名、**状态→片段名逐条**、locomotion 三态绑循环片段、AnyState 的 7 条目的/阈值/时长/
`canTransitionToSelf=false`、`Hit`/`Death` 无出边、攻击 exit-time 回 `Idle`、
**预制体已指向新控制器且关根运动**，最后**真驱动一遍**（6 个值 × 2 只怪各一台干净 Rig）。

### 5.2 Play 探针（真机、8000 在跑、`--monsters 4 --bots 40 --bots-radius 60`）

场景 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，进 Play 等 10s 生成 NPC 视图后开始。
探针通过反射读驱动私有字段 `_animator`（非 null = 未被降级）。

**① 识别为可驱动（4 个视图全部）**：

```
view=SlimePBR_2000002_T1        drivable=True ctrl=NpcAnimator_Slime        layers=1 clip=RunFWD isName=Run| params=[MonsterState:Int,AttackVariant:Int,Attack:Trigger,]
view=SlimePBR_2000003_T1        drivable=True ctrl=NpcAnimator_Slime        layers=1 clip=RunFWD isName=Run| params=[MonsterState:Int,AttackVariant:Int,Attack:Trigger,]
view=TurtleShellPBR_2000001_T2  drivable=True ctrl=NpcAnimator_TurtleShell  layers=1 clip=RunFWD isName=Run| params=[MonsterState:Int,AttackVariant:Int,Attack:Trigger,]
view=TurtleShellPBR_2000004_T2  drivable=True ctrl=NpcAnimator_TurtleShell  layers=1 clip=RunFWD isName=Run| params=[MonsterState:Int,AttackVariant:Int,Attack:Trigger,]
```

**② 五个参数值 → 状态断言表**（每条都在 4 个视图上读；`IsName(...)` 的结果）：

| 注入（等价于驱动写的参数） | Slime ×2 | TurtleShell ×2 | 结论 |
|---|---|---|---|
| `MonsterState=0` | `Idle` | `Idle` | ✅ |
| `MonsterState=2` | `Run` | `Run` | ✅ |
| `MonsterState=3` + `AttackVariant=0` + `Attack` trigger | `Attack01`（t=0.30s、0.60s 两次采样） | `Attack01`（同） | ✅ 且 t=1.00s 已按 exit-time 回 `Idle` |
| `MonsterState=3` + `AttackVariant=1` + `Attack` trigger | `Attack02`（t=0.30s、0.61s） | `Attack02`（同） | ✅ 且 t=1.00s 回 `Idle` |
| `MonsterState=4` | `Hit` | `Hit` | ✅ |
| `MonsterState=5` | `Death` | `Death` | ✅ |

读数形式：注入与读数分成相邻两次调用（**自然帧**，不手动 `Animator.Update` —— 实测 Play 中手动
`Update` 不可靠），读取时同时打印参数真值（`GetInteger("MonsterState")`）确认注入未被服务端快照改写。
攻击两条用 `EditorApplication.update` 回调在 **+0.10/0.30/0.60/1.00/1.60s** 各采样一次：

```
[SCHED] t=0.10 variant=0  四视图=Death|         （上一轮的 Death 还没被覆盖）
[SCHED] t=0.30 variant=0  四视图=Attack01|
[SCHED] t=0.60 variant=0  四视图=Attack01|
[SCHED] t=1.00 variant=0  四视图=Idle|          （0.95 exit-time 回 Idle ✅）
[SCHED] t=0.30 variant=1  四视图=Attack02|
[SCHED] t=1.00 variant=1  四视图=Idle|
```

**③ 自然发生（无注入）的观察**：

- 进 Play 第 10s 的第一帧读数：**4 只怪全部处于 `Run`**（`RunFWD`）—— 巡逻/追击中的真实移动动画；
- 20s 窗口（重置到驱动最后写过的 `Ms=2` 后不再注入）：全部经历 `Idle → Run` 并稳定在 `Run`；
  另一段 35s 窗口：全程 `Run`。**两个窗口共约 55s 内没有等到一次自然攻击**（服务端的怪一直在追击
  目标、没有进入攻击窗口），所以 `Attack01/02` 的自然发生本次未采到 —— 只有注入形式（②）的证据。
- 剪辑**真的在推进**（不是定格）：0.5s 后重采样 `normalizedTime`：
  `Slime clip=RunFWD->RunFWD speed=1 120.719 -> 121.959 (delta=1.241)`、
  `TurtleShell clip=RunFWD->RunFWD speed=1 150.899 -> 152.450 (delta=1.551)`。

探针跑完已**退出 Play**，`DemoDay` 场景 `IsDirty=false`（未触碰场景字段）。

## 6. 疑虑 / 遗留

1. **GUID 变更**：§4.1 的修复让两个控制器换了 GUID（预制体引用同步更新）。若别处（预制体变体、
   Addressables 条目、文档）还按首次生成的 GUID 引用过这两个控制器，需要跟着更新 —— 本次全仓只有
   两个预制体引用它们。
2. **自然攻击未采到**：55s 观察窗口内服务端的怪没有发动攻击，`Attack01/02` 只有注入证据。
   想要自然证据，可让服务端把怪的移动速度/攻击节奏调成容易接战（**不改**，只是记录）。
3. `MonsterState == 3` 单独写不触发攻击（Trigger 门槛，同 DogKnight）。若将来有人想改成"写 3 就播"，
   会同时改坏忙位语义（`_busy` 依赖 `PlayAttack` 起手），别改。
4. `Hit` 状态无出边 + `GetHit` 是循环片段 —— 与 DogKnight 同形态，兜底在**服务端**硬直窗口到期时
   补写状态（见 `NpcAnimator_DogKnight.controller.md` 的 I1 修正）。本次未动服务端。
