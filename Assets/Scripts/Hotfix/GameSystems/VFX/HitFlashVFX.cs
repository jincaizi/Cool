using DG.Tweening;
using DataDefinition;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class HitFlashVFX : MonoBehaviour
    {
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private Color _flashColor = Color.red;

        private MaterialPropertyBlock _propBlock;
        private Tween _flashTween;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

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
            EventBus.SubscribeTargeted<MonsterTakeDamageEvent>(gameObject.GetInstanceID(), OnMonsterDamaged);
        }

        private void OnDisable()
        {
            EventBus.UnsubscribeTargeted<MonsterTakeDamageEvent>(gameObject.GetInstanceID(), OnMonsterDamaged);
            _flashTween?.Kill();
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            TriggerFlash();
        }

        private void TriggerFlash()
        {
            if (_targetRenderer == null) return;

            var settings = GameSettings.Instance;
            var flashDuration = settings.HitFlashDuration;
            var originalColor = _targetRenderer.sharedMaterial.GetColor(ColorId);

            _flashTween?.Kill();

            _propBlock.SetColor(ColorId, _flashColor);
            _targetRenderer.SetPropertyBlock(_propBlock);

            var current = _flashColor;
            _flashTween = DOTween.To(() => current, color =>
            {
                current = color;
                if (_targetRenderer == null) return;
                _targetRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(ColorId, color);
                _targetRenderer.SetPropertyBlock(_propBlock);
            }, originalColor, flashDuration).SetTarget(_targetRenderer);
        }

        private void OnDestroy()
        {
            _flashTween?.Kill();
        }
    }
}
