# 远端实体表现统一与表示分级 —— 设计

日期：2026-10-13
状态：**设计已定稿，待实现**
上游：[P3 AOI 大世界](../plans/2026-09-10-p3-aoi-world.md)、[P5 怪物服务端化](../plans/2026-09-12-p5-monster-server.md)、[移动/碰撞/避让报告](../plans/2026-10-13-spawn-and-collision-report.md)、[WebGL 移动端验收](../guides/webgl-mobile-acceptance.md)

## 0. 前置结论（本设计的事实基础）

以下事实均在 2026-10-13 实测或读码核实，每条带出处。后面的决策全部建立在这些之上。

### 0.1 客户端现状

| # | 事实 | 出处 |
|---|---|---|
| F1 | 远端实体分三支：真人 → 真角色预制体；怪物 → 真模型 + 驱动 + 碰撞体；**bot → `CreatePrimitive(Capsule)`，无动画** | [RemoteEntityRegistry.cs:200-260](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs) |
| F2 | 场景里 `ClientNetworkHost._playerAvatarPrefab` 指向 `MaleCharacterPBR.prefab`——它是 **3C 全装备预制体**（16 个 MonoBehaviour + `CharacterController` + 4 个 SMR），远程驱动停掉本地行为后靠快照驱动 | [DemoDay.unity:7434](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity) |
| F3 | **男女两套全装备预制体都已存在**（`MaleCharacterPBR` / `FemalePlayer`），共用同一个 3C 控制器（guid `fb7ffdef…`）⇒ bot 混用男女**不需要新资产** | 预制体结构比对（各 16 组件 / 4 SMR / 同控制器） |
| F4 | RpgDuo 的 PBR/Polyart **原始**预制体（0 个 MonoBehaviour、包自带控制器）不在此列——没有 3C 控制器，驱动参数对不上 | 同上 |
| F5 | 远端真人的动画只驱动受击层——**没有任何人根据速度写行走动画**，真人在滑行 | [RemoteEntityAnimator.cs:92-104](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs) |
| F6 | 本地行走动画契约：`Blend`（float 0/0.5/1，Idle/Walk/Sprint 混合；**由输入幅度与冲刺开关**决定）、`BaseState`（int；移动态 `Locomotion=7`）、`LocomotionSpeed`（float，**步频倍率**——举盾写 0.4、平时 1，注释原文"同步放慢走步循环，避免脚步滑步"） | [CharacterController.cs:376-391](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs)、[FSMManager.cs:85-93](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs) |
| F7 | 速度常量：`MoveSpeed = 5.0`、`SprintSpeed = 8.0`（m/s）。bot 速度 `0.53 / 0.066 ≈ 8.03 m/s` | CharacterController.cs:14-15、[BotRunner.cs:16](../../../Server/Aoi/BotRunner.cs) |
| F8 | 快照**恒发全掩码**（含 `Speed`，m/s），但**本设计不消费它**——行走速度改由渲染位移反推（D17） | [AoiBroadcaster.cs:63-65](../../../Server/Aoi/AoiBroadcaster.cs)、[EntitySnapshot.cs:67](../../../Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs) |
| F9 | 插值样本只带 位置/朝向/ActionState/Dead | [RemoteInterpolator.cs:25-40](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteInterpolator.cs) |
| F10 | **同屏实测峰值 5~6 个 bot**（`--bots 100`，±400m 撒点，80m 视野；期望值 ~3） | [t12/_console_build.log:1781](../../../production/verify/t12/_console_build.log)、`t13/_console_build.log:1385` |
| F11 | 选角是**纯客户端** `PlayerPrefs`，协议与 `PlayerState` 里**没有任何外观字段** | [CharacterSelectionService.cs:6](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterSelectionService.cs)（注释："网络化时再扩展协议"） |
| F12 | 碰撞现状**不一致**：bot 的胶囊自带 `CapsuleCollider`（挡人）；远端真人碰撞体被驱动全部禁用（可穿过）；怪物走 `NpcBody` 层 + `NpcBodyCollider`（挡人，有守护用例） | RemoteEntityRegistry.cs:232、RemoteEntityAnimator.cs:47-48、[NpcBodyCollider.cs](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcBodyCollider.cs) |
| F13 | `BotRadius = 0.5` 的注释明确写着它"对应客户端那条占位胶囊"——**换成模型后这条真值源失效** | BotRunner.cs:18-24 |

