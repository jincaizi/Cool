# 本地玩家受击与状态表现增强 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为本地玩家实现低血量持续泛红、受击约 1 秒泛红（期间不可重触发）、被玩家攻击时红色闪屏、实际回血时绿色粒子。

**架构：** 协议新增 `DamageResult.SourceKind` 区分玩家/NPC 来源；`Sys3CEntry` 实现 `Heal` 并发治疗事件；`PlayerBodyTint` 用 MaterialPropertyBlock 组合低血量/受击两层染色；`PlayerHealVFX` 在治疗事件时生成粒子；`EntityDisplayManager` 消费玩家来源受击事件触发既有闪屏。

**技术栈：** Unity 2022.3.25f1、C#、NUnit/UnityTest、protobuf-net、DOTween、Unity MCP。

**规格：** `docs/superpowers/specs/2026-10-22-player-hit-feedback-vfx-design.md`

---

## 文件结构

### 新建

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/PlayerFeedbackEvents.cs` | `PlayerHealedEvent`、`LocalPlayerHitByPlayerEvent` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IPlayerHpSource.cs` | 表现层读取 HP 的最小接口（便于测试注入） |
| `Assets/Scripts/Hotfix/GameSystems/VFX/HitTintState.cs` | 受击泛红的触发门 + 计时（纯逻辑） |
| `Assets/Scripts/Hotfix/GameSystems/VFX/BodyTintRenderer.cs` | MaterialPropertyBlock 染色与恢复 |
| `Assets/Scripts/Hotfix/GameSystems/VFX/PlayerBodyTint.cs` | 本地玩家低血量/受击泛红组件 |
| `Assets/Scripts/Hotfix/GameSystems/VFX/PlayerHealVFX.cs` | 治疗事件 → 绿色粒子 |
| `Assets/Prefabs/VFX/HealGreenParticles.prefab` | 绿色上升回血粒子 |
| `Assets/Scripts/Tests/Editor/VFX/HitTintStateTests.cs` | 触发门/衰减单测 |
| `Assets/Scripts/Tests/Editor/VFX/BodyTintRendererTests.cs` | MaterialPropertyBlock 染色单测 |
| `Assets/Scripts/Tests/PlayMode/PlayerHealTests.cs` | Heal 数值/事件/死亡边界 |
| `Assets/Scripts/Tests/PlayMode/PlayerBodyTintTests.cs` | 组件染色集成测试 |
| `Assets/Scripts/Tests/PlayMode/PlayerHealVFXTests.cs` | 回血粒子生成测试 |
| `Assets/Scripts/Tests/PlayMode/DamageScreenFlashTests.cs` | 来源门控 + 闪屏消费测试 |

### 修改

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/AOT/Protocol/Messages/Combat.cs:85-101` | `DamageResult.SourceKind`（tag 8） |
| `Server/Handler/CastHandler.cs:27-38,424-437` | `CastResolution.SourceKind` 属性；广播时写入 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs:29-50,112-113` | 回调增加 `sourceIsPlayer` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:588-590` | 透传 `sourceIsPlayer` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:25,49-50,225-245,587,935` | 实现 `IPlayerHpSource`、`Heal`、来源事件、调试键 |
| `Assets/Scripts/AOT/DataDefinition/GameSettings.cs:27-40` | Player Feedback 配置 |
| `Assets/Resources/Setting/GameSettings.asset` | 显式写入新字段值 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs:43` | 运行时挂 `PlayerBodyTint` / `PlayerHealVFX` |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/DamageScreenEffect.cs:33-37` | 读玩家闪屏颜色/冷却；修初始冷却 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs:66-74` | 订阅/退订玩家来源受击事件 |
| `Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs` | 固定 `SourceKind` tag 8 |
| `Server/Server.Tests/Protocol/CombatMessagesTests.cs` | `SourceKind` 往返 |
| `Server/Server.Tests/Combat/CastHandlerTests.cs` | `CastResolution.SourceKind` 映射 |
| `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` | 7 参回调 + 来源标记测试 |

---

## 任务 1：协议 `SourceKind` 与伤害来源贯通

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Combat.cs:85-101`
- 修改：`Server/Handler/CastHandler.cs:27-38,424-437`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs:29-50,112-113`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:588-590`
- 测试：`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs`（追加）
- 测试：`Server/Server.Tests/Protocol/CombatMessagesTests.cs:36-79`
- 测试：`Server/Server.Tests/Combat/CastHandlerTests.cs`（追加）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs:147-194,264-288`（改回调）与追加

- [ ] **步骤 1：编写失败的测试**

在 `ProtocolShapeGuardTests.cs` 的 `DamageResult_ProjectileStop_Tags_Are_Pinned` 之后追加：

```csharp
        [Test]
        public void DamageResult_SourceKind_Tag_Is_Pinned()
        {
            // 只加不删的机器化防线：1..7 已发布，SourceKind 必须追加在 tag 8
            var prop = typeof(Cool.Protocol.DamageResult).GetProperty("SourceKind");
            Assert.IsNotNull(prop, "DamageResult 必须有 SourceKind");
            Assert.AreEqual(typeof(int), prop!.PropertyType);
            Assert.AreEqual(8, GetProtoMemberTag(prop!),
                "SourceKind 必须挂在 tag 8（1..7 是已发布的 wire 契约）");
        }
```

在 `CombatMessagesTests.cs` 的 `DamageResult` 初始化器中加入 `SourceKind = (int)EntityKind.Npc`，并在断言区加入：

```csharp
        Assert.Equal((int)EntityKind.Npc, damage.SourceKind,
            "SourceKind 未上线——客户端无法区分玩家/怪物攻击，闪屏会乱触发");
