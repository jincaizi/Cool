using System.Collections;
using Core.Resource;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    public class ElementalAddressableLifecycleTests
    {
        [UnityTest]
        public IEnumerator AllElementalVisualsLoadAndReleaseTheirAddressableReferences()
        {
            var types = new[] { EffectType.Freeze, EffectType.Burn, EffectType.Stun, EffectType.Vulnerable };
            var root = new GameObject("ElementalAddressableLifecycle");
            var visuals = new ElementVfxAttachment[8];
            var keys = new string[visuals.Length];
            try
            {
                for (int i = 0; i < types.Length; i++)
                {
                    keys[i * 2] = ElementVfxAssets.SwordQiKey(i + 1);
                    keys[i * 2 + 1] = ElementVfxAssets.StatusKey(types[i]);
                }

                for (int i = 0; i < visuals.Length; i++)
                {
                    visuals[i] = new ElementVfxAttachment();
                    visuals[i].Show(keys[i], root.transform);
                }

                float timeout = Time.realtimeSinceStartup + 12f;
                for (int i = 0; i < visuals.Length; i++)
                {
                    while (!visuals[i].IsVisible && Time.realtimeSinceStartup < timeout)
                        yield return null;
                    Assert.IsTrue(visuals[i].IsVisible, keys[i]);
                    Assert.AreEqual(1, Res.GetRefCount(keys[i]), keys[i]);
                }

                foreach (var visual in visuals)
                    visual.Clear();
                // Destroy is deferred by Unity before the corresponding prefab lease is released.
                yield return null;
                yield return null;
                Assert.AreEqual(0, root.transform.childCount);
                foreach (string key in keys)
                    Assert.AreEqual(-1, Res.GetRefCount(key), key);
            }
            finally
            {
                foreach (var visual in visuals)
                    visual?.Clear();
                Object.Destroy(root);
            }
        }
    }
}
