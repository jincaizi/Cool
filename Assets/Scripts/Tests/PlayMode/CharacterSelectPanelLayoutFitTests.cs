using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KcpNet.PlayModeTests
{
    public class CharacterSelectPanelLayoutFitTests
    {
        private GameObject _prefab;
        private EventSystem _eventSystem;
        private GameObject _eventSystemGo;
        private GameObject _canvasGo;
        private GameObject _panel;
        private CharacterSelectPanel _panelComponent;
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;

        [SetUp]
        public void SetUp()
        {
            _prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(_prefab);
        }

        [UnitySetUp]
        public IEnumerator InteractionSetUp()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
            NetworkModeService.Save(NetworkMode.Online);

            if (_prefab == null)
                _prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(_prefab);

            _eventSystem = EventSystem.current;
            if (_eventSystem == null)
            {
                _eventSystemGo = new GameObject("CharacterSelectTests_EventSystem");
                _eventSystem = _eventSystemGo.AddComponent<EventSystem>();
                _eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }

            _canvasGo = new GameObject("CharacterSelectTests_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f) / 1.15f;
            scaler.matchWidthOrHeight = 0.5f;

            _panel = Object.Instantiate(_prefab, _canvasGo.transform, false);
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _panelComponent = _panel.GetComponent<CharacterSelectPanel>();
            var show = _panelComponent.GetType()
                .GetMethod("OnShow", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? typeof(UIPanel).GetMethod("OnShow", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(show);
            show.Invoke(_panelComponent, null);

            yield return null;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_panel.transform);
        }

        [TearDown]
        public void InteractionTearDown()
        {
            if (_panel != null) Object.DestroyImmediate(_panel);
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            if (_eventSystemGo != null) Object.DestroyImmediate(_eventSystemGo);
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        private static float ScaleFactor(Vector2 screen, Vector2 reference, float match)
        {
            float logWidth = Mathf.Log(screen.x / reference.x, 2f);
            float logHeight = Mathf.Log(screen.y / reference.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, match));
        }

        private static Bounds BoundsOf(RectTransform parent, RectTransform child)
            => RectTransformUtility.CalculateRelativeRectTransformBounds(parent, child);

        [UnityTest]
        public IEnumerator test_character_select_controls_fit_without_overlap_at_phone_aspects()
        {
            Vector2[] screens =
            {
                new Vector2(1920f, 1080f),
                new Vector2(2400f, 1080f),
                new Vector2(2520f, 1080f),
            };
            Vector2 reference = new Vector2(1920f, 1080f) / 1.15f;

            foreach (Vector2 screen in screens)
            {
                float scale = ScaleFactor(screen, reference, 0.5f);
                Vector2 canvasSize = new Vector2(screen.x / scale, screen.y / scale);

                var host = new GameObject("FitHost", typeof(RectTransform));
                try
                {
                    var hostRect = host.GetComponent<RectTransform>();
                    hostRect.sizeDelta = canvasSize;

                    var panel = Object.Instantiate(_prefab, host.transform, false);
                    var panelRect = (RectTransform)panel.transform;
                    panelRect.anchorMin = Vector2.zero;
                    panelRect.anchorMax = Vector2.one;
                    panelRect.offsetMin = Vector2.zero;
                    panelRect.offsetMax = Vector2.zero;
                    Find(panelRect, "ServerSettings").gameObject.SetActive(true);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);

                    Bounds server = BoundsOf(panelRect, Find(panelRect, "ServerSettings"));
                    Bounds confirm = BoundsOf(panelRect, Find(panelRect, "ConfirmButton"));
                    Bounds solo = BoundsOf(panelRect, Find(panelRect, "SoloButton"));
                    Bounds net = BoundsOf(panelRect, Find(panelRect, "NetButton"));
                    Bounds maleCard = BoundsOf(panelRect, Find(panelRect, "MaleCard"));
                    Bounds femaleCard = BoundsOf(panelRect, Find(panelRect, "FemaleCard"));
                    Bounds maleHigh = BoundsOf(panelRect, Find(panelRect, "MaleHighlight"));
                    Bounds femaleHigh = BoundsOf(panelRect, Find(panelRect, "FemaleHighlight"));

                    Assert.GreaterOrEqual(confirm.min.y - server.max.y, 16f - 0.01f);
                    Assert.GreaterOrEqual(solo.min.y - confirm.max.y, 16f - 0.01f);
                    Assert.GreaterOrEqual(maleHigh.min.y - solo.max.y, 16f - 0.01f);
                    Assert.GreaterOrEqual(net.min.x - solo.max.x, 60f - 0.01f,
                        "两个模式按钮横向净距至少 60");
                    Assert.GreaterOrEqual(femaleHigh.min.x - maleHigh.max.x, 40f - 0.01f,
                        "两个角色外框横向净距至少 40");

                    Rect panelBounds = panelRect.rect;
                    foreach (Bounds r in new[] { server, confirm, solo, net, maleCard, femaleCard, maleHigh, femaleHigh })
                    {
                        Assert.GreaterOrEqual(r.min.x, panelBounds.xMin - 0.01f);
                        Assert.LessOrEqual(r.max.x, panelBounds.xMax + 0.01f);
                        Assert.GreaterOrEqual(r.min.y, panelBounds.yMin - 0.01f);
                        Assert.LessOrEqual(r.max.y, panelBounds.yMax + 0.01f);
                    }

                    float soloCenterY = solo.center.y;
                    Find(panelRect, "ServerSettings").gameObject.SetActive(false);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
                    Assert.AreEqual(soloCenterY,
                        BoundsOf(panelRect, Find(panelRect, "SoloButton")).center.y, 0.01f,
                        "单机隐藏 ServerSettings 后其他控件不得跳位");
                    Find(panelRect, "ServerSettings").gameObject.SetActive(true);

                    Object.DestroyImmediate(host);
                }
                finally
                {
                    if (host != null) Object.DestroyImmediate(host);
                }

                yield return null;
            }
        }

        private static Vector2 ScreenPoint(RectTransform rect)
            => RectTransformUtility.WorldToScreenPoint(
                null, rect.TransformPoint(rect.rect.center));

        private GameObject FirstHit(Vector2 screenPoint)
        {
            var data = new PointerEventData(_eventSystem) { position = screenPoint };
            var results = new System.Collections.Generic.List<RaycastResult>();
            _canvasGo.GetComponent<GraphicRaycaster>().Raycast(data, results);
            return results.Count > 0 ? results[0].gameObject : null;
        }

        private static void Click(RectTransform rect)
        {
            var target = rect.gameObject;
            var data = new PointerEventData(EventSystem.current)
            {
                position = ScreenPoint(rect),
                button = PointerEventData.InputButton.Left,
            };
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        public IEnumerator test_character_select_inputs_and_buttons_receive_expected_pointer()
        {
            yield return null;

            string[] inputs = { "HostInput", "PortInput", "UsernameInput" };
            foreach (string name in inputs)
            {
                var rect = (RectTransform)_panel.transform.Find("ServerSettings/" + name);
                Assert.IsNotNull(rect, name);
                GameObject hit = FirstHit(ScreenPoint(rect));
                Assert.IsNotNull(hit, name + " 未被射线命中");
                Assert.IsNotNull(hit.GetComponentInParent<TMP_InputField>(),
                    name + " 首命中不是输入框");
            }

            foreach (string name in inputs)
            {
                var field = _panel.transform.Find("ServerSettings/" + name).GetComponent<TMP_InputField>();
                Assert.IsNotNull(field, name);
                Click((RectTransform)field.transform);
                yield return null;
                Assert.IsTrue(field.isFocused, name + " 点击后应可编辑");
            }

            var username = _panel.transform.Find("ServerSettings/UsernameInput").GetComponent<TMP_InputField>();
            username.text = "测试昵称";
            Assert.AreEqual("测试昵称", username.text);

            int selected = 0;
            _panelComponent.OnSelectionChanged += _ => selected++;
            Click((RectTransform)_panel.transform.Find("MaleCard"));
            yield return null;
            Assert.AreEqual(1, selected);
            Click((RectTransform)_panel.transform.Find("FemaleCard"));
            yield return null;
            Assert.AreEqual(2, selected);

            int confirmed = 0;
            void OnSelected(CharacterSelectedEvent e) => confirmed++;
            EventBus.Subscribe<CharacterSelectedEvent>(OnSelected);
            try
            {
                Click((RectTransform)_panel.transform.Find("ConfirmButton"));
                yield return null;
                Assert.AreEqual(1, confirmed, "确认按钮必须触发 CharacterSelectedEvent");
            }
            finally
            {
                EventBus.Unsubscribe<CharacterSelectedEvent>(OnSelected);
            }
        }

        [UnityTest]
        public IEnumerator test_character_select_highlights_do_not_block_card_clicks()
        {
            yield return null;

            _panel.transform.Find("MaleHighlight").gameObject.SetActive(true);
            _panel.transform.Find("FemaleHighlight").gameObject.SetActive(true);

            int selected = 0;
            _panelComponent.OnSelectionChanged += _ => selected++;

            string[] cards = { "MaleCard", "FemaleCard" };
            foreach (string name in cards)
            {
                var card = (RectTransform)_panel.transform.Find(name);
                GameObject hit = FirstHit(ScreenPoint(card));
                Assert.IsNotNull(hit, name);
                Assert.IsNotNull(hit.GetComponentInParent<Button>(),
                    name + " 高亮激活后首命中仍应是卡片按钮");

                Click(card);
                yield return null;
            }

            Assert.AreEqual(2, selected);
        }

        private static RectTransform Find(RectTransform root, string name)
        {
            var child = root.Find(name);
            Assert.IsNotNull(child, name);
            return (RectTransform)child;
        }
    }
}