```

在 `CastHandlerTests.cs` 末尾（最后一个 `}` 之前）追加：

```csharp
    [Fact]
    public void test_cast_resolution_source_kind_maps_npc_and_player()
    {
        var npc = new CombatResolver.CastResolution { IsNpcCaster = true };
        var player = new CombatResolver.CastResolution { IsNpcCaster = false };

        Assert.Equal((int)EntityKind.Npc, npc.SourceKind);
        Assert.Equal((int)EntityKind.Player, player.SourceKind);
    }
```

把 `CombatNetworkBridgeTests.cs` 中三处 6 参 lambda（约 147、178、272 行）改为 7 参，并把现有 `SourceKind` 显式写进用例：

```csharp
// OnDamageResult_LocalTarget_Invokes_Damage_Sink
(targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
{
    sinkTarget = targetId;
    sinkDamage = damage;
    sinkCrit = isCritical;
    sinkRemainingHp = remainingHp;
});

// 该用例的消息体加：SourceKind = (int)EntityKind.Player

// OnDamageResult_LocalTarget_Passes_Blocked_Flag
(targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
{
    sinkDamage = damage;
    sinkBlocked = isBlocked;
});

// 该用例的消息体加：SourceKind = (int)EntityKind.Npc

// Monster_Source_Damage_To_Local_Player_Invokes_Local_Damage_Callback
(targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
{
    sinkDamage = damage;
    sinkRemainingHp = remainingHp;
});

// 该用例的消息体加：SourceKind = (int)EntityKind.Npc
```

在 `CombatNetworkBridgeTests.cs` 末尾追加：

```csharp
        [Test]
        public void test_combat_bridge_npc_source_is_not_player_source()
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            bool? sinkSourceIsPlayer = null;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                    sinkSourceIsPlayer = sourceIsPlayer);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 2_000_001,
                SourceKind = (int)EntityKind.Npc,
                SkillId = 50001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(false, sinkSourceIsPlayer, "怪物来源不能标记为玩家攻击，否则会触发红色闪屏");
        }

        [Test]
        public void test_combat_bridge_player_and_bot_sources_are_player_source()
        {
            AssertPlayerSource((int)EntityKind.Player);
            AssertPlayerSource((int)EntityKind.PlayerBot);
        }

        private void AssertPlayerSource(int sourceKind)
        {
            var registry = new RemoteEntityRegistry(_root.transform);
            bool? sinkSourceIsPlayer = null;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                    sinkSourceIsPlayer = sourceIsPlayer);

            bridge.OnMessage(MessageId.DamageResult, new DamageResult
            {
                SourcePlayerId = 3,
                SourceKind = sourceKind,
                SkillId = 20001,
                Hits = { new HitEntry { TargetPlayerId = 7, Damage = 10f, TargetRemainingHp = 90f } }
            });
            bridge.Update();

            Assert.AreEqual(true, sinkSourceIsPlayer, "玩家/bot 来源必须标记为玩家攻击");
        }
```

- [ ] **步骤 2：运行测试确认失败**

```
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
bash tools/test-server.sh --filter "FullyQualifiedName~CombatMessagesTests|FullyQualifiedName~CastHandlerTests"
```

预期：Unity 侧 `DamageResult` 无 `SourceKind` 属性，测试失败；服务端测试编译失败（`SourceKind` 不存在）。这是预期的红灯。

- [ ] **步骤 3：实现**

`Combat.cs` 的 `DamageResult` 末尾追加：

```csharp
        /// <summary>
        /// 来源种类（值同 EntityKind：0 Player / 1 Npc / 2 PlayerBot）。
        /// 只加不删：tag 8，老客户端读默认 0。客户端据此区分玩家来源闪屏与怪物攻击。
        /// </summary>
        [ProtoMember(8)] public int SourceKind { get; set; }
```

`CastHandler.cs` 的 `CastResolution` 内追加：

```csharp
            /// <summary>下行 DamageResult 的来源种类（值同 EntityKind）</summary>
            public int SourceKind => IsNpcCaster ? (int)EntityKind.Npc : (int)EntityKind.Player;
```

`BroadcastDamageAsync` 的 `new DamageResult { ... }` 中加入：

```csharp
                SourceKind = resolution.SourceKind,
```

`CombatNetworkBridge.cs` 回调类型与构造参数改为：

```csharp
        private readonly Action<long, float, bool, Vector3, float, bool, bool>? _onDamageToLocalPlayer;

        public CombatNetworkBridge(INetworkClient client, Func<long> localPlayerId,
            RemoteEntityRegistry registry,
            Action<long, float, bool, Vector3, float, bool, bool>? onDamageToLocalPlayer = null)
```

`ApplyDamageResult` 的本地玩家分支改为：

```csharp
                if (hit.TargetPlayerId == localId)
                {
                    bool sourceIsPlayer = result.SourceKind != (int)EntityKind.Npc;
                    _onDamageToLocalPlayer?.Invoke(hit.TargetPlayerId, hit.Damage, hit.IsCritical,
                        Vector3.zero, hit.TargetRemainingHp, hit.IsBlocked, sourceIsPlayer);
                    continue;
                }
```

`ClientNetworkHost.cs` 的 lambda 改为：

```csharp
                (targetId, dmg, crit, dir, remainingHp, blocked, sourceIsPlayer) =>
                    _localEntry.ApplyServerDamage(dmg, dir, remainingHp, blocked, sourceIsPlayer));
