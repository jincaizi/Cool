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
            // 驱动的参数名（Animator.StringToHash 常量）与资产必须成对；对不上 = 参数永远写不进去。
            // **类型也要对**：`Attack` 若被写成 Bool，`SetTrigger` 一样静默不动（名字对、类型错也是死的）。
            var expected = new (string Name, AnimatorControllerParameterType Type)[]
            {
                ("MonsterState", AnimatorControllerParameterType.Int),
                ("AttackVariant", AnimatorControllerParameterType.Int),
                ("Attack", AnimatorControllerParameterType.Trigger),
            };

            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var actual = controller.parameters.Select(p => p.name).ToArray();
                CollectionAssert.AreEquivalent(expected.Select(e => e.Name).ToArray(), actual, path);

                foreach (var (name, type) in expected)
                    Assert.AreEqual(type, controller.parameters.First(p => p.name == name).type, $"{path}:{name} 类型");
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
        public void Locomotion_States_Bind_Looping_Clips()
        {
            // 循环态绑了非循环片段 = 播一遍就定格在末帧（Boss 站桩时会僵住）。
            // 只钉"循环态必须循环"这一半：**不**断言一次性态必须不循环 —— DogKnight 的
            // GetHit.anim 本就是循环的，而"受击态会不会滞留"由**服务端**兜底（硬直窗口刚过期
            // 那一 tick 写回状态，见 MonsterModule.Tick 与 NpcAnimator_DogKnight.controller.md；
            // 早先"窗口 0.33s < 片段 0.833s ⇒ 风险不可达"的说法已被终审 I1 推翻）。
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var states = controller.layers[0].stateMachine.states;

                foreach (var name in new[] { "Idle", "Walk", "Run" })
                {
                    var st = states.First(s => s.state.name == name).state;
                    Assert.IsNotNull(st.motion, $"{path}:{name} 没挂片段");
                    var clip = (AnimationClip)st.motion!;
                    Assert.IsTrue(clip.isLooping,
                        $"{path}:{name} 绑的是 {clip.name}，必须开循环（否则播一遍就定格）");
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
            // 外加时长/exit-time：0.15s（locomotion）/ 0.1s（Hit、Death、两条攻击）与 hasExitTime=false 是逐字给定的硬参数，
            // 被改成 0.5 或打开 exit-time 会让"参数一写就切"变成"等源状态走完"，且全套断言照样绿——所以这里也守住。
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
                {
                    Assert.IsFalse(t.canTransitionToSelf, $"{path}:ANY->{t.destinationState.name} 不许重入");
                    Assert.IsFalse(t.hasExitTime, $"{path}:ANY->{t.destinationState.name} 不该吃 exit time");
                    bool isLocomotion = t.destinationState.name == "Idle" || t.destinationState.name == "Walk"
                        || t.destinationState.name == "Run";
                    Assert.AreEqual(isLocomotion ? 0.15f : 0.1f, t.duration, 0.001f,
                        $"{path}:ANY->{t.destinationState.name} 过渡时长");
                }

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

        [Test]
        public void Monster_Prefabs_Carry_The_Driver_And_The_Right_Controller()
        {
            var cases = new (string Path, string Controller)[]
            {
                ("Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab", "NpcAnimator_DogKnight"),
                ("Assets/Monstor/fantasySpider/SpiderMonster.prefab", "NpcAnimator_Spider"),
            };

            foreach (var (path, controllerName) in cases)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);

                // 预制体保持纯美术：驱动由 registry 实例化后 AddComponent（普通类不能用 GetComponent）
                Assert.IsNull(prefab!.GetComponentInChildren<NpcEntityAnimator>(true),
                    $"{path} 不该挂驱动组件——它在实例化后由 registry 添加");

                var animator = prefab.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(animator, $"{path} 缺 Animator");
                Assert.IsNotNull(animator!.runtimeAnimatorController, $"{path} 的 Animator 没有控制器");
                Assert.IsTrue(animator.runtimeAnimatorController!.name.Contains(controllerName), $"{path} 控制器不对");
                Assert.IsFalse(animator.applyRootMotion, $"{path} 必须关根运动（位置由服务端权威）");
            }
        }

        [Test]
        public void Spider_Boss_Is_Twice_Its_Imported_Size()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Monstor/fantasySpider/SpiderMonster.prefab")!;
            Assert.AreEqual(2f, prefab.transform.localScale.x, 0.001f);
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
            // 两只怪都要守：asset 注记（*.controller.md）里那条不变量对两边都成立
            foreach (var path in Controllers) Two_Swings_On(path);
        }

        private static void Two_Swings_On(string controllerPath)
        {
            var go = new GameObject("TwoSwings");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
                var driver = go.AddComponent<NpcEntityAnimator>();
                driver.ResolveAnimator();   // EditMode 下 AddComponent 不触发 Awake

                // 推进量从**该控制器**攻击片段的实测长度推，不写死数字：
                // exit-time 在 0.95 处起退、再加 0.1s 混合 —— 死数字在换片段时会假红（片段只要仍短于冷却，不变量就成立）。
                float advance = AttackClipLength(controllerPath) + 0.3f;

                driver.PlayAttack(variant: 0);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack01"), controllerPath);

                animator.Update(advance);   // 推过片段 + exit-time 回 Idle
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), controllerPath + " 攻击态必须已退出");

                driver.PlayAttack(variant: 1);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack02"),
                    controllerPath + " 第二刀必须能播 —— 攻击态若没在冷却内退出，canTransitionToSelf=false 会把它静默吞掉");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static float AttackClipLength(string controllerPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)!;
            var st = controller.layers[0].stateMachine.states.First(s => s.state.name == "Attack01").state;
            Assert.IsNotNull(st.motion, $"{controllerPath}:Attack01 没挂片段");
            return ((AnimationClip)st.motion!).length;
        }

        /// <summary>
        /// DuoPolyart 两只怪（服务端模板 1 Slime / 2 TurtleShell）。它们的控制器原先是美术包词表
        /// （AIState/Speed/Hit/…）—— 缺 <c>MonsterState</c> 于是被 <c>NpcEntityAnimator.ResolveAnimator</c>
        /// 整只降级成"只渲染、不动画"。本次给它们换上驱动词表：**参数名/层/状态语义必须与
        /// NpcAnimator_DogKnight 逐条一致**（客户端逻辑已按那套语义写死），否则 <c>SetInteger</c>
        /// 静默失败，动画不动且不报错。
        /// 元组 =（控制器路径，宿主预制体路径，状态表顺序的片段名 —— 见 <see cref="ExpectedStates"/>）。
        /// </summary>
        private static readonly (string Controller, string Prefab, string[] Clips)[] NpcWordControllers =
        {
            ("Assets/Monstor/DuoPolyart/Animators/NpcAnimator_Slime.controller",
             "Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab",
             new[] { "IdleBattle", "WalkFWD", "RunFWD", "Attack01", "Attack02", "GetHit", "Die" }),
            ("Assets/Monstor/DuoPolyart/Animators/NpcAnimator_TurtleShell.controller",
             "Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/TurtleShellPBR.prefab",
             new[] { "IdleBattle", "WalkFWD", "RunFWD", "Attack01", "Attack02", "GetHit", "Die" }),
        };

        /// <summary>
        /// 两只 DuoPolyart 怪：参数、层/状态名与转移语义照 DogKnight；预制体已改线；且**真驱动一遍**
        /// （参数写进去必须真的落进对应状态 —— 光是资产长得对不算数）。
        /// </summary>
        [Test]
        public void Slime_And_TurtleShell_Controllers_Speak_The_Driver_Vocabulary()
        {
            var driverParameters = new (string Name, AnimatorControllerParameterType Type)[]
            {
                ("MonsterState", AnimatorControllerParameterType.Int),
                ("AttackVariant", AnimatorControllerParameterType.Int),
                ("Attack", AnimatorControllerParameterType.Trigger),
            };
            var stateValues = new (string Dest, int Value)[]
            {
                ("Idle", 0), ("Walk", 1), ("Run", 2), ("Hit", 4), ("Death", 5),
            };

            foreach (var (path, prefabPath, clips) in NpcWordControllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                Assert.IsNotNull(controller, path);

                // 参数：名字与类型逐字（驱动用 Animator.StringToHash 常量写参数；改名不报错、只是动画不动）
                Assert.AreEqual(3, controller!.parameters.Length, path + " 参数数");
                foreach (var (name, type) in driverParameters)
                {
                    var p = controller.parameters.FirstOrDefault(x => x.name == name);
                    Assert.IsNotNull(p, path + " 缺参数 " + name);
                    Assert.AreEqual(type, p!.type, path + ":" + name + " 类型");
                }

                // 单层 Base Layer、默认 Idle、7 个状态名与 DogKnight 一致
                Assert.AreEqual(1, controller.layers.Length, path + " 层数");
                Assert.AreEqual("Base Layer", controller.layers[0].name, path);
                var sm = controller.layers[0].stateMachine;
                Assert.AreEqual("Idle", sm.defaultState.name, path + " 默认状态");
                CollectionAssert.AreEquivalent(ExpectedStates, sm.states.Select(s => s.state.name).ToArray(), path);

                // 状态 → 片段（每个 FBX 单片段）；locomotion 三个态必须绑循环片段
                for (int i = 0; i < ExpectedStates.Length; i++)
                {
                    var st = sm.states.First(s => s.state.name == ExpectedStates[i]).state;
                    Assert.IsNotNull(st.motion, path + ":" + ExpectedStates[i] + " 没挂片段");
                    Assert.AreEqual(clips[i], st.motion!.name, path + ":" + ExpectedStates[i] + " 片段不对");
                }
                foreach (var name in new[] { "Idle", "Walk", "Run" })
                {
                    var clip = (AnimationClip)sm.states.First(s => s.state.name == name).state.motion!;
                    Assert.IsTrue(clip.isLooping, path + ":" + name + " 必须循环（否则播一遍就定格）");
                }

                // AnyState 语义：MonsterState 0/1/2/4/5 → Idle/Walk/Run/Hit/Death；两条攻击 = Trigger + 变体
                Assert.AreEqual(7, sm.anyStateTransitions.Length, path + " AnyState 转移数");
                foreach (var t in sm.anyStateTransitions)
                {
                    string to = path + ":ANY->" + t.destinationState.name;
                    Assert.IsFalse(t.canTransitionToSelf, to + " 不许重入");
                    Assert.IsFalse(t.hasExitTime, to + " 不该吃 exit time");
                    bool locomotion = t.destinationState.name == "Idle"
                        || t.destinationState.name == "Walk" || t.destinationState.name == "Run";
                    Assert.AreEqual(locomotion ? 0.15f : 0.1f, t.duration, 0.001f, to + " 过渡时长");
                }
                foreach (var (dest, value) in stateValues)
                {
                    var c = sm.anyStateTransitions.First(t => t.destinationState.name == dest)
                        .conditions.First(x => x.parameter == "MonsterState");
                    Assert.AreEqual(AnimatorConditionMode.Equals, c.mode, path + ":ANY->" + dest);
                    Assert.AreEqual(value, c.threshold, 0.001f, path + ":ANY->" + dest);
                }
                foreach (var (dest, variant) in new[] { ("Attack01", 0), ("Attack02", 1) })
                {
                    var t = sm.anyStateTransitions.First(x => x.destinationState.name == dest);
                    Assert.IsTrue(t.conditions.Any(x => x.parameter == "Attack" && x.mode == AnimatorConditionMode.If),
                        path + ":ANY->" + dest + " 缺 Attack Trigger 条件（只有变体条件 = 永远不播）");
                    var c = t.conditions.First(x => x.parameter == "AttackVariant");
                    Assert.AreEqual(AnimatorConditionMode.Equals, c.mode, path + ":ANY->" + dest);
                    Assert.AreEqual(variant, c.threshold, 0.001f, path + ":ANY->" + dest);
                }

                // 受击/死亡不放出边；两条攻击靠 exit-time 回 Idle（同 DogKnight 的两条硬约束）
                foreach (var name in new[] { "Hit", "Death" })
                {
                    Assert.AreEqual(0, sm.states.First(s => s.state.name == name).state.transitions.Length,
                        path + ":" + name + " 不该有出边");
                }
                foreach (var name in new[] { "Attack01", "Attack02" })
                {
                    var t = sm.states.First(s => s.state.name == name).state.transitions;
                    Assert.AreEqual(1, t.Length, path + ":" + name + " 应有一条 exit-time 回 Idle 的出边");
                    Assert.IsTrue(t[0].hasExitTime, path + ":" + name);
                    Assert.AreEqual("Idle", t[0].destinationState.name, path + ":" + name);
                }

                // 预制体改线：Animator 在子物体上，registry 实例化后靠 GetComponentInChildren 找它
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(prefab, prefabPath);
                var prefabAnimator = prefab!.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(prefabAnimator, prefabPath + " 缺 Animator");
                Assert.IsNotNull(prefabAnimator!.runtimeAnimatorController, prefabPath + " 的 Animator 没有控制器");
                Assert.AreEqual(controller.name, prefabAnimator.runtimeAnimatorController!.name, prefabPath + " 控制器不对");
                Assert.IsFalse(prefabAnimator.applyRootMotion, prefabPath + " 必须关根运动（位置由服务端权威）");

                // 真驱动一遍（Hit/Death 没有出边，各用一台干净 Rig）
                DriveTo(path, "Walk", d => d.ApplyState((byte)NpcAnimationState.Walk, false));
                DriveTo(path, "Run", d => d.ApplyState((byte)NpcAnimationState.Run, false));
                DriveTo(path, "Attack01", d => d.PlayAttack(0));
                DriveTo(path, "Attack02", d => d.PlayAttack(1));
                DriveTo(path, "Hit", d => d.ApplyState((byte)NpcAnimationState.Hit, false));
                DriveTo(path, "Death", d => d.ApplyState((byte)NpcAnimationState.Idle, true));   // Dead 位优先
            }
        }

        /// <summary>一台干净 Rig 上驱动一次，断言真的落进目标状态。</summary>
        private static void DriveTo(string controllerPath, string expectedState, System.Action<NpcEntityAnimator> drive)
        {
            var go = new GameObject("NpcWordDrive");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
                var driver = go.AddComponent<NpcEntityAnimator>();
                driver.ResolveAnimator();   // EditMode 下 AddComponent 不触发 Awake

                drive(driver);
                Settle(animator);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(expectedState),
                    controllerPath + " 驱动后应进 " + expectedState + "（参数名/类型对不上就会停在上一个状态）");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
