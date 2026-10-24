# 格挡成功反馈设计（火花 + 飘字）

**Date:** 2026-10-25
**Engine:** Unity 2022.3.25f1
**Layer:** `Hotfix/Sys3C`、`Hotfix/SyncLayer`、`Hotfix/Nameplate`、`Hotfix/VFX`
**状态:** 待实现

---

## 0. 结论摘要

举盾格挡成功目前只有 0.4s 的 `DefendHit` 动画和 15 点精力消耗，玩家感知不到"防住了"。

本设计新增一个 `BlockSucceededEvent`，在三条格挡成功分支发射，由两个现有表现系统订阅：

1. `DisplayEventBridge` → 头顶金色「格挡」飘字（复用闲置的 `FloatText_Block.asset`）
2. `HitFeedbackSystem` → 盾面/接触点金色火花（复用现有 `HitSpark.prefab` 池）

覆盖范围：本地玩家（单机 + 联机）与远程玩家/bot（攻击者与防御者两端可见）。注意：bot 的代码路径已具备，但当前 bot AI 不会举盾（见 §5-8）。

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| 反馈类型 | 火花 + 飘字 | 用户选择 |
| 可见范围 | 本地玩家 + 远程玩家/bot | 用户选择 |
| 远程格挡的伤害数字 | 取消，改为显示「格挡」 | 用户选择"只显示格挡" |
| 飘字资源 | 复用 `FloatText_Block.asset` | 已存在但从未接线，零新美术资源 |
| 火花资源 | 复用 `HitSpark.prefab`，金色、缩放 0.8 | 零新美术，与飘字同色 |
| 实现方式 | 新增独立事件，不改受击事件 | 避免污染受击语义，既有订阅者零改动 |
| 音效 / 屏幕闪光 / 震屏 / 顿帧 | 不做 | 用户未选；音效需先新增音频系统 |
| 远程玩家/bot 的格挡动画 | 不做 | 协议无该同步通道（既有 backlog） |
| 第三方旁观者可见 | 不可见 | 服务端 `DamageResult` 只发 caster + 受击者 |

---

## 1. 现状与证据

| # | 事实 | 出处 |
|---|---|---|
| 1 | 单机格挡成功只做两件事：`EnterDefendHit` + 扣 15 精力 | `Sys3C/Sys3CEntry.cs:717-727` |
| 2 | 联机本地玩家格挡成功同样只做这两件事 | `Sys3C/Sys3CEntry.cs:616-635` |
| 3 | 本地格挡分支**不发** `PlayerTakeDamageEvent`，所以没有伤害数字、没有受击火花、不闪屏 | `Sys3C/Sys3CEntry.cs:616-635`、`717-727` |
| 4 | `FloatTextType.Block` 已定义，渲染器已有 Block 漂移分支 | `Nameplate/FloatTextConfig.cs:9`、`FloatTextRenderer.cs:137-145` |
| 5 | `FloatText_Block.asset` 已存在（金色 `(1,0.84,0)`、28pt、0.6s），但全项目无任何引用 | `Assets/PreRes/Settings/Display/FloatText_Block.asset:15-22` |
| 6 | `HitFeedbackSystem` 只订阅 `MonsterTakeDamageEvent` / `PlayerTakeDamageEvent` | `VFX/HitFeedbackSystem.cs:38-48` |
| 7 | `HitSpark` 支持运行时改 `startColor` 与缩放 | `VFX/HitSpark.cs:17-26` |
| 8 | `PlayerBodyTint` 只处理 `EntityId == 本地玩家实例ID` 的事件，远程实体不受影响 | `VFX/PlayerBodyTint.cs:60-64` |
| 9 | 联机远程目标分支当前对玩家/bot 发 `PlayerTakeDamageEvent`（灰字伤害数字） | `SyncLayer/CombatNetworkBridge.cs:122-144` |
| 10 | 服务端只对 Player 目标判格挡（受击者防御中 + 攻击者在正面锥内） | `Server/Handler/CastHandler.cs:302-308` |
| 11 | `DamageResult` 收件人 = 施法者 + 每个受击者，不含旁观者 | `Server/Handler/CastHandler.cs:446-456` |
| 12 | `EntityDisplayManager` 的四个飘字配置都在 DemoDay 场景里手接；全项目只有 DemoDay 挂了该组件 | `DemoDay.unity:7230-7233` |
| 13 | `DefendHit` 动画是真实盾牌受击动作（`DefendHit_SwordAndShield.fbx`），动画链路本身正常 | `RpgDuo/Animator/Character3C.controller:1084` |
| 14 | 联机本地受击回调的 `hitDirection` 恒为 `Vector3.zero`（协议无方向字段） | `SyncLayer/CombatNetworkBridge.cs:119` |
| 15 | 单机格挡分支可获得真实接触点 `resolved.HitPoint`（世界坐标） | `Sys3C/Core/Combat/HitContact.cs:14-19`、`Sys3CEntry.cs:685-688` |
| 16 | 服务端 bot（`BotRunner`）只更新位置/朝向，**从不设置 `IsDefending`** → bot 当前永远不会格挡 | `Server/Aoi/BotRunner.cs:91,137`、`Server/Player/PlayerState.cs:34` |
| 17 | 本地玩家非格挡受击的飘字是**灰色**（0.85 灰、58pt），不是红色 | `FloatText_PlayerDamage.asset:18-21`、`DisplayEventBridge.cs:39-43` |
| 18 | `HitFeedbackSystem` 也只挂在 DemoDay 场景 → 火花与飘字都只在 DemoDay 生效 | `DemoDay.unity:7573` |

