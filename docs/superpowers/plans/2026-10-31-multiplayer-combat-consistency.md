# 联网多人战斗一致性（演示级）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers-zh:subagent-driven-development（推荐）或 superpowers-zh:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让所有在场真人玩家看到彼此的攻击/受击/格挡动作、技能特效、伤害数字与血条；联网怪物有名字/血条/选中反馈；远程投射物在服务端命中点爆炸/停住；玩家可显式锁定目标。

**架构：** 状态 + 事件双通道。`EntitySnapshot` 纯追加 `Hp/MaxHp/IsDefending`；`DamageResult` 扩播给 AOI 观察者；`CastBroadcast` 追加落点/蓄力。客户端新增 `AnimationCallbackRegistry` 隔离本地/远程 Animator，`RemoteEntityAnimator` 做表现仲裁（快照状态与瞬时事件分离），`RemoteCastPresenter` 立即播放远程施法并按 `(casterId, skillId)` 跟踪投射物视觉，`NetworkTargetSelector` 贯通 `TargetPlayerId`。

**技术栈：** Unity 2022.3.25f1（WebGL + IL2CPP）· protobuf-net 3.2.56 · .NET 8 服务端 · NUnit（Unity EditMode/PlayMode）· xUnit（Server.Tests）· 既有 KCP/WebSocket 传输。

**设计规格：** `docs/superpowers/specs/2026-10-31-multiplayer-combat-consistency-design.md`（实现时以规格为准；本计划是它的任务化展开）。

---

## 文件结构

| 动作 | 文件 | 职责 |
|---|---|---|
| 创建 | `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AnimationCallbackRegistry.cs` | Animator → 本地动画完成回调/受击标志 |
| 修改 | `.../StateBehaviours/AttackStateBehaviour.cs` | 改为按 Animator 查表回调 |
| 修改 | `.../StateBehaviours/BaseStateBehaviour.cs` | 同上（JumpEnd） |
| 修改 | `.../StateBehaviours/HitStateBehaviour.cs` | 受击完成标志按 Animator 隔离 |
| 修改 | `.../Sys3C/FSM/FSMManager.cs` | 构造时注册，移除静态 SetCallback |
| 修改 | `.../Sys3C/Sys3CEntry.cs` | OnDestroy 注销 |
| 修改 | `Assets/Scripts/Tests/Editor/HitAnimatorContractTests.cs` | 改用注册表 |
| 创建 | `Assets/Scripts/Tests/Editor/SyncLayer/AnimationCallbackRegistryTests.cs` | 注册/隔离/清理 |
| 修改 | `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs` | HP/MaxHP/IsDefending + 掩码位 |
| 修改 | `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` | CastBroadcast 落点/蓄力 |
| 修改 | `Server/Aoi/AoiBroadcaster.cs` | 快照填充 HP/防御 |
| 修改 | `Server/Aoi/AoiBandScheduler.cs` | HP/防御变化立即发 |
| 修改 | `Server/Handler/CastHandler.cs` | DamageResult 扩播、CastBroadcast 落点 |
| 修改 | `Server/Handler/LoginHandler.cs` | 已有玩家名字名册 |
| 修改 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs` | 表现仲裁器 + 死亡序列 |
| 创建 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetable.cs` | 只读 ITargetable |
| 创建 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/PlayerNameRegistry.cs` | playerId → 名字 |
| 创建 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/INetworkLabelSink.cs` | 名牌/血条注入接口 |
| 修改 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | vitals/名字/标签/View 事件 |
| 修改 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs` | PlayerEnterRoom 名册/队列 |
| 修改 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 接线 presenter/selector/label/catalog |
| 修改 | `Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs` | 远程来源命中火花（保留镜头/顿帧守卫） |
| 修改 | `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateRenderer.cs` | 带血条模板 + 第二池 |
| 修改 | `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs` | RegisterWithHealth/UpdateHealth |
| 创建 | `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillPresentationCatalog.cs` | 7 个同步技能表现目录 |
| 创建 | `Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset` | 目录资产 |
| 修改 | `.../Skills/Runtime/SwordQi/SwordQiSpawner.cs` | Spawn 返回 projectile |
| 修改 | `.../Skills/Runtime/Projectile/FireballSpawner.cs` | Spawn 返回 projectile |
| 修改 | `.../Skills/Runtime/Projectile/FireballProjectile.cs` | IsActive/ExplodeAt |
| 创建 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs` | 远程施法表现/投射物跟踪 |
| 创建 | `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs` | 目标锁定/切换 |
| 修改 | `.../SyncLayer/SkillCastReporter.cs` | targetId 参数 |
| 修改 | `.../SyncLayer/CombatNetworkBridge.cs` | targetId/PlayHit/Targetable HP |
| 修改 | `.../Sys3C/Input/InputManager.cs` | T 键 + UI 标志 |
| 修改 | `.../Sys3C/Input/MobileInputBinder.cs` | 目标切换按钮 |
| 修改 | `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` | 怪物名/目录/按钮 |
| 创建 | `Assets/Scripts/Tests/PlayMode/ThreeClientCombatConsistencyTests.cs` | 三客户端旁观验收 |
| 修改 | `Server/Server.Tests/Aoi/BandwidthBudgetTests.cs` | 新快照字段带宽 |

---

## 任务 1：StateMachineBehaviour 按 Animator 隔离

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AnimationCallbackRegistry.cs`
- 修改：`.../StateBehaviours/AttackStateBehaviour.cs`、`BaseStateBehaviour.cs`、`HitStateBehaviour.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 修改：`Assets/Scripts/Tests/Editor/HitAnimatorContractTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/AnimationCallbackRegistryTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/SyncLayer/AnimationCallbackRegistryTests.cs`：

```csharp
using System;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class AnimationCallbackRegistryTests
    {
        private GameObject _a = null!;
        private GameObject _b = null!;
        private Animator _animA = null!;
        private Animator _animB = null!;

        [SetUp]
        public void SetUp()
        {
            AnimationCallbackRegistry.ClearForTests();
            _a = new GameObject("AnimA");
            _b = new GameObject("AnimB");
            _animA = _a.AddComponent<Animator>();
            _animB = _b.AddComponent<Animator>();
        }

        [TearDown]
        public void TearDown()
        {
            AnimationCallbackRegistry.ClearForTests();
            UnityEngine.Object.DestroyImmediate(_a);
            UnityEngine.Object.DestroyImmediate(_b);
        }

        [Test]
        public void Register_Stores_Attack_And_Hit_Callbacks()
        {
            string attack = null;
            string hit = null;
            AnimationCallbackRegistry.Register(_animA, s => attack = s, s => hit = s);

            Assert.IsTrue(AnimationCallbackRegistry.TryGet(_animA, out var entry));
            entry.Attack?.Invoke("Attack1");
            entry.Hit?.Invoke("Hit");

            Assert.AreEqual("Attack1", attack);
            Assert.AreEqual("Hit", hit);
        }

        [Test]
        public void Unregistered_Animator_Does_Not_Invoke_Local_Callbacks()
        {
            int localCalls = 0;
            AnimationCallbackRegistry.Register(_animA, _ => localCalls++, _ => localCalls++);

            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animB, out _),
                "未注册的远程 Animator 必须查不到条目——否则远程动画会调用本地 FSM");
            Assert.AreEqual(0, localCalls);
        }

        [Test]
        public void Register_Resets_Hit_Flags_For_The_Same_Animator()
        {
            AnimationCallbackRegistry.Register(_animA, null, null);
            Assert.IsTrue(AnimationCallbackRegistry.TryGet(_animA, out var entry));
            entry.HasTriggeredHitComplete = true;
            entry.LastNormalizedTime = 0.95f;

            AnimationCallbackRegistry.Register(_animA, null, null); // 重装配/角色重建

            Assert.IsTrue(AnimationCallbackRegistry.TryGet(_animA, out var reset));
            Assert.IsFalse(reset.HasTriggeredHitComplete);
            Assert.AreEqual(0f, reset.LastNormalizedTime);
        }

        [Test]
        public void Unregister_Is_Idempotent()
        {
            AnimationCallbackRegistry.Register(_animA, null, null);
            AnimationCallbackRegistry.Unregister(_animA);
            AnimationCallbackRegistry.Unregister(_animA);
            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animA, out _));
        }

        [Test]
        public void ClearForTests_Removes_All_Entries()
        {
            AnimationCallbackRegistry.Register(_animA, null, null);
            AnimationCallbackRegistry.Register(_animB, null, null);
            AnimationCallbackRegistry.ClearForTests();
            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animA, out _));
            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animB, out _));
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode AnimationCallbackRegistryTests`
预期：编译失败，`AnimationCallbackRegistry` 不存在。

- [ ] **步骤 3：实现注册表**

创建 `AnimationCallbackRegistry.cs`：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    /// <summary>
    /// 动画完成回调按 **Animator 实例** 注册。控制器资产被本地与远程角色共用；
    /// 旧的静态回调会让远程动画调用本地 FSMManager（远程攻击打断本地施法、
    /// 远程受击提前完成本地 HitFSM）。远程 Animator 查不到条目 → 天然不触发。
    /// </summary>
    public static class AnimationCallbackRegistry
    {
        internal sealed class Entry
        {
            public Action<string>? Attack;
            public Action<string>? Hit;
            public bool HasTriggeredHitComplete;
            public float LastNormalizedTime;
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();

        public static void Register(Animator animator, Action<string>? attack, Action<string>? hit)
        {
            if (animator == null) return;
            Entries[animator.GetInstanceID()] = new Entry
            {
                Attack = attack,
                Hit = hit,
                HasTriggeredHitComplete = false,
                LastNormalizedTime = 0f
            };
        }

        public static void Unregister(Animator animator)
        {
            if (animator == null) return;
            Entries.Remove(animator.GetInstanceID());
        }

        internal static bool TryGet(Animator animator, out Entry entry)
        {
            entry = null!;
            return animator != null && Entries.TryGetValue(animator.GetInstanceID(), out entry!);
        }

        public static void ClearForTests() => Entries.Clear();
    }
}
```

- [ ] **步骤 4：改三个 StateMachineBehaviour**

`AttackStateBehaviour.cs` 删除静态 `_onAnimationCompleted` / `SetCallback`，改为：

```csharp
override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
{
    if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
    if (IsPlayingClip(stateInfo) && stateInfo.normalizedTime >= 0.95f && stateInfo.normalizedTime < 1.1f)
        entry.Attack?.Invoke(GetStateName(stateInfo));
}

override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
{
    if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
    if (IsPlayingClip(stateInfo)) entry.Attack?.Invoke(GetStateName(stateInfo));
}
```

`BaseStateBehaviour.cs` 同样删除静态回调/`SetCallback`；`OnStateUpdate` 内改为：

```csharp
if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
if (stateInfo.shortNameHash == HASH_JumpEnd && stateInfo.normalizedTime >= 0.9f)
    entry.Attack?.Invoke("JumpEnd");
```

`HitStateBehaviour.cs` 删除三个静态字段与 `SetCallback`，把标志放进条目：

```csharp
override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
{
    if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
    if (IsHitState(stateInfo.shortNameHash))
    {
        entry.HasTriggeredHitComplete = false;
        entry.LastNormalizedTime = 0f;
    }
}

override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
{
    if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
    int stateHash = stateInfo.shortNameHash;
    if (IsHitState(stateHash) && !entry.HasTriggeredHitComplete)
    {
        if (stateInfo.normalizedTime >= 0.9f && stateInfo.normalizedTime > entry.LastNormalizedTime)
        {
            entry.HasTriggeredHitComplete = true;
            entry.Hit?.Invoke(GetStateName(stateHash));
        }
        entry.LastNormalizedTime = stateInfo.normalizedTime;
    }
}

override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
{
    if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
    if (IsHitState(stateInfo.shortNameHash))
    {
        entry.HasTriggeredHitComplete = false;
        entry.LastNormalizedTime = 0f;
    }
}
```

- [ ] **步骤 5：改 FSMManager 与 Sys3CEntry**

`FSMManager` 构造里删除：

```csharp
BaseStateBehaviour.SetCallback(HandleAnimationCompleted);
AttackStateBehaviour.SetCallback(HandleAnimationCompleted);
HitStateBehaviour.SetCallback(HandleHitAnimationCompleted);
```

替换为：

```csharp
AnimationCallbackRegistry.Register(_animator, HandleAnimationCompleted, HandleHitAnimationCompleted);
```

在 `Sys3CEntry.OnDestroy()` 内追加：

```csharp
Hotfix.GameSystems.Sys3C.Animation.StateBehaviours.AnimationCallbackRegistry.Unregister(Animator);
```

- [ ] **步骤 6：改 HitAnimatorContractTests**

把 `HitStateBehaviour.SetCallback(null)` 改为 `AnimationCallbackRegistry.ClearForTests()`；死亡序列测试里把 `HitStateBehaviour.SetCallback(fsm.OnAnimationEnd)` 改为 `AnimationCallbackRegistry.Register(animator, null, fsm.OnAnimationEnd)`，`finally` 里改为 `AnimationCallbackRegistry.Unregister(animator)`。

- [ ] **步骤 7：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode AnimationCallbackRegistryTests`
- `bash tools/unity-tests.sh EditMode HitAnimatorContractTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿；`HitAnimatorContractTests` 的死亡链仍通过。

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C Assets/Scripts/Tests/Editor
git commit -m "fix(3c): scope animation callbacks per Animator

The controller asset is shared by local and remote avatars, but the three
StateMachineBehaviours used static callbacks and static hit flags. Driving
remote attack/hit states therefore called the local FSMManager - a remote
attack could interrupt the local skill, and a remote hit could complete the
local hit FSM early.

Callbacks and hit-completion flags now live in a registry keyed by Animator
instance. Remote avatars never register, so their animations are inert;
Sys3CEntry unregisters on destroy. HitAnimatorContractTests switches to the
registry; the death-chain contract stays green."
```