### 0.2 资产与工程设置现状（资深审计补充）

这些决定了"性能实测算不算数"，也决定了"为什么网格 LOD 不解决问题"。

| # | 事实 | 出处 |
|---|---|---|
| F14 | 当前质量档 **档 5 = "Ultra"**：软阴影 / 4 级联 / **shadowDistance 150** / 像素光 4 / MSAA 2× / **实时反射探针开** / 蒙皮权重 4 / lodBias 2；**`m_PerPlatformDefaultQuality` 为空 ⇒ 全平台同档** | `ProjectSettings/QualitySettings.asset` |
| F15 | **无任何运行时代码或构建脚本覆盖质量档**（全项目零 `QualitySettings` 调用；[WebGLBuildScript.cs](../../../Assets/Scripts/Editor/WebGLBuildScript.cs) 无相关行）⇒ **手机浏览器跑的就是 Ultra** | grep |
| F16 | DemoDay：1 个方向光、**软阴影开**、**0 个 `PostProcessVolume`**（⇒ 运动矢量与 TAA 在本项目里无人消费） | [DemoDay.unity](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity) |
| F17 | 角色预制体：`m_SkinnedMotionVectors: 1`（SMR 双缓冲 + 每帧速度缓冲——无 TAA 时纯浪费；怪物预制体同样开着）、`m_CastShadows: 1`（阴影 pass 再渲染一遍）、4 个 SMR **共用 1 个材质** ⇒ 每角色 4 个 draw call | MaleCharacterPBR.prefab、DogPBR.prefab |
| F18 | 模型是 **Humanoid**（`animationType: 3`）⇒ 每帧肌肉空间重定向，是每角色最大的 CPU 项 | ModularCharacterPBR.fbx.meta:614 |
| F19 | `m_CullingMode: 1`（CullUpdateTransforms，已是次优档：屏外仍评估状态机）、`m_ApplyRootMotion: 0`（根运动已关）、`m_KeepAnimatorStateOnDisable: 0`（**Animator 禁用即重置状态**，升档必须失效去重游标） | MaleCharacterPBR.prefab:2621-2631 |
| F20 | **全项目零 `LODGroup`**：没有任何模型带 LOD 资产 | `grep -rl LODGroup --include=*.prefab Assets` = 0 |

## 1. 目标与成功判据

### 目标

1. **一条路径**：真人玩家与压测 bot 走同一条远端渲染路径（外观目录 → 实例化 → 驱动），不再按 Kind 分叉。
2. **表示分级骨架**：按大世界标准建立"距离 + 每帧预算"决定表现档位的机制——**本轮只实现 Full / Proxy 两档**，档位决策点、换档机制、预算旋钮、诊断读数全部到位。
3. **远端角色会走路**：真人 + bot 共用同一个驱动，按**渲染位移反推的速度**驱动 Idle/Walk/Run，并让步频匹配速度（不滑步）。

### 成功判据（这就是"完成"的定义）

| # | 判据 | 验证方式 |
|---|---|---|
| S1 | 场景里 bot 是真模型，且以匹配速度的步频移动（不滑行、不溜冰、不是胶囊） | 人工目视 + 截图 |
| S2 | bot 与真人视图**组件构成一致**：都有远端驱动、都无启用的 Collider | EditMode 断言 |
| S3 | 同屏 Full 档数量**从不超过预算**；超出者自动降 Proxy | EditMode 断言（合成多实体） |
| S4 | 未注入观察者位置时**行为与今天完全一致**（全 Full） | 现有测试全绿 + 断言 |
| S5 | 手机上能读到 `Full/Proxy 计数 + 帧率`，并能用 `?full=` 扫参数 | 真机走 [webgl-mobile-acceptance](../guides/webgl-mobile-acceptance.md) 流程 |
| S6 | 实测产出"这台机器扛多少个 Full 不掉档"的**数字**，回填为预算默认值——**且实测在移动端定档下进行**（见 §6-T0） | 密集压测记录 |
| S7 | 现有 AOI / 怪物 / 碰撞测试全部保持绿 | `AoiWorldTests`、`NpcBodyColliderTests`、`MonsterBodyRadiusTests` |

