# SyncLayer —— 客户端网络接线与远端实体表现

## 入口与职责

`ClientNetworkHost.cs` 是游戏内网络接线的唯一入口：创建 adapter / manager / 远端实体渲染根并每帧驱动。
联网模式自动连接；登录进世界后装配本地角色，`MovementReporter` 上报，`NetMovementPolicy` 接管移动与平滑纠偏。
单机模式零影响。

设计与验收路线图：[network-sync-design.md](../../../../../docs/superpowers/specs/2026-08-27-network-sync-design.md)；
接线步骤见 [p2-client-network-host.md](../../../../../docs/superpowers/guides/p2-client-network-host.md) 与
[network-playtest.md](../../../../../docs/superpowers/guides/network-playtest.md)。

| 文件 | 作用 |
|---|---|
| `ClientNetworkHost.cs` | 唯一接线入口；联网时禁用本地 `MonsterSpawner` |
| `LatencyProbe.cs` | 应用层 RTT 探针（Heartbeat 1Hz 往返 + EMA；3 秒无回包显示 `--`） |
| `ClientNetworkManager.cs` | 连接 / 登录 / 进世界 / 断线重连 |
| `MessageDispatcher.cs` | 协议消息分发 |
| `ClockSyncDriver.cs` | 时钟同步驱动（算法在 `SyncCore`） |
| `MovementReporter.cs` | 位置上报（节流 + 预测帧） |
| `SkillCastReporter.cs` | 技能施法上报 |
| `CombatNetworkBridge.cs` | 远程伤害与格挡事件桥接 |
| `RemoteEntityRegistry.cs` | 远端实体注册与分档切换 |
| `RemoteInterpolator.cs` | 远端插值 |
| `RemoteTierPolicy.cs` / `TierQuery.cs` | Full / Proxy 分档策略与 URL 覆盖 |
| `RenderSpeedEstimator.cs` | 由渲染位移反推速度 |
| `RemoteEntityAnimator.cs` / `NpcEntityAnimator.cs` | 玩家 / bot 与怪物动画 |
| `INetworkLabelSink.cs` | 远程名牌/血条注入点（生产实现 `ClientNetworkHost.NameplateLabelSink` → `EntityDisplayManager`） |
| `NetworkTargetable.cs` | 远程实体只读 `ITargetable`（HP / 死亡 / 位置；不实现 `IDamageable`，本地伤害链够不到） |
| `NetworkTargetSelector.cs` | 联网目标锁定（`T` / 移动端「切换目标」按钮；15m 获取 / 20m 保持迟滞） |
| `PlayerNameRegistry.cs` | 登录名册缓存（服务端 `PlayerEnterRoom` → 远程名牌显示名） |
| `RemoteCastPresenter.cs` | 远程施法表现（攻击动画、施放 VFX、投射物停点/落点） |
| `NpcBodyCollider.cs` | 客户端玩家 vs 怪物阻挡 |
| `NpcAttackSchedule.cs` | NPC 攻击节奏 |
| `NetworkModeService.cs` / `ServerAddressQuery.cs` | 网络模式与服务器地址 |

## 属性剑气 v1（2026-11-14）

状态：Plan 01–04 已完成（Part A 取消广播/NpcAction；Part B 战斗投影版本合并、AOI 基线投影、
`ActiveCast` 补播与在途剑气补发消费；Plan 03 位移上报、跳跃边沿与本人控制消费；
Plan 04 权威状态投影消费、远端/本人状态染色与 DoT 分流）。
元素 VFX 资源化与远端武器模型仍属 Plan 05。

