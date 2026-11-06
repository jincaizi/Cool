# 火球术移动端地面瞄准 实现计划

> **实现偏差：** Task 9 最终锁存逻辑（`ShouldKeepLastAim` / `ShouldReResolveDefaultAim`）见代码与 SDD 账本；本计划的该片段为初始设计。

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把火球术（20003）改为按住瞄准、拖动控方向与距离、可拉远取消、精力耗尽自动取消的地面落点技能；客户端与服务端判定一致，表现使用占位资源。

**架构：** UI 摇杆只产出瞄准状态；`MobileInputBinder → InputManager → Sys3CEntry` 负责世界落点、角色转向与地面圈；`SkillCoordinator/SkillExecutor` 增加「刷新落点 / 取消蓄力 / 报告落点」三个入口；服务端 20003 从线形判定改为落点圆判定，并按飞行距离延迟结算。

**技术栈：** Unity 2022.3.25f1、C#、UGUI、NUnit（Unity Test Framework）、xUnit（服务端）、KCP + Protobuf。

**规格：** `docs/superpowers/specs/2026-11-07-fireball-ground-aim-design.md`

---

## 文件结构

### 创建

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/AimGestureState.cs` | 瞄准摇杆纯逻辑（按下点、死区、取消回滞、释放类型） |
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillAimJoystick.cs` | 火球按钮上的 UGUI 摇杆组件 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/SkillAimMath.cs` | 相机相对方向、距离映射、射程钳制纯函数 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/GroundAimIndicator.cs` | 地面落点圈组件（占位） |
| `Assets/PreRes/Skills/FireballAimIndicator.prefab` | 占位地面圈预制体 |
| `Assets/Scripts/Tests/Editor/AimGestureStateTests.cs` | 摇杆纯逻辑测试 |
| `Assets/Scripts/Tests/Editor/SkillAimMathTests.cs` | 瞄准数学测试 |
| `Assets/Scripts/Tests/Editor/SkillInterruptionMatrixCancelTests.cs` | `PlayerCancel` 矩阵测试 |
| `Assets/Scripts/Tests/Editor/FireballAssetTableTests.cs` | 火球资产与服务端表一致性 |
| `Assets/Scripts/Tests/Editor/GroundAimIndicatorTests.cs` | 占位 prefab 与颜色测试 |

### 修改

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 20003 表项、`ProjectileSpec.Speed`、`ProjectileTravelTicks` |
| `Server/Handler/CastHandler.cs` | 首个判定 tick 加飞行延迟 |
| `Server/Server.Tests/Handler/CombatResolverProjectileTests.cs` | 20003 新判定测试 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs` | `HoldToAim` / `MinAimDistance` / `AimIndicatorPrefab` |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs` | `InterruptionSource.PlayerCancel` |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs` | 放行 `PlayerCancel` |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs` | 不自动释放、放行 `PlayerCancel` |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs` | 刷新落点、取消蓄力、移动锁、精力取消 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs` | `ReportedImpactPoint`、按落点生成火球 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballSpawner.cs` | `targetPoint` 参数与近距保护 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs` | 目标点弹道、落地判定、池复位 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/SkillCastReporter.cs` | `HoldToAim` 上报落点 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs` | 远程火球飞向 `Impact` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs` | `TryGetLockedPosition` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 注入/清除选中目标位置提供者 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs` | 暴露 `CurrentTarget` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs` | aim/cancel/release 状态 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs` | 转发瞄准状态、teardown 取消 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 世界落点、转向、移动锁、取消、指示器 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillBarPresenter.cs` | 隐藏蓄力环、复位手势、可交互 |
| `Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset` | 火球资产新字段 |
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` | 挂载摇杆组件与场景接线 |
| `Assets/Scripts/Tests/Editor/SkillStateMachineChargeTests.cs` | `HoldToAim` 状态机测试 |
| `Assets/Scripts/Tests/Editor/SkillCoordinatorChargeTests.cs` | 落点/取消测试 |
| `Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs` | 精力取消测试 |
| `Assets/Scripts/Tests/Editor/SkillExecutorFireballTests.cs` | 落点方向/上报兜底测试 |
| `Assets/Scripts/Tests/Editor/FireballProjectileTests.cs` | 变距/上坡/池复位测试 |
| `Assets/Scripts/Tests/Editor/FireballSpawnerTests.cs` | 近距保护测试 |
| `Assets/Scripts/Tests/Editor/MobileInputManagerUiHooksTests.cs` | 瞄准状态透传测试 |
| `Assets/Scripts/Tests/Editor/MobileSkillInputBridgeTests.cs` | binder 转发/teardown 测试 |
| `Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs` | 锁定位置测试 |
| `Assets/Scripts/Tests/Editor/SyncLayer/RemoteCastPresenterTests.cs` | Impact 爆炸点测试 |
| `Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs` | 拖动/取消/失焦/精力验收 |
| `Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs` | 场景接线/蓄力环隐藏 |

---

### 任务 1：服务端 20003 改落点圆 + 飞行延迟

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs:15-46, 124-137, 184-196`
- 修改：`Server/Handler/CastHandler.cs:125`
- 测试：`Server/Server.Tests/Handler/CombatResolverProjectileTests.cs`

- [ ] **步骤 1：编写失败的服务端测试**

把 `CombatResolverProjectileTests.cs` 中 `Fireball_Explodes_At_First_Target_And_Stops` 替换为落点圆语义，并追加 5 条：

```csharp
[Fact]
public void Fireball_Circle_At_Impact_Hits_Targets_In_Radius()
{
    // 落点 (0,0,4)；目标 2 在落点中心、4 距 0.5m（半径 1.5 内）、3 在 9m 外。
    var resolver = new CombatResolver(
        MakeStore(targetAt: (0f, 4f), secondAt: (0f, 9f), thirdAt: (0f, 4.5f)), new Random(1));
    var request = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 4f };
    Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

    var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
    Assert.Contains(2L, hitIds);
    Assert.Contains(4L, hitIds);
    Assert.DoesNotContain(3L, hitIds);
}

[Fact]
public void Fireball_Does_Not_Hit_Along_Path_Outside_Impact_Circle()
{
    // 目标在 4m，落点在 10m：4m 处不在 1.5m 圆内，线形旧行为不得残留。
    var resolver = new CombatResolver(MakeStore(targetAt: (0f, 4f)), new Random(1));
    var request = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f };
    Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

    var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
    Assert.DoesNotContain(2L, hitIds);
}

[Fact]
public void Fireball_Impact_Too_Far_Is_Rejected()
{
    var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
    var far = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 15.5f };
    var decision = resolver.Decide(1, far, tick: 0);
    Assert.False(decision.Accepted);
    Assert.Equal("impact-too-far", decision.RejectReason);
}

[Fact]
public void Fireball_Travel_Delay_Defers_First_Hit()
{
    // 10m / 18 m/s × 15Hz = 8.33 → ceil = 9；tick 8 不得结算，tick 9 结算。
    var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
    var request = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f };
    Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

    Assert.Empty(resolver.DrainResolved(8).SelectMany(r => r.Hits));
    Assert.NotEmpty(resolver.DrainResolved(9).SelectMany(r => r.Hits));
}

[Fact]
public void Fireball_Charge_Progress_Does_Not_Scale_Damage()
{
    var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
    var request = new CastRequest
    {
        SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f, ChargeProgress = 1f
    };
    Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

    var hit = Drain(resolver, 40).SelectMany(r => r.Hits).First(h => h.TargetPlayerId == 2L);
    Assert.Equal(20f, hit.Damage, 0.001f);
}

