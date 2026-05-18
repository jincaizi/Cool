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

            var settings = GameSettings.Instance;
            var flashWidth = 0.05f;
            var flashDuration = settings.HitFlashDuration;

            _flashTween?.Kill();

            _targetRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorId, settings.HitFlashColor);
            _propBlock.SetFloat(OutlineWidthId, flashWidth);
            _targetRenderer.SetPropertyBlock(_propBlock);

            var startColor = settings.HitFlashColor;
            _flashTween = DOTween.To(() => flashWidth, width =>
            {
                if (_targetRenderer == null) return;
                _targetRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(OutlineWidthId, width);
                float t = 1f - width / flashWidth;
                _propBlock.SetColor(OutlineColorId, Color.Lerp(startColor, Color.clear, t));
                _targetRenderer.SetPropertyBlock(_propBlock);
            }, 0f, flashDuration).SetTarget(_targetRenderer);
        }

        private void OnDestroy()
        {
            _flashTween?.Kill();
        }
    }
}