- **权威换装**：`ClientEquipmentState` 按 `EquipmentRevision` 应用 `EquipWeaponConfirm`；
  旧版本忽略。拒绝确认携带的较新摘要也会应用（恢复服务器当前装备）；登录基线可把版本 0
  作为起点。`CombatNetworkBridge.RequestEquip(instanceId)` 发送会话内递增的
  `ClientEquipSeq + KnownEquipmentRevision` 并返回序号（0 = 未发送）；宿主在 `OnEquipResult`
  里统一刷新 `WeaponBagController` 的标记与外观（`Sys3CEntry.SetAttackSpeed` / `ApplySwordView`），
  `OnEquipmentChanged` 只在 `TryApply` 真正更新摘要时触发。
- **权威剑气**：`CastBroadcast` 只驱动远端攻击动作；剑气视觉由
  `ProjectileLaunchBroadcast` 按 `CastId` 唯一创建（本人与旁观者同路径）。晚到发射按
  `ServerTimestamp` 与服务器时钟补飞行进度，终止先到时缓存并跳过后续生成，
  重复发射/终止按 `CastId` 幂等。`ProjectileStopBroadcast` 只停指定 `CastId`；
  旧服务端（`HitEntry.CastId == 0`）仍走 `DamageResult.StopX/StopZ` 兼容路径。
- **不重影**：`NetworkSwordQiGate.SuppressLocalSpawn` 在启用能力位的联机客户端为 true，
  `SkillExecutor.OnSwordQiFrame` 不再创建本地剑气；离线/单机保持 false。
- **场景**：`CombatSceneId` 使用登录 `ZoneId`；客户端忽略不同场景的发射/终止。
- **取消与 NpcAction 路由（Plan 02 Part A）**：`CombatNetworkBridge` 把 `CastCancelBroadcast` /
  `NpcActionEvent` 入队并在主线程转成 `OnCastCancel` / `OnNpcAction`；`ClientNetworkHost`
  装配时订阅、拆卸时退订，前者交 `RemoteCastPresenter.OnCastCancel`，后者交
  `RemoteEntityRegistry.ApplyNpcActionEvent`。
- **动作存储与合并**：`RemoteEntityRegistry` 按 `(EntityId, LifeId, InstanceId, Revision)`
  合并动作——实例号只增，同实例只接受更大的 `Revision`，生命代次不匹配丢弃；视图未建时
  先入 64 条有界缓存，视图创建后按 LifeId 过滤回放。AOI 基线在
  `EntityFieldMask.NpcAction` 置位时走同一合并路径。
- **取消表现**：`RemoteCastPresenter.OnCastCancel` 把 `CastId` 记入有界取消集，只停止匹配
  `CastId` 的攻击表现（`RemoteEntityAnimator.CancelAttack(CastId)`）；已发射的剑气继续飞行，
  晚到的 `CastBroadcast` / `ProjectileLaunchBroadcast` 跳过补播。
- **战斗投影路由与版本合并（Plan 02 Part B）**：`CombatNetworkBridge` 把
  `CombatStateUpdate` 入队并在主线程转成 `OnCombatStateUpdate`。`ClientNetworkHost` 先按
  `IsLocalProjection(localPlayerId, entityId)` 分流：**本人的投影在本地消费**——`ControlSummary`
  → `Sys3CEntry.ApplyAuthoritativeControl(SpeedScale, ActionLock, ControlVersion)`，
  `MaxHp > 0` → `SetMaxHp(fill:false)`；不再进 registry 的未知实体缓存。其余实体转交
  `RemoteEntityRegistry.ApplyCombatStateUpdate`。登录前 `localPlayerId == 0` 一律不算本人
  （否则未分配 id 会把所有实体当成自己）。
  `ApplyProjection` 按 `LifeId` + 单一 `CombatVersion` 原子合并：旧生命/旧版本丢弃，
  新生命先入 64 条有界缓存等该世代基线更新视图后回放，相同版本视为幂等重放仍应用
  （基线与 `Health` 快照同版本）。HP/Dead/IsDefending 同步 `Targetable`/名牌/Animator；
  `Equipment`/`Control` 只存储（不替换远端模型、不改 3C）；`Statuses` 为空列表即清空旧状态
  （Plan 04 起内容为权威冰/火/雷/风，见下文“权威状态表现”）。
