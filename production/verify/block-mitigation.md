# 服务端格挡减伤（D-P4-8 落地）

2026-10-17。联网模式下"举盾完全无效"修复：防御姿态经位置上报链路进入服务端权威裁决，
格挡成功 ⇒ 伤害 × 0.3 + 扣盾耐久 + `HitEntry` 格挡标志回报；客户端据标志走既有格挡受击表现。

## 协议字段与 tag（累积式：只加不删）

| 消息 | 字段 | tag | 语义 |
|---|---|---|---|
| `PositionSyncRequest` | `bool IsDefending` | **8** | 客户端上报防御姿态（1..7 已被 X/Y/Z/Rotation/Speed/Timestamp/Sequence 占用）。缺该字段的老客户端读到 false = 未防御（全额伤害，安全方向） |
| `HitEntry` | `bool IsBlocked` | **7** | 这一击被格挡（`Damage` 已按 `BlockDamageScale` 减免；`TargetRemainingHp` 仍是**扣后权威余血**，格挡分支同样成立）。缺该字段的老客户端读到 false = 普通受击 |

两个 tag 各有 EditMode 反射钉（`ProtocolShapeGuardTests`）——protobuf-net 对"挪号成另一个唯一号"不报错，
只有钉住才防静默解错字段。往返有 EditMode + 服务端两侧用例。

## 表值（`CombatRules`，双端同源编译；与判定延迟同一"表值自持"作风）

| 常量 | 值 | 说明 |
|---|---|---|
| `BlockDamageScale` | `0.3f` | 格挡后伤害倍率 |
| `BlockHalfAngleDeg` | `60f` | 格挡锥**半角**（锥全角 120°；与 `HitShape.Angle` 的全角语义不同，注释里写了勿混） |
| `ShieldMaxDurability` | `100f` | 服务端盾上限（登录重建/复活回满） |
| `ShieldDurabilityPerBlock` | `25f` | 每格挡消耗（100/25 = 4 次后耗尽，归零后不再减伤） |

## 判定几何（`CombatRules.IsBlocked(defenderYawDeg, dx, dz)`）

- yaw 语义与 `HitResolver` 一致（yaw=0 → +Z；sin/cos 展开，天然 wrap 安全），零 `UnityEngine` 依赖（只用 `System.Math`）。
- `dx/dz` = **攻击者相对受击者的偏移**（`casterX - targetX, casterZ - targetZ`）；受击者朝向与"受击者→攻击者"方向夹角 ≤ 60°（含边界）即格挡。
- **零距离视为格挡**——沿用 `HitResolver` 的退化护栏惯例（D-P4-2a）：贴脸时方向向量趋零、归一化不可靠，不修会出现"贴脸永远无法格挡"。

## 改动点（逐文件）

**协议**
- `Assets/Scripts/AOT/Protocol/Messages/PositionSync.cs` — `PositionSyncRequest.IsDefending`（tag 8）+ 只加不删契约注释。
- `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` — `HitEntry.IsBlocked`（tag 7）+ 语义/契约注释。

**共享表**
- `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` — 4 个常量 + `IsBlocked`（含信任面 XML 登记）。

**服务端**
- `Server/Player/PlayerState.cs` — `IsDefending` / `ShieldDurability`（初值满）/ `ConsumeShieldDurability`（下限 0）；`TryRespawn` 回满盾。
  恢复口径（实现选择，已注释）：**不做战斗内恢复**，登录重建 + 复活回满（与 Hp"复活即满"同语义）。
- `Server/Handler/PositionSyncHandler.cs` — `ValidateAndApply` 写 `player.IsDefending = request.IsDefending`；**故意不进 `MovementValidator`**（位置校验与防御无关）。
- `Server/Handler/CastHandler.cs` — `ResolveHits`：扣血前判定 `target.IsDefending && ShieldDurability > 0 && IsBlocked(target.Rotation, casterX-targetX, casterZ-targetZ)` ⇒ `damage *= BlockDamageScale`、扣耐久、`IsBlocked=true`。
  **掷骰顺序不变**（暴击照掷、格挡最后乘倍率——固定种子序列不漂移）；`TargetRemainingHp` 仍是扣后权威余血。类头注释登记"格挡减伤"环节。