```

- [ ] **步骤 4：运行测试确认通过**

```
bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/test-server.sh --filter "FullyQualifiedName~CombatMessagesTests|FullyQualifiedName~CastHandlerTests"
```

预期：全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/Messages/Combat.cs \
  Server/Handler/CastHandler.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
  Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs \
  Server/Server.Tests/Protocol/CombatMessagesTests.cs \
  Server/Server.Tests/Combat/CastHandlerTests.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs
git commit -m "feat(combat): DamageResult 携带来源种类，客户端区分玩家/怪物攻击"
```

---

## 任务 2：`Sys3CEntry.Heal`、治疗事件与调试键

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/PlayerFeedbackEvents.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IPlayerHpSource.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:25,49-50,225-245,587-643,935`
- 测试：`Assets/Scripts/Tests/PlayMode/PlayerHealTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `PlayerHealTests.cs`：

```csharp
using System;
using System.Collections;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class PlayerHealTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab");
            _player = UnityEngine.Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry, "PlayerEntryForTest 上没有 Sys3CEntry");
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) UnityEngine.Object.DestroyImmediate(_player);
        }

        [UnityTest]
        public IEnumerator test_heal_positive_amount_restores_hp_and_emits_event()
        {
            _entry.ApplyServerDamage(60f, Vector3.zero, 40f);

            PlayerHealedEvent? captured = null;
            Action<PlayerHealedEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                _entry.Heal(30f);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(70f, _entry.CurrentHp, 0.01f);
            Assert.IsTrue(captured.HasValue, "实际回血必须发 PlayerHealedEvent");
            Assert.AreEqual(30f, captured.Value.Amount, 0.01f);
        }

        [UnityTest]
        public IEnumerator test_heal_clamps_to_max_hp()
        {
            _entry.ApplyServerDamage(60f, Vector3.zero, 40f);
            _entry.Heal(999f);
            yield return null;

            Assert.AreEqual(_entry.MaxHp, _entry.CurrentHp, 0.01f);
        }

        [UnityTest]
        public IEnumerator test_heal_dead_player_is_ignored_and_emits_nothing()
        {
            _entry.ApplyServerDamage(100f, Vector3.zero, 0f);

            bool emitted = false;
            Action<PlayerHealedEvent> handler = e => emitted = true;
            EventBus.Subscribe(handler);
            try
            {
                _entry.Heal(30f);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(0f, _entry.CurrentHp, 0.01f);
            Assert.IsFalse(emitted, "死亡状态不应回血，也不应发治疗事件");
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

```
bash tools/unity-tests.sh PlayMode PlayerHealTests
```

预期：编译失败——`Sys3CEntry.Heal` 返回类型不匹配（现有是 `void` 但空实现，测试可编译；`PlayerHealedEvent` 不存在导致编译失败）。这是预期的红灯。

- [ ] **步骤 3：实现**

创建 `PlayerFeedbackEvents.cs`：

```csharp
using Hotfix.GameSystems.Skills.Events;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>实际回血发生时发出（本地玩家）。表现层据此播绿色粒子。</summary>
    public struct PlayerHealedEvent : IEvent
    {
        public int EntityId;
        public float Amount;

        public PlayerHealedEvent(int entityId, float amount)
        {
            EntityId = entityId;
            Amount = amount;
        }
    }

    /// <summary>本地玩家被玩家/bot 来源命中（非格挡、非致死）时发出。UI 层据此触发红色闪屏。</summary>
    public struct LocalPlayerHitByPlayerEvent : IEvent
    {
        public int EntityId;

        public LocalPlayerHitByPlayerEvent(int entityId)
        {
            EntityId = entityId;
        }
    }
}
```

创建 `IPlayerHpSource.cs`：

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>表现层读取玩家 HP 的最小接口（便于测试注入）。</summary>
    public interface IPlayerHpSource
    {
        float HpPercent { get; }
        bool IsDead { get; }
    }
}
```

`Sys3CEntry.cs`：

1. 类声明改为：

```csharp
    public class Sys3CEntry : MonoBehaviour, IDamageable, IPlayerHpSource
```

2. 在 `CurrentHp` 旁加入：

```csharp
        public float HpPercent => _maxHP > 0f ? _currentHP / _maxHP : 0f;
```

3. `ApplyServerDamage` 签名改为：

```csharp
        public void ApplyServerDamage(float damage, Vector3 hitDirection, float? serverRemainingHp = null,
            bool isBlocked = false, bool sourceIsPlayer = false)
```

4. 在非格挡分支的 `PlayerTakeDamageEvent` 之后加入：

```csharp
            if (sourceIsPlayer && _currentHP > 0)
            {
                EventBus.Emit(new LocalPlayerHitByPlayerEvent(gameObject.GetInstanceID()));
            }
```

5. 把空的 `Heal` 替换为：

```csharp
        public void Heal(float amount)
        {
            if (amount <= 0f || _currentHP <= 0f) return; // 死亡不可治疗；负数/零忽略
            float before = _currentHP;
            _currentHP = Mathf.Min(_maxHP, _currentHP + amount);
            float actual = _currentHP - before;
            if (actual > 0f)
                EventBus.Emit(new PlayerHealedEvent(gameObject.GetInstanceID(), actual));
        }
```

6. 在 `Update` 的 `_inputManager.Update();` 之后加入：

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (UnityEngine.Input.GetKeyDown(KeyCode.H))
                Heal(30f);
#endif
```

- [ ] **步骤 4：运行测试确认通过**

```
bash tools/unity-tests.sh PlayMode PlayerHealTests
```

预期：3 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/PlayerFeedbackEvents.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IPlayerHpSource.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
  Assets/Scripts/Tests/PlayMode/PlayerHealTests.cs
git commit -m "feat(player): 实现回血与治疗事件，编辑器 H 键调试回血"
```