- **AOI 基线投影**：`EntityFieldMask.Combat` 置位的基线快照走同一条 `ApplyProjection`；
  `EntitySnapshot.CombatVersion/LifeId` 随 `Health`/`Combat` 位下发。服务端每快照携带
  `Dead/Defending`，`Health` 是条件字段（Baseline / 血量翻转）——客户端仍按掩码位取值，
  `Health` 未置位时沿用 last-known。
- **视图生命周期的 last-known vitals**：视图 1.2s 无快照会被删，服务端 `HasSent` 却仍为
  true，重建后的 Keepalive/Changed 不会补 Health 基线。`RemoteEntityRegistry` 用有界
  （≤256）`Dictionary<long, LastKnownVitals>` 跨视图存活记录 LifeId/CombatVersion/Hp/MaxHp/
  Dead/IsDefending：快照按位合并、投影成功时全量更新；`CreateView` 在缺 Health/Dead/
  Defending 位时用**同 LifeId** 的 last-known 回填（快照带值优先）。`Remove`/`UpdateExpiry`
  故意不清，`Clear()`（换世界/断线）清空。
- **ActiveCast 补播**：`RemoteEntityRegistry.ActiveCastApplied` 同时覆盖 `CombatStateUpdate`
  与 AOI 基线，`RemoteCastPresenter.OnActiveCast` 按 `EndTick − ClientNetworkManager.ServerTick`
  换算剩余时长播放远程玩家攻击，`CastId` 与 `CastBroadcast` 共用去重/取消通道。Proxy 档
  没有 Animator 时不占去重槽，升 Full 后由 `SetTier` 用保存的 `ActiveCast` 补播一次；
  Full→Proxy→Full 的重复升档会被 `_activeCastPlayed` 去重挡住，不在补播例外内（已知限制）。
  已取消的 `CastId` 直接跳过；`EndTick <= ServerTick` 的过期投影不补播（晚到的基线不该
  重放早已结束的挥砍）；投影为 `null` 时不补播已结束的施法。服务端基线补发的在途剑气
  仍走同一条 `ProjectileLaunchBroadcast` 路径，按原始 `ServerTimestamp` 补飞行进度。
- **与攻击起手的分工**：Plan 02 保留 `CastBroadcast` → `NpcAttackSchedule` 作为 NPC 攻击起手；
  `NpcActionEvent` 只负责阶段/取消（技能动作仅跟踪实例，`Cancelled` 时停止），避免同一次施法
  挥两次。NpcAction 直接驱动攻击变体/时间轴留 Plan 05（Plan 04 未改这条分工）。

## 位移预算上报与控制消费（Plan 03，2026-11-18）

`MovementReporter` 通过宿主注入的 `Func<MovementReportState>` 读取本地 B5 状态；缺省
`default` 即旧上报（全零 → 服务端按 `None/Walk` 处理）。

- `MovementReportState`：`Mode`（Walk/Sprint）、`JumpSeq`、`Airborne`、`Grounded`、
  `VerticalVelocity`、`DisplacementSkillId`、`CastId`、`LifeId`、`ControlVersion`。
  `ClientNetworkHost` 的接线：模式来自 `CharacterController.Data.IsSprint`，跳跃来自
  `JumpSeq/IsAirborne/IsGrounded`，`DisplacementSkillId` 只在 `IsDashing` 时取当前技能，
  `CastId` 由 `CombatNetworkBridge.TryGetAcceptedCastId(skillId, LifeId)` 取服务端已确认值
  （仅 `IsDashing` 时查询，否则为 0；生命代次不匹配也返回 0），`LifeId/ControlVersion`
  来自装备摘要与 `LocalControlState`。
