using System.Reflection;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    public class AttackHeavyReadyViewTests
    {
        [Test]
        public void test_heavy_ready_switches_icon_to_gold_and_shows_border()
        {
            var root = new GameObject("AttackHeavyReadyViewTests");
            var icon = new GameObject("Icon").AddComponent<Image>();
            var border = new GameObject("GoldBorder");
            var view = root.AddComponent<AttackHeavyReadyView>();
            SetField(view, "_icon", icon);
            SetField(view, "_goldBorder", border);
            SetField(view, "_defaultColor", Color.white);
            SetField(view, "_readyColor", new Color(1f, 0.72f, 0.18f, 1f));

            view.SetReady(true);
            Assert.AreEqual(new Color(1f, 0.72f, 0.18f, 1f), icon.color);
            Assert.IsTrue(border.activeSelf);

            view.SetReady(false);
            Assert.AreEqual(Color.white, icon.color);
            Assert.IsFalse(border.activeSelf);

            Object.DestroyImmediate(icon.gameObject);
            Object.DestroyImmediate(border);
            Object.DestroyImmediate(root);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }
}
