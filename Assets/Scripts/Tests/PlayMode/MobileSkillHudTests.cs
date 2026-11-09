using System.Collections;
using System.Linq;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KcpNet.PlayModeTests
{
    public class MobileSkillHudTests
    {
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;

        [SetUp]
        public void SavePrefsAndForceSinglePlayer()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
            NetworkModeService.Save(NetworkMode.SinglePlayer);
        }

        [TearDown]
        public void RestorePrefs()
        {
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        [UnityTest]
        public IEnumerator test_demo_day_has_five_radial_skill_buttons_and_attack_ready_view()
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("DemoDay");
            yield return null;
            yield return null;

            var skills = Object.FindObjectsOfType<MobileSkillButton>(true);
            Assert.AreEqual(5, skills.Length, "应有 Q/R/火球/炎爆/陨石五个技能按钮");
            CollectionAssert.AreEquivalent(
                new[] { (int)SkillID.SkillQ, (int)SkillID.SkillR, (int)SkillID.Fireball, (int)SkillID.ChargeBlast, (int)SkillID.Ultimate },
                skills.Select(x => x.SkillId).ToArray());

            foreach (var skill in skills)
            {
                var cooldown = skill.transform.Find("CooldownFill")?.GetComponent<Image>();
                Assert.IsNotNull(cooldown, skill.name + " 缺少冷却遮罩");
                Assert.AreEqual(Image.Type.Filled, cooldown.type);
                Assert.AreEqual(Image.FillMethod.Radial360, cooldown.fillMethod);
                Assert.IsTrue(cooldown.fillClockwise);
                Assert.IsFalse(cooldown.raycastTarget, "冷却装饰不得拦截触摸");
                Assert.IsNotNull(skill.transform.Find("Countdown"), skill.name + " 缺少数字倒计时");
            }

            var fireball = skills.First(x => x.SkillId == (int)SkillID.Fireball);
            var aim = fireball.GetComponent<MobileSkillAimJoystick>();
            Assert.IsNotNull(aim, "火球按钮必须挂 MobileSkillAimJoystick");
            Assert.IsNotNull(aim.Icon, "摇杆必须绑定 Icon");

            var attack = GameObject.Find("AttackButton");
            Assert.IsNotNull(attack);
            Assert.IsNotNull(attack.GetComponent<AttackHeavyReadyView>(), "普攻按钮必须有剑气就绪视图");
            Assert.IsNotNull(GameObject.Find("MobileInput").GetComponent<MobileSkillBarPresenter>(), "MobileInput 必须驱动技能显示");

            SceneManager.SetActiveScene(SceneManager.CreateScene("MobileSkillHudCleanup"));
            var unload = SceneManager.UnloadSceneAsync("DemoDay");
            while (unload != null && !unload.isDone) yield return null;
            LogAssert.ignoreFailingMessages = previous;
        }
    }
}