---

## 2. 目标与非目标

### 目标

1. 本地玩家格挡成功时，头顶出现金色「格挡」飘字，盾面/接触点出现金色火花。
2. 远程玩家/bot 格挡成功时，攻击者客户端同样看到「格挡」飘字与火花。
3. 远程玩家/bot 被格挡时不再飘减免后的伤害数字，改由「格挡」替代。
4. 同一实体 0.2s 内的多段格挡只显示一次飘字（火花仍逐次播放）。
5. 不改格挡判定、减伤系数、精力消耗与 `DefendHit` 动画。
6. 非格挡命中、暴击、怪物受击的表现完全不变。

### 非目标

- 音效（项目当前没有音频播放系统）。
- 屏幕边缘闪光、镜头震动、顿帧。
- 远程玩家/bot 的格挡**动画**同步（协议无通道）。
- 旁观者可见（需扩展服务端广播范围）。
- 盾牌材质高亮、身体泛红。
- 格挡失败的反馈（未举盾/背面受击仍走普通受击）。

---

## 3. 架构与数据流

```
【单机】怪物 → 本地玩家
  Sys3CEntry.IDamageable.TakeDamage
    └ DefendModifier 判定格挡成功
        └ Emit BlockSucceededEvent(本地玩家, resolved.HitPoint, 头顶)

【联机】攻击者 → 本地玩家（服务端判 IsBlocked）
  CombatNetworkBridge → onDamageToLocalPlayer
    └ Sys3CEntry.ApplyServerDamage(isBlocked: true)
        └ Emit BlockSucceededEvent(本地玩家, 胸前近似点, 头顶)

【联机】攻击者 → 远程玩家/bot（攻击者客户端收到 IsBlocked）
  CombatNetworkBridge.ApplyDamageResult
    └ hit.IsBlocked == true
        └ Emit BlockSucceededEvent(远程实体, 胸点, 头顶)
        └ 跳过 PlayerTakeDamageEvent（不再飘伤害数字）

BlockSucceededEvent
  ├─ DisplayEventBridge.OnBlockSucceeded → FloatTextRenderer.ShowFloatingText("格挡")
  └─ HitFeedbackSystem.OnBlockSucceeded  → HitSpark.Play(金色, 0.8)
```

事件是纯表现层事实，不参与伤害结算，不发包，不改协议。

---

## 4. 详细设计

### 4.1 新事件

新增文件 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/BlockSucceededEvent.cs`：

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Events;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>格挡成功（纯表现事件，不参与结算）。</summary>
    public struct BlockSucceededEvent : IEvent
    {
        public int EntityId;          // 防御者实例 ID（飘字去重用）
        public Vector3 HitPosition;   // 火花位置：盾面/接触点
        public Vector3 TextPosition;  // 飘字位置：头顶

        public BlockSucceededEvent(int entityId, Vector3 hitPosition, Vector3 textPosition)
        {
            EntityId = entityId;
            HitPosition = hitPosition;
            TextPosition = textPosition;
        }
    }
}
```

### 4.2 三个发射点

