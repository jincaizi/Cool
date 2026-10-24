# 格挡成功反馈（火花 + 飘字）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 举盾格挡成功时，在本地玩家与远程玩家/bot 身上给出金色「格挡」飘字和金色火花，替代当前只有 0.4s `DefendHit` 动画的弱反馈。

**架构：** 新增纯表现事件 `BlockSucceededEvent`。单机与联机本地格挡在 `Sys3CEntry` 的两条格挡分支发射；远程玩家/bot 由 `CombatNetworkBridge` 的远程分支发射并跳过伤害数字。`DisplayEventBridge` 订阅后出飘字（复用 `FloatText_Block.asset`），`HitFeedbackSystem` 订阅后出火花（复用 `HitSpark.prefab` 池）。不改协议、不改伤害结算、不动格挡判定。

**技术栈：** Unity 2022.3.25f1、C#、UGUI/TextMeshPro、DOTween、Unity Test Framework（EditMode/PlayMode）、Unity MCP。

---

## 0. 文件结构

**创建：**

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/BlockSucceededEvent.cs` | 格挡成功的纯表现事件 |
| `Assets/Scripts/Tests/PlayMode/BlockFeedbackDisplayTests.cs` | 飘字生成与 0.2s 去重的 PlayMode 测试 |
| `Assets/Scripts/Tests/PlayMode/BlockFeedbackSparkTests.cs` | 火花位置/缩放/颜色的 PlayMode 测试 |

**修改：**

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 本地两条格挡分支发射事件 + 一个私有辅助方法 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | 远程玩家/bot 格挡分支发射事件并跳过伤害数字 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs` | 订阅事件、0.2s 去重、出「格挡」飘字 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs` | 新增 `_blockSettings` 字段并传入桥 |
| `Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs` | 订阅事件、金色火花 |
| `Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` | 3 个桥接层回归用例 |
| `Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs` | 4 个本地格挡事件用例 |
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` | 给 `EntityDisplayManager` 接 `FloatText_Block.asset` |

**测试命令：**

- EditMode：`bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests`
- PlayMode：`bash tools/unity-tests.sh PlayMode BlockStaminaTests`
- PlayMode：`bash tools/unity-tests.sh PlayMode BlockFeedbackDisplayTests`
- PlayMode：`bash tools/unity-tests.sh PlayMode BlockFeedbackSparkTests`

---

## 任务 1：事件类型 + 远程桥接分支

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/BlockSucceededEvent.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`（`ApplyDamageResult` 远程分支，约 122-145 行）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs`（追加用例）

- [ ] **步骤 1：创建事件结构**

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

- [ ] **步骤 2：写三个失败的 Editor 测试**

在 `CombatNetworkBridgeTests` 类内、`OnDamageResult_RemoteTarget_Emits_PlayerTakeDamageEvent` 之后追加：