[Fact]
public void Projectile_Travel_Ticks_Clamps_NonFinite_And_Zero_Distance()
{
    Assert.True(CombatRules.TryGet(20003, out var entry));
    Assert.Equal(9u, CombatRules.ProjectileTravelTicks(entry.Projectile, 0f, 0f, 0f, 10f));
    Assert.Equal(0u, CombatRules.ProjectileTravelTicks(entry.Projectile, 0f, 0f, float.NaN, 0f));
    Assert.Equal(0u, CombatRules.ProjectileTravelTicks(entry.Projectile, 0f, 0f, 0f, 0f));
    Assert.Equal(0u, CombatRules.ProjectileTravelTicks(null, 0f, 0f, 0f, 10f));
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~CombatResolverProjectileTests"`

预期：编译失败（`ProjectileTravelTicks` 不存在）或新用例失败（20003 仍是线形、无落点校验）。

若本机 testhost 抛 `Win32Exception(5)`，改用：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CombatResolverProjectileTests"`。

- [ ] **步骤 3：实现 `ProjectileSpec.Speed` 与 `ProjectileTravelTicks`**

在 `CombatRules.cs` 的 `ProjectileSpec` 中加字段/构造参数（保持现有命名参数调用不变）：

```csharp
public float Speed { get; }

public ProjectileSpec(ProjectileKind kind, float length = 0f, float halfWidth = 0f, float radius = 0f,
    int stages = 1, float totalFlightSeconds = 0f,
    float maxImpactDistance = 0f, bool chargeScales = false,
    float chargeAreaMaxScale = 1f, float chargeDamageMaxScale = 1f,
    bool keepsSwing = false, float speed = 0f)
{
    Kind = kind; Length = length; HalfWidth = halfWidth; Radius = radius;
    Stages = stages; TotalFlightSeconds = totalFlightSeconds;
    MaxImpactDistance = maxImpactDistance; ChargeScales = chargeScales;
    ChargeAreaMaxScale = chargeAreaMaxScale; ChargeDamageMaxScale = chargeDamageMaxScale;
    KeepsSwing = keepsSwing; Speed = speed;
}
```

在 `CombatRules` 中追加：

```csharp
/// <summary>CircleAtPoint 投射物的飞行 tick 数（距离 / 速度 × 15Hz，向上取整）。</summary>
public static uint ProjectileTravelTicks(ProjectileSpec? spec,
    float casterX, float casterZ, float impactX, float impactZ)
{
    if (spec == null || spec.Kind != ProjectileKind.CircleAtPoint || spec.Speed <= 0f) return 0u;
    if (!float.IsFinite(casterX) || !float.IsFinite(casterZ)
        || !float.IsFinite(impactX) || !float.IsFinite(impactZ)) return 0u;

    float dx = impactX - casterX;
    float dz = impactZ - casterZ;
    float distance = (float)Math.Sqrt(dx * dx + dz * dz);
    if (!(distance > 0f) || !float.IsFinite(distance)) return 0u;

    double ticks = Math.Ceiling(distance / spec.Speed * TickRateHz);
    return ticks >= uint.MaxValue ? uint.MaxValue : (uint)ticks;
}
```

把 20003 表项改为：

```csharp
[20003] = new SkillEntry(20003, shapeType: 0, maxRange: 0f, maxAreaRadius: 0f, maxAngle: 0f,
    cooldownSeconds: 3f, baseDamage: 20f, criticalRate: 0f, hitDelaySeconds: 0f,
    projectile: new ProjectileSpec(ProjectileKind.CircleAtPoint,
        radius: 1.5f, maxImpactDistance: 15f, speed: 18f)),
```

- [ ] **步骤 4：`CastHandler.Decide` 加入飞行延迟**

把 `Server/Handler/CastHandler.cs:125` 改为：

```csharp
var firstTick = tick + CombatRules.HitDelayTicks(entry)
    + CombatRules.ProjectileTravelTicks(entry.Projectile,
        caster.Position.X, caster.Position.Z, request.ImpactX, request.ImpactZ);
```

- [ ] **步骤 5：运行测试确认通过**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~CombatResolverProjectileTests"`

预期：全部 PASS（含旧的 `ChargeBlast_*`、`Meteor_*`、`Impact_*` 用例）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs Server/Handler/CastHandler.cs Server/Server.Tests/Handler/CombatResolverProjectileTests.cs
git commit -m "feat(server): judge fireball as circle at impact with travel delay"
```

---

### 任务 2：技能数据、状态机与火球资产

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs`
- 修改：`Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset`
- 测试：`Assets/Scripts/Tests/Editor/ProjectileSkillDataTests.cs`、`Assets/Scripts/Tests/Editor/SkillStateMachineChargeTests.cs`、`Assets/Scripts/Tests/Editor/SkillInterruptionMatrixCancelTests.cs`

- [ ] **步骤 1：编写失败测试**

在 `ProjectileSkillDataTests.cs` 追加：

```csharp
[Test]
public void test_projectile_skill_data_ground_aim_defaults()
{
    // Arrange
    var data = ScriptableObject.CreateInstance<ProjectileSkillData>();

    // Assert
    Assert.IsFalse(data.HoldToAim, "默认不是按住瞄准技能");
    Assert.AreEqual(2f, data.MinAimDistance, 0.001f);
    Assert.IsNull(data.AimIndicatorPrefab);
}
```

在 `SkillStateMachineChargeTests.cs` 追加（并把 `CreateProjectileData` 增加可选 `holdToAim`）：

```csharp
private static ProjectileSkillData CreateProjectileData(float maxCharge, bool holdToAim = false)
{
    var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
    TestHelpers.SetField(data, "_maxChargeTime", maxCharge);
    TestHelpers.SetField(data, "_holdToAim", holdToAim);
    return data;
}

[Test]
public void test_hold_to_aim_projectile_does_not_auto_release_at_max()
{
    var machine = new FakeTimeMachine(CreateProjectileData(2f, holdToAim: true));
    AdvanceToCharging(machine);
    machine.Now = 2.15f;
    machine.Update(0.01f);

    Assert.AreEqual(SkillSubState.Charging, machine.CurrentState, "按住瞄准不得在满蓄时自动释放");
    Assert.IsTrue(machine.Release());
    Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
}

[Test]
public void test_player_cancel_interrupts_charging()
{
    var machine = new FakeTimeMachine(CreateProjectileData(2f, holdToAim: true));
    AdvanceToCharging(machine);

    Assert.IsTrue(machine.Interrupt(InterruptionSource.PlayerCancel));
    Assert.AreEqual(SkillSubState.Cancelled, machine.CurrentState);
}

[Test]
public void test_player_cancel_interrupts_casting()
{
    var machine = new FakeTimeMachine(CreateProjectileData(2f, holdToAim: true));
    machine.TryStart();
    Assert.AreEqual(SkillSubState.Casting, machine.CurrentState);

    Assert.IsTrue(machine.Interrupt(InterruptionSource.PlayerCancel));
    Assert.AreEqual(SkillSubState.Cancelled, machine.CurrentState);
}
```

新建 `SkillInterruptionMatrixCancelTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillInterruptionMatrixCancelTests
    {
        [Test]
        public void test_player_cancel_allowed_in_casting_and_charging_only()
        {
            // Arrange
            var matrix = new SkillInterruptionMatrix();
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            TestHelpers.SetField(data, "_skillType", SkillType.Projectile);

            // Assert
            Assert.IsTrue(matrix.CanBeInterruptedInState(data, SkillSubState.Casting, InterruptionSource.PlayerCancel));
            Assert.IsTrue(matrix.CanBeInterruptedInState(data, SkillSubState.Charging, InterruptionSource.PlayerCancel));
            Assert.IsFalse(matrix.CanBeInterruptedInState(data, SkillSubState.Execution, InterruptionSource.PlayerCancel));
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode ProjectileSkillDataTests`
运行：`bash tools/unity-tests.sh EditMode SkillStateMachineChargeTests`
运行：`bash tools/unity-tests.sh EditMode SkillInterruptionMatrixCancelTests`

预期：编译失败（`HoldToAim`、`PlayerCancel` 不存在）。若编译失败导致脚本无法过滤，先修编译。

- [ ] **步骤 3：实现数据与状态机**

`ProjectileSkillData.cs` 在 `_projectileGravity` 后追加：

```csharp
[Header("=== Ground Aim ===")]
[Tooltip("按住瞄准：不自动释放；精力耗尽取消；释放时上报落点")]
[SerializeField] private bool _holdToAim;
public bool HoldToAim => _holdToAim;

[Tooltip("最小落点距离(米)")]
[SerializeField] private float _minAimDistance = 2f;
public float MinAimDistance => _minAimDistance;

[Tooltip("地面落点圈占位预制体（仅本地瞄准表现）")]
[SerializeField] private GameObject _aimIndicatorPrefab;
public GameObject AimIndicatorPrefab => _aimIndicatorPrefab;
```

`SkillState.cs` 的 `InterruptionSource` 末尾追加：

```csharp
PlayerCancel        // 玩家主动取消（拖远/teardown）
```

`SkillInterruptionMatrix.CanBeInterrupted` 在 `StaminaExhausted` 放行旁追加：

```csharp
if (source == InterruptionSource.PlayerCancel) return true;
```

`SkillStateMachine`：
- 新增字段：

```csharp
private readonly bool _holdToAim;
```

- 构造函数里 `_projectileData` 赋值后：

```csharp
_holdToAim = _projectileData != null && _projectileData.HoldToAim;
```

- `UpdateCharging` 的自动释放改为：

```csharp
if (!_holdToAim && chargeTime >= MaxChargeTime)
{
    ChargeProgressAtRelease = 1f;
    TransitionTo(SkillSubState.Execution);
}
```

- `CanBeInterrupted` 的 switch 追加：

```csharp
InterruptionSource.PlayerCancel => true,
```

- [ ] **步骤 4：更新火球资产**

在 `Fireball_SkillQ.asset` 的 `_maxChargeTime` 后追加/修改：

```yaml
  _canMoveWhileCharging: 0
  _holdToAim: 1
  _minAimDistance: 2
  _aimIndicatorPrefab: {fileID: 0}
```

`_canMoveWhileCharging` 原有 `1` 改为 `0`；`_aimIndicatorPrefab` 在任务 10 再赋值。

- [ ] **步骤 5：运行测试确认通过**

运行：

```bash
bash tools/unity-tests.sh EditMode ProjectileSkillDataTests
bash tools/unity-tests.sh EditMode SkillStateMachineChargeTests
bash tools/unity-tests.sh EditMode SkillInterruptionMatrixCancelTests
```

预期：全部 PASS；旧的 `test_chargeable_projectile_auto_release_at_max` 仍用 `holdToAim=false`，保持通过。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/ProjectileSkillData.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillState.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillInterruptionMatrix.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs \
  Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset \
  Assets/Scripts/Tests/Editor/ProjectileSkillDataTests.cs \
  Assets/Scripts/Tests/Editor/SkillStateMachineChargeTests.cs \
  Assets/Scripts/Tests/Editor/SkillInterruptionMatrixCancelTests.cs
git commit -m "feat(skills): add hold-to-aim data and player cancel interruption"
```

---

### 任务 3：协调器与执行器（落点、取消、精力取消、上报兜底）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`
- 测试：`Assets/Scripts/Tests/Editor/SkillCoordinatorChargeTests.cs`、`Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs`、`Assets/Scripts/Tests/Editor/SkillExecutorFireballTests.cs`

- [ ] **步骤 1：编写失败测试**

`SkillCoordinatorChargeTests.cs`：把 `CreateChargeableProjectileData` 改为可配置：

```csharp
private ProjectileSkillData CreateChargeableProjectileData(bool holdToAim = false)
{
    var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
    TestHelpers.SetField(data, "_skillId", 20003);
    TestHelpers.SetField(data, "_maxChargeTime", 2f);
    TestHelpers.SetField(data, "_projectileRange", 15f);
    TestHelpers.SetField(data, "_skillType", SkillType.Projectile);
    TestHelpers.SetField(data, "_holdToAim", holdToAim);
    return data;
}
```

追加三条：

```csharp
[Test]
public void test_hold_to_aim_release_sends_cast_request_with_impact()
{
    var data = CreateChargeableProjectileData(holdToAim: true);
    var (coordinator, client, _) = CreateCoordinatorWithBridge(data);
    coordinator.UpdateAimTarget(new Vector3(0f, 0f, 10f));

    Assert.IsTrue(coordinator.TryReleaseCharge(20003), "火球松手应释放");

    Assert.AreEqual(1, client.Sent.Count);
    var cast = (CastRequest)client.Sent[0].msg;
    Assert.AreEqual(10f, cast.ImpactZ, 0.001f, "按住瞄准必须上报落点");
}

[Test]
public void test_hold_to_aim_cancel_sends_no_cast_request_and_starts_no_cooldown()
{
    var data = CreateChargeableProjectileData(holdToAim: true);
    var (coordinator, client, _) = CreateCoordinatorWithBridge(data);

    Assert.IsTrue(coordinator.CancelCharge(20003));
    Assert.AreEqual(SkillSubState.Cancelled, coordinator.CurrentSubState);
    Assert.AreEqual(0, client.Sent.Count, "取消不得上报");
    Assert.IsFalse(coordinator.GetCooldownInfo(20003).IsOnCooldown, "取消不得起冷却");
}

[Test]
public void test_hold_to_aim_impact_falls_back_to_forward_range()
{
    var data = CreateChargeableProjectileData(holdToAim: true);
    var (coordinator, client, _) = CreateCoordinatorWithBridge(data);
    // 不调用 UpdateAimTarget；施法者在原点、朝向 +Z，兜底 = (0,0,15)

    Assert.IsTrue(coordinator.TryReleaseCharge(20003));

    var cast = (CastRequest)client.Sent[0].msg;
    Assert.AreEqual(15f, cast.ImpactZ, 0.001f, "未设置落点时必须兜底为前方最大射程");
}
```

`SkillCoordinatorStaminaTests.cs` 追加：

```csharp
private ProjectileSkillData CreateHoldToAimProjectileData()
{
    var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
    TestHelpers.SetField(data, "_skillId", 20003);
    TestHelpers.SetField(data, "_maxChargeTime", 2f);
    TestHelpers.SetField(data, "_projectileRange", 15f);
    TestHelpers.SetField(data, "_staminaDrainPerSecond", 15f);
    TestHelpers.SetField(data, "_skillType", SkillType.Projectile);
    TestHelpers.SetField(data, "_holdToAim", true);
    return data;
}

[Test]
public void test_hold_to_aim_stamina_exhausted_cancels_without_release()
{
    var (coordinator, stamina, executor, _) = CreateWithExecutor(CreateHoldToAimProjectileData());
    // 先进 Charging
    var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
    machine.Now = 0.15f;
    executor.Update(0.01f);
    Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);

    stamina.TryConsume(99f); // 剩 1
    int released = 0;
    coordinator.OnChargeReleased += (_, _, _) => released++;

    coordinator.Update(0.1f);

    Assert.AreEqual(SkillSubState.Cancelled, executor.CurrentSubState);
    Assert.AreEqual(0, released, "精力耗尽必须取消，不得自动释放");
}
```

`SkillExecutorFireballTests.cs` 追加：

```csharp
[Test]
public void test_skill_executor_hold_to_aim_uses_target_direction()
{
    // Arrange
    var prefab = NewGo("FireballPrefab");
    prefab.AddComponent<FireballProjectile>();
    var data = CreateFireballData(prefab);
    TestHelpers.SetField(data, "_holdToAim", true);
    var executor = CreateExecutor(data);
    // 发射点在 (0, 1.2, 0.5)（SpawnOffset），因此目标 z 取 0.5 才得到纯 +X 方向
    executor.SetTargetPosition(new Vector3(10f, 0f, 0.5f));

    // Act
    Assert.IsTrue(executor.TryStart());

    // Assert
    Assert.AreEqual(1f, _lastDirection.x, 0.001f, "落点在 +X 时火球应朝 +X 发射");
    Assert.AreEqual(0f, _lastDirection.z, 0.001f);
}

[Test]
public void test_skill_executor_reported_impact_falls_back_to_forward_range()
{
    var prefab = NewGo("FireballPrefab");
    prefab.AddComponent<FireballProjectile>();
    var data = CreateFireballData(prefab);
    TestHelpers.SetField(data, "_holdToAim", true);
    var executor = CreateExecutor(data);

    Assert.AreEqual(new Vector3(0f, 0f, 15f), executor.ReportedImpactPoint);
}

[Test]
public void test_skill_executor_reported_impact_uses_last_target()
{
    var prefab = NewGo("FireballPrefab");
    prefab.AddComponent<FireballProjectile>();
    var data = CreateFireballData(prefab);
    TestHelpers.SetField(data, "_holdToAim", true);
    var executor = CreateExecutor(data);
    executor.SetTargetPosition(new Vector3(3f, 0f, 4f));

    Assert.AreEqual(new Vector3(3f, 0f, 4f), executor.ReportedImpactPoint);
}
```

注意：`EventBus.Subscribe` 用 lambda 无法退订；在该测试类里新增字段方法并在 `SetUp`/`TearDown` 订阅/退订，参照现有 `OnFireballSpawned` 模式：

```csharp
private Vector3 _lastDirection;
private void OnDirection(FireballSpawnedEvent e) => _lastDirection = e.Direction;
// SetUp 追加：EventBus.Subscribe<FireballSpawnedEvent>(OnDirection);
// TearDown 追加：EventBus.Unsubscribe<FireballSpawnedEvent>(OnDirection);
```

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/unity-tests.sh EditMode SkillCoordinatorChargeTests
bash tools/unity-tests.sh EditMode SkillCoordinatorStaminaTests
bash tools/unity-tests.sh EditMode SkillExecutorFireballTests
```

预期：编译失败（`UpdateAimTarget`、`CancelCharge`、`ReportedImpactPoint` 不存在）。

- [ ] **步骤 3：实现 `SkillCoordinator`**

在 `TryReleaseCharge` 附近新增：

```csharp
/// <summary>按住瞄准期间刷新落点（仅 HoldToAim 投射物）。</summary>
public void UpdateAimTarget(Vector3 point)
{
    if (_currentSkill == null || !_currentSkill.IsActive) return;
    if (!(_currentSkill.Data is ProjectileSkillData p) || !p.HoldToAim) return;
    _currentSkill.SetTargetPosition(point);
}

/// <summary>玩家主动取消蓄力：不发射、不冷却、不上报。</summary>
public bool CancelCharge(int skillId)
{
    if (_currentSkill == null || !_currentSkill.IsActive || _currentSkill.SkillId != skillId)
        return false;
    var sub = _currentSkill.CurrentSubState;
    if (sub != SkillSubState.Casting && sub != SkillSubState.Charging) return false;
    if (!_currentSkill.TryInterrupt(InterruptionSource.PlayerCancel)) return false;
    // TryInterrupt 在矩阵放行但状态机拒绝时仍返回 true——按真实状态判定。
    return _currentSkill.CurrentSubState == SkillSubState.Cancelled;
}
```

`CanMove` 的 `Casting` 与 `Charging` 分支改为：

```csharp
SkillSubState.Casting => !(_currentSkill.Data is ProjectileSkillData castingProjectile
                           && castingProjectile.HoldToAim),
...
SkillSubState.Charging => _currentSkill.Data switch
{
    ChargedSkillData c => c.CanMoveWhileCharging,
    ProjectileSkillData p => !p.HoldToAim && p.CanMoveWhileCharging,
    _ => false
},
```

`HandleStaminaDepleted` 改为：

```csharp
bool holdToAim = skill.Data is ProjectileSkillData projectile && projectile.HoldToAim;
bool resolved = sub switch
{
    SkillSubState.Charging => holdToAim
        ? skill.TryInterrupt(InterruptionSource.StaminaExhausted)
        : TryReleaseCharge(skill.SkillId),
    SkillSubState.Channeling or SkillSubState.Spinning
        => skill.TryInterrupt(InterruptionSource.StaminaExhausted),
    _ => false
};

bool applied = sub == SkillSubState.Charging
    ? (holdToAim ? skill.CurrentSubState == SkillSubState.Cancelled : resolved)
    : skill.CurrentSubState == SkillSubState.Cancelled;
if (!applied) skill.ForceComplete();
```

`ReportChargeReleased` 的落点改为：

```csharp
OnChargeReleased?.Invoke(_currentSkill.Data, progress, _currentSkill.ReportedImpactPoint);
```

- [ ] **步骤 4：实现 `SkillExecutor`**

新增字段：

```csharp
private bool _hasTargetPosition;
```

`SetTargetPosition` 改为：

```csharp
public void SetTargetPosition(Vector3 position)
{
    _targetPosition = position;
    _hasTargetPosition = true;
}
```

新增属性：

```csharp
/// <summary>蓄力释放上报的落点。HoldToAim 未设置落点时兜底为前方最大射程，
/// 绝不能回退到 owner 位置（否则服务端会把落点算在施法者脚下）。</summary>
public Vector3 ReportedImpactPoint
{
    get
    {
        if (_projectileData != null && _projectileData.HoldToAim)
        {
            if (_hasTargetPosition) return _targetPosition;
            Vector3 forward = _owner.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            return _owner.transform.position + forward.normalized * _projectileData.ProjectileRange;
        }
        return TargetPositionOrOwner;
    }
}
```

`SpawnFireball` 的 origin/dir 段改为：

```csharp
Vector3 origin = _owner.transform.position
                 + _owner.transform.TransformDirection(_projectileData.SpawnOffset);
bool useTarget = _projectileData.HoldToAim && _hasTargetPosition;
Vector3 dir;
Vector3? spawnTarget = null;
if (useTarget)
{
    Vector3 flat = _targetPosition - origin;
    flat.y = 0f;
    dir = flat.sqrMagnitude > 0.0001f ? flat.normalized : _owner.transform.forward;
    spawnTarget = _targetPosition;
}
else
{
    dir = _owner.transform.forward;
    dir.y = 0f;
    dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _owner.transform.forward;
}

FireballSpawner.Spawn(_projectileData, damage, origin, dir, spawnTarget);
```

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode SkillCoordinatorChargeTests
bash tools/unity-tests.sh EditMode SkillCoordinatorStaminaTests
bash tools/unity-tests.sh EditMode SkillExecutorFireballTests
```

预期：全部 PASS；旧的 `test_skill_charged_at_zero_stamina_auto_releases_and_reports_cast` 仍通过。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs \
  Assets/Scripts/Tests/Editor/SkillCoordinatorChargeTests.cs \
  Assets/Scripts/Tests/Editor/SkillCoordinatorStaminaTests.cs \
  Assets/Scripts/Tests/Editor/SkillExecutorFireballTests.cs
git commit -m "feat(skills): aim target, cancel and stamina-exhaustion cancel for hold-to-aim"
```

---

### 任务 4：投射物目标点弹道与池复位

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballSpawner.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs`
- 测试：`Assets/Scripts/Tests/Editor/FireballProjectileTests.cs`、`Assets/Scripts/Tests/Editor/FireballSpawnerTests.cs`

- [ ] **步骤 1：编写失败测试**

`FireballProjectileTests.cs` 的 `CreateProjectile` 增加可选 `targetPoint`，并追加：

```csharp
private FireballProjectile CreateProjectile(ProjectileSkillData data, Vector3 origin, Vector3 dir,
    Vector3? targetPoint = null)
{
    var damage = DamageBlock.CreateDefault(30f, 0f);
    damage.CalculatedDamage = 30f;
    var go = NewGo("Fireball").AddComponent<FireballProjectile>();
    go.OnSpawn(data, damage, origin, dir, null, targetPoint);
    return go;
}

[Test]
public void test_fireball_target_point_lands_at_variable_distance()
{
    // Arrange（8m 落点）
    var fb = CreateProjectile(CreateData(18f, 15f), new Vector3(0f, 1.2f, 0f), Vector3.forward,
        new Vector3(0f, 0f, 8f));

    // Act
    for (int i = 0; i < 200 && fb.gameObject.activeSelf; i++) fb.Tick(0.005f);

    // Assert
    Assert.IsFalse(fb.gameObject.activeSelf);
    Assert.AreEqual(8f, fb.transform.position.z, 0.3f);
    Assert.LessOrEqual(fb.transform.position.y, 0.01f);
}

[Test]
public void test_fireball_hold_to_aim_does_not_explode_on_midflight_target()
{
    // Arrange（目标在 6m 地面，落点在 12m；途中不得提前爆）
    var data = CreateData(18f, 15f);
    TestHelpers.SetField(data, "_holdToAim", true);
    var mid = new FakeDamageable(NewGo("Mid").transform);
    mid.Transform.position = new Vector3(0f, 0f, 6f);
    Register(mid);
    var fb = CreateProjectile(data, new Vector3(0f, 1.2f, 0f), Vector3.forward,
        new Vector3(0f, 0f, 12f));

    // Act
    for (int i = 0; i < 300 && fb.gameObject.activeSelf; i++) fb.Tick(0.005f);

    // Assert
    Assert.AreEqual(0, mid.HitCount, "按住瞄准不得撞怪提前爆");
    Assert.IsFalse(fb.gameObject.activeSelf, "应在落点爆炸");
}

[Test]
public void test_fireball_uphill_target_does_not_explode_at_spawn()
{
    // Arrange（落点高于胸口：目标地面 y=3 > 发射 y=1.2）
    var data = CreateData(18f, 15f);
    TestHelpers.SetField(data, "_holdToAim", true);
    var fb = CreateProjectile(data, new Vector3(0f, 1.2f, 0f), Vector3.forward,
        new Vector3(0f, 3f, 10f));

    // Act
    fb.Tick(0.005f);

    // Assert
    Assert.IsTrue(fb.gameObject.activeSelf, "上坡落点不得在出生点立即爆炸");

    for (int i = 0; i < 400 && fb.gameObject.activeSelf; i++) fb.Tick(0.005f);
    Assert.IsFalse(fb.gameObject.activeSelf);
    Assert.AreEqual(10f, fb.transform.position.z, 0.4f);
}

[Test]
public void test_fireball_target_below_launch_ground_lands()
{
    // Arrange（下坡落点 y=-2）
    var fb = CreateProjectile(CreateData(18f, 15f), new Vector3(0f, 1.2f, 0f), Vector3.forward,
        new Vector3(0f, -2f, 10f));

    // Act
    for (int i = 0; i < 400 && fb.gameObject.activeSelf; i++) fb.Tick(0.005f);

    // Assert
    Assert.IsFalse(fb.gameObject.activeSelf);
    Assert.AreEqual(-2f, fb.transform.position.y, 0.3f);
}
```

`FireballSpawnerTests.cs` 追加：

```csharp
[Test]
public void test_fireball_spawner_skips_too_close_target()
{
    var prefab = NewGo("FireballPrefab");
    prefab.AddComponent<FireballProjectile>();
    var data = CreateData(prefab);

    var before = Object.FindObjectsOfType<FireballProjectile>().Length;
    var result = FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f),
        Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 0.05f));

    Assert.IsNull(result, "目标 < 0.1m 不生成投射物");
    Assert.AreEqual(before, Object.FindObjectsOfType<FireballProjectile>().Length,
        "只应有场景里的模板，不生成新实例");
}
```

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/unity-tests.sh EditMode FireballProjectileTests
bash tools/unity-tests.sh EditMode FireballSpawnerTests
```

预期：编译失败（`OnSpawn`/`Spawn` 无第 6/5 个参数）。

- [ ] **步骤 3：实现 `FireballSpawner`**

签名与近距保护：

```csharp
public static FireballProjectile Spawn(ProjectileSkillData data, DamageBlock damage,
    Vector3 origin, Vector3 direction, Vector3? targetPoint = null)
{
    if (data == null || data.ProjectilePrefab == null)
    {
        Debug.LogWarning("[FireballSpawner] data or prefab is null, skip spawn");
        return null;
    }

    if (targetPoint.HasValue)
    {
        Vector3 flat = targetPoint.Value - origin;
        flat.y = 0f;
        if (flat.magnitude < 0.1f) return null;
    }

    // ... 原池逻辑不变 ...
    var fb = _pool.Get();
    fb.OnSpawn(data, damage, origin, direction, _pool, targetPoint);
    return fb;
}
```

- [ ] **步骤 4：实现 `FireballProjectile`**

字段：

```csharp
private bool _hasTarget;
private Vector3 _targetPoint;
```

`OnSpawn` 签名与弹道：

```csharp
public void OnSpawn(ProjectileSkillData data, DamageBlock damage, Vector3 origin, Vector3 direction,
                    ComponentPool<FireballProjectile> pool, Vector3? targetPoint = null)
{
    _data = data;
    _damage = damage;
    _direction = direction;
    _pool = pool;
    _released = false;
    _flightTime = 0f;
    _hasTarget = targetPoint.HasValue;
    _targetPoint = targetPoint ?? default;
    transform.position = origin;
    transform.rotation = Quaternion.LookRotation(direction);

    float t;
    if (_hasTarget)
    {
        Vector3 flat = _targetPoint - origin;
        flat.y = 0f;
        float distance = flat.magnitude;
        _direction = distance > 0.0001f ? flat / distance : direction;
        transform.rotation = Quaternion.LookRotation(_direction);
        _groundY = _targetPoint.y;
        t = Mathf.Max(0.1f, distance / Mathf.Max(0.01f, data.ProjectileSpeed));
        float dy = _targetPoint.y - origin.y;
        float vy0 = dy / t + 0.5f * data.ProjectileGravity * t;
        _velocity = _direction * data.ProjectileSpeed + Vector3.up * vy0;
    }
    else
    {
        t = data.ProjectileSpeed > 0.01f
            ? data.ProjectileRange / data.ProjectileSpeed
            : 1f;
        _groundY = origin.y - data.SpawnOffset.y;
        float dy = _groundY - origin.y;
        float vy0 = dy / t + 0.5f * data.ProjectileGravity * t;
        _velocity = direction * data.ProjectileSpeed + Vector3.up * vy0;
    }

    gameObject.SetActive(true);
}
```

`Tick` 改为：

```csharp
public void Tick(float deltaTime)
{
    if (_data == null || _released) return;

    _velocity.y -= _data.ProjectileGravity * deltaTime;
    transform.position += _velocity * deltaTime;
    _flightTime += deltaTime;

    if (!_hasTarget) DetectAndExplode();
    if (_released) return;

    bool reachedTarget = _hasTarget
        && Vector3.Dot(transform.position - _targetPoint, _direction) >= 0f;
    bool landed = _hasTarget
        ? reachedTarget
          || (_velocity.y < 0f && _flightTime >= 0.05f && transform.position.y <= _groundY)
        : transform.position.y <= _groundY;

    if (landed)
    {
        Explode(null, transform.position);
        return;
    }

    if (_flightTime > MaxFlightTime)
        Explode(null, transform.position);
}
```

`_hasTarget` 必须在 `OnSpawn` 里复位；`Release`/`OnDestroy` 不用额外处理。

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode FireballProjectileTests
bash tools/unity-tests.sh EditMode FireballSpawnerTests
```

预期：全部 PASS；旧的火球用例（固定射程、首个目标、落地）仍通过。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballSpawner.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs \
  Assets/Scripts/Tests/Editor/FireballProjectileTests.cs \
  Assets/Scripts/Tests/Editor/FireballSpawnerTests.cs
git commit -m "feat(projectile): fly fireball to an explicit target point"
```

---

### 任务 5：网络上报与远程表现

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/SkillCastReporter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteCastPresenterTests.cs`（并复用任务 3 的 `SkillCoordinatorChargeTests`）

- [ ] **步骤 1：编写失败测试**

`RemoteCastPresenterTests.cs` 追加：

```csharp
[Test]
public void Fireball_With_Impact_Explodes_At_Tracked_Impact_Not_First_Hit()
{
    var visual = new FakeVisual();
    var presenter = NewPresenter(localPlayerId: 1L, visual);
    presenter.TrackForTest(casterId: 2L, skillId: 20003, visual,
        impact: new Vector3(3f, 0f, 4f));

    presenter.ApplyDamageForTest(FireballResult(2L, hitX: 9f, hitZ: 9f));

    Assert.AreEqual(1, visual.Explosions);
    Assert.AreEqual(new Vector3(3f, 0f, 4f), visual.LastPoint);
}
```

并修改 `TrackForTest` 签名（见实现步骤）。

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/unity-tests.sh EditMode RemoteCastPresenterTests
```

预期：编译失败（`TrackForTest` 无 `impact` 参数）。

- [ ] **步骤 3：实现上报条件**

`SkillCastReporter.ReportChargeRelease` 改为：

```csharp
bool sendsImpact = data is ChargedSkillData
    || (data is ProjectileSkillData projectile && projectile.HoldToAim);
Vector3? impact = sendsImpact ? impactPoint : (Vector3?)null;
bridge.NotifySkillCast(data.SkillId, HitShapeConverter.FromSkillData(data), impact,
    chargeProgress, targetId);
```

- [ ] **步骤 4：实现远程表现**

`Tracked` 增加字段：

```csharp
public readonly bool HasImpact;
public readonly Vector3 Impact;

public Tracked(long casterId, int skillId, IRemoteProjectileVisual visual,
    bool hasImpact = false, Vector3 impact = default)
{
    CasterId = casterId;
    SkillId = skillId;
    Visual = visual;
    HasImpact = hasImpact;
    Impact = impact;
}
```

`TrackForTest` 改为：

```csharp
internal void TrackForTest(long casterId, int skillId, IRemoteProjectileVisual visual,
    Vector3? impact = null)
    => _tracked.Add(new Tracked(casterId, skillId, visual, impact.HasValue, impact ?? default));
```

`OnCastBroadcast` 中把 `var impact = ...` 移到创建视觉之前，并把视觉创建改为：

```csharp
Vector3 impact = new Vector3(broadcast.ImpactX, broadcast.ImpactY, broadcast.ImpactZ);
IRemoteProjectileVisual? visual;
bool hasImpact = data is ProjectileSkillData projectileData && projectileData.HoldToAim;
if (_projectileFactory != null)
    visual = _projectileFactory(data, origin, forward);
else
    visual = DefaultFactory(data, origin, forward, hasImpact ? impact : (Vector3?)null);
if (visual != null)
    _tracked.Add(new Tracked(broadcast.CasterPlayerId, broadcast.SkillId, visual,
        hasImpact, impact));
```

`DefaultFactory` 增加可选目标点：

```csharp
private static IRemoteProjectileVisual? DefaultFactory(SkillData data, Vector3 origin,
    Vector3 forward, Vector3? targetPoint = null)
{
    if (data.SwordQi != null)
    {
        var qi = SwordQiSpawner.Spawn(data.SwordQi, data.Damage?.Clone(), origin, forward);
        return qi != null ? new SwordQiVisual(qi) : null;
    }
    if (data is ProjectileSkillData projectile)
    {
        var fireball = FireballSpawner.Spawn(projectile, data.Damage?.Clone(), origin, forward,
            targetPoint);
        return fireball != null ? new FireballVisual(fireball) : null;
    }
    return null;
}
```

`OnDamageResult` 的火球分支改为：

```csharp
if (result.SkillId == (int)SkillID.Fireball
    && result.Hits != null && result.Hits.Count > 0)
{
    var hit = result.Hits[0];
    Vector3 point = tracked.HasImpact
        ? tracked.Impact
        : new Vector3(hit.HitX, 0f, hit.HitZ);
    tracked.Visual.ExplodeAt(point);
    _tracked.Remove(tracked);
}
```

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode RemoteCastPresenterTests
bash tools/unity-tests.sh EditMode SkillCoordinatorChargeTests
```

预期：全部 PASS；新增用例在 Impact 点爆炸，旧用例（无 Impact）在 Hit 点爆炸。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/SkillCastReporter.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/RemoteCastPresenterTests.cs
git commit -m "feat(sync): report fireball impact and land remote visual at it"
```

---

### 任务 6：瞄准手势纯逻辑与 UGUI 摇杆

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/AimGestureState.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillAimJoystick.cs`
- 测试：`Assets/Scripts/Tests/Editor/AimGestureStateTests.cs`

- [ ] **步骤 1：编写失败测试**

新建 `AimGestureStateTests.cs`：

```csharp
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class AimGestureStateTests
    {
        private static AimGestureState NewState()
            => new AimGestureState(radius: 100f, deadZone: 0.1f,
                cancelEnterScale: 1.4f, cancelExitScale: 1.2f);

        [Test]
        public void test_aim_gesture_press_without_drag_has_zero_value()
        {
            var state = NewState();
            state.Press(new Vector2(10f, 10f));

            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(Vector2.zero, state.Value);
            Assert.AreEqual(Vector2.zero, state.IconOffset);
            Assert.IsFalse(state.CancelArmed);
        }

        [Test]
        public void test_aim_gesture_maps_delta_from_press_point()
        {
            var state = NewState();
            state.Press(new Vector2(10f, 10f));
            state.Drag(new Vector2(60f, 10f)); // 相对按下点 +50，半径 100

            Assert.AreEqual(new Vector2(0.5f, 0f), state.Value);
            Assert.AreEqual(new Vector2(50f, 0f), state.IconOffset);
            Assert.IsFalse(state.CancelArmed);
        }

        [Test]
        public void test_aim_gesture_clamps_icon_offset_and_value()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(250f, 0f));

            Assert.AreEqual(new Vector2(1f, 0f), state.Value);
            Assert.AreEqual(100f, state.IconOffset.x, 0.001f);
        }

        [Test]
        public void test_aim_gesture_dead_zone_zeroes_value_but_not_icon()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(5f, 0f)); // 0.05 < 0.1

            Assert.AreEqual(Vector2.zero, state.Value);
            Assert.AreEqual(5f, state.IconOffset.x, 0.001f);
        }

        [Test]
        public void test_aim_gesture_cancel_has_hysteresis()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(150f, 0f)); // > 1.4×100
            Assert.IsTrue(state.CancelArmed);

            state.Drag(new Vector2(125f, 0f)); // 1.25×：仍在回滞区内
            Assert.IsTrue(state.CancelArmed);

            state.Drag(new Vector2(110f, 0f)); // < 1.2×：退出
            Assert.IsFalse(state.CancelArmed);
        }

        [Test]
        public void test_aim_gesture_release_kind_and_reset()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(150f, 0f));
            Assert.AreEqual(AimReleaseKind.Cancel, state.Release());
            Assert.IsFalse(state.IsActive);

            state.Press(Vector2.zero);
            state.Drag(new Vector2(50f, 0f));
            Assert.AreEqual(AimReleaseKind.Fire, state.Release());
            Assert.AreEqual(AimReleaseKind.None, state.Release());
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode AimGestureStateTests`

预期：编译失败（`AimGestureState` 不存在）。

- [ ] **步骤 3：实现 `AimGestureState`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public enum AimReleaseKind
    {
        None = 0,
        Fire = 1,
        Cancel = 2
    }

    /// <summary>瞄准摇杆纯逻辑：按下点为原点，半径归一，取消带回滞。</summary>
    public sealed class AimGestureState
    {
        private readonly float _radius;
        private readonly float _deadZone;
        private readonly float _cancelEnterScale;
        private readonly float _cancelExitScale;

        private Vector2 _pressLocal;
        private Vector2 _delta;
        private bool _active;
        private bool _cancelArmed;

        public AimGestureState(float radius, float deadZone, float cancelEnterScale, float cancelExitScale)
        {
            _radius = Mathf.Max(1f, radius);
            _deadZone = Mathf.Clamp01(deadZone);
            _cancelEnterScale = Mathf.Max(1f, cancelEnterScale);
            _cancelExitScale = Mathf.Clamp(cancelExitScale, 1f, _cancelEnterScale);
        }

        public bool IsActive => _active;
        public bool CancelArmed => _cancelArmed;
        public Vector2 IconOffset => _active ? Vector2.ClampMagnitude(_delta, _radius) : Vector2.zero;
        public Vector2 Value
        {
            get
            {
                if (!_active) return Vector2.zero;
                Vector2 clamped = Vector2.ClampMagnitude(_delta, _radius);
                return clamped.magnitude <= _deadZone * _radius
                    ? Vector2.zero
                    : clamped / _radius;
            }
        }

        public void Press(Vector2 localPoint)
        {
            _pressLocal = localPoint;
            _delta = Vector2.zero;
            _active = true;
            _cancelArmed = false;
        }

        public void Drag(Vector2 localPoint)
        {
            if (!_active) return;
            _delta = localPoint - _pressLocal;
            float magnitude = _delta.magnitude;
            if (!_cancelArmed && magnitude > _radius * _cancelEnterScale)
                _cancelArmed = true;
            else if (_cancelArmed && magnitude < _radius * _cancelExitScale)
                _cancelArmed = false;
        }

        public AimReleaseKind Release()
        {
            if (!_active) return AimReleaseKind.None;
            var kind = _cancelArmed ? AimReleaseKind.Cancel : AimReleaseKind.Fire;
            Reset();
            return kind;
        }

        public void Reset()
        {
            _active = false;
            _delta = Vector2.zero;
            _cancelArmed = false;
        }
    }
}
```

- [ ] **步骤 4：实现 `MobileSkillAimJoystick`**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    /// <summary>火球按钮上的瞄准摇杆：只产出瞄准状态与释放类型，不动技能数据。</summary>
    public class MobileSkillAimJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _buttonRect;
        [SerializeField] private RectTransform _icon;
        [SerializeField] private float _aimRadius = 90f;      // Canvas 单位
        [SerializeField] private float _deadZone = 0.1f;
        [SerializeField] private float _cancelEnterScale = 1.4f;
        [SerializeField] private float _cancelExitScale = 1.2f;
        [SerializeField] private Color _cancelColor = Color.red;
        [SerializeField] private Graphic _cancelMark;

        private AimGestureState _state;
        private Vector2 _iconHome;
        private Image _iconImage;
        private Color _iconColor = Color.white;
        private AimReleaseKind _releaseKind;

        public bool Interactable { get; set; } = true;
        public RectTransform Icon => _icon;
        public bool IsGestureActive => _state != null && _state.IsActive;
        public Vector2 Aim => _state != null ? _state.Value : Vector2.zero;
        public bool CancelArmed => _state != null && _state.CancelArmed;

        private void Awake()
        {
            _state = new AimGestureState(_aimRadius, _deadZone, _cancelEnterScale, _cancelExitScale);
            if (_icon != null)
            {
                _iconHome = _icon.anchoredPosition;
                _iconImage = _icon.GetComponent<Image>();
                if (_iconImage != null) _iconColor = _iconImage.color;
            }
        }

        private void OnEnable()
        {
            _releaseKind = AimReleaseKind.None;
            if (_state != null) _state.Reset();
            ApplyVisual();
        }

        private void OnDisable()
        {
            if (_state != null) _state.Reset();
            _releaseKind = AimReleaseKind.Cancel;
            ApplyVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable || _buttonRect == null || _state == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _buttonRect, eventData.position, eventData.pressEventCamera, out var local);
            _state.Press(local);
            _releaseKind = AimReleaseKind.None;
            ApplyVisual();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_state == null || !_state.IsActive || _buttonRect == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _buttonRect, eventData.position, eventData.pressEventCamera, out var local);
            _state.Drag(local);
            ApplyVisual();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_state == null) return;
            _releaseKind = _state.Release();
            ApplyVisual();
        }

        public AimReleaseKind ConsumeReleaseKind()
        {
            var kind = _releaseKind;
            _releaseKind = AimReleaseKind.None;
            return kind;
        }

        /// <summary>技能已结束但手指仍按着时复位；不合成释放。</summary>
        public void ResetGesture()
        {
            if (_state != null) _state.Reset();
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (_icon != null)
                _icon.anchoredPosition = _iconHome + (_state != null ? _state.IconOffset : Vector2.zero);

            if (_iconImage != null)
                _iconImage.color = _state != null && _state.CancelArmed ? _cancelColor : _iconColor;

            if (_cancelMark != null)
                _cancelMark.enabled = _state != null && _state.CancelArmed;
        }
    }
}
```

