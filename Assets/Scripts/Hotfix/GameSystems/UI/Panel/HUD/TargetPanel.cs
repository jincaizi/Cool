using Hotfix.GameSystems.Sys3C.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class TargetPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Base;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "TargetPanel";

        [SerializeField] private Image _portrait;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private Slider _hpSlider;
        [SerializeField] private TMP_Text _hpText;
        [SerializeField] private GameObject _contentRoot;

        private ITargetable _currentTarget;

        public void Bind(ITargetable target)
        {
            Clear();
            _currentTarget = target;

            if (_nameText != null) _nameText.text = target.DisplayName;
            if (_levelText != null) _levelText.text = $"Lv.{target.Level}";
            if (_portrait != null && target.Portrait != null) _portrait.sprite = target.Portrait;

            UpdateHP(target.HPPercent, target.CurrentHP, target.MaxHP);
            target.OnHPChanged += UpdateHP;
            target.OnDeath += OnTargetDeath;

            if (_contentRoot != null) _contentRoot.SetActive(true);
        }

        public void Clear()
        {
            if (_currentTarget != null)
            {
                _currentTarget.OnHPChanged -= UpdateHP;
                _currentTarget.OnDeath -= OnTargetDeath;
                _currentTarget = null;
            }

            if (_contentRoot != null) _contentRoot.SetActive(false);
        }

        private void UpdateHP(float percent, int current, int max)
        {
            if (_hpSlider != null) _hpSlider.value = percent;
            if (_hpText != null) _hpText.text = $"{current}/{max}";
        }

        private void OnTargetDeath()
        {
            Clear();
            UIManager.Instance?.HideAlwaysAsync(PanelId);
        }

        private new void OnDestroy()
        {
            Clear();
        }
    }
}