| # | 场景 | 文件位置 | EntityId | HitPosition | TextPosition |
|---|---|---|---|---|---|
| 1 | 单机格挡 | `Sys3CEntry.IDamageable.TakeDamage` 的 `_cc.IsDefending && wasBlocked` 分支 | `gameObject.GetInstanceID()` | `resolved.HasContact ? resolved.HitPoint : transform.position + Vector3.up * 1.3f + transform.forward * 0.4f`（优先真实接触点） | `transform.position + Vector3.up * 1.5f` |
| 2 | 联机本地格挡 | `Sys3CEntry.ApplyServerDamage` 的 `isBlocked` 分支 | `gameObject.GetInstanceID()` | `transform.position + Vector3.up * 1.3f + transform.forward * 0.4f`（协议无方向，正面近似） | `transform.position + Vector3.up * 1.5f` |
| 3 | 联机远程格挡 | `CombatNetworkBridge.ApplyDamageResult` 远程分支，`hit.IsBlocked == true && view.Kind != (byte)EntityKind.Npc` | `view.GameObject.GetInstanceID()` | `view.Position + Vector3.up * 1.2f` | `view.Position + Vector3.up * 1.5f` |

发射时序：本地两条分支都在 `EnterDefendHit` 与扣精力都完成之后、死亡判定之前发射，保证事件订阅者看到的动画状态与精力状态一致；远程分支没有动画/精力动作，发事件后直接 `continue`。

**发射点 1 的兜底**：`resolved.HasContact == false` 时（无碰撞箱路径 / 无接触上下文），`HitContact.None.HitPoint` 是零向量；必须回退到"胸前前方"点，否则火花会出现在世界原点。

**发射点 3 的实现要点**：`if (hit.IsBlocked && view.Kind != (byte)EntityKind.Npc)` 必须放在 `var view = _registry.GetView(...); if (view == null) continue;` **之后**、`view.Kind == Npc` 分支**之前**，命中后 `continue`——被格挡的远程玩家/bot 命中不再走 `PlayerTakeDamageEvent`。服务端只对 Player 判格挡（证据 10），显式加 `Kind != Npc` 守卫是为了保证"即使未来服务端给 Npc 置 IsBlocked，怪物伤害数字也不会被吞掉"。

**发射点 2 的边界**：`ApplyServerDamage` 的 `hitDirection` 恒为零，无法算真实接触点；但格挡判定要求攻击者在正面 ±60° 锥内，因此"角色正前方 0.4m"在视觉上成立。这是已知近似，不是 bug。

### 4.3 飘字表现

**`DisplayEventBridge`**（`Nameplate/DisplayEventBridge.cs`）：

- 构造函数签名改为 `(FloatTextRenderer floatText, FloatTextSettings damageSettings, FloatTextSettings critDamageSettings, FloatTextSettings playerDamageSettings, FloatTextSettings blockSettings)`，最后一个保存为 `_blockSettings`
- `Enable()` / `Disable()` 增删 `EventBus.Subscribe<BlockSucceededEvent>(OnBlockSucceeded)`

新增字段与常量：

```csharp
private const float BlockMergeWindow = 0.2f;
private readonly Dictionary<int, float> _lastBlockTime = new Dictionary<int, float>();
private bool _warnedMissingBlockSettings;
```

处理器逻辑：

```csharp
private void OnBlockSucceeded(BlockSucceededEvent e)
{
    if (!FloatTextRenderer.EnableVFX) return;
    if (_blockSettings == null)
    {
        if (!_warnedMissingBlockSettings)
        {
            _warnedMissingBlockSettings = true;
            Debug.LogWarning("[DisplayEventBridge] _blockSettings 未接线——格挡飘字不会显示");
        }
        return;
    }
    float now = UnityEngine.Time.time;
    if (_lastBlockTime.TryGetValue(e.EntityId, out float last)
        && now - last < BlockMergeWindow) return;
    _lastBlockTime[e.EntityId] = now;
    _floatText.ShowFloatingText(e.TextPosition, _blockSettings, "格挡");
}
```

- `BlockMergeWindow = 0.2f`，与 `FloatTextRenderer` 的伤害合并窗口一致
- `_lastBlockTime` 在 `Disable()` 中 `Clear()`
- 飘字走 `ShowFloatingText`（文本重载），不走 `ShowDamageText`（后者会拼 `-{value}`）
- 文件需补 `using UnityEngine;`（`Debug`/`Time`）与 `using System.Collections.Generic;`（`Dictionary`）

