# P2 移动同步闭环 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 落地 B+ 移动同步闭环：客户端即时模拟 + 节流上报 + 预测帧对账 + 平滑纠偏；服务器校验 + EntityStore 接线（快照链激活）；远程插值渲染；双客户端移动互见验收。

**Architecture:** 共享 SyncCore 增加 `Vec3`/`MovementValidator`/`PredictionReconciler`（校验与对账规则两端同源）；服务器 PositionSyncHandler 从 Room 存储切换到 EntityStore（MarkDirty → 既有 WorldSnapshot 链自动广播，RemotePlayerSync 双轨退役）；客户端 MovementReporter（节流上报+预测帧）+ NetMovementPolicy（注入面接管+平滑拉回）+ RemoteInterpolator（2-tick 延迟插值，时钟同步首次实战）。

**Tech Stack:** .NET 8 / Unity 2022.3（C#9）/ protobuf-net 3.2.56 / xUnit + NUnit(EditMode) + PlayMode

**Spec:** `docs/superpowers/specs/2026-08-27-network-sync-design.md` §5-P2 + §6（B+ 模型）+ §4 D1/D2/D5；P1 终审 MUST-DO-P2 六项

---

## 关键环境事实与执行决策（先读）

| 事实/决策 | 影响 |
|---|---|
| `PositionSyncHandler` 已有完整校验（MAX_SPEED=15/瞬移 50m/跳高 3m/纠偏下发）但更新 **Room 的 PlayerState** + 广播 RemotePlayerSync——**不触碰 EntityStore**（P1 终审 MUST-DO-1：快照链休眠，位置恒 (0,0,0)） | T3 核心接线 |
| `CharacterController.ApplyServerPosition(Vector3, Quaternion)`（L475）——P0 保留的纠偏入口（0.5 lerp rubber-band + 物理短暂禁用）；**P2 不采用**（NetMovementPolicy 用逐帧 blend 8/s 收敛更平滑、不动物理）——拉回执行在 T4 | T4 拉回执行 |
| `IMovementPolicy`/`LocalMovementPolicy` 注入面保留但**无调用方**（Sys3CEntry 直接调 `_cc.Update`） | T4 Sys3CEntry 注入点改造 |
| `MoveCommand`（MoveDir/Speed/Rotation/IsSprint）+ `CharacterData`（Position/Rotation/Velocity）就绪 | T4 数据源 |
| 协议：`PositionSyncRequest/Response` 已存在（P0）——**P2 零协议新增**；RemotePlayerSync 定义保留但 P2 退役（不再发送，只加不删） | T3 |
| `ClientNetworkManager` 已含主线程封送（pending 队列 Update drain）+ `ClockSyncDriver`（ToServerTime 可用） | T4/T5 挂点 |
| **决策 D1**：SyncCore 增加最小 `Vec3` 结构（x/y/z float + Distance/DistanceSqr/加减/标量乘）——共享校验需向量运算但 SyncCore 零引擎依赖；服务器 KcpServer.Vector3 与客户端 UnityEngine.Vector3 各自转换 | T1 |
| **决策 D2**：校验规则进 SyncCore（spec"两端同源"）——`MovementValidator.Validate` 纯函数；纠偏目标位置改进：原 `Vector3.forward`（固定 Z 轴，不按玩家朝向）改为 **next-prev 归一化方向**（真实移动方向） | T1 |
| **决策 D3**：Unreliable 评估（MUST-DO-4）结论 = **P2 位置流仍走 Reliable + 最新生效语义**（服务器端只保留最新位置；KCP 无真实不可靠通道，MessageFlags.Unreliable 位未实现）——记录到计划偏差表，P3 AOI 再评估 | T7 |
| **决策 D4**：per-observer 编码 O(n²)（MUST-DO-3）P2 不修（规模 OK，AOI P3 优化）；AddPlayer 标脏（MUST-DO-2）T3 落地；`_wireAction` 死存储（MUST-DO-6）T7 清理 | T3/T7 |
| **决策 D5**：时钟同步首次实战——远程插值渲染时刻 = `ClockSyncDriver.ToServerTime(本地墙钟) - 2×ServerTickRateMs`（基准 2 ticks，spec D1） | T5 |
| 预测帧上限 60（1s @66ms）；阈值沿用 P0 草稿（0.5m / 5°） | T2 |
| 服务器 EntityStore.UpdatePosition 已置脏（T4 P1）——接线后快照链自动激活 ✓；PositionSyncHandler 主循环线程（ProcessPacket）→ EntityStore 独占安全 ✓ | T3 |
| 测试基线：Server.Tests 39 绿；Unity EditMode 145 绿；PlayMode EchoSmoke/AdapterConnect/LoginEnterWorld/TwoClientWorld 绿 | 各任务验证基准 |

---

### Task 1: SyncCore Vec3 + MovementValidator（共享校验规则）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncCore/Vec3.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncCore/MovementValidator.cs`
- Test: `Server/Server.Tests/SyncCore/MovementValidatorTests.cs` + `Assets/Scripts/Tests/Editor/SyncCore/MovementValidatorTests.cs`

- [x] **Step 1: 写失败测试（服务器侧）**

创建 `Server/Server.Tests/SyncCore/MovementValidatorTests.cs`（完整内容）：

```csharp
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class MovementValidatorTests
{
    [Fact]
    public void Speed_Over_Limit_Clamped_And_Corrected()
    {
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(1, 0, 0),
            rotation: 0f, speed: 30f,
            prevTimestamp: 0, nextTimestamp: 1_000_000L);

        Assert.True(result.NeedsCorrection);
        Assert.Equal(MovementValidator.MaxSpeed, result.ClampedSpeed, 4);
    }

    [Fact]
    public void Teleport_Over_Limit_Corrected_To_Plausible_Position()
    {
        // 1 秒内移动 100m（远超 15m/s × 1.2 容差）→ 纠偏到 prev + 方向 × MaxSpeed × dt
        // dir = (100,0,0) 归一化 = (1,0,0)；权威位置 = 0 + (1,0,0) × 15 × 1.0s = (15, 0, 0)
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(100, 0, 0),
            rotation: 0f, speed: 5f,
            prevTimestamp: 0, nextTimestamp: 10_000_000L);

        Assert.True(result.NeedsCorrection);
        Assert.InRange(result.AuthoritativePosition.X, 14.9f, 15.1f); // 15 × 1.0s
    }

    [Fact]
    public void Teleport_Under_Distance_Threshold_Accepted()
    {
        // 0.1s 内移动 1m（合理）→ 无纠偏
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(1, 0, 0),
            rotation: 0f, speed: 5f,
            prevTimestamp: 0, nextTimestamp: 1_000_000L);

        Assert.False(result.NeedsCorrection);
        Assert.Equal(5f, result.ClampedSpeed, 4);
    }

    [Fact]
    public void Vertical_Jump_Over_Height_Corrected()
    {
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(0, 10, 0),
            rotation: 0f, speed: 0f,
            prevTimestamp: 0, nextTimestamp: 1_000_000L);

        Assert.True(result.NeedsCorrection);
    }

    [Fact]
    public void Zero_TimeDelta_No_Teleport_Check()
    {
        // 时间戳相同（时钟未走）→ 跳过瞬移判定，仅速度判定
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(100, 0, 0),
            rotation: 0f, speed: 5f,
            prevTimestamp: 100, nextTimestamp: 100);

        Assert.False(result.NeedsCorrection);
    }
}
```

- [x] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: CS0246（找不到 `Cool.SyncCore.Vec3`/`MovementValidator`）。

- [x] **Step 3: 实现 Vec3 与 MovementValidator**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncCore/Vec3.cs`（完整内容；C#9 块式 namespace）：

```csharp
namespace Cool.SyncCore
{
    /// <summary>
    /// 最小 3D 向量（共享校验规则用）——SyncCore 零引擎依赖，
    /// 两端各自与 UnityEngine.Vector3 / KcpServer.Vector3 转换。
    /// </summary>
    public readonly struct Vec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float DistanceSqr(Vec3 other)
        {
            float dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public float Distance(Vec3 other) => (float)System.Math.Sqrt(DistanceSqr(other));

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public float Magnitude => (float)System.Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>归一化（零向量返回自身）</summary>
        public Vec3 Normalized()
        {
            float mag = Magnitude;
            return mag < 1e-6f ? this : new Vec3(X / mag, Y / mag, Z / mag);
        }
    }
}
```

创建 `Assets/Scripts/Hotfix/GameSystems/SyncCore/MovementValidator.cs`（完整内容）：

```csharp
namespace Cool.SyncCore
{
    /// <summary>移动校验结果（决策 D1/D2：共享规则，两端同源）</summary>
    public readonly struct MoveValidationResult
    {
        public readonly bool NeedsCorrection;
        public readonly Vec3 AuthoritativePosition;
        public readonly float AuthoritativeRotation;
        public readonly float ClampedSpeed;

        public MoveValidationResult(bool needsCorrection, Vec3 authoritativePosition, float authoritativeRotation, float clampedSpeed)
        {
            NeedsCorrection = needsCorrection;
            AuthoritativePosition = authoritativePosition;
            AuthoritativeRotation = authoritativeRotation;
            ClampedSpeed = clampedSpeed;
        }
    }

