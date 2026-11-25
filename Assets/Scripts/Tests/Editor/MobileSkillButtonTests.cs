using System.Reflection;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    public class MobileSkillButtonTests
    {
        private GameObject _root;
        private MobileSkillButton _button;
        private Image _cooldown;
        private Image _charge;
        private TMP_Text _text;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("MobileSkillButtonTests");
            _button = _root.AddComponent<MobileSkillButton>();
            _cooldown = new GameObject("Cooldown").AddComponent<Image>();
            _charge = new GameObject("Charge").AddComponent<Image>();
            _text = new GameObject("Countdown").AddComponent<TextMeshProUGUI>();
            SetField("_cooldownFill", _cooldown);
            SetField("_chargeFill", _charge);
            SetField("_countdownText", _text);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_cooldown.gameObject);
            Object.DestroyImmediate(_charge.gameObject);
            Object.DestroyImmediate(_text.gameObject);
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void test_cooldown_uses_remaining_ratio_and_rounds_up_seconds()
        {
            _button.RenderCooldown(new CooldownInfo
            {
                IsOnCooldown = true,
                Remaining = 2.01f,
                Total = 6f
            });

            Assert.AreEqual(2.01f / 6f, _cooldown.fillAmount, 0.0001f);
            Assert.AreEqual("3", _text.text);
            Assert.IsTrue(_cooldown.gameObject.activeSelf);
            Assert.IsTrue(_text.gameObject.activeSelf);
        }

        [Test]
        public void test_finished_cooldown_hides_fill_and_countdown_together()
        {
            _button.RenderCooldown(new CooldownInfo { IsOnCooldown = false });

            Assert.IsFalse(_cooldown.gameObject.activeSelf);
            Assert.IsFalse(_text.gameObject.activeSelf);
        }

        [Test]
        public void test_charge_ring_only_shows_for_positive_progress()
        {
            _button.RenderCharge(0.75f);
            Assert.IsTrue(_charge.gameObject.activeSelf);
            Assert.AreEqual(0.75f, _charge.fillAmount, 0.0001f);

            _button.RenderCharge(0f);
            Assert.IsFalse(_charge.gameObject.activeSelf);
        }

        [Test]
        public void EchoPreparation_Fills_The_Overlay_And_Hands_Back_To_Cooldown()
        {
            var prepared = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/PreRes/SkillsCfg/Textures/SkillIconSwordQiEchoPrepared.png");
            Assert.IsNotNull(prepared);
            _charge.sprite = prepared;
            _charge.type = Image.Type.Filled;
            _charge.fillMethod = Image.FillMethod.Radial360;

            _button.RenderEchoPreparation(2.2f, 5f);
            Assert.AreEqual("3", _text.text);
            Assert.AreEqual(0.44f, _charge.fillAmount, 0.001f);
            Assert.AreSame(prepared, _charge.sprite);
            Assert.IsTrue(_charge.gameObject.activeSelf);
            Assert.IsFalse(_cooldown.gameObject.activeSelf);

            _button.RenderEchoPreparation(0f, 0f);
            _button.RenderCooldown(new CooldownInfo { IsOnCooldown = true, Remaining = 12f, Total = 12f });
            Assert.AreEqual("12", _text.text);
            Assert.IsTrue(_cooldown.gameObject.activeSelf);
            Assert.IsFalse(_charge.gameObject.activeSelf);
        }

        private void SetField(string name, object value)
        {
            typeof(MobileSkillButton).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(_button, value);
        }
    }
}
