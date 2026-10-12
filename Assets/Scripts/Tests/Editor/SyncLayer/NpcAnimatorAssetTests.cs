using System.Linq;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>控制器资产断言：结构被改坏时立刻红（驱动侧只认参数名，名字错=动画静默不动）。</summary>
    public class NpcAnimatorAssetTests
    {
        private static readonly string[] Controllers =
        {
            "Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller",
            "Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller",
        };

        private static readonly string[] ExpectedStates =
        {
            "Idle", "Walk", "Run", "Attack01", "Attack02", "Hit", "Death"
        };

        [Test]
        public void Both_Controllers_Exist_With_The_Expected_States()
        {
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                Assert.IsNotNull(controller, path);

                Assert.AreEqual("Idle", controller!.layers[0].stateMachine.defaultState.name, $"{path} 默认状态");
                var names = controller.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
                CollectionAssert.AreEquivalent(ExpectedStates, names, path);
            }
        }

        [Test]
        public void Parameters_Match_What_The_Driver_Hashes()
        {
            // 驱动的参数名（Animator.StringToHash 常量）与资产必须成对；对不上 = 参数永远写不进去
            var expected = new[] { "MonsterState", "AttackVariant", "Attack" };
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var actual = controller.parameters.Select(p => p.name).ToArray();
                CollectionAssert.AreEquivalent(expected, actual, path);
            }
        }

        [Test]
        public void Hit_And_Death_Have_No_Outgoing_Transitions()
        {
            // 受击/死亡不放出边：受击片段若短于服务端硬直窗口，AnyState 会顺着仍成立的
            // MonsterState==4 逐帧重播受击；死亡本就该定格。
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var states = controller.layers[0].stateMachine.states;
                foreach (var name in new[] { "Hit", "Death" })
                {
                    var st = states.First(s => s.state.name == name).state;
                    Assert.AreEqual(0, st.transitions.Length, $"{path}:{name}");
                }
            }
        }

        [Test]
        public void Attack_States_Return_Via_Exit_Time()
        {
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var states = controller.layers[0].stateMachine.states;
                foreach (var name in new[] { "Attack01", "Attack02" })
                {
                    var st = states.First(s => s.state.name == name).state;
                    Assert.AreEqual(1, st.transitions.Length, $"{path}:{name} 应有一条 exit-time 回 Idle 的出边");
                    Assert.IsTrue(st.transitions[0].hasExitTime, $"{path}:{name}");
                    Assert.AreEqual("Idle", st.transitions[0].destinationState.name, $"{path}:{name}");
                }
            }
        }

        [Test]
        public void States_Carry_The_Clips_From_The_State_Table()
        {
            // 状态表（任务简报）：状态名 → 片段名。片段没挂上 = 状态是空壳，动画不动。
            var expected = new[]
            {
                new[] { "Idle_Battle", "WalkForwardBattle", "RunForwardBattle", "Attack01", "Attack02", "GetHit", "Die" },
                new[] { "idle", "walk", "run", "attack1", "attack2", "hit1", "death1" },   // spider：FBX 内嵌片段
            };

            for (int i = 0; i < Controllers.Length; i++)
            {
                var path = Controllers[i];
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var states = controller.layers[0].stateMachine.states;

                for (int s = 0; s < ExpectedStates.Length; s++)
                {
                    var st = states.First(x => x.state.name == ExpectedStates[s]).state;
                    Assert.IsNotNull(st.motion, $"{path}:{ExpectedStates[s]} 没挂片段");
                    Assert.AreEqual(expected[i][s], st.motion!.name, $"{path}:{ExpectedStates[s]} 片段不对");
                }
            }
        }

        [Test]
        public void AnyState_Transitions_Match_The_Driver_Contract()
        {
            // 三条硬约束（AnyState 每帧评估，写错就静默跑偏）：
            //   · 不许重入（条件持续为真时逐帧重播自己）
            //   · 状态值/变体阈值必须与驱动写的常量一致
            //   · 两条攻击必须挂 Trigger 条件——只有变体条件 = 永远不会播
            var locomotion = new (string Dest, float Threshold)[]
            {
                ("Idle", 0f), ("Walk", 1f), ("Run", 2f), ("Hit", 4f), ("Death", 5f),
            };

            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var anyState = controller.layers[0].stateMachine.anyStateTransitions;

                Assert.AreEqual(7, anyState.Length, $"{path} AnyState 转移数");
                foreach (var t in anyState)
                    Assert.IsFalse(t.canTransitionToSelf, $"{path}:ANY->{t.destinationState.name} 不许重入");

                foreach (var (dest, threshold) in locomotion)
                {
                    var t = anyState.First(x => x.destinationState.name == dest);
                    var c = t.conditions.First(x => x.parameter == "MonsterState");
                    Assert.AreEqual(AnimatorConditionMode.Equals, c.mode, $"{path}:ANY->{dest}");
                    Assert.AreEqual(threshold, c.threshold, 0.001f, $"{path}:ANY->{dest}");
                }

                foreach (var (dest, variant) in new[] { ("Attack01", 0f), ("Attack02", 1f) })
                {
                    var t = anyState.First(x => x.destinationState.name == dest);
                    var c = t.conditions.First(x => x.parameter == "AttackVariant");
                    Assert.AreEqual(AnimatorConditionMode.Equals, c.mode, $"{path}:ANY->{dest}");
                    Assert.AreEqual(variant, c.threshold, 0.001f, $"{path}:ANY->{dest}");
                    Assert.IsTrue(t.conditions.Any(x => x.parameter == "Attack" && x.mode == AnimatorConditionMode.If),
                        $"{path}:ANY->{dest} 缺 Attack Trigger 条件");
                }
            }
        }

        /// <summary>
        /// 推帧直到过渡落定。实测：AnyState 过渡在参数写入后的**第一次** Update 里才开始计时，
        /// 且落定前 <c>GetCurrentAnimatorStateInfo</c> 报的仍是**源**状态（目标状态在
        /// <c>GetNextAnimatorStateInfo</c> 里）—— 单次 <c>Update(0.2f)</c> 会把"已经进了新状态"
        /// 读成"没进"。读法与本目录既有用例（<c>NpcEntityAnimatorTests.CurrentState</c>）一致，
        /// 上限 20 帧 =1s 防呆。
        /// </summary>
        private static void Settle(Animator animator)
        {
            int guard = 0;
            do
            {
                animator.Update(0.05f);
            } while (++guard < 20 && animator.IsInTransition(0));
        }

        [Test]
        public void Driver_And_Real_Controller_Agree_On_Parameter_Names()
        {
            var go = new GameObject("DriverOnRealController");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller");
                var driver = go.AddComponent<NpcEntityAnimator>();   // 必须在设好控制器之后加：Awake 会缓存 Animator
                driver.ResolveAnimator();                            // EditMode 下 AddComponent 不触发 Awake

                driver.ApplyState((byte)NpcAnimationState.Run, false);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"),
                    "写 MonsterState=2 之后必须真的进 Run —— 参数名对不上就会停在上一个状态");

                driver.PlayAttack(variant: 0);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack01"),
                    "Trigger 名对不上就永远不播攻击");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Two_Swings_In_A_Row_Both_Play()
        {
            var go = new GameObject("TwoSwings");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller");
                var driver = go.AddComponent<NpcEntityAnimator>();
                driver.ResolveAnimator();   // EditMode 下 AddComponent 不触发 Awake

                driver.PlayAttack(variant: 0);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack01"));

                animator.Update(1.0f);      // 推过片段（0.667s）+ exit-time 回 Idle
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "攻击态必须已退出");

                driver.PlayAttack(variant: 1);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack02"),
                    "第二刀必须能播 —— 攻击态若没在冷却内退出，canTransitionToSelf=false 会把它静默吞掉");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