- [ ] **步骤 5：运行测试确认通过**

运行：`bash tools/unity-tests.sh EditMode AimGestureStateTests`

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Mobile/AimGestureState.cs \
  Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillAimJoystick.cs \
  Assets/Scripts/Tests/Editor/AimGestureStateTests.cs
git commit -m "feat(ui): add aim gesture state and fireball aim joystick"
```

---

### 任务 7：InputManager 与 MobileInputBinder 接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`
- 测试：`Assets/Scripts/Tests/Editor/MobileInputManagerUiHooksTests.cs`、`Assets/Scripts/Tests/Editor/MobileSkillInputBridgeTests.cs`

- [ ] **步骤 1：编写失败测试**

`MobileInputManagerUiHooksTests.cs` 追加：

```csharp
[Test]
public void test_ui_fireball_aim_states_forwarded()
{
    _input.SetSkillFireballAimFromUI(true, new Vector2(0.5f, 0f), true);

    Assert.IsTrue(_input.IsSkillFireballAimActive());
    Assert.AreEqual(new Vector2(0.5f, 0f), _input.SkillFireballAim);
    Assert.IsTrue(_input.IsSkillFireballCancelArmed());

    _input.SetSkillFireballAimFromUI(false, Vector2.zero, false);
    Assert.IsFalse(_input.IsSkillFireballAimActive());
    Assert.AreEqual(Vector2.zero, _input.SkillFireballAim);
    Assert.IsFalse(_input.IsSkillFireballCancelArmed());
}

[Test]
public void test_ui_fireball_release_kind_consumed_once()
{
    _input.SetSkillFireballReleaseFromUI(canceled: true);

    Assert.IsTrue(_input.ConsumeSkillFireballRelease(out bool canceled));
    Assert.IsTrue(canceled);
    Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _), "释放类型只能消费一次");
}

[Test]
public void test_ui_clear_combat_input_resets_aim_state()
{
    _input.SetSkillFireballAimFromUI(true, new Vector2(1f, 0f), true);
    _input.SetSkillFireballReleaseFromUI(canceled: true);

    MobileInputBinder.ClearCombatUiInput(_input);

    Assert.IsFalse(_input.IsSkillFireballAimActive());
    Assert.AreEqual(Vector2.zero, _input.SkillFireballAim);
    Assert.IsFalse(_input.IsSkillFireballCancelArmed());
    Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _));
}
```