---

## 任务 2：协议字段（HP / 防御 / 落点 / 蓄力）

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Combat.cs`
- 测试：`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs`
- 测试：`Server/Server.Tests/Protocol/CodecRoundtripTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `ProtocolShapeGuardTests.cs` 追加：

```csharp
[Test]
public void EntitySnapshot_Carries_Health_And_Defending_With_Mask_Bits()
{
    var hp = typeof(Cool.Protocol.EntitySnapshot).GetProperty("Hp");
    var maxHp = typeof(Cool.Protocol.EntitySnapshot).GetProperty("MaxHp");
    var def = typeof(Cool.Protocol.EntitySnapshot).GetProperty("IsDefending");
    Assert.IsNotNull(hp); Assert.IsNotNull(maxHp); Assert.IsNotNull(def);
    Assert.AreEqual(typeof(float), hp!.PropertyType);
    Assert.AreEqual(typeof(float), maxHp!.PropertyType);
    Assert.AreEqual(typeof(bool), def!.PropertyType);
    Assert.AreEqual(13, GetProtoMemberTag(hp));
    Assert.AreEqual(14, GetProtoMemberTag(maxHp));
    Assert.AreEqual(15, GetProtoMemberTag(def));
    Assert.IsTrue(System.Enum.IsDefined(typeof(Cool.Protocol.EntityFieldMask), (ushort)(1 << 7)),
        "Health 必须占 1 << 7");
    Assert.IsTrue(System.Enum.IsDefined(typeof(Cool.Protocol.EntityFieldMask), (ushort)(1 << 8)),
        "Defending 必须占 1 << 8");
}

[Test]
public void CastBroadcast_Carries_Impact_And_Charge()
{
    var x = typeof(Cool.Protocol.CastBroadcast).GetProperty("ImpactX");
    var y = typeof(Cool.Protocol.CastBroadcast).GetProperty("ImpactY");
    var z = typeof(Cool.Protocol.CastBroadcast).GetProperty("ImpactZ");
    var c = typeof(Cool.Protocol.CastBroadcast).GetProperty("ChargeProgress");
    Assert.IsNotNull(x); Assert.IsNotNull(y); Assert.IsNotNull(z); Assert.IsNotNull(c);
    Assert.AreEqual(10, GetProtoMemberTag(x!));
    Assert.AreEqual(11, GetProtoMemberTag(y!));
    Assert.AreEqual(12, GetProtoMemberTag(z!));
    Assert.AreEqual(13, GetProtoMemberTag(c!));
}
```

在 `Server/Server.Tests/Protocol/CodecRoundtripTests.cs` 追加：

```csharp
[Fact]
public void Roundtrip_WorldSnapshot_Preserves_Health_And_Defending()
{
    var source = new WorldSnapshot
    {
        Tick = 7u,
        ServerTimestamp = 123L,
        Entities =
        {
            new EntitySnapshot
            {
                PlayerId = 2L,
                FieldMask = (ushort)(EntityFieldMask.Health | EntityFieldMask.Defending),
                Hp = 42.5f,
                MaxHp = 100f,
                IsDefending = true,
                Tick = 7u
            }
        }
    };

    var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, source);
    var (_, decoded) = MessageCodec.Decode(bytes);
    var snapshot = Assert.IsType<WorldSnapshot>(decoded);
    var entity = Assert.Single(snapshot.Entities);
    Assert.Equal(42.5f, entity.Hp, 4);
    Assert.Equal(100f, entity.MaxHp, 4);
    Assert.True(entity.IsDefending);
}

[Fact]
public void Roundtrip_CastBroadcast_Preserves_Impact_And_Charge()
{
    var source = new CastBroadcast
    {
        SkillId = 20004, CasterPlayerId = 3L,
        ImpactX = 1f, ImpactY = 0f, ImpactZ = 9f,
        ChargeProgress = 0.75f,
        Shape = new HitShape { ShapeType = 1, AreaRadius = 2f }
    };

    var bytes = MessageCodec.EncodeToArray(MessageId.CastBroadcast, source);
    var (_, decoded) = MessageCodec.Decode(bytes);
    var cast = Assert.IsType<CastBroadcast>(decoded);
    Assert.Equal(1f, cast.ImpactX, 4);
    Assert.Equal(9f, cast.ImpactZ, 4);
    Assert.Equal(0.75f, cast.ChargeProgress, 4);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：
- `bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests`
- `bash tools/server-test.sh --filter "FullyQualifiedName~CodecRoundtripTests"`（项目脚本是 `bash tools/test-server.sh`）

预期：编译失败（字段不存在）。

- [ ] **步骤 3：实现协议字段**

`WorldSnapshot.cs` 的 `EntityFieldMask` 在 `Dead = 1 << 6` 后追加：

```csharp
/// <summary>Hp / MaxHp 有效（只加不删：新字段追加高位）</summary>
Health = 1 << 7,
/// <summary>IsDefending 有效</summary>
Defending = 1 << 8
```

`EntitySnapshot` 在 `Dead` 后追加：

```csharp
/// <summary>服务端权威血量（FieldMask.Health 置位时有效）</summary>
[ProtoMember(13)] public float Hp { get; set; }
/// <summary>服务端权威最大血量（FieldMask.Health 置位时有效）</summary>
[ProtoMember(14)] public float MaxHp { get; set; }
/// <summary>是否防御中（FieldMask.Defending 置位时有效）</summary>
[ProtoMember(15)] public bool IsDefending { get; set; }
```

`Combat.cs` 的 `CastBroadcast` 在 `ServerTimestamp`（tag 9）后追加：

```csharp
[ProtoMember(10)] public float ImpactX { get; set; }
[ProtoMember(11)] public float ImpactY { get; set; }
[ProtoMember(12)] public float ImpactZ { get; set; }
[ProtoMember(13)] public float ChargeProgress { get; set; }
```

- [ ] **步骤 4：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests`
- `bash tools/test-server.sh --filter "FullyQualifiedName~CodecRoundtripTests"`

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/Protocol Server/Server.Tests/Protocol
git commit -m "feat(protocol): carry vitals on entity snapshots and impact data on casts

EntitySnapshot gains Hp/MaxHp/IsDefending behind two new mask bits (13/14/15,
1<<7/1<<8). CastBroadcast gains ImpactX/Y/Z and ChargeProgress (10..13).
All additions are pure protobuf appends; existing tags and values are pinned
by ProtocolShapeGuardTests."
```

---

## 任务 3：服务端快照带 HP/防御，变化立即下发

**文件：**
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Server/Aoi/AoiBandScheduler.cs`
- 测试：`Server/Server.Tests/Aoi/AoiBroadcasterTests.cs`
- 测试：`Server/Server.Tests/Aoi/AoiBandSchedulerTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `AoiBroadcasterTests.cs` 的 `Snapshot_Carries_Current_Full_Fields` 中，把期望掩码改为：

```csharp
Assert.Equal((ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed
    | EntityFieldMask.ActionState | EntityFieldMask.Kind | EntityFieldMask.TemplateId
    | EntityFieldMask.Dead | EntityFieldMask.Health | EntityFieldMask.Defending), captured.FieldMask);
Assert.Equal(100f, captured.MaxHp, 4);
Assert.Equal(mover.Hp, captured.Hp, 4);
Assert.False(captured.IsDefending);
```

并在 `Spawn` 后设置 `mover.ApplyDamage(10f);` / `mover.IsDefending` 需要 setter——当前 `IsDefending` 是 `internal set`，测试程序集在同一个 Server 项目？`Server.Tests` 是独立程序集，`internal set` 不可写。改用真实路径：`PositionSyncHandler` 才写 `IsDefending`。为测试，给 `PlayerState` 增加一个 `public void SetDefendingForTests(bool)`？不要。更简单：在 `AoiBroadcasterTests` 里只断言默认 `false` 与 HP；防御变化的测试放到 `AoiBandSchedulerTests`（直接传参数）。

在 `AoiBandSchedulerTests.cs` 的 `Send` 辅助方法增加参数：

```csharp
private static bool Send(AoiBandScheduler s, long o, long e, float dist, uint tick,
    float x = 0f, float z = 0f, float rot = 0f, byte actionState = 0, bool dead = false,
    float hp = 100f, float maxHp = 100f, bool defending = false)
    => s.ShouldSend(o, e, dist, tick, x, z, rot, actionState, dead, hp, maxHp, defending);
```

新增测试：

```csharp
[Fact]
public void Health_Change_Sends_Immediately_For_A_Stationary_Entity()
{
    var s = new AoiBandScheduler();
    Assert.True(Send(s, 1, 2, 10f, 100, hp: 100f));
    Assert.False(Send(s, 1, 2, 10f, 101, hp: 100f));
    Assert.True(Send(s, 1, 2, 10f, 102, hp: 70f));
}

[Fact]
public void Defending_Change_Sends_Immediately_For_A_Stationary_Entity()
{
    var s = new AoiBandScheduler();
    Assert.True(Send(s, 1, 2, 10f, 100, defending: false));
    Assert.False(Send(s, 1, 2, 10f, 101, defending: false));
    Assert.True(Send(s, 1, 2, 10f, 102, defending: true));
}

