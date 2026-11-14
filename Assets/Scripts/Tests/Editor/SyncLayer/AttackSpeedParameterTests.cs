using System.Linq;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class AttackSpeedParameterTests
    {
        [Test]
        public void Clamps_And_Stores_Attack_Speed()
        {
            var go = new GameObject("animator-test");
            var animator = go.AddComponent<RemoteEntityAnimator>();
            try
            {
                animator.PlayAttack("Attack", 1f, 1.25f);
                Assert.AreEqual(1.25f, animator.AttackSpeedForTest, 0.001f);

                animator.PlayAttack("Attack", 1f, 0.01f);
                Assert.AreEqual(0.25f, animator.AttackSpeedForTest, 0.001f);

                animator.PlayAttack("Attack", 1f, 99f);
                Assert.AreEqual(4f, animator.AttackSpeedForTest, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Attack_Duration_Is_Divided_By_Speed()
        {
            var go = new GameObject("animator-duration-test");
            var animator = go.AddComponent<RemoteEntityAnimator>();
            try
            {
                animator.PlayAttack("Attack", 2f, 2f);
                Assert.AreEqual(1f, animator.AttackRemainingForTest, 0.001f);

                animator.Tick(0.25f);
                Assert.AreEqual(0.75f, animator.AttackRemainingForTest, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Real_Controller_Binds_AttackSpeed_On_Light_And_Heavy_States()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/RpgDuo/Animator/Character3C.controller");
            Assert.IsNotNull(controller, "Character3C.controller 未找到");

            var parameter = controller.parameters.FirstOrDefault(p => p.name == "AttackSpeed");
            Assert.IsNotNull(parameter, "控制器必须声明 float 参数 AttackSpeed");
            Assert.AreEqual(AnimatorControllerParameterType.Float, parameter!.type);

            var attackLayer = controller.layers.First(l => l.name == "Attack");
            var states = attackLayer.stateMachine.states
                .Select(s => s.state)
                .Where(s => s.name == "BaseAttack" || s.name == "Attack02")
                .ToList();
            Assert.AreEqual(2, states.Count, "轻击 BaseAttack / 重击 Attack02 都必须存在");
            foreach (var state in states)
            {
                Assert.IsTrue(state.speedParameterActive,
                    state.name + " 必须启用 speed parameter");
                Assert.AreEqual("AttackSpeed", state.speedParameter,
                    state.name + " 的 speed parameter 必须是 AttackSpeed");
            }
        }

        [Test]
        public void State_Machine_TimeScale_Scales_Elapsed_Time()
        {
            var data = ScriptableObject.CreateInstance<ComboSkillData>();
            try
            {
                var machine = new FakeClockStateMachine(data) { Now = 0f };
                Assert.IsTrue(machine.TryStart());

                machine.Now = 0.1f;
                machine.TimeScale = 1f;
                machine.Update(0.1f);
                Assert.AreEqual(0.1f, machine.ElapsedTime, 0.001f);

                machine.Now = 0.2f;
                machine.TimeScale = 2f;
                machine.Update(0.1f);
                Assert.AreEqual(0.4f, machine.ElapsedTime, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        private sealed class FakeClockStateMachine : SkillStateMachine
        {
            public float Now;

            public FakeClockStateMachine(SkillData data) : base(data) { }

            protected override float GetCurrentTime() => Now;
        }
    }
}
