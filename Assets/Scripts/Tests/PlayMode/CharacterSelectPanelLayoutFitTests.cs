using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KcpNet.PlayModeTests
{
    public class CharacterSelectPanelLayoutFitTests
    {
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(_prefab);
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

        private static RectTransform Find(RectTransform root, string name)
        {
            var child = root.Find(name);
            Assert.IsNotNull(child, name);
            return (RectTransform)child;
        }
    }
}