```csharp
        [Test]
        public void OnDamageResult_RemoteBlockedTarget_Emits_BlockSucceededEvent_Not_PlayerTakeDamageEvent()
        {
            // 远程玩家/bot 被格挡：飘字改为「格挡」，不再出减免后的伤害数字
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = 1f, Y = 0f, Z = 2f
            }, serverTicks: 1_000_000L);
            var view = registry.GetView(2);
            Assert.NotNull(view, "视图未创建——ApplySnapshot Position 位应触发");

            BlockSucceededEvent? blockCaptured = null;
            PlayerTakeDamageEvent? playerCaptured = null;
            Action<BlockSucceededEvent> blockHandler = e => blockCaptured = e;
            Action<PlayerTakeDamageEvent> playerHandler = e => playerCaptured = e;
            EventBus.Subscribe(blockHandler);
            EventBus.Subscribe(playerHandler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 20001,
                    Hits = { new HitEntry { TargetPlayerId = 2, Damage = 3f, IsBlocked = true, TargetRemainingHp = 97f } }
                });
                bridge.Update();

                Assert.IsTrue(blockCaptured.HasValue, "远程格挡未发 BlockSucceededEvent");
                Assert.IsFalse(playerCaptured.HasValue,
                    "远程格挡不得再发 PlayerTakeDamageEvent（伤害数字会被重复）");
                Assert.AreEqual(view.GameObject.GetInstanceID(), blockCaptured.Value.EntityId);
                Assert.AreEqual(view.Position.x, blockCaptured.Value.HitPosition.x, 0.001f);
                Assert.AreEqual(view.Position.y + 1.2f, blockCaptured.Value.HitPosition.y, 0.001f);
                Assert.AreEqual(view.Position.z, blockCaptured.Value.HitPosition.z, 0.001f);
                Assert.AreEqual(view.Position.y + 1.5f, blockCaptured.Value.TextPosition.y, 0.001f);
            }
            finally
            {
                EventBus.Unsubscribe(blockHandler);
                EventBus.Unsubscribe(playerHandler);
            }
        }

        [Test]
        public void OnDamageResult_RemoteUnblockedTarget_Does_Not_Emit_BlockSucceededEvent()
        {
            // 回归护栏：普通远程命中仍走 PlayerTakeDamageEvent
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = 1f, Y = 0f, Z = 2f
            }, serverTicks: 1_000_000L);

            BlockSucceededEvent? blockCaptured = null;
            PlayerTakeDamageEvent? playerCaptured = null;
            Action<BlockSucceededEvent> blockHandler = e => blockCaptured = e;
            Action<PlayerTakeDamageEvent> playerHandler = e => playerCaptured = e;
            EventBus.Subscribe(blockHandler);
            EventBus.Subscribe(playerHandler);
            try
            {
                var bridge = new CombatNetworkBridge(_client, () => 1, registry);
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 20001,
                    Hits = { new HitEntry { TargetPlayerId = 2, Damage = 57.4f, IsBlocked = false, TargetRemainingHp = 42.6f } }
                });
                bridge.Update();

                Assert.IsFalse(blockCaptured.HasValue, "非格挡远程命中不得发 BlockSucceededEvent");
                Assert.IsTrue(playerCaptured.HasValue, "非格挡远程命中必须仍走 PlayerTakeDamageEvent");
            }
            finally
            {
                EventBus.Unsubscribe(blockHandler);
                EventBus.Unsubscribe(playerHandler);
            }
        }

        [Test]
        public void OnDamageResult_LocalBlockedTarget_Does_Not_Emit_BlockSucceededEvent()
        {
            // 本地格挡由 Sys3CEntry.ApplyServerDamage 发事件；桥不得越权重复发
            var registry = new RemoteEntityRegistry(_root.transform);
            int sinkCalls = 0;
            var bridge = new CombatNetworkBridge(_client, () => 7, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) => sinkCalls++);

            BlockSucceededEvent? blockCaptured = null;
            Action<BlockSucceededEvent> blockHandler = e => blockCaptured = e;
            EventBus.Subscribe(blockHandler);
            try
            {
                bridge.OnMessage(MessageId.DamageResult, new DamageResult
                {
                    SourcePlayerId = 1, SkillId = 20001,
                    Hits = { new HitEntry { TargetPlayerId = 7, Damage = 3f, IsBlocked = true, TargetRemainingHp = 97f } }
                });
                bridge.Update();

                Assert.AreEqual(1, sinkCalls, "本地格挡必须走 ApplyServerDamage 注入点");
                Assert.IsFalse(blockCaptured.HasValue,
                    "桥不得越权发本地格挡事件（会与 ApplyServerDamage 重复）");
            }
            finally
            {
                EventBus.Unsubscribe(blockHandler);
            }
        }
```

- [ ] **步骤 3：运行测试确认失败**

运行：`bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests`

预期：新增 3 个用例中至少 `OnDamageResult_RemoteBlockedTarget_...` 失败（`blockCaptured` 为 null）；其余两个因为桥尚未发事件，可能直接通过——这是正常的，它们是回归护栏。

- [ ] **步骤 4：在桥接层发射事件**

先更新类头 XML 注释：在「远程 **玩家/bot** 视图 Emit PlayerTakeDamageEvent（走玩家灰字）」后补一句「被格挡时改发 `BlockSucceededEvent`（格挡飘字 + 火花），不再飘伤害数字」。

然后在 `CombatNetworkBridge.ApplyDamageResult` 中，把这段（连同上面两行已过时的注释）：

```csharp
                // 远程实体：被格挡时沿用既有飘字链路（Damage 已是减免后的值）；远程格挡动画
                // 无同步通道（表现层只做本地玩家，P4 backlog——见格挡减伤交付报告）
                var view = _registry.GetView(hit.TargetPlayerId);
                if (view == null) continue; // 视野外无视图（AOI 下不应发生——防御跳过）

                int viewId = view.GameObject.GetInstanceID();
                Vector3 hitPos = view.Position + Vector3.up * 1.5f;
                if (view.Kind == (byte)EntityKind.Npc)
```