**`EntityDisplayManager`**（`Nameplate/EntityDisplayManager.cs`）：

- 新增序列化字段 `[SerializeField] private FloatTextSettings _blockSettings;`
- `Awake()` 中传给 `DisplayEventBridge` 构造函数

**场景接线**：DemoDay 场景的 `EntityDisplayManager` 组件新增一行：

```yaml
_blockSettings: {fileID: 11400000, guid: 2036363bbffefee4a89e8b873a40caed, type: 2}
```

全项目只有 DemoDay 挂了 `EntityDisplayManager`，只需改这一处。若 Unity 编辑器正在运行，优先通过 Inspector 或 Unity MCP 赋值，不要手改场景 YAML（会被编辑器回写覆盖）。

### 4.4 火花表现

**`HitFeedbackSystem`**（`VFX/HitFeedbackSystem.cs`）：

- 新增序列化字段：

```csharp
[SerializeField] private Color _blockSparkColor = new Color(1f, 0.84f, 0f);
[SerializeField] private float _blockSparkScale = 0.8f;
```

- `OnEnable()` / `OnDisable()` 增删 `EventBus.Subscribe<BlockSucceededEvent>(OnBlockSucceeded)`
- 处理器复用现有 8 实例池：

```csharp
private void OnBlockSucceeded(BlockSucceededEvent e)
{
    if (!EnableVFX) return;
    if (_sparks == null || _sparks.Length == 0) return;
    _sparks[_cursor % _sparks.Length].Play(e.HitPosition, _blockSparkColor, _blockSparkScale);
    _cursor++;
}
```

- 不新建粒子池；格挡火花与命中火花共享 round-robin 池
- `HitFeedbackSystem` 只挂在 DemoDay 场景（证据 18）；其他场景不会播火花

### 4.5 保持不变

- `HitFSM.EnterDefendHit`、`DefendHit` 动画、`DefendHitDuration=0.4s`
- `ConsumeBlockStamina`、精力耗尽强制收盾
- 格挡判定（`DefendModifier` / `CombatRules.IsBlocked`）、减伤倍率
- 本地格挡不闪屏、不泛红（本地格挡分支仍然不发 `PlayerTakeDamageEvent`）

---

## 5. 边界与已知限制

1. **旁观者不可见**：服务端只把 `DamageResult` 发给施法者和受击者；第三人看不到别人的格挡反馈。要让旁观者可见需要扩大服务端广播范围（增加带宽），不在本次范围。
2. **联机本地火花位置是近似**：协议没有命中方向字段，只能用"角色正前方 0.4m"。单机分支使用真实接触点。
3. **远程格挡没有动画**：远程玩家/bot 只有飘字 + 火花，不播 `DefendHit`（无同步通道，既有 backlog）。
4. **飘字去重只作用于文本**：同一实体 0.2s 内多段命中只显示一次「格挡」；火花仍逐次播放。
5. **致死格挡**：先发反馈，再走 `EvaluateLocalDeath`（与现有"先扣精再死亡"顺序一致）。
6. **格挡后精力耗尽收盾**：反馈仍正常播放，随后 `ForceExitDefendByExhaustion` 收盾。
7. 音频、屏幕闪光、震屏、顿帧、盾牌高亮均不改变。
8. **bot 当前不会举盾**：服务端 `BotRunner` 从不设置 `IsDefending`（证据 16），所以"远程 bot 格挡"的代码路径已具备、但当前无法实测；等 bot AI 支持防御后自动生效。
9. **表现只在 DemoDay 生效**：`EntityDisplayManager`（飘字）与 `HitFeedbackSystem`（火花）都只挂在 DemoDay 场景。
10. **高优先级受击状态会吞掉 `DefendHit` 动画，但不吞反馈**：`HitFSM.EnterDefendHit` 在 Dizzy/Knockback/Launched/Down/GetUp/Death 状态会拒绝切换，此时飘字与火花仍会播放（格挡已经被判定成功）。

---

## 6. 测试与验收

### 6.1 自动化测试

**`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs`**（沿用文件内 PascalCase 命名）：

