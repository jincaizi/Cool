using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Hotfix.GameSystems.Sys3C;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameSys.EditorTests
{
    public class WeaponViewFitTests
    {
        private const string MaleCharacterPath = "Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab";
        private const string FemaleCharacterPath = "Assets/Prefabs/Character/FemalePlayer.prefab";
        private const string MaleDefaultSwordPath = "Assets/RpgDuo/Prefab/OHS03PBR.prefab";
        private const string FemaleDefaultSwordPath = "Assets/RpgDuo/Prefab/OHS06PBR.prefab";
        private const string WeaponViewDir = "Assets/Prefabs/Character/WeaponViews";
        private const string EvidenceDir = "production/verify/weapon-bag-sword-fit";

        private static readonly string[] ElementNames = { "Ice", "Fire", "Thunder", "Wind" };

        [Test]
        public void test_male_binds_four_calibrated_variants()
        {
            AssertCalibratedBindings(MaleCharacterPath, "Male");
        }

        [Test]
        public void test_female_binds_four_calibrated_variants()
        {
            AssertCalibratedBindings(FemaleCharacterPath, "Female");
        }

        [Test]
        public void test_missing_element_restores_default_and_hides_previous()
        {
            var root = new GameObject("WeaponViewFitTests");
            var socket = new GameObject("weapon_r");
            socket.transform.SetParent(root.transform, false);
            var defaultSword = new GameObject("DefaultSword");
            defaultSword.transform.SetParent(socket.transform, false);
            defaultSword.SetActive(true);

            var iceSword = new GameObject("IceSword");
            iceSword.transform.SetParent(root.transform, false);
            iceSword.SetActive(true);

            var entryObject = new GameObject("Entry");
            entryObject.transform.SetParent(root.transform, false);
            var entry = entryObject.AddComponent<Sys3CEntry>();
            var serialized = new SerializedObject(entry);
            serialized.FindProperty("_swordSocket").objectReferenceValue = socket.transform;
            serialized.FindProperty("_defaultSwordView").objectReferenceValue = defaultSword.transform;
            var views = serialized.FindProperty("_elementalSwordViews");
            views.arraySize = 2;
            views.GetArrayElementAtIndex(0).objectReferenceValue = iceSword;
            views.GetArrayElementAtIndex(1).objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.IsTrue(entry.ApplySwordView(1), "valid element must apply");
                Assert.IsFalse(defaultSword.activeSelf, "ice sword must hide the default view");
                var iceInstance = socket.transform.Cast<Transform>()
                    .Select(t => t.gameObject)
                    .FirstOrDefault(go => go.name.StartsWith("IceSword", StringComparison.Ordinal));
                Assert.IsNotNull(iceInstance, "ice sword must be instantiated under weapon_r");
                Assert.IsTrue(entry.ApplySwordView(1), "repeated application of an active element must succeed");

                Assert.IsFalse(entry.ApplySwordView(2), "missing element must fall back to the default sword");
                Assert.IsTrue(defaultSword.activeSelf,
                    "missing element view must restore the default sword");
                Assert.IsTrue(iceInstance == null || !iceInstance.activeSelf,
                    "previous elemental sword must be hidden immediately when fallback occurs");
                Assert.AreEqual(0, CountActiveElementalSwords(socket.transform),
                    "fallback must leave only the default sword visible");

                Assert.IsFalse(entry.ApplySwordView(0), "element 0 is a fallback, not a successful elemental application");
                Assert.IsTrue(defaultSword.activeSelf);
                Assert.AreEqual(0, CountActiveElementalSwords(socket.transform));

                Assert.IsTrue(entry.ApplySwordView(1), "valid element must apply after a fallback");
                Assert.IsFalse(defaultSword.activeSelf);
                Assert.IsFalse(entry.ApplySwordView(2), "missing element must fall back again");
                Assert.IsTrue(defaultSword.activeSelf);
                Assert.AreEqual(0, CountActiveElementalSwords(socket.transform));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void test_calibrated_variants_match_default_blade_length_ratio()
        {
            foreach (var character in new[]
                     {
                         (Name: "Male", CharacterPath: MaleCharacterPath, DefaultPath: MaleDefaultSwordPath),
                         (Name: "Female", CharacterPath: FemaleCharacterPath, DefaultPath: FemaleDefaultSwordPath)
                     })
            {
                var info = LoadDefaultSwordInfo(character.CharacterPath);
                var defaultMetrics = MeasureSword(
                    AssetDatabase.LoadAssetAtPath<GameObject>(character.DefaultPath),
                    info.Position, info.Rotation, info.Scale);
                Assert.IsNotNull(defaultMetrics, "default sword must have measurable blade geometry");
                Assert.Greater(defaultMetrics!.Extent, 0.01f, "default blade length must be positive");

                foreach (var element in ElementNames)
                {
                    string variantPath = VariantPath(character.Name, element);
                    var variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
                    Assert.IsNotNull(variant, "missing calibrated variant: " + variantPath);
                    var metrics = MeasureSword(variant, Vector3.zero, Quaternion.identity, Vector3.one);
                    Assert.IsNotNull(metrics, "variant has no mesh geometry: " + variantPath);
                    float ratio = metrics!.Extent / defaultMetrics.Extent;
                    Assert.IsTrue(ratio >= 0.9f && ratio <= 1.1f,
                        variantPath + " blade length ratio " + ratio.ToString("F3", CultureInfo.InvariantCulture)
                        + " outside [0.90, 1.10] (default=" + defaultMetrics.Extent.ToString("F3", CultureInfo.InvariantCulture)
                        + ", variant=" + metrics.Extent.ToString("F3", CultureInfo.InvariantCulture) + ")");
                }
            }
        }

        [Test]
        public void test_dump_sword_fit_measurements()
        {
            Directory.CreateDirectory(EvidenceDir);
            var lines = new List<string> { "character,element,source,bounds_min,bounds_max,extent,axis,meshes" };
            foreach (var character in new[]
                     {
                         (Name: "Male", CharacterPath: MaleCharacterPath, DefaultPath: MaleDefaultSwordPath),
                         (Name: "Female", CharacterPath: FemaleCharacterPath, DefaultPath: FemaleDefaultSwordPath)
                     })
            {
                var info = LoadDefaultSwordInfo(character.CharacterPath);
                var defaultMetrics = MeasureSword(
                    AssetDatabase.LoadAssetAtPath<GameObject>(character.DefaultPath),
                    info.Position, info.Rotation, info.Scale);
                Assert.IsNotNull(defaultMetrics, "default sword must have measurable blade geometry");
                AddLine(lines, character.Name, "Default(Base)", character.DefaultPath, defaultMetrics!);

                foreach (var element in ElementNames)
                {
                    string vendorPath = character.Name == "Male"
                        ? "Assets/HovlStudio/MoonSword/Prefabs/" + VendorName(element, true) + ".prefab"
                        : "Assets/HovlStudio/MoonSword/Prefabs/" + VendorName(element, false) + ".prefab";
                    var vendor = AssetDatabase.LoadAssetAtPath<GameObject>(vendorPath);
                    Assert.IsNotNull(vendor, "missing vendor prefab: " + vendorPath);
                    var vendorMetrics = MeasureSword(vendor, Vector3.zero, Quaternion.identity, Vector3.one);
                    Assert.IsNotNull(vendorMetrics, "vendor prefab has no mesh geometry: " + vendorPath);
                    AddLine(lines, character.Name, element + "(Raw)", vendorPath, vendorMetrics!);

                    string variantPath = VariantPath(character.Name, element);
                    var variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
                    if (variant != null)
                        AddLine(lines, character.Name, element + "(Variant)", variantPath,
                            MeasureSword(variant, Vector3.zero, Quaternion.identity, Vector3.one)!);
                }
            }

            File.WriteAllLines(Path.Combine(EvidenceDir, "measurements.csv"), lines);
            Assert.Greater(lines.Count, 1, "measurement dump must contain at least the default swords");
        }

        private static void AddLine(List<string> lines, string character, string element, string source,
            SwordMetrics metrics)
        {
            lines.Add(string.Join(",",
                character,
                element,
                source,
                Format(metrics.Min),
                Format(metrics.Max),
                metrics.Extent.ToString("F4", CultureInfo.InvariantCulture),
                metrics.Axis,
                metrics.Meshes));
        }

        private static string Format(Vector3 value)
        {
            return "(" + value.x.ToString("F4", CultureInfo.InvariantCulture)
                       + ";" + value.y.ToString("F4", CultureInfo.InvariantCulture)
                       + ";" + value.z.ToString("F4", CultureInfo.InvariantCulture) + ")";
        }

        private static void AssertCalibratedBindings(string characterPath, string role)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            Assert.IsNotNull(prefab, "missing character prefab: " + characterPath);
            var entry = prefab!.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(entry, "character prefab has no Sys3CEntry: " + characterPath);

            var serialized = new SerializedObject(entry);
            var views = serialized.FindProperty("_elementalSwordViews");
            Assert.IsNotNull(views, "Sys3CEntry has no _elementalSwordViews field");
            Assert.AreEqual(ElementNames.Length, views!.arraySize,
                role + " must bind exactly four elemental sword views");

            for (int i = 0; i < ElementNames.Length; i++)
            {
                var view = views.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                Assert.IsNotNull(view, role + " " + ElementNames[i] + " sword view is not assigned");
                string expected = VariantPath(role, ElementNames[i]);
                Assert.AreEqual(expected, AssetDatabase.GetAssetPath(view),
                    role + " " + ElementNames[i] + " sword view must be the project-owned calibrated variant");
                AssertVariantTransform(view!, expected);
            }
        }

        private static void AssertVariantTransform(GameObject variant, string path)
        {
            Assert.AreEqual(Vector3.zero, variant.transform.localPosition, path + " root must use socket-local identity position");
            Assert.AreEqual(Quaternion.identity, variant.transform.localRotation, path + " root must use socket-local identity rotation");
            Assert.AreEqual(Vector3.one, variant.transform.localScale, path + " root must use socket-local identity scale");

            Assert.AreEqual(1, variant.transform.childCount, path + " must wrap exactly one visual model");
            var model = variant.transform.GetChild(0);
            var scale = model.localScale;
            Assert.IsTrue(IsFinite(scale.x) && IsFinite(scale.y) && IsFinite(scale.z),
                path + " visual model scale must be finite");
            Assert.IsTrue(scale.x > 0f && scale.y > 0f && scale.z > 0f,
                path + " visual model scale must be positive");
            Assert.AreEqual(scale.x, scale.y, 0.0001f, path + " visual model scale must be uniform (x/y)");
            Assert.AreEqual(scale.x, scale.z, 0.0001f, path + " visual model scale must be uniform (x/z)");
        }

        private static DefaultSwordInfo LoadDefaultSwordInfo(string characterPath)
        {
            var contents = PrefabUtility.LoadPrefabContents(characterPath);
            try
            {
                var socket = contents.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "weapon_r");
                Assert.IsNotNull(socket, characterPath + " has no weapon_r socket");
                Assert.GreaterOrEqual(socket!.childCount, 1, characterPath + " weapon_r has no default sword");
                var sword = socket.GetChild(0);
                return new DefaultSwordInfo
                {
                    Position = sword.localPosition,
                    Rotation = sword.localRotation,
                    Scale = sword.localScale
                };
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static SwordMetrics? MeasureSword(GameObject? prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (prefab == null) return null;
            var socket = new GameObject("MeasurementSocket");
            var instance = Object.Instantiate(prefab, socket.transform, false);
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;
            instance.transform.localScale = scale;

            try
            {
                var points = new List<Vector3>();
                var meshes = new List<string>();
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh;
                    if (mesh == null) continue;
                    meshes.Add(filter.name + ":" + mesh.name);
                    var toSocket = socket.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    var vertices = mesh.vertices;
                    foreach (var vertex in vertices)
                        points.Add(toSocket.MultiplyPoint3x4(vertex));
                }

                if (points.Count == 0) return null;

                var min = points[0];
                var max = points[0];
                foreach (var point in points)
                {
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }

                var size = max - min;
                int axis = 0;
                if (size.y > size[axis]) axis = 1;
                if (size.z > size[axis]) axis = 2;
                return new SwordMetrics
                {
                    Min = min,
                    Max = max,
                    Extent = size[axis],
                    Axis = axis,
                    Meshes = string.Join("|", meshes)
                };
            }
            finally
            {
                Object.DestroyImmediate(socket);
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static int CountActiveElementalSwords(Transform socket)
        {
            int count = 0;
            foreach (Transform child in socket)
            {
                if (child.gameObject.activeSelf && !child.name.StartsWith("DefaultSword", StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        private static string VariantPath(string role, string element)
            => WeaponViewDir + "/" + role + element + "Sword.prefab";

        private static string VendorName(string element, bool male)
        {
            string prefix = element;
            string suffix = male ? " M" : " W";
            if (element == "Wind") return "WindSword_" + (male ? "M" : "W");
            return prefix + "Sword" + suffix;
        }

        private sealed class DefaultSwordInfo
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private sealed class SwordMetrics
        {
            public Vector3 Min;
            public Vector3 Max;
            public float Extent;
            public int Axis;
            public string Meshes = string.Empty;
        }
    }
}
