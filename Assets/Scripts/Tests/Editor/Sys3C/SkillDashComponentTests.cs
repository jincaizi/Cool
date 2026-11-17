using Hotfix.GameSystems.Sys3C.Skill;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.Sys3C
{
    public class SkillDashComponentTests
    {
        [Test]
        public void DashSpeed_Uses_Shared_Startup_And_Recovery()
        {
            var component = new SkillDashComponent(null, null);
            component.StartDash(Vector3.forward, distance: 2f, duration: 0.3f,
                startupSeconds: 0.05f, recoverySeconds: 0.05f);

            Assert.AreEqual(10f, component.DashSpeed, 0.0001f);
        }

        [Test]
        public void DashSpeed_Applies_Status_Scale()
        {
            var component = new SkillDashComponent(null, null) { StatusSpeedScaleProvider = () => 0.7f };
            component.StartDash(Vector3.forward, 2f, 0.3f, 0.05f, 0.05f);

            Assert.AreEqual(7f, component.CurrentFrameSpeed(1f), 0.0001f);
        }
    }
}