---

## 任务 3：低血量/受击泛红组件

**文件：**
- 修改：`Assets/Scripts/AOT/DataDefinition/GameSettings.cs:27-40`
- 修改：`Assets/Resources/Setting/GameSettings.asset`（MCP 显式写值）
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/HitTintState.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/BodyTintRenderer.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/PlayerBodyTint.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs:43`
- 测试：`Assets/Scripts/Tests/Editor/VFX/HitTintStateTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/VFX/BodyTintRendererTests.cs`
- 测试：`Assets/Scripts/Tests/PlayMode/PlayerBodyTintTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `HitTintStateTests.cs`：

```csharp
using Hotfix.GameSystems.VFX;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class HitTintStateTests
    {
        [Test]
        public void test_hit_tint_state_ignores_retrigger_while_active()
        {
            var state = new HitTintState();

            Assert.IsTrue(state.TryTrigger(1f), "首次受击应触发泛红");
            Assert.IsFalse(state.TryTrigger(1f), "泛红期间再次受击必须被忽略");
        }

        [Test]
        public void test_hit_tint_state_allows_retrigger_after_expiry()
        {
            var state = new HitTintState();
            state.TryTrigger(1f);

            state.Tick(1.01f);

            Assert.IsTrue(state.TryTrigger(1f), "泛红结束后应允许下一次触发");
        }

        [Test]
        public void test_hit_tint_state_amount_decays_to_zero()
        {
            var state = new HitTintState();
            state.TryTrigger(1f);

            Assert.AreEqual(1f, state.Amount(1f), 0.001f);
            state.Tick(0.5f);
            Assert.AreEqual(0.5f, state.Amount(1f), 0.001f);
            state.Tick(0.6f);
            Assert.AreEqual(0f, state.Amount(1f), 0.001f);
        }
    }
}
```

创建 `BodyTintRendererTests.cs`：

```csharp
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class BodyTintRendererTests
    {
        private GameObject _go;
        private MeshRenderer _renderer;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TintTest");
            _renderer = _go.AddComponent<MeshRenderer>();
            _material = new Material(Shader.Find("Standard"));
            _renderer.sharedMaterial = _material;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_material);
        }

        [Test]
        public void test_body_tint_renderer_applies_low_layer()
        {
            var tint = new BodyTintRenderer(new Renderer[] { _renderer });

            tint.Apply(Color.red, 0.5f, Color.red, 0f);

            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            var c = block.GetColor("_Color");
            Assert.Greater(c.r, 0.9f);
            Assert.Less(c.g, 0.6f);
        }

        [Test]
        public void test_body_tint_renderer_clear_restores_original()
        {
            var tint = new BodyTintRenderer(new Renderer[] { _renderer });
            tint.Apply(Color.red, 1f, Color.red, 0f);

            tint.Clear();

            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, "清除后渲染器不应再带 property block");
        }
    }
}
```

创建 `PlayerBodyTintTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class PlayerBodyTintTests
    {
        private sealed class FakeHpSource : IPlayerHpSource
        {
            public float HpPercent { get; set; }
            public bool IsDead { get; set; }
        }

        [UnityTest]
        public IEnumerator test_body_tint_hit_event_applies_red()
        {
            var go = new GameObject("TintTarget");
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = new Mesh();
            smr.sharedMaterial = new Material(Shader.Find("Standard"));
            var tint = go.AddComponent<PlayerBodyTint>();
            tint.Bind(new FakeHpSource { HpPercent = 1f });

            EventBus.Emit(new PlayerTakeDamageEvent(go.GetInstanceID(), Vector3.zero,
                HitZonePart.Torso, Vector3.zero, 10, Color.white));
            yield return null;

            var block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block);
            var c = block.GetColor("_Color");
            Assert.Greater(c.r, c.g, "受击泛红应提高红通道");

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator test_body_tint_low_hp_clears_after_recovery()
        {
            var go = new GameObject("TintTarget");
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = new Mesh();
            smr.sharedMaterial = new Material(Shader.Find("Standard"));
            var tint = go.AddComponent<PlayerBodyTint>();
            var hp = new FakeHpSource { HpPercent = 0.1f };
            tint.Bind(hp);
            yield return null;

            var block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block);
            Assert.Greater(block.GetColor("_Color").r, block.GetColor("_Color").g,
                "HP<20% 应持续泛红");

            hp.HpPercent = 0.5f;
            yield return null;

            block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, "回到阈值以上应清除染色");

            Object.Destroy(go);
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

```
bash tools/unity-tests.sh EditMode HitTintStateTests
bash tools/unity-tests.sh PlayMode PlayerBodyTintTests
```

预期：编译失败——`HitTintState`、`BodyTintRenderer`、`PlayerBodyTint` 不存在。这是预期的红灯。

- [ ] **步骤 3：实现**

在 `GameSettings.cs` 的 `Combat` 段之前加入：

```csharp
        [Header("Player Feedback")]
        [Tooltip("低血量阈值（0-1）")]
        [Range(0f, 1f)] public float LowHpThreshold = 0.2f;

        [Tooltip("低血量身体染色（alpha = 混合强度）")]
        public Color LowHpTintColor = new Color(0.75f, 0.05f, 0.05f, 0.45f);

        [Tooltip("受击身体泛红颜色（alpha = 混合强度）")]
        public Color HitTintColor = new Color(1f, 0.1f, 0.1f, 0.7f);

        [Tooltip("受击泛红持续时间（秒）")]
        public float HitTintDuration = 1f;

        [Tooltip("被玩家攻击时的屏幕闪屏颜色")]
        public Color PlayerHitFlashColor = new Color(0.9f, 0.05f, 0.05f, 1f);

        [Tooltip("屏幕闪屏冷却（秒）")]
        public float PlayerHitFlashCooldown = 0.8f;

        [Tooltip("回血绿色粒子预制体")]
        public GameObject HealVfxPrefab;
