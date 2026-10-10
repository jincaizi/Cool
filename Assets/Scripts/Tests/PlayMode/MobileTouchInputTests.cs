using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// Task 6 修复轮：移动端分支的验收。
    ///
    /// `Application.isMobilePlatform` 在编辑器恒 false，所以这条分支此前**按构造测不到** ——
    /// 这正是"绑定不可重入"那个缺陷一路绿灯出厂的原因。本测试用 `MobileInputBinder.TouchOverride`
    /// 这个人工档位（同时也是出包后 URL 覆盖的同类入口）真的进入该分支，断言四件事：
    /// ① 适配器被换成 Joystick；② 切回无触控时适配器被**撤销**回键鼠（不是只藏 UI）；
    /// ③ 再切回来仍能重新绑定；④ 玩家被销毁重建后仍能重新绑定。
    ///
    /// 全程**不依赖宿主机自身有没有触摸屏**：断言都建立在显式档位上，而不是 Auto 的结果。
    /// （`Input.touchSupported` 在一台触屏 PC 上为 true ⇒ Auto 会先绑 Joystick；因此本用例
    /// 不把"Auto ⇒ Keyboard"当作断言。）
    /// </summary>
    public class MobileTouchInputTests
    {
        private static GameObject FindRoot(string name)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        private static string AdapterNameOf(Sys3CEntry entry)
        {
            if (entry == null) return null;
            var inputManager = entry.GetComponent<InputManager>();
            if (inputManager == null) return null;
            var field = typeof(InputManager).GetField("_adapter", BindingFlags.Instance | BindingFlags.NonPublic);
            var adapter = field != null ? field.GetValue(inputManager) as IInputAdapter : null;
            return adapter != null ? adapter.AdapterName : null;
        }

        [UnityTest]
        public IEnumerator test_touch_branch_binds_revokes_and_rebinds_across_player_rebuild()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;   // DemoDay 起网络但没有服务器，容忍其报错
            MobileInputBinder binder = null;
            try
            {
                SceneManager.LoadScene("DemoDay");
                yield return null;
                yield return null;

                var host = FindRoot("MobileInput");
                Assert.IsNotNull(host, "场景里应有常驻激活的 MobileInput 宿主（binder 不能挂在不激活的 UI 根下）");
                binder = host.GetComponent<MobileInputBinder>();
                Assert.IsNotNull(binder, "MobileInput 上应有 MobileInputBinder");
                var mobileRoot = FindRoot("MobileUiRoot");
                Assert.IsNotNull(mobileRoot, "场景里应有 MobileUiRoot");

                // 等玩家生成（PlayerSpawner 在场景加载时就生成）
                Sys3CEntry entry = null;
                for (int i = 0; i < 600 && entry == null; i++)
                {
                    entry = Object.FindObjectOfType<Sys3CEntry>();
                    if (entry == null) yield return null;
                }
                Assert.IsNotNull(entry, "PlayerSpawner 应在场景加载时生成玩家");

                // ① 进移动端分支（此前测不到的那条）
                binder.TouchOverride = TouchOverride.ForceTouch;
                for (int i = 0; i < 600 && AdapterNameOf(entry) != "Joystick"; i++) yield return null;
                Assert.AreEqual("Joystick", AdapterNameOf(entry), "ForceTouch 后适配器应换成 Joystick");
                Assert.IsTrue(mobileRoot.activeSelf, "ForceTouch 后移动端 UI 根应激活");
                Debug.Log("[T6R] bound: adapter=" + AdapterNameOf(entry) + " uiRootActive=" + mobileRoot.activeSelf);

                // ② 回退：切到无触控必须**撤销**已装的适配器（只藏 UI 会让 Joystick 继续驱动角色）
                binder.TouchOverride = TouchOverride.ForceNoTouch;
                for (int i = 0; i < 600 && AdapterNameOf(entry) != "Keyboard"; i++) yield return null;
                Assert.AreEqual("Keyboard", AdapterNameOf(entry), "ForceNoTouch 必须把适配器还原成键鼠（不是只藏 UI）");
                Assert.IsFalse(mobileRoot.activeSelf, "ForceNoTouch 时移动端 UI 根不得激活");
                Debug.Log("[T6R] revoked: adapter=" + AdapterNameOf(entry) + " uiRootActive=" + mobileRoot.activeSelf);

                // ③ 再切回来仍能绑定
                binder.TouchOverride = TouchOverride.ForceTouch;
                for (int i = 0; i < 600 && AdapterNameOf(entry) != "Joystick"; i++) yield return null;
                Assert.AreEqual("Joystick", AdapterNameOf(entry), "再次切回触屏应能重新绑定");

                // ④ 重建玩家（= 手机上点"确认角色"这条真实路径）：必须重新绑定到新玩家的 InputManager
                var oldPlayer = entry.gameObject;
                EventBus.Emit(new CharacterSelectedEvent(CharacterGender.Female));
                yield return null;

                Sys3CEntry reborn = null;
                for (int i = 0; i < 600; i++)
                {
                    reborn = Object.FindObjectOfType<Sys3CEntry>();
                    if (reborn != null && reborn.gameObject != oldPlayer && AdapterNameOf(reborn) == "Joystick") break;
                    yield return null;
                }

                Assert.IsNotNull(reborn, "确认角色后应生成新玩家");
                Assert.AreNotSame(oldPlayer, reborn.gameObject, "PlayerSpawner 应销毁旧玩家并重建");
                Assert.IsNotNull(reborn.GetComponent<InputManager>(), "新玩家应有自己的 InputManager");
                Debug.Log("[T6R] after rebuild: adapter=" + AdapterNameOf(reborn));
                Assert.AreEqual("Joystick", AdapterNameOf(reborn),
                    "新玩家必须被重新绑定为触屏适配器 —— 否则手机上确认角色后摇杆与按钮整体静默失效");

                // 收尾：本用例是 Tests/ 下**唯一** LoadScene 的用例（其余都从
                // Resources/PlayerEntryForTest 自己装配），不卸载就会把共享 play 会话留在
                // "联网 + 已重建玩家"的 DemoDay 上，给其后所有用例制造顺序依赖。
                // 先清人工档位（回退逻辑会把适配器还原成键鼠），再换空场景并卸载 DemoDay。
                binder.TouchOverride = TouchOverride.Auto;
                yield return null;
                SceneManager.SetActiveScene(SceneManager.CreateScene("T6CleanupScene"));
                var unload = SceneManager.UnloadSceneAsync("DemoDay");
                while (unload != null && !unload.isDone) yield return null;
                Debug.Log("[T6R] cleanup: loadedScenes=" + SceneManager.sceneCount);
            }
            finally
            {
                if (binder != null) binder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }
    }
}
