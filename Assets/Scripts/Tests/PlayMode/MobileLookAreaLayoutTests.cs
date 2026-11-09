using System.Collections;
using DataDefinition;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.UI;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
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

        private static Transform Find(string name)
        {
            var root = GameObject.Find("MobileUiRoot");
            Assert.IsNotNull(root, "场景里应有 MobileUiRoot");
            var child = root.transform.Find(name);
            Assert.IsNotNull(child, "MobileUiRoot 下缺少 " + name);
            return child;
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
