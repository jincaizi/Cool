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
    /// 这个人工档位（同时也是面试现场的救命绳）真的进入该分支，断言两件事：
    /// ① 适配器被换成 Joystick（且 ForceNoTouch 时确实维持键鼠）；
    /// ② 玩家被销毁重建后仍能重新绑定 —— PlayerSpawner 每次 `CharacterSelectedEvent` 都会
    ///    销毁旧玩家、实例化新玩家（带全新 InputManager，其 Awake 装的是 KeyboardInputAdapter），
    ///    若 binder 一次性 latch 就会静默失效，手机上的表现是"确认角色后摇杆与按钮只画不发"。
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
        public IEnumerator test_touch_branch_swaps_adapter_and_rebinds_after_player_rebuild()
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

                // ① 反向分支：强制关 → 必须维持键鼠、UI 根不激活（PC 路径不受影响，且不依赖本机有没有触摸屏）
                binder.TouchOverride = TouchOverride.ForceNoTouch;
                for (int i = 0; i < 60; i++) yield return null;
                Assert.AreEqual("Keyboard", AdapterNameOf(entry), "ForceNoTouch 时必须维持键鼠适配器");
                Assert.IsFalse(mobileRoot.activeSelf, "ForceNoTouch 时移动端 UI 根不得激活");

                // ② 正向分支：真的进得去（这是此前测不到的那条）
                binder.TouchOverride = TouchOverride.ForceTouch;
                for (int i = 0; i < 600 && AdapterNameOf(entry) != "Joystick"; i++) yield return null;
                Assert.AreEqual("Joystick", AdapterNameOf(entry), "ForceTouch 后适配器应换成 Joystick");
                Assert.IsTrue(mobileRoot.activeSelf, "ForceTouch 后移动端 UI 根应激活");
                Debug.Log("[T6R] touch branch entered: adapter=" + AdapterNameOf(entry) + " uiRootActive=" + mobileRoot.activeSelf);

                // ③ 重建玩家（= 玩家在手机上点"确认角色"）：必须重新绑定到新玩家的 InputManager
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
            }
            finally
            {
                if (binder != null) binder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }
    }
}