- **跳跃 Start 边沿只发一次**：`ResolveJumpPhase` 用 `JumpSeq` 去重（非 0 且变化才发 Start）；
  起跳与落地落在同一上报间隔时发 `None`（不制造幽灵起跳）；`_wasAirborne` 在真正接地时发一次
  `End`，空中发 `Air`。
- **复活清跳跃上报边沿**：`ResetForRespawn` 清零 `_lastReportedJumpSeq` 与 `_wasAirborne`。
  复活后控制器是重建的新角色（`JumpSeq` 从头计数），不清零会把第一次起跳压成 `Air`、漏报
  `Start`，服务端因此拿不到起跳 tick/水平速度。
- **CastConfirm 映射**：`CombatNetworkBridge` 只记录 `Accepted=true` 且 `ClientCastSeq != 0` 的
  确认；同一技能只接受不小于当前 `ClientCastSeq` 的确认（同序号幂等覆盖），按技能保存
  `(seq, CastId, CasterLifeId)`，供突进上报复用。
- **本人 311 消费**：`ControlSummary` → `ApplyAuthoritativeControl`，`MaxHp` → `SetMaxHp`；
  旧 `ControlVersion` 由 `LocalControlState` 忽略，“变为锁”的边沿清输入缓冲、打断技能、停突进。
- **位置响应**：`PositionSyncResponse.ControlVersion` 写入
  `MovementReporter.LastServerControlVersion`（诊断与后续基线判断的依据）。

回归：`MovementReporterFieldsTests`（字段逐项）、`CombatNetworkBridgeTests`（CastConfirm 记忆）、
`ClientLocalControlRoutingTests`（本人/远端分流）。

## 权威状态表现与 DoT 分流（Plan 04，2026-11-19）

- **远端状态投影**：`RemoteEntityRegistry.ApplyProjection` 合并成功后，仅在 `Statuses`
  列表变化时发 `StatusesChanged(entityId, target, statuses)`；`target` 是**当前可见渲染根**
  （`StatusVfxTarget`：FullRoot → active ProxyRoot → view.GameObject）。分档切换后
  `SetTier` 用新根对当前状态重放一次（初始建档由基线投影自带状态）。`ClientNetworkHost`
  把事件交给 `VFX/AuthoritativeStatusVfx`，
  由它按 `(EntityId, GroupId)` diff 后发 `StatusEffectAppliedEvent/RemovedEvent`；
  重复投影零事件，同组换效果先移除再应用。
- **本人状态**：311 的本人分支除 `Control`/`MaxHp` 外缓存 `projection.Statuses` 并对本地
  角色根 `Apply`；本地角色重建（新 transform）后 `ReplayLocalStatuses()` 用缓存重放，
  断开连接时清空缓存。渲染根变化（Proxy↔Full / 角色重建）由 `AuthoritativeStatusVfx`
  先对旧根发 Removed、再对新根发 Applied，染色不会留在已隐藏/销毁的旧模型上。
- **DoT 只走 Dot 通道**：`CombatNetworkBridge.ApplyDamageResult` 先判 `DamageKind.Dot`：
  本人 → `onDotDamageToLocalPlayer` → `Sys3CEntry.ApplyDotDamage`（只对齐权威余血 +
  `PlayerDotDamageEvent` 飘字）；远程 Npc → `MonsterDotDamageEvent`、远程玩家/bot →
  `PlayerDotDamageEvent`，并同步 `Targetable` 余血/死亡。Dot 分支**不**调用 `PlayHit`、
  不发 `BlockSucceededEvent`/`MonsterTakeDamageEvent`/`PlayerTakeDamageEvent`，
  因此没有受击硬直、格挡、击退、命中火花、泛红或震屏；直接命中（缺省 `Direct=0`）路径不变。