    /// <summary>
    /// 移动校验规则（spec B+：客户端报位置 + 服务器合理性校验）——
    /// 判定逻辑从服务器 PositionSyncHandler 移植并共享；纠偏目标位置改进为
    /// 沿 next−prev 真实移动方向推算（原实现用固定 Z 轴，不按玩家朝向）。
    /// </summary>
    public static class MovementValidator
    {
        public const float MaxSpeed = 15f;
        public const float MaxTeleportDistance = 50f;
        public const float MaxJumpHeight = 3f;
        private const float SpeedToleranceFactor = 1.2f;

        /// <summary>
        /// 校验一条位置上报。
        /// prevTimestamp/nextTimestamp 单位：ticks（服务器时钟语义，spec §3）。
        /// </summary>
        public static MoveValidationResult Validate(
            Vec3 prev, Vec3 next, float rotation, float speed,
            long prevTimestamp, long nextTimestamp)
        {
            bool needsCorrection = false;
            Vec3 authoritative = next;
            float authoritativeRotation = rotation;
            float clampedSpeed = speed;

            // 1) 超速
            if (speed > MaxSpeed)
            {
                clampedSpeed = MaxSpeed;
                needsCorrection = true;
            }

            // 2) 瞬移（时间窗内距离超物理可能）
            long timeDelta = nextTimestamp - prevTimestamp;
            float timeDeltaSeconds = timeDelta / (float)System.TimeSpan.TicksPerSecond;
            if (timeDeltaSeconds > 0f)
            {
                float distance = prev.Distance(next);
                float maxPossibleDistance = MaxSpeed * timeDeltaSeconds * SpeedToleranceFactor;
                if (distance > maxPossibleDistance && distance > MaxTeleportDistance)
                {
                    needsCorrection = true;
                    // 纠偏目标：沿真实移动方向（next−prev 归一化）按最大可行速度推算
                    Vec3 dir = (next - prev).Normalized();
                    authoritative = prev + dir * (MaxSpeed * timeDeltaSeconds);
                }
            }

            // 3) 异常跳高（垂直位移超物理）
            float verticalDelta = next.Y - prev.Y;
            if (verticalDelta > MaxJumpHeight)
            {
                needsCorrection = true;
            }

            return new MoveValidationResult(needsCorrection, authoritative, authoritativeRotation, clampedSpeed);
        }
    }
}
```

> **已知限制（P0 移植语义，P3 反作弊再评估）**：瞬移判定为 `distance > MaxSpeed×dt×1.2 && distance > MaxTeleportDistance(50)` 双重门槛——正常 66ms~1s 上报窗口内 50m 绝对门槛主导，时间基方向纠偏只在单报 >50m 时生效；dt=0（时间戳未走）整段跳过（`Zero_TimeDelta_No_Teleport_Check` 固化该语义）。时间戳完全由客户端上报，恶意客户端冻结时间戳即可绕过瞬移检查——P2 不设防，P3 引入服务端接收时刻或单调计数。

- [x] **Step 4: 运行确认通过（服务器侧）**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 44 passed（39 + 5）。

- [x] **Step 5: Unity 侧同批断言（EditMode 双跑）**

创建 `Assets/Scripts/Tests/Editor/SyncCore/MovementValidatorTests.cs`（NUnit 同批 5 测试，命名/数值与 xUnit 一致；直接镜像上方内容改用 NUnit 断言——`Assert.IsTrue/IsFalse/AreEqual/GreaterOrEqual` 等，浮点精度同 4 位）：

```csharp
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class MovementValidatorTests
    {
        [Test]
        public void Speed_Over_Limit_Clamped_And_Corrected()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), 0f, 30f, 0, 1_000_000L);
            Assert.IsTrue(result.NeedsCorrection);
            Assert.AreEqual(MovementValidator.MaxSpeed, result.ClampedSpeed, 0.0001f);
        }

        [Test]
        public void Teleport_Over_Limit_Corrected_To_Plausible_Position()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(100, 0, 0), 0f, 5f, 0, 10_000_000L);
            Assert.IsTrue(result.NeedsCorrection);
            // 权威位置 = prev + dir × MaxSpeed × dt = 15 × 1.0s = 15.0（与 xUnit 版同口径；
            // 17.9~18.1 是把 1.2 容差系数混入权威位置计算的错误值）
            Assert.GreaterOrEqual(result.AuthoritativePosition.X, 14.9f);
            Assert.LessOrEqual(result.AuthoritativePosition.X, 15.1f);
        }

        [Test]
        public void Teleport_Under_Distance_Threshold_Accepted()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), 0f, 5f, 0, 1_000_000L);
            Assert.IsFalse(result.NeedsCorrection);
            Assert.AreEqual(5f, result.ClampedSpeed, 0.0001f);
        }

        [Test]
        public void Vertical_Jump_Over_Height_Corrected()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(0, 10, 0), 0f, 0f, 0, 1_000_000L);
            Assert.IsTrue(result.NeedsCorrection);
        }

        [Test]
        public void Zero_TimeDelta_No_Teleport_Check()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(100, 0, 0), 0f, 5f, 100, 100);
            Assert.IsFalse(result.NeedsCorrection);
        }
    }
}
```

1. Skill `assets-refresh` → 无编译错误（SyncCore 目录自动纳入——`GameSys.EditorTests.asmdef` 已引用 SyncCore，T5-P1 已改）
2. Skill `tests-run`(testMode=EditMode, testClass=MovementValidatorTests) → Expected 5 passed

- [x] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore Assets/Scripts/Tests/Editor/SyncCore Server/Server.Tests
git commit -m "feat(synccore): movement validator - shared speed/teleport/jump rules, dual-end tests green"
```

---

### Task 2: SyncCore PredictionReconciler（预测帧对账，共享）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncCore/PredictionReconciler.cs`
- Test: `Server/Server.Tests/SyncCore/PredictionReconcilerTests.cs` + `Assets/Scripts/Tests/Editor/SyncCore/PredictionReconcilerTests.cs`

- [x] **Step 1: 写失败测试（服务器侧）**

创建 `Server/Server.Tests/SyncCore/PredictionReconcilerTests.cs`（完整内容）：

```csharp
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class PredictionReconcilerTests
{
    [Fact]
    public void Deviation_Under_Threshold_No_Correction()
    {
        var reconciler = new PredictionReconciler();
        reconciler.RecordFrame(1, new Vec3(10, 0, 0), 0f);

        bool corrected = reconciler.TryGetCorrection(1, new Vec3(10.2f, 0, 0), 0f,
            out _, out _);

        Assert.False(corrected); // 0.2m < 0.5m 阈值
    }

    [Fact]
    public void Deviation_Over_Threshold_Returns_Server_Position()
    {
        var reconciler = new PredictionReconciler();
        reconciler.RecordFrame(2, new Vec3(10, 0, 0), 0f);

        bool corrected = reconciler.TryGetCorrection(2, new Vec3(20, 0, 0), 0f,
            out Vec3 targetPos, out float targetRot);

        Assert.True(corrected); // 10m > 0.5m
        Assert.Equal(20f, targetPos.X, 4);
        Assert.Equal(0f, targetRot, 4);
    }

    [Fact]
    public void Rotation_Deviation_Triggers_Correction()
    {
        var reconciler = new PredictionReconciler();
        reconciler.RecordFrame(3, new Vec3(0, 0, 0), 90f);

        bool corrected = reconciler.TryGetCorrection(3, new Vec3(0, 0, 0), 120f,
            out _, out float targetRot);

        Assert.True(corrected); // 30° > 5°
        Assert.Equal(120f, targetRot, 4);
    }

    [Fact]
    public void Unknown_Sequence_No_Correction()
    {
        var reconciler = new PredictionReconciler();
        reconciler.RecordFrame(1, new Vec3(0, 0, 0), 0f);

        bool corrected = reconciler.TryGetCorrection(99, new Vec3(50, 0, 0), 0f,
            out _, out _);

        Assert.False(corrected); // 无对应预测帧：不纠偏（首次连接/帧被清理）
    }

    [Fact]
    public void Prune_Removes_Old_Frames()
    {
        var reconciler = new PredictionReconciler();
        for (uint i = 1; i <= 70; i++)
            reconciler.RecordFrame(i, new Vec3(i, 0, 0), 0f);

        Assert.Equal(60, reconciler.FrameCount); // 上限 60

        reconciler.Prune(50);
        Assert.Equal(21, reconciler.FrameCount); // 保留 seq 50..70 共 21 帧
        Assert.False(reconciler.TryGetCorrection(10, new Vec3(0, 0, 0), 0f, out _, out _));
    }
}
```

- [x] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: CS0246（找不到 `PredictionReconciler`）。

