# 三客户端验收 + 表现收口实现计划（Plan 05）

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 对已完成的属性剑气 01–04 做最终验收：AOI 观察者反查从 O(N²) 降为反向索引、三客户端元素一致性、100 实体 ×15Hz×60s 带宽门禁、收包重复/乱序注入、麻痹空中惯性/推挤纠偏、元素表现回退与全量回归。

**架构：** 不新增战斗机制。AOI 在 `GetVisible` 维护 `entityId → observers` 反向索引，`GetObserversOf` 只扫观察者集合；验收以 PlayMode 三客户端 + 服务端 xUnit 编码回放 + 消息消费层注入完成；表现只做 Addressables 键解析/回退与人工美术签收边界，不改战斗逻辑。

**技术栈：** Unity 2022.3.25f1 / C# 9、.NET 8 服务端、protobuf-net、NUnit/xUnit。

---

## 范围与边界

**本计划覆盖：**
- AOI 反向索引（Plan 05 登记项 `GetObserversOf` O(N²)）。
- 三客户端元素验收：A 施法、B 受击、C 旁观，记录 `CastId/EventSeq/CombatVersion`、状态与 AoI。
- 带宽：100 实体 ×15Hz×60s、含 `CombatProjection/Statuses`、DoT/可靠事件，每观察者 ≤100 KB/s、单 tick ≤150 KB。
- 收包消费层重复/乱序注入；KCP/WebSocket 正常流程回归。
- 麻痹空中惯性、`NpcBody` 推挤纠偏的客户端/服务端预算验收。
- 元素表现回退（Addressables 键解析/加载失败用程序化颜色）与全量回归。

**明确不在 Plan 05：**
- 离线单机共享状态运行时（本地 `StatusEffectManager` 仍独立）→ **Plan 06/backlog**；原 Plan 04 文档的“Plan 05”前瞻登记在本计划任务 6 修正。
- 新美术资源制作；Addressables 元素资源若缺失必须回退到程序化表现，不阻塞战斗。
- 新战斗机制/协议字段。

## 全局约束

- 验收断言必须是数字/集合相等，不能只断言“非空”。
- 带宽预算 `GlobalGameSettings.BandwidthBudgetBytesPerSec = 100_000`；单 tick 上界 150 KB；按实际 `MessageCodec.EncodeToArray` 字节计算。
- 三客户端测试使用真实服务器（默认 `127.0.0.1:8000`），必须等待 AOI 可见再施法；超时失败输出阶段。
- 消息注入只在消费层（`CombatNetworkBridge`/`RemoteEntityRegistry`）做，不伪造服务端权威结果。
- AOI 反向索引必须与旧暴力反查结果逐项相等（同一 tick、同一迟滞状态）。
- 不新增协议消息；`ProtocolVersion` 保持 1。
- 不改变 Plan 01–04 的权威语义；验收失败先修测试/文档，不改生产逻辑除非计划明确。

## 文件结构

| 文件 | 职责 |
|---|---|
| `Server/Aoi/AoiGrid.cs` | `_observersByEntity` 反向索引、`GetObserversOf`、清理 |
| `Server/Aoi/AoiBroadcaster.cs` | 反查转发、`HasSightState` |
| `Server/Server3C.cs` | `GetObserversOf` 改用反查；首 tick 回退 |
| `Assets/Scripts/Tests/PlayMode/ThreeClientElementalAcceptanceTests.cs` | 三客户端元素验收 |
| `Server/Server.Tests/Aoi/BandwidthBudgetTests.cs` | 60s 状态/DoT 带宽回放 |
| `Assets/Scripts/Tests/Editor/SyncLayer/NetworkConsumptionInjectionTests.cs` | 重复/乱序消费 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | `EventId` 有界去重 |
| `Assets/Scripts/Tests/PlayMode/PlayerControlInertiaTests.cs` | 麻痹空中惯性/推挤 |
| `Assets/Scripts/Hotfix/GameSystems/VFX/ElementVfxFallback.cs` | Addressables 键解析/颜色回退 |
| `docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md` / `design.md` | Plan 05 ✅；离线改 Plan 06/backlog |

---

### 任务 1：AOI 观察者反向索引

