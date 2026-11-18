# 状态结算 + DoT + 元素表现实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在 Plan 03 的权威位移/控制基础上，落地服务端权威的冰/火/雷/风状态运行时（含同次施法只附加一次、同 tick 顺序、灼烧末跳、`DamageKind` 分流），并让客户端按 `CombatStateUpdate.Statuses` 显示状态表现、按元素给剑气和状态染色。

**架构：** `SyncCore` 新增纯 C# `StatusRuntime`（定义校验、同组刷新、DoT 时序、控制派生、PvP 易伤/免疫规则）；服务端 `PlayerState` 持有运行时，`CombatResolver` 在首个有效命中时附加元素，`StatusTickService` 在 **GameLoop 预 tick**（网络包之前）推进到期/灼烧；投影把 `Statuses` 填入 `CombatProjection`，客户端 `CombatNetworkBridge` 按 `DamageKind=Dot` 走纯血量/飘字通道，`RemoteEntityRegistry`/`Sys3CEntry` 把权威状态交给 VFX。离线单机共用同一套规则留到 Plan 05（见范围说明）。

**技术栈：** Unity 2022.3.25f1 / C# 9、.NET 8 服务端、protobuf-net 3.2.56、NUnit/xUnit。

---

## 范围说明（必须先读）

**本计划覆盖：**
- 服务端权威的 PvE（服务器怪物）与 PvP（同战斗场景玩家）状态运行时。
- 冰（减速）、火（灼烧 DoT）、雷（麻痹/动作锁）、风（易伤）四种状态。
- 同次施法首个有效命中附加一次；同 tick 顺序；灼烧末跳；`DamageKind` 分流。
- 客户端权威状态表现（染色/循环特效）与剑气元素染色。

**明确不在 Plan 04：**
- **离线单机**的共享状态运行时（本地 `StatusEffectManager` 的冰冻/灼烧仍独立）；离线四元素与现有本地状态去重留给 Plan 05，README/路线图必须如实写。
- Addressables 美术资源接入与最终特效品质；本计划先做可测试的程序化染色/简化表现，资源键留空。
- 三客户端带宽/延迟验收（Plan 05）。

## 全局约束

- **状态变更的唯一权威来源是服务端**；客户端只按 `CombatStateUpdate`/`DamageResult` 更新表现，不自行判定命中或 DoT。
- 同次施法对每个目标只在**首个有效命中**附加一次；有效命中 = 正伤害、未格挡、目标仍存活。挥砍与剑气共用同一记录。
- 同组状态最多一条；刷新结束时间；灼烧仇恨来源保留首次施加者；灼烧下一跳取原下一跳与新间隔的较早者。
- 状态区间 `[ApplyTick, EndTick)`；灼烧允许在 `EndTick` 结算排定末跳后移除；风在 `EndTick` 先失效，不放大同 tick 的灼烧末跳。
- 同 tick 顺序：解除到期修饰 → 结算灼烧 → 处理输入 → 怪物动作/命中段 → 复活与同步。GameLoop 必须在网络包消费前执行状态 tick。
- PvP 风倍率用 `Pvp.WindVulnerabilityMultiplier`（1.28），PvE 用状态表 `IncomingDamageMultiplier`（2.0）；PvP 风不放大灼烧。控制结束 0.5s 免疫、同组易伤 4s 不刷新。
- 事件：`HitEntry.EventSeq` 按 `(LifeId, EntityId)` 单调，`EventId` 全局唯一，`CombatVersion` 与状态变更同版本；客户端允许序号空洞、按 `EventId` 去重。
- 协议只加不删；`Statuses` 字段置位且列表为空表示全部清除，省略字段保留已有状态（Plan 02 已实现）。
- 不引入 `List<T>` 协议成员；不新增第二条状态通道；状态 tick 在主循环线程执行。

