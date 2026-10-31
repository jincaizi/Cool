using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class NetworkTargetableTests
    {
        [Test]
        public void SetVitals_Fires_Hp_And_Death_Once()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            float percent = -1f; int current = -1; int max = -1; int deaths = 0;
            t.OnHPChanged += (p, c, m) => { percent = p; current = c; max = m; };
            t.OnDeath += () => deaths++;

            t.SetVitals(50f, 100f, dead: false);
            Assert.AreEqual(0.5f, percent, 0.001f);
            Assert.AreEqual(50, current);
            Assert.AreEqual(100, max);
            Assert.AreEqual(0, deaths);

            t.SetVitals(0f, 100f, dead: true);
            t.SetVitals(0f, 100f, dead: true);
            Assert.AreEqual(1, deaths, "死亡只触发一次");
        }

        [Test]
        public void Respawn_Resets_Death_Flag()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            int deaths = 0; t.OnDeath += () => deaths++;
            t.SetVitals(0f, 100f, dead: true);
            t.SetVitals(100f, 100f, dead: false);
            t.SetVitals(0f, 100f, dead: true);
            Assert.AreEqual(2, deaths, "复活后再次死亡应再次触发");
        }

        [Test]
        public void MarkRemoved_Fires_Death_And_Cached_Position_Does_Not_Touch_Transform()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            int deaths = 0; t.OnDeath += () => deaths++;
            t.SetPosition(new Vector3(1f, 0f, 2f));
            t.MarkRemoved();
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), t.WorldPosition);
        }

        [Test]
        public void HpPercent_Clamps_And_Guards_Zero_MaxHp()
        {
            var t = new NetworkTargetable(2L, "Wolf");
            Assert.AreEqual(0f, t.HPPercent);
            t.SetVitals(-5f, 100f, dead: true);
            Assert.AreEqual(0f, t.HPPercent);
            Assert.AreEqual(0, t.CurrentHP);
        }
    }
}
