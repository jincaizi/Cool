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
            Assert.AreEqual((int)HitState.Down, _animator.GetInteger("HitState"));

            // 模拟 FullRoot 重新启用：Animator 状态与层权重被重置
            _animator.SetInteger("HitState", 0);
            _driver.ResetLayerWeights();
            Assert.AreEqual(0, _animator.GetInteger("HitState"));

            _driver.Reapply();
            Assert.AreEqual((int)HitState.Down, _animator.GetInteger("HitState"));
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f);
        }

        [Test]
        public void Reapply_Replays_Defend_After_A_Tier_Upgrade()
        {
            _driver.SetDefending(true);
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.DefendLayerIndex), 0.001f,
                "前置：格挡姿态已写出");

            // 模拟 FullRoot 重新启用：Animator 参数与层权重被重置
            _animator.SetBool("IsDefending", false);
            _driver.ResetLayerWeights();
            Assert.IsFalse(_animator.GetBool("IsDefending"));
            Assert.AreEqual(0f, _animator.GetLayerWeight(AnimHashes.DefendLayerIndex), 0.001f);

            _driver.Reapply();
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.DefendLayerIndex), 0.001f);
            Assert.IsTrue(_animator.GetBool("IsDefending"));
        }

        [Test]
        public void Death_And_Down_Ignore_PlayHit_And_PlayAttack()
        {
            _driver.ApplySnapshotState(dead: true);

            _driver.PlayHit(blocked: false, duration: 0.5f);
            _driver.PlayAttack("Attack", 0.5f);
            Assert.AreEqual((int)HitState.Death, _animator.GetInteger("HitState"),
                "Death 霸体：受击/攻击都不得改写表现");
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f);
            Assert.AreEqual(0f, _animator.GetLayerWeight(AnimHashes.AttackLayerIndex), 0.001f);

            _driver.Tick(0.71f);   // Falling → Down
            Assert.AreEqual((int)HitState.Down, _animator.GetInteger("HitState"));

            _driver.PlayHit(blocked: true, duration: 0.5f);
            _driver.PlayAttack("Attack", 0.5f);
            Assert.AreEqual((int)HitState.Down, _animator.GetInteger("HitState"),
                "Down 霸体：受击/攻击都不得改写表现");
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f);
            Assert.AreEqual(0f, _animator.GetLayerWeight(AnimHashes.AttackLayerIndex), 0.001f);
        }

        [Test]
        public void GetUp_Is_Interrupted_By_PlayAttack()
        {
            _driver.ApplySnapshotState(dead: true);
            _driver.Tick(0.71f);
            _driver.ApplySnapshotState(dead: false);
            Assert.AreEqual((int)HitState.GetUp, _animator.GetInteger("HitState"),
                "前置：复活先进入 GetUp");

            _driver.PlayAttack("Attack", 0.5f);
            // `HitState` 整型参数只在 Hit 层有权重时被控制器消费；攻击接管后 Hit 层已被压到 0，
            // 旧整型值保留是既有设计（不额外写参数）。实际表现看层权重，仲裁结果看 LastHitState。
            Assert.AreNotEqual((int)HitState.GetUp, _driver.LastHitState,
                "GetUp 非霸体：攻击必须能打断起身（仲裁结果不再是 GetUp）");
            Assert.AreEqual(1f, _animator.GetLayerWeight(AnimHashes.AttackLayerIndex), 0.001f,
                "攻击层必须接管表现");
            Assert.AreEqual(0f, _animator.GetLayerWeight(AnimHashes.HitLayerIndex), 0.001f,
                "GetUp 的 Hit 层必须让位给攻击");
        }

        [Test]
        public void Root_Motion_Is_Disabled()
        {
            Assert.IsFalse(_animator.applyRootMotion);
        }
    }
}
