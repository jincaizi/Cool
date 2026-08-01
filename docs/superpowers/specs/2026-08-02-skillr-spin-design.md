# SkillR 旋转技能（Spin Skill）设计

**Date:** 2026-08-02
**Status:** Ready for implementation

## 1. 需求与目标

技能R（挥剑转圈）从"蓄力释放"改为"持续旋转"技能。动画已有2个clip：`SkillR_start`（起手）、`SkillR_loop`（循环）。

| # | 需求 | 配置字段 |
|---|------|---------|
| 1 | 瞬发：按一次R即发动，最低持续1秒 | `_minDuration` |
| 2 | 最大持续时长，到点自动结束 | `_maxDuration` |
| 3 | 在 `[min, max)` 窗口内再按R取消；`< min` 时按R无效 | `IsInCancelWindow` |
| 4 | 持续期间无法施放其他技能，但可以移动（减速，可配置） | `_moveSpeedMultiplier` + `CanChainSkill => false` |
| 5 | 持续期间多次伤害；先后进入范围的目标独立结算 | `_tickInterval` + `_maxHitsPerTarget` + `SpinHitTracker` |

## 2. 当前设计的问题（审查结论）

现状：技能R被建模为 `ChargedSkillData`（蓄力），配置在 `Assets/PreRes/SkillsCfg/Charged_SkillR.asset`（skillId=20002，冷却15s，伤害160，AOE半径2.5，击退4）。

1. **类型语义错配**：蓄力（按住蓄力/松开发射）≠ 持续旋转（按下开始/再按取消）。`ChargedSkillData` 的 `HoldToCharge/ReleaseToFire/MinChargeTime/MaxChargeTime` 全部是错误语义。
2. **无持续时长/取消概念**：`SkillCoordinator.HandleInput` 中 R 激活期间再按 R → `CanChainSkill` false → 输入进缓冲（0.5s TTL），不是取消。
3. **命中帧硬编码**：`ShapeBlock._hitboxTimings` 是手工数组（12个时间点到1.8s），持续5秒时1.8s后无判定。`GetExecutionDuration()` 从空的 `_releaseClip` 取时长回退0.5s——状态机不认识 `SkillR_loop`。
4. **伤害机制 hack**：工作区未提交改动 `_hitThisSwing`（每目标每cast最多一次伤害，违背"多次伤害"）+ `_consecutiveHits`（每3次检测结算一次，依赖帧率）。需求5"先后进入范围的目标"无模型。
5. **规则散落**：移动/转向/技能链接散在 `CanMove/CanRotate/CanChainSkill` 三个switch；`Channeling` 状态下 `CanChainSkill` 允许 InstantSkillData 打断（既有漏洞，本次不触碰）。
6. **空壳FSM状态**：`SkillRState` Enter/Exit/Update 全空，动画由 trigger+StateMachineBehaviour 双轨驱动。

## 3. 设计决定：新建 `SpinSkillData` 专用类型（方案A）

仿照 `InstantSkillData`/`ChanneledSkillData` 的既有模式新增技能类型，字段与需求一一对应，不动现有4种技能类型。

## 4. 数据层：`SpinSkillData`

新建 `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SpinSkillData.cs`：