- [x] **Step 3: 实现 PredictionReconciler**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncCore/PredictionReconciler.cs`（完整内容）：

```csharp
using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>
    /// 预测帧对账（spec B+：客户端预测 + 服务端校验）——
    /// 记录本地预测帧（seq → 位置/旋转），收到 ack 后按 seq 比对：
    /// 偏差 ≤ 阈值忽略；超阈值返回服务器权威目标（由客户端执行平滑拉回）。
    /// 纯 C# 共享；帧上限 60（1s @66ms 上报）。
    /// </summary>
    public sealed class PredictionReconciler
    {
        public const float PositionThreshold = 0.5f;
        public const float RotationThresholdDegrees = 5f;
        public const int MaxFrames = 60;

        private readonly Dictionary<uint, PredictedFrame> _frames = new Dictionary<uint, PredictedFrame>();

        public int FrameCount => _frames.Count;

        /// <summary>记录一帧本地预测（上报时调用）</summary>
        public void RecordFrame(uint sequence, Vec3 position, float rotationY)
        {
            _frames[sequence] = new PredictedFrame { Position = position, RotationY = rotationY };

            // 上限保护：超限清最旧（按 seq 最小）
            if (_frames.Count > MaxFrames)
            {
                uint oldest = uint.MaxValue;
                foreach (var seq in _frames.Keys)
                    if (seq < oldest) oldest = seq;
                _frames.Remove(oldest);
            }
        }

        /// <summary>
        /// 用服务器 ack 比对预测帧。返回 true 表示需要纠偏（out 参数为服务器权威目标）。
        /// 无对应帧或偏差 ≤ 阈值返回 false。
        /// </summary>
        public bool TryGetCorrection(uint serverSequence, Vec3 serverPosition, float serverRotationY,
            out Vec3 targetPosition, out float targetRotationY)
        {
            targetPosition = serverPosition;
            targetRotationY = serverRotationY;

            if (!_frames.TryGetValue(serverSequence, out var predicted))
                return false;

            float posDeviation = predicted.Position.Distance(serverPosition);
            float rotDeviation = AngleDelta(predicted.RotationY, serverRotationY);

            return posDeviation > PositionThreshold || rotDeviation > RotationThresholdDegrees;
        }

        /// <summary>清理 seq &lt; minSeq 的旧帧（收到新 ack 时调用，控制内存）</summary>
        public void Prune(uint minSeq)
        {
            var stale = new List<uint>();
            foreach (var kv in _frames)
                if (kv.Key < minSeq) stale.Add(kv.Key);
            foreach (var seq in stale)
                _frames.Remove(seq);
        }

        private static float AngleDelta(float a, float b)
        {
            float delta = (b - a) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta < 0f ? -delta : delta;
        }

        private struct PredictedFrame
        {
            public Vec3 Position;
            public float RotationY;
        }
    }
}
```

- [x] **Step 4: 运行确认通过（服务器侧）**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 49 passed（44 + 5）。

- [x] **Step 5: Unity 侧同批断言（EditMode）**

创建 `Assets/Scripts/Tests/Editor/SyncCore/PredictionReconcilerTests.cs`（NUnit 镜像 5 测试，命名/数值一致——`Assert.IsFalse/IsTrue/AreEqual`、`reconciler.FrameCount` 断言）。

1. Skill `assets-refresh` → 无编译错误
2. Skill `tests-run`(testMode=EditMode, testClass=PredictionReconcilerTests) → Expected 5 passed

- [x] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore Assets/Scripts/Tests/Editor/SyncCore Server/Server.Tests
git commit -m "feat(synccore): prediction reconciler - seq-keyed frame ledger, threshold correction, dual-end tests green"
```

---

### Task 3: 服务器接线（EntityStore + RemotePlayerSync 退役 + 注册标脏）

**Files:**
- Modify: `Server/Handler/PositionSyncHandler.cs`（EntityStore 接线 + MovementValidator 替换本地判定 + RemotePlayerSync 退役）
- Modify: `Server/Handler/LoginHandler.cs` 或 `Server/Server3C.cs`（RegisterEntity 标脏——MUST-DO-2）
- Test: `Server/Server.Tests/Server/PositionSyncEntityStoreTests.cs`

- [x] **Step 1: 写失败测试**

创建 `Server/Server.Tests/Server/PositionSyncEntityStoreTests.cs`（完整内容；验证：PositionSync 更新 EntityStore 实体 + 标脏 + 快照链可见）：

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class PositionSyncEntityStoreTests
{
    [Fact]
    public void PositionSync_Updates_EntityStore_And_Marks_Dirty()
    {
        // 最小闭环：EntityStore + PositionSyncHandler 注入依赖（不依赖 Server3C 全量）
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");

        var handler = new PositionSyncHandler(new Server3C(new TestLogger()), new TestLogger(), store);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 5f, Y = 0f, Z = 0f, Rotation = 90f, Speed = 4f,
            Timestamp = 10_000_000L, Sequence = 1U
        }, player);

        Assert.True(player.IsDirty, "位置更新后必须标脏（快照链激活的前提）");
        Assert.Equal(5f, player.Position.X, 4);
        Assert.Equal(90f, player.Rotation, 4);
    }

    [Fact]
    public void OverSpeed_Report_Corrected_To_EntityStore()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");
        player.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 0L, 0U);

        var handler = new PositionSyncHandler(new Server3C(new TestLogger()), new TestLogger(), store);
        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 1f, Y = 0f, Z = 0f, Rotation = 0f, Speed = 99f,
            Timestamp = 1_000_000L, Sequence = 1U
        }, player);

        Assert.Equal(MovementValidator.MaxSpeed, player.Speed, 4); // 速度被钳制
        Assert.True(player.IsDirty);
    }

}
```

注意：`PositionSyncHandler` 现签名 `(Server3C, RoomManager, ILogger)`——测试需要新签名。**设计修正**：`PositionSyncHandler` 重构为注入 `EntityStore`（核心校验+应用逻辑与 Server3C/RoomManager 解耦），构造函数定为 **`(Server3C server, ILogger logger, EntityStore entityStore)`**（Server3C 注入 = LoginHandler 同款惯例，见 Step 3 代码——唯一权威签名）；原 HandlePositionSyncAsync 保留为薄壳（session 解析 → 调 ValidateAndApply → 回 ack）。Server3C 构造对应改。**测试构造统一 `new Server3C(new TestLogger())`**（Server3C 不 StartAsync ✓——T9-P1 已验证该模式）。

- [x] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: CS0246（新签名测试引用）/ CS1729（构造不匹配）。

- [x] **Step 3: 重构 PositionSyncHandler**

读 `Server/Util/Vector3.cs` 核对构造后，改写 `Server/Handler/PositionSyncHandler.cs`（完整内容）：

```csharp
using System;
using System.Threading.Tasks;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// 位置同步处理器 —— 校验（共享 MovementValidator）+ EntityStore 应用（P2 接线：
    /// 快照链激活；RemotePlayerSync 双轨退役，广播统一走 WorldSnapshot）。
    /// 主循环线程调用（ProcessPacket）。
    /// </summary>
    public class PositionSyncHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;
        private readonly EntityStore _entityStore;

        // Server3C 注入（LoginHandler 同款惯例——项目既有模式，不用委托 seam）
        public PositionSyncHandler(Server3C server, ILogger logger, EntityStore entityStore)
        {
            _server = server;
            _logger = logger;
            _entityStore = entityStore;
        }

        /// <summary>核心校验+应用（可独立测试——不依赖 Server3C 运行时）</summary>
        public MoveValidationResult ValidateAndApply(PositionSyncRequest request, PlayerState player)
        {
            var result = MovementValidator.Validate(
                new Vec3(player.Position.X, player.Position.Y, player.Position.Z),
                new Vec3(request.X, request.Y, request.Z),
                request.Rotation, request.Speed,
                player.LastUpdateTimestamp, request.Timestamp);

            player.UpdatePosition(
                new Vector3(result.AuthoritativePosition.X, result.AuthoritativePosition.Y, result.AuthoritativePosition.Z),
                result.AuthoritativeRotation,
                result.ClampedSpeed,
                request.Timestamp, request.Sequence);
            // UpdatePosition 已置 IsDirty（P1 T4）——WorldSnapshot 链自动携带

            if (result.NeedsCorrection)
                _logger.LogWarning($"Player {player.PlayerId} corrected: speed={request.Speed}");

            return result;
        }

        public async Task HandlePositionSyncAsync(KcpServerSession session, PositionSyncRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
            {
                _logger.LogWarning($"PositionSync from unknown session {session.SessionId}");
                return;
            }

            var player = _entityStore.GetPlayer(playerId.Value);
            if (player == null)
            {
                _logger.LogWarning($"Player {playerId} entity not found");
                return;
            }

            var result = ValidateAndApply(request, player);

            var response = new PositionSyncResponse
            {
                AcknowledgedSequence = request.Sequence,
                ServerTimestamp = DateTime.UtcNow.Ticks,
                HasPositionCorrection = result.NeedsCorrection,
                AuthoritativeX = result.AuthoritativePosition.X,
                AuthoritativeY = result.AuthoritativePosition.Y,
                AuthoritativeZ = result.AuthoritativePosition.Z,
                AuthoritativeRotation = result.AuthoritativeRotation
            };

            await _server.SendToSessionAsync(session, MessageId.PositionSyncResponse, response);
        }
    }
}
```

**Server3C.csproj 补 SyncCore glob**（应用工程编译共享规则——Server.Tests.csproj 已含，应用工程漏了；缺则 T3/T7 的 `dotnet build Server3C.csproj` 报 CS0246 `Cool.SyncCore`）：

```xml
<ItemGroup>
  <Compile Include="..\Assets\Scripts\Hotfix\GameSystems\SyncCore\**\*.cs" />