### 关于"上千玩家"的定位

客户端**不渲染世界总人数**——AOI（40m 格 / 80m 视野 / 迟滞环）是第一条控制线，客户端承受的是**局部密度**。第二条控制线就是本设计的表示分级：**距离保观感、预算控成本**。手机上"全动画人形"的真实预算量级是 20~40 个；分级后"可见总数"可到 100~200（远档换代理）。这些数字本轮不预设——先建骨架，用密集压测量出真值。

## 2. 范围边界

### 做

- **移动端定档**（实测前提，见 §6-T0）：给 WebGL/移动端固定一个质量档
- 外观目录（Inspector 数组，`Id → {Full, Proxy}`）与 bot 的确定性哈希选型
- `CreateView` 合并 bot 分支到真人路径
- 视图双表现（FullRoot / ProxyRoot）+ 惰性实例化 + 换档
- 分档决策：距离阈值（含迟滞）+ 每帧预算（最近优先）
- 行走动画驱动（渲染位移反推速度 → `Blend` / `LocomotionSpeed` / `BaseState`）
- 服务端密集压测模式（`--bots-radius`）
- 诊断读数（overlay 计数 + 帧率）与 URL 旋钮（`?full=`）
- ORCA `BotRadius` 真值源核对与守护断言

### 明确不做

| 不做 | 理由 |
|---|---|
| 网格 LOD / 减面资产 | **省不到真成本**——见下方"LOD 反面证据" |
| impostor / billboard 烘焙工具链 | `Proxy` 位已留好，等有资产时填预制体即可，不改代码 |
| 协议下发外观 id | 服务端今天不知道玩家选角（F11）；接缝已留，等选角网络化 |
| 装备 / 外观组合系统 | 与当前目标无关 |
| **怪物参与分档** | 怪物是玩法实体（碰撞体、受击、攻击排程耦合），且总量小；决策点已共用，扩展时加字段即可 |
| 对象池 / GPU instancing / DOTS | 当前规模无收益；惰性实例化的尖峰先量再定（§8-R1） |
| 相机距离分档 | 用本地玩家位置（与 AOI 同心、可测）；将来换相机距离需同步阈值 |
| 人群变体资产（单 SMR / Generic 化 / flat 材质） | 属资产层优化；`AppearanceEntry` 已支持挂任意预制体，将来做好直接填 |

### LOD 反面证据（为什么不做网格 LOD）

网格 LOD 只减顶点与三角形。而实测的每角色成本大头，它**一个都碰不到**：

| 成本项 | 现状 | LOD 能否降低 |
|---|---|---|
| Humanoid 重定向（肌肉空间 → 骨骼） | F18：所有角色都是 Humanoid | ✗ |
| 阴影 pass 再渲染一遍 | F17：`CastShadows: 1`，阴影距离 150m | ✗ |
| SMR 双缓冲 + 速度缓冲 | F17：`SkinnedMotionVectors: 1`，项目无 TAA | ✗ |
| draw call | F17：4 个 SMR / 角色 | ✗ |
| 每像素 PBR 着色 | 内置 Standard 着色器（PBR/Polyart 用**同一个**，只差贴图） | ✗ |
| 顶点/三角形 | — | ✓（唯一一项） |

**且全项目零 LOD 资产**（F20）：要做需先有减面工具链 + 12 个减面网格（男/女 × PBR/Polyart × 3 档），WebGL 无流式加载 ⇒ 各级网格全量进包、常驻内存。

**`LODGroup` 也不适用**：它是引擎内置的距离切换，但**不会停 `Animator`**——而停掉动画/蒙皮正是本设计的核心目标；且它没有迟滞、按最近 N 个裁剪的预算逻辑仍要自己记账。所以分级自己实现，用 `Animator` + `Renderer` 开关。

## 3. 架构