| 用例 | Arrange | Act | Assert |
|---|---|---|---|
| `OnDamageResult_RemoteBlockedTarget_Emits_BlockSucceededEvent_Not_PlayerTakeDamageEvent` | 远程视图（Position 位）；订阅两个事件 | `HitEntry { TargetPlayerId=2, Damage=3, IsBlocked=true }` | 收到 `BlockSucceededEvent`，`EntityId == view.GameObject.GetInstanceID()`；`HitPosition ≈ view.Position + up*1.2`；**没有** `PlayerTakeDamageEvent` |
| `OnDamageResult_RemoteUnblockedTarget_Does_Not_Emit_BlockSucceededEvent` | 同上 | `HitEntry { IsBlocked=false }` | 仍发 `PlayerTakeDamageEvent`，不发 `BlockSucceededEvent`（回归护栏） |
| `OnDamageResult_LocalBlockedTarget_Does_Not_Emit_BlockSucceededEvent` | 本地 id=7；带 sink | `HitEntry { TargetPlayerId=7, IsBlocked=true }` | sink 必须被调用一次；桥不发 `BlockSucceededEvent`（避免与 `ApplyServerDamage` 重复反馈） |

**`Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs`**（沿用文件内 snake_case 命名）：

| 用例 | 断言 |
|---|---|
| `test_block_feedback_online_block_emits_block_succeeded_event` | `ApplyServerDamage(..., isBlocked: true)` 后收到一次事件；`EntityId == _entry.gameObject.GetInstanceID()`；`TextPosition ≈ transform.position + up*1.5` |
| `test_block_feedback_online_non_blocked_does_not_emit_event` | `isBlocked: false` 时不发事件 |
| `test_block_feedback_offline_block_emits_block_succeeded_event` | `EnterDefend()` 后 `((IDamageable)_entry).TakeDamage(DamageBlock.CreateDefault(50f, 0f), -_entry.transform.forward)`；收到一次事件，`EntityId` 正确，`TextPosition ≈ transform.position + up*1.5`，`HitPosition` 非零 |
| `test_block_feedback_offline_non_blocked_does_not_emit_event` | 不进入防御时同一伤害不发事件 |

所有新测试必须在 `try/finally` 中 `EventBus.Unsubscribe`，避免 EventBus 静态订阅跨用例泄漏。

### 6.2 手动验收

1. **单机**（选人面板选 SinglePlayer）：DemoDay 举盾正面被 DogKnight 攻击 → 头顶金色「格挡」+ 盾面金色火花；不出现伤害数字；`DefendHit` 动画仍正常播放。
2. **单机背面/未举盾**：灰色伤害数字（`FloatText_PlayerDamage`）+ 原有火花，行为不变。
3. **联机双方**（Online 模式 + 服务器）：A 攻击举盾面朝 A 的 B → A 与 B 两端都看到「格挡」+ 火花；A 不再看到 B 的伤害数字。
4. **多段命中**：一次"挥击 + 剑气"两段格挡 → 只出现一次「格挡」，火花可多次。
5. **远程玩家**：A 攻击举盾的远程玩家 B → A 看到 B 头上「格挡」+ 火花。（bot 当前不会举盾，见 §5-8，无法实测）
6. **回归**：普通命中、暴击、怪物受击飘字颜色/数字不变。
7. **场景接线**：若 `_blockSettings` 未接，Console 出现一次警告而不是静默无飘字。

### 6.3 通过标准

- 6.1 全部自动化用例通过，既有测试全绿
- 6.2 手动项全部通过
- Unity Console 无新增错误/异常
- 验证命令：`bash tools/unity-tests.sh EditMode`、`bash tools/unity-tests.sh PlayMode BlockStaminaTests`

---

## 7. 实施顺序

1. 新增 `BlockSucceededEvent` 结构 → 编译通过
2. 先写桥接层与 PlayMode 测试，再实现三个发射点 → 测试变绿
3. `DisplayEventBridge` + `EntityDisplayManager` + 场景接线 → 手动验收飘字
4. `HitFeedbackSystem` 订阅 → 手动验收火花
5. 全量 EditMode / PlayMode 回归

---

## 8. 明确不做（Backlog）

- 远程玩家/bot 的格挡动画同步
- 旁观者可见（服务端广播范围扩展）
- 格挡音效
- 盾牌材质高亮 / 屏幕边缘闪光 / 镜头震动 / 顿帧
- 精力环在格挡时的额外强调
