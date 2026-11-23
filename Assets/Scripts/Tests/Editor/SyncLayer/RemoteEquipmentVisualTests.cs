using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class RemoteEquipmentVisualTests
    {
        private const long Id = 42;

        [TestCase("Male")]
        [TestCase("Female")]
        public void EquipmentRestoresAfterProxyAndViewRebuild(string fit)
        {
            var root = new GameObject("RemoteEquipment");
            var avatar = CreateAvatar(fit);
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = avatar } });
            try
            {
                var baseline = Snapshot(1);
                baseline.FieldMask |= (ushort)EntityFieldMask.Combat;
                baseline.Combat = Projection(1, 5, 1);
                registry.ApplySnapshot(baseline, 1_000);
                var view = registry.GetView(Id);
                var entry = view.FullRoot.GetComponentInChildren<Sys3CEntry>();
                Assert.IsFalse(entry.enabled, "Remote weapon swapping must leave local 3C disabled.");
                var ice = FindSword(view, fit + "IceSword");
                Assert.IsNotNull(ice);
                Assert.AreEqual(0, ice.GetComponentsInChildren<Collider>(true).Length);

                var health = Snapshot(1);
                health.FieldMask |= (ushort)EntityFieldMask.Health;
                health.CombatVersion = 9;
                registry.ApplySnapshot(health, 2_000);
                registry.ApplyCombatStateUpdate(Update(1, 7, 2));
                Assert.IsTrue(ice == null, "Replacing the sword must retire the old instance.");
                Assert.IsNotNull(FindSword(view, fit + "FireSword"));

                registry.ApplyCombatStateUpdate(Update(1, 6, 1));
                Assert.IsNotNull(FindSword(view, fit + "FireSword"));

                Vector3 observer = new Vector3(100, 0, 0);
                registry.ObserverPositionProvider = () => observer;
                registry.UpdateRender(2_000, 0);
                Assert.AreEqual(RemoteTier.Proxy, view.Tier);
                Assert.IsNull(FindSword(view, fit + "FireSword"));

                registry.ApplyCombatStateUpdate(Update(1, 10, 3));
                Assert.IsNull(FindSword(view, fit + "ThunderSword"));
                observer = Vector3.zero;
                registry.UpdateRender(2_000, 0);
                Assert.AreEqual(RemoteTier.Full, view.Tier);
                Assert.IsNotNull(FindSword(view, fit + "ThunderSword"));

                registry.Remove(Id);
                registry.ObserverPositionProvider = null;
                registry.ApplySnapshot(Snapshot(0), 3_000);
                view = registry.GetView(Id);
                Assert.IsNotNull(FindSword(view, fit + "ThunderSword"));

                registry.ApplySnapshot(Snapshot(2), 4_000);
                registry.ApplyCombatStateUpdate(Update(1, 999, 4));
                Assert.IsNull(FindSword(view, fit + "ThunderSword"));
                Assert.IsNull(FindSword(view, fit + "WindSword"));
                Assert.AreEqual(0, view.FullRoot.GetComponentInChildren<Sys3CEntry>().CurrentElementId);
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
            }
        }

        private static GameObject CreateAvatar(string fit)
        {
            var avatar = new GameObject("Avatar");
            var socket = new GameObject("weapon_r").transform;
            socket.SetParent(avatar.transform, false);
            var original = new GameObject("OriginalSword").transform;
            original.SetParent(socket, false);
            var entry = new SerializedObject(avatar.AddComponent<Sys3CEntry>());
            entry.FindProperty("_swordSocket").objectReferenceValue = socket;
            entry.FindProperty("_defaultSwordView").objectReferenceValue = original;
            var views = entry.FindProperty("_elementalSwordViews");
            var elements = new[] { "Ice", "Fire", "Thunder", "Wind" };
            views.arraySize = elements.Length;
            for (int i = 0; i < elements.Length; i++)
                views.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Character/WeaponViews/" + fit + elements[i] + "Sword.prefab");
            entry.ApplyModifiedPropertiesWithoutUndo();
            return avatar;
        }

        private static Transform FindSword(RemoteEntityView view, string name)
            => view.FullRoot.transform.Find("weapon_r/" + name + "(Clone)");

        private static EntitySnapshot Snapshot(uint life) => new EntitySnapshot
        {
            PlayerId = Id,
            Kind = (byte)EntityKind.Player,
            LifeId = life,
            FieldMask = (ushort)EntityFieldMask.Position
        };

        private static CombatProjection Projection(uint life, uint version, int element) => new CombatProjection
        {
            LifeId = life,
            CombatVersion = version,
            Equipment = new EquipmentSummary { ElementId = element }
        };

        private static CombatStateUpdate Update(uint life, uint version, int element) => new CombatStateUpdate
        {
            EntityId = Id,
            Projection = Projection(life, version, element)
        };
    }
}
