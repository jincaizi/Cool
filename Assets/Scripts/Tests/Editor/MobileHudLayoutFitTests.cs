using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MobileHudLayoutFitTests
    {
        [Test]
        public void test_mobile_hud_and_character_select_fit_phone_aspects_at_115_scale()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(prefab);
            var root = (RectTransform)prefab.transform;

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
                Vector2 canvas = new Vector2(screen.x / scale, screen.y / scale);

                Rect server = RectOf(root, "ServerSettings", canvas);
                Rect confirm = RectOf(root, "ConfirmButton", canvas);
                Rect solo = RectOf(root, "SoloButton", canvas);
                Rect net = RectOf(root, "NetButton", canvas);
                Rect maleCard = RectOf(root, "MaleCard", canvas);
                Rect femaleCard = RectOf(root, "FemaleCard", canvas);
                Rect maleHigh = RectOf(root, "MaleHighlight", canvas);
                Rect femaleHigh = RectOf(root, "FemaleHighlight", canvas);

                Assert.GreaterOrEqual(confirm.yMin - server.yMax, 16f - 0.01f);
                Assert.GreaterOrEqual(solo.yMin - confirm.yMax, 16f - 0.01f);
                Assert.GreaterOrEqual(maleHigh.yMin - solo.yMax, 16f - 0.01f);
                Assert.GreaterOrEqual(net.xMin - solo.xMax, 60f - 0.01f,
                    "两个模式按钮横向净距至少 60");
                Assert.GreaterOrEqual(femaleHigh.xMin - maleHigh.xMax, 40f - 0.01f,
                    "两个角色外框横向净距至少 40");

                foreach (Rect r in new[]
                {
                    server, confirm, solo, net, maleCard, femaleCard, maleHigh, femaleHigh,
                })
                {
                    Assert.GreaterOrEqual(r.xMin, 0f);
                    Assert.LessOrEqual(r.xMax, canvas.x);
                    Assert.GreaterOrEqual(r.yMin, 0f);
                    Assert.LessOrEqual(r.yMax, canvas.y);
                }
            }
        }

        private static float ScaleFactor(Vector2 screen, Vector2 reference, float match)
        {
            float logWidth = Mathf.Log(screen.x / reference.x, 2f);
            float logHeight = Mathf.Log(screen.y / reference.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, match));
        }

        private static Rect RectOf(RectTransform root, string name, Vector2 canvas)
        {
            var child = (RectTransform)root.Find(name);
            Assert.IsNotNull(child, name);
            Vector2 anchor = child.anchorMin;
            Vector2 center = new Vector2(
                canvas.x * anchor.x + child.anchoredPosition.x,
                canvas.y * anchor.y + child.anchoredPosition.y);
            return new Rect(center - child.sizeDelta * 0.5f, child.sizeDelta);
        }
    }
}
