using System.Collections;
using Cool.SyncCore;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
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
    /// ③ 贴 NpcBody 持续挤压无往返抖动、不穿透、不超步行预算。
    /// </summary>
    public class PlayerControlInertiaTests
    {
        /// <summary>步进帧上限（防 Time.deltaTime 异常时死循环；墙钟由测试框架 300s 兜底）。</summary>
        private const int MaxFrames = 200000;

        private GameObject _player;
        private GameObject _ground;
        private GameObject _body;
        private Sys3CEntry _entry;
        private Sys3CController _cc;
        private UnityEngine.CharacterController _unityController;

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
            _player = Object.Instantiate(prefab, new Vector3(0f, 2f, 0f), Quaternion.identity);
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
            if (_player != null) Object.Destroy(_player);
            if (_body != null) Object.Destroy(_body);
            if (_ground != null) Object.Destroy(_ground);
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
            Vector3 jumpDir = new Vector3(preJumpVelocity.x, 0f, preJumpVelocity.z).normalized;
            Assert.Greater(jumpDir.z, 0.9f,
                "前置：起跳前应为 +Z 水平移动（velocity=" + preJumpVelocity + "）");

            _cc.RequestJump();
            _cc.Update(Forward());
            yield return null;
            Assert.AreEqual(1u, _cc.JumpSeq, "前置：起跳必须被消费");
            Assert.IsTrue(_cc.IsAirborne, "前置：起跳后应处于空中");

            // 起跳瞬间上麻痹：保留水平惯性，拒绝新输入与新起跳。
            _cc.SetStatusActionLock(true);
            uint seqAtLock = _cc.JumpSeq;
            Vector3 p0 = _player.transform.position;

            // 空中推进 0.3s（输入 Idle；麻痹下只允许起跳水平惯性）。
            elapsed = 0f;
            frames = 0;
            while (elapsed < 0.3f && frames < MaxFrames)
            {
                _cc.Update(Idle());
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
                _cc.RequestJump(); // 麻痹期间的新起跳请求必须被拒绝
            }

            Vector3 airDelta = _player.transform.position - p0;
            Vector3 airHorizontal = new Vector3(airDelta.x, 0f, airDelta.z);
            Assert.Greater(airHorizontal.magnitude, 0.2f,
                "空中麻痹必须保留起跳水平惯性（displacement=" + airHorizontal + "）");
            Assert.Greater(Vector3.Dot(airHorizontal.normalized, jumpDir), 0.99f,
                "惯性方向应与起跳水平速度一致（delta=" + airHorizontal + " dir=" + jumpDir + "）");

            float airSpeed = airHorizontal.magnitude / Mathf.Max(elapsed, 1e-4f);
            Assert.That(airSpeed, Is.InRange(CombatRules.WalkSpeed * 0.8f, CombatRules.WalkSpeed * 1.2f),
                "惯性速度应保持起跳水平速度（measured=" + airSpeed.ToString("F2")
                + " walk=" + CombatRules.WalkSpeed + "）");
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
            Debug.Log("[Inertia] postLand displacement=" + postLandHorizontal.magnitude.ToString("F4")
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
            Assert.LessOrEqual(blocked.z, 0.6f,
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
            Assert.LessOrEqual(moved, CombatRules.WalkSpeed * window + 0.01f,
                "挤压期间水平位移不得超过步行预算（moved=" + moved.ToString("F3")
                + "m walk=" + CombatRules.WalkSpeed + " window=" + window.ToString("F2") + "s）");
            Assert.LessOrEqual(maxZ, 0.6f,
                "不得穿透 NpcBody（maxZ=" + maxZ.ToString("F3") + "）");
            Debug.Log("[Inertia] push amplitude=" + amplitude.ToString("F4")
                      + " moved=" + moved.ToString("F4")
                      + " maxZ=" + maxZ.ToString("F3")
                      + " window=" + window.ToString("F2"));
        }
    }
}