- **重复命中只生效一次**：`CombatNetworkBridge.ApplyDamageResult` 按 `HitEntry.EventId`
  （服务端跨重试唯一；0 = 旧服务端未分配，不去重）做有界去重（最近 1024 条）；
  `ClearDedupe()` 在断线/重建桥的路径清空窗口。同一命中的重投不会二次触发受击/飘字/
  权威余血写入。

回归：`DotDamageRoutingTests`（7 项，本地/远程 DoT 事件隔离）；重复/乱序收包
（同 `EventId` 重投、旧版本 311、终止先于发射、重复 CastBroadcast）由
`NetworkConsumptionInjectionTests` 覆盖；换根重放由
`AuthoritativeStatusVfxTests.test_remote_entity_registry_replays_statuses_when_tier_changes_render_root`
覆盖；投影版本合并（高版本应用/低版本丢弃、空状态清空、LifeId 基线等待）由
`CombatStateProjectionTests` 覆盖。

## 联网复活

- 联网复活在主线程应用服务端落点。先清空旧预测帧和平滑纠偏，再传送并上报。
  位置响应的复活 tick 必须匹配当前周期，迟到的纠偏不会跨复活生效。

## 默认联网与玩家名

- `NetworkModeService.Load()`：无 `net.mode` 存档时默认 `Online`；已有存档（含 `SinglePlayer`）
  优先。连接失败 / 登录被拒时提示 3 秒后回选人界面，**不改写模式存档**，面板仍按联网预填。
- 玩家名：`net.username` 无存档时由 `RandomNicknameGenerator` 首次生成并持久化（随机中文前缀 +
  后缀，四字）；已有存档原样使用，不迁移旧 `"Player"`。

## 联网 HUD 读数（FPS + 延迟）

- 仅联网进世界后显示；`ClientNetworkHost` 每 0.5 秒把 `FPS xx  延迟 xxms` 推给
  `PlayerHudPanel.SetNetStats`，断线 / 回选人时置空隐藏。单机默认不显示。
- 延迟 = `LatencyProbe` 的平滑 RTT：每 1 秒发一条 `Heartbeat`（服务端原样回显），
  按 `Sequence` 匹配；回包到达时刻在回调线程用 `Stopwatch` 采样，避免把等待下一帧算进 RTT。
  3 秒无匹配回包显示 `延迟 --`。
- 左下角调试浮层保留原有 `FPS=` 与分档读数（任务 10 实测口径不变）。

## 远端实体表现

| 实体 | 路径 | 退化 |
|---|---|---|
| 玩家 + bot | `_appearances` 外观目录（留空退回 `_playerAvatarPrefab`）→ 真模型 + `RemoteEntityAnimator` | 两者都空 → 胶囊占位 |
| 怪物 | `_npcPrefabs`（templateId → 预制体）→ 真模型 + `NpcEntityAnimator` + `NpcBodyCollider` | 未命中 → 球体占位 |

- bot 与真人走同一条真模型路径，不再是胶囊。
- 分档：进入 60m / 退出 69m（迟滞）+ 每帧 `MaxFull` 预算（最近优先）。
  Full = 真模型，Proxy = 代理（未配置则运行时胶囊），整棵剥碰撞体。
- 升档必须当场 `ApplySnapshotState(view.Dead)` + `Reapply()`（并补 `SetDefending(view.IsDefending)`）：
  `m_KeepAnimatorStateOnDisable: 0` 会重置 Animator，只做“失效去重”会让死亡 / 受击中的实体先站直最多约 264ms。
- URL 旋钮：`?full=<N>`、`?fulldist=<米>`、`?quality=mobile`；`mobile` 别名无条件生效。
- 调试浮层第二行：`Full=… Proxy=… B=… Q=… FPS=…`（B = 预算，Q = 质量档）；约 390px 竖屏也必须完整可见。

## 远程战斗表现与目标锁定

