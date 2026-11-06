using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Definition;
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
        private MobileInputBinder _aimBinder;

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

                // 收尾：本文件的两条用例**各自** LoadScene("DemoDay") 并各自清理。两个类会加载
                // DemoDay：本类与 ClientRespawnWaitTests；不卸载就会把共享 play 会话留在
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

                // ⑤ 核心动作按钮：各在屏内、中心命中自身，且按钮之间不得覆盖。
                string[] buttonNames =
                {
                    "AttackButton", "DefendButton", "JumpButton", "SkillQButton",
                    "SkillRButton", "FireballButton", "ChargeBlastButton", "MeteorButton"
                };
                var buttonRects = new List<Rect>();
                foreach (string buttonName in buttonNames)
                {
                    var child = mobileRoot.transform.Find(buttonName);
                    Assert.IsNotNull(child, "缺少移动按钮 " + buttonName);
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
                    var screenRect = Rect.MinMaxRect(cs[0].x, cs[0].y, cs[2].x, cs[2].y);
                    foreach (var previous in buttonRects)
                        Assert.IsFalse(previous.Overlaps(screenRect), child.name + " 不得与其他动作按钮重叠");
                    buttonRects.Add(screenRect);
                }

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

        /// <summary>
        /// 2026-10-23：摇杆满推保持 0.2s 必须进入冲刺；松手立即退出。
        /// 独立用例（各自 LoadScene + 清理），不塞进几何用例的叙事。
        /// 迟滞区间（0.85 保持 / 0.79 退出）由 EditMode 的 SprintGestureStateTests 覆盖，
        /// 这里只验“手势 → binder → InputManager”这条真链路。
        /// </summary>
        [UnityTest]
        public IEnumerator test_joystick_full_push_sprints_after_hold_and_release_exits()
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
                Assert.IsNotNull(host, "场景里应有常驻激活的 MobileInput 宿主");
                binder = host.GetComponent<MobileInputBinder>();
                Assert.IsNotNull(binder, "MobileInput 上应有 MobileInputBinder");
                binder.TouchOverride = TouchOverride.ForceTouch;

                // 等“被 binder 绑定为 Joystick”的本地 entry；不要直接取第一个
                // （远端视图上也有一个被禁用的 Sys3CEntry）。
                Sys3CEntry entry = null;
                InputManager input = null;
                for (int i = 0; i < 600; i++)
                {
                    foreach (var candidate in Object.FindObjectsOfType<Sys3CEntry>())
                    {
                        if (candidate == null) continue;
                        if (AdapterNameOf(candidate) != "Joystick") continue;
                        entry = candidate;
                        input = candidate.GetComponent<InputManager>();
                        break;
                    }
                    if (entry != null && input != null) break;
                    yield return null;
                }
                Assert.IsNotNull(entry, "应有一个被绑定为 Joystick 的本地玩家");
                Assert.IsNotNull(input, "本地玩家应有 InputManager");

                var mobileRoot = FindRoot("MobileUiRoot");
                Assert.IsNotNull(mobileRoot, "场景里应有 MobileUiRoot");
                var joystick = mobileRoot.GetComponentInChildren<VirtualJoystick>(true);
                Assert.IsNotNull(joystick, "MobileUiRoot 下应有 VirtualJoystick");

                var eventSystem = EventSystem.current;
                Assert.IsNotNull(eventSystem, "应有 EventSystem");
                var joyRect = (RectTransform)joystick.transform;
                var corners = new Vector3[4];
                joyRect.GetWorldCorners(corners);
                var center = new Vector2((corners[0].x + corners[2].x) * 0.5f,
                                         (corners[0].y + corners[2].y) * 0.5f);

                // 按下并把摇杆推到远超半径的屏幕点：Value 必然钳到 1.0，
                // 不依赖 Canvas.scaleFactor / Game View 尺寸。
                var down = new PointerEventData(eventSystem) { position = center };
                ExecuteEvents.Execute(joystick.gameObject, down, ExecuteEvents.pointerDownHandler);
                var drag = new PointerEventData(eventSystem)
                {
                    position = center + new Vector2(Screen.width, Screen.height)
                };
                ExecuteEvents.Execute(joystick.gameObject, drag, ExecuteEvents.dragHandler);
                Assert.AreEqual(1f, joystick.Value.magnitude, 0.001f,
                    "拖到屏外后 Value 必须钳到 1");

                // 满推保持 0.2s；多等一帧让 binder 的 LateUpdate 汇入 InputManager。
                yield return new WaitForSeconds(0.2f);
                yield return null;

                Assert.IsTrue(input.GetMoveCommand(Vector3.forward).IsSprint,
                    "满推保持 0.2s 后应进入冲刺");

                // 抬手：立即退出冲刺。
                var up = new PointerEventData(eventSystem) { position = drag.position };
                ExecuteEvents.Execute(joystick.gameObject, up, ExecuteEvents.pointerUpHandler);
                yield return null;

                Assert.IsFalse(input.GetMoveCommand(Vector3.forward).IsSprint,
                    "松手后必须退出冲刺");

                // 收尾：清档位 + 卸载场景（与既有用例同一套清理）。
                binder.TouchOverride = TouchOverride.Auto;
                yield return null;
                SceneManager.SetActiveScene(SceneManager.CreateScene("T7SprintCleanupScene"));
                var unload = SceneManager.UnloadSceneAsync("DemoDay");
                while (unload != null && !unload.isDone) yield return null;
            }
            finally
            {
                if (binder != null) binder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        /// <summary>
        /// 2026-10-23 最终审查补的覆盖缺口：binder 两条冲刺清理路径此前没有断言。
        /// ① `Update` 的 `!_touch` 分支（切 ForceNoTouch）必须清掉 UI 冲刺标记 ——
        ///   否则只是把 UI 藏了，切回键鼠后角色仍在冲刺；
        /// ② `OnDisable`（停用/失焦）同样必须清掉。
        ///
        /// ① 用**真实摇杆满推**制造前置状态，而不是直接 `SetSprintHeld(true)`：
        /// binder.LateUpdate 每帧把摇杆状态写进 InputManager，直接置位会在同一帧被覆写 ——
        /// 断言照样通过，却覆盖不到清理行（假绿）。
        /// 断言用 `IsSprintHeld()` 而非 `GetMoveCommand().IsSprint`：切键鼠后 adapter 是 Keyboard、
        /// 没有移动输入，后者天然为 false，测不出标记是否残留。
        /// ② `enabled = false` 同步触发 OnDisable，前置状态直接置位即可。
        /// </summary>
        [UnityTest]
        public IEnumerator test_sprint_flag_cleared_when_touch_revoked_and_when_binder_disabled()
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
                Assert.IsNotNull(host, "场景里应有常驻激活的 MobileInput 宿主");
                binder = host.GetComponent<MobileInputBinder>();
                Assert.IsNotNull(binder, "MobileInput 上应有 MobileInputBinder");
                binder.TouchOverride = TouchOverride.ForceTouch;

                // 等“被 binder 绑定为 Joystick”的本地 entry；不要直接取第一个
                // （远端视图上也有一个被禁用的 Sys3CEntry）。
                Sys3CEntry entry = null;
                InputManager input = null;
                for (int i = 0; i < 600; i++)
                {
                    foreach (var candidate in Object.FindObjectsOfType<Sys3CEntry>())
                    {
                        if (candidate == null) continue;
                        if (AdapterNameOf(candidate) != "Joystick") continue;
                        entry = candidate;
                        input = candidate.GetComponent<InputManager>();
                        break;
                    }
                    if (entry != null && input != null) break;
                    yield return null;
                }
                Assert.IsNotNull(entry, "应有一个被绑定为 Joystick 的本地玩家");
                Assert.IsNotNull(input, "本地玩家应有 InputManager");

                var mobileRoot = FindRoot("MobileUiRoot");
                Assert.IsNotNull(mobileRoot, "场景里应有 MobileUiRoot");
                var joystick = mobileRoot.GetComponentInChildren<VirtualJoystick>(true);
                Assert.IsNotNull(joystick, "MobileUiRoot 下应有 VirtualJoystick");
                var eventSystem = EventSystem.current;
                Assert.IsNotNull(eventSystem, "应有 EventSystem");
                var joyRect = (RectTransform)joystick.transform;
                var corners = new Vector3[4];
                joyRect.GetWorldCorners(corners);
                var center = new Vector2((corners[0].x + corners[2].x) * 0.5f,
                                         (corners[0].y + corners[2].y) * 0.5f);

                // ① 真实满推：Value 钳到 1，保持 0.2s 后由 binder.LateUpdate 汇入 InputManager。
                ExecuteEvents.Execute(joystick.gameObject,
                    new PointerEventData(eventSystem) { position = center },
                    ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(joystick.gameObject,
                    new PointerEventData(eventSystem) { position = center + new Vector2(Screen.width, Screen.height) },
                    ExecuteEvents.dragHandler);
                Assert.AreEqual(1f, joystick.Value.magnitude, 0.001f, "拖到屏外后 Value 必须钳到 1");

                for (int i = 0; i < 600 && !input.IsSprintHeld(); i++) yield return null;
                Assert.IsTrue(input.IsSprintHeld(), "前置：满推保持后 UI 冲刺标记应已置位");

                binder.TouchOverride = TouchOverride.ForceNoTouch;
                for (int i = 0; i < 600 && AdapterNameOf(entry) != "Keyboard"; i++) yield return null;
                Assert.AreEqual("Keyboard", AdapterNameOf(entry), "ForceNoTouch 必须把适配器还原成键鼠");
                Assert.IsFalse(input.IsSprintHeld(),
                    "切到 ForceNoTouch 必须清掉 UI 冲刺标记 —— 否则切回键鼠后角色仍在冲刺");

                // ② 重新绑定后停用 binder（同步触发 OnDisable），UI 冲刺标记同样必须清掉。
                binder.TouchOverride = TouchOverride.ForceTouch;
                for (int i = 0; i < 600 && AdapterNameOf(entry) != "Joystick"; i++) yield return null;
                Assert.AreEqual("Joystick", AdapterNameOf(entry), "再次切回触屏应能重新绑定");
                input.SetSprintHeld(true);
                Assert.IsTrue(input.IsSprintHeld(), "前置：UI 冲刺标记应已置位");
                binder.enabled = false;   // OnDisable 同步执行
                Assert.IsFalse(input.IsSprintHeld(),
                    "禁用 MobileInputBinder 触发 OnDisable 后必须清掉 UI 冲刺标记");
                binder.enabled = true;

                // 收尾：清档位 + 卸载场景（与既有用例同一套清理）。
                binder.TouchOverride = TouchOverride.Auto;
                yield return null;
                SceneManager.SetActiveScene(SceneManager.CreateScene("T7SprintFlagCleanupScene"));
                var unload = SceneManager.UnloadSceneAsync("DemoDay");
                while (unload != null && !unload.isDone) yield return null;
            }
            finally
            {
                if (binder != null)
                {
                    binder.enabled = true;
                    binder.TouchOverride = TouchOverride.Auto;
                }
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        /// <summary>
        /// 火球地面瞄准验收：按下 → 拖到近处 → 松手，必须真的发射并进入冷却，
        /// 且发射落点来自拖动（不是松手帧回退到 PC 的最大射程兜底）。
        ///
        /// 与虚拟摇杆用例同一套合成指针事件（直接 ExecuteEvents 派发到按钮 GameObject，
        /// 同时驱动 `MobileSkillButton` 的按下/抬起与 `MobileSkillAimJoystick` 的手势）。
        /// 拖动必须**跨帧**：摇杆在帧末才把值写进 `InputManager`，`Sys3CEntry` 下一帧才在
        /// Charging 里采纳；同帧按-拖-松会让瞄准从未到达执行器（落点会退回 15m 兜底）。
        /// </summary>
        [UnityTest]
        public IEnumerator test_fireball_aim_drag_release_fires()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                yield return LoadDemoDayAndBind();
                var aim = Object.FindObjectOfType<MobileSkillAimJoystick>();
                Assert.IsNotNull(aim, "火球按钮应挂瞄准摇杆");
                aim.Interactable = true;

                SendPointerDown(aim, Vector2.zero);

                var coordinator = _aimBinder.BoundEntry.SkillCoordinator;
                Assert.IsNotNull(coordinator, "本地角色应有技能协调器");
                bool charging = false;
                for (int i = 0; i < 180; i++)
                {
                    if (coordinator.CurrentSkill != null && coordinator.CurrentSkill.SkillId == 20003
                        && coordinator.CurrentSubState == SkillSubState.Charging)
                    {
                        charging = true;
                        break;
                    }
                    yield return null;
                }
                Assert.IsTrue(charging, "按住火球应进入蓄力（HoldToAim）");

                // 屏幕像素必须按 Canvas 缩放换算：摇杆按 Canvas 单位归一（_aimRadius=90），
                // 固定 60px 在 1280×720（scaleFactor≈0.667）会放大成 90 单位 = 满幅 ⇒ 落点 15m。
                var canvas = aim.GetComponentInParent<Canvas>();
                Assert.IsNotNull(canvas, "火球按钮应在 Canvas 下");
                const float dragCanvasUnits = 60f;
                float dragPixels = dragCanvasUnits * canvas.scaleFactor;
                SendPointerDrag(aim, new Vector2(dragPixels, 0f));
                yield return null; // 帧末写入 InputManager，下一帧 Sys3CEntry 采纳落点
                SendPointerUp(aim, new Vector2(dragPixels, 0f));

                // 落点必须来自拖拽，而不是被松手帧 PC 兜底覆盖成最大射程。
                // 冷却在释放（ReportChargeReleased）时起算；循环到冷却出现为止，
                // 期间持续读取执行器落点 —— 最后一个非空值就是实际发射用的落点。
                Vector3 impact = default;
                bool sawSkill = false;
                for (int i = 0; i < 180; i++)
                {
                    var skill = coordinator.CurrentSkill;
                    if (skill != null && skill.SkillId == 20003)
                    {
                        impact = skill.ReportedImpactPoint;
                        sawSkill = true;
                    }
                    if (coordinator.GetCooldownInfo(20003).IsOnCooldown) break;
                    yield return null;
                }

                Assert.IsTrue(sawSkill, "释放后应能读到 20003 的执行器落点");
                Assert.IsTrue(coordinator.GetCooldownInfo(20003).IsOnCooldown,
                    "拖动后松手应发射并进入冷却");

                Vector3 playerPos = _aimBinder.BoundEntry.transform.position;
                float aimDistance = new Vector2(impact.x - playerPos.x, impact.z - playerPos.z).magnitude;
                // 幅度 60/90 ⇒ 期望距离 = Lerp(MinAimDistance 2, ProjectileRange 15, 2/3) ≈ 10.667。
                // 上界守住“不得被覆盖成 15m 兜底”，下界/近似值守住“确实是按拖拽幅度映射”。
                float expected = Mathf.Lerp(2f, 15f, dragCanvasUnits / 90f);
                Assert.Less(aimDistance, 14.5f, "拖拽落点不得被覆盖成最大射程（15m）");
                Assert.Greater(aimDistance, 2f, "拖拽落点必须大于最小瞄准距离");
                Assert.AreEqual(expected, aimDistance, 0.75f,
                    "拖拽落点应接近按幅度映射的距离，实际 " + aimDistance);

                yield return UnloadDemoDay();
            }
            finally
            {
                if (_aimBinder != null) _aimBinder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        /// <summary>
        /// 火球地面瞄准验收：按下 → 拉远到取消阈值 → 松手，不得发射、不得进入冷却。
        /// 先断言技能真的激活，否则"没进冷却"可能只是因为技能从未开始（假绿）。
        /// </summary>
        [UnityTest]
        public IEnumerator test_fireball_aim_far_release_cancels_without_cooldown()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                yield return LoadDemoDayAndBind();
                var aim = Object.FindObjectOfType<MobileSkillAimJoystick>();
                Assert.IsNotNull(aim, "火球按钮应挂瞄准摇杆");
                aim.Interactable = true;

                var coordinator = _aimBinder.BoundEntry.SkillCoordinator;
                Assert.IsNotNull(coordinator, "本地角色应有技能协调器");

                SendPointerDown(aim, Vector2.zero);
                bool activated = false;
                for (int i = 0; i < 180; i++)
                {
                    if (coordinator.CurrentSkill != null && coordinator.CurrentSkill.SkillId == 20003)
                    {
                        activated = true;
                        break;
                    }
                    yield return null;
                }
                Assert.IsTrue(activated, "按住火球应激活 20003");

                // 400 Canvas 单位 > 1.4 × 90 的取消阈值；按 Canvas 缩放换算成屏幕像素。
                var canvas = aim.GetComponentInParent<Canvas>();
                Assert.IsNotNull(canvas, "火球按钮应在 Canvas 下");
                float farPixels = 400f * canvas.scaleFactor;
                SendPointerDrag(aim, new Vector2(0f, farPixels));
                yield return null; // 让取消锁存跨帧被 Sys3CEntry 消费
                SendPointerUp(aim, new Vector2(0f, farPixels));

                for (int i = 0; i < 30; i++) yield return null;

                Assert.IsFalse(coordinator.GetCooldownInfo(20003).IsOnCooldown,
                    "拉远取消不得进入冷却");

                yield return UnloadDemoDay();
            }
            finally
            {
                if (_aimBinder != null) _aimBinder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        /// <summary>
        /// 失焦取消：蓄力中切后台（WebGL 浏览器失焦 / 移动端切走）必须取消火球、
        /// 复位摇杆手势、不进入冷却；之后补发的抬指不得把技能“补射”出去。
        /// 走 `MobileInputBinder.OnApplicationFocus` 这条真实路径（与 Unity 引擎回调同名同签名）。
        /// </summary>
        [UnityTest]
        public IEnumerator test_fireball_aim_focus_loss_cancels_without_cooldown()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                yield return LoadDemoDayAndBind();
                var aim = Object.FindObjectOfType<MobileSkillAimJoystick>();
                Assert.IsNotNull(aim, "火球按钮应挂瞄准摇杆");
                aim.Interactable = true;

                var coordinator = _aimBinder.BoundEntry.SkillCoordinator;
                Assert.IsNotNull(coordinator, "本地角色应有技能协调器");

                SendPointerDown(aim, Vector2.zero);
                bool charging = false;
                for (int i = 0; i < 180; i++)
                {
                    if (coordinator.CurrentSkill != null && coordinator.CurrentSkill.SkillId == 20003
                        && coordinator.CurrentSubState == SkillSubState.Charging)
                    {
                        charging = true;
                        break;
                    }
                    yield return null;
                }
                Assert.IsTrue(charging, "按住火球应进入蓄力（HoldToAim）");

                _aimBinder.SendMessage("OnApplicationFocus", false);
                for (int i = 0; i < 30; i++) yield return null;

                Assert.IsFalse(coordinator.GetCooldownInfo(20003).IsOnCooldown,
                    "失焦取消不得进入冷却");
                Assert.AreNotEqual(SkillSubState.Charging, coordinator.CurrentSubState,
                    "失焦后技能不得仍在蓄力");
                Assert.IsFalse(aim.IsGestureActive, "失焦后摇杆手势必须复位");

                // 手势已复位，补发的抬指不得合成释放（防“回前台自动补射”）。
                SendPointerUp(aim, Vector2.zero);
                for (int i = 0; i < 10; i++) yield return null;
                Assert.IsFalse(coordinator.GetCooldownInfo(20003).IsOnCooldown,
                    "复位后的抬指不得补发火球");

                yield return UnloadDemoDay();
            }
            finally
            {
                if (_aimBinder != null) _aimBinder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        /// <summary>
        /// 精力耗尽取消：蓄力中把精力一次抽干，下一帧 `StaminaExhausted` 必须取消火球、
        /// 复位摇杆手势（`MobileSkillBarPresenter` 的离开 Charging 边沿）且不进入冷却。
        /// 同帧扣除不会立刻取消——取消发生在 `SkillCoordinator.UpdateSustainedStamina`。
        /// </summary>
        [UnityTest]
        public IEnumerator test_fireball_aim_stamina_depleted_cancels_without_cooldown()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                yield return LoadDemoDayAndBind();
                var aim = Object.FindObjectOfType<MobileSkillAimJoystick>();
                Assert.IsNotNull(aim, "火球按钮应挂瞄准摇杆");
                aim.Interactable = true;

                var coordinator = _aimBinder.BoundEntry.SkillCoordinator;
                Assert.IsNotNull(coordinator, "本地角色应有技能协调器");
                var stamina = _aimBinder.BoundEntry.Stamina;
                Assert.IsNotNull(stamina, "本地角色应有精力系统");

                SendPointerDown(aim, Vector2.zero);
                bool charging = false;
                for (int i = 0; i < 180; i++)
                {
                    if (coordinator.CurrentSkill != null && coordinator.CurrentSkill.SkillId == 20003
                        && coordinator.CurrentSubState == SkillSubState.Charging)
                    {
                        charging = true;
                        break;
                    }
                    yield return null;
                }
                Assert.IsTrue(charging, "按住火球应进入蓄力（HoldToAim）");

                Assert.IsFalse(stamina.TryConsume(100f), "一次性抽干精力应直接力竭");
                for (int i = 0; i < 30; i++) yield return null;

                Assert.IsFalse(coordinator.GetCooldownInfo(20003).IsOnCooldown,
                    "精力耗尽取消不得进入冷却");
                Assert.AreNotEqual(SkillSubState.Charging, coordinator.CurrentSubState,
                    "精力耗尽后技能不得仍在蓄力");
                Assert.IsFalse(aim.IsGestureActive, "精力耗尽后摇杆手势必须复位");

                SendPointerUp(aim, Vector2.zero);
                for (int i = 0; i < 10; i++) yield return null;
                Assert.IsFalse(coordinator.GetCooldownInfo(20003).IsOnCooldown,
                    "复位后的抬指不得补发火球");

                yield return UnloadDemoDay();
            }
            finally
            {
                if (_aimBinder != null) _aimBinder.TouchOverride = TouchOverride.Auto;
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

        /// <summary>加载 DemoDay 并把 MobileInputBinder 拨进触屏档，等待本地角色绑定。</summary>
        private IEnumerator LoadDemoDayAndBind()
        {
            SceneManager.LoadScene("DemoDay");
            yield return null;
            yield return null;

            var host = FindRoot("MobileInput");
            Assert.IsNotNull(host, "场景里应有 MobileInput 宿主");
            _aimBinder = host.GetComponent<MobileInputBinder>();
            Assert.IsNotNull(_aimBinder);
            _aimBinder.TouchOverride = TouchOverride.ForceTouch;

            for (int i = 0; i < 600 && _aimBinder.BoundEntry == null; i++) yield return null;
            Assert.IsNotNull(_aimBinder.BoundEntry, "应绑定本地角色");
        }

        private IEnumerator UnloadDemoDay()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("FireballAimCleanup"));
            var unload = SceneManager.UnloadSceneAsync("DemoDay");
            while (unload != null && !unload.isDone) yield return null;
            _aimBinder = null;
        }

        private static void SendPointerDown(MobileSkillAimJoystick aim, Vector2 position)
        {
            var data = new PointerEventData(EventSystem.current) { position = position };
            ExecuteEvents.Execute(aim.gameObject, data, ExecuteEvents.pointerDownHandler);
        }

        private static void SendPointerDrag(MobileSkillAimJoystick aim, Vector2 position)
        {
            var data = new PointerEventData(EventSystem.current) { position = position };
            ExecuteEvents.Execute(aim.gameObject, data, ExecuteEvents.dragHandler);
        }

        private static void SendPointerUp(MobileSkillAimJoystick aim, Vector2 position)
        {
            var data = new PointerEventData(EventSystem.current) { position = position };
            ExecuteEvents.Execute(aim.gameObject, data, ExecuteEvents.pointerUpHandler);
        }
    }
}
