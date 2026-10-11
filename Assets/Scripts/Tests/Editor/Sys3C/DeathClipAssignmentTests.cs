using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 死亡链两个状态的**剪辑挂载**契约。
    ///
    /// N4 之前这两个状态没有任何入边转移，挂反了也看不出来；N4 把
    /// `Death → Down → GetUp` 接上以后，反挂的形态才显形：
    /// `Death` 播的是躺卧循环，玩家一死就**直接躺下**（倒下的过程一个镜头都没有），
    /// `Down` 播的是倒下——躺在地上的不是躺姿。
    /// 按状态名，`Death` 该播倒下（`Die01`，播一次）、`Down` 该播躺卧（`Die01_Stay`，循环）。
    /// </summary>
    public class DeathClipAssignmentTests
    {
        private const string ControllerPath = "Assets/RpgDuo/Animator/Character3C.controller";

        private static string ClipNameOf(AnimatorControllerLayer layer, string stateName)
        {
            foreach (var child in layer.stateMachine.states)
            {
                if (child.state.name == stateName)
                    return child.state.motion != null ? child.state.motion.name : null;
            }
            Assert.Fail("Hit 层里没有名为 " + stateName + " 的状态");
            return null;
        }

        [Test]
        public void Death_Plays_The_Fall_And_Down_Plays_Lying_Still()
        {
            // 两个剪辑挂反了：Death 播的是躺卧循环、Down 播的是倒下。
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, "Animator Controller 缺失: " + ControllerPath);

            var layer = controller.layers.First(l => l.name == "Hit");

            Assert.AreEqual("Die01_SwordAndShield", ClipNameOf(layer, "Death"),
                "Death 必须播倒下（Die01_SwordAndShield）——挂成躺卧循环的话，玩家一死就直接躺平，"
                + "倒下的过程永远看不到");
            Assert.AreEqual("Die01_Stay_SwordAndShield", ClipNameOf(layer, "Down"),
                "Down 必须播躺卧（Die01_Stay_SwordAndShield，循环）——挂成倒下的话，"
                + "此后一直躺在地上的是倒下的终帧，不是躺姿");
        }
    }
}