`MobileSkillInputBridgeTests.cs` 追加：

```csharp
[Test]
public void test_binder_forwards_fireball_aim_and_release()
{
    var binder = CreateBinder();
    var aim = new GameObject("Aim").AddComponent<MobileSkillAimJoystick>();
    _created.Add(aim.gameObject);
    TestHelpers.SetField(binder, "_fireballAim", aim);
    TestHelpers.SetField(binder, "_inputManager", _input);

    // 直接驱动纯状态，绕过 UGUI 指针
    TestHelpers.SetField(aim, "_state",
        new AimGestureState(100f, 0.1f, 1.4f, 1.2f));
    var state = (AimGestureState)TestHelpers.GetField(aim, "_state");
    state.Press(Vector2.zero);
    state.Drag(new Vector2(150f, 0f));
    TestHelpers.SetField(aim, "_releaseKind", AimReleaseKind.Cancel);

    Invoke(binder, "LateUpdate");

    Assert.IsTrue(_input.IsSkillFireballAimActive());
    Assert.IsTrue(_input.IsSkillFireballCancelArmed());
    Assert.IsTrue(_input.ConsumeSkillFireballRelease(out bool canceled));
    Assert.IsTrue(canceled);
}
```

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/unity-tests.sh EditMode MobileInputManagerUiHooksTests
bash tools/unity-tests.sh EditMode MobileSkillInputBridgeTests
```

预期：编译失败（新 API 不存在）。

- [ ] **步骤 3：实现 `InputManager`**

字段：

```csharp
private bool _skillFireballAimActiveFromUI;
private Vector2 _skillFireballAimFromUI;
private bool _skillFireballCancelArmedFromUI;
private bool _skillFireballReleaseFromUI;
private bool _skillFireballReleaseCanceledFromUI;
private bool _skillFireballReleaseConsumed = true;
```

方法：

```csharp
public void SetSkillFireballAimFromUI(bool active, Vector2 aim, bool cancelArmed)
{
    _skillFireballAimActiveFromUI = active;
    _skillFireballAimFromUI = active ? aim : Vector2.zero;
    _skillFireballCancelArmedFromUI = active && cancelArmed;
}