**客户端**
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs` — 新增 `defendStateProvider`（照 positionProvider 惯例注入，不在这里 FindObjectOfType）；`Report()` 填 `IsDefending`。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs` — `AttachMovementReporter` 透传 `defendStateProvider`（可选参，既有调用零改动）。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` — 宿主接线 `defendStateProvider: () => controller.IsDefending`（本地真值源 = 3C `CharacterController.IsDefending`）；战斗桥回调加 `blocked` 透传。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` — `onDamageToLocalPlayer` 委托加第 6 参 `isBlocked`，`DamageResult` 处理时原样下传；远程实体分支沿用既有飘字链路（见"未做"）。
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` — `ApplyServerDamage(..., bool isBlocked = false)`：格挡分支复用单机格挡同款挂点
  `HitFSM.EnterDefendHit` + `Coordinator.SetActiveLayer(LayerType.Hit)`（**不新增状态/动画参数**）；浮字不发（与单机格挡一致）；HP 对齐/死亡判定照旧。本地盾耐久不在此扣（服务端盾才是权威）。

## 测试证据（命令 + 输出）

1. `dotnet test Server/Server.Tests/Server.Tests.csproj`
   → **246 通过 / 0 失败**（基线 236 + 新增 10：`Combat/BlockMitigationTests.cs` 5 条 + `SyncCore/CombatRulesTests.cs` 5 条）。
   新增覆盖：① 防御中+锥内 ⇒ 伤害 = BaseDamage × 0.3（精确值 + 余血 + 耐久 75）② 背对 ⇒ 全额 ③ 未防御 ⇒ 全额（回归）
   ④ 4 次格挡后第 5 击全额（耐久 0）⑤ 固定种子双世界 ⇒ 暴击逐击相同、格挡伤害 = 非格挡 × 0.3（掷骰顺序不变）；
   `IsBlocked` 的锥内/锥外/边界（60° 含边界，位级可复现）/零距离/yaw 环绕（350° vs 10°）。
   既有往返用例加强：`CodecRoundtripTests.Roundtrip_PositionSyncRequest_PreservesFields`（+IsDefending）、
   `CombatMessagesTests.DamageResult_Roundtrip_Preserves_Hits`（+IsBlocked 两方向断言）。
2. `unity-mcp-cli run-tool assets-refresh --input '{"options":"ForceSynchronousImport"}'` → 无编译错误。
3. `unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}' --timeout 600000`
   → **397 通过 / 0 失败**（基线 392 + 新增 5：往返 ×2 + tag 钉 ×2 + `CombatNetworkBridgeTests` 格挡标志下传 ×1）。
4. PlayMode（**跑到了**）：`tests-run {"testMode":"PlayMode","testClass":"BlockMitigationE2ETests","includeLogs":true}`
   → **通过**，日志实测：`未防御 50.0（余血 50.0） → 防御中 15.0（余血 35.0，格挡=True）`（= 50 × 0.3，余血 = 50 − 15，双端同值同标志）。
   装置：双 `ClientNetworkManager`（照 CombatSyncTests）但每帧驱动；B 的位置/朝向/防御标志全部经真实
   `MovementReporter` 上报链上行（防御标志由 `defendStateProvider` 注入），A 用 10001 显式目标砍 B：
   先未防御（对照组全额）→ 翻防御标志 → 再砍（格挡 + 减免）。期望值从 `CombatRules` 表读，不写死数字。

### 服务端重启（必要，用户可能正在玩）

PlayMode 需要带格挡逻辑的服务端：已 `dotnet build Server/Server3C.csproj` 后**杀掉 8000 上的旧进程并重启**
（`dotnet run --no-build --project Server/Server3C.csproj -- 8000 --monsters 4 --bots 40 --bots-radius 60`，
stdout 留档 `production/verify/block-server.log`）。重启后行为与旧构建的唯一差异 = 防御中受击者的减伤/耐久/标志。

### 全套 PlayMode 顺带结果（与本次改动无关的既有 flake）

整包 33 条跑了一遍：31 过、2 败——都不是本改动路径（两条都不涉防御，`IsDefending` 恒 false ⇒ 行为逐字等同旧码）：
- `MonsterWorldTests.Monsters_Patrol_Chase_And_Attack_Server_Authoritative`（"damage source X != wolf id Y"；
  `production/verify/t13/_playmode_monsterworld_rerun.log` 有同款历史失败）。
- `AoiWorldTests.Bots_Enter_And_Leave_Sight_Over_Time`（"registry count never dropped below peak N"；
  `production/verify/playmode_monsters*.json` 有同款历史失败）。这两个用例对服务器配置敏感，本次按任务书
  的 `--bots 40 --bots-radius 60` 启动。未在改动前另跑对照（旧构建无法验证下行标志），此判断基于失败消息与改动路径无交集。

## 信任面登记（诚实口径）

`IsDefending` 由**客户端上报**（随位置同步链路上行），防守朝向（`Rotation`）同样来自客户端上报的位置同步——
服务端只做**几何 + 耐久**校验：不做输入真实性证明、不校验"防御动作是否播放/是否被允许"。
这与既有"客户端权威位置 + 服务端部分校验"是同一哲学（`MovementValidator` 的边界就是本裁决的边界）；
**不是防作弊闸门**，不要写成"防作弊"。三处注释已登记：`CombatRules.IsBlocked`（XML）、
`CastHandler.ResolveHits`（循环内）、`PositionSyncHandler.ValidateAndApply`。

## 未做/边界（有意为之）

- **远程实体被格挡**：沿用既有飘字链路（伤害已是减免值），不做远程格挡动画——协议没有"远程防御姿态"通道，
  表现只做本地玩家（任务书允许，此处写明）。
- **本地盾 vs 服务端盾**：`CharacterController` 的本地盾（上限 50，服务单机权威战斗）与服务端盾（100）**不互抄、不同步**；
  联网时客户端本地盾耐久不随服务端格挡扣除。
- 格挡不减控制效果、不改打断/霸体语义（P4 伤害下发无 Knockback）。
- 不做战斗内耐久恢复（见 PlayerState 注释）。
