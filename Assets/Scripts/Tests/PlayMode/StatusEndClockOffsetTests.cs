using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// Plan 07 Task 1：状态结束时刻只由权威 318 投影 diff 决定，与客户端服务器时钟偏移/渲染延迟无关。
    ///
    /// 模型：两个客户端各持有独立的 <see cref="RemoteEntityRegistry"/> + <see cref="AuthoritativeStatusVfx"/>，
    /// 复用 <c>ClientNetworkHost</c> 的订阅路径（`registry.StatusesChanged → vfx.Apply`）。
    /// 同一条 318 在两端于同一网络到达时刻投递；两端注入不同的服务器时钟偏移（+0ms / +400ms）
    /// 与渲染延迟（0 / 1 个 60 Hz 帧）。时钟偏移只用于断言“若按本地 EndTick 倒计时会怎样”——
    /// 生产路径不读它，只按 318 到达 + 渲染延迟施加/移除。
    /// </summary>
    public class StatusEndClockOffsetTests
    {
        private const long EntityId = 42;
        private const long BaselineServerTimestamp = 638_000_000_000_000_000L;
        private const uint BaselineServerGameTick = 200;
        private const uint StatusDurationTicks = 3;              // 15 Hz ⇒ 200 ms
        private const uint BaselineCombatVersion = 1;
        private const uint RemovalCombatVersion = 2;
        private const int EffectId = 101;                        // 1xx ⇒ EffectType.Freeze
        private const int StatusGroupId = 1;

        /// <summary>一个 60 Hz 渲染帧（16.67 ms）。</summary>
        private const double OneRenderFrameMs = 1000.0 / 60.0;

        private const int BaselineNetworkDelayMs = 100;
        private const int RemovalExtraDelayMs = 400;             // 移除 318 晚到 400 ms
        private const int ClientAServerClockOffsetMs = 0;
        private const int ClientBServerClockOffsetMs = 400;
        private const int ClientARenderDelayMs = 0;
        private const int ClientBRenderDelayMs = 16;             // ≤ 1 个 60 Hz 帧

        private static readonly int StatusDurationMs =
            (int)Math.Round(StatusDurationTicks * 1000.0 / CombatRules.TickRateHz);

        /// <summary>移除 318 的计划到达时刻：基线 100ms + 状态时长 200ms + 额外迟到 400ms。</summary>
        private static readonly long RemovalNetworkArrivalMs =
            BaselineNetworkDelayMs + StatusDurationMs + RemovalExtraDelayMs;

        private sealed class Client
        {
            public string Name;
            public int ServerClockOffsetMs;
            public int RenderDelayMs;
            public GameObject Root;
            public RemoteEntityRegistry Registry;
            public AuthoritativeStatusVfx Vfx;
            public readonly List<long> AppliedAtMs = new List<long>();
            public readonly List<long> RemovedAtMs = new List<long>();

            public bool EffectActive => AppliedAtMs.Count > RemovedAtMs.Count;
        }

        private readonly List<Client> _clients = new List<Client>();
        private readonly List<(long ApplyAtMs, Client Client, CombatStateUpdate Update)> _pending =
            new List<(long, Client, CombatStateUpdate)>();

        private GameObject _avatarPrefab;
        private Client _clientA;
        private Client _clientB;
        private long _nowMs;

        [SetUp]
        public void SetUp()
        {
            _clients.Clear();
            _pending.Clear();
            _nowMs = 0;

            _avatarPrefab = new GameObject("StatusClockAvatarPrefab");
            _avatarPrefab.AddComponent<Animator>();

            EventBus.Subscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Subscribe<StatusEffectRemovedEvent>(OnRemoved);

            _clientA = CreateClient("ClientA", ClientAServerClockOffsetMs, ClientARenderDelayMs);
            _clientB = CreateClient("ClientB", ClientBServerClockOffsetMs, ClientBRenderDelayMs);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);

            foreach (var client in _clients)
            {
                client.Registry.Clear();
                UnityEngine.Object.DestroyImmediate(client.Root);
            }
            _clients.Clear();
            UnityEngine.Object.DestroyImmediate(_avatarPrefab);
        }

        // ---------------------------------------------------------------- 用例

        /// <summary>1) 施加只在收到 318 后发生；本地 EndTick 已过（甚至到达时已过）不创建也不重复创建。</summary>
        [Test]
        public void test_status_end_clock_offset_applies_only_on_authoritative_318()
        {
            // Arrange：318 未到达
            AdvanceTo(BaselineNetworkDelayMs - 1);
            Assert.AreEqual(0, _clientA.AppliedAtMs.Count, "318 到达前不得施加（本地 EndTick 不是施加源）");
            Assert.AreEqual(0, _clientB.AppliedAtMs.Count, "318 到达前不得施加（本地 EndTick 不是施加源）");

            // Act：同一条基线 318 在 100ms 到达两端
            DeliverToBoth(BaselineUpdate, BaselineNetworkDelayMs);
            AdvanceTo(BaselineNetworkDelayMs);

            // Assert：A 当帧施加；B 晚一个渲染帧施加
            CollectionAssert.AreEqual(new long[] { 100 }, _clientA.AppliedAtMs,
                "A 应在 318 到达帧（100ms）施加一次");
            Assert.AreEqual(0, _clientB.AppliedAtMs.Count, "B 有 1 帧渲染延迟，此时尚未施加");

            AdvanceTo(BaselineNetworkDelayMs + ClientBRenderDelayMs);
            CollectionAssert.AreEqual(new long[] { 116 }, _clientB.AppliedAtMs,
                "B 应在 318 到达 + 1 帧（116ms）施加一次");

            // B 的时钟快 400ms：318 到达时它已认为 EndTick 过期，仍必须由 318 创建
            Assert.Greater(
                EstimatedServerTickAt(_clientB, BaselineNetworkDelayMs + ClientBRenderDelayMs),
                (double)(BaselineServerGameTick + StatusDurationTicks),
                "B 的本地时钟在 318 到达时已越过 EndTick（注入 +400ms）");

            // A 的本地 EndTick 在 300ms 越过：越过之后不得产生任何事件
            AdvanceTo(1000);
            Assert.Greater(EstimatedServerTickAt(_clientA, 301),
                (double)(BaselineServerGameTick + StatusDurationTicks),
                "A 的本地时钟在 301ms 时已越过 EndTick");
            CollectionAssert.AreEqual(new long[] { 100 }, _clientA.AppliedAtMs,
                "本地 EndTick 越过不得再次施加");
            CollectionAssert.AreEqual(new long[] { 116 }, _clientB.AppliedAtMs);
            Assert.AreEqual(0, _clientA.RemovedAtMs.Count, "没有 318 移除投影，特效必须保持");
            Assert.AreEqual(0, _clientB.RemovedAtMs.Count, "没有 318 移除投影，特效必须保持");
            Assert.IsTrue(_clientA.EffectActive);
            Assert.IsTrue(_clientB.EffectActive);
        }

        /// <summary>2) 移除 318 晚到 400ms：两端特效保持到它到达，且各自只在到达时移除一次。</summary>
        [Test]
        public void test_status_end_clock_offset_keeps_effect_until_late_removal_318_arrives()
        {
            // Arrange：基线 318 @100ms；移除 318 计划 @700ms（正常应 @300ms，晚 400ms）
            DeliverToBoth(BaselineUpdate, BaselineNetworkDelayMs);
            DeliverToBoth(RemovalUpdate, RemovalNetworkArrivalMs);

            // Act/Assert：A 的本地 EndTick @300ms——没有移除 318 就不得移除
            AdvanceTo(300);
            CollectionAssert.AreEqual(new long[] { 100 }, _clientA.AppliedAtMs);
            Assert.AreEqual(0, _clientA.RemovedAtMs.Count, "权威移除 318 未到，本地 EndTick 越过不得移除");
            Assert.IsTrue(_clientA.EffectActive);

            // B 的时钟快 400ms：在“本应到达”的 300ms 甚至 690ms 都必须保持
            AdvanceTo(690);
            Assert.IsTrue(_clientB.EffectActive, "移除 318 到达前特效必须保持（B 的时钟早已越过 EndTick）");
            Assert.AreEqual(0, _clientB.RemovedAtMs.Count);

            // Act：699ms 仍未到达
            AdvanceTo(RemovalNetworkArrivalMs - 1);
            Assert.AreEqual(0, _clientA.RemovedAtMs.Count, "700ms 前不得移除");
            Assert.AreEqual(0, _clientB.RemovedAtMs.Count, "700ms 前不得移除");

            // 700ms：A 当帧移除，B 一帧后移除
            AdvanceTo(RemovalNetworkArrivalMs);
            CollectionAssert.AreEqual(new long[] { 700 }, _clientA.RemovedAtMs,
                "A 应在移除 318 到达帧（700ms）移除一次");
            Assert.AreEqual(0, _clientB.RemovedAtMs.Count);

            AdvanceTo(RemovalNetworkArrivalMs + ClientBRenderDelayMs);
            CollectionAssert.AreEqual(new long[] { 716 }, _clientB.RemovedAtMs,
                "B 应在到达 + 1 帧（716ms）移除一次");

            // Assert：各一次施加/移除，最终不再有效果
            CollectionAssert.AreEqual(new long[] { 100 }, _clientA.AppliedAtMs);
            CollectionAssert.AreEqual(new long[] { 116 }, _clientB.AppliedAtMs);
            Assert.IsFalse(_clientA.EffectActive);
            Assert.IsFalse(_clientB.EffectActive);
        }

        /// <summary>3) 两端移除时刻差 ≤ 1 个渲染帧，而不是各自按 EndTick 的 400ms 时钟偏移。</summary>
        [Test]
        public void test_status_end_clock_offset_removal_spread_is_bounded_by_one_render_frame()
        {
            // Arrange
            DeliverToBoth(BaselineUpdate, BaselineNetworkDelayMs);
            DeliverToBoth(RemovalUpdate, RemovalNetworkArrivalMs);

            // Act
            AdvanceTo(2000);

            // Assert：实际移除时刻
            CollectionAssert.AreEqual(new long[] { 700 }, _clientA.RemovedAtMs);
            CollectionAssert.AreEqual(new long[] { 716 }, _clientB.RemovedAtMs);
            long spread = Math.Abs(_clientA.RemovedAtMs[0] - _clientB.RemovedAtMs[0]);
            Assert.LessOrEqual(spread, OneRenderFrameMs,
                "移除时刻差必须 ≤ 1 个渲染帧（两端渲染延迟差），不得被 400ms 时钟偏移拉开");

            // 反证：若按本地 EndTick 倒计时移除，两端会差出注入的 400ms 时钟偏移
            double clockSpread = Math.Abs(
                LocalEndTickRemovalAtMs(_clientA) - LocalEndTickRemovalAtMs(_clientB));
            Assert.AreEqual(400.0, clockSpread, 0.01,
                "按 EndTick 移除时两端会差 400ms（正是注入的时钟偏移）");
            Assert.Less(spread, clockSpread / 10.0,
                "实测移除差必须远小于时钟偏移");
        }

        /// <summary>4) 重复同版本 318 不重复施加/移除。</summary>
        [Test]
        public void test_status_end_clock_offset_repeated_same_version_318_does_not_reapply_or_remove()
        {
            // Arrange：同版本基线 318 各投递两次（同一到达时刻）
            DeliverToBoth(BaselineUpdate, BaselineNetworkDelayMs);
            DeliverToBoth(BaselineUpdate, BaselineNetworkDelayMs);

            // Act
            AdvanceTo(500);

            // Assert
            CollectionAssert.AreEqual(new long[] { 100 }, _clientA.AppliedAtMs,
                "同版本重复 318 不得重复施加（A）");
            CollectionAssert.AreEqual(new long[] { 116 }, _clientB.AppliedAtMs,
                "同版本重复 318 不得重复施加（B）");
            Assert.AreEqual(0, _clientA.RemovedAtMs.Count);
            Assert.AreEqual(0, _clientB.RemovedAtMs.Count);

            // Arrange：同版本移除 318 各投递两次
            DeliverToBoth(RemovalUpdate, RemovalNetworkArrivalMs);
            DeliverToBoth(RemovalUpdate, RemovalNetworkArrivalMs);

            // Act
            AdvanceTo(2000);

            // Assert
            CollectionAssert.AreEqual(new long[] { 700 }, _clientA.RemovedAtMs,
                "同版本重复移除 318 不得重复移除（A）");
            CollectionAssert.AreEqual(new long[] { 716 }, _clientB.RemovedAtMs,
                "同版本重复移除 318 不得重复移除（B）");
            CollectionAssert.AreEqual(new long[] { 100 }, _clientA.AppliedAtMs,
                "重复 318 不得重新施加（A）");
            CollectionAssert.AreEqual(new long[] { 116 }, _clientB.AppliedAtMs,
                "重复 318 不得重新施加（B）");
        }

        // ---------------------------------------------------------------- 模型

        /// <summary>
        /// 两个客户端各建一个视图。订阅路径与 <c>ClientNetworkHost</c> 相同：
        /// `registry.StatusesChanged → AuthoritativeStatusVfx.Apply`。
        /// </summary>
        private Client CreateClient(string name, int serverClockOffsetMs, int renderDelayMs)
        {
            var root = new GameObject(name + "_Root");
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = AppearanceResolver.DefaultId, Full = _avatarPrefab } });
            var vfx = new AuthoritativeStatusVfx();
            registry.StatusesChanged += (entityId, target, statuses) =>
            {
                if (target == null) return;
                vfx.Apply(entityId, target.transform, statuses);
            };

            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = EntityId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
            }, serverTicks: 0);

            var client = new Client
            {
                Name = name,
                ServerClockOffsetMs = serverClockOffsetMs,
                RenderDelayMs = renderDelayMs,
                Root = root,
                Registry = registry,
                Vfx = vfx,
            };
            _clients.Add(client);
            return client;
        }

        /// <summary>同一条 318 在两端各自的“网络到达 + 渲染延迟”时刻应用。</summary>
        private void DeliverToBoth(Func<CombatStateUpdate> updateFactory, long networkArrivalMs)
        {
            foreach (var client in _clients)
                _pending.Add((networkArrivalMs + client.RenderDelayMs, client, updateFactory()));
        }

        /// <summary>推进模拟时钟：按到达 + 渲染延迟顺序应用 318（事件在应用当刻记账）。</summary>
        private void AdvanceTo(long targetMs)
        {
            while (true)
            {
                int next = -1;
                for (int i = 0; i < _pending.Count; i++)
                {
                    if (_pending[i].ApplyAtMs > targetMs) continue;
                    if (next < 0 || _pending[i].ApplyAtMs < _pending[next].ApplyAtMs) next = i;
                }
                if (next < 0) break;

                var item = _pending[next];
                _pending.RemoveAt(next);
                _nowMs = item.ApplyAtMs;
                item.Client.Registry.ApplyCombatStateUpdate(item.Update);
            }
            _nowMs = targetMs;
        }

        private void OnApplied(StatusEffectAppliedEvent e)
            => ClientFor(e.Target)?.AppliedAtMs.Add(_nowMs);

        private void OnRemoved(StatusEffectRemovedEvent e)
            => ClientFor(e.Target)?.RemovedAtMs.Add(_nowMs);

        private Client ClientFor(Transform target)
        {
            if (target == null) return null;
            foreach (var client in _clients)
            {
                if (target == client.Root.transform || target.IsChildOf(client.Root.transform))
                    return client;
            }
            return null;
        }

        /// <summary>
        /// 客户端在本地时刻 localMs 对服务器游戏 tick 的估计（注入时钟偏移）。
        /// 只用于断言“若按本地 EndTick 倒计时会怎样”，生产路径不读它。
        /// </summary>
        private static double EstimatedServerTickAt(Client client, long localMs)
        {
            double elapsedMs = localMs - BaselineNetworkDelayMs + client.ServerClockOffsetMs;
            return BaselineServerGameTick + elapsedMs * CombatRules.TickRateHz / 1000.0;
        }

        /// <summary>若按本地 EndTick 倒计时移除，该客户端会在何时移除（毫秒）。</summary>
        private static double LocalEndTickRemovalAtMs(Client client)
            => BaselineNetworkDelayMs
               + StatusDurationTicks * 1000.0 / CombatRules.TickRateHz
               - client.ServerClockOffsetMs;

        /// <summary>基线 318：版本 1，冰状态（EndTick = 基线服务器 tick + 状态时长）。</summary>
        private static CombatStateUpdate BaselineUpdate()
        {
            return new CombatStateUpdate
            {
                EntityId = EntityId,
                ServerTimestamp = BaselineServerTimestamp,
                Projection = new CombatProjection
                {
                    CombatVersion = BaselineCombatVersion,
                    Hp = 100f,
                    MaxHp = 100f,
                    Statuses = new List<StatusProjection>
                    {
                        new StatusProjection
                        {
                            EffectId = EffectId,
                            GroupId = StatusGroupId,
                            EndTick = BaselineServerGameTick + StatusDurationTicks,
                            SourceId = 7,
                        },
                    },
                },
            };
        }

        /// <summary>移除 318：版本 2，状态列表为空（移除的唯一权威来源）。</summary>
        private static CombatStateUpdate RemovalUpdate()
        {
            return new CombatStateUpdate
            {
                EntityId = EntityId,
                ServerTimestamp = BaselineServerTimestamp + StatusDurationMs * TimeSpan.TicksPerMillisecond,
                Projection = new CombatProjection
                {
                    CombatVersion = RemovalCombatVersion,
                    Hp = 100f,
                    MaxHp = 100f,
                    Statuses = new List<StatusProjection>(),
                },
            };
        }
    }
}