```

创建 `HitTintState.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>受击泛红的触发门与计时：泛红期间不允许再次触发。</summary>
    public sealed class HitTintState
    {
        private float _remaining;

        public bool IsHitActive => _remaining > 0f;

        public bool TryTrigger(float duration)
        {
            if (_remaining > 0f || duration <= 0f) return false;
            _remaining = duration;
            return true;
        }

        public void Tick(float deltaTime)
        {
            _remaining = Mathf.Max(0f, _remaining - deltaTime);
        }

        public float Amount(float duration)
        {
            return duration > 0f ? Mathf.Clamp01(_remaining / duration) : 0f;
        }
    }
}
```

创建 `BodyTintRenderer.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>用 MaterialPropertyBlock 给一组渲染器做身体染色，不污染共享材质。</summary>
    public sealed class BodyTintRenderer
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Renderer[] _renderers;
        private readonly Color[] _originals;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private bool _tinted;

        public BodyTintRenderer(Renderer[] renderers)
        {
            var valid = new List<Renderer>();
            var originals = new List<Color>();
            if (renderers != null)
            {
                foreach (var renderer in renderers)
                {
                    if (renderer == null || renderer.sharedMaterial == null
                        || !renderer.sharedMaterial.HasProperty(ColorId))
                        continue;
                    valid.Add(renderer);
                    originals.Add(renderer.sharedMaterial.GetColor(ColorId));
                }
            }
            _renderers = valid.ToArray();
            _originals = originals.ToArray();
        }

        public void Apply(Color lowColor, float lowAmount, Color hitColor, float hitAmount)
        {
            bool any = lowAmount > 0.001f || hitAmount > 0.001f;
            if (!any)
            {
                if (_tinted) Clear();
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Color c = Color.Lerp(_originals[i], lowColor, Mathf.Clamp01(lowAmount));
                c = Color.Lerp(c, hitColor, Mathf.Clamp01(hitAmount));
                _block.SetColor(ColorId, c);
                _renderers[i].SetPropertyBlock(_block);
            }
            _tinted = true;
        }

        public void Clear()
        {
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].SetPropertyBlock(null);
            _tinted = false;
        }
    }
}
```

创建 `PlayerBodyTint.cs`：

```csharp
using DataDefinition;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>本地玩家低血量持续泛红 + 受击泛红（期间不可重触发）。</summary>
    public sealed class PlayerBodyTint : MonoBehaviour
    {
        private IPlayerHpSource _hp;
        private BodyTintRenderer _tint;
        private readonly HitTintState _state = new HitTintState();
        private GameSettings _settings;

        private void Awake()
        {
            _hp = GetComponent<Sys3CEntry>();
            _settings = GameSettings.Instance;
            _tint = new BodyTintRenderer(GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        /// <summary>测试/宿主可注入替代的 HP 来源。</summary>
        public void Bind(IPlayerHpSource hp) => _hp = hp;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnDamaged);
            _tint?.Clear();
        }

        private void OnDestroy()
        {
            _tint?.Clear();
        }

        private void OnDamaged(PlayerTakeDamageEvent e)
        {
            if (_settings == null || e.EntityId != gameObject.GetInstanceID()) return;
            _state.TryTrigger(_settings.HitTintDuration);
        }

        private void Update()
        {
            if (_settings == null || _tint == null) return;

            _state.Tick(Time.deltaTime);

            float hpPercent = _hp != null ? _hp.HpPercent : 1f;
            bool lowActive = _hp != null && !_hp.IsDead && hpPercent > 0f
                             && hpPercent < _settings.LowHpThreshold;

            float lowAmount = _settings.LowHpTintColor.a * (lowActive ? 1f : 0f);
            float hitAmount = _settings.HitTintColor.a * _state.Amount(_settings.HitTintDuration);

            _tint.Apply(_settings.LowHpTintColor, lowAmount,
                _settings.HitTintColor, hitAmount);
        }
    }
}
```

`PlayerSpawner.cs` 加入 `using Hotfix.GameSystems.VFX;`，并在 `Instantiate` 之后：

```csharp
            _currentPlayer = Instantiate(prefab, spawnPos, Quaternion.identity);
            _currentPlayer.AddComponent<PlayerBodyTint>();
```

- [ ] **步骤 4：显式写入 GameSettings 资产**

用 MCP `script-execute` 执行：

```csharp
var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<DataDefinition.GameSettings>(
    "Assets/Resources/Setting/GameSettings.asset");
