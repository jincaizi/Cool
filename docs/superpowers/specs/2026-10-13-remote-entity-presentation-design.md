# 远端实体表现统一与表示分级 —— 设计

日期：2026-10-13
状态：**设计已定稿，待实现**
上游：[P3 AOI 大世界](../plans/2026-09-10-p3-aoi-world.md)、[P5 怪物服务端化](../plans/2026-09-12-p5-monster-server.md)、[移动/碰撞/避让报告](../plans/2026-10-13-spawn-and-collision-report.md)、[WebGL 移动端验收](../guides/webgl-mobile-acceptance.md)

## 0. 前置结论（本设计的事实基础）

这些是 2026-10-13 实测/读码得到的事实，不是假设。后面每个决策都建立在它们之上。

| # | 事实 | 出处 |
|---|---|---|
| F1 | 远端实体分三支：真人 → 真角色预制体；怪物 → 真模型 + 驱动 + 碰撞体；**bot → `CreatePrimitive(Capsule)`，无动画** | [RemoteEntityRegistry.cs:200-260](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs) |
| F2 | 场景里 `ClientNetworkHost._playerAvatarPrefab` 指向 `MaleCharacterPBR.prefab`——它是 **3C 全装备预制体**（16 个 MonoBehaviour + `CharacterController` + 4 个 SMR），远程驱动把本地行为全停掉后靠快照驱动 | [DemoDay.unity:7434](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity) |
| F3 | **男女两套全装备预制体都已存在**（`MaleCharacterPBR` / `FemalePlayer`），共用同一个 3C 控制器（guid `fb7ffdef…`）⇒ bot 混用男女**不需要新资产** | 预制体结构比对（各 16 组件 / 4 SMR / 同控制器） |
| F4 | RpgDuo 的 PBR/Polyart **原始**预制体（0 个 MonoBehaviour、包自带控制器）不在此列——它们没有 3C 控制器，驱动参数对不上 | 同上 |
| F5 | **全项目零 `LODGroup`**：没有任何模型带 LOD 资产 | `grep -rl LODGroup --include=*.prefab Assets` = 0 |
| F6 | 快照**恒发全掩码**（`Position\|Rotation\|Speed\|ActionState\|Kind\|TemplateId\|Dead`），字段 `Speed`（m/s）**已在协议里**，客户端目前无人消费 | [AoiBroadcaster.cs:63-65](../../../Server/Aoi/AoiBroadcaster.cs)、[EntitySnapshot.cs:67](../../../Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs) |
| F7 | 远端真人的动画只驱动受击层——**没有任何人根据 Speed 写行走动画**，真人在滑行 | [RemoteEntityAnimator.cs:92-104](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs) |
| F8 | 本地行走动画的既有契约：`Blend`（float 0/0.5/1，**由输入幅度与冲刺开关决定**）、`BaseState`（int；移动态为 `Locomotion=7`）、`LocomotionSpeed`（float，举盾写 0.4、平时 1） | [CharacterController.cs:376-391](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs)、[FSMManager.cs:85-93](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs) |
| F9 | 速度常量：`MoveSpeed = 5.0`、`SprintSpeed = 8.0`（m/s）。bot 速度 `0.53 / 0.066 ≈ 8.03 m/s` ⇒ **bot 天然落在冲刺档** | CharacterController.cs:14-15、[BotRunner.cs:16](../../../Server/Aoi/BotRunner.cs) |
| F10 | 插值样本只带 位置/朝向/ActionState/Dead，**不带 Speed** | [RemoteInterpolator.cs:25-40](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteInterpolator.cs) |
| F11 | **同屏实测峰值 5~6 个 bot**（`--bots 100`，±400m 撒点，80m 视野；期望值 ~3） | `AoiWorldTests` 输出，[production/verify/t12/_console_build.log:1781](../../../production/verify/t12/_console_build.log)、`t13/_console_build.log:1385` |
| F12 | 选角是**纯客户端** `PlayerPrefs`，协议与 `PlayerState` 里**没有任何外观字段**，服务端不知道玩家选了男还是女 | [CharacterSelectionService.cs:6](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterSelectionService.cs)（注释原文："网络化时再扩展协议"） |
| F13 | 碰撞现状**不一致**：bot 的胶囊自带 `CapsuleCollider`（挡人）；远端真人的碰撞体被驱动全部禁用（可穿过）。怪物走 `NpcBody` 层 + `NpcBodyCollider`（挡人，有守护用例） | RemoteEntityRegistry.cs:232、RemoteEntityAnimator.cs:47-48、[NpcBodyCollider.cs](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcBodyCollider.cs) |
| F14 | `BotRadius = 0.5` 的注释明确写着它"对应客户端那条占位胶囊"——**换成模型后这条真值源失效** | BotRunner.cs:18-24 |