```csharp
[CreateAssetMenu(fileName = "SpinSkill", menuName = "Game/Skills/Spin Skill")]
public class SpinSkillData : SkillData
{
    [Header("=== Spin Duration ===")]
    [SerializeField] private float _minDuration = 1f;   // 需求1：最低持续时长
    [SerializeField] private float _maxDuration = 5f;   // 需求2：最大持续时长

    [Header("=== Damage Ticks ===")]
    [SerializeField] private float _tickInterval = 0.2f;         // 需求5：伤害结算间隔
    [SerializeField] private int _maxHitsPerTarget = 5;          // 需求5：单目标上限（<=0 = 无上限）

    [Header("=== Movement ===")]
    [SerializeField] private float _moveSpeedMultiplier = 0.5f;  // 需求4：旋转时移动速度倍率

    // Shape / Effect / Presentation / Damage —— 复用现有 block（继承 SkillData）
    // _castClip = SkillR_start（决定 tick 起始相位）, _releaseClip = SkillR_loop

    public bool IsInCancelWindow(float elapsed)
        => elapsed >= _minDuration && elapsed < _maxDuration;   // 纯函数，可单测

    private void OnValidate()
    {
        _skillType = Definition.SkillType.Spin;
        _tickInterval = Mathf.Max(0.01f, _tickInterval);
        _maxDuration = Mathf.Max(_minDuration, _maxDuration);
        _moveSpeedMultiplier = Mathf.Clamp01(_moveSpeedMultiplier);
        // 若 _castClip != null 且 _maxDuration <= _castClip.length → Debug.LogWarning("全程无tick")
    }
}
```

- `SkillState.cs` 加 `SkillSubState.Spinning`；`SkillType.cs` 加 `SkillType.Spin`
- 动画clip不新增字段：复用 `SkillData._castClip`/`_releaseClip`（Animator 已静态引用，数据层引用仅用于 tick 相位计算）

## 5. 状态机：`SkillStateMachine` Spinning 分支

```
按下R → TryStart() → TransitionTo(Spinning)      // 瞬发：无 Casting 阶段
UpdateSpinning():
    tickTime[n] = startClip.length + (n + 1) * _tickInterval   // n = 0,1,2,…
    // 第一个 tick 在 start 动画结束后的一个 tickInterval；start 动画期间不结算
    到点 → OnHitboxFrame(n) + OnHitConfirm
    elapsed >= _maxDuration → Complete()                  // 需求2：自动结束
Cancel()：
    仅 Spinning 状态 + IsInCancelWindow(elapsed) 成立
    → TransitionTo(Completed)（发 OnSkillCompleted，不走中断语义；冷却已启动不受影响）
```

关键规则：
- 状态从按下起算（"最低持续1秒从按下起算"）；第一个 tick 在 `startClip.length + tickInterval`
- `Cancel()` 幂等：非 Spinning 状态直接返回，防 Complete/Cancel 同帧双触发重复发事件
- 取消走 `OnSkillCompleted`（正常完成），**不走** `OnSkillInterrupted`（被打断的语义保留给 Stun/RollDodge/Parry）
- `GetCurrentTime()` 改为 `protected virtual`（测试缝，子类注入假时间）

**帧内执行顺序安全**：`Sys3CEntry.HandleInput`（输入）先于 `_skillCoordinator.Update`（状态机）→ 按R时 `elapsed >= max` 则取消检查拒绝（`< max` 不成立）→ 同帧状态机自动 Complete，无竞态。

## 6. 伤害执行：`SkillExecutor` tick 重写

- **删除** `_consecutiveHits`（每3次检测）与未提交的 `_hitThisSwing`（每cast一次）
- **新增** `SpinHitTracker`（纯逻辑小类，可单测）：
  - `Dictionary<int,int>` 每目标命中计数，贯穿整个施放（离开再回来继续累计）
  - `bool TryRecordHit(int instanceId)`：未达上限返回 true 并计数+1；`_maxHitsPerTarget <= 0` = 不设上限
- `OnHitboxTriggered(tickIndex)` 改为：
  1. `DetectTargets()` 复用现有 AOE 检测（`AOE_Circle`，无锁定目标时中心在自身）
  2. 每个目标：`TryRecordHit` 通过 → `ApplyDamage`（**全额**，无蓄力倍率）+ `ApplyEffects` + `OnTargetHit`；达上限跳过
  3. 清理：`OnSkillComplete`/`OnSkillInterrupt` 清空计数
- `GetShape()` / `GetEffect()` / `GetPresentation()` 各加一行 `SpinSkillData` 分支
- `ApplyDamage` 中蓄力倍率分支（`Charging/Execution`）对 Spinning 不生效（全额伤害）