public bool IsSkillFireballAimActive() => _skillFireballAimActiveFromUI;
public Vector2 SkillFireballAim => _skillFireballAimFromUI;
public bool IsSkillFireballCancelArmed() => _skillFireballCancelArmedFromUI;

public void SetSkillFireballReleaseFromUI(bool canceled)
{
    _skillFireballReleaseFromUI = true;
    _skillFireballReleaseCanceledFromUI = canceled;
    _skillFireballReleaseConsumed = false;
}

public bool ConsumeSkillFireballRelease(out bool canceled)
{
    canceled = _skillFireballReleaseCanceledFromUI;
    if (!_skillFireballReleaseFromUI || _skillFireballReleaseConsumed) return false;
    _skillFireballReleaseConsumed = true;
    return true;
}

/// <summary>teardown 清掉未消费的释放锁存（避免复活后补取消）。</summary>
public void ClearSkillFireballReleaseFromUI()
{
    _skillFireballReleaseFromUI = false;
    _skillFireballReleaseCanceledFromUI = false;
    _skillFireballReleaseConsumed = true;
}
```

在 `SetSkillFireballPressedFromUI(bool pressed)` 里，当 `pressed` 为 true 时清掉旧的释放锁存：

```csharp
if (pressed)
{
    _skillFireballReleaseFromUI = false;
    _skillFireballReleaseConsumed = true;
}
```

在 `Update()` 的重置段追加 `_skillFireballReleaseConsumed = false;`（值本身保留，供本帧晚些时候消费；下一次 LateUpdate 没有新释放时不会重新置位）。

- [ ] **步骤 4：实现 `MobileInputBinder`**

字段：

```csharp
[SerializeField] private MobileSkillAimJoystick _fireballAim;
```

`LateUpdate` **末尾**（`_buttons` 处理与 `ConsumeSkillPresses` 之后）追加：

```csharp
if (_fireballAim != null)
{
    _inputManager.SetSkillFireballAimFromUI(
        _fireballAim.IsGestureActive, _fireballAim.Aim, _fireballAim.CancelArmed);
    var kind = _fireballAim.ConsumeReleaseKind();
    if (kind != AimReleaseKind.None)
        _inputManager.SetSkillFireballReleaseFromUI(kind == AimReleaseKind.Cancel);
}
else
{
    _inputManager.SetSkillFireballAimFromUI(false, Vector2.zero, false);
}
```

**顺序原因**：`SetSkillFireballPressedFromUI(true)` 会清掉旧的释放锁存；同一帧「按下即松开」时，必须先写 pressed（按钮块），再写 release（本块），否则取消释放会被 pressed 清掉，下一帧 level-based 释放会把技能打出去。

`ClearCombatUiInput` 追加：

```csharp
inputManager.SetSkillFireballAimFromUI(false, Vector2.zero, false);
inputManager.ClearSkillFireballReleaseFromUI();
```

`OnApplicationFocus(false)` 与 `ReleaseBinding` 在清 UI 之前调用取消：

```csharp
if (_boundEntry != null) _boundEntry.CancelAimingSkill();
if (_fireballAim != null) _fireballAim.ResetGesture();
```

顺序必须先取消、再 `ClearCombatInput`/`ClearCombatUiInput`。

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode MobileInputManagerUiHooksTests
bash tools/unity-tests.sh EditMode MobileSkillInputBridgeTests
```

