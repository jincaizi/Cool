# 网络同步系统设计（大世界 MMO 路线图）

- 日期：2026-08-27
- 状态：已确认（经三轮分节评审 + 两轮综合审核修订）
- 技术路线锁定：KCP over UDP；其余网络代码均为可替换草稿

---

## 1. 目标与约束

| 项 | 结论 |
|---|---|
| 最终形态 | 大世界 MMO（同屏可达数十人，需要 AOI 兴趣管理） |
| 服务器 | 独立 C# .NET 8 项目，与客户端**同仓库**（`Cool/Server/`），复用 KCP 会话层 |
| 移动权威模型 | **B+：客户端权威移动 + 服务端合理性校验纠偏**（完整决策记录见 §6） |
| 硬性要求 | ① 少返工 ② KCP+UDP 不变 ③ 客户端服务端代码最大化共用 ④ 技术先进且优缺点明示 |

### 已排除的方向

- **Lockstep 帧同步**：要求全栈确定性模拟，Unity 物理（CharacterController / Physics.Raycast 地面检测）不可复现，改造成本极高；断线重连需逐帧追赶；大世界 MMO 行业不用。排除。
- **开源库承载（Fish-Net 等）**：框架以自身 MonoBehaviour 契约为中心，与本项目的 Hotfix/AOT/HybridCLR 三层架构存在适配返工风险；KCP 挂接需自定义 transport；AOI 非一等公民。排除。

## 2. 总体架构

```
E:\CodeForJob\Cool\                      ← 单仓库
 ├─ Assets\Scripts\AOT\Protocol\         ★ 协议层：独立 asmdef
 │    （纯 POCO + protobuf 特性，零逻辑）
 ├─ Assets\Scripts\AOT\KcpNet\           KCP 传输层（保留现状）+ NetChannelAdapter
 │    （只认 Protocol 类型，不认识任何游戏类型）
 ├─ Assets\Scripts\Hotfix\GameSystems\
 │    ├─ SyncCore\                       ★ 共享逻辑：独立 asmdef，零 UnityEngine 依赖，
 │    │                                    列入 HybridCLR 热更程序集清单
 │    │    （时钟同步 / 移动校验规则 / AOI 格子 / CombatRules）
 │    └─ SyncLayer\                      客户端专属（Dispatcher/ClockSync采样调度[复用SyncCore算法]/
 │                                         RemoteEntityRegistry/MovementPolicy 注入点），Hotfix 层可热修
 └─ Server\                              .NET 8 控制台服务器
       csproj 用 <Compile Include> 链接上述两个 ★ 目录的同一批源文件

★ = 双端编译同一份源文件的目录
```

**代码归属原则：**

| 代码 | 归属 | 理由 |
|---|---|---|
| Protocol、SyncCore | 共享目录，两端编译同一份 | 校验规则两边不同源 = 作弊温床 + 行为分裂；时钟同步**算法**（D3 纯函数部分）在 SyncCore，采样调度在客户端 SyncLayer |
| SyncLayer | 客户端 Hotfix | bug 高发区走热更，不发版就能修 |
| KcpNet + NetChannelAdapter | 客户端 AOT | Unity 传输依赖，不需要热更 |
| SessionManager/ZoneManager/TickLoop/BotRunner/NpcBrain | 服务端独有 | 纯 .NET 世界 |

**HybridCLR 引用方向合法性**：Hotfix→AOT 单向引用成立；Protocol 放 AOT 侧使传输层可见；协议冻结后高频变更区全在可热更的 SyncCore/SyncLayer。

**与现存代码的处置清单（P0 执行）：**

- ❌ 删除四个死件：`NetworkBridge` / `NetworkPrediction` / `PositionInterpolator` / `SkillNetworkSync`（前人桩中 `INetworkClient` 声明"AOT 实现"违反 HybridCLR 引用方向，编不过去，是死因）
- ❌ 收编两份重复 Messages 定义（KcpNet/Common 与 ServerExample）为共享库唯一一份
- ✅ 保留 `MovementPolicy` 现状（含 `LocalMovementPolicy` 单机回退），带 P2 扩展标记
- ✅ KcpClient/KcpServer 传输会话管理原样保留——"KCP 不动"的边界就在这里

## 3. 协议治理规则（全文唯一口径）

