using UnityEngine;

namespace DataDefinition
{
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Game/GameSettings")]
    public class GameSettings : ScriptableObject
    {
        private static GameSettings _instance;

        public static GameSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<GameSettings>("Setting/GameSettings");
                return _instance;
            }
        }

        [Header("Display")]
        [Tooltip("设计分辨率")]
        public Vector2 ReferenceResolution = new(1920, 1080);

        [Tooltip("目标帧率")]
        public int TargetFrameRate = 60;

        [Header("VFX")]
        [Tooltip("受击闪屏贴图")]
        public Sprite HitFlashSprite;

        [Tooltip("受击闪屏颜色")]
        public Color HitFlashColor = Color.white;

        [Tooltip("受击闪屏时长(秒)")]
        public float HitFlashDuration = 0.15f;

        [Tooltip("受击闪屏cd(秒)")]
        public float HitFlashCD = 120f;

        [Header("Combat")]
        [Tooltip("Damage fluctuation range as a fraction. 0.1 = ±10%")]
        [Range(0f, 1f)]
        public float DamageFluctuation = 0f;
    }
}