预期：PASS。注意 `MobileSkillInputBridgeTests` 的 `_created` 列表是 private；用 `NewGameObject` 辅助或把新对象加入 `_created`。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs \
  Assets/Scripts/Tests/Editor/MobileInputManagerUiHooksTests.cs \
  Assets/Scripts/Tests/Editor/MobileSkillInputBridgeTests.cs
git commit -m "feat(input): forward fireball aim state and release kind"
```

---

### 任务 8：选中目标来源

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs`

- [ ] **步骤 1：编写失败测试**

`NetworkTargetSelectorTests.cs` 追加：

```csharp
[Test]
public void TryGetLockedPosition_Returns_View_Position()
{
    CreateView(2L, new Vector3(3f, 0f, 4f), EntityKind.Npc);
    PressSwitchAndTick();
    Assert.AreEqual(2L, _selector.LockedTargetId, "前置：应已锁定");

    Assert.IsTrue(_selector.TryGetLockedPosition(out var position));
    Assert.AreEqual(new Vector3(3f, 0f, 4f), position);
}

[Test]
public void TryGetLockedPosition_Returns_False_Without_Lock()
{
    Assert.IsFalse(_selector.TryGetLockedPosition(out _));
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests`

预期：编译失败（`TryGetLockedPosition` 不存在）。

- [ ] **步骤 3：实现 `TryGetLockedPosition`**

在 `NetworkTargetSelector` 的 `LockedTargetId` 后追加：

```csharp
/// <summary>当前锁定目标的渲染位置；无锁/视图消失/已死亡返回 false。</summary>
public bool TryGetLockedPosition(out Vector3 position)
{
    position = default;
    if (_registry == null || _lockedId == 0) return false;
    var view = _registry.GetView(_lockedId);
    if (view == null || view.Dead) return false;
    position = view.Position;
    return true;
}
```

- [ ] **步骤 4：暴露单机目标并注入联网提供者**

`CharacterAttackHandler.cs` 在 `_currentTarget` 后追加：

```csharp
/// <summary>当前选中目标（目标面板/选中环显示的那个）；无目标为 null。</summary>
public ITargetable CurrentTarget => _currentTarget;
```

`ClientNetworkHost.cs` 在 `_networkTargetSelector = selector;` 后追加：

```csharp
_localEntry.SetSelectedTargetPositionProvider(() =>
    selector != null && selector.TryGetLockedPosition(out var p) ? p : (Vector3?)null);
```

`DetachTargetSelector` 在 `_networkTargetSelector = null;` 前追加：

```csharp
_localEntry?.SetSelectedTargetPositionProvider(null);
```

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests
```

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs
git commit -m "feat(target): expose locked target position for tap aiming"
```

---

### 任务 9：Sys3CEntry 瞄准集成

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/SkillAimMath.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 测试：`Assets/Scripts/Tests/Editor/SkillAimMathTests.cs`

- [ ] **步骤 1：编写失败测试**

新建 `SkillAimMathTests.cs`：

```csharp
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillAimMathTests
    {
        [Test]
        public void test_aim_direction_is_camera_relative()
        {
            // 相机朝 +Z；摇杆右推 → 世界 +X
            var dir = SkillAimMath.CameraRelativeDirection(new Vector2(1f, 0f), Vector3.forward);
            Assert.AreEqual(1f, dir.x, 0.001f);
            Assert.AreEqual(0f, dir.z, 0.001f);
        }

        [Test]
        public void test_aim_direction_uses_yaw_only()
        {
            // 相机带俯仰：方向仍应水平
            var dir = SkillAimMath.CameraRelativeDirection(new Vector2(0f, 1f),
                new Vector3(0f, -0.5f, 0.8f));
            Assert.AreEqual(0f, dir.y, 0.001f);
            Assert.AreEqual(1f, dir.z, 0.001f, 0.01f);
        }

        [Test]
        public void test_aim_distance_maps_magnitude()
        {
            Assert.AreEqual(2f, SkillAimMath.DistanceFromMagnitude(0f, 2f, 15f), 0.001f);
            Assert.AreEqual(8.5f, SkillAimMath.DistanceFromMagnitude(0.5f, 2f, 15f), 0.001f);
            Assert.AreEqual(15f, SkillAimMath.DistanceFromMagnitude(1f, 2f, 15f), 0.001f);
        }

        [Test]
        public void test_aim_clamp_keeps_direction_and_max_distance()
        {
            var origin = Vector3.zero;
            var target = new Vector3(0f, 2f, 30f);
            var clamped = SkillAimMath.ClampToRange(origin, target, 15f);
            Assert.AreEqual(15f, clamped.z, 0.001f);
            Assert.AreEqual(2f, clamped.y, 0.001f);
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode SkillAimMathTests`

预期：编译失败（`SkillAimMath` 不存在）。

- [ ] **步骤 3：实现 `SkillAimMath`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>地面瞄准的纯数学：相机相对方向、幅度距离映射、射程钳制。</summary>
    public static class SkillAimMath
    {
        public static Vector3 CameraRelativeDirection(Vector2 aim, Vector3 cameraForward)
        {
            Vector3 flat = new Vector3(cameraForward.x, 0f, cameraForward.z);
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            flat.Normalize();

            Vector3 world = Quaternion.LookRotation(flat) * new Vector3(aim.x, 0f, aim.y);
            return world.sqrMagnitude > 0.0001f ? world.normalized : flat;
        }

        public static float DistanceFromMagnitude(float magnitude, float min, float max)
            => Mathf.Lerp(min, max, Mathf.Clamp01(magnitude));

        public static Vector3 ClampToRange(Vector3 origin, Vector3 target, float maxDistance)
        {
            Vector3 flat = target - origin;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance <= maxDistance || distance < 0.0001f) return target;

            Vector3 clamped = origin + flat / distance * maxDistance;
            clamped.y = target.y;
            return clamped;
        }
    }
}
```

- [ ] **步骤 4：实现 Sys3CEntry 集成**

在 `Sys3CEntry` 字段区追加：

```csharp
[SerializeField] private LayerMask _aimGroundMask = ~(1 << 10);
private Func<Vector3?> _selectedTargetPositionProvider;
private GroundAimIndicator _aimIndicator;
private bool _fireballCancelRequested;
```

（文件顶部需要 `using Hotfix.GameSystems.Skills.Data;` 与 `using Hotfix.GameSystems.Skills.Runtime.Projectile;`，若尚未存在。）

公开方法：

```csharp
/// <summary>联网装配注入：当前选中目标的世界位置；单机走 CharacterAttackHandler 兜底。</summary>
public void SetSelectedTargetPositionProvider(Func<Vector3?> provider)
    => _selectedTargetPositionProvider = provider;

/// <summary>teardown 用：取消正在瞄准的火球，不发射、不冷却、不上报。</summary>
public bool CancelAimingSkill()
{
    int fireballId = GetSkillFireballId();
    return fireballId > 0 && _skillCoordinator.CancelCharge(fireballId);
}

private bool IsHoldToAimCharging()
    => _skillCoordinator.CurrentSubState == Skills.Definition.SkillSubState.Charging
       && _skillCoordinator.CurrentSkill != null
       && _skillCoordinator.CurrentSkill.Data is ProjectileSkillData p
       && p.HoldToAim;

private bool IsMobileAimActive()
    => IsHoldToAimCharging() && _inputManager != null && _inputManager.IsSkillFireballAimActive();
```

在 `HandleInput` 现有的 `if (_isDead) return;` **之前**插入（死亡也要走一次隐藏；该行本身保留）：

```csharp
// EditMode 测试可能没有 _skillCoordinator/_inputManager（Awake/Start 未执行）——
// 必须先判空，否则死亡用例会在早退之前 NRE。
bool aimCharging = _skillCoordinator != null && IsHoldToAimCharging();
bool mobileAim = aimCharging && _inputManager != null
    && _inputManager.IsSkillFireballAimActive();
UpdateFireballAim(aimCharging, mobileAim);
```

防御块改为：`if (!aimCharging && _inputManager.IsDefendHeld())`（进入防御）；否则分支保持退出防御。跳跃改为：

```csharp
if (!aimCharging && _inputManager.IsJumpPressed())
    _cc.RequestJump();
```

火球释放块改为：

```csharp
if (_inputManager.IsSkillFireballPressed())
{
    _fireballCancelRequested = false;
    // ... 原有激活逻辑 ...
}

