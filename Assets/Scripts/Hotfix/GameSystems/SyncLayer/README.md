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

- **权威 vitals**：`RemoteEntityRegistry.ApplySnapshot` 只在 `FieldMask.Health` / `Defending` 置位时
  更新 `view.Hp/MaxHp/IsDefending`，再推给 `NetworkTargetable.SetVitals`；`INetworkLabelSink`
  同步名牌血条（bot 不注册名牌）。`NetworkTargetable.CurrentHP` 是显示口径
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
（真实 `Character3C.controller`：Hit/DefendHit/Death/Down/GetUp 状态与剪辑时长）等）。
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
