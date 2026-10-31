using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// `RemoteEntityAnimator` ↔ 真实 `Character3C.controller` 的契约测试。
    ///
    /// 之前的仲裁器测试用的是空壳控制器（只有层和参数，没有状态/转移），因此钉不住
    /// "写进去的参数在真实控制器上到底进不进状态"。两类真缺陷正是这么漏掉的：
    ///   · C1：Hit 层从 Empty 进 Hit **只认 Trigger `Hit`**，`HitState == 1` 没有 AnyState
    ///     转移——只写整数会停在无 motion 的 Empty，远程普通受击不播动画；
    ///   · I1：GetUp 兜底 0.5s 把真实 0.8334s 的起身剪辑截掉 40%。
    ///
    /// 手段与 `HitAnimatorContractTests` 同款：加载真实控制器、驱动真实 Animator，
    /// 断言 Hit 层的**当前状态**（shortNameHash），不硬编码剪辑资产名。
    /// 旧实现上首条用例必须红（PlayHit 后停在 Empty）。
    /// </summary>
    public class RemoteEntityAnimatorContractTests
    {
        private const string ControllerPath = "Assets/RpgDuo/Animator/Character3C.controller";
        private const float Step = 1f / 60f;

        private GameObject _go = null!;
        private Animator _animator = null!;
        private RemoteEntityAnimator _driver = null!;

        [SetUp]
        public void SetUp()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, "Animator Controller 缺失: " + ControllerPath);

            _go = new GameObject("RemoteAnimatorContractHost");
            _animator = _go.AddComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _driver = _go.AddComponent<RemoteEntityAnimator>();
            _driver.ResolveAnimator();   // EditMode 下 AddComponent 不跑生命周期
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        /// <summary>推进 Animator 直到 Hit 层稳定停在目标状态（不在混合中）。</summary>
        private void AdvanceTo(string stateName, float timeout = 2f)
        {
            int expected = Animator.StringToHash(stateName);
            for (float t = 0f; t < timeout; t += Step)
            {
                _animator.Update(Step);
                if (!_animator.IsInTransition(AnimHashes.HitLayerIndex)
                    && _animator.GetCurrentAnimatorStateInfo(AnimHashes.HitLayerIndex).shortNameHash == expected)
                    return;
            }

            Assert.Fail("Hit 层未在 " + timeout + "s 内进入 " + stateName
                + "，当前 " + _animator.GetCurrentAnimatorStateInfo(AnimHashes.HitLayerIndex).shortNameHash);
        }

        private static int CurrentHitStateHash(Animator animator)
            => animator.GetCurrentAnimatorStateInfo(AnimHashes.HitLayerIndex).shortNameHash;

        /// <summary>当前 Hit 层状态的剪辑时长（没有剪辑则失败——前置条件不成立）。</summary>
        private float CurrentHitClipLength()
        {
            var clips = _animator.GetCurrentAnimatorClipInfo(AnimHashes.HitLayerIndex);
            foreach (var info in clips)
                if (info.clip != null) return info.clip.length;
            Assert.Fail("Hit 层当前状态没有剪辑，无法测量时长");
            return 0f;
        }

        [Test]
        public void test_normal_hit_enters_the_real_hit_state_via_trigger()
        {
            // Act：真实控制器里 Empty → Hit 是 0s 过渡，只认 Trigger `Hit`
            _driver.PlayHit(blocked: false, duration: 0.5f);
            _animator.Update(Step);

            // Assert
            Assert.AreEqual(Animator.StringToHash("Hit"), CurrentHitStateHash(_animator),
                "普通受击必须靠 Trigger `Hit` 真正进入 Hit 状态；只写 HitState==1 会停在 Empty（C1）");
        }

        [Test]
        public void test_blocked_hit_enters_defend_hit_state()
        {
            // Arrange：先进入普通受击，确保状态机已初始化并离开 Empty
            _driver.PlayHit(blocked: false, duration: 0.5f);
            _animator.Update(Step);
            Assert.AreEqual(Animator.StringToHash("Hit"), CurrentHitStateHash(_animator),
                "前置：应先进入 Hit");

            // Act：格挡受击走 AnyState 的 HitState==8 转移
            _driver.PlayHit(blocked: true, duration: 0.5f);
            AdvanceTo("DefendHit");

            // Assert
            Assert.AreEqual((int)HitState.DefendHit, _driver.LastHitState);
            Assert.AreEqual(Animator.StringToHash("DefendHit"), CurrentHitStateHash(_animator));
        }

        [Test]
        public void test_death_down_and_get_up_walk_the_real_states()
        {
            // Death（AnyState HitState==7）
            _driver.ApplySnapshotState(dead: true);
            AdvanceTo("Death");
            Assert.AreEqual((int)HitState.Death, _driver.LastHitState);

            // Down（AnyState HitState==5）：Tick 越过 Death 阶段
            _driver.Tick(0.71f);
            Assert.AreEqual((int)HitState.Down, _driver.LastHitState,
                "Death 之后必须进入 Down（躺卧）");
            AdvanceTo("Down");

            // GetUp（AnyState HitState==6）：复活快照
            _driver.ApplySnapshotState(dead: false);
            AdvanceTo("GetUp");
            Assert.AreEqual((int)HitState.GetUp, _driver.LastHitState);
        }

        [Test]
        public void test_get_up_is_not_truncated_before_the_real_clip_length()
        {
            // Arrange：推进到 GetUp
            _driver.ApplySnapshotState(dead: true);
            AdvanceTo("Death");
            _driver.Tick(0.71f);
            AdvanceTo("Down");
            _driver.ApplySnapshotState(dead: false);
            AdvanceTo("GetUp");

            float clipLength = CurrentHitClipLength();
            Assert.Greater(clipLength, 0.8f, "GetUp 剪辑应约 0.8334s（与本地 FSMConfig 注释一致）");

            // Act：0.84s 仍在真实剪辑时长（0.8334s）之后、校准后的兜底（≈0.9s）之内
            _driver.Tick(0.84f);

            // Assert：旧实现 GetUpDuration=0.5s 会在这里清掉（I1）
            Assert.AreEqual((int)HitState.GetUp, _driver.LastHitState,
                "GetUp 计时器必须 ≥ 真实剪辑时长（含 2 帧尾量）——旧 0.5s 会把起身截掉 40%");

            _driver.Tick(0.1f);
            Assert.AreEqual((int)HitState.None, _driver.LastHitState,
                "超过校准时长后才交还 Base 层");
        }

        [Test]
        public void test_death_falling_phase_calibrates_to_the_real_clip_length()
        {
            // Arrange
            _driver.ApplySnapshotState(dead: true);
            AdvanceTo("Death");

            float clipLength = CurrentHitClipLength();
            Assert.Greater(clipLength, 0f, "Death 剪辑时长应为正数");
            Assert.Less(clipLength + 0.1f, 0.7f,
                "前提：真实 Death 剪辑（" + clipLength + "s）必须短于 0.7s 兜底，本用例才能区分校准与兜底");

            // Act：越过真实剪辑长度、但仍在旧兜底 0.7s 之内
            _driver.Tick(clipLength + 0.1f);

            // Assert：校准生效才会换到 Down；只靠 0.7s 兜底会停在 Death
            Assert.AreEqual((int)HitState.Down, _driver.LastHitState,
                "Death 阶段必须按真实剪辑长度校准（" + clipLength + "s），而不是固定用 0.7s");
        }
    }
}