**文件：**
- 修改：`Server/Aoi/AoiGrid.cs`
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Server/Server3C.cs`
- 测试：`Server/Server.Tests/Aoi/AoiReverseIndexTests.cs`（新建）

- [ ] **步骤 1：写失败测试**

- 随机 50 实体、多个 tick 位置，逐个调用 `GetVisible` 后，`GetObserversOf(id)` 与暴力扫描（旧 `Server3C` 逻辑）结果集合逐项相等。
- 迟滞：进入 80m 可见、80–90m 保持、>90m 移除；索引与 `GetVisible` 同步。
- `RemoveEntity`/`RemoveObserver` 清理索引，重登同 id 不残留。
- 性能：N=1000、100 个观察者，单次 `GetObserversOf` ≤1ms（Debug 本机，重复 100 次取中位数；仅作回归护栏）。

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiReverseIndexTests"
```
预期：`GetObserversOf` 不存在，编译失败。

- [ ] **步骤 3：实现**

`AoiGrid`：

```csharp
private readonly Dictionary<long, HashSet<long>> _observersByEntity = new();
public int TrackedObservers => _sightState.Count;

public List<long> GetObserversOf(long entityId)
    => _observersByEntity.TryGetValue(entityId, out var set)
        ? new List<long>(set) : new List<long>();

// GetVisible 内，计算 visibleNow 后：
if (wasVisible != visibleNow)
{
    if (visibleNow)
    {
        if (!_observersByEntity.TryGetValue(p.PlayerId, out var observers))
        {
            observers = new HashSet<long>();
            _observersByEntity[p.PlayerId] = observers;
        }
        observers.Add(observerId);
    }
    else if (_observersByEntity.TryGetValue(p.PlayerId, out var observers))
    {
        observers.Remove(observerId);
        if (observers.Count == 0) _observersByEntity.Remove(p.PlayerId);
    }
}
```

`RemoveEntity` 追加 `_observersByEntity.Remove(entityId);`；`RemoveObserver` 遍历所有 set 移除 observer 并清理空 set。

`AoiBroadcaster` 增加：

```csharp
public bool HasSightState => _grid.TrackedObservers > 0;
public List<long> GetObserversOf(long entityId) => _grid.GetObserversOf(entityId);
```

`Server3C.GetObserversOf`：

```csharp
public List<long> GetObserversOf(long entityId)
{
    if (_aoi.HasSightState)
    {
        var indexed = _aoi.GetObserversOf(entityId);
        indexed.RemoveAll(id => !_playerIdToSession.ContainsKey(id));
        return indexed;
    }
    // 首 tick/尚无观察者快照：保留旧暴力路径，避免漏发第一条 311。
    var result = new List<long>();
    foreach (var observer in _entityStore.All) { /* 旧逻辑 */ }
    return result;
}
```

- [ ] **步骤 4：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiReverseIndexTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AoiBroadcasterTests"
bash tools/test-server-inprocess.sh
```
预期：全部 PASS；全量从 654 增加新用例。

- [ ] **步骤 5：Commit**

```bash
git add Server/Aoi/AoiGrid.cs Server/Aoi/AoiBroadcaster.cs Server/Server3C.cs \
  Server/Server.Tests/Aoi/AoiReverseIndexTests.cs
