using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [System.Serializable]
    public class PresentationBlock
    {
        [Header("=== VFX ===")]
        [Tooltip("施法阶段生成的VFX预制体")]
        [SerializeField] private GameObject _castVFX;
        public GameObject CastVFX => _castVFX;

        [Tooltip("技能释放/命中时生成的VFX预制体")]
        [SerializeField] private GameObject _releaseVFX;
        public GameObject ReleaseVFX => _releaseVFX;

        [Header("=== SFX ===")]
        [Tooltip("施法时播放的SFX")]
        [SerializeField] private AudioClip _castSFX;
        public AudioClip CastSFX => _castSFX;

        [Header("=== Hit ===")]
        [Tooltip("命中时的冻结帧持续时间(秒)")]
        [SerializeField] private float _hitStopDuration;
        public float HitStopDuration => _hitStopDuration;

        [Header("=== Casting Bar ===")]
        [Tooltip("在HUD上显示此技能的引导条?")]
        [SerializeField] private bool _showCastingBar = true;
        public bool ShowCastingBar => _showCastingBar;

        [Tooltip("HUD上引导条的颜色")]
        [SerializeField] private Color _castingBarColor = Color.blue;
        public Color CastingBarColor => _castingBarColor;
    }
}