改成：

```csharp
                // 远程实体：被格挡时发改发表现事件（飘字/火花），不再出减免后的伤害数字；
                // 远程格挡动画仍无同步通道（P4 backlog——见格挡减伤交付报告）
                var view = _registry.GetView(hit.TargetPlayerId);
                if (view == null) continue; // 视野外无视图（AOI 下不应发生——防御跳过）

                int viewId = view.GameObject.GetInstanceID();
                Vector3 hitPos = view.Position + Vector3.up * 1.5f;

                // 玩家/bot 专用；Npc 永不格挡，守卫防未来服务端给 Npc 置 IsBlocked 时吞掉红字
                if (hit.IsBlocked && view.Kind != (byte)EntityKind.Npc)
                {
                    EventBus.Emit(new BlockSucceededEvent(
                        view.GameObject.GetInstanceID(),
                        view.Position + Vector3.up * 1.2f,
                        hitPos));
                    continue;
                }

                if (view.Kind == (byte)EntityKind.Npc)
```

- [ ] **步骤 5：运行测试确认通过**

运行：`bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests`

预期：全部用例 PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/BlockSucceededEvent.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/BlockSucceededEvent.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs
git commit -m "feat(combat): 远程格挡成功发 BlockSucceededEvent 并替代伤害数字"
```

---

## 任务 2：本地玩家两条格挡分支

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`（`ConsumeBlockStamina` 之后；`ApplyServerDamage` 约 616-635；`IDamageable.TakeDamage` 约 717-727）
- 测试：`Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs`

- [ ] **步骤 1：给测试文件补 using**

在 `BlockStaminaTests.cs` 顶部加入：

```csharp
using System;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
```

- [ ] **步骤 2：写四个失败的 PlayMode 测试**

在 `BlockStaminaTests` 类内、`test_block_stamina_offline_block_consumes_fifteen` 之后追加：

```csharp
        [Test]
        public void test_block_feedback_online_block_emits_block_succeeded_event()
        {
            // Arrange
            BlockSucceededEvent? captured = null;
            Action<BlockSucceededEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                // Act
                _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);

                // Assert
                Assert.IsTrue(captured.HasValue, "联机格挡成功未发 BlockSucceededEvent");
                Assert.AreEqual(_entry.gameObject.GetInstanceID(), captured.Value.EntityId);
                Assert.AreEqual(_entry.transform.position.y + 1.5f,
                    captured.Value.TextPosition.y, 0.001f);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }

        [Test]
        public void test_block_feedback_online_non_blocked_does_not_emit_event()
        {
            // Arrange
            BlockSucceededEvent? captured = null;
            Action<BlockSucceededEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                // Act
                _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: false);

                // Assert
                Assert.IsFalse(captured.HasValue, "非格挡命中不得发 BlockSucceededEvent");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }

        [Test]
        public void test_block_feedback_offline_block_emits_block_succeeded_event()
        {
            // Arrange
            EnterDefend();
            BlockSucceededEvent? captured = null;
            Action<BlockSucceededEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                // Act
                ((IDamageable)_entry).TakeDamage(
                    DamageBlock.CreateDefault(50f, 0f), -_entry.transform.forward);

                // Assert
                Assert.IsTrue(captured.HasValue, "单机格挡成功未发 BlockSucceededEvent");
                Assert.AreEqual(_entry.gameObject.GetInstanceID(), captured.Value.EntityId);
                Assert.AreEqual(_entry.transform.position.y + 1.5f,
                    captured.Value.TextPosition.y, 0.001f);
                Assert.Greater(captured.Value.HitPosition.sqrMagnitude, 0.0001f,
                    "HitPosition 不得是零向量");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }

        [Test]
        public void test_block_feedback_offline_non_blocked_does_not_emit_event()
        {
            // Arrange
            BlockSucceededEvent? captured = null;
            Action<BlockSucceededEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                // Act：不进入防御，正面受击走普通受击
                ((IDamageable)_entry).TakeDamage(
                    DamageBlock.CreateDefault(10f, 0f), -_entry.transform.forward);

                // Assert
                Assert.IsFalse(captured.HasValue, "未举盾受击不得发 BlockSucceededEvent");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }
```