git commit -m "perf(aoi): index observers by entity"
```

---

### 任务 2：三客户端元素一致性验收

**文件：**
- 创建：`Assets/Scripts/Tests/PlayMode/ThreeClientElementalAcceptanceTests.cs`
- 参考：`ThreeClientCombatConsistencyTests.cs` 的连接/等待模式

- [ ] **步骤 1：写失败测试**

测试步骤：
1. A/B/C 登录同一服务器（`127.0.0.1:8000`），等 C 的 registry 看到 B。
2. A 从 `WeaponInventory.Weapons` 找 `ElementId == 3`（雷剑）；发送 `EquipWeaponRequest { ClientEquipSeq=1, WeaponInstanceId=..., KnownEquipmentRevision=baseline.EquipmentRevision }`，等 `EquipWeaponConfirm.Accepted`。
3. A 发送 `CastRequest { SkillId=10002, TargetPlayerId=B, Shape{ShapeType=0,Range=2.5,Angle=120}, ClientCastSeq=1, EquipmentRevision=confirm.EquipmentRevision, CasterLifeId=baseline.LifeId }`（`EquipWeaponConfirm` 不含 LifeId，用登录基线）。
4. 收集 A/B/C 的 `CastBroadcast`、`DamageResult`、`CombatStateUpdate`。
5. 断言：
   - A 的 `CastBroadcast.ElementId == 3`；`CastConfirm.Accepted`。
   - A/B/C 同一 `HitEntry` 的 `CastId`、`EventSeq`、`CombatVersion`、`TargetRemainingHp` 逐项相等。
   - B 本人 311：`Projection.Statuses` 含 `EffectId=301`，`Control.ActionLock == true`，`ControlVersion > 0`。
   - C 把 B 的 311 喂给 `c.Registry.ApplyCombatStateUpdate` 后，`GetView(B).Statuses` 同样含 301 且 `Control.ActionLock` 为真。
   - 0.8s（12 tick + 余量）后，B/C 收到 `Statuses` 空、`ActionLock == false` 的投影；断言只发生一次 `EffectId=301` 的移除（按 `CombatVersion` 递增）。

- [ ] **步骤 2：运行测试确认失败/超时**

先起服务端：
```bash
dotnet build Server/Server3C.csproj
dotnet Server/bin/Debug/net8.0/Server3C.dll 8000
```
再跑 PlayMode（Unity MCP 不可用时 batchmode，`UNITY_MCP_DISABLE=1`，300s 超时，XML 落盘即杀进程）：
`ThreeClientElementalAcceptanceTests`
预期：测试存在但业务断言失败/超时（元素装备链尚未被该场景覆盖）。

- [ ] **步骤 3：实现测试**

使用 `KcpNetClientAdapter`/`ClientNetworkManager` 与现有 `ThreeClientCombatConsistencyTests` 的等待循环；`RemoteEntityRegistry` 只用于 C 的视图断言。所有等待必须有 `120s` 总超时与阶段字符串。

- [ ] **步骤 4：运行测试**

同一服务器下重跑；若失败，输出 A/B/C 三个客户端收到的 `CastBroadcast/DamageResult/CombatStateUpdate` 摘要（CastId、EffectId、ActionLock、版本），再判断是测试时序还是服务端缺陷。不得放宽断言。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/ThreeClientElementalAcceptanceTests.cs
git commit -m "test(playmode): accept three-client elemental status"
```

---

### 任务 3：100 实体 ×15Hz×60s 状态/DoT 带宽门禁

**文件：**
- 修改：`Server/Server.Tests/Aoi/BandwidthBudgetTests.cs`
- 可选：`production/verify/plan05-bandwidth/report.md`（记录实测数字）

- [ ] **步骤 1：写失败测试**

新增 `Hundred_Entities_Status_And_Dot_60s_Stay_Under_100_KBps_Per_Observer`：
- 构造 100 实体，1 个观察者；每个 tick（900 tick）为每实体构造 `EntitySnapshot`：
  - 位置/旋转/速度/Dead/Defending 全量；
  - 每 15 tick 一个实体带 `Health` + `CombatVersion/LifeId`；
  - 每 30 tick 一个实体带 `Combat` 投影（4 个 `StatusProjection` + `ActiveCast`）；
  - 每 15 tick 构造一条 `CombatStateUpdate`（4 状态）与一条 `DamageResult`（DoT HitEntry）。
- 用 `MessageCodec.EncodeToArray` 实际编码；累加 60 秒所有字节。
- 断言：
  - 每观察者总字节 ≤ `GlobalGameSettings.BandwidthBudgetBytesPerSec * 60`。
  - 单 tick（含 WorldSnapshot + 311 + DamageResult）≤ 150 * 1024。
  - 投影基线单实体 ≤ 300B（沿用现有上下限）。
- 输出 `perTickMax`, `totalBytes`, `kbps` 到测试输出（`ITestOutputHelper`）。

- [ ] **步骤 2：运行测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BandwidthBudgetTests"
```
预期：当前无状态/DoT 回放，测试失败或低于下限。

- [ ] **步骤 3：实现/调参**

只改测试数据构造，不改预算常量。若超 100KB/s，先降测试中的近带实体数/保活频率并记录原因；不得上调预算。

- [ ] **步骤 4：Commit**

```bash
git add Server/Server.Tests/Aoi/BandwidthBudgetTests.cs
git commit -m "test(aoi): budget status and dot replay for 60 seconds"
```

---

### 任务 4：收包重复/乱序 + `EventId` 去重 + KCP/WebSocket 正常流程

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/NetworkConsumptionInjectionTests.cs`
- 参考：`RemoteCastPresenterCastIdTests`、`CombatStateProjectionTests`、`WebSocketClientAdapterTests`