**一句话**：把"远端实体 → 表现"收敛成 **一条创建路径、一个外观目录、一个每帧分档决策点**。

```
ViewRoot                    ← 位置/朝向由插值写（不变）
 ├─ FullRoot                ← 模型实例 + RemoteEntityAnimator
 └─ ProxyRoot               ← 代理（今天=胶囊；将来=impostor 预制体）
```

**两档语义**：Full（完整模型 + 全速动画）/ Proxy（无 Animator、无蒙皮、1 个 draw call）。**AOI 出视野仍是销毁，不是第三档**——生命周期与档位是两回事，别混。

### 数据流（每帧）

```
WorldSnapshot → ApplySnapshot（建视图 / 写插值样本）
ClientNetworkManager.Update → registry.UpdateRender(now, delay, 观察者位置)
   ├─ 插值 → 写 ViewRoot 位置/朝向                        （不变）
   ├─ 反推速度：|本次位置 − 上次位置| / 渲染时刻差 → 钳位 → 平滑   （新，D17）
   ├─ 分档：距离迟滞 + 最近优先预算 → 目标档 → 换档        （新）
   └─ **仅 Full 档**：采样状态 → 驱动 Apply(speed, actionState, dead)
```

### 观察者位置

由 `ClientNetworkHost` 用 `_localEntry.transform.position` 注入 registry（与 AOI 同心）。**未注入 / 本地角色不存在 = 全 Full**——测试宿主与现有行为不受影响（延续项目"缺配置即降级"的惯例）。

## 4. 组件分解

| 组件 | 改动 | 说明 |
|---|---|---|
| `ClientNetworkHost` | 新增 `AppearanceEntry[] _appearances` 序列化字段 | 沿用 `_npcPrefabs` 范式：**不能用 `Dictionary`**（Unity 不序列化字典，场景配的值会静默丢光）。同时注入观察者位置 |
| `AppearanceEntry`（新，`[Serializable]`） | `int Id` / `GameObject Full` / `GameObject Proxy` | `Id=0` = 默认外观（真人用）。`Proxy` 留空 → 运行时生成胶囊 |
| `RemoteEntityRegistry.CreateView` | 删除 `PlayerBot` 分支；bot 与真人共用"目录查表 → 实例化 → 挂驱动" | 怪物分支**整条不动**（`_npcPrefabs` + `NpcEntityAnimator` + `NpcBodyCollider`） |
| `RemoteEntityView` | 新增 `Tier`、`FullRoot`、`ProxyRoot`、渲染位置/时刻记账；`Animator` 已有 | 视图根仍是插值写入点 |
| `RemoteEntityRegistry.UpdateRender` | 签名加观察者位置；新增 **速度反推段** 与 **分档段** | 顺序：插值 → 反推速度 → 分档 → 驱动 |
| `RemoteEntityAnimator` | 新增 locomotion 写入（`Blend` / `LocomotionSpeed` / `BaseState`）+ 平滑 | 受击层逻辑不变；升档必须失效 `_lastKey` 去重游标（F19：Animator 禁用即重置状态） |
| `RemoteInterpolator` | **不改**（D17：速度不穿样本，由渲染位移反推） | — |
| `BotRunner`（服务端） | 新增 `--bots-radius R` | **撒点与反弹边界都收到 ±R**（密集模式测试者站原点）。不带该参数时保持现状（±400m 撒点、±500m 边界） |

### 外观解析顺序

| Kind | 外观 id 来源 |
|---|---|
| `Npc` | `_npcPrefabs[TemplateId]`（现状不动） |
| `PlayerBot` | `hash(playerId)` 混合后取模选条目——**确定性**：同一个 bot 每次进视野长得一样（随机选会换脸）。在**全部条目**中选（含 Id=0）；条目只有一条时自然退化为统一外观 |
| `Player` | `Id = 0`（默认外观） |

哈希必须用乘法混合（如 `(playerId * 2654435761u)`），**不能直接 `playerId % N`**：bot id 连续分配，取模会让男女规律交替，一眼假。

### 分档规则