| 规则 | 内容 |
|---|---|
| 分层冻结 | P0 冻结**规则层**：消息信封、版本号字段、seq/时间戳约定、位掩码增量编码方式。各系统消息体随所属阶段追加 |
| 只加不删 | 追加新消息/新字段永远非破坏性；废弃字段置零不再填充，不删 ProtoMember tag |
| 版本握手 | 登录请求携带客户端协议版本；不匹配则在握手段拦截拒登录并提示升级 |
| 时间戳语义 | 所有双向时间戳 = 发送时刻换算成的**服务器时钟**；禁止直接填客户端墙钟（DateTime.UtcNow.Ticks 仅做换算输入） |
| 序列化对齐 | 双端使用同一 protobuf 库且版本锁定（Protocol 目录 README 记录；CI/本地脚本校验两端版本常量一致） |

## 4. 关键技术决策（每项附代价）

### D1 · Tick 与插值参数

- 服务端 TickLoop **15Hz**（66.7ms/tick），MMO 快照行业标准区间低端，移动端带宽友好。
- 远程插值延迟**基准 2 ticks ≈ 133ms**（Valve/Gasser 经典模型），授权 D2 自适应在 [2,4] ticks 带宽内动态调整。
- 本地玩家位置上报：客户端以约 66ms **本地定时器节流**发送 `PositionSyncRequest`，**不依赖也不等待服务器 tick 相位**。
- 参数归入现有 GlobalGameSettings 配置体系（2026-05-19 spec），不另造配置轮子。
- **调参总规则（全局适用）**：所有联机调参——速度上限、瞬移/偏差阈值、插值延迟带宽、AOI 分级半径与频率、beacon 周期——均进 GlobalGameSettings，本文档只定机制不锁死数值；实现阶段以压测数据标定。
- **代价**：渲染看到的他人慢约 0.13s——状态同步固有代价，换取画面永不抽搐。

### D2 · KCP 可靠流的队头阻塞（本路线图最大固有约束）

三层缓解：

1. `NoDelay=true` + `Interval=10`（现配置形态保持）。
2. **最新生效语义**：位置快照只认最大 seq，迟到旧包丢弃不进插值缓冲。
3. **自适应 jitter buffer**：滑动平均测量快照到达间隔，插值延迟在 [2,4] ticks 动态微调。

**支撑前提**："最新生效丢包"依赖逐消息边界保持——P0 定稿 KcpOptions 时必须确认为消息模式（或自行帧化），写入 Server 骨架交付标准。

**反贪心条款**：禁止试图在 KCP 上再加第二条不可靠通道。KCP 给不了原生 UDP 的 fire-and-forget 是锁定的代价；对状态同步可接受（包小频高、插值兜底）。

### D3 · 时钟同步算法

- EnterWorld 后连发 8 样本突发初始化；此后每 10s 复校。
- 算法：滑窗内取 RTT 最小样本的偏移（min-filter 抗拥塞假偏差）；偏移变化经一阶低通缓动。比朴素平均抗抖、比完整 NTP 回归轻量。
- 全网唯一时间基准 = 服务器时钟（tick 计数语义），见 §3 时间戳规则。
- 依赖链：P2 seq 对账窗口、P4 冷却/施法时序判定均站在 D3 上，故置于 P1。

### D4 · 随机数策略

- **战斗骰只有服务器掷**：暴击/伤害浮动由服务端 CombatResolver 单点生成，结果随命中消息下发。客户端不掷战斗骰，不做共享种子流（两者择一，选前者：更简单且严格更强，无序列对齐负担）。
- 客户端表现级随机（粒子、音效变体）自由。
- **代价**：每次命中消息多几个字节的 RNG 结果字段，买到双端数字永远一致。

### D5 · 快照编码与带宽基线

- 增量单位 = 位掩码选择变更顶层字段（实体快照首日起即包含**动作/战斗状态标志位**，为 P4 施法者状态校验预留，后补即破坏性变更）。
- 完全静止实体降为 4Hz 轻量 beacon 保活可见性判断。
- 插值器兜底：快照时间戳超龄即瞬移合并，禁止跨越地图的飞行动画。
- 位置默认 float32 直传；坐标量化（int16 网格）为 P3 压测后的条件触发项，验收带宽超标才启用（YAGNI）。

### D6 · 异常路径总纲