- [ ] **步骤 3：运行测试确认失败**

运行：`bash tools/unity-tests.sh PlayMode BlockStaminaTests`

预期：`test_block_feedback_online_block_emits_block_succeeded_event` 与 `test_block_feedback_offline_block_emits_block_succeeded_event` FAIL（事件未发射）；两条 non-blocked 用例通过（回归护栏）。

- [ ] **步骤 4：加发射辅助方法**

在 `Sys3CEntry.ConsumeBlockStamina` 方法之后加入：

```csharp
        /// <summary>格挡成功的纯表现事件（火花/飘字）；不参与结算。</summary>
        private void EmitBlockSucceeded(Vector3 hitPosition)
        {
            EventBus.Emit(new BlockSucceededEvent(
                gameObject.GetInstanceID(),
                hitPosition,
                transform.position + Vector3.up * 1.5f));
        }
```

- [ ] **步骤 5：联机本地格挡分支发射事件**

先更新 `ApplyServerDamage` 上方的 XML 注释：把「浮字不发（与单机格挡一致：格挡成功本就跳过 `PlayerTakeDamageEvent`）」改成「不飘伤害数字，改发 `BlockSucceededEvent`（格挡飘字 + 火花）」。

然后在 `ApplyServerDamage` 的 `if (isBlocked)` 分支里，把：

```csharp
                ConsumeBlockStamina();

                if (_currentHP <= 0)
```

改成：

```csharp
                ConsumeBlockStamina();

                // 协议无方向字段（hitDirection 恒为零）：举盾时正面朝敌，用正前方近似盾面点
                EmitBlockSucceeded(
                    transform.position + Vector3.up * 1.3f + transform.forward * 0.4f);

                if (_currentHP <= 0)
```

- [ ] **步骤 6：单机本地格挡分支发射事件**

在 `IDamageable.TakeDamage` 里，把：

```csharp
            // 格挡成功 → 扣精 + 播 DefendHit
            if (_cc.IsDefending && wasBlocked)
            {
                ConsumeBlockStamina();
                _fsmManager.HitFSM.EnterDefendHit(new HitData
                {
                    Damage = finalDamage,
                    HitDirection = hitDirection
                });
                _fsmManager.Coordinator.SetActiveLayer(LayerType.Hit);
            }
```

改成：

```csharp
            // 格挡成功 → 扣精 + 播 DefendHit + 表现事件
            if (_cc.IsDefending && wasBlocked)
            {
                ConsumeBlockStamina();
                _fsmManager.HitFSM.EnterDefendHit(new HitData
                {
                    Damage = finalDamage,
                    HitDirection = hitDirection
                });
                _fsmManager.Coordinator.SetActiveLayer(LayerType.Hit);

                // 有真实接触点就用接触点；无碰撞箱路径回退到胸前前方，避免零向量
                Vector3 sparkPosition = resolved.HasContact
                    ? resolved.HitPoint
                    : transform.position + Vector3.up * 1.3f + transform.forward * 0.4f;
                EmitBlockSucceeded(sparkPosition);
            }
```

- [ ] **步骤 7：运行测试确认通过**

运行：`bash tools/unity-tests.sh PlayMode BlockStaminaTests`