- [ ] **步骤 1：写失败测试**

`NetworkConsumptionInjectionTests`：
- 同一 `DamageResult`（同一 `HitEntry.EventId`）投递两次：本地受击回调/飘字只执行一次；不同 EventId 执行两次。
- `CombatStateUpdate` 版本 5 后投递版本 4：registry 丢弃 4；版本 5 的状态/装备不回退。
- `ProjectileStopBroadcast` 先于 `ProjectileLaunchBroadcast`：launch 到达后仍只创建一个视觉并按停点结束；重复 launch/stop 幂等（复用 presenter 测试接缝）。
- `CastBroadcast` 重复：同一 `CastId` 只安排一次攻击（registry/presenter 去重）。
- 正常 KCP/WebSocket：跑 `WebSocketClientAdapterTests`、`WebSocketServerSessionTests`，确认无改动回归。

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/unity-tests.sh EditMode NetworkConsumptionInjectionTests
```
MCP 不可用则 batchmode（300s 超时，XML 落盘即杀进程）；预期 `EventId` 去重用例失败（当前无去重）。

- [ ] **步骤 3：实现**

`CombatNetworkBridge` 增加有界去重：

```csharp
private readonly HashSet<ulong> _seenEventIds = new HashSet<ulong>();
private readonly Queue<ulong> _eventIdOrder = new Queue<ulong>();
private const int MaxSeenEventIds = 1024;

private bool MarkEventSeen(ulong eventId)
{
    if (eventId == 0) return true; // 旧服务端未分配：不去重
    if (!_seenEventIds.Add(eventId)) return false;
    _eventIdOrder.Enqueue(eventId);
    if (_eventIdOrder.Count > MaxSeenEventIds)
        _seenEventIds.Remove(_eventIdOrder.Dequeue());
    return true;
}
```

`ApplyDamageResult` 对每个 hit：`if (!MarkEventSeen(hit.EventId)) continue;`。清空条件：连接重置/新会话时清空（`ClientNetworkBridge` 可由宿主在 `Reset` 时清；本任务至少提供 `ClearDedupe` 并在断线路径调用，若没有则文档登记）。

- [ ] **步骤 4：运行测试**

```bash
bash tools/unity-tests.sh EditMode NetworkConsumptionInjectionTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/unity-tests.sh EditMode RemoteCastPresenterCastIdTests
bash tools/unity-tests.sh EditMode CombatStateProjectionTests
bash tools/unity-tests.sh EditMode WebSocketClientAdapterTests
```

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/NetworkConsumptionInjectionTests.cs
git commit -m "test(sync): inject duplicate and reordered combat messages"
```

---

### 任务 5：麻痹空中惯性 / 推挤纠偏验收

**文件：**
- 创建：`Assets/Scripts/Tests/PlayMode/PlayerControlInertiaTests.cs`
- 参考：`PlayerJumpBodyCollisionTests`、`MovementSyncTests`
- 可选：`Server/Server.Tests/Server/PvpMovementBudgetTests.cs` 补充空中麻痹包

- [ ] **步骤 1：写失败测试**

- 本地 `CharacterController`：起跳后 `SetStatusActionLock(true)`，推进 0.3s；断言水平位移 > 0 且方向/速度保持起跳值，`JumpSeq` 不再增加；落地后位移停止；解锁后恢复输入。
- 地面麻痹：按住移动 + 新跳跃请求；断言水平位移 0、`JumpSeq` 不变。
- 推挤：贴 `NpcBody` 站立并用移动输入挤压 0.5s；断言位置往返幅度 ≤0.1m（无持续抖动），且服务端预算/纠偏不产生超预算位移。
- 服务端：构造空中麻痹位置包，断言 `JumpInertiaDistance` 按起跳水平速度 × 剩余滞空时间发放，`ActionLock` 期间普通预算 0。

- [ ] **步骤 2：运行测试**

PlayMode（Unity MCP 不可用则 batchmode，300s 超时，XML 落盘即杀进程）：
`PlayerControlInertiaTests`
服务端：
`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~PvpMovementBudgetTests"`

- [ ] **步骤 3：实现/修正**