| 场景 | 行为 |
|---|---|
| 连接期断线 | KcpClient 自动重连（现成机制参数沿用） |
| 会话期断线 | Token 重连 → EnterWorld 重建现场 |
| 校验失败（超速/非法技能） | 服务端拒收该消息 + 下发纠偏 + 审计日志；永不崩进程、不断连接 |
| 协议版本不符 | 握手期拦截，拒登录并提示升级 |
| 单机模式 | NullChannel 全吞实现 + LocalMovementPolicy——拔网线仍是完整游戏 |

## 5. 阶段划分与验收标准

架构先行节奏：P0-P1 打地基，之后每阶段往框架里填一个系统。贯穿两条横切约定：① 每 Phase 收尾跑通 EditMode 测试集（SyncCore/Protocol 测试文件即 xUnit 同一份，双端各跑一次）；② 任何阶段保持拔网线可玩的单机兜底。

### P0 工程底座（规模：小）

- 建 `Server/` .NET 8 项目骨架（game loop 主循环单线程独占世界状态，IO 线程只入无锁队列——R2 规定写进骨架第一课）。
- 建 Protocol / SyncCore 目录与 asmdef；**冻结协议规则层 v1**（§3）；定义 P0-P2 所需消息体（echo、登录/EnterWorld、位置同步）。
- 执行 §2 处置清单（删四死件、收编 Messages、MovementPolicy 保留）；确认 KCP 消息模式（D2 前提）。
- **验收**：服务器进程启动 → Unity 客户端连接 → echo 往返成功；两端编译同一批共享源文件。

### P1 双端框架核心（规模：中大）

- 服务端：SessionManager、EntityStore（脏字段跟踪）、TickLoop(15Hz)+SnapshotAssembler（增量位掩码）、PerceptionFilter 接口（先用全量可见实现占位——P3 只换实现不动协议）。
- 客户端：NetChannelAdapter 接线、MessageDispatcher、ClockSync（D3 算法）、RemoteEntityRegistry。
- 登录闭环：Login → EnterWorld（serverTime/tickRate/playerId/出生点）；会话 Token 与重连重建流程协议一次定好；实体快照位掩码含动作/战斗状态标志。
- 联调方式定稿并落地一种（ParrelSync 或进程内 LoopbackTransport），不允许留白到用时再议。
- **验收**：双客户端登录进同 Zone，服务器日志确认两实体注册；ClockSync 输出稳定（偏移抖动 ±10ms 内，局域网/本机环境口径）；模拟断线重连现场正确重建。

### P2 移动同步闭环（规模：大，B+ 模型落地）

- 共享：SyncCore 的移动模型与校验规则（速度上限/瞬移阈值，两端同源）。
- 本地：角色按帧即时模拟不受网控；约 66ms 定时器节流上报（序列号递增）；收到 ack 后取历史预测帧比对——偏差 ≤ 阈值忽略，超阈值**平滑偏移衰减拉回**（明确不做输入重放，规避 Unity 物理确定性深坑）。
- 远程：远程实体快照缓冲 + 插值渲染（基准 133ms，D2 自适应带宽内浮动）。
- 服务端：MoveValidator 生效——越界瞬移/超速拒收并下发纠偏 + 审计日志。
- DebugWindow 新增网络面板：延迟注入滑条（0~300ms）、seq 对账计数、纠偏次数。
- **验收**：延迟注入下本地移动手感不发飘、纠偏肉眼平滑；作弊瞬移秒被拉回且留日志。

### P3 AOI 大世界化（规模：中大）

- 九宫格 AoiGrid 替换 PerceptionFilter 占位实现。
- 分级更新频率：近 15Hz / 中 8Hz / 远 4Hz；视野进出事件驱动远程实体生成销毁；迟滞边界防 entity 反复进出闪烁。
- BotRunner 压测：100~200 个移动 bot。
- **验收**：带宽门禁 = 按"可见实体数 × 平均快照字节 × 对应更新频率"公式折算的量化目标（P3 开工时计算并写入 GlobalGameSettings 作为硬门禁），实测达标且帧率不塌；"走近现身/走远消失"无抖动。带宽超标则启用坐标量化（D5 条件项）。

### P4 技能与战斗同步（规模：大）

- 共享：CombatRules 冷却/消耗/射程判定 + Hitbox 形状数据的 Protocol 序列化（判定几何两端同源）。
- 流向：CastRequest → 服务端校验（冷却/消耗/射程/施法者动作状态）→ CastBroadcast → 服务端权威命中裁决 → 伤害结果随命中消息下发（含 D4 RNG 结果）。
- 客户端：技能表现立即播放（预测反馈不打折手感），伤害数字等服务端结果驱动——现有 FloatText/DamageScreenEffect 事件驱动体系直挂即可。
- 反作弊边界明示：移动 B+ 的中等反作弊强度不影响本阶段——战斗裁决纯服务端权威，外挂伪移动会打不中人。
- **验收**：双端伤害数字一致且以服务器为准；伪造越射程/未冷却技能被拒绝并留审计日志。