- **权威 vitals**：服务端每快照携带 `Dead/Defending`（状态语义），`Health` 是条件字段；
  `RemoteEntityRegistry.ApplySnapshot` 按掩码位更新 `view.Hp/MaxHp/IsDefending/Dead`，再推给
  `NetworkTargetable.SetVitals`；视图因缺席超时删除后重建时，用同 LifeId 的 last-known 回填
  （见上文）。`INetworkLabelSink` 同步名牌血条（bot 不注册名牌）。`NetworkTargetable.CurrentHP` 是显示口径
  `Max(0, CeilToInt(Hp))`——服务端余血可为负（`ApplyDamage` 不钳制），UI 口径钳到 0。
- **远程死亡序列**：`RemoteEntityAnimator` 状态机 `Death`（倒下）→ `Down`（躺卧，霸体）→
  `GetUp` → `None`。`Dead` 是**状态**不是边沿事件，重复 `dead=true` 不重置死亡计时；
  复活由服务端 8 秒 `RespawnNotice` / 快照驱动。`Death`/`Down` 期间攻击/受击不接，
  `GetUp` 可被打断。
- **死亡/起身时长**：进入 `Death` / `GetUp` 后的下一帧，驱动读 Hit 层真实剪辑长度并校准为
  `length + 2 帧`（30fps 导入帧率）；拿不到剪辑才回退常量（Death 0.7s / GetUp 0.9s）。
  GetUp 兜底与本地 `FSMConfig.GetUpDuration`（0.8334s + 2 帧）同口径——旧 0.5s 会把旁观端
  起身截掉 40%。真实控制器契约测试见 `RemoteEntityAnimatorContractTests`。
- **远程普通受击**：真实 `Character3C.controller` 的 Hit 层从 `Empty` 进 `Hit` **只认 Trigger `Hit`**
  （`HitState == 1` 没有 AnyState 转移）；驱动在写权重/整数后补点一次 Trigger，并用
  `_hitTriggerApplied` 去重，离开 Hit / 进入攻击 / 回 Locomotion 时 `ResetTrigger`。
  `Death` / `Down` / `GetUp` / `DefendHit` 仍走整数 AnyState 转移。
- **怪物攻击变体**：`CastBroadcast.SkillId` 为已知怪物技能时固定映射到 Attack01/02
  （50001/50003/50004 → Attack01，50002/50005/50006 → Attack02）；未登记技能回退
  `NpcAttackSchedule.VariantOf(serverTicks)` 的时间戳桶变体，保证旧服务端/未来技能仍能播放。
- **投射物对齐**：`RemoteCastPresenter` 在 `CastBroadcast` 到达即播攻击动画与施放 VFX
  （不等 `DamageResult`，否则零命中延迟技能会先看到受击）；火球在 `DamageResult.HitX/HitZ`
  爆炸，被格挡的剑气在 `StopX/StopZ` 停住；20004（蓄力爆发）与 30001（陨石）直接在服务端
  落点生成释放/陨石表现。本地施法者与 NPC 由各自执行器负责，这里一律跳过。
- **目标锁定**：`T` 键 / 移动端「切换目标」按钮经 `InputManager.IsTargetSwitchPressed()` /
  `SetTargetSwitchPressedFromUI()` 进入 `NetworkTargetSelector.Tick`；候选为视野内 Player/Npc，
  `AcquireRange=15m`、`DropRange=20m` 迟滞；目标死亡、出 AOI 或超距自动清锁并解绑面板/选中环。
  施法前宿主调 `EnsureTargetFor(skillData)`：无锁定按技能形状选最近候选，`LockedTargetId` 随
  `CastRequest.TargetPlayerId` 上线，服务端按显式目标裁决（形状/距离校验不变）。
  候选筛选与施法前复核使用**表面距离**（中心距 − 目标半径）和同一角扩展
  （`半角 + asin(半径/中心距)`）；大体型目标贴脸时不丢锁。