</ItemGroup>
```

`Server3C.cs`：构造改为 `_positionSyncHandler = new PositionSyncHandler(this, logger, _entityStore);`（`_entityStore` 字段已在 P1 T4 存在 ✓；Server3C 构造不启动——测试可 `new Server3C(new TestLogger())` 后注入，T9-P1 已验证该模式）。

**测试文件对应调整**：`PositionSyncEntityStoreTests` 构造 `new PositionSyncHandler(new Server3C(new TestLogger()), new TestLogger(), store)`（Server3C 不 StartAsync ✓）；`Vec3ToKcp` 辅助已从 Step-1 代码删除（直接 `new Vector3(0, 0, 0)`——`Server/Util/Vector3.cs` 构造签名已核对：`Vector3(float x, float y, float z)` + X/Y/Z 可写属性）。

**RemotePlayerSync 广播退役**：原 HandlePositionSyncAsync 中的 RemotePlayerSync 广播整段删除（WorldSnapshot 链接管——EntityStore MarkDirty → SnapshotAssembler 每 tick 组装）。`RemoteSync.cs` 协议定义保留（只加不删）。

**RoomSync 登录下发退役（双轨清理一致）**：`LoginHandler.HandleLoginAsync` 中的 RoomSync 发送段删除（Room 存储的 PlayerState 位置在 T3 后成为陈旧数据源——新玩家看已在线玩家靠 EntityStore 注册标脏 → 下一 tick WorldSnapshot 广播 ✓；RoomSync 协议定义保留）。`Room.GetAllRemotePlayerSync` 方法已删除（本任务使其成孤儿——执行修正 e3de605；协议定义仍保留）。

**RegisterEntity 标脏（MUST-DO-2）**：`Server3C.RegisterEntity` 改为：

```csharp
        public void RegisterEntity(long playerId, string playerName)
        {
            var player = _entityStore.AddPlayer(playerId, playerName);
            _entityStore.MarkDirty(player); // 注册即标脏：首个快照立即携带新实体（MUST-DO-P2）
        }
```

- [x] **Step 4: 运行确认通过 + 构建**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
```

Expected: 51 passed（49 + 2）；0 Errors。

- [x] **Step 5: 回归（TwoClientWorldTests 仍绿——断链风险检查）**

起服务器 → Skill `tests-run`(PlayMode, testClass=TwoClientWorldTests) → 1 passed（双客户端实体互见依赖 EntityStore 注册 + 快照广播——本任务接线后链路未变）→ 停服。

- [x] **Step 6: Commit**

```bash
git add Server
git commit -m "feat(server): position sync to entity store - shared validator, snapshot chain live, legacy broadcast retired"
```

---

### Task 4: 客户端 MovementReporter + NetMovementPolicy + Sys3CEntry 注入

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetMovementPolicy.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`（policy 注入点 + CoreController 暴露）
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`（PositionSyncResponse 订阅 + MovementReporter 装配）

- [x] **Step 1: 实现 MovementReporter**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs`（完整内容；节流上报 + 预测帧记录）：

```csharp
using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpNet;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 移动上报器 —— 本地角色即时模拟后的节流上报（spec B+）：
    /// 每 ServerTickRateMs 上报一次 PositionSyncRequest（seq 递增）；
    /// 预测帧交共享 PredictionReconciler；响应驱动纠偏查询。
    /// 驱动点：ClientNetworkManager.Update（宿主每帧调）——与测试宿主解耦，
    /// 不依赖真实 NetMovementPolicy。
    /// </summary>
    public sealed class MovementReporter
    {
        private readonly INetworkClient _client;
        private readonly Func<Vec3> _positionProvider;
        private readonly Func<float> _rotationProvider;
        private readonly Func<float> _speedProvider;
        private readonly PredictionReconciler _reconciler = new PredictionReconciler();
        private float _timer;
        private uint _sequence;

        /// <summary>最近一次上报的服务器时钟（纠偏用）</summary>
        public long LastServerTimestamp { get; private set; }

        public MovementReporter(INetworkClient client,
            Func<Vec3> positionProvider, Func<float> rotationProvider, Func<float> speedProvider)
        {
            _client = client;
            _positionProvider = positionProvider;
            _rotationProvider = rotationProvider;
            _speedProvider = speedProvider;
        }

        /// <summary>每帧驱动；reportIntervalSeconds = ServerTickRateMs/1000（默认 66ms）</summary>
        public void Tick(float deltaSeconds, float reportIntervalSeconds)
        {
            _timer -= deltaSeconds;
            if (_timer > 0f) return;

            _timer = reportIntervalSeconds;
            Report();
        }

        public void Report()
        {
            if (!_client.IsConnected) return;

            var pos = _positionProvider();
            float rot = _rotationProvider();
            uint seq = ++_sequence;

            _reconciler.RecordFrame(seq, pos, rot);

            _client.Send(MessageId.PositionSyncRequest, new PositionSyncRequest
            {
                X = pos.X, Y = pos.Y, Z = pos.Z,
                Rotation = rot,
                Speed = _speedProvider(),
                Timestamp = DateTime.UtcNow.Ticks,
                Sequence = seq
            });
        }

        /// <summary>收到 PositionSyncResponse 时调用——返回是否需要纠偏（超阈值）</summary>
        public bool OnResponse(PositionSyncResponse response)
        {
            LastServerTimestamp = response.ServerTimestamp;
            _reconciler.Prune(response.AcknowledgedSequence);
            return _reconciler.TryGetCorrection(
                response.AcknowledgedSequence,
                new Vec3(response.AuthoritativeX, response.AuthoritativeY, response.AuthoritativeZ),
                response.AuthoritativeRotation,
                out _targetPosition, out _targetRotation);
        }

        public Vec3 LastCorrectionTarget => _targetPosition;
        public float LastCorrectionRotation => _targetRotation;

        private Vec3 _targetPosition;
        private float _targetRotation;
    }
}
```

- [x] **Step 2: 实现 NetMovementPolicy**

创建 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetMovementPolicy.cs`（完整内容；平滑拉回）：

```csharp
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.SyncLayer;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 网络移动策略（spec B+：本地即时模拟 + 服务端校验平滑纠偏）——
    /// Update 驱动本地物理 + 节流上报；纠偏按偏移衰减收敛（不瞬移）。
    /// </summary>
    public class NetMovementPolicy : IMovementPolicy
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _controller;
        private bool _correcting;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private const float CorrectionBlendPerSecond = 8f; // 每秒收敛速度

        // 不持有 MovementReporter——上报节流由 ClientNetworkManager.Update 驱动（与测试宿主解耦）
        public NetMovementPolicy(Hotfix.GameSystems.Sys3C.Character.CharacterController controller)
        {
            _controller = controller;
        }

        public void Update(MoveCommand command)
        {
            _controller.Update(command);
            // 上报节流由 ClientNetworkManager.Update 驱动（与测试宿主解耦）——
            // policy 只管本地物理 + 拉回

            // 平滑拉回：每帧向纠偏目标收敛（偏移衰减，无跳变）
            if (_correcting)
            {
                var current = _controller.Transform.position;
                var next = Vector3.Lerp(current, _targetPosition, CorrectionBlendPerSecond * Time.deltaTime);
                _controller.Transform.position = next;
                _controller.Transform.rotation = Quaternion.Slerp(
                    _controller.Transform.rotation, _targetRotation, CorrectionBlendPerSecond * Time.deltaTime);

                if (Vector3.Distance(next, _targetPosition) < 0.05f)
                    _correcting = false;
            }
        }

        public void ApplyServerCorrection(Vector3 position, Quaternion rotation)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            _correcting = true;
        }
    }
}
```

- [x] **Step 3: Sys3CEntry 注入点改造**

`Sys3CEntry.cs`：
- 新增字段：`private IMovementPolicy _movementPolicy;`
- 新增公开属性（网络宿主装配用）：`public Hotfix.GameSystems.Sys3C.Character.CharacterController CoreController => _cc;` 与 `public IMovementPolicy MovementPolicy { get => _movementPolicy; set => _movementPolicy = value; }`
- `Start()` 末尾：`_movementPolicy = new LocalMovementPolicy(_cc);`
- `Update()` 中 `_cc.Update(command);` 改为 `_movementPolicy.Update(command);`

- [x] **Step 4: ClientNetworkManager 装配**