settings.LowHpThreshold = 0.2f;
settings.LowHpTintColor = new UnityEngine.Color(0.75f, 0.05f, 0.05f, 0.45f);
settings.HitTintColor = new UnityEngine.Color(1f, 0.1f, 0.1f, 0.7f);
settings.HitTintDuration = 1f;
settings.PlayerHitFlashColor = new UnityEngine.Color(0.9f, 0.05f, 0.05f, 1f);
settings.PlayerHitFlashCooldown = 0.8f;
UnityEditor.EditorUtility.SetDirty(settings);
UnityEditor.AssetDatabase.SaveAssets();
"ok"
```

验证：

```
unity-mcp-cli run-tool rql-query --input '{"query":"unity.asset(\"Assets/Resources/Setting/GameSettings.asset\") | select LowHpThreshold HitTintDuration PlayerHitFlashCooldown"}'
```

预期输出包含 `0.2 | 1 | 0.8`。

- [ ] **步骤 5：运行测试确认通过**

```
bash tools/unity-tests.sh EditMode HitTintStateTests
bash tools/unity-tests.sh EditMode BodyTintRendererTests
bash tools/unity-tests.sh PlayMode PlayerBodyTintTests
```

预期：全部 PASS。`PlayerBodyTintTests` 里的 `SkinnedMeshRenderer` 没有网格，可能产生无害的渲染警告；断言只读 MaterialPropertyBlock。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/AOT/DataDefinition/GameSettings.cs \
  Assets/Resources/Setting/GameSettings.asset \
  Assets/Scripts/Hotfix/GameSystems/VFX/HitTintState.cs \
  Assets/Scripts/Hotfix/GameSystems/VFX/BodyTintRenderer.cs \
  Assets/Scripts/Hotfix/GameSystems/VFX/PlayerBodyTint.cs \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs \
  Assets/Scripts/Tests/Editor/VFX/HitTintStateTests.cs \
  Assets/Scripts/Tests/Editor/VFX/BodyTintRendererTests.cs \
  Assets/Scripts/Tests/PlayMode/PlayerBodyTintTests.cs
# 新 .cs 的 .meta 也要一并 add
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitTintState.cs.meta \
  Assets/Scripts/Hotfix/GameSystems/VFX/BodyTintRenderer.cs.meta \
  Assets/Scripts/Hotfix/GameSystems/VFX/PlayerBodyTint.cs.meta \
  Assets/Scripts/Tests/Editor/VFX/HitTintStateTests.cs.meta \
  Assets/Scripts/Tests/Editor/VFX/BodyTintRendererTests.cs.meta \
  Assets/Scripts/Tests/PlayMode/PlayerBodyTintTests.cs.meta
git commit -m "feat(player): 低血量与受击身体泛红，参数集中在 GameSettings"
```

---

## 任务 4：回血绿色粒子预制体与 `PlayerHealVFX`

**文件：**
- 创建：`Assets/Prefabs/VFX/HealGreenParticles.prefab`
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/PlayerHealVFX.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs:43`
- 修改：`Assets/Resources/Setting/GameSettings.asset`（HealVfxPrefab 引用）
- 测试：`Assets/Scripts/Tests/PlayMode/PlayerHealVFXTests.cs`

- [ ] **步骤 1：创建回血粒子预制体**

用 MCP `script-execute` 执行：

```csharp
var go = new UnityEngine.GameObject("HealGreenParticles");
var ps = go.AddComponent<UnityEngine.ParticleSystem>();
var main = ps.main;
main.duration = 0.8f;
main.loop = false;
main.playOnAwake = true;
main.startLifetime = 0.8f;
main.startSpeed = 0.6f;
main.startSize = 0.15f;
main.startColor = new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f);
main.gravityModifier = -0.05f;
main.simulationSpace = UnityEngine.ParticleSystemSimulationSpace.Local;
main.maxParticles = 80;
var emission = ps.emission;
emission.rateOverTime = 0f;
emission.SetBursts(new[] { new UnityEngine.ParticleSystem.Burst(0f, 25) });
var shape = ps.shape;
shape.shapeType = UnityEngine.ParticleSystemShapeType.Sphere;
shape.radius = 0.4f;
var renderer = go.GetComponent<UnityEngine.ParticleSystemRenderer>();
renderer.sharedMaterial = UnityEditor.AssetDatabase.GetBuiltinExtraResource<UnityEngine.Material>("Default-Particle.mat");
UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/VFX/HealGreenParticles.prefab");
UnityEngine.Object.DestroyImmediate(go);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
"created"
```

- [ ] **步骤 2：验证预制体**

```
unity-mcp-cli run-tool rql-query --input '{"query":"unity.assets(\"t:Prefab\", \"Assets/Prefabs/VFX\") | where name == \"HealGreenParticles\" | select name"}'
```

预期：1 result。

- [ ] **步骤 3：编写失败的测试**

创建 `PlayerHealVFXTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class PlayerHealVFXTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab);
            _player = Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry);
            _player.AddComponent<PlayerHealVFX>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) Object.DestroyImmediate(_player);
        }

        [UnityTest]
        public IEnumerator test_heal_spawns_green_particle_child()
        {
            _entry.ApplyServerDamage(50f, Vector3.zero, 50f);
            _entry.Heal(10f);
            yield return null;

            var spawned = _player.transform.Find("HealGreenParticles(Clone)");
            Assert.IsNotNull(spawned, "实际回血后应生成 HealGreenParticles(Clone)");
            Assert.IsNotNull(spawned.GetComponent<ParticleSystem>());
        }
    }
}
```

- [ ] **步骤 4：运行测试确认失败**

```
bash tools/unity-tests.sh PlayMode PlayerHealVFXTests
```

预期：编译失败——`PlayerHealVFX` 不存在。这是预期的红灯。

- [ ] **步骤 5：实现**

创建 `PlayerHealVFX.cs`：

```csharp
using DataDefinition;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>本地玩家实际回血时，在身体位置播放绿色粒子。</summary>
    public sealed class PlayerHealVFX : MonoBehaviour
    {
        private GameSettings _settings;

        private void Awake()
        {
            _settings = GameSettings.Instance;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHealedEvent>(OnHealed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerHealedEvent>(OnHealed);
        }

        private void OnHealed(PlayerHealedEvent e)
        {
            if (_settings == null || _settings.HealVfxPrefab == null) return;
            if (e.EntityId != gameObject.GetInstanceID()) return;

            var go = Instantiate(_settings.HealVfxPrefab,
                transform.position + Vector3.up * 1.0f,
                Quaternion.identity, transform);

            float lifetime = 2f;
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                lifetime = main.duration + main.startLifetime.constantMax + 0.5f;
            }
            Destroy(go, lifetime);
        }
    }
}
```

`PlayerSpawner.cs` 再追加：

```csharp
            _currentPlayer.AddComponent<PlayerHealVFX>();
