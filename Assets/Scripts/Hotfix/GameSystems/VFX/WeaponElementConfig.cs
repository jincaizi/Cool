using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public enum VFXQualityLevel { High, Low }
    public enum BladeAxis { Y, Z, X }

    [CreateAssetMenu(menuName = "VFX/Weapon Element Config")]
    public class WeaponElementConfig : ScriptableObject
    {
        [Header("Blade Positioning")]
        public BladeAxis Axis = BladeAxis.Y;
        [Tooltip("从剑柄 (weapon_r) 沿剑身方向的偏移距离")]
        public float BladeOffset = 0.8f;

        [Header("Mist Particles")]
        public Texture2D ParticleTexture;
        public Color MistStartColor = new Color(0.6f, 0.8f, 1f, 1f);
        public Color MistEndColor = new Color(0.6f, 0.8f, 1f, 0f);
        [Tooltip("Particles emitted per second.")]
        public float MistEmissionRate = 15f;
        [Tooltip("Minimum particle lifespan (seconds).")]
        public float MistLifetimeMin = 1f;
        [Tooltip("Maximum particle lifespan (seconds).")]
        public float MistLifetimeMax = 2f;
        public float MistStartSizeMin = 0.05f;
        public float MistStartSizeMax = 0.15f;

        [Header("Mist Shape")]
        public float MistShapeRadius = 0.3f;
        public float MistShapeHeight = 1.5f;
        [Tooltip("Minimum orbital rotation speed (degrees/second) around the weapon.")]
        public float MistOrbitalSpeedMin = 2f;
        [Tooltip("Maximum orbital rotation speed (degrees/second) around the weapon.")]
        public float MistOrbitalSpeedMax = 5f;
        [Tooltip("Random position jitter strength for irregular mist movement.")]
        public float MistNoiseStrength = 0.3f;
        [Tooltip("How rapidly noise position changes.")]
        public float MistNoiseFrequency = 0.5f;

        [Header("Trail")]
        public Color TrailColor = new Color(0.2f, 0.5f, 1f, 1f);
        public float TrailTime = 0.15f;
        public float TrailWidth = 0.3f;
        public float TrailMinVertexDistance = 0.1f;

        [Header("Frost Shader")]
        public Color FrostColor = new Color(0.6f, 0.8f, 1f, 1f);
        public float FrostAmount = 0.5f;
        [Tooltip("UV scroll speed of frost pattern.")]
        public float FrostFlowSpeed = 0.05f;
        [Tooltip("Duration (seconds) for frost amount to lerp between states.")]
        public float FrostBlendTime = 0.3f;

        [Header("Performance")]
        public VFXQualityLevel Quality = VFXQualityLevel.High;
        public int MaxParticlesHigh = 30;
        public int MaxParticlesLow = 15;
        public float EmissionRateLow = 8f;

        public static Vector3 GetAxisVector(BladeAxis axis) => axis switch
        {
            BladeAxis.X => Vector3.right,
            BladeAxis.Y => Vector3.up,
            BladeAxis.Z => Vector3.forward,
            _ => Vector3.up
        };

        private void OnValidate()
        {
            MistLifetimeMax = Mathf.Max(MistLifetimeMin, MistLifetimeMax);
            MistStartSizeMax = Mathf.Max(MistStartSizeMin, MistStartSizeMax);
            MistOrbitalSpeedMax = Mathf.Max(MistOrbitalSpeedMin, MistOrbitalSpeedMax);
        }
    }
}
