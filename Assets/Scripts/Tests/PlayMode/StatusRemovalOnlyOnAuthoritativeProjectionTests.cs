using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// Plan 07 Task 1（范围裁定 2026-11-24）：自然状态结束只由权威 318 的状态列表 diff 驱动，
    /// 消费端推进时间不得按本地时长/EndTick 自行移除。
    ///
    /// 被测链路：`RemoteEntityRegistry`（318 合并/版本门）→ `AuthoritativeStatusVfx`（按组 diff）
    /// → `EventBus` → 真实 `StatusVFXSystem`（染色/特效）。两端各持独立 registry + vfx，
    /// 共用同一台 `StatusVFXSystem`（真实宿主每客户端一台；这里两台客户端观察的实体 id 不同，
    /// 以便同一台消费端按键隔离）。
    ///
    /// 非 318 的清除只允许全量：`Remove(entityId)`（AOI 出界/销毁）与渲染根切换重放。
    /// 不注入时钟偏移——生产没有本地计时移除路径，且不新增测试专用生产 API。
    /// </summary>
    public class StatusRemovalOnlyOnAuthoritativeProjectionTests
    {
        private const long ClientAEntityId = 1001;
        private const long ClientBEntityId = 2002;
        private const uint BaselineCombatVersion = 1;
        private const uint RemovalCombatVersion = 2;
        private const int FreezeEffectId = 101;      // 1xx ⇒ EffectType.Freeze
        private const int BurnEffectId = 201;        // 2xx ⇒ EffectType.Burn
        private const int PrimaryGroupId = 1;
        private const int SecondaryGroupId = 2;
        private const uint ServerGameTick = 200;     // 协议字段值；本测试不据此计时
        private const uint StatusDurationTicks = 3;  // 15 Hz ⇒ 200 ms 名义时长

        /// <summary>固定每帧 deltaTime：推进 0.4s 只需 8 帧，且 Update 收到的增量稳定。</summary>
        private const float CaptureDeltaSeconds = 0.05f;
        /// <summary>明显超过 200 ms 名义状态时长。</summary>
        private const float BeyondNominalDurationSeconds = 0.4f;

        private sealed class Client
        {
            public long EntityId;
            public GameObject Root;
            public RemoteEntityRegistry Registry;
            public MeshRenderer Renderer;
            public readonly List<int> AppliedFrames = new List<int>();
            public readonly List<int> RemovedFrames = new List<int>();
        }

        private static readonly FieldInfo TintsField = typeof(StatusVFXSystem).GetField(
            "_tints", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Client> _clients = new List<Client>();
        private Client _clientA;
        private Client _clientB;
        private GameObject _avatarPrefab;
        private GameObject _systemGo;
        private Material _material;
        private StatusVFXSystem _system;

        private IDictionary Tints => (IDictionary)TintsField.GetValue(_system);

        [SetUp]
        public void SetUp()
        {
            _clients.Clear();
            Time.captureDeltaTime = CaptureDeltaSeconds;

            _avatarPrefab = new GameObject("StatusProjectionAvatar");
            _avatarPrefab.AddComponent<Animator>();
            var renderer = _avatarPrefab.AddComponent<MeshRenderer>();
            _material = new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = _material;

            Assert.IsNotNull(TintsField, "StatusVFXSystem._tints 反射字段缺失");
            _systemGo = new GameObject("StatusVFX");
            _system = _systemGo.AddComponent<StatusVFXSystem>();

            EventBus.Subscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Subscribe<StatusEffectRemovedEvent>(OnRemoved);

            _clientA = CreateClient("ClientA", ClientAEntityId);
            _clientB = CreateClient("ClientB", ClientBEntityId);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            EventBus.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);

            if (_systemGo != null) UnityEngine.Object.Destroy(_systemGo);
            foreach (var client in _clients)
            {
                client.Registry.Clear();
                UnityEngine.Object.Destroy(client.Root);
            }
            _clients.Clear();
            if (_avatarPrefab != null) UnityEngine.Object.Destroy(_avatarPrefab);
            if (_material != null) UnityEngine.Object.Destroy(_material);

            // ElementVfxAttachment 的实例销毁与租约释放延迟到下一帧。
            yield return null;
            yield return null;
            Time.captureDeltaTime = 0f;
        }

        // ---------------------------------------------------------------- 用例

        /// <summary>1) 施加只在 318 到达后发生；没有 318 时推进时间不创建（本地时长不是施加源）。</summary>
        [UnityTest]
        public IEnumerator test_status_applies_only_after_authoritative_projection_arrives()
        {
            // Arrange/Act：没有 318，推进超过名义状态时长
            yield return new WaitForSeconds(BeyondNominalDurationSeconds);

            // Assert：两端都没有施加/染色
            Assert.AreEqual(0, _clientA.AppliedFrames.Count, "没有 318 不得施加（A）");
            Assert.AreEqual(0, _clientB.AppliedFrames.Count, "没有 318 不得施加（B）");
            AssertTintAbsent(_clientA);
            AssertTintAbsent(_clientB);

            // Act：同一条投影在同一帧到达两端
            DeliverToBoth(BaselineUpdate);

            // Assert：各施加一次，同一帧到达 ⇒ 同一帧施加，染色条目在场
            Assert.AreEqual(1, _clientA.AppliedFrames.Count, "318 到达时施加一次（A）");
            Assert.AreEqual(1, _clientB.AppliedFrames.Count, "318 到达时施加一次（B）");
            Assert.AreEqual(_clientA.AppliedFrames[0], _clientB.AppliedFrames[0],
                "同一帧到达应在同一帧施加");
            AssertTintPresent(_clientA);
            AssertTintPresent(_clientB);

            // Act：继续推进时间（> 名义时长），没有移除 318
            yield return new WaitForSeconds(BeyondNominalDurationSeconds);

            // Assert：Update 已运行多帧，仍不得自行移除
            Assert.AreEqual(0, _clientA.RemovedFrames.Count, "推进时间不得移除（A）");
            Assert.AreEqual(0, _clientB.RemovedFrames.Count, "推进时间不得移除（B）");
            AssertTintPresent(_clientA);
            AssertTintPresent(_clientB);
        }

        /// <summary>2) 移除 318 晚到：特效/染色保持到到达，到达时各移除一次并清染色。</summary>
        [UnityTest]
        public IEnumerator test_status_remains_until_late_removal_318_arrives()
        {
            // Arrange：基线 318 施加
            DeliverToBoth(BaselineUpdate);
            Assert.AreEqual(1, _clientA.AppliedFrames.Count);
            Assert.AreEqual(1, _clientB.AppliedFrames.Count);
            AssertTintPresent(_clientA);
            AssertTintPresent(_clientB);

            // Act：推进 0.4s（名义时长 0.2s 的两倍），移除 318 仍未到
            yield return new WaitForSeconds(BeyondNominalDurationSeconds);

            // Assert：Update 多帧后仍保持
            Assert.AreEqual(0, _clientA.RemovedFrames.Count, "迟到移除 318 未到，不得移除（A）");
            Assert.AreEqual(0, _clientB.RemovedFrames.Count, "迟到移除 318 未到，不得移除（B）");
            AssertTintPresent(_clientA);
            AssertTintPresent(_clientB);

            // Act：移除 318 先到 A（A 到达帧）
            Deliver(_clientA, RemovalUpdate(ClientAEntityId));

            // Assert：A 恰好移除一次并清染色；B 的 318 未到，保持
            Assert.AreEqual(1, _clientA.RemovedFrames.Count, "318 到达时移除一次（A）");
            Assert.AreEqual(0, _clientB.RemovedFrames.Count, "B 的 318 未到不得移除");
            AssertTintAbsent(_clientA);
            AssertTintPresent(_clientB);
            AssertRendererCleared(_clientA);

            // Act：下一帧移除 318 到 B
            yield return null;
            Deliver(_clientB, RemovalUpdate(ClientBEntityId));

            // Assert：B 恰好移除一次；两端移除帧差 = 318 到达帧差（1 帧）
            Assert.AreEqual(1, _clientB.RemovedFrames.Count, "318 到达时移除一次（B）");
            Assert.AreEqual(1, _clientB.RemovedFrames[0] - _clientA.RemovedFrames[0],
                "移除时刻差只能来自 318 到达差（1 帧）");
            AssertTintAbsent(_clientA);
            AssertTintAbsent(_clientB);
            AssertRendererCleared(_clientB);

            // Act：继续推进时间
            yield return new WaitForSeconds(BeyondNominalDurationSeconds);

            // Assert：不再产生额外移除
            Assert.AreEqual(1, _clientA.RemovedFrames.Count);
            Assert.AreEqual(1, _clientB.RemovedFrames.Count);
        }

        /// <summary>3) 同一帧到达同一投影 ⇒ 同一帧移除；不存在按本地时长的分叉。</summary>
        [UnityTest]
        public IEnumerator test_status_removal_timing_only_tracks_318_arrival()
        {
            // Arrange
            DeliverToBoth(BaselineUpdate);
            yield return new WaitForSeconds(BeyondNominalDurationSeconds);
            Assert.AreEqual(0, _clientA.RemovedFrames.Count);
            Assert.AreEqual(0, _clientB.RemovedFrames.Count);

            // Act：同一帧到达
            DeliverToBoth(RemovalUpdate);

            // Assert
            Assert.AreEqual(1, _clientA.RemovedFrames.Count);
            Assert.AreEqual(1, _clientB.RemovedFrames.Count);
            Assert.AreEqual(_clientA.RemovedFrames[0], _clientB.RemovedFrames[0],
                "同一帧到达 ⇒ 同一帧移除；不存在两端按本地时长各自消失的分叉");
            AssertTintAbsent(_clientA);
            AssertTintAbsent(_clientB);
        }

        /// <summary>4) 重复同版本 318 不重复施加/移除。</summary>
        [UnityTest]
        public IEnumerator test_status_repeated_same_version_318_is_idempotent()
        {
            // Arrange/Act：同版本基线 318 各投递两次（同帧）
            DeliverToBoth(BaselineUpdate);
            DeliverToBoth(BaselineUpdate);
            yield return null;

            // Assert
            Assert.AreEqual(1, _clientA.AppliedFrames.Count, "同版本重复 318 不得重复施加（A）");
            Assert.AreEqual(1, _clientB.AppliedFrames.Count, "同版本重复 318 不得重复施加（B）");
            Assert.AreEqual(0, _clientA.RemovedFrames.Count);
            Assert.AreEqual(0, _clientB.RemovedFrames.Count);
            AssertTintPresent(_clientA);
            AssertTintPresent(_clientB);

            // Act：同版本移除 318 各投递两次（同帧）
            DeliverToBoth(RemovalUpdate);
            DeliverToBoth(RemovalUpdate);
            yield return null;

            // Assert
            Assert.AreEqual(1, _clientA.RemovedFrames.Count, "同版本重复移除 318 不得重复移除（A）");
            Assert.AreEqual(1, _clientB.RemovedFrames.Count, "同版本重复移除 318 不得重复移除（B）");
            Assert.AreEqual(1, _clientA.AppliedFrames.Count, "重复 318 不得重新施加（A）");
            Assert.AreEqual(1, _clientB.AppliedFrames.Count, "重复 318 不得重新施加（B）");
            AssertTintAbsent(_clientA);
            AssertTintAbsent(_clientB);

            // Act：移除已生效后再来一次同版本移除 318
            DeliverToBoth(RemovalUpdate);

            // Assert
            Assert.AreEqual(1, _clientA.RemovedFrames.Count, "移除后重复 318 不得再移除（A）");
            Assert.AreEqual(1, _clientB.RemovedFrames.Count, "移除后重复 318 不得再移除（B）");
        }

        /// <summary>
        /// 5) 非 318 来源只允许 `Remove(entityId)` 全量清除：AOI 出界/销毁路径对每个组各移除一次，
        /// 不得伪造单组移除，也不影响其他实体。
        /// </summary>
        [UnityTest]
        public IEnumerator test_status_non_projection_teardown_clears_all_groups_only()
        {
            // Arrange：两个组的状态（冰 + 火）
            DeliverToBoth(TwoStatusUpdate);
            yield return null;
            Assert.AreEqual(2, _clientA.AppliedFrames.Count, "两个组各施加一次（A）");
            Assert.AreEqual(2, _clientB.AppliedFrames.Count, "两个组各施加一次（B）");
            AssertTintPresent(_clientA);
            AssertTintPresent(_clientB);

            // Act：AOI 出界/销毁 → registry.ViewRemoved → AuthoritativeStatusVfx.Remove(entityId)
            Assert.IsTrue(_clientA.Registry.Remove(ClientAEntityId), "A 的视图应存在");

            // Assert：全量清除（每组一次），B 不受影响
            Assert.AreEqual(2, _clientA.RemovedFrames.Count,
                "Remove(entityId) 必须清除全部组（每组一次），不得伪造单组移除");
            Assert.AreEqual(0, _clientB.RemovedFrames.Count, "其他实体不受影响");
            AssertTintAbsent(_clientA);
            AssertTintPresent(_clientB);

            // Act：重复清除幂等（视图已移除）
            Assert.IsFalse(_clientA.Registry.Remove(ClientAEntityId));

            // Assert
            Assert.AreEqual(2, _clientA.RemovedFrames.Count, "重复 Remove(entityId) 不得再发移除");
        }

        // ---------------------------------------------------------------- 接线/断言辅助

        /// <summary>
        /// ClientNetworkHost 的接线模型：`StatusesChanged → Apply`（318 diff）与
        /// `ViewRemoved → Remove(entityId)`（AOI 出界/销毁全清）。宿主其余订阅（相机/名牌等）
        /// 与本契约无关，不复制。
        /// </summary>
        private Client CreateClient(string name, long entityId)
        {
            var root = new GameObject(name + "_Root");
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = AppearanceResolver.DefaultId, Full = _avatarPrefab } });
            var vfx = new AuthoritativeStatusVfx();
            registry.StatusesChanged += (id, target, statuses) =>
            {
                if (target != null) vfx.Apply(id, target.transform, statuses);
            };
            registry.ViewRemoved += id => vfx.Remove(id);

            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = entityId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                X = 1f, Y = 0f, Z = 2f,
                Kind = (byte)EntityKind.Player,
            }, serverTicks: 0);

            var view = registry.GetView(entityId);
            Assert.IsNotNull(view, "快照应建立视图");
            Assert.IsNotNull(view.FullRoot, "视图应建立 Full 渲染根");
            var renderer = view.FullRoot.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(renderer, "染色目标应带 Renderer");

            var client = new Client
            {
                EntityId = entityId,
                Root = root,
                Registry = registry,
                Renderer = renderer,
            };
            _clients.Add(client);
            return client;
        }

        private void DeliverToBoth(System.Func<long, CombatStateUpdate> updateFactory)
        {
            Deliver(_clientA, updateFactory(ClientAEntityId));
            Deliver(_clientB, updateFactory(ClientBEntityId));
        }

        private static void Deliver(Client client, CombatStateUpdate update)
            => client.Registry.ApplyCombatStateUpdate(update);

        private void OnApplied(StatusEffectAppliedEvent e)
            => FindClient(e.EntityId)?.AppliedFrames.Add(Time.frameCount);

        private void OnRemoved(StatusEffectRemovedEvent e)
            => FindClient(e.EntityId)?.RemovedFrames.Add(Time.frameCount);

        private Client FindClient(int entityId)
        {
            foreach (var client in _clients)
                if (client.EntityId == entityId) return client;
            return null;
        }

        private void AssertTintPresent(Client client)
            => Assert.IsTrue(Tints.Contains((int)client.EntityId),
                client.Root.name + " 应有染色条目（状态保持）");

        private void AssertTintAbsent(Client client)
            => Assert.IsFalse(Tints.Contains((int)client.EntityId),
                client.Root.name + " 不应再有染色条目");

        private static void AssertRendererCleared(Client client)
        {
            var block = new MaterialPropertyBlock();
            client.Renderer.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, client.Root.name + " 移除后渲染器不应再带染色 property block");
        }

        /// <summary>基线 318：版本 1，冰状态（EndTick 只是协议字段，本测试不据此计时）。</summary>
        private static CombatStateUpdate BaselineUpdate(long entityId)
            => StatusUpdate(entityId, BaselineCombatVersion,
                (FreezeEffectId, PrimaryGroupId), (0, 0));

        /// <summary>移除 318：版本 2，状态列表为空（自然结束的唯一权威来源）。</summary>
        private static CombatStateUpdate RemovalUpdate(long entityId)
            => StatusUpdate(entityId, RemovalCombatVersion, (0, 0), (0, 0));

        /// <summary>两个组的状态：冰 + 火（非 318 全量清除用例）。</summary>
        private static CombatStateUpdate TwoStatusUpdate(long entityId)
            => StatusUpdate(entityId, BaselineCombatVersion,
                (FreezeEffectId, PrimaryGroupId), (BurnEffectId, SecondaryGroupId));

        private static CombatStateUpdate StatusUpdate(long entityId, uint combatVersion,
            (int EffectId, int GroupId) first, (int EffectId, int GroupId) second)
        {
            var statuses = new List<StatusProjection>();
            if (first.EffectId != 0)
            {
                statuses.Add(new StatusProjection
                {
                    EffectId = first.EffectId,
                    GroupId = first.GroupId,
                    EndTick = ServerGameTick + StatusDurationTicks,
                    SourceId = 7,
                });
            }
            if (second.EffectId != 0)
            {
                statuses.Add(new StatusProjection
                {
                    EffectId = second.EffectId,
                    GroupId = second.GroupId,
                    EndTick = ServerGameTick + StatusDurationTicks,
                    SourceId = 8,
                });
            }

            return new CombatStateUpdate
            {
                EntityId = entityId,
                ServerTimestamp = 638_000_000_000_000_000L,
                Projection = new CombatProjection
                {
                    CombatVersion = combatVersion,
                    Hp = 100f,
                    MaxHp = 100f,
                    Statuses = statuses,
                },
            };
        }
    }
}