```

用 MCP `script-execute` 写入 GameSettings 引用：

```csharp
var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<DataDefinition.GameSettings>(
    "Assets/Resources/Setting/GameSettings.asset");
settings.HealVfxPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
    "Assets/Prefabs/VFX/HealGreenParticles.prefab");
UnityEditor.EditorUtility.SetDirty(settings);
UnityEditor.AssetDatabase.SaveAssets();
"ok"
```

- [ ] **步骤 6：运行测试确认通过**

```
bash tools/unity-tests.sh PlayMode PlayerHealVFXTests
```

预期：PASS。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Prefabs/VFX/HealGreenParticles.prefab \
  Assets/Prefabs/VFX/HealGreenParticles.prefab.meta \
  Assets/Scripts/Hotfix/GameSystems/VFX/PlayerHealVFX.cs \
  Assets/Scripts/Hotfix/GameSystems/VFX/PlayerHealVFX.cs.meta \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/PlayerSpawner.cs \
  Assets/Resources/Setting/GameSettings.asset \
  Assets/Scripts/Tests/PlayMode/PlayerHealVFXTests.cs \
  Assets/Scripts/Tests/PlayMode/PlayerHealVFXTests.cs.meta
git commit -m "feat(player): 回血绿色粒子与玩家生成器挂载"
```

---