| 规则 | 初值 | 理由 |
|---|---|---|
| 进入 Full | ≤ 60m | 人形在 60m 处约 25px，切换看不出来；贴近 AOI 边缘（80m） |
| 退出 Full | 进入阈值 × 1.15（≈ 69m） | **由进入阈值推导**，不独立配置——与 AOI 的 80/90 同款迟滞范式，防阈值抖动反复换档 |
| 每帧 Full 预算 | 20（**未经实测校准的保守起点**） | **预算才是成本主控**；低密度全 Full 好看，高密度靠预算兜底 |
| 预算裁剪迟滞 | 已在 Full 的距离 ×0.9 折扣后再参与排序 | 距离相近时不来回抢档 |
| 预算裁剪 | 视野内按距离升序，前 N 个 Full，其余 Proxy | 排序走**预分配缓冲**（照项目 `ResolveNonAlloc` 惯例），禁止每帧 new List / 闭包比较器 |

**换档机制**：降档 = 禁用 `FullRoot` 整棵（Animator / 蒙皮 / 渲染器全停——这正是表示分级相对于网格 LOD 的价值）；**不销毁**（会再升回来）。升档 = 启用；若从未实例化过则先实例化。

**驱动守卫**：`RemoteEntityAnimator.Apply` 是 registry 主动调用的公开方法（不是生命周期回调），**禁用 GameObject 挡不住它**。守卫必须放在调用点（组件不知道自己的档位）。

### 速度反推与行走动画映射

**速度来源（D17）**：渲染位移的导数——即"屏幕上的实际移动速度"，这正是动画要匹配的量。

```
rawSpeed = |本次插值位置 − 上次插值位置| / (本次 renderAt − 上次 renderAt)   // 服务器 tick 基准
speed    = clamp(rawSpeed, 0, 1.5 × SprintSpeed)                        // 瞬移/重生不让它爆掉
speed   += 指数平滑（时间常数 ≈ 0.15s）                                    // 快照 15Hz 带来的台阶
```

新建视图首帧没有上一位置 → 速度按 0（下一帧自纠）。位置保持（无新样本）时速度自然为 0 → Idle，正确。

| 参数 | 远端映射 |
|---|---|
| `Blend`（float 0~1） | `< 0.1 → 0`（Idle）；`< 6.5 → 0.5`（走）；`否则 → 1`（跑）。**6.5 = 走/跑标称速度的中点**（5.0 与 8.0），消歧义 |
| `LocomotionSpeed`（float） | **`clamp(speed / 该档位标称速度, 0.3, 1.5)`**——步频按速度缩放，否则溜冰（F6：本地正是用它防滑步）。**下限 0.3 是必需的**：速度趋 0 时比例为 0 会把动画冻成静止帧；`Blend == 0`（静止）时直接写 1。bot 8.03 m/s 落在跑档标称 8.0 上 ⇒ ≈1.0 |
| `BaseState`（int） | 静止 → `Idle(0)`；移动 → `Locomotion(7)` |

标称速度（5.0 / 8.0）**不是本设计自定义的常量**——它们必须与本地 `CharacterController.MoveSpeed/SprintSpeed` 保持一致，用守护断言钉住（照 `MonsterBodyRadiusTests` 范式：本地改了常量，测试会红）。

> 本地是**输入驱动**（0/0.5/1 离散）、远端是**速度驱动**（过阈值 + 按比例调步频）：同一动作的取值可能有细微差异，这是有意的——远端没有输入，只有渲染位移。

### 诊断读数与旋钮

- 扩展 `ClientNetworkHost._showDebugOverlay`：增加 `Full / Proxy 计数`、`当前预算`、`帧率`
- URL 覆盖：`?full=<N>` 覆盖预算、`?fulldist=<m>` 覆盖**进入阈值**（退出阈值按 ×1.15 跟随）
  - **`?full=0` = 全 Proxy = 回到"纯胶囊"条件**。两个用途：① AOI 验证时去掉渲染噪声；② 实测若证明模型太贵，这是**不改代码的回滚开关**

## 5. 错误处理

