using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>驱动侧的参数写入规则（spec §5.2 四条规则）。</summary>
    public class NpcEntityAnimatorTests
    {
        [Test]
        public void Map_State_Covers_The_Protocol_Enum()
        {
            Assert.AreEqual(NpcEntityAnimator.StateIdle, NpcEntityAnimator.MapState((byte)NpcAnimationState.Idle, false));
            Assert.AreEqual(NpcEntityAnimator.StateWalk, NpcEntityAnimator.MapState((byte)NpcAnimationState.Walk, false));
            Assert.AreEqual(NpcEntityAnimator.StateRun, NpcEntityAnimator.MapState((byte)NpcAnimationState.Run, false));
            Assert.AreEqual(NpcEntityAnimator.StateAttacking, NpcEntityAnimator.MapState((byte)NpcAnimationState.Attack, false));
            Assert.AreEqual(NpcEntityAnimator.StateHit, NpcEntityAnimator.MapState((byte)NpcAnimationState.Hit, false));
            Assert.AreEqual(NpcEntityAnimator.StateDeath, NpcEntityAnimator.MapState((byte)NpcAnimationState.Death, false));

            // 忽略值：保持当前（返回 -1 表示"不写"）
            Assert.AreEqual(-1, NpcEntityAnimator.MapState((byte)NpcAnimationState.Skill, false));
            Assert.AreEqual(-1, NpcEntityAnimator.MapState((byte)NpcAnimationState.Down, false));
            Assert.AreEqual(-1, NpcEntityAnimator.MapState((byte)NpcAnimationState.GetUp, false));

            // Dead 位优先于一切
            Assert.AreEqual(NpcEntityAnimator.StateDeath, NpcEntityAnimator.MapState((byte)NpcAnimationState.Run, dead: true));
        }

        private GameObject _go = null!;
        private Animator _animator = null!;
        private NpcEntityAnimator _driver = null!;
        private const string TempControllerPath = "Assets/Tests_TempNpc.controller";

        [SetUp]
        public void SetUp()
        {
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(TempControllerPath);
            controller.AddParameter("MonsterState", AnimatorControllerParameterType.Int);
            controller.AddParameter("AttackVariant", AnimatorControllerParameterType.Int);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var run = sm.AddState("Run");
            var attack = sm.AddState("Attack01");
            sm.defaultState = idle;

            var toRun = sm.AddAnyStateTransition(run);
            toRun.canTransitionToSelf = false;
            toRun.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, 2f, "MonsterState");

            var toAttack = sm.AddAnyStateTransition(attack);
            toAttack.canTransitionToSelf = false;
            toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Attack");

            var backToIdle = attack.AddTransition(idle);      // exit-time 回 Idle
            backToIdle.hasExitTime = true;
            backToIdle.exitTime = 1f;
            backToIdle.duration = 0f;

            _go = new GameObject("NpcDriverTest");
            _animator = _go.AddComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _driver = _go.AddComponent<NpcEntityAnimator>();

            // EditMode 下 AddComponent **不会**触发生命周期（组件没挂 [ExecuteAlways]），而驱动在
            // Awake 里解析 Animator —— 不显式补这一下，驱动永远拿不到 Animator、一个参数都不写
            // （三条 Animator 用例会集体读成 Idle，看起来像"驱动完全没实现"）。
            _driver.ResolveAnimator();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            UnityEditor.AssetDatabase.DeleteAsset(TempControllerPath);
        }

        private string CurrentState()
        {
            // AnyState 转移的默认混合时长是 0.25s，而混合期间 GetCurrentAnimatorStateInfo 读到的仍是
            // **源**状态（目标状态只在 GetNextAnimatorStateInfo 里）。只推一帧会把"转移已经发生"读成
            // "没发生"——先把参数交给状态机评估，再推到混合结束（上限 20 帧=`1s`，防呆）后读。
            int guard = 0;
            do
            {
                _animator.Update(0.05f);
            } while (++guard < 20 && _animator.IsInTransition(0));

            var info = _animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Run")) return "Run";
            if (info.IsName("Attack01")) return "Attack01";
            if (info.IsName("Idle")) return "Idle";
            return "<unknown>";
        }

        [Test]
        public void Timeline_State_Reaches_The_Animator()
        {
            _driver.ApplyState((byte)NpcAnimationState.Run, false);
            Assert.AreEqual("Run", CurrentState());
        }

        [Test]
        public void A_Stale_Locomotion_Sample_Does_Not_Interrupt_The_Swing()
        {
            _driver.ApplyState((byte)NpcAnimationState.Run, false);
            _driver.PlayAttack(variant: 0);
            // 竞态：施法广播与状态快照同 tick 发出，到达顺序不保证——
            // 广播先到时会先起手，紧接着来的还是**上一 tick 的** Run 样本
            _driver.ApplyState((byte)NpcAnimationState.Run, false);
            Assert.AreEqual("Attack01", CurrentState(), "确认前不得被陈旧的 locomotion 样本打断");
        }

        [Test]
        public void Leaving_The_Attack_Window_Releases_The_Busy_Flag()
        {
            _driver.ApplyState((byte)NpcAnimationState.Run, false);
            _driver.PlayAttack(0);
            _driver.ApplyState((byte)NpcAnimationState.Attack, false);   // 服务端确认进入攻击窗口
            _driver.ApplyState((byte)NpcAnimationState.Run, false);      // 服务端离开攻击窗口
            Assert.AreEqual("Run", CurrentState(), "离开攻击窗口后必须释放忙位并把时间轴状态写回去");
        }

        [Test]
        public void A_Controller_Missing_The_Driver_Params_Is_Not_Written_To()
        {
            // 回归：美术包自带的控制器（Slime/TurtleShell 的 AIState/Speed/Hit/… 词表）里没有
            // MonsterState——驱动照写就每帧刷 `Parameter 'Hash 54231900' does not exist`（玩家实测日志）。
            // 这里用"有层、参数只配了 1/3"的控制器复现同类缺参控制器：写入必须被整体拒绝（保持默认 0），
            // 而不是写进去。修掉前：ResolveAnimator 只挡 null 控制器，MonsterState 会被写成 2。
            const string path = "Assets/Tests_TempPartialNpc.controller";
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("MonsterState", AnimatorControllerParameterType.Int);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            sm.defaultState = idle;

            var go = new GameObject("PartialParamNpc");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                var driver = go.AddComponent<NpcEntityAnimator>();
                driver.ResolveAnimator();   // EditMode 下 AddComponent 不触发 Awake

                driver.ApplyState((byte)NpcAnimationState.Run, false);

                Assert.AreEqual(0, animator.GetInteger("MonsterState"),
                    "缺参数的控制器必须整只降级为只渲染——写下去只会每帧刷警告");
            }
            finally
            {
                Object.DestroyImmediate(go);
                UnityEditor.AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
