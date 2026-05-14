using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public enum VFXQualityLevel { High, Medium, Low }

    [CreateAssetMenu(menuName = "VFX/Weapon Element Config")]
    public class WeaponElementConfig : ScriptableObject
    {
        [Header("Mist Particles")]
        public Color MistStartColor = new Color(0.6f, 0.8f, 1f, 1f);
        public Color MistEndColor = new Color(0.6f, 0.8f, 1f, 0f);
        public float MistEmissionRate = 15f;
        public float MistLifetimeMin = 1f;
        public float MistLifetimeMax = 2f;
        public float MistStartSizeMin = 0.05f;
        public float MistStartSizeMax = 0.15f;

        [Header("Mist Shape")]
        public float MistShapeRadius = 0.3f;
        public float MistShapeHeight = 1.5f;
        public float MistOrbitalSpeedMin = 2f;
        public float MistOrbitalSpeedMax = 5f;
        public float MistNoiseStrength = 0.3f;
        public float MistNoiseFrequency = 0.5f;

        [Header("Trail")]
        public Color TrailColor = new Color(0.2f, 0.5f, 1f, 1f);
        public float TrailTime = 0.15f;
        public float TrailWidth = 0.3f;
        public float TrailMinVertexDistance = 0.1f;

        [Header("Frost Shader")]
        public Color FrostColor = new Color(0.6f, 0.8f, 1f, 1f);
        public float FrostAmount = 0.5f;
        public float FrostFlowSpeed = 0.05f;
        public float FrostBlendTime = 0.3f;

        [Header("Performance")]
        public VFXQualityLevel Quality = VFXQualityLevel.High;
        public int MaxParticlesHigh = 30;
        public int MaxParticlesLow = 15;
        public float EmissionRateLow = 8f;
    }
}