## 1. 目标与成功判据

### 目标

1. **一条路径**：真人玩家与压测 bot 走同一条远端渲染路径（外观目录 → 实例化 → 驱动），不再按 Kind 分叉。
2. **表示分级骨架**：按大世界标准建立"距离 + 每帧预算"决定表现档位的机制——**本轮只实现 Full / Proxy 两档**，档位决策点、换档机制、预算旋钮、诊断读数全部到位。
3. **远端角色会走路**：真人 + bot 共用同一个驱动，按快照 `Speed` 驱动 Idle/Walk/Run。

### 成功判据（这就是"完成"的定义）

| # | 判据 | 验证方式 |
|---|---|---|
| S1 | 场景里 bot 是真模型，且以"跑"的姿势移动（不是滑行、不是胶囊） | 人工目视 + 截图 |
| S2 | bot 与真人视图**组件构成一致**：都有远端驱动、都无启用的 Collider | EditMode 断言 |
| S3 | 同屏 Full 档数量**从不超过预算**；超出者自动降 Proxy | EditMode 断言（合成多实体） |
| S4 | 未注入观察者位置时**行为与今天完全一致**（全 Full） | 现有测试全绿 + 断言 |
| S5 | 手机上能读到 `Full/Proxy 计数 + 帧率`，并能用 `?full=` 扫参数 | 真机走 [webgl-mobile-acceptance](../guides/webgl-mobile-acceptance.md) 流程 |
| S6 | 实测产出"这台机器扛多少个 Full 不掉档"的**数字**，回填为预算默认值 | 密集压测记录 |
| S7 | 现有 AOI / 怪物 / 碰撞测试全部保持绿 | `AoiWorldTests`、`NpcBodyColliderTests`、`MonsterBodyRadiusTests` |

### 关于"上千玩家"的定位（本轮的技术立场）

客户端**不渲染世界总人数**——AOI（40m 格 / 80m 视野 / 迟滞环）是第一条控制线，客户端承受的是**局部密度**。第二条控制线就是本设计的表示分级：**距离保观感、预算控成本**。手机上"全动画人形"的真实预算量级是 20~40 个；分级后"可见总数"可到 100~200（远档换代理）。这些数字本轮不预设——先建骨架，用密集压测量出真值。

## 2. 范围边界

### 做

- 外观目录（Inspector 数组，`Id → {Full, Proxy}`）与 bot 的确定性哈希选型
- `CreateView` 合并 bot 分支到真人路径
- 视图双表现（FullRoot / ProxyRoot）+ 惰性实例化 + 换档
- 分档决策：距离阈值（含迟滞）+ 每帧预算（最近优先）
- 行走动画驱动（Speed → `Blend` / `BaseState`）与 `Speed` 进插值样本
- 服务端密集压测模式（`--bots-radius`）
- 诊断读数（overlay 计数 + 帧率）与 URL 旋钮（`?full=`）
- ORCA `BotRadius` 真值源核对与守护断言

### 明确不做

| 不做 | 理由 |
|---|---|
| 网格 LOD / 减面资产 | 省不到真成本（Animator/蒙皮/draw call 不减），且全项目零 LOD 资产 |
| impostor / billboard 烘焙工具链 | `Proxy` 位已留好，等有资产时填预制体即可，不改代码 |
| 协议下发外观 id | 服务端今天不知道玩家选角（F12）；接缝已留，等选角网络化 |
| 装备 / 外观组合系统 | 与当前目标无关 |
| **怪物参与分档** | 怪物是玩法实体（碰撞体、受击、攻击排程耦合），且总量小；决策点已共用，扩展时加字段即可 |
| 对象池 / GPU instancing / DOTS | 当前规模无收益；惰性实例化的尖峰先量再定（§8-R1） |
| 相机距离分档 | 用本地玩家位置（与 AOI 同心、可测）；将来换相机距离需同步阈值 |

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
WorldSnapshot → ApplySnapshot（建视图 / 写插值样本；含 Speed）
ClientNetworkManager.Update → registry.UpdateRender(now, delay, 观察者位置)
   ├─ 插值 → 写 ViewRoot 位置/朝向                        （不变）
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
| `RemoteEntityView` | 新增 `Tier`、`FullRoot`、`ProxyRoot`、`Animator`（已有）；不变 `Position/Rotation` | 视图根仍是插值写入点 |
| `RemoteEntityRegistry.UpdateRender` | 签名加观察者位置；新增分档段 | 分档在插值之后、驱动之前 |
| `RemoteEntityAnimator` | 新增 locomotion 写入（`Blend` / `BaseState` / `LocomotionSpeed`）+ 平滑 | 受击层逻辑不变（`_lastKey` 去重游标需处理升档强制重写） |
| `RemoteInterpolator` | `Sample` 增 `Speed`；`AddSample` 签名增该参数 | 阶梯取值（与 ActionState/Dead 同规则），不插值 |
| `BotRunner`（服务端） | 新增 `--bots-radius R` | **撒点与反弹边界都收到 ±R**（密集模式测试者站原点）。不带该参数时保持现状（±400m 撒点、±500m 边界） |