if (!_inputManager.IsSkillFireballHeld())
{
    if (_inputManager.ConsumeSkillFireballRelease(out bool canceled))
        _fireballCancelRequested = canceled;

    if (_fireballCancelRequested)
    {
        if (CancelAimingSkill()) _fireballCancelRequested = false;
        else if (_skillCoordinator.CurrentSkill == null) _fireballCancelRequested = false;
    }
    else
    {
        _skillCoordinator.TryReleaseCharge(GetSkillFireballId());
    }
}
```

（原有激活块里已经有 `if (_inputManager.IsSkillFireballPressed())`；把 `_fireballCancelRequested = false;` 放在它内部第一行。）

新增方法：

```csharp
private void UpdateFireballAim(bool aimCharging, bool mobileAim)
{
    if (!aimCharging)
    {
        if (_aimIndicator != null) _aimIndicator.Hide();
        return;
    }

    var data = _skillCoordinator.CurrentSkill.Data as ProjectileSkillData;
    if (data == null) return;

    Vector3 target;
    if (mobileAim)
    {
        Vector2 aim = _inputManager.SkillFireballAim;
        if (aim.sqrMagnitude > 0f)
        {
            Vector3 cameraForward = _camera != null ? _camera.transform.forward : Vector3.forward;
            Vector3 dir = SkillAimMath.CameraRelativeDirection(aim, cameraForward);
            float distance = SkillAimMath.DistanceFromMagnitude(
                aim.magnitude, data.MinAimDistance, data.ProjectileRange);
            target = transform.position + dir * distance;
        }
        else
        {
            target = ResolveDefaultTarget(data);
        }
    }
    else
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        target = transform.position + forward.normalized * data.ProjectileRange;
    }

    target = SnapToGround(target);
    _skillCoordinator.UpdateAimTarget(target);

    if (mobileAim)
    {
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(flat.normalized), 10f * Time.deltaTime);
        }
    }

    if (mobileAim)
    {
        EnsureAimIndicator(data);
        if (_aimIndicator != null)
        {
            _aimIndicator.Show(target, data.ExplosionRadius,
                _inputManager.IsSkillFireballCancelArmed());
        }
    }
    else if (_aimIndicator != null)
    {
        _aimIndicator.Hide();
    }
}

private Vector3 ResolveDefaultTarget(ProjectileSkillData data)
{
    Vector3? selected = _selectedTargetPositionProvider?.Invoke();
    if (!selected.HasValue && _attackHandler != null && _attackHandler.CurrentTarget != null)
        selected = _attackHandler.CurrentTarget.WorldPosition;

    if (selected.HasValue)
        return SkillAimMath.ClampToRange(transform.position, selected.Value, data.ProjectileRange);

    Vector3 forward = transform.forward;
    forward.y = 0f;
    if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
    return transform.position + forward.normalized * data.ProjectileRange;
}

private Vector3 SnapToGround(Vector3 point)
{
    Vector3 origin = point + Vector3.up * 20f;
    if (Physics.Raycast(origin, Vector3.down, out var hit, 50f,
            _aimGroundMask, QueryTriggerInteraction.Ignore))
        return hit.point;
    return point;
}

private void EnsureAimIndicator(ProjectileSkillData data)
{
    if (_aimIndicator != null || data.AimIndicatorPrefab == null) return;
    var instance = Instantiate(data.AimIndicatorPrefab, transform);
    _aimIndicator = instance.GetComponent<GroundAimIndicator>();
    if (_aimIndicator == null)
    {
        Destroy(instance);
        return;
    }
    _aimIndicator.Hide();
}
```

`Update` 中现有的「蓄力中无移动输入时朝相机转」块改为仅在非移动端瞄准时执行：

```csharp
if (!IsMobileAimActive()
    && _skillCoordinator.CurrentSubState == Skills.Definition.SkillSubState.Charging
    && _skillCoordinator.CanRotate()
    && !_inputManager.IsMoving())
{
    // 原相机跟随代码
}
```

- [ ] **步骤 5：运行测试确认通过**

运行：`bash tools/unity-tests.sh EditMode SkillAimMathTests`

预期：PASS。Sys3CEntry 的编译由下一步全量编译检查（PlayMode 测试）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/SkillAimMath.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
  Assets/Scripts/Tests/Editor/SkillAimMathTests.cs
git commit -m "feat(sys3c): drive fireball aim point rotation and cancel"
```

---

### 任务 10：地面落点圈组件与占位 prefab

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/GroundAimIndicator.cs`
- 创建：`Assets/PreRes/Skills/FireballAimIndicator.prefab`
- 修改：`Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset`
- 测试：`Assets/Scripts/Tests/Editor/GroundAimIndicatorTests.cs`

- [ ] **步骤 1：编写失败测试**

新建 `GroundAimIndicatorTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameSys.EditorTests
{
    public class GroundAimIndicatorTests
    {
        private const string PrefabPath = "Assets/PreRes/Skills/FireballAimIndicator.prefab";
        private const string FireballPath = "Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset";

        [Test]
        public void test_indicator_prefab_has_no_collider_and_no_shadow_cast()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "占位地面圈 prefab 缺失");
            Assert.IsNull(prefab.GetComponentInChildren<Collider>(), "地面圈不得带 Collider");
            Assert.IsNotNull(prefab.GetComponent<GroundAimIndicator>(), "缺少 GroundAimIndicator");
            var renderer = prefab.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(renderer);
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
        }

        [Test]
        public void test_fireball_asset_references_aim_indicator_prefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(FireballPath);
            Assert.IsNotNull(asset);
            Assert.IsNotNull(asset.AimIndicatorPrefab, "火球资产必须引用占位地面圈");
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode GroundAimIndicatorTests`

预期：FAIL（prefab 与组件不存在）。

- [ ] **步骤 3：实现 `GroundAimIndicator`**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Projectile
{
    /// <summary>地面落点圈占位表现：只负责位置、半径与颜色，不做判定。</summary>
    public class GroundAimIndicator : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Color _normalColor = new Color(1f, 0.2f, 0.1f, 0.35f);
        [SerializeField] private Color _cancelColor = new Color(1f, 0f, 0f, 0.6f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        }

        public void Show(Vector3 position, float radius, bool cancel)
        {
            gameObject.SetActive(true);
            transform.position = position + Vector3.up * 0.05f;
            transform.rotation = Quaternion.identity;
            transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

            if (_renderer != null)
            {
                _block ??= new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_block);
                _block.SetColor(ColorId, cancel ? _cancelColor : _normalColor);
                _renderer.SetPropertyBlock(_block);
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
```

- [ ] **步骤 4：创建占位 prefab 并绑定资产**

用 Unity MCP `script-execute` 执行以下编辑器脚本（或手动在编辑器完成等价操作）：

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Skills.Data;

var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
go.name = "FireballAimIndicator";
var collider = go.GetComponent<Collider>();
if (collider != null) { collider.enabled = false; Object.DestroyImmediate(collider); }

var renderer = go.GetComponent<MeshRenderer>();
renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/PreRes/Skills/Materials/WarnDecal.mat");
renderer.shadowCastingMode = ShadowCastingMode.Off;
renderer.receiveShadows = false;
go.transform.localScale = new Vector3(1f, 0.02f, 1f);

var indicator = go.AddComponent<GroundAimIndicator>();
var so = new SerializedObject(indicator);
so.FindProperty("_renderer").objectReferenceValue = renderer;
so.ApplyModifiedProperties();

var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/PreRes/Skills/FireballAimIndicator.prefab");
Object.DestroyImmediate(go);

var asset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>("Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset");
var assetSo = new SerializedObject(asset);
assetSo.FindProperty("_aimIndicatorPrefab").objectReferenceValue = prefab;
assetSo.ApplyModifiedProperties();
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
```

注意：prefab 的初始 scale 保持 1；运行时 `Show` 会把 scale 设为 `(radius*2, 0.02, radius*2)`。

- [ ] **步骤 5：运行测试确认通过**

运行：`bash tools/unity-tests.sh EditMode GroundAimIndicatorTests`

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/GroundAimIndicator.cs \
  Assets/PreRes/Skills/FireballAimIndicator.prefab \
  Assets/PreRes/Skills/FireballAimIndicator.prefab.meta \
  Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset \
  Assets/Scripts/Tests/Editor/GroundAimIndicatorTests.cs
git commit -m "feat(skills): add placeholder ground aim indicator"
```

---

### 任务 11：技能栏表现与场景接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillBarPresenter.cs`
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 测试：`Assets/Scripts/Tests/Editor/MobileSkillBarPresenterTests.cs`、`Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs`

- [ ] **步骤 1：编写失败测试**

`MobileSkillBarPresenterTests.cs` 追加：

```csharp
[Test]
public void test_hold_to_aim_charging_hides_charge_ring()
{
    // Arrange（火球 HoldToAim、Charging）
    var entry = CreateEntry("AimEntry");
    var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
    TestHelpers.SetField(data, "_skillId", (int)SkillID.Fireball);
    TestHelpers.SetField(data, "_maxChargeTime", 2f);
    TestHelpers.SetField(data, "_holdToAim", true);
    TestHelpers.SetField(data, "_skillType", SkillType.Projectile);

    var coordinator = new SkillCoordinator(new TestHelpers.FakeTarget(entry.gameObject.transform));
    coordinator.RegisterSkill(data);
    var executor = new SkillExecutor(new TestHelpers.FakeTarget(entry.gameObject.transform), data);
    var machine = new FakeTimeMachine(data);
    InjectMachine(executor, machine);
    executor.TryStart();
    machine.Now = 0.15f;
    executor.Update(0.01f);
    Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
    TestHelpers.SetField(coordinator, "_currentSkill", executor);
    TestHelpers.SetField(entry, "_skillCoordinator", coordinator);
    Bind(entry);

    // Act
    Refresh();

    // Assert
    Assert.IsFalse(_charges[SkillID.Fireball].gameObject.activeSelf,
        "按住瞄准期间不得显示蓄力环");
}
```

`MobileSkillHudTests.cs` 追加断言：

```csharp
var fireball = skills.First(x => x.SkillId == (int)SkillID.Fireball);
var aim = fireball.GetComponent<MobileSkillAimJoystick>();
Assert.IsNotNull(aim, "火球按钮必须挂 MobileSkillAimJoystick");
Assert.IsNotNull(aim.Icon, "摇杆必须绑定 Icon");
```

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/unity-tests.sh EditMode MobileSkillBarPresenterTests
bash tools/unity-tests.sh PlayMode MobileSkillHudTests
```

预期：蓄力环用例失败；PlayMode 场景断言失败（组件未挂）。

- [ ] **步骤 3：实现 `MobileSkillBarPresenter`**

追加字段（文件顶部补 `using Hotfix.GameSystems.Skills.Data;`）：

```csharp
[SerializeField] private MobileSkillAimJoystick _fireballAim;
```

`Update` 的蓄力环逻辑改为：

```csharp
float charge = 0f;
bool holdToAim = false;
if (coordinator.CurrentSkill != null
    && coordinator.CurrentSkill.SkillId == button.SkillId
    && coordinator.CurrentSubState == SkillSubState.Charging)
{
    holdToAim = coordinator.CurrentSkill.Data is ProjectileSkillData p && p.HoldToAim;
    charge = holdToAim ? 0f : coordinator.CurrentSkill.GetChargeProgress();
}
button.RenderCharge(charge);
```

在循环后追加：

```csharp
if (_fireballAim != null)
{
    bool fireballCharging = coordinator.CurrentSkill != null
        && coordinator.CurrentSkill.SkillId == (int)SkillID.Fireball
        && coordinator.CurrentSubState == SkillSubState.Charging;

    if (_fireballAim.IsGestureActive && !fireballCharging)
        _fireballAim.ResetGesture();

    bool canStart = entry.Stamina != null && entry.Stamina.CanStartConsuming
        && !coordinator.GetCooldownInfo((int)SkillID.Fireball).IsOnCooldown;
    _fireballAim.Interactable = fireballCharging || canStart;
}
```

`ResetViews` 追加 `_fireballAim?.ResetGesture();`。

- [ ] **步骤 4：场景接线**

用 Unity MCP `script-execute` 执行：

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hotfix.GameSystems.UI;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Skills.Definition;

var scene = SceneManager.GetActiveScene();
if (scene.name != "DemoDay") return;

MobileSkillButton fireball = null;
foreach (var b in Object.FindObjectsOfType<MobileSkillButton>(true))
    if (b.SkillId == (int)SkillID.Fireball) fireball = b;
if (fireball == null) { Debug.LogError("fireball button not found"); return; }

var go = fireball.gameObject;
var aim = go.GetComponent<MobileSkillAimJoystick>();
if (aim == null) aim = go.AddComponent<MobileSkillAimJoystick>();

var so = new SerializedObject(aim);
so.FindProperty("_buttonRect").objectReferenceValue = go.GetComponent<RectTransform>();
var icon = go.transform.Find("Icon");
if (icon != null) so.FindProperty("_icon").objectReferenceValue = icon.GetComponent<RectTransform>();
so.ApplyModifiedProperties();

var binder = Object.FindObjectOfType<MobileInputBinder>();
if (binder != null)
{
    var bso = new SerializedObject(binder);
    bso.FindProperty("_fireballAim").objectReferenceValue = aim;
    bso.ApplyModifiedProperties();
}

var presenter = Object.FindObjectOfType<MobileSkillBarPresenter>();
if (presenter != null)
{
    var pso = new SerializedObject(presenter);
    pso.FindProperty("_fireballAim").objectReferenceValue = aim;
    pso.ApplyModifiedProperties();
}

EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);
```