若客户端无空中水平惯性（`CharacterController` 已有 `_jumpStartHorizontalVelocity`），补测试夹具；若推挤抖动超限，优先修测试场景设置或记录为既有 `NpcBody` 债，不改服务端预算公式。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/PlayerControlInertiaTests.cs \
  Server/Server.Tests/Server/PvpMovementBudgetTests.cs
git commit -m "test(playmode): accept paralysis inertia and push correction"
```

---

### 任务 6：元素表现回退、文档与全量回归

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/ElementVfxFallback.cs`
- 测试：`Assets/Scripts/Tests/Editor/VFX/ElementVfxFallbackTests.cs`
- 修改：`docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md`
- 修改：`docs/superpowers/specs/2026-11-14-elemental-sword-qi-design.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md`（离线边界指向 Plan 06）
- 修改：`Server/README.md`（验收数字）

- [ ] **步骤 1：写失败测试**

`ElementVfxFallback`：
- `ViewKey` 为空 → 回退到 `ElementPalette.ColorOf(elementId)`；四种元素颜色非白且互异；未知元素回退白。
- 注入一个假 `Func<string, GameObject?>` loader：key 有资源返回资源；缺失返回 null → 回退颜色且不抛。
- 不实际调用 Addressables（避免 `AsyncOperationHandle.Task` 违规）；生产用 Addressables 的宿主在 Plan 06 接入。

- [ ] **步骤 2：实现**

```csharp
public static class ElementVfxFallback
{
    public static Color ResolveColor(int elementId, string viewKey, Func<string, GameObject?>? loader)
    {
        if (!string.IsNullOrEmpty(viewKey) && loader != null && loader(viewKey) != null)
            return Color.white; // 资源自带颜色，Host 负责实例化
        return ElementPalette.ColorOf(elementId);
    }
}
```
（Host 侧若加载资源则使用资源；否则用回退颜色；本任务只锁定判定顺序。）

- [ ] **步骤 3：运行测试**

`ElementVfxFallbackTests`、`SwordQiElementTintTests`、`StatusVFXSystemTests`；batchmode 超时/杀进程规则同上。

- [ ] **步骤 4：文档与范围修正**

- 路线图 Plan 05 ✅（写明服务端 654+、PlayMode 三客户端、带宽、注入、AOI 基准）。
- 设计规格 Plan 05 ✅。
- **修正 Plan 04 文档中“离线共享运行时属 Plan 05”**：改为“Plan 06/backlog；Plan 05 不新增机制”。在路线图新增 Plan 06 登记项：离线单机四元素共享运行时、Addressables 美术资源签收、Plan 04 final review 的 M2/M4/M6–M11/M16–M19。
- Server README 写验收数字与已知失败（Bot_Radius PPtr、MonsterWorld 导航模式导航债）。

- [ ] **步骤 5：全量回归**

```bash
bash tools/test-server-inprocess.sh
bash tools/unity-tests.sh EditMode AoiReverseIndexTests  # server-only; skip if not Unity
bash tools/unity-tests.sh EditMode NetworkConsumptionInjectionTests
bash tools/unity-tests.sh EditMode ElementVfxFallbackTests
bash tools/unity-tests.sh PlayMode ThreeClientElementalAcceptanceTests
bash tools/unity-tests.sh PlayMode PlayerControlInertiaTests
bash tools/unity-tests.sh PlayMode MovementSyncTests
bash tools/unity-tests.sh PlayMode ThreeClientCombatConsistencyTests
```
服务端/Unity 目标类全绿；已知 PPtr/导航债登记。三客户端测试需先起服务端（`dotnet Server/bin/Debug/net8.0/Server3C.dll 8000`）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/ElementVfxFallback.cs \
  Assets/Scripts/Tests/Editor/VFX/ElementVfxFallbackTests.cs \
  docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md \
  docs/superpowers/specs/2026-11-14-elemental-sword-qi-design.md \
  Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md Server/README.md
git commit -m "docs: close plan 05 acceptance and move offline to plan 06"
```

---

## 自检

- 覆盖：AOI、三客户端、带宽、注入、麻痹/推挤、表现回退、文档；无新机制。
- 验收均为数字/集合断言；带宽按实际编码字节；三客户端走真实服务器。
- 离线共享运行时/美术资源明确移到 Plan 06，不再写成 Plan 05 已交付。
- 协议不变；预算常量不上调；测试不依赖未提交的 TestResults。