预期：四个新用例 + 既有 `BlockStaminaTests` 全部 PASS。

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/PlayMode/BlockStaminaTests.cs
git commit -m "feat(combat): 本地格挡成功发 BlockSucceededEvent"
```

---

## 任务 3：飘字表现 + 场景接线

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 测试：创建 `Assets/Scripts/Tests/PlayMode/BlockFeedbackDisplayTests.cs`

- [ ] **步骤 1：写失败测试**

创建 `Assets/Scripts/Tests/PlayMode/BlockFeedbackDisplayTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.Nameplate;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>格挡飘字：生成 + 同实体 0.2s 去重。直接测 DisplayEventBridge，不依赖场景接线。</summary>
    public class BlockFeedbackDisplayTests
    {
        private GameObject _host;
        private FloatTextRenderer _renderer;
        private FloatTextSettings _blockSettings;
        private DisplayEventBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            FloatTextRenderer.EnableVFX = true;
            _host = new GameObject("BlockDisplayTest");
            _host.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _renderer = new FloatTextRenderer(_host.transform);
            _blockSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
            _blockSettings.Type = FloatTextType.Block;
            _blockSettings.FontSize = 28f;
            _blockSettings.Duration = 0.6f;
            _bridge = new DisplayEventBridge(
                _renderer, _blockSettings, _blockSettings, _blockSettings, _blockSettings);
            _bridge.Enable();
        }

        [TearDown]
        public void TearDown()
        {
            _bridge.Disable();
            _renderer.Cleanup();
            Object.DestroyImmediate(_blockSettings);
            Object.DestroyImmediate(_host);
        }

        [UnityTest]
        public IEnumerator test_block_feedback_spawns_block_text()
        {
            // Act
            EventBus.Emit(new BlockSucceededEvent(42, Vector3.zero, Vector3.up * 1.5f));
            yield return null;

            // Assert
            var texts = _host.GetComponentsInChildren<TextMeshProUGUI>();
            Assert.AreEqual(1, texts.Length, "格挡成功应生成一个飘字");
            Assert.AreEqual("格挡", texts[0].text);
        }

        [UnityTest]
        public IEnumerator test_block_feedback_dedupes_within_window()
        {
            // Act：同一实体同帧两段命中
            EventBus.Emit(new BlockSucceededEvent(42, Vector3.zero, Vector3.up * 1.5f));
            EventBus.Emit(new BlockSucceededEvent(42, Vector3.zero, Vector3.up * 1.5f));
            yield return null;

            // Assert
            var texts = _host.GetComponentsInChildren<TextMeshProUGUI>();
            Assert.AreEqual(1, texts.Length, "0.2s 内同一实体只显示一次「格挡」");
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh PlayMode BlockFeedbackDisplayTests`

预期：编译失败——`DisplayEventBridge` 还没有五参数构造函数。

- [ ] **步骤 3：改 `DisplayEventBridge`**

把文件内容改成：

```csharp
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Nameplate
{
    public class DisplayEventBridge
    {
        private const float BlockMergeWindow = 0.2f;

        private readonly FloatTextRenderer _floatText;
        private readonly FloatTextSettings _damageSettings;
        private readonly FloatTextSettings _critDamageSettings;
        private readonly FloatTextSettings _playerDamageSettings;
        private readonly FloatTextSettings _blockSettings;
        private readonly Dictionary<int, float> _lastBlockTime = new Dictionary<int, float>();
        private bool _warnedMissingBlockSettings;

        public DisplayEventBridge(
            FloatTextRenderer floatText,
            FloatTextSettings damageSettings,
            FloatTextSettings critDamageSettings,
            FloatTextSettings playerDamageSettings,
            FloatTextSettings blockSettings)
        {
            _floatText = floatText;
            _damageSettings = damageSettings;
            _critDamageSettings = critDamageSettings;
            _playerDamageSettings = playerDamageSettings;
            _blockSettings = blockSettings;
        }

        public void Enable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterDotDamageEvent>(OnMonsterDotDamaged);
            EventBus.Subscribe<BlockSucceededEvent>(OnBlockSucceeded);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterDotDamageEvent>(OnMonsterDotDamaged);
            EventBus.Unsubscribe<BlockSucceededEvent>(OnBlockSucceeded);
            _lastBlockTime.Clear();
        }

        private void OnPlayerDamaged(PlayerTakeDamageEvent e)
        {
            // 玩家掉血飘字使用专属配置(灰字+黑描边),与怪物掉血(红字)区分
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _playerDamageSettings, e.Damage);
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, settings, e.Damage);
        }

        private void OnMonsterDotDamaged(MonsterDotDamageEvent e)
        {
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _damageSettings, e.Damage);
        }

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

            float now = Time.time;
            if (_lastBlockTime.TryGetValue(e.EntityId, out float last)
                && now - last < BlockMergeWindow) return;
            _lastBlockTime[e.EntityId] = now;

            _floatText.ShowFloatingText(e.TextPosition, _blockSettings, "格挡");
        }
    }
}
```

- [ ] **步骤 4：改 `EntityDisplayManager`**

加字段（跟在 `_skillNameSettings` 后）：

```csharp
        [SerializeField] private FloatTextSettings _blockSettings;
```

把构造函数调用改成：

```csharp
            _eventBridge = new DisplayEventBridge(
                _floatText,
                _damageSettings, _critDamageSettings, _playerDamageSettings, _blockSettings);
