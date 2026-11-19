using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Network;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Sys3CController = Hotfix.GameSystems.Sys3C.Character.CharacterController;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// Plan 05 任务 5：麻痹（StatusActionLock）验收。
    /// 真实 CharacterController + 实心 layer 10 盒体（NpcBody 同层）；停掉输入驱动后
    /// 手动步进 Update(MoveCommand)，用位移/序号数字断言：
    /// ① 空中麻痹保留起跳水平惯性、拒绝新起跳，落地即停、解锁恢复输入；
    /// ② 地面麻痹水平位移为 0 且拒绝起跳；
    /// ③ 贴 NpcBody 持续挤压无往返抖动、不穿透、不超**本地**步行预算代理
    /// （前三条用例无服务器；服务端 tick 预算/纠偏由服务端 PvpMovementBudgetTests 覆盖）。
    /// Plan 06 任务 5 追加末条用例：启用 Sys3CEntry 推挤 + 移动 NpcBody + 真实 Server3C
    /// 上报/纠偏联测（该用例需要 `127.0.0.1:8000` 上的 Server3C）。
    /// </summary>
    public class PlayerControlInertiaTests
    {
        /// <summary>步进帧上限（防 Time.deltaTime 异常时死循环；墙钟由测试框架 300s 兜底）。</summary>
        private const int MaxFrames = 200000;

        /// <summary>Plan 06 任务 5：移动 NpcBody 的 X 向速度（m/s）与观察窗口（秒）。</summary>
        private const float MovingBodySpeed = 1.5f;
        private const float MovingBodyWindowSeconds = 2f;

        /// <summary>任务 5 断言 1 的净变化上限（作用在 0.25s 平滑后的相对偏移上）。</summary>
        private const float NetOffsetChangeLimit = 0.1f;

        /// <summary>PredictionReconciler.PositionThreshold：上报位置与权威目标超过它才纠偏。</summary>
        private const float CorrectionThreshold = 0.5f;

        private GameObject _player;
        private GameObject _ground;
        private GameObject _body;
        private Sys3CEntry _entry;
        private Sys3CController _cc;
        private UnityEngine.CharacterController _unityController;
        private GameObject _registryRoot;
        private ClientNetworkManager _networkManager;

        /// <summary>脚本化输入适配器：让**启用中的** Sys3CEntry 走真实输入→移动管线（贴附输入）。</summary>
        private sealed class ScriptedInputAdapter : IInputAdapter
        {
            public Vector3 Move = Vector3.zero;
            public string AdapterName => "Scripted";
            public Vector3 GetMoveInput() => Move;
            public Vector2 GetCameraRotationInput() => Vector2.zero;
            public bool HasMoveInput() => Move.sqrMagnitude > 0f;
            public bool HasCameraRotationInput() => false;
        }

        /// <summary>
        /// NpcBody 上的判定桩：Sys3CEntry.OnControllerColliderHit 以 collider 上有 IDamageable
        /// 为推离门槛（生产 `NpcBodyCollider` 只挂 BoxCollider，见 backlog）。本夹具显式提供桩，
        /// 以启用**真实推挤路径**——不是生产行为变化。
        /// </summary>
        private sealed class PushDamageableStub : MonoBehaviour, IDamageable
        {
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection) { }
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact) { }
            public bool IsAlive => true;
            public Transform Transform => transform;
        }

        /// <summary>
        /// 包装真实 KCP 客户端：生产消息链路原样转发，仅旁路记录 PositionSync 请求/响应，
        /// 供断言"上报位置 vs 服务端权威目标"与"超预算包数"。响应在回调线程入队，
        /// 读取在主线程（与 ClientNetworkManager 的封送纪律一致）。
        /// </summary>
        private sealed class RecordingClient : INetworkClient
        {
            private readonly INetworkClient _inner;
            public readonly ConcurrentQueue<PositionSyncRequest> Requests =
                new ConcurrentQueue<PositionSyncRequest>();
            public readonly ConcurrentQueue<PositionSyncResponse> Responses =
                new ConcurrentQueue<PositionSyncResponse>();

            public RecordingClient(INetworkClient inner)
            {
                _inner = inner;
                _inner.MessageReceived += OnInnerMessage;
                _inner.Disconnected += reason => Disconnected?.Invoke(reason);
                _inner.Error += ex => Error?.Invoke(ex);
            }

            private void OnInnerMessage(MessageId id, IMessage message)
            {
                if (id == MessageId.PositionSyncResponse)
                    Responses.Enqueue((PositionSyncResponse)message);
                MessageReceived?.Invoke(id, message);
            }

            public bool IsConnected => _inner.IsConnected;
            public Task<bool> ConnectAsync(string host, int port) => _inner.ConnectAsync(host, port);
            public Task DisconnectAsync(string reason) => _inner.DisconnectAsync(reason);

            public void Send(MessageId messageId, IMessage message,
                MessageFlags flags = MessageFlags.Reliable)
            {
                if (messageId == MessageId.PositionSyncRequest)
                    Requests.Enqueue((PositionSyncRequest)message);
                _inner.Send(messageId, message, flags);
            }

            public event Action<MessageId, IMessage>? MessageReceived;
            public event Action<string>? Disconnected;
            public event Action<Exception>? Error;
            public void Dispose() => _inner.Dispose();
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);

            // 已知 Unity 编辑器伪报：Resources.Load 预制体变体时 PPtr 反引用会打
            // "PPtr cast failed when dereferencing! Casting from GameObject to Prefab!"，
            // 资源本身加载成功（既有 PlayerJumpBodyCollisionTests 同款）。只包住加载本身。
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            LogAssert.ignoreFailingMessages = previousIgnore;
            Assert.IsNotNull(prefab, "测试用玩家 Prefab 缺失");
            _player = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, 2f, 0f), Quaternion.identity);
            _player.layer = 9; // NpcBody 层矩阵只放行本地玩家层 9
            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry);
            yield return null;

            _entry.enabled = false; // 停掉输入驱动，测试手动步进 CharacterController
            _cc = _entry.CoreController;
            _unityController = _player.GetComponent<UnityEngine.CharacterController>();
            Assert.IsNotNull(_cc);
            Assert.IsNotNull(_unityController);
        }

        [TearDown]
        public void TearDown()
        {
            _networkManager?.Dispose();
            _networkManager = null;
            if (_player != null) UnityEngine.Object.Destroy(_player);
            if (_body != null) UnityEngine.Object.Destroy(_body);
            if (_ground != null) UnityEngine.Object.Destroy(_ground);
            if (_registryRoot != null) UnityEngine.Object.Destroy(_registryRoot);
        }

        private static MoveCommand Forward() => new MoveCommand
        {
            MoveDir = Vector3.forward,
            Rotation = Quaternion.identity,
            IsSprint = false
        };

        private static MoveCommand Idle() => new MoveCommand
        {
            MoveDir = Vector3.zero,
            Rotation = Quaternion.identity
        };

        private IEnumerator SettleOnGround()
        {
            float elapsed = 0f;
            int frames = 0;
            while (!_cc.Data.IsGrounded && elapsed < 10f && frames < MaxFrames)
            {
                _cc.Update(Idle());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }
            Assert.IsTrue(_cc.Data.IsGrounded, "前置：角色应落到测试地面");
        }

        private void CreateBody(Vector3 position)
        {
            _body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _body.name = "NpcBody";
            _body.layer = 10; // NpcBody：只与本地玩家层 9 碰撞
            _body.transform.position = position;
            _body.transform.localScale = new Vector3(1f, 2f, 1f);
        }

        [UnityTest]
        public IEnumerator test_air_action_lock_keeps_jump_inertia_until_landing()
        {
            yield return SettleOnGround();

            // 先向 +Z 走 0.2s 建立起跳水平速度（WalkSpeed=5）。
            float elapsed = 0f;
            int frames = 0;
            while (elapsed < 0.2f && frames < MaxFrames)
            {
                _cc.Update(Forward());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }
            Vector3 preJumpVelocity = _unityController.velocity;
            Vector3 preJumpHorizontal = new Vector3(preJumpVelocity.x, 0f, preJumpVelocity.z);
            float jumpSpeed = preJumpHorizontal.magnitude;
            Vector3 jumpDir = preJumpHorizontal.normalized;
            Assert.Greater(jumpDir.z, 0.9f,
                "前置：起跳前应为 +Z 水平移动（velocity=" + preJumpVelocity + "）");
            Assert.Greater(jumpSpeed, 0.5f,
                "前置：起跳前应有水平速度（velocity=" + preJumpVelocity + "）");

            _cc.RequestJump();
            _cc.Update(Forward());
            yield return null;
            Assert.AreEqual(1u, _cc.JumpSeq, "前置：起跳必须被消费");
            Assert.IsTrue(_cc.IsAirborne, "前置：起跳后应处于空中");

            // 起跳瞬间上麻痹：保留水平惯性，拒绝新输入与新起跳。
            _cc.SetStatusActionLock(true);
            uint seqAtLock = _cc.JumpSeq;
            Vector3 p0 = _player.transform.position;
            // 锁定后只发一次新起跳请求（每帧发会刷屏 [JumpProbe]；锁拒绝的语义由这一次 + 地面用例覆盖）。
            _cc.RequestJump();

            // 空中推进 0.3s（输入 Idle；麻痹下只允许起跳水平惯性）。
            elapsed = 0f;
            frames = 0;
            while (elapsed < 0.3f && frames < MaxFrames)
            {
                _cc.Update(Idle());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }

            Vector3 airDelta = _player.transform.position - p0;
            Vector3 airHorizontal = new Vector3(airDelta.x, 0f, airDelta.z);
            Assert.Greater(airHorizontal.magnitude, 0.2f,
                "空中麻痹必须保留起跳水平惯性（displacement=" + airHorizontal + "）");
            Assert.Greater(Vector3.Dot(airHorizontal.normalized, jumpDir), 0.99f,
                "惯性方向应与起跳水平速度一致（delta=" + airHorizontal + " dir=" + jumpDir + "）");

            float airSpeed = airHorizontal.magnitude / Mathf.Max(elapsed, 1e-4f);
            Assert.That(airSpeed, Is.InRange(jumpSpeed * 0.8f, jumpSpeed * 1.2f),
                "惯性速度应保持起跳水平速度（measured=" + airSpeed.ToString("F2")
                + " jump=" + jumpSpeed.ToString("F2") + "）");
            Assert.AreEqual(seqAtLock, _cc.JumpSeq, "麻痹期间不得消费新起跳（JumpSeq 不应增加）");
            Debug.Log("[Inertia] air 0.3s displacement=" + airHorizontal.ToString("F3")
                      + " speed=" + airSpeed.ToString("F2")
                      + " jumpSeq=" + _cc.JumpSeq);

            // 推进到落地：落地即清零惯性，位移停止。
            elapsed = 0f;
            frames = 0;
            while (_cc.IsAirborne && elapsed < 5f && frames < MaxFrames)
            {
                _cc.Update(Idle());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }
            Assert.IsFalse(_cc.IsAirborne, "前置：应在 5s 内落地");

            Vector3 landPos = _player.transform.position;
            Vector3 landDelta = landPos - p0;
            Vector3 landHorizontal = new Vector3(landDelta.x, 0f, landDelta.z);
            // 0.3s 窗口之后到落地仍在推进：落地时的累计水平位移必须显著大于窗口内位移。
            Assert.Greater(landHorizontal.magnitude, airHorizontal.magnitude + 0.3f,
                "落地前惯性必须继续（air=" + airHorizontal.magnitude.ToString("F3")
                + "m land=" + landHorizontal.magnitude.ToString("F3") + "m）");

            elapsed = 0f;
            frames = 0;
            while (elapsed < 0.2f && frames < MaxFrames)
            {
                _cc.Update(Idle());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }
            Vector3 postLandDelta = _player.transform.position - landPos;
            Vector3 postLandHorizontal = new Vector3(postLandDelta.x, 0f, postLandDelta.z);
            Assert.LessOrEqual(postLandHorizontal.magnitude, 1e-3f,
                "落地后位移必须停止（delta=" + postLandHorizontal + "）");

            // 解锁后移动输入恢复。
            _cc.SetStatusActionLock(false);
            Vector3 unlockPos = _player.transform.position;
            elapsed = 0f;
            frames = 0;
            while (elapsed < 0.2f && frames < MaxFrames)
            {
                _cc.Update(Forward());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }
            float movedAfterUnlock = _player.transform.position.z - unlockPos.z;
            Assert.Greater(movedAfterUnlock, 0.2f,
                "解锁后移动输入应恢复（moved=" + movedAfterUnlock.ToString("F2") + "m）");
            Debug.Log("[Inertia] landDisplacement=" + landHorizontal.magnitude.ToString("F3")
                      + " postLand displacement=" + postLandHorizontal.magnitude.ToString("F4")
                      + " unlockMoved=" + movedAfterUnlock.ToString("F3"));
        }

        [UnityTest]
        public IEnumerator test_ground_action_lock_rejects_move_and_jump()
        {
            yield return SettleOnGround();

            _cc.SetStatusActionLock(true);
            uint seqAtLock = _cc.JumpSeq;
            Vector3 p0 = _player.transform.position;

            _cc.RequestJump();
            float elapsed = 0f;
            int frames = 0;
            while (elapsed < 0.2f && frames < MaxFrames)
            {
                _cc.Update(Forward());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }

            Vector3 delta = _player.transform.position - p0;
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            Assert.LessOrEqual(horizontal.magnitude, 1e-3f,
                "地面麻痹：水平位移必须为 0（delta=" + horizontal + "）");
            Assert.AreEqual(seqAtLock, _cc.JumpSeq,
                "地面麻痹：新起跳请求必须被拒绝（JumpSeq 不变）");
            Assert.IsTrue(_cc.Data.IsGrounded, "前置：地面麻痹期间应保持接地");
            Debug.Log("[Inertia] groundLock displacement=" + horizontal.magnitude.ToString("F5")
                      + " jumpSeq=" + _cc.JumpSeq);
        }

        [UnityTest]
        public IEnumerator test_push_against_npc_body_has_no_sustained_jitter()
        {
            yield return SettleOnGround();
            CreateBody(new Vector3(0f, 1f, 1.5f)); // 近面 z=1.0；玩家胶囊半径 0.5
            yield return null;

            // 夹具停用了 Sys3CEntry 的输入驱动，因此本用例只验收 CharacterController 对
            // NpcBody 层（10）静态实体盒的推挤稳定性。生产路径的推离反馈
            // （Sys3CEntry.OnControllerColliderHit）要求碰撞体上有 IDamageable，而远程视图的
            // NpcBody 子物体只挂了 BoxCollider（无 IDamageable）⇒ 推离反馈对 NpcBody 实际不触发，
            // 属生产行为/限制（已登记 backlog），不是本夹具证明稳定的“抖动债”。

            // 先向前压住盒体 1s（无阻挡会走 5m；实际约 0.4m 即被挡）。
            float elapsed = 0f;
            int frames = 0;
            while (elapsed < 1f && frames < MaxFrames)
            {
                _cc.Update(Forward());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }
            Vector3 blocked = _player.transform.position;
            // 近面 z=1.0 − 胶囊半径 0.5 − skinWidth 0.09 → 期望停在 ~0.41；0.5 内即未穿透。
            Assert.LessOrEqual(blocked.z, 0.5f,
                "前置：应被 NpcBody 挡住（blocked z=" + blocked.z.ToString("F3") + "）");

            // 持续挤压 0.5s：记录位置往返幅度与水平位移。
            float window = 0f;
            float minZ = blocked.z;
            float maxZ = blocked.z;
            frames = 0;
            while (window < 0.5f && frames < MaxFrames)
            {
                _cc.Update(Forward());
                yield return null;
                window += Time.deltaTime;
                frames++;
                float z = _player.transform.position.z;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            float amplitude = maxZ - minZ;
            Vector3 delta = _player.transform.position - blocked;
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            float moved = horizontal.magnitude;

            Assert.LessOrEqual(amplitude, 0.1f,
                "贴 NpcBody 挤压不得持续抖动（amplitude=" + amplitude.ToString("F3") + "m）");
            // 这是**本地输入预算代理**（步行速度 × 窗口时长）：夹具内没有服务器，也没有纠偏事件。
            // 服务端 tick 预算与纠偏（含空中麻痹包）由 PvpMovementBudgetTests 单独覆盖。
            Assert.LessOrEqual(moved, CombatRules.WalkSpeed * window + 0.01f,
                "挤压期间水平位移不得超过本地步行预算代理（moved=" + moved.ToString("F3")
                + "m walk=" + CombatRules.WalkSpeed + " window=" + window.ToString("F2") + "s）");
            Assert.LessOrEqual(maxZ, 0.5f,
                "不得穿透 NpcBody（maxZ=" + maxZ.ToString("F3") + "）");
            Debug.Log("[Inertia] push amplitude=" + amplitude.ToString("F4")
                      + " moved=" + moved.ToString("F4")
                      + " maxZ=" + maxZ.ToString("F3")
                      + " window=" + window.ToString("F2"));
        }

        /// <summary>
        /// Plan 06 任务 5：移动 `NpcBody` 推挤机制 + 位置上报/真实服务端纠偏联测。
        ///
        /// 设计文档要求碰撞推挤与冻结纠偏联测，避免两边反复移动同一角色。本夹具与上面的静态盒
        /// 用例不同：**启用真实 `Sys3CEntry`（推挤路径）**，`NpcBody` 以 1.5 m/s 沿 +X 移动 2s，
        /// 玩家经脚本化输入贴附挤压；同时经真实 `Server3C:8000` 以 15Hz 上报位置，
        /// `OnCorrectionNeeded → NetMovementPolicy.ApplyServerCorrection` 走生产纠偏落点。
        ///
        /// **覆盖面边界**：生产 `NpcBodyCollider.BuildCollider` 只给子物体挂 `BoxCollider`，
        /// 没有 `IDamageable`（`Sys3CEntry.OnControllerColliderHit` 的推离门槛），因此生产
        /// NpcBody 今天**不会**触发 `_pushVector`（已登记 backlog）。本用例在移动盒上挂测试桩
        /// `PushDamageableStub` 来启用推挤机制本身——验证的是"推挤 + 上报/纠偏"在真实管线下
        /// 不会持续互相拉扯，**不是**"生产 NpcBody 会推挤"的验收。静态盒用例（Sys3CEntry 关闭）
        /// 继续覆盖本地 CharacterController 对 NpcBody 层实体的稳定性。
        ///
        /// 断言：
        /// ① 2s 内**原始端点**相对偏移净变化 ≤0.1m；方向翻转 ≤1 在 0.25s 因果滑动平均上计数
        ///    （原始推挤锯齿 ≤ `_pushForce`=0.08m/次、周期 ~20ms，属验收允许的接触噪声；
        ///    真实纠偏环路是 ≥0.5s 尺度的位置摆动，平滑不会掩盖它）。平滑成立的前提是原始
        ///    振荡有界：同时断言原始最大偏离 ≤0.15m（观测 0.099–0.102m，留 ~47% 余量）；
        /// ② 每条 ack 的"上报位置 vs 服务端权威目标" ≤0.5m（纠正阈值），且窗口内纠偏次数
        ///    不超过被服务端钳制的包数；
        /// ③ 怪物停止后玩家稳定、无残余推挤速度；
        /// ④ 正控（故意 1.5m 超预算位移）必须触发 ≥1 次**真实**服务端纠偏并收敛到目标 0.5m 内，
        ///    证明纠偏目标来自服务器而不是夹具伪造。
        ///
        /// 运行前置：本机 Server3C（`dotnet Server/bin/Debug/net8.0/Server3C.dll 8000`）。
        /// </summary>
        [UnityTest]
        public IEnumerator test_moving_npc_body_push_mechanics_with_server_correction_have_no_sustained_ping_pong()
        {
            // —— Arrange：启用真实 Sys3CEntry（推挤在 Update 内），脚本化"贴附输入" ——
            var input = _player.GetComponent<InputManager>();
            Assert.IsNotNull(input, "前置：玩家应有 InputManager");
            InputBlockGate.ClearAll(); // 静态阻断可能跨用例残留（InputManager 会消费它）
            var scripted = new ScriptedInputAdapter();
            input.SetAdapter(scripted);
            _entry.enabled = true;
            yield return SettleOnGroundLive();

            // —— 真实 Server3C 会话（与 MovementSyncTests 同款真实登录/上报链路） ——
            _registryRoot = new GameObject("PushCorrectionRegistryRoot");
            var recording = new RecordingClient(new KcpNetClientAdapter());
            _networkManager = new ClientNetworkManager(
                recording, new RemoteEntityRegistry(_registryRoot.transform), "MovePushCorrection");
            _networkManager.ConnectAndLogin("127.0.0.1", 8000);
            float loginStart = Time.realtimeSinceStartup;
            int loginFrames = 0;
            while (_networkManager.CurrentState != ClientNetworkManager.State.InWorld
                   && Time.realtimeSinceStartup - loginStart < 10f && loginFrames < MaxFrames)
            {
                _networkManager.Update(Time.deltaTime);
                yield return null;
                loginFrames++;
            }
            Assert.AreEqual(ClientNetworkManager.State.InWorld, _networkManager.CurrentState,
                "前置：需要本机 Server3C（dotnet Server/bin/Debug/net8.0/Server3C.dll 8000）");

            var netPolicy = new NetMovementPolicy(_cc);
            _entry.MovementPolicy = netPolicy;
            int corrections = 0;
            _networkManager.OnCorrectionNeeded += (position, rotation) =>
            {
                corrections++;
                netPolicy.ApplyServerCorrection(position, rotation);
            };
            _networkManager.AttachMovementReporter(
                () =>
                {
                    var p = _player.transform.position;
                    return new Vec3(p.x, p.y, p.z);
                },
                () => _player.transform.eulerAngles.y,
                () =>
                {
                    var v = _unityController.velocity;
                    return new Vector3(v.x, 0f, v.z).magnitude;
                },
                stateProvider: () => new MovementReportState
                {
                    Mode = LocomotionMode.Walk,
                    JumpSeq = _cc.JumpSeq,
                    Airborne = _cc.IsAirborne,
                    Grounded = _cc.Data.IsGrounded,
                    VerticalVelocity = _unityController.velocity.y,
                    ControlVersion = _entry.ControlVersion
                });

            // —— 移动 NpcBody + 贴附输入：先建立接触，再开始 2s 观察窗口 ——
            CreateBody(new Vector3(1.5f, 1f, 0f)); // 近面 x=1.0；胶囊半径 0.5+skin 0.09
            _body.AddComponent<PushDamageableStub>(); // 生产 NpcBody 无 IDamageable；本夹具显式启用推挤路径
            yield return null;

            scripted.Move = Vector3.right;
            float approach = 0f;
            int approachFrames = 0;
            while (approach < 1f
                   && Mathf.Abs(_player.transform.position.x - _body.transform.position.x) > 1.2f
                   && approachFrames < MaxFrames)
            {
                approach += Time.deltaTime;
                yield return null;
                approachFrames++;
            }
            float contactGap = Mathf.Abs(_player.transform.position.x - _body.transform.position.x);
            Assert.LessOrEqual(contactGap, 1.2f,
                "前置：玩家应贴住移动 NpcBody（gap=" + contactGap.ToString("F3") + "m）");

            uint windowStartSeq = LatestRequestSequence(recording);
            int correctionsAtWindowStart = corrections;
            int pushFrames = 0;
            var relativeOffsets = new List<Vector3> { RelativeXzOffset() };
            var offsetTimes = new List<float> { 0f };
            float elapsed = 0f;
            int windowFrames = 0;
            while (elapsed < MovingBodyWindowSeconds && windowFrames < MaxFrames)
            {
                float dt = Time.deltaTime;
                _body.transform.position += Vector3.right * (MovingBodySpeed * dt);
                _networkManager.Update(dt); // 上报节流 + 主线程纠偏对账
                yield return null;
                elapsed += Time.deltaTime;
                // 推挤在 Sys3CEntry.Update 中于输入移动之后 ApplyPushVector：推挤帧速度反向。
                if (_unityController.velocity.x < -0.1f) pushFrames++;
                relativeOffsets.Add(RelativeXzOffset());
                offsetTimes.Add(elapsed);
                windowFrames++;
            }
            uint windowEndSeq = LatestRequestSequence(recording);

            // 松手并排空窗口内 ack（怪物停下，玩家不再贴附输入）。
            scripted.Move = Vector3.zero;
            float drain = 0f;
            int drainFrames = 0;
            while (drain < 0.5f && drainFrames < MaxFrames)
            {
                _networkManager.Update(Time.deltaTime);
                yield return null;
                drain += Time.deltaTime;
                drainFrames++;
            }

            // —— 断言 1：无持续往返 ——
            // 净变化按验收字面用**原始端点**（不经平滑，避免端点混用带来的偏置）。
            // 方向翻转在 0.25s 因果滑动平均上计数：接触推挤锯齿（_pushForce=0.08m，周期 ~20ms）
            // 与偶发帧顿挫是高频噪声，真实纠偏环路是 ≥0.5s 尺度的位置摆动，平滑不会掩盖它。
            // 平滑成立的前提是原始振荡有界，因此同时断言 rawAmplitude ≤0.15m（观测 0.099–0.102m）。
            Vector3 rawNet = relativeOffsets[relativeOffsets.Count - 1] - relativeOffsets[0];
            float netChange = new Vector2(rawNet.x, rawNet.z).magnitude;
            Vector3[] smoothed = MovingAverage(relativeOffsets, offsetTimes, 0.25f);
            Vector3 smoothNet = smoothed[smoothed.Length - 1] - smoothed[0];
            float smoothedNetChange = new Vector2(smoothNet.x, smoothNet.z).magnitude;
            int reversals = CountReversals(smoothed, 0.03f);
            float rawAmplitude = MaxExcursion(relativeOffsets);
            Assert.Greater(pushFrames, 0,
                "前置：贴附挤压期间真实推挤路径必须触发（OnControllerColliderHit → ApplyPushVector）");
            Assert.LessOrEqual(netChange, NetOffsetChangeLimit,
                "移动 NpcBody 下玩家相对 XZ 偏移净变化必须 ≤0.1m（rawNet="
                + netChange.ToString("F4") + "m smoothNet=" + smoothedNetChange.ToString("F4") + "m）");
            Assert.LessOrEqual(rawAmplitude, 0.15f,
                "原始推挤振荡必须有界（平滑反翻转计数的前提；rawAmplitude="
                + rawAmplitude.ToString("F3") + "m）");
            Assert.LessOrEqual(reversals, 1,
                "移动 NpcBody 下不得出现持续往返（reversals=" + reversals
                + " rawAmplitude=" + rawAmplitude.ToString("F3") + "m）");

            // —— 断言 2：上报位置 vs 服务端权威目标；纠偏不超过超预算包数 ——
            var windowRequests = new Dictionary<uint, PositionSyncRequest>();
            foreach (var request in recording.Requests.ToArray())
                if (request.Sequence > windowStartSeq && request.Sequence <= windowEndSeq)
                    windowRequests[request.Sequence] = request;

            int windowAcks = 0;
            int windowOverBudget = 0;
            float maxReportToAuthoritative = 0f;
            foreach (var response in recording.Responses.ToArray())
            {
                if (response.AcknowledgedSequence <= windowStartSeq
                    || response.AcknowledgedSequence > windowEndSeq) continue;
                if (!windowRequests.TryGetValue(response.AcknowledgedSequence, out var request)) continue;

                windowAcks++;
                if (response.HasPositionCorrection) windowOverBudget++;
                float deviation = new Vector2(request.X - response.AuthoritativeX,
                    request.Z - response.AuthoritativeZ).magnitude;
                if (deviation > maxReportToAuthoritative) maxReportToAuthoritative = deviation;
            }

            int windowCorrections = corrections - correctionsAtWindowStart;
            Assert.Greater(windowAcks, 10,
                "前置：2s 窗口内应有 ≥10 条服务端确认（上报节流 15Hz；acks=" + windowAcks + "）");
            Assert.LessOrEqual(maxReportToAuthoritative, CorrectionThreshold,
                "上报位置与服务端权威目标差必须 ≤0.5m（max="
                + maxReportToAuthoritative.ToString("F3") + "m）");
            Assert.LessOrEqual(windowCorrections, windowOverBudget,
                "窗口内纠偏次数不得超过超预算包数（corrections=" + windowCorrections
                + " overBudget=" + windowOverBudget + "）");

            // —— 断言 3：怪物停止后稳定、无残余推挤速度 ——
            Vector3 settleStart = _player.transform.position;
            float settle = 0f;
            int settleFrames = 0;
            while (settle < 0.3f && settleFrames < MaxFrames)
            {
                _networkManager.Update(Time.deltaTime);
                yield return null;
                settle += Time.deltaTime;
                settleFrames++;
            }
            Vector3 settleDelta = _player.transform.position - settleStart;
            Vector3 settledVelocity = _unityController.velocity;
            Assert.LessOrEqual(new Vector2(settleDelta.x, settleDelta.z).magnitude, 1e-3f,
                "怪物停止后玩家应稳定在最后位置（delta="
                + new Vector2(settleDelta.x, settleDelta.z).magnitude.ToString("F4") + "m）");
            Assert.LessOrEqual(new Vector2(settledVelocity.x, settledVelocity.z).magnitude, 0.01f,
                "怪物停止后不得残留推挤速度（velocity=" + settledVelocity + "）");

            // —— 正控：故意超预算位移 → 真实服务器纠偏 + 收敛 ——
            uint teleportSeq = LatestRequestSequence(recording);
            int correctionsBeforeTeleport = corrections;
            _cc.Teleport(_player.transform.position + Vector3.forward * 1.5f);
            float correctionWait = 0f;
            int correctionWaitFrames = 0;
            while (corrections == correctionsBeforeTeleport && correctionWait < 3f
                   && correctionWaitFrames < MaxFrames)
            {
                _networkManager.Update(Time.deltaTime);
                yield return null;
                correctionWait += Time.deltaTime;
                correctionWaitFrames++;
            }
            Assert.Greater(corrections, correctionsBeforeTeleport,
                "正控：1.5m 超预算位移必须触发真实服务端纠偏（证明权威目标来自服务器）");

            Vector3 lastTarget = ToVector3(_networkManager.Reporter!.LastCorrectionTarget);
            float converge = 0f;
            int convergeFrames = 0;
            while (converge < 2f && convergeFrames < MaxFrames
                   && new Vector2(_player.transform.position.x - lastTarget.x,
                       _player.transform.position.z - lastTarget.z).magnitude > 0.1f)
            {
                lastTarget = ToVector3(_networkManager.Reporter!.LastCorrectionTarget);
                _networkManager.Update(Time.deltaTime);
                yield return null;
                converge += Time.deltaTime;
                convergeFrames++;
            }
            lastTarget = ToVector3(_networkManager.Reporter!.LastCorrectionTarget);
            float finalGap = new Vector2(_player.transform.position.x - lastTarget.x,
                _player.transform.position.z - lastTarget.z).magnitude;
            Assert.LessOrEqual(finalGap, CorrectionThreshold,
                "纠偏收敛后本地位置与服务端目标差必须 ≤0.5m（gap="
                + finalGap.ToString("F3") + "m）");

            int teleportOverBudget = 0;
            foreach (var response in recording.Responses.ToArray())
                if (response.AcknowledgedSequence > teleportSeq && response.HasPositionCorrection)
                    teleportOverBudget++;
            Assert.LessOrEqual(corrections - correctionsBeforeTeleport, teleportOverBudget,
                "正控纠偏次数不得超过超预算包数（corrections="
                + (corrections - correctionsBeforeTeleport) + " overBudget=" + teleportOverBudget + "）");

            int totalOverBudget = 0;
            foreach (var response in recording.Responses.ToArray())
                if (response.HasPositionCorrection) totalOverBudget++;
            Assert.LessOrEqual(corrections, totalOverBudget,
                "纠偏次数不得超过超预算包数（corrections=" + corrections
                + " overBudget=" + totalOverBudget + "）");

            Debug.Log("[Inertia] moving body: pushFrames=" + pushFrames
                      + " netRaw=" + netChange.ToString("F4") + "m"
                      + " netSmooth=" + smoothedNetChange.ToString("F4") + "m"
                      + " rawAmp=" + rawAmplitude.ToString("F3") + "m"
                      + " reversals=" + reversals
                      + " windowAcks=" + windowAcks
                      + " maxReportToAuth=" + maxReportToAuthoritative.ToString("F3") + "m"
                      + " corrections=" + corrections
                      + " windowOverBudget=" + windowOverBudget
                      + " lastTarget=" + lastTarget
                      + " finalGap=" + finalGap.ToString("F3") + "m");
        }

        private IEnumerator SettleOnGroundLive()
        {
            float elapsed = 0f;
            int frames = 0;
            while (!_cc.Data.IsGrounded && elapsed < 10f && frames < MaxFrames)
            {
                yield return null; // Sys3CEntry.Update 自己驱动 CharacterController
                elapsed += Time.deltaTime;
                frames++;
            }
            Assert.IsTrue(_cc.Data.IsGrounded, "前置：角色应落到测试地面");
        }

        private Vector3 RelativeXzOffset()
        {
            var player = _player.transform.position;
            var body = _body.transform.position;
            return new Vector3(player.x - body.x, 0f, player.z - body.z);
        }

        /// <summary>
        /// 相对偏移的方向翻转计数。只统计**有意义的移动**：偏移须从上个拐点走出
        /// ≥ <paramref name="minExcursion"/> 才确立方向；更小的往返不计（调用方传入的是
        /// 0.25s 滑动平均后的信号，接触锯齿/帧顿挫已被滤掉）。
        /// </summary>
        private static int CountReversals(IReadOnlyList<Vector3> offsets, float minExcursion)
        {
            int reversals = 0;
            Vector3 anchor = offsets[0];
            Vector3 direction = Vector3.zero;
            bool hasDirection = false;

            for (int i = 1; i < offsets.Count; i++)
            {
                Vector3 excursion = offsets[i] - anchor;
                if (excursion.magnitude < minExcursion) continue;

                Vector3 next = excursion.normalized;
                if (hasDirection && Vector3.Dot(next, direction) < 0f) reversals++;
                direction = next;
                hasDirection = true;
                anchor = offsets[i];
            }
            return reversals;
        }

        /// <summary>因果滑动平均（时间戳与样本一一对应）：滤掉接触锯齿与帧顿挫这类高频噪声。</summary>
        private static Vector3[] MovingAverage(IReadOnlyList<Vector3> offsets,
            IReadOnlyList<float> times, float windowSeconds)
        {
            var result = new Vector3[offsets.Count];
            int start = 0;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < offsets.Count; i++)
            {
                sum += offsets[i];
                while (times[i] - times[start] > windowSeconds)
                {
                    sum -= offsets[start];
                    start++;
                }
                result[i] = sum / (i - start + 1);
            }
            return result;
        }

        /// <summary>相对偏移相对起始点的最大偏离（诊断用）。</summary>
        private static float MaxExcursion(IReadOnlyList<Vector3> offsets)
        {
            float max = 0f;
            for (int i = 1; i < offsets.Count; i++)
            {
                Vector3 delta = offsets[i] - offsets[0];
                float magnitude = new Vector2(delta.x, delta.z).magnitude;
                if (magnitude > max) max = magnitude;
            }
            return max;
        }

        private static uint LatestRequestSequence(RecordingClient client)
        {
            uint latest = 0;
            foreach (var request in client.Requests.ToArray())
                if (request.Sequence > latest) latest = request.Sequence;
            return latest;
        }

        private static Vector3 ToVector3(Vec3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
