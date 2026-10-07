using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// HitFSM ↔ Character3C.controller 的 Hit 层契约（C1 的探测器）。
    ///
    /// C1 的成因是"代码进的受击态在 Hit 层没有任何表现"：HitFSM.TransitionTo(Knockback)
    /// 只 SetInteger(HitState, 2) + 抬层权重，而 Hit 层**没有** Knockback 状态、也没有任何
    /// 消费 HitState==2 的转移 → 层停在无 motion 的 Empty：受击动画不播，唯一能结束受击态的
    /// 回调（HitStateBehaviour.IsHitState 不含 Empty）也不触发。
    /// 这类"代码态 ↔ 资产态"错位可以廉价自动化，不该再交给人工目视。
    ///
    /// 手段：用**真实 Animator + 真实 HitFSM** 驱动，断言 Hit 层真的在播一个剪辑
    /// （GetCurrentAnimatorClipInfo 非空）。不问"资产长什么样"，只问"进去以后播不播"。
    /// </summary>
    public class HitAnimatorContractTests
    {
        private const string ControllerPath = "Assets/RpgDuo/Animator/Character3C.controller";

        /// <summary>
        /// P6 明确不承诺表现层的受击态（I1 backlog）。这些状态在 Hit 层既无对应状态、
        /// 也无入边转移——登记在此是为了让"没做"是显式决定，而不是无人发现的空白：
        /// 若某个已被覆盖的状态出现在这里（或反之），下面的覆盖登记测试会红。
        /// </summary>
        private static readonly HitState[] KnownBacklogStates =
        {
            HitState.Launched, // Hit 层无 Launched 状态（无任何调用方设置 LaunchForce，P6 内不可达）
            HitState.Down,     // Hit 层有 Down 状态但零入边转移
            HitState.GetUp,    // Hit 层有 GetUp 状态但零入边转移
            HitState.Death,    // 同上，且 AnimHashes.Death / IsDead 两个参数在 controller 里根本不存在（见 PENDING-HUMAN A5）
        };

        /// <summary>
        /// 已覆盖（断言必须播剪辑）的受击态 → 进入方式。
        /// 进入参数取本构建伤害路径实际产生的组合：
        /// 怪物攻击带击退（Slime 1.5 / TurtleShell 2.5）→ Knockback；盾破 StunDuration=1.5 → Dizzy；
        /// 盾牌格挡成功 → DefendHit；无击退无眩晕的平伤 → Hit。
        /// </summary>
        private static readonly Dictionary<HitState, Action<HitFSM>> Entrances =
            new Dictionary<HitState, Action<HitFSM>>
            {
                { HitState.Hit, fsm => fsm.EnterHit(new HitData
                    { Damage = 10f, HitDirection = Vector3.back }) },
                { HitState.Knockback, fsm => fsm.EnterHit(new HitData
                    { Damage = 10f, HitDirection = Vector3.back, KnockbackForce = 1.5f }) },
                { HitState.Dizzy, fsm => fsm.EnterHit(new HitData
                    { Damage = 10f, HitDirection = Vector3.back, StunDuration = 1.5f }) },
                { HitState.DefendHit, fsm => fsm.EnterDefendHit(new HitData
                    { Damage = 10f, HitDirection = Vector3.back }) },
            };

        private RuntimeAnimatorController _controller;

        [SetUp]
        public void SetUp()
        {
            // HitStateBehaviour 的回调是静态的，FSMManager 构造时会写它并跨测试泄漏。
            // 本测试只关心 Animator 播没播，先置空以免触发别处留下的悬挂委托。
            HitStateBehaviour.SetCallback(null);

            _controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            Assert.IsNotNull(_controller, "Animator Controller 缺失: " + ControllerPath);
        }

        /// <summary>当前受击态在 Hit 层播的剪辑（没播任何东西则为空数组）。</summary>
        private static AnimatorClipInfo[] HitLayerClips(Animator animator)
            => animator.GetCurrentAnimatorClipInfo(AnimHashes.HitLayerIndex);

        private static bool HasClip(AnimatorClipInfo[] clips)
        {
            foreach (var info in clips)
            {
                if (info.clip != null) return true;
            }
            return false;
        }

        /// <summary>
        /// 推进 Animator 直到 Hit 层开始播剪辑（或超时）。
        /// 不能用固定帧数：各转移耗时不同——`Empty --(trigger Hit)--> Hit` 是 0s，
        /// 而 AnyState → Dizzy/DefendHit 带 0.25s 过渡；同时 `Hit` 状态自身有 0.9 的
        /// 退出时间转移，推太久又会滑回 Empty。轮询在"刚开始播"的瞬间取到，两边都稳。
        /// </summary>
        private static AnimatorClipInfo[] PlayUntilClip(Animator animator, float timeout = 1f)
        {
            const float step = 1f / 60f;
            for (float t = 0f; t < timeout; t += step)
            {
                var clips = HitLayerClips(animator);
                if (HasClip(clips)) return clips;
                animator.Update(step);
            }
            return HitLayerClips(animator);
        }

        /// <summary>新建一套独立的 Animator + HitFSM（每个受击态一条干净时间线）。</summary>
        private (GameObject go, Animator animator, HitFSM fsm) CreateRig()
        {
            var go = new GameObject("HitAnimatorContractHost");
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = _controller;
            return (go, animator, new HitFSM(animator));
        }

        [Test]
        public void test_hit_layer_index_matches_animhashes()
        {
            // Arrange
            var (go, animator, _) = CreateRig();
            try
            {
                // Act & Assert（AnimHashes.HitLayerIndex 被 HitFSM/FSMManager 直接当索引用，
                // 层顺序一变，权重要么加在错层上、要么加不上）
                Assert.AreEqual("Hit", animator.GetLayerName(AnimHashes.HitLayerIndex),
                    "AnimHashes.HitLayerIndex 指向的层必须是 Hit");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_every_covered_hit_state_plays_a_clip_on_the_hit_layer()
        {
            // Arrange & Act & Assert（逐态独立驱动；失败信息里带状态名）
            foreach (var pair in Entrances)
            {
                var state = pair.Key;
                var (go, animator, fsm) = CreateRig();
                try
                {
                    pair.Value(fsm);
                    Assert.AreEqual(state, fsm.CurrentState,
                        "前置失败：该进入方式应让 HitFSM 停在 " + state);

                    var clips = PlayUntilClip(animator);

                    Assert.IsTrue(HasClip(clips),
                        "进入 " + state + " 后 Hit 层必须真的在播剪辑，实际播了 " + clips.Length + " 个。"
                        + "这就是 C1 的形态：状态切了但层停在没有 motion 的 Empty 上——"
                        + "受击动画一个都不播，且唯一能结束受击态的动画回调（HitStateBehaviour）也不会触发。");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void test_hit_durations_are_at_least_the_hit_clip_length()
        {
            // Arrange：实测 Hit 状态所用剪辑的真实时长（读 Animator 当前状态，不硬编码资产名）
            var (go, animator, fsm) = CreateRig();
            try
            {
                fsm.EnterHit(new HitData { Damage = 10f, HitDirection = Vector3.back });

                var clips = PlayUntilClip(animator);
                Assert.IsTrue(HasClip(clips),
                    "前提：Hit 层应正在播放剪辑（否则下面的时长断言无从测量）");

                AnimationClip hitClip = null;
                foreach (var info in clips)
                {
                    if (info.clip != null) { hitClip = info.clip; break; }
                }
                float clipLength = hitClip.length;
                Assert.Greater(clipLength, 0f, "剪辑时长应为正数");

                // Act
                var config = FSMConfig.Default;

                // Assert：计时器是**超时兜底**，不是截断器。
                // 两个时长此前从未被验证过（HitFSM.Update 无调用者 → 一直是惰性的）；
                // 一旦被驱动，低于剪辑时长就会把受击动画拦腰截断。
                Assert.GreaterOrEqual(config.HitDuration, clipLength,
                    "HitDuration(" + config.HitDuration + "s) 必须 ≥ Hit 剪辑时长(" + clipLength
                    + "s)——否则计时器会截断受击动画");
                Assert.GreaterOrEqual(config.KnockbackDuration, clipLength,
                    "KnockbackDuration(" + config.KnockbackDuration + "s) 必须 ≥ Hit 剪辑时长(" + clipLength
                    + "s)——Knockback 复用同一条 Hit 剪辑，低于它同样会截断");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_every_hit_state_is_covered_or_declared_backlog()
        {
            // Arrange
            var backlog = new HashSet<HitState>(KnownBacklogStates);

            // Act & Assert
            foreach (HitState state in Enum.GetValues(typeof(HitState)))
            {
                if (state == HitState.None) continue;

                bool covered = Entrances.ContainsKey(state);
                bool declared = backlog.Contains(state);

                Assert.IsTrue(covered || declared,
                    "HitState." + state + " 既没有被契约测试覆盖、也没有登记为 backlog——"
                    + "新增受击态时必须二选一（C1 正是这么漏网的：Knockback 谁都没管）");
                Assert.IsFalse(covered && declared,
                    "HitState." + state + " 同时出现在覆盖表与 backlog 里，二选一即可");
            }
        }
    }
}
