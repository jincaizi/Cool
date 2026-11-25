#if UNITY_EDITOR
using System.Collections;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    public class ChargeFireTexturePresentationTests
    {
        private sealed class Owner : MonoBehaviour, IEffectTarget
        {
            public IEffectStats Stats => null;
            public IShieldSystem ShieldSystem => null;
            public IPhysicsSystem PhysicsSystem => null;
            public IStatusController StatusController => null;
            public void Heal(float amount) { }
            public void ApplyDamageOverTime(float amount) { }
        }

        [UnityTest]
        public IEnumerator GeneratedFireTexturesFollowChargingAndRelease()
        {
            var data = AssetDatabase.LoadAssetAtPath<ChargedSkillData>(
                "Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset");
            var root = new GameObject("ChargeFireAssetTest");
            root.transform.position = new Vector3(500f, 0f, 500f);
            var owner = root.AddComponent<Owner>();
            GameObject burst = null;
            try
            {
                var executor = new SkillExecutor(owner, data);
                Assert.IsTrue(executor.TryStart());
                yield return new WaitForSeconds(0.15f);
                executor.Update(Time.deltaTime);
                Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
                var gather = root.GetComponentInChildren<ParticleSystemRenderer>();
                Assert.AreEqual("ChargeFireGatherRibbon", gather.sharedMaterial.mainTexture.name);

                Assert.IsTrue(executor.Release());
                Assert.IsFalse(gather.gameObject.activeSelf);
                Vector3 releasePosition = root.transform.position;
                Vector3 ringOrigin = releasePosition + Vector3.up * 0.74f;
                foreach (var effect in Object.FindObjectsOfType<ParticleSystem>())
                {
                    var parent = effect.transform.parent;
                    if (parent == null || parent.name != "Explosion_Charge(Clone)" ||
                        parent.position != ringOrigin) continue;
                    burst = parent.gameObject;
                    break;
                }
                Assert.IsNotNull(burst);
                var ring = burst.transform.Find("TorsoRing").GetComponent<ParticleSystem>();
                Assert.AreEqual("ChargeBlastShockRing",
                    ring.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture.name);
                Assert.AreEqual(releasePosition, executor.ReportedImpactPoint);
                root.transform.position += Vector3.right;
                yield return null;
                Assert.IsTrue(ring.isPlaying);
                Assert.AreEqual(ringOrigin, ring.transform.position);

                executor = new SkillExecutor(owner, data);
                Assert.IsTrue(executor.TryStart());
                yield return new WaitForSeconds(0.15f);
                executor.Update(Time.deltaTime);
                gather = root.GetComponentInChildren<ParticleSystemRenderer>();
                Assert.IsNotNull(gather);
                executor.ForceComplete();
                Assert.IsFalse(gather.gameObject.activeSelf);
            }
            finally
            {
                if (burst != null) Object.Destroy(burst);
                Object.Destroy(root);
            }
        }
    }
}
#endif
