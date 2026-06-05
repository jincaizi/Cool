using UnityEditor;
using UnityEngine;

namespace Editor.VFX
{
    public static class VFXMaterialBuilder
    {
        [MenuItem("Tools/VFX/Build All VFX Materials")]
        public static void BuildAll()
        {
            EnsureFolder("Assets/Prefabs/VFX");

            var bloodMat = CreateParticleMat("Assets/Prefabs/VFX/BloodParticle.mat",
                new Color(0.8f, 0.1f, 0.1f, 1f));
            var shockwaveMat = CreateParticleMat("Assets/Prefabs/VFX/HitShockwave.mat",
                new Color(1f, 1f, 1f, 0.6f));
            var sparkMat = CreateParticleMat("Assets/Prefabs/VFX/HitSpark.mat",
                new Color(1f, 0.9f, 0.5f, 1f));
            var trailMat = CreateParticleMat("Assets/Prefabs/VFX/SlashBloodTrail.mat",
                new Color(0.7f, 0.05f, 0.05f, 1f));

            AssignMatToPrefab("Assets/Prefabs/VFX/BloodSplatterCritical.prefab", bloodMat);
            AssignMatToPrefab("Assets/Prefabs/VFX/BloodSplatterNormal.prefab", bloodMat);
            AssignMatToPrefab("Assets/Prefabs/VFX/HitShockwave.prefab", shockwaveMat);
            AssignMatToPrefab("Assets/Prefabs/VFX/HitSparkBurst.prefab", sparkMat);
            AssignMatToPrefab("Assets/Prefabs/VFX/SlashBloodTrail.prefab", trailMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VFXMaterialBuilder] All VFX materials built and assigned.");
        }

        private static Material CreateParticleMat(string path, Color color)
        {
            var mat = new Material(Shader.Find("Mobile/Particles/Additive"));
            mat.color = color;
            mat.SetColor("_EmissionColor", color);
            mat.SetInt("_BlendOp", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void AssignMatToPrefab(string prefabPath, Material mat)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[VFXMaterialBuilder] Prefab not found: " + prefabPath);
                return;
            }

            var renderers = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
            foreach (var r in renderers)
                r.sharedMaterial = mat;

            PrefabUtility.SavePrefabAsset(prefab);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            var folder = System.IO.Path.GetFileName(path);
            if (parent != null && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