## 任务 5：玩家来源红色闪屏

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/DamageScreenEffect.cs:33-37`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs:66-74`
- 测试：`Assets/Scripts/Tests/PlayMode/DamageScreenFlashTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `DamageScreenFlashTests.cs`：

```csharp
using System;
using System.Collections;
using Hotfix.GameSystems.Nameplate;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameSys.PlayModeTests
{
    public class DamageScreenFlashTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab);
            _player = UnityEngine.Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) UnityEngine.Object.DestroyImmediate(_player);

            var display = UnityEngine.Object.FindObjectOfType<EntityDisplayManager>();
            if (display != null) UnityEngine.Object.DestroyImmediate(display.gameObject);
        }

        [UnityTest]
        public IEnumerator test_npc_source_damage_does_not_emit_flash_event()
        {
            int count = 0;
            Action<LocalPlayerHitByPlayerEvent> handler = e => count++;
            EventBus.Subscribe(handler);
            try
            {
                _entry.ApplyServerDamage(10f, Vector3.zero, 90f,
                    isBlocked: false, sourceIsPlayer: false);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(0, count, "怪物来源不应触发玩家攻击闪屏事件");
        }

        [UnityTest]
        public IEnumerator test_player_source_gating_ignores_blocked_hit()
        {
            int count = 0;
            Action<LocalPlayerHitByPlayerEvent> handler = e => count++;
            EventBus.Subscribe(handler);
            try
            {
                _entry.ApplyServerDamage(10f, Vector3.zero, 90f,
                    isBlocked: true, sourceIsPlayer: true);
                yield return null;

                _entry.ApplyServerDamage(10f, Vector3.zero, 80f,
                    isBlocked: false, sourceIsPlayer: true);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(1, count, "格挡不闪屏；非格挡玩家来源应闪一次");
        }

        [UnityTest]
        public IEnumerator test_player_source_event_flashes_overlay()
        {
            var existing = UnityEngine.Object.FindObjectOfType<EntityDisplayManager>();
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var display = new GameObject("DisplayTest");
            display.AddComponent<EntityDisplayManager>();
            yield return null;

            EventBus.Emit(new LocalPlayerHitByPlayerEvent(123));
            yield return new WaitForSeconds(0.12f);

            var overlay = GameObject.Find("DamageOverlay");
            Assert.IsNotNull(overlay, "闪屏 overlay 未创建");
            var image = overlay.GetComponent<Image>();
            Assert.Greater(image.color.a, 0f, "玩家来源受击后 overlay 应可见");

            UnityEngine.Object.Destroy(display);
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

```
bash tools/unity-tests.sh PlayMode DamageScreenFlashTests
```

预期：
- `test_npc_source_damage_does_not_emit_flash_event` PASS（任务 2 已实现来源门控）；
- `test_player_source_gating_ignores_blocked_hit` PASS；
- `test_player_source_event_flashes_overlay` FAIL——`EntityDisplayManager` 尚未订阅事件，overlay alpha 为 0。

- [ ] **步骤 3：实现**

`DamageScreenEffect.cs` 构造函数改为：

```csharp
            _overlay.sprite = GameSettings.Instance.HitFlashSprite;
            var initColor = GameSettings.Instance.PlayerHitFlashColor;
            _overlay.color = new Color(initColor.r, initColor.g, initColor.b, 0f);
            _cooldown = GameSettings.Instance.PlayerHitFlashCooldown;
            _lastFlashTime = -_cooldown; // 开局即可闪，不被初始冷却挡住
```

`EntityDisplayManager.cs` 顶部加入：

```csharp
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
```

`OnEnable/OnDisable` 改为：

```csharp
        private void OnEnable()
        {
            _eventBridge?.Enable();
            EventBus.Subscribe<LocalPlayerHitByPlayerEvent>(OnLocalPlayerHitByPlayer);
        }

        private void OnDisable()
        {
            _eventBridge?.Disable();
            EventBus.Unsubscribe<LocalPlayerHitByPlayerEvent>(OnLocalPlayerHitByPlayer);
        }

        private void OnLocalPlayerHitByPlayer(LocalPlayerHitByPlayerEvent e)
        {
            _damageScreenEffect?.Flash();
        }
```

- [ ] **步骤 4：运行测试确认通过**

```
bash tools/unity-tests.sh PlayMode DamageScreenFlashTests
```

预期：3 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/DamageScreenEffect.cs \
  Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs \
  Assets/Scripts/Tests/PlayMode/DamageScreenFlashTests.cs \
  Assets/Scripts/Tests/PlayMode/DamageScreenFlashTests.cs.meta
git commit -m "feat(ui): 玩家来源受击触发红色闪屏，怪物来源不触发"
```

---

## 任务 6：全量验证与视觉验收

**文件：** 无新增代码；只做验证与参数微调。

- [ ] **步骤 1：跑全量测试**

```
bash tools/test-server.sh
bash tools/unity-tests.sh EditMode
bash tools/unity-tests.sh PlayMode
```

预期：服务端全绿；Unity EditMode/PlayMode 全绿。若 PlayMode 全量因既有环境依赖失败，至少确保本计划新增的 4 个测试类与 `CombatNetworkBridgeTests`、`ProtocolShapeGuardTests` 全绿。

- [ ] **步骤 2：进入 PlayMode 做视觉验收**

用 MCP `script-execute` 把编辑器切到运行态：

```csharp
UnityEditor.EditorApplication.isPlaying = true;
"playing"
```

> 进入 PlayMode 会触发域重载，MCP 工具可能短暂返回 503 或 session expired。等待约 10–20 秒；若原生工具消失，改用 `unity-mcp-cli run-tool script-execute --input '{"csharpCode":"..."}'`。

- [ ] **步骤 3：低血量泛红截图**

在 PlayMode 中执行：

```csharp
var entry = UnityEngine.Object.FindObjectOfType<Hotfix.GameSystems.Sys3C.Sys3CEntry>();
entry.ApplyServerDamage(85f, UnityEngine.Vector3.zero, 15f);
"hp=" + entry.CurrentHp + " max=" + entry.MaxHp
```

预期：HP=15/100（<20%）。等 0.3s 后用 MCP `screenshot-game-view` 截图，保存到 `production/verify/low-hp.png`，再用 `python tools/shrink_image.py production/verify/low-hp.png production/verify/low-hp-small.jpg` 缩到 ≤1024px 后查看。

验收：角色身体呈红色（与普通状态截图对比）。

- [ ] **步骤 4：受击泛红截图**

在 PlayMode 中执行：

```csharp
var entry = UnityEngine.Object.FindObjectOfType<Hotfix.GameSystems.Sys3C.Sys3CEntry>();
entry.ApplyServerDamage(10f, UnityEngine.Vector3.forward, entry.CurrentHp - 10f);
"hp=" + entry.CurrentHp
```

立即（0.2s 内）截图 `production/verify/hit-tint.png` 并缩图查看。验收：身体比低血量状态更亮的红色；约 1s 后恢复低血量红色（或原色）。

- [ ] **步骤 5：回血粒子截图**

在 PlayMode 中执行：

```csharp
var entry = UnityEngine.Object.FindObjectOfType<Hotfix.GameSystems.Sys3C.Sys3CEntry>();
entry.Heal(30f);
"hp=" + entry.CurrentHp
```

立即截图 `production/verify/heal-vfx.png`。验收：角色身边出现绿色上升粒子。

- [ ] **步骤 6：退出 PlayMode 并微调**

```csharp
UnityEditor.EditorApplication.isPlaying = false;
"stopped"
```

如果截图中泛红过暗/过亮、粒子太稀或闪屏不明显，只改 `GameSettings.asset` 的数值/颜色（不改代码），然后重跑：

```
bash tools/unity-tests.sh EditMode BodyTintRendererTests
bash tools/unity-tests.sh PlayMode PlayerBodyTintTests
bash tools/unity-tests.sh PlayMode PlayerHealVFXTests
bash tools/unity-tests.sh PlayMode DamageScreenFlashTests
```

- [ ] **步骤 7：最终提交（仅当步骤 6 调整了参数）**

```bash
git add Assets/Resources/Setting/GameSettings.asset
git commit -m "tune(player-feedback): 按实机截图调整泛红与回血粒子参数"
```

---

## 计划自检

- **规格覆盖度：** 规格 §4.1 → 任务 1；§4.2 → 任务 1、5；§4.3 → 任务 2；§4.4 → 任务 2；§4.5-4.6 → 任务 3；§4.7-4.8 → 任务 4；§4.9 → 任务 5；§4.10 → 任务 3/4；§5 测试计划 → 各任务测试 + 任务 6。
- **占位符扫描：** 无“待定/TODO/后续实现”；每个代码步骤都有可直接粘贴的代码。
- **类型一致性：** `SourceKind`（int，tag 8）、`PlayerHealedEvent`、`LocalPlayerHitByPlayerEvent`、`IPlayerHpSource`、`HitTintState`、`BodyTintRenderer`、`PlayerBodyTint`、`PlayerHealVFX` 的签名在各任务中一致。
- **已知取舍：** 调试键只在 Editor/Development 编译；正式包暂无回血触发源（规格 §6.7）。