[Fact]
public void Health_Change_Does_Not_Push_Back_The_Position_Cadence()
{
    var s = new AoiBandScheduler();
    const float mid = 45f;
    Assert.True(Send(s, 1, 2, mid, 100, hp: 100f));
    Assert.True(Send(s, 1, 2, mid, 101, hp: 90f));
    Assert.True(Send(s, 1, 2, mid, 102, x: 0.6f, hp: 90f));
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~AoiBandSchedulerTests"`
预期：编译失败（`ShouldSend` 参数不匹配）。

- [ ] **步骤 3：实现快照填充**

`AoiBroadcaster.cs` 的 `ShouldSend` 调用追加：

```csharp
entity.Hp, entity.MaxHp, entity.IsDefending
```

`EntitySnapshot` 初始化器追加：

```csharp
Hp = entity.Hp,
MaxHp = entity.MaxHp,
IsDefending = entity.IsDefending,
```

`FieldMask` 追加：

```csharp
| EntityFieldMask.Health | EntityFieldMask.Defending
```

- [ ] **步骤 4：实现调度器变化检测**

`AoiBandScheduler.cs`：

- `Slot` 追加 `Hp` / `MaxHp` / `IsDefending`。
- `ShouldSend` 签名追加 `float hp, float maxHp, bool defending`。
- 每个 `new Slot { ... }` 初始化器补三个字段。
- 状态翻转判断改为：

```csharp
if (slot.ActionState != actionState || slot.Dead != dead
    || slot.Hp != hp || slot.MaxHp != maxHp || slot.IsDefending != defending)
{
    slot.ActionState = actionState;
    slot.Dead = dead;
    slot.Hp = hp;
    slot.MaxHp = maxHp;
    slot.IsDefending = defending;
    slot.X = x; slot.Z = z; slot.RotY = rotY;
    entities[entityId] = slot;
    return true;
}
```

- [ ] **步骤 5：运行测试验证通过**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~Aoi"`
预期：全绿。

- [ ] **步骤 6：Commit**

```bash
git add Server/Aoi Server/Server.Tests/Aoi
git commit -m "feat(server): broadcast authoritative hp and defending state

Entity snapshots now carry Hp/MaxHp/IsDefending behind the new mask bits.
The band scheduler treats a vitals or defending change like an action-state
flip: send immediately without resetting the position cadence, so a hit or a
guard toggle is visible on every observer's next snapshot."
```

---

## 任务 4：DamageResult 扩播 + 登录名字名册

**文件：**
- 修改：`Server/Handler/CastHandler.cs`
- 修改：`Server/Handler/LoginHandler.cs`
- 测试：`Server/Server.Tests/Combat/CastHandlerTests.cs`
- 测试：`Server/Server.Tests/Handler/LoginRosterTests.cs`（新建）

- [ ] **步骤 1：编写失败的测试**

在 `CastHandlerTests.cs` 追加一个**纯收件人**测试（先让实现暴露 `CollectDamageRecipients`）：

```csharp
[Fact]
public void Damage_Recipients_Include_Caster_Targets_And_Both_Observer_Sets()
{
    var visible = new Dictionary<long, long[]>
    {
        [1] = new long[] { 5L, 6L },  // 施法者 1 的观察者
        [2] = new long[] { 6L, 7L }   // 受击者 2 的观察者
    };
    var recipients = CastHandler.CollectDamageRecipients(
        hitTargets: new long[] { 2L },
        casterId: 1L,
        npcCaster: false,
        observersOf: id => visible.TryGetValue(id, out var ids) ? ids : System.Array.Empty<long>(),
        visiblePlayersOf: id => id == 1L
            ? new[] { new PlayerState(5L), new PlayerState(6L) }
            : System.Array.Empty<PlayerState>());

    Assert.Equal(new long[] { 1L, 2L, 5L, 6L, 7L }, recipients.OrderBy(id => id).ToArray());
}
```

新建 `Server/Server.Tests/Handler/LoginRosterTests.cs`：

```csharp
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class LoginRosterTests
{
    [Fact]
    public void Roster_Contains_Existing_Players_Except_The_New_One()
    {
        var states = new Dictionary<long, PlayerState>
        {
            [1] = new PlayerState(1) { PlayerName = "A" },
            [2] = new PlayerState(2) { PlayerName = "B" },
            [3] = new PlayerState(3) { PlayerName = "New" }
        };

        var roster = LoginHandler.BuildRoster(states, excludePlayerId: 3L, zoneId: "Global");

        Assert.Equal(new[] { 1L, 2L }, roster.Select(r => r.PlayerId).OrderBy(id => id).ToArray());
        Assert.Equal("A", roster.Single(r => r.PlayerId == 1L).PlayerName);
        Assert.Equal("B", roster.Single(r => r.PlayerId == 2L).PlayerName);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~Damage_Recipients|FullyQualifiedName~LoginRosterTests"`
预期：编译失败。

- [ ] **步骤 3：实现收件人纯函数并接入广播**

在 `CastHandler` 内新增：

```csharp
public static HashSet<long> CollectDamageRecipients(
    IReadOnlyList<long> hitTargets, long casterId, bool npcCaster,
    Func<long, IEnumerable<long>> observersOf,
    Func<long, IEnumerable<PlayerState>> visiblePlayersOf)
{
    var recipients = new HashSet<long>();
    if (!npcCaster) recipients.Add(casterId);
    foreach (var id in hitTargets) recipients.Add(id);

    IEnumerable<long> casterObservers = npcCaster
        ? observersOf(casterId)
        : visiblePlayersOf(casterId).Select(p => p.PlayerId);
    foreach (var id in casterObservers) recipients.Add(id);

    foreach (var target in hitTargets)
        foreach (var id in observersOf(target)) recipients.Add(id);

    return recipients;
}
```

`BroadcastDamageAsync` 的收件人改用：

```csharp
var hitTargets = new List<long>();
foreach (var hit in resolution.Hits) hitTargets.Add(hit.TargetPlayerId);

var recipients = CollectDamageRecipients(
    hitTargets,
    resolution.Broadcast.CasterPlayerId,
    resolution.IsNpcCaster,
    _server.GetObserversOf,
    id => _server.GetVisiblePlayers(id));
```

> `CastHandler.cs` 当前没有 `using System.Linq;`；上面的写法用 `List<long>` 手工收集，不需要新增 Linq 引用。`_server.GetObserversOf` 返回 `List<long>`，可隐式转换为 `Func<long, IEnumerable<long>>`；若编译器不接受方法组，改成 `id => _server.GetObserversOf(id)`。

然后遍历 `recipients` 发送（原有 `GetSession` 逻辑不变）。

- [ ] **步骤 4：实现 CastBroadcast 落点/蓄力**

`CombatResolver.Decide` 构造 `CastBroadcast` 时追加：

```csharp
ImpactX = request.ImpactX,
ImpactY = request.ImpactY,
ImpactZ = request.ImpactZ,
ChargeProgress = request.ChargeProgress
```

- [ ] **步骤 5：实现登录名册**

在 `LoginHandler` 内新增：

```csharp
public static List<PlayerEnterRoom> BuildRoster(
    IReadOnlyDictionary<long, PlayerState> players, long excludePlayerId, string zoneId)
{
    var list = new List<PlayerEnterRoom>();
    foreach (var pair in players)
    {
        if (pair.Key == excludePlayerId) continue;
        list.Add(new PlayerEnterRoom
        {
            PlayerId = pair.Key,
            PlayerName = pair.Value.PlayerName,
            ZoneId = zoneId,
            ServerTimestamp = DateTime.UtcNow.Ticks
        });
    }
    return list;
}
```

在 `HandleLoginAsync` 的 `var room = _roomManager.GetPlayerRoom(playerId);` 之后、通知其他人的代码之前追加：

```csharp
if (room != null)
{
    foreach (var existing in BuildRoster(room.GetAllPlayerStates(), playerId, zoneId))
        await _server.SendToSessionAsync(session, MessageId.PlayerEnterRoom, existing);
}
```

- [ ] **步骤 6：运行测试验证通过**

运行：
- `bash tools/test-server.sh --filter "FullyQualifiedName~CastHandlerTests"`
- `bash tools/test-server.sh --filter "FullyQualifiedName~LoginRosterTests"`
- `bash tools/test-server.sh`

预期：全绿。

- [ ] **步骤 7：Commit**

```bash
git add Server/Handler Server/Server.Tests
git commit -m "feat(server): broadcast damage results to observers and seed a name roster

DamageResult now goes to the caster, every hit target, and the observers of
both sides (deduplicated). This lets bystanders see damage numbers, hit
sparks and vitals updates, while the damage authority is unchanged.

New players receive PlayerEnterRoom entries for the players already in the
room, so the client can show remote names from the first snapshot. Positions
in those entries are ignored by the client; the roster only carries names."
```

---

## 任务 5：`RemoteEntityAnimator` 表现仲裁器 + 死亡序列

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityAnimatorArbiterTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityAnimatorLocomotionTests.cs`（保持全绿）

- [ ] **步骤 1：编写失败的测试**

新建 `RemoteEntityAnimatorArbiterTests.cs`：

```csharp
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class RemoteEntityAnimatorArbiterTests
    {
        private GameObject _go = null!;
        private Animator _animator = null!;
        private RemoteEntityAnimator _driver = null!;

        [SetUp]
        public void SetUp()
        {
            var controller = UnityEditor.Animations.AnimatorController
                .CreateAnimatorControllerAtPath("Assets/Tests_TempArbiter.controller");
            foreach (var (name, type) in new[]
            {
                ("AttackState", AnimatorControllerParameterType.Int),
                ("HitState", AnimatorControllerParameterType.Int),
                ("IsDefending", AnimatorControllerParameterType.Bool),
                ("Attack", AnimatorControllerParameterType.Trigger),
                ("HeavyAttack", AnimatorControllerParameterType.Trigger),
            })
                controller.AddParameter(name, type);

            // 仲裁器会写 Attack/Hit/Defend 三个层的权重；临时控制器必须真的有 4 层，
            // 否则 SetLayerWeight 会刷 "layer index out of range" 错误日志（NUnit 视为失败）。
            controller.AddLayer("Attack");
            controller.AddLayer("Hit");
            controller.AddLayer("Defend");

            _go = new GameObject("ArbiterTest");
            _animator = _go.AddComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _driver = _go.AddComponent<RemoteEntityAnimator>();
            _animator.applyRootMotion = true; // 驱动必须在 ResolveAnimator/Awake 里显式关掉
            _driver.ResolveAnimator();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            UnityEditor.AssetDatabase.DeleteAsset("Assets/Tests_TempArbiter.controller");
        }

        [Test]
        public void Attack_Writes_Weight_Before_Trigger_And_Clears_After_Duration()
        {
            _driver.PlayAttack("Attack", 0.2f);
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.AttackLayerIndex), 0.001f);
            Assert.AreEqual((int)Hotfix.GameSystems.Sys3C.FSM.AttackState.Attacking,
                _animator.GetInteger("AttackState"));

            _driver.Tick(0.25f);
            Assert.AreEqual(0f, _animator.GetLayerWeight(AnimHashes.AttackLayerIndex), 0.001f);
            Assert.AreEqual((int)Hotfix.GameSystems.Sys3C.FSM.AttackState.Idle,
                _animator.GetInteger("AttackState"));
        }

        [Test]
        public void Hit_Overrides_Attack_And_Defend_Stance_Returns_After_Hit()
        {
            _driver.SetDefending(true);
            _driver.PlayAttack("Attack", 1f);
            _driver.PlayHit(blocked: true, duration: 0.2f);

            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f);
            Assert.AreEqual((int)HitState.DefendHit, _animator.GetInteger("HitState"));
            Assert.AreEqual(0f, _animator.GetLayerWeight(AnimHashes.AttackLayerIndex), 0.001f);

            _driver.Tick(0.25f);
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.DefendLayerIndex), 0.001f);
            Assert.IsTrue(_animator.GetBool("IsDefending"));
        }

        [Test]
        public void Repeat_Player_Snapshot_Does_Not_Clear_A_Hit_Reaction()
        {
            // 玩家快照的 ActionState 恒 0；它绝不能把事件驱动的受击表现清掉。
            _driver.PlayHit(blocked: false, duration: 0.5f);
            _driver.ApplySnapshotState(dead: false);
            Assert.AreEqual((int)HitState.Hit, _animator.GetInteger("HitState"));
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f);
        }

        [Test]
        public void Death_Walks_To_Down_And_Repated_Dead_Snapshots_Do_Not_Restart()
        {
            _driver.ApplySnapshotState(dead: true);
            Assert.AreEqual((int)HitState.Death, _animator.GetInteger("HitState"));

            _driver.Tick(0.71f); // 超过 Death 回退常量 0.7s
            Assert.AreEqual((int)HitState.Down, _animator.GetInteger("HitState"));

            _driver.ApplySnapshotState(dead: true); // 重复快照
            _driver.Tick(0.1f);
            Assert.AreEqual((int)HitState.Down, _animator.GetInteger("HitState"),
                "重复 dead=true 不得把死亡计时重置回 Death");
        }

        [Test]
        public void Respawn_Plays_GetUp_And_Clears_After_Duration()
        {
            _driver.ApplySnapshotState(dead: true);
            _driver.Tick(0.71f);
            _driver.ApplySnapshotState(dead: false);
            Assert.AreEqual((int)HitState.GetUp, _animator.GetInteger("HitState"));

            _driver.Tick(0.51f);
            Assert.AreEqual(0f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f);
        }

        [Test]
        public void Missing_Trigger_Degrades_Without_Logging_Errors()
        {
            Assert.DoesNotThrow(() => _driver.PlayAttack("DoesNotExist", 0.2f));
            _driver.Tick(0.25f);
        }

        [Test]
        public void Reapply_Replays_Down_After_A_Tier_Upgrade()
        {
            _driver.ApplySnapshotState(dead: true);
            _driver.Tick(0.71f);
            _driver.Reapply();
            Assert.AreEqual((int)HitState.Down, _animator.GetInteger("HitState"));
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f);
        }

        [Test]
        public void Root_Motion_Is_Disabled()
        {
            Assert.IsFalse(_animator.applyRootMotion);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode RemoteEntityAnimatorArbiterTests`
预期：编译失败（新 API 不存在）。

- [ ] **步骤 3：实现仲裁器**

重写 `RemoteEntityAnimator.cs` 的字段与 API（保留现有 `ApplyLocomotion`、`ResetLayerWeights`、`InvalidateDedup`、`LastHitState`/`LastBaseState`/`LastBlend`/`LastLocomotionSpeed`、`NominalWalkSpeed` 等常量与 `ResolveAnimator` 的控制器解析逻辑）：

```csharp
private enum DeathPhase { None, Falling, Down, GettingUp }

private bool _dead;
private bool _defending;
private float _hitRemaining;
private bool _hitBlocked;
private float _attackRemaining;
private float _deathRemaining;
private DeathPhase _deathPhase;
private string? _attackTrigger;
private bool _attackTriggerApplied;

internal void ApplySnapshotState(bool dead)
{
    if (dead == _dead) return; // 重复快照绝不重置计时

    _dead = dead;
    _hitRemaining = 0f;
    _attackRemaining = 0f;

    if (dead)
    {
        _deathPhase = DeathPhase.Falling;
        _deathRemaining = 0.7f;
    }
    else
    {
        _deathPhase = DeathPhase.GettingUp;
        _deathRemaining = 0.5f;
    }
    RefreshLayers();
}

internal void PlayAttack(string trigger, float duration)
{
    if (_dead || _deathPhase == DeathPhase.Down) return;
    if (_deathPhase == DeathPhase.GettingUp) _deathPhase = DeathPhase.None; // GetUp 非霸体，可被攻击打断
    _attackRemaining = Mathf.Max(0.1f, duration);
    _attackTrigger = trigger;
    _attackTriggerApplied = false;
    RefreshLayers();
}

internal void PlayHit(bool blocked, float duration)
{
    if (_dead || _deathPhase == DeathPhase.Down) return;
    if (_deathPhase == DeathPhase.GettingUp) _deathPhase = DeathPhase.None;
    _hitRemaining = Mathf.Max(0.1f, duration);
    _hitBlocked = blocked;
    RefreshLayers();
}

internal void SetDefending(bool defending)
{
    if (_defending == defending) return; // 快照每 tick 都带 IsDefending；不变不要重刷层
    _defending = defending;
    RefreshLayers();
}

internal void Tick(float deltaTime)
{
    if (_attackRemaining > 0f) _attackRemaining = Mathf.Max(0f, _attackRemaining - deltaTime);
    if (_hitRemaining > 0f) _hitRemaining = Mathf.Max(0f, _hitRemaining - deltaTime);
    if (_deathRemaining > 0f) _deathRemaining = Mathf.Max(0f, _deathRemaining - deltaTime);

    if (_deathPhase == DeathPhase.Falling && _deathRemaining <= 0f)
    {
        _deathPhase = DeathPhase.Down;
        RefreshLayers();
    }
    else if (_deathPhase == DeathPhase.GettingUp && _deathRemaining <= 0f)
    {
        _deathPhase = DeathPhase.None;
        RefreshLayers();
    }
    else if (_attackRemaining <= 0f && _hitRemaining <= 0f)
    {
        RefreshLayers(); // 清 trigger/权重；幂等
    }
}

private void RefreshLayers()
{
    if (_animator == null) return;

    if (_deathPhase == DeathPhase.Falling)
        WriteHitOnly(HitState.Death);
    else if (_deathPhase == DeathPhase.Down)
        WriteHitOnly(HitState.Down);
    else if (_hitRemaining > 0f)
        WriteHitOnly(_hitBlocked ? HitState.DefendHit : HitState.Hit);
    else if (_attackRemaining > 0f)
        WriteAttackOnly();
    else if (_deathPhase == DeathPhase.GettingUp)
        WriteHitOnly(HitState.GetUp);
    else
        WriteLocomotionLayers();

    LastHitState = _animator.GetInteger(AnimHashes.HitState); // 观察面（无控制器时为 0）
}

internal void Reapply()
{
    _hitRemaining = 0f;
    _attackRemaining = 0f;

    if (_dead)
    {
        if (_deathPhase != DeathPhase.Down)
        {
            _deathPhase = DeathPhase.Falling;
            _deathRemaining = 0.7f; // 重新播倒下（升档时 Animator 已被重置）
        }
    }
    else if (_deathPhase == DeathPhase.GettingUp)
    {
        _deathPhase = DeathPhase.None; // 升档/OnEnable 不重播 GetUp
    }

    RefreshLayers();
}
```

```csharp
private void WriteHitOnly(HitState hitState)
{
    _animator!.SetLayerWeight(AnimHashes.HitLayerIndex, 1f);
    _animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 0f);
    _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, 0f);
    _animator.SetInteger(AnimHashes.HitState, (int)hitState);

    if (!string.IsNullOrEmpty(_attackTrigger))
    {
        _animator.ResetTrigger(_attackTrigger);
        _attackTriggerApplied = false;
    }
}