`ClientNetworkManager.cs`：
- 订阅 `MessageId.PositionSyncResponse` → `OnPositionSyncResponse`：
```csharp
        private MovementReporter? _reporter;

        public MovementReporter? Reporter => _reporter;

        /// <summary>
        /// 装配移动上报（进世界后由宿主调用；传入本地角色数据源）。
        /// speedProvider 必须反映真实移动速度（超速钳制链路依赖它——固定值会使纠偏测不到）。
        /// 宿主接线：manager.OnCorrectionNeeded += (pos, rot) => policy.ApplyServerCorrection(pos, rot);
        /// </summary>
        public void AttachMovementReporter(Func<Cool.SyncCore.Vec3> positionProvider,
            Func<float> rotationProvider, Func<float> speedProvider)
        {
            _reporter = new MovementReporter(_client, positionProvider, rotationProvider, speedProvider);
            _dispatcher.Subscribe(MessageId.PositionSyncResponse, OnPositionSyncResponse);
        }

        private readonly System.Collections.Concurrent.ConcurrentQueue<PositionSyncResponse> _pendingResponses =
            new System.Collections.Concurrent.ConcurrentQueue<PositionSyncResponse>();

        private void OnPositionSyncResponse(IMessage message)
        {
            // 回调线程（网络驱动）：只入队——对账字典（PredictionReconciler._frames）与主线程
            // RecordFrame 并发会竞态；且纠偏事件会触发 Unity Transform 写（必须主线程）。
            // 纪律同 WorldSnapshot 封送（OnWorldSnapshot 同款模式）。
            _pendingResponses.Enqueue((PositionSyncResponse)message);
        }

        /// <summary>主线程消费纠偏响应（Update 内、上报 Tick 前调用）</summary>
        private void DrainResponses()
        {
            while (_pendingResponses.TryDequeue(out var response))
            {
                if (_reporter != null && _reporter.OnResponse(response))
                {
                    // 需要纠偏 → 上层经 NetMovementPolicy.ApplyServerCorrection 执行平滑拉回
                    OnCorrectionNeeded?.Invoke(
                        new Vector3(response.AuthoritativeX, response.AuthoritativeY, response.AuthoritativeZ),
                        Quaternion.Euler(0f, response.AuthoritativeRotation, 0f));
                }
            }
        }

        public event Action<Vector3, Quaternion>? OnCorrectionNeeded;

        // Update 内（快照 drain 后）：
        //     DrainResponses();   // 纠偏响应封送（主线程对账 + 事件）
        //     _reporter?.Tick(deltaSeconds, ServerTickRateMs / 1000f);
```

（`OnCorrectionNeeded` 由宿主接线到 NetMovementPolicy.ApplyServerCorrection。）

- [x] **Step 5: 验证编译**

1. Skill `assets-refresh` → 无编译错误
2. Skill `tests-run`(testMode=EditMode 全量) → 全绿（145 + MovementValidator 5 + PredictionReconciler 5 = 155）
3. `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj` → 51 passed

- [x] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetMovementPolicy.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat(synclayer): movement reporter + net movement policy - throttled reporting, prediction frames, smooth correction"
```

---

### Task 5: RemoteInterpolator（快照缓冲 + 2-tick 延迟插值）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteInterpolator.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（ApplySnapshot 改为喂插值缓冲）
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`（Update 驱动插值渲染）

- [x] **Step 1: 实现 RemoteInterpolator**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteInterpolator.cs`（完整内容）：

```csharp
using System.Collections.Generic;
using Cool.SyncCore;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程实体插值器（spec D1：渲染时钟落后 2 ticks）——
    /// 快照缓冲按服务器时钟排序，渲染时刻 = now − renderDelayTicks，前后快照插值。
    /// 时钟换算经 ClockSyncDriver.ToServerTime（D5 决策）。
    /// </summary>
    public sealed class RemoteInterpolator
    {
        private readonly Dictionary<long, List<Sample>> _buffers = new Dictionary<long, List<Sample>>();
        private const int MaxSamplesPerEntity = 16;

        /// <summary>
        /// 入样本。serverTicks = 所属 WorldSnapshot.ServerTimestamp（**绝对服务器时钟**——
        /// 与 ClockSyncDriver.ToServerTime 同基准；不得用 tick 序号换算的相对基准，否则插值错乱）
        /// </summary>
        public void AddSample(long playerId, long serverTicks, Vec3 position, float rotationY)
        {
            if (!_buffers.TryGetValue(playerId, out var samples))
            {
                samples = new List<Sample>();
                _buffers[playerId] = samples;
            }
            samples.Add(new Sample { ServerTicks = serverTicks, Position = position, RotationY = rotationY });
            if (samples.Count > MaxSamplesPerEntity)
                samples.RemoveAt(0);
        }

        /// <summary>计算渲染状态：nowServerTicks 为换算后的服务器时钟，renderDelayTicks 为渲染延迟</summary>
        public bool TryGetRenderState(long playerId, long nowServerTicks, long renderDelayTicks,
            out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (!_buffers.TryGetValue(playerId, out var samples) || samples.Count == 0)
                return false;

            long renderAt = nowServerTicks - renderDelayTicks;

            // 最新样本仍早于渲染时刻 → 用最新（快照不足场景）
            if (renderAt >= samples[samples.Count - 1].ServerTicks)
            {
                var last = samples[samples.Count - 1];
                position = ToUnity(last.Position);
                rotation = Quaternion.Euler(0f, last.RotationY, 0f);
                return true;
            }

            // 找包围渲染时刻的相邻样本插值
            for (int i = 0; i < samples.Count - 1; i++)
            {
                var a = samples[i];
                var b = samples[i + 1];
                if (renderAt >= a.ServerTicks && renderAt <= b.ServerTicks)
                {
                    long span = b.ServerTicks - a.ServerTicks;
                    float t = span <= 0 ? 0f : (float)(renderAt - a.ServerTicks) / span;
                    position = Vector3.LerpUnclamped(ToUnity(a.Position), ToUnity(b.Position), t);
                    rotation = Quaternion.Slerp(Quaternion.Euler(0f, a.RotationY, 0f), Quaternion.Euler(0f, b.RotationY, 0f), t);
                    return true;
                }
            }

            // 渲染时刻早于全部样本 → 用最旧（时钟尚未追平）
            var first = samples[0];
            position = ToUnity(first.Position);
            rotation = Quaternion.Euler(0f, first.RotationY, 0f);
            return true;
        }

        public void Remove(long playerId) => _buffers.Remove(playerId);

        public void Clear() => _buffers.Clear();

        private static Vector3 ToUnity(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private struct Sample
        {
            public long ServerTicks;
            public Vec3 Position;
            public float RotationY;
        }
    }
}
```

- [x] **Step 2: RemoteEntityRegistry 集成**

`RemoteEntityRegistry.cs` 改造（**设计：interpolator 内部 new、ctor 保持 1 参**——P1 既有测试 LoginEnterWorldTests/TwoClientWorldTests 零波纹；插值器是纯实现细节）：
- 构造内部创建 `RemoteInterpolator`
- `ApplySnapshot` 改为喂缓冲：
- `Remove`/`Clear` 同步清理插值缓冲；新增 `GetView(long)`（T6 验收用）

```csharp
        private readonly RemoteInterpolator _interpolator = new RemoteInterpolator();

        public RemoteEntityRegistry(Transform root)
        {
            _root = root;
        }

        /// <summary>
        /// 快照入插值缓冲（不再直接定位）。serverTicks = 所属 WorldSnapshot.ServerTimestamp
        /// （绝对服务器时钟——同批实体同值，插值时间基准统一）
        /// </summary>
        public void ApplySnapshot(EntitySnapshot snapshot, long serverTicks)
        {
            if (!_views.TryGetValue(snapshot.PlayerId, out var view))
            {
                view = CreateView(snapshot.PlayerId);
                _views[snapshot.PlayerId] = view;
            }

            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Position) != 0)
            {
                _interpolator.AddSample(snapshot.PlayerId, serverTicks,
                    new Cool.SyncCore.Vec3(snapshot.X, snapshot.Y, snapshot.Z), snapshot.Rotation);
            }
        }

        /// <summary>每帧渲染（nowServerTicks 换算后；renderDelayTicks = 2×ServerTickRateMs 换算）</summary>
        public void UpdateRender(long nowServerTicks, long renderDelayTicks)
        {
            foreach (var kv in _views)
            {
                if (_interpolator.TryGetRenderState(kv.Key, nowServerTicks, renderDelayTicks,
                    out var pos, out var rot))
                {
                    kv.Value.Position = pos;
                    kv.Value.Rotation = rot;
                }
            }
        }

        public void Remove(long playerId)
        {
            if (_views.TryGetValue(playerId, out var view))
            {
                Object.Destroy(view.GameObject);
                _views.Remove(playerId);
                _interpolator.Remove(playerId); // 插值缓冲同步清理（防重连同 ID 残留旧样本）
            }
        }

        public void Clear()
        {
            foreach (var view in _views.Values)
                Object.Destroy(view.GameObject);
            _views.Clear();
            _interpolator.Clear();
        }

        /// <summary>读远程实体视图（T6 验收断言用；无视图返回 null）</summary>
        public RemoteEntityView? GetView(long playerId)
            => _views.TryGetValue(playerId, out var view) ? view : null;
