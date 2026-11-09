using DataDefinition;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class UIScaleResolverTests
    {
        [Test]
        public void test_platform_scale_webgl_and_mobile_is_115()
        {
            Assert.AreEqual(1.15f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1.15f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.Android, true, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1.15f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.IPhonePlayer, true, 1.15f, 1f), 0.0001f);
        }

        [Test]
        public void test_platform_scale_desktop_and_editor_is_1()
        {
            Assert.AreEqual(1f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.WindowsPlayer, false, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.OSXPlayer, false, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.WindowsEditor, false, 1.15f, 1f), 0.0001f);
        }

        [Test]
        public void test_global_scale_multiplies_and_clamps()
        {
            Assert.AreEqual(1.38f, GameSettings.ResolveEffectiveUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f, 1.2f), 0.0001f);
            Assert.AreEqual(1.5f, GameSettings.ResolveEffectiveUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f, 2f), 0.0001f);
            Assert.AreEqual(1.15f, GameSettings.ResolveEffectiveUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f, 0f), 0.0001f,
                "global 0 回退为 1.0 后仍乘平台倍率 1.15");
        }

        [Test]
        public void test_reference_resolution_divides_by_effective_scale()
        {
            Vector2 result = GameSettings.ScaleReferenceResolution(
                new Vector2(1920f, 1080f), 1.15f);

            Assert.AreEqual(1669.5652f, result.x, 0.01f);
            Assert.AreEqual(939.1304f, result.y, 0.01f);
        }

        [Test]
        public void test_game_settings_asset_has_ui_scale_defaults()
        {
            var settings = Resources.Load<GameSettings>("Setting/GameSettings");
            Assert.IsNotNull(settings);
            Assert.AreEqual(1f, settings.UIScale, 0.0001f);
            Assert.AreEqual(1.15f, settings.MobilePlatformUIScale, 0.0001f);
            Assert.AreEqual(1f, settings.DesktopPlatformUIScale, 0.0001f);
        }
    }
}
