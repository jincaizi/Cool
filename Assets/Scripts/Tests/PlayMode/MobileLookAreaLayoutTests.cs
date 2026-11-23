using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DataDefinition;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.UI;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KcpNet.PlayModeTests
{
    public class MobileLookAreaLayoutTests
    {
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;
        private bool _previousIgnore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            InputBlockGate.ClearAll();
            _previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene("DemoDay");
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            InputBlockGate.ClearAll();
            LogAssert.ignoreFailingMessages = _previousIgnore;
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        [UnityTearDown]
        public IEnumerator UnloadDemoScene()
        {
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            SceneManager.SetActiveScene(SceneManager.CreateScene("MobileLookAreaLayoutCleanup"));
            var unload = SceneManager.UnloadSceneAsync("DemoDay");
            while (unload != null && !unload.isDone) yield return null;
            LogAssert.ignoreFailingMessages = previousIgnore;
        }

        private static Transform Find(string name)
        {
            var root = GameObject.Find("MobileUiRoot");
            Assert.IsNotNull(root, "场景里应有 MobileUiRoot");
            var child = root.transform.Find(name);
            Assert.IsNotNull(child, "MobileUiRoot 下缺少 " + name);
            return child;
        }

        [UnityTest]
        public IEnumerator test_mobile_weapon_bag_receives_touches_and_equips_offline()
        {
            var ui = UIManager.Instance;
            Assert.NotNull(ui);
            yield return ui.WhenReady().ToCoroutine();
            yield return ui.CloseAsync<CharacterSelectPanel>().ToCoroutine();
            var input = Object.FindObjectOfType<MobileInputBinder>();
            Assert.NotNull(input);
            input.TouchOverride = TouchOverride.ForceTouch;
            yield return null;
            Assert.IsTrue(Find("LookArea").gameObject.activeInHierarchy);

            var bag = GameObject.Find("WeaponBag");
            Assert.NotNull(bag, "单机角色就绪后要有可换剑的背包");
            Assert.AreSame(ui.GetLayerRoot(LayerType.Popup), bag.transform.parent);
            var entry = bag.transform.Find("背包").GetComponent<Button>();
            AssertButtonReceivesTouch(entry);
            entry.onClick.Invoke();
            yield return null;

            var panel = bag.transform.Find("Swords");
            var names = new[] { "默认剑", "冰剑", "火剑", "雷剑", "风剑" };
            foreach (var gender in new[] { CharacterGender.Male, CharacterGender.Female })
            {
                EventBus.Emit(new CharacterSelectedEvent(gender));
                yield return null;
                yield return null;
                Assert.AreEqual(5, panel.GetComponentsInChildren<Button>().Length);
                foreach (var row in panel.GetComponentsInChildren<Button>())
                    AssertButtonReceivesTouch(row);

                for (int index = 1; index <= names.Length; index++)
                {
                    int elementId = index % names.Length;
                    var sword = panel.Find(names[elementId]).GetComponent<Button>();
                    sword.onClick.Invoke();
                    yield return null;
                    Assert.AreEqual(elementId, input.BoundEntry.CurrentElementId, gender + " " + names[elementId]);
                    Assert.AreEqual(elementId, input.BoundEntry.SkillCoordinator.OfflineWeaponProvider().ElementId);
                }
            }
        }

        private static void AssertButtonReceivesTouch(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.IsNotEmpty(hits, button.name);
            Assert.AreSame(button, hits[0].gameObject.GetComponentInParent<Button>(), button.name);
        }

        [UnityTest]
        public IEnumerator test_mobile_look_area_uses_top_right_quadrant()
        {
            yield return null;

            var rect = (RectTransform)Find("LookArea");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);

            var image = Find("LookArea").GetComponent<Image>();
            Assert.IsNotNull(image);
            Assert.IsTrue(image.raycastTarget);
            Assert.IsNotNull(Find("LookArea").GetComponent<TouchLookArea>());
        }

        [UnityTest]
        public IEnumerator test_mobile_action_buttons_are_bottom_right_anchored()
        {
            yield return null;

            string[] names =
            {
                "AttackButton", "DefendButton", "JumpButton", "SkillQButton",
                "SkillRButton", "FireballButton", "ChargeBlastButton", "MeteorButton",
            };

            foreach (string name in names)
            {
                var rect = (RectTransform)Find(name);
                Assert.AreEqual(new Vector2(1f, 0f), rect.anchorMin, name);
                Assert.AreEqual(new Vector2(1f, 0f), rect.anchorMax, name);
            }

            string[] allMobileButtons =
            {
                "AttackButton", "DefendButton", "JumpButton", "SkillQButton",
                "SkillRButton", "FireballButton", "ChargeBlastButton", "MeteorButton",
                "TargetSwitchButton", "Joystick",
            };

            foreach (string name in allMobileButtons)
            {
                var rect = (RectTransform)Find(name);
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                foreach (var c in corners)
                {
                    Assert.IsTrue(c.x >= 0f && c.x <= Screen.width
                        && c.y >= 0f && c.y <= Screen.height,
                        name + " 角点超出屏幕: " + c);
                }
            }
        }

        [UnityTest]
        public IEnumerator test_mobile_ui_root_applies_effective_reference_resolution()
        {
            yield return null;

            var root = GameObject.Find("MobileUiRoot");
            Assert.Greater(root.transform.lossyScale.x, 0.0001f,
                "MobileUiRoot 不得是零缩放");

            var adapter = root.GetComponent<ScreenAdapter>();
            Assert.IsNotNull(adapter, "MobileUiRoot 必须挂 ScreenAdapter");
            Assert.IsFalse(adapter.ApplySafeArea, "MobileUiRoot 不启用安全区");

            var scaler = root.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler);
            Vector2 expected = GameSettings.Instance.ResolveReferenceResolution();
            Assert.AreEqual(expected.x, scaler.referenceResolution.x, 0.01f);
            Assert.AreEqual(expected.y, scaler.referenceResolution.y, 0.01f);
        }

        [UnityTest]
        public IEnumerator test_mobile_look_area_uses_default_sensitivity()
        {
            yield return null;

            var look = Find("LookArea").GetComponent<TouchLookArea>();
            Assert.IsNotNull(look, "LookArea 必须挂 TouchLookArea");
            Assert.AreEqual(TouchLookArea.DefaultSensitivity, look.Sensitivity, 0.0001f,
                "场景 LookArea 的 _sensitivity 必须与代码默认值同步");
        }

        [UnityTest]
        public IEnumerator test_mobile_look_area_leaves_blank_drag_region_above_skill_cluster()
        {
            yield return null;

            var canvasRect = GameObject.Find("MobileUiRoot").GetComponent<RectTransform>();
            var meteor = (RectTransform)Find("MeteorButton");
            float meteorTop = meteor.anchoredPosition.y + meteor.rect.height * 0.5f;
            float canvasHeight = canvasRect.rect.height;

            Assert.GreaterOrEqual(canvasHeight - meteorTop, 100f,
                "技能栏上方必须留出可拖拽空白区");
        }
    }
}