| 情形 | 行为 |
|---|---|
| 目录未命中 / 未接线 | 保留现有胶囊降级 + warn once（不崩快照消费链——既有硬约束） |
| 预制体无 Animator / 无控制器 | 驱动自禁用（既有惯例：`runtimeAnimatorController == null → null`） |
| **Proxy 胶囊** | **必须移除 `CreatePrimitive` 自带的 `Collider`**——否则违反 D5（都不挡），且 S2 断言会红 |
| 瞬移 / 服务器校正 / 重生 | 速度导数被钳到 `1.5 × SprintSpeed`，平滑吸收 |
| 观察者位置缺失 | 全 Full（与今天一致） |
| Proxy 档 | 不调用驱动写入；位置插值照常（Proxy 也要动） |
| 升档 | 失效 `_lastKey` 去重游标，强制重写一次（F19：`KeepAnimatorStateOnDisable=0`，Animator 禁用即重置状态） |

## 6. 依赖序任务清单（不排日历）

| # | 任务 | 验证 |
|---|---|---|
| **T0** | **移动端定档**：给 WebGL/移动端固定一个质量档（阴影距离/级联数/像素光/MSAA/反射探针/蒙皮权重） | 手机上帧率明显改善；**这是 T9 实测的前提**——不定档，测出来的数字是别的瓶颈 |
| T1 | 速度反推（含钳位 + 平滑 + 每视图记账） | EditMode：静止→0 / 匀速→标称 / 瞬移→被钳位 |
| T2 | 动画驱动 locomotion（`Blend` / `LocomotionSpeed` 比例 / `BaseState`） | EditMode：参数映射断言 + 标称速度守护断言 |
| T3 | 外观目录 + 解析（哈希混合） | EditMode：命中 / 未命中降级 / 同 id 恒定同外观 |
| T4 | 统一路径（bot 并入真人分支） | EditMode：组件构成一致 + 无启用 Collider；目视 |
| T5 | 分档骨架（阈值 / 迟滞 / 预算 / 惰性 / 换档） | EditMode：分档决策；Proxy 档 FullRoot 渲染器全禁用（结构性断言） |
| T6 | ORCA 半径核对 + 守护断言 | 实测模型半宽 vs 0.5 → 取一或改半径，注释同步（F13） |
| T7 | 服务端密集压测模式 `--bots-radius` | 本机跑通 100 实体入视野（顺带 AOI 带宽验收） |
| T8 | 诊断读数 + URL 旋钮 | 桌面浏览器先能读到数字 |
| T9 | 手机实测 → 回填预算默认值 | 走 webgl-mobile-acceptance 流程，产出 S6 的数字 |

## 7. 测试策略

**前置**：T9 必须在 T0（移动端定档）完成后进行；实测记录里要写明档位。

**EditMode**
- 目录：命中 / 未命中降级 / 哈希稳定（同 id 恒定）
- 统一路径：bot 与真人视图组件构成一致（有驱动、**无启用 Collider**）
- 分档：距离阈值、迟滞（进入 60 / 退出 69，阈值附近不来回抖）、预算裁剪（最近优先）、裁剪迟滞（0.9 折扣）、未注入观察者 = 全 Full
- Proxy 档结构性断言：`FullRoot` 下所有 `Renderer.enabled == false`
- 速度反推：静止 → 0；匀速 → 标称；瞬移 → 被钳位；无新样本保持 → 0
- 驱动：速度 → `Blend` / `LocomotionSpeed` 映射；标称速度常量与本地 `CharacterController` 一致性守护；**静止时 `LocomotionSpeed` 写 1（不冻 idle）**；Proxy 档不被调用

**PlayMode**
- `AoiWorldTests` 保持绿（进出视野 / 缺席超时销毁语义不变）
- 新增：多实体注入下 Full 数 ≤ 预算

**真机**（人工）
- 密集模式扫 `?full=8/12/20/40/0` → 记录 `Full 数 vs 帧率` → 定预算

**守护**（不得因本设计变红）
- `NpcBodyColliderTests`、`MonsterBodyRadiusTests`、`AoiWorldTests` 现有断言

## 8. 风险清单