### 外观解析顺序

| Kind | 外观 id 来源 |
|---|---|
| `Npc` | `_npcPrefabs[TemplateId]`（现状不动） |
| `PlayerBot` | `hash(playerId)` 混合后取模选条目——**确定性**：同一个 bot 每次进视野长得一样（随机选会换脸）。在**全部条目**中选（含 Id=0）；条目只有一条时自然退化为统一外观 |
| `Player` | `Id = 0`（默认外观） |

哈希必须用乘法混合（如 `(playerId * 2654435761u)`），**不能直接 `playerId % N`**：bot id 连续分配，取模会让男女逐帧规律交替，一眼假。

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

### 行走动画映射

| 参数 | 远端映射 |
|---|---|
| `Blend`（float 0~1） | 由 `Speed` 过阈值取 0 / 0.5 / 1：`< 0.1 → 0`；`≤ MoveSpeed(5.0) → 0.5`；`≥ SprintSpeed(8.0) → 1` |
| `BaseState`（int） | 静止 → `Idle(0)`；移动 → `Locomotion(7)` |
| `LocomotionSpeed`（float） | **显式写 1**（不能"不管"——控制器参数默认值可能是 0，会把步频冻住） |

- 快照 15Hz 会让 `Blend` 呈台阶 → 驱动侧做一阶平滑
- **本地是输入驱动（0/0.5/1 离散）、远端是速度驱动（过阈值）**：两者对同一动作的取值可能有细微差异，这是有意的——远端没有输入，只有权威速度

### 诊断读数与旋钮

- 扩展 `ClientNetworkHost._showDebugOverlay`：增加 `Full / Proxy 计数`、`当前预算`、`帧率`
- URL 覆盖：`?full=<N>` 覆盖预算、`?fulldist=<m>` 覆盖**进入阈值**（退出阈值按 ×1.15 跟随）
  - **`?full=0` = 全 Proxy = 回到"纯胶囊"条件**。两个用途：① AOI 验证时去掉渲染噪声；② 实测若证明模型太贵，这是**不改代码的回滚开关**

## 5. 错误处理

| 情形 | 行为 |
|---|---|
| 目录未命中 / 未接线 | 保留现有胶囊降级 + warn once（不崩快照消费链——既有硬约束） |
| 预制体无 Animator / 无控制器 | 驱动自禁用（既有惯例：`runtimeAnimatorController == null → null`） |
| `Speed` 位缺失的快照 | **沿用 `_lastKnownSpeed`**（照 `_lastKnownYaw` 范式）。今天服务端恒发全掩码不会触发，但**不能假设**——一旦 Speed 掉 0，全部远端角色原地踏步 |
| 观察者位置缺失 | 全 Full（与今天一致） |
| Proxy 档 | 不调用驱动写入；位置插值照常（Proxy 也要动） |

## 6. 依赖序任务清单（不排日历）

| # | 任务 | 验证 |
|---|---|---|
| 1 | `Speed` 进插值样本（含位守卫 + 沿用） | EditMode：阶梯取值、部分位快照不掉 0 |
| 2 | 动画驱动 locomotion + 平滑 | EditMode：参数映射断言（速度 → Blend/BaseState） |
| 3 | 外观目录 + 解析（哈希混合） | EditMode：命中 / 未命中降级 / 同 id 恒定同外观 |
| 4 | 统一路径（bot 并入真人分支） | EditMode：组件构成一致 + 无启用 Collider；目视 |
| 5 | 分档骨架（阈值 / 迟滞 / 预算 / 惰性 / 换档） | EditMode：分档决策；Proxy 档 FullRoot 渲染器全禁用（结构性断言） |
| 6 | ORCA 半径核对 + 守护断言 | 实测模型半宽 vs 0.5 → 取一或改半径，注释同步 |
| 7 | 服务端密集压测模式 `--bots-radius` | 本机跑通 100 实体入视野（顺带 AOI 带宽验收） |
| 8 | 诊断读数 + URL 旋钮 | 桌面浏览器先能读到数字 |
| 9 | 手机实测 → 回填预算默认值 | 走 webgl-mobile-acceptance 流程，产出 S6 的数字 |

