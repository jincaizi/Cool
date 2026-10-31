using System;
using System.Collections;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// D6 PlayMode 验收：**重复的玩家快照不得清掉远程受击表现**。
    ///
    /// 服务端玩家快照的 `ActionState` 恒 0（D-P5-5）。旧实现每个 tick 都按
    /// `ActionState` 重写受击层，于是事件驱动的 `PlayHit` 表现会在下一条快照到达时当场消失。
    /// 现在快照只喂状态（`Dead` / `IsDefending`），受击是事件，由 `RemoteEntityAnimator`
    /// 的仲裁器计时释放。
    ///
    /// 受击走**生产路径**：`CombatNetworkBridge` 消费 `DamageResult` → `view.Animator.PlayHit`
    /// （PlayMode 测试程序集没有 `InternalsVisibleTo`，不直接调 internal 方法）。
    /// 快照周期按服务端 66ms（15Hz）取；两条 `ActionState=0` 的快照跨越 ≥2 个周期
    /// （0 → 66ms → 132ms），仍远短于 0.5s 的受击时长。
    /// </summary>
    public class RemoteHitSnapshotPersistenceTests
    {
        private const long SnapshotPeriodTicks = 660_000L;   // 66ms × 10,000,000 ticks/s

        private sealed class StubClient : INetworkClient
        {
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;
            public void Dispose() { }
        }

        private GameObject _root = null!;
        private GameObject _avatarPrefab = null!;
        private RemoteEntityRegistry _registry = null!;
        private CombatNetworkBridge _bridge = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("RemoteHitSnapshotRoot");

            // 与 RemoteRenderBranchTests 同款：运行时角色预制体替身。受击表现的判据取在
            // 仲裁结果 `LastHitState` 上——空 Animator 没有控制器，驱动照样记录仲裁结果，
            // 生产预制体走的是同一条代码路径。
            _avatarPrefab = new GameObject("AvatarPrefabDouble");
            _avatarPrefab.AddComponent<Animator>();

            _registry = new RemoteEntityRegistry(_root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = _avatarPrefab } });
            _bridge = new CombatNetworkBridge(new StubClient(), () => 999L, _registry);
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Clear();
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_avatarPrefab);
        }

        private static EntitySnapshot Snap(long playerId, uint tick)
        {
            return new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                    | EntityFieldMask.Kind | EntityFieldMask.ActionState
                    | EntityFieldMask.Dead | EntityFieldMask.Health | EntityFieldMask.Defending),
                X = 1f, Y = 0f, Z = 2f, Rotation = 0f,
                Kind = (byte)EntityKind.Player,
                ActionState = 0,   // D-P5-5：服务端玩家动作状态恒 0
                Dead = false,
                Hp = 100f, MaxHp = 100f, IsDefending = false,
                Tick = tick
            };
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        /// <summary>生产路径：DamageResult → CombatNetworkBridge → 远程视图 PlayHit。</summary>
        private RemoteEntityAnimator PrepareHit(bool blocked)
        {
            const long id = 7;
            _registry.ApplySnapshot(Snap(id, 1), 0);
            var view = _registry.GetView(id);
            Assert.IsNotNull(view, "视图应在首条快照后建立");
            Assert.IsNotNull(view!.Animator, "真人远程视图必须挂动画驱动");

            var result = new DamageResult
            {
                SourcePlayerId = 1,
                SourceKind = (int)EntityKind.Player,
                SkillId = 10001
            };
            result.Hits.Add(new HitEntry
            {
                TargetPlayerId = id,
                Damage = 10f,
                TargetRemainingHp = 90f,
                IsBlocked = blocked
            });
            _bridge.OnMessage(MessageId.DamageResult, result);
            _bridge.Update();

            Assert.AreEqual((int)(blocked ? HitState.DefendHit : HitState.Hit), view.Animator.LastHitState,
                "前置：DamageResult 事件应建立受击表现");
            Assert.AreEqual(0, view.ActionState, "前置：服务端玩家快照 ActionState 恒 0");
            return view.Animator;
        }

        private IEnumerator ApplyTwoActionStateZeroSnapshots(long id, RemoteEntityAnimator driver, int expected)
        {
            // 第二条快照：一个周期后
            yield return WaitRealtime(0.066f);
            _registry.ApplySnapshot(Snap(id, 2), SnapshotPeriodTicks);
            Assert.AreEqual(expected, driver.LastHitState,
                "第 1 条 ActionState=0 的快照不得清掉受击表现");

            // 第三条快照：再一个周期，跨越 ≥2 个快照周期
            yield return WaitRealtime(0.066f);
            _registry.ApplySnapshot(Snap(id, 3), SnapshotPeriodTicks * 2);
            Assert.AreEqual(expected, driver.LastHitState,
                "跨越 ≥2 个快照周期后，受击表现仍必须保持（D6）");
        }

        [UnityTest]
        public IEnumerator Remote_Normal_Hit_Survives_Two_ActionState_Zero_Snapshots()
        {
            var driver = PrepareHit(blocked: false);
            yield return ApplyTwoActionStateZeroSnapshots(7, driver, (int)HitState.Hit);
        }

        [UnityTest]
        public IEnumerator Remote_Blocked_Hit_Survives_Two_ActionState_Zero_Snapshots()
        {
            var driver = PrepareHit(blocked: true);
            yield return ApplyTwoActionStateZeroSnapshots(7, driver, (int)HitState.DefendHit);
        }
    }
}