## 文件结构

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/StatusRuntime.cs` | 共享状态运行时（纯逻辑，无 UnityEngine） |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefaults.cs` | 增加 `FindStatus(int effectId)` 只读查询 |
| `Server/GameLoop.cs` | 增加 `onPreTick`（状态 tick 先于网络包） |
| `Server/Player/ServerStatusManager.cs` | 每实体状态包装：应用/刷新/清理/同步控制/版本 |
| `Server/Player/PlayerState.cs` | 持有 `ServerStatusManager`、`NextEventSeq`、状态清理钩子 |
| `Server/Status/StatusTickService.cs` | 到期修饰、灼烧跳数、DamageResult、死亡/离场清理 |
| `Server/Handler/CastHandler.cs` | 首个有效命中附加元素、风倍率、`HitEntry` 事件 ID；DoT 广播入口 |
| `Server/Monster/MonsterModule.cs` / `MonsterBrain.cs` / `MonsterBtActions.cs` / `MonsterActionRunner.cs` | 怪物读 `SpeedScale/ActionLock`、麻痹打断霸体规则、Evade 到家清状态 |
| `Server/Aoi/CombatProjectionBuilder.cs` | 填 `Statuses` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | `DamageKind=Dot` 分流、状态表现事件路由 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 本人 311 状态消费 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | 远程状态变更事件 |
| `Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs` | 权威状态染色/循环特效 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/*` | 剑气元素染色 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 本人状态表现、DoT 血量/飘字 |
| `Server/Config/elemental_catalog.json` | `ViewKey` 可暂空；状态参数已存在 |

---

### 任务 1：共享状态运行时

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/StatusRuntime.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefaults.cs`
- 测试：`Server/Server.Tests/Elemental/StatusRuntimeTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncCore/StatusRuntimeTests.cs`（新建）

- [ ] **步骤 1：写失败测试**

服务端测试覆盖：应用/刷新/同组唯一、灼烧跳数/末跳、风到期先于灼烧、控制派生、PvP 免疫/不刷新、清除。关键用例：

```csharp
[Fact]
public void Apply_SameGroup_Refreshes_EndTick_And_Keeps_Burn_Source()
{
    var catalog = ElementalCatalogDefaults.Create();
    var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);

    Assert.Equal(StatusApplyResult.Applied,
        runtime.Apply(burn, sourceId: 1, skillId: 10002, castId: 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true));

    // tick 15 先结算原排定跳（状态 tick 先于输入），再被新命中刷新。
    var dueAt15 = new List<ActiveStatus>();
    runtime.CollectDueDots(15, dueAt15);
    runtime.AdvanceDot(dueAt15[0], 15);

    Assert.Equal(StatusApplyResult.Refreshed,
        runtime.Apply(burn, sourceId: 2, skillId: 10002, castId: 8, tick: 15, targetIsPlayer: false, sourceIsPlayer: true));

    var active = runtime.Active.Single();
    Assert.Equal(1L, active.SourceId);          // 首次施加者保留
    Assert.Equal(60u, active.EndTick);          // 15 + 45
    Assert.Equal(30u, active.NextTick);         // 原下一跳 30 与新间隔 30 取较早
}

[Fact]
public void Burn_Ticks_On_Interval_And_Removes_After_EndTick_Tick()
{
    var catalog = ElementalCatalogDefaults.Create();
    var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);
    runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);

    var due = new List<ActiveStatus>();
    runtime.CollectDueDots(15, due);
    Assert.Single(due);
    runtime.AdvanceDot(due[0], 15);
    Assert.Single(runtime.Active);

    due.Clear();
    runtime.CollectDueDots(45, due);
    Assert.Single(due);                          // EndTick 仍结算
    runtime.AdvanceDot(due[0], 45);
    Assert.Empty(runtime.Active);
}

[Fact]
public void Wind_Expires_Before_Burn_On_Same_EndTick()
{
    var catalog = ElementalCatalogDefaults.Create();
    var wind = catalog.Statuses.Find(s => s.EffectId == 401)!;
    var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);
    runtime.Apply(wind, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);
    runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);

    runtime.RemoveExpiredModifiers(23);           // wind 1.5s = 23 ticks
    Assert.DoesNotContain(runtime.Active, s => s.EffectId == 401);
    Assert.Equal(1f, runtime.IncomingDamageMultiplier(pvp: false, pvpMultiplier: 1.28f), 4);
}

[Fact]
public void Control_Derives_SpeedScale_And_ActionLock()
{
    var catalog = ElementalCatalogDefaults.Create();
    var ice = catalog.Statuses.Find(s => s.EffectId == 101)!;
    var thunder = catalog.Statuses.Find(s => s.EffectId == 301)!;
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);
    runtime.Apply(ice, 1, 10002, 7, 0, false, true);
    runtime.Apply(thunder, 1, 10002, 7, 0, false, true);

    runtime.DeriveControl(out float speed, out bool locked);
    Assert.Equal(0.7f, speed, 4);
    Assert.True(locked);
}

[Fact]
public void Pvp_Control_Immunity_And_Vulnerability_NoRefresh()
{
    var catalog = ElementalCatalogDefaults.Create();
    var thunder = catalog.Statuses.Find(s => s.EffectId == 301)!;
    var wind = catalog.Statuses.Find(s => s.EffectId == 401)!;
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);

    runtime.Apply(thunder, 1, 10002, 7, 0, targetIsPlayer: true, sourceIsPlayer: true);
    runtime.Remove(301, tick: 12);                // 0.8s = 12 ticks
    Assert.Equal(StatusApplyResult.Ignored,
        runtime.Apply(thunder, 2, 10002, 8, tick: 15, targetIsPlayer: true, sourceIsPlayer: true)); // 0.5s 免疫

    runtime.Apply(wind, 1, 10002, 7, 0, targetIsPlayer: true, sourceIsPlayer: true);
    runtime.RemoveExpiredModifiers(23);           // wind 1.5s 已到期移除
    Assert.Equal(StatusApplyResult.Ignored,
        runtime.Apply(wind, 2, 10002, 8, tick: 30, targetIsPlayer: true, sourceIsPlayer: true)); // 4s 内仍不刷新
    Assert.Equal(StatusApplyResult.Applied,
        runtime.Apply(wind, 2, 10002, 8, tick: 61, targetIsPlayer: true, sourceIsPlayer: true)); // 4s 后可再施加
}

[Fact]
public void Burn_OffPhase_Refresh_Is_Removed_At_EndTick_Without_Extra_Tick()
{
    var catalog = ElementalCatalogDefaults.Create();
    var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);
    runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);
    Advance(runtime, 15);
    Advance(runtime, 30);
    runtime.Apply(burn, 2, 10002, 8, tick: 32, targetIsPlayer: false, sourceIsPlayer: true); // EndTick 77

    Advance(runtime, 45);
    Advance(runtime, 60);
    Advance(runtime, 75);
    Advance(runtime, 77);                          // 无排定跳：直接移除，不额外补一跳
    Assert.Empty(runtime.Active);
}

[Fact]
public void Burn_NonDividing_Interval_Removed_At_EndTick_Without_Extra_Tick()
{
    var catalog = ElementalCatalogDefaults.Create();
    var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
    burn = new StatusEffectDefinition
    {
        EffectId = burn.EffectId, EffectKind = burn.EffectKind, StackGroupId = burn.StackGroupId,
        DurationSeconds = 3f, IntervalSeconds = 1.1f, DamagePerTick = burn.DamagePerTick
    };
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);
    runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);

    Advance(runtime, 17);
    Advance(runtime, 34);
    runtime.RemoveExpiredDots(45);
    Assert.Empty(runtime.Active);
}

[Fact]
public void Pve_Paralyze_Does_Not_Grant_Control_Immunity()
{
    var catalog = ElementalCatalogDefaults.Create();
    var thunder = catalog.Statuses.Find(s => s.EffectId == 301)!;
    var runtime = new StatusRuntime();
    runtime.Configure(catalog.Pvp);
    runtime.Apply(thunder, 1, 10002, 7, 0, targetIsPlayer: false, sourceIsPlayer: true);
    runtime.Remove(301, tick: 12);

    Assert.Equal(StatusApplyResult.Applied,
        runtime.Apply(thunder, 2, 10002, 8, tick: 15, targetIsPlayer: false, sourceIsPlayer: true));
}

private static void Advance(StatusRuntime runtime, uint tick)
{
    var due = new List<ActiveStatus>();
    runtime.CollectDueDots(tick, due);
    foreach (var status in due) runtime.AdvanceDot(status, tick);
    runtime.RemoveExpiredDots(tick);
}
```

Unity 侧至少覆盖应用/刷新、灼烧末跳、控制派生（NUnit）。

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~StatusRuntimeTests"
```
预期：`StatusRuntime` 不存在，编译失败。

- [ ] **步骤 3：实现 `StatusRuntime`**

```csharp
using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    public enum StatusEffectKind { Slow = 1, Burn = 2, Paralyze = 3, Vulnerable = 4 }
    public enum StatusApplyResult { Ignored, Applied, Refreshed }

    public sealed class ActiveStatus
    {
        public StatusEffectDefinition Definition = null!;
        public long SourceId;
        public int SourceSkillId;
        public ulong SourceCastId;
        public uint AppliedTick;
        public uint EndTick;
        public uint NextTick;
        public bool PendingRemoval;
        public bool IsPvpControl;

        public int EffectId => Definition.EffectId;
        public int GroupId => Definition.StackGroupId;
        public StatusEffectKind Kind => (StatusEffectKind)Definition.EffectKind;
        public bool IsDot => Kind == StatusEffectKind.Burn;
    }

    /// <summary>状态运行时：纯逻辑，服务端与客户端离线共用；tick 由调用方按 15Hz 提供。</summary>
    public sealed class StatusRuntime
    {
        private readonly Dictionary<int, ActiveStatus> _byEffect = new();
        private readonly Dictionary<int, ActiveStatus> _byGroup = new();
        private uint _controlImmunityTicks;
        private uint _vulnerabilityNoRefreshTicks;
        private uint? _lastVulnerableAppliedTick;

        public IEnumerable<ActiveStatus> Active => _byEffect.Values;
        public int Count => _byEffect.Count;
        public uint ControlImmuneUntilTick { get; private set; }

        public void Configure(PvpCombatSettings pvp)
        {
            _controlImmunityTicks = SecondsToTicks(pvp.ControlImmunitySeconds);
            _vulnerabilityNoRefreshTicks = SecondsToTicks(pvp.SameGroupVulnerabilityNoRefreshSeconds);
        }

        public StatusApplyResult Apply(StatusEffectDefinition def, long sourceId, int skillId,
            ulong castId, uint tick, bool targetIsPlayer, bool sourceIsPlayer)
        {
            if (def == null) return StatusApplyResult.Ignored;
            var kind = (StatusEffectKind)def.EffectKind;
            uint duration = SecondsToTicks(def.DurationSeconds);
            if (duration == 0) return StatusApplyResult.Ignored;

            if (kind == StatusEffectKind.Paralyze && tick < ControlImmuneUntilTick)
                return StatusApplyResult.Ignored;
            // 4s 窗口跨状态移除保存：不能只看 _byGroup 里是否仍有活跃 wind。
            if (kind == StatusEffectKind.Vulnerable && targetIsPlayer && sourceIsPlayer
                && _lastVulnerableAppliedTick.HasValue
                && tick - _lastVulnerableAppliedTick.Value < _vulnerabilityNoRefreshTicks)
                return StatusApplyResult.Ignored;

            if (_byGroup.TryGetValue(def.StackGroupId, out var existing))
            {
                if (existing.EffectId != def.EffectId)
                {
                    Remove(existing.EffectId, tick);
                }
                else
                {
                    existing.Definition = def;
                    existing.EndTick = AddTicks(tick, duration);
                    existing.PendingRemoval = false;
                    existing.IsPvpControl = kind == StatusEffectKind.Paralyze
                        && targetIsPlayer && sourceIsPlayer;
                    if (existing.IsDot)
                    {
                        uint next = AddTicks(tick, SecondsToTicks(def.IntervalSeconds));
                        existing.NextTick = existing.NextTick == 0 || next < existing.NextTick
                            ? next : existing.NextTick; // 保留首次来源
                    }
                    else
                    {
                        existing.SourceId = sourceId;
                        existing.SourceSkillId = skillId;
                        existing.SourceCastId = castId;
                        existing.AppliedTick = tick;
                    }
                    if (kind == StatusEffectKind.Vulnerable) _lastVulnerableAppliedTick = tick;
                    return StatusApplyResult.Refreshed;
                }
            }

            var status = new ActiveStatus
            {
                Definition = def, SourceId = sourceId, SourceSkillId = skillId, SourceCastId = castId,
                AppliedTick = tick, EndTick = AddTicks(tick, duration),
                NextTick = kind == StatusEffectKind.Burn
                    ? AddTicks(tick, SecondsToTicks(def.IntervalSeconds)) : 0u,
                IsPvpControl = kind == StatusEffectKind.Paralyze && targetIsPlayer && sourceIsPlayer
            };
            _byEffect[def.EffectId] = status;
            _byGroup[def.StackGroupId] = status;
            if (kind == StatusEffectKind.Vulnerable) _lastVulnerableAppliedTick = tick;
            return StatusApplyResult.Applied;
        }

        public bool Remove(int effectId, uint tick)
        {
            if (!_byEffect.Remove(effectId, out var status)) return false;
            if (status.IsPvpControl)
                ControlImmuneUntilTick = AddTicks(tick, _controlImmunityTicks);
            if (_byGroup.TryGetValue(status.GroupId, out var group) && ReferenceEquals(group, status))
                _byGroup.Remove(status.GroupId);
            return true;
        }

        public int RemoveExpiredModifiers(uint tick)
        {
            var expired = new List<int>();
            foreach (var s in _byEffect.Values)
                if (!s.IsDot && tick >= s.EndTick) expired.Add(s.EffectId);
            foreach (var id in expired) Remove(id, tick);
            return expired.Count;
        }

        public void CollectDueDots(uint tick, List<ActiveStatus> due)
        {
            foreach (var s in _byEffect.Values)
            {
                if (!s.IsDot || s.NextTick == 0 || tick < s.NextTick) continue;
                due.Add(s);
                s.PendingRemoval = tick >= s.EndTick;
            }
        }

        /// <summary>没有排定跳落在 EndTick 时（刷新错相/间隔不整除）到期移除；必须在 CollectDueDots/AdvanceDot 之后调用。</summary>
        public int RemoveExpiredDots(uint tick)
        {
            var expired = new List<int>();
            foreach (var s in _byEffect.Values)
                if (s.IsDot && tick >= s.EndTick) expired.Add(s.EffectId);
            foreach (var id in expired) Remove(id, tick);
            return expired.Count;
        }

        public void AdvanceDot(ActiveStatus status, uint tick)
        {
            if (status.PendingRemoval || tick >= status.EndTick)
            {
                Remove(status.EffectId, tick);
                return;
            }
            status.NextTick = AddTicks(status.NextTick, SecondsToTicks(status.Definition.IntervalSeconds));
        }

        public void DeriveControl(out float speedScale, out bool actionLock)
        {
            float slow = 0f;
            actionLock = false;
            foreach (var s in _byEffect.Values)
            {
                if (s.Kind == StatusEffectKind.Slow) slow = Math.Max(slow, s.Definition.SlowRatio);
                else if (s.Kind == StatusEffectKind.Paralyze) actionLock = true;
            }
            speedScale = Math.Clamp(1f - slow, 0f, 1f);
        }

        public float IncomingDamageMultiplier(bool pvp, float pvpMultiplier)
        {
            float result = 1f;
            foreach (var s in _byEffect.Values)
                if (s.Kind == StatusEffectKind.Vulnerable)
                    result *= pvp ? pvpMultiplier : s.Definition.IncomingDamageMultiplier;
            return result;
        }

        public void ClearAll()
        {
            _byEffect.Clear();
            _byGroup.Clear();
            ControlImmuneUntilTick = 0;
            _lastVulnerableAppliedTick = null;
        }

        public static uint SecondsToTicks(float seconds)
            => seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)
                ? 0u : (uint)Math.Ceiling(seconds * CombatRules.TickRateHz);

        private static uint AddTicks(uint tick, uint delta)
            => delta > uint.MaxValue - tick ? uint.MaxValue : tick + delta;
    }
}
```

`ElementalCatalogDefaults` 追加：

```csharp
public static StatusEffectDefinition? FindStatus(int effectId)
{
    foreach (var s in Shared.Statuses)
        if (s.EffectId == effectId) return s;
    return null;
}

public static ElementDefinition? FindElement(int elementId)
{
    foreach (var e in Shared.Elements)
        if (e.ElementId == elementId) return e;
    return null;
}
```

- [ ] **步骤 4：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~StatusRuntimeTests"
```
预期：全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/StatusRuntime.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefaults.cs \
  Server/Server.Tests/Elemental/StatusRuntimeTests.cs \
  Assets/Scripts/Tests/Editor/SyncCore/StatusRuntimeTests.cs
git commit -m "feat(sync): add shared status runtime"
```

---

### 任务 2：GameLoop 预 tick + 服务端状态管理与投影

**文件：**
- 修改：`Server/GameLoop.cs`
- 创建：`Server/Player/ServerStatusManager.cs`
- 修改：`Server/Player/PlayerState.cs`
- 修改：`Server/Aoi/CombatProjectionBuilder.cs`
- 修改：`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Server/GameLoopOrderingTests.cs`（新建）
- 测试：`Server/Server.Tests/Handler/ServerStatusManagerTests.cs`（新建）
- 测试：`Server/Server.Tests/Aoi/CombatProjectionBuilderTests.cs`

- [ ] **步骤 1：写失败测试**

`GameLoopOrderingTests`：用一个假 loop 记录 `pre → packet → post` 顺序，断言同一 tick 内 pre 先于 handler、handler 先于 onTick；`TickCount` 在 onTick 后才递增。

`ServerStatusManagerTests`：应用减速/麻痹后 `PlayerState.ControlSpeedScale/ActionLock/ControlVersion` 同步；移除/到期后恢复；`CombatDirty` 置位；`CombatProjectionBuilder` 的 `Statuses` 含 EffectId/GroupId/EndTick/SourceId/SlowRatio。

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~GameLoopOrderingTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ServerStatusManagerTests"
```

- [ ] **步骤 3：实现 GameLoop 预 tick**

`GameLoop` 构造函数追加 `Action<long>? onPreTick = null` 与 `ConcurrentQueue<Action> _postPreTickQueue`；LoopAsync：

```csharp
// 先 drain 动作队列（断开/清理等世界状态变更）
while (_actionQueue.TryDequeue(out var action)) { ... }

long nextTick = Interlocked.Read(ref _tickCount) + 1;
try { _onPreTick?.Invoke(nextTick); }
catch (Exception ex) { _logger.LogError(ex, "GameLoop pre-tick error"); }

// WebSocket 等“必须在状态 tick 之后”的入站包走这个队列：
// 先于状态 tick 会复现 WS/KCP 的 1 tick 时序分裂。
while (_postPreTickQueue.TryDequeue(out var action)) { ... }

// 再 drain 网络包：handler 看到的是 nextTick（Server3C 在 preTick 里写 CurrentTick）
while (_queue.TryDequeue(out var packet)) { ... }

Interlocked.Exchange(ref _tickCount, nextTick);
try { _onTick?.Invoke(nextTick); } ...
```

`Server3C.OnSessionDataReceivedEnqueued` 改为 `_gameLoop.EnqueueAfterPreTick(() => { _ = ProcessPacket(session, data); });`；KCP 路径仍走 `GameLoop.Enqueue`。`Server3C` preTick 先 `CurrentTick = tick`，再逐实体 `Statuses?.RemoveExpired(tick)`。

**同 tick 基准统一**：preTick 已把 `CurrentTick` 写成当前 tick，所有包 handler 直接用它。删除 `CastHandler.HandleCastAsync`/`HandleCancel` 里的 `_server.CurrentTick + 1`（改为 `_server.CurrentTick`），并更新 `Server3C.CurrentTick` / `CastHandler` 的旧 F17 注释（“包处理落后一个迭代”已不成立）。补一个包路径测试：`HandleCastAsync` 的 `CastConfirm.AcceptedTick` 必须等于 `Server3C.CurrentTick`，不是 +1。

- [ ] **步骤 4：实现 `ServerStatusManager` 与 `PlayerState`**

`ServerStatusManager`：

```csharp
public sealed class ServerStatusManager
{
    private readonly PlayerState _owner;
    private readonly Action<float, bool>? _controlSink;
    public StatusRuntime Runtime { get; } = new();

    public ServerStatusManager(PlayerState owner, ElementalCatalogDefinition catalog,
        Action<float, bool>? controlSink = null)
    {
        _owner = owner;
        _controlSink = controlSink;
        Runtime.Configure(catalog.Pvp);
    }

    public StatusApplyResult Apply(StatusEffectDefinition def, long sourceId, int skillId,
        ulong castId, uint tick, ElementalCatalogDefinition catalog)
    {
        var result = Runtime.Apply(def, sourceId, skillId, castId, tick,
            _owner.Kind == (byte)EntityKind.Player, sourceIsPlayer: true);
        if (result != StatusApplyResult.Ignored)
        {
            SyncControl();
            _owner.BumpCombatVersion();
        }
        return result;
    }

    public void RemoveExpired(uint tick)
    {
        if (Runtime.RemoveExpiredModifiers(tick) > 0)
        {
            SyncControl();
            _owner.BumpCombatVersion();
        }
    }

    public void ClearAll(uint tick)
    {
        if (Runtime.Count == 0) return;
        Runtime.ClearAll();
        SyncControl();
        _owner.BumpCombatVersion();
    }

    public void SyncControl()
    {
        Runtime.DeriveControl(out float speed, out bool locked);
        if (speed == _owner.ControlSpeedScale && locked == _owner.ControlActionLock) return;
        if (_controlSink != null) _controlSink(speed, locked);
        else _owner.ApplyControl(speed, locked);
    }
}
```

`PlayerState` 追加：

```csharp
public ServerStatusManager Statuses { get; }
public uint NextEventSeq { get; private set; }
public uint AllocateEventSeq() => ++NextEventSeq;
```

构造函数 `Statuses = new ServerStatusManager(this, catalog)` 需要 catalog；`PlayerState` 构造函数当前只有 playerId。改为 `PlayerState(long playerId, ElementalCatalogDefinition? catalog = null)` 并使用默认目录；所有测试构造兼容。`EntityStore.AddPlayer` 传入默认/服务器目录？`EntityStore` 没有 catalog；`Server3C` 可在 `RegisterEntity` 后 `player.ConfigureStatuses(_elementalCatalog)`。更简单：`PlayerState.Statuses` 延迟初始化，`ConfigureStatuses(catalog)` 在登录/怪物生成时调用。Plan 04 task 2 can add:
```csharp
public ServerStatusManager? Statuses { get; private set; }
public void ConfigureStatuses(ElementalCatalogDefinition catalog,
    Action<float, bool>? controlSink = null)
    => Statuses ??= new ServerStatusManager(this, catalog, controlSink);
```

`Server3C.RegisterEntity` 传 `(speed, locked) => ApplyPlayerControl(player.PlayerId, speed, locked)`，让状态控制走 Plan 03 的唯一入口（含首次 ActionLock 取消未提交施法）；`ServerStatusManagerTests` 可用假 sink 断言。怪物在 Task 5 接线时传同一 sink。
`CombatProjectionBuilder` uses `entity.Statuses?.Runtime.Active ?? empty`. `CombatResolver` ensures `target.ConfigureStatuses(_elementalCatalog)` before apply. `Server3C.RegisterEntity` and `MonsterModule.Spawn` configure. This avoids changing all constructors. Good.

`CombatProjectionBuilder`:

```csharp
Statuses = entity.Statuses == null ? new List<StatusProjection>() :
    entity.Statuses.Runtime.Active.Select(s => new StatusProjection
    {
        EffectId = s.EffectId, GroupId = s.GroupId, EndTick = s.EndTick,
        SourceId = s.SourceId, SlowRatio = s.Definition.SlowRatio,
        DamagePerTick = s.Definition.DamagePerTick,
        IntervalTicks = StatusRuntime.SecondsToTicks(s.Definition.IntervalSeconds),
        IncomingDamageMultiplier = s.Definition.IncomingDamageMultiplier
    }).ToList(),
```

`Server3C.RegisterEntity` call `player.ConfigureStatuses(_elementalCatalog)`；`MonsterModule` spawn call too (or Server3C after spawn). `PlayerState` statuses null for bots; configure in `EnableBots` maybe.

- [ ] **步骤 5：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~GameLoopOrderingTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ServerStatusManagerTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatProjectionBuilderTests"
```
预期：PASS；全量服务端回归。

- [ ] **步骤 6：Commit**

```bash
git add Server/GameLoop.cs Server/Player/ServerStatusManager.cs Server/Player/PlayerState.cs \
  Server/Aoi/CombatProjectionBuilder.cs Server/Server3C.cs
git add Server/Server.Tests/Server/GameLoopOrderingTests.cs \
  Server/Server.Tests/Handler/ServerStatusManagerTests.cs
git commit -m "feat(server): tick authoritative statuses before input"
```

---

### 任务 3：首个有效命中附加元素 + 风倍率 + 事件 ID

**文件：**
- 修改：`Server/Handler/CastHandler.cs`
- 修改：`Server/Player/PlayerState.cs`（Add `NextEventSeq`/`AllocateEventSeq`）
- 测试：`Server/Server.Tests/Combat/ElementalApplicationTests.cs`（新建）
- 测试：`Server/Server.Tests/Combat/EventIdentityTests.cs`（新建）

- [ ] **步骤 1：写失败测试**

覆盖：
- 10002 挥砍先命中 → 附加一次；后续剑气不再附加（同一目标）。
- 剑气先命中 → 由剑气附加；后续挥砍不再附加。
- 格挡/0 伤害/目标已死不附加。
- 风同次施法首个命中附加后，后续段伤害读取新倍率；PvE 2.0，PvP 1.28。
- `HitEntry.EventSeq` 按目标单调、`EventId` 唯一、`CombatVersion` 等于目标当前版本。

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ElementalApplicationTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~EventIdentityTests"
```

- [ ] **步骤 3：实现**

`PendingCast` 追加：

```csharp
public readonly HashSet<(long TargetId, uint LifeId)> ElementAppliedTargets = new();
```

`ApplyHit` 在 `target.ApplyDamage(damage)` 之前读风倍率：

```csharp
bool targetIsPlayer = target.Kind == (byte)EntityKind.Player;
bool pvp = targetIsPlayer; // 元素只由玩家武器施加；target 玩家即 PvP
if (target.Statuses != null)
    damage *= target.Statuses.Runtime.IncomingDamageMultiplier(pvp,
        _elementalCatalog.Pvp.WindVulnerabilityMultiplier);
```

在 `target.ApplyDamage` 后、`Hits.Add` 前附加（`ElementId` 只对 10002 生效）：

```csharp
if (!pending.IsNpcCaster && pending.Request.SkillId == 10002
    && pending.Weapon.ElementId > 0 && damage > 0f && !blocked && target.IsAlive)
{
    var key = (targetId, target.LifeId);
    if (pending.ElementAppliedTargets.Add(key))
    {
        target.ConfigureStatuses(_elementalCatalog);
        var element = _elementalCatalog.Elements.Find(e => e.ElementId == pending.Weapon.ElementId);
        if (element != null)
            foreach (int effectId in element.StatusEffectIds)
            {
                var def = _elementalCatalog.Statuses.Find(s => s.EffectId == effectId);
                if (def != null)
                    target.Statuses!.Apply(def, pending.CasterId, pending.Request.SkillId,
                        pending.CastId, tick, _elementalCatalog);
            }
    }
}
```

`ApplyHit` 当前签名没有 `tick`；加上 `uint tick` 参数并从 `ResolveStage` 传入。`CombatResolver` 的 `tick` 在 `ResolveStage(pending,tick)` 可用。

`HitEntry` 填充：

```csharp
uint seq = target.AllocateEventSeq();
resolution.Hits.Add(new HitEntry {
    ...,
    EventSeq = seq,
    EventId = AllocateEventId(),
    CombatVersion = target.CombatVersion,
});
```

`CombatResolver` 增加 `private ulong _nextEventId; public ulong AllocateEventId() => ++_nextEventId;`。`EventId` 全局唯一；`EventSeq` 每目标单调（复活后 `LifeId` 变，但 `NextEventSeq` 不重置——协议按 `(LifeId, EntityId)` 序列，复活后序号继续单调即可）。

- [ ] **步骤 4：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ElementalApplicationTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~EventIdentityTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolver"
```

- [ ] **步骤 5：Commit**

```bash
git add Server/Handler/CastHandler.cs Server/Player/PlayerState.cs \
  Server/Server.Tests/Combat/ElementalApplicationTests.cs \
  Server/Server.Tests/Combat/EventIdentityTests.cs
git commit -m "feat(server): apply elemental status on first valid hit"
```

---

### 任务 4：灼烧跳数、DamageResult 与死亡/离场清理

**文件：**
- 创建：`Server/Status/StatusTickService.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Server/Handler/CastHandler.cs`（DoT 广播入口）
- 修改：`Server/Player/PlayerState.cs`（死亡/复活清状态）
- 修改：`Server/Monster/Brain/MonsterBrain.cs`（Evade 到家清状态）
- 测试：`Server/Server.Tests/Combat/DotDamageTests.cs`（新建）
- 测试：`Server/Server.Tests/Combat/DotTerminalTickTests.cs`（新建）

- [ ] **步骤 1：写失败测试**

覆盖：
- 灼烧按间隔掉血，`DamageKind=Dot`，每跳 `EventSeq/EventId/CombatVersion` 正确；目标死亡后不再跳。
- 末跳在 `EndTick` 结算且只一次；风同 `EndTick` 不放大末跳。
- 怪物 DoT 走 `MonsterModule.OnDamageTaken(Dot)`：不进入 `HitStartTick/ActionState=Hit`，仇恨按 `damage × ThreatPerDamage`。
- 玩家 DoT 死亡后清状态；复活清状态；Evade 到家清状态并回满血。
- `DamageResult` 收件人 = 来源 + 目标 + 观察者，与直接伤害同规则。

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~DotDamageTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~DotTerminalTickTests"
```

- [ ] **步骤 3：实现 `StatusTickService`**

```csharp
public sealed class StatusTickService
{
    private readonly EntityStore _store;
    private readonly CastHandler _cast;
    private readonly MonsterModule? _monsters;
    private readonly ElementalCatalogDefinition _catalog;

    public void Tick(uint tick)
    {
        foreach (var entity in _store.All)
        {
            if (entity.Statuses == null) continue;
            entity.Statuses.RemoveExpired(tick);
            var due = new List<ActiveStatus>();
            entity.Statuses.Runtime.CollectDueDots(tick, due);
            foreach (var status in due)
            {
                if (!entity.IsAlive) { entity.Statuses.ClearAll(tick); break; }
                bool pvp = entity.Kind == (byte)EntityKind.Player;
                float wind = pvp ? 1f : entity.Statuses.Runtime.IncomingDamageMultiplier(
                    pvp: false, _catalog.Pvp.WindVulnerabilityMultiplier);
                float damage = status.Definition.DamagePerTick * wind;
                entity.ApplyDamage(damage);
                if (entity.Kind == (byte)EntityKind.Npc)
                    _monsters?.OnDamageTaken(entity.PlayerId, status.SourceId, damage, MonsterDamageKind.Dot);

                var hit = new HitEntry
                {
                    TargetPlayerId = entity.PlayerId,
                    Damage = damage,
                    TargetRemainingHp = entity.Hp,
                    CastId = status.SourceCastId,
                    DamageKind = DamageKind.Dot,
                    TargetLifeId = entity.LifeId,
                    EventSeq = entity.AllocateEventSeq(),
                    EventId = _cast.AllocateEventId(),
                    CombatVersion = entity.CombatVersion
                };
                _ = _cast.BroadcastDotDamageAsync(status.SourceId, status.SourceSkillId, hit);
                entity.Statuses.AdvanceDot(status, tick);
                if (!entity.IsAlive) entity.Statuses.ClearAll(tick);
            }
            entity.Statuses.Runtime.RemoveExpiredDots(tick);
        }
    }
}
```

`CastHandler` 增加：

```csharp
public Task BroadcastDotDamageAsync(long sourceId, int skillId, HitEntry hit)
{
    var result = new DamageResult
    {
        SourcePlayerId = sourceId, SkillId = skillId,
        ServerTimestamp = ServerClock.NowTicks(),
        Hits = new List<HitEntry> { hit },
        SourceKind = (int)EntityKind.Player
    };
    var recipients = CollectDamageRecipients(
        new[] { hit.TargetPlayerId }, sourceId, npcCaster: false,
        id => _server.GetObserversOf(id), id => _server.GetVisiblePlayers(id));
    foreach (var id in recipients) { var s = _server.GetSession(id); if (s != null) _ = _server.SendToSessionAsync(s, MessageId.DamageResult, result); }
    return Task.CompletedTask;
}
```

`PlayerState.MarkDead`/`TryRespawn` 调 `Statuses?.ClearAll(tick)`（`MarkDead` 的 tick 参数；TryRespawn 用 nowTick）。`MonsterBrain.ArriveHome` 调 `entity.Statuses?.ClearAll(tick)`。

`Server3C` 构造 `StatusTickService` 并在 preTick 中 `_statusService.Tick(tick)`（任务 2 的 preTick 钩子）。

- [ ] **步骤 4：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~DotDamageTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~DotTerminalTickTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterEvadeTests"
```

- [ ] **步骤 5：Commit**

```bash
git add Server/Status/StatusTickService.cs Server/Server3C.cs Server/Handler/CastHandler.cs \
  Server/Player/PlayerState.cs Server/Monster/Brain/MonsterBrain.cs
git add Server/Server.Tests/Combat/DotDamageTests.cs Server/Server.Tests/Combat/DotTerminalTickTests.cs
git commit -m "feat(server): tick burn damage and publish dot results"
```

---

### 任务 5：怪物控制、霸体与 Evade 清理

**文件：**
- 修改：`Server/Monster/MonsterModule.cs`
- 修改：`Server/Monster/MonsterBtActions.cs`
- 修改：`Server/Monster/MonsterActionRunner.cs`
- 修改：`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Monster/MonsterStatusControlTests.cs`（新建）

- [ ] **步骤 1：写失败测试**

覆盖：
- 冰减速后怪物 `UpdatePosition.Speed` = `Config.moveSpeed × SpeedScale`。
- 雷 `ActionLock` 时行为树不再发起新攻击/移动；未霸体前摇被 `Cancel`，霸体（SpiderBoss）不被取消但状态仍挂上。
- 麻痹结束恢复移动/攻击；Evade 到家清状态并回满血。
- 玩家 DoT/控制不受怪物霸体逻辑影响。

- [ ] **步骤 2：实现**

`MonsterModule.Tick`：

```csharp
bool actionLocked = entity.ControlActionLock;
...
if (!inHitWindow && !actionLocked && (m.ActionRunner == null || m.ActionRunner.CanStartSkill))
    m.BehaviorTree?.Tick(ctx);
else m.Moving = false;

entity.UpdatePosition(m.Position, YawOf(m.Rotation),
    m.Moving ? m.Config.moveSpeed * entity.ControlSpeedScale : 0f, tick * 10_000L + 1L, tick);
```

`MonsterBtActions` 的攻击入口开头：`if (entity.ControlActionLock) { m.Moving=false; return BtStatus.Failure; }`。

`MonsterActionRunner` 暴露 `public bool IsArmored => _armor;`；`MonsterModule` 增加：

```csharp
public void CancelAction(long npcId, NpcActionCancelReason reason, uint tick)
{
    if (!_monsters.TryGetValue(npcId, out var m) || m.ActionRunner == null) return;
    if (m.ActionRunner.IsArmored) return; // 首版：霸体只免打断
    m.ActionRunner.Cancel(reason, tick);
}
```

`Server3C.ApplyPlayerControl` 在 `!wasLocked && actionLock` 时，若实体是 NPC，调用 `_monsterModule?.CancelAction(playerId, NpcActionCancelReason.Hit, CurrentTick)`；玩家仍走 `CancelCaster`。

`MonsterBrain.ArriveHome` 调 `entity.Statuses?.ClearAll(tick)`。

- [ ] **步骤 3：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterStatusControlTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterActionRunnerTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterEvadeTests"
```

- [ ] **步骤 4：Commit**

```bash
git add Server/Monster/MonsterModule.cs Server/Monster/MonsterBtActions.cs \
  Server/Monster/MonsterActionRunner.cs Server/Server3C.cs
git add Server/Server.Tests/Monster/MonsterStatusControlTests.cs
git commit -m "feat(server): apply status control to monsters"
```

---

### 任务 6：客户端 `DamageKind=Dot` 分流

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/DotDamageRoutingTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs`（补充）

- [ ] **步骤 1：写失败测试**

- 本地玩家收到 `DamageKind=Dot`：HP 变为 `TargetRemainingHp`、飘字一次，不触发受击 FSM/格挡/击退/命中特效。
- 远程玩家/怪物 Dot：`Targetable` 血条更新 + 对应飘字事件；不调用 `PlayHit`、不发 `MonsterTakeDamageEvent`（避免命中特效/震屏），怪物改用 `MonsterDotDamageEvent`。
- 直接伤害（`DamageKind=Direct`）行为完全不变。

- [ ] **步骤 2：实现**

`CombatNetworkBridge` 构造增加可选 `Action<long, float, float>? onDotDamageToLocalPlayer`（目标 id、伤害、余血）。`ApplyDamageResult`：

```csharp
if (hit.DamageKind == DamageKind.Dot)
{
    if (hit.TargetPlayerId == localId)
    {
        view?.Targetable?.SetVitals(...);
        _onDotDamageToLocalPlayer?.Invoke(hit.TargetPlayerId, hit.Damage, hit.TargetRemainingHp);
        continue;
    }
    // 远程：更新 Targetable；NPC 发 MonsterDotDamageEvent，玩家发 PlayerTakeDamageEvent；
    // 绝不调用 PlayHit/BlockSucceededEvent/MonsterTakeDamageEvent。
    continue;
}
```

`ClientNetworkHost` 把 `onDotDamageToLocalPlayer` 接到 `Sys3CEntry.ApplyDotDamage(damage, remainingHp)`；后者只改 `_currentHP`、发 `PlayerTakeDamageEvent` 飘字、必要时 `EvaluateLocalDeath`，不调用 `_fsmManager.HandleDamage`/格挡/击退/Flash。

- [ ] **步骤 3：运行测试**

```bash
bash tools/unity-tests.sh EditMode DotDamageRoutingTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
```
MCP 不可用则直接 batchmode（不加 `-quit`），还原 `Assets/Packages/**`/`.meta`/`ProjectSettings/**`。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs
git add Assets/Scripts/Tests/Editor/SyncLayer/DotDamageRoutingTests.cs
git commit -m "feat(client): route dot damage without hit reactions"
```

---

### 任务 7：权威状态表现与剑气元素染色

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（状态变更事件）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（本人状态消费）
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/AuthoritativeStatusVfx.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs`（Slow/Stun/Vulnerable 颜色）
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs` / `SwordQiProjectile.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`（`CurrentElementId`/provider）
- 测试：`Assets/Scripts/Tests/Editor/VFX/AuthoritativeStatusVfxTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/Skills/SwordQiElementTintTests.cs`（新建）

- [ ] **步骤 1：写失败测试**

- 权威状态列表变化时，`AuthoritativeStatusVfx` 对同一实体按 `(EntityId, GroupId)` 只保留一条，应用/移除各一次，不因每帧投影重复。
- 冰/火/雷/风映射到四种颜色；`StatusVFXSystem` 能对 `Stun`/`Vulnerable` 染色并在移除后清理。
- `SwordQiSpawner.Spawn(..., elementId)` 后剑气带元素色；`ElementPalette.ColorOf(1..4)` 四种颜色不同；`elementId=0` 不染色。

- [ ] **步骤 2：实现最小表现**

`RemoteEntityRegistry` 在 `ApplyProjection` 成功且 `Statuses` 变化后发：

```csharp
public event Action<long, GameObject, IList<StatusProjection>>? StatusesChanged;
```

`ClientNetworkHost` 订阅并调用 `_statusVfx.Apply(entityId, view.GameObject.transform, statuses)`；本人 311 分支调用 `_statusVfx.Apply(localId, _localEntry.transform, projection.Statuses)`。

`AuthoritativeStatusVfx` 维护 `Dictionary<long, Dictionary<int, int>>`（entity→group→effectId），diff 后发 `StatusEffectAppliedEvent`/`StatusEffectRemovedEvent`：

```csharp
EffectType Map(int effectId) => effectId / 100 switch
{
    1 => EffectType.Freeze,    // 冰
    2 => EffectType.Burn,      // 火
    3 => EffectType.Stun,      // 雷
    4 => EffectType.Vulnerable,// 风
    _ => EffectType.Debuff
};
```

`EffectType` 追加 `Vulnerable`（末尾，不改既有值）。`StatusVFXSystem` 的 `OnApplied/OnRemoved` 接受 `Stun`/`Vulnerable`，颜色：冰蓝、火橙、雷紫、风青；同一实体同组只计数一次。

`SwordQiSpawner.Spawn` 增加 `int elementId = 0`，`SwordQiProjectile.OnSpawn` 保存并由 `SwordQiElementTint.Apply(elementId)` 设置 `TrailRenderer.startColor`/`ParticleSystem.startColor`/材质 `_Color`；`ElementPalette` 常量四种颜色。`RemoteCastPresenter` 传 `launch.ElementId`；`Sys3CEntry` 暴露 `CurrentElementId` 并在 `ApplySwordView` 更新；离线 `SkillExecutor` 通过注入的 `ElementIdProvider` 传本地元素（Plan 05 接离线状态时启用）。

- [ ] **步骤 3：运行测试**

```bash
bash tools/unity-tests.sh EditMode AuthoritativeStatusVfxTests
bash tools/unity-tests.sh EditMode SwordQiElementTintTests
bash tools/unity-tests.sh EditMode StatusVFXSystemTests
```
MCP 不可用则直接 batchmode，还原副作用。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
  Assets/Scripts/Hotfix/GameSystems/VFX/AuthoritativeStatusVfx.cs \
  Assets/Scripts/Hotfix/GameSystems/VFX/StatusVFXSystem.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git add Assets/Scripts/Tests/Editor/VFX/AuthoritativeStatusVfxTests.cs \
  Assets/Scripts/Tests/Editor/Skills/SwordQiElementTintTests.cs
git commit -m "feat(client): show authoritative statuses and elemental sword qi"
```

---

### 任务 8：文档、离线边界与全量回归

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/README.md`
- 修改：`Server/README.md`
- 修改：`Server/Monster/README.md`
- 修改：`docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md`
- 修改：`docs/superpowers/specs/2026-11-14-elemental-sword-qi-design.md`

- [ ] **步骤 1：全量回归**

```bash
bash tools/test-server-inprocess.sh
bash tools/unity-tests.sh EditMode StatusRuntimeTests
bash tools/unity-tests.sh EditMode ElementalApplicationTests
bash tools/unity-tests.sh EditMode DotDamageRoutingTests
bash tools/unity-tests.sh EditMode AuthoritativeStatusVfxTests
bash tools/unity-tests.sh EditMode SwordQiElementTintTests
bash tools/unity-tests.sh EditMode StatusVFXSystemTests
bash tools/unity-tests.sh EditMode RemoteEntityRegistryTests
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
```
MCP 不可用则 batchmode；`RemoteEntityRegistryTests.Bot_Radius` 既有 PPtr 失败可登记，其他失败必须修。

- [ ] **步骤 2：回写文档**

必须写清：
- Plan 04 已完成：服务端权威四状态、同次施法一次、DoT 末跳、`DamageKind` 分流、客户端状态/剑气表现。
- **离线单机未接入共享状态运行时**：本地 `StatusEffectManager` 的冰冻/灼烧保持独立；离线四元素与去重留给 Plan 05。这是本计划的明确边界，不能写成已打通。
- `Statuses` 已启用；`ApplyPlayerControl` 现在由状态运行时调用（不再是“无生产调用者”）。
- `ControlSummary` 的 `SpeedScale/ActionLock` 现在会随冰/雷变化。
- 测试数字：服务端 xx/xx、Unity 目标类、已知 PPtr 失败。
- 路线图/设计规格 Plan 04 ✅；Plan 05 未开始，并在 Plan 05 登记“离线共享状态运行时/三客户端验收/GetObserversOf O(N²)”。

- [ ] **步骤 3：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/README.md \
  Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md \
  Assets/Scripts/Hotfix/GameSystems/VFX/README.md \
  Assets/Scripts/Hotfix/GameSystems/Skills/README.md \
  Server/README.md Server/Monster/README.md \
  docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md \
  docs/superpowers/specs/2026-11-14-elemental-sword-qi-design.md
git commit -m "docs: record status runtime and dot scope"
```

---

## 自检

**1. 规格覆盖度**
- 冰/火/雷/风四状态：任务 1/3/5/7；同次施法一次：任务 3；同 tick 顺序：任务 2/4；DoT 末跳：任务 1/4；`DamageKind` 分流：任务 6；元素 VFX：任务 7。
- PvP 控制规则（0.5s 免疫/4s 不刷新/雷 0.8s）：任务 1/3；PvE/PvP 风倍率：任务 1/3/4。
- 离线边界：明确留给 Plan 05，文档在任务 8 如实登记。
- 协议：`Statuses`/`DamageKind`/`EventSeq` 已有字段，本计划只填充；不新增消息。

**2. 占位符扫描**
- 无“TODO/待定/类似任务”；关键 API、时序、测试用例与提交命令均已给出。
- 任务 7 的表现实现是程序化染色/简化特效，不依赖缺失的美术资源；ViewKey 留空。

**3. 类型一致性**
- `StatusRuntime`/`ActiveStatus`/`StatusApplyResult` 在 `Cool.SyncCore`；`ServerStatusManager`/`StatusTickService` 在 `KcpServer`。
- `EffectType` 只追加 `Vulnerable`，不改既有值；`DamageKind` 既有 Direct=0/Dot=1。
- `CombatProjection.Statuses` 是 `IList<StatusProjection>`，不引入 `List<T>` 协议成员。
