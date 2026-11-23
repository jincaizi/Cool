using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cool.EditorTools;
using Core.Resource;
using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameSys.EditorTests
{
    public class ElementVfxAttachmentTests
    {
        [TestCase(1, EffectType.Freeze, "ice")]
        [TestCase(2, EffectType.Burn, "fire")]
        [TestCase(3, EffectType.Stun, "thunder")]
        [TestCase(4, EffectType.Vulnerable, "wind")]
        public void ShippedKeysRegisterVisualOnlyPrefabs(int element, EffectType effect, string name)
        {
            ElementalVfxRegistration.Register();
            var group = AddressableAssetSettingsDefaultObject.Settings.FindGroup(ElementalVfxRegistration.GroupName);
            var projectile = group.entries.Single(e => e.address == ElementVfxAssets.SwordQiKey(element));
            var status = group.entries.Single(e => e.address == ElementVfxAssets.StatusKey(effect));
            Assert.AreEqual("swordqi/" + name, projectile.address);
            Assert.AreEqual("status/" + name, status.address);

            var projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(projectile.AssetPath);
            var wave = projectilePrefab.GetComponent<MeshRenderer>();
            Assert.AreEqual("Cool/VFX/Sword Qi Energy", wave.sharedMaterial.shader.name);
            Assert.AreEqual(element, wave.sharedMaterial.GetFloat("_Profile"));
            Assert.IsTrue(wave.sharedMaterial.HasProperty("_WaveFlight"));
            Assert.AreEqual(1, projectilePrefab.GetComponentsInChildren<Renderer>(true).Length);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Prefabs/VFX/SwordQiWave.asset");
            Assert.IsNotNull(mesh);
            Assert.AreSame(mesh, projectilePrefab.GetComponent<MeshFilter>().sharedMesh);
            Assert.IsEmpty(projectilePrefab.GetComponentsInChildren<MonoBehaviour>(true));
            Assert.IsEmpty(projectilePrefab.GetComponentsInChildren<Collider>(true));

            var statusPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(status.AssetPath);
            var statusRenderer = statusPrefab.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(statusRenderer);
            Assert.AreEqual("Cool/VFX/Skill Texture", statusRenderer.sharedMaterial.shader.name);
            Assert.AreEqual(element switch
            {
                1 => "ElementFrostFootRing",
                2 => "ElementBurnLoop",
                3 => "ElementThunderArc",
                _ => "ElementWindRibbons"
            }, statusRenderer.sharedMaterial.mainTexture.name);
            if (effect == EffectType.Burn)
                Assert.AreEqual(8, statusPrefab.GetComponentInChildren<ParticleSystem>().textureSheetAnimation.spriteCount);
            Assert.IsEmpty(statusPrefab.GetComponentsInChildren<MonoBehaviour>(true));
            Assert.IsEmpty(statusPrefab.GetComponentsInChildren<Collider>(true));
        }

        [Test]
        public void ReusedOwnerRejectsLateVisualAndRestoresFallbacksOnClear()
        {
            var ice = new UniTaskCompletionSource<GameObject>();
            var fire = new UniTaskCompletionSource<GameObject>();
            var released = new List<string>();
            CancellationToken iceToken = default;
            var attachment = new ElementVfxAttachment((key, token) =>
            {
                if (key != "ice") return fire.Task;
                iceToken = token;
                return ice.Task;
            }, released.Add);
            var owner = new GameObject("PooledOwner");
            var icePrefab = new GameObject("Ice");
            var firePrefab = new GameObject("Fire");
            var fallback = owner.AddComponent<LineRenderer>();
            try
            {
                attachment.Show("ice", owner.transform, new Renderer[] { fallback });
                Assert.IsTrue(fallback.enabled);
                attachment.Clear();
                Assert.IsTrue(iceToken.IsCancellationRequested);
                GameObject prepared = null;
                attachment.Show("fire", owner.transform, new Renderer[] { fallback }, instance =>
                {
                    Assert.IsTrue(fallback.enabled, "Initialize the incoming visual before hiding the fallback");
                    prepared = instance;
                });
                fire.TrySetResult(firePrefab);
                ice.TrySetResult(icePrefab);

                Assert.IsTrue(attachment.IsVisible);
                Assert.AreSame(owner.transform.GetChild(0).gameObject, prepared);
                Assert.IsFalse(fallback.enabled);
                Assert.AreEqual(1, owner.transform.childCount);
                Assert.AreEqual("Fire(Clone)", owner.transform.GetChild(0).name);
                CollectionAssert.AreEqual(new[] { "ice" }, released);

                attachment.Clear();
                attachment.Clear();
                Assert.IsTrue(fallback.enabled);
                Assert.AreEqual(0, owner.transform.childCount);
                CollectionAssert.AreEqual(new[] { "ice", "fire" }, released);
            }
            finally
            {
                attachment.Clear();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(icePrefab);
                Object.DestroyImmediate(firePrefab);
            }
        }

        [Test]
        public void MissingAddressLeavesFallbackActive()
        {
            var owner = new GameObject("FallbackOwner");
            var fallback = owner.AddComponent<LineRenderer>();
            var attachment = new ElementVfxAttachment((key, _) => UniTask.FromException<GameObject>(
                new ResourceLoadException(key, new InvalidOperationException())), _ => Assert.Fail("No lease was acquired."));
            try
            {
                attachment.Show("missing", owner.transform, new Renderer[] { fallback });
                Assert.IsTrue(fallback.enabled);
                Assert.IsFalse(attachment.IsVisible);
                Assert.AreEqual(0, owner.transform.childCount);
            }
            finally
            {
                attachment.Clear();
                Object.DestroyImmediate(owner);
            }
        }
    }
}