## 7. 测试策略

**EditMode**
- 目录：命中 / 未命中降级 / 哈希稳定（同 id 恒定）
- 统一路径：bot 与真人视图组件构成一致（有驱动、无启用 Collider）
- 分档：距离阈值、迟滞（进入 60 / 退出 69，阈值附近不来回抖）、预算裁剪（最近优先）、裁剪迟滞（0.9 折扣）、未注入观察者 = 全 Full
- Proxy 档结构性断言：`FullRoot` 下所有 `Renderer.enabled == false`
- Speed 样本：阶梯取值；部分位快照沿用 last-known
- 驱动：速度 → `Blend`/`BaseState` 映射；Proxy 档不被调用

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
| R1 | **惰性实例化的 Instantiate 尖峰**：实体从远档走近时一次性实例化 16 组件 + 4 SMR | 本轮不池化（YAGNI）；密集压测**专门量**；实测尖峰明显 → 下一步对象池 |
| R2 | 换档 popping | 阈值放到 60/70m（低密度场景最显眼，故阈值贴近 AOI 边缘）+ 迟滞；密集场景反而看不出来 |
| R3 | 每帧排序的 GC 压力（WebGL） | 强制预分配缓冲 + 无闭包比较器 |
| R4 | 密集模式下 ORCA 满负荷（100 邻居） | 这是**有意的**（顺带压测避让）；邻居缓冲上限 128，106 实体不溢出 |
| R5 | bot 上模型后给 AOI 验证引入渲染噪声 | `?full=0` 一键回到纯胶囊条件 |
| R6 | 远端真人的外观仍是单一（默认男模型） | 已知边界：协议无外观字段（F12）；接缝已留 |
| R7 | 本地/远端动画取值的细微差异 | 有意的（输入驱动 vs 速度驱动）；写进 guide 避免误报 bug |

## 9. 决策记录

| # | 决策 | 来源 |
|---|---|---|
| D1 | 目标是**产品化铺路**（真人 + bot 统一真模型路径），不是 demo 美化 | 用户选择 |
| D2 | 目标是**大世界标准**（世界可上千玩家）；客户端渲染目标是**局部密度**，AOI 是第一控制线 | 用户选择 |
| D3 | 客户端渲染规模：**先做分级骨架 + 实测后再定具体目标**（不预设 50~100 或 200~500） | 用户选择 |
| D4 | 行走动画：**真人 + bot 共用同一个驱动**，顺手修掉真人滑行 | 用户选择 |
| D5 | 碰撞：**都不挡**（远端实体是视图不是物理体）；**怪物仍然挡**（玩法实体） | 用户选择 |
| D6 | 外观身份：bot 客户端确定性哈希；真人固定 Id 0；**本轮不动协议** | 用户选择（方案 B） |
| D7 | **不做网格 LOD**：省不到真成本，且全项目零 LOD 资产（F5） | 自查（用户问"能否用 LOD"） |
| D8 | 分级 = **表示分级**（近 Full / 远 Proxy），不是网格 LOD | 自查（同上） |
| D9 | 阈值初值 **60/70m**（原 35/40m 定反了：低密度场景最容易看见 popping） | **自查修正** |
| D10 | 驱动写入**仅在 Full 档**（`Apply` 是主动调用，禁 GameObject 挡不住） | **自查修正** |
| D11 | `Speed` 按 **FieldMask 位守卫 + 沿用**（照 `_lastKnownYaw` 范式） | **自查修正** |
| D12 | 惰性实例化尖峰：**先量不先池化** | **自查修正** |
| D13 | 排序走预分配缓冲（NonAlloc） | **自查修正** |
| D14 | 哈希用乘法混合，不用取模（bot id 连续，取模会规律交替） | **自查修正** |
| D15 | 加 `?full=0` 开关（AOI 验证去噪 + 回滚开关） | **自查** |
| D16 | **怪物不参与分档**（玩法实体 + 碰撞体耦合）；决策点共用，扩展时加字段 | **自查修正** |
