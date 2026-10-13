using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 远端行走动画驱动（design §4 行走动画映射）。
    /// 本地是**输入驱动**（0/0.5/1 离散），远端是**速度驱动**（过阈值 + 按比例调步频）——
    /// 同一动作取值可能有细微差异，这是有意的（远端没有输入，只有渲染位移）。
    /// </summary>
    public class RemoteEntityAnimatorLocomotionTests
    {
        private GameObject _go = null!;
        private RemoteEntityAnimator _driver = null!;
        private const string TempControllerPath = "Assets/Tests_TempLocomotion.controller";

        [SetUp]
        public void SetUp()
        {
            var controller = UnityEditor.Animations.AnimatorController
                .CreateAnimatorControllerAtPath(TempControllerPath);
            foreach (var (name, type) in new[]
            {
                ("Blend", AnimatorControllerParameterType.Float),
                ("LocomotionSpeed", AnimatorControllerParameterType.Float),
                ("BaseState", AnimatorControllerParameterType.Int),
                ("HitState", AnimatorControllerParameterType.Int),
            })
                controller.AddParameter(name, type);

            _go = new GameObject("LocomotionDriverTest");
            var animator = _go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            _driver = _go.AddComponent<RemoteEntityAnimator>();
            // EditMode 下 AddComponent 不触发生命周期——必须显式解析一次（与 NpcEntityAnimatorTests 同惯例；
            // 该方法已是 internal，测试程序集有 InternalsVisibleTo）
            _driver.ResolveAnimator();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            UnityEditor.AssetDatabase.DeleteAsset(TempControllerPath);
        }

        [Test]
        public void Stationary_Maps_To_Idle_And_Keeps_The_Cycle_Running()
        {
            _driver.ApplyLocomotion(0f);
            Assert.AreEqual(0f, _driver.LastBlend, "静止 ⇒ Blend=0（Idle）");
            Assert.AreEqual(1f, _driver.LastLocomotionSpeed,
                "静止必须写 1 而不是 0——写 0 会把 idle 冻成静止帧");
        }

        [Test]
        public void Bot_Speed_At_8_mps_Maps_To_Sprint_With_Matched_Foot_Speed()
        {
            // bot 实测 0.53 m/tick ÷ 0.066s ≈ 8.03 m/s（标称跑速 8.0）⇒ Blend=1、步频≈1
            _driver.ApplyLocomotion(8.03f);
            Assert.AreEqual(1f, _driver.LastBlend, "≥ 走跑中点(6.5) ⇒ Blend=1（跑）");
            Assert.That(_driver.LastLocomotionSpeed, Is.EqualTo(1.004f).Within(0.02f),
                "步频 = 速度 ÷ 标称跑速（8.0）——不按比例调就是溜冰");
        }

        [Test]
        public void Walk_Speed_Scales_The_Cycle_Rate()
        {
            // 慢速移动：Blend 落到走档（0.5），步频按 速度 ÷ 标称走速(5.0) 缩放
            _driver.ApplyLocomotion(2.5f);
            Assert.AreEqual(0.5f, _driver.LastBlend, "低于走跑中点 ⇒ 走档");
            Assert.AreEqual(0.5f, _driver.LastLocomotionSpeed, "2.5 ÷ 5.0 = 0.5 步频");
        }

        [Test]
        public void Cycle_Rate_Has_A_Floor()
        {
            // 极慢速（0.2 m/s 仍算移动）：比例为 0.04 ⇒ 必须钳到下限，否则腿几乎不动
            _driver.ApplyLocomotion(0.2f);
            Assert.AreEqual(0.5f, _driver.LastBlend);
            Assert.AreEqual(0.3f, _driver.LastLocomotionSpeed, "下限 0.3");
        }

        [Test]
        public void Parameters_Actually_Land_On_The_Animator()
        {
            _driver.ApplyLocomotion(8.03f);
            var animator = _go.GetComponent<Animator>()!;
            Assert.AreEqual(1f, animator.GetFloat("Blend"), "Blend 必须真的写进控制器");
            Assert.AreEqual(7, animator.GetInteger("BaseState"), "移动态 = Locomotion(7)");

            // 步频必须用"比例 ≠ 1"的场景钉在**真实 Animator** 上：8.03 ÷ 8.0 = 1.004，与"写死 1"
            // 只差 0.004，断在这里放不出滑步；2.5 ÷ 5.0 = 0.5 才能让"没按比例调/压根没写"当场变红
            // （只断言观测属性 `LastLocomotionSpeed` 是弱 oracle——它和写入是同一个方法算出来的）。
            _driver.ApplyLocomotion(2.5f);
            Assert.AreEqual(0.5f, animator.GetFloat("LocomotionSpeed"),
                "LocomotionSpeed 必须真的落到 Animator 上（只写进观测属性 = 远端步频根本没接上）");
        }

        [Test]
        public void Invalidate_Dedup_Forces_A_Rewrite_After_A_Tier_Promotion()
        {
            _driver.ApplyLocomotion(0f);
            _go.GetComponent<Animator>()!.SetFloat("Blend", 99f);   // 模拟禁用期状态被重置
            _driver.InvalidateDedup();
            _driver.ApplyLocomotion(0f);
            Assert.AreEqual(0f, _go.GetComponent<Animator>()!.GetFloat("Blend"),
                "升档后必须强制重写一次（Animator 禁用即重置状态，去重游标不能拦它）");
        }

        /// <summary>
        /// 标称速度常量与**本地** `CharacterController` 的一致性守护——不一致时，远端按错的标称
        /// 速度缩放步频/挑档位，表现是**脚步无声滑步**（循环按 A 速度走、位移是 B 速度）。
        ///
        /// 真值源是普通类（非 MonoBehaviour，且只有带参构造），实例化不了 ⇒ 读源码文本解析
        /// （同 MonsterBodyRadiusTests 读 `monster_config.json` 的"文本即真值源"惯例）。
        /// </summary>
        [Test]
        public void Nominal_Speeds_Match_The_Local_Character_Controller()
        {
            string path = Path.Combine(Application.dataPath,
                "Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs");
            Assert.IsTrue(File.Exists(path), "本地 CharacterController 源码应存在：" + path);
            string source = File.ReadAllText(path);

            float localWalk = ParseFloatDefault(source, "MoveSpeed");
            float localSprint = ParseFloatDefault(source, "SprintSpeed");

            Assert.AreEqual(localWalk, RemoteEntityAnimator.NominalWalkSpeed,
                "NominalWalkSpeed 必须与本地 MoveSpeed(" + localWalk + ") 一致——"
                + "不一致时远端走档步频按错的标称速度缩放，脚步无声滑步");
            Assert.AreEqual(localSprint, RemoteEntityAnimator.NominalSprintSpeed,
                "NominalSprintSpeed 必须与本地 SprintSpeed(" + localSprint + ") 一致——跑档同样会滑步");
            Assert.AreEqual((localWalk + localSprint) * 0.5f, RemoteEntityAnimator.WalkRunSplitSpeed,
                "走跑分界必须是两个标称速度的中点——偏了会让远端在某档用错标称速度（步频错、脚步滑步）");
        }

        /// <summary>从源码文本里抠出 `public float X { get; set; } = 1f;` 的默认值。</summary>
        private static float ParseFloatDefault(string source, string propertyName)
        {
            var match = Regex.Match(source,
                @"float\s+" + Regex.Escape(propertyName) + @"\s*\{[^}]*\}\s*=\s*(-?[0-9]*\.?[0-9]+)f");
            Assert.IsTrue(match.Success,
                "没在 CharacterController.cs 里解析到 " + propertyName + " 的默认值——"
                + "属性写法变了就要同步改本守护用例的正则，别让它静默失效");
            return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}
