using DG.Tweening;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class HitFlashVFX : MonoBehaviour
    {
        [SerializeField] private float _flashWidth = 0.05f;
        [SerializeField] private float _flashDuration = 0.15f;
        [SerializeField] private Color _flashStartColor = Color.white;
        [SerializeField] private Color _flashEndColor = Color.red;
        [SerializeField] private Renderer _targetRenderer;

        private MaterialPropertyBlock _propBlock;
        private Tween _flashTween;
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            if (_targetRenderer == null)
                _targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_targetRenderer == null)
                _targetRenderer = GetComponentInChildren<MeshRenderer>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            _flashTween?.Kill();
        }

        private void OnPlayerDamaged(DamageEvent e)
        {
            TriggerFlash();
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            TriggerFlash();
        }

        private void TriggerFlash()
        {
            if (_targetRenderer == null) return;

            _flashTween?.Kill();

            _targetRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorId, _flashStartColor);
            _propBlock.SetFloat(OutlineWidthId, _flashWidth);
            _targetRenderer.SetPropertyBlock(_propBlock);

            _flashTween = DOTween.To(() => _flashWidth, width =>
            {
                if (_targetRenderer == null) return;
                _targetRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(OutlineWidthId, width);
                float t = 1f - width / _flashWidth;
                _propBlock.SetColor(OutlineColorId, Color.Lerp(_flashStartColor, _flashEndColor, t));
                _targetRenderer.SetPropertyBlock(_propBlock);
            }, 0f, _flashDuration).SetTarget(_targetRenderer);
        }

        private void OnDestroy()
        {
            _flashTween?.Kill();
        }
    }
}