private void WriteAttackOnly()
{
    _animator!.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);
    _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, 0f);
    _animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 1f);
    _animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Attacking);

    if (!_attackTriggerApplied
        && !string.IsNullOrEmpty(_attackTrigger)
        && _availableParams.Contains(Animator.StringToHash(_attackTrigger)))
    {
        _animator.SetTrigger(_attackTrigger);
        _attackTriggerApplied = true; // 只在本次攻击开始时触发一次，别被快照/格挡刷新重复点
    }
}

private void WriteLocomotionLayers()
{
    _animator!.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);
    _animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 0f);
    _animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Idle);
    _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, _defending ? 1f : 0f);
    if (_hasDefending) _animator.SetBool(AnimHashes.IsDefending, _defending);

    if (!string.IsNullOrEmpty(_attackTrigger))
    {
        _animator.ResetTrigger(_attackTrigger);
        _attackTriggerApplied = false;
    }
}
```

参数能力检测在 `ResolveAnimator` 里缓存：

```csharp
private bool _hasAttackState, _hasHitState, _hasDefending;
private readonly HashSet<int> _availableParams = new HashSet<int>();
```

`Awake` 中 `applyRootMotion = false`；`OnEnable` 调 `Reapply()`；`OnDisable` 清 `_hitRemaining/_attackRemaining`；`Reapply` 按 `_dead/_deathPhase/_defending` 重放。`Update` 调 `Tick(Time.deltaTime)`。

- [ ] **步骤 4：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode RemoteEntityAnimatorArbiterTests`
- `bash tools/unity-tests.sh EditMode RemoteEntityAnimatorLocomotionTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿。若 `Death` 回退常量与真实剪辑长度不同，测试的 0.71s 只验证"计时换相"；真实长度在 PlayMode/浏览器验收。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs Assets/Scripts/Tests/Editor/SyncLayer
git commit -m "feat(sync): drive remote players through a presentation arbiter

RemoteEntityAnimator now separates snapshot state (Dead/IsDefending) from
transient events (PlayAttack/PlayHit/Tick). Priority is
Falling/Down > Hit > Attack > GettingUp > Defend > Locomotion.

Repeated dead=true snapshots no longer restart the death timer. The death
sequence walks Death -> Down and respawn walks GetUp -> None, because the
controller switches on HitState values and the StateMachineBehaviours are now
inert for remote animators. GetUp stays interruptible, matching HitFSM's
Death/Down-only super armour."
```

---

## 任务 6：客户端 vitals / 名字 / 只读可选中

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetable.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/PlayerNameRegistry.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/INetworkLabelSink.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetableTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/PlayerNameRegistryTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs`（追加）

- [ ] **步骤 1：编写失败的测试**

`NetworkTargetableTests.cs`：

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class NetworkTargetableTests
    {
        [Test]
        public void SetVitals_Fires_Hp_And_Death_Once()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            float percent = -1f; int current = -1; int max = -1; int deaths = 0;
            t.OnHPChanged += (p, c, m) => { percent = p; current = c; max = m; };
            t.OnDeath += () => deaths++;

            t.SetVitals(50f, 100f, dead: false);
            Assert.AreEqual(0.5f, percent, 0.001f);
            Assert.AreEqual(50, current);
            Assert.AreEqual(100, max);
            Assert.AreEqual(0, deaths);

            t.SetVitals(0f, 100f, dead: true);
            t.SetVitals(0f, 100f, dead: true);
            Assert.AreEqual(1, deaths, "死亡只触发一次");
        }

        [Test]
        public void Respawn_Resets_Death_Flag()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            int deaths = 0; t.OnDeath += () => deaths++;
            t.SetVitals(0f, 100f, dead: true);
            t.SetVitals(100f, 100f, dead: false);
            t.SetVitals(0f, 100f, dead: true);
            Assert.AreEqual(2, deaths, "复活后再次死亡应再次触发");
        }

        [Test]
        public void MarkRemoved_Fires_Death_And_Cached_Position_Does_Not_Touch_Transform()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            int deaths = 0; t.OnDeath += () => deaths++;
            t.SetPosition(new Vector3(1f, 0f, 2f));
            t.MarkRemoved();
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), t.WorldPosition);
        }

        [Test]
        public void HpPercent_Clamps_And_Guards_Zero_MaxHp()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            Assert.AreEqual(0f, t.HPPercent);
            t.SetVitals(-5f, 100f, dead: true);
            Assert.AreEqual(0f, t.HPPercent);
            Assert.AreEqual(0, t.CurrentHP);
        }
    }
}
```

`PlayerNameRegistryTests.cs`：

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class PlayerNameRegistryTests
    {
        [Test]
        public void Set_Stores_And_Notifies_Once_Per_Change()
        {
            var r = new PlayerNameRegistry();
            int changes = 0; r.OnNameChanged += (_, __) => changes++;
            r.Set(3L, "Alice");
            r.Set(3L, "Alice");
            Assert.IsTrue(r.TryGetName(3L, out var name));
            Assert.AreEqual("Alice", name);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void Remove_And_Clear_Work()
        {
            var r = new PlayerNameRegistry();
            r.Set(1L, "A");
            r.Set(2L, "B");
            r.Remove(1L);
            Assert.IsFalse(r.TryGetName(1L, out _));
            Assert.IsTrue(r.TryGetName(2L, out _));
            r.Clear();
            Assert.IsFalse(r.TryGetName(2L, out _));
        }
    }
}
```

在 `RemoteEntityRegistryTests.cs` 追加：

```csharp
[Test]
public void Snapshot_Vitals_Update_View_And_Targetable()
{
    var root = new GameObject("VitalsRoot");
    var registry = new RemoteEntityRegistry(root.transform);
    try
    {
        var snap = new EntitySnapshot
        {
            PlayerId = 9L,
            FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Health | EntityFieldMask.Defending),
            X = 0, Z = 0,
            Hp = 35f, MaxHp = 100f, IsDefending = true
        };
        registry.ApplySnapshot(snap, serverTicks: 1_000L);
        var view = registry.GetView(9L);
        Assert.IsNotNull(view);
        Assert.AreEqual(35f, view!.Hp, 0.001f);
        Assert.AreEqual(100f, view.MaxHp, 0.001f);
        Assert.IsTrue(view.IsDefending);
        Assert.AreEqual(35, view.Targetable.CurrentHP);
    }
    finally
    {
        registry.Clear();
        Object.DestroyImmediate(root);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode NetworkTargetableTests` 和 `PlayerNameRegistryTests`
预期：编译失败。

- [ ] **步骤 3：实现 `NetworkTargetable`**

```csharp
using System;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>远程实体的只读 ITargetable：本地伤害链永远够不到它（不实现 IDamageable）。</summary>
    public sealed class NetworkTargetable : ITargetable
    {
        private float _hp;
        private float _maxHp;
        private bool _dead;
        private bool _removed;
        private Vector3 _lastPosition;

        public long EntityId { get; }
        public string DisplayName { get; private set; }
        public int Level => 1;
        public Sprite? Portrait => null;
        public float HPPercent => _maxHp > 0f ? Mathf.Clamp01(_hp / _maxHp) : 0f;
        public int CurrentHP => Mathf.Max(0, Mathf.CeilToInt(_hp));
        public int MaxHP => Mathf.Max(1, Mathf.CeilToInt(_maxHp));
        public Vector3 WorldPosition => _lastPosition;
        public float SelectionRingYOffset => 0.05f;

        public event Action<float, int, int>? OnHPChanged;
        public event Action? OnDeath;

        public NetworkTargetable(long entityId, string displayName)
        {
            EntityId = entityId;
            DisplayName = displayName;
        }

        public void SetVitals(float hp, float maxHp, bool dead)
        {
            bool hpChanged = !Mathf.Approximately(_hp, hp) || !Mathf.Approximately(_maxHp, maxHp);
            _hp = hp;
            _maxHp = maxHp;
            if (dead && !_dead) OnDeath?.Invoke();
            _dead = dead;
            if (hpChanged) OnHPChanged?.Invoke(HPPercent, CurrentHP, MaxHP);
        }

        public void SetDisplayName(string displayName)
        {
            if (!string.IsNullOrEmpty(displayName)) DisplayName = displayName;
        }

        public void SetPosition(Vector3 position) => _lastPosition = position;

        public void MarkRemoved()
        {
            if (_removed) return;
            _removed = true;
            if (!_dead) { _dead = true; OnDeath?.Invoke(); }
        }
    }
}
```

- [ ] **步骤 4：实现 `PlayerNameRegistry`**

```csharp
using System;
using System.Collections.Generic;

namespace Hotfix.GameSystems.SyncLayer
{
    public sealed class PlayerNameRegistry
    {
        private readonly Dictionary<long, string> _names = new Dictionary<long, string>();

        public event Action<long, string>? OnNameChanged;

        public bool TryGetName(long playerId, out string name)
            => _names.TryGetValue(playerId, out name!);

        public string Resolve(long playerId)
            => _names.TryGetValue(playerId, out var name) ? name : "玩家 " + playerId;

        public void Set(long playerId, string name)
        {
            if (string.IsNullOrEmpty(name)) name = "玩家 " + playerId;
            if (_names.TryGetValue(playerId, out var old) && old == name) return;
            _names[playerId] = name;
            OnNameChanged?.Invoke(playerId, name);
        }

        public void Remove(long playerId) => _names.Remove(playerId);

        public void Clear() => _names.Clear();
    }
}
```

创建 `INetworkLabelSink.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>远程实体名牌/血条的注入点；测试传 null 即不注册（避免命中场景单例）。</summary>
    public interface INetworkLabelSink
    {
        void Register(long entityId, Transform owner, string displayName);
        void UpdateName(long entityId, string displayName);
        void UpdateHp(long entityId, float percent);
        void Unregister(long entityId);
    }
}
```

`RemoteEntityView` 增加：

```csharp
public float Hp { get; set; }
public float MaxHp { get; set; }
public bool IsDefending { get; set; }
public string DisplayName { get; set; } = string.Empty;
public NetworkTargetable Targetable { get; set; } = null!;
```

- [ ] **步骤 5：接入 `ClientNetworkManager`**

- 增加字段：`private readonly PlayerNameRegistry _names = new PlayerNameRegistry();`
  `private readonly ConcurrentQueue<(long id, string name)> _pendingNames = new ...;`
- 暴露：`public PlayerNameRegistry Names => _names;`
- 构造订阅：`_dispatcher.Subscribe(MessageId.PlayerEnterRoom, OnPlayerEnter);`
- 网络线程只入队：

```csharp
private void OnPlayerEnter(IMessage message)
{
    var enter = (PlayerEnterRoom)message;
    _pendingNames.Enqueue((enter.PlayerId, enter.PlayerName));
}
```

- `Update` 内、处理 `_pendingLeaves` 的地方，追加：

```csharp
while (_pendingNames.TryDequeue(out var pending))
    _names.Set(pending.id, pending.name);
