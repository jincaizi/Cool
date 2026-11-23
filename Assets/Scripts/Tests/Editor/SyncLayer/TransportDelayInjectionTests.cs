using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// Plan 07 任务 2：传输消费层的延迟/重复/乱序注入。
    /// 与 <c>NetworkConsumptionInjectionTests</c> 的区别：消息不再直接调
    /// <see cref="CombatNetworkBridge.OnMessage"/>，而是经测试本地
    /// <see cref="DelayedNetworkClient"/>（真 <see cref="INetworkClient"/> 装饰器）扣住、
    /// 由用例按确定顺序释放——延迟/重复/乱序都由测试驱动，不用 sleep 排程，也不碰真实 socket。
    /// 接线与 <see cref="ClientNetworkHost"/> 一致：<c>adapter.MessageReceived += bridge.OnMessage</c>。
    /// KCP/WebSocket 正常流程由 PlayMode <c>ElementalTwoClientTests</c> 覆盖，本文件只钉消费层防御。
    /// </summary>
    public class TransportDelayInjectionTests
    {
        private const string CatalogPath = "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset";
        private const long LocalPlayerId = 7L;

        /// <summary>
        /// 测试本地传输延迟装饰器：包住 fake <see cref="INetworkClient"/>，把出站 Send 与
        /// 入站 MessageReceived 都先扣在测试队列里，由用例按确定顺序释放。
        /// 延迟 = 扣住跨帧；重复 = 同一消息投递两次；乱序 = 按索引释放。
        /// 不触碰生产传输层。
        /// </summary>
        private sealed class DelayedNetworkClient : INetworkClient
        {
            private readonly INetworkClient _inner;
            private readonly List<(MessageId Id, IMessage Message, MessageFlags Flags)> _outbound = new();
            private readonly List<(MessageId Id, IMessage Message)> _heldInbound = new();

            public DelayedNetworkClient(INetworkClient inner)
            {
                _inner = inner;
                // 线路收到的包先进延迟队列；消费者只能看到测试释放的消息。
                _inner.MessageReceived += HoldInbound;
                _inner.Disconnected += reason => Disconnected?.Invoke(reason);
                _inner.Error += error => Error?.Invoke(error);
            }

            public bool IsConnected => _inner.IsConnected;

            public Task<bool> ConnectAsync(string host, int port) => _inner.ConnectAsync(host, port);

            public Task DisconnectAsync(string reason) => _inner.DisconnectAsync(reason);

            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable)
                => _outbound.Add((messageId, message, flags));

            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;

            /// <summary>当前被扣住的入站消息数（尚未交给消费者）。</summary>
            public int HeldInboundCount => _heldInbound.Count;

            /// <summary>当前被扣住的出站消息数（尚未交给内层 fake）。</summary>
            public int HeldOutboundCount => _outbound.Count;

            private void HoldInbound(MessageId messageId, IMessage message)
                => _heldInbound.Add((messageId, message));

            /// <summary>释放当前队列中第 <paramref name="index"/> 条入站消息
            /// （乱序注入 = 不按 0 起释放）。</summary>
            public void ReleaseInbound(int index)
            {
                var (id, message) = _heldInbound[index];
                _heldInbound.RemoveAt(index);
                MessageReceived?.Invoke(id, message);
            }

            public void ReleaseAllInbound()
            {
                while (_heldInbound.Count > 0) ReleaseInbound(0);
            }

            /// <summary>出站延迟结束：把扣住的消息交给内层 fake（等价于真正上线发送）。</summary>
            public void ReleaseOutbound()
            {
                foreach (var (id, message, flags) in _outbound)
                    _inner.Send(id, message, flags);
                _outbound.Clear();
            }

            public void Dispose()
            {
                _inner.MessageReceived -= HoldInbound;
                _inner.Dispose();
            }
        }

        /// <summary>内层 fake：只记录出站消息；服务器入站由用例经 <see cref="ReceiveFromServer"/> 模拟。</summary>
        private sealed class FakeNetworkClient : INetworkClient
        {
            /// <summary>装饰器释放后才可见的出站消息（延迟断言用）。</summary>
            public readonly List<(MessageId Id, IMessage Message, MessageFlags Flags)> Sent = new();

            public bool IsConnected => true;

            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);

            public Task DisconnectAsync(string reason) => Task.CompletedTask;

            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable) => Sent.Add((messageId, message, flags));

            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;

            /// <summary>模拟线路收到一条服务器消息（装饰器会把它扣进延迟队列）。</summary>
            public void ReceiveFromServer(MessageId messageId, IMessage message)
                => MessageReceived?.Invoke(messageId, message);

            public void Dispose() { }
        }

        private sealed class FakeVisual : IRemoteProjectileVisual
        {
            public bool IsActive { get; private set; } = true;
            public int Stops;
            public Vector3 LastPoint;

            public void ExplodeAt(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
            public void StopAtPoint(Vector3 point) { Stops++; LastPoint = point; IsActive = false; }
        }

        private GameObject _root = null!;
        private FakeNetworkClient _fake = null!;
        private DelayedNetworkClient _client = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TransportDelayInjectionTests");
            _fake = new FakeNetworkClient();
            _client = new DelayedNetworkClient(_fake);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        /// <summary>与 <see cref="ClientNetworkHost"/> 同款接线：adapter.MessageReceived → bridge.OnMessage。</summary>
        private void Attach(CombatNetworkBridge bridge) => _client.MessageReceived += bridge.OnMessage;

        // ------------------------------------------------------------------------------------
        // ① 换装确认延迟于施法确认：版本门不回退，出站请求仍以已确认版本为基准
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_delayed_equip_confirm_after_cast_confirm_keeps_revision_and_equipment_gate()
        {
            // Arrange
            var registry = new RemoteEntityRegistry(_root.transform);
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            Attach(bridge);
            int castConfirms = 0, equipResults = 0, equipmentChanged = 0;
            bridge.OnCastConfirm += confirm =>
            {
                castConfirms++;
                Assert.AreEqual(5u, confirm.EquipmentRevision,
                    "施法确认携带的服务端冻结装备版本必须原样转发");
            };
            bridge.OnEquipResult += _ => equipResults++;
            bridge.OnEquipmentChanged += _ => equipmentChanged++;

            // Act：施法确认先到，版本 5 的换装确认在传输层被扣住（迟一帧）
            _fake.ReceiveFromServer(MessageId.CastConfirm, new CastConfirm
            {
                ClientCastSeq = 1, Accepted = true, CastId = 501, SkillId = 10002,
                CasterLifeId = 1, EquipmentRevision = 5
            });
            _fake.ReceiveFromServer(MessageId.EquipWeaponConfirm, new EquipWeaponConfirm
            {
                ClientEquipSeq = 1, Accepted = true, EquipmentRevision = 5,
                WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
            });
            Assert.AreEqual(2, _client.HeldInboundCount, "两条消息都应被装饰器扣住");
            bridge.Update();
            Assert.AreEqual(0, castConfirms, "释放前不得消费任何入站消息");

            _client.ReleaseInbound(0);
            bridge.Update();
            Assert.AreEqual(1, castConfirms, "施法确认只消费一次");
            Assert.AreEqual(0u, bridge.Equipment.Revision, "换装确认仍被扣住，不得提前生效");
            Assert.IsTrue(bridge.TryGetAcceptedCastId(10002, lifeId: 1, out ulong castId));
            Assert.AreEqual(501UL, castId, "施法确认必须按序号被记住");

            _client.ReleaseInbound(0);
            bridge.Update();
            Assert.AreEqual(5u, bridge.Equipment.Revision);
            Assert.AreEqual(42L, bridge.Equipment.WeaponInstanceId);
            Assert.AreEqual(1, equipResults);
            Assert.AreEqual(1, equipmentChanged);

            // Act：旧版本 4 的换装确认在版本 5 之后到达——乱序不得回退
            _fake.ReceiveFromServer(MessageId.EquipWeaponConfirm, new EquipWeaponConfirm
            {
                ClientEquipSeq = 2, Accepted = true, EquipmentRevision = 4,
                WeaponInstanceId = 7, TemplateId = 90001, ElementId = 1
            });
            _client.ReleaseAllInbound();
            bridge.Update();

            Assert.AreEqual(5u, bridge.Equipment.Revision, "旧换装确认不得回退装备版本门");
            Assert.AreEqual(42L, bridge.Equipment.WeaponInstanceId);
            Assert.AreEqual(2, equipResults, "每条确认仍须通知宿主收尾（含旧版本）");
            Assert.AreEqual(1, equipmentChanged, "旧版本确认不得再触发状态更新");

            // Act：下一次出站请求先在装饰器里延迟，释放后才真正发出
            bridge.RequestEquip(43);
            bridge.NotifySkillCast(10002, new HitShape { ShapeType = 0, Range = 2f, Angle = 120f });
            Assert.AreEqual(2, _client.HeldOutboundCount, "装饰器应扣住出站请求");
            Assert.AreEqual(0, _fake.Sent.Count, "释放前内层 fake 不得看到任何出站消息");

            _client.ReleaseOutbound();

            Assert.AreEqual(2, _fake.Sent.Count);
            var equipRequest = (EquipWeaponRequest)_fake.Sent[0].Message;
            Assert.AreEqual(5u, equipRequest.KnownEquipmentRevision,
                "下一次换装请求必须以上一次已确认版本为基准");
            Assert.AreEqual(43L, equipRequest.WeaponInstanceId);
            var castRequest = (CastRequest)_fake.Sent[1].Message;
            Assert.AreEqual(5u, castRequest.EquipmentRevision,
                "下一次施法请求必须携带已确认的装备版本（旧确认不得污染版本门）");
        }

        // ------------------------------------------------------------------------------------
        // ② 发射晚于格挡结果：晚到的发射不重建已停止视觉
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_delayed_launch_after_stop_does_not_rebuild_visual()
        {
            // Arrange：presenter 假视觉接缝（同 RemoteCastPresenterCastIdTests）
            int factoryCalls = 0;
            var visual = new FakeVisual();
            var presenter = new RemoteCastPresenter(null, localPlayerId: 1L, LoadCatalog(),
                launchProjectileFactory: (_, __, ___, ____) =>
                {
                    factoryCalls++;
                    return visual;
                },
                sceneIdProvider: () => "room-a");
            var bridge = new CombatNetworkBridge(_client, () => 1L,
                new RemoteEntityRegistry(_root.transform));
            Attach(bridge);
            int launchEvents = 0, stopEvents = 0;
            bridge.OnProjectileLaunch += launch => { launchEvents++; presenter.OnProjectileLaunch(launch); };
            bridge.OnProjectileStop += stop => { stopEvents++; presenter.OnProjectileStop(stop); };

            // Act：格挡结果先消费；该发剑气的发射在传输层被扣住后迟到
            _fake.ReceiveFromServer(MessageId.ProjectileStopBroadcast, Stop(710));
            Assert.AreEqual(1, _client.HeldInboundCount);
            _client.ReleaseAllInbound();
            bridge.Update();
            Assert.AreEqual(1, stopEvents);
            Assert.AreEqual(0, factoryCalls);

            _fake.ReceiveFromServer(MessageId.ProjectileLaunchBroadcast, Launch(710));
            _client.ReleaseAllInbound();
            bridge.Update();
            Assert.AreEqual(1, launchEvents, "桥层原样转发（终止记忆在 presenter）");
            Assert.AreEqual(0, factoryCalls, "已停止 CastId 的迟到发射不得重建视觉");
            Assert.AreEqual(0, presenter.CastVisualCountForTest);
            Assert.AreEqual(0, visual.Stops, "未创建视觉时不得有停点调用");

            // Act：正常顺序（发射先于终止）仍照常建视觉并按停点结束
            _fake.ReceiveFromServer(MessageId.ProjectileLaunchBroadcast, Launch(711));
            _client.ReleaseAllInbound();
            bridge.Update();
            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(1, presenter.CastVisualCountForTest);

            _fake.ReceiveFromServer(MessageId.ProjectileStopBroadcast, Stop(711));
            _client.ReleaseAllInbound();
            bridge.Update();
            Assert.AreEqual(1, visual.Stops);
            Assert.AreEqual(new Vector3(0f, 0f, 5f), visual.LastPoint);
            Assert.AreEqual(0, presenter.CastVisualCountForTest);

            // Act：已停止 CastId 的延迟重复发射
            _fake.ReceiveFromServer(MessageId.ProjectileLaunchBroadcast, Launch(711));
            _client.ReleaseAllInbound();
            bridge.Update();

            Assert.AreEqual(3, launchEvents, "桥层转发三条发射事件（门在 presenter）");
            Assert.AreEqual(2, stopEvents);
            Assert.AreEqual(1, factoryCalls, "已停止 CastId 的延迟重复发射不得重建视觉");
            Assert.AreEqual(1, visual.Stops, "重复发射不得多停一次");
            Assert.AreEqual(0, presenter.CastVisualCountForTest);
        }

        // ------------------------------------------------------------------------------------
        // ③ 旧伤害晚于复活：旧 LifeId 的 DamageResult 不扣血/不飘字
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_delayed_old_life_damage_after_respawn_is_not_consumed()
        {
            const long npcId = 2_000_201L;

            // Arrange：远程怪物视图 + 本地受击注入点
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(NpcSnapshot(npcId, lifeId: 1), serverTicks: 1_000_000L);
            var view = registry.GetView(npcId);
            Assert.NotNull(view);

            int localCalls = 0, damageEvents = 0, floatTextEvents = 0;
            float localRemaining = -1f;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    localCalls++;
                    localRemaining = remainingHp;
                });
            Attach(bridge);
            bridge.OnDamageResult += _ => damageEvents++;
            Action<MonsterTakeDamageEvent> floatText = _ => floatTextEvents++;
            EventBus.Subscribe(floatText);
            try
            {
                // 宿主消费 RespawnNotice 后写入新代次；远程视图由新基线更新。
                bridge.Equipment.SetLifeId(2);
                registry.ApplySnapshot(NpcSnapshot(npcId, lifeId: 2), serverTicks: 2_000_000L);
                Assert.AreEqual(2u, view!.LifeId);

                // Act：换命前的旧伤害在传输层延迟到复活之后才释放
                _fake.ReceiveFromServer(MessageId.DamageResult,
                    DirectHit(eventId: 7301, targetLifeId: 1));
                _fake.ReceiveFromServer(MessageId.DamageResult,
                    DirectHit(eventId: 7302, targetId: npcId, damage: 40f, remainingHp: 1f,
                        targetLifeId: 1));
                _client.ReleaseAllInbound();
                bridge.Update();

                Assert.AreEqual(0, localCalls, "旧 LifeId 命中不得走本地受击注入（不扣血）");
                Assert.AreEqual(0, damageEvents, "旧世代命中不得触发 OnDamageResult");
                Assert.AreEqual(0, floatTextEvents, "旧世代命中不得飘伤害数字");
                Assert.AreEqual(1f, view.Targetable.HPPercent, 0.001f,
                    "旧世代命中不得回退新生命血条");

                // Act：新世代复用旧包用过的 EventId（旧包不得占用去重窗口）
                _fake.ReceiveFromServer(MessageId.DamageResult,
                    DirectHit(eventId: 7301, damage: 30f, remainingHp: 70f, targetLifeId: 2));
                _fake.ReceiveFromServer(MessageId.DamageResult,
                    DirectHit(eventId: 7302, targetId: npcId, damage: 40f, remainingHp: 60f,
                        targetLifeId: 2));
                _client.ReleaseAllInbound();
                bridge.Update();

                Assert.AreEqual(1, localCalls, "当前世代命中必须生效");
                Assert.AreEqual(70f, localRemaining, 0.001f, "本地受击注入必须拿到权威余血");
                Assert.AreEqual(2, damageEvents);
                Assert.AreEqual(1, floatTextEvents, "远程怪物新世代命中只飘一次");
                Assert.AreEqual(0.6f, view.Targetable.HPPercent, 0.001f);
            }
            finally
            {
                EventBus.Unsubscribe(floatText);
            }
        }

        // ------------------------------------------------------------------------------------
        // ④ 较新血量早于状态消息：旧 CombatStateUpdate 不回退新血量/状态
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_delayed_older_projection_after_newer_does_not_roll_back_hp_or_status()
        {
            // Arrange：基线版本 7（血量/格挡/装备/状态齐全）
            var registry = new RemoteEntityRegistry(_root.transform);
            int statusEvents = 0;
            registry.StatusesChanged += (_, __, ___) => statusEvents++;

            registry.ApplySnapshot(ProjectionSnapshot(2L, version: 7, hp: 300f,
                weaponInstanceId: 42, defending: true, lifeId: 1, statusEffectId: 4),
                serverTicks: 1_000_000L);
            var view = registry.GetView(2L);
            Assert.NotNull(view);
            Assert.AreEqual(1, statusEvents, "基线状态挂载一次");

            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry);
            Attach(bridge);
            int updates = 0;
            bridge.OnCombatStateUpdate += update =>
            {
                updates++;
                registry.ApplyCombatStateUpdate(update);
            };

            // Act：较新的血量/状态先消费，旧版本投影在传输层延迟后到
            _fake.ReceiveFromServer(MessageId.CombatStateUpdate,
                CombatUpdate(2L, version: 8, hp: 250f, weaponInstanceId: 99,
                    statuses: new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 5, GroupId = 1 }
                    }));
            _client.ReleaseAllInbound();
            bridge.Update();

            Assert.AreEqual(1, updates);
            Assert.AreEqual(8u, view!.CombatVersion);
            Assert.AreEqual(250f, view.Hp, 0.001f);
            Assert.AreEqual(2, statusEvents, "较新状态变化挂载一次");
            Assert.AreEqual(1, view.Statuses.Count);
            Assert.AreEqual(5, view.Statuses[0].EffectId);

            _fake.ReceiveFromServer(MessageId.CombatStateUpdate,
                CombatUpdate(2L, version: 7, hp: 1f, weaponInstanceId: 7, defending: true,
                    dead: true, statuses: new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 4, GroupId = 1 }
                    }));
            _client.ReleaseAllInbound();
            bridge.Update();

            // Assert：桥层原样转发两条，版本门在 registry
            Assert.AreEqual(2, updates, "桥层必须转发两条投影（消费门在视图版本门）");
            Assert.AreEqual(8u, view.CombatVersion, "旧版本必须在版本门前被丢弃");
            Assert.AreEqual(250f, view.Hp, 0.001f, "已应用的血量不得被旧版本回退");
            Assert.IsFalse(view.Dead, "死亡标志不得被旧版本回退");
            Assert.IsFalse(view.IsDefending, "格挡状态不得被旧版本回退");
            Assert.NotNull(view.Equipment);
            Assert.AreEqual(99L, view.Equipment!.WeaponInstanceId, "装备不得被旧版本回退");
            Assert.AreEqual(1, view.Statuses.Count, "状态数量不得被旧版本替换");
            Assert.AreEqual(5, view.Statuses[0].EffectId, "状态内容不得被旧版本回退");
            Assert.AreEqual(2, statusEvents, "旧版本不得触发状态变化事件");
        }

        // ------------------------------------------------------------------------------------
        // ⑤ 延迟但同版本消息只消费一次
        // ------------------------------------------------------------------------------------

        [Test]
        public void test_delayed_duplicate_same_version_messages_are_consumed_once()
        {
            const long npcId = 2_000_301L;

            // Arrange
            var registry = new RemoteEntityRegistry(_root.transform);
            registry.ApplySnapshot(NpcSnapshot(npcId, lifeId: 1), serverTicks: 1_000_000L);
            var view = registry.GetView(npcId);
            Assert.NotNull(view);

            int localCalls = 0, damageEvents = 0, updates = 0;
            float lastRemaining = -1f;
            var bridge = new CombatNetworkBridge(_client, () => LocalPlayerId, registry,
                (targetId, damage, isCritical, dir, remainingHp, isBlocked, sourceIsPlayer) =>
                {
                    localCalls++;
                    lastRemaining = remainingHp;
                });
            Attach(bridge);
            bridge.OnDamageResult += _ => damageEvents++;
            bridge.OnCombatStateUpdate += update =>
            {
                updates++;
                registry.ApplyCombatStateUpdate(update);
            };

            // Act：首次命中；同一 EventId 的独立解码副本在下一帧才释放（延迟重复）
            _fake.ReceiveFromServer(MessageId.DamageResult,
                DirectHit(eventId: 7401, damage: 10f, remainingHp: 90f, targetLifeId: 1));
            _client.ReleaseAllInbound();
            bridge.Update();
            Assert.AreEqual(1, localCalls);
            Assert.AreEqual(90f, lastRemaining, 0.001f);

            _fake.ReceiveFromServer(MessageId.DamageResult,
                DirectHit(eventId: 7401, damage: 10f, remainingHp: 90f, targetLifeId: 1));
            _client.ReleaseAllInbound();
            bridge.Update();

            Assert.AreEqual(1, localCalls, "同一 EventId 的延迟重投只消费一次");
            Assert.AreEqual(1, damageEvents, "桥层事件不得因延迟重投再次触发");
            Assert.AreEqual(90f, lastRemaining, 0.001f, "重投不得改写权威余血");

            // Act：同版本 CombatStateUpdate 的两份独立副本跨帧释放
            int statusEvents = 0;
            registry.StatusesChanged += (_, __, ___) => statusEvents++;
            _fake.ReceiveFromServer(MessageId.CombatStateUpdate,
                CombatUpdate(npcId, version: 2, hp: 80f, weaponInstanceId: 42,
                    statuses: new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 6, GroupId = 1 }
                    }));
            _client.ReleaseAllInbound();
            bridge.Update();
            Assert.AreEqual(2u, view!.CombatVersion);
            Assert.AreEqual(80f, view.Hp, 0.001f);
            Assert.AreEqual(1, statusEvents, "同版本投影首次应用一次状态");

            _fake.ReceiveFromServer(MessageId.CombatStateUpdate,
                CombatUpdate(npcId, version: 2, hp: 80f, weaponInstanceId: 42,
                    statuses: new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 6, GroupId = 1 }
                    }));
            _client.ReleaseAllInbound();
            bridge.Update();

            Assert.AreEqual(2, updates, "桥层转发两份投影（幂等门在视图合并）");
            Assert.AreEqual(1, statusEvents, "同版本延迟重投不得再次触发状态变化");
            Assert.AreEqual(2u, view.CombatVersion);
            Assert.AreEqual(80f, view.Hp, 0.001f);
            Assert.AreEqual(1, view.Statuses.Count);
            Assert.AreEqual(6, view.Statuses[0].EffectId);
        }

        // ------------------------------------------------------------------------------------
        // 夹具
        // ------------------------------------------------------------------------------------

        private static SkillPresentationCatalog LoadCatalog()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(CatalogPath);
            Assert.NotNull(catalog, "SkillPresentationCatalog.asset 未找到");
            return catalog;
        }

        private static DamageResult DirectHit(ulong eventId, long targetId = LocalPlayerId,
            float damage = 10f, float remainingHp = 90f, uint targetLifeId = 0)
            => new DamageResult
            {
                SourcePlayerId = 1L,
                SkillId = 20001,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = targetId,
                        Damage = damage,
                        TargetRemainingHp = remainingHp,
                        EventId = eventId,
                        TargetLifeId = targetLifeId
                    }
                }
            };

        /// <summary>远程怪物基线（无模板预制体 = 胶囊占位，测试不需要美术）。</summary>
        private static EntitySnapshot NpcSnapshot(long npcId, uint lifeId,
            float hp = 100f, float maxHp = 100f)
            => new EntitySnapshot
            {
                PlayerId = npcId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.TemplateId | EntityFieldMask.Health | EntityFieldMask.Dead),
                X = 0f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Npc, TemplateId = 1,
                Hp = hp, MaxHp = maxHp, LifeId = lifeId
            };

        /// <summary>带完整战斗投影的玩家基线（版本门/状态门的起点）。</summary>
        private static EntitySnapshot ProjectionSnapshot(long playerId, uint version, float hp,
            long weaponInstanceId, bool defending, uint lifeId, int statusEffectId)
            => new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.Dead | EntityFieldMask.Health | EntityFieldMask.Defending
                    | EntityFieldMask.Combat),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
                Dead = false, Hp = hp, MaxHp = 720f, IsDefending = defending,
                CombatVersion = version, LifeId = lifeId,
                Combat = new CombatProjection
                {
                    CombatVersion = version, LifeId = lifeId, Hp = hp, MaxHp = 720f,
                    Dead = false, IsDefending = defending,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = weaponInstanceId, TemplateId = 90002, ElementId = 1
                    },
                    Statuses = new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = statusEffectId, GroupId = 1 }
                    }
                }
            };

        private static CombatStateUpdate CombatUpdate(long entityId, uint version, float hp,
            long weaponInstanceId, bool defending = false, bool dead = false,
            uint lifeId = 1, IList<StatusProjection>? statuses = null)
            => new CombatStateUpdate
            {
                EntityId = entityId,
                Projection = new CombatProjection
                {
                    CombatVersion = version,
                    LifeId = lifeId,
                    Hp = hp,
                    MaxHp = 720f,
                    Dead = dead,
                    IsDefending = defending,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = weaponInstanceId, TemplateId = 90002, ElementId = 1
                    },
                    // 默认空列表；StatusesEqual 把 null 与空列表同样按 0 项处理。
                    Statuses = statuses ?? new List<StatusProjection>()
                }
            };

        private static ProjectileLaunchBroadcast Launch(ulong castId, long casterId = 2L,
            string scene = "room-a", long timestamp = 1_000_000L)
            => new ProjectileLaunchBroadcast
            {
                CastId = castId,
                CasterPlayerId = casterId,
                CasterLifeId = 1,
                CombatSceneId = scene,
                LaunchTick = 6,
                OriginX = 0f,
                OriginY = 0f,
                OriginZ = 0f,
                Yaw = 0f,
                Speed = 18f,
                Range = 12f,
                ElementId = 1,
                SkillId = 10002,
                ServerTimestamp = timestamp
            };

        private static ProjectileStopBroadcast Stop(ulong castId, string scene = "room-a")
            => new ProjectileStopBroadcast
            {
                CastId = castId,
                CasterPlayerId = 2L,
                CasterLifeId = 1,
                CombatSceneId = scene,
                StopTick = 10,
                Reason = ProjectileStopReason.Blocked,
                StopX = 0f,
                StopZ = 5f
            };
    }
}