| # | 风险 | 处置 |
|---|---|---|
| R1 | **实测数字无效**：不定档就测，测到的是 Ultra 档的阴影/AA/反射探针瓶颈，不是角色瓶颈 | T0 作为 T9 硬前提；实测记录写明档位 |
| R2 | **惰性实例化的 Instantiate 尖峰**：实体从远档走近时一次性实例化 16 组件 + 4 SMR | 本轮不池化（YAGNI）；密集压测**专门量**；实测尖峰明显 → 下一步对象池 |
| R3 | 换档 popping | 阈值放到 60/69m（低密度场景最显眼，故阈值贴近 AOI 边缘）+ 迟滞 |
| R4 | 每帧排序的 GC 压力（WebGL） | 强制预分配缓冲 + 无闭包比较器 |
| R5 | 密集模式下 ORCA 满负荷（100 邻居） | 这是**有意的**（顺带压测避让）；邻居缓冲上限 128，106 实体不溢出 |
| R6 | bot 上模型后给 AOI 验证引入渲染噪声 | `?full=0` 一键回到纯胶囊条件 |
| R7 | 远端真人的外观仍是单一（默认男模型） | 已知边界：协议无外观字段（F11）；接缝已留 |
| R8 | 本地/远端动画取值的细微差异 | 有意的（输入驱动 vs 速度驱动）；写进 guide 避免误报 bug |

## 9. 决策记录

| # | 决策 | 来源 |
|---|---|---|
| D1 | 目标是**产品化铺路**（真人 + bot 统一真模型路径），不是 demo 美化 | 用户选择 |
| D2 | 目标是**大世界标准**（世界可上千玩家）；客户端渲染目标是**局部密度**，AOI 是第一控制线 | 用户选择 |
| D3 | 客户端渲染规模：**先做分级骨架 + 实测后再定具体目标** | 用户选择 |
| D4 | 行走动画：**真人 + bot 共用同一个驱动**，顺手修掉真人滑行 | 用户选择 |
| D5 | 碰撞：**都不挡**（远端实体是视图不是物理体）；**怪物仍然挡**（玩法实体） | 用户选择 |
| D6 | 外观身份：bot 客户端确定性哈希；真人固定 Id 0；**本轮不动协议** | 用户选择（方案 B） |
| D7 | **不做网格 LOD**：只减顶点/三角形，碰不到 Humanoid 重定向、阴影 pass、蒙皮双缓冲、draw call、PBR 着色（§2 反面证据） | 用户问"能否用 LOD" → 审计补硬证据 |
| D8 | 分级 = **表示分级**（近 Full / 远 Proxy），不是网格 LOD；**`LODGroup` 不适用**（不停 Animator、无迟滞） | 自查 + 审计 |
| D9 | 阈值初值 **60/69m**（原 35/40m 定反了：低密度场景最容易看见 popping） | **自查修正** |
| D10 | 驱动写入**仅在 Full 档**（`Apply` 是主动调用，禁 GameObject 挡不住） | **自查修正** |
| D11 | 惰性实例化尖峰：**先量不先池化** | **自查修正** |
| D12 | 排序走预分配缓冲（NonAlloc） | **自查修正** |
| D13 | 哈希用乘法混合，不用取模（bot id 连续，取模会规律交替） | **自查修正** |
| D14 | 加 `?full=0` 开关（AOI 验证去噪 + 回滚开关） | **自查** |
| D15 | **怪物不参与分档**（玩法实体 + 碰撞体耦合）；决策点共用，扩展时加字段 | **自查修正** |
| D16 | `LocomotionSpeed` **按速度比例写**（含 0.3 下限 + 静止写 1），不是恒 1——它是步频倍率，写死会溜冰、写 0 会冻住 idle | **资深审计修正 + 自检补丁** |
| D17 | **速度不穿协议**：由渲染位移反推（钳位 + 平滑）。理由：动画要匹配的是屏幕位移，且免动插值器与测试 | 资深审计 → **用户选择** |
| D18 | **移动端定档作为实测硬前提**（当前全平台跑 Ultra，`m_PerPlatformDefaultQuality` 为空且无代码覆盖） | 资深审计 → **用户选择** |
| D19 | Proxy 胶囊必须移除 `CreatePrimitive` 自带 Collider（D5 的直接推论） | **资深审计修正** |
