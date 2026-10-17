using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// Task 6 修复轮：移动端分支的验收。
    ///
    /// `Application.isMobilePlatform` 在编辑器恒 false —— 所以历史上这条分支按构造测不到，
    /// "绑定不可重入"那个缺陷因此一路绿灯出厂。2026-10-17 起编辑器**默认**就进触屏分支
    /// （产品决定），但本文件的断言仍建立在**显式档位**上，不依赖 `Auto` 的结果。
    /// 本测试用 `MobileInputBinder.TouchOverride` 真的进入该分支，断言四件事：
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

                // 收尾：本文件的两条用例**各自** LoadScene("DemoDay") 并各自清理 —— 其余用例
                // 都从 Resources/PlayerEntryForTest 自己装配；不卸载就会把共享 play 会话留在
                // "联网 + 已重建玩家"的 DemoDay 上，给其后所有用例制造顺序依赖。
                // 先清人工档位（编辑器里 Auto 也判触屏，适配器保持 Joystick，随场景卸载一起销毁），
                // 再换空场景并卸载 DemoDay。
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

        /// <summary>
        /// 触控 UI 真的落在屏上、且摇杆保留了模拟量（2026-10-17 补的验收缺口）。
        ///
        /// 本用例是**表征测试**：预期直接通过 —— 守的是「以后别坏」，不是「现在坏了要修」。
        /// 现有断言只到 `MobileUiRoot.activeSelf`，而「激活」不等于「可见可点」：节点可以
        /// 0 尺寸、在屏外、或被别的 raycast 目标盖住（T6 当年只有程序化探针，没有这条）。
        /// 显式档位而非 Auto：与本文件既有约定一致，不依赖宿主有没有触摸屏。
        ///
        /// 边界（不过度声称）：
        /// - 本用例验的是**触控 UI 这一层**（几何/命中/模拟量）。`Value` → `JoystickInputAdapter`
        ///   → `InputManager` 那条链已由本文件既有用例与 `MobileInputAdapterTests` 覆盖，不在此重复。
        /// - 摇杆的**视觉样式**（当前是半透明方块背景 + 方块手柄，不是圆形摇杆头）不在断言范围。
        /// - 真手指只在手机上能验；本用例走的是合成指针事件（与验收清单 3.2 的
        ///   `EventSystem.RaycastAll` 那条路同一条）。
        /// - 装置只到「关闭选人面板」这一步：**不覆盖**确认后 `OnClosed → LoadScene("DemoDay")`
        ///   的场景重载与玩家重建（那是既有用例 ④ 与 `HudLifecycleTests` 的范围）。
        /// </summary>
        [UnityTest]
        public IEnumerator test_touch_ui_lands_on_screen_and_joystick_stays_analog()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;   // DemoDay 可能起网络，容忍其报错
            MobileInputBinder binder = null;
            try
            {
                SceneManager.LoadScene("DemoDay");
                yield return null;
                yield return null;

                var host = FindRoot("MobileInput");
                Assert.IsNotNull(host, "场景里应有常驻激活的 MobileInput 宿主");
                binder = host.GetComponent<MobileInputBinder>();
                Assert.IsNotNull(binder, "MobileInput 上应有 MobileInputBinder");
                binder.TouchOverride = TouchOverride.ForceTouch;

                GameObject mobileRoot = null;
                for (int i = 0; i < 600; i++)
                {
                    mobileRoot = FindRoot("MobileUiRoot");
                    if (mobileRoot != null && mobileRoot.activeSelf) break;
                    yield return null;
                }
                Assert.IsNotNull(mobileRoot, "场景里应有 MobileUiRoot");
                Assert.IsTrue(mobileRoot.activeSelf, "ForceTouch 后移动端 UI 根应激活");

                // 进游戏态：DemoDay 启动时 UIManager 会打开选人面板（Popup 3000），其全屏 Bg
                // 按设计盖住 MobileUiRoot（2500）—— 确认前摸不到摇杆是正确行为。真实流程里
                // Confirm() 内部执行的正是 CloseAsync；这里只做那一步（不调 Confirm —— 它会写
                // PlayerPrefs 的连接设置）。之后断言才是「进游戏后」的 HUD。
                var ui = UIManager.Instance;
                // 启动装载是异步的：先等它完成（此时选人面板已打开），避免「关在打开之前」的竞态
                for (int i = 0; i < 600 && ui != null && !ui.IsReady; i++) yield return null;
                // 测试程序集没有 UniTask 引用（asmdef 未列），公开的 CloseAsync 只能反射调用；
                // 它同步完成（该面板无隐藏动画），与 Confirm() 内部执行的关闭步骤完全一致。
                var closeAsync = typeof(UIManager).GetMethod("CloseAsync");
                if (ui != null && closeAsync != null)
                    closeAsync.MakeGenericMethod(typeof(CharacterSelectPanel)).Invoke(ui, null);
                var selectPanel = ui != null ? ui.GetPanel<CharacterSelectPanel>() : null;
                for (int i = 0; i < 600 && selectPanel != null && selectPanel.IsVisible; i++) yield return null;
                Assert.IsTrue(selectPanel == null || !selectPanel.IsVisible, "选人面板应已关闭（进游戏态）");

                var joystick = mobileRoot.GetComponentInChildren<VirtualJoystick>(true);
                Assert.IsNotNull(joystick, "MobileUiRoot 下应有 VirtualJoystick");
                var joyRect = (RectTransform)joystick.transform;

                // ① 几何：摇杆完整在屏内，中心在左下象限（验收清单 3.2「左下摇杆」）
                var corners = new Vector3[4];
                joyRect.GetWorldCorners(corners);
                foreach (var c in corners)
                    Assert.IsTrue(c.x >= 0f && c.x <= Screen.width && c.y >= 0f && c.y <= Screen.height,
                        "摇杆必须完整落在屏幕内，实际角点 " + c + "（屏 " + Screen.width + "x" + Screen.height + "）");
                var center = new Vector2((corners[0].x + corners[2].x) * 0.5f,
                                         (corners[0].y + corners[2].y) * 0.5f);
                Assert.IsTrue(center.x < Screen.width * 0.5f, "摇杆中心必须在左半屏");
                Assert.IsTrue(center.y < Screen.height * 0.5f, "摇杆中心必须在下半屏");

                // ② 射线：摇杆中心必须命中摇杆自身（activeSelf==true 保证不了可点）
                var eventSystem = EventSystem.current;
                Assert.IsNotNull(eventSystem, "应有 EventSystem（UIBootstrap 在 AfterSceneLoad 建）");
                var hits = new List<RaycastResult>();
                eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = center }, hits);
                Assert.IsTrue(hits.Count > 0 && hits[0].gameObject != null &&
                    (hits[0].gameObject == joystick.gameObject || hits[0].gameObject.transform.IsChildOf(joystick.transform)),
                    "摇杆中心射线的首位命中必须是摇杆自身（被面板/观察区盖住就是不可点）");

                // ③ 模拟量：小位移 < 大位移（半推=慢走、满推=快走；只断大小关系，不断言饱和）
                float small = DragAndGetMagnitude(joystick, center, 20f);
                float big = DragAndGetMagnitude(joystick, center, 90f);
                Assert.IsTrue(small > 0f, "小位移拖拽后 Value 必须非零 —— 模拟量没被丢弃");
                Assert.IsTrue(small < big, "小位移的 Value 必须小于大位移，实际 " + small + " vs " + big);
                Assert.IsTrue(big <= 1.0001f, "Value 必须被钳在 0–1，实际 " + big);

                // ④ 抬指：立刻归零（松手立刻停）
                var up = new PointerEventData(eventSystem) { position = center + new Vector2(90f, 0f) };
                ExecuteEvents.Execute(joystick.gameObject, up, ExecuteEvents.pointerUpHandler);
                Assert.AreEqual(Vector2.zero, joystick.Value, "pointerUp 后 Value 必须归零");

                // ⑤ 三按钮：各在屏内，且中心射线命中自身（不被 LookArea 盖住）
                int buttonsChecked = 0;
                for (int i = 0; i < mobileRoot.transform.childCount; i++)
                {
                    var child = mobileRoot.transform.GetChild(i);
                    if (!child.name.EndsWith("Button")) continue;
                    buttonsChecked++;
                    var rect = (RectTransform)child;
                    var cs = new Vector3[4];
                    rect.GetWorldCorners(cs);
                    foreach (var c in cs)
                        Assert.IsTrue(c.x >= 0f && c.x <= Screen.width && c.y >= 0f && c.y <= Screen.height,
                            child.name + " 必须完整落在屏幕内");
                    var bc = new Vector2((cs[0].x + cs[2].x) * 0.5f, (cs[0].y + cs[2].y) * 0.5f);
                    var bhits = new List<RaycastResult>();
                    eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = bc }, bhits);
                    Assert.IsTrue(bhits.Count > 0 && bhits[0].gameObject == child.gameObject,
                        child.name + " 中心射线的首位命中必须是按钮自身（被观察区盖住就是不可点）");
                }
                Assert.AreEqual(3, buttonsChecked, "应有三个动作按钮（AttackButton/DefendButton/JumpButton）");

                // 收尾：清档位 + 卸载场景（与既有用例同一套清理）
                binder.TouchOverride = TouchOverride.Auto;
                yield return null;
                SceneManager.SetActiveScene(SceneManager.CreateScene("T7CleanupScene"));
                var unload = SceneManager.UnloadSceneAsync("DemoDay");
                while (unload != null && !unload.isDone) yield return null;
            }
            finally
            {
                if (binder != null) binder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        /// <summary>合成一次 pointerDown + drag，返回摇杆 Value 的模长；最后合成 pointerUp 复位。</summary>
        private static float DragAndGetMagnitude(VirtualJoystick joystick, Vector2 start, float dx)
        {
            var es = EventSystem.current;
            var down = new PointerEventData(es) { position = start };
            ExecuteEvents.Execute(joystick.gameObject, down, ExecuteEvents.pointerDownHandler);
            var drag = new PointerEventData(es) { position = start + new Vector2(dx, 0f) };
            ExecuteEvents.Execute(joystick.gameObject, drag, ExecuteEvents.dragHandler);
            float magnitude = joystick.Value.magnitude;
            var up = new PointerEventData(es) { position = drag.position };
            ExecuteEvents.Execute(joystick.gameObject, up, ExecuteEvents.pointerUpHandler);
            return magnitude;
        }
    }
}