while (_pendingLeaves.TryDequeue(out var leftId))
{
    _names.Remove(leftId);
    _remoteEntities.Remove(leftId);
}
```

（若原 `Update` 已有 `_pendingLeaves` 循环，只合并，不要重复 Remove。）

- `Dispose` 内 `_names.Clear();`。

- [ ] **步骤 6：接入 `RemoteEntityRegistry`**

- 构造追加可选参数（保持既有 3 参调用可编译）：

```csharp
public RemoteEntityRegistry(Transform root,
    IReadOnlyList<AppearanceEntry>? appearances = null,
    IReadOnlyDictionary<int, GameObject>? npcPrefabs = null,
    IReadOnlyDictionary<int, string>? npcDisplayNames = null,
    INetworkLabelSink? labelSink = null,
    Func<long, string?>? playerNameResolver = null)
```

- 字段保存 3 个新依赖；`RemoteEntityView` 增加 `Hp/MaxHp/IsDefending/DisplayName/Targetable`。
- `CreateView` 里：

```csharp
var displayName = snapshot.Kind == (byte)EntityKind.Npc
    ? (_npcDisplayNames != null && _npcDisplayNames.TryGetValue(snapshot.TemplateId, out var n) ? n : "怪物 " + snapshot.TemplateId)
    : (_playerNameResolver?.Invoke(playerId) ?? "玩家 " + playerId);
if ((snapshot.FieldMask & (ushort)EntityFieldMask.Health) != 0)
{
    view.Hp = snapshot.Hp;
    view.MaxHp = snapshot.MaxHp;
}
if ((snapshot.FieldMask & (ushort)EntityFieldMask.Defending) != 0)
    view.IsDefending = snapshot.IsDefending;
var targetable = new NetworkTargetable(playerId, displayName);
targetable.SetVitals(view.Hp, view.MaxHp, snapshot.Dead);
view.Targetable = targetable;
view.DisplayName = displayName;
if (snapshot.Kind != (byte)EntityKind.PlayerBot)
{
    _labelSink?.Register(playerId, view.GameObject.transform, displayName);
    _labelSink?.UpdateHp(playerId, targetable.HPPercent); // 首帧直接用快照 HP，不显示 0
}
ViewCreated?.Invoke(playerId);
```

- `ApplySnapshot` 里：

```csharp
if ((snapshot.FieldMask & (ushort)EntityFieldMask.Health) != 0)
{
    view.Hp = snapshot.Hp;
    view.MaxHp = snapshot.MaxHp;
}
if ((snapshot.FieldMask & (ushort)EntityFieldMask.Defending) != 0)
    view.IsDefending = snapshot.IsDefending;

view.Targetable.SetVitals(view.Hp, view.MaxHp, snapshot.Dead);
_labelSink?.UpdateHp(viewId, view.Targetable.HPPercent);
view.Animator?.ApplySnapshotState(snapshot.Dead);
if (view.Tier == RemoteTier.Full)
    view.Animator?.SetDefending(view.IsDefending);
// 注意：Reapply 只在 SetTier 升 Full / OnEnable 调，不能每条快照都调——
// 否则会把进行中的受击/攻击计时和死亡阶段重置掉。
```

- `UpdateRender` 每帧：`view.Targetable.SetPosition(view.Position);`
- `SetTier` 升 Full：`view.Animator!.Reapply(); view.Animator.SetDefending(view.IsDefending);`
- `Remove` / `Clear`：`_labelSink?.Unregister(id); view.Targetable.MarkRemoved(); ViewRemoved?.Invoke(id);`
- 新增 `UpdatePlayerName(long id, string name)`：更新 `view.DisplayName`、`view.Targetable.SetDisplayName`、`_labelSink?.UpdateName(id,name)`。
- 新增事件：`public event Action<long>? ViewCreated; public event Action<long>? ViewRemoved;`
- `ClientNetworkHost` 在 `ClientNetworkManager` 创建后订阅名字变化，并接到 registry：

```csharp
_manager.Names.OnNameChanged += HandlePlayerNameChanged;

private void HandlePlayerNameChanged(long playerId, string displayName)
    => _remoteEntities?.UpdatePlayerName(playerId, displayName);
```

断线/重建时退订（`-` 事件）。

- `ClientNetworkHost.NpcPrefabEntry` 增加 `public string DisplayName;`；构造 registry 前建表：

```csharp
var npcDisplayNames = new Dictionary<int, string>();
foreach (var entry in _npcPrefabs)
    if (entry != null && !string.IsNullOrEmpty(entry.DisplayName))
        npcDisplayNames[entry.TemplateId] = entry.DisplayName;
```

- 场景 `DemoDay.unity` 的 4 个 `_npcPrefabs` 条目按 `Server/Config/monster_config.json` 配置名字：1=Slime、2=Wolf、3=DogKnight、4=SpiderBoss；保存后 `assets-refresh` 再读回复核。

- [ ] **步骤 7：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode NetworkTargetableTests`
- `bash tools/unity-tests.sh EditMode PlayerNameRegistryTests`
- `bash tools/unity-tests.sh EditMode RemoteEntityRegistryTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿。

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer Assets/Scripts/Tests/Editor/SyncLayer
git commit -m "feat(sync): expose remote vitals, names and a read-only target adapter

EntitySnapshot vitals now land on RemoteEntityView and on a NetworkTargetable
that implements ITargetable without IDamageable, so the target panel can
display authoritative HP without local damage ever reaching the view.

PlayerEnterRoom is queued on the network thread and drained into a name
registry on the main thread. The registry exposes ViewCreated/ViewRemoved so
presentation systems can clean up without polling."
```

---

## 任务 7：名牌血条模板与 label sink

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateRenderer.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（提供 `INetworkLabelSink` 实现）
- 测试：`Assets/Scripts/Tests/Editor/Nameplate/NameplateRendererHealthTests.cs`（新建）

- [ ] **步骤 1：编写失败的测试**

```csharp
using Hotfix.GameSystems.Nameplate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    public class NameplateRendererHealthTests
    {
        private GameObject _canvasGo = null!;
        private NameplateRenderer _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("Canvas", typeof(Canvas));
            var settings = ScriptableObject.CreateInstance<NameplateSettings>();
            _renderer = new NameplateRenderer(settings, _canvasGo.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.Cleanup();
            Object.DestroyImmediate(_canvasGo);
        }

        [Test]
        public void RegisterWithHealth_Creates_A_Filled_Bar_And_Updates_It()
        {
            var owner = new GameObject("Owner").transform;
            _renderer.RegisterWithHealth(7, owner, new NameplateConfig("Wolf", showHPBar: true));
            _renderer.UpdateHealth(7, 0.4f);

            var fill = _canvasGo.transform.Find("NameplateHealth/HPBar/Fill");
            Assert.IsNotNull(fill, "带血条模板必须有 HPBar/Fill");
            var image = fill!.GetComponent<Image>();
            Assert.IsNotNull(image);
            Assert.AreEqual(Image.Type.Filled, image!.type);
            Assert.AreEqual(0.4f, image.fillAmount, 0.001f);
        }

        [Test]
        public void Register_Without_Health_Keeps_The_Existing_Template()
        {
            var owner = new GameObject("Owner").transform;
            _renderer.Register(8, owner, new NameplateConfig("A"));
            Assert.IsNull(_canvasGo.transform.Find("Nameplate/HPBar"), "旧 Register 不得长出 HP 条");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode NameplateRendererHealthTests`
预期：编译失败（`RegisterWithHealth` 不存在）。

- [ ] **步骤 3：实现带血条模板**

`NameplateRenderer`：
- `DisplayEntry` 增加 `Image? HealthFill; bool HasHealthBar;`
- 增加 `Stack<GameObject> _healthPool`、`Sprite _whiteSprite`（1×1，懒创建）。
- `RegisterWithHealth(int entityId, Transform owner, NameplateConfig config)`：
  - 与 `Register` 相同的重复守卫；
  - `Rent(withHealth: true)`；
  - 创建纵向 Root（`VerticalLayoutGroup` + `ContentSizeFitter`，pivot (0.5,1)）；
  - NameRow（`HorizontalLayoutGroup` + ClassIcon + NameText）；
  - HPBar（背景 `Image` + Fill `Image`，`type=Filled`、`fillMethod=Horizontal`、`fillOrigin=Left`、`sprite=_whiteSprite`）；
  - 保存 `HealthFill`。
- `UpdateHealth(int entityId, float percent)`：
```csharp
if (_entries.TryGetValue(entityId, out var entry) && entry.HealthFill != null)
    entry.HealthFill.fillAmount = Mathf.Clamp01(percent);
```
- `UpdateName` 已存在。
- `Return` 按 `entry.HasHealthBar` 推入 `_healthPool`，复位 `fillAmount=1f`、颜色、`SetActive(false)`。
- `Cleanup` 销毁 `_whiteSprite` 的 texture/sprite。
- **既有 `Register` / `CreateTemplate` 一行不动**，保持单机回归。

`EntityDisplayManager` 增加：

```csharp
public void RegisterWithHealth(int entityId, Transform owner, NameplateConfig config)
    => _nameplate.RegisterWithHealth(entityId, owner, config);

public void UpdateHealth(int entityId, float percent)
    => _nameplate.UpdateHealth(entityId, percent);
```

- [ ] **步骤 4：实现 label sink**

在 `ClientNetworkHost` 内新增一个私有嵌套类或独立类：

```csharp
private sealed class NameplateLabelSink : INetworkLabelSink
{
    public void Register(long entityId, Transform owner, string displayName)
    {
        var mgr = EntityDisplayManager.Instance;
        mgr?.RegisterWithHealth((int)entityId, owner, new NameplateConfig(displayName, showHPBar: true));
    }
    public void UpdateName(long entityId, string displayName)
        => EntityDisplayManager.Instance?.UpdateName((int)entityId, displayName);
    public void UpdateHp(long entityId, float percent)
        => EntityDisplayManager.Instance?.UpdateHealth((int)entityId, percent);
    public void Unregister(long entityId)
        => EntityDisplayManager.Instance?.Unregister((int)entityId);
}
```

`ClientNetworkHost` 构造 `RemoteEntityRegistry` 时传入 `new NameplateLabelSink()` 与名字解析器：

```csharp
_remoteEntities = new RemoteEntityRegistry(root.transform, appearances, npcPrefabs,
    npcDisplayNames, new NameplateLabelSink(), id => _manager?.Names.TryGetName(id, out var n) == true ? n : null);
```

- [ ] **步骤 5：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode NameplateRendererHealthTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate Assets/Scripts/Hotfix/GameSystems/SyncLayer
git commit -m "feat(ui): add a health nameplate template for remote entities

NameplateRenderer gains a second template and pool: name row plus a filled HP
bar under it. The existing Register/CreateTemplate path is untouched, so
single-player monster nameplates do not change.

EntityDisplayManager exposes RegisterWithHealth/UpdateHealth. ClientNetworkHost
provides the INetworkLabelSink adapter and skips registration in tests because
the registry's label sink is optional."
```

---

## 任务 8：技能表现目录 + Spawner 返回值

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillPresentationCatalog.cs`
- 创建：`Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset`
- 修改：`.../Skills/Runtime/SwordQi/SwordQiSpawner.cs`
- 修改：`.../Skills/Runtime/Projectile/FireballSpawner.cs`
- 修改：`.../Skills/Runtime/Projectile/FireballProjectile.cs`
- 测试：`Assets/Scripts/Tests/Editor/Skills/SkillPresentationCatalogTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/Skills/RemoteProjectileApiTests.cs`（新建）

- [ ] **步骤 1：编写失败的测试**

`SkillPresentationCatalogTests.cs`：

```csharp
using System.Linq;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    public class SkillPresentationCatalogTests
    {
        private static readonly int[] SyncedPlayerSkills = { 10001, 10002, 20001, 20002, 20003, 20004, 30001 };

        [Test]
        public void Catalog_Contains_All_Seven_Synced_Player_Skills()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(
                "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset");
            Assert.IsNotNull(catalog, "SkillPresentationCatalog.asset 未创建");

            foreach (var id in SyncedPlayerSkills)
            {
                Assert.IsTrue(catalog!.TryGet(id, out var data), "目录缺少技能 " + id);
                Assert.IsFalse(string.IsNullOrEmpty(data!.AnimatorTrigger), "技能 " + id + " 缺 AnimatorTrigger");
            }
            Assert.AreEqual(SyncedPlayerSkills.Length, catalog!.Skills.Count,
                "目录只应收录 7 个同步玩家技能（怪物技能不在客户端目录）");
        }
    }
}
```