**VFX 事件复用**（避免改6个VFX文件，事件命名是蓄力语义的历史债，另提重构）：
- 进入 Spinning → `SkillChargingStartedEvent`（WeaponVFXController/SwordGlowVFX/FrostAuraVFX 现有订阅生效）
- 每 tick → `SkillChargeTickEvent`（`Progress = elapsed / _maxDuration`，SwordGlowVFX/SlashTrailVFX）
- 结束（取消或自动完成）→ `SkillReleasedEvent`，**仅当 `_skillData is SpinSkillData` 时发射**（在 executor 的完成/中断回调中守卫，避免污染其他技能）
- 每命中 → `SkillHitTargetEvent`（`IsFullCharge = false`，SkillFreezeEffector/IceBurstVFX）

## 7. 输入与规则：`SkillCoordinator` + `Sys3CEntry`

**同键取消特例**（`HandleInput` 最前面拦截，**永不进入缓冲路径**）：

```csharp
if (_currentSkill is SpinSkillData && _currentSkill.CurrentSubState == Spinning)
{
    if (executor.CanCancel()) executor.Cancel();   // 幂等
    return;   // elapsed < min 时忽略；永不入缓冲
}
```

- `SkillExecutor` 新增 `bool CanCancel()`（转发 `IsInCancelWindow`）与 `void Cancel()`
- **缓冲竞态规则**：spin 激活期间 R 按键永不入缓冲。原因：`SkillInputBuffer` 保留0.5s且 `CleanupExecutor` 在技能结束时立即消费缓冲（`SkillCoordinator.cs:426`），若R入缓冲会在技能结束瞬间意外重施放
- `CanChainSkill` 加 `SkillSubState.Spinning => false`（需求4：不能施放其他技能；`TryActivateSkill` 优先级检查为第二道保险）
- `CanMove()` 加 `Spinning => true`；`CanRotate()` 加 `Spinning => true`（允许转向）
- `Sys3CEntry`：**删除** `IsSkill3Released() → ReleaseCharge()` 分支（蓄力语义移除）；保留 `IsSkill3Pressed()`
- 中断路径（Stun/RollDodge/Parry）保持现有 `CanBeInterrupted` 矩阵：Stun => true 等，走 `OnSkillInterrupted` → 现有 animator cleanup 复用

## 8. 移动减速与动画

**移动减速**（`Sys3CEntry.Update`）：

```csharp
var command = _inputManager.GetMoveCommand(cameraForward);
command *= _skillCoordinator.GetMoveSpeedMultiplier();   // spin 激活 = _moveSpeedMultiplier，否则 1
_cc.Update(command);
```

- `SkillCoordinator` 新增 `GetMoveSpeedMultiplier()`（从当前技能查）
- start 动画期间同样减速（状态从按下即 Spinning，行为一致）；冲刺/击退等非输入位移不受影响
- 实现时确认 `GetMoveCommand` 返回值语义（方向 vs 方向×速度），乘倍率位置以它为准

**动画**：Animator 现有链路不改——`SkillR` trigger → `SkillR_start`（播完）→ `SkillR_loop`（循环，`AttackState==0` 时退出）。取消/到期 → `CleanupSkillAnimation` 重置 `AttackState` → loop 自然退出。loop 退出过渡当前约0.8s（exitTime 0.774 + duration 0.06），若嫌取消响应拖沓调小（可选）。

## 9. 边界情况（已处理）

