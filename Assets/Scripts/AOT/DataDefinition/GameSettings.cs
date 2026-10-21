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

        [Header("Player Feedback")]
        [Tooltip("低血量阈值（0-1）")]
        [Range(0f, 1f)] public float LowHpThreshold = 0.2f;

        [Tooltip("低血量身体染色（alpha = 混合强度）")]
        public Color LowHpTintColor = new Color(0.75f, 0.05f, 0.05f, 0.45f);

        [Tooltip("受击身体泛红颜色（alpha = 混合强度）")]
        public Color HitTintColor = new Color(1f, 0.1f, 0.1f, 0.7f);

        [Tooltip("受击泛红持续时间（秒）")]
        public float HitTintDuration = 1f;

        [Tooltip("被玩家攻击时的屏幕闪屏颜色")]
        public Color PlayerHitFlashColor = new Color(0.9f, 0.05f, 0.05f, 1f);

        [Tooltip("屏幕闪屏冷却（秒）")]
        public float PlayerHitFlashCooldown = 0.8f;

        [Tooltip("回血绿色粒子预制体")]
        public GameObject HealVfxPrefab;

        [Header("Combat")]
        [Tooltip("Damage fluctuation range as a fraction. 0.1 = ±10%")]
        [Range(0f, 1f)]
        public float DamageFluctuation = 0f;
    }
}