```

注意：快照时间统一用 `WorldSnapshot.ServerTimestamp`（**绝对服务器时钟**，与 `ClockSyncDriver.ToServerTime` 同基准——A8 修正）。**不要**用 `EntitySnapshot.Tick × ServerTickRateMs` 换算相对基准（tick 序号是服务器启动后纪元，与渲染时刻的绝对时钟永不对齐 → 插值退化为贴最新）。渲染延迟换算用 `ServerTickRateMs × 10_000 × 2`（ClientNetworkManager 已存 ServerTickRateMs）。

- [x] **Step 3: ClientNetworkManager 驱动**

`ClientNetworkManager.cs`：
- **`OnWorldSnapshot` 修改**（P1 既有方法）：`_remoteEntities.ApplySnapshot(entity)` → `_remoteEntities.ApplySnapshot(entity, snapshot.ServerTimestamp)`（批级绝对服务器时钟——A8 修正）
- 构造增加 `RemoteInterpolator`（或由宿主创建传入 registry 时带）——**设计**：`RemoteEntityRegistry` 自带 `RemoteInterpolator`（内部 new），暴露 `UpdateRender`；ClientNetworkManager.Update 里调用：

```csharp
        private long _lastRenderDriveTicks;

        /// <summary>时钟平滑偏移（ticks）——T6 时钟抖动实测读</summary>
        public long ClockSmoothedOffsetTicks => _clockSync.SmoothedOffsetTicks;

        // Update 内（快照 drain 后）：
            // 插值渲染（服务器时钟基准：ClockSyncDriver.ToServerTime(本地墙钟) − 2 ticks 延迟）
            long nowServerTicks = _clockSync.ToServerTime(System.DateTime.UtcNow.Ticks);
            long renderDelayTicks = ServerTickRateMs * 10_000L * 2; // 2 ticks
            _remoteEntities.UpdateRender(nowServerTicks, renderDelayTicks);
```

（`_remoteEntities.UpdateRender` 转发到内部 interpolator。）

- [x] **Step 4: 验证编译**

1. Skill `assets-refresh` → 无编译错误
2. Skill `tests-run`(testMode=EditMode 全量) → 155 绿
3. 起服务器 → Skill `tests-run`(PlayMode, testClass=LoginEnterWorldTests) → 1 passed（回归）→ 停服

- [x] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer
git commit -m "feat(synclayer): remote interpolator - snapshot buffer, 2-tick render delay, clock-synced interpolation"
```

---

### Task 6: 端到端验收（移动互见 + 纠偏 + 时钟实测）

**Files:**
- Create: `Assets/Scripts/Tests/PlayMode/MovementSyncTests.cs`
- Modify: `Assets/Scripts/Tests/PlayMode/TwoClientWorldTests.cs`（适配插值语义——实体存在断言不变，位置断言从 (0,0,0) 变为真实移动）

- [x] **Step 1: 写 PlayMode 移动互见测试**

创建 `Assets/Scripts/Tests/PlayMode/MovementSyncTests.cs`（完整内容；A 移动 → B 插值看到 A 真实位置变化）：

```csharp
using System.Collections;
using Cool.Protocol;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P2 核心验收：A 移动（本地模拟 + 上报）→ 服务器校验 → B 快照 → B 插值渲染看到 A 移动；
    /// A 单报瞬移 100m → 服务器纠偏 → A 收到纠偏事件、B 看不到瞬移位置（作弊拉回链路）。
    /// </summary>
    public class MovementSyncTests
    {
        private sealed class MoveContext
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
            public Hotfix.GameSystems.Sys3C.Character.CharacterController Controller = null!;
            public Vector3 LocalPosition;
            public bool CorrectionSeen;
        }

        [UnityTest]
        public IEnumerator A_Moves_B_Sees_Interpolated_Movement()
        {
            var runTask = RunAsync();
            int frames = 0;
            while (!runTask.IsCompleted && frames < 3600)
            {
                frames++;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "movement test timed out");
            var (ok, reason) = runTask.Result;
            Assert.IsTrue(ok, reason);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunAsync()
        {
            // —— 构造两个客户端（A 带本地角色模拟，B 观察）——
            var root = new GameObject("MoveRoot");
            var registryB = new RemoteEntityRegistry(root.transform);

            var ctxA = new MoveContext();
            ctxA.Registry = new RemoteEntityRegistry(root.transform);
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, ctxA.Registry, "Move_A");

            var ctxB = new MoveContext();
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, registryB, "Move_B");

            // A 的本地角色（纯模拟：不用真实地面/物理——直接驱动位置）
            ctxA.LocalPosition = Vector3.zero;
            ctxA.Manager.AttachMovementReporter(
                () => new Cool.SyncCore.Vec3(ctxA.LocalPosition.x, ctxA.LocalPosition.y, ctxA.LocalPosition.z),
                () => 0f,
                () => 4f); // 正常速度（纠偏链路另有超速用例）

            // A 端纠偏观察
            ctxA.Manager.OnCorrectionNeeded += (pos, rot) => ctxA.CorrectionSeen = true;

            try
            {
                // 双客户端登录
                ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxA.Manager)) return (false, "A login failed");
                ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxB.Manager)) return (false, "B login failed");

                // B 已能看到 A 实体（登录注册）
                bool bSeesA = false;
                for (int i = 0; i < 40 && !bSeesA; i++)
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    bSeesA = ctxB.Registry.Count > 0;
                }
                if (!bSeesA) return (false, "B never saw A entity");

                // —— A 移动 3 秒（模拟：每帧向前 +0.1，同时驱动上报）——
                for (int i = 0; i < 60; i++)
                {
                    ctxA.LocalPosition += Vector3.forward * 0.1f;
                    ctxA.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(50);
                }

                // —— B 端应看到 A 实体位置显著变化（插值渲染生效，非恒 (0,0,0)）——
                bool bSeesMovement = false;
                for (int i = 0; i < 40 && !bSeesMovement; i++)
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    // 从 registry 读 A 实体视图位置
                    var view = registryB.GetView(ctxA.Manager.LocalPlayerId);
                    if (view != null && view.Position.z > 1f) // A 移动了 6m，B 应看到 > 1m
                        bSeesMovement = true;
                }
                if (!bSeesMovement) return (false, "B never saw A's movement (interpolation inactive?)");

                return (true, "ok");
            }
            finally
            {
                ctxA.Manager.Dispose();
                ctxB.Manager.Dispose();
                Object.Destroy(root);
            }
        }

        private static async System.Threading.Tasks.Task<bool> WaitInWorld(ClientNetworkManager manager)
        {
            float elapsed = 0f;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
            {
                manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }
            return manager.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
```

（`RemoteEntityRegistry.GetView(long)` 已在 T5 Step 2 补：`public RemoteEntityView? GetView(long playerId) => _views.TryGetValue(playerId, out var v) ? v : null;`）

**asmdef**：`KcpNet.PlayModeTests.asmdef` references 增加 `"SyncCore"`（测试全限定引用 `Cool.SyncCore.Vec3` 需程序集引用——当前仅 KcpNet/Cool.Protocol/GameSys，缺则 PlayMode 编译失败）。

- [x] **Step 1b: 作弊瞬移纠偏用例（终审补——原"超速观察"在 T6 落空）**

`MovementSyncTests` 追加 `A_Teleports_Server_Corrects_B_Smoothes`（完整内容）：

```csharp
        [UnityTest]
        public IEnumerator A_Teleports_Server_Corrects_B_Smoothes()
        {
            var runTask = RunTeleportAsync();
            int frames = 0;
            while (!runTask.IsCompleted && frames < 3600)
            {
                frames++;
                yield return null;
            }
            Assert.IsTrue(runTask.IsCompleted, "teleport test timed out");
            var (ok, reason) = runTask.Result;
            Assert.IsTrue(ok, reason);
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunTeleportAsync()
        {
            // —— 同 A_Moves 构造：双客户端登录，A 带模拟位置 + 上报 ——
            var root = new GameObject("MoveRoot");
            var registryB = new RemoteEntityRegistry(root.transform);

            var ctxA = new MoveContext();
            ctxA.Registry = new RemoteEntityRegistry(root.transform);
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, ctxA.Registry, "Move_A");

            var ctxB = new MoveContext();
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, registryB, "Move_B");

            ctxA.LocalPosition = Vector3.zero;
            ctxA.Manager.AttachMovementReporter(
                () => new Cool.SyncCore.Vec3(ctxA.LocalPosition.x, ctxA.LocalPosition.y, ctxA.LocalPosition.z),
                () => 0f,
                () => 4f);
            ctxA.Manager.OnCorrectionNeeded += (pos, rot) => ctxA.CorrectionSeen = true;

            try
            {
                ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxA.Manager)) return (false, "A login failed");
                ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(ctxB.Manager)) return (false, "B login failed");

                // 正常移动 ~0.5s 建立服务器 prev 基准
                for (int i = 0; i < 10; i++)
                {
                    ctxA.LocalPosition += Vector3.forward * 0.1f;
                    ctxA.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(50);
                }

                // 单帧瞬移 100m（模拟作弊/穿墙）→ 服务器纠偏到 prev + 15×dt ≈ 数米
                // → ack 权威位置 ≠ 预测帧 → 纠偏事件必须触发
                ctxA.LocalPosition += Vector3.forward * 100f;
                ctxA.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);

                bool correctionSeen = false;
                for (int i = 0; i < 40 && !correctionSeen; i++)
                {
                    ctxA.Manager.Update(0.05f); // 驱动响应封送（DrainResponses 主线程对账）
                    await System.Threading.Tasks.Task.Delay(100);
                    correctionSeen = ctxA.CorrectionSeen;
                }
                if (!correctionSeen) return (false, "A never got correction event (teleport not caught)");

                // B 端观察：A 被纠偏回 prev 附近（数米内），绝看不到 100m 瞬移位。
                // 注意：此后不再驱动 ctxA.Manager.Update（冻结上报——否则每报重触发纠偏、
                // 服务器权威位置以 15m/s 爬升直到 50m 门槛放行，断言窗口会漂）
                bool bNeverSawTeleport = false;
                for (int i = 0; i < 40 && !bNeverSawTeleport; i++)
                {
                    ctxB.Manager.Update(0.05f);
                    await System.Threading.Tasks.Task.Delay(100);
                    var view = registryB.GetView(ctxA.Manager.LocalPlayerId);
                    if (view != null && view.Position.z < 10f) // 权威 ≈ prev+15×dt ≤ 数米
                        bNeverSawTeleport = true;
                }
                if (!bNeverSawTeleport) return (false, "B saw the teleport position (correction not applied)");

                return (true, "ok");
            }
            finally
            {
                ctxA.Manager.Dispose();
                ctxB.Manager.Dispose();
                Object.Destroy(root);
            }
        }
```