### P5 怪物服务端化（规模：中，分两步）

- 过渡版（P1 已白得）：BotRunner 广播 NPC 快照，客户端纯表现，同时供 P3 压测制造真实感。
- 完整版：MonsterAI 决策/感知逻辑剥离 UnityEngine 依赖后移植服务端（进 SimCore 或服务端侧），巡逻/仇恨/攻击走 P2/P4 同步链路，打玩家触发 HitFSM 同步。
- **验收**：服务端驱动的巡逻+仇恨追击正常；玩家断线怪物世界持续运转。

### Out-of-Scope（显式排除，防范围蔓延）

背包/物品同步、聊天、组队、交易等系统级同步不在本路线图内；§3 协议治理规则的追加机制为其预留扩展空间。

## 6. B+ 移动模型决策记录（备查完整版）

| 维度 | A：服务器模拟移动 | B+：客户端移动+服务端校验（选定） |
|---|---|---|
| 手感 | 好 | 好（平滑偏移衰减，无跳变） |
| 反作弊强度 | 强 | 中等：超速/瞬移可查可拒，精细物理外挂难挡 |
| 工程量 | 大：Unity 物理回放确定性深坑（Raycast 地面检测在纠正后位置上结果不同） | 小：不重放输入，seq 对账 + 偏差阈值 + 平滑拉回 |
| 行业背书 | FPS 类标准 | 主流 MMORPG 传统折中路 |
| 补偿设计 | — | 战斗判定（P4）不受影响仍纯服务端权威；伪移动打不中人 |

选定理由：规避与 lockstep 否决理由同类的确定性泥潭（少返工）；手感无损；反作弊短板用 P4 权威裁决补偿；原草稿的消息形状（PositionSyncRequest 带 XYZ+Sequence）与预测帧比对思想原样成活。

## 7. 风险清单

| # | 风险 | 概率/影响 | 缓解 |
|---|---|---|---|
| R1 | KCP 流抖动突破插值缓冲上限 | 中/中 | D2 三层缓解；实测不达标则快照降频+加大缓冲（方案顺序预排） |
| R2 | 服务端 IO 线程与主循环数据竞争 | 高/高（不预防必踩） | game loop 架构硬规定：主循环单线程独占世界状态，IO 线程只入无锁队列 |
| R3 | HybridCLR 元代码裁剪砍掉共享库泛型 | 中/高 | link.xml 反射白名单维护；SyncCore 不向热更层暴露需 codegen 的泛型接口（asmdef 目录 README 写明纪律） |
| R4 | 双端 protobuf 库版本漂移 | 低/高 | Protocol README 锁版本 + CI/脚本校验（§3 规则） |
| R5 | 移动端 NAT 超时踢人 | 低/中 | 现有心跳 5s 覆盖主流 NAT 超时，仅记录不改 |
| R6 | 编辑器双开联调工作流摩擦 | 高/中 | P1 交付物之一：联调方式定稿并落地 |
| R7 | 范围蔓延（学习型项目首要死因） | 高/高 | Out-of-Scope 清单 + 每 Phase 验收门禁硬执行 |

## 8. 硬要求终验映射

| 要求 | 达成机制 |
|---|---|
| 少返工 | B+ 模型规避确定性回放深坑；§3 协议规则层冻结；PerceptionFilter 接口占位先行 |
| KCP+UDP 不变 | AOT 传输层零改动，仅收编消息定义 |
| 双端共用最大化 | Protocol/SyncCore 双 asmdef 共享源码 + 单仓库原子提交 |
| 先进且明白 | AOI/增量位掩码/自适应 jitter buffer/min-filter 时钟——每个决策附代价条款（D1-D6） |

---

*评审过程记录：技术路线比选（X/Y/lockstep）→ 用户定架构先行 → 三节呈现各含一轮用户审核修正（第 1 节：共享目录落位/AOT-Hotfix 引用方向陷阱/单仓库；第 2 节：B+ 模型定型/协议分层冻结/Out-of-Scope；第 3 节：插值表述统一/消息模式前提/RNG 机制归一）。*
