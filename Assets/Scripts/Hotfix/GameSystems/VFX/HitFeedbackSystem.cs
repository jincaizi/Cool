using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 击打反馈系统：订阅双向受击事件播放命中火花。
    /// 8 实例池 round-robin，stopAction 回调自动回收，纯事件驱动无 Update/轮询。
    /// </summary>
    public class HitFeedbackSystem : MonoBehaviour
    {
        [SerializeField] private GameObject _sparkPrefab;
        [SerializeField] private int _poolSize = 8;

        public static bool EnableVFX = true;
        public static HitFeedbackSystem Instance { get; private set; }

        private HitSpark[] _sparks;
        private int _cursor;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _sparks = new HitSpark[_poolSize];
            for (int i = 0; i < _poolSize; i++)
            {
                var go = Instantiate(_sparkPrefab, transform);
                go.SetActive(false);
                _sparks[i] = go.GetComponent<HitSpark>();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterHit);
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnPlayerHit);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterHit);
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnPlayerHit);
        }

        private void OnMonsterHit(MonsterTakeDamageEvent e)
        {
            if (!e.IsLocalSource) return;   // 远程玩家打怪只飘字，不播本地命中反馈
            Spawn(e.HitPosition, e.Part, e.ElementColor, e.IsCritical, e.SkillId);
        }

        private void OnPlayerHit(PlayerTakeDamageEvent e)
        {
            Spawn(e.HitPosition, e.Part, e.ElementColor, isCritical: false, skillId: 0);
        }

        private void Spawn(Vector3 worldPos, HitZonePart part, Color color, bool isCritical, int skillId)
        {
            if (!EnableVFX) return;
            if (_sparks == null || _sparks.Length == 0) return;

            // 缩放 = 基础(1) × 部位倍率 × 暴击 ×2 × 技能 ×1.5
            float scale = HitZoneClassifier.ScaleMultiplier(part);
            if (isCritical) scale *= 2f;
            if (skillId > 0) scale *= 1.5f;

            Color c = color.a <= 0f ? Color.white : color;  // 防御：事件未带有效色时用白

            _sparks[_cursor % _sparks.Length].Play(worldPos, c, scale);
            _cursor++;
        }
    }
}