手动等价路径：选中火球按钮 → Add Component `MobileSkillAimJoystick` → 绑定 `Button Rect` / `Icon`；把该组件拖到 `MobileInput` 上的 `MobileInputBinder._fireballAim` 与 `MobileSkillBarPresenter._fireballAim`。

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/unity-tests.sh EditMode MobileSkillBarPresenterTests
bash tools/unity-tests.sh PlayMode MobileSkillHudTests
```

预期：PASS（PlayMode 用例会加载 DemoDay，需先保存场景）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileSkillBarPresenter.cs \
  Assets/SimpleLowPolyNature/Scenes/DemoDay.unity \
  Assets/Scripts/Tests/Editor/MobileSkillBarPresenterTests.cs \
  Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs
git commit -m "feat(ui): hide charge ring during aim and wire fireball joystick"
```

---

### 任务 12：资产一致性、PlayMode 验收与文档

**文件：**
- 创建：`Assets/Scripts/Tests/Editor/FireballAssetTableTests.cs`
- 修改：`Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的资产一致性测试**

新建 `FireballAssetTableTests.cs`：

```csharp
using Cool.SyncCore;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    public class FireballAssetTableTests
    {
        private const string Path = "Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset";

        [Test]
        public void test_fireball_asset_matches_server_projectile_table()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(Path);
            Assert.IsNotNull(asset);
            Assert.IsTrue(asset.HoldToAim);
            Assert.AreEqual(2f, asset.MinAimDistance, 0.001f);
            Assert.IsFalse(asset.CanMoveWhileCharging);

            Assert.IsTrue(CombatRules.TryGet(20003, out var entry));
            Assert.IsNotNull(entry.Projectile);
            Assert.AreEqual(ProjectileKind.CircleAtPoint, entry.Projectile.Kind);
            Assert.AreEqual(asset.ExplosionRadius, entry.Projectile.Radius, 0.001f);
            Assert.AreEqual(asset.ProjectileRange, entry.Projectile.MaxImpactDistance, 0.001f);
            Assert.AreEqual(asset.ProjectileSpeed, entry.Projectile.Speed, 0.001f);
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败/通过**

运行：`bash tools/unity-tests.sh EditMode FireballAssetTableTests`

预期：若任务 1/2 已正确完成则 PASS；若资产字段漏写则 FAIL。若 FAIL，回到任务 2/10 补齐资产。

- [ ] **步骤 3：编写 PlayMode 验收**

在 `MobileTouchInputTests.cs` 追加两个 `[UnityTest]` 与三个私有辅助方法（放在类内）：

```csharp
private MobileInputBinder _aimBinder;

private IEnumerator LoadDemoDayAndBind()
{
    SceneManager.LoadScene("DemoDay");
    yield return null;
    yield return null;

    var host = FindRoot("MobileInput");
    Assert.IsNotNull(host, "场景里应有 MobileInput 宿主");
    _aimBinder = host.GetComponent<MobileInputBinder>();
    Assert.IsNotNull(_aimBinder);
    _aimBinder.TouchOverride = TouchOverride.ForceTouch;

    for (int i = 0; i < 600 && _aimBinder.BoundEntry == null; i++) yield return null;
    Assert.IsNotNull(_aimBinder.BoundEntry, "应绑定本地角色");
}

private IEnumerator UnloadDemoDay()
{
    SceneManager.SetActiveScene(SceneManager.CreateScene("FireballAimCleanup"));
    var unload = SceneManager.UnloadSceneAsync("DemoDay");
    while (unload != null && !unload.isDone) yield return null;
}

private static void SendPointerDown(MobileSkillAimJoystick aim, Vector2 position)
{
    var data = new PointerEventData(EventSystem.current) { position = position };
    ExecuteEvents.Execute(aim.gameObject, data, ExecuteEvents.pointerDownHandler);
}

private static void SendPointerDrag(MobileSkillAimJoystick aim, Vector2 position)
{
    var data = new PointerEventData(EventSystem.current) { position = position };
    ExecuteEvents.Execute(aim.gameObject, data, ExecuteEvents.dragHandler);
}

private static void SendPointerUp(MobileSkillAimJoystick aim, Vector2 position)
{
    var data = new PointerEventData(EventSystem.current) { position = position };
    ExecuteEvents.Execute(aim.gameObject, data, ExecuteEvents.pointerUpHandler);
}

[UnityTest]
public IEnumerator test_fireball_aim_drag_release_fires()
{
    bool prevIgnore = LogAssert.ignoreFailingMessages;
    LogAssert.ignoreFailingMessages = true;

    yield return LoadDemoDayAndBind();
    var aim = Object.FindObjectOfType<MobileSkillAimJoystick>();
    Assert.IsNotNull(aim, "火球按钮应挂瞄准摇杆");
    aim.Interactable = true;

    SendPointerDown(aim, Vector2.zero);
    SendPointerDrag(aim, new Vector2(60f, 0f));
    SendPointerUp(aim, new Vector2(60f, 0f));

    // 落点必须来自拖拽，而不是被松手帧 PC 兜底覆盖成最大射程
    Vector3 impact = default;
    for (int i = 0; i < 30; i++)
    {
        var skill = _aimBinder.BoundEntry.SkillCoordinator.CurrentSkill;
        if (skill != null && skill.SkillId == 20003)
        {
            impact = skill.ReportedImpactPoint;
            break;
        }
        yield return null;
    }
    Vector3 playerPos = _aimBinder.BoundEntry.transform.position;
    float aimDistance = new Vector2(impact.x - playerPos.x, impact.z - playerPos.z).magnitude;
    Assert.Less(aimDistance, 14.5f, "拖拽落点不得被覆盖成最大射程");

    for (int i = 0; i < 180 && !_aimBinder.BoundEntry.SkillCoordinator
             .GetCooldownInfo(20003).IsOnCooldown; i++)
        yield return null;

    Assert.IsTrue(_aimBinder.BoundEntry.SkillCoordinator.GetCooldownInfo(20003).IsOnCooldown,
        "拖动后松手应发射并进入冷却");

    yield return UnloadDemoDay();
    LogAssert.ignoreFailingMessages = prevIgnore;
}

[UnityTest]
public IEnumerator test_fireball_aim_far_release_cancels_without_cooldown()
{
    bool prevIgnore = LogAssert.ignoreFailingMessages;
    LogAssert.ignoreFailingMessages = true;

    yield return LoadDemoDayAndBind();
    var aim = Object.FindObjectOfType<MobileSkillAimJoystick>();
    Assert.IsNotNull(aim, "火球按钮应挂瞄准摇杆");
    aim.Interactable = true;

    SendPointerDown(aim, Vector2.zero);
    yield return null; // 让激活发生（Casting 也允许取消）
    SendPointerDrag(aim, new Vector2(0f, 400f)); // 远大于 1.4 × 90，兼容 Canvas 缩放
    SendPointerUp(aim, new Vector2(0f, 400f));

    for (int i = 0; i < 30; i++) yield return null;

    Assert.IsFalse(_aimBinder.BoundEntry.SkillCoordinator.GetCooldownInfo(20003).IsOnCooldown,
        "拉远取消不得进入冷却");

    yield return UnloadDemoDay();
    LogAssert.ignoreFailingMessages = prevIgnore;
}
```

注意：`PointerEventData.position` 是屏幕坐标；本用例只验证「按下→拖→松」链路，不依赖具体分辨率。

- [ ] **步骤 4：运行 PlayMode 验收**

```bash
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
```

预期：PASS。若场景未保存，先保存 DemoDay。

- [ ] **步骤 5：更新模块 README**

按以下内容追加到对应 README 的移动端/技能章节：

- `Skills/README.md`：火球按住瞄准、`PlayerCancel`、精力耗尽取消、释放上报 `ImpactX/Y/Z`、20003 服务端落点圆。
- `UI/README.md`：`MobileSkillAimJoystick`（按下点原点、Canvas 单位、取消回滞）、`GroundAimIndicator`（占位、无 Collider、MPB `_Color`）、瞄准期间隐藏蓄力环。
- `Sys3C/README.md`：`Sys3CEntry` 的瞄准刷新顺序（释放判定之前）、移动锁（Casting/Charging）、转向来源、`SkillAimMath`。
- `SyncLayer/README.md`：点按目标（含自动锁定）、远程火球按 `broadcast.Impact` 飞行/爆炸、`TryGetLockedPosition`。
- `Server/README.md`：20003 `CircleAtPoint`、飞行延迟 `ProjectileTravelTicks`。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Tests/Editor/FireballAssetTableTests.cs \
  Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs \
  Assets/Scripts/Hotfix/GameSystems/Skills/README.md \
  Assets/Scripts/Hotfix/GameSystems/UI/README.md \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
  Server/README.md
git commit -m "test(fireball): lock asset table alignment and playmode aim acceptance"
```

---

## 完成标准

- 服务端全部测试通过：`bash tools/test-server.sh`（或 inprocess 脚本）。
- 相关 EditMode 测试通过：
  `AimGestureStateTests`、`SkillAimMathTests`、`SkillStateMachineChargeTests`、`SkillInterruptionMatrixCancelTests`、`SkillCoordinatorChargeTests`、`SkillCoordinatorStaminaTests`、`SkillExecutorFireballTests`、`FireballProjectileTests`、`FireballSpawnerTests`、`MobileInputManagerUiHooksTests`、`MobileSkillInputBridgeTests`、`NetworkTargetSelectorTests`、`RemoteCastPresenterTests`、`GroundAimIndicatorTests`、`FireballAssetTableTests`、`MobileSkillBarPresenterTests`。
- PlayMode：`MobileSkillHudTests`、`MobileTouchInputTests` 通过。
- WebGL 手工验收：按住拖动、点按、拉远取消、切后台再回来均不留锁定状态；联机下客户端落点与服务端受击位置一致。