- [x] **Step 2: 时钟 ±10ms 实测记录（MUST-DO-5）**

在 `MovementSyncTests` 末尾（或独立测试）追加时钟抖动实测——记录而非断言（spec 口径"记录实测值"）：

```csharp
        [UnityTest]
        public IEnumerator Clock_Sync_Jitter_Recorded()
        {
            var runTask = RecordClockJitterAsync();
            int frames = 0;
            while (!runTask.IsCompleted && frames < 3600) { frames++; yield return null; }
            Assert.IsTrue(runTask.IsCompleted, "clock test timed out");
        }

        private static async System.Threading.Tasks.Task RecordClockJitterAsync()
        {
            var root = new GameObject("ClockRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            using var adapter = new KcpNetClientAdapter();
            var manager = new ClientNetworkManager(adapter, registry, "Clock_Tester");
            try
            {
                manager.ConnectAndLogin("127.0.0.1", 8000);
                if (!await WaitInWorld(manager)) return;

                // 10s 采样：记录 SmoothedOffsetTicks 的 max−min 抖动
                long min = long.MaxValue, max = long.MinValue;
                for (int i = 0; i < 200; i++)
                {
                    manager.Update(0.05f);
                    long v = manager.ClockSmoothedOffsetTicks;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    await System.Threading.Tasks.Task.Delay(50);
                }
                long jitterTicks = max - min;
                Debug.Log($"[ClockSync] jitter={jitterTicks} ticks ({(jitterTicks / 10_000f):F2}ms) over 10s");
                // ±10ms 口径记录（不硬断言——局域网实测参考；日志留档）
                Assert.Less(jitterTicks, 500_000L, "clock jitter should be < 50ms on loopback (recorded value in log)");
            }
            finally
            {
                manager.Dispose();
                Object.Destroy(root);
            }
        }
```

（`ClientNetworkManager.ClockSmoothedOffsetTicks` 已在 T5 Step 3 添加。）

- [x] **Step 3: 冒烟验收**

1. Bash 后台起服务器 → 确认 running
2. Skill `assets-refresh` → 无编译错误
3. Skill `tests-run`(PlayMode, testClass=MovementSyncTests) → 3 passed（移动互见 + 瞬移纠偏 + 时钟实测）
4. **连跑 2 次**确认非 flaky
5. 拉服务器日志确认 PositionSync 链（A 上报 → 校验 → ack）
6. 停服；netstat 确认释放

- [x] **Step 4: 全量回归**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 51 passed。Unity EditMode 全量 → 155 绿。

- [x] **Step 5: Commit**

```bash
git add Assets/Scripts/Tests/PlayMode Assets/Scripts/Hotfix/GameSystems/SyncLayer
git commit -m "test(net): p2 acceptance - movement seen across clients via interpolation, clock jitter recorded"
```

---

### Task 7: 收尾（_wireAction 清理 / Unreliable 评估记录 / 延迟注入文档）

**Files:**
- Modify: `Server/KcpServerSession.cs`（`_wireAction` 死存储清理——MUST-DO-6）
- Modify: `docs/superpowers/plans/2026-08-28-p2-movement-sync.md`（偏差记录——终审后由协调者写）
- Modify: `docs/superpowers/guides/p1-dual-client-dev.md`（P2 移动验证追加）

- [x] **Step 1: _wireAction 清理**

`Server/KcpServerSession.cs`：`EnsureWired` 中 `_wireAction` 字段删除（从未读取——P1 重连走全新 Login/新会话，无需重接线）：

```csharp
        private bool _wired;

        /// <summary>幂等接线：保证事件订阅先于任何包处理（消除发布-订阅窗口竞态）</summary>
        public void EnsureWired(Action wireAction)
        {
            lock (_sendLock)
            {
                if (_wired) return;
                _wired = true;
                wireAction();
            }
        }
```

（删除 `private Action? _wireAction;` 字段与其赋值。）

- [x] **Step 2: Unreliable 评估记录（MUST-DO-4）**

在 `docs/superpowers/guides/p1-dual-client-dev.md` 追加段：

```markdown
## P2 移动同步验证（2026-08-28）

- A 移动 → B 插值可见（MovementSyncTests；插值渲染延迟 = 2 ticks，时钟同步驱动）
- **Unreliable 评估结论（MUST-DO-4 关闭）**：P2 位置流保持 Reliable + 最新生效语义
  （服务器端只保留最新位置；KCP 无真实不可靠通道，MessageFlags.Unreliable 位未实现）。
  P3 AOI 压测时如带宽超标再评估。
- 延迟注入工具：PlayMode 测试用 `Task.Delay` 在 MovementReporter.Report 前模拟
  上行延迟（0~300ms 手测档位见 MovementSyncTests 变体或调试宿主）
```

- [x] **Step 3: 延迟注入验收（spec §5-P2 验收项）**

PlayMode 追加测试（`MovementSyncTests` 变体或独立）：注入 200ms 上行延迟 → 断言 A 本地移动不被打断（本地即时模拟不受网控——`_controller.Update` 直接执行）+ B 端插值仍平滑（无跳变——插值缓冲兜底）。**简化落地**：在 `MovementSyncTests` 中加一个 `A_Moves_With_Injected_Delay_Still_Local_Smooth` 测试：MovementReporter 包延迟包装（`client.Send` 前 await Task.Delay(200)——通过 adapter 层模拟：测试内联一个延迟 Send 包装器实现 INetworkClient？重。**最小**：MovementReporter 构造增加可选 `int injectedSendDelayMs = 0`，Report 中 `if (injectedSendDelayMs > 0) System.Threading.Thread.Sleep(injectedSendDelayMs)`——测试传 200。断言：A 的 LocalPosition 持续增长（本地模拟不被延迟影响）+ B 在延迟期间仍看到旧位置（插值兜底）→ 延迟解除后 B 追上。

（**若此变体复杂度超标**：以"本地即时模拟不受网控"为验收下限——`A_Moves_With_Injected_Delay_Local_Unaffected`：注入 200ms，断言 A 本地位置在 2s 内推进 ≥ 1.5m（无延迟影响），B 端最终收敛。按此落地，报告说明。）

- [x] **Step 4: 验证 + 全量**

1. `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj` → 51 passed
2. `dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q` → 0 Errors
3. Skill `assets-refresh` + `tests-run`(EditMode 全量) → 155 绿
4. 起服务器 → `tests-run`(PlayMode, testClass=MovementSyncTests) → 4 passed → 停服

- [x] **Step 5: Commit**

```bash
git add Server/KcpServerSession.cs Assets/Scripts/Tests/PlayMode docs/superpowers/guides/p1-dual-client-dev.md
git commit -m "chore(net): p2 polish - wireaction dead storage removed, unreliable eval recorded, injected-delay acceptance"
```

---

## Self-Review（计划撰写时已自查）

**1. Spec 覆盖**：§5-P2 验收项 → 移动互见（T6 MovementSyncTests）/本地手感不发飘（T7 延迟注入）/对端平滑位移（T5 插值 + T6 验收）/作弊瞬移拉回（T1 共享校验 + T3 接线 + T6 瞬移纠偏用例——单报 100m → 纠偏事件 + B 不见瞬移位）；§4 D1（2-tick 插值延迟 + ServerTickRateMs）→ T5；D2（最新生效语义）→ T3/决策 D3；D5（增量位掩码已有）→ 复用；B+ 模型（§6）→ T2/T4 全链。P1 MUST-DO-P2 六项 → T3（接线/标脏）、T4/T7（_wireAction）、T6（时钟实测）、T7（Unreliable 评估）、per-observer 编码记录（决策 D4）。**缺项检查**：PositionSyncResponse 的 ack→纠偏链路 → T4 ✓；预测帧对账 → T2/T4 ✓；RemotePlayerSync 退役 → T3 ✓；ClockSync 实战 → T5 ✓。