`RemoteProjectileApiTests.cs`：

```csharp
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class RemoteProjectileApiTests
    {
        [Test]
        public void Fireball_ExplodeAt_Is_Idempotent_And_Moves_To_The_Point()
        {
            var go = new GameObject("FireballTest");
            var projectile = go.AddComponent<FireballProjectile>();
            projectile.ExplodeAt(new Vector3(3f, 0f, 4f));
            Assert.AreEqual(new Vector3(3f, 0f, 4f), go.transform.position);
            Assert.IsFalse(projectile.IsActive);
            Assert.DoesNotThrow(() => projectile.ExplodeAt(Vector3.zero));
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode SkillPresentationCatalogTests`
预期：编译失败 / 资产不存在。

- [ ] **步骤 3：实现目录类**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(menuName = "Game/Skills/Skill Presentation Catalog",
        fileName = "SkillPresentationCatalog")]
    public sealed class SkillPresentationCatalog : ScriptableObject
    {
        [SerializeField] private SkillData[] _skills = Array.Empty<SkillData>();

        public IReadOnlyList<SkillData> Skills => _skills;

        public bool TryGet(int skillId, out SkillData data)
        {
            foreach (var skill in _skills)
            {
                if (skill != null && skill.SkillId == skillId)
                {
                    data = skill;
                    return true;
                }
            }
            data = null!;
            return false;
        }
    }
}
```

- [ ] **步骤 4：创建目录资产**

经 Unity MCP `script-execute` 运行（参数名 `code`）：

```csharp
var catalog = ScriptableObject.CreateInstance<Hotfix.GameSystems.Skills.Data.SkillPresentationCatalog>();
var paths = new[]
{
    "Assets/PreRes/SkillsCfg/BA_Combo1.asset",
    "Assets/PreRes/SkillsCfg/BA_Heavy.asset",
    "Assets/PreRes/SkillsCfg/Instant_SkillQ.asset",
    "Assets/PreRes/SkillsCfg/Spin_SkillR.asset",
    "Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset",
    "Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset",
    "Assets/PreRes/SkillsCfg/Meteor_Ultimate.asset"
};
var skills = new Hotfix.GameSystems.Skills.Data.SkillData[paths.Length];
for (int i = 0; i < paths.Length; i++)
    skills[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Skills.Data.SkillData>(paths[i]);

var so = new UnityEditor.SerializedObject(catalog);
var prop = so.FindProperty("_skills");
prop.arraySize = skills.Length;
for (int i = 0; i < skills.Length; i++)
    prop.GetArrayElementAtIndex(i).objectReferenceValue = skills[i];
so.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.AssetDatabase.CreateAsset(catalog,
    "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset");
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
```

随后经 Unity MCP `assets-refresh`，再用 `assets-find`/`asset-get-data` 读回验证 7 个引用仍在（项目已记录过资产写入被刷新回滚的坑）。

- [ ] **步骤 5：让 Spawner 返回 projectile**

`FireballSpawner.Spawn` 返回 `FireballProjectile`：

```csharp
public static FireballProjectile? Spawn(ProjectileSkillData data, DamageBlock damage,
    Vector3 origin, Vector3 direction)
{
    // ... 原有守卫与池初始化 ...
    var fb = _pool.Get();
    fb.OnSpawn(data, damage, origin, direction, _pool);
    return fb;
}
```

`SwordQiSpawner.Spawn` 返回 `SwordQiProjectile`（同样只改返回类型与末尾 `return qi;`）。

`FireballProjectile` 增加：

```csharp
public bool IsActive => !_released;

/// <summary>远程表现对齐：把视觉移到服务端命中点后引爆；幂等。</summary>
public void ExplodeAt(Vector3 point)
{
    if (_released) return;
    transform.position = point;
    if (_data == null) { Release(); return; } // EditMode 测试与降级视觉没有数据源
    Explode(null, point);
}
```

`Explode` 首行加：

```csharp
if (_released) return;
```

- [ ] **步骤 6：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode SkillPresentationCatalogTests`
- `bash tools/unity-tests.sh EditMode RemoteProjectileApiTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿（`RemoteProjectileApiTests` 需要 `FireballProjectile` 没有 `_data` 时也能安全 `Explode`；`Explode` 的 `_data == null` 分支需先判空，否则会 NRE）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills Assets/PreRes/SkillsCfg Assets/Scripts/Tests/Editor/Skills
git commit -m "feat(skills): add a class-independent presentation catalog

The local SkillCoordinator only knows the local character's skills, so a
warrior client could not render a mage's fireball. SkillPresentationCatalog
references the seven synced player SkillData assets and is independent of
the local class.

Fireball/SwordQi spawners now return their projectile so RemoteCastPresenter
can stop the visual at the server-authoritative hit point. FireballProjectile
gains IsActive and an idempotent ExplodeAt."
```

---

## 任务 9：`RemoteCastPresenter` + 投射物视觉对齐

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`（PlayHit/Targetable HP）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（接线）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteCastPresenterTests.cs`（新建）

- [ ] **步骤 1：编写失败的测试**

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class RemoteCastPresenterTests
    {
        private sealed class FakeVisual : IRemoteProjectileVisual
        {
            public bool IsActive { get; private set; } = true;
            public int Explosions;
            public int Stops;
            public Vector3 LastPoint;
            public void ExplodeAt(Vector3 point) { Explosions++; LastPoint = point; IsActive = false; }
            public void StopAtPoint(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
        }

        private sealed class FakeFactory
        {
            public readonly Dictionary<int, FakeVisual> Visuals = new Dictionary<int, FakeVisual>();
            public IRemoteProjectileVisual? Create(int skillId) { var v = new FakeVisual(); Visuals[skillId] = v; return v; }
        }

        [Test]
        public void Fireball_DamageResult_Explodes_At_The_Server_Hit_Point()
        {
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(
                registry: null!, localPlayerId: 1L,
                catalog: null!, // 测试直接注入工厂
                projectileFactory: (_, __, ___) => visual,
                castVfxSpawner: null);
            presenter.TrackForTest(casterId: 2L, skillId: 20003, visual);

            presenter.ApplyDamageForTest(new DamageResult
            {
                SourcePlayerId = 2L, SkillId = 20003,
                Hits = { new HitEntry { TargetPlayerId = 3L, HitX = 5f, HitZ = 6f } }
            });

            Assert.AreEqual(1, visual.Explosions);
            Assert.AreEqual(new Vector3(5f, 0f, 6f), visual.LastPoint);
        }

        [Test]
        public void Blocked_SwordQi_Stops_At_The_Server_Stop_Point()
        {
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(
                registry: null!, localPlayerId: 1L, catalog: null!,
                projectileFactory: (_, __, ___) => visual, castVfxSpawner: null);
            presenter.TrackForTest(casterId: 2L, skillId: 10002, visual);

            presenter.ApplyDamageForTest(new DamageResult
            {
                SourcePlayerId = 2L, SkillId = 10002, ProjectileStopped = true,
                StopX = 4f, StopZ = 2f
            });

            Assert.AreEqual(1, visual.Stops);
            Assert.AreEqual(new Vector3(4f, 0f, 2f), visual.LastPoint);
        }

        [Test]
        public void Local_Caster_Is_Ignored()
        {
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(
                registry: null!, localPlayerId: 1L, catalog: null!,
                projectileFactory: (_, __, ___) => visual, castVfxSpawner: null);
            presenter.TrackForTest(casterId: 1L, skillId: 20003, visual);

            presenter.ApplyDamageForTest(new DamageResult
            {
                SourcePlayerId = 1L, SkillId = 20003,
                Hits = { new HitEntry { TargetPlayerId = 3L, HitX = 9f, HitZ = 9f } }
            });

            Assert.AreEqual(0, visual.Explosions, "本地施法者的视觉由本地 SkillExecutor 负责");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode RemoteCastPresenterTests`
预期：编译失败。

- [ ] **步骤 3：实现 `IRemoteProjectileVisual` 与 presenter**

```csharp
public interface IRemoteProjectileVisual
{
    bool IsActive { get; }
    void ExplodeAt(Vector3 point);
    void StopAtPoint(Vector3 point);
}

internal sealed class FireballVisual : IRemoteProjectileVisual
{
    private readonly FireballProjectile _p;
    public FireballVisual(FireballProjectile p) => _p = p;
    public bool IsActive => _p != null && _p.IsActive;
    public void ExplodeAt(Vector3 point) { if (_p != null) _p.ExplodeAt(point); }
    public void StopAtPoint(Vector3 point) { if (_p != null) _p.ExplodeAt(point); }
}

internal sealed class SwordQiVisual : IRemoteProjectileVisual
{
    private readonly SwordQiProjectile _p;
    public SwordQiVisual(SwordQiProjectile p) => _p = p;
    public bool IsActive => _p != null && _p.IsActive;
    public void ExplodeAt(Vector3 point) { if (_p != null) _p.StopAtPoint(point); }
    public void StopAtPoint(Vector3 point) { if (_p != null) _p.StopAtPoint(point); }
}
```

`RemoteCastPresenter` 构造：

```csharp
public RemoteCastPresenter(
    RemoteEntityRegistry? registry,
    long localPlayerId,
    SkillPresentationCatalog? catalog,
    Func<SkillData, Vector3, Vector3, IRemoteProjectileVisual?>? projectileFactory = null,
    Action<GameObject, Vector3, Quaternion>? castVfxSpawner = null)
```

- 构造参数 `registry`/`catalog` 允许为 null（EditMode 假视觉测试只走 `TrackForTest`/`ApplyDamageForTest`）；`OnCastBroadcast` 首行若二者为 null 直接 return。
- `OnCastBroadcast(CastBroadcast b)`：
  - `if (registry == null || catalog == null) return;`
  - `if (b.CasterPlayerId == localPlayerId) return;`
  - `var view = registry?.GetView(b.CasterPlayerId); if (view == null || view.Kind != (byte)EntityKind.Player) return;`
  - `if (!catalog.TryGet(b.SkillId, out var data)) return;`
  - `view.Animator?.PlayAttack(data.AnimatorTrigger, (data is ChargedSkillData ? data.ReleaseClip : data.CastClip)?.length ?? 0.8f);`
  - `if (data.Presentation?.CastVFX != null) (castVfxSpawner ?? DefaultCastVfx)(data.Presentation.CastVFX, view.Position, view.Rotation);`
  - `var visual = (projectileFactory ?? DefaultFactory)(data, view.Position, view.Rotation * Vector3.forward); if (visual != null) _tracked.Add(new Tracked(b.CasterPlayerId, b.SkillId, visual));`
  - 20004/30001 不跟踪飞行体，直接在落点生成表现：

```csharp
var impact = new Vector3(b.ImpactX, b.ImpactY, b.ImpactZ);
if (data is ChargedSkillData && data.Presentation?.ReleaseVFX != null)
    SpawnVfx(data.Presentation.ReleaseVFX, impact);
if (data.Meteor != null)
    MeteorSpawner.Spawn(data.Meteor, data.Damage?.Clone(), impact);
```
- `OnDamageResult(DamageResult result)`:
  - `if (result.SourcePlayerId == localPlayerId) return;`
  - find the earliest tracked matching `(source, skill)` with `IsActive` and Unity non-null; if none return.
  - if fireball (`SkillId == 20003`) and `result.Hits.Count > 0`: `visual.ExplodeAt(new Vector3(first.HitX, 0f, first.HitZ));`
  - else if swordqi (`SkillId == 10002`) and `result.ProjectileStopped`: `visual.StopAtPoint(new Vector3(result.StopX, 0f, result.StopZ));`
  - remove tracked.
- `Update(float dt)`: prune inactive/null.
- `ClearForEntity(long id)`: remove matching.
- `Clear()`.
- Test seams `TrackForTest`/`ApplyDamageForTest` are `internal` wrappers.

`DefaultFactory`:

```csharp
private static IRemoteProjectileVisual? DefaultFactory(SkillData data, Vector3 origin, Vector3 forward)
{
    if (data.SwordQi != null)
    {
        var qi = SwordQiSpawner.Spawn(data.SwordQi, data.Damage?.Clone(), origin, forward);
        return qi != null ? new SwordQiVisual(qi) : null;
    }
    if (data is ProjectileSkillData projectile)
    {
        var fb = FireballSpawner.Spawn(projectile, data.Damage?.Clone(), origin, forward);
        return fb != null ? new FireballVisual(fb) : null;
    }
    return null;
}
```

- [ ] **步骤 4：接入 `CombatNetworkBridge`**

- `NotifySkillCast` 增加 `long targetId = 0`，写 `TargetPlayerId = targetId`。
- `ApplyDamageResult` 对远程玩家/怪物追加 `view.Targetable?.SetVitals(hit.TargetRemainingHp, view.MaxHp, hit.TargetRemainingHp <= 0f);`
- 远程玩家（`Kind == Player`）追加 `view.Animator?.PlayHit(hit.IsBlocked, 0.5f);`
- `HitFeedbackSystem.cs` 的 `OnMonsterHit` 删除 `if (!e.IsLocalSource) return;`，让第三人看到命中火花；`CameraShakeManager`/`HitStopManager`/`HitParticleController` 的 `IsLocalSource` 守卫保持不变（别人打怪不得晃本地镜头/顿帧）。本类没有直接单测，由任务 12 的浏览器 E2E 判据覆盖。
- `OnDamageResult` 事件保持；presenter 订阅。

- [ ] **步骤 5：接入 `ClientNetworkHost`**

- 字段：`private RemoteCastPresenter? _remoteCastPresenter; private SkillPresentationCatalog? _skillCatalog;`
- 在 `DemoDay.unity` 的 `ClientNetworkHost` 上把 `SkillPresentationCatalog.asset` 赋给 `_skillCatalog`；保存后 `assets-refresh` 复核序列化引用仍在。
- 构造 registry 后创建 presenter，订阅 `_combatBridge.OnCastBroadcast`/`OnDamageResult`；`OnCastBroadcast` 中 NPC 仍走 `ScheduleAttack`，Player 交给 presenter。
- 订阅 `_remoteEntities.ViewRemoved += _remoteCastPresenter.ClearForEntity;`（断线/重建时退订），避免视图删除后跟踪表残留。
- `Update` 顺序：`_manager.Update` → `_combatBridge.Update()` → `_remoteCastPresenter?.Update(Time.deltaTime)` → selector。
- 断线/重建清空 presenter。

- [ ] **步骤 6：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode RemoteCastPresenterTests`
- `bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿（`CombatNetworkBridgeTests` 的 `Assert.AreEqual(0L, request.TargetPlayerId)` 需改为传入的 targetId 或 0 默认）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer Assets/Scripts/Tests/Editor/SyncLayer
git commit -m "feat(sync): render remote casts and align projectile impacts

RemoteCastPresenter plays a remote player's attack immediately on
CastBroadcast (zero-hit-delay skills would otherwise show the hit before the
swing), spawns CastVFX at the rendered pose, and tracks Fireball/SwordQi
visuals by (caster, skill).

On the remote DamageResult the fireball explodes at the server HitX/HitZ and
a blocked sword qi stops at StopX/StopZ. Local casters are skipped; online
damage guards in the spawners stay untouched."
```

---

## 任务 10：目标锁定与 `TargetPlayerId` 贯通

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs`
- 修改：`.../SyncLayer/SkillCastReporter.cs`
- 修改：`.../SyncLayer/CombatNetworkBridge.cs`（已加 targetId）
- 修改：`.../SyncLayer/ClientNetworkHost.cs`（EnsureTargetFor + 传入）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NetworkTargetSelectorTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/SkillCastReporterTargetTests.cs`（新建）

- [ ] **步骤 1：编写失败的测试**

`SkillCastReporterTargetTests.cs`：

```csharp
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class SkillCastReporterTargetTests
    {
        private sealed class Client : INetworkClient
        {
            public readonly List<IMessage> Sent = new List<IMessage>();
            public bool IsConnected => true;
            public System.Threading.Tasks.Task<bool> ConnectAsync(string h, int p) => System.Threading.Tasks.Task.FromResult(true);
            public System.Threading.Tasks.Task DisconnectAsync(string r) => System.Threading.Tasks.Task.CompletedTask;
            public void Send(MessageId id, IMessage msg, MessageFlags flags = MessageFlags.Reliable) => Sent.Add(msg);
            public event System.Action<MessageId, IMessage>? MessageReceived;
            public event System.Action<string>? Disconnected;
            public event System.Action<System.Exception>? Error;
            public void Dispose() { }
        }

        [Test]
        public void Activation_Forwards_The_Locked_Target_Id()
        {
            var client = new Client();
            var bridge = new CombatNetworkBridge(client, () => 1L, null!);
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(
                "Assets/PreRes/SkillsCfg/BA_Combo1.asset");

            SkillCastReporter.ReportActivation(bridge, data, Vector3.zero, null!, targetId: 42L);

            var cast = (CastRequest)client.Sent.Single();
            Assert.AreEqual(42L, cast.TargetPlayerId);
        }
    }
}
```

`NetworkTargetSelectorTests.cs` 可以先只测候选筛选纯函数（避免依赖完整场景）：

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class NetworkTargetSelectorTests
    {
        [Test]
        public void Filter_Excludes_Dead_Bots_And_Out_Of_Range()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(1L, new Vector3(0,0,2f), (byte)Cool.Protocol.EntityKind.Player, dead: false),
                new NetworkTargetSelector.Candidate(2L, new Vector3(0,0,3f), (byte)Cool.Protocol.EntityKind.PlayerBot, dead: false),
                new NetworkTargetSelector.Candidate(3L, new Vector3(0,0,4f), (byte)Cool.Protocol.EntityKind.Npc, dead: true),
                new NetworkTargetSelector.Candidate(4L, new Vector3(0,0,30f), (byte)Cool.Protocol.EntityKind.Npc, dead: false),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 15f, halfAngleDeg: 90f);

            Assert.AreEqual(new[] { 1L }, result.ConvertAll(c => c.EntityId).ToArray());
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests`
预期：编译失败。

- [ ] **步骤 3：实现 `NetworkTargetSelector`**

```csharp
public readonly struct Candidate
{
    public readonly long EntityId;
    public readonly Vector3 Position;
    public readonly byte Kind;
    public readonly bool Dead;

    public Candidate(long entityId, Vector3 position, byte kind, bool dead)
    {
        EntityId = entityId; Position = position; Kind = kind; Dead = dead;
    }
}

public static List<Candidate> FilterCandidates(
    Vector3 origin, Vector3 forward, IReadOnlyList<Candidate> candidates,
    float acquireRange, float halfAngleDeg)
{
    var result = new List<Candidate>();
    float halfCos = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
    forward.y = 0f;
    if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
    forward.Normalize();

    foreach (var candidate in candidates)
    {
        if (candidate.Kind == (byte)EntityKind.PlayerBot || candidate.Dead) continue;
        Vector3 delta = candidate.Position - origin;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (distance > acquireRange) continue;
        if (distance > 0.0001f && Vector3.Dot(forward, delta / distance) < halfCos) continue;
        result.Add(candidate);
    }

    result.Sort((a, b) =>
        (a.Position - origin).sqrMagnitude.CompareTo((b.Position - origin).sqrMagnitude));
    return result;
}

public void Update()
{
    if (_input != null && _input.IsTargetSwitchPressed()) Cycle();
    ValidateLocked();
}

public void EnsureTargetFor(SkillData data)
{
    if (IsLockStillValid(out _)) return;
    var shape = HitShapeConverter.GetShape(data);
    float range = shape != null && shape.Range > 0f ? shape.Range : AcquireRange;
    float halfAngle = shape != null && shape.Angle > 0f ? shape.Angle * 0.5f : 90f;
    var filtered = FilterCandidates(_owner.position, _owner.forward, BuildCandidates(), range, halfAngle);
    if (filtered.Count == 0) { ClearLock(); return; }
    Bind(filtered[0].EntityId);
}

private void Cycle()
{
    var filtered = FilterCandidates(_owner.position, _owner.forward, BuildCandidates(),
        AcquireRange, 90f);
    if (filtered.Count == 0) { ClearLock(); return; }
    int current = filtered.FindIndex(c => c.EntityId == _lockedId);
    Bind(filtered[(current + 1) % filtered.Count].EntityId);
}

private void Bind(long entityId)
{
    var view = _registry.GetView(entityId);
    if (view == null) { ClearLock(); return; }
    _lockedId = entityId;
    _ring?.AttachTo(view.GameObject.transform, view.Targetable.SelectionRingYOffset);
    _bindPanel?.Invoke(view.Targetable);
}

private void ClearLock()
{
    _lockedId = 0;
    _ring?.Detach();
    _clearPanel?.Invoke();
}
```

- `Initialize(RemoteEntityRegistry registry, Transform owner, InputManager? input, SelectionRing? ring, Action<NetworkTargetable>? bindPanel, Action? clearPanel, Func<long>? localId)`。
- `ClientNetworkHost.TryAttachLocalCharacter` 创建 selector（照 `CharacterAttackHandler.SelectTarget` 的 TargetPanel 绑定模式）：

```csharp
_networkTargetSelector = _localEntry.gameObject.AddComponent<NetworkTargetSelector>();
_networkTargetSelector.Initialize(
    _remoteEntities,
    _localEntry.transform,
    _localEntry.GetComponent<InputManager>(),
    _localEntry.GetComponentInChildren<SelectionRing>(true),
    target =>
    {
        UIManager.Instance?.GetPanel<TargetPanel>()?.Bind(target);
        UIManager.Instance?.ShowAlwaysAsync<TargetPanel>().Forget();
    },
    () =>
    {
        UIManager.Instance?.GetPanel<TargetPanel>()?.Clear();
        UIManager.Instance?.HideAlwaysAsync<TargetPanel>().Forget();
    },
    () => _manager?.LocalPlayerId ?? 0L);
```

> `ShowAlwaysAsync/HideAlwaysAsync` 返回 UniTask，照既有 `CharacterAttackHandler` 写法 `.Forget()`；`ClientNetworkHost` 需要 `using Cysharp.Threading.Tasks;` 与 `using Hotfix.GameSystems.UI;`。

- `BuildCandidates()`：遍历 registry 的视图，构造 `Candidate(viewId, view.Position, view.Kind, view.Dead)`。
- `IsLockStillValid`：视图存在、未死、在 `DropRange` 内、本地玩家存活。
- 名字晚到：`ClientNetworkHost.HandlePlayerNameChanged` 若当前锁定的目标 id 匹配，重新 `_bindPanel?.Invoke(view.Targetable)`（`TargetPanel.Bind` 会先 Clear，幂等）。
- `LockedTargetId` 属性；selector 是 MonoBehaviour，由 `ClientNetworkHost` 加到本地角色上。

- [ ] **步骤 4：贯通 reporter/bridge/host**

`SkillCastReporter` 两个方法增加 `long targetId = 0`，调用 `bridge.NotifySkillCast(..., targetId)`。
`ClientNetworkHost.OnLocalSkillCast`：

```csharp
_networkTargetSelector?.EnsureTargetFor(skillData);
SkillCastReporter.ReportActivation(_combatBridge, skillData, targetPosition,
    _localEntry.transform, _networkTargetSelector?.LockedTargetId ?? 0L);
```

`OnLocalChargeReleased` 同样在释放时 `EnsureTargetFor`，再 `ReportChargeRelease(..., targetId)`。

- [ ] **步骤 5：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode NetworkTargetSelectorTests`
- `bash tools/unity-tests.sh EditMode SkillCastReporterTargetTests`
- `bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿。`CombatNetworkBridgeTests` 中旧的 `Assert.AreEqual(0L, request.TargetPlayerId)` 改为 `Assert.AreEqual(0L, ...)` 仍成立（默认参数 0）；新增 targetId 测试。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer Assets/Scripts/Tests/Editor/SyncLayer
git commit -m "feat(sync): lock a target and send its entity id with casts

NetworkTargetSelector cycles Player/Npc candidates (bots and dead entities
excluded), binds TargetPanel and SelectionRing, and validates the lock every
frame. EnsureTargetFor runs before SkillCastReporter so CastRequest carries
the locked TargetPlayerId; the server's explicit-target validation is
unchanged."
```

---

## 任务 11：`T` 键 / 移动端切换目标按钮

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileActionButtons.cs`
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`（按钮）
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/InputManagerTargetSwitchTests.cs`（新建）

- [ ] **步骤 1：编写失败的测试**

```csharp
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class InputManagerTargetSwitchTests
    {
        private GameObject _go = null!;
        private InputManager _input = null!;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Input");
            _input = _go.AddComponent<InputManager>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Ui_Flag_Produces_One_Press_Per_Consume()
        {
            _input.SetTargetSwitchPressedFromUI(true);
            Assert.IsTrue(_input.IsTargetSwitchPressed());
            Assert.IsFalse(_input.IsTargetSwitchPressed(), "同一次按下只消费一次");
        }

        [Test]
        public void Ui_Flag_Is_Consumed_Once_Across_Update()
        {
            _input.SetTargetSwitchPressedFromUI(true);
            _input.Update();
            Assert.IsTrue(_input.IsTargetSwitchPressed());
            Assert.IsFalse(_input.IsTargetSwitchPressed());
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode InputManagerTargetSwitchTests`
预期：编译失败。

- [ ] **步骤 3：实现输入**

`InputManager` 字段：`private bool _targetSwitchPressedFromUI; private bool _targetSwitchConsumed;`
`Update` 重置：`_targetSwitchConsumed = false;`
方法：

```csharp
public void SetTargetSwitchPressedFromUI(bool pressed)
{
    if (pressed) { _targetSwitchPressedFromUI = true; _targetSwitchConsumed = false; }
}

public bool IsTargetSwitchPressed()
{
    bool pressed = _targetSwitchPressedFromUI || UnityInput.GetKeyDown(KeyCode.T);
    if (!pressed || _targetSwitchConsumed) return false;
    _targetSwitchConsumed = true;
    _targetSwitchPressedFromUI = false;
    return true;
}
```

- [ ] **步骤 4：接入移动端按钮**

`MobileActionButtons` 增加（按现有 `JumpPressed`/`ConsumeJump` 的按钮字段模式）：

```csharp
public bool TargetSwitchPressed { get; private set; }
public void NotifyTargetSwitch() => TargetSwitchPressed = true;
public void ConsumeTargetSwitch() => TargetSwitchPressed = false;
```

按钮的 `onClick` 调 `NotifyTargetSwitch()`。`MobileInputBinder.LateUpdate` 中：

```csharp
if (_buttons.TargetSwitchPressed)
{
    _inputManager.SetTargetSwitchPressedFromUI(true);
    _buttons.ConsumeTargetSwitch();
}
```

在 `DemoDay.unity` 的移动按钮组新增一个“切换目标”按钮，绑定到 `MobileActionButtons.TargetSwitch`；390px 竖屏下不遮挡摇杆/攻击/技能按钮。场景修改后按项目流程 `assets-refresh` 并复核序列化字段。

- [ ] **步骤 5：运行测试验证通过**

运行：
- `bash tools/unity-tests.sh EditMode InputManagerTargetSwitchTests`
- `bash tools/unity-tests.sh EditMode`

预期：全绿。移动端按钮做一次 390px 竖屏人工/截图验收。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input Assets/SimpleLowPolyNature/Scenes/DemoDay.unity Assets/Scripts/Tests/Editor/Sys3C
git commit -m "feat(input): add a target-switch action on T and a mobile button

Tab is avoided because WebGL browsers can take focus away from the canvas.
T is unused elsewhere; the mobile button sets the same UI flag and the input
gate remains in effect."
```

---

## 任务 12：三客户端旁观验收 + 带宽 + 文档

**文件：**
- 创建：`Assets/Scripts/Tests/PlayMode/ThreeClientCombatConsistencyTests.cs`
- 修改：`Server/Server.Tests/Aoi/BandwidthBudgetTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Assets/Scripts/AOT/Protocol/README.md`
- 修改：`Server/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md`

- [ ] **步骤 1：编写三客户端 PlayMode 测试**

创建 A（攻击者）、B（受击者）、C（旁观者）：

```csharp
using System.Collections;
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    public class ThreeClientCombatConsistencyTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
            public readonly List<DamageResult> Damages = new List<DamageResult>();
        }

        [UnityTest]
        public IEnumerator Observer_Receives_Damage_And_Vitals_From_A_Vs_B()
        {
            var root = new GameObject("ThreeClientCombatRoot");
            var a = NewClient(root, "Obs_A");
            var b = NewClient(root, "Obs_B");
            var c = NewClient(root, "Obs_C");

            var task = RunAsync(a, b, c);
            float elapsed = 0f;
            while (!task.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            try
            {
                Assert.IsTrue(task.IsCompleted, "three-client test timed out");
                var (ok, reason) = task.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                a.Manager.Dispose();
                b.Manager.Dispose();
                c.Manager.Dispose();
                UnityEngine.Object.Destroy(root);
            }
        }

        private static Ctx NewClient(GameObject root, string name)
        {
            var ctx = new Ctx();
            ctx.Registry = new RemoteEntityRegistry(root.transform);
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, ctx.Registry, name);
            ctx.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctx.Damages.Add((DamageResult)msg);
            };
            return ctx;
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunAsync(Ctx a, Ctx b, Ctx c)
        {
            a.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(a.Manager)) return (false, "A failed to enter world");
            b.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(b.Manager)) return (false, "B failed to enter world");
            c.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(c.Manager)) return (false, "C failed to enter world");

            // 三方同出生点；等 C 看到 B 再攻击（C 的 AOI 必须包含 B）。
            for (int i = 0; i < 40 && c.Registry.GetView(b.Manager.LocalPlayerId) == null; i++)
            {
                a.Manager.Update(0.05f);
                b.Manager.Update(0.05f);
                c.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }
            if (c.Registry.GetView(b.Manager.LocalPlayerId) == null)
                return (false, "C never saw B");

            a.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001,
                TargetPlayerId = b.Manager.LocalPlayerId,
                Shape = new HitShape
                {
                    ShapeType = 0, Range = 3.5f, Angle = 120f,
                    InnerRadius = 0.8f, InnerAngle = 90f
                }
            });

            for (int i = 0; i < 100
                && (a.Damages.Count == 0 || b.Damages.Count == 0 || c.Damages.Count == 0); i++)
            {
                a.Manager.Update(0.05f);
                b.Manager.Update(0.05f);
                c.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }

            var aHit = FindHit(a.Damages, b.Manager.LocalPlayerId);
            var bHit = FindHit(b.Damages, b.Manager.LocalPlayerId);
            var cHit = FindHit(c.Damages, b.Manager.LocalPlayerId);
            if (aHit == null || bHit == null || cHit == null)
                return (false, "caster/victim/observer did not all receive the DamageResult");

            if (Mathf.Abs(aHit.TargetRemainingHp - cHit.TargetRemainingHp) > 0.001f
                || Mathf.Abs(bHit.TargetRemainingHp - cHit.TargetRemainingHp) > 0.001f)
                return (false, "damage values differ across clients");

            // C 的权威 HP 必须从快照落到 Targetable。
            for (int i = 0; i < 40; i++)
            {
                var view = c.Registry.GetView(b.Manager.LocalPlayerId);
                if (view != null
                    && Mathf.Abs(view.Targetable.CurrentHP - cHit.TargetRemainingHp) <= 0.001f)
                    return (true, "ok");
                c.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }

            return (false, $"C targetable HP != DamageResult HP ({cHit.TargetRemainingHp})");
        }

        private static HitEntry? FindHit(List<DamageResult> results, long targetId)
        {
            foreach (var result in results)
                foreach (var hit in result.Hits)
                    if (hit.TargetPlayerId == targetId) return hit;
            return null;
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

- [ ] **步骤 2：运行测试**

先起服务器：`dotnet run --project Server/Server3C.csproj -- 8000`
运行：`bash tools/unity-tests.sh PlayMode ThreeClientCombatConsistencyTests`
预期：PASS。本任务是集成验收（前置任务已完成）；若 FAIL，按日志定位是快照 HP、DamageResult 收件人还是 Targetable 更新，不要改断言口径。

- [ ] **步骤 3：若失败则修复并重跑**

常见失败点与对应任务：
- C 收不到 DamageResult → 任务 4 的观察者扩播；
- C 的 Targetable 不更新 → 任务 6 的 `ApplySnapshot`/`SetVitals`；
- 服务端没命中 → 任务 4 的 `TargetPlayerId`/`CollectDamageRecipients` 或任务 10 的 targetId 贯通。

修复后重跑同一条命令，直到 PASS。

- [ ] **步骤 4：带宽测试补字段**

把 `BandwidthBudgetTests.Entity_FullField_Snapshot_Bytes_Measured` 的快照改成：

```csharp
new EntitySnapshot
{
    PlayerId = 123, X = 100.5f, Y = 2f, Z = -50.25f,
    Rotation = 90f, Speed = 8f, ActionState = 0, Tick = 1,
    Hp = 100f, MaxHp = 100f, IsDefending = false,
    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
        | EntityFieldMask.Speed | EntityFieldMask.Dead
        | EntityFieldMask.Health | EntityFieldMask.Defending)
}
```

并把上下限改为 `Assert.InRange(bytes.Length, 20, 140);`（新增约 12B，140 为裕量）。
若超出门禁，按规格 R1 只对真人 + 怪物发送 HP（bot 省略），并在报告里记录偏差；不要直接放宽预算常量。

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~BandwidthBudgetTests"`

- [ ] **步骤 5：更新模块 README**

- `SyncLayer/README.md`：新增 `NetworkTargetable`/`NetworkTargetSelector`/`PlayerNameRegistry`/`RemoteCastPresenter`/`INetworkLabelSink` 的职责；说明远程死亡序列、投射物对齐、目标锁定输入（T/按钮）。
- `Protocol/README.md`：记录 `EntitySnapshot` tag 13–15 / mask 1<<7/1<<8、`CastBroadcast` tag 10–13 的追加；仍不升版本。
- `Server/README.md`：DamageResult 观察者扩播、登录名册。
- `Nameplate/README.md`：带血条模板与 label sink。

- [ ] **步骤 6：运行全量回归**

```bash
bash tools/test-server.sh
bash tools/unity-tests.sh EditMode
bash tools/unity-tests.sh PlayMode TwoClientWorldTests
bash tools/unity-tests.sh PlayMode CombatSyncTests
bash tools/unity-tests.sh PlayMode MonsterWorldTests
bash tools/unity-tests.sh PlayMode ThreeClientCombatConsistencyTests
```

预期：全绿。浏览器 E2E 按规格 11.4 手工执行，至少覆盖：
- 双浏览器 + 旁观者：攻击/受击/格挡动作、伤害数字、**旁观者命中火花**、血条一致；
- 远程玩家攻击时，本地玩家正在施放的技能正常完成（远程动画回调未打断本地 `CleanupSkillAnimation`）；
- `T`/按钮切换目标后，攻击命中锁定目标，目标面板/选中环正确；
- 火球在服务端命中点爆炸、被格挡剑气在停点停下；
- 远程玩家被击杀：旁观端看到倒下→躺卧→8 秒后起身；
- 390px 竖屏“切换目标”按钮不重叠、可点击。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode Server/Server.Tests/Aoi Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md Assets/Scripts/AOT/Protocol/README.md Server/README.md Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md
git commit -m "test(network): prove observer-side combat consistency and document it

Three clients now verify that an observer receives the same DamageResult and
vitals as the caster and victim. BandwidthBudgetTests include the new snapshot
fields. Module READMEs record the new messages, components and the T/mobile
target-switch input."
```

---

## 自检

### 1. 规格覆盖度

| 规格项 | 任务 |
|---|---|
| StateMachineBehaviour 按 Animator 隔离 | 1 |
| `EntitySnapshot` HP/MaxHP/IsDefending + 掩码 | 2、3 |
| `CastBroadcast` 落点/蓄力 | 2、4 |
| `DamageResult` 观察者扩播 | 4 |
| 登录名字名册 | 4、6 |
| `RemoteEntityAnimator` 仲裁 + 死亡序列 | 5 |
| 客户端 vitals / `NetworkTargetable` / `PlayerNameRegistry` | 6 |
| 名牌血条 / label sink | 7 |
| 技能表现目录（7 个技能） | 8 |
| Spawner 返回值 / `ExplodeAt` | 8 |
| `RemoteCastPresenter` / 投射物对齐 | 9 |
| 目标锁定 / `TargetPlayerId` | 10 |
| 第三人命中火花（HitFeedbackSystem） | 9、12 |
| `T` / 移动端按钮 / 模态门禁 | 11 |
| 三客户端 / 带宽 / README / E2E | 12 |

### 2. 占位符扫描

本计划不含 "TODO/待定/后续实现/类似任务 N"。测试与方法均给出实际代码；场景与资产步骤给出具体路径与验证方式。

### 3. 类型一致性

- `AnimationCallbackRegistry.TryGet(Animator, out Entry)`：任务 1 定义，三个 behaviour 使用；`Entry.Attack/Hit/HasTriggeredHitComplete/LastNormalizedTime` 同名。
- `RemoteEntityAnimator.ApplySnapshotState(bool dead)` / `PlayAttack(string,float)` / `PlayHit(bool,float)` / `SetDefending(bool)` / `Tick(float)` / `Reapply()`：任务 5 定义，任务 9/6 使用。
- `NetworkTargetable.SetVitals(float,float,bool)` / `SetPosition(Vector3)` / `MarkRemoved()`：任务 6 定义，任务 6/9 使用。
- `RemoteCastPresenter` 构造参数与 `IRemoteProjectileVisual`：任务 9 定义，任务 9 测试使用。
- `SkillCastReporter.ReportActivation(..., long targetId = 0)`：任务 10 修改，任务 10 测试使用。
- `InputManager.IsTargetSwitchPressed()` / `SetTargetSwitchPressedFromUI(bool)`：任务 11 定义，任务 10 的 selector 使用。

### 4. 已知执行假设

- `RemoteEntityAnimator` 的死亡/受击回退常量（0.7s / 0.5s）在真实控制器上可能需按实测剪辑长度微调；实现时优先读 `Animator.GetCurrentAnimatorStateInfo(HitLayerIndex).length`。
- `FireballProjectile.Explode` 在 `_data == null` 时必须判空（任务 8 测试会暴露）；远程视觉总是传 `SkillData.Damage` 的克隆，实际不为 null。
- 场景/资产编辑（名册、目录、按钮）必须按项目惯例 `assets-refresh` 后读回复核，防静默回滚。

---

*计划结束。执行时先做任务 1（否则远程攻击动画会污染本地状态机），再按 2–12 顺序推进。*