```

- [ ] **步骤 5：运行测试确认通过**

运行：`bash tools/unity-tests.sh PlayMode BlockFeedbackDisplayTests`

预期：2 个用例 PASS。

- [ ] **步骤 6：场景接线**

DemoDay 里只有一处 `EntityDisplayManager`（约 7211 行）。

- **Unity 编辑器开着（推荐）**：打开 DemoDay，选中 `EntityDisplayManager`，把 `Assets/PreRes/Settings/Display/FloatText_Block.asset` 拖到 `Block Settings` 槽，保存场景。
- **Unity 编辑器关着**：在 `DemoDay.unity` 的 `EntityDisplayManager` 组件块里、`_skillNameSettings` 那行之后插入：

```yaml
  _blockSettings: {fileID: 11400000, guid: 2036363bbffefee4a89e8b873a40caed, type: 2}
```

验证：

```bash
grep -n "_blockSettings" Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
```

预期：只输出一行，且包含 `2036363bbffefee4a89e8b873a40caed`。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs \
        Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs \
        Assets/Scripts/Tests/PlayMode/BlockFeedbackDisplayTests.cs \
        Assets/Scripts/Tests/PlayMode/BlockFeedbackDisplayTests.cs.meta \
        Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat(ui): 格挡成功显示金色「格挡」飘字并接场景配置"
```

---

## 任务 4：金色火花

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs`
- 测试：创建 `Assets/Scripts/Tests/PlayMode/BlockFeedbackSparkTests.cs`

- [ ] **步骤 1：写失败测试**

创建 `Assets/Scripts/Tests/PlayMode/BlockFeedbackSparkTests.cs`：

```csharp
using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>格挡火花：位置、缩放、金色与命中火花共用池。</summary>
    public class BlockFeedbackSparkTests
    {
        private GameObject _host;
        private GameObject _sparkPrefab;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            HitFeedbackSystem.EnableVFX = true;
            var existing = Object.FindObjectOfType<HitFeedbackSystem>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            _sparkPrefab = new GameObject("BlockSparkPrefab");
            _sparkPrefab.AddComponent<ParticleSystem>();
            _sparkPrefab.AddComponent<HitSpark>();

            _host = new GameObject("BlockSparkHost");
            _host.SetActive(false);
            var system = _host.AddComponent<HitFeedbackSystem>();
            typeof(HitFeedbackSystem)
                .GetField("_sparkPrefab", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(system, _sparkPrefab);
            _host.SetActive(true);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_sparkPrefab != null) Object.DestroyImmediate(_sparkPrefab);
            var left = Object.FindObjectOfType<HitFeedbackSystem>();
            if (left != null) Object.DestroyImmediate(left.gameObject);
        }

        [UnityTest]
        public IEnumerator test_block_feedback_plays_gold_spark_at_hit_position()
        {
            // Arrange
            var hitPos = new Vector3(1f, 2f, 3f);

            // Act
            EventBus.Emit(new BlockSucceededEvent(42, hitPos, Vector3.up * 1.5f));
            yield return null;

            // Assert
            var spark = _host.GetComponentInChildren<HitSpark>(true);
            Assert.IsNotNull(spark, "格挡成功应播放火花");
            Assert.AreEqual(hitPos, spark.transform.position, "火花必须落在格挡接触点");
            Assert.AreEqual(0.8f, spark.transform.localScale.x, 0.001f, "格挡火花缩放应为 0.8");

            var color = spark.GetComponent<ParticleSystem>().main.startColor.color;
            Assert.AreEqual(1f, color.r, 0.001f);
            Assert.AreEqual(0.84f, color.g, 0.001f);
            Assert.AreEqual(0f, color.b, 0.001f);
        }
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`bash tools/unity-tests.sh PlayMode BlockFeedbackSparkTests`

预期：`test_block_feedback_plays_gold_spark_at_hit_position` FAIL（`HitFeedbackSystem` 未订阅事件，找不到激活的火花）。

- [ ] **步骤 3：改 `HitFeedbackSystem`**

在序列化字段区加入：

```csharp
        [SerializeField] private Color _blockSparkColor = new Color(1f, 0.84f, 0f);
        [SerializeField] private float _blockSparkScale = 0.8f;
```

在 `OnEnable` 加入：

```csharp
            EventBus.Subscribe<BlockSucceededEvent>(OnBlockSucceeded);
```

在 `OnDisable` 加入：