**2. 占位符扫描**：无 TBD/TODO；代码块完整。两处"按现场适配"（Vector3 构造核对、延迟注入变体取舍）为容错指引。

**3. 类型一致性**：`Vec3`（SyncCore）在 T1 定义、T2/3/4/5 六处使用一致（构造/运算符）；`MoveValidationResult`（T1）在 T3 服务器消费一致；`PredictionReconciler.RecordFrame(seq, Vec3, float)` 签名在 T2 定义、T4 Predicter 调用一致；`MovementReporter.OnResponse` 返回 bool + `LastCorrectionTarget` 与 T4 纠偏链路一致；`RemoteInterpolator.TryGetRenderState(playerId, nowServerTicks, renderDelayTicks, out pos, out rot)` 在 T5 定义、T6 经 UpdateRender 调用一致；快照时间统一 `WorldSnapshot.ServerTimestamp`（绝对时钟）在 T5 定义、T6 一致，渲染延迟 `ServerTickRateMs × 10_000 × 2` 同基准。

## 规模说明

7 个任务：共享规则（T1-T2）、服务器接线（T3）、客户端闭环（T4-T5）、端到端验收（T6）、收尾（T7）。P2 零协议新增（PositionSync 消息 P0 已备）——风险集中在接线正确性与插值时间换算，两者均有针对性测试。

## 执行偏差记录（2026-09-09 落地，coordinator 批准）

全部 7 任务完成（commits 9602cb6 → 2473a67 + 收尾 9b1xxxx）。相对计划文本的实际偏差：

**评审驱动修正（per-task code review 两阶段发现的，均已落地 + 回归测试锁定）：**
1. MovementValidator 非有限输入守卫（∞/NaN 网络包 → 纠偏回 prev，防 NaN 广播）——双端回归测试
2. PredictionReconciler 类注释线程契约 + 非有限 ack 守卫——mutation 验证的回归测试
3. T3：PositionSyncHandler rotation NaN 回退（validator 守卫原样透传 rotation 会进 store/ack/广播）——含 ack sink 同值回退 + 回归测试；`Room.GetAllRemotePlayerSync` 删除（本任务成孤儿）；纠偏日志 cause-aware
4. T4：AttachMovementReporter 重入守卫（双订阅 + 旧会话残留响应污染）；NetMovementPolicy 终止条件含旋转（Quaternion.Angle < 5°）；`_reportInterval`/`SetReportInterval`/reporter 依赖删除（yagni）；Sys3CEntry Start 预置守卫；LastCorrection* 语义文档；IMovementPolicy 注释更新
5. T5：beacon yaw 毒化修复（Position-only beacon 的 Rotation 是 protobuf 默认 0——last-known yaw carry，按位掩码契约）；RemoteInterpolator 内部持有（ctor 保持 1 参——P1 测试零波纹，与 Step 3 注释一致）；Remove/Clear 清插值缓冲；GetView 并入 T5
6. T6：3600 帧上限 → **60s 墙钟上限**（编辑器 400-6000fps 下帧数上限只够 0.6-4s 墙钟，场景需 8-20s——TwoClientWorldTests 已有"帧数上限不可靠"教训）；A_Teleports B 断言改 **max-observed-z < 60m**（产品保证：权威沿 prev+15m/s 走廊爬升，seen 时 ≤50m 门槛内；原 <10f 落在爬升噪声带）；`ctxB.Registry` NRE 修正（计划文本 bug——MoveContext.Registry 只给 ctxA 赋值，改读局部 registryB）；补 RemoteInterpolatorTests EditMode 6 测试（A_Moves 无法区分插值与贴最新）；Clock 测试注释对齐
7. T7：延迟注入 **全版变体落地**（A 本地推进 ≥1.5m + B 稀疏上报收敛——连跑 2 次稳定，未用逃生口版）；`ClientNetworkManager.AttachMovementReporter` 增加 optional `injectedSendDelayMs` 透传（测试 4 参调用编译需要）；guide MessageFlags 措辞修正（无 Unreliable 位，仅 Reserved 占位）

**测试计数最终值（计划各 Step 期望为基线推导，实际以最终为准）：** Server.Tests 54（39 基线 + 15：validator 6/reconciler 6/entity-store 3）；EditMode 163（145 基线 + 18：validator 6/reconciler 6/interpolator 6）；PlayMode MovementSyncTests 4 测试 3 连跑全绿（时钟抖动实测 5.8~12.5ms，远低于 50ms 红线）；TwoClientWorldTests/LoginEnterWorldTests 回归绿（1 参 ctor 未破坏）。

**遗留跟踪（final review Minor，未在本期执行）：**
- `LoginResponse.ServerTickRateMs` 服务器从未填写（客户端 fallback 67 vs 服务器 66ms——1.5% 偏差，benign；P3 顺手填或删字段）
- `Room.UpdatePlayerPosition`/`Server/AI NpcComponent` 读 Room 位置——AI 服务器接线时改喂 EntityStore（P3）
- `PlayerState.LastValidPosition/RecordValidPosition/AcknowledgeSequence` 死 API（P0/P1 遗留）
- RemoteEntityRegistry 断线重连不清视图（P1 gap，离线期间幽灵残留至服务器会话超时）
- 游戏内宿主接线（真实场景 AttachMovementReporter + NetMovementPolicy 切换）仍无调用方——验收由测试宿主覆盖，P3 世界场景接线时落地

## 终审修正（2026-08-31 第二轮，对照代码复核）

1. **T1 NUnit 断言数学修正**：`Teleport_Over_Limit` 断言 `X ∈ [17.9, 18.1]` → `[14.9, 15.1]`（17.9~18.1 是 1.2 容差系数混入权威位置计算的错误值；实际 = 15×1.0s = 15.0，xUnit 版本已正确）
2. **T1 补已知限制注释**：瞬移双重门槛（dt<2.78s 时 50m 绝对门槛主导）+ dt=0 绕过 + 客户端时间戳可控——P3 反作弊再评估
3. **T3 构造函数签名统一**：唯一权威签名 `(Server3C, ILogger, EntityStore)`（Server3C 注入 = LoginHandler 惯例）；Step-1 测试构造改 3 参；删 2 参设计残句与 `Vec3ToKcp` 辅助
4. **T3 补 Server3C.csproj SyncCore glob**：应用工程漏编译共享规则（Server.Tests.csproj 已含）——否则 T3/T7 `dotnet build Server3C.csproj` CS0246
5. **T4 纠偏响应改主线程封送**：PositionSyncResponse 回调线程只入队 pending 队列，Update 内 DrainResponses——修对账字典（PredictionReconciler._frames）并发竞态 + 纠偏事件 Unity Transform 非主线程调用（纪律同 WorldSnapshot 封送）
6. **T4 NetMovementPolicy 去死成员**：删 `_reportInterval`/`SetReportInterval`/`MovementReporter` 依赖（上报节流由 manager.Update 驱动）
7. **T4 事实表修正**：ApplyServerPosition（L475）改标"P2 不采用"（P0 入口为 0.5 lerp rubber-band + 物理短暂禁用；P2 用逐帧 blend 8/s 更平滑、不动物理）
8. **T5 RemoteEntityRegistry 统一内部 new interpolator、ctor 保持 1 参**：P1 既有测试（LoginEnterWorldTests/TwoClientWorldTests 1 参构造）零波纹；`Remove`/`Clear` 同步清理插值缓冲；`GetView(long)` 并入 T5 Step 2（原标"T5 里补"但 T5 清单没有）
9. **T5 删 tick×interval 矛盾注释**：统一 `WorldSnapshot.ServerTimestamp` 绝对服务器时钟基准（A8 修正——上次终审修的 time-basis 错误以注释形式回潮，执行者照注释改会退回"贴最新"退化）
10. **T5 ClientNetworkManager 补 `ClockSmoothedOffsetTicks`**（原标"T7 补"落空——T7 无该改动）
11. **T6 补瞬移纠偏 PlayMode 用例**（`A_Teleports_Server_Corrects_B_Smoothes`）：`CorrectionSeen` 从死字段变验收；注意超速钳制不会触发客户端纠偏（权威=自报位置偏差 0），真正触发链的是单报瞬移
12. **T6 asmdef 补漏**：`KcpNet.PlayModeTests.asmdef` references 加 `"SyncCore"`（测试全限定 `Cool.SyncCore.Vec3` 需程序集引用，缺则 PlayMode 编译失败）；测试构造改 1 参
13. **计数更新**：T6 MovementSyncTests 3 passed（+瞬移纠偏）、T7 全量 4 passed；Server.Tests 基线 39 ✓（实测）