- **点按瞄准的目标来源**：`ClientNetworkHost` 通过 `SetSelectedTargetPositionProvider` 注入
  `NetworkTargetSelector.TryGetLockedPosition`；HoldToAim 火球点按（未拖动）时落点优先取锁定目标
  位置（按 15m 射程钳制），无锁定才回落到角色前方最大射程。移动端瞄准拖拽会覆盖该默认落点。
- **旋转取消上报**：本地 `SkillCoordinator.OnSkillCancelled`（同键取消/受击打断）→
  `SkillCastReporter.ReportCancel` → `CancelCastRequest`（仅 `SpinSkillData`）。服务端丢弃
  未到期判定段，避免已取消的旋转继续扣血。
- **远程火球按广播落点飞行/爆炸**：`RemoteCastPresenter.OnCastBroadcast` 只在
  `ProjectileSkillData.HoldToAim` 时把 `CastBroadcast.ImpactX/Y/Z` 记为投射物终点；
  `DamageResult` 到达后用该终点 `ExplodeAt`（不退回受击者位置），与服务端落点圆判定同源。
  20004 / 30001 等落点类技能直接在广播落点生成释放/陨石表现。

## 客户端碰撞（玩家 vs 怪物）

- `NpcBodyCollider` 在运行时按模型 bounds 生成，挂在怪物视图上；预制体本身不挂碰撞体（有资产断言）。
  `BodyRadius` 由模型 XZ 较大半宽推导，供目标锁定使用。
- `NpcBody` 层索引 **10**；碰撞矩阵只放行本地玩家层 9；相机防穿墙掩码必须排除它。
- 改层 / 矩阵前先关 Unity（2022.3 会按层名缓存回写 `TagManager.asset`），代码一律用层索引。
- 已知限制：碰撞只在客户端；会移动的怪可走进静止玩家，最坏约 0.48m 交叠。

## 测试

Editor：`Assets/Scripts/Tests/Editor/SyncLayer/`（`RemoteTierPolicyTests`、`RemoteInterpolatorTests`、
`RemoteEntityRegistryTests`、`NpcBodyColliderTests`、`AppearanceCatalogTests`、`TierQueryTests`、
`NpcViewGameplayStripTests`、`MonsterBodyRadiusTests`、`RemoteEntityAnimatorContractTests`
（真实 `Character3C.controller`：Hit/DefendHit/Death/Down/GetUp 状态与剪辑时长）、
`MovementReporterFieldsTests`、`ClientLocalControlRoutingTests`、`CombatNetworkBridgeTests`、
`DotDamageRoutingTests` 等）。
PlayMode：`RemoteRenderBranchTests`、`RemoteHitSnapshotPersistenceTests`（D6：`ActionState=0`
快照不清受击表现）、`MovementSyncTests`、`CombatSyncTests`、`TwoClientWorldTests`、
`ThreeClientCombatConsistencyTests`（A 打 B、C 旁观的 DamageResult 与 Targetable 余血一致性）。

**人工/集成验收**（2026-11-01 浏览器 E2E 更新；证据在 `production/verify/e2e/`，该目录被 gitignore）：

已执行：

- **观察者扩播**：A 打 B 时，施法者 / 受击者 / 观察者收到同一批 `CastBroadcast` 与
  `DamageResult`，伤害与余血逐值一致；观察端看到伤害数字与命中火花。
- **目标锁定**：移动端「切换目标」后目标面板 + 选中环出现；`CastRequest.TargetPlayerId`
  随包上线，服务端命中该锁定目标。
- **死亡 → 复活**：观察端看到受击与倒地；受害者约 8 秒后收到 `RespawnNotice` 并复活。
  修复前服务端 `TickRespawns` 对死者 `continue`、`TryRespawn` 永不执行——受害者等 15 秒
  无通知后掉回选人界面；已由 `7327d89` 修复，回归测试
  `RespawnTests.Dead_Player_Respawns_Through_The_Tick_Sweep`。**起身姿势本身未单独截帧**
  （受害者复活时传送回出生点、与其他玩家重叠）；状态映射仍由
  `RemoteEntityAnimatorContractTests` 在真实控制器上覆盖。