```csharp
            EventBus.Unsubscribe<BlockSucceededEvent>(OnBlockSucceeded);
```

在 `Spawn` 方法之后加入：

```csharp
        private void OnBlockSucceeded(BlockSucceededEvent e)
        {
            if (!EnableVFX) return;
            if (_sparks == null || _sparks.Length == 0) return;

            _sparks[_cursor % _sparks.Length].Play(e.HitPosition, _blockSparkColor, _blockSparkScale);
            _cursor++;
        }
```

- [ ] **步骤 4：运行测试确认通过**

运行：`bash tools/unity-tests.sh PlayMode BlockFeedbackSparkTests`

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs \
        Assets/Scripts/Tests/PlayMode/BlockFeedbackSparkTests.cs \
        Assets/Scripts/Tests/PlayMode/BlockFeedbackSparkTests.cs.meta
git commit -m "feat(vfx): 格挡成功播放金色火花"
```

---

## 任务 5：全量回归与手动验收

**文件：** 无新增改动（除非验收发现问题）。

- [ ] **步骤 1：跑 EditMode 全量**

运行：`bash tools/unity-tests.sh EditMode`

预期：全部 PASS。

- [ ] **步骤 2：跑 PlayMode 相关类**

依次运行：

```bash
bash tools/unity-tests.sh PlayMode BlockStaminaTests
bash tools/unity-tests.sh PlayMode BlockFeedbackDisplayTests
bash tools/unity-tests.sh PlayMode BlockFeedbackSparkTests
```

预期：全部 PASS。

- [ ] **步骤 3：跑服务端回归**

运行：`bash tools/test-server.sh`

预期：全部 PASS（本功能不改服务端，此步只防意外）。

- [ ] **步骤 4：手动验收（DemoDay）**

按规格 §6.2 逐项确认：

1. 选人面板选 **SinglePlayer**，进入 DemoDay，举盾正面被 DogKnight 攻击：
   - 头顶金色「格挡」飘字
   - 盾面/接触点金色火花
   - 不出现伤害数字
   - `DefendHit` 动画仍播放
2. 背面受击或未举盾：灰色伤害数字 + 原有火花。
3. Online 模式 + 服务器：先起服 `dotnet run --project Server/Server3C.csproj -- 8000`，两个客户端（或一个客户端 + 一个好友/bot 压测场）都选 Online 模式进入 DemoDay；A 攻击举盾面朝 A 的 B → A 与 B 两端都看到「格挡」+ 火花；A 不再看到 B 的伤害数字。
4. 一次"挥击 + 剑气"两段格挡 → 只出现一次「格挡」，火花可多次。
5. 远程玩家：A 攻击举盾的远程玩家 B → A 看到 B 头上「格挡」+ 火花（与第 3 项同一次联机验收）。
6. 普通命中、暴击、怪物受击飘字颜色/数字不变。
7. 若 `_blockSettings` 未接：Console 出现一次 `_blockSettings 未接线` 警告。

- [ ] **步骤 5：检查 Console**

Unity Console 不得有新增错误/异常（尤其是 DOTween、TMP、EventBus 回调异常）。

- [ ] **步骤 6：最终提交（如有验收修正）**

```bash
git status --short
```

只提交与本次功能相关的文件；不要把仓库里已有的无关改动（NuGet DLL、字体资产等）一起提交。

---

## 自检清单

- [ ] 规格 §4.1 的事件结构与任务 1 一致
- [ ] 规格 §4.2 的三个发射点分别落在任务 1（远程）与任务 2（两条本地）
- [ ] 规格 §4.3 的飘字、去重、警告、场景接线落在任务 3
- [ ] 规格 §4.4 的火花落在任务 4
- [ ] 规格 §5 的 10 条边界没有被实现偷偷改变（尤其：不改协议、不改格挡判定、远程无动画、旁观者不可见、bot 当前不会举盾）
- [ ] 规格 §6.1 的 7 条自动化用例全部有对应实现（任务 1 三条 + 任务 2 四条）
- [ ] 规格 §6.2 的 7 条手动验收在任务 5 中逐项列出
- [ ] 所有新增测试都在 `try/finally` 中取消 EventBus 订阅（任务 1/2 明确写了；任务 3/4 通过 `Disable`/`TearDown` 清理）
- [ ] 没有把 `PlayerTakeDamageEvent` 的语义改掉；远程格挡只是不再发它