| # | 边界情况 | 处理 |
|---|---------|------|
| 1 | `maxDuration <= startClip.length` | 全程无tick，OnValidate 警告 |
| 2 | `minDuration < startClip.length` | 取消可能发生在start动画期间（配置者责任），OnValidate 提示 |
| 3 | 按R在 `[0, min)` 内（start动画期间） | 忽略且不缓冲（防缓冲R在技能结束后重施放） |
| 4 | 按R时 `elapsed >= max` 同帧 | 输入先于状态机 → 取消拒绝，同帧自动Complete，无竞态 |
| 5 | `Cancel()` 重复调用 | 幂等：仅 Spinning 状态可取消 |
| 6 | `maxHitsPerTarget <= 0` | 无上限（逃生口），文档写明 |
| 7 | 击退×多次命中 | 每tick击退4把目标推出2.5m半径，maxHits=5实战难打满——tuning问题，非bug；可调knockback=0观察纯tick命中 |
| 8 | 死亡时旋转不停 | `StateCoordinator.HandleDeath` 不中断技能执行器（既有缺口）→ 死亡入口补 `InterruptCurrentSkill`，实现计划列为检查项 |
| 9 | spin期间按Q | 缓冲0.5s TTL → **仅当spin在按键后0.5s内结束时**才衔接施放，否则过期丢弃：既有输入缓冲行为，保留 |
| 10 | 取消后冷却 | 冷却在 `TryActivateSkill` 启动（早于 TryStart），取消不重置 → 无法无限转圈 |
| 11 | spin期间再按R连点 | 第一次取消，后续按R：状态非Spinning → 走正常路径 → 冷却中 → 入缓冲0.5s过期 → 无意外重施放 |

## 10. 资产迁移

- 新建 `Assets/PreRes/SkillsCfg/Spin_SkillR.asset`（skillId=20002）
  - 迁移现有数值：`_damage` baseDamage=160、冷却15s、Shape AOE半径2.5/内圈0.5+60°、击退4
  - 新字段：`_minDuration=1`、`_maxDuration=5`、`_tickInterval=0.2`、`_maxHitsPerTarget=5`、`_moveSpeedMultiplier=0.5`
  - `_castClip`=SkillR_start、`_releaseClip`=SkillR_loop（Guid 从 Character3C.controller 中对应 Motion 引用取得）
  - 删除 `Charged_SkillR.asset` + meta
- `Sys3CEntry._characterSkills` 的 Inspector 引用需重新拖入新资产（GUID 变化）

## 11. 测试计划（`GameSys.EditorTests`，沿用现有 reflection 风格）

| 测试 | 验证点 |
|------|--------|
| `SpinSkillData.OnValidate` | SkillType=Spin、tickInterval 钳制、max≥min、moveSpeed∈(0,1] |
| `IsInCancelWindow` | 边界：elapsed=min 允许、elapsed=max-ε 允许、elapsed=max 拒绝 |
| `SpinHitTracker` | 计数达标后拒绝；`maxHits<=0` 无上限；目标ID独立计数 |
| `SkillStateMachine`（注入假时间） | 进入Spinning；tick按 `startClip.length + (n+1)×tick` 触发；elapsed≥max 自动Complete；`Cancel()` 窗口外拒绝、窗口内完成且不重复发事件、幂等 |
| `SkillExecutor`（假时间+纯逻辑路径） | 完成/中断后无残余计数 |

物理检测（`OverlapSphere`）不进 EditMode 测试，留 PlayMode/手动验证。

## 12. 范围外（明确不做）

- ❌ 不改 VFX 事件命名（蓄力语义历史债，另提重构）
- ❌ 不改 `ChanneledSkillData` 的 `CanChainSkill` 漏洞（方案A不触碰）
- ❌ 不做伤害分摊/衰减（已确认：全额+上限）
- ❌ 不修玩家死亡流程本身（只保证死亡入口中断 spin）

## 13. 验收标准

1. 按一次R：立即进入旋转（SkillR_start→SkillR_loop），持续 ≥ 1s
2. 旋转期间再按R：`elapsed >= 1s` 时取消，立即退出loop动画回Idle；`elapsed < 1s` 时无效
3. 旋转 ≥ 5s 自动结束
4. 旋转期间：Q/普攻等无法施放；移动正常但速度×0.5；转向正常
5. 每0.2s对范围内所有目标结算全额伤害，单目标最多5次；中途进入范围的目标从0开始计数
6. 转圈结束（取消/到期/被打断）后：冷却15s正常；VFX（剑光/拖尾/冰冻/冰爆）随技能启停