- **390px 竖屏**：移动 UA + 390×844 视口下「切换目标」按钮与技能键不重叠。
- **远程施法不打断本地技能**：B 的火球（20003）在收到 A 的 Q `CastBroadcast` 期间完成，
  并命中 A（20 伤害）；服务端与客户端都看到同一帧结果。该轮还修掉了火球的
  `shape-tampered`——`ProjectileSkillData` 无 `ShapeBlock`，旧代码回退到 `new ShapeBlock()`
  的默认值（Range 2/Angle 120），被 20003 表项（maxRange=0/maxAngle=0）全部拒掉；
  已由 `e5db046` 修复，回归测试 `HitShapeConverter_Projectile_Sends_Zero_Shape`。
- **火球落点**：服务端 `DamageResult.HitX/HitZ` = 受害者位置（与快照一致），施法者与受击者
  收到同一帧；投射物 VFX 在客户端可见。
- **格挡**：B 举盾时 A 的普攻 `IsBlocked=true`，伤害 50→15（暴击 75→22.5）。

仍未执行 / 已知限制：

- **被格挡剑气的停点帧未抓到**：无头 CDP 模拟的右键按住约 4 秒后会被浏览器清掉，第二轮
  重击时服务端已看到 `IsDefending=false`；停点逻辑由
  `CombatResolverSwordQiBlockTests`（`ProjectileStopped`/`StopPosition`）与
  `OnDamageResult_ProjectileStopped_*` 覆盖。
- A 打怪 → C 观察怪物血条 / 受击 / 死亡的验收**未完成**：本轮 A 走到怪前被四只怪连续命中，
  攻击输入被受击状态吃掉；观察者侧已确认能收到怪物 → 玩家的 `DamageResult` 扩播。
- 浏览器 E2E 清单其余条目见
  [`task-12-report.md`](../../../../../.superpowers/sdd/2026-10-31-multiplayer-combat-consistency/task-12-report.md)
  第 5 节。

```bash
bash tools/unity-tests.sh EditMode RemoteTierPolicyTests
bash tools/unity-tests.sh PlayMode RemoteRenderBranchTests
```

## 相关模块

- [SyncCore](../SyncCore/README.md) —— 共享同步规则
- [服务端移动](../../../../../Server/Movement/README.md) —— ORCA 避让
- [Sys3C](../Sys3C/README.md) —— 本地角色与移动
- [Monster](../Monster/README.md) —— 客户端怪物模块

## DemoDay 简易武器背包

联网宿主在装备基线可用后显示背包按钮，确认后刷新当前条目与本地持剑外观。断线时销毁面板。

- `CombatNetworkBridge.RequestEquip(long)` 返回发送的 `ClientEquipSeq`（0 = 未发送）；
  构造时可注入连接级 `ClientEquipmentState` 与起始序号，角色重建复用同一摘要。
  `OnEquipmentChanged` 只在 `TryApply` 真正更新时触发；宿主用 `OnEquipResult` 作为唯一刷新入口。
- `ClientNetworkHost` 持有 `WeaponBagController`：单在途请求、5 秒超时、拒绝原因、
  缺失外观提示、技能/死亡期间延迟应用最新外观。`PlayerSpawnedEvent` 保留装备摘要与在途序号，
  重建后把同一份状态应用到新角色；`Disconnect` 重置会话并销毁面板。
- 登录基线 `WeaponInventoryBaseline` 仍先 `ApplyServerWeapons` 建列表，再在本地角色装配时
  `InitializeFromBaseline`；列表标记与持剑外观都读 `ClientEquipmentState`。
- 回归：`WeaponBagViewTests`、`ClientEquipmentStateTests`、`BagWeaponInventoryTests`。
