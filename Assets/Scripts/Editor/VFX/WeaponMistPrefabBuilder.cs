using UnityEditor;
using UnityEngine;

namespace Editor.VFX
{
    public static class WeaponMistPrefabBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/VFX/WeaponMist.prefab";
        private const string MaterialPath = "Assets/Prefabs/VFX/WeaponMist.mat";

        [MenuItem("Tools/VFX/Build WeaponMist Prefab")]
        public static void Build()
        {
            var go = new GameObject("WeaponMist");

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startColor = new Color(0.6f, 0.8f, 1f, 1f);
            main.maxParticles = 30;
            main.duration = 999f;
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = 15f;

            var shape = ps.shape;
            shape.shapeType = (ParticleSystemShapeType)17; // Cylinder
            shape.radius = 0.3f;
            shape.radiusThickness = 0.3f;
            shape.arc = 360f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            shape.scale = new Vector3(1f, 0.75f, 1f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.radial = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.space = ParticleSystemSimulationSpace.Local;

            var colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.6f, 0.8f, 1f), 0f),
                        new GradientColorKey(new Color(0.6f, 0.8f, 1f), 0.3f),
                        new GradientColorKey(new Color(0.6f, 0.8f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) });
            colorLife.color = grad;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            // Save material as a standalone asset so it can be edited in Inspector
            var mat = new Material(Shader.Find("Mobile/Particles/Additive"));
            mat.SetInt("_BlendOp", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.color = Color.white;
            mat.SetColor("_EmissionColor", Color.white);
            AssetDatabase.CreateAsset(mat, MaterialPath);
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Ensure parent directory exists
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/VFX"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                    AssetDatabase.CreateFolder("Assets", "Prefabs");
                AssetDatabase.CreateFolder("Assets/Prefabs", "VFX");
            }

            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WeaponMistPrefabBuilder] Prefab saved to " + PrefabPath);
        }
    }
}
