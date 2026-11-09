using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class CharacterSelectPanelLayoutTests
    {
        [Test]
        public void test_character_select_content_uses_shared_bottom_anchors()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(prefab);
            var root = Object.Instantiate(prefab);
            try
            {
                AssertBottom(root, "ServerSettings", 105f, new Vector2(640f, 132f));
                AssertBottom(root, "ConfirmButton", 222f, new Vector2(220f, 70f));
                AssertBottom(root, "SoloButton", 303f, new Vector2(200f, 60f), -130f);
                AssertBottom(root, "NetButton", 303f, new Vector2(200f, 60f), 130f);
                AssertBottom(root, "MaleCard", 499f, new Vector2(200f, 280f), -130f);
                AssertBottom(root, "FemaleCard", 499f, new Vector2(200f, 280f), 130f);
                AssertBottom(root, "MaleHighlight", 499f, new Vector2(220f, 300f), -130f);
                AssertBottom(root, "FemaleHighlight", 499f, new Vector2(220f, 300f), 130f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertBottom(GameObject root, string name, float y,
            Vector2 size, float x = 0f)
        {
            var child = root.transform.Find(name);
            Assert.IsNotNull(child, name);
            var rect = (RectTransform)child;
            Assert.AreEqual(new Vector2(0.5f, 0f), rect.anchorMin, name);
            Assert.AreEqual(new Vector2(0.5f, 0f), rect.anchorMax, name);
            Assert.AreEqual(x, rect.anchoredPosition.x, 0.01f, name);
            Assert.AreEqual(y, rect.anchoredPosition.y, 0.01f, name);
            Assert.AreEqual(size.x, rect.sizeDelta.x, 0.01f, name);
            Assert.AreEqual(size.y, rect.sizeDelta.y, 0.01f, name);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rect.pivot, name);
        }
    }
}
