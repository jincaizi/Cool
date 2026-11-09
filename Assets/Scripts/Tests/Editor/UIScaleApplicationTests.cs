using DataDefinition;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    public class UIScaleApplicationTests
    {
        [Test]
        public void test_screen_adapter_applies_resolved_reference_resolution()
        {
            var go = new GameObject("UIScaleApplicationTests");
            try
            {
                var scaler = go.AddComponent<CanvasScaler>();
                ScreenAdapter.ApplyReferenceResolution(scaler, GameSettings.Instance);

                Vector2 expected = GameSettings.Instance.ResolveReferenceResolution();
                Assert.AreEqual(expected.x, scaler.referenceResolution.x, 0.01f);
                Assert.AreEqual(expected.y, scaler.referenceResolution.y, 0.01f);
                Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
                Assert.AreEqual(0.5f, scaler.matchWidthOrHeight, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
